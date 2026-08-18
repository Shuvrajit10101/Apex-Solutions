using Apex.Ledger.Domain;

namespace Apex.Ledger.Services;

/// <summary>Which fact an alteration warning is about (see <see cref="VoucherAlterationWarning"/>).</summary>
public enum VoucherAlterationWarningCode
{
    /// <summary>A reconciled bank line's amount changed, so its bank date was cleared (design §3.4).</summary>
    BankDateCleared,

    /// <summary>A reconciled bank line is absent from the replacement, so its reconciliation is gone (§3.4).</summary>
    BankDateLineRemoved,

    /// <summary>The voucher's date moved — ORCHESTRATOR RULING 2 makes this warn-and-proceed, not refuse.</summary>
    DateChanged,
}

/// <summary>
/// Something an alteration did that the operator must be TOLD about but that does not refuse the alteration
/// (phase-10-11-voucher-lifecycle-design §3.4, ORCHESTRATOR RULING 2). Shaped like
/// <see cref="NegativeStockShortfall"/>: a code, the ids involved, and a ready-to-show <see cref="Message"/>.
///
/// <para>These exist because the alternative — doing the same thing silently — is the exact failure mode §3.4
/// documents: <c>Replace</c> implemented the obvious way destroys every bank reconciliation date on the
/// voucher <i>"with no message and no test failing"</i>.</para>
/// </summary>
public sealed record VoucherAlterationWarning(
    Guid VoucherId,
    VoucherAlterationWarningCode Code,
    string Message,
    Guid LedgerId = default,
    DateOnly? BankDate = null);

/// <summary>
/// The posting service (design §8.2). Validates §6 invariants then appends a voucher to
/// the company's posted set; rejects an unbalanced/malformed voucher (never persists it);
/// supports Cancel (Alt+X, keep number) and Delete (Alt+D, may gap numbering); and
/// assigns automatic numbers per voucher type.
/// </summary>
public sealed class LedgerService
{
    private readonly Company _company;

    public LedgerService(Company company)
        => _company = company ?? throw new ArgumentNullException(nameof(company));

    /// <summary>
    /// Validates invariants (§6), assigns a number for Automatic-numbered types when the
    /// voucher's number is unset, then appends it. Throws on any violation — a bad voucher
    /// is never persisted.
    /// <para><b>Item-invoice mode (slice 3.3b).</b> When the voucher carries
    /// <see cref="Voucher.InventoryLines"/> (a Purchase/Sales run in item-invoice mode), posting is
    /// <b>atomic across accounts and stock</b>: (a) the item lines' direction is stamped from the voucher nature
    /// (Purchase ⇒ inward, Sales ⇒ outward), (b) the balanced Dr/Cr legs are validated with the pairing
    /// invariant (§10), and (c) the resulting stock movement is verified against the no-negative-stock guard
    /// (DP-7). If the stock effect is invalid (e.g. a Sales item-invoice would drive an on-hand negative), the
    /// ENTIRE post fails — the voucher is removed and nothing (no accounting leg, no stock movement) persists.</para>
    /// </summary>
    public Voucher Post(Voucher voucher) => Post(voucher, CostAllocationStrictness.Strict);

