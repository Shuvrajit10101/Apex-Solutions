namespace Apex.Ledger.Domain;

/// <summary>
/// A <b>GST Classification</b> — a named, reusable bundle of HSN/SAC and GST-rate details that a whole category of
/// goods or services shares, so the operator keys the rate once and points many masters at it (census row 6.25).
///
/// <para><b>R7 — VENDOR-ATTESTED.</b>
/// <c>help.tallysolutions.com/tally-prime/gst-master-setup/india-gst-create-use-and-update-gst-classifications-tally/</c>
/// defines it verbatim: "<i>GST Classifications in TallyPrime are a powerful tool for recording tax rates and other
/// details for categories of goods and services that attract a common GST rate.</i>" The same page gives the menu
/// route this slice reproduces — <i>Gateway of Tally &gt; Create &gt; type or select GST Classification &gt; press
/// Enter</i>, and the equivalent <i>Alt+G (Go To) &gt; Create Master &gt; GST Classification</i> — and enumerates
/// the creation screen in the two sections this class mirrors: <b>HSN/SAC &amp; Related Details</b> (<i>HSN/SAC</i>,
/// <i>Description</i>, <i>Nature of Transaction</i> — the page marks it optional) and <b>GST Rate &amp; Related
/// Details</b> (<i>Integrated Tax rate</i>, <i>Central Tax</i> and <i>State Tax</i> both auto-calculated, <i>Cess</i>,
/// <i>Cess Valuation Type</i> "<i>based on value and/or quantity</i>", <i>Cess Rate per Unit</i>).</para>
///
/// <para>🔴 <b>THIS IS A GENUINELY NEW MASTER, NOT A PARTIAL VERSION OF SOMETHING THAT SHIPPED.</b> The census row
/// is emphatic and it was re-measured for this slice: the <c>GstClassification</c> hits that already exist in
/// <c>src/Apex.Desktop</c> are all the engine-managed <see cref="LedgerGstClassification"/> value object (a tax
/// head / direction / RCM tag on a <i>Duties &amp; Taxes</i> ledger, deliberately excluded from user editing). It
/// is a different thing that happens to share a word. Nothing here replaces it and nothing there is reused.</para>
///
/// <para>🔴 <b>AND IT SETTLES AN OPEN CONTRADICTION IN <see cref="MasterGstDetails"/>.</b> That type's remarks
/// record the corpus listing five GST <i>methods</i>, the fifth being "<i>Creating GST Classification</i>", and
/// note that the project <b>excluded</b> it (plan.md orchestrator ruling 3) on the express ground that "<i>no
/// GST-Classification master exists in <c>src/</c></i>". That ground was a statement about our code, not about the
/// product. The vendor page above attests the master directly, so the exclusion no longer holds and the master is
/// built. <b>The five-level resolution hierarchy is NOT changed by this slice</b> — see the scope limit below.</para>
/// </summary>
/// <remarks>
/// <para>🔴 <b>SCOPE, STATED HONESTLY: THIS SLICE SHIPS THE MASTER AND ITS APPLICATION, NOT A SIXTH HIERARCHY
/// LEVEL.</b> The vendor applies a classification by <i>assignment</i> — "<i>Select the items … Press Alt+S (Set
/// Rate), and select the GST Classification</i>" on the GST Rate Setup report — after which "<i>tax details
/// automatically populate from the classification rather than manual entry</i>". That is a <b>copy at assignment
/// time</b> into the master's own GST block, which is exactly what <see cref="ToDetails"/> produces and what the
/// screen writes. It is deliberately NOT a live sixth level consulted during rate resolution: no vendor page
/// states where a classification would sit in the documented HSN/SAC-and-rate hierarchy, and inventing a
/// precedence for it would change resolved tax on existing books to answer a question no source asked. Every
/// figure a v60 book computes is unchanged by this slice.</para>
///
/// <para><b>Nature of Transaction is CAPTURED, and captured is all it is.</b> The vendor names the field and marks
/// it optional; it publishes no enumeration of its values on this page and states no arithmetic that follows from
/// one. It is therefore stored as free text and carried onto the master it is assigned to as a label. <b>No
/// computation reads it</b> — recording that here rather than guessing a value set follows the standing rule that
/// an honest divergence beats a fabricated flow, and matches how three Group flags shipped
/// captured-and-inert in the previous wave.</para>
///
/// <para><b>Central and State tax are derived, never stored.</b> The vendor shows them auto-calculated from the
/// integrated rate, so <see cref="CentralTaxBasisPoints"/> and <see cref="StateTaxBasisPoints"/> are computed
/// halves of <see cref="RateBasisPoints"/> rather than two more columns that could disagree with it. An odd
/// integrated rate in basis points splits with the remainder on the Central half, which is the only split that
/// keeps CGST + SGST == IGST exactly.</para>
///
/// <para>Immutable value object, constructed validated (fail-fast, ER-6). Framework- and DB-agnostic.</para>
/// </remarks>
public sealed class GstClassification
{
    /// <summary>Stable identity.</summary>
    public Guid Id { get; }

    /// <summary>The classification's name — the label the operator picks it by. Required and unique per company
    /// (case-insensitively; enforced by <see cref="GstConfig.EnsureValid"/>).</summary>
    public string Name { get; }

