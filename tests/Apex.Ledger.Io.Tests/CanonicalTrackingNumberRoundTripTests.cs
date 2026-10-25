using System.Text;
using Apex.Ledger.Domain;
using Apex.Ledger.Reports;
using Apex.Ledger.Services;

namespace Apex.Ledger.Io.Tests;

/// <summary>
/// 🔴 <b>Census 9.8 (schema v58) — THE TRACKING NUMBER DID NOT REACH THE TRANSPORT AT ALL, AND NOTHING SAID
/// SO.</b> Measured 2026-10-23 on `origin/main` 8eea0c6: <c>grep -n racking</c> over
/// <c>CanonicalModel.cs</c>, <c>CanonicalMapper.cs</c> and <c>CanonicalXml.cs</c> returned only
/// <c>TrackingComponents</c> — a job-work BOM flag with nothing to do with this feature. Neither
/// <c>VoucherInventoryLineDto</c> nor <c>InventoryAllocationDto</c> carried <c>TrackingNumber</c> or
/// <c>CostTrackingNumber</c>, on either side of the wire.
///
/// <para><b>What that cost, stated as the artefact it damages.</b> The Tracking No. is the ONLY link between a
/// Receipt Note and the Purchase that bills it; <c>BillsPending</c> nets Received − Billed on that string and
/// the bill-side picker (IV-149) offers it. So exporting a company and re-importing it returned a book in
/// which <b>every bill-to-note link had been silently erased</b> and both Bills Pending reports came back
/// empty — not wrong-looking, not an error, just gone. The persisted column has existed since v58; the
/// transport never read it.</para>
///
/// <para>🔴 <b>IT WAS FOUND BY CHECKING, NOT BY A FAILURE.</b> No test in this project covered it: the whole
/// <c>Apex.Ledger.Io.Tests</c> suite was green on `main` with the datum being dropped on every export. That is
/// the measured failure mode this project calls <i>a green suite proves nothing</i>, and it is why this file
/// asserts on the EMITTED BYTES and on the book read back through <see cref="CompanyImportService"/> rather
/// than on a DTO property.</para>
///
/// <para><b>ER-13 is the sharp edge, exactly as it was for the line unit.</b> The canonical JSON writer is
/// configured <c>DefaultIgnoreCondition = Never</c>, so naively-added <c>string?</c> properties would emit
/// <c>"trackingNumber": null</c> on <i>every item line and every allocation of every existing company</i>.
/// <see cref="Er13_a_company_with_no_tracking_numbers_exports_byte_identically"/> pins that, with a
/// belt-and-braces positive so it cannot pass by the writer never emitting the field at all.</para>
/// </summary>
public sealed class CanonicalTrackingNumberRoundTripTests
{
    private static readonly DateOnly FyStart = new(2025, 4, 1);

    private const string NoteTracking = "RN/2025-26/0007";
    private const string BillTracking = "RN/2025-26/0007";
    private const string NoteCostTracking = "JOB-SITE-A";
    private const string BillCostTracking = "JOB-SITE-B";   // distinct from the note's, so a swap or a drop shows
    private const string BillNarration = "Purchase billing receipt note RN/2025-26/0007";

    /// <summary>
    /// A book holding BOTH sides of one tracking link: a Receipt Note whose allocation carries the Tracking No.
    /// (and a Cost Tracking Number), and the Purchase that bills it, whose item line carries the same Tracking
    /// No. Both sides are needed — a fix that carried only one would leave Bills Pending reporting the other
    /// side as permanently pending, which is <i>wrong</i> rather than merely empty.
    /// </summary>
    private static Company BuildCompanyWithATrackedReceiptAndItsBill()
    {
        var c = Seed("Tracked Traders");
        var widget = c.FindStockItemByName("Widget")!;
        var godown = c.MainLocation!.Id;

        var supplier = new Domain.Ledger(
            Guid.NewGuid(), "Acme Supplies", c.FindGroupByName("Sundry Creditors")!.Id, Money.Zero, false);
        var purchases = new Domain.Ledger(
            Guid.NewGuid(), "Purchases", c.FindGroupByName("Purchase Accounts")!.Id, Money.Zero, true);
        c.AddLedger(supplier);
        c.AddLedger(purchases);

        // ---- the goods side: a Receipt Note of 40 under the tracking number.
        new InventoryPostingService(c).Post(new InventoryVoucher(
            Guid.NewGuid(),
            c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.ReceiptNote).Id,
            new DateOnly(2025, 4, 5),
            new[]
            {
                new InventoryAllocation(widget.Id, godown, 40m, StockDirection.Inward,
                    Money.FromRupees(100m), null, null, NoteTracking, NoteCostTracking),
            },
            partyId: supplier.Id));

