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

    /// <summary>
    /// The <b>rendered</b> voucher number moved even though the integer <see cref="Voucher.Number"/> did not
    /// (§6.5 clause 3). <c>VoucherNumberFormatter</c> selects the prefix/suffix by voucher DATE, so a date change
    /// silently rewrites the PRINTED document number — the string the outside world uses
    /// (<c>EInvoiceRecord.DocumentNumberUpper</c>, the Prevent-Duplicate comparison, the printed invoice).
    /// Clause 3 preserves the int; this warning covers the half of the number it does not.
    /// </summary>
    RenderedNumberChanged,

    // 🔴 There is deliberately NO ProvisionalStateChanged code. Optional / PostDated / ApplicableUpto are
    // REFUSED by Replace, by name (see the §7.4 guard in Replace) — a warning code for them would imply the
    // move is something Replace performs. It is not. See design §12.8.

    /// <summary>
    /// A statutory record stored BESIDE the voucher (§3.3) now MISSTATES it. The record is carried for free by the
    /// preserved <see cref="Voucher.Id"/>, which is exactly the problem: it still resolves, and it still declares
    /// the pre-alteration figure or date. §3.3 calls the e-Way case <i>"the highest silent-divergence risk in the
    /// phase"</i> and assigns it CARRY + <b>WARN</b>; this is the WARN.
    /// </summary>
    StatutoryRecordDiverged,
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
        ArgumentNullException.ThrowIfNull(voucher);

        // 🔴 THE VOUCHER GUID IS UNIQUE ACROSS BOTH AGGREGATES — and until this guard, nothing said so.
        // `Replace`'s clause 2 rests on the Guid being "the outside world's only handle on this voucher": 25+ tables
        // REFERENCES vouchers(id), and every §3.3 record points at the voucher by it. Measured without this guard, a
        // second voucher carrying an ALREADY-USED Guid posted without complaint, after which `Company.FindVoucher`
        // (a FirstOrDefault) could only ever see the first of them — so `Replace` silently altered one of two
        // vouchers sharing an id. The CROSS-aggregate half is what makes "Replace cannot reach an InventoryVoucher"
        // true: post a pure-stock voucher carrying an accounting voucher's Guid and one Guid names two different
        // things. Both halves are refused here and in InventoryPostingService.Post, the two entry doors.
        // (The rehydration doors — AddVoucherInternal / AddInventoryVoucher — are NOT guarded: the store's own
        // PRIMARY KEY is the uniqueness authority there. Recorded as a declared gap on Replace.)
        EnsureVoucherIdIsFree(_company, voucher.Id);

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
    /// persisted (<c>ORDER BY rowid</c>) and is therefore a real, user-visible property: it is the
    /// <b>rehydration order</b>, and it surfaces in <c>Outstandings</c>, which walks <c>company.Vouchers</c> in
    /// list order <i>"preserving first-seen order"</i> (<c>Outstandings.cs line 129-131</c>) and so decides the
    /// order open bills are listed in.
    /// <para>⚠️ <b>CORRECTED (S5a review).</b> This clause used to justify itself as <i>"the Day Book order of
    /// same-dated vouchers"</i>. That is FALSE and falsifiable in one test: <c>DayBook.cs line 52-56</c> sorts by
    /// (Date, Number) and never reads the list index — move a voucher to the end of the list and the Day Book is
    /// unchanged. The clause is still right; only its stated reason was wrong, and a wrong reason is what gets a
    /// future maintainer to conclude the index does not matter.</para></item>
    /// <item>Together with the get-only <c>Date</c>/<c>TypeId</c>, the above is the whole engine contract.</item>
    /// </list></para>
    ///
    /// <para><b>🔴 The bank reconciliation date is CARRIED, not re-derived (§3.4).</b>
    /// <c>BankReconciliation.SetBankDate</c> writes <see cref="BankAllocation.BankDate"/> onto a POSTED line;
    /// it is a fact written onto the voucher graph by a later human action and it exists NOWHERE in the
    /// voucher entry screen. A replacement rebuilt from an entry screen therefore arrives with it blank, and
    /// a naive swap would silently un-reconcile a bank line a human had ticked. This method carries the date
    /// forward for every line whose ledger + bank-instrument identity is unchanged AND whose amount and side
    /// are unchanged, and clears it — <b>with a warning, never silently</b> — when either moved, because a
    /// cleared item that no longer matches the statement is not cleared.</para>
    ///
    /// <para><b>🔴 The other eleven §3.3 records are CARRIED FOR FREE — and Replace makes NO statement that they
    /// still DESCRIBE the voucher.</b> This paragraph used to read <i>"no CARRY table for the other eleven
    /// voucher-attached records"</i>, which reads as "they are not carried". Measured, all twelve families resolve
    /// after a Replace, by <see cref="Voucher.Id"/>, exactly as clause 2 intends — <b>and three of them then LIE</b>:
    /// <c>EWayBillRecord.ConsignmentValuePaisa</c>, <c>GstCreditDebitNoteLink.OriginalInvoiceDate</c> (the frozen
    /// basis for the §34(2) 30-Nov cut-off) and <c>RcmDocument.DocDate</c> are all frozen snapshots of a voucher that
    /// just moved. <see cref="VoucherAlterationWarningCode.StatutoryRecordDiverged"/> is raised for each one that
    /// diverges (§3.3's CARRY + WARN), plus the e-invoice and the GSTR-1 Table-11A advance receipt. <b>A warning is
    /// not a refusal</b>: the <c>EInvoiceStatus.Generated</c> REFUSAL is §6.6's, and belongs to S5b.</para>
    ///
    /// <para><b>What this method deliberately does NOT do (§6.6/§6.7).</b> No GST re-stamp, no TDS re-carve, no
    /// <c>ForAlter</c> rehydration, no UI, no audit trail, no un-cancel, no schema change, and <b>no statutory
    /// REFUSAL of any kind</b>. Those are S5b/S5c. Everything on <paramref name="replacement"/> other than the bank
    /// date is taken exactly as the caller built it.</para>
    ///
    /// <para><b>Declared gaps carried by this slice</b> (recorded here so they are carried rather than
    /// rediscovered):
    /// <list type="bullet">
    /// <item><b>Stamped-vs-posted line tax is not policed — on EITHER path.</b> A replacement whose lines carry a
    /// stale <c>EntryLine.Gst</c> taxable value is accepted, and GSTR-1/3B then declare that stale value rather than
    /// the posted amounts (§3.2's "copy the old lines forward" trap). <b>This is not an S5a regression:</b>
    /// <see cref="Post(Voucher)"/> accepts the byte-identical shape on a fresh Guid — both go through the same
    /// <c>VoucherValidator.EnsureValid</c>, which cross-checks neither. Naming it here because S5b's <c>ForAlter</c>
    /// rehydration is precisely the caller that produces the shape: it must RE-DERIVE the line tax, never echo it.</item>
    /// <item><b>Prevent Duplicate on a book that already holds a duplicate.</b> The scan is narrowed on this path so
    /// a collision the alteration did NOT create cannot make a voucher permanently unalterable — see
    /// <c>VoucherValidator.EnsureValid</c>'s <c>replacing</c> parameter.</item>
    /// <item><b>Guid uniqueness</b> is enforced by <see cref="Post(Voucher)"/> / <c>InventoryPostingService.Post</c>
    /// but NOT by the rehydration paths (<c>Company.AddVoucherInternal</c> / <c>AddInventoryVoucher</c>), so clause
    /// 2's "the Guid is the outside world's only handle" rests on those two posting guards, not on the aggregate.</item>
    /// </list></para>
    /// </summary>
    /// <exception cref="InvalidOperationException">The voucher is unknown, the replacement IS the posted voucher
    /// (aliasing — see below), or the replacement changes its <see cref="Voucher.Id"/>,
    /// <see cref="Voucher.TypeId"/>, <see cref="Voucher.Number"/>, <see cref="Voucher.Cancelled"/> flag,
    /// <see cref="Voucher.IsAccountingInvoice"/> flag or its <b>provisional-state vector</b>
    /// (<see cref="Voucher.Optional"/> / <see cref="Voucher.PostDated"/> / <see cref="Voucher.ApplicableUpto"/>)
    /// — each refused by name rather than applied silently.
    /// <para><b>🔴 Why aliasing is refused first.</b> Every identity guard below is a comparison between
    /// <c>replacement.X</c> and <c>existing.X</c>. Hand the method the LIVE posted voucher as its own replacement
    /// and every one of those comparisons compares a value to ITSELF: measured, <c>Replace(id, live)</c> after
    /// setting <c>live.Number = 99; live.Cancelled = true; live.Optional = true</c> renumbered #10 to #99, cancelled
    /// it, made it Optional, raised ZERO warnings and pushed <c>NextNumber</c> from 12 to 100. Clause 5 already
    /// assumes a NEW instance (that is why <c>Date</c>/<c>TypeId</c> are get-only); the guard makes the assumption
    /// enforceable instead of merely stated.</para></exception>
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

        var existing = _company.FindVoucher(voucherId) ?? throw NotFound(voucherId);

        // 🔴 ALIASING — refused BEFORE anything else, because every guard below compares replacement.X to
        // existing.X and aliasing makes all of them compare a value to itself. See the <exception> note.
        if (ReferenceEquals(replacement, existing))
            throw new InvalidOperationException(
                $"Replace must be given a NEW voucher instance: voucher {existing.Id} was passed as its own "
                + "replacement, which defeats every identity guard (id, type, number, cancelled) because each "
                + "would compare a value to itself. Build a replacement from the posted voucher's values.");

        // Every identity fact is captured into a LOCAL before it is compared, so no later mutation of `existing`
        // (or of a future aliasing route) can move the thing a guard is checking against.
        var existingId = existing.Id;
        var existingTypeId = existing.TypeId;
        var existingNumber = existing.Number;
        var existingCancelled = existing.Cancelled;
        var existingIsAccountingInvoice = existing.IsAccountingInvoice;
        var existingOptional = existing.Optional;
        var existingPostDated = existing.PostDated;
        var existingApplicableUpto = existing.ApplicableUpto;

        // Clause 2 — the Guid is the outside world's only handle on this voucher.
        if (replacement.Id != existingId)
            throw new InvalidOperationException(
                $"Replace must preserve the voucher's identity: voucher {existing.Id} cannot be replaced by "
                + $"one carrying id {replacement.Id}. Every credit/debit-note link, e-invoice record, e-way "
                + "bill and challan link points at the voucher by that Guid.");

        // The voucher TYPE is the numbering sequence the preserved number belongs to. Changing it would carry
        // a number out of its own sequence — and straight into a collision with the target type's own #n. That
        // collision is REAL and PERMANENT, not speculative: measured with the guard disabled, retyping Sales #2
        // to Purchase in a book holding Purchase #1-3 left TWO live Purchase vouchers numbered 2, a permanent
        // hole at Sales #2, and NextNumber unaware of either. S5a refuses it BY NAME, and names the remedy,
        // because what it blocks is the legitimate "keyed under the wrong type" correction.
        if (replacement.TypeId != existingTypeId)
            throw new InvalidOperationException(
                $"Replace does not change a voucher's type (voucher {existingId}: {existingTypeId} -> "
                + $"{replacement.TypeId}). The preserved number belongs to the original type's sequence, and "
                + "carrying it across would collide with the target type's own voucher of that number. Delete "
                + "this voucher and re-enter it under the correct type.");

        // Cancellation is Cancel's verb, not Alter's — and un-cancel is out of scope for this phase (§6.7).
        // Refusing here stops Replace becoming a silent back door to either. NOT over-broad: a CANCELLED voucher
        // can still be altered, provided the caller carries the flag (pinned by its own test).
        if (replacement.Cancelled != existingCancelled)
            throw new InvalidOperationException(
                $"Replace does not change a voucher's cancelled status (voucher {existingId}). "
                + "Use Cancel to cancel; un-cancel is not supported.");

        // 🔴 §7.4 — the PROVISIONAL-STATE VECTOR: Optional, PostDated, ApplicableUpto. Refused for the IDENTICAL
        // reason Cancelled is, one guard above: Replace is for CONTENT, and all three of these are LIFECYCLE
        // STATE. It must not be a back door to Ctrl+L any more than to Cancel. LedgerBalances.CountsAsOf reads
        // Optional exactly as it reads Cancelled (LedgerBalance.cs line 47 is literally
        // `if (v.Cancelled || v.Optional) return false;`) and ApplicableUpto is what makes a Reversing Journal
        // lapse (LedgerBalance.cs line 78) — so moving one moves live balances by the WHOLE voucher without a
        // single figure ON the voucher changing.
        //
        // MEASURED, every one of them on byte-identical amounts: an Optional voucher whose replacement left the
        // flag at its default became a real posting and swung the Sales closing by ₹1,84,733.45; the mirror
        // dropped the same ₹1,84,733.45 out of the live books; a Reversing Journal that lost ApplicableUpto to a
        // narration-only alteration NEVER LAPSED (the scenario figure at 01-May moved 0 to 3,000).
        //
        // WHY NOT carry-when-default, the obvious alternative: Optional and PostDated are BOOLS, so "left at the
        // default" and "explicitly set to false" are INDISTINGUISHABLE. Carrying-when-default would therefore make
        // it impossible to turn an Optional voucher live — it would silently ignore a real operator intent. That
        // ambiguity is exactly why the §3.4 bank-date ECHO rule works where this would not: BankDate is DateOnly?,
        // so null genuinely means "not stated". ApplicableUpto IS nullable and could have been carried that way,
        // but split behaviour across one conceptual vector is worse than one rule — so all three are refused alike.
        //
        // WHY REFUSE RATHER THAN WARN: warn-and-proceed cannot distinguish "the operator pressed Ctrl+L" from "the
        // caller forgot to carry the flag", and the caller that will produce exactly that second shape is S5b's
        // ForAlter rehydration. A refusal turns a silent balance corruption into a compile- or test-time failure
        // the moment S5b gets it wrong — the same standard that already binds ForAlter to RE-DERIVE line tax
        // rather than echo it (§12.2 item 3).
        //
        // 🔴 R7 FIDELITY, recorded honestly — this is OUR DELIBERATE NARROWING OF AN ATTESTED BEHAVIOUR, and NOT
        // a "corpus silent" case. TallyPrime genuinely does attest Ctrl+L (Optional) and Ctrl+T (Post-Dated) as
        // ALTERATION-TIME verbs, so refusing the change here is an infidelity, and it is taken deliberately: our
        // Replace is a general-purpose ENGINE PRIMITIVE with no operator intent attached to it, not a screen. The
        // toggle belongs on its own verb; a UI that wants Ctrl+L / Ctrl+T must call THAT verb rather than Replace.
        // Recorded as a divergence in design §12.8.
        if (replacement.Optional != existingOptional
            || replacement.PostDated != existingPostDated
            || replacement.ApplicableUpto != existingApplicableUpto)
        {
            static string Flag(bool set, string on) => set ? on : "live";
            static string Day(DateOnly? d) => d is { } value ? value.ToString("dd-MMM-yyyy") : "(none)";

            var moved = new List<string>(3);
            if (replacement.Optional != existingOptional)
                moved.Add($"Optional changed from {Flag(existingOptional, "Optional")} to "
                    + Flag(replacement.Optional, "Optional"));
            if (replacement.PostDated != existingPostDated)
                moved.Add($"Post-dated changed from {Flag(existingPostDated, "post-dated")} to "
                    + Flag(replacement.PostDated, "post-dated"));
            if (replacement.ApplicableUpto != existingApplicableUpto)
                moved.Add($"Applicable Upto changed from {Day(existingApplicableUpto)} to "
                    + Day(replacement.ApplicableUpto));

            throw new InvalidOperationException(
                $"Replace does not change a voucher's provisional state (voucher {existingId}: "
                + $"{string.Join("; ", moved)}). Optional, Post-dated and Applicable Upto decide whether the "
                + "voucher affects live balances AT ALL, so moving one moves the books by the WHOLE voucher even "
                + "though no figure on it need have changed. Build the replacement carrying the posted voucher's "
                + "Optional, PostDated and ApplicableUpto; marking a voucher Optional or Post-dated is its own "
                + "verb (Ctrl+L / Ctrl+T), and a screen that wants it must call that verb, not Replace.");
        }

        // Voucher.IsAccountingInvoice is get-only WITH A WRITTEN REASON — "the printed document type of an issued
        // invoice must not be flippable after the fact". Clause 5's whole premise (construct a NEW voucher) is the
        // door that immutability left open, and measured, Replace flipped it in BOTH directions, silently: with it
        // the ledger-only Sales voucher projects as a Rule-46 TAX INVOICE, without it as a plain Dr/Cr voucher
        // (GstReportSupport.IsServiceAccountingInvoice reads it as "the whole gate"). Refused by name, exactly as
        // Cancelled is, because it is the same category of fact: what the operator DID at posting time.
        if (replacement.IsAccountingInvoice != existingIsAccountingInvoice)
            throw new InvalidOperationException(
                $"Replace does not change whether a voucher is an Accounting Invoice (voucher {existingId}: "
                + $"{existingIsAccountingInvoice} -> {replacement.IsAccountingInvoice}). The printed document type "
                + "of an issued invoice is fixed at posting time; build the replacement with the same flag.");

        // Clause 3 — preserve the number, and refuse a renumber rather than discard it silently. A caller that
        // passes 0 (a freshly built replacement) simply inherits the original's number; a caller that passes the
        // voucher's OWN number (the shape an S5b ForAlter rehydration produces, since rehydrating reads the Number
        // back) is ACCEPTED; a caller that passes a DIFFERENT number is asking for something S5a does not do.
        if (replacement.Number != 0 && replacement.Number != existingNumber)
            throw new InvalidOperationException(
                $"Replace preserves the voucher number: voucher {existingId} is #{existingNumber} and the "
                + $"replacement asks for #{replacement.Number}. Renumbering a posted voucher is not part of Alter.");

        // Clause 1 — validate BEFORE the book is touched. Stamp first, exactly as Post does, so the pairing check
        // and the on-hand engine read the canonical direction.
        //
        // 🔴 …and UNDO the stamping if validation refuses. Both the Number copy and the direction stamp MUTATE the
        // caller's object, and a rejected replacement used to be handed back carrying the original's number and
        // rewritten directions. That is not cosmetic: re-posting the corrected draft as a NEW voucher then took the
        // STAMPED number instead of a fresh one, producing two live vouchers of one type sharing one number.
        // Clause 1 promises the original is untouched; this extends the same promise to the REJECTED replacement.
        var incomingNumber = replacement.Number;
        var incomingDirections = replacement.InventoryLines.Count == 0
            ? Array.Empty<StockDirection>()
            : replacement.InventoryLines.Select(l => l.Direction).ToArray();

        replacement.Number = existingNumber;
        try
        {
            StampInventoryLineDirections(replacement);
            VoucherValidator.EnsureValid(replacement, _company, CostAllocationStrictness.Strict, replacing: existing);
        }
        catch
        {
            replacement.Number = incomingNumber;
            replacement.RestoreInventoryLineDirections(incomingDirections);
            throw;
        }

        // Past this point nothing can throw, so the swap is safe.
        var raised = new List<VoucherAlterationWarning>();
        CarryBankDatesForward(existing, replacement, raised);

        if (replacement.Date != existing.Date)
            raised.Add(new VoucherAlterationWarning(
                existingId,
                VoucherAlterationWarningCode.DateChanged,
                $"Voucher date changed from {existing.Date:dd-MMM-yyyy} to {replacement.Date:dd-MMM-yyyy}."));

        RaiseRenderedNumberWarning(existing, replacement, raised);
        RaiseStatutoryDivergenceWarnings(existing, replacement, raised);

        // Clause 4 — swap at the index; never Remove + Add.
        _company.ReplaceVoucherInternal(existing, replacement);

        warnings = raised;
        return replacement;
    }

    /// <summary>
    /// The not-found throw for <see cref="Replace(Guid, Voucher, out IReadOnlyList{VoucherAlterationWarning})"/>.
    /// A pure-stock <c>InventoryVoucher</c> lives in a DIFFERENT aggregate list with its own posting service, so
    /// <see cref="Company.FindVoucher"/> cannot see it and the caller used to get a bare "not found" —
    /// indistinguishable from a mistyped Guid, and with no hint that <c>InventoryPostingService</c> is the right
    /// door. §7.4 calls the pure-stock family <i>"the single nastiest in the phase"</i>; a caller that lands here
    /// deserves to be told which aggregate it landed in.
    /// </summary>
    /// <summary>
    /// Refuses a voucher id that is already in use by EITHER aggregate — the accounting book or the pure-stock
    /// book. Called by <see cref="Post(Voucher, CostAllocationStrictness)"/> and by
    /// <c>InventoryPostingService.Post</c>, the two entry doors. See the comment at the head of
    /// <see cref="Post(Voucher, CostAllocationStrictness)"/> for why this invariant is load-bearing.
    /// </summary>
    internal static void EnsureVoucherIdIsFree(Company company, Guid id)
    {
        if (company.FindVoucher(id) is not null)
            throw new InvalidVoucherException(
                $"Voucher id {id} is already posted in this company's accounting book. A voucher's Guid is the "
                + "only handle every credit/debit-note link, e-invoice record, e-way bill and challan link has on "
                + "it, so two vouchers may never share one.");

        foreach (var iv in company.InventoryVouchers)
            if (iv.Id == id)
                throw new InvalidVoucherException(
                    $"Voucher id {id} is already posted in this company's inventory book. The accounting and "
                    + "pure-stock aggregates share one id space; two vouchers may never share one Guid.");
    }

    private InvalidOperationException NotFound(Guid voucherId) =>
        _company.InventoryVouchers.Any(iv => iv.Id == voucherId)
            ? new InvalidOperationException(
                $"Voucher {voucherId} is a pure-stock inventory voucher; LedgerService.Replace does not reach the "
                + "inventory aggregate — use InventoryPostingService.")
            : new InvalidOperationException($"Voucher {voucherId} not found.");

    /// <summary>
    /// Carries <see cref="BankAllocation.BankDate"/> from the outgoing voucher onto the replacement (§3.4).
    ///
    /// <para><b>Pairing is TWO-PASS, exact first.</b> Pass 1 pairs on ledger + bank-instrument identity
    /// <b>+ amount + side</b>; pass 2 falls back to ledger + instrument only, for the replacement lines pass 1 left
    /// unmatched. Each old line is consumed at most once. For a matched pair the reconcile tick is CARRIED when the
    /// amount and side are unchanged and CLEARED — <b>with a warning, and by an actual assignment</b> — when either
    /// moved. A reconciled old bank line with no counterpart in the replacement warns too: the line was removed, and
    /// with it a reconciliation somebody performed.</para>
    ///
    /// <para>🔴 <b>Why pass 1 exists (S5a review finding C, widened).</b> Single-pass first-match-wins pairs on
    /// instrument identity alone, so when two bank lines share one ledger AND one instrument identity, the wrong old
    /// line is consumed. Measured on the shipped single-pass code: a Payment with two ₹100/₹200 lines on the same
    /// cheque, both ticked, with the ₹100 line REMOVED — the surviving, byte-identical, genuinely reconciled ₹200
    /// line lost its tick, and BOTH warnings the operator saw were factually false ("the line amount changed from
    /// 100.00 to 200.00" — no line's amount changed; "not present in the replacement" — a line WAS present). Merely
    /// REORDERING the two lines destroyed both ticks the same way. An exact first pass makes each line pair with
    /// itself; the fallback still reports a genuine removal correctly.</para>
    ///
    /// <para>🔴 <b>The ECHO rule.</b> A replacement line carrying a bank date <b>equal to the outgoing line's</b> is
    /// an ECHO of the posted fact, not a statement — it is exactly what an S5b <c>ForAlter</c> rehydration produces,
    /// because rehydrating a posted line reads <c>BankDate</c> back with it. Treating an echo as "the caller stated
    /// it" defeats this whole guard: measured on the shipped code, an amount change from ₹47,239.55 to ₹47,241.05
    /// carrying the old date left the tick standing with ZERO warnings — the precise defect §3.4 exists to prevent,
    /// silently reintroduced by the slice that was going to consume this one. Only a date DIFFERENT from the
    /// outgoing one (or a date on a line that had none) is a statement, and those are honoured untouched.</para>
    /// </summary>
    private static void CarryBankDatesForward(
        Voucher existing, Voucher replacement, List<VoucherAlterationWarning> warnings)
    {
        var oldLines = existing.Lines;
        var consumed = new bool[oldLines.Count];

        var newBankLines = new List<EntryLine>();
        foreach (var line in replacement.Lines)
            if (line.BankAllocation is not null) newBankLines.Add(line);
        if (newBankLines.Count == 0 && !oldLines.Any(l => l.BankAllocation?.BankDate is not null)) return;

        var matchIndex = new int[newBankLines.Count];
        for (var n = 0; n < newBankLines.Count; n++)
            matchIndex[n] = FindBankMatch(oldLines, consumed, newBankLines[n], requireAmountAndSide: true);
        for (var n = 0; n < newBankLines.Count; n++)
            if (matchIndex[n] < 0)
                matchIndex[n] = FindBankMatch(oldLines, consumed, newBankLines[n], requireAmountAndSide: false);

        for (var n = 0; n < newBankLines.Count; n++)
        {
            if (matchIndex[n] < 0) continue;

            var newLine = newBankLines[n];
            var newAllocation = newLine.BankAllocation!;
            var matched = oldLines[matchIndex[n]];

            if (matched.BankAllocation!.BankDate is not { } bankDate) continue;  // nothing was reconciled

            // A date the caller genuinely STATED (different from the one on the outgoing line) is honoured; a date
            // EQUAL to the outgoing one is an echo and falls through to the carry-vs-clear rule below.
            if (newAllocation.BankDate is { } stated && stated != bankDate) continue;

            if (matched.Amount == newLine.Amount && matched.Side == newLine.Side)
            {
                newAllocation.BankDate = bankDate;                               // CARRY
                continue;
            }

            // CLEAR — an assignment, not a hope that the replacement already carried null. Under the echo rule the
            // replacement CAN arrive holding the old date, and a warning whose state did not follow it is worse
            // than no warning at all.
            newAllocation.BankDate = null;

            var amountMoved = matched.Amount != newLine.Amount;
            var sideMoved = matched.Side != newLine.Side;
            var what = (amountMoved, sideMoved) switch
            {
                (true, true) => $"amount {matched.Amount} -> {newLine.Amount} and side {matched.Side} -> {newLine.Side}",
                (true, false) => $"amount {matched.Amount} -> {newLine.Amount}",
                _ => $"side {matched.Side} -> {newLine.Side}",
            };

            warnings.Add(new VoucherAlterationWarning(
                existing.Id,
                VoucherAlterationWarningCode.BankDateCleared,
                $"Bank reconciliation date {bankDate:dd-MMM-yyyy} was cleared on the bank line for ledger "
                + $"{newLine.LedgerId}: the replacement's matching bank line no longer matches the reconciled one "
                + $"({what}). A cleared item that no longer matches the statement is not cleared — reconcile it again.",
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
    /// The first unconsumed old bank line matching <paramref name="newLine"/> on ledger + bank-instrument identity,
    /// and — when <paramref name="requireAmountAndSide"/> — on amount and side too. Marks the match consumed.
    /// Returns -1 when there is none.
    /// </summary>
    private static int FindBankMatch(
        IReadOnlyList<EntryLine> oldLines, bool[] consumed, EntryLine newLine, bool requireAmountAndSide)
    {
        var newAllocation = newLine.BankAllocation!;
        for (var i = 0; i < oldLines.Count; i++)
        {
            if (consumed[i]) continue;
            var candidate = oldLines[i];
            if (candidate.BankAllocation is not { } oldAllocation) continue;
            if (candidate.LedgerId != newLine.LedgerId) continue;
            if (!SameBankInstrument(oldAllocation, newAllocation)) continue;
            if (requireAmountAndSide && (candidate.Amount != newLine.Amount || candidate.Side != newLine.Side))
                continue;

            consumed[i] = true;
            return i;
        }

        return -1;
    }

    /// <summary>
    /// Clause 3 preserves the integer <see cref="Voucher.Number"/> — but NOT the RENDERED number, and the rendered
    /// number is the one the outside world uses. <c>VoucherNumberFormatter</c> selects the prefix/suffix by voucher
    /// DATE, so moving a voucher across a date-effective affix boundary silently rewrites the printed document
    /// number (measured: <c>SL2/10</c> became <c>SL/10</c> on a date change, warned about only as "the date
    /// changed"). That is the very hazard the TypeId refusal above cites — a preserved number leaving the sequence
    /// it belongs to — reached by a path that had no guard at all.
    /// </summary>
    private void RaiseRenderedNumberWarning(
        Voucher existing, Voucher replacement, List<VoucherAlterationWarning> warnings)
    {
        var before = _company.FormatVoucherNumber(existing);
        var after = _company.FormatVoucherNumber(replacement);
        if (string.Equals(before, after, StringComparison.Ordinal)) return;

        warnings.Add(new VoucherAlterationWarning(
            existing.Id,
            VoucherAlterationWarningCode.RenderedNumberChanged,
            $"The rendered voucher number changed from '{before}' to '{after}' even though the voucher number is "
            + $"still #{existing.Number}: the prefix/suffix is selected by voucher DATE. The printed document "
            + "number, and every record that froze it, now disagree with the voucher."));
    }

    /// <summary>
    /// §3.3 CARRY + WARN — the records stored BESIDE the voucher that FREEZE a fact about it. All of them are
    /// carried for free by the preserved <see cref="Voucher.Id"/> (clause 2), which is exactly why they can lie:
    /// they still resolve, and they still declare the pre-alteration figure.
    ///
    /// <para><b>Measured, all with ZERO warnings before this method existed.</b> A Generated e-Way Bill kept a
    /// portal-issued EWB number against a consignment value TEN TIMES the amended invoice, and the EWB-01 request
    /// the app files became internally contradictory — <c>totInvValue</c> 70,800 over an <c>itemList</c> summing to
    /// 7,080 (the header is the frozen field, the items are re-read live off the amended lines, ER-9) — while the
    /// movement itself dropped below the Rule-138 threshold. A GSTR-1 Table 11A line went on declaring a ₹10,000
    /// advance with ₹1,800 tax against a book now recording ₹1,180. §3.3 calls the e-Way row <i>"the highest
    /// silent-divergence risk in the phase"</i> and assigns it CARRY + WARN; this is the WARN.</para>
    ///
    /// <para><b>Deliberately warnings, not refusals.</b> §6.6 puts the <c>EInvoiceStatus.Generated</c> REFUSAL in
    /// S5b and states warn-and-proceed for an active e-Way bill; S5a does not invent a refusal the design assigned
    /// to a later slice.</para>
    /// </summary>
    private void RaiseStatutoryDivergenceWarnings(
        Voucher existing, Voucher replacement, List<VoucherAlterationWarning> warnings)
    {
        var id = existing.Id;
        var dateMoved = replacement.Date != existing.Date;
        var totalMoved = replacement.TotalDebit != existing.TotalDebit;

        void Diverged(string message) =>
            warnings.Add(new VoucherAlterationWarning(
                id, VoucherAlterationWarningCode.StatutoryRecordDiverged, message));

        // e-invoice — the IRN was signed over a document this alteration has changed. The only content check the
        // app has (Gstr1.EInvoiceReconciliation) compares the DOCUMENT NUMBER, so an amount-only amendment leaves
        // it reporting Mismatched = 0 — a clean bill of health over a diverged document.
        if (_company.FindEInvoiceRecordForVoucher(id) is { } eInvoice
            && eInvoice.Status == EInvoiceStatus.Generated
            && (totalMoved || dateMoved))
        {
            Diverged(
                $"The e-invoice record for this voucher carries IRN '{eInvoice.Irn}' issued against document "
                + $"'{eInvoice.DocumentNumberUpper}' as it stood before this alteration. An IRN cannot be "
                + "re-derived, and the e-invoice reconciliation compares only the document NUMBER, so it will not "
                + "report this. Cancel the IRN with the IRP and raise a fresh document if the supply changed.");
        }

        // e-Way bill — ConsignmentValuePaisa is frozen "for audit" and NOTHING in Reports reads it back against
        // the voucher, so a divergence here has no other detector anywhere in the app.
        if (_company.FindEWayBillRecordForVoucher(id) is { } eWay)
        {
            var now = PaisaConversion.ToPaisaRounded(new EWayBillService(_company).ConsignmentValue(replacement));
            if (now != eWay.ConsignmentValuePaisa)
                Diverged(
                    $"The e-Way Bill record for this voucher froze a consignment value of "
                    + $"{PaisaConversion.ToMoney(eWay.ConsignmentValuePaisa)} (status {eWay.Status}"
                    + (eWay.EwbNumber is { Length: > 0 } ewb ? $", EWB {ewb}" : "")
                    + $"); the amended voucher's consignment value is {PaisaConversion.ToMoney(now)}. The EWB-01 request states "
                    + "the frozen value in its header while its item list is read live off these lines, so the two "
                    + "no longer agree.");
        }

        // GSTR-1 Table 11A reads the FROZEN GstAdvanceReceipt.AdvanceAmount and uses the voucher only as a
        // date/liveness gate — so the ledger moves and the return line does not. A date move is just as bad: the
        // period window is taken from the VOUCHER date, so a whole 11A row can appear or vanish.
        foreach (var advance in _company.AdvanceReceipts)
        {
            if (advance.ReceiptVoucherId != id) continue;
            if (!totalMoved && !dateMoved) continue;
            Diverged(
                $"The GST advance receipt attached to this voucher froze an advance of {advance.AdvanceAmount} "
                + $"with tax {advance.AdvanceTax}; GSTR-1 Table 11A declares those frozen figures and reads this "
                + "voucher only for its date. The return line will not follow this alteration."
                + (dateMoved ? " The date move can also add or drop the row from a return period entirely." : ""));
        }

        // §34(2): GstCreditDebitNoteLink freezes the ORIGINAL invoice's date, and Gstr1Amendments reads that frozen
        // date to decide whether a note is an AMENDMENT (the 30-Nov cut-off). Altering the ORIGINAL invoice's date
        // therefore changes an answer computed from a copy of it.
        foreach (var link in _company.CreditDebitNoteLinks)
        {
            if (link.OriginalInvoiceVoucherId != id) continue;
            var numberBefore = _company.FormatVoucherNumber(existing);
            var numberAfter = _company.FormatVoucherNumber(replacement);
            var linkNumberStale = link.OriginalInvoiceNumber is { Length: > 0 } n
                && string.Equals(n, numberBefore, StringComparison.Ordinal)
                && !string.Equals(numberBefore, numberAfter, StringComparison.Ordinal);
            var linkDateStale = link.OriginalInvoiceDate is { } od && od == existing.Date && dateMoved;
            if (!linkNumberStale && !linkDateStale) continue;

            Diverged(
                $"A GST credit/debit note links to this voucher as its ORIGINAL invoice and froze "
                + $"'{link.OriginalInvoiceNumber}' dated "
                + $"{(link.OriginalInvoiceDate is { } d ? d.ToString("dd-MMM-yyyy") : "(none)")}. That frozen date "
                + "is what decides the §34(2) 30-Nov cut-off — i.e. whether the note is reported as an amendment — "
                + "and it no longer matches this voucher.");
        }

        // RcmDocument (self-invoice / Rule-52 payment voucher) freezes its own DocDate off the source voucher.
        foreach (var rcm in _company.RcmDocuments)
        {
            if (rcm.SourceVoucherId != id || rcm.DocDate != existing.Date || !dateMoved) continue;
            Diverged(
                $"The RCM {rcm.Kind} document #{rcm.SeriesNumber} raised from this voucher is dated "
                + $"{rcm.DocDate:dd-MMM-yyyy}, which no longer matches the voucher's "
                + $"{replacement.Date:dd-MMM-yyyy}.");
        }
    }


    /// <summary>
    /// Two bank allocations describe the SAME instrument — the identity a reconcile tick belongs to. The bank
    /// date itself is deliberately not part of the comparison (it is the thing being carried).
    /// <para><b>All three clauses are load-bearing and all three are now pinned.</b> Dropping
    /// <see cref="BankAllocation.TransactionType"/> or <see cref="BankAllocation.InstrumentDate"/> used to leave the
    /// whole gate green while letting a tick from a DIFFERENT instrument attach — a cheque re-keyed as an NEFT, or a
    /// cheque re-issued under the same number on a later date, inheriting a clearance it never had. One test row
    /// each, in <c>VoucherReplaceBankPairingTests</c>.</para>
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
