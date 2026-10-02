using Apex.Ledger.Domain;

namespace Apex.Ledger.Reports;

/// <summary>
/// One Day Book row (design §7.5). <see cref="VoucherId"/> is the underlying voucher's stable id
/// (RQ-7 universal drill-down): Enter on the row opens that voucher's detail. It is always a real id here
/// (every Day Book row IS a voucher), so <see cref="IsDrillable"/> is always true for Day Book rows.
///
/// <para>🔴 <b><see cref="IsInventory"/> says WHICH AGGREGATE <see cref="VoucherId"/> addresses, and every
/// consumer must branch on it before resolving the id.</b> The two aggregates share one id space
/// (<c>LedgerService.EnsureVoucherIdIsFree</c> keeps them disjoint), so an id is unambiguous — but
/// <c>Company.FindVoucher</c> returns <c>null</c> for a pure-stock voucher and <c>FindInventoryVoucher</c>
/// returns <c>null</c> for an accounting one, and a caller that asks only the first silently drops the row.
/// That is precisely the defect this flag exists to make unmissable: before it, every route that consumed a
/// Day Book row (drill, Alt+X cancel, Alt+D delete) resolved through <c>FindVoucher</c> alone and became a
/// dead key on any row this field is <c>true</c> for.</para>
/// </summary>
public sealed record DayBookRow(
    DateOnly Date,
    string VoucherTypeName,
    int Number,
    string? PartyOrParticulars,
    Money Amount,
    bool IsCancelled,
    Guid VoucherId = default,
    string FormattedNumber = "",
    bool IsInventory = false)
{
    /// <summary>True iff Enter should drill this row into the underlying voucher's detail.</summary>
    public bool IsDrillable => VoucherId != Guid.Empty;
}

/// <summary>
/// The Day Book (design §7.5): all vouchers within a date range in chronological order
/// (then by number within a date). Cancelled vouchers are included but flagged (shown
/// greyed with zero effect); optional/post-dated vouchers still list. The amount is the
/// voucher's debit total (= credit total for a balanced voucher).
///
/// <para>🔴 <b>BOTH aggregates list here, and for eight census rows (4.9–4.16) that is the whole difference
/// between a posted stock movement being correctable and being permanent.</b> This used to iterate
/// <see cref="Company.Vouchers"/> alone, so a Stock Journal, Physical Stock, Delivery/Receipt Note, order or
/// Rejection was invisible in the one report an operator opens to find a transaction — and because the Day
/// Book is the surface Alt+X (cancel) and Alt+D (delete) act on, a mis-keyed stock movement had no remedy in
/// the product but posting an equal and opposite one.</para>
///
/// <para><b>FIDELITY (R7; RULING 14 — vendor documentation).</b>
/// <i>help.tallysolutions.com/tally-prime/accounting-reports-tally/day-book-tally/</i> (fetched 2026-09-14)
/// attests that the Day Book covers both: its view knob offers <i>"All Vouchers"</i> — <i>"displays Day Book
/// for all the vouchers, irrespective of the type of voucher"</i> — beside <i>"Inventory Entries Only"</i>,
/// which <i>"displays Day Book for only inventory vouchers such as Journal Vouchers for stock items, Delivery
/// Note, Physical Stock Voucher, and others"</i>. The same page attests the two lifecycle verbs on this report:
/// <i>"Press Alt+D to delete"</i> and <i>"Press Alt+X to cancel"</i>. Our default is the vendor's "All
/// Vouchers"; the two narrowing views are not built and are an honest, named divergence.</para>
///
/// <para>🔴 <b>WHAT AN INVENTORY ROW'S <see cref="DayBookRow.Amount"/> IS, AND WHAT IT IS NOT.</b> It is the
/// <b>value of the stock the voucher moved</b> — rate × quantity, both re-expressed in the item's BASE unit,
/// which is the identical derivation <see cref="InventoryRegisters"/> already ships for its Value column, so
/// the Day Book and the registers cannot state two different figures for one voucher. It is <b>NOT an
/// accounting amount</b>: a pure-stock voucher posts no entry (DP-5), so nothing it contributes belongs in a
/// money total. <b>Nothing in the product sums this column</b> — <c>ReportsViewModel.BuildDayBook</c> adds no
/// total row and no other caller of <see cref="Build"/> aggregates it — which is what keeps the Day Book
/// reconciling with the books after this change, and is why the two kinds of figure can share one column at
/// all. A future total row MUST restrict itself to <c>!IsInventory</c> rows or it will invent money.</para>
///
/// <para><b>An allocation with no rate contributes nothing</b> (<see cref="Money.Zero"/>), which is why a
/// Physical Stock count — a statement about the shelf, carrying no rate — lists at zero rather than at a
/// fabricated valuation. Orders (PO/SO) value their order lines; they move neither stock nor money, and the
/// figure is the order's worth, which is what makes the row identifiable in the list.</para>
/// </summary>
public static class DayBook
{
    public static IReadOnlyList<DayBookRow> Build(Company company, DateOnly from, DateOnly to)
    {
        ArgumentNullException.ThrowIfNull(company);

        var rows = new List<DayBookRow>();

        foreach (var v in company.Vouchers)
        {
            if (v.Date < from || v.Date > to) continue;

            var type = company.FindVoucherType(v.TypeId);
            var typeName = type?.Name ?? "(unknown)";

            string? particulars = null;
            if (v.PartyId is Guid partyId)
                particulars = company.FindLedger(partyId)?.Name;
            particulars ??= v.Narration;

            rows.Add(new DayBookRow(v.Date, typeName, v.Number, particulars, v.TotalDebit, v.Cancelled, v.Id,
                company.FormatVoucherNumber(v)));
        }

        foreach (var v in company.InventoryVouchers)
        {
            if (v.Date < from || v.Date > to) continue;

            var type = company.FindVoucherType(v.TypeId);
            var typeName = type?.Name ?? "(unknown)";

            string? particulars = null;
            if (v.PartyId is Guid partyId)
                particulars = company.FindLedger(partyId)?.Name;
            particulars ??= v.Narration;

            rows.Add(new DayBookRow(v.Date, typeName, v.Number, particulars, MovementValue(company, v),
                v.Cancelled, v.Id, company.FormatVoucherNumber(v), IsInventory: true));
        }

        rows.Sort((a, b) =>
        {
            var byDate = a.Date.CompareTo(b.Date);
            if (byDate != 0) return byDate;
            var byNumber = a.Number.CompareTo(b.Number);
            if (byNumber != 0) return byNumber;
            // 🔴 THE TIE-BREAK IS NOT DECORATION. The two aggregates number INDEPENDENTLY — an accounting
            // Receipt #1 and a Delivery Note #1 on one date are ordinary — so (Date, Number) alone stopped being
            // a total order the moment both kinds listed here, and `List.Sort` is UNSTABLE. Type name then id:
            // both are deterministic, and the id makes the order total even for two same-named types.
            //
            // ⚠️ CORRECTED — the rationale written here first claimed the order "varied between runs and between
            // platforms" and would go "intermittently red on ubuntu/macos". THAT IS NOT TRUE and was never
            // measured: .NET's introsort is deterministic for a given input, so without this key the order would
            // have been stable — just meaningless, an artefact of which of the two loops above appended first.
            // The claim mattered because a test was written to the false premise (rebuild the book twelve times,
            // assert the order matches) and it could not fail: it survived reducing this comparator to
            // `return 0`. The honest reason to keep the key is that the order should be PREDICTABLE FROM THE ROW,
            // not from the loop order in this method — and that is what the test now pins.
            var byType = string.Compare(a.VoucherTypeName, b.VoucherTypeName, StringComparison.Ordinal);
            return byType != 0 ? byType : a.VoucherId.CompareTo(b.VoucherId);
        });

        return rows;
    }

