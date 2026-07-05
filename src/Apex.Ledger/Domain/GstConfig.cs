namespace Apex.Ledger.Domain;

/// <summary>
/// The company-level GST configuration captured on "Enable GST" (F11 → Taxation; catalog §12; phase4
/// RQ-1/RQ-2). A company with no <see cref="GstConfig"/> (or with <see cref="Enabled"/> false) is a non-GST
/// company — every existing (Phase 1/2/3) path is byte-for-byte unchanged (ER-10). When enabled it carries
/// the GSTIN, the home State/UT (place-of-supply supplier location), registration type, the
/// GST-applicable-from date, the return periodicity, and the seeded config-driven rate slabs (RQ-25).
/// </summary>
/// <remarks>
/// Mutable master hung off <see cref="Company"/> as a nullable reference (mirroring how a ledger carries an
/// optional <see cref="InterestParameters"/> block). The seeded tax ledgers and Round-Off ledger live on the
/// company's ordinary ledger set (auto-created by <c>GstService.EnableGst</c>), not here. Framework- and
/// DB-agnostic; unit-testable.
/// </remarks>
public sealed class GstConfig
{
    private readonly List<GstRateSlab> _rateSlabs = new();

    /// <summary>Whether GST is enabled for the company. When false, no GST field or report is active.</summary>
    public bool Enabled { get; set; }

    /// <summary>The company GSTIN/UIN (validated per <see cref="Gstin"/>); <c>null</c> when unset.</summary>
    public string? Gstin { get; set; }

    /// <summary>The home State/UT 2-digit GST code (supplier location for place of supply); required when enabled.</summary>
    public string? HomeStateCode { get; set; }

    /// <summary>Registration type (Phase 4: Regular is the working type; others stored but inert).</summary>
    public GstRegistrationType RegistrationType { get; set; } = GstRegistrationType.Regular;

    /// <summary>The date GST applies from; <c>null</c> when unset.</summary>
    public DateOnly? ApplicableFrom { get; set; }

    /// <summary>GSTR-1 (and paired 3B) periodicity election.</summary>
    public GstReturnPeriodicity Periodicity { get; set; } = GstReturnPeriodicity.Monthly;

    /// <summary>The seeded, config-driven GST rate slabs (RQ-25; Phase 4 seeds 0/5/18/40).</summary>
    public IReadOnlyList<GstRateSlab> RateSlabs => _rateSlabs;

    /// <summary>Adds a rate slab (used by the seed on enable).</summary>
    public void AddRateSlab(GstRateSlab slab) => _rateSlabs.Add(slab ?? throw new ArgumentNullException(nameof(slab)));

    /// <summary>The home <see cref="IndianState"/>, or <c>null</c> if the home state code is unset/invalid.</summary>
    public IndianState? HomeState => IndianState.FromCode(HomeStateCode);

    /// <summary>
    /// Validates the enabled config: a valid GSTIN (when set), a recognised home state code, and — for the
    /// working Regular type — a GSTIN present. Throws <see cref="ArgumentException"/> on a bad value (fail-fast,
    /// ER-6). A disabled config validates trivially.
    /// </summary>
    public void EnsureValid()
    {
        if (!Enabled) return;

        if (!IndianState.IsValidCode(HomeStateCode))
            throw new ArgumentException($"GST home state code '{HomeStateCode}' is not a valid Indian State/UT code.");

        if (Gstin is not null)
            Domain.Gstin.Validate(Gstin);

        if (RegistrationType == GstRegistrationType.Regular && Gstin is null)
            throw new ArgumentException("A Regular GST registration requires a GSTIN.");
    }
}
