using Apex.Ledger;
using Apex.Ledger.Banking;
using Apex.Ledger.Domain;
using Apex.Ledger.Reports;
using Apex.Ledger.Services;
using Apex.Persistence.Sqlite;
using Microsoft.Data.Sqlite;
using Xunit;
using Domain = Apex.Ledger.Domain;

namespace Apex.Persistence.Sqlite.Tests;

/// <summary>
/// Banking persistence contract (task §5; catalog §8): a company with a bank ledger, cheque-printing
/// configuration, and bank-allocated voucher lines (one cleared, one still uncleared) SAVES and RELOADS
/// with schema_version = 5, preserving the transaction type / instrument / bank date on every line and
/// reproducing the Bank Reconciliation book-vs-bank figures; and a legacy v4 database still opens
/// (auto-migrated to v5) and then accepts bank data.
/// </summary>
public sealed class BankingRoundTripTests
{
    private static (Company Company, Domain.Ledger Hdfc, Guid ClearedVoucherId, Guid UnclearedVoucherId) SeedWithBanking()
    {
        var c = CompanyFactory.CreateSeeded("Bank Persist Co", new DateOnly(2024, 4, 1));

        var cash = c.FindLedgerByName("Cash")!;
        cash.OpeningBalance = Money.FromRupees(1000000m);
        cash.OpeningIsDebit = true;

        var hdfc = new Domain.Ledger(Guid.NewGuid(), "HDFC Bank", c.FindGroupByName("Bank Accounts")!.Id,
            Money.FromRupees(500000m), openingIsDebit: true)
        {
            EnableChequePrinting = true,
            ChequePrintingBankName = "HDFC Bank",
        };
        c.AddLedger(hdfc);

        var rent = new Domain.Ledger(Guid.NewGuid(), "Rent", c.FindGroupByName("Indirect Expenses")!.Id,
            Money.Zero, openingIsDebit: true);
        c.AddLedger(rent);

        var payment = c.FindVoucherTypeByName("Payment")!;
        var svc = new LedgerService(c);

        // Cleared cheque: 40,000 out on 2024-04-05, reconciled on 2024-04-28.
        var cleared = svc.Post(new Voucher(Guid.NewGuid(), payment.Id, new DateOnly(2024, 4, 5), new[]
        {
            new EntryLine(rent.Id, Money.FromRupees(40000m), DrCr.Debit),
            new EntryLine(hdfc.Id, Money.FromRupees(40000m), DrCr.Credit,
                bankAllocation: new BankAllocation(BankTransactionType.ChequeOrDD, "100200",
                    instrumentDate: new DateOnly(2024, 4, 5), bankDate: new DateOnly(2024, 4, 28))),
        }));

        // Uncleared cheque: 15,000 out on 2024-04-20, no bank date yet.
        var uncleared = svc.Post(new Voucher(Guid.NewGuid(), payment.Id, new DateOnly(2024, 4, 20), new[]
        {
            new EntryLine(rent.Id, Money.FromRupees(15000m), DrCr.Debit),
            new EntryLine(hdfc.Id, Money.FromRupees(15000m), DrCr.Credit,
                bankAllocation: new BankAllocation(BankTransactionType.NEFT, "UTR7788")),
        }));

        return (c, hdfc, cleared.Id, uncleared.Id);
    }

