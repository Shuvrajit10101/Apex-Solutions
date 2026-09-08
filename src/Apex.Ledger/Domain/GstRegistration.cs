namespace Apex.Ledger.Domain;

/// <summary>
/// An <b>additional</b> GST registration held by the company — a second (or third …) GSTIN in another State/UT,
/// alongside the one the company already carries on <see cref="GstConfig"/> (census row 6.23).
///
/// <para><b>R7 — VENDOR-ATTESTED, AND THE ARCHITECTURE WAS THE ATTESTED ONE, NOT THE CHEAP ONE.</b>
/// <c>help.tallysolutions.com/set-up-gst-details-in-company/</c> states that the product has "<i>the facility of
/// maintaining a single Company data with multiple GST registrations, while enabling you to record transactions
/// and view report for any specific GST registration or all GST registrations</i>", reached by setting
/// "<i>Create another GST Registration for the Company</i>" to <i>Yes</i>. The same page names the fields this
/// class carries — <i>State</i>, <i>GSTIN/UIN</i> ("<i>Enter your 15-digit GST number</i>"),
/// <i>Registration Type</i> ("<i>Choose Regular, Composition, Regular – SEZ</i>"), <i>Periodicity of GSTR-1</i>
/// ("<i>Choose Monthly or Quarterly</i>"), <i>Assessee of Other Territory</i> and <i>Registration Name</i>
/// ("<i>TallyPrime auto-generates the name based on the State mentioned in the Company details, for example
/// Karnataka Registration</i>" — which is exactly what <see cref="DefaultNameFor"/> reproduces). The voucher-side
/// selection is a different page, <c>help.tallysolutions.com/tally-prime/gst-regular-sales/gst-sales-tally/</c>,
/// verbatim: "<i>For TallyPrime 3.0 &amp; later, if you have multiple registrations, press F3 (Company/Tax
/// Registration) and select the registration under which you want to create the voucher.</i>" That page also
/// dates the capability — "<i>For TallyPrime 2.1 or earlier, multiple registrations under the same company are
/// not supported</i>" — and confirms the shape: "<i>If you have multiple GST registrations, you may configure
/// GST details for all registrations under the same company.</i>"</para>
///
/// <para>🔴 <b>SO IT IS EXPLICITLY <i>NOT</i> "ONE COMPANY PER GSTIN".</b> The vendor keeps the registrations
/// inside ONE company's data, which is why this is a collection on <see cref="GstConfig"/> and why
/// <see cref="Voucher.GstRegistrationId"/> exists at all: GST returns are filed <b>per registration</b>, so every
/// voucher must attribute to exactly one, and the attribution cannot be derived after the fact from the party's
/// State (an inter-State sale out of the Karnataka registration and one out of the Maharashtra registration can
/// name the same customer).</para>
/// </summary>
/// <remarks>
/// <para>🔴 <b>WHY THE FIRST REGISTRATION IS NOT A ROW IN THIS COLLECTION — READ THIS BEFORE CHANGING IT.</b>
/// Every GST-enabled book that already exists stores its one registration in <see cref="GstConfig"/>'s scalar
/// fields (<see cref="GstConfig.Gstin"/>, <see cref="GstConfig.HomeStateCode"/>,
/// <see cref="GstConfig.RegistrationType"/>, <see cref="GstConfig.Periodicity"/>), and the whole GST engine —
/// place-of-supply routing, the returns, e-invoicing, e-Way — reads them there. Promoting that block into a row
/// of this collection would have created <b>two</b> storage sites for the company's own GSTIN and required a
/// data-moving back-fill across every shipped book, on the one field whose value decides whether a filed return
/// is correct. Instead the first registration <b>stays exactly where it is</b> and is surfaced through this same
/// type by <see cref="GstConfig.PrimaryRegistration"/>, carrying the reserved id <see cref="PrimaryId"/>
/// (<see cref="System.Guid.Empty"/>). This collection holds the <i>additional</i> registrations only.</para>
///
/// <para><b>The consequence, stated plainly, is the back-fill contract the briefing demanded.</b> A voucher
/// attributes to the primary registration when <see cref="Voucher.GstRegistrationId"/> is <c>null</c> or
/// <see cref="PrimaryId"/>. Every voucher in every pre-v61 book therefore already attributes to the registration
/// that book has always had — <b>the back-fill is a no-op, and is exact by construction rather than by a
/// statement that ran correctly once.</b> There is no <c>UPDATE vouchers SET …</c> anywhere in
/// <c>Schema.MigrateV60ToV61</c>, and so no way for it to have mis-stamped a row.</para>
///
/// <para><b>ER-13.</b> A company that never adds a second registration has an empty collection, an all-NULL
/// <c>vouchers.gst_registration_id</c> column and byte-identical returns — <see cref="GstConfig.IsMultiRegistration"/>
/// is false and <c>GstReportSupport</c> takes exactly the path it took at v60.</para>
///
/// <para>Immutable value object, constructed validated (fail-fast, ER-6). Framework- and DB-agnostic.</para>
/// </remarks>
public sealed class GstRegistration
{
    /// <summary>
    /// The reserved id of the company's <b>first</b> registration — the one held in <see cref="GstConfig"/>'s own
    /// scalar fields rather than in the additional-registration collection. A voucher carrying this id (or
    /// <c>null</c>) attributes to it. No row in the collection may use it; <see cref="GstConfig.EnsureValid"/>
    /// rejects one that tries.
    /// </summary>
    public static readonly Guid PrimaryId = Guid.Empty;

