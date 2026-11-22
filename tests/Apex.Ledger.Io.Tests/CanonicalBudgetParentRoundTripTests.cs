using System.Text;
using Apex.Ledger.Domain;
using Apex.Ledger.Services;

namespace Apex.Ledger.Io.Tests;

/// <summary>
/// v70 (census row <b>2.9</b>, "no nested budget") — a budget's parent <b>budget</b> survives a JSON and an XML
/// round trip and an import into a fresh company.
///
/// <para>🔴 <b>WHAT THIS FILE PINS.</b> Until v70 <c>BudgetDto.UnderId</c> named a GROUP, and <c>ImportPlan</c>
/// resolved it through the group map: a document carrying a parent-budget id would have thrown
/// <i>"Group reference … could not be resolved"</i> and rolled the WHOLE import back. The importer now resolves
/// it through the budget map, parents first, so a child that sorts BEFORE its parent in the document (the
/// mapper orders budgets by name) still lands under the parent's NEW id.</para>
///
/// <para><b>Mutation (run 2026-11-22):</b> restoring <c>ResolveGroupId(uid, groupId, t)</c> for the parent in
/// <c>ImportPlan</c> reddens <see cref="A_budget_hierarchy_round_trips_through_json_and_xml_and_imports_under_the_new_ids"/>
/// (import not applied: "Group reference … could not be resolved").</para>
/// </summary>
public sealed class CanonicalBudgetParentRoundTripTests
{
    private static readonly DateOnly FyStart = new(2024, 4, 1);
    private static readonly DateOnly FyEnd = new(2025, 3, 31);

    /// <summary>"Alpha Sales Budget" (child) sorts before "Zed Department Budget" (parent), and a grandchild sits
    /// under the child — so the document order is child-first and a parents-first import is required.</summary>
    private static Company BuildCompany()
    {
        var c = CompanyFactory.CreateSeeded("Budget Tree Exporter", FyStart);
        var ledger = c.Ledgers.First().Id;
        var parent = new Budget(Guid.NewGuid(), "Zed Department Budget", FyStart, FyEnd);
        parent.AddLine(BudgetLine.ForLedger(ledger, BudgetType.OnClosingBalance, new Money(10_000.37m)));
        var child = new Budget(Guid.NewGuid(), "Alpha Sales Budget", FyStart, FyEnd, underId: parent.Id);
        child.AddLine(BudgetLine.ForLedger(ledger, BudgetType.OnNettTransactions, new Money(2_500.05m)));
        var grandchild = new Budget(Guid.NewGuid(), "Beta Region Budget", FyStart, FyEnd, underId: child.Id);
        grandchild.AddLine(BudgetLine.ForLedger(ledger, BudgetType.OnNettTransactions, new Money(999.99m)));
        c.AddBudget(grandchild);
        c.AddBudget(child);
        c.AddBudget(parent);
        return c;
    }

    private static Company ImportInto(CanonicalModel model, string name)
    {
        var target = CompanyFactory.CreateSeeded(name, FyStart);
        var result = new CompanyImportService(target).Apply(model, DuplicatePolicy.Skip);
        Assert.True(result.Applied, string.Join(" | ", result.Errors));
        return target;
    }

    private static void AssertTreeSurvived(Company source, Company target)
    {
        var parent = target.FindBudgetByName("Zed Department Budget")!;
        var child = target.FindBudgetByName("Alpha Sales Budget")!;
        var grandchild = target.FindBudgetByName("Beta Region Budget")!;

        Assert.Null(parent.UnderId);
        Assert.Equal(parent.Id, child.UnderId);
        Assert.Equal(child.Id, grandchild.UnderId);
        // The ids are NEW — the import re-keys every master — so equality above is to the target's own parent,
        // never to the source's.
        Assert.DoesNotContain(source.Budgets, b => b.Id == parent.Id);
        Assert.Equal(2_500.05m, child.Lines.Single().Amount.Amount);
    }

    [Fact]
    public void A_budget_hierarchy_round_trips_through_json_and_xml_and_imports_under_the_new_ids()
    {
        var c = BuildCompany();

        var json = CanonicalJson.Export(c);
        var (jsonModel, jsonErrors) = CanonicalJson.Parse(json);
        Assert.Empty(jsonErrors);
        Assert.Equal(json, CanonicalJson.Export(jsonModel!));      // byte-stable
        var jsonChild = jsonModel!.Payload.Budgets.Single(b => b.Name == "Alpha Sales Budget");
        Assert.Equal(c.FindBudgetByName("Zed Department Budget")!.Id, jsonChild.UnderId);
        AssertTreeSurvived(c, ImportInto(jsonModel!, "Budget Tree Json Co"));

        var xml = CanonicalXml.Export(c);
        var (xmlModel, xmlErrors) = CanonicalXml.Parse(xml);
        Assert.Empty(xmlErrors);
        Assert.Equal(xml, CanonicalXml.Export(xmlModel!));         // byte-stable
        var text = Encoding.UTF8.GetString(xml);
        Assert.Contains($"underId=\"{c.FindBudgetByName("Zed Department Budget")!.Id:D}\"", text);
        AssertTreeSurvived(c, ImportInto(xmlModel!, "Budget Tree Xml Co"));
    }

    /// <summary>A document whose budget is filed under a budget that is neither imported nor present yields a NAMED
    /// error, and nothing is applied.</summary>
    [Fact]
    public void A_budget_filed_under_an_unknown_budget_is_a_named_error()
    {
        var c = BuildCompany();
        var xml = Encoding.UTF8.GetString(CanonicalXml.Export(c));
        var parentId = c.FindBudgetByName("Zed Department Budget")!.Id.ToString("D");
        // Drop the parent budget element (it is one <budget …> … </budget> block), leaving the child pointing at it.
        var start = xml.IndexOf($"<budget id=\"{parentId}\"", StringComparison.Ordinal);
        Assert.True(start >= 0, "the parent budget element was not found in the document");
        var end = xml.IndexOf("</budget>", start, StringComparison.Ordinal) + "</budget>".Length;
        var stripped = xml.Remove(start, end - start);

        var (model, errors) = CanonicalXml.Parse(Encoding.UTF8.GetBytes(stripped));
        Assert.Empty(errors);
        Assert.Equal(2, model!.Payload.Budgets.Count);

        var target = CompanyFactory.CreateSeeded("Unknown Parent Co", FyStart);
        var result = new CompanyImportService(target).Apply(model, DuplicatePolicy.Skip);
        Assert.False(result.Applied);
        Assert.Contains(result.Errors, e => e.Contains("filed under a budget", StringComparison.Ordinal));
        Assert.Empty(target.Budgets);
    }
}
