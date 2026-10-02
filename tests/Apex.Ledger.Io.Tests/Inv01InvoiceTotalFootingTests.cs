using System.Text.Json;
using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Reports;
using Apex.Ledger.Services;
using Xunit;

namespace Apex.Ledger.Io.Tests;

/// <summary>
/// 🔴 <b>T1-73 — THE INV-01 WE REGISTER WITH THE IRP WAS UNDERSTATED BY EXACTLY THE EXEMPT VALUE, ON BOTH OF ITS
/// TWO PATHS, AGAINST RULES NIC PUBLISHES VERBATIM.</b>
///
/// <para><b>What was measured off the emitted bytes, before the fix.</b> On a Widget ₹50,000 @ 18% (HSN 847130) +
/// EXEMPT Fresh Milk ₹20,000 (HSN 040110) intra-State tax invoice, <c>BuildInv01</c> emitted
/// <c>ValDtls.AssVal</c> <b>50,000</b> and <c>TotInvVal</c> <b>59,000</b> against an <c>ItemList</c> whose
/// <c>AssAmt</c> summed to <b>70,000</b> and whose <c>TotItemVal</c> summed to <b>79,000</b> — and 79,000 is the
/// voucher's <b>own posted party debit</b>, i.e. what the buyer was billed. Short by exactly the ₹20,000 of exempt
/// goods, in the understating direction, on the document the buyer's ITC and our own declared turnover rest on.</para>
///
/// <para><b>The SECOND path, measured here for the first time.</b> On Consultancy ₹10,000 @ 18% (SAC 998311) +
/// EXEMPT Education ₹5,000 (SAC 999293) the payload carried <b>ONE</b> item, <c>AssVal</c> 10,000 and
/// <c>TotInvVal</c> <b>11,800</b> against a posted party debit of <b>16,800</b>. Same defect, same ₹-for-₹ shape,
/// different mechanism: <c>ServiceLegsByRate</c> correctly kept the exempt leg out of every rate group and then
/// dropped it from the <c>ItemList</c> as well, so the registered invoice did not describe a supply the buyer paid
/// for. And because Σ <c>AssAmt</c> agreed with <c>AssVal</c>, this one <b>NIC WOULD HAVE ACCEPTED</b> — an accepted
/// wrong invoice being worse than a refused one.</para>
///
/// <para><b>🔴 NIC PUBLISHES BOTH EQUALITIES — this is not a reading.</b> Retrieved and checked by content at
/// <c>https://einv-apisandbox.nic.in/version1.03/generate-irn.html</c> (HTTP 200), under "The following summation
/// validations are to be done on Invoice total":
/// <list type="bullet">
/// <item><i>"Total Taxable Value = Taxable Value of all Items"</i></item>
/// <item><i>"Total Invoice Value = Sum of All Total Value of Items - Invoice Discount + Invoice Other charges +
/// Round-off amount"</i></item>
/// </list>
/// The published schema on the same page describes <c>AssVal</c> as <i>"Total Assessable value of all items"</i> and
/// the item member <c>AssAmt</c> as <i>"Taxable Value (Total Amount -Discount)"</i>, and types
/// <c>"GstRt": { "type": "number", "minimum": 0, "maximum": 999.999 }</c> — so 0 is inside its declared domain and an
/// exempt line is a legal <c>ItemList</c> entry. <c>ValDtls</c> has <b>no exempt-value member at all</b>
/// (<c>required: [AssVal, TotInvVal]</c>; its properties are AssVal, CgstVal, SgstVal, IgstVal, CesVal, StCesVal,
/// Discount, OthChrg, RndOffAmt, TotInvVal, TotInvValFc), so on this document the exempt line's value has nowhere to
/// go but <c>AssVal</c>. v1.01 of the same page states the second rule as <i>"Total Invoice Value = Total Taxable
/// Value + Total SGST Value + Total CGST Value + Total IGST Value + Total Cess Value + …"</i>, which is the shape the
/// writer assembles <c>TotInvVal</c> in — so once <c>AssVal</c> foots to the <c>ItemList</c>, BOTH published
/// formulations hold at once.</para>
///
/// <para><b>The published tolerance is a RUPEE, which is what makes a ₹20,000 gap a filing error rather than a
/// rounding one.</b> Verbatim: <i>"Minimum value is considered as the rupee part of the calculated value minus one
/// rupee and maximum value is taken as the rounded up to next rupee value of the calculated value plus one
/// rupee."</i> For a calculated 70,000 that admits [69,999.00, 70,001.00]; the emitted 50,000 missed it by
/// 19,999.00.</para>
///
/// <para><b>🔴 THIS IS NOT THE EWB-01 QUESTION, and the distinction is deliberate.</b> There NIC describes
/// <c>totalValue</c> as <i>"Sum of Taxable value"</i>, publishes <b>no</b> rule tying it to
/// Σ <c>itemList.taxableAmount</c>, and Rule 138(1) Explanation 2 expressly excludes exempt value from the
/// consignment value — so the e-Way reading is a separately-recorded, labelled divergence and is left alone. No
/// Rule-138 exclusion reaches a tax invoice.</para>
///
/// <para><b>Why the suite could not see it.</b> <c>EInvoiceInv01SchemaConformanceTests.</c>
/// <c>Item_values_foot_to_the_document_totals</c> already asserted exactly these two identities over "every" fixture
/// — and passed, because no fixture mixed an exempt line in. Those two shapes are now enrolled in that fixture, so
/// the standing invariant guards them too; these tests pin the absolute hand-computed rupees beside it, because an
/// identity test alone cannot tell a correct total from two consistent wrong ones.</para>
/// </summary>
public sealed class Inv01InvoiceTotalFootingTests
{
    private const string Gstin = "27AAPFU0939F1ZV";
    private static readonly DateOnly FyStart = new(2026, 4, 1);
    private static readonly DateOnly SaleDate = new(2026, 9, 10);

