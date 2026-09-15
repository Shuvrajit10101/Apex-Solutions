using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Services;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Apex.Persistence.Sqlite.Tests;

/// <summary>
/// Schema v63 → v64 (defect <b>T1-26</b> and the user's ruling on the <b>4% Health &amp; Education Cess</b>) — the
/// establishment's own <b>effective-from-dated</b> cess rate, as one child table of <c>companies</c> plus its
/// by-company index.
///
/// <para>Older databases are manufactured by walking the ladder DOWN from current with
/// <see cref="SchemaDowngrade"/> rather than from hand-written DDL, so the migrations are exercised against real
/// rows — the idiom every schema test here uses.</para>
///
/// <para>🔴 <b>THE LOAD-BEARING TEST IS
/// <see cref="A_migrated_book_computes_the_SAME_payroll_tax_it_did_before_the_upgrade"/>, AND IT IS NOT "THE TABLE
/// EXISTS".</b> Limb (c) of the ruling is that <b>no existing book changes behaviour on upgrade</b>, and a silent
/// change to a shipped payroll deduction is the worst failure available on this path. A table-existence test cannot
/// see such a change at all, so that test migrates a real book across the rung and compares the <b>computed annual
/// tax to the paisa</b>, before and after.</para>
///
/// <para>🔴 <b>THE LADDER IS PROVED BY CLIMBING, NOT ONLY BY DESCENDING</b>
/// (<see cref="A_v62_book_climbs_the_whole_ladder_and_gets_both_v63_and_v64_objects"/>). Every other test here
/// manufactures its book by DOWNGRADING, so a guard pointed at the wrong rung can hide behind a matched pair of
/// wrong steps. That one climbs UP from v62 through the production store and asserts that v63's columns AND v64's
/// table both exist at the top.</para>
/// </summary>
public sealed class IncomeTaxCessRateSchemaTests
{
    private static readonly DateOnly FyStart = new(2025, 4, 1);
    private const string Table = "income_tax_cess_rates";
    private const string Index = "ix_income_tax_cess_rates_company";

    // ================================================================= migration parity

    /// <summary>
    /// The table a migrated book ends up with is byte-identical in contract (name / type / NOT NULL / DEFAULT / PK
    /// on every column) to the one <c>CreateV1</c> gives a fresh book — the equivalence rule this repository treats
    /// as absolute, and the exact comparison <c>SchemaMigrationEquivalenceTests</c> makes.
    /// </summary>
    [Fact]
    [Trait("Category", "RoundTrip")]
    public void Migration_v63_to_v64_matches_CreateV1()
    {
        var migratedPath = TempDbFile.NewPath("apex-v64-migrated");
        var freshPath = TempDbFile.NewPath("apex-v64-fresh");
        try
        {
            using (var store = new SqliteCompanyStore(freshPath))
                store.Save(CompanyFactory.CreateSeeded("Fresh Cess Co", FyStart));

            using (var store = new SqliteCompanyStore(migratedPath))
                store.Save(CompanyFactory.CreateSeeded("Legacy Cess Co", FyStart));

            // ONE rung only — this test is about the v63 → v64 step in isolation.
            using (var conn = Open(migratedPath)) { SchemaDowngrade.V64ToV63(conn); SqliteConnection.ClearPool(conn); }
            Assert.Equal(63L, ReadScalar(migratedPath, "SELECT version FROM schema_version LIMIT 1;"));

            // The downgraded book genuinely lacks them — otherwise the "migration" below proves nothing.
            Assert.False(TableExists(migratedPath, Table));
            Assert.False(IndexExists(migratedPath, Index));

            // Reopen through the production store — the v63 → v64 migration runs.
            using (new SqliteCompanyStore(migratedPath)) { }
            Assert.Equal((long)Schema.CurrentVersion,
                ReadScalar(migratedPath, "SELECT version FROM schema_version LIMIT 1;"));

            Assert.True(TableExists(migratedPath, Table));
            Assert.True(IndexExists(migratedPath, Index));

            // Same columns, in the same order, with the same full contract.
            Assert.Equal(ColumnNames(freshPath, Table), ColumnNames(migratedPath, Table));
            foreach (var col in ColumnNames(freshPath, Table))
                Assert.Equal(ColumnContract(freshPath, Table, col), ColumnContract(migratedPath, Table, col));
        }
        finally
        {
            TempDbFile.Delete(migratedPath);
            TempDbFile.Delete(freshPath);
        }
    }

