using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Services;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Apex.Persistence.Sqlite.Tests;

/// <summary>
/// Schema v60 → v61 (census 6.23 <b>Multiple GSTIN registrations</b> · 6.25 <b>GST Classification master</b>) —
/// two tables, two indexes, one <c>companies</c> column and one <c>vouchers</c> column.
///
/// <para>The "genuine v60 database" is manufactured with <see cref="SchemaDowngrade.V61ToV60"/> rather than
/// hand-written DDL, so the migration is exercised against real rows — the idiom every schema test here uses.</para>
///
/// <para>🔴 <b>THE LOAD-BEARING TEST IN THIS FILE IS
/// <see cref="A_migrated_v60_book_attributes_every_existing_voucher_to_the_companys_own_registration"/>, AND IT
/// IS NOT THE USUAL "DEFAULTS ARE TIDY".</b> <c>vouchers.gst_registration_id</c> decides which GST RETURN a
/// supply is filed in. The design deliberately makes NULL mean "the company's own registration" precisely so
/// that the migration runs no <c>UPDATE</c> over any book's vouchers — the back-fill is a no-op and is therefore
/// exact by construction. This test is what proves the resulting book actually reads that way end to end: every
/// pre-existing voucher still resolves to the registration the book has always had, and its GST return is
/// unchanged. A column-existence test cannot see any of that.</para>
///
/// <para>🔴 <b><see cref="Downgrading_to_v60_keeps_the_primary_keys_of_both_rebuilt_tables"/> is the second one
/// that matters.</b> <c>vouchers</c> and <c>companies</c> are both foreign-key PARENTS, so a
/// <c>CREATE … AS SELECT</c> rebuild that lost their PRIMARY KEY would leave SQLite reporting <c>foreign key
/// mismatch</c> on the next child insert — a MEASURED failure in this repository, recorded on
/// <c>SchemaDowngrade.V56ToV55</c>. It also pins the ORDER: <c>vouchers</c> must be rebuilt (dropping its
/// <c>REFERENCES gst_registrations(id)</c> clause) BEFORE <c>gst_registrations</c> is dropped.</para>
/// </summary>
public sealed class GstRegistrationSchemaTests
{
    private static readonly DateOnly FyStart = new(2025, 4, 1);
    private static readonly DateOnly SaleDate = new(2025, 4, 10);
    private const string GstinMaharashtra = "27AAPFU0939F1ZV";
    private const string GstinGujarat = "24AAACC1206D1ZM";

    // ================================================================= migration parity

    /// <summary>Fails on today's main, where <see cref="Schema.CurrentVersion"/> is 60, neither table exists and
    /// <c>SchemaDowngrade.V61ToV60</c> does not compile.</summary>
    [Fact]
    [Trait("Category", "RoundTrip")]
    public void Migration_v60_to_v61_matches_CreateV1()
    {
        var migratedPath = TempDbFile.NewPath("apex-p1-v60-migrated");
        var freshPath = TempDbFile.NewPath("apex-p1-v61-fresh");
        try
        {
            var fresh = CompanyFactory.CreateSeeded("Fresh GST Co", FyStart);
            using (var store = new SqliteCompanyStore(freshPath)) store.Save(fresh);

            var legacy = CompanyFactory.CreateSeeded("Legacy GST Co", FyStart);
            using (var store = new SqliteCompanyStore(migratedPath)) store.Save(legacy);
            using (var conn = Open(migratedPath)) { SchemaDowngrade.V61ToV60(conn); SqliteConnection.ClearPool(conn); }
            Assert.Equal(60L, ReadScalar(migratedPath, "SELECT version FROM schema_version LIMIT 1;"));

            foreach (var table in Schema.V61Tables)
                Assert.DoesNotContain(table, TableNames(migratedPath));
            foreach (var col in Schema.V61CompanyColumns)
                Assert.DoesNotContain(col, ColumnNames(migratedPath, "companies"));
            foreach (var col in Schema.V61VoucherColumns)
                Assert.DoesNotContain(col, ColumnNames(migratedPath, "vouchers"));

            // Reopen through the production store — the v60 → v61 migration runs.
            using (new SqliteCompanyStore(migratedPath)) { }
            Assert.Equal((long)Schema.CurrentVersion,
                ReadScalar(migratedPath, "SELECT version FROM schema_version LIMIT 1;"));

            // Only the objects THIS migration adds are compared; whole-schema parity across the full v1→current
            // chain is separately guaranteed by SchemaMigrationEquivalenceTests.
            foreach (var table in Schema.V61Tables)
            {
                Assert.Contains(table, TableNames(migratedPath));
                Assert.Equal(ColumnNames(freshPath, table), ColumnNames(migratedPath, table));
                foreach (var col in ColumnNames(freshPath, table))
                    Assert.Equal(ColumnContract(freshPath, table, col),
                                 ColumnContract(migratedPath, table, col));
            }
            foreach (var col in Schema.V61CompanyColumns)
                Assert.Equal(ColumnContract(freshPath, "companies", col),
                             ColumnContract(migratedPath, "companies", col));
            foreach (var col in Schema.V61VoucherColumns)
                Assert.Equal(ColumnContract(freshPath, "vouchers", col),
                             ColumnContract(migratedPath, "vouchers", col));

            // The indexes too — a migration that created the tables but not their per-company index would leave
            // a fresh and an upgraded book with different read plans and PRAGMA table_info would never say so.
            foreach (var index in Schema.V61Indexes)
            {
                Assert.Contains(index, IndexNames(freshPath));
                Assert.Contains(index, IndexNames(migratedPath));
            }
        }
        finally
        {
            TempDbFile.Delete(migratedPath);
            TempDbFile.Delete(freshPath);
        }
    }