    /// <summary>
    /// The value of the stock (or the order) an <see cref="InventoryVoucher"/> carries — see the class summary
    /// for what this figure is and, more importantly, what it is not.
    ///
    /// <para><b>Why a Stock Journal reads its DESTINATION side and every other movement its source side.</b> A
    /// Stock Journal carries BOTH sides of one movement and they balance in the base unit; summing both would
    /// state the voucher at twice its worth. The destination (production/inward) side is the one that carries
    /// the landed rate — <c>AdditionalCostApportionment</c> raises exactly those lines (RQ-20) — so it is also
    /// the side whose figure an operator would recognise. A Material movement is the same shape and takes the
    /// same branch. A one-sided movement has only the one list to read, whichever it is.</para>
    /// </summary>
    private static Money MovementValue(Company company, InventoryVoucher v)
    {
        if (v.DestinationAllocations.Count > 0) return Sum(company, v.DestinationAllocations);
        if (v.Allocations.Count > 0) return Sum(company, v.Allocations);

        var orderValue = Money.Zero;
        foreach (var line in v.OrderLines)
            if (line.Rate is { } rate)
                orderValue += Money.ForexBase(rate, line.Quantity);
        return orderValue;
    }

    private static Money Sum(Company company, IReadOnlyList<InventoryAllocation> allocations)
    {
        var total = Money.Zero;
        foreach (var a in allocations)
        {
            if (a.Rate is not { } rate) continue;

            // The same base-unit normalisation InventoryRegisters performs, and for the same reason: a line's
            // rate is per the unit the LINE is stated in, so quantity and rate must be converted by the same
            // factor or the product moves by it. Unresolvable unit => the line is taken as already in base.
            var quantity = a.Quantity;
            var perUnit = rate.Amount;
            if (a.UnitId is { } unitId && company.FindUnit(unitId) is { } unit)
            {
                quantity = unit.QuantityInBaseMeasure(a.Quantity);
                perUnit = unit.RateInBaseMeasure(rate.Amount);
            }

            total += Money.ForexBase(new Money(perUnit), quantity);
        }

        return total;
    }
}
