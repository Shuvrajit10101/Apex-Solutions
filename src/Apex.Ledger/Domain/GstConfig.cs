using System.Text.RegularExpressions;

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
    private readonly List<GstRateHistoryEntry> _rateHistory = new();
    private readonly List<GstCessRate> _cessRates = new();
    private readonly List<RcmCategory> _rcmCategories = new();

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

    /// <summary>
    /// The composition sub-type (Phase 9 slice 3; RQ-4). Non-null only when <see cref="RegistrationType"/> is
    /// Composition; drives the tax-on-turnover rate + base (<see cref="CompositionThreshold"/>). Null for a Regular
    /// company (byte-identical, ER-13).
    /// </summary>
    public CompositionSubType? CompositionSubType { get; set; }

    /// <summary>The date the dealer opted into composition (CMP-02); advisory (period-scoping), null when unset.</summary>
    public DateOnly? CompositionOptInDate { get; set; }

    // --- e-Invoicing (Phase 9 slice 4a; RQ-5). All default off/null ⇒ an e-invoicing-off company is byte-identical
    //     to a v40 company (ER-13). NO secret field lives here — NIC credentials flow ONLY through INicCredentialStore
    //     (ER-16), so the pure CanonicalMapper cannot serialise a secret. ---

    /// <summary>F11 master gate: whether e-invoicing (IRN generation) is enabled for the company.</summary>
    public bool EInvoicingEnabled { get; set; }

    /// <summary>The date e-invoicing applies from; a covered document dated before this is Not-Applicable.</summary>
    public DateOnly? EInvoiceApplicableFrom { get; set; }

    /// <summary>The AATO applicability threshold (DP-11; default ₹5 cr, CONFIGURABLE — NOT ₹2 cr, which is an unnotified
    /// proposal). At/above it the company is covered; below it (and not overridden) e-invoicing is Not-Applicable.</summary>
    public Money EInvoiceAatoThreshold { get; set; } = new Money(50_000_000m); // ₹5 cr

    /// <summary>A sticky manual applicability override (a company that voluntarily e-invoices below the threshold).</summary>
    public bool EInvoiceApplicabilityOverride { get; set; }

    /// <summary>The typed e-invoicing exemptions the supplier's business class carries (SEZ unit/insurer/GTA/…). Any
    /// matching flag makes a document Exempt regardless of turnover.</summary>
    public EInvoiceExemptionClass ExemptionClasses { get; set; } = EInvoiceExemptionClass.None;

    /// <summary>The 30-day reporting-age limit applies — true ONLY when AATO ≥ ₹10 cr. This is INDEPENDENT of the ₹5 cr
    /// applicability threshold (a ₹6 cr taxpayer is covered but has no 30-day age limit).</summary>
    public bool ReportingAgeLimitApplies { get; set; }

    // --- connectivity (Phase 9 slice 4a; RQ-30) ---

    /// <summary>The GST-portal transport mode (default <see cref="GstConnectorMode.OfflineJson"/> — the zero-credential
    /// baseline, ER-16). Selected by the engine-agnostic connector at the Desktop composition root.</summary>
    public GstConnectorMode ConnectorMode { get; set; } = GstConnectorMode.OfflineJson;

    // --- B2C dynamic QR (Phase 9 slice 4a schema/config; RQ-28; the B2cQrService projection lands in S4b) ---

    /// <summary>F11 gate: whether the self-generated B2C dynamic (UPI) QR is enabled (a supplier &gt; ₹500 cr).</summary>
    public bool B2cDynamicQrEnabled { get; set; }

    /// <summary>The B2C-QR AATO threshold (DP-28; default ₹500 cr, configurable). Gated above this turnover.</summary>
    public Money B2cQrAatoThreshold { get; set; } = new Money(5_000_000_000m); // ₹500 cr

    /// <summary>The payee UPI VPA the B2C QR pays to; required when <see cref="B2cDynamicQrEnabled"/>.</summary>
    public string? B2cQrUpiId { get; set; }

    /// <summary>The payee name shown in the B2C QR; required when <see cref="B2cDynamicQrEnabled"/>.</summary>
    public string? B2cQrPayeeName { get; set; }

    /// <summary>The permitted shape of a payee UPI VPA — <c>name@handle</c> with only unreserved characters. A VPA
    /// carrying a space, <c>&amp;</c>, <c>?</c> or any other reserved character would corrupt or inject into the UPI
    /// deep link (<c>upi://pay?pa=…</c>), so it is rejected (Phase 9 slice 4b; finding #3).</summary>
    private static readonly Regex UpiVpaShape = new(@"^[A-Za-z0-9._-]+@[A-Za-z0-9.-]+$", RegexOptions.Compiled);

    /// <summary>Whether <paramref name="vpa"/> is a well-formed UPI VPA (<c>name@handle</c>, unreserved characters only).
    /// Shared by <see cref="EnsureValid"/> (fail-fast) and the B2C-QR projection (which yields no QR for a bad VPA).</summary>
    public static bool IsValidUpiVpa(string? vpa) => vpa is not null && UpiVpaShape.IsMatch(vpa);

    /// <summary>The seeded, config-driven GST rate slabs (RQ-25; Phase 4 seeds 0/5/18/40).</summary>
    public IReadOnlyList<GstRateSlab> RateSlabs => _rateSlabs;

    /// <summary>Adds a rate slab (used by the seed on enable).</summary>
    public void AddRateSlab(GstRateSlab slab) => _rateSlabs.Add(slab ?? throw new ArgumentNullException(nameof(slab)));

    /// <summary>
    /// The dated GST rate-history windows (Phase 9 slice 1; RQ-1). <b>Empty</b> for a company that never enables
    /// advanced GST — <c>ResolveRate</c> then resolves exactly as Phase-4/8 (ER-13 byte-identical when off).
    /// </summary>
    public IReadOnlyList<GstRateHistoryEntry> RateHistory => _rateHistory;

    /// <summary>Adds a dated rate-history window (used by the advanced-GST seed / import).</summary>
    public void AddRateHistory(GstRateHistoryEntry entry) =>
        _rateHistory.Add(entry ?? throw new ArgumentNullException(nameof(entry)));

    /// <summary>
    /// The dated Compensation-Cess windows (Phase 9 slice 1; RQ-2/RQ-9). <b>Empty</b> for a company that bears no
    /// cess — the cess compute is a no-op and no Cess ledger is created (ER-13 byte-identical when off).
    /// </summary>
    public IReadOnlyList<GstCessRate> CessRates => _cessRates;

    /// <summary>Adds a dated cess window (used by the advanced-GST seed / import).</summary>
    public void AddCessRate(GstCessRate rate) =>
        _cessRates.Add(rate ?? throw new ArgumentNullException(nameof(rate)));

    /// <summary>
    /// The dated notified reverse-charge categories (Phase 9 slice 2; RQ-3/RQ-7). <b>Empty</b> for a company that never
    /// enables advanced GST — no inward supply attracts reverse charge and no RCM ledger is created (ER-13
    /// byte-identical when off).
    /// </summary>
    public IReadOnlyList<RcmCategory> RcmCategories => _rcmCategories;

    /// <summary>Adds a dated reverse-charge category (used by the advanced-GST seed / import).</summary>
    public void AddRcmCategory(RcmCategory category) =>
        _rcmCategories.Add(category ?? throw new ArgumentNullException(nameof(category)));

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

        // A Composition dealer is a registered person too, so it also requires a GSTIN (Phase 9 slice 3; RQ-4).
        if (RegistrationType is GstRegistrationType.Regular or GstRegistrationType.Composition && Gstin is null)
            throw new ArgumentException($"A {RegistrationType} GST registration requires a GSTIN.");

        // A Composition registration must declare its sub-type (drives the tax-on-turnover rate + base).
        if (RegistrationType == GstRegistrationType.Composition && CompositionSubType is null)
            throw new ArgumentException("A Composition registration requires a composition sub-type.");

        // e-Invoicing (Phase 9 slice 4a): requires a GSTIN + an applicable-from date when enabled.
        if (EInvoicingEnabled)
        {
            if (Gstin is null)
                throw new ArgumentException("e-Invoicing requires a GSTIN.");
            if (EInvoiceApplicableFrom is null)
                throw new ArgumentException("e-Invoicing requires an applicable-from date.");
        }

        // B2C dynamic QR (Phase 9 slice 4a): requires the payee UPI id + name when enabled.
        if (B2cDynamicQrEnabled && (string.IsNullOrWhiteSpace(B2cQrUpiId) || string.IsNullOrWhiteSpace(B2cQrPayeeName)))
            throw new ArgumentException("B2C dynamic QR requires a payee UPI id and payee name.");

        // The payee VPA must be a well-formed UPI id — a malformed VPA would corrupt/inject into the deep link (finding #3).
        if (B2cDynamicQrEnabled && !IsValidUpiVpa(B2cQrUpiId))
            throw new ArgumentException($"B2C dynamic QR payee UPI id '{B2cQrUpiId}' is not a well-formed UPI VPA (name@handle).");
    }
}
