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
/// Scenario persistence contract (task §7): a company with a Scenario master (Include Actuals + an
/// included and an excluded voucher type), an OPTIONAL voucher, and a Reversing Journal carrying an
/// "Applicable upto" date SAVES and RELOADS with schema_version = 6, preserving the optional flag, the
/// applicable_upto column, and the scenario include/exclude lists, and reproducing the scenario Trial
/// Balance. A legacy v5 database still opens (auto-migrated to v6) with its data intact and then accepts
/// scenario data.
/// </summary>
public sealed class ScenarioRoundTripTests
{
    private static (Company Company, Scenario Scenario, DateOnly AsOf) SeedWithScenario()
    {
        var c = CompanyFactory.CreateSeeded("Scenario Persist Co", new DateOnly(2024, 4, 1));

        var cash = c.FindLedgerByName("Cash")!;
        cash.OpeningBalance = Money.FromRupees(1000000m);
        cash.OpeningIsDebit = true;
        var capital = new Domain.Ledger(Guid.NewGuid(), "Capital", c.FindGroupByName("Capital Account")!.Id,
            Money.FromRupees(1000000m), openingIsDebit: false);
        c.AddLedger(capital);

        var rent = new Domain.Ledger(Guid.NewGuid(), "Rent", c.FindGroupByName("Indirect Expenses")!.Id,
            Money.Zero, openingIsDebit: true);
        var provision = new Domain.Ledger(Guid.NewGuid(), "Provision for Expenses",
            c.FindGroupByName("Provisions")!.Id, Money.Zero, openingIsDebit: false);
        var sales = new Domain.Ledger(Guid.NewGuid(), "Sales", c.FindGroupByName("Sales Accounts")!.Id,
            Money.Zero, openingIsDebit: false);
        c.AddLedger(rent);
        c.AddLedger(provision);
        c.AddLedger(sales);

        var journalType = c.FindVoucherTypeByName("Journal")!;
        var revType = c.FindVoucherTypeByName("Reversing Journal")!;
        var payType = c.FindVoucherTypeByName("Payment")!;
        var svc = new LedgerService(c);

        // A real receipt, an OPTIONAL provision accrual, and a Reversing Journal with an Applicable-upto.
        svc.Post(new Voucher(Guid.NewGuid(), journalType.Id, new DateOnly(2024, 4, 5), new[]
        {
            new EntryLine(cash.Id, Money.FromRupees(8000m), DrCr.Debit),
            new EntryLine(sales.Id, Money.FromRupees(8000m), DrCr.Credit),
        }));
        svc.Post(new Voucher(Guid.NewGuid(), journalType.Id, new DateOnly(2024, 4, 10), new[]
        {
            new EntryLine(rent.Id, Money.FromRupees(5000m), DrCr.Debit),
            new EntryLine(provision.Id, Money.FromRupees(5000m), DrCr.Credit),
        }, optional: true));
        svc.Post(new Voucher(Guid.NewGuid(), revType.Id, new DateOnly(2024, 4, 12), new[]
        {
            new EntryLine(rent.Id, Money.FromRupees(3000m), DrCr.Debit),
            new EntryLine(provision.Id, Money.FromRupees(3000m), DrCr.Credit),
        }, applicableUpto: new DateOnly(2024, 4, 30)));

        // Scenario: include actuals, surface the Journal (Optional) and Reversing Journal, exclude Payments.
        var scenario = new Scenario(Guid.NewGuid(), "What-if", includeActuals: true,
            includedTypeIds: new[] { journalType.Id, revType.Id },
            excludedTypeIds: new[] { payType.Id });
        c.AddScenario(scenario);

        return (c, scenario, new DateOnly(2024, 4, 30));
    }

