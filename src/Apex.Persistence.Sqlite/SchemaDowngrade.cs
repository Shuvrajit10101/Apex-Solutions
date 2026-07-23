using Microsoft.Data.Sqlite;

namespace Apex.Persistence.Sqlite;

/// <summary>
/// The <b>downgrade</b> counterparts to the <c>MigrateVNToVN+1</c> scripts — the house discipline that every
/// schema bump ships with a way back down. A downgrade is not a production path (the store only ever migrates
/// forward); it exists so a round-trip test can manufacture a <i>genuine</i> older database, <b>carrying real
/// rows</b>, and then drive the real migration over it. Without that, a migration is only ever exercised against
/// an empty schema, which is exactly where the interesting failures are not.
/// </summary>
public static class SchemaDowngrade
{
    /// <summary>
    /// Reverses <see cref="Schema.MigrateV44ToV45"/>: removes the four party Mailing Details columns from
    /// <c>ledgers</c> and stamps <c>schema_version</c> back to 44. Any captured mailing details are discarded —
    /// that is what a downgrade means. Nothing else is touched, because v45 added no tables, indexes or constraints.
    ///
    /// <para><b>Why this is code and not a <c>DROP COLUMN</c> script.</b> SQLite implements
    /// <c>ALTER TABLE … DROP COLUMN</c> by editing the table's stored <c>CREATE TABLE</c> text, and that editing
    /// fails outright on a heavily-commented DDL like ours — it leaves a dangling trailing comma ahead of the
    /// v45 comment block and SQLite rejects the result with <c>"error in table ledgers after drop column:
    /// incomplete input"</c>. The alternative the repo used previously was to hand-write the whole prior-version
    /// <c>CREATE TABLE</c> in the downgrade; for <c>ledgers</c> that would mean duplicating sixty-odd columns that
    /// then silently rot the next time a column is added. So this rebuilds the table from
    /// <c>PRAGMA table_info</c> instead: whatever columns exist minus the v45 four. It cannot drift.</para>
    /// </summary>
    public static void V45ToV44(SqliteConnection connection)
    {
        ArgumentNullException.ThrowIfNull(connection);

        var keep = ColumnNames(connection, "ledgers")
            .Where(c => !Schema.V45MailingColumns.Contains(c, StringComparer.OrdinalIgnoreCase))
            .ToList();

        if (keep.Count > 0 && keep.Count < ColumnNames(connection, "ledgers").Count)
        {
            var columnList = string.Join(", ", keep.Select(c => $"\"{c}\""));

            // Foreign keys off for the swap: other tables reference ledgers(id), and the intermediate DROP would
            // otherwise trip enforcement. The rebuilt table carries the same rows and the same id values.
            Exec(connection, "PRAGMA foreign_keys=OFF;");
            Exec(connection, $"""
                CREATE TABLE ledgers_v44 AS SELECT {columnList} FROM ledgers;
                DROP TABLE ledgers;
                ALTER TABLE ledgers_v44 RENAME TO ledgers;
                """);
            Exec(connection, "PRAGMA foreign_keys=ON;");
        }

        Exec(connection, "UPDATE schema_version SET version = 44;");
    }

