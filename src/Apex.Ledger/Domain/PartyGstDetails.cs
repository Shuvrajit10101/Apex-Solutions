namespace Apex.Ledger.Domain;

/// <summary>
/// Optional GST details on a party (Sundry Debtor/Creditor) ledger (catalog §12; phase4 RQ-7). The party's
/// <see cref="StateCode"/> is the place-of-supply driver for goods delivered to the party (DP-7), and a
/// party with <b>no GSTIN</b> (Unregistered/Consumer) is a <b>B2C</b> party (DP-8). A ledger with no
/// <see cref="PartyGstDetails"/> is treated as an unregistered/B2C party for GST purposes.
/// </summary>
/// <remarks>
/// Mutable value object hung off <see cref="Ledger"/> as a nullable reference (mirroring
/// <see cref="InterestParameters"/>). Framework- and DB-agnostic.
/// </remarks>
public sealed class PartyGstDetails
{
    /// <summary>The party's registration type (Regular / Unregistered / Consumer; Composition stored but inert).</summary>
    public GstRegistrationType RegistrationType { get; set; } = GstRegistrationType.Unregistered;

    /// <summary>The party GSTIN/UIN (validated per <see cref="Gstin"/> when set); <c>null</c> ⇒ B2C.</summary>
    public string? Gstin { get; set; }

    /// <summary>The party's State/UT 2-digit GST code (the place of supply for goods); <c>null</c> when unset.</summary>
    public string? StateCode { get; set; }

    // ---- Phase 9 slice 2: reverse-charge (RCM) qualifiers (RQ-3). Default false so a party with no RCM profile is
    // byte-identical to a v38 party (ER-13); the blanket §9(4) stays OFF (promoter-only, Notn 7/2019). ----

    /// <summary>True iff this party is a real-estate <b>promoter</b> — the sole surviving §9(4) trigger (Notn 7/2019).
    /// Default false, so a company with no promoter profile leaves §9(4) OFF.</summary>
    public bool IsPromoter { get; set; }

    /// <summary>True iff this party is a <b>body corporate</b> — drives the recipient/supplier qualifier match (e.g. GTA /
    /// security / renting-of-motor-vehicle RCM shifts to a body-corporate recipient). Default false.</summary>
    public bool IsBodyCorporate { get; set; }

    /// <summary>
    /// True iff this is a B2C party — no GSTIN, or a registration type of Unregistered/Consumer. B2C supplies
    /// go to the B2C section of GSTR-1 (DP-8) but pay CGST+SGST/IGST normally by place of supply.
    /// </summary>
    public bool IsB2C =>
        string.IsNullOrWhiteSpace(Gstin)
        || RegistrationType is GstRegistrationType.Unregistered or GstRegistrationType.Consumer;

    /// <summary>The party's <see cref="IndianState"/>, or <c>null</c> if unset/invalid.</summary>
    public IndianState? State => IndianState.FromCode(StateCode);

    /// <summary>
    /// Validates the details: a valid GSTIN (when set) and a recognised state code (when set). A Regular
    /// party must carry a GSTIN. Throws <see cref="ArgumentException"/> on a bad value (fail-fast, ER-6).
    /// </summary>
    public void EnsureValid()
    {
        if (Gstin is not null)
            Domain.Gstin.Validate(Gstin);

        if (StateCode is not null && !IndianState.IsValidCode(StateCode))
            throw new ArgumentException($"Party GST state code '{StateCode}' is not a valid Indian State/UT code.");

        if (RegistrationType == GstRegistrationType.Regular && string.IsNullOrWhiteSpace(Gstin))
            throw new ArgumentException("A Regular GST party requires a GSTIN.");
    }
}
