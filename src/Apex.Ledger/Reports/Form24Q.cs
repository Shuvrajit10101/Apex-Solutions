using Apex.Ledger.Domain;
using Apex.Ledger.Services;

namespace Apex.Ledger.Reports;

/// <summary>
/// The deductor (filer) block of a <see cref="Form24Q"/> return (Phase 8 slice 7) — the TAN, deductor legal type
/// and person-responsible identity captured on F11 (<see cref="TdsConfig"/>), <b>reused</b> from the Phase-7
/// deductor config (§192 does not fork a parallel deductor). Denormalised onto the projection so the FVU writer
/// needs no further company lookup.
/// </summary>
public sealed record Form24QDeductor(
    string Tan,
    DeductorType DeductorType,
    string? ResponsiblePersonName,
    string? ResponsiblePersonPan,
    string? ResponsiblePersonDesignation,
    string? ResponsiblePersonAddress);

/// <summary>
/// One <b>Annexure I</b> deductee row of Form 24Q — a single §192 salary-TDS withholding on an employee in the
/// return quarter: the employee <see cref="Pan"/> + name, the salary <see cref="SectionCode"/> (92B private / 92A
/// govt / 92C union-govt), the <see cref="DeductionDate"/> (the payroll voucher date) and the <see cref="TdsAmount"/>
/// withheld. Read verbatim off the posted payroll <see cref="PayrollLineDetail"/> deduction line — never
/// recomputed — so the rows reconcile to the salary-TDS-payable credit postings for the quarter by construction.
/// </summary>
public sealed record Form24QDeducteeRow(
    Guid EmployeeId,
    string EmployeeName,
    string? Pan,
    DateOnly DeductionDate,
    Money TdsAmount,
    string SectionCode);

/// <summary>
/// One <b>Annexure II</b> row of Form 24Q (filed in Q4 only) — an employee's full-year salary + tax computation:
/// the annual <see cref="GrossSalary"/>, the <see cref="StandardDeduction"/> and <see cref="ChapterViaDeductions"/>
/// (regime-allowed), the resulting <see cref="TaxableIncome"/>, the <see cref="IncomeTax"/> (slab − §87A rebate),
/// <see cref="Surcharge"/>, <see cref="Cess"/> and <see cref="TotalTax"/>, the <see cref="TaxDeducted"/> (Σ the
/// year's posted §192 deductions) and the elected <see cref="Regime"/> flag. Drives <b>Form 16 Part B</b>. The
/// gross salary + tax deducted are read off the posted payroll lines; the tax figures are recomputed by the §192
/// engine over the actual annual income so a fully-trued-up year has <see cref="TaxDeducted"/> == <see cref="TotalTax"/>.
/// </summary>
public sealed record Form24QAnnexureIIRow(
    Guid EmployeeId,
    string EmployeeName,
    string? Pan,
    TaxRegime Regime,
    Money GrossSalary,
    Money StandardDeduction,
    Money ChapterViaDeductions,
    Money TaxableIncome,
    Money IncomeTax,
    Money Surcharge,
    Money Cess,
    Money TotalTax,
    Money TaxDeducted);

/// <summary>
/// Form-27A-style <b>control totals</b> for a Form 24Q return (Phase 8 slice 7; F4) — the record counts and money
/// totals a filer cross-checks before generating the FVU upload file, the salary sibling of
/// <see cref="Form26QControlTotals"/>. Salary-TDS has no Phase-7 challan/deposit block yet (that integration is a
/// documented carry-forward), so the totals describe Annexure I (the deductee-wise §192 withholdings) and — in Q4 —
/// Annexure II (the annual computation). <see cref="Validate"/> emulates the FVU control-total checks (no total may be
/// negative) and returns a human-readable warning list — empty when the return tallies.
/// </summary>
/// <param name="DeducteeRecordCount">Count of Annexure I deductee rows in the quarter.</param>
/// <param name="TotalTdsDeducted">Σ §192 TDS withheld across the Annexure I rows (the salary-TDS-payable credit).</param>
/// <param name="AnnexureIIRecordCount">Count of Annexure II annual-computation rows (Q4 only; 0 in Q1–Q3).</param>
/// <param name="AnnexureIITaxDeducted">Σ TDS the Annexure II rows report deducted for the year (Q4 only).</param>
public sealed record Form24QControlTotals(
    int DeducteeRecordCount,
    Money TotalTdsDeducted,
    int AnnexureIIRecordCount,
    Money AnnexureIITaxDeducted)
{
    /// <summary>Emulates the FVU control-total cross-checks and returns the mismatch messages (empty ⇒ the return
    /// tallies): no money total may be negative.</summary>
    public IReadOnlyList<string> Validate()
    {
        var problems = new List<string>();
        if (TotalTdsDeducted.Amount < 0m)
            problems.Add($"Total §192 TDS deducted is negative ({TotalTdsDeducted}).");
        if (AnnexureIITaxDeducted.Amount < 0m)
            problems.Add($"Annexure II tax deducted is negative ({AnnexureIITaxDeducted}).");
        return problems;
    }

    /// <summary>True iff the control totals tally (no warnings/errors).</summary>
    public bool Tallies => Validate().Count == 0;
}

