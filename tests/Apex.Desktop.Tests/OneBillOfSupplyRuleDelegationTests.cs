using System;
using System.Collections.Generic;
using System.Linq;
using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Reports;
using Apex.Ledger.Services;
using Apex.Desktop.Services;
using Apex.Desktop.ViewModels;
using Xunit;
using DomainLedger = Apex.Ledger.Domain.Ledger;

namespace Apex.Desktop.Tests;

/// <summary>
/// <b>W0-9 — the printed title and the filed e-Way <c>docType</c> are ONE decision.</b> This is the invariant the whole
/// slice exists to establish, and it had <b>no</b> test for the §31(3)(c) exempt limb before now.
///
/// <para><b>The defect it locks out.</b> There were TWO predicates named <c>IsBillOfSupply</c> — a §10-composition-only
/// one in <c>Apex.Ledger.Reports.GstReportSupport</c> (which the e-Way Bill Part-A read) and a wider one in
/// <c>Apex.Desktop.Services.VoucherPrintProjector</c> that added the §31(3)(c) wholly-exempt limb (which the printed
/// title read). A REGULAR dealer's wholly-exempt goods movement therefore printed <b>BILL OF SUPPLY</b> on paper while
/// the EWB-01 declared <c>docType "INV"</c>, a Tax Invoice. The root cause was LAYERING — <c>Apex.Ledger</c> cannot
/// reference <c>Apex.Desktop</c>, so the exempt limb could not be put where the engine would see it — so the fix moved
/// the rule DOWN rather than copying it sideways.</para>
///
/// <para><b>Why the assertion is written as an equivalence, not as five separate expectations.</b> Four consumers read
/// the document kind: the printed <c>DocumentTitle</c>, the NIC <c>docType</c>, the on-screen drill badge, and the
/// Desktop wrapper predicate. Asserting each against a hard-coded value would let two of them drift apart while both
/// still matched their own expectation — which is exactly how this defect survived a green suite. So every shape is
/// driven through all of them and they are required to <b>agree with each other</b> as well as with the statute.</para>
///
/// <para><b>One documented exception to "ONE decision", added by the W0-9 review.</b> The e-Way path reads
/// <c>GstReportSupport.IsBillOfSupplyForFiling</c>, which diverges from the print rule on exactly one shape — a §10
/// dealer's movement carrying posted forward tax (R12 user ruling, 2026-08-14). That shape prints <b>no statutory
/// title at all</b>, so nothing on paper can contradict the filing; it is pinned separately by
/// <see cref="The_section_10_contradiction_files_BIL_while_printing_no_statutory_title_at_all"/>, and it is
/// unreachable through <see cref="Ask"/> because <c>ProjectInvoice</c> refuses it.</para>
///
/// <para>Sources (R7): CGST Act §31(3)(c), §2(47), §2(98), §10(4) —
/// <c>https://cbic-gst.gov.in/pdf/CGST-Act-Updated-30092020.pdf</c>; NIC e-Way document-type master (<c>INV</c> Tax
/// Invoice / <c>BIL</c> Bill of Supply) — <c>https://docs.ewaybillgst.gov.in/apidocs/master-codes-list.html</c>.</para>
///
/// <para>Every fixture is odd to the paisa (₹2,17,483.91, ₹1,63,059.37, ₹94,271.63, ₹41.09) — a round figure would pass
/// under a rounding defect and assert nothing.</para>
/// </summary>
public sealed class OneBillOfSupplyRuleDelegationTests
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
        public required Guid UnclassifiedItemId { get; init; }
        public required Guid SalesLedgerId { get; init; }
        public required Guid PurchaseLedgerId { get; init; }
        /// <summary>An income ledger declaring an EXEMPT SAC supply — the wholly-exempt service invoice.</summary>
        public required Guid ExemptServiceLedgerId { get; init; }
        /// <summary>An income ledger declaring a TAXABLE SAC supply at a 0% (LUT/export) rate — a genuine Rule-46 tax
        /// invoice that posts no tax leg, so it needs no seeded tax ledger and exists under BOTH registrations.</summary>
        public required Guid ZeroRatedServiceLedgerId { get; init; }
        /// <summary>The Gujarat (24) buyer — the INTER-state routing.</summary>
        public required Guid PartyId { get; init; }
        /// <summary>A Maharashtra (27) buyer — the INTRA-state routing, and the only routing CGST §10(2)(c) permits a
        /// composition dealer ("he is not engaged in making any inter-State outward supplies of goods").</summary>
        public required Guid LocalPartyId { get; init; }
        public required Guid SupplierId { get; init; }
        public Guid SalesTypeId => Company.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Sales).Id;
        public Guid PurchaseTypeId => Company.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Purchase).Id;
    }

    private static DomainLedger Add(Company c, string name, string groupName, bool openingIsDebit)
    {
        var l = new DomainLedger(Guid.NewGuid(), name, c.FindGroupByName(groupName)!.Id, Money.Zero, openingIsDebit);
        c.AddLedger(l);
        return l;
    }

    private static Fx Build(GstRegistrationType registration)
    {
        var c = CompanyFactory.CreateSeeded("One Rule " + registration + " Co", FyStart);
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
        var nil = inv.CreateStockItem("Salt", grp.Id, nos.Id);
        nil.Gst = new StockItemGstDetails { HsnSac = "250100", Taxability = GstTaxability.NilRated };
        var unclassified = inv.CreateStockItem("Mystery Crate", grp.Id, nos.Id);   // no GST master anywhere

        var sales = Add(c, "Sales", "Sales Accounts", false);
        var purchases = Add(c, "Purchases", "Purchase Accounts", true);

        // The two SERVICE-income legs. Both are SAC-bearing (so `Gstr1.ServiceLegs` sees them) and neither carries a
        // GstClassification (so neither is read as a tax ledger). The zero-rated one declares a TAXABLE supply at 0%,
        // which is why it is a tax invoice and NOT a bill of supply while still posting no tax leg — so it needs none
        // of the six Output tax ledgers and therefore exists under the Composition build too.
        var exemptService = Add(c, "Exempt Service Income", "Sales Accounts", false);
        exemptService.SalesPurchaseGst = new StockItemGstDetails
        { HsnSac = "999999", Taxability = GstTaxability.Exempt, SupplyType = GstSupplyType.Services };
        var zeroRatedService = Add(c, "Export Consultancy (LUT)", "Sales Accounts", false);
        zeroRatedService.SalesPurchaseGst = new StockItemGstDetails
        {
            HsnSac = "998311", Taxability = GstTaxability.Taxable, RateBasisPoints = 0,
            SupplyType = GstSupplyType.Services,
        };

        // A Gujarat (24) buyer against a Maharashtra (27) home State — the inter-state routing, so an IGST-taxed
        // control posts a single head and the e-Way threshold is comfortably met.
        var party = Add(c, "Gujarat Buyer", "Sundry Debtors", true);
        party.PartyGst = new PartyGstDetails
        { RegistrationType = GstRegistrationType.Regular, Gstin = GstinGujarat, StateCode = "24" };
        // …and a Maharashtra (27) buyer, the INTRA-state routing. CGST §10(2)(c) permits a composition dealer only
        // this one, so the §10 limb cannot be exercised honestly without it.
        var localParty = Add(c, "Mumbai Buyer", "Sundry Debtors", true);
        localParty.PartyGst = new PartyGstDetails
        { RegistrationType = GstRegistrationType.Consumer, StateCode = "27" };
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
            NilRatedItemId = nil.Id,
            UnclassifiedItemId = unclassified.Id,
            SalesLedgerId = sales.Id,
            PurchaseLedgerId = purchases.Id,
            ExemptServiceLedgerId = exemptService.Id,
            ZeroRatedServiceLedgerId = zeroRatedService.Id,
            PartyId = party.Id,
            LocalPartyId = localParty.Id,
            SupplierId = supplier.Id,
        };
    }

    private static Voucher Sale(Fx f, params (Guid ItemId, decimal Value)[] items) => SaleTo(f, f.PartyId, items);

    /// <summary>An item-invoice sale billed to <paramref name="partyId"/> — the routing (24 ⇒ 27 inter-state, or
    /// 27 ⇒ 27 intra-state) is the ONLY thing that varies between it and <see cref="Sale"/>.</summary>
    private static Voucher SaleTo(Fx f, Guid partyId, params (Guid ItemId, decimal Value)[] items)
    {
        var total = items.Sum(i => i.Value);
        return new Voucher(Guid.NewGuid(), f.SalesTypeId, MoveDate, new List<EntryLine>
        {
            new(partyId, new Money(total), DrCr.Debit),
            new(f.SalesLedgerId, new Money(total), DrCr.Credit),
        }, partyId: partyId,
        inventoryLines: items.Select(i =>
            new VoucherInventoryLine(i.ItemId, f.GodownId, 1m, new Money(i.Value))).ToArray());
    }

    /// <summary>A LEDGER-ONLY Sales voucher — no stock lines at all. <paramref name="accountingInvoice"/> is the v49
    /// persisted flag <c>IsServiceAccountingInvoice</c> gates on, so the same two legs are a service tax invoice with
    /// it and a plain As-Voucher sale without it.</summary>
    private static Voucher LedgerOnlySale(Fx f, Guid incomeLedgerId, decimal value, bool accountingInvoice) =>
        new(Guid.NewGuid(), f.SalesTypeId, MoveDate, new List<EntryLine>
        {
            new(f.PartyId, new Money(value), DrCr.Debit),
            new(incomeLedgerId, new Money(value), DrCr.Credit),
        }, partyId: f.PartyId, isAccountingInvoice: accountingInvoice);

    /// <summary>An item PURCHASE — a goods movement that is never an outward invoice document on any reading.</summary>
    private static Voucher PurchaseItem(Fx f, Guid itemId, decimal value) =>
        new(Guid.NewGuid(), f.PurchaseTypeId, MoveDate, new List<EntryLine>
        {
            new(f.PurchaseLedgerId, new Money(value), DrCr.Debit),
            new(f.SupplierId, new Money(value), DrCr.Credit),
        }, partyId: f.SupplierId, inventoryLines: new[]
        {
            new VoucherInventoryLine(itemId, f.GodownId, 1m, new Money(value)),
        });

    /// <summary>An item sale whose Output CGST/SGST legs are hand-keyed with NO <see cref="GstLineTax"/> metadata — the
    /// As-Voucher shape. <c>PostedOutputTaxIsFullyTagged</c> answers false for it, so it is not an invoice document at
    /// all: the one item shape for which <c>IsTaxInvoice</c> is FALSE. Returns null when the company has no seeded
    /// Output tax ledgers (every Composition company the app itself creates).</summary>
    private static Voucher? UntaggedOutputTaxSale(Fx f, decimal value, decimal cgst, decimal sgst)
    {
        var outCgst = f.Company.Ledgers.SingleOrDefault(l => l.GstClassification is
        { Direction: GstTaxDirection.Output, TaxHead: GstTaxHead.Central, IsReverseCharge: false });
        var outSgst = f.Company.Ledgers.SingleOrDefault(l => l.GstClassification is
        { Direction: GstTaxDirection.Output, TaxHead: GstTaxHead.State, IsReverseCharge: false });
        if (outCgst is null || outSgst is null) return null;

        return new Voucher(Guid.NewGuid(), f.SalesTypeId, MoveDate, new List<EntryLine>
        {
            new(f.PartyId, new Money(value + cgst + sgst), DrCr.Debit),
            new(f.SalesLedgerId, new Money(value), DrCr.Credit),
            new(outCgst.Id, new Money(cgst), DrCr.Credit),   // hand-keyed: no GstLineTax metadata at all
            new(outSgst.Id, new Money(sgst), DrCr.Credit),
        }, partyId: f.PartyId, inventoryLines: new[]
        {
            new VoucherInventoryLine(f.TaxableItemId, f.GodownId, 1m, new Money(value)),
        });
    }

    /// <summary>What every consumer of the document-kind rule answered for one voucher, gathered so they can be
    /// required to agree with each other rather than each with its own hard-coded expectation.</summary>
    private readonly record struct Verdicts(
        bool Engine, bool PrinterWrapper, string PrintedTitle, string? FiledDocType, string ScreenBadge);

    private static Verdicts Ask(Fx f, Voucher v) => new(
        GstReportSupport.IsBillOfSupply(f.Company, v),
        VoucherPrintProjector.IsBillOfSupply(f.Company, v),
        VoucherPrintProjector.ProjectInvoice(f.Company, v).DocumentTitle,
        f.Service.PrepareRecord(v, MoveDate).DocType,
        new VoucherDetailViewModel(f.Company, v).DocumentLabel);

    /// <summary>The equivalence itself: whatever the document kind is, all five readings must say the SAME thing.
    /// <paramref name="expectBillOfSupply"/> anchors the class to the statute so the test cannot pass by having every
    /// consumer agree on the WRONG answer.</summary>
    private static void AssertOneDecision(Verdicts v, bool expectBillOfSupply)
    {
        Assert.Equal(expectBillOfSupply, v.Engine);
        Assert.Equal(v.Engine, v.PrinterWrapper);                                    // the wrapper is a pure forward
        Assert.Equal(expectBillOfSupply ? GstReportSupport.BillOfSupplyTitle
                                        : GstReportSupport.TaxInvoiceTitle, v.PrintedTitle);
        Assert.Equal(expectBillOfSupply ? "BIL" : "INV", v.FiledDocType);
        Assert.Equal(expectBillOfSupply ? "Bill of Supply" : "Tax Invoice", v.ScreenBadge);
        // Stated as an equivalence too, so a future change that flips BOTH sides of one pair still fails here.
        Assert.Equal(v.PrintedTitle == GstReportSupport.BillOfSupplyTitle, v.FiledDocType == "BIL");
    }

    // ================================================================ both limbs of §31(3)(c)

    /// <summary><b>Limb 1 — §10 composition.</b> Already agreed before this slice (W0-8 routed it); asserted here so the
    /// matrix covers the whole section and a regression on the composition half cannot hide.
    ///
    /// <para><b>🔴 W0-9 REVIEW FINDING #4 — IT USED TO DRIVE ONE ROUTING, AND THE WRONG ONE.</b> The test claimed
    /// "one decision across print, filing and screen" for the §10 limb while driving a single voucher billed to the
    /// fixture's Gujarat (24) buyer against a Maharashtra (27) home State — an <b>INTER-state</b> outward supply by a
    /// composition dealer, which CGST <b>§10(2)(c)</b> forbids outright: a §10 person is eligible only if "he is not
    /// engaged in making any inter-State outward supplies of goods". So the one shape it exercised was the one shape
    /// a lawful §10 dealer can never make, and the INTRA-state supply — the whole of his permitted trade — was never
    /// driven at all. It is now a <see cref="Theory"/> over both routings.</para>
    ///
    /// <para><b>Both are kept, deliberately.</b> The intra-state case is the lawful one and carries the claim. The
    /// inter-state case stays because it is REACHABLE on real data — F11 lets a Regular dealer opt into composition
    /// after posting inter-state sales, and §10(2)(c) is an ELIGIBILITY condition on the person, not a rule about what
    /// document an already-posted movement bears — and because the two must not answer differently: §31(3)(c) is
    /// unconditional for a §10 person, so the routing has no say in the document kind. A drift between them would be
    /// a place-of-supply test leaking into a document-kind decision.</para>
    ///
    /// <para>Source (R7): CGST Act §10(2)(c), §31(3)(c) —
    /// <c>https://cbic-gst.gov.in/pdf/CGST-Act-Updated-30092020.pdf</c>.</para>
    /// </summary>
    [Theory]
    [InlineData(true)]    // 27 ⇒ 27 intra-state — the ONLY routing §10(2)(c) permits him
    [InlineData(false)]   // 27 ⇒ 24 inter-state — ineligible under §10(2)(c), but reachable after an F11 switch
    public void The_section_10_limb_is_one_decision_across_print_filing_and_screen(bool intraState)
    {
        var f = Build(GstRegistrationType.Composition);
        var v = SaleTo(f, intraState ? f.LocalPartyId : f.PartyId, (f.TaxableItemId, 1_63_059.37m));

        // The routing really did change — otherwise the two cases would be the same test twice.
        Assert.Equal(intraState ? "27" : "24", GstReportSupport.PlaceOfSupply(f.Company, v));

        AssertOneDecision(Ask(f, v), expectBillOfSupply: true);
        // The Rule 5(1)(f) declaration belongs to THIS limb, and only to it.
        Assert.True(GstReportSupport.IsCompositionBillOfSupply(f.Company, v));
        Assert.Equal(GstReportSupport.BillOfSupplyDeclaration,
            VoucherPrintProjector.ProjectInvoice(f.Company, v).TopDeclaration);
    }

    /// <summary><b>Limb 2 — §31(3)(c) wholly exempt, by a REGULAR dealer. THE DEFECT THIS SLICE FIXES.</b> Before the
    /// move this shape printed BILL OF SUPPLY and filed <c>INV</c>: one consignment, two mutually exclusive statutory
    /// claims, with the wrong one on the government filing. §2(47) folds nil-rated and non-taxable supplies into
    /// "exempt supply", so both taxabilities take the same limb.</summary>
    [Theory]
    [InlineData(GstTaxability.Exempt)]
    [InlineData(GstTaxability.NilRated)]
    public void The_exempt_limb_is_one_decision_across_print_filing_and_screen(GstTaxability taxability)
    {
        var f = Build(GstRegistrationType.Regular);
        var itemId = taxability == GstTaxability.Exempt ? f.ExemptItemId : f.NilRatedItemId;
        var v = Sale(f, (itemId, 2_17_483.91m));

        AssertOneDecision(Ask(f, v), expectBillOfSupply: true);

        // …and it is NOT the §10 limb: a regular dealer may not claim composition status, so the Rule 5(1)(f)
        // declaration must stay off both the paper and the drill badge.
        Assert.False(GstReportSupport.IsCompositionBillOfSupply(f.Company, v));
        Assert.Equal(string.Empty, VoucherPrintProjector.ProjectInvoice(f.Company, v).TopDeclaration);
        Assert.Equal(string.Empty, new VoucherDetailViewModel(f.Company, v).BillOfSupplyDeclaration);

        // Rule 49 prescribes no rate and no tax-amount particular, so the document carries no breakup and its
        // "value of supply" IS its total — it must still foot to the debt the GL recorded, to the paisa.
        var data = VoucherPrintProjector.ProjectInvoice(f.Company, v);
        Assert.Empty(data.TaxRows);
        Assert.Equal(Money.Zero, data.TotalCgst);
        Assert.Equal(Money.Zero, data.TotalSgst);
        Assert.Equal(Money.Zero, data.TotalIgst);
        Assert.Equal(Money.FromRupees(2_17_483.91m), data.TotalTaxable);
        Assert.Equal(v.Lines.Single(l => l.LedgerId == f.PartyId && l.Side == DrCr.Debit).Amount, data.GrandTotal);
    }

    // ================================================================ the negative controls

    /// <summary>A REGULAR dealer's TAXABLE supply is a Rule-46 tax invoice on every reading. The widening must not
    /// reclassify the ordinary case — and this control carries POSTED IGST, so it also exercises the
    /// <c>CarriesForwardTax</c> gate rather than only the declared-taxability discriminator.</summary>
    [Fact]
    public void A_taxable_supply_is_one_decision_the_other_way()
    {
        var f = Build(GstRegistrationType.Regular);
        var value = 94_271.63m;
        var igst = 16_968.89m;   // 18% of 94,271.63 = 16,968.8934 -> 16,968.89
        var outIgst = f.Company.Ledgers.Single(l => l.GstClassification is
        { Direction: GstTaxDirection.Output, TaxHead: GstTaxHead.Integrated, IsReverseCharge: false });

        var v = new Voucher(Guid.NewGuid(), f.SalesTypeId, MoveDate, new List<EntryLine>
        {
            new(f.PartyId, new Money(value + igst), DrCr.Debit),
            new(f.SalesLedgerId, new Money(value), DrCr.Credit),
            new(outIgst.Id, new Money(igst), DrCr.Credit,
                gst: new GstLineTax(GstTaxHead.Integrated, 1800, new Money(value))),
        }, partyId: f.PartyId, inventoryLines: new[]
        {
            new VoucherInventoryLine(f.TaxableItemId, f.GodownId, 1m, new Money(value)),
        });

        Assert.True(GstReportSupport.CarriesForwardTax(f.Company, v));
        AssertOneDecision(Ask(f, v), expectBillOfSupply: false);
    }

    /// <summary>A supply carrying even ONE taxable line is a tax invoice for the WHOLE document. §31(3)(c) reserves the
    /// bill of supply for a supply of <b>exempted</b> goods; Rule 46A's combined "invoice-cum-bill of supply" is
    /// permissive ("may be issued") and confined to an unregistered recipient, so it cannot make the bill of supply the
    /// required document. The taxable line here is ₹41.09 against ₹2,17,483.91 exempt — a rounding-scale line must be
    /// enough to decide the document kind.</summary>
    [Fact]
    public void A_mixed_supply_is_a_tax_invoice_on_every_reading()
    {
        var f = Build(GstRegistrationType.Regular);
        var v = Sale(f, (f.ExemptItemId, 2_17_483.91m), (f.TaxableItemId, 41.09m));
        AssertOneDecision(Ask(f, v), expectBillOfSupply: false);
    }

    /// <summary>An UNRESOLVED line — no GST master on the item, none on the value ledger, none on the company — is not
    /// read as exempt. Silence is not an exemption; reading it as one would strip the tax breakup off a genuinely
    /// taxable supply and file a Bill of Supply for it.</summary>
    [Fact]
    public void An_unresolved_supply_is_a_tax_invoice_on_every_reading()
    {
        var f = Build(GstRegistrationType.Regular);
        var v = Sale(f, (f.UnclassifiedItemId, 2_17_483.91m));
        AssertOneDecision(Ask(f, v), expectBillOfSupply: false);
    }

    // ================================================================ the ONE documented print/file divergence

    /// <summary>
    /// <b>🔴 The single shape where the printed title and the filed <c>docType</c> are NOT the same predicate — and the
    /// proof that it is still not a contradiction.</b> A §10 (composition) outward movement carrying posted forward tax
    /// files <c>BIL</c> by a recorded user ruling (R12, 2026-08-14: §31(3)(c) is unconditional for a §10 person, and the
    /// NIC <c>docType</c> carries no money for <c>IsBillOfSupply</c>'s print-money gate to protect), while the printer
    /// refuses the voucher outright.
    ///
    /// <para>The equivalence <see cref="AssertOneDecision"/> asserts is therefore intact: it binds the printed
    /// <b>statutory title</b> to the filed code, and this shape has <b>no statutory title at all</b> — no TAX INVOICE,
    /// no BILL OF SUPPLY, no drill badge, no Rule 5(1)(f) declaration. It prints as the plain Dr/Cr voucher, which
    /// states every posted leg exactly. A filing that must name a document kind and a document that is never issued
    /// cannot disagree; what WOULD be a contradiction is paper titled one thing and a filing declaring another, and
    /// that is what this test exists to keep out.</para>
    ///
    /// <para>Money is odd to the paisa and hand-keyed with no <c>GstLineTax</c> — the As-Voucher shape, so the ruling is
    /// pinned against the general-ledger read rather than against line metadata.</para>
    /// </summary>
    [Fact]
    public void The_section_10_contradiction_files_BIL_while_printing_no_statutory_title_at_all()
    {
        // Regular first (so EnableGst seeds the six tax ledgers), then the F11 switch to Composition — the reachable
        // route: EnableGst only SKIPS seeding for composition, it never deletes.
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
            new(outCgst.Id, new Money(cgst), DrCr.Credit),
            new(outSgst.Id, new Money(sgst), DrCr.Credit),
        }, partyId: f.PartyId, inventoryLines: new[]
        {
            new VoucherInventoryLine(f.TaxableItemId, f.GodownId, 1m, new Money(value)),
        });

        // The FILING names the document §31(3)(c) obliges a §10 person to issue.
        Assert.Equal("BIL", f.Service.PrepareRecord(v, MoveDate).DocType);

        // The PAPER names nothing at all — the projector refuses it structurally rather than issue either document.
        Assert.False(VoucherPrintProjector.IsBillOfSupply(c, v));
        Assert.False(VoucherPrintProjector.IsTaxInvoice(c, v));
        Assert.Throws<InvalidOperationException>(() => VoucherPrintProjector.ProjectInvoice(c, v));

        var detail = new VoucherDetailViewModel(c, v);
        Assert.Equal(string.Empty, detail.DocumentLabel);
        Assert.Equal(string.Empty, detail.BillOfSupplyDeclaration);
    }

    // ================================================================ the wrapper carries no logic of its own

    /// <summary>
    /// <b>The delegation itself, pinned.</b> <c>VoucherPrintProjector</c> keeps three predicates whose names match the
    /// engine's, and every one of them must be a PURE FORWARD — a body of its own here is precisely how the two
    /// <c>IsBillOfSupply</c> predicates came to disagree in the first place. This drives the whole document matrix
    /// through both layers and fails on the first divergence, so re-adding a condition to any wrapper is caught by a
    /// test rather than by a reviewer.
    ///
    /// <para><b>🔴 W0-9 REVIEW FINDING #1 — TWO OF THE THREE COMPARISONS USED TO BE CONSTANT-VALUED.</b> The matrix was
    /// five ITEM-invoice Sales vouchers, and on every one of them <c>IsTaxInvoice</c> is true (a Sales voucher with
    /// stock lines and no untagged Output tax) and <c>IsServiceAccountingInvoice</c> is false (it returns false the
    /// moment <c>HasInventoryLines</c> is true). Two of the three lines therefore compared <c>true == true</c> and
    /// <c>false == false</c> on every iteration and <b>could not have failed under any implementation of their
    /// wrappers</b>. The single test that advertised itself as the lock on all three was one third of a lock — the same
    /// failure mode W0-10 found in the old characterization test (an assertion that reads as a strong guarantee and
    /// enforces nothing).</para>
    ///
    /// <para><b>What the matrix now contains, and why each shape earns its place.</b> Five ledger-only and inward
    /// shapes join the five item ones, chosen so that each predicate takes BOTH values across the matrix:</para>
    /// <list type="bullet">
    /// <item><c>ledger-only/plain-sale</c> — a Sales voucher with no stock lines and no v49 accounting-invoice flag:
    /// <c>IsServiceAccountingInvoice</c> false ⇒ <c>IsTaxInvoice</c> false. Under Composition it is <b>still</b> a bill
    /// of supply (limb 1 tests the base type alone), so it is also the shape on which the three predicates most sharply
    /// disagree with one another — a wrapper mutation cannot hide behind its neighbours.</item>
    /// <item><c>ledger-only/service-exempt</c> — the v49 flag plus an exempt SAC leg: all three TRUE.</item>
    /// <item><c>ledger-only/service-zero-rated</c> — a taxable SAC leg at 0% (LUT/export): a genuine Rule-46 tax
    /// invoice that posts no tax leg, so <c>IsServiceAccountingInvoice</c> and <c>IsTaxInvoice</c> are true while the
    /// exempt limb correctly refuses it. It needs no seeded Output tax ledger, so it exists under both registrations.</item>
    /// <item><c>item/untagged-output-tax</c> — Output CGST/SGST hand-keyed with no <c>GstLineTax</c>:
    /// <c>PostedOutputTaxIsFullyTagged</c> false, so <c>IsTaxInvoice</c> is FALSE on an ITEM voucher. Regular only —
    /// a Composition company has no seeded Output tax ledgers to key.</item>
    /// <item><c>purchase/item</c> — an inward goods movement: all three false on the base-type gate.</item>
    /// </list>
    ///
    /// <para><b>And the non-vacuity is itself asserted, not merely intended.</b> The engine's answers are collected and
    /// each predicate is required to have produced both <c>true</c> and <c>false</c> somewhere in the matrix. Deleting
    /// or narrowing a shape can therefore no longer quietly return this test to two-thirds vacuous — it fails with the
    /// name of the predicate that went constant. That check is the actual fix for finding #1; the extra shapes are how
    /// it is satisfied.</para>
    /// </summary>
    [Fact]
    public void The_desktop_wrappers_never_answer_differently_from_the_engine()
    {
        var sawBillOfSupply = new HashSet<bool>();
        var sawTaxInvoice = new HashSet<bool>();
        var sawServiceInvoice = new HashSet<bool>();

        foreach (var registration in new[] { GstRegistrationType.Regular, GstRegistrationType.Composition })
        {
            bool composition = registration == GstRegistrationType.Composition;
            var f = Build(registration);
            // 🔴 T0-11 slice S1 — EVERY ROW NOW CARRIES ITS EXPECTED ANSWER, AND THAT IS THE POINT OF THIS EDIT.
            // The matrix used to assert wrapper-vs-engine AGREEMENT and nothing else, which is a strictly weaker
            // claim than it reads as: two layers that agree can agree on the WRONG answer, and the purchase row
            // below is exactly where that mattered. The census proposed "fixing" T0-11 by flipping IsTaxInvoice's
            // Sales gate so a Purchase would print with item detail; under that change the wrapper would have gone
            // on forwarding faithfully to the engine, both layers would have moved together, and this test — the
            // one test in the repository that drives a real purchase item-invoice through the document-kind rule —
            // WOULD HAVE STAYED GREEN while the app began titling a supplier's document as our own tax invoice.
            // The expectations are derived from the statute, never from the predicates: CGST Act §31(1)/(2) put the
            // duty on "a registered person SUPPLYING", §31(3)(c) + §2(47) give the bill of supply, §10(4) bars a
            // composition dealer from collecting tax.
            var shapes = new List<(string Name, Voucher V, bool Bos, bool Tax, bool Svc)>
            {
                // A taxable outward supply is a Rule-46 tax invoice — unless the supplier is a §10 dealer, for whom
                // §31(3)(c) is unconditional ("shall issue, INSTEAD OF a tax invoice, a bill of supply").
                ("item/taxable",         Sale(f, (f.TaxableItemId, 94_271.63m)),
                    Bos: composition, Tax: true, Svc: false),
                // Wholly exempt / nil-rated: §31(3)(c)'s first limb for a Regular dealer, §10 for a composition one.
                // §2(47) folds nil-rated and non-taxable into "exempt supply", so both taxabilities take one limb.
                ("item/exempt",          Sale(f, (f.ExemptItemId, 2_17_483.91m)),
                    Bos: true, Tax: true, Svc: false),
                ("item/nil-rated",       Sale(f, (f.NilRatedItemId, 1_63_059.37m)),
                    Bos: true, Tax: true, Svc: false),
                // Silence is not an exemption: a line with no GST master anywhere is not read as exempt, so a
                // Regular dealer's document stays a tax invoice. A §10 dealer's is a bill of supply regardless.
                ("item/unresolved",      Sale(f, (f.UnclassifiedItemId, 2_17_483.91m)),
                    Bos: composition, Tax: true, Svc: false),
                // One taxable line decides the whole document (Rule 46A's combined form is permissive and confined
                // to an unregistered recipient), so ₹41.09 of taxable against ₹2,17,483.91 exempt is a tax invoice.
                ("item/mixed",           Sale(f, (f.ExemptItemId, 2_17_483.91m), (f.TaxableItemId, 41.09m)),
                    Bos: composition, Tax: true, Svc: false),
                // An As-Voucher sale is not an invoice-mode entry, so no invoice document is projected for it. Note
                // the §10 row: a composition dealer's As-Voucher sale IS a bill of supply (limb 1 tests the base
                // type alone) that is nonetheless NOT rendered as an invoice document — the one shipped shape that
                // proves entitlement and rendering were always two questions, which is census T0-11's whole thesis.
                ("ledger-only/plain-sale",
                    LedgerOnlySale(f, f.SalesLedgerId, 1_63_059.37m, accountingInvoice: false),
                    Bos: composition, Tax: false, Svc: false),
                // A declared-exempt SAC service takes §31(3)(c) exactly as exempt goods do.
                ("ledger-only/service-exempt",
                    LedgerOnlySale(f, f.ExemptServiceLedgerId, 2_17_483.91m, accountingInvoice: true),
                    Bos: true, Tax: true, Svc: true),
                // A TAXABLE service at a 0% (LUT/export) rate is not an exempt supply — it attracts tax at a nil
                // RATE under a taxable classification — so §31(2)'s tax invoice stands for a Regular dealer.
                ("ledger-only/service-zero-rated",
                    LedgerOnlySale(f, f.ZeroRatedServiceLedgerId, 94_271.63m, accountingInvoice: true),
                    Bos: composition, Tax: true, Svc: true),
                // 🔴 THE ROW THE NAIVE FIX WOULD HAVE MOVED. §31(1) binds "a registered person SUPPLYING": on an
                // inward supply we supply nothing and are entitled to issue NO document — not a tax invoice, and
                // not a bill of supply either, because Rule 49 says that one too is "issued by the supplier". These
                // three FALSEs are a permanent statutory claim and must survive every later T0-11 slice: slice S2
                // makes a purchase RENDER with item detail as a recipient-side record, which is a different axis and
                // changes none of them. Any change that turns one of these true has re-titled someone else's
                // document as ours and has silently moved the NIC e-Way docType with it.
                ("purchase/item",        PurchaseItem(f, f.TaxableItemId, 2_17_483.91m),
                    Bos: false, Tax: false, Svc: false),
            };
            // Regular only: a Composition company has none of the six Output tax ledgers to hand-key against.
            // Output CGST/SGST posted with no GstLineTax: the money is in the GL but invisible to the item pass, so
            // the document would print a Grand Total short of the posted party leg. It is neither document.
            if (UntaggedOutputTaxSale(f, 2_17_483.91m, 19_573.55m, 19_573.56m) is { } untagged)
                shapes.Add(("item/untagged-output-tax", untagged, Bos: false, Tax: false, Svc: false));

            foreach (var (name, v, expectBos, expectTax, expectSvc) in shapes)
            {
                var why = $"{registration}/{name}";

                var engineBos = GstReportSupport.IsBillOfSupply(f.Company, v);
                var engineTax = GstReportSupport.IsTaxInvoice(f.Company, v);
                var engineSvc = GstReportSupport.IsServiceAccountingInvoice(f.Company, v);
                sawBillOfSupply.Add(engineBos);
                sawTaxInvoice.Add(engineTax);
                sawServiceInvoice.Add(engineSvc);

                // The ANSWER, from the statute — asserted BEFORE the agreement, because an agreement between two
                // layers that are both wrong is the failure this row set exists to catch.
                Assert.True(expectBos == engineBos, $"IsBillOfSupply answered wrongly for {why}");
                Assert.True(expectTax == engineTax, $"IsTaxInvoice answered wrongly for {why}");
                Assert.True(expectSvc == engineSvc, $"IsServiceAccountingInvoice answered wrongly for {why}");

                // …and then the delegation: each wrapper must be a pure forward, carrying no logic of its own.
                Assert.True(engineBos == VoucherPrintProjector.IsBillOfSupply(f.Company, v),
                    $"IsBillOfSupply diverged for {why}");
                Assert.True(engineTax == VoucherPrintProjector.IsTaxInvoice(f.Company, v),
                    $"IsTaxInvoice diverged for {why}");
                Assert.True(engineSvc == VoucherPrintProjector.IsServiceAccountingInvoice(f.Company, v),
                    $"IsServiceAccountingInvoice diverged for {why}");

                // T0-11 slice S1: the classification seam is required to agree with the answer too, so a later slice
                // cannot move the printed document without either moving the statutory expectation above or being
                // caught here. The badge is the same decision, so it is driven from the same row.
                var doc = GstReportSupport.ClassifyPrintedDocument(f.Company, v);
                // 🔴 T0-11 slice S2 — THE ROW WHERE THE CLASSIFICATION AND THE ENTITLEMENT PREDICATES PART COMPANY,
                // which is the whole thesis of the census item. The three engine answers above stay FALSE for a
                // purchase forever (§31(1): the duty is the supplier's), and yet the purchase now RENDERS with item
                // detail as a recipient-side record under its own badge — because rendering and orientation are
                // different axes from entitlement. Derived from RQ-11a, not from the classifier; the full derivation
                // is in PrintedDocumentClassificationTests.StatutoryExpectation.
                bool inwardRecord = name == "purchase/item";
                var expectedRenders = inwardRecord || expectTax;
                var expectedTax = inwardRecord ? TaxParticulars.AsChargedByTheSupplier
                    : expectBos ? TaxParticulars.None : TaxParticulars.AsChargedByUs;
                Assert.True(expectedRenders == doc.RendersItemDetail, $"RendersItemDetail answered wrongly for {why}");
                Assert.True(expectedTax == doc.StatesTax, $"StatesTax answered wrongly for {why}");
                var expectedLabel = inwardRecord ? GstReportSupport.PurchaseRecordScreenLabel
                    : expectBos ? "Bill of Supply" : expectTax ? "Tax Invoice" : string.Empty;
                Assert.True(expectedLabel == doc.ScreenLabel, $"ScreenLabel answered wrongly for {why}");
                Assert.True(expectedLabel == new VoucherDetailViewModel(f.Company, v).DocumentLabel,
                    $"DocumentLabel diverged from the classification for {why}");
            }
        }

        // 🔴 Finding #1's actual fix: a comparison that cannot fail proves nothing, so the matrix must be shown to
        // exercise BOTH answers of every predicate. If a future edit narrows the shapes, this fails by name.
        Assert.True(sawBillOfSupply.Count == 2, "IsBillOfSupply was constant-valued across the matrix");
        Assert.True(sawTaxInvoice.Count == 2, "IsTaxInvoice was constant-valued across the matrix");
        Assert.True(sawServiceInvoice.Count == 2, "IsServiceAccountingInvoice was constant-valued across the matrix");
    }
}
