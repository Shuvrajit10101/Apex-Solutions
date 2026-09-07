using Apex.Ledger.Domain;

namespace Apex.Ledger.Reports;

/// <summary>
/// The six status buckets of the <b>Cheque Register</b> (census row 8.5).
///
/// <para><b>Vendor grounding.</b> <c>help.tallysolutions.com/cheque-register/</c>, "View Cheques with Specific
/// Statuses", and <c>help.tallysolutions.com/docs/te9rel65/Banking/Cheque_Register.htm</c> for the
/// <i>Out of Period</i> bucket. Available = <i>"still unused and can be issued"</i>; Unreconciled =
/// <i>"transactions that are not complete"</i>; Reconciled = matched against the bank statement; Blank =
/// <i>"physical inventory of the cheques"</i>; Cancelled = <i>"voided due to errors"</i>.</para>
///
/// <para><b>🔴 THREE OF THESE ARE STORED AND THREE ARE COMPUTED, AND THE SPLIT IS THE WHOLE DESIGN.</b> Only
/// <see cref="ChequeStatus"/>'s Available / Blank / Cancelled are ever persisted. Reconciled, Unreconciled and
/// OutOfPeriod are derived here on every projection from the posted bank allocations and the report period — the
/// moment a Bank Date is keyed on a voucher, a stored copy would be stale and the register would lie.</para>
/// </summary>
public enum ChequeRegisterStatus
{
    /// <summary>Unused and issuable: no posted cheque carries this leaf number and no operator status says otherwise.</summary>
    Available = 0,

    /// <summary>Issued, but the bank statement has not matched it yet (<c>BankAllocation.BankDate</c> is null).</summary>
    Unreconciled = 1,

    /// <summary>Issued and matched against the bank statement.</summary>
    Reconciled = 2,

    /// <summary>Held in the physical inventory but set aside by the operator.</summary>
    Blank = 3,

    /// <summary>Voided by the operator.</summary>
    Cancelled = 4,

    /// <summary>Issued, but on a voucher dated outside the report period — so it is neither of the two
    /// reconciliation buckets, which are only meaningful inside the period being looked at.</summary>
    OutOfPeriod = 5,
}

/// <summary>One leaf on the Cheque Register's detail view.</summary>
/// <param name="ChequeBookId">The book this leaf belongs to, or <see cref="Guid.Empty"/> for a "not in range" row.</param>
/// <param name="ChequeBookName">That book's name, or a caption for the not-in-range overflow.</param>
/// <param name="BankLedgerId">The bank ledger the book is held against.</param>
/// <param name="BankName">That ledger's name.</param>
/// <param name="ChequeNumber">The leaf number, exactly as printed / as keyed.</param>
/// <param name="Status">The bucket this leaf falls in.</param>
/// <param name="VoucherId">The paying voucher when the leaf has been issued, else <c>null</c> — this is the drill.</param>
/// <param name="VoucherDate">That voucher's date, or <c>null</c>.</param>
/// <param name="FormattedNumber">That voucher's formatted number, or blank.</param>
/// <param name="FavouringName">Who the cheque was drawn in favour of, or blank.</param>
/// <param name="Amount">The cheque amount, or <see cref="Money.Zero"/> for an unissued leaf.</param>
/// <param name="Printed">Whether the leaf has been marked printed.</param>
public sealed record ChequeRegisterRow(
    Guid ChequeBookId,
    string ChequeBookName,
    Guid BankLedgerId,
    string BankName,
    string ChequeNumber,
    ChequeRegisterStatus Status,
    Guid? VoucherId,
    DateOnly? VoucherDate,
    string FormattedNumber,
    string FavouringName,
    Money Amount,
    bool Printed);

