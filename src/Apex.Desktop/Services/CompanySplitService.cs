using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Apex.Ledger.Domain;
using Apex.Ledger.Services;
using Apex.Persistence.Sqlite;

namespace Apex.Desktop.Services;

/// <summary>What a split run did: whether it completed, one line fit to show verbatim, and the books it wrote.</summary>
public sealed record CompanySplitOutcome(bool Ok, string Message, IReadOnlyList<CompanyEntry> Created);

/// <summary>
/// <b>Census row 16.5 — Split Company Data</b>, the storage half: it turns one company <c>.db</c> into one or two
/// NEW <c>.db</c> files and <b>never writes to the source</b>.
///
/// <para>🔴 <b>THE STRUCTURAL GUARANTEE, AND WHY IT IS A LOAD AND NOT A COPY OF THE OPEN AGGREGATE.</b> Each book
/// this class shapes is obtained by <see cref="CompanyStorage.Load"/>ing the source file again. That is the only
/// construction in this codebase that yields an aggregate sharing <b>no object</b> with the open company, so the
/// engine — which mutates what it is handed — structurally cannot reach the book the operator has on screen. The
/// open aggregate is never passed in; this class does not even take one.</para>
///
/// <para><b>What "never writes to the source" precisely means, stated rather than overclaimed.</b> Nothing here
/// calls <see cref="CompanyStorage.Save"/> with the source's name, and a refusal below makes a new book that
/// would land on the source's file unreachable rather than merely unlikely; the verification opens the file
/// <b>read-only</b>. The one thing that is NOT read-only is <see cref="CompanyStorage.Load"/>, which opens
/// read-write like every other open in this application — so splitting a book written by an OLDER build migrates
/// that book's file forward exactly as opening it normally would. That is the shell's ordinary behaviour, not
/// something this class adds, and it is named here so nobody reads the guarantee as wider than it is.
/// <c>SplitCompanyDataTests.Splitting_writes_two_new_books_and_leaves_the_original_byte_identical</c> pins the
/// current-version case on the source file's BYTES.</para>
///
/// <para><b>Vendor grounding (R7):</b> <c>help.tallysolutions.com/split-company-data-tally/</c> — the route
/// <i>"Alt+Y (Data) &gt; Split &gt; Split Data"</i>, the three options, the verification precondition, and
/// <i>"After splitting your company, the original company will remain as it is."</i></para>
/// </summary>
public sealed class CompanySplitService
{
    private readonly CompanyStorage _storage;

    public CompanySplitService(CompanyStorage storage)
        => _storage = storage ?? throw new ArgumentNullException(nameof(storage));

    /// <summary>
    /// The default name for the "before split date" book — <c>"&lt;name&gt; (to dd-MMM-yyyy)"</c>.
    /// <b>Ours, not the vendor's</b> (recorded in <c>docs/invented-vs-cloned.md</c>): the reference product keys
    /// its company list by an opaque company NUMBER and so can give both halves the original's name, while our
    /// store keys a book by its file name, which is derived from the company name. Two books cannot share one.
    /// </summary>
    public static string DefaultBeforeName(string sourceName, DateOnly splitDate)
        => $"{sourceName} (to {Show(splitDate.AddDays(-1))})";

    /// <summary>The default name for the "from split date" book — <c>"&lt;name&gt; (from dd-MMM-yyyy)"</c>.
    /// Ours, for the same reason as <see cref="DefaultBeforeName"/>.</summary>
    public static string DefaultFromName(string sourceName, DateOnly splitDate)
        => $"{sourceName} (from {Show(splitDate)})";

    /// <summary>
    /// Every reason this split cannot run, in the order to show them — the storage refusals first (a name that
    /// would land on an existing book is the one that could destroy data), then the engine's own
    /// (<see cref="CompanySplit.Check"/>). An empty list means <see cref="Split"/> may be called.
    ///
    /// <para>The book is loaded here to run the engine's checks against it. That load is read-only in effect:
    /// the aggregate is discarded when this returns.</para>
    /// </summary>
    public IReadOnlyList<string> Check(
        CompanyEntry source, DateOnly splitDate, CompanySplitMode mode, string? beforeName, string? fromName)
    {
        ArgumentNullException.ThrowIfNull(source);
        var reasons = new List<string>();

        // ---- The verification the vendor makes a precondition.
        var verification = CompanyBackup.Verify(source.DatabasePath);
        if (!verification.Ok)
            reasons.Add(
                "Verify Data has not passed on this company: " + verification.Summary +
                " Resolve the errors before splitting.");

        var wantsBefore = mode is CompanySplitMode.BeforeSplitDate or CompanySplitMode.IntoTwoCompanies;
        var wantsFrom = mode is CompanySplitMode.FromSplitDate or CompanySplitMode.IntoTwoCompanies;

        var names = new List<string>();
        if (wantsBefore) reasons.AddRange(CheckName(source, beforeName, "before the split date", names));
        if (wantsFrom) reasons.AddRange(CheckName(source, fromName, "from the split date", names));

        if (names.Count == 2 && PathsCollide(names[0], names[1]))
            reasons.Add(
                $"'{names[0]}' and '{names[1]}' would be written to the same file, so one would overwrite the " +
                "other. Give the two new companies names that differ.");

        if (reasons.Count > 0) return reasons;   // do not load a book we already know we will not split

        Company book;
        try
        {
            book = _storage.Load(source);
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or Microsoft.Data.Sqlite.SqliteException)
        {
            reasons.Add($"'{source.Name}' could not be opened: {ex.Message}");
            return reasons;
        }

        reasons.AddRange(CompanySplit.Check(book, splitDate, mode));
        return reasons;
    }