    [Fact]
    [Trait("Category", "RoundTrip")]
    public void Bank_allocations_and_cheque_config_survive_save_reload_with_schema_version_5()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"apex-bank-{Guid.NewGuid():N}.db");
        try
        {
            var (original, hdfc, clearedId, unclearedId) = SeedWithBanking();
            var asOf = new DateOnly(2024, 4, 30);
            var beforeBrs = BankReconciliation.Build(original, hdfc, asOf);

            using (var write = new SqliteCompanyStore(dbPath))
            {
                write.Save(original);
                Assert.Equal(5, Schema.CurrentVersion);
            }

            Assert.Equal((long)Schema.CurrentVersion, ReadSchemaVersion(dbPath));

            Company reloaded;
            using (var read = new SqliteCompanyStore(dbPath))
                reloaded = read.Load(original.Id)!;

            // Cheque-printing config survived on the bank ledger.
            var rHdfc = reloaded.FindLedgerByName("HDFC Bank")!;
            Assert.True(rHdfc.EnableChequePrinting);
            Assert.Equal("HDFC Bank", rHdfc.ChequePrintingBankName);

            // The cleared cheque line kept its type, instrument, instrument date AND bank date.
            var clearedLine = reloaded.FindVoucher(clearedId)!.Lines.Single(l => l.LedgerId == rHdfc.Id);
            Assert.NotNull(clearedLine.BankAllocation);
            Assert.Equal(BankTransactionType.ChequeOrDD, clearedLine.BankAllocation!.TransactionType);
            Assert.Equal("100200", clearedLine.BankAllocation.InstrumentNumber);
            Assert.Equal(new DateOnly(2024, 4, 5), clearedLine.BankAllocation.InstrumentDate);
            Assert.Equal(new DateOnly(2024, 4, 28), clearedLine.BankAllocation.BankDate);
            Assert.True(clearedLine.BankAllocation.IsReconciled);

            // The uncleared cheque line kept its details and stayed unreconciled (bank date null).
            var unclearedLine = reloaded.FindVoucher(unclearedId)!.Lines.Single(l => l.LedgerId == rHdfc.Id);
            Assert.Equal(BankTransactionType.NEFT, unclearedLine.BankAllocation!.TransactionType);
            Assert.Equal("UTR7788", unclearedLine.BankAllocation.InstrumentNumber);
            Assert.Null(unclearedLine.BankAllocation.BankDate);
            Assert.False(unclearedLine.BankAllocation.IsReconciled);

            // The BRS reproduces on the reloaded company: books 4,45,000 Dr; bank reflects only the
            // cleared cheque → 4,60,000 Dr; the 15,000 uncleared cheque is the difference.
            var afterBrs = BankReconciliation.Build(reloaded, rHdfc, asOf);
            Assert.Equal(beforeBrs.BalanceAsPerBooks, afterBrs.BalanceAsPerBooks);
            Assert.Equal(beforeBrs.BalanceAsPerBank, afterBrs.BalanceAsPerBank);
            Assert.Equal(Money.FromRupees(445000m), afterBrs.BalanceAsPerBooks.Amount);
            Assert.Equal(Money.FromRupees(460000m), afterBrs.BalanceAsPerBank.Amount);
            Assert.Single(afterBrs.Unreconciled);
            Assert.Single(afterBrs.Reconciled);
            Assert.Equal(Money.FromRupees(-15000m), afterBrs.AmountNotReflectedInBank);
        }
        finally
        {
            if (File.Exists(dbPath)) File.Delete(dbPath);
        }
    }

    [Fact]
    [Trait("Category", "RoundTrip")]
    public void Legacy_v4_database_opens_and_auto_migrates_to_v5()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"apex-v4legacy-{Guid.NewGuid():N}.db");
        try
        {
            var companyId = Guid.NewGuid();
            var connStr = new SqliteConnectionStringBuilder
            {
                DataSource = dbPath, Mode = SqliteOpenMode.ReadWriteCreate,
            }.ToString();

            using (var conn = new SqliteConnection(connStr))
            {
                conn.Open();
                Exec(conn, LegacyV4Ddl);
                Exec(conn, "INSERT INTO schema_version(version) VALUES (4);");
                Exec(conn,
                    """
                    INSERT INTO companies
                      (id, name, mailing_name, address, country, state, pin,
                       financial_year_start, books_begin_from, base_currency_symbol,
                       base_currency_name, decimal_places, decimal_unit_name,
                       primary_cost_category, main_location, profit_and_loss_head_id)
                    VALUES
                      ($id, 'Legacy V4 Co', 'Legacy V4 Co', NULL, 'India', NULL, NULL,
                       '2024-04-01', '2024-04-01', '₹', 'INR', 2, 'Paisa',
                       'Primary Cost Category', 'Main Location', NULL);
                    """,
                    ("$id", companyId.ToString("D")));
                SqliteConnection.ClearPool(conn);
            }

            Assert.Equal(4L, ReadSchemaVersion(dbPath));

            using (var store = new SqliteCompanyStore(dbPath))
            {
                var loaded = store.Load(companyId);
                Assert.NotNull(loaded);
                Assert.Equal("Legacy V4 Co", loaded!.Name);
            }

            Assert.Equal((long)Schema.CurrentVersion, ReadSchemaVersion(dbPath));
            Assert.True(TableExists(dbPath, "bank_allocations"), "bank_allocations table was not created.");
            Assert.True(ColumnExists(dbPath, "ledgers", "enable_cheque_printing"));
            Assert.True(ColumnExists(dbPath, "ledgers", "cheque_bank_name"));
        }
        finally
        {
            if (File.Exists(dbPath)) File.Delete(dbPath);
        }
    }

    [Fact]
    [Trait("Category", "RoundTrip")]
    public void Migrated_v4_database_then_accepts_and_round_trips_bank_data()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"apex-v4then5-{Guid.NewGuid():N}.db");
        try
        {
            var connStr = new SqliteConnectionStringBuilder
            {
                DataSource = dbPath, Mode = SqliteOpenMode.ReadWriteCreate,
            }.ToString();
            using (var conn = new SqliteConnection(connStr))
            {
                conn.Open();
                Exec(conn, LegacyV4Ddl);
                Exec(conn, "INSERT INTO schema_version(version) VALUES (4);");
                SqliteConnection.ClearPool(conn);
            }

            var (original, _, _, _) = SeedWithBanking();
            var asOf = new DateOnly(2024, 4, 30);

            using (var store = new SqliteCompanyStore(dbPath)) // opens v4 → migrates to current
            {
                Assert.Equal((long)Schema.CurrentVersion, ReadSchemaVersion(dbPath));
                store.Save(original);
            }

            using (var store = new SqliteCompanyStore(dbPath))
            {
                var reloaded = store.Load(original.Id)!;
                var hdfc = reloaded.FindLedgerByName("HDFC Bank")!;
                var brs = BankReconciliation.Build(reloaded, hdfc, asOf);
                Assert.Equal(Money.FromRupees(460000m), brs.BalanceAsPerBank.Amount);
                Assert.Single(brs.Unreconciled);
            }
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
            DataSource = dbPath, Mode = SqliteOpenMode.ReadWrite,
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

    /// <summary>The pre-banking v4 DDL (the tables the migration/load touches), kept in the test so it does
    /// not drift when the production <see cref="Schema"/> advances past v4. Ledgers carry the v2/v3 columns
    /// but NOT the v5 cheque-printing columns; there is no bank_allocations table.</summary>
    private const string LegacyV4Ddl = """
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
            alias TEXT NULL, is_predefined INTEGER NOT NULL,
            maintain_bill_by_bill INTEGER NOT NULL DEFAULT 0, default_credit_period INTEGER NULL,
            cost_applicable INTEGER NULL);
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
        CREATE TABLE bill_allocations (
            id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT, entry_line_id INTEGER NOT NULL REFERENCES entry_lines(id),
            alloc_order INTEGER NOT NULL, ref_type INTEGER NOT NULL, name TEXT NOT NULL,
            amount_paisa INTEGER NOT NULL, due_date TEXT NULL, credit_days INTEGER NULL);
        CREATE TABLE cost_categories (
            id TEXT NOT NULL PRIMARY KEY, company_id TEXT NOT NULL REFERENCES companies(id),
            name TEXT NOT NULL, allocate_revenue INTEGER NOT NULL, allocate_non_revenue INTEGER NOT NULL,
            is_predefined INTEGER NOT NULL);
        CREATE TABLE cost_centres (
            id TEXT NOT NULL PRIMARY KEY, company_id TEXT NOT NULL REFERENCES companies(id),
            name TEXT NOT NULL, category_id TEXT NOT NULL REFERENCES cost_categories(id),
            parent_id TEXT NULL REFERENCES cost_centres(id), alias TEXT NULL);
        CREATE TABLE cost_allocations (
            id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT, entry_line_id INTEGER NOT NULL REFERENCES entry_lines(id),
            alloc_order INTEGER NOT NULL, category_id TEXT NOT NULL REFERENCES cost_categories(id),
            centre_id TEXT NOT NULL REFERENCES cost_centres(id), amount_paisa INTEGER NOT NULL);
        CREATE TABLE budgets (
            id TEXT NOT NULL PRIMARY KEY, company_id TEXT NOT NULL REFERENCES companies(id),
            name TEXT NOT NULL, under_id TEXT NULL REFERENCES groups(id),
            period_from TEXT NOT NULL, period_to TEXT NOT NULL);
        CREATE TABLE budget_lines (
            id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT, budget_id TEXT NOT NULL REFERENCES budgets(id),
            line_order INTEGER NOT NULL, group_id TEXT NULL REFERENCES groups(id),
            ledger_id TEXT NULL REFERENCES ledgers(id), budget_type INTEGER NOT NULL, amount_paisa INTEGER NOT NULL);
        """;
}
