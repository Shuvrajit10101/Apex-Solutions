using Apex.Ledger.Domain;
using Apex.Ledger.Reports;

namespace Apex.Ledger.Services;

/// <summary>
/// Which book (or books) a split produces — the vendor's own three options, quoted on
/// <see cref="CompanySplit"/>.
/// </summary>
public enum CompanySplitMode
{
    /// <summary>"To create a new company with data from the first date of entry in the company till one day
    /// before the split date".</summary>
    BeforeSplitDate,

    /// <summary>"To create a new company with data from the split date to the last entry date".</summary>
    FromSplitDate,

    /// <summary>Both of the above, in one run.</summary>
    IntoTwoCompanies,
}

/// <summary>
/// <b>Split Company Data (census row 16.5)</b> — the pure engine behind
/// <c>Alt+Y (Data) &gt; Split &gt; Split Data</c>.
///
/// <para><b>Vendor grounding (R7, ruling 14).</b> <c>help.tallysolutions.com/split-company-data-tally/</c>:
/// <i>"Press <b>Alt+Y</b> (Data) &gt; <b>Split</b> &gt; <b>Split Data</b>"</i>; the three options —
/// <i>"From Split Date … a new company with data from the split date to the last entry date"</i>,
/// <i>"Before Split Date … from the first date of entry in the company till one day before the split date"</i>,
/// and <i>"Into Two Companies"</i>; the precondition <i>"it is recommended to verify your data and resolve the
/// errors after data verification"</i>; and the guarantee this whole class is built around —
/// <i>"After splitting your company, the original company will remain as it is."</i></para>
///
/// <para>🔴 <b>THE ONE RULE THIS FILE EXISTS TO KEEP: A SPLIT NEVER TOUCHES THE SOURCE BOOK.</b> Every method
/// here MUTATES the aggregate it is handed, and is documented to. The caller's contract is therefore absolute:
/// <b>hand it an independent copy, never the open company</b>. In the shell that copy comes from re-loading the
/// source <c>.db</c> (<c>CompanySplitService</c>), which is the only construction in this codebase that
/// guarantees two aggregates share no object; in tests it comes from loading the fixture twice.
/// <c>CompanySplitPreservesOriginalTests</c> is the standing proof.</para>
///
/// <para><b>THE DATE BOUNDARY, from the vendor's own ranges.</b> The before-book runs to <i>"one day before the
/// split date"</i> and the from-book runs <i>"from the split date"</i>, so a voucher dated <b>exactly on</b> the
/// split date belongs to the <b>from</b>-book. That is not a preference; it is the only reading under which the
/// two quoted ranges partition the book without overlap or gap.</para>
///
/// <para><b>THE CORRECTNESS PROPERTY — the from-book's opening balances are the before-book's closings.</b>
/// The vendor's split pages do not spell the carry out, and this engine deliberately does <b>not</b> invent one:
/// it performs the double-entry close-the-books transfer and nothing else.
/// <list type="number">
/// <item>Every <b>balance-sheet</b> ledger's opening becomes its own closing balance as at the day before the
///   split date. So its closing at any later date is unchanged — the from-book reports exactly what the
///   unsplit book reports.</item>
/// <item>Every <b>Income/Expense</b> ledger opens at <b>zero</b>, because a new book must not report last
///   period's revenue as this period's. Their combined balance is transferred, in one algebraic move, onto the
///   predefined <b>Profit &amp; Loss A/c</b> ledger's opening — which is a Liability-nature head
///   (<c>SeedLedgers.BuildProfitAndLossHead</c>), so it shows on the Balance Sheet as accumulated profit
///   brought forward.</item>
/// </list>
/// 🔴 <b>The transfer is computed from the ledger balances themselves and NOT from any report.</b> Σ over all
/// ledgers of the signed closing is zero by double entry; zeroing the P&amp;L ledgers removes exactly their sum,
/// and adding that same sum to the P&amp;L A/c restores it. The from-book's opening trial balance is therefore
/// balanced <i>by arithmetic</i>, under every closing-stock basis and every scenario — where a
/// <c>ProfitAndLoss.Build</c> result would have baked one basis in and left the other one out of balance.</para>
///
/// <para><b>WHAT THIS BUILD REFUSES TO SPLIT, AND WHY REFUSING IS THE FEATURE.</b> A split rewrites a whole
/// book; a half-carried one silently misstates every figure in it. <see cref="Check"/> therefore refuses,
/// naming the reason, rather than producing a book whose numbers cannot be trusted — see the refusal list
/// there. Nothing is written until every refusal is clear.</para>
/// </summary>
public static class CompanySplit
{
    /// <summary>The earliest dated entry in the book (accounting or inventory), or null when it has none.</summary>
    public static DateOnly? FirstEntryDate(Company company)
    {
        ArgumentNullException.ThrowIfNull(company);
        DateOnly? first = null;
        foreach (var v in company.Vouchers)
            if (first is null || v.Date < first) first = v.Date;
        foreach (var v in company.InventoryVouchers)
            if (first is null || v.Date < first) first = v.Date;
        return first;
    }