    /// <summary>Stable identity. <see cref="PrimaryId"/> only for the synthesised primary registration.</summary>
    public Guid Id { get; }

    /// <summary>
    /// The registration's name — the vendor's <i>Registration Name</i>, which it "<i>auto-generates … based on the
    /// State</i>" as e.g. <i>Karnataka Registration</i> (see <see cref="DefaultNameFor"/>) and lets the operator
    /// override. This is the label the F3 picker and the per-registration report header show, so it is required
    /// and non-blank.
    /// </summary>
    public string Name { get; }

    /// <summary>The registration's 2-digit GST State/UT code — the supplier location for place of supply on every
    /// voucher attributed to it. Validated against <see cref="IndianState"/>.</summary>
    public string StateCode { get; }

    /// <summary>The 15-character GSTIN/UIN, validated per <see cref="Gstin"/>; <c>null</c> for a registration type
    /// that carries none.</summary>
    public string? Gstin { get; }

    /// <summary>Regular / Composition / … — the vendor's <i>Registration Type</i>.</summary>
    public GstRegistrationType RegistrationType { get; }

    /// <summary>The date this registration applies from; <c>null</c> when unset.</summary>
    public DateOnly? ApplicableFrom { get; }

    /// <summary>The GSTR-1 (and paired 3B) periodicity elected for <b>this</b> registration. Deliberately
    /// per-registration rather than per-company: the returns are filed separately, so the elections are separate.</summary>
    public GstReturnPeriodicity Periodicity { get; }

    /// <summary>The vendor's <i>Assessee of Other Territory</i> flag ("<i>Enable only if your business is in other
    /// territory</i>"). Captured per registration.</summary>
    public bool AssesseeOfOtherTerritory { get; }

    /// <summary>The <see cref="IndianState"/> this registration is in, or <c>null</c> if the code is unrecognised.</summary>
    public IndianState? State => IndianState.FromCode(StateCode);

    /// <summary>True for the synthesised first registration (<see cref="GstConfig.PrimaryRegistration"/>).</summary>
    public bool IsPrimary => Id == PrimaryId;

    /// <summary>
    /// The vendor's auto-generated registration name for a State code — "<i>Karnataka Registration</i>" for
    /// <c>29</c>. Falls back to a code-shaped label when the code is unrecognised, so the picker never shows a
    /// blank row.
    /// </summary>
    public static string DefaultNameFor(string? stateCode) =>
        IndianState.FromCode(stateCode) is { } s ? $"{s.Name} Registration" : "GST Registration";

    public GstRegistration(
        Guid id, string name, string stateCode, string? gstin,
        GstRegistrationType registrationType = GstRegistrationType.Regular,
        DateOnly? applicableFrom = null,
        GstReturnPeriodicity periodicity = GstReturnPeriodicity.Monthly,
        bool assesseeOfOtherTerritory = false)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("A GST registration requires a name.", nameof(name));
        if (!IndianState.IsValidCode(stateCode))
            throw new ArgumentException(
                $"GST registration state code '{stateCode}' is not a valid Indian State/UT code.", nameof(stateCode));
        if (gstin is not null)
            Domain.Gstin.Validate(gstin);

        // A registered person is a registered person whichever State the registration is in — the same rule
        // GstConfig.EnsureValid applies to the first registration, applied here so the second cannot be weaker.
        if (registrationType is GstRegistrationType.Regular or GstRegistrationType.Composition && gstin is null)
            throw new ArgumentException($"A {registrationType} GST registration requires a GSTIN.", nameof(gstin));

        Id = id;
        Name = name.Trim();
        StateCode = stateCode;
        Gstin = gstin;
        RegistrationType = registrationType;
        ApplicableFrom = applicableFrom;
        Periodicity = periodicity;
        AssesseeOfOtherTerritory = assesseeOfOtherTerritory;
    }
}
