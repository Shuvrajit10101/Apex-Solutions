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
/// do nothing at three of the five levels.</para>
///
/// <para>🔴 <b>R7 GROUNDING — CORRECTED BY THE OWED REVIEW (lens 3 findings 1 and 2). The previous version of this
/// paragraph presented the whole model as corpus-grounded and it is not.</b> Read the three parts separately.</para>
///
/// <para><b>(a) What the corpus actually says, verbatim</b> (<c>tally/703679456-TALLY-PRIME-WITH-GST-Notes-PDF.pdf</c>,
/// <c>pdftotext -layout</c> extracted lines 2660-2666): <i>"GST can be implemented in Tally prime by using any one
/// method of the following: 1. Defining at Company Level 2. Defining at Stock Group Level 3. Defining at Stock item
/// level 4. Defining at Ledger Level 5. Creating GST Classification"</i>. ⚠️ <b>That is a list of five METHODS,
/// framed as "any one method of the following" — it is NOT a resolution order, and it is NOT our five levels.</b>
/// It contains <b>no accounting Group</b>, and it contains <b>GST Classification</b>, which we exclude (plan.md
/// orchestrator ruling 3 — the reference application's own published hierarchy omits it and no GST-Classification
/// master exists in <c>src/</c>). Our five and the corpus's five overlap in four members, not five;
/// <c>docs/invented-vs-cloned.md</c> IV-1's 2026-08-15 dagger has always said so.</para>
///
/// <para><b>(b) The accounting <see cref="Group"/> level is [web]-sourced with ZERO corpus support.</b> It appears
/// only in TallyHelp's "HSN/SAC &amp; GST Rate Hierarchy" strings (IV-1's Citation cell). Checked directly this
/// review: the corpus's accounting-Group creation screen
/// (<c>tally/696054070-TALLY-PRIME-STUDY-GUIDE.pdf</c>, extracted lines 2071-2090) lists <b>Name, alias and Under —
/// and no GST field at all</b>, and there are no corpus hits for GST details on an accounting Group across the ten
/// PDFs. <b>Do not cite the corpus for this level.</b></para>
///
/// <para><b>(c) The FIELD SET is partly inferred, and "and nothing else" is not sourced.</b> The one corpus page
/// that actually shows a Stock <b>Group</b> GST sub-screen is
/// <c>tally/680842180-Tally-With-GST-Notes.pdf</c>, extracted lines 110-122: <i>"Set/alter GST Details: Yes"</i> →
/// <i>"GST details for stock group Shirts"</i> → <i>"Taxability: Taxable"</i>, <i>"Integrated tax: 12%"</i> —
/// <b>TWO fields, not four; no HSN/SAC and no Type of Supply</b>. Corroborating that a stock group carries a rate:
/// <c>696054070</c> extracted lines 3090-3091, Stock Group Creation step 5, <i>"Set/Alter GST Details: 'Yes' for
/// setting fixed GST Rate, which will be applicable for all items under this group"</i> — which names no fields
/// either. The remaining two members are carried over from screens the corpus DOES enumerate: the Stock <b>Item</b>
/// screen (<c>703679456</c> extracted lines 2167 and 2182-2190 — HSN Code, GST Rate, Type of Supply) and the
/// <b>company</b>-level F11 sub-screen (<c>tally/664311548-Tally-Prime-Book.pdf</c>, extracted lines 6165-6172:
/// <i>"Fill details like, description, HSN/SAC code, Type of Goods/services and tax rate"</i>). <b>So: taxability
/// and rate on a Stock Group are corpus-sourced; HSN/SAC and Type of Supply on a Stock Group are
/// [web]/INFERRED and A14-UNVERIFIED</b>, and the earlier claim that these are "exactly the fields … and nothing
/// else" had no source in the repository. Do not re-assert it without a citation.</para>
///
/// <para><b>Persisted-but-inert in slice S4</b> (plan.md Phase 10.10 slice <b>S4</b> — four class docs used to say
/// "S1", which is the DEAD W0-2 workflow's slice number and is Interest running-balance accrual in this plan;
/// corrected by the owed review, lens 3 finding 10). The slice adds the masters and their storage only; no resolver
/// reads them yet, so every existing figure is unchanged. That matches the house precedent set by
/// <see cref="StockValuationMethod"/>, which also shipped persist-only.</para>
///
/// <para>Mutable value object hung off its master as a nullable reference — <c>null</c> means "this master carries
/// no GST details", which is what every pre-v51 master reads as. Framework- and DB-agnostic.</para>
///
/// <para>🔴 <b>VALIDATION REACHABILITY — RECORDED, NOT FIXED (owed-review lens 2 finding 4). Read this before
/// building the resolver.</b> <see cref="EnsureValid"/> has exactly <b>three</b> call sites in <c>src/</c> —
/// <c>ImportPlan.cs</c> (accounting Group), <c>ImportPlan.cs</c> (Stock Group) and
/// <see cref="GstConfig.EnsureValid"/> for the company default, itself called only from <c>GstService</c> and the
/// same import. <b>The canonical import path and nothing else.</b> Measured: <c>Company.AddStockGroup</c>,
/// <c>Company.AddGroup</c>, <c>InventoryService.CreateStockGroup</c>, the <see cref="GstConfig.DefaultGst"/> setter
/// and <c>SqliteCompanyStore.Save</c> all accept a malformed block and reload it verbatim — a 5-digit HSN, a
/// 7-digit HSN, an empty string, <c>ABCDEFGH</c>, −500 bp, 1 000 000 bp and <c>int.MaxValue</c> all survive a full
/// save/load. <b>The application can therefore produce a database its own importer rejects</b> (export → parse gives
/// 0 errors, then <c>CompanyImportService.Apply</c> returns <c>Applied = false</c>). This is exactly the shape of
/// the <c>Company.EnsureValid</c> limit recorded for W0-2a, on the block the resolver will read. It is latent only
/// because <b>no UI writes these fields</b>. ⚠️ <b>Wording corrected 2026-08-18</b> — this read *"zero hits for
/// <c>MasterGstDetails</c> in <c>src/Apex.Desktop</c>"*, which was an overstatement. Re-measured
/// (<c>grep -rn "MasterGstDetails" src/Apex.Desktop --include=*.cs --include=*.axaml</c>): <b>exactly one hit, and
/// it is a doc comment</b> — the <c>&lt;para&gt;</c> on <c>CompanyStorage.Save</c> (the desktop layer's single
/// validation choke point), which cites <c>MasterGstDetails.EnsureValid</c> as the precedent for putting that
/// floor at a choke point rather than in a screen. <b>The conclusion is unchanged and the operative facts still hold: no
/// view-model property, no XAML field, and no writer anywhere in the desktop layer.</b> A doc comment is not a
/// property, a route or a caller. The deferred master-GST screens are precisely what would break that, so they
/// must validate on save.</para>
///
/// <para>🔴 <b>AND THERE IS NO UPPER BOUND ON THE RATE (lens 2 finding 7), at either level.</b>
/// <see cref="EnsureValid"/> rejects only a negative value, so 1 000 000 bp (10 000 %) and <c>int.MaxValue</c>
/// validate, persist and reload. <see cref="StockItemGstDetails"/> has no upper bound either, so the parity below
/// holds — but neither has one. Likewise the 4/6/8-digit HSN rule is enforced <b>here and nowhere else</b>: there is
/// no <c>CHECK</c> constraint in the schema, no store-side check and no UI check (lens 2 finding 8).</para>
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
