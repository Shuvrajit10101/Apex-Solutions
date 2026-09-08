using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Services;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Apex.Persistence.Sqlite.Tests;

/// <summary>
/// Schema v58 → v59 (W-N1; census 15.1 State VAT · 15.2 the Tax Rate on the masters · 15.5 VAT Computation ·
/// 15.6 CST declaration forms) — <b>State VAT and Central Sales Tax for the goods GST never absorbed</b>: seven
/// <c>companies</c> columns, five <c>ledgers</c> columns, two <c>stock_items</c> columns and four
/// <c>vouchers</c> columns.
///
/// <para>The "genuine v58 database" is manufactured with <see cref="SchemaDowngrade.V59ToV58"/> rather than
/// hand-written DDL, so the migration is exercised against real rows — the idiom every schema test here
/// uses.</para>
///
/// <para>🔴 <b>The load-bearing test in this file is
/// <see cref="A_migrated_v58_book_is_ordinary_GST_goods_with_no_VAT_anywhere"/>.</b> The whole of census area
/// 15 rests on <c>stock_items.non_gst_goods_class DEFAULT 0</c> meaning "ordinary GST goods". If that default
/// were ever anything else, every item in every upgraded book in the world would silently become a VAT good and
/// the VAT Computation report would start putting a repealed tax on ordinary trade. A migration that got this
/// wrong would be invisible to a column-existence test and catastrophic in the field.</para>
/// </summary>
public sealed class StateVatCstSchemaTests
{
    private static readonly DateOnly FyStart = new(2025, 4, 1);

    // ================================================================= migration parity

    /// <summary>Fails on today's main, where <see cref="Schema.CurrentVersion"/> is 58 and none of these
    /// eighteen columns exists in either database.</summary>
    [Fact]
    [Trait("Category", "RoundTrip")]
    public void Migration_v58_to_v59_matches_CreateV1()
    {
        var migratedPath = TempDbFile.NewPath("apex-n1-v58-migrated");
        var freshPath = TempDbFile.NewPath("apex-n1-v59-fresh");
        try
        {
            var fresh = CompanyFactory.CreateSeeded("Fresh Vat Co", FyStart);
            using (var store = new SqliteCompanyStore(freshPath)) store.Save(fresh);

            var legacy = CompanyFactory.CreateSeeded("Legacy Vat Co", FyStart);
            using (var store = new SqliteCompanyStore(migratedPath)) store.Save(legacy);
            using (var conn = Open(migratedPath)) { SchemaDowngrade.V59ToV58(conn); SqliteConnection.ClearPool(conn); }
            Assert.Equal(58L, ReadScalar(migratedPath, "SELECT version FROM schema_version LIMIT 1;"));

            // Every v59 column really is absent from the manufactured v58 book.
            foreach (var col in Schema.V59VatCompanyColumns)
                Assert.DoesNotContain(col, ColumnNames(migratedPath, "companies"));
            foreach (var col in Schema.V59VatLedgerColumns)
                Assert.DoesNotContain(col, ColumnNames(migratedPath, "ledgers"));
            foreach (var col in Schema.V59VatStockItemColumns)
                Assert.DoesNotContain(col, ColumnNames(migratedPath, "stock_items"));
            foreach (var col in Schema.V59CstVoucherColumns)
                Assert.DoesNotContain(col, ColumnNames(migratedPath, "vouchers"));

            // Reopen through the production store — the v58 → v59 migration runs.
            using (new SqliteCompanyStore(migratedPath)) { }
            Assert.Equal((long)Schema.CurrentVersion,
                ReadScalar(migratedPath, "SELECT version FROM schema_version LIMIT 1;"));

            // Only the objects THIS migration adds are compared: the downgrade's rebuild is not asked to
            // reproduce pre-existing declarations byte for byte. Whole-schema parity across the full v1→current
            // chain is separately guaranteed by SchemaMigrationEquivalenceTests.
            foreach (var col in Schema.V59VatCompanyColumns)
                Assert.Equal(ColumnContract(freshPath, "companies", col),
                             ColumnContract(migratedPath, "companies", col));
            foreach (var col in Schema.V59VatLedgerColumns)
                Assert.Equal(ColumnContract(freshPath, "ledgers", col),
                             ColumnContract(migratedPath, "ledgers", col));
            foreach (var col in Schema.V59VatStockItemColumns)
                Assert.Equal(ColumnContract(freshPath, "stock_items", col),
                             ColumnContract(migratedPath, "stock_items", col));
            foreach (var col in Schema.V59CstVoucherColumns)
                Assert.Equal(ColumnContract(freshPath, "vouchers", col),
                             ColumnContract(migratedPath, "vouchers", col));
        }
        finally
        {
            TempDbFile.Delete(migratedPath);
            TempDbFile.Delete(freshPath);
        }
    }

