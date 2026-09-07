using Apex.Ledger.Domain;
using Apex.Ledger.Services;

namespace Apex.Ledger.Reports;

/// <summary>One printed line of an <see cref="IncomeTaxComputationReport"/>.</summary>
/// <param name="Caption">The line's caption, as the report prints it.</param>
/// <param name="Amount">The figure; <c>null</c> for a caption-only band heading.</param>
/// <param name="IsDrillable">The vendor prints a component that has a breakdown behind it in <b>italics</b> and
/// opens that breakdown on Enter. This flag marks those lines, and <see cref="IncomeTaxComputationDetail"/>
/// carries what the drill shows.</param>
/// <param name="IsSubtotal">A ruled subtotal / total line.</param>
/// <param name="IsDeduction">A "Less: …" line, so a caller can render it as a subtraction.</param>
public sealed record IncomeTaxComputationLine(
    string Caption,
    Money? Amount,
    bool IsDrillable = false,
    bool IsSubtotal = false,
    bool IsDeduction = false);

/// <summary>The breakdown behind one drillable (italic) line — the vendor's <i>"detailed computation for the
/// selected income tax component"</i>.</summary>
public sealed record IncomeTaxComputationDetail(
    string Caption,
    IReadOnlyList<IncomeTaxComputationLine> Lines);

