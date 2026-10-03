using System.Text.Json;
using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Reports;
using Apex.Ledger.Services;
using Xunit;

namespace Apex.Ledger.Io.Tests;

/// <summary>
/// 🔴 <b>T1-79 / T1-80 / T1-81 — THE LEDGER-ONLY INV-01 PRE-FLIGHT. Three ways a service accounting invoice could
/// mint a payload the IRP must refuse, or worse ACCEPT as a wrong registered invoice, and one gate that refuses all
/// three before a byte is written.</b>
///
/// <para><b>T1-79, measured off the emitted bytes.</b> Consultancy ₹7,500 <b>Cr</b> @ 18% (CGST 675 + SGST 675) with
/// an EXEMPT SAC leg of ₹1,000 posted <b>Dr</b> — a leg that REDUCES the invoice. Hand-computed, the document is
/// <c>AssVal</c> ₹6,500 and <c>TotInvVal</c> ₹7,850, and ₹7,850 is the voucher's own posted party debit. The payload
/// declared <c>AssVal</c> <b>₹8,500</b> and <c>TotInvVal</c> <b>₹9,850</b>: <b>overstated by ₹2,000 — twice the
/// leg</b>, because <see cref="Gstr1.ServiceLegs"/> yielded a magnitude and the correct contribution is −1,000. And
/// Σ <c>AssAmt</c> equalled <c>AssVal</c>, so the IRP would have <b>ACCEPTED</b> it. An accepted wrong invoice is
/// this project's worst class of defect.</para>
///
/// <para><b>T1-80, and it is a SECOND money defect found while building the gate, not a restatement of the first.</b>
/// The e-invoice writer already computed <see cref="GstReportSupport.IsServiceAccountingInvoice"/> and used the
/// verdict ONLY to pick <c>IsServc</c> "Y"/"N", discarding the general-ledger reconciliation inside it — while the
/// payload's own "footing" was structural self-consistency (T1-73 made <c>AssVal</c> be Σ <c>ItemList.AssAmt</c> by
/// construction, which is true of EVERY payload and so says nothing about the books). Two mechanisms that can
/// disagree. Measured on an ordinary discounted service invoice — Consultancy ₹7,500 @ 18% + EXEMPT ₹1,000, less a
/// ₹1,000 trade discount posted Dr to a ledger carrying no GST block, party debit ₹8,850 — the payload declared
/// <c>TotInvVal</c> ₹9,850 against that ₹8,850: <b>overstated by exactly the discount</b>, again with
/// Σ <c>AssAmt</c> == <c>AssVal</c> so again in the form NIC ACCEPTS. <c>ValDtls</c> has a <c>Discount</c> member this
/// writer never emits, so there is no correct payload to substitute; the document is refused.</para>
///
/// <para><b>T1-81 — a blank mandatory <c>HsnCd</c>, and NIC leaves no room for a reading.</b> Retrieved and checked
/// by content at <c>https://einv-apisandbox.nic.in/version1.03/generate-irn.html</c> (HTTP 200, 82,078 bytes), the
/// item object's own required list is
/// <c>"required": ["SlNo","IsServc","HsnCd","UnitPrice","TotAmt","AssAmt","GstRt","TotItemVal"]</c> — <b>every line,
/// unconditionally; there is no conditional limb</b> — and the field is typed
/// <c>"HsnCd": { "type": "string", "minLength": 4, "maxLength": 8,
/// "pattern": "^(?!0+$)([0-9]{4}|[0-9]{6}|[0-9]{8})$" }</c>, which <c>""</c> fails on all three counts. The same
/// page's validations say it in prose: <i>"Each item needs to have valid HSN code with at least 4 digits. HSN Code
/// should be valid as per the GST master."</i></para>
///
/// <para>🔴 <b>WHY THE SIGN IS EXPOSED AND NOT APPLIED — and why that is the whole point.</b> NIC types
/// <c>"AssAmt": { "type": "number", "minimum": 0, … }</c> and <c>"TotAmt"</c> likewise, so <b>no correct INV-01
/// exists</b> for a document carrying a line that subtracts: the schema has no vocabulary for it. Making
/// <see cref="Gstr1.ServiceLegs"/> return a SIGNED value — the obvious "fix" — would have been strictly worse than
/// doing nothing: <see cref="GstReportSupport.ServiceProjectionFoots"/> sums those magnitudes against the posted
/// party leg, and double-entry balance makes that a COMPLETE detector (a deduction of D lowers the party leg by D
/// while the magnitude sum rises by D, so they differ by exactly <b>2D</b> for every D &gt; 0). Signing the source
/// would have made the defective document foot exactly, silenced the detector, and admitted it to both the printed
/// invoice and the IRP. <c>The_detector_cannot_be_evaded_by_choosing_the_deduction_value</c> pins the 2D property
/// directly.</para>
/// </summary>
public sealed class Inv01LedgerLegPreflightTests
{
    private const string SellerGstin = "27AAPFU0939F1ZV";
    private const string BuyerGstin = "27AACCM9910C1ZK";   // distinct from the seller's: not a self-invoice
    private static readonly DateOnly FyStart = new(2026, 4, 1);
    private static readonly DateOnly SaleDate = new(2026, 9, 10);

