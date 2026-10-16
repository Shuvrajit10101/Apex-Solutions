using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Services;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Apex.Persistence.Sqlite.Tests;

/// <summary>
/// Schema v65 → v66 (<b>USER RULING 30(3)+(4)</b>; census rows <b>5.5</b> and <b>6.23</b>; gap register
/// <b>T2-121(ii)</b>, <b>T2-125</b>) — <c>voucher_types.retain_original_voucher_no</c> and
/// <c>gstr2b_snapshots.gst_registration_id</c>, in ONE migration.
///
/// <para>Older databases are manufactured by walking the ladder DOWN from current with
/// <see cref="SchemaDowngrade"/> rather than from hand-written DDL, so the migration is exercised against real
/// rows — the idiom every schema test in this project uses.</para>
///
/// <para>🔴 <b>WHAT THIS FILE CHECKS BEYOND "THE COLUMNS EXIST", BECAUSE COLUMN-EXISTENCE PROVES NOTHING.</b>
/// <list type="number">
///   <item><b>The migrated contract equals the fresh one</b>, column for column, on BOTH tables — the
///   byte-identity rule this repository treats as absolute, pinned here with a readable message rather than only
///   in the whole-schema sweep.</item>
///   <item><b>BOTH tables keep their PRIMARY KEY across the downgrade.</b> Both are FK PARENTS
///   (<c>vouchers.type_id</c> / <c>inventory_vouchers.type_id</c> → <c>voucher_types(id)</c>, and
///   <c>gstr2b_lines.snapshot_id</c> → <c>gstr2b_snapshots(id)</c>), and a <c>CREATE … AS SELECT</c> rebuild of a
///   parent silently loses the PK — the <c>foreign key mismatch</c> failure <c>V56ToV55</c> records. This is why
///   <c>V66ToV65</c> uses <c>RebuildPreservingShape</c> twice.</item>
///   <item><b>The migration moves NOTHING.</b> v66 has no <c>UPDATE</c> at all, and ER-13 says so: every existing
///   voucher type must plan the identical insertion afterwards, and every existing snapshot must still attribute
///   to the registration it always did.</item>
///   <item><b>Both values actually ROUND-TRIP through the production store</b> — a column nothing reads or writes
///   is dead schema, which is the shape a migration test most easily fakes passing.</item>
/// </list></para>
/// </summary>
public sealed class RegistrationAttributionSchemaTests
{
    private static readonly DateOnly FyStart = new(2024, 4, 1);

    private const string Karnataka = "29";

    private static string GstinFor(string stateCode, string pan)
    {
        var body = stateCode + pan + "1Z";
        return body + Gstin.ComputeCheckDigit(body + "0");
    }

    // ================================================================= migration parity

    /// <summary>
    /// Both v66 tables end up byte-identical in contract (name / type / NOT NULL / DEFAULT / PK on every column)
    /// to what <c>CreateV1</c> gives a fresh book. A DEFAULT drifting between an <c>ALTER</c> and
    /// <see cref="Schema.CreateV1"/> is caught here with a readable message.
    /// </summary>
    [Fact]
    [Trait("Category", "RoundTrip")]
    public void Migration_v65_to_v66_matches_CreateV1_on_both_tables()
    {
        var migratedPath = TempDbFile.NewPath("apex-v66-migrated");
        var freshPath = TempDbFile.NewPath("apex-v66-fresh");
        try
        {
            using (var store = new SqliteCompanyStore(freshPath))
                store.Save(CompanyFactory.CreateSeeded("Fresh Attribution Co", FyStart));
            using (var store = new SqliteCompanyStore(migratedPath))
                store.Save(CompanyFactory.CreateSeeded("Legacy Attribution Co", FyStart));

            // ONE rung only — this test is about the v65 → v66 step in isolation.
            using (var conn = Open(migratedPath)) { SchemaDowngrade.V66ToV65(conn); SqliteConnection.ClearPool(conn); }
            Assert.Equal(65L, ReadScalar(migratedPath, "SELECT version FROM schema_version LIMIT 1;"));

            // The downgraded book genuinely lacks them — otherwise the "migration" below proves nothing.
            foreach (var col in Schema.V66VoucherTypeColumns)
                Assert.DoesNotContain(col, ColumnNames(migratedPath, "voucher_types"));
            foreach (var col in Schema.V66Gstr2bSnapshotColumns)
                Assert.DoesNotContain(col, ColumnNames(migratedPath, "gstr2b_snapshots"));

            // Reopen through the production store — the v65 → v66 migration runs.
            using (new SqliteCompanyStore(migratedPath)) { }
            Assert.Equal((long)Schema.CurrentVersion,
                ReadScalar(migratedPath, "SELECT version FROM schema_version LIMIT 1;"));

            Assert.Equal(TableInfo(freshPath, "voucher_types"), TableInfo(migratedPath, "voucher_types"));
            Assert.Equal(TableInfo(freshPath, "gstr2b_snapshots"), TableInfo(migratedPath, "gstr2b_snapshots"));
        }
        finally
        {
            TempDbFile.Delete(migratedPath);
            TempDbFile.Delete(freshPath);
        }
    }

