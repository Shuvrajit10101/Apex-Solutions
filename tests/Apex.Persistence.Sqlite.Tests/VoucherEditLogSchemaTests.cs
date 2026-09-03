using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Services;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Apex.Persistence.Sqlite.Tests;

/// <summary>
/// Schema v51 → v52 — the <b>voucher edit log</b>. Proves what this version bump owes: the new
/// <c>voucher_edit_log</c> table round-trips every verb; a genuine v51 database migrates up and gains an EMPTY
/// log with every other row untouched; the downgrade is a true inverse; and — the two decisions the whole design
/// rests on — the log survives the DELETION of the voucher it names, and it survives a whole-company
/// <see cref="SqliteCompanyStore.Save"/> from an aggregate that never loaded it.
///
/// <para>⚠️ <b>THE BACK-FILL DIRECTION.</b> The last two bumps were each caught by the opposite mistake — v51's
/// lesson was a <c>DEFAULT</c> that back-fills to the NEW behaviour and silently moves shipped figures, v50's a
/// <c>DEFAULT 0</c> that back-fills to the OLD one and silently re-ships a bug. This migration back-fills nothing
/// in either direction <b>because it cannot</b>: it adds no column to any existing table, so it contains no
/// <c>DEFAULT</c> literal and no <c>UPDATE</c>. The only "back-fill" is the new table's emptiness on an upgraded
/// book, and that is the TRUTH — nothing recorded pre-v52 edits, so any row written for one would be fabricated.
/// <see cref="An_upgraded_book_gains_an_EMPTY_log_and_nothing_else_moves"/> is the test that says so.</para>
///
/// <para>The "genuine v51 database" is manufactured with <see cref="SchemaDowngrade.V52ToV51"/> rather than
/// hand-written DDL, so the migration is exercised against real rows.</para>
/// </summary>
public sealed class VoucherEditLogSchemaTests
{
    private static readonly DateOnly FyStart = new(2025, 4, 1);
    private static readonly DateTimeOffset Instant = new(2026, 8, 19, 14, 5, 6, TimeSpan.FromHours(5.5));

    // ================================================================= the table's own shape

    [Fact]
    [Trait("Category", "RoundTrip")]
    public void A_fresh_database_carries_the_table_its_index_and_NO_foreign_keys()
    {
        var dbPath = TempDbFile.NewPath("apex-editlog-shape");
        try
        {
            using (var store = new SqliteCompanyStore(dbPath)) store.Save(Seed().Company);

            Assert.Equal(
                new[] { "before_snapshot", "company_id", "id", "recorded_at", "verb", "voucher_id" },
                ColumnNames(dbPath, "voucher_edit_log").Order(StringComparer.Ordinal).ToArray());
            Assert.Equal(1L, ReadScalar(dbPath,
                "SELECT COUNT(*) FROM sqlite_master WHERE type='index' AND name='ix_voucher_edit_log_company';"));

            // 🔴 NO FOREIGN KEYS, and both omissions are load-bearing. A vouchers FK would make a DELETE's own
            // log line unstorable — the one entry that matters most. A companies FK would break whole-company
            // Save, which deletes the companies row mid-transaction and does not own this table.
            Assert.Equal(0L, ReadScalar(dbPath, "SELECT COUNT(*) FROM pragma_foreign_key_list('voucher_edit_log');"));
        }
        finally { TempDbFile.Delete(dbPath); }
    }

    // ================================================================= round-trip, all four verbs

    [Fact]
    [Trait("Category", "RoundTrip")]
    public void Every_verb_round_trips_with_its_snapshot_verb_and_instant_intact()
    {
        var dbPath = TempDbFile.NewPath("apex-editlog-roundtrip");
        try
        {
            var k = Seed();
            var service = new LedgerService(k.Company, () => Instant);
            service.Cancel(k.FirstId);
            service.Replace(k.SecondId, SalesVoucher(k, k.SecondId, Money.FromRupees(2222.22m), "altered"));
            service.Delete(k.ThirdId);

            using (var store = new SqliteCompanyStore(dbPath)) store.Save(k.Company);

            using var reopened = new SqliteCompanyStore(dbPath);
            var log = reopened.Load(k.Company.Id)!.VoucherEditLog;

            Assert.Equal(3, log.Count);
            Assert.Equal(
                new[] { VoucherEditVerb.Cancel, VoucherEditVerb.Alter, VoucherEditVerb.Delete },
                log.Select(e => e.Verb).ToArray());
            Assert.Equal(new[] { k.FirstId, k.SecondId, k.ThirdId }, log.Select(e => e.VoucherId).ToArray());

            // The instant survives WITH ITS OFFSET — a log read in another zone must still say when the edit
            // happened, not what o'clock it looked like.
            Assert.All(log, e => Assert.Equal(Instant, e.RecordedAt));
            Assert.All(log, e => Assert.Equal(TimeSpan.FromHours(5.5), e.RecordedAt.Offset));

            // …and the snapshots are byte-identical to what the engine wrote.
            Assert.Equal(
                k.Company.VoucherEditLog.Select(e => e.BeforeSnapshot).ToArray(),
                log.Select(e => e.BeforeSnapshot).ToArray());
        }
        finally { TempDbFile.Delete(dbPath); }
    }