    private const string TaxedSac = "998311";
    private const string ExemptSac = "999293";

    // ---- hand-computed. Consultancy 7,500 @ 18% ⇒ CGST 675 + SGST 675. Exempt leg 1,000.
    private const decimal Taxed = 7_500m;
    private const decimal HeadTax = 675m;
    private const decimal Leg = 1_000m;

    // ================================================================ 1 — T1-79, the deduction leg

    /// <summary>
    /// 🔴 <b>THE DEFECT.</b> A reducing (Debit-side) exempt service leg can no longer reach the payload at all, and
    /// the refusal NAMES the ledger so the operator can act on it — the footing total alone would only have said that
    /// two numbers disagree.
    /// </summary>
    [Fact]
    public void A_reducing_service_leg_is_refused_instead_of_overstating_the_registered_invoice()
    {
        var (company, sale) = ServiceInvoice(exemptSac: ExemptSac, legOnDebit: true, leg: Leg);

        var ex = Assert.Throws<InvalidOperationException>(() => EInvoiceJson.BuildInv01(company, sale));

        Assert.Contains("Exempt Income", ex.Message);          // names the leg at fault
        Assert.Contains("Debit", ex.Message);                  // and the side that makes it one
        Assert.Contains("minimum of 0", ex.Message);           // and the NIC ground for refusing rather than negating
    }

    /// <summary>
    /// 🔴 <b>THE MONEY, FROM THE POSTED ENTRY — the figures the refusal prevents, asserted rather than asserted
    /// about.</b> The posted party debit is the book's own statement of what the buyer was billed; the magnitude
    /// projection the old payload was built from is read here the same way the writer read it. Their difference is
    /// exactly twice the leg, which is both the overstatement that shipped and the reason the detector is complete.
    /// </summary>
    [Fact]
    public void The_overstatement_the_refusal_prevents_is_exactly_twice_the_reducing_leg()
    {
        var (company, sale) = ServiceInvoice(exemptSac: ExemptSac, legOnDebit: true, leg: Leg);

        // From the POSTED ENTRY: what the buyer was actually billed.
        Assert.Equal(Taxed - Leg + 2 * HeadTax, PostedPartyDebit(sale));   // 7,500 − 1,000 + 1,350 = 7,850

        // The magnitude projection the INV-01 was built from, read exactly as the writer read it.
        var magnitudeProjection = Gstr1.ServiceLegs(company, sale).Sum(l => l.Value) + 2 * HeadTax;
        Assert.Equal(Taxed + Leg + 2 * HeadTax, magnitudeProjection);      // 7,500 + 1,000 + 1,350 = 9,850

        // 9,850 against 7,850 — the invoice was overstated by 2 × 1,000, not by 1,000.
        Assert.Equal(2 * Leg, magnitudeProjection - PostedPartyDebit(sale));

        // And the books themselves are sound: this is a projection defect, not a posting defect.
        Assert.Equal(
            sale.Lines.Where(l => l.Side == DrCr.Debit).Sum(l => l.Amount.Amount),
            sale.Lines.Where(l => l.Side == DrCr.Credit).Sum(l => l.Amount.Amount));
    }

