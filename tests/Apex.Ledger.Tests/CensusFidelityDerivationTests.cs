using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace Apex.Ledger.Tests;

/// <summary>
/// <b>T0-11 review C19/L3-05 — §1.3's four fidelity figures, re-derived by machine instead of by prose.</b>
///
/// <para><b>The defect this exists for.</b> <c>docs/full-clone-census.md</c> §1.3 declares itself the single
/// derivation of the four fidelity figures — <i>"THESE FOUR FIGURES ARE MAINTAINED HERE AND NOWHERE ELSE"</i> —
/// and orders every reader to <i>"re-count the headers; never carry a digit forward"</i>. The rule was prose, and
/// prose cannot be re-run: the commit that added item 14 wrote its header as <c>GROUNDED; PARTLY BUILT</c> while
/// editing derivation bullet 2 to count <i>"the items whose header records GROUNDED, NOT YET BUILT. Today that is
/// item 14 alone"</i> — a literal <b>no live item carries</b>. The only §1.3 header text containing that string is
/// item 12's struck-out quotation of its own superseded grade, and item 12 is already inside figure (1). So a
/// reader obeying the block's own instruction re-derived <b>grounded = 12</b> and <b>total = 204</b> against the
/// stated 13 and 203. The digits were right; the derivation that is supposed to produce them was not — the same
/// class of defect the block itself had diagnosed and claimed to have just repaired.</para>
///
/// <para><b>What changed in the document, and why it is the fix rather than a patch.</b> Every numbered item in
/// §1.3 now carries one machine-readable grade — <c>[GRADE: COMPARED]</c>, <c>[GRADE: GROUNDED-AHEAD]</c> or
/// <c>[GRADE: METHOD-NOTE]</c> — and the derivation bullets read those tokens instead of describing header prose.
/// The old rules could not be executed at all: bullet 1's <i>"header records the surface as BUILT / shipped"</i>
/// matches neither items 1-8 (one-line entries naming a source, with no grade word) nor item 9 (<c>PARTIAL</c>),
/// and it does match item 14 (<c>PARTLY BUILT ... half shipped</c>), which it must not. §1.2a has carried a
/// runnable counting command since 2026-08-18; §1.3 now carries one too, and this test is that command in C#.</para>
///
/// <para><b>No integer was edited.</b> The four figures are re-derived here from the grades and asserted against
/// what the anchor block states, against a denominator read out of §1.2 rather than hard-coded. They reproduce at
/// 12 / 13 / 204 / 203.</para>
/// </summary>
public sealed class CensusFidelityDerivationTests
{
    private const string Census = "docs/full-clone-census.md";

    private const string Compared = "[GRADE: COMPARED]";
    private const string GroundedAhead = "[GRADE: GROUNDED-AHEAD]";
    private const string MethodNote = "[GRADE: METHOD-NOTE]";

    private static readonly string[] AllGrades = { Compared, GroundedAhead, MethodNote };

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Apex.slnx")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }

    private static string[] CensusLines() =>
        File.ReadAllLines(Path.Combine(RepoRoot(), Census.Replace('/', Path.DirectorySeparatorChar)));

    /// <summary>§1.3 runs from its own "### 1.3 " heading to the next "## " heading.</summary>
    private static (int Start, int End) SectionOneThree(string[] lines)
    {
        int start = Array.FindIndex(lines, l => l.StartsWith("### 1.3 ", StringComparison.Ordinal));
        Assert.True(start >= 0, "docs/full-clone-census.md has no `### 1.3 ` heading.");
        int end = Array.FindIndex(lines, start + 1, l => l.StartsWith("## ", StringComparison.Ordinal));
        Assert.True(end > start, "§1.3 is not bounded by a following `## ` heading.");
        return (start, end);
    }

    /// <summary>
    /// One numbered capability item, and the HEADER its grade must sit in: the numbered line at column 0, plus
    /// every following line up to the first blank line or the next numbered line at column 0, whichever comes
    /// first. Items 1-8 are one-line entries, so their header is that single line; items 9-14 open with a
    /// multi-line paragraph.
    /// </summary>
    private sealed record Item(int Number, int LineNo, string Header);

