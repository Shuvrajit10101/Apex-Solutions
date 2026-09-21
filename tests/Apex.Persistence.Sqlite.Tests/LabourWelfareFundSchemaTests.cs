using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Services;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Apex.Persistence.Sqlite.Tests;

/// <summary>
/// Schema v62 → v63 (census 7.19 <b>Labour Welfare Fund</b>) — the vendor's Computation Information
/// <b>"Effective From"</b> window, as two nullable columns on <c>pay_head_computation_slabs</c>.
///
/// <para>Older databases are manufactured by walking the ladder DOWN from current with
/// <see cref="SchemaDowngrade"/> rather than from hand-written DDL, so the migrations are exercised against real
/// rows — the idiom every schema test here uses.</para>
///
/// <para>🔴 <b>THE LADDER NO LONGER SKIPS 62, AND THE HISTORY MATTERS.</b> v62 (Voucher Class) was built on a
/// sibling branch in the same wave and had not landed on <c>origin/main</c> when this one was cut, so ruling 22
/// assigned this track <b>63</b> and the step originally spanned 61 → 63 in one move, guarded on
/// <c>version == 61</c>. v62 has since landed (<c>origin/main</c> 973d933) and its step now sits directly above
/// this one, so the guard has been re-pointed to <c>version == 62</c>, the constant renamed
/// <c>Schema.MigrateV62ToV63</c>, and the downgrade rung renamed <see cref="SchemaDowngrade.V63ToV62"/> — it
/// stamps <b>62</b>, because reversing v62 is <see cref="SchemaDowngrade.V62ToV61"/>'s separate job.</para>
///
/// <para>🔴 <b>THE TEST THAT PROVES THE REPAIR IS
/// <see cref="A_v61_book_climbs_the_whole_ladder_and_gets_BOTH_v62_and_v63_objects"/>.</b> A ladder that compiles
/// is not a ladder that migrates. Every other test here manufactures its book by DOWNGRADING, so a guard pointed
/// at the wrong rung can hide behind a matched pair of wrong steps; that one climbs UP from v61 through the
/// production store and asserts that the v62 tables AND the v63 columns both exist at the top, then walks back
/// down and asserts the shape at each rung. It is the only test in this file that would have failed for the
/// original 61 → 63 guard AND for the naive fix of simply ordering the v63 step first.</para>
///
/// <para>🔴 <b>THE LOAD-BEARING TEST IS
/// <see cref="A_dated_slab_survives_the_store_round_trip_and_still_fires_in_one_month_only"/>, AND IT IS NOT
/// "THE COLUMN EXISTS".</b> Persisting the window but dropping it on the way back out would produce a book that
/// computes a DIFFERENT payslip after a save-and-reopen than it did before one: a Labour Welfare Fund deduction
/// configured for December alone would quietly become a monthly deduction the next time the book was opened.
/// A column-existence test cannot see that, so the round trip is asserted through the <b>computed amount</b>,
/// not through the stored string.</para>
/// </summary>
public sealed class LabourWelfareFundSchemaTests
{
    private static readonly DateOnly FyStart = new(2026, 4, 1);

    /// <summary>A fixture contribution. 🔴 NOT any State's Labour Welfare Fund rate — none is seeded or asserted
    /// anywhere in this product, because LWF amounts are per-State law and inventing one on a payroll path is the
    /// defect class that already cost this project a Tier 0 fix.</summary>
    private const decimal OperatorTypedContribution = 100m;

    // ================================================================= migration parity

