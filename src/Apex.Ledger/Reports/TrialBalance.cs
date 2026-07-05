using Apex.Ledger.Domain;

namespace Apex.Ledger.Reports;

/// <summary>One Trial Balance row: a ledger's closing placed in exactly one column.</summary>
public sealed record TrialBalanceRow(string LedgerName, string GroupName, Money Debit, Money Credit);

/// <summary>
/// The Trial Balance (design §7.2): every ledger's closing balance placed in the Dr or Cr
/// column, with totals. <see cref="TotalDebit"/> == <see cref="TotalCredit"/> always (§6.10).
/// </summary>
public sealed record TrialBalance(IReadOnlyList<TrialBalanceRow> Rows, Money TotalDebit, Money TotalCredit)
{
    public bool Balanced => TotalDebit == TotalCredit;

    /// <summary>
    /// Builds the Trial Balance as of a date (pure over masters + posted set). When
    /// <paramref name="scenario"/> is non-<c>null</c> the balances are computed under that scenario
    /// (catalog §7): actuals (if the scenario includes them) plus its included provisional vouchers.
    /// A <c>null</c> scenario yields the plain actual Trial Balance (unchanged behaviour).
    /// </summary>
    public static TrialBalance Build(Company company, DateOnly asOf, Scenario? scenario = null)
    {
        var rows = new List<TrialBalanceRow>();
        var totalDr = 0m;
        var totalCr = 0m;

        foreach (var ledger in company.Ledgers)
        {
            var bal = LedgerBalances.Closing(company, ledger, asOf, scenario);
            if (bal.Amount == Money.Zero) continue; // zero-balance ledgers do not appear

            var group = company.FindGroup(ledger.GroupId);
            var groupName = group?.Name ?? "(unknown)";

            if (bal.Side == DrCr.Debit)
            {
                rows.Add(new TrialBalanceRow(ledger.Name, groupName, bal.Amount, Money.Zero));
                totalDr += bal.Amount.Amount;
            }
            else
            {
                rows.Add(new TrialBalanceRow(ledger.Name, groupName, Money.Zero, bal.Amount));
                totalCr += bal.Amount.Amount;
            }
        }

        return new TrialBalance(rows, new Money(totalDr), new Money(totalCr));
    }

    /// <summary>
    /// Builds the Trial Balance under <see cref="ReportOptions"/> (RQ-1). The Trial Balance is a
    /// <b>closing-balance</b> statement — each ledger's balance carried forward (opening + movements)
    /// AS AT the report date — exactly like its sibling Balance Sheet.
    /// <para>When <see cref="ReportOptions.Period"/> is <c>null</c> this is the plain as-of Trial Balance
    /// at <see cref="ReportOptions.AsOfDate"/> — byte-for-byte the legacy behaviour, so nothing regresses.</para>
    /// <para>When a period is set (Alt+F2) the Trial Balance is built as CLOSING balances AS AT the
    /// period-end <c>To</c> — opening balances are carried forward, NOT dropped. <c>From</c> selects the
    /// as-at date's upper bound only; it does not turn the statement into in-window movement. So an
    /// opening-only ledger (e.g. a Capital account with no vouchers) still appears in a period Trial
    /// Balance, matching Tally. The scenario is honoured.</para>
    /// </summary>
    public static TrialBalance Build(Company company, ReportOptions options)
        => options.Period is { } p
            ? Build(company, p.To, options.Scenario)
            : Build(company, options.AsOfDate, options.Scenario);
}
