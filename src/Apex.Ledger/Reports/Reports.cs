using Apex.Ledger.Domain;

namespace Apex.Ledger.Reports;

/// <summary>
/// The pure report façade (design §8.4). None of these mutates the company; each returns
/// an immutable record computed over (masters, posted vouchers, as-of date). The methods
/// are named with a <c>Build</c> prefix to avoid colliding with the report record types
/// that share this namespace; callers may equally call each record's own <c>Build</c>.
/// </summary>
public static class Report
{
    public static TrialBalance BuildTrialBalance(Company c, DateOnly asOf)
        => TrialBalance.Build(c, asOf);

    public static ProfitAndLoss BuildProfitAndLoss(
        Company c, DateOnly to, ClosingStockMode mode = ClosingStockMode.AsPostedLedger)
        => ProfitAndLoss.Build(c, to, mode);

    public static BalanceSheet BuildBalanceSheet(Company c, DateOnly asOf)
        => BalanceSheet.Build(c, asOf);

    public static IReadOnlyList<DayBookRow> BuildDayBook(Company c, DateOnly from, DateOnly to)
        => DayBook.Build(c, from, to);

    public static LedgerBook BuildLedgerBook(Company c, Guid ledgerId, DateOnly from, DateOnly to)
        => LedgerBook.Build(c, ledgerId, from, to);
}
