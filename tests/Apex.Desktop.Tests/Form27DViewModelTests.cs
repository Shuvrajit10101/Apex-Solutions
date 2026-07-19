using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Io;
using Apex.Ledger.Reports;
using Apex.Ledger.Services;
using Apex.Desktop.ViewModels;
using Apex.Desktop.Services;
using Xunit;
using Domain = Apex.Ledger.Domain;

namespace Apex.Desktop.Tests;

/// <summary>
/// Phase 7 slice 7 — the <b>Form 27D</b> TCS-certificate page (<see cref="Form27DViewModel"/>), the collector's mirror
/// of Form 16A. Proves the page projects the collector / collectee / collection / challan blocks + totals off the pure
/// <see cref="Form27D"/> engine (screen figures == engine); that its export renders the byte-stable certificate PDF
/// straight off <see cref="Form27DPdf"/> (bytes == engine, ER-4) to the export folder; that "Form 27D" is gated on TCS
/// (ER-13) and opens/returns through the shell; and that Alt+B save-return writes the PDF and pops back. Headless.
/// </summary>
public sealed class Form27DViewModelTests : IDisposable
{
    private const string ValidTan = "MUMA12345B";
    private const string BuyerPan = "AAQCS1234K";
    private const string GstinMaharashtra = "27AAPFU0939F1ZV";
    private static readonly DateOnly FyStart = new(2025, 4, 1);

    private readonly string _tempDir;
    private readonly CompanyStorage _storage;

