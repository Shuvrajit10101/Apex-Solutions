using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Reports;
using Apex.Ledger.Services;
using Xunit;

namespace Apex.Ledger.Tests;

/// <summary>
/// Phase-8 slice-9 <b>statutory-bonus register</b> contract (RQ-15) — the projection over the dated salary structure.
/// A ≤ ₹21,000 earner appears with the §12-capped base + annual bonus; a &gt; ₹21,000 earner is excluded; a mid-year
/// joiner is prorated; a &lt; 30-day worker shows as ineligible with ₹0. The golden: Basic + DA ₹18,000 → capped
/// ₹7,000 → 8.33% ⇒ ₹6,997/yr; at 20% ⇒ ₹16,800/yr.
/// </summary>
public sealed class BonusRegisterTests
{
    private static readonly DateOnly FyStart = new(2025, 4, 1);

    private static Company Build(int rateBp, out Guid basicHeadId)
    {
        var c = CompanyFactory.CreateSeeded("Bonus Co", new DateOnly(2020, 4, 1), new DateOnly(2020, 4, 1));
        var pay = new PayrollService(c);
        pay.EnablePayroll();
        pay.EnableStatutoryBonus(rateBasisPoints: rateBp);
        var ph = new PayHeadService(c);
        basicHeadId = ph.CreatePayHead("Basic", PayHeadType.Earnings, PayHeadCalculationType.FlatRate,
            underGroupId: c.FindGroupByName("Indirect Expenses")!.Id, useForGratuity: true).Id;
        return c;
    }

    private static Guid AddEmployee(Company c, Guid basic, string name, DateOnly doj, DateOnly structureFrom, decimal basicDa)
    {
        var pay = new PayrollService(c);
        var group = c.FindEmployeeGroupByName("Staff") ?? pay.CreateEmployeeGroup("Staff");
        var e = pay.CreateEmployee(name, group.Id);
        c.FindEmployee(e.Id)!.DateOfJoining = doj;
        new SalaryStructureService(c).DefineForEmployee(e.Id, structureFrom,
            new[] { new SalaryStructureLine(basic, 0, new Money(basicDa)) });
        return e.Id;
    }

    // ---------------------------------------------------------------- eligible row + over-ceiling exclusion

    [Fact]
    public void Golden_18000_eligible_yields_6997_and_25000_is_excluded()
    {
        var c = Build(833, out var basic);
        var eligible = AddEmployee(c, basic, "Eligible", new DateOnly(2020, 4, 1), new DateOnly(2020, 4, 1), 18_000m);
        var over = AddEmployee(c, basic, "Overpaid", new DateOnly(2020, 4, 1), new DateOnly(2020, 4, 1), 25_000m);

        var reg = BonusRegister.Build(c, new[] { eligible, over }, FyStart);

        // The > ₹21,000 earner is excluded from the register entirely.
        Assert.DoesNotContain(reg.Rows, r => r.EmployeeName == "Overpaid");
        var row = Assert.Single(reg.Rows);
        Assert.True(row.Eligible);
        Assert.Equal(18_000L, row.ActualBasicDa);
        Assert.Equal(7_000L, row.CappedBase);       // §12 cap
        Assert.Equal(8.33m, row.RatePercent);
        Assert.Equal(6_997L, row.AnnualBonus);       // 7,000 × 12 × 8.33% = 6,997
        Assert.Equal(6_997L, reg.TotalBonus);
        Assert.Equal(new DateOnly(2026, 3, 31), reg.FinancialYearEnd);
    }

    // ---------------------------------------------------------------- 20% rate

    [Fact]
    public void At_20_percent_the_18000_earner_gets_16800()
    {
        var c = Build(2000, out var basic);
        var e = AddEmployee(c, basic, "Eligible", new DateOnly(2020, 4, 1), new DateOnly(2020, 4, 1), 18_000m);
        var reg = BonusRegister.Build(c, new[] { e }, FyStart);
        var row = Assert.Single(reg.Rows);
        Assert.Equal(20m, row.RatePercent);
        Assert.Equal(16_800L, row.AnnualBonus);
    }

    // ---------------------------------------------------------------- mid-year joiner prorated

    [Fact]
    public void A_mid_year_joiner_is_prorated_by_months_worked()
    {
        var c = Build(833, out var basic);
        // Joins 2025-10-01 ⇒ 6 months (Oct..Mar) in the accounting year; base ₹7,000.
        var e = AddEmployee(c, basic, "Joiner", new DateOnly(2025, 10, 1), new DateOnly(2025, 10, 1), 7_000m);
        var reg = BonusRegister.Build(c, new[] { e }, FyStart);
        var row = Assert.Single(reg.Rows);
        Assert.True(row.Eligible);
        Assert.Equal(3_499L, row.AnnualBonus); // 7,000 × 6 × 8.33% = 3,498.6 → 3,499
    }

    // ---------------------------------------------------------------- < 30 days ⇒ ineligible, ₹0, still listed (≤ ₹21k)

    [Fact]
    public void A_worker_under_thirty_days_is_listed_ineligible_with_zero_bonus()
    {
        var c = Build(833, out var basic);
        // Joins 2026-03-25 ⇒ 7 days in the year (< 30); base ₹10,000 (≤ ₹21,000, so still a row).
        var e = AddEmployee(c, basic, "Latecomer", new DateOnly(2026, 3, 25), new DateOnly(2026, 3, 25), 10_000m);
        var reg = BonusRegister.Build(c, new[] { e }, FyStart);
        var row = Assert.Single(reg.Rows);
        Assert.False(row.Eligible);
        Assert.Equal(0L, row.AnnualBonus);
        Assert.Equal(0L, reg.TotalBonus);
    }

    // ---------------------------------------------------------------- ER-13: empty until enrolled

    [Fact]
    public void The_register_is_empty_until_the_establishment_enrols_for_bonus()
    {
        var c = CompanyFactory.CreateSeeded("No Bonus Co", new DateOnly(2020, 4, 1), new DateOnly(2020, 4, 1));
        var pay = new PayrollService(c);
        pay.EnablePayroll(); // NB: bonus NOT enrolled
        var ph = new PayHeadService(c);
        var basic = ph.CreatePayHead("Basic", PayHeadType.Earnings, PayHeadCalculationType.FlatRate,
            underGroupId: c.FindGroupByName("Indirect Expenses")!.Id, useForGratuity: true).Id;
        var e = AddEmployee(c, basic, "Eligible", new DateOnly(2020, 4, 1), new DateOnly(2020, 4, 1), 18_000m);

        var reg = BonusRegister.Build(c, new[] { e }, FyStart);
        Assert.Empty(reg.Rows);
        Assert.Equal(0L, reg.TotalBonus);
    }
}
