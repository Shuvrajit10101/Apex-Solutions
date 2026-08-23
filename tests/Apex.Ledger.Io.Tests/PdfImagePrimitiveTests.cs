using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Apex.Ledger.Io;

namespace Apex.Ledger.Io.Tests;

/// <summary>
/// <b>The image primitive (census T0-9).</b> Before it existed <see cref="PdfWriter"/> exposed
/// <c>BeginPage</c> / <c>Text</c> / <c>Line</c> / <c>Build</c> and nothing else, so no raster mark could reach any
/// document this product prints — which is why an e-invoiced supply's PDF could not carry its QR code.
///
/// <para><b>ER-13 is the load-bearing test in this file</b> (<see cref="A_document_that_draws_no_image_is_byte_identical_to_the_pre_primitive_writer"/>),
/// and it is built to be able to fail. The obvious way to write it — render twice with today's code and compare — is
/// self-consistent: it compares a constant with itself and no defect can redden it. Instead the expected bytes are
/// reconstructed here from the writer's documented serialisation, so a change to the object numbering, the resource
/// dictionary or the xref offsets moves one side and not the other.</para>
/// </summary>
public sealed class PdfImagePrimitiveTests
{
    private static string AsLatin1(byte[] b) => Encoding.Latin1.GetString(b);

    private static int Count(string haystack, string needle)
    {
        int n = 0, i = 0;
        while ((i = haystack.IndexOf(needle, i, StringComparison.Ordinal)) >= 0) { n++; i += needle.Length; }
        return n;
    }

    // ================================================================ the bitmap

    [Fact]
    public void A_bitmap_packs_one_bit_per_pixel_msb_first_with_rows_padded_to_a_byte()
    {
        // 10 px wide => 2 bytes per row, with 6 unused low bits in the second byte.
        var bmp = PdfBitmap.FromPredicate(10, 3, (x, y) => y == 1 && x < 4);
        Assert.Equal(10, bmp.PixelWidth);
        Assert.Equal(3, bmp.PixelHeight);
        Assert.Equal(2, bmp.BytesPerRow);

        var bytes = bmp.ToBytes();
        Assert.Equal(6, bytes.Length);
        // Row 0: all light => every SAMPLE bit is 1. The two padding bits stay 0 (documented: fixed, not incidental).
        Assert.Equal(0xFF, bytes[0]);
        Assert.Equal(0b1100_0000, bytes[1]);
        // Row 1: x 0..3 dark (0), x 4..9 light (1).
        Assert.Equal(0b0000_1111, bytes[2]);
        Assert.Equal(0b1100_0000, bytes[3]);
        // Row 2: light again.
        Assert.Equal(0xFF, bytes[4]);
    }

    [Fact]
    public void A_bitmap_cannot_be_mutated_after_it_has_been_handed_out()
    {
        var bmp = PdfBitmap.FromPredicate(8, 1, (_, _) => false);
        var first = bmp.ToBytes();
        first[0] = 0x00;
        Assert.Equal(0xFF, bmp.ToBytes()[0]);
    }

    /// <summary>
    /// ISO/IEC 18004 §9.1 requires four light modules on every side. It is the commonest cause of a symbol that looks
    /// perfect and will not scan, so a caller cannot ask for less.
    /// </summary>
    [Fact]
    public void A_quiet_zone_below_the_standards_minimum_is_refused_rather_than_silently_accepted()
    {
        var qr = QrCode.Encode("A", QrErrorCorrection.Low);
        var ex = Assert.Throws<ArgumentOutOfRangeException>(() => PdfBitmap.FromQr(qr, quietModules: 3));
        Assert.Contains("four modules", ex.Message, StringComparison.Ordinal);

        // …and the default IS four, so the bitmap is eight modules wider than the symbol on each axis.
        var bmp = PdfBitmap.FromQr(qr);
        Assert.Equal(qr.Size + 8, bmp.PixelWidth);
        Assert.Equal(qr.Size + 8, bmp.PixelHeight);
    }

