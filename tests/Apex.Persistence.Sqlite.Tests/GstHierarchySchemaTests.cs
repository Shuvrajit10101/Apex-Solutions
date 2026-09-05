using System.Text;
using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Services;
using Microsoft.Data.Sqlite;
using Xunit;
using Domain = Apex.Ledger.Domain;

namespace Apex.Persistence.Sqlite.Tests;

/// <summary>
/// Schema v50 → v51 — the <b>GST five-level hierarchy masters</b> (plan.md Phase 10.10 WF-1 / register IV-1, slice S4).
/// This bump adds the three masters TallyPrime resolves GST through and we never had — a narrow
/// <see cref="MasterGstDetails"/> block on the <b>Stock Group</b>, on the accounting <b>Group</b> and on the
/// <b>company</b> (<see cref="GstConfig.DefaultGst"/>) — plus the <b>two</b> separate source-order options
/// (<see cref="GstConfig.SourceOfHsnSacDetails"/>, <see cref="GstConfig.SourceOfGstRate"/>). ⚠️ That the reference
/// application ships <b>two</b> such options is a <b>[web], A14-unverified</b> claim, not corpus — see
/// <see cref="GstDetailSource"/> for the sourcing (owed-review lens 3 finding 5).
///
/// <para>⚠️ <b>THE FRESH/UPGRADED SPLIT — the reason this migration is not boilerplate, and the only thing in the
/// chain so far where a fresh database and a migrated one deliberately hold DIFFERENT values.</b> A fresh company
/// gets TallyPrime's shipped order, <see cref="GstDetailSource.LedgerFirst"/> (the column's own
/// <c>DEFAULT 0</c>). An <b>existing</b> book must keep resolving exactly as it does today — item first — so
/// <see cref="Schema.MigrateV50ToV51"/> back-fills every pre-existing company row to
/// <see cref="GstDetailSource.StockItemFirst"/> with an explicit <c>UPDATE</c> (R12 decision 1: it provably changes
/// zero currently-resolvable figures). The <c>DEFAULT</c> literal therefore CANNOT carry the back-fill — it must stay
/// 0 on both sides or <see cref="SchemaMigrationEquivalenceTests"/> fails on the <c>PRAGMA table_info</c> default.
/// <see cref="ExistingV50Companies_migrateTo_StockItemFirst"/> is the test that fails if the <c>UPDATE</c> is
/// dropped.</para>
///
/// <para>🔴 <b>CORRECTED BY THE OWED REVIEW (lens 2 finding 1). This paragraph used to end "…and
/// <see cref="FreshCompanies_get_LedgerFirst"/> is the test that fails if someone 'simplifies' the back-fill into
/// the <c>DEFAULT</c>. Neither can pass the other's mutation." THAT WAS MEASURED FALSE:</b> making the whole
/// forbidden change (both <c>DEFAULT</c>s 0 → 1, back-fill <c>UPDATE</c> deleted) left
/// <see cref="FreshCompanies_get_LedgerFirst"/> GREEN, because no production INSERT ever falls through to the
/// column default — that test measures a C# null-coalesce. The DDL default is now pinned behaviourally by
/// <see cref="A_row_that_omits_the_two_source_order_columns_takes_the_DEFAULT_LedgerFirst"/>, which inserts a bare
/// row that omits the two columns. The other half of the split WAS and remains alive: delete only the back-fill
/// <c>UPDATE</c> and two tests go red.</para>
///
/// <para>The "genuine v50 database" is manufactured with <see cref="SchemaDowngrade.V51ToV50"/> rather than
/// hand-written DDL, so the migration is exercised against real rows. ⚠️ <b>"Genuine" has a measured limit</b>
/// (lens 1 finding 2): the downgrade rebuilds its three tables with <c>CREATE … AS SELECT</c>, which reproduces
/// columns and data but NOT the PRIMARY KEY, the NOT NULLs or the DEFAULTs — so <b>the v50 → v51 migration has
/// never been exercised against a <c>companies</c> table that still had them</b>. Indexes ARE restored, since the
/// review (<see cref="Downgrade_v51_to_v50_preserves_the_indexes_on_the_rebuilt_tables"/>); the constraint loss is
/// documented on <see cref="SchemaDowngrade"/> and is not fixed here.</para>
/// </summary>
public sealed class GstHierarchySchemaTests
{
    private static readonly DateOnly FyStart = new(2025, 4, 1);
    private const string GstinMaharashtra = "27AAPFU0939F1ZV";

    // Odd, mutually distinct fixture values: a mapper that cross-wires the stock-group block into the group slot
    // (or either into the company default) cannot pass, and none of them coincides with a domain default.
    private const string StockGroupHsn = "85171213";
    private const int StockGroupRateBp = 1237;
    private const string GroupHsn = "998313";
    private const int GroupRateBp = 631;
    private const string CompanyHsn = "7318";
    private const int CompanyRateBp = 1741;

    // ================================================================= round-trip (all three levels)

    /// <summary>
    /// Each of the three new masters round-trips its own block through SQLite, with a distinct HSN, rate, taxability
    /// and supply type, so a save/load that reads one level's columns into another level's block cannot pass.
    /// </summary>
    [Fact]
    [Trait("Category", "RoundTrip")]
    public void MasterGstDetails_roundTrips_on_stockGroup_group_and_company()
    {
        var dbPath = TempDbFile.NewPath("apex-gsthier-roundtrip");
        try
        {
            var c = SeedGstCompany();

            var sg = new StockGroup(Guid.NewGuid(), "Mobile")
            {
                Gst = new MasterGstDetails
                {
                    HsnSac = StockGroupHsn,
                    RateBasisPoints = StockGroupRateBp,
                    Taxability = GstTaxability.Taxable,
                    SupplyType = GstSupplyType.Goods,
                },
            };
            c.AddStockGroup(sg);

            var grp = new Group(Guid.NewGuid(), "Consultancy Sales", GroupNature.Income,
                parentId: c.FindGroupByName("Sales Accounts")!.Id)
            {
                Gst = new MasterGstDetails
                {
                    HsnSac = GroupHsn,
                    RateBasisPoints = GroupRateBp,
                    Taxability = GstTaxability.Taxable,
                    SupplyType = GstSupplyType.Services,
                },
            };
            c.AddGroup(grp);

            c.Gst!.DefaultGst = new MasterGstDetails
            {
                HsnSac = CompanyHsn,
                RateBasisPoints = CompanyRateBp,
                Taxability = GstTaxability.Taxable,
                SupplyType = GstSupplyType.Goods,
            };

            using (var store = new SqliteCompanyStore(dbPath)) store.Save(c);
            using var reopened = new SqliteCompanyStore(dbPath);
            var loaded = reopened.Load(c.Id)!;

            var loadedSg = loaded.StockGroups.Single(g => g.Name == "Mobile");
            Assert.Equal(StockGroupHsn, loadedSg.Gst!.HsnSac);
            Assert.Equal(StockGroupRateBp, loadedSg.Gst.RateBasisPoints);
            Assert.Equal(GstSupplyType.Goods, loadedSg.Gst.SupplyType);
            Assert.Equal(GstTaxability.Taxable, loadedSg.Gst.Taxability);

            var loadedGrp = loaded.Groups.Single(g => g.Name == "Consultancy Sales");
            Assert.Equal(GroupHsn, loadedGrp.Gst!.HsnSac);
            Assert.Equal(GroupRateBp, loadedGrp.Gst.RateBasisPoints);
            Assert.Equal(GstSupplyType.Services, loadedGrp.Gst.SupplyType);

            Assert.Equal(CompanyHsn, loaded.Gst!.DefaultGst!.HsnSac);
            Assert.Equal(CompanyRateBp, loaded.Gst.DefaultGst.RateBasisPoints);
            Assert.Equal(GstSupplyType.Goods, loaded.Gst.DefaultGst.SupplyType);

            // A master that carries NO block reads back null — no empty block is conjured (ER-13).
            Assert.All(loaded.StockGroups.Where(g => g.Name != "Mobile"), g => Assert.Null(g.Gst));
            Assert.All(loaded.Groups.Where(g => g.Name != "Consultancy Sales"), g => Assert.Null(g.Gst));
        }
        finally { TempDbFile.Delete(dbPath); }
    }