    /// <summary>
    /// 🔴 <b>THE DETECTOR IS COMPLETE, NOT A HEURISTIC.</b> No choice of deduction value makes the magnitude
    /// projection coincide with the posted party leg, because double-entry balance moves the two in opposite
    /// directions. This is what justifies exposing the sign at <see cref="Gstr1.ServiceLegs"/> rather than applying
    /// it: a signed source would have made every one of these shapes foot exactly.
    /// </summary>
    [Theory]
    [InlineData(1)]
    [InlineData(37)]
    [InlineData(2_500)]
    [InlineData(7_499)]
    public void The_detector_cannot_be_evaded_by_choosing_the_deduction_value(int legRupees)
    {
        var leg = (decimal)legRupees;
        var (company, sale) = ServiceInvoice(exemptSac: ExemptSac, legOnDebit: true, leg: leg);

        var magnitudeProjection = Gstr1.ServiceLegs(company, sale).Sum(l => l.Value) + 2 * HeadTax;
        Assert.Equal(2 * leg, magnitudeProjection - PostedPartyDebit(sale));          // the 2D property
        Assert.False(GstReportSupport.ServiceProjectionFoots(company, sale));         // so the gate always fires

        Assert.Throws<InvalidOperationException>(() => EInvoiceJson.BuildInv01(company, sale));
    }

    /// <summary>
    /// <b>CONTROL — the same leg on its NATURAL side is still emitted, and correctly.</b> The gate discriminates on
    /// the posted side, not on the presence of an exempt leg, so T1-73's restored exempt line is untouched. Asserted
    /// on the emitted bytes against the posted party debit.
    /// </summary>
    [Fact]
    public void An_adding_exempt_service_leg_is_still_emitted_and_still_foots_to_the_posted_debit()
    {
        var (company, sale) = ServiceInvoice(exemptSac: ExemptSac, legOnDebit: false, leg: Leg);

        using var payload = JsonDocument.Parse(EInvoiceJson.BuildInv01(company, sale));
        var root = payload.RootElement;
        var val = root.GetProperty("ValDtls");

        Assert.Equal(2, root.GetProperty("ItemList").GetArrayLength());
        Assert.Equal(Taxed + Leg, val.GetProperty("AssVal").GetDecimal());                 // 8,500
        Assert.Equal(Taxed + Leg + 2 * HeadTax, val.GetProperty("TotInvVal").GetDecimal()); // 9,850
        Assert.Equal(PostedPartyDebit(sale), val.GetProperty("TotInvVal").GetDecimal());    // == the posted debit
    }

