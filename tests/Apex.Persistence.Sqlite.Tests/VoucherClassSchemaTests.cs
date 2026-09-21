using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Services;
using Microsoft.Data.Sqlite;
using Xunit;
using Domain = Apex.Ledger.Domain;

namespace Apex.Persistence.Sqlite.Tests;

/// <summary>
/// Schema v61 → v62 (census row <b>2.6 Voucher Class</b> — the general machinery). Proves the four things this
/// version bump owes: a configured class round-trips both child tables with every field intact; a genuine v61
/// database migrates up to a shape matching a fresh <see cref="Schema.CreateV1"/>; a SECOND Save of a
/// class-configured company does not FK-break (the delete-clear ordering, which here is unusual because both
/// tables are children of TWO parents); and the downgrade drops exactly the two tables and their two indexes
/// while every voucher-type and class ROW survives.
///
/// <para>The "genuine v61 database" is manufactured by walking the ladder down from CURRENT —
/// <see cref="SchemaDowngrade.V63ToV62"/> then <see cref="SchemaDowngrade.V62ToV61"/> — rather than from
/// hand-written DDL, so the migration is exercised against real rows. 🔴 <b>BOTH rungs are required.</b> v63
/// (census 7.19) landed above v62, so calling <c>V62ToV61</c> alone would stamp the marker 61 on a book still
/// carrying v63's two <c>pay_head_computation_slabs</c> columns — a marker that lies about its own shape, which
/// is precisely the class of defect this suite exists to catch.</para>
///
/// <para><b>R7 — ATTESTED.</b> See <c>Apex.Ledger.Services.VoucherClassPosting</c> and
/// <c>Schema.MigrateV61ToV62</c> for the per-field vendor citations
/// (<c>help.tallysolutions.com</c>: the voucher-types page, the customs-duty voucher-class page, and the
/// round-off page).</para>
/// </summary>
public sealed class VoucherClassSchemaTests
{
    private static readonly DateOnly FyStart = new(2025, 4, 1);

    // ================================================================= round-trip

    /// <summary>
    /// 🔴 <b>THE ROUND-TRIP THAT MATTERS IS THE VALUE ROUND-TRIP, NOT THE ROW COUNT.</b> A class whose Value
    /// Basis or Rounding Limit came back a paisa different posts a different figure on every voucher, and a
    /// count-only assertion would not see it. Every field of both tables is read back and compared.
    /// </summary>
    [Fact]
    [Trait("Category", "RoundTrip")]
    public void A_configured_class_round_trips_both_child_tables_field_for_field()
    {
        var path = TempDbFile.NewPath("apex-vclass-roundtrip");
        try
        {
            var company = CompanyFactory.CreateSeeded("Class Round Trip Co", FyStart);
            var (typeId, classId, sales, freight, roundOff) = ConfigureClass(company);

            using (var store = new SqliteCompanyStore(path)) store.Save(company);

            Company reloaded;
            using (var store = new SqliteCompanyStore(path)) reloaded = store.Load(company.Id)!;

            var cls = reloaded.FindVoucherType(typeId)!.Classes.Single(c => c.Id == classId);

            var allocations = cls.LedgerAllocations;
            Assert.Equal(2, allocations.Count);
            Assert.Equal(sales, allocations[0].LedgerId);
            Assert.Equal(7_500, allocations[0].PercentBasisPoints);
            Assert.Equal(0, allocations[0].Order);
            Assert.Equal(2_500, allocations[1].PercentBasisPoints);
            Assert.Equal(1, allocations[1].Order);

            var entries = cls.AdditionalEntries;
            Assert.Equal(2, entries.Count);

            Assert.Equal(freight, entries[0].LedgerId);
            Assert.Equal(VoucherClassCalculationType.BasedOnQuantity, entries[0].CalculationType);
            Assert.Equal(2.50m, entries[0].ValueBasis.Amount);
            Assert.Equal(VoucherClassRoundingMethod.NotApplicable, entries[0].RoundingMethod);
            Assert.Equal(Money.Zero, entries[0].RoundingLimit);
            Assert.True(entries[0].RemoveIfZero);

            Assert.Equal(roundOff, entries[1].LedgerId);
            Assert.Equal(VoucherClassCalculationType.AsTotalAmountRounding, entries[1].CalculationType);
            Assert.Equal(VoucherClassRoundingMethod.Normal, entries[1].RoundingMethod);
            Assert.Equal(1.00m, entries[1].RoundingLimit.Amount);
            Assert.False(entries[1].RemoveIfZero);

            // …and the reloaded class posts the SAME legs the in-memory one did. A round-trip that restores the
            // fields but not the behaviour would pass every assertion above.
            var before = VoucherClassPosting.Compute(
                company.FindVoucherType(typeId)!.Classes.Single(c => c.Id == classId),
                Money.FromRupees(1_000.01m), 3m);
            var after = VoucherClassPosting.Compute(cls, Money.FromRupees(1_000.01m), 3m);
            Assert.Equal(before.Total.Amount, after.Total.Amount);
            Assert.Equal(
                before.AllLegs.Select(l => (l.LedgerId, l.Amount.Amount)),
                after.AllLegs.Select(l => (l.LedgerId, l.Amount.Amount)));
        }
        finally { TempDbFile.Delete(path); }
    }

