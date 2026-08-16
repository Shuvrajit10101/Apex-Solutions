namespace Apex.Ledger.Domain;

/// <summary>
/// The <b>narrow</b> GST details block carried by the three masters that exist only to answer "what HSN/SAC and
/// what GST rate applies here?" — a <see cref="StockGroup"/>, an accounting <see cref="Group"/>, and the company
/// itself (<see cref="GstConfig.DefaultGst"/>). Together with the Stock Item and the Sales/Purchase
/// <see cref="Ledger"/> these are the <b>five levels</b> the reference application resolves GST through
/// (plan.md Phase 10.10 WF-1; register IV-1).
/// </summary>
/// <remarks>
/// <para><b>Why this is not <see cref="StockItemGstDetails"/>.</b> That block is shared by the Stock Item and the
/// Sales/Purchase ledger and carries RSP valuation, Compensation-Cess overrides, reverse-charge flags and §17(5)
/// ITC-eligibility. Every one of those is read <b>item-first</b> by its own resolver and none of them has a
/// stock-group / accounting-group / company meaning, so reusing it here would publish a dozen fields that silently
/// do nothing at three of the five levels. The four members below are exactly the fields the reference application
/// puts on a Stock Group's "Set/Alter GST details" sub-screen — <b>HSN/SAC code, GST rate, taxability and type of
/// supply</b> — and nothing else.</para>
///
/// <para><b>R7 grounding.</b> The corpus enumerates the levels verbatim: <i>"GST can be implemented in Tally prime
/// by using any one method of the following: 1. Defining at Company Level 2. Defining at Stock Group Level
/// 3. Defining at Stock item level 4. Defining at Ledger Level 5. Creating GST Classification"</i>
/// (<c>tally/703679456-TALLY-PRIME-WITH-GST-Notes-PDF.pdf</c>, <c>pdftotext -layout</c> extracted lines
/// 2661-2665), and its worked "GST on Stock Group Level" walk-through sets a rate on a stock group through
/// <i>"Set/Alter GST details set to Yes"</i> (extracted lines 2771-2790). The field vocabulary — HSN Code, GST
/// Rate, Type of Supply — is the corpus's own (extracted lines 2167, 2189). <b>Level 5, "Creating GST
/// Classification", is deliberately EXCLUDED</b> (plan.md orchestrator ruling 3): the reference application's own
/// published hierarchy omits it and no GST-Classification master exists in <c>src/</c>.
/// </para>
///
/// <para><b>Persisted-but-inert in this slice.</b> S1 adds the masters and their storage only; no resolver reads
/// them yet, so every existing figure is unchanged. That matches the house precedent set by
/// <see cref="StockValuationMethod"/>, which also shipped persist-only.</para>
///
/// <para>Mutable value object hung off its master as a nullable reference — <c>null</c> means "this master carries
/// no GST details", which is what every pre-v51 master reads as. Framework- and DB-agnostic.</para>
/// </remarks>
public sealed class MasterGstDetails
{
    /// <summary>HSN (goods) / SAC (services) classification code — 4, 6 or 8 digits; <c>null</c> when unset.</summary>
    public string? HsnSac { get; set; }

    /// <summary>The taxability declared at this level. Only <see cref="GstTaxability.Taxable"/> attracts tax.</summary>
    public GstTaxability Taxability { get; set; } = GstTaxability.Taxable;

    /// <summary>The integrated GST rate in basis points (1800 = 18%); <c>null</c> ⇒ unresolved at this level.</summary>
    public int? RateBasisPoints { get; set; }

    /// <summary>Goods (HSN) or Services (SAC).</summary>
    public GstSupplyType SupplyType { get; set; } = GstSupplyType.Goods;

    /// <summary>True iff this block is taxable (attracts tax when a rate resolves).</summary>
    public bool IsTaxable => Taxability == GstTaxability.Taxable;

    /// <summary>
    /// Validates the HSN/SAC length (4/6/8 digits, all numeric) when set, and that a positive rate is only present
    /// on a taxable block. Throws <see cref="ArgumentException"/> on a bad value (fail-fast, ER-6). These are the
    /// same three rules <see cref="StockItemGstDetails.EnsureValid"/> applies to the same four fields — a rate or an
    /// HSN that would be rejected on a Stock Item must not be accepted on a Stock Group.
    /// </summary>
    public void EnsureValid()
    {
        if (HsnSac is not null)
        {
            var hsn = HsnSac.Trim();
            if (hsn.Length is not (4 or 6 or 8) || !hsn.All(char.IsDigit))
                throw new ArgumentException($"HSN/SAC '{HsnSac}' must be 4, 6 or 8 digits (numeric).");
        }

        if (RateBasisPoints is < 0)
            throw new ArgumentException("GST rate basis points must be ≥ 0 when set.");

        if (RateBasisPoints is { } && Taxability != GstTaxability.Taxable && RateBasisPoints > 0)
            throw new ArgumentException(
                $"A {Taxability} master must not carry a positive GST rate ({RateBasisPoints} bp).");
    }
}