/// <summary>One cheque book on the Cheque Register's summary view: the six bucket counts.</summary>
/// <param name="ChequeBookId">The book.</param>
/// <param name="ChequeBookName">Its name.</param>
/// <param name="BankLedgerId">The bank ledger it is held against.</param>
/// <param name="BankName">That ledger's name.</param>
/// <param name="FromNumber">The first leaf number.</param>
/// <param name="ToNumber">The last leaf number.</param>
/// <param name="Total">Leaves in the range (the vendor's auto-calculated "Number of Cheques").</param>
/// <param name="Available">Count in the Available bucket.</param>
/// <param name="Unreconciled">Count in the Unreconciled bucket.</param>
/// <param name="Reconciled">Count in the Reconciled bucket.</param>
/// <param name="Blank">Count in the Blank bucket.</param>
/// <param name="Cancelled">Count in the Cancelled bucket.</param>
/// <param name="OutOfPeriod">Count in the Out-of-Period bucket.</param>
public sealed record ChequeRegisterSummaryRow(
    Guid ChequeBookId,
    string ChequeBookName,
    Guid BankLedgerId,
    string BankName,
    string FromNumber,
    string ToNumber,
    int Total,
    int Available,
    int Unreconciled,
    int Reconciled,
    int Blank,
    int Cancelled,
    int OutOfPeriod);

/// <summary>
/// The pure projection behind the <b>Cheque Register</b> (census row 8.5) — the report that answers "which of the
/// leaves in my cheque books are still issuable, which are out with somebody, which have cleared, and which did I
/// spoil".
///
/// <para><b>Vendor grounding.</b> <c>help.tallysolutions.com/docs/te9rel65/Banking/Cheque_Register.htm</c>:
/// <i>"Gateway of Tally &gt; Banking &gt; Cheque Register"</i>. <c>help.tallysolutions.com/cheque-register/</c>
/// gives the status set, the per-bank scope ("View Cheques with Status for Specific Banks"), the range scope
/// ("View Cheques Based on Range") and the drill ("View More Details in Cheque Register"), and names the case of
/// <i>cheques which do not belong to any cheque range</i> — reported here as its own <b>Not in Range</b> section
/// rather than silently dropped, because a cheque you actually wrote that this report cannot see is worse than an
/// untidy report.</para>
///
/// <para><b>🔴 WHY THIS CANNOT BE DERIVED FROM THE BOOKS ALONE.</b> Reconciled and Unreconciled are derivable —
/// they are <c>BankAllocation.BankDate</c> set or not. Available, Blank and Cancelled are <b>not</b>, at any
/// price: they are facts about the paper in the drawer, and no posting anywhere records that the bank issued you
/// leaves 000101–000200 or that you tore one up. That is the entire justification for the schema this row
/// needed.</para>
///
/// <para>Pure: no UI, no DB, no clock, no RNG.</para>
/// </summary>
public static class ChequeRegister
{
    /// <summary>The caption used for cheques that fall in no cheque book's range.</summary>
    public const string NotInRangeCaption = "Cheques not in any cheque range";

    /// <summary>
    /// Every leaf of every cheque book (optionally narrowed to one bank ledger), bucketed, plus a
    /// <b>Not in Range</b> section for posted cheques whose instrument number falls in no book.
    ///
    /// <para><paramref name="period"/> decides the Out-of-Period bucket only: a leaf issued on a voucher dated
    /// outside it is reported as OutOfPeriod rather than as (Un)Reconciled, because those two buckets are
    /// statements about the period being looked at.</para>
    /// </summary>
    public static IReadOnlyList<ChequeRegisterRow> Build(
        Company company,
        PeriodRange period,
        Guid? bankLedgerId = null,
        IReadOnlyCollection<ChequeRegisterStatus>? statusFilter = null)
    {
        ArgumentNullException.ThrowIfNull(company);

        var issued = IssuedCheques(company);
        var rows = new List<ChequeRegisterRow>();
        var claimed = new HashSet<(Guid Bank, string Number)>();

        foreach (var book in company.ChequeBooks)
        {
            if (bankLedgerId is { } wanted && book.LedgerId != wanted) continue;
            if (company.FindLedger(book.LedgerId) is not { } bank) continue;

            foreach (var leaf in book.LeafNumbers())
            {
                var stored = company.FindChequeStatus(book.Id, leaf);
                issued.TryGetValue((book.LedgerId, leaf), out var use);
                if (use is not null) claimed.Add((book.LedgerId, leaf));

                rows.Add(new ChequeRegisterRow(
                    book.Id,
                    book.Name,
                    bank.Id,
                    bank.Name,
                    leaf,
                    Bucket(use, stored, period),
                    use?.VoucherId,
                    use?.Date,
                    use?.FormattedNumber ?? string.Empty,
                    use?.FavouringName ?? string.Empty,
                    use?.Amount ?? Money.Zero,
                    stored?.Printed ?? false));
            }
        }

        // The vendor's "cheques which do not belong to any cheque range". A posted cheque with no book is a real
        // payment the operator made; it gets its own section so the register never quietly loses one.
        foreach (var ((ledgerId, number), use) in issued)
        {
            if (claimed.Contains((ledgerId, number))) continue;
            if (bankLedgerId is { } wanted && ledgerId != wanted) continue;
            if (company.FindLedger(ledgerId) is not { } bank) continue;

            rows.Add(new ChequeRegisterRow(
                Guid.Empty,
                NotInRangeCaption,
                bank.Id,
                bank.Name,
                number,
                Bucket(use, stored: null, period),
                use.VoucherId,
                use.Date,
                use.FormattedNumber,
                use.FavouringName,
                use.Amount,
                Printed: false));
        }

        rows.Sort(CompareRows);

        return statusFilter is { Count: > 0 }
            ? rows.Where(r => statusFilter.Contains(r.Status)).ToList()
            : rows;
    }

