namespace Apex.Ledger.Domain;

/// <summary>
/// The <b>Cheque Dimensions</b> of one bank ledger's cheque leaf — where, on a pre-printed leaf, each element
/// of the cheque is to be inked (catalog §8 Banking; row 8.4 "Cheque printing").
///
/// <para><b>Vendor grounding.</b> The field list is taken verbatim from the only vendor page that enumerates the
/// Cheque Dimensions screen:
/// <c>help.tallysolutions.com/docs/te9rel51/Advanced_Features/Advanced_Accounting_Features/Creation_Mode.htm</c>,
/// section "Cheque Dimensions" — which names, per element: <i>Cheque Date</i> ("Distance of Line from Top Edge",
/// "Starting location from left Edge", Style of Date <c>d d m m y y y y</c>, "Distance between Characters");
/// <i>Party's Payee Name</i> ("Distance of Line from Top Edge", "Starting Location from Left Edge", "Width area
/// (default: 135)"); <i>Amount in Words</i> (a two-line field with "Distance of 2nd Line from Top Edge", the
/// height gap between lines, the starting location of each line from the left edge, the width area, and "Print
/// Currency Formal Name"); <i>Amount in Figures</i> ("Distance from Top Edge", "Starting Location from Left
/// Edge", width area, "Print Currency Symbol"); and <i>Signatory Details</i> (salutation of the 1st and 2nd
/// signatories, distance from top edge, starting location from left edge, width and height of the signature
/// area). Every measure on that screen is in millimetres.</para>
///
/// <para><b>🔴 UNITS: TENTHS OF A MILLIMETRE, STORED AS <see langword="int"/> — NEVER a floating-point
/// millimetre.</b> This is the same rule <c>Paisa</c> exists for. A <see langword="double"/> millimetre would
/// let the identical layout render two different byte streams on two machines, which breaks every PDF
/// determinism assertion in this repository. So 135 mm is <c>1350</c> here, and the only floating-point step in
/// the whole pipeline is the single tenths-mm → PDF-point conversion inside the renderer.</para>
///
/// <para><b>🔴 A ZERO OFFSET MEANS "NOT SET", AND AN UNSET ELEMENT IS SKIPPED, NOT PRINTED AT THE ORIGIN.</b>
/// Nothing sensible is ever inked at the very corner of a cheque leaf, and a payee name in the top-left corner
/// of a negotiable instrument is worse than no payee name at all. <see cref="ChequeElementIsSet"/> encodes the
/// rule once so the renderer and the UI cannot disagree about it.</para>
///
/// <para><b>🔴 THERE IS EXACTLY ONE FORMAT, "USER DEFINED", AND NO TABLE OF NAMED BANK FORMATS.</b> The vendor's
/// per-bank predefined dimension table is served from its own subscription service
/// (<c>help.tallysolutions.com/cheque-payments-set-up/</c>, "Alter Predefined Cheque Dimensions" — selected from
/// a list when the subscription is active and the internet is available). Not one millimetre of it is published
/// anywhere we may cite, and guessing a millimetre would put ink in the wrong place on a negotiable instrument.
/// The operator calibrates instead — see <c>ChequePdf.RenderCalibrationSheet</c>.</para>
///
/// <para><b>What is deliberately NOT here.</b> (a) <i>No A/C-payee crossing.</i> The Cheque Dimensions list
/// contains Date, Payee, Amount in Words, Amount in Figures and Signatory, and <b>no crossing element</b>; we do
/// not invent one. (b) <i>No "height gap between lines" column.</i> The vendor screen shows it, but it is
/// exactly <see cref="WordsLine2TopTmm"/> − <see cref="WordsLine1TopTmm"/> — see <see cref="WordsLineGapTmm"/>.
/// Two stored numbers obliged to agree is a drift bug waiting to be written. (c) <i>No leaf size default.</i>
/// The physical width and height of a cheque leaf are not attested by any vendor page we could cite, so they
/// start at 0 and printing is refused with a message until the operator sets them.</para>
/// </summary>
/// <remarks>
/// Mutable, framework-agnostic and DB-agnostic, hung off a bank <see cref="Ledger"/> as an optional block in the
/// same shape as <see cref="PartyMailingDetails"/>. <c>null</c> ⇒ no dimensions captured, which is the state of
/// every ledger that existed before this feature and keeps its persisted bytes identical.
/// </remarks>
public sealed class ChequeLayout
{
    /// <summary>The vendor's documented default for the payee-name width area: 135 mm, i.e. 1350 tenths.</summary>
    public const int DefaultPayeeWidthTmm = 1350;

