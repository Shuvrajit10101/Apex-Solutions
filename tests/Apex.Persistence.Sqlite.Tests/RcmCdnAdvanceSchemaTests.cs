using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Services;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Apex.Persistence.Sqlite.Tests;

/// <summary>
/// Phase-9 slice-2 <b>RCM core + §34-CDN/advances seam</b> schema contract (v38→v39; ER-13). The bump is additive: four
/// new tables (<c>rcm_categories</c>, <c>rcm_documents</c>, <c>gst_cdn_links</c>, <c>gst_advance_receipts</c>) + their
/// indexes, and the reverse-charge ALTER-added columns on <c>stock_items</c>/<c>ledgers</c>/<c>entry_lines</c>/
/// <c>voucher_types</c>. Covers: a fresh DB stamps to <see cref="Schema.CurrentVersion"/> with the new tables/columns;
/// a legacy v38 DB auto-migrates forward preserving every row (new tables empty, new columns default 0/NULL) and
/// re-opening is idempotent; and a save/load round-trips a full RCM company (categories, RCM Output ledger
/// classification, RCM-tagged lines, a self-invoice document, a CDN link + advance receipt) paisa-exact.
/// </summary>
public sealed class RcmCdnAdvanceSchemaTests
{
    private const string GstinMaharashtra = "27AAPFU0939F1ZV";
    private const string GstinGujarat = "24AAACC1206D1ZM";
    private static readonly DateOnly FyStart = new(2025, 4, 1);
    private static readonly DateOnly D1 = new(2025, 4, 10);

    [Fact]
    [Trait("Category", "RoundTrip")]
    public void Fresh_database_stamps_to_current_version_with_new_tables_and_columns()
    {
        var dbPath = TempDb("apex-rcm-fresh");
        try
        {
            using (new SqliteCompanyStore(dbPath)) { }

            Assert.True(Schema.CurrentVersion >= 39);
            Assert.Equal((long)Schema.CurrentVersion, ReadSchemaVersion(dbPath));

            foreach (var t in new[] { "rcm_categories", "rcm_documents", "gst_cdn_links", "gst_advance_receipts" })
                Assert.True(TableExists(dbPath, t), $"table {t} missing");

            foreach (var col in new[] { "reverse_charge_applicable", "gta_forward_charge", "rcm_category_id" })
                Assert.Contains(col, ColumnNames(dbPath, "stock_items"));
            foreach (var col in new[] { "sp_reverse_charge_applicable", "sp_gta_forward_charge", "sp_rcm_category_id",
                                        "party_is_promoter", "party_is_body_corporate", "gst_class_reverse_charge" })
                Assert.Contains(col, ColumnNames(dbPath, "ledgers"));
            foreach (var col in new[] { "gst_is_reverse_charge", "gst_rcm_scheme" })
                Assert.Contains(col, ColumnNames(dbPath, "entry_lines"));
            Assert.Contains("is_rcm_payment_voucher", ColumnNames(dbPath, "voucher_types"));
        }
        finally { Delete(dbPath); }
    }

