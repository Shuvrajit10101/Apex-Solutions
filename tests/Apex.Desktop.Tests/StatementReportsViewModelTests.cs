using System;
using System.IO;
using System.Linq;
using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Reports;
using Apex.Desktop.Services;
using Apex.Desktop.ViewModels;

namespace Apex.Desktop.Tests;

/// <summary>
/// UI-side coverage for RQ-5 (part 1) — the three Statements reports (Cash Flow, Funds Flow, Ratio Analysis)
/// wired into <see cref="ReportsViewModel"/> and nested under the Reports → Statements cascade in
/// <see cref="MainWindowViewModel"/>. The engine projections are trusted (covered by the engine
/// <c>StatementReportsTests</c>); these tests pin the UI wiring: each report opens, builds non-empty rows on
/// the Robert demo, reconciles a headline figure the engine guarantees, and is reachable through the menu.
/// </summary>
public sealed class StatementReportsViewModelTests : IDisposable
{
    private readonly string _tempDir;
    private readonly CompanyStorage _storage;

    public StatementReportsViewModelTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "ApexStatementsTests_" + Guid.NewGuid().ToString("N"));
        _storage = new CompanyStorage(_tempDir);
    }

    private static Company Robert() => DemoData.BuildRobert("Robert " + Guid.NewGuid().ToString("N"));

    // =============================================================== ReportsViewModel: rows built + reconcile

    [Fact]
    public void CashFlow_opens_as_an_accounting_report_with_non_empty_rows()
    {
        var vm = new ReportsViewModel(Robert(), ReportKind.CashFlow);

        Assert.Equal(ReportKind.CashFlow, vm.Kind);
        Assert.Equal("Cash Flow", vm.Title);
        Assert.True(vm.IsAccountingReport);        // renders through the Particulars/Amount grid
        Assert.True(vm.ShowSingleAccountingGrid);
        Assert.False(vm.IsInventoryReport);
        Assert.False(vm.IsGstReport);
        Assert.NotEmpty(vm.Rows);
        // The statement always carries its opening/closing/net footer lines.
        Assert.Contains(vm.Rows, r => r.Particulars == "Opening Balance");
        Assert.Contains(vm.Rows, r => r.Particulars == "Net Cash Flow");
        Assert.Contains(vm.Rows, r => r.Particulars == "Closing Balance");
    }

    [Fact]
    public void CashFlow_rows_reconcile_opening_plus_net_to_closing()
    {
        var company = Robert();
        var vm = new ReportsViewModel(company, ReportKind.CashFlow);

        // The engine guarantees Opening + Net == Closing; the VM renders those three lines verbatim.
        var period = new PeriodRange(company.BooksBeginFrom, vm.AsOf);
        var cf = CashFlow.Build(company, period);
        Assert.True(cf.Reconciles);

        var opening = vm.Rows.Single(r => r.Particulars == "Opening Balance").Amount;
        var net = vm.Rows.Single(r => r.Particulars == "Net Cash Flow").Amount;
        var closing = vm.Rows.Single(r => r.Particulars == "Closing Balance").Amount;

        Assert.Equal(IndianFormat.AmountAlways(cf.OpeningBalance), opening);
        Assert.Equal(IndianFormat.AmountAlways(cf.NetCashFlow), net);
        Assert.Equal(IndianFormat.AmountAlways(cf.ClosingBalance), closing);
    }

    [Fact]
    public void FundsFlow_opens_with_balanced_total_sources_and_applications()
    {
        var company = Robert();
        var vm = new ReportsViewModel(company, ReportKind.FundsFlow);

        Assert.Equal("Funds Flow", vm.Title);
        Assert.True(vm.IsAccountingReport);
        Assert.NotEmpty(vm.Rows);
        Assert.Contains(vm.Rows, r => r.Particulars == "Sources of Funds");
        Assert.Contains(vm.Rows, r => r.Particulars == "Applications of Funds");

        // The funds-flow statement always balances: the rendered Total Sources == Total Applications.
        var ff = FundsFlow.Build(company, new PeriodRange(company.BooksBeginFrom, vm.AsOf));
        Assert.True(ff.Balanced);
        var totalSources = vm.Rows.Single(r => r.Particulars == "Total Sources").Amount;
        var totalApplications = vm.Rows.Single(r => r.Particulars == "Total Applications").Amount;
        Assert.Equal(totalSources, totalApplications);
        Assert.Equal(IndianFormat.AmountAlways(ff.TotalSources), totalSources);
    }

    [Fact]
    public void RatioAnalysis_renders_a_present_ratio_and_guards_divide_by_zero_as_na()
    {
        var company = Robert();
        var vm = new ReportsViewModel(company, ReportKind.RatioAnalysis);

        Assert.Equal("Ratio Analysis", vm.Title);
        Assert.True(vm.IsAccountingReport);
        Assert.NotEmpty(vm.Rows);

        var ra = RatioAnalysis.Build(company, vm.AsOf, ReportOptions.AsOf(vm.AsOf));

        // Working Capital money is always rendered (even when a derived ratio is n/a). Exclude the section
        // header of the same name — the value row is the non-header one carrying the amount.
        var workingCapitalRow = vm.Rows.Single(r => r.Particulars == "Working Capital" && !r.IsHeader);
        Assert.Equal(IndianFormat.AmountAlways(ra.WorkingCapital), workingCapitalRow.Amount);

        // Return on Investment % is present (proprietor's funds are non-zero for Robert) and rendered as a %.
        Assert.NotNull(ra.ReturnOnInvestmentPercent);
        var roiRow = vm.Rows.Single(r => r.Particulars == "Return on Investment %");
        Assert.Equal(
            ra.ReturnOnInvestmentPercent!.Value.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) + "%",
            roiRow.Amount);

        // Robert has no stock → inventory turnover is a guarded divide-by-zero → renders "N/A" (never a crash).
        Assert.Null(ra.InventoryTurnover);
        Assert.Equal("N/A", vm.Rows.Single(r => r.Particulars == "Inventory Turnover").Amount);

        // The expanded (Tally-faithful) ratio set is rendered: Working Capital Turnover has a value row that
        // matches the engine (Robert has working capital and no stock, so it is a real 2-dp ratio, not N/A).
        Assert.NotNull(ra.WorkingCapitalTurnover);
        var wctRow = vm.Rows.Single(r => r.Particulars == "Working Capital Turnover");
        Assert.Equal(
            ra.WorkingCapitalTurnover!.Value.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture),
            wctRow.Amount);
    }

    [Fact]
    public void RatioAnalysis_renders_tally_two_column_layout_with_unit_suffixes()
    {
        var vm = new ReportsViewModel(Robert(), ReportKind.RatioAnalysis);

        // The Tally layout: a "Principal Groups" section header followed by a "Principal Ratios" section header.
        var headers = vm.Rows.Where(r => r.IsHeader).Select(r => r.Particulars).ToList();
        Assert.Contains("Principal Groups", headers);
        Assert.Contains("Principal Ratios", headers);
        Assert.True(headers.IndexOf("Principal Groups") < headers.IndexOf("Principal Ratios"),
            "Principal Groups must render above Principal Ratios (Tally two-column order).");

        // Every principal-group figure and principal-ratio label from the engine has a matching rendered row.
        var ra = RatioAnalysis.Build(Robert(), vm.AsOf, ReportOptions.AsOf(vm.AsOf));
        foreach (var g in ra.PrincipalGroups)
            Assert.Contains(vm.Rows, r => !r.IsHeader && r.Particulars == g.Label);
        foreach (var r in ra.PrincipalRatios)
            Assert.Contains(vm.Rows, row => row.Particulars == r.Label);

        // Unit suffixes: a days ratio ends " days" or "N/A"; a percent ends "%" or "N/A"; a plain ratio is a
        // 2-dp number or "N/A".
        // 🔴 CORRECTED. This clause used to allow only " days" or a bare "N/A", and Robert is accounts-only with
        // no bill-wise details — so it REJECTED the explicit marker user ruling 27 requires. A withheld ratio
        // that carries a reason renders that reason; the suffix rule applies to a ratio that HAS a value.
        var recvRow = vm.Rows.Single(r => r.Particulars == "Receivables Turnover (days)");
        Assert.True(
            recvRow.Amount == "N/A"
            || recvRow.Amount == RatioAnalysis.ReceivablesNoBillWiseDetails
            || recvRow.Amount == RatioAnalysis.ReceivablesNoBillsDueYet
            || recvRow.Amount.EndsWith(" days"),
            $"Receivables Turnover should render ' days', 'N/A' or a stated reason but was '{recvRow.Amount}'.");
        // On Robert specifically the answer is the marker, not a number and not a bare "N/A": the book keeps no
        // bill-wise details at all, which is the case the ruling is about.
        Assert.Equal(RatioAnalysis.ReceivablesNoBillWiseDetails, recvRow.Amount);
        var opCostRow = vm.Rows.Single(r => r.Particulars == "Operating Cost %");
        Assert.True(opCostRow.Amount == "N/A" || opCostRow.Amount.EndsWith("%"),
            $"Operating Cost % should render '%' or 'N/A' but was '{opCostRow.Amount}'.");
    }

    /// <summary>
    /// 🔴 THE STRING A BANK OR AN AUDITOR ACTUALLY READS. The engine withholding the figure is only half the
    /// fix: <c>AddRatioDays</c> renders <c>null</c> as "N/A" but <c>0</c> as <b>"0 days"</b>, so a book with no
    /// bill-wise details used to PRINT "0 days" beside a Sundry Debtors figure of 1,00,000 — the wrong answer
    /// stated with confidence rather than withheld. This asserts the rendered cell, not the engine member.
    /// <para>The fixture has NON-ZERO sales on purpose, so the "N/A" cannot come from the pre-existing
    /// zero-denominator guard.</para>
    /// </summary>
    [Fact]
    public void RatioAnalysis_renders_NA_not_zero_days_when_debtors_keep_no_bill_wise_details()
    {
        var asOf = new DateOnly(2024, 6, 30);
        var c = Apex.Ledger.Services.CompanyFactory.CreateSeeded(
            "Plain Books Co " + Guid.NewGuid().ToString("N"),
            new DateOnly(2024, 4, 1), new DateOnly(2024, 4, 1));
        var journal = c.FindVoucherTypeByName("Journal")!;

        var sales = new Ledger.Domain.Ledger(Guid.NewGuid(), "Sales",
            c.FindGroupByName("Sales Accounts")!.Id, Money.Zero, openingIsDebit: false);
        c.AddLedger(sales);
        // Default MaintainBillByBill (false) — the ordinary book shape.
        var debtor = new Ledger.Domain.Ledger(Guid.NewGuid(), "Plain Co",
            c.FindGroupByName("Sundry Debtors")!.Id, Money.Zero, openingIsDebit: true);
        c.AddLedger(debtor);

        new Apex.Ledger.Services.LedgerService(c).Post(new Voucher(
            Guid.NewGuid(), journal.Id, new DateOnly(2024, 4, 20), new[]
            {
                new EntryLine(debtor.Id, Money.FromRupees(100000m), DrCr.Debit),
                new EntryLine(sales.Id, Money.FromRupees(100000m), DrCr.Credit),
            }));

        var vm = new ReportsViewModel(c, ReportKind.RatioAnalysis);
        vm.SetAsOf(asOf);

        // 🔴 THE RENDERED CELL. "0 days" here is the shipped defect. A BARE "N/A" was the previous attempt and
        // is ALSO wrong: it is byte-identical to the zero-denominator "N/A" every other ratio uses, so an
        // operator cannot tell "we have no bill-wise data" from "the denominator was zero". User ruling 27
        // requires an explicit marker that says WHY, and this asserts the shipping string.
        var recvRow = vm.Rows.Single(r => r.Particulars == "Receivables Turnover (days)");
        Assert.Equal(Apex.Ledger.Reports.RatioAnalysis.ReceivablesNoBillWiseDetails, recvRow.Amount);
        Assert.NotEqual("0 days", recvRow.Amount);
        Assert.NotEqual("N/A", recvRow.Amount);
        // …and it is a legible sentence, not a code: it names the missing thing.
        Assert.Contains("bill-wise", recvRow.Amount);

        // …while the Balance-Sheet-side figure on the SAME report shows the real money, which is exactly what
        // made "0 days" beside it indefensible. The group row keeps its "(due till today)" qualifier.
        Assert.Contains(vm.Rows, r => r.Particulars == "Sundry Debtors (due till today)");

        // Sales is non-zero, so a ratio that divides by Sales still renders a number — proof the marker above is
        // the bill-wise guard and not a zero denominator.
        var grossRow = vm.Rows.Single(r => r.Particulars == "Gross Profit %");
        Assert.EndsWith("%", grossRow.Amount);
        Assert.NotEqual("N/A", grossRow.Amount);
    }

    /// <summary>
    /// 🔴 USER RULING 27, ASSERTED AS THREE RENDERED STRINGS IN ONE PLACE. Receivables Turnover can be absent for
    /// three different reasons, and an operator reading the cell must be able to tell them apart:
    /// <list type="number">
    /// <item>the book keeps no bill-wise details, so the numerator measures nothing;</item>
    /// <item>the bills exist and cover every rupee, but none has fallen due yet;</item>
    /// <item>the denominator is zero — the ordinary guard every other ratio uses, which renders a bare "N/A".</item>
    /// </list>
    /// A previous attempt rendered case 1 as a bare "N/A", byte-identical to case 3. This test fails if any two of
    /// the three ever render the same string again.
    /// </summary>
    [Fact]
    public void RatioAnalysis_renders_a_DIFFERENT_cell_for_each_reason_the_receivables_ratio_is_absent()
    {
        var asOf = new DateOnly(2024, 6, 30);

        // ---- (1) no bill-wise details: a default-flag debtor funded by a credit sale.
        var noBills = NewBook("No Bills Co");
        var noBillsSales = AddLedger(noBills, "Sales", "Sales Accounts", debit: false);
        var plainDebtor = AddLedger(noBills, "Plain Co", "Sundry Debtors", debit: true);
        Post(noBills, new DateOnly(2024, 4, 20),
            new EntryLine(plainDebtor.Id, Money.FromRupees(100000m), DrCr.Debit),
            new EntryLine(noBillsSales.Id, Money.FromRupees(100000m), DrCr.Credit));

        // ---- (2) covered by bills, none due: a bill-wise debtor whose only invoice is due after the statement.
        var notDue = NewBook("Not Due Co");
        var notDueSales = AddLedger(notDue, "Sales", "Sales Accounts", debit: false);
        var patient = AddLedger(notDue, "Patient Ltd", "Sundry Debtors", debit: true, billWise: true);
        Post(notDue, new DateOnly(2024, 4, 20),
            new EntryLine(patient.Id, Money.FromRupees(100000m), DrCr.Debit, new[]
            {
                new BillAllocation(BillRefType.NewRef, "INV-9", Money.FromRupees(100000m),
                    dueDate: new DateOnly(2024, 7, 20)),
            }),
            new EntryLine(notDueSales.Id, Money.FromRupees(100000m), DrCr.Credit));

        // ---- (3) zero denominator: a bill-wise debtor with a DUE bill and NO sales at all, so the numerator is
        //          real and it is Sales that is 0 — the pre-existing guard, untouched by this work.
        var noSales = NewBook("No Sales Co");
        var loan = AddLedger(noSales, "Loan Taken", "Loans (Liability)", debit: false);
        var owing = AddLedger(noSales, "Owes Us Ltd", "Sundry Debtors", debit: true, billWise: true);
        Post(noSales, new DateOnly(2024, 4, 20),
            new EntryLine(owing.Id, Money.FromRupees(50000m), DrCr.Debit, new[]
            {
                new BillAllocation(BillRefType.NewRef, "INV-Z", Money.FromRupees(50000m),
                    dueDate: new DateOnly(2024, 5, 1)),
            }),
            new EntryLine(loan.Id, Money.FromRupees(50000m), DrCr.Credit));

        // ---- (1b) THE SHAPE THE FLAG-BASED GUARD COULD NOT SEE: the debtor IS bill-wise, but its balance came in
        //           as an OPENING balance, so no bill exists for it. Rendered here, because this is the cell the
        //           reviewer found still printing "0 days".
        var opening = NewBook("Opening Co");
        var openingSales = AddLedger(opening, "Sales", "Sales Accounts", debit: false);
        var cash = AddLedger(opening, "Cash A", "Cash-in-Hand", debit: true);
        var carried = new Ledger.Domain.Ledger(Guid.NewGuid(), "Carried Forward Ltd",
            opening.FindGroupByName("Sundry Debtors")!.Id, Money.FromRupees(100000m), openingIsDebit: true,
            maintainBillByBill: true);
        opening.AddLedger(carried);
        Post(opening, new DateOnly(2024, 4, 20),
            new EntryLine(cash.Id, Money.FromRupees(80000m), DrCr.Debit),
            new EntryLine(openingSales.Id, Money.FromRupees(80000m), DrCr.Credit));

        var a = RenderedReceivablesCell(noBills, asOf);
        var b = RenderedReceivablesCell(notDue, asOf);
        var z = RenderedReceivablesCell(noSales, asOf);

        // 🔴 THE RENDERED CELL ON THE OPENING-BALANCE BOOK. "0 days" is what shipped past the flag-based guard.
        var carriedCell = RenderedReceivablesCell(opening, asOf);
        Assert.Equal(RatioAnalysis.ReceivablesNoBillWiseDetails, carriedCell);
        Assert.NotEqual("0 days", carriedCell);

        // 🔴 THE THREE SHIPPING STRINGS, each named.
        Assert.Equal(RatioAnalysis.ReceivablesNoBillWiseDetails, a);
        Assert.Equal(RatioAnalysis.ReceivablesNoBillsDueYet, b);
        Assert.Equal("N/A", z);

        // 🔴 AND PAIRWISE DISTINCT — the ruling's actual requirement, independent of the constants' values.
        Assert.NotEqual(a, b);
        Assert.NotEqual(a, z);
        Assert.NotEqual(b, z);
        // None of them is the confident zero, which is where this whole track started.
        Assert.NotEqual("0 days", a);
        Assert.NotEqual("0 days", b);
        Assert.NotEqual("0 days", z);
    }

    /// <summary>
    /// 🔴 THE MARKER HAS TO LEAVE THE BUILDING INTACT, AND THAT IS NOT FREE. The printed Amount column carries
    /// weight 1.5 against Particulars' 3, and <c>ReportPdf</c> silently truncates any cell too wide for its column
    /// (<c>PdfWriter.FitToWidth</c>, ellipsis appended). A marker is a sentence, not a money figure, so it is far
    /// wider than anything this column was sized for — and a previous pass in this very family shipped a print
    /// regression that clipped a figure mid-number. This asserts the EMITTED PDF BYTES, not the screen row: the
    /// ASCII-folded marker (the em dash folds to '-' in <c>ReportPrintProjector.Ascii</c>) must appear whole, with
    /// no ellipsis, in the rendered document.
    /// </summary>
    [Fact]
    public void The_withheld_receivables_marker_survives_whole_into_the_emitted_report_PDF()
    {
        var asOf = new DateOnly(2024, 6, 30);
        var c = NewBook("Printed Marker Co");
        var salesLedger = AddLedger(c, "Sales", "Sales Accounts", debit: false);
        var debtor = AddLedger(c, "Plain Co", "Sundry Debtors", debit: true);
        Post(c, new DateOnly(2024, 4, 20),
            new EntryLine(debtor.Id, Money.FromRupees(100000m), DrCr.Debit),
            new EntryLine(salesLedger.Id, Money.FromRupees(100000m), DrCr.Credit));

        var vm = new ReportsViewModel(c, ReportKind.RatioAnalysis);
        vm.SetAsOf(asOf);
        Assert.Equal(RatioAnalysis.ReceivablesNoBillWiseDetails,
            vm.Rows.Single(r => r.Particulars == "Receivables Turnover (days)").Amount);

        var pdf = System.Text.Encoding.Latin1.GetString(
            Apex.Ledger.Io.ReportPdf.Render(
                Apex.Desktop.Services.ReportPrintProjector.Project(vm),
                new Apex.Ledger.Io.PageConfig()));

        // The marker as the printed page spells it: em dash folded to a hyphen, nothing else changed.
        var printed = RatioAnalysis.ReceivablesNoBillWiseDetails.Replace('—', '-');
        Assert.Contains(printed, pdf, StringComparison.Ordinal);
        // 🔴 AND NOT TRUNCATED. "N/A - no bill-..." with an ellipsis would still "contain" nothing useful, so the
        // clipped forms are named: if the column ever narrows, this fails instead of shipping a half sentence.
        Assert.DoesNotContain("bill-wise deta...", pdf, StringComparison.Ordinal);
        Assert.DoesNotContain("no bill-...", pdf, StringComparison.Ordinal);
        // The row's own caption is on the page too, so the marker is demonstrably beside its label. Note the
        // parentheses: a PDF content stream delimits strings with "(" and ")", so the writer escapes them —
        // searching for the screen caption verbatim finds nothing. Match the unparenthesised stem.
        Assert.Contains("Receivables Turnover", pdf, StringComparison.Ordinal);
    }

    private static Company NewBook(string name)
        => Apex.Ledger.Services.CompanyFactory.CreateSeeded(
            name + " " + Guid.NewGuid().ToString("N"), new DateOnly(2024, 4, 1), new DateOnly(2024, 4, 1));

    private static Ledger.Domain.Ledger AddLedger(
        Company c, string name, string groupName, bool debit, bool billWise = false)
    {
        var l = new Ledger.Domain.Ledger(Guid.NewGuid(), name, c.FindGroupByName(groupName)!.Id,
            Money.Zero, openingIsDebit: debit, maintainBillByBill: billWise);
        c.AddLedger(l);
        return l;
    }

    private static void Post(Company c, DateOnly date, params EntryLine[] lines)
        => new Apex.Ledger.Services.LedgerService(c).Post(
            new Voucher(Guid.NewGuid(), c.FindVoucherTypeByName("Journal")!.Id, date, lines));

    private static string RenderedReceivablesCell(Company c, DateOnly asOf)
    {
        var vm = new ReportsViewModel(c, ReportKind.RatioAnalysis);
        vm.SetAsOf(asOf);
        return vm.Rows.Single(r => r.Particulars == "Receivables Turnover (days)").Amount;
    }

    private static void PostAs(Company c, string typeName, DateOnly date, params EntryLine[] lines)
        => new Apex.Ledger.Services.LedgerService(c).Post(
            new Voucher(Guid.NewGuid(), c.FindVoucherTypeByName(typeName)!.Id, date, lines));

    /// <summary>
    /// 🔴 A MEMORANDUM VOUCHER MUST NOT MOVE THE BILL-COVERAGE GUARD — IN EITHER DIRECTION. A Memorandum never
    /// touches the real books (<c>LedgerBalances.IsProvisionalBaseType</c>), so
    /// <c>LedgerBalances.SignedClosing</c> drops it and it is absent from the Balance-Sheet debtors figure. The
    /// coverage guard compares the bill-wise projection's pending amounts against exactly that closing balance, so
    /// it has to drop the same vouchers. It did not: it re-derived the closing balance with the voucher rule that
    /// omits the base type, and BOTH errors were reachable and rendered:
    /// <list type="bullet">
    /// <item><b>Spurious withholding.</b> A fully bill-wise book whose one invoice is already due rendered
    ///   "91 days". A Memorandum debit of 50,000 with no allocations invented a 50,000 shortfall and the cell
    ///   became the no-bill-wise-details marker — while the Balance Sheet still showed 1,00,000, unchanged.</item>
    /// <item><b>The leak back to a published number.</b> Debtor X's 1,00,000 is an uncovered bill-wise OPENING
    ///   balance, so the ratio is correctly withheld. A Memorandum CREDIT of 1,00,000 to X cancelled it out of the
    ///   measure, the guard went blind, and the report published "91 days" computed from debtor Y's 50,000 alone —
    ///   the partial-basis figure this whole track exists to stop.</item>
    /// </list>
    /// The two directions are two Facts, so a regression in either reddens by its own name; each asserts the
    /// RENDERED cell BEFORE and AFTER the memo, so the memo is proved to be the only thing that changed.
    /// </summary>
    [Fact]
    public void RatioAnalysis_a_memorandum_voucher_does_not_withhold_a_fully_bill_covered_ratio()
    {
        var asOf = new DateOnly(2024, 6, 30);

        // ---- A perfectly bill-wise, fully covered, already-due book. It publishes an honest number.
        var covered = NewBook("Memo Withhold Co");
        var coveredSales = AddLedger(covered, "Sales", "Sales Accounts", debit: false);
        var billsCo = AddLedger(covered, "Bills Co", "Sundry Debtors", debit: true, billWise: true);
        Post(covered, new DateOnly(2024, 4, 20),
            new EntryLine(billsCo.Id, Money.FromRupees(100000m), DrCr.Debit, new[]
            {
                new BillAllocation(BillRefType.NewRef, "INV-A", Money.FromRupees(100000m),
                    dueDate: new DateOnly(2024, 5, 1)),
            }),
            new EntryLine(coveredSales.Id, Money.FromRupees(100000m), DrCr.Credit));
        Assert.Equal("91 days", RenderedReceivablesCell(covered, asOf));

        var coveredSuspense = AddLedger(covered, "Suspense", "Suspense A/c", debit: true);
        PostAs(covered, "Memorandum", new DateOnly(2024, 5, 10),
            new EntryLine(billsCo.Id, Money.FromRupees(50000m), DrCr.Debit),
            new EntryLine(coveredSuspense.Id, Money.FromRupees(50000m), DrCr.Credit));

        // 🔴 THE RENDERED CELL FIRST — that is the thing the operator reads, and the thing that moved.
        var afterMemo = RenderedReceivablesCell(covered, asOf);
        Assert.Equal("91 days", afterMemo);
        Assert.NotEqual(RatioAnalysis.ReceivablesNoBillWiseDetails, afterMemo);
        // And the two figures behind it: the memo is NOT in the books, so neither may move either.
        Assert.Equal(100000m,
            RatioAnalysis.Build(covered, asOf, ReportOptions.AsOf(asOf)).SundryDebtorsClosing.Amount);
        Assert.Equal(0m, Outstandings.ClosingNotCoveredByBills(covered, asOf, "Sundry Debtors").Amount);
    }

    /// <inheritdoc cref="RatioAnalysis_a_memorandum_voucher_does_not_withhold_a_fully_bill_covered_ratio"/>
    [Fact]
    public void RatioAnalysis_a_memorandum_voucher_does_not_blind_the_bill_coverage_guard()
    {
        var asOf = new DateOnly(2024, 6, 30);

        // ---- An uncovered bill-wise OPENING balance beside a genuinely covered debtor: correctly withheld.
        var leak = NewBook("Memo Leak Co");
        var leakSales = AddLedger(leak, "Sales", "Sales Accounts", debit: false);
        var carriedX = new Ledger.Domain.Ledger(Guid.NewGuid(), "Carried X",
            leak.FindGroupByName("Sundry Debtors")!.Id, Money.FromRupees(100000m), openingIsDebit: true,
            maintainBillByBill: true);
        leak.AddLedger(carriedX);
        var coveredY = AddLedger(leak, "Covered Y", "Sundry Debtors", debit: true, billWise: true);
        Post(leak, new DateOnly(2024, 4, 20),
            new EntryLine(coveredY.Id, Money.FromRupees(50000m), DrCr.Debit, new[]
            {
                new BillAllocation(BillRefType.NewRef, "INV-Y", Money.FromRupees(50000m),
                    dueDate: new DateOnly(2024, 5, 1)),
            }),
            new EntryLine(leakSales.Id, Money.FromRupees(50000m), DrCr.Credit));
        Assert.Equal(RatioAnalysis.ReceivablesNoBillWiseDetails, RenderedReceivablesCell(leak, asOf));

        var leakSuspense = AddLedger(leak, "Suspense", "Suspense A/c", debit: true);
        PostAs(leak, "Memorandum", new DateOnly(2024, 5, 10),
            new EntryLine(leakSuspense.Id, Money.FromRupees(100000m), DrCr.Debit),
            new EntryLine(carriedX.Id, Money.FromRupees(100000m), DrCr.Credit));

        // 🔴 THE RENDERED CELL FIRST. The memo must not blind the guard: 1,00,000 still has no bill behind it.
        var stillWithheld = RenderedReceivablesCell(leak, asOf);
        Assert.Equal(RatioAnalysis.ReceivablesNoBillWiseDetails, stillWithheld);
        Assert.NotEqual("91 days", stillWithheld);   // the exact partial-basis figure the memo used to publish
        Assert.Equal(100000m, Outstandings.ClosingNotCoveredByBills(leak, asOf, "Sundry Debtors").Amount);
    }

    [Fact]
    public void CashFlow_honours_the_slice1_period_selection()
    {
        var company = Robert();
        var vm = new ReportsViewModel(company, ReportKind.CashFlow);

        // A half-month window (Alt+F2) re-projects over that period; the closing line matches the engine build.
        var from = company.BooksBeginFrom;
        var to = company.BooksBeginFrom.AddDays(14);
        vm.SetPeriod(from, to);

        var cf = CashFlow.Build(company, new PeriodRange(from, to));
        var closing = vm.Rows.Single(r => r.Particulars == "Closing Balance").Amount;
        Assert.Equal(IndianFormat.AmountAlways(cf.ClosingBalance), closing);
    }

    // =============================================================== shell nav wiring (Reports → Statements)

    [Fact]
    public void Statements_menu_lists_the_three_reports_under_one_section()
    {
        var vm = new MainWindowViewModel(_storage);
        vm.LoadRobertDemo();

        vm.ShowStatementsMenu();

        Assert.Equal(Screen.Gateway, vm.CurrentScreen);
        Assert.Equal(GatewayMenu.Statements, vm.CurrentGatewayMenu);

        // One "Financial Statements" section header, three report items — never a flat dump.
        var headers = vm.Menu.Where(m => m.IsHeader).Select(m => m.Label).ToArray();
        Assert.Equal(new[] { "Financial Statements" }, headers);
        var items = vm.Menu.Where(m => m.IsSelectable).Select(m => m.Label).ToArray();
        Assert.Equal(new[] { "Cash Flow", "Funds Flow", "Ratio Analysis" }, items);
    }

    [Theory]
    [InlineData("Cash Flow", ReportKind.CashFlow)]
    [InlineData("Funds Flow", ReportKind.FundsFlow)]
    [InlineData("Ratio Analysis", ReportKind.RatioAnalysis)]
    public void Activating_a_statements_item_opens_that_report_proving_labels_match_routing(
        string label, ReportKind expected)
    {
        var vm = new MainWindowViewModel(_storage);
        vm.LoadRobertDemo();
        vm.ShowStatementsMenu();

        while (vm.Menu[vm.SelectedIndex].Label != label) vm.MoveDown();
        vm.ActivateSelected();

        Assert.Equal(Screen.Report, vm.CurrentScreen);
        Assert.NotNull(vm.Reports);
        Assert.Equal(expected, vm.Reports!.Kind);
        Assert.NotEmpty(vm.Reports.Rows);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true); }
        catch { /* best-effort temp cleanup */ }
    }
}