    // ---------------------------------------------------------------- the leaf

    /// <summary>Width of the physical cheque leaf, tenths of a millimetre. 0 ⇒ not set; printing is refused.</summary>
    public int LeafWidthTmm { get; set; }

    /// <summary>Height of the physical cheque leaf, tenths of a millimetre. 0 ⇒ not set; printing is refused.</summary>
    public int LeafHeightTmm { get; set; }

    // ---------------------------------------------------------------- Cheque Date

    /// <summary>"Distance of Line from Top Edge" for the date, tenths of a millimetre.</summary>
    public int DateTopTmm { get; set; }

    /// <summary>"Starting location from left Edge" for the date, tenths of a millimetre.</summary>
    public int DateLeftTmm { get; set; }

    /// <summary>
    /// "Distance between Characters" — the pitch of the NPCI boxed date field, tenths of a millimetre. The date
    /// prints as EIGHT separate glyphs (<c>d d m m y y y y</c>), each advanced by this pitch, so each falls in
    /// its own printed box. Printing <c>dd/MM/yyyy</c> as one string is the wrong shape for this field.
    /// </summary>
    public int DateCharPitchTmm { get; set; }

    // ---------------------------------------------------------------- Party's Payee Name

    /// <summary>"Distance of Line from Top Edge" for the payee name, tenths of a millimetre.</summary>
    public int PayeeTopTmm { get; set; }

    /// <summary>"Starting Location from Left Edge" for the payee name, tenths of a millimetre.</summary>
    public int PayeeLeftTmm { get; set; }

    /// <summary>"Width area" for the payee name, tenths of a millimetre. Vendor default 135 mm.</summary>
    public int PayeeWidthTmm { get; set; } = DefaultPayeeWidthTmm;

    // ---------------------------------------------------------------- Amount in Words (two lines)

    /// <summary>Distance of the FIRST amount-in-words line from the top edge, tenths of a millimetre.</summary>
    public int WordsLine1TopTmm { get; set; }

    /// <summary>Starting location of the first amount-in-words line from the left edge, tenths of a millimetre.</summary>
    public int WordsLine1LeftTmm { get; set; }

    /// <summary>"Distance of 2nd Line from Top Edge", tenths of a millimetre.</summary>
    public int WordsLine2TopTmm { get; set; }

    /// <summary>Starting location of the second amount-in-words line from the left edge, tenths of a millimetre.</summary>
    public int WordsLine2LeftTmm { get; set; }

    /// <summary>"Width area" each amount-in-words line must wrap within, tenths of a millimetre.</summary>
    public int WordsWidthTmm { get; set; }

    /// <summary>
    /// The vendor's "Height (gap) between lines" — DERIVED, never stored, so the gap and the two line positions
    /// can never contradict each other. Zero (or negative) when the second line is not set.
    /// </summary>
    public int WordsLineGapTmm => WordsLine2TopTmm - WordsLine1TopTmm;

    /// <summary>
    /// "Print Currency Formal Name" — when on, the words name the company's base currency by its formal name
    /// rather than the fixed "Rupees"/"Paise" wording.
    /// </summary>
    public bool PrintCurrencyFormalName { get; set; }

    // ---------------------------------------------------------------- Amount in Figures

    /// <summary>"Distance from Top Edge" for the amount in figures, tenths of a millimetre.</summary>
    public int FiguresTopTmm { get; set; }

    /// <summary>"Starting Location from Left Edge" for the amount in figures, tenths of a millimetre.</summary>
    public int FiguresLeftTmm { get; set; }

    /// <summary>"Width area" for the amount in figures, tenths of a millimetre.</summary>
    public int FiguresWidthTmm { get; set; }

    /// <summary>"Print Currency Symbol" — when on, the figures are prefixed with the base-currency symbol.</summary>
    public bool PrintCurrencySymbol { get; set; }

    // ---------------------------------------------------------------- Signatory Details

    /// <summary>"Salutation of 1st Signatory" (e.g. "For Apex Solutions"). <c>null</c> ⇒ nothing printed.</summary>
    public string? Salutation1 { get; set; }