    private static readonly Regex NumberedItem = new(@"^(\d+)\. ", RegexOptions.Compiled);

    private static List<Item> Items(string[] lines)
    {
        var (start, end) = SectionOneThree(lines);
        var starts = new List<(int Number, int Index)>();
        for (int i = start; i < end; i++)
        {
            var m = NumberedItem.Match(lines[i]);
            if (m.Success) starts.Add((int.Parse(m.Groups[1].Value), i));
        }

        var items = new List<Item>();
        foreach (var (number, index) in starts)
        {
            var header = new List<string> { lines[index] };
            for (int j = index + 1; j < end; j++)
            {
                if (lines[j].Trim().Length == 0) break;
                if (NumberedItem.IsMatch(lines[j])) break;
                header.Add(lines[j]);
            }
            items.Add(new Item(number, index + 1, string.Join("\n", header)));
        }
        return items;
    }

    private static string GradeOf(Item item)
    {
        var found = AllGrades.Where(g => item.Header.Contains(g, StringComparison.Ordinal)).ToList();
        Assert.True(found.Count == 1,
            $"§1.3 item {item.Number} (census:{item.LineNo}) carries {found.Count} grade tokens, not exactly one. "
          + "Every numbered item must declare one of " + string.Join(" / ", AllGrades)
          + " inside its header — the derivation is a count of those tokens and cannot read prose.\n"
          + "      Header opens: " + item.Header.Split('\n')[0]);
        return found[0];
    }

    // ================================================================ the counting command, in C#

    /// <summary>
    /// The derivation, re-run. This is §1.3's own counting command executed against the live rows: count the grade
    /// tokens, apply the four bullets, and assert the result equals what the anchor block STATES. If a header's
    /// grade moves and the anchor block is not re-derived, this goes red — which is the exact failure (item 12 on
    /// the first pass of 2026-08-20, item 14 on the second) the block has now been caught by twice.
    /// </summary>
    [Fact]
    public void The_four_fidelity_figures_re_derive_from_the_item_grades()
    {
        var lines = CensusLines();
        var items = Items(lines);
        Assert.True(items.Count >= 14, $"§1.3 lists only {items.Count} numbered items; the derivation is vacuous.");

        var grades = items.ToDictionary(i => i.Number, GradeOf);

        int compared = grades.Values.Count(g => g == Compared);
        int groundedAhead = grades.Values.Count(g => g == GroundedAhead);
        int methodNotes = grades.Values.Count(g => g == MethodNote);
        Assert.True(methodNotes >= 1, "no item is graded METHOD-NOTE; item 13's own header says it is one.");

        int denominator = Denominator(lines);

        int derivedShippedAndCompared = compared;                             // bullet 1
        int derivedGrounded = compared + groundedAhead;                       // bullet 2
        int derivedUncompared = denominator - compared;                       // bullet 3
        int derivedNoSourcedVerification = derivedUncompared - groundedAhead; // bullet 4

        var (stated, statedLine) = StatedFigures(lines);

        Assert.True(
            (derivedShippedAndCompared, derivedGrounded, derivedUncompared, derivedNoSourcedVerification) == stated,
            $"§1.3's anchor block (census:{statedLine}) states "
          + $"{stated.Item1} / {stated.Item2} / {stated.Item3} / {stated.Item4}, but re-counting the item grades "
          + $"gives {derivedShippedAndCompared} / {derivedGrounded} / {derivedUncompared} / "
          + $"{derivedNoSourcedVerification} against a denominator of {denominator}.\n"
          + $"      COMPARED={compared} GROUNDED-AHEAD={groundedAhead} METHOD-NOTE={methodNotes}\n"
          + "      Re-derive the block from the rows; never edit one of its digits.");
    }

