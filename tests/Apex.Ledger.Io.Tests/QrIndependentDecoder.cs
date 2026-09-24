using System;
using System.Collections.Generic;
using System.Text;

namespace Apex.Ledger.Io.Tests;

/// <summary>The result of reading a symbol back: the payload plus the structural facts the DECODER derived for
/// itself, so a test can assert on them rather than on what the encoder claims.</summary>
internal sealed record DecodedQr(
    string Text, int Version, int Mask, int EccBits, int Blocks, int EcPerBlock, int DataCodewords);

/// <summary>
/// A <b>QR Code decoder written to be independent of <see cref="QrCode"/></b> — the instrument this repository did
/// not have, and the only one that can answer the question compliance actually turns on: <i>does the symbol on the
/// printed page decode</i>?
///
/// <para><b>Why the existing tests could not answer it.</b> Every print assertion in <c>EInvoiceQrPrintTests</c>
/// compares the bytes in the PDF against <c>PdfBitmap.FromQr(QrCode.Encode(...))</c> — our encoder measured against
/// our encoder. That comparison is blind to every defect that transforms BOTH sides equally: a vertical flip in the
/// image XObject's sample order, an inverted <c>/Decode</c> sense, a wrong bit-packing direction, a transposed row
/// stride. Each of those produces a symbol no scanner on earth can read, and each of them passes those tests. The
/// repository's own <c>QrCodeTests</c> remarks record the general form of this trap — an encoder checked by a reader
/// that shared its tables passed 189 cases while the version-8-H block count was wrong — so the lesson was already
/// paid for once here; this file applies it to the <i>PDF pipeline</i> rather than to the module matrix.</para>
///
/// <para><b>How independence is achieved without re-typing the standard's tables.</b> The one table that matters for
/// reading data back is the error-correction block structure, and re-typing it from the standard would risk making
/// the same transcription error twice. So this decoder does not take that table from anywhere: it <b>derives</b> the
/// block split from the symbol. It enumerates every plausible (block count, EC-codewords-per-block) pair, de-
/// interleaves under each, and accepts only the one for which <b>every block's Reed-Solomon syndromes vanish</b> over
/// GF(256). A wrong split scrambles the codewords, so its syndromes are non-zero and it is rejected. The structure
/// is therefore recovered from the bits themselves — exactly what a scanner does not have to do, but the strongest
/// available guarantee that no table was shared with the code under test.</para>
///
/// <para><b>Determinism.</b> Pure arithmetic: no clock, no RNG, no culture-sensitive formatting.</para>
/// </summary>
internal static class QrIndependentDecoder
{
    // ---------------------------------------------------------------- GF(256), primitive polynomial 0x11D

    private static readonly byte[] Exp = new byte[512];
    private static readonly byte[] Log = new byte[256];

    static QrIndependentDecoder()
    {
        int x = 1;
        for (int i = 0; i < 255; i++)
        {
            Exp[i] = (byte)x;
            Log[x] = (byte)i;
            x <<= 1;
            if ((x & 0x100) != 0) x ^= 0x11D;
        }
        for (int i = 255; i < 512; i++) Exp[i] = Exp[i - 255];
    }

    private static byte Mul(byte a, byte b) => a == 0 || b == 0 ? (byte)0 : Exp[Log[a] + Log[b]];

    // ---------------------------------------------------------------- entry point

    /// <summary>
    /// Reads a symbol back to its payload. <paramref name="rows"/> is the module grid <b>without</b> the quiet zone,
    /// indexed <c>rows[y][x]</c>, <c>true</c> = dark.
    /// </summary>
    /// <exception cref="FormatException">The grid is not a readable symbol — wrong size, corrupt format information,
    /// or no block structure whose Reed-Solomon syndromes vanish. This is the exception an orientation or packing
    /// defect produces, and a test that expects a successful decode fails on it.</exception>
    internal static DecodedQr Decode(bool[][] rows)
    {
        ArgumentNullException.ThrowIfNull(rows);
        int n = rows.Length;
        if (n < 21 || n > 177 || (n - 17) % 4 != 0)
            throw new FormatException($"a QR symbol is 17+4v modules square for v in 1..40; this grid is {n}.");
        foreach (var r in rows)
            if (r.Length != n) throw new FormatException("the module grid is not square.");

        int version = (n - 17) / 4;
        var (mask, eccBits) = ReadFormat(rows, n);
        var function = BuildFunctionMap(version, n);
        var codewords = ReadCodewords(rows, function, n, mask);
        var (blocks, ecPerBlock, data) = RecoverBlocks(codewords);
        string text = ReadPayload(data, version);
        return new DecodedQr(text, version, mask, eccBits, blocks, ecPerBlock, data.Count);
    }

