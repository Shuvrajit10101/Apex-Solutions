using System;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Io;

namespace Apex.Ledger.Io.Tests;

/// <summary>
/// Tests for <see cref="ChequePdf"/> — the renderer that inks a <b>pre-printed cheque leaf</b> (catalog §8
/// Banking; census row 8.4).
///
/// <para><b>Vendor grounding.</b> The five placed elements and their fields come from the only vendor page that
/// enumerates the Cheque Dimensions screen —
/// <c>help.tallysolutions.com/docs/te9rel51/Advanced_Features/Advanced_Accounting_Features/Creation_Mode.htm</c>,
/// section "Cheque Dimensions" (Cheque Date with its "Distance between Characters"; Party's Payee Name with its
/// "Width area (default: 135)"; the two-line Amount in Words with "Print Currency Formal Name"; Amount in Figures
/// with "Print Currency Symbol"; Signatory Details). The per-print nudge is
/// <c>help.tallysolutions.com/docs/te9rel53/Banking/Cheque_Printing.htm</c>, "Adjust Distance From Top Edge
/// (in mm)" / "…From Left Edge (in mm)".</para>
///
/// <para><b>🔴 Why these tests read <c>Td</c> COORDINATES out of the content stream and not a view-model flag.</b>
/// The whole risk of this file is a coordinate flip: every cheque dimension is measured DOWN from the top edge of
/// the leaf and every PDF coordinate is measured UP from the bottom. A test that asserted "the payee name is on
/// the page" passes just as happily when the cheque prints mirrored top-to-bottom — and the artefact is a
/// negotiable instrument, so being on the page is not the property that matters. Each assertion below therefore
/// names the exact point the ink must land on.</para>
///
/// <para>Every test here fails on <c>main</c> by construction: <c>ChequePdf</c> does not exist there.</para>
/// </summary>
public sealed class ChequePdfTests
{
    private static string AsLatin1(byte[] bytes) => Encoding.Latin1.GetString(bytes);

    /// <summary>Tenths of a millimetre → PDF points, as a stream-formatted number ("0.###", invariant).</summary>
    private static string PtText(double tenthsMm) =>
        Math.Round(tenthsMm * 72.0 / 254.0, 3, MidpointRounding.AwayFromZero)
            .ToString("0.###", CultureInfo.InvariantCulture);

    /// <summary>The "&lt;x&gt; &lt;y&gt; Td" the writer emits for a text run placed at that point.</summary>
    private static string TdAt(double xTenthsMm, double yPointsFromBottom) =>
        $"{PtText(xTenthsMm)} {Math.Round(yPointsFromBottom, 3, MidpointRounding.AwayFromZero).ToString("0.###", CultureInfo.InvariantCulture)} Td";

    /// <summary>
    /// A deliberately round leaf and set of offsets so every expected coordinate is checkable by hand:
    /// leaf 200 mm x 92 mm; date at 20 mm from the top, 25 mm from the left, 5 mm pitch; payee at 30/25;
    /// words line 1 at 40/25 and line 2 at 47/25; figures at 40/160; signature at 70/140.
    /// </summary>
    private static ChequeLayout RoundLayout() => new()
    {
        LeafWidthTmm = 2000,
        LeafHeightTmm = 920,
        DateTopTmm = 200,
        DateLeftTmm = 250,
        DateCharPitchTmm = 50,
        PayeeTopTmm = 300,
        PayeeLeftTmm = 250,
        PayeeWidthTmm = 1350,
        WordsLine1TopTmm = 400,
        WordsLine1LeftTmm = 250,
        WordsLine2TopTmm = 470,
        WordsLine2LeftTmm = 250,
        WordsWidthTmm = 1400,
        FiguresTopTmm = 400,
        FiguresLeftTmm = 1600,
        FiguresWidthTmm = 350,
        SignTopTmm = 700,
        SignLeftTmm = 1400,
        SignWidthTmm = 500,
        SignHeightTmm = 150,
        Salutation1 = "For Apex Solutions",
    };

    private static ChequePrintData Cheque(decimal amount = 12500.75m) => new()
    {
        PayeeName = "Acme Supplies",
        Amount = Money.FromRupees(amount),
        ChequeDate = new DateOnly(2024, 7, 9),
        InstrumentNumber = "100123",
        BankName = "HDFC Bank",
        CompanyName = "Apex Solutions",
    };

    // ================================================================ the page IS the leaf

