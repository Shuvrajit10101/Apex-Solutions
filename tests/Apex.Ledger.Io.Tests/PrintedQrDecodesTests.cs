using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Apex.Ledger;
using Apex.Ledger.Io;

namespace Apex.Ledger.Io.Tests;

/// <summary>
/// <b>Census 6.14 / T0-9 — the compliance assertion: the QR on the printed invoice DECODES.</b>
///
/// <para><b>Why this file exists alongside <see cref="EInvoiceQrPrintTests"/>.</b> Those tests establish that an
/// image reaches the page and that its bytes are the encoding of the IRP's string <i>verbatim</i>. They do it by
/// comparing the PDF's samples against <c>PdfBitmap.FromQr(QrCode.Encode(...))</c> — our encoder checked against our
/// encoder. That comparison cannot fail for any defect that moves both sides together, and the defects that matter
/// most here are exactly of that kind: emit the sample rows bottom-up instead of top-down, pack each byte least-
/// significant-bit first, swap the sense of <c>/DeviceGray</c>, or transpose the row stride, and every existing
/// assertion still passes while the printed symbol becomes unscannable. CGST Rule 46(r) is not satisfied by an image
/// that is QR-shaped; it is satisfied by one a scanner can read.</para>
///
/// <para><b>What these tests do instead.</b> They render the invoice, pull the image XObject out of the emitted PDF
/// as a reader would, rebuild the module grid from the sample bits, and hand it to
/// <see cref="QrIndependentDecoder"/> — which shares no tables with <see cref="QrCode"/> and recovers the error-
/// correction block structure from the Reed-Solomon syndromes rather than from any table at all. The payload has to
/// come back byte-for-byte, and the IRN with it.</para>
///
/// <para><b>Statutory basis.</b> CGST Rule 46 clause (r) makes the Quick Response code with the embedded Invoice
/// Reference Number a mandatory particular of an invoice issued under Rule 48(4), and Rule 48(5) provides that such
/// an invoice issued in any other manner is not to be treated as an invoice. <b>Citation discipline (R7):</b> this
/// clause is stated here by rule NUMBER, not by a URL, because the two official sources for the rule text
/// (<c>taxinformation.cbic.gov.in</c> and the <c>cbic-gst.gov.in</c> Rules PDF) could NOT be opened when this file
/// was written — both reset the connection — and an unopened link is not a citation. The pre-existing statement of
/// the same clause, with the URL that was current when it was written, is in <see cref="EInvoiceQrPrintTests"/>;
/// it was not re-verified here and should not be assumed live.</para>
///
/// <para><b>Sources that WERE opened and returned the content relied on:</b> the official e-invoice portal's FAQ
/// index at <c>https://einvoice1.gst.gov.in/Others/FAQs</c>, which carries a "Signed Invoice &amp; QR Code"
/// section — establishing that the signed QR is an artefact the IRP returns rather than one the billing system
/// derives; and the vendor help root at <c>https://help.tallysolutions.com/</c>, which resolves and covers the GST
/// and e-invoice topics (Ruling 14's fidelity ground truth). The vendor's deeper e-invoice topic path <b>404s</b>
/// and is deliberately not cited.</para>
/// </summary>
public sealed class PrintedQrDecodesTests
{
    private const string Irn = "a5c12bbe4c1f0b1b1cfa4e0c2b4a63c9d8e7f60a1b2c3d4e5f60718293a4b5c6";

    private static string B64Url(string s) => Convert.ToBase64String(Encoding.UTF8.GetBytes(s))
        .TrimEnd('=').Replace('+', '-').Replace('/', '_');

    /// <summary>
    /// A signed QR shaped like the artefact the IRP actually returns: a JWS whose payload JSON carries the IRN
    /// alongside the invoice particulars, and an RS256 signature (256 bytes, 342 base64url characters). The IRN is
    /// <b>inside the payload</b>, which is what makes "the IRN round-trips through the printed page" a meaningful
    /// assertion rather than a restatement of the caption printed next to the symbol.
    /// </summary>
    private static string SignedQr(string nonce)
    {
        string header = B64Url("{\"alg\":\"RS256\",\"typ\":\"JWT\"}");
        string payload = B64Url(
            "{\"data\":\"{\\\"SellerGstin\\\":\\\"27AAPFU0939F1ZV\\\",\\\"BuyerGstin\\\":\\\"29AAGCM5385K1ZW\\\","
            + "\\\"DocNo\\\":\\\"1\\\",\\\"DocTyp\\\":\\\"INV\\\",\\\"DocDt\\\":\\\"01/04/2024\\\","
            + "\\\"TotInvVal\\\":55810.14,\\\"ItemCnt\\\":1,\\\"MainHsnCode\\\":\\\"847130\\\","
            + "\\\"Irn\\\":\\\"" + Irn + "\\\",\\\"IrnDt\\\":\\\"2024-04-01 12:30:00\\\","
            + "\\\"Nonce\\\":\\\"" + nonce + "\\\"}\"}");
        return header + "." + payload + "." + new string('A', 342);
    }

