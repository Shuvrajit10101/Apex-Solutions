using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace Apex.Ledger.Tests;

/// <summary>
/// <b>Drift locks for the census derivation</b> — <c>docs/full-clone-census.md</c>.
///
/// <para><b>Why this exists, stated as the measurement that forced it.</b> §1.2's integers are supposed to be
/// DERIVED by re-running §1.2a's own <c>awk</c>, never typed. That <c>awk</c> matches a state cell as the BARE
/// token <c>| COMPLETE |</c>, <c>| PARTIAL |</c>, <c>| ABSENT |</c> or <c>| UNDETERMINED |</c>. Between
/// 2026-09-06 and 2026-09-07 ten rows had been written as <c>| **PARTIAL** |</c> — bold markdown — which that
/// pattern cannot match, so each was counted in its area's ROW total and in NO state bucket. The literal output
/// was <c>TOTAL rows=216 C=52 P=110 A=44 U=0 sum=206</c>: 216 rows against a state sum of 206, ten rows falling
/// silently through the floor. The command printed <c>!! UNPARSED:</c> for every one of them and it was read as
/// noise. Every one of the ten was a row this campaign had just LANDED, so the published figures understated our
/// own work — the failure mode that is hardest to notice, because nobody double-checks a number that flatters
/// nobody.</para>
///
/// <para><b>Four surfaces, not one.</b> The same fact is asserted in four places in that document, and on
/// 2026-09-07 all four disagreed: the §1.2a ROWS (the truth), the §1.2 TABLE, each AREA HEADING's stated split,
/// and the prose. Area 10's heading and table row had been stale since row 10.1 moved when Credit Limits shipped
/// in PR #62, and nobody noticed for a wave. A lock that checked only the token would have let that stand, so
/// this one reconciles every derived surface against the rows.</para>
///
/// <para><b>What each test guards.</b>
/// <list type="bullet">
/// <item><see cref="Every_state_cell_is_one_of_the_four_bare_tokens"/> — the token itself.</item>
/// <item><see cref="The_state_sum_equals_the_row_count_in_every_area"/> — <c>sum == rows</c>, per area and
///   overall. This is the arithmetic the broken run violated.</item>
/// <item><see cref="The_published_awk_classifies_every_row_the_same_way_this_lock_does"/> — re-implements the
///   document's OWN loose substring match and requires it to agree with the strict cell parse. This is the test
///   that speaks for the reader who runs the command printed in the document rather than this suite.</item>
/// <item><see cref="Every_area_heading_matches_its_own_rows"/> and
///   <see cref="The_1_2_table_matches_the_counted_rows"/> — the two derived surfaces.</item>
/// </list></para>
///
/// <para><b>Non-vacuity is asserted, not assumed.</b> <see cref="The_lock_actually_reads_the_census"/> proves the
/// parser found the real document rather than an empty scan, and
/// <see cref="The_lock_bites_on_every_way_this_has_actually_broken"/> runs the SAME validator over synthetic
/// documents carrying each defect — a bolded cell, a struck cell, a mis-summed area, a stale heading, a stale
/// §1.2 row — and requires each to be reported BY NAME. A lock that does not bite is worse than none, because it
/// converts an unchecked file into one everybody believes is checked.</para>
///
/// <para><b>Honest limits.</b> This lock proves the document is INTERNALLY CONSISTENT and machine-readable. It
/// cannot tell you a state is CORRECT — that a row graded PARTIAL really is partial is a judgement against the
/// code and against R7's sources, and no test in this file has an opinion about it. It also deliberately does
/// NOT pin the complete/partial/absent split, which moves every wave; pinning that would turn every landed slice
/// into a red suite and teach the next author to edit the expectation instead of the rows.</para>
/// </summary>
public sealed class CensusDerivationDriftLockTests
{
    // ============================================================ the shape of the document, as shared constants
    // Each constant is used BOTH by the real scan and by the bite proof, so the two can never drift apart.

