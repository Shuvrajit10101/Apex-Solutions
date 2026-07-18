using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Services;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Apex.Persistence.Sqlite.Tests;

/// <summary>
/// Phase-9 slice-7a schema + persistence contract (v43→v44; ER-7/ER-2/ER-13). The bump is additive: four new tables
/// (<c>gst_setoff_lines</c> with the CGST↔SGST cross-head + cess CHECKs, <c>itc_reversals</c> with the UNIQUE
/// idempotency key, <c>gst_challans</c>, <c>gst_drc03</c>) + one column each on <c>entry_lines</c> and
/// <c>voucher_types</c>. Covers: a fresh DB stamps to <see cref="Schema.CurrentVersion"/> with the new tables/columns;
/// a legacy v43 DB auto-migrates forward (new tables empty, new columns default — ER-13); the DB-layer CHECK rejects a
/// cross-head / cess-cross set-off row; the UNIQUE idempotency key rejects a duplicate reversal; and a full save/load
/// round-trips a posted set-off (incl. the <c>gst_adjustment_kind</c> tag + the <c>is_gst_stat_adjustment</c> flag) +
/// a PMT-06 challan + a DRC-03.
/// </summary>
public sealed class GstSetOffSchemaTests
{
    private const string Gstin = "27AAPFU0939F1ZV";
    private static readonly DateOnly FyStart = new(2024, 4, 1);
    private static readonly DateOnly D = new(2024, 4, 20);

    [Fact]
    [Trait("Category", "RoundTrip")]
    public void Fresh_database_stamps_to_v44_with_new_tables_and_columns()
    {
        var dbPath = TempDbFile.NewPath("apex-s7a-fresh");
        try
        {
            using (new SqliteCompanyStore(dbPath)) { }
            // A fresh DB stamps straight to the current version; slice 7a's artefacts must be in that shape. (The
            // constant has since moved past 44 — v45 added the WI-4 party mailing columns — so this pins the
            // slice-7a CONTENT, and the stamped version against the constant, not the literal 44.)
            Assert.True(Schema.CurrentVersion >= 44);
            Assert.Equal((long)Schema.CurrentVersion, ReadSchemaVersion(dbPath));
            foreach (var t in new[] { "gst_setoff_lines", "itc_reversals", "gst_challans", "gst_drc03" })
                Assert.True(TableExists(dbPath, t), $"missing table {t}");
            Assert.Contains("gst_adjustment_kind", ColumnNames(dbPath, "entry_lines"));
            Assert.Contains("is_gst_stat_adjustment", ColumnNames(dbPath, "voucher_types"));
        }
        finally { TempDbFile.Delete(dbPath); }
    }

    [Fact]
    [Trait("Category", "RoundTrip")]
    public void Legacy_v43_database_auto_migrates_to_v44_with_empty_new_tables()
    {
        var dbPath = TempDbFile.NewPath("apex-s7a-v43legacy");
        try
        {
            var connStr = new SqliteConnectionStringBuilder { DataSource = dbPath, Mode = SqliteOpenMode.ReadWriteCreate }.ToString();
            using (var conn = new SqliteConnection(connStr))
            {
                conn.Open();
                Exec(conn, MinimalV43Ddl);
                Exec(conn, "INSERT INTO schema_version(version) VALUES (43);");
                Exec(conn, "INSERT INTO entry_lines(id) VALUES (7);");
                SqliteConnection.ClearPool(conn);
            }
            Assert.Equal(43L, ReadSchemaVersion(dbPath));

            using (new SqliteCompanyStore(dbPath)) { }   // v43 → v44 → … → CurrentVersion

            Assert.Equal((long)Schema.CurrentVersion, ReadSchemaVersion(dbPath));
            foreach (var t in new[] { "gst_setoff_lines", "itc_reversals", "gst_challans", "gst_drc03" })
            {
                Assert.True(TableExists(dbPath, t));
                Assert.Equal(0L, ReadScalar(dbPath, $"SELECT COUNT(*) FROM {t};")); // new tables empty
            }
            Assert.Equal(7L, ReadScalar(dbPath, "SELECT id FROM entry_lines;")); // existing row preserved
            Assert.Contains("gst_adjustment_kind", ColumnNames(dbPath, "entry_lines"));
        }
        finally { TempDbFile.Delete(dbPath); }
    }

