using Apex.Ledger.Domain;
using Apex.Ledger.Reports;
using Apex.Ledger.Services;
using Xunit;

namespace Apex.Ledger.Tests;

/// <summary>
/// The <b>PF and ESI statutory forms beyond the ECR</b> — census rows 7.20 (PF Forms 3A, 5, 6A, 10, 12A) and 7.21
/// (ESI Forms 3, 5, 6).
///
/// <para><b>What these tests are actually guarding.</b> Not "the projection returns rows" — that is the test that
/// passes on a broken build. Every assertion below pins one of the three things that can go silently wrong on a
/// statutory register:</para>
/// <list type="number">
///   <item><b>The wrong twelve months.</b> Forms 3A and 6A run over the PF <i>currency period</i>
///   (1 March … 28/29 February), which is <b>not</b> the April–March financial year. Built over the financial year
///   they would be off by two months at each end and nobody would see it, because both windows are twelve months
///   long and both foot to a plausible total.</item>
///   <item><b>The wrong population.</b> Forms 5 and 10 are the joiner and leaver returns; a filter that is
///   inclusive at the wrong end puts a member on the wrong month's return.</item>
///   <item><b>A figure that does not reconcile.</b> Form 12A's account heads and Form 6A page 2 must equal the
///   challan totals the ECR itself produces, and Form 6A page 1 must foot to the Form 3A cards. These are
///   cross-form identities: they catch an aggregation bug that a single-form test cannot see, because a single
///   form is self-consistent with its own mistake.</item>
/// </list>
///
/// <para>All figures come from the shipped <see cref="PfEcr"/> / <see cref="EsiMonthlyContribution"/> engines, so
/// nothing here asserts a hand-computed PF or ESI number — asserting one would only duplicate those engines' own
/// tests and would drift from them.</para>
/// </summary>
public sealed class PayrollStatutoryFormsTests
{
    private const string Uan = "100123456789";
    private const string Ip = "1234567890";

    // The fixture's financial year is the ordinary Indian one, 1 April 2025 – 31 March 2026. That matters: the PF
    // currency period containing it starts on 1 MARCH 2025, two months EARLIER, which is exactly the confusion the
    // currency-period tests below exist to catch.
    private static readonly DateOnly FyStart = new(2025, 4, 1);

    // ------------------------------------------------------------------------------------ PF currency period

    /// <summary>
    /// 🔴 The PF currency period is <b>1 March … 28/29 February</b>, not the financial year. A March-to-December
    /// date belongs to the period that opened in March of the SAME year; a January or February date belongs to the
    /// period that opened in March of the PREVIOUS one. Getting this wrong shifts every Form 3A and 6A by two
    /// months while still producing twelve rows that foot correctly.
    /// </summary>
    [Theory]
    [InlineData(2025, 3, 1, 2025)]    // the first day of a period
    [InlineData(2025, 4, 1, 2025)]    // the financial-year start sits INSIDE the period that opened in March
    [InlineData(2025, 12, 31, 2025)]
    [InlineData(2026, 1, 1, 2025)]    // January belongs to the PREVIOUS March
    [InlineData(2026, 2, 28, 2025)]   // and so does February, right up to the last day
    [InlineData(2026, 3, 1, 2026)]    // then it rolls
    public void The_pf_currency_period_runs_from_1_March_and_January_February_belong_to_the_previous_one(
        int year, int month, int day, int expectedStartYear)
    {
        var start = PayrollStatutoryPeriods.CurrencyPeriodStart(new DateOnly(year, month, day));

        Assert.Equal(new DateOnly(expectedStartYear, 3, 1), start);
        Assert.Equal(new DateOnly(expectedStartYear + 1, 2, 28 + (DateTime.IsLeapYear(expectedStartYear + 1) ? 1 : 0)),
            PayrollStatutoryPeriods.CurrencyPeriodEnd(start));
        Assert.Equal(12, PayrollStatutoryPeriods.CurrencyPeriodMonths(start).Count);
    }

    /// <summary>The end of the currency period is 29 February in a leap year — derived from the date arithmetic,
    /// never a hardcoded day count.</summary>
    [Fact]
    public void The_currency_period_ends_on_29_February_in_a_leap_year()
    {
        Assert.Equal(new DateOnly(2024, 2, 29),
            PayrollStatutoryPeriods.CurrencyPeriodEnd(new DateOnly(2023, 3, 1)));
        Assert.Equal(new DateOnly(2026, 2, 28),
            PayrollStatutoryPeriods.CurrencyPeriodEnd(new DateOnly(2025, 3, 1)));
    }

    /// <summary>The ESI contribution periods are April–September and October–March, six months each — and they are
    /// delegated to the shipped coverage engine rather than re-derived, so the register and the coverage decision
    /// can never disagree about where a period starts.</summary>
    [Theory]
    [InlineData(2025, 4, 1, 2025, 4, 1, 2025, 9, 30)]
    [InlineData(2025, 9, 30, 2025, 4, 1, 2025, 9, 30)]
    [InlineData(2025, 10, 1, 2025, 10, 1, 2026, 3, 31)]
    [InlineData(2026, 3, 31, 2025, 10, 1, 2026, 3, 31)]
    public void The_esi_contribution_periods_are_April_September_and_October_March(
        int y, int m, int d, int fy, int fm, int fd, int ty, int tm, int td)
    {
        var probe = new DateOnly(y, m, d);
        Assert.Equal(new DateOnly(fy, fm, fd), PayrollStatutoryPeriods.EsiContributionPeriodStart(probe));
        Assert.Equal(new DateOnly(ty, tm, td), PayrollStatutoryPeriods.EsiContributionPeriodEnd(probe));
        Assert.Equal(6, PayrollStatutoryPeriods.EsiContributionPeriodMonths(probe).Count);
    }

