using Apex.Ledger.Domain;

namespace Apex.Ledger.Services;

/// <summary>
/// One marginal slab segment of a published income-tax table: <c>[From, To)</c> taxed at
/// <see cref="RateBasisPoints"/> (10 000 = 100%). <see cref="To"/> <c>null</c> ⇒ the open-ended top band.
/// </summary>
/// <remarks>
/// Basis points, never a decimal percentage, for the reason the rest of this codebase uses them: every published
/// slab rate (5 / 10 / 15 / 20 / 25 / 30 per cent) is an exact integer number of basis points, so the table carries
/// no binary-fraction rounding of its own and two tables can be compared for equality.
/// </remarks>
public readonly record struct SalaryTaxSlab(decimal From, decimal? To, int RateBasisPoints);

/// <summary>
/// One surcharge rung: the surcharge <see cref="Rate"/> that applies once taxable income <b>exceeds</b>
/// <see cref="Threshold"/>.
/// </summary>
public readonly record struct SalarySurchargeBand(decimal Threshold, decimal Rate);

/// <summary>
/// 🔴 <b>THE DATED §192 RATE TABLE — the fix for defect T1-26.</b> Every slab boundary, rate, standard deduction,
/// §87A ceiling/cap, surcharge rung and cess rate the salary income-tax engine uses, resolved <b>by the financial
/// year of the payroll period</b> rather than read out of a compile-time constant.
///
/// <para>🔴 <b>WHAT WAS WRONG, STATED PLAINLY, BECAUSE IT WAS SHIPPING.</b> Before this type,
/// <c>SalaryIncomeTax.Slabs(TaxRegime, AgeBand)</c> took <b>no date of any kind</b> and every figure was a bare
/// <c>const</c> with no effective-from. The FY 2025-26 tables were therefore applied to <b>every</b> payroll period
/// this product will ever run, including FY 2024-25 periods already in a book and FY 2026-27 periods running today.
/// It is the largest wrong-money exposure in the payroll area, and it is not confined to the payslip: the Income Tax
/// Computation report, <c>Form24Q.BuildAnnexureII</c> and Form 16 Part B are all asserted equal to the same engine,
/// so a wrong year contaminated a <b>filed government return</b> as silently as it contaminated a payslip.</para>
///
/// <para>🔴 <b>ONLY YEARS THAT COULD BE SOURCED ARE SHIPPED. A FABRICATED SLAB IS FAR WORSE THAN A MISSING ONE.</b>
/// Two financial years carry a notified table here — FY 2024-25 (AY 2025-26) and FY 2025-26 (AY 2026-27) — because
/// those are the two whose full published tables could be retrieved and read by content from the Income Tax
/// Department's own portals on 2026-09-15. <b>FY 2026-27 (AY 2027-28) is deliberately absent.</b> A search restricted
/// to <c>incometaxindia.gov.in</c> and <c>incometax.gov.in</c> returned <b>no</b> AY 2027-28 rate publication, and the
/// note that used to sit on <c>SalaryIncomeTax.CessRate</c> (a bare <c>const</c> this change removed, because a
/// compile-time rate with no effective-from IS the defect) already recorded that the Finance Act 2026 First Schedule
/// could not be retrieved. Inventing a table for it would repeat the mistake this project has already had to strip
/// out of shipped code.</para>
///
/// <para>🔴 <b>WHAT HAPPENS FOR A YEAR WITH NO NOTIFIED TABLE — CARRY FORWARD, BUT SAY SO.</b> Refusing to compute
/// would break a live payroll, so <see cref="ForFinancialYear"/> carries the nearest notified table forward (or, for
/// a period older than the earliest shipped table, backward) and sets <see cref="IsProvisional"/>. That flag is
/// <b>not</b> decorative: it reaches <see cref="SalaryTaxComputation.RatesAreProvisional"/> and from there the
/// payslip, the Income Tax Computation report, Annexure II and Form 16 Part B. The difference from the defect this
/// replaces is the whole point — the substitution was <b>silent and undiscoverable</b> before, and is <b>named,
/// dated and displayed</b> now.</para>
///
/// <para><b>Anchored on the period END date</b>, matching this engine's existing convention for resolving the dated
/// <c>SalaryStructure</c> and the v63 dated computation slab ("the one in force on the period-end date"), so the
/// three cannot disagree about which period a date belongs to.</para>
/// </summary>
public sealed class SalaryTaxRates
{
    /// <summary>The financial year this table was <b>asked</b> for, as its starting calendar year (2025 = FY 2025-26).</summary>
    public int FinancialYearStartYear { get; }

