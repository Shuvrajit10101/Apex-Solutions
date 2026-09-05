using System.Globalization;
using Apex.Ledger.Domain;
using Apex.Ledger.Services;

namespace Apex.Ledger.Reports;

/// <summary>
/// The identity block every PF statutory form repeats for a member: the PF account number, the name, the UAN the
/// ECR keys on, and the dates the joiner/leaver returns are filtered by. <see cref="FathersOrHusbandsName"/> is
/// carried as an EMPTY string on purpose — see <see cref="PfStatutoryForms.NotMaintainedNote"/>.
/// </summary>
public sealed record PfFormMember(
    Guid EmployeeId,
    string AccountNumber,
    string Name,
    string Uan,
    string FathersOrHusbandsName,
    DateOnly? DateOfBirth,
    string Sex,
    DateOnly? DateOfJoiningTheFund,
    DateOnly? DateOfLeavingService);

/// <summary>One month row of PF <b>Form 3A</b> — the columns as the reference product spells them
/// (help.tallysolutions.com/docs/te9rel66/Payroll/Form_3A.htm, the Form 3A column table). Whole rupees, because
/// every figure is read verbatim off the <see cref="PfEcr"/> projection, which is whole-rupee by the EPFO's own
/// file layout.</summary>
public sealed record PfForm3AMonth(
    DateOnly Month,
    long AmountOfWages,
    long WorkersEpf,
    long HigherRateOfVoluntaryContribution,
    long EmployerEpfDifference,
    long PensionFundContribution,
    long RefundOfAdvance,
    int NonContributingServiceDays);

/// <summary>One member's PF <b>Form 3A</b> contribution card — twelve month rows over the currency period plus the
/// column totals the card foots to.</summary>
public sealed record PfForm3AMember(
    PfFormMember Member,
    string StatutoryRateOfContribution,
    IReadOnlyList<PfForm3AMonth> Months,
    long TotalAmountOfWages,
    long TotalWorkersEpf,
    long TotalHigherRateOfVoluntaryContribution,
    long TotalEmployerEpfDifference,
    long TotalPensionFundContribution,
    long TotalRefundOfAdvance,
    int TotalNonContributingServiceDays);

/// <summary>PF <b>Form 3A</b> — the per-member annual contribution card for a currency period
/// (1 March … 28/29 February).</summary>
public sealed record PfForm3A(
    string EstablishmentName,
    string? EstablishmentCode,
    DateOnly CurrencyPeriodFrom,
    DateOnly CurrencyPeriodTo,
    IReadOnlyList<PfForm3AMember> Members);

/// <summary>One member row of PF <b>Form 6A</b> page 1 — the consolidated annual statement
/// (help.tallysolutions.com/docs/te9rel65/Payroll/Form_6A.htm).</summary>
public sealed record PfForm6AMemberRow(
    int SerialNumber,
    PfFormMember Member,
    long Wages,
    long WorkersContribution,
    long EmployerEpfDifference,
    long PensionFundContribution,
    long RefundOfAdvance,
    long RateOfHigherVoluntaryContribution);

/// <summary>One monthly-remittance row of PF <b>Form 6A</b> page 2 — the challan account heads the annual
/// statement reconciles to. <see cref="DateOfRemittance"/> is a CHALLAN fact, not a fact about our books, so it is
/// always <c>null</c> here; see <see cref="PfStatutoryForms.ChallanFactNote"/>.</summary>
public sealed record PfForm6ARemittanceRow(
    int SerialNumber,
    DateOnly Month,
    long EpfContributionsAccount1,
    long PensionFundContributionsAccount10,
    long EdliContributionAccount21,
    long AdminChargesAccount2,
    long EdliAdminChargesAccount22,
    DateOnly? DateOfRemittance);

