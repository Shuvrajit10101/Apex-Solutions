using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Services;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Apex.Persistence.Sqlite.Tests;

/// <summary>
/// Schema v59 → v60 (census 2.2 <b>Group behavioural flags</b> · 3.6 <b>Alternate units per stock item</b>) —
/// five <c>groups</c> columns and two <c>stock_items</c> columns.
///
/// <para>The "genuine v59 database" is manufactured with <see cref="SchemaDowngrade.V60ToV59"/> rather than
/// hand-written DDL, so the migration is exercised against real rows — the idiom every schema test here uses.</para>
///
/// <para>🔴 <b>The load-bearing test in this file is
/// <see cref="A_migrated_v59_book_has_every_flag_off_and_no_alternate_unit"/>, and the reason is not the usual
/// "defaults are tidy".</b> Two of the seven columns would move MONEY or QUANTITY if they were back-filled.
/// <c>affects_gross_profits</c> feeds the Gross Profit line; back-filling the seeded <i>Direct Expenses</i> and
/// <i>Direct Incomes</i> heads to 1 — which is superficially "correct", since that is what they are — would
/// write a live-looking setting into every shipped book that <c>ProfitAndLoss</c> never reads (it matches those
/// four heads by NAME and always has). And <c>alternate_conversion_micro</c> defaulted to anything but NULL —
/// 1 000 000, say — would make every item in every upgraded book claim an alternate unit whose quantity equals
/// its base quantity. Both are invisible to a column-existence test.</para>
///
/// <para>🔴 <b><see cref="Downgrading_to_v59_keeps_the_primary_keys_of_both_rebuilt_tables"/> is the second one
/// that matters.</b> <c>groups</c> and <c>stock_items</c> are BOTH foreign-key parents — <c>ledgers.group_id</c>,
/// <c>groups.parent_id</c>, <c>companies.profit_and_loss_head_id</c>, and every stock-line table — so a
/// <c>CREATE … AS SELECT</c> rebuild that lost their PRIMARY KEY would leave SQLite reporting <c>foreign key
/// mismatch</c> on the next child insert. That is a MEASURED failure in this repository, recorded on
/// <c>SchemaDowngrade.V56ToV55</c>, which is why <c>V60ToV59</c> uses <c>RebuildPreservingShape</c>.</para>
/// </summary>
public sealed class MasterGapsSchemaTests
{
    private static readonly DateOnly FyStart = new(2025, 4, 1);

    // ================================================================= migration parity

    /// <summary>Fails on today's main, where <see cref="Schema.CurrentVersion"/> is 59 and none of these seven
    /// columns exists in either database (and <c>SchemaDowngrade.V60ToV59</c> does not compile).</summary>
    [Fact]
    [Trait("Category", "RoundTrip")]
    public void Migration_v59_to_v60_matches_CreateV1()
    {
        var migratedPath = TempDbFile.NewPath("apex-o1-v59-migrated");
        var freshPath = TempDbFile.NewPath("apex-o1-v60-fresh");
        try
        {
            var fresh = CompanyFactory.CreateSeeded("Fresh Master Co", FyStart);
            using (var store = new SqliteCompanyStore(freshPath)) store.Save(fresh);

            var legacy = CompanyFactory.CreateSeeded("Legacy Master Co", FyStart);
            using (var store = new SqliteCompanyStore(migratedPath)) store.Save(legacy);
            using (var conn = Open(migratedPath)) { SchemaDowngrade.V60ToV59(conn); SqliteConnection.ClearPool(conn); }
            Assert.Equal(59L, ReadScalar(migratedPath, "SELECT version FROM schema_version LIMIT 1;"));

            foreach (var col in Schema.V60GroupBehaviourColumns)
                Assert.DoesNotContain(col, ColumnNames(migratedPath, "groups"));
            foreach (var col in Schema.V60AlternateUnitColumns)
                Assert.DoesNotContain(col, ColumnNames(migratedPath, "stock_items"));

            // Reopen through the production store — the v59 → v60 migration runs.
            using (new SqliteCompanyStore(migratedPath)) { }
            Assert.Equal((long)Schema.CurrentVersion,
                ReadScalar(migratedPath, "SELECT version FROM schema_version LIMIT 1;"));

            // Only the objects THIS migration adds are compared: the downgrade's rebuild is not asked to
            // reproduce pre-existing declarations byte for byte. Whole-schema parity across the full v1→current
            // chain is separately guaranteed by SchemaMigrationEquivalenceTests.
            foreach (var col in Schema.V60GroupBehaviourColumns)
                Assert.Equal(ColumnContract(freshPath, "groups", col),
                             ColumnContract(migratedPath, "groups", col));
            foreach (var col in Schema.V60AlternateUnitColumns)
                Assert.Equal(ColumnContract(freshPath, "stock_items", col),
                             ColumnContract(migratedPath, "stock_items", col));
        }
        finally
        {
            TempDbFile.Delete(migratedPath);
            TempDbFile.Delete(freshPath);
        }
    }