    /// <summary>
    /// 🔴 <b>THE DELETE CASE — the reason <c>voucher_id</c> is not a foreign key.</b> After Alt+D the voucher is
    /// not in <c>vouchers</c> at all, so its log row names an id nothing resolves. That dangling id IS the
    /// record. A <c>REFERENCES vouchers(id)</c> would have made the single most important entry the one entry
    /// that could not be written.
    /// </summary>
    [Fact]
    [Trait("Category", "RoundTrip")]
    public void The_log_row_for_a_DELETED_voucher_survives_the_voucher_it_names()
    {
        var dbPath = TempDbFile.NewPath("apex-editlog-delete");
        try
        {
            var k = Seed();
            new LedgerService(k.Company, () => Instant).Delete(k.ThirdId);
            using (var store = new SqliteCompanyStore(dbPath)) store.Save(k.Company);

            Assert.Equal(0L, ReadScalar(dbPath, $"SELECT COUNT(*) FROM vouchers WHERE id = '{k.ThirdId:D}';"));
            Assert.Equal(1L, ReadScalar(dbPath, $"SELECT COUNT(*) FROM voucher_edit_log WHERE voucher_id = '{k.ThirdId:D}';"));

            using var reopened = new SqliteCompanyStore(dbPath);
            var loaded = reopened.Load(k.Company.Id)!;
            Assert.Null(loaded.FindVoucher(k.ThirdId));
            var entry = Assert.Single(loaded.VoucherEditLog);
            Assert.Equal(VoucherEditVerb.Delete, entry.Verb);
            Assert.Contains("third", entry.BeforeSnapshot, StringComparison.Ordinal);
        }
        finally { TempDbFile.Delete(dbPath); }
    }

    /// <summary>
    /// 🔴 <b>THE APPEND-ONLY GUARANTEE — the defect class this project has already been bitten by: "a full
    /// rewrite can delete a sole register and no invariant will see it".</b> <see cref="SqliteCompanyStore.Save"/>
    /// is delete-all + full re-insert, so any table it owns is exactly as complete as the aggregate handed to it.
    /// Here a SECOND company object with the same id and NO log is saved over the first — precisely the shape a
    /// stale instance, a screen that built its own company, or a future code path that forgets would produce. The
    /// rows must still be there. Delete the note in <c>DeleteCompanyRows</c> and add a
    /// <c>DELETE FROM voucher_edit_log</c> and this test is what goes red.
    /// </summary>
    [Fact]
    [Trait("Category", "RoundTrip")]
    public void A_save_from_a_company_that_never_loaded_the_log_cannot_erase_it()
    {
        var dbPath = TempDbFile.NewPath("apex-editlog-appendonly");
        try
        {
            var k = Seed();
            new LedgerService(k.Company, () => Instant).Cancel(k.FirstId);
            using (var store = new SqliteCompanyStore(dbPath)) store.Save(k.Company);
            Assert.Equal(1L, ReadScalar(dbPath, "SELECT COUNT(*) FROM voucher_edit_log;"));

            // Manufacture the amnesiac shape: the same company, still carrying its cancelled voucher, but with an
            // EMPTY in-memory log. (The discard API is being ABUSED here on purpose — it exists for entries whose
            // save did not commit, and these committed. It is simply the only public way to produce, from a test,
            // the state a stale Company instance arrives in: the book present, the log forgotten.)
            var forgetter = new LedgerService(k.Company);
            while (k.Company.LastVoucherEditLogEntry is { } stale)
                forgetter.DiscardUncommittedEditLogEntry(stale);
            Assert.Empty(k.Company.VoucherEditLog);
            Assert.True(k.Company.FindVoucher(k.FirstId)!.Cancelled);

            using (var store = new SqliteCompanyStore(dbPath)) store.Save(k.Company);

            Assert.Equal(1L, ReadScalar(dbPath, "SELECT COUNT(*) FROM voucher_edit_log;"));

            using var reopened = new SqliteCompanyStore(dbPath);
            Assert.Single(reopened.Load(k.Company.Id)!.VoucherEditLog);
        }
        finally { TempDbFile.Delete(dbPath); }
    }