    /// <summary>A non-taxable block (Exempt, no rate) survives as itself rather than degrading to "no block".</summary>
    [Fact]
    [Trait("Category", "RoundTrip")]
    public void An_exempt_stockGroup_block_roundTrips_without_a_rate()
    {
        var dbPath = TempDbFile.NewPath("apex-gsthier-exempt");
        try
        {
            var c = SeedGstCompany();
            var sg = new StockGroup(Guid.NewGuid(), "Fresh Produce")
            {
                Gst = new MasterGstDetails { HsnSac = "0702", Taxability = GstTaxability.Exempt },
            };
            c.AddStockGroup(sg);

            using (var store = new SqliteCompanyStore(dbPath)) store.Save(c);
            using var reopened = new SqliteCompanyStore(dbPath);

            var loaded = reopened.Load(c.Id)!.StockGroups.Single(g => g.Name == "Fresh Produce");
            Assert.NotNull(loaded.Gst);
            Assert.Equal(GstTaxability.Exempt, loaded.Gst!.Taxability);
            Assert.Null(loaded.Gst.RateBasisPoints);
            Assert.False(loaded.Gst.IsTaxable);
        }
        finally { TempDbFile.Delete(dbPath); }
    }

    /// <summary>Both source-order options round-trip both values, independently of each other — TallyPrime ships them
    /// as TWO separate options, so a store that wrote one column twice cannot pass.</summary>
    [Fact]
    [Trait("Category", "RoundTrip")]
    public void The_two_source_orders_roundTrip_independently()
    {
        foreach (var (hsnSource, rateSource) in new[]
                 {
                     (GstDetailSource.LedgerFirst, GstDetailSource.StockItemFirst),
                     (GstDetailSource.StockItemFirst, GstDetailSource.LedgerFirst),
                 })
        {
            var dbPath = TempDbFile.NewPath($"apex-gsthier-source-{hsnSource}-{rateSource}");
            try
            {
                var c = SeedGstCompany();
                Assert.Equal(GstDetailSource.LedgerFirst, c.Gst!.SourceOfHsnSacDetails);  // the shipped default
                Assert.Equal(GstDetailSource.LedgerFirst, c.Gst.SourceOfGstRate);
                c.Gst.SourceOfHsnSacDetails = hsnSource;
                c.Gst.SourceOfGstRate = rateSource;

                using (var store = new SqliteCompanyStore(dbPath)) store.Save(c);
                Assert.Equal((long)hsnSource, ReadScalar(dbPath,
                    $"SELECT gst_source_of_hsn_sac FROM companies WHERE id = '{c.Id:D}';"));
                Assert.Equal((long)rateSource, ReadScalar(dbPath,
                    $"SELECT gst_source_of_rate FROM companies WHERE id = '{c.Id:D}';"));

                using var reopened = new SqliteCompanyStore(dbPath);
                var loaded = reopened.Load(c.Id)!;
                Assert.Equal(hsnSource, loaded.Gst!.SourceOfHsnSacDetails);
                Assert.Equal(rateSource, loaded.Gst.SourceOfGstRate);
            }
            finally { TempDbFile.Delete(dbPath); }
        }
    }

    // ================================================================= migration parity

    [Fact]
    [Trait("Category", "RoundTrip")]
    public void Migration_v50_to_v51_matches_CreateV1()
    {
        var migratedPath = TempDbFile.NewPath("apex-gsthier-v50-migrated");
        var freshPath = TempDbFile.NewPath("apex-gsthier-v51-fresh");
        try
        {
            var fresh = CompanyFactory.CreateSeeded("Fresh Hier Co", FyStart);
            using (var store = new SqliteCompanyStore(freshPath)) store.Save(fresh);

            // Manufacture a genuine v50 database: save at v51, then downgrade (drop the v51 columns).
            var legacy = CompanyFactory.CreateSeeded("Legacy Hier Co", FyStart);
            using (var store = new SqliteCompanyStore(migratedPath)) store.Save(legacy);
            using (var conn = Open(migratedPath))
            {
                SchemaDowngrade.V53ToV52(conn);   // v53 voucher-type user flags
                SchemaDowngrade.V52ToV51(conn);   // v52 voucher edit log
                SchemaDowngrade.V51ToV50(conn);
                SqliteConnection.ClearPool(conn);
            }
            Assert.Equal(50L, ReadScalar(migratedPath, "SELECT version FROM schema_version LIMIT 1;"));
            foreach (var col in Schema.V51GstHierarchyCompanyColumns)
                Assert.DoesNotContain(col, ColumnNames(migratedPath, "companies"));
            foreach (var col in Schema.V51GstHierarchyMasterColumns)
            {
                Assert.DoesNotContain(col, ColumnNames(migratedPath, "groups"));
                Assert.DoesNotContain(col, ColumnNames(migratedPath, "stock_groups"));
            }

            // Reopen through the production store — the v50 → v51 migration runs.
            using (new SqliteCompanyStore(migratedPath)) { }
            Assert.Equal((long)Schema.CurrentVersion, ReadScalar(migratedPath, "SELECT version FROM schema_version LIMIT 1;"));

            // Every column this migration adds matches the fresh CreateV1 schema exactly — same declared type, NOT
            // NULL and DEFAULT literal. THE DEFAULT IS THE POINT for the two source-order columns: this comparison is
            // what pins DEFAULT 0 (= LedgerFirst) on BOTH sides, which is why the StockItemFirst back-fill has to be
            // an explicit UPDATE and cannot hide in the DEFAULT.
            // (The downgrade rebuilds the three tables via CREATE … AS SELECT, which erases the PRE-EXISTING columns'
            // declared type/notnull — an artifact of the downgrade helper, not the migration — so we compare only what
            // the v50→v51 migration itself adds. Whole-schema parity across the full chain is separately guaranteed by
            // SchemaMigrationEquivalenceTests.)
            foreach (var col in Schema.V51GstHierarchyCompanyColumns)
                Assert.Equal(ColumnContract(freshPath, "companies", col), ColumnContract(migratedPath, "companies", col));
            foreach (var col in Schema.V51GstHierarchyMasterColumns)
            {
                Assert.Equal(ColumnContract(freshPath, "groups", col), ColumnContract(migratedPath, "groups", col));
                Assert.Equal(ColumnContract(freshPath, "stock_groups", col),
                    ColumnContract(migratedPath, "stock_groups", col));
            }

            // The two source-order columns are NOT NULL DEFAULT 0 on the fresh side — asserted literally, so a change
            // to either half of the contract is caught here and not only through the whole-schema comparison.
            Assert.Contains("notnull=1 | default=0",
                ColumnContract(freshPath, "companies", "gst_source_of_hsn_sac"), StringComparison.Ordinal);
            Assert.Contains("notnull=1 | default=0",
                ColumnContract(freshPath, "companies", "gst_source_of_rate"), StringComparison.Ordinal);
        }
        finally
        {
            TempDbFile.Delete(migratedPath);
            TempDbFile.Delete(freshPath);
        }
    }