    [Fact]
    [Trait("Category", "RoundTrip")]
    public void Legacy_v38_database_auto_migrates_to_v39_preserving_every_row()
    {
        var dbPath = TempDb("apex-rcm-v38legacy");
        try
        {
            var company = Guid.NewGuid();
            var connStr = new SqliteConnectionStringBuilder { DataSource = dbPath, Mode = SqliteOpenMode.ReadWriteCreate }.ToString();
            using (var conn = new SqliteConnection(connStr))
            {
                conn.Open();
                Exec(conn, MinimalV38Ddl);
                Exec(conn, "INSERT INTO schema_version(version) VALUES (38);");
                Exec(conn, "INSERT INTO companies(id, name) VALUES ($id, 'Legacy V38 Co');", ("$id", company.ToString("D")));
                Exec(conn, "INSERT INTO stock_items(id, name) VALUES ('item-1', 'Widget');");
                Exec(conn, "INSERT INTO ledgers(id, name) VALUES ('led-1', 'Sales');");
                Exec(conn, "INSERT INTO voucher_types(id, name) VALUES ('vt-1', 'Purchase');");
                SqliteConnection.ClearPool(conn);
            }

            Assert.Equal(38L, ReadSchemaVersion(dbPath));
            Assert.DoesNotContain("reverse_charge_applicable", ColumnNames(dbPath, "stock_items"));

            using (new SqliteCompanyStore(dbPath)) { } // opens v38 → migrates to current

            Assert.Equal((long)Schema.CurrentVersion, ReadSchemaVersion(dbPath));
            Assert.True(TableExists(dbPath, "rcm_categories"));
            Assert.True(TableExists(dbPath, "rcm_documents"));
            Assert.True(TableExists(dbPath, "gst_cdn_links"));
            Assert.True(TableExists(dbPath, "gst_advance_receipts"));
            Assert.Contains("reverse_charge_applicable", ColumnNames(dbPath, "stock_items"));
            Assert.Contains("sp_reverse_charge_applicable", ColumnNames(dbPath, "ledgers"));
            Assert.Contains("gst_is_reverse_charge", ColumnNames(dbPath, "entry_lines"));
            Assert.Contains("is_rcm_payment_voucher", ColumnNames(dbPath, "voucher_types"));

            // Every existing row survived; the new tables are empty and the new columns default 0/NULL (ER-13).
            Assert.Equal("Legacy V38 Co", ReadScalarStr(dbPath, "SELECT name FROM companies LIMIT 1;"));
            Assert.Equal("Widget", ReadScalarStr(dbPath, "SELECT name FROM stock_items LIMIT 1;"));
            Assert.Equal(0L, ReadScalar(dbPath, "SELECT COUNT(*) FROM rcm_categories;"));
            Assert.Equal(0L, ReadScalar(dbPath, "SELECT COUNT(*) FROM rcm_documents;"));
            Assert.Equal(0L, ReadScalar(dbPath, "SELECT COUNT(*) FROM gst_cdn_links;"));
            Assert.Equal(0L, ReadScalar(dbPath, "SELECT COUNT(*) FROM gst_advance_receipts;"));
            Assert.Equal(0L, ReadScalar(dbPath, "SELECT reverse_charge_applicable FROM stock_items LIMIT 1;"));
            Assert.Equal(0L, ReadScalar(dbPath, "SELECT gst_class_reverse_charge FROM ledgers LIMIT 1;"));
            Assert.Equal(0L, ReadScalar(dbPath, "SELECT is_rcm_payment_voucher FROM voucher_types LIMIT 1;"));

            // Re-opening is idempotent.
            using (new SqliteCompanyStore(dbPath)) { }
            Assert.Equal((long)Schema.CurrentVersion, ReadSchemaVersion(dbPath));
        }
        finally { Delete(dbPath); }
    }

