using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using Apex.Ledger.Io;

namespace Apex.Ledger.Io.Tests;

/// <summary>
/// The sweep <see cref="QrCodeTests"/> compares against an independent encoder. Public so the (temporary) oracle
/// dumper and the permanent test walk ONE definition of the cases — a second copy is how a sweep quietly shrinks.
/// </summary>
internal static class QrSweep
{
    internal static readonly QrErrorCorrection[] Levels =
    {
        QrErrorCorrection.Low, QrErrorCorrection.Medium, QrErrorCorrection.Quartile, QrErrorCorrection.High,
    };

    internal static char LevelCode(QrErrorCorrection ecc) => ecc switch
    {
        QrErrorCorrection.Low => 'L',
        QrErrorCorrection.Medium => 'M',
        QrErrorCorrection.Quartile => 'Q',
        _ => 'H',
    };

    /// <summary>Reads the mask index back out of a finished symbol's format information — the same 15 bits a scanner
    /// reads, so this asserts what the SYMBOL says rather than what the encoder remembers.</summary>
    internal static int ReadMask(QrCodeMatrix m)
    {
        // The first format copy: (8,0)…(8,5), (8,7), (8,8), (7,8), then (5,8)…(0,8) — bit i ascending.
        var seq = new (int X, int Y)[]
        {
            (8,0),(8,1),(8,2),(8,3),(8,4),(8,5),(8,7),(8,8),(7,8),(5,8),(4,8),(3,8),(2,8),(1,8),(0,8),
        };
        int bits = 0;
        for (int i = 0; i < seq.Length; i++)
            if (m.IsDark(seq[i].X, seq[i].Y)) bits |= 1 << i;
        bits ^= 0x5412;
        return (bits >> 10) & 7;
    }

    /// <summary>Reads the two-bit error-correction indicator back out of the format information.</summary>
    internal static int ReadEccBits(QrCodeMatrix m)
    {
        var seq = new (int X, int Y)[]
        {
            (8,0),(8,1),(8,2),(8,3),(8,4),(8,5),(8,7),(8,8),(7,8),(5,8),(4,8),(3,8),(2,8),(1,8),(0,8),
        };
        int bits = 0;
        for (int i = 0; i < seq.Length; i++)
            if (m.IsDark(seq[i].X, seq[i].Y)) bits |= 1 << i;
        bits ^= 0x5412;
        return (bits >> 13) & 3;
    }

    internal static string Sha256Hex(string s)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(s));
        var sb = new StringBuilder(hash.Length * 2);
        foreach (byte b in hash) sb.Append(b.ToString("x2", System.Globalization.CultureInfo.InvariantCulture));
        return sb.ToString();
    }

    /// <summary>
    /// The cases, in the order both sides walk them. The length sweep straddles <b>every</b> byte-mode version
    /// boundary at every level (so an off-by-one in a capacity table lands on a boundary case), and crosses the
    /// 9→10 point where the character-count field widens from 8 bits to 16.
    /// </summary>
    internal static (string Name, string Payload)[] Cases()
    {
        var list = new List<(string, string)>
        {
            ("hello", "HELLO WORLD"),
            ("digits8", "01234567"),
            ("one", "A"),
            ("empty", ""),
        };
        foreach (int n in new[]
        {
            1, 2, 7, 8, 9, 14, 16, 17, 32, 34, 60, 62, 63, 100, 101, 128, 154, 195, 224,
            271, 321, 367, 425, 458, 520, 586, 644, 718, 792, 858, 929, 1003, 1091, 1171,
            1273, 1367, 1465, 1528, 1628, 1732, 1840, 1952, 2068, 2188, 2303, 2431, 2563,
            2699, 2809, 2953,
        })
        {
            var sb = new StringBuilder(n);
            // A deterministic NON-REPEATING printable-ASCII pattern. A repeating filler would let a broken
            // interleave or a mis-split block look right, because the wrong bytes would equal the right ones.
            for (int i = 0; i < n; i++) sb.Append((char)(33 + (i * 7 + i / 94) % 94));
            list.Add(("len" + n.ToString(System.Globalization.CultureInfo.InvariantCulture), sb.ToString()));
        }
        // The shape this feature actually has to carry: an IRP-style JWS — base64url characters and two dots.
        var jws = new StringBuilder();
        const string B64 = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-_";
        for (int i = 0; i < 804; i++) jws.Append(i == 36 || i == 460 ? '.' : B64[(i * 13 + 5) % 64]);
        list.Add(("jws804", jws.ToString()));
        return list.ToArray();
    }
}