    [Fact]
    public void The_quiet_zone_is_light_and_the_symbols_own_corner_module_is_dark()
    {
        var qr = QrCode.Encode("A", QrErrorCorrection.Low);
        var bmp = PdfBitmap.FromQr(qr);
        var bytes = bmp.ToBytes();

        bool Sample(int x, int y) => (bytes[y * bmp.BytesPerRow + x / 8] & (0x80 >> (x % 8))) == 0;   // true == dark

        // The four quiet-zone rows and columns are entirely light.
        for (int y = 0; y < 4; y++)
            for (int x = 0; x < bmp.PixelWidth; x++)
                Assert.False(Sample(x, y));
        for (int y = 0; y < bmp.PixelHeight; y++)
            for (int x = 0; x < 4; x++)
                Assert.False(Sample(x, y));
        // The finder pattern's outer corner starts immediately inside it.
        Assert.True(Sample(4, 4));
    }

    // ================================================================ the PDF object

    [Fact]
    public void An_image_emits_one_xobject_the_page_resources_name_and_one_Do_operator()
    {
        var w = new PdfWriter { DocumentTitle = "T" };
        w.BeginPage(200, 100);
        w.Image(10, 20, 30, 40, PdfBitmap.FromPredicate(8, 8, (x, y) => x == y));
        var text = AsLatin1(w.Build());

        Assert.Contains("/Type /XObject /Subtype /Image", text, StringComparison.Ordinal);
        Assert.Contains("/Width 8 /Height 8 /ColorSpace /DeviceGray /BitsPerComponent 1 /Interpolate false",
            text, StringComparison.Ordinal);
        // Placement: the CTM maps the unit square onto the requested box, bracketed by q/Q so it cannot leak.
        Assert.Contains("q\n30 0 0 40 10 20 cm\n/Im1 Do\nQ\n", text, StringComparison.Ordinal);
        // The page's resource dictionary names it, and the object number it points at is the one that was emitted.
        Assert.Contains("/Resources << /Font << /F1 3 0 R /F2 4 0 R >> /XObject << /Im1 8 0 R >> >>",
            text, StringComparison.Ordinal);
        Assert.Contains("\n8 0 obj\n", text, StringComparison.Ordinal);
        Assert.Equal(1, Count(text, " Do\n"));
        // /Size and the xref must both count the image object: 5 fixed + 1 page x 2 + 1 image = 8.
        Assert.Contains("/Size 9 /Root 1 0 R /Info 5 0 R", text, StringComparison.Ordinal);
    }

