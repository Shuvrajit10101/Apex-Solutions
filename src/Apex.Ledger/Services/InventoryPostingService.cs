using Apex.Ledger.Domain;

namespace Apex.Ledger.Services;

/// <summary>
/// One detected negative-stock shortfall (plan.md NS-3): an (item, godown, batch) key whose running on-hand went
/// below zero, reported with the <b>earliest</b> date it did so and the on-hand at that date, plus the
/// operator-facing <see cref="Message"/>. Purely descriptive — producing one never rejects, alters or rolls back
/// anything.
/// </summary>
/// <param name="StockItemId">The stock item that went short.</param>
/// <param name="ItemName">Its name at detection time (resolved once, so callers need no company reference).</param>
/// <param name="GodownId">The godown the shortfall is in.</param>
/// <param name="GodownName">Its name at detection time.</param>
/// <param name="Batch">The batch label, or <see cref="string.Empty"/> for a non-batch key.</param>
/// <param name="AsOf">The EARLIEST date at which this key's on-hand was negative.</param>
/// <param name="OnHand">The (negative) on-hand at <paramref name="AsOf"/> — not the worst it later reaches.</param>
/// <param name="Message">The operator-facing text, naming item, godown, batch and shortfall.</param>
public sealed record NegativeStockShortfall(
    Guid StockItemId,
    string ItemName,
    Guid GodownId,
    string GodownName,
    string Batch,
    DateOnly AsOf,
    decimal OnHand,
    string Message);

/// <summary>
/// The stock/order-voucher posting service (catalog §10; phase3-inventory-requirements RQ-8..RQ-15,
/// ER-5/ER-10, DP-3/DP-5). It validates an <see cref="InventoryVoucher"/> against its type, enforces the
/// <b>Stock-Journal balance</b> rule (source total = destination total, in the base unit), then appends the
/// voucher — assigning an automatic number per type. It is the inventory analogue of <see cref="LedgerService"/>
/// for the accounting side, and it is the <b>only</b> path that mutates the company's stock/order voucher set.
/// Framework- and DB-agnostic — unit-tested exactly like the accounting core.
///
/// <para><b>⚠️ Negative stock is NOT blocked (plan.md NS-3; changed at schema v50).</b> What was the
/// "no-negative-stock hard block" (DP-7) is now a <b>non-throwing detector</b>: every posting persists, and
/// <see cref="DetectNegativeStock"/> reports the resulting shortfalls. This matches the reference application,
/// which has no built-in block anywhere — its only reaction is an advisory F12 warning after which the voucher
/// still saves (official TallyHelp, <i>Configuring an Invoice</i> + the Sales FAQ; <b>the licensed corpus is
/// silent — 0 hits for "negative stock"/"negative balance" across all ten PDFs</b>, so this is docs-sourced).
/// The most common real Indian trading sequence — deliver today, book the supplier's bill next week — was
/// un-postable under the old block.</para>
///
/// <para><b>What this did NOT change.</b> The Stock-Journal balance rule still <b>rejects</b> and still rolls
/// back; a Physical-Stock count of a negative quantity is still rejected (a count is a statement of fact about
/// the shelf); and <b>how a negative on-hand is VALUED is untouched</b> — <see cref="StockValuationService"/> is
/// deliberately not modified by this slice (plan.md NS-1/NS-8 remain open).</para>
/// </summary>
public sealed class InventoryPostingService
{
    private readonly Company _company;
    private readonly InventoryLedger _ledger;

    public InventoryPostingService(Company company)
    {
        _company = company ?? throw new ArgumentNullException(nameof(company));
        _ledger = new InventoryLedger(company);
    }