    /// <summary>The financial year whose <b>notified</b> table these figures actually are. Equal to
    /// <see cref="FinancialYearStartYear"/> whenever a table was published for the year asked for.</summary>
    public int NotifiedFinancialYearStartYear { get; }

    /// <summary>
    /// 🔴 <b>True when no rate table has been published for <see cref="FinancialYearStartYear"/> and the figures below
    /// are a neighbouring year's, carried over.</b> Every computation made on such a table is provisional and must say
    /// so on its face — see <see cref="SalaryTaxComputation.RatesAreProvisional"/>.
    /// </summary>
    public bool IsProvisional => FinancialYearStartYear != NotifiedFinancialYearStartYear;

    /// <summary>The financial-year label these rates are <b>for</b>, e.g. "2025-26".</summary>
    public string FinancialYearLabel => FinancialYearLabelOf(FinancialYearStartYear);

    /// <summary>The financial-year label these rates actually <b>came from</b>, e.g. "2025-26". Differs from
    /// <see cref="FinancialYearLabel"/> exactly when <see cref="IsProvisional"/>.</summary>
    public string NotifiedFinancialYearLabel => FinancialYearLabelOf(NotifiedFinancialYearStartYear);

    /// <summary>"2025" ⇒ "2025-26".</summary>
    public static string FinancialYearLabelOf(int financialYearStartYear) =>
        $"{financialYearStartYear}-{(financialYearStartYear + 1) % 100:00}";

    /// <summary>
    /// 🔴 <b>The single wording of the provisional-rates disclosure</b>, or <c>null</c> when the year asked for has
    /// its own notified table. Written <b>once, here</b> so the payslip, the Income Tax Computation report, Form 24Q
    /// Annexure II and Form 16 Part B cannot word the same substitution differently — every one of them is the same
    /// arithmetic and a reader comparing two of them must not find two different stories.
    /// </summary>
    public static string? ProvisionalNoteFor(int financialYearStartYear, int notifiedFinancialYearStartYear) =>
        financialYearStartYear == notifiedFinancialYearStartYear
            ? null
            : $"Income-tax rates for FY {FinancialYearLabelOf(financialYearStartYear)} have not been notified in "
              + $"this build; the FY {FinancialYearLabelOf(notifiedFinancialYearStartYear)} rates have been applied "
              + "and this computation is provisional.";

    /// <inheritdoc cref="ProvisionalNoteFor"/>
    public string? ProvisionalNote => ProvisionalNoteFor(FinancialYearStartYear, NotifiedFinancialYearStartYear);

    /// <summary>
    /// 🔴 <b>The rate-basis sentence every printed §192 surface carries</b> — always present, never <c>null</c>. It
    /// states which financial year's tables produced the figures and whose Health &amp; Education Cess rate was
    /// charged (the statutory one, or this company's dated override).
    ///
    /// <para><b>Why this exists as data rather than as a literal on the report.</b> Defect T1-26 left behind a
    /// hard-coded footnote that named one financial year and declared the tables undated. Once the engine became
    /// dated that sentence was not merely stale, it was <b>false on the face of a tax computation</b>: an FY 2024-25
    /// report — correctly priced on the FY 2024-25 table — still printed that FY 2025-26 tables had been used and
    /// that no effective-from date was consulted. A disclosure that is computed from the table actually resolved
    /// cannot drift away from the arithmetic it describes.</para>
    /// </summary>
    public string BasisNote
    {
        get
        {
            var cess = $"Health and Education Cess at {CessRate * 100m:0.##}% "
                + (CessRateIsCompanyOverride
                    ? "from this company's own dated rate."
                    : "as notified for that year.");
            return IsProvisional
                ? $"Computed on the FY {NotifiedFinancialYearLabel} slab, surcharge and cess tables, applied to "
                  + $"FY {FinancialYearLabel} because that year's own rates are not notified in this build. {cess}"
                : $"Computed on the slab, surcharge and cess tables notified for FY {FinancialYearLabel}. {cess}";
        }
    }

