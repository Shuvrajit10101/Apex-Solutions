namespace Apex.Ledger.Domain;

/// <summary>
/// A scenario master (catalog §7; plan.md §5): a what-if column that surfaces provisional entries
/// (<b>Optional</b> vouchers, <b>Reversing Journals</b> within their "Applicable upto", and
/// <b>Memorandum</b> vouchers) that never touch the real books.
/// <c>Create → Scenario</c> captures a <see cref="Name"/>, an <b>Include Actuals?</b> flag
/// (<see cref="IncludeActuals"/>), and lists of <b>Included</b> / <b>Excluded voucher types</b>.
/// </summary>
/// <remarks>
/// <para>Semantics (design §7; catalog §7): a scenario report = the actual (real-books) figures when
/// <see cref="IncludeActuals"/> is <c>true</c>, PLUS the provisional vouchers whose voucher type is in
/// <see cref="IncludedTypeIds"/> — <b>except</b> Optional/PostDated exclusion is relaxed for
/// the included types (an Optional voucher of an included type IS counted), while a Reversing Journal is
/// counted only while the as-of date is within its <see cref="Voucher.ApplicableUpto"/>.
/// <b>A CANCELLED voucher never counts, in a scenario column or out of it</b> — <c>LedgerBalances.CountsAsOf</c>
/// refuses it on its first statement, before any inclusion test runs, and that method's own doc list says so.
/// This sentence used to name Cancelled alongside Optional/PostDated as relaxed, which was false and, until
/// Phase 10.11 S3 made a voucher cancellable at all, untestable.</para>
/// <para><see cref="ExcludedTypeIds"/> removes a voucher type from the scenario even if it would
/// otherwise be actual (e.g. exclude a real Journal type). Exclusion takes precedence over inclusion.
/// A real (non-provisional) voucher is counted when actuals are included and its type is not excluded.
/// The <see cref="Id"/> is the stable key; the <see cref="Name"/> is not, so an Alter renames in place.</para>
/// </remarks>
public sealed class Scenario
{
    private readonly HashSet<Guid> _includedTypeIds;
    private readonly HashSet<Guid> _excludedTypeIds;

    /// <summary>Stable surrogate key.</summary>
    public Guid Id { get; }

    /// <summary>Unique within a company; a rename does not change identity.</summary>
    public string Name { get; set; }

    /// <summary>Whether the real-books (actual) figures form the base of the scenario column.</summary>
    public bool IncludeActuals { get; set; }

    /// <summary>Voucher types whose (otherwise-provisional) vouchers this scenario surfaces.</summary>
    public IReadOnlyCollection<Guid> IncludedTypeIds => _includedTypeIds;

    /// <summary>Voucher types this scenario removes from its column (precedence over inclusion).</summary>
    public IReadOnlyCollection<Guid> ExcludedTypeIds => _excludedTypeIds;

    public Scenario(
        Guid id,
        string name,
        bool includeActuals = true,
        IEnumerable<Guid>? includedTypeIds = null,
        IEnumerable<Guid>? excludedTypeIds = null)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Scenario name is required.", nameof(name));

        Id = id;
        Name = name;
        IncludeActuals = includeActuals;
        _includedTypeIds = includedTypeIds is null ? new HashSet<Guid>() : new HashSet<Guid>(includedTypeIds);
        _excludedTypeIds = excludedTypeIds is null ? new HashSet<Guid>() : new HashSet<Guid>(excludedTypeIds);
    }

    /// <summary>Adds a voucher type to the "included" set (idempotent).</summary>
    public void IncludeType(Guid voucherTypeId) => _includedTypeIds.Add(voucherTypeId);

    /// <summary>Adds a voucher type to the "excluded" set (idempotent).</summary>
    public void ExcludeType(Guid voucherTypeId) => _excludedTypeIds.Add(voucherTypeId);

    /// <summary>True iff <paramref name="voucherTypeId"/> is in the included set.</summary>
    public bool Includes(Guid voucherTypeId) => _includedTypeIds.Contains(voucherTypeId);

    /// <summary>True iff <paramref name="voucherTypeId"/> is in the excluded set.</summary>
    public bool Excludes(Guid voucherTypeId) => _excludedTypeIds.Contains(voucherTypeId);

    /// <summary>
    /// Replaces both voucher-type sets at once — the write half of <b>Scenario Alteration</b> (census 2.10).
    /// </summary>
    /// <remarks>
    /// <para>🔴 <b>WHY BOTH SETS IN ONE CALL, AND WHY REPLACE RATHER THAN REMOVE.</b> Before this,
    /// <see cref="IncludeType"/> and <see cref="ExcludeType"/> were the only mutators and both are
    /// <i>add-only</i> — so a scenario could never have a voucher type taken back out of it, which is precisely
    /// the act the vendor's own alteration page describes (<i>"From the Include and Exclude sections, select the
    /// scenarios to alter from the List of Voucher Types"</i>,
    /// help.tallysolutions.com/tally-prime/financial-controlling/scenarios-tally/, opened by content 2026-10-11).
    /// Taking the two together is not convenience: <see cref="ExcludedTypeIds"/> has PRECEDENCE over
    /// <see cref="IncludedTypeIds"/> (see this type's own remarks), so applying them in two steps would leave the
    /// aggregate briefly describing a scenario the operator never asked for.</para>
    /// <para>Persistence needs no migration for this: <c>scenario_voucher_types</c> already carries
    /// <c>is_included</c> and is written delete-all + re-insert from this object graph.</para>
    /// </remarks>
    public void ReplaceTypes(IEnumerable<Guid> includedTypeIds, IEnumerable<Guid> excludedTypeIds)
    {
        ArgumentNullException.ThrowIfNull(includedTypeIds);
        ArgumentNullException.ThrowIfNull(excludedTypeIds);

        var included = new HashSet<Guid>(includedTypeIds);
        var excluded = new HashSet<Guid>(excludedTypeIds);

        _includedTypeIds.Clear();
        foreach (var id in included) _includedTypeIds.Add(id);
        _excludedTypeIds.Clear();
        foreach (var id in excluded) _excludedTypeIds.Add(id);
    }
}