    /// <summary>
    /// 🔴 <b>THE ONE THAT MATTERS.</b> The migration back-fills nothing: every group in a migrated book has all
    /// four flags off and no allocation method — <b>the seeded Direct Expenses and Direct Incomes heads
    /// included</b> — and every stock item has no alternate unit and no conversion factor.
    /// </summary>
    [Fact]
    [Trait("Category", "RoundTrip")]
    public void A_migrated_v59_book_has_every_flag_off_and_no_alternate_unit()
    {
        var path = TempDbFile.NewPath("apex-o1-backfill");
        try
        {
            var legacy = CompanyFactory.CreateSeeded("Backfill Master Co", FyStart);
            var inventory = new InventoryService(legacy);
            var unit = inventory.CreateSimpleUnit("Nos", "Numbers");
            var group = legacy.FindStockGroupByName("Primary") ?? inventory.CreateStockGroup("Primary");
            inventory.CreateStockItem("Plain Widget", group.Id, unit.Id);

            using (var store = new SqliteCompanyStore(path)) store.Save(legacy);
            using (var conn = Open(path)) { SchemaDowngrade.V60ToV59(conn); SqliteConnection.ClearPool(conn); }

            using var reopened = new SqliteCompanyStore(path);
            var loaded = reopened.Load(legacy.Id)!;

            foreach (var g in loaded.Groups)
            {
                Assert.False(g.BehavesLikeSubLedger);
                Assert.False(g.NettBalancesForReporting);
                Assert.False(g.UsedForCalculation);
                Assert.False(g.AffectsGrossProfits);
                Assert.Null(g.PurchaseAllocationMethod);
            }

            // Named explicitly, because "it would be correct to back-fill these two" is the exact temptation
            // this assertion exists to refuse. Nothing reads the flag on them — ProfitAndLoss matches the four
            // trading heads by name — so a 1 here would be a stored setting with no reader.
            Assert.False(loaded.FindGroupByName("Direct Expenses")!.AffectsGrossProfits);
            Assert.False(loaded.FindGroupByName("Direct Incomes")!.AffectsGrossProfits);

            var item = loaded.StockItems.Single(i => i.Name == "Plain Widget");
            Assert.Null(item.AlternateUnitId);
            Assert.Null(item.AlternateUnitConversion);
            Assert.False(AlternateUnitConversion.HasAlternateUnit(item));

            // And the stored cells really are NULL — not a 1.0 factor dressed up as "unset", which would make
            // every derived alternate quantity silently equal the base quantity.
            Assert.Equal(0L, ReadScalar(path,
                "SELECT COUNT(*) FROM stock_items WHERE alternate_conversion_micro IS NOT NULL;"));
            Assert.Equal(0L, ReadScalar(path,
                "SELECT COUNT(*) FROM groups WHERE affects_gross_profits <> 0;"));
        }
        finally { TempDbFile.Delete(path); }
    }

    // ================================================================= round trips

