using Apex.Ledger.Domain;

namespace Apex.Ledger.Reports;

/// <summary>
/// One row of a <see cref="GroupSummary"/>: either an immediate sub-group (whose figures are its whole
/// sub-tree, rolled up) or a ledger attached directly to the summarised group.
/// <see cref="GroupId"/> is set on a sub-group row and <see cref="LedgerId"/> on a ledger row — the two
/// drill keys, so Enter opens the sub-group's own summary or the ledger's monthly summary.
/// </summary>
public sealed record GroupSummaryRow(
    string Name,
    bool IsGroup,
    Guid GroupId,
    Guid LedgerId,
    DrCr OpeningSide,
    Money OpeningAmount,
    Money Debit,
    Money Credit,
    DrCr ClosingSide,
    Money ClosingAmount);

/// <summary>
/// <b>Group Summary</b> (census row 11.7) — the closing balance of the accounts under a chosen group for
/// the reporting period, one row per immediate sub-group (rolled up over its whole sub-tree) and one per
/// directly-attached ledger. Sub-groups come first, then ledgers; each block is name-sorted.
///
/// <para>Its drill path is <b>group → sub-group → ledger → <see cref="LedgerMonthlySummary"/> →
/// <see cref="LedgerBook"/></b>. Every row carries the key its level needs, so no figure on this report is
/// a dead end (the catalogue's drill-down-everywhere rule).</para>
///
/// <para><b>What counts.</b> Figures are the same ones the Trial Balance shows: opening is the balance
/// carried into the window, <see cref="GroupSummaryRow.Debit"/> / <see cref="GroupSummaryRow.Credit"/> are
/// the in-window movement, and closing is
/// <see cref="LedgerBalances.Closing(Company, Domain.Ledger, DateOnly)"/> at the window end. Cancelled,
/// optional and not-yet-due post-dated vouchers never contribute.</para>
///
/// <para>⚠️ <b>DIVERGENCE, LABELLED AS OURS (R7 / RULING 9).</b> The vendor sentence the wave-3 pass
/// confirmed by fetch is only that the report <i>"displays the closing balance of the accounts in the
/// selected group for a specified period"</i>. The row ORDER, the opening/Dr/Cr/closing column set, and the
/// decision to show sub-groups and directly-attached ledgers in one list are <b>ours</b>. This projection
/// may not be recorded as corpus-verified.</para>
/// </summary>
public sealed record GroupSummary(
    Guid GroupId,
    string GroupName,
    DateOnly From,
    DateOnly To,
    IReadOnlyList<GroupSummaryRow> Rows,
    DrCr OpeningSide,
    Money OpeningAmount,
    Money TotalDebit,
    Money TotalCredit,
    DrCr ClosingSide,
    Money ClosingAmount)
{
    /// <summary>Builds the Group Summary for <paramref name="groupId"/> over <c>[from, to]</c>.</summary>
    public static GroupSummary Build(Company company, Guid groupId, DateOnly from, DateOnly to)
    {
        var group = company.FindGroup(groupId)
            ?? throw new InvalidOperationException($"Group {groupId} not found.");

        var rows = new List<GroupSummaryRow>();
        var openingTotal = 0m;
        var debitTotal = 0m;
        var creditTotal = 0m;
        var closingTotal = 0m;

        var children = company.Groups
            .Where(g => g.ParentId == groupId)
            .OrderBy(g => g.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var child in children)
        {
            var subtree = company.Ledgers.Where(l => ClassificationRules.LedgerIsUnderGroup(l, child.Id, company));
            var (opening, debit, credit, closing) = Roll(company, subtree, from, to);

            openingTotal += opening;
            debitTotal += debit;
            creditTotal += credit;
            closingTotal += closing;

            var open = LedgerBalance.FromSigned(opening);
            var close = LedgerBalance.FromSigned(closing);
            rows.Add(new GroupSummaryRow(child.Name, true, child.Id, Guid.Empty,
                open.Side, open.Amount, new Money(debit), new Money(credit), close.Side, close.Amount));
        }

        var direct = company.Ledgers
            .Where(l => l.GroupId == groupId)
            .OrderBy(l => l.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var ledger in direct)
        {
            var (opening, debit, credit, closing) = Roll(company, [ledger], from, to);

            openingTotal += opening;
            debitTotal += debit;
            creditTotal += credit;
            closingTotal += closing;

            var open = LedgerBalance.FromSigned(opening);
            var close = LedgerBalance.FromSigned(closing);
            rows.Add(new GroupSummaryRow(ledger.Name, false, Guid.Empty, ledger.Id,
                open.Side, open.Amount, new Money(debit), new Money(credit), close.Side, close.Amount));
        }

        var groupOpening = LedgerBalance.FromSigned(openingTotal);
        var groupClosing = LedgerBalance.FromSigned(closingTotal);
        return new GroupSummary(groupId, group.Name, from, to, rows,
            groupOpening.Side, groupOpening.Amount,
            new Money(debitTotal), new Money(creditTotal),
            groupClosing.Side, groupClosing.Amount);
    }

    /// <summary>Signed opening / Dr movement / Cr movement / signed closing over a set of ledgers.</summary>
    private static (decimal Opening, decimal Debit, decimal Credit, decimal Closing) Roll(
        Company company, IEnumerable<Domain.Ledger> ledgers, DateOnly from, DateOnly to)
    {
        var opening = 0m;
        var debit = 0m;
        var credit = 0m;
        var closing = 0m;

        foreach (var ledger in ledgers)
        {
            opening += LedgerBalances.SignedClosing(company, ledger, from.AddDays(-1));
            closing += LedgerBalances.SignedClosing(company, ledger, to);

            foreach (var v in company.Vouchers)
            {
                if (v.Date < from || v.Date > to) continue;
                if (!LedgerBalances.CountsAsOf(v, to, company.FindVoucherType(v.TypeId)?.BaseType)) continue;
                foreach (var line in v.Lines)
                {
                    if (line.LedgerId != ledger.Id) continue;
                    if (line.Side == DrCr.Debit) debit += line.Amount.Amount;
                    else credit += line.Amount.Amount;
                }
            }
        }

        return (opening, debit, credit, closing);
    }
}