    /// <summary>Standard deduction u/s 16(ia) against salary — new regime.</summary>
    public decimal NewRegimeStandardDeduction { get; }

    /// <summary>Standard deduction u/s 16(ia) against salary — old regime.</summary>
    public decimal OldRegimeStandardDeduction { get; }

    /// <summary>§87A (new regime): the total-income ceiling at or below which the rebate is available.</summary>
    public decimal NewRegimeRebateTaxableCeiling { get; }

    /// <summary>§87A (new regime): the maximum rebate.</summary>
    public decimal NewRegimeRebateCap { get; }

    /// <summary>§87A (old regime): the total-income ceiling — a hard cliff, no marginal relief.</summary>
    public decimal OldRegimeRebateTaxableCeiling { get; }

    /// <summary>§87A (old regime): the maximum rebate.</summary>
    public decimal OldRegimeRebateCap { get; }

    /// <summary>
    /// The Health &amp; Education Cess rate applied last on (income-tax + surcharge). Defaults to the statutory rate
    /// notified for the year; a company may override it for a dated period — see <see cref="WithCessRate"/> and
    /// <see cref="CessRateIsCompanyOverride"/>.
    /// </summary>
    public decimal CessRate { get; }

    /// <summary>True when <see cref="CessRate"/> came from a company's own dated override rather than from the
    /// statutory table. Surfaced so a report can distinguish "the law's rate" from "this book's rate".</summary>
    public bool CessRateIsCompanyOverride { get; }

    /// <summary>The surcharge rungs for the regime (new regime capped at 25% — no 37% band).</summary>
    public IReadOnlyList<SalarySurchargeBand> NewRegimeSurcharge { get; }

    /// <inheritdoc cref="NewRegimeSurcharge"/>
    public IReadOnlyList<SalarySurchargeBand> OldRegimeSurcharge { get; }

    private readonly IReadOnlyList<SalaryTaxSlab> _newRegime;
    private readonly IReadOnlyList<SalaryTaxSlab> _oldBelow60;
    private readonly IReadOnlyList<SalaryTaxSlab> _oldSenior;
    private readonly IReadOnlyList<SalaryTaxSlab> _oldSuperSenior;

    private SalaryTaxRates(
        int financialYearStartYear,
        int notifiedFinancialYearStartYear,
        decimal newRegimeStandardDeduction,
        decimal oldRegimeStandardDeduction,
        decimal newRegimeRebateTaxableCeiling,
        decimal newRegimeRebateCap,
        decimal oldRegimeRebateTaxableCeiling,
        decimal oldRegimeRebateCap,
        decimal cessRate,
        bool cessRateIsCompanyOverride,
        IReadOnlyList<SalaryTaxSlab> newRegime,
        IReadOnlyList<SalaryTaxSlab> oldBelow60,
        IReadOnlyList<SalaryTaxSlab> oldSenior,
        IReadOnlyList<SalaryTaxSlab> oldSuperSenior,
        IReadOnlyList<SalarySurchargeBand> newRegimeSurcharge,
        IReadOnlyList<SalarySurchargeBand> oldRegimeSurcharge)
    {
        FinancialYearStartYear = financialYearStartYear;
        NotifiedFinancialYearStartYear = notifiedFinancialYearStartYear;
        NewRegimeStandardDeduction = newRegimeStandardDeduction;
        OldRegimeStandardDeduction = oldRegimeStandardDeduction;
        NewRegimeRebateTaxableCeiling = newRegimeRebateTaxableCeiling;
        NewRegimeRebateCap = newRegimeRebateCap;
        OldRegimeRebateTaxableCeiling = oldRegimeRebateTaxableCeiling;
        OldRegimeRebateCap = oldRegimeRebateCap;
        CessRate = cessRate;
        CessRateIsCompanyOverride = cessRateIsCompanyOverride;
        _newRegime = newRegime;
        _oldBelow60 = oldBelow60;
        _oldSenior = oldSenior;
        _oldSuperSenior = oldSuperSenior;
        NewRegimeSurcharge = newRegimeSurcharge;
        OldRegimeSurcharge = oldRegimeSurcharge;
    }