    /// <summary>Saving the same company repeatedly must not duplicate its log — the writer is
    /// <c>INSERT OR IGNORE</c> on the entry's own primary key, so a re-save of a known entry is a no-op.</summary>
    [Fact]
    [Trait("Category", "RoundTrip")]
    public void Re_saving_the_same_company_does_not_duplicate_its_log()
    {
        var dbPath = TempDbFile.NewPath("apex-editlog-idempotent");
        try
        {
            var k = Seed();
            new LedgerService(k.Company, () => Instant).Cancel(k.FirstId);

            using (var store = new SqliteCompanyStore(dbPath))
            {
                store.Save(k.Company);
                store.Save(k.Company);
                store.Save(k.Company);
            }

            Assert.Equal(1L, ReadScalar(dbPath, "SELECT COUNT(*) FROM voucher_edit_log;"));
        }
        finally { TempDbFile.Delete(dbPath); }
    }

    // ================================================================= migration + downgrade

    /// <summary>
    /// A genuine v51 book — no <c>voucher_edit_log</c> table anywhere — migrates up, gains an EMPTY log, and
    /// every other row is exactly where it was. The emptiness is the point: it is the honest statement that this
    /// application recorded nothing about edits before v52, and there is nothing to reconstruct.
    /// </summary>
    [Fact]
    [Trait("Category", "RoundTrip")]
    public void An_upgraded_book_gains_an_EMPTY_log_and_nothing_else_moves()
    {
        var dbPath = TempDbFile.NewPath("apex-editlog-v51-migrated");
        try
        {
            var k = Seed();
            using (var store = new SqliteCompanyStore(dbPath)) store.Save(k.Company);

            var vouchersBefore = ReadScalar(dbPath, "SELECT COUNT(*) FROM vouchers;");
            var linesBefore = ReadScalar(dbPath, "SELECT COUNT(*) FROM entry_lines;");

            using (var conn = Open(dbPath))
            {
                SchemaDowngrade.V52ToV51(conn);
                SqliteConnection.ClearPool(conn);
            }
            Assert.Equal(51L, ReadScalar(dbPath, "SELECT version FROM schema_version LIMIT 1;"));
            Assert.False(TableExists(dbPath, "voucher_edit_log"));

            using (new SqliteCompanyStore(dbPath)) { }        // v51 → v52 runs

            Assert.Equal((long)Schema.CurrentVersion, ReadScalar(dbPath, "SELECT version FROM schema_version LIMIT 1;"));
            Assert.True(TableExists(dbPath, "voucher_edit_log"));
            Assert.Equal(0L, ReadScalar(dbPath, "SELECT COUNT(*) FROM voucher_edit_log;"));
            Assert.Equal(vouchersBefore, ReadScalar(dbPath, "SELECT COUNT(*) FROM vouchers;"));
            Assert.Equal(linesBefore, ReadScalar(dbPath, "SELECT COUNT(*) FROM entry_lines;"));

            using var reopened = new SqliteCompanyStore(dbPath);
            Assert.Empty(reopened.Load(k.Company.Id)!.VoucherEditLog);
        }
        finally { TempDbFile.Delete(dbPath); }
    }

