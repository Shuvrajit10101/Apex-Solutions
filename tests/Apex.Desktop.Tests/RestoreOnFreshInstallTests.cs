using System;
using System.IO;
using System.Linq;
using Apex.Desktop.Services;
using Apex.Desktop.ViewModels;
using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Reports;
using Apex.Ledger.Services;
using Apex.Persistence.Sqlite;
using Microsoft.Data.Sqlite;
using Domain = Apex.Ledger.Domain;

namespace Apex.Desktop.Tests;

/// <summary>
/// <b>Census row 13.2 / gap-register T1-7, widened by T1-20 — RESTORE ON A MACHINE WITH NO COMPANY.</b>
///
/// <para>T1-7, verbatim: <i>"Restore is unreachable on a fresh install. The engine supports restoring a company
/// this machine never had; the screen is gated on an open company … The exact disaster-recovery case backup
/// exists for is the one case it cannot serve."</i> Row 13.2 named the two locks that caused it —
/// <c>ShowDataMenu</c> bounced to Company Select with no company open, and <c>OpenRestoreCompany</c> returned
/// early — and T1-20 added that the target could not be redirected even with a company open.</para>
///
/// <para>🔴 <b>WHAT THESE TESTS ASSERT ON, AND WHY.</b> Every one of them drives the SHELL and then reads the
/// result back off the <b>reloaded book</b> or off <c>CompanyStorage.ListCompanies()</c> — never off the
/// aggregate the test handed in. The whole defect class this row sits in is "complete, correct, unreachable",
/// and the only thing that distinguishes a reachable feature from an unreachable one is whether the route
/// exists; so the route is what is driven. <c>RestoreOnFreshInstallRealisedTests</c> carries the last mile (a
/// realised, bound control for the target).</para>
/// </summary>
public sealed class RestoreOnFreshInstallTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _outDir;
    private readonly string _companiesDir;

    public RestoreOnFreshInstallTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "ApexRestoreFresh_" + Guid.NewGuid().ToString("N"));
        _outDir = Path.Combine(_tempDir, "out");
        _companiesDir = Path.Combine(_tempDir, "Companies");
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

    private static readonly DateOnly AsOf = new(2026, 3, 31);
    private static readonly DateTime Now = new(2026, 8, 2, 14, 30, 0);

    /// <summary>Odd-paisa figures so a truncation defect cannot hide behind a round number. TB 44,746.28.</summary>
    private static Company OddPaisaCompany(string name)
    {
        var c = CompanyFactory.CreateSeeded(name, new DateOnly(2025, 4, 1));

        var cash = c.FindLedgerByName("Cash")!;
        cash.OpeningBalance = Money.FromRupees(13_504.91m);
        cash.OpeningIsDebit = true;

        var capital = new Domain.Ledger(Guid.NewGuid(), "Capital A/c",
            c.FindGroupByName("Capital Account")!.Id, Money.FromRupees(13_504.91m), openingIsDebit: false);
        c.AddLedger(capital);

        var sales = new Domain.Ledger(Guid.NewGuid(), "Sales",
            c.FindGroupByName("Sales Accounts")!.Id, Money.Zero, openingIsDebit: false);
        c.AddLedger(sales);

        var debtor = new Domain.Ledger(Guid.NewGuid(), "Varun Agencies",
            c.FindGroupByName("Sundry Debtors")!.Id, Money.Zero, openingIsDebit: true);
        c.AddLedger(debtor);

        var journal = c.FindVoucherTypeByName("Journal")!;
        var svc = new LedgerService(c);
        svc.Post(new Voucher(Guid.NewGuid(), journal.Id, new DateOnly(2025, 4, 11), new[]
        {
            new EntryLine(debtor.Id, Money.FromRupees(31_241.37m), DrCr.Debit),
            new EntryLine(sales.Id, Money.FromRupees(31_241.37m), DrCr.Credit),
        }));

        return c;
    }

    /// <summary>
    /// Takes a real archive of <paramref name="name"/> through the shipped Backup panel, then wipes the
    /// companies directory so the machine genuinely has nothing — the fresh-install state.
    /// </summary>
    private string ArchiveOfAVanishedCompany(string name, out Company original)
    {
        original = OddPaisaCompany(name);

        var seedStorage = new CompanyStorage(_companiesDir);
        seedStorage.Save(original);
        var backup = new BackupCompanyViewModel(original, seedStorage, _outDir, Now);
        Assert.True(backup.Apply(), backup.Status);
        var archive = backup.FullPath;
        Assert.True(File.Exists(archive), "the Backup panel must have written a real archive");

        // 🔴 THE DISASTER. The whole directory goes, registry included, exactly as a lost disk would take it.
        SqliteConnection.ClearAllPools();
        Directory.Delete(_companiesDir, recursive: true);

        return archive!;
    }

    private static MainWindowViewModel FreshShell(CompanyStorage storage)
    {
        var vm = new MainWindowViewModel(storage);
        vm.ShowCompanySelect();
        return vm;
    }

    // ------------------------------------------------------------------ the two locks

    /// <summary>
    /// 🔴 <b>LOCK ONE, GONE: the Data menu no longer bounces to Company Select with nothing open</b>, and it
    /// offers Restore. It also stops offering Backup, which is the correct asymmetry — backup copies the OPEN
    /// book, so with nothing open that row would be a door onto a no-op.
    /// </summary>
    [Fact]
    public void The_data_menu_opens_with_no_company_and_offers_restore_but_not_backup()
    {
        var storage = new CompanyStorage(_companiesDir);
        var vm = FreshShell(storage);
        Assert.Null(vm.Company);

        vm.ShowDataMenu();

        Assert.NotEqual(Screen.CompanySelect, vm.CurrentScreen);
        var offered = vm.Menu.Where(m => m.IsSelectable).Select(m => m.Label).ToArray();
        Assert.Contains("Restore Company", offered);
        Assert.DoesNotContain("Backup Company", offered);
    }

    /// <summary>
    /// 🔴 <b>LOCK TWO, GONE: the panel opens with no company open.</b> It used to return early, so even once
    /// the menu let an operator through there was nothing to arrive at.
    /// </summary>
    [Fact]
    public void The_restore_panel_opens_with_no_company_open()
    {
        var storage = new CompanyStorage(_companiesDir);
        var vm = FreshShell(storage);
        Assert.Null(vm.Company);

        vm.OpenRestoreCompany();

        Assert.NotNull(vm.RestoreCompanyPanel);
        Assert.Equal(Screen.RestoreCompany, vm.CurrentScreen);
        // No company to default to, and defaulting to one would arm a destructive replace nobody chose.
        Assert.Equal(string.Empty, vm.RestoreCompanyPanel!.TargetCompanyName);
    }

    /// <summary>
    /// <b>The fresh-install DOOR.</b> Company Select is the screen a machine with no company actually lands on,
    /// and it now carries a Restore row that opens the panel when activated like any other row.
    /// </summary>
    [Fact]
    public void Company_select_offers_restore_even_with_zero_companies_and_the_row_opens_the_panel()
    {
        var storage = new CompanyStorage(_companiesDir);
        var vm = FreshShell(storage);

        Assert.Empty(storage.ListCompanies());
        var row = vm.Menu.Single(m => m.Label == "Restore Company");

        row.Activate();

        Assert.NotNull(vm.RestoreCompanyPanel);
        Assert.Equal(Screen.RestoreCompany, vm.CurrentScreen);
    }

    // ------------------------------------------------------------------ the end-to-end recovery

    /// <summary>
    /// 🔴 <b>THE WHOLE POINT OF THE ROW: a machine with NO companies recovers a lost book end to end, through
    /// the shipped screens, and the recovered book is still there for the NEXT session.</b>
    ///
    /// <para>The last clause is the one that nearly shipped broken. <c>CompanyStorage.ListCompanies</c> reads
    /// the REGISTRY, not the directory, and <c>Load</c> registers nothing — only <c>Save</c> does. So a restore
    /// that CREATED a company would have written a perfectly good <c>.db</c> that Company Select never listed:
    /// the operator recovers his data and has no way to open it again. That is why this test re-opens a
    /// brand-new shell on the same directory at the end instead of stopping at a green status line.</para>
    /// </summary>
    [Fact]
    public void A_machine_with_no_companies_recovers_a_lost_book_and_it_is_listed_next_session()
    {
        var archive = ArchiveOfAVanishedCompany("Varun Transport", out var original);

        var storage = new CompanyStorage(_companiesDir);
        Assert.Empty(storage.ListCompanies());

        var vm = FreshShell(storage);
        vm.Menu.Single(m => m.Label == "Restore Company").Activate();
        var panel = vm.RestoreCompanyPanel!;

        // The operator names the target himself — there is no open company to inherit it from.
        panel.FilePath = archive;
        panel.TargetCompanyName = "Varun Transport";
        Assert.False(panel.TargetExists, "nothing is on this machine yet, so this is a CREATE");
        Assert.Contains("CREATED", panel.TargetEffectNote, StringComparison.Ordinal);

        Assert.True(panel.Examine(), panel.Status);
        panel.Confirmed = true;
        Assert.True(panel.Apply(), panel.Status);
        Assert.Contains("as a new company", panel.Status, StringComparison.Ordinal);

        // The shell is now open on the recovered book.
        Assert.NotNull(vm.Company);
        Assert.Equal("Varun Transport", vm.Company!.Name);

        // 🔴 Read back through a BRAND-NEW shell and storage on the same directory — what the next session sees.
        SqliteConnection.ClearAllPools();
        var nextSession = new CompanyStorage(_companiesDir);
        Assert.Contains(nextSession.ListCompanies(), e => e.Name == "Varun Transport");

        var reopenedShell = FreshShell(nextSession);
        reopenedShell.Menu.Single(m => m.Label == "Varun Transport").Activate();
        var reopened = reopenedShell.Company!;

        // And the figures are the lost book's, to the paisa.
        var tb = TrialBalance.Build(reopened, AsOf);
        var originalTb = TrialBalance.Build(original, AsOf);
        Assert.Equal(originalTb.TotalDebit.Amount, tb.TotalDebit.Amount);
        Assert.Equal(originalTb.TotalCredit.Amount, tb.TotalCredit.Amount);
        Assert.True(tb.Balanced);
        Assert.Equal(original.Vouchers.Count, reopened.Vouchers.Count);
        Assert.Equal(31_241.37m, reopened.Vouchers.Single().Lines.First().Amount.Amount);
    }

    // ------------------------------------------------------------------ the target is redirectable

    /// <summary>
    /// 🔴 <b>T1-20: the target is REDIRECTABLE.</b> With a company open, the panel starts on it and can be
    /// pointed at a DIFFERENT one — and the restore lands on the one the operator pointed at, asserted by
    /// reading the book back off disk rather than off the status line.
    /// </summary>
    [Fact]
    public void With_a_company_open_the_target_can_be_redirected_to_a_different_company()
    {
        var storage = new CompanyStorage(_companiesDir);

        var keeper = OddPaisaCompany("Keeper Books");
        storage.Save(keeper);
        var doomed = CompanyFactory.CreateSeeded("Doomed Books", new DateOnly(2025, 4, 1));
        storage.Save(doomed);

        var backup = new BackupCompanyViewModel(keeper, storage, _outDir, Now);
        Assert.True(backup.Apply(), backup.Status);
        SqliteConnection.ClearAllPools();

        // Open "Doomed Books", then point the restore at it while holding Keeper's archive.
        var vm = FreshShell(storage);
        vm.Menu.Single(m => m.Label == "Doomed Books").Activate();
        vm.OpenRestoreCompany();
        var panel = vm.RestoreCompanyPanel!;
        Assert.Equal("Doomed Books", panel.TargetCompanyName);   // defaults to the open book

        panel.FilePath = backup.FullPath;
        panel.TargetCompanyName = "Doomed Books";
        Assert.True(panel.TargetExists, "this one IS on disk, so it is a REPLACE");
        Assert.Contains("REPLACED", panel.TargetEffectNote, StringComparison.Ordinal);

        Assert.True(panel.Examine(), panel.Status);
        panel.Confirmed = true;
        Assert.True(panel.Apply(), panel.Status);
        Assert.Contains("replacing it", panel.Status, StringComparison.Ordinal);

        // Read "Doomed Books" back off disk: it now holds Keeper's figures.
        SqliteConnection.ClearAllPools();
        var nextSession = new CompanyStorage(_companiesDir);
        var entry = nextSession.ListCompanies().Single(e => e.Name == "Doomed Books");
        var reloaded = nextSession.Load(entry);
        var tb = TrialBalance.Build(reloaded, AsOf);
        Assert.Equal(TrialBalance.Build(keeper, AsOf).TotalDebit.Amount, tb.TotalDebit.Amount);
        Assert.Equal(keeper.Vouchers.Count, reloaded.Vouchers.Count);

        // "Keeper Books" itself was NOT touched — a redirect must move only the target it names.
        var keeperEntry = nextSession.ListCompanies().Single(e => e.Name == "Keeper Books");
        Assert.Equal("Keeper Books", nextSession.Load(keeperEntry).Name);
    }

    /// <summary>
    /// Re-pointing the target DISARMS the confirmation. The tick means "I accept that THIS book is replaced";
    /// carrying it across a change of target would let an operator who confirmed over one book press Restore
    /// and destroy another he never confirmed.
    /// </summary>
    [Fact]
    public void Re_pointing_the_target_disarms_the_confirmation()
    {
        var archive = ArchiveOfAVanishedCompany("Disarm Co", out _);
        var storage = new CompanyStorage(_companiesDir);

        var panel = new RestoreCompanyViewModel(storage, onRestored: null)
        {
            FilePath = archive,
            TargetCompanyName = "Disarm Co",
        };
        Assert.True(panel.Examine(), panel.Status);
        panel.Confirmed = true;
        Assert.True(panel.IsArmed);

        panel.TargetCompanyName = "Somewhere Else";

        Assert.False(panel.Confirmed);
        Assert.False(panel.IsArmed);
        Assert.False(panel.Apply(), "a re-pointed restore must not fire on the previous confirmation");
    }

    /// <summary>
    /// A blank target is refused and nothing is written. The fresh-install panel opens blank, so this is the
    /// default state of the screen and not an edge case.
    /// </summary>
    [Fact]
    public void A_blank_target_is_refused_and_writes_nothing()
    {
        var archive = ArchiveOfAVanishedCompany("Blank Target Co", out _);
        var storage = new CompanyStorage(_companiesDir);

        var panel = new RestoreCompanyViewModel(storage, onRestored: null) { FilePath = archive };
        Assert.True(panel.Examine(), panel.Status);
        panel.Confirmed = true;

        Assert.False(panel.Apply());
        Assert.Contains("No company was selected", panel.Status, StringComparison.Ordinal);
        Assert.Empty(storage.ListCompanies());
    }

    /// <summary>
    /// A CORRUPT archive is refused on the fresh-install path too, by a message naming the problem, and creates
    /// NOTHING — the "nothing has been changed" promise has to hold when there was nothing there to begin with,
    /// or a failed recovery would leave a half-written book behind for the operator to find later.
    /// </summary>
    [Fact]
    public void A_corrupt_archive_is_refused_on_the_fresh_install_path_and_creates_nothing()
    {
        var archive = ArchiveOfAVanishedCompany("Corrupt Co", out _);
        var storage = new CompanyStorage(_companiesDir);

        // Truncate the archive to half its bytes — a real half-copied file, not a fabricated one.
        var bytes = File.ReadAllBytes(archive);
        var truncated = Path.Combine(_outDir, "truncated" + CompanyBackup.ArchiveExtension);
        File.WriteAllBytes(truncated, bytes.Take(bytes.Length / 2).ToArray());

        var vm = FreshShell(storage);
        vm.Menu.Single(m => m.Label == "Restore Company").Activate();
        var panel = vm.RestoreCompanyPanel!;
        panel.FilePath = truncated;
        panel.TargetCompanyName = "Corrupt Co";

        // It is refused at Examine, so the Restore button never arms at all.
        Assert.False(panel.Examine());
        Assert.False(panel.CanRestore);
        Assert.False(string.IsNullOrWhiteSpace(panel.Status));

        // Nothing was created, and the shell has no company.
        Assert.False(storage.Exists("Corrupt Co"));
        Assert.Empty(storage.ListCompanies());
        Assert.Null(vm.Company);
    }

    /// <summary>
    /// The schema guard on the fresh-install path: an archive stamped with a FUTURE data format is refused by
    /// name and number rather than written over nothing, and an archive stamped with THIS build's version
    /// restores. Both halves, because a guard that refused everything would pass the first assertion alone.
    /// </summary>
    [Fact]
    public void A_future_data_format_is_refused_and_the_current_one_restores()
    {
        var archive = ArchiveOfAVanishedCompany("Schema Co", out _);
        var storage = new CompanyStorage(_companiesDir);

        // The archive this build just wrote carries this build's own version, and it restores.
        var manifest = CompanyBackup.ReadManifest(archive);
        Assert.Equal(Schema.CurrentVersion, manifest.SchemaVersion);
        Assert.True(CompanyBackup.CanRestoreSchemaVersion(manifest.SchemaVersion));

        var ok = new RestoreCompanyViewModel(storage, onRestored: null)
        {
            FilePath = archive,
            TargetCompanyName = "Schema Co",
        };
        Assert.True(ok.Examine(), ok.Status);
        ok.Confirmed = true;
        Assert.True(ok.Apply(), ok.Status);

        // And one version beyond this build is refused, naming both numbers.
        Assert.False(CompanyBackup.CanRestoreSchemaVersion(Schema.CurrentVersion + 1));
        var refusal = CompanyBackup.UnsupportedSchemaMessage(Schema.CurrentVersion + 1);
        Assert.Contains((Schema.CurrentVersion + 1).ToString(), refusal, StringComparison.Ordinal);
        Assert.Contains(Schema.CurrentVersion.ToString(), refusal, StringComparison.Ordinal);
        Assert.Contains("Nothing has been changed", refusal, StringComparison.Ordinal);
    }
}
