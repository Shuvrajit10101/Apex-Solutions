using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using Apex.Ledger.Domain;
using Apex.Ledger.Io;
using Apex.Ledger.Reports;
using Apex.Ledger.Services;
using Xunit;

namespace Apex.Ledger.Tests;

/// <summary>
/// <b>The D7 absent-HSN sentinels, pinned at the CONSUMERS.</b> The resolution ORDER is unified
/// (<see cref="GstReportSupport.HsnSacOf"/>); the way each consumer spells the ABSENCE is deliberately different,
/// and that divergence is the single thing about D7 most exposed to a later "consistency" tidy-up.
///
/// <para><b>Why this class exists — the test it replaces asserted nothing.</b> The original guard bound
/// <c>var absent = GstReportSupport.HsnSacOf(NewItem());</c> — already known to be null — and then asserted
/// <c>Assert.Equal("(none)", absent ?? "(none)")</c>, <c>Assert.Equal("", absent ?? "")</c> and
/// <c>Assert.Equal(string.Empty, absent ?? string.Empty)</c>. Every one of those is <c>x == (null ?? x)</c>: true
/// for ANY x, true even if every consumer's sentinel were changed, and true without calling a single consumer.
/// The three assertions were also two distinct claims, one of them written twice. Nothing in the repository
/// asserted that the four sentinels differ.</para>
///
/// <para><b>What that left unprotected.</b> <c>Gstr1.AddHsnRow</c> spells absence <c>"(none)"</c> five files away
/// from <c>EInvoiceJson</c>'s <c>""</c>. "Unifying" them looks like an obvious cleanup — and changing
/// <c>EInvoiceJson</c>'s operand to <c>"(none)"</c> would file the literal text <c>(none)</c> into the NIC
/// <c>HsnCd</c> field of a live e-invoice: a malformed statutory submission, with the whole suite green. The D7
/// drift lock does not help; it bans re-deriving the resolution ORDER, not the sentinels.</para>
///
/// <para>So each assertion below drives the REAL consumer over a posted voucher whose stock item declares neither
/// <c>Gst.HsnSac</c> nor the legacy <c>HsnSacCode</c>, and pins that consumer's own spelling of absence. Swapping
/// any one consumer's <c>??</c> operand for another's fails exactly the assertion that names it. The fourth
/// consumer, <c>VoucherPrintProjector</c>, lives in Apex.Desktop and is pinned by the twin of this class in
/// <c>tests/Apex.Desktop.Tests/AbsentHsnPrintedInvoiceSentinelTests.cs</c>.</para>
///
/// <para>The consignment is ₹2,04,317.63 — ODD to the paisa, and over the ₹50,000 Rule-138 threshold so the e-Way
/// payload is genuinely producible.</para>
/// </summary>
public sealed class AbsentHsnSentinelsPerConsumerTests
{
    private const string GstinHome = "27AAPFU0939F1ZV";
    private static readonly DateOnly FyStart = new(2025, 4, 1);
    private static readonly DateOnly SaleDate = new(2025, 4, 10);

    /// <summary>₹2,04,317.63 — odd to the paisa, and above the Rule-138 threshold.</summary>
    private const decimal Taxable = 2_04_317.63m;

    private sealed class Fx
    {
        public required Company Company { get; init; }
        public required Voucher Sale { get; init; }
    }

    private static Domain.Ledger Add(Company c, string name, string groupName, bool openingIsDebit)
    {
        var l = new Domain.Ledger(Guid.NewGuid(), name, c.FindGroupByName(groupName)!.Id, Money.Zero, openingIsDebit);
        c.AddLedger(l);
        return l;
    }

    /// <summary>
    /// A GST company whose only stock item declares NO HSN by either route — the GST block carries a rate and a
    /// taxability but no <c>HsnSac</c>, and the legacy Phase-3 <c>HsnSacCode</c> is left unset — so
    /// <see cref="GstReportSupport.HsnSacOf"/> resolves to <c>null</c> and every consumer must reach for its own
    /// sentinel. The sale is posted through <see cref="LedgerService.Post"/>, so a shape the application could not
    /// actually create would fail here rather than pass.
    /// </summary>
    private static Fx Build()
    {
        var c = CompanyFactory.CreateSeeded("Absent-HSN Co", FyStart);
        var gst = new GstService(c);
        gst.EnableGst(new GstConfig
        {
            HomeStateCode = "27", Gstin = GstinHome, RegistrationType = GstRegistrationType.Regular,
            ApplicableFrom = FyStart, Periodicity = GstReturnPeriodicity.Monthly,
            EInvoicingEnabled = true, EInvoiceApplicableFrom = FyStart,
            EWayBillEnabled = true, EWayApplicableFrom = FyStart, EWayIntraStateApplicable = true,
        });

        var inv = new InventoryService(c);
        var grp = inv.CreateStockGroup("Goods");
        var nos = inv.CreateSimpleUnit("Nos", "Numbers", unitQuantityCode: "NOS");
        var widget = inv.CreateStockItem("Widget", grp.Id, nos.Id);
        // Taxable and rated, but NO HsnSac — and HsnSacCode is deliberately left null.
        widget.Gst = new StockItemGstDetails { Taxability = GstTaxability.Taxable, RateBasisPoints = 1800 };
        Assert.Null(GstReportSupport.HsnSacOf(widget)); // the fixture's premise, asserted rather than assumed
        inv.AddOpeningBalance(widget.Id, c.MainLocation!.Id, 100m, Money.FromRupees(500m));

        var sales = Add(c, "Sales", "Sales Accounts", false);
        var party = Add(c, "Local Debtor", "Sundry Debtors", true);
        party.PartyGst = new PartyGstDetails
        { RegistrationType = GstRegistrationType.Regular, Gstin = GstinHome, StateCode = "27" };

        var tax = gst.ComputeInvoiceTax(
            new[] { new GstService.TaxableLine(Money.FromRupees(Taxable), 1800, null) },
            interState: false, GstTaxDirection.Output);

        var legs = new List<EntryLine>
        {
            new(party.Id, new Money(Taxable + tax.TotalTax.Amount + tax.TotalCess.Amount), DrCr.Debit),
            new(sales.Id, Money.FromRupees(Taxable), DrCr.Credit),
        };
        legs.AddRange(tax.TaxLines);

        var post = new LedgerService(c);
        var salesType = c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Sales).Id;
        // Quantity 1 at the full rate keeps the item-invoice pairing invariant (Σ qty × rate == the accounting leg).
        var sale = post.Post(new Voucher(
            Guid.NewGuid(), salesType, SaleDate, legs, partyId: party.Id,
            inventoryLines: new[]
            {
                new VoucherInventoryLine(widget.Id, c.MainLocation!.Id, 1m, Money.FromRupees(Taxable)),
            }));