    // ================================================================= ⚠️ the fresh/upgraded split

    /// <summary>
    /// R12 decision 1, and the assertion that cannot be satisfied by accident: <b>an existing book keeps resolving
    /// item-first</b>. The company is saved with both source orders explicitly at <c>LedgerFirst</c> — the fresh
    /// default — and then downgraded to a genuine v50 database, at which point the setting does not exist anywhere,
    /// which is exactly the state every real pre-v51 book is in. Migrating up must yield <c>StockItemFirst</c> on both,
    /// from <see cref="Schema.MigrateV50ToV51"/>'s explicit back-fill <c>UPDATE</c>.
    ///
    /// <para>Starting from <c>LedgerFirst</c> is deliberate and load-bearing: it is the value the column's own
    /// <c>DEFAULT 0</c> would produce, so <c>StockItemFirst</c> is reachable ONLY through the back-fill. Delete the
    /// <c>UPDATE</c> from the migration and this test fails while every other test in the file still passes.</para>
    /// </summary>
    [Fact]
    [Trait("Category", "RoundTrip")]
    public void ExistingV50Companies_migrateTo_StockItemFirst()
    {
        var dbPath = TempDbFile.NewPath("apex-gsthier-legacy-rows");
        try
        {
            var c = SeedGstCompany();
            c.Gst!.SourceOfHsnSacDetails = GstDetailSource.LedgerFirst;   // the FRESH value, so only the
            c.Gst.SourceOfGstRate = GstDetailSource.LedgerFirst;          // back-fill can produce StockItemFirst
            using (var store = new SqliteCompanyStore(dbPath)) store.Save(c);
            Assert.Equal(0L, ReadScalar(dbPath, "SELECT gst_source_of_hsn_sac FROM companies;"));
            Assert.Equal(0L, ReadScalar(dbPath, "SELECT gst_source_of_rate FROM companies;"));

            using (var conn = Open(dbPath))
            {
                SchemaDowngrade.V53ToV52(conn);   // v53 voucher-type user flags
                SchemaDowngrade.V52ToV51(conn);   // v52 voucher edit log
                SchemaDowngrade.V51ToV50(conn);
                SqliteConnection.ClearPool(conn);
            }
            Assert.Equal(1L, ReadScalar(dbPath, "SELECT COUNT(*) FROM companies;"));   // the row survived

            using var reopened = new SqliteCompanyStore(dbPath);
            Assert.Equal((long)Schema.CurrentVersion, ReadScalar(dbPath, "SELECT version FROM schema_version LIMIT 1;"));

            // The stored bytes are 1 = StockItemFirst …
            Assert.Equal((long)GstDetailSource.StockItemFirst,
                ReadScalar(dbPath, "SELECT gst_source_of_hsn_sac FROM companies;"));
            Assert.Equal((long)GstDetailSource.StockItemFirst,
                ReadScalar(dbPath, "SELECT gst_source_of_rate FROM companies;"));
            // … and they survive the load path, rather than being re-derived from an enum default that agrees.
            var loaded = reopened.Load(c.Id)!;
            Assert.Equal(GstDetailSource.StockItemFirst, loaded.Gst!.SourceOfHsnSacDetails);
            Assert.Equal(GstDetailSource.StockItemFirst, loaded.Gst.SourceOfGstRate);
        }
        finally { TempDbFile.Delete(dbPath); }
    }

    /// <summary>
    /// The other half of the split, and the mutation guard on the first: a company created <b>fresh</b> on v51 gets
    /// TallyPrime's shipped order, <c>LedgerFirst</c>. Moving the back-fill into the column <c>DEFAULT</c> — the
    /// obvious "simplification" of <see cref="ExistingV50Companies_migrateTo_StockItemFirst"/> — fails here.
    /// </summary>
    [Fact]
    [Trait("Category", "RoundTrip")]
    public void FreshCompanies_get_LedgerFirst()
    {
        var dbPath = TempDbFile.NewPath("apex-gsthier-fresh-rows");
        try
        {
            var c = SeedGstCompany();
            using (var store = new SqliteCompanyStore(dbPath)) store.Save(c);

            Assert.Equal((long)Schema.CurrentVersion, ReadScalar(dbPath, "SELECT version FROM schema_version LIMIT 1;"));
            Assert.Equal((long)GstDetailSource.LedgerFirst,
                ReadScalar(dbPath, "SELECT gst_source_of_hsn_sac FROM companies;"));
            Assert.Equal((long)GstDetailSource.LedgerFirst,
                ReadScalar(dbPath, "SELECT gst_source_of_rate FROM companies;"));

            using var reopened = new SqliteCompanyStore(dbPath);
            var loaded = reopened.Load(c.Id)!;
            Assert.Equal(GstDetailSource.LedgerFirst, loaded.Gst!.SourceOfHsnSacDetails);
            Assert.Equal(GstDetailSource.LedgerFirst, loaded.Gst.SourceOfGstRate);
        }
        finally { TempDbFile.Delete(dbPath); }
    }

    // ================================================================= ⚠️ the column DEFAULT, behaviourally

