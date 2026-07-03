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
/// Cost-centre persistence contract (task §5/§6): a company with cost categories, hierarchical cost
/// centres, a cost-applicable override, and posted cost allocations SAVES and RELOADS with
/// schema_version = 3, preserving every master and allocation; and a legacy v2 database still opens
/// (auto-migrated to v3) with its data intact.
/// </summary>
public sealed class CostCentreRoundTripTests
{
    private static (Company Company, CostCategory Category, CostCentre Delhi, CostCentre DelhiNorth, CostCentre Mumbai)
        SeedWithCosts()
    {
        var c = CompanyFactory.CreateSeeded("Cost Persist Co", new DateOnly(2024, 4, 1));

        var cash = c.FindLedgerByName("Cash")!;
        cash.OpeningBalance = Money.FromRupees(1000000m);
        cash.OpeningIsDebit = true;

        var salaries = new Domain.Ledger(Guid.NewGuid(), "Salaries", c.FindGroupByName("Indirect Expenses")!.Id,
            Money.Zero, openingIsDebit: true);
        c.AddLedger(salaries);
        var rent = new Domain.Ledger(Guid.NewGuid(), "Rent", c.FindGroupByName("Indirect Expenses")!.Id,
            Money.Zero, openingIsDebit: true);
        c.AddLedger(rent);

        // An explicit override: Cash forced cost-applicable ON (asset would default OFF).
        cash.CostCentresApplicable = true;

        var category = c.FindCostCategoryByName("Primary Cost Category")!;
        var delhi = new CostCentre(Guid.NewGuid(), "Delhi", category.Id);
        var mumbai = new CostCentre(Guid.NewGuid(), "Mumbai", category.Id);
        c.AddCostCentre(delhi);
        c.AddCostCentre(mumbai);
        var delhiNorth = new CostCentre(Guid.NewGuid(), "Delhi-North", category.Id, parentId: delhi.Id, alias: "DL-N");
        c.AddCostCentre(delhiNorth);

        var journal = c.FindVoucherTypeByName("Journal")!;
        var svc = new LedgerService(c);

        // Salaries 10000 split Delhi 6000 + Mumbai 4000.
        svc.Post(new Voucher(Guid.NewGuid(), journal.Id, new DateOnly(2024, 4, 5), new[]
        {
            new EntryLine(salaries.Id, Money.FromRupees(10000m), DrCr.Debit, billAllocations: null, costAllocations: new[]
            {
                new CostAllocation(category.Id, delhi.Id, Money.FromRupees(6000m)),
                new CostAllocation(category.Id, mumbai.Id, Money.FromRupees(4000m)),
            }),
            new EntryLine(cash.Id, Money.FromRupees(10000m), DrCr.Credit),
        }));

        // Rent 3000 all to Delhi-North (child).
        svc.Post(new Voucher(Guid.NewGuid(), journal.Id, new DateOnly(2024, 4, 10), new[]
        {
            new EntryLine(rent.Id, Money.FromRupees(3000m), DrCr.Debit, billAllocations: null, costAllocations: new[]
            {
                new CostAllocation(category.Id, delhiNorth.Id, Money.FromRupees(3000m)),
            }),
            new EntryLine(cash.Id, Money.FromRupees(3000m), DrCr.Credit),
        }));

        return (c, category, delhi, delhiNorth, mumbai);
    }