    /// <summary>One row per cheque book, carrying the six bucket counts — the register's summary view, which
    /// drills to <see cref="Build"/>. Books with an uncountable range report a Total of 0 and no leaves, which is
    /// the honest reading of a range whose ends cannot be enumerated.</summary>
    public static IReadOnlyList<ChequeRegisterSummaryRow> Summary(
        Company company, PeriodRange period, Guid? bankLedgerId = null)
    {
        ArgumentNullException.ThrowIfNull(company);

        var detail = Build(company, period, bankLedgerId);
        var summary = new List<ChequeRegisterSummaryRow>();

        foreach (var book in company.ChequeBooks)
        {
            if (bankLedgerId is { } wanted && book.LedgerId != wanted) continue;
            if (company.FindLedger(book.LedgerId) is not { } bank) continue;

            var leaves = detail.Where(r => r.ChequeBookId == book.Id).ToList();
            summary.Add(new ChequeRegisterSummaryRow(
                book.Id,
                book.Name,
                bank.Id,
                bank.Name,
                book.FromNumber,
                book.ToNumber,
                book.Count,
                leaves.Count(r => r.Status == ChequeRegisterStatus.Available),
                leaves.Count(r => r.Status == ChequeRegisterStatus.Unreconciled),
                leaves.Count(r => r.Status == ChequeRegisterStatus.Reconciled),
                leaves.Count(r => r.Status == ChequeRegisterStatus.Blank),
                leaves.Count(r => r.Status == ChequeRegisterStatus.Cancelled),
                leaves.Count(r => r.Status == ChequeRegisterStatus.OutOfPeriod)));
        }

        summary.Sort((a, b) =>
        {
            var byBank = string.Compare(a.BankName, b.BankName, StringComparison.OrdinalIgnoreCase);
            return byBank != 0
                ? byBank
                : string.Compare(a.ChequeBookName, b.ChequeBookName, StringComparison.OrdinalIgnoreCase);
        });
        return summary;
    }

    /// <summary>
    /// 🔴 The bucketing rule, written once so the summary and the detail can never disagree.
    ///
    /// <para><b>An ISSUED leaf beats a stored status, deliberately.</b> If a cheque has actually been paid out,
    /// calling it "Blank" because somebody once ticked it in the inventory would be a register that contradicts
    /// the books. The operator statuses only ever describe leaves nobody has spent.</para>
    /// </summary>
    private static ChequeRegisterStatus Bucket(IssuedCheque? use, ChequeStatusOverride? stored, PeriodRange period)
    {
        if (use is not null)
        {
            if (use.Date < period.From || use.Date > period.To) return ChequeRegisterStatus.OutOfPeriod;
            return use.Reconciled ? ChequeRegisterStatus.Reconciled : ChequeRegisterStatus.Unreconciled;
        }

        return stored?.Status switch
        {
            ChequeStatus.Blank => ChequeRegisterStatus.Blank,
            ChequeStatus.Cancelled => ChequeRegisterStatus.Cancelled,
            _ => ChequeRegisterStatus.Available,
        };
    }

