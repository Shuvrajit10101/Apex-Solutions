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
/// Budget persistence contract (task §7): a company with a budget master (group + ledger lines, both
/// OnClosingBalance and OnNettTransactions, an optional Under group) SAVES and RELOADS with
/// schema_version = 4, preserving every line and reproducing the variance report; and a legacy v3
/// database still opens (auto-migrated to v4) with its data intact and then accepts budget data.
/// </summary>
public sealed class BudgetRoundTripTests
{
    private static (Company Company, Budget Budget) SeedWithBudget()
    {
        // Books begin 2024-03-01 so a pre-period voucher is valid; budget window is Apr–Jun.
        var c = CompanyFactory.CreateSeeded("Budget Persist Co", new DateOnly(2024, 3, 1));

        var cash = c.FindLedgerByName("Cash")!;
        cash.OpeningBalance = Money.FromRupees(1000000m);
        cash.OpeningIsDebit = true;

        var indirect = c.FindGroupByName("Indirect Expenses")!;
        var salaries = new Domain.Ledger(Guid.NewGuid(), "Salaries", indirect.Id, Money.Zero, openingIsDebit: true);
        var rent = new Domain.Ledger(Guid.NewGuid(), "Rent", indirect.Id, Money.Zero, openingIsDebit: true);
        c.AddLedger(salaries);
        c.AddLedger(rent);

        var journal = c.FindVoucherTypeByName("Journal")!;
        var svc = new LedgerService(c);
        void Expense(Domain.Ledger led, decimal amt, DateOnly date) =>
            svc.Post(new Voucher(Guid.NewGuid(), journal.Id, date, new[]
            {
                new EntryLine(led.Id, Money.FromRupees(amt), DrCr.Debit),
                new EntryLine(cash.Id, Money.FromRupees(amt), DrCr.Credit),
            }));

        Expense(salaries, 1000m, new DateOnly(2024, 3, 20)); // pre-period → in closing, not in nett
        Expense(salaries, 6000m, new DateOnly(2024, 4, 5));
        Expense(rent, 3000m, new DateOnly(2024, 4, 10));
        Expense(salaries, 2000m, new DateOnly(2024, 5, 15));

        // Under = the "Indirect Expenses" group (an optional Primary reference).
        var budget = new Budget(Guid.NewGuid(), "Q1 Budget",
            new DateOnly(2024, 4, 1), new DateOnly(2024, 6, 30), underId: indirect.Id, lines: new[]
            {
                BudgetLine.ForLedger(salaries.Id, BudgetType.OnNettTransactions, Money.FromRupees(10000m)),
                BudgetLine.ForLedger(salaries.Id, BudgetType.OnClosingBalance, Money.FromRupees(9000m)),
                BudgetLine.ForGroup(indirect.Id, BudgetType.OnNettTransactions, Money.FromRupees(12000m)),
            });
        c.AddBudget(budget);

        return (c, budget);
    }

