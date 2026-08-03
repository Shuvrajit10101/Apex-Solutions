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
/// <b>G-7 / rev2-1 CRITICAL</b> — <b>reverse charge</b> on a Purchase <b>Accounting Invoice</b>.
///
/// <para><b>The defect these tests pin.</b> Widening <c>CanBeAccountingInvoice</c> to Purchase made a combination
/// reachable that had never existed: <c>ComputeAccountingInvoiceGst()</c> resolved a <b>forward-charge</b> rate for
/// EVERY complete Particulars line and never consulted <c>SalesPurchaseGst.ReverseChargeApplicable</c>, while
/// <c>AcceptAccountingInvoice</c> ALSO appended the RCM dual pair. A ₹87,654.32 legal fee from a Gujarat advocate
/// therefore posted <c>Cr Advocate 1,03,432.10 / Dr Legal Fees 87,654.32 / Dr Input IGST 15,777.78 /
/// Cr RCM Output IGST 15,777.78 / Dr Input IGST 15,777.78</c> — the supplier over-credited by ₹15,777.78 (a
/// reverse-charge supplier charges NO tax, which is the entire point of the mechanism) and Input IGST claimed
/// <b>twice</b> (₹31,555.56) against a single ₹15,777.78 liability. The voucher balanced at 1,19,209.88 either way,
/// so no validator, no balance rule and no existing test caught it.</para>
///
/// <para>The rule: a Particulars line on which reverse charge <b>actually resolves as applying</b> (the same
/// <c>RcmService.Resolve</c> the Accept path calls, and only when the operator has not declined) contributes NO
/// forward-charge tax — the whole tax movement is the self-accounting dual pair. Every other line, including an
/// RCM-flagged line whose notified category does not fire, keeps ordinary forward charge.</para>
///
/// <para><b>Odd-paisa fixtures throughout</b> — ₹87,654.32 @ 18% = ₹15,777.7776 → ₹15,777.78, so a rounding or
/// double-count slip is visible to the paisa. Round figures would prove nothing.</para>
/// </summary>
public sealed class PurchaseAccountingInvoiceRcmTests : IDisposable
{
    private const string GstinMaharashtra = "27AAPFU0939F1ZV";
    private const string GstinGujarat = "24AAACC1206D1ZM";
    private static readonly DateOnly FyStart = new(2025, 4, 1);
    private static readonly DateOnly D1 = new(2025, 4, 10);

    private readonly string _tempDir;
    private readonly CompanyStorage _storage;

    public PurchaseAccountingInvoiceRcmTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "ApexPurchAcctRcm_" + Guid.NewGuid().ToString("N"));
        _storage = new CompanyStorage(_tempDir);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true); }
        catch (IOException) { /* best-effort cleanup */ }
    }

    // ---------------------------------------------------------------- fixture helpers (mirror RcmVoucherEntryViewModelTests)

    /// <summary>A Regular-GST (Maharashtra, home state 27) company with the notified RCM categories + rate history.</summary>
    private MainWindowViewModel GstCompany(string name)
    {
        var vm = new MainWindowViewModel(_storage);
        vm.NewCompanyName = name;
        vm.CreateCompany();
        var c = vm.Company!;
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
        gst.SeedAdvancedGst();
        return vm;
    }

    private static DomainLedger AddLedger(Company c, string name, string groupName, bool openingIsDebit) =>
        Add(c, new DomainLedger(Guid.NewGuid(), name, c.FindGroupByName(groupName)!.Id, Money.Zero, openingIsDebit));

    private static DomainLedger Add(Company c, DomainLedger l) { c.AddLedger(l); return l; }

    /// <summary>An expense ledger flagged reverse-charge and linked to a seeded notified category.</summary>
    private static DomainLedger RcmExpense(
        Company c, string name, string nature, int rateBp,
        GstSupplyType type = GstSupplyType.Services, string? hsn = null)
    {
        var l = AddLedger(c, name, "Indirect Expenses", true);
        l.SalesPurchaseGst = new StockItemGstDetails
        {
            HsnSac = hsn,
            Taxability = GstTaxability.Taxable,
            RateBasisPoints = rateBp,
            SupplyType = type,
            ReverseChargeApplicable = true,
            RcmCategoryId = c.Gst!.RcmCategories.First(x => x.SupplyNature == nature).Id,
        };
        return l;
    }

    /// <summary>An ordinary FORWARD-charge taxable expense ledger — the control that must keep its Input tax leg.</summary>
    private static DomainLedger ForwardExpense(Company c, string name, int rateBp)
    {
        var l = AddLedger(c, name, "Indirect Expenses", true);
        l.SalesPurchaseGst = new StockItemGstDetails
        {
            Taxability = GstTaxability.Taxable,
            RateBasisPoints = rateBp,
            SupplyType = GstSupplyType.Services,
        };
        return l;
    }

    private static DomainLedger Supplier(Company c, string name, string? gstin, string? state, bool unregistered = false)
    {
        var l = AddLedger(c, name, "Sundry Creditors", false);
        l.PartyGst = new PartyGstDetails
        {
            RegistrationType = unregistered ? GstRegistrationType.Unregistered : GstRegistrationType.Regular,
            Gstin = gstin,
            StateCode = state,
        };
        return l;
    }

    /// <summary>Opens a Purchase in ACCOUNTING-INVOICE mode with the given Particulars lines (ledger, amount text).</summary>
    private static VoucherEntryViewModel OpenAccountingPurchase(
        MainWindowViewModel vm, DomainLedger party, params (DomainLedger Ledger, string Amount)[] particulars)
    {
        vm.OpenVoucher(VoucherBaseType.Purchase);
        var e = vm.VoucherEntry!;
        e.Date = D1;
        e.Mode = VoucherEntryMode.AccountingInvoice;
        e.SelectedParty = e.Parties.Single(p => p.Ledger?.Id == party.Id);
        for (var i = 0; i < particulars.Length; i++)
        {
            while (e.AccountingInvoiceLines.Count <= i) e.AddAccountingInvoiceLine();
            var row = e.AccountingInvoiceLines[i];
            row.SelectedLedger = e.AccountingInvoiceLedgers.Single(l => l.Id == particulars[i].Ledger.Id);
            row.AmountText = particulars[i].Amount;
        }
        e.RecalculateAccountingInvoice();
        return e;
    }

    private static Voucher LastVoucher(Company c) => c.Vouchers.OrderBy(v => v.Number).Last();

    private static Money AmountOn(Voucher v, Guid ledgerId, DrCr side) =>
        v.Lines.Where(l => l.LedgerId == ledgerId && l.Side == side)
            .Aggregate(Money.Zero, (a, l) => a + l.Amount);

    // ================================================================ (1) THE CRITICAL REPRO — inter-state

    /// <summary>
    /// <b>The reviewer's exact fixture.</b> Company GST-registered in Maharashtra; "Legal Fees" flagged reverse-charge
    /// under the notified <i>Legal</i> category @18%; supplier is an advocate in Gujarat; Purchase accounting invoice
    /// of ₹87,654.32.
    /// <para>The supplier must be credited the <b>bare</b> ₹87,654.32 — a reverse-charge supplier charges no tax — and
    /// Input IGST must be debited <b>exactly once</b>, ₹15,777.78, matched by the ₹15,777.78 cash-only RCM Output IGST
    /// credit. Before the fix the party carried ₹1,03,432.10 and Input IGST ₹31,555.56 across two legs.</para>
    /// </summary>
    [Fact]
    public void Reverse_charge_particulars_line_credits_the_supplier_the_bare_value_and_claims_input_tax_once()
    {
        var vm = GstCompany("Acct RCM Legal Co");
        var c = vm.Company!;
        var fees = RcmExpense(c, "Legal Fees", "Legal", 1800);
        var advocate = Supplier(c, "Advocate (Gujarat)", GstinGujarat, "24");

        var e = OpenAccountingPurchase(vm, advocate, (fees, "87654.32"));

        // The live panel must ALSO show the bare figure — ER-4: what is shown is what is posted.
        Assert.True(e.ShowRcmPanel);
        Assert.Equal("Yes — reverse charge applies", e.RcmAppliesText);
        Assert.Equal("15,777.78", e.RcmTaxText);
        Assert.Equal("0.00", e.GstIgstText);                 // NO forward-charge tax on a reverse-charge line
        Assert.Equal("87,654.32", e.PartyTotalText);

        Assert.True(e.Accept());

        var v = LastVoucher(c);
        var gst = new GstService(c);
        var rcmOutIgst = gst.FindRcmOutputLedger(GstTaxHead.Integrated)!;
        var inputIgst = gst.FindTaxLedger(GstTaxHead.Integrated, GstTaxDirection.Input)!;

        Assert.True(VoucherValidator.IsBalanced(v));
        Assert.Equal(4, v.Lines.Count);                                          // party + expense + the dual pair
        Assert.Equal(87654.32m, AmountOn(v, advocate.Id, DrCr.Credit).Amount);   // the bare value — NOT 1,03,432.10
        Assert.Equal(87654.32m, AmountOn(v, fees.Id, DrCr.Debit).Amount);
        Assert.Equal(15777.78m, AmountOn(v, rcmOutIgst.Id, DrCr.Credit).Amount); // the §49(4) cash-only liability
        Assert.Equal(15777.78m, AmountOn(v, inputIgst.Id, DrCr.Debit).Amount);   // NOT 31,555.56

        // "Exactly once" is a LINE-COUNT claim, not just a total: two half-sized legs would sum the same.
        Assert.Equal(1, v.Lines.Count(l => l.LedgerId == inputIgst.Id && l.Side == DrCr.Debit));

        // And the ordinary (non-RCM) Output IGST ledger is untouched — the liability is structurally cash-only.
        Assert.Equal(0m, AmountOn(v, gst.FindTaxLedger(GstTaxHead.Integrated, GstTaxDirection.Output)!.Id, DrCr.Credit).Amount);

        Assert.Equal(103432.10m, v.TotalDebit.Amount);   // 87,654.32 + 15,777.78
        Assert.Equal(103432.10m, v.TotalCredit.Amount);  // 87,654.32 + 15,777.78
    }

    // ================================================================ (2) intra-state — the CGST/SGST dual pair

    /// <summary>
    /// The same supply from a Mumbai advocate splits into CGST+SGST. ₹87,654.32 @ 9% = ₹7,888.8888 → ₹7,888.89 a head.
    /// The supplier is still credited the bare value, and each Input head is debited exactly once.
    /// </summary>
    [Fact]
    public void Intrastate_reverse_charge_on_an_accounting_invoice_posts_one_cgst_sgst_pair_and_a_bare_party_leg()
    {
        var vm = GstCompany("Acct RCM Intra Co");
        var c = vm.Company!;
        var fees = RcmExpense(c, "Legal Fees", "Legal", 1800);
        var advocate = Supplier(c, "Advocate (Mumbai)", GstinMaharashtra, "27");

        var e = OpenAccountingPurchase(vm, advocate, (fees, "87654.32"));
        Assert.Equal("Intra-State (CGST+SGST)", e.RcmPosText);
        Assert.Equal("0.00", e.GstCgstText);
        Assert.Equal("0.00", e.GstSgstText);
        Assert.Equal("87,654.32", e.PartyTotalText);
        Assert.True(e.Accept());

        var v = LastVoucher(c);
        var gst = new GstService(c);
        var inCgst = gst.FindTaxLedger(GstTaxHead.Central, GstTaxDirection.Input)!;
        var inSgst = gst.FindTaxLedger(GstTaxHead.State, GstTaxDirection.Input)!;

        Assert.True(VoucherValidator.IsBalanced(v));
        Assert.Equal(87654.32m, AmountOn(v, advocate.Id, DrCr.Credit).Amount);
        Assert.Equal(7888.89m, AmountOn(v, gst.FindRcmOutputLedger(GstTaxHead.Central)!.Id, DrCr.Credit).Amount);
        Assert.Equal(7888.89m, AmountOn(v, gst.FindRcmOutputLedger(GstTaxHead.State)!.Id, DrCr.Credit).Amount);
        Assert.Equal(7888.89m, AmountOn(v, inCgst.Id, DrCr.Debit).Amount);
        Assert.Equal(7888.89m, AmountOn(v, inSgst.Id, DrCr.Debit).Amount);
        Assert.Equal(1, v.Lines.Count(l => l.LedgerId == inCgst.Id && l.Side == DrCr.Debit));
        Assert.Equal(1, v.Lines.Count(l => l.LedgerId == inSgst.Id && l.Side == DrCr.Debit));
    }

    // ================================================================ (3) the skip is PER LINE, not per voucher

    /// <summary>
    /// One invoice, two heads: a reverse-charge legal fee of ₹87,654.32 and an ordinary forward-charge consultancy
    /// charge of ₹12,345.67 @18% (IGST ₹2,222.2206 → ₹2,222.22). The supplier is owed the legal fee bare PLUS the
    /// consultancy line's tax-inclusive value: 87,654.32 + 12,345.67 + 2,222.22 = ₹1,02,222.21. Input IGST carries
    /// both movements — ₹2,222.22 forward + ₹15,777.78 RCM = ₹18,000.00 — on two distinct legs.
    /// <para>A whole-voucher (rather than per-line) skip would strip the forward-charge tax off the consultancy line
    /// and under-credit the supplier by ₹2,222.22.</para>
    /// </summary>
    [Fact]
    public void A_forward_charge_line_on_the_same_invoice_keeps_its_input_tax_and_inflates_only_its_own_share()
    {
        var vm = GstCompany("Acct RCM Mixed Co");
        var c = vm.Company!;
        var fees = RcmExpense(c, "Legal Fees", "Legal", 1800);
        var consultancy = ForwardExpense(c, "Consultancy Charges", 1800);
        var advocate = Supplier(c, "Advocate (Gujarat)", GstinGujarat, "24");

        var e = OpenAccountingPurchase(vm, advocate, (fees, "87654.32"), (consultancy, "12345.67"));

        Assert.Equal("2,222.22", e.GstIgstText);        // forward charge on the consultancy line only
        Assert.Equal("1,02,222.21", e.PartyTotalText);
        Assert.True(e.Accept());

        var v = LastVoucher(c);
        var gst = new GstService(c);
        var inputIgst = gst.FindTaxLedger(GstTaxHead.Integrated, GstTaxDirection.Input)!;

        Assert.True(VoucherValidator.IsBalanced(v));
        Assert.Equal(102222.21m, AmountOn(v, advocate.Id, DrCr.Credit).Amount);
        Assert.Equal(87654.32m, AmountOn(v, fees.Id, DrCr.Debit).Amount);
        Assert.Equal(12345.67m, AmountOn(v, consultancy.Id, DrCr.Debit).Amount);
        Assert.Equal(15777.78m, AmountOn(v, gst.FindRcmOutputLedger(GstTaxHead.Integrated)!.Id, DrCr.Credit).Amount);
        Assert.Equal(18000.00m, AmountOn(v, inputIgst.Id, DrCr.Debit).Amount); // 2,222.22 forward + 15,777.78 RCM
        Assert.Equal(2, v.Lines.Count(l => l.LedgerId == inputIgst.Id && l.Side == DrCr.Debit));
    }

    // ================================================================ (4) the skip is gated on the operator's decline

    /// <summary>
    /// The operator declines reverse charge via the "Not Applicable" sentinel — the screen cannot know every reason a
    /// notified-looking inward supply is really forward charge. The dual pair must vanish AND the ordinary forward
    /// charge must come back: supplier ₹1,03,432.10, Input IGST ₹15,777.78 once, no RCM Output ledger conjured.
    /// <para>This is what makes the fix a conditional skip rather than a blanket "never tax an RCM-flagged ledger".</para>
    /// </summary>
    [Fact]
    public void Declining_reverse_charge_restores_ordinary_forward_charge_on_the_accounting_invoice()
    {
        var vm = GstCompany("Acct RCM Declined Co");
        var c = vm.Company!;
        var fees = RcmExpense(c, "Legal Fees", "Legal", 1800);
        var advocate = Supplier(c, "Advocate (Gujarat)", GstinGujarat, "24");

        var e = OpenAccountingPurchase(vm, advocate, (fees, "87654.32"));
        Assert.Equal("87,654.32", e.PartyTotalText);

        e.SelectedRcmSupplyKind = e.RcmSupplyKinds.Single(k => k.Kind is null); // "Not Applicable"
        Assert.Equal("1,03,432.10", e.PartyTotalText);                          // forward charge is back
        Assert.True(e.Accept());

        var v = LastVoucher(c);
        var gst = new GstService(c);
        var inputIgst = gst.FindTaxLedger(GstTaxHead.Integrated, GstTaxDirection.Input)!;

        Assert.True(VoucherValidator.IsBalanced(v));
        Assert.Equal(3, v.Lines.Count);                                           // party + expense + Input IGST
        Assert.Equal(103432.10m, AmountOn(v, advocate.Id, DrCr.Credit).Amount);
        Assert.Equal(15777.78m, AmountOn(v, inputIgst.Id, DrCr.Debit).Amount);
        Assert.Equal(1, v.Lines.Count(l => l.LedgerId == inputIgst.Id && l.Side == DrCr.Debit));
        Assert.Null(gst.FindRcmOutputLedger(GstTaxHead.Integrated));              // no RCM ledger conjured
    }

    // ================================================================ (5) the skip is gated on the ENGINE's resolution

    /// <summary>
    /// A reverse-charge-FLAGGED ledger whose notified category does not actually fire is an ordinary forward-charge
    /// supply, and must keep its Input tax leg. Sponsorship (Notn 13/2017) fires only where the RECIPIENT is a body
    /// corporate, so the very same voucher swings on that qualifier alone: body corporate ⇒ the bare ₹87,654.32 and
    /// the dual pair; not a body corporate ⇒ ordinary forward charge, party ₹1,03,432.10 with Input IGST ₹15,777.78.
    /// <para>This is the test that fails if the fix skips on the <b>flag</b> instead of on
    /// <c>RcmService.Resolve(...).Applies</c> — the flag is identical in both halves.</para>
    /// </summary>
    [Fact]
    public void A_flagged_line_whose_category_does_not_fire_keeps_forward_charge()
    {
        var vm = GstCompany("Acct RCM Sponsorship Co");
        var c = vm.Company!;
        var sponsorship = RcmExpense(c, "Sponsorship Fees", "Sponsorship", 1800);
        var promoter = Supplier(c, "Event Promoter (Gujarat)", GstinGujarat, "24");

        var e = OpenAccountingPurchase(vm, promoter, (sponsorship, "87654.32"));

        // We ARE a body corporate (the screen default) ⇒ §9(3) fires ⇒ bare party leg.
        Assert.True(e.RcmRecipientIsBodyCorporate);
        Assert.Equal("Yes — reverse charge applies", e.RcmAppliesText);
        Assert.Equal("87,654.32", e.PartyTotalText);

        // Untick ⇒ the same flagged ledger is now an ordinary forward-charge supply and the supplier DOES charge tax.
        e.RcmRecipientIsBodyCorporate = false;
        Assert.Equal("No — forward charge", e.RcmAppliesText);
        Assert.Equal("1,03,432.10", e.PartyTotalText);
        Assert.True(e.Accept());

        var v = LastVoucher(c);
        var gst = new GstService(c);
        var inputIgst = gst.FindTaxLedger(GstTaxHead.Integrated, GstTaxDirection.Input)!;

        Assert.True(VoucherValidator.IsBalanced(v));
        Assert.Equal(3, v.Lines.Count);                                             // party + expense + Input IGST
        Assert.Equal(103432.10m, AmountOn(v, promoter.Id, DrCr.Credit).Amount);
        Assert.Equal(87654.32m, AmountOn(v, sponsorship.Id, DrCr.Debit).Amount);
        Assert.Equal(15777.78m, AmountOn(v, inputIgst.Id, DrCr.Debit).Amount);
        Assert.Null(gst.FindRcmOutputLedger(GstTaxHead.Integrated));                // no dual pair, no RCM ledger
    }
}