    /// <summary>The published slab table for the regime + old-regime age band. The <b>new regime does not vary with
    /// age</b> — §115BAC states one table for every individual — so <paramref name="age"/> is read only in the old
    /// regime, where the basic exemption is the only thing that shifts.</summary>
    public IReadOnlyList<SalaryTaxSlab> Slabs(TaxRegime regime, SalaryIncomeTax.AgeBand age) =>
        regime == TaxRegime.New
            ? _newRegime
            : age switch
            {
                SalaryIncomeTax.AgeBand.SuperSenior => _oldSuperSenior,
                SalaryIncomeTax.AgeBand.Senior => _oldSenior,
                _ => _oldBelow60,
            };

    /// <summary>The surcharge rungs for the regime.</summary>
    public IReadOnlyList<SalarySurchargeBand> SurchargeBands(TaxRegime regime) =>
        regime == TaxRegime.New ? NewRegimeSurcharge : OldRegimeSurcharge;

    /// <summary>The standard deduction u/s 16(ia) for the regime, for this year.</summary>
    public decimal StandardDeduction(TaxRegime regime) =>
        regime == TaxRegime.New ? NewRegimeStandardDeduction : OldRegimeStandardDeduction;

    /// <summary>
    /// This same table with the Health &amp; Education Cess charged at <paramref name="rate"/> instead of the
    /// statutory rate — the per-company, dated override the user ruled for. Everything else is unchanged: a company
    /// may set its own cess rate, it may <b>not</b> set its own slabs.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The rate is negative or exceeds 100%.</exception>
    public SalaryTaxRates WithCessRate(decimal rate)
    {
        if (rate < 0m || rate > 1m)
            throw new ArgumentOutOfRangeException(nameof(rate), rate, "A cess rate must be between 0 and 1 (0% – 100%).");
        return new SalaryTaxRates(
            FinancialYearStartYear, NotifiedFinancialYearStartYear,
            NewRegimeStandardDeduction, OldRegimeStandardDeduction,
            NewRegimeRebateTaxableCeiling, NewRegimeRebateCap,
            OldRegimeRebateTaxableCeiling, OldRegimeRebateCap,
            rate, cessRateIsCompanyOverride: true,
            _newRegime, _oldBelow60, _oldSenior, _oldSuperSenior,
            NewRegimeSurcharge, OldRegimeSurcharge);
    }

    // ---- resolution ------------------------------------------------------------------------------------------

    /// <summary>The financial year (Apr–Mar) a date falls in, as its starting calendar year: 2025-04-01 … 2026-03-31
    /// ⇒ 2025.</summary>
    public static int FinancialYearStartYearOf(DateOnly date) => date.Month >= 4 ? date.Year : date.Year - 1;

    /// <summary>The rate table in force for the payroll period ending <paramref name="periodTo"/>.</summary>
    public static SalaryTaxRates ForPeriod(DateOnly periodTo) => ForFinancialYear(FinancialYearStartYearOf(periodTo));

    /// <summary>
    /// 🔴 <b>The table every production caller should use</b>: the statutory table for the period ending
    /// <paramref name="periodTo"/>, with <paramref name="company"/>'s own dated Health &amp; Education Cess rate
    /// applied if it has set one at or before that date.
    ///
    /// <para>This exists as ONE function so that no caller can resolve the year and forget the company's cess rate,
    /// or vice versa. A company that has never opened the cess screen has no dated row, so this returns the plain
    /// statutory table and the arithmetic is unchanged — which is the (c) limb of the cess ruling, held in one
    /// place rather than re-implemented at each of the four call sites.</para>
    /// </summary>
    public static SalaryTaxRates ForCompanyPeriod(Company company, DateOnly periodTo)
    {
        ArgumentNullException.ThrowIfNull(company);
        var statutory = ForPeriod(periodTo);
        var over = company.ResolveIncomeTaxCessRate(periodTo);
        return over is null ? statutory : statutory.WithCessRate(over.Rate);
    }