    /// <summary>The published object lists are the ones the migration actually creates — the single-source-of-truth
    /// the migration, CreateV1, the downgrade and these tests all read.</summary>
    [Fact]
    [Trait("Category", "RoundTrip")]
    public void The_published_v64_object_lists_describe_what_the_migration_creates()
    {
        Assert.Equal(new[] { Table }, Schema.V64Tables);
        Assert.Equal(new[] { Index }, Schema.V64Indexes);
        Assert.Equal(64, Schema.CurrentVersion);
    }

    // ================================================================= 🔴 limb (c): nothing changes on upgrade

    /// <summary>
    /// 🔴 <b>THE RULING'S (c) LIMB, ASSERTED RATHER THAN ARGUED.</b> A pre-v64 book — one that cannot possibly have
    /// set a cess rate, because there was nowhere to keep one — must compute <b>exactly the same annual tax</b>
    /// after the upgrade as before it. The migration inserts nothing and back-fills nothing; an empty table means
    /// "charge the statutory rate for the year", which is the 4% every sourceable year publishes.
    ///
    /// <para>This compares the COMPUTED FIGURE across the rung, not the schema. A version that seeded a 4% row
    /// instead would still pass a table-existence test and would still (today) produce the same number — but it
    /// would assert 4% as this company's own decision when nobody decided it, and it would freeze 4% forward past
    /// any future statutory change. The emptiness is the point, so it is asserted too.</para>
    /// </summary>
    [Fact]
    [Trait("Category", "RoundTrip")]
    public void A_migrated_book_computes_the_SAME_payroll_tax_it_did_before_the_upgrade()
    {
        var path = TempDbFile.NewPath("apex-v64-no-behaviour-change");
        try
        {
            var seeded = CompanyFactory.CreateSeeded("Upgrade Co", FyStart);
            using (var store = new SqliteCompanyStore(path)) store.Save(seeded);

            // Manufacture a genuine pre-v64 book.
            using (var conn = Open(path)) { SchemaDowngrade.V64ToV63(conn); SqliteConnection.ClearPool(conn); }
            Assert.False(TableExists(path, Table));

            // Reopen through the production store — the migration runs — and load the company back.
            Company migrated;
            using (var store = new SqliteCompanyStore(path)) migrated = store.Load(seeded.Id)!;

            // NOTHING was back-filled. Emptiness is the truth about a book that never set a rate.
            Assert.Empty(migrated.IncomeTaxCessRates);
            Assert.Equal(0L, ReadScalar(path, $"SELECT COUNT(*) FROM {Table};"));

            // And the arithmetic is identical to the pre-v64 engine's, to the rupee, across both regimes and the
            // §87A / surcharge / cess limbs. These are the Phase-8 golden figures, unchanged.
            var rates = SalaryTaxRates.ForCompanyPeriod(migrated, new DateOnly(2026, 3, 31));
            Assert.Equal(0.04m, rates.CessRate);
            Assert.False(rates.CessRateIsCompanyOverride);

            Assert.Equal(new Money(97_500m), SalaryIncomeTax.ComputeAnnual(14_25_000m, TaxRegime.New, rates).AnnualTax);
            Assert.Equal(new Money(2_02_800m), SalaryIncomeTax.ComputeAnnual(12_75_000m, TaxRegime.Old, rates).AnnualTax);
            Assert.Equal(Money.Zero, SalaryIncomeTax.ComputeAnnual(12_00_000m, TaxRegime.New, rates).AnnualTax);
            Assert.Equal(new Money(10_400m), SalaryIncomeTax.ComputeAnnual(12_10_000m, TaxRegime.New, rates).AnnualTax);
        }
        finally
        {
            TempDbFile.Delete(path);
        }
    }

    // ================================================================= round trip

