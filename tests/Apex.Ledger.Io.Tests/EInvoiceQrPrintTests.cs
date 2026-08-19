using System;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Apex.Ledger;
using Apex.Ledger.Io;

namespace Apex.Ledger.Io.Tests;

/// <summary>
/// <b>Census T0-9 — the e-invoice QR reaches the printed document.</b>
///
/// <para><b>The defect these tests close, as it was measured.</b> An e-invoiced Sales supply was posted through the
/// shipped UI, an IRP response recorded (status Generated, a 64-character IRN and an 804-character signed QR both
/// persisted), and the invoice rendered. The 3,938-byte PDF contained the IRN string: <b>no</b>. It contained
/// <c>/XObject</c>: <b>no</b>. It contained <c>/Subtype /Image</c>: <b>no</b>. Image-draw (<c>Do</c>) operators:
/// <b>zero</b>. <see cref="InvoicePrintData"/> properties naming an IRN or a QR: <b>zero</b>. And
/// <see cref="PdfWriter"/>'s entire public drawing surface was <c>BeginPage, Build, Line, Text</c> — so the omission
/// was not an oversight in the layout, it was structurally unreachable.</para>
///
/// <para><b>Why that is a legal defect and not a cosmetic one.</b> CGST Rule 46(r) makes the "Quick Response code,
/// having embedded Invoice Reference Number (IRN) in it" a mandatory particular "in case invoice has been issued in
/// the manner prescribed under sub-rule (4) of rule 48", and Rule 48(5) says an invoice issued by such a person "in
/// any manner other than the manner specified in the said sub-rule <b>shall not be treated as an invoice</b>". The
/// recipient's input tax credit hangs off a document the law would decline to recognise. Sources:
/// <c>https://taxinformation.cbic.gov.in/</c> (CGST Rules 46, 48); GSTN e-invoice FAQ v1.4 (30-03-2021) Q71 —
/// "The QR code … which comes as part of signed JSON from IRP, shall be extracted and printed on the invoice".</para>
///
/// <para><b>The corpus is silent on every part of this</b> (A14 sweep, 2026-08-19: zero occurrences of "IRN", "QR",
/// "IRP", "Ack No" or "digital signature" across all ten source PDFs), so nothing here narrows an attested behaviour.
/// The size, the placement, the error-correction level, printing the IRN as text and printing the QR on page 1 only
/// are all <b>OURS</b>, each recorded as such at the constant that decides it.</para>
/// </summary>
public sealed class EInvoiceQrPrintTests
{
    private static string AsLatin1(byte[] b) => Encoding.Latin1.GetString(b);

    /// <summary>A JWS-shaped signed QR carrying a NONCE. The nonce is what makes the byte comparisons below able to
    /// fail: a test that encoded a fixed string and compared it against the same fixed string would be
    /// self-consistent, and any transformation applied to BOTH sides would survive it.</summary>
    private static string SignedQr(string nonce)
    {
        string B64(string s) => Convert.ToBase64String(Encoding.ASCII.GetBytes(s))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
        return B64("{\"alg\":\"RS256\",\"typ\":\"JWT\"}") + "."
             + B64("{\"data\":\"{\\\"SellerGstin\\\":\\\"27AAPFU0939F1ZV\\\",\\\"Nonce\\\":\\\"" + nonce
                   + "\\\",\\\"TotInvVal\\\":55810.14}\"}") + "."
             + new string('A', 342);
    }

    private const string Irn = "a5c12bbe4c1f0b1b1cfa4e0c2b4a63c9d8e7f60a1b2c3d4e5f60718293a4b5c6";

