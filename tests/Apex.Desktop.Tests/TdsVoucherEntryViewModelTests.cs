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

    /// <summary>A TDS-enabled company with a 194J(b) "Professional Fees" <b>expense</b> ledger (Is-TDS-Applicable +
    /// default nature — the EXPENSE drives applicability &amp; section) and a deductee vendor (Firm; PAN optional —
    /// the PARTY drives only the rate).</summary>
    private (MainWindowViewModel Vm, DomainLedger Fees, DomainLedger Vendor) TdsCompany(string name, string? pan)
    {
        var vm = NewCompany(name);
        var c = vm.Company!;
        new TdsTcsService(c).EnableTds(new TdsConfig { Tan = ValidTan });
        var fees = AddLedger(c, "Professional Fees", "Indirect Expenses", true);
        var vendor = AddLedger(c, "Acme Consultants", "Sundry Creditors", false);
        var nop = c.FindNatureOfPaymentByCode("194J(b)")!;
        // Correct (Tally) model: the EXPENSE ledger is Is-TDS-Applicable and carries the default Nature of Payment.
        fees.TdsApplicable = true;
        fees.TdsNatureOfPaymentId = nop.Id;
        // The PARTY is a deductee — it drives ONLY the rate (via PAN); it carries no applicability flag / section.
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
        // A full correct-model TDS shape (expense Is-TDS-Applicable + deductee party) — but TDS was never enabled,
        // so no natures exist, no panel shows, and the voucher posts verbatim.
        fees.TdsApplicable = true; vendor.DeducteeType = DeducteeType.Firm; vendor.PartyPan = DeducteePan;

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

    // ---- the corrected model: applicability is EXPENSE-driven, not party-driven ----

    [Fact]
    public void Deductee_party_on_non_tds_expense_does_not_withhold()
    {
        // A deductee vendor paid against a NON-TDS expense ledger (Is-TDS-Applicable = false) must NOT withhold —
        // the trigger is the expense leg, not the party. This is the exact bug the adversarial review flagged.
        var (vm, _, vendor) = TdsCompany("Non-TDS-Expense Co", DeducteePan);
        var c = vm.Company!;
        var stationery = AddLedger(c, "Office Stationery", "Indirect Expenses", true); // NOT Is-TDS-Applicable

        var e = OpenJournal(vm, stationery, 1_00_000m, vendor, 1_00_000m);
        Assert.False(e.ShowTdsPanel);           // no Is-TDS-Applicable Dr leg ⇒ no panel
        Assert.True(e.CanAccept);
        Assert.True(e.Accept());

        var posted = c.Vouchers.Single(v => v.TypeId == e.Type.Id);
        Assert.Equal(2, posted.Lines.Count);    // verbatim — no carved TDS-Payable leg (ER-13)
        Assert.All(posted.Lines, l => Assert.False(l.HasTds));
        var vendorLine = posted.Lines.Single(l => l.LedgerId == vendor.Id);
        Assert.Equal(Money.FromRupees(1_00_000m), vendorLine.Amount); // full gross to the party
    }

    [Fact]
    public void Section_comes_from_expense_ledger_not_party()
    {
        // Rent (194I(b)) expense paid to a deductee vendor whose OWN default nature is 194J(b): the section must be
        // the EXPENSE ledger's 194I(b) @10% (rent land/building), never the party's 194J(b).
        var vm = NewCompany("Section-Source Co");
        var c = vm.Company!;
        new TdsTcsService(c).EnableTds(new TdsConfig { Tan = ValidTan });
        var rent = AddLedger(c, "Office Rent", "Indirect Expenses", true);
        var landlord = AddLedger(c, "Estate Holdings", "Sundry Creditors", false);
        var rent194I = c.FindNatureOfPaymentByCode("194I(b)")!;
        var prof194J = c.FindNatureOfPaymentByCode("194J(b)")!;
        // Expense drives the section (194I(b)); the party carries a DIFFERENT default (194J(b)) that must be ignored.
        rent.TdsApplicable = true;
        rent.TdsNatureOfPaymentId = rent194I.Id;
        landlord.DeducteeType = DeducteeType.Firm;
        landlord.PartyPan = DeducteePan;
        landlord.TdsNatureOfPaymentId = prof194J.Id; // the party's default — deliberately the WRONG section

        // 194I(b) has a cumulative ₹6,00,000/FY threshold (no single-txn cap) — cross it so TDS fires.
        var e = OpenJournal(vm, rent, 7_00_000m, landlord, 7_00_000m);
        Assert.True(e.ShowTdsPanel);
        Assert.NotNull(e.SelectedTdsNature);
        Assert.Equal("194I(b)", e.SelectedTdsNature!.SectionCode); // expense's section, not the party's 194J(b)
        Assert.Equal("194I(b)", e.TdsSectionText);
        Assert.Equal("70,000.00", e.TdsAmountText);                // 194I(b) @10% on 7,00,000

        Assert.True(e.Accept());
        var posted = c.Vouchers.Single(v => v.TypeId == e.Type.Id);
        var payable = new TdsService(c).RequirePayableLedger();
        var payableLine = posted.Lines.Single(l => l.LedgerId == payable.Id);
        Assert.Equal(rent194I.Id, payableLine.Tds!.NatureId);      // the withholding detail records 194I(b)
        Assert.Equal("194I(b)", payableLine.Tds!.SectionCode);
    }

    [Fact]
    public void Operator_can_decline_tds_via_not_applicable_sentinel()
    {
        // The "Not Applicable" sentinel lets the operator decline TDS on a mixed/edge voucher — panel stays visible
        // (so they can re-enable) but the voucher posts verbatim with no carve-out (ER-13).
        var (vm, fees, vendor) = TdsCompany("Decline Co", DeducteePan);
        var e = OpenJournal(vm, fees, 1_00_000m, vendor, 1_00_000m);
        Assert.True(e.ShowTdsPanel);
        Assert.Contains(e.TdsNatureOptions, n => ReferenceEquals(n, VoucherEntryViewModel.TdsNotApplicable));

        e.SelectedTdsNature = VoucherEntryViewModel.TdsNotApplicable; // decline
        e.Recalculate();
        Assert.True(e.ShowTdsPanel);            // still visible so the operator can change their mind
        Assert.Equal("0.00", e.TdsAmountText);

        Assert.True(e.Accept());
        var posted = vm.Company!.Vouchers.Single(v => v.TypeId == e.Type.Id);
        Assert.Equal(2, posted.Lines.Count);    // verbatim — no TDS-Payable leg
        Assert.All(posted.Lines, l => Assert.False(l.HasTds));
    }
}