    /// <summary>§1.2's denominator, read out of §1.2 rather than hard-coded, and confirmed against the anchor
    /// block's own quotation of it. Two independent statements of one number are exactly what drifts apart.</summary>
    private static int Denominator(string[] lines)
    {
        var rx = new Regex(@"A full clone requires \*{0,2}(\d+)\*{0,2} named capabilities");
        var hit = lines.Select(l => rx.Match(l)).FirstOrDefault(m => m.Success);
        Assert.True(hit is { Success: true }, "§1.2's \"A full clone requires N named capabilities\" sentence is gone.");
        int n = int.Parse(hit!.Groups[1].Value);

        var quoted = new Regex(@"against §1\.2's \*{0,2}(\d+)\*{0,2} denominator");
        var q = lines.Select(l => quoted.Match(l)).FirstOrDefault(m => m.Success);
        Assert.True(q is { Success: true }, "the anchor block no longer names the denominator it is derived against.");
        Assert.Equal(n, int.Parse(q!.Groups[1].Value));
        return n;
    }

    /// <summary>The four figures as the anchor block STATES them: the first unstruck statement after the block's
    /// own "MAINTAINED HERE AND NOWHERE ELSE" declaration. Lines carrying "~~" are superseded quotations the
    /// census deliberately keeps, and must never be read as current.</summary>
    private static ((int, int, int, int) Figures, int LineNo) StatedFigures(string[] lines)
    {
        int anchor = Array.FindIndex(lines,
            l => l.Contains("MAINTAINED HERE AND NOWHERE ELSE", StringComparison.Ordinal));
        Assert.True(anchor >= 0, "§1.3's anchor block declaration is gone.");

        var rx = new Regex(@"(\d+) shipped-and-compared \S+ (\d+) grounded \S+ (\d+) uncompared as shipped \S+ (\d+) with no sourced");
        var struckSpan = new Regex("~~.*?~~", RegexOptions.Singleline);
        for (int i = anchor; i < lines.Length; i++)
        {
            var m = rx.Match(struckSpan.Replace(lines[i], string.Empty));
            if (!m.Success) continue;
            return ((int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value),
                     int.Parse(m.Groups[3].Value), int.Parse(m.Groups[4].Value)), i + 1);
        }