    private static InvoicePrintData Invoice(string signedQr = "", string irn = "", string ackNo = "", string ackDate = "")
        => new()
        {
            Seller = new InvoicePartyBlock { Name = "Acme Traders Pvt Ltd", Gstin = "27AAPFU0939F1ZV", StateText = "Maharashtra (27)" },
            Buyer = new InvoicePartyBlock { Name = "Local Customer", Gstin = "27AAPFU0939F1ZV", StateText = "Maharashtra (27)" },
            InvoiceNumber = "1",
            InvoiceDateText = "01-04-2024",
            PlaceOfSupply = "Maharashtra (27)",
            IsInterState = false,
            EInvoiceSignedQr = signedQr,
            EInvoiceIrn = irn,
            EInvoiceAckNo = ackNo,
            EInvoiceAckDateText = ackDate,
            Items = new[]
            {
                new InvoiceItemRow
                {
                    Description = "Widget", HsnSac = "847130", QuantityText = "60.125 Nos",
                    RateText = "786.64", TaxableValue = Money.FromRupees(47_296.73m),
                },
            },
            TaxRows = new[]
            {
                new InvoiceTaxRow
                {
                    RateLabel = "18%", TaxableValue = Money.FromRupees(47_296.73m),
                    Cgst = Money.FromRupees(4_256.71m), Sgst = Money.FromRupees(4_256.70m), Igst = Money.Zero,
                },
            },
            TotalTaxable = Money.FromRupees(47_296.73m),
            TotalCgst = Money.FromRupees(4_256.71m),
            TotalSgst = Money.FromRupees(4_256.70m),
        };

    /// <summary>Pulls the one image XObject's raw sample bytes back out of a rendered PDF, so the assertions below are
    /// about what is IN THE FILE rather than about what the writer was asked to draw.</summary>
    private static (int Width, int Height, byte[] Samples) ExtractImage(byte[] pdf)
    {
        var text = AsLatin1(pdf);
        var m = Regex.Match(text,
            @"/Type /XObject /Subtype /Image /Width (\d+) /Height (\d+) /ColorSpace /DeviceGray /BitsPerComponent 1 /Interpolate false /Length (\d+) >>\nstream\n");
        Assert.True(m.Success, "the rendered PDF carries no 1-bit image XObject");
        int w = int.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
        int h = int.Parse(m.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture);
        int len = int.Parse(m.Groups[3].Value, System.Globalization.CultureInfo.InvariantCulture);
        return (w, h, pdf.Skip(m.Index + m.Length).Take(len).ToArray());
    }

    // ================================================================ the fix

    /// <summary>
    /// The exact inverse of the constructed failure: the same document that carried none of this now carries all of
    /// it. Each assertion below names a measurement that read the other way before the primitive existed.
    /// </summary>
    [Fact]
    public void An_e_invoiced_supply_prints_the_signed_qr_and_the_irn()
    {
        var pdf = InvoicePdf.Render(
            Invoice(SignedQr("NONCE-A"), Irn, "112010036777771", "01-04-2024"), new PrintConfig(), new PageConfig());
        var text = AsLatin1(pdf);

        Assert.Contains("/Subtype /Image", text, StringComparison.Ordinal);      // was: absent
        Assert.Contains("/XObject", text, StringComparison.Ordinal);             // was: absent
        Assert.Contains(" Do\n", text, StringComparison.Ordinal);                // was: zero operators
        Assert.Contains(Irn, text, StringComparison.Ordinal);                    // was: absent
        // The PDF escapes literal parentheses inside a string object, so the caption appears as "\(IRP registered\)".
        Assert.Contains(@"e-Invoice \(IRP registered\)", text, StringComparison.Ordinal);
        Assert.Contains("112010036777771", text, StringComparison.Ordinal);
    }

    /// <summary>
    /// <b>ER-5, and the assertion that actually pins it.</b> The bytes in the file must be the QR of the IRP's string
    /// <i>exactly as stored</i>. The expected bitmap is built here from the independently-verified encoder, so any
    /// transformation the print path applied on the way — a trim, a case fold, a de-brand, a re-serialisation — moves
    /// one side of this comparison and not the other. The nonce guarantees the two sides are not the same constant.
    ///
    /// <para>The signature over the payload is the whole point of the artefact: a QR that scans but whose bytes are
    /// not the ones the IRP signed verifies as forged, which is worse than a document with no QR at all.</para>
    /// </summary>
    [Fact]
    public void The_printed_symbol_is_the_irp_string_verbatim_not_a_re_derivation()
    {
        var signed = SignedQr("NONCE-VERBATIM-7f3a");
        var pdf = InvoicePdf.Render(Invoice(signed, Irn), new PrintConfig(), new PageConfig());
        var (w, h, samples) = ExtractImage(pdf);

        var expected = PdfBitmap.FromQr(QrCode.Encode(signed, QrErrorCorrection.Low));
        Assert.Equal(expected.PixelWidth, w);
        Assert.Equal(expected.PixelHeight, h);
        Assert.Equal(expected.ToBytes(), samples);

        // …and it is NOT the symbol for any of the manglings a print path might casually apply.
        foreach (var mangled in new[]
                 {
                     signed.ToUpperInvariant(), signed.ToLowerInvariant(), signed.Trim('A'),
                     signed.Replace(".", string.Empty, StringComparison.Ordinal),
                 })
        {
            var other = PdfBitmap.FromQr(QrCode.Encode(mangled, QrErrorCorrection.Low));
            Assert.NotEqual(other.ToBytes(), samples);
        }
    }