    [Fact]
    [Trait("Category", "RoundTrip")]
    public void The_check_constraint_rejects_a_cross_head_and_a_cess_cross_setoff_row_at_the_db_layer()
    {
        var dbPath = TempDbFile.NewPath("apex-s7a-check");
        try
        {
            using (new SqliteCompanyStore(dbPath)) { }
            using var conn = Open(dbPath);
            Exec(conn, "PRAGMA foreign_keys=OFF;"); // isolate the CHECK from FK enforcement

            // A legal same-head row is accepted (CGST credit → CGST liability).
            Exec(conn, "INSERT INTO gst_setoff_lines(id, company_id, voucher_id, period, credit_head, liability_head, is_cash, amount_paisa) " +
                       "VALUES ('a','c','v','2024-04', 0, 0, 0, 100);");

            // CGST(0) credit → SGST(1) liability is impossible to persist (ER-7).
            Assert.Throws<SqliteException>(() => Exec(conn,
                "INSERT INTO gst_setoff_lines(id, company_id, voucher_id, period, credit_head, liability_head, is_cash, amount_paisa) " +
                "VALUES ('b','c','v','2024-04', 0, 1, 0, 100);"));
            // SGST(1) credit → CGST(0) liability is impossible to persist (ER-7).
            Assert.Throws<SqliteException>(() => Exec(conn,
                "INSERT INTO gst_setoff_lines(id, company_id, voucher_id, period, credit_head, liability_head, is_cash, amount_paisa) " +
                "VALUES ('d','c','v','2024-04', 1, 0, 0, 100);"));
            // Cess(3) credit → CGST(0) liability (cess ring-fence, ER-2) is impossible to persist.
            Assert.Throws<SqliteException>(() => Exec(conn,
                "INSERT INTO gst_setoff_lines(id, company_id, voucher_id, period, credit_head, liability_head, is_cash, amount_paisa) " +
                "VALUES ('e','c','v','2024-04', 3, 0, 0, 100);"));

            SqliteConnection.ClearPool(conn);
        }
        finally { TempDbFile.Delete(dbPath); }
    }

    [Fact]
    [Trait("Category", "RoundTrip")]
    public void The_unique_idempotency_key_rejects_a_duplicate_reversal_row()
    {
        var dbPath = TempDbFile.NewPath("apex-s7a-unique");
        try
        {
            using (new SqliteCompanyStore(dbPath)) { }
            using var conn = Open(dbPath);
            Exec(conn, "PRAGMA foreign_keys=OFF;");

            const string ins =
                "INSERT INTO itc_reversals(id, company_id, rule, period, reversal_voucher_id, source_voucher_id, source_line_id, created_at) " +
                "VALUES ($id,'c', 0, '2024-04', 'rv', 'sv', 'sl', '2024-04-20');";
            using (var cmd = conn.CreateCommand()) { cmd.CommandText = ins.Replace("$id", "'r1'"); cmd.ExecuteNonQuery(); }
            // Same (company, rule, period, source_voucher_id, source_line_id) ⇒ a double-post is a DB error (§5.3).
            Assert.Throws<SqliteException>(() =>
            {
                using var cmd = conn.CreateCommand();
                cmd.CommandText = ins.Replace("$id", "'r2'");
                cmd.ExecuteNonQuery();
            });
            SqliteConnection.ClearPool(conn);
        }
        finally { TempDbFile.Delete(dbPath); }
    }

