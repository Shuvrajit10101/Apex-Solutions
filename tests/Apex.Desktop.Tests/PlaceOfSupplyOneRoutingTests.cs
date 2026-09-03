using System;
using System.Collections.Generic;
using System.Linq;
using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Io;
using Apex.Ledger.Reports;
using Apex.Ledger.Services;
using Apex.Desktop.Services;
using Apex.Desktop.ViewModels;
using Xunit;
using DomainLedger = Apex.Ledger.Domain.Ledger;

namespace Apex.Desktop.Tests;

/// <summary>
/// <b>W0-15 — ONE routing rule, ONE place of supply.</b>
/// </summary>
public sealed class PlaceOfSupplyOneRoutingTests
{
    private static readonly DateOnly FyStart = new(2025, 4, 1);
    private static readonly DateOnly SaleDate = new(2025, 4, 10);

    private const string HomeState = "19";    // West Bengal — the SUPPLIER's own State
    private const string PartyState = "07";   // Delhi — the buyer's

    private static readonly string GstinHome = "19AAAAA0000A1Z" + Gstin.ComputeCheckDigit("19AAAAA0000A1Z0");
    private const string GstinDelhi = "07AAACA1111A1ZU";
    private static readonly string GstinWestBengalCustomer =
        "19AAACA1111A1Z" + Gstin.ComputeCheckDigit("19AAACA1111A1Z0");

    // 3 Nos @ ₹13,772.54 = ₹41,317.62 — odd to the paisa; a round figure asserts nothing here.
    private const decimal Qty = 3m;
    private static readonly Money UnitRate = Money.FromRupees(13_772.54m);
    private static readonly Money SupplyValue = Money.FromRupees(41_317.62m);

    /// <summary>18% IGST on ₹41,317.62, read off the engine in <see cref="Build"/> and frozen here.</summary>
    private const decimal PostedIgst = 7_437.17m;

    /// <summary>₹2,04,317.63 — the e-Way family's canonical odd movement value, well over the ₹50,000 baseline.</summary>
    private static readonly Money MovementValue = Money.FromRupees(2_04_317.63m);

    private sealed class Fx
    {
        public required Company Company { get; init; }
        public required Voucher Sale { get; init; }
        public required Guid CustomerId { get; init; }
    }

    private static Fx Build(string? partyStateCode = PartyState, string? partyGstin = GstinDelhi) =>
        BuildCore("Place of Supply Co", partyStateCode, partyGstin, Qty, UnitRate, SupplyValue, PostedIgst,
            eWayBill: false, eWayIntraStateApplicable: true, stateOverrideFor: null);

    /// <summary>The same book, billing the ₹2,04,317.63 goods movement the e-Way limbs need, with e-Way switched on.</summary>
    private static Fx BuildMovement(bool eWayIntraStateApplicable, string? stateOverrideFor) =>
        BuildCore("e-Way Routing Co", PartyState, GstinDelhi, 1m, MovementValue, MovementValue,
            expectedIgst: 36_777.17m,
            eWayBill: true, eWayIntraStateApplicable: eWayIntraStateApplicable, stateOverrideFor: stateOverrideFor);

    /// <summary>The same book billing a <b>zero-rated (LUT/export)</b> supply: a TAX INVOICE that posts no forward tax
    /// leg at all, which is the only shape whose routing can end up genuinely unknown.</summary>
    private static Fx BuildZeroRated() =>
        BuildCore("Zero-Rated Routing Co", PartyState, GstinDelhi, Qty, UnitRate, SupplyValue,
            expectedIgst: 0m, eWayBill: false, eWayIntraStateApplicable: true, stateOverrideFor: null,
            rateBasisPoints: 0);