    private const string TaxedHsn = "847130";
    private const string ExemptHsn = "040110";
    private const string TaxedSac = "998311";
    private const string ExemptSac = "999293";

    // ---- hand-computed, GOODS. Widget 2,500 Nos × ₹20 = 50,000 @ 18% ⇒ CGST 4,500 + SGST 4,500 (ONE 1800 group,
    // because the posting excludes the exempt line). Milk 2,000 Nos × ₹10 = 20,000, exempt, bears no tax.
    //   Σ ItemList.AssAmt     = 50,000 + 20,000            = 70,000  ⇒ AssVal
    //   Σ ItemList.TotItemVal = (50,000+4,500+4,500) + 20,000 = 79,000  ⇒ TotInvVal
    //   posted party debit    = 70,000 + 9,000             = 79,000
    private const decimal GoodsTaxedValue = 50_000m;
    private const decimal GoodsExemptValue = 20_000m;
    private const decimal GoodsHeadTax = 4_500m;
    private const decimal GoodsAssVal = 70_000m;
    private const decimal GoodsTotInvVal = 79_000m;

    // ---- hand-computed, SERVICES. Consultancy 10,000 @ 18% ⇒ CGST 900 + SGST 900. Education 5,000, exempt.
    //   Σ AssAmt = 15,000 ⇒ AssVal.  Σ TotItemVal = 11,800 + 5,000 = 16,800 ⇒ TotInvVal = posted party debit.
    private const decimal ServiceTaxedValue = 10_000m;
    private const decimal ServiceExemptValue = 5_000m;
    private const decimal ServiceHeadTax = 900m;
    private const decimal ServiceAssVal = 15_000m;
    private const decimal ServiceTotInvVal = 16_800m;

    // ================================================================ 1 — the money, absolutely

    /// <summary>
    /// 🔴 <b>THE DEFECT, ON THE EMITTED BYTES.</b> Absolute hand-computed rupees, not an identity: a payload whose
    /// <c>AssVal</c> and <c>TotInvVal</c> were BOTH short by the same 20,000 satisfied every footing check the suite
    /// had, which is precisely why this assertion is written as two literal figures.
    /// </summary>
    [Fact]
    public void The_registered_invoice_total_is_the_whole_invoice_and_not_only_its_taxed_part()
    {
        var (company, sale) = MixedGoodsInvoice();

        using var payload = JsonDocument.Parse(EInvoiceJson.BuildInv01(company, sale));
        var val = payload.RootElement.GetProperty("ValDtls");

        Assert.Equal(GoodsAssVal, val.GetProperty("AssVal").GetDecimal());        // was 50,000
        Assert.Equal(GoodsTotInvVal, val.GetProperty("TotInvVal").GetDecimal());  // was 59,000
    }

