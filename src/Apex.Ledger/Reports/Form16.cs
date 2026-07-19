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
/// <b>Form 16 (2025 Act: Form 130)</b> — the annual salary-TDS certificate a deductor issues an employee
/// (Phase 8 slice 7; RQ-13) — the
/// <b>salary sibling</b> of the Phase-7 <see cref="Form16A"/> certificate. <b>Part A</b> is the quarter-wise TDS
/// summary (from the four Form 24Q Annexure-I filings); <b>Part B</b> is the salary / deduction / tax computation
/// (the employee's Form 24Q Annexure-II row). A pure, read-only projection over the posted §192 lines: the Part A
/// quarter totals sum to the Part B <see cref="Form24QAnnexureIIRow.TaxDeducted"/>, and — for a fully-trued-up year
/// — that equals the Part B <see cref="Form24QAnnexureIIRow.TotalTax"/>, so the certificate reconciles to Form 24Q
/// and to the payroll postings by construction. Generated offline (no TRACES upload), mirroring Form 16A.
///
/// <para><b>Statute vocabulary (CA S9).</b> From <b>FY 2026-27</b> the Income-tax Act 2025 renames this certificate
/// <b>Form 130</b>, renumbers the salary section <b>§192→§392</b> and the certificate section <b>§203→§395</b>, and
/// replaces the "assessment year" with the <b>tax year</b> — which <b>is</b> the financial year, so the displayed
/// period value moves, not merely its caption. All of that is presentation: this projection's figures are unchanged
/// and prior-year certificates reprint byte-identically (ER-13). See <see cref="StatuteVocabulary"/>.</para>
///
/// <para>Two substantive 2025-Act differences are recorded here as <b>documentation only</b> — neither is implemented,
/// because implementing them was out of scope for S9: Form 130 has Parts A, B <b>and C</b> (Rule 215(2)) where Form 16
/// had only Parts A and B; and Rule 215(1) requires the certificate to be generated/downloaded from the departmental
/// portal rather than self-generated as it is here.</para>
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
    /// <remarks><b>Kept verbatim (CA S9, ER-13).</b> The Income-tax Act 2025 retires the "assessment year" concept
    /// for FY 2026-27 onward, but FY 2025-26 and earlier certificates are filed documents that must reprint
    /// byte-identically. New callers should prefer <see cref="PeriodCaption"/> + <see cref="PeriodLabel"/>, which
    /// select the right vocabulary for the year; this property is the 1961-Act value regardless of year.</remarks>
    public string AssessmentYearLabel => $"{FinancialYearStartYear + 1}-{(FinancialYearStartYear + 2) % 100:00}";

    /// <summary>The period caption for this certificate's year — "Assessment Year" (1961 Act) or "Tax Year"
    /// (2025 Act, FY 2026-27 onward). See <see cref="StatuteVocabulary"/>.</summary>
    public string PeriodCaption => StatuteVocabulary.PeriodCaption(FinancialYearStartYear);

    /// <summary>The period <b>value</b> for this certificate's year. Under the 2025 Act the tax year <b>is</b> the
    /// financial year, so this differs from <see cref="AssessmentYearLabel"/> from FY 2026-27 onward — it is a value
    /// change, not merely a caption change. Always render it with <see cref="PeriodCaption"/>: "AY 2026-27" and
    /// "Tax Year 2026-27" are different years that collide numerically.</summary>
    public string PeriodLabel => StatuteVocabulary.PeriodLabel(FinancialYearStartYear);

    /// <summary>The abbreviated period caption ("AY" / "Tax Year") for tight report subtitles.</summary>
    public string PeriodCaptionShort => StatuteVocabulary.PeriodCaptionShort(FinancialYearStartYear);

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
