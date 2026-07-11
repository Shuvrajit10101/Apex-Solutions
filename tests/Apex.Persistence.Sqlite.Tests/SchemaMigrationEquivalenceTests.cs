using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Apex.Persistence.Sqlite.Tests;

/// <summary>
/// Migration-equivalence contract (S9 hardening). A database created at the <b>oldest supported version</b>
/// (v1 — the Phase-1 accounting core) and migrated all the way up to <see cref="Schema.CurrentVersion"/> through
/// the normal <see cref="SqliteCompanyStore"/> <c>EnsureSchema</c> path MUST end with a schema byte-for-byte
/// equivalent to a database stamped straight to the current version by <see cref="Schema.CreateV1"/>: the same set
/// of tables, the same columns on each table (name / declared type / NOT NULL / default / primary-key flag, via
/// <c>PRAGMA table_info</c>), and the same named indexes (via <c>sqlite_master</c>). This catches the classic
/// divergence bug where a table, column or index lands in <see cref="Schema.CreateV1"/> but is forgotten in a
/// <c>MigrateVNToVN+1</c> constant — or the reverse (present in a migration, missing from <see cref="Schema.CreateV1"/>).
///
/// <para>The comparison is <b>order-independent</b>: columns and indexes are folded into sorted dictionaries, so
/// the fact that migrations append columns via <c>ALTER TABLE ADD COLUMN</c> (a different physical column order than
/// a single <c>CREATE TABLE</c>) is irrelevant — only the schema's <i>content</i> is compared. Index SQL is
/// whitespace-normalised so cosmetic spacing differences between <see cref="Schema.CreateV1"/> and the migration
/// scripts do not register as a divergence.</para>
///
/// <para>v1 is not reachable through production code (<see cref="Schema.CreateV1"/> always stamps a fresh database
/// straight to the current version — there is no "create v1 only" path), so the frozen v1 baseline schema is
/// reproduced verbatim in <see cref="BaselineV1Ddl"/> below: exactly the seven v1 tables and their five core
/// indexes, with every column definition copied from the current create DDL minus the columns that later
/// migrations add. Starting from v1 exercises the ENTIRE migration chain (v1→…→v29), giving the widest possible
/// divergence coverage.</para>
/// </summary>
public sealed class SchemaMigrationEquivalenceTests
{
    [Fact]
    [Trait("Category", "RoundTrip")]
    public void Migrating_the_oldest_version_up_reproduces_a_fresh_current_schema_exactly()
    {
        var migratedPath = TempDbFile.NewPath("apex-schema-migrated-from-v1");
        var freshPath = TempDbFile.NewPath("apex-schema-fresh-current");
        try
        {
            // (1) Build a genuine v1 database from the frozen baseline DDL, stamp it at version 1, then open it
            //     through the production store so the full v1 → … → CurrentVersion migration chain runs.
            var connStr = new SqliteConnectionStringBuilder
            {
                DataSource = migratedPath,
                Mode = SqliteOpenMode.ReadWriteCreate,
            }.ToString();
            using (var conn = new SqliteConnection(connStr))
            {
                conn.Open();
                Exec(conn, BaselineV1Ddl);
                Exec(conn, "INSERT INTO schema_version(version) VALUES (1);");
                SqliteConnection.ClearPool(conn);
            }
            Assert.Equal(1L, ReadSchemaVersion(migratedPath));

            using (new SqliteCompanyStore(migratedPath)) { }  // v1 → CurrentVersion (every migration runs)
            Assert.Equal((long)Schema.CurrentVersion, ReadSchemaVersion(migratedPath));

            // (2) A fresh database stamped straight to CurrentVersion via CreateV1.
            using (new SqliteCompanyStore(freshPath)) { }
            Assert.Equal((long)Schema.CurrentVersion, ReadSchemaVersion(freshPath));

            // (3) The two schemas must be identical, compared order-independently.
            var migrated = SnapshotSchema(migratedPath);
            var fresh = SnapshotSchema(freshPath);

            // Same set of tables.
            Assert.Equal(fresh.Tables.Keys, migrated.Tables.Keys);
            // Same columns on every table (name/type/notnull/default/pk).
            foreach (var table in fresh.Tables.Keys)
                Assert.Equal(fresh.Tables[table], migrated.Tables[table]);

            // Same set of named indexes, each with the same target table + (normalised) definition.
            Assert.Equal(fresh.Indexes.Keys, migrated.Indexes.Keys);
            foreach (var index in fresh.Indexes.Keys)
                Assert.Equal(fresh.Indexes[index], migrated.Indexes[index]);

            // Belt-and-suspenders: the fully-rendered canonical schema strings match exactly.
            Assert.Equal(fresh.Canonical, migrated.Canonical);
        }
        finally
        {
            TempDbFile.Delete(migratedPath);
            TempDbFile.Delete(freshPath);
        }
    }