    /// <summary>The five Group behavioural fields survive a save/reload exactly (census 2.2).</summary>
    [Fact]
    [Trait("Category", "RoundTrip")]
    public void Group_behavioural_fields_round_trip()
    {
        var path = TempDbFile.NewPath("apex-o1-group-flags");
        try
        {
            var c = CompanyFactory.CreateSeeded("Group Flags Co", FyStart);
            var service = new GroupService(c);

            // A CUSTOM PRIMARY GROUP — impossible before v60 (defect T1-31), and the only kind of group on which
            // the vendor shows Nature of Group and Does it affect Gross Profits at all.
            var primary = service.CreateGroup(
                "Trading Charges", parentId: null, alias: "TC",
                primaryNature: GroupNature.Expense,
                behaviour: new GroupBehaviour(
                    BehavesLikeSubLedger: true,
                    NettBalancesForReporting: true,
                    UsedForCalculation: true,
                    AffectsGrossProfits: true,
                    PurchaseAllocationMethod: MethodOfAppropriation.ByValue));

            var child = service.CreateGroup(
                "Freight Inward", parentId: primary.Id, alias: null,
                behaviour: new GroupBehaviour(
                    BehavesLikeSubLedger: true,
                    PurchaseAllocationMethod: MethodOfAppropriation.ByQuantity));

            using (var store = new SqliteCompanyStore(path)) store.Save(c);
            using var reopened = new SqliteCompanyStore(path);
            var loaded = reopened.Load(c.Id)!;

            var p = loaded.FindGroup(primary.Id)!;
            Assert.Null(p.ParentId);
            Assert.True(p.IsPrimary);
            Assert.Equal(GroupNature.Expense, p.Nature);
            Assert.True(p.BehavesLikeSubLedger);
            Assert.True(p.NettBalancesForReporting);
            Assert.True(p.UsedForCalculation);
            Assert.True(p.AffectsGrossProfits);
            Assert.Equal(MethodOfAppropriation.ByValue, p.PurchaseAllocationMethod);

            var ch = loaded.FindGroup(child.Id)!;
            Assert.Equal(primary.Id, ch.ParentId);
            Assert.Equal(GroupNature.Expense, ch.Nature);          // derived from the new primary head
            Assert.True(ch.BehavesLikeSubLedger);
            Assert.False(ch.AffectsGrossProfits);                  // refused on a child; never stored
            Assert.Equal(MethodOfAppropriation.ByQuantity, ch.PurchaseAllocationMethod);
        }
        finally { TempDbFile.Delete(path); }
    }

    /// <summary>
    /// The Alternate Unit and its factor survive a save/reload EXACTLY, including a fractional factor
    /// (census 3.6). The fractional case is the one that matters: the factor is stored as micros, so a
    /// conversion like 0.4536 kg per lb must come back as 0.4536 and not as a binary-float approximation of it.
    /// </summary>
    [Fact]
    [Trait("Category", "RoundTrip")]
    public void Alternate_unit_and_a_fractional_factor_round_trip_exactly()
    {
        var path = TempDbFile.NewPath("apex-o1-alt-unit");
        try
        {
            var c = CompanyFactory.CreateSeeded("Alt Unit Co", FyStart);
            var inv = new InventoryService(c);
            var nos = inv.CreateSimpleUnit("Nos", "Numbers");
            var box = inv.CreateSimpleUnit("Box", "Boxes");
            var kg = inv.CreateSimpleUnit("Kg", "Kilograms");
            var lb = inv.CreateSimpleUnit("Lb", "Pounds");
            var group = c.FindStockGroupByName("Primary") ?? inv.CreateStockGroup("Primary");

            var widget = inv.CreateStockItem("Widget", group.Id, nos.Id);
            inv.SetAlternateUnit(widget, box.Id, 10m);

            var powder = inv.CreateStockItem("Powder", group.Id, kg.Id);
            inv.SetAlternateUnit(powder, lb.Id, 0.4536m);

            using (var store = new SqliteCompanyStore(path)) store.Save(c);
            using var reopened = new SqliteCompanyStore(path);
            var loaded = reopened.Load(c.Id)!;

            var w = loaded.StockItems.Single(i => i.Name == "Widget");
            Assert.Equal(box.Id, w.AlternateUnitId);
            Assert.Equal(10m, w.AlternateUnitConversion);
            Assert.Equal("1 Box = 10 Nos", AlternateUnitConversion.DescribeConversion(w, loaded));

            var p = loaded.StockItems.Single(i => i.Name == "Powder");
            Assert.Equal(lb.Id, p.AlternateUnitId);
            Assert.Equal(0.4536m, p.AlternateUnitConversion);      // exact, not 0.45360000000000001
            Assert.Equal("1 Lb = 0.4536 Kg", AlternateUnitConversion.DescribeConversion(p, loaded));
        }
        finally { TempDbFile.Delete(path); }
    }

    // ================================================================= downgrade