    /// <summary>
    /// 🔴 <b>The <c>DEFAULT 0</c> on the two source-order columns, asserted through SQLite's own behaviour rather
    /// than through a <c>PRAGMA table_info</c> string.</b>
    ///
    /// <para><b>Why this test had to be added</b> (owed-review lens 2 finding 1). The docstring at the top of this
    /// file claims <see cref="FreshCompanies_get_LedgerFirst"/> and
    /// <see cref="ExistingV50Companies_migrateTo_StockItemFirst"/> are each other's mutation guard. Measured, they
    /// are not: performing the exact forbidden "simplification" — <c>DEFAULT 0</c> → <c>1</c> in
    /// <see cref="Schema.CreateV1"/> AND in <see cref="Schema.MigrateV50ToV51"/>, back-fill <c>UPDATE</c> deleted —
    /// left <b>both</b> of them green, and the only red in the whole project was
    /// <see cref="Migration_v50_to_v51_matches_CreateV1"/> failing on a hard-coded string literal. The mechanism is
    /// that <b>no production INSERT ever falls through to the DEFAULT</b>: the single
    /// <c>INSERT INTO companies</c> in <c>src/</c> always supplies both columns explicitly, so
    /// <see cref="FreshCompanies_get_LedgerFirst"/> measures a C# null-coalesce, not the DDL.</para>
    ///
    /// <para>So this inserts a <b>bare row</b> that omits them — supplying only the columns SQLite would otherwise
    /// reject — which is the only way the DDL default is observable at all. Both halves of the chain are pinned:
    /// the fresh <see cref="Schema.CreateV1"/> DDL and the <c>ALTER … ADD COLUMN</c> the migration runs.</para>
    /// </summary>
    [Theory]
    [Trait("Category", "RoundTrip")]
    [InlineData(false)]   // fresh CreateV1 DDL
    [InlineData(true)]    // the migration's own ALTER … ADD COLUMN
    public void A_row_that_omits_the_two_source_order_columns_takes_the_DEFAULT_LedgerFirst(bool viaMigration)
    {
        var dbPath = TempDbFile.NewPath($"apex-gsthier-default-{(viaMigration ? "migrated" : "fresh")}");
        try
        {
            using (var store = new SqliteCompanyStore(dbPath)) store.Save(SeedGstCompany());

            if (viaMigration)
            {
                using (var conn = Open(dbPath))
                {
                    SchemaDowngrade.V53ToV52(conn);   // v53 voucher-type user flags
                    SchemaDowngrade.V52ToV51(conn);   // v52 voucher edit log
                    SchemaDowngrade.V51ToV50(conn);
                    SqliteConnection.ClearPool(conn);
                }
                using (new SqliteCompanyStore(dbPath)) { }   // the v50 → v51 migration runs
                // The back-fill has moved the EXISTING row to 1; the new bare row below must still get the DEFAULT.
                Assert.Equal(1L, ReadScalar(dbPath, "SELECT gst_source_of_hsn_sac FROM companies;"));
            }

            var bareId = Guid.NewGuid().ToString("D");
            InsertBareCompanyRow(dbPath, bareId);

            Assert.Equal((long)GstDetailSource.LedgerFirst, ReadScalar(dbPath,
                $"SELECT gst_source_of_hsn_sac FROM companies WHERE id = '{bareId}';"));
            Assert.Equal((long)GstDetailSource.LedgerFirst, ReadScalar(dbPath,
                $"SELECT gst_source_of_rate FROM companies WHERE id = '{bareId}';"));
        }
        finally { TempDbFile.Delete(dbPath); }
    }

    // ================================================================= ⚠️ the back-fill has to SURVIVE a save

    /// <summary>
    /// 🔴 <b>R12 decision 1 is a guarantee about the book, not about the <c>UPDATE</c> statement — and it was
    /// FALSE for a non-GST book</b> (owed-review lens 1 finding 1).
    ///
    /// <para>The two source orders are NOT NULL <c>companies</c> columns but are carried in memory on
    /// <see cref="GstConfig"/>, which <c>SqliteCompanyStore</c> builds <b>only when <c>gst_enabled = 1</c></b>. So a
    /// migrated book with GST switched off loaded with <c>Gst == null</c>, and the next whole-company save — a
    /// DELETE + re-INSERT triggered by roughly forty ordinary master and voucher screens — re-INSERTed a fabricated
    /// <c>LedgerFirst</c> over the back-fill. Measured before the fix: stored <c>1|1</c> → one save → <c>0|0</c>.
    /// Nothing caught it because every back-fill fixture in this file is GST-ENABLED and none saves after
    /// migrating.</para>
    ///
    /// <para>The fix is <c>SqliteCompanyStore.ReadStoredSourceOrders</c>: when the aggregate carries no GST config
    /// it has no value for these columns, so the stored one is preserved instead of a default being invented.
    /// <b>Collapse that back to <c>?? LedgerFirst</c> and this test goes red.</b></para>
    ///
    /// <para>⚠️ <b>Why the back-filled state is reached by running the back-fill statement rather than by
    /// downgrading and re-migrating.</b> A book put through <see cref="SchemaDowngrade.V51ToV50"/> <b>cannot be
    /// saved to at all</b> afterwards — see
    /// <see cref="KNOWN_LIMIT_a_downgraded_book_cannot_be_saved_because_the_rebuild_drops_the_primary_key"/> — so
    /// the downgrade harness cannot host a test about saving. The row is therefore put into exactly the state
    /// <see cref="Schema.MigrateV50ToV51"/> leaves it in, using the migration's own <c>UPDATE</c>. That is the state
    /// under test: what happens to a back-filled value when the application then does something ordinary.</para>
    /// </summary>
    [Fact]
    [Trait("Category", "RoundTrip")]
    public void An_ordinary_save_of_a_migrated_nonGst_book_preserves_the_StockItemFirst_backfill()
    {
        var dbPath = TempDbFile.NewPath("apex-gsthier-nongst-save");
        try
        {
            // A NON-GST book — the case every existing back-fill fixture misses, and the common one in the field.
            var c = CompanyFactory.CreateSeeded("No GST Co", FyStart);
            Assert.Null(c.Gst);
            using (var store = new SqliteCompanyStore(dbPath)) store.Save(c);

            // The state MigrateV50ToV51 leaves an existing book in, produced by the migration's own statement.
            Assert.Contains("UPDATE companies SET gst_source_of_hsn_sac = 1, gst_source_of_rate = 1;",
                Schema.MigrateV50ToV51, StringComparison.Ordinal);
            ExecSql(dbPath, "UPDATE companies SET gst_source_of_hsn_sac = 1, gst_source_of_rate = 1;");

            using var reopened = new SqliteCompanyStore(dbPath);

            // …now do the most ordinary thing in the application: load the book and save it again. This is what
            // roughly forty master and voucher screens do on every Accept.
            var loaded = reopened.Load(c.Id)!;
            Assert.Null(loaded.Gst);                 // still non-GST — nothing holds the orders in memory
            loaded.AddStockGroup(new StockGroup(Guid.NewGuid(), "Anything"));
            reopened.Save(loaded);

            Assert.Equal((long)GstDetailSource.StockItemFirst,
                ReadScalar(dbPath, "SELECT gst_source_of_hsn_sac FROM companies;"));
            Assert.Equal((long)GstDetailSource.StockItemFirst,
                ReadScalar(dbPath, "SELECT gst_source_of_rate FROM companies;"));

            // And a second save is not a slow leak either.
            reopened.Save(reopened.Load(c.Id)!);
            Assert.Equal((long)GstDetailSource.StockItemFirst,
                ReadScalar(dbPath, "SELECT gst_source_of_rate FROM companies;"));
        }
        finally { TempDbFile.Delete(dbPath); }
    }

