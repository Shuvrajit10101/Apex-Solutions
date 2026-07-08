using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Reports;
using Apex.Ledger.Services;
using Xunit;

namespace Apex.Persistence.Sqlite.Tests;

/// <summary>
/// POS persistence contract (Phase 6 slice 7; RQ-38..RQ-44; ER-13; DP-6). A company with a POS-flagged Sales type
/// and a posted multi-tender POS voucher SAVES and RELOADS at <see cref="Schema.CurrentVersion"/> preserving every
/// field: the <c>use_for_pos</c> flag, the retail-till config (default godown/party, print-after-save, title,
/// messages, declaration), the tender-ledger class map, and each tender row (type, ledger, amount, tendered,
/// change, card/bank/cheque). The reloaded voucher re-validates and re-posts identically. A company with NO POS
/// data round-trips byte-identically (the flag defaults 0, the three tables stay empty).
/// </summary>
public sealed class PosRoundTripTests
{
    private static readonly DateOnly FyStart = new(2024, 4, 1);
    private static readonly DateOnly D1 = new(2024, 4, 5);
    private static readonly DateOnly D2 = new(2024, 4, 10);
    private const string GstinMaharashtra = "27AAPFU0939F1ZV";

    private static Apex.Ledger.Domain.Ledger AddLedger(Company c, string name, string groupName, bool openingIsDebit)
    {
        var l = new Apex.Ledger.Domain.Ledger(Guid.NewGuid(), name, c.FindGroupByName(groupName)!.Id, Money.Zero, openingIsDebit);
        c.AddLedger(l);
        return l;
    }

    private static (Company Company, Guid PosTypeId, Guid GiftId, Guid CashId, Guid MainId) SeedWithPos()
    {
        var c = CompanyFactory.CreateSeeded("POS Retail Co", FyStart);
        var gst = new GstService(c);
        gst.EnableGst(new GstConfig
        {
            HomeStateCode = "27", Gstin = GstinMaharashtra, RegistrationType = GstRegistrationType.Regular,
            ApplicableFrom = FyStart, Periodicity = GstReturnPeriodicity.Monthly,
        });

        var masters = new InventoryService(c);
        var grp = masters.CreateStockGroup("Goods");
        var nos = masters.CreateSimpleUnit("Nos", "Numbers");
        var item = masters.CreateStockItem("Widget", grp.Id, nos.Id, valuationMethod: StockValuationMethod.Fifo);
        item.Gst = new StockItemGstDetails { Taxability = GstTaxability.Taxable, RateBasisPoints = 1800 };
        var main = c.MainLocation!.Id;

        var sales = AddLedger(c, "Sales (POS)", "Sales Accounts", openingIsDebit: false);
        var gift = AddLedger(c, "Gift Voucher", "Sundry Debtors", openingIsDebit: true);
        var card = AddLedger(c, "ICICI Credit Card", "Bank Accounts", openingIsDebit: true);
        var cheque = AddLedger(c, "SBI Cheque", "Bank Accounts", openingIsDebit: true);
        var cash = AddLedger(c, "Cash", "Cash-in-Hand", openingIsDebit: true);
        var walkIn = AddLedger(c, "(cash)", "Sundry Debtors", openingIsDebit: true);

        var cfg = new PosConfig
        {
            DefaultGodownId = main, DefaultPartyId = walkIn.Id, PrintAfterSave = true,
            DefaultTitle = "Retail Invoice", Message1 = "Thank you for shopping", Message2 = "Visit again",
            Declaration = "Goods once sold are not returnable",
        };
        cfg.SetTenderLedgerDefault(PosTenderType.GiftVoucher, gift.Id);
        cfg.SetTenderLedgerDefault(PosTenderType.Card, card.Id);
        cfg.SetTenderLedgerDefault(PosTenderType.Cheque, cheque.Id);
        cfg.SetTenderLedgerDefault(PosTenderType.Cash, cash.Id);
        var posType = new VoucherType(Guid.NewGuid(), "Sales (POS)", VoucherBaseType.Sales, useForPos: true, posConfig: cfg);
        c.AddVoucherType(posType);

        var ledgers = new LedgerService(c);
        var purchases = AddLedger(c, "Purchases", "Purchase Accounts", openingIsDebit: true);
        var creditor = AddLedger(c, "Creditor", "Sundry Creditors", openingIsDebit: false);
        ledgers.Post(new Voucher(Guid.NewGuid(), c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Purchase).Id, D1,
            new[] { new EntryLine(purchases.Id, Money.FromRupees(20000m), DrCr.Debit), new EntryLine(creditor.Id, Money.FromRupees(20000m), DrCr.Credit) },
            inventoryLines: new[] { new VoucherInventoryLine(item.Id, main, 10m, Money.FromRupees(2000m)) }));