    /// <summary>
    /// <see cref="Post(Voucher)"/> with an explicit cost-allocation invariant.
    /// <para><paramref name="costAllocationStrictness"/> is <see cref="CostAllocationStrictness.Strict"/>
    /// for every entry path. The two rehydration paths (<c>SqliteCompanyStore.Load</c> and company import)
    /// pass <see cref="CostAllocationStrictness.Legacy"/> so books written under the superseded partition
    /// rule still open — see that enum for why. The same flag also distinguishes entry from rehydration for the
    /// §10(4) "a composition dealer may not collect tax" guard (<see cref="VoucherValidator.EnsureValid(Voucher,
    /// Company, CostAllocationStrictness)"/>), which must refuse a new ENTRY without making an existing book
    /// unopenable.</para>
    /// </summary>
    public Voucher Post(Voucher voucher, CostAllocationStrictness costAllocationStrictness)
    {
        // Item-invoice mode: stamp the voucher-nature-implied direction on every item line BEFORE validating,
        // so the pairing check and the on-hand engine both read the canonical direction.
        StampInventoryLineDirections(voucher);

        VoucherValidator.EnsureValid(voucher, _company, costAllocationStrictness);

        var type = _company.FindVoucherType(voucher.TypeId)!;
        if (type.Numbering == NumberingMethod.Automatic && voucher.Number <= 0)
            voucher.Number = NextNumber(voucher.TypeId);

        _company.AddVoucherInternal(voucher);

        // ⚠️ NS-3: this used to append the voucher provisionally, run the no-negative guard over the book and
        // roll the WHOLE voucher back (accounting leg included) when an item-invoice Sales over-drew on-hand.
        // Negative stock is no longer blocked anywhere, so the append simply stands — an over-drawing sale posts,
        // and InventoryPostingService.DetectNegativeStock reports the shortfall. Atomicity is unaffected: there
        // is no longer a rejection here to be atomic about.
        return voucher;
    }

    /// <summary>
    /// Stamps each item-invoice line's <see cref="VoucherInventoryLine.Direction"/> from the voucher type's
    /// nature (Purchase ⇒ Inward, Sales ⇒ Outward). Only Purchase/Sales types are valid carriers; other types
    /// are left untouched here and rejected by the validator. Rebuilds the voucher's lines in place via the
    /// domain's own <see cref="VoucherInventoryLine.WithDirection"/> so the stored line is self-consistent.
    /// </summary>
    private void StampInventoryLineDirections(Voucher voucher)
    {
        if (!voucher.HasInventoryLines) return;
        var type = _company.FindVoucherType(voucher.TypeId);
        if (type is null) return; // referential integrity is reported by the validator

        StockDirection? dir = type.BaseType switch
        {
            VoucherBaseType.Purchase => StockDirection.Inward,
            VoucherBaseType.Sales => StockDirection.Outward,
            _ => null,
        };
        if (dir is not { } direction) return; // wrong carrier type — validator throws

        voucher.SetInventoryLineDirections(direction);
    }

    /// <summary>Alt+X — mark cancelled; keeps the number in sequence, zero effect on balances.
    /// ⚠️ NS-3: cancelling an item-invoice voucher reverses its stock effect, and that used to be BLOCKED when it
    /// retro-drove a later movement's on-hand negative (e.g. cancelling the purchase a later delivery drew from).
    /// It no longer is — the cancel always applies, and the shortfall is reported by
    /// <see cref="InventoryPostingService.DetectNegativeStock"/>.</summary>
    public void Cancel(Guid voucherId)
    {
        var v = _company.FindVoucher(voucherId)
            ?? throw new InvalidOperationException($"Voucher {voucherId} not found.");
        v.Cancelled = true;
    }

    /// <summary>Alt+D — remove entirely.
    /// <para>🔴 <b>WHAT THIS DOES TO NUMBERING — the doc comment used to say only "may leave a gap in numbering",
    /// which describes the mid-sequence case and MISSES the one that matters.</b> Deleting voucher #7 of 1…10
    /// leaves a permanent gap at 7 (<see cref="NextNumber"/> still reads max = 10). Deleting #10 — the
    /// <b>highest-numbered</b> voucher of its type — drops max to 9, so <b>the next post REUSES 10</b>: two
    /// different documents carrying the same number, with the first no longer on the books to prove which was
    /// which. This method does not guard it and deliberately does not: the guard is
    /// <see cref="MasterDeletionRules.EnsureVoucherDeletable"/>, which every UI delete route calls, and it refuses
    /// only the FILED statutory documents (plan.md §5, decision D-3). <b>Reuse on a non-filed top number is a
    /// KNOWN AND ACCEPTED behaviour</b>, pinned by a test so it stays recorded rather than rediscovered. Any new
    /// caller of this method must call that guard first.</para>
    /// ⚠️ NS-3: deleting an item-invoice voucher reverses its stock effect, and that used to be BLOCKED when it
    /// retro-drove a later movement's on-hand negative. It no longer is — the delete always applies, and the
    /// shortfall is reported by <see cref="InventoryPostingService.DetectNegativeStock"/>.</summary>
    public void Delete(Guid voucherId)
    {
        var v = _company.FindVoucher(voucherId)
            ?? throw new InvalidOperationException($"Voucher {voucherId} not found.");
        _company.RemoveVoucherInternal(v);
    }