    /// <summary>
    /// The downgrade is a TRUE inverse — the first one in <see cref="SchemaDowngrade"/> that is, and only because
    /// v52 adds nothing to an existing table. The table and its index go and NOTHING else changes; the object set
    /// of the downgraded file equals a real v51 file's, and re-migrating restores it.
    /// </summary>
    [Fact]
    [Trait("Category", "RoundTrip")]
    public void The_downgrade_removes_the_table_and_its_index_and_touches_nothing_else()
    {
        var dbPath = TempDbFile.NewPath("apex-editlog-downgrade");
        try
        {
            var k = Seed();
            new LedgerService(k.Company, () => Instant).Cancel(k.FirstId);
            using (var store = new SqliteCompanyStore(dbPath)) store.Save(k.Company);

            var atV52 = SchemaObjects(dbPath);

            using (var conn = Open(dbPath))
            {
                SchemaDowngrade.V52ToV51(conn);
                SqliteConnection.ClearPool(conn);
            }

            var atV51 = SchemaObjects(dbPath);
            Assert.Equal(
                new[] { "index:ix_voucher_edit_log_company", "table:voucher_edit_log" },
                atV52.Except(atV51, StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray());
            Assert.Empty(atV51.Except(atV52, StringComparer.Ordinal));

            // …and the recorded edit is genuinely gone, which is what a downgrade means and is the sharpest
            // statement of why this table had to exist: a v51 book cannot carry the evidence, because a v51 book
            // never could.
            using (new SqliteCompanyStore(dbPath)) { }
            Assert.Equal(atV52, SchemaObjects(dbPath));
            Assert.Equal(0L, ReadScalar(dbPath, "SELECT COUNT(*) FROM voucher_edit_log;"));
        }
        finally { TempDbFile.Delete(dbPath); }
    }

    // ================================================================= ER-13

    /// <summary>
    /// ER-13 at the storage layer: a book that never cancels, deletes or alters anything writes NO log row, and
    /// its every other table is what it always was.
    /// </summary>
    [Fact]
    [Trait("Category", "RoundTrip")]
    public void A_book_that_never_uses_a_verb_writes_no_log_row()
    {
        var dbPath = TempDbFile.NewPath("apex-editlog-er13");
        try
        {
            var k = Seed();
            using (var store = new SqliteCompanyStore(dbPath)) store.Save(k.Company);

            Assert.Equal(0L, ReadScalar(dbPath, "SELECT COUNT(*) FROM voucher_edit_log;"));

            using var reopened = new SqliteCompanyStore(dbPath);
            var loaded = reopened.Load(k.Company.Id)!;
            Assert.Empty(loaded.VoucherEditLog);
            Assert.Equal(3, loaded.Vouchers.Count);
        }
        finally { TempDbFile.Delete(dbPath); }
    }

    // ================================================================= fixture + helpers

    private sealed record Kit(Company Company, Apex.Ledger.Domain.Ledger Customer, Apex.Ledger.Domain.Ledger Sales, VoucherType SalesType,
        Guid FirstId, Guid SecondId, Guid ThirdId);

    private static Kit Seed()
    {
        var company = CompanyFactory.CreateSeeded("Edit Log Co", FyStart, FyStart);

        var sales = new Apex.Ledger.Domain.Ledger(Guid.NewGuid(), "Sales", company.FindGroupByName("Sales Accounts")!.Id,
            Money.Zero, openingIsDebit: false);
        company.AddLedger(sales);
        var customer = new Apex.Ledger.Domain.Ledger(Guid.NewGuid(), "A Customer", company.FindGroupByName("Sundry Debtors")!.Id,
            Money.Zero, openingIsDebit: true);
        company.AddLedger(customer);

        var type = company.FindVoucherTypeByName("Sales")!;
        var kit = new Kit(company, customer, sales, type, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());

        var service = new LedgerService(company);
        service.Post(SalesVoucher(kit, kit.FirstId, Money.FromRupees(1111.11m), "first"));
        service.Post(SalesVoucher(kit, kit.SecondId, Money.FromRupees(2222.22m), "second"));
        service.Post(SalesVoucher(kit, kit.ThirdId, Money.FromRupees(3333.33m), "third"));
        return kit;
    }

    private static Voucher SalesVoucher(Kit k, Guid id, Money amount, string narration) => new(
        id, k.SalesType.Id, FyStart.AddDays(5),
        new[]
        {
            new EntryLine(k.Customer.Id, amount, DrCr.Debit),
            new EntryLine(k.Sales.Id, amount, DrCr.Credit),
        },
        narration: narration, partyId: k.Customer.Id);

    private static long ReadScalar(string dbPath, string sql)
    {
        using var conn = Open(dbPath);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        var value = Convert.ToInt64(cmd.ExecuteScalar());
        SqliteConnection.ClearPool(conn);
        return value;
    }

    private static bool TableExists(string dbPath, string table) => ReadScalar(dbPath,
        $"SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='{table}';") > 0;

    private static IReadOnlyList<string> ColumnNames(string dbPath, string table)
    {
        using var conn = Open(dbPath);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"PRAGMA table_info(\"{table}\");";
        var names = new List<string>();
        using (var r = cmd.ExecuteReader())
            while (r.Read()) names.Add(r.GetString(1));
        SqliteConnection.ClearPool(conn);
        return names;
    }

    /// <summary>Every named table and index in the file, as "kind:name" — the instrument for "nothing else moved".</summary>
    private static IReadOnlyList<string> SchemaObjects(string dbPath)
    {
        using var conn = Open(dbPath);
        using var cmd = conn.CreateCommand();
        cmd.CommandText =
            "SELECT type, name FROM sqlite_master WHERE type IN ('table','index') AND sql IS NOT NULL ORDER BY type, name;";
        var objects = new List<string>();
        using (var r = cmd.ExecuteReader())
            while (r.Read()) objects.Add($"{r.GetString(0)}:{r.GetString(1)}");
        SqliteConnection.ClearPool(conn);
        return objects;
    }

    private static SqliteConnection Open(string dbPath)
    {
        var conn = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = dbPath,
            Mode = SqliteOpenMode.ReadWrite,
        }.ToString());
        conn.Open();
        return conn;
    }
}