    /// <summary>
    /// Validates then posts a stock/order voucher. Rejects (never persists) a voucher whose content does not
    /// match its type or whose Stock-Journal sides do not balance. A voucher that drives an (item, godown[,
    /// batch]) on-hand <b>negative is accepted and persisted</b> (NS-3) — call
    /// <see cref="DetectNegativeStock"/> afterwards to surface the shortfall. Returns the posted voucher.
    /// </summary>
    public InventoryVoucher Post(InventoryVoucher voucher)
    {
        ArgumentNullException.ThrowIfNull(voucher);

        // The accounting and pure-stock aggregates share ONE id space, and nothing used to say so. The
        // "LedgerService.Replace cannot reach an InventoryVoucher" guarantee is structural only while the two id
        // spaces stay disjoint: measured without this guard, a Physical Stock voucher posted carrying an ACCOUNTING
        // voucher's Guid was accepted, after which Replace(thatGuid, …) silently altered the accounting one.
        LedgerService.EnsureVoucherIdIsFree(_company, voucher.Id);

        var type = _company.FindVoucherType(voucher.TypeId)
            ?? throw new InvalidOperationException($"Unknown voucher type {voucher.TypeId}.");

        if (voucher.Date < _company.BooksBeginFrom)
            throw new InvalidOperationException(
                $"Voucher date {voucher.Date:yyyy-MM-dd} is before BooksBeginFrom {_company.BooksBeginFrom:yyyy-MM-dd}.");

        EnsureContentMatchesType(voucher, type);
        EnsureReferencesResolve(voucher);

        // Balance rule: a two-sided stock movement whose company net-on-hand must stay constant (a plain Stock
        // Journal, or a Material transfer that carries BOTH a source and a destination — RQ-46) must balance in the
        // base unit. EXEMPT are the two TRANSFORMS: a Manufacturing Journal (RQ-11/RQ-13) and a consuming Material In
        // (base Material In + Allow Consumption, RQ-49) — inputs become a different output, so the sides need not
        // balance by quantity. A one-sided Material movement (a worker's pure-outward FG dispatch, or a pure-inward
        // receipt) has nothing to balance. A plain Stock Journal still must balance (ER-13).
        if (RequiresSourceDestinationBalance(type, voucher))
            EnsureStockJournalBalances(voucher);

        if (type.Numbering == NumberingMethod.Automatic && voucher.Number <= 0)
            voucher.Number = NextNumber(voucher.TypeId);

        // numbering-design-v2 §3/§7 — Prevent Duplicate on the second (inventory) engine, mirroring
        // VoucherValidator so a Prevent-Duplicate Stock Journal is ENFORCED, never silently ignored (review r2-F2).
        // An Automatic number is max+1 ⇒ never collides; the guard bites on a Manual/pre-set number that already
        // renders the same string (ordinal, case-sensitive) on a non-deleted inventory voucher of the same type.
        if (type.PreventDuplicate)
        {
            var rendered = VoucherNumberFormatter.Render(type, voucher.Number, voucher.Date);
            if (rendered.Length > 0)
                foreach (var other in _company.InventoryVouchers)
                {
                    if (other.Id == voucher.Id) continue;
                    if (other.TypeId != voucher.TypeId) continue;
                    if (string.Equals(
                            VoucherNumberFormatter.Render(type, other.Number, other.Date), rendered,
                            StringComparison.Ordinal))
                        throw new InvalidOperationException(
                            $"Voucher number '{rendered}' already exists for '{type.Name}' (Prevent Duplicates is on).");
                }
        }

        // ⚠️ NS-3 — CALL SITE 1 of 4. This used to apply the voucher provisionally, run the negative guard and
        // roll back on violation. The guard no longer throws, so the posting simply STANDS. Kept as a plain
        // append (no try/catch) rather than a detector call whose result is discarded: the caller decides when
        // to ask for shortfalls, and running a whole-book scan on every post would be pure cost.
        _company.AddInventoryVoucherInternal(voucher);
        return voucher;
    }

    /// <summary>
    /// Cancels a stock/order voucher (Alt+X): its effect drops to zero but its number is retained. ⚠️ NS-3 —
    /// CALL SITE 2 of 4: this used to be BLOCKED when removing the effect retro-drove a later movement negative.
    /// It no longer is; the cancel always applies, and the resulting shortfall (if any) is reported by
    /// <see cref="DetectNegativeStock"/>.
    /// </summary>
    public void Cancel(Guid voucherId)
    {
        var v = _company.FindInventoryVoucher(voucherId)
            ?? throw new InvalidOperationException($"Inventory voucher {voucherId} not found.");
        v.Cancelled = true;
    }

    /// <summary>
    /// Deletes a stock/order voucher (Alt+D). ⚠️ NS-3 — CALL SITE 3 of 4: this used to be BLOCKED when removing
    /// its (inward) effect retro-drove a later movement's on-hand negative. It no longer is; the delete always
    /// applies, and the resulting shortfall (if any) is reported by <see cref="DetectNegativeStock"/>.
    /// </summary>
    public void Delete(Guid voucherId)
    {
        var v = _company.FindInventoryVoucher(voucherId)
            ?? throw new InvalidOperationException($"Inventory voucher {voucherId} not found.");

        _company.RemoveInventoryVoucherInternal(v);
    }

