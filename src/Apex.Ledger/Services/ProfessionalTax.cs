using Apex.Ledger.Domain;

namespace Apex.Ledger.Services;

/// <summary>
/// The <b>Professional-Tax engine</b> (Phase 8 slice 6; RQ-11; Article 276(2)) — a <b>pure, deterministic</b>,
/// framework-/DB-/clock-/RNG-free calculator for the state PT slab deduction. It exists as dedicated logic (rather
/// than the generic As-Computed-Value slabs) because PT cannot be expressed as ordinary slabs:
/// <list type="bullet">
///   <item>it is <b>flat-amount-by-band</b>, not a percentage — the single band containing the monthly PT-wages
///     contributes its fixed rupee amount;</item>
///   <item><b>some</b> states charge a higher amount in a single <b>balancing month</b> — of the seeded states,
///     <b>only Maharashtra</b>, whose schedule pays its top band's ₹2,500 p.a. as ₹200 per month <i>except</i>
///     February and ₹300 for February — via a per-band month override. <b>Karnataka and West Bengal have no
///     balancing month</b>: their schedules are flat monthly rates (see <see cref="SeedSlabTables"/> for the
///     statutory citation of each);</item>
///   <item>only <b>Maharashtra</b> differentiates by gender (women exempt to ₹25,000) — handled by resolving the
///     gender-scoped slab table before this engine is reached;</item>
///   <item>a <b>constitutional hard cap of ₹2,500 per person per financial year</b> trims the last deduction so the
///     cumulative FY PT never exceeds ₹2,500, regardless of the configured slabs.</item>
/// </list>
/// All figures are exact whole-rupee <see cref="Money"/> (PT is whole rupees; PT-wages are compared in whole rupees).
/// </summary>
public static class ProfessionalTax
{
    /// <summary>
    /// The constitutional <b>annual cap</b> on PT per person per financial year (Article 276(2)): ₹2,500.
    /// <para><b>Source</b> — The Constitution (Sixtieth Amendment) Act, 1988, Ministry of Law and Justice
    /// (Legislative Department), <c>https://www.legislative.gov.in/static/uploads/2025/07/1f71451452256d6fa3063c5678d7f8a9.pdf</c>
    /// (retrieved and read in full 2026-09-06), s. 2: <i>"Amendment of article 276.—In article 276 of the
    /// Constitution, in clause (2),— (a) for the words 'two hundred and fifty rupees', the words 'two thousand and
    /// five hundred rupees' shall be substituted; (b) the proviso shall be omitted."</i></para>
    /// <para>🔴 It is a <b>CAP, not a schedule.</b> A state may levy anything up to it and most levy less; reading
    /// ₹2,500 back as a per-state annual target is what produced the Karnataka February defect fixed in
    /// <see cref="SeedSlabTables"/>. This constant bounds a mis-configured slab — it must never be used to derive one.</para>
    /// </summary>
    public const decimal AnnualCap = 2500m;

    // GST state codes for the seeded PT states.
    private const string Maharashtra = "27";
    private const string Karnataka = "29";
    private const string WestBengal = "19";

    /// <summary>February — the balancing month <b>Maharashtra</b> (and only Maharashtra, of the seeded states)
    /// over-charges on its top band, so that band's twelve months total the ₹2,500 p.a. its own schedule states.
    /// This is a per-state fact carried in each state's data, <b>not</b> a general rule of professional tax.</summary>
    public const int FebruaryOverrideMonth = 2;