    /// <summary>The four legal state tokens. A state cell must equal one of these EXACTLY — no bold, no
    /// strikethrough, no trailing annotation.</summary>
    private static readonly string[] LegalStates = ["COMPLETE", "PARTIAL", "ABSENT", "UNDETERMINED"];

    /// <summary>§1.2a's area heading, which states its own split: <c>#### Area 6 — … · 42 rows · 18 complete /
    /// 20 partial / 4 absent</c>.</summary>
    private const string AreaHeadingPattern =
        @"^#### Area (\d+) [^\n]*?· (\d+) rows · (\d+) complete / (\d+) partial / (\d+) absent\s*$";

    /// <summary>A §1.2a capability row: <c>| 6.26 | Kerala Flood Cess | PARTIAL | evidence… |</c>. This is the
    /// document's own row pattern, verbatim.</summary>
    private const string CapabilityRowPattern = @"^\| ([0-9]+\.[0-9]+) \|";

    /// <summary>The line that ENDS §1.2a's areas, matching the document's own <c>awk</c>.</summary>
    private const string AreasEndPattern = @"^#### 1\.2b";

    /// <summary>The §1.2 section heading, and the heading that ends it (the superseded snapshot).</summary>
    private const string Section12Start = @"^### 1\.2 The number";
    private const string Section12End = @"^#### 1\.2 \(superseded\)";

    /// <summary>A §1.2 body row: <c>| 6 | Statutory… | 42 | 18 | 20 | 4 | 0 |</c>.</summary>
    private const string Table12RowPattern =
        @"^\| (\d+) \| .*? \| (\d+) \| (\d+) \| (\d+) \| (\d+) \| (\d+) \|\s*$";

    /// <summary>The §1.2 TOTAL row, whose figures are bold: <c>| | **TOTAL** | **216** | … |</c>.</summary>
    private const string Table12TotalPattern =
        @"^\| \| \*\*TOTAL\*\* \| \*\*(\d+)\*\* \| \*\*(\d+)\*\* \| \*\*(\d+)\*\* \| \*\*(\d+)\*\* \| \*\*(\d+)\*\* \|\s*$";

    /// <summary>
    /// The number of capability rows the census is scoped to. This is a USER RULING (ruling 10 brought the former
    /// §3 and §4 in: <c>200 + 9 + 7 = 216</c>), not a derived figure, so it is anchored here deliberately. If a
    /// capability row is genuinely added or removed this test SHOULD stop the suite — see its failure text.
    ///
    /// <para>🔴 <b>MOVED <c>216</c> → <c>221</c> ON 2026-09-07 BY USER RULINGS 19 AND 20 (R12,
    /// <c>plan.md</c> §5, <c>TWO FURTHER USER RULINGS (R12, 2026-09-07)</c>) — <c>216 + 8 − 3 = 221</c>. This is
    /// the ONE case this constant may move, and it is the case this test's own failure text describes: a
    /// user-ruled scope change, not an expectation bent to fit a state cell.</b>
    /// <list type="bullet">
    ///   <item><b><c>+8</c> — ruling 19.</b> Eight vendor-attested capabilities had <b>no census row at all</b>,
    ///     each verified twice by name (zero hits in the census, zero in <c>src/</c>), so they were missing rows
    ///     AND missing features. Area 7 gained <b>7.22–7.26</b> (Attendance Sheet, Pay Head Employee Breakup,
    ///     Employee Pay Head Breakup, Payroll Statutory Summary, Income Tax Computation report); Area 8 gained
    ///     <b>8.11–8.13</b> (Connected Banking, Payment Request, auto-create vouchers from a bank statement).
    ///     All eight are <c>ABSENT</c>, so the old 216 understated us in the numerator and the denominator at
    ///     once. The honest total is <b>224</b>.</item>
    ///   <item><b><c>−3</c> — ruling 20, and it is NOT progress.</b> Rows <b>15.3</b>, <b>15.7</b> and
    ///     <b>15.9</b> (the 2005 four-slab VAT structure, Service Tax + ST-3, Fringe Benefit Tax) were STRUCK
    ///     as dead law and moved OUT of §1.2a into <b>§1.2d</b>, which is why this lock still sees a consistent
    ///     document: a struck row leaves the counted region entirely rather than taking a fifth state token,
    ///     which would have reddened <c>NonBareStateCells</c> and <c>AreasWhereSumDoesNotEqualRows</c> — <i>and
    ///     would have reddened them correctly.</i> All three were <c>ABSENT</c>, so striking them lowered the
    ///     denominator and the missing count <b>without shipping anything</b>.</item>
    /// </list>
    /// The census's own <c>awk</c> returns <c>TOTAL rows=221 C=52 P=122 A=47 U=0 sum=221</c> after the move; it
    /// returned <c>… rows=216 … A=42 … sum=216</c> before it. Not one state cell changed.</para>
    /// </summary>
    private const int ScopedCapabilityRows = 221;