    /// <summary>
    /// 🔴 The registered invoice must declare what the buyer was BILLED. The voucher's own posted party debit is the
    /// book's statement of that, so this reads it off the posted entry rather than off a second computation of the
    /// payload — the one assertion that could not be satisfied by any self-consistent pair of wrong totals.
    /// </summary>
    [Fact]
    public void The_INV01_total_equals_the_debit_the_buyer_was_actually_billed()
    {
        var (company, sale) = MixedGoodsInvoice();

        var postedPartyDebit = sale.Lines
            .Where(l => l.LedgerId == sale.PartyId && l.Side == DrCr.Debit)
            .Sum(l => l.Amount.Amount);
        Assert.Equal(GoodsTotInvVal, postedPartyDebit);   // the fixture's own premise, held at read time

        using var payload = JsonDocument.Parse(EInvoiceJson.BuildInv01(company, sale));
        Assert.Equal(
            postedPartyDebit,
            payload.RootElement.GetProperty("ValDtls").GetProperty("TotInvVal").GetDecimal());
    }

    /// <summary>
    /// Both of NIC's published invoice-total summations, asserted as NIC states them — inside the tolerance NIC
    /// states, which is one rupee either side of the calculated value and not the ₹20,000 the payload was out by.
    /// </summary>
    [Fact]
    public void Both_published_NIC_invoice_total_summations_hold_within_the_published_rupee_tolerance()
    {
        var (company, sale) = MixedGoodsInvoice();

        using var payload = JsonDocument.Parse(EInvoiceJson.BuildInv01(company, sale));
        var root = payload.RootElement;
        var items = root.GetProperty("ItemList").EnumerateArray().ToList();
        var val = root.GetProperty("ValDtls");
        decimal Sum(string member) => items.Sum(i => i.GetProperty(member).GetDecimal());

        // "Total Taxable Value = Taxable Value of all Items"
        AssertWithinNicTolerance("AssVal", Sum("AssAmt"), val.GetProperty("AssVal").GetDecimal());
        // "Total Invoice Value = Sum of All Total Value of Items - Invoice Discount + Invoice Other charges +
        //  Round-off amount" — this writer emits no invoice-level Discount / OthChrg / RndOffAmt, so the subtrahend
        //  and the two addends are zero and the rule reduces to Σ TotItemVal.
        AssertWithinNicTolerance("TotInvVal", Sum("TotItemVal"), val.GetProperty("TotInvVal").GetDecimal());

        // And the per-head rules in the same published list, which the exempt line must not disturb.
        Assert.Equal(Sum("CgstAmt"), val.GetProperty("CgstVal").GetDecimal());
        Assert.Equal(Sum("SgstAmt"), val.GetProperty("SgstVal").GetDecimal());
        Assert.Equal(Sum("IgstAmt"), val.GetProperty("IgstVal").GetDecimal());

        // The pre-fix figures, shown to be OUTSIDE the band this test enforces — so the tolerance is not a
        // formality that the defect would also have satisfied.
        Assert.False(IsWithinNicTolerance(calculated: GoodsAssVal, passed: 50_000m));
        Assert.False(IsWithinNicTolerance(calculated: GoodsTotInvVal, passed: 59_000m));
    }

    // ================================================================ 2 — the ledger path

