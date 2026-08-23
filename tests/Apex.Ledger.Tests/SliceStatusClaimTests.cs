using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace Apex.Ledger.Tests;

/// <summary>
/// <b>T0-11 review C21/L3-07 and C25/L3-11 — status claims, anchored so they cannot outlive the truth.</b>
///
/// <para><b>Why prose was not enough.</b> The review's own root-cause reading of five separate findings is one
/// sentence: <i>"this project's documentation carries STATUS in prose that append-only amendments never
/// revisit"</i>. Two of them are here. <c>ADR-0002</c>'s Status line, written at slice S0 as a forward-looking
/// allocation, says <i>"S1–S4 implement it"</i> and flags only S5 as deferred — while S3 and S4 have not shipped
/// and the ADR nowhere says so; the ADR meanwhile grew dated completion amendments, which turn it into a
/// status-carrying document that a reader takes at its word. And <c>plan.md</c> stamped slice S2 <c>DONE</c>
/// while, 100-odd lines lower inside the SAME phase block, still recording that S2 is <b>BLOCKED</b> on an open
/// R12 question — so a reader trusting the stamp believes the user ruled, and a reader trusting the block
/// believes the tax treatment is still open to reversal. Both cannot be right and neither reader is at fault.</para>
///
/// <para><b>What this asserts.</b> Two agreements, each between two documents that already existed and had
/// silently diverged: ADR-0002's per-slice status ledger against <c>plan.md</c>'s completion stamps, and
/// <c>plan.md</c>'s own DONE stamps against its own open-question block. Neither is a new claim; each is the
/// mechanised form of a claim the documents were making anyway.</para>
///
/// <para><b>Scope honesty.</b> This covers the T0-11 chain (Phase 10.13 / ADR-0002) and nothing else. It is the
/// same shape as <see cref="LoadBearingCitationContentTests"/> — a targeted guard on the claims that decide
/// whether a gate has been passed, not general coverage of every status sentence in the repository.</para>
/// </summary>
public sealed class SliceStatusClaimTests
{
    private const string Plan = "plan.md";
    private const string Adr = "docs/adr/0002-printed-document-three-axis-split.md";
    private const string PhaseHeadingPrefix = "### Phase 10.13 ";

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Apex.slnx")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }

    private static string[] ReadLines(string relative) =>
        File.ReadAllLines(Path.Combine(RepoRoot(), relative.Replace('/', Path.DirectorySeparatorChar)));

    /// <summary>Phase 10.13 runs from its own heading to the next "### " heading — proved by scan, never by eye:
    /// the DONE stamp and the open-questions block that contradicted it are both INSIDE this one block.</summary>
    private static (string[] Lines, int Start, int End) PhaseBlock()
    {
        var lines = ReadLines(Plan);
        int start = Array.FindIndex(lines, l => l.StartsWith(PhaseHeadingPrefix, StringComparison.Ordinal));
        Assert.True(start >= 0, $"{Plan} has no `{PhaseHeadingPrefix}` heading.");
        int end = Array.FindIndex(lines, start + 1, l => l.StartsWith("### ", StringComparison.Ordinal));
        Assert.True(end > start, "Phase 10.13 is not bounded by a following `### ` heading.");
        return (lines, start, end);
    }

    /// <summary>The bullet that opens a slice, e.g. "  - **S2 — ...". Its own line plus the next few carry the
    /// completion stamp, which is what a reader reads as the slice's status.</summary>
    private static (int LineNo, string Text)? SliceBullet(string[] lines, int start, int end, string slice)
    {
        var rx = new Regex(@"^\s*-\s+\*\*" + Regex.Escape(slice) + @"\b");
        for (int i = start; i < end; i++)
        {
            if (!rx.IsMatch(lines[i])) continue;
            int stop = Math.Min(end, i + 4);
            return (i + 1, string.Join("\n", lines[i..stop]));
        }
        return null;
    }

    private static bool CarriesDoneStamp(string bulletText) =>
        bulletText.Contains("✅ DONE", StringComparison.Ordinal);

    // ================================================================ C25 / L3-11

