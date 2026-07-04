using System.Globalization;
using Apex.Ledger.Domain;
using Apex.Ledger.Reports;

namespace Apex.Ledger.Banking;

/// <summary>
/// One parsed row of an imported bank statement (catalog §8 statement auto-import). A minimal shape:
/// the value <see cref="Date"/> the bank posted the entry, a free-text <see cref="Description"/>, the
/// signed <see cref="Amount"/> (positive = credit into the account, negative = debit out), and an
/// optional <see cref="InstrumentNumber"/> (cheque/UTR reference) used for matching.
/// </summary>
public sealed record BankStatementRow(
    DateOnly Date,
    string Description,
    Money Amount,
    string InstrumentNumber)
{
    /// <summary>The signed statement movement (bank's view): + when money came in, − when it went out.</summary>
    public decimal Signed => Amount.Amount;

    /// <summary>Magnitude of the movement, always ≥ 0.</summary>
    public Money Magnitude => new(Math.Abs(Amount.Amount));
}

/// <summary>
/// The outcome of matching a statement to the ledger's unreconciled bank transactions (catalog §8):
/// the transactions whose Bank Date was set (with the statement row that matched), plus the statement
/// rows and book transactions that could not be matched.
/// </summary>
public sealed record BankStatementMatchResult(
    IReadOnlyList<(BankTransactionRow Transaction, BankStatementRow StatementRow)> Matched,
    IReadOnlyList<BankStatementRow> UnmatchedStatementRows,
    IReadOnlyList<BankTransactionRow> UnmatchedBookTransactions)
{
    /// <summary>Count of transactions reconciled by this import.</summary>
    public int MatchedCount => Matched.Count;
}

/// <summary>
/// Parses a simple bank statement (CSV text/stream) and auto-matches its rows to a bank ledger's
/// unreconciled book transactions, setting the Bank Date on each match (catalog §8; plan.md §5, §8).
/// Kept entirely in the framework-agnostic core: the parser takes a <see cref="string"/> or
/// <see cref="TextReader"/> — there is <b>no file IO</b> here (the caller reads the file and passes text).
/// </summary>
public static class BankStatementImport
{
    /// <summary>
    /// Parses CSV statement text into rows. Format (header optional, case-insensitive):
    /// <c>date, description, amount[, instrument]</c>. Date is ISO <c>yyyy-MM-dd</c>; amount is a signed
    /// decimal (a credit is positive, a debit negative). Blank lines are skipped. A leading line whose
    /// first field is not a date is treated as a header and ignored. Throws <see cref="FormatException"/>
    /// on a malformed data row.
    /// </summary>
    public static IReadOnlyList<BankStatementRow> ParseCsv(string csv)
    {
        ArgumentNullException.ThrowIfNull(csv);
        using var reader = new StringReader(csv);
        return ParseCsv(reader);
    }

    /// <summary>Streaming overload of <see cref="ParseCsv(string)"/> — reads line by line, no file IO.</summary>
    public static IReadOnlyList<BankStatementRow> ParseCsv(TextReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        var rows = new List<BankStatementRow>();

        string? line;
        var lineNo = 0;
        while ((line = reader.ReadLine()) is not null)
        {
            lineNo++;
            if (string.IsNullOrWhiteSpace(line)) continue;

            var fields = SplitCsvLine(line);
            if (fields.Count < 3)
                throw new FormatException($"Statement line {lineNo} has fewer than 3 fields: '{line}'.");

            // A first line whose first field is not a date is a header — skip it once (only line 1).
            if (rows.Count == 0 && !TryParseDate(fields[0], out _))
            {
                if (lineNo == 1) continue;
                throw new FormatException($"Statement line {lineNo} has an unparseable date: '{fields[0]}'.");
            }

            if (!TryParseDate(fields[0], out var date))
                throw new FormatException($"Statement line {lineNo} has an unparseable date: '{fields[0]}'.");

            var description = fields[1].Trim();

            if (!decimal.TryParse(fields[2].Trim(), NumberStyles.Number, CultureInfo.InvariantCulture, out var amount))
                throw new FormatException($"Statement line {lineNo} has an unparseable amount: '{fields[2]}'.");

            var instrument = fields.Count >= 4 ? fields[3].Trim() : string.Empty;

            rows.Add(new BankStatementRow(date, description, new Money(amount), instrument));
        }

        return rows;
    }

