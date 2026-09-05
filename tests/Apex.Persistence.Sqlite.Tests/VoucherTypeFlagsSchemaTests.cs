using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Services;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Apex.Persistence.Sqlite.Tests;

/// <summary>
/// Schema v52 → v53 (W2-03; census 5.11) — the two ATTESTED <b>Voucher Type user flags</b>
/// (<c>print_after_saving</c>, <c>provide_narration_for_each_ledger</c>). Proves the four things this version
/// bump owes: both columns round-trip a real value; a genuine v52 database migrates up matching a fresh
/// <see cref="Schema.CreateV1"/> on those additions; the two new <see cref="NumberingMethod"/> ordinals survive a
/// round trip (they are stored as bare integers in the pre-existing <c>numbering</c> column, so nothing but the
/// ordinal contract protects them); and a downgrade drops the two columns while every voucher-type ROW survives.
///
/// <para>The "genuine v52 database" is manufactured with <see cref="SchemaDowngrade.V53ToV52"/> rather than
/// hand-written DDL, so the migration is exercised against real rows — the idiom every schema test here uses.</para>
///
/// <para><b>R7 — ATTESTED</b> (help.tallysolutions.com voucher-types page, fetched 2026-09-05): <i>"Enable Print
/// voucher after saving to automatically open the Voucher Printing screen"</i> and <i>"Provide narration for each
/// ledger in voucher"</i>. The column NAMES and the additive-only shape are ours.</para>
/// </summary>
public sealed class VoucherTypeFlagsSchemaTests
{
    private static readonly DateOnly FyStart = new(2025, 4, 1);

    // ================================================================= migration parity

    [Fact]
    [Trait("Category", "RoundTrip")]
    public void Migration_v52_to_v53_matches_CreateV1()
    {
        var migratedPath = TempDbFile.NewPath("apex-vtflags-v52-migrated");
        var freshPath = TempDbFile.NewPath("apex-vtflags-v53-fresh");
        try
        {
            var company = CompanyFactory.CreateSeeded("Fresh Flag Co", FyStart);
            using (var store = new SqliteCompanyStore(freshPath)) store.Save(company);

            var legacy = CompanyFactory.CreateSeeded("Legacy Flag Co", FyStart);
            using (var store = new SqliteCompanyStore(migratedPath)) store.Save(legacy);
            using (var conn = Open(migratedPath))
            {
                SchemaDowngrade.V53ToV52(conn);
                SqliteConnection.ClearPool(conn);
            }
            Assert.Equal(52L, ReadScalar(migratedPath, "SELECT version FROM schema_version LIMIT 1;"));

            var before = ColumnNames(migratedPath, "voucher_types");
            Assert.DoesNotContain("print_after_saving", before);
            Assert.DoesNotContain("provide_narration_for_each_ledger", before);

            // Reopen through the production store — the v52 → v53 migration runs.
            using (new SqliteCompanyStore(migratedPath)) { }
            Assert.Equal((long)Schema.CurrentVersion,
                ReadScalar(migratedPath, "SELECT version FROM schema_version LIMIT 1;"));

            // Only the columns this migration adds are compared: the downgrade's CREATE … AS SELECT rebuild erases
            // the PRE-EXISTING columns' declared type/notnull (an artifact of the downgrade helper, documented on
            // SchemaDowngrade.DropColumns and shared with V47ToV46). Whole-schema parity across the full chain is
            // separately guaranteed by SchemaMigrationEquivalenceTests.
            foreach (var col in Schema.V53VoucherTypeFlagColumns)
                Assert.Equal(ColumnContract(freshPath, "voucher_types", col),
                             ColumnContract(migratedPath, "voucher_types", col));
        }
        finally
        {
            TempDbFile.Delete(migratedPath);
            TempDbFile.Delete(freshPath);
        }
    }