    [Fact]
    [Trait("Category", "RoundTrip")]
    public void Scenarios_optional_and_applicable_upto_survive_save_reload_with_schema_version_6()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"apex-scenario-{Guid.NewGuid():N}.db");
        try
        {
            var (original, scenario, asOf) = SeedWithScenario();
            var beforeActual = TrialBalance.Build(original, asOf);
            var beforeScenario = TrialBalance.Build(original, asOf, scenario);

            using (var write = new SqliteCompanyStore(dbPath))
            {
                write.Save(original);
                Assert.True(Schema.CurrentVersion >= 6);
            }

            Assert.Equal((long)Schema.CurrentVersion, ReadSchemaVersion(dbPath));

            Company reloaded;
            using (var read = new SqliteCompanyStore(dbPath))
                reloaded = read.Load(original.Id)!;

            // The Optional flag persisted.
            var reloadedOptional = Assert.Single(reloaded.Vouchers, v => v.Optional);
            Assert.True(reloadedOptional.Optional);

            // The Reversing Journal's Applicable-upto persisted.
            var reloadedRev = Assert.Single(reloaded.Vouchers, v => v.ApplicableUpto is not null);
            Assert.Equal(new DateOnly(2024, 4, 30), reloadedRev.ApplicableUpto);

            // The scenario master persisted with its include/exclude lists and IncludeActuals.
            var rScenario = Assert.Single(reloaded.Scenarios);
            Assert.Equal("What-if", rScenario.Name);
            Assert.True(rScenario.IncludeActuals);
            Assert.Equal(2, rScenario.IncludedTypeIds.Count);
            Assert.Single(rScenario.ExcludedTypeIds);
            Assert.True(rScenario.Includes(reloaded.FindVoucherTypeByName("Journal")!.Id));
            Assert.True(rScenario.Includes(reloaded.FindVoucherTypeByName("Reversing Journal")!.Id));
            Assert.True(rScenario.Excludes(reloaded.FindVoucherTypeByName("Payment")!.Id));

            // Both the plain actual and the scenario Trial Balance reproduce on the reloaded company.
            var afterActual = TrialBalance.Build(reloaded, asOf);
            var afterScenario = TrialBalance.Build(reloaded, asOf, rScenario);
            Assert.Equal(beforeActual.TotalDebit.Amount, afterActual.TotalDebit.Amount);
            Assert.Equal(beforeScenario.TotalDebit.Amount, afterScenario.TotalDebit.Amount);
            Assert.Equal(beforeScenario.TotalCredit.Amount, afterScenario.TotalCredit.Amount);

            // Sanity: the scenario Rent = 5,000 (optional) + 3,000 (reversing, within upto) = 8,000 Dr;
            // the actual has no Rent at all.
            Assert.DoesNotContain(afterActual.Rows, r => r.LedgerName == "Rent");
            var rentRow = Assert.Single(afterScenario.Rows, r => r.LedgerName == "Rent");
            Assert.Equal(8000m, rentRow.Debit.Amount);
        }
        finally
        {
            TempDbFile.Delete(dbPath);
        }
    }

    [Fact]
    [Trait("Category", "RoundTrip")]
    public void Legacy_v5_database_opens_and_auto_migrates_to_v6()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"apex-v5legacy-{Guid.NewGuid():N}.db");
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
                Exec(conn, LegacyV5Ddl);
                Exec(conn, "INSERT INTO schema_version(version) VALUES (5);");
                Exec(conn,
                    """
                    INSERT INTO companies
                      (id, name, mailing_name, address, country, state, pin,
                       financial_year_start, books_begin_from, base_currency_symbol,
                       base_currency_name, decimal_places, decimal_unit_name,
                       primary_cost_category, main_location, profit_and_loss_head_id)
                    VALUES
                      ($id, 'Legacy V5 Co', 'Legacy V5 Co', NULL, 'India', NULL, NULL,
                       '2024-04-01', '2024-04-01', '₹', 'INR', 2, 'Paisa',
                       'Primary Cost Category', 'Main Location', NULL);
                    """,
                    ("$id", companyId.ToString("D")));
                SqliteConnection.ClearPool(conn);
            }

            Assert.Equal(5L, ReadSchemaVersion(dbPath));

            using (var store = new SqliteCompanyStore(dbPath))
            {
                var loaded = store.Load(companyId);
                Assert.NotNull(loaded);
                Assert.Equal("Legacy V5 Co", loaded!.Name);
                Assert.Empty(loaded.Scenarios); // no scenarios existed in v5
            }

            Assert.Equal((long)Schema.CurrentVersion, ReadSchemaVersion(dbPath));
            Assert.True(TableExists(dbPath, "scenarios"), "scenarios table was not created.");
            Assert.True(TableExists(dbPath, "scenario_voucher_types"), "scenario_voucher_types table was not created.");
            Assert.True(ColumnExists(dbPath, "vouchers", "applicable_upto"), "applicable_upto column was not added.");
        }
        finally
        {
            TempDbFile.Delete(dbPath);
        }
    }

    [Fact]
    [Trait("Category", "RoundTrip")]
    public void Migrated_v5_database_then_accepts_and_round_trips_scenario_data()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"apex-v5then6-{Guid.NewGuid():N}.db");
        try
        {
            var connStr = new SqliteConnectionStringBuilder
            {
                DataSource = dbPath, Mode = SqliteOpenMode.ReadWriteCreate,
            }.ToString();
            using (var conn = new SqliteConnection(connStr))
            {
                conn.Open();
                Exec(conn, LegacyV5Ddl);
                Exec(conn, "INSERT INTO schema_version(version) VALUES (5);");
                SqliteConnection.ClearPool(conn);
            }

            var (original, scenario, asOf) = SeedWithScenario();
            var beforeScenario = TrialBalance.Build(original, asOf, scenario);

            using (var store = new SqliteCompanyStore(dbPath)) // opens v5 → migrates (chained) to current
            {
                Assert.Equal((long)Schema.CurrentVersion, ReadSchemaVersion(dbPath));
                store.Save(original);
            }

            using (var store = new SqliteCompanyStore(dbPath))
            {
                var reloaded = store.Load(original.Id)!;
                var rScenario = Assert.Single(reloaded.Scenarios);
                Assert.Equal(2, rScenario.IncludedTypeIds.Count);
                var after = TrialBalance.Build(reloaded, asOf, rScenario);
                Assert.Equal(beforeScenario.TotalDebit.Amount, after.TotalDebit.Amount);
                Assert.Contains(reloaded.Vouchers, v => v.ApplicableUpto == new DateOnly(2024, 4, 30));
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

    /// <summary>The pre-scenarios v5 DDL (the tables the migration/load touches), kept in the test so it
    /// does not drift when the production <see cref="Schema"/> advances past v5. It includes budgets and
    /// banking, but NOT <c>scenarios</c>/<c>scenario_voucher_types</c> nor the <c>applicable_upto</c>
    /// voucher column.</summary>
    private const string LegacyV5Ddl = """
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
            cost_applicable INTEGER NULL,
            enable_cheque_printing INTEGER NOT NULL DEFAULT 0, cheque_bank_name TEXT NULL);
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
        CREATE TABLE bank_allocations (
            id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT, entry_line_id INTEGER NOT NULL REFERENCES entry_lines(id),
            transaction_type INTEGER NOT NULL, instrument_number TEXT NOT NULL,
            instrument_date TEXT NULL, bank_date TEXT NULL);
        """;
}