    /// <summary>
    /// A company's dated cess rates survive a save-and-reopen <b>as behaviour</b>, not merely as rows: the reloaded
    /// book must resolve the same rate for the same period. Persisting the dates but dropping them on the way back
    /// out would produce a book that computes a different payslip after a reopen than before one — the failure a
    /// row-count test cannot see.
    /// </summary>
    [Fact]
    [Trait("Category", "RoundTrip")]
    public void Dated_cess_rates_survive_the_store_round_trip_as_behaviour()
    {
        var path = TempDbFile.NewPath("apex-v64-roundtrip");
        try
        {
            var c = CompanyFactory.CreateSeeded("Round Trip Cess Co", FyStart);
            c.AddIncomeTaxCessRate(new IncomeTaxCessRate(Guid.NewGuid(), new DateOnly(2025, 10, 1), 600)); // 6%
            c.AddIncomeTaxCessRate(new IncomeTaxCessRate(Guid.NewGuid(), new DateOnly(2026, 1, 1), 300));  // 3%
            using (var store = new SqliteCompanyStore(path)) store.Save(c);

            Company back;
            using (var store = new SqliteCompanyStore(path)) back = store.Load(c.Id)!;

            Assert.Equal(2, back.IncomeTaxCessRates.Count);
            // Ordered oldest-first, with the exact rates and dates.
            Assert.Equal(new DateOnly(2025, 10, 1), back.IncomeTaxCessRates[0].EffectiveFrom);
            Assert.Equal(600, back.IncomeTaxCessRates[0].RateBasisPoints);
            Assert.Equal(new DateOnly(2026, 1, 1), back.IncomeTaxCessRates[1].EffectiveFrom);
            Assert.Equal(300, back.IncomeTaxCessRates[1].RateBasisPoints);

            // 🔴 The behaviour, which is what actually matters: the window still confines each rate to its period.
            Assert.Equal(0.04m, SalaryTaxRates.ForCompanyPeriod(back, new DateOnly(2025, 9, 30)).CessRate); // statutory
            Assert.Equal(0.06m, SalaryTaxRates.ForCompanyPeriod(back, new DateOnly(2025, 12, 31)).CessRate);
            Assert.Equal(0.03m, SalaryTaxRates.ForCompanyPeriod(back, new DateOnly(2026, 3, 31)).CessRate);
        }
        finally
        {
            TempDbFile.Delete(path);
        }
    }

    /// <summary>A company that sets no rate writes no row at all — the ER-13 property the (c) limb rests on.</summary>
    [Fact]
    [Trait("Category", "RoundTrip")]
    public void A_company_that_sets_no_cess_rate_writes_no_row()
    {
        var path = TempDbFile.NewPath("apex-v64-empty");
        try
        {
            using (var store = new SqliteCompanyStore(path))
                store.Save(CompanyFactory.CreateSeeded("Quiet Co", FyStart));
            Assert.Equal(0L, ReadScalar(path, $"SELECT COUNT(*) FROM {Table};"));
        }
        finally
        {
            TempDbFile.Delete(path);
        }
    }

    /// <summary>Re-saving replaces rather than accumulates — the ordinary store contract, asserted here because a
    /// duplicated cess row would be a duplicated statutory deduction rule.</summary>
    [Fact]
    [Trait("Category", "RoundTrip")]
    public void Re_saving_a_company_does_not_accumulate_duplicate_cess_rows()
    {
        var path = TempDbFile.NewPath("apex-v64-resave");
        try
        {
            var c = CompanyFactory.CreateSeeded("Resave Co", FyStart);
            c.AddIncomeTaxCessRate(new IncomeTaxCessRate(Guid.NewGuid(), new DateOnly(2025, 4, 1), 400));
            using (var store = new SqliteCompanyStore(path))
            {
                store.Save(c);
                store.Save(c);
                store.Save(c);
            }
            Assert.Equal(1L, ReadScalar(path, $"SELECT COUNT(*) FROM {Table};"));
        }
        finally
        {
            TempDbFile.Delete(path);
        }
    }

    // ================================================================= the ladder, climbed