    /// <summary>
    /// <b>Alter</b> — replaces the posted voucher <paramref name="voucherId"/> with
    /// <paramref name="replacement"/>, <b>in place</b> (phase-10-11-voucher-lifecycle-design §6.5, slice S5a).
    /// Returns the accepted replacement, which is now the instance on the book.
    ///
    /// <para><b>Why the signature takes a fully-constructed <see cref="Voucher"/> and not a mutator delegate.</b>
    /// <see cref="Voucher.Date"/> and <see cref="Voucher.TypeId"/> are get-only, so a date change — which
    /// ORCHESTRATOR RULING 2 makes warn-and-proceed, not refuse — cannot be done by mutating the posted
    /// voucher. It forces construction of a NEW voucher carrying the SAME <see cref="Guid"/>. That is not a
    /// stylistic choice and must not be "improved" into <c>Alter(Guid, Action&lt;Voucher&gt;)</c>.</para>
    ///
    /// <para><b>The five contract clauses (§6.5), each with its reason:</b>
    /// <list type="number">
    /// <item><b>Validate the replacement BEFORE removing the original.</b> <see cref="Post(Voucher)"/> MUTATES
    /// before it can fail — it stamps item-line directions, then assigns <see cref="Voucher.Number"/>, then
    /// appends — so the obvious remove-then-post implementation DESTROYS the original when the replacement is
    /// rejected. The precedent already in this file is <see cref="ConvertToRegular"/>, which posts first and
    /// removes second. Here: validate, then swap.</item>
    /// <item><b>The <see cref="Guid"/> is preserved</b> — 25+ tables <c>REFERENCES vouchers(id)</c>, and the
    /// §34 credit-note link, the e-invoice record, the e-way bill and the TDS/TCS challan links are all Guid
    /// pointers. The Guid is the only thing holding the outside-world links together, so a replacement whose
    /// Id differs is REFUSED rather than silently re-keyed.</item>
    /// <item><b>The <see cref="Voucher.Number"/> is preserved.</b> <see cref="Post(Voucher)"/> assigns when
    /// <c>Number &lt;= 0</c>, so a replacement carrying 0 would silently renumber a mid-sequence voucher to
    /// max+1. The original's number is copied on BEFORE validation (the Prevent-Duplicate check renders it).</item>
    /// <item><b>The list index is preserved</b> — see <c>Company.ReplaceVoucherInternal</c>. The index is
    /// persisted (<c>ORDER BY rowid</c>) and is therefore a real, user-visible property: the Day Book order of
    /// same-dated vouchers.</item>
    /// <item>Together with the get-only <c>Date</c>/<c>TypeId</c>, the above is the whole engine contract.</item>
    /// </list></para>
    ///
    /// <para><b>🔴 The bank reconciliation date is CARRIED, not re-derived (§3.4).</b>
    /// <c>BankReconciliation.SetBankDate</c> writes <see cref="BankAllocation.BankDate"/> onto a POSTED line;
    /// it is a fact written onto the voucher graph by a later human action and it exists NOWHERE in the
    /// voucher entry screen. A replacement rebuilt from an entry screen therefore arrives with it blank, and
    /// a naive swap would silently un-reconcile a bank line a human had ticked. This method carries the date
    /// forward for every line whose ledger + bank-instrument identity is unchanged AND whose amount and side
    /// are unchanged, and clears it — <b>with a warning, never silently</b> — when the amount moved, because a
    /// cleared item that no longer matches the statement is not cleared.</para>
    ///
    /// <para><b>What this method deliberately does NOT do (§6.6/§6.7).</b> No GST re-stamp, no TDS re-carve,
    /// no CARRY table for the other eleven voucher-attached records, no <c>ForAlter</c> rehydration, no UI, no
    /// audit trail, no un-cancel, no schema change. Those are S5b/S5c. Everything on
    /// <paramref name="replacement"/> other than the bank date is taken exactly as the caller built it.</para>
    /// </summary>
    /// <exception cref="InvalidOperationException">The voucher is unknown, or the replacement changes its
    /// <see cref="Voucher.Id"/>, <see cref="Voucher.TypeId"/>, <see cref="Voucher.Number"/> or
    /// <see cref="Voucher.Cancelled"/> flag — each refused by name rather than applied silently.</exception>
    /// <exception cref="InvalidVoucherException">The replacement violates a §6 posting invariant. The original
    /// is still on the book, unchanged, at its own index.</exception>
    /// <exception cref="UnbalancedVoucherException">The replacement does not balance. Same guarantee.</exception>
    public Voucher Replace(Guid voucherId, Voucher replacement)
        => Replace(voucherId, replacement, out _);

