using Apex.Ledger.Domain;
using Apex.Ledger.Reports;
using Apex.Ledger.Services;
using Xunit;

namespace Apex.Ledger.Tests;

/// <summary>
/// <b>W0-15 review — the SECOND divergence between <c>EWayBillService</c>'s deleted private <c>IsInterState</c> and
/// the shared <c>GstReportSupport.RoutingOf</c> that replaced it, and it moves a statutory coverage verdict.</b>
///
/// <para>W0-15 described the deletion as a pure delegation whose only semantic change was the null-HOME case, and
/// <c>GstReportSupport.RoutingOf</c>'s note said a blank PARTY State was "unchanged". That was true of
/// <c>GstService.IsInterState</c> and <b>false of the rule actually deleted</b>. The deleted copy read
/// <c>GstReportSupport.PlaceOfSupply</c>, whose <c>StateCode is { } code</c> pattern matches a <b>non-null EMPTY or
/// WHITESPACE</b> string, and compared it unequal to the home code — so a party State of <c>""</c> or <c>"   "</c>
/// answered <b>INTER-state</b>. <c>RoutingOf</c> tests <see cref="string.IsNullOrWhiteSpace"/> and answers
/// <b>INTRA</b>.</para>
///
/// <para><b>The new answer is the statute's and is taken deliberately.</b> IGST s.10(1)(ca) — quoted in
/// <c>docs/diverged-rules-de-place-of-supply-grounding.md</c> §4.1 — fixes the place of supply at "the location of the
/// supplier where the address of the said person is not recorded". An unrecorded recipient State is therefore a
/// DETERMINED intra-state supply, not an unknown, so the intra-state relaxations may be spent on it; the deleted
/// e-Way copy was the one departing from the ladder. What is NOT deliberate is claiming nothing changed, which is why
/// this file exists: the verdict below is different from the one this application shipped, and it is now pinned.</para>
///
/// <para><b>Reachable.</b> <c>CanonicalXml</c> reads an attribute with <c>e.Attribute(name)?.Value</c> — no trim, no
/// empty-to-null — and writes it straight into <c>PartyGstDetails.StateCode</c>, so an imported
/// <c>stateCode=""</c> produces exactly this state. Every pre-existing e-Way fixture uses real two-digit codes, which
/// is why the whole suite stayed green over the change.</para>
///
/// <para><b>RED PROOF (measured):</b> revert <c>RoutingOf</c>'s blank limb to the deleted copy's comparison —
/// <c>var pos = GstReportSupport.PlaceOfSupply(company, voucher); return pos is not null &amp;&amp; home is not null
/// &amp;&amp; !string.Equals(pos, home, StringComparison.Ordinal);</c> — and both blank-State cases below flip to
/// <see cref="EWayCoverage.Required"/> / <see cref="EWayCoverage.MandatoryIrrespectiveOfValue"/> and fail.</para>
///
/// <para>Odd to the paisa: a ₹59,374.80 consignment and a ₹5,900.44 job-work movement. A round ₹59,000 would prove
/// the same thing about the branch and nothing about the arithmetic beside it.</para>
/// </summary>
public sealed class EWayBlankPartyStateRoutingTests
{
    private const string GstinMaharashtra = "27AAPFU0939F1ZV";
    private const string HomeState = "27";
    private const string OtherState = "24";
    private static readonly DateOnly FyStart = new(2025, 4, 1);
    private static readonly DateOnly SaleDate = new(2025, 4, 10);

    /// <summary>₹50,317.63 @18% ⇒ ₹9,057.17 tax ⇒ a ₹59,374.80 consignment, well over Rule 138's flat ₹50,000.</summary>
    private const decimal BigTaxable = 50_317.63m;
    private const decimal BigCgst = 4_528.59m;
    private const decimal BigSgst = 4_528.58m;
    private const decimal BigIgst = 9_057.17m;

    /// <summary>₹5,000.37 @18% ⇒ ₹900.07 ⇒ a ₹5,900.44 movement, far UNDER the flat threshold — so only the
    /// job-work/handicraft short-circuit can ever cover it.</summary>
    private const decimal SmallTaxable = 5_000.37m;
    private const decimal SmallCgst = 450.04m;
    private const decimal SmallSgst = 450.03m;
    private const decimal SmallIgst = 900.07m;

