using Apex.Ledger.Io;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Apex.Persistence.Sqlite.Tests;

/// <summary>
/// The SMTP-profile persistence contract (RQ-27; R13). The profile is capture-only — host/port/TLS/from —
/// stored one-per-company, never a password. This covers: a fresh DB stamps to v15 and has <c>smtp_profile</c>;
/// a legacy v14 DB auto-migrates to v15 gaining <c>smtp_profile</c> with no data loss; save→get→delete
/// round-trips; save upserts (one row per company); per-company isolation; and — critically — the
/// <c>smtp_profile</c> schema has NO password/secret column.
/// </summary>
public sealed class SmtpProfileRoundTripTests
{
    private static SmtpProfile SampleProfile() => new()
    {
        Host = "smtp.apexco.example",
        Port = 587,
        UseTls = true,
        FromAddress = "billing@apexco.example",
        FromName = "Apex Solutions Billing",
    };

    [Fact]
    [Trait("Category", "RoundTrip")]
    public void Fresh_database_stamps_to_v15_with_smtp_profile_table()
    {
        var dbPath = TempDb("apex-smtp-fresh");
        try
        {
            using (new SqliteCompanyStore(dbPath)) { }
            Assert.True(Schema.CurrentVersion >= 15);                     // SMTP profile landed at v15
            Assert.Equal((long)Schema.CurrentVersion, ReadSchemaVersion(dbPath));
            Assert.True(TableExists(dbPath, "smtp_profile"));
        }
        finally { Delete(dbPath); }
    }

    [Fact]
    [Trait("Category", "RoundTrip")]
    public void Save_get_delete_round_trip()
    {
        var dbPath = TempDb("apex-smtp-crud");
        try
        {
            var companyId = Guid.NewGuid();
            var profile = SampleProfile();
            using var store = new SqliteCompanyStore(dbPath);

            Assert.Null(store.GetSmtpProfile(companyId)); // nothing saved yet

            store.SaveSmtpProfile(companyId, profile);

            var got = store.GetSmtpProfile(companyId);
            Assert.NotNull(got);
            Assert.Equal(profile, got);                    // identical round-trip

            store.DeleteSmtpProfile(companyId);
            Assert.Null(store.GetSmtpProfile(companyId));
        }
        finally { Delete(dbPath); }
    }

    [Fact]
    [Trait("Category", "RoundTrip")]
    public void Save_upserts_one_profile_per_company()
    {
        var dbPath = TempDb("apex-smtp-upsert");
        try
        {
            var companyId = Guid.NewGuid();
            using var store = new SqliteCompanyStore(dbPath);

            store.SaveSmtpProfile(companyId, SampleProfile());
            store.SaveSmtpProfile(companyId, SampleProfile() with { Host = "smtp2.apexco.example", Port = 465 });

            Assert.Equal(1L, CountRows(dbPath, "smtp_profile"));                  // still exactly one row
            var got = store.GetSmtpProfile(companyId)!;
            Assert.Equal("smtp2.apexco.example", got.Host);
            Assert.Equal(465, got.Port);
        }
        finally { Delete(dbPath); }
    }

    [Fact]
    [Trait("Category", "RoundTrip")]
    public void Optional_from_name_round_trips_as_null()
    {
        var dbPath = TempDb("apex-smtp-nullname");
        try
        {
            var companyId = Guid.NewGuid();
            using var store = new SqliteCompanyStore(dbPath);

            store.SaveSmtpProfile(companyId, SampleProfile() with { FromName = null });
            var got = store.GetSmtpProfile(companyId)!;
            Assert.Null(got.FromName);
            Assert.False(got.UseTls == false); // still true from the sample
        }
        finally { Delete(dbPath); }
    }

    [Fact]
    [Trait("Category", "RoundTrip")]
    public void Profiles_are_isolated_per_company()
    {
        var dbPath = TempDb("apex-smtp-isolation");
        try
        {
            var companyA = Guid.NewGuid();
            var companyB = Guid.NewGuid();
            using var store = new SqliteCompanyStore(dbPath);

            store.SaveSmtpProfile(companyA, SampleProfile() with { Host = "a.example" });
            Assert.Null(store.GetSmtpProfile(companyB));                          // A's profile is invisible to B

            store.SaveSmtpProfile(companyB, SampleProfile() with { Host = "b.example" });
            Assert.Equal("a.example", store.GetSmtpProfile(companyA)!.Host);
            Assert.Equal("b.example", store.GetSmtpProfile(companyB)!.Host);
        }
        finally { Delete(dbPath); }
    }