    /// <summary>A window that opens mid-month still yields WHOLE wage months — the statutory forms are
    /// month-columned and a part month is not one of their columns.</summary>
    [Fact]
    public void A_window_opening_mid_month_still_yields_whole_wage_months()
    {
        var months = PayrollStatutoryPeriods.Months(new DateOnly(2025, 4, 17), new DateOnly(2025, 6, 30));

        Assert.Equal(3, months.Count);
        Assert.Equal(new DateOnly(2025, 4, 1), months[0].From);
        Assert.Equal(new DateOnly(2025, 4, 30), months[0].To);
        Assert.Equal(new DateOnly(2025, 6, 30), months[^1].To);
        Assert.Empty(PayrollStatutoryPeriods.Months(new DateOnly(2025, 6, 1), new DateOnly(2025, 4, 1)));
    }

    // ------------------------------------------------------------------------------------------- PF Form 3A

    /// <summary>
    /// Form 3A is a <b>twelve-month card</b> over the currency period, and the twelve months it walks are
    /// March…February. A card built over the financial year would also have twelve rows — so the assertion that
    /// matters is the FIRST and LAST month, not the count.
    /// </summary>
    [Fact]
    public void Form_3A_walks_the_twelve_months_of_the_currency_period_starting_in_March()
    {
        var (c, _) = BuildPfCompany();

        var form = PfStatutoryForms.BuildForm3A(c, PayrollStatutoryPeriods.CurrencyPeriodStart(FyStart));

        Assert.Equal(new DateOnly(2025, 3, 1), form.CurrencyPeriodFrom);
        Assert.Equal(new DateOnly(2026, 2, 28), form.CurrencyPeriodTo);
        var card = Assert.Single(form.Members);
        Assert.Equal(12, card.Months.Count);
        Assert.Equal(new DateOnly(2025, 3, 1), card.Months[0].Month);
        Assert.Equal(new DateOnly(2026, 2, 1), card.Months[^1].Month);
    }

    /// <summary>A member with no PF wages in a month still gets that month's row, with zeroes. A missing row on a
    /// twelve-month card reads as a missing month, which is a different — and worse — statement.</summary>
    [Fact]
    public void Form_3A_prints_a_zero_row_for_a_month_with_no_wages_rather_than_omitting_it()
    {
        // The salary structure takes effect on 1 April, so March has no wages at all.
        var (c, _) = BuildPfCompany();

        var card = Assert.Single(PfStatutoryForms.BuildForm3A(c, new DateOnly(2025, 3, 1)).Members);

        var march = card.Months[0];
        Assert.Equal(new DateOnly(2025, 3, 1), march.Month);
        Assert.Equal(0, march.AmountOfWages);
        Assert.Equal(0, march.WorkersEpf);
        Assert.True(card.Months.Skip(1).Any(m => m.AmountOfWages > 0),
            "the fixture posted no PF wages at all — the zero-row assertion above would be vacuous");
    }

    /// <summary>
    /// 🔴 <b>The defect this test was written for.</b> The PF currency period opens on <b>1 March</b> and the Indian
    /// financial year opens on <b>1 April</b>, so a salary structure defined from the year start — which is what
    /// every company does, and what every payroll fixture in this repo does — leaves <b>March with no structure in
    /// force</b>. <see cref="PfEcr.Build"/> <i>throws</i> on that. Before the fix, Form 3A, Form 6A and Form 12A
    /// therefore threw <see cref="InvalidOperationException"/> on the ordinary case rather than an exotic one, and
    /// the whole of census row 7.20 was unreachable for any company at all.
    ///
    /// <para>The month must project as a zero row, because that is the truth: the member was not on a structure
    /// then. Note the assertion is on the FORM building at all — the zero-row content is checked separately.</para>
    /// </summary>
    [Fact]
    public void A_month_before_the_salary_structure_takes_effect_is_a_zero_row_and_never_an_exception()
    {
        var (c, empId) = BuildPfCompany();   // structure effective 1 April 2025; the period opens 1 March 2025
        var march = new DateOnly(2025, 3, 1);

        // All three ECR-backed forms must build. Each of these threw before the month-walk filtered the member list.
        var card = Assert.Single(PfStatutoryForms.BuildForm3A(c, march).Members);
        Assert.Equal(0, card.Months[0].AmountOfWages);
        Assert.Single(PfStatutoryForms.BuildForm6A(c, march).Members);
        Assert.Equal(0, PfStatutoryForms.BuildForm6A(c, march).Remittances[0].EpfContributionsAccount1);
        Assert.Equal(0, PfStatutoryForms.BuildForm12A(c, march).WagesOnWhichContributionsArePayable);

        // ...and April, which IS on the structure, is not nil — so the zeroes above are March's, not a dead engine.
        Assert.True(PfStatutoryForms.BuildForm12A(c, new DateOnly(2025, 4, 1))
            .WagesOnWhichContributionsArePayable > 0);
    }