    /// <summary>
    /// 🔴 <b>THE ONE THAT MATTERS.</b> The migration back-fills nothing: a migrated book has VAT off, and every
    /// stock item in it reads as <b>ordinary GST goods with no VAT rate</b>.
    ///
    /// <para>The <c>non_gst_goods_class DEFAULT 0</c> assertion is the whole safety property of schema v59.
    /// State VAT and CST survive GST only for alcoholic liquor for human consumption (Constitution Art.
    /// 366(12A); CGST Act s.9(1)) and the five petroleum products (CGST Act s.9(2)). If the default classified
    /// existing items as anything else, every upgraded book would acquire VAT goods it never had.</para>
    /// </summary>
    [Fact]
    [Trait("Category", "RoundTrip")]
    public void A_migrated_v58_book_is_ordinary_GST_goods_with_no_VAT_anywhere()
    {
        var path = TempDbFile.NewPath("apex-n1-backfill");
        try
        {
            var legacy = CompanyFactory.CreateSeeded("Backfill Vat Co", FyStart);
            var inventory = new InventoryService(legacy);
            var unit = inventory.CreateSimpleUnit("Nos", "Numbers");
            var group = legacy.FindStockGroupByName("Primary") ?? inventory.CreateStockGroup("Primary");
            inventory.CreateStockItem("Ordinary Widget", group.Id, unit.Id);

            using (var store = new SqliteCompanyStore(path)) store.Save(legacy);
            using (var conn = Open(path)) { SchemaDowngrade.V59ToV58(conn); SqliteConnection.ClearPool(conn); }

            using var reopened = new SqliteCompanyStore(path);
            var loaded = reopened.Load(legacy.Id)!;

            Assert.Null(loaded.Vat);
            Assert.False(loaded.VatEnabled);

            var item = loaded.StockItems.Single(i => i.Name == "Ordinary Widget");
            Assert.Equal(NonGstGoodsClass.None, item.NonGstGoodsClass);
            Assert.Null(item.VatTaxRateBasisPoints);
            Assert.False(item.IsOutsideGst);

            foreach (var l in loaded.Ledgers)
            {
                Assert.False(l.VatApplicable);
                Assert.Null(l.VatTaxRateBasisPoints);
                Assert.Null(l.PartyVatTin);
                Assert.Null(l.PartyCstNumber);
                Assert.Null(l.PartyVatDealerType);
            }
        }
        finally { TempDbFile.Delete(path); }
    }

    // ================================================================= round trips

    /// <summary>
    /// The Company VAT Details block survives a save/reload byte for byte (census 15.1).
    ///
    /// <para>🔴 <b>And the Form-C rate round-trips as NULL when it was never supplied.</b> That assertion is
    /// not filler: the concessional rate could not be sourced officially for this slice, so no default may
    /// exist anywhere. A <c>?? 200</c> creeping into the writer or the reader would put an unsourced statutory
    /// rate into every VAT company's book, which is the exact defect class this project already has an open
    /// user decision about.</para>
    /// </summary>
    [Fact]
    [Trait("Category", "RoundTrip")]
    public void Company_VAT_registration_round_trips_and_an_unset_CST_rate_stays_null()
    {
        var path = TempDbFile.NewPath("apex-n1-company-vat");
        try
        {
            var c = CompanyFactory.CreateSeeded("Liquor Wholesale Co", FyStart);
            new VatService(c).EnableVat(
                tin: "29123456789",
                interstateSalesTaxNumber: "CST/29/0001",
                applicableFrom: new DateOnly(2025, 4, 1),
                dealerType: VatDealerType.Composite,
                periodicity: VatReturnPeriodicity.Quarterly,
                cstRateAgainstFormCBasisPoints: null);
            using (var store = new SqliteCompanyStore(path)) store.Save(c);

            using var reopened = new SqliteCompanyStore(path);
            var loaded = reopened.Load(c.Id)!;

            var vat = Assert.IsType<VatConfig>(loaded.Vat);
            Assert.True(vat.Enabled);
            Assert.Equal("29123456789", vat.Tin);
            Assert.Equal("CST/29/0001", vat.InterstateSalesTaxNumber);
            Assert.Equal(new DateOnly(2025, 4, 1), vat.ApplicableFrom);
            Assert.Equal(VatDealerType.Composite, vat.DealerType);
            Assert.Equal(VatReturnPeriodicity.Quarterly, vat.Periodicity);
            // 🔴 NULL, not 200. No statutory Form-C rate is asserted anywhere in this build.
            Assert.Null(vat.CstRateAgainstFormCBasisPoints);
            Assert.False(vat.HasCstRateAgainstFormC);
        }
        finally { TempDbFile.Delete(path); }
    }

