using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Services;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Apex.Persistence.Sqlite.Tests;

/// <summary>
/// Schema v69 → v70 (census row <b>2.9</b>, the open clause <i>"no nested budget"</i>): <c>budgets.under_id</c> is
/// re-pointed from <c>REFERENCES groups(id)</c> to <c>REFERENCES budgets(id)</c> by a table rebuild.
///
/// <para><b>Fidelity (R7, ruling 14).</b> help.tallysolutions.com/budgets-tally/, November 3, 2025 version
/// (web.archive.org snapshot 20260310093634, read by content 2026-11-22): <i>"you need to create the budget under
/// Primary from the List of Budgets … under the main Department Budget. This shows the budget structure in a
/// hierarchical form."</i> The live page's conflicting "predefined groups" wording is IV-325.</para>
///
/// <para><b>What this file proves:</b> (1) a v69 book, carrying a legacy GROUP id in <c>under_id</c>, climbs
/// with every budget and line intact, the group id cleared, the PK, the index and zero FK violations; (2) the
/// migrated <c>sqlite_master.sql</c> for the table AND its index is byte-identical to a fresh book's; (3) the
/// downgrade restores the v69 target and keeps the PK of an FK parent; (4) a hierarchy whose parent sits LATER in
/// <c>Company.Budgets</c> than its child is written and read back off a fresh handle in the saved order; (5) the
/// stored FK now refuses a GROUP id.</para>
///
/// <para><b>Wording (IV wave 12d):</b> the v69 book in (1) is a v70 book taken down by <c>SchemaDowngrade.V70ToV69</c>,
/// not one written by the v69 binary; the wave 12c review matched the downgrade's DDL text to the v69 binary's
/// <c>sqlite_master</c> and climbed a book that binary wrote, outside this suite. The test name says which it is.</para>
///
/// <para><b>Mutations (run 2026-11-22; each reverted with <c>git checkout</c> and confirmed empty):</b>
/// (a) deleting the <c>if (version == 69)</c> rung → <see cref="A_v69_book_downgraded_from_v70_climbs_back_with_every_budget_and_line_and_the_legacy_group_id_cleared"/>
/// RED ("schema version 69 is not supported"); (b) copying <c>under_id</c> instead of <c>NULL</c> in
/// <c>MigrateV69ToV70</c> → the same test RED (the in-transaction orphan check throws); (c) one extra space in the
/// migration's <c>CREATE TABLE budgets</c> → <see cref="Migration_v69_to_v70_is_byte_identical_to_CreateV1_for_the_table_and_its_index"/>
/// RED; (d) removing the store's second-pass parent write → <see cref="A_parent_saved_after_its_child_is_written_and_read_back_off_a_fresh_handle_in_saved_order"/>
/// RED (raw under_id NULL); (e) <c>CreateV1</c> back to <c>REFERENCES groups(id)</c> →
/// <see cref="The_stored_foreign_key_refuses_a_group_id_as_a_budget_parent"/> RED (the save succeeds).</para>
/// </summary>
public sealed class BudgetParentV70SchemaTests
{
    private static readonly DateOnly FyStart = new(2024, 4, 1);
    private static readonly DateOnly FyEnd = new(2025, 3, 31);

    private sealed record Seeded(Company Company, Guid Dept, Guid Sales, Guid Ops, Guid GroupId);

    /// <summary>Three budgets with lines on a group, a ledger and a cost centre; Sales is filed under Dept, and Sales
    /// is added BEFORE Dept so the parent sits later in the collection.</summary>
    private static Seeded Seed(string name)
    {
        var c = CompanyFactory.CreateSeeded(name, FyStart);
        var indirect = c.Groups.Single(g => g.Name == "Indirect Expenses");
        var ledger = c.Ledgers.First();
        var category = c.CostCategories.First();
        var centre = new CostCentre(Guid.NewGuid(), "V70 North", category.Id);
        c.AddCostCentre(centre);

        var dept = new Budget(Guid.NewGuid(), "Department Budget", FyStart, FyEnd);
        dept.AddLine(BudgetLine.ForGroup(indirect.Id, BudgetType.OnNettTransactions, new Money(12_345.67m)));
        var sales = new Budget(Guid.NewGuid(), "Sales Department Budget", FyStart, FyEnd, underId: dept.Id);
        sales.AddLine(BudgetLine.ForLedger(ledger.Id, BudgetType.OnClosingBalance, new Money(2_500.05m)));
        sales.AddLine(BudgetLine.ForCostCentre(centre.Id, BudgetType.OnNettTransactions, new Money(777.77m)));
        var ops = new Budget(Guid.NewGuid(), "Ops Budget", FyStart, FyEnd);
        ops.AddLine(BudgetLine.ForLedger(ledger.Id, BudgetType.OnNettTransactions, new Money(1.01m)));
        c.AddBudget(sales);
        c.AddBudget(dept);
        c.AddBudget(ops);
        return new Seeded(c, dept.Id, sales.Id, ops.Id, indirect.Id);
    }