        return new Fx { Company = c, Sale = sale };
    }

    // ============================================================ consumer 1 — the GSTR-1 HSN summary

    /// <summary>
    /// The GSTR-1 Table-12 summary buckets by HSN for a HUMAN-read report, so the unclassified bucket is labelled
    /// <c>(none)</c> — a blank row key would read as a rendering fault. This fails if
    /// <c>Gstr1.AddHsnRow</c>'s operand is changed to the NIC consumers' <c>""</c>.
    /// </summary>
    [Fact]
    public void TheGstr1HsnSummaryLabelsTheUnclassifiedBucketNone()
    {
        var f = Build();

        var gstr1 = Gstr1.Build(f.Company, FyStart, FyStart.AddMonths(1));
        var row = Assert.Single(gstr1.HsnSummary);

        Assert.Equal("(none)", row.HsnSac);
        Assert.NotEqual(string.Empty, row.HsnSac);
        Assert.Equal(Money.FromRupees(Taxable), row.TaxableValue); // the row is the real posted line, not a stub
    }

    // ============================================================ consumer 2 — the NIC INV-01 e-invoice

    /// <summary>
    /// The NIC INV-01 schema types <c>HsnCd</c> as a string and the department's convention for "not declared" is
    /// the EMPTY string. Filing the literal text <c>(none)</c> into a statutory code field would be a malformed
    /// submission, so this pins the empty string exactly and additionally refuses any spelling of "none".
    /// </summary>
    [Fact]
    public void TheEInvoicePayloadFilesTheEmptyStringNotTheReportsLabel()
    {
        var f = Build();

        using var doc = JsonDocument.Parse(EInvoiceJson.BuildInv01(f.Company, f.Sale));
        var hsn = doc.RootElement.GetProperty("ItemList")[0].GetProperty("HsnCd").GetString();

        Assert.Equal(string.Empty, hsn);
        Assert.DoesNotContain("none", hsn, StringComparison.OrdinalIgnoreCase);
    }

    // ============================================================ consumer 3 — the NIC EWB-01 e-Way Bill

    /// <summary>
    /// EWB-01 files the same NIC convention as INV-01 — the empty string, never the report's bucket label.
    /// </summary>
    [Fact]
    public void TheEWayBillPayloadFilesTheEmptyStringNotTheReportsLabel()
    {
        var f = Build();

        var service = new EWayBillService(f.Company);
        var record = service.PrepareRecord(f.Sale, f.Sale.Date);
        service.SetPartB(record, "TRANSIN01", EWayTransportMode.Road, "MH12AB1234", 250);

        using var doc = JsonDocument.Parse(
            Encoding.UTF8.GetString(EWayBillJson.BuildEwb01(f.Company, f.Sale, record)));
        var hsn = doc.RootElement.GetProperty("itemList")[0].GetProperty("HsnCd").GetString();

        Assert.Equal(string.Empty, hsn);
        Assert.DoesNotContain("none", hsn, StringComparison.OrdinalIgnoreCase);
    }

    // ============================================================ the sentinels are DIFFERENT, on purpose

    /// <summary>
    /// The point of the whole class, stated as one assertion: the human-read report and the two statutory payloads
    /// resolve the SAME absence and spell it DIFFERENTLY. If a future "cleanup" unifies the sentinel, whichever
    /// direction it goes, this fails.
    /// </summary>
    [Fact]
    public void TheReportLabelAndTheStatutorySentinelAreDeliberatelyNotTheSame()
    {
        var f = Build();

        var reportLabel = Assert.Single(Gstr1.Build(f.Company, FyStart, FyStart.AddMonths(1)).HsnSummary).HsnSac;

        using var inv01 = JsonDocument.Parse(EInvoiceJson.BuildInv01(f.Company, f.Sale));
        var nicSentinel = inv01.RootElement.GetProperty("ItemList")[0].GetProperty("HsnCd").GetString();

        Assert.NotEqual(reportLabel, nicSentinel);
        Assert.Equal("(none)", reportLabel);
        Assert.Equal(string.Empty, nicSentinel);
    }
}
