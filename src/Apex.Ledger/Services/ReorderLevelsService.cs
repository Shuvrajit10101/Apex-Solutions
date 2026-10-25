using Apex.Ledger.Domain;

namespace Apex.Ledger.Services;

/// <summary>
/// The <b>Reorder Levels</b> master service (Phase 6 slice 6; requirements RQ-32..RQ-35; Tally-Book pp.158–162).
/// Creates, upserts and deletes <see cref="ReorderDefinition"/> records — the per Item / Group / Category reorder
/// thresholds the Reorder-Status report consumes — enforcing the same discipline the other inventory masters
/// ship with:
/// <list type="bullet">
///   <item>the <see cref="ReorderScope"/> target must exist (a stock item / group / category respectively);</item>
///   <item>quantities are ≥ 0 and to 6-dp precision; an Advanced figure requires a positive consumption period +
///     a Higher/Lower criterion (validated by <see cref="ReorderDefinition"/>);</item>
///   <item>at most <b>one</b> definition per (scope, target) — <see cref="CreateOrUpdate"/> <i>upserts</i>,
///     replacing any existing definition for the same (scope, target) rather than adding a duplicate (RQ-32).</item>
/// </list>
/// Throws <see cref="InvalidOperationException"/> on any violation <b>without mutating the company</b> — exactly
/// like <see cref="BatchService"/> / <see cref="PriceListService"/>. Framework- and DB-agnostic; unit-tested like
/// the accounting core.
/// </summary>
public sealed class ReorderLevelsService
{
    private readonly Company _company;

    public ReorderLevelsService(Company company)
        => _company = company ?? throw new ArgumentNullException(nameof(company));

    /// <summary>
    /// Creates (or replaces) the reorder definition for a (scope, target). The target must exist; the quantities
    /// and Advanced period/criterion are validated by the <see cref="ReorderDefinition"/> constructor. If a
    /// definition already exists for the same (scope, target) it is <b>replaced</b> (upsert — RQ-32), preserving
    /// no stale figures. The company is not mutated if any validation fails.
    /// </summary>
    public ReorderDefinition CreateOrUpdate(
        ReorderScope scope,
        Guid targetId,
        bool reorderAdvanced = false,
        decimal? reorderQuantity = null,
        bool minQtyAdvanced = false,
        decimal? minOrderQuantity = null,
        int? periodCount = null,
        ExpiryPeriodUnit? periodUnit = null,
        ReorderCriteria? criteria = null)
    {
        if (!TargetExists(scope, targetId))
            throw new InvalidOperationException($"{scope} {targetId} not found.");

        // Build (and validate) the new definition BEFORE removing the old one, so a validation failure leaves the
        // company untouched (ER-12).
        var definition = new ReorderDefinition(Guid.NewGuid(), scope, targetId, reorderAdvanced, reorderQuantity,
            minQtyAdvanced, minOrderQuantity, periodCount, periodUnit, criteria);

        if (_company.FindReorderDefinition(scope, targetId) is { } existing)
            _company.RemoveReorderDefinition(existing);
        _company.AddReorderDefinition(definition);
        return definition;
    }

    /// <summary>
    /// <b>Creates</b> a reorder definition for a (scope, target) that has NONE yet, and <b>refuses by name</b> when
    /// one already exists — census 3.12's last open clause (T2-158 item 1).
    ///
    /// <para>🔴 <b>WHY THE SCREEN NO LONGER CALLS <see cref="CreateOrUpdate"/>.</b> That upsert mints a fresh
    /// <c>Guid</c> and discards the existing row, and the screen reads a blank quantity box as <i>unset</i>; so
    /// an operator who picked an occupied (scope, target) in CREATE mode, keyed one figure and accepted silently
    /// WIPED every other figure the Reorder Status report consumes and replaced the record under a new id
    /// (reviewer probe P1c: 100 / 25 → Create, key 150, Ctrl+A → new id, minimum <c>NULL</c>, notice "saved").
    /// A create verb may only ever add; changing an existing definition is <see cref="Alter"/>'s job, which keeps
    /// the id. <see cref="CreateOrUpdate"/> is kept for seeding and tests, never for a user-facing verb.</para>
    /// </summary>
    /// <exception cref="InvalidOperationException">The target does not exist, a definition already covers this
    /// (scope, target), or the <see cref="ReorderDefinition"/> constructor refuses the figures.</exception>
    public ReorderDefinition Create(
        ReorderScope scope,
        Guid targetId,
        bool reorderAdvanced = false,
        decimal? reorderQuantity = null,
        bool minQtyAdvanced = false,
        decimal? minOrderQuantity = null,
        int? periodCount = null,
        ExpiryPeriodUnit? periodUnit = null,
        ReorderCriteria? criteria = null)
    {
        if (!TargetExists(scope, targetId))
            throw new InvalidOperationException($"{scope} {targetId} not found.");

        if (_company.FindReorderDefinition(scope, targetId) is not null)
            throw new InvalidOperationException(
                $"A reorder level already exists for {scope} '{TargetName(scope, targetId)}'. Alter it instead of creating a second one.");

        var definition = new ReorderDefinition(Guid.NewGuid(), scope, targetId, reorderAdvanced, reorderQuantity,
            minQtyAdvanced, minOrderQuantity, periodCount, periodUnit, criteria);
        _company.AddReorderDefinition(definition);
        return definition;
    }

