using Apex.Ledger.Domain;
using Apex.Ledger.Services;

namespace Apex.Ledger.Reports;

/// <summary>
/// The identity block the ESI statutory forms repeat for an insured person (IP). <see cref="FathersOrHusbandsName"/>
/// and <see cref="Dispensary"/> are carried as EMPTY strings on purpose — see
/// <see cref="EsiStatutoryForms.NotMaintainedNote"/>. <see cref="InsuranceNumberAllottedByCorporation"/> is empty by
/// the FORM'S OWN DESIGN: its caption says it is entered at the branch office, so a blank there is not a gap.
/// </summary>
public sealed record EsiFormMember(
    Guid EmployeeId,
    string InsuranceNumber,
    string Name,
    string DistinguishingNumber,
    string FathersOrHusbandsName,
    string Occupation,
    string DepartmentOrShift,
    DateOnly? DateOfAppointment,
    DateOnly? DateOfLeavingService,
    string InsuranceNumberAllottedByCorporation,
    string Dispensary);

/// <summary>One row of ESI <b>Form 3</b> — the return of declaration forms (ESI (General) Regulations 1950,
/// Reg. 14). Columns per help.tallysolutions.com/docs/te9rel65/Payroll/Form_3.htm.</summary>
public sealed record EsiForm3Row(int SerialNumber, EsiFormMember Member);

/// <summary>ESI <b>Form 3</b> — <i>Return of Declaration Forms</i> for a month: the insured persons whose ESI
/// coverage BEGINS in that month.</summary>
public sealed record EsiForm3(
    string EstablishmentName,
    string? EmployerCode,
    DateOnly Month,
    IReadOnlyList<EsiForm3Row> Rows);

/// <summary>
/// One insured person's row of ESI <b>Form 5</b> — the half-yearly return of contributions (Reg. 26). The column
/// numbers are the form's own, per
/// help.tallysolutions.com/tally-prime/payroll-esi-reports/esi-form-5-tally/:
/// col 2 Insurance No. · col 3 Name of the Insured Person · col 4 No. of days for which wages paid/payable ·
/// col 5 Total amount of wages paid/payable · col 6 Employees' contribution deducted ·
/// col 7 <b>"Average Daily wages 5/4"</b> · col 8 whether still working within the insurable wages ceiling ·
/// col 9 Name of the Dispensary of IP · Remarks.
///
/// <para>🔴 <b>Column 7 is column 5 ÷ column 4 — the vendor states the formula in the column caption itself
/// ("5/4").</b> It is NOT the 27/26 divisor that appears on Form 6; the two forms carry two different average-daily-wage
/// columns and conflating them would put a wrong figure under a right heading.</para>
/// </summary>
public sealed record EsiForm5Row(
    int SerialNumber,
    EsiFormMember Member,
    int NoOfDaysWagesPaid,
    long TotalWages,
    long EmployeesContributionDeducted,
    decimal AverageDailyWages,
    bool StillWorkingWithinCeiling,
    string Remarks);

/// <summary>ESI <b>Form 5</b> — <i>Return of Contributions</i> for a contribution period (Apr–Sep or Oct–Mar;
/// Reg. 26).</summary>
public sealed record EsiForm5(
    string EstablishmentName,
    string? EmployerCode,
    DateOnly ContributionPeriodFrom,
    DateOnly ContributionPeriodTo,
    IReadOnlyList<EsiForm5Row> Rows,
    int TotalDays,
    long TotalWages,
    long TotalEmployeesContribution);

/// <summary>One wage month of an ESI <b>Form 6</b> row — the monthly block the register is columned by:
/// <i>No. of days for which wages paid/payable</i> · <i>Total amount of wages paid/payable</i> ·
/// <i>Employees' share of contribution</i>.</summary>
public sealed record EsiForm6Month(
    DateOnly Month,
    int NoOfDaysWagesPaid,
    long TotalWages,
    long EmployeesShareOfContribution);