    /// <summary>
    /// The rate table for the financial year starting <paramref name="financialYearStartYear"/>. When no table has
    /// been notified for that year the nearest notified year's table is carried over and
    /// <see cref="IsProvisional"/> is set — see this type's remarks for why that is the behaviour and how it is
    /// surfaced.
    /// </summary>
    public static SalaryTaxRates ForFinancialYear(int financialYearStartYear)
    {
        // Clamp into the notified range: a year older than the earliest table gets the earliest, a year newer than
        // the latest gets the latest. Either way the result is flagged IsProvisional, because it is not that year's
        // own law. Written as a clamp rather than a switch so that adding a notified year between the two ends
        // needs no change here — only a new entry in the resolution below.
        var notified = Math.Clamp(financialYearStartYear, EarliestNotifiedFy, LatestNotifiedFy);
        var table = notified switch
        {
            Fy2024 => Fy2024_25(),
            _ => Fy2025_26(),
        };
        return financialYearStartYear == notified
            ? table
            : table.AsCarriedOverTo(financialYearStartYear);
    }

    /// <summary>The financial years that carry a notified, sourced table — ascending. Published so a report or a test
    /// can state exactly which years this build can compute without provisional carry-over.</summary>
    public static IReadOnlyList<int> NotifiedFinancialYears { get; } = new[] { Fy2024, Fy2025 };

    private SalaryTaxRates AsCarriedOverTo(int financialYearStartYear) =>
        new(financialYearStartYear, NotifiedFinancialYearStartYear,
            NewRegimeStandardDeduction, OldRegimeStandardDeduction,
            NewRegimeRebateTaxableCeiling, NewRegimeRebateCap,
            OldRegimeRebateTaxableCeiling, OldRegimeRebateCap,
            CessRate, CessRateIsCompanyOverride,
            _newRegime, _oldBelow60, _oldSenior, _oldSuperSenior,
            NewRegimeSurcharge, OldRegimeSurcharge);

    private const int Fy2024 = 2024;
    private const int Fy2025 = 2025;
    private const int EarliestNotifiedFy = Fy2024;
    private const int LatestNotifiedFy = Fy2025;

    // ---- the notified tables ---------------------------------------------------------------------------------

    // Both years' surcharge ladders are identical and are kept per-year anyway, so a future fork is a data change
    // rather than a code change. NEW REGIME IS CAPPED AT 25% — the 37% rung does not apply to income chargeable
    // under §115BAC. SOURCE, by content: incometax.gov.in/iec/foportal/help/individual/return-applicable-2 ("Senior
    // Citizens and Super Senior Citizens for AY 2026-2027"), surcharge table — "Above ₹5 crores … 25% (new) / 37%
    // (old)"; retrieved 2026-09-15.
    private static IReadOnlyList<SalarySurchargeBand> NewSurcharge() => new[]
    {
        new SalarySurchargeBand(50_00_000m, 0.10m),
        new SalarySurchargeBand(1_00_00_000m, 0.15m),
        new SalarySurchargeBand(2_00_00_000m, 0.25m),
    };

    private static IReadOnlyList<SalarySurchargeBand> OldSurcharge() => new[]
    {
        new SalarySurchargeBand(50_00_000m, 0.10m),
        new SalarySurchargeBand(1_00_00_000m, 0.15m),
        new SalarySurchargeBand(2_00_00_000m, 0.25m),
        new SalarySurchargeBand(5_00_00_000m, 0.37m),
    };

