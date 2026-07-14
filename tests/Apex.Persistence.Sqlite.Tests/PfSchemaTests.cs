using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Services;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Apex.Persistence.Sqlite.Tests;

/// <summary>
/// The Phase-8 slice-4 <b>Provident Fund</b> schema contract (v32→v33; ER-13). The bump is purely additive: four
/// <c>ALTER TABLE companies</c> columns for the establishment PF config, three <c>ALTER TABLE employees</c>
/// columns for the per-employee PF details, and two <c>ALTER TABLE pay_heads</c> columns for the PF statutory role
/// + PF-wage flag — no new tables, no row rewrites. Covers: a fresh DB stamps to <see cref="Schema.CurrentVersion"/>
/// with the new columns; a legacy v32 DB auto-migrates forward preserving every row (the new columns default off —
/// ER-13); and a full save/load round-trip preserves every PF field.
/// </summary>
public sealed class PfSchemaTests
{
    [Fact]
    [Trait("Category", "RoundTrip")]
    public void Fresh_database_stamps_to_current_version_with_pf_columns()
    {
        var dbPath = TempDb("apex-pf-fresh");
        try
        {
            using (new SqliteCompanyStore(dbPath)) { }

            Assert.True(Schema.CurrentVersion >= 33);
            Assert.Equal((long)Schema.CurrentVersion, ReadSchemaVersion(dbPath));

            foreach (var col in new[] { "pf_config_enabled", "pf_epf_rate_bp", "pf_establishment_code", "pf_cap_at_ceiling" })
                Assert.Contains(col, ColumnNames(dbPath, "companies"));
            foreach (var col in new[] { "pf_applicable", "pf_higher_wages", "pf_join_date" })
                Assert.Contains(col, ColumnNames(dbPath, "employees"));
            foreach (var col in new[] { "pf_component", "part_of_pf_wages" })
                Assert.Contains(col, ColumnNames(dbPath, "pay_heads"));
        }
        finally { Delete(dbPath); }
    }

    [Fact]
    [Trait("Category", "RoundTrip")]
    public void Legacy_v32_database_auto_migrates_to_v33_preserving_every_row()
    {
        var dbPath = TempDb("apex-pf-v32legacy");
        try
        {
            var company = Guid.NewGuid();
            var connStr = new SqliteConnectionStringBuilder { DataSource = dbPath, Mode = SqliteOpenMode.ReadWriteCreate }.ToString();
            using (var conn = new SqliteConnection(connStr))
            {
                conn.Open();
                Exec(conn, MinimalV32Ddl);
                Exec(conn, "INSERT INTO schema_version(version) VALUES (32);");
                Exec(conn, "INSERT INTO companies(id, name) VALUES ($id, 'Legacy V32 Co');", ("$id", company.ToString("D")));
                Exec(conn, "INSERT INTO employees(id, company_id, name) VALUES ($id, $cid, 'Emp');",
                    ("$id", Guid.NewGuid().ToString("D")), ("$cid", company.ToString("D")));
                Exec(conn, "INSERT INTO pay_heads(id, company_id, name) VALUES ($id, $cid, 'Basic');",
                    ("$id", Guid.NewGuid().ToString("D")), ("$cid", company.ToString("D")));
                SqliteConnection.ClearPool(conn);
            }

            Assert.Equal(32L, ReadSchemaVersion(dbPath));
            Assert.DoesNotContain("pf_config_enabled", ColumnNames(dbPath, "companies"));
            Assert.DoesNotContain("pf_applicable", ColumnNames(dbPath, "employees"));
            Assert.DoesNotContain("pf_component", ColumnNames(dbPath, "pay_heads"));

            using (new SqliteCompanyStore(dbPath)) { } // opens v32 → migrates to the current version

            Assert.Equal((long)Schema.CurrentVersion, ReadSchemaVersion(dbPath));
            Assert.Contains("pf_config_enabled", ColumnNames(dbPath, "companies"));
            Assert.Contains("pf_applicable", ColumnNames(dbPath, "employees"));
            Assert.Contains("pf_component", ColumnNames(dbPath, "pay_heads"));

            // Every existing row survived (ER-13); the new flags default off, the code stays NULL.
            Assert.Equal("Legacy V32 Co", ReadScalarStr(dbPath, "SELECT name FROM companies LIMIT 1;"));
            Assert.Equal(0L, ReadScalar(dbPath, "SELECT pf_config_enabled FROM companies LIMIT 1;"));
            Assert.Equal(1200L, ReadScalar(dbPath, "SELECT pf_epf_rate_bp FROM companies LIMIT 1;"));
            Assert.Equal(1L, ReadScalar(dbPath, "SELECT pf_cap_at_ceiling FROM companies LIMIT 1;"));
            Assert.True(IsDbNull(dbPath, "SELECT pf_establishment_code FROM companies LIMIT 1;"));
            Assert.Equal(0L, ReadScalar(dbPath, "SELECT pf_applicable FROM employees LIMIT 1;"));
            Assert.Equal(0L, ReadScalar(dbPath, "SELECT pf_component FROM pay_heads LIMIT 1;"));

            using (new SqliteCompanyStore(dbPath)) { }
            Assert.Equal((long)Schema.CurrentVersion, ReadSchemaVersion(dbPath));
        }
        finally { Delete(dbPath); }
    }

