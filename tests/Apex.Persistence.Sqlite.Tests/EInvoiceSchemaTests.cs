using System.Text;
using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Services;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Apex.Persistence.Sqlite.Tests;

/// <summary>
/// Phase-9 slice-4a <b>e-Invoice</b> schema contract (v40→v41; ER-13, ER-16). The bump is additive: one new table
/// (<c>einvoice_records</c> + its index) and the e-invoice / B2C-QR / connector-mode config columns on <c>companies</c>,
/// plus the four <c>nic_*_enc</c> protected-credential BLOB columns. Covers: a fresh DB stamps to
/// <see cref="Schema.CurrentVersion"/> with the new table/columns; a legacy v40 DB auto-migrates forward preserving every
/// row (new table empty, new columns default 0/NULL/threshold — ER-13) and re-opening is idempotent; a save/load
/// round-trips a Generated e-invoice record + config; and the NIC credential BLOBs round-trip as opaque ciphertext via
/// <see cref="SqliteNicCredentialStore"/> and NEVER surface on <see cref="GstConfig"/> (ER-16).
/// </summary>
public sealed class EInvoiceSchemaTests
{
    private const string GstinMaharashtra = "27AAPFU0939F1ZV";
    private const string GstinGujarat = "24AAACC1206D1ZM";
    private static readonly DateOnly FyStart = new(2025, 4, 1);

    [Fact]
    [Trait("Category", "RoundTrip")]
    public void Fresh_database_stamps_to_current_version_with_new_table_and_columns()
    {
        var dbPath = TempDb("apex-einv-fresh");
        try
        {
            using (new SqliteCompanyStore(dbPath)) { }

            Assert.True(Schema.CurrentVersion >= 41);
            Assert.Equal((long)Schema.CurrentVersion, ReadSchemaVersion(dbPath));
            Assert.True(TableExists(dbPath, "einvoice_records"));

            foreach (var col in new[]
            {
                "einvoicing_enabled", "einvoice_applicable_from", "einvoice_aato_threshold_paisa",
                "einvoice_applicability_override", "einvoice_exemption_classes", "einvoice_reporting_age_applies",
                "gst_connector_mode", "b2c_dynamic_qr_enabled", "b2c_qr_aato_threshold_paisa", "b2c_qr_upi_id",
                "b2c_qr_payee_name", "nic_client_id_enc", "nic_client_secret_enc", "nic_api_username_enc",
                "nic_api_password_enc",
            })
                Assert.Contains(col, ColumnNames(dbPath, "companies"));
        }
        finally { Delete(dbPath); }
    }

    [Fact]
    [Trait("Category", "RoundTrip")]
    public void Legacy_v40_database_auto_migrates_to_v41_preserving_every_row()
    {
        var dbPath = TempDb("apex-einv-v40legacy");
        try
        {
            var connStr = new SqliteConnectionStringBuilder { DataSource = dbPath, Mode = SqliteOpenMode.ReadWriteCreate }.ToString();
            using (var conn = new SqliteConnection(connStr))
            {
                conn.Open();
                Exec(conn, MinimalV40Ddl);
                Exec(conn, "INSERT INTO schema_version(version) VALUES (40);");
                Exec(conn, "INSERT INTO companies(id, name) VALUES ('c-1', 'Legacy V40 Co');");
                SqliteConnection.ClearPool(conn);
            }

            Assert.Equal(40L, ReadSchemaVersion(dbPath));
            Assert.DoesNotContain("einvoicing_enabled", ColumnNames(dbPath, "companies"));

            using (new SqliteCompanyStore(dbPath)) { } // opens v40 → migrates to current

            Assert.Equal((long)Schema.CurrentVersion, ReadSchemaVersion(dbPath));
            Assert.True(TableExists(dbPath, "einvoice_records"));
            Assert.Contains("einvoicing_enabled", ColumnNames(dbPath, "companies"));
            Assert.Contains("nic_client_id_enc", ColumnNames(dbPath, "companies"));

            // Every existing row survived; new table empty; new columns default 0/threshold/NULL (ER-13).
            Assert.Equal("Legacy V40 Co", ReadScalarStr(dbPath, "SELECT name FROM companies LIMIT 1;"));
            Assert.Equal(0L, ReadScalar(dbPath, "SELECT COUNT(*) FROM einvoice_records;"));
            Assert.Equal(0L, ReadScalar(dbPath, "SELECT einvoicing_enabled FROM companies LIMIT 1;"));
            Assert.Equal(5000000000L, ReadScalar(dbPath, "SELECT einvoice_aato_threshold_paisa FROM companies LIMIT 1;"));
            Assert.Equal(500000000000L, ReadScalar(dbPath, "SELECT b2c_qr_aato_threshold_paisa FROM companies LIMIT 1;"));
            Assert.Equal(1L, ReadScalar(dbPath, "SELECT COUNT(*) FROM companies WHERE nic_client_id_enc IS NULL;"));

            // Re-opening is idempotent.
            using (new SqliteCompanyStore(dbPath)) { }
            Assert.Equal((long)Schema.CurrentVersion, ReadSchemaVersion(dbPath));
        }
        finally { Delete(dbPath); }
    }

