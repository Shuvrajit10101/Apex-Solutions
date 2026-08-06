using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Services;
using Microsoft.Data.Sqlite;
using Xunit;
using Domain = Apex.Ledger.Domain;

namespace Apex.Persistence.Sqlite.Tests;

/// <summary>
/// Schema v47 → v48 (voucher-numbering S5; numbering-design-v2 §8) — the <b>counterparty captured field</b>. Proves
/// the three things this version bump owes: the two new nullable <c>vouchers</c> columns
/// (<c>reference_no</c> / <c>reference_date</c>) round-trip a real Sales voucher's captured reference; a genuine v47
/// database migrates up matching a fresh <see cref="Schema.CreateV1"/> on those additions; and a downgrade drops the
/// two columns while every voucher ROW survives.
///
/// <para>The "genuine v47 database" is manufactured with <see cref="SchemaDowngrade.V48ToV47"/> rather than
/// hand-written DDL, so the migration is exercised against real rows.</para>
/// </summary>
public sealed class ReferenceFieldSchemaTests
{
    private static readonly DateOnly FyStart = new(2025, 4, 1);

    // ================================================================= round-trip

    [Fact]
    [Trait("Category", "RoundTrip")]
    public void ReferenceNo_roundTrips_sqlite()
    {
        var dbPath = TempDbFile.NewPath("apex-ref-roundtrip");
        try
        {
            var (company, salesTypeId, saleId) = SeedSaleWithReference(
                referenceNo: "ABC/123", referenceDate: new DateOnly(2025, 4, 5));

            using (var store = new SqliteCompanyStore(dbPath)) store.Save(company);

            // The columns landed with the captured values.
            Assert.Equal("ABC/123", ReadString(dbPath,
                $"SELECT reference_no FROM vouchers WHERE id = '{saleId:D}';"));
            Assert.Equal("2025-04-05", ReadString(dbPath,
                $"SELECT reference_date FROM vouchers WHERE id = '{saleId:D}';"));

            using var reopened = new SqliteCompanyStore(dbPath);
            var loaded = reopened.Load(company.Id)!;
            var sale = loaded.Vouchers.Single(v => v.Id == saleId);

            Assert.Equal("ABC/123", sale.ReferenceNo);
            Assert.Equal(new DateOnly(2025, 4, 5), sale.ReferenceDate);

            // ER-13: a voucher WITHOUT a reference reads back null/null (no value conjured).
            var plain = loaded.Vouchers.Single(v => v.Id != saleId);
            Assert.Null(plain.ReferenceNo);
            Assert.Null(plain.ReferenceDate);
        }
        finally { TempDbFile.Delete(dbPath); }
    }

    // ================================================================= migration parity

    [Fact]
    [Trait("Category", "RoundTrip")]
    public void Migration_v47_to_v48_matches_CreateV1()
    {
        var migratedPath = TempDbFile.NewPath("apex-ref-v47-migrated");
        var freshPath = TempDbFile.NewPath("apex-ref-v48-fresh");
        try
        {
            // A fresh v48 database (stamped straight to CurrentVersion by CreateV1).
            var fresh = CompanyFactory.CreateSeeded("Fresh Ref Co", FyStart);
            using (var store = new SqliteCompanyStore(freshPath)) store.Save(fresh);

            // Manufacture a genuine v47 database: save at the current version, then step down one version at a time
            // (v49→v48 drops the accounting-invoice flag, v48→v47 drops the 2 reference columns).
            var legacy = CompanyFactory.CreateSeeded("Legacy Ref Co", FyStart);
            using (var store = new SqliteCompanyStore(migratedPath)) store.Save(legacy);
            using (var conn = Open(migratedPath))
            {
                SchemaDowngrade.V50ToV49(conn);   // v50 negative-stock warn flag
                SchemaDowngrade.V49ToV48(conn);
                SchemaDowngrade.V48ToV47(conn);
                SqliteConnection.ClearPool(conn);
            }
            Assert.Equal(47L, ReadScalar(migratedPath, "SELECT version FROM schema_version LIMIT 1;"));
            Assert.DoesNotContain("reference_no", ColumnNames(migratedPath, "vouchers"));
            Assert.DoesNotContain("reference_date", ColumnNames(migratedPath, "vouchers"));

            // Reopen through the production store — the v47 → v48 migration runs.
            using (new SqliteCompanyStore(migratedPath)) { }
            Assert.Equal((long)Schema.CurrentVersion, ReadScalar(migratedPath, "SELECT version FROM schema_version LIMIT 1;"));

            // The migration's own additions match the fresh CreateV1 schema exactly. (The downgrade rebuilds
            // vouchers via CREATE … AS SELECT, which erases the PRE-EXISTING columns' declared type/notnull — an
            // artifact of the downgrade helper, not the migration, and the same one V45ToV44 has — so we compare
            // only what the v47→v48 migration itself adds: the two ALTER-added columns keep their TEXT NULL contract.
            // Whole-schema parity across the full chain is separately guaranteed by SchemaMigrationEquivalenceTests.)
            foreach (var col in Schema.V48ReferenceColumns)
                Assert.Equal(ColumnContract(freshPath, "vouchers", col), ColumnContract(migratedPath, "vouchers", col));
        }
        finally
        {
            TempDbFile.Delete(migratedPath);
            TempDbFile.Delete(freshPath);
        }
    }