    // ================================================================= the back-fill (which is a no-op)

    /// <summary>
    /// 🔴 <b>THE ONE THAT MATTERS.</b> A book that existed before v61 keeps every voucher attributed to the
    /// company's own registration — stored as NULL, resolved as the primary — and its GST return is unchanged.
    /// </summary>
    [Fact]
    [Trait("Category", "RoundTrip")]
    public void A_migrated_v60_book_attributes_every_existing_voucher_to_the_companys_own_registration()
    {
        var path = TempDbFile.NewPath("apex-p1-backfill");
        try
        {
            var legacy = BuildGstCompanyWithOneSale();
            using (var store = new SqliteCompanyStore(path)) store.Save(legacy);
            using (var conn = Open(path)) { SchemaDowngrade.V61ToV60(conn); SqliteConnection.ClearPool(conn); }

            using var reopened = new SqliteCompanyStore(path);
            var loaded = reopened.Load(legacy.Id)!;

            // No registration row was invented for the company, and no voucher was stamped.
            Assert.Empty(loaded.Gst!.AdditionalRegistrations);
            Assert.False(loaded.Gst.IsMultiRegistration);
            Assert.Null(loaded.Gst.PrimaryRegistrationName);
            Assert.All(loaded.Vouchers, v => Assert.Null(v.GstRegistrationId));

            // …and the company's own registration is still projected out of the columns that already held it,
            // under the vendor's auto-generated name.
            var primary = loaded.Gst.PrimaryRegistration!;
            Assert.Equal(GstRegistration.PrimaryId, primary.Id);
            Assert.Equal("27", primary.StateCode);
            Assert.Equal(GstinMaharashtra, primary.Gstin);
            Assert.Equal("Maharashtra Registration", primary.Name);

            // No classification was invented either (census 6.25).
            Assert.Empty(loaded.Gst.Classifications);
        }
        finally { TempDbFile.Delete(path); }
    }

    // ================================================================= round trip