    /// <summary>Bank, then book, then leaf number — numerically when the leaves are all digits, so 000009 sorts
    /// before 000010 rather than after it.</summary>
    private static int CompareRows(ChequeRegisterRow a, ChequeRegisterRow b)
    {
        var byBank = string.Compare(a.BankName, b.BankName, StringComparison.OrdinalIgnoreCase);
        if (byBank != 0) return byBank;
        var byBook = string.Compare(a.ChequeBookName, b.ChequeBookName, StringComparison.OrdinalIgnoreCase);
        if (byBook != 0) return byBook;
        if (a.ChequeNumber.Length != b.ChequeNumber.Length)
            return a.ChequeNumber.Length.CompareTo(b.ChequeNumber.Length);
        return string.CompareOrdinal(a.ChequeNumber, b.ChequeNumber);
    }

    /// <summary>A posted cheque, keyed by (bank ledger, instrument number).</summary>
    private sealed record IssuedCheque(
        Guid VoucherId, DateOnly Date, string FormattedNumber, string FavouringName, Money Amount, bool Reconciled);

    /// <summary>
    /// Every posted, book-affecting bank line that spends a cheque, keyed by (bank ledger, instrument number).
    ///
    /// <para>The four conditions are the same four the Cheque Printing report keeps, and each matters: the base
    /// type is passed to <c>LedgerBalances.CountsAsOf</c> so Memorandum and Reversing-Journal vouchers — which
    /// never reach the books — cannot report a leaf as spent; the line must be a CREDIT to the bank, because a
    /// cheque you issue is money leaving and a receipt is somebody else's cheque; the allocation must be
    /// Cheque/DD; and the instrument number must be non-blank, because a leaf with no number cannot be matched to
    /// a range.</para>
    ///
    /// <para>🔴 Deliberately NOT gated on <c>EnableChequePrinting</c>, unlike the Cheque Printing report. A leaf
    /// out of your cheque book is spent whether or not you asked this product to ink it, and hiding it would
    /// report a spent cheque as still Available — the one error in this report that costs real money.</para>
    /// </summary>
    private static Dictionary<(Guid Bank, string Number), IssuedCheque> IssuedCheques(Company company)
    {
        var map = new Dictionary<(Guid, string), IssuedCheque>();

        foreach (var v in company.Vouchers)
        {
            // 🔴 The as-of is the voucher's OWN date, not the report period's end, and that is deliberate: a
            // POST-DATED cheque is physically out of your cheque book the day you write it, whatever date it
            // bears. Passing the period end here would report a post-dated leaf as still Available and let a
            // second cheque be written on it. (Cancelled, Optional, Memorandum and Reversing-Journal vouchers
            // are still excluded — those never reach the books at all.)
            if (!LedgerBalances.CountsAsOf(v, v.Date, company.FindVoucherType(v.TypeId)?.BaseType)) continue;

            foreach (var line in v.Lines)
            {
                if (line.BankAllocation is not { } alloc) continue;
                if (alloc.TransactionType != BankTransactionType.ChequeOrDD) continue;
                if (line.Side != DrCr.Credit) continue;
                var number = (alloc.InstrumentNumber ?? string.Empty).Trim();
                if (number.Length == 0) continue;
                if (company.FindLedger(line.LedgerId) is not { } bank) continue;

                var key = (bank.Id, number);
                // First writer wins, and the loop walks the vouchers in book order — so a duplicated cheque number
                // reports against the earliest voucher rather than whichever happened to be enumerated last.
                if (map.ContainsKey(key)) continue;
                map[key] = new IssuedCheque(
                    v.Id,
                    v.Date,
                    company.FormatVoucherNumber(v),
                    ChequePrinting.FavouringName(company, v, bank.Id),
                    line.Amount,
                    alloc.IsReconciled);
            }
        }
        return map;
    }
}