    /// <summary>
    /// 🔴 <b>BOTH v66 TABLES ARE FK PARENTS</b>, so the downgrade must rebuild each with
    /// <c>RebuildPreservingShape</c> and never with a <c>CREATE … AS SELECT</c>, which silently loses the PRIMARY
    /// KEY. This asserts both PKs survive the round trip, that no FK anywhere in the file is left dangling, that
    /// each table's own index survived the rebuild, and that the file climbs back up clean.
    /// </summary>
    [Fact]
    [Trait("Category", "RoundTrip")]
    public void The_v66_downgrade_preserves_both_primary_keys_their_indexes_and_every_foreign_key()
    {
        var path = TempDbFile.NewPath("apex-v66-fk");
        try
        {
            using (var store = new SqliteCompanyStore(path))
                store.Save(CompanyFactory.CreateSeeded("FK Attribution Co", FyStart));

            using (var conn = Open(path)) { SchemaDowngrade.V66ToV65(conn); SqliteConnection.ClearPool(conn); }

            Assert.Equal(new List<string> { "id" }, PrimaryKeyColumns(path, "voucher_types"));
            Assert.Equal(new List<string> { "id" }, PrimaryKeyColumns(path, "gstr2b_snapshots"));

            // 🔴 DROP TABLE drops every index with it, and RebuildPreservingShape replays them from
            // sqlite_master. Both tables carry a company index; losing one is a silent performance regression a
            // PK assertion cannot see.
            Assert.Contains("ix_voucher_types_company", IndexNames(path, "voucher_types"));
            Assert.Contains("ix_gstr2b_snapshots_company", IndexNames(path, "gstr2b_snapshots"));

            Assert.Equal(0, ForeignKeyViolationCount(path));
            Assert.Equal("ok", IntegrityCheck(path));

            using (new SqliteCompanyStore(path)) { }
            Assert.Equal((long)Schema.CurrentVersion,
                ReadScalar(path, "SELECT version FROM schema_version LIMIT 1;"));
            Assert.Equal(0, ForeignKeyViolationCount(path));
        }
        finally { TempDbFile.Delete(path); }
    }

    /// <summary>
    /// 🔴 <b>ER-13 — THE MIGRATION MOVES NOTHING, AND THAT IS ASSERTED ON THE ROWS RATHER THAN ON THE ABSENCE OF
    /// AN <c>UPDATE</c> IN THE SQL.</b> Every voucher type a pre-v66 book held comes back with
    /// <c>retain_original_voucher_no = 0</c> — the vendor's own <i>Renumber Vouchers</i> default, and the only
    /// behaviour this build had before v66 — so no insertion plan changes. And the book's own name, numbering
    /// method and type count are untouched.
    /// </summary>
    [Fact]
    [Trait("Category", "RoundTrip")]
    public void A_pre_v66_book_climbs_the_rung_with_every_voucher_type_on_the_vendor_default()
    {
        var path = TempDbFile.NewPath("apex-v66-inert");
        try
        {
            var company = CompanyFactory.CreateSeeded("Inert Co", FyStart);
            using (var store = new SqliteCompanyStore(path)) store.Save(company);

            var typesBefore = ReadScalar(path, "SELECT COUNT(*) FROM voucher_types;");
            Assert.True(typesBefore > 0, "the seeded book has no voucher types, so this test proves nothing");

            using (var conn = Open(path)) { SchemaDowngrade.V66ToV65(conn); SqliteConnection.ClearPool(conn); }
            using (new SqliteCompanyStore(path)) { }   // climbs v65 → v66

            Assert.Equal(typesBefore, ReadScalar(path, "SELECT COUNT(*) FROM voucher_types;"));
            // Not one row carries the non-default — the migration back-fills nothing.
            Assert.Equal(0L, ReadScalar(path, "SELECT COUNT(*) FROM voucher_types WHERE retain_original_voucher_no <> 0;"));
            // And the attribution column is NULL on every snapshot a pre-v66 book could hold (it holds none here,
            // which is itself the point: v66 creates no row and deletes none).
            Assert.Equal(0L, ReadScalar(path, "SELECT COUNT(*) FROM gstr2b_snapshots WHERE gst_registration_id IS NOT NULL;"));

            using var reopened = new SqliteCompanyStore(path);
            var back = reopened.Load(company.Id)!;
            Assert.All(back.VoucherTypes, t => Assert.False(t.RetainOriginalVoucherNo));
        }
        finally { TempDbFile.Delete(path); }
    }

