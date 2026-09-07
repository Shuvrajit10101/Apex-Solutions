using Apex.Ledger.Domain;

namespace Apex.Ledger.Reports;

/// <summary>
/// Which side of a <see cref="BillsPending"/> report a row belongs to (census 9.8). The vendor names both
/// sections verbatim on its Purchase Bills Pending page.
/// </summary>
public enum BillsPendingSection
{
    /// <summary>The vendor's <b>"Goods Recd. but Bills not Recd."</b> — a Receipt/Delivery Note moved goods
    /// under a Tracking No. that no bill has yet accounted for in full.</summary>
    GoodsNotBilled = 0,

    /// <summary>The vendor's <b>"Bills Recd. but Goods not Recd."</b> — a bill quotes a Tracking No. for a
    /// quantity larger than the goods actually moved under it.</summary>
    BilledNotReceived = 1,
}

/// <summary>
/// One pending line in a <see cref="BillsPending"/> report (census 9.8): a single (Tracking No., stock item)
/// pair whose goods movement and bill do not agree, together with the shortfall.
/// </summary>
/// <param name="Section">Which of the vendor's two sections this row belongs to.</param>
/// <param name="TrackingNumber">The operator-entered Tracking No. that joins the two sides.</param>
/// <param name="StockItemId">The item whose quantities disagree.</param>
/// <param name="ItemName">The item's display name.</param>
/// <param name="PartyId">The party on the earliest voucher that carries this tracking number, when there is
/// one. A tracking number is free text and may in principle appear on vouchers for two different parties;
/// the report does not silently merge them into one party — it reports the first and nothing else.</param>
/// <param name="PartyName">That party's display name, or <c>null</c>.</param>
/// <param name="Date">The date of the earliest voucher carrying this tracking number on the section's own
/// side — the date an operator ages the pending item from.</param>
/// <param name="ReceivedQuantity">Total quantity MOVED under this tracking number, in the item's base unit.</param>
/// <param name="BilledQuantity">Total quantity BILLED under this tracking number, in the item's base unit.</param>
/// <param name="PendingQuantity">The shortfall — always &gt; 0, and always the section's own direction.</param>
public sealed record BillsPendingRow(
    BillsPendingSection Section,
    string TrackingNumber,
    Guid StockItemId,
    string ItemName,
    Guid? PartyId,
    string? PartyName,
    DateOnly Date,
    decimal ReceivedQuantity,
    decimal BilledQuantity,
    decimal PendingQuantity);

/// <summary>
/// The <b>Purchase Bills Pending</b> and <b>Sales Bills Pending</b> reports (census 9.8) — the two ends of the
/// goods-vs-bill reconciliation that TRACKING NUMBERS exist to make visible.
/// </summary>
/// <remarks>
/// <para>🔴 <b>WHY THIS REPORT IS THE POINT OF THE FEATURE.</b> A Receipt Note and the Purchase invoice that
/// pays for it are two different vouchers in two different tables — the note's movement lives on
/// <see cref="InventoryVoucher.Allocations"/>, the bill's on <see cref="Voucher.InventoryLines"/>. Before
/// tracking numbers this product could only <i>infer</i> which bill paid for which goods, by a FIFO walk over
/// candidate movements. A FIFO guess cannot answer the question an operator actually asks — "which of my
/// received goods am I still waiting on a bill for?" — because it cannot distinguish "these 40 units were
/// billed" from "40 units happened to be billed". The operator-entered
/// <see cref="InventoryAllocation.TrackingNumber"/> answers it exactly.</para>
///
/// <para>🔴 <b>THE CORRECTNESS PROPERTY, STATED SO IT CAN BE TESTED.</b> For one (tracking number, item):
/// <list type="number">
/// <item>Goods received, no bill ⇒ the full received quantity appears once under
/// <see cref="BillsPendingSection.GoodsNotBilled"/>.</item>
/// <item>Goods received, bill for PART of them ⇒ only the UNBILLED REMAINDER appears — the report must not
/// keep reporting the whole receipt after a part-bill arrives.</item>
/// <item>Goods received, bill for all of them ⇒ NOTHING appears. <b>This is the double-count case</b>, and it
/// is the one a naive implementation gets wrong by listing the receipt and the bill as two separate pending
/// items.</item>
/// <item>Bill first, goods not yet in ⇒ the quantity appears once under
/// <see cref="BillsPendingSection.BilledNotReceived"/>, and vanishes when the goods arrive.</item>
/// </list>
/// Netting the two totals per (tracking number, item) — rather than listing movements and bills separately —
/// is what makes all four hold at once.</para>
///
/// <para><b>Quantities are netted in the item's BASE unit.</b> Both sides may state a line in any unit
/// (<see cref="InventoryAllocation.UnitId"/> / <see cref="VoucherInventoryLine.UnitId"/>), so a Receipt Note in
/// dozens and a Purchase bill in pieces would otherwise cancel 12:1 against each other and report a fictitious
/// shortfall. Every quantity is converted with <see cref="Unit.QuantityInBaseMeasure"/> before it is netted.
/// No money is netted at all: a bill's rate may legitimately differ from the note's estimate, so this report
/// deals only in quantity, which is what the vendor's own two section captions ask about.</para>
///
/// <para><b>The BILLED quantity, not the Actual.</b> On the bill side the netting uses
/// <see cref="VoucherInventoryLine.BilledQuantity"/>, because the question is what has been BILLED. A
/// free-goods line billed at zero quantity therefore leaves its received goods pending, which is correct: the
/// goods are in and no bill accounts for them.</para>
///
/// <para><b>The bill side uses the SHARED voucher filter</b> (<see cref="LedgerBalances.CountsAsOf"/>): cancelled
/// and <b>optional</b> vouchers are excluded and a post-dated one counts only once its date has arrived, so this
/// report can never disagree with the balances about which bills exist. The goods side excludes cancelled notes
/// and not-yet-due post-dated ones — an inventory voucher has no Optional flag. A row whose two sides agree
/// exactly is omitted from both sections rather than listed with a zero.</para>
///
/// <para><b>R7 — ATTESTED.</b> <c>help.tallysolutions.com/purchase-order-tally/</c> names the report
/// <b>"Purchase Bills Pending"</b> and its two sections verbatim as <i>"Goods Recd. but Bills not Recd.:"</i>
/// and <i>"Bills Recd. but Goods not Recd.:"</i>, and describes the Tracking No. field on the Stock Item
/// Allocations screen (<i>"Enter a Tracking No. By default, the invoice number appears"</i>).
/// <c>help.tallysolutions.com/sales-order-tally/</c> is the Delivery Note ↔ Sales counterpart. The vendor's own
/// summary of the mechanism — <i>"A Tracking number is the reference to have a link between transactions"</i> —
/// is what the string match here implements.</para>
///
/// <para>A <b>pure</b> projection: no UI, no DB, no clock.</para>
/// </remarks>
public static class BillsPending
{
    /// <summary>
    /// <b>Purchase Bills Pending</b> — Receipt Notes netted against Purchase item-invoices by Tracking No.,
    /// over vouchers dated on/before <paramref name="asOf"/>.
    /// </summary>
    public static IReadOnlyList<BillsPendingRow> BuildPurchase(Company company, DateOnly asOf)
        => Build(company, asOf, VoucherBaseType.ReceiptNote, VoucherBaseType.Purchase);

