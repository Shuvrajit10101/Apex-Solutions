using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Apex.Ledger.Io;
using Xunit;

namespace Apex.Ledger.Io.Tests;

/// <summary>
/// 🔴 <b>DRIFT LOCK — the spreadsheet-formula guard exists TWICE in this tree, on purpose, and the two copies must
/// never disagree.</b>
///
/// <para><b>Why there are two.</b> <see cref="SpreadsheetFormulaGuard"/> is the home of the rule for everything
/// that can see <c>Apex.Ledger.Io</c>. <c>Apex.Ledger.Reports.EPayments.Csv</c> — the bank payment-instruction
/// file — cannot: <c>Apex.Ledger.Io.csproj</c> references <c>Apex.Ledger</c>, so the dependency points
/// <c>Io → Ledger</c> and reversing it to share forty characters of guard would be a far worse trade than the
/// duplication. The copy stays; what must not happen is that it silently falls behind.</para>
///
/// <para>🔴 <b>The concrete failure this prevents.</b> Somebody greps, finds a doc comment saying the rule has one
/// home, adds a new trigger to <see cref="SpreadsheetFormulaGuard"/>, ships — and every payment-instruction file
/// quietly keeps the old rule. A cross-reference in a comment is advisory; this test makes it enforceable.</para>
///
/// <para><b>How it works, and why it is not vacuous.</b> The canonical trigger set is derived AT RUNTIME by
/// probing <see cref="SpreadsheetFormulaGuard.Neutralize"/> itself, so it can never be a stale copy of the rule.
/// The shipped <c>src/</c> tree is then scanned for the guard's textual idiom — a
/// <c>c is '=' or '+' or … </c> character set containing both <c>'='</c> and <c>'@'</c> — and EVERY site found
/// must declare exactly the canonical set and must also carry the leading-space skip.
/// <see cref="The_lock_bites_on_a_divergent_copy"/> runs the same extraction over reconstructed divergent sources
/// and proves each check actually fails on them, so "the scan found nothing wrong" cannot silently mean "the
/// pattern matches nothing".</para>
///
/// <para><b>Honest limit.</b> This matches the textual idiom, so a copy that restructures the expression entirely
/// (a <c>HashSet&lt;char&gt;</c>, a <c>switch</c>) slips past. It is a ratchet against the ordinary copy-paste that
/// actually happened here, not a proof of uniqueness.</para>
/// </summary>
public sealed class SpreadsheetFormulaGuardDriftLockTests
{
    /// <summary>A <c>c is 'x' or 'y' or …</c> character-set test, tolerant of line breaks between the alternatives.</summary>
    private const string TriggerSetPattern =
        @"is\s*(?:'(?:\\.|[^'\\])'\s*or\s*)+'(?:\\.|[^'\\])'";

    /// <summary>One C# character literal inside such a set.</summary>
    private const string CharLiteralPattern = @"'((?:\\.|[^'\\]))'";

    /// <summary>The leading-space skip, written variable-agnostically: <c>while (i &lt; f.Length &amp;&amp; f[i] == ' ') i++;</c>.</summary>
    private const string SpaceSkipPattern =
        @"while\s*\(\s*\w+\s*<\s*\w+\.Length\s*&&\s*\w+\[\s*\w+\s*\]\s*==\s*' '\s*\)";

    /// <summary>The file that is allowed — and required — to be the home of the rule for this assembly.</summary>
    private const string GuardHome = "src/Apex.Ledger.Io/SpreadsheetFormulaGuard.cs";

    /// <summary>The one copy that cannot share it, named so a failure says WHERE the other rule lives.</summary>
    private const string ForcedDuplicate = "src/Apex.Ledger/Reports/EPayments.cs";

    // ─────────────────────────────────────────────────────────────────────────────── the locks