    /// <summary>Next automatic number for an inventory voucher type = max existing + 1 (per type).</summary>
    public int NextNumber(Guid voucherTypeId)
    {
        var max = 0;
        foreach (var v in _company.InventoryVouchers)
            if (v.TypeId == voucherTypeId && v.Number > max)
                max = v.Number;
        return max + 1;
    }

    /// <summary>
    /// The negative-stock <b>detector</b> (NS-3) — what the old hard block became, and its single entry point.
    ///
    /// <para>⚠️ <b>There were FOUR call sites into the old guard, and all four had to be un-blocked together.</b>
    /// <see cref="Post"/>, <see cref="Cancel"/> and <see cref="Delete"/> called the PRIVATE
    /// <c>EnsureNoNegativeStockAnywhere</c> directly, while <see cref="LedgerService"/> (Post/Cancel/Delete of an
    /// item-invoice voucher) went through a PUBLIC <c>EnsureNoNegativeStock</c> wrapper — so a change applied to
    /// only one of the two would have left the other half of the engine still hard-blocking, invisibly. That public
    /// wrapper is now <b>deleted</b> rather than aliased to this method: a method named "Ensure…" that returns a
    /// list and never throws is a trap for the next reader, and with all four sites un-blocked it had no callers
    /// left to keep.</para>
    ///
    /// <para>Scans the whole company timeline (pure-stock movements AND item-invoice Purchase/Sales stock lines)
    /// and returns one row per (item, godown, batch) key whose running on-hand went below zero, carrying the
    /// <b>earliest</b> date it did so. <b>Unconditional</b>: it ignores <see cref="Company.WarnOnNegativeStock"/>,
    /// because that flag governs whether the operator is told, never what the books contain. Never throws, never
    /// mutates. Rows are ordered by item name, then godown name, then batch — deterministic, framework-agnostic.</para>
    /// </summary>
    public IReadOnlyList<NegativeStockShortfall> DetectNegativeStock() => DetectNegativeStockAnywhere();

    /// <summary>
    /// The flag-gated <b>warning surface</b> (NS-3/NS-4): <see cref="DetectNegativeStock"/> when the company's
    /// <see cref="Company.WarnOnNegativeStock"/> is on, an empty list when it is off. This is the ONLY place the
    /// flag is consulted — turning it off silences warnings and changes nothing else, which is exactly what
    /// "warn-only" has to mean.
    /// </summary>
    public IReadOnlyList<NegativeStockShortfall> NegativeStockWarnings() =>
        _company.WarnOnNegativeStock ? DetectNegativeStock() : [];

    // ------------------------------------------------------------------ validation

