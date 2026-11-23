using System.Text;
using Apex.Ledger.Domain;
using Apex.Ledger.Services;

namespace Apex.Ledger.Io.Tests;

/// <summary>
/// v70 (census row <b>2.9</b>, IV-328) — a canonical document written before v70 may file a budget under a
/// <b>GROUP</b> (the only parent v69 could store). That parent imports as <b>Primary</b>, the treatment the v69 → v70
/// migration gives the same value on disk, instead of refusing the whole import.
///
/// <para><b>Mutation (run 2026-11-23, wave 12 hc continuation):</b> removing the
/// <c>!plan.IsLegacyGroupBudgetParent(uid, _target)</c> clause from <c>CompanyImportService</c>'s budget validation
/// reddens <see cref="A_pre_v70_document_filing_a_budget_under_a_group_imports_it_under_Primary_with_its_lines"/>
/// (import refused: "filed under a budget that is neither imported nor present").</para>
/// </summary>
public sealed class CanonicalBudgetLegacyGroupParentImportTests
{
    private static readonly DateOnly FyStart = new(2024, 4, 1);
    private static readonly DateOnly FyEnd = new(2025, 3, 31);

    [Fact]
    public void A_pre_v70_document_filing_a_budget_under_a_group_imports_it_under_Primary_with_its_lines()
    {
        var c = CompanyFactory.CreateSeeded("Legacy Budget Exporter", FyStart);
        var group = c.Groups.First(g => g.Name == "Indirect Expenses");
        var ledger = c.Ledgers.First().Id;
        var budget = new Budget(Guid.NewGuid(), "Old Group Budget", FyStart, FyEnd);
        budget.AddLine(BudgetLine.ForLedger(ledger, BudgetType.OnNettTransactions, new Money(1_234.57m)));
        c.AddBudget(budget);

        // Re-create the pre-v70 shape: the budget element names a GROUP id as its parent.
        var xml = Encoding.UTF8.GetString(CanonicalXml.Export(c));
        var anchor = $"<budget id=\"{budget.Id:D}\"";
        Assert.Contains(anchor, xml);
        Assert.DoesNotContain("underId=", xml);
        var legacy = xml.Replace(anchor, $"{anchor} underId=\"{group.Id:D}\"", StringComparison.Ordinal);

        var (model, errors) = CanonicalXml.Parse(Encoding.UTF8.GetBytes(legacy));
        Assert.Empty(errors);
        Assert.Equal(group.Id, model!.Payload.Budgets.Single().UnderId);

        var target = CompanyFactory.CreateSeeded("Legacy Budget Importer", FyStart);
        var result = new CompanyImportService(target).Apply(model, DuplicatePolicy.Skip);

        Assert.True(result.Applied, string.Join(" | ", result.Errors));
        var imported = target.FindBudgetByName("Old Group Budget")!;
        Assert.Null(imported.UnderId);
        Assert.Equal(1_234.57m, imported.Lines.Single().Amount.Amount);
    }
}