    // ================================================================= 🔴 the round trip

    /// <summary>
    /// <b>The per-type insertion behaviour round-trips through the production store.</b> A column the store never
    /// writes or never reads is dead schema, and that is exactly what a migration-parity test cannot see.
    /// </summary>
    [Fact]
    [Trait("Category", "RoundTrip")]
    public void Retain_original_voucher_no_round_trips_per_voucher_type()
    {
        var path = TempDbFile.NewPath("apex-v66-retain");
        try
        {
            var company = CompanyFactory.CreateSeeded("Retain Co", FyStart);
            var sales = company.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Sales);
            var purchase = company.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Purchase);
            sales.RetainOriginalVoucherNo = true;      // this one elects the vendor's other behaviour
            // purchase deliberately left on the default, so the round trip is proved PER TYPE rather than
            // globally — a writer that stamped every row from one type's value would pass a single-type test.

            using (var store = new SqliteCompanyStore(path)) store.Save(company);
            using var reopened = new SqliteCompanyStore(path);
            var back = reopened.Load(company.Id)!;

            Assert.True(back.FindVoucherType(sales.Id)!.RetainOriginalVoucherNo);
            Assert.False(back.FindVoucherType(purchase.Id)!.RetainOriginalVoucherNo);
            Assert.Equal(1L, ReadScalar(path, "SELECT COUNT(*) FROM voucher_types WHERE retain_original_voucher_no = 1;"));
        }
        finally { TempDbFile.Delete(path); }
    }

    /// <summary>
    /// 🔴 <b>THE GSTR-2B REGISTRATION ANCHOR ROUND-TRIPS, AND THE PRIMARY IS STORED AS <c>NULL</c>.</b>
    ///
    /// <para>The NULL is not a detail. It is the contract the column's own declaration states — <i>"the company's
    /// own first registration"</i> — it is why no book needed back-filling, and it is <b>required</b>: writing
    /// <see cref="GstRegistration.PrimaryId"/>'s all-zero Guid instead would violate the column's
    /// <c>REFERENCES gst_registrations(id)</c> clause, because the primary registration deliberately has no row
    /// in that table. This test asserts the stored cell, not only the rehydrated property.</para>
    /// </summary>
    [Fact]
    [Trait("Category", "RoundTrip")]
    public void The_gstr2b_registration_anchor_round_trips_and_the_primary_is_stored_as_NULL()
    {
        var path = TempDbFile.NewPath("apex-v66-2breg");
        try
        {
            var company = CompanyFactory.CreateSeeded("Anchor Co", FyStart);
            new GstService(company).EnableGst(new GstConfig
            {
                HomeStateCode = "27",
                Gstin = GstinFor("27", "AAPFU0939F"),
                RegistrationType = GstRegistrationType.Regular,
                ApplicableFrom = FyStart,
                Periodicity = GstReturnPeriodicity.Monthly,
            });
            var branchId = Guid.NewGuid();
            company.Gst!.AddRegistration(new GstRegistration(
                branchId, "Karnataka Registration", Karnataka, GstinFor(Karnataka, "AAACC1206D"),
                GstRegistrationType.Regular, FyStart));
            company.Gst!.EnsureValid();

            // Two snapshots: one anchored to the branch, one on the primary.
            company.AddGstr2bSnapshot(Snapshot("2024-04", GstinFor(Karnataka, "AAACC1206D"), branchId));
            company.AddGstr2bSnapshot(Snapshot("2024-05", GstinFor("27", "AAPFU0939F"), GstRegistration.PrimaryId));

            using (var store = new SqliteCompanyStore(path)) store.Save(company);

            // 🔴 THE STORED CELLS, read with SQL rather than through the mapper that wrote them.
            Assert.Equal(1L, ReadScalar(path, "SELECT COUNT(*) FROM gstr2b_snapshots WHERE gst_registration_id IS NULL;"));
            Assert.Equal(1L, ReadScalar(path, "SELECT COUNT(*) FROM gstr2b_snapshots WHERE gst_registration_id IS NOT NULL;"));
            Assert.Equal(0L, ReadScalar(
                path,
                "SELECT COUNT(*) FROM gstr2b_snapshots WHERE gst_registration_id = '00000000-0000-0000-0000-000000000000';"));

            using var reopened = new SqliteCompanyStore(path);
            var back = reopened.Load(company.Id)!;

            var branchSnap = back.Gstr2bSnapshots.Single(s => s.ReturnPeriod == "2024-04");
            var primarySnap = back.Gstr2bSnapshots.Single(s => s.ReturnPeriod == "2024-05");

            Assert.Equal(branchId, branchSnap.GstRegistrationId);
            Assert.Equal(branchId, branchSnap.OwningRegistrationId);
            // NULL round-trips AS null — nothing is defaulted to a registration at the storage boundary, so a
            // book whose column was never written stays distinguishable from one that chose the primary.
            Assert.Null(primarySnap.GstRegistrationId);
            Assert.Equal(GstRegistration.PrimaryId, primarySnap.OwningRegistrationId);

            // And the FK is real: the branch row points at a registration the company actually holds.
            Assert.Equal(0, ForeignKeyViolationCount(path));
        }
        finally { TempDbFile.Delete(path); }
    }

    // ================================================================= the published constants

    /// <summary>
    /// The published v66 object-name lists describe exactly what the migration adds and the downgrade removes, so
    /// the migration SQL, <see cref="Schema.CreateV1"/>, <c>SchemaDowngrade</c> and these tests cannot drift.
    /// </summary>
    [Fact]
    public void The_published_v66_constants_describe_what_the_migration_adds()
    {
        Assert.Equal(new[] { "retain_original_voucher_no" }, Schema.V66VoucherTypeColumns);
        Assert.Equal(new[] { "gst_registration_id" }, Schema.V66Gstr2bSnapshotColumns);

        // 🔴 Each ALTER's declaration must be byte-identical to its CreateV1 counterpart; the whole-schema
        // equivalence test compares PRAGMA table_info, and this is the cheap readable guard on the SQL itself.
        Assert.Contains("ALTER TABLE voucher_types ADD COLUMN retain_original_voucher_no", Schema.MigrateV65ToV66);
        Assert.Contains("ALTER TABLE gstr2b_snapshots ADD COLUMN gst_registration_id", Schema.MigrateV65ToV66);

        // 🔴 AND THERE IS NO UPDATE. v66 back-fills nothing — stated in the constant's own doc comment and
        // asserted here, because "the migration moves nothing" is the claim ER-13 rests on for this version.
        Assert.DoesNotContain("UPDATE ", Schema.MigrateV65ToV66);

        Assert.Equal(66, Schema.CurrentVersion);
    }

    // ================================================================= fixtures & helpers

    private static Gstr2bSnapshot Snapshot(string period, string recipientGstin, Guid registrationId) =>
        new(Guid.NewGuid(), GstStatementType.Gstr2b, period, recipientGstin, new DateOnly(2024, 5, 14),
            "f" + period.Replace("-", string.Empty), new DateTimeOffset(2024, 5, 20, 10, 0, 0, TimeSpan.Zero),
            0L, 0L, 0L, 0L, Array.Empty<Gstr2bLine>(), registrationId);

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

    /// <summary>name | type | notnull | default | pk for every column — the equivalence contract.</summary>
    private static List<string> TableInfo(string path, string table)
    {
        using var conn = Open(path);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"PRAGMA table_info(\"{table}\");";
        var rows = new List<string>();
        using (var r = cmd.ExecuteReader())
            while (r.Read())
                rows.Add(string.Join('|',
                    r.GetString(1), r.GetString(2), r.GetInt32(3),
                    r.IsDBNull(4) ? "<null>" : r.GetString(4), r.GetInt32(5)));
        SqliteConnection.ClearPool(conn);
        return rows;
    }

    private static List<string> PrimaryKeyColumns(string path, string table)
    {
        using var conn = Open(path);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"PRAGMA table_info(\"{table}\");";
        var pk = new List<string>();
        using (var r = cmd.ExecuteReader())
            while (r.Read()) if (r.GetInt32(5) > 0) pk.Add(r.GetString(1));
        SqliteConnection.ClearPool(conn);
        return pk;
    }

    private static List<string> IndexNames(string path, string table)
    {
        using var conn = Open(path);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT name FROM sqlite_master WHERE type = 'index' AND tbl_name = $t AND sql IS NOT NULL;";
        cmd.Parameters.AddWithValue("$t", table);
        var names = new List<string>();
        using (var r = cmd.ExecuteReader()) while (r.Read()) names.Add(r.GetString(0));
        SqliteConnection.ClearPool(conn);
        return names;
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
