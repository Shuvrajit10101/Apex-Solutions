using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Services;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Apex.Persistence.Sqlite.Tests;

/// <summary>
/// The Phase-8 slice-6 <b>Professional Tax</b> schema contract (v34→v35; ER-13). The bump is additive: four
/// <c>ALTER TABLE companies</c> columns for the establishment PT config, one <c>ALTER TABLE pay_heads</c> column for
/// the PT statutory role, and one new <c>pt_slab_bands</c> table (+ index) for the editable per-state slab bands.
/// Covers: a fresh DB stamps to <see cref="Schema.CurrentVersion"/> with the new columns/table; a legacy v34 DB
/// auto-migrates forward preserving every row (the new columns default off — ER-13); and a full save/load round-trip
/// preserves the PT config, its slab tables (incl. the February override) and the pay-head PT tag.
/// </summary>
public sealed class PtSchemaTests
{
    private const string MH = "27";

    [Fact]
    [Trait("Category", "RoundTrip")]
    public void Fresh_database_stamps_to_current_version_with_pt_columns_and_table()
    {
        var dbPath = TempDb("apex-pt-fresh");
        try
        {
            using (new SqliteCompanyStore(dbPath)) { }

            Assert.True(Schema.CurrentVersion >= 35);
            Assert.Equal((long)Schema.CurrentVersion, ReadSchemaVersion(dbPath));

            foreach (var col in new[] { "pt_config_enabled", "pt_state", "pt_registration_number", "pt_wage_basis" })
                Assert.Contains(col, ColumnNames(dbPath, "companies"));
            Assert.Contains("pt_component", ColumnNames(dbPath, "pay_heads"));
            foreach (var col in new[] { "id", "company_id", "slab_id", "state_code", "gender_scope", "band_order",
                                        "from_wage_paisa", "to_wage_paisa", "monthly_amount_paisa", "month_overrides" })
                Assert.Contains(col, ColumnNames(dbPath, "pt_slab_bands"));
        }
        finally { Delete(dbPath); }
    }

    [Fact]
    [Trait("Category", "RoundTrip")]
    public void Legacy_v34_database_auto_migrates_to_v35_preserving_every_row()
    {
        var dbPath = TempDb("apex-pt-v34legacy");
        try
        {
            var company = Guid.NewGuid();
            var connStr = new SqliteConnectionStringBuilder { DataSource = dbPath, Mode = SqliteOpenMode.ReadWriteCreate }.ToString();
            using (var conn = new SqliteConnection(connStr))
            {
                conn.Open();
                Exec(conn, MinimalV34Ddl);
                Exec(conn, "INSERT INTO schema_version(version) VALUES (34);");
                Exec(conn, "INSERT INTO companies(id, name) VALUES ($id, 'Legacy V34 Co');", ("$id", company.ToString("D")));
                Exec(conn, "INSERT INTO pay_heads(id, company_id, name) VALUES ($id, $cid, 'Basic');",
                    ("$id", Guid.NewGuid().ToString("D")), ("$cid", company.ToString("D")));
                SqliteConnection.ClearPool(conn);
            }

            Assert.Equal(34L, ReadSchemaVersion(dbPath));
            Assert.DoesNotContain("pt_config_enabled", ColumnNames(dbPath, "companies"));
            Assert.DoesNotContain("pt_component", ColumnNames(dbPath, "pay_heads"));

            using (new SqliteCompanyStore(dbPath)) { } // opens v34 → migrates to the current version

            Assert.Equal((long)Schema.CurrentVersion, ReadSchemaVersion(dbPath));
            Assert.Contains("pt_config_enabled", ColumnNames(dbPath, "companies"));
            Assert.Contains("pt_component", ColumnNames(dbPath, "pay_heads"));

            // Every existing row survived (ER-13); the new flags default off, pt_slab_bands starts empty.
            Assert.Equal("Legacy V34 Co", ReadScalarStr(dbPath, "SELECT name FROM companies LIMIT 1;"));
            Assert.Equal(0L, ReadScalar(dbPath, "SELECT pt_config_enabled FROM companies LIMIT 1;"));
            Assert.True(IsDbNull(dbPath, "SELECT pt_state FROM companies LIMIT 1;"));
            Assert.Equal(0L, ReadScalar(dbPath, "SELECT pt_wage_basis FROM companies LIMIT 1;"));
            Assert.Equal(0L, ReadScalar(dbPath, "SELECT pt_component FROM pay_heads LIMIT 1;"));
            Assert.Equal(0L, ReadScalar(dbPath, "SELECT COUNT(*) FROM pt_slab_bands;"));

            using (new SqliteCompanyStore(dbPath)) { }
            Assert.Equal((long)Schema.CurrentVersion, ReadSchemaVersion(dbPath));
        }
        finally { Delete(dbPath); }
    }