    /// <summary>The migration back-fills nothing — every migrated type reads both flags OFF, which is exactly what
    /// a v52 type was (ER-13). A DEFAULT that said otherwise would silently switch on print-after-saving for every
    /// voucher type in every existing book.</summary>
    [Fact]
    [Trait("Category", "RoundTrip")]
    public void A_migrated_v52_book_reads_both_flags_off()
    {
        var path = TempDbFile.NewPath("apex-vtflags-backfill");
        try
        {
            var legacy = CompanyFactory.CreateSeeded("Backfill Co", FyStart);
            using (var store = new SqliteCompanyStore(path)) store.Save(legacy);
            using (var conn = Open(path)) { SchemaDowngrade.V53ToV52(conn); SqliteConnection.ClearPool(conn); }

            using var reopened = new SqliteCompanyStore(path);
            var loaded = reopened.Load(legacy.Id)!;

            Assert.NotEmpty(loaded.VoucherTypes);
            Assert.All(loaded.VoucherTypes, t =>
            {
                Assert.False(t.PrintAfterSaving);
                Assert.False(t.ProvideNarrationForEachLedger);
            });
        }
        finally { TempDbFile.Delete(path); }
    }

    // ================================================================= round-trip

    [Fact]
    [Trait("Category", "RoundTrip")]
    public void The_two_user_flags_roundTrip_sqlite()
    {
        var path = TempDbFile.NewPath("apex-vtflags-roundtrip");
        try
        {
            var company = CompanyFactory.CreateSeeded("Flag Co", FyStart);
            var created = new VoucherTypeService(company).Create(
                "Export Sales", VoucherBaseType.Sales, NumberingMethod.Manual,
                abbreviation: "ExpS", printAfterSaving: true, provideNarrationForEachLedger: true);

            using (var store = new SqliteCompanyStore(path)) store.Save(company);

            using var reopened = new SqliteCompanyStore(path);
            var loaded = reopened.Load(company.Id)!;
            var t = loaded.VoucherTypes.Single(x => x.Id == created.Id);

            Assert.True(t.PrintAfterSaving);
            Assert.True(t.ProvideNarrationForEachLedger);
            Assert.Equal(NumberingMethod.Manual, t.Numbering);
            Assert.Equal("ExpS", t.Abbreviation);
            Assert.False(t.IsPredefined);
            Assert.True(t.IsActive);

            // ER-13: an untouched seeded type carries both flags OFF.
            var plainSales = loaded.VoucherTypes.First(x => x.BaseType == VoucherBaseType.Sales && x.IsPredefined);
            Assert.False(plainSales.PrintAfterSaving);
            Assert.False(plainSales.ProvideNarrationForEachLedger);
        }
        finally { TempDbFile.Delete(path); }
    }

    /// <summary>
    /// 🔴 The two APPENDED numbering ordinals (3 = Automatic (Manual Override), 4 = Multi-user Auto) are stored as
    /// bare integers in the pre-existing <c>numbering</c> column, so nothing but the append-only contract keeps a
    /// saved book readable. Asserted against the raw stored INTEGER, not just the enum, so a future renumber fails
    /// here rather than in a customer's book.
    /// </summary>
    [Theory]
    [Trait("Category", "RoundTrip")]
    [InlineData(NumberingMethod.AutomaticManualOverride, 3L)]
    [InlineData(NumberingMethod.MultiUserAuto, 4L)]
    [InlineData(NumberingMethod.Manual, 1L)]
    [InlineData(NumberingMethod.None, 2L)]
    public void Each_numbering_method_roundTrips_on_its_persisted_ordinal(NumberingMethod method, long ordinal)
    {
        var path = TempDbFile.NewPath("apex-vtflags-numbering");
        try
        {
            var company = CompanyFactory.CreateSeeded("Numbering Co", FyStart);
            var created = new VoucherTypeService(company)
                .Create("Series B", VoucherBaseType.Journal, method);

            using (var store = new SqliteCompanyStore(path)) store.Save(company);

            Assert.Equal(ordinal, ReadScalar(path,
                $"SELECT numbering FROM voucher_types WHERE id = '{created.Id:D}';"));

            using var reopened = new SqliteCompanyStore(path);
            var loaded = reopened.Load(company.Id)!;
            Assert.Equal(method, loaded.VoucherTypes.Single(x => x.Id == created.Id).Numbering);
        }
        finally { TempDbFile.Delete(path); }
    }

