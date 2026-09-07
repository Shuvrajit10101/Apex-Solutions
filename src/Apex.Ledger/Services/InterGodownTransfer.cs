using Apex.Ledger.Domain;

namespace Apex.Ledger.Services;

/// <summary>
/// The <b>Stock Journal transfer class</b> (census 9.9) — the single place a source stock line is mirrored to
/// a destination godown under a <see cref="VoucherClass"/> whose
/// <see cref="VoucherClass.UseClassForInterGodownTransfers"/> is set.
/// </summary>
/// <remarks>
/// <para><b>What the class buys the operator.</b> A Stock Journal has two arms — the outward
/// <see cref="InventoryVoucher.Allocations"/> and the inward <see cref="InventoryVoucher.DestinationAllocations"/>
/// — and they must balance. Without a class an operator keys both arms by hand and nothing stops them
/// diverging: a typo in the destination quantity, a forgotten batch, a different rate. With the class they name
/// ONE destination godown and key only the source; this method produces the other arm, so the two cannot
/// disagree by construction.</para>
///
/// <para>🔴 <b>EVERYTHING BUT THE GODOWN IS CARRIED THROUGH — including the batch and the rate.</b> The vendor
/// is explicit: <i>"all items/batches thus selected will be exactly mirrored to the destination, including
/// Batch Number, Rate and Value"</i>. Dropping the rate would let the transfer revalue the stock as it moved
/// (the destination arm would come in rate-less and the valuation engine would re-derive a rate), and dropping
/// the batch would silently merge tracked lots. Both are real money bugs, and both are what a hand-keyed
/// mirror gets wrong. The unit is carried for the same reason — mirroring "2 Doz" as "2" would move a
/// twelfth of the stock.</para>
///
/// <para>🔴 <b>The tracking data are carried too.</b> A transfer that dropped the
/// <see cref="InventoryAllocation.CostTrackingNumber"/> would break the item's cost lifecycle (census 9.7)
/// exactly at the point the goods moved site — the outward half would be attributed to the lot and the inward
/// half to nothing.</para>
///
/// <para><b>This is NOT the general Voucher Class machinery.</b> Census row 2.6 — ledger pre-maps, default
/// accounting allocations, rounding — remains ABSENT and is not this track's. A Stock Journal posts no ledger
/// entry, so the transfer class needs none of it. See <see cref="VoucherClass"/>.</para>
///
/// <para><b>R7 — ATTESTED.</b> <c>help.tallysolutions.com/voucher-types-tally/</c>: <i>"Use Class for
/// Inter-Godown Transfers"</i>, and <i>"Once you have created a voucher class with inter-godown transfer
/// enabled, all you have to do is just enter the destination godown while recording the stock journal voucher
/// and enter the item details."</i></para>
/// </remarks>
public static class InterGodownTransfer
{
    /// <summary>
    /// Mirrors every <paramref name="sourceLines"/> entry to <paramref name="destinationGodownId"/>, producing
    /// the inward arm of a class-driven Stock Journal transfer.
    /// </summary>
    /// <param name="sourceLines">The outward arm the operator keyed. Each must be
    /// <see cref="StockDirection.Outward"/> — this method does not flip a line's direction for the caller,
    /// because silently accepting an inward source line would produce a voucher that creates stock out of
    /// nothing on both arms.</param>
    /// <param name="destinationGodownId">The one godown the operator named on the class.</param>
    /// <returns>The inward destination allocations, in the same order as the source.</returns>
    /// <exception cref="InvalidOperationException">A source line already sits in the destination godown (a
    /// transfer from a godown to itself moves nothing and would post two cancelling arms that the balance
    /// guard cannot distinguish from a real transfer), or a source line is not outward.</exception>
    public static IReadOnlyList<InventoryAllocation> Mirror(
        IEnumerable<InventoryAllocation> sourceLines, Guid destinationGodownId)
    {
        ArgumentNullException.ThrowIfNull(sourceLines);
        if (destinationGodownId == Guid.Empty)
            throw new ArgumentException(
                "An inter-godown transfer class needs a destination godown.", nameof(destinationGodownId));

        var mirrored = new List<InventoryAllocation>();
        foreach (var line in sourceLines)
        {
            if (line.Direction != StockDirection.Outward)
                throw new InvalidOperationException(
                    "An inter-godown transfer's source lines must be outward; the class supplies the inward arm.");
            if (line.GodownId == destinationGodownId)
                throw new InvalidOperationException(
                    "An inter-godown transfer cannot move stock from a godown to itself.");

            mirrored.Add(new InventoryAllocation(
                line.StockItemId,
                destinationGodownId,
                line.Quantity,
                StockDirection.Inward,
                // Rate, batch and unit are carried VERBATIM — see this class's remarks for why dropping any
                // one of them is a money bug and not a cosmetic omission.
                rate: line.Rate,
                batchLabel: line.BatchLabel,
                unitId: line.UnitId,
                trackingNumber: line.TrackingNumber,
                costTrackingNumber: line.CostTrackingNumber));
        }
        return mirrored;
    }

    /// <summary>
    /// Resolves the transfer class named <paramref name="className"/> on <paramref name="type"/>, or
    /// <c>null</c> when the type has no such class.
    /// <para>The name match is <b>ordinal, case-insensitive</b>: a class name is an operator-typed label the
    /// schema already keeps unique per type, and an operator who types "transfer" means the class they called
    /// "Transfer". It is deliberately not culture-aware — a culture-aware compare resolves "TRANSFER" to a
    /// different class under a Turkish locale than under invariant, and the gate runs on ubuntu and macos as
    /// well as windows.</para>
    /// </summary>
    public static VoucherClass? FindTransferClass(VoucherType? type, string? className)
    {
        if (type is null || string.IsNullOrWhiteSpace(className)) return null;
        var wanted = className.Trim();
        foreach (var c in type.InterGodownTransferClasses)
            if (string.Equals(c.Name, wanted, StringComparison.OrdinalIgnoreCase))
                return c;
        return null;
    }
}
