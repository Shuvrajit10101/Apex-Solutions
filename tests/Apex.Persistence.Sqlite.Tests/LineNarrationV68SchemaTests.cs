using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Services;
using Microsoft.Data.Sqlite;
using Xunit;
using Domain = Apex.Ledger.Domain;

namespace Apex.Persistence.Sqlite.Tests;

/// <summary>
/// Schema v67 → v68 (census row <b>5.11</b>, the voucher type's <i>Provide narration for each ledger in
/// voucher</i> flag): ONE nullable column, <c>entry_lines.narration</c>.
///
/// <para><b>Fidelity (R7, ruling 14).</b> help.tallysolutions.com/voucher-types-tally/, opened by content
/// 2026-10-25 (Configure Voucher Types → Set up transaction options): <i>"Enable Provide narration for each
/// ledger in voucher to add narration for individual ledgers"</i>.</para>
///
/// <para>🔴 <b>What this file proves beyond "the column exists":</b> (1) a REAL v67 book — one holding posted
/// vouchers whose lines carry bill-wise children — climbs the rung with every line, every amount and every child
/// row intact and every line narration NULL; (2) the migrated contract equals the fresh one; (3) the downgrade
/// keeps the PRIMARY KEY of an FK parent and leaves no child row dangling; (4) a narration written by the
/// production store is read back off a FRESH store handle — a column nothing writes or reads is dead schema,
/// which a parity test cannot see.</para>
/// </summary>
public sealed class LineNarrationV68SchemaTests
{
    private static readonly DateOnly FyStart = new(2024, 4, 1);

    private static (Company Company, Guid NarratedVoucherId) SeedWithVouchers(string name, bool narrate)
    {
        var c = CompanyFactory.CreateSeeded(name, FyStart);
        var cash = c.FindLedgerByName("Cash")!;
        var sales = new Domain.Ledger(Guid.NewGuid(), "Sales", c.FindGroupByName("Sales Accounts")!.Id,
            Money.Zero, openingIsDebit: false);
        c.AddLedger(sales);
        var debtor = new Domain.Ledger(Guid.NewGuid(), "Acme Ltd", c.FindGroupByName("Sundry Debtors")!.Id,
            Money.Zero, openingIsDebit: true, maintainBillByBill: true, defaultCreditPeriodDays: 30);
        c.AddLedger(debtor);

        var journal = c.FindVoucherTypeByName("Journal")!;
        var receipt = c.FindVoucherTypeByName("Receipt")!;
        var svc = new LedgerService(c);

        var narrated = svc.Post(new Voucher(Guid.NewGuid(), journal.Id, new DateOnly(2024, 4, 1), new[]
        {
            new EntryLine(debtor.Id, Money.FromRupees(8000m), DrCr.Debit, new[]
            {
                new BillAllocation(BillRefType.NewRef, "INV-A", Money.FromRupees(8000m)),
            }, narration: narrate ? "  Goods sold on credit, INV-A  " : null),
            new EntryLine(sales.Id, Money.FromRupees(8000m), DrCr.Credit,
                narration: narrate ? "Counter sales, April" : null),
        }, narration: "Voucher-level narration"));

        svc.Post(new Voucher(Guid.NewGuid(), receipt.Id, new DateOnly(2024, 4, 10), new[]
        {
            new EntryLine(cash.Id, Money.FromRupees(2000m), DrCr.Debit),
            new EntryLine(debtor.Id, Money.FromRupees(2000m), DrCr.Credit, new[]
            {
                new BillAllocation(BillRefType.AgstRef, "INV-A", Money.FromRupees(2000m)),
            }),
        }));

        return (c, narrated.Id);
    }

