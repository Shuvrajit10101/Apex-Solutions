using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Apex.Ledger;
using Apex.Ledger.Io;
using Apex.Ledger.Reports;
using Apex.Ledger.Services;

namespace Apex.Ledger.Io.Tests;

/// <summary>
/// <b>T0-11 review C4/L1-04 — nothing the invoice renderer draws may land inside the page footer.</b>
///
/// <para><b>The defect.</b> <c>InvoicePdf.FirstHeaderHeight</c> reserved exactly two post-party-block header rows
/// (number/date and place-of-supply) while <c>DrawFirstHeader</c> draws a THIRD whenever <c>ReferenceNo</c> is set —
/// which RQ-11a makes the NORM on a purchase record, where that pair carries the supplier's own document number. The
/// paginator sizes page 1 from the RESERVED height (<c>InvoicePdf.cs</c>, the single <c>FirstHeaderHeight</c> call
/// site) while the drawing starts a whole row lower, so page 1 admitted one row more than it could hold. Measured on
/// the review's fixture: every item row dropped 11.00 pt (exactly <c>BodyFontSize + 2</c>) and page 1's last row —
/// six cells including the Amount — was drawn at y = 40.89 against a footer occupying [36, 44].</para>
///
/// <para><b>Why this asserts the INVARIANT and not the trigger.</b> The review's verifier corrected the finding on
/// exactly this point: the onset item count is FIXTURE-DEPENDENT (49 on the finding's fixture, 48 on the verifier's)
/// because it moves with the party-block height. A test pinned to a literal row count would pass on a fixture one
/// address line away from the one that bites. So this sweeps counts and shapes and asserts the property the renderer
/// itself states — nothing is drawn below <c>MarginBottom + FooterFontSize + 6</c>, the bottom guard
/// <c>InvoicePdf.Render</c> paginates against — with the footer baseline itself excluded because it IS the footer.</para>
///
/// <para><b>The overshoot was 14 pt, not 11.</b> The verifier's second correction: 11 pt from the unreserved
/// reference row plus a latent 3 pt that <c>DrawItemTableHeader</c> consumes (its <c>y -= 3</c> before the rule) and
/// that neither header measurement ever reserved. Correcting only the reference row would leave the arithmetic 3 pt
/// out of true and the continuation pages 3 pt out on their own — which is why the two-shape sweep below runs
/// documents long enough to put a row near the guard on page 2 as well as page 1.</para>
///
/// <para>Renderer-level, hand-built DTOs, no company and no projector: the defect is pure page arithmetic and a
/// fixture that had to be posted through the ledger would hide which term is wrong.</para>
/// </summary>
public sealed class InvoicePageBottomGuardTests
{
    /// <summary>One drawn string and the baseline it was drawn at, read back out of the page's content stream.</summary>
    private readonly record struct Placement(double X, double Y, string Text);

    private static readonly Regex StreamRx =
        new(@"stream\r?\n(.*?)endstream", RegexOptions.Singleline | RegexOptions.Compiled);

    /// <summary>The writer emits <c>x y Td</c> then <c>(text) Tj</c> for every string; nothing else moves the pen.</summary>
    private static readonly Regex TextRx =
        new(@"(-?[\d.]+) (-?[\d.]+) Td\s*\((.*?)\) Tj", RegexOptions.Singleline | RegexOptions.Compiled);

    /// <summary>Every page's placements, in page order. The content streams are the ONLY streams
    /// <c>PdfWriter.Build</c> emits (one per page, uncompressed), so stream order is page order.</summary>
    private static List<List<Placement>> Pages(byte[] pdf)
    {
        var text = Encoding.Latin1.GetString(pdf);
        var pages = new List<List<Placement>>();
        foreach (Match s in StreamRx.Matches(text))
        {
            var placements = new List<Placement>();
            foreach (Match t in TextRx.Matches(s.Groups[1].Value))
                placements.Add(new Placement(
                    double.Parse(t.Groups[1].Value, CultureInfo.InvariantCulture),
                    double.Parse(t.Groups[2].Value, CultureInfo.InvariantCulture),
                    t.Groups[3].Value));
            pages.Add(placements);
        }
        return pages;
    }

    private static InvoicePartyBlock Party(string name, int addressLines) => new()
    {
        Name = name,
        AddressLines = Enumerable.Range(1, addressLines).Select(i => name + " address line " + i).ToArray(),
        Gstin = "19AAAAA0000A1Z5",
        StateText = "West Bengal (19)",
    };

    /// <summary>
    /// A taxed invoice of <paramref name="items"/> identical rows. The money is deliberately odd to the paisa —
    /// a round figure would let a rounding defect through — but nothing here asserts money: the subject is geometry.
    /// </summary>
    private static InvoicePrintData Invoice(int items, string referenceNo, int addressLines, bool cancelled)
    {
        var rows = Enumerable.Range(1, items).Select(i => new InvoiceItemRow
        {
            Description = "Widget " + i.ToString(CultureInfo.InvariantCulture),
            HsnSac = "84713010",
            QuantityText = "1.000",
            RateText = "137.53",
            TaxableValue = new Money(137.53m),
        }).ToArray();
        var taxable = new Money(137.53m * items);
        var tax = GstService.ComputeLineTax(taxable, 1800, interState: false);

        return new InvoicePrintData
        {
            Seller = Party("Bright Traders", addressLines),
            Buyer = Party("Acme Retail", addressLines),
            DocumentTitle = GstReportSupport.TaxInvoiceTitle,
            InvoiceNumber = "INV-001",
            InvoiceDateText = "31-03-2025",
            ReferenceNo = referenceNo,
            ReferenceCaption = "Supplier Invoice No.",
            ReferenceDateText = string.IsNullOrEmpty(referenceNo) ? string.Empty : "08-04-2025",
            PlaceOfSupply = "West Bengal (19)",
            IsInterState = false,
            IsCancelled = cancelled,
            Items = rows,
            TaxRows = new[]
            {
                new InvoiceTaxRow { RateLabel = "18%", TaxableValue = taxable, Cgst = tax.Cgst, Sgst = tax.Sgst, Igst = Money.Zero },
            },
            TotalTaxable = taxable,
            TotalCgst = tax.Cgst,
            TotalSgst = tax.Sgst,
            TotalIgst = Money.Zero,
        };
    }