    [Fact]
    public void The_page_is_the_size_of_the_leaf_and_the_pdf_is_valid_and_debranded()
    {
        var layout = RoundLayout();
        var s = AsLatin1(ChequePdf.Render(Cheque(), layout));

        Assert.StartsWith("%PDF-", s);
        Assert.Contains("%%EOF", s);
        Assert.DoesNotContain("tally", s.ToLowerInvariant());   // ER-11 brand guard (body AND /Title)

        // 200 mm x 92 mm in points — NOT A4. A cheque printed on an A4 page is ink in the wrong place.
        Assert.Contains($"/MediaBox [0 0 {PtText(2000)} {PtText(920)}]", s);
    }

    // ================================================================ 🔴 the top-edge flip

    /// <summary>
    /// 🔴 The single assertion this whole file exists for. A dimension of 20 mm "from the top edge" of a 92 mm
    /// leaf must render at y = 72 mm in PDF space (92 − 20), not at 20. Get this wrong once and every cheque in
    /// the country prints mirrored top-to-bottom.
    /// </summary>
    [Fact]
    public void Top_edge_offsets_are_measured_downward_from_the_leaf_top()
    {
        var layout = RoundLayout();
        var s = AsLatin1(ChequePdf.Render(Cheque(), layout));

        double expectedY = ChequePdf.Pt(920) - ChequePdf.Pt(200);   // 92 mm − 20 mm
        Assert.Contains(TdAt(250, expectedY), s);                   // the date's first glyph

        // And it is emphatically NOT the un-flipped reading.
        Assert.DoesNotContain(TdAt(250, ChequePdf.Pt(200)), s);

        // The helper agrees with the arithmetic, so nobody can "fix" one without the other.
        Assert.Equal(expectedY, ChequePdf.TopY(920, 200), 6);
    }

    // ================================================================ the NPCI boxed date

    /// <summary>
    /// The date is the NPCI <c>d d m m y y y y</c> boxed field: EIGHT separate glyphs, each advanced by the
    /// configured "Distance between Characters". Printing "09/07/2024" as one string straddles the printed boxes.
    /// </summary>
    [Fact]
    public void The_date_prints_as_eight_glyphs_advanced_by_the_configured_character_pitch()
    {
        var layout = RoundLayout();
        var s = AsLatin1(ChequePdf.Render(Cheque(), layout));

        double y = ChequePdf.TopY(920, 200);
        var digits = "09072024";                                     // 9 July 2024, ddMMyyyy
        for (int i = 0; i < digits.Length; i++)
        {
            Assert.Contains(TdAt(250 + i * 50, y), s);
            // Each glyph is shown on its own, at its own point.
            Assert.Contains($"{TdAt(250 + i * 50, y)}\n({digits[i]}) Tj", s);
        }

        // No single-string date anywhere on the leaf.
        Assert.DoesNotContain("(09/07/2024) Tj", s);
        Assert.DoesNotContain("(09072024) Tj", s);
    }

    // ================================================================ the amount in words is NOT reimplemented

    /// <summary>
    /// 🔴 The single most likely accidental duplication in this wave. The words on the leaf must be, character
    /// for character, what <see cref="IndianAmountInWords"/> produces — the paisa-exact, Indian-grouped,
    /// culture-invariant converter the tax invoice already uses.
    /// </summary>
    [Fact]
    public void Amount_in_words_comes_from_IndianAmountInWords_and_is_not_reimplemented()
    {
        var layout = RoundLayout();
        layout.WordsWidthTmm = 1900;                                  // wide enough to keep it on one line
        var data = Cheque(12500.75m);
        var s = AsLatin1(ChequePdf.Render(data, layout));

        var expected = IndianAmountInWords.Convert(data.Amount.Amount);
        Assert.Contains($"({expected}) Tj", s);
    }

    /// <summary>S3's "Print Currency Formal Name" selects the two-argument overload, naming the company's base
    /// currency rather than the fixed wording.</summary>
    [Fact]
    public void Print_currency_formal_name_uses_the_supplied_currency_names()
    {
        var layout = RoundLayout();
        layout.WordsWidthTmm = 1900;
        layout.PrintCurrencyFormalName = true;

        var data = new ChequePrintData
        {
            PayeeName = "Acme Supplies",
            Amount = Money.FromRupees(101.50m),
            ChequeDate = new DateOnly(2024, 7, 9),
            InstrumentNumber = "100123",
            CurrencyFormalName = "Indian Rupees",
            CurrencyMinorName = "Paise",
        };

        var s = AsLatin1(ChequePdf.Render(data, layout));
        var expected = IndianAmountInWords.Convert(data.Amount.Amount, "Indian Rupees", "Paise");
        Assert.Contains($"({expected}) Tj", s);
        Assert.Contains("Indian Rupees", s);
    }