        Assert.Fail("the anchor block no longer states the four figures in the form "
                  + "\"N shipped-and-compared . N grounded . N uncompared as shipped . N with no sourced verification\".");
        return default;
    }

    // ================================================================ the defects that were live

    /// <summary>
    /// The load-bearing half of C19/L3-05. Each counting bullet must be expressed in a grade token that a live
    /// item actually carries. Bullet 2 named the literal "GROUNDED, NOT YET BUILT", which no live header has ever
    /// carried — item 14 was born "GROUNDED; PARTLY BUILT" in the same commit (96db1c0) that wrote the bullet, so
    /// the block was internally inconsistent at the moment it was written, and the only header text matching the
    /// bullet was item 12's struck-through quote of its own superseded grade.
    /// </summary>
    [Fact]
    public void Every_derivation_bullet_counts_a_grade_some_live_item_carries()
    {
        var lines = CensusLines();
        var items = Items(lines);
        var live = items.Select(GradeOf).Distinct(StringComparer.Ordinal).ToHashSet(StringComparer.Ordinal);

        var (start, end) = SectionOneThree(lines);
        var bulletGrades = new Regex(@"\[GRADE: [A-Z-]+\]");

        var bullets = new List<(int LineNo, string Text)>();
        for (int i = start; i < end; i++)
            if (Regex.IsMatch(lines[i], @"^> \d\. \*\*(shipped-and-compared|grounded|uncompared as shipped|no sourced)"))
                bullets.Add((i + 1, lines[i]));

        Assert.True(bullets.Count == 4,
            $"§1.3's derivation has {bullets.Count} bullets, not the four the anchor block promises.");

        // Bullets 1 and 2 are the counting ones; 3 and 4 are pure arithmetic over them.
        foreach (var (lineNo, text) in bullets.Take(2))
        {
            var cited = bulletGrades.Matches(text).Select(m => m.Value).ToList();
            Assert.True(cited.Count > 0,
                $"census:{lineNo} counts items by prose, not by a grade token: {text.Trim()}\n"
              + "      A rule that cannot be re-run is how bullet 2 came to point at a header string no item carries.");
            foreach (var g in cited)
                Assert.True(live.Contains(g),
                    $"census:{lineNo} counts \"{g}\", which NO live §1.3 item header carries. Live grades are: "
                  + string.Join(", ", live.OrderBy(x => x, StringComparer.Ordinal)));
        }
    }

    /// <summary>
    /// The two false restatements. Two places outside §1.3 described item 14's header as "GROUNDED, NOT YET
    /// BUILT". That is a false statement about this document's own current contents, and it was false when
    /// written: `git show 96db1c0:docs/full-clone-census.md` shows the header reading "GROUNDED; PARTLY BUILT" on
    /// the day item 14 was created. A struck-through quotation is exempt — the census deliberately keeps
    /// superseded text — an unstruck assertion is not.
    /// </summary>
    [Fact]
    public void No_unstruck_line_describes_item_14_with_a_grade_its_header_does_not_carry()
    {
        var lines = CensusLines();
        var item14 = Items(lines).Single(i => i.Number == 14);

        // Strike the struck spans out FIRST, not the whole line. A line-granularity exemption is not good
        // enough here and was measured to be so: census:1156 carries a struck sentence AND, further along the
        // same line, an unstruck "**item 14**, GROUNDED, NOT YET BUILT" — so skipping any line containing "~~"
        // would have passed the third false restatement while reporting the other two.
        var struckSpan = new Regex("~~.*?~~", RegexOptions.Singleline);

        var offenders = lines
            .Select((text, i) => (text: struckSpan.Replace(text, string.Empty), lineNo: i + 1))
            .Where(x => x.lineNo != item14.LineNo)
            .Where(x => Regex.IsMatch(x.text, @"item \*{0,2}14\*{0,2}", RegexOptions.IgnoreCase))
            .Where(x => x.text.Contains("GROUNDED, NOT YET BUILT", StringComparison.Ordinal))
            .ToList();

        Assert.True(offenders.Count == 0,
            "these lines state that §1.3 item 14 is headed \"GROUNDED, NOT YET BUILT\". Its header has never said "
          + "that — it was written \"GROUNDED; PARTLY BUILT\" in the same commit — so the restatement is false "
          + "about the document's own contents:\n  - "
          + string.Join("\n  - ", offenders.Select(o => $"census:{o.lineNo}: {o.text.Trim()}")));
    }

    /// <summary>
    /// Two opposite instructions inside one item. Item 14 told the next pass both that figure (1) does NOT move
    /// when S3/S4 land ("this header becomes BUILT — and the anchor block still does not move") and that it DOES
    /// ("this header changes from GROUNDED to BUILT and figure (1) moves by one"). Which is right is a ruling-9
    /// question — whether an item every one of whose STRINGS is corpus-silent can ever join the shipped-and-
    /// compared set — and it is not settled by asserting both. The item must state the question once, not answer
    /// it twice in opposite directions.
    /// </summary>
    [Fact]
    public void Item_14_does_not_carry_two_opposite_instructions_for_figure_1()
    {
        var lines = CensusLines();
        var (_, end) = SectionOneThree(lines);
        int itemStart = Items(lines).Single(i => i.Number == 14).LineNo - 1;

        // Struck spans are the census's own way of keeping a superseded sentence checkable, so the lock reads
        // LIVE assertions only — otherwise the item could never record what it used to say.
        var struckSpan = new Regex("~~.*?~~", RegexOptions.Singleline);
        var body = struckSpan.Replace(string.Join("\n", lines[itemStart..end]), string.Empty);

        const string doesNotMove = "the anchor block still does not move";
        const string movesByOne = "figure (1) moves by one";

        bool a = body.Contains(doesNotMove, StringComparison.Ordinal);
        bool b = body.Contains(movesByOne, StringComparison.Ordinal);

        Assert.False(a && b,
            $"§1.3 item 14 asserts BOTH \"{doesNotMove}\" and \"{movesByOne}\" about the same future event, so the "
          + "next pass has two opposite instructions for figure (1). State the open question once instead.");
    }
}