    /// <summary>
    /// 🔴 A ladder that compiles is not a ladder that migrates. This climbs UP from v62 through the production
    /// store and asserts that <b>both</b> v63's columns and v64's table exist at the top, then walks back down and
    /// asserts the shape at each rung. A v64 guard pointed at the wrong version — or a rung ordered before the one
    /// it depends on — fails here and nowhere else, because every other test in this file manufactures its book by
    /// descending, where a matched pair of wrong steps cancels out.
    /// </summary>
    [Fact]
    [Trait("Category", "RoundTrip")]
    public void A_v62_book_climbs_the_whole_ladder_and_gets_both_v63_and_v64_objects()
    {
        var path = TempDbFile.NewPath("apex-v64-ladder");
        try
        {
            using (var store = new SqliteCompanyStore(path))
                store.Save(CompanyFactory.CreateSeeded("Ladder Co", FyStart));

            // Down two rungs to a genuine v62 book.
            using (var conn = Open(path))
            {
                SchemaDowngrade.V64ToV63(conn);
                SchemaDowngrade.V63ToV62(conn);
                SqliteConnection.ClearPool(conn);
            }
            Assert.Equal(62L, ReadScalar(path, "SELECT version FROM schema_version LIMIT 1;"));
            Assert.False(TableExists(path, Table));
            foreach (var col in Schema.V63SlabColumns)
                Assert.DoesNotContain(col, ColumnNames(path, "pay_head_computation_slabs"));

            // Climb the whole way through the production store.
            using (new SqliteCompanyStore(path)) { }
            Assert.Equal((long)Schema.CurrentVersion, ReadScalar(path, "SELECT version FROM schema_version LIMIT 1;"));

            // BOTH versions' objects, not just the top one — the failure mode of a mis-ordered rung.
            Assert.True(TableExists(path, Table));
            Assert.True(IndexExists(path, Index));
            foreach (var col in Schema.V63SlabColumns)
                Assert.Contains(col, ColumnNames(path, "pay_head_computation_slabs"));

            // And back down one rung: v64's objects go, v63's columns stay, the marker says 63.
            using (var conn = Open(path)) { SchemaDowngrade.V64ToV63(conn); SqliteConnection.ClearPool(conn); }
            Assert.Equal(63L, ReadScalar(path, "SELECT version FROM schema_version LIMIT 1;"));
            Assert.False(TableExists(path, Table));
            Assert.False(IndexExists(path, Index));
            foreach (var col in Schema.V63SlabColumns)
                Assert.Contains(col, ColumnNames(path, "pay_head_computation_slabs"));
        }
        finally
        {
            TempDbFile.Delete(path);
        }
    }

    /// <summary>
    /// 🔴 The downgrade must not touch <c>companies</c>, the FK <b>PARENT</b> it hangs off. This is the
    /// <c>V56ToV55</c> trap: a <c>CREATE … AS SELECT</c> rebuild of a parent silently loses its PRIMARY KEY and every
    /// child FK then dangles with <c>foreign key mismatch</c>. v64 adds no column to any existing table precisely so
    /// the downgrade can be a plain DROP — so the parent's shape is asserted to be byte-identical across the rung,
    /// and a child insert is exercised afterwards to prove the key still functions.
    /// </summary>
    [Fact]
    [Trait("Category", "RoundTrip")]
    public void The_downgrade_leaves_the_companies_parent_table_and_its_primary_key_intact()
    {
        var path = TempDbFile.NewPath("apex-v64-parent-intact");
        try
        {
            var c = CompanyFactory.CreateSeeded("Parent Co", FyStart);
            using (var store = new SqliteCompanyStore(path)) store.Save(c);

            var before = string.Join("\n", ColumnNames(path, "companies").Select(n => ColumnContract(path, "companies", n)));

            using (var conn = Open(path)) { SchemaDowngrade.V64ToV63(conn); SqliteConnection.ClearPool(conn); }

            var after = string.Join("\n", ColumnNames(path, "companies").Select(n => ColumnContract(path, "companies", n)));
            Assert.Equal(before, after);

            // The primary key still functions as an FK target: a child insert against it must succeed, and a
            // duplicate company id must still be rejected.
            using (var conn = Open(path))
            {
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "PRAGMA foreign_keys = ON; INSERT INTO pt_slab_bands "
                    + "(id, company_id, slab_id, state_code, gender_scope, band_order, from_wage_paisa, "
                    + " to_wage_paisa, monthly_amount_paisa, month_overrides) "
                    + "VALUES ($id, $cid, $sid, '27', 0, 0, 0, NULL, 0, '');";
                cmd.Parameters.AddWithValue("$id", Guid.NewGuid().ToString("D"));
                cmd.Parameters.AddWithValue("$cid", c.Id.ToString("D"));
                cmd.Parameters.AddWithValue("$sid", Guid.NewGuid().ToString("D"));
                cmd.ExecuteNonQuery(); // no "foreign key mismatch" ⇒ companies.id is still a usable key
                SqliteConnection.ClearPool(conn);
            }
        }
        finally
        {
            TempDbFile.Delete(path);
        }
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