    /// <summary>S3's "Print Currency Symbol" prefixes the figures. Off by default, and the symbol comes from the
    /// caller so nothing is guessed.</summary>
    [Fact]
    public void Print_currency_symbol_prefixes_only_the_figures_and_only_when_asked()
    {
        var layout = RoundLayout();
        var plain = AsLatin1(ChequePdf.Render(Cheque(), layout));
        Assert.DoesNotContain("(Rs. ", plain);

        layout.PrintCurrencySymbol = true;
        var withSymbol = AsLatin1(ChequePdf.Render(Cheque(), layout));
        Assert.Contains("Rs. 12,500.75", withSymbol);
    }

    // ================================================================ unset elements are SKIPPED

    /// <summary>
    /// 🔴 An element whose two offsets are both zero was never placed by the operator, and is skipped. Printing
    /// the payee name at the top-left corner of a negotiable instrument is worse than not printing it.
    /// </summary>
    [Fact]
    public void An_element_with_no_offsets_is_skipped_rather_than_printed_at_the_origin()
    {
        var layout = RoundLayout();
        layout.PayeeTopTmm = 0;
        layout.PayeeLeftTmm = 0;
        layout.FiguresTopTmm = 0;
        layout.FiguresLeftTmm = 0;

        var s = AsLatin1(ChequePdf.Render(Cheque(), layout));

        Assert.DoesNotContain("(Acme Supplies) Tj", s);              // the payee is not inked at all
        Assert.DoesNotContain("12,500.75", s);                       // nor the figures
        Assert.DoesNotContain($"{TdAt(0, ChequePdf.Pt(920))} ", s);  // and nothing lands on the corner

        // The elements that WERE placed still print, so this is a skip and not a blanket refusal.
        Assert.Contains("(For Apex Solutions) Tj", s);
    }

    // ================================================================ the per-print nudge

    /// <summary>
    /// S4's "Adjust Distance From Top/Left Edge (in mm)" shifts EVERY element and, as that page states, "does not
    /// affect the settings of cheque dimensions pre-configured for the selected cheque format" — so the layout
    /// object itself must come back unchanged.
    /// </summary>
    [Fact]
    public void The_per_print_nudge_moves_every_element_and_never_mutates_the_stored_layout()
    {
        var layout = RoundLayout();
        var nudged = new ChequePrintData
        {
            PayeeName = "Acme Supplies",
            Amount = Money.FromRupees(12500.75m),
            ChequeDate = new DateOnly(2024, 7, 9),
            InstrumentNumber = "100123",
            NudgeTopTmm = 30,        // +3 mm down
            NudgeLeftTmm = 40,       // +4 mm right
        };

        var s = AsLatin1(ChequePdf.Render(nudged, layout));

        // The payee moved by exactly the nudge, in both axes and in the right directions.
        Assert.Contains(TdAt(250 + 40, ChequePdf.TopY(920, 300 + 30)), s);
        Assert.DoesNotContain(TdAt(250, ChequePdf.TopY(920, 300)), s);

        // 🔴 The stored dimensions are untouched — the nudge is a render-time addend, never a write-back.
        Assert.Equal(300, layout.PayeeTopTmm);
        Assert.Equal(250, layout.PayeeLeftTmm);
        Assert.Equal(920, layout.LeafHeightTmm);
    }

    // ================================================================ the guards

    /// <summary>
    /// Guards on a negotiable instrument refuse rather than degrade. Each message is the text the operator sees
    /// instead of a wrong cheque.
    /// </summary>
    [Fact]
    public void An_unset_leaf_a_blank_number_and_a_blank_payee_each_refuse_with_their_own_message()
    {
        var good = RoundLayout();

        var noLeaf = RoundLayout();
        noLeaf.LeafWidthTmm = 0;
        noLeaf.LeafHeightTmm = 0;
        Assert.Contains("Cheque dimensions are not set", ChequePdf.Validate(Cheque(), noLeaf));
        Assert.Throws<InvalidOperationException>(() => ChequePdf.Render(Cheque(), noLeaf));

        var noNumber = new ChequePrintData { PayeeName = "Acme", Amount = Money.FromRupees(1m), InstrumentNumber = "" };
        Assert.Contains("no cheque number", ChequePdf.Validate(noNumber, good));
        Assert.Throws<InvalidOperationException>(() => ChequePdf.Render(noNumber, good));

        var noPayee = new ChequePrintData { PayeeName = "", Amount = Money.FromRupees(1m), InstrumentNumber = "1" };
        Assert.Contains("names no payee", ChequePdf.Validate(noPayee, good));
        Assert.Throws<InvalidOperationException>(() => ChequePdf.Render(noPayee, good));

        Assert.Null(ChequePdf.Validate(Cheque(), good));
    }