    /// <summary>
    /// 🔴 <b>The measured consequence of <see cref="SchemaDowngrade"/>'s constraint loss, pinned rather than
    /// described</b> (owed-review lens 1 finding 2). The <c>CREATE … AS SELECT</c> rebuild reproduces columns and
    /// data but not the PRIMARY KEY, so on a round-tripped file <c>companies.id</c> is no longer a key and every
    /// table that references <c>companies(id)</c> becomes a <i>foreign key mismatch</i> the moment the store's
    /// <c>PRAGMA foreign_keys = ON</c> takes effect. <b><c>SqliteCompanyStore.Save</c> therefore THROWS on any book
    /// this helper has manufactured</b>, which is why no test in this file can both downgrade and then save.
    ///
    /// <para>Also worth knowing before trusting a green run: <c>PRAGMA integrity_check</c> still answers <c>ok</c>
    /// on such a file, so the obvious check does NOT see this.</para>
    ///
    /// <para><b>This is a test-harness fidelity limit, NOT shipped data loss</b> — <c>SchemaDowngrade</c> has no
    /// caller anywhere in <c>src/</c> and no shipped path ever opens a downgraded database. What it does mean is
    /// that <b>the v50 → v51 migration has never been exercised against a <c>companies</c> table that still had its
    /// PRIMARY KEY, NOT NULLs and DEFAULTs.</b> Fixing it means emitting a real prior-version DDL from
    /// <c>PRAGMA table_info</c> + <c>foreign_key_list</c> in every downgrade, which is a change of its own; this
    /// test exists so the limit cannot be forgotten. <b>When it is fixed, this test will start failing — delete it
    /// then, with a note, rather than weakening it.</b></para>
    /// </summary>
    [Fact]
    [Trait("Category", "RoundTrip")]
    public void KNOWN_LIMIT_a_downgraded_book_cannot_be_saved_because_the_rebuild_drops_the_primary_key()
    {
        var dbPath = TempDbFile.NewPath("apex-gsthier-downgrade-pk-loss");
        try
        {
            var c = SeedGstCompany();
            using (var store = new SqliteCompanyStore(dbPath)) store.Save(c);
            Assert.Contains("pk=1", ColumnContract(dbPath, "companies", "id"), StringComparison.Ordinal);

            using (var conn = Open(dbPath))
            {
                SchemaDowngrade.V53ToV52(conn);   // v53 voucher-type user flags
                SchemaDowngrade.V52ToV51(conn);   // v52 voucher edit log
                SchemaDowngrade.V51ToV50(conn);
                SqliteConnection.ClearPool(conn);
            }

            // The primary key is gone — and integrity_check does not notice.
            Assert.Contains("pk=0", ColumnContract(dbPath, "companies", "id"), StringComparison.Ordinal);
            Assert.Equal("ok", ReadText(dbPath, "PRAGMA integrity_check;"));

            using var reopened = new SqliteCompanyStore(dbPath);   // the migration itself still runs cleanly
            Assert.Equal((long)Schema.CurrentVersion,
                ReadScalar(dbPath, "SELECT version FROM schema_version LIMIT 1;"));

            var ex = Assert.Throws<SqliteException>(() => reopened.Save(reopened.Load(c.Id)!));
            Assert.Contains("foreign key mismatch", ex.Message, StringComparison.Ordinal);
        }
        finally { TempDbFile.Delete(dbPath); }
    }

    /// <summary>A brand-new company — no stored row to preserve — still gets the fresh <c>LedgerFirst</c>, so the
    /// preservation above cannot be satisfied by simply never writing the columns.</summary>
    [Fact]
    [Trait("Category", "RoundTrip")]
    public void A_first_save_of_a_nonGst_company_writes_the_fresh_LedgerFirst()
    {
        var dbPath = TempDbFile.NewPath("apex-gsthier-nongst-fresh");
        try
        {
            var c = CompanyFactory.CreateSeeded("Fresh No GST Co", FyStart);
            using (var store = new SqliteCompanyStore(dbPath)) store.Save(c);
            Assert.Equal((long)GstDetailSource.LedgerFirst,
                ReadScalar(dbPath, "SELECT gst_source_of_hsn_sac FROM companies;"));
            Assert.Equal((long)GstDetailSource.LedgerFirst,
                ReadScalar(dbPath, "SELECT gst_source_of_rate FROM companies;"));
        }
        finally { TempDbFile.Delete(dbPath); }
    }

    // ================================================================= ⚠️ the company block at NON-DEFAULT values

    /// <summary>
    /// 🔴 <b>The company default block's <c>Taxability</c> and <c>SupplyType</c> were never round-tripped at a
    /// value other than the enum zero</b> (owed-review lens 2 finding 3): replacing
    /// <c>(int)defaultGst.Taxability</c> with <c>(int)GstTaxability.Taxable</c> and the supply type with
    /// <c>Goods</c> in <c>SqliteCompanyStore</c> left this project 223/223 green.
    /// <c>gst_default_taxability</c> is also the NULL-marker for the whole company block, i.e. the most
    /// load-bearing of the six company columns.
    /// </summary>
    [Fact]
    [Trait("Category", "RoundTrip")]
    public void The_company_default_block_roundTrips_a_nonDefault_taxability_and_supplyType()
    {
        var dbPath = TempDbFile.NewPath("apex-gsthier-company-nondefault");
        try
        {
            var c = SeedGstCompany();
            c.Gst!.DefaultGst = new MasterGstDetails
            {
                HsnSac = "998313",
                Taxability = GstTaxability.Exempt,      // NOT the enum zero, and not the domain default
                RateBasisPoints = null,                 // an Exempt block may not carry a positive rate
                SupplyType = GstSupplyType.Services,    // NOT the enum zero
            };

            using (var store = new SqliteCompanyStore(dbPath)) store.Save(c);
            Assert.Equal((long)GstTaxability.Exempt,
                ReadScalar(dbPath, "SELECT gst_default_taxability FROM companies;"));
            Assert.Equal((long)GstSupplyType.Services,
                ReadScalar(dbPath, "SELECT gst_default_supply_type FROM companies;"));

            using var reopened = new SqliteCompanyStore(dbPath);
            var loaded = reopened.Load(c.Id)!.Gst!.DefaultGst!;
            Assert.Equal(GstTaxability.Exempt, loaded.Taxability);
            Assert.Equal(GstSupplyType.Services, loaded.SupplyType);
            Assert.False(loaded.IsTaxable);
            Assert.Null(loaded.RateBasisPoints);
        }
        finally { TempDbFile.Delete(dbPath); }
    }

    /// <summary>The same at the two MASTER levels, for the same reason — a store that hard-coded either enum's zero
    /// would otherwise be invisible on <c>groups</c> and <c>stock_groups</c> too.</summary>
    [Fact]
    [Trait("Category", "RoundTrip")]
    public void The_two_master_blocks_roundTrip_a_nonDefault_taxability_and_supplyType()
    {
        var dbPath = TempDbFile.NewPath("apex-gsthier-master-nondefault");
        try
        {
            var c = SeedGstCompany();
            c.AddStockGroup(new StockGroup(Guid.NewGuid(), "Nil Rated SG")
            {
                Gst = new MasterGstDetails
                {
                    HsnSac = "0702",
                    Taxability = GstTaxability.NilRated,
                    RateBasisPoints = 0,
                    SupplyType = GstSupplyType.Services,
                },
            });
            c.AddGroup(new Group(Guid.NewGuid(), "NonGst Sales", GroupNature.Income,
                parentId: c.FindGroupByName("Sales Accounts")!.Id)
            {
                Gst = new MasterGstDetails
                {
                    HsnSac = "7318",
                    Taxability = GstTaxability.NonGst,
                    SupplyType = GstSupplyType.Services,
                },
            });

            using (var store = new SqliteCompanyStore(dbPath)) store.Save(c);
            using var reopened = new SqliteCompanyStore(dbPath);
            var loaded = reopened.Load(c.Id)!;

            var sg = loaded.StockGroups.Single(g => g.Name == "Nil Rated SG").Gst!;
            Assert.Equal(GstTaxability.NilRated, sg.Taxability);
            Assert.Equal(GstSupplyType.Services, sg.SupplyType);
            Assert.Equal(0, sg.RateBasisPoints);

            var grp = loaded.Groups.Single(g => g.Name == "NonGst Sales").Gst!;
            Assert.Equal(GstTaxability.NonGst, grp.Taxability);
            Assert.Equal(GstSupplyType.Services, grp.SupplyType);
        }
        finally { TempDbFile.Delete(dbPath); }
    }