    /// <summary>One symbol per document. A different signed string must produce different bytes — the control that
    /// makes the comparison above mean something rather than passing on any two QR-shaped blobs.</summary>
    [Fact]
    public void A_different_signed_string_produces_a_different_symbol()
    {
        var a = ExtractImage(InvoicePdf.Render(Invoice(SignedQr("NONCE-A"), Irn), new PrintConfig(), new PageConfig()));
        var b = ExtractImage(InvoicePdf.Render(Invoice(SignedQr("NONCE-B"), Irn), new PrintConfig(), new PageConfig()));
        Assert.Equal(a.Width, b.Width);
        Assert.NotEqual(a.Samples, b.Samples);
    }

    /// <summary>
    /// The QR is drawn inside the 96 pt box the constant declares, at the right-hand margin of the header band —
    /// above the item table, so no page break and no table rule can cross it.
    /// </summary>
    [Fact]
    public void The_symbol_is_placed_in_a_96_point_box_at_the_right_margin()
    {
        var page = new PageConfig();
        var pdf = InvoicePdf.Render(Invoice(SignedQr("N"), Irn), new PrintConfig(), page);
        var text = AsLatin1(pdf);

        var m = Regex.Match(text, @"q\n(\S+) 0 0 (\S+) (\S+) (\S+) cm\n/Im1 Do\nQ\n");
        Assert.True(m.Success, "no image placement operator in the rendered invoice");
        Assert.Equal("96", m.Groups[1].Value);
        Assert.Equal("96", m.Groups[2].Value);
        double x = double.Parse(m.Groups[3].Value, System.Globalization.CultureInfo.InvariantCulture);
        double right = page.PageWidth - page.MarginRight;
        Assert.Equal(right - 96, x, 3);
        // Its top edge sits below the page's top margin and its bottom edge above the bottom margin.
        double y = double.Parse(m.Groups[4].Value, System.Globalization.CultureInfo.InvariantCulture);
        Assert.True(y + 96 < page.PageHeight - page.MarginTop, "the QR overruns the top margin");
        Assert.True(y > page.MarginBottom, "the QR overruns the bottom margin");
    }

