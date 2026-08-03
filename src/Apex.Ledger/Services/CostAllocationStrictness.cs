namespace Apex.Ledger.Services;

/// <summary>
/// Which cost-allocation invariant <see cref="VoucherValidator"/> enforces on a voucher (spec §4.2 rule
/// C-27; gap G-2).
/// <para><b>Cost categories are parallel allocation axes, not a partition.</b> The corpus's own worked
/// example allocates one ₹5,000 travelling expense in full to Branch → Kolkata <i>and</i> in full to
/// Department → Marketing <i>and</i> in full to Executive → Sales Executive 1 (TALLY PRIME STUDY GUIDE
/// pp.101–102). The correct invariant is therefore <b>per category</b>.</para>
/// <para>This project shipped the wrong (partition) invariant first, so books already on disk may contain
/// lines whose axes foot only when they are added together. Because <c>SqliteCompanyStore.Load</c> re-posts
/// every stored voucher through the engine, tightening the rule unconditionally would make those companies
/// <b>unopenable</b>. <see cref="Legacy"/> exists solely so rehydration paths keep accepting what was
/// legitimately accepted before; it is never used when a user enters or alters a voucher.</para>
/// </summary>
public enum CostAllocationStrictness
{
    /// <summary>
    /// The correct rule (the default, used by every entry path): within <b>each</b> cost category the
    /// allocations on a line must total the line amount. Categories are never added together.
    /// </summary>
    Strict = 0,

    /// <summary>
    /// Rehydration only (SQLite <c>Load</c>, company import). Accepts a line when it satisfies
    /// <see cref="Strict"/>, <b>or</b> when it satisfies the superseded partition rule (the allocations
    /// summed across all categories equal the line amount). Anything that foots under neither rule is
    /// still rejected — a corrupt store is surfaced, not swallowed. Lines admitted only by the second
    /// clause are listed by <c>CostAllocationDiagnostics.FindLegacyCrossCategoryLines</c> for a human to
    /// re-allocate; nothing is ever silently rewritten.
    /// </summary>
    Legacy = 1,
}