/// <summary>
/// One insured person's row of ESI <b>Form 6</b> — the register of employees (Reg. 32(1)). Columns per
/// help.tallysolutions.com/tally-prime/payroll-esi-reports/esi-form-6-tally/: Insurance No. · Name of the Insured
/// Person · Occupation · Rate of Wages etc., in the first wage period · Deptt. and shift, if any · date of
/// appointment / leaving service · Father's or Husband's Name · Insurance No. allotted by the corporation ·
/// then per month No. of days / Total wages / Employees' share, then the contribution-period totals and
/// <i>Daily wages</i>.
/// </summary>
public sealed record EsiForm6Row(
    int SerialNumber,
    EsiFormMember Member,
    long RateOfWagesInFirstWagePeriod,
    IReadOnlyList<EsiForm6Month> Months,
    int TotalDaysInContributionPeriod,
    long TotalWagesInContributionPeriod,
    long TotalEmployeesShareInContributionPeriod,
    decimal AverageDailyWages,
    string Remarks);

/// <summary>ESI <b>Form 6</b> — the <i>Register of Employees</i> a covered employer maintains (Reg. 32(1)),
/// month-columned over a contribution period.</summary>
public sealed record EsiForm6(
    string EstablishmentName,
    string? EmployerCode,
    DateOnly ContributionPeriodFrom,
    DateOnly ContributionPeriodTo,
    IReadOnlyList<StatutoryMonth> Months,
    IReadOnlyList<EsiForm6Row> Rows);

/// <summary>
/// The three <b>Employees' State Insurance statutory forms</b> beyond the monthly contribution file (census row
/// 7.21) — <b>Form 3</b> (Reg. 14, return of declaration forms), <b>Form 5</b> (Reg. 26, half-yearly return of
/// contributions) and <b>Form 6</b> (Reg. 32(1), register of employees).
///
/// <para><b>What these are, stated plainly.</b> Form 6 is a <b>register the employer maintains</b>, not a return he
/// files, so building it is unambiguously correct. For Forms 3 and 5 we could <b>not</b> establish from an
/// admissible ESIC source whether the paper return survives ESIC's online contribution filing (esic.gov.in fails
/// TLS chain validation for automated fetch). They are therefore implemented — and captioned — as <b>registers
/// derived from our own posted payroll</b>. Nothing here, and nothing the UI renders from it, asserts a filing
/// status in either direction.</para>
///
/// <para><b>No ESI arithmetic is written here.</b> Days and wages are read verbatim off
/// <see cref="EsiMonthlyContribution.Build"/> — the same projection the monthly contribution file comes from — and
/// each member's contribution is read off the SAME <see cref="PayrollComputationService"/> the payroll voucher
/// posts, through <see cref="PayrollComputationResult.EsiEmployeeContribution"/>. The only computation in this file
/// is addition and one documented division (see <see cref="AverageDailyWagesNote"/>).</para>
/// </summary>
public static class EsiStatutoryForms
{
    /// <summary>The footnote every column carries whose source the Employee master does not maintain (Father's /
    /// Husband's Name, the IP's dispensary). The column is printed and ruled with the form's own heading and left
    /// blank — dropping it would misrepresent the form and inventing a value would be worse than both.</summary>
    public const string NotMaintainedNote = "Column not maintained in this book — complete by hand.";

    /// <summary>The footnote for Form 3's last column, which the form's OWN caption says is filled in at the branch
    /// office. A blank here is the form working as designed, not a gap in this book.</summary>
    public const string BranchOfficeColumnNote =
        "Insurance number allotted by the Corporation is entered at the branch office — blank by the form's design.";