/// <summary>
/// <b>Form 24Q</b> — the quarterly salary-TDS return u/s 192 (Phase 8 slice 7; RQ-13; catalog §13) as a pure,
/// read-only projection over the posted payroll §192 deduction lines. The <b>salary sibling</b> of
/// <see cref="Form26Q"/> (same FVU family): <b>Annexure I</b> (deductee + TDS) is filed every quarter; <b>Annexure
/// II</b> (per-employee annual salary + tax computation) is filed in <b>Q4 only</b> and drives <b>Form 16 Part B</b>.
/// It reads the withheld tax off each posted deduction line — never recomputing the withholding — so the return
/// reconciles to the salary-TDS-payable credit postings for the quarter by construction. A company that never
/// deducts salary-TDS yields an empty return (ER-13). Deterministic (no clock/RNG); ordered for byte-stability.
/// </summary>
/// <remarks>
/// <b>CARRY-FORWARD (Phase 8 slice 7, F4b) — salary-TDS deposit-path integration.</b> The §192 salary-TDS is
/// credited to its own auto-created payable ledger (e.g. "TDS on Salary" under Current Liabilities), <b>separate</b>
/// from the Phase-7 "TDS Payable" (Duties &amp; Taxes) ledger that <see cref="TdsDepositService"/> /
/// <see cref="ChallanReconciliation"/> / <see cref="Form26Q"/> operate on. Routing salary-TDS onto that shared ledger
/// was assessed and <b>deliberately deferred</b>: the Phase-7 challan/FIFO/26Q machinery keys on
/// <see cref="TdsLineTax"/> (section 194x) details, so wiring salary-TDS (which carries
/// <see cref="PayrollLineDetail"/>, not <see cref="TdsLineTax"/>) into it would either pollute Form 26Q with §192
/// rows or require invasive changes to the Phase-7 collectors — risking the 26Q/challan reconciliation. Consequently
/// Form 24Q is a complete, self-consistent <b>return</b> (Annexure I + Annexure II + the FVU flat file with control
/// totals), but salary-TDS is <b>not yet depositable via the Phase-7 stat-payment/challan path</b> (so the 24Q FVU
/// file carries no CD challan block). Integrating salary-TDS deposits + challan reconciliation into Form 24Q is a
/// later-slice / S10-gate item.
/// </remarks>
public sealed record Form24Q(
    int FinancialYearStartYear,
    int Quarter,
    DateOnly From,
    DateOnly To,
    Form24QDeductor Deductor,
    IReadOnlyList<Form24QDeducteeRow> Deductees,
    IReadOnlyList<Form24QAnnexureIIRow> AnnexureII)
{
    /// <summary>The default salary section code — 92B (a private, non-government employer).</summary>
    public const string SalarySectionCode = "92B";

    /// <summary>The Government-employer salary section code — 92A.</summary>
    public const string GovernmentSalarySectionCode = "92A";

    /// <summary>The union (Central) Government salary section code — 92C (a manual override; no distinct deductor type).</summary>
    public const string UnionGovernmentSalarySectionCode = "92C";

    /// <summary>The default salary section code for the company's <b>deductor category</b> (Phase 8 slice 7; F6):
    /// <see cref="GovernmentSalarySectionCode"/> (92A) for a <see cref="DeductorType.Government"/> deductor, else the
    /// private <see cref="SalarySectionCode"/> (92B). The union-government 92C variant has no distinct deductor-type
    /// value, so it stays a manual override. Reuses the Phase-7 <see cref="TdsConfig.DeductorType"/> — §192 does not
    /// fork a parallel deductor.</summary>
    public static string SalarySectionCodeFor(Company company)
    {
        ArgumentNullException.ThrowIfNull(company);
        return company.Tds?.DeductorType == DeductorType.Government
            ? GovernmentSalarySectionCode
            : SalarySectionCode;
    }

    /// <summary>The financial-year label (e.g. "2025-26").</summary>
    public string FinancialYearLabel => $"{FinancialYearStartYear}-{(FinancialYearStartYear + 1) % 100:00}";

    /// <summary>The quarter label ("Q1".."Q4").</summary>
    public string QuarterLabel => $"Q{Quarter}";

    /// <summary>Σ §192 TDS withheld across the Annexure I rows (the salary-TDS-payable credit for the quarter).</summary>
    public Money TotalTdsDeducted => new(Deductees.Sum(d => d.TdsAmount.Amount));

    /// <summary>Σ TDS the year's Annexure II rows report deducted (Q4 only; empty in Q1–Q3).</summary>
    public Money AnnexureIITaxDeducted => new(AnnexureII.Sum(r => r.TaxDeducted.Amount));

    /// <summary>The Form-27A-style control totals for the return (Phase 8 slice 7; F4) — Annexure I record count +
    /// Σ TDS, and the Q4 Annexure II count + tax deducted; the FVU writer's file trailer is derived from these.</summary>
    public Form24QControlTotals ControlTotals =>
        new(Deductees.Count, TotalTdsDeducted, AnnexureII.Count, AnnexureIITaxDeducted);

    /// <summary>The §192 TDS withheld from <paramref name="employeeId"/> in the quarter (Annexure I).</summary>
    public Money TdsForEmployee(Guid employeeId) =>
        new(Deductees.Where(d => d.EmployeeId == employeeId).Sum(d => d.TdsAmount.Amount));

    /// <summary>True iff there is nothing to file this quarter (no deduction).</summary>
    public bool IsEmpty => Deductees.Count == 0;

    /// <summary>The inclusive window of quarter <paramref name="quarter"/> (1..4) — identical to Form 26Q's.</summary>
    public static (DateOnly From, DateOnly To) QuarterWindow(int fyStartYear, int quarter) =>
        Form26Q.QuarterWindow(fyStartYear, quarter);

    /// <summary>Builds Form 24Q for <paramref name="company"/> for quarter <paramref name="quarter"/> (1..4) of the
    /// financial year starting <paramref name="fyStartYear"/>-04-01. Annexure II is populated only in Q4. When
    /// <paramref name="sectionCode"/> is null the salary section code is derived from the deductor category
    /// (<see cref="SalarySectionCodeFor"/> — 92A Government / 92B private; F6).</summary>
    public static Form24Q Build(Company company, int fyStartYear, int quarter, string? sectionCode = null)
    {
        ArgumentNullException.ThrowIfNull(company);
        var section = sectionCode ?? SalarySectionCodeFor(company);
        var (from, to) = QuarterWindow(fyStartYear, quarter);
        var deductor = BuildDeductor(company);

        var deductees = CollectDeductions(company, from, to, section);
        var annexureII = quarter == 4
            ? BuildAnnexureII(company, fyStartYear)
            : Array.Empty<Form24QAnnexureIIRow>();

        return new Form24Q(fyStartYear, quarter, from, to, deductor, deductees, annexureII);
    }

    /// <summary>The Annexure II per-employee annual salary + tax rows for the FY starting
    /// <paramref name="fyStartYear"/> — the Q4-only annual computation that drives Form 16 Part B. Public so Form 16
    /// reads exactly the same rows.</summary>
    public static IReadOnlyList<Form24QAnnexureIIRow> BuildAnnexureII(Company company, int fyStartYear)
    {
        ArgumentNullException.ThrowIfNull(company);
        var fyStart = new DateOnly(fyStartYear, 4, 1);
        var fyEnd = new DateOnly(fyStartYear + 1, 3, 31);

        var rows = new List<Form24QAnnexureIIRow>();
        foreach (var employee in company.Employees.OrderBy(e => e.Name, StringComparer.Ordinal))
        {
            var grossSalary = SumPostedTaxableGross(company, employee.Id, fyStart, fyEnd);
            var taxDeducted = SumPostedSalaryTds(company, employee.Id, fyStart, fyEnd);
            if (grossSalary.Amount <= 0m && taxDeducted.Amount <= 0m) continue; // not paid this FY

            var regime = employee.ApplicableTaxRegime;
            var declaration = company.FindTaxDeclaration(employee.Id);
            var additionalIncome = declaration?.AdditionalIncome.Amount ?? 0m;
            var allowed = declaration?.AllowedDeductions(regime) ?? Money.Zero;
            var estAnnual = grossSalary.Amount + additionalIncome;
            var taxable = SalaryIncomeTax.TaxableIncome(estAnnual, allowed.Amount, regime);
            var age = SalaryIncomeTax.AgeBandFor(employee.DateOfBirth, fyEnd);
            var c = SalaryIncomeTax.ComputeAnnual(taxable, regime, age);
            // Mirror the §192 engine's §206AA no-PAN branch (F3): a deductee with no valid PAN was withheld at the
            // higher of the average rate or the 20% floor, so the certificate's Total Tax must report that floor —
            // otherwise Annexure II / Form 16 Part B can never reconcile to the Σ-posted TDS for a no-PAN employee.
            var totalTax = Pan.IsValid(employee.Pan)
                ? c.AnnualTax
                : SalaryIncomeTax.AnnualTaxNoPan(taxable, regime, age);

            rows.Add(new Form24QAnnexureIIRow(
                employee.Id, employee.Name, employee.Pan, regime,
                new Money(estAnnual),
                new Money(SalaryIncomeTax.StandardDeduction(regime)),
                allowed,
                new Money(taxable),
                new Money(c.IncomeTaxAfterRebate),
                new Money(c.Surcharge),
                new Money(c.Cess),
                totalTax,
                taxDeducted));
        }
        return rows;
    }

    private static Form24QDeductor BuildDeductor(Company company)
    {
        var cfg = company.Tds;
        if (cfg is null || !cfg.Enabled)
            return new Form24QDeductor(string.Empty, DeductorType.Company, null, null, null, null);
        return new Form24QDeductor(
            cfg.Tan ?? string.Empty, cfg.DeductorType, cfg.ResponsiblePersonName, cfg.ResponsiblePersonPan,
            cfg.ResponsiblePersonDesignation, cfg.ResponsiblePersonAddress);
    }

    /// <summary>Every posted, non-cancelled §192 deduction line dated in <c>[from, to]</c>, one Annexure I row each,
    /// ordered by (date, employee) for byte-stability.</summary>
    private static List<Form24QDeducteeRow> CollectDeductions(Company company, DateOnly from, DateOnly to, string sectionCode)
    {
        var rows = new List<(DateOnly Date, string Name, Form24QDeducteeRow Row)>();
        foreach (var v in company.Vouchers)
        {
            if (v.Cancelled) continue;
            if (v.Date < from || v.Date > to) continue;
            foreach (var line in v.Lines)
            {
                if (line.Payroll is not { } pd) continue;
                if (pd.Category != PayrollLineCategory.Deduction) continue;
                if (pd.PayHeadId is not { } phId) continue;
                var ph = company.FindPayHead(phId);
                if (ph is null || ph.IncomeTaxComponent != IncomeTaxComponent.TaxDeductedAtSource) continue;
                var employee = company.FindEmployee(pd.EmployeeId);
                var name = employee?.Name ?? "(unknown)";
                rows.Add((v.Date, name, new Form24QDeducteeRow(
                    pd.EmployeeId, name, employee?.Pan, v.Date, pd.Amount, sectionCode)));
            }
        }
        return rows.OrderBy(r => r.Date).ThenBy(r => r.Name, StringComparer.Ordinal).Select(r => r.Row).ToList();
    }

    /// <summary>Σ posted taxable gross earnings for an employee across the FY — the earning lines whose pay head is
    /// not tagged <see cref="IncomeTaxComponent.FullyExempt"/> (the same base the §192 engine annualised).</summary>
    private static Money SumPostedTaxableGross(Company company, Guid employeeId, DateOnly fyStart, DateOnly fyEnd)
    {
        decimal sum = 0m;
        foreach (var v in company.Vouchers)
        {
            if (v.Cancelled || v.Date < fyStart || v.Date > fyEnd) continue;
            foreach (var line in v.Lines)
            {
                if (line.Payroll is not { } pd) continue;
                if (pd.EmployeeId != employeeId) continue;
                if (pd.Category != PayrollLineCategory.Earning) continue;
                if (pd.PayHeadId is not { } phId) continue;
                var ph = company.FindPayHead(phId);
                if (ph is null || ph.IncomeTaxComponent == IncomeTaxComponent.FullyExempt) continue;
                sum += pd.Amount.Amount;
            }
        }
        return new Money(sum);
    }

    /// <summary>Σ posted §192 salary-TDS deductions for an employee across the FY (the tax deducted to date).</summary>
    private static Money SumPostedSalaryTds(Company company, Guid employeeId, DateOnly fyStart, DateOnly fyEnd)
    {
        decimal sum = 0m;
        foreach (var v in company.Vouchers)
        {
            if (v.Cancelled || v.Date < fyStart || v.Date > fyEnd) continue;
            foreach (var line in v.Lines)
            {
                if (line.Payroll is not { } pd) continue;
                if (pd.EmployeeId != employeeId) continue;
                if (pd.Category != PayrollLineCategory.Deduction) continue;
                if (pd.PayHeadId is not { } phId) continue;
                var ph = company.FindPayHead(phId);
                if (ph is null || ph.IncomeTaxComponent != IncomeTaxComponent.TaxDeductedAtSource) continue;
                sum += pd.Amount.Amount;
            }
        }
        return new Money(sum);
    }
}