    /// <summary>
    /// <b>Sales Bills Pending</b> — Delivery Notes netted against Sales item-invoices by Tracking No., over
    /// vouchers dated on/before <paramref name="asOf"/>. Exactly the same netting as
    /// <see cref="BuildPurchase"/> with the goods side and the bill side swapped for their outward twins.
    /// </summary>
    public static IReadOnlyList<BillsPendingRow> BuildSales(Company company, DateOnly asOf)
        => Build(company, asOf, VoucherBaseType.DeliveryNote, VoucherBaseType.Sales);

    /// <summary>The accumulator for one (tracking number, stock item) pair.</summary>
    private sealed class Pair
    {
        public decimal Received;
        public decimal Billed;
        public DateOnly? GoodsDate;
        public DateOnly? BillDate;
        public Guid? PartyId;
        public DateOnly? PartyDate;
    }

    private static IReadOnlyList<BillsPendingRow> Build(
        Company company, DateOnly asOf, VoucherBaseType notesType, VoucherBaseType billType)
    {
        ArgumentNullException.ThrowIfNull(company);

        // 🔴 The feature gate is honoured HERE rather than by the caller. With "Use tracking numbers" off the
        // operator has no way to key a tracking number, so any value present is stale data from a period when
        // the flag was on; reporting it would show pending items the operator cannot act on or clear.
        if (!company.UseTrackingNumbers) return Array.Empty<BillsPendingRow>();

        // Keyed by (tracking number, item). The tracking-number comparison is ORDINAL and case-sensitive:
        // it is an operator-typed document reference, and folding case would silently merge two genuinely
        // distinct references ("A/1" vs "a/1") — the opposite failure to the one this report exists to prevent.
        var pairs = new Dictionary<(string Tracking, Guid Item), Pair>();

        // ── The GOODS side: pure-inventory note vouchers. ────────────────────────────────────────────────
        foreach (var v in company.InventoryVouchers)
        {
            // An InventoryVoucher has no Optional flag, so Cancelled + the date bound is the whole rule here.
            if (v.Cancelled || v.Date > asOf) continue;
            var type = company.FindVoucherType(v.TypeId);
            if (type is null || type.BaseType != notesType) continue;

            foreach (var a in v.Allocations)
            {
                if (a.TrackingNumber is not { } tracking) continue;
                var p = Slot(pairs, tracking, a.StockItemId);
                p.Received += BaseQuantity(company, a.StockItemId, a.UnitId, a.Quantity);
                if (p.GoodsDate is null || v.Date < p.GoodsDate) p.GoodsDate = v.Date;
                NotePartyIfEarliest(p, v.Date, v.PartyId);
            }
        }

        // ── The BILL side: item-invoice lines on an accounting voucher. ─────────────────────────────────
        // 🔴 The SHARED voucher filter, not a hand-rolled Cancelled check. An OPTIONAL voucher is a memo —
        // it posts nothing — so an optional Purchase must not discharge a real receipt: doing so would clear a
        // pending item off the report on the strength of a document the books do not recognise, which is the
        // worst failure this report has (a WRONG answer rather than an empty one). CountsAsOf also carries the
        // not-yet-due post-dated rule, so a bill dated next month cannot reconcile this month's goods either.
        foreach (var v in company.Vouchers)
        {
            if (!LedgerBalances.CountsAsOf(v, asOf)) continue;
            var type = company.FindVoucherType(v.TypeId);
            if (type is null || type.BaseType != billType) continue;

            foreach (var line in v.InventoryLines)
            {
                if (line.TrackingNumber is not { } tracking) continue;
                var p = Slot(pairs, tracking, line.StockItemId);
                // BilledQuantity, not Quantity — see the class remarks. A zero-billed free-goods line adds
                // nothing here, leaving its received goods correctly pending.
                p.Billed += BaseQuantity(company, line.StockItemId, line.UnitId, line.BilledQuantity);
                if (p.BillDate is null || v.Date < p.BillDate) p.BillDate = v.Date;
                NotePartyIfEarliest(p, v.Date, v.PartyId);
            }
        }

        var rows = new List<BillsPendingRow>();
        foreach (var ((tracking, itemId), p) in pairs)
        {
            // The NET is what makes the four properties in the class remarks hold simultaneously. A pair that
            // agrees exactly nets to zero and is listed in neither section — that is the no-double-count case.
            var net = p.Received - p.Billed;
            if (net == 0m) continue;

            var item = company.FindStockItem(itemId);
            var partyName = p.PartyId is { } pid ? company.FindLedger(pid)?.Name : null;

            var section = net > 0m ? BillsPendingSection.GoodsNotBilled : BillsPendingSection.BilledNotReceived;
            // Age the row from the side it is pending ON: goods-not-billed ages from the receipt, and
            // billed-not-received from the bill. Falling back to the other side keeps the field non-null in the
            // (impossible-by-construction) case where the pending side contributed no voucher.
            var date = section == BillsPendingSection.GoodsNotBilled
                ? p.GoodsDate ?? p.BillDate
                : p.BillDate ?? p.GoodsDate;

            rows.Add(new BillsPendingRow(
                section,
                tracking,
                itemId,
                item?.Name ?? "(unknown)",
                p.PartyId,
                partyName,
                date ?? asOf,
                p.Received,
                p.Billed,
                net > 0m ? net : -net));
        }

        // Deterministic total order across every platform: section, then date, then tracking number, then item
        // name, then the item id as the final tie-break. 🔴 Every string comparison is ORDINAL — a
        // culture-sensitive compare orders the same two rows differently on the ubuntu, macos and windows legs
        // of the gate, and a report whose row order depends on the machine cannot be asserted on.
        rows.Sort(static (a, b) =>
        {
            var bySection = a.Section.CompareTo(b.Section);
            if (bySection != 0) return bySection;
            var byDate = a.Date.CompareTo(b.Date);
            if (byDate != 0) return byDate;
            var byTracking = string.CompareOrdinal(a.TrackingNumber, b.TrackingNumber);
            if (byTracking != 0) return byTracking;
            var byItem = string.CompareOrdinal(a.ItemName, b.ItemName);
            return byItem != 0 ? byItem : a.StockItemId.CompareTo(b.StockItemId);
        });
        return rows;
    }

