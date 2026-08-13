using Apex.Ledger.Domain;
using Apex.Ledger.Reports;
using Apex.Ledger.Services;
using Xunit;

namespace Apex.Ledger.Tests;

/// <summary>
/// <b>W0-9 — ONE bill-of-supply rule.</b> CGST Act §31(3)(c) has <b>two</b> limbs: "a registered person supplying
/// <b>exempted</b> goods or services or both <b>or</b> paying tax under the provisions of <b>section 10</b> shall
/// issue, <i>instead of a tax invoice</i>, a bill of supply". Until this slice the <b>Ledger</b> layer knew only the
/// §10 limb, while the §31(3)(c) exempt limb lived in <c>Apex.Desktop.Services.VoucherPrintProjector</c> — a project
/// the engine cannot reference. The consequence was a REGULAR dealer's wholly-exempt goods movement printing
/// <b>BILL OF SUPPLY</b> on paper while the EWB-01 declared <c>docType "INV"</c>, a Tax Invoice: one consignment,
/// two mutually exclusive statutory claims, with the wrong one on the government filing.
///
/// <para>These tests bind the rule at the <b>engine</b> layer, which is where both consumers can reach it. They fail
/// on HEAD because <c>GstReportSupport.IsBillOfSupply</c> is the §10 limb alone.</para>
///
/// <para>Sources (R7): CGST Act §31(3)(c), §2(47), §2(98), §10(4) —
/// <c>https://cbic-gst.gov.in/pdf/CGST-Act-Updated-30092020.pdf</c>; NIC e-Way document-type master
/// (<c>INV</c> Tax Invoice / <c>BIL</c> Bill of Supply) —
/// <c>https://docs.ewaybillgst.gov.in/apidocs/master-codes-list.html</c>.</para>
///
/// <para>Every money fixture is odd to the paisa (₹2,17,483.91, ₹1,63,059.37, ₹94,271.63) — a round figure would pass
/// under a rounding defect and assert nothing.</para>
/// </summary>
public sealed class OneBillOfSupplyRuleTests
{
    private const string GstinMaharashtra = "27AAPFU0939F1ZV";
    private const string GstinGujarat = "24AAACC1206D1ZM";
    private static readonly DateOnly FyStart = new(2025, 4, 1);
    private static readonly DateOnly MoveDate = new(2025, 4, 10);

    private sealed class Fx
    {
        public required Company Company { get; init; }
        public required EWayBillService Service { get; init; }
        public required Guid GodownId { get; init; }
        public required Guid TaxableItemId { get; init; }
        public required Guid ExemptItemId { get; init; }
        public required Guid NilRatedItemId { get; init; }
        public required Guid NonGstItemId { get; init; }
        public required Guid UnclassifiedItemId { get; init; }
        public required Guid SalesLedgerId { get; init; }
        public required Guid RcmSalesLedgerId { get; init; }
        public required Guid PurchaseLedgerId { get; init; }
        public required Guid PartyId { get; init; }
        public required Guid SupplierId { get; init; }

