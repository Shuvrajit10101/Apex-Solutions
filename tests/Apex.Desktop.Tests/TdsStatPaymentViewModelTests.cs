using System;
using System.Globalization;
using System.IO;
using System.Linq;
using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Services;
using Apex.Desktop.Services;
using Apex.Desktop.ViewModels;
using Xunit;
using DomainLedger = Apex.Ledger.Domain.Ledger;

namespace Apex.Desktop.Tests;

/// <summary>
/// Phase 7 slice 3 — the TDS <b>Stat Payment</b> (deposit) UI (<see cref="TdsStatPaymentViewModel"/>) and the
/// <b>Challan Reconciliation</b> report UI (<see cref="ChallanReconciliationViewModel"/>). Proves the deposit page
/// discharges the accrued "TDS Payable" back to zero through the engine and records the ITNS-281 challan (persisted
/// with its fields + voucher link), that the reconciliation renders and matches deposits to deductions per section,
/// and that both are gated so a non-TDS company is byte-identical (ER-13): the menu entries hide and the openers are
/// no-ops.
/// </summary>
public sealed class TdsStatPaymentViewModelTests : IDisposable
{
    private const string ValidTan = "MUMA12345B";
    private const string DeducteePan = "AAPFU0939F";

    private readonly string _tempDir;
    private readonly CompanyStorage _storage;

    public TdsStatPaymentViewModelTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "ApexTdsStatPayTests_" + Guid.NewGuid().ToString("N"));
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

    /// <summary>A TDS-enabled company with a 194J(b) expense ledger + a deductee vendor (mirrors the S2 setup).</summary>
    private (MainWindowViewModel Vm, DomainLedger Fees, DomainLedger Vendor) TdsCompany(string name)
    {
        var vm = NewCompany(name);
        var c = vm.Company!;
        new TdsTcsService(c).EnableTds(new TdsConfig { Tan = ValidTan });
        var fees = AddLedger(c, "Professional Fees", "Indirect Expenses", true);
        var vendor = AddLedger(c, "Acme Consultants", "Sundry Creditors", false);
        var nop = c.FindNatureOfPaymentByCode("194J(b)")!;
        fees.TdsApplicable = true;
        fees.TdsNatureOfPaymentId = nop.Id;
        vendor.DeducteeType = DeducteeType.Firm;
        vendor.PartyPan = DeducteePan;
        return (vm, fees, vendor);
    }

    /// <summary>Posts a 194J(b) withholding: Dr Fees 1,00,000 / Cr Vendor 90,000 / Cr TDS Payable 10,000.</summary>
    private static void PostWithholding(MainWindowViewModel vm, DomainLedger fees, DomainLedger vendor)
    {
        vm.OpenVoucher(VoucherBaseType.Journal);
        var e = vm.VoucherEntry!;
        e.Lines[0].SelectedLedger = fees; e.Lines[0].Side = DrCr.Debit; e.Lines[0].AmountText = "100000";
        e.Lines[1].SelectedLedger = vendor; e.Lines[1].Side = DrCr.Credit; e.Lines[1].AmountText = "100000";
        e.Recalculate();
        Assert.True(e.ShowTdsPanel);
        Assert.True(e.Accept()); // onSaved returns the shell to the Gateway
    }

    private static bool FillChallan(TdsStatPaymentViewModel p, string challanNo)
    {
        p.ChallanNo = challanNo;
        p.BsrCode = "0510308";
        p.MinorHead = "200";
        return p.Deposit();
    }

    // ---- (1) stat payment zeroes the payable in the view ----

    [Fact]
    public void Stat_payment_zeroes_payable_in_the_view()
    {
        var (vm, fees, vendor) = TdsCompany("Deposit Co");
        PostWithholding(vm, fees, vendor);

        var page = new TdsStatPaymentViewModel(vm.Company!, _storage);
        // Opening the page defaults to the 194J(b) section with its full ₹10,000 remaining.
        Assert.Equal("10,000.00", page.OutstandingText);
        Assert.NotNull(page.SelectedSection);
        Assert.Equal("194J(b)", page.SelectedSection!.Section);
        Assert.Equal("10000.00", page.AmountText);
        Assert.Equal("194J(b)", page.SectionCode);
        Assert.NotNull(page.SelectedBank);

        Assert.True(FillChallan(page, "CIN-001"));
        Assert.True(page.LastDepositSucceeded);
        // The payable is now discharged — the outstanding shown in the view is zero.
        Assert.Equal("0.00", page.OutstandingText);
        Assert.Empty(page.SectionOptions);
    }

    [Fact]
    public void Stat_payment_posts_dr_payable_cr_bank_and_reduces_the_ledger_to_zero()
    {
        var (vm, fees, vendor) = TdsCompany("Ledger-Zero Co");
        PostWithholding(vm, fees, vendor);
        var c = vm.Company!;
        var payable = new TdsService(c).RequirePayableLedger();
        var cash = c.FindLedgerByName("Cash")!;

        var page = new TdsStatPaymentViewModel(c, _storage);
        page.SelectedBank = cash;
        Assert.True(FillChallan(page, "CIN-002"));

        // A Stat-Payment voucher was posted: Dr TDS Payable 10,000 / Cr Cash 10,000, and it is balanced.
        var statType = c.VoucherTypes.Single(t => t.IsStatPaymentType);
        var posted = c.Vouchers.Single(v => v.TypeId == statType.Id);
        Assert.True(VoucherValidator.IsBalanced(posted));
        var payableLine = posted.Lines.Single(l => l.LedgerId == payable.Id);
        var cashLine = posted.Lines.Single(l => l.LedgerId == cash.Id);
        Assert.Equal(DrCr.Debit, payableLine.Side);
        Assert.Equal(Money.FromRupees(10_000m), payableLine.Amount);
        Assert.Equal(DrCr.Credit, cashLine.Side);
        Assert.Equal(Money.FromRupees(10_000m), cashLine.Amount);

        // The TDS Payable closing balance is now flat zero (accrued 10,000 Cr, deposited 10,000 Dr).
        Assert.Equal(0m, new TdsDepositService(c).OutstandingPayable(page.AsOf).Amount);
    }

    // ---- (2) recon report renders + matches ----

    [Fact]
    public void Recon_report_renders_and_matches_after_deposit()
    {
        var (vm, fees, vendor) = TdsCompany("Recon-Matched Co");
        PostWithholding(vm, fees, vendor);
        Assert.True(FillChallan(new TdsStatPaymentViewModel(vm.Company!, _storage), "CIN-003"));

        var recon = new ChallanReconciliationViewModel(vm.Company!);
        var row = Assert.Single(recon.Rows);
        Assert.Equal("194J(b)", row.Section);
        Assert.Equal("10,000.00", row.Deducted);
        Assert.Equal("10,000.00", row.Deposited);
        Assert.Equal("0.00", row.Remaining);
        Assert.True(row.IsMatched);
        Assert.Equal("Matched", row.Status);
        Assert.True(recon.IsFullyReconciled);
        Assert.Equal("0.00", recon.TotalRemaining);
        Assert.Equal(0, recon.HighlightedIndex); // a live ListBox selection is seeded
    }

    [Fact]
    public void Recon_report_shows_short_section_when_not_deposited()
    {
        var (vm, fees, vendor) = TdsCompany("Recon-Short Co");
        PostWithholding(vm, fees, vendor);
        // No deposit made — the section is deducted-but-undeposited.

        var recon = new ChallanReconciliationViewModel(vm.Company!);
        var row = Assert.Single(recon.Rows);
        Assert.Equal("10,000.00", row.Deducted);
        Assert.Equal("0.00", row.Deposited);
        Assert.Equal("10,000.00", row.Remaining);
        Assert.False(row.IsMatched);
        Assert.Equal("Short", row.Status);
        Assert.False(recon.IsFullyReconciled);
        Assert.Equal("10,000.00", recon.TotalRemaining);
    }

    [Fact]
    public void Recon_report_empty_on_a_company_with_no_tds_activity()
    {
        var (vm, _, _) = TdsCompany("No-Activity Co");
        var recon = new ChallanReconciliationViewModel(vm.Company!);
        Assert.Empty(recon.Rows);
        Assert.Equal(-1, recon.HighlightedIndex);
        Assert.NotNull(recon.Message);
    }

    // ---- (3) the challan fields persist (round-trip through storage) ----

    [Fact]
    public void Challan_fields_persist_and_link_to_the_stat_payment_voucher()
    {
        var (vm, fees, vendor) = TdsCompany("Challan-Persist Co");
        PostWithholding(vm, fees, vendor);
        var companyName = vm.Company!.Name;

        var page = new TdsStatPaymentViewModel(vm.Company!, _storage);
        page.DepositDateText = "07-05-2026";
        page.SectionCode = "194J(b)";
        Assert.True(FillChallan(page, "CIN-999"));

        var statType = vm.Company!.VoucherTypes.Single(t => t.IsStatPaymentType);
        var statVoucherId = vm.Company!.Vouchers.Single(v => v.TypeId == statType.Id).Id;

        // Reload the company fresh from storage — the challan + its fields + the voucher link survive the round-trip.
        var entry = _storage.ListCompanies().Single(e => e.Name == companyName);
        var reloaded = _storage.Load(entry);
        var challan = Assert.Single(reloaded.TdsChallans);
        Assert.Equal("CIN-999", challan.ChallanNo);
        Assert.Equal("0510308", challan.BsrCode);
        Assert.Equal("194J(b)", challan.Section);
        Assert.Equal("200", challan.MinorHead);
        Assert.Equal(new DateOnly(2026, 5, 7), challan.DepositDate);
        Assert.Equal(Money.FromRupees(10_000m), challan.Amount);
        Assert.Contains(reloaded.ChallanVoucherLinks,
            link => link.ChallanId == challan.Id && link.VoucherId == statVoucherId);
    }

    // ---- validation guards ----

    [Fact]
    public void Deposit_rejected_without_a_challan_number()
    {
        var (vm, fees, vendor) = TdsCompany("Guard Co");
        PostWithholding(vm, fees, vendor);

        var page = new TdsStatPaymentViewModel(vm.Company!, _storage);
        page.BsrCode = "0510308";
        page.ChallanNo = "   "; // blank
        Assert.False(page.Deposit());
        Assert.False(page.LastDepositSucceeded);
        Assert.NotNull(page.Message);
        Assert.Empty(vm.Company!.TdsChallans); // nothing posted / recorded
    }

    [Fact]
    public void Deposit_rejected_when_amount_exceeds_the_outstanding_payable()
    {
        var (vm, fees, vendor) = TdsCompany("Over-Deposit Co");
        PostWithholding(vm, fees, vendor); // ₹10,000 accrued in "TDS Payable"

        var page = new TdsStatPaymentViewModel(vm.Company!, _storage);
        page.AmountText = "15000"; // deliberately more than the ₹10,000 owed (a single ledger — never over-deposit)
        Assert.False(FillChallan(page, "CIN-OVER"));
        Assert.False(page.LastDepositSucceeded);
        Assert.NotNull(page.Message);
        Assert.Empty(vm.Company!.TdsChallans); // nothing posted / recorded

        // The payable is untouched — still the full ₹10,000, never driven negative (which OutstandingPayable would
        // have masked by clamping to zero).
        Assert.Equal(Money.FromRupees(10_000m),
            new TdsDepositService(vm.Company!).OutstandingPayable(page.AsOf));
    }

    // ---- (4) ER-13 gating: a non-TDS company is byte-identical (no screens, no menu entries) ----

    [Fact]
    public void Stat_payment_opener_is_a_no_op_on_a_non_tds_company()
    {
        var vm = NewCompany("Plain Co");
        vm.ShowTdsStatPayment();
        Assert.Null(vm.TdsStatPayment);
        Assert.DoesNotContain(vm.Columns, col => col.IsPage);
    }

    [Fact]
    public void Challan_recon_opener_is_a_no_op_on_a_non_tds_company()
    {
        var vm = NewCompany("Plain Co 2");
        vm.OpenChallanReconciliation();
        Assert.Null(vm.ChallanReconciliation);
        Assert.DoesNotContain(vm.Columns, col => col.IsPage);
    }

    [Fact]
    public void Menu_entries_appear_only_when_tds_is_enabled()
    {
        // TDS on: the Vouchers column carries "TDS Stat Payment"; GST Reports carries "Challan Reconciliation".
        var (tdsVm, _, _) = TdsCompany("Menu-On Co");
        tdsVm.ShowVouchersMenu();
        Assert.Contains(tdsVm.Columns[^1].Items, m => m.Label == "TDS Stat Payment");
        tdsVm.ShowGstReportsMenu();
        Assert.Contains(tdsVm.Columns[^1].Items, m => m.Label == "Challan Reconciliation");

        // TDS off: neither entry is present (ER-13, byte-identical menus).
        var plainVm = NewCompany("Menu-Off Co");
        plainVm.ShowVouchersMenu();
        Assert.DoesNotContain(plainVm.Columns[^1].Items, m => m.Label == "TDS Stat Payment");
        plainVm.ShowGstReportsMenu();
        Assert.DoesNotContain(plainVm.Columns[^1].Items, m => m.Label == "Challan Reconciliation");
    }

    // ---- the screens open + activate through the shell (no dead shortcuts) ----

    [Fact]
    public void Opening_the_screens_binds_them_as_page_columns()
    {
        var (vm, fees, vendor) = TdsCompany("Open-Screens Co");
        PostWithholding(vm, fees, vendor);

        vm.ShowTdsStatPayment();
        Assert.NotNull(vm.TdsStatPayment);
        Assert.Equal(Screen.TdsStatPayment, vm.CurrentScreen);
        Assert.Same(vm.TdsStatPayment, vm.Columns[^1].TdsStatPayment);

        vm.OpenChallanReconciliation();
        Assert.NotNull(vm.ChallanReconciliation);
        Assert.Equal(Screen.ChallanReconciliation, vm.CurrentScreen);
        Assert.Same(vm.ChallanReconciliation, vm.Columns[^1].ChallanReconciliation);
        // The recon arrow-nav hook is live (drives the ListBox highlight).
        Assert.True(vm.IsChallanReconciliationScreen);
    }
}