    /// <summary>
    /// 🔴 <b>(d) THE MIGRATION TEST ON A REAL v67 BOOK.</b> A book with posted vouchers and bill-wise children is
    /// walked down to v67 (proved: marker 67, column absent), then re-opened by the production store. It must climb
    /// to v68 with every voucher, every line amount, every bill allocation and the voucher-level narration intact,
    /// every line narration NULL (no back-fill), and no FK violation anywhere.
    ///
    /// <para><b>Mutation:</b> deleting the <c>if (version == 67)</c> rung in <c>SqliteCompanyStore</c> reddens this
    /// at the version assertion (the store throws "schema version 67 is not supported").</para>
    /// </summary>
    [Fact]
    [Trait("Category", "RoundTrip")]
    public void A_real_v67_book_climbs_to_v68_with_every_line_and_child_intact_and_no_line_narration()
    {
        var path = TempDbFile.NewPath("apex-v68-real");
        try
        {
            var (company, voucherId) = SeedWithVouchers("Legacy Narration Co", narrate: false);
            using (var store = new SqliteCompanyStore(path)) store.Save(company);
            var billRowsBefore = ReadScalar(path, "SELECT COUNT(*) FROM bill_allocations;");
            var linesBefore = ReadScalar(path, "SELECT COUNT(*) FROM entry_lines;");
            Assert.True(billRowsBefore >= 2);

            using (var conn = Open(path)) { SchemaDowngrade.V68ToV67(conn); SqliteConnection.ClearPool(conn); }
            Assert.Equal(67L, ReadScalar(path, "SELECT version FROM schema_version LIMIT 1;"));
            Assert.DoesNotContain("narration", ColumnNames(path, "entry_lines"));

            Company reloaded;
            using (var store = new SqliteCompanyStore(path)) reloaded = store.Load(company.Id)!;

            Assert.Equal(68L, ReadScalar(path, "SELECT version FROM schema_version LIMIT 1;"));
            Assert.Contains("narration", ColumnNames(path, "entry_lines"));
            Assert.Equal(linesBefore, ReadScalar(path, "SELECT COUNT(*) FROM entry_lines;"));
            Assert.Equal(billRowsBefore, ReadScalar(path, "SELECT COUNT(*) FROM bill_allocations;"));
            Assert.Equal(0L, ReadScalar(path, "SELECT COUNT(*) FROM entry_lines WHERE narration IS NOT NULL;"));
            Assert.Equal(0, ForeignKeyViolationCount(path));

            Assert.Equal(2, reloaded.Vouchers.Count);
            var v = reloaded.Vouchers.Single(x => x.Id == voucherId);
            Assert.Equal("Voucher-level narration", v.Narration);
            Assert.All(reloaded.Vouchers.SelectMany(x => x.Lines), l => Assert.Null(l.Narration));
            Assert.Equal(8000m, v.Lines[0].Amount.Amount);
            Assert.Equal("INV-A", Assert.Single(v.Lines[0].BillAllocations).Name);
        }
        finally { TempDbFile.Delete(path); }
    }

    /// <summary>The migrated <c>entry_lines</c> contract equals the fresh one, column for column.</summary>
    [Fact]
    [Trait("Category", "RoundTrip")]
    public void Migration_v67_to_v68_matches_CreateV1_on_entry_lines()
    {
        var migratedPath = TempDbFile.NewPath("apex-v68-migrated");
        var freshPath = TempDbFile.NewPath("apex-v68-fresh");
        try
        {
            using (var store = new SqliteCompanyStore(freshPath))
                store.Save(CompanyFactory.CreateSeeded("Fresh Narration Co", FyStart));
            using (var store = new SqliteCompanyStore(migratedPath))
                store.Save(CompanyFactory.CreateSeeded("Migrated Narration Co", FyStart));

            using (var conn = Open(migratedPath)) { SchemaDowngrade.V68ToV67(conn); SqliteConnection.ClearPool(conn); }
            Assert.DoesNotContain("narration", ColumnNames(migratedPath, "entry_lines"));

            using (new SqliteCompanyStore(migratedPath)) { }
            Assert.Equal(TableInfo(freshPath, "entry_lines"), TableInfo(migratedPath, "entry_lines"));
            Assert.Equal(Schema.V68EntryLineColumns, new[] { "narration" });
            Assert.DoesNotContain("UPDATE ", Schema.MigrateV67ToV68);
            Assert.Equal(68, Schema.CurrentVersion);
        }
        finally
        {
            TempDbFile.Delete(migratedPath);
            TempDbFile.Delete(freshPath);
        }
    }

    /// <summary>
    /// 🔴 <c>entry_lines</c> is an FK PARENT. The downgrade must keep its PRIMARY KEY and its index, and leave
    /// every bill-allocation child pointing at a live line; the file must then climb back up clean.
    /// </summary>
    [Fact]
    [Trait("Category", "RoundTrip")]
    public void The_v68_downgrade_preserves_the_primary_key_index_and_every_child_row()
    {
        var path = TempDbFile.NewPath("apex-v68-fk");
        try
        {
            var (company, _) = SeedWithVouchers("FK Narration Co", narrate: true);
            using (var store = new SqliteCompanyStore(path)) store.Save(company);
            var indexesBefore = IndexNames(path, "entry_lines").OrderBy(n => n).ToList();

            using (var conn = Open(path)) { SchemaDowngrade.V68ToV67(conn); SqliteConnection.ClearPool(conn); }

            Assert.Equal(new List<string> { "id" }, PrimaryKeyColumns(path, "entry_lines"));
            Assert.Equal(indexesBefore, IndexNames(path, "entry_lines").OrderBy(n => n).ToList());
            Assert.Equal(0, ForeignKeyViolationCount(path));
            Assert.Equal("ok", IntegrityCheck(path));

            Company reloaded;
            using (var store = new SqliteCompanyStore(path)) reloaded = store.Load(company.Id)!;
            Assert.Equal(68L, ReadScalar(path, "SELECT version FROM schema_version LIMIT 1;"));
            // What a downgrade means: the narration is gone, the figures are not.
            Assert.All(reloaded.Vouchers.SelectMany(x => x.Lines), l => Assert.Null(l.Narration));
            Assert.Equal(2, reloaded.Vouchers.Count);
            // The rebuilt table still allocates ids — a later save must succeed.
            using (var store = new SqliteCompanyStore(path)) store.Save(reloaded);
            Assert.Equal(0, ForeignKeyViolationCount(path));
        }
        finally { TempDbFile.Delete(path); }
    }

