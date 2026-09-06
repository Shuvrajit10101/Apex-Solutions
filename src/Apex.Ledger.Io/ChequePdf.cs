using System.Globalization;
using Apex.Ledger.Domain;

namespace Apex.Ledger.Io;

/// <summary>
/// Inks one <b>pre-printed cheque leaf</b> (catalog §8 Banking; census row 8.4), and prints the
/// <b>calibration sheet</b> the operator uses to measure that leaf in the first place.
///
/// <para><b>🔴 THIS RENDERER ADDS INK; IT DOES NOT DRAW A CHEQUE.</b> The leaf already exists — it came from the
/// bank, pre-printed and pre-numbered. So there are no ruling lines, no captions, no boxes and no borders here:
/// only the five elements the vendor's Cheque Dimensions screen places
/// (<c>help.tallysolutions.com/docs/te9rel51/Advanced_Features/Advanced_Accounting_Features/Creation_Mode.htm</c>,
/// "Cheque Dimensions") — Cheque Date, Party's Payee Name, Amount in Words, Amount in Figures and Signatory
/// Details — each drawn only when the operator has actually placed it.</para>
///
/// <para><b>🔴 THE COORDINATE FLIP IS THE WHOLE RISK OF THIS FILE.</b> Every cheque dimension is measured
/// <i>downward from the TOP edge</i> of the leaf; PDF's origin is at the <i>bottom-left</i>. The single
/// conversion <see cref="TopY"/> is the only place that flip happens, and every element goes through it. Get it
/// wrong once and every cheque prints mirrored top-to-bottom, which is why it has a test of its own.</para>
///
/// <para><b>Amount in words is not reimplemented.</b> It comes from
/// <see cref="IndianAmountInWords"/> — the same paisa-exact, Indian-grouped, culture-invariant converter the tax
/// invoice uses. The layout's "Print Currency Formal Name" toggle simply selects its two-argument overload.</para>
///
/// <para><b>Determinism.</b> No clock, no RNG, invariant formatting throughout, and the tenths-of-a-millimetre
/// integers of <see cref="ChequeLayout"/> mean the only floating-point step is the one conversion to points. The
/// same cheque renders byte-identically on every host.</para>
///
/// <para><b>🔴 REACHABILITY, STATED PLAINLY: NO OPERATOR CAN REACH THIS RENDERER TODAY.</b> It is driven by
/// <c>Ledger.ChequeLayout</c>, which does not persist — the <c>cheque_layouts</c> table is this wave's single
/// schema migration and it could not be taken on this branch (wave 7 allocated the next schema version to a
/// SIBLING track; this tree is already at v53, so the cheque tables are a later version somebody must claim).
/// A loaded company therefore always has a <c>null</c> layout, <see cref="Validate"/> always refuses, and the
/// <b>leaf never prints</b>. The code, its guards and its tests are correct and complete and go live the moment
/// the migration lands; until then row 8.4 ships on its <i>Cheque Printing report</i> half alone, and nothing
/// here may be reported as delivered. See the block in <c>Ledger.cs</c> for the exact unblock.</para>
///
/// <para><b>🔴 SOURCE-SILENCE, STATED RATHER THAN INVENTED.</b> (a) The Cheque Dimensions screen enumerates no
/// <i>A/C-payee crossing</i> element, so this renderer draws none. (b) It gives no <i>type size</i>, so one fixed
/// body size is used (<see cref="BodyFontSize"/>) and the vendor's width areas do the fitting — the size is ours,
/// not the vendor's. (c) It gives no offset pair for the drawer's <i>company name</i>; the toggle exists
/// (<c>help.tallysolutions.com/cheque-payments-set-up/</c>, "Disable Company Name in the Pre-printed Cheques") but
/// no position does, so when the toggle is on the name is placed on the top line of the <i>signature area</i>,
/// which is the only attested region it can belong to. That placement is OURS and is recorded as such.</para>
/// </summary>
public static class ChequePdf
{
    /// <summary>Points per tenth of a millimetre: 72 pt / 25.4 mm ÷ 10.</summary>
    private const double PointsPerTenthMm = 72.0 / 254.0;

    /// <summary>
    /// The body type size, in points. The vendor's Cheque Dimensions screen places elements but does not
    /// prescribe a type size, so one is fixed here and the attested "width area" of each element does the
    /// fitting. OURS, not cloned.
    /// </summary>
    public const double BodyFontSize = 10.0;

    /// <summary>Converts tenths of a millimetre to PDF points.</summary>
    public static double Pt(int tenthsMm) => tenthsMm * PointsPerTenthMm;

