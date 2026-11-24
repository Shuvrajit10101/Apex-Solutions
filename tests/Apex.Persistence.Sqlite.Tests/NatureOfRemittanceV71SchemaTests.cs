using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Services;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Apex.Persistence.Sqlite.Tests;

/// <summary>
/// Schema v70 → v71 (census row <b>6.28</b>, clause (i) <i>"no Nature of Remittance"</i>): ONE nullable column,
/// <c>nature_of_payment.nature_of_remittance</c>.
///
/// <para><b>Fidelity (R7, ruling 14).</b> help.tallysolutions.com/tds-faq-tally/, opened by content 2026-11-24:
/// <i>"manually enter the applicable Nature of Remittance in the Nature of Payment master."</i></para>
///
/// <para>🔴 <b>What this file proves beyond "the column exists":</b> (1) a REAL v70 book holding the seeded natures,
/// a custom nature, a ledger default and a posted TDS voucher climbs the rung with every nature row, every id and
/// every TDS line intact and no remittance invented; (2) the migrated contract equals the fresh one column for column
/// and the migration's declaration is the CreateV1 declaration byte for byte; (3) the downgrade keeps the primary
/// key, the index and every row; (4) a remittance written by the production store is read back off a FRESH store
/// handle.</para>
/// </summary>
public sealed class NatureOfRemittanceV71SchemaTests
{
    private static readonly DateOnly FyStart = new(2025, 4, 1);

    private sealed record Seeded(Company Company, Guid CustomId, Guid JId, Guid VoucherId);