    [Fact]
    [Trait("Category", "RoundTrip")]
    public void A_v69_book_downgraded_from_v70_climbs_back_with_every_budget_and_line_and_the_legacy_group_id_cleared()
    {
        var path = TempDbFile.NewPath("apex-v70-real");
        try
        {
            var s = Seed("Legacy Budget Parent Co");
            using (var store = new SqliteCompanyStore(path)) store.Save(s.Company);
            var linesBefore = Rows(path, "SELECT budget_id || '|' || line_order || '|' || amount_paisa FROM budget_lines ORDER BY budget_id, line_order;");
            Assert.Equal(4, linesBefore.Count);
            var orderBefore = Rows(path, "SELECT name FROM budgets ORDER BY rowid;");

            using (var conn = Open(path)) { SchemaDowngrade.V70ToV69(conn); SqliteConnection.ClearPool(conn); }
            Assert.Equal(69L, ReadScalar(path, "SELECT version FROM schema_version LIMIT 1;"));
            Assert.Equal("groups", UnderIdTarget(path));
            // A v69 value: Ops is filed under a GROUP, the shape v69 could store (the FK then pointed at groups).
            Exec(path, $"UPDATE budgets SET under_id = '{s.GroupId:D}' WHERE id = '{s.Ops:D}';");
            Assert.Equal(1L, ReadScalar(path, "SELECT COUNT(*) FROM budgets WHERE under_id IS NOT NULL;"));

            Company reloaded;
            using (var store = new SqliteCompanyStore(path)) reloaded = store.Load(s.Company.Id)!;

            Assert.Equal(70L, ReadScalar(path, "SELECT version FROM schema_version LIMIT 1;"));
            Assert.Equal("budgets", UnderIdTarget(path));
            Assert.Equal(0L, ReadScalar(path, "SELECT COUNT(*) FROM budgets WHERE under_id IS NOT NULL;"));
            Assert.Equal(linesBefore, Rows(path, "SELECT budget_id || '|' || line_order || '|' || amount_paisa FROM budget_lines ORDER BY budget_id, line_order;"));
            Assert.Equal(orderBefore, Rows(path, "SELECT name FROM budgets ORDER BY rowid;"));
            Assert.Equal(new List<string> { "id" }, PrimaryKeyColumns(path, "budgets"));
            Assert.Contains("ix_budgets_company", IndexNames(path, "budgets"));
            Assert.Equal(0L, ReadScalar(path,
                $"SELECT COUNT(*) FROM sqlite_master WHERE name = '{Schema.V70BudgetsScratchTable}';"));
            Assert.Equal(0, ForeignKeyViolationCount(path));
            Assert.Equal("ok", IntegrityCheck(path));

            Assert.Equal(3, reloaded.Budgets.Count);
            Assert.All(reloaded.Budgets, b => Assert.Null(b.UnderId));   // the hierarchy did not survive v69
            Assert.Equal(2, reloaded.FindBudget(s.Sales)!.Lines.Count);
            Assert.Equal(777.77m, reloaded.FindBudget(s.Sales)!.Lines.Single(l => l.CostCentreId is not null).Amount.Amount);

            // And the climbed book takes a hierarchy and saves cleanly.
            reloaded.FindBudget(s.Sales)!.UnderId = s.Dept;
            using (var store = new SqliteCompanyStore(path)) store.Save(reloaded);
            Assert.Equal(s.Dept.ToString("D"), ReadText(path, $"SELECT under_id FROM budgets WHERE id = '{s.Sales:D}';"));
            Assert.Equal(0, ForeignKeyViolationCount(path));
        }
        finally { TempDbFile.Delete(path); }
    }