    /// <summary>
    /// <b>Alters ONE existing reorder definition in place</b> — census 3.12. Keeps the definition's
    /// <see cref="ReorderDefinition.Id"/> and replaces every other field, so a correction updates the row that
    /// already exists instead of creating a second one.
    ///
    /// <para><b>FIDELITY (R7, ruling 14): VENDOR-ATTESTED ROUTE.</b> TallyPrime reaches the reorder level through
    /// an ALTERATION route — <i>"Press Alt+G (Go To) &gt; Alter Master &gt; type or select Reorder Level (Stock
    /// Item) and press Enter."</i> — and the field set it then shows is the one this signature carries:
    /// <i>"Minimum Order Quantity: Enter the minimum quantity you want to order."</i>, <i>"Period: Select the
    /// consumption for various periods – Days, Weeks, Months, or Years."</i> and a <i>Criteria</i> of Higher or
    /// Lower [<c>help.tallysolutions.com/tally-prime/inventory/reorder-stock-items-reorder-status-and-reorder-quantity/</c>,
    /// opened and read BY CONTENT 2026-10-23]. The page is silent on DELETING a definition, which is recorded as
    /// silence rather than permission on <see cref="Delete"/>; it is not silent on altering one.</para>
    ///
    /// <para>🔴 <b>WHY THIS IS NOT <see cref="CreateOrUpdate"/> WITH EXTRA STEPS, and the difference is the whole
    /// point of the method.</b> <see cref="CreateOrUpdate"/> mints a <c>Guid.NewGuid()</c> and discards the old
    /// row, so the definition the operator opened is GONE and a different record stands in its place. Every
    /// screen that holds the id it was opened over — the alteration screen itself, the arrow highlight that is
    /// restored BY ID after a rebuild — is then pointing at a row that no longer exists. That is the exact defect
    /// shape this area has been demoted for three times: an alteration path whose only button ran a bare
    /// unconditional create, so the correction was lost or appended as a second version.</para>
    ///
    /// <para><b>The (scope, target) invariant is preserved rather than silently broken.</b> At most ONE definition
    /// may exist per (scope, target) (RQ-32). Re-pointing a definition at a (scope, target) that ANOTHER
    /// definition already occupies is refused by name — upserting over it here would delete a second,
    /// unrelated definition the operator never named, which an alteration must never do. Re-accepting a
    /// definition over its OWN (scope, target) is of course allowed.</para>
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// No definition with that id; the target does not exist; another definition already owns that
    /// (scope, target); or the <see cref="ReorderDefinition"/> constructor refuses the figures.
    /// </exception>
    public ReorderDefinition Alter(
        Guid definitionId,
        ReorderScope scope,
        Guid targetId,
        bool reorderAdvanced = false,
        decimal? reorderQuantity = null,
        bool minQtyAdvanced = false,
        decimal? minOrderQuantity = null,
        int? periodCount = null,
        ExpiryPeriodUnit? periodUnit = null,
        ReorderCriteria? criteria = null)
    {
        var existing = _company.FindReorderDefinition(definitionId)
            ?? throw new InvalidOperationException($"Reorder definition {definitionId} not found.");

        if (!TargetExists(scope, targetId))
            throw new InvalidOperationException($"{scope} {targetId} not found.");

        if (_company.FindReorderDefinition(scope, targetId) is { } owner && owner.Id != definitionId)
            throw new InvalidOperationException(
                $"Another reorder definition already covers this {scope}. Alter or delete that one instead.");

        // Build (and validate) BEFORE removing the old row, so a refusal leaves the company untouched (ER-12) —
        // the same ordering CreateOrUpdate uses, for the same reason.
        var altered = new ReorderDefinition(existing.Id, scope, targetId, reorderAdvanced, reorderQuantity,
            minQtyAdvanced, minOrderQuantity, periodCount, periodUnit, criteria);

        _company.RemoveReorderDefinition(existing);
        _company.AddReorderDefinition(altered);
        return altered;
    }

    /// <summary>Deletes a reorder definition by id.</summary>
    public void Delete(Guid definitionId)
    {
        var definition = _company.FindReorderDefinition(definitionId)
            ?? throw new InvalidOperationException($"Reorder definition {definitionId} not found.");
        _company.RemoveReorderDefinition(definition);
    }

    private string TargetName(ReorderScope scope, Guid targetId) => scope switch
    {
        ReorderScope.Item => _company.FindStockItem(targetId)?.Name ?? targetId.ToString(),
        ReorderScope.Group => _company.FindStockGroup(targetId)?.Name ?? targetId.ToString(),
        ReorderScope.Category => _company.FindStockCategory(targetId)?.Name ?? targetId.ToString(),
        _ => targetId.ToString(),
    };

    private bool TargetExists(ReorderScope scope, Guid targetId) => scope switch
    {
        ReorderScope.Item => _company.FindStockItem(targetId) is not null,
        ReorderScope.Group => _company.FindStockGroup(targetId) is not null,
        ReorderScope.Category => _company.FindStockCategory(targetId) is not null,
        _ => false,
    };
}