    // ================================================================= downgrade

    [Fact]
    [Trait("Category", "RoundTrip")]
    public void Downgrade_v48_to_v47_dropsColumns_preservesVouchers()
    {
        var dbPath = TempDbFile.NewPath("apex-ref-downgrade");
        try
        {
            var (company, _, _) = SeedSaleWithReference("REF/9", new DateOnly(2025, 4, 9));
            var voucherCountBefore = company.Vouchers.Count;
            Assert.True(voucherCountBefore > 0);

            using (var store = new SqliteCompanyStore(dbPath)) store.Save(company);
            Assert.Equal((long)voucherCountBefore, ReadScalar(dbPath, "SELECT COUNT(*) FROM vouchers;"));

            using (var conn = Open(dbPath))
            {
                // Step down one version at a time from the current version to reach v47.
                SchemaDowngrade.V50ToV49(conn);   // v50 negative-stock warn flag
                SchemaDowngrade.V49ToV48(conn);
                SchemaDowngrade.V48ToV47(conn);
                SqliteConnection.ClearPool(conn);
            }

            Assert.Equal(47L, ReadScalar(dbPath, "SELECT version FROM schema_version LIMIT 1;"));

            // Every voucher ROW survived the rebuild.
            Assert.Equal((long)voucherCountBefore, ReadScalar(dbPath, "SELECT COUNT(*) FROM vouchers;"));

            // The two reference columns are gone.
            var cols = ColumnNames(dbPath, "vouchers");
            Assert.DoesNotContain("reference_no", cols);
            Assert.DoesNotContain("reference_date", cols);
        }
        finally { TempDbFile.Delete(dbPath); }
    }

    // ---- helpers ----

    /// <summary>A seeded company with one Sales voucher that carries the given counterparty reference, plus a second
    /// (plain, no-reference) Sales voucher. Returns the company, the Sales type id, and the referenced voucher id.</summary>
    private static (Company Company, Guid SalesTypeId, Guid SaleId) SeedSaleWithReference(
        string referenceNo, DateOnly referenceDate)
    {
        var c = CompanyFactory.CreateSeeded("Ref Co", FyStart);
        var sales = new Domain.Ledger(Guid.NewGuid(), "Sales", c.FindGroupByName("Sales Accounts")!.Id,
            Money.Zero, openingIsDebit: false);
        c.AddLedger(sales);
        var debtor = new Domain.Ledger(Guid.NewGuid(), "Acme Ltd", c.FindGroupByName("Sundry Debtors")!.Id,
            Money.Zero, openingIsDebit: true);
        c.AddLedger(debtor);

        var salesType = c.FindVoucherTypeByName("Sales")!;
        var svc = new LedgerService(c);

        var withRef = svc.Post(new Voucher(Guid.NewGuid(), salesType.Id, new DateOnly(2025, 4, 5),
            new[]
            {
                new EntryLine(debtor.Id, Money.FromRupees(1000m), DrCr.Debit),
                new EntryLine(sales.Id, Money.FromRupees(1000m), DrCr.Credit),
            }, partyId: debtor.Id, referenceNo: referenceNo, referenceDate: referenceDate));

        svc.Post(new Voucher(Guid.NewGuid(), salesType.Id, new DateOnly(2025, 4, 6),
            new[]
            {
                new EntryLine(debtor.Id, Money.FromRupees(500m), DrCr.Debit),
                new EntryLine(sales.Id, Money.FromRupees(500m), DrCr.Credit),
            }, partyId: debtor.Id));

        return (c, salesType.Id, withRef.Id);
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

    private static string? ReadString(string dbPath, string sql)
    {
        using var conn = Open(dbPath);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        var v = cmd.ExecuteScalar();
        SqliteConnection.ClearPool(conn);
        return v is null or DBNull ? null : Convert.ToString(v);
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

    /// <summary>The single-column contract "name|type|notnull|default|pk" (or "&lt;absent&gt;" when the column does
    /// not exist), matching the SchemaMigrationEquivalence comparison shape.</summary>
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

    private static SqliteConnection Open(string dbPath)
    {
        var conn = new SqliteConnection(
            new SqliteConnectionStringBuilder { DataSource = dbPath, Mode = SqliteOpenMode.ReadWrite }.ToString());
        conn.Open();
        return conn;
    }
}