    /// <summary>
    /// 🔴 <b>The other half of the same rule, and the reason the fix is a FILTER and not a swallowed exception.</b>
    /// A member who is on a structure but has no valid 12-digit UAN is a real data fault the operator must fix —
    /// the ECR keys the member line on the UAN — so it must still surface. A blanket try/catch around the month
    /// walk would have hidden it and produced a silently short return, which on a statutory form is the worst
    /// available outcome.
    /// </summary>
    [Fact]
    public void A_member_on_a_structure_with_no_valid_uan_still_makes_the_form_refuse_rather_than_going_missing()
    {
        var (c, empId) = BuildPfCompany();
        c.FindEmployee(empId)!.Uan = "12345";   // not a 12-digit UAN

        var ex = Assert.Throws<InvalidOperationException>(
            () => PfStatutoryForms.BuildForm3A(c, new DateOnly(2025, 3, 1)));
        Assert.Contains("UAN", ex.Message, StringComparison.Ordinal);

        Assert.Throws<InvalidOperationException>(() => PfStatutoryForms.BuildForm12A(c, new DateOnly(2025, 4, 1)));
    }

    /// <summary>The columns the form is named after are the ones the ECR produces: the worker's own 12% share and
    /// the employer's EPF <i>difference</i> (12% − 8.33%), which is a DIFFERENT figure from the pension
    /// contribution. Swapping the two is the classic Form 3A defect and both columns still foot.</summary>
    [Fact]
    public void Form_3A_reads_the_workers_share_employer_difference_and_pension_straight_off_the_ecr()
    {
        var (c, empId) = BuildPfCompany();
        var april = new DateOnly(2025, 4, 1);
        var ecr = PfEcr.Build(c, new[] { empId }, april, april.AddMonths(1).AddDays(-1));
        var member = Assert.Single(ecr.Members);

        var card = Assert.Single(PfStatutoryForms.BuildForm3A(c, new DateOnly(2025, 3, 1)).Members);
        var row = card.Months.Single(m => m.Month == april);

        Assert.Equal(member.EpfWages, row.AmountOfWages);
        Assert.Equal(member.EmployeeShareEpf, row.WorkersEpf);
        Assert.Equal(member.EmployerShareEpf, row.EmployerEpfDifference);
        Assert.Equal(member.EpsContribution, row.PensionFundContribution);
        // ...and the two employer columns are genuinely different figures, so the assertions above are not
        // simultaneously satisfiable by a projection that filled both from one source.
        Assert.NotEqual(row.EmployerEpfDifference, row.PensionFundContribution);
    }

    /// <summary>The card's footing totals equal the sum of its own twelve rows.</summary>
    [Fact]
    public void Form_3A_totals_equal_the_sum_of_the_twelve_month_rows()
    {
        var card = Assert.Single(
            PfStatutoryForms.BuildForm3A(BuildPfCompany().Company, new DateOnly(2025, 3, 1)).Members);

        Assert.Equal(card.Months.Sum(m => m.AmountOfWages), card.TotalAmountOfWages);
        Assert.Equal(card.Months.Sum(m => m.WorkersEpf), card.TotalWorkersEpf);
        Assert.Equal(card.Months.Sum(m => m.EmployerEpfDifference), card.TotalEmployerEpfDifference);
        Assert.Equal(card.Months.Sum(m => m.PensionFundContribution), card.TotalPensionFundContribution);
        Assert.True(card.TotalWorkersEpf > 0, "a zero total would satisfy every equality above vacuously");
    }

    /// <summary>
    /// Father's / Husband's Name is <b>printed, ruled and blank</b> — never dropped, never invented. The Employee
    /// master does not maintain it, and a form that silently omits one of its own columns misrepresents itself.
    /// </summary>
    [Fact]
    public void The_unmaintained_identity_columns_are_carried_as_blanks_and_never_filled_from_another_field()
    {
        var (c, _) = BuildPfCompany();
        var card = Assert.Single(PfStatutoryForms.BuildForm3A(c, new DateOnly(2025, 3, 1)).Members);

        Assert.Equal(string.Empty, card.Member.FathersOrHusbandsName);
        // Specifically: it did not borrow the member's own name to look populated.
        Assert.NotEqual(card.Member.Name, card.Member.FathersOrHusbandsName);
        Assert.Equal("Column not maintained in this book — complete by hand.", PfStatutoryForms.NotMaintainedNote);
    }

    /// <summary>The statutory rate is read from the establishment's configured EPF rate, never printed as the
    /// literal "12%".</summary>
    [Fact]
    public void The_statutory_rate_of_contribution_comes_from_the_configured_epf_rate()
    {
        var (c, _) = BuildPfCompany();
        Assert.Equal("12%", PfStatutoryForms.StatutoryRate(c));

        c.PfConfig!.EpfRateBasisPoints = 1000;
        Assert.Equal("10%", PfStatutoryForms.StatutoryRate(c));
    }

    // ------------------------------------------------------------------------------------------- PF Form 6A