    /// <summary>The drawer's name is off by default (the leaf is normally already printed with it) and appears
    /// only when the vendor's toggle asks for it.</summary>
    [Fact]
    public void The_company_name_is_inked_only_when_the_vendor_toggle_asks_for_it()
    {
        var layout = RoundLayout();
        Assert.DoesNotContain("(Apex Solutions) Tj", AsLatin1(ChequePdf.Render(Cheque(), layout)));

        var withName = new ChequePrintData
        {
            PayeeName = "Acme Supplies",
            Amount = Money.FromRupees(100m),
            ChequeDate = new DateOnly(2024, 7, 9),
            InstrumentNumber = "100123",
            CompanyName = "Apex Solutions",
            PrintCompanyName = true,
        };
        Assert.Contains("(Apex Solutions) Tj", AsLatin1(ChequePdf.Render(withName, layout)));
    }

    // ================================================================ determinism (the house rule)

    [Fact]
    public void Rendering_the_same_cheque_twice_is_byte_identical()
    {
        var layout = RoundLayout();
        Assert.Equal(ChequePdf.Render(Cheque(), layout), ChequePdf.Render(Cheque(), layout));
        Assert.Equal(
            ChequePdf.RenderCalibrationSheet(layout),
            ChequePdf.RenderCalibrationSheet(layout));
    }

    /// <summary>
    /// The output must not depend on the ambient culture. A culture whose decimal separator is "," would emit
    /// "12,5 Td" and produce a syntactically broken content stream — this is one of the seven platform
    /// assumptions class of defect, caught here rather than on a CI leg.
    /// </summary>
    [Fact]
    public void Rendering_is_culture_invariant()
    {
        var layout = RoundLayout();
        var original = CultureInfo.CurrentCulture;
        try
        {
            var reference = ChequePdf.Render(Cheque(), layout);
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");   // decimal comma, dot grouping
            Assert.Equal(reference, ChequePdf.Render(Cheque(), layout));
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    // ================================================================ the calibration sheet

    /// <summary>
    /// The calibration sheet is how the operator obtains real millimetres without anyone inventing one: a 10 mm
    /// grid ruled from the TOP-LEFT corner, labelled in the same direction the Cheque Dimensions screen measures
    /// in, on a page the exact size of the leaf.
    /// </summary>
    [Fact]
    public void The_calibration_sheet_rules_a_ten_millimetre_grid_from_the_top_left_of_the_leaf()
    {
        var layout = RoundLayout();
        var s = AsLatin1(ChequePdf.RenderCalibrationSheet(layout));

        Assert.Contains($"/MediaBox [0 0 {PtText(2000)} {PtText(920)}]", s);

        // Labelled every 10 mm across (0..200) and down (0..90).
        foreach (var mm in new[] { "0", "10", "50", "100", "200" }) Assert.Contains($"({mm}) Tj", s);
        Assert.Contains("(90) Tj", s);

        // 21 vertical rules (0..200 mm) and 10 horizontal ones (0..90 mm) — every one a stroked line.
        var strokes = Regex.Matches(s, @"\bS\n").Count;
        Assert.Equal(21 + 10, strokes);

        // The horizontal rule for "0 mm from the top" sits at the TOP of the page, not the bottom.
        Assert.Contains($"0 {PtText(920)} m", s);

        // Needs only a leaf size; an unset leaf refuses with the same message the renderer uses.
        var noLeaf = new ChequeLayout();
        var ex = Assert.Throws<InvalidOperationException>(() => ChequePdf.RenderCalibrationSheet(noLeaf));
        Assert.Contains("Cheque dimensions are not set", ex.Message);
    }

    // ================================================================ the words wrap by MEASURE

    /// <summary>
    /// The two amount-in-words lines wrap by measured width, and the second line is placed at ITS OWN offsets —
    /// the vendor models it as an independently-placed line, not as a fixed leading below the first. The derived
    /// "height (gap) between lines" is exactly the difference, so the two can never contradict.
    /// </summary>
    [Fact]
    public void A_long_amount_in_words_continues_on_the_second_line_at_its_own_offsets()
    {
        var layout = RoundLayout();
        layout.WordsWidthTmm = 400;                                  // narrow: forces a second line
        layout.WordsLine2LeftTmm = 300;                              // deliberately different from line 1

        var s = AsLatin1(ChequePdf.Render(Cheque(9876543.21m), layout));

        Assert.Contains(TdAt(250, ChequePdf.TopY(920, 400)), s);     // line 1 at its own point
        Assert.Contains(TdAt(300, ChequePdf.TopY(920, 470)), s);     // line 2 at ITS own point
        Assert.Equal(70, layout.WordsLineGapTmm);                    // 47 mm − 40 mm, derived not stored
    }
}