/// <summary>
/// <b>The QR encoder is checked against an INDEPENDENT encoder, not against itself.</b>
///
/// <para><b>Why that distinction is the whole point of this file.</b> The first version of this encoder was checked by
/// a round-trip — encode, then decode with a reader written alongside it — and all 189 sweep cases passed. They passed
/// while the block-count table said <b>5</b> blocks for version 8 at level H where the standard says <b>6</b>, because
/// the reader read the same wrong table: the instrument was self-consistent, so nothing could redden on it. An
/// independent encoder found that entry in one pass. It is the only defect the oracle found, and it would have
/// produced symbols no real scanner could read at that one version/level.</para>
///
/// <para><b>The oracle.</b> <c>qrcode</c> (the Lincoln Loop pure-Python library), which shares no code, no tables and
/// no author with <see cref="QrCode"/>. <b>A second candidate oracle was rejected on the evidence:</b> <c>segno</c>
/// disagreed on 145 of 189 cases, and a three-way comparison localised the disagreement to the PAD codewords —
/// <c>segno</c> emits an extra <c>0x00</c> where ISO/IEC 18004 §8.4.8 specifies a 4-bit terminator, so for
/// <c>"HELLO WORLD"</c> at 1-L it produces <c>… 40 00 EC 11 EC 11 EC</c> where both this encoder and <c>qrcode</c>
/// produce <c>… 40 EC 11 EC 11 EC 11</c>. Both symbols scan; only one follows the clause. Recording which oracle was
/// rejected, and why, matters more than the count of cases that agreed.</para>
///
/// <para><b>What the aggregate hash covers, and how to regenerate it.</b> <see cref="OracleSweepSha256"/> is the
/// SHA-256 of the <b>oracle's</b> rendering of the whole sweep, not ours. The serialisation is, for every case of
/// <see cref="QrSweep.Cases"/> in order, for every level in L,M,Q,H order that the payload FITS at, for mask 0…7:
/// <c>name\tlevelCode\tmask\tversion\n</c> then one line of '0'/'1' per module row. Regenerate with
/// <c>qrcode.QRCode(version=v, error_correction=…, box_size=1, border=0, mask_pattern=k)</c> over
/// <c>qrcode.util.QRData(payload, mode=MODE_8BIT_BYTE)</c>, taking the version from an independent
/// <c>make(fit=True)</c> — so the version choice is the oracle's too, not ours.</para>
///
/// <para><b>Why the mask is FORCED in that comparison.</b> The one thing two conforming encoders may legitimately
/// differ on is which of the eight masks they prefer: ISO/IEC 18004's rule-3 penalty does not say whether the four
/// light modules may fall in the quiet zone, and implementations split on it. Forcing the mask removes that single
/// degree of freedom so every remaining difference is a defect. Our own mask CHOICE is then pinned separately, as
/// ours, by <see cref="Our_mask_choice_is_stable"/>.</para>
/// </summary>
public sealed class QrCodeTests
{
    /// <summary>
    /// SHA-256 of the independent encoder's rendering of the full sweep (see the class remarks for the exact
    /// serialisation and the regeneration recipe). <b>Generated by the oracle, not by us</b> — if this constant were
    /// produced from our own output it would assert nothing at all.
    /// </summary>
    private const string OracleSweepSha256 = "c44e277966d0867814eb7f3f92e1de4f56c2ef25af85ef41f517e83a3dfef4da";

    [Fact]
    public void Every_symbol_matches_an_independent_encoder_module_for_module()
    {
        var sweep = new StringBuilder();
        int cases = 0;
        foreach (var (name, payload) in QrSweep.Cases())
        {
            var bytes = Encoding.UTF8.GetBytes(payload);
            foreach (var ecc in QrSweep.Levels)
            {
                try { QrCode.Encode(bytes, ecc); }
                catch (ArgumentException) { continue; }
                cases++;
                for (int mask = 0; mask < 8; mask++)
                {
                    var m = QrCode.Encode(bytes, ecc, mask);
                    sweep.Append(name).Append('\t').Append(QrSweep.LevelCode(ecc)).Append('\t')
                         .Append(mask).Append('\t').Append(m.Version).Append('\n');
                    for (int y = 0; y < m.Size; y++)
                    {
                        for (int x = 0; x < m.Size; x++) sweep.Append(m.IsDark(x, y) ? '1' : '0');
                        sweep.Append('\n');
                    }
                }
            }
        }

        // The sweep must not have quietly shrunk: a case that stops fitting, or a Cases() edit, changes this.
        Assert.Equal(189, cases);
        Assert.Equal(OracleSweepSha256, QrSweep.Sha256Hex(sweep.ToString()));
    }