    /// <summary>
    /// 🔴 The top-edge flip. A cheque dimension is a distance measured DOWN from the top edge of the leaf; a PDF
    /// coordinate is measured UP from the bottom. Every element on the cheque goes through this one function.
    /// </summary>
    public static double TopY(int leafHeightTmm, int topTmm) => Pt(leafHeightTmm) - Pt(topTmm);

    /// <summary>
    /// The reason this cheque cannot be printed, or <c>null</c> when it can. These are guards on a
    /// <b>negotiable instrument</b>, so each one refuses rather than degrades: printing a cheque with a guessed
    /// offset, for an instrument that is not a cheque, or with no number is not a cosmetic defect.
    /// </summary>
    public static string? Validate(ChequePrintData data, ChequeLayout layout)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(layout);

        if (!layout.HasLeafSize)
            return "Cheque dimensions are not set for this bank. Set them on the bank ledger before printing.";
        if (string.IsNullOrWhiteSpace(data.InstrumentNumber))
            return "This payment carries no cheque number. Enter the instrument number before printing.";
        if (string.IsNullOrWhiteSpace(data.PayeeName))
            return "This payment names no payee. A cheque cannot be printed without a favouring name.";

        // 🔴 A LEAF SIZE IS NOT A LAYOUT. `HasLeafSize` alone let through a layout that places NOTHING: every
        // element in Render is guarded by `ChequeElementIsSet(top, left)`, so with all the offsets still at zero
        // the renderer produced a correctly-sized page with no payee, no amount, no date and no signatory on it —
        // a blank pass through the printer that consumes a real, pre-numbered leaf out of the cheque book, and
        // the operator has to void it. This does NOT touch the deliberate rule one element down: an individual
        // element the operator never placed is still SKIPPED, because a payee name inked at the corner of a
        // negotiable instrument is worse than no payee name. What is refused is a layout that inks nothing at all.
        bool placesSomething =
            ChequeLayout.ChequeElementIsSet(layout.DateTopTmm, layout.DateLeftTmm)
            || ChequeLayout.ChequeElementIsSet(layout.PayeeTopTmm, layout.PayeeLeftTmm)
            || ChequeLayout.ChequeElementIsSet(layout.WordsLine1TopTmm, layout.WordsLine1LeftTmm)
            || ChequeLayout.ChequeElementIsSet(layout.FiguresTopTmm, layout.FiguresLeftTmm)
            || ChequeLayout.ChequeElementIsSet(layout.SignTopTmm, layout.SignLeftTmm);
        if (!placesSomething)
            return "Cheque dimensions place nothing on this leaf. Print the calibration sheet, measure a leaf and "
                   + "set the offsets before printing.";

        // 🔴 A TRUNCATED AMOUNT IN WORDS IS A DIFFERENT CHEQUE. The vendor's field is TWO independently-placed
        // lines. A long amount wraps to more, and everything past line 2 was being silently dropped — or
        // ellipsised by FitToWidth — leaving an instrument whose words and figures disagree: the bank returns it
        // and the leaf is already spent. The words are not something to fit as best we can; they ARE the amount.
        // So the fit is a GUARD, and it names the dimension to widen.
        if (ChequeLayout.ChequeElementIsSet(layout.WordsLine1TopTmm, layout.WordsLine1LeftTmm))
        {
            var wordLines = VoucherPdf.WrapText(AmountWords(data, layout), WordsWidth(layout), BodyFontSize);
            if (wordLines.Count > 1
                && !ChequeLayout.ChequeElementIsSet(layout.WordsLine2TopTmm, layout.WordsLine2LeftTmm))
                return "The amount in words needs a second line and this leaf places none. Set the second "
                       + "amount-in-words offsets, or widen the amount-in-words area.";
            if (wordLines.Count > 2)
                return "The amount in words does not fit the two lines this leaf places. Widen the "
                       + "amount-in-words area before printing.";
        }

