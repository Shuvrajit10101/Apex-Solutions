using System.Text.Json;
using System.Text.Json.Serialization;

namespace Apex.Ledger.Domain;

/// <summary>
/// Which lifecycle verb an <see cref="VoucherEditLogEntry"/> records. The ordinals are PERSISTED
/// (<c>voucher_edit_log.verb</c>, schema v52) — append new members, never renumber existing ones.
/// </summary>
public enum VoucherEditVerb
{
    /// <summary>Alt+X — <c>LedgerService.Cancel</c>. The voucher stays on the book, flagged.</summary>
    Cancel = 0,

    /// <summary>Alt+D — <c>LedgerService.Delete</c>. The voucher leaves the book entirely.</summary>
    Delete = 1,

    /// <summary>Ctrl+Enter — <c>LedgerService.Replace</c>. The voucher is overwritten in place.</summary>
    Alter = 2,

    /// <summary>
    /// <c>LedgerService.ConvertToRegular</c> — a Memorandum voucher was regularised, which REMOVES the memo from
    /// the book and posts a real voucher in its place under a fresh <see cref="Guid"/>. Its own verb rather than
    /// a <see cref="Delete"/> because the entry it left is not a deletion: the content lives on.
    /// </summary>
    ConvertMemorandum = 3,
}

/// <summary>
/// One line of the <b>voucher edit log</b>: a posted voucher was cancelled, deleted or altered, and this is the
/// record that it happened. Append-only; nothing in the product rewrites an entry, and the only removal is the
/// bounded <c>LedgerService.DiscardUncommittedEditLogEntry</c> (see that method for why it must exist).
///
/// <para><b>Why this exists.</b> Before it, <c>Cancel</c> left a flag on the voucher and was therefore the ONLY
/// one of the three verbs that left any evidence at all; <c>Delete</c> removed the row and <c>Replace</c>
/// overwrote it, after which nothing anywhere in the product could tell an auditor the book had been edited.</para>
///
/// <para>🔴 <b>WHAT THIS RECORD CANNOT SAY, stated here rather than implied by its absence: WHO.</b> There is no
/// user, no actor, no login and no session identity anywhere in this application — a census of the code found
/// zero hits for <c>AuditTrail</c>, <c>EditLog</c>, <c>ModifiedBy</c>, <c>CreatedBy</c> and <c>ActorId</c>, and no
/// audit table among the 182. A <c>ModifiedBy</c> column here would therefore have exactly one honest value
/// ("unknown") and one dishonest one (a fabricated name), and a log that claims to say who when nothing knows who
/// is worse than one that admits it. When an identity model lands, an <c>actor</c> column is an additive schema
/// bump; until then this record deliberately carries none.</para>
///
/// <para><b>Only the BEFORE state is recorded, and that is not a narrowing.</b> The after-state of a
/// <see cref="VoucherEditVerb.Cancel"/> or <see cref="VoucherEditVerb.Alter"/> is on the book, and where a later
/// entry exists for the same voucher its own <see cref="BeforeSnapshot"/> IS the earlier entry's after-state —
/// so the chain <c>entry₁.Before → entry₂.Before → … → the live voucher</c> reconstructs every intermediate
/// state. A <see cref="VoucherEditVerb.Delete"/> ends the chain with nothing on the book, which is exactly what
/// its entry says. Recording an after-state as well would duplicate the book on every line.</para>
/// </summary>
/// <param name="Id">This entry's own surrogate key (the persisted PRIMARY KEY).</param>
/// <param name="VoucherId">The <see cref="Voucher.Id"/> the verb was applied to. <b>Deliberately not a foreign
/// key in the schema</b> — <see cref="VoucherEditVerb.Delete"/>'s whole point is that the voucher is gone, and a
/// FK would make its own log line unstorable.</param>
/// <param name="Verb">Which verb ran.</param>
/// <param name="RecordedAt">When the verb ran, from the clock the caller handed
/// <c>LedgerService</c>. Not a book date and never used in any calculation.</param>
/// <param name="BeforeSnapshot">The pre-change voucher, rendered by <see cref="VoucherSnapshot"/>.</param>
public sealed record VoucherEditLogEntry(
    Guid Id,
    Guid VoucherId,
    VoucherEditVerb Verb,
    DateTimeOffset RecordedAt,
    string BeforeSnapshot);

/// <summary>
/// Renders a <see cref="Voucher"/> to the text an <see cref="VoucherEditLogEntry"/> stores as its before-state.
///
/// <para>🔴 <b>WHY THIS IS A WHOLE-OBJECT SERIALISATION AND NOT A HAND-PICKED FIELD LIST.</b> A hand-written
/// projection is a narrowing waiting to happen: every field added to <see cref="Voucher"/> or
/// <see cref="EntryLine"/> after this file is written would be silently absent from every future snapshot, and
/// nothing would go red. Serialising the object graph makes the snapshot complete <i>by construction</i> — a new
/// property appears in it the day it is added, with no edit here. <see cref="VoucherSnapshotCompletenessTests"/>
/// pins that property against a <c>[JsonIgnore]</c> ever being introduced.</para>
///
/// <para><b>This is NOT the canonical export.</b> The canonical export lives in <c>Apex.Ledger.Io</c>
/// (<c>CanonicalMapper</c>/<c>CanonicalJson</c>) and this assembly cannot reference it — <c>Apex.Ledger</c> has no
/// project references at all, by design, and <c>Apex.Ledger.Io</c> depends on <i>it</i>. So the snapshot is an
/// engine-local rendering with a narrower job: be readable, be complete, and never need a schema. It carries the
/// derived/convenience members too (<c>TotalDebit</c>, <c>HasGst</c>, …), which is not tidy but is free and gives
/// a reader a cross-check on the lines.</para>
///
/// <para><b>Write-only.</b> Nothing reads a snapshot back into a <see cref="Voucher"/>; it is evidence, not a
/// restore point. That is deliberate — an un-delete built on this text would resurrect a voucher at the END of
/// the book, silently re-ordering it (see <c>Company.ReplaceVoucherInternal</c> on why the list index matters).</para>
/// </summary>
public static class VoucherSnapshot
{
    /// <summary>The serialiser options. <c>WriteIndented = false</c> keeps a log line one line; the default
    /// (declaration-order) property order makes two snapshots of the same shape textually comparable.</summary>
    internal static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        NumberHandling = JsonNumberHandling.Strict,
    };

    /// <summary>Renders <paramref name="voucher"/> as the before-state text of an edit-log entry.</summary>
    public static string Of(Voucher voucher)
    {
        ArgumentNullException.ThrowIfNull(voucher);
        return JsonSerializer.Serialize(voucher, Options);
    }
}
