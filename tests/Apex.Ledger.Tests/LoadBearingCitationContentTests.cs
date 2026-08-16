using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace Apex.Ledger.Tests;

/// <summary>
/// <b>Content-matching for the handful of citations that carry an argument.</b>
///
/// <para><b>Why this exists.</b> <c>DocumentCodeAgreementTests</c> proves a <c>file.ext:NN</c> citation is not
/// dangling and not past EOF. It states its own limit outright: it "CANNOT tell you that <c>Foo.cs:172</c> still
/// points at the method the sentence is about". That gap is not theoretical — the W0-2a review found <b>a dozen
/// drifted print-path citations in <c>docs/w0-2-company-screen-grounding.md</c> and one inside the R12 user gate
/// in <c>plan.md</c></b>, all of them green under the reach check, because the files are long enough that a wrong
/// line is still a valid line. One of them was the single citation demonstrating that the printer ignores
/// <c>Company.State</c> — the evidence the user is being asked to rule on.</para>
///
/// <para><b>What this adds, and deliberately no more.</b> A small table of citations that are <i>load-bearing</i>
/// — each one is the sole evidence for a design ruling or a user gate. For each, the test locates the citation in
/// the document <b>by an adjacent context phrase</b> (never by a hard-coded line number, which would itself
/// drift), reads the line it points at in the CODE, and asserts that line contains a required token. Re-anchor a
/// citation correctly and this test follows it; re-anchor it to the wrong place and this test goes red.</para>
///
/// <para><b>Scope honesty.</b> This covers <see cref="Anchors"/> and nothing else — a few citations out of
/// thousands. It is a targeted guard for the claims that decide behaviour, not general coverage. Citations
/// outside the table remain protected only by the reach check.</para>
/// </summary>
public sealed class LoadBearingCitationContentTests
{
    /// <param name="Document">Repo-relative document carrying the citation.</param>
    /// <param name="ContextPhrase">Text on the SAME document line as the citation — how the citation is found.</param>
    /// <param name="CitedFile">The file name the citation must name (e.g. <c>VoucherPrintProjector.cs</c>).</param>
    /// <param name="RequiredToken">Text that must appear within the cited line range in that file.</param>
    private sealed record Anchor(string Document, string ContextPhrase, string CitedFile, string RequiredToken);

    private static readonly Anchor[] Anchors =
    {
        // ---- the R12 user gate's evidence: the printer reads the GST State, never the postal one ----
        new("plan.md",
            "GST one** — `src/Apex.Desktop/Services/VoucherPrintProjector.cs:726` is",
            "VoucherPrintProjector.cs",
            "StateText(company.Gst?.HomeStateCode)"),

        // ---- the correction that overturned "Company.State goes nowhere": the canonical round-trip carries it ----
        new("plan.md",
            "export–import round-trip**",
            "CanonicalMapper.cs",
            "State = c.State"),
        new("plan.md",
            "(read), `ImportPlan.cs:1198-1199` (assign)",
            "ImportPlan.cs",
            "t.State = c.State"),

        // ---- W0-2a's ER-13 guard: the reason every existing book still prints a blank supplier block ----
        new("plan.md",
            "**▶ The load-bearing guard — `SupplierPostalAddressText`",
            "VoucherPrintProjector.cs",
            "IsNullOrWhiteSpace(company.Address)"),

        // ---- the grounding doc's headline finding, after W0-2a made half of it false ----
        new("docs/w0-2-company-screen-grounding.md",
            "**changed by W0-2a.**",
            "VoucherPrintProjector.cs",
            "SplitAddress(SupplierPostalAddressText(company))"),
        new("docs/w0-2-company-screen-grounding.md",
            "**unchanged; still never `company.State`.**",
            "VoucherPrintProjector.cs",
            "StateText(company.Gst?.HomeStateCode)"),
        new("docs/w0-2-company-screen-grounding.md",
            "the party State rides on",
            "CanonicalXml.cs",
            "No <c>state</c> attribute"),
    };

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

