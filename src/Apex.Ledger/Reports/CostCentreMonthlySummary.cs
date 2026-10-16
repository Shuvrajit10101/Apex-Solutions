using Apex.Ledger.Domain;

namespace Apex.Ledger.Reports;

/// <summary>
/// One month of a cost centre's monthly summary: the income and expense allocated to the centre in that
/// month, how many vouchers produced it, and the centre's cumulative closing balance at the end of the month.
///
/// <para><see cref="Debit"/> is the expense side and <see cref="Credit"/> the income side. A
/// <see cref="CostAllocation"/> is a positive MAGNITUDE that inherits its parent <see cref="EntryLine"/>'s
/// Dr/Cr side (see that type's own remarks), so the side is read off the LINE, never off the allocation.</para>
/// </summary>
public sealed record CostCentreMonthlySummaryRow(
    MonthWindow Month,
    Money Debit,
    Money Credit,
    int VoucherCount,
    DrCr ClosingSide,
    Money ClosingAmount);

/// <summary>
/// The <b>Cost Centre Monthly Summary</b> — a row per month of the window for ONE cost centre, the balance
/// carried into the window above it and the running cumulative closing carried down the column.
///
/// <para><b>Vendor grounding (R7 source 1), opened BY CONTENT on 2026-10-13:</b>
/// <c>help.tallysolutions.com/tally-prime/financial-controlling/cost-centre-or-profit-centre-tally/</c>,
/// section <i>Cost Centre Monthly Summary</i> — <i>"You can view the monthly breakup of income and expense
/// details along with the closing balance for the selected Cost Centre."</i> The same section attests how the
/// report is opened — <i>"Press Alt+G (Go To) &gt; type Cost Centre Monthly Summary . Alternatively, you can
/// drill down from the Cost Centre Summary report."</i> — and what a month row drills into: <i>"Select the
/// required month and press Enter , to view the Cost Centre Vouchers report."</i></para>
///
/// <para>🔴 <b>THE VOUCHER-WISE LEVEL IS NOT BUILT, AND THIS REPORT DELIBERATELY DOES NOT FAKE ONE.</b> There
/// is no <c>Cost Centre Vouchers</c> report in this product, so a month row carries no drill target rather than
/// drilling somewhere the vendor does not send it. Census row 11.10 therefore stays PARTIAL on that clause; it
/// is named here so the next reader does not have to rediscover which half shipped.</para>
///
/// <para><b>What counts.</b> A voucher contributes iff it counts under
/// <see cref="LedgerBalances.CountsAsOf(Voucher, DateOnly, VoucherBaseType?)"/> — the same Cancelled /
/// Optional / not-yet-due PostDated filter <see cref="CostReports"/> applies — so this report and the Cost
/// Centre Break-up can never disagree about which entries exist. A month with no allocations still gets a row,
/// carrying the previous month's closing unchanged, exactly as <see cref="LedgerMonthlySummary"/> does.</para>
///
/// <para>🔴 <b>This is a SINGLE-CENTRE report, so there is no parallel-axis overlap to warn about.</b> The
/// overlap flag the three <see cref="CostReports"/> projections carry exists because summing ACROSS categories
/// double-counts one line (spec §4.2 rule C-27). One centre belongs to exactly one category, so its own total
/// counts each allocated line at most once and the column foots. An overlap footnote here would warn about an
/// arithmetic that cannot occur.</para>
/// </summary>
public sealed record CostCentreMonthlySummary(
    Guid CentreId,
    string CentreName,
    DrCr OpeningSide,
    Money OpeningAmount,
    IReadOnlyList<CostCentreMonthlySummaryRow> Rows,
    DrCr ClosingSide,
    Money ClosingAmount)
{
    /// <summary>The centre's signed cumulative allocation (Dr positive) over every voucher counting as at
    /// <paramref name="asOf"/>. This is the opening carried into the window.</summary>
    private static decimal SignedThrough(Company company, Guid centreId, DateOnly asOf)
    {
        var signed = 0m;
        foreach (var v in company.Vouchers)
        {
            if (!LedgerBalances.CountsAsOf(v, asOf, company.FindVoucherType(v.TypeId)?.BaseType)) continue;
            foreach (var line in v.Lines)
                foreach (var a in line.CostAllocations)
                {
                    if (a.CentreId != centreId) continue;
                    signed += line.Side == DrCr.Debit ? a.Amount.Amount : -a.Amount.Amount;
                }
        }

        return signed;
    }

    /// <summary>Builds the cost centre's monthly summary over <c>[from, to]</c>.</summary>
    public static CostCentreMonthlySummary Build(Company company, Guid centreId, DateOnly from, DateOnly to)
    {
        ArgumentNullException.ThrowIfNull(company);

        // Defensive, like LedgerMonthlySummary.Build: a report opened before a centre has been picked, or
        // against a centre that has since been deleted, renders an empty summary rather than throwing.
        if (centreId == Guid.Empty || company.FindCostCentre(centreId) is not { } centre)
            return new CostCentreMonthlySummary(
                centreId, string.Empty, DrCr.Debit, Money.Zero, [], DrCr.Debit, Money.Zero);

        var running = SignedThrough(company, centreId, from.AddDays(-1));
        var opening = LedgerBalance.FromSigned(running);

        var rows = new List<CostCentreMonthlySummaryRow>();
        foreach (var month in MonthAxis.Months(from, to))
        {
            var debit = 0m;
            var credit = 0m;
            var count = 0;

            foreach (var v in company.Vouchers)
            {
                if (v.Date < month.From || v.Date > month.To) continue;
                if (!LedgerBalances.CountsAsOf(v, month.To, company.FindVoucherType(v.TypeId)?.BaseType)) continue;

                var touched = false;
                foreach (var line in v.Lines)
                    foreach (var a in line.CostAllocations)
                    {
                        if (a.CentreId != centreId) continue;
                        touched = true;
                        if (line.Side == DrCr.Debit) debit += a.Amount.Amount;
                        else credit += a.Amount.Amount;
                    }

                if (touched) count++;
            }

            running += debit - credit;
            var closing = LedgerBalance.FromSigned(running);
            rows.Add(new CostCentreMonthlySummaryRow(
                month, new Money(debit), new Money(credit), count, closing.Side, closing.Amount));
        }

        var final = LedgerBalance.FromSigned(running);
        return new CostCentreMonthlySummary(
            centreId, centre.Name, opening.Side, opening.Amount, rows, final.Side, final.Amount);
    }
}
