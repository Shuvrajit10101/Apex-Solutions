using System;
using System.IO;
using System.Linq;
using Apex.Desktop.Services;
using Xunit;

namespace Apex.Desktop.Tests;

/// <summary>
/// 🔴 <b>A FILED STATUTORY RETURN MUST NEVER LAND IN THE PROCESS WORKING DIRECTORY.</b>
///
/// <para><b>The defect, and how it was found.</b> Seventeen export surfaces — Form 16 / 16A / 24Q / 26Q / 27A /
/// 27D / 27EQ, the ESI contribution register, the PF ECR, the PT register, e-invoice and e-way-bill JSON, report
/// export, data export, backup, and the two Save-to-Documents accelerators — each seeded their destination with a
/// bare <c>Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)</c>. The .NET contract for that method
/// is explicit that it <b>returns the empty string when the folder cannot be located</b>, and on a Linux CI
/// container with no <c>HOME</c> that is exactly what happens. An empty folder makes
/// <c>Path.Combine(folder, name)</c> collapse to a bare file name, so <c>File.WriteAllBytes</c> drops a FILED
/// GST return, or a TDS certificate, beside the executable with no picker and no way for the operator to find it
/// again. That is a data-loss defect on any operating system; Linux is merely where it is reproducible.</para>
///
/// <para><b>Authority for the new expectation.</b> Not a vendor page — this is a platform contract. Microsoft
/// documents <c>Environment.GetFolderPath</c> as returning <c>String.Empty</c> if the system does not have the
/// folder, and documents <c>Path.GetTempPath</c> as always returning a path (it falls back to the OS temp root:
/// <c>/tmp</c> on Unix, <c>%TEMP%</c> on Windows). So the invariant that can be asserted on every OS is not
/// "the default IS My Documents" — that is unprovable off Windows — but <b>"the default is a non-empty, ROOTED
/// path"</b>, with My Documents preferred whenever the platform supplies it.</para>
///
/// <para><b>Why the probes are injected.</b> The failure only happens where <c>MyDocuments</c> resolves empty,
/// which never occurs on this development machine — so a test that just called the real thing would be green
/// here and red on CI, which is precisely the bug it is meant to catch. The seam takes the two folder lookups and
/// the temp root as delegates, so the Linux-CI state is reproduced deterministically anywhere.</para>
/// </summary>
public sealed class ExportFolderDefaultTests
{
    [Fact]
    public void My_documents_is_preferred_when_the_platform_supplies_it()
    {
        var docs = Path.Combine(Path.GetTempPath(), "ApexDocsProbe");
        Assert.Equal(docs, ExportFolderDefault.Resolve(() => docs, () => "/home/x", () => "/tmp"));
    }

    /// <summary>🔴 THE LINUX-CI CASE. My Documents resolves EMPTY; the user profile carries the answer.</summary>
    [Fact]
    public void An_empty_my_documents_falls_through_to_the_user_profile()
    {
        var home = Path.Combine(Path.GetTempPath(), "ApexHomeProbe");
        Assert.Equal(home, ExportFolderDefault.Resolve(() => "", () => home, () => "/tmp"));
    }

    /// <summary>Whitespace is treated as absent — a folder of spaces makes <c>Path.Combine</c> produce a path no
    /// operator will ever find, which is the very failure this guards.</summary>
    [Fact]
    public void Whitespace_counts_as_absent_on_both_folder_lookups()
    {
        var temp = Path.GetTempPath();
        Assert.Equal(temp, ExportFolderDefault.Resolve(() => "   ", () => "\t", () => temp));
    }

    /// <summary>Both folder lookups empty — the temp root is the last resort, and it is a real, rooted, writable
    /// directory on every supported OS. A findable wrong folder beats an invisible one.</summary>
    [Fact]
    public void Both_lookups_empty_falls_through_to_the_temp_root()
    {
        var temp = Path.GetTempPath();
        var resolved = ExportFolderDefault.Resolve(() => "", () => "", () => temp);
        Assert.Equal(temp, resolved);
        Assert.True(Path.IsPathRooted(resolved));
    }

    /// <summary>A lookup that THROWS (a sandboxed or trimmed platform) is treated exactly as one that returns
    /// empty — the original 17 call sites each wrapped their own <c>try/catch</c> and each swallowed to
    /// <c>string.Empty</c>, which is the state that caused the defect.</summary>
    [Fact]
    public void A_throwing_lookup_is_treated_as_absent_rather_than_propagating()
    {
        var temp = Path.GetTempPath();
        var resolved = ExportFolderDefault.Resolve(
            () => throw new PlatformNotSupportedException(),
            () => throw new InvalidOperationException(),
            () => temp);
        Assert.Equal(temp, resolved);
    }

    /// <summary>The real, uninjected entry point must answer a non-empty ROOTED path on whatever OS this is
    /// running on. This is the assertion the shipped code has to satisfy, and it is OS-agnostic by construction.
    /// </summary>
    [Fact]
    public void The_real_default_is_always_a_non_empty_rooted_path()
    {
        var folder = ExportFolderDefault.Resolve();
        Assert.False(string.IsNullOrWhiteSpace(folder));
        Assert.True(Path.IsPathRooted(folder), $"'{folder}' is not a rooted path");
    }

    /// <summary>
    /// 🔴 THE COMPLETENESS HALF. No production file may reach for <c>SpecialFolder.MyDocuments</c> directly any
    /// more: one seam, one fallback chain, one place to correct. Derived by scanning <c>src/</c> rather than
    /// listed, so a seventeenth site added tomorrow fails here on the day it is added.
    /// </summary>
    [Fact]
    public void No_production_file_reaches_for_MyDocuments_outside_the_shared_seam()
    {
        var root = RepoRoot();
        var offenders = Directory
            .EnumerateFiles(Path.Combine(root, "src"), "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                     && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            .Where(f => !string.Equals(Path.GetFileName(f), "ExportFolderDefault.cs", StringComparison.Ordinal))
            .Where(f => File.ReadAllText(f).Contains("SpecialFolder.MyDocuments", StringComparison.Ordinal))
            .Select(f => Path.GetRelativePath(root, f))
            .OrderBy(f => f, StringComparer.Ordinal)
            .ToList();

        Assert.True(offenders.Count == 0,
            "These files still resolve My Documents themselves instead of through ExportFolderDefault, so each "
            + "keeps its own empty-string failure mode:\n  " + string.Join("\n  ", offenders));
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Apex.slnx")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }
}
