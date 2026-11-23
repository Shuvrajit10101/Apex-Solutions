using System.Text.Json;
using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Io;
using Apex.Ledger.Services;
using Apex.Persistence.Sqlite;
using Xunit;
using Domain = Apex.Ledger.Domain;

namespace Apex.Persistence.Sqlite.Tests;

/// <summary>
/// 🔴 <b>WAVE 12, TRACK ha — THE LEDGER MASTER'S DEFAULT BLOCK IS "AS PER COMPANY/GROUP" FOR EVERY READER (IV-316 (5)
/// and (6)).</b> Ticking GST Applicable on the Ledger master and leaving every field at its default writes a block that
/// is Taxable with no HSN/SAC, no rate, no cess and no reverse charge. Two families of reader treated that block as a
/// declaration of its own and got the wrong answer (wave-12 review R1 and R2):
/// <list type="bullet">
///   <item><b>R1 — the winning detail block.</b> <c>GstService.ResolveDetailBlock</c> returned it as the first rung
///     under the Ledger-first order, so reverse charge a stock item declares was masked (and the item's dated cess,
///     pinned on the screen by <c>LedgerMasterAsPerGroupBlockCessTests</c> in Apex.Desktop.Tests).</item>
///   <item><b>R2 — composition turnover.</b> <c>GstReportSupport.OutwardSupplyValue</c> and its <c>LineIsTaxable</c>
///     read the block's own <c>IsTaxable</c>, so under a group declaring Exempt a composition Trader taxed the sale
///     (review probe P2: ₹1,234.57, CGST 6.18) while every other return filed it as exempt.</item>
/// </list>
/// Every figure is read off a book saved and re-loaded through a fresh <see cref="SqliteCompanyStore"/>.
/// <para><b>Mutations.</b> F-M1 (the ledger-rung skip in <c>ResolveDetailBlock</c> removed) reddens
/// <see cref="Reverse_charge_an_item_declares_survives_a_ticked_default_purchase_ledger"/>. F-M2 (the as-voucher leg
/// reads <c>SalesPurchaseGst?.IsTaxable</c> again) reddens
/// <see cref="A_composition_trader_does_not_tax_an_as_voucher_sale_the_group_declares_exempt"/>. F-M3
/// (<c>LineIsTaxable</c> reads the item block, else the first ledger block, again) reddens
/// <see cref="A_composition_trader_does_not_tax_an_item_sale_the_group_declares_exempt"/>.</para>
/// </summary>
public sealed class AsPerGroupLedgerBlockReloadTests
{
    private static readonly DateOnly FyFrom = new(2025, 4, 1);
    private static readonly DateOnly FyTo = new(2026, 3, 31);
    private static readonly DateOnly Q1To = new(2025, 6, 30);
    private static readonly DateOnly D = new(2025, 4, 10);
    private const string Gstin = "27AAPFU0939F1ZV";

    /// <summary>Exactly what the Ledger master writes for GST Applicable ticked with every field left at default.</summary>
    private static StockItemGstDetails ScreenDefaultBlock() => new()
    {
        HsnSac = null, Taxability = GstTaxability.Taxable, RateBasisPoints = null, SupplyType = GstSupplyType.Goods,
        ValuationBasis = GstValuationBasis.TransactionValue, ItcEligibility = ItcEligibility.Eligible,
        BlockedCreditCategory = BlockedCreditCategory.None,
    };

    // ================================================================ R1: reverse charge

    /// <summary>A stock item declares reverse charge under the §9(4) "Capital-goods" category; the purchase ledger is
    /// the screen's default block. The pair must still be raised: ₹12,345.67 @ 18% intra = CGST 1,111.11 + SGST
    /// 1,111.11 in GSTR-3B 3.1(d). Before the fix the ledger's block won the walk and the reverse charge was dropped.
    /// The control (the same item through an unticked ledger) raises the same pair.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Reverse_charge_an_item_declares_survives_a_ticked_default_purchase_ledger(bool ledgerTicked)
    {
        var c = Reload(RcmBook(ledgerTicked));
        Assert.Equal(GstDetailSource.LedgerFirst, c.Gst!.SourceOfGstRate);
        var item = c.StockItems.Single(i => i.Name == "Server Rack");
        var purchases = c.FindLedgerByName("Purchases")!;
        Assert.Equal(ledgerTicked, purchases.SalesPurchaseGst is not null);       // the premise survived the reload
        var supplier = c.FindLedgerByName("Promoter Supplier")!;

        var posting = new RcmService(c).BuildReverseCharge(new Money(12_345.67m), item, purchases,
            supplier.PartyGst, D, RcmService.SupplyKind.Domestic, recipientIsPromoter: true, quantity: 1m);
        Assert.True(posting.Applies, "the item's reverse charge was masked by the ledger's As-per-Group block.");

        var lines = new List<EntryLine>
        {
            new(purchases.Id, new Money(12_345.67m), DrCr.Debit),
            new(supplier.Id, new Money(12_345.67m), DrCr.Credit),
        };
        lines.AddRange(posting.Lines);
        new LedgerService(c).Post(new Voucher(Guid.NewGuid(),
            c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Purchase).Id, D, lines));