    /// <summary>The HSN (goods) / SAC (services) code — 4, 6 or 8 digits; <c>null</c> when unset.</summary>
    public string? HsnSac { get; }

    /// <summary>The vendor's <i>Description</i> free-text field; <c>null</c> when unset.</summary>
    public string? Description { get; }

    /// <summary>Whether this category is taxable / exempt / nil-rated / non-GST.</summary>
    public GstTaxability Taxability { get; }

    /// <summary>The <b>integrated</b> tax rate in basis points (1800 = 18%); <c>null</c> ⇒ the classification
    /// declares no rate.</summary>
    public int? RateBasisPoints { get; }

    /// <summary>Goods (HSN) or Services (SAC).</summary>
    public GstSupplyType SupplyType { get; }

    /// <summary>
    /// The vendor's optional <i>Nature of Transaction</i> label. 🔴 <b>CAPTURED AND INERT</b> — stored and shown,
    /// read by no computation. See the scope note on the class.
    /// </summary>
    public string? NatureOfTransaction { get; }

    /// <summary>How Compensation Cess is valued for this category, when it bears any.</summary>
    public CessValuationMode CessValuationMode { get; }

    /// <summary>The ad-valorem cess rate in basis points; 0 unless <see cref="CessValuationMode.AdValorem"/>.</summary>
    public int CessRateBasisPoints { get; }

    /// <summary>The vendor's <i>Cess Rate per Unit</i> — a specific per-unit cess amount, paisa-exact; zero unless
    /// <see cref="CessValuationMode.Specific"/>.</summary>
    public Money CessPerUnit { get; }

    /// <summary>The auto-calculated <b>Central</b> tax half of <see cref="RateBasisPoints"/>, in basis points
    /// (derived, never stored). Carries the odd basis point so CGST + SGST == IGST exactly.</summary>
    public int? CentralTaxBasisPoints => RateBasisPoints is { } bp ? bp - bp / 2 : null;

    /// <summary>The auto-calculated <b>State</b> tax half of <see cref="RateBasisPoints"/>, in basis points
    /// (derived, never stored).</summary>
    public int? StateTaxBasisPoints => RateBasisPoints is { } bp ? bp / 2 : null;

    /// <summary>True iff this classification is taxable (attracts tax when a rate is declared).</summary>
    public bool IsTaxable => Taxability == GstTaxability.Taxable;

    public GstClassification(
        Guid id, string name, string? hsnSac = null, string? description = null,
        GstTaxability taxability = GstTaxability.Taxable, int? rateBasisPoints = null,
        GstSupplyType supplyType = GstSupplyType.Goods, string? natureOfTransaction = null,
        CessValuationMode cessValuationMode = CessValuationMode.AdValorem,
        int cessRateBasisPoints = 0, Money? cessPerUnit = null)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("A GST classification requires a name.", nameof(name));

        // The SAME three rules MasterGstDetails.EnsureValid applies to the same fields — an HSN or a rate that
        // would be rejected on a Stock Group must not be accepted here and then COPIED onto one by ToDetails().
        if (hsnSac is not null)
        {
            var hsn = hsnSac.Trim();
            if (hsn.Length is not (4 or 6 or 8) || !hsn.All(char.IsDigit))
                throw new ArgumentException($"HSN/SAC '{hsnSac}' must be 4, 6 or 8 digits (numeric).", nameof(hsnSac));
        }

        if (rateBasisPoints is < 0)
            throw new ArgumentException("GST rate basis points must be ≥ 0 when set.", nameof(rateBasisPoints));

        if (rateBasisPoints is { } r && taxability != GstTaxability.Taxable && r > 0)
            throw new ArgumentException(
                $"A {taxability} GST classification must not carry a positive GST rate ({r} bp).", nameof(rateBasisPoints));

        if (cessRateBasisPoints < 0)
            throw new ArgumentException("Cess rate basis points must be ≥ 0.", nameof(cessRateBasisPoints));

        var cess = cessPerUnit ?? Money.Zero;
        if (cess.Amount < 0)
            throw new ArgumentException("Cess per unit must be ≥ 0.", nameof(cessPerUnit));

        Id = id;
        Name = name.Trim();
        HsnSac = hsnSac;
        Description = description;
        Taxability = taxability;
        RateBasisPoints = rateBasisPoints;
        SupplyType = supplyType;
        NatureOfTransaction = natureOfTransaction;
        CessValuationMode = cessValuationMode;
        CessRateBasisPoints = cessRateBasisPoints;
        CessPerUnit = cess;
    }

    /// <summary>
    /// The <see cref="MasterGstDetails"/> block this classification stamps onto a master when it is assigned to it
    /// — the vendor's "<i>tax details automatically populate from the classification</i>". A <b>copy</b>, taken at
    /// assignment time: the master owns its block afterwards, and later editing the classification does not
    /// silently re-rate documents already posted against masters assigned from it.
    /// </summary>
    public MasterGstDetails ToDetails() => new()
    {
        HsnSac = HsnSac,
        Taxability = Taxability,
        RateBasisPoints = RateBasisPoints,
        SupplyType = SupplyType,
    };
}