    // ---------------------------------------------------------------- format information

    /// <summary>The 15 positions of the first format-information copy, bit 0 first (bit 14 is the most significant
    /// of the BCH code word).</summary>
    private static readonly (int X, int Y)[] FormatCopy1 =
    {
        (8, 0), (8, 1), (8, 2), (8, 3), (8, 4), (8, 5), (8, 7), (8, 8),
        (7, 8), (5, 8), (4, 8), (3, 8), (2, 8), (1, 8), (0, 8),
    };

    /// <summary>
    /// Recovers the mask and error-correction level, and <b>verifies the BCH(15,5) code</b> rather than trusting the
    /// 15 bits it read. Both copies are read and required to agree, so a symbol whose format area was damaged — or
    /// whose modules were written in the wrong orientation — is rejected here instead of silently decoding to noise.
    /// </summary>
    private static (int Mask, int EccBits) ReadFormat(bool[][] rows, int n)
    {
        int copy1 = 0;
        for (int i = 0; i < 15; i++)
            if (rows[FormatCopy1[i].Y][FormatCopy1[i].X]) copy1 |= 1 << i;

        int copy2 = 0;
        for (int i = 0; i < 8; i++) if (rows[8][n - 1 - i]) copy2 |= 1 << i;
        for (int i = 8; i < 15; i++) if (rows[n - 15 + i][8]) copy2 |= 1 << i;

        if (copy1 != copy2)
            throw new FormatException(
                $"the two format-information copies disagree (0x{copy1:X4} vs 0x{copy2:X4}); the symbol is not "
                + "readable as written.");

        int bits = copy1 ^ 0x5412;
        int data5 = (bits >> 10) & 0x1F;
        if (Reencode(data5) != bits)
            throw new FormatException(
                $"the format information fails its BCH(15,5) check (read 0x{bits:X4}); the symbol is corrupt.");

        // The five data bits sit at the TOP of the 15-bit code word (bits 10..14): the error-correction indicator in
        // bits 13..14 and the mask in bits 10..12. The low ten bits are BCH parity and carry no meaning — reading the
        // mask from them yields a plausible-looking 0..7 that is right one time in eight, which is exactly the kind
        // of near-miss that makes a decoder look like it works.
        return ((data5 & 7), (data5 >> 3) & 3);
    }

    /// <summary>Re-computes the 10 BCH parity bits over the 5 data bits, so the read code word can be checked.</summary>
    private static int Reencode(int data5)
    {
        int rem = data5;
        for (int i = 0; i < 10; i++) rem = (rem << 1) ^ ((rem >> 9) != 0 ? 0x537 : 0);
        return (data5 << 10) | (rem & 0x3FF);
    }

    // ---------------------------------------------------------------- function-module map

    /// <summary>
    /// Marks every module that carries a function pattern rather than data: the three finders with their separators,
    /// both timing lines, the alignment patterns, the two format-information copies with the always-dark module, and
    /// (from version 7) the two version-information blocks.
    /// </summary>
    private static bool[][] BuildFunctionMap(int version, int n)
    {
        var f = new bool[n][];
        for (int y = 0; y < n; y++) f[y] = new bool[n];

        void Fill(int x0, int y0, int w, int h)
        {
            for (int y = y0; y < y0 + h; y++)
                for (int x = x0; x < x0 + w; x++)
                    if (x >= 0 && y >= 0 && x < n && y < n) f[y][x] = true;
        }

        // Finder patterns plus their separators, as 8x8 corner blocks.
        Fill(0, 0, 8, 8);
        Fill(n - 8, 0, 8, 8);
        Fill(0, n - 8, 8, 8);

        // Timing lines.
        for (int i = 0; i < n; i++) { f[6][i] = true; f[i][6] = true; }

        // Alignment patterns.
        var centres = AlignmentCentres(version, n);
        for (int i = 0; i < centres.Count; i++)
            for (int j = 0; j < centres.Count; j++)
            {
                bool overlapsFinder = (i == 0 && j == 0)
                    || (i == 0 && j == centres.Count - 1)
                    || (i == centres.Count - 1 && j == 0);
                if (overlapsFinder) continue;
                Fill(centres[j] - 2, centres[i] - 2, 5, 5);
            }

        // Format information (both copies) and the dark module.
        foreach (var (x, y) in FormatCopy1) f[y][x] = true;
        for (int i = 0; i < 8; i++) f[8][n - 1 - i] = true;
        for (int i = 8; i < 15; i++) f[n - 15 + i][8] = true;
        f[n - 8][8] = true;

        // Version information, from version 7.
        if (version >= 7)
        {
            Fill(0, n - 11, 6, 3);
            Fill(n - 11, 0, 3, 6);
        }

        return f;
    }

