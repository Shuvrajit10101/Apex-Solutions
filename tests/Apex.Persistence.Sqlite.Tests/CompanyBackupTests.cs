using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Reports;
using Apex.Ledger.Services;
using Microsoft.Data.Sqlite;
using Xunit;
using Domain = Apex.Ledger.Domain;

namespace Apex.Persistence.Sqlite.Tests;

/// <summary>
/// The <b>backup / restore</b> contract — <c>plan.md</c> R-7's own named mitigation ("data-loss / migration risk"),
/// carved out of the otherwise-excluded Phase 10 because a plan cannot name backup as the mitigation for its
/// top-ranked risk and then not build it.
///
/// <para>The headline test is the <b>restore round trip</b>: a company carrying real, odd-paisa figures is saved,
/// backed up, the original file is then <i>destroyed</i>, and the backup restored — after which the file is
/// byte-identical to the snapshot, every table's contents are byte-identical to before, and the engine's reports
/// recomputed on the reloaded company reconcile to the paisa. A backup nobody has ever restored is not a backup.</para>
///
/// <para>The second group pins the <b>mechanism</b>: the SQLite Online Backup API, not a file copy. Exactly ONE test
/// discriminates it — <see cref="A_plain_file_copy_loses_committed_data_that_the_backup_api_captures"/>, which puts
/// the source in WAL mode with committed-but-uncheckpointed data and shows a plain <c>File.Copy</c> silently loses
/// it while the backup does not. That is the whole of the evidence: replacing <c>BackupDatabase</c> with a
/// <c>File.Copy</c> turns that test, and only that test, red. The other two tests in the group are genuine
/// consistency and refusal assertions but they do <b>not</b> prove the mechanism — both survive a <c>File.Copy</c>,
/// each for its own documented reason. Do not cite them for it, and do not weaken the WAL test without replacing
/// the discrimination it carries alone.</para>
///
/// <para>The third group pins the <b>version stamp and the refusals</b>: the manifest carries the source's
/// <c>schema_version</c>; a backup from a newer build is refused by name and number; a corrupted payload is refused;
/// and in every refusal the target company file is left byte-identical to what it was.</para>
///
/// <para>The fourth group pins what happens when a restore fails at the <b>swap</b> rather than at a validation —
/// the only window in which this code can damage the company it was run to protect — and the fifth pins that taking
/// a backup can never itself destroy a company.</para>
/// </summary>
public sealed class CompanyBackupTests : IDisposable
{
    private readonly string _dir;