    /// <summary>
    /// 🔴 The note Form 6's average-daily-wage column carries, and the reason it is worded this way.
    ///
    /// <para>The reference product captions that column <c>"Daily wages (27/26)"</c> but <b>does not state the rule
    /// for choosing between the two divisors</b>, and no retrievable ESIC or gazette source attests it either (the
    /// ESI (Central) Rules' 26-day deeming applies to <i>benefit</i> computation, not to this register's column).
    /// Rather than invent a divisor rule, this book reports the average daily wage the way its OWN ESI engine
    /// already defines it — total wages ÷ the days for which wages were paid — which is the same quantity
    /// <see cref="EsiContribution.EmployeeExemptionDailyWage"/> is tested against when the ₹176 employee-share
    /// waiver is applied. That keeps the register consistent with the contribution it reports, by construction.</para>
    /// </summary>
    public const string AverageDailyWagesNote =
        "Average daily wages = total wages ÷ total days for which wages were paid — the same average daily wage "
        + "this book applies the ₹176 employee-share waiver against. A 27/26 standard-days divisor is used by some "
        + "registers; no retrievable statutory source states the rule for choosing between 27 and 26, so it is not "
        + "applied here.";

    /// <summary>The ESI-applicable members of <paramref name="company"/>, in name order. A member without a valid
    /// 10-digit IP number cannot appear on an ESI return at all — <see cref="EsiMonthlyContribution.Build"/>
    /// refuses such a company outright, and these forms inherit that refusal rather than silently dropping a
    /// member.</summary>
    private static List<Employee> EsiMembers(Company company)
        => company.Employees
            .Where(e => e.EsiApplicable)
            .OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(e => e.Id)
            .ToList();

    /// <summary>The establishment name the forms print in <i>"Name and Address of the Factory/Establishment"</i>.</summary>
    private static string EstablishmentName(Company company)
        => string.IsNullOrWhiteSpace(company.MailingName) ? company.Name : company.MailingName;

    /// <summary>The identity block for <paramref name="employee"/>. <i>Distinguishing Number with Employer</i> is
    /// the Employee master's employee number; <i>Occupation</i> is the designation; <i>Deptt. and shift</i> is the
    /// function, falling back to the location.</summary>
    private static EsiFormMember Identity(Employee employee) => new(
        EmployeeId: employee.Id,
        InsuranceNumber: (employee.EsiNumber ?? string.Empty).Trim(),
        Name: employee.Name,
        DistinguishingNumber: (employee.EmployeeNumber ?? string.Empty).Trim(),
        FathersOrHusbandsName: string.Empty,   // not maintained — see NotMaintainedNote
        Occupation: (employee.Designation ?? string.Empty).Trim(),
        DepartmentOrShift: string.IsNullOrWhiteSpace(employee.Function)
            ? (employee.Location ?? string.Empty).Trim()
            : employee.Function!.Trim(),
        DateOfAppointment: employee.DateOfJoining,
        DateOfLeavingService: employee.DateOfLeaving,
        InsuranceNumberAllottedByCorporation: string.Empty,  // entered at the branch office — see BranchOfficeColumnNote
        Dispensary: string.Empty);             // not maintained — see NotMaintainedNote

    /// <summary>
    /// The per-member ESI figures for one wage month: days and wages read off the monthly-contribution projection
    /// (so the forms reconcile to the monthly file by construction), and the employee's own contribution read off
    /// the same payroll computation the voucher posts. Keyed by employee id.
    /// </summary>
    private static Dictionary<Guid, (int Days, long Wages, long EmployeeContribution)> MonthFigures(
        Company company, List<Employee> members, StatutoryMonth month)
    {
        var ids = members.Select(e => e.Id).ToList();
        var monthly = EsiMonthlyContribution.Build(company, ids, month.From, month.To);

        // The monthly projection keys the row on the IP number (it is the file's key), so map it back to the member.
        var byIp = new Dictionary<string, EsiContributionRow>(StringComparer.Ordinal);
        foreach (var r in monthly.Rows) byIp[r.IpNumber] = r;

        var computation = new PayrollComputationService(company);
        var figures = new Dictionary<Guid, (int, long, long)>();
        foreach (var e in members)
        {
            var ip = (e.EsiNumber ?? string.Empty).Trim();
            byIp.TryGetValue(ip, out var row);

            long contribution = 0;
            try
            {
                contribution = WholeRupee(computation.Compute(e.Id, month.From, month.To).EsiEmployeeContribution);
            }
            catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
            {
                // A member with no salary structure in force for the month contributes nothing that month; the
                // monthly projection has already reported the days and wages as zero for the same reason.
                contribution = 0;
            }

            figures[e.Id] = (row?.NoOfDays ?? 0, row?.TotalMonthlyWages ?? 0L, contribution);
        }
        return figures;
    }