        var rc = Reload(c);
        using var g3b = JsonDocument.Parse(GstReturnJson.Gstr3b(rc, FyFrom, FyTo));
        Assert.Equal(111_111L, g3b.RootElement.GetProperty("tbl3_1d_rcm_camt_paisa").GetInt64());
        Assert.Equal(111_111L, g3b.RootElement.GetProperty("tbl3_1d_rcm_samt_paisa").GetInt64());
    }

    /// <summary>Passing over the ledger's block does not over-collect: an EXEMPT item on a cess-bearing HSN through
    /// the ticked default ledger still bears no cess (the item's block now wins the walk, and it is exempt), and a
    /// ledger block that DECLARES an HSN still wins it as before — the skip is for a block that states nothing.</summary>
    [Fact]
    public void The_skip_neither_charges_cess_on_an_exempt_item_nor_unseats_a_ledger_block_that_declares()
    {
        var c = CompanyFactory.CreateSeeded("As Per Group Cess Gate Co", FyFrom);
        new GstService(c).EnableGst(new GstConfig
        {
            HomeStateCode = "27", Gstin = Gstin, RegistrationType = GstRegistrationType.Regular,
            ApplicableFrom = FyFrom, Periodicity = GstReturnPeriodicity.Monthly,
        });
        c.Gst!.AddCessRate(new GstCessRate(Guid.NewGuid(), "24022090", CessValuationMode.AdValorem,
            cessRateBasisPoints: 500, cessPerUnit: Money.Zero, cessRspFactorMillis: 0,
            effectiveFrom: FyFrom, effectiveTo: null, label: "test 5%"));
        var inv = new InventoryService(c);
        var nos = inv.CreateSimpleUnit("Nos", "Numbers");
        var tobacco = inv.CreateStockGroup("Tobacco");
        var exempt = inv.CreateStockItem("Exempt Leaf", tobacco.Id, nos.Id);
        exempt.Gst = new StockItemGstDetails { HsnSac = "24022090", Taxability = GstTaxability.Exempt };
        var taxed = inv.CreateStockItem("Cigarettes", tobacco.Id, nos.Id);
        taxed.Gst = new StockItemGstDetails { HsnSac = "24022090", Taxability = GstTaxability.Taxable, RateBasisPoints = 2800 };
        var sales = c.FindGroupByName("Sales Accounts")!.Id;
        c.AddLedger(new Domain.Ledger(Guid.NewGuid(), "Sales Ticked", sales, Money.Zero, false)
        { SalesPurchaseGst = ScreenDefaultBlock() });
        var declaring = ScreenDefaultBlock();
        declaring.HsnSac = "24021000";                                           // a different, cess-free HSN
        c.AddLedger(new Domain.Ledger(Guid.NewGuid(), "Sales Declaring", sales, Money.Zero, false)
        { SalesPurchaseGst = declaring });

        var rc = Reload(c);
        var gst = new GstService(rc);
        var ticked = rc.FindLedgerByName("Sales Ticked")!;
        Assert.Null(gst.ResolveCess(rc.StockItems.Single(i => i.Name == "Exempt Leaf"), ticked, D, 7m));
        Assert.Equal(500, gst.ResolveCess(rc.StockItems.Single(i => i.Name == "Cigarettes"), ticked, D, 7m)?.RateBasisPoints);
        Assert.Null(gst.ResolveCess(rc.StockItems.Single(i => i.Name == "Cigarettes"),
            rc.FindLedgerByName("Sales Declaring")!, D, 7m));
    }

    // ================================================================ R2: composition turnover

    /// <summary>Composition Trader (taxable-turnover base). An as-voucher cash sale of ₹1,234.57 through the screen's
    /// default ledger under Sales Accounts declaring Exempt / 999293: CMP-08 turnover base 0 and CGST 0 (review probe
    /// P2 measured 1,234.57 and 6.18). The control — the same ledger under a group that declares nothing, an
    /// unresolved walk — stays taxable (ER-5: silence is not an exemption), as it always was.</summary>
    [Theory]
    [InlineData(true, 0L, 0L)]
    [InlineData(false, 123_457L, 618L)]
    public void A_composition_trader_does_not_tax_an_as_voucher_sale_the_group_declares_exempt(
        bool groupExempt, long expectedBase, long expectedCgst)
    {
        var c = CompositionBook(groupExempt, out var ledger, out var cash);
        new LedgerService(c).Post(new Voucher(Guid.NewGuid(),
            c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Sales).Id, D, new[]
            {
                new EntryLine(cash.Id, new Money(1_234.57m), DrCr.Debit),
                new EntryLine(ledger.Id, new Money(1_234.57m), DrCr.Credit),
            }, number: 1));

        AssertCmp08(Reload(c), expectedBase, expectedCgst);
    }

    /// <summary>The item-line half of the same predicate: an item with no GST block of its own, sold for ₹1,234.57
    /// through the default ledger under the Exempt group. The walk the posting path uses (ledger: nothing; group:
    /// Exempt) says exempt, so the composition base is 0 — <c>LineIsTaxable</c> used to read the ledger block's own
    /// "Taxable".</summary>
    [Theory]
    [InlineData(true, 0L, 0L)]
    [InlineData(false, 123_457L, 618L)]
    public void A_composition_trader_does_not_tax_an_item_sale_the_group_declares_exempt(
        bool groupExempt, long expectedBase, long expectedCgst)
    {
        var c = CompositionBook(groupExempt, out var ledger, out var cash);
        var inv = new InventoryService(c);
        var nos = inv.CreateSimpleUnit("Nos", "Numbers", unitQuantityCode: "NOS");
        var item = inv.CreateStockItem("Tutorial Kit", inv.CreateStockGroup("Kits").Id, nos.Id);
        var main = c.MainLocation!.Id;
        inv.AddOpeningBalance(item.Id, main, 10m, Money.FromRupees(100m));
        new LedgerService(c).Post(new Voucher(Guid.NewGuid(),
            c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Sales).Id, D, new[]
            {
                new EntryLine(cash.Id, new Money(1_234.57m), DrCr.Debit),
                new EntryLine(ledger.Id, new Money(1_234.57m), DrCr.Credit),
            }, number: 1,
            inventoryLines: new[] { new VoucherInventoryLine(item.Id, main, 1m, new Money(1_234.57m)) }));

        AssertCmp08(Reload(c), expectedBase, expectedCgst);
    }

    // ================================================================ fixtures

    private static void AssertCmp08(Company c, long expectedBase, long expectedCgst)
    {
        using var cmp = JsonDocument.Parse(GstReturnJson.Cmp08(c, FyFrom, Q1To));
        Assert.Equal(expectedBase, cmp.RootElement.GetProperty("turnover_base_paisa").GetInt64());
        Assert.Equal(expectedCgst, cmp.RootElement.GetProperty("tbl3i_out_cgst_paisa").GetInt64());
    }

    private static Company CompositionBook(bool groupExempt, out Domain.Ledger ledger, out Domain.Ledger cash)
    {
        var c = CompanyFactory.CreateSeeded("As Per Group Composition Co", FyFrom);
        new GstService(c).EnableGst(new GstConfig
        {
            HomeStateCode = "27", Gstin = Gstin, RegistrationType = GstRegistrationType.Composition,
            CompositionSubType = CompositionSubType.Trader, ApplicableFrom = FyFrom,
            Periodicity = GstReturnPeriodicity.Quarterly,
        });
        var sales = c.FindGroupByName("Sales Accounts")!;
        if (groupExempt)
            sales.Gst = new MasterGstDetails { Taxability = GstTaxability.Exempt, HsnSac = "999293" };
        ledger = new Domain.Ledger(Guid.NewGuid(), "Coaching Income", sales.Id, Money.Zero, false)
        { SalesPurchaseGst = ScreenDefaultBlock() };
        c.AddLedger(ledger);
        cash = c.Ledgers.First(l => l.Name == "Cash");
        return c;
    }

    private static Company RcmBook(bool ledgerTicked)
    {
        var c = CompanyFactory.CreateSeeded("As Per Group RCM Co", FyFrom);
        var gst = new GstService(c);
        gst.EnableGst(new GstConfig
        {
            HomeStateCode = "27", Gstin = Gstin, RegistrationType = GstRegistrationType.Regular,
            ApplicableFrom = FyFrom, Periodicity = GstReturnPeriodicity.Monthly,
        });
        gst.SeedAdvancedGst();   // the notified reverse-charge categories
        var inv = new InventoryService(c);
        var nos = inv.CreateSimpleUnit("Nos", "Numbers", unitQuantityCode: "NOS");
        var item = inv.CreateStockItem("Server Rack", inv.CreateStockGroup("Racks").Id, nos.Id);
        item.Gst = new StockItemGstDetails
        {
            HsnSac = "847130", Taxability = GstTaxability.Taxable, RateBasisPoints = 1800,
            SupplyType = GstSupplyType.Goods, ReverseChargeApplicable = true,
            RcmCategoryId = c.Gst!.RcmCategories.First(x => x.SupplyNature == "Capital-goods").Id,
        };
        var purchases = new Domain.Ledger(Guid.NewGuid(), "Purchases", c.FindGroupByName("Purchase Accounts")!.Id,
            Money.Zero, true);
        if (ledgerTicked) purchases.SalesPurchaseGst = ScreenDefaultBlock();
        c.AddLedger(purchases);
        c.AddLedger(new Domain.Ledger(Guid.NewGuid(), "Promoter Supplier", c.FindGroupByName("Sundry Creditors")!.Id,
            Money.Zero, false)
        {
            PartyGst = new PartyGstDetails
            { RegistrationType = GstRegistrationType.Unregistered, StateCode = "27", IsPromoter = true },
        });
        return c;
    }

    private static Company Reload(Company original)
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"apex-as-per-group-block-{Guid.NewGuid():N}.db");
        try
        {
            using (var write = new SqliteCompanyStore(dbPath))
                write.Save(original);
            using var read = new SqliteCompanyStore(dbPath);
            return read.Load(original.Id)!;
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            try { File.Delete(dbPath); } catch (IOException) { }
        }
    }
}