    /// <summary>
    /// The two columns a migrated book ends up with are byte-identical in contract (name / type / NOT NULL /
    /// DEFAULT / PK) to the ones <c>CreateV1</c> gives a fresh book — the equivalence rule this repository treats
    /// as absolute. Fails on any build whose <c>CurrentVersion</c> predates the window, where
    /// <c>SchemaDowngrade.V63ToV62</c> does not even compile.
    /// </summary>
    [Fact]
    [Trait("Category", "RoundTrip")]
    public void Migration_v62_to_v63_matches_CreateV1()
    {
        var migratedPath = TempDbFile.NewPath("apex-r1-v62-migrated");
        var freshPath = TempDbFile.NewPath("apex-r1-v63-fresh");
        try
        {
            var fresh = CompanyFactory.CreateSeeded("Fresh LWF Co", FyStart);
            using (var store = new SqliteCompanyStore(freshPath)) store.Save(fresh);

            var legacy = CompanyFactory.CreateSeeded("Legacy LWF Co", FyStart);
            using (var store = new SqliteCompanyStore(migratedPath)) store.Save(legacy);
            // ONE rung only — this test is about the v62 → v63 step in isolation, so the manufactured book stops
            // at 62 and still carries v62's own objects while it is migrated.
            using (var conn = Open(migratedPath)) { SchemaDowngrade.V64ToV63(conn); SchemaDowngrade.V63ToV62(conn); SqliteConnection.ClearPool(conn); }
            Assert.Equal(62L, ReadScalar(migratedPath, "SELECT version FROM schema_version LIMIT 1;"));

            // The downgraded book genuinely lacks them — otherwise the "migration" below proves nothing.
            foreach (var col in Schema.V63SlabColumns)
                Assert.DoesNotContain(col, ColumnNames(migratedPath, "pay_head_computation_slabs"));

            // Reopen through the production store — the v62 → v63 migration runs.
            using (new SqliteCompanyStore(migratedPath)) { }
            Assert.Equal((long)Schema.CurrentVersion,
                ReadScalar(migratedPath, "SELECT version FROM schema_version LIMIT 1;"));

            foreach (var col in Schema.V63SlabColumns)
            {
                Assert.Contains(col, ColumnNames(migratedPath, "pay_head_computation_slabs"));
                Assert.Equal(ColumnContract(freshPath, "pay_head_computation_slabs", col),
                             ColumnContract(migratedPath, "pay_head_computation_slabs", col));
            }

            // Column ORDER too: ALTER TABLE appends, so CreateV1 must declare them last or PRAGMA table_info
            // would disagree about cid between a fresh and an upgraded book.
            Assert.Equal(ColumnNames(freshPath, "pay_head_computation_slabs"),
                         ColumnNames(migratedPath, "pay_head_computation_slabs"));
        }
        finally
        {
            TempDbFile.Delete(migratedPath);
            TempDbFile.Delete(freshPath);
        }
    }

    /// <summary>
    /// Both columns are NULL on every slab a pre-v63 book already had, and NULL means "in force in every period"
    /// — so the migration moves no payslip by a paisa (ER-13). This is the property that let the migration ship
    /// with <b>no back-fill and no UPDATE</b>: it is exact by construction rather than by a statement believed to
    /// have run correctly once.
    /// </summary>
    [Fact]
    [Trait("Category", "RoundTrip")]
    public void A_migrated_book_leaves_every_pre_existing_slab_perpetual()
    {
        var path = TempDbFile.NewPath("apex-r1-nobackfill");
        try
        {
            var legacy = BuildCompanyWithDeductionHead(levyYear: null);
            using (var store = new SqliteCompanyStore(path)) store.Save(legacy);
            using (var conn = Open(path)) { SchemaDowngrade.V64ToV63(conn); SchemaDowngrade.V63ToV62(conn); SqliteConnection.ClearPool(conn); }

            using var reopened = new SqliteCompanyStore(path);
            var loaded = reopened.Load(legacy.Id)!;

            var slabs = loaded.PayHeads
                .Where(p => p.Computation is not null)
                .SelectMany(p => p.Computation!.Slabs)
                .ToList();

            Assert.NotEmpty(slabs);
            Assert.All(slabs, s =>
            {
                Assert.Null(s.EffectiveFrom);
                Assert.Null(s.EffectiveTo);
                Assert.False(s.IsDated);
                // The behavioural half of the claim, not just the storage half.
                Assert.True(s.IsInForceOn(new DateOnly(2026, 7, 31)));
                Assert.True(s.IsInForceOn(new DateOnly(2031, 1, 31)));
            });

            // And no stray non-NULL crept into the raw columns either.
            Assert.Equal(0L, ReadScalar(path,
                "SELECT COUNT(*) FROM pay_head_computation_slabs WHERE effective_from IS NOT NULL OR effective_to IS NOT NULL;"));
        }
        finally { TempDbFile.Delete(path); }
    }