    // ================================================================= ⚠️ existing books survive intact

    /// <summary>
    /// The worst outcome this slice could produce is a migration that loses or reshapes one byte of an existing
    /// book, so this test compares the <b>entire database contents</b> — every table, every row, every pre-existing
    /// column — across a genuine v50 → v51 upgrade, on a populated book (28 groups, seeded ledgers and voucher types,
    /// stock groups, a stock item, posted vouchers with odd amounts, GST enabled).
    ///
    /// <para>The comparison is exhaustive by construction: the snapshot enumerates <c>sqlite_master</c>, so a table
    /// nobody thought about is still compared. Only two differences are permitted, and both are named and asserted
    /// individually rather than skipped in bulk — the two source-order columns, which the back-fill deliberately
    /// moves from 0 to 1. Everything else, including the twelve new master-GST columns (which stay NULL on a book
    /// that never set them), must be identical.</para>
    /// </summary>
    [Fact]
    [Trait("Category", "RoundTrip")]
    public void An_existing_book_survives_the_v50_to_v51_upgrade_byte_for_byte()
    {
        var dbPath = TempDbFile.NewPath("apex-gsthier-survival");
        try
        {
            var c = SeedPopulatedBook();
            using (var store = new SqliteCompanyStore(dbPath)) store.Save(c);

            // Give the book real BLOB content — the four encrypted NIC credential columns, the only BLOBs in the
            // schema and the only data here that no domain object carries. Without this the hex rendering in
            // SnapshotData would be untested, which is exactly how a BLOB change went invisible before (lens 1
            // finding 3): these columns are written solely by INicCredentialStore, so a fixture built from the
            // domain leaves all four NULL.
            ExecSql(dbPath, """
                UPDATE companies SET
                    nic_api_username_enc = x'0102030405',
                    nic_api_password_enc = x'FFEE00',
                    nic_client_id_enc    = x'7F',
                    nic_client_secret_enc = x'DEADBEEF';
                """);

            // Snapshot the v51 database MINUS everything v51 added — i.e. exactly the information a genuine v50
            // database holds. That is the thing the upgrade must not disturb.
            var before = SnapshotData(dbPath, excluded: V51Columns());
            Assert.Contains("0xDEADBEEF", before, StringComparison.Ordinal);   // the snapshot really reads BLOBs

            using (var conn = Open(dbPath))
            {
                SchemaDowngrade.V53ToV52(conn);   // v53 voucher-type user flags
                SchemaDowngrade.V52ToV51(conn);   // v52 voucher edit log
                SchemaDowngrade.V51ToV50(conn);
                SqliteConnection.ClearPool(conn);
            }
            Assert.Equal(50L, ReadScalar(dbPath, "SELECT version FROM schema_version LIMIT 1;"));

            using var reopened = new SqliteCompanyStore(dbPath);
            Assert.Equal((long)Schema.CurrentVersion, ReadScalar(dbPath, "SELECT version FROM schema_version LIMIT 1;"));

            var after = SnapshotData(dbPath, excluded: V51Columns());
            Assert.Equal(before, after);

            // The two permitted differences, asserted explicitly (they are excluded above, so the bulk comparison
            // above cannot see them).
            Assert.Equal((long)GstDetailSource.StockItemFirst,
                ReadScalar(dbPath, "SELECT gst_source_of_hsn_sac FROM companies;"));
            Assert.Equal((long)GstDetailSource.StockItemFirst,
                ReadScalar(dbPath, "SELECT gst_source_of_rate FROM companies;"));

            // The twelve new master-GST columns are NULL on a book that never set them — no value is conjured.
            Assert.Equal(0L, ReadScalar(dbPath, "SELECT COUNT(*) FROM groups WHERE gst_taxability IS NOT NULL;"));
            Assert.Equal(0L, ReadScalar(dbPath, "SELECT COUNT(*) FROM stock_groups WHERE gst_taxability IS NOT NULL;"));
            Assert.Equal(0L, ReadScalar(dbPath, "SELECT COUNT(*) FROM companies WHERE gst_default_taxability IS NOT NULL;"));

            // And the domain reload agrees: the money and the masters are unchanged.
            var loaded = reopened.Load(c.Id)!;
            Assert.Equal(c.Vouchers.Count, loaded.Vouchers.Count);
            Assert.Equal(c.Groups.Count, loaded.Groups.Count);
            Assert.Equal(c.StockGroups.Count, loaded.StockGroups.Count);
            Assert.All(loaded.StockGroups, g => Assert.Null(g.Gst));
            Assert.All(loaded.Groups, g => Assert.Null(g.Gst));
            Assert.Null(loaded.Gst!.DefaultGst);
        }
        finally { TempDbFile.Delete(dbPath); }
    }

    // ================================================================= downgrade

    [Fact]
    [Trait("Category", "RoundTrip")]
    public void Downgrade_v51_to_v50_dropsColumns_preservesRows()
    {
        var dbPath = TempDbFile.NewPath("apex-gsthier-downgrade");
        try
        {
            var c = SeedPopulatedBook();
            var groupCount = c.Groups.Count;
            var stockGroupCount = c.StockGroups.Count;
            Assert.True(groupCount > 0 && stockGroupCount > 0);

            using (var store = new SqliteCompanyStore(dbPath)) store.Save(c);
            using (var conn = Open(dbPath))
            {
                SchemaDowngrade.V53ToV52(conn);   // v53 voucher-type user flags
                SchemaDowngrade.V52ToV51(conn);   // v52 voucher edit log
                SchemaDowngrade.V51ToV50(conn);
                SqliteConnection.ClearPool(conn);
            }

            Assert.Equal(50L, ReadScalar(dbPath, "SELECT version FROM schema_version LIMIT 1;"));
            Assert.Equal(1L, ReadScalar(dbPath, "SELECT COUNT(*) FROM companies;"));
            Assert.Equal((long)stockGroupCount, ReadScalar(dbPath, "SELECT COUNT(*) FROM stock_groups;"));
            // The P&L head is a groups row too, so the stored count is groupCount + 1.
            Assert.Equal(groupCount + 1L, ReadScalar(dbPath, "SELECT COUNT(*) FROM groups;"));

            foreach (var col in Schema.V51GstHierarchyCompanyColumns)
                Assert.DoesNotContain(col, ColumnNames(dbPath, "companies"));
            foreach (var col in Schema.V51GstHierarchyMasterColumns)
            {
                Assert.DoesNotContain(col, ColumnNames(dbPath, "groups"));
                Assert.DoesNotContain(col, ColumnNames(dbPath, "stock_groups"));
            }

            // The v50 column below them on companies is untouched by this downgrade.
            Assert.Contains("warn_on_negative_stock", ColumnNames(dbPath, "companies"));
            // …as are the v1 columns on the two master tables.
            Assert.Contains("add_quantities", ColumnNames(dbPath, "stock_groups"));
            Assert.Contains("is_pl_head", ColumnNames(dbPath, "groups"));
        }
        finally { TempDbFile.Delete(dbPath); }
    }