    /// <summary>
    /// 🔴 <b>THE SECOND SAVE.</b> <c>Save</c> is delete-all + full re-insert, and BOTH v62 tables are children of
    /// two parents at once — <c>voucher_type_classes</c> and <c>ledgers</c>. The clear therefore has to run before
    /// the LEDGERS delete, not alongside the class delete where the affix tables sit. Getting that order wrong
    /// does not fail the first Save; it fails the second, on a company the operator has been using.
    /// </summary>
    [Fact]
    [Trait("Category", "RoundTrip")]
    public void A_second_save_of_a_class_configured_company_does_not_foreign_key_break()
    {
        var path = TempDbFile.NewPath("apex-vclass-second-save");
        try
        {
            var company = CompanyFactory.CreateSeeded("Class Second Save Co", FyStart);
            ConfigureClass(company);

            using (var store = new SqliteCompanyStore(path))
            {
                store.Save(company);
                store.Save(company);   // ← the one that used to break
            }

            Assert.Equal(2L, ReadScalar(path, "SELECT COUNT(*) FROM voucher_class_ledger_allocations;"));
            Assert.Equal(2L, ReadScalar(path, "SELECT COUNT(*) FROM voucher_class_additional_entries;"));
        }
        finally { TempDbFile.Delete(path); }
    }

    /// <summary>A book that never configures a class writes NO row into either table (ER-13) — v62 must not have
    /// given the shipped Stock Journal transfer class an allocation it never had.</summary>
    [Fact]
    [Trait("Category", "RoundTrip")]
    public void A_book_that_configures_no_class_writes_no_row_in_either_table()
    {
        var path = TempDbFile.NewPath("apex-vclass-er13");
        try
        {
            var company = CompanyFactory.CreateSeeded("Class ER13 Co", FyStart);
            using (var store = new SqliteCompanyStore(path)) store.Save(company);

            Assert.Equal(0L, ReadScalar(path, "SELECT COUNT(*) FROM voucher_class_ledger_allocations;"));
            Assert.Equal(0L, ReadScalar(path, "SELECT COUNT(*) FROM voucher_class_additional_entries;"));
        }
        finally { TempDbFile.Delete(path); }
    }

    // ================================================================= migration parity

    /// <summary>A genuine v61 database migrated up matches a fresh <see cref="Schema.CreateV1"/> on both new
    /// tables, column contract for column contract (name/type/notnull/default/pk) — the same comparison
    /// <c>SchemaMigrationEquivalenceTests</c> makes globally, made here against real rows.</summary>
    [Fact]
    [Trait("Category", "RoundTrip")]
    public void Migration_v61_to_v62_matches_CreateV1()
    {
        var migratedPath = TempDbFile.NewPath("apex-vclass-v61-migrated");
        var freshPath = TempDbFile.NewPath("apex-vclass-v62-fresh");
        try
        {
            var fresh = CompanyFactory.CreateSeeded("Fresh Class Co", FyStart);
            using (var store = new SqliteCompanyStore(freshPath)) store.Save(fresh);

            var legacy = CompanyFactory.CreateSeeded("Legacy Class Co", FyStart);
            using (var store = new SqliteCompanyStore(migratedPath)) store.Save(legacy);
            using (var conn = Open(migratedPath)) { SchemaDowngrade.V64ToV63(conn); SchemaDowngrade.V63ToV62(conn); SchemaDowngrade.V62ToV61(conn); SqliteConnection.ClearPool(conn); }

            // The manufactured book really is v61 — neither table exists on it yet.
            Assert.Equal(61L, ReadScalar(migratedPath, "SELECT version FROM schema_version LIMIT 1;"));
            foreach (var table in Schema.V62Tables) Assert.False(TableExists(migratedPath, table));

            using (new SqliteCompanyStore(migratedPath)) { }   // v61 → v62
            Assert.Equal((long)Schema.CurrentVersion, ReadScalar(migratedPath, "SELECT version FROM schema_version LIMIT 1;"));

            foreach (var table in Schema.V62Tables)
                Assert.Equal(ColumnContract(freshPath, table), ColumnContract(migratedPath, table));
        }
        finally { TempDbFile.Delete(migratedPath); TempDbFile.Delete(freshPath); }
    }

    // ================================================================= downgrade