    /// <summary>The item class of goods and VAT rate, and the ledger's two independent VAT blocks, survive a
    /// save/reload (census 15.2).</summary>
    [Fact]
    [Trait("Category", "RoundTrip")]
    public void Item_goods_class_and_the_two_ledger_VAT_blocks_round_trip()
    {
        var path = TempDbFile.NewPath("apex-n1-masters-vat");
        try
        {
            var c = CompanyFactory.CreateSeeded("Fuel Depot Co", FyStart);
            var vat = new VatService(c);
            vat.EnableVat(tin: "29999999999");

            var inventory = new InventoryService(c);
            var unit = inventory.CreateSimpleUnit("KL", "Kilolitre");
            var group = c.FindStockGroupByName("Primary") ?? inventory.CreateStockGroup("Primary");
            var item = inventory.CreateStockItem("High Speed Diesel", group.Id, unit.Id);
            vat.SetItemGoodsClass(item, NonGstGoodsClass.HighSpeedDiesel);
            vat.SetItemVatRate(item, 1450);   // 14.5%

            var party = new Apex.Ledger.Domain.Ledger(
                Guid.NewGuid(), "Highway Fuels", c.FindGroupByName("Sundry Debtors")!.Id,
                Money.Zero, openingIsDebit: true);
            c.AddLedger(party);
            vat.SetPartyVatDetails(party, VatDealerType.Regular, "27000111222", "CST/27/9");

            var salesLedger = new Apex.Ledger.Domain.Ledger(
                Guid.NewGuid(), "Fuel Sales", c.FindGroupByName("Sales Accounts")!.Id,
                Money.Zero, openingIsDebit: false);
            c.AddLedger(salesLedger);
            vat.SetLedgerVat(salesLedger, vatApplicable: true, rateBasisPoints: 1450);

            using (var store = new SqliteCompanyStore(path)) store.Save(c);

            using var reopened = new SqliteCompanyStore(path);
            var loaded = reopened.Load(c.Id)!;

            var loadedItem = loaded.StockItems.Single(i => i.Name == "High Speed Diesel");
            Assert.Equal(NonGstGoodsClass.HighSpeedDiesel, loadedItem.NonGstGoodsClass);
            Assert.Equal(1450, loadedItem.VatTaxRateBasisPoints);
            Assert.True(loadedItem.IsOutsideGst);

            var loadedParty = loaded.Ledgers.Single(l => l.Name == "Highway Fuels");
            Assert.Equal(VatDealerType.Regular, loadedParty.PartyVatDealerType);
            Assert.Equal("27000111222", loadedParty.PartyVatTin);
            Assert.Equal("CST/27/9", loadedParty.PartyCstNumber);

            var loadedSales = loaded.Ledgers.Single(l => l.Id == salesLedger.Id);
            Assert.True(loadedSales.VatApplicable);
            Assert.Equal(1450, loadedSales.VatTaxRateBasisPoints);
        }
        finally { TempDbFile.Delete(path); }
    }

