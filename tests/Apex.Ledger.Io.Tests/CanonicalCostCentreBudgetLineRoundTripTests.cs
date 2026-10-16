using System.Text;
using Apex.Ledger.Domain;
using Apex.Ledger.Services;

namespace Apex.Ledger.Io.Tests;

/// <summary>
/// v67 (census row <b>2.9</b>) — a <b>cost-centre</b> budget line survives a JSON and an XML round trip.
///
/// <para>🔴 <b>WHY THIS FILE EXISTS: THE W2 ADVERSARIAL REVIEW REPRODUCED A BLOCKING DEFECT HERE.</b> v67
/// added <c>BudgetLine.ForCostCentre</c>, <c>budget_lines.cost_centre_id</c>, the engine arm and the target
/// picker on the Budget master — so an operator can create a cost-centre budget line today. But
/// <see cref="BudgetLineDto"/> still modelled only TWO targets. A cost-centre line therefore exported with
/// <b>no target attribute at all</b> (<c>&lt;line budgetType="…" amountPaisa="…" /&gt;</c>), and re-importing
/// that document hit <c>bl.LedgerId!.Value</c> in <c>ImportPlan</c> and aborted the <b>WHOLE</b> import —
/// every master, every voucher — with <i>"Import aborted and rolled back (nothing applied): Nullable object
/// must have a value."</i> Not a gap: a closed storage gap with an open transport gap that takes an unrelated
/// feature down with it.</para>
///
/// <para><b>ER-13.</b> The XML attribute is emitted with <c>OptId()</c>, exactly as <c>groupId</c> and
/// <c>ledgerId</c> are, so a budget that targets only groups and ledgers emits a document byte-identical to a
/// pre-v67 one, and a pre-v67 document reads back with <c>CostCentreId == null</c>.</para>
///
/// <para><b>R7.</b> <c>help.tallysolutions.com/budgets-tally/</c>, opened BY CONTENT 2026-10-16:
/// <i>"Set Groups to Yes"</i>, <i>"Set Ledgers to Yes"</i>, <i>"Set Cost Centres to Yes"</i>.</para>
/// </summary>
public sealed class CanonicalCostCentreBudgetLineRoundTripTests
{
    private static readonly DateOnly FyStart = new(2024, 4, 1);
    private static readonly DateOnly FyEnd = new(2025, 3, 31);

    private static Company BuildCompany()
    {
        var c = CompanyFactory.CreateSeeded("Cost Centre Budgeter", FyStart);
        var category = c.CostCategories.First();
        var north = new CostCentre(Guid.NewGuid(), "North Region", category.Id);
        var south = new CostCentre(Guid.NewGuid(), "South Region", category.Id);
        c.AddCostCentre(north);
        c.AddCostCentre(south);

        var budget = new Budget(Guid.NewGuid(), "FY Budget", FyStart, FyEnd);
        // A ledger line beside the two centre lines, so the test can tell "the targets survived" from
        // "every line became the same thing".
        budget.AddLine(BudgetLine.ForLedger(
            c.Ledgers.First().Id, BudgetType.OnClosingBalance, new Money(10_000m)));
        budget.AddLine(BudgetLine.ForCostCentre(
            north.Id, BudgetType.OnNettTransactions, new Money(50_000m)));
        budget.AddLine(BudgetLine.ForCostCentre(
            south.Id, BudgetType.OnNettTransactions, new Money(25_000m)));
        c.AddBudget(budget);
        return c;
    }

    private static Company ImportInto(CanonicalModel model, string name)
    {
        var target = CompanyFactory.CreateSeeded(name, FyStart);
        var result = new CompanyImportService(target).Apply(model, DuplicatePolicy.Skip);
        Assert.True(result.Applied, string.Join(" | ", result.Errors));
        return target;
    }

    private static void AssertTargetsSurvived(Company target)
    {
        var budget = target.Budgets.Single(b => b.Name == "FY Budget");
        Assert.Equal(3, budget.Lines.Count);

        var north = target.FindCostCentreByName("North Region")!;
        var south = target.FindCostCentreByName("South Region")!;

        var northLine = budget.Lines.Single(l => l.CostCentreId == north.Id);
        Assert.Equal(50_000m, northLine.Amount.Amount);
        Assert.Null(northLine.GroupId);
        Assert.Null(northLine.LedgerId);

        var southLine = budget.Lines.Single(l => l.CostCentreId == south.Id);
        Assert.Equal(25_000m, southLine.Amount.Amount);

        // The ledger line is untouched — a mapper that bound one target for every row would not show here
        // if the test only looked at the centre lines.
        Assert.Single(budget.Lines, l => l.IsLedgerTarget && l.Amount.Amount == 10_000m);
    }