    /// <summary>
    /// Version 1 at level M with mask 0 for the payload "A", written out module by module. The aggregate hash above
    /// is a strong lock but a silent one; this is the symbol a reviewer can read with their own eyes, and the thing
    /// they can compare against any QR generator on the web in thirty seconds.
    /// </summary>
    [Fact]
    public void A_version_1_symbol_is_laid_out_exactly()
    {
        var m = QrCode.Encode(Encoding.UTF8.GetBytes("A"), QrErrorCorrection.Medium, 0);
        Assert.Equal(1, m.Version);
        Assert.Equal(21, m.Size);

        var rows = new string[m.Size];
        for (int y = 0; y < m.Size; y++)
        {
            var sb = new StringBuilder(m.Size);
            for (int x = 0; x < m.Size; x++) sb.Append(m.IsDark(x, y) ? '#' : '.');
            rows[y] = sb.ToString();
        }

        Assert.Equal(new[]
        {
            "#######..###..#######",
            "#.....#.#.###.#.....#",
            "#.###.#...#...#.###.#",
            "#.###.#..#....#.###.#",
            "#.###.#.##..#.#.###.#",
            "#.....#...#.#.#.....#",
            "#######.#.#.#.#######",
            "...........##........",
            "#.#.#.#...##....#..#.",
            "#..###...#....#...##.",
            ".#....#.###.#...#...#",
            "#.#.#....##...#...#..",
            "#.#..##.#.#.#.#.#.#.#",
            "........####.#.#.#.#.",
            "#######....#.###.####",
            "#.....#..#.###.###...",
            "#.###.#.##.#.###.##.#",
            "#.###.#.......#...##.",
            "#.###.#.##..#...#...#",
            "#.....#..##...#...##.",
            "#######.#...#.#.#.###",
        }, rows);

        Assert.Equal(21, rows[0].Length);
        Assert.Equal("#######", rows[0][..7]);                    // top-left finder, outer ring
        Assert.Equal("#######", rows[0][14..]);                   // top-right finder
        Assert.Equal("#######", rows[20][..7]);                   // bottom-left finder
        Assert.Equal(".......", rows[7][..7]);                    // its separator
        // The vertical timing column alternates from row 8 to row 12 inclusive.
        for (int y = 8; y <= 12; y++)
            Assert.Equal(y % 2 == 0, m.IsDark(6, y));
        // The always-dark module (ISO/IEC 18004 §8.9) sits at (8, size-8).
        Assert.True(m.IsDark(8, m.Size - 8));
    }

    /// <summary>The format information a scanner reads must state the level and the mask the symbol was actually
    /// built with — not what the encoder intended. Read back off the modules, both copies implicitly.</summary>
    [Theory]
    [InlineData(QrErrorCorrection.Low, 1)]
    [InlineData(QrErrorCorrection.Medium, 0)]
    [InlineData(QrErrorCorrection.Quartile, 3)]
    [InlineData(QrErrorCorrection.High, 2)]
    public void The_symbol_states_its_own_error_correction_level(QrErrorCorrection ecc, int expectedFormatBits)
    {
        for (int mask = 0; mask < 8; mask++)
        {
            var m = QrCode.Encode(Encoding.UTF8.GetBytes("Apex"), ecc, mask);
            Assert.Equal(expectedFormatBits, QrSweep.ReadEccBits(m));
            Assert.Equal(mask, QrSweep.ReadMask(m));
        }
    }