    public Form27DViewModelTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "ApexForm27DVmTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _storage = new CompanyStorage(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { /* best-effort */ }
    }

    private static Domain.Ledger AddLedger(Company c, string name, string groupName, bool openingIsDebit)
    {
        var l = new Domain.Ledger(Guid.NewGuid(), name, c.FindGroupByName(groupName)!.Id, Money.Zero, openingIsDebit);
        c.AddLedger(l);
        return l;
    }

    private static void EnableTcsAndGst(Company c)
    {
        new TdsTcsService(c).EnableTcs(new TcsConfig
        {
            Tan = ValidTan, CollectorType = DeductorType.Company,
            ResponsiblePersonName = "A. Sharma", ResponsiblePersonPan = "AAPFU0939F",
            ResponsiblePersonDesignation = "Director", ResponsiblePersonAddress = "12 MG Road",
        });
        new GstService(c).EnableGst(new GstConfig
        {
            HomeStateCode = "27", Gstin = GstinMaharashtra, RegistrationType = GstRegistrationType.Regular,
            ApplicableFrom = c.FinancialYearStart, Periodicity = GstReturnPeriodicity.Monthly,
        });
    }

    private static Domain.Ledger CollectElevenEighty(Company c)
    {
        var d1 = c.FinancialYearStart.AddMonths(1).AddDays(9);
        var gst = new GstService(c);
        var post = new LedgerService(c);
        var inv = new InventoryService(c);

        var scrap = inv.CreateStockItem("Scrap Metal", inv.CreateStockGroup("Waste").Id, inv.CreateSimpleUnit("Kg", "Kilogram").Id);
        scrap.Gst = new StockItemGstDetails { Taxability = GstTaxability.Taxable, RateBasisPoints = 1800 };
        scrap.TcsNatureOfGoodsId = c.FindNatureOfGoodsByCode("6CE")!.Id;
        var main = c.MainLocation!.Id;

        var sales = AddLedger(c, "Scrap Sales", "Sales Accounts", false);
        var buyer = AddLedger(c, "Scrap Buyer", "Sundry Debtors", true);
        buyer.PartyGst = new PartyGstDetails { RegistrationType = GstRegistrationType.Regular, Gstin = GstinMaharashtra, StateCode = "27" };
        buyer.TcsApplicable = true; buyer.CollecteeType = CollecteeType.Individual; buyer.PartyPan = BuyerPan;

        var purchases = AddLedger(c, "Purchases", "Purchase Accounts", true);
        var creditor = AddLedger(c, "Creditor", "Sundry Creditors", false);
        post.Post(new Voucher(Guid.NewGuid(), c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Purchase).Id, d1,
            new[] { new EntryLine(purchases.Id, Money.FromRupees(50_000m), DrCr.Debit), new EntryLine(creditor.Id, Money.FromRupees(50_000m), DrCr.Credit) },
            inventoryLines: new[] { new VoucherInventoryLine(scrap.Id, main, 1000m, Money.FromRupees(50m)) }));

        var value = Money.FromRupees(1_00_000m);
        var interState = gst.IsInterState(buyer.PartyGst!.StateCode);
        var tax = gst.ComputeInvoiceTax(new[] { new GstService.TaxableLine(value, 1800) }, interState, GstTaxDirection.Output);
        var nature = new TcsService(c).ResolveNature(scrap, sales)!;
        var col = new TcsService(c).BuildCollection(value, tax.TotalTax, nature, buyer, d1);

        var partyTotal = value + tax.TotalTax + col.TcsAmount;
        var lines = new List<EntryLine> { new(buyer.Id, partyTotal, DrCr.Debit), new(sales.Id, value, DrCr.Credit) };
        lines.AddRange(tax.TaxLines);
        lines.Add(col.TcsPayableLine!);
        post.Post(new Voucher(Guid.NewGuid(), c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Sales).Id, d1, lines,
            inventoryLines: new[] { new VoucherInventoryLine(scrap.Id, main, 1000m, Money.FromRupees(100m)) }));
        return buyer;
    }

    private static void Deposit(Company c, Money amount, DateOnly on, string challanNo)
    {
        var dep = new TcsDepositService(c);
        var statType = dep.EnsureStatPaymentType();
        var bank = c.FindLedgerByName("HDFC Bank") ?? AddLedger(c, "HDFC Bank", "Bank Accounts", true);
        var posted = new LedgerService(c).Post(dep.BuildStatPayment(amount, bank, on, statType));
        dep.RecordChallan(challanNo, "0510308", on, amount, "6CE", "200", posted);
    }

    private static (Company, Domain.Ledger) GoldenCompany()
    {
        var c = CompanyFactory.CreateSeeded("Collecting Co", FyStart);
        EnableTcsAndGst(c);
        var buyer = CollectElevenEighty(c);
        Deposit(c, Money.FromRupees(1_180m), new DateOnly(2025, 6, 5), "00123");
        return (c, buyer);
    }

    private static Form27DViewModel Q1Vm(Company c)
    {
        var vm = new Form27DViewModel(c);
        vm.SelectedYear = vm.FinancialYears.Single(y => y.StartYear == 2025);
        vm.SelectedQuarter = vm.Quarters.Single(q => q.Quarter == 1);
        return vm;
    }

    // ============================================================ (1) renders blocks + rows

    [Fact]
    public void Renders_collector_collectee_collection_and_challan_blocks()
    {
        var (c, _) = GoldenCompany();
        var vm = Q1Vm(c);

        Assert.Equal(ValidTan, vm.CollectorTan);
        Assert.Equal("Collecting Co", vm.CollectorName);
        Assert.Contains("A. Sharma", vm.ResponsiblePerson);

        var collectee = Assert.Single(vm.Collectees);
        Assert.Equal(BuyerPan, collectee.Pan);
        Assert.Equal("Scrap Buyer", collectee.Name);

        var row = Assert.Single(vm.Collections);
        Assert.Equal("6CE", row.CollectionCode);
        Assert.Equal("6CE", row.FvuCode);
        Assert.Equal("1.00", row.Rate);

        var ch = Assert.Single(vm.Challans);
        Assert.Equal("00123", ch.ChallanNo);
        Assert.False(vm.IsEmpty);
    }

    // ============================================================ (2) screen figures == engine

    [Fact]
    public void Screen_figures_equal_the_engine_projection()
    {
        var (c, buyer) = GoldenCompany();
        var vm = Q1Vm(c);

        var engine = Form27D.Build(c, 2025, 1, buyer.Id);

        Assert.NotNull(vm.Certificate);
        Assert.Equal(engine.Collections.Count, vm.Collections.Count);
        Assert.Equal(engine.Challans.Count, vm.Challans.Count);
        Assert.Equal(engine.Collector.Tan, vm.CollectorTan);
        Assert.Equal(engine.Collectee.Pan, vm.CollecteePan);
        Assert.Equal(IndianFormat.AmountAlways(engine.TotalAmountReceived), vm.TotalAmountReceived);
        Assert.Equal(IndianFormat.AmountAlways(engine.TotalTcsCollected), vm.TotalTcsCollected);
        Assert.Equal(IndianFormat.AmountAlways(engine.TotalTcsDeposited), vm.TotalTcsDeposited);
        Assert.Equal(engine.TotalTcsCollected, vm.Certificate!.TotalTcsCollected);
    }

    // ============================================================ (3) export PDF bytes == engine (ER-4)

    [Fact]
    public void Export_writes_the_certificate_pdf_matching_the_engine_bytes()
    {
        var (c, _) = GoldenCompany();
        var vm = Q1Vm(c);
        vm.ExportFolder = _tempDir;

        string? capturedPath = null;
        byte[]? capturedBytes = null;
        var ok = vm.ExportPdf((path, bytes) => { capturedPath = path; capturedBytes = bytes; });

        Assert.True(ok);
        Assert.NotNull(capturedPath);
        Assert.EndsWith(".pdf", capturedPath);
        Assert.Contains("Form27D_2025_26_Q1_" + BuyerPan, capturedPath);
        var expected = Form27DPdf.Render(vm.Certificate!, CertificatePages.Build(c.Name));
        Assert.Equal(expected, capturedBytes);
        Assert.Contains("Exported", vm.ExportStatus);
    }

    [Fact]
    public void Export_to_real_folder_creates_the_pdf_on_disk()
    {
        var (c, _) = GoldenCompany();
        var vm = Q1Vm(c);
        vm.ExportFolder = _tempDir;

        Assert.True(vm.ExportPdf());
        var expected = Path.Combine(_tempDir, vm.ExportResolvedFileName);
        Assert.True(File.Exists(expected));
        Assert.Equal(Form27DPdf.Render(vm.Certificate!, CertificatePages.Build(c.Name)), File.ReadAllBytes(expected));
    }

    // ============================================================ (4) shell gating (ER-13) + open/return

    /// <summary>
    /// Opens a shell company whose financial year is pinned to <see cref="FyStart"/> — the same year this class's
    /// vouchers are dated in. <c>CreateCompany()</c> derives the FY from <c>DateTime.Today</c>, which silently put the
    /// shell in a different financial year from its own test data and made the assertions below clock-dependent
    /// (they change vocabulary once the Income-tax Act 2025 gate opens at FY 2026-27 — see StatuteVocabularyUiTests).
    /// Pinning the year keeps this test about what it is actually testing: the TDS/TCS gating of the menu item.
    /// </summary>
    private MainWindowViewModel NewShellCompany(string name)
    {
        _storage.Save(Apex.Ledger.Services.CompanyFactory.CreateSeeded(name, FyStart, FyStart));
        var vm = new MainWindowViewModel(_storage);
        vm.ShowCompanySelect();
        vm.Menu.Single(m => m.Label == name).Activate();
        Assert.Equal(Screen.Gateway, vm.CurrentScreen);
        Assert.Equal(FyStart.Year, vm.Company!.FinancialYearStart.Year);
        return vm;
    }

    [Fact]
    public void Form27d_menu_item_is_gated_on_tcs_and_opens_the_page()
    {
        var vm = NewShellCompany("Gated Co");

        // TCS OFF: no "Form 27D" item, and OpenForm27D is a no-op (ER-13).
        vm.ShowGstReportsMenu();
        Assert.DoesNotContain("Form 27D", vm.Menu.Select(m => m.Label));
        vm.OpenForm27D();
        Assert.NotEqual(Screen.Form27D, vm.CurrentScreen);
        Assert.Null(vm.Form27D);

        // TCS ON: the item appears and the page opens + binds as a page column.
        EnableTcsAndGst(vm.Company!);
        vm.ShowGstReportsMenu();
        Assert.Contains("Form 27D", vm.Menu.Select(m => m.Label));

        vm.OpenForm27D();
        Assert.Equal(Screen.Form27D, vm.CurrentScreen);
        Assert.NotNull(vm.Form27D);
        Assert.Same(vm.Form27D, vm.Columns[^1].Form27D);
        Assert.True(vm.IsForm27DScreen);
    }

    [Fact]
    public void Alt_b_save_return_writes_the_pdf_and_pops_back()
    {
        var vm = NewShellCompany("Save Return Co");
        EnableTcsAndGst(vm.Company!);
        var c = vm.Company!;
        var fy = c.FinancialYearStart.Year;
        CollectElevenEighty(c);

        vm.OpenForm27D();
        Assert.Equal(Screen.Form27D, vm.CurrentScreen);

        vm.Form27D!.SelectedYear = vm.Form27D.FinancialYears.Single(y => y.StartYear == fy);
        vm.Form27D.SelectedQuarter = vm.Form27D.Quarters.Single(q => q.Quarter == 1);
        vm.Form27D.ExportFolder = _tempDir;
        var fileName = vm.Form27D.ExportResolvedFileName;

        vm.SaveReturnForm27D();

        Assert.NotEqual(Screen.Form27D, vm.CurrentScreen);
        Assert.Null(vm.Form27D);
        Assert.True(File.Exists(Path.Combine(_tempDir, fileName)));
    }
}
