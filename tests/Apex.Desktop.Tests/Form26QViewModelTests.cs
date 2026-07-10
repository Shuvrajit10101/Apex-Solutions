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
/// Phase 7 slice 4 — the <b>Form 26Q</b> return report page (<see cref="Form26QViewModel"/>) surfaced in the
/// cascade. Proves the page renders the deductor / challan / deductee blocks + control totals off the pure
/// <see cref="Form26Q"/> engine (screen figures == engine, nothing recomputed in the VM); that the FVU export action
/// writes the byte-stable <see cref="FvuWriter"/> flat file to the export folder; that the "Form 26Q" report is
/// gated on TDS (present only when enabled — ER-13) and opens/returns through the shell; and that the Alt+B
/// save-return writes the file and pops back. Drives the real VMs headlessly — no UI toolkit.
/// </summary>
public sealed class Form26QViewModelTests : IDisposable
{
    private const string ValidTan = "MUMA12345B";
    private const string DeducteePan = "AAPFU0939F";
    private static readonly DateOnly FyStart = new(2025, 4, 1);

    private readonly string _tempDir;
    private readonly CompanyStorage _storage;

    public Form26QViewModelTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "ApexForm26QTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _storage = new CompanyStorage(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { /* best-effort */ }
    }

    // ---- direct-engine company with one 194J deduction + its stat-payment challan (mirrors Form26QTests) ----

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

    private static Domain.Ledger Vendor(Company c)
    {
        var nop = c.FindNatureOfPaymentByCode("194J(b)")!;
        var v = AddLedger(c, "Consultant", "Sundry Creditors", false);
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

    private static Company GoldenCompany()
    {
        var c = NewTdsCompany();
        var vendor = Vendor(c);
        BookDeduction(c, new DateOnly(2025, 5, 10), vendor);              // Q1 deduction
        Deposit(c, Money.FromRupees(10_000m), new DateOnly(2025, 6, 5), "00123"); // deposited in Q1
        return c;
    }

    // ============================================================ (1) renders blocks + rows + totals

    [Fact]
    public void Renders_deductor_challan_deductee_and_control_totals()
    {
        var vm = new Form26QViewModel(GoldenCompany());
        // Default selection is the company FY (2025-26) + Q1 — pick explicitly for determinism.
        vm.SelectedYear = vm.FinancialYears.Single(y => y.StartYear == 2025);
        vm.SelectedQuarter = vm.Quarters.Single(q => q.Quarter == 1);

        // Deductor block from F11.
        Assert.Equal(ValidTan, vm.DeductorTan);
        Assert.Equal("Company", vm.DeductorType);
        Assert.Contains("A. Sharma", vm.ResponsiblePerson);

        // Exactly one deductee row: ₹10,000 TDS on ₹1,00,000, FVU 94J-B, 10%.
        var row = Assert.Single(vm.Deductees);
        Assert.Equal(DeducteePan, row.Pan);
        Assert.Equal("Consultant", row.Name);
        Assert.Equal("194J(b)", row.Section);
        Assert.Equal("94J-B", row.FvuCode);
        Assert.Equal("10.00", row.Rate);
        Assert.True(row.PanApplied);

        // Exactly one challan block.
        var ch = Assert.Single(vm.Challans);
        Assert.Equal("00123", ch.ChallanNo);
        Assert.Equal("0510308", ch.BsrCode);
        Assert.Equal("194J(b)", ch.Section);
        Assert.Equal("1", ch.DeducteeCount);

        // Control totals (Form-27A style) tally to the engine.
        Assert.Equal("1", vm.DeducteeRecordCount);
        Assert.Equal("1", vm.ChallanRecordCount);
        Assert.True(vm.ControlTotalsTally);
        Assert.False(vm.IsEmpty);
        Assert.Contains("ready to export", vm.StatusText);
    }

    // ============================================================ (2) screen figures == engine

    [Fact]
    public void Screen_figures_equal_the_engine_projection()
    {
        var c = GoldenCompany();
        var vm = new Form26QViewModel(c);
        vm.SelectedYear = vm.FinancialYears.Single(y => y.StartYear == 2025);
        vm.SelectedQuarter = vm.Quarters.Single(q => q.Quarter == 1);

        var engine = Form26Q.Build(c, 2025, 1);

        Assert.Equal(engine.Deductees.Count, vm.Deductees.Count);
        Assert.Equal(engine.Challans.Count, vm.Challans.Count);
        Assert.Equal(engine.Deductor.Tan, vm.DeductorTan);
        Assert.Equal(IndianFormat.AmountAlways(engine.TotalTdsDeducted), vm.TotalTdsDeducted);
        Assert.Equal(IndianFormat.AmountAlways(engine.TotalAmountPaid), vm.TotalAmountPaid);
        Assert.Equal(IndianFormat.AmountAlways(engine.TotalDepositedAsPerChallans), vm.TotalDeposited);
        Assert.Equal(engine.ControlTotals.Tallies, vm.ControlTotalsTally);
        // The VM holds the very same return the engine builds (no recompute).
        Assert.Equal(engine.TotalTdsDeducted, vm.Return.TotalTdsDeducted);
    }

    // ============================================================ (3) FVU export produces the file

    [Fact]
    public void Fvu_export_writes_the_flat_file_matching_the_writer_bytes()
    {
        var c = GoldenCompany();
        var vm = new Form26QViewModel(c) { ExportFolder = _tempDir };
        vm.SelectedYear = vm.FinancialYears.Single(y => y.StartYear == 2025);
        vm.SelectedQuarter = vm.Quarters.Single(q => q.Quarter == 1);

        string? capturedPath = null;
        byte[]? capturedBytes = null;
        var ok = vm.ExportFvu((path, bytes) => { capturedPath = path; capturedBytes = bytes; });

        Assert.True(ok);
        Assert.NotNull(capturedPath);
        Assert.EndsWith(".txt", capturedPath);
        Assert.Contains("Form26Q_2025_26_1", capturedPath);
        // Byte-stable: exactly what the deterministic FvuWriter emits for this return.
        Assert.Equal(FvuWriter.Write(vm.Return), capturedBytes);
        Assert.Contains("Exported", vm.ExportStatus);
    }

    [Fact]
    public void Fvu_export_to_real_folder_creates_a_file_on_disk()
    {
        var c = GoldenCompany();
        var vm = new Form26QViewModel(c) { ExportFolder = _tempDir };
        vm.SelectedYear = vm.FinancialYears.Single(y => y.StartYear == 2025);
        vm.SelectedQuarter = vm.Quarters.Single(q => q.Quarter == 1);

        Assert.True(vm.ExportFvu());
        var expected = Path.Combine(_tempDir, "Form26Q_2025_26_1.txt");
        Assert.True(File.Exists(expected));
        Assert.Equal(FvuWriter.Write(vm.Return), File.ReadAllBytes(expected));
    }

    // ============================================================ (4) empty quarter → header-only, no crash

    [Fact]
    public void Empty_quarter_renders_nothing_to_file_and_still_exports()
    {
        var vm = new Form26QViewModel(NewTdsCompany());
        vm.SelectedYear = vm.FinancialYears.Single(y => y.StartYear == 2025);
        vm.SelectedQuarter = vm.Quarters.Single(q => q.Quarter == 3); // no deductions booked

        Assert.True(vm.IsEmpty);
        Assert.Empty(vm.Deductees);
        Assert.Empty(vm.Challans);
        Assert.Contains("Nothing to file", vm.StatusText);

        byte[]? bytes = null;
        Assert.True(vm.ExportFvu((_, b) => bytes = b));
        Assert.NotNull(bytes);          // a valid header-only FVU file is still produced
        Assert.NotEmpty(bytes);
    }

    // ============================================================ (5) shell gating (ER-13) + open/return

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

    [Fact]
    public void Form26q_menu_item_is_gated_on_tds_and_opens_the_page()
    {
        var vm = NewShellCompany("Gated Co");

        // TDS OFF: the GST-Reports column has no "Form 26Q" item, and OpenForm26Q is a no-op (ER-13).
        vm.ShowGstReportsMenu();
        Assert.DoesNotContain("Form 26Q", vm.Menu.Select(m => m.Label));
        vm.OpenForm26Q();
        Assert.NotEqual(Screen.Form26Q, vm.CurrentScreen);

        // TDS ON: the item appears and the page opens.
        EnableTds(vm);
        vm.ShowGstReportsMenu();
        Assert.Contains("Form 26Q", vm.Menu.Select(m => m.Label));

        vm.OpenForm26Q();
        Assert.Equal(Screen.Form26Q, vm.CurrentScreen);
        Assert.NotNull(vm.Form26Q);
        Assert.False(vm.IsMenuScreen);
    }

    [Fact]
    public void Alt_b_save_return_writes_the_file_and_pops_back()
    {
        var vm = NewShellCompany("Save Return Co");
        EnableTds(vm);
        vm.OpenForm26Q();
        Assert.Equal(Screen.Form26Q, vm.CurrentScreen);

        // Point the export at the temp dir so the real save-return write lands there, then Alt+B.
        vm.Form26Q!.ExportFolder = _tempDir;
        var fileName = vm.Form26Q.ExportResolvedFileName;
        vm.SaveReturnForm26Q();

        Assert.NotEqual(Screen.Form26Q, vm.CurrentScreen);          // returned to the menu
        Assert.Null(vm.Form26Q);
        Assert.True(File.Exists(Path.Combine(_tempDir, fileName))); // the FVU file was saved
    }

    // ============================================================ (6) FY + quarter selector reprojects

    [Fact]
    public void Changing_quarter_reprojects_the_return()
    {
        var c = NewTdsCompany();
        var vendor = Vendor(c);
        BookDeduction(c, new DateOnly(2025, 5, 10), vendor);   // Q1
        BookDeduction(c, new DateOnly(2025, 8, 20), vendor);   // Q2

        var vm = new Form26QViewModel(c);
        vm.SelectedYear = vm.FinancialYears.Single(y => y.StartYear == 2025);

        vm.SelectedQuarter = vm.Quarters.Single(q => q.Quarter == 1);
        Assert.Single(vm.Deductees);
        Assert.Equal(new DateOnly(2025, 5, 10),
            Form26Q.Build(c, 2025, 1).Deductees.Single().DeductionDate);

        vm.SelectedQuarter = vm.Quarters.Single(q => q.Quarter == 2);
        Assert.Single(vm.Deductees);
        Assert.Equal("Form26Q_2025_26_2.txt", vm.ExportResolvedFileName);
    }
}