/// <summary>PF <b>Form 6A</b> — the consolidated annual statement of contribution: page 1 (per member) and page 2
/// (the twelve monthly remittances).</summary>
public sealed record PfForm6A(
    string EstablishmentName,
    string? EstablishmentCode,
    string StatutoryRateOfContribution,
    DateOnly CurrencyPeriodFrom,
    DateOnly CurrencyPeriodTo,
    IReadOnlyList<PfForm6AMemberRow> Members,
    IReadOnlyList<PfForm6ARemittanceRow> Remittances,
    long TotalWages,
    long TotalWorkersContribution,
    long TotalEmployerEpfDifference,
    long TotalPensionFundContribution,
    long TotalRefundOfAdvance,
    int MembersVoluntarilyContributingAtHigherRate);

/// <summary>One row of PF <b>Form 5</b> — a member who joined the fund during the month
/// (help.tallysolutions.com/docs/te9rel66/Payroll/Form_5.htm).</summary>
public sealed record PfForm5Row(
    int SerialNumber,
    PfFormMember Member,
    string TotalPeriodOfPreviousService,
    string Remarks);

/// <summary>PF <b>Form 5</b> — the monthly return of employees newly joining the Provident Fund scheme.</summary>
public sealed record PfForm5(
    string EstablishmentName,
    string? EstablishmentCode,
    DateOnly Month,
    IReadOnlyList<PfForm5Row> Rows);

/// <summary>One row of PF <b>Form 10</b> — a member who left service during the month
/// (help.tallysolutions.com/docs/te9rel66/Payroll/Form_10.htm).</summary>
public sealed record PfForm10Row(
    int SerialNumber,
    PfFormMember Member,
    string ReasonForLeaving,
    string Remarks);

/// <summary>PF <b>Form 10</b> — <i>"Return of the members leaving service during the month of …"</i>.</summary>
public sealed record PfForm10(
    string EstablishmentName,
    string? EstablishmentCode,
    DateOnly Month,
    IReadOnlyList<PfForm10Row> Rows);

/// <summary>
/// PF <b>Form 12A</b> — the monthly statement of contribution
/// (help.tallysolutions.com/docs/te9rel55/Payroll/Form_12A.htm). Every <c>Due</c> figure is derived from our own
/// posted books through <see cref="PfEcr"/>; every <c>Remitted</c> figure and the date of remittance are facts
/// about a bank challan and are therefore <b>null</b> — never silently equated to the due figure, which would
/// assert a remittance that may not have happened.
/// </summary>
public sealed record PfForm12A(
    string EstablishmentName,
    string? EstablishmentCode,
    string? GroupCode,
    DateOnly Month,
    DateOnly CurrencyPeriodFrom,
    long WagesOnWhichContributionsArePayable,
    long ContributionRecoveredFromEmployeesAccount1,
    long ContributionPayableByEmployerAccount1,
    long ContributionPayableByEmployerAccount10,
    long ContributionPayableByEmployerAccount21,
    long AdministrativeChargesDueAccount2,
    long AdministrativeChargesDueAccount22,
    long? ContributionRemittedEmployeesShare,
    long? ContributionRemittedEmployersShare,
    long? AdministrativeChargesRemitted,
    DateOnly? DateOfRemittance,
    int DetailsOfSubscribers);

/// <summary>
/// The five <b>Provident Fund statutory forms</b> beyond the ECR — Forms 3A, 5, 6A, 10 and 12A (census row 7.20).
///
/// <para><b>What these are, stated plainly.</b> The EPFO's electronic return is the ECR
/// (epfo.gov.in/revamped-ecr/), which this product already builds and exports. <b>No admissible EPFO or gazette
/// source was retrievable stating that Forms 3A/5/6A/10/12A are or are not still filed on paper.</b> They are
/// therefore implemented here as <b>registers derived from our own posted payroll</b>, captioned the way the forms
/// caption themselves (Form 6A: <i>Annual Statement of Contribution</i>; Form 12A: <i>Statement of
/// Contribution</i>). Nothing in this file, and nothing the UI renders from it, asserts a filing status in either
/// direction.</para>
///
/// <para><b>No PF arithmetic is written here.</b> Every figure is read verbatim off <see cref="PfEcr.Build"/> —
/// the same projection the ECR and the challan totals come from — so Form 12A reconciles to the challan and Form
/// 6A page 1 reconciles to the Form 3A cards by construction rather than by agreement. The only computation in
/// this file is addition.</para>
/// </summary>
public static class PfStatutoryForms
{
    /// <summary>The footnote every column carries whose source the Employee master does not maintain (Father's /
    /// Husband's Name, Reason for Leaving, the voluntary higher RATE). The column is printed and ruled with the
    /// form's own heading and left blank — dropping it would misrepresent the form and inventing a value would be
    /// worse than both.</summary>
    public const string NotMaintainedNote = "Column not maintained in this book — complete by hand.";

