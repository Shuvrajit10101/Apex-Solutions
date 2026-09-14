using Apex.Ledger.Domain;
using Apex.Ledger.Reports;
using Apex.Ledger.Services;
using Xunit;

namespace Apex.Ledger.Tests;

/// <summary>
/// Census row <b>10.2 (Multi Address)</b> — the <b>guard</b> that has to exist BEFORE a shipping address does.
///
/// <para>🔴 <b>THIS FILE EXISTS BECAUSE THIS REPOSITORY'S OWN DEFECT REGISTER STATES THE OPPOSITE OF THE GROUND
/// TRUTH, AND A SLICE ALMOST BUILT THE WRONG RULE FROM IT.</b> Register entry <b>T1-30</b> asserts that "under GST
/// the place of supply for goods follows the ship-to address, so bill-to ≠ ship-to is precisely the case that
/// decides CGST+SGST versus IGST", and concludes that Multi Address is a GST-correctness dependency. The vendor
/// documents the opposite for its own product, verbatim: "<i>GST calculation depends only on the location of the
/// buyer, and not the consignee</i>"
/// (<c>help.tallysolutions.com/docs/te9rel62/Tax_India/gst/special_cases_in_gst_sales.htm</c>, "Consignee Sales").
/// Under ruling 14 the vendor documentation is the fidelity ground truth for product behaviour, so <b>the buyer's
/// location drives the computation and a consignee does not move the tax</b>. T1-30 is reported as a defect in the
/// register, not acted on.</para>
///
/// <para><b>What this file pins, and what it honestly does not.</b> It does NOT test a shipping address — there
/// isn't one yet; Multi Address needs storage this track had no schema allocation for. It pins the PROPERTY that
/// the future feature must not break: the tax head on a voucher is decided by the <b>billed party</b> and by
/// nothing else on the voucher. A Multi Address slice that lets a selected ship-to address feed
/// <c>RoutingOf</c> — or that adds a second, independently-stored party State — fails here rather than in a filed
/// return. These tests therefore PASS on today's main by construction; that is the point of a guard.</para>
/// </summary>
public class BuyerNotConsigneeGstRoutingTests
{
    private static readonly DateOnly FyStart = new(2025, 4, 1);
    private static readonly DateOnly SaleDate = new(2025, 4, 10);

    private const string Karnataka = "29";   // the supplier's own State
    private const string TamilNadu = "33";   // the consignee's State — goods physically go here
    private const string Kerala = "32";

    private static string GstinFor(string stateCode, string pan)
    {
        var body = stateCode + pan + "1Z";
        return body + Gstin.ComputeCheckDigit(body + "0");
    }

    private sealed class Fixture
    {
        public required Company Company { get; init; }
        public required Domain.Ledger Buyer { get; init; }
        public required Domain.Ledger Consignee { get; init; }
        public required Voucher Sale { get; init; }
    }

    /// <summary>
    /// A Karnataka supplier sells to a <b>Karnataka buyer</b> while a <b>Tamil Nadu consignee</b> ledger also
    /// exists on the book. The supply is intra-State on the buyer's location, so it bears CGST + SGST. If anything
    /// ever routed off the consignee instead, this same invoice would bear IGST — a different tax head on a filed
    /// return and on the printed document.
    /// </summary>
    private static Fixture Build(string buyerState)
    {
        var c = CompanyFactory.CreateSeeded("Consignee Sales Co", FyStart);
        var gst = new GstService(c);
        gst.EnableGst(new GstConfig
        {
            HomeStateCode = Karnataka,
            Gstin = GstinFor(Karnataka, "AAPFU0939F"),
            RegistrationType = GstRegistrationType.Regular,
            ApplicableFrom = FyStart,
            Periodicity = GstReturnPeriodicity.Monthly,
        });

        var ledgers = new LedgerService(c);
        var sales = Add(c, "Sales", "Sales Accounts", false);

        var buyer = Add(c, "Buyer (bill to)", "Sundry Debtors", true);
        buyer.PartyGst = new PartyGstDetails
        {
            RegistrationType = GstRegistrationType.Regular,
            Gstin = GstinFor(buyerState, "AAACC1206D"),
            StateCode = buyerState,
        };

        var consignee = Add(c, "Consignee (ship to)", "Sundry Debtors", true);
        consignee.PartyGst = new PartyGstDetails
        {
            RegistrationType = GstRegistrationType.Regular,
            Gstin = GstinFor(TamilNadu, "AABCC1206D"),
            StateCode = TamilNadu,
        };

        var interState = GstReportSupport.RoutingOf(c, buyerState) ?? false;
        var tax = gst.ComputeInvoiceTax(
            new[] { new GstService.TaxableLine(Money.FromRupees(10_000m), 1800) }, interState, GstTaxDirection.Output);
        var lines = new List<EntryLine>
        {
            new(buyer.Id, Money.FromRupees(11_800m), DrCr.Debit),
            new(sales.Id, Money.FromRupees(10_000m), DrCr.Credit),
        };
        lines.AddRange(tax.TaxLines);
        var sale = new Voucher(
            Guid.NewGuid(), c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Sales).Id,
            SaleDate, lines, partyId: buyer.Id);
        ledgers.Post(sale);