    [Fact]
    [Trait("Category", "RoundTrip")]
    public void Migration_v69_to_v70_is_byte_identical_to_CreateV1_for_the_table_and_its_index()
    {
        var migratedPath = TempDbFile.NewPath("apex-v70-migrated");
        var freshPath = TempDbFile.NewPath("apex-v70-fresh");
        try
        {
            using (var store = new SqliteCompanyStore(freshPath)) store.Save(Seed("Fresh Budget Parent Co").Company);
            using (var store = new SqliteCompanyStore(migratedPath)) store.Save(Seed("Migrated Budget Parent Co").Company);

            using (var conn = Open(migratedPath)) { SchemaDowngrade.V70ToV69(conn); SqliteConnection.ClearPool(conn); }
            Assert.Equal("groups", UnderIdTarget(migratedPath));
            using (new SqliteCompanyStore(migratedPath)) { }

            const string tableSql = "SELECT sql FROM sqlite_master WHERE type = 'table' AND name = 'budgets';";
            const string indexSql = "SELECT sql FROM sqlite_master WHERE type = 'index' AND name = 'ix_budgets_company';";
            Assert.Equal(ReadText(freshPath, tableSql), ReadText(migratedPath, tableSql));
            Assert.Equal(ReadText(freshPath, indexSql), ReadText(migratedPath, indexSql));
            Assert.Contains("REFERENCES budgets(id)", ReadText(freshPath, tableSql));
            Assert.Equal(70, Schema.CurrentVersion);
        }
        finally
        {
            TempDbFile.Delete(migratedPath);
            TempDbFile.Delete(freshPath);
        }
    }

    [Fact]
    [Trait("Category", "RoundTrip")]
    public void The_v70_downgrade_restores_the_group_target_and_keeps_the_primary_key_index_and_every_line()
    {
        var path = TempDbFile.NewPath("apex-v70-down");
        try
        {
            var s = Seed("Downgrade Budget Parent Co");
            using (var store = new SqliteCompanyStore(path)) store.Save(s.Company);
            Assert.Equal(1L, ReadScalar(path, "SELECT COUNT(*) FROM budgets WHERE under_id IS NOT NULL;"));

            using (var conn = Open(path)) { SchemaDowngrade.V70ToV69(conn); SqliteConnection.ClearPool(conn); }

            Assert.Equal("groups", UnderIdTarget(path));
            Assert.Equal(new List<string> { "id" }, PrimaryKeyColumns(path, "budgets"));
            Assert.Contains("ix_budgets_company", IndexNames(path, "budgets"));
            Assert.Equal(0L, ReadScalar(path, "SELECT COUNT(*) FROM budgets WHERE under_id IS NOT NULL;"));
            Assert.Equal(4L, ReadScalar(path, "SELECT COUNT(*) FROM budget_lines;"));
            Assert.Equal(0, ForeignKeyViolationCount(path));
            Assert.Equal("ok", IntegrityCheck(path));
        }
        finally { TempDbFile.Delete(path); }
    }

    [Fact]
    [Trait("Category", "RoundTrip")]
    public void A_parent_saved_after_its_child_is_written_and_read_back_off_a_fresh_handle_in_saved_order()
    {
        var path = TempDbFile.NewPath("apex-v70-tree");
        try
        {
            var s = Seed("Tree Budget Parent Co");
            var savedOrder = s.Company.Budgets.Select(b => b.Name).ToList();
            Assert.Equal("Sales Department Budget", savedOrder[0]);   // the child precedes its parent
            using (var store = new SqliteCompanyStore(path)) store.Save(s.Company);

            Assert.Equal(s.Dept.ToString("D"), ReadText(path, $"SELECT under_id FROM budgets WHERE id = '{s.Sales:D}';"));
            Assert.Equal(1L, ReadScalar(path, "SELECT COUNT(*) FROM budgets WHERE under_id IS NOT NULL;"));

            Company reloaded;
            using (var store = new SqliteCompanyStore(path)) reloaded = store.Load(s.Company.Id)!;
            Assert.Equal(savedOrder, reloaded.Budgets.Select(b => b.Name).ToList());
            Assert.Equal(s.Dept, reloaded.FindBudget(s.Sales)!.UnderId);
            Assert.Null(reloaded.FindBudget(s.Dept)!.UnderId);
            Assert.Null(reloaded.FindBudget(s.Ops)!.UnderId);
            Assert.Equal(
                new[] { ("Department Budget", 0), ("Sales Department Budget", 1), ("Ops Budget", 0) },
                BudgetHierarchy.TreeOrder(reloaded).Select(t => (t.Budget.Name, t.Depth)).ToArray());

            // Re-save of the reloaded book (delete-all + re-insert of a self-referencing table) stays clean.
            using (var store = new SqliteCompanyStore(path)) store.Save(reloaded);
            Assert.Equal(0, ForeignKeyViolationCount(path));
        }
        finally { TempDbFile.Delete(path); }
    }