    /// <summary>
    /// 🔴 The downgrade rebuilds <c>groups</c> and <c>stock_items</c> — both FK PARENTS — and both must keep
    /// their PRIMARY KEY. Losing it is the measured <c>foreign key mismatch</c> failure recorded on
    /// <see cref="SchemaDowngrade.V56ToV55"/>; the proof here is that a child row can still be inserted after
    /// the rebuild, which is the operation that actually fails when the key is gone.
    /// </summary>
    [Fact]
    [Trait("Category", "RoundTrip")]
    public void Downgrading_to_v59_keeps_the_primary_keys_of_both_rebuilt_tables()
    {
        var path = TempDbFile.NewPath("apex-o1-downgrade-pk");
        try
        {
            var c = CompanyFactory.CreateSeeded("Downgrade Co", FyStart);
            var inv = new InventoryService(c);
            var nos = inv.CreateSimpleUnit("Nos", "Numbers");
            var group = c.FindStockGroupByName("Primary") ?? inv.CreateStockGroup("Primary");
            inv.CreateStockItem("Widget", group.Id, nos.Id);

            var groupCountBefore = c.Groups.Count;
            using (var store = new SqliteCompanyStore(path)) store.Save(c);

            using (var conn = Open(path))
            {
                SchemaDowngrade.V60ToV59(conn);

                Assert.True(HasPrimaryKey(conn, "groups"));
                Assert.True(HasPrimaryKey(conn, "stock_items"));

                // The operation that actually breaks when a parent's PK is lost: a child insert naming it.
                using (var fk = conn.CreateCommand())
                {
                    fk.CommandText = "PRAGMA foreign_keys=ON;";
                    fk.ExecuteNonQuery();
                }
                using (var probe = conn.CreateCommand())
                {
                    probe.CommandText = """
                        INSERT INTO ledgers (id, company_id, name, group_id, opening_balance_paisa,
                                             opening_is_debit, is_predefined, maintain_bill_by_bill,
                                             cost_applicable)
                        SELECT lower(hex(randomblob(16))), company_id, 'FK Probe Ledger', id, 0, 1, 0, 0, 0
                        FROM groups LIMIT 1;
                        """;
                    probe.ExecuteNonQuery();     // throws "foreign key mismatch" if the PK was lost
                }
                SqliteConnection.ClearPool(conn);
            }

            Assert.Equal(59L, ReadScalar(path, "SELECT version FROM schema_version LIMIT 1;"));
            foreach (var col in Schema.V60GroupBehaviourColumns)
                Assert.DoesNotContain(col, ColumnNames(path, "groups"));
            foreach (var col in Schema.V60AlternateUnitColumns)
                Assert.DoesNotContain(col, ColumnNames(path, "stock_items"));

            // No row was lost by either rebuild (the probe ledger is the only addition).
            Assert.Equal((long)groupCountBefore + 1, ReadScalar(path, "SELECT COUNT(*) FROM groups;"));
            Assert.Equal(1L, ReadScalar(path, "SELECT COUNT(*) FROM stock_items;"));
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

    private static long ReadScalar(string path, string sql)
    {
        using var conn = Open(path);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        var value = Convert.ToInt64(cmd.ExecuteScalar());
        SqliteConnection.ClearPool(conn);
        return value;
    }

    private static List<string> ColumnNames(string path, string table)
    {
        using var conn = Open(path);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"PRAGMA table_info(\"{table}\");";
        var names = new List<string>();
        using (var r = cmd.ExecuteReader())
            while (r.Read()) names.Add(r.GetString(1));
        SqliteConnection.ClearPool(conn);
        return names;
    }

    private static bool HasPrimaryKey(SqliteConnection conn, string table)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"PRAGMA table_info(\"{table}\");";
        using var r = cmd.ExecuteReader();
        while (r.Read())
            if (r.GetInt64(5) > 0) return true;
        return false;
    }

    /// <summary>The column's whole contract — name / type / NOT NULL / DEFAULT / PK — the same tuple
    /// <c>SchemaMigrationEquivalenceTests</c> compares.</summary>
    private static string ColumnContract(string path, string table, string column)
    {
        using var conn = Open(path);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"PRAGMA table_info(\"{table}\");";
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            if (!string.Equals(r.GetString(1), column, StringComparison.OrdinalIgnoreCase)) continue;
            var contract =
                $"{r.GetString(1)}|{r.GetString(2)}|{r.GetInt64(3)}|{(r.IsDBNull(4) ? "<null>" : r.GetString(4))}|{r.GetInt64(5)}";
            SqliteConnection.ClearPool(conn);
            return contract;
        }
        SqliteConnection.ClearPool(conn);
        return $"<missing:{table}.{column}>";
    }
}
