using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Services;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Apex.Persistence.Sqlite.Tests;

/// <summary>
/// The Phase-8 slice-9 <b>Gratuity + statutory-Bonus</b> schema contract (v36→v37; ER-13). The bump is additive: nine
/// <c>ALTER TABLE companies</c> columns (four gratuity, five bonus) and <b>no new tables</b>. Covers: a fresh DB stamps
/// to <see cref="Schema.CurrentVersion"/> with the new columns; a legacy v36 DB auto-migrates forward preserving every
/// row (the new flags default off — ER-13); and a save/load round-trip preserves both configs' values (money
/// paisa-exact).
/// </summary>
public sealed class GratuityBonusSchemaTests
{
    [Fact]
    [Trait("Category", "RoundTrip")]
    public void Fresh_database_stamps_to_current_version_with_gratuity_and_bonus_columns()
    {
        var dbPath = TempDb("apex-gratbonus-fresh");
        try
        {
            using (new SqliteCompanyStore(dbPath)) { }

            Assert.True(Schema.CurrentVersion >= 37);
            Assert.Equal((long)Schema.CurrentVersion, ReadSchemaVersion(dbPath));

            foreach (var col in new[] { "gratuity_config_enabled", "gratuity_cap_paisa", "gratuity_wage_basis",
                                        "gratuity_population", "bonus_config_enabled", "bonus_rate_bp",
                                        "bonus_calc_ceiling_paisa", "bonus_minimum_wage_paisa", "bonus_prorate" })
                Assert.Contains(col, ColumnNames(dbPath, "companies"));
        }
        finally { Delete(dbPath); }
    }

    [Fact]
    [Trait("Category", "RoundTrip")]
    public void Legacy_v36_database_auto_migrates_to_v37_preserving_every_row()
    {
        var dbPath = TempDb("apex-gratbonus-v36legacy");
        try
        {
            var company = Guid.NewGuid();
            var connStr = new SqliteConnectionStringBuilder { DataSource = dbPath, Mode = SqliteOpenMode.ReadWriteCreate }.ToString();
            using (var conn = new SqliteConnection(connStr))
            {
                conn.Open();
                Exec(conn, MinimalV36Ddl);
                Exec(conn, "INSERT INTO schema_version(version) VALUES (36);");
                Exec(conn, "INSERT INTO companies(id, name) VALUES ($id, 'Legacy V36 Co');", ("$id", company.ToString("D")));
                SqliteConnection.ClearPool(conn);
            }

            Assert.Equal(36L, ReadSchemaVersion(dbPath));
            Assert.DoesNotContain("gratuity_config_enabled", ColumnNames(dbPath, "companies"));

            using (new SqliteCompanyStore(dbPath)) { } // opens v36 → migrates to the current version

            Assert.Equal((long)Schema.CurrentVersion, ReadSchemaVersion(dbPath));
            Assert.Contains("gratuity_config_enabled", ColumnNames(dbPath, "companies"));
            Assert.Contains("bonus_config_enabled", ColumnNames(dbPath, "companies"));

            // Every existing row survived (ER-13); the new flags default off, the money columns carry statutory defaults.
            Assert.Equal("Legacy V36 Co", ReadScalarStr(dbPath, "SELECT name FROM companies LIMIT 1;"));
            Assert.Equal(0L, ReadScalar(dbPath, "SELECT gratuity_config_enabled FROM companies LIMIT 1;"));
            Assert.Equal(0L, ReadScalar(dbPath, "SELECT bonus_config_enabled FROM companies LIMIT 1;"));
            Assert.Equal(200000000L, ReadScalar(dbPath, "SELECT gratuity_cap_paisa FROM companies LIMIT 1;"));
            Assert.Equal(833L, ReadScalar(dbPath, "SELECT bonus_rate_bp FROM companies LIMIT 1;"));
            Assert.Equal(700000L, ReadScalar(dbPath, "SELECT bonus_calc_ceiling_paisa FROM companies LIMIT 1;"));
            Assert.Equal(1L, ReadScalar(dbPath, "SELECT bonus_prorate FROM companies LIMIT 1;"));

            using (new SqliteCompanyStore(dbPath)) { }
            Assert.Equal((long)Schema.CurrentVersion, ReadSchemaVersion(dbPath));
        }
        finally { Delete(dbPath); }
    }

    [Fact]
    [Trait("Category", "RoundTrip")]
    public void Save_load_round_trips_the_gratuity_and_bonus_configs()
    {
        var dbPath = TempDb("apex-gratbonus-roundtrip");
        try
        {
            var c = CompanyFactory.CreateSeeded("Grat/Bonus DB Co", new DateOnly(2025, 4, 1), new DateOnly(2025, 4, 1));
            var pay = new PayrollService(c);
            pay.EnablePayroll();
            pay.EnableGratuity(cap: new Money(1_500_000m), population: GratuityProvisionPopulation.VestedOnly);
            pay.EnableStatutoryBonus(rateBasisPoints: 1500, calculationCeiling: new Money(8_000m),
                minimumWage: new Money(5_000m), prorate: false);

            using (var store = new SqliteCompanyStore(dbPath)) store.Save(c);

            Company reloaded;
            using (var store = new SqliteCompanyStore(dbPath)) reloaded = store.Load(c.Id)!;

            var g = reloaded.GratuityConfig!;
            Assert.NotNull(g);
            Assert.Equal(new Money(1_500_000m), g.CapAmount);
            Assert.Equal(GratuityWageBasis.BasicAndDearnessAllowance, g.WageBasis);
            Assert.Equal(GratuityProvisionPopulation.VestedOnly, g.Population);

            var b = reloaded.BonusConfig!;
            Assert.NotNull(b);
            Assert.Equal(1500, b.RateBasisPoints);
            Assert.Equal(new Money(8_000m), b.CalculationCeiling);
            Assert.Equal(new Money(5_000m), b.MinimumWage);
            Assert.False(b.Prorate);
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

    /// <summary>A minimal pre-v37 (v36) DDL: enough for the v36→v37 migration (which only ALTERs <c>companies</c>)
    /// plus a data-preservation assertion. Kept in the test so it never drifts as the production schema advances.</summary>
    private const string MinimalV36Ddl = """
        CREATE TABLE schema_version (version INTEGER NOT NULL);
        CREATE TABLE companies (id TEXT NOT NULL PRIMARY KEY, name TEXT NOT NULL);
        """;
}
