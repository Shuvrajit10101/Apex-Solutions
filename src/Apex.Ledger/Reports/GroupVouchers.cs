using Apex.Ledger.Domain;

namespace Apex.Ledger.Reports;

/// <summary>
/// One row of <see cref="GroupVouchers"/>: a voucher carrying at least one ledger line under the chosen
/// group. <see cref="Debit"/> / <see cref="Credit"/> are that voucher's movement <b>on the group's own
/// ledgers only</b>, not its whole total, so the column foots to the group's period movement.
/// <see cref="VoucherId"/> is the RQ-7 drill key.
/// </summary>
public sealed record GroupVoucherRow(
    Guid VoucherId,
    DateOnly Date,
    string VoucherTypeName,
    int Number,
    string FormattedNumber,
    string? Particulars,
    Money Debit,
    Money Credit);

/// <summary>
/// <b>Group Vouchers</b> (census row 11.7) — every voucher containing at least one ledger from the selected
/// group, at any depth beneath it. Chronological, then by voucher number.
///
/// <para>The vendor sentence confirmed by fetch in the wave-3 pass is <i>"Group Vouchers report lists all
/// vouchers containing at least one ledger from the selected group"</i>, and that is exactly the selection
/// rule implemented here (transitively, via
/// <see cref="ClassificationRules.LedgerIsUnderGroup"/>).</para>
///
/// <para>⚠️ <b>DIVERGENCE, LABELLED AS OURS (R7 / RULING 9).</b> The selection rule is sourced; the COLUMN
/// SET is not. Showing the group's own Dr/Cr movement rather than the whole voucher total is our choice —
/// taken because a column that foots to the group's own movement is the one a reader can reconcile against
/// the Group Summary above it. Recorded as ours, not asserted as fidelity.</para>
/// </summary>
public sealed record GroupVouchers(
    Guid GroupId,
    string GroupName,
    DateOnly From,
    DateOnly To,
    IReadOnlyList<GroupVoucherRow> Rows,
    Money TotalDebit,
    Money TotalCredit)
{
    /// <summary>Builds the Group Vouchers listing for <paramref name="groupId"/> over <c>[from, to]</c>.</summary>
    public static GroupVouchers Build(Company company, Guid groupId, DateOnly from, DateOnly to)
    {
        var group = company.FindGroup(groupId)
            ?? throw new InvalidOperationException($"Group {groupId} not found.");

        // Resolve the group's ledger set once — the alternative walks the parent chain per line per voucher.
        var inGroup = company.Ledgers
            .Where(l => ClassificationRules.LedgerIsUnderGroup(l, groupId, company))
            .Select(l => l.Id)
            .ToHashSet();

        var collected = new List<(Voucher Voucher, GroupVoucherRow Row)>();
        var totalDebit = 0m;
        var totalCredit = 0m;

        foreach (var v in company.Vouchers)
        {
            if (v.Date < from || v.Date > to) continue;
            var type = company.FindVoucherType(v.TypeId);
            if (!LedgerBalances.CountsAsOf(v, to, type?.BaseType)) continue;

            var debit = 0m;
            var credit = 0m;
            var touched = false;

            foreach (var line in v.Lines)
            {
                if (!inGroup.Contains(line.LedgerId)) continue;
                touched = true;
                if (line.Side == DrCr.Debit) debit += line.Amount.Amount;
                else credit += line.Amount.Amount;
            }

            if (!touched) continue;

            string? particulars = v.PartyId is Guid pid ? company.FindLedger(pid)?.Name : null;
            particulars ??= GroupSideNames(company, v, inGroup);
            particulars ??= v.Narration;

            collected.Add((v, new GroupVoucherRow(
                v.Id, v.Date, type?.Name ?? "(unknown)", v.Number, company.FormatVoucherNumber(v),
                particulars, new Money(debit), new Money(credit))));

            totalDebit += debit;
            totalCredit += credit;
        }

        collected.Sort((a, b) =>
        {
            var byDate = a.Voucher.Date.CompareTo(b.Voucher.Date);
            return byDate != 0 ? byDate : a.Voucher.Number.CompareTo(b.Voucher.Number);
        });

        return new GroupVouchers(groupId, group.Name, from, to,
            collected.Select(c => c.Row).ToList(), new Money(totalDebit), new Money(totalCredit));
    }

    /// <summary>The group's own ledger name(s) in this voucher — what the row is actually about.</summary>
    private static string? GroupSideNames(Company company, Voucher v, HashSet<Guid> inGroup)
    {
        var names = v.Lines
            .Where(l => inGroup.Contains(l.LedgerId))
            .Select(l => company.FindLedger(l.LedgerId)?.Name)
            .Where(n => n is not null)
            .Select(n => n!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return names.Count switch
        {
            0 => null,
            1 => names[0],
            _ => "(multiple)",
        };
    }
}