    [Fact]
    [Trait("Category", "RoundTrip")]
    public void Save_load_round_trips_a_generated_einvoice_record_and_config()
    {
        var dbPath = TempDb("apex-einv-roundtrip");
        try
        {
            var c = CompanyFactory.CreateSeeded("e-Invoice Co", FyStart);
            var gst = new GstService(c);
            gst.EnableGst(new GstConfig
            {
                HomeStateCode = "27", Gstin = GstinMaharashtra, RegistrationType = GstRegistrationType.Regular,
                ApplicableFrom = FyStart, Periodicity = GstReturnPeriodicity.Monthly,
                EInvoicingEnabled = true, EInvoiceApplicableFrom = new DateOnly(2025, 4, 5),
                EInvoiceAatoThreshold = new Money(100_000_000m),
                ReportingAgeLimitApplies = true, ConnectorMode = GstConnectorMode.CustomerNicDirect,
                B2cDynamicQrEnabled = true, B2cQrUpiId = "acme@upi", B2cQrPayeeName = "Acme",
            });

            // A genuinely COVERED B2B sale carries forward tax (an exempt/nil-only B2B supply is a Bill of Supply and is
            // Excluded, never e-invoiced). Inter-state to Gujarat ⇒ IGST @18% on ₹1,000 = ₹180 ⇒ invoice ₹1,180.
            var inv = new InventoryService(c);
            var item = inv.CreateStockItem("Widget", inv.CreateStockGroup("Goods").Id,
                inv.CreateSimpleUnit("Nos", "Numbers", unitQuantityCode: "NOS").Id);
            item.Gst = new StockItemGstDetails { HsnSac = "847130", Taxability = GstTaxability.Taxable, RateBasisPoints = 1800 };
            inv.AddOpeningBalance(item.Id, c.MainLocation!.Id, 100m, Money.FromRupees(40000m));

            var sales = Add(c, "Sales", "Sales Accounts", false);
            var b2b = Add(c, "Debtor", "Sundry Debtors", true);
            b2b.PartyGst = new PartyGstDetails { RegistrationType = GstRegistrationType.Regular, Gstin = GstinGujarat, StateCode = "24" };
            var tax = gst.ComputeInvoiceTax(new[] { new GstService.TaxableLine(Money.FromRupees(1000m), 1800) },
                interState: true, GstTaxDirection.Output);
            var saleLines = new List<EntryLine>
            {
                new(b2b.Id, new Money(1000m + tax.TotalTax.Amount), DrCr.Debit),
                new(sales.Id, Money.FromRupees(1000m), DrCr.Credit),
            };
            saleLines.AddRange(tax.TaxLines);
            var sale = new LedgerService(c).Post(new Voucher(Guid.NewGuid(),
                c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Sales).Id, new DateOnly(2025, 4, 10),
                saleLines, partyId: b2b.Id,
                inventoryLines: new[] { new VoucherInventoryLine(item.Id, c.MainLocation!.Id, 1m, Money.FromRupees(1000m)) }));
            var svc = new EInvoiceService(c);
            var record = svc.PrepareRecord(sale);
            svc.RecordIrpResponse(record, new string('A', 64), "ACK123", new DateOnly(2025, 4, 10), "IRP-QR", new byte[] { 1, 2, 3 });
            // Set a typed exemption AFTER minting the record (exemption is company-level; it round-trips independently).
            c.Gst!.ExemptionClasses = EInvoiceExemptionClass.Gta;

            using (var store = new SqliteCompanyStore(dbPath)) store.Save(c);
            Company reloaded;
            using (var store = new SqliteCompanyStore(dbPath)) reloaded = store.Load(c.Id)!;

            // Config survived (paisa / enum / date / bool exact).
            var g = reloaded.Gst!;
            Assert.True(g.EInvoicingEnabled);
            Assert.Equal(new DateOnly(2025, 4, 5), g.EInvoiceApplicableFrom);
            Assert.Equal(new Money(100_000_000m), g.EInvoiceAatoThreshold);
            Assert.Equal(EInvoiceExemptionClass.Gta, g.ExemptionClasses);
            Assert.True(g.ReportingAgeLimitApplies);
            Assert.Equal(GstConnectorMode.CustomerNicDirect, g.ConnectorMode);
            Assert.True(g.B2cDynamicQrEnabled);
            Assert.Equal("acme@upi", g.B2cQrUpiId);

            // The Generated record survived, re-linked to the imported voucher (IRN/QR/signed-JSON verbatim).
            var r = Assert.Single(reloaded.EInvoiceRecords);
            Assert.Equal(EInvoiceStatus.Generated, r.Status);
            Assert.Equal(new string('A', 64), r.Irn);
            Assert.Equal("IRP-QR", r.SignedQr);
            Assert.Equal(new byte[] { 1, 2, 3 }, r.SignedJson);
            Assert.Equal(new DateOnly(2025, 4, 10), r.AckDate);
        }
        finally { Delete(dbPath); }
    }

    [Fact]
    [Trait("Category", "RoundTrip")]
    public void Nic_credentials_round_trip_as_opaque_ciphertext_and_never_surface_on_gstconfig()
    {
        var dbPath = TempDb("apex-einv-nic");
        try
        {
            var c = CompanyFactory.CreateSeeded("NIC Co", FyStart);
            new GstService(c).EnableGst(new GstConfig
            {
                HomeStateCode = "27", Gstin = GstinMaharashtra, RegistrationType = GstRegistrationType.Regular,
                ApplicableFrom = FyStart, Periodicity = GstReturnPeriodicity.Monthly,
            });
            using (var store = new SqliteCompanyStore(dbPath)) store.Save(c);

            var creds = new NicApiCredentials("client-42", "s3cr3t-shhh", "apiuser", "p@ssw0rd");
            using (var nic = new SqliteNicCredentialStore(dbPath))
            {
                Assert.False(nic.HasCredentials(GstinMaharashtra));
                nic.Store(GstinMaharashtra, creds);
                Assert.True(nic.HasCredentials(GstinMaharashtra));
            }

            // Round-trips via a fresh store (protected-at-rest survives a reopen).
            using (var nic = new SqliteNicCredentialStore(dbPath))
                Assert.Equal(creds, nic.Get(GstinMaharashtra));

            // The on-disk blob is ciphertext (≠ the plaintext bytes) — protected at rest.
            var rawClientId = ReadBlob(dbPath, "SELECT nic_client_id_enc FROM companies WHERE gstin = '27AAPFU0939F1ZV';");
            Assert.NotNull(rawClientId);
            Assert.NotEqual(Encoding.UTF8.GetBytes(creds.ClientId), rawClientId!);
            // No plaintext secret leaked into any credential blob.
            Assert.DoesNotContain("s3cr3t-shhh", ReadAllCompanyText(dbPath));

            // The loaded company's GstConfig exposes NO secret-bearing property (ER-16, structural).
            Company reloaded;
            using (var store = new SqliteCompanyStore(dbPath)) reloaded = store.Load(c.Id)!;
            Assert.NotNull(reloaded.Gst);
            var secretish = typeof(GstConfig).GetProperties()
                .Where(p => p.Name.Contains("Nic", StringComparison.OrdinalIgnoreCase)
                         || p.Name.Contains("ClientSecret", StringComparison.OrdinalIgnoreCase)
                         || p.Name.Contains("Password", StringComparison.OrdinalIgnoreCase)
                         || p.Name.Contains("Credential", StringComparison.OrdinalIgnoreCase))
                .ToList();
            Assert.Empty(secretish);

            // Clear removes them.
            using (var nic = new SqliteNicCredentialStore(dbPath)) nic.Clear(GstinMaharashtra);
            using (var nic = new SqliteNicCredentialStore(dbPath)) Assert.False(nic.HasCredentials(GstinMaharashtra));
        }
        finally { Delete(dbPath); }
    }

    // ---- helpers ----

    private static Apex.Ledger.Domain.Ledger Add(Company c, string name, string groupName, bool openingIsDebit)
    {
        var l = new Apex.Ledger.Domain.Ledger(Guid.NewGuid(), name, c.FindGroupByName(groupName)!.Id, Money.Zero, openingIsDebit);
        c.AddLedger(l);
        return l;
    }

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

    private static byte[]? ReadBlob(string dbPath, string sql)
    {
        using var conn = Open(dbPath);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        var v = cmd.ExecuteScalar();
        SqliteConnection.ClearPool(conn);
        return v is byte[] b ? b : null;
    }

    private static string ReadAllCompanyText(string dbPath)
    {
        using var conn = Open(dbPath);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT quote(nic_client_id_enc) || quote(nic_client_secret_enc) || quote(nic_api_username_enc) || quote(nic_api_password_enc) FROM companies;";
        var v = cmd.ExecuteScalar()?.ToString() ?? "";
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

    private static void Exec(SqliteConnection conn, string sql)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    /// <summary>A minimal pre-v41 (v40) DDL: the v40→v41 migration creates <c>einvoice_records</c> (FK companies +
    /// vouchers) and ALTERs <c>companies</c>. <c>stock_items</c> + <c>ledgers</c> are included because the
    /// migrate-to-current chain runs through the v42→v43 §17(5) ALTER on both, which needs the tables to exist.</summary>
    private const string MinimalV40Ddl = """
        CREATE TABLE schema_version (version INTEGER NOT NULL);
        CREATE TABLE companies (id TEXT NOT NULL PRIMARY KEY, name TEXT NOT NULL);
        CREATE TABLE vouchers (id TEXT NOT NULL PRIMARY KEY, company_id TEXT NULL);
        CREATE TABLE stock_items (id TEXT NOT NULL PRIMARY KEY, name TEXT NOT NULL);
        CREATE TABLE ledgers (id TEXT NOT NULL PRIMARY KEY, name TEXT NOT NULL);
        """;
}
