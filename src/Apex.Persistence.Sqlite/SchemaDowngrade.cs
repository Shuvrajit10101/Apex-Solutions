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

    /// <summary>
    /// Reverses <see cref="Schema.MigrateV47ToV48"/>: removes the two counterparty-reference columns
    /// (<see cref="Schema.V48ReferenceColumns"/> — <c>reference_no</c>, <c>reference_date</c>) from <c>vouchers</c>
    /// and stamps <c>schema_version</c> back to 47. Any captured reference is discarded — that is what a downgrade
    /// means, and the resulting voucher reads as "no counterparty reference", exactly how v47 interpreted every row.
    /// Nothing else is touched; v48 added no tables, indexes or constraints.
    ///
    /// <para><c>vouchers</c> is rebuilt from <c>PRAGMA table_info</c> minus the two v48 columns via the plain
    /// <c>CREATE … AS SELECT</c> idiom of <see cref="V45ToV44"/>. <c>vouchers</c>'s primary key is a <c>TEXT</c> GUID
    /// (<c>id</c>), so the AUTOINCREMENT-preserving full-DDL special-case that <see cref="V46ToV45"/> needed does NOT
    /// apply. Constraint/index loss on the rebuild is tolerated by the row-survival-only downgrade harness, exactly
    /// as it already is for <c>ledgers</c> and <c>voucher_types</c>. Foreign keys are switched off for the swap
    /// because <c>entry_lines</c>, <c>voucher_inventory_lines</c>, <c>pos_tender_allocations</c> and others reference
    /// <c>vouchers(id)</c>.</para>
    /// </summary>
    public static void V48ToV47(SqliteConnection connection)
    {
        ArgumentNullException.ThrowIfNull(connection);

        var all = ColumnNames(connection, "vouchers");
        var keep = all
            .Where(c => !Schema.V48ReferenceColumns.Contains(c, StringComparer.OrdinalIgnoreCase))
            .ToList();

        if (keep.Count > 0 && keep.Count < all.Count)
        {
            var columnList = string.Join(", ", keep.Select(c => $"\"{c}\""));

            Exec(connection, "PRAGMA foreign_keys=OFF;");
            Exec(connection, $"""
                CREATE TABLE vouchers_v47 AS SELECT {columnList} FROM vouchers;
                DROP TABLE vouchers;
                ALTER TABLE vouchers_v47 RENAME TO vouchers;
                """);
            Exec(connection, "PRAGMA foreign_keys=ON;");
        }

        Exec(connection, "UPDATE schema_version SET version = 47;");
    }

    /// <summary>
    /// Reverses <see cref="Schema.MigrateV48ToV49"/>: removes the accounting-invoice flag column
    /// (<see cref="Schema.V49AccountingInvoiceColumns"/> — <c>is_accounting_invoice</c>) from <c>vouchers</c> and
    /// stamps <c>schema_version</c> back to 48. The flag is discarded — that is what a downgrade means, and the
    /// resulting voucher reads as "not an accounting invoice", exactly how v48 interpreted every row. Nothing else is
    /// touched; v49 added no tables, indexes or constraints.
    ///
    /// <para><c>vouchers</c> is rebuilt from <c>PRAGMA table_info</c> minus the v49 column via the same plain
    /// <c>CREATE … AS SELECT</c> idiom <see cref="V48ToV47"/> uses, for the same reasons (SQLite's
    /// <c>DROP COLUMN</c> chokes on our commented DDL; a hand-written prior-version <c>CREATE TABLE</c> would rot).
    /// Foreign keys are switched off for the swap because <c>entry_lines</c>, <c>voucher_inventory_lines</c>,
    /// <c>pos_tender_allocations</c> and others reference <c>vouchers(id)</c>.</para>
    ///
    /// <para><b>KNOWN (F6), unchanged deliberately:</b> the <c>CREATE … AS SELECT</c> rebuild reproduces the columns
    /// and data but NOT the table's PRIMARY KEY, its NOT NULL constraints or its index — the downgraded
    /// <c>vouchers</c> table is looser than a genuine v48 one. This is the SAME pre-existing idiom as
    /// <see cref="V48ToV47"/> and <see cref="V47ToV46"/>, and <c>SchemaDowngrade</c> is referenced nowhere in
    /// <c>src/</c> — it exists so the tests can prove the forward migration reaches byte-equal parity with a fresh
    /// <c>CreateV1</c>, and no shipped code path ever opens a downgraded database. Fixing it means rewriting all three
    /// (and every future) downgrade to emit a real prior-version DDL, which is a separate change; doing it for v49
    /// alone would leave the chain inconsistent.</para>
    /// </summary>
    public static void V49ToV48(SqliteConnection connection)
    {
        ArgumentNullException.ThrowIfNull(connection);

        var all = ColumnNames(connection, "vouchers");
        var keep = all
            .Where(c => !Schema.V49AccountingInvoiceColumns.Contains(c, StringComparer.OrdinalIgnoreCase))
            .ToList();

        if (keep.Count > 0 && keep.Count < all.Count)
        {
            var columnList = string.Join(", ", keep.Select(c => $"\"{c}\""));

            Exec(connection, "PRAGMA foreign_keys=OFF;");
            Exec(connection, $"""
                CREATE TABLE vouchers_v48 AS SELECT {columnList} FROM vouchers;
                DROP TABLE vouchers;
                ALTER TABLE vouchers_v48 RENAME TO vouchers;
                """);
            Exec(connection, "PRAGMA foreign_keys=ON;");
        }

        Exec(connection, "UPDATE schema_version SET version = 48;");
    }

    /// <summary>
    /// Reverses <see cref="Schema.MigrateV49ToV50"/>: removes the negative-stock warning column
    /// (<see cref="Schema.V50NegativeStockColumns"/> — <c>warn_on_negative_stock</c>) from <c>companies</c> and
    /// stamps <c>schema_version</c> back to 49. Nothing else is touched; v50 added no tables, indexes or constraints.
    ///
    /// <para>⚠️ <b>The default-TRUE asymmetry makes this downgrade's round-trip a real assertion, not a formality.</b>
    /// For every previous flag the column defaulted 0, so dropping it and re-migrating restored the same value the
    /// row already had — the round-trip could not fail. Here the column defaults <b>1</b>, so a company that had the
    /// flag OFF loses that setting on the way down and comes back up ON. That is the correct meaning of a downgrade
    /// (the information genuinely no longer exists in a v49 database), and it is exactly what makes the re-migration
    /// worth testing: it proves the back-fill hands a pre-v50 book warnings-ON rather than <c>default(bool)</c>.</para>
    ///
    /// <para><c>companies</c> is rebuilt from <c>PRAGMA table_info</c> minus the v50 column via the same plain
    /// <c>CREATE … AS SELECT</c> idiom <see cref="V49ToV48"/> uses, for the same reasons (SQLite's
    /// <c>DROP COLUMN</c> chokes on our commented DDL; a hand-written prior-version <c>CREATE TABLE</c> would rot).
    /// Foreign keys are switched off for the swap. <b>KNOWN (F6), unchanged deliberately:</b> like every other
    /// downgrade here, the rebuild reproduces columns and data but not the PRIMARY KEY / NOT NULLs — see
    /// <see cref="V49ToV48"/> for why that is tolerated and why fixing it is a separate change.</para>
    /// </summary>
    public static void V50ToV49(SqliteConnection connection)
    {
        ArgumentNullException.ThrowIfNull(connection);

        var all = ColumnNames(connection, "companies");
        var keep = all
            .Where(c => !Schema.V50NegativeStockColumns.Contains(c, StringComparer.OrdinalIgnoreCase))
            .ToList();

        if (keep.Count > 0 && keep.Count < all.Count)
        {
            var columnList = string.Join(", ", keep.Select(c => $"\"{c}\""));

            Exec(connection, "PRAGMA foreign_keys=OFF;");
            Exec(connection, $"""
                CREATE TABLE companies_v49 AS SELECT {columnList} FROM companies;
                DROP TABLE companies;
                ALTER TABLE companies_v49 RENAME TO companies;
                """);
            Exec(connection, "PRAGMA foreign_keys=ON;");
        }

        Exec(connection, "UPDATE schema_version SET version = 49;");
    }

    /// <summary>
    /// Reverses <see cref="Schema.MigrateV50ToV51"/>: removes the GST five-level hierarchy columns — the six on
    /// <c>companies</c> (<see cref="Schema.V51GstHierarchyCompanyColumns"/>) and the four
    /// (<see cref="Schema.V51GstHierarchyMasterColumns"/>) on <b>each</b> of <c>groups</c> and <c>stock_groups</c> —
    /// and stamps <c>schema_version</c> back to 50. Nothing else is touched; v51 added no tables, indexes or
    /// constraints.
    ///
    /// <para>⚠️ <b>This downgrade is NOT information-preserving on the two source-order columns, and that is what
    /// makes re-migrating a real assertion.</b> A company that had chosen <c>LedgerFirst</c> loses that choice on the
    /// way down (a v50 database has nowhere to record it) and comes back up as <c>StockItemFirst</c> from
    /// <see cref="Schema.MigrateV50ToV51"/>'s back-fill. That is the correct meaning of a downgrade, and it is
    /// precisely the round-trip that proves the back-fill hands a pre-v51 book the ITEM-FIRST order it has always
    /// resolved with, rather than the fresh default. The twelve <c>MasterGstDetails</c> columns are genuinely
    /// discarded, and come back NULL — "no GST block" — which is what a v50 master was.</para>
    ///
    /// <para>Each table is rebuilt from <c>PRAGMA table_info</c> minus its v51 columns via the same plain
    /// <c>CREATE … AS SELECT</c> idiom <see cref="V50ToV49"/> uses, for the same reasons (SQLite's
    /// <c>DROP COLUMN</c> chokes on our commented DDL; a hand-written prior-version <c>CREATE TABLE</c> would rot).
    /// Foreign keys are switched off for the swap — <c>groups</c> is referenced by <c>ledgers</c>,
    /// <c>companies.profit_and_loss_head_id</c> and by itself, <c>stock_groups</c> by <c>stock_items</c> and by
    /// itself, and <c>companies</c> by nearly every table. <b>KNOWN (F6), unchanged deliberately:</b> like every
    /// other downgrade here, the rebuild reproduces columns and data but not the PRIMARY KEY / NOT NULLs — see
    /// <see cref="V49ToV48"/> for why that is tolerated and why fixing it is a separate change.</para>
    /// </summary>
    public static void V51ToV50(SqliteConnection connection)
    {
        ArgumentNullException.ThrowIfNull(connection);

        DropColumns(connection, "companies", Schema.V51GstHierarchyCompanyColumns, "companies_v50");
        DropColumns(connection, "groups", Schema.V51GstHierarchyMasterColumns, "groups_v50");
        DropColumns(connection, "stock_groups", Schema.V51GstHierarchyMasterColumns, "stock_groups_v50");

        Exec(connection, "UPDATE schema_version SET version = 50;");
    }

    /// <summary>
    /// Rebuilds <paramref name="table"/> without <paramref name="drop"/>, via the <c>CREATE … AS SELECT</c> / swap
    /// idiom every downgrade above open-codes. Extracted at v51 only because that version is the first to drop
    /// columns from three tables at once — the behaviour is identical to the open-coded blocks, including the
    /// no-op guard when nothing (or everything) would be kept.
    /// </summary>
    private static void DropColumns(
        SqliteConnection connection, string table, IReadOnlyList<string> drop, string scratchName)
    {
        var all = ColumnNames(connection, table);
        var keep = all.Where(c => !drop.Contains(c, StringComparer.OrdinalIgnoreCase)).ToList();
        if (keep.Count == 0 || keep.Count == all.Count) return;

        var columnList = string.Join(", ", keep.Select(c => $"\"{c}\""));

        Exec(connection, "PRAGMA foreign_keys=OFF;");
        Exec(connection, $"""
            CREATE TABLE {scratchName} AS SELECT {columnList} FROM "{table}";
            DROP TABLE "{table}";
            ALTER TABLE {scratchName} RENAME TO "{table}";
            """);
        Exec(connection, "PRAGMA foreign_keys=ON;");
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