    /// <summary>Whole rupees, truncated — the same whole-rupee convention the monthly contribution file uses, so a
    /// register row and the file it reconciles to never differ by a rounding step.</summary>
    private static long WholeRupee(Money money) => (long)decimal.Truncate(money.Amount);

    // ============================================================================================ Form 3

    /// <summary>
    /// ESI <b>Form 3</b> for the wage month containing <paramref name="month"/> — the <i>return of declaration
    /// forms</i> (Reg. 14): the insured persons whose ESI coverage <b>begins</b> in that month. "Begins" is decided
    /// from the shipped coverage engine, not re-derived: a member is on this return when
    /// <see cref="PayrollComputationService.IsEsiCovered"/> is true at the end of this month and was false at the
    /// end of the previous one (or the member joined during the month) — a declaration is filed once, on entry.
    /// </summary>
    public static EsiForm3 BuildForm3(Company company, DateOnly month)
    {
        ArgumentNullException.ThrowIfNull(company);
        var from = new DateOnly(month.Year, month.Month, 1);
        var to = from.AddMonths(1).AddDays(-1);
        var previousTo = from.AddDays(-1);

        var computation = new PayrollComputationService(company);
        var rows = new List<EsiForm3Row>();
        var serial = 0;
        foreach (var employee in EsiMembers(company))
        {
            bool coveredNow, coveredBefore;
            try
            {
                coveredNow = computation.IsEsiCovered(employee.Id, to);
                coveredBefore = computation.IsEsiCovered(employee.Id, previousTo);
            }
            catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
            {
                continue;   // no structure in force ⇒ the member is not yet an insured person
            }

            if (!coveredNow) continue;
            // A member who joined during this month enters coverage this month even if the coverage test would
            // also have passed a month earlier on a structure that was not yet in force for them.
            var joinedThisMonth = PayrollStatutoryPeriods.FallsWithin(employee.DateOfJoining, from, to);
            if (coveredBefore && !joinedThisMonth) continue;

            rows.Add(new EsiForm3Row(++serial, Identity(employee)));
        }

        return new EsiForm3(EstablishmentName(company), company.EsiConfig?.EmployerCode, from, rows);
    }

    // ============================================================================================ Form 5

    /// <summary>
    /// ESI <b>Form 5</b> for the contribution period containing <paramref name="dateInPeriod"/> (Apr–Sep or
    /// Oct–Mar; Reg. 26) — the half-yearly return of contributions, one row per insured person aggregating the six
    /// wage months. Column 7 is <b>column 5 ÷ column 4</b> (total wages ÷ days for which wages were paid), which is
    /// the formula the column's own caption states; a member with no paid days reports 0 rather than dividing by
    /// zero. Column 8 reads from the member's date of leaving service: still working when they had not left by the
    /// period end.
    /// </summary>
    public static EsiForm5 BuildForm5(Company company, DateOnly dateInPeriod)
    {
        ArgumentNullException.ThrowIfNull(company);
        var from = PayrollStatutoryPeriods.EsiContributionPeriodStart(dateInPeriod);
        var to = PayrollStatutoryPeriods.EsiContributionPeriodEnd(dateInPeriod);
        var months = PayrollStatutoryPeriods.Months(from, to);

        var members = EsiMembers(company);
        var monthly = months.Select(m => MonthFigures(company, members, m)).ToList();

        var rows = new List<EsiForm5Row>(members.Count);
        var serial = 0;
        foreach (var employee in members)
        {
            var days = 0;
            long wages = 0, contribution = 0;
            foreach (var figures in monthly)
            {
                if (!figures.TryGetValue(employee.Id, out var f)) continue;
                days += f.Days;
                wages += f.Wages;
                contribution += f.EmployeeContribution;
            }

            rows.Add(new EsiForm5Row(
                SerialNumber: ++serial,
                Member: Identity(employee),
                NoOfDaysWagesPaid: days,
                TotalWages: wages,
                EmployeesContributionDeducted: contribution,
                AverageDailyWages: days > 0 ? decimal.Round(wages / (decimal)days, 2, MidpointRounding.AwayFromZero) : 0m,
                // "Whether still continue working and drawing wages within the insurable wages ceiling": a member
                // who had not left service by the period end is still working. An unrecorded date of leaving means
                // they have not left — see the T0-13 note in the track report.
                StillWorkingWithinCeiling: employee.DateOfLeaving is not { } left || left > to,
                Remarks: string.Empty));
        }

        return new EsiForm5(
            EstablishmentName: EstablishmentName(company),
            EmployerCode: company.EsiConfig?.EmployerCode,
            ContributionPeriodFrom: from,
            ContributionPeriodTo: to,
            Rows: rows,
            TotalDays: rows.Sum(r => r.NoOfDaysWagesPaid),
            TotalWages: rows.Sum(r => r.TotalWages),
            TotalEmployeesContribution: rows.Sum(r => r.EmployeesContributionDeducted));
    }

