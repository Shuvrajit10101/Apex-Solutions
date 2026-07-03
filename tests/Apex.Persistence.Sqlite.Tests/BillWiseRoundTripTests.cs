using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Reports;
using Apex.Ledger.Services;
using Apex.Persistence.Sqlite;
using Microsoft.Data.Sqlite;
using Xunit;
using Domain = Apex.Ledger.Domain;

namespace Apex.Persistence.Sqlite.Tests;

/// <summary>
/// Bill-wise persistence contract (task §5): a company with bill-by-bill ledgers and posted
/// New/Agst/split allocations SAVES and RELOADS with schema_version = 2, preserving every bill
/// allocation and the bill-wise ledger fields; and a legacy v1 database still opens (auto-migrated
/// to v2) with its data intact.
/// </summary>
public sealed class BillWiseRoundTripTests
{
    private static (Company Company, Domain.Ledger Debtor, Domain.Ledger Creditor) SeedWithBills()
    {
        var c = CompanyFactory.CreateSeeded("Bill Persist Co", new DateOnly(2024, 4, 1));

        var cash = c.FindLedgerByName("Cash")!;
        cash.OpeningBalance = Money.FromRupees(100000m);
        cash.OpeningIsDebit = true;

        var sales = new Domain.Ledger(Guid.NewGuid(), "Sales", c.FindGroupByName("Sales Accounts")!.Id,
            Money.Zero, openingIsDebit: false);
        c.AddLedger(sales);
        var purchases = new Domain.Ledger(Guid.NewGuid(), "Purchases", c.FindGroupByName("Purchase Accounts")!.Id,
            Money.Zero, openingIsDebit: true);
        c.AddLedger(purchases);

        var debtor = new Domain.Ledger(Guid.NewGuid(), "Acme Ltd", c.FindGroupByName("Sundry Debtors")!.Id,
            Money.Zero, openingIsDebit: true, maintainBillByBill: true, defaultCreditPeriodDays: 30);
        c.AddLedger(debtor);
        var creditor = new Domain.Ledger(Guid.NewGuid(), "Supplier Co", c.FindGroupByName("Sundry Creditors")!.Id,
            Money.Zero, openingIsDebit: false, maintainBillByBill: true, defaultCreditPeriodDays: 45);
        c.AddLedger(creditor);

        var journal = c.FindVoucherTypeByName("Journal")!;
        var receipt = c.FindVoucherTypeByName("Receipt")!;
        var svc = new LedgerService(c);

        // Split invoice 8000 → INV-A 5000 + INV-B 3000 (explicit due on INV-A).
        svc.Post(new Voucher(Guid.NewGuid(), journal.Id, new DateOnly(2024, 4, 1), new[]
        {
            new EntryLine(debtor.Id, Money.FromRupees(8000m), DrCr.Debit, new[]
            {
                new BillAllocation(BillRefType.NewRef, "INV-A", Money.FromRupees(5000m), dueDate: new DateOnly(2024, 4, 30)),
                new BillAllocation(BillRefType.NewRef, "INV-B", Money.FromRupees(3000m)),
            }),
            new EntryLine(sales.Id, Money.FromRupees(8000m), DrCr.Credit),
        }));

        // Partial receipt 2000 against INV-A.
        svc.Post(new Voucher(Guid.NewGuid(), receipt.Id, new DateOnly(2024, 4, 10), new[]
        {
            new EntryLine(cash.Id, Money.FromRupees(2000m), DrCr.Debit),
            new EntryLine(debtor.Id, Money.FromRupees(2000m), DrCr.Credit, new[]
            {
                new BillAllocation(BillRefType.AgstRef, "INV-A", Money.FromRupees(2000m)),
            }),
        }));

        // A payable bill.
        svc.Post(new Voucher(Guid.NewGuid(), journal.Id, new DateOnly(2024, 4, 5), new[]
        {
            new EntryLine(purchases.Id, Money.FromRupees(4000m), DrCr.Debit),
            new EntryLine(creditor.Id, Money.FromRupees(4000m), DrCr.Credit, new[]
            {
                new BillAllocation(BillRefType.NewRef, "P-1", Money.FromRupees(4000m)),
            }),
        }));

        return (c, debtor, creditor);
    }