    [Fact]
    [Trait("Category", "RoundTrip")]
    public void A_posted_setoff_plus_challan_plus_drc03_round_trips_through_the_store()
    {
        var dbPath = TempDbFile.NewPath("apex-s7a-roundtrip");
        try
        {
            var c = CompanyFactory.CreateSeeded("S7a Co", FyStart);
            var gst = new GstService(c);
            gst.EnableGst(new GstConfig
            {
                HomeStateCode = "27", Gstin = Gstin, RegistrationType = GstRegistrationType.Regular,
                ApplicableFrom = FyStart, Periodicity = GstReturnPeriodicity.Monthly,
            });
            var bank = new Apex.Ledger.Domain.Ledger(Guid.NewGuid(), "Bank", c.FindGroupByName("Bank Accounts")!.Id, Money.Zero, true);
            c.AddLedger(bank);

            // A posted Rule-88A set-off (Alt+J Journal + Table-6.1 rows + the gst_adjustment_kind tag on each leg).
            var alloc = GstSetOffService.Allocate(new GstSetOffService.SetOffDemand(90000, 90000, 0, 0, 0, 90000, 90000, 0, 0));
            var setoff = new GstSetOffService(c).PostSetOff("2024-04", alloc, D);
            Assert.NotNull(setoff);

            // A PMT-06 deposit + a DRC-03.
            var deposit = new GstDepositService(c);
            deposit.PostPmt06(GstTaxHead.Central, GstMinorHead.Tax, Money.FromRupees(500m), bank, D, "24CPIN00000009999", "CIN9", "BRN9");
            deposit.PostDrc03("voluntary", "2024-04", D, 1000, 1000, 0, 0, 200, GstDepositService.PaymentMethod.Bank, bank,
                drc03Ref: "ARN-1", drc03aDemandRef: "AD-1");

            using (var store = new SqliteCompanyStore(dbPath)) store.Save(c);
            Company r;
            using (var store = new SqliteCompanyStore(dbPath)) r = store.Load(c.Id)!;

            // Set-off allocation rows + the Alt+J stat-adjustment voucher-type flag survived.
            Assert.Equal(2, r.GstSetoffLines.Count);
            Assert.All(r.GstSetoffLines, l => Assert.Equal(l.CreditHead, l.LiabilityHead)); // own-head only here
            Assert.Contains(r.VoucherTypes, t => t.IsGstStatAdjustmentType);
            // The gst_adjustment_kind tag round-tripped on the posted set-off Journal.
            var setoffVoucher = r.FindVoucher(setoff!.Id)!;
            Assert.All(setoffVoucher.Lines, l => Assert.Equal(GstAdjustmentKind.SetOff, l.Gst!.Adjustment));

            // Challan survived (CPIN/CIN, major/minor head, paisa-exact amount, voucher link).
            var challan = Assert.Single(r.GstChallans);
            Assert.Equal("24CPIN00000009999", challan.Cpin);
            Assert.Equal("CIN9", challan.Cin);
            Assert.Equal(GstTaxHead.Central, challan.MajorHead);
            Assert.Equal(GstMinorHead.Tax, challan.MinorHead);
            Assert.Equal(Money.FromRupees(500m), challan.Amount);

            // DRC-03 survived (cause, per-head + flag-only interest).
            var drc = Assert.Single(r.GstDrc03s);
            Assert.Equal("voluntary", drc.Cause);
            Assert.Equal(2000, drc.TotalTaxPaisa);
            Assert.Equal(200, drc.InterestPaisa);
            Assert.Equal("AD-1", drc.Drc03aDemandRef);

            Assert.Empty(r.ItcReversals); // this fixture posts no reversal — the table stays empty for it
        }
        finally { TempDbFile.Delete(dbPath); }
    }

    [Fact]
    [Trait("Category", "RoundTrip")]
    public void Posted_itc_reversals_reclaim_and_credit_note_round_trip_through_the_store()
    {
        // Phase-9 slice-7b: the reversal engine now writes real itc_reversals rows — a Rule-37 reversal (4B2), its
        // reclaim (4D1, reclaim_of_id → the reversal, a self-FK), and a credit-note reversal (4B1, the new CreditNote
        // rule ordinal). All three must save + load verbatim (rule, bucket, per-head paisa, reclaim link).
        var dbPath = TempDbFile.NewPath("apex-s7b-reversal-roundtrip");
        try
        {
            var c = CompanyFactory.CreateSeeded("S7b Co", FyStart);
            var gst = new GstService(c);
            gst.EnableGst(new GstConfig
            {
                HomeStateCode = "27", Gstin = Gstin, RegistrationType = GstRegistrationType.Regular,
                ApplicableFrom = FyStart, Periodicity = GstReturnPeriodicity.Monthly,
            });
            var bank = new Apex.Ledger.Domain.Ledger(Guid.NewGuid(), "Bank", c.FindGroupByName("Bank Accounts")!.Id, Money.Zero, true);
            c.AddLedger(bank);
            var vid = AccrueInterPurchaseItc(c, gst, bank, 100000m); // Input IGST 18000

            var svc = new GstReversalService(c);
            var reversal = svc.PostRule37(vid, "2024-04", D);
            var reclaim = svc.Reclaim(reversal.Id, "2024-09", D);
            var cn = svc.PostReversal(ItcReversalRule.CreditNote, "2024-10",
                new GstReversalService.ReversalAmount(0, 0, 30_000, 0), D, sourceVoucherId: vid)!;

            using (var store = new SqliteCompanyStore(dbPath)) store.Save(c);
            Company r;
            using (var store = new SqliteCompanyStore(dbPath)) r = store.Load(c.Id)!;

            Assert.Equal(3, r.ItcReversals.Count);

            var rev = Assert.Single(r.ItcReversals, x => x.Rule == ItcReversalRule.Rule37 && x.ReclaimOfId is null);
            Assert.Equal(Table4bBucket.Table4B2, rev.Table4bBucket);
            Assert.Equal(1_800_000, rev.IgstPaisa);
            Assert.Equal(vid, rev.SourceVoucherId);

            var rec = Assert.Single(r.ItcReversals, x => x.ReclaimOfId is not null);
            Assert.Equal(reversal.Id, rec.ReclaimOfId);               // the self-FK reclaim link survived
            Assert.Equal(Table4bBucket.Table4D1, rec.Table4bBucket);
            Assert.Equal(1_800_000, rec.IgstPaisa);

            var note = Assert.Single(r.ItcReversals, x => x.Rule == ItcReversalRule.CreditNote);
            Assert.Equal(Table4bBucket.Table4B1, note.Table4bBucket);  // the new CreditNote rule ordinal round-tripped
            Assert.Equal(30_000, note.IgstPaisa);
            Assert.Equal(cn.Id, note.Id);
        }
        finally { TempDbFile.Delete(dbPath); }
    }

