using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Apex.Ledger.Domain;
using Apex.Ledger.Seed;
using Xunit;

namespace Apex.Ledger.Tests;

/// <summary>
/// <b>Doc-vs-code agreement.</b> Until this file existed, <b>no test in this repository read a <c>.md</c>
/// file</b> — which is the structural reason three register rows (invented-vs-cloned IV-9 and IV-20(a),
/// tally-fidelity-defects D7) came to state the OPPOSITE of the code, and why three fixes and the register
/// describing them shipped on the SAME DAY (2026-08-06) with neither noticing. Review was the only instrument
/// that caught it.
///
/// <para><b>WHAT THIS COVERS — three invariants, and nothing else.</b></para>
/// <list type="number">
/// <item><b>Invariant 1 — every explicit <c>file.ext:NN</c> citation resolves.</b> The cited file exists, it
/// resolves to exactly ONE file in the tree, and every line number it names (both endpoints of a
/// <c>:NN-MM</c> range) is inside that file.</item>
/// <item><b>Invariant 2 — the seed tables in <c>docs/design/accounting-core.md</c> §5.1/§5.2/§5.3 match the
/// seed code row-for-row</b>, in order, by name / nature / parent / alias / base type / shortcut / numbering
/// / active flag.</item>
/// <item><b>Invariant 3 — the seed COUNT claims stated in prose anywhere in the document set</b> ("N
/// predefined voucher types", "N predefined groups", "N default ledgers") equal
/// <see cref="SeedVoucherTypes.Count"/> / <see cref="SeedGroups.Count"/> / <see cref="SeedLedgers.Count"/>.</item>
/// </list>
///
/// <para><b>WHAT THIS CANNOT COVER — read this before trusting a green run.</b></para>
/// <list type="bullet">
/// <item><b>Invariant 1 does not read the cited line.</b> It proves the citation is not dangling and not past
/// the end of its file. It CANNOT tell you that <c>Foo.cs:172</c> still points at the method the sentence is
/// about — a citation that has merely drifted a few lines inside a long file passes. It is a reach check, not
/// a content check.</item>
/// <item><b>Bare <c>:NNN</c> continuation citations are NOT covered — 2,052 of them at the time of writing,
/// against the 1,738 explicit citations (2,429 line anchors) that ARE covered.</b> The corpus writes
/// "<c>`A.cs:47`… and `:96`", meaning line
/// 96 OF A.cs. Binding those requires inferring which file each continues, and BOTH inference rules I built
/// mis-bound on this corpus: "nearest preceding file citation on the line" mis-binds wherever the intended
/// anchor is an unlinked type name (<c>invented-vs-cloned.md:295</c>, where <c>:2124-2135</c> means
/// MainWindowViewModel.cs but the last file named is VoucherDetailViewModel.cs), and adding a type-name anchor
/// rule produced 45 fresh mis-bindings off type names merely mentioned in prose. Rather than relax a rule
/// until it passed, the class is EXCLUDED and counted. <b>Roughly 46% of the line anchors in these documents
/// are unmeasured.</b></item>
/// <item><b>Invariant 3's claim vocabulary is finite and listed in code below.</b> Counts phrased any other
/// way are not seen. Schema-version claims ("schema v37") are deliberately NOT checked: <c>memory.md</c> is a
/// historical log and most of those are true statements about a past version.</item>
/// <item><b>Only <c>*.md</c> is scanned.</b> False claims living in C# doc comments — e.g.
/// <c>VoucherType.cs</c>'s "24 are seeded" — are outside this test's reach entirely.</item>
/// <item><b>Invariant 2 compares a column only where the table declares one</b> — and a column the table DOES
/// declare can no longer be switched off by a rename; every absent column except those named in
/// <see cref="MissingColumnOptOut"/> is now a FAILURE. <b>Why that guard exists (review finding):</b> the comparer
/// skipped a column it could not find among a row's header keys, silently and per row. Measured: renaming §5.3's
/// <c>Shortcut</c> header to <c>Key</c> AND corrupting row 1's shortcut to <c>F99</c> left this file 8/8 GREEN,
/// where the same corruption under the correct header failed. One word disabled the column for all 16 rows, and
/// the row-count assertions could not see it because the row count was unchanged. The genuinely absent columns are
/// §5.3's core <c>IsActive default</c> — so the active flag is pinned for the 7 additional types and not for the
/// 16 core ones — and §5.2's "Under (Group)", which is prose in one of its two rows and is not compared at all.</item>
/// <item><b>The count allow-list exempts a PHRASE PER DOCUMENT, with an expected number of occurrences</b> — not a
/// site. A wrong count written into a document that already carries a justified quotation of the same phrase is
/// caught (the count goes up), but swapping one violating occurrence for another inside the same document is not.
/// Keying on line numbers was rejected: they drift on every edit. See <see cref="ForeignOrQuotedCountAllowList"/>.</item>
/// </list>
///
/// <para><b>Non-vacuity is asserted, not assumed.</b> <see cref="TheScanActuallyReadsTheGovernedDocuments"/>
/// pins floors on documents/citations/anchors actually seen, and each invariant has a bite proof
/// (<see cref="TheCitationScannerBitesOnASyntheticDangler"/>, <see cref="TheSeedTableComparerBitesOnEachMutation"/>,
/// <see cref="TheCountScannerBitesOnAWrongNumber"/>) plus a false-positive guard over verbatim shipped lines
/// that must NOT be flagged.</para>
/// </summary>
public sealed class DocumentCodeAgreementTests
{
    // =============================================================================================
    // MACHINERY
    // =============================================================================================

    /// <summary>The repository root — the directory holding <c>Apex.slnx</c>.</summary>
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Apex.slnx")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }

    /// <summary>
    /// THE GOVERNED DOCUMENT SET: every <c>*.md</c> under the repository root, excluding <c>bin</c>,
    /// <c>obj</c>, <c>node_modules</c> and every directory whose name starts with a dot. That is
    /// <c>CLAUDE.md</c>, <c>agents.md</c>, <c>memory.md</c>, <c>plan.md</c>, <c>README.md</c>,
    /// <c>CONTRIBUTING.md</c>, all of <c>docs/**</c> and <c>tools/HeadOracle/README.md</c> — 30 files.
    /// <b>NOT covered:</b> the three <c>.github/</c> issue and PR templates (they carry no citations and no
    /// counts), and anything under a dot-directory such as a nested worktree.
    /// </summary>
    private static IReadOnlyList<string> GovernedDocuments() => EnumerateRelative("*.md");

    /// <summary>Every file in the tree, repo-relative, forward-slashed — the citation resolution index.</summary>
    private static readonly Lazy<IReadOnlyList<string>> AllFiles =
        new(() => EnumerateRelative("*"));

