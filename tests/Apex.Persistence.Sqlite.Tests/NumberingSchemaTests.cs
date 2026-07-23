using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Services;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Apex.Persistence.Sqlite.Tests;

/// <summary>
/// Schema v46 → v47 (voucher-numbering S3; numbering-design-v2 §6) — the per-type <b>numbering config</b>. Proves
/// the four things this version bump owes: the three new <c>voucher_types</c> columns
/// (<c>prevent_duplicate</c> / <c>number_width</c> / <c>prefill_with_zero</c>) plus the two date-keyed affix child
/// tables (<c>voucher_type_prefix</c> / <c>voucher_type_suffix</c>) round-trip a real config; a genuine v46 database
/// migrates up matching a fresh <see cref="Schema.CreateV1"/> on those additions; a second Save of a
/// numbering-configured company does not FK-break on the surviving affix child rows (the r2-F3 delete-clear); and a
/// downgrade drops the two child tables + three columns while every voucher-type ROW survives.
///
/// <para>The "genuine v46 database" is manufactured with <see cref="SchemaDowngrade.V47ToV46"/> rather than
/// hand-written DDL, so the migration is exercised against real rows.</para>
/// </summary>
public sealed class NumberingSchemaTests
{
    private static readonly DateOnly FyStart = new(2025, 4, 1);

    // ================================================================= migration parity

    [Fact]
    [Trait("Category", "RoundTrip")]
    public void Migration_v46_to_v47_matches_CreateV1()
    {
        var migratedPath = TempDbFile.NewPath("apex-num-v46-migrated");
        var freshPath = TempDbFile.NewPath("apex-num-v47-fresh");
        try
        {
            // A fresh v47 database (stamped straight to CurrentVersion by CreateV1).
            var company = CompanyFactory.CreateSeeded("Fresh Num Co", FyStart);
            using (var store = new SqliteCompanyStore(freshPath)) store.Save(company);

            // Manufacture a genuine v46 database: save at v47, then downgrade (drop the 3 columns + 2 child tables).
            var legacy = CompanyFactory.CreateSeeded("Legacy Num Co", FyStart);
            using (var store = new SqliteCompanyStore(migratedPath)) store.Save(legacy);
            using (var conn = Open(migratedPath))
            {
                SchemaDowngrade.V47ToV46(conn);
                SqliteConnection.ClearPool(conn);
            }
            Assert.Equal(46L, ReadScalar(migratedPath, "SELECT version FROM schema_version LIMIT 1;"));

            // Reopen through the production store — the v46 → v47 migration runs.
            using (new SqliteCompanyStore(migratedPath)) { }
            Assert.Equal((long)Schema.CurrentVersion, ReadScalar(migratedPath, "SELECT version FROM schema_version LIMIT 1;"));

            // The migration's own additions match the fresh CreateV1 schema exactly. (The downgrade rebuilds
            // voucher_types via CREATE … AS SELECT, which erases the PRE-EXISTING columns' declared type/notnull —
            // an artifact of the downgrade helper, not the migration, and the same one V45ToV44 has — so we compare
            // only what the v46→v47 migration itself adds: the three ALTER-added columns keep their INTEGER NOT NULL
            // DEFAULT 0 contract, and the two child tables are freshly CREATEd.) Whole-schema parity across the full
            // chain is separately guaranteed by SchemaMigrationEquivalenceTests.
            foreach (var col in Schema.V47NumberingColumns)
                Assert.Equal(ColumnContract(freshPath, "voucher_types", col), ColumnContract(migratedPath, "voucher_types", col));
            Assert.True(HasTable(migratedPath, "voucher_type_prefix"), "voucher_type_prefix missing after migration");
            Assert.True(HasTable(migratedPath, "voucher_type_suffix"), "voucher_type_suffix missing after migration");
            Assert.Equal(ColumnContracts(freshPath, "voucher_type_prefix"), ColumnContracts(migratedPath, "voucher_type_prefix"));
            Assert.Equal(ColumnContracts(freshPath, "voucher_type_suffix"), ColumnContracts(migratedPath, "voucher_type_suffix"));
            Assert.True(HasIndex(migratedPath, "ix_vt_prefix_type"), "ix_vt_prefix_type missing after migration");
            Assert.True(HasIndex(migratedPath, "ix_vt_suffix_type"), "ix_vt_suffix_type missing after migration");
        }
        finally
        {
            TempDbFile.Delete(migratedPath);
            TempDbFile.Delete(freshPath);
        }
    }

    // ================================================================= round-trip

