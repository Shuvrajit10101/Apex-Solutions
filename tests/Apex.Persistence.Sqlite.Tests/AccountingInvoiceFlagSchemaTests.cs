using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Services;
using Microsoft.Data.Sqlite;
using Xunit;
using Domain = Apex.Ledger.Domain;

namespace Apex.Persistence.Sqlite.Tests;

/// <summary>
/// Schema v48 → v49 — the <b>accounting-invoice flag</b>. Proves the four things this version bump owes: the new
/// <c>vouchers.is_accounting_invoice</c> column round-trips a real service invoice's flag (and does not smear it over
/// other vouchers); a genuine v48 database migrates up matching a fresh <see cref="Schema.CreateV1"/> on that addition;
/// every pre-existing v48 row migrates to 0 = "not an accounting invoice" (which is what makes existing data print
/// exactly as before, ER-13); and a downgrade drops the column while every voucher ROW survives.
///
/// <para>The "genuine v48 database" is manufactured with <see cref="SchemaDowngrade.V49ToV48"/> rather than
/// hand-written DDL, so the migration is exercised against real rows.</para>
/// </summary>
public sealed class AccountingInvoiceFlagSchemaTests
{
    private static readonly DateOnly FyStart = new(2025, 4, 1);

    // ================================================================= round-trip

    [Fact]
    [Trait("Category", "RoundTrip")]
    public void AccountingInvoiceFlag_roundTrips_sqlite()
    {
        var dbPath = TempDbFile.NewPath("apex-acctinv-roundtrip");
        try
        {
            var (company, serviceSaleId) = SeedServiceSaleAndPlainSale();

            using (var store = new SqliteCompanyStore(dbPath)) store.Save(company);

            // The column landed with the captured value on the service invoice, and 0 on the plain sale.
            Assert.Equal(1L, ReadScalar(dbPath,
                $"SELECT is_accounting_invoice FROM vouchers WHERE id = '{serviceSaleId:D}';"));
            Assert.Equal(0L, ReadScalar(dbPath,
                $"SELECT COUNT(*) FROM vouchers WHERE id <> '{serviceSaleId:D}' AND is_accounting_invoice <> 0;"));

            using var reopened = new SqliteCompanyStore(dbPath);
            var loaded = reopened.Load(company.Id)!;

            Assert.True(loaded.Vouchers.Single(v => v.Id == serviceSaleId).IsAccountingInvoice);
            // ER-13: a voucher that is NOT an accounting invoice reads back false (no value conjured).
            Assert.All(loaded.Vouchers.Where(v => v.Id != serviceSaleId), v => Assert.False(v.IsAccountingInvoice));
        }
        finally { TempDbFile.Delete(dbPath); }
    }

    // ================================================================= migration parity

    [Fact]
    [Trait("Category", "RoundTrip")]
    public void Migration_v48_to_v49_matches_CreateV1()
    {
        var migratedPath = TempDbFile.NewPath("apex-acctinv-v48-migrated");
        var freshPath = TempDbFile.NewPath("apex-acctinv-v49-fresh");
        try
        {
            // A fresh v49 database (stamped straight to CurrentVersion by CreateV1).
            var fresh = CompanyFactory.CreateSeeded("Fresh Acct Co", FyStart);
            using (var store = new SqliteCompanyStore(freshPath)) store.Save(fresh);

            // Manufacture a genuine v48 database: save at v49, then downgrade (drop the flag column).
            var legacy = CompanyFactory.CreateSeeded("Legacy Acct Co", FyStart);
            using (var store = new SqliteCompanyStore(migratedPath)) store.Save(legacy);
            using (var conn = Open(migratedPath))
            {
                SchemaDowngrade.V50ToV49(conn);   // v50 negative-stock warn flag
                SchemaDowngrade.V49ToV48(conn);
                SqliteConnection.ClearPool(conn);
            }
            Assert.Equal(48L, ReadScalar(migratedPath, "SELECT version FROM schema_version LIMIT 1;"));
            Assert.DoesNotContain("is_accounting_invoice", ColumnNames(migratedPath, "vouchers"));

            // Reopen through the production store — the v48 → v49 migration runs.
            using (new SqliteCompanyStore(migratedPath)) { }
            Assert.Equal((long)Schema.CurrentVersion, ReadScalar(migratedPath, "SELECT version FROM schema_version LIMIT 1;"));

            // The migration's own addition matches the fresh CreateV1 schema exactly — same declared type, NOT NULL
            // and DEFAULT literal. (The downgrade rebuilds vouchers via CREATE … AS SELECT, which erases the
            // PRE-EXISTING columns' declared type/notnull — an artifact of the downgrade helper, not the migration —
            // so we compare only what the v48→v49 migration itself adds. Whole-schema parity across the full chain is
            // separately guaranteed by SchemaMigrationEquivalenceTests.)
            foreach (var col in Schema.V49AccountingInvoiceColumns)
                Assert.Equal(ColumnContract(freshPath, "vouchers", col), ColumnContract(migratedPath, "vouchers", col));
        }
        finally
        {
            TempDbFile.Delete(migratedPath);
            TempDbFile.Delete(freshPath);
        }
    }

    // ================================================================= existing data migrates to "not an invoice"