    /// <summary>
    /// 🔴 <b>THE REGRESSION GUARD THAT MATTERS MOST IN THIS FILE — A NOTE REVERSES ITS INVOICE, SO ITS VALUE LEG
    /// POSTS ON THE OTHER SIDE, AND <see cref="GstReportSupport.DirectionOf"/> GROUPS THE BASE TYPES THE WRONG WAY FOR
    /// THIS QUESTION.</b>
    ///
    /// <para>The first cut of <see cref="Gstr1.NaturalServiceLegSide"/> delegated to <c>DirectionOf</c>, which is the
    /// obvious reuse and is <b>wrong on both notes</b>: it groups Sales with CreditNote (both outward supplies for
    /// tax) and Purchase with DebitNote, whereas the side a VALUE leg takes groups them the other way —
    /// Sales/DebitNote post their value Credit, Purchase/CreditNote post it Debit. Measured consequence: every leg of
    /// this ordinary ledger-only service credit note looked like a deduction and <c>BuildInv01</c> <b>REFUSED a sound
    /// document</b> — one whose <c>CoverageOf</c> is <c>Covered</c>, whose <c>DocDtls.Typ</c> is <c>"CRN"</c>, and
    /// which <see cref="GstReportSupport.ServiceProjectionFoots"/> reports <b>True</b> for.</para>
    ///
    /// <para>That is a sign error of exactly the class this slice exists to end, introduced by the fix for it and
    /// caught by probing a credit note rather than by reasoning about one. This test is the guard: it fails the moment
    /// anyone "simplifies" the four-way mapping back into <c>DirectionOf</c>.</para>
    /// </summary>
    [Fact]
    public void A_ledger_only_service_credit_note_is_still_emitted_because_a_note_reverses_its_invoice()
    {
        var (company, note) = ServiceCreditNote();

        // The mapping, asserted directly: on a CRN the value leg's natural side is DEBIT, not Credit.
        Assert.Equal(DrCr.Debit, Gstr1.NaturalServiceLegSide(company, note));
        var leg = Gstr1.ServiceLegs(company, note).Single();
        Assert.Equal(DrCr.Debit, leg.Side);
        Assert.False(Gstr1.IsDeductionServiceLeg(company, note, leg.Side));   // a reversal, not a deduction

        // The document is sound and must still be registrable.
        Assert.True(GstReportSupport.ServiceProjectionFoots(company, note));

        using var payload = JsonDocument.Parse(EInvoiceJson.BuildInv01(company, note));
        var root = payload.RootElement;
        Assert.Equal("CRN", root.GetProperty("DocDtls").GetProperty("Typ").GetString());
        Assert.Equal(TaxedSac, root.GetProperty("ItemList").EnumerateArray().Single()
            .GetProperty("HsnCd").GetString());
        Assert.Equal(Taxed, root.GetProperty("ValDtls").GetProperty("AssVal").GetDecimal());               // 7,500
        Assert.Equal(Taxed + 2 * HeadTax, root.GetProperty("ValDtls").GetProperty("TotInvVal").GetDecimal()); // 8,850

        // …and it equals what the books credited the party, read off the posted entry.
        Assert.Equal(-(Taxed + 2 * HeadTax), PostedPartyDebit(note));
    }

    // ================================================================ 2 — T1-80, the discarded footing verdict

    /// <summary>
    /// 🔴 <b>T1-80 — THE SECOND MONEY DEFECT: an ordinary trade discount overstated the registered invoice by exactly
    /// the discount, in the form NIC ACCEPTS.</b> The discount ledger carries no GST block, so it is not a service leg
    /// at all and the T1-79 sign check is silent on it — this is the footing gate's own catch, which is why resolving
    /// the two mechanisms to one mattered rather than only fixing the sign.
    /// </summary>
    [Fact]
    public void A_service_invoice_whose_legs_do_not_add_up_to_the_posted_debit_is_refused()
    {
        var (company, sale) = DiscountedServiceInvoice();

        // The books: the buyer was billed 8,850 (7,500 + 1,000 exempt − 1,000 discount + 1,350 tax).
        Assert.Equal(Taxed + Leg - Leg + 2 * HeadTax, PostedPartyDebit(sale));   // 8,850

        // What the payload declared instead: Σ AssAmt 8,500 ⇒ TotInvVal 9,850. Overstated by exactly the discount,
        // and self-consistent, so the IRP would have registered it.
        var projected = Gstr1.ServiceLegs(company, sale).Sum(l => l.Value) + 2 * HeadTax;
        Assert.Equal(Taxed + Leg + 2 * HeadTax, projected);                      // 9,850
        Assert.Equal(Leg, projected - PostedPartyDebit(sale));                   // over by the 1,000 discount

        // Not the sign defect: no service leg is a deduction here.
        Assert.All(
            Gstr1.ServiceLegs(company, sale),
            l => Assert.False(Gstr1.IsDeductionServiceLeg(company, sale, l.Side)));

        var ex = Assert.Throws<InvalidOperationException>(() => EInvoiceJson.BuildInv01(company, sale));
        Assert.Contains("do not add up to the amount recorded against the party", ex.Message);
    }