    [Fact]
    [Trait("Category", "RoundTrip")]
    public void Cost_masters_and_allocations_survive_save_reload_with_schema_version_3()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"apex-cost-{Guid.NewGuid():N}.db");
        try
        {
            var (original, category, delhi, delhiNorth, mumbai) = SeedWithCosts();
            var from = new DateOnly(2024, 4, 1);
            var to = new DateOnly(2024, 4, 30);

            var beforeSummary = CostReports.BuildCategorySummary(original, from, to);
            var beforeBreakup = CostReports.BuildCostCentreBreakup(original, from, to);

            using (var write = new SqliteCompanyStore(dbPath))
            {
                write.Save(original);
                // Schema has since advanced past v3 (budgets = v4); a fresh DB is stamped to the current
                // version. This test still guarantees the v3 cost-centre data survives round-trip.
                Assert.True(Schema.CurrentVersion >= 3);
            }

            Assert.Equal((long)Schema.CurrentVersion, ReadSchemaVersion(dbPath));

            Company reloaded;
            using (var read = new SqliteCompanyStore(dbPath))
                reloaded = read.Load(original.Id)!;

            // Categories survived (the seeded Primary Cost Category).
            var rCat = Assert.Single(reloaded.CostCategories);
            Assert.Equal("Primary Cost Category", rCat.Name);
            Assert.True(rCat.IsPredefined);
            Assert.True(rCat.AllocateRevenueItems);

            // Centres + hierarchy + alias survived.
            Assert.Equal(3, reloaded.CostCentres.Count);
            var rDelhi = reloaded.FindCostCentreByName("Delhi")!;
            var rChild = reloaded.FindCostCentreByName("Delhi-North")!;
            Assert.True(rDelhi.IsPrimary);
            Assert.Equal(rDelhi.Id, rChild.ParentId);
            Assert.Equal("DL-N", rChild.Alias);
            Assert.Equal(rCat.Id, rChild.CategoryId);

            // The cost-applicable override survived (Cash forced ON).
            var rCash = reloaded.FindLedgerByName("Cash")!;
            Assert.True(rCash.CostCentresApplicable);
            Assert.True(ClassificationRules.CostCentresApplicableFor(rCash, reloaded));
            // A default (auto) expense ledger stays null after reload.
            var rSalaries = reloaded.FindLedgerByName("Salaries")!;
            Assert.Null(rSalaries.CostCentresApplicable);

            // Allocations survived: the salaries split line has two allocations totalling 10000.
            var splitLine = reloaded.Vouchers
                .SelectMany(v => v.Lines)
                .Single(l => l.LedgerId == rSalaries.Id && l.CostAllocations.Count == 2);
            Assert.Equal(Money.FromRupees(10000m), splitLine.CostAllocationTotal);
            Assert.Equal(Money.FromRupees(6000m), splitLine.CostAllocations.Single(a => a.CentreId == rDelhi.Id).Amount);

            // The three cost reports reproduce on the reloaded company.
            var afterSummary = CostReports.BuildCategorySummary(reloaded, from, to);
            var afterBreakup = CostReports.BuildCostCentreBreakup(reloaded, from, to);
            Assert.Equal(beforeSummary.GrandTotal, afterSummary.GrandTotal);
            Assert.Equal(Money.FromRupees(13000m), afterSummary.GrandTotal);
            // Delhi rolled-up = 6000 own + 3000 child = 9000, preserved.
            Assert.Equal(Money.FromRupees(9000m), afterBreakup.Centres.Single(l => l.CentreName == "Delhi").RolledUpTotal);
            Assert.Equal(
                beforeBreakup.Centres.Single(l => l.CentreName == "Delhi").RolledUpTotal,
                afterBreakup.Centres.Single(l => l.CentreName == "Delhi").RolledUpTotal);
        }
        finally
        {
            if (File.Exists(dbPath)) File.Delete(dbPath);
        }
    }

    [Fact]
    [Trait("Category", "RoundTrip")]
    public void Legacy_v2_database_opens_and_auto_migrates_to_v3()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"apex-v2legacy-{Guid.NewGuid():N}.db");
        try
        {
            // Build a minimal LEGACY v2 database by hand (the pre-cost-centre shape): a schema_version
            // marker of 2 plus a companies table with one row. Opening it with the store must migrate.
            var companyId = Guid.NewGuid();
            var connStr = new SqliteConnectionStringBuilder
            {
                DataSource = dbPath,
                Mode = SqliteOpenMode.ReadWriteCreate,
            }.ToString();

            using (var conn = new SqliteConnection(connStr))
            {
                conn.Open();
                Exec(conn, LegacyV2Ddl);
                Exec(conn, "INSERT INTO schema_version(version) VALUES (2);");
                Exec(conn,
                    """
                    INSERT INTO companies
                      (id, name, mailing_name, address, country, state, pin,
                       financial_year_start, books_begin_from, base_currency_symbol,
                       base_currency_name, decimal_places, decimal_unit_name,
                       primary_cost_category, main_location, profit_and_loss_head_id)
                    VALUES
                      ($id, 'Legacy V2 Co', 'Legacy V2 Co', NULL, 'India', NULL, NULL,
                       '2024-04-01', '2024-04-01', '₹', 'INR', 2, 'Paisa',
                       'Primary Cost Category', 'Main Location', NULL);
                    """,
                    ("$id", companyId.ToString("D")));
                SqliteConnection.ClearPool(conn);
            }

            Assert.Equal(2L, ReadSchemaVersion(dbPath));

            // Open with the current adapter → auto-migrate (chained v2→v3→…→current).
            using (var store = new SqliteCompanyStore(dbPath))
            {
                var loaded = store.Load(companyId);
                Assert.NotNull(loaded);
                Assert.Equal("Legacy V2 Co", loaded!.Name);
                // No cost masters existed in v2 → the migrated company has none.
                Assert.Empty(loaded.CostCategories);
                Assert.Empty(loaded.CostCentres);
            }

            // The chained migration bumped the marker to the current version and created the v3 structures.
            Assert.Equal((long)Schema.CurrentVersion, ReadSchemaVersion(dbPath));
            Assert.True(TableExists(dbPath, "cost_categories"), "cost_categories table was not created.");
            Assert.True(TableExists(dbPath, "cost_centres"), "cost_centres table was not created.");
            Assert.True(TableExists(dbPath, "cost_allocations"), "cost_allocations table was not created.");
            Assert.True(ColumnExists(dbPath, "ledgers", "cost_applicable"));
        }
        finally
        {
            if (File.Exists(dbPath)) File.Delete(dbPath);
        }
    }

    [Fact]
    [Trait("Category", "RoundTrip")]
    public void Migrated_v2_database_then_accepts_and_round_trips_cost_data()
    {
        // Guard against a migration that creates tables incompatible with the writer: after auto-migrating
        // a v2 db, saving a company with cost data and reloading must still work.
        var dbPath = Path.Combine(Path.GetTempPath(), $"apex-v2then3-{Guid.NewGuid():N}.db");
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
                Exec(conn, LegacyV2Ddl);
                Exec(conn, "INSERT INTO schema_version(version) VALUES (2);");
                SqliteConnection.ClearPool(conn);
            }

            var (original, _, delhi, _, _) = SeedWithCosts();
            var from = new DateOnly(2024, 4, 1);
            var to = new DateOnly(2024, 4, 30);

            using (var store = new SqliteCompanyStore(dbPath)) // opens v2 → migrates (chained) to current
            {
                Assert.Equal((long)Schema.CurrentVersion, ReadSchemaVersion(dbPath));
                store.Save(original);
            }

            using (var store = new SqliteCompanyStore(dbPath))
            {
                var reloaded = store.Load(original.Id)!;
                var summary = CostReports.BuildCategorySummary(reloaded, from, to);
                Assert.Equal(Money.FromRupees(13000m), summary.GrandTotal);
                Assert.Equal(3, reloaded.CostCentres.Count);
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

    /// <summary>The pre-cost-centre v2 DDL (the tables the migration/load touches), kept in the test so it
    /// does not drift when the production <see cref="Schema"/> advances past v2. Ledgers carry the v2
    /// bill-wise columns but NOT <c>cost_applicable</c>.</summary>
    private const string LegacyV2Ddl = """
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
            maintain_bill_by_bill INTEGER NOT NULL DEFAULT 0, default_credit_period INTEGER NULL);
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
        """;
}