    /// <summary>§1.2a's sixteen areas.</summary>
    private const int ScopedAreas = 16;

    // ============================================================ the parsed document

    private sealed record CapabilityRow(int Line, string Id, string StateCell, string Raw);

    private sealed record Area(
        int HeadingLine,
        int Number,
        string Heading,
        int StatedRows,
        int StatedComplete,
        int StatedPartial,
        int StatedAbsent,
        List<CapabilityRow> Rows);

    private sealed record Table12Row(int Line, int Number, int InScope, int Complete, int Partial, int Absent, int Undetermined);

    private sealed record Census(List<Area> Areas, List<Table12Row> Table, Table12Row? Total);

    // ============================================================ scanning machinery

    /// <summary>The repository root — the directory holding <c>Apex.slnx</c>.</summary>
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Apex.slnx")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }

    /// <summary>The census document itself.</summary>
    private static string CensusPath()
    {
        var path = Path.Combine(RepoRoot(), "docs", "full-clone-census.md");
        Assert.True(File.Exists(path), $"the census is not where this lock expects it: {path}");
        return path;
    }

    private static string[] CensusLines() => File.ReadAllLines(CensusPath());

    /// <summary>
    /// Parses §1.2a's areas and §1.2's table out of <paramref name="lines"/>. Deliberately takes the lines rather
    /// than reading the file, so the bite proofs can drive the SAME parser over synthetic documents.
    /// </summary>
    private static Census Parse(string[] lines)
    {
        var areaHeading = new Regex(AreaHeadingPattern, RegexOptions.Compiled);
        var capabilityRow = new Regex(CapabilityRowPattern, RegexOptions.Compiled);
        var areasEnd = new Regex(AreasEndPattern, RegexOptions.Compiled);
        var table12Row = new Regex(Table12RowPattern, RegexOptions.Compiled);
        var table12Total = new Regex(Table12TotalPattern, RegexOptions.Compiled);
        var section12Start = new Regex(Section12Start, RegexOptions.Compiled);
        var section12End = new Regex(Section12End, RegexOptions.Compiled);

        var areas = new List<Area>();
        Area? current = null;
        var inAreas = false;

        var table = new List<Table12Row>();
        Table12Row? total = null;
        var inSection12 = false;

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];

            // ---- §1.2, up to (and excluding) the superseded snapshot that repeats its shape
            if (section12Start.IsMatch(line)) { inSection12 = true; continue; }
            if (inSection12 && section12End.IsMatch(line)) inSection12 = false;
            if (inSection12)
            {
                var t = table12Row.Match(line);
                if (t.Success)
                    table.Add(new Table12Row(
                        i + 1, N(t, 1), N(t, 2), N(t, 3), N(t, 4), N(t, 5), N(t, 6)));

                var tot = table12Total.Match(line);
                if (tot.Success && total is null)
                    total = new Table12Row(i + 1, 0, N(tot, 1), N(tot, 2), N(tot, 3), N(tot, 4), N(tot, 5));
            }

