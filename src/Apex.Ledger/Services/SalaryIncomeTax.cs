using Apex.Ledger.Domain;

namespace Apex.Ledger.Services;

/// <summary>
/// The <b>§192 salary-TDS income-tax engine</b> (Phase 8 slice 7; RQ-12; Finance Act 2025 / §115BAC(1A) / §87A;
/// A14-verified FY 2025-26 / AY 2026-27) — a <b>pure, deterministic</b>, framework-/DB-/clock-/RNG-free calculator
/// for the annual income-tax on an employee's estimated salary and the average-rate monthly §192 withholding. It is
/// dedicated logic (not the generic As-Computed-Value slabs) because the statutory computation cannot be expressed
/// as ordinary pay-head slabs:
/// <list type="bullet">
///   <item>two regimes with different slab tables, standard deductions and rebate rules
///     (<see cref="TaxRegime.New"/> default u/s 115BAC; <see cref="TaxRegime.Old"/> with Chapter VI-A);</item>
///   <item>a <b>§87A rebate</b> that is a <b>marginal-relief band</b> in the new regime (income just above ₹12L)
///     but a <b>hard cliff</b> at ₹5L in the old regime;</item>
///   <item><b>surcharge</b> with its own marginal relief at each threshold (≥₹50L), new-regime-capped at 25%;</item>
///   <item>a <b>4% health-and-education cess</b> applied <b>last</b>;</item>
///   <item>the <b>§192 average-rate mechanic</b> — annual tax spread over the months remaining in the FY, trued-up
///     as estimates change, never negative.</item>
/// </list>
/// Money rounds to the <b>nearest rupee, half-up</b> (the Phase-7 income-tax convention, <see cref="TdsService.NearestRupee"/>).
/// </summary>
public static class SalaryIncomeTax
{
    /// <summary>Standard deduction against salary — ₹75,000 (new regime) / ₹50,000 (old regime).</summary>
    public const decimal NewRegimeStandardDeduction = 75_000m;
    public const decimal OldRegimeStandardDeduction = 50_000m;

    /// <summary>§87A (new regime): taxable income ≤ ₹12,00,000 ⇒ full rebate (cap ₹60,000 = tax at ₹12L).</summary>
    public const decimal NewRegimeRebateTaxableCeiling = 12_00_000m;
    public const decimal NewRegimeRebateCap = 60_000m;

    /// <summary>§87A (old regime): taxable income ≤ ₹5,00,000 ⇒ rebate up to ₹12,500 (a hard cliff, no marginal relief).</summary>
    public const decimal OldRegimeRebateTaxableCeiling = 5_00_000m;
    public const decimal OldRegimeRebateCap = 12_500m;

    /// <summary>Health &amp; Education Cess — 4% on (income-tax + surcharge − rebate), both regimes, applied last.</summary>
    public const decimal CessRate = 0.04m;

    /// <summary>The §206AA no-PAN floor rate — 20% of taxable income (higher of average rate or 20%).</summary>
    public const decimal NoPanFloorRate = 0.20m;

    /// <summary>The old-regime senior-citizen age band (only the first nil band shifts): below 60, 60–&lt;80, 80+.</summary>
    public enum AgeBand
    {
        /// <summary>Below 60 (nil to ₹2,50,000).</summary>
        Below60 = 0,
        /// <summary>Senior citizen 60–&lt;80 (nil to ₹3,00,000).</summary>
        Senior = 1,
        /// <summary>Super-senior 80+ (nil to ₹5,00,000).</summary>
        SuperSenior = 2,
    }

    // A marginal slab segment: [From, To) taxed at RateBasisPoints (To null ⇒ open-ended top band).
    private readonly record struct Segment(decimal From, decimal? To, int RateBasisPoints);

    /// <summary>
    /// The old-regime senior-citizen <see cref="AgeBand"/> for an employee born on <paramref name="dateOfBirth"/>
    /// as at <paramref name="asOf"/> (the payroll date) — <see cref="AgeBand.SuperSenior"/> at 80+,
    /// <see cref="AgeBand.Senior"/> at 60–&lt;80, else <see cref="AgeBand.Below60"/>. An unknown date of birth is
    /// treated as below-60 (the safest — no extra exemption). Only the old regime uses the band; the new regime is
    /// flat ₹4L for all ages.
    /// </summary>
    public static AgeBand AgeBandFor(DateOnly? dateOfBirth, DateOnly asOf)
    {
        if (dateOfBirth is not { } dob) return AgeBand.Below60;
        var age = asOf.Year - dob.Year;
        if (dob > asOf.AddYears(-age)) age--; // not yet had this year's birthday
        return age >= 80 ? AgeBand.SuperSenior : age >= 60 ? AgeBand.Senior : AgeBand.Below60;
    }