    // ================================================================= the LADDER

    /// <summary>
    /// 🔴 <b>THE LADDER TEST. A LADDER THAT COMPILES IS NOT A LADDER THAT MIGRATES.</b> A genuine v61 book climbs
    /// the whole way up through the production store, and BOTH rungs are asserted to have actually run: v62's two
    /// voucher-class child tables exist AND v63's two slab columns exist, each matching a fresh
    /// <c>CreateV1</c> book column for column. Then the book walks back DOWN and the shape is asserted at each
    /// rung on the way.
    ///
    /// <para><b>Why this test and not the parity tests above.</b> Every other schema test in this repository
    /// manufactures its "old" book by DOWNGRADING from current, so a guard aimed at the wrong rung can hide behind
    /// a matched pair of wrong steps — the downgrade and the migration cancel out and the test still passes. This
    /// one starts from a book that is genuinely at 61 and only ever climbs, so nothing cancels.</para>
    ///
    /// <para>🔴 <b>It is written to fail for BOTH ways of getting the re-point wrong</b>, which is the whole reason
    /// it exists:</para>
    /// <list type="bullet">
    /// <item>Guard left on <c>version == 61</c> (the pre-repair state): the v62 step fires first and moves the book
    /// to 62, the v63 step then never fires, and the store's own version check throws. Loud.</item>
    /// <item>v63 step ordered FIRST and left on <c>version == 61</c> (the tempting "fix"): the book is stamped 63
    /// and the store raises no complaint at all — but it never received v62's tables, so its
    /// <c>schema_version</c> is a lie about its own shape. <b>Only the v62 assertions below catch that</b>, and
    /// they are the reason this test asserts v62 objects in a file about v63.</item>
    /// </list>
    /// </summary>
    [Fact]
    [Trait("Category", "RoundTrip")]
    public void A_v61_book_climbs_the_whole_ladder_and_gets_BOTH_v62_and_v63_objects()
    {
        var laddered = TempDbFile.NewPath("apex-r1-ladder");
        var freshPath = TempDbFile.NewPath("apex-r1-ladder-fresh");
        try
        {
            var fresh = CompanyFactory.CreateSeeded("Fresh Ladder Co", FyStart);
            using (var store = new SqliteCompanyStore(freshPath)) store.Save(fresh);

            var company = BuildCompanyWithDeductionHead(levyYear: 2026, levyMonth: 12);
            var companyId = company.Id;
            using (var store = new SqliteCompanyStore(laddered)) store.Save(company);

            // ---- down to a GENUINE v61 book: both rungs, in order.
            using (var conn = Open(laddered))
            {
                SchemaDowngrade.V64ToV63(conn); SchemaDowngrade.V63ToV62(conn);
                SchemaDowngrade.V62ToV61(conn);
                SqliteConnection.ClearPool(conn);
            }
            Assert.Equal(61L, ReadScalar(laddered, "SELECT version FROM schema_version LIMIT 1;"));

            // It really is v61 — NEITHER version's objects are present. If this fails the climb proves nothing.
            foreach (var table in Schema.V62Tables)
                Assert.False(TableExists(laddered, table), $"the manufactured v61 book still has {table}");
            foreach (var col in Schema.V63SlabColumns)
                Assert.DoesNotContain(col, ColumnNames(laddered, "pay_head_computation_slabs"));

            // ---- ONE reopen climbs 61 → 62 → 63 through the real runner.
            using (new SqliteCompanyStore(laddered)) { }

            Assert.Equal((long)Schema.CurrentVersion,
                ReadScalar(laddered, "SELECT version FROM schema_version LIMIT 1;"));

            // 🔴 v62's objects exist. This is the assertion that fails if the v63 step is ordered ahead of v62's.
            foreach (var table in Schema.V62Tables)
            {
                Assert.True(TableExists(laddered, table),
                    $"the ladder reached {Schema.CurrentVersion} WITHOUT creating v62's {table} — the schema_version "
                    + "now lies about the database's own shape");
                Assert.Equal(ColumnNames(freshPath, table), ColumnNames(laddered, table));
            }
            foreach (var index in Schema.V62Indexes)
                Assert.True(IndexExists(laddered, index), $"the ladder skipped v62's index {index}");

            // 🔴 …and v63's columns exist, matching CreateV1 contract for contract.
            foreach (var col in Schema.V63SlabColumns)
            {
                Assert.Contains(col, ColumnNames(laddered, "pay_head_computation_slabs"));
                Assert.Equal(ColumnContract(freshPath, "pay_head_computation_slabs", col),
                             ColumnContract(laddered, "pay_head_computation_slabs", col));
            }
            Assert.Equal(ColumnNames(freshPath, "pay_head_computation_slabs"),
                         ColumnNames(laddered, "pay_head_computation_slabs"));

            // The book is still usable at the top of the ladder — a load/save round trip through the real store.
            using (var store = new SqliteCompanyStore(laddered)) store.Save(store.Load(companyId)!);

            // ---- and back DOWN, one rung at a time, asserting the shape at each.
            using (var conn = Open(laddered)) { SchemaDowngrade.V64ToV63(conn); SchemaDowngrade.V63ToV62(conn); SqliteConnection.ClearPool(conn); }
            Assert.Equal(62L, ReadScalar(laddered, "SELECT version FROM schema_version LIMIT 1;"));
            foreach (var col in Schema.V63SlabColumns)
                Assert.DoesNotContain(col, ColumnNames(laddered, "pay_head_computation_slabs"));
            foreach (var table in Schema.V62Tables)   // v63's rung must not have taken v62's objects with it
                Assert.True(TableExists(laddered, table), $"V63ToV62 wrongly dropped v62's {table}");

            using (var conn = Open(laddered)) { SchemaDowngrade.V62ToV61(conn); SqliteConnection.ClearPool(conn); }
            Assert.Equal(61L, ReadScalar(laddered, "SELECT version FROM schema_version LIMIT 1;"));
            foreach (var table in Schema.V62Tables)
                Assert.False(TableExists(laddered, table), $"V62ToV61 left {table} behind");

            // The shape survived the full up-and-down: the slab table still has its AUTOINCREMENT primary key and
            // its outgoing FK to pay_heads, neither of which a CREATE…AS SELECT rebuild would have kept.
            using (var conn = Open(laddered))
            {
                Assert.True(HasPrimaryKey(conn, "pay_head_computation_slabs"));
                Assert.True(HasForeignKeyTo(conn, "pay_head_computation_slabs", "pay_heads"));
                SqliteConnection.ClearPool(conn);
            }
        }
        finally
        {
            TempDbFile.Delete(laddered);
            TempDbFile.Delete(freshPath);
        }
    }