    /// <summary>
    /// 🔴 <b>ONE MECHANISM, NOT TWO.</b> The verdict the payload is now gated on is the SAME
    /// <see cref="GstReportSupport.ServiceProjectionFoots"/> that already gates the printed tax invoice — so the paper
    /// and the IRP document can no longer disagree about whether a voucher is a tax invoice at all. Pinned in both
    /// directions so a future change cannot quietly give the payload its own second opinion.
    /// </summary>
    [Fact]
    public void The_payload_and_the_printed_invoice_now_share_one_footing_verdict()
    {
        var (badCompany, bad) = DiscountedServiceInvoice();
        Assert.False(GstReportSupport.ServiceProjectionFoots(badCompany, bad));
        Assert.False(GstReportSupport.IsTaxInvoice(badCompany, bad));            // the printer already refused it
        Assert.Throws<InvalidOperationException>(() => EInvoiceJson.BuildInv01(badCompany, bad));

        var (goodCompany, good) = ServiceInvoice(exemptSac: ExemptSac, legOnDebit: false, leg: Leg);
        Assert.True(GstReportSupport.ServiceProjectionFoots(goodCompany, good));
        Assert.True(GstReportSupport.IsTaxInvoice(goodCompany, good));
        Assert.NotEmpty(EInvoiceJson.BuildInv01(goodCompany, good));
    }

    // ================================================================ 3 — T1-81, the blank mandatory HsnCd

    /// <summary>
    /// 🔴 <b>T1-81 on the EXEMPT service branch</b> — the line T1-73 newly introduced. A ledger that declares a GST
    /// block but no HSN/SAC emitted <c>"HsnCd": ""</c>, which NIC's own required list and <c>minLength: 4</c> make an
    /// IRP rejection. Refused, naming the ledger the operator must correct.
    /// </summary>
    [Fact]
    public void An_exempt_service_ledger_with_no_declared_SAC_is_refused_rather_than_emitting_a_blank_HsnCd()
    {
        var (company, sale) = ServiceInvoice(exemptSac: null, legOnDebit: false, leg: Leg);

        // The premise: the ledger declares a GST block, so the SAC is a field that CAN be filled.
        var exempt = Gstr1.ServiceLegs(company, sale)
            .Single(l => Gstr1.IsNonTaxableServiceLedger(l.Ledger)).Ledger;
        Assert.NotNull(exempt.SalesPurchaseGst);
        Assert.Null(Gstr1.ServiceSacOf(exempt));

        var ex = Assert.Throws<InvalidOperationException>(() => EInvoiceJson.BuildInv01(company, sale));
        Assert.Contains("Exempt Income", ex.Message);
        Assert.Contains("no HSN/SAC code", ex.Message);
    }

    /// <summary>
    /// 🔴 <b>T1-81 on the TAXABLE service branch too — the root was swept, not the one branch that was reported.</b>
    /// This branch shipped the identical blank <c>HsnCd</c> long before the exempt line existed, and fixing only the
    /// newly-reported one would have left the same rejected payload reachable by the commoner route.
    /// </summary>
    [Fact]
    public void A_taxable_service_ledger_with_no_declared_SAC_is_refused_on_the_same_ground()
    {
        var (company, sale) = TaxableOnlyServiceInvoice(taxedSac: null);

        var ex = Assert.Throws<InvalidOperationException>(() => EInvoiceJson.BuildInv01(company, sale));
        Assert.Contains("Consultancy Income", ex.Message);
        Assert.Contains("no HSN/SAC code", ex.Message);
    }

