using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Services;
using Apex.Desktop.Services;
using Apex.Desktop.ViewModels;

namespace Apex.Desktop.Tests;

/// <summary>
/// End-to-end coverage for slice 4d — the three Phase-4 GST reports (Tax Analysis, GSTR-1, GSTR-3B) wired into
/// the Miller-column nav and projected by <see cref="ReportsViewModel"/>. Seeds a GST company through the engine
/// (home Maharashtra 27; an in-state B2B customer, an out-of-state B2B customer (Gujarat 24), a B2C consumer, a
/// local supplier; an intra sale (CGST+SGST), an inter sale (IGST), a B2C sale, an exempt sale and a purchase
/// for ITC — the same five scenarios as the engine's GstReportsTests), then opens each report via the shell and
/// asserts <see cref="Screen.Report"/>, the report <see cref="ReportKind"/>/Title, the section headers and the
/// key amounts (Tax-Analysis totals, a GSTR-1 B2B row, GSTR-3B net payable). Also covers the "GST Reports"
/// submenu hierarchy + label→routing, and the GST-off empty state (no crash). Drives the headless shell over a
/// throwaway <c>.db</c> — no UI toolkit.
/// </summary>
public sealed class GstReportsViewModelTests : IDisposable
{
    private const string GstinMaharashtra = "27AAPFU0939F1ZV";
    private const string GstinGujarat = "24AAACC1206D1ZM";

    private static readonly DateOnly FyStart = new(2024, 4, 1);
    private static readonly DateOnly D1 = new(2024, 4, 3);   // purchase (ITC)
    private static readonly DateOnly D2 = new(2024, 4, 5);   // intra B2B sale
    private static readonly DateOnly D3 = new(2024, 4, 7);   // inter B2B sale
    private static readonly DateOnly D4 = new(2024, 4, 9);   // B2C intra sale
    private static readonly DateOnly D5 = new(2024, 4, 11);  // exempt sale

    private readonly string _tempDir;
    private readonly CompanyStorage _storage;

