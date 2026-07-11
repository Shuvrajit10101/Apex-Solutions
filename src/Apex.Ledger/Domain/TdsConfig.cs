namespace Apex.Ledger.Domain;

/// <summary>
/// The company-level <b>TDS deductor</b> configuration captured on F11 "Enable TDS" (Phase 7 slice 1; mirrors
/// <see cref="GstConfig"/>). A company with no <see cref="TdsConfig"/> (or with <see cref="Enabled"/> false) is a
/// non-TDS company — every existing path is byte-for-byte unchanged (ER-13). When enabled it carries the
/// deductor's <see cref="Tan"/>, deductor type, the person responsible for deduction, the surcharge/cess flags,
/// the return periodicity, the applicable-from date, and the seeded <see cref="NaturesOfPayment"/> (the TDS
/// section masters). The auto-created "TDS Payable" ledger lives on the company's ledger set (created by
/// <c>TdsTcsService.EnableTds</c>), not here.
/// </summary>
/// <remarks>Mutable master hung off <see cref="Company"/> as a nullable reference (mirroring <see cref="GstConfig"/>).
/// The deductor identity (TAN/type/responsible person/surcharge/cess/periodicity) is shared with
/// <see cref="TcsConfig"/> — a company files 26Q and 27EQ under the same TAN — and is persisted once on the company
/// row. Framework- and DB-agnostic; unit-testable.</remarks>
public sealed class TdsConfig
{
    private readonly List<NatureOfPayment> _naturesOfPayment = new();

    /// <summary>Whether TDS is enabled for the company. When false, no TDS field or report is active.</summary>
    public bool Enabled { get; set; }

    /// <summary>The deductor's TAN (validated per <see cref="Domain.Tan"/> when set); <c>null</c> when unset.</summary>
    public string? Tan { get; set; }

    /// <summary>The deductor's legal status (drives 26Q deductor block).</summary>
    public DeductorType DeductorType { get; set; } = DeductorType.Company;

    /// <summary>The name of the person responsible for deduction; <c>null</c> when unset.</summary>
    public string? ResponsiblePersonName { get; set; }

    /// <summary>The responsible person's PAN (validated per <see cref="Pan"/> when set); <c>null</c> when unset.</summary>
    public string? ResponsiblePersonPan { get; set; }

    /// <summary>The responsible person's designation; <c>null</c> when unset.</summary>
    public string? ResponsiblePersonDesignation { get; set; }

    /// <summary>The responsible person's address; <c>null</c> when unset.</summary>
    public string? ResponsiblePersonAddress { get; set; }

    /// <summary>Whether surcharge applies to the deductor's computations (forward-compat seam for Phase 7 slice 2).</summary>
    public bool SurchargeApplicable { get; set; }

    /// <summary>Whether health &amp; education cess applies (forward-compat seam).</summary>
    public bool CessApplicable { get; set; }

    /// <summary>Return-filing periodicity (Form 26Q — quarterly).</summary>
    public TdsTcsPeriodicity Periodicity { get; set; } = TdsTcsPeriodicity.Quarterly;

    /// <summary>The date TDS applies from; <c>null</c> when unset.</summary>
    public DateOnly? ApplicableFrom { get; set; }

    /// <summary>The seeded, config-driven Nature-of-Payment (TDS section) masters.</summary>
    public IReadOnlyList<NatureOfPayment> NaturesOfPayment => _naturesOfPayment;

    /// <summary>Adds a Nature-of-Payment master (used by the seed on enable / by import).</summary>
    public void AddNatureOfPayment(NatureOfPayment nature) =>
        _naturesOfPayment.Add(nature ?? throw new ArgumentNullException(nameof(nature)));

    /// <summary>
    /// Validates the enabled config (fail-fast, ER-6): a valid TAN (required when enabled) and, when set, a valid
    /// responsible-person PAN. A disabled config validates trivially.
    /// </summary>
    public void EnsureValid()
    {
        if (!Enabled) return;

        if (string.IsNullOrWhiteSpace(Tan))
            throw new ArgumentException("Enabling TDS requires the deductor's TAN.");
        Domain.Tan.Validate(Tan);

        if (ResponsiblePersonPan is not null)
            Pan.Validate(ResponsiblePersonPan);
    }
}
