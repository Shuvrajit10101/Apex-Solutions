using System;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Apex.Desktop.Services;
using Xunit;

namespace Apex.Desktop.Tests;

/// <summary>
/// <b><c>CompanyStorage.Delete</c> is documented BEST-EFFORT</b> — "a file that cannot be removed is left in
/// place" — and for most of its life it delivered that only on Windows. It caught <see cref="IOException"/>
/// alone, which on Windows is very nearly complete because a file in use is how a delete is refused there. On
/// Linux and macOS the permission that decides whether a file can be unlinked sits on the PARENT DIRECTORY, and
/// a refusal arrives as <see cref="UnauthorizedAccessException"/> — which escaped, turning a documented
/// best-effort no-op into an unhandled crash on exactly the platforms where that refusal is the likely one.
///
/// <para>The widening shipped in <c>87624c2</c> <b>with no test at all</b>, and the whole Desktop suite stayed
/// green with it reverted — the method has no caller in <c>src/</c> yet, so nothing exercised it from any
/// direction. A cross-platform defect fix with no test is one refactor away from being silently undone, and
/// this file is that pin.</para>
///
/// <para><b>Why the behavioural half is Windows-only, stated rather than hidden.</b> The POSIX refusal needs a
/// directory whose write permission has been removed, and a CI runner with enough privilege ignores that
/// outright — a test that arms no trap and then asserts "it did not throw" is vacuous, and vacuous-but-green is
/// this project's documented doctored-test class. Windows CAN arm the exact escape cheaply and deterministically:
/// the ReadOnly attribute makes <c>File.Delete</c> raise <see cref="UnauthorizedAccessException"/>, the very
/// exception that used to escape. So the behavioural test ARMS AND PROVES the trap on Windows, and asserts the
/// no-throw contract on all three platforms. The catch list itself — the thing a refactor would narrow — is then
/// pinned structurally below, which holds on every platform because it is a fact about the code, not about a
/// filesystem this runner is allowed to lock.</para>
/// </summary>
public sealed class CompanyStorageDeleteTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "ApexCompanyStorageDeleteTests_" + Guid.NewGuid().ToString("N"));

    /// <summary>Clears any ReadOnly bit this fixture set before removing the tree — a left-behind read-only
    /// file makes the NEXT run's cleanup fail, which is how one guarded test poisons a whole CI leg.</summary>
    public void Dispose()
    {
        try
        {
            if (!Directory.Exists(_root)) return;
            foreach (var file in Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories))
            {
                try { File.SetAttributes(file, FileAttributes.Normal); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
                catch (ArgumentException) { }
            }
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private string WriteCompanyFile(string name)
    {
        Directory.CreateDirectory(_root);
        var path = Path.Combine(_root, name + ".db");
        File.WriteAllBytes(path, new byte[] { 0x53, 0x51, 0x4C, 0x69, 0x74, 0x65 });
        return path;
    }

    // ================================================================ 1. THE BEHAVIOUR

    [Fact]
    public void Delete_absorbs_the_refusal_the_os_raises_and_leaves_the_company_file_in_place()
    {
        var storage = new CompanyStorage(_root);
        var dbPath = WriteCompanyFile("Refused Co");
        var entry = new CompanyEntry("Refused Co", dbPath);

        File.SetAttributes(dbPath, FileAttributes.ReadOnly);
        try
        {
            if (OperatingSystem.IsWindows())
            {
                // ARM THE TRAP, AND PROVE IT IS ARMED. Without this the no-throw assertion below would be
                // vacuous — a Delete that simply succeeded would also "not throw". This asserts that the raw
                // File.Delete on this exact setup raises UnauthorizedAccessException: the precise exception
                // that Delete's old IOException-only catch list let escape to the caller.
                var escape = Assert.Throws<UnauthorizedAccessException>(() => File.Delete(dbPath));
                Assert.Contains(Path.GetFileName(dbPath), escape.Message, StringComparison.Ordinal);
            }
            // On Linux and macOS the ReadOnly bit is a chmod of the FILE, and unlink() consults the PARENT
            // DIRECTORY instead — so the delete goes through there and no trap can be armed this cheaply. The
            // contract assertion still runs; the catch list is pinned structurally in section 2.

            // THE CONTRACT, on every platform: best-effort means it absorbs the refusal instead of throwing.
            var thrown = Record.Exception(() => storage.Delete(entry));
            Assert.Null(thrown);

            if (OperatingSystem.IsWindows())
                Assert.True(File.Exists(dbPath),
                    "Delete swallowed the refusal but the company file is gone — 'left in place' is the other " +
                    "half of the contract, and losing a company silently is worse than declining loudly.");
        }
        finally
        {
            if (File.Exists(dbPath)) File.SetAttributes(dbPath, FileAttributes.Normal);
        }
    }

    [Fact]
    public void Delete_still_removes_a_company_file_nothing_is_holding()
    {
        // The guard against over-fixing: a Delete widened until it swallows everything, or gutted to a no-op,
        // passes the refusal test above. It must still actually delete.
        var storage = new CompanyStorage(_root);
        var dbPath = WriteCompanyFile("Removable Co");

        storage.Delete(new CompanyEntry("Removable Co", dbPath));

        Assert.False(File.Exists(dbPath), "Delete left behind a company file that nothing was holding.");
    }

    [Fact]
    public void Delete_of_a_company_whose_file_is_already_gone_is_a_silent_no_op()
    {
        var storage = new CompanyStorage(_root);
        Directory.CreateDirectory(_root);
        var entry = new CompanyEntry("Never Existed Co", Path.Combine(_root, "Never Existed Co.db"));

        Assert.Null(Record.Exception(() => storage.Delete(entry)));
    }

    // ================================================================ 2. THE CATCH LIST

    /// <summary>Resolves the repository root from THIS source file's location — the shape
    /// <c>CompanyCaptureReachTests</c> uses, so the scan needs no build-time copy step and no working
    /// directory.</summary>
    private static string RepoRoot([CallerFilePath] string thisFile = "")
        => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(thisFile)!, "..", ".."));

    [Theory]
    [InlineData("IOException")]                  // Windows: the file is in use
    [InlineData("UnauthorizedAccessException")]  // POSIX: an unwritable parent directory. THE ONE THAT ESCAPED.
    [InlineData("ArgumentException")]            // an unusable path is not holding a company file
    [InlineData("NotSupportedException")]
    public void Delete_catches_every_way_a_refusal_arrives_on_every_platform(string exceptionType)
    {
        // A STRUCTURAL assertion, and deliberately so. The POSIX manifestation of this defect cannot be
        // constructed on a CI runner (see the class remarks), so the behavioural test above pins it on Windows
        // only. This pins the catch list itself, which is the thing a refactor would narrow, and it holds on
        // all three platforms because it is a fact about the source text rather than about the filesystem.
        var source = Path.Combine(RepoRoot(), "src", "Apex.Desktop", "Services", "CompanyStorage.cs");
        Assert.True(File.Exists(source), $"CompanyStorage.cs not found at '{source}'.");
        var text = File.ReadAllText(source);

        var body = Regex.Match(
            text,
            @"public void Delete\(CompanyEntry entry\)(?<body>.*?)\r?\n    \}",
            RegexOptions.Singleline);
        Assert.True(body.Success,
            "Could not locate the body of CompanyStorage.Delete(CompanyEntry). If it was renamed or reshaped, " +
            "this guard must be re-aimed rather than deleted — it is the only cross-platform pin on the catch list.");

        Assert.True(
            Regex.IsMatch(body.Groups["body"].Value, @"catch\s*\(\s*" + Regex.Escape(exceptionType) + @"\s*\)"),
            $"CompanyStorage.Delete no longer catches {exceptionType}. It is documented best-effort — " +
            "'a file that cannot be removed is left in place' — and every entry in this list is a way an OS " +
            "refuses a delete. Narrowing it turns a documented no-op back into a crash, which is exactly the " +
            "defect 87624c2 fixed (UnauthorizedAccessException, on Linux and macOS).");
    }
}