    /// <summary>The footnote for a column that is a fact about a bank challan rather than about our books (Form
    /// 12A's remitted figures and date of remittance; Form 6A page 2's date of remittance).</summary>
    public const string ChallanFactNote =
        "Remittance figures and dates are challan facts, not book entries — complete by hand.";

    /// <summary>The PF-applicable members of <paramref name="company"/> with a valid UAN, in name order. The ECR
    /// keys the member on the UAN, so a PF-applicable employee without one cannot appear on a PF return at all —
    /// <see cref="PfEcr.Build"/> refuses such a company outright, and these forms inherit that refusal.</summary>
    private static List<Employee> PfMembers(Company company)
        => company.Employees
            .Where(e => e.PfApplicable)
            .OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(e => e.Id)
            .ToList();

    /// <summary>The identity block for <paramref name="employee"/>. The <i>Account No.</i> column is the PF
    /// account number the Employee master carries; when it is unset the column prints blank rather than borrowing
    /// the UAN, which is a different identifier.</summary>
    private static PfFormMember Identity(Employee employee) => new(
        EmployeeId: employee.Id,
        AccountNumber: (employee.PfAccountNumber ?? string.Empty).Trim(),
        Name: employee.Name,
        Uan: (employee.Uan ?? string.Empty).Trim(),
        FathersOrHusbandsName: string.Empty,   // not maintained — see NotMaintainedNote
        DateOfBirth: employee.DateOfBirth,
        Sex: (employee.Gender ?? string.Empty).Trim(),
        DateOfJoiningTheFund: employee.PfJoinDate ?? employee.DateOfJoining,
        DateOfLeavingService: employee.DateOfLeaving);

    /// <summary>The statutory rate of contribution as the forms print it, from the establishment's configured EPF
    /// rate in basis points (1200 bp ⇒ <c>"12%"</c>). Never a literal.</summary>
    public static string StatutoryRate(Company company)
    {
        var bp = company.PfConfig?.EpfRateBasisPoints ?? PfContribution.DefaultEpfRateBasisPoints;
        var percent = bp / 100m;
        return percent.ToString("0.##", CultureInfo.InvariantCulture) + "%";
    }

    /// <summary>The establishment name the forms print in <i>"Name and Address of the Factory/Establishment"</i>.</summary>
    private static string EstablishmentName(Company company)
        => string.IsNullOrWhiteSpace(company.MailingName) ? company.Name : company.MailingName;

    /// <summary>
    /// Builds one wage month's <see cref="PfEcrReturn"/> for every PF member, and indexes its member rows by UAN so
    /// a twelve-month walk can pick a single member's row out of each month. This is the ONE place the ECR engine
    /// is called; nothing else in this file recomputes PF.
    /// </summary>
    private static (PfEcrReturn Return, Dictionary<string, PfEcrMember> ByUan) MonthReturn(
        Company company, IReadOnlyList<Guid> memberIds, StatutoryMonth month)
    {
        var ecr = PfEcr.Build(company, memberIds, month.From, month.To);
        var byUan = new Dictionary<string, PfEcrMember>(StringComparer.Ordinal);
        foreach (var m in ecr.Members) byUan[m.Uan] = m;
        return (ecr, byUan);
    }