    /// <summary>
    /// 🔴 <b>A downgrade that silently deletes an index manufactures a database no real v50 book ever was</b>
    /// (owed-review lens 1 finding 2). v51 is the first version whose downgrade rebuilds tables that CARRY indexes:
    /// <c>DROP TABLE groups</c> takes <c>ix_groups_company</c> with it and <c>DROP TABLE stock_groups</c> takes
    /// <c>ix_stock_groups_company</c>. Measured before the fix: both were gone after
    /// <see cref="SchemaDowngrade.V51ToV50"/> and were never recreated, so every migration test in this file — and
    /// the legacy-upgrade fixtures elsewhere that use the same technique — ran the real migration over a schema
    /// missing two indexes. <c>SchemaDowngrade.DropColumns</c> now replays the index DDL it dropped; delete that
    /// replay and this test goes red.
    /// </summary>
    [Fact]
    [Trait("Category", "RoundTrip")]
    public void Downgrade_v51_to_v50_preserves_the_indexes_on_the_rebuilt_tables()
    {
        var dbPath = TempDbFile.NewPath("apex-gsthier-downgrade-indexes");
        try
        {
            using (var store = new SqliteCompanyStore(dbPath)) store.Save(SeedPopulatedBook());

            var atCurrentVersion = IndexNames(dbPath);

            // 🔴 STEP DOWN OFF v52 FIRST, AND TAKE THE BASELINE AFTER IT. v52's downgrade DROPS the whole
            // voucher_edit_log table, so its index goes with it BY DESIGN — that is what dropping a table means,
            // not an index this rebuild lost. Measuring from a v52 baseline would have folded that intended loss
            // into the assertion and made the v51→v50 contract this test exists for unfalsifiable.
            using (var conn = Open(dbPath))
            {
                SchemaDowngrade.V53ToV52(conn);   // v53 voucher-type user flags
                SchemaDowngrade.V52ToV51(conn);   // v52 voucher edit log
                SqliteConnection.ClearPool(conn);
            }

            var before = IndexNames(dbPath);
            Assert.Contains("ix_groups_company", before);
            Assert.Contains("ix_stock_groups_company", before);
            Assert.DoesNotContain("ix_voucher_edit_log_company", before);

            using (var conn = Open(dbPath))
            {
                SchemaDowngrade.V51ToV50(conn);
                SqliteConnection.ClearPool(conn);
            }

            // Not "the two we happened to think of" — NO index anywhere in the database may go missing.
            Assert.Equal(before, IndexNames(dbPath));

            // …and they survive the round trip back up, so the migrated book is index-complete too — all the way
            // to the CURRENT version, which puts voucher_edit_log's index back.
            using (new SqliteCompanyStore(dbPath)) { }
            Assert.Equal(atCurrentVersion, IndexNames(dbPath));
        }
        finally { TempDbFile.Delete(dbPath); }
    }

    // ---- fixtures ----

    /// <summary>A seeded, GST-enabled company (the GST config has to exist before the source orders can be read).</summary>
    private static Company SeedGstCompany()
    {
        var c = CompanyFactory.CreateSeeded("Hierarchy Co", FyStart);
        new GstService(c).EnableGst(new GstConfig
        {
            Enabled = true,
            Gstin = GstinMaharashtra,
            HomeStateCode = "27",
        });
        return c;
    }

    /// <summary>
    /// A populated book: the full seed (28 groups + P&amp;L head, 2 ledgers, 24 voucher types, currency, godown,
    /// cost category), GST enabled, two nested stock groups, a unit, a stock item, two extra ledgers and two posted
    /// vouchers with ODD amounts. It carries NO v51 data at all — it is a genuine pre-v51 book.
    /// </summary>
    private static Company SeedPopulatedBook()
    {
        var c = SeedGstCompany();

        var parent = new StockGroup(Guid.NewGuid(), "Electronics");
        c.AddStockGroup(parent);
        c.AddStockGroup(new StockGroup(Guid.NewGuid(), "Handsets", parentId: parent.Id, addQuantities: false));

        var unit = Unit.Simple(Guid.NewGuid(), "Nos", "Numbers");
        c.AddUnit(unit);
        c.AddStockItem(new StockItem(Guid.NewGuid(), "Handset A7", parent.Id, unit.Id));

        var sales = new Domain.Ledger(Guid.NewGuid(), "Handset Sales", c.FindGroupByName("Sales Accounts")!.Id,
            Money.Zero, openingIsDebit: false);
        c.AddLedger(sales);
        var debtor = new Domain.Ledger(Guid.NewGuid(), "Vishal Traders", c.FindGroupByName("Sundry Debtors")!.Id,
            Money.FromRupees(7_431.63m), openingIsDebit: true);
        c.AddLedger(debtor);

        var salesType = c.FindVoucherTypeByName("Sales")!;
        var svc = new LedgerService(c);
        svc.Post(new Voucher(Guid.NewGuid(), salesType.Id, new DateOnly(2025, 4, 7),
            new[]
            {
                new EntryLine(debtor.Id, Money.FromRupees(13_907.41m), DrCr.Debit),
                new EntryLine(sales.Id, Money.FromRupees(13_907.41m), DrCr.Credit),
            }, partyId: debtor.Id));
        svc.Post(new Voucher(Guid.NewGuid(), salesType.Id, new DateOnly(2025, 4, 19),
            new[]
            {
                new EntryLine(debtor.Id, Money.FromRupees(2_063.17m), DrCr.Debit),
                new EntryLine(sales.Id, Money.FromRupees(2_063.17m), DrCr.Credit),
            }, partyId: debtor.Id));

        return c;
    }

    /// <summary>Every column v51 adds, keyed by table — the set the survival snapshot excludes.</summary>
    private static Dictionary<string, HashSet<string>> V51Columns() => new(StringComparer.OrdinalIgnoreCase)
    {
        ["companies"] = new(Schema.V51GstHierarchyCompanyColumns, StringComparer.OrdinalIgnoreCase),
        ["groups"] = new(Schema.V51GstHierarchyMasterColumns, StringComparer.OrdinalIgnoreCase),
        ["stock_groups"] = new(Schema.V51GstHierarchyMasterColumns, StringComparer.OrdinalIgnoreCase),
    };

    // ---- helpers ----

    /// <summary>
    /// Every non-implicit index in the database, by name, in a stable order.
    /// </summary>
    private static IReadOnlyList<string> IndexNames(string dbPath)
    {
        using var conn = Open(dbPath);
        using var cmd = conn.CreateCommand();
        cmd.CommandText =
            "SELECT name FROM sqlite_master WHERE type='index' AND sql IS NOT NULL ORDER BY name;";
        var names = new List<string>();
        using (var r = cmd.ExecuteReader())
            while (r.Read()) names.Add(r.GetString(0));
        SqliteConnection.ClearPool(conn);
        return names;
    }

