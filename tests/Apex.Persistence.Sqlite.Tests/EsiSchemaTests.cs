using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Services;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Apex.Persistence.Sqlite.Tests;

/// <summary>
/// The Phase-8 slice-5 <b>Employees' State Insurance</b> schema contract (v33→v34; ER-13). The bump is purely
/// additive: four <c>ALTER TABLE companies</c> columns for the establishment ESI config, one <c>ALTER TABLE
/// employees</c> column for per-employee ESI applicability, and three <c>ALTER TABLE pay_heads</c> columns for the
/// ESI statutory role + ESI-wage flag + overtime marker — no new tables, no row rewrites. Covers: a fresh DB stamps
/// to <see cref="Schema.CurrentVersion"/> with the new columns; a legacy v33 DB auto-migrates forward preserving
/// every row (the new columns default off — ER-13); and a full save/load round-trip preserves every ESI field.
/// </summary>
public sealed class EsiSchemaTests
{
    [Fact]
    [Trait("Category", "RoundTrip")]
    public void Fresh_database_stamps_to_current_version_with_esi_columns()
    {
        var dbPath = TempDb("apex-esi-fresh");
        try
        {
            using (new SqliteCompanyStore(dbPath)) { }

            Assert.True(Schema.CurrentVersion >= 34);
            Assert.Equal((long)Schema.CurrentVersion, ReadSchemaVersion(dbPath));

            foreach (var col in new[] { "esi_config_enabled", "esi_ee_rate_bp", "esi_er_rate_bp", "esi_employer_code" })
                Assert.Contains(col, ColumnNames(dbPath, "companies"));
            foreach (var col in new[] { "esi_applicable", "esi_person_with_disability" })
                Assert.Contains(col, ColumnNames(dbPath, "employees"));
            foreach (var col in new[] { "esi_component", "part_of_esi_wages", "is_overtime" })
                Assert.Contains(col, ColumnNames(dbPath, "pay_heads"));
        }
        finally { Delete(dbPath); }
    }

    [Fact]
    [Trait("Category", "RoundTrip")]
    public void Legacy_v33_database_auto_migrates_to_v34_preserving_every_row()
    {
        var dbPath = TempDb("apex-esi-v33legacy");
        try
        {
            var company = Guid.NewGuid();
            var connStr = new SqliteConnectionStringBuilder { DataSource = dbPath, Mode = SqliteOpenMode.ReadWriteCreate }.ToString();
            using (var conn = new SqliteConnection(connStr))
            {
                conn.Open();
                Exec(conn, MinimalV33Ddl);
                Exec(conn, "INSERT INTO schema_version(version) VALUES (33);");
                Exec(conn, "INSERT INTO companies(id, name) VALUES ($id, 'Legacy V33 Co');", ("$id", company.ToString("D")));
                Exec(conn, "INSERT INTO employees(id, company_id, name) VALUES ($id, $cid, 'Emp');",
                    ("$id", Guid.NewGuid().ToString("D")), ("$cid", company.ToString("D")));
                Exec(conn, "INSERT INTO pay_heads(id, company_id, name) VALUES ($id, $cid, 'Basic');",
                    ("$id", Guid.NewGuid().ToString("D")), ("$cid", company.ToString("D")));
                SqliteConnection.ClearPool(conn);
            }

            Assert.Equal(33L, ReadSchemaVersion(dbPath));
            Assert.DoesNotContain("esi_config_enabled", ColumnNames(dbPath, "companies"));
            Assert.DoesNotContain("esi_applicable", ColumnNames(dbPath, "employees"));
            Assert.DoesNotContain("esi_component", ColumnNames(dbPath, "pay_heads"));

            using (new SqliteCompanyStore(dbPath)) { } // opens v33 → migrates to the current version

            Assert.Equal((long)Schema.CurrentVersion, ReadSchemaVersion(dbPath));
            Assert.Contains("esi_config_enabled", ColumnNames(dbPath, "companies"));
            Assert.Contains("esi_applicable", ColumnNames(dbPath, "employees"));
            Assert.Contains("esi_component", ColumnNames(dbPath, "pay_heads"));

            // Every existing row survived (ER-13); the new flags default off, the rates carry their defaults.
            Assert.Equal("Legacy V33 Co", ReadScalarStr(dbPath, "SELECT name FROM companies LIMIT 1;"));
            Assert.Equal(0L, ReadScalar(dbPath, "SELECT esi_config_enabled FROM companies LIMIT 1;"));
            Assert.Equal(75L, ReadScalar(dbPath, "SELECT esi_ee_rate_bp FROM companies LIMIT 1;"));
            Assert.Equal(325L, ReadScalar(dbPath, "SELECT esi_er_rate_bp FROM companies LIMIT 1;"));
            Assert.True(IsDbNull(dbPath, "SELECT esi_employer_code FROM companies LIMIT 1;"));
            Assert.Equal(0L, ReadScalar(dbPath, "SELECT esi_applicable FROM employees LIMIT 1;"));
            Assert.Equal(0L, ReadScalar(dbPath, "SELECT esi_component FROM pay_heads LIMIT 1;"));
            Assert.Equal(0L, ReadScalar(dbPath, "SELECT is_overtime FROM pay_heads LIMIT 1;"));

            using (new SqliteCompanyStore(dbPath)) { }
            Assert.Equal((long)Schema.CurrentVersion, ReadSchemaVersion(dbPath));
        }
        finally { Delete(dbPath); }
    }