/// <summary>
/// The <b>Income Tax Computation</b> report (census row 7.26) — the vendor's per-employee computation
/// <i>"in the Form 16 format"</i>, showing <i>"the total tax payable, … the tax already paid, the balance tax
/// payable, and the tax to be deducted in subsequent months"</i>
/// (help.tallysolutions.com/tally-prime/payroll-income-tax-reports/tax-computation-tally/).
///
/// <para>🔴 <b>THE ARITHMETIC ALREADY EXISTED; ONLY THE REPORT WAS MISSING — SO THIS FILE COMPUTES NO TAX.</b>
/// Every figure below is taken from <see cref="Form24Q.BuildAnnexureII"/>, which is the SAME call that backs Form
/// 24Q Annexure II and Form 16 Part B and which runs <see cref="SalaryIncomeTax.ComputeAnnual"/>. Writing a second
/// computation here would produce a report that silently drifts from the certificate the employee is handed. If a
/// figure on this report looks wrong, it is wrong on Form 16 too, and it is wrong in one place.</para>
///
/// <para>⚠️ <b>TWO OPEN DEFECTS ARE SURFACED BY THIS REPORT AND ARE NOT DEEPENED BY IT.</b> (1) The §192 engine is
/// <b>date-blind</b>: its slab tables are bare constants with no effective-from, so this report states, on its own
/// face (<see cref="RateVintageNote"/>), which year's tables produced the figures rather than implying they were
/// selected for the year shown. (2) The 4% health-and-education cess this product applies is an open user
/// decision; the cess is shown as its own line rather than folded into a total, so it is never hidden inside a
/// figure. Nothing here adds a rate, a threshold or a citation of its own.</para>
///
/// <para>A pure, deterministic, culture-invariant projection — no clock, no RNG.</para>
/// </summary>
public sealed record IncomeTaxComputationReport(
    int FinancialYearStartYear,
    Guid EmployeeId,
    string EmployeeName,
    string? Pan,
    TaxRegime Regime,
    IReadOnlyList<IncomeTaxComputationLine> Lines,
    IReadOnlyList<IncomeTaxComputationDetail> Details,
    Money TotalTaxPayable,
    Money TaxDeductedSoFar,
    Money BalanceTaxPayable)
{
    /// <summary>The vintage of the rate tables that produced these figures, printed on the report. See the type
    /// doc — the engine takes no date, so the year is stated rather than implied.</summary>
    public const string RateVintageNote =
        "Computed with the slab, surcharge and cess tables this book encodes for the financial year 2025-26 "
        + "(assessment year 2026-27). Those tables carry no effective-from date and are not selected by the "
        + "period shown, so verify them before relying on this report for another year.";

    /// <summary>The financial-year label (e.g. "2025-26").</summary>
    public string FinancialYearLabel => $"{FinancialYearStartYear}-{(FinancialYearStartYear + 1) % 100:00}";

    /// <summary>The regime as the report captions it.</summary>
    public string RegimeLabel => Regime == TaxRegime.New ? "New regime (u/s 115BAC)" : "Old regime";

    /// <summary>
    /// Builds the computation for <paramref name="employeeId"/> for the financial year starting
    /// <paramref name="fyStartYear"/>-04-01, or <c>null</c> when that employee has no salary and no §192 deduction
    /// posted in the year — the report has nothing to compute, and printing a page of zeros for someone who was
    /// never paid would read as a computation rather than as an absence.
    /// </summary>
    public static IncomeTaxComputationReport? Build(Company company, Guid employeeId, int fyStartYear)
    {
        ArgumentNullException.ThrowIfNull(company);

        var row = Form24Q.BuildAnnexureII(company, fyStartYear).FirstOrDefault(r => r.EmployeeId == employeeId);
        if (row is null) return null;

        var incomeTaxAndSurcharge = row.IncomeTax + row.Surcharge;

        var lines = new List<IncomeTaxComputationLine>
        {
            new("Gross Salary", row.GrossSalary, IsDrillable: true),
            new("Less: Standard Deduction u/s 16(ia)", row.StandardDeduction, IsDeduction: true),
            new("Less: Deductions under Chapter VI-A", row.ChapterViaDeductions, IsDrillable: true, IsDeduction: true),
            new("Total Taxable Income", row.TaxableIncome, IsSubtotal: true),
            new("Income Tax on Total Income", row.IncomeTax, IsDrillable: true),
            new("Surcharge", row.Surcharge),
            new("Health and Education Cess", row.Cess),
            new("Total Tax Payable", row.TotalTax, IsSubtotal: true),
            new("Less: Tax Deducted so far", row.TaxDeducted, IsDeduction: true),
            new("Balance Tax Payable", row.TotalTax - row.TaxDeducted, IsSubtotal: true),
        };

        var details = new List<IncomeTaxComputationDetail>
        {
            new("Gross Salary", new List<IncomeTaxComputationLine>
            {
                new("Taxable salary paid and posted in the year", row.GrossSalary),
                new("Total", row.GrossSalary, IsSubtotal: true),
            }),
            // 🔴 The caption matches its line VERBATIM, including the "Less: " prefix. The drill is looked up by
            // caption, so a detail titled "Deductions under Chapter VI-A" beside a line reading "Less: Deductions
            // under Chapter VI-A" is an italic line that opens nothing.
            new("Less: Deductions under Chapter VI-A", new List<IncomeTaxComputationLine>
            {
                new("Deductions allowed under the applicable regime", row.ChapterViaDeductions),
                new("Total", row.ChapterViaDeductions, IsSubtotal: true),
            }),
            new("Income Tax on Total Income", new List<IncomeTaxComputationLine>
            {
                new("Income tax after rebate u/s 87A", row.IncomeTax),
                new("Surcharge", row.Surcharge),
                new("Income tax and surcharge", incomeTaxAndSurcharge, IsSubtotal: true),
                new("Health and Education Cess", row.Cess),
                new("Total", row.TotalTax, IsSubtotal: true),
            }),
        };

        return new IncomeTaxComputationReport(
            fyStartYear,
            row.EmployeeId,
            row.EmployeeName,
            row.Pan,
            row.Regime,
            lines,
            details,
            row.TotalTax,
            row.TaxDeducted,
            row.TotalTax - row.TaxDeducted);
    }

    /// <summary>The financial year (its April start year) that <paramref name="date"/> falls in — April to March.
    /// Used to scope the report to the year the chosen wage month belongs to.</summary>
    public static int FinancialYearStartYearFor(DateOnly date) => date.Month >= 4 ? date.Year : date.Year - 1;
}