    public GstReportsViewModelTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "ApexGstReportTests_" + Guid.NewGuid().ToString("N"));
        _storage = new CompanyStorage(_tempDir);
    }

    // ---------------------------------------------------------------- scaffolding

    /// <summary>A seeded, GST-enabled, fully-posted company opened in the shell (five GST scenarios posted).</summary>
    private MainWindowViewModel NewGstCompany(string name)
    {
        var vm = new MainWindowViewModel(_storage);
        vm.NewCompanyName = name;
        vm.CreateCompany();
        Assert.Equal(Screen.Gateway, vm.CurrentScreen);

        var c = vm.Company!;
        // The shell seeds a current-year financial year; back-date it to FY 2024-25 so the dated GST
        // scenarios (April 2024) post within the books' open window.
        c.FinancialYearStart = FyStart;
        c.BooksBeginFrom = FyStart;

        var gst = new GstService(c);
        gst.EnableGst(new GstConfig
        {
            HomeStateCode = "27",
            Gstin = GstinMaharashtra,
            RegistrationType = GstRegistrationType.Regular,
            ApplicableFrom = FyStart,
            Periodicity = GstReturnPeriodicity.Monthly,
        });

        var ledgers = new LedgerService(c);
        var inv = new InventoryService(c);
        var grp = inv.CreateStockGroup("Goods");
        var nos = inv.CreateSimpleUnit("Nos", "Numbers", unitQuantityCode: "NOS");
        var main = c.MainLocation!.Id;

        var widget = inv.CreateStockItem("Widget", grp.Id, nos.Id);
        widget.Gst = new StockItemGstDetails { HsnSac = "847130", Taxability = GstTaxability.Taxable, RateBasisPoints = 1800 };
        var gadget = inv.CreateStockItem("Gadget", grp.Id, nos.Id);
        gadget.Gst = new StockItemGstDetails { HsnSac = "852990", Taxability = GstTaxability.Taxable, RateBasisPoints = 500 };
        var book = inv.CreateStockItem("Book", grp.Id, nos.Id);
        book.Gst = new StockItemGstDetails { HsnSac = "490199", Taxability = GstTaxability.Exempt };

        inv.AddOpeningBalance(gadget.Id, main, 40m, Money.FromRupees(20m));
        inv.AddOpeningBalance(book.Id, main, 5m, Money.FromRupees(150m));

        var sales = Add(c, "Sales", "Sales Accounts", false);
        var purchases = Add(c, "Purchases", "Purchase Accounts", true);
        var localDebtor = Add(c, "Local Debtor", "Sundry Debtors", true);
        localDebtor.PartyGst = new PartyGstDetails { RegistrationType = GstRegistrationType.Regular, Gstin = GstinMaharashtra, StateCode = "27" };
        var gujaratDebtor = Add(c, "Gujarat Debtor", "Sundry Debtors", true);
        gujaratDebtor.PartyGst = new PartyGstDetails { RegistrationType = GstRegistrationType.Regular, Gstin = GstinGujarat, StateCode = "24" };
        var consumer = Add(c, "Walk-in Consumer", "Sundry Debtors", true);
        consumer.PartyGst = new PartyGstDetails { RegistrationType = GstRegistrationType.Consumer, StateCode = "27" };
        var supplier = Add(c, "Local Supplier", "Sundry Creditors", false);
        supplier.PartyGst = new PartyGstDetails { RegistrationType = GstRegistrationType.Regular, Gstin = GstinMaharashtra, StateCode = "27" };

        var salesType = c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Sales).Id;
        var purchaseType = c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Purchase).Id;

        // D1: PURCHASE (ITC). ₹5000 @ 18% intra ⇒ Input CGST 450 + SGST 450.
        var pTax = gst.ComputeInvoiceTax(new[] { new GstService.TaxableLine(Money.FromRupees(5000m), 1800) }, false, GstTaxDirection.Input);
        var pLines = new List<EntryLine>
        {
            new(purchases.Id, Money.FromRupees(5000m), DrCr.Debit),
            new(supplier.Id, Money.FromRupees(5900m), DrCr.Credit),
        };
        pLines.AddRange(pTax.TaxLines);
        ledgers.Post(new Voucher(Guid.NewGuid(), purchaseType, D1, pLines, partyId: supplier.Id,
            inventoryLines: new[] { new VoucherInventoryLine(widget.Id, main, 100m, Money.FromRupees(50m)) }));

        // D2: INTRA B2B SALE. ₹1000 @ 18% ⇒ CGST 90 + SGST 90.
        var s1Tax = gst.ComputeInvoiceTax(new[] { new GstService.TaxableLine(Money.FromRupees(1000m), 1800) }, false, GstTaxDirection.Output);
        var s1Lines = new List<EntryLine>
        {
            new(localDebtor.Id, Money.FromRupees(1180m), DrCr.Debit),
            new(sales.Id, Money.FromRupees(1000m), DrCr.Credit),
        };
        s1Lines.AddRange(s1Tax.TaxLines);
        ledgers.Post(new Voucher(Guid.NewGuid(), salesType, D2, s1Lines, partyId: localDebtor.Id,
            inventoryLines: new[] { new VoucherInventoryLine(widget.Id, main, 10m, Money.FromRupees(100m)) }));

        // D3: INTER B2B SALE. ₹2000 @ 18% inter ⇒ IGST 360.
        var s2Tax = gst.ComputeInvoiceTax(new[] { new GstService.TaxableLine(Money.FromRupees(2000m), 1800) }, true, GstTaxDirection.Output);
        var s2Lines = new List<EntryLine>
        {
            new(gujaratDebtor.Id, Money.FromRupees(2360m), DrCr.Debit),
            new(sales.Id, Money.FromRupees(2000m), DrCr.Credit),
        };
        s2Lines.AddRange(s2Tax.TaxLines);
        ledgers.Post(new Voucher(Guid.NewGuid(), salesType, D3, s2Lines, partyId: gujaratDebtor.Id,
            inventoryLines: new[] { new VoucherInventoryLine(widget.Id, main, 20m, Money.FromRupees(100m)) }));

        // D4: B2C INTRA SALE. ₹1000 @ 5% ⇒ CGST 25 + SGST 25.
        var s3Tax = gst.ComputeInvoiceTax(new[] { new GstService.TaxableLine(Money.FromRupees(1000m), 500) }, false, GstTaxDirection.Output);
        var s3Lines = new List<EntryLine>
        {
            new(consumer.Id, Money.FromRupees(1050m), DrCr.Debit),
            new(sales.Id, Money.FromRupees(1000m), DrCr.Credit),
        };
        s3Lines.AddRange(s3Tax.TaxLines);
        ledgers.Post(new Voucher(Guid.NewGuid(), salesType, D4, s3Lines, partyId: consumer.Id,
            inventoryLines: new[] { new VoucherInventoryLine(gadget.Id, main, 40m, Money.FromRupees(25m)) }));

        // D5: EXEMPT SALE. ₹1000 exempt ⇒ zero tax.
        var s4Lines = new List<EntryLine>
        {
            new(localDebtor.Id, Money.FromRupees(1000m), DrCr.Debit),
            new(sales.Id, Money.FromRupees(1000m), DrCr.Credit),
        };
        ledgers.Post(new Voucher(Guid.NewGuid(), salesType, D5, s4Lines, partyId: localDebtor.Id,
            inventoryLines: new[] { new VoucherInventoryLine(book.Id, main, 5m, Money.FromRupees(200m)) }));

        _storage.Save(c);
        return vm;
    }

    private static Apex.Ledger.Domain.Ledger Add(Company c, string name, string groupName, bool openingIsDebit)
    {
        var l = new Apex.Ledger.Domain.Ledger(Guid.NewGuid(), name, c.FindGroupByName(groupName)!.Id, Money.Zero, openingIsDebit);
        c.AddLedger(l);
        return l;
    }

    // ---------------------------------------------------------------- (1) each report opens

    [Theory]
    [InlineData(ReportKind.TaxAnalysis, "Tax Analysis")]
    [InlineData(ReportKind.Gstr1, "GSTR-1")]
    [InlineData(ReportKind.Gstr3b, "GSTR-3B")]
    public void Each_gst_report_opens_as_a_report_page(ReportKind kind, string title)
    {
        var vm = NewGstCompany($"Open {kind} Co");

        vm.OpenReport(kind);

        Assert.Equal(Screen.Report, vm.CurrentScreen);
        Assert.NotNull(vm.Reports);
        Assert.Equal(kind, vm.Reports!.Kind);
        Assert.Equal(title, vm.Reports!.Title);
        Assert.True(vm.Reports!.IsGstReport);
        Assert.False(vm.Reports!.IsInventoryReport);
        Assert.False(vm.Reports!.IsAccountingReport);
        Assert.Equal(1, vm.Columns.Count(col => col.IsPage));
        Assert.Same(vm.Reports, vm.Columns[^1].Report);
    }

    // ---------------------------------------------------------------- (2) Tax Analysis totals + sections

    [Fact]
    public void Tax_analysis_shows_outward_and_inward_sections_with_head_subtotals()
    {
        var vm = NewGstCompany("TA Co");
        vm.OpenReport(ReportKind.TaxAnalysis);
        var rows = vm.Reports!.Rows;

        // Two section headers (Outward + Inward), one grand-total row.
        Assert.Contains(rows, r => r.IsHeader && r.Col1.StartsWith("Outward"));
        Assert.Contains(rows, r => r.IsHeader && r.Col1.StartsWith("Inward"));

        // Two "Sub-total" rows: outward CGST/SGST 115 each, IGST 360; inward CGST/SGST 450 each.
        var subtotals = rows.Where(r => r.IsTotal && r.Col1 == "Sub-total").ToArray();
        Assert.Equal(2, subtotals.Length);
        Assert.Equal("115.00", subtotals[0].Col2);   // outward CGST
        Assert.Equal("115.00", subtotals[0].Col3);   // outward SGST
        Assert.Equal("360.00", subtotals[0].Col4);   // outward IGST
        Assert.Equal("450.00", subtotals[1].Col2);   // inward CGST
        Assert.Equal("450.00", subtotals[1].Col3);   // inward SGST

        // Grand total row present.
        Assert.Contains(rows, r => r.IsTotal && r.Col1.StartsWith("Grand Total"));
    }

    // ---------------------------------------------------------------- (3) GSTR-1 sections + a B2B row

    [Fact]
    public void Gstr1_shows_all_sections_and_a_b2b_row_with_party_and_amounts()
    {
        var vm = NewGstCompany("G1 Co");
        vm.OpenReport(ReportKind.Gstr1);
        var rows = vm.Reports!.Rows;

        // The four section headers are present.
        Assert.Contains(rows, r => r.IsHeader && r.Col1.StartsWith("B2B"));
        Assert.Contains(rows, r => r.IsHeader && r.Col1.StartsWith("B2C"));
        Assert.Contains(rows, r => r.IsHeader && r.Col1 == "Rate-wise summary");
        Assert.Contains(rows, r => r.IsHeader && r.Col1 == "HSN / SAC summary");

        // The intra B2B invoice for the Local Debtor: GSTIN in Col2, taxable 1000, CGST 90, SGST 90.
        var b2b = rows.Single(r => r.Col1 == "Local Debtor");
        Assert.Equal(GstinMaharashtra, b2b.Col2);
        Assert.Equal("1,000.00", b2b.Col5);
        Assert.Equal("90.00", b2b.Col6);
        Assert.Equal("90.00", b2b.Col7);

        // The Gujarat inter-state B2B invoice shows IGST 360 (Col8) and no CGST.
        var inter = rows.Single(r => r.Col1 == "Gujarat Debtor");
        Assert.Equal("360.00", inter.Col8);
        Assert.Equal(string.Empty, inter.Col6);

        // An HSN row for Widget (847130) with UQC NOS and qty 30.
        var widgetHsn = rows.Single(r => r.Col1 == "847130");
        Assert.Equal("NOS", widgetHsn.Col3);
        Assert.Equal("30", widgetHsn.Col4);

        // Exempt outward line + grand total.
        Assert.Contains(rows, r => r.Col1.StartsWith("Exempt") && r.Col5 == "1,000.00");
        Assert.Contains(rows, r => r.IsTotal && r.Col1.StartsWith("Grand Total"));
    }

    // ---------------------------------------------------------------- (4) GSTR-3B structure + net payable

    [Fact]
    public void Gstr3b_shows_31_outward_4_itc_and_net_payable_per_head()
    {
        var vm = NewGstCompany("G3B Co");
        vm.OpenReport(ReportKind.Gstr3b);
        var rows = vm.Reports!.Rows;

        // Section headers 3.1 and 4 are present.
        Assert.Contains(rows, r => r.IsHeader && r.Col1.StartsWith("3.1"));
        Assert.Contains(rows, r => r.IsHeader && r.Col1.StartsWith("4"));

        // 3.1(a) taxable outward supplies: taxable 4000, CGST 115, SGST 115, IGST 360.
        var outward = rows.Single(r => r.Col1.StartsWith("(a)"));
        Assert.Equal("4,000.00", outward.Col2);
        Assert.Equal("115.00", outward.Col3);
        Assert.Equal("360.00", outward.Col5);

        // 4(A) eligible ITC: CGST 450, SGST 450, IGST blank.
        var itc = rows.Single(r => r.Col1.StartsWith("(A)"));
        Assert.Equal("450.00", itc.Col3);
        Assert.Equal("450.00", itc.Col4);

        // Net payable per head: CGST/SGST = 115 − 450 = −335 (carried-forward credit); IGST = 360.
        var net = rows.Single(r => r.Col1.StartsWith("Net payable"));
        Assert.Equal("-335.00", net.Col3);
        Assert.Equal("-335.00", net.Col4);
        Assert.Equal("360.00", net.Col5);
        Assert.True(net.IsTotal);
    }

    // ---------------------------------------------------------------- (5) nav hierarchy + label routing

    [Fact]
    public void Gst_reports_group_nests_under_reports_and_lists_the_three_returns()
    {
        var vm = NewGstCompany("Nav Co");

        // The root Reports section exposes the "GST Reports" group.
        var rootItems = vm.Menu.Where(m => m.IsSelectable).Select(m => m.Label).ToArray();
        Assert.Contains("GST Reports", rootItems);

        vm.ShowGstReportsMenu();
        Assert.Equal(Screen.Gateway, vm.CurrentScreen);
        Assert.Equal(GatewayMenu.GstReports, vm.CurrentGatewayMenu);

        // One GST section header, three report items — never a flat dump.
        var headers = vm.Menu.Where(m => m.IsHeader).Select(m => m.Label).ToArray();
        Assert.Equal(new[] { "GST" }, headers);
        var items = vm.Menu.Where(m => m.IsSelectable).Select(m => m.Label).ToArray();
        Assert.Equal(new[] { "Tax Analysis", "GSTR-1", "GSTR-3B" }, items);
    }

    [Fact]
    public void Activating_a_gst_report_item_opens_that_report_proving_labels_match_routing()
    {
        var vm = NewGstCompany("Route Co");
        vm.ShowGstReportsMenu();

        while (vm.Menu[vm.SelectedIndex].Label != "GSTR-3B") vm.MoveDown();
        vm.ActivateSelected();

        Assert.Equal(Screen.Report, vm.CurrentScreen);
        Assert.Equal(ReportKind.Gstr3b, vm.Reports!.Kind);
        Assert.Equal("GSTR-3B", vm.Reports!.Title);
    }

    [Fact]
    public void Esc_steps_back_from_a_gst_report_to_the_gst_reports_submenu()
    {
        var vm = NewGstCompany("Back Co");
        vm.ShowGstReportsMenu();
        vm.OpenReport(ReportKind.Gstr1);
        Assert.Equal(Screen.Report, vm.CurrentScreen);

        vm.Back();
        Assert.Equal(Screen.Gateway, vm.CurrentScreen);
        Assert.Equal(GatewayMenu.GstReports, vm.CurrentGatewayMenu);
        Assert.Null(vm.Reports);
    }

    // ---------------------------------------------------------------- (6) GST-off empty state (no crash)

    [Fact]
    public void Gst_off_company_opens_each_gst_report_to_a_friendly_empty_state()
    {
        var vm = new MainWindowViewModel(_storage);
        vm.NewCompanyName = "Plain Co";
        vm.CreateCompany();
        Assert.False(vm.Company!.GstEnabled);

        foreach (var kind in new[] { ReportKind.TaxAnalysis, ReportKind.Gstr1, ReportKind.Gstr3b })
        {
            vm.OpenReport(kind);
            Assert.Equal(Screen.Report, vm.CurrentScreen);
            Assert.Equal(kind, vm.Reports!.Kind);
            // A single explanatory header row, no crash.
            Assert.Contains(vm.Reports!.Rows, r => r.IsHeader && r.Col1.Contains("GST is not enabled"));
        }
    }

    public void Dispose()
    {
        try
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true);
        }
        catch (IOException) { /* best effort */ }
    }
}