    /// <summary>
    /// <b>The height reservation, asserted as GEOMETRY.</b> The band is 96 pt tall, and the paginator decides how
    /// many item rows fit on page 1 from <c>FirstHeaderHeight</c>. If that measurement does not include the band, the
    /// paginator lets roughly seven more rows onto page 1 than there is room for, and they are drawn below the bottom
    /// margin - through the footer and off the sheet.
    ///
    /// <para><b>This test was rewritten because its first version was a DEAD GUARD.</b> It asserted that all 46 rows
    /// appeared somewhere in the file and that the page count did not fall. Deleting the reservation left both true -
    /// the rows are still emitted, just at coordinates no printer will put on paper - so the mutation passed 10 of 10.
    /// Content assertions cannot see a layout defect; only the coordinates can.</para>
    /// </summary>
    [Fact]
    public void No_item_row_is_pushed_below_the_bottom_margin_by_the_bands_height()
    {
        var page = new PageConfig();
        var items = Enumerable.Range(1, 60).Select(i => new InvoiceItemRow
        {
            Description = "Item " + i.ToString(System.Globalization.CultureInfo.InvariantCulture),
            HsnSac = "847130", QuantityText = "1", RateText = "100.00", TaxableValue = Money.FromRupees(100m),
        }).ToArray();

        var pdf = InvoicePdf.Render(NewItems(Invoice(SignedQr("N"), Irn), items), new PrintConfig(), page);
        var text = AsLatin1(pdf);

        // Every "Item NN" cell that was drawn, with the baseline it was drawn at.
        var drawn = Regex.Matches(text, @"(-?[\d.]+) (-?[\d.]+) Td\s+\(Item (\d+)\) Tj")
            .Select(m => (
                Y: double.Parse(m.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture),
                Sr: int.Parse(m.Groups[3].Value, System.Globalization.CultureInfo.InvariantCulture)))
            .ToList();
        Assert.Equal(60, drawn.Count);                       // all rows present - the old, insufficient assertion
        Assert.True(CountPages(pdf) > 1, "the fixture must paginate for this test to mean anything");

        // …and every one of them is on the sheet, above the footer band.
        double floor = page.MarginBottom;
        var offPage = drawn.Where(d => d.Y < floor).ToList();
        Assert.True(offPage.Count == 0,
            "rows drawn below the bottom margin (" + floor.ToString(System.Globalization.CultureInfo.InvariantCulture)
            + " pt): " + string.Join(", ", offPage.Select(d => "Item " + d.Sr + " at y=" + d.Y)));

        // The QR box and the first item row must not overlap either.
        var box = Regex.Match(text, @"q\s+(\S+) 0 0 (\S+) (\S+) (\S+) cm\s+/Im1 Do\s+Q");
        Assert.True(box.Success);
        double qrBottom = double.Parse(box.Groups[4].Value, System.Globalization.CultureInfo.InvariantCulture);
        Assert.True(drawn.Where(d => d.Sr <= 30).All(d => d.Y < qrBottom),
            "an item row was drawn level with or above the QR box");
    }

    private static InvoicePrintData NewItems(InvoicePrintData d, InvoiceItemRow[] items) => new()
    {
        Seller = d.Seller, Buyer = d.Buyer, InvoiceNumber = d.InvoiceNumber, InvoiceDateText = d.InvoiceDateText,
        PlaceOfSupply = d.PlaceOfSupply, IsInterState = d.IsInterState, Items = items, TaxRows = d.TaxRows,
        TotalTaxable = d.TotalTaxable, TotalCgst = d.TotalCgst, TotalSgst = d.TotalSgst,
        EInvoiceSignedQr = d.EInvoiceSignedQr, EInvoiceIrn = d.EInvoiceIrn,
        EInvoiceAckNo = d.EInvoiceAckNo, EInvoiceAckDateText = d.EInvoiceAckDateText,
    };

    private static int CountPages(byte[] pdf) => Regex.Matches(AsLatin1(pdf), @"/Type /Page /Parent").Count;

    /// <summary>The symbol is drawn once per document, on page 1 — not once per sheet. OURS; see
    /// <c>InvoicePdf.DrawEInvoiceBand</c> for why it differs from the CANCELLED over-print.</summary>
    [Fact]
    public void A_multi_page_invoice_carries_exactly_one_symbol()
    {
        var items = Enumerable.Range(1, 120).Select(i => new InvoiceItemRow
        {
            Description = "Item " + i.ToString(System.Globalization.CultureInfo.InvariantCulture),
            HsnSac = "847130", QuantityText = "1", RateText = "100.00", TaxableValue = Money.FromRupees(100m),
        }).ToArray();

        var pdf = InvoicePdf.Render(NewItems(Invoice(SignedQr("N"), Irn), items), new PrintConfig(), new PageConfig());
        var text = AsLatin1(pdf);
        Assert.True(CountPages(pdf) > 1, "the fixture must actually paginate for this test to mean anything");
        Assert.Single(Regex.Matches(text, @"/Im1 Do"));
        Assert.Single(Regex.Matches(text, @"/Subtype /Image"));
    }

    // ================================================================ ER-13

