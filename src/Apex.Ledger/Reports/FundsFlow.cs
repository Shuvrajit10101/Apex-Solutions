using Apex.Ledger.Domain;

namespace Apex.Ledger.Reports;

/// <summary>A Funds-Flow line: a balance-sheet ledger-group and the funds it provided (source) or used (application).</summary>
public sealed record FundsFlowLine(string Name, string GroupName, Money Amount);

/// <summary>
/// The <b>Funds Flow</b> statement (catalog §16): the sources and applications of funds over a window
/// <c>[From, To]</c>. Movements are taken over the balance-sheet ledgers only; the period's net result
/// (Funds From Operations) is folded in as a source (profit) or application (loss).
/// <para><b>Why it balances.</b> Over any window Σ of every ledger's signed movement is zero (each voucher
/// balances). Hence Σ signed movement of the balance-sheet ledgers equals the net profit
/// (income − expense). A balance-sheet ledger that moved <b>credit-ward</b> is a <see cref="Sources"/>
/// (liability/capital up, or asset down — funds came in); one that moved <b>debit-ward</b> is an
/// <see cref="Applications"/> (asset up, or liability down — funds went out). Adding the net result to the
/// appropriate side makes <c>TotalSources == TotalApplications</c> by construction (<see cref="Balanced"/>).</para>
/// </summary>
public sealed record FundsFlow(
    IReadOnlyList<FundsFlowLine> Sources,
    Money TotalSources,
    IReadOnlyList<FundsFlowLine> Applications,
    Money TotalApplications,
    Money FundsFromOperations)
{
    /// <summary>True when total sources == total applications, to the paisa (§6 double-entry guarantee).</summary>
    public bool Balanced => TotalSources == TotalApplications;

    /// <summary>Builds the Funds-Flow statement over <paramref name="period"/>.</summary>
    public static FundsFlow Build(Company company, PeriodRange period)
    {
        var from = period.From;
        var to = period.To;

        var sources = new List<FundsFlowLine>();
        var applications = new List<FundsFlowLine>();
        var totalSources = 0m;
        var totalApplications = 0m;

        foreach (var ledger in company.Ledgers)
        {
            var group = company.FindGroup(ledger.GroupId)
                ?? throw new InvalidOperationException($"Ledger '{ledger.Name}' has unknown group {ledger.GroupId}.");

            // Only balance-sheet ledgers contribute individual source/application lines; the net P&L result is
            // folded in below as Funds From Operations so the statement covers all activity and still balances.
            if (ClassificationRules.IsProfitAndLossGroup(group, company)) continue;

            var movement = LedgerBalances.SignedMovement(company, ledger, from, to);
            if (movement == 0m) continue;

            if (movement < 0m)
            {
                // Credit-ward: a source of funds (liability/capital increase, or asset decrease).
                var amount = -movement;
                sources.Add(new FundsFlowLine(ledger.Name, group.Name, new Money(amount)));
                totalSources += amount;
            }
            else
            {
                // Debit-ward: an application of funds (asset increase, or liability/capital decrease).
                applications.Add(new FundsFlowLine(ledger.Name, group.Name, new Money(movement)));
                totalApplications += movement;
            }
        }

        // Funds From Operations = the net of all balance-sheet movements = −Σ(P&L-ledger movements) by double
        // entry (every voucher balances). It is the residual that makes the statement balance exactly, and it is
        // the funds the trading/P&L activity generated (source, when > 0) or consumed (application, when < 0).
        // Deriving it from the balance-sheet residual (rather than the P&L net profit) keeps it exact even where
        // the P&L applies periodic-inventory opening/closing-stock adjustments that are not simple movements.
        var fundsFromOps = totalApplications - totalSources;

        if (fundsFromOps > 0m)
        {
            sources.Insert(0, new FundsFlowLine("Funds From Operations", "Profit & Loss A/c", new Money(fundsFromOps)));
            totalSources += fundsFromOps;
        }
        else if (fundsFromOps < 0m)
        {
            applications.Insert(0, new FundsFlowLine("Funds Lost In Operations", "Profit & Loss A/c", new Money(-fundsFromOps)));
            totalApplications += -fundsFromOps;
        }

        return new FundsFlow(
            sources, new Money(totalSources),
            applications, new Money(totalApplications),
            new Money(fundsFromOps));
    }
}
