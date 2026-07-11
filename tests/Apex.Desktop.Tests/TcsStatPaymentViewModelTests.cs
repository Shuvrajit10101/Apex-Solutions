using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Io;
using Apex.Ledger.Reports;
using Apex.Ledger.Services;
using Apex.Desktop.Services;
using Apex.Desktop.ViewModels;
using Xunit;
using DomainLedger = Apex.Ledger.Domain.Ledger;

namespace Apex.Desktop.Tests;

/// <summary>
/// Phase 7 slice 6 — the TCS <b>Stat Payment</b> (deposit) UI (<see cref="TcsStatPaymentViewModel"/>), the <b>TCS
/// Challan Reconciliation</b> report UI (<see cref="TcsChallanReconciliationViewModel"/>) and the <b>Form 27EQ</b>
/// return UI (<see cref="Form27EQViewModel"/>). The exact mirror of the TDS slice-3/4 UI tests, for the collector's
/// side: the deposit page discharges the collected "TCS Payable" back to zero through the engine and records the
/// ITNS-281 challan (persisted with its fields + voucher link); the reconciliation renders + matches deposits to
/// collections per §206C code; Form 27EQ renders the collector/challan/collectee blocks + control totals off the pure
/// engine (screen == engine) and its FVU export writes the byte-stable file; and all three are gated so a non-TCS
/// company is byte-identical (ER-13). Drives the real VMs headlessly — no UI toolkit.
/// </summary>
public sealed class TcsStatPaymentViewModelTests : IDisposable
{
    private const string ValidTan = "MUMA12345B";
    private const string BuyerPan = "AAQCS1234K";
    private const string GstinMaharashtra = "27AAPFU0939F1ZV";

    private readonly string _tempDir;
    private readonly CompanyStorage _storage;

    public TcsStatPaymentViewModelTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "ApexTcsStatPayTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _storage = new CompanyStorage(_tempDir);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true); }
        catch { /* best-effort cleanup */ }
    }

    private MainWindowViewModel NewCompany(string name)
    {
        var vm = new MainWindowViewModel(_storage);
        vm.NewCompanyName = name;
        vm.CreateCompany();
        return vm;
    }

    private static DomainLedger AddLedger(Company c, string name, string groupName, bool openingIsDebit)
    {
        var l = new DomainLedger(Guid.NewGuid(), name, c.FindGroupByName(groupName)!.Id, Money.Zero, openingIsDebit);
        c.AddLedger(l);
        return l;
    }

    /// <summary>A TCS-enabled (+GST) company (mirrors the engine slice-6 setup) hosted by a real shell.</summary>
    private MainWindowViewModel TcsCompany(string name)
    {
        var vm = NewCompany(name);
        var c = vm.Company!;
        new TdsTcsService(c).EnableTcs(new TcsConfig
        {
            Tan = ValidTan, CollectorType = DeductorType.Company,
            ResponsiblePersonName = "A. Sharma", ResponsiblePersonPan = BuyerPan,
            ResponsiblePersonDesignation = "Director", ResponsiblePersonAddress = "12 MG Road",
        });
        new GstService(c).EnableGst(new GstConfig
        {
            HomeStateCode = "27", Gstin = GstinMaharashtra, RegistrationType = GstRegistrationType.Regular,
            ApplicableFrom = c.FinancialYearStart, Periodicity = GstReturnPeriodicity.Monthly,
        });
        return vm;
    }

    /// <summary>Q1 collection date (~10-May) of the hosted company's own financial year — inside its BooksBeginFrom.</summary>
    private static DateOnly CollectionDate(Company c) => c.FinancialYearStart.AddMonths(1).AddDays(9);

    /// <summary>Posts the golden scrap sale collecting ₹1,180 TCS (1% of the ₹1,18,000 GST-inclusive base under 6CE).
    /// Mirrors <c>TcsDepositServiceTests.CollectElevenEighty</c>.</summary>
    private static void CollectElevenEighty(Company c)
    {
        var d1 = CollectionDate(c);
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
        Assert.Equal(Money.FromRupees(1_180m), col.TcsAmount);

        var partyTotal = value + tax.TotalTax + col.TcsAmount;
        var lines = new List<EntryLine> { new(buyer.Id, partyTotal, DrCr.Debit), new(sales.Id, value, DrCr.Credit) };
        lines.AddRange(tax.TaxLines);
        lines.Add(col.TcsPayableLine!);
        post.Post(new Voucher(Guid.NewGuid(), c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Sales).Id, d1, lines,
            inventoryLines: new[] { new VoucherInventoryLine(scrap.Id, main, 1000m, Money.FromRupees(100m)) }));
    }

    private static bool FillChallan(TcsStatPaymentViewModel p, string challanNo)
    {
        p.ChallanNo = challanNo;
        p.BsrCode = "0510308";
        p.MinorHead = "200";
        return p.Deposit();
    }

    // ---- (1) deposit zeroes the payable in the view ----

    [Fact]
    public void Stat_payment_zeroes_payable_in_the_view()
    {
        var vm = TcsCompany("Deposit Co");
        CollectElevenEighty(vm.Company!);

        var page = new TcsStatPaymentViewModel(vm.Company!, _storage);
        // Opening the page defaults to the 6CE code with its full ₹1,180 remaining.
        Assert.Equal("1,180.00", page.OutstandingText);
        Assert.NotNull(page.SelectedCode);
        Assert.Equal("6CE", page.SelectedCode!.CollectionCode);
        Assert.Equal("1180.00", page.AmountText);
        Assert.Equal("6CE", page.CollectionCode);
        Assert.NotNull(page.SelectedBank);

        Assert.True(FillChallan(page, "CIN-001"));
        Assert.True(page.LastDepositSucceeded);
        // The payable is now discharged — the outstanding shown in the view is zero.
        Assert.Equal("0.00", page.OutstandingText);
        Assert.Empty(page.CodeOptions);
    }

    [Fact]
    public void Stat_payment_posts_dr_payable_cr_bank_and_reduces_the_ledger_to_zero()
    {
        var vm = TcsCompany("Ledger-Zero Co");
        CollectElevenEighty(vm.Company!);
        var c = vm.Company!;
        var payable = new TcsService(c).RequirePayableLedger();
        var bank = AddLedger(c, "HDFC Bank", "Bank Accounts", true);

        var page = new TcsStatPaymentViewModel(c, _storage);
        page.SelectedBank = bank;
        Assert.True(FillChallan(page, "CIN-002"));

        // A Stat-Payment voucher was posted: Dr TCS Payable 1,180 / Cr Bank 1,180, and it is balanced.
        var statType = c.VoucherTypes.Single(t => t.IsStatPaymentType);
        var posted = c.Vouchers.Single(v => v.TypeId == statType.Id);
        Assert.True(VoucherValidator.IsBalanced(posted));
        var payableLine = posted.Lines.Single(l => l.LedgerId == payable.Id);
        var bankLine = posted.Lines.Single(l => l.LedgerId == bank.Id);
        Assert.Equal(DrCr.Debit, payableLine.Side);
        Assert.Equal(Money.FromRupees(1_180m), payableLine.Amount);
        Assert.Equal(DrCr.Credit, bankLine.Side);
        Assert.Equal(Money.FromRupees(1_180m), bankLine.Amount);

        Assert.Equal(0m, new TcsDepositService(c).OutstandingPayable(page.AsOf).Amount);
    }

    // ---- (2) recon report renders + matches ----

    [Fact]
    public void Recon_report_renders_and_matches_after_deposit()
    {
        var vm = TcsCompany("Recon-Matched Co");
        CollectElevenEighty(vm.Company!);
        Assert.True(FillChallan(new TcsStatPaymentViewModel(vm.Company!, _storage), "CIN-003"));

        var recon = new TcsChallanReconciliationViewModel(vm.Company!);
        var row = Assert.Single(recon.Rows);
        Assert.Equal("6CE", row.CollectionCode);
        Assert.Equal("1,180.00", row.Collected);
        Assert.Equal("1,180.00", row.Deposited);
        Assert.Equal("0.00", row.Remaining);
        Assert.True(row.IsMatched);
        Assert.Equal("Matched", row.Status);
        Assert.True(recon.IsFullyReconciled);
        Assert.Equal("0.00", recon.TotalRemaining);
        Assert.Equal(0, recon.HighlightedIndex); // a live ListBox selection is seeded
    }

    [Fact]
    public void Recon_report_shows_short_code_when_not_deposited()
    {
        var vm = TcsCompany("Recon-Short Co");
        CollectElevenEighty(vm.Company!);
        // No deposit made — the code is collected-but-undeposited.

        var recon = new TcsChallanReconciliationViewModel(vm.Company!);
        var row = Assert.Single(recon.Rows);
        Assert.Equal("1,180.00", row.Collected);
        Assert.Equal("0.00", row.Deposited);
        Assert.Equal("1,180.00", row.Remaining);
        Assert.False(row.IsMatched);
        Assert.Equal("Short", row.Status);
        Assert.False(recon.IsFullyReconciled);
        Assert.Equal("1,180.00", recon.TotalRemaining);
    }

    [Fact]
    public void Recon_report_empty_on_a_company_with_no_tcs_activity()
    {
        var vm = TcsCompany("No-Activity Co");
        var recon = new TcsChallanReconciliationViewModel(vm.Company!);
        Assert.Empty(recon.Rows);
        Assert.Equal(-1, recon.HighlightedIndex);
        Assert.NotNull(recon.Message);
    }

    // ---- (3) the challan fields persist (round-trip through storage) ----

    [Fact]
    public void Challan_fields_persist_and_link_to_the_stat_payment_voucher()
    {
        var vm = TcsCompany("Challan-Persist Co");
        CollectElevenEighty(vm.Company!);
        var companyName = vm.Company!.Name;

        var depositDate = vm.Company!.FinancialYearStart.AddMonths(2).AddDays(4); // ~05-Jun of the company FY (Q1)
        var page = new TcsStatPaymentViewModel(vm.Company!, _storage);
        page.DepositDateText = depositDate.ToString("dd-MM-yyyy", System.Globalization.CultureInfo.InvariantCulture);
        page.CollectionCode = "6CE";
        Assert.True(FillChallan(page, "CIN-999"));

        var statType = vm.Company!.VoucherTypes.Single(t => t.IsStatPaymentType);
        var statVoucherId = vm.Company!.Vouchers.Single(v => v.TypeId == statType.Id).Id;

        // Reload the company fresh from storage — the challan + its fields + the voucher link survive the round-trip.
        var entry = _storage.ListCompanies().Single(e => e.Name == companyName);
        var reloaded = _storage.Load(entry);
        var challan = Assert.Single(reloaded.TcsChallans);
        Assert.Equal("CIN-999", challan.ChallanNo);
        Assert.Equal("0510308", challan.BsrCode);
        Assert.Equal("6CE", challan.CollectionCode);
        Assert.Equal("200", challan.MinorHead);
        Assert.Equal(depositDate, challan.DepositDate);
        Assert.Equal(Money.FromRupees(1_180m), challan.Amount);
        Assert.Contains(reloaded.TcsChallanVoucherLinks,
            link => link.ChallanId == challan.Id && link.VoucherId == statVoucherId);
    }

    // ---- validation guards ----

    [Fact]
    public void Deposit_rejected_without_a_challan_number()
    {
        var vm = TcsCompany("Guard Co");
        CollectElevenEighty(vm.Company!);

        var page = new TcsStatPaymentViewModel(vm.Company!, _storage);
        page.BsrCode = "0510308";
        page.ChallanNo = "   "; // blank
        Assert.False(page.Deposit());
        Assert.False(page.LastDepositSucceeded);
        Assert.NotNull(page.Message);
        Assert.Empty(vm.Company!.TcsChallans); // nothing posted / recorded
    }

    [Fact]
    public void Deposit_rejected_when_amount_exceeds_the_outstanding_payable()
    {
        var vm = TcsCompany("Over-Deposit Co");
        CollectElevenEighty(vm.Company!); // ₹1,180 accrued in "TCS Payable"

        var page = new TcsStatPaymentViewModel(vm.Company!, _storage);
        page.AmountText = "2000"; // deliberately more than the ₹1,180 owed (a single ledger — never over-deposit)
        Assert.False(FillChallan(page, "CIN-OVER"));
        Assert.False(page.LastDepositSucceeded);
        Assert.NotNull(page.Message);
        Assert.Empty(vm.Company!.TcsChallans);

        Assert.Equal(Money.FromRupees(1_180m),
            new TcsDepositService(vm.Company!).OutstandingPayable(page.AsOf));
    }

    // ---- (4) ER-13 gating: a non-TCS company is byte-identical (no screens, no menu entries) ----

    [Fact]
    public void Stat_payment_opener_is_a_no_op_on_a_non_tcs_company()
    {
        var vm = NewCompany("Plain Co");
        vm.ShowTcsStatPayment();
        Assert.Null(vm.TcsStatPayment);
        Assert.DoesNotContain(vm.Columns, col => col.IsPage);
    }

    [Fact]
    public void Tcs_recon_opener_is_a_no_op_on_a_non_tcs_company()
    {
        var vm = NewCompany("Plain Co 2");
        vm.OpenTcsChallanReconciliation();
        Assert.Null(vm.TcsChallanReconciliation);
        Assert.DoesNotContain(vm.Columns, col => col.IsPage);
    }

    [Fact]
    public void Form27eq_opener_is_a_no_op_on_a_non_tcs_company()
    {
        var vm = NewCompany("Plain Co 3");
        vm.OpenForm27EQ();
        Assert.Null(vm.Form27EQ);
        Assert.DoesNotContain(vm.Columns, col => col.IsPage);
    }

    [Fact]
    public void Menu_entries_appear_only_when_tcs_is_enabled()
    {
        // TCS on: the Vouchers column carries "TCS Stat Payment"; GST Reports carries the two TCS reports.
        var tcsVm = TcsCompany("Menu-On Co");
        tcsVm.ShowVouchersMenu();
        Assert.Contains(tcsVm.Columns[^1].Items, m => m.Label == "TCS Stat Payment");
        tcsVm.ShowGstReportsMenu();
        Assert.Contains(tcsVm.Columns[^1].Items, m => m.Label == "TCS Challan Reconciliation");
        Assert.Contains(tcsVm.Columns[^1].Items, m => m.Label == "Form 27EQ");

        // TCS off: none of the entries are present (ER-13, byte-identical menus).
        var plainVm = NewCompany("Menu-Off Co");
        plainVm.ShowVouchersMenu();
        Assert.DoesNotContain(plainVm.Columns[^1].Items, m => m.Label == "TCS Stat Payment");
        plainVm.ShowGstReportsMenu();
        Assert.DoesNotContain(plainVm.Columns[^1].Items, m => m.Label == "TCS Challan Reconciliation");
        Assert.DoesNotContain(plainVm.Columns[^1].Items, m => m.Label == "Form 27EQ");
    }

    // ---- the screens open + activate through the shell (no dead shortcuts) ----

    [Fact]
    public void Opening_the_screens_binds_them_as_page_columns()
    {
        var vm = TcsCompany("Open-Screens Co");
        CollectElevenEighty(vm.Company!);

        vm.ShowTcsStatPayment();
        Assert.NotNull(vm.TcsStatPayment);
        Assert.Equal(Screen.TcsStatPayment, vm.CurrentScreen);
        Assert.Same(vm.TcsStatPayment, vm.Columns[^1].TcsStatPayment);

        vm.OpenTcsChallanReconciliation();
        Assert.NotNull(vm.TcsChallanReconciliation);
        Assert.Equal(Screen.TcsChallanReconciliation, vm.CurrentScreen);
        Assert.Same(vm.TcsChallanReconciliation, vm.Columns[^1].TcsChallanReconciliation);
        Assert.True(vm.IsTcsChallanReconciliationScreen);

        vm.OpenForm27EQ();
        Assert.NotNull(vm.Form27EQ);
        Assert.Equal(Screen.Form27EQ, vm.CurrentScreen);
        Assert.Same(vm.Form27EQ, vm.Columns[^1].Form27EQ);
        Assert.True(vm.IsForm27EQScreen);
    }

    // ============================================================ Form 27EQ: screen == engine + FVU export

    private Company GoldenTcsCompany()
    {
        var vm = TcsCompany("Return Co");
        CollectElevenEighty(vm.Company!);
        // Deposit the ₹1,180 in Q1 so the challan block + control totals tally.
        Assert.True(FillChallan(new TcsStatPaymentViewModel(vm.Company!, _storage), "00777"));
        return vm.Company!;
    }

    [Fact]
    public void Form27eq_renders_collector_challan_collectee_and_control_totals()
    {
        var c = GoldenTcsCompany();
        var page = new Form27EQViewModel(c);
        page.SelectedYear = page.FinancialYears.Single(y => y.StartYear == c.FinancialYearStart.Year);
        page.SelectedQuarter = page.Quarters.Single(q => q.Quarter == 1);

        // Collector block from F11.
        Assert.Equal(ValidTan, page.CollectorTan);
        Assert.Equal("Company", page.CollectorType);
        Assert.Contains("A. Sharma", page.ResponsiblePerson);

        // Exactly one collectee row: ₹1,180 TCS on the ₹1,18,000 base, 6CE, 1%.
        var row = Assert.Single(page.Collectees);
        Assert.Equal(BuyerPan, row.Pan);
        Assert.Equal("Scrap Buyer", row.Name);
        Assert.Equal("6CE", row.CollectionCode);
        Assert.Equal("1.00", row.Rate);
        Assert.True(row.PanApplied);

        // Exactly one challan block.
        var ch = Assert.Single(page.Challans);
        Assert.Equal("00777", ch.ChallanNo);
        Assert.Equal("0510308", ch.BsrCode);
        Assert.Equal("6CE", ch.CollectionCode);
        Assert.Equal("1", ch.CollecteeCount);

        Assert.Equal("1", page.CollecteeRecordCount);
        Assert.Equal("1", page.ChallanRecordCount);
        Assert.True(page.ControlTotalsTally);
        Assert.False(page.IsEmpty);
        Assert.Contains("ready to export", page.StatusText);
    }

    [Fact]
    public void Form27eq_screen_figures_equal_the_engine_projection()
    {
        var c = GoldenTcsCompany();
        var fy = c.FinancialYearStart.Year;
        var page = new Form27EQViewModel(c);
        page.SelectedYear = page.FinancialYears.Single(y => y.StartYear == fy);
        page.SelectedQuarter = page.Quarters.Single(q => q.Quarter == 1);

        var engine = Form27EQ.Build(c, fy, 1);
        Assert.Equal(engine.Collectees.Count, page.Collectees.Count);
        Assert.Equal(engine.Challans.Count, page.Challans.Count);
        Assert.Equal(engine.Collector.Tan, page.CollectorTan);
        Assert.Equal(IndianFormat.AmountAlways(engine.TotalTcsCollected), page.TotalTcsCollected);
        Assert.Equal(IndianFormat.AmountAlways(engine.TotalAmountReceived), page.TotalAmountReceived);
        Assert.Equal(IndianFormat.AmountAlways(engine.TotalDepositedAsPerChallans), page.TotalDeposited);
        Assert.Equal(engine.ControlTotals.Tallies, page.ControlTotalsTally);
        // The VM holds the very same return the engine builds (no recompute).
        Assert.Equal(engine.TotalTcsCollected, page.Return.TotalTcsCollected);
    }

    [Fact]
    public void Form27eq_fvu_export_writes_the_flat_file_matching_the_writer_bytes()
    {
        var c = GoldenTcsCompany();
        var fy = c.FinancialYearStart.Year;
        var page = new Form27EQViewModel(c) { ExportFolder = _tempDir };
        page.SelectedYear = page.FinancialYears.Single(y => y.StartYear == fy);
        page.SelectedQuarter = page.Quarters.Single(q => q.Quarter == 1);

        string? capturedPath = null;
        byte[]? capturedBytes = null;
        var ok = page.ExportFvu((path, bytes) => { capturedPath = path; capturedBytes = bytes; });

        Assert.True(ok);
        Assert.NotNull(capturedPath);
        Assert.EndsWith(".txt", capturedPath);
        Assert.Contains($"Form27EQ_{fy}_{(fy + 1) % 100:00}_1", capturedPath);
        Assert.Equal(FvuWriter.Write(page.Return), capturedBytes);
        Assert.Contains("Exported", page.ExportStatus);
    }

    [Fact]
    public void Form27eq_fvu_export_to_real_folder_creates_a_file_on_disk()
    {
        var c = GoldenTcsCompany();
        var fy = c.FinancialYearStart.Year;
        var page = new Form27EQViewModel(c) { ExportFolder = _tempDir };
        page.SelectedYear = page.FinancialYears.Single(y => y.StartYear == fy);
        page.SelectedQuarter = page.Quarters.Single(q => q.Quarter == 1);

        Assert.True(page.ExportFvu());
        var expected = Path.Combine(_tempDir, $"Form27EQ_{fy}_{(fy + 1) % 100:00}_1.txt");
        Assert.True(File.Exists(expected));
        Assert.Equal(FvuWriter.Write(page.Return), File.ReadAllBytes(expected));
    }

    [Fact]
    public void Form27eq_empty_quarter_renders_nothing_to_file_and_still_exports()
    {
        var c = TcsCompany("Empty Co").Company!;
        var page = new Form27EQViewModel(c);
        page.SelectedYear = page.FinancialYears.Single(y => y.StartYear == c.FinancialYearStart.Year);
        page.SelectedQuarter = page.Quarters.Single(q => q.Quarter == 3); // no collections booked

        Assert.True(page.IsEmpty);
        Assert.Empty(page.Collectees);
        Assert.Empty(page.Challans);
        Assert.Contains("Nothing to file", page.StatusText);

        byte[]? bytes = null;
        Assert.True(page.ExportFvu((_, b) => bytes = b));
        Assert.NotNull(bytes);
        Assert.NotEmpty(bytes);
    }

    [Fact]
    public void Form27eq_alt_b_save_return_writes_the_file_and_pops_back()
    {
        var vm = TcsCompany("Save Return Co");
        CollectElevenEighty(vm.Company!);
        vm.OpenForm27EQ();
        Assert.Equal(Screen.Form27EQ, vm.CurrentScreen);

        vm.Form27EQ!.ExportFolder = _tempDir;
        var fileName = vm.Form27EQ.ExportResolvedFileName;
        vm.SaveReturnForm27EQ();

        Assert.NotEqual(Screen.Form27EQ, vm.CurrentScreen); // returned to the menu
        Assert.Null(vm.Form27EQ);
        Assert.True(File.Exists(Path.Combine(_tempDir, fileName)));
    }
}
