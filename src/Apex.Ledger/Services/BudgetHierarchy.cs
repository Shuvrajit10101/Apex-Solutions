using Apex.Ledger.Domain;

namespace Apex.Ledger.Services;

/// <summary>
/// The budget tree (census row <b>2.9</b>, schema v70): a budget's <see cref="Budget.UnderId"/> names its parent
/// <b>budget</b>, or is <c>null</c> for Primary. This class owns the three questions every surface asks of that
/// tree — may this parent be chosen, which budgets sit directly under this one, and in what order does the List of
/// Budgets show them — so the master screen, the canonical import and the delete guard cannot answer them
/// differently. Pure, framework- and DB-agnostic, and directly unit-tested.
///
/// <para>🔴 <b>Fidelity (R7, ruling 14).</b> <c>help.tallysolutions.com/budgets-tally/</c>, November 3, 2025 version
/// (web.archive.org snapshot 20260310093634, read BY CONTENT 2026-11-22): <i>"you need to create the budget under
/// Primary from the List of Budgets … create specific Department Budgets such as Sales Department Budget, Marketing
/// Department Budget and so on under the main Department Budget. This shows the budget structure in a hierarchical
/// form."</i> The live page (re-written November 19, 2026) instead offers <i>"Primary or any other predefined
/// groups"</i> under <i>Under</i>; the conflict is recorded in docs/invented-vs-cloned.md IV-325. <b>The cycle
/// refusal, the order of the list (name-sorted siblings, depth-first) and every message here are OURS</b> — no
/// source speaks to them (IV-326).</para>
/// </summary>
public static class BudgetHierarchy
{
    /// <summary>
    /// True when <paramref name="candidateParentId"/> is <paramref name="budgetId"/> itself or sits anywhere below
    /// it — i.e. filing <paramref name="budgetId"/> under the candidate would close a loop. A chain that already
    /// loops (only an imported or hand-edited book could carry one) is treated as "would loop" rather than walked
    /// forever.
    /// </summary>
    public static bool IsSelfOrDescendant(Company company, Guid candidateParentId, Guid budgetId)
    {
        ArgumentNullException.ThrowIfNull(company);
        var seen = new HashSet<Guid>();
        Guid? cursor = candidateParentId;
        while (cursor is { } id)
        {
            if (id == budgetId) return true;
            if (!seen.Add(id)) return true;   // an existing loop: refuse rather than spin
            cursor = company.FindBudget(id)?.UnderId;
        }
        return false;
    }

    /// <summary>
    /// The refusal message for filing <paramref name="budgetId"/> (or a budget not yet created, when
    /// <c>null</c>) under <paramref name="parentId"/>, or <c>null</c> when the choice is allowed. <c>null</c>
    /// <paramref name="parentId"/> (Primary) is always allowed.
    /// </summary>
    public static string? ParentRefusal(Company company, Guid? budgetId, Guid? parentId)
    {
        ArgumentNullException.ThrowIfNull(company);
        if (parentId is not { } pid) return null;
        if (company.FindBudget(pid) is not { } parent)
            return "The budget chosen under 'Under' no longer exists.";
        if (budgetId is { } self && IsSelfOrDescendant(company, pid, self))
            return pid == self
                ? "A budget cannot be filed under itself."
                : $"A budget cannot be filed under '{parent.Name}', which is filed under it.";
        return null;
    }

    /// <summary>The budgets filed DIRECTLY under <paramref name="budgetId"/>, name-sorted.</summary>
    public static IReadOnlyList<Budget> ChildrenOf(Company company, Guid budgetId)
    {
        ArgumentNullException.ThrowIfNull(company);
        return company.Budgets
            .Where(b => b.UnderId == budgetId)
            .OrderBy(b => b.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// Every budget in List-of-Budgets order with its depth: Primary budgets name-sorted, each followed depth-first
    /// by the budgets filed under it (also name-sorted). A budget whose parent is missing, or that sits on a loop,
    /// is listed at depth 0 so it can never drop out of the list.
    /// </summary>
    public static IReadOnlyList<(Budget Budget, int Depth)> TreeOrder(Company company)
    {
        ArgumentNullException.ThrowIfNull(company);
        var result = new List<(Budget, int)>(company.Budgets.Count);
        var emitted = new HashSet<Guid>();

        void Emit(Budget b, int depth)
        {
            if (!emitted.Add(b.Id)) return;
            result.Add((b, depth));
            foreach (var child in ChildrenOf(company, b.Id)) Emit(child, depth + 1);
        }

        var byName = company.Budgets.OrderBy(b => b.Name, StringComparer.OrdinalIgnoreCase).ToList();
        foreach (var root in byName.Where(b => b.UnderId is null || company.FindBudget(b.UnderId.Value) is null))
            Emit(root, 0);
        foreach (var stray in byName) Emit(stray, 0);   // only a loop leaves anything un-emitted here
        return result;
    }
}