    private static Pair Slot(
        Dictionary<(string Tracking, Guid Item), Pair> pairs, string tracking, Guid itemId)
    {
        var key = (tracking, itemId);
        if (!pairs.TryGetValue(key, out var p)) pairs[key] = p = new Pair();
        return p;
    }

    /// <summary>Records the party from the EARLIEST voucher seen for this pair, so a report row names one
    /// party deterministically rather than whichever voucher the enumeration happened to reach last.</summary>
    private static void NotePartyIfEarliest(Pair p, DateOnly date, Guid? partyId)
    {
        if (partyId is null) return;
        if (p.PartyDate is not null && date >= p.PartyDate) return;
        p.PartyId = partyId;
        p.PartyDate = date;
    }

    /// <summary>
    /// Converts a line quantity to the stock item's BASE unit. A line stated in a compound/alternate unit
    /// (<c>unitId</c> non-null) is scaled by that unit's exact integer factor; a line already in the base unit
    /// passes through untouched. Without this a Receipt Note in dozens and a Purchase bill in pieces would net
    /// 12:1 against each other and manufacture a shortfall out of nothing.
    /// </summary>
    private static decimal BaseQuantity(Company company, Guid stockItemId, Guid? unitId, decimal quantity)
    {
        if (unitId is not { } uid) return quantity;
        var unit = company.FindUnit(uid);
        if (unit is null) return quantity;
        // A line whose unit IS the item's base unit needs no conversion — converting it would apply the
        // factor twice for a compound base unit.
        var item = company.FindStockItem(stockItemId);
        if (item is not null && item.BaseUnitId == uid) return quantity;
        return unit.QuantityInBaseMeasure(quantity);
    }
}
