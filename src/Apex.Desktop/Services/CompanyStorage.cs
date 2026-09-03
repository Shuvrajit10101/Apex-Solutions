using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Apex.Ledger.Domain;
using Apex.Ledger.Io;
using Apex.Ledger.Persistence;
using Apex.Ledger.Reports;
using Apex.Ledger.Services;
using Apex.Persistence.Sqlite;

namespace Apex.Desktop.Services;

/// <summary>A discoverable company on disk: its display name and backing <c>.db</c> file.</summary>
public sealed record CompanyEntry(string Name, string DatabasePath);

/// <summary>
/// Manages the on-disk company store: a "Companies" folder holding one SQLite <c>.db</c> per
/// company (accounting-core §2). Lists existing companies, creates a fresh seeded company,
/// saves a company aggregate, and loads one back — all through <see cref="SqliteCompanyStore"/>.
/// </summary>
public sealed class CompanyStorage
{
    /// <summary>The folder all company <c>.db</c> files live under.</summary>
    public string CompaniesDirectory { get; }

    /// <summary>
    /// Creates a storage rooted at <paramref name="companiesDirectory"/>, or the default
    /// <c>%AppData%/ApexSolutions/Companies</c> (falling back to <c>./Companies</c> if AppData
    /// is unavailable). The directory is created if missing.
    /// </summary>
    public CompanyStorage(string? companiesDirectory = null)
    {
        CompaniesDirectory = companiesDirectory ?? DefaultDirectory();
        Directory.CreateDirectory(CompaniesDirectory);
    }

