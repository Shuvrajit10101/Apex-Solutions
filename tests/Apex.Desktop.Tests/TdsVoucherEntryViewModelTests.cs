using System;
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
/// Phase 7 slice 2 — the TDS withholding <b>voucher-entry UI</b> (<see cref="VoucherEntryViewModel"/>). Proves the
/// plain-grid screen surfaces the auto-computed deduction via the SAME <see cref="TdsService.BuildCarveOut"/> the
/// posting uses (ER-4, no re-implementation), and that on Accept the party leg is carved to the DERIVED net while a
/// TDS-Payable leg is appended — so <c>Dr Expense GROSS == Cr Party NET + Cr TDS-Payable</c> to the paisa and the
/// engine accepts it. Also proves the ER-13 gate: a voucher with no deductee (or on a TDS-off company) shows no
/// panel and posts verbatim, and the no-PAN §206AA 20% rate flows through the UI.
/// </summary>
public sealed class TdsVoucherEntryViewModelTests : IDisposable
{
    private const string ValidTan = "MUMA12345B";
    private const string DeducteePan = "AAPFU0939F";

    private readonly string _tempDir;
    private readonly CompanyStorage _storage;

    public TdsVoucherEntryViewModelTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "ApexTdsVoucherTests_" + Guid.NewGuid().ToString("N"));
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

    /// <summary>A TDS-enabled company with a plain "Professional Fees" expense and a 194J(b) vendor (PAN optional).</summary>
    private (MainWindowViewModel Vm, DomainLedger Fees, DomainLedger Vendor) TdsCompany(string name, string? pan)
    {
        var vm = NewCompany(name);
        var c = vm.Company!;
        new TdsTcsService(c).EnableTds(new TdsConfig { Tan = ValidTan });
        var fees = AddLedger(c, "Professional Fees", "Indirect Expenses", true);
        var vendor = AddLedger(c, "Acme Consultants", "Sundry Creditors", false);
        var nop = c.FindNatureOfPaymentByCode("194J(b)")!;
        vendor.TdsApplicable = true;
        vendor.TdsNatureOfPaymentId = nop.Id;
        vendor.DeducteeType = DeducteeType.Firm;
        vendor.PartyPan = pan;
        return (vm, fees, vendor);
    }

    private static VoucherEntryViewModel OpenJournal(MainWindowViewModel vm, DomainLedger dr, decimal drAmt, DomainLedger cr, decimal crAmt)
    {
        vm.OpenVoucher(VoucherBaseType.Journal);
        var e = vm.VoucherEntry!;
        e.Lines[0].SelectedLedger = dr; e.Lines[0].Side = DrCr.Debit; e.Lines[0].AmountText = drAmt.ToString(System.Globalization.CultureInfo.InvariantCulture);
        e.Lines[1].SelectedLedger = cr; e.Lines[1].Side = DrCr.Credit; e.Lines[1].AmountText = crAmt.ToString(System.Globalization.CultureInfo.InvariantCulture);
        e.Recalculate();
        return e;
    }

    // ---- the panel shows only when it applies (ER-13) ----

    [Fact]
    public void Panel_hidden_when_no_deductee_party()
    {
        var (vm, fees, _) = TdsCompany("No-Deductee Co", DeducteePan);
        var cash = vm.Company!.FindLedgerByName("Cash")!;

        // Dr Fees / Cr Cash — Cash is not a deductee, so no TDS panel and a verbatim 2-line posting.
        var e = OpenJournal(vm, fees, 5_000m, cash, 5_000m);
        Assert.False(e.ShowTdsPanel);
        Assert.True(e.CanAccept);
        Assert.True(e.Accept());

        var posted = vm.Company!.Vouchers.Single(v => v.TypeId == e.Type.Id);
        Assert.Equal(2, posted.Lines.Count);
        Assert.All(posted.Lines, l => Assert.False(l.HasTds));
    }

    [Fact]
    public void Panel_hidden_on_tds_disabled_company()
    {
        // Same shape as a TDS journal, but the company never enabled TDS → no panel, no natures, verbatim posting.
        var vm = NewCompany("Tds-Off Co");
        var c = vm.Company!;
        var fees = AddLedger(c, "Professional Fees", "Indirect Expenses", true);
        var vendor = AddLedger(c, "Acme Consultants", "Sundry Creditors", false);
        vendor.TdsApplicable = true; vendor.DeducteeType = DeducteeType.Firm; vendor.PartyPan = DeducteePan;

        var e = OpenJournal(vm, fees, 1_00_000m, vendor, 1_00_000m);
        Assert.Empty(e.TdsNatureOptions);
        Assert.False(e.ShowTdsPanel);
        Assert.True(e.Accept());
        var posted = vm.Company!.Vouchers.Single(v => v.TypeId == e.Type.Id);
        Assert.Equal(2, posted.Lines.Count);
    }

    // ---- the load-bearing contract: net DERIVED from the engine; gross == net + TDS ----

    [Fact]
    public void With_pan_194J_shows_10pct_and_posts_derived_net()
    {
        var (vm, fees, vendor) = TdsCompany("Withholding Co", DeducteePan);
        var e = OpenJournal(vm, fees, 1_00_000m, vendor, 1_00_000m);

        // Panel is shown, nature auto-defaulted, and the figures come straight from the engine.
        Assert.True(e.ShowTdsPanel);
        Assert.NotNull(e.SelectedTdsNature);
        Assert.Equal("194J(b)", e.SelectedTdsNature!.SectionCode);
        Assert.Equal("10,000.00", e.TdsAmountText);
        Assert.Equal("90,000.00", e.TdsNetPayableText);

        Assert.True(e.Accept());
        var posted = vm.Company!.Vouchers.Single(v => v.TypeId == e.Type.Id);
        Assert.True(VoucherValidator.IsBalanced(posted));
        Assert.Equal(3, posted.Lines.Count);

        var feesLine = posted.Lines.Single(l => l.LedgerId == fees.Id);
        var vendorLine = posted.Lines.Single(l => l.LedgerId == vendor.Id);
        var payable = new TdsService(vm.Company!).RequirePayableLedger();
        var payableLine = posted.Lines.Single(l => l.LedgerId == payable.Id);

        Assert.Equal(Money.FromRupees(1_00_000m), feesLine.Amount);   // Dr expense GROSS
        Assert.Equal(DrCr.Debit, feesLine.Side);
        Assert.Equal(Money.FromRupees(90_000m), vendorLine.Amount);   // Cr party NET (derived)
        Assert.Equal(DrCr.Credit, vendorLine.Side);
        Assert.Equal(Money.FromRupees(10_000m), payableLine.Amount);  // Cr TDS Payable
        Assert.Equal(DrCr.Credit, payableLine.Side);
        Assert.True(payableLine.HasTds);
        // gross == net + TDS, to the paisa.
        Assert.Equal(feesLine.Amount, vendorLine.Amount + payableLine.Amount);
    }

    // ---- TDS is assessed on the GST-EXCLUSIVE base (CBDT Circular 23/2017), NOT the GST-inclusive party gross ----

    [Fact]
    public void Tds_assessed_on_gst_exclusive_base_when_input_gst_on_grid()
    {
        var (vm, fees, vendor) = TdsCompany("GST-Journal Co", DeducteePan);
        var c = vm.Company!;
        var cgst = AddLedger(c, "Input CGST", "Duties & Taxes", true);
        var sgst = AddLedger(c, "Input SGST", "Duties & Taxes", true);

        // A GST purchase bill booked via a Journal: Dr Fees 1,00,000 + Dr Input CGST 9,000 + Dr Input SGST 9,000
        // / Cr Vendor 1,18,000 (the party's gross obligation INCLUDES the 18,000 GST).
        vm.OpenVoucher(VoucherBaseType.Journal);
        var e = vm.VoucherEntry!;
        e.AddLine(DrCr.Debit);
        e.AddLine(DrCr.Debit);
        e.Lines[0].SelectedLedger = fees;   e.Lines[0].Side = DrCr.Debit;  e.Lines[0].AmountText = "100000";
        e.Lines[1].SelectedLedger = cgst;   e.Lines[1].Side = DrCr.Debit;  e.Lines[1].AmountText = "9000";
        e.Lines[2].SelectedLedger = sgst;   e.Lines[2].Side = DrCr.Debit;  e.Lines[2].AmountText = "9000";
        e.Lines[3].SelectedLedger = vendor; e.Lines[3].Side = DrCr.Credit; e.Lines[3].AmountText = "118000";
        e.Recalculate();

        Assert.True(e.ShowTdsPanel);
        // TDS 194J(b) @10% on the GST-EXCLUSIVE 1,00,000 = 10,000 — NOT 11,800 (10% of the GST-inclusive 1,18,000).
        Assert.Equal("10,000.00", e.TdsAmountText);
        // Net payable to the vendor = gross 1,18,000 − TDS 10,000 = 1,08,000 (DERIVED from the full gross).
        Assert.Equal("1,08,000.00", e.TdsNetPayableText);

        Assert.True(e.Accept());
        var posted = c.Vouchers.Single(v => v.TypeId == e.Type.Id);
        Assert.True(VoucherValidator.IsBalanced(posted));

        var vendorLine = posted.Lines.Single(l => l.LedgerId == vendor.Id);
        var payable = new TdsService(c).RequirePayableLedger();
        var payableLine = posted.Lines.Single(l => l.LedgerId == payable.Id);
        Assert.Equal(Money.FromRupees(1_08_000m), vendorLine.Amount);   // Cr party NET (derived from gross incl GST)
        Assert.Equal(Money.FromRupees(10_000m), payableLine.Amount);    // Cr TDS Payable (on the ex-GST base)
        // The withholding detail records the GST-EXCLUSIVE assessable base, not the inclusive gross.
        Assert.Equal(Money.FromRupees(1_00_000m), payableLine.Tds!.AssessableValue);
        // gross Dr (fees + CGST + SGST = 1,18,000) == net Cr + TDS Cr, to the paisa.
        Assert.Equal(Money.FromRupees(1_18_000m), vendorLine.Amount + payableLine.Amount);
    }

    [Fact]
    public void No_pan_shows_and_posts_20_percent_under_206AA()
    {
        var (vm, fees, vendor) = TdsCompany("No-PAN Co", pan: null);
        var e = OpenJournal(vm, fees, 1_00_000m, vendor, 1_00_000m);

        Assert.True(e.ShowTdsPanel);
        Assert.Contains("No PAN", e.TdsRateText);
        Assert.Equal("20,000.00", e.TdsAmountText);
        Assert.Equal("80,000.00", e.TdsNetPayableText);

        Assert.True(e.Accept());
        var posted = vm.Company!.Vouchers.Single(v => v.TypeId == e.Type.Id);
        var vendorLine = posted.Lines.Single(l => l.LedgerId == vendor.Id);
        Assert.Equal(Money.FromRupees(80_000m), vendorLine.Amount);
        Assert.True(VoucherValidator.IsBalanced(posted));
    }
}