    /// <summary>
    /// <b>ER-13.</b> A document that is not an e-invoice must render byte-for-byte as it did before this feature.
    /// Compared against the SAME document rendered from a DTO built without the new members at all, so the comparison
    /// is not a constant against itself: if the band reserved height, emitted an empty <c>/XObject</c> entry or
    /// shifted an object number when the fields are blank, the two sides diverge.
    /// </summary>
    [Fact]
    public void An_invoice_with_no_e_invoice_particulars_is_byte_identical()
    {
        var page = new PageConfig();
        var cfg = new PrintConfig();
        var withoutFields = Invoice();                                   // never touches the new members
        var withBlankFields = Invoice(string.Empty, string.Empty, string.Empty, string.Empty);

        var a = InvoicePdf.Render(withoutFields, cfg, page);
        var b = InvoicePdf.Render(withBlankFields, cfg, page);
        Assert.Equal(a, b);
        Assert.DoesNotContain("/XObject", AsLatin1(a), StringComparison.Ordinal);
        Assert.DoesNotContain("e-Invoice", AsLatin1(a), StringComparison.Ordinal);

        // And a whitespace-only value is an ABSENCE, not a blank band: same bytes again.
        var whitespace = Invoice("   ", "\t", " ", " ");
        Assert.Equal(a, InvoicePdf.Render(whitespace, cfg, page));
    }

    /// <summary>An IRN with no signed QR still prints its text — the operator is not left with nothing — but no image
    /// is drawn, because there is no signed artefact to draw.</summary>
    [Fact]
    public void An_irn_without_a_signed_qr_prints_the_text_and_draws_no_image()
    {
        var pdf = InvoicePdf.Render(Invoice(string.Empty, Irn), new PrintConfig(), new PageConfig());
        var text = AsLatin1(pdf);
        Assert.Contains(Irn, text, StringComparison.Ordinal);
        Assert.DoesNotContain("/Subtype /Image", text, StringComparison.Ordinal);
        Assert.DoesNotContain("/XObject", text, StringComparison.Ordinal);
    }

    /// <summary>A bill of supply is never e-invoiced (a composition or wholly-exempt supply is out of Rule 48(4)
    /// altogether), but the renderer must be safe against a caller that sets both rather than silently producing a
    /// bill of supply wearing an IRN. The band follows the DTO; the tax suppressions are unaffected.</summary>
    [Fact]
    public void A_bill_of_supply_keeps_every_tax_suppression_even_if_a_caller_sets_e_invoice_fields()
    {
        var data = Invoice(SignedQr("N"), Irn);
        var bos = new InvoicePrintData
        {
            IsBillOfSupply = true,
            Seller = data.Seller, Buyer = data.Buyer, InvoiceNumber = data.InvoiceNumber,
            InvoiceDateText = data.InvoiceDateText, PlaceOfSupply = data.PlaceOfSupply,
            Items = data.Items, TotalTaxable = data.TotalTaxable,
            EInvoiceSignedQr = data.EInvoiceSignedQr, EInvoiceIrn = data.EInvoiceIrn,
        };
        var text = AsLatin1(InvoicePdf.Render(bos, new PrintConfig(), new PageConfig()));
        Assert.Contains("BILL OF SUPPLY", text, StringComparison.Ordinal);
        Assert.DoesNotContain("TAX INVOICE", text, StringComparison.Ordinal);
        Assert.DoesNotContain("CGST", text, StringComparison.Ordinal);
        Assert.DoesNotContain("GST Breakup", text, StringComparison.Ordinal);
    }

    /// <summary>
    /// A payload too large for any QR symbol is refused LOUDLY rather than quietly dropped. Silence would issue a
    /// covered supply's document without the Rule 46(r) particular, which Rule 48(5) says "shall not be treated as an
    /// invoice" — an operator who is told nothing cannot know that. Unreachable with real IRP data (a signed QR runs
    /// to roughly 800 characters against a 2,953-byte ceiling); this pins the direction of the failure.
    /// </summary>
    [Fact]
    public void A_signed_string_too_large_to_encode_fails_loudly_rather_than_printing_a_document_without_it()
    {
        var huge = new string('x', 3000);
        var ex = Assert.Throws<ArgumentException>(
            () => InvoicePdf.Render(Invoice(huge, Irn), new PrintConfig(), new PageConfig()));
        Assert.Contains("3000", ex.Message, StringComparison.Ordinal);
    }
}