    /// <summary>Accrues Input IGST credit by posting a bank-funded inter-state purchase of <paramref name="taxable"/> @18%;
    /// returns the posted voucher id (its ITC lands in Input IGST).</summary>
    private static Guid AccrueInterPurchaseItc(Company c, GstService gst, Apex.Ledger.Domain.Ledger bank, decimal taxable)
    {
        var purchases = new Apex.Ledger.Domain.Ledger(
            Guid.NewGuid(), "Purchases", c.FindGroupByName("Purchase Accounts")!.Id, Money.Zero, true);
        c.AddLedger(purchases);
        var tax = gst.ComputeInvoiceTax(
            new[] { new GstService.TaxableLine(new Money(taxable), 1800) }, interState: true, GstTaxDirection.Input);
        var lines = new List<EntryLine>
        {
            new(purchases.Id, new Money(taxable), DrCr.Debit),
            new(bank.Id, new Money(taxable + tax.TotalTax.Amount), DrCr.Credit),
        };
        lines.AddRange(tax.TaxLines);
        var type = c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Purchase).Id;
        return new LedgerService(c).Post(new Voucher(Guid.NewGuid(), type, D, lines)).Id;
    }

    // ---- helpers ----

    /// <summary>A minimal v43 DDL: the v43→v44 migration ALTERs <c>entry_lines</c> + <c>voucher_types</c> and CREATEs the
    /// four new tables (their FKs are forward references, satisfied lazily), so these five stubs suffice.</summary>
    /// <summary>A deliberately minimal stand-in for a v43 database: just enough tables for the migrations from v43
    /// onward to have something to ALTER. <c>ledgers</c> is present because the v44→v45 (WI-4 party Mailing Details)
    /// migration adds columns to it — every real v43 database has one.</summary>
    private const string MinimalV43Ddl = """
        CREATE TABLE schema_version (version INTEGER NOT NULL);
        CREATE TABLE companies (id TEXT NOT NULL PRIMARY KEY, name TEXT NOT NULL);
        CREATE TABLE vouchers (id TEXT NOT NULL PRIMARY KEY, company_id TEXT NULL);
        CREATE TABLE entry_lines (id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT);
        CREATE TABLE voucher_types (id TEXT NOT NULL PRIMARY KEY, name TEXT NOT NULL);
        CREATE TABLE ledgers (id TEXT NOT NULL PRIMARY KEY, name TEXT NOT NULL);
        """;

    private static long ReadSchemaVersion(string dbPath) => ReadScalar(dbPath, "SELECT version FROM schema_version LIMIT 1;");

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
        cmd.CommandText = $"PRAGMA table_info({table});";
        var names = new List<string>();
        using (var r = cmd.ExecuteReader())
            while (r.Read()) names.Add(r.GetString(1));
        SqliteConnection.ClearPool(conn);
        return names;
    }

    private static bool TableExists(string dbPath, string table)
    {
        using var conn = Open(dbPath);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name=$t;";
        cmd.Parameters.AddWithValue("$t", table);
        var exists = Convert.ToInt64(cmd.ExecuteScalar()) > 0;
        SqliteConnection.ClearPool(conn);
        return exists;
    }

    private static SqliteConnection Open(string dbPath)
    {
        var conn = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = dbPath, Mode = SqliteOpenMode.ReadWrite }.ToString());
        conn.Open();
        return conn;
    }

    private static void Exec(SqliteConnection conn, string sql)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }
}