    /// <summary>
    /// <b>FY 2024-25 — Assessment Year 2025-26.</b>
    ///
    /// <para><b>SOURCE, BY CONTENT (R7 / ruling 14).</b> Income Tax Department e-filing portal,
    /// <c>https://www.incometax.gov.in/iec/foportal/help/individual/return-applicable-3</c>, page heading
    /// "<i>Salaried Individuals for AY 2025-26</i>", retrieved and read 2026-09-15. That page publishes all four
    /// tables encoded below:</para>
    /// <list type="bullet">
    ///   <item><b>New Tax Regime (Section 115BAC)</b>: "Up to ₹3,00,000 — Nil"; "₹3,00,001–₹7,00,000 — 5% above
    ///     ₹3,00,000"; "₹7,00,001–₹10,00,000 — ₹20,000 + 10% above ₹7,00,000"; "₹10,00,001–₹12,00,000 — ₹50,000 +
    ///     15% above ₹10,00,000"; "₹12,00,001–₹15,00,000 — ₹80,000 + 20% above ₹12,00,000"; then 30% above
    ///     ₹15,00,000.</item>
    ///   <item><b>Old Tax Regime – Below 60 Years</b>: nil to ₹2,50,000; 5%; 20% from ₹5,00,000; 30% above
    ///     ₹10,00,000.</item>
    ///   <item><b>Old Tax Regime – Senior Citizens (60–80 Years)</b>: nil to ₹3,00,000, then as above.</item>
    ///   <item><b>Old Tax Regime – Super Senior Citizens (80+ Years)</b>: "Up to ₹5,00,000 — Nil";
    ///     "₹5,00,001–₹10,00,000 — 20% above ₹5,00,000" — note there is <b>no 5% band at all</b>, which is why the
    ///     table below jumps straight from the nil band to 20%; then 30% above ₹10,00,000.</item>
    ///   <item><b>§87A</b>: ceiling "Up to ₹5 lakh" (old) / "Up to ₹7 lakh" (new).</item>
    ///   <item><b>Cess</b>: "<i>Health &amp; Education cess @ 4% to be paid on the amount of income tax plus
    ///     Surcharge</i>", both regimes.</item>
    /// </list>
    ///
    /// <para><b>The §87A new-regime CAP of ₹25,000</b> is taken from the statutory words quoted on the Department's
    /// own FAQ, <c>https://www.incometax.gov.in/iec/foportal/help/new-tax-vs-old-tax-regime-faqs</c> (retrieved
    /// 2026-09-15): a resident whose total income does not exceed seven hundred thousand rupees gets "<i>a deduction
    /// from the amount of income-tax … of an amount equal to one hundred per cent of such income-tax or an amount of
    /// twenty-five thousand rupees, whichever is less</i>". ⚠️ <b>Stated rather than glossed:</b> the AY 2025-26 page
    /// describes the same relief as "Tax rebate up to Rs.20,000". The two are <b>arithmetically indistinguishable
    /// here</b> and neither figure can change a rupee of output: under the slab table above the tax at the ₹7,00,000
    /// ceiling is exactly 5% × ₹4,00,000 = ₹20,000, so <c>min(slabTax, cap)</c> is ₹20,000 whether the cap is
    /// ₹20,000 or ₹25,000. The statutory wording is encoded because it is the instrument; the divergence is recorded
    /// here rather than hidden because a later year could make the cap bind.</para>
    ///
    /// <para><b>Standard deduction ₹75,000 (new) / ₹50,000 (old)</b> — 🔴 <b>RE-SOURCED 2026-09-15; the citation that
    /// stood here named the wrong document.</b> It attributed the figure loosely to "the Department's portal", and the
    /// nearest page it sat beside — the new-vs-old FAQ cited just above — in fact reads "<i>Standard deduction of
    /// Rs.50,000 … is available for both old and new tax regimes from AY 2024-25 onwards</i>", which would send a
    /// reviewer checking this line to a source that contradicts it. <b>The figures are right and the quote is real; it
    /// simply lives somewhere else.</b> Both are published in the CBDT e-Filing <b>ITR-1 Validation Rules</b>, whose
    /// AY 2026-27 edition (<c>incometax.gov.in/iec/foportal/sites/default/files/2026-05/</c><c>CBDT_e-Filing_ITR 1_Validation
    /// Rules_AY 2026-27.pdf</c>) was downloaded and read in full on 2026-09-15: rule 215, "<i>In case of New Tax Regime:
    /// Taxpayer being an employee can claim Standard deduction u/s 16ia only to the extent of Rs 75000</i>", and
    /// rule 112, "<i>In case of Old Tax Regime, taxpayer being an employee can claim Standard deduction u/s 16ia only
    /// to the extent of Rs 50000</i>". The AY 2025-26 edition of the same document carries the ₹75,000 rule for the new
    /// regime as well, which is why both notified years below ship the same pair.</para>
    /// </summary>
    private static SalaryTaxRates Fy2024_25() => new(
        financialYearStartYear: Fy2024,
        notifiedFinancialYearStartYear: Fy2024,
        newRegimeStandardDeduction: 75_000m,
        oldRegimeStandardDeduction: 50_000m,
        newRegimeRebateTaxableCeiling: 7_00_000m,
        newRegimeRebateCap: 25_000m,
        oldRegimeRebateTaxableCeiling: 5_00_000m,
        oldRegimeRebateCap: 12_500m,
        cessRate: 0.04m,
        cessRateIsCompanyOverride: false,
        newRegime: new[]
        {
            new SalaryTaxSlab(0m, 3_00_000m, 0),
            new SalaryTaxSlab(3_00_000m, 7_00_000m, 500),
            new SalaryTaxSlab(7_00_000m, 10_00_000m, 1000),
            new SalaryTaxSlab(10_00_000m, 12_00_000m, 1500),
            new SalaryTaxSlab(12_00_000m, 15_00_000m, 2000),
            new SalaryTaxSlab(15_00_000m, null, 3000),
        },
        oldBelow60: OldBelow60(),
        oldSenior: OldSenior(),
        oldSuperSenior: OldSuperSenior(),
        newRegimeSurcharge: NewSurcharge(),
        oldRegimeSurcharge: OldSurcharge());