    private static InvoicePrintData Invoice(string signedQr, string irn) => new()
    {
        Seller = new InvoicePartyBlock
        {
            Name = "Acme Traders Pvt Ltd", Gstin = "27AAPFU0939F1ZV", StateText = "Maharashtra (27)",
        },
        Buyer = new InvoicePartyBlock
        {
            Name = "Local Customer", Gstin = "29AAGCM5385K1ZW", StateText = "Karnataka (29)",
        },
        InvoiceNumber = "1",
        InvoiceDateText = "01-04-2024",
        PlaceOfSupply = "Karnataka (29)",
        IsInterState = true,
        EInvoiceSignedQr = signedQr,
        EInvoiceIrn = irn,
        EInvoiceAckNo = "112010036777771",
        EInvoiceAckDateText = "01-04-2024",
        Items = new[]
        {
            new InvoiceItemRow
            {
                Description = "Widget", HsnSac = "847130", QuantityText = "60.125 Nos",
                RateText = "800.00", TaxableValue = Money.FromRupees(48100m),
            },
        },
        TotalTaxable = Money.FromRupees(48100m),
    };

    // ================================================================ the compliance assertion

    /// <summary>
    /// <b>The test the row turns on.</b> Render an e-invoiced supply, read the symbol off the page with an
    /// independent decoder, and require the IRP's signed string back byte-for-byte — with the IRN inside it.
    /// </summary>
    [Fact]
    public void The_printed_symbol_decodes_back_to_the_irp_signed_string()
    {
        string signed = SignedQr("NONCE-DECODE-9c41");
        var pdf = InvoicePdf.Render(Invoice(signed, Irn), new PrintConfig(), new PageConfig());

        var decoded = QrIndependentDecoder.Decode(SymbolFromPdf(pdf));

        Assert.Equal(signed, decoded.Text);

        // …and the IRN genuinely travelled inside the symbol. It is not a substring of the JWS, which is base64url;
        // recovering it means decoding the payload segment the way a verifier would, which is the round trip Rule
        // 46(r) is actually about — "embedded Invoice Reference Number (IRN) in it".
        Assert.Contains(Irn, JwsPayload(decoded.Text), StringComparison.Ordinal);
    }

    /// <summary>Base64url-decodes the payload segment of a JWS, as anyone scanning the symbol would.</summary>
    private static string JwsPayload(string jws)
    {
        var parts = jws.Split('.');
        Assert.Equal(3, parts.Length);
        string b64 = parts[1].Replace('-', '+').Replace('_', '/');
        return Encoding.UTF8.GetString(Convert.FromBase64String(b64.PadRight((b64.Length + 3) / 4 * 4, '=')));
    }

    /// <summary>
    /// The symbol is encoded at error-correction level <b>L</b>, read back out of the symbol's own format
    /// information (L is <c>01</c> in the two-bit indicator) rather than from the constant the renderer used. A
    /// symbol whose format bits say one level while its blocks are built for another decodes on no scanner.
    /// </summary>
    [Fact]
    public void The_symbols_own_format_information_states_level_L()
    {
        var pdf = InvoicePdf.Render(Invoice(SignedQr("N"), Irn), new PrintConfig(), new PageConfig());
        var decoded = QrIndependentDecoder.Decode(SymbolFromPdf(pdf));

        Assert.Equal(0b01, decoded.EccBits);
        Assert.InRange(decoded.Mask, 0, 7);
        // The payload is ~470 characters, so the symbol is well past the versions that need only one block: the
        // decode above therefore exercised de-interleaving across several blocks, not a degenerate single-block case.
        Assert.True(decoded.Blocks >= 2,
            $"expected a multi-block symbol for a realistic signed QR; got {decoded.Blocks} block(s).");
    }

