using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Io;
using Apex.Ledger.Persistence;
using Apex.Ledger.Reports;
using Apex.Ledger.Services;
using Microsoft.Data.Sqlite;

namespace Apex.Persistence.Sqlite;

/// <summary>
/// SQLite adapter for the Phase-1 accounting core: <b>one <c>.db</c> file per company</b>. Opens or
/// creates the database at a given path, applies the versioned schema (v1), and implements the three
/// persistence ports (<see cref="ICompanyRepository"/>, <see cref="IMasterRepository"/>,
/// <see cref="IVoucherRepository"/>) against relational tables (accounting-core §2). Money is stored
/// as INTEGER paisa so reports reconcile to the paisa (NFR-3). The domain library stays free of any
/// SQLite dependency — this adapter is the only place <c>Microsoft.Data.Sqlite</c> is referenced.
/// </summary>
/// <remarks>
/// <para><b>Save</b> persists a whole <see cref="Company"/> aggregate (company + masters + posted
/// vouchers) transactionally, replacing any prior state for that company id (idempotent upsert via
/// delete-then-insert within one transaction).</para>
/// <para><b>Load</b> rehydrates the aggregate: it rebuilds the masters directly, then re-posts every
/// stored voucher through <see cref="LedgerService"/> (the real posting path), which preserves the
/// stored voucher number. A voucher that fails validation on reload signals a corrupt store and is
/// surfaced as an exception rather than silently dropped.</para>
/// </remarks>
public sealed class SqliteCompanyStore : ICompanyRepository, IMasterRepository, IVoucherRepository, ISavedReportViewRepository, ISmtpProfileRepository, IDisposable
{
    private readonly SqliteConnection _connection;

    /// <summary>The database file path this store is bound to (empty for in-memory).</summary>
    public string DatabasePath { get; }

    /// <summary>
    /// Opens (or creates) the company database at <paramref name="databasePath"/> and ensures the
    /// schema is present and at the expected version. A single long-lived connection is held for the
    /// lifetime of the store; dispose it to release the file handle.
    /// </summary>
    public SqliteCompanyStore(string databasePath)
    {
        if (string.IsNullOrWhiteSpace(databasePath))
            throw new ArgumentException("A database path is required.", nameof(databasePath));

        DatabasePath = databasePath;

        var dir = Path.GetDirectoryName(Path.GetFullPath(databasePath));
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        var connStr = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
        }.ToString();

        _connection = new SqliteConnection(connStr);
        _connection.Open();
        Exec("PRAGMA foreign_keys = ON;");
        EnsureSchema();
    }

    // ------------------------------------------------------------------ schema / migrations

    private void EnsureSchema()
    {
        var hasVersionTable = ScalarLong(
            "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='schema_version';") > 0;

        if (!hasVersionTable)
        {
            using var tx = _connection.BeginTransaction();
            using (var cmd = _connection.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = Schema.CreateV1;
                cmd.ExecuteNonQuery();
            }
            using (var ins = _connection.CreateCommand())
            {
                ins.Transaction = tx;
                ins.CommandText = "INSERT INTO schema_version(version) VALUES ($v);";
                ins.Parameters.AddWithValue("$v", Schema.CurrentVersion);
                ins.ExecuteNonQuery();
            }
            tx.Commit();
            return;
        }

        var version = ScalarLong("SELECT version FROM schema_version LIMIT 1;");

        // v1 → v2: apply the bill-wise migration, then bump the marker. Existing v1 data survives.
        if (version == 1)
        {
            using var tx = _connection.BeginTransaction();
            using (var mig = _connection.CreateCommand())
            {
                mig.Transaction = tx;
                mig.CommandText = Schema.MigrateV1ToV2;
                mig.ExecuteNonQuery();
            }
            using (var bump = _connection.CreateCommand())
            {
                bump.Transaction = tx;
                bump.CommandText = "UPDATE schema_version SET version = $v;";
                bump.Parameters.AddWithValue("$v", 2);
                bump.ExecuteNonQuery();
            }
            tx.Commit();
            version = 2;
        }

        // v2 → v3: apply the cost-centre migration, then bump the marker. Existing v2 data survives.
        if (version == 2)
        {
            using var tx = _connection.BeginTransaction();
            using (var mig = _connection.CreateCommand())
            {
                mig.Transaction = tx;
                mig.CommandText = Schema.MigrateV2ToV3;
                mig.ExecuteNonQuery();
            }
            using (var bump = _connection.CreateCommand())
            {
                bump.Transaction = tx;
                bump.CommandText = "UPDATE schema_version SET version = $v;";
                bump.Parameters.AddWithValue("$v", 3);
                bump.ExecuteNonQuery();
            }
            tx.Commit();
            version = 3;
        }

        // v3 → v4: apply the budgets migration, then bump the marker. Existing v3 data survives.
        if (version == 3)
        {
            using var tx = _connection.BeginTransaction();
            using (var mig = _connection.CreateCommand())
            {
                mig.Transaction = tx;
                mig.CommandText = Schema.MigrateV3ToV4;
                mig.ExecuteNonQuery();
            }
            using (var bump = _connection.CreateCommand())
            {
                bump.Transaction = tx;
                bump.CommandText = "UPDATE schema_version SET version = $v;";
                bump.Parameters.AddWithValue("$v", 4);
                bump.ExecuteNonQuery();
            }
            tx.Commit();
            version = 4;
        }

        // v4 → v5: apply the banking migration, then bump the marker. Existing v4 data survives.
        if (version == 4)
        {
            using var tx = _connection.BeginTransaction();
            using (var mig = _connection.CreateCommand())
            {
                mig.Transaction = tx;
                mig.CommandText = Schema.MigrateV4ToV5;
                mig.ExecuteNonQuery();
            }
            using (var bump = _connection.CreateCommand())
            {
                bump.Transaction = tx;
                bump.CommandText = "UPDATE schema_version SET version = $v;";
                bump.Parameters.AddWithValue("$v", 5);
                bump.ExecuteNonQuery();
            }
            tx.Commit();
            version = 5;
        }

        // v5 → v6: apply the scenarios migration, then bump the marker. Existing v5 data survives.
        if (version == 5)
        {
            using var tx = _connection.BeginTransaction();
            using (var mig = _connection.CreateCommand())
            {
                mig.Transaction = tx;
                mig.CommandText = Schema.MigrateV5ToV6;
                mig.ExecuteNonQuery();
            }
            using (var bump = _connection.CreateCommand())
            {
                bump.Transaction = tx;
                bump.CommandText = "UPDATE schema_version SET version = $v;";
                bump.Parameters.AddWithValue("$v", 6);
                bump.ExecuteNonQuery();
            }
            tx.Commit();
            version = 6;
        }

        // v6 → v7: apply the interest-calculation migration, then bump the marker. Existing v6 data survives.
        if (version == 6)
        {
            using var tx = _connection.BeginTransaction();
            using (var mig = _connection.CreateCommand())
            {
                mig.Transaction = tx;
                mig.CommandText = Schema.MigrateV6ToV7;
                mig.ExecuteNonQuery();
            }
            using (var bump = _connection.CreateCommand())
            {
                bump.Transaction = tx;
                bump.CommandText = "UPDATE schema_version SET version = $v;";
                bump.Parameters.AddWithValue("$v", 7);
                bump.ExecuteNonQuery();
            }
            tx.Commit();
            version = 7;
        }

        // v7 → v8: apply the multi-currency migration, then bump the marker. Existing v7 data survives.
        if (version == 7)
        {
            using var tx = _connection.BeginTransaction();
            using (var mig = _connection.CreateCommand())
            {
                mig.Transaction = tx;
                mig.CommandText = Schema.MigrateV7ToV8;
                mig.ExecuteNonQuery();
            }
            using (var bump = _connection.CreateCommand())
            {
                bump.Transaction = tx;
                bump.CommandText = "UPDATE schema_version SET version = $v;";
                bump.Parameters.AddWithValue("$v", 8);
                bump.ExecuteNonQuery();
            }
            tx.Commit();
            version = 8;
        }

        // v8 → v9: apply the inventory-masters migration, then bump the marker. Existing v8 data survives.
        if (version == 8)
        {
            using var tx = _connection.BeginTransaction();
            using (var mig = _connection.CreateCommand())
            {
                mig.Transaction = tx;
                mig.CommandText = Schema.MigrateV8ToV9;
                mig.ExecuteNonQuery();
            }
            using (var bump = _connection.CreateCommand())
            {
                bump.Transaction = tx;
                bump.CommandText = "UPDATE schema_version SET version = $v;";
                bump.Parameters.AddWithValue("$v", 9);
                bump.ExecuteNonQuery();
            }
            tx.Commit();
            version = 9;
        }

        // v9 → v10: apply the inventory & order voucher migration, then bump the marker. Existing v9 data survives.
        if (version == 9)
        {
            using var tx = _connection.BeginTransaction();
            using (var mig = _connection.CreateCommand())
            {
                mig.Transaction = tx;
                mig.CommandText = Schema.MigrateV9ToV10;
                mig.ExecuteNonQuery();
            }
            using (var bump = _connection.CreateCommand())
            {
                bump.Transaction = tx;
                bump.CommandText = "UPDATE schema_version SET version = $v;";
                bump.Parameters.AddWithValue("$v", 10);
                bump.ExecuteNonQuery();
            }
            tx.Commit();
            version = 10;
        }

        // v10 → v11: apply the per-item standard-cost migration, then bump the marker. Existing v10 data survives.
        if (version == 10)
        {
            using var tx = _connection.BeginTransaction();
            using (var mig = _connection.CreateCommand())
            {
                mig.Transaction = tx;
                mig.CommandText = Schema.MigrateV10ToV11;
                mig.ExecuteNonQuery();
            }
            using (var bump = _connection.CreateCommand())
            {
                bump.Transaction = tx;
                bump.CommandText = "UPDATE schema_version SET version = $v;";
                bump.Parameters.AddWithValue("$v", 11);
                bump.ExecuteNonQuery();
            }
            tx.Commit();
            version = 11;
        }

        // v11 → v12: apply the item-invoice stock-line migration, then bump the marker. Existing v11 data survives.
        if (version == 11)
        {
            using var tx = _connection.BeginTransaction();
            using (var mig = _connection.CreateCommand())
            {
                mig.Transaction = tx;
                mig.CommandText = Schema.MigrateV11ToV12;
                mig.ExecuteNonQuery();
            }
            using (var bump = _connection.CreateCommand())
            {
                bump.Transaction = tx;
                bump.CommandText = "UPDATE schema_version SET version = $v;";
                bump.Parameters.AddWithValue("$v", 12);
                bump.ExecuteNonQuery();
            }
            tx.Commit();
            version = 12;
        }

        // v12 → v13: apply the core-GST migration, then bump the marker. Existing v12 data survives (GST off).
        if (version == 12)
        {
            using var tx = _connection.BeginTransaction();
            using (var mig = _connection.CreateCommand())
            {
                mig.Transaction = tx;
                mig.CommandText = Schema.MigrateV12ToV13;
                mig.ExecuteNonQuery();
            }
            using (var bump = _connection.CreateCommand())
            {
                bump.Transaction = tx;
                bump.CommandText = "UPDATE schema_version SET version = $v;";
                bump.Parameters.AddWithValue("$v", 13);
                bump.ExecuteNonQuery();
            }
            tx.Commit();
            version = 13;
        }

        // v13 → v14: apply the saved-views migration, then bump the marker. Existing v13 data survives untouched.
        if (version == 13)
        {
            using var tx = _connection.BeginTransaction();
            using (var mig = _connection.CreateCommand())
            {
                mig.Transaction = tx;
                mig.CommandText = Schema.MigrateV13ToV14;
                mig.ExecuteNonQuery();
            }
            using (var bump = _connection.CreateCommand())
            {
                bump.Transaction = tx;
                bump.CommandText = "UPDATE schema_version SET version = $v;";
                bump.Parameters.AddWithValue("$v", 14);
                bump.ExecuteNonQuery();
            }
            tx.Commit();
            version = 14;
        }

        // v14 → v15: apply the SMTP-profile migration, then bump the marker. Existing v14 data survives untouched.
        if (version == 14)
        {
            using var tx = _connection.BeginTransaction();
            using (var mig = _connection.CreateCommand())
            {
                mig.Transaction = tx;
                mig.CommandText = Schema.MigrateV14ToV15;
                mig.ExecuteNonQuery();
            }
            using (var bump = _connection.CreateCommand())
            {
                bump.Transaction = tx;
                bump.CommandText = "UPDATE schema_version SET version = $v;";
                bump.Parameters.AddWithValue("$v", 15);
                bump.ExecuteNonQuery();
            }
            tx.Commit();
            version = 15;
        }

        // v15 → v16: apply the batch-masters migration, then bump the marker. Existing v15 data survives untouched
        // (pure CREATE + additive nullable batch_id columns; batch_label stays intact — Phase 6 slice 1).
        if (version == 15)
        {
            using var tx = _connection.BeginTransaction();
            using (var mig = _connection.CreateCommand())
            {
                mig.Transaction = tx;
                mig.CommandText = Schema.MigrateV15ToV16;
                mig.ExecuteNonQuery();
            }
            using (var bump = _connection.CreateCommand())
            {
                bump.Transaction = tx;
                bump.CommandText = "UPDATE schema_version SET version = $v;";
                bump.Parameters.AddWithValue("$v", 16);
                bump.ExecuteNonQuery();
            }
            tx.Commit();
            version = 16;
        }

        // v16 → v17: apply the Bill-of-Materials masters migration, then bump the marker. Existing v16 data survives
        // untouched (two pure CREATE TABLE + four CREATE INDEX; no ALTER, no row rewrites — Phase 6 slice 2).
        if (version == 16)
        {
            using var tx = _connection.BeginTransaction();
            using (var mig = _connection.CreateCommand())
            {
                mig.Transaction = tx;
                mig.CommandText = Schema.MigrateV16ToV17;
                mig.ExecuteNonQuery();
            }
            using (var bump = _connection.CreateCommand())
            {
                bump.Transaction = tx;
                bump.CommandText = "UPDATE schema_version SET version = $v;";
                bump.Parameters.AddWithValue("$v", 17);
                bump.ExecuteNonQuery();
            }
            tx.Commit();
            version = 17;
        }

        // v17 → v18: apply the two Manufacturing-Journal master-flag columns, then bump the marker. Existing v17
        // data survives untouched (two additive ALTER … ADD COLUMN DEFAULT 0; no row rewrites — Phase 6 slice 2).
        if (version == 17)
        {
            using var tx = _connection.BeginTransaction();
            using (var mig = _connection.CreateCommand())
            {
                mig.Transaction = tx;
                mig.CommandText = Schema.MigrateV17ToV18;
                mig.ExecuteNonQuery();
            }
            using (var bump = _connection.CreateCommand())
            {
                bump.Transaction = tx;
                bump.CommandText = "UPDATE schema_version SET version = $v;";
                bump.Parameters.AddWithValue("$v", 18);
                bump.ExecuteNonQuery();
            }
            tx.Commit();
            version = 18;
        }

        // v18 → v19: apply the Additional-Cost-of-Purchase schema (track_additional_costs flag +
        // method_of_appropriation column + additional_cost_lines table), then bump the marker. Existing v18 data
        // survives untouched (two additive ALTER + one CREATE TABLE/INDEX; no row rewrites — Phase 6 slice 3).
        if (version == 18)
        {
            using var tx = _connection.BeginTransaction();
            using (var mig = _connection.CreateCommand())
            {
                mig.Transaction = tx;
                mig.CommandText = Schema.MigrateV18ToV19;
                mig.ExecuteNonQuery();
            }
            using (var bump = _connection.CreateCommand())
            {
                bump.Transaction = tx;
                bump.CommandText = "UPDATE schema_version SET version = $v;";
                bump.Parameters.AddWithValue("$v", 19);
                bump.ExecuteNonQuery();
            }
            tx.Commit();
            version = 19;
        }

        // v19 → v20: apply the Actual/Billed-quantity + zero-valued schema (four nullable qty columns on the two
        // stock-line tables + a company F11 flag + a voucher-type flag), then bump the marker. Existing v19 data
        // survives untouched (four additive ALTER ADD COLUMN NULL + two ALTER ADD COLUMN DEFAULT 0; no row rewrites —
        // Phase 6 slice 4).
        if (version == 19)
        {
            using var tx = _connection.BeginTransaction();
            using (var mig = _connection.CreateCommand())
            {
                mig.Transaction = tx;
                mig.CommandText = Schema.MigrateV19ToV20;
                mig.ExecuteNonQuery();
            }
            using (var bump = _connection.CreateCommand())
            {
                bump.Transaction = tx;
                bump.CommandText = "UPDATE schema_version SET version = $v;";
                bump.Parameters.AddWithValue("$v", 20);
                bump.ExecuteNonQuery();
            }
            tx.Commit();
            version = 20;
        }

        // v20 → v21: apply the Price Levels / Price Lists schema (three new tables + a company F11 flag + a party
        // ledger default-level FK), then bump the marker. Existing v20 data survives untouched (three CREATE TABLE/
        // INDEX + one ALTER ADD COLUMN DEFAULT 0 + one ALTER ADD COLUMN NULL; no row rewrites — Phase 6 slice 5).
        if (version == 20)
        {
            using var tx = _connection.BeginTransaction();
            using (var mig = _connection.CreateCommand())
            {
                mig.Transaction = tx;
                mig.CommandText = Schema.MigrateV20ToV21;
                mig.ExecuteNonQuery();
            }
            using (var bump = _connection.CreateCommand())
            {
                bump.Transaction = tx;
                bump.CommandText = "UPDATE schema_version SET version = $v;";
                bump.Parameters.AddWithValue("$v", 21);
                bump.ExecuteNonQuery();
            }
            tx.Commit();
            version = 21;
        }

        // v21 → v22: apply the Reorder Levels schema (one new table + two indexes), then bump the marker. Existing
        // v21 data survives untouched (a single additive CREATE TABLE/INDEX; no ALTER, no row rewrites — Phase 6
        // slice 6).
        if (version == 21)
        {
            using var tx = _connection.BeginTransaction();
            using (var mig = _connection.CreateCommand())
            {
                mig.Transaction = tx;
                mig.CommandText = Schema.MigrateV21ToV22;
                mig.ExecuteNonQuery();
            }
            using (var bump = _connection.CreateCommand())
            {
                bump.Transaction = tx;
                bump.CommandText = "UPDATE schema_version SET version = $v;";
                bump.Parameters.AddWithValue("$v", 22);
                bump.ExecuteNonQuery();
            }
            tx.Commit();
            version = 22;
        }

        // v22 → v23: apply the POS schema (a use_for_pos flag on voucher_types + three new tables + one index),
        // then bump the marker. Existing v22 data survives untouched (one additive ALTER ADD COLUMN DEFAULT 0 +
        // three CREATE TABLE + one CREATE INDEX; no row rewrites — Phase 6 slice 7).
        if (version == 22)
        {
            using var tx = _connection.BeginTransaction();
            using (var mig = _connection.CreateCommand())
            {
                mig.Transaction = tx;
                mig.CommandText = Schema.MigrateV22ToV23;
                mig.ExecuteNonQuery();
            }
            using (var bump = _connection.CreateCommand())
            {
                bump.Transaction = tx;
                bump.CommandText = "UPDATE schema_version SET version = $v;";
                bump.Parameters.AddWithValue("$v", 23);
                bump.ExecuteNonQuery();
            }
            tx.Commit();
            version = 23;
        }

        // v23 → v24: apply the Job Work schema (three new tables + three additive flag columns), then bump the
        // marker. Existing v23 data survives untouched (three CREATE TABLE/INDEX + three ALTER ADD COLUMN DEFAULT 0;
        // no row rewrites — Phase 6 slice 8).
        if (version == 23)
        {
            using var tx = _connection.BeginTransaction();
            using (var mig = _connection.CreateCommand())
            {
                mig.Transaction = tx;
                mig.CommandText = Schema.MigrateV23ToV24;
                mig.ExecuteNonQuery();
            }
            using (var bump = _connection.CreateCommand())
            {
                bump.Transaction = tx;
                bump.CommandText = "UPDATE schema_version SET version = $v;";
                bump.Parameters.AddWithValue("$v", 24);
                bump.ExecuteNonQuery();
            }
            tx.Commit();
            version = 24;
        }

        // v24 → v25: apply the TDS/TCS masters+config schema (two new tables + additive company/ledger/stock-item
        // columns), then bump the marker. Existing v24 data survives untouched (CREATE TABLE/INDEX + ALTER ADD COLUMN
        // with 0/NULL defaults; no row rewrites — Phase 7 slice 1).
        if (version == 24)
        {
            using var tx = _connection.BeginTransaction();
            using (var mig = _connection.CreateCommand())
            {
                mig.Transaction = tx;
                mig.CommandText = Schema.MigrateV24ToV25;
                mig.ExecuteNonQuery();
            }
            using (var bump = _connection.CreateCommand())
            {
                bump.Transaction = tx;
                bump.CommandText = "UPDATE schema_version SET version = $v;";
                bump.Parameters.AddWithValue("$v", 25);
                bump.ExecuteNonQuery();
            }
            tx.Commit();
            version = 25;
        }

        // v25 → v26: apply the TDS withholding-detail schema (one new tds_lines table + index), then bump the
        // marker. Existing v25 data survives untouched (a single CREATE TABLE/INDEX; no ALTER, no row rewrites —
        // Phase 7 slice 2). ER-13 byte-identical when TDS is never withheld (the table stays empty).
        if (version == 25)
        {
            using var tx = _connection.BeginTransaction();
            using (var mig = _connection.CreateCommand())
            {
                mig.Transaction = tx;
                mig.CommandText = Schema.MigrateV25ToV26;
                mig.ExecuteNonQuery();
            }
            using (var bump = _connection.CreateCommand())
            {
                bump.Transaction = tx;
                bump.CommandText = "UPDATE schema_version SET version = $v;";
                bump.Parameters.AddWithValue("$v", 26);
                bump.ExecuteNonQuery();
            }
            tx.Commit();
            version = 26;
        }

        // v26 → v27: apply the TDS deposit + challan schema (is_stat_payment voucher-type flag + two new challan
        // tables + indexes), then bump the marker. Existing v26 data survives untouched (an ALTER ADD COLUMN with a
        // 0 default + CREATE TABLE/INDEX; no row rewrites — Phase 7 slice 3). ER-13 byte-identical when no TDS is
        // deposited (the new column defaults 0, the new tables stay empty).
        if (version == 26)
        {
            using var tx = _connection.BeginTransaction();
            using (var mig = _connection.CreateCommand())
            {
                mig.Transaction = tx;
                mig.CommandText = Schema.MigrateV26ToV27;
                mig.ExecuteNonQuery();
            }
            using (var bump = _connection.CreateCommand())
            {
                bump.Transaction = tx;
                bump.CommandText = "UPDATE schema_version SET version = $v;";
                bump.Parameters.AddWithValue("$v", 27);
                bump.ExecuteNonQuery();
            }
            tx.Commit();
            version = 27;
        }

        // v27 → v28: apply the TCS collection-detail schema (one new tcs_lines table + index), then bump the marker.
        // Existing v27 data survives untouched (a single CREATE TABLE/INDEX; no ALTER, no row rewrites — Phase 7
        // slice 5). ER-13 byte-identical when TCS is never collected (the table stays empty). TCS is additive (the
        // mirror of GST), a straight sibling of the v26 tds_lines table.
        if (version == 27)
        {
            using var tx = _connection.BeginTransaction();
            using (var mig = _connection.CreateCommand())
            {
                mig.Transaction = tx;
                mig.CommandText = Schema.MigrateV27ToV28;
                mig.ExecuteNonQuery();
            }
            using (var bump = _connection.CreateCommand())
            {
                bump.Transaction = tx;
                bump.CommandText = "UPDATE schema_version SET version = $v;";
                bump.Parameters.AddWithValue("$v", 28);
                bump.ExecuteNonQuery();
            }
            tx.Commit();
            version = 28;
        }

        // v28 → v29: apply the TCS deposit + challan schema (two new tcs_challan tables + indexes), then bump the
        // marker. Existing v28 data survives untouched (CREATE TABLE/INDEX only; no ALTER — the is_stat_payment flag
        // from v27 is reused; no row rewrites — Phase 7 slice 6). ER-13 byte-identical when no TCS is deposited (the
        // new tables stay empty). The exact sibling of the v26→v27 TDS challan migration.
        if (version == 28)
        {
            using var tx = _connection.BeginTransaction();
            using (var mig = _connection.CreateCommand())
            {
                mig.Transaction = tx;
                mig.CommandText = Schema.MigrateV28ToV29;
                mig.ExecuteNonQuery();
            }
            using (var bump = _connection.CreateCommand())
            {
                bump.Transaction = tx;
                bump.CommandText = "UPDATE schema_version SET version = $v;";
                bump.Parameters.AddWithValue("$v", 29);
                bump.ExecuteNonQuery();
            }
            tx.Commit();
            version = 29;
        }

        // v29 → v30: apply the Payroll masters + F11-config schema (two additive companies columns + five new
        // master tables + indexes), then bump the marker. Existing v29 data survives untouched (ALTER … ADD COLUMN
        // DEFAULT 0 + CREATE TABLE/INDEX only; no row rewrites — Phase 8 slice 1). ER-13 byte-identical when Payroll
        // is never enabled (the flags default 0, the new tables stay empty).
        if (version == 29)
        {
            using var tx = _connection.BeginTransaction();
            using (var mig = _connection.CreateCommand())
            {
                mig.Transaction = tx;
                mig.CommandText = Schema.MigrateV29ToV30;
                mig.ExecuteNonQuery();
            }
            using (var bump = _connection.CreateCommand())
            {
                bump.Transaction = tx;
                bump.CommandText = "UPDATE schema_version SET version = $v;";
                bump.Parameters.AddWithValue("$v", 30);
                bump.ExecuteNonQuery();
            }
            tx.Commit();
            version = 30;
        }

        // v30 → v31: apply the Pay Heads + dated Salary Structures schema (five new tables + indexes), then bump the
        // marker. Existing v30 data survives untouched (CREATE TABLE/INDEX only; no ALTER, no companies column, no row
        // rewrites — Phase 8 slice 2). ER-13 byte-identical when Payroll is never used (the new tables stay empty).
        if (version == 30)
        {
            using var tx = _connection.BeginTransaction();
            using (var mig = _connection.CreateCommand())
            {
                mig.Transaction = tx;
                mig.CommandText = Schema.MigrateV30ToV31;
                mig.ExecuteNonQuery();
            }
            using (var bump = _connection.CreateCommand())
            {
                bump.Transaction = tx;
                bump.CommandText = "UPDATE schema_version SET version = $v;";
                bump.Parameters.AddWithValue("$v", 31);
                bump.ExecuteNonQuery();
            }
            tx.Commit();
            version = 31;
        }

        // v31 → v32: apply the Attendance + Payroll-voucher engine schema (one additive pay_heads column + two new
        // tables — attendance_entries + payroll_lines — with indexes), then bump the marker. Existing v31 data
        // survives untouched (ALTER … ADD COLUMN NULL + CREATE TABLE/INDEX only; no row rewrites — Phase 8 slice 3).
        // ER-13 byte-identical when Payroll is never run (the new column defaults NULL, the new tables stay empty).
        if (version == 31)
        {
            using var tx = _connection.BeginTransaction();
            using (var mig = _connection.CreateCommand())
            {
                mig.Transaction = tx;
                mig.CommandText = Schema.MigrateV31ToV32;
                mig.ExecuteNonQuery();
            }
            using (var bump = _connection.CreateCommand())
            {
                bump.Transaction = tx;
                bump.CommandText = "UPDATE schema_version SET version = $v;";
                bump.Parameters.AddWithValue("$v", 32);
                bump.ExecuteNonQuery();
            }
            tx.Commit();
            version = 32;
        }

        // v32 → v33: apply the Provident-Fund schema (four additive companies columns for the establishment PF
        // config, three additive employees columns for the per-employee PF details, two additive pay_heads columns
        // for the PF statutory role + PF-wage flag), then bump the marker. Existing v32 data survives untouched
        // (ALTER … ADD COLUMN only; no new tables, no row rewrites — Phase 8 slice 4). ER-13 byte-identical when the
        // establishment never enrols for PF (pf_config_enabled defaults 0, the per-employee/pay-head flags default 0).
        if (version == 32)
        {
            using var tx = _connection.BeginTransaction();
            using (var mig = _connection.CreateCommand())
            {
                mig.Transaction = tx;
                mig.CommandText = Schema.MigrateV32ToV33;
                mig.ExecuteNonQuery();
            }
            using (var bump = _connection.CreateCommand())
            {
                bump.Transaction = tx;
                bump.CommandText = "UPDATE schema_version SET version = $v;";
                bump.Parameters.AddWithValue("$v", 33);
                bump.ExecuteNonQuery();
            }
            tx.Commit();
            version = 33;
        }

        // v33 → v34: apply the Employees'-State-Insurance schema (four additive companies columns for the
        // establishment ESI config, one additive employees column for per-employee ESI applicability, three additive
        // pay_heads columns for the ESI statutory role + ESI-wage flag + overtime marker), then bump the marker.
        // Existing v33 data survives untouched (ALTER … ADD COLUMN only; no new tables, no row rewrites — Phase 8
        // slice 5). ER-13 byte-identical when the establishment never enrols for ESI (esi_config_enabled defaults 0,
        // the per-employee/pay-head flags default 0).
        if (version == 33)
        {
            using var tx = _connection.BeginTransaction();
            using (var mig = _connection.CreateCommand())
            {
                mig.Transaction = tx;
                mig.CommandText = Schema.MigrateV33ToV34;
                mig.ExecuteNonQuery();
            }
            using (var bump = _connection.CreateCommand())
            {
                bump.Transaction = tx;
                bump.CommandText = "UPDATE schema_version SET version = $v;";
                bump.Parameters.AddWithValue("$v", 34);
                bump.ExecuteNonQuery();
            }
            tx.Commit();
            version = 34;
        }

        // v34 → v35: apply the Professional-Tax schema (four additive companies columns for the establishment PT
        // config, one additive pay_heads column for the PT statutory role, one new pt_slab_bands table + index for the
        // editable per-state slab bands), then bump the marker. Existing v34 data survives untouched (ALTER … ADD
        // COLUMN + a new empty table; no row rewrites — Phase 8 slice 6). ER-13 byte-identical when the establishment
        // never enrols for PT (pt_config_enabled defaults 0, pt_slab_bands starts empty).
        if (version == 34)
        {
            using var tx = _connection.BeginTransaction();
            using (var mig = _connection.CreateCommand())
            {
                mig.Transaction = tx;
                mig.CommandText = Schema.MigrateV34ToV35;
                mig.ExecuteNonQuery();
            }
            using (var bump = _connection.CreateCommand())
            {
                bump.Transaction = tx;
                bump.CommandText = "UPDATE schema_version SET version = $v;";
                bump.Parameters.AddWithValue("$v", 35);
                bump.ExecuteNonQuery();
            }
            tx.Commit();
            version = 35;
        }

        // v35 → v36: apply the §192 salary-TDS schema (one additive companies column for the establishment salary-TDS
        // toggle, one new employee_tax_declarations table + index for the per-employee Form-12BB declaration), then
        // bump the marker. Existing v35 data survives untouched (ALTER … ADD COLUMN + a new empty table; no row
        // rewrites — Phase 8 slice 7). ER-13 byte-identical when the establishment never deducts salary-TDS
        // (salary_tds_enabled defaults 0, employee_tax_declarations starts empty).
        if (version == 35)
        {
            using var tx = _connection.BeginTransaction();
            using (var mig = _connection.CreateCommand())
            {
                mig.Transaction = tx;
                mig.CommandText = Schema.MigrateV35ToV36;
                mig.ExecuteNonQuery();
            }
            using (var bump = _connection.CreateCommand())
            {
                bump.Transaction = tx;
                bump.CommandText = "UPDATE schema_version SET version = $v;";
                bump.Parameters.AddWithValue("$v", 36);
                bump.ExecuteNonQuery();
            }
            tx.Commit();
            version = 36;
        }

        // v36 → v37: apply the Gratuity + statutory-Bonus schema (nine additive companies columns for the
        // establishment gratuity config [enrolled/cap/wage-basis/population] and bonus config [enrolled/rate/
        // calc-ceiling/minimum-wage/prorate]), then bump the marker. Existing v36 data survives untouched (ALTER …
        // ADD COLUMN only; no new tables, no row rewrites — Phase 8 slice 9). ER-13 byte-identical when the
        // establishment provisions neither (both *_config_enabled default 0, the rest carry statutory defaults).
        if (version == 36)
        {
            using var tx = _connection.BeginTransaction();
            using (var mig = _connection.CreateCommand())
            {
                mig.Transaction = tx;
                mig.CommandText = Schema.MigrateV36ToV37;
                mig.ExecuteNonQuery();
            }
            using (var bump = _connection.CreateCommand())
            {
                bump.Transaction = tx;
                bump.CommandText = "UPDATE schema_version SET version = $v;";
                bump.Parameters.AddWithValue("$v", 37);
                bump.ExecuteNonQuery();
            }
            tx.Commit();
            version = 37;
        }

        // v37 → v38: apply the Phase-9 GST 2.0 dated rate framework + Compensation-Cess seam (two new tables
        // gst_rate_history/gst_cess_rates + their indexes, and seven additive columns each on stock_items and
        // ledgers), then bump the marker. Existing v37 data survives untouched (CREATE TABLE/INDEX + ALTER … ADD
        // COLUMN only; no row rewrites). ER-13 byte-identical when a company enables no advanced GST (new tables
        // empty, new columns default 0/NULL).
        if (version == 37)
        {
            using var tx = _connection.BeginTransaction();
            using (var mig = _connection.CreateCommand())
            {
                mig.Transaction = tx;
                mig.CommandText = Schema.MigrateV37ToV38;
                mig.ExecuteNonQuery();
            }
            using (var bump = _connection.CreateCommand())
            {
                bump.Transaction = tx;
                bump.CommandText = "UPDATE schema_version SET version = $v;";
                bump.Parameters.AddWithValue("$v", 38);
                bump.ExecuteNonQuery();
            }
            tx.Commit();
            version = 38;
        }

        // v38 → v39: apply the Phase-9 slice 2 RCM core + §34-CDN/advances seam (four new tables rcm_categories/
        // rcm_documents/gst_cdn_links/gst_advance_receipts + their indexes, and the reverse-charge additive columns on
        // stock_items/ledgers/entry_lines/voucher_types), then bump the marker. Existing v38 data survives untouched
        // (CREATE TABLE/INDEX + ALTER … ADD COLUMN only; no row rewrites). ER-13 byte-identical when a company uses no
        // reverse charge / CDN / advances (new tables empty, new columns default 0/NULL).
        if (version == 38)
        {
            using var tx = _connection.BeginTransaction();
            using (var mig = _connection.CreateCommand())
            {
                mig.Transaction = tx;
                mig.CommandText = Schema.MigrateV38ToV39;
                mig.ExecuteNonQuery();
            }
            using (var bump = _connection.CreateCommand())
            {
                bump.Transaction = tx;
                bump.CommandText = "UPDATE schema_version SET version = $v;";
                bump.Parameters.AddWithValue("$v", 39);
                bump.ExecuteNonQuery();
            }
            tx.Commit();
            version = 39;
        }

        // v39 → v40: apply the Phase-9 slice 3 Composition-scheme config (two ALTER-added columns on companies:
        // composition_sub_type + composition_opt_in_date), then bump the marker. Existing v39 data survives untouched
        // (ALTER … ADD COLUMN only; no row rewrites, no new table). ER-13 byte-identical when a company is not a
        // composition dealer (both new columns default NULL).
        if (version == 39)
        {
            using var tx = _connection.BeginTransaction();
            using (var mig = _connection.CreateCommand())
            {
                mig.Transaction = tx;
                mig.CommandText = Schema.MigrateV39ToV40;
                mig.ExecuteNonQuery();
            }
            using (var bump = _connection.CreateCommand())
            {
                bump.Transaction = tx;
                bump.CommandText = "UPDATE schema_version SET version = $v;";
                bump.Parameters.AddWithValue("$v", 40);
                bump.ExecuteNonQuery();
            }
            tx.Commit();
            version = 40;
        }

        // v40 → v41: apply the Phase-9 slice 4a e-invoice core (new einvoice_records table + index; e-invoice / B2C-QR /
        // connector-mode config columns on companies; and the nic_*_enc protected-credential BLOB columns), then bump the
        // marker. Existing v40 data survives untouched (CREATE TABLE/INDEX + ALTER … ADD COLUMN only; no row rewrites).
        // ER-13 byte-identical when e-invoicing is off (new table empty, new columns default 0/NULL/threshold).
        if (version == 40)
        {
            using var tx = _connection.BeginTransaction();
            using (var mig = _connection.CreateCommand())
            {
                mig.Transaction = tx;
                mig.CommandText = Schema.MigrateV40ToV41;
                mig.ExecuteNonQuery();
            }
            using (var bump = _connection.CreateCommand())
            {
                bump.Transaction = tx;
                bump.CommandText = "UPDATE schema_version SET version = $v;";
                bump.Parameters.AddWithValue("$v", 41);
                bump.ExecuteNonQuery();
            }
            tx.Commit();
            version = 41;
        }

        // v41 → v42: apply the Phase-9 slice 5 e-Way Bill core (new eway_bills + eway_state_thresholds tables + indexes;
        // five non-secret e-Way config columns on companies), then bump the marker. Existing v41 data survives untouched
        // (CREATE TABLE/INDEX + ALTER … ADD COLUMN only; no row rewrites). The live NIC path REUSES the shared
        // gst_connector_mode + nic_*_enc columns — no new secret column. ER-13 byte-identical when e-Way is off.
        if (version == 41)
        {
            using var tx = _connection.BeginTransaction();
            using (var mig = _connection.CreateCommand())
            {
                mig.Transaction = tx;
                mig.CommandText = Schema.MigrateV41ToV42;
                mig.ExecuteNonQuery();
            }
            using (var bump = _connection.CreateCommand())
            {
                bump.Transaction = tx;
                bump.CommandText = "UPDATE schema_version SET version = $v;";
                bump.Parameters.AddWithValue("$v", 42);
                bump.ExecuteNonQuery();
            }
            tx.Commit();
            version = 42;
        }

        // v42 → v43: apply the Phase-9 slice 6 GSTR-2A/2B inbound core (new gstr2b_snapshots + gstr2b_lines + ims_status
        // + gstr2b_recon tables + indexes; two §17(5) columns each on stock_items + ledgers), then bump the marker.
        // Existing v42 data survives untouched (CREATE TABLE/INDEX + ALTER … ADD COLUMN only; no row rewrites). The
        // ims_status table + the §17(5) columns stay unused until S6b. ER-13 byte-identical when 2B is never imported.
        if (version == 42)
        {
            using var tx = _connection.BeginTransaction();
            using (var mig = _connection.CreateCommand())
            {
                mig.Transaction = tx;
                mig.CommandText = Schema.MigrateV42ToV43;
                mig.ExecuteNonQuery();
            }
            using (var bump = _connection.CreateCommand())
            {
                bump.Transaction = tx;
                bump.CommandText = "UPDATE schema_version SET version = $v;";
                bump.Parameters.AddWithValue("$v", 43);
                bump.ExecuteNonQuery();
            }
            tx.Commit();
            version = 43;
        }

        // v43 → v44: apply the Phase-9 slice 7 electronic-ledger / Rule-88A set-off / GST-payment core (new
        // gst_setoff_lines + itc_reversals + gst_challans + gst_drc03 tables + indexes; one adjustment-tag column on
        // entry_lines + one stat-adjustment flag on voucher_types), then bump the marker. Existing v43 data survives
        // untouched (CREATE TABLE/INDEX + ALTER … ADD COLUMN only; no row rewrites). The itc_reversals table stays
        // unused until S7b. ER-13 byte-identical when the company never sets off / pays / reverses.
        if (version == 43)
        {
            using var tx = _connection.BeginTransaction();
            using (var mig = _connection.CreateCommand())
            {
                mig.Transaction = tx;
                mig.CommandText = Schema.MigrateV43ToV44;
                mig.ExecuteNonQuery();
            }
            using (var bump = _connection.CreateCommand())
            {
                bump.Transaction = tx;
                bump.CommandText = "UPDATE schema_version SET version = $v;";
                bump.Parameters.AddWithValue("$v", 44);
                bump.ExecuteNonQuery();
            }
            tx.Commit();
            version = 44;
        }

        if (version != Schema.CurrentVersion)
            throw new InvalidOperationException(
                $"Database schema version {version} is not supported by this adapter (expected {Schema.CurrentVersion}). " +
                "A migration is required; none is registered for this version.");
    }

    // ------------------------------------------------------------------ ICompanyRepository

    /// <inheritdoc />
    public Company? Load(Guid companyId)
    {
        using var read = _connection.CreateCommand();
        read.CommandText = """
            SELECT id, name, mailing_name, address, country, state, pin,
                   financial_year_start, books_begin_from, base_currency_symbol,
                   base_currency_name, decimal_places, decimal_unit_name,
                   primary_cost_category, main_location, profit_and_loss_head_id,
                   gst_enabled, gstin, gst_home_state, gst_reg_type, gst_applicable_from, gst_periodicity,
                   use_separate_actual_billed_qty, enable_multiple_price_levels, enable_job_order_processing,
                   tds_enabled, tcs_enabled, tan, deductor_type, responsible_person_name, responsible_person_pan,
                   responsible_person_designation, responsible_person_address, surcharge_applicable,
                   cess_applicable, tds_periodicity, tds_applicable_from, tcs_applicable_from,
                   payroll_enabled, payroll_statutory_enabled,
                   pf_config_enabled, pf_epf_rate_bp, pf_establishment_code, pf_cap_at_ceiling,
                   esi_config_enabled, esi_ee_rate_bp, esi_er_rate_bp, esi_employer_code,
                   pt_config_enabled, pt_state, pt_registration_number, pt_wage_basis,
                   salary_tds_enabled,
                   gratuity_config_enabled, gratuity_cap_paisa, gratuity_wage_basis, gratuity_population,
                   bonus_config_enabled, bonus_rate_bp, bonus_calc_ceiling_paisa, bonus_minimum_wage_paisa, bonus_prorate,
                   composition_sub_type, composition_opt_in_date,
                   einvoicing_enabled, einvoice_applicable_from, einvoice_aato_threshold_paisa,
                   einvoice_applicability_override, einvoice_exemption_classes, einvoice_reporting_age_applies,
                   gst_connector_mode, b2c_dynamic_qr_enabled, b2c_qr_aato_threshold_paisa, b2c_qr_upi_id, b2c_qr_payee_name,
                   eway_bill_enabled, eway_applicable_from, eway_threshold_paisa, eway_consignment_basis, eway_intrastate_applicable,
                   recon_value_tolerance_paisa, recon_date_window_days
            FROM companies WHERE id = $id;
            """;
        read.Parameters.AddWithValue("$id", companyId.ToString("D"));

        Company company;
        Guid? plHeadId;
        using (var r = read.ExecuteReader())
        {
            if (!r.Read())
                return null;

            var fyStart = ParseDate(r.GetString(7));
            var books = ParseDate(r.GetString(8));
            company = new Company(companyId, r.GetString(1), fyStart, books)
            {
                MailingName = r.GetString(2),
                Address = r.IsDBNull(3) ? null : r.GetString(3),
                Country = r.GetString(4),
                State = r.IsDBNull(5) ? null : r.GetString(5),
                Pin = r.IsDBNull(6) ? null : r.GetString(6),
                BaseCurrencySymbol = r.GetString(9),
                BaseCurrencyName = r.GetString(10),
                DecimalPlaces = (int)r.GetInt64(11),
                DecimalUnitName = r.GetString(12),
                PrimaryCostCategoryName = r.GetString(13),
                MainLocationName = r.GetString(14),
                // v20 (RQ-22): F11 "Use separate Actual & Billed Quantity" — a plain persisted toggle, read verbatim.
                UseSeparateActualBilledQuantity = r.GetInt64(22) != 0,
                // v21 (RQ-26): F11 "Enable multiple Price Levels" — a plain persisted toggle, read verbatim.
                EnableMultiplePriceLevels = r.GetInt64(23) != 0,
                // v24 (RQ-45): F11 "Enable Job Order Processing" — a plain persisted toggle, read verbatim.
                EnableJobOrderProcessing = r.GetInt64(24) != 0,
                // v30 (Phase 8 slice 1): Payroll F11 toggles — plain persisted flags, read verbatim (default 0 — ER-13).
                PayrollEnabled = r.GetInt64(38) != 0,
                PayrollStatutoryEnabled = r.GetInt64(39) != 0,
            };
            plHeadId = r.IsDBNull(15) ? null : Guid.Parse(r.GetString(15));

            // v13 core GST config. A company is GST-enabled iff gst_enabled = 1; when off (default for every
            // pre-v13 company) Gst stays null and no GST path activates (ER-10).
            if (r.GetInt64(16) != 0)
            {
                company.Gst = new GstConfig
                {
                    Enabled = true,
                    Gstin = r.IsDBNull(17) ? null : r.GetString(17),
                    HomeStateCode = r.IsDBNull(18) ? null : r.GetString(18),
                    RegistrationType = r.IsDBNull(19) ? GstRegistrationType.Regular : (GstRegistrationType)(int)r.GetInt64(19),
                    ApplicableFrom = r.IsDBNull(20) ? (DateOnly?)null : ParseDate(r.GetString(20)),
                    Periodicity = r.IsDBNull(21) ? GstReturnPeriodicity.Monthly : (GstReturnPeriodicity)(int)r.GetInt64(21),
                    // v40 (Phase 9 slice 3): composition-scheme config — NULL unless the company is a composition dealer (ER-13).
                    CompositionSubType = r.IsDBNull(62) ? (CompositionSubType?)null : (CompositionSubType)(int)r.GetInt64(62),
                    CompositionOptInDate = r.IsDBNull(63) ? (DateOnly?)null : ParseDate(r.GetString(63)),
                    // v41 (Phase 9 slice 4a): NON-SECRET e-invoice / B2C-QR / connector-mode config, read verbatim
                    // (defaults ⇒ byte-identical when off, ER-13). The nic_*_enc credential BLOBs are DELIBERATELY NOT
                    // selected/read here (ER-16) — they flow only through INicCredentialStore.
                    EInvoicingEnabled = r.GetInt64(64) != 0,
                    EInvoiceApplicableFrom = r.IsDBNull(65) ? (DateOnly?)null : ParseDate(r.GetString(65)),
                    EInvoiceAatoThreshold = Paisa.ToMoney(r.GetInt64(66)),
                    EInvoiceApplicabilityOverride = r.GetInt64(67) != 0,
                    ExemptionClasses = (EInvoiceExemptionClass)(int)r.GetInt64(68),
                    ReportingAgeLimitApplies = r.GetInt64(69) != 0,
                    ConnectorMode = (GstConnectorMode)(int)r.GetInt64(70),
                    B2cDynamicQrEnabled = r.GetInt64(71) != 0,
                    B2cQrAatoThreshold = Paisa.ToMoney(r.GetInt64(72)),
                    B2cQrUpiId = r.IsDBNull(73) ? null : r.GetString(73),
                    B2cQrPayeeName = r.IsDBNull(74) ? null : r.GetString(74),
                    // v42 (Phase 9 slice 5): NON-SECRET e-Way Bill config, read verbatim (defaults ⇒ byte-identical when
                    // off, ER-13). The live NIC path reuses gst_connector_mode + nic_*_enc — no new secret column here.
                    EWayBillEnabled = r.GetInt64(75) != 0,
                    EWayApplicableFrom = r.IsDBNull(76) ? (DateOnly?)null : ParseDate(r.GetString(76)),
                    EWayThreshold = Paisa.ToMoney(r.GetInt64(77)),
                    ConsignmentBasis = (EWayConsignmentBasis)(int)r.GetInt64(78),
                    EWayIntraStateApplicable = r.GetInt64(79) != 0,
                    // v43 (Phase 9 slice 6): the GSTR-2B reconciliation tolerance, read verbatim (default 0/0 = exact ⇒
                    // byte-identical when off, ER-13; a matching parameter only, ER-14; finding #5).
                    ReconValueTolerance = Paisa.ToMoney(r.GetInt64(80)),
                    ReconDateWindowDays = (int)r.GetInt64(81),
                };
                foreach (var t in ReadEWayStateThresholds(companyId))
                    company.Gst.AddEWayStateThreshold(t);
            }

            // v25 (Phase 7 slice 1): TDS/TCS deductor config. The deductor identity (TAN/type/responsible person/
            // surcharge/cess/periodicity) is shared, stored once on the row, and read into whichever config(s) are
            // enabled. A non-TDS/TCS company (tds_enabled = tcs_enabled = 0) leaves both null (ER-13).
            var deductorType = r.IsDBNull(28) ? DeductorType.Company : (DeductorType)(int)r.GetInt64(28);
            var respName = r.IsDBNull(29) ? null : r.GetString(29);
            var respPan = r.IsDBNull(30) ? null : r.GetString(30);
            var respDesig = r.IsDBNull(31) ? null : r.GetString(31);
            var respAddr = r.IsDBNull(32) ? null : r.GetString(32);
            var surcharge = r.GetInt64(33) != 0;
            var cess = r.GetInt64(34) != 0;
            var periodicity = r.IsDBNull(35) ? TdsTcsPeriodicity.Quarterly : (TdsTcsPeriodicity)(int)r.GetInt64(35);
            var tan = r.IsDBNull(27) ? null : r.GetString(27);

            if (r.GetInt64(25) != 0)
                company.Tds = new TdsConfig
                {
                    Enabled = true, Tan = tan, DeductorType = deductorType,
                    ResponsiblePersonName = respName, ResponsiblePersonPan = respPan,
                    ResponsiblePersonDesignation = respDesig, ResponsiblePersonAddress = respAddr,
                    SurchargeApplicable = surcharge, CessApplicable = cess, Periodicity = periodicity,
                    ApplicableFrom = r.IsDBNull(36) ? (DateOnly?)null : ParseDate(r.GetString(36)),
                };
            if (r.GetInt64(26) != 0)
                company.Tcs = new TcsConfig
                {
                    Enabled = true, Tan = tan, CollectorType = deductorType,
                    ResponsiblePersonName = respName, ResponsiblePersonPan = respPan,
                    ResponsiblePersonDesignation = respDesig, ResponsiblePersonAddress = respAddr,
                    SurchargeApplicable = surcharge, CessApplicable = cess, Periodicity = periodicity,
                    ApplicableFrom = r.IsDBNull(37) ? (DateOnly?)null : ParseDate(r.GetString(37)),
                };

            // v33 (Phase 8 slice 4): establishment Provident-Fund config. Present iff pf_config_enabled = 1; when
            // off (default for a company not enrolled for PF) PfConfig stays null and no PF path activates (ER-13).
            if (r.GetInt64(40) != 0)
                company.PfConfig = new PfConfig(
                    (int)r.GetInt64(41),
                    r.IsDBNull(42) ? null : r.GetString(42),
                    r.GetInt64(43) != 0);

            // v34 (Phase 8 slice 5): establishment ESI config. Present iff esi_config_enabled = 1; when off (default
            // for a company not enrolled for ESI) EsiConfig stays null and no ESI path activates (ER-13).
            if (r.GetInt64(44) != 0)
                company.EsiConfig = new EsiConfig(
                    (int)r.GetInt64(45),
                    (int)r.GetInt64(46),
                    r.IsDBNull(47) ? null : r.GetString(47));

            // v35 (Phase 8 slice 6): establishment Professional-Tax config. Present iff pt_config_enabled = 1; when off
            // (default for a company not enrolled for PT) PtConfig stays null and no PT path activates (ER-13). The
            // per-state slab tables are hydrated from pt_slab_bands below.
            if (r.GetInt64(48) != 0)
                company.PtConfig = new PtConfig(
                    r.IsDBNull(49) ? null : r.GetString(49),
                    r.IsDBNull(50) ? null : r.GetString(50),
                    (PtWageBasis)(int)r.GetInt64(51));

            // v36 (Phase 8 slice 7): establishment §192 salary-TDS toggle. Off (default) for a company that never
            // deducts salary-TDS (ER-13); per-employee declarations are hydrated from employee_tax_declarations below.
            company.SalaryTdsEnabled = r.GetInt64(52) != 0;

            // v37 (Phase 8 slice 9): establishment Gratuity config. Present iff gratuity_config_enabled = 1; when off
            // (default for a company that does not provision) GratuityConfig stays null and no gratuity path activates (ER-13).
            if (r.GetInt64(53) != 0)
                company.GratuityConfig = new GratuityConfig(
                    new Money(r.GetInt64(54) / 100m),
                    (GratuityWageBasis)(int)r.GetInt64(55),
                    (GratuityProvisionPopulation)(int)r.GetInt64(56));

            // v37 (Phase 8 slice 9): establishment statutory-Bonus config. Present iff bonus_config_enabled = 1; when
            // off (default) BonusConfig stays null and no bonus path activates (ER-13).
            if (r.GetInt64(57) != 0)
                company.BonusConfig = new BonusConfig(
                    (int)r.GetInt64(58),
                    new Money(r.GetInt64(59) / 100m),
                    new Money(r.GetInt64(60) / 100m),
                    r.GetInt64(61) != 0);
        }

        // v13 GST rate slabs (only present when GST was enabled). Loaded into the config's slab set.
        if (company.Gst is not null)
        {
            foreach (var slab in ReadGstRateSlabs(companyId))
                company.Gst.AddRateSlab(slab);
            // v38 (Phase 9 slice 1): dated rate-history + Compensation-Cess windows (empty when advanced GST is off).
            foreach (var entry in ReadGstRateHistory(companyId))
                company.Gst.AddRateHistory(entry);
            foreach (var rate in ReadGstCessRates(companyId))
                company.Gst.AddCessRate(rate);
            // v39 (Phase 9 slice 2): dated reverse-charge categories (empty when RCM is off).
            foreach (var cat in ReadRcmCategories(companyId))
                company.Gst.AddRcmCategory(cat);
        }

        // v25 TDS/TCS masters (only present when the respective feature was enabled). Loaded into the config.
        if (company.Tds is not null)
            foreach (var nature in ReadNaturesOfPayment(companyId))
                company.Tds.AddNatureOfPayment(nature);
        if (company.Tcs is not null)
            foreach (var nature in ReadNaturesOfGoods(companyId))
                company.Tcs.AddNatureOfGoods(nature);

        // v35 PT slab tables (only present when PT was enrolled). Bands are grouped by (slab_id) into PtSlab tables.
        if (company.PtConfig is not null)
            foreach (var slab in ReadPtSlabTables(companyId))
                company.PtConfig.AddSlabTable(slab);

        // v36 per-employee §192 income-tax declarations (empty for a company with no declarations — ER-13).
        foreach (var declaration in ReadTaxDeclarations(companyId))
            company.AddTaxDeclaration(declaration);

        // Groups: the reserved P&L head (is_pl_head = 1) is registered via SetProfitAndLossHead and
        // kept OUT of Company.Groups; the 28 (and any custom) groups go into Company.Groups. Load
        // reads ALL rows (including the head) and routes the head aside by its id.
        foreach (var g in ReadGroupRows(companyId, includePlHead: true))
        {
            if (plHeadId is not null && g.Id == plHeadId.Value)
                company.SetProfitAndLossHead(g);
            else
                company.AddGroup(g);
        }

        // Currencies + rates before ledgers/vouchers: ledgers.currency_id and entry-line forex reference them.
        foreach (var cur in ReadCurrencies(companyId))
            company.AddCurrency(cur);
        foreach (var rate in ReadExchangeRates(companyId))
            company.AddExchangeRate(rate);

        foreach (var l in ReadLedgers(companyId))
            company.AddLedger(l);

        foreach (var t in ReadVoucherTypes(companyId))
            company.AddVoucherType(t);

        // Cost categories then centres (centres reference categories; both must exist before vouchers
        // are re-posted, since the validator checks a line's cost allocations against them).
        foreach (var cat in ReadCostCategories(companyId))
            company.AddCostCategory(cat);
        foreach (var centre in ReadCostCentres(companyId))
            company.AddCostCentre(centre);

        // Inventory masters (catalog §9). Order matters: units first (a compound unit + a stock item
        // reference simple units), then stock groups + categories + godowns, then stock items (reference
        // group/category/unit), then opening balances (reference items + godowns).
        foreach (var u in ReadUnits(companyId))
            company.AddUnit(u);
        foreach (var g in ReadStockGroups(companyId))
            company.AddStockGroup(g);
        foreach (var cat in ReadStockCategories(companyId))
            company.AddStockCategory(cat);
        foreach (var g in ReadGodowns(companyId))
            company.AddGodown(g);
        foreach (var item in ReadStockItems(companyId))
            company.AddStockItem(item);
        // Batch masters after items + godowns (they reference both), before opening balances.
        foreach (var bm in ReadBatchMasters(companyId))
            company.AddBatchMaster(bm);
        // Bill-of-Materials masters after items + godowns (a BOM header + line reference both). The finished
        // good's Set-Components flag is persisted verbatim on the item (loaded above), so it is NOT re-derived here.
        foreach (var bom in ReadBillsOfMaterials(companyId))
            company.AddBillOfMaterials(bom);
        // Price levels (bare masters) then price lists (reference levels + stock items, both loaded above) —
        // Phase 6 slice 5. The party ledger's DefaultPriceLevelId was read verbatim with the ledger above.
        foreach (var level in ReadPriceLevels(companyId))
            company.AddPriceLevel(level);
        foreach (var list in ReadPriceLists(companyId))
            company.AddPriceList(list);
        // Reorder definitions after stock items/groups/categories (they target one of the three, all loaded above)
        // — Phase 6 slice 6.
        foreach (var def in ReadReorderDefinitions(companyId))
            company.AddReorderDefinition(def);
        foreach (var ob in ReadStockOpeningBalances(companyId))
            company.AddStockOpeningBalance(ob);

        // Inventory & order vouchers (catalog §10): rehydrated directly (the store is trusted; posting guards
        // ran when they were first accepted). They reference stock items/godowns/units + a party ledger.
        foreach (var iv in ReadInventoryVouchers(companyId))
            company.AddInventoryVoucher(iv);

        // Vouchers: re-post through the engine (real posting path). The stored number is preserved
        // because Post only assigns a number when the voucher's number is unset (≤ 0).
        var service = new LedgerService(company);
        foreach (var v in ReadVouchers(companyId))
            service.Post(v);

        // Budgets (catalog §7): masters that reference groups/ledgers already loaded above.
        foreach (var b in ReadBudgets(companyId))
            company.AddBudget(b);

        // Scenarios (catalog §7): masters whose include/exclude lists reference voucher types loaded above.
        foreach (var s in ReadScenarios(companyId))
            company.AddScenario(s);

        // TDS deposit challans + their voucher links (Phase 7 slice 3): challans + the challan↔stat-payment-voucher
        // link set, loaded after vouchers so the links reference real posted vouchers.
        foreach (var ch in ReadTdsChallans(companyId))
            company.AddTdsChallan(ch);
        foreach (var (challanId, voucherId) in ReadChallanVoucherLinks(companyId))
            company.LinkChallanToVoucher(challanId, voucherId);

        // TCS deposit challans + their voucher links (Phase 7 slice 6): the exact sibling of the TDS challan set,
        // loaded after vouchers so the links reference real posted vouchers.
        foreach (var ch in ReadTcsChallans(companyId))
            company.AddTcsChallan(ch);
        foreach (var (challanId, voucherId) in ReadTcsChallanVoucherLinks(companyId))
            company.LinkTcsChallanToVoucher(challanId, voucherId);

        // RCM generated documents + §34-CDN links + GST-on-advance receipts (Phase 9 slice 2). Loaded after vouchers so
        // their source-voucher references resolve. The CDN/advance sets stay empty until S2b (ER-13).
        foreach (var doc in ReadRcmDocuments(companyId))
            company.AddRcmDocument(doc);
        // e-Invoice IRP artefacts (Phase 9 slice 4a). Loaded after vouchers so their source-voucher references resolve;
        // empty when e-invoicing is unused (ER-13).
        foreach (var record in ReadEInvoiceRecords(companyId))
            company.AddEInvoiceRecord(record);
        // e-Way Bill artefacts (Phase 9 slice 5). Loaded after vouchers so their source-voucher references resolve; empty
        // when e-Way is unused (ER-13). (The per-state threshold overrides load with the GstConfig, above.)
        foreach (var record in ReadEWayBillRecords(companyId))
            company.AddEWayBillRecord(record);
        foreach (var link in ReadGstCdnLinks(companyId))
            company.AddCreditDebitNoteLink(link);
        foreach (var adv in ReadGstAdvanceReceipts(companyId))
            company.AddAdvanceReceipt(adv);
        // Imported GSTR-2B/2A snapshots (owning their lines) + reconciliation results (Phase 9 slice 6). Snapshots load
        // first (they own the lines); the recon results reference the loaded lines by id + an optional matched voucher.
        // Empty when 2B is never imported (ER-13).
        foreach (var snapshot in ReadGstr2bSnapshots(companyId))
            company.AddGstr2bSnapshot(snapshot);
        foreach (var result in ReadGstr2bReconResults(companyId))
            company.AddGstr2bReconResult(result);
        // Offline IMS decisions (Phase 9 slice 6b): load AFTER the snapshots (they key the 2B lines). Empty until the
        // user acts (a line with no row reads deemed-accepted, ER-13).
        foreach (var action in ReadImsStatus(companyId))
            company.AddImsAction(action);
        // Electronic-ledger set-off / reversal / challan / DRC-03 records (Phase 9 slice 7). Loaded after vouchers so
        // their voucher references resolve; empty when the company never sets off / pays / reverses (ER-13).
        foreach (var line in ReadGstSetoffLines(companyId))
            company.AddGstSetoffLine(line);
        foreach (var challan in ReadGstChallans(companyId))
            company.AddGstChallan(challan);
        foreach (var drc03 in ReadGstDrc03s(companyId))
            company.AddGstDrc03(drc03);
        foreach (var reversal in ReadItcReversals(companyId))
            company.AddItcReversal(reversal);

        // Payroll masters (Phase 8 slice 1). Order: categories + groups + payroll units first, then attendance
        // types (reference payroll units) and employees (reference groups + categories). Empty when Payroll off.
        foreach (var cat in ReadEmployeeCategories(companyId))
            company.AddEmployeeCategory(cat);
        foreach (var g in ReadEmployeeGroups(companyId))
            company.AddEmployeeGroup(g);
        foreach (var u in ReadPayrollUnits(companyId))
            company.AddPayrollUnit(u);
        foreach (var a in ReadAttendanceTypes(companyId))
            company.AddAttendanceType(a);
        foreach (var e in ReadEmployees(companyId))
            company.AddEmployee(e);

        // Pay heads (Phase 8 slice 2): the master rows first, then their computed-on basis + slab child rows are
        // folded in by ReadPayHeads. Then dated salary structures (+ their lines), which FK pay heads.
        foreach (var ph in ReadPayHeads(companyId))
            company.AddPayHead(ph);
        foreach (var s in ReadSalaryStructures(companyId))
            company.AddSalaryStructure(s);

        // Attendance entries (Phase 8 slice 3): recorded attendance/production values, FK employees + attendance types.
        foreach (var a in ReadAttendanceEntries(companyId))
            company.AddAttendanceEntry(a);

        return company;
    }

    private IEnumerable<AttendanceEntry> ReadAttendanceEntries(Guid companyId)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            SELECT id, employee_id, attendance_type_id, from_date, to_date, value_micro
            FROM attendance_entries WHERE company_id = $cid ORDER BY rowid;
            """;
        cmd.Parameters.AddWithValue("$cid", companyId.ToString("D"));
        using var r = cmd.ExecuteReader();
        var list = new List<AttendanceEntry>();
        while (r.Read())
            list.Add(new AttendanceEntry(
                Guid.Parse(r.GetString(0)),
                Guid.Parse(r.GetString(1)),
                Guid.Parse(r.GetString(2)),
                ParseDate(r.GetString(3)),
                ParseDate(r.GetString(4)),
                r.GetInt64(5) / 1_000_000m));
        return list;
    }

    private IEnumerable<TdsChallan> ReadTdsChallans(Guid companyId)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            SELECT id, challan_no, bsr_code, deposit_date, amount_micro, section, minor_head
            FROM tds_challans WHERE company_id = $cid ORDER BY rowid;
            """;
        cmd.Parameters.AddWithValue("$cid", companyId.ToString("D"));
        using var r = cmd.ExecuteReader();
        var list = new List<TdsChallan>();
        while (r.Read())
            list.Add(new TdsChallan(
                Guid.Parse(r.GetString(0)), r.GetString(1), r.GetString(2), ParseDate(r.GetString(3)),
                new Money(r.GetInt64(4) / 1_000_000m), r.GetString(5), r.GetString(6)));
        return list;
    }

    private IEnumerable<(Guid ChallanId, Guid VoucherId)> ReadChallanVoucherLinks(Guid companyId)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            SELECT challan_id, voucher_id FROM challan_voucher_links
            WHERE challan_id IN (SELECT id FROM tds_challans WHERE company_id = $cid) ORDER BY id;
            """;
        cmd.Parameters.AddWithValue("$cid", companyId.ToString("D"));
        using var r = cmd.ExecuteReader();
        var list = new List<(Guid, Guid)>();
        while (r.Read())
            list.Add((Guid.Parse(r.GetString(0)), Guid.Parse(r.GetString(1))));
        return list;
    }

    private IEnumerable<TcsChallan> ReadTcsChallans(Guid companyId)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            SELECT id, challan_no, bsr_code, deposit_date, amount_micro, collection_code, minor_head
            FROM tcs_challans WHERE company_id = $cid ORDER BY rowid;
            """;
        cmd.Parameters.AddWithValue("$cid", companyId.ToString("D"));
        using var r = cmd.ExecuteReader();
        var list = new List<TcsChallan>();
        while (r.Read())
            list.Add(new TcsChallan(
                Guid.Parse(r.GetString(0)), r.GetString(1), r.GetString(2), ParseDate(r.GetString(3)),
                new Money(r.GetInt64(4) / 1_000_000m), r.GetString(5), r.GetString(6)));
        return list;
    }

    private IEnumerable<(Guid ChallanId, Guid VoucherId)> ReadTcsChallanVoucherLinks(Guid companyId)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            SELECT challan_id, voucher_id FROM tcs_challan_voucher_links
            WHERE challan_id IN (SELECT id FROM tcs_challans WHERE company_id = $cid) ORDER BY id;
            """;
        cmd.Parameters.AddWithValue("$cid", companyId.ToString("D"));
        using var r = cmd.ExecuteReader();
        var list = new List<(Guid, Guid)>();
        while (r.Read())
            list.Add((Guid.Parse(r.GetString(0)), Guid.Parse(r.GetString(1))));
        return list;
    }

    /// <summary>
    /// Reads the ids and names of all companies stored in this database file. In the one-db-per-company
    /// model this is normally a single row, but the method returns all rows so a UI can list them without
    /// knowing the company id in advance.
    /// </summary>
    public IReadOnlyList<(Guid Id, string Name)> ListCompanies()
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT id, name FROM companies ORDER BY rowid;";
        using var r = cmd.ExecuteReader();
        var list = new List<(Guid, string)>();
        while (r.Read())
            list.Add((Guid.Parse(r.GetString(0)), r.GetString(1)));
        return list;
    }

    /// <inheritdoc />
    public void Save(Company company)
    {
        if (company is null) throw new ArgumentNullException(nameof(company));

        using var tx = _connection.BeginTransaction();

        DeleteCompanyRows(tx, company.Id);

        // Company row is written after its groups so the profit_and_loss_head_id FK resolves; but the
        // groups reference companies(id), so insert the company row first WITHOUT the head fk, then
        // patch the head id once the head group row exists.
        InsertCompany(tx, company);
        InsertGroups(tx, company);
        SetProfitAndLossHead(tx, company);
        // Currencies + rates before ledgers (ledgers.currency_id FK currencies) and before vouchers
        // (entry-line forex FK currencies).
        InsertCurrencies(tx, company);
        InsertExchangeRates(tx, company);
        // Price levels before ledgers: a party ledger's default_price_level_id FK references price_levels (slice 5).
        InsertPriceLevels(tx, company);
        InsertLedgers(tx, company);
        InsertVoucherTypes(tx, company);
        // Cost categories before centres (centres FK categories); both before vouchers (cost allocations
        // FK both).
        InsertCostCategories(tx, company);
        InsertCostCentres(tx, company);
        // Inventory masters (catalog §9). Units first (a compound unit references simple units; a stock item
        // references a unit); then groups + categories + godowns; then stock items; then opening balances.
        InsertUnits(tx, company);
        InsertStockGroups(tx, company);
        InsertStockCategories(tx, company);
        InsertGodowns(tx, company);
        // POS voucher-type config (v23): its FKs reference voucher_types (above), ledgers (above) and godowns
        // (just inserted) — so insert it here, after godowns exist (Phase 6 slice 7).
        InsertPosVoucherTypeConfig(tx, company);
        InsertStockItems(tx, company);
        // Batch masters after stock items (FK stock_items) + godowns; before opening balances (a batch_id on an
        // opening layer would FK batch_masters — even though we do not populate batch_id today, keep the order safe).
        InsertBatchMasters(tx, company);
        // Bill-of-Materials masters after stock items + godowns (a BOM header + its lines FK both).
        InsertBillsOfMaterials(tx, company);
        // Price lists after stock items (a price_lists header FKs stock_items) + price levels (inserted above).
        InsertPriceLists(tx, company);
        // Reorder definitions after stock items/groups/categories (they target one of the three) — Phase 6 slice 6.
        InsertReorderDefinitions(tx, company);
        InsertStockOpeningBalances(tx, company);
        InsertVouchers(tx, company);
        // Inventory & order vouchers (catalog §10): reference voucher_types, stock_items, godowns, units, and
        // (optionally) a party ledger — all inserted above.
        InsertInventoryVouchers(tx, company);
        // Budgets last: their lines FK groups and ledgers, both already inserted.
        InsertBudgets(tx, company);
        // Scenarios: their voucher-type rows FK voucher_types, already inserted.
        InsertScenarios(tx, company);
        // TDS deposit challans + their voucher links (Phase 7 slice 3): challans FK companies; a link FKs a challan +
        // a (stat-payment) voucher — both inserted above, so insert challans then links last.
        InsertTdsChallans(tx, company);
        // TCS deposit challans + their voucher links (Phase 7 slice 6): the exact sibling of the TDS challan set.
        InsertTcsChallans(tx, company);
        // RCM generated documents + §34-CDN links + GST-on-advance receipts (Phase 9 slice 2): all FK vouchers (source /
        // cdn / receipt / adjustment / refund), inserted above, so insert them here.
        InsertRcmRecords(tx, company);
        // e-Invoice IRP artefacts (Phase 9 slice 4a): FK the source voucher (inserted above).
        InsertEInvoiceRecords(tx, company);
        // e-Way Bill artefacts (Phase 9 slice 5): FK the source voucher (inserted above).
        InsertEWayBillRecords(tx, company);
        // Imported GSTR-2B/2A snapshots + lines + reconciliation results (Phase 9 slice 6): snapshots/lines are external
        // data (no source-voucher FK); the recon rows FK the lines + an optional matched voucher (inserted above).
        InsertGstr2bRecords(tx, company);
        // Electronic-ledger set-off / reversal / challan / DRC-03 records (Phase 9 slice 7): FK the posted vouchers
        // (inserted above). All-rows-replace on save (the child-cleanup DELETEs above ran first).
        InsertGstSetOffRecords(tx, company);
        // Payroll masters (Phase 8 slice 1). Categories + groups + payroll units first; then attendance types
        // (FK payroll_units) and employees (FK employee_groups + employee_categories). Hierarchical/compound
        // masters are inserted parents-before-children so their self-FK resolves.
        InsertEmployeeCategories(tx, company);
        InsertEmployeeGroups(tx, company);
        InsertPayrollUnits(tx, company);
        InsertAttendanceTypes(tx, company);
        InsertEmployees(tx, company);
        // Pay heads (Phase 8 slice 2): all master rows first (they FK groups/ledgers/attendance_types, inserted
        // above), then the computed-on basis + slab child rows (which FK pay_heads on both ends), then dated salary
        // structures + their lines (FK pay_heads). Inserting every pay-head row before any computation row means a
        // computed-on reference to another pay head is always already present.
        InsertPayHeads(tx, company);
        InsertSalaryStructures(tx, company);
        // Attendance entries (Phase 8 slice 3): recorded attendance/production values — FK employees + attendance
        // types (both inserted above). The Payroll voucher's payroll_lines are written by InsertVouchers above.
        InsertAttendanceEntries(tx, company);

        tx.Commit();
    }

    // ------------------------------------------------------------------ IMasterRepository

    /// <inheritdoc />
    public IReadOnlyList<Group> GetGroups(Guid companyId) => ReadGroups(companyId).ToList();

    /// <inheritdoc />
    public IReadOnlyList<Apex.Ledger.Domain.Ledger> GetLedgers(Guid companyId) => ReadLedgers(companyId).ToList();

    /// <inheritdoc />
    public IReadOnlyList<VoucherType> GetVoucherTypes(Guid companyId) => ReadVoucherTypes(companyId).ToList();

    // ------------------------------------------------------------------ IVoucherRepository

    /// <inheritdoc />
    public IReadOnlyList<Voucher> GetAll(Guid companyId) => ReadVouchers(companyId).ToList();

    /// <inheritdoc />
    public void Add(Guid companyId, Voucher voucher)
    {
        if (voucher is null) throw new ArgumentNullException(nameof(voucher));
        using var tx = _connection.BeginTransaction();
        InsertVoucher(tx, companyId, voucher);
        tx.Commit();
    }

    /// <inheritdoc />
    public void Remove(Guid companyId, Guid voucherId)
    {
        using var tx = _connection.BeginTransaction();
        using (var delAllocs = _connection.CreateCommand())
        {
            delAllocs.Transaction = tx;
            delAllocs.CommandText = """
                DELETE FROM bill_allocations WHERE entry_line_id IN (
                    SELECT id FROM entry_lines WHERE voucher_id = $vid);
                """;
            delAllocs.Parameters.AddWithValue("$vid", voucherId.ToString("D"));
            delAllocs.ExecuteNonQuery();
        }
        using (var delCostAllocs = _connection.CreateCommand())
        {
            delCostAllocs.Transaction = tx;
            delCostAllocs.CommandText = """
                DELETE FROM cost_allocations WHERE entry_line_id IN (
                    SELECT id FROM entry_lines WHERE voucher_id = $vid);
                """;
            delCostAllocs.Parameters.AddWithValue("$vid", voucherId.ToString("D"));
            delCostAllocs.ExecuteNonQuery();
        }
        using (var delBankAllocs = _connection.CreateCommand())
        {
            delBankAllocs.Transaction = tx;
            delBankAllocs.CommandText = """
                DELETE FROM bank_allocations WHERE entry_line_id IN (
                    SELECT id FROM entry_lines WHERE voucher_id = $vid);
                """;
            delBankAllocs.Parameters.AddWithValue("$vid", voucherId.ToString("D"));
            delBankAllocs.ExecuteNonQuery();
        }
        using (var delLines = _connection.CreateCommand())
        {
            delLines.Transaction = tx;
            delLines.CommandText = "DELETE FROM entry_lines WHERE voucher_id = $vid;";
            delLines.Parameters.AddWithValue("$vid", voucherId.ToString("D"));
            delLines.ExecuteNonQuery();
        }
        using (var delV = _connection.CreateCommand())
        {
            delV.Transaction = tx;
            delV.CommandText = "DELETE FROM vouchers WHERE id = $vid AND company_id = $cid;";
            delV.Parameters.AddWithValue("$vid", voucherId.ToString("D"));
            delV.Parameters.AddWithValue("$cid", companyId.ToString("D"));
            delV.ExecuteNonQuery();
        }
        tx.Commit();
    }

    // ------------------------------------------------------------------ readers

    /// <summary>
    /// Reads the normal group masters — the 28 predefined groups plus any custom ones — mirroring
    /// <see cref="Company.Groups"/>. The reserved Profit &amp; Loss head (<c>is_pl_head = 1</c>) is
    /// deliberately excluded; it is not one of the 28 and is surfaced only via
    /// <see cref="Company.ProfitAndLossHead"/> on a full <see cref="Load"/>.
    /// </summary>
    private IEnumerable<Group> ReadGroups(Guid companyId) => ReadGroupRows(companyId, includePlHead: false);

    private IEnumerable<Group> ReadGroupRows(Guid companyId, bool includePlHead)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = includePlHead
            ? """
              SELECT id, name, nature, parent_id, alias, is_predefined
              FROM groups WHERE company_id = $cid ORDER BY rowid;
              """
            : """
              SELECT id, name, nature, parent_id, alias, is_predefined
              FROM groups WHERE company_id = $cid AND is_pl_head = 0 ORDER BY rowid;
              """;
        cmd.Parameters.AddWithValue("$cid", companyId.ToString("D"));
        using var r = cmd.ExecuteReader();
        var list = new List<Group>();
        while (r.Read())
        {
            var parent = r.IsDBNull(3) ? (Guid?)null : Guid.Parse(r.GetString(3));
            list.Add(new Group(
                Guid.Parse(r.GetString(0)),
                r.GetString(1),
                (GroupNature)(int)r.GetInt64(2),
                parentId: parent,
                alias: r.IsDBNull(4) ? null : r.GetString(4),
                isPredefined: r.GetInt64(5) != 0));
        }
        return list;
    }

    private IEnumerable<Apex.Ledger.Domain.Ledger> ReadLedgers(Guid companyId)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            SELECT id, name, group_id, opening_balance_paisa, opening_is_debit, alias, is_predefined,
                   maintain_bill_by_bill, default_credit_period, cost_applicable,
                   enable_cheque_printing, cheque_bank_name,
                   interest_enabled, interest_rate_millis, interest_per, interest_on_balance,
                   interest_applicability, interest_calc_from, interest_style,
                   interest_round_method, interest_round_decimals, currency_id,
                   party_gst_reg_type, party_gstin, party_gst_state,
                   sp_gst_hsn, sp_gst_taxability, sp_gst_rate_bp, sp_gst_supply_type,
                   gst_tax_head, gst_tax_direction, method_of_appropriation, default_price_level_id,
                   tds_applicable, tds_nature_id, deductee_type, party_pan, deduct_in_same_voucher,
                   tcs_applicable, tcs_nature_id, collectee_type, tds_tcs_class_kind,
                   sp_gst_valuation_basis, sp_cess_applicable, sp_cess_valuation_mode, sp_cess_rate_bp,
                   sp_cess_per_unit_paisa, sp_cess_rsp_factor_millis, sp_rsp_paisa,
                   sp_reverse_charge_applicable, sp_gta_forward_charge, sp_rcm_category_id,
                   party_is_promoter, party_is_body_corporate, gst_class_reverse_charge,
                   itc_eligibility, blocked_credit_category
            FROM ledgers WHERE company_id = $cid ORDER BY rowid;
            """;
        cmd.Parameters.AddWithValue("$cid", companyId.ToString("D"));
        using var r = cmd.ExecuteReader();
        var list = new List<Apex.Ledger.Domain.Ledger>();
        while (r.Read())
        {
            list.Add(new Apex.Ledger.Domain.Ledger(
                Guid.Parse(r.GetString(0)),
                r.GetString(1),
                Guid.Parse(r.GetString(2)),
                Paisa.ToMoney(r.GetInt64(3)),
                openingIsDebit: r.GetInt64(4) != 0,
                alias: r.IsDBNull(5) ? null : r.GetString(5),
                isPredefined: r.GetInt64(6) != 0,
                maintainBillByBill: r.GetInt64(7) != 0,
                defaultCreditPeriodDays: r.IsDBNull(8) ? (int?)null : (int)r.GetInt64(8),
                costCentresApplicable: r.IsDBNull(9) ? (bool?)null : r.GetInt64(9) != 0,
                enableChequePrinting: r.GetInt64(10) != 0,
                chequePrintingBankName: r.IsDBNull(11) ? null : r.GetString(11),
                interest: ReadInterest(r),
                currencyId: r.IsDBNull(21) ? (Guid?)null : Guid.Parse(r.GetString(21)),
                partyGst: ReadPartyGst(r),
                salesPurchaseGst: ReadSalesPurchaseGst(r),
                gstClassification: ReadLedgerGstClassification(r),
                // v19 (RQ-16..RQ-20): method of appropriation (NULL = plain P&L ledger, not additional-cost).
                methodOfAppropriation: r.IsDBNull(31) ? (MethodOfAppropriation?)null : (MethodOfAppropriation)(int)r.GetInt64(31),
                // v21 (RQ-30): a party ledger's default Price Level (NULL = no default level).
                defaultPriceLevelId: r.IsDBNull(32) ? (Guid?)null : Guid.Parse(r.GetString(32)))
            {
                // v25 (Phase 7 slice 1): TDS/TCS applicability flags + party PAN + payable classification (all
                // read verbatim; default off/null for every existing ledger, ER-13).
                TdsApplicable = r.GetInt64(33) != 0,
                TdsNatureOfPaymentId = r.IsDBNull(34) ? (Guid?)null : Guid.Parse(r.GetString(34)),
                DeducteeType = r.IsDBNull(35) ? (DeducteeType?)null : (DeducteeType)(int)r.GetInt64(35),
                PartyPan = r.IsDBNull(36) ? null : r.GetString(36),
                DeductTdsInSameVoucher = r.GetInt64(37) != 0,
                TcsApplicable = r.GetInt64(38) != 0,
                TcsNatureOfGoodsId = r.IsDBNull(39) ? (Guid?)null : Guid.Parse(r.GetString(39)),
                CollecteeType = r.IsDBNull(40) ? (CollecteeType?)null : (CollecteeType)(int)r.GetInt64(40),
                TdsTcsClassification = r.IsDBNull(41) ? (TdsTcsLedgerKind?)null : (TdsTcsLedgerKind)(int)r.GetInt64(41),
            });
        }
        return list;
    }

    /// <summary>Reads the party GST block (columns 22–24 + v39 RCM qualifiers 52–53), or <c>null</c> when the ledger has
    /// no party GST. A ledger carrying only a v39 RCM qualifier (no reg-type/gstin/state) still materialises a block so
    /// the flag round-trips.</summary>
    private static PartyGstDetails? ReadPartyGst(SqliteDataReader r)
    {
        var promoter = r.GetInt64(52) != 0;
        var bodyCorporate = r.GetInt64(53) != 0;
        // Present iff any of reg-type / gstin / state / v39 RCM qualifier is set.
        if (r.IsDBNull(22) && r.IsDBNull(23) && r.IsDBNull(24) && !promoter && !bodyCorporate) return null;
        return new PartyGstDetails
        {
            RegistrationType = r.IsDBNull(22) ? GstRegistrationType.Unregistered : (GstRegistrationType)(int)r.GetInt64(22),
            Gstin = r.IsDBNull(23) ? null : r.GetString(23),
            StateCode = r.IsDBNull(24) ? null : r.GetString(24),
            // v39 (Phase 9 slice 2): reverse-charge qualifiers (default off).
            IsPromoter = promoter,
            IsBodyCorporate = bodyCorporate,
        };
    }

    /// <summary>Reads the sales/purchase GST block (columns 25–28 + v38 cess/RSP columns 42–48), or <c>null</c> when
    /// taxability is NULL.</summary>
    private static StockItemGstDetails? ReadSalesPurchaseGst(SqliteDataReader r)
    {
        if (r.IsDBNull(26)) return null; // sp_gst_taxability NULL = no block
        return new StockItemGstDetails
        {
            HsnSac = r.IsDBNull(25) ? null : r.GetString(25),
            Taxability = (GstTaxability)(int)r.GetInt64(26),
            RateBasisPoints = r.IsDBNull(27) ? (int?)null : (int)r.GetInt64(27),
            SupplyType = r.IsDBNull(28) ? GstSupplyType.Goods : (GstSupplyType)(int)r.GetInt64(28),
            // v38 (Phase 9 slice 1): GST 2.0 RSP valuation + Compensation-Cess (default off/null for a plain ledger).
            ValuationBasis = (GstValuationBasis)(int)r.GetInt64(42),
            CessApplicable = r.GetInt64(43) != 0,
            CessValuationMode = r.IsDBNull(44) ? (CessValuationMode?)null : (CessValuationMode)(int)r.GetInt64(44),
            CessRateBasisPoints = r.IsDBNull(45) ? (int?)null : (int)r.GetInt64(45),
            CessPerUnit = r.IsDBNull(46) ? (Money?)null : Paisa.ToMoney(r.GetInt64(46)),
            CessRspFactorMillis = r.IsDBNull(47) ? (int?)null : (int)r.GetInt64(47),
            RetailSalePrice = r.IsDBNull(48) ? (Money?)null : Paisa.ToMoney(r.GetInt64(48)),
            // v39 (Phase 9 slice 2): reverse-charge flags on the sales/purchase-ledger block (default off/null).
            ReverseChargeApplicable = r.GetInt64(49) != 0,
            GtaForwardCharge = r.GetInt64(50) != 0,
            RcmCategoryId = r.IsDBNull(51) ? (Guid?)null : Guid.Parse(r.GetString(51)),
            // v43 (Phase 9 slice 6): §17(5) ITC-eligibility (columns 55–56; default Eligible/None for a plain ledger).
            ItcEligibility = (ItcEligibility)(int)r.GetInt64(55),
            BlockedCreditCategory = (BlockedCreditCategory)(int)r.GetInt64(56),
        };
    }

    /// <summary>Reads the tax-ledger classification (columns 29–30 + v39 reverse-charge discriminator 54), or <c>null</c>
    /// for an ordinary ledger.</summary>
    private static LedgerGstClassification? ReadLedgerGstClassification(SqliteDataReader r)
    {
        if (r.IsDBNull(29) || r.IsDBNull(30)) return null;
        return new LedgerGstClassification(
            (GstTaxHead)(int)r.GetInt64(29), (GstTaxDirection)(int)r.GetInt64(30), isReverseCharge: r.GetInt64(54) != 0);
    }

    /// <summary>
    /// Reads the interest block from the current ledger row (columns 12–20), or <c>null</c> when the ledger
    /// carries no block (<c>interest_enabled IS NULL</c>). Rate is stored as millis (rate% × 1000).
    /// </summary>
    private static InterestParameters? ReadInterest(SqliteDataReader r)
    {
        if (r.IsDBNull(12)) return null; // no interest block at all

        return new InterestParameters(
            enabled: r.GetInt64(12) != 0,
            ratePercent: r.GetInt64(13) / 1000m,
            per: (InterestPer)(int)r.GetInt64(14),
            onBalance: (InterestOnBalance)(int)r.GetInt64(15),
            applicability: (InterestApplicability)(int)r.GetInt64(16),
            calculateFrom: r.IsDBNull(17) ? (DateOnly?)null : ParseDate(r.GetString(17)),
            style: (InterestStyle)(int)r.GetInt64(18),
            roundingMethod: (InterestRoundingMethod)(int)r.GetInt64(19),
            roundingDecimals: (int)r.GetInt64(20));
    }

    private IEnumerable<VoucherType> ReadVoucherTypes(Guid companyId)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            SELECT id, name, base_type, default_shortcut, numbering, abbreviation, is_active, is_predefined,
                   affects_accounts, affects_stock, use_as_manufacturing_journal, track_additional_costs,
                   allow_zero_valued, use_for_pos, use_for_job_work, allow_consumption, is_stat_payment,
                   is_rcm_payment_voucher, is_gst_stat_adjustment
            FROM voucher_types WHERE company_id = $cid ORDER BY rowid;
            """;
        cmd.Parameters.AddWithValue("$cid", companyId.ToString("D"));
        using var r = cmd.ExecuteReader();
        var list = new List<VoucherType>();
        while (r.Read())
        {
            var useForPos = r.GetInt64(13) != 0;
            var type = new VoucherType(
                Guid.Parse(r.GetString(0)),
                r.GetString(1),
                (VoucherBaseType)(int)r.GetInt64(2),
                numbering: (NumberingMethod)(int)r.GetInt64(4),
                defaultShortcut: r.IsDBNull(3) ? null : r.GetString(3),
                abbreviation: r.IsDBNull(5) ? null : r.GetString(5),
                isActive: r.GetInt64(6) != 0,
                isPredefined: r.GetInt64(7) != 0,
                affectsAccounts: r.GetInt64(8) != 0,
                affectsStock: r.GetInt64(9) != 0,
                // v18 (RQ-11): "Use as Manufacturing Journal" flag, read verbatim.
                useAsManufacturingJournal: r.GetInt64(10) != 0,
                // v19 (RQ-16..RQ-20): "Track Additional Costs for Purchases" flag, read verbatim.
                trackAdditionalCosts: r.GetInt64(11) != 0,
                // v20 (RQ-21): "Allow zero-valued transactions" flag, read verbatim.
                allowZeroValuedTransactions: r.GetInt64(12) != 0,
                // v23 (RQ-38): "Use for POS invoicing" flag, read verbatim.
                useForPos: useForPos,
                // v24 (RQ-45/RQ-48): "Use for Job Work" + "Allow Consumption" flags, read verbatim.
                useForJobWork: r.GetInt64(14) != 0,
                allowConsumption: r.GetInt64(15) != 0,
                // v27 (Phase 7 slice 3): "Use for Statutory Payment" flag, read verbatim.
                isStatPayment: r.GetInt64(16) != 0,
                // v39 (Phase 9 slice 2): "Use for RCM Payment Voucher" flag, read verbatim.
                isRcmPaymentVoucher: r.GetInt64(17) != 0,
                // v44 (Phase 9 slice 7): "Use for GST Statutory Adjustment (Alt+J)" flag, read verbatim.
                isGstStatAdjustment: r.GetInt64(18) != 0);
            list.Add(type);
        }
        // v23 (RQ-38/DP-4): attach the retail-till config to each POS-flagged type (a second pass so the reader
        // above is not held open while these child queries run).
        foreach (var type in list)
            if (type.UseForPos)
                type.PosConfig = ReadPosVoucherTypeConfig(type.Id);
        return list;
    }

    /// <summary>Reads the POS retail-till config (v23; RQ-38/DP-4) for one voucher type — its
    /// <c>pos_voucher_type_config</c> row plus the <c>pos_tender_ledger_defaults</c> class-map rows. Returns a fresh
    /// <see cref="PosConfig"/> even when the config row is absent (a POS type may exist with no saved config
    /// yet), so a POS-flagged type never round-trips with a null config.</summary>
    private PosConfig ReadPosVoucherTypeConfig(Guid voucherTypeId)
    {
        var cfg = new PosConfig();
        using (var cmd = _connection.CreateCommand())
        {
            cmd.CommandText = """
                SELECT default_godown_id, default_party_id, print_after_save, default_title, message_1, message_2,
                       declaration
                FROM pos_voucher_type_config WHERE voucher_type_id = $vt;
                """;
            cmd.Parameters.AddWithValue("$vt", voucherTypeId.ToString("D"));
            using var r = cmd.ExecuteReader();
            if (r.Read())
            {
                cfg.DefaultGodownId = r.IsDBNull(0) ? (Guid?)null : Guid.Parse(r.GetString(0));
                cfg.DefaultPartyId = r.IsDBNull(1) ? (Guid?)null : Guid.Parse(r.GetString(1));
                cfg.PrintAfterSave = r.GetInt64(2) != 0;
                cfg.DefaultTitle = r.IsDBNull(3) ? null : r.GetString(3);
                cfg.Message1 = r.IsDBNull(4) ? null : r.GetString(4);
                cfg.Message2 = r.IsDBNull(5) ? null : r.GetString(5);
                cfg.Declaration = r.IsDBNull(6) ? null : r.GetString(6);
            }
        }
        using (var cmd = _connection.CreateCommand())
        {
            cmd.CommandText = """
                SELECT tender_type, ledger_id FROM pos_tender_ledger_defaults
                WHERE voucher_type_id = $vt ORDER BY tender_type;
                """;
            cmd.Parameters.AddWithValue("$vt", voucherTypeId.ToString("D"));
            using var r = cmd.ExecuteReader();
            while (r.Read())
                cfg.SetTenderLedgerDefault((PosTenderType)(int)r.GetInt64(0), Guid.Parse(r.GetString(1)));
        }
        return cfg;
    }

    private IEnumerable<GstRateSlab> ReadGstRateSlabs(Guid companyId)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            SELECT id, rate_bp, label, is_predefined
            FROM gst_rate_slabs WHERE company_id = $cid ORDER BY rowid;
            """;
        cmd.Parameters.AddWithValue("$cid", companyId.ToString("D"));
        using var r = cmd.ExecuteReader();
        var list = new List<GstRateSlab>();
        while (r.Read())
            list.Add(new GstRateSlab(
                Guid.Parse(r.GetString(0)),
                (int)r.GetInt64(1),
                r.GetString(2),
                isPredefined: r.GetInt64(3) != 0));
        return list;
    }

    /// <summary>v38: reads the dated GST rate-history windows for a company (empty when advanced GST is off).</summary>
    private IEnumerable<GstRateHistoryEntry> ReadGstRateHistory(Guid companyId)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            SELECT id, hsn_sac, rate_bp, rate_class, effective_from, effective_to, valuation_basis, label, is_predefined
            FROM gst_rate_history WHERE company_id = $cid ORDER BY rowid;
            """;
        cmd.Parameters.AddWithValue("$cid", companyId.ToString("D"));
        using var r = cmd.ExecuteReader();
        var list = new List<GstRateHistoryEntry>();
        while (r.Read())
            list.Add(new GstRateHistoryEntry(
                Guid.Parse(r.GetString(0)),
                r.IsDBNull(1) ? null : r.GetString(1),
                (int)r.GetInt64(2),
                (GstRateClass)(int)r.GetInt64(3),
                ParseDate(r.GetString(4)),
                r.IsDBNull(5) ? (DateOnly?)null : ParseDate(r.GetString(5)),
                (GstValuationBasis)(int)r.GetInt64(6),
                r.GetString(7),
                isPredefined: r.GetInt64(8) != 0));
        return list;
    }

    /// <summary>v38: reads the dated Compensation-Cess windows for a company (empty when the company bears no cess).</summary>
    private IEnumerable<GstCessRate> ReadGstCessRates(Guid companyId)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            SELECT id, hsn_sac, valuation_mode, cess_rate_bp, cess_per_unit_paisa, cess_rsp_factor_millis,
                   effective_from, effective_to, label, is_predefined
            FROM gst_cess_rates WHERE company_id = $cid ORDER BY rowid;
            """;
        cmd.Parameters.AddWithValue("$cid", companyId.ToString("D"));
        using var r = cmd.ExecuteReader();
        var list = new List<GstCessRate>();
        while (r.Read())
            list.Add(new GstCessRate(
                Guid.Parse(r.GetString(0)),
                r.IsDBNull(1) ? null : r.GetString(1),
                (CessValuationMode)(int)r.GetInt64(2),
                (int)r.GetInt64(3),
                Paisa.ToMoney(r.GetInt64(4)),
                (int)r.GetInt64(5),
                ParseDate(r.GetString(6)),
                r.IsDBNull(7) ? (DateOnly?)null : ParseDate(r.GetString(7)),
                r.GetString(8),
                isPredefined: r.GetInt64(9) != 0));
        return list;
    }

    /// <summary>v39: reads the dated reverse-charge categories for a company (empty when RCM is off).</summary>
    private IEnumerable<RcmCategory> ReadRcmCategories(Guid companyId)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            SELECT id, notification, stream, supply_nature, supply_type, hsn_sac, rate_bp,
                   supplier_qualifier, recipient_qualifier, effective_from, effective_to, label, is_predefined
            FROM rcm_categories WHERE company_id = $cid ORDER BY rowid;
            """;
        cmd.Parameters.AddWithValue("$cid", companyId.ToString("D"));
        using var r = cmd.ExecuteReader();
        var list = new List<RcmCategory>();
        while (r.Read())
            list.Add(new RcmCategory(
                Guid.Parse(r.GetString(0)),
                r.GetString(1),
                (RcmStream)(int)r.GetInt64(2),
                r.GetString(3),
                (GstSupplyType)(int)r.GetInt64(4),
                r.IsDBNull(5) ? null : r.GetString(5),
                (int)r.GetInt64(6),
                (RcmParty)(int)r.GetInt64(7),
                (RcmParty)(int)r.GetInt64(8),
                ParseDate(r.GetString(9)),
                r.IsDBNull(10) ? (DateOnly?)null : ParseDate(r.GetString(10)),
                r.GetString(11),
                isPredefined: r.GetInt64(12) != 0));
        return list;
    }

    /// <summary>v39: reads the RCM generated documents (self-invoices + payment vouchers) for a company (Phase 9 slice 2).</summary>
    private IEnumerable<RcmDocument> ReadRcmDocuments(Guid companyId)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            SELECT id, doc_kind, source_voucher_id, series_number, doc_date, supplier_ledger_id
            FROM rcm_documents WHERE company_id = $cid ORDER BY rowid;
            """;
        cmd.Parameters.AddWithValue("$cid", companyId.ToString("D"));
        using var r = cmd.ExecuteReader();
        var list = new List<RcmDocument>();
        while (r.Read())
            list.Add(new RcmDocument(
                Guid.Parse(r.GetString(0)),
                (RcmDocumentKind)(int)r.GetInt64(1),
                Guid.Parse(r.GetString(2)),
                (int)r.GetInt64(3),
                ParseDate(r.GetString(4)),
                r.IsDBNull(5) ? (Guid?)null : Guid.Parse(r.GetString(5))));
        return list;
    }

    /// <summary>v41: reads the e-invoice IRP artefacts for a company (Phase 9 slice 4a). Rehydrates each record verbatim
    /// via <see cref="EInvoiceRecord.Rehydrate"/> — the IRP-issued IRN/QR are copied, never derived (ER-5). Empty when
    /// e-invoicing is unused (ER-13). The <c>nic_*_enc</c> credential BLOBs are NOT selected here (ER-16).</summary>
    private IEnumerable<EInvoiceRecord> ReadEInvoiceRecords(Guid companyId)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            SELECT id, source_voucher_id, document_number_upper, status, irn, ack_no, ack_date, signed_qr, signed_json,
                   cancelled_on, cancel_reason_code, error_code, error_message
            FROM einvoice_records WHERE company_id = $cid ORDER BY rowid;
            """;
        cmd.Parameters.AddWithValue("$cid", companyId.ToString("D"));
        using var r = cmd.ExecuteReader();
        var list = new List<EInvoiceRecord>();
        while (r.Read())
            list.Add(EInvoiceRecord.Rehydrate(
                Guid.Parse(r.GetString(0)),
                Guid.Parse(r.GetString(1)),
                r.GetString(2),
                (EInvoiceStatus)(int)r.GetInt64(3),
                r.IsDBNull(4) ? null : r.GetString(4),
                r.IsDBNull(5) ? null : r.GetString(5),
                r.IsDBNull(6) ? (DateOnly?)null : ParseDate(r.GetString(6)),
                r.IsDBNull(7) ? null : r.GetString(7),
                r.IsDBNull(8) ? null : (byte[])r.GetValue(8),
                r.IsDBNull(9) ? (DateOnly?)null : ParseDate(r.GetString(9)),
                r.IsDBNull(10) ? null : r.GetString(10),
                r.IsDBNull(11) ? null : r.GetString(11),
                r.IsDBNull(12) ? null : r.GetString(12)));
        return list;
    }

    /// <summary>v42: reads the e-Way Bill artefacts for a company (Phase 9 slice 5). Rehydrates each record verbatim via
    /// <see cref="EWayBillRecord.Rehydrate"/> — the portal-issued EWB number / validity are copied, never derived (ER-5).
    /// Empty when e-Way is unused (ER-13).</summary>
    private IEnumerable<EWayBillRecord> ReadEWayBillRecords(Guid companyId)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            SELECT id, source_voucher_id, document_number_upper, status, supply_type, sub_supply_type, doc_type,
                   consignment_value_paisa, transporter_id, trans_mode, vehicle_number, distance_km, transport_doc_no,
                   ship_from_state_code, ship_to_state_code, is_odc, ship_to_gstin, closure_requested, closed_on,
                   ewb_number, generated_at, valid_upto, cancelled_on, cancel_reason_code, error_code, error_message
            FROM eway_bills WHERE company_id = $cid ORDER BY rowid;
            """;
        cmd.Parameters.AddWithValue("$cid", companyId.ToString("D"));
        using var r = cmd.ExecuteReader();
        var list = new List<EWayBillRecord>();
        while (r.Read())
            list.Add(EWayBillRecord.Rehydrate(
                Guid.Parse(r.GetString(0)),
                Guid.Parse(r.GetString(1)),
                r.GetString(2),
                (EWayStatus)(int)r.GetInt64(3),
                r.IsDBNull(4) ? null : r.GetString(4),
                r.IsDBNull(5) ? null : r.GetString(5),
                r.IsDBNull(6) ? null : r.GetString(6),
                r.GetInt64(7),
                r.IsDBNull(8) ? null : r.GetString(8),
                r.IsDBNull(9) ? (EWayTransportMode?)null : (EWayTransportMode)(int)r.GetInt64(9),
                r.IsDBNull(10) ? null : r.GetString(10),
                (int)r.GetInt64(11),
                r.IsDBNull(12) ? null : r.GetString(12),
                r.IsDBNull(13) ? null : r.GetString(13),
                r.IsDBNull(14) ? null : r.GetString(14),
                r.GetInt64(15) != 0,
                r.IsDBNull(16) ? null : r.GetString(16),
                r.GetInt64(17) != 0,
                r.IsDBNull(18) ? (DateOnly?)null : ParseDate(r.GetString(18)),
                r.IsDBNull(19) ? null : r.GetString(19),
                r.IsDBNull(20) ? (DateTimeOffset?)null : ParseDateTimeOffset(r.GetString(20)),
                r.IsDBNull(21) ? (DateTimeOffset?)null : ParseDateTimeOffset(r.GetString(21)),
                r.IsDBNull(22) ? (DateOnly?)null : ParseDate(r.GetString(22)),
                r.IsDBNull(23) ? null : r.GetString(23),
                r.IsDBNull(24) ? null : r.GetString(24),
                r.IsDBNull(25) ? null : r.GetString(25)));
        return list;
    }

    /// <summary>v42: reads the per-state e-Way threshold overrides for a company (Phase 9 slice 5). Empty on the flat
    /// ₹50,000 default (ER-13).</summary>
    private IEnumerable<EWayStateThreshold> ReadEWayStateThresholds(Guid companyId)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            SELECT id, state_code, txn_type, threshold_paisa
            FROM eway_state_thresholds WHERE company_id = $cid ORDER BY rowid;
            """;
        cmd.Parameters.AddWithValue("$cid", companyId.ToString("D"));
        using var r = cmd.ExecuteReader();
        var list = new List<EWayStateThreshold>();
        while (r.Read())
            list.Add(new EWayStateThreshold(
                Guid.Parse(r.GetString(0)), r.GetString(1), (EWayTransactionType)(int)r.GetInt64(2),
                Paisa.ToMoney(r.GetInt64(3))));
        return list;
    }

    /// <summary>v39: reads the §34 credit/debit-note links for a company (empty until S2b).</summary>
    private IEnumerable<GstCreditDebitNoteLink> ReadGstCdnLinks(Guid companyId)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            SELECT id, cdn_voucher_id, cdn_type, original_invoice_voucher_id, original_invoice_number,
                   original_invoice_date, reason_code, is_9b_target
            FROM gst_cdn_links WHERE company_id = $cid ORDER BY rowid;
            """;
        cmd.Parameters.AddWithValue("$cid", companyId.ToString("D"));
        using var r = cmd.ExecuteReader();
        var list = new List<GstCreditDebitNoteLink>();
        while (r.Read())
            list.Add(new GstCreditDebitNoteLink(
                Guid.Parse(r.GetString(0)),
                Guid.Parse(r.GetString(1)),
                (CdnType)(int)r.GetInt64(2),
                r.IsDBNull(3) ? (Guid?)null : Guid.Parse(r.GetString(3)),
                r.IsDBNull(4) ? null : r.GetString(4),
                r.IsDBNull(5) ? (DateOnly?)null : ParseDate(r.GetString(5)),
                r.GetString(6),
                is9BTarget: r.GetInt64(7) != 0));
        return list;
    }

    /// <summary>v39: reads the GST-on-advance receipts for a company (empty until S2b).</summary>
    private IEnumerable<GstAdvanceReceipt> ReadGstAdvanceReceipts(Guid companyId)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            SELECT id, receipt_voucher_id, is_service, advance_amount_paisa, rate_bp, inter_state, pos_state_code,
                   advance_tax_paisa, adjusted_against_invoice_vid, refund_voucher_id
            FROM gst_advance_receipts WHERE company_id = $cid ORDER BY rowid;
            """;
        cmd.Parameters.AddWithValue("$cid", companyId.ToString("D"));
        using var r = cmd.ExecuteReader();
        var list = new List<GstAdvanceReceipt>();
        while (r.Read())
            list.Add(new GstAdvanceReceipt(
                Guid.Parse(r.GetString(0)),
                Guid.Parse(r.GetString(1)),
                r.GetInt64(2) != 0,
                Paisa.ToMoney(r.GetInt64(3)),
                (int)r.GetInt64(4),
                r.GetInt64(5) != 0,
                r.IsDBNull(6) ? null : r.GetString(6),
                Paisa.ToMoney(r.GetInt64(7)),
                r.IsDBNull(8) ? (Guid?)null : Guid.Parse(r.GetString(8)),
                r.IsDBNull(9) ? (Guid?)null : Guid.Parse(r.GetString(9))));
        return list;
    }

    /// <summary>v43: reads the imported GSTR-2B/2A snapshots for a company, each with its child inward-supply lines
    /// (Phase 9 slice 6). Snapshots are external portal data (NOT the app's postings); each is rehydrated verbatim with
    /// its immutable lines. Empty when 2B is never imported (ER-13).</summary>
    private IEnumerable<Gstr2bSnapshot> ReadGstr2bSnapshots(Guid companyId)
    {
        // Read every line once, grouped by snapshot id (ordered by rowid), then assemble each snapshot with its lines.
        var linesBySnapshot = new Dictionary<Guid, List<Gstr2bLine>>();
        using (var lineCmd = _connection.CreateCommand())
        {
            lineCmd.CommandText = """
                SELECT snapshot_id, id, supplier_gstin, supplier_trade_name, doc_type, doc_number, doc_number_norm,
                       doc_date, pos_state_code, taxable_value_paisa, igst_paisa, cgst_paisa, sgst_paisa, cess_paisa,
                       itc_available, itc_unavailable_reason, reverse_charge
                FROM gstr2b_lines WHERE company_id = $cid ORDER BY rowid;
                """;
            lineCmd.Parameters.AddWithValue("$cid", companyId.ToString("D"));
            using var lr = lineCmd.ExecuteReader();
            while (lr.Read())
            {
                var snapshotId = Guid.Parse(lr.GetString(0));
                if (!linesBySnapshot.TryGetValue(snapshotId, out var bucket))
                    linesBySnapshot[snapshotId] = bucket = new List<Gstr2bLine>();
                bucket.Add(new Gstr2bLine(
                    Guid.Parse(lr.GetString(1)),
                    lr.GetString(2),
                    lr.IsDBNull(3) ? null : lr.GetString(3),
                    (Gstr2bDocType)(int)lr.GetInt64(4),
                    lr.GetString(5),
                    lr.IsDBNull(6) ? null : lr.GetString(6),
                    ParseDate(lr.GetString(7)),
                    lr.IsDBNull(8) ? null : lr.GetString(8),
                    lr.GetInt64(9), lr.GetInt64(10), lr.GetInt64(11), lr.GetInt64(12), lr.GetInt64(13),
                    lr.GetInt64(14) != 0,
                    lr.IsDBNull(15) ? null : lr.GetString(15),
                    lr.GetInt64(16) != 0));
            }
        }

        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            SELECT id, statement_type, return_period, recipient_gstin, generated_on, source_file_hash, imported_at,
                   summary_igst_paisa, summary_cgst_paisa, summary_sgst_paisa, summary_cess_paisa
            FROM gstr2b_snapshots WHERE company_id = $cid ORDER BY rowid;
            """;
        cmd.Parameters.AddWithValue("$cid", companyId.ToString("D"));
        using var r = cmd.ExecuteReader();
        var list = new List<Gstr2bSnapshot>();
        while (r.Read())
        {
            var id = Guid.Parse(r.GetString(0));
            list.Add(Gstr2bSnapshot.Rehydrate(
                id,
                (GstStatementType)(int)r.GetInt64(1),
                r.GetString(2),
                r.GetString(3),
                r.IsDBNull(4) ? (DateOnly?)null : ParseDate(r.GetString(4)),
                r.GetString(5),
                ParseDateTimeOffset(r.GetString(6)),
                r.GetInt64(7), r.GetInt64(8), r.GetInt64(9), r.GetInt64(10),
                linesBySnapshot.TryGetValue(id, out var lines) ? lines : new List<Gstr2bLine>()));
        }
        return list;
    }

    /// <summary>v43: reads the persisted GSTR-2B reconciliation results for a company (Phase 9 slice 6). ADVISORY only —
    /// <c>matched_voucher_id</c> is a read-only pointer (ER-14). Empty until a reconciliation is run (ER-13).</summary>
    private IEnumerable<Gstr2bReconResult> ReadGstr2bReconResults(Guid companyId)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            SELECT id, line_id, bucket, matched_voucher_id, taxable_variance_paisa, tax_variance_paisa, match_pinned,
                   reconciled_at
            FROM gstr2b_recon WHERE company_id = $cid ORDER BY rowid;
            """;
        cmd.Parameters.AddWithValue("$cid", companyId.ToString("D"));
        using var r = cmd.ExecuteReader();
        var list = new List<Gstr2bReconResult>();
        while (r.Read())
            list.Add(Gstr2bReconResult.Rehydrate(
                Guid.Parse(r.GetString(0)),
                Guid.Parse(r.GetString(1)),
                (ReconBucket)(int)r.GetInt64(2),
                r.IsDBNull(3) ? (Guid?)null : Guid.Parse(r.GetString(3)),
                r.GetInt64(4),
                r.GetInt64(5),
                r.GetInt64(6) != 0,
                r.IsDBNull(7) ? (DateTimeOffset?)null : ParseDateTimeOffset(r.GetString(7))));
        return list;
    }

    /// <summary>v43: reads the offline IMS decisions for a company (Phase 9 slice 6b). ADVISORY only — the mirror stores
    /// the Accept/Reject/Pending decision + the Oct-2025 CDN reversal declaration; it posts nothing (ER-14). Empty until
    /// the user acts (a line with no row is deemed-accepted, ER-13).</summary>
    private IEnumerable<ImsAction> ReadImsStatus(Guid companyId)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            SELECT id, line_id, action, remarks, declared_reversal_paisa, no_reversal_declared, acted_on
            FROM ims_status WHERE company_id = $cid ORDER BY rowid;
            """;
        cmd.Parameters.AddWithValue("$cid", companyId.ToString("D"));
        using var r = cmd.ExecuteReader();
        var list = new List<ImsAction>();
        while (r.Read())
            list.Add(ImsAction.Rehydrate(
                Guid.Parse(r.GetString(0)),
                Guid.Parse(r.GetString(1)),
                (ImsStatus)(int)r.GetInt64(2),
                r.IsDBNull(3) ? null : r.GetString(3),
                r.IsDBNull(4) ? (long?)null : r.GetInt64(4),
                r.GetInt64(5) != 0,
                r.IsDBNull(6) ? (DateOnly?)null : ParseDate(r.GetString(6))));
        return list;
    }

    /// <summary>v44: reads the posted Rule-88A set-off Table-6.1 allocation rows for a company (Phase 9 slice 7). Empty
    /// until a period is set off (ER-13).</summary>
    private IEnumerable<GstSetoffLine> ReadGstSetoffLines(Guid companyId)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            SELECT id, voucher_id, period, credit_head, liability_head, is_cash, amount_paisa
            FROM gst_setoff_lines WHERE company_id = $cid ORDER BY rowid;
            """;
        cmd.Parameters.AddWithValue("$cid", companyId.ToString("D"));
        using var r = cmd.ExecuteReader();
        var list = new List<GstSetoffLine>();
        while (r.Read())
            list.Add(GstSetoffLine.Rehydrate(
                Guid.Parse(r.GetString(0)),
                Guid.Parse(r.GetString(1)),
                r.GetString(2),
                (GstTaxHead)(int)r.GetInt64(3),
                (GstTaxHead)(int)r.GetInt64(4),
                r.GetInt64(5) != 0,
                r.GetInt64(6)));
        return list;
    }

    /// <summary>v44: reads the PMT-06 GST deposit challans for a company (Phase 9 slice 7). Empty until GST is
    /// deposited (ER-13). amount_paisa is integer paisa.</summary>
    private IEnumerable<GstChallan> ReadGstChallans(Guid companyId)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            SELECT id, cpin, cin, brn, deposit_date, major_head, minor_head, amount_paisa, voucher_id, interest_flag
            FROM gst_challans WHERE company_id = $cid ORDER BY rowid;
            """;
        cmd.Parameters.AddWithValue("$cid", companyId.ToString("D"));
        using var r = cmd.ExecuteReader();
        var list = new List<GstChallan>();
        while (r.Read())
            list.Add(GstChallan.Rehydrate(
                Guid.Parse(r.GetString(0)),
                r.GetString(1),
                r.IsDBNull(2) ? null : r.GetString(2),
                r.IsDBNull(3) ? null : r.GetString(3),
                ParseDate(r.GetString(4)),
                (GstTaxHead)(int)r.GetInt64(5),
                (GstMinorHead)(int)r.GetInt64(6),
                Paisa.ToMoney(r.GetInt64(7)),
                Guid.Parse(r.GetString(8)),
                r.GetInt64(9) != 0));
        return list;
    }

    /// <summary>v44: reads the DRC-03 voluntary GST payments for a company (Phase 9 slice 7). Empty until one is
    /// raised (ER-13).</summary>
    private IEnumerable<GstDrc03> ReadGstDrc03s(Guid companyId)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            SELECT id, drc03_ref, cause, period, cgst_paisa, sgst_paisa, igst_paisa, cess_paisa, interest_paisa,
                   drc03a_demand_ref, voucher_id, created_at
            FROM gst_drc03 WHERE company_id = $cid ORDER BY rowid;
            """;
        cmd.Parameters.AddWithValue("$cid", companyId.ToString("D"));
        using var r = cmd.ExecuteReader();
        var list = new List<GstDrc03>();
        while (r.Read())
            list.Add(GstDrc03.Rehydrate(
                Guid.Parse(r.GetString(0)),
                r.IsDBNull(1) ? null : r.GetString(1),
                r.GetString(2),
                r.GetString(3),
                r.GetInt64(4), r.GetInt64(5), r.GetInt64(6), r.GetInt64(7), r.GetInt64(8),
                r.IsDBNull(9) ? null : r.GetString(9),
                r.IsDBNull(10) ? (Guid?)null : Guid.Parse(r.GetString(10)),
                ParseDateTimeOffset(r.GetString(11))));
        return list;
    }

    /// <summary>v44: reads the posted ITC-reversal audit rows for a company (Phase 9 slice 7). The reversal engine is
    /// S7b, so this stays EMPTY in S7a (ER-13).</summary>
    private IEnumerable<ItcReversal> ReadItcReversals(Guid companyId)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            SELECT id, rule, period, cgst_paisa, sgst_paisa, igst_paisa, cess_paisa, d1_basis_paisa, d2_basis_paisa,
                   source_voucher_id, source_line_id, reversal_voucher_id, reclaim_of_id, drc03_id, table4b_bucket,
                   created_at
            FROM itc_reversals WHERE company_id = $cid ORDER BY rowid;
            """;
        cmd.Parameters.AddWithValue("$cid", companyId.ToString("D"));
        using var r = cmd.ExecuteReader();
        var list = new List<ItcReversal>();
        while (r.Read())
            list.Add(ItcReversal.Rehydrate(
                Guid.Parse(r.GetString(0)),
                (ItcReversalRule)(int)r.GetInt64(1),
                r.GetString(2),
                r.GetInt64(3), r.GetInt64(4), r.GetInt64(5), r.GetInt64(6),
                r.IsDBNull(7) ? (long?)null : r.GetInt64(7),
                r.IsDBNull(8) ? (long?)null : r.GetInt64(8),
                r.IsDBNull(9) ? (Guid?)null : Guid.Parse(r.GetString(9)),
                r.IsDBNull(10) ? (Guid?)null : Guid.Parse(r.GetString(10)),
                Guid.Parse(r.GetString(11)),
                r.IsDBNull(12) ? (Guid?)null : Guid.Parse(r.GetString(12)),
                r.IsDBNull(13) ? (Guid?)null : Guid.Parse(r.GetString(13)),
                (Table4bBucket)(int)r.GetInt64(14),
                ParseDateTimeOffset(r.GetString(15))));
        return list;
    }

    /// <summary>v35: reads the PT slab bands for a company and groups them (by <c>slab_id</c>, ordered by
    /// <c>band_order</c>) into <see cref="PtSlab"/> tables, preserving each table's state + gender scope. No rows ⇒ an
    /// empty list.</summary>
    private IEnumerable<PtSlab> ReadPtSlabTables(Guid companyId)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            SELECT slab_id, state_code, gender_scope, from_wage_paisa, to_wage_paisa, monthly_amount_paisa, month_overrides
            FROM pt_slab_bands WHERE company_id = $cid ORDER BY band_order, rowid;
            """;
        cmd.Parameters.AddWithValue("$cid", companyId.ToString("D"));

        // Group by slab_id preserving first-seen order. Ordered by the WRITTEN band_order (low band to high) — the
        // documented key — so band order is robust to any future in-place edit that leaves rowid and band_order out of
        // step (F2); rowid is only a deterministic tie-break across slabs (band_order is unique within a slab), which
        // keeps each slab's first-seen position stable = its saved SlabTables order.
        var order = new List<Guid>();
        var byId = new Dictionary<Guid, (string State, PtGenderScope Scope, List<PtSlabBand> Bands)>();
        using (var r = cmd.ExecuteReader())
            while (r.Read())
            {
                var slabId = Guid.Parse(r.GetString(0));
                if (!byId.TryGetValue(slabId, out var table))
                {
                    table = (r.GetString(1), (PtGenderScope)(int)r.GetInt64(2), new List<PtSlabBand>());
                    byId[slabId] = table;
                    order.Add(slabId);
                }
                var from = new Money(r.GetInt64(3) / 100m);
                Money? to = r.IsDBNull(4) ? null : new Money(r.GetInt64(4) / 100m);
                var amount = new Money(r.GetInt64(5) / 100m);
                var overrides = ParsePtMonthOverrides(r.GetString(6));
                table.Bands.Add(new PtSlabBand(from, to, amount, overrides));
            }

        var list = new List<PtSlab>(order.Count);
        foreach (var id in order)
        {
            var t = byId[id];
            list.Add(new PtSlab(id, t.State, t.Scope, t.Bands));
        }
        return list;
    }

    /// <summary>v36: reads the per-employee §192 income-tax declarations (Form 12BB) for a company. No rows ⇒ an
    /// empty list (ER-13). All money is stored as integer paisa.</summary>
    private IEnumerable<TaxDeclaration> ReadTaxDeclarations(Guid companyId)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            SELECT employee_id, section_80c_paisa, section_80d_paisa, section_80ccd1b_paisa,
                   section_80ccd2_employer_paisa, hra_exempt_paisa, home_loan_interest_paisa, other_income_paisa,
                   prev_employer_salary_paisa, prev_employer_tds_paisa
            FROM employee_tax_declarations WHERE company_id = $cid ORDER BY rowid;
            """;
        cmd.Parameters.AddWithValue("$cid", companyId.ToString("D"));

        var list = new List<TaxDeclaration>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(new TaxDeclaration
            {
                EmployeeId = Guid.Parse(r.GetString(0)),
                Section80C = new Money(r.GetInt64(1) / 100m),
                Section80D = new Money(r.GetInt64(2) / 100m),
                Section80CCD1B = new Money(r.GetInt64(3) / 100m),
                Section80CCD2Employer = new Money(r.GetInt64(4) / 100m),
                HouseRentAllowanceExempt = new Money(r.GetInt64(5) / 100m),
                HomeLoanInterest24b = new Money(r.GetInt64(6) / 100m),
                OtherIncome = new Money(r.GetInt64(7) / 100m),
                PreviousEmployerSalary = new Money(r.GetInt64(8) / 100m),
                PreviousEmployerTds = new Money(r.GetInt64(9) / 100m),
            });
        return list;
    }

    /// <summary>Parses the compact PT month-override column ("month:paisa" pairs joined by ';', "" = none).</summary>
    private static IReadOnlyList<PtMonthOverride> ParsePtMonthOverrides(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return Array.Empty<PtMonthOverride>();
        var result = new List<PtMonthOverride>();
        foreach (var part in text.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var kv = part.Split(':', 2);
            var month = int.Parse(kv[0], System.Globalization.CultureInfo.InvariantCulture);
            var paisa = long.Parse(kv[1], System.Globalization.CultureInfo.InvariantCulture);
            result.Add(new PtMonthOverride(month, new Money(paisa / 100m)));
        }
        return result;
    }

    /// <summary>Formats a PT band's month overrides into the compact "month:paisa" column ("" = none).</summary>
    private static string FormatPtMonthOverrides(IReadOnlyList<PtMonthOverride> overrides)
    {
        if (overrides.Count == 0) return string.Empty;
        return string.Join(';', overrides.Select(o =>
            $"{o.Month.ToString(System.Globalization.CultureInfo.InvariantCulture)}:" +
            $"{((long)(o.Amount.Amount * 100m)).ToString(System.Globalization.CultureInfo.InvariantCulture)}"));
    }

    /// <summary>v25: a threshold stored as rupees × 1,000,000 ("micros") → exact <see cref="Money"/> (rupees).</summary>
    private static Money? MicroToMoney(object? micro) =>
        micro is null ? null : new Money(Convert.ToInt64(micro) / 1_000_000m);

    /// <summary>v25: an exact <see cref="Money"/> (rupees) → rupees × 1,000,000 ("micros") for storage.</summary>
    private static object MoneyToMicro(Money? money) =>
        money is { } m ? (long)(m.Amount * 1_000_000m) : (object)DBNull.Value;

    private IEnumerable<NatureOfPayment> ReadNaturesOfPayment(Guid companyId)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            SELECT id, section_code, name, rate_with_pan_bp, rate_without_pan_bp,
                   single_threshold_micro, cumulative_threshold_micro, fvu_code, effective_from, is_predefined
            FROM nature_of_payment WHERE company_id = $cid ORDER BY rowid;
            """;
        cmd.Parameters.AddWithValue("$cid", companyId.ToString("D"));
        using var r = cmd.ExecuteReader();
        var list = new List<NatureOfPayment>();
        while (r.Read())
            list.Add(new NatureOfPayment(
                Guid.Parse(r.GetString(0)), r.GetString(1), r.GetString(2),
                (int)r.GetInt64(3), (int)r.GetInt64(4), r.GetString(7),
                singleTransactionThreshold: MicroToMoney(r.IsDBNull(5) ? null : r.GetInt64(5)),
                cumulativeThreshold: MicroToMoney(r.IsDBNull(6) ? null : r.GetInt64(6)),
                effectiveFrom: r.IsDBNull(8) ? (DateOnly?)null : ParseDate(r.GetString(8)),
                isPredefined: r.GetInt64(9) != 0));
        return list;
    }

    private IEnumerable<NatureOfGoods> ReadNaturesOfGoods(Guid companyId)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            SELECT id, collection_code, name, rate_with_pan_bp, rate_without_pan_bp, threshold_micro,
                   base_includes_gst, fvu_code, effective_from, is_predefined, is_legacy, legacy_cutoff
            FROM nature_of_goods WHERE company_id = $cid ORDER BY rowid;
            """;
        cmd.Parameters.AddWithValue("$cid", companyId.ToString("D"));
        using var r = cmd.ExecuteReader();
        var list = new List<NatureOfGoods>();
        while (r.Read())
            list.Add(new NatureOfGoods(
                Guid.Parse(r.GetString(0)), r.GetString(1), r.GetString(2),
                (int)r.GetInt64(3), (int)r.GetInt64(4), r.GetString(7),
                threshold: MicroToMoney(r.IsDBNull(5) ? null : r.GetInt64(5)),
                baseIncludesGst: r.GetInt64(6) != 0,
                effectiveFrom: r.IsDBNull(8) ? (DateOnly?)null : ParseDate(r.GetString(8)),
                isPredefined: r.GetInt64(9) != 0,
                isLegacy: r.GetInt64(10) != 0,
                legacyCutoff: r.IsDBNull(11) ? (DateOnly?)null : ParseDate(r.GetString(11))));
        return list;
    }

    private IEnumerable<Currency> ReadCurrencies(Guid companyId)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            SELECT id, symbol, formal_name, decimal_places, is_base
            FROM currencies WHERE company_id = $cid ORDER BY rowid;
            """;
        cmd.Parameters.AddWithValue("$cid", companyId.ToString("D"));
        using var r = cmd.ExecuteReader();
        var list = new List<Currency>();
        while (r.Read())
        {
            list.Add(new Currency(
                Guid.Parse(r.GetString(0)),
                r.GetString(1),
                r.GetString(2),
                decimalPlaces: (int)r.GetInt64(3),
                isBaseCurrency: r.GetInt64(4) != 0));
        }
        return list;
    }

    private IEnumerable<ExchangeRate> ReadExchangeRates(Guid companyId)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            SELECT id, currency_id, rate_date, standard_rate_micro, selling_rate_micro, buying_rate_micro
            FROM exchange_rates WHERE company_id = $cid ORDER BY rowid;
            """;
        cmd.Parameters.AddWithValue("$cid", companyId.ToString("D"));
        using var r = cmd.ExecuteReader();
        var list = new List<ExchangeRate>();
        while (r.Read())
        {
            list.Add(new ExchangeRate(
                Guid.Parse(r.GetString(0)),
                Guid.Parse(r.GetString(1)),
                ParseDate(r.GetString(2)),
                standardRate: MicroToDecimal(r.GetInt64(3)),
                sellingRate: r.IsDBNull(4) ? (decimal?)null : MicroToDecimal(r.GetInt64(4)),
                buyingRate: r.IsDBNull(5) ? (decimal?)null : MicroToDecimal(r.GetInt64(5))));
        }
        return list;
    }

    private IEnumerable<CostCategory> ReadCostCategories(Guid companyId)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            SELECT id, name, allocate_revenue, allocate_non_revenue, is_predefined
            FROM cost_categories WHERE company_id = $cid ORDER BY rowid;
            """;
        cmd.Parameters.AddWithValue("$cid", companyId.ToString("D"));
        using var r = cmd.ExecuteReader();
        var list = new List<CostCategory>();
        while (r.Read())
        {
            list.Add(new CostCategory(
                Guid.Parse(r.GetString(0)),
                r.GetString(1),
                allocateRevenueItems: r.GetInt64(2) != 0,
                allocateNonRevenueItems: r.GetInt64(3) != 0,
                isPredefined: r.GetInt64(4) != 0));
        }
        return list;
    }

    private IEnumerable<CostCentre> ReadCostCentres(Guid companyId)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            SELECT id, name, category_id, parent_id, alias
            FROM cost_centres WHERE company_id = $cid ORDER BY rowid;
            """;
        cmd.Parameters.AddWithValue("$cid", companyId.ToString("D"));
        using var r = cmd.ExecuteReader();
        var list = new List<CostCentre>();
        while (r.Read())
        {
            list.Add(new CostCentre(
                Guid.Parse(r.GetString(0)),
                r.GetString(1),
                Guid.Parse(r.GetString(2)),
                parentId: r.IsDBNull(3) ? (Guid?)null : Guid.Parse(r.GetString(3)),
                alias: r.IsDBNull(4) ? null : r.GetString(4)));
        }
        return list;
    }

    private IEnumerable<Unit> ReadUnits(Guid companyId)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            SELECT id, symbol, formal_name, is_compound, uqc, decimal_places,
                   first_unit_id, tail_unit_id, conversion_numerator, conversion_denominator
            FROM units WHERE company_id = $cid ORDER BY rowid;
            """;
        cmd.Parameters.AddWithValue("$cid", companyId.ToString("D"));
        using var r = cmd.ExecuteReader();
        var list = new List<Unit>();
        while (r.Read())
        {
            list.Add(Unit.FromStorage(
                Guid.Parse(r.GetString(0)),
                r.GetString(1),
                r.GetString(2),
                isCompound: r.GetInt64(3) != 0,
                unitQuantityCode: r.IsDBNull(4) ? null : r.GetString(4),
                decimalPlaces: (int)r.GetInt64(5),
                firstUnitId: r.IsDBNull(6) ? (Guid?)null : Guid.Parse(r.GetString(6)),
                tailUnitId: r.IsDBNull(7) ? (Guid?)null : Guid.Parse(r.GetString(7)),
                conversionNumerator: r.IsDBNull(8) ? (int?)null : (int)r.GetInt64(8),
                conversionDenominator: r.IsDBNull(9) ? (int?)null : (int)r.GetInt64(9)));
        }
        return list;
    }

    private IEnumerable<EmployeeCategory> ReadEmployeeCategories(Guid companyId)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            SELECT id, name, allocate_revenue, allocate_non_revenue, is_predefined
            FROM employee_categories WHERE company_id = $cid ORDER BY rowid;
            """;
        cmd.Parameters.AddWithValue("$cid", companyId.ToString("D"));
        using var r = cmd.ExecuteReader();
        var list = new List<EmployeeCategory>();
        while (r.Read())
            list.Add(new EmployeeCategory(
                Guid.Parse(r.GetString(0)), r.GetString(1),
                allocateRevenueItems: r.GetInt64(2) != 0,
                allocateNonRevenueItems: r.GetInt64(3) != 0,
                isPredefined: r.GetInt64(4) != 0));
        return list;
    }

    private IEnumerable<EmployeeGroup> ReadEmployeeGroups(Guid companyId)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            SELECT id, name, parent_id, alias, define_salary_details
            FROM employee_groups WHERE company_id = $cid ORDER BY rowid;
            """;
        cmd.Parameters.AddWithValue("$cid", companyId.ToString("D"));
        using var r = cmd.ExecuteReader();
        var list = new List<EmployeeGroup>();
        while (r.Read())
            list.Add(new EmployeeGroup(
                Guid.Parse(r.GetString(0)), r.GetString(1),
                parentId: r.IsDBNull(2) ? (Guid?)null : Guid.Parse(r.GetString(2)),
                alias: r.IsDBNull(3) ? null : r.GetString(3),
                defineSalaryDetails: r.GetInt64(4) != 0));
        return list;
    }

    private IEnumerable<PayrollUnit> ReadPayrollUnits(Guid companyId)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            SELECT id, symbol, formal_name, is_compound, decimal_places,
                   first_unit_id, tail_unit_id, conversion_numerator, conversion_denominator
            FROM payroll_units WHERE company_id = $cid ORDER BY rowid;
            """;
        cmd.Parameters.AddWithValue("$cid", companyId.ToString("D"));
        using var r = cmd.ExecuteReader();
        var list = new List<PayrollUnit>();
        while (r.Read())
            list.Add(PayrollUnit.FromStorage(
                Guid.Parse(r.GetString(0)), r.GetString(1), r.GetString(2),
                isCompound: r.GetInt64(3) != 0,
                decimalPlaces: (int)r.GetInt64(4),
                firstUnitId: r.IsDBNull(5) ? (Guid?)null : Guid.Parse(r.GetString(5)),
                tailUnitId: r.IsDBNull(6) ? (Guid?)null : Guid.Parse(r.GetString(6)),
                conversionNumerator: r.IsDBNull(7) ? (int?)null : (int)r.GetInt64(7),
                conversionDenominator: r.IsDBNull(8) ? (int?)null : (int)r.GetInt64(8)));
        return list;
    }

    private IEnumerable<AttendanceType> ReadAttendanceTypes(Guid companyId)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            SELECT id, name, parent_id, kind, payroll_unit_id
            FROM attendance_types WHERE company_id = $cid ORDER BY rowid;
            """;
        cmd.Parameters.AddWithValue("$cid", companyId.ToString("D"));
        using var r = cmd.ExecuteReader();
        var list = new List<AttendanceType>();
        while (r.Read())
            list.Add(new AttendanceType(
                Guid.Parse(r.GetString(0)), r.GetString(1),
                (AttendanceTypeKind)(int)r.GetInt64(3),
                parentId: r.IsDBNull(2) ? (Guid?)null : Guid.Parse(r.GetString(2)),
                payrollUnitId: r.IsDBNull(4) ? (Guid?)null : Guid.Parse(r.GetString(4))));
        return list;
    }

    private IEnumerable<Employee> ReadEmployees(Guid companyId)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            SELECT id, name, employee_group_id, employee_category_id, employee_number,
                   date_of_joining, date_of_leaving, designation, function, location, gender, date_of_birth,
                   pan, aadhaar, uan, pf_account_number, esi_number,
                   bank_account_number, bank_name, bank_ifsc, tax_regime,
                   pf_applicable, pf_higher_wages, pf_join_date,
                   esi_applicable, esi_person_with_disability
            FROM employees WHERE company_id = $cid ORDER BY rowid;
            """;
        cmd.Parameters.AddWithValue("$cid", companyId.ToString("D"));
        using var r = cmd.ExecuteReader();
        var list = new List<Employee>();
        while (r.Read())
            list.Add(new Employee(Guid.Parse(r.GetString(0)), r.GetString(1), Guid.Parse(r.GetString(2)))
            {
                EmployeeCategoryId = r.IsDBNull(3) ? (Guid?)null : Guid.Parse(r.GetString(3)),
                EmployeeNumber = r.IsDBNull(4) ? null : r.GetString(4),
                DateOfJoining = r.IsDBNull(5) ? (DateOnly?)null : ParseDate(r.GetString(5)),
                DateOfLeaving = r.IsDBNull(6) ? (DateOnly?)null : ParseDate(r.GetString(6)),
                Designation = r.IsDBNull(7) ? null : r.GetString(7),
                Function = r.IsDBNull(8) ? null : r.GetString(8),
                Location = r.IsDBNull(9) ? null : r.GetString(9),
                Gender = r.IsDBNull(10) ? null : r.GetString(10),
                DateOfBirth = r.IsDBNull(11) ? (DateOnly?)null : ParseDate(r.GetString(11)),
                Pan = r.IsDBNull(12) ? null : r.GetString(12),
                Aadhaar = r.IsDBNull(13) ? null : r.GetString(13),
                Uan = r.IsDBNull(14) ? null : r.GetString(14),
                PfAccountNumber = r.IsDBNull(15) ? null : r.GetString(15),
                EsiNumber = r.IsDBNull(16) ? null : r.GetString(16),
                BankAccountNumber = r.IsDBNull(17) ? null : r.GetString(17),
                BankName = r.IsDBNull(18) ? null : r.GetString(18),
                BankIfsc = r.IsDBNull(19) ? null : r.GetString(19),
                ApplicableTaxRegime = (TaxRegime)(int)r.GetInt64(20),
                // v33 (Phase 8 slice 4): per-employee PF details (default off — ER-13).
                PfApplicable = r.GetInt64(21) != 0,
                PfContributeOnHigherWages = r.GetInt64(22) != 0,
                PfJoinDate = r.IsDBNull(23) ? (DateOnly?)null : ParseDate(r.GetString(23)),
                // v34 (Phase 8 slice 5): per-employee ESI applicability + person-with-disability (default off — ER-13).
                EsiApplicable = r.GetInt64(24) != 0,
                IsPersonWithDisability = r.GetInt64(25) != 0,
            });
        return list;
    }

    private IEnumerable<PayHead> ReadPayHeads(Guid companyId)
    {
        // 1) the computed-on basis components + slab bands, grouped by pay head, so we can attach a Computation.
        var components = new Dictionary<Guid, List<PayHeadComputationComponent>>();
        using (var cc = _connection.CreateCommand())
        {
            cc.CommandText = """
                SELECT c.pay_head_id, c.component_pay_head_id, c.is_subtraction
                FROM pay_head_computation c
                JOIN pay_heads p ON p.id = c.pay_head_id
                WHERE p.company_id = $cid
                ORDER BY c.pay_head_id, c.ord, c.id;
                """;
            cc.Parameters.AddWithValue("$cid", companyId.ToString("D"));
            using var r = cc.ExecuteReader();
            while (r.Read())
            {
                var owner = Guid.Parse(r.GetString(0));
                if (!components.TryGetValue(owner, out var list)) components[owner] = list = new();
                list.Add(new PayHeadComputationComponent(Guid.Parse(r.GetString(1)), isSubtraction: r.GetInt64(2) != 0));
            }
        }

        var slabs = new Dictionary<Guid, List<PayHeadComputationSlab>>();
        using (var cs = _connection.CreateCommand())
        {
            cs.CommandText = """
                SELECT s.pay_head_id, s.from_amount_paisa, s.to_amount_paisa, s.slab_type, s.rate_basis_points, s.value_paisa
                FROM pay_head_computation_slabs s
                JOIN pay_heads p ON p.id = s.pay_head_id
                WHERE p.company_id = $cid
                ORDER BY s.pay_head_id, s.ord, s.id;
                """;
            cs.Parameters.AddWithValue("$cid", companyId.ToString("D"));
            using var r = cs.ExecuteReader();
            while (r.Read())
            {
                var owner = Guid.Parse(r.GetString(0));
                if (!slabs.TryGetValue(owner, out var list)) slabs[owner] = list = new();
                list.Add(new PayHeadComputationSlab(
                    (PayHeadComputationSlabType)(int)r.GetInt64(3),
                    rateBasisPoints: (int)r.GetInt64(4),
                    value: Paisa.ToMoney(r.GetInt64(5)),
                    fromAmount: r.IsDBNull(1) ? (Money?)null : Paisa.ToMoney(r.GetInt64(1)),
                    toAmount: r.IsDBNull(2) ? (Money?)null : Paisa.ToMoney(r.GetInt64(2))));
            }
        }

        // 2) the pay-head master rows.
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            SELECT id, name, display_name, pay_head_type, calculation_type, affects_net_salary,
                   under_group_id, ledger_id, income_tax_component, use_for_gratuity,
                   rounding_method, rounding_limit_paisa, calculation_period, attendance_type_id, per_day_calculation_basis,
                   employer_expense_ledger_id, pf_component, part_of_pf_wages,
                   esi_component, part_of_esi_wages, is_overtime, pt_component
            FROM pay_heads WHERE company_id = $cid ORDER BY rowid;
            """;
        cmd.Parameters.AddWithValue("$cid", companyId.ToString("D"));
        using var pr = cmd.ExecuteReader();
        var list2 = new List<PayHead>();
        while (pr.Read())
        {
            var id = Guid.Parse(pr.GetString(0));
            var payHead = new PayHead(
                id, pr.GetString(1),
                (PayHeadType)(int)pr.GetInt64(3),
                (PayHeadCalculationType)(int)pr.GetInt64(4))
            {
                DisplayName = pr.IsDBNull(2) ? null : pr.GetString(2),
                AffectsNetSalary = pr.GetInt64(5) != 0,
                UnderGroupId = pr.IsDBNull(6) ? (Guid?)null : Guid.Parse(pr.GetString(6)),
                LedgerId = pr.IsDBNull(7) ? (Guid?)null : Guid.Parse(pr.GetString(7)),
                IncomeTaxComponent = (IncomeTaxComponent)(int)pr.GetInt64(8),
                UseForGratuity = pr.GetInt64(9) != 0,
                RoundingMethod = (PayHeadRoundingMethod)(int)pr.GetInt64(10),
                RoundingLimit = Paisa.ToMoney(pr.GetInt64(11)),
                CalculationPeriod = (PayHeadCalculationPeriod)(int)pr.GetInt64(12),
                AttendanceTypeId = pr.IsDBNull(13) ? (Guid?)null : Guid.Parse(pr.GetString(13)),
                PerDayCalculationBasisDays = pr.IsDBNull(14) ? (int?)null : (int)pr.GetInt64(14),
                EmployerExpenseLedgerId = pr.IsDBNull(15) ? (Guid?)null : Guid.Parse(pr.GetString(15)),
                // v33 (Phase 8 slice 4): PF statutory role + PF-wage flag (default None/false — ER-13).
                PfComponent = (PfStatutoryComponent)(int)pr.GetInt64(16),
                PartOfPfWages = pr.GetInt64(17) != 0,
                // v34 (Phase 8 slice 5): ESI statutory role + ESI-wage flag + overtime marker (default None/false — ER-13).
                EsiComponent = (EsiStatutoryComponent)(int)pr.GetInt64(18),
                PartOfEsiWages = pr.GetInt64(19) != 0,
                IsOvertime = pr.GetInt64(20) != 0,
                // v35 (Phase 8 slice 6): PT statutory role (default None — ER-13).
                PtComponent = (PtStatutoryComponent)(int)pr.GetInt64(21),
            };
            var hasComponents = components.TryGetValue(id, out var comps);
            var hasSlabs = slabs.TryGetValue(id, out var slabList);
            if (hasComponents || hasSlabs)
                payHead.Computation = new PayHeadComputation(
                    comps ?? Enumerable.Empty<PayHeadComputationComponent>(),
                    slabList ?? Enumerable.Empty<PayHeadComputationSlab>());
            list2.Add(payHead);
        }
        return list2;
    }

    private IEnumerable<SalaryStructure> ReadSalaryStructures(Guid companyId)
    {
        var lines = new Dictionary<Guid, List<SalaryStructureLine>>();
        using (var lc = _connection.CreateCommand())
        {
            lc.CommandText = """
                SELECT l.salary_structure_id, l.pay_head_id, l.ord, l.amount_paisa
                FROM salary_structure_lines l
                JOIN salary_structures s ON s.id = l.salary_structure_id
                WHERE s.company_id = $cid
                ORDER BY l.salary_structure_id, l.ord, l.id;
                """;
            lc.Parameters.AddWithValue("$cid", companyId.ToString("D"));
            using var r = lc.ExecuteReader();
            while (r.Read())
            {
                var owner = Guid.Parse(r.GetString(0));
                if (!lines.TryGetValue(owner, out var list)) lines[owner] = list = new();
                list.Add(new SalaryStructureLine(
                    Guid.Parse(r.GetString(1)), (int)r.GetInt64(2),
                    amount: r.IsDBNull(3) ? (Money?)null : Paisa.ToMoney(r.GetInt64(3))));
            }
        }

        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            SELECT id, scope, scope_id, effective_from, start_type
            FROM salary_structures WHERE company_id = $cid ORDER BY rowid;
            """;
        cmd.Parameters.AddWithValue("$cid", companyId.ToString("D"));
        using var sr = cmd.ExecuteReader();
        var result = new List<SalaryStructure>();
        while (sr.Read())
        {
            var id = Guid.Parse(sr.GetString(0));
            var structLines = lines.TryGetValue(id, out var l) ? l : new List<SalaryStructureLine>();
            result.Add(new SalaryStructure(
                id,
                (SalaryStructureScope)(int)sr.GetInt64(1),
                Guid.Parse(sr.GetString(2)),
                ParseDate(sr.GetString(3)),
                (SalaryStructureStartType)(int)sr.GetInt64(4),
                structLines));
        }
        return result;
    }

    private IEnumerable<StockGroup> ReadStockGroups(Guid companyId)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            SELECT id, name, parent_id, alias, add_quantities
            FROM stock_groups WHERE company_id = $cid ORDER BY rowid;
            """;
        cmd.Parameters.AddWithValue("$cid", companyId.ToString("D"));
        using var r = cmd.ExecuteReader();
        var list = new List<StockGroup>();
        while (r.Read())
        {
            list.Add(new StockGroup(
                Guid.Parse(r.GetString(0)),
                r.GetString(1),
                parentId: r.IsDBNull(2) ? (Guid?)null : Guid.Parse(r.GetString(2)),
                alias: r.IsDBNull(3) ? null : r.GetString(3),
                addQuantities: r.GetInt64(4) != 0));
        }
        return list;
    }

    private IEnumerable<StockCategory> ReadStockCategories(Guid companyId)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            SELECT id, name, parent_id, alias
            FROM stock_categories WHERE company_id = $cid ORDER BY rowid;
            """;
        cmd.Parameters.AddWithValue("$cid", companyId.ToString("D"));
        using var r = cmd.ExecuteReader();
        var list = new List<StockCategory>();
        while (r.Read())
        {
            list.Add(new StockCategory(
                Guid.Parse(r.GetString(0)),
                r.GetString(1),
                parentId: r.IsDBNull(2) ? (Guid?)null : Guid.Parse(r.GetString(2)),
                alias: r.IsDBNull(3) ? null : r.GetString(3)));
        }
        return list;
    }

    private IEnumerable<Godown> ReadGodowns(Guid companyId)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            SELECT id, name, parent_id, alias, third_party, is_main_location
            FROM godowns WHERE company_id = $cid ORDER BY rowid;
            """;
        cmd.Parameters.AddWithValue("$cid", companyId.ToString("D"));
        using var r = cmd.ExecuteReader();
        var list = new List<Godown>();
        while (r.Read())
        {
            list.Add(new Godown(
                Guid.Parse(r.GetString(0)),
                r.GetString(1),
                parentId: r.IsDBNull(2) ? (Guid?)null : Guid.Parse(r.GetString(2)),
                alias: r.IsDBNull(3) ? null : r.GetString(3),
                thirdParty: r.GetInt64(4) != 0,
                isMainLocation: r.GetInt64(5) != 0));
        }
        return list;
    }

    private IEnumerable<StockItem> ReadStockItems(Guid companyId)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            SELECT id, name, stock_group_id, category_id, base_unit_id, alias, valuation_method,
                   hsn_sac_code, is_taxable, reorder_level_micro, min_order_qty_micro, standard_cost_paisa,
                   gst_hsn_sac, gst_taxability, gst_rate_bp, gst_supply_type,
                   maintain_in_batches, track_manufacturing_date, use_expiry_dates, set_components,
                   tcs_nature_id,
                   gst_valuation_basis, cess_applicable, cess_valuation_mode, cess_rate_bp,
                   cess_per_unit_paisa, cess_rsp_factor_millis, rsp_paisa,
                   reverse_charge_applicable, gta_forward_charge, rcm_category_id,
                   itc_eligibility, blocked_credit_category
            FROM stock_items WHERE company_id = $cid ORDER BY rowid;
            """;
        cmd.Parameters.AddWithValue("$cid", companyId.ToString("D"));
        using var r = cmd.ExecuteReader();
        var list = new List<StockItem>();
        while (r.Read())
        {
            var item = new StockItem(
                Guid.Parse(r.GetString(0)),
                r.GetString(1),
                stockGroupId: Guid.Parse(r.GetString(2)),
                baseUnitId: Guid.Parse(r.GetString(4)),
                categoryId: r.IsDBNull(3) ? (Guid?)null : Guid.Parse(r.GetString(3)),
                alias: r.IsDBNull(5) ? null : r.GetString(5),
                valuationMethod: (StockValuationMethod)(int)r.GetInt64(6),
                hsnSacCode: r.IsDBNull(7) ? null : r.GetString(7),
                isTaxable: r.GetInt64(8) != 0,
                reorderLevel: r.IsDBNull(9) ? (decimal?)null : QtyMicroToDecimal(r.GetInt64(9)),
                minimumOrderQuantity: r.IsDBNull(10) ? (decimal?)null : QtyMicroToDecimal(r.GetInt64(10)),
                standardCost: r.IsDBNull(11) ? (Money?)null : Paisa.ToMoney(r.GetInt64(11)),
                gst: ReadStockItemGst(r));
            // v16 batch switches (columns 16–18).
            item.MaintainInBatches = r.GetInt64(16) != 0;
            item.TrackManufacturingDate = r.GetInt64(17) != 0;
            item.UseExpiryDates = r.GetInt64(18) != 0;
            // v18 Set-Components (BOM) flag (column 19; RQ-10), read verbatim.
            item.SetComponents = r.GetInt64(19) != 0;
            // v25 (Phase 7 slice 1): the item's default Nature-of-Goods (§206C TCS) id (column 20), read verbatim.
            item.TcsNatureOfGoodsId = r.IsDBNull(20) ? (Guid?)null : Guid.Parse(r.GetString(20));
            list.Add(item);
        }
        return list;
    }

    private IEnumerable<BatchMaster> ReadBatchMasters(Guid companyId)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            SELECT id, stock_item_id, batch_no, mfg_date, expiry_date, expiry_period,
                   godown_id, inward_qty_micro, inward_rate_paisa
            FROM batch_masters WHERE company_id = $cid ORDER BY rowid;
            """;
        cmd.Parameters.AddWithValue("$cid", companyId.ToString("D"));
        using var r = cmd.ExecuteReader();
        var list = new List<BatchMaster>();
        while (r.Read())
        {
            list.Add(new BatchMaster(
                Guid.Parse(r.GetString(0)),
                stockItemId: Guid.Parse(r.GetString(1)),
                batchNumber: r.GetString(2),
                manufacturingDate: r.IsDBNull(3) ? (DateOnly?)null : ParseDate(r.GetString(3)),
                expiryDate: r.IsDBNull(4) ? (DateOnly?)null : ParseDate(r.GetString(4)),
                expiryPeriod: r.IsDBNull(5) ? null : ExpiryPeriod.Parse(r.GetString(5)),
                godownId: r.IsDBNull(6) ? (Guid?)null : Guid.Parse(r.GetString(6)),
                inwardQuantity: r.IsDBNull(7) ? (decimal?)null : QtyMicroToDecimal(r.GetInt64(7)),
                inwardRate: r.IsDBNull(8) ? (Money?)null : Paisa.ToMoney(r.GetInt64(8))));
        }
        return list;
    }

    /// <summary>
    /// Reads the Bill-of-Materials masters (Phase 6 slice 2; RQ-9, DP-3) from the v17 <c>bill_of_materials</c> +
    /// <c>bom_lines</c> tables, reconstructing each header with its ordered lines. Micros (× 1,000,000) →
    /// exact decimal quantity / unit-of-manufacture; paisa → carve-out rate; millis (× 1,000) → carve-out percent
    /// — all lossless (ER-3). Lines are read in stored <c>line_order</c>.
    /// </summary>
    private IEnumerable<BillOfMaterials> ReadBillsOfMaterials(Guid companyId)
    {
        // Read the lines per BOM once, keyed by bom id (ordered by line_order), then assemble the headers.
        var linesByBom = new Dictionary<Guid, List<BomLine>>();
        using (var lineCmd = _connection.CreateCommand())
        {
            lineCmd.CommandText = """
                SELECT l.bom_id, l.line_type, l.component_stock_item_id, l.godown_id, l.qty_micro,
                       l.rate_paisa, l.percent_millis
                FROM bom_lines l
                JOIN bill_of_materials b ON b.id = l.bom_id
                WHERE b.company_id = $cid
                ORDER BY l.bom_id, l.line_order;
                """;
            lineCmd.Parameters.AddWithValue("$cid", companyId.ToString("D"));
            using var lr = lineCmd.ExecuteReader();
            while (lr.Read())
            {
                var bomId = Guid.Parse(lr.GetString(0));
                var line = new BomLine(
                    (BomLineType)(int)lr.GetInt64(1),
                    componentStockItemId: Guid.Parse(lr.GetString(2)),
                    quantityPerBlock: QtyMicroToDecimal(lr.GetInt64(4)),
                    godownId: lr.IsDBNull(3) ? (Guid?)null : Guid.Parse(lr.GetString(3)),
                    rate: lr.IsDBNull(5) ? (Money?)null : Paisa.ToMoney(lr.GetInt64(5)),
                    percentOfFinishedGoodCost: lr.IsDBNull(6) ? (decimal?)null : PercentMillisToDecimal(lr.GetInt64(6)));
                if (!linesByBom.TryGetValue(bomId, out var list)) linesByBom[bomId] = list = new List<BomLine>();
                list.Add(line);
            }
        }

        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            SELECT id, stock_item_id, name, unit_of_manufacture_micro
            FROM bill_of_materials WHERE company_id = $cid ORDER BY rowid;
            """;
        cmd.Parameters.AddWithValue("$cid", companyId.ToString("D"));
        using var r = cmd.ExecuteReader();
        var boms = new List<BillOfMaterials>();
        while (r.Read())
        {
            var id = Guid.Parse(r.GetString(0));
            boms.Add(new BillOfMaterials(
                id,
                stockItemId: Guid.Parse(r.GetString(1)),
                name: r.GetString(2),
                unitOfManufacture: QtyMicroToDecimal(r.GetInt64(3)),
                lines: linesByBom.TryGetValue(id, out var lines) ? lines : new List<BomLine>()));
        }
        return boms;
    }

    /// <summary>
    /// Reads the Reorder Level definitions (Phase 6 slice 6; RQ-32..RQ-35) from <c>reorder_definitions</c>. Micros
    /// (× 1,000,000) → exact decimal quantities; the Simple/Advanced flags, period unit/count and Higher/Lower
    /// criterion re-hydrate their enums; all NULLable Advanced columns stay null when neither figure is Advanced.
    /// </summary>
    private IEnumerable<ReorderDefinition> ReadReorderDefinitions(Guid companyId)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            SELECT id, scope, target_id, reorder_advanced, reorder_qty_micro, minqty_advanced,
                   min_order_qty_micro, period_unit, period_count, criteria
            FROM reorder_definitions WHERE company_id = $cid ORDER BY rowid;
            """;
        cmd.Parameters.AddWithValue("$cid", companyId.ToString("D"));
        using var r = cmd.ExecuteReader();
        var list = new List<ReorderDefinition>();
        while (r.Read())
        {
            list.Add(new ReorderDefinition(
                Guid.Parse(r.GetString(0)),
                scope: (ReorderScope)(int)r.GetInt64(1),
                targetId: Guid.Parse(r.GetString(2)),
                reorderAdvanced: r.GetInt64(3) != 0,
                reorderQuantity: r.IsDBNull(4) ? (decimal?)null : QtyMicroToDecimal(r.GetInt64(4)),
                minQtyAdvanced: r.GetInt64(5) != 0,
                minOrderQuantity: r.IsDBNull(6) ? (decimal?)null : QtyMicroToDecimal(r.GetInt64(6)),
                periodUnit: r.IsDBNull(7) ? (ExpiryPeriodUnit?)null : (ExpiryPeriodUnit)(int)r.GetInt64(7),
                periodCount: r.IsDBNull(8) ? (int?)null : (int)r.GetInt64(8),
                criteria: r.IsDBNull(9) ? (ReorderCriteria?)null : (ReorderCriteria)(int)r.GetInt64(9)));
        }
        return list;
    }

    /// <summary>Reads the named Price Levels (Phase 6 slice 5; RQ-26) — bare per-company masters.</summary>
    private IEnumerable<PriceLevel> ReadPriceLevels(Guid companyId)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT id, name FROM price_levels WHERE company_id = $cid ORDER BY rowid;";
        cmd.Parameters.AddWithValue("$cid", companyId.ToString("D"));
        using var r = cmd.ExecuteReader();
        var list = new List<PriceLevel>();
        while (r.Read())
            list.Add(new PriceLevel(Guid.Parse(r.GetString(0)), r.GetString(1)));
        return list;
    }

    /// <summary>
    /// Reads the dated Price List versions (Phase 6 slice 5; RQ-27/RQ-28) from <c>price_lists</c> +
    /// <c>price_list_lines</c>, reconstructing each header with its ordered slabs. Quantity micros (× 1,000,000) →
    /// exact decimal From/To; paisa → rate; discount millis (× 1,000) → exact decimal percent — all lossless
    /// (ER-10). Slabs are read in stored <c>line_order</c>; <c>to_qty_micro</c> NULL = open-ended top slab.
    /// </summary>
    private IEnumerable<PriceList> ReadPriceLists(Guid companyId)
    {
        var slabsByList = new Dictionary<Guid, List<PriceListSlab>>();
        using (var lineCmd = _connection.CreateCommand())
        {
            lineCmd.CommandText = """
                SELECT l.price_list_id, l.from_qty_micro, l.to_qty_micro, l.rate_paisa, l.discount_percent_millis
                FROM price_list_lines l
                JOIN price_lists p ON p.id = l.price_list_id
                WHERE p.company_id = $cid
                ORDER BY l.price_list_id, l.line_order;
                """;
            lineCmd.Parameters.AddWithValue("$cid", companyId.ToString("D"));
            using var lr = lineCmd.ExecuteReader();
            while (lr.Read())
            {
                var listId = Guid.Parse(lr.GetString(0));
                var slab = new PriceListSlab(
                    fromQty: QtyMicroToDecimal(lr.GetInt64(1)),
                    toQty: lr.IsDBNull(2) ? (decimal?)null : QtyMicroToDecimal(lr.GetInt64(2)),
                    rate: Paisa.ToMoney(lr.GetInt64(3)),
                    discountPercent: PercentMillisToDecimal(lr.GetInt64(4)));
                if (!slabsByList.TryGetValue(listId, out var slabs)) slabsByList[listId] = slabs = new List<PriceListSlab>();
                slabs.Add(slab);
            }
        }

        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            SELECT id, price_level_id, stock_item_id, applicable_from
            FROM price_lists WHERE company_id = $cid ORDER BY rowid;
            """;
        cmd.Parameters.AddWithValue("$cid", companyId.ToString("D"));
        using var r = cmd.ExecuteReader();
        var lists = new List<PriceList>();
        while (r.Read())
        {
            var id = Guid.Parse(r.GetString(0));
            lists.Add(new PriceList(
                id,
                priceLevelId: Guid.Parse(r.GetString(1)),
                stockItemId: Guid.Parse(r.GetString(2)),
                applicableFrom: ParseDate(r.GetString(3)),
                slabs: slabsByList.TryGetValue(id, out var slabs) ? slabs : new List<PriceListSlab>()));
        }
        return lists;
    }

    /// <summary>Reads the item GST block (columns 12–15), or <c>null</c> when gst_taxability is NULL.</summary>
    private static StockItemGstDetails? ReadStockItemGst(SqliteDataReader r)
    {
        if (r.IsDBNull(13)) return null; // gst_taxability NULL = no GST block
        return new StockItemGstDetails
        {
            HsnSac = r.IsDBNull(12) ? null : r.GetString(12),
            Taxability = (GstTaxability)(int)r.GetInt64(13),
            RateBasisPoints = r.IsDBNull(14) ? (int?)null : (int)r.GetInt64(14),
            SupplyType = r.IsDBNull(15) ? GstSupplyType.Goods : (GstSupplyType)(int)r.GetInt64(15),
            // v38 (Phase 9 slice 1): GST 2.0 RSP valuation + Compensation-Cess (default off/null for a plain item).
            ValuationBasis = (GstValuationBasis)(int)r.GetInt64(21),
            CessApplicable = r.GetInt64(22) != 0,
            CessValuationMode = r.IsDBNull(23) ? (CessValuationMode?)null : (CessValuationMode)(int)r.GetInt64(23),
            CessRateBasisPoints = r.IsDBNull(24) ? (int?)null : (int)r.GetInt64(24),
            CessPerUnit = r.IsDBNull(25) ? (Money?)null : Paisa.ToMoney(r.GetInt64(25)),
            CessRspFactorMillis = r.IsDBNull(26) ? (int?)null : (int)r.GetInt64(26),
            RetailSalePrice = r.IsDBNull(27) ? (Money?)null : Paisa.ToMoney(r.GetInt64(27)),
            // v39 (Phase 9 slice 2): reverse-charge (RCM) flags (default off/null for a plain item).
            ReverseChargeApplicable = r.GetInt64(28) != 0,
            GtaForwardCharge = r.GetInt64(29) != 0,
            RcmCategoryId = r.IsDBNull(30) ? (Guid?)null : Guid.Parse(r.GetString(30)),
            // v43 (Phase 9 slice 6): §17(5) ITC-eligibility (columns 31–32; default Eligible/None for a plain item).
            ItcEligibility = (ItcEligibility)(int)r.GetInt64(31),
            BlockedCreditCategory = (BlockedCreditCategory)(int)r.GetInt64(32),
        };
    }

    private IEnumerable<StockOpeningBalance> ReadStockOpeningBalances(Guid companyId)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            SELECT id, stock_item_id, godown_id, batch_label, quantity_micro, rate_paisa, mfg_date, expiry_date
            FROM stock_opening_balances WHERE company_id = $cid ORDER BY rowid;
            """;
        cmd.Parameters.AddWithValue("$cid", companyId.ToString("D"));
        using var r = cmd.ExecuteReader();
        var list = new List<StockOpeningBalance>();
        while (r.Read())
        {
            list.Add(new StockOpeningBalance(
                Guid.Parse(r.GetString(0)),
                stockItemId: Guid.Parse(r.GetString(1)),
                godownId: Guid.Parse(r.GetString(2)),
                quantity: QtyMicroToDecimal(r.GetInt64(4)),
                rate: Paisa.ToMoney(r.GetInt64(5)),
                batchLabel: r.IsDBNull(3) ? null : r.GetString(3),
                manufacturingDate: r.IsDBNull(6) ? (DateOnly?)null : ParseDate(r.GetString(6)),
                expiryDate: r.IsDBNull(7) ? (DateOnly?)null : ParseDate(r.GetString(7))));
        }
        return list;
    }

    private IEnumerable<InventoryVoucher> ReadInventoryVouchers(Guid companyId)
    {
        // Header rows first, then the four kinds of child line per voucher.
        var headers = new List<(Guid Id, Guid TypeId, int Number, DateOnly Date, string? Narration,
            Guid? PartyId, bool Cancelled, bool PostDated)>();
        using (var cmd = _connection.CreateCommand())
        {
            cmd.CommandText = """
                SELECT id, type_id, number, date, narration, party_id, cancelled, post_dated
                FROM inventory_vouchers WHERE company_id = $cid ORDER BY rowid;
                """;
            cmd.Parameters.AddWithValue("$cid", companyId.ToString("D"));
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                headers.Add((
                    Guid.Parse(r.GetString(0)),
                    Guid.Parse(r.GetString(1)),
                    (int)r.GetInt64(2),
                    ParseDate(r.GetString(3)),
                    r.IsDBNull(4) ? null : r.GetString(4),
                    r.IsDBNull(5) ? (Guid?)null : Guid.Parse(r.GetString(5)),
                    r.GetInt64(6) != 0,
                    r.GetInt64(7) != 0));
            }
        }

        var result = new List<InventoryVoucher>();
        foreach (var h in headers)
        {
            var (source, destination) = ReadInventoryAllocations(h.Id);
            result.Add(InventoryVoucher.FromStorage(
                h.Id, h.TypeId, h.Date,
                allocations: source,
                destinationAllocations: destination,
                orderLines: ReadOrderLines(h.Id),
                physicalLines: ReadPhysicalStockLines(h.Id),
                number: h.Number,
                narration: h.Narration,
                partyId: h.PartyId,
                cancelled: h.Cancelled,
                postDated: h.PostDated,
                additionalCostLines: ReadAdditionalCostLines(h.Id),
                jobWorkOrder: ReadJobWorkOrder(h.Id),
                orderLinks: ReadMaterialOrderLinks(h.Id)));
        }
        return result;
    }

    /// <summary>Reads the Job Work Order payload for one inventory voucher (v24; RQ-47), or <c>null</c> when the
    /// voucher is not a Job Work order. job_work_orders is 1:1 with inventory_vouchers (its id == the voucher id).</summary>
    private JobWorkOrder? ReadJobWorkOrder(Guid voucherId)
    {
        JobWorkDirection direction;
        string orderNo;
        string? duration, nature;
        Guid fgItem;
        decimal fgQty;
        DateOnly? fgDue;
        Guid? fgGodown;
        Money? fgRate;
        bool tracking;
        Guid? bomId;

        using (var cmd = _connection.CreateCommand())
        {
            cmd.CommandText = """
                SELECT direction, order_no, duration_of_process, nature_of_processing, fg_stock_item_id, fg_qty_micro,
                       fg_due_date, fg_godown_id, fg_rate_paisa, tracking_components, fill_components_bom_id
                FROM job_work_orders WHERE inventory_voucher_id = $vid;
                """;
            cmd.Parameters.AddWithValue("$vid", voucherId.ToString("D"));
            using var r = cmd.ExecuteReader();
            if (!r.Read()) return null;
            direction = (JobWorkDirection)(int)r.GetInt64(0);
            orderNo = r.GetString(1);
            duration = r.IsDBNull(2) ? null : r.GetString(2);
            nature = r.IsDBNull(3) ? null : r.GetString(3);
            fgItem = Guid.Parse(r.GetString(4));
            fgQty = QtyMicroToDecimal(r.GetInt64(5));
            fgDue = r.IsDBNull(6) ? (DateOnly?)null : ParseDate(r.GetString(6));
            fgGodown = r.IsDBNull(7) ? (Guid?)null : Guid.Parse(r.GetString(7));
            fgRate = r.IsDBNull(8) ? null : Paisa.ToMoney(r.GetInt64(8));
            tracking = r.GetInt64(9) != 0;
            bomId = r.IsDBNull(10) ? (Guid?)null : Guid.Parse(r.GetString(10));
        }

        var lines = new List<JobWorkOrderLine>();
        using (var cmd = _connection.CreateCommand())
        {
            cmd.CommandText = """
                SELECT component_stock_item_id, track, due_date, godown_id, qty_micro, rate_paisa
                FROM job_work_order_lines WHERE job_work_order_id = $jwo ORDER BY line_order, id;
                """;
            cmd.Parameters.AddWithValue("$jwo", voucherId.ToString("D"));
            using var r = cmd.ExecuteReader();
            while (r.Read())
                lines.Add(new JobWorkOrderLine(
                    Guid.Parse(r.GetString(0)),
                    (JobWorkComponentTrack)(int)r.GetInt64(1),
                    QtyMicroToDecimal(r.GetInt64(4)),
                    godownId: r.IsDBNull(3) ? (Guid?)null : Guid.Parse(r.GetString(3)),
                    dueDate: r.IsDBNull(2) ? (DateOnly?)null : ParseDate(r.GetString(2)),
                    rate: r.IsDBNull(5) ? null : Paisa.ToMoney(r.GetInt64(5))));
        }

        return new JobWorkOrder(
            direction, orderNo, fgItem, fgQty, lines,
            finishedGoodRate: fgRate, finishedGoodDueDate: fgDue, finishedGoodGodownId: fgGodown,
            trackingComponents: tracking, fillComponentsBomId: bomId,
            durationOfProcess: duration, natureOfProcessing: nature);
    }

    /// <summary>Reads the fulfilled Job Work order ids linked to a Material In/Out voucher (v24; RQ-48).</summary>
    private List<Guid> ReadMaterialOrderLinks(Guid voucherId)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            SELECT job_work_order_id FROM material_order_links WHERE material_voucher_id = $vid ORDER BY id;
            """;
        cmd.Parameters.AddWithValue("$vid", voucherId.ToString("D"));
        using var r = cmd.ExecuteReader();
        var list = new List<Guid>();
        while (r.Read())
            list.Add(Guid.Parse(r.GetString(0)));
        return list;
    }

    private List<AdditionalCostLine> ReadAdditionalCostLines(Guid voucherId)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            SELECT ledger_id, amount_paisa
            FROM additional_cost_lines WHERE inventory_voucher_id = $vid ORDER BY line_order, id;
            """;
        cmd.Parameters.AddWithValue("$vid", voucherId.ToString("D"));
        using var r = cmd.ExecuteReader();
        var list = new List<AdditionalCostLine>();
        while (r.Read())
            list.Add(new AdditionalCostLine(Guid.Parse(r.GetString(0)), Paisa.ToMoney(r.GetInt64(1))));
        return list;
    }

    private (List<InventoryAllocation> Source, List<InventoryAllocation> Destination) ReadInventoryAllocations(Guid voucherId)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            SELECT role, stock_item_id, godown_id, unit_id, quantity_micro, direction, rate_paisa, batch_label
            FROM inventory_allocations WHERE inventory_voucher_id = $vid ORDER BY line_order, id;
            """;
        cmd.Parameters.AddWithValue("$vid", voucherId.ToString("D"));
        using var r = cmd.ExecuteReader();
        var source = new List<InventoryAllocation>();
        var destination = new List<InventoryAllocation>();
        while (r.Read())
        {
            var alloc = new InventoryAllocation(
                Guid.Parse(r.GetString(1)),
                Guid.Parse(r.GetString(2)),
                QtyMicroToDecimal(r.GetInt64(4)),
                (StockDirection)(int)r.GetInt64(5),
                rate: r.IsDBNull(6) ? null : Paisa.ToMoney(r.GetInt64(6)),
                batchLabel: r.IsDBNull(7) ? null : r.GetString(7),
                unitId: r.IsDBNull(3) ? (Guid?)null : Guid.Parse(r.GetString(3)));
            if (r.GetInt64(0) == 1) destination.Add(alloc);
            else source.Add(alloc);
        }
        return (source, destination);
    }

    private List<OrderLine> ReadOrderLines(Guid voucherId)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            SELECT stock_item_id, godown_id, quantity_micro, rate_paisa
            FROM order_lines WHERE inventory_voucher_id = $vid ORDER BY line_order, id;
            """;
        cmd.Parameters.AddWithValue("$vid", voucherId.ToString("D"));
        using var r = cmd.ExecuteReader();
        var list = new List<OrderLine>();
        while (r.Read())
        {
            list.Add(new OrderLine(
                Guid.Parse(r.GetString(0)),
                Guid.Parse(r.GetString(1)),
                QtyMicroToDecimal(r.GetInt64(2)),
                r.IsDBNull(3) ? null : Paisa.ToMoney(r.GetInt64(3))));
        }
        return list;
    }

    private List<PhysicalStockLine> ReadPhysicalStockLines(Guid voucherId)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            SELECT stock_item_id, godown_id, counted_qty_micro, batch_label
            FROM physical_stock_lines WHERE inventory_voucher_id = $vid ORDER BY line_order, id;
            """;
        cmd.Parameters.AddWithValue("$vid", voucherId.ToString("D"));
        using var r = cmd.ExecuteReader();
        var list = new List<PhysicalStockLine>();
        while (r.Read())
        {
            list.Add(new PhysicalStockLine(
                Guid.Parse(r.GetString(0)),
                Guid.Parse(r.GetString(1)),
                QtyMicroToDecimal(r.GetInt64(2)),
                r.IsDBNull(3) ? null : r.GetString(3)));
        }
        return list;
    }

    private IEnumerable<Budget> ReadBudgets(Guid companyId)
    {
        // Header rows first, then lines per budget (ordered), to build each aggregate.
        var headers = new List<(Guid Id, string Name, Guid? UnderId, DateOnly From, DateOnly To)>();
        using (var cmd = _connection.CreateCommand())
        {
            cmd.CommandText = """
                SELECT id, name, under_id, period_from, period_to
                FROM budgets WHERE company_id = $cid ORDER BY rowid;
                """;
            cmd.Parameters.AddWithValue("$cid", companyId.ToString("D"));
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                headers.Add((
                    Guid.Parse(r.GetString(0)),
                    r.GetString(1),
                    r.IsDBNull(2) ? (Guid?)null : Guid.Parse(r.GetString(2)),
                    ParseDate(r.GetString(3)),
                    ParseDate(r.GetString(4))));
            }
        }

        var result = new List<Budget>();
        foreach (var h in headers)
            result.Add(new Budget(h.Id, h.Name, h.From, h.To, underId: h.UnderId, lines: ReadBudgetLines(h.Id)));
        return result;
    }

    private List<BudgetLine> ReadBudgetLines(Guid budgetId)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            SELECT group_id, ledger_id, budget_type, amount_paisa
            FROM budget_lines WHERE budget_id = $bid ORDER BY line_order, id;
            """;
        cmd.Parameters.AddWithValue("$bid", budgetId.ToString("D"));
        using var r = cmd.ExecuteReader();
        var list = new List<BudgetLine>();
        while (r.Read())
        {
            var type = (BudgetType)(int)r.GetInt64(2);
            var amount = Paisa.ToMoney(r.GetInt64(3));
            list.Add(r.IsDBNull(0)
                ? BudgetLine.ForLedger(Guid.Parse(r.GetString(1)), type, amount)
                : BudgetLine.ForGroup(Guid.Parse(r.GetString(0)), type, amount));
        }
        return list;
    }

    private IEnumerable<Scenario> ReadScenarios(Guid companyId)
    {
        // Header rows first, then include/exclude voucher-type rows per scenario.
        var headers = new List<(Guid Id, string Name, bool IncludeActuals)>();
        using (var cmd = _connection.CreateCommand())
        {
            cmd.CommandText = """
                SELECT id, name, include_actuals
                FROM scenarios WHERE company_id = $cid ORDER BY rowid;
                """;
            cmd.Parameters.AddWithValue("$cid", companyId.ToString("D"));
            using var r = cmd.ExecuteReader();
            while (r.Read())
                headers.Add((Guid.Parse(r.GetString(0)), r.GetString(1), r.GetInt64(2) != 0));
        }

        var result = new List<Scenario>();
        foreach (var h in headers)
        {
            var (included, excluded) = ReadScenarioVoucherTypes(h.Id);
            result.Add(new Scenario(h.Id, h.Name, h.IncludeActuals, included, excluded));
        }
        return result;
    }

    private (List<Guid> Included, List<Guid> Excluded) ReadScenarioVoucherTypes(Guid scenarioId)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            SELECT voucher_type_id, is_included
            FROM scenario_voucher_types WHERE scenario_id = $sid ORDER BY id;
            """;
        cmd.Parameters.AddWithValue("$sid", scenarioId.ToString("D"));
        using var r = cmd.ExecuteReader();
        var included = new List<Guid>();
        var excluded = new List<Guid>();
        while (r.Read())
        {
            var id = Guid.Parse(r.GetString(0));
            if (r.GetInt64(1) != 0) included.Add(id);
            else excluded.Add(id);
        }
        return (included, excluded);
    }

    private IEnumerable<Voucher> ReadVouchers(Guid companyId)
    {
        // Header rows first, then lines per voucher (ordered), to build each aggregate.
        var headers = new List<(Guid Id, Guid TypeId, int Number, DateOnly Date, string? Narration,
            Guid? PartyId, bool Cancelled, bool Optional, bool PostDated, DateOnly? ApplicableUpto)>();

        using (var cmd = _connection.CreateCommand())
        {
            cmd.CommandText = """
                SELECT id, type_id, number, date, narration, party_id, cancelled, optional, post_dated,
                       applicable_upto
                FROM vouchers WHERE company_id = $cid ORDER BY rowid;
                """;
            cmd.Parameters.AddWithValue("$cid", companyId.ToString("D"));
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                headers.Add((
                    Guid.Parse(r.GetString(0)),
                    Guid.Parse(r.GetString(1)),
                    (int)r.GetInt64(2),
                    ParseDate(r.GetString(3)),
                    r.IsDBNull(4) ? null : r.GetString(4),
                    r.IsDBNull(5) ? (Guid?)null : Guid.Parse(r.GetString(5)),
                    r.GetInt64(6) != 0,
                    r.GetInt64(7) != 0,
                    r.GetInt64(8) != 0,
                    r.IsDBNull(9) ? (DateOnly?)null : ParseDate(r.GetString(9))));
            }
        }

        var result = new List<Voucher>();
        foreach (var h in headers)
        {
            var lines = ReadEntryLines(h.Id);
            var inventoryLines = ReadVoucherInventoryLines(h.Id);
            var posTenders = ReadPosTenders(h.Id);
            result.Add(new Voucher(
                h.Id, h.TypeId, h.Date, lines,
                number: h.Number,
                narration: h.Narration,
                partyId: h.PartyId,
                cancelled: h.Cancelled,
                optional: h.Optional,
                postDated: h.PostDated,
                applicableUpto: h.ApplicableUpto,
                inventoryLines: inventoryLines.Count > 0 ? inventoryLines : null,
                posTenders: posTenders.Count > 0 ? posTenders : null));
        }
        return result;
    }

    private List<VoucherInventoryLine> ReadVoucherInventoryLines(Guid voucherId)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            SELECT stock_item_id, godown_id, quantity_micro, direction, rate_paisa, batch_label,
                   billed_qty_micro
            FROM voucher_inventory_lines WHERE voucher_id = $vid ORDER BY line_order, id;
            """;
        cmd.Parameters.AddWithValue("$vid", voucherId.ToString("D"));
        using var r = cmd.ExecuteReader();
        var list = new List<VoucherInventoryLine>();
        while (r.Read())
        {
            // v20 (RQ-22/RQ-23): quantity_micro is the Actual (stock) quantity; Billed = billed_qty_micro when
            // present, else defaults to Actual (feature off / no split — byte-identical, ER-13).
            var actual = QtyMicroToDecimal(r.GetInt64(2));
            var billed = r.IsDBNull(6) ? actual : QtyMicroToDecimal(r.GetInt64(6));
            list.Add(new VoucherInventoryLine(
                Guid.Parse(r.GetString(0)),
                Guid.Parse(r.GetString(1)),
                actual,
                Paisa.ToMoney(r.GetInt64(4)),
                (StockDirection)(int)r.GetInt64(3),
                batchLabel: r.IsDBNull(5) ? null : r.GetString(5),
                billedQuantity: billed));
        }
        return list;
    }

    private List<EntryLine> ReadEntryLines(Guid voucherId)
    {
        // Read the raw line rows first (capturing the id), then load each line's bill allocations.
        var raw = new List<(long Id, Guid LedgerId, long AmountPaisa, DrCr Side, ForexInfo? Forex, GstLineTax? Gst)>();
        using (var cmd = _connection.CreateCommand())
        {
            cmd.CommandText = """
                SELECT id, ledger_id, amount_paisa, side,
                       forex_currency_id, forex_amount_micro, forex_rate_micro,
                       gst_tax_head, gst_rate_bp, gst_taxable_value_paisa,
                       gst_is_reverse_charge, gst_rcm_scheme, gst_adjustment_kind
                FROM entry_lines WHERE voucher_id = $vid ORDER BY line_order, id;
                """;
            cmd.Parameters.AddWithValue("$vid", voucherId.ToString("D"));
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                ForexInfo? forex = null;
                if (!r.IsDBNull(4))
                {
                    forex = new ForexInfo(
                        Guid.Parse(r.GetString(4)),
                        Money.FromRupees(MicroToDecimal(r.GetInt64(5))),
                        MicroToDecimal(r.GetInt64(6)));
                }

                // v13 GST tax-line detail: present iff gst_tax_head is set. v39 (Phase 9 slice 2) adds the reverse-charge
                // tag (gst_is_reverse_charge / gst_rcm_scheme), default 0/NULL for a forward-charge line.
                GstLineTax? gst = null;
                if (!r.IsDBNull(7))
                {
                    gst = new GstLineTax(
                        (GstTaxHead)(int)r.GetInt64(7),
                        (int)r.GetInt64(8),
                        Paisa.ToMoney(r.GetInt64(9)),
                        isReverseCharge: r.GetInt64(10) != 0,
                        rcmScheme: r.IsDBNull(11) ? (RcmItcScheme?)null : (RcmItcScheme)(int)r.GetInt64(11),
                        // v44 (Phase 9 slice 7): the set-off / cash-payment / reversal adjustment tag (NULL ⇒ forward line).
                        adjustment: r.IsDBNull(12) ? (GstAdjustmentKind?)null : (GstAdjustmentKind)(int)r.GetInt64(12));
                }

                raw.Add((
                    r.GetInt64(0),
                    Guid.Parse(r.GetString(1)),
                    r.GetInt64(2),
                    (DrCr)(int)r.GetInt64(3),
                    forex,
                    gst));
            }
        }

        var lines = new List<EntryLine>(raw.Count);
        foreach (var (id, ledgerId, amountPaisa, side, forex, gst) in raw)
        {
            var allocs = ReadBillAllocations(id);
            var costAllocs = ReadCostAllocations(id);
            var bankAlloc = ReadBankAllocation(id);
            var tds = ReadTdsLine(id); // v26: TDS withholding detail (null for a non-TDS line)
            var tcs = ReadTcsLine(id); // v28: TCS collection detail (null for a non-TCS line)
            var payroll = ReadPayrollLine(id); // v32: payroll detail (null for a non-payroll line)
            lines.Add(new EntryLine(
                ledgerId,
                Paisa.ToMoney(amountPaisa),
                side,
                allocs.Count > 0 ? allocs : null,
                costAllocs.Count > 0 ? costAllocs : null,
                bankAlloc,
                forex,
                gst,
                tds,
                tcs,
                payroll));
        }
        return lines;
    }

    /// <summary>v32: reads the payroll detail hung off an entry line, or <c>null</c> for a non-payroll line.</summary>
    private PayrollLineDetail? ReadPayrollLine(long entryLineId)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            SELECT employee_id, pay_head_id, category, amount_micro
            FROM payroll_lines WHERE entry_line_id = $lid LIMIT 1;
            """;
        cmd.Parameters.AddWithValue("$lid", entryLineId);
        using var r = cmd.ExecuteReader();
        if (!r.Read()) return null;
        return new PayrollLineDetail(
            Guid.Parse(r.GetString(0)),
            r.IsDBNull(1) ? (Guid?)null : Guid.Parse(r.GetString(1)),
            (PayrollLineCategory)(int)r.GetInt64(2),
            new Money(r.GetInt64(3) / 1_000_000m));
    }

    /// <summary>v28: reads the TCS collection detail hung off an entry line, or <c>null</c> for a non-TCS line.</summary>
    private TcsLineTax? ReadTcsLine(long entryLineId)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            SELECT nature_id, collection_code, assessable_value_micro, rate_bp, tcs_amount_micro,
                   collectee_ledger_id, pan_applied
            FROM tcs_lines WHERE entry_line_id = $lid LIMIT 1;
            """;
        cmd.Parameters.AddWithValue("$lid", entryLineId);
        using var r = cmd.ExecuteReader();
        if (!r.Read()) return null;
        return new TcsLineTax(
            Guid.Parse(r.GetString(0)),
            r.GetString(1),
            new Money(r.GetInt64(2) / 1_000_000m),
            (int)r.GetInt64(3),
            new Money(r.GetInt64(4) / 1_000_000m),
            Guid.Parse(r.GetString(5)),
            r.GetInt64(6) != 0);
    }

    /// <summary>v26: reads the TDS withholding detail hung off an entry line, or <c>null</c> for a non-TDS line.</summary>
    private TdsLineTax? ReadTdsLine(long entryLineId)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            SELECT nature_id, section_code, assessable_value_micro, rate_bp, tds_amount_micro,
                   deductee_ledger_id, pan_applied
            FROM tds_lines WHERE entry_line_id = $lid LIMIT 1;
            """;
        cmd.Parameters.AddWithValue("$lid", entryLineId);
        using var r = cmd.ExecuteReader();
        if (!r.Read()) return null;
        return new TdsLineTax(
            Guid.Parse(r.GetString(0)),
            r.GetString(1),
            new Money(r.GetInt64(2) / 1_000_000m),
            (int)r.GetInt64(3),
            new Money(r.GetInt64(4) / 1_000_000m),
            Guid.Parse(r.GetString(5)),
            r.GetInt64(6) != 0);
    }

    private List<BillAllocation> ReadBillAllocations(long entryLineId)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            SELECT ref_type, name, amount_paisa, due_date, credit_days
            FROM bill_allocations WHERE entry_line_id = $lid ORDER BY alloc_order, id;
            """;
        cmd.Parameters.AddWithValue("$lid", entryLineId);
        using var r = cmd.ExecuteReader();
        var list = new List<BillAllocation>();
        while (r.Read())
        {
            list.Add(new BillAllocation(
                (BillRefType)(int)r.GetInt64(0),
                r.GetString(1),
                Paisa.ToMoney(r.GetInt64(2)),
                dueDate: r.IsDBNull(3) ? (DateOnly?)null : ParseDate(r.GetString(3)),
                creditPeriodDays: r.IsDBNull(4) ? (int?)null : (int)r.GetInt64(4)));
        }
        return list;
    }

    private List<CostAllocation> ReadCostAllocations(long entryLineId)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            SELECT category_id, centre_id, amount_paisa
            FROM cost_allocations WHERE entry_line_id = $lid ORDER BY alloc_order, id;
            """;
        cmd.Parameters.AddWithValue("$lid", entryLineId);
        using var r = cmd.ExecuteReader();
        var list = new List<CostAllocation>();
        while (r.Read())
        {
            list.Add(new CostAllocation(
                Guid.Parse(r.GetString(0)),
                Guid.Parse(r.GetString(1)),
                Paisa.ToMoney(r.GetInt64(2))));
        }
        return list;
    }

    /// <summary>Reads the (at most one) bank allocation for an entry line, or <c>null</c> if none.</summary>
    private BankAllocation? ReadBankAllocation(long entryLineId)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            SELECT transaction_type, instrument_number, instrument_date, bank_date
            FROM bank_allocations WHERE entry_line_id = $lid ORDER BY id LIMIT 1;
            """;
        cmd.Parameters.AddWithValue("$lid", entryLineId);
        using var r = cmd.ExecuteReader();
        if (!r.Read()) return null;
        return new BankAllocation(
            (BankTransactionType)(int)r.GetInt64(0),
            instrumentNumber: r.GetString(1),
            instrumentDate: r.IsDBNull(2) ? (DateOnly?)null : ParseDate(r.GetString(2)),
            bankDate: r.IsDBNull(3) ? (DateOnly?)null : ParseDate(r.GetString(3)));
    }

    // ------------------------------------------------------------------ writers

    private void DeleteCompanyRows(SqliteTransaction tx, Guid companyId)
    {
        // Child-first so foreign keys are satisfied. Break the company→head fk before deleting groups.
        var cid = companyId.ToString("D");
        ExecTx(tx, "UPDATE companies SET profit_and_loss_head_id = NULL WHERE id = $cid;", ("$cid", cid));
        ExecTx(tx, """
            DELETE FROM bill_allocations WHERE entry_line_id IN (
                SELECT el.id FROM entry_lines el
                JOIN vouchers v ON v.id = el.voucher_id
                WHERE v.company_id = $cid);
            """, ("$cid", cid));
        ExecTx(tx, """
            DELETE FROM cost_allocations WHERE entry_line_id IN (
                SELECT el.id FROM entry_lines el
                JOIN vouchers v ON v.id = el.voucher_id
                WHERE v.company_id = $cid);
            """, ("$cid", cid));
        ExecTx(tx, """
            DELETE FROM bank_allocations WHERE entry_line_id IN (
                SELECT el.id FROM entry_lines el
                JOIN vouchers v ON v.id = el.voucher_id
                WHERE v.company_id = $cid);
            """, ("$cid", cid));
        // Item-invoice stock lines FK the accounting voucher + stock masters → delete before vouchers and the
        // stock masters below.
        ExecTx(tx, """
            DELETE FROM voucher_inventory_lines WHERE voucher_id IN (
                SELECT id FROM vouchers WHERE company_id = $cid);
            """, ("$cid", cid));
        // v23 POS tender allocations FK the accounting voucher + a ledger → delete before vouchers and before
        // ledgers below (Phase 6 slice 7).
        ExecTx(tx, """
            DELETE FROM pos_tender_allocations WHERE voucher_id IN (
                SELECT id FROM vouchers WHERE company_id = $cid);
            """, ("$cid", cid));
        // v23 POS voucher-type config + tender-ledger class map FK voucher_types + ledgers + godowns → delete
        // before those masters below (they are keyed by voucher_type_id).
        ExecTx(tx, """
            DELETE FROM pos_tender_ledger_defaults WHERE voucher_type_id IN (
                SELECT id FROM voucher_types WHERE company_id = $cid);
            """, ("$cid", cid));
        ExecTx(tx, """
            DELETE FROM pos_voucher_type_config WHERE voucher_type_id IN (
                SELECT id FROM voucher_types WHERE company_id = $cid);
            """, ("$cid", cid));
        // v27 challan-voucher links FK tds_challans + vouchers; tds_challans FK companies → delete the links first,
        // then the challans, before vouchers and the company row below (Phase 7 slice 3).
        ExecTx(tx, """
            DELETE FROM challan_voucher_links WHERE challan_id IN (
                SELECT id FROM tds_challans WHERE company_id = $cid);
            """, ("$cid", cid));
        ExecTx(tx, "DELETE FROM tds_challans WHERE company_id = $cid;", ("$cid", cid));
        // v29 TCS challan-voucher links FK tcs_challans + vouchers; tcs_challans FK companies → delete the links
        // first, then the challans, before vouchers and the company row below (Phase 7 slice 6).
        ExecTx(tx, """
            DELETE FROM tcs_challan_voucher_links WHERE challan_id IN (
                SELECT id FROM tcs_challans WHERE company_id = $cid);
            """, ("$cid", cid));
        ExecTx(tx, "DELETE FROM tcs_challans WHERE company_id = $cid;", ("$cid", cid));
        // v39 RCM documents + §34-CDN links + GST-on-advance receipts FK vouchers (source/cdn/receipt/adjustment/refund) +
        // companies → delete before vouchers and the company row below (Phase 9 slice 2).
        ExecTx(tx, "DELETE FROM rcm_documents WHERE company_id = $cid;", ("$cid", cid));
        // v41 e-invoice IRP artefacts FK the source voucher → delete before vouchers (Phase 9 slice 4a).
        ExecTx(tx, "DELETE FROM einvoice_records WHERE company_id = $cid;", ("$cid", cid));
        // v42 e-Way Bill artefacts FK the source voucher → delete before vouchers; state overrides FK companies only
        // (Phase 9 slice 5).
        ExecTx(tx, "DELETE FROM eway_bills WHERE company_id = $cid;", ("$cid", cid));
        ExecTx(tx, "DELETE FROM eway_state_thresholds WHERE company_id = $cid;", ("$cid", cid));
        ExecTx(tx, "DELETE FROM gst_cdn_links WHERE company_id = $cid;", ("$cid", cid));
        ExecTx(tx, "DELETE FROM gst_advance_receipts WHERE company_id = $cid;", ("$cid", cid));
        // v44 set-off / reversal / challan / DRC-03 (Phase 9 slice 7): itc_reversals FKs gst_drc03 + gstr2b_lines +
        // vouchers → delete it FIRST; the other three FK vouchers → delete before vouchers below. (itc_reversals is
        // empty in S7a — the engine is S7b — but the order is correct for when it fills.)
        ExecTx(tx, "DELETE FROM itc_reversals WHERE company_id = $cid;", ("$cid", cid));
        ExecTx(tx, "DELETE FROM gst_setoff_lines WHERE company_id = $cid;", ("$cid", cid));
        ExecTx(tx, "DELETE FROM gst_challans WHERE company_id = $cid;", ("$cid", cid));
        ExecTx(tx, "DELETE FROM gst_drc03 WHERE company_id = $cid;", ("$cid", cid));
        // v43 GSTR-2B recon rows FK gstr2b_lines + vouchers; ims_status FK gstr2b_lines; gstr2b_lines FK
        // gstr2b_snapshots → delete children before parents, and all before vouchers below (Phase 9 slice 6).
        ExecTx(tx, "DELETE FROM gstr2b_recon WHERE company_id = $cid;", ("$cid", cid));
        ExecTx(tx, "DELETE FROM ims_status WHERE company_id = $cid;", ("$cid", cid));
        ExecTx(tx, "DELETE FROM gstr2b_lines WHERE company_id = $cid;", ("$cid", cid));
        ExecTx(tx, "DELETE FROM gstr2b_snapshots WHERE company_id = $cid;", ("$cid", cid));
        // v26 TDS withholding detail FKs the entry line → delete before entry_lines (Phase 7 slice 2).
        ExecTx(tx, """
            DELETE FROM tds_lines WHERE entry_line_id IN (
                SELECT el.id FROM entry_lines el
                JOIN vouchers v ON v.id = el.voucher_id
                WHERE v.company_id = $cid);
            """, ("$cid", cid));
        // v28 TCS collection detail FKs the entry line → delete before entry_lines (Phase 7 slice 5).
        ExecTx(tx, """
            DELETE FROM tcs_lines WHERE entry_line_id IN (
                SELECT el.id FROM entry_lines el
                JOIN vouchers v ON v.id = el.voucher_id
                WHERE v.company_id = $cid);
            """, ("$cid", cid));
        // v32 payroll detail FKs the entry line → delete before entry_lines (Phase 8 slice 3).
        ExecTx(tx, """
            DELETE FROM payroll_lines WHERE entry_line_id IN (
                SELECT el.id FROM entry_lines el
                JOIN vouchers v ON v.id = el.voucher_id
                WHERE v.company_id = $cid);
            """, ("$cid", cid));
        ExecTx(tx, "DELETE FROM entry_lines WHERE voucher_id IN (SELECT id FROM vouchers WHERE company_id = $cid);", ("$cid", cid));
        ExecTx(tx, "DELETE FROM vouchers WHERE company_id = $cid;", ("$cid", cid));
        // Inventory & order vouchers: child lines FK the header; the header FKs voucher_types + a party ledger
        // + stock masters → delete these before voucher_types / ledgers / stock masters below.
        ExecTx(tx, """
            DELETE FROM inventory_allocations WHERE inventory_voucher_id IN (
                SELECT id FROM inventory_vouchers WHERE company_id = $cid);
            """, ("$cid", cid));
        ExecTx(tx, """
            DELETE FROM order_lines WHERE inventory_voucher_id IN (
                SELECT id FROM inventory_vouchers WHERE company_id = $cid);
            """, ("$cid", cid));
        ExecTx(tx, """
            DELETE FROM physical_stock_lines WHERE inventory_voucher_id IN (
                SELECT id FROM inventory_vouchers WHERE company_id = $cid);
            """, ("$cid", cid));
        // v19 additional-cost lines FK the inventory-voucher header AND a ledger → delete before the header and
        // before ledgers below (FK order).
        ExecTx(tx, """
            DELETE FROM additional_cost_lines WHERE inventory_voucher_id IN (
                SELECT id FROM inventory_vouchers WHERE company_id = $cid);
            """, ("$cid", cid));
        // v24 Job Work: material_order_links FK inventory_vouchers + job_work_orders; job_work_order_lines FK
        // job_work_orders; job_work_orders FK inventory_vouchers + stock_items + godowns + bill_of_materials →
        // delete links, then lines, then headers, all before inventory_vouchers / stock masters / BOM below.
        ExecTx(tx, """
            DELETE FROM material_order_links WHERE material_voucher_id IN (
                SELECT id FROM inventory_vouchers WHERE company_id = $cid);
            """, ("$cid", cid));
        ExecTx(tx, """
            DELETE FROM job_work_order_lines WHERE job_work_order_id IN (
                SELECT id FROM job_work_orders WHERE company_id = $cid);
            """, ("$cid", cid));
        ExecTx(tx, "DELETE FROM job_work_orders WHERE company_id = $cid;", ("$cid", cid));
        ExecTx(tx, "DELETE FROM inventory_vouchers WHERE company_id = $cid;", ("$cid", cid));
        // Budgets (and their lines) FK groups + ledgers → delete them before those masters.
        ExecTx(tx, """
            DELETE FROM budget_lines WHERE budget_id IN (SELECT id FROM budgets WHERE company_id = $cid);
            """, ("$cid", cid));
        ExecTx(tx, "DELETE FROM budgets WHERE company_id = $cid;", ("$cid", cid));
        // Scenarios (and their voucher-type rows) FK voucher_types → delete them before those masters.
        ExecTx(tx, """
            DELETE FROM scenario_voucher_types WHERE scenario_id IN (
                SELECT id FROM scenarios WHERE company_id = $cid);
            """, ("$cid", cid));
        ExecTx(tx, "DELETE FROM scenarios WHERE company_id = $cid;", ("$cid", cid));
        // Price lists (and their slab lines) FK price_levels + stock_items; a ledger's default_price_level_id FKs
        // price_levels → delete the lines + lists first, then ledgers, then the price_levels master last (slice 5).
        ExecTx(tx, """
            DELETE FROM price_list_lines WHERE price_list_id IN (
                SELECT id FROM price_lists WHERE company_id = $cid);
            """, ("$cid", cid));
        ExecTx(tx, "DELETE FROM price_lists WHERE company_id = $cid;", ("$cid", cid));
        // v31 Payroll pay heads + salary structures (Phase 8 slice 2): child rows first, then pay_heads — all before
        // ledgers/groups/attendance_types (which pay_heads FK) are dropped below. salary_structure_lines FK
        // pay_heads + salary_structures; pay_head_computation(_slabs) FK pay_heads.
        ExecTx(tx, """
            DELETE FROM salary_structure_lines WHERE salary_structure_id IN (
                SELECT id FROM salary_structures WHERE company_id = $cid);
            """, ("$cid", cid));
        ExecTx(tx, "DELETE FROM salary_structures WHERE company_id = $cid;", ("$cid", cid));
        ExecTx(tx, """
            DELETE FROM pay_head_computation_slabs WHERE pay_head_id IN (
                SELECT id FROM pay_heads WHERE company_id = $cid);
            """, ("$cid", cid));
        ExecTx(tx, """
            DELETE FROM pay_head_computation WHERE pay_head_id IN (
                SELECT id FROM pay_heads WHERE company_id = $cid);
            """, ("$cid", cid));
        ExecTx(tx, "DELETE FROM pay_heads WHERE company_id = $cid;", ("$cid", cid));
        ExecTx(tx, "DELETE FROM ledgers WHERE company_id = $cid;", ("$cid", cid));
        // price_levels is referenced by ledgers (default) + price_lists, both deleted above → safe to drop now.
        ExecTx(tx, "DELETE FROM price_levels WHERE company_id = $cid;", ("$cid", cid));
        // Reorder definitions reference only companies(id) (target_id is a bare id, no FK) → drop before the
        // company row and before the stock masters they logically point at (Phase 6 slice 6).
        ExecTx(tx, "DELETE FROM reorder_definitions WHERE company_id = $cid;", ("$cid", cid));
        // Exchange rates FK currencies; ledgers + entry-line forex FK currencies → after those are gone.
        ExecTx(tx, "DELETE FROM exchange_rates WHERE company_id = $cid;", ("$cid", cid));
        ExecTx(tx, "DELETE FROM currencies WHERE company_id = $cid;", ("$cid", cid));
        ExecTx(tx, "DELETE FROM voucher_types WHERE company_id = $cid;", ("$cid", cid));
        // Cost centres reference cost categories → delete centres first.
        ExecTx(tx, "DELETE FROM cost_centres WHERE company_id = $cid;", ("$cid", cid));
        ExecTx(tx, "DELETE FROM cost_categories WHERE company_id = $cid;", ("$cid", cid));
        // Inventory: openings FK items+godowns; items FK groups/categories/units → delete child-first.
        ExecTx(tx, "DELETE FROM stock_opening_balances WHERE company_id = $cid;", ("$cid", cid));
        // Bill-of-Materials lines FK the header; the header FKs stock_items + godowns → delete lines, then headers,
        // before items + godowns below.
        ExecTx(tx, """
            DELETE FROM bom_lines WHERE bom_id IN (SELECT id FROM bill_of_materials WHERE company_id = $cid);
            """, ("$cid", cid));
        ExecTx(tx, "DELETE FROM bill_of_materials WHERE company_id = $cid;", ("$cid", cid));
        // Batch masters FK stock_items + godowns; the batch_id-bearing line tables (openings/allocations/physical/
        // item-invoice) were all deleted above → safe to drop batch masters before items + godowns.
        ExecTx(tx, "DELETE FROM batch_masters WHERE company_id = $cid;", ("$cid", cid));
        ExecTx(tx, "DELETE FROM stock_items WHERE company_id = $cid;", ("$cid", cid));
        ExecTx(tx, "DELETE FROM stock_categories WHERE company_id = $cid;", ("$cid", cid));
        ExecTx(tx, "DELETE FROM stock_groups WHERE company_id = $cid;", ("$cid", cid));
        ExecTx(tx, "DELETE FROM godowns WHERE company_id = $cid;", ("$cid", cid));
        ExecTx(tx, "DELETE FROM units WHERE company_id = $cid;", ("$cid", cid));
        ExecTx(tx, "DELETE FROM groups WHERE company_id = $cid;", ("$cid", cid));
        // v13 GST rate slabs FK companies → delete before the company row.
        ExecTx(tx, "DELETE FROM gst_rate_slabs WHERE company_id = $cid;", ("$cid", cid));
        // v38 GST rate-history + Compensation-Cess masters FK companies → delete before the company row.
        ExecTx(tx, "DELETE FROM gst_rate_history WHERE company_id = $cid;", ("$cid", cid));
        ExecTx(tx, "DELETE FROM gst_cess_rates WHERE company_id = $cid;", ("$cid", cid));
        // v39 RCM category master FK companies → delete before the company row.
        ExecTx(tx, "DELETE FROM rcm_categories WHERE company_id = $cid;", ("$cid", cid));
        // v35 PT slab bands FK companies → delete before the company row.
        ExecTx(tx, "DELETE FROM pt_slab_bands WHERE company_id = $cid;", ("$cid", cid));
        // v36 §192 tax declarations FK companies → delete before the company row.
        ExecTx(tx, "DELETE FROM employee_tax_declarations WHERE company_id = $cid;", ("$cid", cid));
        // v25 TDS/TCS masters FK companies → delete before the company row.
        ExecTx(tx, "DELETE FROM nature_of_payment WHERE company_id = $cid;", ("$cid", cid));
        ExecTx(tx, "DELETE FROM nature_of_goods WHERE company_id = $cid;", ("$cid", cid));
        // v30 Payroll masters (Phase 8 slice 1): cross-table FK order — employees (FK employee_groups +
        // employee_categories) first, then attendance_types (FK payroll_units), then payroll_units, then
        // employee_groups, then employee_categories. Each is a single-statement delete, so the self-FK
        // (employee_groups.parent_id / attendance_types.parent_id / payroll_units.first/tail) is transiently
        // violated then cleared within the same statement (net-zero at statement end), as for stock_groups.
        // v32 attendance entries FK employees + attendance_types → delete before both (Phase 8 slice 3).
        ExecTx(tx, "DELETE FROM attendance_entries WHERE company_id = $cid;", ("$cid", cid));
        ExecTx(tx, "DELETE FROM employees WHERE company_id = $cid;", ("$cid", cid));
        ExecTx(tx, "DELETE FROM attendance_types WHERE company_id = $cid;", ("$cid", cid));
        ExecTx(tx, "DELETE FROM payroll_units WHERE company_id = $cid;", ("$cid", cid));
        ExecTx(tx, "DELETE FROM employee_groups WHERE company_id = $cid;", ("$cid", cid));
        ExecTx(tx, "DELETE FROM employee_categories WHERE company_id = $cid;", ("$cid", cid));
        ExecTx(tx, "DELETE FROM companies WHERE id = $cid;", ("$cid", cid));
    }

    private void InsertCompany(SqliteTransaction tx, Company c)
    {
        using var cmd = _connection.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = """
            INSERT INTO companies
                (id, name, mailing_name, address, country, state, pin,
                 financial_year_start, books_begin_from, base_currency_symbol,
                 base_currency_name, decimal_places, decimal_unit_name,
                 primary_cost_category, main_location, profit_and_loss_head_id,
                 gst_enabled, gstin, gst_home_state, gst_reg_type, gst_applicable_from, gst_periodicity,
                 use_separate_actual_billed_qty, enable_multiple_price_levels, enable_job_order_processing,
                 tds_enabled, tcs_enabled, tan, deductor_type, responsible_person_name, responsible_person_pan,
                 responsible_person_designation, responsible_person_address, surcharge_applicable,
                 cess_applicable, tds_periodicity, tds_applicable_from, tcs_applicable_from,
                 payroll_enabled, payroll_statutory_enabled,
                 pf_config_enabled, pf_epf_rate_bp, pf_establishment_code, pf_cap_at_ceiling,
                 esi_config_enabled, esi_ee_rate_bp, esi_er_rate_bp, esi_employer_code,
                 pt_config_enabled, pt_state, pt_registration_number, pt_wage_basis,
                 salary_tds_enabled,
                 gratuity_config_enabled, gratuity_cap_paisa, gratuity_wage_basis, gratuity_population,
                 bonus_config_enabled, bonus_rate_bp, bonus_calc_ceiling_paisa, bonus_minimum_wage_paisa, bonus_prorate,
                 composition_sub_type, composition_opt_in_date,
                 einvoicing_enabled, einvoice_applicable_from, einvoice_aato_threshold_paisa,
                 einvoice_applicability_override, einvoice_exemption_classes, einvoice_reporting_age_applies,
                 gst_connector_mode, b2c_dynamic_qr_enabled, b2c_qr_aato_threshold_paisa, b2c_qr_upi_id, b2c_qr_payee_name,
                 eway_bill_enabled, eway_applicable_from, eway_threshold_paisa, eway_consignment_basis, eway_intrastate_applicable,
                 recon_value_tolerance_paisa, recon_date_window_days)
            VALUES
                ($id, $name, $mail, $addr, $country, $state, $pin,
                 $fy, $books, $sym, $curname, $dp, $unit, $pcc, $loc, NULL,
                 $gsten, $gstin, $gsthome, $gstreg, $gstfrom, $gstper,
                 $abqty, $empl, $ejop,
                 $tdsen, $tcsen, $tan, $dedtype, $rpname, $rppan, $rpdesig, $rpaddr, $surch, $cess, $tdsper,
                 $tdsfrom, $tcsfrom,
                 $payen, $paystat,
                 $pfen, $pfrate, $pfcode, $pfcap,
                 $esien, $esieerate, $esiererate, $esicode,
                 $pten, $ptstate, $ptreg, $ptbasis,
                 $salarytds,
                 $graten, $gratcap, $gratbasis, $gratpop,
                 $bonusen, $bonusrate, $bonusceil, $bonusminwage, $bonusprorate,
                 $compsub, $compdate,
                 $eien, $eifrom, $eiaato, $eioverride, $eiexempt, $eiage,
                 $connmode, $b2cqren, $b2caato, $b2cupi, $b2cpayee,
                 $ewayen, $ewayfrom, $ewaythresh, $ewaybasis, $ewayintra,
                 $reconval, $recondays);
            """;
        // NOTE (ER-16): the four nic_*_enc credential BLOB columns are DELIBERATELY OMITTED from this INSERT — the pure
        // company writer never touches a secret. They default NULL on a fresh row and are written exclusively by the
        // INicCredentialStore impl (a re-save resets them to NULL, matching the deferred live-path seam).
        cmd.Parameters.AddWithValue("$id", c.Id.ToString("D"));
        cmd.Parameters.AddWithValue("$name", c.Name);
        cmd.Parameters.AddWithValue("$mail", c.MailingName);
        cmd.Parameters.AddWithValue("$addr", (object?)c.Address ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$country", c.Country);
        cmd.Parameters.AddWithValue("$state", (object?)c.State ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$pin", (object?)c.Pin ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$fy", FormatDate(c.FinancialYearStart));
        cmd.Parameters.AddWithValue("$books", FormatDate(c.BooksBeginFrom));
        cmd.Parameters.AddWithValue("$sym", c.BaseCurrencySymbol);
        cmd.Parameters.AddWithValue("$curname", c.BaseCurrencyName);
        cmd.Parameters.AddWithValue("$dp", c.DecimalPlaces);
        cmd.Parameters.AddWithValue("$unit", c.DecimalUnitName);
        cmd.Parameters.AddWithValue("$pcc", c.PrimaryCostCategoryName);
        cmd.Parameters.AddWithValue("$loc", c.MainLocationName);

        // v13 core GST config: all NULL / 0 for a non-GST company (default), so existing companies are unchanged.
        var gst = c.Gst;
        cmd.Parameters.AddWithValue("$gsten", gst is { Enabled: true } ? 1 : 0);
        cmd.Parameters.AddWithValue("$gstin", (object?)gst?.Gstin ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$gsthome", (object?)gst?.HomeStateCode ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$gstreg", gst is null ? (object)DBNull.Value : (int)gst.RegistrationType);
        cmd.Parameters.AddWithValue("$gstfrom", gst?.ApplicableFrom is { } af ? FormatDate(af) : (object)DBNull.Value);
        cmd.Parameters.AddWithValue("$gstper", gst is null ? (object)DBNull.Value : (int)gst.Periodicity);
        // v20 (RQ-22): the F11 Actual/Billed toggle, written verbatim (default 0 for an existing company — ER-13).
        cmd.Parameters.AddWithValue("$abqty", c.UseSeparateActualBilledQuantity ? 1 : 0);
        // v21 (RQ-26): the F11 "Enable multiple Price Levels" toggle, written verbatim (default 0 — ER-13).
        cmd.Parameters.AddWithValue("$empl", c.EnableMultiplePriceLevels ? 1 : 0);
        // v24 (RQ-45): the F11 "Enable Job Order Processing" toggle, written verbatim (default 0 — ER-13).
        cmd.Parameters.AddWithValue("$ejop", c.EnableJobOrderProcessing ? 1 : 0);

        // v25 (Phase 7 slice 1): the shared TDS/TCS deductor config. All 0/NULL for a non-TDS/TCS company (ER-13).
        // The deductor identity is shared: write it from whichever config is present (they carry identical values).
        var tds = c.Tds;
        var tcs = c.Tcs;
        cmd.Parameters.AddWithValue("$tdsen", tds is { Enabled: true } ? 1 : 0);
        cmd.Parameters.AddWithValue("$tcsen", tcs is { Enabled: true } ? 1 : 0);
        cmd.Parameters.AddWithValue("$tan", (object?)(tds?.Tan ?? tcs?.Tan) ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$dedtype",
            tds is not null ? (int)tds.DeductorType : tcs is not null ? (int)tcs.CollectorType : (object)DBNull.Value);
        cmd.Parameters.AddWithValue("$rpname", (object?)(tds?.ResponsiblePersonName ?? tcs?.ResponsiblePersonName) ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$rppan", (object?)(tds?.ResponsiblePersonPan ?? tcs?.ResponsiblePersonPan) ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$rpdesig", (object?)(tds?.ResponsiblePersonDesignation ?? tcs?.ResponsiblePersonDesignation) ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$rpaddr", (object?)(tds?.ResponsiblePersonAddress ?? tcs?.ResponsiblePersonAddress) ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$surch", (tds?.SurchargeApplicable ?? tcs?.SurchargeApplicable ?? false) ? 1 : 0);
        cmd.Parameters.AddWithValue("$cess", (tds?.CessApplicable ?? tcs?.CessApplicable ?? false) ? 1 : 0);
        cmd.Parameters.AddWithValue("$tdsper",
            tds is not null ? (int)tds.Periodicity : tcs is not null ? (int)tcs.Periodicity : (object)DBNull.Value);
        cmd.Parameters.AddWithValue("$tdsfrom", tds?.ApplicableFrom is { } tf ? FormatDate(tf) : (object)DBNull.Value);
        cmd.Parameters.AddWithValue("$tcsfrom", tcs?.ApplicableFrom is { } cf ? FormatDate(cf) : (object)DBNull.Value);
        // v30 (Phase 8 slice 1): the Payroll F11 toggles, written verbatim (default 0 for an existing company — ER-13).
        cmd.Parameters.AddWithValue("$payen", c.PayrollEnabled ? 1 : 0);
        cmd.Parameters.AddWithValue("$paystat", c.PayrollStatutoryEnabled ? 1 : 0);
        // v33 (Phase 8 slice 4): the establishment PF config. NULL/defaults for a company not enrolled for PF (ER-13).
        var pf = c.PfConfig;
        cmd.Parameters.AddWithValue("$pfen", pf is not null ? 1 : 0);
        cmd.Parameters.AddWithValue("$pfrate", pf?.EpfRateBasisPoints ?? PfConfig.DefaultEpfRateBasisPoints);
        cmd.Parameters.AddWithValue("$pfcode", (object?)pf?.EstablishmentCode ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$pfcap", (pf?.CapWagesAtCeiling ?? true) ? 1 : 0);
        // v34 (Phase 8 slice 5): the establishment ESI config. NULL/defaults for a company not enrolled for ESI (ER-13).
        var esi = c.EsiConfig;
        cmd.Parameters.AddWithValue("$esien", esi is not null ? 1 : 0);
        cmd.Parameters.AddWithValue("$esieerate", esi?.EmployeeRateBasisPoints ?? EsiConfig.DefaultEmployeeRateBasisPoints);
        cmd.Parameters.AddWithValue("$esiererate", esi?.EmployerRateBasisPoints ?? EsiConfig.DefaultEmployerRateBasisPoints);
        cmd.Parameters.AddWithValue("$esicode", (object?)esi?.EmployerCode ?? DBNull.Value);
        // v35 (Phase 8 slice 6): the establishment PT config. NULL/defaults for a company not enrolled for PT (ER-13).
        var pt = c.PtConfig;
        cmd.Parameters.AddWithValue("$pten", pt is not null ? 1 : 0);
        cmd.Parameters.AddWithValue("$ptstate", (object?)pt?.StateCode ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$ptreg", (object?)pt?.RegistrationNumber ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$ptbasis", (int)(pt?.WageBasis ?? PtWageBasis.GrossEarnings));
        // v36 (Phase 8 slice 7): the establishment §192 salary-TDS toggle, written verbatim (default 0 — ER-13).
        cmd.Parameters.AddWithValue("$salarytds", c.SalaryTdsEnabled ? 1 : 0);
        // v37 (Phase 8 slice 9): the establishment Gratuity config. NULL/defaults for a company not provisioning (ER-13).
        var gratuity = c.GratuityConfig;
        cmd.Parameters.AddWithValue("$graten", gratuity is not null ? 1 : 0);
        cmd.Parameters.AddWithValue("$gratcap", (long)((gratuity?.CapAmount.Amount ?? GratuityConfig.DefaultCapAmount) * 100m));
        cmd.Parameters.AddWithValue("$gratbasis", (int)(gratuity?.WageBasis ?? GratuityWageBasis.BasicAndDearnessAllowance));
        cmd.Parameters.AddWithValue("$gratpop", (int)(gratuity?.Population ?? GratuityProvisionPopulation.AllActiveEmployees));
        // v37 (Phase 8 slice 9): the establishment statutory-Bonus config. NULL/defaults for a company not enrolled (ER-13).
        var bonus = c.BonusConfig;
        cmd.Parameters.AddWithValue("$bonusen", bonus is not null ? 1 : 0);
        cmd.Parameters.AddWithValue("$bonusrate", bonus?.RateBasisPoints ?? BonusConfig.DefaultRateBasisPoints);
        cmd.Parameters.AddWithValue("$bonusceil", (long)((bonus?.CalculationCeiling.Amount ?? BonusConfig.DefaultCalculationCeiling) * 100m));
        cmd.Parameters.AddWithValue("$bonusminwage", (long)((bonus?.MinimumWage.Amount ?? 0m) * 100m));
        cmd.Parameters.AddWithValue("$bonusprorate", (bonus?.Prorate ?? true) ? 1 : 0);
        // v40 (Phase 9 slice 3): the composition-scheme config. NULL for a non-composition company (ER-13).
        cmd.Parameters.AddWithValue("$compsub", gst?.CompositionSubType is { } st ? (int)st : (object)DBNull.Value);
        cmd.Parameters.AddWithValue("$compdate", gst?.CompositionOptInDate is { } d ? FormatDate(d) : (object)DBNull.Value);
        // v41 (Phase 9 slice 4a): the NON-SECRET e-invoice / B2C-QR / connector-mode config, written verbatim. All
        // default off/NULL/threshold for a company that does not e-invoice (ER-13). The nic_*_enc creds are NOT written
        // here (ER-16). A null Gst writes the same defaults the schema column defaults carry.
        cmd.Parameters.AddWithValue("$eien", (gst?.EInvoicingEnabled ?? false) ? 1 : 0);
        cmd.Parameters.AddWithValue("$eifrom", gst?.EInvoiceApplicableFrom is { } eiaf ? FormatDate(eiaf) : (object)DBNull.Value);
        cmd.Parameters.AddWithValue("$eiaato", Paisa.FromMoney(gst?.EInvoiceAatoThreshold ?? new Money(50_000_000m)));
        cmd.Parameters.AddWithValue("$eioverride", (gst?.EInvoiceApplicabilityOverride ?? false) ? 1 : 0);
        cmd.Parameters.AddWithValue("$eiexempt", (int)(gst?.ExemptionClasses ?? EInvoiceExemptionClass.None));
        cmd.Parameters.AddWithValue("$eiage", (gst?.ReportingAgeLimitApplies ?? false) ? 1 : 0);
        cmd.Parameters.AddWithValue("$connmode", (int)(gst?.ConnectorMode ?? GstConnectorMode.OfflineJson));
        cmd.Parameters.AddWithValue("$b2cqren", (gst?.B2cDynamicQrEnabled ?? false) ? 1 : 0);
        cmd.Parameters.AddWithValue("$b2caato", Paisa.FromMoney(gst?.B2cQrAatoThreshold ?? new Money(5_000_000_000m)));
        cmd.Parameters.AddWithValue("$b2cupi", (object?)gst?.B2cQrUpiId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$b2cpayee", (object?)gst?.B2cQrPayeeName ?? DBNull.Value);
        // v42 (Phase 9 slice 5): the NON-SECRET e-Way Bill config, written verbatim. All default off/NULL/₹50,000/0/1 for
        // a company that does not e-Way-bill (ER-13). No secret is written here — the live path reuses gst_connector_mode
        // + nic_*_enc (ER-16). A null Gst writes the same defaults the schema column defaults carry.
        cmd.Parameters.AddWithValue("$ewayen", (gst?.EWayBillEnabled ?? false) ? 1 : 0);
        cmd.Parameters.AddWithValue("$ewayfrom", gst?.EWayApplicableFrom is { } ewaf ? FormatDate(ewaf) : (object)DBNull.Value);
        cmd.Parameters.AddWithValue("$ewaythresh", Paisa.FromMoney(gst?.EWayThreshold ?? new Money(50_000m)));
        cmd.Parameters.AddWithValue("$ewaybasis", (int)(gst?.ConsignmentBasis ?? EWayConsignmentBasis.Rule138Default));
        cmd.Parameters.AddWithValue("$ewayintra", (gst?.EWayIntraStateApplicable ?? true) ? 1 : 0);
        // v43 (Phase 9 slice 6): the GSTR-2B reconciliation tolerance, written verbatim. Default 0/0 (exact-match) for a
        // company that never sets it (ER-13). A matching parameter only — never a posted figure (ER-14; finding #5).
        cmd.Parameters.AddWithValue("$reconval", Paisa.FromMoney(gst?.ReconValueTolerance ?? Money.Zero));
        cmd.Parameters.AddWithValue("$recondays", gst?.ReconDateWindowDays ?? 0);
        cmd.ExecuteNonQuery();

        // v42 (Phase 9 slice 5): per-state e-Way threshold overrides — FK companies (just inserted). Empty for a company
        // on the flat ₹50,000 default (ER-13).
        if (gst is not null)
            foreach (var t in gst.EWayStateThresholds)
            {
                using var s = _connection.CreateCommand();
                s.Transaction = tx;
                s.CommandText = """
                    INSERT INTO eway_state_thresholds (id, company_id, state_code, txn_type, threshold_paisa)
                    VALUES ($id, $cid, $state, $txn, $thresh);
                    """;
                s.Parameters.AddWithValue("$id", t.Id.ToString("D"));
                s.Parameters.AddWithValue("$cid", c.Id.ToString("D"));
                s.Parameters.AddWithValue("$state", t.StateCode);
                s.Parameters.AddWithValue("$txn", (int)t.TxnType);
                s.Parameters.AddWithValue("$thresh", Paisa.FromMoney(t.Threshold));
                s.ExecuteNonQuery();
            }

        // v36 per-employee §192 income-tax declarations (only for employees that declared figures). All money in
        // integer paisa. One row per declaration; a company with none writes nothing (ER-13).
        foreach (var declaration in c.TaxDeclarations)
        {
            using var s = _connection.CreateCommand();
            s.Transaction = tx;
            s.CommandText = """
                INSERT INTO employee_tax_declarations
                    (employee_id, company_id, section_80c_paisa, section_80d_paisa, section_80ccd1b_paisa,
                     section_80ccd2_employer_paisa, hra_exempt_paisa, home_loan_interest_paisa, other_income_paisa,
                     prev_employer_salary_paisa, prev_employer_tds_paisa)
                VALUES ($eid, $cid, $c80, $d80, $ccd1b, $ccd2, $hra, $loan, $other, $prevsal, $prevtds);
                """;
            s.Parameters.AddWithValue("$eid", declaration.EmployeeId.ToString("D"));
            s.Parameters.AddWithValue("$cid", c.Id.ToString("D"));
            s.Parameters.AddWithValue("$c80", (long)(declaration.Section80C.Amount * 100m));
            s.Parameters.AddWithValue("$d80", (long)(declaration.Section80D.Amount * 100m));
            s.Parameters.AddWithValue("$ccd1b", (long)(declaration.Section80CCD1B.Amount * 100m));
            s.Parameters.AddWithValue("$ccd2", (long)(declaration.Section80CCD2Employer.Amount * 100m));
            s.Parameters.AddWithValue("$hra", (long)(declaration.HouseRentAllowanceExempt.Amount * 100m));
            s.Parameters.AddWithValue("$loan", (long)(declaration.HomeLoanInterest24b.Amount * 100m));
            s.Parameters.AddWithValue("$other", (long)(declaration.OtherIncome.Amount * 100m));
            s.Parameters.AddWithValue("$prevsal", (long)(declaration.PreviousEmployerSalary.Amount * 100m));
            s.Parameters.AddWithValue("$prevtds", (long)(declaration.PreviousEmployerTds.Amount * 100m));
            s.ExecuteNonQuery();
        }

        // v35 PT slab bands (only when PT is enrolled), hung off the PT config like the GST slabs. Each band is one
        // row keyed to its PtSlab (slab_id) with its state + gender scope, ordered low-to-high.
        if (pt is not null)
            foreach (var slab in pt.SlabTables)
            {
                var bandOrder = 0;
                foreach (var band in slab.Bands)
                {
                    using var s = _connection.CreateCommand();
                    s.Transaction = tx;
                    s.CommandText = """
                        INSERT INTO pt_slab_bands
                            (id, company_id, slab_id, state_code, gender_scope, band_order,
                             from_wage_paisa, to_wage_paisa, monthly_amount_paisa, month_overrides)
                        VALUES ($id, $cid, $slab, $state, $scope, $ord, $from, $to, $amt, $ovr);
                        """;
                    s.Parameters.AddWithValue("$id", Guid.NewGuid().ToString("D"));
                    s.Parameters.AddWithValue("$cid", c.Id.ToString("D"));
                    s.Parameters.AddWithValue("$slab", slab.Id.ToString("D"));
                    s.Parameters.AddWithValue("$state", slab.StateCode);
                    s.Parameters.AddWithValue("$scope", (int)slab.GenderScope);
                    s.Parameters.AddWithValue("$ord", bandOrder++);
                    s.Parameters.AddWithValue("$from", (long)(band.FromWage.Amount * 100m));
                    s.Parameters.AddWithValue("$to", band.ToWage is { } tw ? (long)(tw.Amount * 100m) : (object)DBNull.Value);
                    s.Parameters.AddWithValue("$amt", (long)(band.MonthlyAmount.Amount * 100m));
                    s.Parameters.AddWithValue("$ovr", FormatPtMonthOverrides(band.MonthOverrides));
                    s.ExecuteNonQuery();
                }
            }

        // v13 GST rate slabs (the seeded config-driven slabs), if any.
        if (gst is not null)
            foreach (var slab in gst.RateSlabs)
            {
                using var s = _connection.CreateCommand();
                s.Transaction = tx;
                s.CommandText = """
                    INSERT INTO gst_rate_slabs (id, company_id, rate_bp, label, is_predefined)
                    VALUES ($id, $cid, $bp, $label, $pre);
                    """;
                s.Parameters.AddWithValue("$id", slab.Id.ToString("D"));
                s.Parameters.AddWithValue("$cid", c.Id.ToString("D"));
                s.Parameters.AddWithValue("$bp", slab.RateBasisPoints);
                s.Parameters.AddWithValue("$label", slab.Label);
                s.Parameters.AddWithValue("$pre", slab.IsPredefined ? 1 : 0);
                s.ExecuteNonQuery();
            }

        // v38 GST rate-history windows (empty when advanced GST is off).
        if (gst is not null)
            foreach (var e in gst.RateHistory)
            {
                using var s = _connection.CreateCommand();
                s.Transaction = tx;
                s.CommandText = """
                    INSERT INTO gst_rate_history
                        (id, company_id, hsn_sac, rate_bp, rate_class, effective_from, effective_to,
                         valuation_basis, label, is_predefined)
                    VALUES ($id, $cid, $hsn, $bp, $cls, $from, $to, $basis, $label, $pre);
                    """;
                s.Parameters.AddWithValue("$id", e.Id.ToString("D"));
                s.Parameters.AddWithValue("$cid", c.Id.ToString("D"));
                s.Parameters.AddWithValue("$hsn", (object?)e.HsnSac ?? DBNull.Value);
                s.Parameters.AddWithValue("$bp", e.RateBasisPoints);
                s.Parameters.AddWithValue("$cls", (int)e.RateClass);
                s.Parameters.AddWithValue("$from", FormatDate(e.EffectiveFrom));
                s.Parameters.AddWithValue("$to", e.EffectiveTo is { } to ? FormatDate(to) : (object)DBNull.Value);
                s.Parameters.AddWithValue("$basis", (int)e.ValuationBasis);
                s.Parameters.AddWithValue("$label", e.Label);
                s.Parameters.AddWithValue("$pre", e.IsPredefined ? 1 : 0);
                s.ExecuteNonQuery();
            }

        // v38 Compensation-Cess windows (empty when the company bears no cess).
        if (gst is not null)
            foreach (var e in gst.CessRates)
            {
                using var s = _connection.CreateCommand();
                s.Transaction = tx;
                s.CommandText = """
                    INSERT INTO gst_cess_rates
                        (id, company_id, hsn_sac, valuation_mode, cess_rate_bp, cess_per_unit_paisa,
                         cess_rsp_factor_millis, effective_from, effective_to, label, is_predefined)
                    VALUES ($id, $cid, $hsn, $mode, $bp, $perunit, $rsp, $from, $to, $label, $pre);
                    """;
                s.Parameters.AddWithValue("$id", e.Id.ToString("D"));
                s.Parameters.AddWithValue("$cid", c.Id.ToString("D"));
                s.Parameters.AddWithValue("$hsn", (object?)e.HsnSac ?? DBNull.Value);
                s.Parameters.AddWithValue("$mode", (int)e.ValuationMode);
                s.Parameters.AddWithValue("$bp", e.CessRateBasisPoints);
                s.Parameters.AddWithValue("$perunit", Paisa.FromMoney(e.CessPerUnit));
                s.Parameters.AddWithValue("$rsp", e.CessRspFactorMillis);
                s.Parameters.AddWithValue("$from", FormatDate(e.EffectiveFrom));
                s.Parameters.AddWithValue("$to", e.EffectiveTo is { } to ? FormatDate(to) : (object)DBNull.Value);
                s.Parameters.AddWithValue("$label", e.Label);
                s.Parameters.AddWithValue("$pre", e.IsPredefined ? 1 : 0);
                s.ExecuteNonQuery();
            }

        // v39 reverse-charge categories (empty when RCM is off), hung off the GST config like the rate-history/cess.
        if (gst is not null)
            foreach (var c2 in gst.RcmCategories)
            {
                using var s = _connection.CreateCommand();
                s.Transaction = tx;
                s.CommandText = """
                    INSERT INTO rcm_categories
                        (id, company_id, notification, stream, supply_nature, supply_type, hsn_sac, rate_bp,
                         supplier_qualifier, recipient_qualifier, effective_from, effective_to, label, is_predefined)
                    VALUES ($id, $cid, $notn, $stream, $nature, $stype, $hsn, $bp, $supq, $recq, $from, $to, $label, $pre);
                    """;
                s.Parameters.AddWithValue("$id", c2.Id.ToString("D"));
                s.Parameters.AddWithValue("$cid", c.Id.ToString("D"));
                s.Parameters.AddWithValue("$notn", c2.Notification);
                s.Parameters.AddWithValue("$stream", (int)c2.Stream);
                s.Parameters.AddWithValue("$nature", c2.SupplyNature);
                s.Parameters.AddWithValue("$stype", (int)c2.SupplyType);
                s.Parameters.AddWithValue("$hsn", (object?)c2.HsnSac ?? DBNull.Value);
                s.Parameters.AddWithValue("$bp", c2.RateBasisPoints);
                s.Parameters.AddWithValue("$supq", (int)c2.SupplierQualifier);
                s.Parameters.AddWithValue("$recq", (int)c2.RecipientQualifier);
                s.Parameters.AddWithValue("$from", FormatDate(c2.EffectiveFrom));
                s.Parameters.AddWithValue("$to", c2.EffectiveTo is { } to ? FormatDate(to) : (object)DBNull.Value);
                s.Parameters.AddWithValue("$label", c2.Label);
                s.Parameters.AddWithValue("$pre", c2.IsPredefined ? 1 : 0);
                s.ExecuteNonQuery();
            }

        // v25 Nature-of-Payment masters (only when TDS is enabled), hung off the TDS config like the GST slabs.
        if (tds is not null)
            foreach (var n in tds.NaturesOfPayment)
            {
                using var s = _connection.CreateCommand();
                s.Transaction = tx;
                s.CommandText = """
                    INSERT INTO nature_of_payment
                        (id, company_id, section_code, name, rate_with_pan_bp, rate_without_pan_bp,
                         single_threshold_micro, cumulative_threshold_micro, fvu_code, effective_from, is_predefined)
                    VALUES ($id, $cid, $sec, $name, $wp, $np, $single, $cum, $fvu, $eff, $pre);
                    """;
                s.Parameters.AddWithValue("$id", n.Id.ToString("D"));
                s.Parameters.AddWithValue("$cid", c.Id.ToString("D"));
                s.Parameters.AddWithValue("$sec", n.SectionCode);
                s.Parameters.AddWithValue("$name", n.Name);
                s.Parameters.AddWithValue("$wp", n.RateWithPanBp);
                s.Parameters.AddWithValue("$np", n.RateWithoutPanBp);
                s.Parameters.AddWithValue("$single", MoneyToMicro(n.SingleTransactionThreshold));
                s.Parameters.AddWithValue("$cum", MoneyToMicro(n.CumulativeThreshold));
                s.Parameters.AddWithValue("$fvu", n.FvuSectionCode);
                s.Parameters.AddWithValue("$eff", n.EffectiveFrom is { } ef ? FormatDate(ef) : (object)DBNull.Value);
                s.Parameters.AddWithValue("$pre", n.IsPredefined ? 1 : 0);
                s.ExecuteNonQuery();
            }

        // v25 Nature-of-Goods masters (only when TCS is enabled).
        if (tcs is not null)
            foreach (var n in tcs.NaturesOfGoods)
            {
                using var s = _connection.CreateCommand();
                s.Transaction = tx;
                s.CommandText = """
                    INSERT INTO nature_of_goods
                        (id, company_id, collection_code, name, rate_with_pan_bp, rate_without_pan_bp,
                         threshold_micro, base_includes_gst, fvu_code, effective_from, is_predefined,
                         is_legacy, legacy_cutoff)
                    VALUES ($id, $cid, $code, $name, $wp, $np, $th, $bg, $fvu, $eff, $pre, $leg, $cut);
                    """;
                s.Parameters.AddWithValue("$id", n.Id.ToString("D"));
                s.Parameters.AddWithValue("$cid", c.Id.ToString("D"));
                s.Parameters.AddWithValue("$code", n.CollectionCode);
                s.Parameters.AddWithValue("$name", n.Name);
                s.Parameters.AddWithValue("$wp", n.RateWithPanBp);
                s.Parameters.AddWithValue("$np", n.RateWithoutPanBp);
                s.Parameters.AddWithValue("$th", MoneyToMicro(n.Threshold));
                s.Parameters.AddWithValue("$bg", n.BaseIncludesGst ? 1 : 0);
                s.Parameters.AddWithValue("$fvu", n.FvuCode);
                s.Parameters.AddWithValue("$eff", n.EffectiveFrom is { } ef ? FormatDate(ef) : (object)DBNull.Value);
                s.Parameters.AddWithValue("$pre", n.IsPredefined ? 1 : 0);
                s.Parameters.AddWithValue("$leg", n.IsLegacy ? 1 : 0);
                s.Parameters.AddWithValue("$cut", n.LegacyCutoff is { } lc ? FormatDate(lc) : (object)DBNull.Value);
                s.ExecuteNonQuery();
            }
    }

    private void InsertGroups(SqliteTransaction tx, Company c)
    {
        foreach (var g in c.Groups)
            InsertGroup(tx, c.Id, g, isPlHead: false);

        if (c.ProfitAndLossHead is not null)
            InsertGroup(tx, c.Id, c.ProfitAndLossHead, isPlHead: true);
    }

    private void InsertGroup(SqliteTransaction tx, Guid companyId, Group g, bool isPlHead)
    {
        using var cmd = _connection.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = """
            INSERT INTO groups (id, company_id, name, nature, parent_id, alias, is_predefined, is_pl_head)
            VALUES ($id, $cid, $name, $nature, $parent, $alias, $pre, $plhead);
            """;
        cmd.Parameters.AddWithValue("$id", g.Id.ToString("D"));
        cmd.Parameters.AddWithValue("$cid", companyId.ToString("D"));
        cmd.Parameters.AddWithValue("$name", g.Name);
        cmd.Parameters.AddWithValue("$nature", (int)g.Nature);
        cmd.Parameters.AddWithValue("$parent", (object?)g.ParentId?.ToString("D") ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$alias", (object?)g.Alias ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$pre", g.IsPredefined ? 1 : 0);
        cmd.Parameters.AddWithValue("$plhead", isPlHead ? 1 : 0);
        cmd.ExecuteNonQuery();
    }

    private void SetProfitAndLossHead(SqliteTransaction tx, Company c)
    {
        if (c.ProfitAndLossHead is null) return;
        ExecTx(tx,
            "UPDATE companies SET profit_and_loss_head_id = $head WHERE id = $cid;",
            ("$head", c.ProfitAndLossHead.Id.ToString("D")),
            ("$cid", c.Id.ToString("D")));
    }

    private void InsertLedgers(SqliteTransaction tx, Company c)
    {
        foreach (var l in c.Ledgers)
        {
            using var cmd = _connection.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = """
                INSERT INTO ledgers
                    (id, company_id, name, group_id, opening_balance_paisa, opening_is_debit, alias, is_predefined,
                     maintain_bill_by_bill, default_credit_period, cost_applicable,
                     enable_cheque_printing, cheque_bank_name,
                     interest_enabled, interest_rate_millis, interest_per, interest_on_balance,
                     interest_applicability, interest_calc_from, interest_style,
                     interest_round_method, interest_round_decimals, currency_id,
                     party_gst_reg_type, party_gstin, party_gst_state,
                     sp_gst_hsn, sp_gst_taxability, sp_gst_rate_bp, sp_gst_supply_type,
                     gst_tax_head, gst_tax_direction, method_of_appropriation, default_price_level_id,
                     tds_applicable, tds_nature_id, deductee_type, party_pan, deduct_in_same_voucher,
                     tcs_applicable, tcs_nature_id, collectee_type, tds_tcs_class_kind,
                     sp_gst_valuation_basis, sp_cess_applicable, sp_cess_valuation_mode, sp_cess_rate_bp,
                     sp_cess_per_unit_paisa, sp_cess_rsp_factor_millis, sp_rsp_paisa,
                     sp_reverse_charge_applicable, sp_gta_forward_charge, sp_rcm_category_id,
                     party_is_promoter, party_is_body_corporate, gst_class_reverse_charge,
                     itc_eligibility, blocked_credit_category)
                VALUES ($id, $cid, $name, $gid, $ob, $od, $alias, $pre, $bbb, $dcp, $cca, $ecp, $cbn,
                        $ien, $irate, $iper, $ion, $iapp, $icf, $istyle, $irm, $ird, $curid,
                        $pgreg, $pgstin, $pgstate, $sphsn, $sptax, $sprate, $spsup, $gthead, $gtdir, $moa, $dpl,
                        $tdsap, $tdsnat, $dedtype, $ppan, $dsv, $tcsap, $tcsnat, $coltype, $classkind,
                        $spvb, $spca, $spcvm, $spcrate, $spcpu, $spcrsp, $sprsp,
                        $sprca, $spgtafc, $sprcmcat, $ppromo, $pbodycorp, $gcrc,
                        $spitcelig, $spblkcat);
                """;
            cmd.Parameters.AddWithValue("$id", l.Id.ToString("D"));
            cmd.Parameters.AddWithValue("$cid", c.Id.ToString("D"));
            cmd.Parameters.AddWithValue("$name", l.Name);
            cmd.Parameters.AddWithValue("$gid", l.GroupId.ToString("D"));
            cmd.Parameters.AddWithValue("$ob", Paisa.FromMoney(l.OpeningBalance));
            cmd.Parameters.AddWithValue("$od", l.OpeningIsDebit ? 1 : 0);
            cmd.Parameters.AddWithValue("$alias", (object?)l.Alias ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$pre", l.IsPredefined ? 1 : 0);
            cmd.Parameters.AddWithValue("$bbb", l.MaintainBillByBill ? 1 : 0);
            cmd.Parameters.AddWithValue("$dcp", (object?)l.DefaultCreditPeriodDays ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$cca", l.CostCentresApplicable is { } cca ? (cca ? 1 : 0) : (object)DBNull.Value);
            cmd.Parameters.AddWithValue("$ecp", l.EnableChequePrinting ? 1 : 0);
            cmd.Parameters.AddWithValue("$cbn", (object?)l.ChequePrintingBankName ?? DBNull.Value);

            // v7 interest block: all NULL when the ledger has no block, so existing ledgers stay off.
            var ip = l.Interest;
            cmd.Parameters.AddWithValue("$ien", ip is null ? (object)DBNull.Value : (ip.Enabled ? 1 : 0));
            cmd.Parameters.AddWithValue("$irate", ip is null ? (object)DBNull.Value : RateToMillis(ip.RatePercent));
            cmd.Parameters.AddWithValue("$iper", ip is null ? (object)DBNull.Value : (int)ip.Per);
            cmd.Parameters.AddWithValue("$ion", ip is null ? (object)DBNull.Value : (int)ip.OnBalance);
            cmd.Parameters.AddWithValue("$iapp", ip is null ? (object)DBNull.Value : (int)ip.Applicability);
            cmd.Parameters.AddWithValue("$icf",
                ip?.CalculateFrom is { } cf ? FormatDate(cf) : (object)DBNull.Value);
            cmd.Parameters.AddWithValue("$istyle", ip is null ? (object)DBNull.Value : (int)ip.Style);
            cmd.Parameters.AddWithValue("$irm", ip is null ? (object)DBNull.Value : (int)ip.RoundingMethod);
            cmd.Parameters.AddWithValue("$ird", ip is null ? (object)DBNull.Value : ip.RoundingDecimals);

            // v8 multi-currency: the ledger's currency (NULL = base).
            cmd.Parameters.AddWithValue("$curid", (object?)l.CurrencyId?.ToString("D") ?? DBNull.Value);

            // v13 party GST details (NULL when the ledger has no party GST block).
            var pg = l.PartyGst;
            cmd.Parameters.AddWithValue("$pgreg", pg is null ? (object)DBNull.Value : (int)pg.RegistrationType);
            cmd.Parameters.AddWithValue("$pgstin", (object?)pg?.Gstin ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$pgstate", (object?)pg?.StateCode ?? DBNull.Value);

            // v13 sales/purchase-ledger GST block (NULL taxability = no block).
            var sp = l.SalesPurchaseGst;
            cmd.Parameters.AddWithValue("$sphsn", (object?)sp?.HsnSac ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$sptax", sp is null ? (object)DBNull.Value : (int)sp.Taxability);
            cmd.Parameters.AddWithValue("$sprate", sp?.RateBasisPoints is { } sprbp ? sprbp : (object)DBNull.Value);
            cmd.Parameters.AddWithValue("$spsup", sp is null ? (object)DBNull.Value : (int)sp.SupplyType);

            // v38 sales/purchase-ledger GST 2.0 RSP valuation + Compensation-Cess (default off/null for a plain ledger).
            cmd.Parameters.AddWithValue("$spvb", sp is null ? 0 : (int)sp.ValuationBasis);
            cmd.Parameters.AddWithValue("$spca", sp is { CessApplicable: true } ? 1 : 0);
            cmd.Parameters.AddWithValue("$spcvm", sp?.CessValuationMode is { } spcvm ? (int)spcvm : (object)DBNull.Value);
            cmd.Parameters.AddWithValue("$spcrate", sp?.CessRateBasisPoints is { } spcr ? spcr : (object)DBNull.Value);
            cmd.Parameters.AddWithValue("$spcpu", sp?.CessPerUnit is { } spcpu ? Paisa.FromMoney(spcpu) : (object)DBNull.Value);
            cmd.Parameters.AddWithValue("$spcrsp", sp?.CessRspFactorMillis is { } spcrsp ? spcrsp : (object)DBNull.Value);
            cmd.Parameters.AddWithValue("$sprsp", sp?.RetailSalePrice is { } sprsp ? Paisa.FromMoney(sprsp) : (object)DBNull.Value);

            // v39 sales/purchase-ledger reverse-charge (RCM) flags + party RCM qualifiers (default off/null).
            cmd.Parameters.AddWithValue("$sprca", sp is { ReverseChargeApplicable: true } ? 1 : 0);
            cmd.Parameters.AddWithValue("$spgtafc", sp is { GtaForwardCharge: true } ? 1 : 0);
            cmd.Parameters.AddWithValue("$sprcmcat", (object?)sp?.RcmCategoryId?.ToString("D") ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$ppromo", pg is { IsPromoter: true } ? 1 : 0);
            cmd.Parameters.AddWithValue("$pbodycorp", pg is { IsBodyCorporate: true } ? 1 : 0);

            // v43 §17(5) ITC-eligibility on the sales/purchase-ledger block (default Eligible/None for a plain ledger).
            cmd.Parameters.AddWithValue("$spitcelig", sp is null ? 0 : (int)sp.ItcEligibility);
            cmd.Parameters.AddWithValue("$spblkcat", sp is null ? 0 : (int)sp.BlockedCreditCategory);

            // v13 tax-ledger classification (NULL for an ordinary ledger); v39 adds the RCM Output discriminator.
            var gc = l.GstClassification;
            cmd.Parameters.AddWithValue("$gthead", gc is null ? (object)DBNull.Value : (int)gc.TaxHead);
            cmd.Parameters.AddWithValue("$gtdir", gc is null ? (object)DBNull.Value : (int)gc.Direction);
            cmd.Parameters.AddWithValue("$gcrc", gc is { IsReverseCharge: true } ? 1 : 0);

            // v19 additional-cost ledger method (NULL for a plain P&L ledger).
            cmd.Parameters.AddWithValue("$moa", l.MethodOfAppropriation is { } moa ? (int)moa : (object)DBNull.Value);
            // v21 party default Price Level (NULL when the ledger carries no default level).
            cmd.Parameters.AddWithValue("$dpl", (object?)l.DefaultPriceLevelId?.ToString("D") ?? DBNull.Value);

            // v25 (Phase 7 slice 1): TDS/TCS applicability flags + party PAN + the payable classification tag.
            // All default off/NULL for an ordinary ledger, so an existing ledger is byte-identical (ER-13).
            cmd.Parameters.AddWithValue("$tdsap", l.TdsApplicable ? 1 : 0);
            cmd.Parameters.AddWithValue("$tdsnat", (object?)l.TdsNatureOfPaymentId?.ToString("D") ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$dedtype", l.DeducteeType is { } dt ? (int)dt : (object)DBNull.Value);
            cmd.Parameters.AddWithValue("$ppan", (object?)l.PartyPan ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$dsv", l.DeductTdsInSameVoucher ? 1 : 0);
            cmd.Parameters.AddWithValue("$tcsap", l.TcsApplicable ? 1 : 0);
            cmd.Parameters.AddWithValue("$tcsnat", (object?)l.TcsNatureOfGoodsId?.ToString("D") ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$coltype", l.CollecteeType is { } ct ? (int)ct : (object)DBNull.Value);
            cmd.Parameters.AddWithValue("$classkind", l.TdsTcsClassification is { } k ? (int)k : (object)DBNull.Value);
            cmd.ExecuteNonQuery();
        }
    }

    private void InsertCurrencies(SqliteTransaction tx, Company c)
    {
        foreach (var cur in c.Currencies)
        {
            using var cmd = _connection.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = """
                INSERT INTO currencies (id, company_id, symbol, formal_name, decimal_places, is_base)
                VALUES ($id, $cid, $sym, $fn, $dp, $base);
                """;
            cmd.Parameters.AddWithValue("$id", cur.Id.ToString("D"));
            cmd.Parameters.AddWithValue("$cid", c.Id.ToString("D"));
            cmd.Parameters.AddWithValue("$sym", cur.Symbol);
            cmd.Parameters.AddWithValue("$fn", cur.FormalName);
            cmd.Parameters.AddWithValue("$dp", cur.DecimalPlaces);
            cmd.Parameters.AddWithValue("$base", cur.IsBaseCurrency ? 1 : 0);
            cmd.ExecuteNonQuery();
        }
    }

    private void InsertExchangeRates(SqliteTransaction tx, Company c)
    {
        foreach (var rate in c.ExchangeRates)
        {
            using var cmd = _connection.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = """
                INSERT INTO exchange_rates
                    (id, company_id, currency_id, rate_date, standard_rate_micro, selling_rate_micro, buying_rate_micro)
                VALUES ($id, $cid, $cur, $date, $std, $sell, $buy);
                """;
            cmd.Parameters.AddWithValue("$id", rate.Id.ToString("D"));
            cmd.Parameters.AddWithValue("$cid", c.Id.ToString("D"));
            cmd.Parameters.AddWithValue("$cur", rate.CurrencyId.ToString("D"));
            cmd.Parameters.AddWithValue("$date", FormatDate(rate.Date));
            cmd.Parameters.AddWithValue("$std", MicroFromDecimal(rate.StandardRate));
            cmd.Parameters.AddWithValue("$sell", rate.SellingRate is { } s ? MicroFromDecimal(s) : (object)DBNull.Value);
            cmd.Parameters.AddWithValue("$buy", rate.BuyingRate is { } b ? MicroFromDecimal(b) : (object)DBNull.Value);
            cmd.ExecuteNonQuery();
        }
    }

    private void InsertVoucherTypes(SqliteTransaction tx, Company c)
    {
        foreach (var t in c.VoucherTypes)
        {
            using var cmd = _connection.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = """
                INSERT INTO voucher_types
                    (id, company_id, name, base_type, default_shortcut, numbering, abbreviation, is_active, is_predefined,
                     affects_accounts, affects_stock, use_as_manufacturing_journal, track_additional_costs, allow_zero_valued,
                     use_for_pos, use_for_job_work, allow_consumption, is_stat_payment, is_rcm_payment_voucher,
                     is_gst_stat_adjustment)
                VALUES ($id, $cid, $name, $base, $sc, $num, $abbr, $active, $pre, $aa, $as, $mfg, $tac, $azv, $pos, $ujw, $ac, $stat, $rcmpv, $gstadj);
                """;
            cmd.Parameters.AddWithValue("$id", t.Id.ToString("D"));
            cmd.Parameters.AddWithValue("$cid", c.Id.ToString("D"));
            cmd.Parameters.AddWithValue("$name", t.Name);
            cmd.Parameters.AddWithValue("$base", (int)t.BaseType);
            cmd.Parameters.AddWithValue("$sc", (object?)t.DefaultShortcut ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$num", (int)t.Numbering);
            cmd.Parameters.AddWithValue("$abbr", (object?)t.Abbreviation ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$active", t.IsActive ? 1 : 0);
            cmd.Parameters.AddWithValue("$pre", t.IsPredefined ? 1 : 0);
            cmd.Parameters.AddWithValue("$aa", t.AffectsAccounts ? 1 : 0);
            cmd.Parameters.AddWithValue("$as", t.AffectsStock ? 1 : 0);
            cmd.Parameters.AddWithValue("$mfg", t.UseAsManufacturingJournal ? 1 : 0); // v18 (RQ-11)
            cmd.Parameters.AddWithValue("$tac", t.TrackAdditionalCosts ? 1 : 0);      // v19 (RQ-16..RQ-20)
            cmd.Parameters.AddWithValue("$azv", t.AllowZeroValuedTransactions ? 1 : 0); // v20 (RQ-21)
            cmd.Parameters.AddWithValue("$pos", t.UseForPos ? 1 : 0);                  // v23 (RQ-38)
            cmd.Parameters.AddWithValue("$ujw", t.UseForJobWork ? 1 : 0);              // v24 (RQ-45/RQ-48)
            cmd.Parameters.AddWithValue("$ac", t.AllowConsumption ? 1 : 0);            // v24 (RQ-49)
            cmd.Parameters.AddWithValue("$stat", t.IsStatPayment ? 1 : 0);             // v27 (Phase 7 slice 3)
            cmd.Parameters.AddWithValue("$rcmpv", t.IsRcmPaymentVoucher ? 1 : 0);      // v39 (Phase 9 slice 2)
            cmd.Parameters.AddWithValue("$gstadj", t.IsGstStatAdjustment ? 1 : 0);     // v44 (Phase 9 slice 7)
            cmd.ExecuteNonQuery();
        }
    }

    /// <summary>
    /// Persists the TDS deposit challans (v27; Phase 7 slice 3) and their stat-payment-voucher links. A company
    /// that never deposits TDS writes nothing — byte-identical (ER-13). amount_micro is rupees × 1,000,000 (exact
    /// for a paisa-exact amount).
    /// </summary>
    private void InsertTdsChallans(SqliteTransaction tx, Company c)
    {
        foreach (var ch in c.TdsChallans)
        {
            using var cmd = _connection.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = """
                INSERT INTO tds_challans
                    (id, company_id, challan_no, bsr_code, deposit_date, amount_micro, section, minor_head)
                VALUES ($id, $cid, $no, $bsr, $date, $amt, $sec, $minor);
                """;
            cmd.Parameters.AddWithValue("$id", ch.Id.ToString("D"));
            cmd.Parameters.AddWithValue("$cid", c.Id.ToString("D"));
            cmd.Parameters.AddWithValue("$no", ch.ChallanNo);
            cmd.Parameters.AddWithValue("$bsr", ch.BsrCode);
            cmd.Parameters.AddWithValue("$date", FormatDate(ch.DepositDate));
            cmd.Parameters.AddWithValue("$amt", (long)(ch.Amount.Amount * 1_000_000m));
            cmd.Parameters.AddWithValue("$sec", ch.Section);
            cmd.Parameters.AddWithValue("$minor", ch.MinorHead);
            cmd.ExecuteNonQuery();
        }

        foreach (var link in c.ChallanVoucherLinks)
        {
            using var cmd = _connection.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = """
                INSERT INTO challan_voucher_links (challan_id, voucher_id) VALUES ($ch, $v);
                """;
            cmd.Parameters.AddWithValue("$ch", link.ChallanId.ToString("D"));
            cmd.Parameters.AddWithValue("$v", link.VoucherId.ToString("D"));
            cmd.ExecuteNonQuery();
        }
    }

    private void InsertTcsChallans(SqliteTransaction tx, Company c)
    {
        foreach (var ch in c.TcsChallans)
        {
            using var cmd = _connection.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = """
                INSERT INTO tcs_challans
                    (id, company_id, challan_no, bsr_code, deposit_date, amount_micro, collection_code, minor_head)
                VALUES ($id, $cid, $no, $bsr, $date, $amt, $code, $minor);
                """;
            cmd.Parameters.AddWithValue("$id", ch.Id.ToString("D"));
            cmd.Parameters.AddWithValue("$cid", c.Id.ToString("D"));
            cmd.Parameters.AddWithValue("$no", ch.ChallanNo);
            cmd.Parameters.AddWithValue("$bsr", ch.BsrCode);
            cmd.Parameters.AddWithValue("$date", FormatDate(ch.DepositDate));
            cmd.Parameters.AddWithValue("$amt", (long)(ch.Amount.Amount * 1_000_000m));
            cmd.Parameters.AddWithValue("$code", ch.CollectionCode);
            cmd.Parameters.AddWithValue("$minor", ch.MinorHead);
            cmd.ExecuteNonQuery();
        }

        foreach (var link in c.TcsChallanVoucherLinks)
        {
            using var cmd = _connection.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = """
                INSERT INTO tcs_challan_voucher_links (challan_id, voucher_id) VALUES ($ch, $v);
                """;
            cmd.Parameters.AddWithValue("$ch", link.ChallanId.ToString("D"));
            cmd.Parameters.AddWithValue("$v", link.VoucherId.ToString("D"));
            cmd.ExecuteNonQuery();
        }
    }

    /// <summary>Persists the RCM generated documents (v39; Phase 9 slice 2) + the §34-CDN links + the GST-on-advance
    /// receipts. All FK vouchers, so this runs after InsertVouchers. Empty sets write nothing — byte-identical (ER-13).</summary>
    private void InsertRcmRecords(SqliteTransaction tx, Company c)
    {
        foreach (var d in c.RcmDocuments)
        {
            using var cmd = _connection.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = """
                INSERT INTO rcm_documents
                    (id, company_id, doc_kind, source_voucher_id, series_number, doc_date, supplier_ledger_id)
                VALUES ($id, $cid, $kind, $src, $seq, $date, $sup);
                """;
            cmd.Parameters.AddWithValue("$id", d.Id.ToString("D"));
            cmd.Parameters.AddWithValue("$cid", c.Id.ToString("D"));
            cmd.Parameters.AddWithValue("$kind", (int)d.Kind);
            cmd.Parameters.AddWithValue("$src", d.SourceVoucherId.ToString("D"));
            cmd.Parameters.AddWithValue("$seq", d.SeriesNumber);
            cmd.Parameters.AddWithValue("$date", FormatDate(d.DocDate));
            cmd.Parameters.AddWithValue("$sup", (object?)d.SupplierLedgerId?.ToString("D") ?? DBNull.Value);
            cmd.ExecuteNonQuery();
        }

        foreach (var l in c.CreditDebitNoteLinks)
        {
            using var cmd = _connection.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = """
                INSERT INTO gst_cdn_links
                    (id, company_id, cdn_voucher_id, cdn_type, original_invoice_voucher_id, original_invoice_number,
                     original_invoice_date, reason_code, is_9b_target)
                VALUES ($id, $cid, $cdnv, $type, $origv, $orignum, $origdate, $reason, $b9);
                """;
            cmd.Parameters.AddWithValue("$id", l.Id.ToString("D"));
            cmd.Parameters.AddWithValue("$cid", c.Id.ToString("D"));
            cmd.Parameters.AddWithValue("$cdnv", l.CdnVoucherId.ToString("D"));
            cmd.Parameters.AddWithValue("$type", (int)l.CdnType);
            cmd.Parameters.AddWithValue("$origv", (object?)l.OriginalInvoiceVoucherId?.ToString("D") ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$orignum", (object?)l.OriginalInvoiceNumber ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$origdate", l.OriginalInvoiceDate is { } od ? FormatDate(od) : (object)DBNull.Value);
            cmd.Parameters.AddWithValue("$reason", l.ReasonCode);
            cmd.Parameters.AddWithValue("$b9", l.Is9BTarget ? 1 : 0);
            cmd.ExecuteNonQuery();
        }

        foreach (var a in c.AdvanceReceipts)
        {
            using var cmd = _connection.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = """
                INSERT INTO gst_advance_receipts
                    (id, company_id, receipt_voucher_id, is_service, advance_amount_paisa, rate_bp, inter_state,
                     pos_state_code, advance_tax_paisa, adjusted_against_invoice_vid, refund_voucher_id)
                VALUES ($id, $cid, $rv, $svc, $amt, $bp, $inter, $pos, $tax, $adj, $ref);
                """;
            cmd.Parameters.AddWithValue("$id", a.Id.ToString("D"));
            cmd.Parameters.AddWithValue("$cid", c.Id.ToString("D"));
            cmd.Parameters.AddWithValue("$rv", a.ReceiptVoucherId.ToString("D"));
            cmd.Parameters.AddWithValue("$svc", a.IsService ? 1 : 0);
            cmd.Parameters.AddWithValue("$amt", Paisa.FromMoney(a.AdvanceAmount));
            cmd.Parameters.AddWithValue("$bp", a.RateBasisPoints);
            cmd.Parameters.AddWithValue("$inter", a.InterState ? 1 : 0);
            cmd.Parameters.AddWithValue("$pos", (object?)a.PlaceOfSupplyStateCode ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$tax", Paisa.FromMoney(a.AdvanceTax));
            cmd.Parameters.AddWithValue("$adj", (object?)a.AdjustedAgainstInvoiceVoucherId?.ToString("D") ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$ref", (object?)a.RefundVoucherId?.ToString("D") ?? DBNull.Value);
            cmd.ExecuteNonQuery();
        }
    }

    /// <summary>v41: persists the e-invoice IRP artefacts (Phase 9 slice 4a). One row per covered voucher; the IRP-issued
    /// IRN/QR/signed-JSON are stored verbatim (ER-5). <c>ack_date</c>/<c>cancelled_on</c> ⇒ ISO TEXT; <c>signed_json</c>
    /// ⇒ BLOB. Empty when e-invoicing is unused (ER-13).</summary>
    private void InsertEInvoiceRecords(SqliteTransaction tx, Company c)
    {
        foreach (var r in c.EInvoiceRecords)
        {
            using var cmd = _connection.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = """
                INSERT INTO einvoice_records
                    (id, company_id, source_voucher_id, document_number_upper, status, irn, ack_no, ack_date, signed_qr,
                     signed_json, cancelled_on, cancel_reason_code, error_code, error_message)
                VALUES ($id, $cid, $src, $doc, $status, $irn, $ack, $ackdate, $qr, $json, $cancelled, $reason, $ecode, $emsg);
                """;
            cmd.Parameters.AddWithValue("$id", r.Id.ToString("D"));
            cmd.Parameters.AddWithValue("$cid", c.Id.ToString("D"));
            cmd.Parameters.AddWithValue("$src", r.SourceVoucherId.ToString("D"));
            cmd.Parameters.AddWithValue("$doc", r.DocumentNumberUpper);
            cmd.Parameters.AddWithValue("$status", (int)r.Status);
            cmd.Parameters.AddWithValue("$irn", (object?)r.Irn ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$ack", (object?)r.AckNo ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$ackdate", r.AckDate is { } ad ? FormatDate(ad) : (object)DBNull.Value);
            cmd.Parameters.AddWithValue("$qr", (object?)r.SignedQr ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$json", (object?)r.SignedJson ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$cancelled", r.CancelledOn is { } co ? FormatDate(co) : (object)DBNull.Value);
            cmd.Parameters.AddWithValue("$reason", (object?)r.CancelReasonCode ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$ecode", (object?)r.ErrorCode ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$emsg", (object?)r.ErrorMessage ?? DBNull.Value);
            cmd.ExecuteNonQuery();
        }
    }

    /// <summary>v42: persists the e-Way Bill artefacts (Phase 9 slice 5). The 12-digit EWB number, generation timestamp
    /// and validity are stored verbatim from the portal response (never derived, ER-5). FK the source voucher (inserted
    /// above). Empty when e-Way is unused (ER-13).</summary>
    private void InsertEWayBillRecords(SqliteTransaction tx, Company c)
    {
        foreach (var r in c.EWayBillRecords)
        {
            using var cmd = _connection.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = """
                INSERT INTO eway_bills
                    (id, company_id, source_voucher_id, document_number_upper, status, supply_type, sub_supply_type,
                     doc_type, consignment_value_paisa, transporter_id, trans_mode, vehicle_number, distance_km,
                     transport_doc_no, ship_from_state_code, ship_to_state_code, is_odc, ship_to_gstin, closure_requested,
                     closed_on, ewb_number, generated_at, valid_upto, cancelled_on, cancel_reason_code, error_code,
                     error_message)
                VALUES ($id, $cid, $src, $doc, $status, $supt, $subt, $doct, $cv, $tid, $mode, $veh, $dist, $tdoc,
                        $from, $to, $odc, $shipgstin, $closreq, $closed, $ewb, $genat, $valid, $cancelled, $reason,
                        $ecode, $emsg);
                """;
            cmd.Parameters.AddWithValue("$id", r.Id.ToString("D"));
            cmd.Parameters.AddWithValue("$cid", c.Id.ToString("D"));
            cmd.Parameters.AddWithValue("$src", r.SourceVoucherId.ToString("D"));
            cmd.Parameters.AddWithValue("$doc", r.DocumentNumberUpper);
            cmd.Parameters.AddWithValue("$status", (int)r.Status);
            cmd.Parameters.AddWithValue("$supt", (object?)r.SupplyType ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$subt", (object?)r.SubSupplyType ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$doct", (object?)r.DocType ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$cv", r.ConsignmentValuePaisa);
            cmd.Parameters.AddWithValue("$tid", (object?)r.TransporterId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$mode", r.Mode is { } m ? (int)m : (object)DBNull.Value);
            cmd.Parameters.AddWithValue("$veh", (object?)r.VehicleNumber ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$dist", r.DistanceKm);
            cmd.Parameters.AddWithValue("$tdoc", (object?)r.TransportDocNo ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$from", (object?)r.ShipFromStateCode ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$to", (object?)r.ShipToStateCode ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$odc", r.IsOverDimensionalCargo ? 1 : 0);
            cmd.Parameters.AddWithValue("$shipgstin", (object?)r.ShipToGstin ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$closreq", r.ClosureRequested ? 1 : 0);
            cmd.Parameters.AddWithValue("$closed", r.ClosedOn is { } cd ? FormatDate(cd) : (object)DBNull.Value);
            cmd.Parameters.AddWithValue("$ewb", (object?)r.EwbNumber ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$genat", r.GeneratedAt is { } ga ? FormatDateTimeOffset(ga) : (object)DBNull.Value);
            cmd.Parameters.AddWithValue("$valid", r.ValidUpto is { } vu ? FormatDateTimeOffset(vu) : (object)DBNull.Value);
            cmd.Parameters.AddWithValue("$cancelled", r.CancelledOn is { } co ? FormatDate(co) : (object)DBNull.Value);
            cmd.Parameters.AddWithValue("$reason", (object?)r.CancelReasonCode ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$ecode", (object?)r.ErrorCode ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$emsg", (object?)r.ErrorMessage ?? DBNull.Value);
            cmd.ExecuteNonQuery();
        }
    }

    /// <summary>v43: persists the imported GSTR-2B/2A snapshots + their child lines + the reconciliation results (Phase 9
    /// slice 6). Snapshots/lines are external portal data (no source-voucher FK); each snapshot is written then its lines
    /// (FK the snapshot). The recon rows FK the lines + an optional matched voucher — ADVISORY only (ER-14). Empty when
    /// 2B is never imported (ER-13).</summary>
    private void InsertGstr2bRecords(SqliteTransaction tx, Company c)
    {
        foreach (var s in c.Gstr2bSnapshots)
        {
            using (var cmd = _connection.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = """
                    INSERT INTO gstr2b_snapshots
                        (id, company_id, statement_type, return_period, recipient_gstin, generated_on, source_file_hash,
                         imported_at, summary_igst_paisa, summary_cgst_paisa, summary_sgst_paisa, summary_cess_paisa)
                    VALUES ($id, $cid, $type, $period, $gstin, $gen, $hash, $imp, $igst, $cgst, $sgst, $cess);
                    """;
                cmd.Parameters.AddWithValue("$id", s.Id.ToString("D"));
                cmd.Parameters.AddWithValue("$cid", c.Id.ToString("D"));
                cmd.Parameters.AddWithValue("$type", (int)s.StatementType);
                cmd.Parameters.AddWithValue("$period", s.ReturnPeriod);
                cmd.Parameters.AddWithValue("$gstin", s.RecipientGstin);
                cmd.Parameters.AddWithValue("$gen", s.GeneratedOn is { } g ? FormatDate(g) : (object)DBNull.Value);
                cmd.Parameters.AddWithValue("$hash", s.SourceFileHash);
                cmd.Parameters.AddWithValue("$imp", FormatDateTimeOffset(s.ImportedAt));
                cmd.Parameters.AddWithValue("$igst", s.SummaryIgstPaisa);
                cmd.Parameters.AddWithValue("$cgst", s.SummaryCgstPaisa);
                cmd.Parameters.AddWithValue("$sgst", s.SummarySgstPaisa);
                cmd.Parameters.AddWithValue("$cess", s.SummaryCessPaisa);
                cmd.ExecuteNonQuery();
            }

            foreach (var l in s.Lines)
            {
                using var cmd = _connection.CreateCommand();
                cmd.Transaction = tx;
                cmd.CommandText = """
                    INSERT INTO gstr2b_lines
                        (id, company_id, snapshot_id, supplier_gstin, supplier_trade_name, doc_type, doc_number,
                         doc_number_norm, doc_date, pos_state_code, taxable_value_paisa, igst_paisa, cgst_paisa,
                         sgst_paisa, cess_paisa, itc_available, itc_unavailable_reason, reverse_charge)
                    VALUES ($id, $cid, $sid, $sup, $trd, $dt, $dnum, $dnorm, $ddate, $pos, $txval, $igst, $cgst, $sgst,
                            $cess, $itc, $rsn, $rev);
                    """;
                cmd.Parameters.AddWithValue("$id", l.Id.ToString("D"));
                cmd.Parameters.AddWithValue("$cid", c.Id.ToString("D"));
                cmd.Parameters.AddWithValue("$sid", s.Id.ToString("D"));
                cmd.Parameters.AddWithValue("$sup", l.SupplierGstin);
                cmd.Parameters.AddWithValue("$trd", (object?)l.SupplierTradeName ?? DBNull.Value);
                cmd.Parameters.AddWithValue("$dt", (int)l.DocType);
                cmd.Parameters.AddWithValue("$dnum", l.DocNumber);
                cmd.Parameters.AddWithValue("$dnorm", (object?)l.DocNumberNorm ?? DBNull.Value);
                cmd.Parameters.AddWithValue("$ddate", FormatDate(l.DocDate));
                cmd.Parameters.AddWithValue("$pos", (object?)l.PosStateCode ?? DBNull.Value);
                cmd.Parameters.AddWithValue("$txval", l.TaxableValuePaisa);
                cmd.Parameters.AddWithValue("$igst", l.IgstPaisa);
                cmd.Parameters.AddWithValue("$cgst", l.CgstPaisa);
                cmd.Parameters.AddWithValue("$sgst", l.SgstPaisa);
                cmd.Parameters.AddWithValue("$cess", l.CessPaisa);
                cmd.Parameters.AddWithValue("$itc", l.ItcAvailable ? 1 : 0);
                cmd.Parameters.AddWithValue("$rsn", (object?)l.ItcUnavailableReason ?? DBNull.Value);
                cmd.Parameters.AddWithValue("$rev", l.ReverseCharge ? 1 : 0);
                cmd.ExecuteNonQuery();
            }
        }

        foreach (var r in c.Gstr2bReconResults)
        {
            using var cmd = _connection.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = """
                INSERT INTO gstr2b_recon
                    (id, company_id, line_id, bucket, matched_voucher_id, taxable_variance_paisa, tax_variance_paisa,
                     match_pinned, reconciled_at)
                VALUES ($id, $cid, $lid, $bucket, $vid, $txvar, $taxvar, $pinned, $recon);
                """;
            cmd.Parameters.AddWithValue("$id", r.Id.ToString("D"));
            cmd.Parameters.AddWithValue("$cid", c.Id.ToString("D"));
            cmd.Parameters.AddWithValue("$lid", r.LineId.ToString("D"));
            cmd.Parameters.AddWithValue("$bucket", (int)r.Bucket);
            cmd.Parameters.AddWithValue("$vid", (object?)r.MatchedVoucherId?.ToString("D") ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$txvar", r.TaxableVariancePaisa);
            cmd.Parameters.AddWithValue("$taxvar", r.TaxVariancePaisa);
            cmd.Parameters.AddWithValue("$pinned", r.MatchPinned ? 1 : 0);
            cmd.Parameters.AddWithValue("$recon", r.ReconciledAt is { } dto ? FormatDateTimeOffset(dto) : (object)DBNull.Value);
            cmd.ExecuteNonQuery();
        }

        // v43 (Phase 9 slice 6b): the offline IMS decisions (all-rows-replace on save; the child-cleanup DELETEs
        // ims_status first). ADVISORY only — the mirror stores the decision + the Oct-2025 CDN reversal declaration but
        // posts nothing (ER-14).
        foreach (var a in c.ImsActions)
        {
            using var cmd = _connection.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = """
                INSERT INTO ims_status
                    (id, company_id, line_id, action, remarks, declared_reversal_paisa, no_reversal_declared, acted_on)
                VALUES ($id, $cid, $lid, $act, $rem, $rev, $norev, $acted);
                """;
            cmd.Parameters.AddWithValue("$id", a.Id.ToString("D"));
            cmd.Parameters.AddWithValue("$cid", c.Id.ToString("D"));
            cmd.Parameters.AddWithValue("$lid", a.LineId.ToString("D"));
            cmd.Parameters.AddWithValue("$act", (int)a.Status);
            cmd.Parameters.AddWithValue("$rem", (object?)a.Remarks ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$rev", a.DeclaredReversalPaisa is { } drp ? drp : (object)DBNull.Value);
            cmd.Parameters.AddWithValue("$norev", a.NoReversalDeclared ? 1 : 0);
            cmd.Parameters.AddWithValue("$acted", a.ActedOn is { } ao ? FormatDate(ao) : (object)DBNull.Value);
            cmd.ExecuteNonQuery();
        }
    }

    /// <summary>
    /// v44: persists the Phase-9-slice-7 electronic-ledger records — PMT-06 challans, DRC-03 payments, Rule-88A set-off
    /// allocation rows, and (empty in S7a) ITC-reversal audit rows. All-rows-replace on save (the child-cleanup DELETEs
    /// above ran first). Insert order: gst_drc03 before itc_reversals (the reversal FKs drc03_id). A company that never
    /// sets off / pays / reverses writes nothing — byte-identical (ER-13). Money is integer paisa.
    /// </summary>
    private void InsertGstSetOffRecords(SqliteTransaction tx, Company c)
    {
        foreach (var ch in c.GstChallans)
        {
            using var cmd = _connection.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = """
                INSERT INTO gst_challans
                    (id, company_id, cpin, cin, brn, deposit_date, major_head, minor_head, amount_paisa, voucher_id,
                     interest_flag)
                VALUES ($id, $cid, $cpin, $cin, $brn, $dd, $maj, $min, $amt, $vid, $int);
                """;
            cmd.Parameters.AddWithValue("$id", ch.Id.ToString("D"));
            cmd.Parameters.AddWithValue("$cid", c.Id.ToString("D"));
            cmd.Parameters.AddWithValue("$cpin", ch.Cpin);
            cmd.Parameters.AddWithValue("$cin", (object?)ch.Cin ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$brn", (object?)ch.Brn ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$dd", FormatDate(ch.DepositDate));
            cmd.Parameters.AddWithValue("$maj", (int)ch.MajorHead);
            cmd.Parameters.AddWithValue("$min", (int)ch.MinorHead);
            cmd.Parameters.AddWithValue("$amt", Paisa.FromMoney(ch.Amount));
            cmd.Parameters.AddWithValue("$vid", ch.VoucherId.ToString("D"));
            cmd.Parameters.AddWithValue("$int", ch.InterestFlag ? 1 : 0);
            cmd.ExecuteNonQuery();
        }

        foreach (var d in c.GstDrc03s)
        {
            using var cmd = _connection.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = """
                INSERT INTO gst_drc03
                    (id, company_id, drc03_ref, cause, period, cgst_paisa, sgst_paisa, igst_paisa, cess_paisa,
                     interest_paisa, drc03a_demand_ref, voucher_id, created_at)
                VALUES ($id, $cid, $ref, $cause, $period, $cgst, $sgst, $igst, $cess, $int, $dref, $vid, $created);
                """;
            cmd.Parameters.AddWithValue("$id", d.Id.ToString("D"));
            cmd.Parameters.AddWithValue("$cid", c.Id.ToString("D"));
            cmd.Parameters.AddWithValue("$ref", (object?)d.Drc03Ref ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$cause", d.Cause);
            cmd.Parameters.AddWithValue("$period", d.Period);
            cmd.Parameters.AddWithValue("$cgst", d.CgstPaisa);
            cmd.Parameters.AddWithValue("$sgst", d.SgstPaisa);
            cmd.Parameters.AddWithValue("$igst", d.IgstPaisa);
            cmd.Parameters.AddWithValue("$cess", d.CessPaisa);
            cmd.Parameters.AddWithValue("$int", d.InterestPaisa);
            cmd.Parameters.AddWithValue("$dref", (object?)d.Drc03aDemandRef ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$vid", (object?)d.VoucherId?.ToString("D") ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$created", FormatDateTimeOffset(d.CreatedAt));
            cmd.ExecuteNonQuery();
        }

        foreach (var l in c.GstSetoffLines)
        {
            using var cmd = _connection.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = """
                INSERT INTO gst_setoff_lines
                    (id, company_id, voucher_id, period, credit_head, liability_head, is_cash, amount_paisa)
                VALUES ($id, $cid, $vid, $period, $ch, $lh, $cash, $amt);
                """;
            cmd.Parameters.AddWithValue("$id", l.Id.ToString("D"));
            cmd.Parameters.AddWithValue("$cid", c.Id.ToString("D"));
            cmd.Parameters.AddWithValue("$vid", l.VoucherId.ToString("D"));
            cmd.Parameters.AddWithValue("$period", l.Period);
            cmd.Parameters.AddWithValue("$ch", (int)l.CreditHead);
            cmd.Parameters.AddWithValue("$lh", (int)l.LiabilityHead);
            cmd.Parameters.AddWithValue("$cash", l.IsCash ? 1 : 0);
            cmd.Parameters.AddWithValue("$amt", l.AmountPaisa);
            cmd.ExecuteNonQuery();
        }

        // itc_reversals lands empty in S7a (the reversal engine is S7b), but the saver is complete so the table
        // round-trips the moment S7b fills it.
        foreach (var rv in c.ItcReversals)
        {
            using var cmd = _connection.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = """
                INSERT INTO itc_reversals
                    (id, company_id, rule, period, cgst_paisa, sgst_paisa, igst_paisa, cess_paisa, d1_basis_paisa,
                     d2_basis_paisa, source_voucher_id, source_line_id, reversal_voucher_id, reclaim_of_id, drc03_id,
                     table4b_bucket, created_at)
                VALUES ($id, $cid, $rule, $period, $cgst, $sgst, $igst, $cess, $d1, $d2, $sv, $sl, $rv, $reclaim,
                        $drc03, $bucket, $created);
                """;
            cmd.Parameters.AddWithValue("$id", rv.Id.ToString("D"));
            cmd.Parameters.AddWithValue("$cid", c.Id.ToString("D"));
            cmd.Parameters.AddWithValue("$rule", (int)rv.Rule);
            cmd.Parameters.AddWithValue("$period", rv.Period);
            cmd.Parameters.AddWithValue("$cgst", rv.CgstPaisa);
            cmd.Parameters.AddWithValue("$sgst", rv.SgstPaisa);
            cmd.Parameters.AddWithValue("$igst", rv.IgstPaisa);
            cmd.Parameters.AddWithValue("$cess", rv.CessPaisa);
            cmd.Parameters.AddWithValue("$d1", rv.D1BasisPaisa is { } d1 ? d1 : (object)DBNull.Value);
            cmd.Parameters.AddWithValue("$d2", rv.D2BasisPaisa is { } d2 ? d2 : (object)DBNull.Value);
            cmd.Parameters.AddWithValue("$sv", (object?)rv.SourceVoucherId?.ToString("D") ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$sl", (object?)rv.SourceLineId?.ToString("D") ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$rv", rv.ReversalVoucherId.ToString("D"));
            cmd.Parameters.AddWithValue("$reclaim", (object?)rv.ReclaimOfId?.ToString("D") ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$drc03", (object?)rv.Drc03Id?.ToString("D") ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$bucket", (int)rv.Table4bBucket);
            cmd.Parameters.AddWithValue("$created", FormatDateTimeOffset(rv.CreatedAt));
            cmd.ExecuteNonQuery();
        }
    }

    /// <summary>
    /// Persists the POS retail-till config (v23; RQ-38/DP-4) for every POS-flagged Sales voucher type: one
    /// <c>pos_voucher_type_config</c> row plus the tender-ledger class-map rows in <c>pos_tender_ledger_defaults</c>.
    /// Called after ledgers + godowns are inserted (its FKs reference both) and after voucher_types. A non-POS type,
    /// or a POS type with no <see cref="PosConfig"/>, writes nothing — byte-identical (ER-13).
    /// </summary>
    private void InsertPosVoucherTypeConfig(SqliteTransaction tx, Company c)
    {
        foreach (var t in c.VoucherTypes)
        {
            if (!t.UseForPos || t.PosConfig is not { } cfg) continue;
            using (var cmd = _connection.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = """
                    INSERT INTO pos_voucher_type_config
                        (voucher_type_id, default_godown_id, default_party_id, print_after_save,
                         default_title, message_1, message_2, declaration)
                    VALUES ($vt, $god, $party, $print, $title, $m1, $m2, $decl);
                    """;
                cmd.Parameters.AddWithValue("$vt", t.Id.ToString("D"));
                cmd.Parameters.AddWithValue("$god", (object?)cfg.DefaultGodownId?.ToString("D") ?? DBNull.Value);
                cmd.Parameters.AddWithValue("$party", (object?)cfg.DefaultPartyId?.ToString("D") ?? DBNull.Value);
                cmd.Parameters.AddWithValue("$print", cfg.PrintAfterSave ? 1 : 0);
                cmd.Parameters.AddWithValue("$title", (object?)cfg.DefaultTitle ?? DBNull.Value);
                cmd.Parameters.AddWithValue("$m1", (object?)cfg.Message1 ?? DBNull.Value);
                cmd.Parameters.AddWithValue("$m2", (object?)cfg.Message2 ?? DBNull.Value);
                cmd.Parameters.AddWithValue("$decl", (object?)cfg.Declaration ?? DBNull.Value);
                cmd.ExecuteNonQuery();
            }

            foreach (var (tenderType, ledgerId) in cfg.TenderLedgerDefaults)
            {
                using var cmd = _connection.CreateCommand();
                cmd.Transaction = tx;
                cmd.CommandText = """
                    INSERT INTO pos_tender_ledger_defaults (voucher_type_id, tender_type, ledger_id)
                    VALUES ($vt, $tt, $lid);
                    """;
                cmd.Parameters.AddWithValue("$vt", t.Id.ToString("D"));
                cmd.Parameters.AddWithValue("$tt", (int)tenderType);
                cmd.Parameters.AddWithValue("$lid", ledgerId.ToString("D"));
                cmd.ExecuteNonQuery();
            }
        }
    }

    private void InsertCostCategories(SqliteTransaction tx, Company c)
    {
        foreach (var cat in c.CostCategories)
        {
            using var cmd = _connection.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = """
                INSERT INTO cost_categories
                    (id, company_id, name, allocate_revenue, allocate_non_revenue, is_predefined)
                VALUES ($id, $cid, $name, $rev, $nonrev, $pre);
                """;
            cmd.Parameters.AddWithValue("$id", cat.Id.ToString("D"));
            cmd.Parameters.AddWithValue("$cid", c.Id.ToString("D"));
            cmd.Parameters.AddWithValue("$name", cat.Name);
            cmd.Parameters.AddWithValue("$rev", cat.AllocateRevenueItems ? 1 : 0);
            cmd.Parameters.AddWithValue("$nonrev", cat.AllocateNonRevenueItems ? 1 : 0);
            cmd.Parameters.AddWithValue("$pre", cat.IsPredefined ? 1 : 0);
            cmd.ExecuteNonQuery();
        }
    }

    private void InsertCostCentres(SqliteTransaction tx, Company c)
    {
        foreach (var centre in c.CostCentres)
        {
            using var cmd = _connection.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = """
                INSERT INTO cost_centres (id, company_id, name, category_id, parent_id, alias)
                VALUES ($id, $cid, $name, $cat, $parent, $alias);
                """;
            cmd.Parameters.AddWithValue("$id", centre.Id.ToString("D"));
            cmd.Parameters.AddWithValue("$cid", c.Id.ToString("D"));
            cmd.Parameters.AddWithValue("$name", centre.Name);
            cmd.Parameters.AddWithValue("$cat", centre.CategoryId.ToString("D"));
            cmd.Parameters.AddWithValue("$parent", (object?)centre.ParentId?.ToString("D") ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$alias", (object?)centre.Alias ?? DBNull.Value);
            cmd.ExecuteNonQuery();
        }
    }

    private void InsertUnits(SqliteTransaction tx, Company c)
    {
        foreach (var u in c.Units)
        {
            using var cmd = _connection.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = """
                INSERT INTO units
                    (id, company_id, symbol, formal_name, is_compound, uqc, decimal_places,
                     first_unit_id, tail_unit_id, conversion_numerator, conversion_denominator)
                VALUES ($id, $cid, $sym, $fn, $comp, $uqc, $dp, $first, $tail, $num, $den);
                """;
            cmd.Parameters.AddWithValue("$id", u.Id.ToString("D"));
            cmd.Parameters.AddWithValue("$cid", c.Id.ToString("D"));
            cmd.Parameters.AddWithValue("$sym", u.Symbol);
            cmd.Parameters.AddWithValue("$fn", u.FormalName);
            cmd.Parameters.AddWithValue("$comp", u.IsCompound ? 1 : 0);
            cmd.Parameters.AddWithValue("$uqc", (object?)u.UnitQuantityCode ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$dp", u.DecimalPlaces);
            cmd.Parameters.AddWithValue("$first", (object?)u.FirstUnitId?.ToString("D") ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$tail", (object?)u.TailUnitId?.ToString("D") ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$num", (object?)u.ConversionNumerator ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$den", (object?)u.ConversionDenominator ?? DBNull.Value);
            cmd.ExecuteNonQuery();
        }
    }

    private void InsertEmployeeCategories(SqliteTransaction tx, Company c)
    {
        foreach (var cat in c.EmployeeCategories)
        {
            using var cmd = _connection.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = """
                INSERT INTO employee_categories (id, company_id, name, allocate_revenue, allocate_non_revenue, is_predefined)
                VALUES ($id, $cid, $name, $rev, $nonrev, $pre);
                """;
            cmd.Parameters.AddWithValue("$id", cat.Id.ToString("D"));
            cmd.Parameters.AddWithValue("$cid", c.Id.ToString("D"));
            cmd.Parameters.AddWithValue("$name", cat.Name);
            cmd.Parameters.AddWithValue("$rev", cat.AllocateRevenueItems ? 1 : 0);
            cmd.Parameters.AddWithValue("$nonrev", cat.AllocateNonRevenueItems ? 1 : 0);
            cmd.Parameters.AddWithValue("$pre", cat.IsPredefined ? 1 : 0);
            cmd.ExecuteNonQuery();
        }
    }

    private void InsertEmployeeGroups(SqliteTransaction tx, Company c)
    {
        // Self-FK (parent_id → id): insert parents before children (a cycle throws a clean domain exception).
        var ordered = HierarchyOrdering.ParentsBeforeChildren(
            c.EmployeeGroups, g => g.Id, g => g.ParentId, "Employee group");
        foreach (var g in ordered)
        {
            using var cmd = _connection.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = """
                INSERT INTO employee_groups (id, company_id, name, parent_id, alias, define_salary_details)
                VALUES ($id, $cid, $name, $parent, $alias, $salary);
                """;
            cmd.Parameters.AddWithValue("$id", g.Id.ToString("D"));
            cmd.Parameters.AddWithValue("$cid", c.Id.ToString("D"));
            cmd.Parameters.AddWithValue("$name", g.Name);
            cmd.Parameters.AddWithValue("$parent", (object?)g.ParentId?.ToString("D") ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$alias", (object?)g.Alias ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$salary", g.DefineSalaryDetails ? 1 : 0);
            cmd.ExecuteNonQuery();
        }
    }

    private void InsertPayrollUnits(SqliteTransaction tx, Company c)
    {
        // Self-FK (first/tail → id): simple units before compound (a compound references two simple units).
        var ordered = c.PayrollUnits.Where(u => !u.IsCompound).Concat(c.PayrollUnits.Where(u => u.IsCompound));
        foreach (var u in ordered)
        {
            using var cmd = _connection.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = """
                INSERT INTO payroll_units
                    (id, company_id, symbol, formal_name, is_compound, decimal_places,
                     first_unit_id, tail_unit_id, conversion_numerator, conversion_denominator)
                VALUES ($id, $cid, $sym, $fn, $comp, $dp, $first, $tail, $num, $den);
                """;
            cmd.Parameters.AddWithValue("$id", u.Id.ToString("D"));
            cmd.Parameters.AddWithValue("$cid", c.Id.ToString("D"));
            cmd.Parameters.AddWithValue("$sym", u.Symbol);
            cmd.Parameters.AddWithValue("$fn", u.FormalName);
            cmd.Parameters.AddWithValue("$comp", u.IsCompound ? 1 : 0);
            cmd.Parameters.AddWithValue("$dp", u.DecimalPlaces);
            cmd.Parameters.AddWithValue("$first", (object?)u.FirstUnitId?.ToString("D") ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$tail", (object?)u.TailUnitId?.ToString("D") ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$num", (object?)u.ConversionNumerator ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$den", (object?)u.ConversionDenominator ?? DBNull.Value);
            cmd.ExecuteNonQuery();
        }
    }

    private void InsertAttendanceTypes(SqliteTransaction tx, Company c)
    {
        // Self-FK (parent_id → id): insert parents before children.
        var ordered = HierarchyOrdering.ParentsBeforeChildren(
            c.AttendanceTypes, a => a.Id, a => a.ParentId, "Attendance type");
        foreach (var a in ordered)
        {
            using var cmd = _connection.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = """
                INSERT INTO attendance_types (id, company_id, name, parent_id, kind, payroll_unit_id)
                VALUES ($id, $cid, $name, $parent, $kind, $unit);
                """;
            cmd.Parameters.AddWithValue("$id", a.Id.ToString("D"));
            cmd.Parameters.AddWithValue("$cid", c.Id.ToString("D"));
            cmd.Parameters.AddWithValue("$name", a.Name);
            cmd.Parameters.AddWithValue("$parent", (object?)a.ParentId?.ToString("D") ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$kind", (int)a.Kind);
            cmd.Parameters.AddWithValue("$unit", (object?)a.PayrollUnitId?.ToString("D") ?? DBNull.Value);
            cmd.ExecuteNonQuery();
        }
    }

    private void InsertEmployees(SqliteTransaction tx, Company c)
    {
        foreach (var e in c.Employees)
        {
            using var cmd = _connection.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = """
                INSERT INTO employees
                    (id, company_id, name, employee_group_id, employee_category_id, employee_number,
                     date_of_joining, date_of_leaving, designation, function, location, gender, date_of_birth,
                     pan, aadhaar, uan, pf_account_number, esi_number,
                     bank_account_number, bank_name, bank_ifsc, tax_regime,
                     pf_applicable, pf_higher_wages, pf_join_date,
                     esi_applicable, esi_person_with_disability)
                VALUES ($id, $cid, $name, $grp, $cat, $num,
                        $doj, $dol, $desig, $func, $loc, $gender, $dob,
                        $pan, $aadhaar, $uan, $pfacc, $esi,
                        $bankacc, $bankname, $ifsc, $regime,
                        $pfapp, $pfhigh, $pfjoin,
                        $esiapp, $esidis);
                """;
            cmd.Parameters.AddWithValue("$id", e.Id.ToString("D"));
            cmd.Parameters.AddWithValue("$cid", c.Id.ToString("D"));
            cmd.Parameters.AddWithValue("$name", e.Name);
            cmd.Parameters.AddWithValue("$grp", e.EmployeeGroupId.ToString("D"));
            cmd.Parameters.AddWithValue("$cat", (object?)e.EmployeeCategoryId?.ToString("D") ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$num", (object?)e.EmployeeNumber ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$doj", e.DateOfJoining is { } j ? FormatDate(j) : (object)DBNull.Value);
            cmd.Parameters.AddWithValue("$dol", e.DateOfLeaving is { } lv ? FormatDate(lv) : (object)DBNull.Value);
            cmd.Parameters.AddWithValue("$desig", (object?)e.Designation ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$func", (object?)e.Function ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$loc", (object?)e.Location ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$gender", (object?)e.Gender ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$dob", e.DateOfBirth is { } d ? FormatDate(d) : (object)DBNull.Value);
            cmd.Parameters.AddWithValue("$pan", (object?)e.Pan ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$aadhaar", (object?)e.Aadhaar ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$uan", (object?)e.Uan ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$pfacc", (object?)e.PfAccountNumber ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$esi", (object?)e.EsiNumber ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$bankacc", (object?)e.BankAccountNumber ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$bankname", (object?)e.BankName ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$ifsc", (object?)e.BankIfsc ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$regime", (int)e.ApplicableTaxRegime);
            // v33 (Phase 8 slice 4): per-employee PF details (default off — ER-13).
            cmd.Parameters.AddWithValue("$pfapp", e.PfApplicable ? 1 : 0);
            cmd.Parameters.AddWithValue("$pfhigh", e.PfContributeOnHigherWages ? 1 : 0);
            cmd.Parameters.AddWithValue("$pfjoin", e.PfJoinDate is { } pfj ? FormatDate(pfj) : (object)DBNull.Value);
            // v34 (Phase 8 slice 5): per-employee ESI applicability + person-with-disability (default off — ER-13).
            cmd.Parameters.AddWithValue("$esiapp", e.EsiApplicable ? 1 : 0);
            cmd.Parameters.AddWithValue("$esidis", e.IsPersonWithDisability ? 1 : 0);
            cmd.ExecuteNonQuery();
        }
    }

    private void InsertPayHeads(SqliteTransaction tx, Company c)
    {
        // All master rows first (no pay-head→pay-head row-level FK), then the computed-on + slab child rows.
        foreach (var p in c.PayHeads)
        {
            using var cmd = _connection.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = """
                INSERT INTO pay_heads
                    (id, company_id, name, display_name, pay_head_type, calculation_type, affects_net_salary,
                     under_group_id, ledger_id, income_tax_component, use_for_gratuity,
                     rounding_method, rounding_limit_paisa, calculation_period, attendance_type_id, per_day_calculation_basis,
                     employer_expense_ledger_id, pf_component, part_of_pf_wages,
                     esi_component, part_of_esi_wages, is_overtime, pt_component)
                VALUES ($id, $cid, $name, $disp, $type, $calc, $anet,
                        $grp, $led, $itc, $grat,
                        $rm, $rl, $cp, $atid, $pdb,
                        $eeled, $pfcomp, $pfwage,
                        $esicomp, $esiwage, $ot, $ptcomp);
                """;
            cmd.Parameters.AddWithValue("$id", p.Id.ToString("D"));
            cmd.Parameters.AddWithValue("$cid", c.Id.ToString("D"));
            cmd.Parameters.AddWithValue("$name", p.Name);
            cmd.Parameters.AddWithValue("$disp", (object?)p.DisplayName ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$type", (int)p.Type);
            cmd.Parameters.AddWithValue("$calc", (int)p.CalculationType);
            cmd.Parameters.AddWithValue("$anet", p.AffectsNetSalary ? 1 : 0);
            cmd.Parameters.AddWithValue("$grp", (object?)p.UnderGroupId?.ToString("D") ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$led", (object?)p.LedgerId?.ToString("D") ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$itc", (int)p.IncomeTaxComponent);
            cmd.Parameters.AddWithValue("$grat", p.UseForGratuity ? 1 : 0);
            cmd.Parameters.AddWithValue("$rm", (int)p.RoundingMethod);
            cmd.Parameters.AddWithValue("$rl", Paisa.FromMoney(p.RoundingLimit));
            cmd.Parameters.AddWithValue("$cp", (int)p.CalculationPeriod);
            cmd.Parameters.AddWithValue("$atid", (object?)p.AttendanceTypeId?.ToString("D") ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$pdb", (object?)p.PerDayCalculationBasisDays ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$eeled", (object?)p.EmployerExpenseLedgerId?.ToString("D") ?? DBNull.Value);
            // v33 (Phase 8 slice 4): PF statutory role + PF-wage flag (default None/false — ER-13).
            cmd.Parameters.AddWithValue("$pfcomp", (int)p.PfComponent);
            cmd.Parameters.AddWithValue("$pfwage", p.PartOfPfWages ? 1 : 0);
            // v34 (Phase 8 slice 5): ESI statutory role + ESI-wage flag + overtime marker (default None/false — ER-13).
            cmd.Parameters.AddWithValue("$esicomp", (int)p.EsiComponent);
            cmd.Parameters.AddWithValue("$esiwage", p.PartOfEsiWages ? 1 : 0);
            cmd.Parameters.AddWithValue("$ot", p.IsOvertime ? 1 : 0);
            // v35 (Phase 8 slice 6): PT statutory role (default None — ER-13).
            cmd.Parameters.AddWithValue("$ptcomp", (int)p.PtComponent);
            cmd.ExecuteNonQuery();
        }

        foreach (var p in c.PayHeads)
        {
            if (p.Computation is not { } comp) continue;
            var ord = 0;
            foreach (var component in comp.BasisComponents)
            {
                using var cmd = _connection.CreateCommand();
                cmd.Transaction = tx;
                cmd.CommandText = """
                    INSERT INTO pay_head_computation (pay_head_id, component_pay_head_id, is_subtraction, ord)
                    VALUES ($ph, $comp, $sub, $ord);
                    """;
                cmd.Parameters.AddWithValue("$ph", p.Id.ToString("D"));
                cmd.Parameters.AddWithValue("$comp", component.PayHeadId.ToString("D"));
                cmd.Parameters.AddWithValue("$sub", component.IsSubtraction ? 1 : 0);
                cmd.Parameters.AddWithValue("$ord", ord++);
                cmd.ExecuteNonQuery();
            }

            ord = 0;
            foreach (var slab in comp.Slabs)
            {
                using var cmd = _connection.CreateCommand();
                cmd.Transaction = tx;
                cmd.CommandText = """
                    INSERT INTO pay_head_computation_slabs
                        (pay_head_id, from_amount_paisa, to_amount_paisa, slab_type, rate_basis_points, value_paisa, ord)
                    VALUES ($ph, $from, $to, $st, $rate, $val, $ord);
                    """;
                cmd.Parameters.AddWithValue("$ph", p.Id.ToString("D"));
                cmd.Parameters.AddWithValue("$from", slab.FromAmount is { } f ? Paisa.FromMoney(f) : (object)DBNull.Value);
                cmd.Parameters.AddWithValue("$to", slab.ToAmount is { } t ? Paisa.FromMoney(t) : (object)DBNull.Value);
                cmd.Parameters.AddWithValue("$st", (int)slab.SlabType);
                cmd.Parameters.AddWithValue("$rate", slab.RateBasisPoints);
                cmd.Parameters.AddWithValue("$val", Paisa.FromMoney(slab.Value));
                cmd.Parameters.AddWithValue("$ord", ord++);
                cmd.ExecuteNonQuery();
            }
        }
    }

    private void InsertSalaryStructures(SqliteTransaction tx, Company c)
    {
        foreach (var s in c.SalaryStructures)
        {
            using (var cmd = _connection.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = """
                    INSERT INTO salary_structures (id, company_id, scope, scope_id, effective_from, start_type)
                    VALUES ($id, $cid, $scope, $sid, $eff, $start);
                    """;
                cmd.Parameters.AddWithValue("$id", s.Id.ToString("D"));
                cmd.Parameters.AddWithValue("$cid", c.Id.ToString("D"));
                cmd.Parameters.AddWithValue("$scope", (int)s.Scope);
                cmd.Parameters.AddWithValue("$sid", s.ScopeId.ToString("D"));
                cmd.Parameters.AddWithValue("$eff", FormatDate(s.EffectiveFrom));
                cmd.Parameters.AddWithValue("$start", (int)s.StartType);
                cmd.ExecuteNonQuery();
            }

            foreach (var line in s.Lines)
            {
                using var cmd = _connection.CreateCommand();
                cmd.Transaction = tx;
                cmd.CommandText = """
                    INSERT INTO salary_structure_lines (salary_structure_id, pay_head_id, ord, amount_paisa)
                    VALUES ($sid, $ph, $ord, $amt);
                    """;
                cmd.Parameters.AddWithValue("$sid", s.Id.ToString("D"));
                cmd.Parameters.AddWithValue("$ph", line.PayHeadId.ToString("D"));
                cmd.Parameters.AddWithValue("$ord", line.Order);
                cmd.Parameters.AddWithValue("$amt", line.Amount is { } m ? Paisa.FromMoney(m) : (object)DBNull.Value);
                cmd.ExecuteNonQuery();
            }
        }
    }

    private void InsertAttendanceEntries(SqliteTransaction tx, Company c)
    {
        foreach (var a in c.AttendanceEntries)
        {
            using var cmd = _connection.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = """
                INSERT INTO attendance_entries
                    (id, company_id, employee_id, attendance_type_id, from_date, to_date, value_micro)
                VALUES ($id, $cid, $emp, $atid, $from, $to, $val);
                """;
            cmd.Parameters.AddWithValue("$id", a.Id.ToString("D"));
            cmd.Parameters.AddWithValue("$cid", c.Id.ToString("D"));
            cmd.Parameters.AddWithValue("$emp", a.EmployeeId.ToString("D"));
            cmd.Parameters.AddWithValue("$atid", a.AttendanceTypeId.ToString("D"));
            cmd.Parameters.AddWithValue("$from", FormatDate(a.FromDate));
            cmd.Parameters.AddWithValue("$to", FormatDate(a.ToDate));
            cmd.Parameters.AddWithValue("$val", (long)(a.Value * 1_000_000m));
            cmd.ExecuteNonQuery();
        }
    }

    private void InsertStockGroups(SqliteTransaction tx, Company c)
    {
        // Self-FK (parent_id → id) is enforced, so a parent must be inserted before its children — even when
        // it appears later in list order (e.g. re-parented under a later-created sibling). A cycle throws a
        // clean domain exception rather than a raw FK error (DEFECT 1).
        var ordered = HierarchyOrdering.ParentsBeforeChildren(
            c.StockGroups, g => g.Id, g => g.ParentId, "Stock group");
        foreach (var g in ordered)
        {
            using var cmd = _connection.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = """
                INSERT INTO stock_groups (id, company_id, name, parent_id, alias, add_quantities)
                VALUES ($id, $cid, $name, $parent, $alias, $addq);
                """;
            cmd.Parameters.AddWithValue("$id", g.Id.ToString("D"));
            cmd.Parameters.AddWithValue("$cid", c.Id.ToString("D"));
            cmd.Parameters.AddWithValue("$name", g.Name);
            cmd.Parameters.AddWithValue("$parent", (object?)g.ParentId?.ToString("D") ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$alias", (object?)g.Alias ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$addq", g.AddQuantities ? 1 : 0);
            cmd.ExecuteNonQuery();
        }
    }

    private void InsertStockCategories(SqliteTransaction tx, Company c)
    {
        var ordered = HierarchyOrdering.ParentsBeforeChildren(
            c.StockCategories, cat => cat.Id, cat => cat.ParentId, "Stock category");
        foreach (var cat in ordered)
        {
            using var cmd = _connection.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = """
                INSERT INTO stock_categories (id, company_id, name, parent_id, alias)
                VALUES ($id, $cid, $name, $parent, $alias);
                """;
            cmd.Parameters.AddWithValue("$id", cat.Id.ToString("D"));
            cmd.Parameters.AddWithValue("$cid", c.Id.ToString("D"));
            cmd.Parameters.AddWithValue("$name", cat.Name);
            cmd.Parameters.AddWithValue("$parent", (object?)cat.ParentId?.ToString("D") ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$alias", (object?)cat.Alias ?? DBNull.Value);
            cmd.ExecuteNonQuery();
        }
    }

    private void InsertGodowns(SqliteTransaction tx, Company c)
    {
        var ordered = HierarchyOrdering.ParentsBeforeChildren(
            c.Godowns, g => g.Id, g => g.ParentId, "Godown");
        foreach (var g in ordered)
        {
            using var cmd = _connection.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = """
                INSERT INTO godowns (id, company_id, name, parent_id, alias, third_party, is_main_location)
                VALUES ($id, $cid, $name, $parent, $alias, $tp, $main);
                """;
            cmd.Parameters.AddWithValue("$id", g.Id.ToString("D"));
            cmd.Parameters.AddWithValue("$cid", c.Id.ToString("D"));
            cmd.Parameters.AddWithValue("$name", g.Name);
            cmd.Parameters.AddWithValue("$parent", (object?)g.ParentId?.ToString("D") ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$alias", (object?)g.Alias ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$tp", g.ThirdParty ? 1 : 0);
            cmd.Parameters.AddWithValue("$main", g.IsMainLocation ? 1 : 0);
            cmd.ExecuteNonQuery();
        }
    }

    private void InsertStockItems(SqliteTransaction tx, Company c)
    {
        foreach (var item in c.StockItems)
        {
            using var cmd = _connection.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = """
                INSERT INTO stock_items
                    (id, company_id, name, stock_group_id, category_id, base_unit_id, alias, valuation_method,
                     hsn_sac_code, is_taxable, reorder_level_micro, min_order_qty_micro, standard_cost_paisa,
                     gst_hsn_sac, gst_taxability, gst_rate_bp, gst_supply_type,
                     maintain_in_batches, track_manufacturing_date, use_expiry_dates, set_components,
                     tcs_nature_id,
                     gst_valuation_basis, cess_applicable, cess_valuation_mode, cess_rate_bp,
                     cess_per_unit_paisa, cess_rsp_factor_millis, rsp_paisa,
                     reverse_charge_applicable, gta_forward_charge, rcm_category_id,
                     itc_eligibility, blocked_credit_category)
                VALUES ($id, $cid, $name, $grp, $cat, $unit, $alias, $vm, $hsn, $tax, $rol, $moq, $std,
                        $ghsn, $gtax, $grate, $gsup, $mib, $tmd, $ued, $setc, $tcsnat,
                        $gvb, $cess, $cvm, $crate, $cpu, $crsp, $rsp,
                        $rca, $gtafc, $rcmcat,
                        $itcelig, $blkcat);
                """;
            cmd.Parameters.AddWithValue("$id", item.Id.ToString("D"));
            cmd.Parameters.AddWithValue("$cid", c.Id.ToString("D"));
            cmd.Parameters.AddWithValue("$name", item.Name);
            cmd.Parameters.AddWithValue("$grp", item.StockGroupId.ToString("D"));
            cmd.Parameters.AddWithValue("$cat", (object?)item.CategoryId?.ToString("D") ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$unit", item.BaseUnitId.ToString("D"));
            cmd.Parameters.AddWithValue("$alias", (object?)item.Alias ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$vm", (int)item.ValuationMethod);
            cmd.Parameters.AddWithValue("$hsn", (object?)item.HsnSacCode ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$tax", item.IsTaxable ? 1 : 0);
            cmd.Parameters.AddWithValue("$rol", item.ReorderLevel is { } rol ? QtyMicroFromDecimal(rol) : (object)DBNull.Value);
            cmd.Parameters.AddWithValue("$moq", item.MinimumOrderQuantity is { } moq ? QtyMicroFromDecimal(moq) : (object)DBNull.Value);
            cmd.Parameters.AddWithValue("$std", item.StandardCost is { } std ? Paisa.FromMoney(std) : (object)DBNull.Value);

            // v13 item GST block (all NULL when the item has no GST block).
            var g = item.Gst;
            cmd.Parameters.AddWithValue("$ghsn", (object?)g?.HsnSac ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$gtax", g is null ? (object)DBNull.Value : (int)g.Taxability);
            cmd.Parameters.AddWithValue("$grate", g?.RateBasisPoints is { } grbp ? grbp : (object)DBNull.Value);
            cmd.Parameters.AddWithValue("$gsup", g is null ? (object)DBNull.Value : (int)g.SupplyType);

            // v38 item GST 2.0 RSP valuation + Compensation-Cess (default off/null for a plain item).
            cmd.Parameters.AddWithValue("$gvb", g is null ? 0 : (int)g.ValuationBasis);
            cmd.Parameters.AddWithValue("$cess", g is { CessApplicable: true } ? 1 : 0);
            cmd.Parameters.AddWithValue("$cvm", g?.CessValuationMode is { } cvm ? (int)cvm : (object)DBNull.Value);
            cmd.Parameters.AddWithValue("$crate", g?.CessRateBasisPoints is { } crbp ? crbp : (object)DBNull.Value);
            cmd.Parameters.AddWithValue("$cpu", g?.CessPerUnit is { } cpu ? Paisa.FromMoney(cpu) : (object)DBNull.Value);
            cmd.Parameters.AddWithValue("$crsp", g?.CessRspFactorMillis is { } crsp ? crsp : (object)DBNull.Value);
            cmd.Parameters.AddWithValue("$rsp", g?.RetailSalePrice is { } rsp ? Paisa.FromMoney(rsp) : (object)DBNull.Value);

            // v39 item reverse-charge (RCM) flags (default off/null for a plain item).
            cmd.Parameters.AddWithValue("$rca", g is { ReverseChargeApplicable: true } ? 1 : 0);
            cmd.Parameters.AddWithValue("$gtafc", g is { GtaForwardCharge: true } ? 1 : 0);
            cmd.Parameters.AddWithValue("$rcmcat", (object?)g?.RcmCategoryId?.ToString("D") ?? DBNull.Value);

            // v43 item §17(5) ITC-eligibility (default Eligible/None for a plain item).
            cmd.Parameters.AddWithValue("$itcelig", g is null ? 0 : (int)g.ItcEligibility);
            cmd.Parameters.AddWithValue("$blkcat", g is null ? 0 : (int)g.BlockedCreditCategory);

            // v16 batch switches (0/1; default off).
            cmd.Parameters.AddWithValue("$mib", item.MaintainInBatches ? 1 : 0);
            cmd.Parameters.AddWithValue("$tmd", item.TrackManufacturingDate ? 1 : 0);
            cmd.Parameters.AddWithValue("$ued", item.UseExpiryDates ? 1 : 0);
            cmd.Parameters.AddWithValue("$setc", item.SetComponents ? 1 : 0); // v18 Set-Components (RQ-10)
            // v25 (Phase 7 slice 1): the item's default Nature-of-Goods (§206C TCS) id, or NULL.
            cmd.Parameters.AddWithValue("$tcsnat", (object?)item.TcsNatureOfGoodsId?.ToString("D") ?? DBNull.Value);
            cmd.ExecuteNonQuery();
        }
    }

    private void InsertBatchMasters(SqliteTransaction tx, Company c)
    {
        foreach (var b in c.BatchMasters)
        {
            using var cmd = _connection.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = """
                INSERT INTO batch_masters
                    (id, company_id, stock_item_id, batch_no, mfg_date, expiry_date, expiry_period,
                     godown_id, inward_qty_micro, inward_rate_paisa)
                VALUES ($id, $cid, $item, $no, $mfg, $exp, $period, $godown, $qty, $rate);
                """;
            cmd.Parameters.AddWithValue("$id", b.Id.ToString("D"));
            cmd.Parameters.AddWithValue("$cid", c.Id.ToString("D"));
            cmd.Parameters.AddWithValue("$item", b.StockItemId.ToString("D"));
            cmd.Parameters.AddWithValue("$no", b.BatchNumber);
            cmd.Parameters.AddWithValue("$mfg", (object?)(b.ManufacturingDate is { } m ? FormatDate(m) : null) ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$exp", (object?)(b.ExpiryDate is { } e ? FormatDate(e) : null) ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$period", (object?)b.ExpiryPeriod?.RawText ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$godown", (object?)b.GodownId?.ToString("D") ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$qty", b.InwardQuantity is { } q ? QtyMicroFromDecimal(q) : (object)DBNull.Value);
            cmd.Parameters.AddWithValue("$rate", b.InwardRate is { } r ? Paisa.FromMoney(r) : (object)DBNull.Value);
            cmd.ExecuteNonQuery();
        }
    }

    /// <summary>
    /// Persists the Bill-of-Materials masters (Phase 6 slice 2; RQ-9, DP-3) into the v17 <c>bill_of_materials</c>
    /// header + <c>bom_lines</c> child tables. The block size (unit of manufacture) and each line's per-block
    /// quantity are stored as INTEGER micros (× 1,000,000); a carve-out per-unit rate as INTEGER paisa; a
    /// carve-out percent as INTEGER millis (× 1,000) — all exact, no float (ER-3). <see cref="BomLine.LineType"/>
    /// is stored as its enum ordinal (Component=0, ByProduct=1, CoProduct=2, Scrap=3). Line order is preserved.
    /// </summary>
    private void InsertBillsOfMaterials(SqliteTransaction tx, Company c)
    {
        foreach (var bom in c.BillsOfMaterials)
        {
            using (var head = _connection.CreateCommand())
            {
                head.Transaction = tx;
                head.CommandText = """
                    INSERT INTO bill_of_materials (id, company_id, stock_item_id, name, unit_of_manufacture_micro)
                    VALUES ($id, $cid, $item, $name, $uom);
                    """;
                head.Parameters.AddWithValue("$id", bom.Id.ToString("D"));
                head.Parameters.AddWithValue("$cid", c.Id.ToString("D"));
                head.Parameters.AddWithValue("$item", bom.StockItemId.ToString("D"));
                head.Parameters.AddWithValue("$name", bom.Name);
                head.Parameters.AddWithValue("$uom", QtyMicroFromDecimal(bom.UnitOfManufacture));
                head.ExecuteNonQuery();
            }

            var order = 0;
            foreach (var line in bom.Lines)
            {
                using var cmd = _connection.CreateCommand();
                cmd.Transaction = tx;
                cmd.CommandText = """
                    INSERT INTO bom_lines
                        (bom_id, line_order, line_type, component_stock_item_id, godown_id, qty_micro,
                         rate_paisa, percent_millis)
                    VALUES ($bom, $ord, $type, $comp, $godown, $qty, $rate, $pct);
                    """;
                cmd.Parameters.AddWithValue("$bom", bom.Id.ToString("D"));
                cmd.Parameters.AddWithValue("$ord", order++);
                cmd.Parameters.AddWithValue("$type", (int)line.LineType);
                cmd.Parameters.AddWithValue("$comp", line.ComponentStockItemId.ToString("D"));
                cmd.Parameters.AddWithValue("$godown", (object?)line.GodownId?.ToString("D") ?? DBNull.Value);
                cmd.Parameters.AddWithValue("$qty", QtyMicroFromDecimal(line.QuantityPerBlock));
                cmd.Parameters.AddWithValue("$rate", line.Rate is { } rt ? Paisa.FromMoney(rt) : (object)DBNull.Value);
                cmd.Parameters.AddWithValue("$pct",
                    line.PercentOfFinishedGoodCost is { } p ? PercentMillisFromDecimal(p) : (object)DBNull.Value);
                cmd.ExecuteNonQuery();
            }
        }
    }

    /// <summary>
    /// Persists the Reorder Level definitions (Phase 6 slice 6; RQ-32..RQ-35) into <c>reorder_definitions</c>.
    /// Fixed quantities are stored as INTEGER micros (× 1,000,000; NULL = unset), the Simple/Advanced flags as 0/1,
    /// and the shared Advanced period unit/count + Higher/Lower criterion as their enum ordinals (NULL when neither
    /// figure is Advanced) — all exact, no float (ER-3).
    /// </summary>
    private void InsertReorderDefinitions(SqliteTransaction tx, Company c)
    {
        foreach (var d in c.ReorderDefinitions)
        {
            using var cmd = _connection.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = """
                INSERT INTO reorder_definitions
                    (id, company_id, scope, target_id, reorder_advanced, reorder_qty_micro, minqty_advanced,
                     min_order_qty_micro, period_unit, period_count, criteria)
                VALUES ($id, $cid, $scope, $target, $radv, $rqty, $madv, $mqty, $punit, $pcount, $crit);
                """;
            cmd.Parameters.AddWithValue("$id", d.Id.ToString("D"));
            cmd.Parameters.AddWithValue("$cid", c.Id.ToString("D"));
            cmd.Parameters.AddWithValue("$scope", (int)d.Scope);
            cmd.Parameters.AddWithValue("$target", d.TargetId.ToString("D"));
            cmd.Parameters.AddWithValue("$radv", d.ReorderAdvanced ? 1 : 0);
            cmd.Parameters.AddWithValue("$rqty", d.ReorderQuantity is { } rq ? QtyMicroFromDecimal(rq) : (object)DBNull.Value);
            cmd.Parameters.AddWithValue("$madv", d.MinQtyAdvanced ? 1 : 0);
            cmd.Parameters.AddWithValue("$mqty", d.MinOrderQuantity is { } mq ? QtyMicroFromDecimal(mq) : (object)DBNull.Value);
            cmd.Parameters.AddWithValue("$punit", d.PeriodUnit is { } pu ? (int)pu : (object)DBNull.Value);
            cmd.Parameters.AddWithValue("$pcount", d.PeriodCount is { } pc ? pc : (object)DBNull.Value);
            cmd.Parameters.AddWithValue("$crit", d.Criteria is { } cr ? (int)cr : (object)DBNull.Value);
            cmd.ExecuteNonQuery();
        }
    }

    /// <summary>Persists the named Price Levels (Phase 6 slice 5; RQ-26) into <c>price_levels</c>.</summary>
    private void InsertPriceLevels(SqliteTransaction tx, Company c)
    {
        foreach (var level in c.PriceLevels)
        {
            using var cmd = _connection.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = "INSERT INTO price_levels (id, company_id, name) VALUES ($id, $cid, $name);";
            cmd.Parameters.AddWithValue("$id", level.Id.ToString("D"));
            cmd.Parameters.AddWithValue("$cid", c.Id.ToString("D"));
            cmd.Parameters.AddWithValue("$name", level.Name);
            cmd.ExecuteNonQuery();
        }
    }

    /// <summary>
    /// Persists the dated Price List versions (Phase 6 slice 5; RQ-27/RQ-28) into the <c>price_lists</c> header +
    /// <c>price_list_lines</c> child tables. Slab From/To quantities are stored as INTEGER micros (× 1,000,000;
    /// <c>to_qty_micro</c> NULL = open-ended top slab), the rate as INTEGER paisa, and the discount as INTEGER
    /// millis (× 1,000) — all exact, no float (ER-10). Slab order is preserved via <c>line_order</c>.
    /// </summary>
    private void InsertPriceLists(SqliteTransaction tx, Company c)
    {
        foreach (var list in c.PriceLists)
        {
            using (var head = _connection.CreateCommand())
            {
                head.Transaction = tx;
                head.CommandText = """
                    INSERT INTO price_lists (id, company_id, price_level_id, stock_item_id, applicable_from)
                    VALUES ($id, $cid, $level, $item, $from);
                    """;
                head.Parameters.AddWithValue("$id", list.Id.ToString("D"));
                head.Parameters.AddWithValue("$cid", c.Id.ToString("D"));
                head.Parameters.AddWithValue("$level", list.PriceLevelId.ToString("D"));
                head.Parameters.AddWithValue("$item", list.StockItemId.ToString("D"));
                head.Parameters.AddWithValue("$from", FormatDate(list.ApplicableFrom));
                head.ExecuteNonQuery();
            }

            var order = 0;
            foreach (var slab in list.Slabs)
            {
                using var cmd = _connection.CreateCommand();
                cmd.Transaction = tx;
                cmd.CommandText = """
                    INSERT INTO price_list_lines
                        (price_list_id, line_order, from_qty_micro, to_qty_micro, rate_paisa, discount_percent_millis)
                    VALUES ($list, $ord, $from, $to, $rate, $disc);
                    """;
                cmd.Parameters.AddWithValue("$list", list.Id.ToString("D"));
                cmd.Parameters.AddWithValue("$ord", order++);
                cmd.Parameters.AddWithValue("$from", QtyMicroFromDecimal(slab.FromQty));
                cmd.Parameters.AddWithValue("$to", slab.ToQty is { } to ? QtyMicroFromDecimal(to) : (object)DBNull.Value);
                cmd.Parameters.AddWithValue("$rate", Paisa.FromMoney(slab.Rate));
                cmd.Parameters.AddWithValue("$disc", PercentMillisFromDecimal(slab.DiscountPercent));
                cmd.ExecuteNonQuery();
            }
        }
    }

    private void InsertStockOpeningBalances(SqliteTransaction tx, Company c)
    {
        foreach (var b in c.StockOpeningBalances)
        {
            using var cmd = _connection.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = """
                INSERT INTO stock_opening_balances
                    (id, company_id, stock_item_id, godown_id, batch_label, quantity_micro, rate_paisa,
                     mfg_date, expiry_date)
                VALUES ($id, $cid, $item, $godown, $batch, $qty, $rate, $mfg, $exp);
                """;
            cmd.Parameters.AddWithValue("$id", b.Id.ToString("D"));
            cmd.Parameters.AddWithValue("$cid", c.Id.ToString("D"));
            cmd.Parameters.AddWithValue("$item", b.StockItemId.ToString("D"));
            cmd.Parameters.AddWithValue("$godown", b.GodownId.ToString("D"));
            cmd.Parameters.AddWithValue("$batch", (object?)b.BatchLabel ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$qty", QtyMicroFromDecimal(b.Quantity));
            cmd.Parameters.AddWithValue("$rate", Paisa.FromMoney(b.Rate));
            cmd.Parameters.AddWithValue("$mfg", (object?)(b.ManufacturingDate is { } m ? FormatDate(m) : null) ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$exp", (object?)(b.ExpiryDate is { } e ? FormatDate(e) : null) ?? DBNull.Value);
            cmd.ExecuteNonQuery();
        }
    }

    private void InsertInventoryVouchers(SqliteTransaction tx, Company c)
    {
        foreach (var v in c.InventoryVouchers)
        {
            using (var cmd = _connection.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = """
                    INSERT INTO inventory_vouchers
                        (id, company_id, type_id, number, date, narration, party_id, cancelled, post_dated)
                    VALUES ($id, $cid, $tid, $num, $date, $narr, $party, $cancel, $pd);
                    """;
                cmd.Parameters.AddWithValue("$id", v.Id.ToString("D"));
                cmd.Parameters.AddWithValue("$cid", c.Id.ToString("D"));
                cmd.Parameters.AddWithValue("$tid", v.TypeId.ToString("D"));
                cmd.Parameters.AddWithValue("$num", v.Number);
                cmd.Parameters.AddWithValue("$date", FormatDate(v.Date));
                cmd.Parameters.AddWithValue("$narr", (object?)v.Narration ?? DBNull.Value);
                cmd.Parameters.AddWithValue("$party", (object?)v.PartyId?.ToString("D") ?? DBNull.Value);
                cmd.Parameters.AddWithValue("$cancel", v.Cancelled ? 1 : 0);
                cmd.Parameters.AddWithValue("$pd", v.PostDated ? 1 : 0);
                cmd.ExecuteNonQuery();
            }

            var order = 0;
            foreach (var a in v.Allocations)
                InsertInventoryAllocation(tx, v.Id, a, role: 0, order++);
            foreach (var a in v.DestinationAllocations)
                InsertInventoryAllocation(tx, v.Id, a, role: 1, order++);

            order = 0;
            foreach (var o in v.OrderLines)
            {
                using var cmd = _connection.CreateCommand();
                cmd.Transaction = tx;
                cmd.CommandText = """
                    INSERT INTO order_lines
                        (inventory_voucher_id, line_order, stock_item_id, godown_id, quantity_micro, rate_paisa)
                    VALUES ($vid, $ord, $item, $godown, $qty, $rate);
                    """;
                cmd.Parameters.AddWithValue("$vid", v.Id.ToString("D"));
                cmd.Parameters.AddWithValue("$ord", order++);
                cmd.Parameters.AddWithValue("$item", o.StockItemId.ToString("D"));
                cmd.Parameters.AddWithValue("$godown", o.GodownId.ToString("D"));
                cmd.Parameters.AddWithValue("$qty", QtyMicroFromDecimal(o.Quantity));
                cmd.Parameters.AddWithValue("$rate", o.Rate is { } r ? Paisa.FromMoney(r) : (object)DBNull.Value);
                cmd.ExecuteNonQuery();
            }

            order = 0;
            foreach (var pl in v.PhysicalLines)
            {
                using var cmd = _connection.CreateCommand();
                cmd.Transaction = tx;
                cmd.CommandText = """
                    INSERT INTO physical_stock_lines
                        (inventory_voucher_id, line_order, stock_item_id, godown_id, counted_qty_micro, batch_label)
                    VALUES ($vid, $ord, $item, $godown, $qty, $batch);
                    """;
                cmd.Parameters.AddWithValue("$vid", v.Id.ToString("D"));
                cmd.Parameters.AddWithValue("$ord", order++);
                cmd.Parameters.AddWithValue("$item", pl.StockItemId.ToString("D"));
                cmd.Parameters.AddWithValue("$godown", pl.GodownId.ToString("D"));
                cmd.Parameters.AddWithValue("$qty", QtyMicroFromDecimal(pl.CountedQuantity));
                cmd.Parameters.AddWithValue("$batch", (object?)pl.BatchLabel ?? DBNull.Value);
                cmd.ExecuteNonQuery();
            }

            // v19 (RQ-20): additional-cost lines on a Stock-Journal transfer (empty for every other voucher).
            order = 0;
            foreach (var acl in v.AdditionalCostLines)
            {
                using var cmd = _connection.CreateCommand();
                cmd.Transaction = tx;
                cmd.CommandText = """
                    INSERT INTO additional_cost_lines
                        (inventory_voucher_id, line_order, ledger_id, amount_paisa)
                    VALUES ($vid, $ord, $ledger, $amt);
                    """;
                cmd.Parameters.AddWithValue("$vid", v.Id.ToString("D"));
                cmd.Parameters.AddWithValue("$ord", order++);
                cmd.Parameters.AddWithValue("$ledger", acl.LedgerId.ToString("D"));
                cmd.Parameters.AddWithValue("$amt", Paisa.FromMoney(acl.Amount));
                cmd.ExecuteNonQuery();
            }

            // v24 (RQ-47): the Job Work Order payload (empty for every non-order voucher). job_work_orders.id is
            // populated with the SAME value as the voucher id, so a material_order_links.job_work_order_id equals the
            // order voucher's id (matching the domain's order-link Guids).
            if (v.JobWorkOrder is { } jwo)
            {
                using (var cmd = _connection.CreateCommand())
                {
                    cmd.Transaction = tx;
                    cmd.CommandText = """
                        INSERT INTO job_work_orders
                            (id, company_id, inventory_voucher_id, direction, order_no, duration_of_process,
                             nature_of_processing, fg_stock_item_id, fg_qty_micro, fg_due_date, fg_godown_id,
                             fg_rate_paisa, tracking_components, fill_components_bom_id)
                        VALUES ($id, $cid, $vid, $dir, $ono, $dur, $nat, $fg, $fgq, $fgd, $fgg, $fgr, $tc, $bom);
                        """;
                    cmd.Parameters.AddWithValue("$id", v.Id.ToString("D"));
                    cmd.Parameters.AddWithValue("$cid", c.Id.ToString("D"));
                    cmd.Parameters.AddWithValue("$vid", v.Id.ToString("D"));
                    cmd.Parameters.AddWithValue("$dir", (int)jwo.Direction);
                    cmd.Parameters.AddWithValue("$ono", jwo.OrderNo);
                    cmd.Parameters.AddWithValue("$dur", (object?)jwo.DurationOfProcess ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("$nat", (object?)jwo.NatureOfProcessing ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("$fg", jwo.FinishedGoodStockItemId.ToString("D"));
                    cmd.Parameters.AddWithValue("$fgq", QtyMicroFromDecimal(jwo.FinishedGoodQuantity));
                    cmd.Parameters.AddWithValue("$fgd", jwo.FinishedGoodDueDate is { } dd ? FormatDate(dd) : (object)DBNull.Value);
                    cmd.Parameters.AddWithValue("$fgg", (object?)jwo.FinishedGoodGodownId?.ToString("D") ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("$fgr", jwo.FinishedGoodRate is { } fr ? Paisa.FromMoney(fr) : (object)DBNull.Value);
                    cmd.Parameters.AddWithValue("$tc", jwo.TrackingComponents ? 1 : 0);
                    cmd.Parameters.AddWithValue("$bom", (object?)jwo.FillComponentsBomId?.ToString("D") ?? DBNull.Value);
                    cmd.ExecuteNonQuery();
                }

                var lineOrder = 0;
                foreach (var line in jwo.Lines)
                {
                    using var cmd = _connection.CreateCommand();
                    cmd.Transaction = tx;
                    cmd.CommandText = """
                        INSERT INTO job_work_order_lines
                            (job_work_order_id, line_order, component_stock_item_id, track, due_date, godown_id,
                             qty_micro, rate_paisa)
                        VALUES ($jwo, $ord, $item, $track, $due, $godown, $qty, $rate);
                        """;
                    cmd.Parameters.AddWithValue("$jwo", v.Id.ToString("D"));
                    cmd.Parameters.AddWithValue("$ord", lineOrder++);
                    cmd.Parameters.AddWithValue("$item", line.ComponentStockItemId.ToString("D"));
                    cmd.Parameters.AddWithValue("$track", (int)line.Track);
                    cmd.Parameters.AddWithValue("$due", line.DueDate is { } d ? FormatDate(d) : (object)DBNull.Value);
                    cmd.Parameters.AddWithValue("$godown", (object?)line.GodownId?.ToString("D") ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("$qty", QtyMicroFromDecimal(line.Quantity));
                    cmd.Parameters.AddWithValue("$rate", line.Rate is { } r ? Paisa.FromMoney(r) : (object)DBNull.Value);
                    cmd.ExecuteNonQuery();
                }
            }

            // v24 (RQ-48): material→order links (empty for every non-material voucher).
            foreach (var linkId in v.OrderLinks)
            {
                using var cmd = _connection.CreateCommand();
                cmd.Transaction = tx;
                cmd.CommandText = """
                    INSERT INTO material_order_links (material_voucher_id, job_work_order_id)
                    VALUES ($vid, $jwo);
                    """;
                cmd.Parameters.AddWithValue("$vid", v.Id.ToString("D"));
                cmd.Parameters.AddWithValue("$jwo", linkId.ToString("D"));
                cmd.ExecuteNonQuery();
            }
        }
    }

    private void InsertInventoryAllocation(SqliteTransaction tx, Guid voucherId, InventoryAllocation a, int role, int order)
    {
        using var cmd = _connection.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = """
            INSERT INTO inventory_allocations
                (inventory_voucher_id, line_order, role, stock_item_id, godown_id, unit_id,
                 quantity_micro, direction, rate_paisa, batch_label, actual_qty_micro, billed_qty_micro)
            VALUES ($vid, $ord, $role, $item, $godown, $unit, $qty, $dir, $rate, $batch, NULL, NULL);
            """;
        cmd.Parameters.AddWithValue("$vid", voucherId.ToString("D"));
        cmd.Parameters.AddWithValue("$ord", order);
        cmd.Parameters.AddWithValue("$role", role);
        cmd.Parameters.AddWithValue("$item", a.StockItemId.ToString("D"));
        cmd.Parameters.AddWithValue("$godown", a.GodownId.ToString("D"));
        cmd.Parameters.AddWithValue("$unit", (object?)a.UnitId?.ToString("D") ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$qty", QtyMicroFromDecimal(a.Quantity));
        cmd.Parameters.AddWithValue("$dir", (int)a.Direction);
        cmd.Parameters.AddWithValue("$rate", a.Rate is { } r ? Paisa.FromMoney(r) : (object)DBNull.Value);
        cmd.Parameters.AddWithValue("$batch", (object?)a.BatchLabel ?? DBNull.Value);
        cmd.ExecuteNonQuery();
    }

    private void InsertBudgets(SqliteTransaction tx, Company c)
    {
        foreach (var budget in c.Budgets)
        {
            using (var cmd = _connection.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = """
                    INSERT INTO budgets (id, company_id, name, under_id, period_from, period_to)
                    VALUES ($id, $cid, $name, $under, $from, $to);
                    """;
                cmd.Parameters.AddWithValue("$id", budget.Id.ToString("D"));
                cmd.Parameters.AddWithValue("$cid", c.Id.ToString("D"));
                cmd.Parameters.AddWithValue("$name", budget.Name);
                cmd.Parameters.AddWithValue("$under", (object?)budget.UnderId?.ToString("D") ?? DBNull.Value);
                cmd.Parameters.AddWithValue("$from", FormatDate(budget.PeriodFrom));
                cmd.Parameters.AddWithValue("$to", FormatDate(budget.PeriodTo));
                cmd.ExecuteNonQuery();
            }

            var order = 0;
            foreach (var line in budget.Lines)
            {
                using var cmd = _connection.CreateCommand();
                cmd.Transaction = tx;
                cmd.CommandText = """
                    INSERT INTO budget_lines
                        (budget_id, line_order, group_id, ledger_id, budget_type, amount_paisa)
                    VALUES ($bid, $ord, $gid, $lid, $type, $amt);
                    """;
                cmd.Parameters.AddWithValue("$bid", budget.Id.ToString("D"));
                cmd.Parameters.AddWithValue("$ord", order++);
                cmd.Parameters.AddWithValue("$gid", (object?)line.GroupId?.ToString("D") ?? DBNull.Value);
                cmd.Parameters.AddWithValue("$lid", (object?)line.LedgerId?.ToString("D") ?? DBNull.Value);
                cmd.Parameters.AddWithValue("$type", (int)line.Type);
                cmd.Parameters.AddWithValue("$amt", Paisa.FromMoney(line.Amount));
                cmd.ExecuteNonQuery();
            }
        }
    }

    private void InsertScenarios(SqliteTransaction tx, Company c)
    {
        foreach (var scenario in c.Scenarios)
        {
            using (var cmd = _connection.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = """
                    INSERT INTO scenarios (id, company_id, name, include_actuals)
                    VALUES ($id, $cid, $name, $ia);
                    """;
                cmd.Parameters.AddWithValue("$id", scenario.Id.ToString("D"));
                cmd.Parameters.AddWithValue("$cid", c.Id.ToString("D"));
                cmd.Parameters.AddWithValue("$name", scenario.Name);
                cmd.Parameters.AddWithValue("$ia", scenario.IncludeActuals ? 1 : 0);
                cmd.ExecuteNonQuery();
            }

            foreach (var typeId in scenario.IncludedTypeIds)
                InsertScenarioVoucherType(tx, scenario.Id, typeId, isIncluded: true);
            foreach (var typeId in scenario.ExcludedTypeIds)
                InsertScenarioVoucherType(tx, scenario.Id, typeId, isIncluded: false);
        }
    }

    private void InsertScenarioVoucherType(SqliteTransaction tx, Guid scenarioId, Guid voucherTypeId, bool isIncluded)
    {
        using var cmd = _connection.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = """
            INSERT INTO scenario_voucher_types (scenario_id, voucher_type_id, is_included)
            VALUES ($sid, $vtid, $inc);
            """;
        cmd.Parameters.AddWithValue("$sid", scenarioId.ToString("D"));
        cmd.Parameters.AddWithValue("$vtid", voucherTypeId.ToString("D"));
        cmd.Parameters.AddWithValue("$inc", isIncluded ? 1 : 0);
        cmd.ExecuteNonQuery();
    }

    private void InsertVouchers(SqliteTransaction tx, Company c)
    {
        foreach (var v in c.Vouchers)
            InsertVoucher(tx, c.Id, v);
    }

    private void InsertVoucher(SqliteTransaction tx, Guid companyId, Voucher v)
    {
        using (var cmd = _connection.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = """
                INSERT INTO vouchers
                    (id, company_id, type_id, number, date, narration, party_id, cancelled, optional, post_dated,
                     applicable_upto)
                VALUES ($id, $cid, $tid, $num, $date, $narr, $party, $cancel, $opt, $pd, $au);
                """;
            cmd.Parameters.AddWithValue("$id", v.Id.ToString("D"));
            cmd.Parameters.AddWithValue("$cid", companyId.ToString("D"));
            cmd.Parameters.AddWithValue("$tid", v.TypeId.ToString("D"));
            cmd.Parameters.AddWithValue("$num", v.Number);
            cmd.Parameters.AddWithValue("$date", FormatDate(v.Date));
            cmd.Parameters.AddWithValue("$narr", (object?)v.Narration ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$party", (object?)v.PartyId?.ToString("D") ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$cancel", v.Cancelled ? 1 : 0);
            cmd.Parameters.AddWithValue("$opt", v.Optional ? 1 : 0);
            cmd.Parameters.AddWithValue("$pd", v.PostDated ? 1 : 0);
            cmd.Parameters.AddWithValue("$au", (object?)(v.ApplicableUpto is { } au ? FormatDate(au) : null) ?? DBNull.Value);
            cmd.ExecuteNonQuery();
        }

        var order = 0;
        foreach (var line in v.Lines)
        {
            long lineId;
            using (var cmd = _connection.CreateCommand())
            {
                cmd.Transaction = tx;
                // RETURNING id gives us the AUTOINCREMENT key to hang bill allocations off (SQLite ≥ 3.35).
                cmd.CommandText = """
                    INSERT INTO entry_lines
                        (voucher_id, line_order, ledger_id, amount_paisa, side,
                         forex_currency_id, forex_amount_micro, forex_rate_micro,
                         gst_tax_head, gst_rate_bp, gst_taxable_value_paisa,
                         gst_is_reverse_charge, gst_rcm_scheme, gst_adjustment_kind)
                    VALUES ($vid, $ord, $lid, $amt, $side, $fxcur, $fxamt, $fxrate, $gthead, $grate, $gtax,
                            $grcm, $grcmsch, $gadj)
                    RETURNING id;
                    """;
                cmd.Parameters.AddWithValue("$vid", v.Id.ToString("D"));
                cmd.Parameters.AddWithValue("$ord", order++);
                cmd.Parameters.AddWithValue("$lid", line.LedgerId.ToString("D"));
                cmd.Parameters.AddWithValue("$amt", Paisa.FromMoney(line.Amount));
                cmd.Parameters.AddWithValue("$side", (int)line.Side);

                // v8 multi-currency: forex detail, all NULL for a base-currency line.
                var fx = line.Forex;
                cmd.Parameters.AddWithValue("$fxcur", (object?)fx?.CurrencyId.ToString("D") ?? DBNull.Value);
                cmd.Parameters.AddWithValue("$fxamt", fx is null ? (object)DBNull.Value : MicroFromDecimal(fx.ForexAmount.Amount));
                cmd.Parameters.AddWithValue("$fxrate", fx is null ? (object)DBNull.Value : MicroFromDecimal(fx.Rate));

                // v13 GST tax-line detail: all NULL for a non-tax line.
                var gt = line.Gst;
                cmd.Parameters.AddWithValue("$gthead", gt is null ? (object)DBNull.Value : (int)gt.TaxHead);
                cmd.Parameters.AddWithValue("$grate", gt is null ? (object)DBNull.Value : gt.RateBasisPoints);
                cmd.Parameters.AddWithValue("$gtax", gt is null ? (object)DBNull.Value : Paisa.FromMoney(gt.TaxableValue));
                // v39 (Phase 9 slice 2): reverse-charge tag (default 0/NULL for a forward-charge line).
                cmd.Parameters.AddWithValue("$grcm", gt is { IsReverseCharge: true } ? 1 : 0);
                cmd.Parameters.AddWithValue("$grcmsch", gt?.RcmScheme is { } sch ? (int)sch : (object)DBNull.Value);
                // v44 (Phase 9 slice 7): the set-off / cash-payment / reversal adjustment tag (NULL for a forward line).
                cmd.Parameters.AddWithValue("$gadj", gt?.Adjustment is { } adj ? (int)adj : (object)DBNull.Value);
                lineId = Convert.ToInt64(cmd.ExecuteScalar());
            }

            InsertBillAllocations(tx, lineId, line);
            InsertCostAllocations(tx, lineId, line);
            InsertBankAllocation(tx, lineId, line);
            InsertTdsLine(tx, lineId, line);
            InsertTcsLine(tx, lineId, line);
            InsertPayrollLine(tx, lineId, line);
        }

        InsertVoucherInventoryLines(tx, v);
        InsertPosTenderAllocations(tx, v);
    }

    /// <summary>v32: persists an entry line's payroll detail (catalog §14; Phase 8 slice 3) to <c>payroll_lines</c>.
    /// A non-payroll line carries none, so nothing is written — byte-identical (ER-13). Money is stored as
    /// rupees × 1,000,000 ("micros"), exact for a paisa-exact amount.</summary>
    private void InsertPayrollLine(SqliteTransaction tx, long entryLineId, EntryLine line)
    {
        if (line.Payroll is not { } p) return;
        using var cmd = _connection.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = """
            INSERT INTO payroll_lines
                (entry_line_id, employee_id, pay_head_id, category, amount_micro)
            VALUES ($lid, $emp, $ph, $cat, $amt);
            """;
        cmd.Parameters.AddWithValue("$lid", entryLineId);
        cmd.Parameters.AddWithValue("$emp", p.EmployeeId.ToString("D"));
        cmd.Parameters.AddWithValue("$ph", (object?)p.PayHeadId?.ToString("D") ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$cat", (int)p.Category);
        cmd.Parameters.AddWithValue("$amt", (long)(p.Amount.Amount * 1_000_000m));
        cmd.ExecuteNonQuery();
    }

    /// <summary>v26: persists an entry line's TDS withholding detail (catalog §13; Phase 7 slice 2) to
    /// <c>tds_lines</c>. A non-TDS line carries none, so nothing is written — byte-identical (ER-13). Money is
    /// stored as rupees × 1,000,000 ("micros"), exact for a paisa-exact amount.</summary>
    private void InsertTdsLine(SqliteTransaction tx, long entryLineId, EntryLine line)
    {
        if (line.Tds is not { } t) return;
        using var cmd = _connection.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = """
            INSERT INTO tds_lines
                (entry_line_id, nature_id, section_code, assessable_value_micro, rate_bp,
                 tds_amount_micro, deductee_ledger_id, pan_applied)
            VALUES ($lid, $nat, $sec, $asv, $rate, $tds, $ded, $pan);
            """;
        cmd.Parameters.AddWithValue("$lid", entryLineId);
        cmd.Parameters.AddWithValue("$nat", t.NatureId.ToString("D"));
        cmd.Parameters.AddWithValue("$sec", t.SectionCode);
        cmd.Parameters.AddWithValue("$asv", (long)(t.AssessableValue.Amount * 1_000_000m));
        cmd.Parameters.AddWithValue("$rate", t.RateBasisPoints);
        cmd.Parameters.AddWithValue("$tds", (long)(t.TdsAmount.Amount * 1_000_000m));
        cmd.Parameters.AddWithValue("$ded", t.DeducteeLedgerId.ToString("D"));
        cmd.Parameters.AddWithValue("$pan", t.PanApplied ? 1 : 0);
        cmd.ExecuteNonQuery();
    }

    /// <summary>v28: persists an entry line's TCS collection detail (catalog §13; Phase 7 slice 5) to
    /// <c>tcs_lines</c>. TCS is additive (the mirror of GST). A non-TCS line carries none, so nothing is written —
    /// byte-identical (ER-13). Money is stored as rupees × 1,000,000 ("micros"), exact for a paisa-exact amount.</summary>
    private void InsertTcsLine(SqliteTransaction tx, long entryLineId, EntryLine line)
    {
        if (line.Tcs is not { } t) return;
        using var cmd = _connection.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = """
            INSERT INTO tcs_lines
                (entry_line_id, nature_id, collection_code, assessable_value_micro, rate_bp,
                 tcs_amount_micro, collectee_ledger_id, pan_applied)
            VALUES ($lid, $nat, $code, $asv, $rate, $tcs, $col, $pan);
            """;
        cmd.Parameters.AddWithValue("$lid", entryLineId);
        cmd.Parameters.AddWithValue("$nat", t.NatureId.ToString("D"));
        cmd.Parameters.AddWithValue("$code", t.CollectionCode);
        cmd.Parameters.AddWithValue("$asv", (long)(t.AssessableValue.Amount * 1_000_000m));
        cmd.Parameters.AddWithValue("$rate", t.RateBasisPoints);
        cmd.Parameters.AddWithValue("$tcs", (long)(t.TcsAmount.Amount * 1_000_000m));
        cmd.Parameters.AddWithValue("$col", t.CollecteeLedgerId.ToString("D"));
        cmd.Parameters.AddWithValue("$pan", t.PanApplied ? 1 : 0);
        cmd.ExecuteNonQuery();
    }

    /// <summary>Persists a POS voucher's tender rows (v23; RQ-39/RQ-40; DP-6) to <c>pos_tender_allocations</c>. A
    /// non-POS voucher carries no tenders, so nothing is written — byte-identical (ER-13). amount_paisa is the
    /// POSTED payable share (Cash = residual, not tendered); tendered/change are Cash-only.</summary>
    private void InsertPosTenderAllocations(SqliteTransaction tx, Voucher v)
    {
        var order = 0;
        foreach (var t in v.PosTenders)
        {
            using var cmd = _connection.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = """
                INSERT INTO pos_tender_allocations
                    (voucher_id, tender_order, tender_type, ledger_id, amount_paisa, tendered_paisa, change_paisa,
                     card_no, bank_name, cheque_no)
                VALUES ($vid, $ord, $tt, $lid, $amt, $tend, $chg, $card, $bank, $chq);
                """;
            cmd.Parameters.AddWithValue("$vid", v.Id.ToString("D"));
            cmd.Parameters.AddWithValue("$ord", order++);
            cmd.Parameters.AddWithValue("$tt", (int)t.Type);
            cmd.Parameters.AddWithValue("$lid", t.LedgerId.ToString("D"));
            cmd.Parameters.AddWithValue("$amt", Paisa.FromMoney(t.Amount));
            cmd.Parameters.AddWithValue("$tend", t.Tendered is { } td ? Paisa.FromMoney(td) : (object)DBNull.Value);
            cmd.Parameters.AddWithValue("$chg", t.Change is { } ch ? Paisa.FromMoney(ch) : (object)DBNull.Value);
            cmd.Parameters.AddWithValue("$card", (object?)t.CardNo ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$bank", (object?)t.BankName ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$chq", (object?)t.ChequeNo ?? DBNull.Value);
            cmd.ExecuteNonQuery();
        }
    }

    private List<PosTender> ReadPosTenders(Guid voucherId)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            SELECT tender_type, ledger_id, amount_paisa, tendered_paisa, change_paisa, card_no, bank_name, cheque_no
            FROM pos_tender_allocations WHERE voucher_id = $vid ORDER BY tender_order, id;
            """;
        cmd.Parameters.AddWithValue("$vid", voucherId.ToString("D"));
        using var r = cmd.ExecuteReader();
        var list = new List<PosTender>();
        while (r.Read())
        {
            list.Add(new PosTender(
                (PosTenderType)(int)r.GetInt64(0),
                Guid.Parse(r.GetString(1)),
                Paisa.ToMoney(r.GetInt64(2)),
                Tendered: r.IsDBNull(3) ? (Money?)null : Paisa.ToMoney(r.GetInt64(3)),
                Change: r.IsDBNull(4) ? (Money?)null : Paisa.ToMoney(r.GetInt64(4)),
                CardNo: r.IsDBNull(5) ? null : r.GetString(5),
                BankName: r.IsDBNull(6) ? null : r.GetString(6),
                ChequeNo: r.IsDBNull(7) ? null : r.GetString(7)));
        }
        return list;
    }

    private void InsertVoucherInventoryLines(SqliteTransaction tx, Voucher v)
    {
        var order = 0;
        foreach (var line in v.InventoryLines)
        {
            using var cmd = _connection.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = """
                INSERT INTO voucher_inventory_lines
                    (voucher_id, line_order, stock_item_id, godown_id, quantity_micro, direction, rate_paisa, batch_label,
                     actual_qty_micro, billed_qty_micro)
                VALUES ($vid, $ord, $item, $godown, $qty, $dir, $rate, $batch, $aqty, $bqty);
                """;
            cmd.Parameters.AddWithValue("$vid", v.Id.ToString("D"));
            cmd.Parameters.AddWithValue("$ord", order++);
            cmd.Parameters.AddWithValue("$item", line.StockItemId.ToString("D"));
            cmd.Parameters.AddWithValue("$godown", line.GodownId.ToString("D"));
            cmd.Parameters.AddWithValue("$qty", QtyMicroFromDecimal(line.Quantity));
            cmd.Parameters.AddWithValue("$dir", (int)line.Direction);
            cmd.Parameters.AddWithValue("$rate", Paisa.FromMoney(line.Rate));
            cmd.Parameters.AddWithValue("$batch", (object?)line.BatchLabel ?? DBNull.Value);
            // v20 (RQ-22/RQ-23; DP-7): persist the Actual/Billed split only when it is active (Billed ≠ Actual);
            // otherwise both columns stay NULL so quantity_micro alone drives Billed ≡ Actual (byte-identical, ER-13).
            var split = line.BilledQuantity != line.Quantity;
            cmd.Parameters.AddWithValue("$aqty", split ? QtyMicroFromDecimal(line.Quantity) : (object)DBNull.Value);
            cmd.Parameters.AddWithValue("$bqty", split ? QtyMicroFromDecimal(line.BilledQuantity) : (object)DBNull.Value);
            cmd.ExecuteNonQuery();
        }
    }

    private void InsertBillAllocations(SqliteTransaction tx, long entryLineId, EntryLine line)
    {
        var allocOrder = 0;
        foreach (var a in line.BillAllocations)
        {
            using var cmd = _connection.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = """
                INSERT INTO bill_allocations
                    (entry_line_id, alloc_order, ref_type, name, amount_paisa, due_date, credit_days)
                VALUES ($lid, $ord, $rt, $name, $amt, $due, $cd);
                """;
            cmd.Parameters.AddWithValue("$lid", entryLineId);
            cmd.Parameters.AddWithValue("$ord", allocOrder++);
            cmd.Parameters.AddWithValue("$rt", (int)a.RefType);
            cmd.Parameters.AddWithValue("$name", a.Name);
            cmd.Parameters.AddWithValue("$amt", Paisa.FromMoney(a.Amount));
            cmd.Parameters.AddWithValue("$due", (object?)(a.DueDate is { } d ? FormatDate(d) : null) ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$cd", (object?)a.CreditPeriodDays ?? DBNull.Value);
            cmd.ExecuteNonQuery();
        }
    }

    private void InsertCostAllocations(SqliteTransaction tx, long entryLineId, EntryLine line)
    {
        var allocOrder = 0;
        foreach (var a in line.CostAllocations)
        {
            using var cmd = _connection.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = """
                INSERT INTO cost_allocations
                    (entry_line_id, alloc_order, category_id, centre_id, amount_paisa)
                VALUES ($lid, $ord, $cat, $centre, $amt);
                """;
            cmd.Parameters.AddWithValue("$lid", entryLineId);
            cmd.Parameters.AddWithValue("$ord", allocOrder++);
            cmd.Parameters.AddWithValue("$cat", a.CategoryId.ToString("D"));
            cmd.Parameters.AddWithValue("$centre", a.CentreId.ToString("D"));
            cmd.Parameters.AddWithValue("$amt", Paisa.FromMoney(a.Amount));
            cmd.ExecuteNonQuery();
        }
    }

    private void InsertBankAllocation(SqliteTransaction tx, long entryLineId, EntryLine line)
    {
        var a = line.BankAllocation;
        if (a is null) return;

        using var cmd = _connection.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = """
            INSERT INTO bank_allocations
                (entry_line_id, transaction_type, instrument_number, instrument_date, bank_date)
            VALUES ($lid, $tt, $inum, $idate, $bdate);
            """;
        cmd.Parameters.AddWithValue("$lid", entryLineId);
        cmd.Parameters.AddWithValue("$tt", (int)a.TransactionType);
        cmd.Parameters.AddWithValue("$inum", a.InstrumentNumber);
        cmd.Parameters.AddWithValue("$idate", (object?)(a.InstrumentDate is { } id ? FormatDate(id) : null) ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$bdate", (object?)(a.BankDate is { } bd ? FormatDate(bd) : null) ?? DBNull.Value);
        cmd.ExecuteNonQuery();
    }

    // ------------------------------------------------------------------ ISavedReportViewRepository

    /// <inheritdoc />
    public void Save(Guid companyId, string name, SavedReportView view)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("A saved-view name is required.", nameof(name));
        ArgumentNullException.ThrowIfNull(view);

        // Config-only JSON: the report is always recomputed on apply, so no figure is ever persisted (ER-9).
        var json = view.ToJson();

        using var tx = _connection.BeginTransaction();
        // Upsert by (company, name), case-insensitive: overwrite the existing row of that name, else insert.
        // Delete-then-insert keeps a stable single row and a fresh id only when creating.
        using (var find = _connection.CreateCommand())
        {
            find.Transaction = tx;
            find.CommandText =
                "SELECT id FROM saved_views WHERE company_id = $cid AND name = $name COLLATE NOCASE LIMIT 1;";
            find.Parameters.AddWithValue("$cid", companyId.ToString("D"));
            find.Parameters.AddWithValue("$name", name);
            var existingId = find.ExecuteScalar() as string;

            if (existingId is not null)
            {
                using var upd = _connection.CreateCommand();
                upd.Transaction = tx;
                upd.CommandText = "UPDATE saved_views SET name = $name, config_json = $json WHERE id = $id;";
                upd.Parameters.AddWithValue("$name", name);
                upd.Parameters.AddWithValue("$json", json);
                upd.Parameters.AddWithValue("$id", existingId);
                upd.ExecuteNonQuery();
            }
            else
            {
                using var ins = _connection.CreateCommand();
                ins.Transaction = tx;
                ins.CommandText =
                    "INSERT INTO saved_views (id, company_id, name, config_json) VALUES ($id, $cid, $name, $json);";
                ins.Parameters.AddWithValue("$id", Guid.NewGuid().ToString("D"));
                ins.Parameters.AddWithValue("$cid", companyId.ToString("D"));
                ins.Parameters.AddWithValue("$name", name);
                ins.Parameters.AddWithValue("$json", json);
                ins.ExecuteNonQuery();
            }
        }
        tx.Commit();
    }

    /// <inheritdoc />
    public IReadOnlyList<SavedReportViewEntry> List(Guid companyId)
    {
        var entries = new List<SavedReportViewEntry>();
        using var cmd = _connection.CreateCommand();
        cmd.CommandText =
            "SELECT name, config_json FROM saved_views WHERE company_id = $cid ORDER BY name COLLATE NOCASE;";
        cmd.Parameters.AddWithValue("$cid", companyId.ToString("D"));
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            entries.Add(new SavedReportViewEntry(reader.GetString(0), SavedReportView.FromJson(reader.GetString(1))));
        return entries;
    }

    /// <inheritdoc />
    public SavedReportView? Get(Guid companyId, string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        using var cmd = _connection.CreateCommand();
        cmd.CommandText =
            "SELECT config_json FROM saved_views WHERE company_id = $cid AND name = $name COLLATE NOCASE LIMIT 1;";
        cmd.Parameters.AddWithValue("$cid", companyId.ToString("D"));
        cmd.Parameters.AddWithValue("$name", name);
        return cmd.ExecuteScalar() is string json ? SavedReportView.FromJson(json) : null;
    }

    /// <inheritdoc />
    public void Delete(Guid companyId, string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return;
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "DELETE FROM saved_views WHERE company_id = $cid AND name = $name COLLATE NOCASE;";
        cmd.Parameters.AddWithValue("$cid", companyId.ToString("D"));
        cmd.Parameters.AddWithValue("$name", name);
        cmd.ExecuteNonQuery();
    }

    // ------------------------------------------------------------------ ISmtpProfileRepository

    // The port's Save/Get/Delete are implemented EXPLICITLY, forwarding to these descriptively-named public
    // methods so the store's own API reads clearly (the plain Save/Get/Delete names collide with the other
    // repositories on this class, e.g. Save(Company) and Get(company, viewName)).

    /// <inheritdoc cref="ISmtpProfileRepository.Save" />
    public void SaveSmtpProfile(Guid companyId, SmtpProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);

        // One row per company (company_id is the PRIMARY KEY): upsert by delete-then-insert in one transaction.
        // No password/secret is ever written — the row carries only host/port/TLS/from (R13).
        using var tx = _connection.BeginTransaction();
        using (var del = _connection.CreateCommand())
        {
            del.Transaction = tx;
            del.CommandText = "DELETE FROM smtp_profile WHERE company_id = $cid;";
            del.Parameters.AddWithValue("$cid", companyId.ToString("D"));
            del.ExecuteNonQuery();
        }
        using (var ins = _connection.CreateCommand())
        {
            ins.Transaction = tx;
            ins.CommandText = """
                INSERT INTO smtp_profile (company_id, host, port, use_tls, from_address, from_name)
                VALUES ($cid, $host, $port, $tls, $from, $fromName);
                """;
            ins.Parameters.AddWithValue("$cid", companyId.ToString("D"));
            ins.Parameters.AddWithValue("$host", profile.Host);
            ins.Parameters.AddWithValue("$port", profile.Port);
            ins.Parameters.AddWithValue("$tls", profile.UseTls ? 1 : 0);
            ins.Parameters.AddWithValue("$from", profile.FromAddress);
            ins.Parameters.AddWithValue("$fromName", (object?)profile.FromName ?? DBNull.Value);
            ins.ExecuteNonQuery();
        }
        tx.Commit();
    }

    /// <inheritdoc cref="ISmtpProfileRepository.Get" />
    public SmtpProfile? GetSmtpProfile(Guid companyId)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            SELECT host, port, use_tls, from_address, from_name
            FROM smtp_profile WHERE company_id = $cid LIMIT 1;
            """;
        cmd.Parameters.AddWithValue("$cid", companyId.ToString("D"));
        using var r = cmd.ExecuteReader();
        if (!r.Read()) return null;
        return new SmtpProfile
        {
            Host = r.GetString(0),
            Port = (int)r.GetInt64(1),
            UseTls = r.GetInt64(2) != 0,
            FromAddress = r.GetString(3),
            FromName = r.IsDBNull(4) ? null : r.GetString(4),
        };
    }

    /// <inheritdoc cref="ISmtpProfileRepository.Delete" />
    public void DeleteSmtpProfile(Guid companyId)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "DELETE FROM smtp_profile WHERE company_id = $cid;";
        cmd.Parameters.AddWithValue("$cid", companyId.ToString("D"));
        cmd.ExecuteNonQuery();
    }

    void ISmtpProfileRepository.Save(Guid companyId, SmtpProfile profile) => SaveSmtpProfile(companyId, profile);
    SmtpProfile? ISmtpProfileRepository.Get(Guid companyId) => GetSmtpProfile(companyId);
    void ISmtpProfileRepository.Delete(Guid companyId) => DeleteSmtpProfile(companyId);

    // ------------------------------------------------------------------ low-level helpers

    private void Exec(string sql)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    private void ExecTx(SqliteTransaction tx, string sql, params (string Name, object Value)[] ps)
    {
        using var cmd = _connection.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = sql;
        foreach (var (name, value) in ps)
            cmd.Parameters.AddWithValue(name, value);
        cmd.ExecuteNonQuery();
    }

    private long ScalarLong(string sql)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = sql;
        var result = cmd.ExecuteScalar();
        return result is null or DBNull ? 0 : Convert.ToInt64(result);
    }

    /// <summary>
    /// Interest rate percent → millis (rate% × 1000), stored as INTEGER so the rate round-trips exactly
    /// (no binary float). Throws if the rate carries more than 3 decimal places (would lose precision).
    /// </summary>
    private static long RateToMillis(decimal ratePercent)
    {
        var scaled = ratePercent * 1000m;
        var truncated = decimal.Truncate(scaled);
        if (scaled != truncated)
            throw new InvalidOperationException(
                $"Interest rate {ratePercent}% is not exact to 3 decimal places; cannot persist without loss.");
        return (long)truncated;
    }

    /// <summary>
    /// A forex amount / exchange rate → micros (value × 1,000,000), stored as INTEGER so it round-trips
    /// exactly (no binary float). Throws if the value carries more than 6 decimal places (would lose precision).
    /// </summary>
    private static long MicroFromDecimal(decimal value)
    {
        var scaled = value * Schema.ForexScale;
        var truncated = decimal.Truncate(scaled);
        if (scaled != truncated)
            throw new InvalidOperationException(
                $"Value {value} is not exact to 6 decimal places; cannot persist as forex micros without loss.");
        return (long)truncated;
    }

    /// <summary>Micros (value × 1,000,000) → an exact decimal.</summary>
    private static decimal MicroToDecimal(long micros) => micros / (decimal)Schema.ForexScale;

    /// <summary>
    /// A stock quantity → micros (qty × 1,000,000), stored as INTEGER so a 0–4-decimal quantity round-trips
    /// exactly (no binary float). Throws if the quantity carries more than 6 decimal places (would lose precision).
    /// </summary>
    private static long QtyMicroFromDecimal(decimal qty)
    {
        var scaled = qty * Schema.QuantityScale;
        var truncated = decimal.Truncate(scaled);
        if (scaled != truncated)
            throw new InvalidOperationException(
                $"Quantity {qty} is not exact to 6 decimal places; cannot persist as quantity micros without loss.");
        return (long)truncated;
    }

    /// <summary>Quantity micros (qty × 1,000,000) → an exact decimal.</summary>
    private static decimal QtyMicroToDecimal(long micros) => micros / (decimal)Schema.QuantityScale;

    /// <summary>The scale a BOM carve-out percent is stored at (× 1,000 = "millis"), as INTEGER, so a 3-dp
    /// percentage (e.g. 5.125%) round-trips exactly (no float — ER-3).</summary>
    private const long PercentMillisScale = 1_000L;

    /// <summary>A carve-out percent → millis (percent × 1,000), stored as INTEGER. Throws if the percent carries
    /// more than 3 decimal places (would lose precision).</summary>
    private static long PercentMillisFromDecimal(decimal percent)
    {
        var scaled = percent * PercentMillisScale;
        var truncated = decimal.Truncate(scaled);
        if (scaled != truncated)
            throw new InvalidOperationException(
                $"BOM carve-out percent {percent} is not exact to 3 decimal places; cannot persist as percent millis without loss.");
        return (long)truncated;
    }

    /// <summary>Percent millis (percent × 1,000) → an exact decimal percent.</summary>
    private static decimal PercentMillisToDecimal(long millis) => millis / (decimal)PercentMillisScale;

    private static string FormatDate(DateOnly d) => d.ToString("yyyy-MM-dd");

    private static DateOnly ParseDate(string s) =>
        DateOnly.ParseExact(s, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>ISO-8601 round-trip format (o) for a portal <see cref="DateTimeOffset"/> (v42 e-Way generation timestamp /
    /// validity) — preserves the offset so the value round-trips byte-stably (never a clock read, always portal-issued).</summary>
    private static string FormatDateTimeOffset(DateTimeOffset dto) =>
        dto.ToString("o", System.Globalization.CultureInfo.InvariantCulture);

    private static DateTimeOffset ParseDateTimeOffset(string s) =>
        DateTimeOffset.Parse(s, System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.RoundtripKind);

    /// <summary>Closes the underlying connection and releases the database file handle.</summary>
    public void Dispose()
    {
        _connection.Dispose();
        // Ensure the native SQLite handle is released promptly so a temp .db file can be deleted.
        SqliteConnection.ClearPool(_connection);
    }
}