    private static void EnsureContentMatchesType(InventoryVoucher v, VoucherType type)
    {
        var bt = type.BaseType;

        if (!VoucherEffects.IsInventoryBaseType(bt))
            throw new InvalidOperationException(
                $"Voucher type '{type.Name}' is not a stock or order voucher; it cannot be posted through the inventory engine.");

        var hasOrders = v.OrderLines.Count > 0;
        var hasAllocs = v.Allocations.Count > 0;
        var hasDest = v.DestinationAllocations.Count > 0;
        var hasPhysical = v.PhysicalLines.Count > 0;

        switch (bt)
        {
            case VoucherBaseType.PurchaseOrder:
            case VoucherBaseType.SalesOrder:
                if (!hasOrders || hasAllocs || hasDest || hasPhysical)
                    throw new InvalidOperationException($"A {type.Name} must carry order lines only (no stock movements).");
                break;

            case VoucherBaseType.ReceiptNote:
            case VoucherBaseType.RejectionIn:
                RequireAllocationsOnly(type, v);
                RequireDirection(type, v.Allocations, StockDirection.Inward);
                break;

            case VoucherBaseType.DeliveryNote:
            case VoucherBaseType.RejectionOut:
                RequireAllocationsOnly(type, v);
                RequireDirection(type, v.Allocations, StockDirection.Outward);
                break;

            case VoucherBaseType.StockJournal:
                if (!hasAllocs || !hasDest || hasOrders || hasPhysical)
                    throw new InvalidOperationException(
                        $"A {type.Name} must carry source (outward) and destination (inward) lines.");
                RequireDirection(type, v.Allocations, StockDirection.Outward, "source");
                RequireDirection(type, v.DestinationAllocations, StockDirection.Inward, "destination");
                break;

            case VoucherBaseType.PhysicalStock:
                if (!hasPhysical || hasAllocs || hasDest || hasOrders)
                    throw new InvalidOperationException($"A {type.Name} must carry counted-quantity lines only.");
                break;

            // Job Work In/Out Order (Phase 6 slice 8; RQ-47): the job-work payload only — no stock movements. The
            // order's direction must match its base type (In ⇒ we are the worker, Out ⇒ we are the principal), so a
            // worker order can never be filed under the principal type and vice-versa.
            case VoucherBaseType.JobWorkInOrder:
            case VoucherBaseType.JobWorkOutOrder:
                if (v.JobWorkOrder is null || hasAllocs || hasDest || hasOrders || hasPhysical)
                    throw new InvalidOperationException(
                        $"A {type.Name} must carry a job-work order payload only (no stock movements).");
                var expected = bt == VoucherBaseType.JobWorkInOrder ? JobWorkDirection.In : JobWorkDirection.Out;
                if (v.JobWorkOrder.Direction != expected)
                    throw new InvalidOperationException(
                        $"A {type.Name} must carry a {expected} Job Work order (its payload direction is {v.JobWorkOrder.Direction}).");
                break;

            // Material In / Material Out (Phase 6 slice 8; RQ-46/RQ-48/RQ-49): source (outward) and/or destination
            // (inward) stock-movement lines — a balanced third-party transfer, a consumption transform, or a pure
            // one-sided movement. NEVER an order payload / order lines / physical lines. Both principal and worker
            // ride the same shape (RQ-50). At least one movement line is required.
            case VoucherBaseType.MaterialIn:
            case VoucherBaseType.MaterialOut:
                if (v.JobWorkOrder is not null || hasOrders || hasPhysical)
                    throw new InvalidOperationException($"A {type.Name} must carry stock-movement lines only.");
                if (!hasAllocs && !hasDest)
                    throw new InvalidOperationException(
                        $"A {type.Name} must carry at least one source (outward) or destination (inward) line.");
                RequireDirection(type, v.Allocations, StockDirection.Outward, "source");
                RequireDirection(type, v.DestinationAllocations, StockDirection.Inward, "destination");
                break;
        }
    }

    /// <summary>
    /// Whether a two-sided stock movement must balance in the base unit (source total = destination total). True
    /// for a plain Stock Journal and for a Material In/Out that carries BOTH sides (a location move, RQ-46). False
    /// for the two transforms — a Manufacturing Journal and a consuming Material In (RQ-49) — and for a one-sided
    /// Material movement (nothing to balance).
    /// </summary>
    private static bool RequiresSourceDestinationBalance(VoucherType type, InventoryVoucher v)
    {
        if (type.IsManufacturingJournal || type.IsConsumingMaterialIn) return false;
        return type.BaseType switch
        {
            VoucherBaseType.StockJournal => true,
            VoucherBaseType.MaterialIn or VoucherBaseType.MaterialOut =>
                v.Allocations.Count > 0 && v.DestinationAllocations.Count > 0,
            _ => false,
        };
    }

    private static void RequireAllocationsOnly(VoucherType type, InventoryVoucher v)
    {
        if (v.Allocations.Count == 0 || v.OrderLines.Count > 0 || v.DestinationAllocations.Count > 0 || v.PhysicalLines.Count > 0)
            throw new InvalidOperationException($"A {type.Name} must carry stock-movement lines only.");
    }

    private static void RequireDirection(VoucherType type, IReadOnlyList<InventoryAllocation> lines,
        StockDirection expected, string? side = null)
    {
        foreach (var a in lines)
            if (a.Direction != expected)
                throw new InvalidOperationException(
                    $"A {type.Name}{(side is null ? "" : $" {side}")} line must be {expected.ToString().ToLowerInvariant()}.");
    }

