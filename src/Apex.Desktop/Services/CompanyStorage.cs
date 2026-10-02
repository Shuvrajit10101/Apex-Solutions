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

/// <summary>
/// A discoverable company on disk: its display name and backing <c>.db</c> file.
///
/// <para>🔴 <b><see cref="IsVaulted"/> and <see cref="Number"/> arrived with census row 16.1.</b> For a
/// VAULTED company <see cref="Name"/> is <c>CompanyVault.MaskedName</c> — a row of asterisks — because the
/// real name is inside the encrypted file and is not knowable until a passphrase is supplied. That is the
/// vendor's own behaviour (<i>"displayed with a series of asterisks, while the company number remains
/// visible"</i>) and it is also the only honest thing this record COULD carry. <see cref="Number"/> is the
/// registry's stable company number, which is what an operator has to tell two vaulted books apart.</para>
///
/// <para><b>Both members are defaulted</b> so the ~150 fixtures and call sites that construct a
/// <c>CompanyEntry</c> from a name and a path keep compiling and keep meaning exactly what they meant: a
/// plain, unvaulted book.</para>
/// </summary>
public sealed record CompanyEntry(string Name, string DatabasePath, bool IsVaulted = false, int Number = 0);

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
        Registry = new CompanyRegistry(CompaniesDirectory);
    }

    /// <summary>
    /// 🔴 <b>The company registry (census 16.1) — what carries a company's identity now that the filename no
    /// longer can.</b> See <see cref="CompanyRegistry"/> for why it had to exist and exactly what an attacker
    /// holding the disk can read out of it.
    /// </summary>
    public CompanyRegistry Registry { get; }

    // =============================================================== 16.1 the open-book session
    //
    // 🔴 WHY THIS FIELD EXISTS, AND WHY THE VAULT COULD NOT SHIP WITHOUT IT.
    // Every write path in this class re-derives the file from the company's NAME (`PathForName`). For a plain
    // book that is fine and has always been fine. For a VAULTED book it is wrong twice over: the file is named
    // after a GUID rather than the company, so the derivation points at a file that does not exist and the next
    // Save would write a brand-new PLAINTEXT book beside the vault — silently un-vaulting the company and
    // leaking its name into a filename. And even the right path cannot be opened without the passphrase.
    // So opening a vaulted company REGISTERS the resolved path and the passphrase here for the life of the
    // session, and every opener below consults it first. `Load` sets it; `ShutCompany` clears it.
    private string? _openVaultPath;
    private string? _openVaultPassphrase;

    /// <summary>True while a vaulted company is open and its passphrase is held for this session.</summary>
    public bool HasOpenVault => _openVaultPath is not null;

    /// <summary>
    /// Forgets the open vaulted book's passphrase. Called when a company is shut, so the passphrase does not
    /// outlive the book it opens. (It lives in managed memory while held; this application does not pin or
    /// zero it, which is recorded as a limit rather than claimed as a guarantee.)
    /// </summary>
    public void CloseVault()
    {
        _openVaultPath = null;
        _openVaultPassphrase = null;
    }

    /// <summary>
    /// Opens a store for <paramref name="company"/>, using the open vault's path and passphrase when this
    /// company IS the open vaulted book, and the name-derived plain path otherwise. Every write method below
    /// goes through here — that is what keeps "which file, and with what key" one decision in one place.
    /// </summary>
    private SqliteCompanyStore OpenFor(Company company)
        => _openVaultPath is not null
            ? new SqliteCompanyStore(_openVaultPath, _openVaultPassphrase)
            : new SqliteCompanyStore(PathForName(company.Name));

    private static string DefaultDirectory()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var root = string.IsNullOrWhiteSpace(appData)
            ? Path.Combine(AppContext.BaseDirectory, "Companies")
            : Path.Combine(appData, "ApexSolutions", "Companies");
        return root;
    }

    /// <summary>
    /// Lists the companies discoverable on disk.
    ///
    /// <para>🔴 <b>THIS USED TO BE THE LEAK, AND THE CHANGE IS THE POINT OF CENSUS ROW 16.1.</b> It read
    /// <c>Directory.EnumerateFiles(…, "*.db")</c> and took every company's display name straight out of its
    /// FILENAME. The vault encrypts a company's name along with its ledger, so on the old scheme a vaulted
    /// company could only either appear under its plaintext name — defeating the feature outright — or vanish
    /// from the list and become unopenable. It now reads <see cref="CompanyRegistry"/>, which maps files to
    /// identities and holds NO name for a vaulted book.</para>
    ///
    /// <para><b>Nothing changes for a book that is not vaulted.</b> The registry adopts every plain <c>.db</c>
    /// it finds on first run, recording exactly what the filename already said, so an existing installation
    /// lists the same companies with the same names and no migration step.</para>
    ///
    /// <para><b>The order is by company NUMBER, not by name</b> — a vaulted company has no name to sort on,
    /// and the number is what the vendor keeps visible for exactly that reason. For an installation that has
    /// never used the vault this is adoption order, which is the old alphabetical order.</para>
    /// </summary>
    public IReadOnlyList<CompanyEntry> ListCompanies()
    {
        if (!Directory.Exists(CompaniesDirectory))
            return new List<CompanyEntry>();

        return Registry.Load()
            .Select(r => new CompanyEntry(r.DisplayName, Registry.PathOf(r), r.IsVaulted, r.Number))
            .ToList();
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
    /// <remarks>
    /// 🔴 <b>Census 16.1 — the path is no longer ALWAYS derived from the name.</b> When a vaulted company is
    /// open, <see cref="OpenFor"/> writes to the registered opaque file with the session's passphrase. Without
    /// that, this method would happily write a brand-new PLAINTEXT book named after the company beside the
    /// vault, un-vaulting it on the first save and putting the hidden name back into a filename. The plain
    /// path is untouched and is still the whole behaviour for every unvaulted book.
    /// </remarks>
    public void Save(Company company)
    {
        company.EnsureValid();
        using var store = OpenFor(company);
        store.Save(company);

        // Keep the registry's display name in step for a PLAIN book, so a name changed on the profile screen
        // shows in Company Select without waiting for a rename. A vaulted book stores no name, by design.
        if (!HasOpenVault)
            Registry.RegisterPlain(company.Name, Path.GetFileName(PathForName(company.Name)));
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
    /// <param name="passphrase">
    /// 🔴 Census 16.1 — required when <c>entry.IsVaulted</c>, ignored otherwise. On success the path and
    /// passphrase are REGISTERED as this session's open vault (see <see cref="OpenFor"/>), so every later save
    /// lands back in the encrypted file instead of writing a plaintext copy. A wrong passphrase surfaces as
    /// <see cref="InvalidOperationException"/> with a message a screen can show, rather than as the raw
    /// <c>SqliteException</c> "file is not a database" the provider raises.
    /// </param>
    public Company Load(CompanyEntry entry, string? passphrase = null)
    {
        ArgumentNullException.ThrowIfNull(entry);

        if (entry.IsVaulted)
        {
            if (string.IsNullOrEmpty(passphrase))
                throw new InvalidOperationException(
                    $"This company is in the {CompanyVault.FeatureName} and cannot be opened without its passphrase.");
            if (!CompanyVault.TryOpen(entry.DatabasePath, passphrase))
                throw new InvalidOperationException(
                    "That passphrase does not open this company. The passphrase is not stored anywhere, so it "
                    + "cannot be looked up or reset.");
        }

        var company = LoadCore(entry, entry.IsVaulted ? passphrase : null);

        if (entry.IsVaulted)
        {
            _openVaultPath = entry.DatabasePath;
            _openVaultPassphrase = passphrase;
        }
        else
        {
            CloseVault();
        }

        return company;
    }

    private Company LoadCore(CompanyEntry entry, string? passphrase)
    {
        using var store = new SqliteCompanyStore(entry.DatabasePath, passphrase);
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
    /// 🔴 <b>Census row 1.4 — RENAMES a company: rewrites the stored name AND moves its <c>.db</c>.</b>
    /// Returns the new <see cref="CompanyEntry"/>.
    ///
    /// <para><b>Its ONE caller is <c>CompanyProfileViewModel.TryRename</c>, reached by editing the Name on the
    /// Company Alteration screen and accepting.</b> That is stated because this method shipped in the W2-18 WIP
    /// commit with <b>zero callers anywhere</b> — written, careful, correct-looking and unreachable by any
    /// sequence of keys, which is this project's most-repeated defect shape. If a refactor ever leaves it
    /// callerless again, row 1.4 is ABSENT whatever the census says.</para>
    ///
    /// <para><b>Both halves are mandatory, and doing only one of them is the book-eater this method was carved
    /// out for.</b> <c>CompanyProfileViewModel.IsNameEditable</c> states the trap in full: the file path is
    /// derived from the NAME by <see cref="PathForName"/>, and <see cref="ListCompanies"/> takes each DISPLAY
    /// name back out of the FILENAME. So a "rename" that only rewrote <c>Company.Name</c> and saved would write
    /// a brand-new <c>.db</c> at the new name and leave the old file standing — two rows in Company Select
    /// carrying one company id, every later save landing on only one of them, and nothing reporting an error.
    /// That is why this is a storage operation and not a field edit.</para>
    ///
    /// <para><b>The order is deliberate: refuse, then load, then save the NEW file, then remove the OLD one.</b>
    /// If the save throws, the old file is still there and the book is intact; the worst reachable outcome is a
    /// stray new file beside the original, which <see cref="ListCompanies"/> shows and the operator can delete.
    /// Removing the old file first and then failing to write the new one would lose the company outright.</para>
    ///
    /// <para><b>Two refusals, both before anything is touched.</b> A blank/whitespace name is refused because
    /// <see cref="SanitiseFileName"/> would silently fall back to the literal "Company"; a name whose SANITISED
    /// path already holds a book is refused because the move would overwrite another company's file. The second
    /// check is <see cref="Exists(string)"/> — the same platform-aware predicate creation uses — so it catches
    /// exactly the pairs that really do collapse onto one file on THIS platform, including the non-injective
    /// cases <see cref="PathForName"/> documents.</para>
    ///
    /// <para><b>The same-path case is a rename, not a no-op, and it is handled rather than refused.</b>
    /// "Acme" → "Acme " and (on Windows) "Acme:Ltd" → "Acme_Ltd" sanitise to the file that is already open, so
    /// there is nothing to move — but the STORED name still changes, and that is the name every printed
    /// document and every report header reads. It is written and the file is left where it is; the collision
    /// refusal deliberately does not fire on the entry's own path, or renaming a book's punctuation would be
    /// impossible.</para>
    /// </summary>
    public CompanyEntry Rename(CompanyEntry entry, string newName)
    {
        ArgumentNullException.ThrowIfNull(entry);

        var trimmed = (newName ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
            throw new ArgumentException("A company name is required.", nameof(newName));

        var newPath = PathForName(trimmed);
        var samePath = string.Equals(newPath, entry.DatabasePath, StringComparison.OrdinalIgnoreCase);

        if (!samePath && File.Exists(newPath))
            throw new InvalidOperationException(
                $"A company book already exists for '{trimmed}'. Renaming onto it would overwrite that book, "
                + "so the rename is refused. Choose a different name.");

        if (entry.IsVaulted)
            throw new InvalidOperationException(
                $"A company in the {CompanyVault.FeatureName} is renamed by opening it and editing its name; its "
                + "file is not named after it. Rename here would move a book that has no name on disk.");

        var company = Load(entry);
        company.Name = trimmed;

        // 🔴 Census 16.1 — the file is written DIRECTLY rather than through Save(), and the registry row is
        // MOVED rather than re-registered. Save() now also registers the company, and a register on a filename
        // the registry has never seen INSERTS a row — so routing a rename through it would leave the book
        // listed twice, under both names, with the old row pointing at a file this method is about to delete.
        // That is the same two-rows-one-company book-eater the method remarks describe, arrived at from the
        // registry side instead of the filename side.
        SaveTo(company, newPath);

        // One UPDATE, so the row never names a file that has not been written. A crash between the write and
        // here leaves a stray new file beside the original exactly as it did before the registry existed —
        // ListCompanies adopts it and the operator can delete it; the original is untouched either way.
        Registry.RecordRename(Path.GetFileName(entry.DatabasePath), Path.GetFileName(newPath), trimmed);

        if (!samePath)
            Delete(entry);

        return new CompanyEntry(trimmed, newPath, IsVaulted: false, entry.Number);
    }

    /// <summary>Writes a company to an explicit path with the same validation floor as <see cref="Save"/>, and
    /// no registry side effect. The rename path's writer — see its remarks.</summary>
    private static void SaveTo(Company company, string path)
    {
        company.EnsureValid();
        using var store = new SqliteCompanyStore(path);
        store.Save(company);
    }

    /// <summary>
    /// Deletes a company's <c>.db</c> file. Best-effort; a file that cannot be removed is left in place.
    ///
    /// <para>🔴 <b>BECAUSE IT IS BEST-EFFORT, A CALLER MUST NOT ANNOUNCE SUCCESS OFF ITS RETURN.</b> It returns
    /// identically whether the file went or an <c>IOException</c>/<c>UnauthorizedAccessException</c> was caught
    /// below, so <c>MainWindowViewModel.PerformOpenCompanyDeletion</c> re-tests <c>File.Exists</c> before it tells
    /// the operator the book is gone and releases the aggregate. It had <b>zero production callers</b> until
    /// census row 1.4 shipped Alt+D on the Company Alteration screen (2026-09-05); it is now reached from there and
    /// from <see cref="Rename"/>'s move.</para>
    ///
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
            // 🔴 Census 16.1 — the registry row goes with the file. Without this the deleted company keeps its
            // row and stays in the list pointing at nothing; and for a VAULTED book the row is the only record
            // that the opaque file ever belonged to a company, so leaving it would be worse than useless.
            // Pools are cleared for the same reason CompanyRegistry.SafeDelete clears them: a pooled connection
            // still holds the OS file handle on Windows and the delete would silently fail.
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (File.Exists(entry.DatabasePath))
                File.Delete(entry.DatabasePath);
            if (!File.Exists(entry.DatabasePath))
                Registry.Forget(Path.GetFileName(entry.DatabasePath));
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
        using var store = OpenFor(company);
        store.Save(company.Id, name, view);
    }

    /// <summary>Lists a company's saved report views, ordered by name (case-insensitive), or empty when none (RQ-8).</summary>
    public IReadOnlyList<SavedReportViewEntry> ListViews(Company company)
    {
        using var store = OpenFor(company);
        return store.List(company.Id);
    }

    /// <summary>Gets a company's saved report view of <paramref name="name"/> (case-insensitive), or null (RQ-8).</summary>
    public SavedReportView? GetView(Company company, string name)
    {
        using var store = OpenFor(company);
        return store.Get(company.Id, name);
    }

    /// <summary>Deletes a company's saved report view of <paramref name="name"/> (case-insensitive; no-op if absent) (RQ-8).</summary>
    public void DeleteView(Company company, string name)
    {
        using var store = OpenFor(company);
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
        using var store = OpenFor(company);
        store.SaveSmtpProfile(company.Id, profile);
    }

    /// <summary>Gets the company's SMTP profile, or <c>null</c> when none has been saved (RQ-27).</summary>
    public SmtpProfile? GetSmtpProfile(Company company)
    {
        using var store = OpenFor(company);
        return store.GetSmtpProfile(company.Id);
    }

    /// <summary>Deletes the company's SMTP profile (no-op if absent) (RQ-27).</summary>
    public void DeleteSmtpProfile(Company company)
    {
        using var store = OpenFor(company);
        store.DeleteSmtpProfile(company.Id);
    }

    // =============================================================== 16.1 the Data Vault (census row 16.1)

    /// <summary>
    /// 🔴 <b>Puts the OPEN company into the vault.</b> Encrypts its book to an opaque filename, switches the
    /// registry over under its journal, deletes the plaintext, and leaves the company open on the new file so
    /// the operator can carry on working. Returns the vaulted entry.
    ///
    /// <para><b>The company is SAVED FIRST.</b> Anything keyed since the last save lives in the aggregate, not
    /// in the file; encrypting the file without flushing would put a stale book in the vault and throw the
    /// newer one away when the plaintext is deleted.</para>
    ///
    /// <para><b>On failure nothing has changed</b> — <c>CompanyRegistry.Enable</c> writes and verifies the new
    /// file before it touches the registry, and unwinds its own journal — so the company is still open, still
    /// plaintext, and the operator can try again.</para>
    /// </summary>
    public CompanyEntry EnableVault(CompanyEntry entry, Company company, string passphrase)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(company);
        if (entry.IsVaulted)
            throw new InvalidOperationException($"This company is already in the {CompanyVault.FeatureName}.");

        Save(company);
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

        var record = Find(entry)
            ?? throw new InvalidOperationException(
                "This company is not in the registry, so there is nothing to move into the vault.");

        var vaulted = Registry.Enable(record, passphrase);

        _openVaultPath = Registry.PathOf(vaulted);
        _openVaultPassphrase = passphrase;

        return new CompanyEntry(vaulted.DisplayName, _openVaultPath, IsVaulted: true, vaulted.Number);
    }

    /// <summary>
    /// Takes the OPEN company back out of the vault, restoring a plain book named after it again. Refuses a
    /// passphrase that does not open the book, and refuses when the plain name would land on another company's
    /// file — the same collision refusal creation and rename use.
    /// </summary>
    public CompanyEntry DisableVault(CompanyEntry entry, Company company, string passphrase)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(company);
        if (!entry.IsVaulted)
            throw new InvalidOperationException($"This company is not in the {CompanyVault.FeatureName}.");
        if (!CompanyVault.TryOpen(entry.DatabasePath, passphrase))
            throw new InvalidOperationException("That passphrase does not open this company.");

        var plainPath = PathForName(company.Name);
        if (File.Exists(plainPath))
            throw new InvalidOperationException(
                $"A company book already exists for '{company.Name}'. Leaving the {CompanyVault.FeatureName} "
                + "would write over it, so it is refused. Rename this company first.");

        Save(company);
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

        var record = Find(entry)
            ?? throw new InvalidOperationException("This company is not in the registry.");

        var plain = Registry.Disable(record, passphrase, Path.GetFileName(plainPath), company.Name);
        CloseVault();
        return new CompanyEntry(plain.DisplayName, Registry.PathOf(plain), IsVaulted: false, plain.Number);
    }

    /// <summary>
    /// Changes the open vaulted company's passphrase. Refuses the wrong old passphrase. The book is rewritten
    /// under the new one and the session's held passphrase is replaced, so work continues uninterrupted.
    /// </summary>
    public CompanyEntry ChangeVaultPassphrase(CompanyEntry entry, string oldPassphrase, string newPassphrase)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (!entry.IsVaulted)
            throw new InvalidOperationException($"This company is not in the {CompanyVault.FeatureName}.");
        if (!CompanyVault.TryOpen(entry.DatabasePath, oldPassphrase))
            throw new InvalidOperationException("The current passphrase is not correct.");

        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

        var record = Find(entry)
            ?? throw new InvalidOperationException("This company is not in the registry.");

        var rekeyed = Registry.ChangePassphrase(record, oldPassphrase, newPassphrase);
        _openVaultPath = Registry.PathOf(rekeyed);
        _openVaultPassphrase = newPassphrase;
        return new CompanyEntry(rekeyed.DisplayName, _openVaultPath, IsVaulted: true, rekeyed.Number);
    }

    /// <summary>The registry row backing an entry, matched on its filename, or null when it has none.</summary>
    private CompanyRecord? Find(CompanyEntry entry)
    {
        var file = Path.GetFileName(entry.DatabasePath);
        return Registry.Load().FirstOrDefault(
            r => string.Equals(r.FileName, file, StringComparison.OrdinalIgnoreCase));
    }

    private static string SanitiseFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray()).Trim();
        return string.IsNullOrEmpty(cleaned) ? "Company" : cleaned;
    }
}
