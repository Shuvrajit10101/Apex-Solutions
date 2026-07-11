namespace Apex.Ledger.Domain;

/// <summary>
/// The company-level <b>TCS collector</b> configuration captured on F11 "Enable TCS" (Phase 7 slice 1; mirrors
/// <see cref="GstConfig"/>/<see cref="TdsConfig"/>). A company with no <see cref="TcsConfig"/> (or with
/// <see cref="Enabled"/> false) is a non-TCS company — byte-for-byte unchanged (ER-13). When enabled it carries
/// the collector's <see cref="Tan"/> (a collector uses the same TAN as a deductor), collector type, responsible
/// person, surcharge/cess flags, periodicity (Form 27EQ), applicable-from date, and the seeded
/// <see cref="NaturesOfGoods"/> (§206C masters, incl. the legacy year-gated 206C(1H)). The auto-created
/// "TCS Payable" ledger lives on the company's ledger set (created by <c>TdsTcsService.EnableTcs</c>).
/// </summary>
/// <remarks>Mutable master hung off <see cref="Company"/> as a nullable reference. The deductor/collector identity
/// is shared with <see cref="TdsConfig"/> and persisted once on the company row. Framework- and DB-agnostic.</remarks>
public sealed class TcsConfig
{
    private readonly List<NatureOfGoods> _naturesOfGoods = new();

    /// <summary>Whether TCS is enabled for the company.</summary>
    public bool Enabled { get; set; }

    /// <summary>The collector's TAN (validated per <see cref="Domain.Tan"/> when set); <c>null</c> when unset.</summary>
    public string? Tan { get; set; }

    /// <summary>The collector's legal status.</summary>
    public DeductorType CollectorType { get; set; } = DeductorType.Company;

    /// <summary>The name of the person responsible for collection; <c>null</c> when unset.</summary>
    public string? ResponsiblePersonName { get; set; }

    /// <summary>The responsible person's PAN (validated per <see cref="Pan"/> when set); <c>null</c> when unset.</summary>
    public string? ResponsiblePersonPan { get; set; }

    /// <summary>The responsible person's designation; <c>null</c> when unset.</summary>
    public string? ResponsiblePersonDesignation { get; set; }

    /// <summary>The responsible person's address; <c>null</c> when unset.</summary>
    public string? ResponsiblePersonAddress { get; set; }

    /// <summary>Whether surcharge applies (forward-compat seam for Phase 7 slice 5).</summary>
    public bool SurchargeApplicable { get; set; }

    /// <summary>Whether health &amp; education cess applies (forward-compat seam).</summary>
    public bool CessApplicable { get; set; }

    /// <summary>Return-filing periodicity (Form 27EQ — quarterly).</summary>
    public TdsTcsPeriodicity Periodicity { get; set; } = TdsTcsPeriodicity.Quarterly;

    /// <summary>The date TCS applies from; <c>null</c> when unset.</summary>
    public DateOnly? ApplicableFrom { get; set; }

    /// <summary>The seeded, config-driven Nature-of-Goods (§206C) masters.</summary>
    public IReadOnlyList<NatureOfGoods> NaturesOfGoods => _naturesOfGoods;

    /// <summary>Adds a Nature-of-Goods master (used by the seed on enable / by import).</summary>
    public void AddNatureOfGoods(NatureOfGoods nature) =>
        _naturesOfGoods.Add(nature ?? throw new ArgumentNullException(nameof(nature)));

    /// <summary>
    /// Validates the enabled config (fail-fast, ER-6): a valid TAN (required when enabled) and, when set, a valid
    /// responsible-person PAN. A disabled config validates trivially.
    /// </summary>
    public void EnsureValid()
    {
        if (!Enabled) return;

        if (string.IsNullOrWhiteSpace(Tan))
            throw new ArgumentException("Enabling TCS requires the collector's TAN.");
        Domain.Tan.Validate(Tan);

        if (ResponsiblePersonPan is not null)
            Pan.Validate(ResponsiblePersonPan);
    }
}