    /// <summary>
    /// 🔴 <b>The cross-form identity.</b> Form 6A page 1 is the consolidated annual statement and Form 3A is the
    /// per-member card for the same currency period; a member's 6A row must equal the footing of that member's 3A
    /// card, column by column. This is the assertion most likely to catch a real aggregation bug, because each form
    /// is individually self-consistent with its own mistake.
    /// </summary>
    [Fact]
    public void Form_6A_page_one_foots_to_the_members_own_Form_3A_card()
    {
        var (c, _) = BuildPfCompany();
        var start = new DateOnly(2025, 3, 1);

        var card = Assert.Single(PfStatutoryForms.BuildForm3A(c, start).Members);
        var row = Assert.Single(PfStatutoryForms.BuildForm6A(c, start).Members);

        Assert.Equal(card.Member.EmployeeId, row.Member.EmployeeId);
        Assert.Equal(card.TotalAmountOfWages, row.Wages);
        Assert.Equal(card.TotalWorkersEpf, row.WorkersContribution);
        Assert.Equal(card.TotalEmployerEpfDifference, row.EmployerEpfDifference);
        Assert.Equal(card.TotalPensionFundContribution, row.PensionFundContribution);
        Assert.True(row.Wages > 0, "an all-zero fixture would satisfy every equality above vacuously");
    }

    /// <summary>
    /// 🔴 <b>Page 2 is not optional.</b> It is where 6A reconciles to the challans, and each of its twelve rows
    /// must carry the five account heads exactly as the ECR's own challan totals report them for that month.
    /// </summary>
    [Fact]
    public void Form_6A_page_two_lists_twelve_remittance_rows_carrying_the_ecr_challan_account_heads()
    {
        var (c, empId) = BuildPfCompany();
        var form = PfStatutoryForms.BuildForm6A(c, new DateOnly(2025, 3, 1));

        Assert.Equal(12, form.Remittances.Count);
        Assert.Equal(new DateOnly(2025, 3, 1), form.Remittances[0].Month);
        Assert.Equal(new DateOnly(2026, 2, 1), form.Remittances[^1].Month);

        var april = new DateOnly(2025, 4, 1);
        var totals = PfEcr.Build(c, new[] { empId }, april, april.AddMonths(1).AddDays(-1)).Totals;
        var aprilRow = form.Remittances.Single(r => r.Month == april);

        Assert.Equal(totals.Account1, aprilRow.EpfContributionsAccount1);
        Assert.Equal(totals.Account10, aprilRow.PensionFundContributionsAccount10);
        Assert.Equal(totals.Account21, aprilRow.EdliContributionAccount21);
        Assert.Equal(totals.Account2, aprilRow.AdminChargesAccount2);
        Assert.Equal(totals.Account22, aprilRow.EdliAdminChargesAccount22);
        Assert.True(aprilRow.EpfContributionsAccount1 > 0, "a nil challan would satisfy the equalities vacuously");

        // The date of remittance is a CHALLAN fact, not a book entry. It is null — never quietly defaulted to the
        // month end, which would print an assertion that the money was remitted on that day.
        Assert.All(form.Remittances, r => Assert.Null(r.DateOfRemittance));
    }

    // ------------------------------------------------------------------------------------- PF Forms 5 and 10

    /// <summary>Form 5 lists only the members who joined the Fund <b>during the month</b> — and the month
    /// boundaries are inclusive at both ends, so a first-of-month and a last-of-month joiner both appear on that
    /// month's return and on no other.</summary>
    [Fact]
    public void Form_5_lists_only_members_whose_fund_join_date_falls_in_the_month()
    {
        var (c, empId) = BuildPfCompany();
        var e = c.FindEmployee(empId)!;
        e.PfJoinDate = new DateOnly(2025, 4, 30);   // the last day of April

        Assert.Empty(PfStatutoryForms.BuildForm5(c, new DateOnly(2025, 3, 1)).Rows);
        var row = Assert.Single(PfStatutoryForms.BuildForm5(c, new DateOnly(2025, 4, 1)).Rows);
        Assert.Equal(empId, row.Member.EmployeeId);
        Assert.Empty(PfStatutoryForms.BuildForm5(c, new DateOnly(2025, 5, 1)).Rows);

        e.PfJoinDate = new DateOnly(2025, 5, 1);    // the first day of May
        Assert.Empty(PfStatutoryForms.BuildForm5(c, new DateOnly(2025, 4, 1)).Rows);
        Assert.Single(PfStatutoryForms.BuildForm5(c, new DateOnly(2025, 5, 1)).Rows);
    }

    /// <summary>
    /// Form 10 is <i>the leavers return</i> and it reads <see cref="Employee.DateOfLeaving"/>. An empty Form 10
    /// means "no member left this month" — a legitimate return, not a failure — and a member with no recorded
    /// leaving date is never on it, because an unrecorded date is not an event in the month.
    /// </summary>
    [Fact]
    public void Form_10_lists_only_members_whose_date_of_leaving_falls_in_the_month_and_is_empty_otherwise()
    {
        var (c, empId) = BuildPfCompany();
        var e = c.FindEmployee(empId)!;

        Assert.Null(e.DateOfLeaving);
        Assert.Empty(PfStatutoryForms.BuildForm10(c, new DateOnly(2025, 6, 1)).Rows);

        e.DateOfLeaving = new DateOnly(2025, 6, 15);
        var row = Assert.Single(PfStatutoryForms.BuildForm10(c, new DateOnly(2025, 6, 1)).Rows);
        Assert.Equal(empId, row.Member.EmployeeId);
        Assert.Equal(new DateOnly(2025, 6, 15), row.Member.DateOfLeavingService);
        Assert.Empty(PfStatutoryForms.BuildForm10(c, new DateOnly(2025, 7, 1)).Rows);

        // Reason for Leaving is not maintained — printed, ruled and blank, never guessed.
        Assert.Equal(string.Empty, row.ReasonForLeaving);
    }