    private static Fx BuildCore(
        string companyName, string? partyStateCode, string? partyGstin,
        decimal quantity, Money unitRate, Money supplyValue, decimal expectedIgst,
        bool eWayBill, bool eWayIntraStateApplicable, string? stateOverrideFor,
        int rateBasisPoints = 1800)
    {
        var c = CompanyFactory.CreateSeeded(companyName, FyStart);
        var gst = new GstService(c);
        var config = new GstConfig
        {
            HomeStateCode = HomeState,
            Gstin = GstinHome,
            RegistrationType = GstRegistrationType.Regular,
            ApplicableFrom = FyStart,
            Periodicity = GstReturnPeriodicity.Monthly,
            EWayBillEnabled = eWayBill,
            EWayApplicableFrom = eWayBill ? FyStart : null,
            EWayIntraStateApplicable = eWayIntraStateApplicable,
        };
        if (stateOverrideFor is not null)
            config.AddEWayStateThreshold(new EWayStateThreshold(
                Guid.NewGuid(), stateOverrideFor, EWayTransactionType.Regular, Money.FromRupees(5_00_000m)));
        gst.EnableGst(config);

        var inv = new InventoryService(c);
        var item = inv.CreateStockItem("Widget", inv.CreateStockGroup("Goods").Id, inv.CreateSimpleUnit("Nos", "Numbers").Id);
        item.Gst = new StockItemGstDetails { HsnSac = "847130", Taxability = GstTaxability.Taxable, RateBasisPoints = rateBasisPoints };
        var main = c.MainLocation!.Id;
        inv.AddOpeningBalance(item.Id, main, 100m, Money.FromRupees(311.17m));

        var sales = AddLedger(c, "Sales", "Sales Accounts", openingIsDebit: false);
        var customer = AddLedger(c, "Out-of-State Customer", "Sundry Debtors", openingIsDebit: true);
        customer.PartyGst = new PartyGstDetails
        { RegistrationType = GstRegistrationType.Regular, Gstin = partyGstin, StateCode = partyStateCode };

        var post = new LedgerService(c);
        var salesType = c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Sales).Id;

        // The routing the ENGINE derives at posting time — never a hand-picked bool.
        var interState = gst.IsInterState(customer.PartyGst.StateCode);
        Assert.True(interState);
        var tax = gst.ComputeInvoiceTax(
            new[] { new GstService.TaxableLine(supplyValue, rateBasisPoints) }, interState, GstTaxDirection.Output);
        Assert.Equal(expectedIgst, tax.TotalIgst.Amount);   // the frozen figure IS the engine's

        var lines = new List<EntryLine>
        {
            new(customer.Id, new Money(supplyValue.Amount + tax.TotalTax.Amount), DrCr.Debit),
            new(sales.Id, supplyValue, DrCr.Credit),
        };
        lines.AddRange(tax.TaxLines);

        var sale = post.Post(new Voucher(Guid.NewGuid(), salesType, SaleDate, lines, partyId: customer.Id,
            inventoryLines: new[] { new VoucherInventoryLine(item.Id, main, quantity, unitRate) }));