    /// <summary>
    /// The alignment-pattern centre coordinates, <b>derived</b> rather than tabulated: first centre at 6, last at
    /// <c>n-7</c>, and the rest evenly spaced by an even step. This reproduces ISO/IEC 18004 Annex E without copying
    /// it, which is the point — a tabulation shared with the encoder could hide a shared mistake.
    /// </summary>
    private static List<int> AlignmentCentres(int version, int n)
    {
        var result = new List<int>();
        if (version == 1) return result;
        int count = version / 7 + 2;
        int step = version == 32 ? 26 : (version * 4 + count * 2 + 1) / (count * 2 - 2) * 2;
        result.Add(6);
        for (int p = n - 7; result.Count < count; p -= step) result.Insert(1, p);
        return result;
    }

    // ---------------------------------------------------------------- codeword reading

    /// <summary>
    /// Walks the data modules in the standard two-column zigzag from the bottom-right corner, skipping the vertical
    /// timing column, un-masking each module as it goes and packing the bits most-significant first.
    /// </summary>
    private static List<byte> ReadCodewords(bool[][] rows, bool[][] function, int n, int mask)
    {
        var result = new List<byte>();
        int cur = 0, filled = 0;
        bool upward = true;

        for (int right = n - 1; right >= 1; right -= 2)
        {
            if (right == 6) right = 5;              // the vertical timing line is not a data column
            for (int v = 0; v < n; v++)
            {
                int y = upward ? n - 1 - v : v;
                for (int c = 0; c < 2; c++)
                {
                    int x = right - c;
                    if (function[y][x]) continue;
                    bool dark = rows[y][x] ^ MaskAt(mask, y, x);
                    cur = (cur << 1) | (dark ? 1 : 0);
                    if (++filled == 8) { result.Add((byte)cur); cur = 0; filled = 0; }
                }
            }
            upward = !upward;
        }
        return result;
    }

    /// <summary>The eight data-mask conditions of ISO/IEC 18004 §8.8.1; true means the module is inverted.</summary>
    private static bool MaskAt(int m, int y, int x) => m switch
    {
        0 => (y + x) % 2 == 0,
        1 => y % 2 == 0,
        2 => x % 3 == 0,
        3 => (y + x) % 3 == 0,
        4 => (y / 2 + x / 3) % 2 == 0,
        5 => y * x % 2 + y * x % 3 == 0,
        6 => (y * x % 2 + y * x % 3) % 2 == 0,
        7 => ((y + x) % 2 + y * x % 3) % 2 == 0,
        _ => throw new FormatException($"mask {m} is not one of the eight defined patterns."),
    };

    // ---------------------------------------------------------------- block structure, derived