        return new Fixture { Company = c, Buyer = buyer, Consignee = consignee, Sale = sale };
    }

    private static Domain.Ledger Add(Company c, string name, string groupName, bool openingIsDebit)
    {
        var l = new Domain.Ledger(Guid.NewGuid(), name, c.FindGroupByName(groupName)!.Id, Money.Zero, openingIsDebit);
        c.AddLedger(l);
        return l;
    }

    [Fact]
    public void The_tax_head_follows_the_buyer_even_when_a_consignee_in_another_state_exists()
    {
        var f = Build(buyerState: Karnataka);

        // Buyer in the supplier's own State ⇒ CGST + SGST, notwithstanding a Tamil Nadu consignee on the book.
        Assert.False(GstReportSupport.RoutingOf(f.Company, f.Sale));
        var heads = f.Sale.Lines.Where(l => l.Gst is not null).Select(l => l.Gst!.TaxHead).ToList();
        Assert.Contains(GstTaxHead.Central, heads);
        Assert.Contains(GstTaxHead.State, heads);
        Assert.DoesNotContain(GstTaxHead.Integrated, heads);
    }

    [Fact]
    public void A_buyer_in_another_state_makes_it_integrated_tax_on_the_buyer_location_alone()
    {
        var f = Build(buyerState: Kerala);

        Assert.True(GstReportSupport.RoutingOf(f.Company, f.Sale));
        var heads = f.Sale.Lines.Where(l => l.Gst is not null).Select(l => l.Gst!.TaxHead).ToList();
        Assert.Contains(GstTaxHead.Integrated, heads);
        Assert.DoesNotContain(GstTaxHead.Central, heads);
    }

    [Fact]
    public void Changing_the_consignee_state_does_not_move_the_tax_and_this_is_the_property_multi_address_must_keep()
    {
        // 🔴 THE ONE THAT MATTERS. Select a "different shipping address" the only way this build can express one —
        // a consignee in a different State — and the computed routing and the printed place of supply must not
        // budge. A Multi Address slice that wires a selected ship-to into the routing breaks exactly this.
        var f = Build(buyerState: Karnataka);

        var routingBefore = GstReportSupport.RoutingOf(f.Company, f.Sale);
        var posBefore = GstReportSupport.PlaceOfSupply(f.Company, f.Sale);
        var issuedBefore = GstReportSupport.IssuedPlaceOfSupply(f.Company, f.Sale);

        foreach (var state in new[] { Kerala, TamilNadu, Karnataka, "07" })
        {
            f.Consignee.PartyGst!.StateCode = state;
            f.Consignee.PartyGst!.Gstin = GstinFor(state, "AABCC1206D");

            Assert.Equal(routingBefore, GstReportSupport.RoutingOf(f.Company, f.Sale));
            Assert.Equal(posBefore, GstReportSupport.PlaceOfSupply(f.Company, f.Sale));
            Assert.Equal(issuedBefore, GstReportSupport.IssuedPlaceOfSupply(f.Company, f.Sale));
        }
    }

    [Fact]
    public void The_party_has_exactly_one_stored_state_so_a_mailing_address_cannot_contradict_the_tax()
    {
        // The mailing block deliberately has NO State column of its own: Ledger.MailingStateCode delegates to the
        // party GST State, so there is one stored value and the mailing State and the place-of-supply State cannot
        // diverge. Multi Address adds ADDRESSES, and this pins that it must not also add a second party State —
        // which is precisely how a shipping address would start silently moving the tax.
        var f = Build(buyerState: Karnataka);

        Assert.Equal(Karnataka, f.Buyer.MailingStateCode);
        Assert.Equal(f.Buyer.PartyGst!.StateCode, f.Buyer.MailingStateCode);

        f.Buyer.MailingStateCode = Kerala;
        Assert.Equal(Kerala, f.Buyer.PartyGst!.StateCode);
        Assert.True(GstReportSupport.RoutingOf(f.Company, Kerala));

        // Clearing it never fabricates a second block.
        f.Buyer.MailingStateCode = null;
        Assert.Null(f.Buyer.PartyGst!.StateCode);
        Assert.Null(f.Buyer.MailingStateCode);
    }
}