    // ================================================================= the round trip that actually matters

    /// <summary>
    /// 🔴 <b>THE ONE THAT MATTERS.</b> A Labour Welfare Fund head confined to December is saved, the book is
    /// closed, and on reopen it still deducts in December <b>and in no other month</b>. Asserted through the
    /// COMPUTED amount across the whole financial year, because that is the thing a user is out of pocket by —
    /// a test that only read back the stored date string would pass even if the engine had stopped consulting it.
    /// </summary>
    [Fact]
    [Trait("Category", "RoundTrip")]
    public void A_dated_slab_survives_the_store_round_trip_and_still_fires_in_one_month_only()
    {
        var path = TempDbFile.NewPath("apex-r1-dated-roundtrip");
        try
        {
            var original = BuildCompanyWithDeductionHead(levyYear: 2026, levyMonth: 12);
            using (var store = new SqliteCompanyStore(path)) store.Save(original);

            using var reopened = new SqliteCompanyStore(path);
            var loaded = reopened.Load(original.Id)!;

            var head = loaded.PayHeads.Single(p => p.Name == "Labour Welfare Fund");
            var slab = Assert.Single(head.Computation!.Slabs);
            Assert.Equal(new DateOnly(2026, 12, 1), slab.EffectiveFrom);
            Assert.Equal(new DateOnly(2026, 12, 31), slab.EffectiveTo);
            Assert.True(slab.IsDated);

            var employee = loaded.Employees.Single();
            var engine = new PayrollComputationService(loaded);

            decimal annual = 0m;
            for (var m = 0; m < 12; m++)
            {
                var first = FyStart.AddMonths(m);
                var last = new DateOnly(first.Year, first.Month, DateTime.DaysInMonth(first.Year, first.Month));
                var amount = engine.Compute(employee.Id, first, last)
                    .Lines.Single(l => l.PayHead.Id == head.Id).Amount.Amount;
                annual += amount;
                var expected = m == 8 ? OperatorTypedContribution : 0m;   // offset 8 = December 2026
                Assert.True(amount == expected,
                    $"After a save/reopen, {first:MMM yyyy} expected {expected} but deducted {amount}.");
            }

            // 🔴 Once a year after a round trip, not twelve times.
            Assert.Equal(OperatorTypedContribution, annual);
        }
        finally { TempDbFile.Delete(path); }
    }