    [Fact]
    [Trait("Category", "RoundTrip")]
    public void Smtp_profile_table_has_no_password_or_secret_column()
    {
        var dbPath = TempDb("apex-smtp-nopwd");
        try
        {
            using (new SqliteCompanyStore(dbPath)) { }
            foreach (var col in ColumnNames(dbPath, "smtp_profile"))
            {
                var lower = col.ToLowerInvariant();
                Assert.DoesNotContain("password", lower);
                Assert.DoesNotContain("secret", lower);
                Assert.DoesNotContain("pwd", lower);
                Assert.DoesNotContain("credential", lower);
            }
        }
        finally { Delete(dbPath); }
    }

    [Fact]
    [Trait("Category", "RoundTrip")]
    public void Legacy_v14_database_auto_migrates_to_v15_gaining_smtp_profile_no_data_loss()
    {
        var dbPath = TempDb("apex-smtp-v14legacy");
        try
        {
            var companyId = Guid.NewGuid();
            var savedViewCompany = Guid.NewGuid();
            var connStr = new SqliteConnectionStringBuilder { DataSource = dbPath, Mode = SqliteOpenMode.ReadWriteCreate }.ToString();
            using (var conn = new SqliteConnection(connStr))
            {
                conn.Open();
                Exec(conn, MinimalV14Ddl);
                Exec(conn, "INSERT INTO schema_version(version) VALUES (14);");
                Exec(conn, "INSERT INTO companies(id, name) VALUES ($id, 'Legacy V14 Co');",
                    ("$id", companyId.ToString("D")));
                // A pre-existing saved view must survive the v14→v15 CREATE-only migration untouched.
                Exec(conn, "INSERT INTO saved_views(id, company_id, name, config_json) VALUES ($id, $cid, 'Keep', '{}');",
                    ("$id", Guid.NewGuid().ToString("D")), ("$cid", savedViewCompany.ToString("D")));
                SqliteConnection.ClearPool(conn);
            }
            Assert.Equal(14L, ReadSchemaVersion(dbPath));
            Assert.False(TableExists(dbPath, "smtp_profile"));

            using (var store = new SqliteCompanyStore(dbPath)) // opens v14 → migrates forward (>= v15)
            {
                Assert.Equal((long)Schema.CurrentVersion, ReadSchemaVersion(dbPath));
                Assert.True(TableExists(dbPath, "smtp_profile"));
                // Existing v14 data survived the pure CREATE-only migration.
                Assert.Equal("Legacy V14 Co", ReadCompanyName(dbPath, companyId));
                Assert.Equal(1L, CountRows(dbPath, "saved_views"));
                // And the migrated DB now accepts and round-trips an SMTP profile.
                store.SaveSmtpProfile(companyId, SampleProfile());
                Assert.Equal(SampleProfile(), store.GetSmtpProfile(companyId));
            }
        }
        finally { Delete(dbPath); }
    }

    // ---- helpers ----