    /// <summary>
    /// <see cref="Replace(Guid, Voucher)"/>, surfacing the warnings the alteration raised — today, the §3.4
    /// bank-date clears and the RULING 2 date change. <b>Any interactive caller must use this overload</b>: a
    /// bank date dropped without the operator being told is precisely the silent defect §3.4 exists to prevent.
    /// The two-argument overload is the published contract and exists for callers (tests, batch fixups) that
    /// have already established there is nothing to warn about.
    /// </summary>
    public Voucher Replace(Guid voucherId, Voucher replacement, out IReadOnlyList<VoucherAlterationWarning> warnings)
    {
        ArgumentNullException.ThrowIfNull(replacement);

        var existing = _company.FindVoucher(voucherId)
            ?? throw new InvalidOperationException($"Voucher {voucherId} not found.");

        // Clause 2 — the Guid is the outside world's only handle on this voucher.
        if (replacement.Id != existing.Id)
            throw new InvalidOperationException(
                $"Replace must preserve the voucher's identity: voucher {existing.Id} cannot be replaced by "
                + $"one carrying id {replacement.Id}. Every credit/debit-note link, e-invoice record, e-way "
                + "bill and challan link points at the voucher by that Guid.");

        // The voucher TYPE is the numbering sequence the preserved number belongs to. Changing it would carry
        // a number out of its own sequence — and straight into a collision with the target type's own #n.
        // S5a refuses it BY NAME; if a later slice wants a type change it must renumber deliberately.
        if (replacement.TypeId != existing.TypeId)
            throw new InvalidOperationException(
                $"Replace does not change a voucher's type (voucher {existing.Id}: {existing.TypeId} -> "
                + $"{replacement.TypeId}). The preserved number belongs to the original type's sequence.");

        // Cancellation is Cancel's verb, not Alter's — and un-cancel is out of scope for this phase (§6.7).
        // Refusing here stops Replace becoming a silent back door to either.
        if (replacement.Cancelled != existing.Cancelled)
            throw new InvalidOperationException(
                $"Replace does not change a voucher's cancelled status (voucher {existing.Id}). "
                + "Use Cancel to cancel; un-cancel is not supported.");

        // Clause 3 — preserve the number, and refuse a renumber rather than discard it silently. A caller that
        // passes 0 (a freshly built replacement) simply inherits the original's number; a caller that passes a
        // DIFFERENT number is asking for something S5a does not do, and is told so.
        if (replacement.Number != 0 && replacement.Number != existing.Number)
            throw new InvalidOperationException(
                $"Replace preserves the voucher number: voucher {existing.Id} is #{existing.Number} and the "
                + $"replacement asks for #{replacement.Number}. Renumbering a posted voucher is not part of Alter.");
        replacement.Number = existing.Number;

        // Clause 1 — validate BEFORE the book is touched. Stamp first, exactly as Post does, so the pairing
        // check and the on-hand engine read the canonical direction.
        StampInventoryLineDirections(replacement);
        VoucherValidator.EnsureValid(replacement, _company, CostAllocationStrictness.Strict);

        // Past this point nothing can throw, so the swap is safe.
        var raised = new List<VoucherAlterationWarning>();
        CarryBankDatesForward(existing, replacement, raised);

        if (replacement.Date != existing.Date)
            raised.Add(new VoucherAlterationWarning(
                existing.Id,
                VoucherAlterationWarningCode.DateChanged,
                $"Voucher date changed from {existing.Date:dd-MMM-yyyy} to {replacement.Date:dd-MMM-yyyy}."));

        // Clause 4 — swap at the index; never Remove + Add.
        _company.ReplaceVoucherInternal(existing, replacement);

        warnings = raised;
        return replacement;
    }