    /// <summary>
    /// 🔴 <b>SCOPE CONTROL, AND A DELIBERATE REFUSAL TO WIDEN.</b> The plain As-Voucher ledger-only sale still emits
    /// its synthetic line with a blank <c>HsnCd</c>. Its sales ledger declares <b>no GST block at all</b>, so there is
    /// nowhere in the domain model to put a code: the blank is not correctable master data but a structural gap, and
    /// refusing it would withdraw the ability to e-invoice a plain ledger-only sale outright. That is a capability
    /// decision for the user, not this writer's, and it remains the standing documented divergence pinned by
    /// <c>EInvoiceInv01SchemaConformanceTests.</c>
    /// <c>PINNED_an_income_ledger_with_no_declared_HSN_or_SAC_still_emits_an_empty_HsnCd</c>. This test exists so the
    /// boundary is asserted rather than merely intended.
    /// </summary>
    [Fact]
    public void PINNED_the_plain_as_voucher_synthetic_line_still_emits_its_blank_HsnCd_unchanged()
    {
        var (company, sale) = PlainAsVoucherSale();

        using var payload = JsonDocument.Parse(EInvoiceJson.BuildInv01(company, sale));
        var root = payload.RootElement;
        var item = root.GetProperty("ItemList").EnumerateArray().Single();

        Assert.Empty(Gstr1.ServiceLegs(company, sale));                       // no SAC leg exists to name
        Assert.Equal("", item.GetProperty("HsnCd").GetString());              // the pinned divergence, unmoved
        Assert.Equal("N", item.GetProperty("IsServc").GetString());
        Assert.Equal(Taxed, root.GetProperty("ValDtls").GetProperty("AssVal").GetDecimal());
        Assert.Equal(PostedPartyDebit(sale), root.GetProperty("ValDtls").GetProperty("TotInvVal").GetDecimal());
    }

    // ================================================================ helpers

    /// <summary>The voucher's own posted party DEBIT — the book's statement of what the buyer was billed.</summary>
    private static decimal PostedPartyDebit(Voucher v)
    {
        var partyId = Assert.IsType<Guid>(v.PartyId);
        var legs = v.Lines.Where(l => l.LedgerId == partyId).ToList();
        Assert.NotEmpty(legs);                                                 // non-vacuity
        return legs.Sum(l => l.Side == DrCr.Debit ? l.Amount.Amount : -l.Amount.Amount);
    }

    // ================================================================ fixtures

    /// <summary>
    /// One intra-State ACCOUNTING (service) invoice: a taxed SAC leg plus one non-taxable SAC leg which is posted on
    /// the natural (Credit) side or, when <paramref name="legOnDebit"/>, on the reducing (Debit) side. Posted through
    /// <see cref="LedgerService.Post"/>, so a shape the engine cannot create fails the fixture rather than passing it.
    /// </summary>
    private static (Company Company, Voucher Sale) ServiceInvoice(string? exemptSac, bool legOnDebit, decimal leg)
    {
        var c = SeededGstCompany("Preflight Service Co");

        var consultancy = Add(c, "Consultancy Income", "Sales Accounts", false);
        consultancy.SalesPurchaseGst = new StockItemGstDetails
        {
            HsnSac = TaxedSac, SupplyType = GstSupplyType.Services,
            Taxability = GstTaxability.Taxable, RateBasisPoints = 1800,
        };
        var exempt = Add(c, "Exempt Income", "Sales Accounts", false);
        exempt.SalesPurchaseGst = new StockItemGstDetails
        {
            HsnSac = exemptSac, SupplyType = GstSupplyType.Services, Taxability = GstTaxability.Exempt,
        };
        var buyer = Buyer(c);

        var tax = TaxOn(c, Taxed);

        // A Debit-side leg REDUCES what the party owes; a Credit-side one adds to it. Either way the voucher balances.
        var party = legOnDebit ? Taxed - leg + 2 * HeadTax : Taxed + leg + 2 * HeadTax;
        var lines = new List<EntryLine>
        {
            new(buyer.Id, new Money(party), DrCr.Debit),
            new(consultancy.Id, new Money(Taxed), DrCr.Credit),
            new(exempt.Id, new Money(leg), legOnDebit ? DrCr.Debit : DrCr.Credit),
        };
        lines.AddRange(tax.TaxLines);

        return (c, PostSale(c, lines, buyer.Id, isAccountingInvoice: true));
    }