    /// <summary>
    /// The renderer's own bottom guard: <c>MarginBottom + FooterFontSize + 6</c>. This is the literal expression
    /// <c>InvoicePdf.Render</c> paginates against, restated here rather than hard-coded to 50 so a page config with
    /// different margins is measured against ITS guard.
    /// </summary>
    private static double Guard(PageConfig page) => page.MarginBottom + page.FooterFontSize + 6;

    /// <summary>
    /// Checks one rendered document and returns the tightest non-footer baseline it found, so the caller can prove
    /// the sweep actually pushed a page to its limit instead of asserting over comfortable documents.
    /// </summary>
    private static double AssertNothingIsDrawnInsideTheFooterBand(InvoicePrintData data, PageConfig page, string what)
    {
        var pdf = InvoicePdf.Render(data, new PrintConfig(), page);
        var pages = Pages(pdf);
        Assert.NotEmpty(pages);

        double tightest = double.MaxValue;
        for (int p = 0; p < pages.Count; p++)
        {
            foreach (var placement in pages[p])
            {
                // The footer IS the footer band: it is drawn at exactly MarginBottom by DrawFooter and is the one
                // thing that belongs there. Everything else is content.
                if (Math.Abs(placement.Y - page.MarginBottom) < 0.001 && placement.Text.Contains("Page ")) continue;

                Assert.True(placement.Y >= Guard(page),
                    $"{what}: page {p + 1} draws \"{placement.Text}\" at y={placement.Y:F2}, below the renderer's own "
                    + $"bottom guard of {Guard(page):F2} (MarginBottom {page.MarginBottom} + FooterFontSize "
                    + $"{page.FooterFontSize} + 6). The footer glyph box is "
                    + $"[{page.MarginBottom:F2}, {page.MarginBottom + page.FooterFontSize:F2}].");

                if (placement.Y < tightest) tightest = placement.Y;
            }
        }
        return tightest;
    }

    /// <summary>
    /// 🔴 The defect, swept rather than pinned. Every combination of item count, reference row and party-block
    /// height must keep every drawn string above the guard.
    ///
    /// <para>The sweep is what makes this fixture-independent: the onset row count moves with the party-block
    /// height, so the two address-line shapes below put the boundary at two different counts and the count range
    /// crosses both.</para>
    /// </summary>
    [Fact]
    public void No_page_ever_draws_its_content_inside_the_footer_band()
    {
        var page = new PageConfig();
        double tightest = double.MaxValue;
        int measured = 0;

        foreach (int addressLines in new[] { 1, 2, 3 })
            foreach (var referenceNo in new[] { string.Empty, "SUP/001" })
                foreach (int items in Enumerable.Range(40, 36))
                {
                    var what = $"items={items} ref={(referenceNo.Length == 0 ? "(none)" : referenceNo)} addr={addressLines}";
                    double t = AssertNothingIsDrawnInsideTheFooterBand(
                        Invoice(items, referenceNo, addressLines, cancelled: false), page, what);
                    if (t < tightest) tightest = t;
                    measured++;
                }

        Assert.True(measured == 3 * 2 * 36, $"sweep ran {measured} documents, expected {3 * 2 * 36}.");

        // NON-VACUITY. A sweep of comfortable documents would prove nothing: the assertion only bites where a page
        // is FULL. Somewhere in the sweep a page must place a row within one row height of the guard.
        Assert.True(tightest < Guard(page) + page.RowHeight,
            $"the sweep never filled a page: the tightest baseline anywhere was {tightest:F2}, more than one row "
            + $"height ({page.RowHeight}) clear of the {Guard(page):F2} guard, so this test could not have failed.");
    }

    /// <summary>
    /// The CANCELLED over-print costs a title-band row on EVERY page (Phase 10.11 S3), so it re-asks the same
    /// question of the continuation header that the reference row asks of the first one.
    /// </summary>
    [Fact]
    public void A_cancelled_document_keeps_the_same_guard_on_every_page()
    {
        var page = new PageConfig();
        foreach (int items in Enumerable.Range(40, 36))
            AssertNothingIsDrawnInsideTheFooterBand(
                Invoice(items, "SUP/001", addressLines: 2, cancelled: true), page,
                $"cancelled items={items}");
    }

    /// <summary>
    /// The guard is expressed in the page config, not in a literal, so a non-default page must hold it too —
    /// Letter, landscape, and a deliberately tight bottom margin, each with the reference row present.
    /// </summary>
    [Theory]
    [InlineData(PageSize.Letter, PageOrientation.Portrait, 36.0)]
    [InlineData(PageSize.A4, PageOrientation.Landscape, 36.0)]
    [InlineData(PageSize.A4, PageOrientation.Portrait, 18.0)]
    public void The_guard_holds_on_a_non_default_page(PageSize size, PageOrientation orientation, double marginBottom)
    {
        var page = new PageConfig { Size = size, Orientation = orientation, MarginBottom = marginBottom };
        foreach (int items in Enumerable.Range(20, 60))
            AssertNothingIsDrawnInsideTheFooterBand(
                Invoice(items, "SUP/001", addressLines: 2, cancelled: false), page,
                $"{size}/{orientation}/mb{marginBottom} items={items}");
    }
}