    /// <summary>
    /// Inserts a <c>companies</c> row that supplies ONLY what SQLite would otherwise reject — every column that is
    /// <c>NOT NULL</c> with no <c>DEFAULT</c> — and nothing else, so every defaulted column is left to its DDL
    /// default. Derived from <c>PRAGMA table_info</c> rather than hand-listed, so it cannot rot as the table grows.
    /// This is the only way the two source-order columns' <c>DEFAULT 0</c> is observable at all: the one production
    /// <c>INSERT INTO companies</c> always supplies them explicitly.
    /// </summary>
    private static void InsertBareCompanyRow(string dbPath, string id)
    {
        using var conn = Open(dbPath);

        var required = new List<(string Name, string Type)>();
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "PRAGMA table_info(\"companies\");";
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                var name = r.GetString(1);
                var type = r.GetString(2);
                var notNull = r.GetInt64(3) != 0;
                var hasDefault = !r.IsDBNull(4);
                if (notNull && !hasDefault) required.Add((name, type));
            }
        }
        // `id` is supplied whether or not the table still declares it NOT NULL. On a database manufactured by
        // SchemaDowngrade it does NOT: the CREATE … AS SELECT rebuild erases every NOT NULL and the PRIMARY KEY
        // (documented on SchemaDowngrade.V51ToV50; owed-review lens 1 finding 2), so the required set comes back
        // EMPTY there. That is a fact about the harness, not about the DDL under test.
        required.RemoveAll(c => string.Equals(c.Name, "id", StringComparison.OrdinalIgnoreCase));
        required.Insert(0, ("id", "TEXT"));
        // The precondition that actually matters: the two columns under test must NOT be in the supplied set, or
        // the row would not be exercising their DEFAULT at all.
        Assert.DoesNotContain(required, c => c.Name.StartsWith("gst_source_of", StringComparison.OrdinalIgnoreCase));

        var columns = string.Join(", ", required.Select(c => $"\"{c.Name}\""));
        var values = string.Join(", ", required.Select(c =>
            string.Equals(c.Name, "id", StringComparison.OrdinalIgnoreCase) ? $"'{id}'"
            : c.Type.Contains("INT", StringComparison.OrdinalIgnoreCase) ? "0"
            : "'x'"));

        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = $"INSERT INTO companies ({columns}) VALUES ({values});";
            cmd.ExecuteNonQuery();
        }
        SqliteConnection.ClearPool(conn);
    }

    /// <summary>
    /// The full contents of every user table, rendered as a deterministic string: tables in name order, columns in
    /// name order (so <c>ALTER … ADD COLUMN</c>'s physical ordering is irrelevant), rows in <c>rowid</c> order.
    /// Columns named in <paramref name="excluded"/> are omitted for their table.
    ///
    /// <para>🔴 <b>Two corrections from the owed review (lens 1 finding 3), because the name
    /// "byte_for_byte" was writing a cheque this helper did not cash.</b> (1) Cells used to render as
    /// <c>r.GetValue(i).ToString()</c>, which for a BLOB is the literal string <c>"System.Byte[]"</c> — measured:
    /// changing <c>companies.nic_api_username_enc</c> from <c>x'0102030405'</c> to nine <c>0xFF</c> bytes left the
    /// snapshot IDENTICAL, and there are four such columns, all of them the encrypted NIC credentials. BLOBs now
    /// render as hex. (2) Rows used to be sorted by their own rendered text, so a migration that reordered rows was
    /// invisible; they are now compared in <c>rowid</c> order, which is what "unchanged" means (no table in this
    /// schema is <c>WITHOUT ROWID</c>).</para>
    /// </summary>
    private static string SnapshotData(string dbPath, Dictionary<string, HashSet<string>> excluded)
    {
        using var conn = Open(dbPath);
        var sb = new StringBuilder();

        var tables = new List<string>();
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText =
                "SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%' ORDER BY name;";
            using var r = cmd.ExecuteReader();
            while (r.Read()) tables.Add(r.GetString(0));
        }

        foreach (var table in tables)
        {
            var cols = new List<string>();
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = $"PRAGMA table_info(\"{table}\");";
                using var r = cmd.ExecuteReader();
                while (r.Read()) cols.Add(r.GetString(1));
            }
            if (excluded.TryGetValue(table, out var skip)) cols.RemoveAll(skip.Contains);
            cols.Sort(StringComparer.Ordinal);
            if (cols.Count == 0) continue;

            var rows = new List<string>();
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText =
                    $"SELECT {string.Join(", ", cols.Select(x => $"\"{x}\""))} FROM \"{table}\" ORDER BY rowid;";
                using var r = cmd.ExecuteReader();
                while (r.Read())
                {
                    var cells = new List<string>();
                    for (var i = 0; i < cols.Count; i++)
                        cells.Add($"{cols[i]}={RenderCell(r, i)}");
                    rows.Add(string.Join(" | ", cells));
                }
            }

            sb.AppendLine($"[{table}] rows={rows.Count}");
            foreach (var row in rows) sb.AppendLine("  " + row);
        }

        SqliteConnection.ClearPool(conn);
        return sb.ToString();
    }

    /// <summary>One cell, rendered so that a BLOB shows its bytes rather than the type name.</summary>
    private static string RenderCell(SqliteDataReader r, int i)
    {
        if (r.IsDBNull(i)) return "<null>";
        var value = r.GetValue(i);
        return value is byte[] blob ? "0x" + Convert.ToHexString(blob) : value.ToString()!;
    }

    private static void ExecSql(string dbPath, string sql)
    {
        using var conn = Open(dbPath);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
        SqliteConnection.ClearPool(conn);
    }

    private static long ReadScalar(string dbPath, string sql)
    {
        using var conn = Open(dbPath);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        var v = Convert.ToInt64(cmd.ExecuteScalar());
        SqliteConnection.ClearPool(conn);
        return v;
    }

    private static string ReadText(string dbPath, string sql)
    {
        using var conn = Open(dbPath);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        var v = Convert.ToString(cmd.ExecuteScalar())!;
        SqliteConnection.ClearPool(conn);
        return v;
    }

    private static IReadOnlyList<string> ColumnNames(string dbPath, string table)
    {
        using var conn = Open(dbPath);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"PRAGMA table_info(\"{table}\");";
        var names = new List<string>();
        using (var r = cmd.ExecuteReader())
            while (r.Read()) names.Add(r.GetString(1));
        SqliteConnection.ClearPool(conn);
        return names;
    }

    /// <summary>The single-column contract "name|type|notnull|default|pk" (or "&lt;absent&gt;" when the column does
    /// not exist), matching the SchemaMigrationEquivalence comparison shape.</summary>
    private static string ColumnContract(string dbPath, string table, string column)
    {
        using var conn = Open(dbPath);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"PRAGMA table_info(\"{table}\");";
        var result = "<absent>";
        using (var r = cmd.ExecuteReader())
            while (r.Read())
            {
                if (!string.Equals(r.GetString(1), column, StringComparison.OrdinalIgnoreCase)) continue;
                var type = r.GetString(2);
                var notNull = r.GetInt64(3);
                var dflt = r.IsDBNull(4) ? "<null>" : r.GetString(4);
                var pk = r.GetInt64(5);
                result = $"{column} | {type} | notnull={notNull} | default={dflt} | pk={pk}";
            }
        SqliteConnection.ClearPool(conn);
        return result;
    }

    private static SqliteConnection Open(string dbPath)
    {
        var conn = new SqliteConnection(
            new SqliteConnectionStringBuilder { DataSource = dbPath, Mode = SqliteOpenMode.ReadWrite }.ToString());
        conn.Open();
        return conn;
    }
}