    /// <summary>
    /// The ER-13 promise of this bump: EVERY row that existed at v48 — including a real service-shaped sale posted
    /// before the flag existed — comes up at v49 reading <c>false</c>, so it keeps printing exactly as it did. A
    /// migration that back-filled 1, or left the column readable as NULL, would silently relabel documents the user
    /// has already issued.
    /// </summary>
    [Fact]
    [Trait("Category", "RoundTrip")]
    public void ExistingV48Rows_migrateTo_notAnAccountingInvoice()
    {
        var dbPath = TempDbFile.NewPath("apex-acctinv-legacy-rows");
        try
        {
            var (company, serviceSaleId) = SeedServiceSaleAndPlainSale();
            using (var store = new SqliteCompanyStore(dbPath)) store.Save(company);

            // Drop back to a genuine v48 database — the flag column (and every captured flag) is gone, exactly the
            // state a user's pre-v49 database is in.
            using (var conn = Open(dbPath))
            {
                SchemaDowngrade.V50ToV49(conn);   // v50 negative-stock warn flag
                SchemaDowngrade.V49ToV48(conn);
                SqliteConnection.ClearPool(conn);
            }
            var rowsBefore = ReadScalar(dbPath, "SELECT COUNT(*) FROM vouchers;");
            Assert.True(rowsBefore > 0);

            // Migrate up through the production store and read the company back.
            using var reopened = new SqliteCompanyStore(dbPath);
            Assert.Equal((long)Schema.CurrentVersion, ReadScalar(dbPath, "SELECT version FROM schema_version LIMIT 1;"));
            Assert.Equal(rowsBefore, ReadScalar(dbPath, "SELECT COUNT(*) FROM vouchers;"));

            var loaded = reopened.Load(company.Id)!;
            Assert.NotEmpty(loaded.Vouchers);
            Assert.All(loaded.Vouchers, v => Assert.False(v.IsAccountingInvoice));
            Assert.Contains(loaded.Vouchers, v => v.Id == serviceSaleId);   // the row itself survived
        }
        finally { TempDbFile.Delete(dbPath); }
    }

    // ================================================================= downgrade

    [Fact]
    [Trait("Category", "RoundTrip")]
    public void Downgrade_v49_to_v48_dropsColumn_preservesVouchers()
    {
        var dbPath = TempDbFile.NewPath("apex-acctinv-downgrade");
        try
        {
            var (company, _) = SeedServiceSaleAndPlainSale();
            var voucherCountBefore = company.Vouchers.Count;
            Assert.True(voucherCountBefore > 0);

            using (var store = new SqliteCompanyStore(dbPath)) store.Save(company);
            Assert.Equal((long)voucherCountBefore, ReadScalar(dbPath, "SELECT COUNT(*) FROM vouchers;"));

            using (var conn = Open(dbPath))
            {
                SchemaDowngrade.V50ToV49(conn);   // v50 negative-stock warn flag
                SchemaDowngrade.V49ToV48(conn);
                SqliteConnection.ClearPool(conn);
            }

            Assert.Equal(48L, ReadScalar(dbPath, "SELECT version FROM schema_version LIMIT 1;"));
            Assert.Equal((long)voucherCountBefore, ReadScalar(dbPath, "SELECT COUNT(*) FROM vouchers;"));
            Assert.DoesNotContain("is_accounting_invoice", ColumnNames(dbPath, "vouchers"));
            // The v48 columns below it are untouched by this downgrade.
            Assert.Contains("reference_no", ColumnNames(dbPath, "vouchers"));
        }
        finally { TempDbFile.Delete(dbPath); }
    }

    // ---- helpers ----

    /// <summary>A seeded company with one accounting-invoice (service) Sales voucher and one ordinary Sales voucher.
    /// Returns the company and the service voucher's id.</summary>
    private static (Company Company, Guid ServiceSaleId) SeedServiceSaleAndPlainSale()
    {
        var c = CompanyFactory.CreateSeeded("Acct Inv Co", FyStart);
        var sales = new Domain.Ledger(Guid.NewGuid(), "Consultancy Income", c.FindGroupByName("Sales Accounts")!.Id,
            Money.Zero, openingIsDebit: false);
        c.AddLedger(sales);
        var debtor = new Domain.Ledger(Guid.NewGuid(), "Acme Ltd", c.FindGroupByName("Sundry Debtors")!.Id,
            Money.Zero, openingIsDebit: true);
        c.AddLedger(debtor);

        var salesType = c.FindVoucherTypeByName("Sales")!;
        var svc = new LedgerService(c);

        var service = svc.Post(new Voucher(Guid.NewGuid(), salesType.Id, new DateOnly(2025, 4, 5),
            new[]
            {
                new EntryLine(debtor.Id, Money.FromRupees(1000m), DrCr.Debit),
                new EntryLine(sales.Id, Money.FromRupees(1000m), DrCr.Credit),
            }, partyId: debtor.Id, isAccountingInvoice: true));

        svc.Post(new Voucher(Guid.NewGuid(), salesType.Id, new DateOnly(2025, 4, 6),
            new[]
            {
                new EntryLine(debtor.Id, Money.FromRupees(500m), DrCr.Debit),
                new EntryLine(sales.Id, Money.FromRupees(500m), DrCr.Credit),
            }, partyId: debtor.Id));

        return (c, service.Id);
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