    // ------------------------------------------------------------------------------------------ PF Form 12A

    /// <summary>
    /// 🔴 <b>Form 12A reconciles to the challan by construction.</b> Its account heads are the same
    /// <see cref="PfChallanTotals"/> the ECR produces for the same wage month, and the A/c 1 identity
    /// (recovered from employees + payable by employer) must hold exactly.
    /// </summary>
    [Fact]
    public void Form_12A_account_heads_equal_the_ecr_challan_totals_for_the_same_month()
    {
        var (c, empId) = BuildPfCompany();
        var april = new DateOnly(2025, 4, 1);
        var ecr = PfEcr.Build(c, new[] { empId }, april, april.AddMonths(1).AddDays(-1));

        var form = PfStatutoryForms.BuildForm12A(c, april);

        Assert.Equal(ecr.Totals.Account10, form.ContributionPayableByEmployerAccount10);
        Assert.Equal(ecr.Totals.Account21, form.ContributionPayableByEmployerAccount21);
        Assert.Equal(ecr.Totals.Account2, form.AdministrativeChargesDueAccount2);
        Assert.Equal(ecr.Totals.Account22, form.AdministrativeChargesDueAccount22);
        Assert.Equal(ecr.Totals.Account1,
            form.ContributionRecoveredFromEmployeesAccount1 + form.ContributionPayableByEmployerAccount1);
        Assert.True(ecr.Totals.Account1 > 0, "a nil challan would satisfy every equality above vacuously");

        // 12A also carries the CURRENCY period, not the financial year.
        Assert.Equal(new DateOnly(2025, 3, 1), form.CurrencyPeriodFrom);
    }

    /// <summary>
    /// 🔴 <b>Due and remitted are different claims.</b> A remitted figure is a fact about a bank challan; defaulting
    /// it to the due figure would print an assertion that money was paid. It stays null, and so does the date.
    /// </summary>
    [Fact]
    public void Form_12A_never_defaults_the_remitted_figures_to_the_due_figures()
    {
        var form = PfStatutoryForms.BuildForm12A(BuildPfCompany().Company, new DateOnly(2025, 4, 1));

        Assert.True(form.ContributionRecoveredFromEmployeesAccount1 > 0, "nothing was due — the test is vacuous");
        Assert.Null(form.ContributionRemittedEmployeesShare);
        Assert.Null(form.ContributionRemittedEmployersShare);
        Assert.Null(form.AdministrativeChargesRemitted);
        Assert.Null(form.DateOfRemittance);
        Assert.Null(form.GroupCode);       // no corresponding master field — printed blank, never invented
    }

    // -------------------------------------------------------------------------------------------- ESI Form 3

    /// <summary>
    /// ESI Form 3 is the <i>return of declaration forms</i> — filed once, on entry into coverage. A member
    /// therefore appears on the month their coverage begins and on no later month, and the Insurance-Number
    /// column is blank <b>by the form's own design</b> (the branch office fills it in), not because we lost it.
    /// </summary>
    [Fact]
    public void Form_3_lists_a_member_only_in_the_month_their_coverage_begins()
    {
        var (c, empId) = BuildEsiCompany();
        var april = new DateOnly(2025, 4, 1);

        var row = Assert.Single(EsiStatutoryForms.BuildForm3(c, april).Rows);
        Assert.Equal(empId, row.Member.EmployeeId);
        Assert.Equal("E-100", row.Member.DistinguishingNumber);
        Assert.Equal(Ip, row.Member.InsuranceNumber);

        // A declaration is filed once — the member is not on May's return as well.
        Assert.Empty(EsiStatutoryForms.BuildForm3(c, new DateOnly(2025, 5, 1)).Rows);

        // The last column is filled in at the branch office; blank here is the form working as designed.
        Assert.Equal(string.Empty, row.Member.InsuranceNumberAllottedByCorporation);
        Assert.Equal(string.Empty, row.Member.FathersOrHusbandsName);
    }

    // ---------------------------------------------------------------------------------------- ESI Forms 5, 6

    /// <summary>ESI Form 5 is the half-yearly return of contributions: six monthly projections aggregated per
    /// insured person over the Apr–Sep or Oct–Mar contribution period.</summary>
    [Fact]
    public void Form_5_aggregates_the_six_months_of_the_contribution_period_per_insured_person()
    {
        var (c, empId) = BuildEsiCompany();
        var form = EsiStatutoryForms.BuildForm5(c, new DateOnly(2025, 4, 1));

        Assert.Equal(new DateOnly(2025, 4, 1), form.ContributionPeriodFrom);
        Assert.Equal(new DateOnly(2025, 9, 30), form.ContributionPeriodTo);

        var row = Assert.Single(form.Rows);
        Assert.Equal(empId, row.Member.EmployeeId);

        // The aggregate equals the sum of the six monthly projections it is built from — not a seventh recomputation.
        long wages = 0;
        var days = 0;
        foreach (var m in PayrollStatutoryPeriods.EsiContributionPeriodMonths(new DateOnly(2025, 4, 1)))
        {
            var monthly = EsiMonthlyContribution.Build(c, new[] { empId }, m.From, m.To);
            foreach (var r in monthly.Rows) { wages += r.TotalMonthlyWages; days += r.NoOfDays; }
        }
        Assert.True(wages > 0, "the fixture posted no ESI wages — the equalities below would be vacuous");
        Assert.Equal(wages, row.TotalWages);
        Assert.Equal(days, row.NoOfDaysWagesPaid);

        // Column 7 is the form's own "5/4" — column 5 (wages) over column 4 (days), which the vendor states in the
        // column caption itself. It is NOT Form 6's 27/26 column; the two forms carry two different averages.
        Assert.Equal(decimal.Round(wages / (decimal)days, 2, MidpointRounding.AwayFromZero), row.AverageDailyWages);

        // The dispensary is not maintained — printed, ruled, blank.
        Assert.Equal(string.Empty, row.Member.Dispensary);
    }