        public Guid SalesTypeId => Company.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Sales).Id;
        public Guid PurchaseTypeId => Company.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Purchase).Id;
    }

    private static Domain.Ledger Add(Company c, string name, string groupName, bool openingIsDebit)
    {
        var l = new Domain.Ledger(Guid.NewGuid(), name, c.FindGroupByName(groupName)!.Id, Money.Zero, openingIsDebit);
        c.AddLedger(l);
        return l;
    }

    private static Fx Build(GstRegistrationType registration = GstRegistrationType.Regular)
    {
        var c = CompanyFactory.CreateSeeded("One BoS Rule Co", FyStart);
        new GstService(c).EnableGst(new GstConfig
        {
            HomeStateCode = "27",
            Gstin = GstinMaharashtra,
            RegistrationType = registration,
            CompositionSubType = registration == GstRegistrationType.Composition ? CompositionSubType.Trader : null,
            ApplicableFrom = FyStart,
            Periodicity = GstReturnPeriodicity.Monthly,
            EWayBillEnabled = true,
            EWayApplicableFrom = FyStart,
            EWayIntraStateApplicable = true,
        });

        var inv = new InventoryService(c);
        var grp = inv.CreateStockGroup("Goods");
        var nos = inv.CreateSimpleUnit("Nos", "Numbers", unitQuantityCode: "NOS");

        var taxable = inv.CreateStockItem("Widget", grp.Id, nos.Id);
        taxable.Gst = new StockItemGstDetails
        { HsnSac = "847130", Taxability = GstTaxability.Taxable, RateBasisPoints = 1800 };
        var exempt = inv.CreateStockItem("Fresh Milk", grp.Id, nos.Id);
        exempt.Gst = new StockItemGstDetails { HsnSac = "040110", Taxability = GstTaxability.Exempt };
        var nilRated = inv.CreateStockItem("Salt", grp.Id, nos.Id);
        nilRated.Gst = new StockItemGstDetails { HsnSac = "250100", Taxability = GstTaxability.NilRated };
        var nonGst = inv.CreateStockItem("Petrol", grp.Id, nos.Id);
        nonGst.Gst = new StockItemGstDetails { HsnSac = "271019", Taxability = GstTaxability.NonGst };
        // No GST block anywhere — the UNRESOLVED shape. Silence is not an exemption.
        var unclassified = inv.CreateStockItem("Mystery Crate", grp.Id, nos.Id);

        var sales = Add(c, "Sales", "Sales Accounts", false);
        var rcmSales = Add(c, "Sales (Reverse Charge)", "Sales Accounts", false);
        rcmSales.SalesPurchaseGst = new StockItemGstDetails
        { HsnSac = "996511", Taxability = GstTaxability.NilRated, ReverseChargeApplicable = true };
        var party = Add(c, "Local Debtor", "Sundry Debtors", true);
        party.PartyGst = new PartyGstDetails { RegistrationType = GstRegistrationType.Consumer, StateCode = "27" };

        // The INWARD side: a Gujarat (24) supplier against a Maharashtra (27) home State, so an inward movement is
        // inter-state and comfortably over the Rule-138 threshold.
        var purchases = Add(c, "Purchases", "Purchase Accounts", true);
        var supplier = Add(c, "Gujarat Supplier", "Sundry Creditors", false);
        supplier.PartyGst = new PartyGstDetails
        { RegistrationType = GstRegistrationType.Regular, Gstin = GstinGujarat, StateCode = "24" };

        return new Fx
        {
            Company = c,
            Service = new EWayBillService(c),
            GodownId = c.MainLocation!.Id,
            TaxableItemId = taxable.Id,
            ExemptItemId = exempt.Id,
            NilRatedItemId = nilRated.Id,
            NonGstItemId = nonGst.Id,
            UnclassifiedItemId = unclassified.Id,
            SalesLedgerId = sales.Id,
            RcmSalesLedgerId = rcmSales.Id,
            PurchaseLedgerId = purchases.Id,
            PartyId = party.Id,
            SupplierId = supplier.Id,
        };
    }

    /// <summary>A balanced INWARD goods movement carrying <paramref name="itemId"/> at <paramref name="value"/> — the
    /// supplier's document, seen from the buyer's books. Posts no input-tax leg, exactly as a no-tax supply must.
    /// </summary>
    private static Voucher Purchase(Fx f, Guid itemId, decimal value) =>
        new(Guid.NewGuid(), f.PurchaseTypeId, MoveDate, new List<EntryLine>
        {
            new(f.PurchaseLedgerId, new Money(value), DrCr.Debit),
            new(f.SupplierId, new Money(value), DrCr.Credit),
        }, partyId: f.SupplierId, inventoryLines: new[]
        {
            new VoucherInventoryLine(itemId, f.GodownId, 1m, new Money(value)),
        });

    /// <summary>A balanced outward goods movement carrying <paramref name="itemId"/> at <paramref name="value"/> —
    /// over the ₹50,000 Rule-138 threshold and odd to the paisa. Posts no tax leg, exactly as a no-tax supply must.
    /// </summary>
    private static Voucher Sale(Fx f, Guid itemId, decimal value, Guid? salesLedgerId = null)
    {
        var salesId = salesLedgerId ?? f.SalesLedgerId;
        return new Voucher(Guid.NewGuid(), f.SalesTypeId, MoveDate, new List<EntryLine>
        {
            new(f.PartyId, new Money(value), DrCr.Debit),
            new(salesId, new Money(value), DrCr.Credit),
        }, partyId: f.PartyId, inventoryLines: new[]
        {
            new VoucherInventoryLine(itemId, f.GodownId, 1m, new Money(value)),
        });
    }

    // ================================================================ the exempt limb, at the ENGINE layer

    /// <summary>
    /// <b>The defect, stated at the layer that can fix it.</b> A REGULAR dealer's wholly-exempt goods movement is a
    /// §31(3)(c) bill of supply — the section's FIRST limb, which does not mention §10 at all. The engine predicate
    /// answered <c>false</c> for it, so everything downstream of the engine (the e-Way Part-A <c>docType</c> above all)
    /// declared a Tax Invoice for a document the same application titles BILL OF SUPPLY.
    /// </summary>
    [Theory]
    [InlineData(GstTaxability.Exempt)]
    [InlineData(GstTaxability.NilRated)]
    [InlineData(GstTaxability.NonGst)]
    public void A_regular_dealers_wholly_non_taxable_supply_is_a_bill_of_supply(GstTaxability taxability)
    {
        var f = Build(GstRegistrationType.Regular);
        var itemId = taxability switch
        {
            GstTaxability.Exempt => f.ExemptItemId,
            GstTaxability.NilRated => f.NilRatedItemId,
            _ => f.NonGstItemId,
        };
        var v = Sale(f, itemId, 2_17_483.91m);

        // §2(47) folds nil-rated and non-taxable supplies into "exempt supply", so all three take the same limb.
        Assert.True(GstReportSupport.IsBillOfSupply(f.Company, v));
        // …and he is NOT a §10 dealer — the two limbs stay distinguishable, because Rule 5(1)(f)'s composition
        // declaration must NOT print on a regular dealer's exempt bill of supply.
        Assert.False(GstReportSupport.IsCompositionBillOfSupply(f.Company, v));
    }

    /// <summary>
    /// <b>The filing follows the document.</b> NIC's document-type master carries <c>BIL</c> = Bill of Supply, and the
    /// Supply-Type/Document-Type mapping permits <c>Outward | Supply | Bill of Supply</c>, so there was never any need
    /// to misdeclare this movement as <c>INV</c>. Rule 138 Explanation 2 reckons the consignment value from "an
    /// invoice, <b>a bill of supply</b> or a delivery challan", so the movement genuinely carries an e-Way Bill.
    /// </summary>
    [Fact]
    public void A_regular_dealers_wholly_exempt_movement_files_the_eWay_docType_BIL()
    {
        var f = Build(GstRegistrationType.Regular);
        var v = Sale(f, f.ExemptItemId, 2_17_483.91m);

        Assert.Equal(EWayCoverage.Required, f.Service.CoverageOf(v));
        var codes = f.Service.PartACodesFor(v);
        Assert.Equal("O", codes.SupplyType);
        Assert.Equal("1", codes.SubSupplyType);
        Assert.Equal("BIL", codes.DocType);
        Assert.NotEqual("INV", codes.DocType);   // the value this movement used to file

        var record = f.Service.PrepareRecord(v, MoveDate);
        Assert.Equal("BIL", record.DocType);
    }

    /// <summary>The §10 limb is untouched by the widening: a composition dealer's outward supply is a bill of supply
    /// whatever its items' taxability, and it is the limb that carries the Rule 5(1)(f) declaration.</summary>
    [Fact]
    public void A_composition_dealers_taxable_item_sale_is_still_a_bill_of_supply_on_the_section_10_limb()
    {
        var f = Build(GstRegistrationType.Composition);
        var v = Sale(f, f.TaxableItemId, 1_63_059.37m);

        Assert.True(GstReportSupport.IsBillOfSupply(f.Company, v));
        Assert.True(GstReportSupport.IsCompositionBillOfSupply(f.Company, v));
        Assert.Equal("BIL", f.Service.PartACodesFor(v).DocType);
    }

    // ================================================================ the negative controls

    /// <summary>A REGULAR dealer's TAXABLE supply is a Rule-46 tax invoice and files <c>INV</c> — the widening must
    /// not reclassify the ordinary case.</summary>
    [Fact]
    public void A_regular_dealers_taxable_supply_stays_a_tax_invoice_and_files_INV()
    {
        var f = Build(GstRegistrationType.Regular);
        var v = Sale(f, f.TaxableItemId, 94_271.63m);

        Assert.False(GstReportSupport.IsBillOfSupply(f.Company, v));
        Assert.False(GstReportSupport.IsCompositionBillOfSupply(f.Company, v));
        Assert.Equal("INV", f.Service.PartACodesFor(v).DocType);
    }

    /// <summary>A supply carrying even ONE taxable line is a tax invoice for the whole document: §31(3)(c) reserves the
    /// bill of supply for a supply of <b>exempted</b> goods, and Rule 46A's combined "invoice-cum-bill of supply" is
    /// permissive ("may be issued") and confined to an unregistered recipient, so it cannot make the bill of supply the
    /// required document.</summary>
    [Fact]
    public void A_supply_mixing_one_taxable_line_with_an_exempt_one_stays_a_tax_invoice()
    {
        var f = Build(GstRegistrationType.Regular);
        var exemptValue = 2_17_483.91m;
        var taxableValue = 41.09m;   // one small taxable line is enough to make the whole document a tax invoice
        var v = new Voucher(Guid.NewGuid(), f.SalesTypeId, MoveDate, new List<EntryLine>
        {
            new(f.PartyId, new Money(exemptValue + taxableValue), DrCr.Debit),
            new(f.SalesLedgerId, new Money(exemptValue + taxableValue), DrCr.Credit),
        }, partyId: f.PartyId, inventoryLines: new[]
        {
            new VoucherInventoryLine(f.ExemptItemId, f.GodownId, 1m, new Money(exemptValue)),
            new VoucherInventoryLine(f.TaxableItemId, f.GodownId, 1m, new Money(taxableValue)),
        });

        Assert.False(GstReportSupport.IsBillOfSupply(f.Company, v));
        Assert.Equal("INV", f.Service.PartACodesFor(v).DocType);
    }

    /// <summary>An UNRESOLVED line — no GST master on the item, none on the value ledger, none on the company — is not
    /// read as exempt. Silence is not an exemption, and reading it as one would strip the tax breakup off a genuinely
    /// taxable supply and file a Bill of Supply for it.</summary>
    [Fact]
    public void An_unresolved_line_is_not_read_as_exempt()
    {
        var f = Build(GstRegistrationType.Regular);
        var v = Sale(f, f.UnclassifiedItemId, 2_17_483.91m);

        Assert.False(GstReportSupport.IsBillOfSupply(f.Company, v));
        Assert.Equal("INV", f.Service.PartACodesFor(v).DocType);
    }

    /// <summary>An outward REVERSE-CHARGE supply is a TAXABLE supply — §2(98) moves the liability to the recipient
    /// "instead of the supplier", it does not extinguish it — so §31(3)(c) does not reach it even though the app's only
    /// reachable shape for one declares the sales ledger Nil-rated and posts no forward tax. Demoting it would
    /// contradict the app's own GSTR-1, which files the same voucher in Table 4B as a taxable outward supply.</summary>
    [Fact]
    public void An_outward_reverse_charge_supply_stays_a_tax_invoice()
    {
        var f = Build(GstRegistrationType.Regular);
        var v = Sale(f, f.ExemptItemId, 2_17_483.91m, salesLedgerId: f.RcmSalesLedgerId);

        Assert.True(GstReportSupport.IsOutwardReverseChargeSupply(f.Company, v));
        Assert.False(GstReportSupport.IsBillOfSupply(f.Company, v));
        Assert.Equal("INV", f.Service.PartACodesFor(v).DocType);
    }

    /// <summary>§31(3)(c) binds "A REGISTERED PERSON", so BOTH limbs require GST to be ON. A company that classified an
    /// item Exempt and then switched GST off in F11 (the disable branch clears <c>Enabled</c> but retains the config and
    /// every master) must not have a GST statutory document named for it.</summary>
    [Fact]
    public void A_GST_disabled_company_issues_no_bill_of_supply_on_either_limb()
    {
        var f = Build(GstRegistrationType.Regular);
        var v = Sale(f, f.ExemptItemId, 2_17_483.91m);
        Assert.True(GstReportSupport.IsBillOfSupply(f.Company, v));   // …while GST is on

        f.Company.Gst!.Enabled = false;
        Assert.False(GstReportSupport.IsBillOfSupply(f.Company, v));
        Assert.False(GstReportSupport.IsCompositionBillOfSupply(f.Company, v));
    }

    /// <summary>A document that POSTED forward tax states that tax, whatever its supplier's registration says: a bill
    /// of supply shows none, so titling it one would print a total short of the posted party leg. Read off the GENERAL
    /// LEDGER (a plain untagged credit to an Output tax ledger counts), not off line metadata.</summary>
    [Fact]
    public void An_exempt_supply_that_nonetheless_posted_forward_tax_is_not_a_bill_of_supply()
    {
        var f = Build(GstRegistrationType.Regular);
        var c = f.Company;
        var value = 2_17_483.91m;
        var cgst = 19_573.55m;   // odd paisa, and deliberately NOT half of anything round
        var sgst = 19_573.56m;
        var outCgst = c.Ledgers.Single(l => l.GstClassification is
        { Direction: GstTaxDirection.Output, TaxHead: GstTaxHead.Central, IsReverseCharge: false });
        var outSgst = c.Ledgers.Single(l => l.GstClassification is
        { Direction: GstTaxDirection.Output, TaxHead: GstTaxHead.State, IsReverseCharge: false });

        var v = new Voucher(Guid.NewGuid(), f.SalesTypeId, MoveDate, new List<EntryLine>
        {
            new(f.PartyId, new Money(value + cgst + sgst), DrCr.Debit),
            new(f.SalesLedgerId, new Money(value), DrCr.Credit),
            new(outCgst.Id, new Money(cgst), DrCr.Credit),   // hand-keyed: no GstLineTax metadata at all
            new(outSgst.Id, new Money(sgst), DrCr.Credit),
        }, partyId: f.PartyId, inventoryLines: new[]
        {
            new VoucherInventoryLine(f.ExemptItemId, f.GodownId, 1m, new Money(value)),
        });

        Assert.True(GstReportSupport.CarriesForwardTax(c, v));
        Assert.False(GstReportSupport.IsBillOfSupply(c, v));
        Assert.Equal("INV", f.Service.PartACodesFor(v).DocType);
    }

    // ================================================================ W0-9 review — the FILING is not the PRINT

    /// <summary>
    /// <b>🔴 A RECORDED USER RULING (R12, taken 2026-08-14) — a §10 dealer's movement files <c>BIL</c>, even when the
    /// books carry forward tax he was never entitled to collect. Dealer status decides.</b>
    ///
    /// <para><b>Why this test exists at all.</b> W0-9 unified the outward document-kind rule into
    /// <see cref="GstReportSupport.IsBillOfSupply"/>, whose FIRST gate is
    /// <see cref="GstReportSupport.CarriesForwardTax"/>. That gate is there for a <b>PRINT-MONEY</b> reason, stated in
    /// its own comment: a bill of supply shows no tax, so titling a tax-carrying document one "would print a Grand
    /// Total short of the posted party leg". Routing the e-Way Part-A through the same predicate silently handed that
    /// money gate authority over a <b>filing field that carries no money at all</b> — a three-letter document-kind
    /// declaration — and flipped this shape from <c>BIL</c> (its value before W0-9, when the engine predicate was the
    /// §10 limb alone) to <c>INV</c>. The flip was an accident of layering, not a decision.</para>
    ///
    /// <para><b>The ruling, and its statutory ground.</b> CGST Act §31(3)(c) is <b>unconditional</b> for a §10 person:
    /// he "shall issue, <i>instead of a tax invoice</i>, a bill of supply". Nothing in the section is contingent on
    /// what he actually collected — and §10(4) separately bars him from collecting "any tax from the recipient", so the
    /// posted tax is an <b>unlawful</b> fact about the books, never a re-characterisation of the document. Declaring
    /// <c>INV</c> would file the exact document §31(3)(c) denies him. So the §10 half of the <c>docType</c> decision is
    /// routed off composition status ALONE (<see cref="GstReportSupport.IsCompositionBillOfSupply"/>), and the anomalous
    /// posted tax is ignored <b>for the filing only</b>. The PRINT still refuses both documents — that is
    /// <c>VoucherPrintProjector.ProjectInvoice</c>'s structural refusal, and it is unchanged here.</para>
    ///
    /// <para><b>Edge/legacy data only.</b> <c>VoucherValidator.EnsureValid</c> refuses to POST this shape on every
    /// entry path (the §10(4) guard from the W0-1 follow-up), so it is reachable only on a book that already contains
    /// it — the Regular dealer who posts taxed sales and later opts into composition in F11, then re-files an old
    /// movement.</para>
    ///
    /// <para>The tax legs are <b>hand-keyed and untagged</b> (no <see cref="GstLineTax"/>) at ODD paisa
    /// ₹19,573.55 / ₹19,573.56 — the As-Voucher shape, so the ruling is pinned against the general-ledger read
    /// (<see cref="GstReportSupport.CarriesForwardTax"/>) and not merely against line metadata. A round figure, or a
    /// pair that halved evenly, would let a rounding or a symmetry shortcut pass.</para>
    ///
    /// <para>Sources (R7): CGST Act §31(3)(c), §10(4), §32(2) —
    /// <c>https://cbic-gst.gov.in/pdf/CGST-Act-Updated-30092020.pdf</c>; NIC document-type master (<c>BIL</c> = Bill of
    /// Supply) and the <c>Outward | 1 Supply | BIL</c> row —
    /// <c>https://docs.ewaybillgst.gov.in/apidocs/master-codes-list.html</c> /
    /// <c>https://docs.ewaybillgst.gov.in/apidocs/sub-docType-mapping.html</c>.</para>
    /// </summary>
    [Fact]
    public void RULING_a_composition_movement_carrying_posted_forward_tax_still_files_BIL()
    {
        // Built as a REGULAR dealer so EnableGst seeds the six tax ledgers, then switched to Composition in F11 —
        // exactly the reachable route (EnableGst only SKIPS seeding for composition; it never deletes).
        var f = Build(GstRegistrationType.Regular);
        var c = f.Company;
        c.Gst!.RegistrationType = GstRegistrationType.Composition;
        c.Gst!.CompositionSubType = CompositionSubType.Trader;

        var value = 2_17_483.91m;
        var cgst = 19_573.55m;
        var sgst = 19_573.56m;
        var outCgst = c.Ledgers.Single(l => l.GstClassification is
        { Direction: GstTaxDirection.Output, TaxHead: GstTaxHead.Central, IsReverseCharge: false });
        var outSgst = c.Ledgers.Single(l => l.GstClassification is
        { Direction: GstTaxDirection.Output, TaxHead: GstTaxHead.State, IsReverseCharge: false });

        var v = new Voucher(Guid.NewGuid(), f.SalesTypeId, MoveDate, new List<EntryLine>
        {
            new(f.PartyId, new Money(value + cgst + sgst), DrCr.Debit),
            new(f.SalesLedgerId, new Money(value), DrCr.Credit),
            new(outCgst.Id, new Money(cgst), DrCr.Credit),   // hand-keyed: no GstLineTax metadata at all
            new(outSgst.Id, new Money(sgst), DrCr.Credit),
        }, partyId: f.PartyId, inventoryLines: new[]
        {
            new VoucherInventoryLine(f.TaxableItemId, f.GodownId, 1m, new Money(value)),
        });
        Assert.All(v.Lines, l => Assert.Null(l.Gst));                      // the As-Voucher path stamps no metadata
        Assert.Equal(new Money(2_56_631.02m),
            v.Lines.Single(l => l.LedgerId == f.PartyId && l.Side == DrCr.Debit).Amount);

        // He IS a §10 dealer, and the books DO carry forward tax — the contradiction, in both directions.
        Assert.True(GstReportSupport.IsCompositionBillOfSupply(c, v));
        Assert.True(GstReportSupport.CarriesForwardTax(c, v));

        // The PRINT rule still bails on the money gate, and must: no lawful paper states this voucher.
        Assert.False(GstReportSupport.IsBillOfSupply(c, v));
        Assert.False(GstReportSupport.IsTaxInvoice(c, v));

        // 🔴 THE RULING: the FILING says BIL. Dealer status decides; the money gate has no money to protect here.
        Assert.Equal("BIL", f.Service.PartACodesFor(v).DocType);
        Assert.NotEqual("INV", f.Service.PartACodesFor(v).DocType);  // the value W0-9 accidentally flipped it to
        Assert.Equal("BIL", f.Service.PrepareRecord(v, MoveDate).DocType);
    }

    /// <summary>
    /// <b>The ruling is confined to the §10 limb.</b> A REGULAR dealer's wholly-exempt supply that nonetheless posted
    /// forward tax keeps filing <c>INV</c>, and the asymmetry is deliberate: nothing bars a regular dealer from
    /// collecting tax, so his posted tax is positive evidence that the supply was NOT exempt and the document was a
    /// tax invoice. §10(4) bars a composition dealer absolutely, so his posted tax can only be unlawful — it says
    /// nothing about which document §31(3)(c) obliged him to issue.
    /// </summary>
    [Fact]
    public void The_ruling_does_not_reach_a_regular_dealers_exempt_supply_that_posted_tax()
    {
        var f = Build(GstRegistrationType.Regular);
        var c = f.Company;
        var value = 2_17_483.91m;
        var outCgst = c.Ledgers.Single(l => l.GstClassification is
        { Direction: GstTaxDirection.Output, TaxHead: GstTaxHead.Central, IsReverseCharge: false });
        var outSgst = c.Ledgers.Single(l => l.GstClassification is
        { Direction: GstTaxDirection.Output, TaxHead: GstTaxHead.State, IsReverseCharge: false });

        var v = new Voucher(Guid.NewGuid(), f.SalesTypeId, MoveDate, new List<EntryLine>
        {
            new(f.PartyId, new Money(value + 19_573.55m + 19_573.56m), DrCr.Debit),
            new(f.SalesLedgerId, new Money(value), DrCr.Credit),
            new(outCgst.Id, new Money(19_573.55m), DrCr.Credit),
            new(outSgst.Id, new Money(19_573.56m), DrCr.Credit),
        }, partyId: f.PartyId, inventoryLines: new[]
        {
            new VoucherInventoryLine(f.ExemptItemId, f.GodownId, 1m, new Money(value)),
        });

        Assert.False(GstReportSupport.IsCompositionBillOfSupply(c, v));
        Assert.True(GstReportSupport.CarriesForwardTax(c, v));
        Assert.Equal("INV", f.Service.PartACodesFor(v).DocType);
    }

    // ================================================================ W0-9 review — the INWARD limb

    /// <summary>
    /// <b>🔴 THE SIXTH COPY, closed.</b> <c>EWayBillService.PartACodesFor</c>'s Purchase arm hardcoded
    /// <c>("I","1","INV")</c> — a document kind decided from the BASE TYPE alone, three lines below the outward arm
    /// W0-9 had just routed, while the class comment asserted the document kind was decided in exactly one place.
    ///
    /// <para><b>The defect is live, not theoretical.</b> Sales and Purchase are the only two limbs of
    /// <c>PartACodesFor</c> that can execute in the shipped app. A Regular dealer buying wholly-exempt goods (here
    /// Fresh Milk, HSN 040110, Taxability Exempt) inter-state above the ₹50,000 threshold filed a <b>Tax Invoice</b>
    /// for a consignment that can only have travelled on a <b>bill of supply</b>: <b>exemption is a property of the
    /// GOODS, not of the counterparty</b>, so the supplier — regular or not — was bound by §31(3)(c)'s first limb
    /// exactly as our own outward rule binds us. NIC's own mapping carries the row
    /// <c>Inward | 1 Supply | BIL</c> (From = Other GSTIN/URP, To = Self), so no code-domain constraint ever forced
    /// the wrong value.</para>
    ///
    /// <para>§2(47) folds nil-rated and non-taxable supplies into "exempt supply", so all three taxabilities take the
    /// limb, exactly as on the outward side.</para>
    /// </summary>
    [Theory]
    [InlineData(GstTaxability.Exempt)]
    [InlineData(GstTaxability.NilRated)]
    [InlineData(GstTaxability.NonGst)]
    public void A_regular_dealers_wholly_exempt_PURCHASE_files_the_inward_docType_BIL(GstTaxability taxability)
    {
        var f = Build(GstRegistrationType.Regular);
        var itemId = taxability switch
        {
            GstTaxability.Exempt => f.ExemptItemId,
            GstTaxability.NilRated => f.NilRatedItemId,
            _ => f.NonGstItemId,
        };
        var v = Purchase(f, itemId, 2_17_483.91m);

        Assert.True(GstReportSupport.IsInwardBillOfSupply(f.Company, v));
        Assert.Equal(EWayCoverage.Required, f.Service.CoverageOf(v));

        var codes = f.Service.PartACodesFor(v);
        Assert.Equal("I", codes.SupplyType);
        Assert.Equal("1", codes.SubSupplyType);
        Assert.Equal("BIL", codes.DocType);
        Assert.NotEqual("INV", codes.DocType);          // the value this movement used to file
        Assert.Equal("BIL", f.Service.PrepareRecord(v, MoveDate).DocType);
    }

    /// <summary>The negative control: an ordinary TAXABLE inward supply is a Rule-46 tax invoice and keeps filing
    /// <c>("I","1","INV")</c>. The inward widening must not reclassify the ordinary case.</summary>
    [Fact]
    public void A_taxable_purchase_still_files_I_1_INV()
    {
        var f = Build(GstRegistrationType.Regular);
        var v = Purchase(f, f.TaxableItemId, 2_17_483.91m);

        Assert.False(GstReportSupport.IsInwardBillOfSupply(f.Company, v));
        var codes = f.Service.PartACodesFor(v);
        Assert.Equal(("I", "1", "INV"), (codes.SupplyType, codes.SubSupplyType, codes.DocType));
    }

    /// <summary>An UNRESOLVED inward line — no GST master on the item, none on the purchase ledger, none on the
    /// company — is not read as exempt on the inward side either. Silence is not an exemption.</summary>
    [Fact]
    public void An_unresolved_purchase_line_is_not_read_as_exempt()
    {
        var f = Build(GstRegistrationType.Regular);
        var v = Purchase(f, f.UnclassifiedItemId, 2_17_483.91m);

        Assert.False(GstReportSupport.IsInwardBillOfSupply(f.Company, v));
        Assert.Equal("INV", f.Service.PartACodesFor(v).DocType);
    }

    /// <summary>
    /// <b>The inward money gate.</b> A purchase of nominally exempt goods that NONETHELESS records input GST is
    /// evidence that the supplier charged tax — so his document was a tax invoice, whatever our own item master
    /// declares — and it keeps filing <c>INV</c>. This is the inward twin of the outward posted-tax gate, and it reads
    /// the GENERAL LEDGER (a plain untagged debit to an Input tax ledger counts), not line metadata.
    /// <para>Odd paisa on both heads (₹19,573.55 / ₹19,573.56), hand-keyed and untagged.</para>
    /// </summary>
    [Fact]
    public void An_exempt_purchase_that_nonetheless_recorded_input_tax_stays_a_tax_invoice()
    {
        var f = Build(GstRegistrationType.Regular);
        var c = f.Company;
        var value = 2_17_483.91m;
        var inCgst = c.Ledgers.Single(l => l.GstClassification is
        { Direction: GstTaxDirection.Input, TaxHead: GstTaxHead.Central, IsReverseCharge: false });
        var inSgst = c.Ledgers.Single(l => l.GstClassification is
        { Direction: GstTaxDirection.Input, TaxHead: GstTaxHead.State, IsReverseCharge: false });

        var v = new Voucher(Guid.NewGuid(), f.PurchaseTypeId, MoveDate, new List<EntryLine>
        {
            new(f.PurchaseLedgerId, new Money(value), DrCr.Debit),
            new(inCgst.Id, new Money(19_573.55m), DrCr.Debit),   // hand-keyed: no GstLineTax metadata at all
            new(inSgst.Id, new Money(19_573.56m), DrCr.Debit),
            new(f.SupplierId, new Money(value + 19_573.55m + 19_573.56m), DrCr.Credit),
        }, partyId: f.SupplierId, inventoryLines: new[]
        {
            new VoucherInventoryLine(f.ExemptItemId, f.GodownId, 1m, new Money(value)),
        });

        Assert.False(GstReportSupport.IsInwardBillOfSupply(c, v));
        Assert.Equal("INV", f.Service.PartACodesFor(v).DocType);
    }

    /// <summary>
    /// <b>The §10 limb, seen from the buyer's side.</b> §31(3)(c) binds the SUPPLIER, so a purchase from a party the
    /// masters record as a <b>composition</b> dealer travelled on a bill of supply whatever the goods' taxability —
    /// he "shall issue, instead of a tax invoice, a bill of supply", and §10(4) means he charged us nothing. The
    /// goods here are the ordinary TAXABLE Widget, so only the counterparty's status can decide it.
    /// </summary>
    [Fact]
    public void A_purchase_from_a_composition_supplier_files_the_inward_docType_BIL()
    {
        var f = Build(GstRegistrationType.Regular);
        f.Company.FindLedger(f.SupplierId)!.PartyGst = new PartyGstDetails
        { RegistrationType = GstRegistrationType.Composition, Gstin = GstinGujarat, StateCode = "24" };
        var v = Purchase(f, f.TaxableItemId, 2_17_483.91m);

        Assert.True(GstReportSupport.IsInwardBillOfSupply(f.Company, v));
        Assert.Equal("BIL", f.Service.PartACodesFor(v).DocType);
    }

    /// <summary>A GST-disabled company files no bill of supply on either DIRECTION: §31(3)(c) is a statutory document
    /// of the GST law, and the inward gate mirrors the outward one so the two can never drift apart on it.</summary>
    [Fact]
    public void A_GST_disabled_company_files_no_inward_bill_of_supply()
    {
        var f = Build(GstRegistrationType.Regular);
        var v = Purchase(f, f.ExemptItemId, 2_17_483.91m);
        Assert.True(GstReportSupport.IsInwardBillOfSupply(f.Company, v));   // …while GST is on

        f.Company.Gst!.Enabled = false;
        Assert.False(GstReportSupport.IsInwardBillOfSupply(f.Company, v));
    }
}
