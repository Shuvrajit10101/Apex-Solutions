using Apex.Ledger.Domain;

namespace Apex.Ledger.Services;

/// <summary>
/// The <b>§192 (2025 Act: §392) salary-TDS income-tax engine</b> (Phase 8 slice 7; RQ-12; §115BAC(1A) / §87A
/// (2025 Act: §156)) — a <b>pure, deterministic</b>, framework-/DB-/clock-/RNG-free calculator for the annual
/// income-tax on an employee's estimated salary and the average-rate monthly §192 withholding. It is dedicated logic
/// (not the generic As-Computed-Value slabs) because the statutory computation cannot be expressed as ordinary
/// pay-head slabs:
/// <list type="bullet">
///   <item>two regimes with different slab tables, standard deductions and rebate rules
///     (<see cref="TaxRegime.New"/> default u/s 115BAC; <see cref="TaxRegime.Old"/> with Chapter VI-A);</item>
///   <item>a <b>§87A rebate</b> that is a <b>marginal-relief band</b> in the new regime but a <b>hard cliff</b> in
///     the old regime;</item>
///   <item><b>surcharge</b> with its own marginal relief at each threshold (≥₹50L), new-regime-capped at 25%;</item>
///   <item>a <b>health-and-education cess</b> applied <b>last</b>;</item>
///   <item>the <b>§192 average-rate mechanic</b> — annual tax spread over the months remaining in the FY, trued-up
///     as estimates change, never negative.</item>
/// </list>
/// Money rounds to the <b>nearest rupee, half-up</b> (the Phase-7 income-tax convention, <see cref="TdsService.NearestRupee"/>).
///
/// <para>🔴 <b>EVERY RATE AND THRESHOLD NOW ARRIVES AS A DATED TABLE — THIS IS THE FIX FOR DEFECT T1-26.</b> None of
/// the figures above live in this class any more. They live in <see cref="SalaryTaxRates"/>, keyed by the financial
/// year of the payroll period, and every money-producing method on this class takes one as a <b>required</b>
/// parameter with <b>no default</b>. That absence of a default is deliberate and is the actual repair: the defect was
/// precisely that the engine could compute a year's tax without ever being told which year it was, so re-admitting a
/// compile-time fallback would re-admit the bug. The caller must resolve the table from the period it is computing —
/// <see cref="SalaryTaxRates.ForPeriod"/> — and every caller in <c>src/</c> already had that date in hand.</para>
///
/// <para><b>Statute vocabulary (CA S9).</b> The Income-tax Act 2025 renumbers the sections cited here — §192→§392,
/// §87A→§156 — from <b>FY 2026-27</b>, when the 1961 Act stands repealed. Those are <b>display</b> changes only and
/// are resolved by <see cref="Domain.StatuteVocabulary"/>; <b>this engine computes nothing differently</b>. Sections
/// that were <b>not</b> verified against a primary source — notably §115BAC and the Chapter VI-A deductions — are
/// deliberately left <b>un-renumbered and un-re-cited</b> throughout.</para>
/// </summary>
public static class SalaryIncomeTax
{
    /// <summary>
    /// The <b>§206AA no-PAN floor rate</b> — 20% of taxable income (higher of average rate or 20%).
    /// </summary>
    /// <remarks>
    /// ⚠️ <b>This one is NOT dated, and that is a claim, so here is the reason.</b> §206AA fixes the floor in the
    /// Income-tax Act itself ("twenty per cent") rather than in a Finance Act's First Schedule, so unlike the slabs,
    /// the standard deduction, the §87A rebate and the cess it is not a figure a budget re-states each year. It is
    /// therefore left as a constant rather than folded into <see cref="SalaryTaxRates"/>. If a future amendment moves
    /// it, it must be dated the same way the rest of the table already is.
    /// </remarks>
    public const decimal NoPanFloorRate = 0.20m;

    /// <summary>The old-regime senior-citizen age band (only the first nil band shifts): below 60, 60–&lt;80, 80+.</summary>
    public enum AgeBand
    {
        /// <summary>Below 60.</summary>
        Below60 = 0,
        /// <summary>Senior citizen 60–&lt;80.</summary>
        Senior = 1,
        /// <summary>Super-senior 80+.</summary>
        SuperSenior = 2,
    }