    // ============================================================================================ Form 3A

    /// <summary>
    /// PF <b>Form 3A</b> for the currency period beginning on <paramref name="currencyPeriodStart"/> (1 March …
    /// 28/29 February): one contribution card per PF member, twelve month rows each, every figure read off the
    /// ECR projection for that wage month. A member with no PF wages in a month still gets that month's row with
    /// zeroes — the card is a twelve-month card and a missing row would read as a missing month.
    /// </summary>
    public static PfForm3A BuildForm3A(Company company, DateOnly currencyPeriodStart)
    {
        ArgumentNullException.ThrowIfNull(company);

        var start = new DateOnly(currencyPeriodStart.Year, currencyPeriodStart.Month, 1);
        var end = PayrollStatutoryPeriods.CurrencyPeriodEnd(start);
        var months = PayrollStatutoryPeriods.CurrencyPeriodMonths(start);

        var members = PfMembers(company);
        var memberIds = members.Select(e => e.Id).ToList();
        var monthly = months.Select(m => MonthReturn(company, memberIds, m)).ToList();
        var rate = StatutoryRate(company);

        var cards = new List<PfForm3AMember>(members.Count);
        foreach (var employee in members)
        {
            var uan = (employee.Uan ?? string.Empty).Trim();
            var rows = new List<PfForm3AMonth>(months.Count);
            for (var i = 0; i < months.Count; i++)
            {
                monthly[i].ByUan.TryGetValue(uan, out var m);
                rows.Add(new PfForm3AMonth(
                    Month: months[i].From,
                    AmountOfWages: m?.EpfWages ?? 0,
                    WorkersEpf: m?.EmployeeShareEpf ?? 0,
                    // The Employee master carries no voluntary-higher-RATE pay head (a VPF component does not
                    // exist in this product), so this column is printed, ruled and blank. PfContributeOnHigherWages
                    // is a DIFFERENT thing — contributing on wages above the ceiling, not at a higher rate — and
                    // reporting it here would be a wrong figure under a right heading.
                    HigherRateOfVoluntaryContribution: 0,
                    EmployerEpfDifference: m?.EmployerShareEpf ?? 0,
                    PensionFundContribution: m?.EpsContribution ?? 0,
                    RefundOfAdvance: m?.RefundOfAdvances ?? 0,
                    NonContributingServiceDays: m?.NcpDays ?? 0));
            }

            cards.Add(new PfForm3AMember(
                Member: Identity(employee),
                StatutoryRateOfContribution: rate,
                Months: rows,
                TotalAmountOfWages: rows.Sum(r => r.AmountOfWages),
                TotalWorkersEpf: rows.Sum(r => r.WorkersEpf),
                TotalHigherRateOfVoluntaryContribution: rows.Sum(r => r.HigherRateOfVoluntaryContribution),
                TotalEmployerEpfDifference: rows.Sum(r => r.EmployerEpfDifference),
                TotalPensionFundContribution: rows.Sum(r => r.PensionFundContribution),
                TotalRefundOfAdvance: rows.Sum(r => r.RefundOfAdvance),
                TotalNonContributingServiceDays: rows.Sum(r => r.NonContributingServiceDays)));
        }

        return new PfForm3A(EstablishmentName(company), company.PfConfig?.EstablishmentCode, start, end, cards);
    }

    // ============================================================================================ Form 6A