    private static string TempDb(string prefix) => Path.Combine(Path.GetTempPath(), $"{prefix}-{Guid.NewGuid():N}.db");
    private static void Delete(string dbPath) => TempDbFile.Delete(dbPath);

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
        cmd.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name=$t;";
        cmd.Parameters.AddWithValue("$t", table);
        var exists = Convert.ToInt64(cmd.ExecuteScalar()) > 0;
        SqliteConnection.ClearPool(conn);
        return exists;
    }

    private static long CountRows(string dbPath, string table)
    {
        using var conn = Open(dbPath);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT COUNT(*) FROM {table};";
        var n = Convert.ToInt64(cmd.ExecuteScalar());
        SqliteConnection.ClearPool(conn);
        return n;
    }

    private static IReadOnlyList<string> ColumnNames(string dbPath, string table)
    {
        using var conn = Open(dbPath);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"PRAGMA table_info({table});";
        var names = new List<string>();
        using (var r = cmd.ExecuteReader())
            while (r.Read())
                names.Add(r.GetString(1)); // column 1 = name
        SqliteConnection.ClearPool(conn);
        return names;
    }

    private static string ReadCompanyName(string dbPath, Guid companyId)
    {
        using var conn = Open(dbPath);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT name FROM companies WHERE id = $id;";
        cmd.Parameters.AddWithValue("$id", companyId.ToString("D"));
        var name = (string)cmd.ExecuteScalar()!;
        SqliteConnection.ClearPool(conn);
        return name;
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

    /// <summary>
    /// A minimal pre-v15 (v14) DDL: enough of the schema for the v14→v15 migration (which only CREATEs the
    /// <c>smtp_profile</c> table) and a data-preservation assertion, including the v14 <c>saved_views</c> table
    /// so we prove that row survives. Because opening the store migrates all the way to
    /// <see cref="Schema.CurrentVersion"/> (past v15), the four v16-touched stock-line tables
    /// (<c>stock_opening_balances</c>, <c>inventory_allocations</c>, <c>physical_stock_lines</c>,
    /// <c>voucher_inventory_lines</c>) are included in their pre-v16 shape so the v15→v16 ADD-COLUMN steps have
    /// real targets. Kept in the test so it never drifts as the production schema advances.
    /// </summary>
    private const string MinimalV14Ddl = """
        CREATE TABLE schema_version (version INTEGER NOT NULL);
        CREATE TABLE companies (
            id   TEXT NOT NULL PRIMARY KEY,
            name TEXT NOT NULL
        );
        CREATE TABLE saved_views (
            id           TEXT NOT NULL PRIMARY KEY,
            company_id   TEXT NOT NULL,
            name         TEXT NOT NULL,
            config_json  TEXT NOT NULL
        );
        CREATE TABLE stock_opening_balances (
            id TEXT NOT NULL PRIMARY KEY, company_id TEXT NOT NULL, stock_item_id TEXT NOT NULL,
            godown_id TEXT NOT NULL, batch_label TEXT NULL, quantity_micro INTEGER NOT NULL,
            rate_paisa INTEGER NOT NULL, mfg_date TEXT NULL, expiry_date TEXT NULL);
        CREATE TABLE inventory_allocations (
            id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT, inventory_voucher_id TEXT NOT NULL,
            line_order INTEGER NOT NULL, role INTEGER NOT NULL, stock_item_id TEXT NOT NULL,
            godown_id TEXT NOT NULL, unit_id TEXT NULL, quantity_micro INTEGER NOT NULL,
            direction INTEGER NOT NULL, rate_paisa INTEGER NULL, batch_label TEXT NULL);
        CREATE TABLE physical_stock_lines (
            id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT, inventory_voucher_id TEXT NOT NULL,
            line_order INTEGER NOT NULL, stock_item_id TEXT NOT NULL, godown_id TEXT NOT NULL,
            counted_qty_micro INTEGER NOT NULL, batch_label TEXT NULL);
        CREATE TABLE voucher_inventory_lines (
            id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT, voucher_id TEXT NOT NULL, line_order INTEGER NOT NULL,
            stock_item_id TEXT NOT NULL, godown_id TEXT NOT NULL, quantity_micro INTEGER NOT NULL,
            direction INTEGER NOT NULL, rate_paisa INTEGER NOT NULL, batch_label TEXT NULL);
        CREATE TABLE stock_items (
            id TEXT NOT NULL PRIMARY KEY, company_id TEXT NOT NULL, name TEXT NOT NULL,
            stock_group_id TEXT NOT NULL, category_id TEXT NULL, base_unit_id TEXT NOT NULL, alias TEXT NULL,
            valuation_method INTEGER NOT NULL DEFAULT 0, hsn_sac_code TEXT NULL, is_taxable INTEGER NOT NULL DEFAULT 0,
            reorder_level_micro INTEGER NULL, min_order_qty_micro INTEGER NULL, standard_cost_paisa INTEGER NULL,
            gst_hsn_sac TEXT NULL, gst_taxability INTEGER NULL, gst_rate_bp INTEGER NULL, gst_supply_type INTEGER NULL);
        """;
}