    /// <summary>
    /// Every formula-trigger set in the shipped tree declares the SAME characters the shared guard actually acts
    /// on. This is the assertion that fails when one copy gains (or loses) a trigger and the other does not.
    /// </summary>
    [Fact]
    public void Every_copy_of_the_guard_declares_the_same_trigger_set()
    {
        var canonical = CanonicalTriggers();
        var sites = GuardSites();

        // Non-vacuity: the scan must find the home itself, or the pattern has stopped matching the rule.
        Assert.Contains(GuardHome, sites.Select(s => s.File));

        var divergent = sites
            .Where(s => !s.Triggers.SetEquals(canonical))
            .Select(s => $"  {s.File}:{s.Line} declares {Show(s.Triggers)}")
            .ToList();

        Assert.True(divergent.Count == 0,
            "a copy of the spreadsheet-formula guard has drifted from SpreadsheetFormulaGuard.Neutralize, which "
            + $"acts on {Show(canonical)}:" + Environment.NewLine + string.Join(Environment.NewLine, divergent)
            + Environment.NewLine
            + "Change every copy in the same commit — see the two-copy note on SpreadsheetFormulaGuard.");
    }

    /// <summary>
    /// Every file carrying a copy of the trigger set also carries the leading-space skip. Dropping it in one copy
    /// is the divergence that would leave indented output (and any importer that trims) unguarded there while the
    /// other copy stayed safe.
    /// </summary>
    [Fact]
    public void Every_copy_of_the_guard_keeps_the_leading_space_skip()
    {
        var rx = new Regex(SpaceSkipPattern, RegexOptions.Compiled);

        var offenders = GuardSites()
            .Select(s => s.File)
            .Distinct(StringComparer.Ordinal)
            .Where(f => !rx.IsMatch(File.ReadAllText(Path.Combine(RepoRoot(), f.Replace('/', Path.DirectorySeparatorChar)))))
            .ToList();

        Assert.True(offenders.Count == 0,
            "a copy of the guard tests field[0] instead of the first value-carrying character:"
            + Environment.NewLine + string.Join(Environment.NewLine, offenders.Select(f => "  " + f)));
    }

    /// <summary>
    /// The forced duplicate is named explicitly, so a reader of a failure knows which OTHER file to edit.
    ///
    /// <para>🔴 The existence check is deliberate and is not a dead guard: <c>EPayments.cs</c> arrives in this
    /// branch's tree only when <c>origin/main</c> (PR #83) is merged in, and this test has to be correct both
    /// before and after that merge. The lock that does the real work —
    /// <see cref="Every_copy_of_the_guard_declares_the_same_trigger_set"/> — is unconditional and covers whatever
    /// copies are present; this one adds the specific claim that the KNOWN second copy is one of them once the
    /// file is there. If <c>EPayments.cs</c> exists and has stopped matching, that is a real failure: either the
    /// copy was restructured out of the pattern's sight or the payment file lost its guard.</para>
    /// </summary>
    [Fact]
    public void The_known_second_copy_is_covered_by_the_scan_once_it_is_in_the_tree()
    {
        var path = Path.Combine(RepoRoot(), ForcedDuplicate.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(path)) return;

        Assert.Contains(ForcedDuplicate, GuardSites().Select(s => s.File));
    }

    /// <summary>
    /// 🔴 <b>The bite proof.</b> The two checks above are run over reconstructed sources that DO diverge, using the
    /// same constants, so a lock that had stopped matching its own rule fails here rather than passing quietly.
    /// </summary>
    [Fact]
    public void The_lock_bites_on_a_divergent_copy()
    {
        var canonical = CanonicalTriggers();

        // (a) a copy that has lost the CR trigger — the exact shape of the bypass this slice closed.
        const string lostCr = @"return c is '=' or '+' or '-' or '@' or '\t' ? ""'"" + v : v;";
        var lostCrSets = ExtractTriggerSets(lostCr).ToList();
        Assert.Single(lostCrSets);
        Assert.False(lostCrSets[0].SetEquals(canonical));

        // (b) a copy that has GAINED a trigger the shared guard does not have.
        const string extra = @"if (ch is '=' or '+' or '-' or '@' or '\t' or '\r' or '|') v = ""'"" + v;";
        var extraSets = ExtractTriggerSets(extra).ToList();
        Assert.Single(extraSets);
        Assert.False(extraSets[0].SetEquals(canonical));

        // (c) a faithful copy, variable-renamed and line-split, is NOT flagged — the lock must not cry wolf.
        const string faithful = @"
            if (first is '=' or '+' or '-'
                or '@' or '\t' or '\r')
                text = ""'"" + text;";
        var faithfulSets = ExtractTriggerSets(faithful).ToList();
        Assert.Single(faithfulSets);
        Assert.True(faithfulSets[0].SetEquals(canonical));

        // (d) the space-skip pattern really matches the idiom, and really misses a field[0] copy.
        var skip = new Regex(SpaceSkipPattern, RegexOptions.Compiled);
        Assert.Matches(skip, "while (n < text.Length && text[n] == ' ') n++;");
        Assert.DoesNotMatch(skip, "char c = field.Length > 0 ? field[0] : ' ';");
    }