    /// <summary>
    /// Carries <see cref="BankAllocation.BankDate"/> from the outgoing voucher onto the replacement (§3.4).
    ///
    /// <para>Old and new bank lines are paired on <b>ledger + bank-instrument identity</b> (transaction type,
    /// instrument number, instrument date) in list order, each old line consumed at most once. For a matched
    /// pair the reconcile tick is carried when the line's amount AND side are unchanged, and dropped with a
    /// warning when they are not. A reconciled old bank line with no counterpart in the replacement also
    /// warns — the line was removed, and with it a reconciliation somebody performed.</para>
    ///
    /// <para>A replacement line that already carries its own <see cref="BankAllocation.BankDate"/> is left
    /// alone: the caller stated it, and it still consumes its match so the removal warning does not misfire.</para>
    /// </summary>
    private static void CarryBankDatesForward(
        Voucher existing, Voucher replacement, List<VoucherAlterationWarning> warnings)
    {
        var oldLines = existing.Lines;
        var consumed = new bool[oldLines.Count];

        foreach (var newLine in replacement.Lines)
        {
            if (newLine.BankAllocation is not { } newAllocation) continue;

            var matchIndex = -1;
            for (var i = 0; i < oldLines.Count; i++)
            {
                if (consumed[i]) continue;
                var candidate = oldLines[i];
                if (candidate.BankAllocation is not { } oldAllocation) continue;
                if (candidate.LedgerId != newLine.LedgerId) continue;
                if (!SameBankInstrument(oldAllocation, newAllocation)) continue;
                matchIndex = i;
                break;
            }

            if (matchIndex < 0) continue;
            consumed[matchIndex] = true;

            var matched = oldLines[matchIndex];
            if (matched.BankAllocation!.BankDate is not { } bankDate) continue;  // nothing was reconciled
            if (newAllocation.BankDate is not null) continue;                    // the caller stated a date

            if (matched.Amount == newLine.Amount && matched.Side == newLine.Side)
            {
                newAllocation.BankDate = bankDate;                               // CARRY
                continue;
            }

            warnings.Add(new VoucherAlterationWarning(
                existing.Id,
                VoucherAlterationWarningCode.BankDateCleared,
                $"Bank reconciliation date {bankDate:dd-MMM-yyyy} was cleared on the bank line for ledger "
                + $"{newLine.LedgerId}: the line amount changed from {matched.Amount} to {newLine.Amount}. "
                + "A cleared item that no longer matches the statement is not cleared — reconcile it again.",
                newLine.LedgerId,
                bankDate));
        }

        for (var i = 0; i < oldLines.Count; i++)
        {
            if (consumed[i]) continue;
            var dropped = oldLines[i];
            if (dropped.BankAllocation?.BankDate is not { } bankDate) continue;

            warnings.Add(new VoucherAlterationWarning(
                existing.Id,
                VoucherAlterationWarningCode.BankDateLineRemoved,
                $"The reconciled bank line for ledger {dropped.LedgerId} (bank date {bankDate:dd-MMM-yyyy}) is "
                + "not present in the replacement; its reconciliation is gone.",
                dropped.LedgerId,
                bankDate));
        }
    }