    /// <summary>The latest dated entry in the book (accounting or inventory), or null when it has none.</summary>
    public static DateOnly? LastEntryDate(Company company)
    {
        ArgumentNullException.ThrowIfNull(company);
        DateOnly? last = null;
        foreach (var v in company.Vouchers)
            if (last is null || v.Date > last) last = v.Date;
        foreach (var v in company.InventoryVouchers)
            if (last is null || v.Date > last) last = v.Date;
        return last;
    }

    /// <summary>
    /// Every reason this book cannot be split at this date, in the order they would be shown. An empty list
    /// means the split may proceed; the shell must show these and write nothing while any of them stands.
    ///
    /// <para>The refusals fall in three families:</para>
    /// <list type="bullet">
    /// <item><b>Period</b> — the chosen date would produce an empty book, or predates the books.</item>
    /// <item><b>Carry</b> — the book holds state this build cannot re-home into a new book without either
    ///   losing it or fabricating it: bill-wise references still open at the split date (a ledger has no
    ///   opening bill-reference breakdown to carry them into), payroll attendance (its entries span a date
    ///   RANGE, so no partition is unambiguous), and the voucher-linked statutory/e-document records, whose
    ///   rows point at voucher ids that the split would leave in the other book.</item>
    /// <item><b>Precision</b> — a carried opening stock rate that is not paisa-exact, which the paisa store
    ///   would silently round and so change the value of the stock the new book opens with.</item>
    /// </list>
    /// </summary>
    public static IReadOnlyList<string> Check(Company source, DateOnly splitDate, CompanySplitMode mode)
    {
        ArgumentNullException.ThrowIfNull(source);
        var reasons = new List<string>();
        reasons.AddRange(CheckPeriod(source, splitDate, mode));
        reasons.AddRange(CheckCarryable(source, splitDate));
        return reasons;
    }

    /// <summary>The period refusals alone (see <see cref="Check"/>).</summary>
    public static IReadOnlyList<string> CheckPeriod(Company source, DateOnly splitDate, CompanySplitMode mode)
    {
        ArgumentNullException.ThrowIfNull(source);
        var reasons = new List<string>();

        var first = FirstEntryDate(source);
        var last = LastEntryDate(source);
        if (first is null || last is null)
        {
            reasons.Add($"'{source.Name}' has no entries, so there is nothing to split.");
            return reasons;
        }

        if (splitDate <= source.BooksBeginFrom)
            reasons.Add(
                $"The split date {Show(splitDate)} is on or before the date the books begin " +
                $"({Show(source.BooksBeginFrom)}). Choose a date inside the book.");

        var needsBefore = mode is CompanySplitMode.BeforeSplitDate or CompanySplitMode.IntoTwoCompanies;
        var needsFrom = mode is CompanySplitMode.FromSplitDate or CompanySplitMode.IntoTwoCompanies;

        if (needsBefore && first.Value >= splitDate)
            reasons.Add(
                $"There are no entries before {Show(splitDate)} — the first entry is dated " +
                $"{Show(first.Value)} — so the 'before split date' company would be empty.");

        if (needsFrom && last.Value < splitDate)
            reasons.Add(
                $"There are no entries on or after {Show(splitDate)} — the last entry is dated " +
                $"{Show(last.Value)} — so the 'from split date' company would be empty.");

        return reasons;
    }

