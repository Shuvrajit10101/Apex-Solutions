using System;
using System.IO;

namespace Apex.Desktop.Services;

/// <summary>
/// <b>The one place this application decides where an export, a certificate, a return or a backup goes by
/// default</b> — and the one place that guarantees the answer is never the process working directory.
///
/// <para>🔴 <b>THE DEFECT THIS REPLACES, MEASURED.</b> Seventeen surfaces each open-coded
/// <c>try { Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments); } catch { string.Empty; }</c>.
/// Microsoft documents that method as returning <b><c>String.Empty</c> when the system does not have the
/// folder</b> — and a Linux CI container with no <c>HOME</c> is exactly such a system. With the folder empty,
/// <c>Path.Combine(folder, name)</c> collapses to a bare file name and <c>File.WriteAllBytes</c> writes beside the
/// executable: a FILED GST return, a TDS certificate or a company backup, with no picker and no way for the
/// operator to find it again. The seventeen <c>catch</c> blocks made it worse rather than better — each converted
/// a diagnosable failure into a silent one.</para>
///
/// <para><b>The chain, and why each rung is there.</b>
/// <list type="number">
///   <item><b>My Documents</b> — the intended destination, and what every one of the seventeen sites meant.</item>
///   <item><b>The user profile</b> — on Unix this is <c>$HOME</c>, which resolves in many environments where the
///     Documents sub-folder does not (no XDG user-dirs configuration). A findable home directory beats an
///     invisible working directory.</item>
///   <item><b>The temp root</b> — <c>Path.GetTempPath</c> is documented to always return a path, and it is rooted
///     and writable on every supported OS (<c>/tmp</c>, <c>%TEMP%</c>). It is the wrong folder in the sense that
///     the operator did not choose it; it is the RIGHT answer in the sense that they can be told where the file
///     went, which is the property the working directory does not have.</item>
/// </list></para>
///
/// <para><b>What this is NOT.</b> It is not a substitute for asking the operator. Every calling screen still shows
/// the folder in an editable box and every one of them should eventually offer a picker (census 13.10 records that
/// no folder picker exists anywhere in this application). This only fixes the DEFAULT, so that a screen the
/// operator never touched cannot write somewhere unfindable.</para>
/// </summary>
public static class ExportFolderDefault
{
    /// <summary>
    /// The default export/save folder for this machine: My Documents when the platform supplies it, else the user
    /// profile, else the temp root. Never empty, and always a rooted path.
    /// </summary>
    public static string Resolve() => Resolve(
        () => Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        () => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        Path.GetTempPath);

    /// <summary>
    /// The seam the tests drive. The three lookups are injected because the failure only occurs where
    /// <paramref name="myDocuments"/> answers empty — which never happens on a Windows development machine, so a
    /// test that called the real thing would be green here and red on CI, which is the bug rather than a test of
    /// it. A lookup that THROWS is treated exactly as one that answers empty.
    /// </summary>
    public static string Resolve(Func<string> myDocuments, Func<string> userProfile, Func<string> tempRoot)
    {
        if (Try(myDocuments) is { } docs) return docs;
        if (Try(userProfile) is { } home) return home;

        // Unreachable by the documented contract of Path.GetTempPath, which always returns a path. Kept anyway,
        // and deliberately NOT an empty string: the current directory is at least ROOTED, so it renders in the
        // screen's folder box and can be reported back to the operator. An empty string is the one answer that
        // cannot be — it makes Path.Combine produce a relative name and the file vanishes silently. That is the
        // entire difference between this last rung and the defect the whole class replaces.
        return Try(tempRoot) ?? Directory.GetCurrentDirectory();
    }

    /// <summary>The candidate, or <c>null</c> when it is absent — blank, whitespace, or a throwing lookup.
    /// Whitespace counts as absent: a folder of spaces produces a path no operator will ever find.</summary>
    private static string? Try(Func<string> lookup)
    {
        try
        {
            var value = lookup();
            return string.IsNullOrWhiteSpace(value) ? null : value;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
        {
            return null;
        }
    }
}