    /// <summary>The standard deduction for the regime.</summary>
    public static decimal StandardDeduction(TaxRegime regime) =>
        regime == TaxRegime.New ? NewRegimeStandardDeduction : OldRegimeStandardDeduction;

    /// <summary>
    /// The taxable income = <paramref name="grossSalary"/> − standard deduction(regime) −
    /// <paramref name="allowedDeductions"/> (the regime-allowed Chapter VI-A / exemptions the caller resolved from
    /// the employee's declaration; ₹0 in the new regime beyond 80CCD(2)), floored at ₹0.
    /// </summary>
    public static decimal TaxableIncome(decimal grossSalary, decimal allowedDeductions, TaxRegime regime) =>
        Math.Max(0m, grossSalary - StandardDeduction(regime) - allowedDeductions);

    // ---- slab tax (marginal) ----

    private static IReadOnlyList<Segment> Slabs(TaxRegime regime, AgeBand age)
    {
        if (regime == TaxRegime.New)
            return new[]
            {
                new Segment(0m, 4_00_000m, 0),
                new Segment(4_00_000m, 8_00_000m, 500),
                new Segment(8_00_000m, 12_00_000m, 1000),
                new Segment(12_00_000m, 16_00_000m, 1500),
                new Segment(16_00_000m, 20_00_000m, 2000),
                new Segment(20_00_000m, 24_00_000m, 2500),
                new Segment(24_00_000m, null, 3000),
            };

        // Old regime — only the first nil band's upper bound shifts with age.
        var nilTo = age switch
        {
            AgeBand.Senior => 3_00_000m,
            AgeBand.SuperSenior => 5_00_000m,
            _ => 2_50_000m,
        };
        var segments = new List<Segment> { new(0m, nilTo, 0) };
        if (nilTo < 5_00_000m) segments.Add(new Segment(nilTo, 5_00_000m, 500));
        var twentyFrom = Math.Max(nilTo, 5_00_000m);
        segments.Add(new Segment(twentyFrom, 10_00_000m, 2000));
        segments.Add(new Segment(10_00_000m, null, 3000));
        return segments;
    }

    /// <summary>The marginal slab tax on <paramref name="taxableIncome"/> under the regime + age band (before §87A,
    /// surcharge and cess). Each segment contributes its rate on the portion of income within its band.</summary>
    public static decimal SlabTax(decimal taxableIncome, TaxRegime regime, AgeBand age = AgeBand.Below60)
    {
        if (taxableIncome <= 0m) return 0m;
        decimal tax = 0m;
        foreach (var seg in Slabs(regime, age))
        {
            var top = seg.To ?? taxableIncome;
            var portion = Math.Min(taxableIncome, top) - seg.From;
            if (portion > 0m) tax += seg.RateBasisPoints / 10_000m * portion;
        }
        return tax;
    }

    // ---- §87A rebate ----

    /// <summary>
    /// The §87A rebate on <paramref name="slabTax"/> for the regime. New regime: full rebate (capped ₹60,000) when
    /// taxable ≤ ₹12L, else a <b>marginal-relief</b> rebate = <c>max(0, slabTax − (taxable − 12L))</c> so tax before
    /// cess never exceeds the income above ₹12L. Old regime: a <b>hard cliff</b> — ₹12,500 (or the slab tax if less)
    /// when taxable ≤ ₹5L, else ₹0 (no marginal relief).
    /// </summary>
    public static decimal Rebate87A(decimal taxableIncome, decimal slabTax, TaxRegime regime)
    {
        if (regime == TaxRegime.New)
        {
            if (taxableIncome <= NewRegimeRebateTaxableCeiling)
                return Math.Min(slabTax, NewRegimeRebateCap);
            var excessOverCeiling = taxableIncome - NewRegimeRebateTaxableCeiling;
            return Math.Max(0m, slabTax - excessOverCeiling); // marginal relief band
        }

        // Old regime — hard cliff at ₹5L.
        return taxableIncome <= OldRegimeRebateTaxableCeiling ? Math.Min(slabTax, OldRegimeRebateCap) : 0m;
    }

    // ---- surcharge (+ marginal relief) ----

    // Surcharge thresholds: income must EXCEED the threshold; new regime is capped at 25% (no 37% band).
    private static IReadOnlyList<(decimal Threshold, decimal Rate)> SurchargeBands(TaxRegime regime) =>
        regime == TaxRegime.New
            ? new[] { (50_00_000m, 0.10m), (1_00_00_000m, 0.15m), (2_00_00_000m, 0.25m) }
            : new[] { (50_00_000m, 0.10m), (1_00_00_000m, 0.15m), (2_00_00_000m, 0.25m), (5_00_00_000m, 0.37m) };

