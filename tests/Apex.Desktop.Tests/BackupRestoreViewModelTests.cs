using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Json;
using Apex.Desktop.Services;
using Apex.Desktop.ViewModels;
using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Reports;
using Apex.Ledger.Services;
using Apex.Persistence.Sqlite;
using Microsoft.Data.Sqlite;
using Xunit;
using Domain = Apex.Ledger.Domain;

namespace Apex.Desktop.Tests;

/// <summary>
/// UI-side coverage for the <b>Backup Company</b> / <b>Restore Company</b> panels — <c>plan.md</c> R-7's own named
/// mitigation, carved out of the otherwise-excluded Phase 10.
///
/// <para>The engine of this feature (<see cref="CompanyBackup"/>) is pinned by
/// <c>Apex.Persistence.Sqlite.Tests.CompanyBackupTests</c>, including the destroy-and-restore round trip and the
/// proof that a plain file copy loses committed data. These tests pin the <b>thin layer and the way in</b>:</para>
/// <list type="bullet">
/// <item>the feature is <b>reachable through the real Miller-column cascade</b> (Gateway → Data → Backup /
/// Restore → the two pages), not only by calling a method;</item>
/// <item>a company backed up by the Backup panel and put back by the Restore panel reconciles to the paisa, with
/// the SHELL's open company swapped to the restored one;</item>
/// <item>the Restore panel refuses to fire until the archive has been examined AND the overwrite explicitly
/// confirmed (NFR-8), and refuses outright a backup from a newer build — leaving the company untouched.</item>
/// </list>
/// </summary>
public sealed class BackupRestoreViewModelTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _outDir;
    private readonly CompanyStorage _storage;

    public BackupRestoreViewModelTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "ApexBackupUiTests_" + Guid.NewGuid().ToString("N"));
        _outDir = Path.Combine(_tempDir, "out");
        Directory.CreateDirectory(_outDir);
        _storage = new CompanyStorage(Path.Combine(_tempDir, "Companies"));
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

    private static readonly DateTime Now = new(2026, 8, 2, 14, 30, 0);
    private static readonly DateOnly AsOf = new(2026, 3, 31);

    /// <summary>
    /// A company whose figures all carry odd paisa, so a rounding or truncation defect cannot hide behind a round
    /// number. Trial Balance totals 89,412.63 on each side.
    /// </summary>
    private static Company OddPaisaCompany(string name)
    {
        var c = CompanyFactory.CreateSeeded(name, new DateOnly(2025, 4, 1));

        var cash = c.FindLedgerByName("Cash")!;
        cash.OpeningBalance = Money.FromRupees(31_806.41m);
        cash.OpeningIsDebit = true;

        var capital = new Domain.Ledger(Guid.NewGuid(), "Capital A/c",
            c.FindGroupByName("Capital Account")!.Id, Money.FromRupees(31_806.41m), openingIsDebit: false);
        c.AddLedger(capital);

        var sales = new Domain.Ledger(Guid.NewGuid(), "Sales",
            c.FindGroupByName("Sales Accounts")!.Id, Money.Zero, openingIsDebit: false);
        c.AddLedger(sales);

        var debtor = new Domain.Ledger(Guid.NewGuid(), "Harsha Enterprises",
            c.FindGroupByName("Sundry Debtors")!.Id, Money.Zero, openingIsDebit: true);
        c.AddLedger(debtor);

        var journal = c.FindVoucherTypeByName("Journal")!;
        var receipt = c.FindVoucherTypeByName("Receipt")!;
        var svc = new LedgerService(c);

        svc.Post(new Voucher(Guid.NewGuid(), journal.Id, new DateOnly(2025, 4, 7), new[]
        {
            new EntryLine(debtor.Id, Money.FromRupees(57_606.22m), DrCr.Debit),
            new EntryLine(sales.Id, Money.FromRupees(57_606.22m), DrCr.Credit),
        }));

        svc.Post(new Voucher(Guid.NewGuid(), receipt.Id, new DateOnly(2025, 4, 21), new[]
        {
            new EntryLine(cash.Id, Money.FromRupees(12_939.87m), DrCr.Debit),
            new EntryLine(debtor.Id, Money.FromRupees(12_939.87m), DrCr.Credit),
        }));

        return c;
    }

    private MainWindowViewModel ShellWith(Company company)
    {
        _storage.Save(company);
        var vm = new MainWindowViewModel(_storage);
        vm.ShowCompanySelect();
        // Open it exactly the way the Company-Select screen does — by activating its row.
        vm.Menu.Single(m => m.Label == company.Name).Activate();
        Assert.Equal(Screen.Gateway, vm.CurrentScreen);
        return vm;
    }

    private BackupCompanyViewModel BackupVm(Company c) => new(c, _storage, _outDir, Now);

    // ---------------------------------------------------------------- reachability through the real cascade

    [Fact]
    public void The_feature_is_reachable_by_drilling_the_real_gateway_cascade()
    {
        var vm = ShellWith(OddPaisaCompany("Reachable Co"));

        // Gateway root carries a "Data" section whose one item is the Backup / Restore group.
        Assert.Contains("Data", vm.Menu.Where(m => m.IsHeader).Select(m => m.Label));
        var group = vm.Menu.Single(m => m.Label == "Backup / Restore");
        Assert.True(group.IsSelectable);

        // Highlight it and drill in with the same call the keyboard uses.
        SelectByLabel(vm, "Backup / Restore");
        vm.DrillIn();

        Assert.Equal(2, vm.Columns.Count);                              // root + the Data submenu, cascading right
        Assert.Equal("Backup / Restore", vm.Columns[1].Title);
        var pages = vm.Columns[1].Items.Where(i => i.IsSelectable).Select(i => i.Label).ToArray();
        Assert.Equal(new[] { "Backup Company", "Restore Company" }, pages);

        // Drill "Backup Company" — a page column opens to the right, and it is the backup panel.
        SelectByLabel(vm.Columns[1], "Backup Company");
        vm.DrillIn();
        Assert.Equal(Screen.BackupCompany, vm.CurrentScreen);
        Assert.NotNull(vm.BackupCompanyPanel);
        Assert.Equal("Backup Company", vm.Columns[^1].Title);
        Assert.Same(vm.BackupCompanyPanel, vm.Columns[^1].BackupCompanyPanel);

        // Step back out and drill "Restore Company" the same way.
        vm.Back();
        SelectByLabel(vm.Columns[1], "Restore Company");
        vm.DrillIn();
        Assert.Equal(Screen.RestoreCompany, vm.CurrentScreen);
        Assert.NotNull(vm.RestoreCompanyPanel);
        Assert.Same(vm.RestoreCompanyPanel, vm.Columns[^1].RestoreCompanyPanel);
    }

    [Fact]
    public void The_data_menu_is_also_reachable_directly_and_from_the_button_bar()
    {
        var vm = ShellWith(OddPaisaCompany("Direct Co"));

        vm.ShowDataMenu();
        Assert.Equal("Backup / Restore", vm.Columns[^1].Title);
        Assert.Equal(GatewayMenu.Data, vm.CurrentGatewayMenu);

        // And the quick button exists, is enabled with a company open, and advertises the Alt+Y it is bound to.
        var button = vm.ButtonBar.Single(b => b.Caption == "Data");
        Assert.Equal("Alt+Y", button.Key);
        Assert.True(button.Enabled);
    }

    [Fact]
    public void Backup_and_restore_are_not_the_export_and_import_screens()
    {
        // The gap audit's point: Import/Export Data exist and are NOT a backup. They must stay distinct screens.
        var vm = ShellWith(OddPaisaCompany("Distinct Co"));

        vm.OpenBackupCompany();
        Assert.NotNull(vm.BackupCompanyPanel);
        Assert.Null(vm.ExportDataPanel);
        Assert.Null(vm.ImportDataPanel);
        Assert.Equal("Backup Company", vm.BackupCompanyPanel!.Title);
        Assert.NotEqual("Export Data", vm.BackupCompanyPanel.Title);
    }

    // ---------------------------------------------------------------- the round trip at the VM layer

    [Fact]
    public void A_company_backed_up_and_restored_through_the_panels_reconciles_to_the_paisa()
    {
        var company = OddPaisaCompany("Round Trip Co");
        var vm = ShellWith(company);

        var before = TrialBalance.Build(vm.Company!, AsOf);
        Assert.Equal(89_412.63m, before.TotalDebit.Amount);
        Assert.Equal(89_412.63m, before.TotalCredit.Amount);

        // ---- Backup through the panel. ----
        vm.OpenBackupCompany();
        var backup = new BackupCompanyViewModel(vm.Company!, _storage, _outDir, Now);
        Assert.EndsWith(".apexbak", backup.ResolvedFileName);
        Assert.Contains("20260802-1430", backup.ResolvedFileName);   // timestamped by default
        Assert.True(backup.Apply(), backup.Status);
        Assert.True(backup.Succeeded);
        Assert.Contains("data format v" + Schema.CurrentVersion, backup.Status);
        var archive = backup.FullPath;
        Assert.True(File.Exists(archive));

        // ---- Wreck the company: post a bogus voucher over it and persist, the way a bad day actually goes. ----
        var cash = vm.Company!.FindLedgerByName("Cash")!;
        var sales = vm.Company.FindLedgerByName("Sales")!;
        new LedgerService(vm.Company).Post(new Voucher(Guid.NewGuid(),
            vm.Company.FindVoucherTypeByName("Receipt")!.Id, new DateOnly(2025, 5, 2), new[]
            {
                new EntryLine(cash.Id, Money.FromRupees(99_999.99m), DrCr.Debit),
                new EntryLine(sales.Id, Money.FromRupees(99_999.99m), DrCr.Credit),
            }));
        _storage.Save(vm.Company);
        Assert.Equal(189_412.62m, TrialBalance.Build(vm.Company, AsOf).TotalDebit.Amount);
        SqliteConnection.ClearAllPools();

        // ---- Restore through the panel: examine, confirm, apply. ----
        var restore = new RestoreCompanyViewModel(vm.Company, _storage, onRestored: null)
        {
            FilePath = archive,
        };
        Assert.True(restore.Examine(), restore.Status);
        Assert.True(restore.CanRestore);
        Assert.Equal("Round Trip Co", restore.BackupCompanyName);
        Assert.Equal(Schema.CurrentVersion, restore.BackupDataFormatVersion);

        restore.Confirmed = true;
        Assert.True(restore.IsArmed);
        Assert.True(restore.Apply(), restore.Status);
        Assert.True(restore.Succeeded);
        SqliteConnection.ClearAllPools();

        // ---- The company on disk is the pre-wreck one, to the paisa. ----
        var reloaded = _storage.Load(_storage.ListCompanies().Single(e => e.Name == "Round Trip Co"));
        var after = TrialBalance.Build(reloaded, AsOf);
        Assert.Equal(89_412.63m, after.TotalDebit.Amount);
        Assert.Equal(89_412.63m, after.TotalCredit.Amount);
        Assert.True(after.Balanced);
        Assert.Equal(2, reloaded.Vouchers.Count);   // the bogus third voucher is gone
    }

    [Fact]
    public void A_restore_swaps_the_shell_open_company_for_the_restored_one()
    {
        var vm = ShellWith(OddPaisaCompany("Shell Swap Co"));

        var backup = BackupVm(vm.Company!);
        Assert.True(backup.Apply(), backup.Status);
        var archive = backup.FullPath;

        // Wreck it, and prove the SHELL is showing the wrecked figures.
        var cash = vm.Company!.FindLedgerByName("Cash")!;
        var sales = vm.Company.FindLedgerByName("Sales")!;
        new LedgerService(vm.Company).Post(new Voucher(Guid.NewGuid(),
            vm.Company.FindVoucherTypeByName("Receipt")!.Id, new DateOnly(2025, 5, 2), new[]
            {
                new EntryLine(cash.Id, Money.FromRupees(44_444.41m), DrCr.Debit),
                new EntryLine(sales.Id, Money.FromRupees(44_444.41m), DrCr.Credit),
            }));
        _storage.Save(vm.Company);
        Assert.Equal(133_857.04m, TrialBalance.Build(vm.Company, AsOf).TotalDebit.Amount);
        SqliteConnection.ClearAllPools();

        // Restore through the SHELL, so the onRestored hook runs.
        vm.OpenRestoreCompany();
        vm.RestoreCompanyPanel!.FilePath = archive;
        Assert.True(vm.ExamineRestore());
        vm.RestoreCompanyPanel.Confirmed = true;
        Assert.True(vm.ApplyRestore(), vm.RestoreCompanyPanel.Status);

        // The shell's OPEN aggregate is the restored one — otherwise every report afterwards would still be
        // answering from the data that was just replaced.
        Assert.Equal(89_412.63m, TrialBalance.Build(vm.Company!, AsOf).TotalDebit.Amount);
        Assert.Equal(2, vm.Company!.Vouchers.Count);
        Assert.Equal("Shell Swap Co", vm.StatusCompany);
    }

    // ---------------------------------------------------------------- the refusals

    [Fact]
    public void Restore_will_not_fire_before_it_has_been_examined()
    {
        var vm = ShellWith(OddPaisaCompany("Unexamined Co"));
        var backup = BackupVm(vm.Company!);
        Assert.True(backup.Apply(), backup.Status);
        SqliteConnection.ClearAllPools();
        var digest = FileDigest(_storage.PathForName("Unexamined Co"));

        var restore = new RestoreCompanyViewModel(vm.Company!, _storage, onRestored: null)
        {
            FilePath = backup.FullPath,
            Confirmed = true,           // ticked, but never examined
        };
        Assert.False(restore.IsArmed);
        Assert.False(restore.Apply());
        Assert.Contains("Examine", restore.Status, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Nothing has been changed", restore.Status, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(digest, FileDigest(_storage.PathForName("Unexamined Co")));
    }

    [Fact]
    public void Restore_will_not_fire_before_the_overwrite_is_confirmed()
    {
        var vm = ShellWith(OddPaisaCompany("Unconfirmed Co"));
        var backup = BackupVm(vm.Company!);
        Assert.True(backup.Apply(), backup.Status);
        SqliteConnection.ClearAllPools();
        var digest = FileDigest(_storage.PathForName("Unconfirmed Co"));

        var restore = new RestoreCompanyViewModel(vm.Company!, _storage, onRestored: null)
        {
            FilePath = backup.FullPath,
        };
        Assert.True(restore.Examine(), restore.Status);
        Assert.True(restore.CanRestore);
        Assert.False(restore.IsArmed);              // examined, but the tick is off — the button stays disabled

        Assert.False(restore.Apply());
        Assert.Contains("cannot be undone", restore.Status, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(digest, FileDigest(_storage.PathForName("Unconfirmed Co")));

        // Ticking arms it, and it then works.
        restore.Confirmed = true;
        Assert.True(restore.IsArmed);
        Assert.True(restore.Apply(), restore.Status);
    }

    [Fact]
    public void Changing_the_file_after_examining_disarms_the_restore_again()
    {
        var vm = ShellWith(OddPaisaCompany("Rearm Co"));
        var backup = BackupVm(vm.Company!);
        Assert.True(backup.Apply(), backup.Status);

        var restore = new RestoreCompanyViewModel(vm.Company!, _storage, onRestored: null)
        {
            FilePath = backup.FullPath,
        };
        Assert.True(restore.Examine());
        restore.Confirmed = true;
        Assert.True(restore.IsArmed);

        restore.FilePath = Path.Combine(_outDir, "some-other.apexbak");
        Assert.False(restore.CanRestore);
        Assert.False(restore.IsArmed);
        Assert.Equal(0, restore.BackupDataFormatVersion);
    }

    [Fact]
    public void Restore_refuses_a_backup_from_a_newer_build_and_names_both_versions()
    {
        var vm = ShellWith(OddPaisaCompany("Future Co"));
        var backup = BackupVm(vm.Company!);
        Assert.True(backup.Apply(), backup.Status);
        SqliteConnection.ClearAllPools();

        var future = Schema.CurrentVersion + 1;
        RestampSchemaVersion(backup.FullPath, future);
        var digest = FileDigest(_storage.PathForName("Future Co"));

        var restore = new RestoreCompanyViewModel(vm.Company!, _storage, onRestored: null)
        {
            FilePath = backup.FullPath,
        };

        // Examine reports the refusal and never arms the button.
        Assert.False(restore.Examine());
        Assert.False(restore.CanRestore);
        Assert.Equal(future, restore.BackupDataFormatVersion);
        Assert.Contains($"v{future}", restore.Status);
        Assert.Contains($"v{Schema.CurrentVersion}", restore.Status);
        Assert.Contains("Nothing has been changed", restore.Status, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("tally", restore.Status, StringComparison.OrdinalIgnoreCase);

        // And Apply is inert even if something ticks the confirmation anyway.
        restore.Confirmed = true;
        Assert.False(restore.IsArmed);
        Assert.False(restore.Apply());
        Assert.Equal(digest, FileDigest(_storage.PathForName("Future Co")));
    }

    [Fact]
    public void Restore_reports_a_file_that_is_not_a_backup_without_touching_the_company()
    {
        var vm = ShellWith(OddPaisaCompany("Junk Co"));
        var junk = Path.Combine(_outDir, "not-a-backup.apexbak");
        File.WriteAllBytes(junk, Encoding.ASCII.GetBytes("a spreadsheet someone renamed"));
        SqliteConnection.ClearAllPools();
        var digest = FileDigest(_storage.PathForName("Junk Co"));

        var restore = new RestoreCompanyViewModel(vm.Company!, _storage, onRestored: null) { FilePath = junk };
        Assert.False(restore.Examine());
        Assert.False(restore.CanRestore);
        Assert.False(string.IsNullOrWhiteSpace(restore.Status));
        Assert.Equal(digest, FileDigest(_storage.PathForName("Junk Co")));
    }

    [Fact]
    public void Backup_reports_the_failure_rather_than_claiming_success()
    {
        var vm = ShellWith(OddPaisaCompany("Bad Dest Co"));

        // A destination path the OS cannot possibly accept.
        var backup = new BackupCompanyViewModel(vm.Company!, _storage, "\0not\0a\0folder\0", Now);

        Assert.False(backup.Apply());
        Assert.False(backup.Succeeded);
        Assert.False(string.IsNullOrWhiteSpace(backup.Status));
    }

    // ---------------------------------------------------------------- helpers

    private static void SelectByLabel(MainWindowViewModel vm, string label)
        => SelectByLabel(vm.Columns[0], label);

    private static void SelectByLabel(GatewayColumn column, string label)
    {
        for (var i = 0; i < column.Items.Count; i++)
            if (column.Items[i].IsSelectable && column.Items[i].Label == label)
            {
                column.SetSelected(i);
                return;
            }
        throw new Xunit.Sdk.XunitException($"No selectable row labelled '{label}' in column '{column.Title}'.");
    }

    private static string FileDigest(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(stream));
    }

    /// <summary>Rewrites an archive's manifest with a different schemaVersion, leaving the payload alone.</summary>
    private static void RestampSchemaVersion(string archivePath, int schemaVersion)
    {
        byte[] db;
        string manifestJson;
        using (var zip = ZipFile.OpenRead(archivePath))
        {
            using var dbStream = zip.GetEntry("company.db")!.Open();
            using var buffer = new MemoryStream();
            dbStream.CopyTo(buffer);
            db = buffer.ToArray();
            using var m = zip.GetEntry("manifest.json")!.Open();
            manifestJson = new StreamReader(m, Encoding.UTF8).ReadToEnd();
        }

        using var doc = JsonDocument.Parse(manifestJson);
        var dict = doc.RootElement.EnumerateObject().ToDictionary(
            p => p.Name,
            p => p.Name == "schemaVersion" ? (object?)schemaVersion : p.Value.ValueKind switch
            {
                JsonValueKind.Number => p.Value.GetInt64(),
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.Null => null,
                _ => p.Value.GetString(),
            });

        File.Delete(archivePath);
        using var rewritten = ZipFile.Open(archivePath, ZipArchiveMode.Create);
        using (var e = rewritten.CreateEntry("manifest.json").Open())
            e.Write(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(dict)));
        using (var e = rewritten.CreateEntry("company.db").Open())
            e.Write(db);
    }
}