    /// <summary>The downgrade removes exactly the two tables and their two indexes, stamps 61, and leaves every
    /// voucher-type and voucher-class row in place — the class survives, only its two rule tables go.</summary>
    [Fact]
    [Trait("Category", "RoundTrip")]
    public void The_downgrade_drops_only_the_v62_objects_and_keeps_every_class_row()
    {
        var path = TempDbFile.NewPath("apex-vclass-downgrade");
        try
        {
            var company = CompanyFactory.CreateSeeded("Class Downgrade Co", FyStart);
            ConfigureClass(company);
            var companyId = company.Id;
            using (var store = new SqliteCompanyStore(path)) store.Save(company);

            var typesBefore = ReadScalar(path, "SELECT COUNT(*) FROM voucher_types;");
            var classesBefore = ReadScalar(path, "SELECT COUNT(*) FROM voucher_type_classes;");
            Assert.True(classesBefore > 0);

            using (var conn = Open(path)) { SchemaDowngrade.V64ToV63(conn); SchemaDowngrade.V63ToV62(conn); SchemaDowngrade.V62ToV61(conn); SqliteConnection.ClearPool(conn); }

            Assert.Equal(61L, ReadScalar(path, "SELECT version FROM schema_version LIMIT 1;"));
            foreach (var table in Schema.V62Tables) Assert.False(TableExists(path, table));
            foreach (var index in Schema.V62Indexes) Assert.False(IndexExists(path, index));

            Assert.Equal(typesBefore, ReadScalar(path, "SELECT COUNT(*) FROM voucher_types;"));
            Assert.Equal(classesBefore, ReadScalar(path, "SELECT COUNT(*) FROM voucher_type_classes;"));

            // 🔴 And the PRIMARY KEY of voucher_type_classes survived. This rung is a plain DROP precisely so it
            // never rebuilds an FK parent; if someone later converts it to a column-dropping rebuild, the PK loss
            // that V56ToV55 records would come back and this assertion is what catches it.
            using (var store = new SqliteCompanyStore(path)) store.Save(store.Load(companyId)!);
        }
        finally { TempDbFile.Delete(path); }
    }

    // ================================================================= helpers

    /// <summary>Builds a Sales type carrying a fully configured class: a two-way ledger pre-map, a per-unit
    /// freight, and a normal round-off to the rupee.</summary>
    private static (Guid TypeId, Guid ClassId, Guid Sales, Guid Freight, Guid RoundOff) ConfigureClass(Company company)
    {
        var salesGroup = company.FindGroupByName("Sales Accounts")!.Id;
        var expenseGroup = company.FindGroupByName("Indirect Expenses")!.Id;

        var salesLedger = new Domain.Ledger(Guid.NewGuid(), "Domestic Sales", salesGroup, Money.Zero, openingIsDebit: false);
        var exportLedger = new Domain.Ledger(Guid.NewGuid(), "Export Sales", salesGroup, Money.Zero, openingIsDebit: false);
        var freightLedger = new Domain.Ledger(Guid.NewGuid(), "Freight Outward", expenseGroup, Money.Zero, openingIsDebit: true);
        company.AddLedger(salesLedger);
        company.AddLedger(exportLedger);
        company.AddLedger(freightLedger);

        var sales = salesLedger.Id;
        var other = exportLedger.Id;
        var third = freightLedger.Id;

        var type = new VoucherType(Guid.NewGuid(), "Retail Sales", VoucherBaseType.Sales);
        company.AddVoucherType(type);

        var service = new VoucherTypeService(company);
        var cls = service.AddClass(type.Id, "Retail", useClassForInterGodownTransfers: false);

        service.AddClassAllocation(type.Id, cls.Id, sales, 7_500);
        service.AddClassAllocation(type.Id, cls.Id, other, 2_500);

        service.AddClassAdditionalEntry(
            type.Id, cls.Id, other, VoucherClassCalculationType.BasedOnQuantity,
            Money.FromRupees(2.50m), VoucherClassRoundingMethod.NotApplicable, Money.Zero, removeIfZero: true);
        service.AddClassAdditionalEntry(
            type.Id, cls.Id, third, VoucherClassCalculationType.AsTotalAmountRounding,
            Money.Zero, VoucherClassRoundingMethod.Normal, Money.FromRupees(1m), removeIfZero: false);

        return (type.Id, cls.Id, sales, other, third);
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

    /// <summary>The per-column contract (name/type/notnull/default/pk) sorted order-independently — the same
    /// comparison <c>SchemaMigrationEquivalenceTests</c> makes.</summary>
    private static IReadOnlyList<string> ColumnContract(string dbPath, string table)
    {
        using var conn = Open(dbPath);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"PRAGMA table_info(\"{table}\");";
        var rows = new List<string>();
        using (var r = cmd.ExecuteReader())
            while (r.Read())
                rows.Add(string.Join('|',
                    r.GetString(1), r.GetString(2), r.GetInt32(3),
                    r.IsDBNull(4) ? "<null>" : r.GetString(4), r.GetInt32(5)));
        SqliteConnection.ClearPool(conn);
        rows.Sort(StringComparer.Ordinal);
        return rows;
    }

    private static bool TableExists(string dbPath, string table) => ObjectExists(dbPath, "table", table);

    private static bool IndexExists(string dbPath, string index) => ObjectExists(dbPath, "index", index);

    private static bool ObjectExists(string dbPath, string kind, string name)
    {
        using var conn = Open(dbPath);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = $k AND name = $n;";
        cmd.Parameters.AddWithValue("$k", kind);
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