    /// <summary>
    /// Runs the split. Every refusal in <see cref="Check"/> is re-run first and nothing is written while one
    /// stands, so a caller that forgot to check cannot half-write a company.
    ///
    /// <para><b>Write order, and what a mid-run failure leaves behind.</b> The two books are independent files;
    /// there is no transaction spanning them. Each is loaded, shaped and saved in turn, and if the second save
    /// throws, the first is already on disk — so the message says exactly which books were written rather than
    /// implying nothing happened. <b>The source is untouched in every one of those outcomes</b>, which is the
    /// property that makes a partial run recoverable: delete the stray book and run it again.</para>
    /// </summary>
    public CompanySplitOutcome Split(
        CompanyEntry source, DateOnly splitDate, CompanySplitMode mode, string? beforeName, string? fromName)
    {
        ArgumentNullException.ThrowIfNull(source);

        var refusals = Check(source, splitDate, mode, beforeName, fromName);
        if (refusals.Count > 0)
            return new CompanySplitOutcome(false, string.Join(" ", refusals), Array.Empty<CompanyEntry>());

        var created = new List<CompanyEntry>();
        try
        {
            if (mode is CompanySplitMode.BeforeSplitDate or CompanySplitMode.IntoTwoCompanies)
                created.Add(Write(source, splitDate, beforeName!, before: true));

            if (mode is CompanySplitMode.FromSplitDate or CompanySplitMode.IntoTwoCompanies)
                created.Add(Write(source, splitDate, fromName!, before: false));
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or IOException
                                      or Microsoft.Data.Sqlite.SqliteException)
        {
            var written = created.Count == 0
                ? "No new company was written."
                : "Written before the failure: " + string.Join(", ", created.Select(c => "'" + c.Name + "'")) + ".";
            return new CompanySplitOutcome(false,
                $"The split did not complete: {ex.Message} '{source.Name}' is unchanged. {written}", created);
        }

        return new CompanySplitOutcome(true,
            $"Split '{source.Name}' at {Show(splitDate)} — wrote " +
            string.Join(" and ", created.Select(c => "'" + c.Name + "'")) +
            $". '{source.Name}' is unchanged.",
            created);
    }

    private CompanyEntry Write(CompanyEntry source, DateOnly splitDate, string newName, bool before)
    {
        var book = _storage.Load(source);         // an aggregate of its own — never the caller's
        if (before) CompanySplit.ShapeAsBeforeBook(book, splitDate, newName);
        else CompanySplit.ShapeAsFromBook(book, splitDate, newName);

        _storage.Save(book);
        return new CompanyEntry(book.Name, _storage.PathForName(book.Name));
    }

    private IEnumerable<string> CheckName(CompanyEntry source, string? name, string which, List<string> accepted)
    {
        var trimmed = (name ?? string.Empty).Trim();
        if (trimmed.Length == 0)
        {
            yield return $"The new company for the data {which} needs a name.";
            yield break;
        }

        // 🔴 The refusal that keeps "the original company will remain as it is" true. PathForName is NOT
        // injective (its own doc records the platform-dependent character collapse), so this compares PATHS,
        // never names: "Acme/Ltd" and "Acme_Ltd" are one file on Windows.
        if (PathsCollide(trimmed, source.Name) || SamePath(_storage.PathForName(trimmed), source.DatabasePath))
        {
            yield return
                $"'{trimmed}' is the book being split, so writing it would overwrite the original. " +
                "Choose a different name.";
            yield break;
        }

        if (_storage.Exists(trimmed))
        {
            yield return $"A company book already exists for '{trimmed}'. Choose a different name.";
            yield break;
        }

        accepted.Add(trimmed);
    }

    private bool PathsCollide(string a, string b) => SamePath(_storage.PathForName(a), _storage.PathForName(b));

    /// <summary>Same-file comparison, using this platform's own rules (Windows and default APFS are
    /// case-insensitive; Linux is not) — the same shape of test <c>CompanyStorage.Rename</c> uses.</summary>
    private static bool SamePath(string a, string b)
    {
        var comparison = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        try
        {
            return string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), comparison);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or IOException
                                      or UnauthorizedAccessException)
        {
            return string.Equals(a, b, comparison);
        }
    }

    /// <summary>Invariant date rendering, so a default name and a message read the same on every platform.</summary>
    private static string Show(DateOnly date) => date.ToString("dd-MMM-yyyy", CultureInfo.InvariantCulture);
}