    // ============================================================================================ Form 6

    /// <summary>
    /// ESI <b>Form 6</b> for the contribution period containing <paramref name="dateInPeriod"/> — the register of
    /// employees (Reg. 32(1)), one row per insured person with a block of columns per wage month plus the
    /// contribution-period totals. <i>Rate of Wages etc., in the first wage period</i> is the ESI wages of the FIRST
    /// month of the contribution period in which the member had wages (this book carries no separate wage-rate
    /// field on the Employee master). The average-daily-wage column is documented at
    /// <see cref="AverageDailyWagesNote"/> — read it before changing the divisor.
    /// </summary>
    public static EsiForm6 BuildForm6(Company company, DateOnly dateInPeriod)
    {
        ArgumentNullException.ThrowIfNull(company);
        var from = PayrollStatutoryPeriods.EsiContributionPeriodStart(dateInPeriod);
        var to = PayrollStatutoryPeriods.EsiContributionPeriodEnd(dateInPeriod);
        var months = PayrollStatutoryPeriods.Months(from, to);

        var members = EsiMembers(company);
        var monthly = months.Select(m => MonthFigures(company, members, m)).ToList();

        var rows = new List<EsiForm6Row>(members.Count);
        var serial = 0;
        foreach (var employee in members)
        {
            var blocks = new List<EsiForm6Month>(months.Count);
            var days = 0;
            long wages = 0, contribution = 0, firstWagePeriodWages = 0;
            for (var i = 0; i < months.Count; i++)
            {
                monthly[i].TryGetValue(employee.Id, out var f);
                blocks.Add(new EsiForm6Month(months[i].From, f.Days, f.Wages, f.EmployeeContribution));
                days += f.Days;
                wages += f.Wages;
                contribution += f.EmployeeContribution;
                if (firstWagePeriodWages == 0 && f.Wages > 0) firstWagePeriodWages = f.Wages;
            }

            rows.Add(new EsiForm6Row(
                SerialNumber: ++serial,
                Member: Identity(employee),
                RateOfWagesInFirstWagePeriod: firstWagePeriodWages,
                Months: blocks,
                TotalDaysInContributionPeriod: days,
                TotalWagesInContributionPeriod: wages,
                TotalEmployeesShareInContributionPeriod: contribution,
                AverageDailyWages: days > 0 ? decimal.Round(wages / (decimal)days, 2, MidpointRounding.AwayFromZero) : 0m,
                Remarks: string.Empty));
        }

        return new EsiForm6(
            EstablishmentName: EstablishmentName(company),
            EmployerCode: company.EsiConfig?.EmployerCode,
            ContributionPeriodFrom: from,
            ContributionPeriodTo: to,
            Months: months,
            Rows: rows);
    }
}
