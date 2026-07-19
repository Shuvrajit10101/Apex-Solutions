namespace Apex.Ledger.Domain;

/// <summary>
/// <b>Statute vocabulary</b> (CA slice S9) — the <b>single, auditable cutover point</b> between the
/// <b>Income-tax Act 1961</b> and the <b>Income-tax Act 2025</b> vocabularies.
///
/// <para>
/// This type is <b>presentation-layer only</b>. It maps <i>display labels</i> — section numbers, form numbers and the
/// period caption — and <b>changes no computation, no rate, no schema and no exported file</b>. Nothing here is
/// consulted by the tax engine (<see cref="Services.SalaryIncomeTax"/>), by the return builders, or by any writer.
/// </para>
///
/// <para><b>Why a gate rather than a rename.</b> The 1961 Act stands repealed on <b>01.04.2026</b>, so FY 2026-27
/// onward is governed by the 2025 Act while <b>FY 2025-26 and earlier remain governed by the 1961 Act</b>. Those
/// prior years are still live: FY 2025-26's Q4 return is filed on the <b>old</b> Form 24Q citing the <b>old</b>
/// section numbers, and §397(3)(f) of the 2025 Act permits correction statements for <b>two years</b>, expressly
/// referring to "section 200 of the Income-tax Act, 1961". 1961-Act artifacts therefore stay in use until roughly
/// FY 2028, and the product must hold <b>both vocabularies simultaneously</b> and select by financial year. A blanket
/// rename would falsify already-filed certificates and returns.</para>
///
/// <para><b>Absence of a mapping is a working state, not a hole.</b> An unknown key returns the legacy string
/// unchanged. That is deliberate and load-bearing: the §194x family (194C/194I/194J/194Q…), §206AA/§206CC,
/// §115BAC/§80C and the challan identifiers have <b>not</b> been verified against a primary source, so they must
/// <b>never</b> be renamed or re-cited here. Only entries confirmed against the CBDT Act browser <i>and</i> the
/// CBDT 1961→2025 concordance appear in the tables below.</para>
///
/// <para><b>The date trap.</b> "AY 2026-27" (= FY 2025-26, 1961 Act) and "tax year 2026-27" (= FY 2026-27, 2025 Act)
/// are <b>different years that collide numerically</b>. <see cref="PeriodLabel"/> returns the caption <i>and</i> the
/// value together so a caller cannot render one while meaning the other.</para>
/// </summary>
public static class StatuteVocabulary
{
    /// <summary>
    /// The <b>one</b> cutover predicate: the Income-tax Act 2025 governs a financial year starting in
    /// <b>2026 or later</b> (the 1961 Act stands repealed on 01.04.2026). Every vocabulary decision in the product
    /// routes through this method, so the cutover is auditable in a single place.
    /// </summary>
    /// <param name="fyStartYear">The calendar year in which the financial year starts (FY 2025-26 ⇒ 2025).</param>
    public static bool IsAct2025(int fyStartYear) => fyStartYear >= 2026;