    /// <summary>
    /// ADR-0002 is the document <c>plan.md</c> makes mandatory reading before touching any file in the phase, and
    /// the one an outside reviewer opens first. Its Status line must not put a slice on the implemented side while
    /// <c>plan.md</c> carries no completion stamp for it. The ADR now states its slice status as a per-slice
    /// ledger rather than as a forward-looking range, and this reads that ledger and checks every entry against
    /// <c>plan.md</c> — in both directions, so an over-claim and an under-claim are equally red.
    /// </summary>
    [Fact]
    public void The_ADRs_slice_status_ledger_agrees_with_plan_mds_completion_stamps()
    {
        var adr = ReadLines(Adr);
        int at = Array.FindIndex(adr,
            l => l.Contains("Slice status (machine-checked against", StringComparison.Ordinal));

        // The ledger is one bullet, wrapped across several lines like every other bullet in this file, so read
        // the whole bullet — to the next top-level bullet — rather than the line the phrase happens to land on.
        int stop = at < 0 ? 0 : at + 1;
        while (at >= 0 && stop < adr.Length && !adr[stop].StartsWith("- ", StringComparison.Ordinal)) stop++;

        var ledgerLine = at < 0
            ? (text: (string?)null, lineNo: 0)
            : (text: (string?)string.Join(" ", adr[at..stop]), lineNo: at + 1);

        Assert.True(ledgerLine.text is not null,
            $"{Adr} carries no machine-checked slice-status ledger. Its Status line was a forward-looking S0-era "
          + "allocation (\"S1-S4 implement it\") that no amendment ever revised, so it kept naming unbuilt slices "
          + "as implemented. Restore a ledger line of the form: "
          + "`- **Slice status (machine-checked against plan.md Phase 10.13):** S0 SHIPPED / S1 SHIPPED / ...`");

        // Each entry reads `S<n> <STATE>`. Four states, and only SHIPPED means the phase gate closed: CODE-
        // COMPLETE is the honest grade for a slice whose code is built and whose governing R12 question is still
        // outstanding, which is exactly what S2 was while it carried a green tick.
        var entries = Regex.Matches(ledgerLine.text!, @"\bS(\d)\s+(SHIPPED|CODE-COMPLETE|NOT-YET-BUILT|DEFERRED)\b")
            .Select(m => (Slice: "S" + m.Groups[1].Value, State: m.Groups[2].Value))
            .ToList();

        Assert.True(entries.Count >= 6,
            $"{Adr}:{ledgerLine.lineNo} names only {entries.Count} slices; the chain is S0-S5.\n"
          + "      Line reads: " + ledgerLine.text!.Trim());

        var (plan, start, end) = PhaseBlock();
        var failures = new List<string>();

        foreach (var (slice, state) in entries)
        {
            var bullet = SliceBullet(plan, start, end, slice);
            if (bullet is null)
            {
                failures.Add($"{Adr} grades {slice} as {state}, but Phase 10.13 in {Plan} has no bullet for it.");
                continue;
            }

            bool stamped = CarriesDoneStamp(bullet.Value.Text);
            if (state == "SHIPPED" && !stamped)
                failures.Add($"{Adr} puts {slice} on the implemented side, but {Plan}:{bullet.Value.LineNo} "
                           + "carries no completion stamp for it — the ADR is claiming work that has not shipped.");
            if (state != "SHIPPED" && stamped)
                failures.Add($"{Adr} grades {slice} as {state}, but {Plan}:{bullet.Value.LineNo} stamps it done — "
                           + "the ADR is now the stale one.");
        }

        Assert.True(failures.Count == 0,
            "ADR-0002's slice status and plan.md's completion stamps disagree:\n  - " + string.Join("\n  - ", failures));
    }

    // ================================================================ C21 / L3-07