    private static Seeded Seed(string name)
    {
        var c = CompanyFactory.CreateSeeded(name, FyStart);
        new TdsTcsService(c).EnableTds(new TdsConfig { Tan = "MUMA12345B" });
        var custom = new NatureOfPayment(Guid.NewGuid(), "195X", "Custom remittance", 1000, 2000, "95X",
            effectiveFrom: FyStart);
        c.Tds!.AddNatureOfPayment(custom);

        var j = c.FindNatureOfPaymentByCode("194J(b)")!;
        var vendor = new Apex.Ledger.Domain.Ledger(Guid.NewGuid(), "V71 Consultant",
            c.FindGroupByName("Sundry Creditors")!.Id, Money.Zero, false)
        {
            TdsApplicable = true, TdsNatureOfPaymentId = j.Id, DeducteeType = DeducteeType.Firm,
            PartyPan = "AAPFU0939F", DeductTdsInSameVoucher = true,
        };
        c.AddLedger(vendor);
        var fees = new Apex.Ledger.Domain.Ledger(Guid.NewGuid(), "V71 Fees",
            c.FindGroupByName("Indirect Expenses")!.Id, Money.Zero, true);
        c.AddLedger(fees);
        var date = new DateOnly(2025, 5, 10);
        var carve = new TdsService(c).BuildCarveOut(Money.FromRupees(1_00_000m), Money.FromRupees(1_00_000m), j, vendor, date);
        var voucher = new LedgerService(c).Post(new Voucher(Guid.NewGuid(),
            c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Journal).Id, date,
            new[] { new EntryLine(fees.Id, Money.FromRupees(1_00_000m), DrCr.Debit), carve.PartyLine, carve.TdsPayableLine! }));
        return new Seeded(c, custom.Id, j.Id, voucher.Id);
    }

    /// <summary>
    /// 🔴 <b>THE MIGRATION TEST ON A REAL v70 BOOK.</b> Walked down to v70 (proved: marker 70, column absent), then
    /// re-opened by the production store: it must climb to v71 with every nature row in its rowid order, every id,
    /// the ledger default and the posted TDS line intact, and NO remittance back-filled.
    /// <para><b>Mutation:</b> deleting the <c>if (version == 70)</c> rung in <c>SqliteCompanyStore</c> reddens this
    /// (the store throws "schema version 70 is not supported").</para>
    /// </summary>
    [Fact]
    [Trait("Category", "RoundTrip")]
    public void A_real_v70_book_climbs_to_v71_with_every_nature_and_tds_line_intact_and_no_remittance_invented()
    {
        var path = TempDbFile.NewPath("apex-v71-real");
        try
        {
            var s = Seed("Legacy Remittance Co");
            using (var store = new SqliteCompanyStore(path)) store.Save(s.Company);
            var rowsBefore = Rows(path,
                "SELECT id || '|' || section_code || '|' || rate_with_pan_bp || '|' || fvu_code FROM nature_of_payment ORDER BY rowid;");
            Assert.Equal(s.Company.NaturesOfPayment.Count, rowsBefore.Count);
            var tdsLinesBefore = ReadScalar(path, "SELECT COUNT(*) FROM tds_lines;");
            Assert.True(tdsLinesBefore >= 1);

            using (var conn = Open(path)) { SchemaDowngrade.V71ToV70(conn); SqliteConnection.ClearPool(conn); }
            Assert.Equal(70L, ReadScalar(path, "SELECT version FROM schema_version LIMIT 1;"));
            Assert.DoesNotContain("nature_of_remittance", ColumnNames(path, "nature_of_payment"));

            Company reloaded;
            using (var store = new SqliteCompanyStore(path)) reloaded = store.Load(s.Company.Id)!;

            Assert.Equal((long)Schema.CurrentVersion, ReadScalar(path, "SELECT version FROM schema_version LIMIT 1;"));
            Assert.Contains("nature_of_remittance", ColumnNames(path, "nature_of_payment"));
            Assert.Equal(rowsBefore, Rows(path,
                "SELECT id || '|' || section_code || '|' || rate_with_pan_bp || '|' || fvu_code FROM nature_of_payment ORDER BY rowid;"));
            Assert.Equal(0L, ReadScalar(path, "SELECT COUNT(*) FROM nature_of_payment WHERE nature_of_remittance IS NOT NULL;"));
            Assert.Equal(tdsLinesBefore, ReadScalar(path, "SELECT COUNT(*) FROM tds_lines;"));

            Assert.Equal(s.Company.NaturesOfPayment.Select(n => n.Id), reloaded.NaturesOfPayment.Select(n => n.Id));
            Assert.All(reloaded.NaturesOfPayment, n => Assert.Null(n.NatureOfRemittance));
            Assert.NotNull(reloaded.FindNatureOfPayment(s.CustomId));
            Assert.Equal(s.JId, reloaded.FindLedgerByName("V71 Consultant")!.TdsNatureOfPaymentId);
            Assert.Contains(reloaded.FindVoucher(s.VoucherId)!.Lines, l => l.Tds is { } t && t.NatureId == s.JId);
        }
        finally { TempDbFile.Delete(path); }
    }

    /// <summary>The migrated <c>nature_of_payment</c> contract equals the fresh one, column for column, the index is
    /// the same, and the migration's column declaration is the CreateV1 declaration byte for byte.</summary>
    [Fact]
    [Trait("Category", "RoundTrip")]
    public void Migration_v70_to_v71_matches_CreateV1_on_nature_of_payment()
    {
        var migratedPath = TempDbFile.NewPath("apex-v71-migrated");
        var freshPath = TempDbFile.NewPath("apex-v71-fresh");
        try
        {
            using (var store = new SqliteCompanyStore(freshPath)) store.Save(Seed("Fresh Remittance Co").Company);
            using (var store = new SqliteCompanyStore(migratedPath)) store.Save(Seed("Migrated Remittance Co").Company);

            using (var conn = Open(migratedPath)) { SchemaDowngrade.V71ToV70(conn); SqliteConnection.ClearPool(conn); }
            Assert.DoesNotContain("nature_of_remittance", ColumnNames(migratedPath, "nature_of_payment"));

            using (new SqliteCompanyStore(migratedPath)) { }
            Assert.Equal(TableInfo(freshPath, "nature_of_payment"), TableInfo(migratedPath, "nature_of_payment"));
            const string indexSql =
                "SELECT sql FROM sqlite_master WHERE type = 'index' AND name = 'ix_nature_of_payment_company';";
            Assert.Equal(Rows(freshPath, indexSql), Rows(migratedPath, indexSql));

            // The declaration the rung adds is, byte for byte, the one CreateV1 carries.
            const string declaration = "nature_of_remittance       TEXT        NULL";
            Assert.Contains("ADD COLUMN " + declaration + ";", Schema.MigrateV70ToV71);
            Assert.Contains(declaration + "\n", Schema.CreateV1.Replace("\r\n", "\n"));
            Assert.Equal(new[] { "nature_of_remittance" }, Schema.V71NatureOfPaymentColumns);
            Assert.DoesNotContain("UPDATE ", Schema.MigrateV70ToV71);
            Assert.Equal(71, Schema.CurrentVersion);
        }
        finally
        {
            TempDbFile.Delete(migratedPath);
            TempDbFile.Delete(freshPath);
        }
    }

    /// <summary>The downgrade keeps the PRIMARY KEY, the index and every row; the file climbs back and saves again.</summary>
    [Fact]
    [Trait("Category", "RoundTrip")]
    public void The_v71_downgrade_preserves_the_primary_key_index_and_every_row()
    {
        var path = TempDbFile.NewPath("apex-v71-down");
        try
        {
            var s = Seed("Down Remittance Co");
            var j = s.Company.FindNatureOfPayment(s.JId)!;
            s.Company.Tds!.ReplaceNatureOfPayment(j.WithTerms(j.SectionCode, j.Name, j.RateWithPanBp,
                j.RateWithoutPanBp, j.FvuSectionCode, j.SingleTransactionThreshold, j.CumulativeThreshold, "27"));
            using (var store = new SqliteCompanyStore(path)) store.Save(s.Company);
            var count = ReadScalar(path, "SELECT COUNT(*) FROM nature_of_payment;");

            using (var conn = Open(path)) { SchemaDowngrade.V71ToV70(conn); SqliteConnection.ClearPool(conn); }

            Assert.Equal(new List<string> { "id" }, PrimaryKeyColumns(path, "nature_of_payment"));
            Assert.Single(Rows(path,
                "SELECT name FROM sqlite_master WHERE type = 'index' AND name = 'ix_nature_of_payment_company';"));
            Assert.Equal(count, ReadScalar(path, "SELECT COUNT(*) FROM nature_of_payment;"));
            Assert.Equal(0, ForeignKeyViolationCount(path));

            Company reloaded;
            using (var store = new SqliteCompanyStore(path)) reloaded = store.Load(s.Company.Id)!;
            // What a downgrade means: the remittance is lost, the nature is not.
            Assert.Null(reloaded.FindNatureOfPayment(s.JId)!.NatureOfRemittance);
            using (var store = new SqliteCompanyStore(path)) store.Save(reloaded);
            Assert.Equal(0, ForeignKeyViolationCount(path));
        }
        finally { TempDbFile.Delete(path); }
    }

    /// <summary>
    /// 🔴 <b>THE WRITER AND THE READER ARE BOTH LIVE.</b> A remittance set on the predefined 194J(b) and on a custom
    /// nature, saved by the production store, is in the raw column and is read back off a FRESH store handle; clearing
    /// it writes NULL.
    /// <para><b>Mutations:</b> binding <c>$nor</c> to <c>DBNull.Value</c> in the writer reddens the raw-SQL
    /// assertion; dropping <c>natureOfRemittance: …</c> in <c>ReadNaturesOfPayment</c> reddens the reloaded
    /// assertion.</para>
    /// </summary>
    [Fact]
    [Trait("Category", "RoundTrip")]
    public void A_remittance_is_written_and_read_back_off_a_fresh_store_handle()
    {
        var path = TempDbFile.NewPath("apex-v71-roundtrip");
        try
        {
            var s = Seed("Round Trip Remittance Co");
            var tds = s.Company.Tds!;
            var j = s.Company.FindNatureOfPayment(s.JId)!;
            tds.ReplaceNatureOfPayment(j.WithTerms(j.SectionCode, j.Name, j.RateWithPanBp, j.RateWithoutPanBp,
                j.FvuSectionCode, j.SingleTransactionThreshold, j.CumulativeThreshold, "27"));
            var custom = s.Company.FindNatureOfPayment(s.CustomId)!;
            tds.ReplaceNatureOfPayment(custom.WithTerms(custom.SectionCode, custom.Name, custom.RateWithPanBp,
                custom.RateWithoutPanBp, custom.FvuSectionCode, null, null, "16"));
            using (var store = new SqliteCompanyStore(path)) store.Save(s.Company);

            Assert.Equal("27", ReadText(path, $"SELECT nature_of_remittance FROM nature_of_payment WHERE id = '{s.JId:D}';"));
            Assert.Equal("16", ReadText(path, $"SELECT nature_of_remittance FROM nature_of_payment WHERE id = '{s.CustomId:D}';"));
            Assert.Equal(2L, ReadScalar(path, "SELECT COUNT(*) FROM nature_of_payment WHERE nature_of_remittance IS NOT NULL;"));

            Company reloaded;
            using (var store = new SqliteCompanyStore(path)) reloaded = store.Load(s.Company.Id)!;
            Assert.Equal("27", reloaded.FindNatureOfPayment(s.JId)!.NatureOfRemittance);
            Assert.Equal("16", reloaded.FindNatureOfPayment(s.CustomId)!.NatureOfRemittance);

            var rj = reloaded.FindNatureOfPayment(s.JId)!;
            reloaded.Tds!.ReplaceNatureOfPayment(rj.WithTerms(rj.SectionCode, rj.Name, rj.RateWithPanBp,
                rj.RateWithoutPanBp, rj.FvuSectionCode, rj.SingleTransactionThreshold, rj.CumulativeThreshold, null));
            using (var store = new SqliteCompanyStore(path)) store.Save(reloaded);
            Assert.Equal(1L, ReadScalar(path, "SELECT COUNT(*) FROM nature_of_payment WHERE nature_of_remittance IS NOT NULL;"));
            Company again;
            using (var store = new SqliteCompanyStore(path)) again = store.Load(s.Company.Id)!;
            Assert.Null(again.FindNatureOfPayment(s.JId)!.NatureOfRemittance);
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

    private static string? ReadText(string path, string sql)
    {
        using var conn = Open(path);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        var value = cmd.ExecuteScalar();
        SqliteConnection.ClearPool(conn);
        return value is null or DBNull ? null : Convert.ToString(value);
    }

    private static List<string> Rows(string path, string sql)
    {
        using var conn = Open(path);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        var rows = new List<string>();
        using (var r = cmd.ExecuteReader()) while (r.Read()) rows.Add(r.IsDBNull(0) ? "<null>" : r.GetString(0));
        SqliteConnection.ClearPool(conn);
        return rows;
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
}