    private static decimal SurchargeRate(decimal taxableIncome, TaxRegime regime)
    {
        decimal rate = 0m;
        foreach (var (threshold, r) in SurchargeBands(regime))
            if (taxableIncome > threshold) rate = r;
        return rate;
    }

    /// <summary>
    /// The surcharge on <paramref name="incomeTaxAfterRebate"/> for a taxable income (0 below ₹50L), with
    /// <b>marginal relief</b> at the crossed threshold: the total (income-tax + surcharge) may not exceed the
    /// income-tax on the threshold (plus that threshold's lower-band surcharge) + the income above the threshold. The
    /// reference income-tax at the threshold is computed under the same <paramref name="age"/> band (F5), so a
    /// senior / super-senior old-regime taxpayer's relief uses their (higher) basic-exemption reference tax.
    /// </summary>
    public static decimal Surcharge(decimal taxableIncome, decimal incomeTaxAfterRebate, TaxRegime regime, AgeBand age = AgeBand.Below60)
    {
        var rate = SurchargeRate(taxableIncome, regime);
        if (rate <= 0m) return 0m;

        var surcharge = rate * incomeTaxAfterRebate;

        // Marginal relief at the threshold this band starts on.
        var threshold = 0m;
        var lowerRate = 0m;
        foreach (var (t, r) in SurchargeBands(regime))
        {
            if (taxableIncome > t) { lowerRate = threshold == 0m ? 0m : lowerRate; threshold = t; }
        }
        // Recompute the lower-band rate (the rate applicable just below the crossed threshold).
        lowerRate = 0m;
        foreach (var (t, r) in SurchargeBands(regime))
        {
            if (t < threshold) lowerRate = r;
        }

        var taxAtThreshold = SlabTaxAfterRebate(threshold, regime, age);
        var surchargeAtThreshold = lowerRate * taxAtThreshold;
        var cappedTotalOverThreshold = (taxAtThreshold + surchargeAtThreshold) + (taxableIncome - threshold);
        var actualTotal = incomeTaxAfterRebate + surcharge;
        var relief = actualTotal - cappedTotalOverThreshold;
        if (relief > 0m) surcharge = Math.Max(0m, surcharge - relief);
        return surcharge;
    }

    private static decimal SlabTaxAfterRebate(decimal taxableIncome, TaxRegime regime, AgeBand age = AgeBand.Below60)
    {
        var slab = SlabTax(taxableIncome, regime, age);
        return slab - Rebate87A(taxableIncome, slab, regime);
    }

    // ---- full annual computation ----

    /// <summary>
    /// The full annual income-tax on <paramref name="taxableIncome"/> under the regime + age band: slab tax → §87A
    /// rebate → surcharge (+ marginal relief) → 4% cess (last) → nearest-rupee annual tax. The intermediate figures
    /// are exposed on <see cref="SalaryTaxComputation"/> for Annexure II / Form 16 Part B.
    /// </summary>
    public static SalaryTaxComputation ComputeAnnual(decimal taxableIncome, TaxRegime regime, AgeBand age = AgeBand.Below60)
    {
        var slabTax = SlabTax(taxableIncome, regime, age);
        var rebate = Rebate87A(taxableIncome, slabTax, regime);
        var incomeTaxAfterRebate = slabTax - rebate;
        var surcharge = Surcharge(taxableIncome, incomeTaxAfterRebate, regime, age);
        var baseTax = incomeTaxAfterRebate + surcharge;
        var cess = TdsService.NearestRupee(CessRate * baseTax).Amount;
        var annual = TdsService.NearestRupee(baseTax + cess);
        return new SalaryTaxComputation(taxableIncome, regime, age, slabTax, rebate, surcharge, cess, annual);
    }

    /// <summary>
    /// The §206AA no-PAN annual tax — the <b>higher</b> of the average-rate annual tax
    /// (<see cref="ComputeAnnual"/>) or 20% of the taxable income (nearest rupee).
    /// </summary>
    public static Money AnnualTaxNoPan(decimal taxableIncome, TaxRegime regime, AgeBand age = AgeBand.Below60)
    {
        var withPan = ComputeAnnual(taxableIncome, regime, age).AnnualTax;
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

    /// <summary>The number of salary months remaining in the FY (Apr–Mar) from <paramref name="month"/> (the payroll
    /// month) inclusive — 12 in April … 1 in March. Used to spread the estimated annual tax.</summary>
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
/// </summary>
public sealed record SalaryTaxComputation(
    decimal TaxableIncome,
    TaxRegime Regime,
    SalaryIncomeTax.AgeBand Age,
    decimal SlabTax,
    decimal Rebate87A,
    decimal Surcharge,
    decimal Cess,
    Money AnnualTax)
{
    /// <summary>Income-tax after the §87A rebate (before surcharge and cess).</summary>
    public decimal IncomeTaxAfterRebate => SlabTax - Rebate87A;
}