    /// <summary>The scan really reads the shipped tree — a mis-rooted scan would find no files and pass silently.</summary>
    [Fact]
    public void The_scan_actually_reads_the_shipped_tree()
    {
        Assert.True(ShippedSources().Count() > 100);
        Assert.Contains(ShippedSources(), p => p.EndsWith("SpreadsheetFormulaGuard.cs", StringComparison.Ordinal));
    }

    // ─────────────────────────────────────────────────────────────────────── the machinery

    private sealed record Site(string File, int Line, HashSet<char> Triggers);

    /// <summary>
    /// The characters <see cref="SpreadsheetFormulaGuard.Neutralize"/> ACTUALLY prefixes on, discovered by probing
    /// it rather than by restating the rule — so this test can never lock in a copy of the rule that the code has
    /// already moved away from.
    /// </summary>
    private static HashSet<char> CanonicalTriggers()
    {
        var set = new HashSet<char>();
        for (var i = 0; i <= 0xFFFF; i++)
        {
            var ch = (char)i;
            var probe = ch.ToString();
            if (SpreadsheetFormulaGuard.Neutralize(probe) != probe) set.Add(ch);
        }
        Assert.NotEmpty(set);
        return set;
    }

    /// <summary>Every formula-guard trigger set declared in the shipped tree, with the file and line it sits on.</summary>
    private static IReadOnlyList<Site> GuardSites()
    {
        var root = RepoRoot();
        var sites = new List<Site>();

        foreach (var path in ShippedSources())
        {
            var text = File.ReadAllText(path);
            foreach (var (set, index) in ExtractTriggerSetsWithOffsets(text))
            {
                var rel = Path.GetRelativePath(root, path).Replace('\\', '/');
                var line = text.Take(index).Count(c => c == '\n') + 1;
                sites.Add(new Site(rel, line, set));
            }
        }

        return sites;
    }

    /// <summary>The formula-guard trigger sets in one piece of source text (those containing both
    /// <c>'='</c> and <c>'@'</c> — the signature that tells this rule apart from any other character test).</summary>
    private static IEnumerable<HashSet<char>> ExtractTriggerSets(string source) =>
        ExtractTriggerSetsWithOffsets(source).Select(x => x.Set);

    private static IEnumerable<(HashSet<char> Set, int Index)> ExtractTriggerSetsWithOffsets(string source)
    {
        foreach (Match m in Regex.Matches(source, TriggerSetPattern, RegexOptions.Singleline))
        {
            var set = new HashSet<char>();
            foreach (Match lit in Regex.Matches(m.Value, CharLiteralPattern))
                set.Add(Unescape(lit.Groups[1].Value));

            if (set.Contains('=') && set.Contains('@')) yield return (set, m.Index);
        }
    }

    /// <summary>Turns one C# character-literal body (<c>=</c>, <c>\t</c>, <c>\\</c>) into the character it denotes.</summary>
    private static char Unescape(string body) => body switch
    {
        "\\t" => '\t',
        "\\r" => '\r',
        "\\n" => '\n',
        "\\0" => '\0',
        "\\\\" => '\\',
        "\\'" => '\'',
        "\\\"" => '"',
        _ => body[0],
    };

    private static string Show(IEnumerable<char> chars) =>
        "{ " + string.Join(", ", chars.OrderBy(c => c).Select(c => c switch
        {
            '\t' => "\\t",
            '\r' => "\\r",
            '\n' => "\\n",
            _ => c.ToString(),
        })) + " }";

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Apex.slnx")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }

    /// <summary>Every shipped C# source file (the <c>src/</c> tree), excluding build output.</summary>
    private static IEnumerable<string> ShippedSources()
    {
        var src = Path.Combine(RepoRoot(), "src");
        Assert.True(Directory.Exists(src), src);
        return Directory
            .EnumerateFiles(src, "*.cs", SearchOption.AllDirectories)
            .Where(p => !p.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                     && !p.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal));
    }
}