    [Fact]
    [Trait("Category", "RoundTrip")]
    public void Budgets_survive_save_reload_with_schema_version_4()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"apex-budget-{Guid.NewGuid():N}.db");
        try
        {
            var (original, budget) = SeedWithBudget();
            var before = BudgetVarianceReport.Build(original, budget);

            using (var write = new SqliteCompanyStore(dbPath))
            {
                write.Save(original);
                // Schema has since advanced past v4 (banking = v5); a fresh DB is stamped to the current
                // version. This test still guarantees the v4 budget data survives round-trip.
                Assert.True(Schema.CurrentVersion >= 4);
            }

            Assert.Equal((long)Schema.CurrentVersion, ReadSchemaVersion(dbPath));

            Company reloaded;
            using (var read = new SqliteCompanyStore(dbPath))
                reloaded = read.Load(original.Id)!;

            // Budget master survived, including the optional Under reference and the period.
            var rBudget = Assert.Single(reloaded.Budgets);
            Assert.Equal("Q1 Budget", rBudget.Name);
            Assert.Equal(reloaded.FindGroupByName("Indirect Expenses")!.Id, rBudget.UnderId);
            Assert.Equal(new DateOnly(2024, 4, 1), rBudget.PeriodFrom);
            Assert.Equal(new DateOnly(2024, 6, 30), rBudget.PeriodTo);

            // All three lines survived, in order, with target/type/amount intact.
            Assert.Equal(3, rBudget.Lines.Count);
            Assert.True(rBudget.Lines[0].IsLedgerTarget);
            Assert.Equal(BudgetType.OnNettTransactions, rBudget.Lines[0].Type);
            Assert.Equal(Money.FromRupees(10000m), rBudget.Lines[0].Amount);
            Assert.True(rBudget.Lines[2].IsGroupTarget);
            Assert.Equal(reloaded.FindGroupByName("Indirect Expenses")!.Id, rBudget.Lines[2].GroupId);

            // The variance report reproduces on the reloaded company.
            var after = BudgetVarianceReport.Build(reloaded, rBudget);
            Assert.Equal(before.Lines.Count, after.Lines.Count);
            for (var i = 0; i < before.Lines.Count; i++)
            {
                Assert.Equal(before.Lines[i].Actual, after.Lines[i].Actual);
                Assert.Equal(before.Lines[i].Variance, after.Lines[i].Variance);
                Assert.Equal(before.Lines[i].VariancePercent, after.Lines[i].VariancePercent);
            }
            // Sanity: Salaries nett 8,000 vs 10,000 budget → variance −2,000; group closing not asserted here.
            Assert.Equal(Money.FromRupees(8000m), after.Lines[0].Actual);
            Assert.Equal(Money.FromRupees(-2000m), after.Lines[0].Variance);
            Assert.Equal(Money.FromRupees(9000m), after.Lines[1].Actual); // Salaries closing incl. pre-period
        }
        finally
        {
            TempDbFile.Delete(dbPath);
        }
    }

    [Fact]
    [Trait("Category", "RoundTrip")]
    public void Legacy_v3_database_opens_and_auto_migrates_to_v4()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"apex-v3legacy-{Guid.NewGuid():N}.db");
        try
        {
            var companyId = Guid.NewGuid();
            var connStr = new SqliteConnectionStringBuilder
            {
                DataSource = dbPath,
                Mode = SqliteOpenMode.ReadWriteCreate,
            }.ToString();

            using (var conn = new SqliteConnection(connStr))
            {
                conn.Open();
                Exec(conn, LegacyV3Ddl);
                Exec(conn, "INSERT INTO schema_version(version) VALUES (3);");
                Exec(conn,
                    """
                    INSERT INTO companies
                      (id, name, mailing_name, address, country, state, pin,
                       financial_year_start, books_begin_from, base_currency_symbol,
                       base_currency_name, decimal_places, decimal_unit_name,
                       primary_cost_category, main_location, profit_and_loss_head_id)
                    VALUES
                      ($id, 'Legacy V3 Co', 'Legacy V3 Co', NULL, 'India', NULL, NULL,
                       '2024-04-01', '2024-04-01', '₹', 'INR', 2, 'Paisa',
                       'Primary Cost Category', 'Main Location', NULL);
                    """,
                    ("$id", companyId.ToString("D")));
                SqliteConnection.ClearPool(conn);
            }

            Assert.Equal(3L, ReadSchemaVersion(dbPath));

            using (var store = new SqliteCompanyStore(dbPath))
            {
                var loaded = store.Load(companyId);
                Assert.NotNull(loaded);
                Assert.Equal("Legacy V3 Co", loaded!.Name);
                Assert.Empty(loaded.Budgets); // no budgets existed in v3
            }

            Assert.Equal((long)Schema.CurrentVersion, ReadSchemaVersion(dbPath));
            Assert.True(TableExists(dbPath, "budgets"), "budgets table was not created.");
            Assert.True(TableExists(dbPath, "budget_lines"), "budget_lines table was not created.");
        }
        finally
        {
            TempDbFile.Delete(dbPath);
        }
    }

    [Fact]
    [Trait("Category", "RoundTrip")]
    public void Migrated_v3_database_then_accepts_and_round_trips_budget_data()
    {
        // Guard: after auto-migrating a v3 db, saving a company with budgets and reloading still works.
        var dbPath = Path.Combine(Path.GetTempPath(), $"apex-v3then4-{Guid.NewGuid():N}.db");
        try
        {
            var connStr = new SqliteConnectionStringBuilder
            {
                DataSource = dbPath, Mode = SqliteOpenMode.ReadWriteCreate,
            }.ToString();
            using (var conn = new SqliteConnection(connStr))
            {
                conn.Open();
                Exec(conn, LegacyV3Ddl);
                Exec(conn, "INSERT INTO schema_version(version) VALUES (3);");
                SqliteConnection.ClearPool(conn);
            }

            var (original, budget) = SeedWithBudget();

            using (var store = new SqliteCompanyStore(dbPath)) // opens v3 → migrates (chained) to current
            {
                Assert.Equal((long)Schema.CurrentVersion, ReadSchemaVersion(dbPath));
                store.Save(original);
            }

            using (var store = new SqliteCompanyStore(dbPath))
            {
                var reloaded = store.Load(original.Id)!;
                var rBudget = Assert.Single(reloaded.Budgets);
                Assert.Equal(3, rBudget.Lines.Count);
                var report = BudgetVarianceReport.Build(reloaded, rBudget);
                Assert.Equal(Money.FromRupees(8000m), report.Lines[0].Actual);
            }
        }
        finally
        {
            TempDbFile.Delete(dbPath);
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

    /// <summary>The pre-budgets v3 DDL (the tables the migration/load touches), kept in the test so it
    /// does not drift when the production <see cref="Schema"/> advances past v3. It includes the cost-centre
    /// tables and the <c>cost_applicable</c> ledger column, but NOT <c>budgets</c>/<c>budget_lines</c>.</summary>
    private const string LegacyV3Ddl = """
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
        """;
}
