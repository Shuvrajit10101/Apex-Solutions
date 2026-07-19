using System;
using System.Linq;
using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Io;
using Apex.Ledger.Reports;
using Apex.Ledger.Services;
using Xunit;

namespace Apex.Ledger.Tests;

/// <summary>
/// WI-7 — the shared derive-Nature-from-parent invariant, locked at the canonical-import boundary (the live
/// Balance-Sheet-corruption path). <see cref="ImportPlan"/> historically accepted the caller-supplied group nature
/// verbatim, so a canonical file declaring <c>Nature=Asset</c> under Current Liabilities silently landed a payable
/// on the <b>asset</b> side of the sheet — it still balanced, so nothing failed loudly. These tests prove a
/// contradicting-nature group is now REJECTED (all-or-nothing, target untouched), and that a correctly-natured
/// custom group still imports losslessly. The engine <see cref="GroupService.CreateGroup"/> derives the nature so
/// the corruption cannot enter through the master screen either.
/// </summary>
public class GroupNatureImportGuardTests
{
    private static readonly DateOnly From = new(2021, 4, 1);

    // ------------------------------------------------------------------ import guard (the live defect)

    [Fact]
    public void Import_rejects_a_group_whose_nature_contradicts_its_parent()
    {
        // A seeded source + a custom "Salary Payable" group whose parent is Current Liabilities (a LIABILITY head)
        // but whose declared nature is ASSET — the exact contradiction WI-7 fixes. Domain AddGroup does no
        // validation, so the contradiction is constructed directly, exported, and offered to the importer.
        var source = CompanyFactory.CreateSeeded("Contradiction Source", From, From);
        var currentLiabilities = source.FindGroupByName("Current Liabilities")!;
        source.AddGroup(new Group(Guid.NewGuid(), "Salary Payable", GroupNature.Asset, currentLiabilities.Id,
            alias: null, isPredefined: false));

        var (model, errors) = CanonicalJson.Parse(CanonicalJson.Export(source));
        Assert.Empty(errors);
        Assert.NotNull(model);

        var fresh = CompanyFactory.CreateSeeded("Import Target", From, From);
        var groupsBefore = fresh.Groups.Count;

        var result = new CompanyImportService(fresh).Apply(model!, DuplicatePolicy.Skip);

        // REJECTED — nothing applied, and the message names the nature contradiction.
        Assert.False(result.Applied);
        Assert.Contains(result.Errors, e =>
            e.Contains("nature", StringComparison.OrdinalIgnoreCase) ||
            e.Contains("contradict", StringComparison.OrdinalIgnoreCase));

        // The target is unchanged: still only its 28 seeded groups, no leaked "Salary Payable".
        Assert.Equal(groupsBefore, fresh.Groups.Count);
        Assert.Null(fresh.FindGroupByName("Salary Payable"));
    }

    [Fact]
    public void Import_accepts_a_custom_group_whose_nature_matches_its_parent()
    {
        // The same shape, correctly natured: a Liability "Salary Payable" under Current Liabilities imports fine
        // and resolves to Liability nature (proving the guard does not reject legitimate custom groups).
        var source = CompanyFactory.CreateSeeded("Correct Source", From, From);
        var currentLiabilities = source.FindGroupByName("Current Liabilities")!;
        source.AddGroup(new Group(Guid.NewGuid(), "Salary Payable", GroupNature.Liability, currentLiabilities.Id,
            alias: null, isPredefined: false));

        var (model, errors) = CanonicalJson.Parse(CanonicalJson.Export(source));
        Assert.Empty(errors);

        var fresh = CompanyFactory.CreateSeeded("Import Target OK", From, From);
        var result = new CompanyImportService(fresh).Apply(model!, DuplicatePolicy.Skip);

        Assert.True(result.Applied, string.Join(" | ", result.Errors));
        var imported = fresh.FindGroupByName("Salary Payable");
        Assert.NotNull(imported);
        Assert.Equal(GroupNature.Liability, imported!.Nature);
        Assert.Equal(GroupNature.Liability, ClassificationRules.PrimaryNatureOf(imported, fresh));
    }

    // ------------------------------------------------------------------ engine service (derive, never accept)

    [Fact]
    public void CreateGroup_derives_liability_nature_from_a_current_liabilities_parent()
    {
        var company = CompanyFactory.CreateSeeded("Service Co", From, From);
        var currentLiabilities = company.FindGroupByName("Current Liabilities")!;

        var group = new GroupService(company).CreateGroup("Salary Payable", currentLiabilities.Id);

        Assert.Equal(GroupNature.Liability, group.Nature);
        Assert.Equal(currentLiabilities.Id, group.ParentId);
        Assert.False(group.IsPredefined);
        Assert.Equal(GroupNature.Liability, ClassificationRules.PrimaryNatureOf(group, company));
    }

    [Fact]
    public void CreateGroup_rejects_a_blank_name_a_duplicate_a_missing_parent_and_a_null_parent()
    {
        var company = CompanyFactory.CreateSeeded("Service Reject Co", From, From);
        var currentLiabilities = company.FindGroupByName("Current Liabilities")!;
        var service = new GroupService(company);

        Assert.Throws<InvalidOperationException>(() => service.CreateGroup("   ", currentLiabilities.Id));
        // Null parent is rejected — a group's nature can only be derived from a parent.
        Assert.Throws<InvalidOperationException>(() => service.CreateGroup("Orphan", null));
        // Unknown parent is rejected.
        Assert.Throws<InvalidOperationException>(() => service.CreateGroup("Ghost Child", Guid.NewGuid()));

        service.CreateGroup("Salary Payable", currentLiabilities.Id);
        // Duplicate name (case-insensitive) rejected.
        Assert.Throws<InvalidOperationException>(() => service.CreateGroup("salary payable", currentLiabilities.Id));
        // Collision with the reserved Profit & Loss head is rejected (head-including lookup).
        Assert.Throws<InvalidOperationException>(() => service.CreateGroup("Profit & Loss A/c", currentLiabilities.Id));
    }

    [Fact]
    public void ValidateNatureAgainstParent_throws_on_a_contradiction_and_passes_on_a_match()
    {
        var company = CompanyFactory.CreateSeeded("Validate Co", From, From);
        var currentLiabilities = company.FindGroupByName("Current Liabilities")!;
        var fixedAssets = company.FindGroupByName("Fixed Assets")!;

        // A Liability declared under Current Liabilities is fine; an Asset under it contradicts.
        GroupService.ValidateNatureAgainstParent(GroupNature.Liability, currentLiabilities.Id, company);
        GroupService.ValidateNatureAgainstParent(GroupNature.Asset, fixedAssets.Id, company);
        Assert.Throws<InvalidOperationException>(() =>
            GroupService.ValidateNatureAgainstParent(GroupNature.Asset, currentLiabilities.Id, company));
        // A null parent (a primary group) is never a contradiction.
        GroupService.ValidateNatureAgainstParent(GroupNature.Income, null, company);
    }
}
