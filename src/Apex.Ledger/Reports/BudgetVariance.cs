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
/// <summary>
/// v67 (census 2.9): WHICH KIND of target a <see cref="BudgetVarianceLine"/> names. Added because the row
/// used to carry a single <c>IsGroup</c> boolean, and every renderer printed
/// <c>IsGroup ? "Group" : "Ledger"</c> — so the moment a third target existed, a <b>cost-centre</b> row
/// would have been labelled <b>"Ledger"</b> in the on-screen report, the printed report and the export,
/// beside a real budgeted figure. A wrong label on a real number is a wrong report.
/// </summary>
public enum BudgetTargetKind
{
    /// <summary>A group target, rolling up every ledger under it at any depth.</summary>
    Group = 0,

    /// <summary>A single ledger.</summary>
    Ledger = 1,

    /// <summary>v67 (census 2.9): a single cost centre — the vendor's "Set Cost Centres to Yes" budget.</summary>
    CostCentre = 2,
}

public sealed record BudgetVarianceLine(
    string TargetName,
    BudgetTargetKind TargetKind,
    BudgetType Type,
    Money Budget,
    Money Actual,
    Money Variance,
    decimal? VariancePercent)
{
    /// <summary>Kept so nothing that only asks "is this a group?" has to change. 🔴 It is NOT the inverse of
    /// "is this a ledger?" any more — use <see cref="TargetKind"/> to LABEL a row.</summary>
    public bool IsGroup => TargetKind == BudgetTargetKind.Group;

    /// <summary>The target kind as the reports print it: "Group", "Ledger" or "Cost Centre". One place, so
    /// the on-screen report, the printed report and the export cannot drift.</summary>
    public string TargetKindLabel => TargetKind switch
    {
        BudgetTargetKind.Group => "Group",
        BudgetTargetKind.CostCentre => "Cost Centre",
        _ => "Ledger",
    };
}

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
            BudgetTargetKind kind;

            if (line.IsGroupTarget)
            {
                var group = company.FindGroup(line.GroupId!.Value)
                    ?? throw new InvalidOperationException($"Budget line targets unknown group {line.GroupId}.");
                kind = BudgetTargetKind.Group;
                targetName = group.Name;
                actualSigned = GroupActualSigned(company, group.Id, line.Type, budget.PeriodFrom, budget.PeriodTo);
            }
            else if (line.IsCostCentreTarget)
            {
                // 🔴 v67 (census 2.9). WITHOUT THIS ARM A COST-CENTRE LINE FELL INTO THE LEDGER BRANCH BELOW
                // AND DEREFERENCED A NULL LedgerId — the whole Budget Variance report THREW for the budget,
                // not merely mis-rendered one row. Storage alone would have shipped that.
                var centre = company.FindCostCentre(line.CostCentreId!.Value)
                    ?? throw new InvalidOperationException(
                        $"Budget line targets unknown cost centre {line.CostCentreId}.");
                kind = BudgetTargetKind.CostCentre;
                targetName = centre.Name;
                actualSigned = CostCentreActualSigned(
                    company, centre.Id, line.Type, budget.PeriodFrom, budget.PeriodTo);
            }
            else
            {
                var ledger = company.FindLedger(line.LedgerId!.Value)
                    ?? throw new InvalidOperationException($"Budget line targets unknown ledger {line.LedgerId}.");
                kind = BudgetTargetKind.Ledger;
                targetName = ledger.Name;
                actualSigned = LedgerActualSigned(company, ledger, line.Type, budget.PeriodFrom, budget.PeriodTo);
            }

            var actualMagnitude = Math.Abs(actualSigned);
            var budgetAmount = line.Amount.Amount;
            var variance = actualMagnitude - budgetAmount;
            decimal? variancePercent = budgetAmount == 0m ? null : variance / budgetAmount * 100m;

            rows.Add(new BudgetVarianceLine(
                targetName,
                kind,
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

    /// <summary>
    /// v67 (census 2.9): the signed actual for a single <b>cost centre</b> — Σ over every posted cost
    /// allocation naming it, signed by the parent entry line's Dr/Cr side so the caller's
    /// <c>Math.Abs</c> yields the same magnitude convention the group and ledger arms produce.
    ///
    /// <para>🔴 <b>A COST CENTRE HAS NO OPENING BALANCE IN THIS MODEL</b> — a cost allocation exists only on a
    /// posted voucher line, and there is no opening-allocation master anywhere in the schema. So
    /// <see cref="BudgetType.OnClosingBalance"/> means "every counted allocation dated on or before
    /// <paramref name="to"/>" and <see cref="BudgetType.OnNettTransactions"/> means "those dated within
    /// [<paramref name="from"/>, <paramref name="to"/>]". <b>That reading is OURS, not a vendor clause</b>:
    /// help.tallysolutions.com/budgets-tally/ describes the Cost Centre Budget screen as carrying
    /// <i>Cost Centre, Expenses, Income and Closing Balance</i> — three figures where this product carries
    /// one measure per line — so the mapping from that shape onto this one is a documented divergence and is
    /// recorded on <c>BudgetLine.CostCentreId</c> rather than left implicit here.</para>
    ///
    /// <para>The voucher filter is the SHARED one, <see cref="LedgerBalances.CountsAsOf"/> — cancelled,
    /// optional and not-yet-due post-dated vouchers are excluded, exactly as every cost report and every
    /// ledger balance in this product excludes them. Using a different filter here would make the budget's
    /// "actual" disagree with the Cost Centre Summary for the same window.</para>
    /// </summary>
    private static decimal CostCentreActualSigned(
        Company company, Guid centreId, BudgetType type, DateOnly from, DateOnly to)
    {
        var total = 0m;
        foreach (var v in company.Vouchers)
        {
            if (!LedgerBalances.CountsAsOf(v, to)) continue;
            // OnClosingBalance has no lower bound (no opening balance exists to carry forward);
            // OnNettTransactions is bounded below by `from`.
            if (type == BudgetType.OnNettTransactions && v.Date < from) continue;

            foreach (var line in v.Lines)
                foreach (var a in line.CostAllocations)
                    if (a.CentreId == centreId)
                        total += line.Side == DrCr.Debit ? a.Amount.Amount : -a.Amount.Amount;
        }
        return total;
    }

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