    private void EnsureReferencesResolve(InventoryVoucher v)
    {
        void CheckAlloc(InventoryAllocation a)
        {
            var item = _company.FindStockItem(a.StockItemId);
            if (item is null)
                throw new InvalidOperationException($"Inventory line references unknown stock item {a.StockItemId}.");
            if (_company.FindGodown(a.GodownId) is null)
                throw new InvalidOperationException($"Inventory line references unknown godown {a.GodownId}.");
            if (a.UnitId is { } uid)
            {
                var unit = _company.FindUnit(uid);
                if (unit is null)
                    throw new InvalidOperationException($"Inventory line references unknown unit {uid}.");
                // A line's quantity is normalised via Unit.QuantityInBaseMeasure before it accumulates on
                // hand, so the unit MUST reduce to the item's own base unit — otherwise "1 Kg" of a
                // Nos-measured item would silently scale by an unrelated factor. (WI-10 Gap 7.)
                if (unit.BaseMeasureUnitId != item.BaseUnitId)
                {
                    var itemUnit = _company.FindUnit(item.BaseUnitId)?.Symbol ?? item.BaseUnitId.ToString();
                    throw new InvalidOperationException(
                        $"Inventory line for '{item.Name}' states its quantity in '{unit.Symbol}', which does not " +
                        $"reduce to the item's base unit '{itemUnit}'.");
                }
            }
        }

        foreach (var a in v.Allocations) CheckAlloc(a);
        foreach (var a in v.DestinationAllocations) CheckAlloc(a);
        foreach (var o in v.OrderLines)
        {
            if (_company.FindStockItem(o.StockItemId) is null)
                throw new InvalidOperationException($"Order line references unknown stock item {o.StockItemId}.");
            if (_company.FindGodown(o.GodownId) is null)
                throw new InvalidOperationException($"Order line references unknown godown {o.GodownId}.");
        }
        foreach (var pl in v.PhysicalLines)
        {
            if (_company.FindStockItem(pl.StockItemId) is null)
                throw new InvalidOperationException($"Physical-stock line references unknown stock item {pl.StockItemId}.");
            if (_company.FindGodown(pl.GodownId) is null)
                throw new InvalidOperationException($"Physical-stock line references unknown godown {pl.GodownId}.");
        }

        // Job Work order payload (RQ-47): the finished good, its godown, the fill-components BOM (Slice 2 link) and
        // every tracked component item/godown must resolve.
        if (v.JobWorkOrder is { } jwo)
        {
            if (_company.FindStockItem(jwo.FinishedGoodStockItemId) is null)
                throw new InvalidOperationException($"Job Work order references unknown finished-good item {jwo.FinishedGoodStockItemId}.");
            if (jwo.FinishedGoodGodownId is { } fgg && _company.FindGodown(fgg) is null)
                throw new InvalidOperationException($"Job Work order references unknown godown {fgg}.");
            if (jwo.FillComponentsBomId is { } bomId && _company.FindBillOfMaterials(bomId) is null)
                throw new InvalidOperationException($"Job Work order references unknown Bill of Materials {bomId}.");
            foreach (var line in jwo.Lines)
            {
                if (_company.FindStockItem(line.ComponentStockItemId) is null)
                    throw new InvalidOperationException($"Job Work order component references unknown stock item {line.ComponentStockItemId}.");
                if (line.GodownId is { } cg && _company.FindGodown(cg) is null)
                    throw new InvalidOperationException($"Job Work order component references unknown godown {cg}.");
            }
        }

        // Material In/Out order links (RQ-48): each fulfilled order must be a posted Job Work order voucher.
        foreach (var linkId in v.OrderLinks)
        {
            var order = _company.FindInventoryVoucher(linkId);
            if (order?.JobWorkOrder is null)
                throw new InvalidOperationException($"Material voucher references unknown Job Work order {linkId}.");
        }
    }

    private void EnsureStockJournalBalances(InventoryVoucher v)
    {
        var source = v.Allocations.Sum(a => _ledger.QuantityInBase(a));
        var dest = v.DestinationAllocations.Sum(a => _ledger.QuantityInBase(a));
        if (source != dest)
            throw new InvalidOperationException(
                $"Stock Journal source total {source} and destination total {dest} do not balance (they must be equal in the base unit).");
    }