    [Fact]
    [Trait("Category", "RoundTrip")]
    public void Save_load_round_trips_the_pt_config_slab_tables_and_pay_head_tag()
    {
        var dbPath = TempDb("apex-pt-roundtrip");
        try
        {
            var c = CompanyFactory.CreateSeeded("PT DB Co", new DateOnly(2025, 4, 1), new DateOnly(2025, 4, 1));
            var pay = new PayrollService(c);
            pay.EnablePayroll();
            pay.EnableProfessionalTax(stateCode: MH, registrationNumber: "27999999999P");
            var ph = new PayHeadService(c);
            var liab = c.FindGroupByName("Current Liabilities")!.Id;
            ph.CreatePayHead("Professional Tax", PayHeadType.EmployeesStatutoryDeductions, PayHeadCalculationType.AsUserDefinedValue,
                underGroupId: liab, ptComponent: PtStatutoryComponent.ProfessionalTax);

            using (var store = new SqliteCompanyStore(dbPath)) store.Save(c);

            Company reloaded;
            using (var store = new SqliteCompanyStore(dbPath)) reloaded = store.Load(c.Id)!;

            var pt = reloaded.PtConfig!;
            Assert.NotNull(pt);
            Assert.Equal(MH, pt.StateCode);
            Assert.Equal("27999999999P", pt.RegistrationNumber);
            Assert.Equal(PtWageBasis.GrossEarnings, pt.WageBasis);

            // The seeded slab tables survive with their bands + the February over-charge.
            Assert.Equal(4, pt.SlabTables.Count);
            var mhMale = pt.SlabTables.Single(s => s.StateCode == MH && s.GenderScope == PtGenderScope.Male);
            Assert.Equal(3, mhMale.Bands.Count);
            var top = mhMale.Bands.Last();
            Assert.Equal(new Money(200m), top.MonthlyAmount);
            Assert.Null(top.ToWage); // open-ended top band survives (NULL to_wage)
            var feb = Assert.Single(top.MonthOverrides);
            Assert.Equal(2, feb.Month);
            Assert.Equal(new Money(300m), feb.Amount);

            // The active slab still computes MH man ₹12,000 → ₹200 (₹300 Feb).
            var slab = pt.ResolveSlab("Male")!;
            Assert.Equal(new Money(200m), ProfessionalTax.MonthlyBeforeCap(slab, 12000m, 4));
            Assert.Equal(new Money(300m), ProfessionalTax.MonthlyBeforeCap(slab, 12000m, 2));

            Assert.Equal(PtStatutoryComponent.ProfessionalTax, reloaded.FindPayHeadByName("Professional Tax")!.PtComponent);
        }
        finally { Delete(dbPath); }
    }

    [Fact]
    [Trait("Category", "RoundTrip")]
    public void Pt_slab_bands_reload_in_band_order_not_rowid_order()
    {
        // F2: the read must honour the WRITTEN band_order, not rowid. Save, then reverse the West-Bengal bands'
        // band_order in place (rowid unchanged) so band_order order ≠ rowid order; a correct read returns them by
        // band_order (₹200 top band first), a rowid read returns the original ₹0…₹200 order.
        var dbPath = TempDb("apex-pt-bandorder");
        try
        {
            var c = CompanyFactory.CreateSeeded("PT Order Co", new DateOnly(2025, 4, 1), new DateOnly(2025, 4, 1));
            var pay = new PayrollService(c);
            pay.EnablePayroll();
            pay.EnableProfessionalTax(stateCode: MH);
            using (var store = new SqliteCompanyStore(dbPath)) store.Save(c);

            using (var conn = Open(dbPath))
            {
                // West Bengal ("19") has five bands (band_order 0..4); 4 − band_order reverses them without moving rowid.
                Exec(conn, "UPDATE pt_slab_bands SET band_order = 4 - band_order WHERE company_id = $cid AND state_code = '19';",
                    ("$cid", c.Id.ToString("D")));
                SqliteConnection.ClearPool(conn);
            }

            Company reloaded;
            using (var store = new SqliteCompanyStore(dbPath)) reloaded = store.Load(c.Id)!;

            var wb = reloaded.PtConfig!.SlabTables.Single(s => s.StateCode == "19");
            Assert.Equal(5, wb.Bands.Count);
            Assert.Equal(new Money(200m), wb.Bands[0].MonthlyAmount);   // band_order 0 after reversal (was the ₹200 top band)
            Assert.Equal(new Money(0m), wb.Bands[^1].MonthlyAmount);    // band_order 4 after reversal (was the ₹0 Nil band)
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

    /// <summary>A minimal pre-v35 (v34) DDL: enough for the v34→v35 migration (which ALTERs <c>companies</c> and
    /// <c>pay_heads</c> and creates <c>pt_slab_bands</c>) plus a data-preservation assertion. Kept in the test so it
    /// never drifts as the production schema advances.</summary>
    private const string MinimalV34Ddl = """
        CREATE TABLE schema_version (version INTEGER NOT NULL);
        CREATE TABLE companies (id TEXT NOT NULL PRIMARY KEY, name TEXT NOT NULL);
        CREATE TABLE stock_items (id TEXT NOT NULL PRIMARY KEY, name TEXT NOT NULL);
        CREATE TABLE ledgers (id TEXT NOT NULL PRIMARY KEY, company_id TEXT NOT NULL, name TEXT NOT NULL);
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