    /// <summary>
    /// T1-80's shape: the same invoice less an ordinary ₹1,000 trade discount posted Dr to a ledger carrying <b>no</b>
    /// GST block — so it is invisible to <see cref="Gstr1.ServiceLegs"/>, the sign check cannot see it, and only the
    /// footing reconciliation catches that the projection now exceeds the posted party debit.
    /// </summary>
    private static (Company Company, Voucher Sale) DiscountedServiceInvoice()
    {
        var c = SeededGstCompany("Preflight Discount Co");

        var consultancy = Add(c, "Consultancy Income", "Sales Accounts", false);
        consultancy.SalesPurchaseGst = new StockItemGstDetails
        {
            HsnSac = TaxedSac, SupplyType = GstSupplyType.Services,
            Taxability = GstTaxability.Taxable, RateBasisPoints = 1800,
        };
        var exempt = Add(c, "Exempt Income", "Sales Accounts", false);
        exempt.SalesPurchaseGst = new StockItemGstDetails
        {
            HsnSac = ExemptSac, SupplyType = GstSupplyType.Services, Taxability = GstTaxability.Exempt,
        };
        var discount = Add(c, "Discount Allowed", "Indirect Expenses", true);   // NO SalesPurchaseGst block
        var buyer = Buyer(c);

        var tax = TaxOn(c, Taxed);

        var lines = new List<EntryLine>
        {
            new(buyer.Id, new Money(Taxed + Leg - Leg + 2 * HeadTax), DrCr.Debit),  // 8,850
            new(discount.Id, new Money(Leg), DrCr.Debit),
            new(consultancy.Id, new Money(Taxed), DrCr.Credit),
            new(exempt.Id, new Money(Leg), DrCr.Credit),
        };
        lines.AddRange(tax.TaxLines);

        return (c, PostSale(c, lines, buyer.Id, isAccountingInvoice: true));
    }

    private static (Company Company, Voucher Sale) TaxableOnlyServiceInvoice(string? taxedSac)
    {
        var c = SeededGstCompany("Preflight Taxable Service Co");

        var consultancy = Add(c, "Consultancy Income", "Sales Accounts", false);
        consultancy.SalesPurchaseGst = new StockItemGstDetails
        {
            HsnSac = taxedSac, SupplyType = GstSupplyType.Services,
            Taxability = GstTaxability.Taxable, RateBasisPoints = 1800,
        };
        var buyer = Buyer(c);

        var tax = TaxOn(c, Taxed);
        var lines = new List<EntryLine>
        {
            new(buyer.Id, new Money(Taxed + 2 * HeadTax), DrCr.Debit),
            new(consultancy.Id, new Money(Taxed), DrCr.Credit),
        };
        lines.AddRange(tax.TaxLines);

        return (c, PostSale(c, lines, buyer.Id, isAccountingInvoice: true));
    }

    /// <summary>
    /// An ordinary ledger-only SERVICE CREDIT NOTE — the sales return that reverses the invoice: income
    /// <b>Debited</b>, party <b>Credited</b>, the forward tax <b>Debited</b> (carrying its <c>GstLineTax</c>
    /// metadata, so the rate group is still read off the posted lines).
    /// </summary>
    private static (Company Company, Voucher Note) ServiceCreditNote()
    {
        var c = SeededGstCompany("Preflight Credit Note Co");

        var consultancy = Add(c, "Consultancy Income", "Sales Accounts", false);
        consultancy.SalesPurchaseGst = new StockItemGstDetails
        {
            HsnSac = TaxedSac, SupplyType = GstSupplyType.Services,
            Taxability = GstTaxability.Taxable, RateBasisPoints = 1800,
        };
        var buyer = Buyer(c);
        var tax = TaxOn(c, Taxed);

        var lines = new List<EntryLine>
        {
            new(buyer.Id, new Money(Taxed + 2 * HeadTax), DrCr.Credit),      // the party is CREDITED
            new(consultancy.Id, new Money(Taxed), DrCr.Debit),               // income is DEBITED back
        };
        foreach (var t in tax.TaxLines)
            lines.Add(new EntryLine(t.LedgerId, t.Amount, DrCr.Debit, gst: t.Gst));

        var note = new LedgerService(c).Post(new Voucher(
            Guid.NewGuid(), c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.CreditNote).Id, SaleDate, lines,
            partyId: buyer.Id, isAccountingInvoice: true));