    /// <summary>
    /// Payloads across a range of lengths all survive the whole pipeline. Each length lands on a different symbol
    /// version and block structure, so this walks several distinct interleaving layouts through the PDF and back.
    /// </summary>
    [Theory]
    [InlineData(1)]
    [InlineData(40)]
    [InlineData(120)]
    [InlineData(300)]
    [InlineData(700)]
    public void A_payload_of_any_realistic_length_survives_the_print_path(int length)
    {
        // A base64url-ish alphabet, which is what a JWS actually contains.
        var sb = new StringBuilder(length);
        const string Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-_.";
        for (int i = 0; i < length; i++) sb.Append(Alphabet[i * 7 % Alphabet.Length]);
        string payload = sb.ToString();

        var pdf = InvoicePdf.Render(Invoice(payload, Irn), new PrintConfig(), new PageConfig());
        var decoded = QrIndependentDecoder.Decode(SymbolFromPdf(pdf));

        Assert.Equal(payload, decoded.Text);
    }

    /// <summary>
    /// <b>ISO/IEC 18004 §9.1 — the quiet zone, measured on the emitted image.</b> Four light modules on every side.
    /// This is asserted against the picture rather than against <c>PdfBitmap</c>'s argument check, because the
    /// margin only protects the symbol if it survives into the file.
    /// </summary>
    [Fact]
    public void The_emitted_image_carries_a_four_module_quiet_zone_on_every_side()
    {
        var pdf = InvoicePdf.Render(Invoice(SignedQr("N"), Irn), new PrintConfig(), new PageConfig());
        var (width, height, dark) = ImageFromPdf(pdf);
        var (minX, minY, maxX, maxY) = DarkBounds(width, height, dark);

        Assert.True(minX >= 4, $"left quiet zone is {minX} modules; ISO/IEC 18004 requires 4.");
        Assert.True(minY >= 4, $"top quiet zone is {minY} modules; ISO/IEC 18004 requires 4.");
        Assert.True(width - 1 - maxX >= 4, $"right quiet zone is {width - 1 - maxX} modules; 4 required.");
        Assert.True(height - 1 - maxY >= 4, $"bottom quiet zone is {height - 1 - maxY} modules; 4 required.");
    }

    // ================================================================ the teeth

    /// <summary>
    /// <b>The mutation that proves these tests can fail.</b> PDF maps an image XObject's FIRST sample row to the TOP
    /// of the unit square, while every other coordinate in the format runs bottom-up; emitting the rows in page
    /// order instead of image order is the single most natural mistake in this primitive, and it is invisible to a
    /// comparison against our own bitmap. Flipping the rows here stands in for that defect: the decode must fail.
    /// </summary>
    [Fact]
    public void A_vertically_flipped_image_does_not_decode()
    {
        var pdf = InvoicePdf.Render(Invoice(SignedQr("N"), Irn), new PrintConfig(), new PageConfig());
        var grid = SymbolFromPdf(pdf);
        Array.Reverse(grid);

        Assert.ThrowsAny<FormatException>(() => QrIndependentDecoder.Decode(grid));
    }

    /// <summary>The mirrored case — a transposed row stride, or a byte packed least-significant-bit first, reverses
    /// each row. It must not decode either.</summary>
    [Fact]
    public void A_horizontally_mirrored_image_does_not_decode()
    {
        var pdf = InvoicePdf.Render(Invoice(SignedQr("N"), Irn), new PrintConfig(), new PageConfig());
        var grid = SymbolFromPdf(pdf);
        foreach (var row in grid) Array.Reverse(row);

        Assert.ThrowsAny<FormatException>(() => QrIndependentDecoder.Decode(grid));
    }

    /// <summary>
    /// The inverted case — the sense of <c>/DeviceGray</c> at one bit per component, or a <c>/Decode [1 0]</c> that
    /// crept in. Dark and light swapped is a symbol no scanner reads, and it must not decode here.
    /// </summary>
    [Fact]
    public void A_tone_inverted_image_does_not_decode()
    {
        var pdf = InvoicePdf.Render(Invoice(SignedQr("N"), Irn), new PrintConfig(), new PageConfig());
        var grid = SymbolFromPdf(pdf);
        for (int y = 0; y < grid.Length; y++)
            for (int x = 0; x < grid[y].Length; x++)
                grid[y][x] = !grid[y][x];

        Assert.ThrowsAny<FormatException>(() => QrIndependentDecoder.Decode(grid));
    }

