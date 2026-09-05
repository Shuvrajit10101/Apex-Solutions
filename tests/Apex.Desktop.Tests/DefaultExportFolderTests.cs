using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Apex.Desktop.Services;

namespace Apex.Desktop.Tests;

/// <summary>
/// 🔴 <b>A GENERATED FILE NEVER LANDS IN THE PROCESS WORKING DIRECTORY.</b>
///
/// <para><b>The defect these tests close.</b> Nineteen shipped call sites — every statutory TDS/TCS return
/// (Form 16, 16A, 24Q, 26Q, 27A, 27D, 27EQ), the GST offline returns, the PF ECR, the ESI contribution
/// report, the Professional Tax register, the e-invoice and e-way-bill payloads, the report exports, the
/// saved print preview, the saved e-mail, and the <b>company backup</b> — each seeded its destination folder
/// with an unguarded <c>try { … MyDocuments … } catch { … string.Empty; }</c>. Left empty,
/// <see cref="Path.Combine(string, string)"/> collapses to a bare file name and the writer drops the file
/// beside the executable: no picker is shown, and the user has no way to learn where a filed return went.</para>
///
/// <para><b>Why the platform matters and why Windows CI could not see it.</b> On Linux and macOS .NET
/// resolves <see cref="Environment.SpecialFolder.MyDocuments"/> from the XDG user-directory configuration and
/// returns the <b>empty string</b> — not an exception — when that configuration is absent, which is the state
/// of an ordinary CI container. Every call site caught exceptions, so nothing tripped; the seed was simply
/// blank. On Windows the lookup always succeeds, so the whole class of failure is invisible to a Windows-only
/// gate. That is exactly how this reached CI.</para>
///
/// <para><b>Two kinds of test, deliberately.</b> The behavioural half drives
/// <see cref="DefaultExportFolder.Resolve(Func{Environment.SpecialFolder, string}, Func{string})"/> through
/// an <b>injected</b> lookup, so the "My Documents is blank" platform is expressible without mutating
/// process-wide environment state — an env-var mutation would leak into whatever xUnit runs beside it in
/// parallel. The <b>drift lock</b> half re-reads the shipped <c>src/</c> tree and fails if a new
/// <c>MyDocuments</c> (or its alias <c>Personal</c>) call site appears outside the one home. A behavioural
/// test pins what the home does; only a source scan notices a twentieth copy written beside a new feature —
/// which is precisely how the first nineteen accumulated. Modelled on the drift locks in
/// <c>tests/Apex.Ledger.Tests/OneRuleDriftLockTests.cs</c>.</para>
/// </summary>
public sealed class DefaultExportFolderTests
{
    // ============================================================ the shared pattern
    // Used BOTH by the tree scan and by the bite proof, so the two can never drift apart.

    /// <summary>
    /// The idiom this slice removed. <c>Personal</c> is included because .NET maps it to the SAME value as
    /// <c>MyDocuments</c> — a new site spelled <c>Personal</c> would reintroduce the identical defect while
    /// dodging a <c>MyDocuments</c>-only pattern. Whitespace is tolerated so a reformat cannot slip past.
    /// </summary>
    private const string BlankableUserFolderLookup = @"SpecialFolder\s*\.\s*(MyDocuments|Personal)\b";

    /// <summary>The one file allowed to contain it.</summary>
    private const string HomeFile = "DefaultExportFolder.cs";

    // ============================================================ behaviour: the ladder

    /// <summary>
    /// <b>The headline case.</b> My Documents resolves to the empty string — the Linux/CI platform — and the
    /// result must still be a real folder, and must NOT be the process working directory. This is the exact
    /// state that shipped a return file beside the executable.
    /// </summary>
    [Fact]
    public void A_blank_my_documents_falls_through_to_the_user_profile_and_never_to_the_working_directory()
    {
        var home = Path.Combine(Path.GetTempPath(), "apex-user-profile-fixture");

        var resolved = DefaultExportFolder.Resolve(
            folder => folder switch
            {
                Environment.SpecialFolder.MyDocuments => string.Empty,   // Linux with no XDG user dirs
                Environment.SpecialFolder.UserProfile => home,
                _ => string.Empty,
            },
            () => Path.Combine(Path.GetTempPath(), "apex-temp-fixture"));

        Assert.False(string.IsNullOrWhiteSpace(resolved));
        Assert.Equal(home, resolved);
        AssertIsNotTheWorkingDirectory(resolved);
    }