        // PR-9 multi-tender POS bill: taxable 10,225 @ 18% ⇒ CGST/SGST 920.25; bill total 12,065.50.
        var residual = PosTenderService.CashResidual(Money.FromRupees(12065.50m), Money.FromRupees(500m), Money.FromRupees(5000m), Money.FromRupees(5000m));
        var change = PosTenderService.Change(Money.FromRupees(1600m), residual);
        var tenders = new[]
        {
            new PosTender(PosTenderType.GiftVoucher, gift.Id, Money.FromRupees(500m)),
            new PosTender(PosTenderType.Card, card.Id, Money.FromRupees(5000m), CardNo: "4111"),
            new PosTender(PosTenderType.Cheque, cheque.Id, Money.FromRupees(5000m), BankName: "SBI", ChequeNo: "235681"),
            new PosTender(PosTenderType.Cash, cash.Id, residual, Tendered: Money.FromRupees(1600m), Change: change),
        };
        var tax = gst.ComputeInvoiceTax(new[] { new GstService.TaxableLine(Money.FromRupees(10225m), 1800) }, interState: false, GstTaxDirection.Output);
        var lines = new List<EntryLine>();
        lines.AddRange(PosTenderService.BuildTenderDebitLines(tenders));
        lines.Add(new EntryLine(sales.Id, Money.FromRupees(10225m), DrCr.Credit));
        lines.AddRange(tax.TaxLines);
        ledgers.Post(new Voucher(Guid.NewGuid(), posType.Id, D2, lines,
            narration: "POS bill",
            inventoryLines: new[] { new VoucherInventoryLine(item.Id, main, 1m, Money.FromRupees(10225m)) },
            posTenders: tenders));