    /// <summary>The carry + precision refusals alone (see <see cref="Check"/>). Independent of the mode:
    /// either book would inherit the same dangling rows.</summary>
    public static IReadOnlyList<string> CheckCarryable(Company source, DateOnly splitDate)
    {
        ArgumentNullException.ThrowIfNull(source);
        var reasons = new List<string>();
        var cutoff = splitDate.AddDays(-1);

        // ---- Bill-wise references still open at the split date.
        // A carried opening balance is a single magnitude and a side; there is nowhere on a Ledger to record
        // WHICH bills make it up. Carrying the balance and dropping the references would leave the new book's
        // party ledger showing money owed and its Outstandings report showing nothing owed — two figures that
        // contradict each other, in the report an operator chases money with.
        var openBills = new List<string>();
        foreach (var ledger in source.Ledgers)
        {
            if (!ledger.MaintainBillByBill) continue;
            foreach (var bill in Outstandings.OpenBillsFor(source, ledger, cutoff))
                openBills.Add($"{ledger.Name} / {bill.Reference}");
        }
        if (openBills.Count > 0)
            reasons.Add(
                $"{openBills.Count} bill-wise reference(s) are still open at {Show(cutoff)} " +
                $"({string.Join(", ", openBills.Take(5))}{(openBills.Count > 5 ? ", …" : "")}). " +
                "This build carries an opening balance as one figure per ledger and has no opening bill-wise " +
                "breakdown to carry the references into, so the split is refused rather than producing a book " +
                "whose party balances and Outstandings disagree.");

        // ---- Records that point at a voucher the split would leave in the other book.
        AddIfAny(reasons, source.TdsChallans.Count, "TDS challans");
        AddIfAny(reasons, source.ChallanVoucherLinks.Count, "TDS challan-to-voucher links");
        AddIfAny(reasons, source.TcsChallans.Count, "TCS challans");
        AddIfAny(reasons, source.TcsChallanVoucherLinks.Count, "TCS challan-to-voucher links");
        AddIfAny(reasons, source.RcmDocuments.Count, "reverse-charge documents");
        AddIfAny(reasons, source.EInvoiceRecords.Count, "e-invoice records");
        AddIfAny(reasons, source.EWayBillRecords.Count, "e-Way Bill records");
        AddIfAny(reasons, source.CreditDebitNoteLinks.Count, "credit/debit-note links");
        AddIfAny(reasons, source.AdvanceReceipts.Count, "GST advance receipts");
        AddIfAny(reasons, source.Gstr2bSnapshots.Count, "GSTR-2B snapshots");
        AddIfAny(reasons, source.Gstr2bReconResults.Count, "GSTR-2B reconciliation results");
        AddIfAny(reasons, source.ImsActions.Count, "IMS actions");
        AddIfAny(reasons, source.GstSetoffLines.Count, "GST set-off lines");
        AddIfAny(reasons, source.ItcReversals.Count, "ITC reversals");
        AddIfAny(reasons, source.GstChallans.Count, "GST challans");
        AddIfAny(reasons, source.GstDrc03s.Count, "GST DRC-03 payments");
        AddIfAny(reasons, source.AttendanceEntries.Count, "payroll attendance entries");

        // ---- Opening stock that cannot be carried without changing its value.
        foreach (var row in GodownSummary.Build(source, cutoff).Rows)
        {
            if (row.ClosingQuantity <= 0m) continue;
            var rate = new Money(row.ClosingValue.Amount / row.ClosingQuantity);
            if (!rate.IsPaisaExact)
                reasons.Add(
                    $"The closing stock of '{row.ItemName}' at '{row.GodownName}' " +
                    $"({row.ClosingQuantity} × {row.ClosingValue.Amount}) does not divide into a paisa-exact " +
                    "unit rate, so carrying it as opening stock would change its value. The split is refused.");
        }

        return reasons;
    }

    /// <summary>
    /// Shapes <paramref name="book"/> — <b>which must be an independent copy of the source</b> — into the
    /// <b>before split date</b> company: <i>"data from the first date of entry in the company till one day
    /// before the split date"</i>. Every entry dated on or after <paramref name="splitDate"/> is dropped;
    /// nothing else changes, because this book keeps the original's own opening balances and period.
    /// <b>Mutates <paramref name="book"/> in place</b> and throws <see cref="InvalidOperationException"/>
    /// (before touching anything) if any refusal in <see cref="Check"/> stands.
    /// </summary>
    public static void ShapeAsBeforeBook(Company book, DateOnly splitDate, string newName)
    {
        ArgumentNullException.ThrowIfNull(book);
        Guard(book, splitDate, CompanySplitMode.BeforeSplitDate);

        foreach (var v in book.Vouchers.Where(v => v.Date >= splitDate).ToList())
            book.RemoveVoucher(v);
        foreach (var v in book.InventoryVouchers.Where(v => v.Date >= splitDate).ToList())
            book.RemoveInventoryVoucher(v);

        book.Name = Rename(newName);
    }

