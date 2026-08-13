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
        public required Guid PartyId { get; init; }
        public Guid SalesTypeId => Company.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Sales).Id;
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
        // A Gujarat (24) buyer against a Maharashtra (27) home State — the inter-state routing, so an IGST-taxed
        // control posts a single head and the e-Way threshold is comfortably met.
        var party = Add(c, "Gujarat Buyer", "Sundry Debtors", true);
        party.PartyGst = new PartyGstDetails
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
            PartyId = party.Id,
        };
    }

    private static Voucher Sale(Fx f, params (Guid ItemId, decimal Value)[] items)
    {
        var total = items.Sum(i => i.Value);
        return new Voucher(Guid.NewGuid(), f.SalesTypeId, MoveDate, new List<EntryLine>
        {
            new(f.PartyId, new Money(total), DrCr.Debit),
            new(f.SalesLedgerId, new Money(total), DrCr.Credit),
        }, partyId: f.PartyId,
        inventoryLines: items.Select(i =>
            new VoucherInventoryLine(i.ItemId, f.GodownId, 1m, new Money(i.Value))).ToArray());
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
    /// matrix covers the whole section and a regression on the composition half cannot hide.</summary>
    [Fact]
    public void The_section_10_limb_is_one_decision_across_print_filing_and_screen()
    {
        var f = Build(GstRegistrationType.Composition);
        var v = Sale(f, (f.TaxableItemId, 1_63_059.37m));
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
    /// </summary>
    [Fact]
    public void The_desktop_wrappers_never_answer_differently_from_the_engine()
    {
        foreach (var registration in new[] { GstRegistrationType.Regular, GstRegistrationType.Composition })
        {
            var f = Build(registration);
            var shapes = new (string Name, Voucher V)[]
            {
                ("taxable",      Sale(f, (f.TaxableItemId, 94_271.63m))),
                ("exempt",       Sale(f, (f.ExemptItemId, 2_17_483.91m))),
                ("nil-rated",    Sale(f, (f.NilRatedItemId, 1_63_059.37m))),
                ("unresolved",   Sale(f, (f.UnclassifiedItemId, 2_17_483.91m))),
                ("mixed",        Sale(f, (f.ExemptItemId, 2_17_483.91m), (f.TaxableItemId, 41.09m))),
            };

            foreach (var (name, v) in shapes)
            {
                var why = $"{registration}/{name}";
                Assert.True(
                    GstReportSupport.IsBillOfSupply(f.Company, v) ==
                    VoucherPrintProjector.IsBillOfSupply(f.Company, v), $"IsBillOfSupply diverged for {why}");
                Assert.True(
                    GstReportSupport.IsTaxInvoice(f.Company, v) ==
                    VoucherPrintProjector.IsTaxInvoice(f.Company, v), $"IsTaxInvoice diverged for {why}");
                Assert.True(
                    GstReportSupport.IsServiceAccountingInvoice(f.Company, v) ==
                    VoucherPrintProjector.IsServiceAccountingInvoice(f.Company, v),
                    $"IsServiceAccountingInvoice diverged for {why}");
            }
        }
    }
}