    [Fact]
    [Trait("Category", "RoundTrip")]
    public void The_stored_foreign_key_refuses_a_group_id_as_a_budget_parent()
    {
        var path = TempDbFile.NewPath("apex-v70-fk");
        try
        {
            var s = Seed("FK Budget Parent Co");
            using (var store = new SqliteCompanyStore(path)) store.Save(s.Company);

            // A group id where a budget id belongs — the v69 shape. The v70 FK must refuse it.
            s.Company.FindBudget(s.Ops)!.UnderId = s.GroupId;
            using (var store = new SqliteCompanyStore(path))
                Assert.ThrowsAny<SqliteException>(() => store.Save(s.Company));

            // The failed save rolled back: the book on disk still has exactly the one valid parent.
            Assert.Equal(1L, ReadScalar(path, "SELECT COUNT(*) FROM budgets WHERE under_id IS NOT NULL;"));
            Assert.Equal(3L, ReadScalar(path, "SELECT COUNT(*) FROM budgets;"));
        }
        finally { TempDbFile.Delete(path); }
    }

    // ================================================================= helpers

    private static SqliteConnection Open(string path)
    {
        var conn = new SqliteConnection(
            new SqliteConnectionStringBuilder { DataSource = path, Mode = SqliteOpenMode.ReadWrite }.ToString());
        conn.Open();
        return conn;
    }

    private static void Exec(string path, string sql)
    {
        using var conn = Open(path);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
        SqliteConnection.ClearPool(conn);
    }

    private static long ReadScalar(string path, string sql)
    {
        using var conn = Open(path);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        var value = Convert.ToInt64(cmd.ExecuteScalar());
        SqliteConnection.ClearPool(conn);
        return value;
    }

    private static string ReadText(string path, string sql)
    {
        using var conn = Open(path);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        var value = Convert.ToString(cmd.ExecuteScalar()) ?? string.Empty;
        SqliteConnection.ClearPool(conn);
        return value;
    }

    private static List<string> Rows(string path, string sql)
    {
        using var conn = Open(path);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        var list = new List<string>();
        using (var r = cmd.ExecuteReader()) while (r.Read()) list.Add(r.GetString(0));
        SqliteConnection.ClearPool(conn);
        return list;
    }

    /// <summary>The table <c>budgets.under_id</c> REFERENCES, read off <c>PRAGMA foreign_key_list</c>.</summary>
    private static string UnderIdTarget(string path)
    {
        using var conn = Open(path);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "PRAGMA foreign_key_list(budgets);";
        string? target = null;
        using (var r = cmd.ExecuteReader())
            while (r.Read())
                if (r.GetString(3) == "under_id") target = r.GetString(2);
        SqliteConnection.ClearPool(conn);
        return target ?? "(none)";
    }

    private static List<string> PrimaryKeyColumns(string path, string table)
    {
        using var conn = Open(path);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"PRAGMA table_info({table});";
        var list = new List<string>();
        using (var r = cmd.ExecuteReader()) while (r.Read()) if (r.GetInt64(5) > 0) list.Add(r.GetString(1));
        SqliteConnection.ClearPool(conn);
        return list;
    }

    private static List<string> IndexNames(string path, string table)
    {
        using var conn = Open(path);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT name FROM sqlite_master WHERE type = 'index' AND tbl_name = '{table}' AND sql IS NOT NULL;";
        var list = new List<string>();
        using (var r = cmd.ExecuteReader()) while (r.Read()) list.Add(r.GetString(0));
        SqliteConnection.ClearPool(conn);
        return list;
    }

    private static int ForeignKeyViolationCount(string path)
    {
        using var conn = Open(path);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "PRAGMA foreign_key_check;";
        var n = 0;
        using (var r = cmd.ExecuteReader()) while (r.Read()) n++;
        SqliteConnection.ClearPool(conn);
        return n;
    }

    private static string IntegrityCheck(string path)
    {
        using var conn = Open(path);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "PRAGMA integrity_check;";
        var result = Convert.ToString(cmd.ExecuteScalar()) ?? string.Empty;
        SqliteConnection.ClearPool(conn);
        return result;
    }
}