    // ================================================================= the downgrade

    /// <summary>
    /// The downgrade drops exactly the two columns and <b>preserves the table's shape</b> — its
    /// <c>INTEGER … AUTOINCREMENT</c> primary key and its outgoing foreign key to <c>pay_heads</c>. A
    /// <c>CREATE … AS SELECT</c> rebuild loses both, and the measured consequence in this repository is SQLite
    /// reporting <c>foreign key mismatch</c> on the next child insert (recorded on
    /// <c>SchemaDowngrade.V56ToV55</c>), which is why <c>RebuildPreservingShape</c> is used here.
    ///
    /// <para>🔴 It also pins the rung's DESTINATION at <b>62</b>. A <c>V63ToV62</c> that stamped 61 would leave a
    /// book marked v61 while v62's two voucher-class child tables were still standing — the marker-lies-about-shape
    /// failure the ladder exists to prevent, and the exact mistake the pre-repair <c>V63ToV61</c> made once v62
    /// landed above it.</para>
    /// </summary>
    [Fact]
    [Trait("Category", "RoundTrip")]
    public void Downgrading_the_v63_rung_drops_only_the_window_and_keeps_the_tables_shape()
    {
        var path = TempDbFile.NewPath("apex-r1-downgrade");
        try
        {
            var company = BuildCompanyWithDeductionHead(levyYear: 2026, levyMonth: 12);
            using (var store = new SqliteCompanyStore(path)) store.Save(company);

            var before = ColumnNames(path, "pay_head_computation_slabs");
            var rowsBefore = ReadScalar(path, "SELECT COUNT(*) FROM pay_head_computation_slabs;");
            Assert.True(rowsBefore > 0, "the fixture must actually have written a slab row");

            using (var conn = Open(path))
            {
                SchemaDowngrade.V64ToV63(conn); SchemaDowngrade.V63ToV62(conn);
                Assert.True(HasPrimaryKey(conn, "pay_head_computation_slabs"),
                    "the rebuild lost the PRIMARY KEY — the failure mode RebuildPreservingShape exists to avoid");
                Assert.True(HasForeignKeyTo(conn, "pay_head_computation_slabs", "pay_heads"),
                    "the rebuild lost the outgoing foreign key to pay_heads");
                SqliteConnection.ClearPool(conn);
            }

            Assert.Equal(62L, ReadScalar(path, "SELECT version FROM schema_version LIMIT 1;"));

            // 🔴 The rung reversed v63 and NOTHING ELSE: v62's own objects are untouched and still standing, which
            // is what makes the 62 stamp above the truth rather than a guess.
            foreach (var table in Schema.V62Tables)
                Assert.True(TableExists(path, table), $"V63ToV62 must not touch v62's {table}");

            // Exactly the two columns went, and every slab ROW survived — the window is dropped, not the data.
            var after = ColumnNames(path, "pay_head_computation_slabs");
            Assert.Equal(before.Where(c => !Schema.V63SlabColumns.Contains(c)).ToList(), after);
            Assert.Equal(rowsBefore, ReadScalar(path, "SELECT COUNT(*) FROM pay_head_computation_slabs;"));
        }
        finally { TempDbFile.Delete(path); }
    }

