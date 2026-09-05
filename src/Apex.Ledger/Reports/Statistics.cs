using Apex.Ledger.Domain;

namespace Apex.Ledger.Reports;

/// <summary>
/// One "Types of Vouchers" row: a voucher type and how many vouchers of it were <b>entered</b> inside the
/// window. <see cref="CancelledCount"/> is the subset of <see cref="Count"/> that has since been cancelled,
/// broken out so a cancelled entry is neither hidden nor silently counted as live.
/// </summary>
public sealed record StatisticsVoucherTypeRow(Guid VoucherTypeId, string Name, int Count, int CancelledCount);

/// <summary>One "Types of Accounts" row: a master kind and how many of it exist.</summary>
public sealed record StatisticsMasterRow(string Name, int Count);

/// <summary>
/// <b>Statistics</b> (census row 11.8) — the vendor describes it as <i>"a snapshot of all the masters
/// created and the number of voucher types entered"</i>, laid out as two sections, <b>Types of
/// Vouchers</b> and <b>Types of Accounts</b>.
///
/// <para><b>Types of Vouchers</b> lists <i>every</i> voucher type the company has, entries or not, so a
/// type that has never been used is visible as an unused type rather than absent. <b>Types of Accounts</b>
/// lists the master kinds with their counts.</para>
///
/// <para>⚠️ <b>DIVERGENCES, LABELLED AS OURS (R7 / RULING 9).</b> Three, stated plainly:</para>
/// <list type="number">
///   <item><b>Cancelled vouchers are counted as entered.</b> Statistics is a report about the DATA, not
///     about the books — a cancelled voucher was entered, keeps its number, and occupies a slot. No source
///     was reachable on the point, so the choice is ours, and <see cref="StatisticsVoucherTypeRow.CancelledCount"/>
///     exists precisely so the reader can subtract it. Every other report in this codebase drops cancelled
///     vouchers, so this is a deliberate local exception, not an oversight.</item>
///   <item><b>The master-kind list</b> below (which kinds, in which order) is ours.</item>
///   <item>The vendor page names <b>"all 22 default voucher types"</b>; our seed has <b>24</b>. That is a
///     different product generation's default set, and the two numbers are a real upstream scope question
///     (which product's voucher-type set the clone targets) that belongs to census area 4, not here. This
///     report counts whatever the company actually has and asserts nothing about what it should be.</item>
/// </list>
/// </summary>
public sealed record Statistics(
    DateOnly From,
    DateOnly To,
    IReadOnlyList<StatisticsVoucherTypeRow> VoucherTypes,
    int TotalVouchers,
    int TotalCancelledVouchers,
    IReadOnlyList<StatisticsMasterRow> Masters)
{
    /// <summary>Builds the Statistics snapshot for the window <c>[from, to]</c>.</summary>
    public static Statistics Build(Company company, DateOnly from, DateOnly to)
    {
        var counts = new Dictionary<Guid, (int Count, int Cancelled)>();
        foreach (var v in company.Vouchers)
        {
            if (v.Date < from || v.Date > to) continue;
            counts.TryGetValue(v.TypeId, out var current);
            counts[v.TypeId] = (current.Count + 1, current.Cancelled + (v.Cancelled ? 1 : 0));
        }

        var voucherTypes = new List<StatisticsVoucherTypeRow>();
        var total = 0;
        var totalCancelled = 0;
        foreach (var type in company.VoucherTypes)
        {
            counts.TryGetValue(type.Id, out var c);
            voucherTypes.Add(new StatisticsVoucherTypeRow(type.Id, type.Name, c.Count, c.Cancelled));
            total += c.Count;
            totalCancelled += c.Cancelled;
        }

        var masters = new List<StatisticsMasterRow>
        {
            new("Groups", company.Groups.Count),
            new("Ledgers", company.Ledgers.Count),
            new("Voucher Types", company.VoucherTypes.Count),
            new("Cost Categories", company.CostCategories.Count),
            new("Cost Centres", company.CostCentres.Count),
            new("Currencies", company.Currencies.Count),
            new("Budgets", company.Budgets.Count),
            new("Scenarios", company.Scenarios.Count),
            new("Stock Groups", company.StockGroups.Count),
            new("Stock Categories", company.StockCategories.Count),
            new("Stock Items", company.StockItems.Count),
            new("Units", company.Units.Count),
            new("Godowns", company.Godowns.Count),
            new("Price Lists", company.PriceLists.Count),
            new("Employee Groups", company.EmployeeGroups.Count),
            new("Employees", company.Employees.Count),
            new("Pay Heads", company.PayHeads.Count),
        };

        return new Statistics(from, to, voucherTypes, total, totalCancelled, masters);
    }
}