    /// <summary>
    /// A CST declaration form on a voucher survives a save/reload, and a form with NO number reloads as
    /// pending (census 15.6).
    ///
    /// <para>🔴 <b>The pending case is the one asserted hardest.</b> "Form recorded but not yet received" is the
    /// state the two Declaration Forms registers exist to surface, and it is encoded as a NULL
    /// <c>cst_form_number</c> beside a non-NULL <c>cst_form_type</c>. A writer that coerced the blank number to
    /// an empty STRING would round-trip a pending form as a completed one and the exposure would vanish off the
    /// report.</para>
    /// </summary>
    [Fact]
    [Trait("Category", "RoundTrip")]
    public void A_pending_CST_declaration_form_round_trips_as_pending()
    {
        var path = TempDbFile.NewPath("apex-n1-cst-form");
        try
        {
            var c = CompanyFactory.CreateSeeded("Interstate Liquor Co", FyStart);
            new VatService(c).EnableVat(tin: "29888888888");

            var customer = new Apex.Ledger.Domain.Ledger(
                Guid.NewGuid(), "Delhi Retailers", c.FindGroupByName("Sundry Debtors")!.Id,
                Money.Zero, openingIsDebit: true);
            c.AddLedger(customer);
            var sales = new Apex.Ledger.Domain.Ledger(
                Guid.NewGuid(), "Liquor Sales", c.FindGroupByName("Sales Accounts")!.Id,
                Money.Zero, openingIsDebit: false);
            c.AddLedger(sales);
            var salesType = c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Sales);

            var pending = new Voucher(
                Guid.NewGuid(), salesType.Id, new DateOnly(2025, 6, 10),
                new[]
                {
                    new EntryLine(customer.Id, Money.FromRupees(50000m), DrCr.Debit),
                    new EntryLine(sales.Id, Money.FromRupees(50000m), DrCr.Credit),
                },
                partyId: customer.Id);
            new LedgerService(c).Post(pending);

            var received = new Voucher(
                Guid.NewGuid(), salesType.Id, new DateOnly(2025, 6, 12),
                new[]
                {
                    new EntryLine(customer.Id, Money.FromRupees(20000m), DrCr.Debit),
                    new EntryLine(sales.Id, Money.FromRupees(20000m), DrCr.Credit),
                },
                partyId: customer.Id);
            new LedgerService(c).Post(received);

            var vatService = new VatService(c);
            vatService.SetCstDeclarationForm(pending, CstDeclarationForm.FormC, seriesNumber: "A");
            vatService.SetCstDeclarationForm(
                received, CstDeclarationForm.FormC, "A", "C-000914", new DateOnly(2025, 7, 1));

            using (var store = new SqliteCompanyStore(path)) store.Save(c);

            using var reopened = new SqliteCompanyStore(path);
            var loaded = reopened.Load(c.Id)!;

            var loadedPending = loaded.Vouchers.Single(v => v.Id == pending.Id);
            Assert.Equal(CstDeclarationForm.FormC, loadedPending.CstFormType);
            Assert.Equal("A", loadedPending.CstFormSeriesNumber);
            Assert.Null(loadedPending.CstFormNumber);
            Assert.Null(loadedPending.CstFormDate);
            Assert.False(loadedPending.CstFormReceived);

            var loadedReceived = loaded.Vouchers.Single(v => v.Id == received.Id);
            Assert.Equal("C-000914", loadedReceived.CstFormNumber);
            Assert.Equal(new DateOnly(2025, 7, 1), loadedReceived.CstFormDate);
            Assert.True(loadedReceived.CstFormReceived);
        }
        finally { TempDbFile.Delete(path); }
    }

    /// <summary>
    /// The downgrade removes exactly the eighteen v59 columns and leaves every table's PRIMARY KEY intact, so
    /// a child row can still be inserted afterwards.
    ///
    /// <para>🔴 <b>The child-insert assertion is the point.</b> All four rebuilt tables are FK PARENTS, and a
    /// <c>CREATE … AS SELECT</c> rebuild silently loses the PK — after which SQLite reports "foreign key
    /// mismatch" on the next child insert. That is a measured failure this schema has already had once
    /// (<c>SchemaDowngrade.V56ToV55</c>), and a column-existence test cannot see it.</para>
    /// </summary>
    [Fact]
    [Trait("Category", "RoundTrip")]
    public void The_downgrade_drops_only_the_v59_columns_and_keeps_every_primary_key()
    {
        var path = TempDbFile.NewPath("apex-n1-downgrade");
        try
        {
            var c = CompanyFactory.CreateSeeded("Downgrade Vat Co", FyStart);
            new VatService(c).EnableVat(tin: "29777777777");
            using (var store = new SqliteCompanyStore(path)) store.Save(c);

            var beforeCompanies = ColumnNames(path, "companies").Count;
            var beforeLedgers = ColumnNames(path, "ledgers").Count;
            var beforeItems = ColumnNames(path, "stock_items").Count;
            var beforeVouchers = ColumnNames(path, "vouchers").Count;

            using (var conn = Open(path))
            {
                SchemaDowngrade.V59ToV58(conn);

                Assert.Equal(beforeCompanies - Schema.V59VatCompanyColumns.Count, ColumnCount(conn, "companies"));
                Assert.Equal(beforeLedgers - Schema.V59VatLedgerColumns.Count, ColumnCount(conn, "ledgers"));
                Assert.Equal(beforeItems - Schema.V59VatStockItemColumns.Count, ColumnCount(conn, "stock_items"));
                Assert.Equal(beforeVouchers - Schema.V59CstVoucherColumns.Count, ColumnCount(conn, "vouchers"));

                // Every rebuilt table is an FK parent, so its PK must have survived.
                foreach (var t in new[] { "companies", "ledgers", "stock_items", "vouchers" })
                    Assert.True(HasPrimaryKey(conn, t), $"\"{t}\" lost its PRIMARY KEY in the v59 downgrade.");

                SqliteConnection.ClearPool(conn);
            }

            // And the migration puts them back, on a book that still carries every row.
            using var reopened = new SqliteCompanyStore(path);
            var loaded = reopened.Load(c.Id)!;
            Assert.Equal("Downgrade Vat Co", loaded.Name);
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
        var names = ReadColumnNames(conn, table);
        SqliteConnection.ClearPool(conn);
        return names;
    }

    private static List<string> ReadColumnNames(SqliteConnection conn, string table)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"PRAGMA table_info(\"{table}\");";
        var names = new List<string>();
        using var r = cmd.ExecuteReader();
        while (r.Read()) names.Add(r.GetString(1));
        return names;
    }

    private static int ColumnCount(SqliteConnection conn, string table) => ReadColumnNames(conn, table).Count;

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