    /// <summary>
    /// Shapes <paramref name="book"/> — <b>which must be an independent copy of the source</b> — into the
    /// <b>from split date</b> company: <i>"data from the split date to the last entry date"</i>, opening with
    /// the closing balances of the day before.
    ///
    /// <para>Order matters and is deliberate: every carried figure is measured <b>while the book is still
    /// whole</b> (ledger closings, then opening stock), and only then are the earlier entries dropped and the
    /// measurements applied. Measuring after the deletion would measure a book that has already lost the
    /// period being carried.</para>
    ///
    /// <b>Mutates <paramref name="book"/> in place</b> and throws <see cref="InvalidOperationException"/>
    /// (before touching anything) if any refusal in <see cref="Check"/> stands.
    /// </summary>
    public static void ShapeAsFromBook(Company book, DateOnly splitDate, string newName)
    {
        ArgumentNullException.ThrowIfNull(book);
        Guard(book, splitDate, CompanySplitMode.FromSplitDate);

        var cutoff = splitDate.AddDays(-1);

        // ---- 1. MEASURE, while the book is still whole.
        var carried = new Dictionary<Guid, decimal>();      // balance-sheet ledger id → signed opening
        var profitAndLossTransfer = 0m;                     // Σ signed of the Income/Expense ledgers
        Domain.Ledger? profitAndLossLedger = null;

        foreach (var ledger in book.Ledgers)
        {
            var signed = LedgerBalances.SignedClosing(book, ledger, cutoff);
            if (ClassificationRules.IsProfitAndLossLedger(ledger, book))
            {
                profitAndLossTransfer += signed;
                carried[ledger.Id] = 0m;                    // a new book does not open with last period's revenue
            }
            else
            {
                carried[ledger.Id] = signed;
                if (profitAndLossLedger is null
                    && string.Equals(ledger.Name, Seed.SeedLedgers.ProfitAndLossName, StringComparison.OrdinalIgnoreCase))
                    profitAndLossLedger = ledger;
            }
        }

        if (profitAndLossTransfer != 0m)
        {
            if (profitAndLossLedger is null)
                throw new InvalidOperationException(
                    $"'{book.Name}' has no '{Seed.SeedLedgers.ProfitAndLossName}' ledger, so the accumulated " +
                    "profit of the period before the split has nowhere to be carried to. The split is refused " +
                    "rather than dropping it.");

            // The one algebraic move: what the P&L ledgers stop carrying, the P&L A/c starts carrying.
            carried[profitAndLossLedger.Id] += profitAndLossTransfer;
        }

        var openingStock = new List<(Guid ItemId, Guid GodownId, decimal Quantity, Money Rate)>();
        foreach (var row in GodownSummary.Build(book, cutoff).Rows)
        {
            if (row.ClosingQuantity <= 0m) continue;
            var rate = new Money(row.ClosingValue.Amount / row.ClosingQuantity);
            if (!rate.IsPaisaExact)
                throw new InvalidOperationException(
                    $"The closing stock of '{row.ItemName}' at '{row.GodownName}' has no paisa-exact unit rate.");
            openingStock.Add((row.StockItemId, row.GodownId, row.ClosingQuantity, rate));
        }

        // ---- 2. DROP the period that belongs to the other book.
        foreach (var v in book.Vouchers.Where(v => v.Date < splitDate).ToList())
            book.RemoveVoucher(v);
        foreach (var v in book.InventoryVouchers.Where(v => v.Date < splitDate).ToList())
            book.RemoveInventoryVoucher(v);

        // ---- 3. APPLY the measurements.
        foreach (var ledger in book.Ledgers)
        {
            var signed = carried.TryGetValue(ledger.Id, out var s) ? s : 0m;
            ledger.OpeningBalance = new Money(Math.Abs(signed));
            ledger.OpeningIsDebit = signed >= 0m;
        }

        foreach (var existing in book.StockOpeningBalances.ToList())
            book.RemoveStockOpeningBalance(existing);
        foreach (var (itemId, godownId, quantity, rate) in openingStock)
            book.AddStockOpeningBalance(new StockOpeningBalance(Guid.NewGuid(), itemId, godownId, quantity, rate));

        // ---- 4. The new book's period. The vendor's split is documented around financial-year boundaries and
        // says nothing about these two fields, so the smallest honest rule is used and labelled as ours in
        // docs/invented-vs-cloned.md: the new book begins on the split date.
        book.FinancialYearStart = splitDate;
        book.BooksBeginFrom = splitDate;

        book.Name = Rename(newName);
    }

    private static void Guard(Company book, DateOnly splitDate, CompanySplitMode mode)
    {
        var reasons = Check(book, splitDate, mode);
        if (reasons.Count > 0)
            throw new InvalidOperationException(
                "This book cannot be split: " + string.Join(" ", reasons));
    }

    private static string Rename(string newName)
    {
        var trimmed = (newName ?? string.Empty).Trim();
        if (trimmed.Length == 0)
            throw new ArgumentException("A name for the new company is required.", nameof(newName));
        return trimmed;
    }

    private static void AddIfAny(List<string> reasons, int count, string what)
    {
        if (count == 0) return;
        reasons.Add(
            $"The book holds {count} {what}, whose rows point at individual vouchers. A split would leave " +
            "those vouchers in the other company, so the split is refused rather than carrying rows that " +
            "reference entries the new book does not contain.");
    }

    /// <summary>Culture-invariant date rendering — a refusal message must read the same on every CI leg.</summary>
    private static string Show(DateOnly date) => date.ToString("dd-MMM-yyyy", System.Globalization.CultureInfo.InvariantCulture);
}