    [Fact]
    [Trait("Category", "RoundTrip")]
    public void Bills_survive_save_reload_with_schema_version_2()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"apex-bills-{Guid.NewGuid():N}.db");
        try
        {
            var (original, debtor, creditor) = SeedWithBills();
            var asOf = new DateOnly(2024, 4, 30);

            var before = Outstandings.Build(original, asOf);

            using (var write = new SqliteCompanyStore(dbPath))
            {
                write.Save(original);
                // The adapter reports v2.
                Assert.Equal(2, Schema.CurrentVersion);
            }

            // The persisted marker is 2.
            Assert.Equal(2L, ReadSchemaVersion(dbPath));

            Company reloaded;
            using (var read = new SqliteCompanyStore(dbPath))
                reloaded = read.Load(original.Id)!;

            // Bill-wise ledger fields survived.
            var rDebtor = reloaded.FindLedgerByName("Acme Ltd")!;
            Assert.True(rDebtor.MaintainBillByBill);
            Assert.Equal(30, rDebtor.DefaultCreditPeriodDays);
            var rCreditor = reloaded.FindLedgerByName("Supplier Co")!;
            Assert.True(rCreditor.MaintainBillByBill);
            Assert.Equal(45, rCreditor.DefaultCreditPeriodDays);

            // Allocations survived: find the split invoice line and check its two allocations.
            var splitLine = reloaded.Vouchers
                .SelectMany(v => v.Lines)
                .Single(l => l.LedgerId == rDebtor.Id && l.BillAllocations.Count == 2);
            Assert.Equal(Money.FromRupees(8000m), splitLine.BillAllocationTotal);
            var invA = splitLine.BillAllocations.Single(a => a.Name == "INV-A");
            Assert.Equal(BillRefType.NewRef, invA.RefType);
            Assert.Equal(new DateOnly(2024, 4, 30), invA.DueDate);
            Assert.Equal(Money.FromRupees(5000m), invA.Amount);

            // The Outstandings projection reproduces on the reloaded company.
            var after = Outstandings.Build(reloaded, asOf);
            Assert.Equal(before.TotalReceivable, after.TotalReceivable);
            Assert.Equal(before.TotalPayable, after.TotalPayable);
            // INV-A pending 3000 + INV-B 3000 = 6000 receivable; P-1 4000 payable.
            Assert.Equal(Money.FromRupees(6000m), after.TotalReceivable);
            Assert.Equal(Money.FromRupees(4000m), after.TotalPayable);

            // Σ open receivable bills == debtor closing balance still holds after reload.
            var rDebtorBills = Outstandings.OpenBillsFor(reloaded, rDebtor, asOf);
            var sumPending = rDebtorBills.Aggregate(0m, (s, b) => s + b.Pending.Amount);
            Assert.Equal(LedgerBalances.Closing(reloaded, rDebtor, asOf).Amount.Amount, sumPending);
        }
        finally
        {
            if (File.Exists(dbPath)) File.Delete(dbPath);
        }
    }

    [Fact]
    [Trait("Category", "RoundTrip")]
    public void Legacy_v1_database_opens_and_auto_migrates_to_v2()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"apex-v1legacy-{Guid.NewGuid():N}.db");
        try
        {
            // Build a minimal LEGACY v1 database by hand (the pre-bill-wise shape): a schema_version
            // marker of 1 plus a companies table with one row. Opening it with the store must migrate.
            var companyId = Guid.NewGuid();
            var connStr = new SqliteConnectionStringBuilder
            {
                DataSource = dbPath,
                Mode = SqliteOpenMode.ReadWriteCreate,
            }.ToString();

            using (var conn = new SqliteConnection(connStr))
            {
                conn.Open();
                Exec(conn, LegacyV1Ddl);
                Exec(conn, "INSERT INTO schema_version(version) VALUES (1);");
                Exec(conn,
                    """
                    INSERT INTO companies
                      (id, name, mailing_name, address, country, state, pin,
                       financial_year_start, books_begin_from, base_currency_symbol,
                       base_currency_name, decimal_places, decimal_unit_name,
                       primary_cost_category, main_location, profit_and_loss_head_id)
                    VALUES
                      ($id, 'Legacy Co', 'Legacy Co', NULL, 'India', NULL, NULL,
                       '2024-04-01', '2024-04-01', '₹', 'INR', 2, 'Paisa',
                       'Primary Cost Category', 'Main Location', NULL);
                    """,
                    ("$id", companyId.ToString("D")));
                SqliteConnection.ClearPool(conn);
            }

            Assert.Equal(1L, ReadSchemaVersion(dbPath));

            // Open with the current adapter → auto-migrate to v2.
            using (var store = new SqliteCompanyStore(dbPath))
            {
                var loaded = store.Load(companyId);
                Assert.NotNull(loaded);
                Assert.Equal("Legacy Co", loaded!.Name);
            }

            // The migration bumped the marker and created the new structures.
            Assert.Equal(2L, ReadSchemaVersion(dbPath));
            Assert.True(TableExists(dbPath, "bill_allocations"), "bill_allocations table was not created.");
            Assert.True(ColumnExists(dbPath, "ledgers", "maintain_bill_by_bill"));
            Assert.True(ColumnExists(dbPath, "ledgers", "default_credit_period"));
        }
        finally
        {
            if (File.Exists(dbPath)) File.Delete(dbPath);
        }
    }

    // ---- helpers ----

    private static long ReadSchemaVersion(string dbPath)
    {
        using var conn = Open(dbPath);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT version FROM schema_version LIMIT 1;";
        var v = Convert.ToInt64(cmd.ExecuteScalar());
        SqliteConnection.ClearPool(conn);
        return v;
    }

    private static bool TableExists(string dbPath, string table)
    {
        using var conn = Open(dbPath);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name=$n;";
        cmd.Parameters.AddWithValue("$n", table);
        var exists = Convert.ToInt64(cmd.ExecuteScalar()) > 0;
        SqliteConnection.ClearPool(conn);
        return exists;
    }

    private static bool ColumnExists(string dbPath, string table, string column)
    {
        using var conn = Open(dbPath);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT COUNT(*) FROM pragma_table_info('{table}') WHERE name=$c;";
        cmd.Parameters.AddWithValue("$c", column);
        var exists = Convert.ToInt64(cmd.ExecuteScalar()) > 0;
        SqliteConnection.ClearPool(conn);
        return exists;
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

    private static void Exec(SqliteConnection conn, string sql, params (string Name, object Value)[] ps)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (n, v) in ps) cmd.Parameters.AddWithValue(n, v);
        cmd.ExecuteNonQuery();
    }

    /// <summary>The pre-bill-wise v1 DDL (only the tables the migration/load touches), kept in the test
    /// so it does not drift when the production <see cref="Schema"/> advances past v1.</summary>
    private const string LegacyV1Ddl = """
        CREATE TABLE schema_version (version INTEGER NOT NULL);
        CREATE TABLE companies (
            id TEXT NOT NULL PRIMARY KEY, name TEXT NOT NULL, mailing_name TEXT NOT NULL,
            address TEXT NULL, country TEXT NOT NULL, state TEXT NULL, pin TEXT NULL,
            financial_year_start TEXT NOT NULL, books_begin_from TEXT NOT NULL,
            base_currency_symbol TEXT NOT NULL, base_currency_name TEXT NOT NULL,
            decimal_places INTEGER NOT NULL, decimal_unit_name TEXT NOT NULL,
            primary_cost_category TEXT NOT NULL, main_location TEXT NOT NULL,
            profit_and_loss_head_id TEXT NULL REFERENCES groups(id));
        CREATE TABLE groups (
            id TEXT NOT NULL PRIMARY KEY, company_id TEXT NOT NULL REFERENCES companies(id),
            name TEXT NOT NULL, nature INTEGER NOT NULL, parent_id TEXT NULL REFERENCES groups(id),
            alias TEXT NULL, is_predefined INTEGER NOT NULL, is_pl_head INTEGER NOT NULL DEFAULT 0);
        CREATE TABLE ledgers (
            id TEXT NOT NULL PRIMARY KEY, company_id TEXT NOT NULL REFERENCES companies(id),
            name TEXT NOT NULL, group_id TEXT NOT NULL REFERENCES groups(id),
            opening_balance_paisa INTEGER NOT NULL, opening_is_debit INTEGER NOT NULL,
            alias TEXT NULL, is_predefined INTEGER NOT NULL);
        CREATE TABLE voucher_types (
            id TEXT NOT NULL PRIMARY KEY, company_id TEXT NOT NULL REFERENCES companies(id),
            name TEXT NOT NULL, base_type INTEGER NOT NULL, default_shortcut TEXT NULL,
            numbering INTEGER NOT NULL, abbreviation TEXT NULL, is_active INTEGER NOT NULL,
            is_predefined INTEGER NOT NULL);
        CREATE TABLE vouchers (
            id TEXT NOT NULL PRIMARY KEY, company_id TEXT NOT NULL REFERENCES companies(id),
            type_id TEXT NOT NULL REFERENCES voucher_types(id), number INTEGER NOT NULL,
            date TEXT NOT NULL, narration TEXT NULL, party_id TEXT NULL REFERENCES ledgers(id),
            cancelled INTEGER NOT NULL, optional INTEGER NOT NULL, post_dated INTEGER NOT NULL);
        CREATE TABLE entry_lines (
            id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT, voucher_id TEXT NOT NULL REFERENCES vouchers(id),
            line_order INTEGER NOT NULL, ledger_id TEXT NOT NULL REFERENCES ledgers(id),
            amount_paisa INTEGER NOT NULL, side INTEGER NOT NULL);
        """;
}