    [Fact]
    [Trait("Category", "RoundTrip")]
    public void NumberingRules_roundTrip_sqlite()
    {
        var dbPath = TempDbFile.NewPath("apex-num-roundtrip");
        try
        {
            var company = CompanyFactory.CreateSeeded("Config Co", FyStart);
            var typeId = AddConfiguredType(company);

            using (var store = new SqliteCompanyStore(dbPath)) store.Save(company);

            // The child rows landed: 2 prefix rows + 1 suffix row for this type.
            Assert.Equal(2L, ReadScalar(dbPath,
                $"SELECT COUNT(*) FROM voucher_type_prefix WHERE voucher_type_id = '{typeId:D}';"));
            Assert.Equal(1L, ReadScalar(dbPath,
                $"SELECT COUNT(*) FROM voucher_type_suffix WHERE voucher_type_id = '{typeId:D}';"));

            using var reopened = new SqliteCompanyStore(dbPath);
            var loaded = reopened.Load(company.Id)!;
            var t = loaded.VoucherTypes.Single(x => x.Id == typeId);

            Assert.True(t.PreventDuplicate);
            Assert.Equal(4, t.NumberWidth);
            Assert.True(t.PrefillWithZero);

            // Prefixes come back ordered by applicable_from; particulars + dates exact.
            Assert.Equal(2, t.Prefixes.Count);
            Assert.Equal(new DateOnly(2025, 4, 1), t.Prefixes[0].ApplicableFrom);
            Assert.Equal("25-26/", t.Prefixes[0].Particulars);
            Assert.Equal(new DateOnly(2026, 4, 1), t.Prefixes[1].ApplicableFrom);
            Assert.Equal("26-27/", t.Prefixes[1].Particulars);

            var suffix = Assert.Single(t.Suffixes);
            Assert.Equal(new DateOnly(2025, 4, 1), suffix.ApplicableFrom);
            Assert.Equal("/A", suffix.Particulars);

            // ER-13: an unconfigured seeded type carries no affix rows and the default scalars.
            var plainSales = loaded.VoucherTypes.First(x => x.BaseType == VoucherBaseType.Sales && x.IsPredefined);
            Assert.False(plainSales.PreventDuplicate);
            Assert.Equal(0, plainSales.NumberWidth);
            Assert.False(plainSales.PrefillWithZero);
            Assert.Empty(plainSales.Prefixes);
            Assert.Empty(plainSales.Suffixes);
        }
        finally { TempDbFile.Delete(dbPath); }
    }

    // ================================================================= second-save FK integrity

    [Fact]
    [Trait("Category", "RoundTrip")]
    public void SecondSave_doesNotFkBreak()
    {
        var dbPath = TempDbFile.NewPath("apex-num-secondsave");
        try
        {
            var company = CompanyFactory.CreateSeeded("Twice-Saved Co", FyStart);
            var typeId = AddConfiguredType(company);

            using var store = new SqliteCompanyStore(dbPath);
            store.Save(company);
            store.Save(company); // second Save deletes-then-reinserts — must clear the affix child rows first

            // The rows are correct after the second save (no duplication, no orphan, no FK failure).
            Assert.Equal(2L, ReadScalar(dbPath,
                $"SELECT COUNT(*) FROM voucher_type_prefix WHERE voucher_type_id = '{typeId:D}';"));
            Assert.Equal(1L, ReadScalar(dbPath,
                $"SELECT COUNT(*) FROM voucher_type_suffix WHERE voucher_type_id = '{typeId:D}';"));

            var loaded = store.Load(company.Id)!;
            var t = loaded.VoucherTypes.Single(x => x.Id == typeId);
            Assert.Equal(2, t.Prefixes.Count);
            Assert.Single(t.Suffixes);
        }
        finally { TempDbFile.Delete(dbPath); }
    }

    // ================================================================= downgrade

    [Fact]
    [Trait("Category", "RoundTrip")]
    public void Downgrade_v47_to_v46_dropsChildTablesAndColumns_preservesTypes()
    {
        var dbPath = TempDbFile.NewPath("apex-num-downgrade");
        try
        {
            var company = CompanyFactory.CreateSeeded("Downgrade Co", FyStart);
            AddConfiguredType(company);
            var typeCountBefore = company.VoucherTypes.Count; // 24 seeds + 1 configured = 25

            using (var store = new SqliteCompanyStore(dbPath)) store.Save(company);
            Assert.Equal((long)typeCountBefore, ReadScalar(dbPath, "SELECT COUNT(*) FROM voucher_types;"));

            using (var conn = Open(dbPath))
            {
                SchemaDowngrade.V47ToV46(conn);
                SqliteConnection.ClearPool(conn);
            }

            Assert.Equal(46L, ReadScalar(dbPath, "SELECT version FROM schema_version LIMIT 1;"));

            // Every voucher-type ROW survived the rebuild.
            Assert.Equal((long)typeCountBefore, ReadScalar(dbPath, "SELECT COUNT(*) FROM voucher_types;"));

            // The three numbering columns are gone.
            var cols = ColumnNames(dbPath, "voucher_types");
            Assert.DoesNotContain("prevent_duplicate", cols);
            Assert.DoesNotContain("number_width", cols);
            Assert.DoesNotContain("prefill_with_zero", cols);

            // Both affix child tables are gone.
            Assert.False(HasTable(dbPath, "voucher_type_prefix"), "voucher_type_prefix should be dropped");
            Assert.False(HasTable(dbPath, "voucher_type_suffix"), "voucher_type_suffix should be dropped");
        }
        finally { TempDbFile.Delete(dbPath); }
    }

