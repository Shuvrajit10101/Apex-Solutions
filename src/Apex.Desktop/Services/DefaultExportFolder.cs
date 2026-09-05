using System;
using System.IO;

namespace Apex.Desktop.Services;

/// <summary>
/// The ONE place the app decides where a generated file goes when the user has not picked a folder —
/// statutory returns (Form 16/16A/24Q/26Q/27A/27D/27EQ, GST offline returns), payroll filings (PF ECR,
/// ESI, Professional Tax), e-invoice / e-way-bill payloads, report exports, saved print previews and
/// e-mails, and the company backup.
///
/// <para><b>The guarantee, which is the whole reason this type exists:</b> the returned path is never
/// empty, never whitespace, and is never derived from the process working directory or
/// <see cref="AppContext.BaseDirectory"/>. An empty seed is not a cosmetic default — it makes the path
/// handed to the writer a BARE FILE NAME, so a filed GST return or a company backup is written beside the
/// executable, with no picker shown and no way for the user to learn where it went.</para>
///
/// <para><b>Why a ladder rather than My Documents alone.</b> On Linux and macOS
/// <see cref="Environment.GetFolderPath(Environment.SpecialFolder)"/> resolves
/// <see cref="Environment.SpecialFolder.MyDocuments"/> from the XDG user-directory configuration, and it
/// returns the EMPTY STRING when that configuration is absent — a bare container, a service account, a CI
/// runner. Nothing throws; the caller simply gets <c>""</c>. That is exactly how this defect reached CI:
/// every call site wrapped the lookup in <c>try/catch</c>, and the failure mode is not an exception.
/// <see cref="Environment.SpecialFolder.Personal"/> is NOT a fallback — .NET maps it to the same value as
/// <see cref="Environment.SpecialFolder.MyDocuments"/>.</para>
///
/// <para>The rungs, in order: My Documents → the user profile (<c>$HOME</c> / <c>%USERPROFILE%</c>) → the
/// platform temp folder. Every rung is guarded, so a lookup that throws falls through to the next one
/// instead of propagating — the historical swallow-the-exception behaviour is kept at the boundary, but it
/// can no longer produce an empty result.</para>
/// </summary>
public static class DefaultExportFolder
{
    /// <summary>
    /// The default destination for a generated file. Never empty, never whitespace, never the process
    /// working directory.
    /// </summary>
    public static string Resolve() => Resolve(Environment.GetFolderPath, Path.GetTempPath);

    /// <summary>
    /// The seam the tests drive. Both lookups are injected so a test can express the "My Documents resolves
    /// to the empty string" platform without mutating process-wide environment state — an env-var mutation
    /// would leak into whatever xUnit runs beside it in parallel.
    /// </summary>
    internal static string Resolve(Func<Environment.SpecialFolder, string> folderPath, Func<string> tempPath)
    {
        ArgumentNullException.ThrowIfNull(folderPath);
        ArgumentNullException.ThrowIfNull(tempPath);

        var documents = Lookup(folderPath, Environment.SpecialFolder.MyDocuments);
        if (documents.Length > 0) return documents;

        var profile = Lookup(folderPath, Environment.SpecialFolder.UserProfile);
        if (profile.Length > 0) return profile;

        try
        {
            var temp = tempPath();
            if (!string.IsNullOrWhiteSpace(temp)) return temp;
        }
        catch
        {
            // Fall through to the last resort — never out, and never to the working directory.
        }

        return LastResort;
    }

    /// <summary>
    /// Reached only if all three rungs come back blank. <see cref="Path.GetTempPath"/> is documented to be
    /// non-empty on every platform we ship on, so this is unreachable in the product; it exists because the
    /// injected seam above makes a blank temp folder expressible, and returning <c>""</c> from there would
    /// re-open the very defect this type closes. A rooted, platform-appropriate constant — deliberately not
    /// anything derived from the current directory.
    ///
    /// <para>The Windows arm is spelled with an escaped backslash rather than a verbatim literal on purpose:
    /// <c>CompanyCaptureReachTests.BlankComments</c> scans every file under <c>src/Apex.Desktop</c> and cannot
    /// parse verbatim or raw literals, so introducing one there would blind a different guard.</para>
    /// </summary>
    internal static string LastResort => OperatingSystem.IsWindows() ? "C:\\Temp" : "/tmp";

    /// <summary>
    /// One guarded rung. Returns <see cref="string.Empty"/> — never null, never whitespace — when the lookup
    /// yields nothing or throws, so the caller's <c>Length > 0</c> test is the single decision point.
    /// </summary>
    private static string Lookup(Func<Environment.SpecialFolder, string> folderPath, Environment.SpecialFolder folder)
    {
        try
        {
            var path = folderPath(folder);
            return string.IsNullOrWhiteSpace(path) ? string.Empty : path;
        }
        catch
        {
            return string.Empty;
        }
    }
}