    /// <summary>
    /// The old-regime senior-citizen <see cref="AgeBand"/> for an employee born on <paramref name="dateOfBirth"/>
    /// as at <paramref name="asOf"/> (the payroll date) — <see cref="AgeBand.SuperSenior"/> at 80+,
    /// <see cref="AgeBand.Senior"/> at 60–&lt;80, else <see cref="AgeBand.Below60"/>. An unknown date of birth is
    /// treated as below-60 (the safest — no extra exemption). Only the old regime uses the band; §115BAC publishes
    /// one table for every individual regardless of age.
    /// </summary>
    public static AgeBand AgeBandFor(DateOnly? dateOfBirth, DateOnly asOf)
    {
        if (dateOfBirth is not { } dob) return AgeBand.Below60;
        var age = asOf.Year - dob.Year;
        if (dob > asOf.AddYears(-age)) age--; // not yet had this year's birthday
        return age >= 80 ? AgeBand.SuperSenior : age >= 60 ? AgeBand.Senior : AgeBand.Below60;
    }

    /// <summary>The standard deduction u/s 16(ia) for the regime, <b>for the year <paramref name="rates"/> is for</b>.</summary>
    public static decimal StandardDeduction(TaxRegime regime, SalaryTaxRates rates)
    {
        ArgumentNullException.ThrowIfNull(rates);
        return rates.StandardDeduction(regime);
    }

    /// <summary>
    /// The taxable income = <paramref name="grossSalary"/> − standard deduction(regime, year) −
    /// <paramref name="allowedDeductions"/> (the regime-allowed Chapter VI-A / exemptions the caller resolved from
    /// the employee's declaration; ₹0 in the new regime beyond 80CCD(2)), floored at ₹0.
    /// </summary>
    public static decimal TaxableIncome(decimal grossSalary, decimal allowedDeductions, TaxRegime regime, SalaryTaxRates rates)
    {
        ArgumentNullException.ThrowIfNull(rates);
        return Math.Max(0m, grossSalary - rates.StandardDeduction(regime) - allowedDeductions);
    }

    // ---- slab tax (marginal) ----

    /// <summary>The marginal slab tax on <paramref name="taxableIncome"/> under the regime + age band, using the slab
    /// table <paramref name="rates"/> publishes for its year (before §87A, surcharge and cess). Each segment
    /// contributes its rate on the portion of income within its band.</summary>
    public static decimal SlabTax(decimal taxableIncome, TaxRegime regime, SalaryTaxRates rates, AgeBand age = AgeBand.Below60)
    {
        ArgumentNullException.ThrowIfNull(rates);
        if (taxableIncome <= 0m) return 0m;
        decimal tax = 0m;
        foreach (var seg in rates.Slabs(regime, age))
        {
            var top = seg.To ?? taxableIncome;
            var portion = Math.Min(taxableIncome, top) - seg.From;
            if (portion > 0m) tax += seg.RateBasisPoints / 10_000m * portion;
        }
        return tax;
    }

    // ---- §87A rebate ----

    /// <summary>
    /// The §87A rebate on <paramref name="slabTax"/> for the regime, using <paramref name="rates"/>' ceilings and
    /// caps. New regime: full rebate (capped) when taxable ≤ the year's ceiling, else a <b>marginal-relief</b>
    /// rebate = <c>max(0, slabTax − (taxable − ceiling))</c> so tax before cess never exceeds the income above the
    /// ceiling. Old regime: a <b>hard cliff</b> — the cap (or the slab tax if less) at or below the year's ceiling,
    /// else ₹0 (no marginal relief).
    /// </summary>
    public static decimal Rebate87A(decimal taxableIncome, decimal slabTax, TaxRegime regime, SalaryTaxRates rates)
    {
        ArgumentNullException.ThrowIfNull(rates);
        if (regime == TaxRegime.New)
        {
            if (taxableIncome <= rates.NewRegimeRebateTaxableCeiling)
                return Math.Min(slabTax, rates.NewRegimeRebateCap);
            var excessOverCeiling = taxableIncome - rates.NewRegimeRebateTaxableCeiling;
            return Math.Max(0m, slabTax - excessOverCeiling); // marginal relief band
        }

        // Old regime — hard cliff.
        return taxableIncome <= rates.OldRegimeRebateTaxableCeiling
            ? Math.Min(slabTax, rates.OldRegimeRebateCap)
            : 0m;
    }

    // ---- surcharge (+ marginal relief) ----

    private static decimal SurchargeRate(decimal taxableIncome, TaxRegime regime, SalaryTaxRates rates)
    {
        decimal rate = 0m;
        foreach (var band in rates.SurchargeBands(regime))
            if (taxableIncome > band.Threshold) rate = band.Rate;
        return rate;
    }