    /// <summary>
    /// 🔴 <b>THE SECOND PATH.</b> An exempt SERVICE leg is a line of the invoice and must appear on the document the
    /// IRP registers — Rule 46 requires the tax invoice to state the description and value of the services supplied,
    /// and Rule 48(4) makes this payload that invoice. It bears <c>GstRt</c> 0 and no tax, which is how its value
    /// joins <c>AssVal</c> without joining a posted rate group.
    /// </summary>
    [Fact]
    public void An_exempt_service_leg_is_an_INV01_line_and_its_value_reaches_the_invoice_total()
    {
        var (company, sale) = MixedServiceInvoice();

        using var payload = JsonDocument.Parse(EInvoiceJson.BuildInv01(company, sale));
        var root = payload.RootElement;

        Assert.Equal(2, root.GetProperty("ItemList").GetArrayLength());   // was 1 — the exempt leg was dropped

        var education = ItemBySac(root, ExemptSac);
        Assert.Equal(ServiceExemptValue, education.GetProperty("AssAmt").GetDecimal());
        Assert.Equal(ServiceExemptValue, education.GetProperty("TotAmt").GetDecimal());
        Assert.Equal(ServiceExemptValue, education.GetProperty("TotItemVal").GetDecimal());
        Assert.Equal(0m, education.GetProperty("GstRt").GetDecimal());
        Assert.Equal(0m, education.GetProperty("CgstAmt").GetDecimal());
        Assert.Equal(0m, education.GetProperty("SgstAmt").GetDecimal());
        Assert.Equal(0m, education.GetProperty("IgstAmt").GetDecimal());
        Assert.Equal("Y", education.GetProperty("IsServc").GetString());   // its ledger declares Services

        var val = root.GetProperty("ValDtls");
        Assert.Equal(ServiceAssVal, val.GetProperty("AssVal").GetDecimal());         // was 10,000
        Assert.Equal(ServiceTotInvVal, val.GetProperty("TotInvVal").GetDecimal());   // was 11,800

        var postedPartyDebit = sale.Lines
            .Where(l => l.LedgerId == sale.PartyId && l.Side == DrCr.Debit)
            .Sum(l => l.Amount.Amount);
        Assert.Equal(ServiceTotInvVal, postedPartyDebit);
        Assert.Equal(postedPartyDebit, val.GetProperty("TotInvVal").GetDecimal());
    }

    // ================================================================ 3 — the controls

    /// <summary>
    /// The fix must be bought by stating the exempt VALUE, never by inventing or moving TAX. Σ of the item tax still
    /// equals the tax actually posted, and the taxed line of each path keeps the whole of its own group's tax.
    /// </summary>
    [Fact]
    public void No_tax_is_invented_or_lost_by_putting_the_exempt_value_into_the_total()
    {
        var (goodsCo, goodsSale) = MixedGoodsInvoice();
        using var goods = JsonDocument.Parse(EInvoiceJson.BuildInv01(goodsCo, goodsSale));
        var widget = ItemByHsn(goods.RootElement, TaxedHsn);
        Assert.Equal(GoodsTaxedValue, widget.GetProperty("AssAmt").GetDecimal());
        Assert.Equal(18m, widget.GetProperty("GstRt").GetDecimal());
        Assert.Equal(GoodsHeadTax, widget.GetProperty("CgstAmt").GetDecimal());
        Assert.Equal(GoodsHeadTax, widget.GetProperty("SgstAmt").GetDecimal());
        Assert.Equal(2 * GoodsHeadTax, TotalItemTax(goods.RootElement));
        // NIC's own item identity, which the exempt line must not make false on its neighbour.
        Assert.Equal(
            widget.GetProperty("AssAmt").GetDecimal() * widget.GetProperty("GstRt").GetDecimal() / 200m,
            widget.GetProperty("CgstAmt").GetDecimal());

        var (serviceCo, serviceSale) = MixedServiceInvoice();
        using var svc = JsonDocument.Parse(EInvoiceJson.BuildInv01(serviceCo, serviceSale));
        var consultancy = ItemBySac(svc.RootElement, TaxedSac);
        Assert.Equal(ServiceTaxedValue, consultancy.GetProperty("AssAmt").GetDecimal());
        Assert.Equal(18m, consultancy.GetProperty("GstRt").GetDecimal());
        Assert.Equal(ServiceHeadTax, consultancy.GetProperty("CgstAmt").GetDecimal());
        Assert.Equal(ServiceHeadTax, consultancy.GetProperty("SgstAmt").GetDecimal());
        Assert.Equal(2 * ServiceHeadTax, TotalItemTax(svc.RootElement));
    }

    /// <summary>
    /// The regression control. On a WHOLLY TAXABLE invoice the new source of <c>AssVal</c> — Σ of the emitted
    /// <c>AssAmt</c> — must give the same figure as the old one, <c>GstReportSupport.InvoiceTaxableValue</c>. This is
    /// what shows the change is confined to the shape that carries a non-taxable line, and it is asserted against the
    /// old projection itself rather than against a literal.
    /// </summary>
    [Fact]
    public void A_wholly_taxable_invoice_declares_exactly_what_the_old_projection_declared()
    {
        var (company, sale) = WhollyTaxableInvoice();

        using var payload = JsonDocument.Parse(EInvoiceJson.BuildInv01(company, sale));
        var val = payload.RootElement.GetProperty("ValDtls");

        var oldProjection = GstReportSupport.InvoiceTaxableValue(sale).Amount;
        Assert.Equal(GoodsTaxedValue, oldProjection);                              // non-vacuity
        Assert.Equal(oldProjection, val.GetProperty("AssVal").GetDecimal());
        Assert.Equal(GoodsTaxedValue + 2 * GoodsHeadTax, val.GetProperty("TotInvVal").GetDecimal());
    }