    private static IReadOnlyList<string> EnumerateRelative(string pattern)
    {
        var root = RepoRoot();
        var results = new List<string>();
        var stack = new Stack<string>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            var dir = stack.Pop();
            foreach (var sub in Directory.EnumerateDirectories(dir))
            {
                var name = Path.GetFileName(sub);
                if (name.StartsWith('.') || name is "bin" or "obj" or "node_modules") continue;
                stack.Push(sub);
            }
            foreach (var file in Directory.EnumerateFiles(dir, pattern))
                results.Add(Path.GetRelativePath(root, file).Replace('\\', '/'));
        }
        results.Sort(StringComparer.Ordinal);
        return results;
    }

    private static readonly Dictionary<string, int> LineCounts = new(StringComparer.Ordinal);

    /// <summary>Physical line count of a repo-relative file (a trailing newline does not add a line).</summary>
    private static int LineCount(string relative)
    {
        if (LineCounts.TryGetValue(relative, out var cached)) return cached;
        var text = File.ReadAllText(Path.Combine(RepoRoot(), relative.Replace('/', Path.DirectorySeparatorChar)));
        var count = text.Count(c => c == '\n');
        if (text.Length > 0 && !text.EndsWith('\n')) count++;
        LineCounts[relative] = count;
        return count;
    }

    private static void AssertNoStaleAllowListEntries(
        IEnumerable<string> allowList, IEnumerable<string> stillViolating, string listName)
    {
        var stale = allowList.Except(stillViolating, StringComparer.Ordinal)
                             .OrderBy(s => s, StringComparer.Ordinal).ToList();
        Assert.True(stale.Count == 0,
            $"{listName}: {stale.Count} allow-list entr{(stale.Count == 1 ? "y is" : "ies are")} STALE — the "
            + "document was corrected (or the line moved) but the exemption was left behind, which would "
            + "silently re-permit the defect. Re-judge and DELETE these entries:\n  "
            + string.Join("\n  ", stale));
    }

    // =============================================================================================
    // INVARIANT 1 — every explicit file:line citation resolves.
    // =============================================================================================
    //
    // THE MECHANISM. These documents cite code by `path/Name.cs:NN` (and `:NN-MM`). Code moves; files get
    // renamed and projects get renamed. Nothing linked the two, so a citation could point at a file that no
    // longer exists (`src/Apex.Ledger.Sqlite/Schema.cs` — that project is `Apex.Persistence.Sqlite` and always
    // was) or 9 lines past the end of a 162-line file, and stay that way indefinitely.

    /// <summary>A file:line citation lifted out of a document.</summary>
    private sealed record Citation(string Document, int DocumentLine, string Raw, string CitedPath,
                                   int FirstLine, int? LastLine)
    {
        /// <summary>Allow-list key: the document plus the citation text, NOT the line number (which drifts).</summary>
        public string Key => $"{Document}|{Raw}";
    }

    /// <summary>
    /// `some/path/Name.ext:NN` or `Name.ext:NN-MM`. Deliberately anchored on the extension, so prose colons,
    /// clock times and version numbers cannot match.
    /// </summary>
    private static readonly Regex CitationPattern = new(
        @"([A-Za-z0-9_][A-Za-z0-9_./\\-]*\.(?:cs|axaml|csproj|slnx|md)):(\d+)(?:\s*[-–]\s*(\d+))?",
        RegexOptions.Compiled);

    private static IEnumerable<Citation> ScanCitations(string document, IEnumerable<string> lines)
    {
        var n = 0;
        foreach (var line in lines)
        {
            n++;
            foreach (Match m in CitationPattern.Matches(line))
            {
                var last = m.Groups[3].Success
                    ? int.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture)
                    : (int?)null;
                yield return new Citation(document, n, m.Value, m.Groups[1].Value.Replace('\\', '/'),
                    int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture), last);
            }
        }
    }

    /// <summary>
    /// Resolves a cited path against the tree: an exact repo-relative hit, else a UNIQUE path-suffix hit
    /// (the corpus routinely writes `Domain/GstConfig.cs` or the bare `GstService.cs`). Returns every
    /// candidate, so the caller can fail an AMBIGUOUS citation rather than silently pick one.
    /// </summary>
    private static IReadOnlyList<string> ResolveCitedPath(string cited)
    {
        if (AllFiles.Value.Contains(cited, StringComparer.Ordinal)) return new[] { cited };
        var suffix = "/" + cited;
        return AllFiles.Value.Where(f => f.EndsWith(suffix, StringComparison.Ordinal)).ToList();
    }

    private static string? CitationViolation(Citation c)
    {
        var candidates = ResolveCitedPath(c.CitedPath);
        if (candidates.Count == 0)
            return $"NO SUCH FILE — nothing in the tree is or ends with '{c.CitedPath}'";
        if (candidates.Count > 1)
            return "AMBIGUOUS — resolves to " + candidates.Count + " files (" + string.Join(", ", candidates)
                 + "); write enough of the path to disambiguate";
        var target = candidates[0];
        var total = LineCount(target);
        foreach (var line in new[] { c.FirstLine, c.LastLine }.Where(x => x is not null).Select(x => x!.Value))
            if (line < 1 || line > total)
                return $"PAST EOF — cites line {line} but {target} has {total} lines";
        return null;
    }

    /// <summary>
    /// KNOWN-STALE citations that are DELIBERATE and must stay, keyed by "document|citation text" (never by
    /// line number — those drift). Each entry names why the stale text is load-bearing. This list may only
    /// SHRINK for any reason other than a new, justified quote-to-correct.
    /// </summary>
    private static readonly HashSet<string> HistoricalCitationAllowList = new(StringComparer.Ordinal)
    {
        // The register's own correction blocks QUOTE the stale citation in order to condemn it — the document
        // header's rule is "Nothing was silently rewritten — the original claim is always quoted beside the
        // correction". Removing the quote would destroy the correction's evidence. The entry below sits inside a
        // block that states, in the same sentence, the true location and the file's real length.
        //
        // 🔴 DELETED 2026-08-17 (Phase 10.11 S4): "docs/invented-vs-cloned.md|LedgerService.cs:171".
        // The exemption existed because the quoted-to-condemn citation was PAST END OF FILE — LedgerService.cs was
        // 162 lines. S4 amended that file's Delete/NextNumber doc comments (Delete described itself as "may leave a
        // gap in numbering", which misses the number-REUSE case S4 makes reachable), taking it to 177 lines, so
        // :171 now RESOLVES and the exemption became STALE — which this test detected and demanded be re-judged.
        // Re-judged: DELETED, because the list may only shrink and the citation no longer dangles. The register's
        // quoted claim is untouched; a dated ††-block beside it records that its own line numbers moved and that
        // the file-length sentence is no longer true of HEAD. If a later edit shortens the file below 171 lines the
        // citation dangles again and this test fails loudly, which is the correct outcome rather than a silent
        // re-permit.
        "docs/invented-vs-cloned.md|InterestParameters.cs:225-231",  // IV-…§5.4 🔴-block: "is 117 lines long,
                                                                     // so the range is past EOF" — the claim is
                                                                     // marked UNSUPPORTED, not repaired, because
                                                                     // nobody has re-located what it meant.
    };

    [Fact]
    public void Every_file_line_citation_in_the_governed_documents_resolves()
    {
        var citations = GovernedDocuments()
            .SelectMany(d => ScanCitations(d, File.ReadLines(Path.Combine(RepoRoot(),
                d.Replace('/', Path.DirectorySeparatorChar)))))
            .ToList();

        var offenders = citations
            .Select(c => (Citation: c, Problem: CitationViolation(c)))
            .Where(x => x.Problem is not null)
            .ToList();

        var violations = offenders.Where(x => !HistoricalCitationAllowList.Contains(x.Citation.Key)).ToList();

        Assert.True(violations.Count == 0,
            $"{violations.Count} file:line citation(s) in the governed documents do not resolve. Either the "
            + "citation drifted (repair it against HEAD) or the claim around it is stale (repair the claim). "
            + "If the stale text is deliberately quoted in order to correct it, add a justified entry to "
            + nameof(HistoricalCitationAllowList) + " — do NOT loosen the pattern:\n"
            + string.Join("\n", violations
                .OrderBy(x => x.Citation.Document, StringComparer.Ordinal)
                .ThenBy(x => x.Citation.DocumentLine)
                .Select(x => $"  {x.Citation.Document}:{x.Citation.DocumentLine} cites "
                           + $"\"{x.Citation.Raw}\" -> {x.Problem}")));

        AssertNoStaleAllowListEntries(HistoricalCitationAllowList, offenders.Select(x => x.Citation.Key),
            nameof(HistoricalCitationAllowList));
    }

    // =============================================================================================
    // INVARIANT 2 — the design document's seed tables ARE the seed code.
    // =============================================================================================
    //
    // THE MECHANISM. docs/design/accounting-core.md §5.1/§5.2/§5.3 spell the seed out row by row. They are the
    // only machine-readable statement of the seed anywhere in the document set, and they are what a reader
    // consults instead of opening SeedVoucherTypes.cs. When the Attendance row was deleted from the seed
    // (decision D24-B) and Physical Stock's shortcut was moved off F10 (decision X1), §5.3 did not move, so
    // the document has been describing a seed that has not existed for months.

    private const string SeedDesignDocument = "docs/design/accounting-core.md";

    /// <summary>Strips markdown emphasis/code marks so a cell compares as its plain text.</summary>
    private static readonly Regex MarkdownMarks = new(@"[*_`]", RegexOptions.Compiled);

    private static string Plain(string cell) => MarkdownMarks.Replace(cell, string.Empty).Trim();

    /// <summary>An em/en dash alone means "none" in these tables.</summary>
    private static string? NoneIfDash(string cell) =>
        cell is "—" or "–" or "-" or "" ? null : cell;

    /// <summary>
    /// Every DATA row of every markdown table inside a <c>### N.N</c> section, as header-keyed cells. A header
    /// row is the row immediately above a <c>|---|</c> separator; everything else that starts with a pipe is
    /// data, keyed by the most recent header.
    /// </summary>
    private static List<Dictionary<string, string>> SectionTableRows(IReadOnlyList<string> docLines, string heading) =>
        SectionTables(docLines, heading).Rows;

    /// <summary>
    /// The data rows of a section's tables, <b>plus every HEADER row the section declared</b>. The headers are what
    /// <see cref="The_seed_tables_in_the_design_document_match_the_seed_code"/> checks the requested columns against:
    /// without them, a renamed or mis-typed header simply produced rows lacking that key and
    /// <see cref="CompareSeedTable"/>'s <c>TryGetValue … continue</c> skipped the column for every row, with no
    /// diagnostic and no change in row count.
    /// </summary>
    private static (List<Dictionary<string, string>> Rows, List<string[]> Headers) SectionTables(
        IReadOnlyList<string> docLines, string heading)
    {
        var start = docLines.ToList().FindIndex(l => l.StartsWith(heading, StringComparison.Ordinal));
        Assert.True(start >= 0, $"{SeedDesignDocument}: section '{heading}' has vanished — this invariant "
                              + "would be vacuous. Restore the heading or retire the test deliberately.");
        var end = docLines.Skip(start + 1).ToList().FindIndex(l => l.StartsWith("### ", StringComparison.Ordinal));
        end = end < 0 ? docLines.Count : start + 1 + end;

        var rows = new List<Dictionary<string, string>>();
        var headers = new List<string[]>();
        string[]? header = null;
        for (var i = start; i < end; i++)
        {
            var line = docLines[i].Trim();
            if (!line.StartsWith('|')) { header = null; continue; }
            var cells = line.Trim('|').Split('|').Select(Plain).ToArray();
            if (cells.All(c => c.Length > 0 && c.All(ch => ch is '-' or ':'))) continue;   // separator
            var next = i + 1 < end ? docLines[i + 1].Trim() : string.Empty;
            var nextIsSeparator = next.StartsWith('|')
                && next.Trim('|').Split('|').Select(Plain).All(c => c.Length > 0 && c.All(ch => ch is '-' or ':'));
            if (nextIsSeparator) { header = cells; headers.Add(cells); continue; }
            if (header is null) continue;
            var row = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (var c = 0; c < cells.Length && c < header.Length; c++) row[header[c]] = cells[c];
            rows.Add(row);
        }
        return (rows, headers);
    }

    /// <summary>
    /// The <b>only</b> columns a section's rows are permitted not to carry, keyed "section|column" and carrying
    /// <b>exactly how many rows may lack it</b>. Each is a column the design document deliberately omits, not one it
    /// renamed. This is the exemption surface for <see cref="AssertEveryComparedColumnIsDeclared"/> and may only
    /// SHRINK; a column that goes missing anywhere else — a rename, a typo, a table restructure — is a failure,
    /// because <see cref="CompareSeedTable"/> then silently stops comparing it for those rows.
    /// <para><b>Why a COUNT and not a flag.</b> A section can hold several tables. §5.3 holds two, and a
    /// section-wide "the column exists somewhere" check passes while the column is missing from every row of one of
    /// them — measured: renaming the CORE table's <c>Shortcut</c> header to <c>Key</c> left such a check green,
    /// because the "additional types" table below still declared <c>Shortcut</c>. The count pins WHICH table omits
    /// it.</para>
    /// </summary>
    private static readonly Dictionary<string, int> MissingColumnOptOut = new(StringComparer.Ordinal)
    {
        // §5.3's 16-row CORE table states #/name/base type/shortcut/numbering only; the active flag appears solely in
        // the 7-row "additional types" table below it, where it IS compared. So exactly 16 rows may lack it.
        ["§5.3 voucher types|IsActive default"] = 16,
    };

    /// <summary>
    /// Every column <see cref="CompareSeedTable"/> is about to compare must be carried by every parsed ROW of its
    /// section, except for the exact number of rows <see cref="MissingColumnOptOut"/> accounts for — otherwise the
    /// comparison for those rows is a no-op and the document can say anything it likes in that column. Failures print
    /// the parsed headers, so the diagnostic points straight at the rename.
    /// </summary>
    private static void AssertEveryComparedColumnIsDeclared(
        string section, IReadOnlyList<Dictionary<string, string>> rows, IReadOnlyList<string[]> headers,
        IReadOnlyList<string> columns)
    {
        var undeclared = columns
            .Select(c => (Column: c, Missing: rows.Count(r => !r.ContainsKey(c))))
            .Where(x => x.Missing != (MissingColumnOptOut.TryGetValue($"{section}|{x.Column}", out var n) ? n : 0))
            .ToList();

        Assert.True(undeclared.Count == 0,
            $"{section}: {undeclared.Count} column(s) this test compares are carried by fewer rows than expected, so "
            + "the comparison for those rows is silently a no-op — which is how a one-word header rename switches a "
            + "column off without changing the row count. "
            + string.Join("; ", undeclared.Select(x =>
                $"'{x.Column}' missing from {x.Missing} of {rows.Count} row(s), expected "
                + (MissingColumnOptOut.TryGetValue($"{section}|{x.Column}", out var n) ? n : 0)))
            + ".\nParsed headers: "
            + string.Join(" ; ", headers.Select(h => "| " + string.Join(" | ", h) + " |"))
            + $"\nRepair the header, or — if the column is genuinely absent from the document by design — set "
            + $"\"{section}|<column>\" in {nameof(MissingColumnOptOut)} to the exact row count, with the reason.");
    }

    /// <summary>Compares the doc rows to the code rows and returns one message per disagreement.</summary>
    private static List<string> CompareSeedTable(
        string section, IReadOnlyList<Dictionary<string, string>> docRows,
        IReadOnlyList<IReadOnlyDictionary<string, string?>> codeRows, IReadOnlyList<string> columns)
    {
        var problems = new List<string>();
        for (var i = 0; i < Math.Max(docRows.Count, codeRows.Count); i++)
        {
            if (i >= docRows.Count)
            {
                problems.Add($"{section} row {i + 1}: MISSING from the document — the code seeds "
                           + $"'{codeRows[i]["Name"]}' and the table stops at {docRows.Count} rows.");
                continue;
            }
            if (i >= codeRows.Count)
            {
                problems.Add($"{section} row {i + 1}: the document lists '{Describe(docRows[i], columns)}' but "
                           + $"the code seeds only {codeRows.Count} rows — this row is seeded NOWHERE.");
                continue;
            }
            foreach (var column in columns)
            {
                if (!docRows[i].TryGetValue(column, out var raw)) continue;   // column absent from this table
                var got = NoneIfDash(raw);
                var want = codeRows[i][column];
                if (string.Equals(got, want, StringComparison.Ordinal)) continue;
                problems.Add($"{section} row {i + 1} ('{codeRows[i]["Name"]}') column '{column}': the document "
                           + $"says '{got ?? "—"}', the code seeds '{want ?? "—"}'.");
            }
        }
        return problems;
    }

    private static string Describe(IReadOnlyDictionary<string, string> row, IReadOnlyList<string> columns) =>
        string.Join(" / ", columns.Where(row.ContainsKey).Select(c => row[c]));

    private static List<IReadOnlyDictionary<string, string?>> CodeGroupRows()
    {
        var groups = SeedGroups.Build();
        var byId = groups.ToDictionary(g => g.Id, g => g.Name);
        return groups.Select(g => (IReadOnlyDictionary<string, string?>)new Dictionary<string, string?>
        {
            ["Group"] = g.Alias is null ? g.Name : $"{g.Name} (alias: {g.Alias})",
            ["Name"] = g.Name,
            ["Nature"] = g.Nature.ToString(),
            ["Parent"] = g.ParentId is null ? null : byId[g.ParentId.Value],
        }).ToList();
    }

    private static List<IReadOnlyDictionary<string, string?>> CodeVoucherTypeRows() =>
        SeedVoucherTypes.Build().Select((v, i) => (IReadOnlyDictionary<string, string?>)new Dictionary<string, string?>
        {
            ["#"] = (i + 1).ToString(CultureInfo.InvariantCulture),
            ["Name"] = v.Name,
            ["Base type"] = v.BaseType.ToString(),
            ["Shortcut"] = v.DefaultShortcut,
            ["Numbering"] = v.Numbering.ToString(),
            ["IsActive default"] = v.IsActive ? "true" : "false",
        }).ToList();

    private static List<IReadOnlyDictionary<string, string?>> CodeLedgerRows() =>
        SeedLedgers.Build(Guid.NewGuid(), Guid.NewGuid())
            .Select(l => (IReadOnlyDictionary<string, string?>)new Dictionary<string, string?>
            {
                ["Ledger"] = l.Name,
                ["Name"] = l.Name,
                ["Opening"] = $"0, {(l.OpeningIsDebit ? "Debit" : "Credit")}",
                ["IsPredefined"] = l.IsPredefined ? "true" : "false",
            }).ToList();

    [Fact]
    public void The_seed_tables_in_the_design_document_match_the_seed_code()
    {
        var lines = File.ReadAllLines(Path.Combine(RepoRoot(),
            SeedDesignDocument.Replace('/', Path.DirectorySeparatorChar)));

        var problems = new List<string>();
        // Every compared column must be DECLARED before it is compared — a header the section does not carry makes
        // CompareSeedTable's per-row `TryGetValue … continue` a silent no-op for that column (review finding).
        foreach (var (section, marker, code, columns) in new (string, string, List<IReadOnlyDictionary<string, string?>>, string[])[]
        {
            ("§5.1 groups", "### 5.1 ", CodeGroupRows(), new[] { "Group", "Nature", "Parent" }),
            ("§5.2 default ledgers", "### 5.2 ", CodeLedgerRows(), new[] { "Ledger", "Opening", "IsPredefined" }),
            ("§5.3 voucher types", "### 5.3 ", CodeVoucherTypeRows(),
                new[] { "#", "Name", "Base type", "Shortcut", "Numbering", "IsActive default" }),
        })
        {
            var (rows, headers) = SectionTables(lines, marker);
            AssertEveryComparedColumnIsDeclared(section, rows, headers, columns);
            problems.AddRange(CompareSeedTable(section, rows, code, columns));
        }

        Assert.True(problems.Count == 0,
            $"{problems.Count} disagreement(s) between {SeedDesignDocument} and the seed code. The CODE is "
            + "authoritative here — repair the table (and say in the doc why the seed differs from TallyPrime, "
            + "if it does):\n  " + string.Join("\n  ", problems));

        // The comparison is only as good as the parse; a heading rename that emptied a section would otherwise
        // pass silently on both sides.
        Assert.Equal(SeedGroups.Count, SectionTableRows(lines, "### 5.1 ").Count);
        Assert.Equal(SeedLedgers.Count, SectionTableRows(lines, "### 5.2 ").Count);
        Assert.Equal(SeedVoucherTypes.Count, SectionTableRows(lines, "### 5.3 ").Count);
    }

    // =============================================================================================
    // INVARIANT 3 — prose seed COUNTS agree with the seed constants.
    // =============================================================================================
    //
    // THE MECHANISM. "24 predefined voucher types" is written in twenty-odd places across plan.md, the
    // catalogue, the design doc, the SRS and agents.md. SeedVoucherTypes.Count has been 23 since the dead
    // Attendance row was removed. Nothing connected the sentence to the constant.

    private sealed record CountClaim(string Document, int DocumentLine, string Raw, string Family,
                                     int Claimed, int Actual)
    {
        public string Key => $"{Document}|{Raw}";
    }

    /// <summary>
    /// A count is only a count when it is not the tail of "7.2", "GAP-4", "S3" or a citation's ":1164".
    /// </summary>
    private const string CountNumber = @"(?<![A-Za-z\d.:\-])(\d+)";

    /// <summary>
    /// How far BEFORE the number the words "voucher type" must appear for a noun-less count ("24 predefined",
    /// "the 24 types") to be read as a voucher-type count. Whole-paragraph context is too coarse: `agents.md`
    /// and `plan.md` §4 both write "28 predefined" meaning GROUPS in a paragraph that goes on to mention
    /// voucher types, and both were false-positived at paragraph scope. The window LOOKS BACK ONLY, because in
    /// every genuine site the subject is named first ("**VoucherType** — **24 predefined**", "a
    /// per-voucher-type numbering config … on the 24 types") while both false positives were rescued only by
    /// a mention that came AFTER.
    /// </summary>
    private const int VoucherTypeContextWindow = 80;

    /// <summary>
    /// THE CLAIM VOCABULARY, in full. A voucher-type count is recognised only on a line that already talks
    /// about voucher types, and only in these shapes. "N additional predefined types" is deliberately absent —
    /// the 16/7 split is a documentation grouping with no constant behind it; the split is pinned instead by
    /// Invariant 2, which compares every row in order.
    /// </summary>
    private static readonly Regex VoucherTypeContext = new(@"voucher[ -]?types?",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex[] VoucherTypeCounts =
    {
        new(CountNumber + @"\s+(?:(?:pre-?defined|seeded)\s+)?voucher[ -]types?\b",
            RegexOptions.Compiled | RegexOptions.IgnoreCase),
        new(CountNumber + @"\s+(?:pre-?defined|seeded)\b(?!\s+(?:groups?|ledgers?|categor|location|currenc|godown))",
            RegexOptions.Compiled | RegexOptions.IgnoreCase),
        new(CountNumber + @"\s+types?\b", RegexOptions.Compiled | RegexOptions.IgnoreCase),
    };

    private static readonly Regex GroupCount =
        new(CountNumber + @"\s+pre-?defined\s+groups?\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex LedgerCount =
        new(CountNumber + @"\s+(?:default|pre-?defined)\s+ledgers?\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>
    /// A hard-wrapped markdown paragraph re-joined into one string, keeping a map back to the physical line
    /// each character came from. <b>Scanning line by line silently missed every claim that a hard wrap split
    /// in two</b> — `SRS-0-skeleton.md` ends a line on "…24 predefined voucher" and begins the next with
    /// "types seed", and three more sites wrap the same way. That is precisely the "covers less than the
    /// reader assumes" failure, so the scanner works on paragraphs.
    /// </summary>
    private sealed record Paragraph(string Text, IReadOnlyList<(int Offset, int Line)> LineStarts)
    {
        public int LineAt(int offset)
        {
            var line = LineStarts[0].Line;
            foreach (var (start, number) in LineStarts)
            {
                if (start > offset) break;
                line = number;
            }
            return line;
        }
    }

    /// <summary>
    /// Paragraphs = maximal runs of consecutive non-blank lines, joined with a single space. A table row,
    /// a heading and a fence line each stand alone, so nothing is glued across a structural boundary.
    /// </summary>
    private static IEnumerable<Paragraph> Paragraphs(IEnumerable<string> lines)
    {
        var text = new System.Text.StringBuilder();
        var starts = new List<(int, int)>();
        var n = 0;

        Paragraph? Flush()
        {
            if (starts.Count == 0) return null;
            var p = new Paragraph(text.ToString(), starts.ToList());
            text.Clear();
            starts.Clear();
            return p;
        }

        foreach (var line in lines)
        {
            n++;
            var trimmed = line.TrimStart();
            var standsAlone = trimmed.StartsWith('|') || trimmed.StartsWith('#') || trimmed.StartsWith("```");
            if (line.Trim().Length == 0 || standsAlone)
            {
                if (Flush() is { } flushed) yield return flushed;
                if (!standsAlone) continue;
                yield return new Paragraph(line, new[] { (0, n) });
                continue;
            }
            if (text.Length > 0) text.Append(' ');
            starts.Add((text.Length, n));
            text.Append(line);
        }
        if (Flush() is { } last) yield return last;
    }

    /// <summary>
    /// Bold/code marks, and a blockquote "&gt;" that a hard wrap left mid-sentence. Underscore is NOT here:
    /// blanking it turned the code span <c>_type.BaseType</c> into " type.BaseType" and manufactured a
    /// "1164 type" count out of a line-number citation.
    /// </summary>
    private static readonly Regex NoiseMarks = new(@"[*`]|(?<=^|\s)>(?=\s)", RegexOptions.Compiled);

    /// <summary>
    /// Blanks those marks <b>without changing length</b>, so a match offset still maps back to a physical
    /// line. Removing them instead would shift every offset after the first asterisk.
    /// </summary>
    private static string BlankMarks(string text) => NoiseMarks.Replace(text, " ");

    /// <summary>
    /// Do the words "voucher type" appear in the window ending at this match — i.e. inside the matched phrase
    /// itself, or in the <see cref="VoucherTypeContextWindow"/> characters just before it?
    /// </summary>
    private static bool VoucherTypeIsNear(string plain, Match m)
    {
        var from = Math.Max(0, m.Index - VoucherTypeContextWindow);
        return VoucherTypeContext.IsMatch(plain[from..(m.Index + m.Length)]);
    }

    private static IEnumerable<CountClaim> ScanCountClaims(string document, IEnumerable<string> lines)
    {
        foreach (var paragraph in Paragraphs(lines))
        {
            var plain = BlankMarks(paragraph.Text);
            var seen = new HashSet<int>();
            foreach (var pattern in VoucherTypeCounts)
                foreach (Match m in pattern.Matches(plain))
                {
                    if (!VoucherTypeIsNear(plain, m)) continue;
                    if (!seen.Add(m.Groups[1].Index)) continue;
                    yield return new CountClaim(document, paragraph.LineAt(m.Groups[1].Index),
                        Squash(m.Value), "predefined voucher types",
                        int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture), SeedVoucherTypes.Count);
                }
            foreach (Match m in GroupCount.Matches(plain))
                yield return new CountClaim(document, paragraph.LineAt(m.Groups[1].Index), Squash(m.Value),
                    "predefined groups",
                    int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture), SeedGroups.Count);
            foreach (Match m in LedgerCount.Matches(plain))
                yield return new CountClaim(document, paragraph.LineAt(m.Groups[1].Index), Squash(m.Value),
                    "default ledgers",
                    int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture), SeedLedgers.Count);
        }
    }

    /// <summary>Collapses the runs of blanks left by <see cref="BlankMarks"/> so the allow-list key is stable
    /// whether or not the phrase was written with emphasis inside it.</summary>
    private static string Squash(string matched) =>
        string.Join(' ', matched.Split(' ', StringSplitOptions.RemoveEmptyEntries));

    /// <summary>
    /// Count claims that are DELIBERATELY not this code's count, keyed by "document|matched phrase" and carrying
    /// <b>the number of occurrences the exemption accounts for</b>. Three kinds only: a statement about TallyPrime
    /// rather than about Apex, a quotation of a wrong figure made in order to correct it, and a CLOSED phase's own
    /// historical record of the count as it stood when that phase shipped. Anything else is a defect and must be
    /// fixed in the document.
    ///
    /// <para><b>🔴 THE COUNT IS THE POINT (review finding).</b> This used to be a bare <c>HashSet</c> keyed
    /// "document|phrase", so ONE entry exempted EVERY occurrence of that phrase in that document —
    /// <b>including occurrences that did not exist yet</b> — and <see cref="AssertNoStaleAllowListEntries"/> could not
    /// see it, because it only proves at least one live match remains. Measured: appending the sentence
    /// <i>"W0-99 — the seed ships 24 voucher types and every one of them is reachable from the menu."</i> to
    /// <c>memory.md</c> — a plainly false claim, neither TallyPrime's nor a quote-to-correct — left this file 8/8
    /// GREEN, while the byte-identical sentence in <c>README.md</c> (no entry) went red. That mattered most for the two
    /// documents the operating model appends to every session, <c>memory.md</c> and <c>plan.md</c>, and
    /// <c>memory.md</c> is the file a new session reads first. Pinning the COUNT makes a new occurrence a
    /// failure.</para>
    ///
    /// <para><b>Honest limit:</b> a count identifies how many, not which. Deleting one genuine quote-to-correct and
    /// adding one fresh violation in the same document still passes. Keying by line number was rejected — they drift
    /// on every edit — and keying by a surrounding snippet would turn an ordinary prose edit into a red suite.</para>
    /// </summary>
    private static readonly Dictionary<string, int> ForeignOrQuotedCountAllowList = new(StringComparer.Ordinal)
    {
        // The catalogue is the requirements reference: it describes TALLYPRIME, where the count really is 24
        // (Tally-Prime-Book p.17). Apex seeds 23 because the 24th, Attendance, was dead master data — nothing
        // ever posted a voucher of that kind (decision D24-B). This is a KNOWN, RECORDED fidelity gap
        // (docs/full-clone-census.md Tier 3), not a typo, and forcing the catalogue to say 23 would erase the
        // requirement. TWO occurrences: §4 (:187) and the §22 summary (:519).
        ["docs/tally-feature-catalog.md|24 predefined voucher types"] = 2,

        // The census row IS the finding: its left column quotes the false claim verbatim and its right column
        // gives the true value ("SeedVoucherTypes.cs:71 is public const int Count = 23"). Deleting the quote
        // deletes the evidence. TWO occurrences, and the SECOND is what the old bare-key allow-list was hiding:
        // §2's "† Three rows below have been acted on" banner quotes the same phrase across a hard wrap, in the same
        // act of reporting that the code says 23. The count is what surfaced it.
        ["docs/full-clone-census.md|24 predefined voucher types"] = 2,
        ["docs/full-clone-census.md|24 voucher types"] = 1,

        // Same act of reporting, one layer down: the W0-7 audit quotes a STALE COMMENT IN THE SOURCE —
        // CompanyFactory carries `// 24 voucher types.` while SeedVoucherTypes.Count is 23 — and the quote IS
        // the finding (the audit's R-5, assigned to W0-6's open remainder). Deleting the quote deletes the
        // evidence, and rewording it would stop it being a quote. TWO occurrences: the body sentence that
        // names the comment, and the R-5 row of the findings table that repeats it verbatim.
        // 🔴 THIS ENTRY RETIRES WHEN R-5 IS FIXED: correcting that comment in CompanyFactory makes both
        // occurrences historical, and the slack check will then fail this entry until the count is dropped.
        ["docs/design-records/w0-7-fixture-audit.md|24 voucher types"] = 2,

        // One level of recursion, and it is genuine rather than cute: the Phase 10.11 design record DISCUSSES
        // the allow-list entry immediately above, and quotes its key verbatim to do so. The occurrence is the
        // string `["docs/design-records/w0-7-fixture-audit.md|24 voucher types"] = 2` appearing inside prose
        // about how counted exemptions work. Rewording it would stop it being a quotation of the code.
        // ONE occurrence. Retires with the entry above, for the same reason.
        ["docs/design-records/phase-10-11-voucher-lifecycle-design.md|24 voucher types"] = 1,

        // Same shape: the kick-off list, the memory log and plan.md's own §5 warning banner each quote the
        // "24" in the act of reporting that the code says 23. All three give the true figure in the same
        // sentence — plan.md's §5 banner literally reads `*"all 24 voucher types reachable"* — the code seeds **23**`.
        ["docs/NEXT_SESSION_KICKOFF.md|24 predefined voucher types"] = 1,
        ["memory.md|24 voucher types"] = 1,

        // plan.md carries this phrase TWICE and the two are different kinds, so they are counted, not conflated:
        // (a) §5's warning banner quoting the wrong figure to retire it, and (b) Phase 10.9's Goals recording the
        // defect it FIXED — "two of the 24 voucher types had no menu row at all" — which was the count at the time
        // (the Attendance seed row was deleted by 7bfc2c6, that phase's own work). See the historical entries below.
        ["plan.md|24 voucher types"] = 2,

        // 🔴 CLOSED PHASES' COUNTS ARE HISTORICAL RECORDS (plan.md's own §5 rule, and its header's rule about gate
        // figures). Phase 10.7 shipped its F12 numbering config on 2026-07-24 (ae9d942) against 24 seeded types;
        // 7bfc2c6 deleted the Attendance row on 2026-08-03, and `git merge-base --is-ancestor ae9d942 7bfc2c6`
        // confirms the order. Rewriting those lines to 23 made the record FALSE — the same class of defect W0-14
        // existed to correct — so they are restored and exempted here instead.
        ["plan.md|24 seeded voucher types"] = 1,     // Phase 10.7 Goals
        ["plan.md|24 types"] = 1,                    // Phase 10.7 Deliverables ("F12 on the 24 types")

        // The verification report's job is to say "if the catalog says 29 predefined groups, replace with 28".
        // The 29 is the quoted error being retired; the 28 beside it is the correction and passes on its own.
        ["docs/tally-feature-catalog-verification-report.md|29 predefined groups"] = 1,
    };

    [Fact]
    public void Every_seed_count_claimed_in_the_governed_documents_matches_the_seed()
    {
        var claims = GovernedDocuments()
            .SelectMany(d => ScanCountClaims(d, File.ReadLines(Path.Combine(RepoRoot(),
                d.Replace('/', Path.DirectorySeparatorChar)))))
            .ToList();

        Assert.True(claims.Count > 40,
            $"Only {claims.Count} seed-count claims found across {GovernedDocuments().Count} documents — the "
            + "scanner is broken, so this test would be vacuous.");

        var wrong = claims.Where(c => c.Claimed != c.Actual).ToList();
        var byKey = wrong.GroupBy(c => c.Key, StringComparer.Ordinal)
                         .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);

        // A key with no entry is wholly unexempted; a key with an entry is exempted only up to the occurrence count
        // the entry declares. The SURPLUS occurrences are the violation, so a new false claim written beside a
        // justified quotation is caught rather than swallowed by the phrase key (review finding).
        var violations = byKey
            .SelectMany(kv =>
            {
                var allowed = ForeignOrQuotedCountAllowList.TryGetValue(kv.Key, out var n) ? n : 0;
                return kv.Value.Skip(allowed);
            })
            .ToList();

        Assert.True(violations.Count == 0,
            $"{violations.Count} seed-count claim(s) disagree with the seed code:\n"
            + string.Join("\n", violations
                .OrderBy(c => c.Document, StringComparer.Ordinal).ThenBy(c => c.DocumentLine)
                .Select(c => $"  {c.Document}:{c.DocumentLine} says \"{c.Raw}\" but the code seeds "
                           + $"{c.Actual} {c.Family}"
                           + (ForeignOrQuotedCountAllowList.ContainsKey(c.Key)
                                ? $"  — \"{c.Key}\" IS allow-listed, but for only "
                                  + $"{ForeignOrQuotedCountAllowList[c.Key]} occurrence(s) and the document now has "
                                  + $"{byKey[c.Key].Count}"
                                : string.Empty)))
            + "\nFix the DOCUMENT. If the sentence is deliberately about TallyPrime rather than about Apex, quotes a "
            + "wrong figure in order to correct it, or is a CLOSED phase's historical record of the count as it stood "
            + "when that phase shipped, add (or raise the count on) a justified entry in "
            + nameof(ForeignOrQuotedCountAllowList) + " — and say which of the three it is.");

        AssertNoStaleAllowListEntries(ForeignOrQuotedCountAllowList.Keys, wrong.Select(c => c.Key),
            nameof(ForeignOrQuotedCountAllowList));

        // …and the mirror of stale: an entry that accounts for MORE occurrences than the document actually has is
        // an exemption with slack in it, which is a licence for the next false claim. It must be tightened, not left.
        var slack = ForeignOrQuotedCountAllowList
            .Where(kv => byKey.TryGetValue(kv.Key, out var hits) && hits.Count < kv.Value)
            .Select(kv => $"{kv.Key} — exempts {kv.Value}, document has {byKey[kv.Key].Count}")
            .OrderBy(s => s, StringComparer.Ordinal).ToList();
        Assert.True(slack.Count == 0,
            $"{nameof(ForeignOrQuotedCountAllowList)}: {slack.Count} entr{(slack.Count == 1 ? "y exempts" : "ies exempt")} "
            + "more occurrences than the document contains. Slack in an exemption silently pre-approves the next "
            + "false claim. Lower the count:\n  " + string.Join("\n  ", slack));
    }

    // =============================================================================================
    // NON-VACUITY AND BITE PROOFS
    // =============================================================================================

    [Fact]
    public void TheScanActuallyReadsTheGovernedDocuments()
    {
        var documents = GovernedDocuments();
        Assert.True(documents.Count >= 25, $"Only {documents.Count} governed documents found.");
        Assert.Contains("plan.md", documents);
        Assert.Contains("memory.md", documents);
        Assert.Contains(SeedDesignDocument, documents);

        var citations = documents
            .SelectMany(d => ScanCitations(d, File.ReadLines(Path.Combine(RepoRoot(),
                d.Replace('/', Path.DirectorySeparatorChar)))))
            .ToList();
        Assert.True(citations.Count >= 1500, $"Only {citations.Count} file:line citations found.");
        var anchors = citations.Count + citations.Count(c => c.LastLine is not null);
        Assert.True(anchors >= 2000, $"Only {anchors} line anchors checked.");
    }

    [Fact]
    public void TheCitationScannerBitesOnASyntheticDangler()
    {
        // A real file, cited inside itself and past its end; a file that does not exist; and a good citation.
        var real = "src/Apex.Ledger/Seed/SeedVoucherTypes.cs";
        var beyond = LineCount(real) + 1;
        var scanned = ScanCitations("synthetic.md", new[]
        {
            $"good: `{real}:1-10` and bare `SeedGroups.cs:2`",
            $"past EOF: `{real}:{beyond}`",
            "gone: `src/Apex.Ledger.Nonexistent/Schema.cs:1`",
            $"range end past EOF: `{real}:1-{beyond}`",
        }).ToList();

        Assert.Equal(5, scanned.Count);
        Assert.Null(CitationViolation(scanned[0]));
        Assert.Null(CitationViolation(scanned[1]));
        Assert.Contains("PAST EOF", CitationViolation(scanned[2]));
        Assert.Contains("NO SUCH FILE", CitationViolation(scanned[3]));
        Assert.Contains("PAST EOF", CitationViolation(scanned[4]));

        // And an ambiguous bare name really is refused rather than silently resolved to the first hit.
        // EsiContribution.cs genuinely exists twice in src/ (Reports/ and Services/).
        Assert.True(ResolveCitedPath("EsiContribution.cs").Count > 1,
            "The ambiguity limb is untested — the 'EsiContribution.cs' collision is gone; pick another name.");
        var ambiguous = ScanCitations("synthetic.md", new[] { "`EsiContribution.cs:1`" }).Single();
        Assert.Contains("AMBIGUOUS", CitationViolation(ambiguous));
    }

    [Fact]
    public void TheSeedTableComparerBitesOnEachMutation()
    {
        var columns = new[] { "#", "Name", "Base type", "Shortcut", "Numbering", "IsActive default" };
        var code = CodeVoucherTypeRows();

        List<Dictionary<string, string>> Faithful() => code
            .Select(r => r.ToDictionary(kv => kv.Key, kv => kv.Value ?? "—", StringComparer.OrdinalIgnoreCase))
            .ToList();

        Assert.Empty(CompareSeedTable("t", Faithful(), code, columns));

        var renamed = Faithful(); renamed[0]["Name"] = "Kontra";
        Assert.Contains("column 'Name'", string.Join("|", CompareSeedTable("t", renamed, code, columns)));

        var reshortcut = Faithful(); reshortcut[9]["Shortcut"] = "F10 (Physical Stock)";
        Assert.Contains("column 'Shortcut'", string.Join("|", CompareSeedTable("t", reshortcut, code, columns)));

        var deactivated = Faithful(); deactivated[0]["IsActive default"] = "false";
        Assert.Contains("column 'IsActive default'",
            string.Join("|", CompareSeedTable("t", deactivated, code, columns)));

        var extra = Faithful();
        extra.Add(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            { ["#"] = "24", ["Name"] = "Attendance", ["Base type"] = "Attendance" });
        Assert.Contains("seeded NOWHERE", string.Join("|", CompareSeedTable("t", extra, code, columns)));

        var dropped = Faithful(); dropped.RemoveAt(dropped.Count - 1);
        Assert.Contains("MISSING from the document", string.Join("|", CompareSeedTable("t", dropped, code, columns)));

        // The group and ledger shapes are compared by the same routine, so one round-trip each is enough to
        // show their column maps line up rather than silently matching nothing.
        var groups = CodeGroupRows();
        Assert.Empty(CompareSeedTable("g",
            groups.Select(r => r.ToDictionary(kv => kv.Key, kv => kv.Value ?? "—",
                StringComparer.OrdinalIgnoreCase)).ToList(),
            groups, new[] { "Group", "Nature", "Parent" }));
        var ledgers = CodeLedgerRows();
        Assert.Empty(CompareSeedTable("l",
            ledgers.Select(r => r.ToDictionary(kv => kv.Key, kv => kv.Value ?? "—",
                StringComparer.OrdinalIgnoreCase)).ToList(),
            ledgers, new[] { "Ledger", "Opening", "IsPredefined" }));
    }

    [Fact]
    public void TheCountScannerBitesOnAWrongNumber()
    {
        var claims = ScanCountClaims("synthetic.md", new[]
        {
            "the **24 predefined voucher types** seed",       // 1 — emphasis must not hide it
            "",
            "Seed: **28 predefined groups** + **2 default ledgers** + 24 voucher types.",   // 3
            "",
            "- **VoucherType** — **24 predefined** (base type + shortcut + numbering)",     // 5
            "",
            "a per-voucher-type numbering config reachable by **F12** on the 24 types",     // 7
            "",
            "the existing **24 seeded voucher types**",                                     // 9
            "",
            "29 predefined groups and 3 default ledgers",                                   // 11
            "",
            "the 28 predefined groups + 2 default ledgers + 24 predefined voucher",         // 13 — HARD WRAP:
            "types seed, and matching reports",                                             // 14   the claim
        }).ToList();                                                                        //      spans both

        Assert.Equal(23, claims.Single(c => c.DocumentLine == 1).Actual);
        Assert.Equal(24, claims.Single(c => c.DocumentLine == 1).Claimed);
        // All three families fire on one line, and the group/ledger counts beside a voucher count are NOT
        // swallowed by the voucher vocabulary (that lookahead is what keeps "28 predefined groups" out of it).
        Assert.Equal(new[] { 24, 28, 2 }, claims.Where(c => c.DocumentLine == 3).Select(c => c.Claimed).ToArray());
        Assert.Equal(new[] { "predefined voucher types", "predefined groups", "default ledgers" },
            claims.Where(c => c.DocumentLine == 3).Select(c => c.Family).ToArray());
        Assert.Equal(24, claims.Single(c => c.DocumentLine == 5).Claimed);
        Assert.Equal(24, claims.Single(c => c.DocumentLine == 7).Claimed);
        Assert.Equal(24, claims.Single(c => c.DocumentLine == 9).Claimed);
        Assert.Equal(new[] { 29, 3 }, claims.Where(c => c.DocumentLine == 11).Select(c => c.Claimed).ToArray());

        // The hard-wrapped claim is seen, is attributed to the line the NUMBER sits on (13, not 14), and reads
        // back as one phrase. Line-by-line scanning missed this class entirely.
        var wrapped = claims.Single(c => c.Family == "predefined voucher types" && c.DocumentLine == 13);
        Assert.Equal(24, wrapped.Claimed);
        Assert.Equal("24 predefined voucher types", wrapped.Raw);

        Assert.Equal(6, claims.Count(c => c.Claimed != c.Actual && c.Family == "predefined voucher types"));
    }

    [Fact]
    public void TheCountScannerDoesNotFireOnTheseVerbatimShippedLines()
    {
        // Every one of these is a real line from the tree that an earlier, looser version of the vocabulary
        // flagged. They must stay unflagged, or the next author "fixes" a sentence that was already true.
        // Blank-separated so each is its own paragraph — gluing them would test something else.
        var innocent = new[]
        {
            "| S3  Voucher Type master + Show Inactive + Voucher Class",              // "S3" is not a count
            "",
            "official 7.2 voucher-type list on the public web [AUDIT §6.3]",          // "7.2" is a version
            "",
            "### 2.2 Voucher-type-master conditions",                                 // "2.2" is a section
            "",
            "- **GAP-4 Voucher-type reachability** (`claude/gap-4` → `7bfc2c6`)",     // "GAP-4" is an id
            "",
            "The **23-vs-24 voucher-type count** (and it is a real fidelity gap)",    // names the discrepancy
            "",
            "the **28 predefined groups + 2 default ledgers + 23 predefined voucher types** seed",
            "",
            "The **7 additional predefined types** are reached from F10 Other Vouchers",  // outside the vocabulary
            "",
            // agents.md: "28 predefined" here means GROUPS. "voucher types" is 100+ characters away on the
            // next wrapped line, so only a whole-PARAGRAPH context gate would (wrongly) claim this one.
            "  schema: companies, groups (28 predefined + custom, with nature/parent), ledgers, cost",
            "  categories/centres, voucher types & voucher headers/line-entries, bill-wise references, stock",
            "",
            // plan.md §4: same shape, the other way round — the voucher count is on the PRECEDING line.
            "  *Seed on create: 28 groups + 2 ledgers + 23 voucher types + Primary Cost Category.*",
            "- **Group** — classification node with **nature** (Asset/Liability/Income/Expense) + parent."
                + " **28 predefined**",
            "",
            // ca-audit-backlog.md: a line-number citation next to a code span. ":1164" is not a count, and
            // `_type` must not be blanked into " type".
            "`VoucherEntryViewModel` **implements them** (`:1164` `_type.BaseType is VoucherBaseType.CreditNote`)",
        };

        var claims = ScanCountClaims("synthetic.md", innocent).ToList();
        var fired = claims.Where(c => c.Claimed != c.Actual).ToList();
        Assert.True(fired.Count == 0,
            "False positive(s): " + string.Join(" | ", fired.Select(c => $"line {c.DocumentLine} \"{c.Raw}\"")));
    }
}
