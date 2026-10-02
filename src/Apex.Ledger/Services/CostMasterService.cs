using System;
using System.Collections.Generic;
using System.Linq;
using Apex.Ledger.Domain;

namespace Apex.Ledger.Services;

/// <summary>
/// The <b>Alter</b> and <b>Delete</b> verbs for the two cost masters — <see cref="CostCategory"/> (census 2.7)
/// and <see cref="CostCentre"/> (census 2.8). Added at wave 28 (clusters C2 + C3), which is when either master
/// first became alterable or deletable from anywhere in the product.
///
/// <para><b>WHY THIS FILE EXISTS AT ALL, AND WHY IT DOES NOT CARRY <c>Create</c>.</b> Both masters shipped with
/// their CREATE logic inline in the two view models — they construct the domain object and call
/// <c>Company.AddCostCategory</c> / <c>AddCostCentre</c> directly, and there has never been a cost service to put
/// it in. Moving create here as part of wave 28 would have rewritten two working, tested screens for tidiness
/// while the wave's actual subject — two verbs that did not exist — was still unbuilt, and it would have made
/// every create test in the suite a change this slice has to defend. So this service owns exactly the two NEW
/// verbs. The asymmetry is real and is recorded rather than hidden: a later slice that moves create here is
/// welcome, and nothing in this file is shaped to prevent it.</para>
///
/// <para><b>The deletion guards are NOT here.</b> They live in <see cref="MasterDeletionRules"/> with the other
/// eight, so that every master Alt+D reaches refuses through one file, in one voice, with one wording for the
/// count and the remedy. See <see cref="MasterDeletionRules.EnsureCostCategoryDeletable"/> and
/// <see cref="MasterDeletionRules.EnsureCostCentreDeletable"/>, which also record the vendor clauses the two
/// refusals are grounded in.</para>
///
/// <para>Pure and framework-agnostic like every other engine service: it references the domain and nothing else,
/// so it is directly unit-testable with no persistence and no UI.</para>
/// </summary>
public sealed class CostMasterService
{
    private readonly Company _company;

    public CostMasterService(Company company)
        => _company = company ?? throw new ArgumentNullException(nameof(company));

    // ------------------------------------------------------------------ Cost categories

    /// <summary>
    /// <b>Alters</b> a cost category in place — rename and the two allocation flags — resolved by its stable
    /// <paramref name="categoryId"/>, so every centre filed under it and every posted allocation along it follows
    /// a rename automatically (they reference the Guid).
    ///
    /// <para>The vendor's Cost Category screen carries <i>Name &amp; alias</i>, <i>Allocate Revenue Items</i> and
    /// <i>Allocate Non-revenue items</i> (help.tallysolutions.com/cost-centre-or-profit-centre-tally/, fetched
    /// 2026-09-14). <b>Alias is not altered here because the domain type has no alias field</b> — capturing it is
    /// its own gap, recorded against row 2.7 and deliberately not smuggled into this verb.</para>
    ///
    /// <para><b>The "at least one must be Yes" rule is re-checked on alter.</b> The constructor enforces it at
    /// create time, but an alter writes the two flags straight onto an existing object and would otherwise be the
    /// one route to a category that allocates nothing — a master that silently cannot receive any allocation at
    /// all. The check runs BEFORE either flag is written, so a rejected alteration leaves the category untouched
    /// rather than half-applied.</para>
    ///
    /// <para><b>The predefined Primary category is renameable.</b> <see cref="CostCategory.IsPredefined"/> blocks
    /// DELETION (it is the fallback every centre is filed under) but says nothing about its name, and a book that
    /// calls its default axis something else is ordinary.</para>
    /// </summary>
    /// <exception cref="InvalidOperationException">The category does not exist, the name is empty or clashes with
    /// another category, or neither allocation flag is set.</exception>
    public CostCategory AlterCostCategory(
        Guid categoryId, string name, bool allocateRevenueItems, bool allocateNonRevenueItems)
    {
        var category = _company.FindCostCategory(categoryId)
            ?? throw new InvalidOperationException($"Cost category {categoryId} not found.");

        var trimmed = RequireName(name, "cost category");
        if (_company.FindCostCategoryByName(trimmed) is { } clash && clash.Id != categoryId)
            throw new InvalidOperationException($"A cost category named '{trimmed}' already exists.");
        if (!allocateRevenueItems && !allocateNonRevenueItems)
            throw new InvalidOperationException(
                "A cost category must allocate revenue and/or non-revenue items (at least one must be Yes).");

        category.Name = trimmed;
        category.AllocateRevenueItems = allocateRevenueItems;
        category.AllocateNonRevenueItems = allocateNonRevenueItems;
        return category;
    }

    /// <summary>
    /// Deletes a cost category. <see cref="MasterDeletionRules.EnsureCostCategoryDeletable"/> owns the refusal —
    /// the predefined category, any posted allocation along it, and the vendor's own stated condition that a
    /// category may go only when no cost centre is grouped under it.
    /// </summary>
    /// <exception cref="InvalidOperationException">The category does not exist, or the guard refuses.</exception>
    public void DeleteCostCategory(Guid categoryId)
    {
        var category = _company.FindCostCategory(categoryId)
            ?? throw new InvalidOperationException($"Cost category {categoryId} not found.");
        MasterDeletionRules.EnsureCostCategoryDeletable(_company, category);
        _company.RemoveCostCategory(category);
    }