    [Fact]
    public void A_cost_centre_budget_line_round_trips_through_json_and_xml_and_imports()
    {
        var c = BuildCompany();

        var json = CanonicalJson.Export(c);
        var (jsonModel, jsonErrors) = CanonicalJson.Parse(json);
        Assert.Empty(jsonErrors);
        Assert.NotNull(jsonModel);
        Assert.Equal(json, CanonicalJson.Export(jsonModel!));      // byte-stable
        Assert.Equal(
            2, jsonModel!.Payload.Budgets.Single().Lines.Count(l => l.CostCentreId is not null));
        AssertTargetsSurvived(ImportInto(jsonModel!, "Fresh Budget Json Co"));

        var xml = CanonicalXml.Export(c);
        var (xmlModel, xmlErrors) = CanonicalXml.Parse(xml);
        Assert.Empty(xmlErrors);
        Assert.NotNull(xmlModel);
        Assert.Equal(xml, CanonicalXml.Export(xmlModel!));         // byte-stable
        Assert.Equal(
            2, xmlModel!.Payload.Budgets.Single().Lines.Count(l => l.CostCentreId is not null));
        AssertTargetsSurvived(ImportInto(xmlModel!, "Fresh Budget Xml Co"));

        Assert.DoesNotContain("Tally", Encoding.UTF8.GetString(json), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 🔴 <b>ER-13: a budget that targets only groups and ledgers emits no <c>costCentreId</c> attribute on
    /// any <c>line</c>,</b> so a document from such a book is byte-identical to a pre-v67 one; and a pre-v67
    /// document, which carries the attribute nowhere, imports exactly as it always did.
    /// </summary>
    [Fact]
    public void A_group_or_ledger_budget_emits_no_cost_centre_attribute_and_a_pre_v67_document_still_imports()
    {
        var plain = CompanyFactory.CreateSeeded("Plain Budget Co", FyStart);
        var budget = new Budget(Guid.NewGuid(), "Plain Budget", FyStart, FyEnd);
        budget.AddLine(BudgetLine.ForLedger(
            plain.Ledgers.First().Id, BudgetType.OnClosingBalance, new Money(1_000m)));
        plain.AddBudget(budget);

        var xml = Encoding.UTF8.GetString(CanonicalXml.Export(plain));
        Assert.Contains("<line ", xml);
        foreach (var element in xml.Split("<line ").Skip(1))
            Assert.DoesNotContain("costCentreId=", element.Split('>')[0]);

        var (model, errors) = CanonicalXml.Parse(Encoding.UTF8.GetBytes(xml));
        Assert.Empty(errors);
        Assert.All(model!.Payload.Budgets.Single().Lines, l => Assert.Null(l.CostCentreId));
        var imported = ImportInto(model!, "Fresh Plain Budget Co");
        Assert.True(imported.Budgets.Single(b => b.Name == "Plain Budget").Lines.Single().IsLedgerTarget);
    }

    /// <summary>
    /// A document whose cost-centre budget line names a centre that is neither imported nor present yields a
    /// NAMED error the operator can act on, not a <c>KeyNotFoundException</c> out of the plan.
    /// </summary>
    [Fact]
    public void A_budget_line_naming_an_unknown_cost_centre_is_a_named_error()
    {
        var c = BuildCompany();
        var xml = Encoding.UTF8.GetString(CanonicalXml.Export(c));
        // Strip the two cost centres out of the document, leaving the budget lines pointing at nothing.
        var stripped = string.Join("\n", xml.Split('\n')
            .Where(l => !l.Contains("<costCentre ", StringComparison.Ordinal)));

        var (model, errors) = CanonicalXml.Parse(Encoding.UTF8.GetBytes(stripped));
        Assert.Empty(errors);

        var target = CompanyFactory.CreateSeeded("Unknown Centre Co", FyStart);
        var result = new CompanyImportService(target).Apply(model!, DuplicatePolicy.Skip);
        Assert.False(result.Applied);
        Assert.Contains(result.Errors, e => e.Contains("cost centre", StringComparison.OrdinalIgnoreCase));
    }
}
