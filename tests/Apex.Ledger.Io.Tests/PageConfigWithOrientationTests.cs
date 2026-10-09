using System;
using System.Linq;
using System.Reflection;
using System.Text;
using Xunit;

namespace Apex.Ledger.Io.Tests;

/// <summary>
/// 🔴 <b>THE AUTO-FIT ORIENTATION, AND THE HAND-WRITTEN CONFIG COPY IT DEPENDS ON.</b>
///
/// <para><see cref="ReportPdf"/> turns a tabular report onto its side when its columns cannot otherwise hold
/// their contents, and it does that by rebuilding the <see cref="PageConfig"/> through
/// <see cref="PageConfig.WithOrientation"/>. That method is a hand-written property-by-property copy, which is a
/// silent-drop waiting to happen: add a knob to <c>PageConfig</c>, forget the copy, and the knob is lost on
/// exactly the wide reports that take this path — the operator's copy count, page range, format or paper choice
/// ignored, with nothing failing and no error. The first test below walks the public init properties by
/// reflection and names any that the copy does not carry.</para>
/// </summary>
public sealed class PageConfigWithOrientationTests
{
    /// <summary>
    /// 🔴 EVERY SETTABLE PROPERTY OF <see cref="PageConfig"/> SURVIVES <see cref="PageConfig.WithOrientation"/>,
    /// CHECKED BY REFLECTION RATHER THAN BY READING THE METHOD.
    ///
    /// <para>Each property is set to a value distinguishable from its default, the config is copied, and the copy
    /// is compared property by property. <see cref="PageConfig.Orientation"/> is the one that is expected to
    /// differ — it is the argument. A property added to the class and omitted from the copy fails here BY NAME.</para>
    /// </summary>
    [Fact]
    public void WithOrientation_carries_every_settable_property_except_the_orientation_itself()
    {
        var settable = typeof(PageConfig)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.SetMethod is not null)
            .ToList();

        Assert.NotEmpty(settable);

        // A source config in which NOTHING holds its default value, so a dropped property shows up as the default
        // rather than coincidentally matching.
        var source = new PageConfig
        {
            Size = PageSize.Letter,
            Orientation = PageOrientation.Portrait,
            MarginLeft = 11,
            MarginRight = 12,
            MarginTop = 13,
            MarginBottom = 14,
            HeaderText = "header probe",
            FooterText = "footer probe",
            TitleFontSize = 21,
            SubtitleFontSize = 22,
            HeaderFontSize = 23,
            BodyFontSize = 24,
            FooterFontSize = 25,
            RowHeight = 26,
            Format = PrintFormat.DotMatrix,
            Paper = PaperKind.PrePrinted,
            Copies = 7,
            FirstPage = 3,
            LastPage = 9,
            StartPageNumber = 5,
            AutoFitOrientation = false,
        };

        // Nothing above may still be at its default, or this test would pass while carrying nothing.
        // Orientation is excluded: it is the ARGUMENT this method changes, so the probe holds the default on
        // purpose and the copy is expected to differ. Every other property must be distinguishable.
        var untouched = settable
            .Where(p => p.Name != nameof(PageConfig.Orientation))
            .Where(p => Equals(p.GetValue(source), p.GetValue(new PageConfig())))
            .Select(p => p.Name)
            .ToList();
        Assert.True(
            untouched.Count == 0,
            "This test's probe config leaves these properties at their DEFAULT, so a copy that dropped them "
            + "would pass anyway. Give each one a distinguishable value: " + string.Join(", ", untouched));

        var copy = source.WithOrientation(PageOrientation.Landscape);