    /// <summary>
    /// The version chosen for a payload, at every level, across every byte-mode capacity boundary. These are the
    /// standard's published byte-mode capacities; a wrong entry in either error-correction table moves one of them.
    /// </summary>
    [Theory]
    // level L: v1 holds 17 bytes, v2 holds 32, v9 holds 230, v10 holds 271 (the count field widens at v10)
    [InlineData(17, QrErrorCorrection.Low, 1)]
    [InlineData(18, QrErrorCorrection.Low, 2)]
    [InlineData(32, QrErrorCorrection.Low, 2)]
    [InlineData(33, QrErrorCorrection.Low, 3)]
    [InlineData(230, QrErrorCorrection.Low, 9)]
    [InlineData(231, QrErrorCorrection.Low, 10)]
    [InlineData(271, QrErrorCorrection.Low, 10)]
    [InlineData(272, QrErrorCorrection.Low, 11)]
    [InlineData(2953, QrErrorCorrection.Low, 40)]
    // level M
    [InlineData(14, QrErrorCorrection.Medium, 1)]
    [InlineData(15, QrErrorCorrection.Medium, 2)]
    [InlineData(2331, QrErrorCorrection.Medium, 40)]
    // level Q
    [InlineData(11, QrErrorCorrection.Quartile, 1)]
    [InlineData(12, QrErrorCorrection.Quartile, 2)]
    [InlineData(1663, QrErrorCorrection.Quartile, 40)]
    // level H — including version 8, whose block count was the one table entry the oracle caught wrong.
    [InlineData(7, QrErrorCorrection.High, 1)]
    [InlineData(8, QrErrorCorrection.High, 2)]
    [InlineData(84, QrErrorCorrection.High, 8)]
    [InlineData(85, QrErrorCorrection.High, 9)]
    [InlineData(1273, QrErrorCorrection.High, 40)]
    public void The_smallest_version_that_holds_the_payload_is_chosen(int byteCount, QrErrorCorrection ecc, int expected)
    {
        Assert.Equal(expected, QrCode.ChooseVersion(byteCount, ecc));
        var m = QrCode.Encode(new byte[byteCount], ecc);
        Assert.Equal(expected, m.Version);
        Assert.Equal(17 + 4 * expected, m.Size);
    }

    [Fact]
    public void A_payload_too_large_for_any_version_is_refused_rather_than_truncated()
    {
        var ex = Assert.Throws<ArgumentException>(() => QrCode.Encode(new byte[2954], QrErrorCorrection.Low));
        Assert.Contains("2954", ex.Message, StringComparison.Ordinal);
        Assert.Contains("40", ex.Message, StringComparison.Ordinal);
        // …and one byte less is the largest that fits, so the refusal is on the boundary and not merely early.
        Assert.Equal(40, QrCode.Encode(new byte[2953], QrErrorCorrection.Low).Version);
    }

    /// <summary>
    /// <b>Ours, not the oracle's.</b> Which of the eight masks wins is this encoder's own decision (see the class
    /// remarks). This pins it so a change in the penalty scoring cannot pass unnoticed, and states plainly that these
    /// numbers are a regression lock rather than an external authority.
    /// </summary>
    [Theory]
    [InlineData("A", QrErrorCorrection.Medium, 4)]
    [InlineData("HELLO WORLD", QrErrorCorrection.Low, 4)]
    [InlineData("HELLO WORLD", QrErrorCorrection.Quartile, 3)]
    [InlineData("Apex Solutions", QrErrorCorrection.Medium, 6)]
    public void Our_mask_choice_is_stable(string payload, QrErrorCorrection ecc, int expectedMask)
    {
        var m = QrCode.Encode(payload, ecc);
        Assert.Equal(expectedMask, QrSweep.ReadMask(m));
    }

    [Fact]
    public void Encoding_is_deterministic_across_runs()
    {
        const string jws = "eyJhbGciOiJSUzI1NiJ9.eyJkYXRhIjoie1wiSXJuXCI6XCJhNWMxMmJiZVwifSJ9.SIGNATURE";
        var a = QrCode.Encode(jws, QrErrorCorrection.Medium);
        var b = QrCode.Encode(jws, QrErrorCorrection.Medium);
        Assert.Equal(a.Version, b.Version);
        for (int y = 0; y < a.Size; y++)
            for (int x = 0; x < a.Size; x++)
                Assert.Equal(a.IsDark(x, y), b.IsDark(x, y));
    }

    [Fact]
    public void Reading_outside_the_symbol_is_light_so_a_consumer_may_over_scan_into_the_quiet_zone()
    {
        var m = QrCode.Encode("A", QrErrorCorrection.Low);
        Assert.False(m.IsDark(-1, 0));
        Assert.False(m.IsDark(0, -1));
        Assert.False(m.IsDark(m.Size, 0));
        Assert.False(m.IsDark(0, m.Size));
    }
}
