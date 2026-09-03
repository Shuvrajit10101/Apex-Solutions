namespace Apex.Ledger.Io;

/// <summary>
/// A <b>1-bit-per-pixel monochrome bitmap</b> in the exact shape a PDF image XObject wants it: rows top-to-bottom,
/// each row packed most-significant-bit first and padded to a whole byte, one sample per pixel, <c>0 = black</c> and
/// <c>1 = white</c> (PDF's <c>/DeviceGray</c> convention at <c>/BitsPerComponent 1</c>).
///
/// <para><b>Why this needs no package — the point worth understanding before anyone reaches for one.</b> An external
/// imaging library exists to <i>decode</i> a compressed raster format (PNG, JPEG) into samples. Here there is nothing
/// to decode: the samples are generated, one bit per QR module. PDF 1.4 consumes exactly this — an uncompressed
/// sample stream with no filter — as a core feature of the format, so the whole path from module grid to printed
/// square is arithmetic plus the PDF operators <see cref="PdfWriter"/> already writes. The no-dependency constraint
/// costs a bit-packing loop, not a codec.</para>
///
/// <para><b>Determinism:</b> the packing is pure and total — the same matrix yields the same bytes, always.</para>
/// </summary>
public sealed class PdfBitmap
{
    private readonly byte[] _rows;

    private PdfBitmap(int width, int height, byte[] rows)
    {
        PixelWidth = width;
        PixelHeight = height;
        _rows = rows;
    }

    /// <summary>Width in pixels (samples per row).</summary>
    public int PixelWidth { get; }

    /// <summary>Height in pixels (number of rows).</summary>
    public int PixelHeight { get; }

    /// <summary>Bytes per packed row — <c>ceil(PixelWidth / 8)</c>. PDF requires each row to start on a byte
    /// boundary, so a width that is not a multiple of 8 leaves the low bits of the last byte unused.</summary>
    public int BytesPerRow => (PixelWidth + 7) / 8;

    /// <summary>The packed sample stream, rows top-to-bottom. Returned as the live array to the writer only; callers
    /// outside this assembly get a copy so a bitmap cannot be mutated after it has been drawn.</summary>
    public byte[] ToBytes() => (byte[])_rows.Clone();

    internal byte[] RawBytes => _rows;

    /// <summary>
    /// Renders a QR symbol as a bitmap, surrounded by the <b>quiet zone ISO/IEC 18004 §9.1 requires</b> — four light
    /// modules on every side. The margin is part of the IMAGE rather than something the layout is asked to leave
    /// around it, because a symbol whose quiet zone is a layout responsibility is a symbol that eventually gets a
    /// table rule drawn through it. One pixel per module: the placement box scales it (see
    /// <see cref="PdfWriter.Image"/>), and the modules stay square because the box is square.
    /// </summary>
    /// <param name="qr">The symbol to render.</param>
    /// <param name="quietModules">Light modules of margin on each side; the standard's minimum is 4, and less is
    /// refused rather than silently accepted — a too-small quiet zone is the commonest cause of a symbol that looks
    /// right and does not scan.</param>
    public static PdfBitmap FromQr(QrCodeMatrix qr, int quietModules = 4)
    {
        ArgumentNullException.ThrowIfNull(qr);
        if (quietModules < 4)
            throw new ArgumentOutOfRangeException(nameof(quietModules),
                "ISO/IEC 18004 requires a quiet zone of at least four modules on every side of a QR symbol.");

        int side = qr.Size + quietModules * 2;
        return FromPredicate(side, side, (x, y) => qr.IsDark(x - quietModules, y - quietModules));
    }

    /// <summary>Builds a bitmap from a dark/light predicate over (column, row), origin top-left.</summary>
    public static PdfBitmap FromPredicate(int width, int height, Func<int, int, bool> isDark)
    {
        ArgumentNullException.ThrowIfNull(isDark);
        if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height));

        int stride = (width + 7) / 8;
        var rows = new byte[stride * height];
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                // 1 = white. Start from all-zero (black) and set the light bits, so the padding bits at the end of a
                // row stay 0 — i.e. BLACK. They are outside /Width and are never sampled, but leaving them at a fixed
                // value keeps the bytes deterministic rather than depending on how the loop happens to run.
                if (!isDark(x, y)) rows[y * stride + x / 8] |= (byte)(0x80 >> (x % 8));
            }
        return new PdfBitmap(width, height, rows);
    }
}