    /// <summary>
    /// Reverses <see cref="Schema.MigrateV45ToV46"/>: removes the <c>unit_id</c> column from
    /// <c>voucher_inventory_lines</c> and stamps <c>schema_version</c> back to 45. Any line unit is discarded —
    /// that is what a downgrade means, and the resulting line reads as "already in the item's base unit", which is
    /// exactly how v45 interpreted every row. Nothing else is touched; v46 added no tables, indexes or constraints.
    ///
    /// <para>Rebuilt from <c>PRAGMA table_info</c> for the same reason <see cref="V45ToV44"/> is: SQLite's
    /// <c>ALTER TABLE … DROP COLUMN</c> re-parses the stored, heavily-commented <c>CREATE TABLE</c> text and fails
    /// on it. <b>The rebuild deliberately restores the full v45 DDL for this table</b> rather than a bare
    /// <c>CREATE … AS SELECT</c>: <c>voucher_inventory_lines</c> has an <c>INTEGER PRIMARY KEY AUTOINCREMENT</c>
    /// that a <c>CREATE … AS SELECT</c> would silently drop, leaving a manufactured "v45" database whose shape
    /// differs from a real one — and the very next insert would then fail to allocate an id.</para>
    /// </summary>
    public static void V46ToV45(SqliteConnection connection)
    {
        ArgumentNullException.ThrowIfNull(connection);

        var all = ColumnNames(connection, "voucher_inventory_lines");
        var keep = all
            .Where(c => !Schema.V46ItemLineUnitColumns.Contains(c, StringComparer.OrdinalIgnoreCase))
            .ToList();

        if (keep.Count > 0 && keep.Count < all.Count)
        {
            var columnList = string.Join(", ", keep.Select(c => $"\"{c}\""));

            Exec(connection, "PRAGMA foreign_keys=OFF;");
            Exec(connection, $"""
                CREATE TABLE voucher_inventory_lines_v45 (
                    id                INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                    voucher_id        TEXT    NOT NULL REFERENCES vouchers(id),
                    line_order        INTEGER NOT NULL,
                    stock_item_id     TEXT    NOT NULL REFERENCES stock_items(id),
                    godown_id         TEXT    NOT NULL REFERENCES godowns(id),
                    quantity_micro    INTEGER NOT NULL,
                    direction         INTEGER NOT NULL,
                    rate_paisa        INTEGER NOT NULL,
                    batch_label       TEXT        NULL,
                    batch_id          TEXT        NULL REFERENCES batch_masters(id),
                    actual_qty_micro  INTEGER     NULL,
                    billed_qty_micro  INTEGER     NULL
                );
                INSERT INTO voucher_inventory_lines_v45 ({columnList}) SELECT {columnList} FROM voucher_inventory_lines;
                DROP TABLE voucher_inventory_lines;
                ALTER TABLE voucher_inventory_lines_v45 RENAME TO voucher_inventory_lines;
                CREATE INDEX ix_voucher_inv_lines_voucher ON voucher_inventory_lines(voucher_id);
                """);
            Exec(connection, "PRAGMA foreign_keys=ON;");
        }

        Exec(connection, "UPDATE schema_version SET version = 45;");
    }

    /// <summary>
    /// Reverses <see cref="Schema.MigrateV46ToV47"/>: drops the two date-keyed affix child tables
    /// (<c>voucher_type_prefix</c>, <c>voucher_type_suffix</c> — their indexes drop with them) and removes the three
    /// numbering columns (<see cref="Schema.V47NumberingColumns"/>) from <c>voucher_types</c>, then stamps
    /// <c>schema_version</c> back to 46. Any captured numbering config is discarded — that is what a downgrade means.
    ///
    /// <para>The child tables are dropped <b>first</b> (they FK <c>voucher_types</c>, which is rebuilt below), then
    /// <c>voucher_types</c> is rebuilt from <c>PRAGMA table_info</c> minus the three v47 columns via the plain
    /// <c>CREATE … AS SELECT</c> idiom of <see cref="V45ToV44"/>. <c>voucher_types</c>'s primary key is a
    /// <c>TEXT</c> GUID (<c>id</c>), so the AUTOINCREMENT-preserving full-DDL special-case that
    /// <see cref="V46ToV45"/> needed does NOT apply. Constraint/index loss on the rebuild is tolerated by the
    /// row-survival-only downgrade harness, exactly as it already is for <c>ledgers</c>. Foreign keys are switched
    /// off for the swap because other tables reference <c>voucher_types(id)</c>.</para>
    /// </summary>
    public static void V47ToV46(SqliteConnection connection)
    {
        ArgumentNullException.ThrowIfNull(connection);

        Exec(connection, "PRAGMA foreign_keys=OFF;");
        // Drop the two numbering affix child tables first (their indexes drop with them); they FK voucher_types,
        // which is rebuilt below. This table-drop-in-downgrade is new territory, so it is explicit and comes FIRST.
        Exec(connection, "DROP TABLE IF EXISTS voucher_type_prefix;");
        Exec(connection, "DROP TABLE IF EXISTS voucher_type_suffix;");

        var all = ColumnNames(connection, "voucher_types");
        var keep = all
            .Where(c => !Schema.V47NumberingColumns.Contains(c, StringComparer.OrdinalIgnoreCase))
            .ToList();

        if (keep.Count > 0 && keep.Count < all.Count)
        {
            var columnList = string.Join(", ", keep.Select(c => $"\"{c}\""));
            Exec(connection, $"""
                CREATE TABLE voucher_types_v46 AS SELECT {columnList} FROM voucher_types;
                DROP TABLE voucher_types;
                ALTER TABLE voucher_types_v46 RENAME TO voucher_types;
                """);
        }

        Exec(connection, "PRAGMA foreign_keys=ON;");
        Exec(connection, "UPDATE schema_version SET version = 46;");
    }

    private static List<string> ColumnNames(SqliteConnection connection, string table)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = $"PRAGMA table_info(\"{table}\");";
        var names = new List<string>();
        using var r = cmd.ExecuteReader();
        while (r.Read()) names.Add(r.GetString(1));
        return names;
    }

    private static void Exec(SqliteConnection connection, string sql)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }
}
