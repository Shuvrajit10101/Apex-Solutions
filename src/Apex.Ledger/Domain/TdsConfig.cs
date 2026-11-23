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
    /// Census 6.28 (wave 12, track hf) — <b>ALTER</b>: replaces the master whose <see cref="NatureOfPayment.Id"/>
    /// matches <paramref name="replacement"/>, <b>in place</b> (same list position, so the persisted
    /// <c>rowid</c> order and every list built from it are unchanged).
    ///
    /// <para>Refuses (with <see cref="InvalidOperationException"/>) an unknown id, a replacement whose
    /// <see cref="NatureOfPayment.IsPredefined"/> differs from the original (an alteration cannot turn a seeded
    /// statutory section into a custom one or back), and a section code another master already carries. The
    /// referential rules — what may change once vouchers are posted — live in
    /// <c>Services.NatureOfPaymentRules</c>, not here, because they need the whole <see cref="Company"/>.</para>
    ///
    /// <para><b>No schema.</b> The store writes this list by delete-and-reinsert on every save
    /// (<c>SqliteCompanyStore</c>: <c>DELETE FROM nature_of_payment</c>, then one <c>INSERT</c> per master), so a
    /// replaced instance is persisted exactly as a created one is.</para>
    /// </summary>
    public void ReplaceNatureOfPayment(NatureOfPayment replacement)
    {
        ArgumentNullException.ThrowIfNull(replacement);
        var index = _naturesOfPayment.FindIndex(n => n.Id == replacement.Id);
        if (index < 0)
            throw new InvalidOperationException($"Nature of Payment {replacement.Id} not found.");
        var existing = _naturesOfPayment[index];
        if (existing.IsPredefined != replacement.IsPredefined)
            throw new InvalidOperationException(
                $"Nature of Payment '{existing.SectionCode}': an alteration cannot change whether a section is "
                + "predefined or custom.");
        if (_naturesOfPayment.Any(n => n.Id != replacement.Id
                                       && string.Equals(n.SectionCode, replacement.SectionCode,
                                           StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException($"A Nature of Payment '{replacement.SectionCode}' already exists.");
        _naturesOfPayment[index] = replacement;
    }

    /// <summary>
    /// Census 6.28 (wave 12, track hf) — <b>DELETE</b>: removes the master with this id. Returns <c>false</c>
    /// when no such master exists. <b>This is the raw list operation and carries no guard</b>: every caller must
    /// first ask <c>Services.NatureOfPaymentRules.EnsureDeletable</c>, which refuses a predefined section and any
    /// master a ledger or a posted TDS line still points at (the store has no foreign key on either reference, so
    /// nothing below this line would stop an orphan).
    /// </summary>
    public bool RemoveNatureOfPayment(Guid id) => _naturesOfPayment.RemoveAll(n => n.Id == id) > 0;

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