    // ================================================================ NIC's published tolerance

    /// <summary>
    /// NIC, verbatim: <i>"Minimum value is considered as the rupee part of the calculated value minus one rupee and
    /// maximum value is taken as the rounded up to next rupee value of the calculated value plus one rupee."</i>
    /// </summary>
    private static bool IsWithinNicTolerance(decimal calculated, decimal passed)
    {
        var min = Math.Truncate(calculated) - 1m;
        var max = Math.Ceiling(calculated) + 1m;
        return passed >= min && passed <= max;
    }

    private static void AssertWithinNicTolerance(string member, decimal calculated, decimal passed) =>
        Assert.True(
            IsWithinNicTolerance(calculated, passed),
            $"{member}: NIC's published summation gives {calculated}, so the passed value must lie in " +
            $"[{Math.Truncate(calculated) - 1m}, {Math.Ceiling(calculated) + 1m}] — the payload passed {passed}, " +
            $"out by {passed - calculated}.");

    private static decimal TotalItemTax(JsonElement root) =>
        root.GetProperty("ItemList").EnumerateArray()
            .Sum(i => i.GetProperty("CgstAmt").GetDecimal()
                    + i.GetProperty("SgstAmt").GetDecimal()
                    + i.GetProperty("IgstAmt").GetDecimal());

    private static JsonElement ItemByHsn(JsonElement root, string hsn) =>
        root.GetProperty("ItemList").EnumerateArray().Single(i => i.GetProperty("HsnCd").GetString() == hsn);

    private static JsonElement ItemBySac(JsonElement root, string sac) =>
        root.GetProperty("ItemList").EnumerateArray().Single(i => i.GetProperty("HsnCd").GetString() == sac);

    // ================================================================ fixtures

    /// <summary>
    /// One intra-State ITEM tax invoice mixing a taxed and an EXEMPT stock line, posted through
    /// <see cref="LedgerService.Post"/>. The exempt value is posted into the party debit and the sales credit but
    /// kept OUT of the tax base, which is exactly what <c>VoucherEntryViewModel.ComputeItemInvoiceGst</c> does — so
    /// the posted party debit is the book's own statement of the invoice total.
    /// </summary>
    private static (Company Company, Voucher Sale) MixedGoodsInvoice() => GoodsInvoice(withExemptLine: true);

    /// <summary>The same fixture with the exempt line removed — the ordinary, wholly taxable shape.</summary>
    private static (Company Company, Voucher Sale) WhollyTaxableInvoice() => GoodsInvoice(withExemptLine: false);

    private static (Company Company, Voucher Sale) GoodsInvoice(bool withExemptLine)
    {
        var c = SeededGstCompany("Mixed Exempt Total Co");
        var inv = new InventoryService(c);
        var grp = inv.CreateStockGroup("Goods");
        var nos = inv.CreateSimpleUnit("Nos", "Numbers", unitQuantityCode: "NOS");

        var widget = inv.CreateStockItem("Widget", grp.Id, nos.Id);
        widget.Gst = new StockItemGstDetails
        { HsnSac = TaxedHsn, Taxability = GstTaxability.Taxable, RateBasisPoints = 1800 };
        var milk = inv.CreateStockItem("Fresh Milk", grp.Id, nos.Id);
        milk.Gst = new StockItemGstDetails { HsnSac = ExemptHsn, Taxability = GstTaxability.Exempt };

        var main = c.MainLocation!.Id;
        inv.AddOpeningBalance(widget.Id, main, 10_000m, Money.FromRupees(20m));
        inv.AddOpeningBalance(milk.Id, main, 10_000m, Money.FromRupees(10m));

        var sales = Add(c, "Sales", "Sales Accounts", false);
        var buyer = Buyer(c);

        var exemptValue = withExemptLine ? GoodsExemptValue : 0m;
        var tax = new GstService(c).ComputeInvoiceTax(
            new[] { new GstService.TaxableLine(Money.FromRupees(GoodsTaxedValue), 1800) },
            interState: false, GstTaxDirection.Output);
        Assert.Equal(2 * GoodsHeadTax, tax.TaxLines.Sum(l => l.Amount.Amount));   // the fixture's own premise

        var lines = new List<EntryLine>
        {
            new(buyer.Id, new Money(GoodsTaxedValue + exemptValue + 2 * GoodsHeadTax), DrCr.Debit),
            new(sales.Id, new Money(GoodsTaxedValue + exemptValue), DrCr.Credit),
        };
        lines.AddRange(tax.TaxLines);

        var inventory = new List<VoucherInventoryLine>
        {
            new(widget.Id, main, 2_500m, Money.FromRupees(20m)),      // 50,000
        };
        // Ordered LAST, as the wave-38 measurement had it: under the pre-T1-59 collapse this was the rate group's
        // remainder-absorbing line.
        if (withExemptLine) inventory.Add(new VoucherInventoryLine(milk.Id, main, 2_000m, Money.FromRupees(10m)));

        var sale = new LedgerService(c).Post(new Voucher(
            Guid.NewGuid(), c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Sales).Id, SaleDate, lines,
            partyId: buyer.Id, inventoryLines: inventory));