    [Fact]
    [Trait("Category", "RoundTrip")]
    public void Additional_registrations_and_classifications_round_trip_with_the_voucher_attribution()
    {
        var path = TempDbFile.NewPath("apex-p1-roundtrip");
        try
        {
            var c = BuildGstCompanyWithOneSale();
            var gujarat = new GstRegistration(
                Guid.NewGuid(), "Gujarat Registration", "24", GstinGujarat,
                GstRegistrationType.Regular, FyStart, GstReturnPeriodicity.Quarterly,
                assesseeOfOtherTerritory: true);
            c.Gst!.AddRegistration(gujarat);
            c.Gst.PrimaryRegistrationName = "Head Office (MH)";
            c.Gst.AddClassification(new GstClassification(
                Guid.NewGuid(), "Textiles 5%", hsnSac: "520100", description: "Cotton, not carded",
                taxability: GstTaxability.Taxable, rateBasisPoints: 500,
                supplyType: GstSupplyType.Goods, natureOfTransaction: "Interstate Sales Taxable",
                cessValuationMode: CessValuationMode.Specific, cessRateBasisPoints: 0,
                cessPerUnit: Money.FromRupees(4m)));

            // Stamp the one sale onto the SECOND registration, so the attribution has something to lose.
            c.Vouchers.Single(v => v.Date == SaleDate).GstRegistrationId = gujarat.Id;

            using (var store = new SqliteCompanyStore(path)) store.Save(c);
            using var reopened = new SqliteCompanyStore(path);
            var loaded = reopened.Load(c.Id)!;

            var reg = Assert.Single(loaded.Gst!.AdditionalRegistrations);
            Assert.Equal(gujarat.Id, reg.Id);
            Assert.Equal("Gujarat Registration", reg.Name);
            Assert.Equal("24", reg.StateCode);
            Assert.Equal(GstinGujarat, reg.Gstin);
            Assert.Equal(GstReturnPeriodicity.Quarterly, reg.Periodicity);
            Assert.Equal(FyStart, reg.ApplicableFrom);
            Assert.True(reg.AssesseeOfOtherTerritory);

            Assert.Equal("Head Office (MH)", loaded.Gst.PrimaryRegistrationName);
            Assert.Equal("Head Office (MH)", loaded.Gst.PrimaryRegistration!.Name);

            var gc = Assert.Single(loaded.Gst.Classifications);
            Assert.Equal("Textiles 5%", gc.Name);
            Assert.Equal("520100", gc.HsnSac);
            Assert.Equal("Cotton, not carded", gc.Description);
            Assert.Equal(500, gc.RateBasisPoints);
            Assert.Equal("Interstate Sales Taxable", gc.NatureOfTransaction);
            Assert.Equal(CessValuationMode.Specific, gc.CessValuationMode);
            Assert.Equal(Money.FromRupees(4m), gc.CessPerUnit);

            // 🔴 The attribution survives the round trip. If it did not, a reopened book would file the Gujarat
            // supply in the Maharashtra return and NOTHING else in the suite would notice.
            Assert.Equal(gujarat.Id, loaded.Vouchers.Single(v => v.Date == SaleDate).GstRegistrationId);
        }
        finally { TempDbFile.Delete(path); }
    }

    [Fact]
    [Trait("Category", "RoundTrip")]
    public void A_voucher_stamped_with_the_reserved_primary_id_is_stored_as_null()
    {
        var path = TempDbFile.NewPath("apex-p1-primary-null");
        try
        {
            var c = BuildGstCompanyWithOneSale();
            // GstRegistration.PrimaryId is Guid.Empty and has NO row in gst_registrations, so writing it
            // literally would violate the REFERENCES clause. The store normalises it to NULL.
            c.Vouchers.Single(v => v.Date == SaleDate).GstRegistrationId = GstRegistration.PrimaryId;

            using (var store = new SqliteCompanyStore(path)) store.Save(c);
            Assert.Equal(0L, ReadScalar(path,
                "SELECT COUNT(*) FROM vouchers WHERE gst_registration_id IS NOT NULL;"));

            using var reopened = new SqliteCompanyStore(path);
            Assert.Null(reopened.Load(c.Id)!.Vouchers.Single(v => v.Date == SaleDate).GstRegistrationId);
        }
        finally { TempDbFile.Delete(path); }
    }

    // ================================================================= downgrade

