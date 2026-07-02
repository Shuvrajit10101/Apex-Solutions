using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Apex.Ledger.Domain;
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

    /// <summary>The <c>.db</c> path a company of the given name maps to (name sanitised for the filename).</summary>
    public string PathForName(string companyName)
        => Path.Combine(CompaniesDirectory, SanitiseFileName(companyName) + ".db");

    /// <summary>True if a company with this name already has a <c>.db</c> file on disk.</summary>
    public bool Exists(string companyName) => File.Exists(PathForName(companyName));

    /// <summary>Persists a company aggregate to its <c>.db</c> file (create or replace).</summary>
    public void Save(Company company)
    {
        var path = PathForName(company.Name);
        using var store = new SqliteCompanyStore(path);
        store.Save(company);
    }

    /// <summary>Loads a company aggregate back from its <c>.db</c> file.</summary>
    public Company Load(CompanyEntry entry)
    {
        using var store = new SqliteCompanyStore(entry.DatabasePath);
        // The company id is not encoded in the filename, so read the single stored row's id.
        var companies = store.ListCompanies();
        if (companies.Count == 0)
            throw new InvalidOperationException($"No company found in '{entry.DatabasePath}'.");
        return store.Load(companies[0].Id)
            ?? throw new InvalidOperationException($"Failed to load company from '{entry.DatabasePath}'.");
    }

    /// <summary>
    /// Deletes a company's <c>.db</c> file. Best-effort; a locked file is left in place.
    /// </summary>
    public void Delete(CompanyEntry entry)
    {
        try
        {
            if (File.Exists(entry.DatabasePath))
                File.Delete(entry.DatabasePath);
        }
        catch (IOException) { /* file in use — leave it */ }
    }

    private static string SanitiseFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray()).Trim();
        return string.IsNullOrEmpty(cleaned) ? "Company" : cleaned;
    }
}