    // ================================================================= fixture

    /// <summary>A book with one employee, a flat Basic and a "Deductions from Employees" head on the vendor's
    /// LWF shape. <paramref name="levyYear"/> null ⇒ an undated (pre-v63-shaped) slab.</summary>
    private static Company BuildCompanyWithDeductionHead(int? levyYear, int levyMonth = 12)
    {
        var c = CompanyFactory.CreateSeeded("LWF Store Co", FyStart);
        var pay = new PayrollService(c);
        pay.EnablePayroll();
        var ph = new PayHeadService(c);

        var basic = ph.CreatePayHead("Basic", PayHeadType.Earnings, PayHeadCalculationType.FlatRate,
            underGroupId: c.FindGroupByName("Indirect Expenses")!.Id,
            incomeTaxComponent: IncomeTaxComponent.BasicSalary);

        var slab = levyYear is { } y
            ? PayHeadComputationSlab.ForSingleMonth(new Money(OperatorTypedContribution), y, levyMonth)
            : PayHeadComputationSlab.FlatValue(new Money(OperatorTypedContribution));

        var lwf = ph.CreatePayHead("Labour Welfare Fund", PayHeadType.Deductions,
            PayHeadCalculationType.AsComputedValue,
            underGroupId: c.FindGroupByName("Current Liabilities")!.Id,
            computation: new PayHeadComputation(
                new[] { new PayHeadComputationComponent(basic.Id) }, new[] { slab }));

        var grp = pay.CreateEmployeeGroup("Staff").Id;
        var emp = pay.CreateEmployee("Rajkumar Sharma", grp, employeeNumber: "E-001", pan: "ABCPD5678L");
        emp.DateOfJoining = FyStart;

        new SalaryStructureService(c).DefineForEmployee(emp.Id, FyStart, new[]
        {
            new SalaryStructureLine(basic.Id, 0, new Money(30_000m)),
            new SalaryStructureLine(lwf.Id, 1),
        });

        return c;
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
        using (var r = cmd.ExecuteReader()) while (r.Read()) names.Add(r.GetString(1));
        SqliteConnection.ClearPool(conn);
        return names;
    }

    private static bool TableExists(string path, string table)
    {
        using var conn = Open(path);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = $n;";
        cmd.Parameters.AddWithValue("$n", table);
        var found = Convert.ToInt64(cmd.ExecuteScalar()) > 0;
        SqliteConnection.ClearPool(conn);
        return found;
    }

    private static bool IndexExists(string path, string index)
    {
        using var conn = Open(path);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'index' AND name = $n;";
        cmd.Parameters.AddWithValue("$n", index);
        var found = Convert.ToInt64(cmd.ExecuteScalar()) > 0;
        SqliteConnection.ClearPool(conn);
        return found;
    }

    private static bool HasPrimaryKey(SqliteConnection conn, string table)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"PRAGMA table_info(\"{table}\");";
        using var r = cmd.ExecuteReader();
        while (r.Read()) if (r.GetInt64(5) > 0) return true;
        return false;
    }

    private static bool HasForeignKeyTo(SqliteConnection conn, string table, string parent)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"PRAGMA foreign_key_list(\"{table}\");";
        using var r = cmd.ExecuteReader();
        while (r.Read())
            if (string.Equals(r.GetString(2), parent, StringComparison.OrdinalIgnoreCase)) return true;
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