    /// <summary>
    /// A slice may not be stamped done while the phase's own block still records it as BLOCKED on an R12 question
    /// the user has not answered. This is CLAUDE.md R9 (the phase gate) and R12 (decisions go to the user)
    /// expressed as an assertion: the DONE stamp is written by the implementer from inside the slice, with no step
    /// that re-reads the open-questions block, and that is exactly how S2 came to ship the recommendation to an
    /// unanswered question under a green tick.
    ///
    /// <para>The block is allowed to say a slice is blocked and the slice is allowed to be code-complete — what is
    /// forbidden is the unqualified DONE stamp over the open gate. A question that has been RULED carries its
    /// ruling in the block, and then the slice may be stamped.</para>
    /// </summary>
    [Fact]
    public void No_slice_is_stamped_done_while_the_phase_still_records_it_as_blocked_on_an_open_question()
    {
        var (lines, start, end) = PhaseBlock();

        var blockedLines = new List<(int LineNo, string Text, List<string> Slices)>();
        for (int i = start; i < end; i++)
        {
            if (!Regex.IsMatch(lines[i], @"\b(IS|ARE) BLOCKED ON\b")) continue;
            var slices = Regex.Matches(lines[i], @"\bS(\d)\b").Select(m => "S" + m.Groups[1].Value).Distinct().ToList();
            blockedLines.Add((i + 1, lines[i], slices));
        }

        Assert.True(blockedLines.Count > 0,
            "Phase 10.13 no longer records ANY slice as blocked on an open R12 question. If the ruling has landed, "
          + "this test's subject is gone and it should be retired deliberately rather than by deletion of the "
          + "block it reads — the open-questions block is the only thing that tells a reader a gate exists.");

        var failures = new List<string>();
        foreach (var (lineNo, text, slices) in blockedLines)
        {
            foreach (var slice in slices)
            {
                var bullet = SliceBullet(lines, start, end, slice);
                if (bullet is null) continue;
                if (!CarriesDoneStamp(bullet.Value.Text)) continue;

                failures.Add(
                    $"{Plan}:{bullet.Value.LineNo} stamps {slice} DONE, while {Plan}:{lineNo} still records it as "
                  + "blocked on an unanswered R12 question:\n        " + text.Trim());
            }
        }

        Assert.True(failures.Count == 0,
            "a phase gate was stamped closed over an open user decision (CLAUDE.md R9 + R12):\n  - "
          + string.Join("\n  - ", failures));
    }

    /// <summary>
    /// Non-vacuity, and the other half of the property: the slices that are NOT blocked must still carry their
    /// stamps. Without this, the test above is satisfied by deleting every stamp in the phase, which would trade
    /// an over-claim for a document that says nothing.
    /// </summary>
    [Fact]
    public void The_slices_that_are_not_blocked_still_carry_their_completion_stamps()
    {
        var (lines, start, end) = PhaseBlock();

        foreach (var slice in new[] { "S0", "S1" })
        {
            var bullet = SliceBullet(lines, start, end, slice);
            Assert.True(bullet is not null, $"Phase 10.13 has no bullet for {slice}.");
            Assert.True(CarriesDoneStamp(bullet!.Value.Text),
                $"{slice} shipped and is not blocked on anything, but {Plan}:{bullet.Value.LineNo} carries no "
              + "completion stamp — the correction to S2 has been applied too widely.");
        }
    }

    /// <summary>
    /// The open question must be recorded as ASKED, with a date, rather than merely listed. C21's aggravator was
    /// that the closure notice concealed the bypass: the S2 block argued its design from law and machinery and
    /// never disclosed that a user gate was outstanding, so a reader of the DONE block could not learn a gate had
    /// been skipped. A question that is only "open" reads as one nobody has got to yet; a question that is ASKED
    /// AND OUTSTANDING names a specific thing the project is waiting on.
    /// </summary>
    [Fact]
    public void The_open_R12_question_records_that_it_was_actually_put_to_the_user()
    {
        var (lines, start, end) = PhaseBlock();
        var block = string.Join("\n", lines[start..end]);

        Assert.True(Regex.IsMatch(block, @"ASKED AND OUTSTANDING"),
            "Phase 10.13's open-questions block does not record that question (1) was PUT to the user. R12 is not "
          + "satisfied by listing a question; the record has to say it was asked and is awaiting a ruling, or a "
          + "later reader cannot tell an unasked question from an unanswered one.");

        Assert.True(Regex.IsMatch(block, @"ASKED AND OUTSTANDING[^\n]*20\d\d-\d\d-\d\d|20\d\d-\d\d-\d\d[^\n]*ASKED AND OUTSTANDING"),
            "the ASKED AND OUTSTANDING marker carries no date. Every other status claim in this phase is dated; an "
          + "undated one cannot be aged.");
    }
}
