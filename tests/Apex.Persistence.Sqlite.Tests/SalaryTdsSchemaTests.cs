using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Services;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Apex.Persistence.Sqlite.Tests;

/// <summary>
/// The Phase-8 slice-7 <b>§192 salary-TDS</b> schema contract (v35→v36; ER-13). The bump is additive: one
/// <c>ALTER TABLE companies</c> column for the establishment salary-TDS toggle and one new
/// <c>employee_tax_declarations</c> table (+ index) for the per-employee Form-12BB declaration. Covers: a fresh DB
/// stamps to <see cref="Schema.CurrentVersion"/> with the new column/table; a legacy v35 DB auto-migrates forward
/// preserving every row (the new column defaults off — ER-13); and a save/load round-trip preserves the toggle and
/// a declaration's paisa-exact figures.
/// </summary>
public sealed class SalaryTdsSchemaTests
{
    [Fact]
    [Trait("Category", "RoundTrip")]
    public void Fresh_database_stamps_to_current_version_with_salary_tds_column_and_table()
    {
        var dbPath = TempDb("apex-tds-fresh");
        try
        {
            using (new SqliteCompanyStore(dbPath)) { }

            Assert.True(Schema.CurrentVersion >= 36);
            Assert.Equal((long)Schema.CurrentVersion, ReadSchemaVersion(dbPath));

            Assert.Contains("salary_tds_enabled", ColumnNames(dbPath, "companies"));
            foreach (var col in new[] { "employee_id", "company_id", "section_80c_paisa", "section_80d_paisa",
                                        "section_80ccd1b_paisa", "section_80ccd2_employer_paisa", "hra_exempt_paisa",
                                        "home_loan_interest_paisa", "other_income_paisa", "prev_employer_salary_paisa",
                                        "prev_employer_tds_paisa" })
                Assert.Contains(col, ColumnNames(dbPath, "employee_tax_declarations"));
        }
        finally { Delete(dbPath); }
    }

    [Fact]
    [Trait("Category", "RoundTrip")]
    public void Legacy_v35_database_auto_migrates_to_v36_preserving_every_row()
    {
        var dbPath = TempDb("apex-tds-v35legacy");
        try
        {
            var company = Guid.NewGuid();
            var connStr = new SqliteConnectionStringBuilder { DataSource = dbPath, Mode = SqliteOpenMode.ReadWriteCreate }.ToString();
            using (var conn = new SqliteConnection(connStr))
            {
                conn.Open();
                Exec(conn, MinimalV35Ddl);
                Exec(conn, "INSERT INTO schema_version(version) VALUES (35);");
                Exec(conn, "INSERT INTO companies(id, name) VALUES ($id, 'Legacy V35 Co');", ("$id", company.ToString("D")));
                SqliteConnection.ClearPool(conn);
            }

            Assert.Equal(35L, ReadSchemaVersion(dbPath));
            Assert.DoesNotContain("salary_tds_enabled", ColumnNames(dbPath, "companies"));

            using (new SqliteCompanyStore(dbPath)) { } // opens v35 → migrates to the current version

            Assert.Equal((long)Schema.CurrentVersion, ReadSchemaVersion(dbPath));
            Assert.Contains("salary_tds_enabled", ColumnNames(dbPath, "companies"));

            // Every existing row survived (ER-13); the new toggle defaults off, employee_tax_declarations starts empty.
            Assert.Equal("Legacy V35 Co", ReadScalarStr(dbPath, "SELECT name FROM companies LIMIT 1;"));
            Assert.Equal(0L, ReadScalar(dbPath, "SELECT salary_tds_enabled FROM companies LIMIT 1;"));
            Assert.Equal(0L, ReadScalar(dbPath, "SELECT COUNT(*) FROM employee_tax_declarations;"));

            using (new SqliteCompanyStore(dbPath)) { }
            Assert.Equal((long)Schema.CurrentVersion, ReadSchemaVersion(dbPath));
        }
        finally { Delete(dbPath); }
    }

    [Fact]
    [Trait("Category", "RoundTrip")]
    public void Save_load_round_trips_the_salary_tds_toggle_and_a_tax_declaration()
    {
        var dbPath = TempDb("apex-tds-roundtrip");
        try
        {
            var c = CompanyFactory.CreateSeeded("TDS DB Co", new DateOnly(2025, 4, 1), new DateOnly(2025, 4, 1));
            var pay = new PayrollService(c);
            pay.EnablePayroll();
            pay.EnableSalaryTds();
            var e = pay.CreateEmployee("Anita Rao", pay.CreateEmployeeGroup("Staff").Id);
            c.FindEmployee(e.Id)!.ApplicableTaxRegime = TaxRegime.Old;
            c.AddTaxDeclaration(new TaxDeclaration
            {
                EmployeeId = e.Id,
                Section80C = new Money(150_000m),
                Section80D = new Money(25_000m),
                HomeLoanInterest24b = new Money(200_000m),
                PreviousEmployerSalary = new Money(300_000m),
                PreviousEmployerTds = new Money(12_345m),
            });

            using (var store = new SqliteCompanyStore(dbPath)) store.Save(c);

            Company reloaded;
            using (var store = new SqliteCompanyStore(dbPath)) reloaded = store.Load(c.Id)!;

            Assert.True(reloaded.SalaryTdsEnabled);
            var employee = reloaded.FindEmployeeByName("Anita Rao")!;
            var d = reloaded.FindTaxDeclaration(employee.Id)!;
            Assert.NotNull(d);
            Assert.Equal(new Money(150_000m), d.Section80C);
            Assert.Equal(new Money(25_000m), d.Section80D);
            Assert.Equal(new Money(200_000m), d.HomeLoanInterest24b);
            Assert.Equal(new Money(300_000m), d.PreviousEmployerSalary);
            Assert.Equal(new Money(12_345m), d.PreviousEmployerTds);
            // The declaration drives the old-regime allowed deductions after reload.
            Assert.Equal(new Money(375_000m), d.AllowedDeductions(TaxRegime.Old)); // 1.5L + 25k + 2L(24b)
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

    /// <summary>A minimal pre-v36 (v35) DDL: enough for the v35→v36 migration (which ALTERs <c>companies</c> and
    /// creates <c>employee_tax_declarations</c>) plus a data-preservation assertion. Kept in the test so it never
    /// drifts as the production schema advances.</summary>
    private const string MinimalV35Ddl = """
        CREATE TABLE schema_version (version INTEGER NOT NULL);
        CREATE TABLE companies (id TEXT NOT NULL PRIMARY KEY, name TEXT NOT NULL);
        """;
}