    /// <summary>
    /// The surcharge on <paramref name="incomeTaxAfterRebate"/> for a taxable income (0 below the first rung), with
    /// <b>marginal relief</b> at the crossed threshold: the total (income-tax + surcharge) may not exceed the
    /// income-tax on the threshold (plus that threshold's lower-band surcharge) + the income above the threshold. The
    /// reference income-tax at the threshold is computed under the same <paramref name="age"/> band and the same
    /// year's table (F5), so a senior / super-senior old-regime taxpayer's relief uses their (higher) basic-exemption
    /// reference tax.
    /// </summary>
    public static decimal Surcharge(
        decimal taxableIncome, decimal incomeTaxAfterRebate, TaxRegime regime, SalaryTaxRates rates, AgeBand age = AgeBand.Below60)
    {
        ArgumentNullException.ThrowIfNull(rates);
        var rate = SurchargeRate(taxableIncome, regime, rates);
        if (rate <= 0m) return 0m;

        var surcharge = rate * incomeTaxAfterRebate;

        // The threshold this band starts on, and the rate applicable just below it.
        var threshold = 0m;
        foreach (var band in rates.SurchargeBands(regime))
            if (taxableIncome > band.Threshold) threshold = band.Threshold;

        var lowerRate = 0m;
        foreach (var band in rates.SurchargeBands(regime))
            if (band.Threshold < threshold) lowerRate = band.Rate;

        var taxAtThreshold = SlabTaxAfterRebate(threshold, regime, rates, age);
        var surchargeAtThreshold = lowerRate * taxAtThreshold;
        var cappedTotalOverThreshold = (taxAtThreshold + surchargeAtThreshold) + (taxableIncome - threshold);
        var actualTotal = incomeTaxAfterRebate + surcharge;
        var relief = actualTotal - cappedTotalOverThreshold;
        if (relief > 0m) surcharge = Math.Max(0m, surcharge - relief);
        return surcharge;
    }

    private static decimal SlabTaxAfterRebate(decimal taxableIncome, TaxRegime regime, SalaryTaxRates rates, AgeBand age = AgeBand.Below60)
    {
        var slab = SlabTax(taxableIncome, regime, rates, age);
        return slab - Rebate87A(taxableIncome, slab, regime, rates);
    }

    // ---- full annual computation ----

    /// <summary>
    /// The full annual income-tax on <paramref name="taxableIncome"/> under the regime + age band, <b>on the rate
    /// table <paramref name="rates"/> carries</b>: slab tax → §87A rebate → surcharge (+ marginal relief) → cess
    /// (last) → nearest-rupee annual tax. The intermediate figures — and the year the rates came from, and whether
    /// that year was provisional — are exposed on <see cref="SalaryTaxComputation"/> for Annexure II / Form 16
    /// Part B / the Income Tax Computation report.
    /// </summary>
    public static SalaryTaxComputation ComputeAnnual(
        decimal taxableIncome, TaxRegime regime, SalaryTaxRates rates, AgeBand age = AgeBand.Below60)
    {
        ArgumentNullException.ThrowIfNull(rates);
        var slabTax = SlabTax(taxableIncome, regime, rates, age);
        var rebate = Rebate87A(taxableIncome, slabTax, regime, rates);
        var incomeTaxAfterRebate = slabTax - rebate;
        var surcharge = Surcharge(taxableIncome, incomeTaxAfterRebate, regime, rates, age);
        var baseTax = incomeTaxAfterRebate + surcharge;
        var cess = TdsService.NearestRupee(rates.CessRate * baseTax).Amount;
        var annual = TdsService.NearestRupee(baseTax + cess);
        return new SalaryTaxComputation(
            taxableIncome, regime, age, slabTax, rebate, surcharge, cess, annual,
            rates.FinancialYearStartYear, rates.NotifiedFinancialYearStartYear,
            rates.CessRate, rates.CessRateIsCompanyOverride);
    }

    /// <summary>
    /// The §206AA no-PAN annual tax — the <b>higher</b> of the average-rate annual tax
    /// (<see cref="ComputeAnnual"/>) or 20% of the taxable income (nearest rupee).
    /// </summary>
    public static Money AnnualTaxNoPan(decimal taxableIncome, TaxRegime regime, SalaryTaxRates rates, AgeBand age = AgeBand.Below60)
    {
        ArgumentNullException.ThrowIfNull(rates);
        var withPan = ComputeAnnual(taxableIncome, regime, rates, age).AnnualTax;
        var floor = TdsService.NearestRupee(NoPanFloorRate * taxableIncome);
        return withPan >= floor ? withPan : floor;
    }