    [Fact]
    [Trait("Category", "RoundTrip")]
    public void Save_load_round_trips_the_pf_config_employee_and_pay_head_fields()
    {
        var dbPath = TempDb("apex-pf-roundtrip");
        try
        {
            var c = CompanyFactory.CreateSeeded("PF DB Co", new DateOnly(2025, 4, 1), new DateOnly(2025, 4, 1));
            var pay = new PayrollService(c);
            pay.EnablePayroll();
            pay.EnableProvidentFund(PfConfig.ReducedEpfRateBasisPoints, "MHBAN0055555000", capWagesAtCeiling: false);
            var ph = new PayHeadService(c);
            var liab = c.FindGroupByName("Current Liabilities")!.Id;
            var indirect = c.FindGroupByName("Indirect Expenses")!.Id;
            ph.CreatePayHead("Basic", PayHeadType.Earnings, PayHeadCalculationType.FlatRate, underGroupId: indirect, partOfPfWages: true);
            ph.CreatePayHead("Employee EPF", PayHeadType.EmployeesStatutoryDeductions, PayHeadCalculationType.AsUserDefinedValue,
                underGroupId: liab, pfComponent: PfStatutoryComponent.EmployeeProvidentFund);
            var e = pay.CreateEmployee("Sanjay Kumar", pay.CreateEmployeeGroup("Staff").Id, uan: "100200300400");
            pay.SetEmployeePfDetails(e.Id, applicable: true, contributeOnHigherWages: true, pfJoinDate: new DateOnly(2021, 1, 1));

            using (var store = new SqliteCompanyStore(dbPath)) store.Save(c);

            Company reloaded;
            using (var store = new SqliteCompanyStore(dbPath)) reloaded = store.Load(c.Id)!;

            var pf = reloaded.PfConfig!;
            Assert.NotNull(pf);
            Assert.Equal(PfConfig.ReducedEpfRateBasisPoints, pf.EpfRateBasisPoints);
            Assert.Equal("MHBAN0055555000", pf.EstablishmentCode);
            Assert.False(pf.CapWagesAtCeiling);

            var re = reloaded.Employees.Single();
            Assert.True(re.PfApplicable);
            Assert.True(re.PfContributeOnHigherWages);
            Assert.Equal(new DateOnly(2021, 1, 1), re.PfJoinDate);

            Assert.True(reloaded.FindPayHeadByName("Basic")!.PartOfPfWages);
            Assert.Equal(PfStatutoryComponent.EmployeeProvidentFund, reloaded.FindPayHeadByName("Employee EPF")!.PfComponent);
            Assert.False(reloaded.FindPayHeadByName("Employee EPF")!.PartOfPfWages);
        }
        finally { Delete(dbPath); }
    }

    // ---- helpers ----

    private static string TempDb(string prefix) => Path.Combine(Path.GetTempPath(), $"{prefix}-{Guid.NewGuid():N}.db");
    private static void Delete(string dbPath) => TempDbFile.Delete(dbPath);

    private static long ReadSchemaVersion(string dbPath) => ReadScalar(dbPath, "SELECT version FROM schema_version LIMIT 1;");

    private static long ReadScalar(string dbPath, string sql)
    {
        using var conn = Open(dbPath);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        var v = Convert.ToInt64(cmd.ExecuteScalar());
        SqliteConnection.ClearPool(conn);
        return v;
    }

    private static string ReadScalarStr(string dbPath, string sql)
    {
        using var conn = Open(dbPath);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        var v = (string)cmd.ExecuteScalar()!;
        SqliteConnection.ClearPool(conn);
        return v;
    }

    private static bool IsDbNull(string dbPath, string sql)
    {
        using var conn = Open(dbPath);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        var v = cmd.ExecuteScalar();
        SqliteConnection.ClearPool(conn);
        return v is null || v is DBNull;
    }

    private static IReadOnlyList<string> ColumnNames(string dbPath, string table)
    {
        using var conn = Open(dbPath);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"PRAGMA table_info({table});";
        var names = new List<string>();
        using (var r = cmd.ExecuteReader())
            while (r.Read())
                names.Add(r.GetString(1));
        SqliteConnection.ClearPool(conn);
        return names;
    }

    private static SqliteConnection Open(string dbPath)
    {
        var conn = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = dbPath, Mode = SqliteOpenMode.ReadWrite }.ToString());
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

    /// <summary>A minimal pre-v33 (v32) DDL: enough for the v32→v33 migration (which ALTERs <c>companies</c>,
    /// <c>employees</c> and <c>pay_heads</c>) plus a data-preservation assertion. Kept in the test so it never
    /// drifts as the production schema advances.</summary>
    private const string MinimalV32Ddl = """
        CREATE TABLE schema_version (version INTEGER NOT NULL);
        CREATE TABLE companies (id TEXT NOT NULL PRIMARY KEY, name TEXT NOT NULL);
        CREATE TABLE stock_items (id TEXT NOT NULL PRIMARY KEY, name TEXT NOT NULL);
        CREATE TABLE ledgers (id TEXT NOT NULL PRIMARY KEY, company_id TEXT NOT NULL, name TEXT NOT NULL);
        CREATE TABLE employees (id TEXT NOT NULL PRIMARY KEY, company_id TEXT NOT NULL, name TEXT NOT NULL);
        CREATE TABLE pay_heads (id TEXT NOT NULL PRIMARY KEY, company_id TEXT NOT NULL, name TEXT NOT NULL);
        -- voucher_types + entry_lines are required because the chain now runs through the v38→v39 RCM migration,
        -- whose ALTER TABLE voucher_types/entry_lines ADD COLUMN … need the tables to exist.
        CREATE TABLE voucher_types (id TEXT NOT NULL PRIMARY KEY, company_id TEXT NOT NULL, name TEXT NOT NULL);
        CREATE TABLE entry_lines (id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT, voucher_id TEXT NOT NULL, line_order INTEGER NOT NULL DEFAULT 0, ledger_id TEXT NOT NULL DEFAULT '', amount_paisa INTEGER NOT NULL DEFAULT 0, side INTEGER NOT NULL DEFAULT 0);
        """;
}