    /// <summary>
    /// Column 7(A) — <i>whether still working</i> — is read from the member's date of leaving service. A member with
    /// no recorded leaving date is still working; one who left inside the period is not. This is the column that
    /// depends on <see cref="Employee.DateOfLeaving"/> being reachable at all.
    /// </summary>
    [Fact]
    public void Form_5_column_7A_still_working_is_read_from_the_date_of_leaving()
    {
        var (c, empId) = BuildEsiCompany();
        var e = c.FindEmployee(empId)!;

        Assert.True(Assert.Single(EsiStatutoryForms.BuildForm5(c, new DateOnly(2025, 4, 1)).Rows)
            .StillWorkingWithinCeiling);

        e.DateOfLeaving = new DateOnly(2025, 7, 31);   // inside the Apr–Sep period
        Assert.False(Assert.Single(EsiStatutoryForms.BuildForm5(c, new DateOnly(2025, 4, 1)).Rows)
            .StillWorkingWithinCeiling);

        // A member who leaves AFTER the period end was still working during it.
        e.DateOfLeaving = new DateOnly(2025, 11, 30);
        Assert.True(Assert.Single(EsiStatutoryForms.BuildForm5(c, new DateOnly(2025, 4, 1)).Rows)
            .StillWorkingWithinCeiling);
    }

    /// <summary>
    /// Form 6 is the <b>register of employees</b>, month-columned across the contribution period: six month cells
    /// per member plus the period totals the register foots to.
    /// </summary>
    [Fact]
    public void Form_6_carries_one_month_cell_per_month_of_the_contribution_period_and_foots_to_them()
    {
        var (c, _) = BuildEsiCompany();
        var form = EsiStatutoryForms.BuildForm6(c, new DateOnly(2025, 4, 1));

        Assert.Equal(new DateOnly(2025, 4, 1), form.ContributionPeriodFrom);
        Assert.Equal(new DateOnly(2025, 9, 30), form.ContributionPeriodTo);

        Assert.Equal(6, form.Months.Count);
        var row = Assert.Single(form.Rows);
        Assert.Equal(6, row.Months.Count);
        Assert.Equal(new DateOnly(2025, 4, 1), row.Months[0].Month);
        Assert.Equal(new DateOnly(2025, 9, 1), row.Months[^1].Month);

        Assert.Equal(row.Months.Sum(m => m.TotalWages), row.TotalWagesInContributionPeriod);
        Assert.Equal(row.Months.Sum(m => m.NoOfDaysWagesPaid), row.TotalDaysInContributionPeriod);
        Assert.Equal(row.Months.Sum(m => m.EmployeesShareOfContribution),
            row.TotalEmployeesShareInContributionPeriod);
        Assert.True(row.TotalWagesInContributionPeriod > 0,
            "an all-zero register would satisfy every equality above vacuously");

        // Occupation and department come from the master, not from nowhere.
        Assert.Equal("Machine Operator", row.Member.Occupation);
    }

    /// <summary>
    /// 🔴 <b>The average daily wage, and the divisor this book deliberately does NOT apply.</b> The reference
    /// product captions the column <c>27/26</c> but publishes no rule for choosing between the two divisors, and no
    /// retrievable statutory source states one. This book therefore reports total wages ÷ days for which wages were
    /// paid — the same average daily wage its own ESI engine tests the employee-share waiver against — and says so
    /// in the column's footnote rather than inventing a rule. This test pins the definition actually shipped, so
    /// that a future change to it has to be deliberate.
    /// </summary>
    [Fact]
    public void Form_6_average_daily_wage_is_wages_over_days_paid_and_the_footnote_says_so()
    {
        var row = Assert.Single(EsiStatutoryForms.BuildForm6(BuildEsiCompany().Company, new DateOnly(2025, 4, 1)).Rows);

        Assert.True(row.TotalDaysInContributionPeriod > 0);
        Assert.Equal(
            decimal.Round(row.TotalWagesInContributionPeriod / (decimal)row.TotalDaysInContributionPeriod, 2,
                MidpointRounding.AwayFromZero),
            row.AverageDailyWages);

        // The footnote states the definition AND records that the 27/26 divisor was not applied — the honest form
        // of "the source is silent". If someone later applies a divisor, this string has to change with it.
        Assert.Contains("total wages ÷ total days", EsiStatutoryForms.AverageDailyWagesNote);
        Assert.Contains("27", EsiStatutoryForms.AverageDailyWagesNote);
        Assert.Contains("not applied here", EsiStatutoryForms.AverageDailyWagesNote);
    }