    private static string DefaultDirectory()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var root = string.IsNullOrWhiteSpace(appData)
            ? Path.Combine(AppContext.BaseDirectory, "Companies")
            : Path.Combine(appData, "ApexSolutions", "Companies");
        return root;
    }

    /// <summary>Lists the companies discoverable on disk (one per <c>.db</c> file), sorted by name.</summary>
    public IReadOnlyList<CompanyEntry> ListCompanies()
    {
        var result = new List<CompanyEntry>();
        if (!Directory.Exists(CompaniesDirectory))
            return result;

        foreach (var path in Directory.EnumerateFiles(CompaniesDirectory, "*.db"))
        {
            var name = Path.GetFileNameWithoutExtension(path);
            result.Add(new CompanyEntry(name, path));
        }
        return result.OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>
    /// The <c>.db</c> path a company of the given name maps to (name sanitised for the filename).
    /// <para><b>The mapping is NOT injective</b> — every character a filename cannot hold collapses to
    /// <c>_</c>, so "Acme/Traders" and "Acme_Traders" share one path. That is what
    /// <see cref="Exists(string)"/> exists to catch on the creation path, and why <see cref="Load"/> refuses a
    /// file that already holds more than one company row.</para>
    /// <para><b>🔴 WHICH characters collapse is PLATFORM-DEPENDENT, and the set is not close to the same
    /// size.</b> <c>Path.GetInvalidFileNameChars()</c> returns <b>41</b> characters on Windows (including
    /// <c>:</c> <c>*</c> <c>?</c> <c>"</c> <c>&lt;</c> <c>&gt;</c> <c>|</c> <c>\</c> and <c>/</c>) but exactly
    /// <b>two</b> on Linux and macOS — <c>'\0'</c> and <c>'/'</c>. So "Acme:Traders" collides with
    /// "Acme_Traders" on Windows and is simply a different file on Unix. That is correct rather than lossy:
    /// the sanitiser uses the platform's own invalid set and <see cref="Exists(string)"/> uses the platform's
    /// own namespace rules (<c>File.Exists</c>, which is case-insensitive on Windows and default APFS and
    /// case-sensitive on Linux), so the guard catches exactly the pairs that really do land on one file HERE.
    /// <c>'/'</c> is the only printable character invalid everywhere, which is why tests that need a
    /// guaranteed collision use it.</para>
    /// <para><b>The one case this does not cover</b> is a <c>.db</c> carried BETWEEN platforms: the stored
    /// company name is re-sanitised on every write, so a book created on Windows as "Acme:Traders" (file
    /// <c>Acme_Traders.db</c>) will, once opened on Linux, have its next save written to a brand-new
    /// <c>Acme:Traders.db</c>. Single-platform use cannot reach it and no test covers it.</para>
    /// </summary>
    public string PathForName(string companyName)
        => Path.Combine(CompaniesDirectory, SanitiseFileName(companyName) + ".db");

    /// <summary>
    /// True if a company with this name already has a <c>.db</c> file on disk. Tests the SANITISED path, so it
    /// answers "would creating this name land on an existing book" rather than "is this name taken".
    /// </summary>
    public bool Exists(string companyName) => File.Exists(PathForName(companyName));

    /// <summary>
    /// Persists a company aggregate to its <c>.db</c> file (create or replace).
    ///
    /// <para><b>THE DESKTOP LAYER'S ONE VALIDATION FLOOR.</b> <see cref="Company.EnsureValid"/> — the shared
    /// six-digit Indian PIN rule the recipient block has had since v45, plus the books-begin ≥ year-start
    /// invariant the constructor used to hold alone — is called here and nowhere else in the UI. Until the
    /// company profile screen shipped, <b>nothing in <c>src/</c> called it except the canonical import</b>,
    /// which was harmless only because no screen could write <see cref="Company.Pin"/>; the profile screen is
    /// what ends that, so the guard lands with it.</para>
    ///
    /// <para><b>Why HERE and not in the screen.</b> Re-measured 2026-08-17: every desktop write funnels through
    /// this method (<b>99</b> <c>_storage.Save(</c> call sites across <c>src/Apex.Desktop</c> — the raw grep
    /// count is 100 and one of those is this very sentence; the doc said 98, which was one short), and this
    /// class is the ONLY place in the desktop
    /// layer that so much as NAMES <see cref="SqliteCompanyStore"/> outside a comment. One call therefore
    /// covers all of them <b>including screens not yet written</b>. Putting it in the screen instead would
    /// cover exactly one path and rebuild, one layer up, the defect already recorded against
    /// <c>MasterGstDetails.EnsureValid</c> — reachable on one write path of five.
    /// <c>CompanyCaptureReachTests.Every_desktop_save_path_goes_through_the_one_guarded_store_opener</c> is
    /// what keeps the choke point a choke point.</para>
    ///
    /// <para><b>Deliberately NOT pushed down into <see cref="SqliteCompanyStore"/>.</b> That is one layer
    /// deeper and would also govern the engine and every test fixture — a wider blast radius than the
    /// evidence supports, on a class that carries no catch blocks of its own. Named as the stopping point
    /// rather than left unexplained.</para>
    ///
    /// <para>Screens pre-validate and show a friendly message first (the stock-item master's pattern); this
    /// throw is the backstop behind them. <c>SaveFailure.IsReportable</c> already lists
    /// <see cref="ArgumentException"/>, so a screen that wraps its save in the shared predicate reports it
    /// rather than crashing.</para>
    ///
    /// <para><b>🔴 THE ONE CARVE-OUT, STATED RATHER THAN IMPLIED: BACKUP RESTORE REPLACES THE WHOLE FILE.</b>
    /// <c>CompanyBackup.Restore</c> is a file-level swap of the <c>.db</c>, so it does not pass through this
    /// method and never could. <c>RestoreCompanyViewModel.Apply</c> therefore checks the restored aggregate
    /// itself: an archive holding a company that cannot be opened at all is ROLLED BACK from a pre-restore
    /// copy, and one that opens but carries a value this floor refuses is kept — recovery wins — and REPORTED
    /// on the panel. That is the whole of the exception: <b>every other desktop write is this method.</b> The
    /// reach test cannot see the restore path — it scans for store constructions — which is exactly why the
    /// carve-out is written here instead of being left for someone to rediscover.</para>
    ///
    /// <para><b>What this floor still cannot promise.</b> A <c>.db</c> that arrives already holding a bad PIN
    /// (edited by hand, or written by a build that predates the guard) loads without complaint — the loader
    /// deliberately does not re-validate, because refusing to OPEN a book is worse than refusing to save it —
    /// and then the next save on any screen throws. Most of this application's ~100 save sites are not wrapped
    /// in <c>SaveFailure</c>, so that surfaces as a crash rather than a message. The ingress routes are closed
    /// (this method, the canonical import, and the restore path all validate), so the residue is a
    /// hand-damaged file; it is recorded here rather than papered over.</para>
    /// </summary>
    public void Save(Company company)
    {
        company.EnsureValid();
        var path = PathForName(company.Name);
        using var store = new SqliteCompanyStore(path);
        store.Save(company);
    }

    /// <summary>
    /// Loads a company aggregate back from its <c>.db</c> file.
    ///
    /// <para><b>A file holding more than one company row is REFUSED, not silently narrowed to the first.</b>
    /// One file is one book; two rows means two different company names collapsed onto one sanitised filename
    /// and the second one's data is invisible to the loader. Returning <c>companies[0]</c> and carrying on is
    /// what made that condition undetectable — every later save landed on the first company while the operator
    /// believed they were editing the second. <see cref="Save"/> can no longer create the condition (creation
    /// refuses a colliding name), so this is for files that already carry it.</para>
    /// </summary>
    public Company Load(CompanyEntry entry)
    {
        using var store = new SqliteCompanyStore(entry.DatabasePath);
        // The company id is not encoded in the filename, so read the single stored row's id.
        var companies = store.ListCompanies();
        if (companies.Count == 0)
            throw new InvalidOperationException($"No company found in '{entry.DatabasePath}'.");
        if (companies.Count > 1)
            throw new InvalidOperationException(
                $"'{entry.DatabasePath}' holds {companies.Count} companies "
                + $"({string.Join(", ", companies.Select(c => "'" + c.Name + "'"))}). One file is one book; "
                + "two names that differ only in characters a filename cannot hold have been written into it, "
                + "and opening either one would hide the other.");
        var company = store.Load(companies[0].Id)
            ?? throw new InvalidOperationException($"Failed to load company from '{entry.DatabasePath}'.");

        // A seeded voucher-type shortcut that has since been CORRECTED is still stored verbatim on a company
        // created before the correction, and the Day-Book Alt+A picker renders that stored string — so the
        // company would be shown one key on its authored menu row and a different, LIVE-but-wrong key beside the
        // same type in the picker. Repair the superseded value on the way in (idempotent, predefined rows only);
        // it persists on the next save. This is the whole reason no v50 schema migration was cut for it.
        VoucherTypeResolver.RepairSupersededSeedShortcuts(company);
        return company;
    }

    /// <summary>
    /// Deletes a company's <c>.db</c> file. Best-effort; a file that cannot be removed is left in place.
    /// <para><b>The catch list must stay as wide as the ways a delete is refused, and those differ by
    /// platform.</b> On Windows a file in use raises <see cref="IOException"/>, which is all this used to
    /// catch. On Linux and macOS the deciding permission is on the PARENT DIRECTORY, and a refusal there
    /// arrives as <see cref="UnauthorizedAccessException"/> — which escaped, turning a method documented as
    /// best-effort into a crash on the platform where that refusal is most likely. The list now matches
    /// <c>CompanyBackup.SafeDelete</c>, which had it right.</para>
    /// </summary>
    public void Delete(CompanyEntry entry)
    {
        try
        {
            if (File.Exists(entry.DatabasePath))
                File.Delete(entry.DatabasePath);
        }
        catch (IOException) { /* file in use — leave it */ }
        catch (UnauthorizedAccessException) { /* read-only, or an unwritable parent directory on POSIX */ }
        catch (ArgumentException) { /* an unusable path is not holding a company file */ }
        catch (NotSupportedException) { /* ditto */ }
    }

    // =============================================================== RQ-8 Save View (per-company saved views)

    /// <summary>
    /// Saves (upserts) a report <paramref name="view"/> under <paramref name="name"/> for the company whose
    /// aggregate <paramref name="company"/> is (RQ-8). Opens the company's own <c>.db</c> transiently — the same
    /// backing store the report reads from — so a view is scoped to exactly this company's file (per-company
    /// isolation is intrinsic: another company is a different file). Config only; no figures are stored.
    /// </summary>
    public void SaveView(Company company, string name, SavedReportView view)
    {
        using var store = new SqliteCompanyStore(PathForName(company.Name));
        store.Save(company.Id, name, view);
    }

    /// <summary>Lists a company's saved report views, ordered by name (case-insensitive), or empty when none (RQ-8).</summary>
    public IReadOnlyList<SavedReportViewEntry> ListViews(Company company)
    {
        using var store = new SqliteCompanyStore(PathForName(company.Name));
        return store.List(company.Id);
    }

    /// <summary>Gets a company's saved report view of <paramref name="name"/> (case-insensitive), or null (RQ-8).</summary>
    public SavedReportView? GetView(Company company, string name)
    {
        using var store = new SqliteCompanyStore(PathForName(company.Name));
        return store.Get(company.Id, name);
    }

    /// <summary>Deletes a company's saved report view of <paramref name="name"/> (case-insensitive; no-op if absent) (RQ-8).</summary>
    public void DeleteView(Company company, string name)
    {
        using var store = new SqliteCompanyStore(PathForName(company.Name));
        store.Delete(company.Id, name);
    }

    // =============================================================== RQ-27 SMTP profile (per-company, capture-only)

    /// <summary>
    /// Saves (upserts) the company's capture-only <paramref name="profile"/> (host / port / TLS / from-address /
    /// from-name; RQ-27). Opens the company's own <c>.db</c> transiently — one profile per company file. There is
    /// deliberately NO password (R13); a credential (if ever) lives in the OS secret store, never the DB.
    /// </summary>
    public void SaveSmtpProfile(Company company, SmtpProfile profile)
    {
        using var store = new SqliteCompanyStore(PathForName(company.Name));
        store.SaveSmtpProfile(company.Id, profile);
    }

    /// <summary>Gets the company's SMTP profile, or <c>null</c> when none has been saved (RQ-27).</summary>
    public SmtpProfile? GetSmtpProfile(Company company)
    {
        using var store = new SqliteCompanyStore(PathForName(company.Name));
        return store.GetSmtpProfile(company.Id);
    }

    /// <summary>Deletes the company's SMTP profile (no-op if absent) (RQ-27).</summary>
    public void DeleteSmtpProfile(Company company)
    {
        using var store = new SqliteCompanyStore(PathForName(company.Name));
        store.DeleteSmtpProfile(company.Id);
    }

    private static string SanitiseFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray()).Trim();
        return string.IsNullOrEmpty(cleaned) ? "Company" : cleaned;
    }
}
