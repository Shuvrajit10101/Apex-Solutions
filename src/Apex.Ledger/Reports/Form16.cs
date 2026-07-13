using Apex.Ledger.Domain;

namespace Apex.Ledger.Reports;

/// <summary>
/// One <b>Part A</b> quarter row of a <see cref="Form16"/> — the §192 TDS deducted (and, once challans are modelled,
/// deposited) for the employee in a quarter, sourced from that quarter's Form 24Q Annexure I (Phase 8 slice 7).
/// </summary>
public sealed record Form16QuarterRow(int Quarter, Money TdsDeducted)
{
    /// <summary>The quarter label ("Q1".."Q4").</summary>
    public string QuarterLabel => $"Q{Quarter}";
}

/// <summary>
/// <b>Form 16</b> — the annual salary-TDS certificate a deductor issues an employee (Phase 8 slice 7; RQ-13) — the
/// <b>salary sibling</b> of the Phase-7 <see cref="Form16A"/> certificate. <b>Part A</b> is the quarter-wise TDS
/// summary (from the four Form 24Q Annexure-I filings); <b>Part B</b> is the salary / deduction / tax computation
/// (the employee's Form 24Q Annexure-II row). A pure, read-only projection over the posted §192 lines: the Part A
/// quarter totals sum to the Part B <see cref="Form24QAnnexureIIRow.TaxDeducted"/>, and — for a fully-trued-up year
/// — that equals the Part B <see cref="Form24QAnnexureIIRow.TotalTax"/>, so the certificate reconciles to Form 24Q
/// and to the payroll postings by construction. Generated offline (no TRACES upload), mirroring Form 16A.
/// </summary>
public sealed record Form16(
    int FinancialYearStartYear,
    Guid EmployeeId,
    string EmployeeName,
    string? EmployeePan,
    Form24QDeductor Deductor,
    IReadOnlyList<Form16QuarterRow> PartA,
    Form24QAnnexureIIRow? PartB)
{
    /// <summary>The financial-year label (e.g. "2025-26").</summary>
    public string FinancialYearLabel => $"{FinancialYearStartYear}-{(FinancialYearStartYear + 1) % 100:00}";

    /// <summary>The assessment-year label (e.g. "2026-27").</summary>
    public string AssessmentYearLabel => $"{FinancialYearStartYear + 1}-{(FinancialYearStartYear + 2) % 100:00}";

    /// <summary>Σ TDS across the four Part A quarters (must equal <see cref="Form24QAnnexureIIRow.TaxDeducted"/>).</summary>
    public Money TotalTdsDeducted => new(PartA.Sum(q => q.TdsDeducted.Amount));

    /// <summary>Builds Form 16 for <paramref name="employeeId"/> for the FY starting <paramref name="fyStartYear"/>:
    /// Part A quarter totals from the four Form 24Q Annexure-I filings, Part B from the Q4 Annexure-II row. When
    /// <paramref name="sectionCode"/> is null the salary section code is derived from the deductor category
    /// (<see cref="Form24Q.SalarySectionCodeFor"/> — 92A Government / 92B private; F6).</summary>
    public static Form16 Build(Company company, Guid employeeId, int fyStartYear, string? sectionCode = null)
    {
        ArgumentNullException.ThrowIfNull(company);
        var employee = company.FindEmployee(employeeId)
            ?? throw new InvalidOperationException($"Employee {employeeId} not found.");

        var partA = new List<Form16QuarterRow>(4);
        Form24QDeductor? deductor = null;
        for (var q = 1; q <= 4; q++)
        {
            var f24 = Form24Q.Build(company, fyStartYear, q, sectionCode);
            deductor ??= f24.Deductor;
            partA.Add(new Form16QuarterRow(q, f24.TdsForEmployee(employeeId)));
        }

        var partB = Form24Q.BuildAnnexureII(company, fyStartYear)
            .FirstOrDefault(r => r.EmployeeId == employeeId);

        return new Form16(
            fyStartYear, employeeId, employee.Name, employee.Pan,
            deductor ?? new Form24QDeductor(string.Empty, DeductorType.Company, null, null, null, null),
            partA, partB);
    }
}