    /// <summary>A member with a zero-day month divides by no one: the average is zero, not an exception.</summary>
    [Fact]
    public void Form_6_average_daily_wage_is_zero_rather_than_a_division_by_zero_when_no_days_were_paid()
    {
        var (c, _) = BuildEsiCompany();

        // The structure takes effect in April, so the Oct–Mar period has months with no wages at all.
        var form = EsiStatutoryForms.BuildForm6(c, new DateOnly(2024, 10, 1));
        var row = Assert.Single(form.Rows);

        Assert.Equal(0, row.TotalDaysInContributionPeriod);
        Assert.Equal(0m, row.AverageDailyWages);
        Assert.Equal(0m, Assert.Single(EsiStatutoryForms.BuildForm5(c, new DateOnly(2024, 10, 1)).Rows)
            .AverageDailyWages);
    }

    // ------------------------------------------------------------------------------------------------ fixtures

    /// <summary>
    /// A one-member PF establishment: ₹20,000 basic from 1 April 2025, capped at the ₹15,000 ceiling, with the four
    /// statutory pay heads the ECR reads. Deliberately the SAME shape as the shipped ECR writer fixture, so these
    /// forms and that file are demonstrably reading one engine.
    /// </summary>
    /// <summary>
    /// 🔴 <b>Two PF members sharing one UAN must be refused, not silently merged.</b>
    ///
    /// <para>Nothing in the product enforces UAN uniqueness — <c>PayrollService.ValidateUan</c> checks the 12-digit
    /// SHAPE only, and there is no <c>FindEmployeeByUan</c> anywhere — so two employees can carry the same one after
    /// a typo or an import. <see cref="PfEcr.Build"/> then emits a member row for each, and any consumer that
    /// indexes those rows <i>by UAN</i> keeps whichever it saw last. Before this guard, Forms 3A and 6A did exactly
    /// that: both members' contribution cards printed the SAME figures, under each member's own name and PF account
    /// number. A wrong figure under a right heading, silently, on an annual statutory return.</para>
    ///
    /// <para>The refusal matches the rule this file already applies to a missing UAN: a real data fault the operator
    /// must fix, never a member quietly going missing or quietly wearing someone else's numbers.</para>
    /// </summary>
    [Fact]
    public void Two_pf_members_sharing_one_uan_are_refused_rather_than_silently_merged()
    {
        var (c, _) = BuildPfCompany();
        var pay = new PayrollService(c);
        // A second member on the SAME UAN, deliberately on a different wage so a merge is detectable.
        var twin = pay.CreateEmployee("Meera Nair", c.FindEmployeeGroupByName("Staff")!.Id, uan: Uan);
        pay.SetEmployeePfDetails(twin.Id, applicable: true, contributeOnHigherWages: false);
        twin.PfAccountNumber = "MH/BAN/0000001/000/0000999";
        new SalaryStructureService(c).DefineForEmployee(twin.Id, FyStart, new[]
        {
            new SalaryStructureLine(c.FindPayHeadByName("Basic")!.Id, 0, new Money(9000m)),
            new SalaryStructureLine(c.FindPayHeadByName("Employee EPF")!.Id, 1),
            new SalaryStructureLine(c.FindPayHeadByName("Employer EPF")!.Id, 2),
            new SalaryStructureLine(c.FindPayHeadByName("Employer Pension")!.Id, 3),
            new SalaryStructureLine(c.FindPayHeadByName("EDLI")!.Id, 4),
        });

        var march = new DateOnly(2025, 3, 1);
        foreach (var build in new Action[]
        {
            () => PfStatutoryForms.BuildForm3A(c, march),
            () => PfStatutoryForms.BuildForm6A(c, march),
            () => PfStatutoryForms.BuildForm12A(c, new DateOnly(2025, 4, 1)),
        })
        {
            var ex = Assert.Throws<InvalidOperationException>(build);
            Assert.Contains(Uan, ex.Message);
            Assert.Contains("Meera Nair", ex.Message);
            Assert.Contains("Sanjay Kumar", ex.Message);
        }
    }

    /// <summary>
    /// The same fault on the ESI side: two insured persons sharing one 10-digit IP number. The monthly contribution
    /// projection keys its rows on the IP number, so an IP-indexed lookup merges them exactly the way the UAN one
    /// did — Forms 5 and 6 would report one person's days and wages against both.
    /// </summary>
    [Fact]
    public void Two_esi_members_sharing_one_ip_number_are_refused_rather_than_silently_merged()
    {
        var (c, _) = BuildEsiCompany();
        var pay = new PayrollService(c);
        var twin = pay.CreateEmployee("Meera Nair", c.FindEmployeeGroupByName("Staff")!.Id,
            employeeNumber: "E-200", esiNumber: Ip);
        pay.SetEmployeeEsiDetails(twin.Id, applicable: true);
        twin.DateOfJoining = FyStart;
        new SalaryStructureService(c).DefineForEmployee(twin.Id, FyStart, new[]
        {
            new SalaryStructureLine(c.FindPayHeadByName("Basic")!.Id, 0, new Money(9000m)),
            new SalaryStructureLine(c.FindPayHeadByName("Employee ESI")!.Id, 1),
            new SalaryStructureLine(c.FindPayHeadByName("Employer ESI")!.Id, 2),
        });

        foreach (var build in new Action[]
        {
            () => EsiStatutoryForms.BuildForm5(c, FyStart),
            () => EsiStatutoryForms.BuildForm6(c, FyStart),
        })
        {
            var ex = Assert.Throws<InvalidOperationException>(build);
            Assert.Contains(Ip, ex.Message);
            Assert.Contains("Meera Nair", ex.Message);
            Assert.Contains("Sanjay Kumar", ex.Message);
        }
    }