    /// <summary>A second Save of a company carrying a user-created type must not duplicate or FK-break it — the
    /// store's delete-all + re-insert snapshot walks voucher_types like every other table.</summary>
    [Fact]
    [Trait("Category", "RoundTrip")]
    public void SecondSave_doesNotDuplicateTheUserType()
    {
        var path = TempDbFile.NewPath("apex-vtflags-secondsave");
        try
        {
            var company = CompanyFactory.CreateSeeded("Twice Co", FyStart);
            var created = new VoucherTypeService(company)
                .Create("Branch Sales", VoucherBaseType.Sales, NumberingMethod.Automatic,
                        printAfterSaving: true);

            using var store = new SqliteCompanyStore(path);
            store.Save(company);
            store.Save(company);

            Assert.Equal(1L, ReadScalar(path,
                $"SELECT COUNT(*) FROM voucher_types WHERE id = '{created.Id:D}';"));
            Assert.True(store.Load(company.Id)!.VoucherTypes.Single(x => x.Id == created.Id).PrintAfterSaving);
        }
        finally { TempDbFile.Delete(path); }
    }

    // ================================================================= downgrade

    [Fact]
    [Trait("Category", "RoundTrip")]
    public void Downgrade_v53_to_v52_dropsTheTwoColumns_preservesTypes()
    {
        var path = TempDbFile.NewPath("apex-vtflags-downgrade");
        try
        {
            var company = CompanyFactory.CreateSeeded("Downgrade Flag Co", FyStart);
            new VoucherTypeService(company)
                .Create("Retail Sales", VoucherBaseType.Sales, NumberingMethod.Automatic);
            var typeCountBefore = company.VoucherTypes.Count;

            using (var store = new SqliteCompanyStore(path)) store.Save(company);
            Assert.Equal((long)typeCountBefore, ReadScalar(path, "SELECT COUNT(*) FROM voucher_types;"));

            using (var conn = Open(path)) { SchemaDowngrade.V53ToV52(conn); SqliteConnection.ClearPool(conn); }

            Assert.Equal(52L, ReadScalar(path, "SELECT version FROM schema_version LIMIT 1;"));
            Assert.Equal((long)typeCountBefore, ReadScalar(path, "SELECT COUNT(*) FROM voucher_types;"));

            var cols = ColumnNames(path, "voucher_types");
            Assert.DoesNotContain("print_after_saving", cols);
            Assert.DoesNotContain("provide_narration_for_each_ledger", cols);
        }
        finally { TempDbFile.Delete(path); }
    }

    // ---- helpers (the shape every schema test file here open-codes) ----

    private static SqliteConnection Open(string path)
    {
        var conn = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path }.ToString());
        conn.Open();
        return conn;
    }

    private static long ReadScalar(string path, string sql)
    {
        using var conn = Open(path);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        var v = cmd.ExecuteScalar();
        SqliteConnection.ClearPool(conn);
        return Convert.ToInt64(v);
    }

    private static List<string> ColumnNames(string path, string table)
    {
        using var conn = Open(path);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"PRAGMA table_info({table});";
        var names = new List<string>();
        using (var r = cmd.ExecuteReader())
            while (r.Read()) names.Add(r.GetString(1));
        SqliteConnection.ClearPool(conn);
        return names;
    }

    /// <summary>(type, notnull, default, pk) for one column — the same contract tuple the other schema tests
    /// compare, so a migration that adds a column with a different declaration fails here.</summary>
    private static string ColumnContract(string path, string table, string column)
    {
        using var conn = Open(path);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"PRAGMA table_info({table});";
        var contract = "(absent)";
        using (var r = cmd.ExecuteReader())
            while (r.Read())
                if (string.Equals(r.GetString(1), column, StringComparison.OrdinalIgnoreCase))
                    contract = $"{r.GetString(2)}|{r.GetInt32(3)}|{(r.IsDBNull(4) ? "-" : r.GetString(4))}|{r.GetInt32(5)}";
        SqliteConnection.ClearPool(conn);
        return contract;
    }
}