        Assert.Equal(EInvoiceCoverage.Covered, new EInvoiceService(c).CoverageOf(note));
        return (c, note);
    }

    /// <summary>A plain As-Voucher ledger-only sale: the income ledger declares no GST block at all.</summary>
    private static (Company Company, Voucher Sale) PlainAsVoucherSale()
    {
        var c = SeededGstCompany("Preflight Plain Co");
        var sales = Add(c, "Sales", "Sales Accounts", false);                   // NO SalesPurchaseGst block
        var buyer = Buyer(c);

        var tax = TaxOn(c, Taxed);
        var lines = new List<EntryLine>
        {
            new(buyer.Id, new Money(Taxed + 2 * HeadTax), DrCr.Debit),
            new(sales.Id, new Money(Taxed), DrCr.Credit),
        };
        lines.AddRange(tax.TaxLines);

        return (c, PostSale(c, lines, buyer.Id, isAccountingInvoice: false));
    }

    private static GstService.InvoiceTax TaxOn(Company c, decimal taxable)
    {
        var tax = new GstService(c).ComputeInvoiceTax(
            new[] { new GstService.TaxableLine(new Money(taxable), 1800) },
            interState: false, GstTaxDirection.Output);
        Assert.Equal(2 * HeadTax, tax.TaxLines.Sum(l => l.Amount.Amount));      // the fixture's own premise
        return tax;
    }

    private static Voucher PostSale(Company c, List<EntryLine> lines, Guid partyId, bool isAccountingInvoice)
    {
        var sale = new LedgerService(c).Post(new Voucher(
            Guid.NewGuid(), c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Sales).Id, SaleDate, lines,
            partyId: partyId, isAccountingInvoice: isAccountingInvoice));

        // The IRP path is genuinely reachable for every fixture here — none is a payload built for a document the app
        // would never e-invoice, which is what makes each refusal a refusal of something that used to be emitted.
        Assert.Equal(EInvoiceCoverage.Covered, new EInvoiceService(c).CoverageOf(sale));
        return sale;
    }

    private static Company SeededGstCompany(string name)
    {
        var c = CompanyFactory.CreateSeeded(name, FyStart);
        c.Address = "Unit 4, Fort Industrial Estate\nBallard Pier";
        c.Pin = "400001";
        new GstService(c).EnableGst(new GstConfig
        {
            HomeStateCode = "27", Gstin = SellerGstin, RegistrationType = GstRegistrationType.Regular,
            ApplicableFrom = FyStart, Periodicity = GstReturnPeriodicity.Monthly,
            EInvoicingEnabled = true, EInvoiceApplicableFrom = FyStart,
        });
        return c;
    }

    private static Domain.Ledger Buyer(Company c)
    {
        var buyer = Add(c, "Buyer", "Sundry Debtors", true);
        buyer.PartyGst = new PartyGstDetails
        { RegistrationType = GstRegistrationType.Regular, Gstin = BuyerGstin, StateCode = "27" };
        buyer.Mailing = new PartyMailingDetails
        {
            MailingName = "Buyer Trading Co", Address = "12 Residency Road\nShivajinagar",
            Country = "India", Pincode = "400012",
        };
        return buyer;
    }

    private static Domain.Ledger Add(Company c, string name, string groupName, bool openingIsDebit)
    {
        var l = new Domain.Ledger(Guid.NewGuid(), name, c.FindGroupByName(groupName)!.Id, Money.Zero, openingIsDebit);
        c.AddLedger(l);
        return l;
    }
}