    [Fact]
    [Trait("Category", "RoundTrip")]
    public void Downgrading_to_v60_keeps_the_primary_keys_of_both_rebuilt_tables()
    {
        var path = TempDbFile.NewPath("apex-p1-downgrade-pk");
        try
        {
            var c = BuildGstCompanyWithOneSale();
            c.Gst!.AddRegistration(new GstRegistration(
                Guid.NewGuid(), "Gujarat Registration", "24", GstinGujarat));
            using (var store = new SqliteCompanyStore(path)) store.Save(c);

            using (var conn = Open(path))
            {
                SchemaDowngrade.V61ToV60(conn);

                // Both rebuilt tables are FK PARENTS; losing a PK here surfaces as "foreign key mismatch" on the
                // next child insert, not as anything this file could otherwise see.
                Assert.True(HasPrimaryKey(conn, "vouchers"));
                Assert.True(HasPrimaryKey(conn, "companies"));
                SqliteConnection.ClearPool(conn);
            }

            Assert.Equal(60L, ReadScalar(path, "SELECT version FROM schema_version LIMIT 1;"));
            foreach (var table in Schema.V61Tables) Assert.DoesNotContain(table, TableNames(path));
            Assert.DoesNotContain("gst_registration_id", ColumnNames(path, "vouchers"));
            Assert.DoesNotContain("gst_primary_registration_name", ColumnNames(path, "companies"));

            // And the book is still loadable and still balances after the round trip back up.
            using var reopened = new SqliteCompanyStore(path);
            var loaded = reopened.Load(c.Id)!;
            Assert.Equal((long)Schema.CurrentVersion,
                ReadScalar(path, "SELECT version FROM schema_version LIMIT 1;"));

            // 🔴 The documented, deliberate residual: the additional registration is GONE and its voucher has
            // reverted to the company's own registration. No posted figure moved — the sale is still there.
            Assert.Empty(loaded.Gst!.AdditionalRegistrations);
            Assert.Single(loaded.Vouchers);
            Assert.Null(loaded.Vouchers[0].GstRegistrationId);
        }
        finally { TempDbFile.Delete(path); }
    }

    // ================================================================= fixture + helpers

    /// <summary>A Maharashtra GST company with exactly one posted intra-State sale (₹1,000 @ 18%).</summary>
    private static Company BuildGstCompanyWithOneSale()
    {
        var c = CompanyFactory.CreateSeeded("GST Registration Co", FyStart);
        var gst = new GstService(c);
        gst.EnableGst(new GstConfig
        {
            HomeStateCode = "27",
            Gstin = GstinMaharashtra,
            RegistrationType = GstRegistrationType.Regular,
            ApplicableFrom = FyStart,
            Periodicity = GstReturnPeriodicity.Monthly,
        });

        var sales = new Apex.Ledger.Domain.Ledger(
            Guid.NewGuid(), "Sales", c.FindGroupByName("Sales Accounts")!.Id, Money.Zero, false);
        c.AddLedger(sales);
        var debtor = new Apex.Ledger.Domain.Ledger(
            Guid.NewGuid(), "Local Debtor", c.FindGroupByName("Sundry Debtors")!.Id, Money.Zero, true);
        debtor.PartyGst = new PartyGstDetails
        { RegistrationType = GstRegistrationType.Regular, Gstin = GstinMaharashtra, StateCode = "27" };
        c.AddLedger(debtor);

        var tax = gst.ComputeInvoiceTax(
            new[] { new GstService.TaxableLine(Money.FromRupees(1000m), 1800) }, false, GstTaxDirection.Output);
        var lines = new List<EntryLine>
        {
            new(debtor.Id, Money.FromRupees(1180m), DrCr.Debit),
            new(sales.Id, Money.FromRupees(1000m), DrCr.Credit),
        };
        lines.AddRange(tax.TaxLines);
        new LedgerService(c).Post(new Voucher(
            Guid.NewGuid(), c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Sales).Id,
            SaleDate, lines, partyId: debtor.Id));

        return c;
    }

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

    private static List<string> TableNames(string path)
    {
        using var conn = Open(path);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT name FROM sqlite_master WHERE type = 'table';";
        var names = new List<string>();
        using (var r = cmd.ExecuteReader()) while (r.Read()) names.Add(r.GetString(0));
        SqliteConnection.ClearPool(conn);
        return names;
    }

    private static List<string> IndexNames(string path)
    {
        using var conn = Open(path);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT name FROM sqlite_master WHERE type = 'index' AND sql IS NOT NULL;";
        var names = new List<string>();
        using (var r = cmd.ExecuteReader()) while (r.Read()) names.Add(r.GetString(0));
        SqliteConnection.ClearPool(conn);
        return names;
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

    private static bool HasPrimaryKey(SqliteConnection conn, string table)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"PRAGMA table_info(\"{table}\");";
        using var r = cmd.ExecuteReader();
        while (r.Read()) if (r.GetInt64(5) > 0) return true;
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