    // ---- helpers ----

    /// <summary>Adds a fully-configured voucher type — 2 date-keyed prefix rows, 1 suffix row, width 4, prefill on,
    /// prevent-duplicate on — and returns its id.</summary>
    private static Guid AddConfiguredType(Company company)
    {
        var id = Guid.NewGuid();
        var t = new VoucherType(id, "Configured Sales", VoucherBaseType.Sales,
            numbering: NumberingMethod.Automatic,
            preventDuplicate: true, numberWidth: 4, prefillWithZero: true,
            prefixes: new[]
            {
                new VoucherNumberAffix(Guid.NewGuid(), new DateOnly(2025, 4, 1), "25-26/"),
                new VoucherNumberAffix(Guid.NewGuid(), new DateOnly(2026, 4, 1), "26-27/"),
            },
            suffixes: new[]
            {
                new VoucherNumberAffix(Guid.NewGuid(), new DateOnly(2025, 4, 1), "/A"),
            });
        company.AddVoucherType(t);
        return id;
    }

    private static long ReadScalar(string dbPath, string sql)
    {
        using var conn = Open(dbPath);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        var v = Convert.ToInt64(cmd.ExecuteScalar());
        SqliteConnection.ClearPool(conn);
        return v;
    }

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

    /// <summary>The per-column contract (name/type/notnull/default/pk) sorted order-independently, matching the
    /// SchemaMigrationEquivalence comparison shape.</summary>
    private static string ColumnContracts(string dbPath, string table)
    {
        using var conn = Open(dbPath);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"PRAGMA table_info(\"{table}\");";
        var cols = new List<string>();
        using (var r = cmd.ExecuteReader())
            while (r.Read())
            {
                var name = r.GetString(1);
                var type = r.GetString(2);
                var notNull = r.GetInt64(3);
                var dflt = r.IsDBNull(4) ? "<null>" : r.GetString(4);
                var pk = r.GetInt64(5);
                cols.Add($"{name} | {type} | notnull={notNull} | default={dflt} | pk={pk}");
            }
        SqliteConnection.ClearPool(conn);
        cols.Sort(StringComparer.Ordinal);
        return string.Join("\n", cols);
    }

    /// <summary>The single-column contract "name|type|notnull|default|pk" (or "&lt;absent&gt;" when the column does
    /// not exist).</summary>
    private static string ColumnContract(string dbPath, string table, string column)
    {
        using var conn = Open(dbPath);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"PRAGMA table_info(\"{table}\");";
        var result = "<absent>";
        using (var r = cmd.ExecuteReader())
            while (r.Read())
            {
                if (!string.Equals(r.GetString(1), column, StringComparison.OrdinalIgnoreCase)) continue;
                var type = r.GetString(2);
                var notNull = r.GetInt64(3);
                var dflt = r.IsDBNull(4) ? "<null>" : r.GetString(4);
                var pk = r.GetInt64(5);
                result = $"{column} | {type} | notnull={notNull} | default={dflt} | pk={pk}";
            }
        SqliteConnection.ClearPool(conn);
        return result;
    }

    private static bool HasTable(string dbPath, string name) => HasObject(dbPath, "table", name);
    private static bool HasIndex(string dbPath, string name) => HasObject(dbPath, "index", name);

    private static bool HasObject(string dbPath, string type, string name)
    {
        using var conn = Open(dbPath);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = $t AND name = $n;";
        cmd.Parameters.AddWithValue("$t", type);
        cmd.Parameters.AddWithValue("$n", name);
        var count = Convert.ToInt64(cmd.ExecuteScalar());
        SqliteConnection.ClearPool(conn);
        return count > 0;
    }

    private static SqliteConnection Open(string dbPath)
    {
        var conn = new SqliteConnection(
            new SqliteConnectionStringBuilder { DataSource = dbPath, Mode = SqliteOpenMode.ReadWrite }.ToString());
        conn.Open();
        return conn;
    }
}