    /// <summary>
    /// The <b>monthly PT before the annual cap</b> for a member whose whole-rupee PT-wages select a band of
    /// <paramref name="slab"/> in calendar <paramref name="month"/> (1–12): the band amount for the month (applying a
    /// February/any-month override when present), or ₹0 when no band contains the wages. PT-wages are rounded to whole
    /// rupees <b>half-up (away from zero)</b> before band selection — the <b>same</b> whole-rupee rounding the PT
    /// register displays the PT-wage with (F1) — so the band selected always agrees with the wage shown against it (a
    /// fractional gross of ₹10,000.50 rounds to ₹10,001 and selects the &gt;₹10,000 band, never the ₹10,000 band), and
    /// a fractional wage never falls into a boundary gap between contiguous integer bands.
    /// </summary>
    public static Money MonthlyBeforeCap(PtSlab slab, decimal ptWages, int month)
    {
        ArgumentNullException.ThrowIfNull(slab);
        if (month is < 1 or > 12)
            throw new ArgumentOutOfRangeException(nameof(month), "PT month must be a calendar month 1–12.");
        if (ptWages < 0m) return Money.Zero;
        var wholeRupee = Math.Round(ptWages, 0, MidpointRounding.AwayFromZero);
        var band = slab.SelectBand(wholeRupee);
        return band is null ? Money.Zero : band.AmountForMonth(month);
    }

    /// <summary>
    /// Trims <paramref name="monthlyBeforeCap"/> so that <paramref name="priorFyCumulative"/> + the result never
    /// exceeds the ₹2,500 annual cap (Article 276(2)): the remaining head-room is <c>2500 − prior</c>; the deduction
    /// is <c>min(monthly, remaining)</c>, and ₹0 once the cap is already reached. This is the safety net that bounds
    /// even a mis-configured over-₹2,500 slab.
    /// </summary>
    public static Money ApplyAnnualCap(Money monthlyBeforeCap, Money priorFyCumulative)
    {
        var remaining = AnnualCap - priorFyCumulative.Amount;
        if (remaining <= 0m) return Money.Zero;
        return monthlyBeforeCap.Amount <= remaining ? monthlyBeforeCap : new Money(remaining);
    }

    /// <summary>The capped monthly PT: <see cref="MonthlyBeforeCap"/> trimmed by <see cref="ApplyAnnualCap"/> against
    /// the member's PT already deducted this financial year.</summary>
    public static Money ComputeMonthly(PtSlab slab, decimal ptWages, int month, Money priorFyCumulative)
        => ApplyAnnualCap(MonthlyBeforeCap(slab, ptWages, month), priorFyCumulative);

    /// <summary>The first day (1 April) of the PT financial year (Apr–Mar) containing <paramref name="date"/> — the
    /// window the ₹2,500 cumulative cap resets on.</summary>
    public static DateOnly FinancialYearStart(DateOnly date) =>
        date.Month >= 4 ? new DateOnly(date.Year, 4, 1) : new DateOnly(date.Year - 1, 4, 1);

