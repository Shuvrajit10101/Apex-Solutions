using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using Apex.Desktop.Services;
using Apex.Desktop.ViewModels;
using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Services;
using Apex.Persistence.Sqlite;
using Microsoft.Data.Sqlite;
using Domain = Apex.Ledger.Domain;

namespace Apex.Desktop.Tests;

/// <summary>
/// 🔴 <b>Census row 13.2 — after a restore, the shell's SAVE TARGET is the book it DISPLAYS</b> (A12 re-review w6,
/// N1 and N2).
///
/// <para>Two things decide which file a save on any screen writes: the company's NAME (a plain book) and, while a
/// vaulted book is open, the storage's vault SESSION; the Data Vault screen additionally acts on the shell's
/// <c>OpenEntry</c>. The restore panel can now restore onto a book other than the open one, and it reads the
/// restored file back before deciding whether to accept it. These tests drive the shell and read every book back
/// through a FRESH <see cref="CompanyStorage"/> after <c>ClearAllPools</c>, so nothing in memory can stand in for
/// what is on disk.</para>
/// </summary>
public sealed class RestoreOpenBookSessionTests : IDisposable
{
    private const string Passphrase = "correct horse battery staple 77";
    private static readonly DateOnly Fy = new(2025, 4, 1);
    private static readonly DateTime Now = new(2026, 8, 2, 14, 30, 0);

    private readonly string _tempDir;
    private readonly string _companiesDir;
    private readonly string _outDir;

    public RestoreOpenBookSessionTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "ApexRestoreSession_" + Guid.NewGuid().ToString("N"));
        _companiesDir = Path.Combine(_tempDir, "Companies");
        _outDir = Path.Combine(_tempDir, "out");
        Directory.CreateDirectory(_outDir);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        for (var attempt = 0; attempt < 10; attempt++)
        {
            try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true); return; }
            catch (IOException) { SqliteConnection.ClearAllPools(); System.Threading.Thread.Sleep(25); }
            catch (UnauthorizedAccessException) { SqliteConnection.ClearAllPools(); System.Threading.Thread.Sleep(25); }
        }
    }

    /// <summary>A book with <paramref name="vouchers"/> odd-paisa journals.</summary>
    private static Company Book(string name, int vouchers)
    {
        var c = CompanyFactory.CreateSeeded(name, Fy);
        c.AddLedger(new Domain.Ledger(Guid.NewGuid(), "Varun Agencies",
            c.FindGroupByName("Sundry Debtors")!.Id, Money.Zero, openingIsDebit: true));
        c.AddLedger(new Domain.Ledger(Guid.NewGuid(), "Sales",
            c.FindGroupByName("Sales Accounts")!.Id, Money.Zero, openingIsDebit: false));
        for (var i = 0; i < vouchers; i++) PostJournal(c, 31.37m * (i + 1), new DateOnly(2025, 4, 11 + i));
        return c;
    }

    private static void PostJournal(Company c, decimal amount, DateOnly date)
    {
        var debtor = c.FindLedgerByName("Varun Agencies")!;
        var sales = c.FindLedgerByName("Sales")!;
        new LedgerService(c).Post(new Voucher(Guid.NewGuid(), c.FindVoucherTypeByName("Journal")!.Id, date, new[]
        {
            new EntryLine(debtor.Id, Money.FromRupees(amount), DrCr.Debit),
            new EntryLine(sales.Id, Money.FromRupees(amount), DrCr.Credit),
        }));
    }

    /// <summary>Alpha Co is backed up holding ONE voucher, then grows to four on disk.</summary>
    private string AlphaArchive(CompanyStorage storage)
    {
        var alpha = Book("Alpha Co", 1);
        storage.Save(alpha);
        var backup = new BackupCompanyViewModel(alpha, storage, _outDir, Now);
        Assert.True(backup.Apply(), backup.Status);
        PostJournal(alpha, 202.02m, new DateOnly(2025, 5, 5));
        PostJournal(alpha, 303.03m, new DateOnly(2025, 6, 5));
        PostJournal(alpha, 404.04m, new DateOnly(2025, 7, 5));
        storage.Save(alpha);
        SqliteConnection.ClearAllPools();
        return backup.FullPath!;
    }

    /// <summary>Saves Beta Co (two vouchers) and moves it into the vault.</summary>
    private static void VaultedBeta(CompanyStorage storage)
    {
        var beta = Book("Beta Co", 2);
        storage.Save(beta);
        storage.EnableVault(storage.ListCompanies().Single(e => e.Name == "Beta Co"), beta, Passphrase);
        storage.CloseVault();
        SqliteConnection.ClearAllPools();
    }

    /// <summary>Opens the one vaulted book through Company Select and the passphrase prompt.</summary>
    private static MainWindowViewModel ShellOnVaultedBeta(CompanyStorage storage)
    {
        var vm = new MainWindowViewModel(storage);
        vm.ShowCompanySelect();
        var vaulted = storage.ListCompanies().Single(e => e.IsVaulted);
        vm.Menu.Single(m => m.Label == vaulted.Name).Activate();
        vm.CompanyUnlock!.Passphrase = Passphrase;
        Assert.True(vm.UnlockCompany(), vm.CompanyUnlock?.Message);
        Assert.Equal("Beta Co", vm.Company!.Name);
        Assert.True(storage.HasOpenVault);
        return vm;
    }

    private (string Name, int Vouchers, bool IsVaulted)[] Books()
    {
        SqliteConnection.ClearAllPools();
        var fresh = new CompanyStorage(_companiesDir);
        var books = fresh.ListCompanies().Select(e =>
        {
            var c = fresh.Load(e, e.IsVaulted ? Passphrase : null);
            fresh.CloseVault();
            return (c.Name, c.Vouchers.Count, e.IsVaulted);
        }).OrderBy(b => b.Name, StringComparer.Ordinal).ToArray();
        SqliteConnection.ClearAllPools();
        return books;
    }

    /// <summary>
    /// 🔴 <b>N1 — the redirect IV-139 sanctions, then Data Vault on the book on screen.</b> Beta Co is open; the
    /// operator restores Alpha Co's archive onto Alpha Co (the archive's own book), so Alpha Co is now displayed.
    /// He vaults it and keeps working. Before the fix the shell's open entry still named Beta Co's file: Beta Co was
    /// vaulted instead, and Alpha Co's next save went INTO Beta Co's vault, which then held two company rows and
    /// opened as neither.
    /// </summary>
    [Fact]
    public void A_restore_redirected_off_the_open_book_moves_the_save_target_with_it()
    {
        var storage = new CompanyStorage(_companiesDir);
        var archive = AlphaArchive(storage);
        storage.Save(Book("Beta Co", 2));
        SqliteConnection.ClearAllPools();

        var vm = new MainWindowViewModel(storage);
        vm.ShowCompanySelect();
        vm.Menu.Single(m => m.Label == "Beta Co").Activate();
        Assert.Equal("Beta Co", vm.Company!.Name);

        vm.OpenRestoreCompany();
        var panel = vm.RestoreCompanyPanel!;
        Assert.Equal("Beta Co", panel.TargetCompanyName);
        panel.FilePath = archive;
        panel.TargetCompanyName = "Alpha Co";
        Assert.True(panel.Examine(), panel.Status);
        panel.Confirmed = true;
        Assert.True(vm.ApplyRestore(), panel.Status);
        Assert.Equal("Alpha Co", vm.Company!.Name);

        vm.ShowGateway();
        vm.OpenDataVault();
        vm.DataVault!.NewPassphrase = Passphrase;
        vm.DataVault.ConfirmPassphrase = Passphrase;
        Assert.True(vm.ApplyDataVault(), vm.DataVault.Message);
        PostJournal(vm.Company!, 99.99m, new DateOnly(2025, 9, 9));
        storage.Save(vm.Company!);
        storage.CloseVault();

        var books = Books();
        Assert.Contains(("Beta Co", 2, false), books);     // never targeted: still plain, still two vouchers
        Assert.Contains(("Alpha Co", 2, true), books);     // the archive's one voucher + the new one, now vaulted
        Assert.Equal(2, books.Length);
    }

    /// <summary>
    /// 🔴 <b>N2 — a REFUSED restore must leave an open vaulted book in its vault.</b> The post-load row check
    /// (F1) refuses an archive whose manifest names the open book but whose company row is another. It read the
    /// file back through the OPEN verb, which forgets the vault session; the shell stayed on the vaulted book, and
    /// its next save wrote a PLAINTEXT "Beta Co.db" — the name and the ledger the vault exists to keep off the disk.
    /// </summary>
    [Fact]
    public void A_refused_restore_leaves_an_open_vaulted_book_in_its_vault()
    {
        var storage = new CompanyStorage(_companiesDir);
        var archive = AlphaArchive(storage);
        VaultedBeta(storage);

        var forged = Path.Combine(_outDir, "forged" + CompanyBackup.ArchiveExtension);
        File.Copy(archive, forged);
        using (var zip = ZipFile.Open(forged, ZipArchiveMode.Update))
        {
            var entry = zip.GetEntry("manifest.json")!;
            string json;
            using (var r = new StreamReader(entry.Open())) json = r.ReadToEnd();
            entry.Delete();
            var renamed = json.Replace("\"Alpha Co\"", "\"Beta Co\"", StringComparison.Ordinal);
            Assert.NotEqual(json, renamed);
            using var w = new StreamWriter(zip.CreateEntry("manifest.json").Open());
            w.Write(renamed);
        }

        var vm = ShellOnVaultedBeta(storage);
        vm.OpenRestoreCompany();
        var panel = vm.RestoreCompanyPanel!;
        panel.FilePath = forged;
        Assert.True(panel.Examine(), panel.Status);
        panel.Confirmed = true;
        Assert.False(vm.ApplyRestore(), panel.Status);
        Assert.Contains("is named 'Alpha Co'", panel.Status, StringComparison.Ordinal);

        PostJournal(vm.Company!, 77.77m, new DateOnly(2025, 9, 9));
        storage.Save(vm.Company!);
        storage.CloseVault();

        Assert.False(File.Exists(storage.PathForName("Beta Co")),
            "the vaulted book's next save wrote a plaintext copy of it under its own name");
        var books = Books();
        Assert.Contains(("Beta Co", 3, true), books);      // the save landed in the vault
        Assert.Contains(("Alpha Co", 4, false), books);    // untouched
        Assert.Equal(2, books.Length);
    }

    /// <summary>
    /// 🔴 <b>An ACCEPTED restore off an open vaulted book forgets the vault session.</b> Beta Co is open from the
    /// vault; the operator restores Alpha Co's archive onto Alpha Co and keeps working on the book now on screen.
    /// The session belongs to Beta Co: holding it would send Alpha Co's saves into Beta Co's encrypted file.
    /// </summary>
    [Fact]
    public void A_restore_off_an_open_vaulted_book_saves_the_restored_book_to_its_own_file()
    {
        var storage = new CompanyStorage(_companiesDir);
        var archive = AlphaArchive(storage);
        VaultedBeta(storage);

        var vm = ShellOnVaultedBeta(storage);
        vm.OpenRestoreCompany();
        var panel = vm.RestoreCompanyPanel!;
        panel.FilePath = archive;
        panel.TargetCompanyName = "Alpha Co";
        Assert.True(panel.Examine(), panel.Status);
        panel.Confirmed = true;
        Assert.True(vm.ApplyRestore(), panel.Status);
        Assert.Equal("Alpha Co", vm.Company!.Name);

        PostJournal(vm.Company!, 88.88m, new DateOnly(2025, 9, 9));
        storage.Save(vm.Company!);
        storage.CloseVault();

        var books = Books();
        Assert.Contains(("Alpha Co", 2, false), books);    // the archive's one voucher + the new one, plain
        Assert.Contains(("Beta Co", 2, true), books);      // the vault holds Beta Co alone
        Assert.Equal(2, books.Length);
    }
}