    /// <summary>
    /// Finds the block structure by search rather than by table (see the class remarks). Every candidate split is
    /// tested by de-interleaving and checking Reed-Solomon syndromes; only a split under which <b>all</b> blocks are
    /// valid code words is accepted.
    /// </summary>
    private static (int Blocks, int EcPerBlock, List<byte> Data) RecoverBlocks(List<byte> codewords)
    {
        int total = codewords.Count;
        (int Blocks, int Ec, List<byte> Data)? accepted = null;

        for (int ec = 7; ec <= 30; ec++)
            for (int blocks = 1; blocks <= 81; blocks++)
            {
                int ecTotal = blocks * ec;
                int dataTotal = total - ecTotal;
                if (dataTotal < blocks) break;                 // more blocks cannot fit; try the next EC size

                var data = TrySplit(codewords, blocks, ec, dataTotal);
                if (data is null) continue;

                // Reed-Solomon syndromes NEST: a code word whose syndromes vanish at alpha^0..alpha^(e-1) also has
                // them vanish at every shorter prefix of roots, so under-stating the EC size always "validates" too.
                // The true structure is therefore the one that verifies the MOST redundancy — every extra vanishing
                // syndrome is a 1-in-256 coincidence for a wrong split, so the maximum is not a close call.
                if (accepted is null || blocks * ec > accepted.Value.Blocks * accepted.Value.Ec)
                    accepted = (blocks, ec, data);
            }

        if (accepted is null)
            throw new FormatException(
                $"no block structure over {total} codewords produces zero Reed-Solomon syndromes — the modules do not "
                + "form a readable symbol. A flipped, mirrored or mis-packed image reaches this exact failure.");

        return (accepted.Value.Blocks, accepted.Value.Ec, accepted.Value.Data);
    }

    /// <summary>De-interleaves under one candidate split and returns the data codewords when every block's syndromes
    /// vanish, or <c>null</c> when they do not.</summary>
    private static List<byte>? TrySplit(List<byte> codewords, int blocks, int ec, int dataTotal)
    {
        int shortLen = dataTotal / blocks;
        int longBlocks = dataTotal % blocks;
        if (shortLen == 0) return null;

        var lens = new int[blocks];
        for (int b = 0; b < blocks; b++) lens[b] = shortLen + (b >= blocks - longBlocks ? 1 : 0);

        var dataBlocks = new byte[blocks][];
        var ecBlocks = new byte[blocks][];
        for (int b = 0; b < blocks; b++) { dataBlocks[b] = new byte[lens[b]]; ecBlocks[b] = new byte[ec]; }

        int p = 0;
        int maxLen = shortLen + (longBlocks > 0 ? 1 : 0);
        for (int i = 0; i < maxLen; i++)
            for (int b = 0; b < blocks; b++)
                if (i < lens[b]) dataBlocks[b][i] = codewords[p++];
        for (int i = 0; i < ec; i++)
            for (int b = 0; b < blocks; b++)
                ecBlocks[b][i] = codewords[p++];

        for (int b = 0; b < blocks; b++)
            if (!SyndromesVanish(dataBlocks[b], ecBlocks[b], ec)) return null;

        var data = new List<byte>(dataTotal);
        for (int b = 0; b < blocks; b++) data.AddRange(dataBlocks[b]);
        return data;
    }

    /// <summary>Evaluates the block's code word polynomial at alpha^0 … alpha^(ec-1). A Reed-Solomon code word is
    /// exactly one for which all of these are zero.</summary>
    private static bool SyndromesVanish(byte[] data, byte[] ec, int ecLen)
    {
        for (int i = 0; i < ecLen; i++)
        {
            byte s = 0;
            byte alpha = Exp[i];
            foreach (byte c in data) s = (byte)(Mul(s, alpha) ^ c);
            foreach (byte c in ec) s = (byte)(Mul(s, alpha) ^ c);
            if (s != 0) return false;
        }
        return true;
    }

    // ---------------------------------------------------------------- payload

    /// <summary>
    /// Reads the byte-mode segment the product emits. Any other mode is refused loudly rather than guessed at: this
    /// decoder exists to verify one specific artefact, and a symbol in a mode we never emit is a finding, not an
    /// input to accommodate.
    /// </summary>
    private static string ReadPayload(List<byte> data, int version)
    {
        int pos = 0;
        int Read(int count)
        {
            int value = 0;
            for (int i = 0; i < count; i++)
            {
                if (pos >= data.Count * 8) throw new FormatException("the bit stream ended inside a field.");
                value = (value << 1) | ((data[pos / 8] >> (7 - pos % 8)) & 1);
                pos++;
            }
            return value;
        }

        int mode = Read(4);
        if (mode == 0) return string.Empty;                         // terminator: an empty payload
        if (mode != 0b0100)
            throw new FormatException($"mode {mode} decoded, but this product emits byte mode (4) only.");

        int count = Read(version <= 9 ? 8 : 16);
        var bytes = new byte[count];
        for (int i = 0; i < count; i++) bytes[i] = (byte)Read(8);
        return Encoding.UTF8.GetString(bytes);
    }
}
