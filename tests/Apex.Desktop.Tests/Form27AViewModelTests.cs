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
/// Phase 7 slice 7 — the <b>Form 27A</b> return-control-chart page (<see cref="Form27AViewModel"/>). Proves the page
/// projects the control totals + FVU-style tally status off the pure <see cref="Form27A"/> engine (screen figures ==
/// engine); that its export renders the byte-stable chart PDF straight off <see cref="Form27APdf"/> (bytes == engine,
/// ER-4); that the returns list is gated on the enabled taxes (ER-13) and opens/returns through the shell; that the
/// tally verdict reads "AGREE" and the chart never renders the word "Tally"; and that Alt+B save-return writes the PDF
/// and pops back. Headless.
/// </summary>
public sealed class Form27AViewModelTests : IDisposable
{
    private const string ValidTan = "MUMA12345B";
    private const string DeducteePan = "AAPFU0939F";
    private static readonly DateOnly FyStart = new(2025, 4, 1);

    private readonly string _tempDir;
    private readonly CompanyStorage _storage;

    public Form27AViewModelTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "ApexForm27AVmTests_" + Guid.NewGuid().ToString("N"));
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

    private static void EnableTds(Company c) =>
        new TdsTcsService(c).EnableTds(new TdsConfig
        {
            Tan = ValidTan, DeductorType = DeductorType.Company,
            ResponsiblePersonName = "A. Sharma", ResponsiblePersonPan = DeducteePan,
            ResponsiblePersonDesignation = "Director", ResponsiblePersonAddress = "12 MG Road",
        });

    private static Domain.Ledger Vendor(Company c)
    {
        var nop = c.FindNatureOfPaymentByCode("194J(b)")!;
        var v = AddLedger(c, "Consultant", "Sundry Creditors", false);
        v.TdsApplicable = true; v.TdsNatureOfPaymentId = nop.Id; v.DeducteeType = DeducteeType.Firm; v.PartyPan = DeducteePan;
        return v;
    }

    private static void BookDeduction(Company c, DateOnly on, Domain.Ledger vendor)
    {
        var fees = AddLedger(c, $"Professional Fees {on:yyyyMMdd}", "Indirect Expenses", true);
        var nop = c.FindNatureOfPaymentByCode("194J(b)")!;
        var gross = Money.FromRupees(1_00_000m);
        var carve = new TdsService(c).BuildCarveOut(gross, gross, nop, vendor, on);
        new LedgerService(c).Post(new Voucher(Guid.NewGuid(),
            c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Journal).Id, on,
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

    /// <summary>A TDS company whose Q1 26Q return control-totals AGREE (deduction fully deposited).</summary>
    private static Company GoldenTdsCompany()
    {
        var c = CompanyFactory.CreateSeeded("Return Co", FyStart);
        EnableTds(c);
        var vendor = Vendor(c);
        BookDeduction(c, new DateOnly(2025, 5, 10), vendor);
        Deposit(c, Money.FromRupees(10_000m), new DateOnly(2025, 6, 5), "00123");
        return c;
    }

    private static Form27AViewModel Q1Vm(Company c, string form = "26Q")
    {
        var vm = new Form27AViewModel(c, form);
        vm.SelectedYear = vm.FinancialYears.Single(y => y.StartYear == 2025);
        vm.SelectedQuarter = vm.Quarters.Single(q => q.Quarter == 1);
        return vm;
    }

    // ============================================================ (1) control totals == engine + AGREE

    [Fact]
    public void Control_totals_and_tally_status_equal_the_engine()
    {
        var c = GoldenTdsCompany();
        var vm = Q1Vm(c);

        var engine = Form27A.FromForm26Q(Form26Q.Build(c, 2025, 1));

        Assert.NotNull(vm.Chart);
        Assert.Equal("Form 26Q", vm.ReturnFormName);
        Assert.Equal(ValidTan, vm.Tan);
        Assert.Equal(engine.Tallies, vm.Tallies);
        Assert.True(vm.Tallies);
        Assert.Contains("AGREE", vm.StatusText);
        Assert.Empty(vm.ValidationMessages);

        // The headline TDS total surfaces in the control-total rows verbatim off the engine.
        Assert.Contains(vm.ControlTotals, r => r.Value == IndianFormat.AmountAlways(engine.TotalTax) && r.IsHeadline);
        Assert.Equal(engine.TotalTax, vm.Chart!.TotalTax);
    }

    // ============================================================ (2) export PDF bytes == engine (ER-4)

    [Fact]
    public void Export_writes_the_chart_pdf_matching_the_engine_bytes()
    {
        var c = GoldenTdsCompany();
        var vm = Q1Vm(c);
        vm.ExportFolder = _tempDir;

        string? capturedPath = null;
        byte[]? capturedBytes = null;
        var ok = vm.ExportPdf((path, bytes) => { capturedPath = path; capturedBytes = bytes; });

        Assert.True(ok);
        Assert.NotNull(capturedPath);
        Assert.EndsWith(".pdf", capturedPath);
        Assert.Contains("Form27A_26Q_2025_26_Q1", capturedPath);
        var expected = Form27APdf.Render(vm.Chart!, CertificatePages.Build(c.Name));
        Assert.Equal(expected, capturedBytes);
        Assert.Contains("Exported", vm.ExportStatus);
    }

    [Fact]
    public void Export_to_real_folder_creates_the_pdf_on_disk()
    {
        var c = GoldenTdsCompany();
        var vm = Q1Vm(c);
        vm.ExportFolder = _tempDir;

        Assert.True(vm.ExportPdf());
        var expected = Path.Combine(_tempDir, vm.ExportResolvedFileName);
        Assert.True(File.Exists(expected));
        Assert.Equal(Form27APdf.Render(vm.Chart!, CertificatePages.Build(c.Name)), File.ReadAllBytes(expected));
    }

    [Fact]
    public void Exported_chart_pdf_never_renders_the_word_tally()
    {
        var c = GoldenTdsCompany();
        var vm = Q1Vm(c);
        byte[]? bytes = null;
        Assert.True(vm.ExportPdf((_, b) => bytes = b));
        Assert.NotNull(bytes);
        var text = System.Text.Encoding.Latin1.GetString(bytes!);
        Assert.DoesNotContain("Tally", text, StringComparison.OrdinalIgnoreCase);
    }

    // ============================================================ (3) shell gating (ER-13) + open/return

    private MainWindowViewModel NewShellCompany(string name)
    {
        var vm = new MainWindowViewModel(_storage);
        vm.NewCompanyName = name;
        vm.CreateCompany();
        Assert.Equal(Screen.Gateway, vm.CurrentScreen);
        return vm;
    }

    [Fact]
    public void Form27a_tds_menu_item_is_gated_on_tds_and_opens_the_page()
    {
        var vm = NewShellCompany("Gated Co");

        // Neither tax enabled: no "Form 27A (TDS)" item, and OpenForm27A is a no-op (ER-13).
        vm.ShowGstReportsMenu();
        Assert.DoesNotContain("Form 27A (TDS)", vm.Menu.Select(m => m.Label));
        vm.OpenForm27A("26Q");
        Assert.NotEqual(Screen.Form27A, vm.CurrentScreen);
        Assert.Null(vm.Form27A);

        // TDS ON: the TDS control-chart item appears and the page opens pre-selected to 26Q.
        EnableTds(vm.Company!);
        vm.ShowGstReportsMenu();
        Assert.Contains("Form 27A (TDS)", vm.Menu.Select(m => m.Label));

        vm.OpenForm27A("26Q");
        Assert.Equal(Screen.Form27A, vm.CurrentScreen);
        Assert.NotNull(vm.Form27A);
        Assert.Same(vm.Form27A, vm.Columns[^1].Form27A);
        Assert.True(vm.IsForm27AScreen);
        Assert.Equal("26Q", vm.Form27A!.SelectedReturn!.FormCode);
    }

    [Fact]
    public void Alt_b_save_return_writes_the_pdf_and_pops_back()
    {
        var vm = NewShellCompany("Save Return Co");
        EnableTds(vm.Company!);
        var c = vm.Company!;
        var fy = c.FinancialYearStart.Year;
        var vendor = Vendor(c);
        BookDeduction(c, c.FinancialYearStart.AddMonths(1).AddDays(9), vendor);

        vm.OpenForm27A("26Q");
        Assert.Equal(Screen.Form27A, vm.CurrentScreen);

        vm.Form27A!.SelectedYear = vm.Form27A.FinancialYears.Single(y => y.StartYear == fy);
        vm.Form27A.SelectedQuarter = vm.Form27A.Quarters.Single(q => q.Quarter == 1);
        vm.Form27A.ExportFolder = _tempDir;
        var fileName = vm.Form27A.ExportResolvedFileName;

        vm.SaveReturnForm27A();

        Assert.NotEqual(Screen.Form27A, vm.CurrentScreen);
        Assert.Null(vm.Form27A);
        Assert.True(File.Exists(Path.Combine(_tempDir, fileName)));
    }
}