    // ---- §192 average-rate monthly spread + true-up ----

    /// <summary>
    /// The §192 average-rate monthly TDS: the annual tax not yet withheld spread over the months remaining in the FY
    /// (including the current month) — <c>round_nearest_rupee((annualTax − alreadyDeducted) / monthsRemaining)</c>,
    /// floored at ₹0 (an over-deducted employee is never refunded a negative deduction; the true-up zeroes it).
    /// </summary>
    public static Money MonthlyTds(Money annualTax, Money alreadyDeducted, int monthsRemaining)
    {
        if (monthsRemaining <= 0)
            throw new ArgumentOutOfRangeException(nameof(monthsRemaining), "Months remaining in the FY must be ≥ 1.");
        var residual = annualTax.Amount - alreadyDeducted.Amount;
        if (residual <= 0m) return Money.Zero;
        return TdsService.NearestRupee(residual / monthsRemaining);
    }

    /// <summary>The number of salary months remaining in the FY (Apr–Mar) from <paramref name="periodTo"/> (the
    /// payroll month) inclusive — 12 in April … 1 in March. Used to spread the estimated annual tax.</summary>
    public static int MonthsRemainingInFy(DateOnly periodTo)
    {
        var m = periodTo.Month;
        // Apr(4) → 12, May(5) → 11, …, Mar(3) → 1.
        return m >= 4 ? 12 - (m - 4) : 12 - (m + 8);
    }
}

/// <summary>
/// The itemised annual income-tax computation produced by <see cref="SalaryIncomeTax.ComputeAnnual"/> (Phase 8
/// slice 7) — the figures Form 24Q Annexure II and Form 16 Part B report. All whole-rupee-precise; the final
/// <see cref="AnnualTax"/> is the nearest-rupee §192 liability.
///
/// <para>🔴 The last four members are the <b>provenance of the arithmetic</b>, added with the T1-26 fix. They exist
/// so a figure can never again be read off a screen or a return without the reader being able to see which year's
/// law produced it, whether that year's law was actually published, and whose cess rate was charged.</para>
/// </summary>
public sealed record SalaryTaxComputation(
    decimal TaxableIncome,
    TaxRegime Regime,
    SalaryIncomeTax.AgeBand Age,
    decimal SlabTax,
    decimal Rebate87A,
    decimal Surcharge,
    decimal Cess,
    Money AnnualTax,
    int RatesFinancialYearStartYear,
    int RatesNotifiedFinancialYearStartYear,
    decimal CessRate,
    bool CessRateIsCompanyOverride)
{
    /// <summary>Income-tax after the §87A rebate (before surcharge and cess).</summary>
    public decimal IncomeTaxAfterRebate => SlabTax - Rebate87A;

    /// <summary>
    /// 🔴 <b>True when no rate table has been published for <see cref="RatesFinancialYearStartYear"/> and a
    /// neighbouring year's was carried over</b> (<see cref="RatesNotifiedFinancialYearStartYear"/> says which). Every
    /// surface that prints these figures must say so — the whole point of defect T1-26 was that this substitution
    /// used to happen silently.
    /// </summary>
    public bool RatesAreProvisional => RatesFinancialYearStartYear != RatesNotifiedFinancialYearStartYear;

    /// <summary>The financial-year label the rates are FOR, e.g. "2025-26".</summary>
    public string RatesFinancialYearLabel =>
        SalaryTaxRates.FinancialYearLabelOf(RatesFinancialYearStartYear);

    /// <summary>The financial-year label the rates actually CAME FROM, e.g. "2025-26".</summary>
    public string RatesNotifiedFinancialYearLabel =>
        SalaryTaxRates.FinancialYearLabelOf(RatesNotifiedFinancialYearStartYear);

    /// <summary>
    /// The sentence a payslip, report or certificate prints when <see cref="RatesAreProvisional"/>, or
    /// <c>null</c> when the year's own table was used.
    ///
    /// <para>🔴 The wording is <b>delegated</b> to <see cref="SalaryTaxRates.ProvisionalNoteFor"/> rather than
    /// repeated here. It used to be a second copy of the same sentence, and two copies of a disclosure are two
    /// things that can be edited apart — which is the precise way the report's old hard-coded footnote came to
    /// contradict the engine it was describing.</para>
    /// </summary>
    public string? ProvisionalRatesNote =>
        SalaryTaxRates.ProvisionalNoteFor(RatesFinancialYearStartYear, RatesNotifiedFinancialYearStartYear);
}