        return new Fx { Company = c, Sale = sale, CustomerId = customer.Id };
    }

    private static DomainLedger AddLedger(Company c, string name, string groupName, bool openingIsDebit)
    {
        var l = new DomainLedger(Guid.NewGuid(), name, c.FindGroupByName(groupName)!.Id, Money.Zero, openingIsDebit);
        c.AddLedger(l);
        return l;
    }

    private static decimal PostedHead(Voucher v, GstTaxHead head) =>
        v.Lines.Where(l => l.Gst?.TaxHead == head).Sum(l => l.Amount.Amount);

    /// <summary>The place of supply GSTR-1 FILES for this voucher.</summary>
    private static string? FiledPlaceOfSupply(Company c, Money taxableValue)
    {
        var g = Gstr1.Build(c, FyStart, SaleDate);
        var row = Assert.Single(g.B2B, r => r.TaxableValue.Amount == taxableValue.Amount);
        return row.PlaceOfSupplyStateCode;
    }

    /// <summary>
    /// The invoice's own rendering of a State code, restated here independently of the projector so the comparison
    /// below discriminates ("West Bengal (19)"; empty for an unrecognised/absent code).
    /// </summary>
    private static string StateText(string? code) =>
        IndianState.FromCode(code) is { } s ? $"{s.Name} ({s.Code})" : string.Empty;

    // ================================================================ 1 — the red proof

    /// <summary>
    /// <b>One supply cannot have two places of supply — and neither of them may be the supplier's own State on a
    /// document bearing IGST.</b>
    /// </summary>
    [Theory]
    [InlineData(null)]        // the party's State is CLEARED after posting (permitted — EnsureValid rejects only INVALID codes)
    [InlineData(HomeState)]   // …or EDITED to the company's own home State — the same wrong answer by the other rung
    public void The_place_of_supply_a_reprint_states_and_the_one_gstr1_files_cannot_disagree(string? editedPartyState)
    {
        var f = Build();
        var c = f.Company;

        // The books really do carry IGST — the document is an inter-state supply, whatever any master says later.
        Assert.Equal(PostedIgst, PostedHead(f.Sale, GstTaxHead.Integrated));
        Assert.Equal(0m, PostedHead(f.Sale, GstTaxHead.Central));

        c.FindLedger(f.CustomerId)!.PartyGst!.StateCode = editedPartyState;

        var printed = VoucherPrintProjector.ProjectInvoice(c, f.Sale).PlaceOfSupply;
        var filed = FiledPlaceOfSupply(c, SupplyValue);

        // (a) NIC e-invoice validation 24 makes supplier-State == place-of-supply on an IGST document self-refuting.
        Assert.NotEqual(HomeState, filed);
        // (b) …and the paper and the return must say the SAME thing about the same supply.
        Assert.Equal(StateText(filed), printed);
    }

    // ================================================================ 2 — F7: the reprint that could not happen

    /// <summary>
    /// <b>F7 — an already-issued invoice must still REPRINT after the book stops being able to route a supply.</b>
    /// The throw sat on <c>ProjectInvoice</c>'s opening line and fired for every projection, even though the value it
    /// computed is consumed ONLY where the voucher posted no forward tax leg (<c>ReadPostedMoney</c>'s
    /// <c>postedRouting ?? livePartyInterState</c>) — so a document that carries IGST, and therefore never needs it,
    /// could not be printed at all.
    /// <para><b>Bite:</b> restore the eager <c>GstService.IsInterState</c> (the throwing form) at the top of
    /// <c>ProjectInvoice</c> and this throws <c>InvalidOperationException("GST is not enabled (no home state) —
    /// cannot route a supply.")</c> instead of returning a document. Measured on HEAD.</para>
    /// <para>The DESCRIPTIVE particulars survive because nothing about them was ever in doubt: the buyer's recorded
    /// State is a fact about the BUYER, and with no home State there is no routing to contradict it — the
    /// reconciliation has nothing to compare against, so it prints the master verbatim (the same limb a voucher with
    /// no posted tax leg already takes).</para>
    /// </summary>
    [Fact]
    public void An_already_issued_invoice_still_reprints_after_the_home_state_is_cleared()
    {
        var f = Build();
        var c = f.Company;

        var before = VoucherPrintProjector.ProjectInvoice(c, f.Sale);
        Assert.Equal(PostedIgst, before.TotalIgst.Amount);
        Assert.Equal("Delhi (07)", before.PlaceOfSupply);

        c.Gst!.HomeStateCode = null;      // the book can no longer route ANY supply

        var after = VoucherPrintProjector.ProjectInvoice(c, f.Sale);
        Assert.Equal(PostedIgst, after.TotalIgst.Amount);
        Assert.Equal(before.GrandTotal, after.GrandTotal);
        Assert.True(after.IsInterState);                 // the POSTED IGST is history and still routes the document
        Assert.Equal("Delhi (07)", after.PlaceOfSupply); // the buyer's own recorded State — nothing to contradict it
        Assert.Equal(GstinDelhi, after.Buyer.Gstin);
    }

    // ================================================================ 3 — the e-Way misrouting

    /// <summary>
    /// <b>An unrouteable book may not buy the intra-state e-Way EXEMPTION.</b> <c>EWayBillService</c>'s own private
    /// <c>IsInterState</c> returned <b>false</b> for a null home State, and <c>false</c> in this codebase is not
    /// "unknown" — it is the positive assertion "intra-state", which <c>CoverageOf</c> then spends on a relaxation:
    /// a company that exempts intra-state movements got <see cref="EWayCoverage.NotRequired"/> for a ₹2,41,094.80
    /// consignment. Rule 138's ₹50,000 flat threshold is the BASELINE; the intra-state exemption is a relaxation that
    /// presupposes a known State, so an unrouteable movement falls back to the baseline and is covered.
    /// <para><b>Bite:</b> widen the exemption test from <c>is false</c> back to <c>!interState</c> (i.e. let unknown
    /// count as intra) and this returns <c>NotRequired</c>. Measured on HEAD.</para>
    /// </summary>
    [Fact]
    public void An_unrouteable_book_may_not_buy_the_intra_state_eway_exemption()
    {
        var f = BuildMovement(eWayIntraStateApplicable: false, stateOverrideFor: null);
        var svc = new EWayBillService(f.Company);
        Assert.Equal(EWayCoverage.Required, svc.CoverageOf(f.Sale));    // inter-state, well over ₹50,000

        f.Company.Gst!.HomeStateCode = null;

        Assert.Equal(EWayCoverage.Required, svc.CoverageOf(f.Sale));
    }

    /// <summary>
    /// The other relaxation the same <c>false</c> was spent on: the <b>per-State intra-state threshold override</b>.
    /// A ₹5,00,000 Delhi override is keyed on the place of supply and applies to intra-state movements only, so an
    /// unrouteable book must not reach it — it takes the flat ₹50,000 and the movement stays covered.
    /// <para><b>Bite:</b> widen <c>EffectiveThreshold</c>'s limb from <c>is false</c> back to <c>!interState</c> and
    /// this returns <c>NotRequired</c> on a ₹2,41,094.80 consignment. Measured on HEAD.</para>
    /// </summary>
    [Fact]
    public void An_unrouteable_book_may_not_reach_a_per_state_intra_state_threshold_override()
    {
        var f = BuildMovement(eWayIntraStateApplicable: true, stateOverrideFor: PartyState);
        var svc = new EWayBillService(f.Company);
        Assert.Equal(EWayCoverage.Required, svc.CoverageOf(f.Sale));

        f.Company.Gst!.HomeStateCode = null;

        Assert.Equal(EWayCoverage.Required, svc.CoverageOf(f.Sale));
    }

    /// <summary>
    /// <b>PINNED GAP (W0-15, not fixed here) — <c>PrepareRecord</c> is a WRITE path that still mints a portal request
    /// from a State the book does not have.</b> It reads <c>_company.Gst!.HomeStateCode</c> directly and stamps it
    /// into the record's consignor/consignee ends, so an unrouteable book produces an EWB-01 request with a null
    /// ShipFrom State — three columns of the NIC mapping filled from nothing. Making the write path refuse is a
    /// change to what the app REJECTS, not to a figure it states, and is out of this slice's scope.
    /// <para>This test states today's behaviour so it cannot change silently: the day <c>PrepareRecord</c> starts
    /// refusing, this fails and the gap is closed deliberately.</para>
    /// </summary>
    [Fact]
    public void PINNED_GAP_prepare_record_still_stamps_a_null_home_state_into_the_portal_request()
    {
        var f = BuildMovement(eWayIntraStateApplicable: false, stateOverrideFor: null);
        f.Company.Gst!.HomeStateCode = null;

        var svc = new EWayBillService(f.Company);
        Assert.Equal(EWayCoverage.Required, svc.CoverageOf(f.Sale));

        var record = svc.PrepareRecord(f.Sale, SaleDate);
        Assert.Equal("O", record.SupplyType);
        Assert.Null(record.ShipFromStateCode);            // ← the gap: a consignor end with no State
        Assert.Equal(PartyState, record.ShipToStateCode);
    }

    // ================================================================ 4 — the whitespace divergence

    /// <summary>
    /// <b>A trailing space on the party's State code routed the document INTER at posting and INTRA at reprint.</b>
    /// <c>GstService.IsInterState</c> compares <c>Ordinal</c> and untrimmed, so <c>"19 "</c> against a home of
    /// <c>"19"</c> is a different State ⇒ IGST is posted. <c>VoucherPrintProjector.ConsistentBuyerStateCode</c> — the
    /// FOURTH copy of the routing rule — compared <c>OrdinalIgnoreCase</c> after <c>Trim()</c>, so it read the same
    /// two codes as the SAME State, judged the live master to contradict the posted IGST, and took its
    /// "inter ⇒ unrecoverable" limb: the buyer's real, historically correct GSTIN was struck off the reprint by a
    /// space. Rule 46(b) requires it on the document.
    /// <para><b>Bite:</b> re-derive the buyer-block routing locally with <c>Trim()</c>/<c>OrdinalIgnoreCase</c>
    /// instead of delegating to the one shared rule and the GSTIN goes back to <c>""</c>. Measured on HEAD.</para>
    /// <para><b>▶ REVIEW CORRECTION — this test used to assert ONLY the GSTIN, and a live divergence survived beside
    /// it.</b> The printed State TEXT is empty because <c>IndianState.FromCode</c> does not trim; the first draft
    /// called that "a separate (input-validation) defect" and stopped there — without noticing that GSTR-1 was FILING
    /// <c>"19 "</c> for the same voucher. So the paper said nothing, the return said the supplier's own State modulo a
    /// space, on a document bearing IGST (NIC validation 24), in a code no State master contains. The FILED value is
    /// now asserted beside the GSTIN, which is the assertion that would have caught it.
    /// <br/>The padded code being accepted onto the master is still a separate, still-open input-validation defect and
    /// is still not asserted as if this slice fixed it. Nothing trims — trimming would re-route the TAX.</para>
    /// </summary>
    [Fact]
    public void A_trailing_space_on_the_party_state_may_not_strike_the_buyer_gstin_off_the_reprint()
    {
        var f = Build(partyStateCode: HomeState + " ", partyGstin: GstinWestBengalCustomer);
        var c = f.Company;

        Assert.Equal(PostedIgst, PostedHead(f.Sale, GstTaxHead.Integrated));   // posted INTER, on a space

        var data = VoucherPrintProjector.ProjectInvoice(c, f.Sale);
        Assert.True(data.IsInterState);
        Assert.Equal(GstinWestBengalCustomer, data.Buyer.Gstin);

        // The paper and the return still cannot state two places of supply for one supply.
        var filed = FiledPlaceOfSupply(c, SupplyValue);
        Assert.Null(filed);                                    // ← filed "19 " before the review fix
        Assert.Equal(string.Empty, data.PlaceOfSupply);
        Assert.Equal(StateText(filed), data.PlaceOfSupply);
    }

    // ================================================================ 5 — what "unknown" RENDERS as

    /// <summary>
    /// <b>A document nothing routed states NO tax head — not "CGST 0.00 / SGST 0.00".</b> This is the shape that
    /// makes <c>InvoicePrintData.IsInterState</c> genuinely three-valued: a <b>zero-rated (LUT/export)</b> item
    /// invoice is a TAX INVOICE (a zero-rated supply is a taxable supply, §16 IGST) yet posts no forward tax leg at
    /// all, so nothing about the routing is on the books — and once the home State is cleared the party master cannot
    /// supply it either.
    /// <para>On a plain <c>bool</c> that unknown collapsed to <c>false</c>, and <c>false</c> is not an absence here:
    /// <c>InvoicePdf</c> spends it on an "Intra-State (CGST + SGST)" caption and a CGST/SGST head-row PAIR — an
    /// intra-state supply asserted on a document whose own buyer block names another State's GSTIN.</para>
    /// <para><b>▶ REVIEW CORRECTION — one of the two bites stated here was FICTION, and it is deleted rather than
    /// re-worded.</b> The first draft claimed that collapsing the null in <c>InvoicePdf.HeadRowCount</c> (<c>null
    /// =&gt; 2</c>) would turn this red. <b>Measured: it did not — Apex.Ledger.Io.Tests 389/389 and
    /// Apex.Desktop.Tests 2125/2125 both stayed green.</b> <c>HeadRowCount</c> was an <c>int</c> consumed ONLY by
    /// <c>MeasureClosingBlock</c>, i.e. geometry, while the DRAWING re-decided with its own switch over the same
    /// property — so the "single source" the code comment declared did not exist, and nothing this test asserts moves
    /// when a measured height changes. The renderer now returns the head ROWS themselves
    /// (<c>InvoicePdf.HeadRows</c>), counted by the measurement and drawn by the drawing, so the mutation that was
    /// fictional is now structurally impossible and the one below is the real one.</para>
    /// <para><b>Bite (both measured):</b> make <c>InvoicePdf.HeadRows</c>'s <c>null</c> limb return the CGST/SGST pair
    /// and "CGST"/"SGST" come back onto the page and into the preview mirror; make the caption limb unconditional
    /// again (<c>data.IsInterState is not true</c>) and "Intra-State" comes back. Either way this goes red.</para>
    /// <para><b>And the measured HEIGHT is now asserted too — separately, because this test cannot see it.</b> The
    /// height reaches only pagination, so nothing here moves when it is wrong.
    /// <c>InvoiceUnknownRoutingRenderTests.The_measured_closing_block_is_one_row_shorter_when_no_head_is_named</c>
    /// (Apex.Ledger.Io.Tests) pins it by searching for an item count at which a document naming no head fits on one
    /// page while its CGST+SGST twin does not. Over-stating the null routing's measurement by two rows — the
    /// geometry-only mutation, measured GREEN across the whole repository before that test existed — now turns it
    /// red.</para>
    /// </summary>
    [Fact]
    public void A_document_nothing_routed_states_no_tax_head_at_all()
    {
        var f = BuildZeroRated();
        var c = f.Company;
        Assert.All(f.Sale.Lines, l => Assert.Null(l.Gst));       // NOT ONE posted forward tax leg

        // While the book can still route, the party master answers — an export to Delhi is inter-state (F1).
        var before = VoucherPrintProjector.ProjectInvoice(c, f.Sale);
        Assert.False(before.IsBillOfSupply);                     // a zero-rated supply is TAXABLE, so: tax invoice
        Assert.True(before.IsInterState);
        Assert.Contains("Inter-State", AsLatin1(InvoicePdf.Render(before, new PrintConfig(), new PageConfig())));

        c.Gst!.HomeStateCode = null;

        var after = VoucherPrintProjector.ProjectInvoice(c, f.Sale);
        Assert.Null(after.IsInterState);                         // nothing established a routing — and it says so
        Assert.Equal(SupplyValue.Amount, after.GrandTotal.Amount);

        var pdf = AsLatin1(InvoicePdf.Render(after, new PrintConfig(), new PageConfig()));
        Assert.DoesNotContain("Inter-State", pdf);
        Assert.DoesNotContain("Intra-State", pdf);
        Assert.DoesNotContain("CGST", pdf);
        Assert.DoesNotContain("SGST", pdf);
        Assert.DoesNotContain("IGST", pdf);
        Assert.Contains("Grand Total", pdf);                      // the money band is still drawn, and still foots

        // The on-screen mirror the operator approves must suppress exactly the same rows as the bytes.
        var cells = new PrintPreviewViewModel(after).Pages.SelectMany(p => p.Lines).SelectMany(r => r.Cells).ToList();
        Assert.DoesNotContain("CGST", cells);
        Assert.DoesNotContain("SGST", cells);
        Assert.DoesNotContain("IGST", cells);
        Assert.Contains("Grand Total", cells);
    }

    private static string AsLatin1(byte[] bytes) => System.Text.Encoding.Latin1.GetString(bytes);
}