        Assert.Equal(PageOrientation.Landscape, copy.Orientation);
        foreach (var p in settable)
        {
            if (p.Name == nameof(PageConfig.Orientation)) continue;
            Assert.Equal(p.GetValue(source), p.GetValue(copy));
        }
    }

    /// <summary>
    /// 🔴 A REPORT THAT FITS IS NOT TURNED, AND ITS BYTES DO NOT MOVE (ER-13). The auto-fit must be invisible to
    /// every document that was already printing correctly — which is all but the widest of them.
    /// </summary>
    [Fact]
    public void A_report_that_fits_portrait_keeps_its_orientation_and_its_bytes()
    {
        var report = NarrowReport();

        var auto = ReportPdf.Render(report, new PageConfig { AutoFitOrientation = true });
        var pinned = ReportPdf.Render(report, new PageConfig { AutoFitOrientation = false });

        Assert.Equal(pinned, auto);
        Assert.Contains("/MediaBox [0 0 595.276 841.89", AsLatin1(auto));
    }

    /// <summary>
    /// 🔴 THE PAGE REALLY TURNS, ASSERTED ON THE EMITTED <c>/MediaBox</c> AND ON THE FIGURES THAT NOW FIT.
    ///
    /// <para>Nine columns of which five must each hold <c>99,99,99,999.00</c> need ~535pt; A4 portrait offers
    /// 523.276pt of content, so no redistribution of the portrait page fits them and the figures were cut. The
    /// landscape sheet offers 769.89pt. Both halves are asserted: the <c>/MediaBox</c> is the landscape one, and
    /// every figure is drawn whole with nothing ellipsised.</para>
    /// </summary>
    [Fact]
    public void A_report_too_wide_for_portrait_is_turned_and_its_figures_print_whole()
    {
        var report = WideReport();
        string pdf = AsLatin1(ReportPdf.Render(report, new PageConfig()));

        Assert.Contains("/MediaBox [0 0 841.89 595.276", pdf);
        Assert.Equal(5, Occurrences(pdf, "(99,99,99,999.00) Tj"));
        Assert.DoesNotContain("99,99,99,99...", pdf);
        Assert.DoesNotContain("99,99,99,999...", pdf);
    }

    /// <summary>
    /// 🔴 PINNING THE ORIENTATION STILL PINS IT. A caller that says <c>AutoFitOrientation = false</c> — a
    /// pre-printed stationery run, or a test asserting the portrait geometry itself — gets portrait, cut figures
    /// and all. The escape hatch has to actually work, or "auto" is "always".
    /// </summary>
    [Fact]
    public void Pinning_the_orientation_keeps_portrait_even_when_the_figures_will_cut()
    {
        string pdf = AsLatin1(ReportPdf.Render(WideReport(), new PageConfig { AutoFitOrientation = false }));

        Assert.Contains("/MediaBox [0 0 595.276 841.89", pdf);

        // And the cut is still there, which is the honest consequence of pinning the page: no figure printed
        // whole, because 535pt of requirement does not fit 523.276pt of paper however it is divided up. This is
        // asserted rather than merely allowed, so the two cases cannot quietly become the same case. The cut
        // point itself is NOT written as a literal — it moves with the caption widths, and pinning a digit count
        // here would make this test a transcription of today's arithmetic rather than a statement about it.
        Assert.Equal(0, Occurrences(pdf, "(99,99,99,999.00) Tj"));
        Assert.True(
            DrawnOperands(pdf).Any(IsCutFigure),
            "Portrait was pinned on a report that does not fit it, so a money figure must be drawn CUT. None "
            + "was, which means either the figures now fit portrait (and this test's premise is stale) or they "
            + "are not reaching the page at all. Drawn: " + string.Join(" | ", DrawnOperands(pdf)));
    }

    /// <summary>
    /// 🔴 PRE-PRINTED STATIONERY IS NEVER TURNED, EVEN WHEN THE FIGURES WILL CUT.
    ///
    /// <para>The sheet is already in the tray the right way up, with a letterhead and a ruled grid on it, and the
    /// operator aligned it there. Rotating the image would print the report sideways across the letterhead — a
    /// worse outcome than the clipped figure it was avoiding, and one the operator cannot correct from inside the
    /// application. The width redistribution still applies on pre-printed paper; only the rotation is suppressed.</para>
    /// </summary>
    [Fact]
    public void Pre_printed_stationery_is_never_turned()
    {
        string pdf = AsLatin1(ReportPdf.Render(WideReport(), new PageConfig { Paper = PaperKind.PrePrinted }));

        Assert.Contains("/MediaBox [0 0 595.276 841.89", pdf);
        Assert.DoesNotContain("/MediaBox [0 0 841.89 595.276", pdf);
    }

    /// <summary>A report whose columns comfortably fit A4 portrait.</summary>
    private static PrintReport NarrowReport() => new()
    {
        Title = "Trial Balance",
        Columns = new[]
        {
            new PrintColumn("Particulars", 3.0, CellAlign.Left),
            new PrintColumn("Debit", 1.5, CellAlign.Right),
            new PrintColumn("Credit", 1.5, CellAlign.Right),
        },
        Rows = new[] { new PrintRow { Cells = new[] { "Sales", "1,00,000.00", string.Empty } } },
    };

    /// <summary>
    /// The GSTR-1 column shape: four label columns and five figure columns, each figure column holding the widest
    /// amount an Indian money column must carry below a hundred crore. The weights are the ones
    /// <c>ReportPrintProjector.ProjectBanded</c> emits — 3 for the leading label, 2.2 for the others, 1.5 for a
    /// figure.
    /// </summary>
    private static PrintReport WideReport() => new()
    {
        Title = "GSTR-1",
        Columns = new[]
        {
            new PrintColumn("Party / HSN", 3.0, CellAlign.Left),
            new PrintColumn("GSTIN / Desc.", 2.2, CellAlign.Left),
            new PrintColumn("Invoice / UQC", 2.2, CellAlign.Left),
            new PrintColumn("POS/Qty", 2.2, CellAlign.Left),
            new PrintColumn("Taxable", 1.5, CellAlign.Right),
            new PrintColumn("CGST", 1.5, CellAlign.Right),
            new PrintColumn("SGST", 1.5, CellAlign.Right),
            new PrintColumn("IGST", 1.5, CellAlign.Right),
            new PrintColumn("Cess", 1.5, CellAlign.Right),
        },
        Rows = new[]
        {
            new PrintRow
            {
                Cells = new[]
                {
                    "A Party", "29ABCDE1234F1Z5", "INV/0001", "29-Karnataka",
                    "99,99,99,999.00", "99,99,99,999.00", "99,99,99,999.00",
                    "99,99,99,999.00", "99,99,99,999.00",
                },
            },
        },
    };

    /// <summary>A drawn cell that is a money figure cut short: digits and separators, then the ellipsis
    /// <c>PdfWriter.FitToWidth</c> appends when it has to cut.</summary>
    private static bool IsCutFigure(string drawnCell)
    {
        if (!drawnCell.EndsWith("...", StringComparison.Ordinal)) return false;
        string head = drawnCell[..^3];
        return head.Length > 0 && head.All(ch => char.IsAsciiDigit(ch) || ch is ',' or '.');
    }

    /// <summary>Every <c>Tj</c> operand in an uncompressed content stream: the literal text of one drawn cell,
    /// already through <c>FitToWidth</c>. None of the strings this file draws contains a parenthesis, so the
    /// simple scan back from <c>") Tj"</c> is sound here.</summary>
    private static System.Collections.Generic.List<string> DrawnOperands(string pdf)
    {
        var drawn = new System.Collections.Generic.List<string>();
        int i = 0;
        while (true)
        {
            int close = pdf.IndexOf(") Tj", i, StringComparison.Ordinal);
            if (close < 0) break;
            int open = pdf.LastIndexOf('(', close);
            if (open < 0) break;
            drawn.Add(pdf.Substring(open + 1, close - open - 1));
            i = close + 4;
        }
        return drawn;
    }

    private static int Occurrences(string haystack, string needle)
    {
        int n = 0, i = 0;
        while ((i = haystack.IndexOf(needle, i, StringComparison.Ordinal)) >= 0) { n++; i += needle.Length; }
        return n;
    }

    private static string AsLatin1(byte[] bytes) => Encoding.Latin1.GetString(bytes);
}
