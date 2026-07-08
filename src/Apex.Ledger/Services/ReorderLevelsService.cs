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

    /// <summary>Deletes a reorder definition by id.</summary>
    public void Delete(Guid definitionId)
    {
        var definition = _company.FindReorderDefinition(definitionId)
            ?? throw new InvalidOperationException($"Reorder definition {definitionId} not found.");
        _company.RemoveReorderDefinition(definition);
    }

    private bool TargetExists(ReorderScope scope, Guid targetId) => scope switch
    {
        ReorderScope.Item => _company.FindStockItem(targetId) is not null,
        ReorderScope.Group => _company.FindStockGroup(targetId) is not null,
        ReorderScope.Category => _company.FindStockCategory(targetId) is not null,
        _ => false,
    };
}