    [Fact]
    [Trait("Category", "RoundTrip")]
    public void Save_load_round_trips_the_esi_config_employee_and_pay_head_fields()
    {
        var dbPath = TempDb("apex-esi-roundtrip");
        try
        {
            var c = CompanyFactory.CreateSeeded("ESI DB Co", new DateOnly(2025, 4, 1), new DateOnly(2025, 4, 1));
            var pay = new PayrollService(c);
            pay.EnablePayroll();
            pay.EnableEsi(employeeRateBasisPoints: 75, employerRateBasisPoints: 325, employerCode: "12345678901234567");
            var ph = new PayHeadService(c);
            var liab = c.FindGroupByName("Current Liabilities")!.Id;
            var indirect = c.FindGroupByName("Indirect Expenses")!.Id;
            ph.CreatePayHead("Basic", PayHeadType.Earnings, PayHeadCalculationType.FlatRate, underGroupId: indirect, partOfEsiWages: true);
            ph.CreatePayHead("Overtime", PayHeadType.Earnings, PayHeadCalculationType.FlatRate, underGroupId: indirect,
                partOfEsiWages: true, isOvertime: true);
            ph.CreatePayHead("Employee ESI", PayHeadType.EmployeesStatutoryDeductions, PayHeadCalculationType.AsUserDefinedValue,
                underGroupId: liab, esiComponent: EsiStatutoryComponent.EmployeeStateInsurance);
            var e = pay.CreateEmployee("Sanjay Kumar", pay.CreateEmployeeGroup("Staff").Id, esiNumber: "3100123456");
            pay.SetEmployeeEsiDetails(e.Id, applicable: true, personWithDisability: true);

            using (var store = new SqliteCompanyStore(dbPath)) store.Save(c);

            Company reloaded;
            using (var store = new SqliteCompanyStore(dbPath)) reloaded = store.Load(c.Id)!;

            var esi = reloaded.EsiConfig!;
            Assert.NotNull(esi);
            Assert.Equal(75, esi.EmployeeRateBasisPoints);
            Assert.Equal(325, esi.EmployerRateBasisPoints);
            Assert.Equal("12345678901234567", esi.EmployerCode);

            var re = reloaded.Employees.Single();
            Assert.True(re.EsiApplicable);
            Assert.True(re.IsPersonWithDisability); // person-with-disability flag survives the round-trip (F1)
            Assert.Equal("3100123456", re.EsiNumber);

            Assert.True(reloaded.FindPayHeadByName("Basic")!.PartOfEsiWages);
            Assert.False(reloaded.FindPayHeadByName("Basic")!.IsOvertime);
            Assert.True(reloaded.FindPayHeadByName("Overtime")!.IsOvertime);
            Assert.Equal(EsiStatutoryComponent.EmployeeStateInsurance, reloaded.FindPayHeadByName("Employee ESI")!.EsiComponent);
        }
        finally { Delete(dbPath); }
    }

    [Fact]
    public void CreateV1_documents_esi_number_as_the_10_digit_ip_number_not_17_digits()
    {
        // F4: the fresh-DB employees block described esi_number as the "17-digit ESI number", contradicting the v34
        // correction (it is the per-employee 10-digit IP number; the 17-digit value is the establishment code).
        Assert.Contains("10-digit ESI IP number", Schema.CreateV1);
        Assert.DoesNotContain("17-digit ESI number", Schema.CreateV1);
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

    /// <summary>A minimal pre-v34 (v33) DDL: enough for the v33→v34 migration (which ALTERs <c>companies</c>,
    /// <c>employees</c> and <c>pay_heads</c>) plus a data-preservation assertion. Kept in the test so it never
    /// drifts as the production schema advances.</summary>
    private const string MinimalV33Ddl = """
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
        -- voucher_inventory_lines is required because the chain now runs through the v45 -> v46 item-invoice
        -- line-unit migration, whose ALTER TABLE voucher_inventory_lines ADD COLUMN unit_id needs the table to
        -- exist. A real database of this vintage always has it (created at v12); this fixture is a minimal
        -- hand-written subset, so the table is declared here for the ALTER to land on.
        CREATE TABLE voucher_inventory_lines (id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT, voucher_id TEXT NOT NULL, line_order INTEGER NOT NULL DEFAULT 0, stock_item_id TEXT NOT NULL DEFAULT '', godown_id TEXT NOT NULL DEFAULT '', quantity_micro INTEGER NOT NULL DEFAULT 0, direction INTEGER NOT NULL DEFAULT 0, rate_paisa INTEGER NOT NULL DEFAULT 0);
        """;
}