    private sealed class Fx
    {
        public required Company Company { get; init; }
        public required EWayBillService Service { get; init; }
        public required Guid SalesTypeId { get; init; }
        public required Guid SalesLedgerId { get; init; }
        public required Guid GodownId { get; init; }
        public required Guid WidgetId { get; init; }
        public required Domain.Ledger Party { get; init; }
    }

    /// <summary>A GST + e-Way company that EXEMPTS intra-state movements — the relaxation the blank State reaches.</summary>
    private static Fx Build(string? partyStateCode)
    {
        var c = CompanyFactory.CreateSeeded("Blank-State e-Way Co", FyStart);
        var gst = new GstService(c);
        gst.EnableGst(new GstConfig
        {
            HomeStateCode = HomeState, Gstin = GstinMaharashtra, RegistrationType = GstRegistrationType.Regular,
            ApplicableFrom = FyStart, Periodicity = GstReturnPeriodicity.Monthly,
            EWayBillEnabled = true, EWayApplicableFrom = FyStart,
            EWayIntraStateApplicable = false,
        });

        var inv = new InventoryService(c);
        var widget = inv.CreateStockItem(
            "Widget", inv.CreateStockGroup("Goods").Id, inv.CreateSimpleUnit("Nos", "Numbers", unitQuantityCode: "NOS").Id);
        widget.Gst = new StockItemGstDetails { HsnSac = "847130", Taxability = GstTaxability.Taxable, RateBasisPoints = 1800 };

        var sales = Add(c, "Sales", "Sales Accounts", openingIsDebit: false);
        var party = Add(c, "Debtor", "Sundry Debtors", openingIsDebit: true);
        party.PartyGst = new PartyGstDetails
        { RegistrationType = GstRegistrationType.Consumer, StateCode = partyStateCode };

        return new Fx
        {
            Company = c, Service = new EWayBillService(c),
            SalesTypeId = c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Sales).Id,
            SalesLedgerId = sales.Id, GodownId = c.MainLocation!.Id, WidgetId = widget.Id, Party = party,
        };
    }

    private static Domain.Ledger Add(Company c, string name, string groupName, bool openingIsDebit)
    {
        var l = new Domain.Ledger(Guid.NewGuid(), name, c.FindGroupByName(groupName)!.Id, Money.Zero, openingIsDebit);
        c.AddLedger(l);
        return l;
    }

    /// <summary>A goods movement carrying explicit posted item + tax legs (the same shape <c>EWayValueTests</c> uses).</summary>
    private static Voucher Movement(Fx f, decimal taxable, IReadOnlyList<EntryLine> taxLines)
    {
        var totalTax = taxLines.Sum(l => l.Amount.Amount);
        var lines = new List<EntryLine>
        {
            new(f.Party.Id, new Money(taxable + totalTax), DrCr.Debit),
            new(f.SalesLedgerId, new Money(taxable), DrCr.Credit),
        };
        lines.AddRange(taxLines);
        return new Voucher(
            Guid.NewGuid(), f.SalesTypeId, SaleDate, lines, partyId: f.Party.Id,
            inventoryLines: new[] { new VoucherInventoryLine(f.WidgetId, f.GodownId, 1m, new Money(taxable)) });
    }

    private static EntryLine Tax(Fx f, GstTaxHead head, int rateBp, decimal taxable, decimal amount) =>
        new(f.SalesLedgerId, new Money(amount), DrCr.Credit,
            gst: new GstLineTax(head, rateBp, new Money(taxable), isReverseCharge: false));

    private static Voucher IntraBigMovement(Fx f) => Movement(f, BigTaxable, new[]
    {
        Tax(f, GstTaxHead.Central, 900, BigTaxable, BigCgst),
        Tax(f, GstTaxHead.State, 900, BigTaxable, BigSgst),
    });

    private static Voucher IntraSmallMovement(Fx f) => Movement(f, SmallTaxable, new[]
    {
        Tax(f, GstTaxHead.Central, 900, SmallTaxable, SmallCgst),
        Tax(f, GstTaxHead.State, 900, SmallTaxable, SmallSgst),
    });

    // ================================================================ the premise, measured not assumed

    /// <summary>
    /// The blank/whitespace party State really does travel a different rung in the two rules. <c>PlaceOfSupply</c> —
    /// which the deleted copy read — hands back the blank verbatim, while <c>RoutingOf</c> treats it as "not
    /// recorded". Without this, the two coverage tests below could pass for some unrelated reason.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void A_blank_party_state_is_not_recorded_for_routing_but_is_still_the_raw_ladders_answer(string blank)
    {
        var f = Build(blank);
        var v = IntraBigMovement(f);

        Assert.Equal(blank, GstReportSupport.PlaceOfSupply(f.Company, v));    // the raw ladder: the blank itself
        Assert.False(GstReportSupport.RoutingOf(f.Company, v));               // the shared rule: INTRA (s.10(1)(ca))
    }

    // ================================================================ the two coverage verdicts that moved

    /// <summary>
    /// A ₹59,374.80 movement to a blank-State party, in a company that exempts intra-state e-Way entirely.
    /// <b>NotRequired</b> — the s.10(1)(ca) place of supply is the supplier's own location, so the movement is
    /// intra-state and the exemption applies. The deleted copy read the blank as a DIFFERENT State and answered
    /// <b>Required</b>.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void A_blank_party_state_is_an_intra_state_movement_and_takes_the_intra_state_exemption(string blank)
    {
        var f = Build(blank);
        var v = IntraBigMovement(f);
        Assert.Equal(new Money(59_374.80m), f.Service.ConsignmentValue(v));   // over the flat ₹50,000 baseline
        Assert.Equal(EWayCoverage.NotRequired, f.Service.CoverageOf(v));
    }

    /// <summary>
    /// The same on the job-work short-circuit, which is a WIDENING that presupposes a known INTER-state movement: a
    /// ₹5,900.44 job-work movement to a blank-State party is <b>NotRequired</b> (intra-state, exempted, and far under
    /// the threshold besides). The deleted copy answered <b>MandatoryIrrespectiveOfValue</b>.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void A_blank_party_state_does_not_reach_the_inter_state_job_work_short_circuit(string blank)
    {
        var f = Build(blank);
        var v = IntraSmallMovement(f);
        Assert.Equal(new Money(5_900.44m), f.Service.ConsignmentValue(v));
        Assert.Equal(EWayCoverage.NotRequired, f.Service.CoverageOf(v, EWayTransactionType.JobWork));
    }

    // ================================================================ the control — a REAL out-of-State code

    /// <summary>
    /// The same two movements to a party with a real Gujarat code: <b>Required</b> and
    /// <b>MandatoryIrrespectiveOfValue</b>. This is what makes the two verdicts above a measurement of the blank-State
    /// branch rather than of the fixture — the branch the blank used to take is exercised here.
    /// </summary>
    [Fact]
    public void A_real_out_of_state_code_still_covers_both_movements()
    {
        var f = Build(OtherState);
        Assert.True(GstReportSupport.RoutingOf(f.Company, f.Party.PartyGst!.StateCode));

        var big = Movement(f, BigTaxable, new[] { Tax(f, GstTaxHead.Integrated, 1800, BigTaxable, BigIgst) });
        Assert.Equal(new Money(59_374.80m), f.Service.ConsignmentValue(big));
        Assert.Equal(EWayCoverage.Required, f.Service.CoverageOf(big));

        var small = Movement(f, SmallTaxable, new[] { Tax(f, GstTaxHead.Integrated, 1800, SmallTaxable, SmallIgst) });
        Assert.Equal(new Money(5_900.44m), f.Service.ConsignmentValue(small));
        Assert.Equal(EWayCoverage.NotRequired, f.Service.CoverageOf(small));                            // under ₹50,000
        Assert.Equal(EWayCoverage.MandatoryIrrespectiveOfValue,
            f.Service.CoverageOf(small, EWayTransactionType.JobWork));                                  // short-circuit
    }
}
