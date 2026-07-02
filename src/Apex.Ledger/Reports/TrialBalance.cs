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

    /// <summary>Builds the Trial Balance as of a date (pure over masters + posted set).</summary>
    public static TrialBalance Build(Company company, DateOnly asOf)
    {
        var rows = new List<TrialBalanceRow>();
        var totalDr = 0m;
        var totalCr = 0m;

        foreach (var ledger in company.Ledgers)
        {
            var bal = LedgerBalances.Closing(company, ledger, asOf);
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
}