    /// <summary>
    /// The <b>seeded PT slab tables</b> — Maharashtra (men + women, gender-scoped), Karnataka and West Bengal —
    /// with fresh ids. Editable per company; seed values are a starting point, not law.
    /// <para><b>Every band below is cited to the issuing state's own published rate schedule</b>, each of which was
    /// retrieved and read in full on 2026-09-06 (closes T0-21, which recorded these tables as driving a live monthly
    /// salary deduction under an "A14-verified" label with no citation of any kind). The URL is stated inline at each
    /// table so the next reader can re-check the figures against the instrument rather than against this comment.</para>
    /// <para>🔴 <b>Only Maharashtra has a February balancing month.</b> Its schedule says so in terms. Karnataka's and
    /// West Bengal's do not, and the ₹2,500 of <see cref="AnnualCap"/> is a constitutional ceiling, not a per-state
    /// annual target — so it must not be back-solved into a February over-charge for a state whose schedule is a flat
    /// monthly rate. Karnataka carried exactly that unsourced ₹300 February override until 2026-09-06 and
    /// over-deducted ₹100 from every Karnataka employee every year (₹2,500 charged against a statutory ₹2,400); the
    /// annual cap never caught it because the over-charge landed exactly <i>at</i> the cap.</para>
    /// <para>✅ <b>The residual this fix left open is now CLOSED — by schema v54, not by this file.</b> These tables
    /// are seeded once, at enrolment (<c>PayrollService.EnableProfessionalTax</c>), and then <b>persisted and
    /// user-editable</b>, so the correction above reaches only companies enrolled after it; a company enrolled for
    /// Karnataka <i>before</i> 2026-09-06 kept the ₹300 February override in its own saved data. That is repaired on
    /// open by <c>Schema.MigrateV53ToV54</c> (Ruling 16, 2026-09-06), which is <b>fingerprint-gated</b>: it clears
    /// the override only from a Karnataka table still matching this seed exactly, and leaves an operator-edited
    /// table alone. <b>Do not add a repair here.</b> Seeding is the future; the migration is history, and correcting
    /// history from the seed would either re-seed live books or clobber operator edits.</para>
    /// </summary>
    public static IReadOnlyList<PtSlab> SeedSlabTables()
    {
        Money R(decimal v) => new(v);
        // The Maharashtra balancing month — ₹300 for February on the ₹200 top band. Maharashtra ONLY; see below.
        PtMonthOverride feb300 = new(FebruaryOverrideMonth, R(300m));

        // ── MAHARASHTRA — MEN: ≤7,500 Nil · 7,501–10,000 ₹175 · >10,000 ₹200 (₹300 Feb) ──────────────────────────
        // SOURCE: Government of Maharashtra, Department of Goods and Services Tax, "RATE SCHEDULES UNDER THE
        // PROFESSION TAX ACT, 1975", SCHEDULE I (SEE SECTION 3), as on 31.03.2025 —
        // https://www.mahagst.gov.in/public/uploads/menufiles/PT%20Rate%20Schedule%20updated%20upto%2031.03.2025%20(1)%20(1).pdf
        // Entry 1, block headed "1/4/2023 onwards", verbatim:
        //   "(i) in case of men, whose monthly salaries or wages,— (a) do not exceed rupees seven thousand five
        //    hundred ; Nil. (b) exceed rupees seven thousand five hundred but do not exceed rupees ten thousand ;
        //    one hundred seventy-five per month. (c) exceed rupees ten thousand ; two thousand five hundred per
        //    annum to be paid in in following manner :— (a) two hundred per month except for the month of February ;
        //    (b) three hundred for the month of February;"
        // The February over-charge IS the schedule's own wording — it is correct here and must not be removed.
        var mhMale = new PtSlab(Guid.NewGuid(), Maharashtra, PtGenderScope.Male, new[]
        {
            new PtSlabBand(R(0m), R(7500m), R(0m)),
            new PtSlabBand(R(7501m), R(10000m), R(175m)),
            new PtSlabBand(R(10001m), null, R(200m), new[] { feb300 }),
        });

        // ── MAHARASHTRA — WOMEN: ≤25,000 Nil · >25,000 ₹200 (₹300 Feb) ───────────────────────────────────────────
        // SOURCE: same schedule, same "1/4/2023 onwards" block, entry 1, verbatim:
        //   "(ii) in case of women, whose monthly salaries or wages,— (a) do not exceed rupees twenty-five thousand ;
        //    Nil. (b) exceed rupees twenty-five thousand ; two thousand five hundred per annum to be paid in
        //    following manner :— (a) two hundred per month except for the month of February ; (b) three hundred for
        //    the month of February."
        // The ₹25,000 women's exemption dates from this block; the preceding block (1/4/2015 to 31/3/2023) exempted
        // women only to ₹10,000. February over-charge: correct here too.
        var mhFemale = new PtSlab(Guid.NewGuid(), Maharashtra, PtGenderScope.Female, new[]
        {
            new PtSlabBand(R(0m), R(25000m), R(0m)),
            new PtSlabBand(R(25001m), null, R(200m), new[] { feb300 }),
        });

        // ── KARNATAKA — no gender: ≤24,999 Nil · ≥25,000 ₹200 FLAT, NO FEBRUARY OVER-CHARGE ─────────────────────
        // SOURCE: Government of Karnataka, Professional Tax portal, PT Amendment Bill, "SCHEDULE [See Section 3(2)]
        // Rates of tax on professions, trades, callings and employments" —
        // https://ptax.karnataka.gov.in/documents/pt%20amendment%20bill.pdf
        // Sl. No. 1, verbatim and in full:
        //   "Salary or wage earners whose salary or wage or both, as the case may be, for a month is Rs. 25,000-00
        //    and above — Rs. 200-00 per month"
        // 🔴 That is the WHOLE entry: one band, one flat monthly rate, no balancing-month provision. The word
        // "February" does not occur anywhere in the schedule — zero hits over the full `pdftotext -raw` extraction
        // (113 lines). The "Rs. 2,500-00 per annum" appearing repeatedly in the same schedule is the rate for OTHER
        // classes of person (Sl. Nos. 2–12: registered persons, employers, professionals, companies …) and has
        // nothing to do with salary earners. Statutory annual liability for a Karnataka employee on ₹25,000+ is
        // 12 × ₹200 = ₹2,400.
        var ka = new PtSlab(Guid.NewGuid(), Karnataka, PtGenderScope.Any, new[]
        {
            new PtSlabBand(R(0m), R(24999m), R(0m)),
            new PtSlabBand(R(25000m), null, R(200m)),
        });

        // ── WEST BENGAL — no gender, NO February override ────────────────────────────────────────────────────────
        // SOURCE: Government of West Bengal, Silpa Sathi portal, "West Bengal State Rates of Tax on Professions,
        // Trades, Callings and Employments", Sl. No. 1 — https://silpasathi.wb.gov.in/read-bytea-file-all/aWQ=/MTQ4/
        // NDQ=/ZW9kYl9zdGF0aWNfcGFnZV9wZGZfbWFuYWdl/cGRmX2NvbnRlbnQ=/RHV0aWVzLUxldmllcy0xLnBkZg==
        // (wbcomtax.gov.in refuses the connection and wbprofessiontax.gov.in no longer resolves; this is the same
        // schedule on a live WB-government host.) Verbatim:
        //   "1 Employees earning monthly salary or wages— (i) Not exceeding Rs.8,500 … Nil, (ii) Above Rs.8,500 but
        //    not exceeding Rs. 10,000 … Nil*, (iii) Above Rs. 10,000 but not exceeding Rs. 15,000 … Rs.110 per month,
        //    (iv) Above Rs. 15,000 but not exceeding Rs. 25,000 … Rs. 130 per month, (v) Above Rs.25,000 but not
        //    exceeding Rs.40,000 … Rs. 150 per month, (vi) Above Rs.40,000 … Rs. 200 per month."
        //   "* Tax rate reduced to Nil, from Rs 90/- p.m., with effect from 1-8-2016 vide Notification No. 682-L
        //    dt.28th July, 2016 read with Notification No. 1197-FT dt.16th August, 2016."
        // Our single ₹0 band to ₹10,000 collapses the schedule's two Nil bands (i) and (ii) — behaviourally
        // identical. No February provision occurs anywhere in the WB schedule either (zero hits), so no override.
        // 12 × ₹200 = ₹2,400 on the top band, under the ₹2,500 cap.
        var wb = new PtSlab(Guid.NewGuid(), WestBengal, PtGenderScope.Any, new[]
        {
            new PtSlabBand(R(0m), R(10000m), R(0m)),
            new PtSlabBand(R(10001m), R(15000m), R(110m)),
            new PtSlabBand(R(15001m), R(25000m), R(130m)),
            new PtSlabBand(R(25001m), R(40000m), R(150m)),
            new PtSlabBand(R(40001m), null, R(200m)),
        });

        return new[] { mhMale, mhFemale, ka, wb };
    }
}