    [Fact]
    [Trait("Category", "RoundTrip")]
    public void Save_load_round_trips_an_rcm_cdn_advance_company()
    {
        var dbPath = TempDb("apex-rcm-roundtrip");
        try
        {
            var c = CompanyFactory.CreateSeeded("RCM Co", FyStart);
            var gst = new GstService(c);
            gst.EnableGst(new GstConfig
            {
                HomeStateCode = "27", Gstin = GstinMaharashtra, RegistrationType = GstRegistrationType.Regular,
                ApplicableFrom = FyStart, Periodicity = GstReturnPeriodicity.Monthly,
            });
            gst.SeedAdvancedGst();
            var rcm = new RcmService(c);

            var legal = Cat(c, "Legal");
            var expense = new Apex.Ledger.Domain.Ledger(Guid.NewGuid(), "Legal Fees", c.FindGroupByName("Indirect Expenses")!.Id, Money.Zero, true)
            {
                SalesPurchaseGst = new StockItemGstDetails
                {
                    Taxability = GstTaxability.Taxable, RateBasisPoints = 1800, SupplyType = GstSupplyType.Services,
                    ReverseChargeApplicable = true, RcmCategoryId = legal.Id,
                },
            };
            c.AddLedger(expense);
            var party = new Apex.Ledger.Domain.Ledger(Guid.NewGuid(), "Advocate (Gujarat)", c.FindGroupByName("Sundry Creditors")!.Id, Money.Zero, false)
            {
                PartyGst = new PartyGstDetails { RegistrationType = GstRegistrationType.Regular, Gstin = GstinGujarat, StateCode = "24", IsBodyCorporate = true },
            };
            c.AddLedger(party);

            var posting = rcm.BuildReverseCharge(Money.FromRupees(10000m), null, expense, party.PartyGst, D1, RcmService.SupplyKind.Domestic);
            var lines = new List<EntryLine> { new(expense.Id, Money.FromRupees(10000m), DrCr.Debit), new(party.Id, Money.FromRupees(10000m), DrCr.Credit) };
            lines.AddRange(posting.Lines);
            var v = new LedgerService(c).Post(new Voucher(Guid.NewGuid(), c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Purchase).Id, D1, lines));

            var doc = rcm.GenerateSelfInvoice(v.Id, D1, D1.AddDays(3), supplierIsRegistered: false, supplierLedgerId: party.Id)!;
            // Exercise the (S2b-empty) CDN + advance tables directly so their persistence round-trips.
            c.AddCreditDebitNoteLink(new GstCreditDebitNoteLink(Guid.NewGuid(), v.Id, CdnType.Credit, v.Id, "INV-1", D1, "01 sales return"));
            c.AddAdvanceReceipt(new GstAdvanceReceipt(Guid.NewGuid(), v.Id, isService: true, Money.FromRupees(5000m), 1800, interState: true, "24", Money.FromRupees(900m)));

            using (var store = new SqliteCompanyStore(dbPath)) store.Save(c);
            Company reloaded;
            using (var store = new SqliteCompanyStore(dbPath)) reloaded = store.Load(c.Id)!;

            // Categories survived; the linked Legal category resolves.
            Assert.Equal(c.Gst!.RcmCategories.Count, reloaded.Gst!.RcmCategories.Count);
            Assert.Contains(reloaded.Gst.RcmCategories, x => x.SupplyNature == "Cement" && x.Stream == RcmStream.Section9_4 && x.HsnSac == "2523");

            // The S/P-ledger RCM flags + party qualifiers survived.
            var rl = reloaded.FindLedgerByName("Legal Fees")!;
            Assert.True(rl.SalesPurchaseGst!.ReverseChargeApplicable);
            Assert.Equal(legal.Id, rl.SalesPurchaseGst.RcmCategoryId);
            Assert.True(reloaded.FindLedgerByName("Advocate (Gujarat)")!.PartyGst!.IsBodyCorporate);

            // The RCM Output IGST ledger's reverse-charge classification survived.
            var rcmOut = reloaded.Ledgers.Single(l => l.GstClassification is { IsReverseCharge: true, TaxHead: GstTaxHead.Integrated });
            Assert.Equal(GstTaxDirection.Output, rcmOut.GstClassification!.Direction);

            // The RCM-tagged tax lines survived (one output liability, one ITC with the OtherRcm scheme).
            var rv = reloaded.FindVoucher(v.Id)!;
            Assert.Contains(rv.Lines, l => l.Gst is { IsReverseCharge: true, RcmScheme: null } && l.Side == DrCr.Credit);
            Assert.Contains(rv.Lines, l => l.Gst is { IsReverseCharge: true, RcmScheme: RcmItcScheme.OtherRcm } && l.Side == DrCr.Debit);

            // The self-invoice document survived, linked to the source voucher.
            var rdoc = Assert.Single(reloaded.RcmDocuments);
            Assert.Equal(RcmDocumentKind.SelfInvoice, rdoc.Kind);
            Assert.Equal(v.Id, rdoc.SourceVoucherId);
            Assert.Equal(party.Id, rdoc.SupplierLedgerId);
            Assert.Equal(doc.SeriesNumber, rdoc.SeriesNumber);

            // The CDN link + advance receipt survived paisa-exact.
            var rlink = Assert.Single(reloaded.CreditDebitNoteLinks);
            Assert.Equal(CdnType.Credit, rlink.CdnType);
            Assert.Equal("01 sales return", rlink.ReasonCode);
            var radv = Assert.Single(reloaded.AdvanceReceipts);
            Assert.True(radv.IsService);
            Assert.Equal(Money.FromRupees(900m), radv.AdvanceTax);
            Assert.Equal(Money.FromRupees(5000m), radv.AdvanceAmount);
        }
        finally { Delete(dbPath); }
    }

    private static RcmCategory Cat(Company c, string nature) => c.Gst!.RcmCategories.First(x => x.SupplyNature == nature);

    // ---- helpers ----

    private static string TempDb(string prefix) => Path.Combine(Path.GetTempPath(), $"{prefix}-{Guid.NewGuid():N}.db");
    private static void Delete(string dbPath) => TempDbFile.Delete(dbPath);
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

    private static string ReadScalarStr(string dbPath, string sql)
    {
        using var conn = Open(dbPath);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        var v = (string)cmd.ExecuteScalar()!;
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
            while (r.Read())
                names.Add(r.GetString(1));
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

    private static void Exec(SqliteConnection conn, string sql, params (string Name, object Value)[] ps)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (n, v) in ps) cmd.Parameters.AddWithValue(n, v);
        cmd.ExecuteNonQuery();
    }

    /// <summary>A minimal pre-v39 (v38) DDL: the tables the v38→v39 migration touches (four new CREATE TABLE reference
    /// companies/vouchers/ledgers; ALTERs hit stock_items/ledgers/entry_lines/voucher_types), plus data-preservation
    /// assertions. Kept in the test so it never drifts as the production schema advances.</summary>
    private const string MinimalV38Ddl = """
        CREATE TABLE schema_version (version INTEGER NOT NULL);
        CREATE TABLE companies (id TEXT NOT NULL PRIMARY KEY, name TEXT NOT NULL);
        CREATE TABLE ledgers (id TEXT NOT NULL PRIMARY KEY, name TEXT NOT NULL);
        CREATE TABLE vouchers (id TEXT NOT NULL PRIMARY KEY, company_id TEXT NULL);
        CREATE TABLE voucher_types (id TEXT NOT NULL PRIMARY KEY, name TEXT NOT NULL);
        CREATE TABLE stock_items (id TEXT NOT NULL PRIMARY KEY, name TEXT NOT NULL);
        CREATE TABLE entry_lines (id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT, voucher_id TEXT NOT NULL,
            line_order INTEGER NOT NULL DEFAULT 0, ledger_id TEXT NOT NULL DEFAULT '', amount_paisa INTEGER NOT NULL DEFAULT 0,
            side INTEGER NOT NULL DEFAULT 0);
        -- voucher_inventory_lines is required because the chain now runs through the v45 -> v46 item-invoice
        -- line-unit migration, whose ALTER TABLE voucher_inventory_lines ADD COLUMN unit_id needs the table to
        -- exist. A real database of this vintage always has it (created at v12); this fixture is a minimal
        -- hand-written subset, so the table is declared here for the ALTER to land on.
        CREATE TABLE voucher_inventory_lines (id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT, voucher_id TEXT NOT NULL, line_order INTEGER NOT NULL DEFAULT 0, stock_item_id TEXT NOT NULL DEFAULT '', godown_id TEXT NOT NULL DEFAULT '', quantity_micro INTEGER NOT NULL DEFAULT 0, direction INTEGER NOT NULL DEFAULT 0, rate_paisa INTEGER NOT NULL DEFAULT 0);
        """;
}