    /// <summary>
    /// <b>FY 2025-26 — Assessment Year 2026-27.</b> These are the figures this engine already shipped as bare
    /// constants; nothing about them changes here, only the fact that they are now confined to their own year.
    ///
    /// <para><b>SOURCE, BY CONTENT (R7 / ruling 14).</b> Income Tax Department e-filing portal,
    /// <c>https://www.incometax.gov.in/iec/foportal/help/individual/return-applicable-1</c>, page heading
    /// "<i>Salaried Individuals for AY 2026-27</i>", and
    /// <c>https://www.incometax.gov.in/iec/foportal/help/individual/return-applicable-2</c>, heading "<i>Senior
    /// Citizens and Super Senior Citizens for AY 2026-2027</i>"; both retrieved and read 2026-09-15. Together they
    /// publish:</para>
    /// <list type="bullet">
    ///   <item><b>New Tax Regime (Section 115BAC)</b>: "Up to ₹4,00,000 — Nil"; "₹4,00,001–₹8,00,000 — 5% above
    ///     ₹4,00,000"; "₹8,00,001–₹12,00,000 — ₹20,000 + 10% above ₹8,00,000"; "₹12,00,001–₹16,00,000 — ₹60,000 +
    ///     15% above ₹12,00,000"; "₹16,00,001–₹20,00,000 — ₹1,20,000 + 20% above ₹16,00,000";
    ///     "₹20,00,001–₹24,00,000 — ₹2,00,000 + 25% above ₹20,00,000"; "Above ₹24,00,000 — ₹3,00,000 + 30% above
    ///     ₹24,00,000".</item>
    ///   <item>🔴 <b>The senior-citizen page prints that SAME new-regime table for 60–80-year-olds</b>, which is the
    ///     direct confirmation that <b>§115BAC does not vary with age</b> — the new regime's basic exemption is
    ///     ₹4,00,000 "<i>irrespective of the classification of the individual</i>". That is why
    ///     <see cref="Slabs"/> ignores the age band in the new regime.</item>
    ///   <item><b>Old regime</b>: nil to ₹2,50,000 (below 60) / ₹3,00,000 (senior) / ₹5,00,000 (super senior), then
    ///     5%, 20% from ₹5,00,000 and 30% above ₹10,00,000 — unchanged from FY 2024-25.</item>
    ///   <item><b>§87A</b>: "New Tax Regime: ₹60,000 (if taxable income ≤ ₹12,00,000)"; "Old Tax Regime: ₹12,500 (if
    ///     taxable income ≤ ₹5,00,000)".</item>
    ///   <item><b>Cess</b>: "<i>Health &amp; Education cess @ 4% to be paid on the amount of income tax plus
    ///     Surcharge</i>". This corroborates the citation that used to sit on the removed <c>CessRate</c> constant,
    ///     <c>incometaxindia.gov.in/w/tax-rates</c> — which returned <b>HTTP 403</b> to every non-browser request on
    ///     2026-09-15, exactly as that note predicted it would. 🔴 <b>403, not 404</b>, which is a different
    ///     diagnosis from the one the defect register records (it reads the page as gone; it is reachable but
    ///     blocked to non-browser agents). The figures are cited here to the e-filing portal instead, which is the
    ///     same Department and answered.</item>
    /// </list>
    /// </summary>
    private static SalaryTaxRates Fy2025_26() => new(
        financialYearStartYear: Fy2025,
        notifiedFinancialYearStartYear: Fy2025,
        newRegimeStandardDeduction: 75_000m,
        oldRegimeStandardDeduction: 50_000m,
        newRegimeRebateTaxableCeiling: 12_00_000m,
        newRegimeRebateCap: 60_000m,
        oldRegimeRebateTaxableCeiling: 5_00_000m,
        oldRegimeRebateCap: 12_500m,
        cessRate: 0.04m,
        cessRateIsCompanyOverride: false,
        newRegime: new[]
        {
            new SalaryTaxSlab(0m, 4_00_000m, 0),
            new SalaryTaxSlab(4_00_000m, 8_00_000m, 500),
            new SalaryTaxSlab(8_00_000m, 12_00_000m, 1000),
            new SalaryTaxSlab(12_00_000m, 16_00_000m, 1500),
            new SalaryTaxSlab(16_00_000m, 20_00_000m, 2000),
            new SalaryTaxSlab(20_00_000m, 24_00_000m, 2500),
            new SalaryTaxSlab(24_00_000m, null, 3000),
        },
        oldBelow60: OldBelow60(),
        oldSenior: OldSenior(),
        oldSuperSenior: OldSuperSenior(),
        newRegimeSurcharge: NewSurcharge(),
        oldRegimeSurcharge: OldSurcharge());

