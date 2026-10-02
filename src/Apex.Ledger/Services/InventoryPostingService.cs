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

        // census 5.10 — ASK the type whether its method numbers automatically. This used to compare against
        // NumberingMethod.Automatic alone, which would have left every Automatic (Manual Override) and
        // Multi-user Auto voucher unnumbered the moment those two attested methods became selectable.
        if (type.AssignsNumberAutomatically && voucher.Number <= 0)
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

    // ===================================================================== Replace — the THIRD lifecycle verb

    /// <summary>
    /// 🔴 <b>Alters a posted stock/order voucher in place (Ctrl+Enter) — the verb this service was MISSING, and
    /// the sole reason census rows 4.9–4.16 and 9.2 could not reach <c>COMPLETE</c>.</b> <c>Post</c>,
    /// <see cref="Cancel"/> and <see cref="Delete"/> shipped in PR #107; alteration had no engine counterpart, so
    /// <c>VoucherEntryViewModel.ForAlter</c> refused every inventory-aggregate voucher by design and the Day Book's
    /// Ctrl+Enter could only name the limit.
    ///
    /// <para><b>🔴 THE STOCK CONSEQUENCE IS THE WHOLE POINT, AND IT IS WHY THE SWAP IS STRUCTURAL RATHER THAN
    /// ARITHMETIC.</b> A partial reversal — subtracting the old quantities and adding the new — would silently
    /// corrupt closing stock, and closing stock moves the Balance Sheet. Nothing here computes a delta. The
    /// outgoing voucher is removed from the timeline and the incoming one takes its slot, so <b>every</b> effect
    /// the old voucher had (on-hand, batch and godown balances, FIFO/Avg consumption order, order fulfilment,
    /// additional-cost apportionment, the Job Work pending figures) is reversed by the same mechanism that
    /// created it, and the new effect is applied by the same mechanism that applies a post. There is no third
    /// code path that could drift from either.</para>
    ///
    /// <para><b>It is logged as <see cref="VoucherEditVerb.Alter"/>, the SAME verb an ordinary voucher's
    /// alteration records</b> (<c>LedgerService.Replace</c>), carrying a <c>VoucherSnapshot</c> of the OUTGOING
    /// voucher. Deliberately not a new verb: an auditor reading the edit log must not have to know which of the
    /// two aggregates a voucher lived in to recognise that it was amended. The entry is appended past every
    /// guard and every throw — a REFUSED alteration logs nothing — and before the swap, so the snapshot is of the
    /// state the operator is leaving.</para>
    ///
    /// <para>⚠️ <b>NS-3 — CALL SITE 5.</b> Like its three siblings this does not block on negative stock: the
    /// alteration applies and <see cref="DetectNegativeStock"/> reports any shortfall the new figures introduced.
    /// An interactive caller must ask, exactly as the Post and Cancel screens do.</para>
    ///
    /// <para><b>What it REFUSES, each by name rather than applied silently</b> — the same four identity facts
    /// <c>LedgerService.Replace</c> refuses, for the same reasons: aliasing (handing the live voucher back as its
    /// own replacement defeats every guard below, because each would compare a value to itself), a changed
    /// <see cref="InventoryVoucher.Id"/> (the Guid is every order link's only handle), a changed
    /// <see cref="InventoryVoucher.TypeId"/> (the preserved number belongs to THAT type's sequence, and carrying
    /// it across would collide with the target type's own #n), a changed <see cref="InventoryVoucher.Number"/>,
    /// and a changed <see cref="InventoryVoucher.Cancelled"/> flag (that is <see cref="Cancel"/>'s verb).</para>
    /// </summary>
    /// <returns>The replacement, now on the book at the outgoing voucher's index.</returns>
    /// <exception cref="InvalidOperationException">The voucher is unknown, or the replacement changes one of the
    /// identity facts above, or it fails a posting invariant. In every case <b>the original is still on the book,
    /// unchanged, at its own index</b>, and nothing was written to the edit log.</exception>
    public InventoryVoucher Replace(Guid voucherId, InventoryVoucher replacement)
    {
        ArgumentNullException.ThrowIfNull(replacement);

        var existing = _company.FindInventoryVoucher(voucherId)
            ?? throw new InvalidOperationException($"Inventory voucher {voucherId} not found.");

        // 🔴 ALIASING — refused BEFORE anything else. Every guard below compares replacement.X to existing.X, and
        // aliasing makes all of them compare a value to itself. Measured on the accounting sibling, the same hole
        // renumbered a voucher, cancelled it and raised zero warnings; the pure-stock aggregate would additionally
        // have handed ReplaceInventoryVoucherInternal a list slot holding the object it was told to overwrite.
        if (ReferenceEquals(replacement, existing))
            throw new InvalidOperationException(
                $"Replace must be given a NEW inventory voucher instance: voucher {existing.Id} was passed as its "
                + "own replacement, which defeats every identity guard (id, type, number, cancelled) because each "
                + "would compare a value to itself. Build a replacement from the posted voucher's values.");

        // Every identity fact is captured into a LOCAL before it is compared, so no later mutation of `existing`
        // can move the thing a guard checks against.
        var existingId = existing.Id;
        var existingTypeId = existing.TypeId;
        var existingNumber = existing.Number;
        var existingCancelled = existing.Cancelled;

        if (replacement.Id != existingId)
            throw new InvalidOperationException(
                $"Replace must preserve the voucher's identity: inventory voucher {existingId} cannot be replaced "
                + $"by one carrying id {replacement.Id}. Order links and tracking-number links point at the "
                + "voucher by that Guid.");

        if (replacement.TypeId != existingTypeId)
            throw new InvalidOperationException(
                $"Replace does not retype a posted voucher: inventory voucher {existingId} is of type "
                + $"{existingTypeId} and the replacement asks for {replacement.TypeId}. "
                + "The preserved number belongs to the original type's numbering sequence and would collide with "
                + "the target type's own number. Delete it and enter a fresh voucher of the type you want.");

        // A caller that passes 0 (a freshly built replacement) inherits the original's number; one that passes
        // the voucher's OWN number (the shape a ForAlter rehydration produces) is accepted; a DIFFERENT number is
        // asking for a renumber, which alteration does not do.
        if (replacement.Number != 0 && replacement.Number != existingNumber)
            throw new InvalidOperationException(
                $"Replace preserves the voucher number: inventory voucher {existingId} is #{existingNumber} and "
                + $"the replacement asks for #{replacement.Number}. Renumbering a posted voucher is not part of "
                + "Alter.");

        if (replacement.Cancelled != existingCancelled)
            throw new InvalidOperationException(
                $"Replace does not cancel or un-cancel a voucher (inventory voucher {existingId}: "
                + $"{existingCancelled} -> {replacement.Cancelled}). Cancellation is Alt+X's verb and is recorded "
                + "as its own edit-log entry; build the replacement with the same flag.");

        var type = _company.FindVoucherType(replacement.TypeId)
            ?? throw new InvalidOperationException($"Unknown voucher type {replacement.TypeId}.");

        if (replacement.Date < _company.BooksBeginFrom)
            throw new InvalidOperationException(
                $"Voucher date {replacement.Date:yyyy-MM-dd} is before BooksBeginFrom "
                + $"{_company.BooksBeginFrom:yyyy-MM-dd}.");

        // 🔴 Validate BEFORE the book is touched, and UNDO the number stamp if validation refuses. The stamp
        // MUTATES the caller's object, so a rejected replacement handed back carrying the original's number would
        // re-post as a SECOND live voucher sharing that number if the operator corrected and accepted it as new —
        // the exact defect the accounting sibling records having measured.
        var incomingNumber = replacement.Number;
        replacement.Number = existingNumber;
        try
        {
            EnsureContentMatchesType(replacement, type);
            EnsureReferencesResolve(replacement);
            if (RequiresSourceDestinationBalance(type, replacement))
                EnsureStockJournalBalances(replacement);

            // Prevent Duplicate, mirroring Post. Its loop already skips `other.Id == voucher.Id`, so the voucher
            // being replaced cannot collide with itself — but a SIBLING of the same type that renders the same
            // string still bites, which is the case this guard is for.
            if (type.PreventDuplicate)
            {
                var rendered = VoucherNumberFormatter.Render(type, replacement.Number, replacement.Date);
                if (rendered.Length > 0)
                    foreach (var other in _company.InventoryVouchers)
                    {
                        if (other.Id == replacement.Id) continue;
                        if (other.TypeId != replacement.TypeId) continue;
                        if (string.Equals(
                                VoucherNumberFormatter.Render(type, other.Number, other.Date), rendered,
                                StringComparison.Ordinal))
                            throw new InvalidOperationException(
                                $"Voucher number '{rendered}' already exists for '{type.Name}' (Prevent "
                                + "Duplicates is on).");
                    }
            }
        }
        catch
        {
            replacement.Number = incomingNumber;
            throw;
        }

        // Past this point nothing throws, so the swap is safe. The log line goes first: the snapshot must be of
        // the voucher the operator is leaving, and `existing` is still the one on the book.
        var entry = RecordEdit(existing, VoucherEditVerb.Alter);

        try
        {
            _company.ReplaceInventoryVoucherInternal(existing, replacement);
        }
        catch
        {
            // The swap is the only remaining throw site (an `existing` that left the list between the lookup and
            // here). Unwinding the log entry keeps the append-only record from asserting an alteration that never
            // happened — the same lie DiscardUncommittedCancel exists to prevent.
            _company.RemoveLastVoucherEditLogEntryInternal(entry);
            throw;
        }

        return replacement;
    }

    /// <summary>
    /// Cancels a stock/order voucher (Alt+X): its effect drops to zero but its number is retained. ⚠️ NS-3 —
    /// CALL SITE 2 of 4: this used to be BLOCKED when removing the effect retro-drove a later movement negative.
    /// It no longer is; the cancel always applies, and the resulting shortfall (if any) is reported by
    /// <see cref="DetectNegativeStock"/>.
    /// </summary>
    /// <summary>
    /// ✅ <b>THE DECLARED EDIT-LOG GAP IS CLOSED, and the statement that stood here is kept rather than deleted
    /// so a reader can see what changed.</b> This summary used to read: <i>"DECLARED GAP — the pure-stock
    /// aggregate is NOT covered by the voucher edit log (schema v52) … cancelling or deleting a pure-stock
    /// voucher still leaves no record … What it would take: <c>VoucherSnapshot.Of</c> is typed to
    /// <see cref="Voucher"/> … so the snapshot needs a sibling overload; the table and the entry record need
    /// nothing new."</i> That was an exactly correct assessment and this slice acted on it:
    /// <c>VoucherSnapshot.Of(InventoryVoucher)</c> now exists, <b>no schema change was needed</b>
    /// (<c>voucher_edit_log.before_snapshot</c> is TEXT and <c>voucher_id</c> is deliberately not a foreign key),
    /// and <see cref="Cancel"/>/<see cref="Delete"/> append an entry exactly as <c>LedgerService</c> does.
    ///
    /// <para>🔴 <b>Why this had to be done in the SAME slice that gave these verbs a user route, not after it.</b>
    /// Until census rows 4.9–4.16 nothing in the Desktop could reach either verb, so the missing log recorded
    /// nothing that ever happened. Shipping the route first would have made a posted stock movement destroyable
    /// from the keyboard with no trace — strictly worse than the state the gap was declared in, where the act was
    /// merely impossible. A cancelled stock movement that leaves no record is worse than one that cannot be
    /// cancelled.</para>
    ///
    /// <para>Cancels a stock/order voucher (Alt+X): its effect drops to zero but its number is retained, and the
    /// returned <see cref="VoucherEditLogEntry"/> records the pre-cancel state. ⚠️ NS-3 — CALL SITE 2 of 4: this
    /// used to be BLOCKED when removing the effect retro-drove a later movement negative. It no longer is; the
    /// cancel always applies, and the resulting shortfall (if any) is reported by
    /// <see cref="DetectNegativeStock"/>.</para>
    ///
    /// <para><b>The log entry is appended BEFORE the flag is set</b>, mirroring <c>LedgerService.Cancel</c>: the
    /// snapshot must be the state the operator is leaving, and a "not found" throw must leave no entry behind.</para>
    /// </summary>
    public VoucherEditLogEntry Cancel(Guid voucherId)
    {
        var v = _company.FindInventoryVoucher(voucherId)
            ?? throw new InvalidOperationException($"Inventory voucher {voucherId} not found.");

        var entry = RecordEdit(v, VoucherEditVerb.Cancel);
        v.Cancelled = true;
        return entry;
    }

    /// <summary>
    /// Deletes a stock/order voucher (Alt+D), returning the <see cref="VoucherEditLogEntry"/> that records it —
    /// the ONLY surviving evidence the voucher ever existed, which is what makes the entry load-bearing here in a
    /// way it is not for <see cref="Cancel"/>. ⚠️ NS-3 — CALL SITE 3 of 4: this used to be BLOCKED when removing
    /// its (inward) effect retro-drove a later movement's on-hand negative. It no longer is; the delete always
    /// applies, and the resulting shortfall (if any) is reported by <see cref="DetectNegativeStock"/>.
    /// </summary>
    public VoucherEditLogEntry Delete(Guid voucherId)
    {
        var v = _company.FindInventoryVoucher(voucherId)
            ?? throw new InvalidOperationException($"Inventory voucher {voucherId} not found.");

        EnsureNothingStillReferences(v);

        var entry = RecordEdit(v, VoucherEditVerb.Delete);
        _company.RemoveInventoryVoucherInternal(v);
        return entry;
    }

    /// <summary>
    /// Asks <see cref="Delete"/>'s referential guard WITHOUT deleting anything, so a screen can refuse before it
    /// puts an irreversible Y/N confirmation on the operator's screen instead of after they answer it.
    ///
    /// <para><b>Why a public pre-ask rather than letting the shell catch <see cref="Delete"/>'s throw.</b> The
    /// throw is still the enforcement — every caller re-asks the rule immediately before the irreversible act,
    /// which is what makes it safe against a book that moved while a prompt was on screen. But a refusal that
    /// arrives only AFTER the operator has confirmed a deletion reads as a failure rather than as a rule, and the
    /// shell's post-confirmation catch appends "Re-open the company before continuing" — advice that is wrong
    /// here, because a refusal removes nothing. The accounting door pre-asks
    /// <c>MasterDeletionRules.EnsureVoucherDeletable</c> for exactly this reason; this is the pure-stock
    /// equivalent, and the two paths share one rule rather than two copies of it.</para>
    /// </summary>
    /// <exception cref="InvalidOperationException">The voucher is unknown, or a posted Material movement still
    /// links to it.</exception>
    public void EnsureDeletable(Guid voucherId)
    {
        var v = _company.FindInventoryVoucher(voucherId)
            ?? throw new InvalidOperationException($"Inventory voucher {voucherId} not found.");
        EnsureNothingStillReferences(v);
    }

    /// <summary>
    /// 🔴 <b>THE REFERENCED-MASTER GUARD ON <see cref="Delete"/> — the mirror of
    /// <see cref="EnsureReferencesResolve"/>, and it exists because the two were asymmetric.</b> Posting a
    /// Material movement REFUSES BY NAME when its <c>OrderLinks</c> do not resolve to a posted Job Work order,
    /// while <see cref="Delete"/> would happily remove the order out from under movements that link to it —
    /// leaving every one of them holding a dangling Guid, a state this engine's own <c>Post</c> declares invalid.
    /// An engine that refuses to CREATE a state must not be able to DELETE its way into it.
    ///
    /// <para>🔴 <b>AND THE CONSEQUENCE IS NOT COSMETIC.</b> Persistence here is delete-all-and-reinsert under
    /// <c>PRAGMA foreign_keys = ON</c>, so an orphan is not merely an ugly report: it can make the OPEN COMPANY
    /// UNSAVABLE, with the operator's only route out being to close without saving and lose the session. The
    /// Job Work Order Books also made Alt+D reachable from the surface where an operator would actually reach for
    /// it, which is what turned a latent hole into one worth closing.</para>
    ///
    /// <para><b>Named, not counted.</b> The refusal lists the movements by voucher number so the operator knows
    /// which entries to unlink or delete first, exactly as the master-deletion refusals do — a bare "it is in
    /// use" leaves them hunting.</para>
    /// </summary>
    /// <exception cref="InvalidOperationException">A posted Material movement still links to this voucher.</exception>
    private void EnsureNothingStillReferences(InventoryVoucher v)
    {
        // Only a Job Work ORDER can be the target of an OrderLinks reference, so nothing else pays for this scan.
        if (v.JobWorkOrder is null) return;

        var linked = _company.InventoryVouchers
            .Where(other => other.Id != v.Id && other.OrderLinks.Contains(v.Id))
            .ToList();
        if (linked.Count == 0) return;

        var names = string.Join(", ", linked.Select(m =>
        {
            var type = _company.FindVoucherType(m.TypeId);
            var rendered = type is null ? string.Empty : VoucherNumberFormatter.Render(type, m.Number, m.Date);
            if (rendered.Length == 0) rendered = $"#{m.Number}";
            return $"{type?.Name ?? "Material voucher"} No. {rendered}";
        }));

        throw new InvalidOperationException(
            $"This Job Work order cannot be deleted: {linked.Count} posted material "
            + $"movement{(linked.Count == 1 ? "" : "s")} still fulfil{(linked.Count == 1 ? "s" : "")} it "
            + $"({names}). Deleting it would leave {(linked.Count == 1 ? "that movement" : "those movements")} "
            + "linked to an order that no longer exists — a state posting refuses by name. Delete or re-key "
            + $"{(linked.Count == 1 ? "it" : "them")} first, or cancel this order with Alt+X instead, which keeps "
            + "the link intact.");
    }

    /// <summary>
    /// Drops <paramref name="entry"/> from the edit log because <b>the save that would have made its verb durable
    /// did not commit</b>. Refuses anything but the most recent entry. The pure-stock sibling of
    /// <c>LedgerService.DiscardUncommittedEditLogEntry</c>, which carries the full argument for why an
    /// append-only audit log has a removal at all; the short form is that this application's persistence is a
    /// whole-aggregate snapshot with no transaction spanning the engine and the store, so every lifecycle verb
    /// mutates the in-memory book BEFORE the save, and a screen that unwound the mutation without unwinding the
    /// log line would leave the log asserting an edit that never reached disk.
    ///
    /// <para>🔴 <b>WHY THIS EXISTS ON THIS SERVICE AND NOT ONLY ON <c>LedgerService</c>.</b>
    /// <see cref="Replace"/> appends an <see cref="VoucherEditVerb.Alter"/> entry, and the inventory alteration
    /// screen's rollback calls <see cref="Replace"/> a SECOND time to put the original back — which appends a
    /// second. Without this method that screen could unwind the swap but not the two log lines, so a later
    /// successful save in the same session persisted TWO fictitious alterations of a voucher nobody had altered.
    /// The accounting door has had the equivalent since v52; this is the pure-stock half, and a caller unwinding
    /// both verbs calls this twice, <b>newest first</b>.</para>
    /// </summary>
    /// <exception cref="InvalidOperationException"><paramref name="entry"/> is not the last entry in the log.</exception>
    public void DiscardUncommittedEditLogEntry(VoucherEditLogEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (!_company.RemoveLastVoucherEditLogEntryInternal(entry))
            throw new InvalidOperationException(
                $"Edit-log entry {entry.Id} is not the last entry and cannot be discarded.");
    }

    /// <summary>
    /// The compensating undo for a <see cref="Cancel"/> whose save did not commit: clears the flag AND discards
    /// the entry <see cref="Cancel"/> appended, in one call. The pure-stock sibling of
    /// <c>LedgerService.DiscardUncommittedCancel</c>, and it exists for the identical reason — a screen that
    /// rolled back by writing <c>voucher.Cancelled = false</c> itself would leave the log asserting a
    /// cancellation that never reached disk, which is the one lie an append-only audit record must not tell.
    /// </summary>
    /// <exception cref="InvalidOperationException">The voucher is unknown, <paramref name="entry"/> does not
    /// describe a <see cref="VoucherEditVerb.Cancel"/> of that voucher, or it is not the last log entry.</exception>
    public void DiscardUncommittedCancel(Guid voucherId, VoucherEditLogEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        if (entry.Verb != VoucherEditVerb.Cancel || entry.VoucherId != voucherId)
            throw new InvalidOperationException(
                $"Edit-log entry {entry.Id} ({entry.Verb} of {entry.VoucherId}) does not describe a "
                + $"Cancel of inventory voucher {voucherId}.");

        var v = _company.FindInventoryVoucher(voucherId)
            ?? throw new InvalidOperationException($"Inventory voucher {voucherId} not found.");

        // Order matters and is the opposite of Cancel's: the log line goes first, because its removal is the
        // bounded one (last-entry-only, enforced inside Company) and is the half that can legitimately refuse.
        // Clearing the flag first and then failing to remove the line would leave the two disagreeing.
        DiscardUncommittedEditLogEntry(entry);

        v.Cancelled = false;
    }

    /// <summary>
    /// Appends one <see cref="VoucherEditLogEntry"/> for <paramref name="verb"/> applied to
    /// <paramref name="before"/>, and returns it. The mirror of <c>LedgerService.RecordEdit</c>.
    ///
    /// <para><b>The timestamp comes from <see cref="DateTimeOffset.UtcNow"/> rather than from an injected
    /// clock, and that is a KNOWN ASYMMETRY with <c>LedgerService</c>, named here rather than left to be
    /// discovered.</b> <c>LedgerService</c> takes a <c>_now</c> delegate so its tests can pin a stamp; this
    /// service has no such constructor parameter and adding one would change a public signature that four
    /// call sites and a fixture loader use. The stamp is documented as "never used in any calculation"
    /// (<see cref="VoucherEditLogEntry.RecordedAt"/>), so no assertion needs to pin it — the tests for these
    /// verbs assert on the verb, the voucher id and the snapshot. If a future slice needs a pinned stamp here,
    /// the fix is the same optional constructor parameter, additively.</para>
    /// </summary>
    private VoucherEditLogEntry RecordEdit(InventoryVoucher before, VoucherEditVerb verb)
    {
        var entry = new VoucherEditLogEntry(
            Guid.NewGuid(), before.Id, verb, DateTimeOffset.UtcNow, VoucherSnapshot.Of(before));
        _company.AddVoucherEditLogEntryInternal(entry);
        return entry;
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
            // Census 4.7/4.8 (T0-10): a Debit Note is a purchase RETURN and moves stock OUTWARD, so it can
            // over-draw on-hand exactly like a Sales invoice and must face the same no-negative guard. Left at
            // Purchase-or-Sales, a return could drive a key negative and the guard would never look at it.
            if (type is null || !VoucherEffects.CanCarryItemInvoiceLines(type.BaseType)) continue;
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