    /// <summary>
    /// PF <b>Form 6A</b> for the currency period beginning on <paramref name="currencyPeriodStart"/> — the
    /// consolidated annual statement. Page 1 is each member's twelve-month totals (so it foots to that member's
    /// Form 3A card by construction: both are the same twelve <see cref="PfEcr"/> calls). Page 2 is the twelve
    /// monthly challan rows straight off <see cref="PfChallanTotals"/>.
    /// </summary>
    public static PfForm6A BuildForm6A(Company company, DateOnly currencyPeriodStart)
    {
        ArgumentNullException.ThrowIfNull(company);

        var start = new DateOnly(currencyPeriodStart.Year, currencyPeriodStart.Month, 1);
        var end = PayrollStatutoryPeriods.CurrencyPeriodEnd(start);
        var months = PayrollStatutoryPeriods.CurrencyPeriodMonths(start);

        var members = PfMembers(company);
        var memberIds = members.Select(e => e.Id).ToList();
        var monthly = months.Select(m => MonthReturn(company, memberIds, m)).ToList();

        var rows = new List<PfForm6AMemberRow>(members.Count);
        var serial = 0;
        foreach (var employee in members)
        {
            var uan = (employee.Uan ?? string.Empty).Trim();
            long wages = 0, workers = 0, difference = 0, pension = 0, refund = 0;
            foreach (var (_, byUan) in monthly)
            {
                if (!byUan.TryGetValue(uan, out var m)) continue;
                wages += m.EpfWages;
                workers += m.EmployeeShareEpf;
                difference += m.EmployerShareEpf;
                pension += m.EpsContribution;
                refund += m.RefundOfAdvances;
            }

            rows.Add(new PfForm6AMemberRow(
                SerialNumber: ++serial,
                Member: Identity(employee),
                Wages: wages,
                WorkersContribution: workers,
                EmployerEpfDifference: difference,
                PensionFundContribution: pension,
                RefundOfAdvance: refund,
                RateOfHigherVoluntaryContribution: 0));   // not maintained — see NotMaintainedNote
        }

        var remittances = new List<PfForm6ARemittanceRow>(months.Count);
        for (var i = 0; i < months.Count; i++)
        {
            var t = monthly[i].Return.Totals;
            remittances.Add(new PfForm6ARemittanceRow(
                SerialNumber: i + 1,
                Month: months[i].From,
                EpfContributionsAccount1: t.Account1,
                PensionFundContributionsAccount10: t.Account10,
                EdliContributionAccount21: t.Account21,
                AdminChargesAccount2: t.Account2,
                EdliAdminChargesAccount22: t.Account22,
                DateOfRemittance: null));                 // challan fact — see ChallanFactNote
        }

        return new PfForm6A(
            EstablishmentName: EstablishmentName(company),
            EstablishmentCode: company.PfConfig?.EstablishmentCode,
            StatutoryRateOfContribution: StatutoryRate(company),
            CurrencyPeriodFrom: start,
            CurrencyPeriodTo: end,
            Members: rows,
            Remittances: remittances,
            TotalWages: rows.Sum(r => r.Wages),
            TotalWorkersContribution: rows.Sum(r => r.WorkersContribution),
            TotalEmployerEpfDifference: rows.Sum(r => r.EmployerEpfDifference),
            TotalPensionFundContribution: rows.Sum(r => r.PensionFundContribution),
            TotalRefundOfAdvance: rows.Sum(r => r.RefundOfAdvance),
            // The count of members contributing at a voluntary HIGHER RATE. No VPF rate is maintained, so the
            // honest count is zero and the field carries NotMaintainedNote alongside it.
            MembersVoluntarilyContributingAtHigherRate: 0);
    }

    // ============================================================================================ Form 5

    /// <summary>
    /// PF <b>Form 5</b> for the wage month containing <paramref name="month"/> — the members who joined the fund
    /// during that month. A member's date of joining the fund is <see cref="Employee.PfJoinDate"/>, falling back
    /// to <see cref="Employee.DateOfJoining"/> when the fund date was never recorded separately.
    /// </summary>
    public static PfForm5 BuildForm5(Company company, DateOnly month)
    {
        ArgumentNullException.ThrowIfNull(company);
        var from = new DateOnly(month.Year, month.Month, 1);
        var to = from.AddMonths(1).AddDays(-1);

        var rows = new List<PfForm5Row>();
        var serial = 0;
        foreach (var employee in PfMembers(company))
        {
            var identity = Identity(employee);
            if (!PayrollStatutoryPeriods.FallsWithin(identity.DateOfJoiningTheFund, from, to)) continue;
            rows.Add(new PfForm5Row(
                SerialNumber: ++serial,
                Member: identity,
                // "Total period of previous service as on the date of joining the Fund" — previous service with
                // another establishment is not a fact this book holds. Printed, ruled and blank.
                TotalPeriodOfPreviousService: string.Empty,
                Remarks: string.Empty));
        }

        return new PfForm5(EstablishmentName(company), company.PfConfig?.EstablishmentCode, from, rows);
    }

