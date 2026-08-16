using System.Text;
using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Services;
using Microsoft.Data.Sqlite;
using Xunit;
using Domain = Apex.Ledger.Domain;

namespace Apex.Persistence.Sqlite.Tests;

/// <summary>
/// Schema v50 → v51 — the <b>GST five-level hierarchy masters</b> (plan.md Phase 10.10 WF-1 / register IV-1, slice S1).
/// This bump adds the three masters TallyPrime resolves GST through and we never had — a narrow
/// <see cref="MasterGstDetails"/> block on the <b>Stock Group</b>, on the accounting <b>Group</b> and on the
/// <b>company</b> (<see cref="GstConfig.DefaultGst"/>) — plus TallyPrime's <b>two</b> separate source-order options
/// (<see cref="GstConfig.SourceOfHsnSacDetails"/>, <see cref="GstConfig.SourceOfGstRate"/>).
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
/// dropped, and <see cref="FreshCompanies_get_LedgerFirst"/> is the test that fails if someone "simplifies" the
/// back-fill into the <c>DEFAULT</c>. Neither can pass the other's mutation.</para>
///
/// <para>The "genuine v50 database" is manufactured with <see cref="SchemaDowngrade.V51ToV50"/> rather than
/// hand-written DDL, so the migration is exercised against real rows.</para>
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

            // Snapshot the v51 database MINUS everything v51 added — i.e. exactly the information a genuine v50
            // database holds. That is the thing the upgrade must not disturb.
            var before = SnapshotData(dbPath, excluded: V51Columns());

            using (var conn = Open(dbPath))
            {
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
    /// The full contents of every user table, rendered as a deterministic string: tables in name order, columns in
    /// name order (so <c>ALTER … ADD COLUMN</c>'s physical ordering is irrelevant), rows in a stable ordering of
    /// their own rendered values. Columns named in <paramref name="excluded"/> are omitted for their table.
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
                cmd.CommandText = $"SELECT {string.Join(", ", cols.Select(x => $"\"{x}\""))} FROM \"{table}\";";
                using var r = cmd.ExecuteReader();
                while (r.Read())
                {
                    var cells = new List<string>();
                    for (var i = 0; i < cols.Count; i++)
                        cells.Add($"{cols[i]}={(r.IsDBNull(i) ? "<null>" : r.GetValue(i).ToString())}");
                    rows.Add(string.Join(" | ", cells));
                }
            }
            rows.Sort(StringComparer.Ordinal);

            sb.AppendLine($"[{table}] rows={rows.Count}");
            foreach (var row in rows) sb.AppendLine("  " + row);
        }

        SqliteConnection.ClearPool(conn);
        return sb.ToString();
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