    /// <summary>
    /// <b>The sensitivity control.</b> This decoder VERIFIES rather than CORRECTS: it accepts only a block whose
    /// Reed-Solomon syndromes vanish, so a single flipped module is refused. That is deliberate and it is what makes
    /// the three mutation tests above mean something — an instrument that shrugged off arbitrary perturbation could
    /// not distinguish "flipped image" from "fine". It also bounds the claim honestly: these tests assert that the
    /// emitted symbol is EXACTLY right, which is stronger than "a real scanner could still recover it".
    /// </summary>
    [Fact]
    public void A_single_flipped_module_is_detected_by_the_syndrome_check()
    {
        string signed = SignedQr("NONCE-ECC");
        var pdf = InvoicePdf.Render(Invoice(signed, Irn), new PrintConfig(), new PageConfig());
        var grid = SymbolFromPdf(pdf);

        // A module well inside the data region, clear of every function pattern.
        int y = grid.Length - 3, x = grid.Length - 3;
        grid[y][x] = !grid[y][x];

        // The syndromes no longer vanish, so this decoder — which verifies rather than corrects — refuses it. That is
        // the point being pinned: the instrument reacts to a ONE-MODULE change, so its silence on the unmutated
        // symbol is evidence and not insensitivity.
        Assert.ThrowsAny<FormatException>(() => QrIndependentDecoder.Decode(grid));
    }

    // ================================================================ reading the PDF as a consumer would

    /// <summary>Pulls the symbol's module grid out of a rendered PDF: locate the image XObject, unpack its samples,
    /// then trim the quiet zone down to the symbol itself.</summary>
    private static bool[][] SymbolFromPdf(byte[] pdf)
    {
        var (width, height, dark) = ImageFromPdf(pdf);
        var (minX, minY, maxX, maxY) = DarkBounds(width, height, dark);

        int side = maxX - minX + 1;
        Assert.Equal(side, maxY - minY + 1);

        var grid = new bool[side][];
        for (int y = 0; y < side; y++)
        {
            grid[y] = new bool[side];
            for (int x = 0; x < side; x++) grid[y][x] = dark[(minY + y) * width + minX + x];
        }
        return grid;
    }

    /// <summary>
    /// Parses the first image XObject in the file: its <c>/Width</c>, <c>/Height</c> and <c>/Length</c>, then the
    /// raw sample bytes. The unpacking follows the PDF semantics the dictionary declares — one bit per sample,
    /// <c>/DeviceGray</c>, rows top-to-bottom, each row padded to a byte, most-significant bit first, <c>0</c> dark —
    /// and NOT anything <see cref="PdfBitmap"/> exposes, so a change of convention inside the writer shows up here.
    /// </summary>
    private static (int Width, int Height, bool[] Dark) ImageFromPdf(byte[] pdf)
    {
        string text = Encoding.Latin1.GetString(pdf);
        int at = text.IndexOf("/Subtype /Image", StringComparison.Ordinal);
        Assert.True(at >= 0, "the rendered PDF contains no image XObject.");

        int dictEnd = text.IndexOf(">>", at, StringComparison.Ordinal);
        Assert.True(dictEnd > at, "the image XObject dictionary is unterminated.");
        string dict = text[at..dictEnd];

        Assert.Contains("/ColorSpace /DeviceGray", dict, StringComparison.Ordinal);
        Assert.Contains("/BitsPerComponent 1", dict, StringComparison.Ordinal);

        int width = DictInt(dict, "Width");
        int height = DictInt(dict, "Height");
        int length = DictInt(dict, "Length");

        int streamAt = text.IndexOf("stream\n", dictEnd, StringComparison.Ordinal) + "stream\n".Length;
        Assert.True(streamAt >= "stream\n".Length, "the image XObject has no stream.");

        int stride = (width + 7) / 8;
        Assert.Equal(stride * height, length);

        var dark = new bool[width * height];
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                byte b = pdf[streamAt + y * stride + x / 8];
                int bit = (b >> (7 - x % 8)) & 1;
                dark[y * width + x] = bit == 0;              // /DeviceGray: 0 is black
            }
        return (width, height, dark);
    }

    private static int DictInt(string dict, string key)
    {
        var m = Regex.Match(dict, @"/" + key + @"\s+(\d+)");
        Assert.True(m.Success, $"the image XObject dictionary has no /{key}.");
        return int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
    }

    private static (int MinX, int MinY, int MaxX, int MaxY) DarkBounds(int width, int height, bool[] dark)
    {
        int minX = width, minY = height, maxX = -1, maxY = -1;
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
                if (dark[y * width + x])
                {
                    if (x < minX) minX = x;
                    if (y < minY) minY = y;
                    if (x > maxX) maxX = x;
                    if (y > maxY) maxY = y;
                }
        Assert.True(maxX >= 0, "the emitted image has no dark modules at all.");
        return (minX, minY, maxX, maxY);
    }
}
