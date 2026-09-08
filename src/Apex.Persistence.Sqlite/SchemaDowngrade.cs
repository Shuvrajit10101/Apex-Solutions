using System.Text;
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
    /// <c>CREATE … AS SELECT</c> idiom <see cref="V50ToV49"/> uses. Foreign keys are switched off for the swap —
    /// <c>groups</c> is referenced by <c>ledgers</c>, <c>companies.profit_and_loss_head_id</c> and by itself,
    /// <c>stock_groups</c> by <c>stock_items</c> and by itself, and <c>companies</c> by nearly every table.</para>
    ///
    /// <para>🔴 <b>KNOWN (F6) — CORRECTED AND NARROWED by the owed review (lens 1 finding 2), because the previous
    /// wording understated the loss on exactly the version that first made it matter.</b> Like every other downgrade
    /// here the rebuild reproduces columns and data but NOT the table's PRIMARY KEY, its NOT NULL constraints or its
    /// DEFAULTs — <b>and this is the first downgrade whose tables also carry INDEXES</b> (<c>ix_groups_company</c>,
    /// <c>ix_stock_groups_company</c>), which <c>DROP TABLE</c> takes with them. <see cref="DropColumns"/> therefore
    /// re-creates every non-implicit index it dropped; the PRIMARY KEY / NOT NULL / DEFAULT loss remains, and is
    /// still tolerated for the reason <see cref="V49ToV48"/> gives. <b>Measured consequences of that residual loss,
    /// recorded rather than left to be rediscovered:</b> on a round-tripped file <c>PRAGMA integrity_check</c> still
    /// answers <c>ok</c> (so it is NOT the check that would catch this), <c>PRAGMA foreign_key_check</c> throws, and
    /// <c>SqliteCompanyStore.Save</c> throws "foreign key mismatch" because the store opens with
    /// <c>PRAGMA foreign_keys = ON</c>. <b>Nothing in <c>src/</c> calls <see cref="SchemaDowngrade"/></b>, so this is
    /// a test-harness fidelity limit, not shipped data loss — but it means the v50 → v51 migration has never been
    /// exercised against a <c>companies</c> table that still had its PRIMARY KEY, NOT NULLs and DEFAULTs.</para>
    ///
    /// <para><b>And the "SQLite's DROP COLUMN chokes on our commented DDL" justification, measured rather than
    /// assumed:</b> on the shipped SQLite 3.50.4, native <c>ALTER TABLE … DROP COLUMN</c> succeeds on <b>12 of the
    /// 14</b> v51 columns and preserves <c>companies.id</c>'s primary key and all three indexes. It fails on exactly
    /// the two that are <b>last in their table's DDL</b> (<c>companies.gst_default_supply_type</c>,
    /// <c>stock_groups.gst_supply_type</c>) with <c>"incomplete input"</c>, because the trailing <c>--</c> comment on
    /// the final column is left dangling. So the blanket justification is true for 2/14, not 14/14. Switching the
    /// whole chain to native <c>DROP COLUMN</c> is a separate change (it would have to move or re-shape those
    /// trailing comments); the rebuild is kept so the chain stays one idiom.</para>
    /// </summary>
    public static void V51ToV50(SqliteConnection connection)
    {
        ArgumentNullException.ThrowIfNull(connection);

        // ⚠️ NOT TRANSACTIONAL — three autocommit DropColumns plus the version stamp (owed-review lens 1
        // finding 4). A failure between them leaves a split state: some tables downgraded, schema_version still
        // 51. Deliberately left as-is because this is test-only code with no caller in src/; the FORWARD
        // migration, which is the one a customer's book runs, IS transactional and was measured to be (see
        // Schema.MigrateV50ToV51). Note what a split state costs if it is ever constructed by hand: reopening
        // through the store throws a raw SqliteException "duplicate column name: gst_source_of_hsn_sac" and
        // throws again on every subsequent open, with no written-for-the-user message of the kind
        // CompanyBackup gives for a bad version.
        DropColumns(connection, "companies", Schema.V51GstHierarchyCompanyColumns, "companies_v50");
        DropColumns(connection, "groups", Schema.V51GstHierarchyMasterColumns, "groups_v50");
        DropColumns(connection, "stock_groups", Schema.V51GstHierarchyMasterColumns, "stock_groups_v50");

        Exec(connection, "UPDATE schema_version SET version = 50;");
    }

    /// <summary>
    /// Reverses <see cref="Schema.MigrateV51ToV52"/>: drops the <c>voucher_edit_log</c> table
    /// (<see cref="Schema.V52EditLogTables"/>) — its <c>ix_voucher_edit_log_company</c> index goes with it — and
    /// stamps <c>schema_version</c> back to 51.
    ///
    /// <para><b>This is the first TRUE inverse in this file, and it is one only because v52 adds nothing to an
    /// existing table.</b> Every downgrade above rebuilds a table it cannot fully reconstruct, and each documents
    /// the residual PRIMARY KEY / NOT NULL / DEFAULT loss (F6). Here there is no rebuild: v51 had no
    /// <c>voucher_edit_log</c>, so removing it restores the v51 shape exactly — same tables, same columns, same
    /// indexes, nothing rewritten and nothing else touched. Re-migrating up produces the same empty table the
    /// forward migration produces on any other v51 book.</para>
    ///
    /// <para>⚠️ <b>It is not information-preserving, and cannot be.</b> Every recorded cancellation, deletion and
    /// alteration is discarded — there is nowhere in a v51 database to keep it. That is what a downgrade means,
    /// and it is the sharpest illustration of why the table had to exist: a v51 book carries no evidence that its
    /// vouchers were ever edited, because a v51 book never could.</para>
    ///
    /// <para><c>DROP TABLE</c> rather than the <c>CREATE … AS SELECT</c> rebuild idiom of the downgrades above,
    /// because there is no column to drop — the whole object goes. The commented-DDL problem that forces the
    /// rebuild elsewhere (SQLite's <c>ALTER TABLE … DROP COLUMN</c> re-parses the stored <c>CREATE TABLE</c> text)
    /// does not arise for a <c>DROP TABLE</c>. Nothing references this table, so no <c>PRAGMA foreign_keys</c>
    /// dance is needed either.</para>
    /// </summary>
    public static void V52ToV51(SqliteConnection connection)
    {
        ArgumentNullException.ThrowIfNull(connection);

        foreach (var table in Schema.V52EditLogTables)
            Exec(connection, $"DROP TABLE IF EXISTS \"{table}\";");

        Exec(connection, "UPDATE schema_version SET version = 51;");
    }

    /// <summary>
    /// Reverses <see cref="Schema.MigrateV52ToV53"/>: removes the two Voucher Type user-flag columns
    /// (<see cref="Schema.V53VoucherTypeFlagColumns"/>) from <c>voucher_types</c> and stamps
    /// <c>schema_version</c> back to 52.
    ///
    /// <para>⚠️ <b>Not information-preserving, and the loss is the point of the version.</b> A type configured to
    /// print after saving, or to take a narration per ledger, comes back with both flags OFF — a v52 database has
    /// nowhere to record either. Re-migrating up therefore yields the same all-zero columns the forward migration
    /// gives any other v52 book, which is exactly what makes the "no back-fill" claim testable.</para>
    ///
    /// <para>🔴 <b>What a downgrade CANNOT undo, recorded so it is not rediscovered.</b> A book saved at v53 may
    /// hold <c>numbering = 3</c> or <c>4</c> (Automatic (Manual Override) / Multi-user Auto). Those ordinals are in
    /// the pre-existing <c>numbering</c> column, which this downgrade does not touch, so they SURVIVE into the
    /// v52-shaped file — where a v52 reader would map them onto an enum that has only 0/1/2 and read them as an
    /// out-of-range cast. That is not a defect in this method (a downgrade cannot invent a v52 meaning for a choice
    /// v52 could not express); it is the reason the two methods were appended rather than inserted, and the reason
    /// this file is test-only — nothing in <c>src/</c> calls <see cref="SchemaDowngrade"/>.</para>
    ///
    /// <para>The <c>CREATE … AS SELECT</c> rebuild idiom via <see cref="DropColumns"/>, so it carries the same
    /// documented residual (PRIMARY KEY / NOT NULL / DEFAULT loss on the rebuilt table, F6) as every other
    /// column-dropping downgrade here, and re-creates <c>ix_voucher_types_company</c> which
    /// <c>DROP TABLE</c> would otherwise take with it.</para>
    /// </summary>
    public static void V53ToV52(SqliteConnection connection)
    {
        ArgumentNullException.ThrowIfNull(connection);

        DropColumns(connection, "voucher_types", Schema.V53VoucherTypeFlagColumns, "voucher_types_v52");

        Exec(connection, "UPDATE schema_version SET version = 52;");
    }

    /// <summary>
    /// Reverses <see cref="Schema.MigrateV53ToV54"/> (census 10.1): drops the three Credit-Limit columns
    /// (<see cref="Schema.V54CreditLimitColumns"/>) from <c>ledgers</c> and stamps the marker back to 53. Used by
    /// the tests to manufacture a genuine v53 database out of a current one, so a migration test runs against real
    /// rows rather than hand-written DDL.
    ///
    /// <para>⚠️ <b>NOT information-preserving, and that is the point of the version.</b> A party carrying a credit
    /// limit comes back with none — the ledger will accept, at v53, an invoice it would have refused at v54. The
    /// two flags come back off. Nothing else on the ledger moves. The same <c>CREATE … AS SELECT</c> constraint-loss
    /// residual documented on <see cref="DropColumns"/> applies; <c>ix_ledgers_company</c> is replayed by that
    /// helper.</para>
    ///
    /// <para>🔴 <b>This is no longer the top rung.</b> v55 (the Karnataka PT back-fill) sits above it, so a caller
    /// manufacturing a v53 book out of a CURRENT one must run <see cref="V55ToV54"/> first and this second. Calling
    /// this alone on a v55 file stamps the marker to 53 while skipping a rung, and the forward climb would then
    /// re-run v54 and v55 against a file that had never been un-done — which is exactly the silent hole a downgrade
    /// chain exists to prevent.</para>
    /// </summary>
    public static void V54ToV53(SqliteConnection connection)
    {
        ArgumentNullException.ThrowIfNull(connection);

        DropColumns(connection, "ledgers", Schema.V54CreditLimitColumns, "ledgers_v53");

        Exec(connection, "UPDATE schema_version SET version = 53;");
    }

    /// <summary>
    /// Reverses <see cref="Schema.MigrateV54ToV55"/> — which, because v55 adds no DDL, means stamping
    /// <c>schema_version</c> back to 54 and <b>nothing else</b>. There is no column to drop and no table to
    /// remove: a v54 database and a v55 database have byte-identical <c>PRAGMA table_info</c> for every table.
    ///
    /// <para>🔴 <b>The corrected Karnataka figure deliberately SURVIVES the downgrade, and that is the whole
    /// point.</b> v55's forward step is a data repair, so the only "information" it destroys is a ₹300 February
    /// over-charge with no statutory basis. Restoring it here — putting back a deduction the state never levied,
    /// in the name of symmetry — would re-introduce the exact wrong-money defect the version exists to close, in
    /// every book that ever round-trips. A downgrade owes the caller a v54 <i>shape</i>; it does not owe them a
    /// v54 <i>mistake</i>. A v54 reader opens the corrected book fine: <c>month_overrides = ''</c> is a value v54
    /// has always been able to store and read (it is what every non-February band has always held).</para>
    ///
    /// <para>This asymmetry means <c>down → up</c> is a no-op rather than a re-run: the second forward pass finds
    /// <c>month_overrides</c> already cleared, fails the <c>'2:30000'</c> predicate and moves nothing, which is
    /// what makes the forward migration's idempotence testable from here.</para>
    ///
    /// <para>🔴 <b>This is no longer the top rung.</b> v56 (Security Control) sits above it, so a caller
    /// manufacturing a v54 book out of a CURRENT one must run <see cref="V56ToV55"/> first and this second.</para>
    ///
    /// <para>Like every method in this file this is test-only — nothing in <c>src/</c> calls
    /// <see cref="SchemaDowngrade"/>.</para>
    /// </summary>
    public static void V55ToV54(SqliteConnection connection)
    {
        ArgumentNullException.ThrowIfNull(connection);

        // Deliberately no DDL and deliberately no data restore. See the summary: v55 adds nothing to un-add, and
        // putting the unsourced ₹300 February back would re-open the defect on every round-trip.
        Exec(connection, "UPDATE schema_version SET version = 54;");
    }

    /// <summary>
    /// Reverses <see cref="Schema.MigrateV55ToV56"/> (census 16.2 Security Control): drops the three v56 tables
    /// (<see cref="Schema.V56SecurityTables"/>) and the three v56 <c>companies</c> columns
    /// (<see cref="Schema.V56SecurityCompanyColumns"/>), then stamps the marker back to 55. Used by the tests to
    /// manufacture a genuine v55 database out of a current one, so the migration test runs against real rows
    /// rather than hand-written DDL.
    ///
    /// <para>🔴 <b>THE DROP ORDER IS LOAD-BEARING.</b> <c>company_users</c> and <c>security_level_rules</c> both
    /// FK <c>security_levels</c>, so the level table must go LAST.
    /// <see cref="Schema.V56SecurityTables"/> is declared in exactly that order and this method walks it as
    /// given — do not sort it.</para>
    ///
    /// <para>⚠️ <b>NOT information-preserving, and that is the point of the version.</b> Every user, every
    /// security level, every facility rule and the password policy are DESTROYED — including the one-way password
    /// verifiers, which is the only correct thing to do with them: there is nothing to migrate them into at v55,
    /// and a v55 book has no access control to enforce. A company that had access control ON comes back with it
    /// off.</para>
    ///
    /// <para>🔴 <b>WHY THIS USES <see cref="RebuildPreservingShape"/> AND NOT <see cref="DropColumns"/>, which
    /// every downgrade above it uses — a MEASURED defect, not a preference.</b> <c>companies</c> is the FK
    /// <b>parent</b> of ~40 child tables. <see cref="DropColumns"/>'s <c>CREATE … AS SELECT</c> rebuild produces
    /// a table with <b>no PRIMARY KEY</b> (the documented constraint-loss residual), and SQLite requires a
    /// parent key to be a PRIMARY KEY or UNIQUE — so the first child insert afterwards dies with
    /// <c>SQLite Error 1: 'foreign key mismatch - "pt_slab_bands" referencing "companies"'</c>. That is exactly
    /// what happened on the first run of this method, in a Karnataka PT test that has nothing to do with
    /// security. Every downgrade above this one drops columns from a CHILD table (<c>ledgers</c>,
    /// <c>voucher_types</c>, <c>groups</c>, <c>stock_groups</c>), where the residual is harmless; this is the
    /// first to touch the parent. <see cref="RebuildPreservingShape"/> reconstructs the declaration from
    /// <c>PRAGMA table_info</c> + <c>PRAGMA foreign_key_list</c>, so the primary key, the NOT NULLs, the DEFAULTs
    /// and the outgoing foreign keys all survive.</para>
    ///
    /// <para>🔴 <b>This is the top rung.</b> A caller manufacturing an older book out of a CURRENT one must run
    /// this FIRST and the lower rungs after it. Calling <see cref="V55ToV54"/> alone on a v56 file stamps the
    /// marker to 54 while skipping a rung, and the forward climb would then re-run v55 and v56 against a file
    /// that had never been un-done — <c>CREATE TABLE security_levels</c> would fail on the second pass.</para>
    /// </summary>
    public static void V56ToV55(SqliteConnection connection)
    {
        ArgumentNullException.ThrowIfNull(connection);

        // FK order: children first, the referenced level table last. V56SecurityTables is declared that way.
        Exec(connection, "PRAGMA foreign_keys=OFF;");
        foreach (var table in Schema.V56SecurityTables)
            Exec(connection, $"DROP TABLE IF EXISTS \"{table}\";");
        Exec(connection, "PRAGMA foreign_keys=ON;");

        RebuildPreservingShape(connection, "companies", Schema.V56SecurityCompanyColumns, "companies_v55");

        Exec(connection, "UPDATE schema_version SET version = 55;");
    }

    /// <summary>
    /// Reverses <see cref="Schema.MigrateV56ToV57"/> (census 8.4 / 8.5 / 8.6 Banking documents): drops the three
    /// v57 tables (<see cref="Schema.V57ChequeTables"/>) and the six v57 <c>ledgers</c> columns
    /// (<see cref="Schema.V57BankingLedgerColumns"/>), then stamps the marker back to 56.
    ///
    /// <para><b>Not a true inverse, and the residual is data, not shape.</b> Every cheque book, every operator-set
    /// cheque status and every captured cheque layout is <b>discarded</b>, because there is nowhere in a v56
    /// database to keep one — the same honest loss <see cref="V52ToV51"/> records for the edit log. A ledger's
    /// account number, branch and IFSC go with the columns. The two calibration nudges and the
    /// print-company-name flag return to "never set", which is what a v56 ledger was.</para>
    ///
    /// <para>🔴 <b><c>ledgers</c> is rebuilt with <see cref="RebuildPreservingShape"/>, NOT
    /// <see cref="DropColumns"/>.</b> <c>ledgers</c> is the PARENT of foreign keys elsewhere in this schema, and a
    /// <c>CREATE … AS SELECT</c> rebuild loses its PRIMARY KEY, after which SQLite reports <c>foreign key
    /// mismatch</c> on the next child insert — the measured failure <see cref="V56ToV55"/> documents for
    /// <c>companies</c>. <see cref="V54ToV53"/> predates that finding and still uses <see cref="DropColumns"/> on
    /// this same table; it is left alone rather than changed under this slice, but a new rung does not repeat it.</para>
    ///
    /// <para>🔴 <b>Order.</b> <c>cheque_status_overrides</c> FKs <c>cheque_books</c>, so the tables are dropped in
    /// <see cref="Schema.V57ChequeTables"/> order (child first), and both are dropped BEFORE the <c>ledgers</c>
    /// rebuild — <c>cheque_books</c> and <c>cheque_layouts</c> both reference <c>ledgers(id)</c>, and rebuilding a
    /// parent out from under a live child FK is exactly the mismatch above.</para>
    ///
    /// <para>⚠️ <b>This is the TOP rung.</b> Manufacturing a v56 book out of a CURRENT one runs this FIRST and the
    /// lower rungs after it. Calling <see cref="V56ToV55"/> alone on a v57 file stamps the marker 55 while the v57
    /// objects are still there, which is a lie the next open cannot detect.</para>
    /// </summary>
    public static void V57ToV56(SqliteConnection connection)
    {
        ArgumentNullException.ThrowIfNull(connection);

        // FK order: the status table (child of cheque_books) first, then the two tables that reference ledgers,
        // and only then the ledgers rebuild. V57ChequeTables is declared in exactly that order.
        Exec(connection, "PRAGMA foreign_keys=OFF;");
        foreach (var table in Schema.V57ChequeTables)
            Exec(connection, $"DROP TABLE IF EXISTS \"{table}\";");
        Exec(connection, "PRAGMA foreign_keys=ON;");

        RebuildPreservingShape(connection, "ledgers", Schema.V57BankingLedgerColumns, "ledgers_v56");

        Exec(connection, "UPDATE schema_version SET version = 56;");
    }

    /// <summary>
    /// Reverses <see cref="Schema.MigrateV57ToV58"/> (census 9.6 Job Costing / 9.7 Item Cost Tracking / 9.8
    /// Tracking Numbers / 9.9 Stock Journal Voucher Class): drops <c>voucher_type_classes</c>
    /// (<see cref="Schema.V58ClassTables"/>), the three <c>companies</c> feature flags
    /// (<see cref="Schema.V58CompanyColumns"/>), the one <c>godowns</c> job link
    /// (<see cref="Schema.V58GodownColumns"/>) and the tracking pair
    /// (<see cref="Schema.V58TrackingLineColumns"/>) from BOTH stock-line tables, then stamps the marker back to 57.
    ///
    /// <para><b>Not a true inverse, and the residual is data, not shape.</b> Every Tracking No. and every Cost
    /// Tracking Number keyed by an operator is <b>discarded</b>, along with every godown→job/project link and
    /// every named voucher class, because a v57 database has nowhere to keep one — the same honest loss
    /// <see cref="V52ToV51"/> records for the edit log. The three feature flags return to "off", which is what a
    /// v57 company was. 🔴 <b>The stock QUANTITIES are untouched</b>: a Receipt Note and its Purchase bill both
    /// survive intact and simply stop being reconcilable by tracking number, falling back to the inferred FIFO
    /// walk that was the only mechanism before v58.</para>
    ///
    /// <para>🔴 <b>Both stock-line tables and <c>godowns</c> are rebuilt with
    /// <see cref="RebuildPreservingShape"/>, NOT <see cref="DropColumns"/>.</b> <c>godowns</c> is the PARENT of
    /// foreign keys from <c>stock_opening_balances</c>, <c>inventory_allocations</c>, <c>order_lines</c> and more,
    /// and a <c>CREATE … AS SELECT</c> rebuild loses its PRIMARY KEY, after which SQLite reports <c>foreign key
    /// mismatch</c> on the next child insert — the measured failure <see cref="V56ToV55"/> documents. The two line
    /// tables are children, but they are rebuilt the same way so their own outgoing FKs and AUTOINCREMENT-backed
    /// INTEGER PRIMARY KEY survive; a lost <c>id</c> PK there would silently renumber rows. <c>companies</c> is the
    /// parent of nearly every table in the schema and gets the same treatment for the same reason.</para>
    ///
    /// <para>🔴 <b>Order.</b> <c>voucher_type_classes</c> is dropped FIRST — it is a child of <c>voucher_types</c>,
    /// and it is gone before any rebuild runs. The four v58 indexes are carried away with their tables' rebuilds
    /// (<see cref="RebuildPreservingShape"/> skips any index naming a dropped column), so they need no explicit
    /// DROP; the class table's unique index goes with the table.</para>
    ///
    /// <para>⚠️ <b>This is the TOP rung.</b> Manufacturing a v57 book out of a CURRENT one runs this FIRST and the
    /// lower rungs after it. Calling <see cref="V57ToV56"/> alone on a v58 file stamps the marker 56 while the v58
    /// objects are still there, which is a lie the next open cannot detect.</para>
    /// </summary>
    public static void V58ToV57(SqliteConnection connection)
    {
        ArgumentNullException.ThrowIfNull(connection);

        // The class table is a child of voucher_types and is dropped outright before any rebuild.
        Exec(connection, "PRAGMA foreign_keys=OFF;");
        foreach (var table in Schema.V58ClassTables)
            Exec(connection, $"DROP TABLE IF EXISTS \"{table}\";");
        Exec(connection, "PRAGMA foreign_keys=ON;");

        // The two stock-line tables first (children), then godowns and companies (parents). Each rebuild
        // preserves the table's PK, NOT NULLs, DEFAULTs and outgoing FKs — see RebuildPreservingShape.
        RebuildPreservingShape(
            connection, "inventory_allocations", Schema.V58TrackingLineColumns, "inventory_allocations_v57");
        RebuildPreservingShape(
            connection, "voucher_inventory_lines", Schema.V58TrackingLineColumns, "voucher_inventory_lines_v57");
        RebuildPreservingShape(connection, "godowns", Schema.V58GodownColumns, "godowns_v57");
        RebuildPreservingShape(connection, "companies", Schema.V58CompanyColumns, "companies_v57");

        Exec(connection, "UPDATE schema_version SET version = 57;");
    }

    /// <summary>
    /// Reverses <see cref="Schema.MigrateV58ToV59"/>: removes the seven <c>companies</c> VAT columns, the five
    /// <c>ledgers</c> VAT columns, the two <c>stock_items</c> columns and the four <c>vouchers</c> CST
    /// declaration-form columns, and stamps <c>schema_version</c> back to 58.
    ///
    /// <para><b>Not a true inverse, and the residual is data, not shape.</b> Every company's VAT registration,
    /// every captured Tax Rate, every counterparty TIN, every item's class of goods and every CST declaration
    /// form number is <b>discarded</b>, because a v58 database has nowhere to keep one — the same honest loss
    /// <see cref="V52ToV51"/> records for the edit log. 🔴 <b>The POSTED FIGURES are untouched</b>: no VAT
    /// amount was ever a separate posted leg in this build (the VAT Computation report is a projection over the
    /// same vouchers every other report reads), so a downgraded book still balances to the paisa and simply
    /// stops being able to say which of its goods were outside GST.</para>
    ///
    /// <para>🔴 <b>ALL FOUR TABLES ARE REBUILT WITH <see cref="RebuildPreservingShape"/>, NOT
    /// <see cref="DropColumns"/>, AND EVERY ONE OF THEM IS AN FK PARENT.</b> <c>companies</c> is the parent of
    /// nearly every table in the schema; <c>ledgers</c> is the parent of <c>entry_lines</c>,
    /// <c>vouchers.party_id</c> and more; <c>stock_items</c> is the parent of every stock-line table; and
    /// <c>vouchers</c> is the parent of <c>entry_lines</c> and <c>voucher_inventory_lines</c>. A
    /// <c>CREATE … AS SELECT</c> rebuild loses the PRIMARY KEY, after which SQLite reports <c>foreign key
    /// mismatch</c> on the next child insert — the measured failure <see cref="V56ToV55"/> documents. There is
    /// no child-table exception to make here, which is why this downgrade has only one idiom.</para>
    ///
    /// <para><b>Order, and why there is no index to drop.</b> v59 adds NO index — see
    /// <see cref="Schema.MigrateV58ToV59"/> for why the one it nearly added was removed — so this downgrade has
    /// only columns to undo. The four tables are independent of one another here (no v59 column is a foreign
    /// key), so they are rebuilt in schema order for readability rather than out of necessity; the indexes each
    /// table already carried are read back and replayed by <see cref="RebuildPreservingShape"/>.</para>
    ///
    /// <para>⚠️ <b>This is the TOP rung.</b> Manufacturing a v58 book out of a CURRENT one runs this FIRST and
    /// the lower rungs after it. Calling <see cref="V58ToV57"/> alone on a v59 file stamps the marker 57 while
    /// the v59 columns are still there, which is a lie the next open cannot detect.</para>
    /// </summary>
    public static void V59ToV58(SqliteConnection connection)
    {
        ArgumentNullException.ThrowIfNull(connection);

        RebuildPreservingShape(connection, "vouchers", Schema.V59CstVoucherColumns, "vouchers_v58");
        RebuildPreservingShape(connection, "stock_items", Schema.V59VatStockItemColumns, "stock_items_v58");
        RebuildPreservingShape(connection, "ledgers", Schema.V59VatLedgerColumns, "ledgers_v58");
        RebuildPreservingShape(connection, "companies", Schema.V59VatCompanyColumns, "companies_v58");

        Exec(connection, "UPDATE schema_version SET version = 58;");
    }

    /// <summary>
    /// Reverses <see cref="Schema.MigrateV59ToV60"/> (census 2.2 Group behavioural flags / 3.6 Alternate units per
    /// stock item): removes the five <c>groups</c> columns (<see cref="Schema.V60GroupBehaviourColumns"/>) and the
    /// two <c>stock_items</c> columns (<see cref="Schema.V60AlternateUnitColumns"/>), then stamps
    /// <c>schema_version</c> back to 59.
    ///
    /// <para><b>Not a true inverse, and the residual is data, not shape.</b> Every behavioural flag an operator
    /// set on a group and every Alternate Unit and conversion factor keyed on a stock item is <b>discarded</b>,
    /// because a v59 database has nowhere to keep one — the same honest loss <see cref="V52ToV51"/> records for
    /// the edit log. 🔴 <b>NO POSTED FIGURE AND NO STOCK QUANTITY MOVES.</b> The alternate unit never stored a
    /// quantity in the first place — every stock line keeps the single base-unit column it always had, and the
    /// alternate expression was derived on display — so a downgraded book holds exactly the same stock, valued
    /// identically, and simply stops being able to say it in boxes. Likewise <c>groups.nature</c> is NOT touched:
    /// a custom primary group created at v60 survives the downgrade with its nature intact and keeps printing on
    /// the same side of the Balance Sheet; only <c>affects_gross_profits</c> goes, which for such a group returns
    /// its ledgers to the below-the-line half of the Profit &amp; Loss.</para>
    ///
    /// <para>🔴 <b>BOTH TABLES ARE REBUILT WITH <see cref="RebuildPreservingShape"/>, NOT
    /// <see cref="DropColumns"/>, AND BOTH ARE FK PARENTS.</b> <c>groups</c> is the parent of
    /// <c>ledgers.group_id</c>, of its own <c>parent_id</c> self-reference and of
    /// <c>companies.profit_and_loss_head_id</c>; <c>stock_items</c> is the parent of every stock-line table. A
    /// <c>CREATE … AS SELECT</c> rebuild loses the PRIMARY KEY, after which SQLite reports <c>foreign key
    /// mismatch</c> on the next child insert — the measured failure <see cref="V56ToV55"/> documents.</para>
    ///
    /// <para><b>Order, and why there is no index to drop.</b> v60 adds NO index, so this downgrade has only
    /// columns to undo. <c>stock_items</c> is rebuilt first purely for readability; the two tables are
    /// independent of one another here (<c>alternate_unit_id</c> references <c>units</c>, not <c>groups</c>), and
    /// the indexes each table already carried are read back and replayed by
    /// <see cref="RebuildPreservingShape"/>.</para>
    ///
    /// <para>⚠️ <b>This is the TOP rung.</b> Manufacturing a v59 book out of a CURRENT one runs this FIRST and
    /// the lower rungs after it. Calling <see cref="V59ToV58"/> alone on a v60 file stamps the marker 58 while
    /// the v60 columns are still there, which is a lie the next open cannot detect.</para>
    /// </summary>
    public static void V60ToV59(SqliteConnection connection)
    {
        ArgumentNullException.ThrowIfNull(connection);

        RebuildPreservingShape(connection, "stock_items", Schema.V60AlternateUnitColumns, "stock_items_v59");
        RebuildPreservingShape(connection, "groups", Schema.V60GroupBehaviourColumns, "groups_v59");

        Exec(connection, "UPDATE schema_version SET version = 59;");
    }

    /// <summary>
    /// Reverses <see cref="Schema.MigrateV60ToV61"/> (census 6.23 Multiple GSTIN registrations / 6.25 GST
    /// Classification master): drops the two tables (<see cref="Schema.V61Tables"/>) with their indexes, the one
    /// <c>vouchers</c> column (<see cref="Schema.V61VoucherColumns"/>) and the one <c>companies</c> column
    /// (<see cref="Schema.V61CompanyColumns"/>), then stamps <c>schema_version</c> back to 60.
    ///
    /// <para>🔴 <b>NOT A TRUE INVERSE, AND HERE THE RESIDUAL IS THE ONE THAT MATTERS — READ IT BEFORE RUNNING
    /// THIS ON A REAL BOOK.</b> Every <b>additional</b> GST registration is discarded, because a v60 database has
    /// nowhere to keep one. Consequently every voucher that was recorded under an additional registration
    /// <b>silently reverts to the company's own first registration</b> — that is what dropping
    /// <c>gst_registration_id</c> means, since NULL is the primary. <b>No posted figure moves and no ledger
    /// balance changes</b> (the attribution never entered a debit or a credit; it selects which return a voucher
    /// folds into), but the downgraded book will then fold every supply into ONE return again. A book that has
    /// actually filed under two GSTINs must not be downgraded and then re-filed from.</para>
    ///
    /// <para><b>Every GST Classification is discarded, and nothing computed changes.</b> A classification was
    /// only ever COPIED onto a master at assignment time (see <c>GstClassification</c>), so the masters keep the
    /// HSN/SAC and rates they were given and every document continues to compute exactly as it did; what is lost
    /// is the reusable definition, not any rate in use.</para>
    ///
    /// <para>🔴 <b>ORDER IS LOAD-BEARING, AND <c>vouchers</c> MUST BE REBUILT BEFORE <c>gst_registrations</c> IS
    /// DROPPED.</b> <c>vouchers.gst_registration_id</c> carries a <c>REFERENCES gst_registrations(id)</c> clause,
    /// so dropping the parent first would leave <c>vouchers</c> declaring a foreign key to a table that no longer
    /// exists — which SQLite tolerates silently until the next write, then reports as
    /// <c>foreign key mismatch</c>. Rebuilding <c>vouchers</c> without the column removes the clause along with
    /// it, after which the parent is free.</para>
    ///
    /// <para>🔴 <b>AND BOTH REBUILDS USE <see cref="RebuildPreservingShape"/>, NOT <see cref="DropColumns"/>.</b>
    /// <c>vouchers</c> is the FK PARENT of <c>entry_lines.voucher_id</c> and of most stock-line and statutory
    /// child tables, and <c>companies</c> is the parent of nearly everything; a <c>CREATE … AS SELECT</c> rebuild
    /// loses the PRIMARY KEY, after which SQLite reports <c>foreign key mismatch</c> on the next child insert —
    /// the measured failure <see cref="V56ToV55"/> documents.</para>
    ///
    /// <para>⚠️ <b>This is now the TOP rung.</b> Manufacturing a v60 book out of a CURRENT one runs this FIRST
    /// and the lower rungs after it. Calling <see cref="V60ToV59"/> alone on a v61 file stamps the marker 59
    /// while the v61 objects are still there, which is a lie the next open cannot detect.</para>
    /// </summary>
    public static void V61ToV60(SqliteConnection connection)
    {
        ArgumentNullException.ThrowIfNull(connection);

        // The child column FIRST — it names gst_registrations in a REFERENCES clause. See the doc comment.
        RebuildPreservingShape(connection, "vouchers", Schema.V61VoucherColumns, "vouchers_v60");
        RebuildPreservingShape(connection, "companies", Schema.V61CompanyColumns, "companies_v60");

        foreach (var index in Schema.V61Indexes) Exec(connection, $"DROP INDEX IF EXISTS {index};");
        foreach (var table in Schema.V61Tables) Exec(connection, $"DROP TABLE IF EXISTS {table};");

        Exec(connection, "UPDATE schema_version SET version = 60;");
    }

    /// <summary>
    /// Rebuilds <paramref name="table"/> without <paramref name="drop"/>, <b>reconstructing its declaration</b>
    /// from <c>PRAGMA table_info</c> and <c>PRAGMA foreign_key_list</c> rather than inferring it from a
    /// <c>CREATE … AS SELECT</c>. Unlike <see cref="DropColumns"/> this preserves the <b>primary key</b>, the
    /// <b>NOT NULL</b>s, the column <b>DEFAULT</b>s and the table's own outgoing <b>foreign keys</b>.
    ///
    /// <para>🔴 <b>Use this, not <see cref="DropColumns"/>, whenever the table is the PARENT of a foreign key.</b>
    /// SQLite requires a referenced key to be a PRIMARY KEY or UNIQUE; a rebuild that loses the PK leaves every
    /// child table's FK dangling and the next child insert fails with <c>foreign key mismatch</c> — see
    /// <see cref="V56ToV55"/> for the measured instance. <see cref="DropColumns"/> is kept as-is for the child
    /// tables the earlier downgrades use it on, where the residual is documented and harmless.</para>
    ///
    /// <para>Indexes are read back from <c>sqlite_master</c> before the swap and replayed after it, exactly as
    /// <see cref="DropColumns"/> does, skipping implicit (UNIQUE/PK) indexes and any index naming a dropped
    /// column. CHECK constraints and multi-column primary keys are NOT reconstructed — this schema has neither on
    /// any table this method is used for, and a silent partial reconstruction would be worse than a loud gap, so
    /// a composite PK is refused rather than quietly flattened.</para>
    /// </summary>
    private static void RebuildPreservingShape(
        SqliteConnection connection, string table, IReadOnlyList<string> drop, string scratchName)
    {
        var columns = ColumnDefinitions(connection, table);
        var keep = columns
            .Where(c => !drop.Contains(c.Name, StringComparer.OrdinalIgnoreCase))
            .ToList();
        if (keep.Count == 0 || keep.Count == columns.Count) return;

        if (columns.Count(c => c.PrimaryKey > 0) > 1)
            throw new NotSupportedException(
                $"Table \"{table}\" has a composite primary key, which this rebuild does not reconstruct. "
                + "Reconstructing it partially would silently change the table's shape.");

        var foreignKeys = ForeignKeys(connection, table)
            .Where(fk => !drop.Contains(fk.From, StringComparer.OrdinalIgnoreCase))
            .ToList();

        var declarations = keep.Select(c =>
        {
            var sb = new StringBuilder();
            sb.Append('"').Append(c.Name).Append("\" ").Append(c.Type);
            if (c.PrimaryKey > 0) sb.Append(" PRIMARY KEY");
            if (c.NotNull) sb.Append(" NOT NULL");
            if (c.Default is not null) sb.Append(" DEFAULT ").Append(c.Default);
            var fk = foreignKeys.FirstOrDefault(f =>
                string.Equals(f.From, c.Name, StringComparison.OrdinalIgnoreCase));
            if (fk is not null) sb.Append(" REFERENCES \"").Append(fk.Table).Append("\"(\"").Append(fk.To).Append("\")");
            return sb.ToString();
        }).ToList();

        var columnList = string.Join(", ", keep.Select(c => $"\"{c.Name}\""));
        var indexes = IndexDefinitions(connection, table)
            .Where(sql => !drop.Any(d => sql.Contains(d, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        Exec(connection, "PRAGMA foreign_keys=OFF;");
        Exec(connection, $"CREATE TABLE {scratchName} (\n  {string.Join(",\n  ", declarations)}\n);");
        Exec(connection, $"INSERT INTO {scratchName} ({columnList}) SELECT {columnList} FROM \"{table}\";");
        Exec(connection, $"DROP TABLE \"{table}\";");
        Exec(connection, $"ALTER TABLE {scratchName} RENAME TO \"{table}\";");
        foreach (var sql in indexes) Exec(connection, sql + ";");
        Exec(connection, "PRAGMA foreign_keys=ON;");
    }

    private sealed record ColumnDefinition(string Name, string Type, bool NotNull, string? Default, long PrimaryKey);

    private static List<ColumnDefinition> ColumnDefinitions(SqliteConnection connection, string table)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = $"PRAGMA table_info(\"{table}\");";
        var list = new List<ColumnDefinition>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(new ColumnDefinition(
                r.GetString(1),
                r.GetString(2),
                r.GetInt64(3) != 0,
                r.IsDBNull(4) ? null : r.GetString(4),
                r.GetInt64(5)));
        return list;
    }

    private sealed record ForeignKeyDefinition(string Table, string From, string To);

    private static List<ForeignKeyDefinition> ForeignKeys(SqliteConnection connection, string table)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = $"PRAGMA foreign_key_list(\"{table}\");";
        var list = new List<ForeignKeyDefinition>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(new ForeignKeyDefinition(r.GetString(2), r.GetString(3), r.GetString(4)));
        return list;
    }

    /// <summary>
    /// Rebuilds <paramref name="table"/> without <paramref name="drop"/>, via the <c>CREATE … AS SELECT</c> / swap
    /// idiom every downgrade above open-codes. Extracted at v51 only because that version is the first to drop
    /// columns from three tables at once — the behaviour is identical to the open-coded blocks, including the
    /// no-op guard when nothing (or everything) would be kept.
    ///
    /// <para>⚠️ <b>The index round-trip is the one deliberate difference from the open-coded blocks.</b>
    /// <c>DROP TABLE</c> drops every index on the table with it, and v51 is the first downgrade whose tables carry
    /// any (<c>ix_groups_company</c>, <c>ix_stock_groups_company</c> — <c>Schema.cs</c>, search for
    /// <c>CREATE INDEX ix_groups_company</c>). The earlier downgrades silently lost none only because
    /// <c>ledgers</c>/<c>vouchers</c>/<c>voucher_types</c>/<c>companies</c> happened to have none dropped, and
    /// <see cref="V47ToV46"/> says so in terms ("their indexes drop with them"). So the CREATE statements are read
    /// back from <c>sqlite_master</c> before the swap and replayed after it. Implicit indexes (UNIQUE / PRIMARY KEY,
    /// which carry a NULL <c>sql</c>) are skipped — they are part of the constraint loss this rebuild already
    /// documents — as is any index whose definition names one of the dropped columns, which could not be recreated
    /// against the new shape. Owed-review lens 1 finding 2.</para>
    /// </summary>
    private static void DropColumns(
        SqliteConnection connection, string table, IReadOnlyList<string> drop, string scratchName)
    {
        var all = ColumnNames(connection, table);
        var keep = all.Where(c => !drop.Contains(c, StringComparer.OrdinalIgnoreCase)).ToList();
        if (keep.Count == 0 || keep.Count == all.Count) return;

        var columnList = string.Join(", ", keep.Select(c => $"\"{c}\""));
        var indexes = IndexDefinitions(connection, table)
            .Where(sql => !drop.Any(d => sql.Contains(d, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        Exec(connection, "PRAGMA foreign_keys=OFF;");
        Exec(connection, $"""
            CREATE TABLE {scratchName} AS SELECT {columnList} FROM "{table}";
            DROP TABLE "{table}";
            ALTER TABLE {scratchName} RENAME TO "{table}";
            """);
        foreach (var sql in indexes) Exec(connection, sql + ";");
        Exec(connection, "PRAGMA foreign_keys=ON;");
    }

    /// <summary>
    /// The <c>CREATE INDEX</c> statements SQLite holds for <paramref name="table"/>. Rows with a NULL <c>sql</c> are
    /// the implicit indexes SQLite builds for UNIQUE / PRIMARY KEY and cannot be replayed, so they are omitted.
    /// </summary>
    private static List<string> IndexDefinitions(SqliteConnection connection, string table)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText =
            "SELECT sql FROM sqlite_master WHERE type = 'index' AND tbl_name = $t AND sql IS NOT NULL;";
        cmd.Parameters.AddWithValue("$t", table);
        var sqls = new List<string>();
        using var r = cmd.ExecuteReader();
        while (r.Read()) sqls.Add(r.GetString(0));
        return sqls;
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
