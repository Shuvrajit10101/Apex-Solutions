using Apex.Ledger.Domain;

namespace Apex.Ledger.Reports;

/// <summary>One row of a ledger book (design §7.6) — a posting with the running balance after it.</summary>
public sealed record LedgerBookRow(
    DateOnly Date,
    string VoucherTypeName,
    int Number,
    string? CounterParticulars,
    Money Debit,
    Money Credit,
    DrCr RunningSide,
    Money RunningAmount);

/// <summary>
/// A single ledger's book (design §7.6): opening balance, then every posting to that
/// ledger in date order with a running balance after each. The final running balance
/// equals the ledger's closing balance. Cash Book / Bank Book are this projection
/// filtered to a Cash-in-Hand / Bank-Accounts ledger.
/// </summary>
public sealed record LedgerBook(
    string LedgerName,
    DrCr OpeningSide,
    Money OpeningAmount,
    IReadOnlyList<LedgerBookRow> Rows,
    DrCr ClosingSide,
    Money ClosingAmount)
{
    public static LedgerBook Build(Company company, Guid ledgerId, DateOnly from, DateOnly to)
    {
        var ledger = company.FindLedger(ledgerId)
            ?? throw new InvalidOperationException($"Ledger {ledgerId} not found.");

        var openingSide = ledger.OpeningIsDebit ? DrCr.Debit : DrCr.Credit;

        // Running balance starts from the signed opening (opening always counts, regardless of range).
        var running = ledger.SignedOpening;

        // Collect this ledger's postings that count as-of, ordered by (date, number).
        var contributing = new List<(Voucher Voucher, EntryLine Line)>();
        foreach (var v in company.Vouchers)
        {
            if (!LedgerBalances.CountsAsOf(v, to)) continue;
            foreach (var line in v.Lines)
                if (line.LedgerId == ledgerId)
                    contributing.Add((v, line));
        }

        contributing.Sort((a, b) =>
        {
            var byDate = a.Voucher.Date.CompareTo(b.Voucher.Date);
            return byDate != 0 ? byDate : a.Voucher.Number.CompareTo(b.Voucher.Number);
        });

        var rows = new List<LedgerBookRow>();
        foreach (var (v, line) in contributing)
        {
            running += line.Signed;

            // Only emit rows within the requested display window; the running balance still
            // accumulates from opening across everything up to each row.
            if (v.Date < from || v.Date > to) continue;

            var type = company.FindVoucherType(v.TypeId);
            var counter = CounterParty(company, v, ledgerId);
            var runBal = LedgerBalance.FromSigned(running);

            rows.Add(new LedgerBookRow(
                v.Date,
                type?.Name ?? "(unknown)",
                v.Number,
                counter,
                line.Side == DrCr.Debit ? line.Amount : Money.Zero,
                line.Side == DrCr.Credit ? line.Amount : Money.Zero,
                runBal.Side,
                runBal.Amount));
        }

        var closing = LedgerBalances.Closing(company, ledger, to);

        return new LedgerBook(
            ledger.Name,
            openingSide,
            ledger.OpeningBalance,
            rows,
            closing.Side,
            closing.Amount);
    }

    /// <summary>The single other-side ledger name (or "(multiple)") for a voucher's counter particulars.</summary>
    private static string? CounterParty(Company company, Voucher v, Guid selfLedgerId)
    {
        var others = v.Lines
            .Where(l => l.LedgerId != selfLedgerId)
            .Select(l => company.FindLedger(l.LedgerId)?.Name ?? "(unknown)")
            .Distinct()
            .ToList();

        return others.Count switch
        {
            0 => null,
            1 => others[0],
            _ => "(multiple)",
        };
    }
}