    /// <summary>
    /// Auto-matches parsed statement rows against the bank ledger's unreconciled book transactions and
    /// <b>sets the Bank Date</b> on each match (mutating the matched transaction's bank allocation to the
    /// statement row's date). A statement row matches a book transaction when their <b>signed amounts are
    /// equal</b> (same magnitude and direction) AND either their instrument numbers agree (both non-empty
    /// and equal) OR — when no instrument is available on either side — their dates are within
    /// <paramref name="dateToleranceDays"/> of each other. Each book transaction and each statement row
    /// is used at most once (first-fit, statement order). Returns the matched pairs plus the unmatched
    /// rows/transactions.
    /// </summary>
    public static BankStatementMatchResult MatchAndReconcile(
        Company company,
        Domain.Ledger bankLedger,
        DateOnly asOf,
        IReadOnlyList<BankStatementRow> statementRows,
        int dateToleranceDays = 3)
    {
        ArgumentNullException.ThrowIfNull(company);
        ArgumentNullException.ThrowIfNull(bankLedger);
        ArgumentNullException.ThrowIfNull(statementRows);
        if (dateToleranceDays < 0)
            throw new ArgumentException("Date tolerance must be ≥ 0.", nameof(dateToleranceDays));

        // Candidate book transactions: this ledger's UNRECONCILED transactions as of the date that
        // actually carry a bank allocation (only those can have their Bank Date set).
        var candidates = BankReconciliation.Transactions(company, bankLedger, asOf)
            .Where(t => !t.IsReconciled)
            .ToList();
        var consumed = new bool[candidates.Count];

        var matched = new List<(BankTransactionRow, BankStatementRow)>();
        var unmatchedRows = new List<BankStatementRow>();

        foreach (var row in statementRows)
        {
            var idx = FindMatch(candidates, consumed, row, dateToleranceDays);
            if (idx < 0)
            {
                unmatchedRows.Add(row);
                continue;
            }

            consumed[idx] = true;
            var tx = candidates[idx];
            // Reconcile: stamp the statement's date as the Bank Date on the book transaction.
            BankReconciliation.SetBankDate(company, tx.VoucherId, bankLedger.Id, row.Date);
            matched.Add((tx with { BankDate = row.Date }, row));
        }

        var unmatchedBook = new List<BankTransactionRow>();
        for (var i = 0; i < candidates.Count; i++)
            if (!consumed[i])
                unmatchedBook.Add(candidates[i]);

        return new BankStatementMatchResult(matched, unmatchedRows, unmatchedBook);
    }

    private static int FindMatch(
        IReadOnlyList<BankTransactionRow> candidates,
        bool[] consumed,
        BankStatementRow row,
        int dateToleranceDays)
    {
        for (var i = 0; i < candidates.Count; i++)
        {
            if (consumed[i]) continue;
            var tx = candidates[i];

            // Signed amounts must be equal (same magnitude AND direction).
            if (tx.Signed != row.Signed) continue;

            var bothHaveInstrument =
                !string.IsNullOrWhiteSpace(tx.InstrumentNumber) &&
                !string.IsNullOrWhiteSpace(row.InstrumentNumber);

            if (bothHaveInstrument)
            {
                if (string.Equals(tx.InstrumentNumber, row.InstrumentNumber, StringComparison.OrdinalIgnoreCase))
                    return i;
                // Instrument numbers present but different → not this one; keep scanning.
                continue;
            }

            // No instrument to key on → fall back to a nearby date within tolerance.
            var dayGap = Math.Abs(tx.Date.DayNumber - row.Date.DayNumber);
            if (dayGap <= dateToleranceDays)
                return i;
        }
        return -1;
    }

    private static bool TryParseDate(string s, out DateOnly date) =>
        DateOnly.TryParseExact(s.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date);

    /// <summary>
    /// Minimal CSV field splitter: comma-separated, supports double-quoted fields (with <c>""</c> escapes)
    /// so a description may contain commas. Good enough for the simple statement format (no embedded CRLF).
    /// </summary>
    private static List<string> SplitCsvLine(string line)
    {
        var fields = new List<string>();
        var sb = new System.Text.StringBuilder();
        var inQuotes = false;

        for (var i = 0; i < line.Length; i++)
        {
            var ch = line[i];
            if (inQuotes)
            {
                if (ch == '"')
                {
                    if (i + 1 < line.Length && line[i + 1] == '"') { sb.Append('"'); i++; }
                    else inQuotes = false;
                }
                else sb.Append(ch);
            }
            else
            {
                if (ch == '"') inQuotes = true;
                else if (ch == ',') { fields.Add(sb.ToString()); sb.Clear(); }
                else sb.Append(ch);
            }
        }
        fields.Add(sb.ToString());
        return fields;
    }
}
