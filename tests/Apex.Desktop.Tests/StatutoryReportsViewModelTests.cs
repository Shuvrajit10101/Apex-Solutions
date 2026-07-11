using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Reports;
using Apex.Ledger.Services;
using Apex.Desktop.Services;
using Apex.Desktop.ViewModels;
using Xunit;
using Domain = Apex.Ledger.Domain;

namespace Apex.Desktop.Tests;

/// <summary>
/// UI-side coverage for Phase 7 slice 8 — the nine statutory TDS/TCS exception &amp; outstanding reports
/// (R1–R9) wired into <see cref="ReportsViewModel"/> and nested under Reports → Statutory Reports →
/// TDS Reports / TCS Reports in <see cref="MainWindowViewModel"/>. The engine projections are trusted
/// (covered by the engine <c>TdsTcsExceptionReportsTests</c>); these tests pin the UI wiring: each report
/// opens through the wide statutory grid, produces the expected columns/rows on a seeded book, renders whole
/// rupees, and is reachable through the cascading menu. Seeding mirrors the engine test's helpers so the
/// figures really surface. No UI toolkit — the real VMs are driven headlessly.
/// </summary>
public sealed class StatutoryReportsViewModelTests : IDisposable
{
    private const string ValidTan = "MUMA12345B";
    private const string VendorPan = "AAPFU0939F";
    private const string BuyerPan = "AAQCS1234K";
    private const string GstinMaharashtra = "27AAPFU0939F1ZV";

    private static readonly DateOnly Fy2025 = new(2025, 4, 1);
    private static readonly DateOnly AsOf = new(2025, 6, 30);

    private readonly string _tempDir;
    private readonly CompanyStorage _storage;

    public StatutoryReportsViewModelTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "ApexStatutoryTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _storage = new CompanyStorage(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { /* best-effort temp cleanup */ }
    }

    // ---------------------------------------------------------------- TDS seeding (mirrors the engine test) ----

    private static Company NewTdsCompany()
    {
        var c = CompanyFactory.CreateSeeded("Stat TDS Co", Fy2025);
        new TdsTcsService(c).EnableTds(new TdsConfig
        {
            Tan = ValidTan, DeductorType = DeductorType.Company,
            ResponsiblePersonName = "A. Sharma", ResponsiblePersonPan = VendorPan,
            ResponsiblePersonDesignation = "Director", ResponsiblePersonAddress = "12 MG Road",
        });
        return c;
    }

    private static Domain.Ledger AddLedger(Company c, string name, string groupName, bool openingIsDebit)
    {
        var l = new Domain.Ledger(Guid.NewGuid(), name, c.FindGroupByName(groupName)!.Id, Money.Zero, openingIsDebit);
        c.AddLedger(l);
        return l;
    }

    private static Domain.Ledger Vendor(Company c, string name = "Consultant", string? pan = VendorPan)
    {
        var nop = c.FindNatureOfPaymentByCode("194J(b)")!;
        var v = AddLedger(c, name, "Sundry Creditors", false);
        v.TdsApplicable = true; v.TdsNatureOfPaymentId = nop.Id; v.DeducteeType = DeducteeType.Firm; v.PartyPan = pan;
        return v;
    }

    private static Guid JournalTypeId(Company c) => c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Journal).Id;

    private static void BookDeduction(Company c, DateOnly on, Domain.Ledger vendor, decimal gross)
    {
        var fees = AddLedger(c, $"Professional Fees {Guid.NewGuid():N}", "Indirect Expenses", true);
        var nop = c.FindNatureOfPaymentByCode("194J(b)")!;
        var g = Money.FromRupees(gross);
        var carve = new TdsService(c).BuildCarveOut(g, g, nop, vendor, on);
        var lines = new List<EntryLine> { new(fees.Id, g, DrCr.Debit), carve.PartyLine };
        if (carve.TdsPayableLine is not null) lines.Add(carve.TdsPayableLine);
        new LedgerService(c).Post(new Voucher(Guid.NewGuid(), JournalTypeId(c), on, lines));
    }

    private static void DepositTds(Company c, Money amount, DateOnly on, string challanNo, string section = "194J(b)")
    {
        var dep = new TdsDepositService(c);
        var statType = dep.EnsureStatPaymentType();
        var bank = c.FindLedgerByName("HDFC Bank") ?? AddLedger(c, "HDFC Bank", "Bank Accounts", true);
        var posted = new LedgerService(c).Post(dep.BuildStatPayment(amount, bank, on, statType));
        dep.RecordChallan(challanNo, "0510308", on, amount, section, "200", posted);
    }

    // ---------------------------------------------------------------- TCS seeding (mirrors the engine test) ----

    private static Company NewTcsCompany()
    {
        var c = CompanyFactory.CreateSeeded("Stat TCS Co", Fy2025);
        new TdsTcsService(c).EnableTcs(new TcsConfig
        {
            Tan = ValidTan, CollectorType = DeductorType.Company,
            ResponsiblePersonName = "A. Sharma", ResponsiblePersonPan = VendorPan,
            ResponsiblePersonDesignation = "Director", ResponsiblePersonAddress = "12 MG Road",
        });
        new GstService(c).EnableGst(new GstConfig
        {
            HomeStateCode = "27", Gstin = GstinMaharashtra, RegistrationType = GstRegistrationType.Regular,
            ApplicableFrom = Fy2025, Periodicity = GstReturnPeriodicity.Monthly,
        });
        return c;
    }

    private sealed record TcsScene(Company C, StockItem Scrap, Domain.Ledger Sales, Domain.Ledger Buyer, Guid Main);

    private static TcsScene BuildTcsScene(Company c, string buyerName = "Scrap Buyer", string? pan = BuyerPan)
    {
        var inv = new InventoryService(c);
        var scrap = inv.CreateStockItem("Scrap Metal", inv.CreateStockGroup("Waste").Id, inv.CreateSimpleUnit("Kg", "Kilogram").Id);
        scrap.Gst = new StockItemGstDetails { Taxability = GstTaxability.Taxable, RateBasisPoints = 1800 };
        scrap.TcsNatureOfGoodsId = c.FindNatureOfGoodsByCode("6CE")!.Id;
        var main = c.MainLocation!.Id;

        var sales = AddLedger(c, "Scrap Sales", "Sales Accounts", false);
        var buyer = AddLedger(c, buyerName, "Sundry Debtors", true);
        buyer.PartyGst = new PartyGstDetails { RegistrationType = GstRegistrationType.Regular, Gstin = GstinMaharashtra, StateCode = "27" };
        buyer.TcsApplicable = true; buyer.CollecteeType = CollecteeType.Individual; buyer.PartyPan = pan;

        var purchases = AddLedger(c, "Purchases", "Purchase Accounts", true);
        var creditor = AddLedger(c, "Creditor", "Sundry Creditors", false);
        new LedgerService(c).Post(new Voucher(Guid.NewGuid(), c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Purchase).Id,
            c.BooksBeginFrom,
            new[] { new EntryLine(purchases.Id, Money.FromRupees(5_00_000m), DrCr.Debit), new EntryLine(creditor.Id, Money.FromRupees(5_00_000m), DrCr.Credit) },
            inventoryLines: new[] { new VoucherInventoryLine(scrap.Id, main, 10_000m, Money.FromRupees(50m)) }));

        return new TcsScene(c, scrap, sales, buyer, main);
    }

    private static void BookScrapSale(TcsScene s, DateOnly on)
    {
        var c = s.C;
        var gst = new GstService(c);
        var value = Money.FromRupees(1_00_000m);
        var interState = gst.IsInterState(s.Buyer.PartyGst!.StateCode);
        var tax = gst.ComputeInvoiceTax(new[] { new GstService.TaxableLine(value, 1800) }, interState, GstTaxDirection.Output);
        var nature = new TcsService(c).ResolveNature(s.Scrap, s.Sales)!;
        var col = new TcsService(c).BuildCollection(value, tax.TotalTax, nature, s.Buyer, on);

        var buyerDebit = col.Applies
            ? new EntryLine(s.Buyer.Id, value + tax.TotalTax + col.TcsAmount, DrCr.Debit)
            : new EntryLine(s.Buyer.Id, value + tax.TotalTax, DrCr.Debit, tcs: col.Detail);
        var lines = new List<EntryLine> { buyerDebit, new(s.Sales.Id, value, DrCr.Credit) };
        lines.AddRange(tax.TaxLines);
        if (col.TcsPayableLine is not null) lines.Add(col.TcsPayableLine);
        new LedgerService(c).Post(new Voucher(Guid.NewGuid(), c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Sales).Id, on, lines,
            inventoryLines: new[] { new VoucherInventoryLine(s.Scrap.Id, s.Main, 1000m, Money.FromRupees(100m)) }));
    }

    private static void DepositTcs(Company c, Money amount, DateOnly on, string challanNo, string code = "6CE")
    {
        var dep = new TcsDepositService(c);
        var statType = dep.EnsureStatPaymentType();
        var bank = c.FindLedgerByName("HDFC Bank") ?? AddLedger(c, "HDFC Bank", "Bank Accounts", true);
        var posted = new LedgerService(c).Post(dep.BuildStatPayment(amount, bank, on, statType));
        dep.RecordChallan(challanNo, "0510308", on, amount, code, "200", posted);
    }

    /// <summary>Posts a §206C(1F)/6CL sale of a paisa-carrying <paramref name="value"/> far below the ₹10,00,000 gate
    /// (⇒ ₹0 collected; GST passed as 0 so the assessable equals the value). The below-threshold TCS detail rides the
    /// party leg for the Not-Collected projection — used to exercise the row-vs-grand-total footing (F5).</summary>
    private static void BookBelowThresholdPaisaTcs(TcsScene s, DateOnly on, decimal value)
    {
        var c = s.C;
        var nature = c.FindNatureOfGoodsByCode("6CL")!;
        var v = Money.FromRupees(value);
        var col = new TcsService(c).BuildCollection(v, Money.Zero, nature, s.Buyer, on);
        var lines = new List<EntryLine>
        {
            new(s.Buyer.Id, v, DrCr.Debit, tcs: col.Detail),
            new(s.Sales.Id, v, DrCr.Credit),
        };
        new LedgerService(c).Post(new Voucher(Guid.NewGuid(), JournalTypeId(c), on, lines));
    }

    // =============================================================== R1 — TDS Outstandings

    [Fact]
    public void TdsOutstanding_opens_as_a_statutory_report_and_lists_the_outstanding()
    {
        var c = NewTdsCompany();
        BookDeduction(c, new DateOnly(2025, 5, 10), Vendor(c), 1_00_000m);          // TDS 10,000
        DepositTds(c, Money.FromRupees(6_000m), new DateOnly(2025, 6, 5), "AA");     // deposit 6,000

        var vm = new ReportsViewModel(c, ReportKind.TdsOutstanding);
        vm.SetAsOf(AsOf);

        Assert.Equal("TDS Outstandings", vm.Title);
        Assert.True(vm.IsStatutoryReport);
        Assert.True(vm.IsStatutoryOutstanding);
        Assert.False(vm.IsAccountingReport);
        Assert.False(vm.IsGstReport);
        Assert.Equal("Section", vm.StatCodeHeader);
        Assert.Equal("Deducted", vm.StatWithheldHeader);

        var row = vm.Rows.Single(r => r.Col1 == "194J(b)");
        Assert.Equal(IndianFormat.Rupees(Money.FromRupees(10_000m)), row.Col3);
        Assert.Equal(IndianFormat.Rupees(Money.FromRupees(6_000m)), row.Col4);
        Assert.Equal(IndianFormat.Rupees(Money.FromRupees(4_000m)), row.Col5);

        var total = vm.Rows.Single(r => r.IsTotal);
        Assert.Equal(IndianFormat.RupeesAlways(Money.FromRupees(4_000m)), total.Col5);
    }

    // =============================================================== R2 — TDS Not Deducted

    [Fact]
    public void TdsNotDeducted_lists_a_below_threshold_assessment_with_threshold_and_shortfall()
    {
        var c = NewTdsCompany();
        BookDeduction(c, new DateOnly(2025, 5, 10), Vendor(c), 20_000m);   // below ₹50,000 ⇒ TDS 0

        var vm = new ReportsViewModel(c, ReportKind.TdsNotDeducted);
        vm.SetAsOf(AsOf);

        Assert.Equal("TDS Not Deducted", vm.Title);
        Assert.True(vm.IsStatutoryNotWithheld);

        var row = vm.Rows.Single(r => r.Col2 == "Consultant");
        Assert.Contains("194J(b)", row.Col3);
        Assert.Equal(IndianFormat.Rupees(Money.FromRupees(20_000m)), row.Col4);
        Assert.Equal(IndianFormat.Rupees(Money.FromRupees(50_000m)), row.Col6);   // threshold
        Assert.Equal(IndianFormat.Rupees(Money.FromRupees(30_000m)), row.Col7);   // shortfall
    }

    // =============================================================== R3 — TDS Interest u/s 201(1A)

    [Fact]
    public void TdsInterest_shows_three_months_at_one_point_five_percent_with_the_footnote()
    {
        var c = NewTdsCompany();
        BookDeduction(c, new DateOnly(2025, 4, 30), Vendor(c), 1_00_000m);           // TDS 10,000, due 7-May
        DepositTds(c, Money.FromRupees(10_000m), new DateOnly(2025, 6, 10), "AA");    // 3 months late

        var vm = new ReportsViewModel(c, ReportKind.TdsInterest);
        vm.SetAsOf(AsOf);

        Assert.Equal("TDS Interest u/s 201(1A)", vm.Title);
        Assert.True(vm.IsStatutoryInterest);
        Assert.Equal("Interest @1.5%", vm.StatInterestHeader);
        Assert.True(vm.HasStatutoryFootnote);
        Assert.Contains("201(1A)(i)", vm.StatutoryFootnote);

        var row = vm.Rows.First(r => r.Col2 == "194J(b)");
        Assert.Equal("3", row.Col7);
        Assert.Equal(IndianFormat.Rupees(Money.FromRupees(450m)), row.Col8);        // 10,000 × 1.5% × 3

        var total = vm.Rows.Single(r => r.IsTotal);
        Assert.Equal(IndianFormat.RupeesAlways(Money.FromRupees(450m)), total.Col8);
    }

    // =============================================================== R4 — TDS Nature summary

    [Fact]
    public void TdsNatureSummary_aggregates_by_section()
    {
        var c = NewTdsCompany();
        BookDeduction(c, new DateOnly(2025, 5, 10), Vendor(c), 1_00_000m);              // deducted 10,000
        BookDeduction(c, new DateOnly(2025, 5, 12), Vendor(c, "SmallCo"), 20_000m);     // below threshold ⇒ count
        DepositTds(c, Money.FromRupees(10_000m), new DateOnly(2025, 6, 5), "AA");

        var vm = new ReportsViewModel(c, ReportKind.TdsNatureSummary);
        vm.SetAsOf(AsOf);

        Assert.Equal("TDS Nature of Payment Summary", vm.Title);
        Assert.True(vm.IsStatutoryNatureSummary);

        var row = vm.Rows.Single(r => r.Col1 == "194J(b)");
        Assert.Equal(IndianFormat.Rupees(Money.FromRupees(10_000m)), row.Col4);   // deducted
        Assert.Equal("1", row.Col7);                                              // one below-threshold txn
    }

    // =============================================================== R5 — TCS Outstandings

    [Fact]
    public void TcsOutstanding_opens_and_lists_the_collection_code_outstanding()
    {
        var s = BuildTcsScene(NewTcsCompany());
        BookScrapSale(s, new DateOnly(2025, 5, 10));                                   // collects ₹1,180
        DepositTcs(s.C, Money.FromRupees(1_000m), new DateOnly(2025, 6, 5), "AA");     // deposit ₹1,000

        var vm = new ReportsViewModel(s.C, ReportKind.TcsOutstanding);
        vm.SetAsOf(AsOf);

        Assert.Equal("TCS Outstandings", vm.Title);
        Assert.True(vm.IsStatutoryOutstanding);
        Assert.Equal("Coll. Code", vm.StatCodeHeader);
        Assert.Equal("Collected", vm.StatWithheldHeader);

        var row = vm.Rows.Single(r => r.Col1 == "6CE");
        Assert.Equal(IndianFormat.Rupees(Money.FromRupees(1_180m)), row.Col3);
        Assert.Equal(IndianFormat.Rupees(Money.FromRupees(1_000m)), row.Col4);
        Assert.Equal(IndianFormat.Rupees(Money.FromRupees(180m)), row.Col5);
    }

    // =============================================================== R6 — TCS Not Collected

    [Fact]
    public void TcsNotCollected_lists_a_below_threshold_collection()
    {
        var s = BuildTcsScene(NewTcsCompany());
        s.Scrap.TcsNatureOfGoodsId = s.C.FindNatureOfGoodsByCode("6CL")!.Id;   // ₹10,00,000 threshold
        BookScrapSale(s, new DateOnly(2025, 5, 10));                            // ₹1,18,000 < ₹10,00,000 ⇒ TCS 0

        var vm = new ReportsViewModel(s.C, ReportKind.TcsNotCollected);
        vm.SetAsOf(AsOf);

        Assert.Equal("TCS Not Collected", vm.Title);
        Assert.True(vm.IsStatutoryNotWithheld);

        var row = vm.Rows.Single(r => r.Col3.Contains("6CL"));
        Assert.Equal(IndianFormat.Rupees(Money.FromRupees(10_00_000m)), row.Col6);   // threshold
    }

    [Fact]
    public void TcsNotCollected_grand_total_foots_to_the_displayed_rows_under_paisa_bases()
    {
        // Two below-threshold 6CL collections whose bases carry paisa (₹20,000.50 each). Each row renders as a
        // round-half-up ₹20,001, so the grand total must FOOT to ₹40,002 (the sum of the displayed rows), not the
        // ₹40,001 obtained by rounding the paisa-exact Σ (₹40,001.00).
        var s = BuildTcsScene(NewTcsCompany());
        BookBelowThresholdPaisaTcs(s, new DateOnly(2025, 5, 10), 20_000.50m);
        BookBelowThresholdPaisaTcs(s, new DateOnly(2025, 5, 11), 20_000.50m);

        var vm = new ReportsViewModel(s.C, ReportKind.TcsNotCollected);
        vm.SetAsOf(AsOf);

        var total = vm.Rows.Single(r => r.IsTotal);
        Assert.Equal("40,002", total.Col4);
        // Each displayed row reads ₹20,001, so the footed total equals their sum (not ₹40,001).
        Assert.Equal(2, vm.Rows.Count(r => r.Col4 == "20,001"));
    }

    // =============================================================== R7 — TCS Interest u/s 206C(7)

    [Fact]
    public void TcsInterest_shows_three_months_at_one_percent_and_no_footnote()
    {
        var s = BuildTcsScene(NewTcsCompany());
        BookScrapSale(s, new DateOnly(2025, 4, 30));                                    // collects ₹1,180, due 7-May
        DepositTcs(s.C, Money.FromRupees(1_180m), new DateOnly(2025, 6, 10), "AA");     // 3 months late

        var vm = new ReportsViewModel(s.C, ReportKind.TcsInterest);
        vm.SetAsOf(AsOf);

        Assert.Equal("TCS Interest u/s 206C(7)", vm.Title);
        Assert.True(vm.IsStatutoryInterest);
        Assert.Equal("Interest @1%", vm.StatInterestHeader);
        Assert.False(vm.HasStatutoryFootnote);   // §206C(7) is a single, fully-computed limb

        var row = vm.Rows.First(r => r.Col2 == "6CE");
        Assert.Equal("3", row.Col7);
        Assert.Equal(IndianFormat.Rupees(Money.FromRupees(35m)), row.Col8);   // 1,180 × 1% × 3 = 35.40 → 35
    }

    // =============================================================== R8 — TCS Nature summary

    [Fact]
    public void TcsNatureSummary_aggregates_by_collection_code()
    {
        var s = BuildTcsScene(NewTcsCompany());
        BookScrapSale(s, new DateOnly(2025, 5, 10));
        DepositTcs(s.C, Money.FromRupees(1_180m), new DateOnly(2025, 6, 5), "AA");

        var vm = new ReportsViewModel(s.C, ReportKind.TcsNatureSummary);
        vm.SetAsOf(AsOf);

        Assert.Equal("TCS Nature of Goods Summary", vm.Title);
        Assert.True(vm.IsStatutoryNatureSummary);

        var row = vm.Rows.Single(r => r.Col1 == "6CE");
        Assert.Equal(IndianFormat.Rupees(Money.FromRupees(1_180m)), row.Col4);   // collected
    }

    // =============================================================== R9 — Ledgers without PAN

    [Fact]
    public void LedgersWithoutPan_flags_a_no_pan_deductee_with_the_no_pan_tax()
    {
        var c = NewTdsCompany();
        var noPan = Vendor(c, "No-PAN Vendor", pan: null);                  // §206AA no-PAN rate applies
        BookDeduction(c, new DateOnly(2025, 5, 10), noPan, 1_00_000m);      // withheld at 20% ⇒ PanApplied false

        var vm = new ReportsViewModel(c, ReportKind.LedgersWithoutPan);
        vm.SetAsOf(AsOf);

        Assert.Equal("Ledgers without PAN", vm.Title);
        Assert.True(vm.IsLedgersWithoutPan);

        var row = vm.Rows.Single(r => r.Col1 == "No-PAN Vendor");
        Assert.Equal("No", row.Col3);
        Assert.Contains("194J(b)", row.Col4);
        Assert.Equal(IndianFormat.Rupees(Money.FromRupees(20_000m)), row.Col5);   // 20% of 1,00,000
    }

    [Fact]
    public void Statutory_reports_render_empty_states_without_crashing_on_a_clean_company()
    {
        var c = NewTdsCompany();
        foreach (var kind in new[]
                 {
                     ReportKind.TdsOutstanding, ReportKind.TdsNotDeducted, ReportKind.TdsInterest,
                     ReportKind.TdsNatureSummary, ReportKind.LedgersWithoutPan,
                 })
        {
            var vm = new ReportsViewModel(c, kind);
            Assert.True(vm.IsStatutoryReport);
            Assert.NotEmpty(vm.Rows);                 // an empty-state row, never a blank pane
            Assert.All(vm.Rows, r => Assert.False(r.IsTotal)); // no grand total on an empty report
        }
    }

    // =============================================================== shell nav wiring (Reports → Statutory Reports)

    private MainWindowViewModel NewShellCompany(string name)
    {
        var vm = new MainWindowViewModel(_storage);
        vm.NewCompanyName = name;
        vm.CreateCompany();
        Assert.Equal(Screen.Gateway, vm.CurrentScreen);
        return vm;
    }

    private static void EnableTds(MainWindowViewModel vm)
    {
        vm.ShowGstConfig();
        var page = vm.GstConfig!;
        page.TdsEnabled = true;
        page.Tan = ValidTan;
        Assert.True(page.ApplyTds());
        vm.ShowGateway();
    }

    private static void EnableTcs(MainWindowViewModel vm)
    {
        vm.ShowGstConfig();
        var page = vm.GstConfig!;
        page.TcsEnabled = true;
        page.Tan = ValidTan;
        Assert.True(page.ApplyTcs());
        vm.ShowGateway();
    }

    [Fact]
    public void StatutoryReports_is_gated_on_at_least_one_tax_being_enabled()
    {
        var vm = NewShellCompany("Gated Co");

        // Neither TDS nor TCS: no "Statutory Reports" item in the root Reports section (ER-13).
        vm.ShowGateway();
        Assert.DoesNotContain("Statutory Reports", vm.Menu.Select(m => m.Label));

        // Enable TDS: the item appears.
        EnableTds(vm);
        Assert.Contains("Statutory Reports", vm.Menu.Select(m => m.Label));
    }

    [Fact]
    public void StatutoryReports_menu_nests_tds_and_tcs_families_plus_the_common_no_pan_report()
    {
        var vm = NewShellCompany("Both Taxes Co");
        EnableTds(vm);
        EnableTcs(vm);

        vm.ShowStatutoryReportsMenu();

        Assert.Equal(Screen.Gateway, vm.CurrentScreen);
        Assert.Equal(GatewayMenu.StatutoryReports, vm.CurrentGatewayMenu);

        var headers = vm.Menu.Where(m => m.IsHeader).Select(m => m.Label).ToArray();
        Assert.Equal(new[] { "Statutory Reports" }, headers);           // one section — never a flat dump
        var items = vm.Menu.Where(m => m.IsSelectable).Select(m => m.Label).ToArray();
        Assert.Equal(new[] { "TDS Reports", "TCS Reports", "Ledgers without PAN" }, items);
    }

    [Fact]
    public void TdsReports_submenu_lists_the_four_tds_reports()
    {
        var vm = NewShellCompany("TDS Menu Co");
        EnableTds(vm);

        vm.ShowTdsReportsMenu();

        Assert.Equal(GatewayMenu.TdsReports, vm.CurrentGatewayMenu);
        var items = vm.Menu.Where(m => m.IsSelectable).Select(m => m.Label).ToArray();
        Assert.Equal(
            new[]
            {
                "TDS Outstandings", "TDS Not Deducted", "TDS Interest u/s 201(1A)",
                "TDS Nature of Payment Summary",
            },
            items);
    }

    [Fact]
    public void TcsReports_submenu_lists_the_four_tcs_reports()
    {
        var vm = NewShellCompany("TCS Menu Co");
        EnableTcs(vm);

        vm.ShowTcsReportsMenu();

        Assert.Equal(GatewayMenu.TcsReports, vm.CurrentGatewayMenu);
        var items = vm.Menu.Where(m => m.IsSelectable).Select(m => m.Label).ToArray();
        Assert.Equal(
            new[]
            {
                "TCS Outstandings", "TCS Not Collected", "TCS Interest u/s 206C(7)",
                "TCS Nature of Goods Summary",
            },
            items);
    }

    [Theory]
    [InlineData("TDS Outstandings", ReportKind.TdsOutstanding)]
    [InlineData("TDS Not Deducted", ReportKind.TdsNotDeducted)]
    [InlineData("TDS Interest u/s 201(1A)", ReportKind.TdsInterest)]
    [InlineData("TDS Nature of Payment Summary", ReportKind.TdsNatureSummary)]
    public void Activating_a_tds_report_opens_that_report_proving_labels_match_routing(string label, ReportKind expected)
    {
        var vm = NewShellCompany("TDS Route Co");
        EnableTds(vm);
        vm.ShowTdsReportsMenu();

        while (vm.Menu[vm.SelectedIndex].Label != label) vm.MoveDown();
        vm.ActivateSelected();

        Assert.Equal(Screen.Report, vm.CurrentScreen);
        Assert.NotNull(vm.Reports);
        Assert.Equal(expected, vm.Reports!.Kind);
    }

    [Theory]
    [InlineData("TCS Outstandings", ReportKind.TcsOutstanding)]
    [InlineData("TCS Not Collected", ReportKind.TcsNotCollected)]
    [InlineData("TCS Interest u/s 206C(7)", ReportKind.TcsInterest)]
    [InlineData("TCS Nature of Goods Summary", ReportKind.TcsNatureSummary)]
    public void Activating_a_tcs_report_opens_that_report_proving_labels_match_routing(string label, ReportKind expected)
    {
        var vm = NewShellCompany("TCS Route Co");
        EnableTcs(vm);
        vm.ShowTcsReportsMenu();

        while (vm.Menu[vm.SelectedIndex].Label != label) vm.MoveDown();
        vm.ActivateSelected();

        Assert.Equal(Screen.Report, vm.CurrentScreen);
        Assert.NotNull(vm.Reports);
        Assert.Equal(expected, vm.Reports!.Kind);
    }

    [Fact]
    public void Activating_ledgers_without_pan_opens_the_r9_report()
    {
        var vm = NewShellCompany("No-PAN Route Co");
        EnableTds(vm);
        vm.ShowStatutoryReportsMenu();

        while (vm.Menu[vm.SelectedIndex].Label != "Ledgers without PAN") vm.MoveDown();
        vm.ActivateSelected();

        Assert.Equal(Screen.Report, vm.CurrentScreen);
        Assert.NotNull(vm.Reports);
        Assert.Equal(ReportKind.LedgersWithoutPan, vm.Reports!.Kind);
    }
}
