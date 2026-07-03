using Apex.Ledger.Domain;

namespace Apex.Ledger.Reports;

/// <summary>
/// One row of a <see cref="BudgetVarianceReport"/> (catalog §7): a budget line's target with its budgeted
/// figure, the actual it reached over the budget's period, and the variance between them.
/// </summary>
/// <remarks>
/// <see cref="Budget"/> and <see cref="Actual"/> are magnitudes. <see cref="Variance"/> is
/// <c>Actual − Budget</c> (positive ⇒ actual over budget, negative ⇒ under). <see cref="VariancePercent"/>
/// is <c>Variance / Budget × 100</c>, or <c>null</c> when the budget is zero (percentage undefined).
/// </remarks>
public sealed record BudgetVarianceLine(
    string TargetName,
    bool IsGroup,
    BudgetType Type,
    Money Budget,
    Money Actual,
    Money Variance,
    decimal? VariancePercent);

/// <summary>
/// The Budget vs Actual projection (catalog §7): for a single budget, each line's budgeted amount against
/// the actual the target reaches over the budget's <see cref="Domain.Budget.PeriodFrom"/>–
/// <see cref="Domain.Budget.PeriodTo"/> window. Pure over (masters, posted vouchers); no UI, no DB.
/// </summary>
/// <remarks>
/// Actual per line:
/// <list type="bullet">
/// <item><b>On Closing Balance</b> — the target's closing balance at PeriodTo (opening + movements ≤ PeriodTo).</item>
/// <item><b>On Nett Transactions</b> — the target's net movement within [PeriodFrom, PeriodTo] (opening excluded).</item>
/// </list>
/// A <b>group</b> target rolls up every ledger under it (any depth) by summing their signed figures; the
/// reported actual is the magnitude of that signed total. A <b>ledger</b> target uses that ledger alone.
/// </remarks>
public sealed record BudgetVarianceReport(
    Guid BudgetId,
    string BudgetName,
    DateOnly PeriodFrom,
    DateOnly PeriodTo,
    IReadOnlyList<BudgetVarianceLine> Lines)
{
    /// <summary>Builds the budget-variance report for <paramref name="budget"/> over its own period.</summary>
    public static BudgetVarianceReport Build(Company company, Budget budget)
    {
        var rows = new List<BudgetVarianceLine>();

        foreach (var line in budget.Lines)
        {
            string targetName;
            decimal actualSigned;

            if (line.IsGroupTarget)
            {
                var group = company.FindGroup(line.GroupId!.Value)
                    ?? throw new InvalidOperationException($"Budget line targets unknown group {line.GroupId}.");
                targetName = group.Name;
                actualSigned = GroupActualSigned(company, group.Id, line.Type, budget.PeriodFrom, budget.PeriodTo);
            }
            else
            {
                var ledger = company.FindLedger(line.LedgerId!.Value)
                    ?? throw new InvalidOperationException($"Budget line targets unknown ledger {line.LedgerId}.");
                targetName = ledger.Name;
                actualSigned = LedgerActualSigned(company, ledger, line.Type, budget.PeriodFrom, budget.PeriodTo);
            }

            var actualMagnitude = Math.Abs(actualSigned);
            var budgetAmount = line.Amount.Amount;
            var variance = actualMagnitude - budgetAmount;
            decimal? variancePercent = budgetAmount == 0m ? null : variance / budgetAmount * 100m;

            rows.Add(new BudgetVarianceLine(
                targetName,
                line.IsGroupTarget,
                line.Type,
                line.Amount,
                new Money(actualMagnitude),
                new Money(variance),
                variancePercent));
        }

        return new BudgetVarianceReport(budget.Id, budget.Name, budget.PeriodFrom, budget.PeriodTo, rows);
    }

    /// <summary>The signed actual for a single ledger under the requested measure.</summary>
    private static decimal LedgerActualSigned(
        Company company, Domain.Ledger ledger, BudgetType type, DateOnly from, DateOnly to) =>
        type == BudgetType.OnClosingBalance
            ? LedgerBalances.SignedClosing(company, ledger, to)
            : LedgerBalances.SignedMovement(company, ledger, from, to);

    /// <summary>The signed actual for a group: Σ over every ledger under it (any depth).</summary>
    private static decimal GroupActualSigned(
        Company company, Guid groupId, BudgetType type, DateOnly from, DateOnly to)
    {
        var total = 0m;
        foreach (var ledger in company.Ledgers)
            if (ClassificationRules.LedgerIsUnderGroup(ledger, groupId, company))
                total += LedgerActualSigned(company, ledger, type, from, to);
        return total;
    }
}
