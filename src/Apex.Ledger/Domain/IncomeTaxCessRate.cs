namespace Apex.Ledger.Domain;

/// <summary>
/// 🔴 <b>A company's own Health &amp; Education Cess rate, effective from a date</b> — the storage half of the user's
/// ruling on the 4% cess.
///
/// <para><b>WHAT THE RULING OWED, AND WHERE EACH PART LIVES.</b> The rate had to gain three properties.
/// <b>(a) Effective-from dating</b>, so a past payroll re-computes on the rate that was live <i>when it ran</i> —
/// that is <see cref="EffectiveFrom"/>, and <c>Company.ResolveIncomeTaxCessRate</c> picks the latest row at or before
/// the payroll period-end date. <b>(b) Per-company editability from the keyboard</b> — this row is created from
/// <b>F11 Features → Payroll Statutory → Income Tax</b>, beside the §192 switch it modifies.
/// <b>(c) A 4% default, so NO EXISTING BOOK CHANGES BEHAVIOUR ON UPGRADE</b> — which is why there is no row here by
/// default and no back-fill anywhere in the v64 migration. <b>Absence means "the statutory rate"</b>, and the
/// statutory rate is resolved from <c>SalaryTaxRates</c>, which publishes 4% for every year it can source. A book
/// that never opens this screen therefore computes to the same paisa after the upgrade as before it, and that
/// equality is asserted directly by a test rather than argued for here.</para>
///
/// <para><b>Why a dated ROW rather than a column on the company.</b> A single column could carry today's rate but not
/// the rate a March-2025 payslip was computed on, so re-opening an old period would silently re-price it with a
/// newer rate — the same class of defect as T1-26 itself, one level down. A row per effective date is the smallest
/// shape that makes a historical re-computation reproducible.</para>
///
/// <para><b>Rate is held in BASIS POINTS</b> (10 000 = 100%, so 4% = 400), matching every other rate this codebase
/// persists. Integer basis points keep the stored value exact and let two rates be compared for equality.</para>
/// </summary>
public sealed class IncomeTaxCessRate
{
    /// <summary>Stable identity (also the persisted primary key).</summary>
    public Guid Id { get; }

    /// <summary>The first date this rate applies to. A payroll period is priced on the row with the greatest
    /// <see cref="EffectiveFrom"/> that is ≤ the period-end date.</summary>
    public DateOnly EffectiveFrom { get; }

    /// <summary>The cess rate in basis points — 400 = 4%.</summary>
    public int RateBasisPoints { get; }

    /// <summary>The rate as a decimal fraction (400 bp ⇒ 0.04m), the form the tax engine multiplies by.</summary>
    public decimal Rate => RateBasisPoints / 10_000m;

    /// <summary>The statutory rate every year this build can source publishes: 4% = 400 basis points.</summary>
    public const int StatutoryRateBasisPoints = 400;

    /// <summary>Creates a dated cess rate.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The rate is negative or above 100% (10 000 bp).</exception>
    public IncomeTaxCessRate(Guid id, DateOnly effectiveFrom, int rateBasisPoints)
    {
        if (rateBasisPoints < 0 || rateBasisPoints > 10_000)
            throw new ArgumentOutOfRangeException(
                nameof(rateBasisPoints), rateBasisPoints, "A cess rate must be between 0 and 10000 basis points (0% – 100%).");
        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        EffectiveFrom = effectiveFrom;
        RateBasisPoints = rateBasisPoints;
    }
}