        return null;
    }

    /// <summary>The amount in words exactly as <see cref="Render"/> will ink it, de-branding included — ONE
    /// statement of it, so the <see cref="Validate"/> fit guard and the rendering cannot disagree by a character
    /// about what is being measured. (The de-brand reaches only the CURRENCY NAMES, which are this product's own
    /// master data; the figure itself is converted from a decimal.)</summary>
    private static string AmountWords(ChequePrintData data, ChequeLayout layout) =>
        Debrand.Text(layout.PrintCurrencyFormalName
            ? IndianAmountInWords.Convert(
                data.Amount.Amount,
                Blank(data.CurrencyFormalName, "Rupees"),
                Blank(data.CurrencyMinorName, "Paise"))
            : IndianAmountInWords.Convert(data.Amount.Amount));

    /// <summary>The measured width of the amount-in-words area, falling back to the whole leaf when the layout
    /// states none.</summary>
    private static double WordsWidth(ChequeLayout layout) =>
        layout.WordsWidthTmm > 0 ? Pt(layout.WordsWidthTmm) : Pt(layout.LeafWidthTmm);

    /// <summary>
    /// Renders the cheque to PDF bytes, on a page the exact size of the leaf. Throws
    /// <see cref="InvalidOperationException"/> carrying the <see cref="Validate"/> message when the cheque must
    /// not be printed — callers surface that text rather than producing a wrong cheque.
    /// </summary>
    public static byte[] Render(ChequePrintData data, ChequeLayout layout)
    {
        if (Validate(data, layout) is { } refusal) throw new InvalidOperationException(refusal);

        var writer = new PdfWriter { DocumentTitle = "Cheque - Apex Solutions" };
        writer.BeginPage(Pt(layout.LeafWidthTmm), Pt(layout.LeafHeightTmm));

        int nudgeTop = data.NudgeTopTmm;
        int nudgeLeft = data.NudgeLeftTmm;

        double X(int leftTmm) => Pt(leftTmm + nudgeLeft);
        double Y(int topTmm) => TopY(layout.LeafHeightTmm, topTmm + nudgeTop);

        // ---- Cheque Date: EIGHT glyphs, each advanced by the configured character pitch (the NPCI d d m m y y y y
        //      boxed field). Printing "dd/MM/yyyy" as one string would straddle the printed boxes. ----
        if (data.ChequeDate is { } chequeDate
            && ChequeLayout.ChequeElementIsSet(layout.DateTopTmm, layout.DateLeftTmm))
        {
            string digits = chequeDate.ToString("ddMMyyyy", CultureInfo.InvariantCulture);
            double y = Y(layout.DateTopTmm);
            for (int i = 0; i < digits.Length; i++)
                writer.Text(X(layout.DateLeftTmm + i * layout.DateCharPitchTmm), y, digits[i].ToString(), BodyFontSize);
        }

        // ---- Party's Payee Name, fitted to its attested width area ----
        // 🔴 VERBATIM — NOT DE-BRANDED. The favouring name is the payee's own legal name on a NEGOTIABLE
        // INSTRUMENT: the bank pays whoever this line names. Debrand.Text strips a case-insensitive vendor token
        // out of text THIS PRODUCT OWNS (ER-11), and pointed at a counterparty's name it silently rewrites it —
        // a cheque favouring "Metally Traders" would be inked "Me Traders" and would be dishonoured, or worse,
        // paid to somebody else. The brand guard belongs on our letterhead, not on the payee line.
        if (ChequeLayout.ChequeElementIsSet(layout.PayeeTopTmm, layout.PayeeLeftTmm))
        {
            var payee = PdfWriter.FitToWidth(data.PayeeName, Pt(layout.PayeeWidthTmm), BodyFontSize);
            writer.Text(X(layout.PayeeLeftTmm), Y(layout.PayeeTopTmm), payee, BodyFontSize);
        }

        // ---- Amount in Words: a two-line field, wrapped by MEASURED width (never a character count) ----
        if (ChequeLayout.ChequeElementIsSet(layout.WordsLine1TopTmm, layout.WordsLine1LeftTmm))
        {
            string words = AmountWords(data, layout);
            double wordsWidth = WordsWidth(layout);
            var lines = VoucherPdf.WrapText(words, wordsWidth, BodyFontSize);

            if (lines.Count > 0)
                writer.Text(X(layout.WordsLine1LeftTmm), Y(layout.WordsLine1TopTmm), lines[0], BodyFontSize);

            // Everything that did not fit on line 1 goes on line 2, at ITS own offsets — the vendor models the
            // second line as an independently-placed line, not as a fixed leading below the first.
            if (lines.Count > 1 && ChequeLayout.ChequeElementIsSet(layout.WordsLine2TopTmm, layout.WordsLine2LeftTmm))
            {
                string rest = string.Join(' ', lines.Skip(1));
                writer.Text(
                    X(layout.WordsLine2LeftTmm),
                    Y(layout.WordsLine2TopTmm),
                    PdfWriter.FitToWidth(rest, wordsWidth, BodyFontSize),
                    BodyFontSize);
            }
        }

        // ---- Amount in Figures ----
        if (ChequeLayout.ChequeElementIsSet(layout.FiguresTopTmm, layout.FiguresLeftTmm))
        {
            string figures = IndianMoneyFormat.Amount(data.Amount);
            if (layout.PrintCurrencySymbol)
                figures = Blank(data.CurrencySymbol, "Rs.") + " " + figures;
            if (layout.FiguresWidthTmm > 0)
                figures = PdfWriter.FitToWidth(figures, Pt(layout.FiguresWidthTmm), BodyFontSize);
            writer.Text(X(layout.FiguresLeftTmm), Y(layout.FiguresTopTmm), figures, BodyFontSize);
        }

        // ---- Signatory Details: the drawer's name (only when the toggle asks for it) then the two salutations,
        //      laid down the signature area from its top edge. ----
        if (ChequeLayout.ChequeElementIsSet(layout.SignTopTmm, layout.SignLeftTmm))
        {
            double signWidth = layout.SignWidthTmm > 0 ? Pt(layout.SignWidthTmm) : Pt(layout.LeafWidthTmm);
            double lineHeight = BodyFontSize + 2;
            double y = Y(layout.SignTopTmm);
            double x = X(layout.SignLeftTmm);

            void SignLine(string? text, bool bold)
            {
                if (string.IsNullOrWhiteSpace(text)) return;
                writer.Text(x, y, PdfWriter.FitToWidth(Debrand.Text(text), signWidth, BodyFontSize), BodyFontSize, bold);
                y -= lineHeight;
            }

            if (data.PrintCompanyName) SignLine(data.CompanyName, bold: true);
            SignLine(layout.Salutation1, bold: false);
            SignLine(layout.Salutation2, bold: false);
        }

        return writer.Build();
    }

    /// <summary>
    /// 🔴 <b>The calibration sheet — this project's answer to the bootstrap problem, and it is OURS, not the
    /// vendor's.</b>
    ///
    /// <para>The vendor solves "what are this bank's millimetres?" by downloading a predefined per-bank dimension
    /// table from its own subscription service. We have no admissible source for a single millimetre of that
    /// table, and a guessed offset on a negotiable instrument is not a cosmetic defect — so we do not guess. This
    /// sheet prints, on a page the exact size of the leaf, a 10 mm grid ruled FROM THE TOP-LEFT CORNER with
    /// millimetre labels down the left edge and across the top. The operator prints it on plain paper, lays it
    /// over a real cheque leaf, and reads the five offsets straight off the grid. That is how real millimetres
    /// are obtained without inventing any.</para>
    ///
    /// <para>Needs only the leaf size; every other measure may still be zero.</para>
    ///
    /// <para><b>🔴 No caller in <c>src/</c> yet, deliberately.</b> Its button belongs on the cheque-dimensions
    /// screen, and that screen cannot exist until the layout persists — see the reachability note on this class.
    /// It is exercised by <c>ChequePdfTests</c> and lands with the rest of the bundle.</para>
    /// </summary>
    public static byte[] RenderCalibrationSheet(ChequeLayout layout)
    {
        ArgumentNullException.ThrowIfNull(layout);
        if (!layout.HasLeafSize)
            throw new InvalidOperationException(
                "Cheque dimensions are not set for this bank. Set them on the bank ledger before printing.");

        const int StepTmm = 100;         // a 10 mm grid
        const double LabelFontSize = 6.0;

        var writer = new PdfWriter { DocumentTitle = "Cheque Calibration Sheet - Apex Solutions" };
        writer.BeginPage(Pt(layout.LeafWidthTmm), Pt(layout.LeafHeightTmm));

        double leafW = Pt(layout.LeafWidthTmm);

        // Vertical rules every 10 mm from the LEFT edge, each labelled with its distance from that edge.
        for (int xTmm = 0; xTmm <= layout.LeafWidthTmm; xTmm += StepTmm)
        {
            double x = Pt(xTmm);
            writer.Line(x, 0, x, Pt(layout.LeafHeightTmm), xTmm == 0 ? 0.8 : 0.2);
            writer.Text(x + 1, TopY(layout.LeafHeightTmm, 30), Mm(xTmm), LabelFontSize);
        }

        // Horizontal rules every 10 mm from the TOP edge, each labelled with its distance from that edge — the
        // same direction the Cheque Dimensions screen measures in, so the operator never has to subtract.
        for (int yTmm = 0; yTmm <= layout.LeafHeightTmm; yTmm += StepTmm)
        {
            double y = TopY(layout.LeafHeightTmm, yTmm);
            writer.Line(0, y, leafW, y, yTmm == 0 ? 0.8 : 0.2);
            writer.Text(2, y + 1, Mm(yTmm), LabelFontSize);
        }

        writer.Text(2, TopY(layout.LeafHeightTmm, 60),
            "Calibration sheet - distances in mm from the top-left corner", LabelFontSize, bold: true);

        return writer.Build();

        static string Mm(int tenthsMm) => (tenthsMm / 10).ToString(CultureInfo.InvariantCulture);
    }

    private static string Blank(string? value, string fallback) =>
        string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
}