    /// <summary>
    /// The centralised negative-stock <b>scan</b> (NS-3; formerly the ER-5/DP-7 hard guard): across every
    /// (item, godown, batch) touched by any posted movement, sample on-hand at every voucher date where the key
    /// is affected and collect the keys that go below zero. A Physical-Stock count is applied LAST within its
    /// date and <b>sets</b> on-hand to the counted quantity (DP-3), so end-of-date sampling would mask a
    /// same-date outward line that over-drew pre-count stock; the scan therefore reads the running balance
    /// <b>before</b> that date's count checkpoint (<see cref="InventoryLedger.PreCountOnHandForKey"/>) — on
    /// non-count dates this equals end-of-date on-hand. DP-3 reporting/carry-forward is unchanged.
    ///
    /// <para><b>One row per key, at the EARLIEST negative date.</b> A key that goes short stays short across
    /// every later voucher date in the book, so sampling every (key, date) pair would re-report one shortfall
    /// dozens of times and bury the others. The first date is the actionable one — it is the posting that
    /// caused it.</para>
    /// </summary>
    private IReadOnlyList<NegativeStockShortfall> DetectNegativeStockAnywhere()
    {
        // Every affected key.
        var keys = new HashSet<InventoryLedger.Key>();
        var dates = new SortedSet<DateOnly>();
        foreach (var v in _company.InventoryVouchers)
        {
            if (v.Cancelled) continue;
            var type = _company.FindVoucherType(v.TypeId);
            if (type is null || !type.AffectsStock) continue;

            foreach (var a in v.Allocations.Concat(v.DestinationAllocations))
            {
                keys.Add(new InventoryLedger.Key(a.StockItemId, a.GodownId, Batch(a.BatchLabel)));
                dates.Add(v.Date);
            }
            foreach (var pl in v.PhysicalLines)
            {
                keys.Add(new InventoryLedger.Key(pl.StockItemId, pl.GodownId, Batch(pl.BatchLabel)));
                dates.Add(v.Date);
            }
        }

        // Item-invoice (Purchase/Sales) stock lines touch the same keys/dates and must satisfy the same guard,
        // so a Sales item-invoice that would over-draw on-hand is blocked (and rolled back) exactly like a
        // Delivery Note (DP-7). Cancelled/optional item-invoice vouchers never contribute (Counts filters them).
        foreach (var v in _company.Vouchers)
        {
            if (!v.HasInventoryLines) continue;
            if (v.Cancelled || v.Optional) continue;
            var type = _company.FindVoucherType(v.TypeId);
            if (type is null || type.BaseType is not (VoucherBaseType.Purchase or VoucherBaseType.Sales)) continue;
            foreach (var line in v.InventoryLines)
            {
                keys.Add(new InventoryLedger.Key(line.StockItemId, line.GodownId, Batch(line.BatchLabel)));
                dates.Add(v.Date);
            }
        }

        var shortfalls = new List<NegativeStockShortfall>();
        foreach (var key in keys)
        {
            foreach (var date in dates)
            {
                // Read the running balance BEFORE the same-date Physical-Stock checkpoint: on a count date this
                // exposes an intra-day over-draw the checkpoint would otherwise hide; on a non-count date it is
                // identical to end-of-date on-hand.
                var onHand = _ledger.PreCountOnHandForKey(key, date);
                if (onHand >= 0m) continue;

                var item = _company.FindStockItem(key.ItemId)?.Name ?? key.ItemId.ToString();
                var godown = _company.FindGodown(key.GodownId)?.Name ?? key.GodownId.ToString();
                var batch = string.IsNullOrEmpty(key.Batch) ? "" : $" (batch '{key.Batch}')";
                shortfalls.Add(new NegativeStockShortfall(
                    key.ItemId, item, key.GodownId, godown, key.Batch, date, onHand,
                    $"'{item}' at '{godown}'{batch} is negative: on-hand {onHand} as of {date:yyyy-MM-dd}."));
                break;   // earliest negative date only — `dates` is a SortedSet, so this is the first one
            }
        }

        shortfalls.Sort((a, b) =>
        {
            var byItem = string.Compare(a.ItemName, b.ItemName, StringComparison.OrdinalIgnoreCase);
            if (byItem != 0) return byItem;
            var byGodown = string.Compare(a.GodownName, b.GodownName, StringComparison.OrdinalIgnoreCase);
            return byGodown != 0 ? byGodown : string.Compare(a.Batch, b.Batch, StringComparison.Ordinal);
        });
        return shortfalls;
    }

    private static string Batch(string? batch) => string.IsNullOrWhiteSpace(batch) ? string.Empty : batch.Trim();
}