    // ============================================================================================ Form 10

    /// <summary>
    /// PF <b>Form 10</b> for the wage month containing <paramref name="month"/> — the members who left service
    /// during that month, read off <see cref="Employee.DateOfLeaving"/>.
    /// </summary>
    public static PfForm10 BuildForm10(Company company, DateOnly month)
    {
        ArgumentNullException.ThrowIfNull(company);
        var from = new DateOnly(month.Year, month.Month, 1);
        var to = from.AddMonths(1).AddDays(-1);

        var rows = new List<PfForm10Row>();
        var serial = 0;
        foreach (var employee in PfMembers(company))
        {
            if (!PayrollStatutoryPeriods.FallsWithin(employee.DateOfLeaving, from, to)) continue;
            rows.Add(new PfForm10Row(
                SerialNumber: ++serial,
                Member: Identity(employee),
                ReasonForLeaving: string.Empty,   // not maintained — see NotMaintainedNote
                Remarks: string.Empty));
        }

        return new PfForm10(EstablishmentName(company), company.PfConfig?.EstablishmentCode, from, rows);
    }

    // ============================================================================================ Form 12A

    /// <summary>
    /// PF <b>Form 12A</b> for the wage month containing <paramref name="month"/> — the monthly statement of
    /// contribution. Every due figure is the same <see cref="PfEcr"/> month the ECR and the challan come from, so
    /// <c>Recovered + PayableByEmployer(A/c 1)</c> equals the challan's A/c 1 by construction.
    /// </summary>
    public static PfForm12A BuildForm12A(Company company, DateOnly month)
    {
        ArgumentNullException.ThrowIfNull(company);
        var from = new DateOnly(month.Year, month.Month, 1);
        var to = from.AddMonths(1).AddDays(-1);

        var members = PfMembers(company);
        var ecr = PfEcr.Build(company, members.Select(e => e.Id).ToList(), from, to);

        long wages = 0, employeeShare = 0, employerEpf = 0;
        var subscribers = 0;
        foreach (var m in ecr.Members)
        {
            wages += m.EpfWages;
            employeeShare += m.EmployeeShareEpf;
            employerEpf += m.EmployerShareEpf;
            if (m.EpfWages > 0) subscribers++;
        }

        return new PfForm12A(
            EstablishmentName: EstablishmentName(company),
            EstablishmentCode: company.PfConfig?.EstablishmentCode,
            // "Group Code" is a header field on the form with nothing corresponding in our masters — printed blank.
            GroupCode: null,
            Month: from,
            CurrencyPeriodFrom: PayrollStatutoryPeriods.CurrencyPeriodStart(from),
            WagesOnWhichContributionsArePayable: wages,
            ContributionRecoveredFromEmployeesAccount1: employeeShare,
            ContributionPayableByEmployerAccount1: employerEpf,
            ContributionPayableByEmployerAccount10: ecr.Totals.Account10,
            ContributionPayableByEmployerAccount21: ecr.Totals.Account21,
            AdministrativeChargesDueAccount2: ecr.Totals.Account2,
            AdministrativeChargesDueAccount22: ecr.Totals.Account22,
            // Remitted figures and the date of remittance describe a bank challan. They are NOT defaulted to the
            // due figures: doing that would print an assertion that the money was remitted.
            ContributionRemittedEmployeesShare: null,
            ContributionRemittedEmployersShare: null,
            AdministrativeChargesRemitted: null,
            DateOfRemittance: null,
            DetailsOfSubscribers: subscribers);
    }
}