    // ---- schema snapshot ----

    private sealed record SchemaSnapshot(
        SortedDictionary<string, string> Tables,
        SortedDictionary<string, string> Indexes,
        string Canonical);

    /// <summary>
    /// Captures a database's schema as order-independent structures: table name → sorted per-column contract
    /// (name/type/notnull/default/pk from <c>PRAGMA table_info</c>), and named index name → "table|normalised-SQL"
    /// from <c>sqlite_master</c>. Internal <c>sqlite_*</c> objects (autoindexes, the AUTOINCREMENT sequence table)
    /// are excluded — they are a deterministic function of the table definitions, which are already compared.
    /// </summary>
    private static SchemaSnapshot SnapshotSchema(string dbPath)
    {
        using var conn = Open(dbPath);

        var tables = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var table in QueryStrings(conn,
            "SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%' ORDER BY name;"))
        {
            var cols = new List<string>();
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = $"PRAGMA table_info(\"{table}\");";
                using var r = cmd.ExecuteReader();
                while (r.Read())
                {
                    var name = r.GetString(1);
                    var type = r.GetString(2);
                    var notNull = r.GetInt64(3);
                    var dflt = r.IsDBNull(4) ? "<null>" : r.GetString(4);
                    var pk = r.GetInt64(5);
                    cols.Add($"{name} | {type} | notnull={notNull} | default={dflt} | pk={pk}");
                }
            }
            cols.Sort(StringComparer.Ordinal);
            tables[table] = string.Join("\n", cols);
        }

        var indexes = new SortedDictionary<string, string>(StringComparer.Ordinal);
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText =
                "SELECT name, tbl_name, sql FROM sqlite_master WHERE type='index' AND name NOT LIKE 'sqlite_%';";
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                var name = r.GetString(0);
                var tbl = r.GetString(1);
                var sql = r.IsDBNull(2) ? "" : NormalizeWhitespace(r.GetString(2));
                indexes[name] = $"{tbl} | {sql}";
            }
        }

        SqliteConnection.ClearPool(conn);

        var sb = new StringBuilder();
        sb.AppendLine("== TABLES ==");
        foreach (var kv in tables)
        {
            sb.AppendLine($"[{kv.Key}]");
            sb.AppendLine(kv.Value);
        }
        sb.AppendLine("== INDEXES ==");
        foreach (var kv in indexes)
            sb.AppendLine($"{kv.Key} => {kv.Value}");

        return new SchemaSnapshot(tables, indexes, sb.ToString());
    }

    private static string NormalizeWhitespace(string sql) => Regex.Replace(sql, @"\s+", " ").Trim();

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

    private static List<string> QueryStrings(SqliteConnection conn, string sql)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        var list = new List<string>();
        using var r = cmd.ExecuteReader();
        while (r.Read()) list.Add(r.GetString(0));
        return list;
    }

    private static SqliteConnection Open(string dbPath)
    {
        var conn = new SqliteConnection(
            new SqliteConnectionStringBuilder { DataSource = dbPath, Mode = SqliteOpenMode.ReadWrite }.ToString());
        conn.Open();
        return conn;
    }

    private static void Exec(SqliteConnection conn, string sql)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    /// <summary>
    /// The frozen v1 (Phase-1 accounting core) create DDL: the seven original tables (<c>schema_version</c>,
    /// <c>companies</c>, <c>groups</c>, <c>ledgers</c>, <c>voucher_types</c>, <c>vouchers</c>, <c>entry_lines</c>)
    /// and the five core indexes, with every column copied verbatim from the current <see cref="Schema.CreateV1"/>
    /// minus the columns that later migrations add (bill-wise, cost centres, banking, interest, multi-currency, GST,
    /// TDS/TCS, …). This is a deliberately independent snapshot — NOT derived from <see cref="Schema.CreateV1"/> at
    /// runtime — so the migration chain that rebuilds it up to the current version is a genuine end-to-end check.
    /// </summary>
    private const string BaselineV1Ddl = """
        CREATE TABLE schema_version (
            version     INTEGER NOT NULL
        );

        CREATE TABLE companies (
            id                       TEXT    NOT NULL PRIMARY KEY,
            name                     TEXT    NOT NULL,
            mailing_name             TEXT    NOT NULL,
            address                  TEXT        NULL,
            country                  TEXT    NOT NULL,
            state                    TEXT        NULL,
            pin                      TEXT        NULL,
            financial_year_start     TEXT    NOT NULL,
            books_begin_from         TEXT    NOT NULL,
            base_currency_symbol     TEXT    NOT NULL,
            base_currency_name       TEXT    NOT NULL,
            decimal_places           INTEGER NOT NULL,
            decimal_unit_name        TEXT    NOT NULL,
            primary_cost_category    TEXT    NOT NULL,
            main_location            TEXT    NOT NULL,
            profit_and_loss_head_id  TEXT        NULL REFERENCES groups(id)
        );

        CREATE TABLE groups (
            id            TEXT    NOT NULL PRIMARY KEY,
            company_id    TEXT    NOT NULL REFERENCES companies(id),
            name          TEXT    NOT NULL,
            nature        INTEGER NOT NULL,
            parent_id     TEXT        NULL REFERENCES groups(id),
            alias         TEXT        NULL,
            is_predefined INTEGER NOT NULL,
            is_pl_head    INTEGER NOT NULL DEFAULT 0
        );

        CREATE TABLE ledgers (
            id                   TEXT    NOT NULL PRIMARY KEY,
            company_id           TEXT    NOT NULL REFERENCES companies(id),
            name                 TEXT    NOT NULL,
            group_id             TEXT    NOT NULL REFERENCES groups(id),
            opening_balance_paisa INTEGER NOT NULL,
            opening_is_debit     INTEGER NOT NULL,
            alias                TEXT        NULL,
            is_predefined        INTEGER NOT NULL
        );

        CREATE TABLE voucher_types (
            id               TEXT    NOT NULL PRIMARY KEY,
            company_id       TEXT    NOT NULL REFERENCES companies(id),
            name             TEXT    NOT NULL,
            base_type        INTEGER NOT NULL,
            default_shortcut TEXT        NULL,
            numbering        INTEGER NOT NULL,
            abbreviation     TEXT        NULL,
            is_active        INTEGER NOT NULL,
            is_predefined    INTEGER NOT NULL
        );

        CREATE TABLE vouchers (
            id          TEXT    NOT NULL PRIMARY KEY,
            company_id  TEXT    NOT NULL REFERENCES companies(id),
            type_id     TEXT    NOT NULL REFERENCES voucher_types(id),
            number      INTEGER NOT NULL,
            date        TEXT    NOT NULL,
            narration   TEXT        NULL,
            party_id    TEXT        NULL REFERENCES ledgers(id),
            cancelled   INTEGER NOT NULL,
            optional    INTEGER NOT NULL,
            post_dated  INTEGER NOT NULL
        );

        CREATE TABLE entry_lines (
            id           INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
            voucher_id   TEXT    NOT NULL REFERENCES vouchers(id),
            line_order   INTEGER NOT NULL,
            ledger_id    TEXT    NOT NULL REFERENCES ledgers(id),
            amount_paisa INTEGER NOT NULL,
            side         INTEGER NOT NULL
        );

        CREATE INDEX ix_groups_company        ON groups(company_id);
        CREATE INDEX ix_ledgers_company        ON ledgers(company_id);
        CREATE INDEX ix_voucher_types_company  ON voucher_types(company_id);
        CREATE INDEX ix_vouchers_company       ON vouchers(company_id);
        CREATE INDEX ix_entry_lines_voucher    ON entry_lines(voucher_id);
        """;
}