    // The three old-regime tables are byte-identical across both notified years (the published pages print the same
    // figures under both assessment years), so they are built once. They are still resolved THROUGH the year record,
    // so a year that forks them is a data change here and nothing else moves.
    private static IReadOnlyList<SalaryTaxSlab> OldBelow60() => new[]
    {
        new SalaryTaxSlab(0m, 2_50_000m, 0),
        new SalaryTaxSlab(2_50_000m, 5_00_000m, 500),
        new SalaryTaxSlab(5_00_000m, 10_00_000m, 2000),
        new SalaryTaxSlab(10_00_000m, null, 3000),
    };

    private static IReadOnlyList<SalaryTaxSlab> OldSenior() => new[]
    {
        new SalaryTaxSlab(0m, 3_00_000m, 0),
        new SalaryTaxSlab(3_00_000m, 5_00_000m, 500),
        new SalaryTaxSlab(5_00_000m, 10_00_000m, 2000),
        new SalaryTaxSlab(10_00_000m, null, 3000),
    };

    // No 5% band: the published super-senior table goes straight from the ₹5,00,000 nil ceiling to 20%.
    private static IReadOnlyList<SalaryTaxSlab> OldSuperSenior() => new[]
    {
        new SalaryTaxSlab(0m, 5_00_000m, 0),
        new SalaryTaxSlab(5_00_000m, 10_00_000m, 2000),
        new SalaryTaxSlab(10_00_000m, null, 3000),
    };
}