    /// <summary>
    /// Both user folders blank — a service account with neither XDG dirs nor <c>$HOME</c>. The temp folder is
    /// the last real rung, and it is still not the working directory.
    /// </summary>
    [Fact]
    public void With_no_user_folders_at_all_the_result_is_the_platform_temp_folder_not_the_working_directory()
    {
        var temp = Path.Combine(Path.GetTempPath(), "apex-temp-fixture");

        var resolved = DefaultExportFolder.Resolve(_ => string.Empty, () => temp);

        Assert.False(string.IsNullOrWhiteSpace(resolved));
        Assert.Equal(temp, resolved);
        AssertIsNotTheWorkingDirectory(resolved);
    }

    /// <summary>
    /// Whitespace is blank. A lookup that hands back <c>" "</c> would satisfy a naive null check and then
    /// produce a path with a leading space separator — a different silent misfile, not a fix.
    /// </summary>
    [Fact]
    public void A_whitespace_only_lookup_counts_as_blank_and_falls_through()
    {
        var home = Path.Combine(Path.GetTempPath(), "apex-user-profile-fixture");

        var resolved = DefaultExportFolder.Resolve(
            folder => folder == Environment.SpecialFolder.UserProfile ? home : "   ",
            () => Path.Combine(Path.GetTempPath(), "apex-temp-fixture"));

        Assert.Equal(home, resolved);
        AssertIsNotTheWorkingDirectory(resolved);
    }

    /// <summary>
    /// A throwing lookup falls through instead of propagating — the historical swallow-the-exception
    /// behaviour is kept at the boundary. What changed is that swallowing can no longer yield an empty seed.
    /// </summary>
    [Fact]
    public void A_lookup_that_throws_falls_through_rather_than_propagating_or_returning_empty()
    {
        var temp = Path.Combine(Path.GetTempPath(), "apex-temp-fixture");

        var resolved = DefaultExportFolder.Resolve(
            _ => throw new PlatformNotSupportedException("no special folders here"),
            () => temp);

        Assert.Equal(temp, resolved);
        AssertIsNotTheWorkingDirectory(resolved);
    }

    /// <summary>
    /// Even with EVERY rung blank — including the temp folder, which only the injected seam can express —
    /// the result is a rooted folder, never <see cref="string.Empty"/>. This is the invariant the nineteen
    /// call sites are now entitled to rely on.
    /// </summary>
    [Fact]
    public void The_result_is_never_empty_even_when_every_rung_is_blank()
    {
        var resolved = DefaultExportFolder.Resolve(_ => string.Empty, () => string.Empty);

        Assert.False(string.IsNullOrWhiteSpace(resolved));
        Assert.True(Path.IsPathRooted(resolved), resolved);
        AssertIsNotTheWorkingDirectory(resolved);
    }

    /// <summary>
    /// Windows behaviour is unchanged: when My Documents resolves, it is returned verbatim. The fix adds
    /// rungs BELOW the existing one; it does not redirect the platform where the old code already worked.
    /// </summary>
    [Fact]
    public void A_populated_my_documents_is_returned_unchanged()
    {
        var documents = Path.Combine(Path.GetTempPath(), "apex-documents-fixture");

        var resolved = DefaultExportFolder.Resolve(
            folder => folder == Environment.SpecialFolder.MyDocuments ? documents : "unused",
            () => "unused");

        Assert.Equal(documents, resolved);
    }

    /// <summary>
    /// The real, uninjected entry point — the one the nineteen call sites actually call — on THIS machine,
    /// whichever OS the gate is running on. Rooted, non-blank, and not the working directory.
    /// </summary>
    [Fact]
    public void The_shipped_entry_point_yields_a_rooted_folder_on_this_platform()
    {
        var resolved = DefaultExportFolder.Resolve();

        Assert.False(string.IsNullOrWhiteSpace(resolved));
        Assert.True(Path.IsPathRooted(resolved), resolved);
        AssertIsNotTheWorkingDirectory(resolved);
    }

    /// <summary>
    /// Compares folder paths the way the filesystem does for this purpose: a trailing separator is not a
    /// difference (<see cref="Path.GetTempPath"/> returns one, <c>$HOME</c> does not), and the comparison is
    /// case-insensitive on Windows and case-SENSITIVE elsewhere, matching each platform's real semantics.
    /// </summary>
    private static void AssertIsNotTheWorkingDirectory(string resolved)
    {
        Assert.False(SameFolder(resolved, Directory.GetCurrentDirectory()),
            $"the default destination resolved to the process working directory: {resolved}");
        Assert.False(SameFolder(resolved, AppContext.BaseDirectory),
            $"the default destination resolved to the executable's own directory: {resolved}");
    }