    /// <summary>"Salutation of 2nd Signatory". <c>null</c> ⇒ nothing printed.</summary>
    public string? Salutation2 { get; set; }

    /// <summary>"Distance from Top Edge" of the signature area, tenths of a millimetre.</summary>
    public int SignTopTmm { get; set; }

    /// <summary>"Starting Location from Left Edge" of the signature area, tenths of a millimetre.</summary>
    public int SignLeftTmm { get; set; }

    /// <summary>"Width of signature area", tenths of a millimetre.</summary>
    public int SignWidthTmm { get; set; }

    /// <summary>"Height of signature area", tenths of a millimetre.</summary>
    public int SignHeightTmm { get; set; }

    // ---------------------------------------------------------------- derived state

    /// <summary>
    /// The one place the "an offset of 0 means NOT SET" rule is written. An element is inked only when at least
    /// one of its two offsets is non-zero; both zero ⇒ the operator has not placed it, and it is skipped.
    /// </summary>
    public static bool ChequeElementIsSet(int topTmm, int leftTmm) => topTmm != 0 || leftTmm != 0;

    /// <summary>True once the physical leaf has both a width and a height — the precondition for rendering.</summary>
    public bool HasLeafSize => LeafWidthTmm > 0 && LeafHeightTmm > 0;

    /// <summary>True iff nothing at all was captured, so an untouched ledger can persist a <c>null</c> block.</summary>
    public bool IsEmpty =>
        LeafWidthTmm == 0 && LeafHeightTmm == 0
        && DateTopTmm == 0 && DateLeftTmm == 0 && DateCharPitchTmm == 0
        && PayeeTopTmm == 0 && PayeeLeftTmm == 0 && PayeeWidthTmm == DefaultPayeeWidthTmm
        && WordsLine1TopTmm == 0 && WordsLine1LeftTmm == 0
        && WordsLine2TopTmm == 0 && WordsLine2LeftTmm == 0 && WordsWidthTmm == 0
        && FiguresTopTmm == 0 && FiguresLeftTmm == 0 && FiguresWidthTmm == 0
        && SignTopTmm == 0 && SignLeftTmm == 0 && SignWidthTmm == 0 && SignHeightTmm == 0
        && !PrintCurrencyFormalName && !PrintCurrencySymbol
        && string.IsNullOrWhiteSpace(Salutation1) && string.IsNullOrWhiteSpace(Salutation2);

    /// <summary>A field-for-field copy — the editor edits a copy so Escape can discard it.</summary>
    public ChequeLayout Clone() => new()
    {
        LeafWidthTmm = LeafWidthTmm,
        LeafHeightTmm = LeafHeightTmm,
        DateTopTmm = DateTopTmm,
        DateLeftTmm = DateLeftTmm,
        DateCharPitchTmm = DateCharPitchTmm,
        PayeeTopTmm = PayeeTopTmm,
        PayeeLeftTmm = PayeeLeftTmm,
        PayeeWidthTmm = PayeeWidthTmm,
        WordsLine1TopTmm = WordsLine1TopTmm,
        WordsLine1LeftTmm = WordsLine1LeftTmm,
        WordsLine2TopTmm = WordsLine2TopTmm,
        WordsLine2LeftTmm = WordsLine2LeftTmm,
        WordsWidthTmm = WordsWidthTmm,
        PrintCurrencyFormalName = PrintCurrencyFormalName,
        FiguresTopTmm = FiguresTopTmm,
        FiguresLeftTmm = FiguresLeftTmm,
        FiguresWidthTmm = FiguresWidthTmm,
        PrintCurrencySymbol = PrintCurrencySymbol,
        Salutation1 = Salutation1,
        Salutation2 = Salutation2,
        SignTopTmm = SignTopTmm,
        SignLeftTmm = SignLeftTmm,
        SignWidthTmm = SignWidthTmm,
        SignHeightTmm = SignHeightTmm,
    };

    /// <summary>Trims the two salutations and normalises blanks to <c>null</c>.</summary>
    public void Normalize()
    {
        Salutation1 = string.IsNullOrWhiteSpace(Salutation1) ? null : Salutation1.Trim();
        Salutation2 = string.IsNullOrWhiteSpace(Salutation2) ? null : Salutation2.Trim();
    }
}