    /// <summary>
    /// Two bank allocations describe the SAME instrument — the identity a reconcile tick belongs to. The bank
    /// date itself is deliberately not part of the comparison (it is the thing being carried).
    /// </summary>
    private static bool SameBankInstrument(BankAllocation a, BankAllocation b) =>
        a.TransactionType == b.TransactionType
        && string.Equals(a.InstrumentNumber, b.InstrumentNumber, StringComparison.Ordinal)
        && a.InstrumentDate == b.InstrumentDate;

    /// <summary>
    /// Converts a <b>Memorandum</b> voucher (a non-affecting suspense entry, catalog §7) into a real
    /// voucher of <paramref name="targetTypeId"/> so it now affects the books. The memo voucher is
    /// removed and a fresh voucher — same date, party, narration, and entry lines, but the chosen type —
    /// is posted through the normal validating path (so it must balance). The new voucher keeps a fresh
    /// id and takes an automatic number for its target type; its <c>Optional</c>/<c>PostDated</c> flags
    /// are cleared (a regularised entry is a real one). Returns the newly posted voucher.
    /// </summary>
    /// <exception cref="InvalidOperationException">The voucher is unknown, is not a Memorandum, or the
    /// target voucher type is unknown.</exception>
    public Voucher ConvertToRegular(Guid memorandumVoucherId, Guid targetTypeId)
    {
        var memo = _company.FindVoucher(memorandumVoucherId)
            ?? throw new InvalidOperationException($"Voucher {memorandumVoucherId} not found.");

        var sourceType = _company.FindVoucherType(memo.TypeId)
            ?? throw new InvalidOperationException($"Voucher {memorandumVoucherId} has unknown type {memo.TypeId}.");
        if (sourceType.BaseType != VoucherBaseType.Memorandum)
            throw new InvalidOperationException(
                $"Voucher {memorandumVoucherId} is a '{sourceType.Name}', not a Memorandum; only memoranda are converted.");

        if (_company.FindVoucherType(targetTypeId) is null)
            throw new InvalidOperationException($"Target voucher type {targetTypeId} not found.");

        var regular = new Voucher(
            Guid.NewGuid(),
            targetTypeId,
            memo.Date,
            memo.Lines,          // same balanced lines
            number: 0,           // take a fresh automatic number for the target type
            narration: memo.Narration,
            partyId: memo.PartyId,
            cancelled: false,
            optional: false,     // a regularised entry affects the real books
            postDated: false,
            applicableUpto: null);

        // Post first (validates); only remove the memo once the real voucher is accepted.
        Post(regular);
        _company.RemoveVoucherInternal(memo);
        return regular;
    }

    /// <summary>Next automatic number for a voucher type = max existing + 1 (per type, per company).
    /// <para><b>Computed by SCANNING the posted vouchers</b> — there is no stored counter and no
    /// <c>last_used_number</c> column anywhere in the schema, so this is not monotone across a
    /// <see cref="Delete"/>: removing the highest-numbered voucher of a type lowers <c>max</c> and this method
    /// hands the same number out again. See <see cref="Delete"/> for the full statement and
    /// <see cref="MasterDeletionRules"/> for the guard that bounds it.</para></summary>
    public int NextNumber(Guid voucherTypeId)
    {
        var max = 0;
        foreach (var v in _company.Vouchers)
            if (v.TypeId == voucherTypeId && v.Number > max)
                max = v.Number;
        return max + 1;
    }
}