    private static (Company Company, Guid EmployeeId) BuildPfCompany()
    {
        var c = CompanyFactory.CreateSeeded("PF Forms Co", FyStart, FyStart);
        var pay = new PayrollService(c);
        pay.EnablePayroll();
        pay.EnableProvidentFund(capWagesAtCeiling: true);
        var ph = new PayHeadService(c);
        var indirect = c.FindGroupByName("Indirect Expenses")!.Id;
        var liab = c.FindGroupByName("Current Liabilities")!.Id;

        var basicHead = ph.CreatePayHead("Basic", PayHeadType.Earnings, PayHeadCalculationType.FlatRate,
            underGroupId: indirect, partOfPfWages: true);
        var ee = ph.CreatePayHead("Employee EPF", PayHeadType.EmployeesStatutoryDeductions,
            PayHeadCalculationType.AsUserDefinedValue, underGroupId: liab,
            pfComponent: PfStatutoryComponent.EmployeeProvidentFund);
        var er = ph.CreatePayHead("Employer EPF", PayHeadType.EmployersStatutoryContributions,
            PayHeadCalculationType.AsUserDefinedValue, underGroupId: liab,
            pfComponent: PfStatutoryComponent.EmployerProvidentFund);
        var eps = ph.CreatePayHead("Employer Pension", PayHeadType.EmployersStatutoryContributions,
            PayHeadCalculationType.AsUserDefinedValue, underGroupId: liab,
            pfComponent: PfStatutoryComponent.EmployerPension);
        var edli = ph.CreatePayHead("EDLI", PayHeadType.EmployersOtherCharges,
            PayHeadCalculationType.AsUserDefinedValue, underGroupId: liab,
            pfComponent: PfStatutoryComponent.EmployeesDepositLinkedInsurance);

        var e = pay.CreateEmployee("Sanjay Kumar", pay.CreateEmployeeGroup("Staff").Id, uan: Uan);
        pay.SetEmployeePfDetails(e.Id, applicable: true, contributeOnHigherWages: false);
        e.PfAccountNumber = "MH/BAN/0000001/000/0000123";
        e.DateOfJoining = new DateOnly(2020, 1, 1);
        e.PfJoinDate = new DateOnly(2020, 1, 1);
        e.DateOfBirth = new DateOnly(1990, 5, 4);
        e.Gender = "Male";

        new SalaryStructureService(c).DefineForEmployee(e.Id, FyStart, new[]
        {
            new SalaryStructureLine(basicHead.Id, 0, new Money(20000m)),
            new SalaryStructureLine(ee.Id, 1),
            new SalaryStructureLine(er.Id, 2),
            new SalaryStructureLine(eps.Id, 3),
            new SalaryStructureLine(edli.Id, 4),
        });
        return (c, e.Id);
    }

    /// <summary>A one-member ESI establishment: ₹15,000 basic from 1 April 2025 (inside the ₹21,000 coverage
    /// ceiling), with the employee and employer ESI heads the monthly contribution file reads.</summary>
    private static (Company Company, Guid EmployeeId) BuildEsiCompany()
    {
        var c = CompanyFactory.CreateSeeded("ESI Forms Co", FyStart, FyStart);
        var pay = new PayrollService(c);
        pay.EnablePayroll();
        pay.EnableEsi(employerCode: "12345678901234567");
        var ph = new PayHeadService(c);
        var indirect = c.FindGroupByName("Indirect Expenses")!.Id;
        var liab = c.FindGroupByName("Current Liabilities")!.Id;

        var basicHead = ph.CreatePayHead("Basic", PayHeadType.Earnings, PayHeadCalculationType.FlatRate,
            underGroupId: indirect, partOfEsiWages: true);
        var ee = ph.CreatePayHead("Employee ESI", PayHeadType.EmployeesStatutoryDeductions,
            PayHeadCalculationType.AsUserDefinedValue, underGroupId: liab,
            esiComponent: EsiStatutoryComponent.EmployeeStateInsurance);
        var er = ph.CreatePayHead("Employer ESI", PayHeadType.EmployersStatutoryContributions,
            PayHeadCalculationType.AsUserDefinedValue, underGroupId: liab,
            esiComponent: EsiStatutoryComponent.EmployerStateInsurance);

        var e = pay.CreateEmployee("Sanjay Kumar", pay.CreateEmployeeGroup("Staff").Id,
            employeeNumber: "E-100", esiNumber: Ip);
        pay.SetEmployeeEsiDetails(e.Id, applicable: true);
        e.Designation = "Machine Operator";
        e.DateOfJoining = FyStart;

        new SalaryStructureService(c).DefineForEmployee(e.Id, FyStart, new[]
        {
            new SalaryStructureLine(basicHead.Id, 0, new Money(15000m)),
            new SalaryStructureLine(ee.Id, 1),
            new SalaryStructureLine(er.Id, 2),
        });
        return (c, e.Id);
    }
}