    /// <summary>
    /// Confirmed 1961-Act → 2025-Act <b>section</b> renumbering (display labels only). Keys are the legacy labels as
    /// they already appear in the product. Retrieved from the CBDT Act browser and corroborated by CBDT's
    /// machine-readable 1961→2025 concordance.
    /// <b>Deliberately absent</b>: the §194x family, §206AA, §206CC, §115BAC, §80C, §80CCD(1B), §80D — unverified,
    /// so they fall through unchanged.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> Sections2025 = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["192"] = "392",       // salary TDS
        ["192A"] = "392(7)",   // PF accumulated balance — folded into the salary section
        ["200"] = "397",       // duty to deposit / file statements
        ["200A"] = "399",      // processing of statements
        ["201"] = "398",       // consequences of failure to deduct or pay
        ["203"] = "395",       // certificate of deduction
        ["87A"] = "156",       // rebate
        ["206C"] = "394",      // TCS (fragmented across 390/395/397/398/400 — 394 is the charging locus)
        ["234E"] = "427",      // fee for late filing
        ["271H"] = "461",      // penalty for failure to file
        ["276B"] = "476",      // prosecution for failure to pay
    };

    /// <summary>
    /// Confirmed 1961-Act → 2025-Act <b>form</b> renumbering (display labels only). Same sourcing and the same
    /// fall-through rule as <see cref="Sections2025"/>.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> Forms2025 = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["24Q"] = "138",   // quarterly salary-TDS return
        ["26Q"] = "140",   // quarterly non-salary TDS return
        ["27Q"] = "144",   // quarterly TDS return — payments to non-residents
        ["27EQ"] = "143",  // quarterly TCS return
        ["16"] = "130",    // annual salary-TDS certificate
        ["16A"] = "131",   // non-salary TDS certificate
        ["12BB"] = "124",  // employee declaration of deductions
        ["12BA"] = "123",  // perquisites statement
        ["27D"] = "133",   // TCS certificate
        ["24G"] = "137",   // government-deductor book-adjustment statement
    };

    /// <summary>
    /// The <b>section</b> display label for <paramref name="legacySection"/> in the financial year starting
    /// <paramref name="fyStartYear"/>. Returns the legacy label unchanged when the 1961 Act still governs the year,
    /// <b>and also when no confirmed mapping exists</b> — which is what keeps the unverified §194x family, §206AA and
    /// §206CC safe from an accidental rename.
    /// </summary>
    /// <param name="legacySection">The 1961-Act section number as already used in the product, e.g. "192", "206C".</param>
    /// <param name="fyStartYear">The calendar year in which the financial year starts.</param>
    public static string SectionLabel(string legacySection, int fyStartYear)
    {
        if (legacySection is null) throw new ArgumentNullException(nameof(legacySection));
        if (!IsAct2025(fyStartYear)) return legacySection;
        return Sections2025.TryGetValue(legacySection, out var renumbered) ? renumbered : legacySection;
    }

    /// <summary>
    /// The <b>form</b> display label for <paramref name="legacyForm"/> in the financial year starting
    /// <paramref name="fyStartYear"/>. Same fall-through contract as <see cref="SectionLabel"/>.
    /// </summary>
    /// <param name="legacyForm">The 1961-Act form number as already used in the product, e.g. "24Q", "16".</param>
    /// <param name="fyStartYear">The calendar year in which the financial year starts.</param>
    public static string FormLabel(string legacyForm, int fyStartYear)
    {
        if (legacyForm is null) throw new ArgumentNullException(nameof(legacyForm));
        if (!IsAct2025(fyStartYear)) return legacyForm;
        return Forms2025.TryGetValue(legacyForm, out var renumbered) ? renumbered : legacyForm;
    }

    /// <summary>
    /// The <b>dual</b> display label "legacy / renumbered" for a static context where <b>no financial year is in
    /// scope</b> (a menu built before a company is loaded). Showing both is honest; guessing one is not. Falls back to
    /// the legacy label alone when no confirmed mapping exists.
    /// </summary>
    public static string FormLabelDual(string legacyForm)
    {
        if (legacyForm is null) throw new ArgumentNullException(nameof(legacyForm));
        return Forms2025.TryGetValue(legacyForm, out var renumbered) ? $"{legacyForm} / {renumbered}" : legacyForm;
    }

    /// <summary>
    /// The <b>short title of the governing Act</b>, as it must be cited verbatim on a printed statutory certificate.
    /// <para>The 1961-Act string is reproduced <b>character-for-character</b> as it already appears in the certificate
    /// subtitles — "Income-tax Act, 1961" — so that a prior-year certificate reprints byte-identically (ER-13). The
    /// 2025-Act string is its exact structural twin, "Income-tax Act, 2025": the new statute's own short title is the
    /// <i>Income-tax Act, 2025</i>, so the hyphenation, the comma and the spacing all carry over unchanged and only the
    /// year moves. That is a <b>deliberate</b> choice over any re-styling ("Income Tax Act 2025", "IT Act, 2025") —
    /// inventing a house style for a statute name on a document a deductee files with the department would be an
    /// unsourced coinage, exactly what <see cref="PeriodCaptionShort"/> refuses to do for "TY".</para>
    /// </summary>
    public static string ActName(int fyStartYear) => IsAct2025(fyStartYear) ? "Income-tax Act, 2025" : "Income-tax Act, 1961";

    /// <summary>The caption for the tax period — "Tax Year" under the 2025 Act, "Assessment Year" before it.</summary>
    /// <remarks>The 2025 Act retires <b>both</b> "assessment year" and "previous year" in favour of
    /// <b>"tax year"</b> (defined in s.3).</remarks>
    public static string PeriodCaption(int fyStartYear) => IsAct2025(fyStartYear) ? "Tax Year" : "Assessment Year";

    /// <summary>
    /// The <b>abbreviated</b> period caption, for the tight contexts (report subtitles, narrow grid cells) that
    /// already read "AY 2026-27". The 1961-Act form stays exactly <c>"AY"</c> so prior-year printouts reproduce
    /// byte-for-byte (ER-13); the 2025-Act form is spelled <c>"Tax Year"</c> because the statute establishes no
    /// abbreviation and inventing one ("TY") would be an unsourced coinage.
    /// </summary>
    public static string PeriodCaptionShort(int fyStartYear) => IsAct2025(fyStartYear) ? "Tax Year" : "AY";

    /// <summary>
    /// The <b>value</b> shown beside <see cref="PeriodCaption"/>. This is <b>not</b> merely a caption change: under
    /// the 2025 Act the <b>tax year IS the financial year</b>, so the value shifts back a year relative to the
    /// assessment year it replaces.
    /// <para><b>The collision to keep in mind:</b> FY 2025-26 displays "Assessment Year 2026-27" while FY 2026-27
    /// displays "Tax Year 2026-27". The numerals match; the years do not. Always render the caption with the value.</para>
    /// </summary>
    public static string PeriodLabel(int fyStartYear) => IsAct2025(fyStartYear)
        ? $"{fyStartYear}-{(fyStartYear + 1) % 100:00}"        // tax year == financial year
        : $"{fyStartYear + 1}-{(fyStartYear + 2) % 100:00}";   // assessment year == FY + 1

    /// <summary>The caption and value together — "Assessment Year 2026-27" / "Tax Year 2026-27" — for the single-string
    /// contexts (subtitles, print rows) where separating them would invite the collision described above.</summary>
    public static string PeriodCaptionAndLabel(int fyStartYear) => $"{PeriodCaption(fyStartYear)} {PeriodLabel(fyStartYear)}";
}