            // ---- §1.2a's areas, using the document's own delimiters
            var h = areaHeading.Match(line);
            if (h.Success)
            {
                current = new Area(i + 1, N(h, 1), line.Trim(), N(h, 2), N(h, 3), N(h, 4), N(h, 5), []);
                areas.Add(current);
                inAreas = true;
                continue;
            }

            // A heading that CLAIMS to be an area but does not state its split would otherwise be invisible.
            if (line.StartsWith("#### Area ", StringComparison.Ordinal))
            {
                current = new Area(i + 1, -1, line.Trim(), -1, -1, -1, -1, []);
                areas.Add(current);
                inAreas = true;
                continue;
            }

            if (areasEnd.IsMatch(line)) { inAreas = false; current = null; continue; }

            if (!inAreas || current is null) continue;

            var r = capabilityRow.Match(line);
            if (!r.Success) continue;

            // The state is the THIRD pipe-delimited cell: "", id, name, state, evidence…
            var cells = line.Split('|');
            var state = cells.Length > 3 ? cells[3].Trim() : "<no third cell>";
            current.Rows.Add(new CapabilityRow(i + 1, r.Groups[1].Value, state, line));
        }

        return new Census(areas, table, total);

        static int N(Match m, int g) => int.Parse(m.Groups[g].Value, CultureInfo.InvariantCulture);
    }

    private static (int C, int P, int A, int U) Count(IEnumerable<CapabilityRow> rows)
    {
        var list = rows.ToList();
        return (list.Count(x => x.StateCell == "COMPLETE"),
                list.Count(x => x.StateCell == "PARTIAL"),
                list.Count(x => x.StateCell == "ABSENT"),
                list.Count(x => x.StateCell == "UNDETERMINED"));
    }

    // ============================================================ the validators
    // Each returns actionable complaints rather than asserting, so the bite proofs can drive the SAME code.

    /// <summary>Complains about every state cell that is not one of the four bare tokens.</summary>
    private static List<string> NonBareStateCells(Census census) =>
        census.Areas
            .SelectMany(a => a.Rows)
            .Where(r => !LegalStates.Contains(r.StateCell, StringComparer.Ordinal))
            .Select(r =>
                $"  row {r.Id} (census line {r.Line}): state cell is \"{r.StateCell}\", not a bare token.\n" +
                $"      {Truncate(r.Raw, 110)}")
            .ToList();

    /// <summary>Complains where an area's state buckets do not sum to its row count.</summary>
    private static List<string> AreasWhereSumDoesNotEqualRows(Census census)
    {
        var complaints = new List<string>();
        foreach (var a in census.Areas)
        {
            var (c, p, abs, u) = Count(a.Rows);
            var sum = c + p + abs + u;
            if (sum != a.Rows.Count)
                complaints.Add(
                    $"  {Truncate(a.Heading, 76)} (line {a.HeadingLine}):\n" +
                    $"      rows={a.Rows.Count} but sum={sum} (C={c} P={p} A={abs} U={u}) — " +
                    $"{a.Rows.Count - sum} row(s) counted in the row total and in NO state bucket:\n" +
                    string.Join("\n", a.Rows
                        .Where(r => !LegalStates.Contains(r.StateCell, StringComparer.Ordinal))
                        .Select(r => $"        {r.Id} (line {r.Line}) -> \"{r.StateCell}\"")));
        }
        return complaints;
    }

    /// <summary>Complains where an area heading's stated split disagrees with its own rows.</summary>
    private static List<string> HeadingsThatDisagreeWithTheirRows(Census census)
    {
        var complaints = new List<string>();
        foreach (var a in census.Areas)
        {
            if (a.Number < 0)
            {
                complaints.Add(
                    $"  line {a.HeadingLine}: \"{Truncate(a.Heading, 90)}\" is an Area heading that does not state " +
                    "its own split. Every area heading must read \"· N rows · C complete / P partial / A absent\".");
                continue;
            }

            var (c, p, abs, _) = Count(a.Rows);
            if (a.StatedRows == a.Rows.Count && a.StatedComplete == c && a.StatedPartial == p && a.StatedAbsent == abs)
                continue;

            complaints.Add(
                $"  Area {a.Number} heading (line {a.HeadingLine}) is stale:\n" +
                $"      heading says  {a.StatedRows} rows · {a.StatedComplete} complete / {a.StatedPartial} partial / {a.StatedAbsent} absent\n" +
                $"      its rows are  {a.Rows.Count} rows · {c} complete / {p} partial / {abs} absent");
        }
        return complaints;
    }

    /// <summary>Complains where a §1.2 table row, or the TOTAL, disagrees with the counted rows.</summary>
    private static List<string> Table12CellsThatDisagreeWithTheRows(Census census)
    {
        var complaints = new List<string>();

        foreach (var a in census.Areas.Where(x => x.Number > 0))
        {
            var row = census.Table.SingleOrDefault(t => t.Number == a.Number);
            if (row is null)
            {
                complaints.Add($"  §1.2 has no row for Area {a.Number}, which §1.2a defines at line {a.HeadingLine}.");
                continue;
            }

            var (c, p, abs, u) = Count(a.Rows);
            if (row.InScope == a.Rows.Count && row.Complete == c && row.Partial == p && row.Absent == abs && row.Undetermined == u)
                continue;

            complaints.Add(
                $"  §1.2 row {a.Number} (line {row.Line}) is stale:\n" +
                $"      table says  in-scope {row.InScope} · {row.Complete} / {row.Partial} / {row.Absent} / {row.Undetermined}\n" +
                $"      §1.2a says  in-scope {a.Rows.Count} · {c} / {p} / {abs} / {u}");
        }

        var allRows = census.Areas.SelectMany(a => a.Rows).ToList();
        var (tc, tp, ta, tu) = Count(allRows);

        if (census.Total is null)
            complaints.Add("  §1.2 has no TOTAL row in the shape \"| | **TOTAL** | **n** | … |\".");
        else if (census.Total.InScope != allRows.Count || census.Total.Complete != tc ||
                 census.Total.Partial != tp || census.Total.Absent != ta || census.Total.Undetermined != tu)
            complaints.Add(
                $"  §1.2 TOTAL (line {census.Total.Line}) is stale:\n" +
                $"      table says  {census.Total.InScope} · {census.Total.Complete} / {census.Total.Partial} / {census.Total.Absent} / {census.Total.Undetermined}\n" +
                $"      §1.2a says  {allRows.Count} · {tc} / {tp} / {ta} / {tu}");

        return complaints;
    }

    /// <summary>
    /// Re-implements the LOOSE substring match the document's own published <c>awk</c> uses — <c>$0 ~ /\| PARTIAL
    /// \|/</c> over the WHOLE line — and complains wherever it disagrees with the strict third-cell parse. This is
    /// the test that speaks for a reader who runs the command printed in §1.2a instead of running this suite.
    /// </summary>
    private static List<string> RowsThePublishedAwkWouldMisread(Census census)
    {
        var complaints = new List<string>();
        foreach (var r in census.Areas.SelectMany(a => a.Rows))
        {
            var awkHits = LegalStates.Where(s => r.Raw.Contains($"| {s} |", StringComparison.Ordinal)).ToList();
            var awkState = awkHits.Count == 1 ? awkHits[0] : awkHits.Count == 0 ? "<UNPARSED>" : $"<AMBIGUOUS: {string.Join(", ", awkHits)}>";

            if (awkState == r.StateCell) continue;

            complaints.Add(
                $"  row {r.Id} (census line {r.Line}): this lock reads the state cell as \"{r.StateCell}\", " +
                $"but §1.2a's published awk reads \"{awkState}\".\n" +
                (awkState == "<UNPARSED>"
                    ? "      The awk would count this row in `rows` and in NO state bucket, so `sum` would fall below `rows`.\n"
                    : "      The awk matches the whole line, so a state token appearing in the EVIDENCE cell can capture it.\n") +
                $"      {Truncate(r.Raw, 110)}");
        }
        return complaints;
    }

    private static string Truncate(string s, int n) => s.Length <= n ? s : s[..n] + " …";

    private static void Report(string what, List<string> complaints, string howToFix)
    {
        if (complaints.Count == 0) return;
        Assert.Fail(
            $"{what} — {complaints.Count} problem(s) in docs/full-clone-census.md:\n" +
            string.Join("\n", complaints) + "\n\n" + howToFix);
    }

    // ============================================================ the locks

    /// <summary>
    /// THE lock. A state cell must be the bare token. Ten rows were written as <c>| **PARTIAL** |</c> and the
    /// census's own counting command went blind to every one of them.
    /// </summary>
    [Fact]
    public void Every_state_cell_is_one_of_the_four_bare_tokens() =>
        Report("A §1.2a state cell is not a bare state token",
            NonBareStateCells(Parse(CensusLines())),
            "A state cell must be EXACTLY `COMPLETE`, `PARTIAL`, `ABSENT` or `UNDETERMINED` — no bold, no\n" +
            "strikethrough, no annotation. §1.2a's counting awk matches the bare token and silently drops anything\n" +
            "else, which is how the published split came to understate ten landed rows. Put the emphasis in the\n" +
            "EVIDENCE cell, where it costs nothing, and leave the state cell machine-readable.");

    /// <summary>The arithmetic the broken run violated: <c>sum</c> must equal <c>rows</c>.</summary>
    [Fact]
    public void The_state_sum_equals_the_row_count_in_every_area() =>
        Report("A §1.2a area's state buckets do not sum to its row count",
            AreasWhereSumDoesNotEqualRows(Parse(CensusLines())),
            "`sum` must equal `rows`. When it does not, rows are being counted in the denominator and in no state\n" +
            "bucket — the census is understating or overstating itself by exactly that difference. Fix the named\n" +
            "state cells, then RE-RUN §1.2a's awk and transcribe its output; never hand-edit an integer in §1.2.");

    /// <summary>The reader who runs the printed command must get the same answer this suite does.</summary>
    [Fact]
    public void The_published_awk_classifies_every_row_the_same_way_this_lock_does() =>
        Report("§1.2a's published awk would read a row differently from its actual state cell",
            RowsThePublishedAwkWouldMisread(Parse(CensusLines())),
            "The command printed in §1.2a is what every reader and every agent re-derives §1.2 with, so it must\n" +
            "agree with the document cell for cell. Keep state cells bare, and keep the bare tokens OUT of evidence\n" +
            "prose (write **partial** or \"graded PARTIAL\" there, never the pipe-delimited `| PARTIAL |` form).");

    /// <summary>Area 10's heading was stale for a whole wave after row 10.1 moved. Not again.</summary>
    [Fact]
    public void Every_area_heading_matches_its_own_rows() =>
        Report("A §1.2a area heading disagrees with the rows underneath it",
            HeadingsThatDisagreeWithTheirRows(Parse(CensusLines())),
            "An area heading states its own split and is therefore a DERIVED figure. Re-run §1.2a's awk and\n" +
            "transcribe the per-area line it prints. Area 10's heading sat stale for a full wave after row 10.1\n" +
            "moved when Credit Limits shipped, because nothing checked it.");

    /// <summary>§1.2's integers are column sums of §1.2a and nothing else — the document says so itself.</summary>
    [Fact]
    public void The_1_2_table_matches_the_counted_rows() =>
        Report("§1.2's table disagrees with §1.2a's rows",
            Table12CellsThatDisagreeWithTheRows(Parse(CensusLines())),
            "§1.2 says of itself: \"EVERY INTEGER BELOW IS A COLUMN SUM OF §1.2a AND NOTHING ELSE… If a row there\n" +
            "changes state, this table is re-summed. Never edit an integer here directly.\" Re-run the awk and\n" +
            "transcribe. If the awk and the table disagree, THE ROWS ARE RIGHT AND THE TABLE IS THE DEFECT.");

    // ============================================================ non-vacuity

    /// <summary>
    /// Guards against the failure mode where the parser matches nothing and every lock above passes vacuously —
    /// the exact shape of the defect that has already bitten this project twice (a quoted grep result that
    /// pointed at a blank line, and the bolded state cells).
    /// </summary>
    [Fact]
    public void The_lock_actually_reads_the_census()
    {
        var census = Parse(CensusLines());
        var rows = census.Areas.SelectMany(a => a.Rows).ToList();

        Assert.True(census.Areas.Count == ScopedAreas,
            $"expected §1.2a's {ScopedAreas} areas, parsed {census.Areas.Count}. Either the document's area " +
            "headings changed shape (fix AreaHeadingPattern) or an area was added or removed (a scope decision — " +
            "update ScopedAreas deliberately and say so in §1.2).");

        Assert.True(rows.Count == ScopedCapabilityRows,
            $"expected {ScopedCapabilityRows} capability rows, parsed {rows.Count}. The denominator is a USER " +
            "RULING (ruling 10: 200 + 9 + 7 = 216), not something a slice may move in passing. If a capability " +
            "row was genuinely added or removed — earlier verification found vendor-attested capabilities with NO " +
            "census row at all, in Areas 7 and 8 — that is a real denominator move: record it in §1.2, tell the " +
            "user the new total, and update ScopedCapabilityRows here in the same change.");

        Assert.NotNull(census.Total);
        Assert.Equal(ScopedCapabilityRows, census.Table.Sum(t => t.InScope));

        // A parse that classified everything into one bucket would satisfy the locks above and mean nothing.
        var (c, p, a, _) = Count(rows);
        Assert.True(c > 0 && p > 0 && a > 0,
            $"the parse produced a degenerate split (C={c} P={p} A={a}); the state cells are not being read.");
    }

    /// <summary>
    /// THE BITE PROOF. Runs the SAME validators over synthetic documents carrying each defect this lock exists to
    /// catch, and requires each to be reported BY NAME. Without this, "the scan found nothing" cannot be
    /// distinguished from "the scan matches nothing".
    /// </summary>
    [Fact]
    public void The_lock_bites_on_every_way_this_has_actually_broken()
    {
        // A minimal, well-formed census: one area, three rows, a §1.2 table that agrees with them.
        static string[] Document(string stateOf62, string heading, string table12, string total) =>
        [
            "### 1.2 The number",
            "",
            "| # | Area | In scope | Complete | Partial | Absent | Undetermined |",
            "|---|---|---:|---:|---:|---:|---:|",
            table12,
            total,
            "",
            "#### 1.2 (superseded) — the old snapshot, which repeats this shape and must be ignored",
            "| 6 | Statutory | 99 | 99 | 99 | 99 | 99 |",
            "",
            "### 1.2a THE NAMED CAPABILITY LIST",
            "",
            heading,
            "",
            "| # | Capability | State | Evidence |",
            "|---|---|---|---|",
            "| 6.1 | A complete thing | COMPLETE | it is there |",
            $"| 6.2 | A partial thing | {stateOf62} | a named missing piece |",
            "| 6.3 | An absent thing | ABSENT | zero hits |",
            "",
            "#### 1.2b WHAT MOVED",
        ];

        const string GoodHeading = "#### Area 6 — Statutory · 3 rows · 1 complete / 1 partial / 1 absent";
        const string GoodTable = "| 6 | Statutory | 3 | 1 | 1 | 1 | 0 |";
        const string GoodTotal = "| | **TOTAL** | **3** | **1** | **1** | **1** | **0** |";

        // ---- the control: the well-formed document must be CLEAN through every validator.
        var clean = Parse(Document("PARTIAL", GoodHeading, GoodTable, GoodTotal));
        Assert.Empty(NonBareStateCells(clean));
        Assert.Empty(AreasWhereSumDoesNotEqualRows(clean));
        Assert.Empty(HeadingsThatDisagreeWithTheirRows(clean));
        Assert.Empty(Table12CellsThatDisagreeWithTheRows(clean));
        Assert.Empty(RowsThePublishedAwkWouldMisread(clean));

        // ---- bite 1: THE defect — a bolded state cell, exactly as the ten rows were written.
        var bolded = Parse(Document("**PARTIAL**", GoodHeading, GoodTable, GoodTotal));
        var boldComplaints = NonBareStateCells(bolded);
        Assert.Single(boldComplaints);
        Assert.Contains("row 6.2", boldComplaints[0], StringComparison.Ordinal);
        Assert.Contains("**PARTIAL**", boldComplaints[0], StringComparison.Ordinal);

        // and it must ALSO show up as sum != rows, which is how the broken run announced itself.
        var boldSum = AreasWhereSumDoesNotEqualRows(bolded);
        Assert.Single(boldSum);
        Assert.Contains("rows=3 but sum=2", boldSum[0], StringComparison.Ordinal);
        Assert.Contains("6.2", boldSum[0], StringComparison.Ordinal);

        // and the published awk must be shown to go blind to it.
        var boldAwk = RowsThePublishedAwkWouldMisread(bolded);
        Assert.Single(boldAwk);
        Assert.Contains("<UNPARSED>", boldAwk[0], StringComparison.Ordinal);

        // ---- bite 2: a struck cell — the other way emphasis has been added to a state.
        var struck = NonBareStateCells(Parse(Document("~~ABSENT~~ PARTIAL", GoodHeading, GoodTable, GoodTotal)));
        Assert.Single(struck);
        Assert.Contains("row 6.2", struck[0], StringComparison.Ordinal);

        // ---- bite 3: a stale area heading (Area 10's real failure, after row 10.1 moved).
        const string StaleHeading = "#### Area 6 — Statutory · 3 rows · 1 complete / 0 partial / 2 absent";
        var staleHeading = HeadingsThatDisagreeWithTheirRows(Parse(Document("PARTIAL", StaleHeading, GoodTable, GoodTotal)));
        Assert.Single(staleHeading);
        Assert.Contains("Area 6 heading", staleHeading[0], StringComparison.Ordinal);
        Assert.Contains("1 complete / 0 partial / 2 absent", staleHeading[0], StringComparison.Ordinal);

        // ---- bite 4: a stale §1.2 row.
        const string StaleTable = "| 6 | Statutory | 3 | 1 | 0 | 2 | 0 |";
        var staleRow = Table12CellsThatDisagreeWithTheRows(Parse(Document("PARTIAL", GoodHeading, StaleTable, GoodTotal)));
        Assert.Single(staleRow);
        Assert.Contains("§1.2 row 6", staleRow[0], StringComparison.Ordinal);

        // ---- bite 5: a stale TOTAL.
        const string StaleTotal = "| | **TOTAL** | **3** | **1** | **0** | **2** | **0** |";
        var staleTotal = Table12CellsThatDisagreeWithTheRows(Parse(Document("PARTIAL", GoodHeading, GoodTable, StaleTotal)));
        Assert.Single(staleTotal);
        Assert.Contains("§1.2 TOTAL", staleTotal[0], StringComparison.Ordinal);

        // ---- bite 6: a state token hiding in the EVIDENCE cell must not be able to capture the awk.
        var poisoned = RowsThePublishedAwkWouldMisread(Parse(
        [
            "### 1.2a THE NAMED CAPABILITY LIST",
            GoodHeading,
            "| 6.1 | A thing | COMPLETE | it is there |",
            "| 6.2 | A thing | PARTIAL | was graded | ABSENT | before |",
            "| 6.3 | A thing | ABSENT | zero hits |",
            "#### 1.2b WHAT MOVED",
        ]));
        Assert.Single(poisoned);
        Assert.Contains("row 6.2", poisoned[0], StringComparison.Ordinal);
        Assert.Contains("AMBIGUOUS", poisoned[0], StringComparison.Ordinal);
    }
}
