using System;
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
/// Phase 7 slice 7 — the <b>Form 16A</b> TDS-certificate page (<see cref="Form16AViewModel"/>) surfaced in the
/// cascade. Proves the page projects the deductor / deductee / deduction / challan blocks + totals off the pure
/// <see cref="Form16A"/> engine (screen figures == engine, nothing recomputed in the VM); that its export renders the
/// byte-stable certificate PDF <b>straight off the engine</b> (bytes == <see cref="Form16APdf"/>, ER-4) to the export
/// folder; that the "Form 16A" report is gated on TDS (present + reachable only when enabled — ER-13) and
/// opens/returns through the shell; and that Alt+B save-return writes the PDF and pops back. Drives the real VMs
/// headlessly — no UI toolkit.
/// </summary>
public sealed class Form16AViewModelTests : IDisposable
{
    private const string ValidTan = "MUMA12345B";
    private const string DeducteePan = "AAPFU0939F";
    private static readonly DateOnly FyStart = new(2025, 4, 1);

    private readonly string _tempDir;
    private readonly CompanyStorage _storage;

    public Form16AViewModelTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "ApexForm16AVmTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _storage = new CompanyStorage(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { /* best-effort */ }
    }

    // ---- direct-engine company with one 194J deduction + its stat-payment challan (mirrors Form16ATests) ----

    private static Company NewTdsCompany()
    {
        var c = CompanyFactory.CreateSeeded("Return Co", FyStart);
        new TdsTcsService(c).EnableTds(new TdsConfig
        {
            Tan = ValidTan, DeductorType = DeductorType.Company,
            ResponsiblePersonName = "A. Sharma", ResponsiblePersonPan = DeducteePan,
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

    private static Domain.Ledger Vendor(Company c, string name = "Consultant")
    {
        var nop = c.FindNatureOfPaymentByCode("194J(b)")!;
        var v = AddLedger(c, name, "Sundry Creditors", false);
        v.TdsApplicable = true; v.TdsNatureOfPaymentId = nop.Id; v.DeducteeType = DeducteeType.Firm; v.PartyPan = DeducteePan;
        return v;
    }

    private static Guid JournalTypeId(Company c) => c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Journal).Id;

    private static void BookDeduction(Company c, DateOnly on, Domain.Ledger vendor)
    {
        var fees = AddLedger(c, $"Professional Fees {on:yyyyMMdd}", "Indirect Expenses", true);
        var nop = c.FindNatureOfPaymentByCode("194J(b)")!;
        var gross = Money.FromRupees(1_00_000m);
        var carve = new TdsService(c).BuildCarveOut(gross, gross, nop, vendor, on);
        new LedgerService(c).Post(new Voucher(Guid.NewGuid(), JournalTypeId(c), on,
            new[] { new EntryLine(fees.Id, gross, DrCr.Debit), carve.PartyLine, carve.TdsPayableLine! }));
    }

    private static void Deposit(Company c, Money amount, DateOnly on, string challanNo)
    {
        var dep = new TdsDepositService(c);
        var statType = dep.EnsureStatPaymentType();
        var bank = c.FindLedgerByName("HDFC Bank") ?? AddLedger(c, "HDFC Bank", "Bank Accounts", true);
        var posted = new LedgerService(c).Post(dep.BuildStatPayment(amount, bank, on, statType));
        dep.RecordChallan(challanNo, "0510308", on, amount, "194J(b)", "200", posted);
    }

    private static (Company, Domain.Ledger) GoldenCompany()
    {
        var c = NewTdsCompany();
        var vendor = Vendor(c);
        BookDeduction(c, new DateOnly(2025, 5, 10), vendor);
        Deposit(c, Money.FromRupees(10_000m), new DateOnly(2025, 6, 5), "00123");
        return (c, vendor);
    }

    private static Form16AViewModel Q1Vm(Company c)
    {
        var vm = new Form16AViewModel(c);
        vm.SelectedYear = vm.FinancialYears.Single(y => y.StartYear == 2025);
        vm.SelectedQuarter = vm.Quarters.Single(q => q.Quarter == 1);
        return vm;
    }

    // ============================================================ (1) renders blocks + rows + totals

    [Fact]
    public void Renders_deductor_deductee_deduction_and_challan_blocks()
    {
        var (c, _) = GoldenCompany();
        var vm = Q1Vm(c);

        Assert.Equal(ValidTan, vm.DeductorTan);
        Assert.Equal("Return Co", vm.DeductorName);
        Assert.Contains("A. Sharma", vm.ResponsiblePerson);

        var deductee = Assert.Single(vm.Deductees);
        Assert.Equal(DeducteePan, deductee.Pan);
        Assert.Equal("Consultant", deductee.Name);

        Assert.Equal("Consultant", vm.DeducteeName);
        Assert.Equal(DeducteePan, vm.DeducteePan);

        var row = Assert.Single(vm.Deductions);
        Assert.Equal("194J(b)", row.Section);
        Assert.Equal("94J-B", row.FvuCode);
        Assert.Equal("10.00", row.Rate);

        var ch = Assert.Single(vm.Challans);
        Assert.Equal("00123", ch.ChallanNo);
        Assert.Equal("0510308", ch.BsrCode);

        Assert.False(vm.IsEmpty);
        Assert.NotEqual(string.Empty, vm.AmountInWords);
    }

    // ============================================================ (2) screen figures == engine

    [Fact]
    public void Screen_figures_equal_the_engine_projection()
    {
        var (c, vendor) = GoldenCompany();
        var vm = Q1Vm(c);

        var engine = Form16A.Build(c, 2025, 1, vendor.Id);

        Assert.NotNull(vm.Certificate);
        Assert.Equal(engine.Deductions.Count, vm.Deductions.Count);
        Assert.Equal(engine.Challans.Count, vm.Challans.Count);
        Assert.Equal(engine.Deductor.Tan, vm.DeductorTan);
        Assert.Equal(engine.Deductee.Pan, vm.DeducteePan);
        Assert.Equal(IndianFormat.AmountAlways(engine.TotalAmountPaid), vm.TotalAmountPaid);
        Assert.Equal(IndianFormat.AmountAlways(engine.TotalTdsDeducted), vm.TotalTdsDeducted);
        Assert.Equal(IndianFormat.AmountAlways(engine.TotalTdsDeposited), vm.TotalTdsDeposited);
        // The VM holds the very same certificate the engine builds (no recompute).
        Assert.Equal(engine.TotalTdsDeducted, vm.Certificate!.TotalTdsDeducted);
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
        Assert.Contains("Form16A_2025_26_Q1_" + DeducteePan, capturedPath);
        // Byte-stable + straight off the engine: exactly what Form16APdf renders for this certificate.
        var expected = Form16APdf.Render(vm.Certificate!, CertificatePages.Build(c.Name));
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
        Assert.Equal(Form16APdf.Render(vm.Certificate!, CertificatePages.Build(c.Name)), File.ReadAllBytes(expected));
    }

    [Fact]
    public void Exported_certificate_pdf_never_renders_the_word_tally()
    {
        var (c, _) = GoldenCompany();
        var vm = Q1Vm(c);
        byte[]? bytes = null;
        Assert.True(vm.ExportPdf((_, b) => bytes = b));
        Assert.NotNull(bytes);
        var text = System.Text.Encoding.Latin1.GetString(bytes!);
        Assert.DoesNotContain("Tally", text, StringComparison.OrdinalIgnoreCase);
    }

    // ============================================================ (4) empty quarter → nothing to certify, no crash

    [Fact]
    public void Empty_quarter_has_no_deductees_and_export_is_a_friendly_no_op()
    {
        var vm = new Form16AViewModel(NewTdsCompany());
        vm.SelectedYear = vm.FinancialYears.Single(y => y.StartYear == 2025);
        vm.SelectedQuarter = vm.Quarters.Single(q => q.Quarter == 3); // no deductions booked

        Assert.Empty(vm.Deductees);
        Assert.Null(vm.Certificate);
        Assert.True(vm.IsEmpty);

        Assert.False(vm.ExportPdf());          // nothing to certify — a friendly no-op, no throw
        Assert.Contains("Pick a deductee", vm.ExportStatus);
    }

    // ============================================================ (5) shell gating (ER-13) + open/return

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

    private static void EnableTds(MainWindowViewModel vm) =>
        new TdsTcsService(vm.Company!).EnableTds(new TdsConfig
        {
            Tan = ValidTan, DeductorType = DeductorType.Company,
            ResponsiblePersonName = "A. Sharma", ResponsiblePersonPan = DeducteePan,
            ResponsiblePersonDesignation = "Director", ResponsiblePersonAddress = "12 MG Road",
        });

    [Fact]
    public void Form16a_menu_item_is_gated_on_tds_and_opens_the_page()
    {
        var vm = NewShellCompany("Gated Co");

        // TDS OFF: the GST-Reports column has no "Form 16A" item, and OpenForm16A is a no-op (ER-13).
        vm.ShowGstReportsMenu();
        Assert.DoesNotContain("Form 16A", vm.Menu.Select(m => m.Label));
        vm.OpenForm16A();
        Assert.NotEqual(Screen.Form16A, vm.CurrentScreen);
        Assert.Null(vm.Form16A);

        // TDS ON: the item appears and the page opens + binds as a page column.
        EnableTds(vm);
        vm.ShowGstReportsMenu();
        Assert.Contains("Form 16A", vm.Menu.Select(m => m.Label));

        vm.OpenForm16A();
        Assert.Equal(Screen.Form16A, vm.CurrentScreen);
        Assert.NotNull(vm.Form16A);
        Assert.Same(vm.Form16A, vm.Columns[^1].Form16A);
        Assert.True(vm.IsForm16AScreen);
        Assert.False(vm.IsMenuScreen);
    }

    [Fact]
    public void Alt_b_save_return_writes_the_pdf_and_pops_back()
    {
        var vm = NewShellCompany("Save Return Co");
        EnableTds(vm);
        // Book a deduction on the shell's own company (inside its financial year) so the certificate has content.
        var c = vm.Company!;
        var fy = c.FinancialYearStart.Year;
        var vendor = Vendor(c);
        BookDeduction(c, c.FinancialYearStart.AddMonths(1).AddDays(9), vendor);

        vm.OpenForm16A();
        Assert.Equal(Screen.Form16A, vm.CurrentScreen);

        vm.Form16A!.SelectedYear = vm.Form16A.FinancialYears.Single(y => y.StartYear == fy);
        vm.Form16A.SelectedQuarter = vm.Form16A.Quarters.Single(q => q.Quarter == 1);
        vm.Form16A.ExportFolder = _tempDir;
        var fileName = vm.Form16A.ExportResolvedFileName;

        vm.SaveReturnForm16A();

        Assert.NotEqual(Screen.Form16A, vm.CurrentScreen); // returned to the menu
        Assert.Null(vm.Form16A);
        Assert.True(File.Exists(Path.Combine(_tempDir, fileName))); // the certificate PDF was saved
    }
}