    [Fact]
    public void The_same_bitmap_drawn_twice_emits_one_xobject_and_two_draw_operators()
    {
        var bmp = PdfBitmap.FromPredicate(8, 8, (x, _) => x < 4);
        var w = new PdfWriter();
        w.BeginPage(200, 200);
        w.Image(0, 0, 10, 10, bmp);
        w.Image(50, 50, 10, 10, bmp);
        var text = AsLatin1(w.Build());

        Assert.Equal(1, Count(text, "/Subtype /Image"));
        Assert.Equal(2, Count(text, "/Im1 Do"));
        Assert.Contains("/XObject << /Im1 8 0 R >>", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Two_different_bitmaps_get_their_own_xobjects_and_only_the_pages_that_use_them_name_them()
    {
        var a = PdfBitmap.FromPredicate(8, 8, (x, _) => x < 4);
        var b = PdfBitmap.FromPredicate(8, 8, (_, y) => y < 4);
        var w = new PdfWriter();
        w.BeginPage(200, 200);
        w.Image(0, 0, 10, 10, a);
        w.BeginPage(200, 200);
        w.Image(0, 0, 10, 10, b);
        var text = AsLatin1(w.Build());

        Assert.Equal(2, Count(text, "/Subtype /Image"));
        // 5 fixed + 2 pages x 2 = 9, so the images are objects 10 and 11.
        Assert.Contains("/XObject << /Im1 10 0 R >>", text, StringComparison.Ordinal);
        Assert.Contains("/XObject << /Im2 11 0 R >>", text, StringComparison.Ordinal);
        Assert.DoesNotContain("/Im2 11 0 R >> /XObject", text, StringComparison.Ordinal);
        Assert.Contains("/Size 12 ", text, StringComparison.Ordinal);
    }

    [Fact]
    public void The_stream_length_matches_the_packed_bytes_and_the_bytes_are_in_the_stream()
    {
        var bmp = PdfBitmap.FromPredicate(17, 5, (x, y) => (x + y) % 3 == 0);
        Assert.Equal(3, bmp.BytesPerRow);
        var w = new PdfWriter();
        w.BeginPage(100, 100);
        w.Image(0, 0, 50, 50, bmp);
        var bytes = w.Build();
        var text = AsLatin1(bytes);

        Assert.Contains("/Length 15 >>", text, StringComparison.Ordinal);
        int start = text.IndexOf("/Type /XObject", StringComparison.Ordinal);
        int streamAt = text.IndexOf("stream\n", start, StringComparison.Ordinal) + "stream\n".Length;
        var packed = bmp.ToBytes();
        Assert.Equal(packed, bytes.Skip(streamAt).Take(packed.Length).ToArray());
        Assert.Equal("\nendstream\n", text.Substring(streamAt + packed.Length, "\nendstream\n".Length));
    }

    [Fact]
    public void The_xref_offsets_still_address_the_start_of_every_object_once_binary_image_bytes_are_in_the_file()
    {
        // The image stream is raw binary and may contain bytes that look like PDF syntax; the xref is computed from
        // byte positions, so this is the test that catches an offset computed from a string length instead.
        var w = new PdfWriter { DocumentTitle = "Binary" };
        w.BeginPage(300, 300);
        w.Text(10, 280, "before", 9);
        w.Image(10, 10, 100, 100, PdfBitmap.FromQr(QrCode.Encode("payload", QrErrorCorrection.Medium)));
        var bytes = w.Build();
        var text = AsLatin1(bytes);

        int startxref = text.LastIndexOf("startxref\n", StringComparison.Ordinal) + "startxref\n".Length;
        int eol = text.IndexOf('\n', startxref);
        long xrefOffset = long.Parse(text[startxref..eol], System.Globalization.CultureInfo.InvariantCulture);
        Assert.Equal("xref\n", text.Substring((int)xrefOffset, 5));

        // Walk the table and check each offset lands on "<id> 0 obj".
        int cursor = (int)xrefOffset + "xref\n".Length;
        int space = text.IndexOf(' ', cursor);
        int nl = text.IndexOf('\n', cursor);
        int size = int.Parse(text[(space + 1)..nl], System.Globalization.CultureInfo.InvariantCulture);
        Assert.Equal(9, size);   // 5 fixed + 1 page x 2 + 1 image, + the free entry
        int entry = nl + 1 + 20; // skip the free entry
        for (int id = 1; id <= size - 1; id++, entry += 20)
        {
            long off = long.Parse(text.Substring(entry, 10), System.Globalization.CultureInfo.InvariantCulture);
            Assert.Equal(id.ToString(System.Globalization.CultureInfo.InvariantCulture) + " 0 obj\n",
                text.Substring((int)off, id.ToString(System.Globalization.CultureInfo.InvariantCulture).Length + 7));
        }
    }

    [Fact]
    public void A_zero_or_negative_box_is_refused()
    {
        var w = new PdfWriter();
        w.BeginPage(100, 100);
        var bmp = PdfBitmap.FromPredicate(4, 4, (_, _) => true);
        Assert.Throws<ArgumentOutOfRangeException>(() => w.Image(0, 0, 0, 10, bmp));
        Assert.Throws<ArgumentOutOfRangeException>(() => w.Image(0, 0, 10, -1, bmp));
        Assert.Throws<ArgumentNullException>(() => w.Image(0, 0, 10, 10, null!));
    }

    [Fact]
    public void Drawing_an_image_before_BeginPage_is_refused_like_every_other_primitive()
    {
        var w = new PdfWriter();
        Assert.Throws<InvalidOperationException>(
            () => w.Image(0, 0, 10, 10, PdfBitmap.FromPredicate(4, 4, (_, _) => true)));
    }

    // ================================================================ ER-13

    /// <summary>
    /// <b>ER-13.</b> A document that draws no image must serialise EXACTLY as it did before the primitive existed.
    ///
    /// <para><b>The expected bytes are reconstructed from the documented serialisation, not captured from a run.</b>
    /// A golden file taken from today's output would be a constant compared with itself: it would survive any change
    /// made to both sides at once, which is precisely the class of change that breaks ER-13. Building the expectation
    /// independently means the object numbering (1–5 fixed, pages at 6+2i), the resource dictionary with NO
    /// <c>/XObject</c> entry and no stray space, the <c>/Size</c>, and every xref offset each have to come out right
    /// on their own.</para>
    /// </summary>
    [Fact]
    public void A_document_that_draws_no_image_is_byte_identical_to_the_pre_primitive_writer()
    {
        var w = new PdfWriter { DocumentTitle = "Apex Solutions Report" };
        w.BeginPage(200, 100);
        w.Text(10, 80, "Hello", 9);
        w.Line(10, 70, 190, 70, 0.5);
        var actual = w.Build();

        // ---- rebuild the file from the writer's documented layout ----
        var content = "BT\n/F1 9 Tf\n10 80 Td\n(Hello) Tj\nET\n0.5 w\n10 70 m\n190 70 l\nS\n";
        var objects = new List<string>
        {
            "<< /Type /Catalog /Pages 2 0 R >>\n",
            "<< /Type /Pages /Count 1 /Kids [6 0 R] >>\n",
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>\n",
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica-Bold /Encoding /WinAnsiEncoding >>\n",
            "<< /Producer (Apex Solutions) /Creator (Apex Solutions) /Title (Apex Solutions Report) >>\n",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 200 100] "
                + "/Resources << /Font << /F1 3 0 R /F2 4 0 R >> >> /Contents 7 0 R >>\n",
            "<< /Length " + content.Length.ToString(System.Globalization.CultureInfo.InvariantCulture)
                + " >>\nstream\n" + content + "endstream\n",
        };

        var sb = new StringBuilder();
        sb.Append("%PDF-1.4\nâãÏÓ".Insert(9, "%"));   // header + the fixed binary comment
        sb.Append('\n');
        var offsets = new long[objects.Count + 1];
        for (int i = 0; i < objects.Count; i++)
        {
            offsets[i + 1] = Encoding.Latin1.GetByteCount(sb.ToString());
            sb.Append(i + 1).Append(" 0 obj\n").Append(objects[i]).Append("endobj\n");
        }
        long xref = Encoding.Latin1.GetByteCount(sb.ToString());
        sb.Append("xref\n0 ").Append(objects.Count + 1).Append('\n').Append("0000000000 65535 f \n");
        for (int id = 1; id <= objects.Count; id++)
            sb.Append(offsets[id].ToString("D10", System.Globalization.CultureInfo.InvariantCulture)).Append(" 00000 n \n");
        sb.Append("trailer\n<< /Size ").Append(objects.Count + 1).Append(" /Root 1 0 R /Info 5 0 R >>\n")
          .Append("startxref\n").Append(xref).Append("\n%%EOF\n");

        var expected = Encoding.Latin1.GetBytes(sb.ToString());
        Assert.Equal(expected.Length, actual.Length);
        Assert.Equal(expected, actual);
        // Belt and braces on the one thing that would be easiest to get wrong silently.
        Assert.DoesNotContain("/XObject", AsLatin1(actual), StringComparison.Ordinal);
    }
}