        // The IRP path is genuinely reachable for this voucher — not a payload built for a document the app would
        // never e-invoice.
        Assert.Equal(EInvoiceCoverage.Covered, new EInvoiceService(c).CoverageOf(sale));
        return (c, sale);
    }

    /// <summary>One intra-State ACCOUNTING (service) invoice mixing a taxed SAC-bearing leg with an EXEMPT one.</summary>
    private static (Company Company, Voucher Sale) MixedServiceInvoice()
    {
        var c = SeededGstCompany("Mixed Exempt Service Co");

        var consultancy = Add(c, "Consultancy Income", "Sales Accounts", false);
        consultancy.SalesPurchaseGst = new StockItemGstDetails
        {
            HsnSac = TaxedSac, SupplyType = GstSupplyType.Services,
            Taxability = GstTaxability.Taxable, RateBasisPoints = 1800,
        };
        var education = Add(c, "Education Income", "Sales Accounts", false);
        education.SalesPurchaseGst = new StockItemGstDetails
        {
            HsnSac = ExemptSac, SupplyType = GstSupplyType.Services, Taxability = GstTaxability.Exempt,
        };
        var buyer = Buyer(c);

        var tax = new GstService(c).ComputeInvoiceTax(
            new[] { new GstService.TaxableLine(Money.FromRupees(ServiceTaxedValue), 1800) },
            interState: false, GstTaxDirection.Output);
        Assert.Equal(2 * ServiceHeadTax, tax.TaxLines.Sum(l => l.Amount.Amount));

        var lines = new List<EntryLine>
        {
            new(buyer.Id, new Money(ServiceTaxedValue + ServiceExemptValue + 2 * ServiceHeadTax), DrCr.Debit),
            new(consultancy.Id, Money.FromRupees(ServiceTaxedValue), DrCr.Credit),
            new(education.Id, Money.FromRupees(ServiceExemptValue), DrCr.Credit),
        };
        lines.AddRange(tax.TaxLines);

        var sale = new LedgerService(c).Post(new Voucher(
            Guid.NewGuid(), c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Sales).Id, SaleDate, lines,
            partyId: buyer.Id, isAccountingInvoice: true));

        Assert.Equal(EInvoiceCoverage.Covered, new EInvoiceService(c).CoverageOf(sale));
        return (c, sale);
    }

    private static Company SeededGstCompany(string name)
    {
        var c = CompanyFactory.CreateSeeded(name, FyStart);
        c.Address = "Unit 4, Fort Industrial Estate\nBallard Pier";
        c.Pin = "400001";
        new GstService(c).EnableGst(new GstConfig
        {
            HomeStateCode = "27", Gstin = Gstin, RegistrationType = GstRegistrationType.Regular,
            ApplicableFrom = FyStart, Periodicity = GstReturnPeriodicity.Monthly,
            EInvoicingEnabled = true, EInvoiceApplicableFrom = FyStart,
        });
        return c;
    }

    private static Domain.Ledger Buyer(Company c)
    {
        var buyer = Add(c, "Buyer", "Sundry Debtors", true);
        buyer.PartyGst = new PartyGstDetails
        { RegistrationType = GstRegistrationType.Regular, Gstin = Gstin, StateCode = "27" };
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