        return (c, posType.Id, gift.Id, cash.Id, main);
    }

    [Fact]
    [Trait("Category", "RoundTrip")]
    public void Pos_voucher_type_config_tenders_survive_save_reload_at_current_version()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"apex-pos-rt-{Guid.NewGuid():N}.db");
        try
        {
            var (original, posTypeId, giftId, cashId, mainId) = SeedWithPos();

            using (var write = new SqliteCompanyStore(dbPath))
            {
                write.Save(original);
                write.Save(original); // re-save (upsert) must not trip an FK
                Assert.Equal((long)Schema.CurrentVersion, ReadSchemaVersion(dbPath));
            }

            Company reloaded;
            using (var read = new SqliteCompanyStore(dbPath))
                reloaded = read.Load(original.Id)!;

            // Voucher-type flag + retail-till config preserved.
            var posType = reloaded.FindVoucherType(posTypeId)!;
            Assert.True(posType.UseForPos);
            var cfg = posType.PosConfig!;
            Assert.Equal(mainId, cfg.DefaultGodownId);
            Assert.NotNull(cfg.DefaultPartyId);
            Assert.True(cfg.PrintAfterSave);
            Assert.Equal("Retail Invoice", cfg.DefaultTitle);
            Assert.Equal("Thank you for shopping", cfg.Message1);
            Assert.Equal("Visit again", cfg.Message2);
            Assert.Equal("Goods once sold are not returnable", cfg.Declaration);
            // Tender-ledger class map preserved (all four).
            Assert.Equal(giftId, cfg.TenderLedgerDefault(PosTenderType.GiftVoucher));
            Assert.Equal(cashId, cfg.TenderLedgerDefault(PosTenderType.Cash));
            Assert.Equal(4, cfg.TenderLedgerDefaults.Count);

            // The POS voucher's tender rows preserved exactly.
            var pos = reloaded.Vouchers.Single(v => v.HasPosTenders);
            Assert.Equal(4, pos.PosTenders.Count);
            var gift = pos.PosTenders.Single(t => t.Type == PosTenderType.GiftVoucher);
            Assert.Equal(giftId, gift.LedgerId);
            Assert.Equal(Money.FromRupees(500m), gift.Amount);
            var card = pos.PosTenders.Single(t => t.Type == PosTenderType.Card);
            Assert.Equal("4111", card.CardNo);
            var cheque = pos.PosTenders.Single(t => t.Type == PosTenderType.Cheque);
            Assert.Equal("SBI", cheque.BankName);
            Assert.Equal("235681", cheque.ChequeNo);
            var cash = pos.PosTenders.Single(t => t.Type == PosTenderType.Cash);
            Assert.Equal(Money.FromRupees(1565.50m), cash.Amount);        // posts the residual
            Assert.Equal(Money.FromRupees(1600m), cash.Tendered);
            Assert.Equal(Money.FromRupees(34.50m), cash.Change);          // informational change

            // The reloaded voucher re-validates (Σ tenders == bill total, grouping OK) and its accounting survives.
            VoucherValidator.EnsurePosTendersValid(pos, reloaded);
            Assert.Equal(1565.50m, LedgerBalances.SignedClosing(reloaded, reloaded.FindLedger(cashId)!, D2));
            Assert.Equal(9m, new InventoryLedger(reloaded).OnHand(reloaded.Vouchers.SelectMany(v => v.InventoryLines).First().StockItemId, mainId, D2));
        }
        finally { TempDbFile.Delete(dbPath); }
    }

    [Fact]
    [Trait("Category", "RoundTrip")]
    public void A_company_with_no_pos_data_round_trips_byte_identically()
    {
        var pathA = Path.Combine(Path.GetTempPath(), $"apex-pos-nopos-a-{Guid.NewGuid():N}.db");
        var pathB = Path.Combine(Path.GetTempPath(), $"apex-pos-nopos-b-{Guid.NewGuid():N}.db");
        try
        {
            // A plain trading company (no POS type, no POS voucher).
            var c = CompanyFactory.CreateSeeded("Plain Co", FyStart);
            var masters = new InventoryService(c);
            var grp = masters.CreateStockGroup("Goods");
            var nos = masters.CreateSimpleUnit("Nos", "Numbers");
            var item = masters.CreateStockItem("Widget", grp.Id, nos.Id);
            var purchases = AddLedger(c, "Purchases", "Purchase Accounts", true);
            var creditor = AddLedger(c, "Creditor", "Sundry Creditors", false);
            new LedgerService(c).Post(new Voucher(Guid.NewGuid(), c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Purchase).Id, D1,
                new[] { new EntryLine(purchases.Id, Money.FromRupees(1000m), DrCr.Debit), new EntryLine(creditor.Id, Money.FromRupees(1000m), DrCr.Credit) },
                inventoryLines: new[] { new VoucherInventoryLine(item.Id, c.MainLocation!.Id, 10m, Money.FromRupees(100m)) }));

            using (var s = new SqliteCompanyStore(pathA)) s.Save(c);

            // Reload via the v23 adapter and re-save to a second file — the POS additions are inert for non-POS data.
            Company reloaded;
            using (var s = new SqliteCompanyStore(pathA)) reloaded = s.Load(c.Id)!;
            using (var s = new SqliteCompanyStore(pathB)) s.Save(reloaded);

            // No voucher carries POS tenders; the three POS tables are empty; the flag is 0 everywhere.
            Assert.All(reloaded.Vouchers, v => Assert.False(v.HasPosTenders));
            Assert.All(reloaded.VoucherTypes, t => Assert.False(t.UseForPos));
            Assert.Equal(0L, CountRows(pathA, "pos_tender_allocations"));
            Assert.Equal(0L, CountRows(pathA, "pos_voucher_type_config"));
            Assert.Equal(0L, CountRows(pathA, "pos_tender_ledger_defaults"));

            // Byte-identical persisted image across the round-trip (ER-13).
            Assert.Equal(File.ReadAllBytes(pathA), File.ReadAllBytes(pathB));
        }
        finally { TempDbFile.Delete(pathA); TempDbFile.Delete(pathB); }
    }

    // ---- helpers ----

    private static long ReadSchemaVersion(string dbPath)
    {
        using var conn = new Microsoft.Data.Sqlite.SqliteConnection(
            new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder { DataSource = dbPath, Mode = Microsoft.Data.Sqlite.SqliteOpenMode.ReadWrite }.ToString());
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT version FROM schema_version LIMIT 1;";
        var v = Convert.ToInt64(cmd.ExecuteScalar());
        Microsoft.Data.Sqlite.SqliteConnection.ClearPool(conn);
        return v;
    }

    private static long CountRows(string dbPath, string table)
    {
        using var conn = new Microsoft.Data.Sqlite.SqliteConnection(
            new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder { DataSource = dbPath, Mode = Microsoft.Data.Sqlite.SqliteOpenMode.ReadWrite }.ToString());
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT COUNT(*) FROM {table};";
        var n = Convert.ToInt64(cmd.ExecuteScalar());
        Microsoft.Data.Sqlite.SqliteConnection.ClearPool(conn);
        return n;
    }
}
