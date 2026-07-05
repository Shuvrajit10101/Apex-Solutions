using Apex.Ledger.Domain;

namespace Apex.Ledger.Reports;

/// <summary>A Cash-Flow section line: a counterparty ledger-group and the cash it released (inflow) or absorbed (outflow).</summary>
public sealed record CashFlowLine(string Name, string GroupName, Money Amount);

/// <summary>
/// The <b>Cash Flow</b> statement (catalog §16). Over a window <c>[From, To]</c> it reconciles the
/// opening cash&amp;bank balance to the closing cash&amp;bank balance by classifying the movement of every
/// <i>counterparty</i> (non-cash-and-bank) ledger against the cash &amp; bank pool.
/// <para>By double entry, the net movement of the cash&amp;bank pool over the window equals the negative of
/// the net movement of all other ledgers. A counterparty ledger that moved <b>credit-ward</b> (e.g. a sale,
/// a fresh loan, a creditor increase) <b>released</b> cash — an <see cref="Inflows"/>. One that moved
/// <b>debit-ward</b> (e.g. an asset purchase, a debtor increase, an expense) <b>absorbed</b> cash — an
/// <see cref="Outflows"/>. The lines are grouped by the counterparty's ledger-group. By construction
/// <c>Opening + (Inflows − Outflows) == Closing</c> to the paisa (<see cref="Reconciles"/>).</para>
/// </summary>
public sealed record CashFlow(
    Money OpeningBalance,
    IReadOnlyList<CashFlowLine> Inflows,
    Money TotalInflows,
    IReadOnlyList<CashFlowLine> Outflows,
    Money TotalOutflows,
    Money NetCashFlow,
    Money ClosingBalance)
{
    /// <summary>True when opening + net cash flow reconciles to closing, to the paisa (§6 double-entry guarantee).</summary>
    public bool Reconciles => OpeningBalance + NetCashFlow == ClosingBalance;

    /// <summary>
    /// Builds the Cash-Flow statement over <paramref name="period"/>. Opening cash&amp;bank is valued as-at the
    /// day <b>before</b> <see cref="PeriodRange.From"/>; closing as-at <see cref="PeriodRange.To"/>. Inflows and
    /// outflows are the in-window movements of counterparty ledgers, grouped by ledger-group and signed so the
    /// statement reconciles.
    /// </summary>
    public static CashFlow Build(Company company, PeriodRange period)
    {
        var from = period.From;
        var to = period.To;
        var openingAsOf = from.AddDays(-1);

        // Opening / closing cash & bank pool = Σ signed closing over cash-in-hand + bank ledgers.
        var opening = 0m;
        var closing = 0m;
        foreach (var ledger in company.Ledgers)
        {
            if (!ClassificationRules.IsCashOrBankLedger(ledger, company)) continue;
            opening += LedgerBalances.SignedClosing(company, ledger, openingAsOf);
            closing += LedgerBalances.SignedClosing(company, ledger, to);
        }

        // Counterparty movements over the window: a credit-ward move released cash (inflow), a debit-ward move
        // absorbed cash (outflow). Signed cash release for a ledger = −(its signed movement).
        var inflows = new List<CashFlowLine>();
        var outflows = new List<CashFlowLine>();
        var totalIn = 0m;
        var totalOut = 0m;

        foreach (var ledger in company.Ledgers)
        {
            if (ClassificationRules.IsCashOrBankLedger(ledger, company)) continue;

            var movement = LedgerBalances.SignedMovement(company, ledger, from, to);
            if (movement == 0m) continue;

            var group = company.FindGroup(ledger.GroupId)
                ?? throw new InvalidOperationException($"Ledger '{ledger.Name}' has unknown group {ledger.GroupId}.");

            var cashReleased = -movement; // credit-ward (negative movement) ⇒ positive inflow
            if (cashReleased > 0m)
            {
                inflows.Add(new CashFlowLine(ledger.Name, group.Name, new Money(cashReleased)));
                totalIn += cashReleased;
            }
            else
            {
                outflows.Add(new CashFlowLine(ledger.Name, group.Name, new Money(-cashReleased)));
                totalOut += -cashReleased;
            }
        }

        var net = totalIn - totalOut;
        return new CashFlow(
            new Money(opening),
            inflows, new Money(totalIn),
            outflows, new Money(totalOut),
            new Money(net),
            new Money(closing));
    }
}