    // ------------------------------------------------------------------ Cost centres

    /// <summary>
    /// <b>Alters</b> a cost centre in place — rename, re-categorise and re-parent — resolved by its stable
    /// <paramref name="centreId"/>. The vendor's Cost Centre screen is <i>Name &amp; alias</i>, <i>Under</i>
    /// (Primary or an existing centre) and <i>Category</i>; alias is again absent from the domain type and is
    /// recorded as row 2.8's own gap rather than invented here.
    ///
    /// <para><b>Three guards, and the second and third are the ones that matter.</b></para>
    /// <list type="number">
    ///   <item>The name is required and unique EXCLUDING this centre, so an alter that leaves the name alone is
    ///     possible — the commonest alteration there is.</item>
    ///   <item><b>A parent must be in the SAME category as the centre's new category.</b> The domain states the
    ///     invariant (<see cref="CostCentre"/>: "another centre in the <b>same</b> category") and the create
    ///     screen honours it by scoping its parent picker; an alter that changed the category and kept the old
    ///     parent would break it silently, leaving a centre whose roll-up crosses two axes.</item>
    ///   <item><b>A re-parent may not create a cycle.</b> Walked up from the proposed parent, exactly as the
    ///     inventory masters' hierarchy checks do. Without it a centre could be made its own ancestor and every
    ///     hierarchical cost report would spin forever on the next run.</item>
    /// </list>
    ///
    /// <para>All three run BEFORE anything is written, so a rejected alteration leaves the centre untouched.</para>
    /// </summary>
    /// <exception cref="InvalidOperationException">The centre or category does not exist, the name is empty or
    /// clashes, the parent is in another category, or the re-parent would form a cycle.</exception>
    public CostCentre AlterCostCentre(Guid centreId, string name, Guid categoryId, Guid? parentId)
    {
        var centre = _company.FindCostCentre(centreId)
            ?? throw new InvalidOperationException($"Cost centre {centreId} not found.");
        if (_company.FindCostCategory(categoryId) is null)
            throw new InvalidOperationException($"Cost category {categoryId} not found.");

        var trimmed = RequireName(name, "cost centre");
        if (_company.FindCostCentreByName(trimmed) is { } clash && clash.Id != centreId)
            throw new InvalidOperationException($"A cost centre named '{trimmed}' already exists.");

        if (parentId is { } pid)
        {
            if (pid == centreId)
                throw new InvalidOperationException("A cost centre cannot be its own parent.");

            var parent = _company.FindCostCentre(pid)
                ?? throw new InvalidOperationException($"Parent cost centre {pid} not found.");
            if (parent.CategoryId != categoryId)
                throw new InvalidOperationException(
                    $"Cost centre '{trimmed}' cannot sit under '{parent.Name}': a parent centre must be in the "
                    + "same cost category.");

            EnsureNoCycle(centreId, pid, trimmed);
        }

        centre.Name = trimmed;
        centre.CategoryId = categoryId;
        centre.ParentId = parentId;
        return centre;
    }

    /// <summary>
    /// Deletes a cost centre. <see cref="MasterDeletionRules.EnsureCostCentreDeletable"/> owns the refusal — the
    /// vendor's allocation condition, plus sub-centres and any godown designated as this centre's job/project.
    /// </summary>
    /// <exception cref="InvalidOperationException">The centre does not exist, or the guard refuses.</exception>
    public void DeleteCostCentre(Guid centreId)
    {
        var centre = _company.FindCostCentre(centreId)
            ?? throw new InvalidOperationException($"Cost centre {centreId} not found.");
        MasterDeletionRules.EnsureCostCentreDeletable(_company, centre);
        _company.RemoveCostCentre(centre);
    }

    // ------------------------------------------------------------------ helpers

    /// <summary>
    /// Refuses a re-parent that would make <paramref name="centreId"/> its own ancestor, by walking UP from the
    /// proposed parent until it runs out of parents or meets the centre again.
    ///
    /// <para>The <c>seen</c> set is not decoration: a book whose cost-centre graph ALREADY contains a cycle —
    /// which import can produce, since nothing validated the hierarchy before this wave — would otherwise make
    /// this walk loop forever and hang the screen instead of refusing the edit.</para>
    /// </summary>
    private void EnsureNoCycle(Guid centreId, Guid proposedParentId, string name)
    {
        var seen = new HashSet<Guid>();
        var cursor = _company.FindCostCentre(proposedParentId);
        while (cursor is not null)
        {
            if (cursor.Id == centreId || !seen.Add(cursor.Id))
                throw new InvalidOperationException($"Cost centre '{name}' would form a nesting cycle.");
            cursor = cursor.ParentId is { } next ? _company.FindCostCentre(next) : null;
        }
    }

    private static string RequireName(string? value, string what)
    {
        var trimmed = (value ?? string.Empty).Trim();
        if (trimmed.Length == 0)
            throw new InvalidOperationException($"A {what} name is required.");
        return trimmed;
    }
}