    /// <summary>
    /// 🔴 <b>(b)/(c) THE STORE'S WRITER AND READER ARE BOTH LIVE.</b> A per-line narration saved by the production
    /// store is read back off a FRESH store handle, on the right line, trimmed; a line nobody narrated stays NULL
    /// in the file (ER-13).
    ///
    /// <para><b>Mutations:</b> binding <c>$lnarr</c> to <c>DBNull.Value</c> in the writer reddens this at the
    /// raw-SQL assertion; dropping <c>narration</c> from the reader's constructor call reddens it at the
    /// reloaded-line assertion.</para>
    /// </summary>
    [Fact]
    [Trait("Category", "RoundTrip")]
    public void A_line_narration_is_read_back_off_a_fresh_store_handle_on_the_right_line()
    {
        var path = TempDbFile.NewPath("apex-v68-roundtrip");
        try
        {
            var (company, voucherId) = SeedWithVouchers("Round Trip Narration Co", narrate: true);
            using (var store = new SqliteCompanyStore(path)) store.Save(company);

            Assert.Equal(2L, ReadScalar(path, "SELECT COUNT(*) FROM entry_lines WHERE narration IS NOT NULL;"));
            Assert.Equal(1L, ReadScalar(path,
                "SELECT COUNT(*) FROM entry_lines WHERE narration = 'Goods sold on credit, INV-A';"));

            Company reloaded;
            using (var store = new SqliteCompanyStore(path)) reloaded = store.Load(company.Id)!;
            var v = reloaded.Vouchers.Single(x => x.Id == voucherId);
            Assert.Equal("Goods sold on credit, INV-A", v.Lines[0].Narration);
            Assert.Equal("Counter sales, April", v.Lines[1].Narration);
            Assert.Equal("Voucher-level narration", v.Narration);
            var other = reloaded.Vouchers.Single(x => x.Id != voucherId);
            Assert.All(other.Lines, l => Assert.Null(l.Narration));
        }
        finally { TempDbFile.Delete(path); }
    }

    // ================================================================= helpers

    private static SqliteConnection Open(string path)
    {
        var conn = new SqliteConnection(
            new SqliteConnectionStringBuilder { DataSource = path, Mode = SqliteOpenMode.ReadWrite }.ToString());
        conn.Open();
        return conn;
    }

    private static long ReadScalar(string path, string sql)
    {
        using var conn = Open(path);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        var value = Convert.ToInt64(cmd.ExecuteScalar());
        SqliteConnection.ClearPool(conn);
        return value;
    }

    private static List<string> ColumnNames(string path, string table)
    {
        using var conn = Open(path);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"PRAGMA table_info(\"{table}\");";
        var names = new List<string>();
        using (var r = cmd.ExecuteReader()) while (r.Read()) names.Add(r.GetString(1));
        SqliteConnection.ClearPool(conn);
        return names;
    }

    private static List<string> TableInfo(string path, string table)
    {
        using var conn = Open(path);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"PRAGMA table_info(\"{table}\");";
        var rows = new List<string>();
        using (var r = cmd.ExecuteReader())
            while (r.Read())
                rows.Add(string.Join('|',
                    r.GetString(1), r.GetString(2), r.GetInt32(3),
                    r.IsDBNull(4) ? "<null>" : r.GetString(4), r.GetInt32(5)));
        SqliteConnection.ClearPool(conn);
        return rows;
    }

    private static List<string> PrimaryKeyColumns(string path, string table)
    {
        using var conn = Open(path);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"PRAGMA table_info(\"{table}\");";
        var pk = new List<string>();
        using (var r = cmd.ExecuteReader())
            while (r.Read()) if (r.GetInt32(5) > 0) pk.Add(r.GetString(1));
        SqliteConnection.ClearPool(conn);
        return pk;
    }

    private static List<string> IndexNames(string path, string table)
    {
        using var conn = Open(path);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT name FROM sqlite_master WHERE type = 'index' AND tbl_name = $t AND sql IS NOT NULL;";
        cmd.Parameters.AddWithValue("$t", table);
        var names = new List<string>();
        using (var r = cmd.ExecuteReader()) while (r.Read()) names.Add(r.GetString(0));
        SqliteConnection.ClearPool(conn);
        return names;
    }

    private static int ForeignKeyViolationCount(string path)
    {
        using var conn = Open(path);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "PRAGMA foreign_key_check;";
        var n = 0;
        using (var r = cmd.ExecuteReader()) while (r.Read()) n++;
        SqliteConnection.ClearPool(conn);
        return n;
    }

    private static string IntegrityCheck(string path)
    {
        using var conn = Open(path);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "PRAGMA integrity_check;";
        var result = Convert.ToString(cmd.ExecuteScalar()) ?? string.Empty;
        SqliteConnection.ClearPool(conn);
        return result;
    }
}