    private static string ResolveCodePath(string fileName)
    {
        // Filter on the REPO-RELATIVE path. Filtering the absolute one excluded every file in this checkout,
        // because the worktree itself lives under a dot-directory (`…/.claude/worktrees/…`).
        var root = RepoRoot();
        var matches = Directory
            .EnumerateFiles(root, fileName, SearchOption.AllDirectories)
            .Where(p =>
            {
                var rel = Path.GetRelativePath(root, p).Replace('\\', '/');
                return !rel.Split('/').Any(seg => seg is "bin" or "obj" or "node_modules" || seg.StartsWith('.'));
            })
            .ToList();

        Assert.True(matches.Count == 1,
            $"'{fileName}' resolves to {matches.Count} files; the anchor table needs an unambiguous name.");
        return matches[0];
    }

    [Fact]
    public void Every_load_bearing_citation_points_at_a_line_that_still_says_what_the_document_claims()
    {
        var failures = new List<string>();

        foreach (var a in Anchors)
        {
            var docLines = ReadLines(a.Document);

            // 1. Find the document line carrying the context phrase.
            var hits = docLines
                .Select((text, i) => (text, lineNo: i + 1))
                .Where(x => x.text.Contains(a.ContextPhrase, StringComparison.Ordinal))
                .ToList();

            if (hits.Count == 0)
            {
                failures.Add($"{a.Document}: context phrase not found — \"{a.ContextPhrase}\". "
                           + "The prose was reworded; update the anchor table so the guard keeps biting.");
                continue;
            }

            // 2. Extract the citation to CitedFile from that line (or the line before, for wrapped prose).
            var escaped = Regex.Escape(a.CitedFile);
            var rx = new Regex(escaped + @":(\d+)(?:-(\d+))?");

            Match? m = null;
            foreach (var hit in hits)
            {
                foreach (var candidate in new[] { hit.text, hit.lineNo >= 2 ? docLines[hit.lineNo - 2] : "" })
                {
                    var found = rx.Match(candidate);
                    if (found.Success) { m = found; break; }
                }
                if (m is not null) break;
            }

            if (m is null)
            {
                failures.Add($"{a.Document}: found \"{a.ContextPhrase}\" but no `{a.CitedFile}:NN` citation on "
                           + "that line or the one above it. The citation was removed or renamed.");
                continue;
            }

            var start = int.Parse(m.Groups[1].Value);
            var end = m.Groups[2].Success ? int.Parse(m.Groups[2].Value) : start;

            // 3. The cited range in the CODE must contain the required token.
            var codeLines = File.ReadAllLines(ResolveCodePath(a.CitedFile));
            if (start < 1 || end > codeLines.Length)
            {
                failures.Add($"{a.Document} → {a.CitedFile}:{start}-{end} is outside the file (1-{codeLines.Length}).");
                continue;
            }

            var slice = string.Join("\n", codeLines[(start - 1)..end]);
            if (!slice.Contains(a.RequiredToken, StringComparison.Ordinal))
            {
                failures.Add(
                    $"{a.Document} cites {a.CitedFile}:{start}"
                  + (end == start ? "" : $"-{end}")
                  + $" for \"{a.ContextPhrase}\", but that line does NOT contain \"{a.RequiredToken}\".\n"
                  + $"      It actually reads: {codeLines[start - 1].Trim()}");
            }
        }

        Assert.True(failures.Count == 0,
            $"{failures.Count} load-bearing citation(s) have drifted off the code they describe. These are the "
          + "citations that are the sole evidence for a design ruling or a user gate, so a wrong one misleads a "
          + "decision rather than merely annoying a reader:\n  - " + string.Join("\n  - ", failures));
    }

    /// <summary>
    /// Non-vacuity: the table is non-empty, every document in it exists, and every context phrase is actually
    /// present. Without this, deleting a row (or a typo in a phrase) would silently reduce the guard to nothing
    /// while staying green.
    /// </summary>
    [Fact]
    public void The_anchor_table_is_non_empty_and_every_row_resolves()
    {
        Assert.True(Anchors.Length >= 7, $"anchor table has shrunk to {Anchors.Length} rows");

        foreach (var a in Anchors)
        {
            var docPath = Path.Combine(RepoRoot(), a.Document.Replace('/', Path.DirectorySeparatorChar));
            Assert.True(File.Exists(docPath), $"{a.Document} does not exist");
            Assert.Contains(a.ContextPhrase, File.ReadAllText(docPath), StringComparison.Ordinal);
            Assert.True(File.Exists(ResolveCodePath(a.CitedFile)), $"{a.CitedFile} does not resolve");
        }
    }
}