    public CompanyBackupTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "ApexBackupTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        for (var attempt = 0; attempt < 10; attempt++)
        {
            try { if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true); return; }
            catch (IOException) { SqliteConnection.ClearAllPools(); Thread.Sleep(25); }
            catch (UnauthorizedAccessException) { SqliteConnection.ClearAllPools(); Thread.Sleep(25); }
        }
    }

    private string Path_(string name) => Path.Combine(_dir, name);

    private static readonly DateTimeOffset TakenAt = new(2026, 8, 2, 9, 45, 17, TimeSpan.Zero);
    private static readonly DateOnly AsOf = new(2026, 3, 31);

    // ---------------------------------------------------------------- the fixture: ODD PAISA throughout

    /// <summary>
    /// A populated company whose every figure carries odd paisa, so a rounding or truncation defect cannot hide
    /// behind a round number. Trial Balance totals 1,15,305.59 on each side.
    /// </summary>
    private static Company OddPaisaCompany(string name = "Backup Fixture Co")
    {
        var c = CompanyFactory.CreateSeeded(name, new DateOnly(2025, 4, 1));

        var cash = c.FindLedgerByName("Cash")!;
        cash.OpeningBalance = Money.FromRupees(41_234.57m);
        cash.OpeningIsDebit = true;

        var capital = new Domain.Ledger(Guid.NewGuid(), "Capital A/c",
            c.FindGroupByName("Capital Account")!.Id, Money.FromRupees(41_234.57m), openingIsDebit: false);
        c.AddLedger(capital);

        var sales = new Domain.Ledger(Guid.NewGuid(), "Sales",
            c.FindGroupByName("Sales Accounts")!.Id, Money.Zero, openingIsDebit: false);
        c.AddLedger(sales);

        var purchases = new Domain.Ledger(Guid.NewGuid(), "Purchases",
            c.FindGroupByName("Purchase Accounts")!.Id, Money.Zero, openingIsDebit: true);
        c.AddLedger(purchases);

        var debtor = new Domain.Ledger(Guid.NewGuid(), "Meridian Traders",
            c.FindGroupByName("Sundry Debtors")!.Id, Money.Zero, openingIsDebit: true,
            maintainBillByBill: true, defaultCreditPeriodDays: 30);
        c.AddLedger(debtor);

        var creditor = new Domain.Ledger(Guid.NewGuid(), "Kalyani Supplies",
            c.FindGroupByName("Sundry Creditors")!.Id, Money.Zero, openingIsDebit: false,
            maintainBillByBill: true, defaultCreditPeriodDays: 45);
        c.AddLedger(creditor);

        var journal = c.FindVoucherTypeByName("Journal")!;
        var receipt = c.FindVoucherTypeByName("Receipt")!;
        var payment = c.FindVoucherTypeByName("Payment")!;
        var svc = new LedgerService(c);

        // Credit sale 57,318.49 split across two bills (33,102.91 + 24,215.58).
        svc.Post(new Voucher(Guid.NewGuid(), journal.Id, new DateOnly(2025, 4, 3), new[]
        {
            new EntryLine(debtor.Id, Money.FromRupees(57_318.49m), DrCr.Debit, new[]
            {
                new BillAllocation(BillRefType.NewRef, "MT/001", Money.FromRupees(33_102.91m),
                    dueDate: new DateOnly(2025, 5, 3)),
                new BillAllocation(BillRefType.NewRef, "MT/002", Money.FromRupees(24_215.58m)),
            }),
            new EntryLine(sales.Id, Money.FromRupees(57_318.49m), DrCr.Credit),
        }));

        // Part receipt 19,447.33 against MT/001.
        svc.Post(new Voucher(Guid.NewGuid(), receipt.Id, new DateOnly(2025, 4, 17), new[]
        {
            new EntryLine(cash.Id, Money.FromRupees(19_447.33m), DrCr.Debit),
            new EntryLine(debtor.Id, Money.FromRupees(19_447.33m), DrCr.Credit, new[]
            {
                new BillAllocation(BillRefType.AgstRef, "MT/001", Money.FromRupees(19_447.33m)),
            }),
        }));

        // Credit purchase 24,903.72 on one bill.
        svc.Post(new Voucher(Guid.NewGuid(), journal.Id, new DateOnly(2025, 4, 9), new[]
        {
            new EntryLine(purchases.Id, Money.FromRupees(24_903.72m), DrCr.Debit),
            new EntryLine(creditor.Id, Money.FromRupees(24_903.72m), DrCr.Credit, new[]
            {
                new BillAllocation(BillRefType.NewRef, "KS/77", Money.FromRupees(24_903.72m)),
            }),
        }));

        // Part payment 8,151.19 against KS/77.
        svc.Post(new Voucher(Guid.NewGuid(), payment.Id, new DateOnly(2025, 4, 23), new[]
        {
            new EntryLine(creditor.Id, Money.FromRupees(8_151.19m), DrCr.Debit, new[]
            {
                new BillAllocation(BillRefType.AgstRef, "KS/77", Money.FromRupees(8_151.19m)),
            }),
            new EntryLine(cash.Id, Money.FromRupees(8_151.19m), DrCr.Credit),
        }));

        return c;
    }

    /// <summary>Writes the fixture company to a fresh <c>.db</c> and returns its path.</summary>
    private string SaveFixture(Company c, string fileName = "company.db")
    {
        var path = Path_(fileName);
        using (var store = new SqliteCompanyStore(path)) store.Save(c);
        SqliteConnection.ClearAllPools();
        return path;
    }

    // ---------------------------------------------------------------- digests

    private static string Sha256OfFile(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    /// <summary>ASCII unit separator — cannot occur in a rendered cell, so it is a safe column delimiter.</summary>
    private const char CellSeparator = '';

    /// <summary>
    /// A deterministic digest of every user table's <i>contents</i> — table name, column names, then every row
    /// rendered cell-by-cell in a stable order. Independent of page layout, free-list state and file size, so it
    /// answers "is the DATA byte-identical" rather than "is the file byte-identical".
    /// </summary>
    private static string ContentDigest(string dbPath)
    {
        var sb = new StringBuilder();
        using var conn = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = dbPath,
            Mode = SqliteOpenMode.ReadWrite,
        }.ToString());
        conn.Open();

        var tables = new List<string>();
        using (var q = conn.CreateCommand())
        {
            q.CommandText =
                "SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%' ORDER BY name;";
            using var r = q.ExecuteReader();
            while (r.Read()) tables.Add(r.GetString(0));
        }

        foreach (var table in tables)
        {
            sb.Append("TABLE ").Append(table).Append('\n');

            var columns = new List<string>();
            using (var q = conn.CreateCommand())
            {
                q.CommandText = $"PRAGMA table_info(\"{table}\");";
                using var r = q.ExecuteReader();
                while (r.Read()) columns.Add(r.GetString(1));
            }
            columns.Sort(StringComparer.Ordinal);
            sb.Append("COLS ").Append(string.Join('|', columns)).Append('\n');

            var projection = string.Join(", ", columns.Select(c => $"\"{c}\""));
            using (var q = conn.CreateCommand())
            {
                q.CommandText = $"SELECT {projection} FROM \"{table}\" ORDER BY {projection};";
                using var r = q.ExecuteReader();
                while (r.Read())
                {
                    for (var i = 0; i < r.FieldCount; i++)
                    {
                        if (i > 0) sb.Append(CellSeparator);
                        sb.Append(r.IsDBNull(i) ? "<null>" : Convert.ToString(r.GetValue(i),
                            System.Globalization.CultureInfo.InvariantCulture));
                    }
                    sb.Append('\n');
                }
            }
        }

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString()))).ToLowerInvariant();
    }

    private static string IntegrityCheck(string dbPath)
    {
        using var conn = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = dbPath,
            Mode = SqliteOpenMode.ReadWrite,
        }.ToString());
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "PRAGMA integrity_check;";
        return Convert.ToString(cmd.ExecuteScalar()) ?? "";
    }

    /// <summary>Rewrites an archive with its manifest replaced by <paramref name="mutate"/>'s output.</summary>
    private static void RewriteManifest(string archivePath, Func<string, string> mutate)
    {
        byte[] db;
        string manifestJson;
        using (var zip = ZipFile.OpenRead(archivePath))
        {
            using var dbStream = zip.GetEntry("company.db")!.Open();
            using var buf = new MemoryStream();
            dbStream.CopyTo(buf);
            db = buf.ToArray();

            using var mStream = zip.GetEntry("manifest.json")!.Open();
            using var reader = new StreamReader(mStream, Encoding.UTF8);
            manifestJson = reader.ReadToEnd();
        }

        File.Delete(archivePath);
        using var zipOut = ZipFile.Open(archivePath, ZipArchiveMode.Create);
        using (var e = zipOut.CreateEntry("manifest.json").Open())
            e.Write(Encoding.UTF8.GetBytes(mutate(manifestJson)));
        using (var e = zipOut.CreateEntry("company.db").Open())
            e.Write(db);
    }

    // ================================================================ 1. THE RESTORE ROUND TRIP

    [Fact]
    [Trait("Category", "RoundTrip")]
    public void Destroyed_company_file_is_restored_byte_for_byte_and_still_reconciles_to_the_paisa()
    {
        var company = OddPaisaCompany();
        var dbPath = SaveFixture(company);

        // Baseline: reports on the saved-and-reloaded company, in odd paisa.
        Company before;
        using (var store = new SqliteCompanyStore(dbPath)) before = store.Load(company.Id)!;
        var tbBefore = TrialBalance.Build(before, AsOf);
        Assert.Equal(115_305.59m, tbBefore.TotalDebit.Amount);
        Assert.Equal(115_305.59m, tbBefore.TotalCredit.Amount);
        SqliteConnection.ClearAllPools();

        var contentBefore = ContentDigest(dbPath);
        SqliteConnection.ClearAllPools();

        // ---- Act 1: back up. ----
        var archive = Path_("company" + CompanyBackup.ArchiveExtension);
        var manifest = CompanyBackup.Create(dbPath, archive, TakenAt);
        Assert.True(File.Exists(archive));
        Assert.Equal(Schema.CurrentVersion, manifest.SchemaVersion);

        // ---- Act 2: DESTROY the original. Not "delete" — overwrite it with garbage, the way a bad
        //             disk or a half-written save actually loses a file. ----
        SqliteConnection.ClearAllPools();
        File.WriteAllBytes(dbPath, Encoding.ASCII.GetBytes(new string('X', 40_000)));
        Assert.Throws<SqliteException>(() =>
        {
            using var dead = new SqliteConnection($"Data Source={dbPath}");
            dead.Open();
            using var cmd = dead.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM ledgers;";
            cmd.ExecuteScalar();
        });
        SqliteConnection.ClearAllPools();

        // ---- Act 3: restore. ----
        var restored = CompanyBackup.Restore(archive, dbPath);
        Assert.Equal(manifest.DatabaseSha256, restored.DatabaseSha256);

        // ---- Assert: the file is byte-identical to the snapshot the backup captured. ----
        Assert.Equal(manifest.DatabaseSha256, Sha256OfFile(dbPath));
        Assert.Equal(manifest.DatabaseBytes, new FileInfo(dbPath).Length);

        // ---- Assert: every table's contents are byte-identical to before the destruction. ----
        SqliteConnection.ClearAllPools();
        Assert.Equal(contentBefore, ContentDigest(dbPath));
        SqliteConnection.ClearAllPools();
        Assert.StartsWith("ok", IntegrityCheck(dbPath));
        SqliteConnection.ClearAllPools();

        // ---- Assert: the engine reloads it and the reports still reconcile to the paisa. ----
        Company after;
        using (var store = new SqliteCompanyStore(dbPath)) after = store.Load(company.Id)!;

        Assert.Equal(before.Name, after.Name);
        Assert.Equal(before.Ledgers.Count, after.Ledgers.Count);
        Assert.Equal(before.Vouchers.Count, after.Vouchers.Count);
        Assert.Equal(before.Vouchers.Sum(v => v.Lines.Count), after.Vouchers.Sum(v => v.Lines.Count));

        var tbAfter = TrialBalance.Build(after, AsOf);
        Assert.Equal(115_305.59m, tbAfter.TotalDebit.Amount);
        Assert.Equal(115_305.59m, tbAfter.TotalCredit.Amount);
        Assert.True(tbAfter.Balanced);

        // Bill-wise detail survives to the paisa (MT/001 33,102.91 − 19,447.33 = 13,655.58 outstanding).
        var outstanding = Outstandings.Build(after, AsOf);
        var mt001 = outstanding.Receivables.Single(b => b.Reference == "MT/001");
        Assert.Equal(13_655.58m, mt001.Pending.Amount);
        var ks77 = outstanding.Payables.Single(b => b.Reference == "KS/77");
        Assert.Equal(16_752.53m, ks77.Pending.Amount);
    }

    [Fact]
    public void Restoring_over_a_different_company_replaces_it_wholesale()
    {
        var keeper = OddPaisaCompany("Keeper Co");
        var keeperDb = SaveFixture(keeper, "keeper.db");
        var archive = Path_("keeper" + CompanyBackup.ArchiveExtension);
        CompanyBackup.Create(keeperDb, archive, TakenAt);
        SqliteConnection.ClearAllPools();
        var keeperContent = ContentDigest(keeperDb);
        SqliteConnection.ClearAllPools();

        // A completely different company file, which the restore must overwrite.
        var other = CompanyFactory.CreateSeeded("Doomed Co", new DateOnly(2025, 4, 1));
        var otherDb = SaveFixture(other, "other.db");
        SqliteConnection.ClearAllPools();
        Assert.NotEqual(keeperContent, ContentDigest(otherDb));
        SqliteConnection.ClearAllPools();

        CompanyBackup.Restore(archive, otherDb);

        SqliteConnection.ClearAllPools();
        Assert.Equal(keeperContent, ContentDigest(otherDb));
        SqliteConnection.ClearAllPools();
        using var store = new SqliteCompanyStore(otherDb);
        Assert.Equal("Keeper Co", store.Load(keeper.Id)!.Name);
    }

    [Fact]
    public void Backup_of_a_restored_backup_carries_the_identical_payload()
    {
        var dbPath = SaveFixture(OddPaisaCompany("Idempotent Co"));
        var first = Path_("first" + CompanyBackup.ArchiveExtension);
        var m1 = CompanyBackup.Create(dbPath, first, TakenAt);

        var target = Path_("target.db");
        CompanyBackup.Restore(first, target);
        SqliteConnection.ClearAllPools();

        var second = Path_("second" + CompanyBackup.ArchiveExtension);
        var m2 = CompanyBackup.Create(target, second, TakenAt.AddHours(3));

        Assert.Equal(m1.DatabaseSha256, m2.DatabaseSha256);
        Assert.Equal(m1.DatabaseBytes, m2.DatabaseBytes);
        Assert.Equal(m1.SchemaVersion, m2.SchemaVersion);
        Assert.NotEqual(m1.TakenAtUtc, m2.TakenAtUtc);
    }

    // ================================================================ 2. THE MECHANISM (not a file copy)

    /// <summary>
    /// ⚠️ THIS TEST DOES NOT DISCRIMINATE THE MECHANISM, and must never be cited as evidence for it. In
    /// rollback-journal mode (the app's production mode) an uncommitted transaction that has not spilled lives in
    /// the journal, and the main <c>.db</c> on disk still holds exactly the committed state — so a plain
    /// <c>File.Copy</c> passes every assertion below. Verified by mutation, not assumed. What it DOES pin is real
    /// and worth keeping: that a backup taken while a write transaction is in flight comes back
    /// <c>integrity_check</c>-clean, contains every committed row byte-for-byte, and contains none of the
    /// in-flight ones. For the mechanism, see
    /// <see cref="A_plain_file_copy_loses_committed_data_that_the_backup_api_captures"/> — the only test that
    /// proves it.
    /// </summary>
    [Fact]
    public void Backup_taken_mid_write_excludes_the_uncommitted_transaction_and_is_intact()
    {
        var company = OddPaisaCompany("Mid Write Co");
        var dbPath = SaveFixture(company);
        SqliteConnection.ClearAllPools();
        var committedContent = ContentDigest(dbPath);
        SqliteConnection.ClearAllPools();

        var archive = Path_("midwrite" + CompanyBackup.ArchiveExtension);

        // A second connection opens a write transaction, dirties EVERY ledger row and every entry-line amount —
        // and never commits. This is exactly the state a "just copy the file" backup would capture half of.
        using (var writer = new SqliteConnection($"Data Source={dbPath}"))
        {
            writer.Open();
            using var tx = writer.BeginTransaction();
            foreach (var sql in new[]
            {
                "UPDATE ledgers SET name = 'GHOST ' || name;",
                "UPDATE entry_lines SET amount_paisa = amount_paisa + 1;",
            })
            {
                using var mut = writer.CreateCommand();
                mut.Transaction = tx;
                mut.CommandText = sql;
                mut.ExecuteNonQuery();
            }

            // The snapshot is taken WHILE that transaction is in flight.
            CompanyBackup.Create(dbPath, archive, TakenAt);

            tx.Rollback();
        }
        SqliteConnection.ClearAllPools();

        var target = Path_("midwrite-restored.db");
        CompanyBackup.Restore(archive, target);

        SqliteConnection.ClearAllPools();
        Assert.StartsWith("ok", IntegrityCheck(target));
        SqliteConnection.ClearAllPools();

        // The in-flight rows are absent and everything committed is present, byte for byte.
        Assert.Equal(committedContent, ContentDigest(target));
        SqliteConnection.ClearAllPools();

        using var check = new SqliteConnection($"Data Source={target}");
        check.Open();
        using var q = check.CreateCommand();
        q.CommandText = "SELECT COUNT(*) FROM ledgers WHERE name LIKE 'GHOST %';";
        Assert.Equal(0L, (long)q.ExecuteScalar()!);
        q.CommandText = "SELECT COUNT(*) FROM ledgers WHERE name = 'Cash';";
        Assert.Equal(1L, (long)q.ExecuteScalar()!);
    }

    [Fact]
    public void A_plain_file_copy_loses_committed_data_that_the_backup_api_captures()
    {
        // The app runs SQLite in its default (rollback-journal) mode today, but this is the decisive, deterministic
        // demonstration of WHY a file copy is not a backup: in WAL mode a COMMITTED transaction lives in the -wal
        // sidecar until a checkpoint, so copying the .db alone silently returns a database missing committed work.
        var dbPath = SaveFixture(OddPaisaCompany("WAL Co"), "wal.db");
        SqliteConnection.ClearAllPools();

        using var live = new SqliteConnection($"Data Source={dbPath}");
        live.Open();
        Exec(live, "PRAGMA journal_mode=WAL;");
        Exec(live, "CREATE TABLE backup_probe(id INTEGER PRIMARY KEY, paisa INTEGER NOT NULL);");
        Exec(live, "INSERT INTO backup_probe(id, paisa) VALUES (1, 1234567);");
        Exec(live, "PRAGMA wal_checkpoint(TRUNCATE);");   // everything so far is now in the .db file itself
        Exec(live, "INSERT INTO backup_probe(id, paisa) VALUES (2, 8901234);");   // COMMITTED — only in the -wal
        Exec(live, "INSERT INTO backup_probe(id, paisa) VALUES (3, 5678901);");   // COMMITTED — only in the -wal

        // (a) a naive copy of the .db file, exactly what "just copy the data file" means in practice.
        var naive = Path_("naive-copy.db");
        File.Copy(dbPath, naive, overwrite: true);

        // (b) the real backup, taken at the same instant, with the connection still open.
        var archive = Path_("wal" + CompanyBackup.ArchiveExtension);
        CompanyBackup.Create(dbPath, archive, TakenAt);

        var restored = Path_("wal-restored.db");
        CompanyBackup.Restore(archive, restored);

        static long CountProbeRows(string path)
        {
            using var c = new SqliteConnection($"Data Source={path};Mode=ReadWrite");
            c.Open();
            using var q = c.CreateCommand();
            q.CommandText = "SELECT COUNT(*) FROM backup_probe;";
            return (long)q.ExecuteScalar()!;
        }

        var naiveRows = CountProbeRows(naive);
        var restoredRows = CountProbeRows(restored);
        SqliteConnection.ClearAllPools();

        Assert.Equal(3L, restoredRows);                 // the backup API captured every committed row
        Assert.Equal(1L, naiveRows);                    // the file copy lost two of them, silently
        Assert.True(restoredRows > naiveRows,
            "A plain file copy must be shown to lose committed data that the SQLite backup API preserves.");

        live.Close();
    }

    [Fact]
    public void A_backup_refuses_a_file_a_spilled_transaction_has_locked_where_a_file_copy_would_lie()
    {
        // A transaction large enough that SQLite SPILLS its dirty pages into the main database file takes an
        // EXCLUSIVE lock — so the .db on disk now physically contains pages of a half-applied transaction whose
        // undo information lives only in the rollback journal.
        //
        // A file copy taken at this instant is worthless: it either carries the uncommitted data or is outright
        // unreadable, and it says nothing about either. The backup API cannot read a database under an EXCLUSIVE
        // lock at all, so the operation FAILS LOUDLY and writes no archive. Failing to take a backup is
        // recoverable — "try again in a moment". Writing a backup that cannot be restored is not.
        var company = OddPaisaCompany("Spilled Co");
        var dbPath = SaveFixture(company, "spilled.db");
        SqliteConnection.ClearAllPools();
        var committedContent = ContentDigest(dbPath);
        SqliteConnection.ClearAllPools();

        var archive = Path_("spilled" + CompanyBackup.ArchiveExtension);
        var naive = Path_("spilled-naive-copy.db");

        using (var writer = new SqliteConnection($"Data Source={dbPath}"))
        {
            writer.Open();
            Exec(writer, "PRAGMA cache_size = 1;");   // a one-page cache cannot hold the transaction

            using var tx = writer.BeginTransaction();
            foreach (var sql in new[]
            {
                "UPDATE ledgers SET name = 'GHOST ' || name;",
                "UPDATE entry_lines SET amount_paisa = amount_paisa + 1;",
                "UPDATE vouchers SET narration = 'GHOST NARRATION, LONG ENOUGH TO SPILL PAGES TO THE MAIN FILE';",
            })
            {
                using var mut = writer.CreateCommand();
                mut.Transaction = tx;
                mut.CommandText = sql;
                mut.ExecuteNonQuery();
            }

            // (a) what "just copy the file" produces at this instant.
            File.Copy(dbPath, naive, overwrite: true);

            // (b) what the backup produces: nothing, and it says why.
            var ex = Assert.Throws<CompanyBackupException>(() => CompanyBackup.Create(dbPath, archive, TakenAt));
            Assert.Contains("busy", ex.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("No backup has been written", ex.Message, StringComparison.OrdinalIgnoreCase);

            tx.Rollback();
        }
        SqliteConnection.ClearAllPools();

        // No half-written archive and no temporaries were left behind.
        Assert.False(File.Exists(archive), "a backup archive was written despite the failure");
        Assert.False(File.Exists(archive + ".apex-backup-tmp"), "the snapshot staging file was left behind");
        Assert.False(File.Exists(archive + ".apex-backup-part"), "the partial archive was left behind");

        // And the file copy is provably NOT the committed state — it is either corrupt or carries the
        // uncommitted transaction. Either way it is not a backup, and nothing about it says so.
        string naiveDescription;
        try
        {
            naiveDescription = ContentDigest(naive);
        }
        catch (SqliteException ex)
        {
            naiveDescription = "unreadable: " + ex.Message;      // equally damning
        }
        SqliteConnection.ClearAllPools();
        Assert.NotEqual(committedContent, naiveDescription);
    }

    [Fact]
    public void The_snapshot_passes_its_own_integrity_check_before_it_is_archived()
    {
        var dbPath = SaveFixture(OddPaisaCompany("Integrity Co"));
        var archive = Path_("integrity" + CompanyBackup.ArchiveExtension);
        CompanyBackup.Create(dbPath, archive, TakenAt);

        var target = Path_("integrity-restored.db");
        CompanyBackup.Restore(archive, target);
        SqliteConnection.ClearAllPools();

        Assert.StartsWith("ok", IntegrityCheck(target));
        SqliteConnection.ClearAllPools();
    }

    [Fact]
    public void Restore_removes_a_stale_hot_journal_belonging_to_the_replaced_database()
    {
        // A leftover -wal/-shm/-journal beside the target belongs to the database being REPLACED. Left in place it
        // would be applied to the restored file on first open and corrupt it. The swap must clear the sidecars.
        var dbPath = SaveFixture(OddPaisaCompany("Sidecar Co"));
        var archive = Path_("sidecar" + CompanyBackup.ArchiveExtension);
        CompanyBackup.Create(dbPath, archive, TakenAt);
        SqliteConnection.ClearAllPools();

        var target = Path_("sidecar-target.db");
        File.Copy(dbPath, target);
        File.WriteAllBytes(target + "-wal", new byte[] { 0xDE, 0xAD, 0xBE, 0xEF });
        File.WriteAllBytes(target + "-shm", new byte[] { 0xDE, 0xAD });
        File.WriteAllBytes(target + "-journal", new byte[] { 0xBA, 0xAD });

        CompanyBackup.Restore(archive, target);

        Assert.False(File.Exists(target + "-wal"), "a stale -wal survived the restore");
        Assert.False(File.Exists(target + "-shm"), "a stale -shm survived the restore");
        Assert.False(File.Exists(target + "-journal"), "a stale -journal survived the restore");
        SqliteConnection.ClearAllPools();
        Assert.StartsWith("ok", IntegrityCheck(target));
        SqliteConnection.ClearAllPools();
    }

    private static void Exec(SqliteConnection conn, string sql)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    // ================================================================ 3. THE VERSION STAMP AND THE REFUSALS

    [Fact]
    public void The_manifest_stamps_the_schema_version_company_name_and_a_payload_digest()
    {
        var company = OddPaisaCompany("Stamped Co");
        var dbPath = SaveFixture(company, "stamped.db");
        var archive = Path_("stamped" + CompanyBackup.ArchiveExtension);

        var manifest = CompanyBackup.Create(dbPath, archive, TakenAt);

        Assert.Equal(CompanyBackup.FormatVersion, manifest.FormatVersion);
        Assert.Equal(Schema.CurrentVersion, manifest.SchemaVersion);
        Assert.Equal("Stamped Co", manifest.CompanyName);
        Assert.Equal("stamped.db", manifest.SourceFileName);
        Assert.Equal(TakenAt, manifest.TakenAtUtc);
        Assert.Equal(64, manifest.DatabaseSha256.Length);
        Assert.True(manifest.DatabaseBytes > 0);

        // ReadManifest sees the same thing without restoring anything.
        var read = CompanyBackup.ReadManifest(archive);
        Assert.Equal(manifest, read);

        // The archive is a plain ZIP with exactly the two documented entries — inspectable by hand.
        using var zip = ZipFile.OpenRead(archive);
        Assert.Equal(new[] { "company.db", "manifest.json" },
            zip.Entries.Select(e => e.FullName).OrderBy(n => n, StringComparer.Ordinal).ToArray());
    }

    [Fact]
    public void Restore_refuses_a_backup_stamped_with_a_newer_schema_version_and_changes_nothing()
    {
        // A GENUINE future backup: the payload's own schema_version row really is v+1, and the manifest agrees
        // with it. Restamping only the manifest would be caught by the stamp-vs-payload cross-check instead, and
        // this test would then pass without the version refusal existing at all.
        var future = Schema.CurrentVersion + 1;
        var dbPath = SaveFixture(OddPaisaCompany("Newer Co"), "newer.db");
        using (var bump = new SqliteConnection($"Data Source={dbPath}"))
        {
            bump.Open();
            using var cmd = bump.CreateCommand();
            cmd.CommandText = "UPDATE schema_version SET version = $v;";
            cmd.Parameters.AddWithValue("$v", future);
            cmd.ExecuteNonQuery();
        }
        SqliteConnection.ClearAllPools();

        var archive = Path_("newer" + CompanyBackup.ArchiveExtension);
        var manifest = CompanyBackup.Create(dbPath, archive, TakenAt);
        Assert.Equal(future, manifest.SchemaVersion);
        SqliteConnection.ClearAllPools();

        var target = Path_("newer-target.db");
        File.Copy(dbPath, target);
        var targetBefore = Sha256OfFile(target);

        var ex = Assert.Throws<CompanyBackupException>(() => CompanyBackup.Restore(archive, target));

        // The message must be the VERSION refusal — naming both versions, telling the user what to do, and
        // saying nothing was changed. This is the whole point of stamping the version into the manifest.
        Assert.Contains($"v{future}", ex.Message);
        Assert.Contains($"v{Schema.CurrentVersion}", ex.Message);
        Assert.Contains("newer version of Apex Solutions", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Update Apex Solutions", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Nothing has been changed", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("damaged", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("tally", ex.Message, StringComparison.OrdinalIgnoreCase);

        // And the target is byte-identical to what it was.
        Assert.Equal(targetBefore, Sha256OfFile(target));
        Assert.False(CompanyBackup.CanRestoreSchemaVersion(future));
        Assert.True(CompanyBackup.CanRestoreSchemaVersion(Schema.CurrentVersion));
        Assert.True(CompanyBackup.CanRestoreSchemaVersion(1));
        Assert.False(CompanyBackup.CanRestoreSchemaVersion(0));
    }

    [Fact]
    public void Restore_refuses_an_unknown_container_format_and_changes_nothing()
    {
        var dbPath = SaveFixture(OddPaisaCompany("Container Co"));
        var archive = Path_("container" + CompanyBackup.ArchiveExtension);
        CompanyBackup.Create(dbPath, archive, TakenAt);
        SqliteConnection.ClearAllPools();

        RewriteManifest(archive, json =>
        {
            using var doc = JsonDocument.Parse(json);
            var dict = doc.RootElement.EnumerateObject()
                .ToDictionary(p => p.Name, p => (object?)(p.Name == "formatVersion" ? 99 : Raw(p.Value)));
            return JsonSerializer.Serialize(dict);
        });

        var target = Path_("container-target.db");
        File.Copy(dbPath, target);
        var before = Sha256OfFile(target);

        var ex = Assert.Throws<CompanyBackupException>(() => CompanyBackup.Restore(archive, target));
        Assert.Contains("99", ex.Message);
        Assert.Equal(before, Sha256OfFile(target));
    }

    [Fact]
    public void Restore_refuses_a_payload_whose_digest_does_not_match_and_changes_nothing()
    {
        var dbPath = SaveFixture(OddPaisaCompany("Tamper Co"));
        var archive = Path_("tamper" + CompanyBackup.ArchiveExtension);
        CompanyBackup.Create(dbPath, archive, TakenAt);
        SqliteConnection.ClearAllPools();

        // Flip bytes deep inside the payload — a truncated download, a bad USB stick, a hand edit.
        byte[] db;
        string manifestJson;
        using (var zip = ZipFile.OpenRead(archive))
        {
            using var s = zip.GetEntry("company.db")!.Open();
            using var buf = new MemoryStream();
            s.CopyTo(buf);
            db = buf.ToArray();
            using var m = zip.GetEntry("manifest.json")!.Open();
            manifestJson = new StreamReader(m, Encoding.UTF8).ReadToEnd();
        }
        for (var i = db.Length / 2; i < db.Length / 2 + 64 && i < db.Length; i++) db[i] ^= 0xFF;

        File.Delete(archive);
        using (var zip = ZipFile.Open(archive, ZipArchiveMode.Create))
        {
            using (var e = zip.CreateEntry("manifest.json").Open()) e.Write(Encoding.UTF8.GetBytes(manifestJson));
            using (var e = zip.CreateEntry("company.db").Open()) e.Write(db);
        }

        var target = Path_("tamper-target.db");
        File.Copy(dbPath, target);
        var before = Sha256OfFile(target);

        var ex = Assert.Throws<CompanyBackupException>(() => CompanyBackup.Restore(archive, target));
        Assert.Contains("damaged", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(before, Sha256OfFile(target));
        Assert.False(File.Exists(target + ".apex-restore-tmp"), "the staging file was left behind");
    }

    [Fact]
    public void Restore_refuses_a_file_that_is_not_a_backup_archive_and_changes_nothing()
    {
        var dbPath = SaveFixture(OddPaisaCompany("NotAnArchive Co"));
        var junk = Path_("holiday-photo.apexbak");
        File.WriteAllBytes(junk, Encoding.ASCII.GetBytes("this is not a zip file, it is a jpeg, honest"));

        var target = Path_("junk-target.db");
        File.Copy(dbPath, target);
        var before = Sha256OfFile(target);

        Assert.Throws<CompanyBackupException>(() => CompanyBackup.Restore(junk, target));
        Assert.Equal(before, Sha256OfFile(target));

        Assert.Throws<CompanyBackupException>(() => CompanyBackup.ReadManifest(junk));
    }

    [Fact]
    public void Restore_refuses_an_archive_whose_stamp_contradicts_its_payload_and_changes_nothing()
    {
        var dbPath = SaveFixture(OddPaisaCompany("Contradiction Co"));
        var archive = Path_("contradiction" + CompanyBackup.ArchiveExtension);
        CompanyBackup.Create(dbPath, archive, TakenAt);
        SqliteConnection.ClearAllPools();

        // Claim an older schema version than the payload actually carries: a hand-edited manifest, or a bug.
        var lie = Schema.CurrentVersion - 1;
        RewriteManifest(archive, json =>
        {
            using var doc = JsonDocument.Parse(json);
            var dict = doc.RootElement.EnumerateObject()
                .ToDictionary(p => p.Name, p => (object?)(p.Name == "schemaVersion" ? lie : Raw(p.Value)));
            return JsonSerializer.Serialize(dict);
        });

        var target = Path_("contradiction-target.db");
        File.Copy(dbPath, target);
        var before = Sha256OfFile(target);

        var ex = Assert.Throws<CompanyBackupException>(() => CompanyBackup.Restore(archive, target));
        Assert.Contains("damaged", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(before, Sha256OfFile(target));
    }

    [Fact]
    public void A_failed_backup_over_an_existing_one_leaves_the_good_backup_untouched()
    {
        // NOTE on what this does and does not pin. It pins that a backup which FAILS does not destroy the
        // previous good archive at the same path — the nightly-backup-over-the-same-name case. The archive is
        // additionally built under a temporary name and moved into place only on success, so a failure part-way
        // through WRITING the zip cannot leave a truncated file either; that second guarantee is defence in
        // depth and is NOT covered here, because forcing a mid-zip failure would need a production seam whose
        // cost is worse than the risk.
        var dbPath = SaveFixture(OddPaisaCompany("Overwrite Co"));
        var archive = Path_("nightly" + CompanyBackup.ArchiveExtension);
        var good = CompanyBackup.Create(dbPath, archive, TakenAt);
        SqliteConnection.ClearAllPools();
        var goodDigest = Sha256OfFile(archive);

        // Today's run, over the same file name, against a source that is now unreadable.
        var broken = Path_("broken.db");
        File.WriteAllBytes(broken, Encoding.ASCII.GetBytes(new string('Q', 8192)));

        Assert.Throws<CompanyBackupException>(() => CompanyBackup.Create(broken, archive, TakenAt.AddDays(1)));

        Assert.True(File.Exists(archive), "the previous good backup was deleted by a failed run");
        Assert.Equal(goodDigest, Sha256OfFile(archive));
        Assert.Equal(good, CompanyBackup.ReadManifest(archive));

        // And it still restores.
        var target = Path_("overwrite-target.db");
        CompanyBackup.Restore(archive, target);
        SqliteConnection.ClearAllPools();
        Assert.StartsWith("ok", IntegrityCheck(target));
        SqliteConnection.ClearAllPools();
    }

    [Fact]
    public void Backup_refuses_a_source_that_is_missing_or_is_not_a_database()
    {
        Assert.Throws<CompanyBackupException>(() =>
            CompanyBackup.Create(Path_("nope.db"), Path_("nope.apexbak"), TakenAt));

        var notADb = Path_("notadb.db");
        File.WriteAllBytes(notADb, Encoding.ASCII.GetBytes(new string('Z', 8192)));
        Assert.Throws<CompanyBackupException>(() =>
            CompanyBackup.Create(notADb, Path_("notadb.apexbak"), TakenAt));
    }

    // ================================================================ 4. AN OLDER BACKUP STILL RESTORES

    [Fact]
    [Trait("Category", "RoundTrip")]
    public void A_backup_taken_at_an_older_schema_version_restores_and_is_migrated_forward()
    {
        var company = OddPaisaCompany("Legacy Co");
        var dbPath = SaveFixture(company, "legacy.db");

        // Manufacture a GENUINE v48 database carrying real rows, using the house downgrade helper.
        using (var conn = new SqliteConnection($"Data Source={dbPath}"))
        {
            conn.Open();
            SchemaDowngrade.V52ToV51(conn);   // v52 voucher edit log
            SchemaDowngrade.V51ToV50(conn);   // v51 GST five-level hierarchy masters
            SchemaDowngrade.V50ToV49(conn);   // v50 negative-stock warn flag
            SchemaDowngrade.V49ToV48(conn);
        }
        SqliteConnection.ClearAllPools();

        var archive = Path_("legacy" + CompanyBackup.ArchiveExtension);
        var manifest = CompanyBackup.Create(dbPath, archive, TakenAt);
        Assert.Equal(48, manifest.SchemaVersion);
        Assert.True(CompanyBackup.CanRestoreSchemaVersion(48));

        var target = Path_("legacy-target.db");
        CompanyBackup.Restore(archive, target);
        SqliteConnection.ClearAllPools();

        // Opening it runs the real v48→v49 migration and the data is intact, to the paisa.
        using var store = new SqliteCompanyStore(target);
        var reloaded = store.Load(company.Id)!;
        var tb = TrialBalance.Build(reloaded, AsOf);
        Assert.Equal(115_305.59m, tb.TotalDebit.Amount);
        Assert.Equal(115_305.59m, tb.TotalCredit.Amount);
    }

    // ================================================================ 5. THE ORIGINAL SURVIVES A FAILED RESTORE
    //
    // Every "and_changes_nothing" test above exercises a REFUSAL — a path that returns before the swap is
    // reached at all. The only window in which a restore can DAMAGE the company it was run to protect is the
    // swap itself: something in the park/replace sequence throws after the sidecars have been moved aside, and
    // the restore does not happen. The original — file AND its -wal/-shm/-journal crash-recovery sidecars —
    // must come through that untouched, because those sidecars are the only thing that can roll a half-written
    // company file back, and a user reaches for a restore precisely when their file is in that state.
    //
    // 🔴 HOW THE SWAP IS MADE TO FAIL, AND WHY IT IS NOT A HELD-OPEN FILE ANY MORE.
    // This test used to obstruct the swap by holding the target open with FileShare.Read, and that is a
    // WINDOWS-ONLY mechanism dressed up as a universal one. On Windows a share-mode denial genuinely makes a
    // file un-replaceable. On Unix, .NET emulates FileShare with advisory flock() locks that bind only other
    // .NET FileStreams, while the swap (CompanyBackup.cs — File.Move(staging, target, overwrite: true)) is
    // rename(2): rename consults no lock and no open descriptor, it unlinks a directory entry and relinks it,
    // and the holder simply keeps reading the old inode. So on Linux and macOS every park succeeded, the
    // replace succeeded, the restore COMPLETED, and Assert.Throws saw nothing — the test failed for a reason
    // that had nothing to do with the behaviour it exists to pin.
    //
    // The obstruction below is portable because it is not about locking at all: a DIRECTORY is planted on the
    // path the "-journal" sidecar must be parked to. SafeDelete is a no-op there (File.Exists is false for a
    // directory — measured on Windows), and the File.Move that follows cannot overwrite a directory on ANY
    // platform: Windows raises UnauthorizedAccessException ("Access to the path is denied" — measured here on
    // .NET 10), Unix raises EISDIR, which .NET surfaces as UnauthorizedAccessException/IOException. Both are
    // inside Restore's catch filter, so the message still opens "Could not restore the backup".
    //
    // It also lands in a STRICTLY better place than the old one: SidecarSuffixes is ["-wal","-shm","-journal"],
    // so -wal and -shm park successfully first and the throw happens with two real files parked — which means
    // this now exercises Unpark for real, where the held-open version failed before anything was parked at all.

    [Fact]
    public void A_restore_that_fails_at_the_swap_leaves_the_original_and_its_recovery_sidecars_intact()
    {
        // The backup being restored is a DIFFERENT company, so "did the restore happen?" is answerable.
        var keeper = OddPaisaCompany("Keeper Co");
        var keeperDb = SaveFixture(keeper, "swapfail-keeper.db");
        var archive = Path_("swapfail" + CompanyBackup.ArchiveExtension);
        CompanyBackup.Create(keeperDb, archive, TakenAt);
        SqliteConnection.ClearAllPools();

        // The live company, with a hot rollback journal beside it — the state a power cut mid-save leaves, and
        // the state a user runs a restore FROM.
        var victim = OddPaisaCompany("Victim Co");
        var target = SaveFixture(victim, "swapfail-victim.db");
        SqliteConnection.ClearAllPools();

        File.WriteAllBytes(target + "-journal", Encoding.ASCII.GetBytes("hot-journal 41234.57 undo pages"));
        File.WriteAllBytes(target + "-wal", Encoding.ASCII.GetBytes("committed-but-uncheckpointed 13655.58"));
        File.WriteAllBytes(target + "-shm", Encoding.ASCII.GetBytes("shared-index"));

        var targetBefore = Sha256OfFile(target);
        var journalBefore = Sha256OfFile(target + "-journal");
        var walBefore = Sha256OfFile(target + "-wal");
        var shmBefore = Sha256OfFile(target + "-shm");

        // ---- Act: restore while the swap cannot complete. Every validation passes; the SWAP fails. ----
        // A directory standing exactly where the "-journal" sidecar must be parked. -wal and -shm park first
        // and succeed, so the failure happens mid-park with two real files already moved aside.
        Directory.CreateDirectory(target + "-journal" + ".apex-restore-old");

        var ex = Assert.Throws<CompanyBackupException>(() => CompanyBackup.Restore(archive, target));

        Assert.Contains("Could not restore the backup", ex.Message, StringComparison.OrdinalIgnoreCase);

        // ---- Assert: the restore did NOT happen, and NOTHING about the original was degraded. ----
        Assert.Equal(targetBefore, Sha256OfFile(target));

        Assert.True(File.Exists(target + "-journal"),
            "the target's hot rollback journal was deleted by a restore that then failed — the original is unrecoverable");
        Assert.True(File.Exists(target + "-wal"),
            "the target's -wal was deleted by a restore that then failed — committed, uncheckpointed work is gone");
        Assert.True(File.Exists(target + "-shm"), "the target's -shm was deleted by a restore that then failed");

        Assert.Equal(journalBefore, Sha256OfFile(target + "-journal"));
        Assert.Equal(walBefore, Sha256OfFile(target + "-wal"));
        Assert.Equal(shmBefore, Sha256OfFile(target + "-shm"));

        // No debris from the abandoned swap. The "-wal" and "-shm" legs are the load-bearing ones here: those
        // two DID park and Unpark had to put them back, so a parked file left behind would show up there.
        // (The "-journal" leg is weaker by construction — this test's own obstruction occupies that name, so
        // File.Exists is false for it whatever happens. It is still checked, and paired with a Directory.Exists
        // so that a swap which somehow clobbered the obstruction would not slip past as "no debris".)
        Assert.False(File.Exists(target + ".apex-restore-tmp"), "the staging file was left behind");
        foreach (var suffix in new[] { "", "-journal", "-wal", "-shm" })
            Assert.False(File.Exists(target + suffix + ".apex-restore-old"),
                $"a parked '{suffix}' was left behind by the failed swap");
        Assert.True(Directory.Exists(target + "-journal" + ".apex-restore-old"),
            "the planted obstruction is gone — the swap did not fail where this test believes it failed");

        // ---- Assert: the original is not merely byte-identical, it is USABLE. (The planted sidecars are this
        //      test's own junk, not real SQLite files, so they are cleared before the engine opens the file.) ----
        foreach (var suffix in new[] { "-journal", "-wal", "-shm" }) File.Delete(target + suffix);
        SqliteConnection.ClearAllPools();

        using var store = new SqliteCompanyStore(target);
        var reloaded = store.Load(victim.Id)!;
        Assert.Equal("Victim Co", reloaded.Name);

        var tb = TrialBalance.Build(reloaded, AsOf);
        Assert.Equal(115_305.59m, tb.TotalDebit.Amount);
        Assert.Equal(115_305.59m, tb.TotalCredit.Amount);
        Assert.True(tb.Balanced);

        var outstanding = Outstandings.Build(reloaded, AsOf);
        Assert.Equal(13_655.58m, outstanding.Receivables.Single(b => b.Reference == "MT/001").Pending.Amount);
        Assert.Equal(16_752.53m, outstanding.Payables.Single(b => b.Reference == "KS/77").Pending.Amount);
    }

    /// <summary>
    /// The held-open target, kept as a scenario but asserted PER PLATFORM, because the platforms genuinely
    /// differ and pretending otherwise is what made the old test fail on CI.
    /// <para><b>Windows:</b> a share-mode denial makes the target un-replaceable, the swap throws, and the
    /// original plus its sidecars must survive — the AV-scanner / second-instance / indexing-agent case.</para>
    /// <para><b>Linux and macOS:</b> the swap is <c>rename(2)</c>, which no advisory lock and no open
    /// descriptor can block, so the restore is EXPECTED to succeed. That is correct behaviour, not a gap: a
    /// file being held open on POSIX really does not make it un-replaceable, and the Windows failure mode has
    /// no POSIX analogue to pin.</para>
    /// <para>This test is deliberately NOT skipped on POSIX. A test that silently does not run on a platform
    /// is worse than no test, so both branches assert something real, and the pair share one invariant that
    /// must hold everywhere: the restore either COMPLETED or CHANGED NOTHING — never half of each. The
    /// portable pin on the failure path itself is the test above, which runs on all three platforms.</para>
    /// </summary>
    [Fact]
    public void A_target_held_open_blocks_the_swap_on_Windows_and_is_replaced_regardless_on_POSIX()
    {
        var keeper = OddPaisaCompany("Keeper Co");
        var keeperDb = SaveFixture(keeper, "holdopen-keeper.db");
        var archive = Path_("holdopen" + CompanyBackup.ArchiveExtension);
        CompanyBackup.Create(keeperDb, archive, TakenAt);
        SqliteConnection.ClearAllPools();

        var victim = OddPaisaCompany("Victim Co");
        var target = SaveFixture(victim, "holdopen-victim.db");
        SqliteConnection.ClearAllPools();

        File.WriteAllBytes(target + "-journal", Encoding.ASCII.GetBytes("hot-journal 41234.57 undo pages"));
        var targetBefore = Sha256OfFile(target);
        var journalBefore = Sha256OfFile(target + "-journal");

        // The scanner / second instance: readable by others, but on Windows not deletable or replaceable.
        CompanyBackupException? thrown = null;
        using (new FileStream(target, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            try { CompanyBackup.Restore(archive, target); }
            catch (CompanyBackupException ex) { thrown = ex; }
        }
        SqliteConnection.ClearAllPools();

        if (OperatingSystem.IsWindows())
        {
            Assert.NotNull(thrown);
            Assert.Contains("Could not restore the backup", thrown!.Message, StringComparison.OrdinalIgnoreCase);
        }
        else
        {
            // rename(2) cannot be blocked by a holder. If THIS is what reddens on CI, the premise behind the
            // whole rewrite is wrong — .NET's FileShare emulation on this platform does obstruct the swap —
            // and the portable directory obstruction above should be re-examined too.
            Assert.True(thrown is null,
                $"a held-open target obstructed the swap on POSIX, which rename(2) should make impossible: {thrown?.Message}");
        }

        // ---- The invariant both platforms owe: completed, or changed nothing. Never half of each. ----
        Assert.False(File.Exists(target + ".apex-restore-tmp"), "the staging file was left behind");
        foreach (var suffix in new[] { "", "-journal", "-wal", "-shm" })
            Assert.False(File.Exists(target + suffix + ".apex-restore-old"),
                $"a parked '{suffix}' was left behind");

        if (thrown is not null)
        {
            // Nothing happened: the original and its only route back must both be byte-identical.
            Assert.Equal(targetBefore, Sha256OfFile(target));
            Assert.True(File.Exists(target + "-journal"),
                "a restore that then failed deleted the target's hot journal — the original is unrecoverable");
            Assert.Equal(journalBefore, Sha256OfFile(target + "-journal"));

            File.Delete(target + "-journal");
            SqliteConnection.ClearAllPools();
            using var untouched = new SqliteCompanyStore(target);
            Assert.Equal("Victim Co", untouched.Load(victim.Id)!.Name);
        }
        else
        {
            // It completed: the target IS the backup, and the replaced database's sidecar is gone rather than
            // left behind to be replayed into the restored file on first open.
            Assert.NotEqual(targetBefore, Sha256OfFile(target));
            Assert.False(File.Exists(target + "-journal"),
                "the replaced database's hot journal outlived the swap and would corrupt the restored file");

            using var restored = new SqliteCompanyStore(target);
            Assert.Equal("Keeper Co", restored.Load(keeper.Id)!.Name);
            var tb = TrialBalance.Build(restored.Load(keeper.Id)!, AsOf);
            Assert.Equal(115_305.59m, tb.TotalDebit.Amount);
            Assert.Equal(115_305.59m, tb.TotalCredit.Amount);
        }
    }

    [Fact]
    public void A_refused_restore_leaves_the_targets_recovery_sidecars_intact()
    {
        // Pins the OTHER direction of the same ordering: nothing may touch the target's sidecars BEFORE the
        // validations have passed either. Without this, moving the sidecar cleanup earlier — "to release the
        // file sooner" — would make every refusal destructive while the suite stayed green.
        var future = Schema.CurrentVersion + 1;
        var dbPath = SaveFixture(OddPaisaCompany("Refused Co"), "refused.db");
        using (var bump = new SqliteConnection($"Data Source={dbPath}"))
        {
            bump.Open();
            using var cmd = bump.CreateCommand();
            cmd.CommandText = "UPDATE schema_version SET version = $v;";
            cmd.Parameters.AddWithValue("$v", future);
            cmd.ExecuteNonQuery();
        }
        SqliteConnection.ClearAllPools();

        var archive = Path_("refused" + CompanyBackup.ArchiveExtension);
        CompanyBackup.Create(dbPath, archive, TakenAt);
        SqliteConnection.ClearAllPools();

        var target = Path_("refused-target.db");
        File.Copy(dbPath, target);
        File.WriteAllBytes(target + "-journal", Encoding.ASCII.GetBytes("hot-journal 24903.72 undo pages"));
        var targetBefore = Sha256OfFile(target);
        var journalBefore = Sha256OfFile(target + "-journal");

        Assert.Throws<CompanyBackupException>(() => CompanyBackup.Restore(archive, target));

        Assert.Equal(targetBefore, Sha256OfFile(target));
        Assert.True(File.Exists(target + "-journal"), "a refused restore deleted the target's hot journal");
        Assert.Equal(journalBefore, Sha256OfFile(target + "-journal"));
    }

    [Fact]
    public void A_failed_store_open_does_not_leave_the_company_file_locked_against_a_restore()
    {
        // The most likely in-app route to a failed swap. Opening a CORRUPT company file — precisely the case a
        // restore exists to fix — throws out of the store's constructor. If that constructor leaks its open
        // connection, the .db stays locked for the rest of the process and the restore that would have saved the
        // day cannot replace it.
        var good = OddPaisaCompany("Recovery Co");
        var goodDb = SaveFixture(good, "recovery-source.db");
        var archive = Path_("recovery" + CompanyBackup.ArchiveExtension);
        CompanyBackup.Create(goodDb, archive, TakenAt);
        SqliteConnection.ClearAllPools();

        var damaged = Path_("recovery-damaged.db");
        File.WriteAllBytes(damaged, Encoding.ASCII.GetBytes(new string('X', 40_000)));

        Assert.ThrowsAny<Exception>(() =>
        {
            using var store = new SqliteCompanyStore(damaged);
        });

        // The user now restores over the damaged file. That must WORK.
        CompanyBackup.Restore(archive, damaged);
        SqliteConnection.ClearAllPools();

        using var reopened = new SqliteCompanyStore(damaged);
        var reloaded = reopened.Load(good.Id)!;
        Assert.Equal("Recovery Co", reloaded.Name);
        var tb = TrialBalance.Build(reloaded, AsOf);
        Assert.Equal(115_305.59m, tb.TotalDebit.Amount);
        Assert.Equal(115_305.59m, tb.TotalCredit.Amount);
    }

    // ================================================================ 6. A BACKUP MUST NOT DESTROY A COMPANY

    [Fact]
    public void Backup_refuses_a_destination_that_is_a_live_company_database()
    {
        var sourceDb = SaveFixture(OddPaisaCompany("Source Co"), "guard-source.db");
        var victimDb = SaveFixture(OddPaisaCompany("Bystander Co"), "guard-victim.db");
        SqliteConnection.ClearAllPools();
        var victimBefore = Sha256OfFile(victimDb);

        var ex = Assert.Throws<CompanyBackupException>(
            () => CompanyBackup.Create(sourceDb, victimDb, TakenAt));
        Assert.Contains("company database", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Nothing has been backed up", ex.Message, StringComparison.OrdinalIgnoreCase);

        Assert.Equal(victimBefore, Sha256OfFile(victimDb));
        Assert.False(File.Exists(victimDb + ".apex-backup-tmp"), "the snapshot staging file was left behind");
        Assert.False(File.Exists(victimDb + ".apex-backup-part"), "the partial archive was left behind");

        // The bystander still opens and still reconciles to the paisa.
        SqliteConnection.ClearAllPools();
        Assert.StartsWith("ok", IntegrityCheck(victimDb));
        SqliteConnection.ClearAllPools();
    }

    [Fact]
    public void Backup_refuses_a_destination_that_is_the_company_being_backed_up()
    {
        var company = OddPaisaCompany("Self Co");
        var dbPath = SaveFixture(company, "self.db");
        SqliteConnection.ClearAllPools();
        var before = Sha256OfFile(dbPath);

        // Same path, and the same path reached by a different spelling — both must be refused.
        foreach (var destination in new[] { dbPath, Path.Combine(_dir, ".", "self.db") })
        {
            var ex = Assert.Throws<CompanyBackupException>(
                () => CompanyBackup.Create(dbPath, destination, TakenAt));
            Assert.Contains("Nothing has been backed up", ex.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(before, Sha256OfFile(dbPath));
        }

        // And the company it was told to back up is still a company, not a ZIP.
        SqliteConnection.ClearAllPools();
        using var store = new SqliteCompanyStore(dbPath);
        var tb = TrialBalance.Build(store.Load(company.Id)!, AsOf);
        Assert.Equal(115_305.59m, tb.TotalDebit.Amount);
        Assert.Equal(115_305.59m, tb.TotalCredit.Amount);
    }

    private static object? Raw(JsonElement e) => e.ValueKind switch
    {
        JsonValueKind.Number => e.GetInt64(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Null => null,
        _ => e.GetString(),
    };
}