        // ---- the accounting side: the Purchase that bills 25 of them under the SAME number, leaving 15
        // pending. A part bill is used rather than a full one so the round-trip has a non-trivial figure to
        // preserve: a full bill would net to zero and an erased link would look the same as a cleared one.
        new LedgerService(c).Post(new Voucher(
            Guid.NewGuid(),
            c.VoucherTypes.Single(t => t.BaseType == VoucherBaseType.Purchase && t.IsActive).Id,
            new DateOnly(2025, 4, 9),
            new[]
            {
                new EntryLine(purchases.Id, Money.FromRupees(2500m), DrCr.Debit),
                new EntryLine(supplier.Id, Money.FromRupees(2500m), DrCr.Credit),
            },
            partyId: supplier.Id,
            narration: BillNarration,
            inventoryLines: new[]
            {
                new VoucherInventoryLine(widget.Id, godown, 25m, Money.FromRupees(100m),
                    StockDirection.Inward, null, null, null, BillTracking, BillCostTracking),
            }));

        return c;
    }

    private static Company Seed(string name)
    {
        var c = CompanyFactory.CreateSeeded(name, FyStart);
        c.UseTrackingNumbers = true;
        c.EnableCostTracking = true;
        var inv = new InventoryService(c);
        var grp = inv.CreateStockGroup("Goods");
        var nos = inv.CreateSimpleUnit("Nos", "Numbers");
        var widget = inv.CreateStockItem("Widget", grp.Id, nos.Id);
        inv.AddOpeningBalance(widget.Id, c.MainLocation!.Id, 500m, Money.FromRupees(100m));
        return c;
    }

    private static Company Fresh() => Seed("Fresh Tracked Co");

    // ================================================================= lossless round-trip

    /// <summary>
    /// <b>Mutation:</b> drop either <c>TrackingNumber = …</c> assignment from <c>CanonicalMapper</c> (or either
    /// <c>Opt("trackingNumber", …)</c> from <c>CanonicalXml</c>, or either argument from <c>ImportPlan</c>) and
    /// this reddens on the Bills Pending assertion below — which is the whole point of asserting the REPORT
    /// rather than the DTO: the report is what an operator loses.
    /// </summary>
    [Fact]
    public void Json_round_trips_byte_stable_and_the_bill_to_note_link_survives()
    {
        var c = BuildCompanyWithATrackedReceiptAndItsBill();
        var first = CanonicalJson.Export(c);

        var (model, errors) = CanonicalJson.Parse(first);
        Assert.Empty(errors);
        Assert.NotNull(model);
        Assert.Equal(first, CanonicalJson.Export(model!));   // byte-stable

        var target = Fresh();
        Assert.True(new CompanyImportService(target).Apply(model!, DuplicatePolicy.Skip).Applied);
        AssertTheLinkSurvived(target);
    }

    [Fact]
    public void Xml_round_trips_byte_stable_and_the_bill_to_note_link_survives()
    {
        var c = BuildCompanyWithATrackedReceiptAndItsBill();
        var first = CanonicalXml.Export(c);

        var (model, errors) = CanonicalXml.Parse(first);
        Assert.Empty(errors);
        Assert.NotNull(model);
        Assert.Equal(first, CanonicalXml.Export(model!));    // byte-stable

        var target = Fresh();
        Assert.True(new CompanyImportService(target).Apply(model!, DuplicatePolicy.Skip).Applied);
        AssertTheLinkSurvived(target);
    }

    /// <summary>
    /// 🔴 <b>THE OBSERVABLE ARTEFACT, AND IT IS THE REPORT.</b> Both tracking strings are read off the IMPORTED
    /// book's own objects, and then <see cref="Report.BuildPurchaseBillsPending"/> is run against that book:
    /// 40 received − 25 billed must still leave <b>15 pending under the same number</b>. A transport that
    /// carried the note's number but not the bill's would still produce a row here — for 40, not 15 — so the
    /// quantity is asserted and not merely the row's existence.
    /// </summary>
    private static void AssertTheLinkSurvived(Company target)
    {
        var note = Assert.Single(target.InventoryVouchers);
        var alloc = Assert.Single(note.Allocations);
        Assert.Equal(NoteTracking, alloc.TrackingNumber);
        Assert.Equal(NoteCostTracking, alloc.CostTrackingNumber);

        var bill = Assert.Single(target.Vouchers, v => v.Narration == BillNarration);
        var billLine = Assert.Single(bill.InventoryLines);
        Assert.Equal(BillTracking, billLine.TrackingNumber);
        // Wave-6 review: the BILL line's Cost Tracking Number was carried by the fix but asserted nowhere --
        // dropping `CostTrackingNumber = l.CostTrackingNumber` from CanonicalMapper's item-line arm left all 864
        // Io tests green. It is the bill half of census 9.7's pair and must survive the transport too.
        Assert.Equal(BillCostTracking, billLine.CostTrackingNumber);

        var pending = Report.BuildPurchaseBillsPending(target, new DateOnly(2026, 3, 31));
        var row = Assert.Single(pending, r => r.TrackingNumber == NoteTracking);
        Assert.Equal(BillsPendingSection.GoodsNotBilled, row.Section);
        Assert.Equal(40m, row.ReceivedQuantity);
        Assert.Equal(25m, row.BilledQuantity);
        Assert.Equal(15m, row.PendingQuantity);
    }

    // ================================================================= ER-13

    /// <summary>
    /// A company that never used tracking numbers must serialise EXACTLY as it did before this fix — no
    /// <c>trackingNumber</c> / <c>costTrackingNumber</c> key (JSON) or attribute (XML) anywhere.
    /// </summary>
    [Fact]
    public void Er13_a_company_with_no_tracking_numbers_exports_byte_identically()
    {
        var c = Seed("Untracked Co");
        var widget = c.FindStockItemByName("Widget")!;
        var supplier = new Domain.Ledger(
            Guid.NewGuid(), "Plain Supplies", c.FindGroupByName("Sundry Creditors")!.Id, Money.Zero, false);
        var purchases = new Domain.Ledger(
            Guid.NewGuid(), "Purchases", c.FindGroupByName("Purchase Accounts")!.Id, Money.Zero, true);
        c.AddLedger(supplier);
        c.AddLedger(purchases);

        new InventoryPostingService(c).Post(new InventoryVoucher(
            Guid.NewGuid(),
            c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.ReceiptNote).Id,
            new DateOnly(2025, 4, 5),
            new[] { new InventoryAllocation(widget.Id, c.MainLocation!.Id, 10m, StockDirection.Inward) },
            partyId: supplier.Id));

        new LedgerService(c).Post(new Voucher(
            Guid.NewGuid(),
            c.VoucherTypes.Single(t => t.BaseType == VoucherBaseType.Purchase && t.IsActive).Id,
            new DateOnly(2025, 4, 9),
            new[]
            {
                new EntryLine(purchases.Id, Money.FromRupees(1000m), DrCr.Debit),
                new EntryLine(supplier.Id, Money.FromRupees(1000m), DrCr.Credit),
            },
            partyId: supplier.Id,
            inventoryLines: new[]
            {
                new VoucherInventoryLine(widget.Id, c.MainLocation!.Id, 10m, Money.FromRupees(100m),
                    StockDirection.Inward),
            }));

        var json = Encoding.UTF8.GetString(CanonicalJson.Export(c));
        var xml = Encoding.UTF8.GetString(CanonicalXml.Export(c));

        Assert.DoesNotContain("trackingNumber", json, StringComparison.Ordinal);
        Assert.DoesNotContain("trackingNumber", xml, StringComparison.Ordinal);
        Assert.DoesNotContain("costTrackingNumber", json, StringComparison.Ordinal);
        Assert.DoesNotContain("costTrackingNumber", xml, StringComparison.Ordinal);

        // 🔴 Belt-and-braces, because an absence test that can never see a presence is a test that cannot
        // fail: a company that DOES carry the numbers must emit all four spellings.
        var tracked = BuildCompanyWithATrackedReceiptAndItsBill();
        var trackedJson = Encoding.UTF8.GetString(CanonicalJson.Export(tracked));
        var trackedXml = Encoding.UTF8.GetString(CanonicalXml.Export(tracked));
        Assert.Contains("\"trackingNumber\": \"" + NoteTracking + "\"", trackedJson, StringComparison.Ordinal);
        Assert.Contains("\"costTrackingNumber\": \"" + NoteCostTracking + "\"", trackedJson,
            StringComparison.Ordinal);
        Assert.Contains("trackingNumber=\"" + NoteTracking + "\"", trackedXml, StringComparison.Ordinal);
        Assert.Contains("costTrackingNumber=\"" + NoteCostTracking + "\"", trackedXml, StringComparison.Ordinal);
    }
}