    private static bool SameFolder(string a, string b)
    {
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        return string.Equals(Trim(a), Trim(b), comparison);

        static string Trim(string p) =>
            p.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    // ============================================================ drift lock: scanning machinery

    /// <summary>The repository root — the directory holding <c>Apex.slnx</c>.</summary>
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

    // ============================================================ drift lock: the lock itself

    /// <summary>
    /// 🔴 <b>THE DRIFT LOCK.</b> No shipped file outside <see cref="HomeFile"/> may look up a user folder that
    /// can come back blank. Every new export screen must seed through
    /// <see cref="DefaultExportFolder.Resolve()"/> instead.
    ///
    /// <para>This project has repeatedly had a fix re-broken by the next feature, because the correct-looking
    /// two-liner is the obvious thing to copy from the file next door. The lock is the ratchet: re-introducing
    /// the idiom is now a red test on every platform, including the Windows leg that cannot observe the
    /// behavioural failure at all.</para>
    /// </summary>
    [Fact]
    public void No_shipped_file_outside_the_one_home_looks_up_a_blankable_user_folder()
    {
        var rx = new Regex(BlankableUserFolderLookup, RegexOptions.Compiled);
        var offenders = new List<string>();

        foreach (var path in ShippedSources())
        {
            if (string.Equals(Path.GetFileName(path), HomeFile, StringComparison.Ordinal)) continue;

            var lines = File.ReadAllLines(path);
            for (var i = 0; i < lines.Length; i++)
                if (rx.IsMatch(lines[i]))
                    offenders.Add($"  {Path.GetRelativePath(RepoRoot(), path).Replace('\\', '/')}:{i + 1}  {lines[i].Trim()}");
        }

        if (offenders.Count > 0)
            Assert.Fail(
                $"A blankable user-folder lookup has reappeared outside {HomeFile}.\n" +
                $"Pattern: {BlankableUserFolderLookup}\n" + string.Join("\n", offenders) +
                "\nOn Linux/macOS MyDocuments (and its alias Personal) resolves to the EMPTY STRING when XDG user\n" +
                "dirs are unconfigured, and the resulting bare file name writes the file into the process working\n" +
                "directory. Call DefaultExportFolder.Resolve() instead — it is guaranteed non-empty.");
    }

    /// <summary>
    /// <b>Non-vacuity, asserted rather than assumed.</b> Runs the SAME pattern constant over reconstructed
    /// copies of every shape the removed idiom actually took in this tree — property assignment, local
    /// assignment, method return, fully-qualified <c>System.Environment</c>, the <c>Personal</c> alias, and a
    /// reformatted/line-split variant. A lock that quietly stopped matching its own rule would fail HERE, so
    /// "the scan found nothing" can never silently mean "the pattern matches nothing".
    /// </summary>
    [Fact]
    public void The_lock_bites_on_every_shape_the_removed_idiom_took()
    {
        var rx = new Regex(BlankableUserFolderLookup, RegexOptions.Compiled);

        string[] mustMatch =
        {
            "        try { ExportFolder = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments); }",
            "        var dir = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);",
            "        try { return Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments); }",
            "        try { return System.Environment.GetFolderPath(System.Environment.SpecialFolder.MyDocuments); }",
            "        var home = Environment.GetFolderPath(Environment.SpecialFolder.Personal);",
            "            Environment . SpecialFolder . MyDocuments );",
        };

        foreach (var line in mustMatch)
            Assert.True(rx.IsMatch(line), $"the lock stopped matching a real removed idiom: {line}");

        // And it must NOT fire on the folders that are legitimately different. CompanyStorage's
        // ApplicationData is a DIFFERENT folder for a different purpose and is correct as it stands; a lock
        // that swept it up would be silenced with an exemption, and an exempted lock gets deleted.
        string[] mustNotMatch =
        {
            "        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);",
            "        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);",
            "        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);",
            "        var docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocumentsArchive);",
        };

        foreach (var line in mustNotMatch)
            Assert.False(rx.IsMatch(line), $"the lock over-matched a legitimate lookup: {line}");
    }

    /// <summary>
    /// Guards the scan itself. If <c>src/</c> moved, the home file were renamed, or the enumeration silently
    /// returned nothing, <see cref="No_shipped_file_outside_the_one_home_looks_up_a_blankable_user_folder"/>
    /// would pass while protecting nothing. Asserting that the pattern DOES match inside the home proves it is
    /// live against real shipped text, not only against the synthetic strings above.
    /// </summary>
    [Fact]
    public void The_scan_actually_reads_the_shipped_tree_and_the_home_still_holds_the_rule()
    {
        var files = ShippedSources().ToList();
        Assert.True(files.Count > 100, $"expected the src/ tree, found only {files.Count} files");

        var home = files.SingleOrDefault(p => string.Equals(Path.GetFileName(p), HomeFile, StringComparison.Ordinal));
        Assert.NotNull(home);

        var rx = new Regex(BlankableUserFolderLookup, RegexOptions.Compiled);
        Assert.Matches(rx, File.ReadAllText(home!));
    }
}
