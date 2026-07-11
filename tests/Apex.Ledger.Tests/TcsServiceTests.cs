using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Reports;
using Apex.Ledger.Services;
using Xunit;
using Domain = Apex.Ledger.Domain;

namespace Apex.Ledger.Tests;

/// <summary>
/// Phase 7 slice 5 — the TCS <b>compute + auto-collect</b> engine (<see cref="TcsService"/>). Pure, deterministic,
/// paisa/rupee-exact. Proves the LOAD-BEARING contract: TCS is <b>additive</b> (collected on top, the mirror of GST,
/// NOT the TDS carve-out). On a Sales voucher the collector books <c>Dr Party = value + GST + TCS</c>, <c>Cr Sales</c>,
/// <c>Cr Output GST</c> (unchanged Phase-4 engine), <c>Cr "TCS Payable"</c> (Duties &amp; Taxes) — so the sale still
/// balances and the item-invoice pairing (Sales credit == Σ item value) foots unchanged (TCS Payable excluded like
/// the GST tax ledgers), with <b>no double-count</b> between GSTR-1 and 27EQ. Also: GOODS-DRIVEN detection (the §206C
/// nature comes from the STOCK ITEM / sales ledger, not the party); the party drives PAN/rate (PAN ⇒ with-PAN;
/// no-PAN ⇒ §206CC 2×/5%, EXCEPT 206C(1H) capped at 1%); base per the nature's base-incl-GST flag (Circular 17/2020);
/// nearest-rupee round-half-up; the §206C(1H) ₹50-lakh cumulative-FY projection; and the 206C(1H) legacy year-gate.
/// </summary>
public class TcsServiceTests
{
    private const string ValidTan = "MUMA12345B";
    private const string BuyerPan = "AAQCS1234K";

    // Home 27 (Maharashtra); intra-state buyer so a sale computes CGST+SGST like the Phase-4 golden.
    private const string GstinMaharashtra = "27AAPFU0939F1ZV";

    private static readonly DateOnly Fy = new(2025, 4, 1);
    private static readonly DateOnly D1 = new(2025, 5, 10);

    private static Company NewTcsCompany()
    {
        var c = CompanyFactory.CreateSeeded("Collecting Co", Fy);
        new TdsTcsService(c).EnableTcs(new TcsConfig { Tan = ValidTan });
        new GstService(c).EnableGst(new GstConfig
        {
            HomeStateCode = "27", Gstin = GstinMaharashtra, RegistrationType = GstRegistrationType.Regular,
            ApplicableFrom = Fy, Periodicity = GstReturnPeriodicity.Monthly,
        });
        return c;
    }

    private static Domain.Ledger AddLedger(Company c, string name, string groupName, bool openingIsDebit)
    {
        var l = new Domain.Ledger(Guid.NewGuid(), name, c.FindGroupByName(groupName)!.Id, Money.Zero, openingIsDebit);
        c.AddLedger(l);
        return l;
    }

    private static Domain.Ledger Buyer(Company c, string? pan)
    {
        var b = AddLedger(c, $"Buyer-{Guid.NewGuid():N}", "Sundry Debtors", true);
        b.PartyGst = new PartyGstDetails { RegistrationType = GstRegistrationType.Regular, Gstin = GstinMaharashtra, StateCode = "27" };
        b.TcsApplicable = true; b.CollecteeType = CollecteeType.Individual; b.PartyPan = pan;
        return b;
    }

    // ---- the golden worked example: scrap ₹1,00,000 + 18% GST → TCS 1% of the GST-inclusive base = ₹1,180 ----

    [Fact]
    public void Golden_scrap_sale_collects_one_percent_of_the_gst_inclusive_base_additively()
    {
        var c = NewTcsCompany();
        var gst = new GstService(c);
        var post = new LedgerService(c);
        var inv = new InventoryService(c);

        var scrap = inv.CreateStockItem("Scrap Metal", inv.CreateStockGroup("Waste").Id, inv.CreateSimpleUnit("Kg", "Kilogram").Id);
        scrap.Gst = new StockItemGstDetails { Taxability = GstTaxability.Taxable, RateBasisPoints = 1800 };
        scrap.TcsNatureOfGoodsId = c.FindNatureOfGoodsByCode("6CE")!.Id; // goods-driven nature
        var main = c.MainLocation!.Id;

        var sales = AddLedger(c, "Scrap Sales", "Sales Accounts", false);
        var buyer = Buyer(c, BuyerPan);

        // Buy first so there is stock to sell.
        var purchases = AddLedger(c, "Purchases", "Purchase Accounts", true);
        var creditor = AddLedger(c, "Creditor", "Sundry Creditors", false);
        post.Post(new Voucher(Guid.NewGuid(), c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Purchase).Id, D1,
            new[] { new EntryLine(purchases.Id, Money.FromRupees(50_000m), DrCr.Debit), new EntryLine(creditor.Id, Money.FromRupees(50_000m), DrCr.Credit) },
            inventoryLines: new[] { new VoucherInventoryLine(scrap.Id, main, 1000m, Money.FromRupees(50m)) }));

        // Sale: 1000 Kg @ ₹100 = ₹1,00,000 taxable; 18% GST = ₹18,000 (CGST 9,000 + SGST 9,000).
        var value = Money.FromRupees(1_00_000m);
        var interState = gst.IsInterState(buyer.PartyGst!.StateCode);
        var tax = gst.ComputeInvoiceTax(new[] { new GstService.TaxableLine(value, 1800) }, interState, GstTaxDirection.Output);
        Assert.Equal(Money.FromRupees(18_000m), tax.TotalTax);

        // Goods-driven: the nature comes from the ITEM (party only drives PAN/rate).
        var nature = new TcsService(c).ResolveNature(scrap, sales)!;
        Assert.Equal("6CE", nature.CollectionCode);

        var col = new TcsService(c).BuildCollection(value, tax.TotalTax, nature, buyer, D1);
        Assert.True(col.Applies);
        Assert.True(col.Collection.PanApplied);
        Assert.Equal(100, col.Collection.RateBasisPoints);                    // 1% with PAN
        Assert.Equal(Money.FromRupees(1_18_000m), col.Collection.AssessableValue); // base INCLUDES GST
        Assert.Equal(Money.FromRupees(1_180m), col.TcsAmount);                // 1% of 1,18,000

        // Additive posting: Dr Buyer value+GST+TCS / Cr Sales value / Cr Output GST / Cr TCS Payable.
        var partyTotal = value + tax.TotalTax + col.TcsAmount; // 1,19,180
        Assert.Equal(Money.FromRupees(1_19_180m), partyTotal);

        var lines = new List<EntryLine>
        {
            new(buyer.Id, partyTotal, DrCr.Debit),
            new(sales.Id, value, DrCr.Credit),
        };
        lines.AddRange(tax.TaxLines);           // Cr Output CGST/SGST (unchanged Phase-4 engine)
        lines.Add(col.TcsPayableLine!);         // Cr TCS Payable (additive)

        var v = post.Post(new Voucher(Guid.NewGuid(), c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Sales).Id, D1, lines,
            inventoryLines: new[] { new VoucherInventoryLine(scrap.Id, main, 1000m, Money.FromRupees(100m)) }));

        // The additive total foots and VoucherValidator accepts.
        Assert.True(VoucherValidator.IsBalanced(v));
        Assert.Equal(Money.FromRupees(1_19_180m), v.TotalDebit);
        Assert.Equal(v.TotalDebit, v.TotalCredit);

        // GSTR-1 unchanged — no double-count: the Sales (stock) leg == Σ item value == ₹1,00,000 (TCS excluded, like GST).
        Assert.Equal(Money.FromRupees(1_00_000m), v.InventoryLinesValue);
        Assert.Equal(-9_000m, LedgerBalances.SignedClosing(c, gst.FindTaxLedger(GstTaxHead.Central, GstTaxDirection.Output)!, D1));
        Assert.Equal(-9_000m, LedgerBalances.SignedClosing(c, gst.FindTaxLedger(GstTaxHead.State, GstTaxDirection.Output)!, D1));

        // TCS Payable is its OWN liability (credit) — a distinct head, never a sales/GST amount.
        var payable = new TcsService(c).RequirePayableLedger();
        Assert.True(ClassificationRules.IsDutiesAndTaxesLedger(payable, c));
        Assert.Equal(-1_180m, LedgerBalances.SignedClosing(c, payable, D1));

        // The collection detail rides the TCS Payable credit line (one assessable contribution for the projection).
        var tcsLine = Assert.Single(v.Lines, l => l.HasTcs);
        Assert.Equal("6CE", tcsLine.Tcs!.CollectionCode);
        Assert.Equal(buyer.Id, tcsLine.Tcs.CollecteeLedgerId);
    }

    // ---- no-PAN §206CC: scrap 1% with-PAN → 5% no-PAN (higher of 2×/5%) ----

    [Fact]
    public void No_pan_scrap_collects_five_percent_under_206CC()
    {
        var c = NewTcsCompany();
        var nature = c.FindNatureOfGoodsByCode("6CE")!;
        var buyer = Buyer(c, pan: null); // no PAN → §206CC higher of 2×1%=2% or 5% → 5%

        var col = new TcsService(c).Compute(Money.FromRupees(1_00_000m), Money.FromRupees(18_000m), nature, buyer, D1);
        Assert.False(col.PanApplied);
        Assert.Equal(500, col.RateBasisPoints);                 // 5%
        Assert.Equal(Money.FromRupees(5_900m), col.TcsAmount);  // 5% of 1,18,000
    }

    // ---- goods-driven detection: nature from the STOCK ITEM, else the SALES LEDGER; never the party ----

    [Fact]
    public void Nature_is_goods_driven_item_first_then_sales_ledger_never_the_party()
    {
        var c = NewTcsCompany();
        var svc = new TcsService(c);
        var scrapNature = c.FindNatureOfGoodsByCode("6CE")!;
        var liquorNature = c.FindNatureOfGoodsByCode("6CA")!;

        var inv = new InventoryService(c);
        var item = inv.CreateStockItem("Scrap", inv.CreateStockGroup("Waste").Id, inv.CreateSimpleUnit("Kg", "Kilogram").Id);
        var sales = AddLedger(c, "Sales", "Sales Accounts", false);

        // Neither carries a nature ⇒ non-TCS line.
        Assert.Null(svc.ResolveNature(item, sales));

        // Sales ledger carries a nature (and is applicable) ⇒ used when the item has none.
        sales.TcsApplicable = true; sales.TcsNatureOfGoodsId = liquorNature.Id;
        Assert.Equal("6CA", svc.ResolveNature(item, sales)!.CollectionCode);

        // The ITEM's nature wins over the sales ledger's (most-granular; the goods drive it).
        item.TcsNatureOfGoodsId = scrapNature.Id;
        Assert.Equal("6CE", svc.ResolveNature(item, sales)!.CollectionCode);

        // A sales ledger that carries a nature but is NOT marked applicable does not drive TCS.
        var plainSales = AddLedger(c, "Plain Sales", "Sales Accounts", false);
        plainSales.TcsNatureOfGoodsId = liquorNature.Id; // TcsApplicable stays false
        Assert.Null(svc.ResolveNature(inv.CreateStockItem("Plain", item.StockGroupId, item.BaseUnitId), plainSales));
    }

    // ---- base flag: a (hypothetical) GST-exclusive nature computes on value only ----

    [Fact]
    public void Base_excludes_gst_when_the_nature_flag_is_off()
    {
        var c = NewTcsCompany();
        var buyer = Buyer(c, BuyerPan);
        var exclusive = new NatureOfGoods(Guid.NewGuid(), "6CX", "Exclusive-base test", 100, 500, "6CX",
            baseIncludesGst: false);

        var col = new TcsService(c).Compute(Money.FromRupees(1_00_000m), Money.FromRupees(18_000m), exclusive, buyer, D1);
        Assert.Equal(Money.FromRupees(1_00_000m), col.AssessableValue);  // GST excluded
        Assert.Equal(Money.FromRupees(1_000m), col.TcsAmount);           // 1% of 1,00,000
    }

    // ---- §206C(1H) legacy year-gate: non-selectable on/after 01-Apr-2025, valid for FY 2024-25 ----

    [Fact]
    public void Legacy_206c_1h_is_non_selectable_on_or_after_the_fa2025_cutoff()
    {
        var c = NewTcsCompany();
        var legacy = c.FindNatureOfGoodsByCode("6CR")!;
        Assert.True(legacy.IsLegacy);

        Assert.False(legacy.IsSelectableOn(new DateOnly(2025, 4, 1)));  // off from the cutoff
        Assert.False(legacy.IsSelectableOn(new DateOnly(2026, 1, 1)));
        Assert.True(legacy.IsSelectableOn(new DateOnly(2025, 3, 31)));  // valid for FY 2024-25 historical returns

        // A non-legacy nature (scrap) is always selectable.
        Assert.True(c.FindNatureOfGoodsByCode("6CE")!.IsSelectableOn(new DateOnly(2025, 4, 1)));
    }

    // ---- §206C(1H) no-PAN cap = 1% (§206CC special proviso), and the ₹50-lakh cumulative-FY projection ----

    [Fact]
    public void Legacy_206c_1h_no_pan_is_capped_at_one_percent_and_uses_a_cumulative_fy_threshold()
    {
        // §206C(1H) is a FY 2024-25 (pre-FA2025-cutoff) scenario, so this company begins books in that year.
        var c = CompanyFactory.CreateSeeded("Legacy 1H Co", new DateOnly(2024, 4, 1));
        new TdsTcsService(c).EnableTcs(new TcsConfig { Tan = ValidTan });
        var post = new LedgerService(c);
        var legacy = c.FindNatureOfGoodsByCode("6CR")!; // ₹50-lakh cumulative; 0.1% with PAN, 1% no-PAN cap
        var svc = new TcsService(c);

        var buyer = Buyer(c, pan: null);
        var sales = AddLedger(c, "Goods Sales", "Sales Accounts", false);

        // First sale ₹40,00,000 (FY-to-date below ₹50 lakh) ⇒ no collection yet, but the assessment is recorded.
        var first = svc.BuildCollection(Money.FromRupees(40_00_000m), Money.Zero, legacy, buyer, new DateOnly(2024, 5, 1));
        Assert.False(first.Applies);
        Assert.Null(first.TcsPayableLine);
        // Ride the below-threshold detail on the party leg so the FY projection counts it (mirrors the TDS pattern).
        post.Post(new Voucher(Guid.NewGuid(), c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Journal).Id,
            new DateOnly(2024, 5, 1),
            new[]
            {
                new EntryLine(buyer.Id, Money.FromRupees(40_00_000m), DrCr.Debit, tcs: first.Detail),
                new EntryLine(sales.Id, Money.FromRupees(40_00_000m), DrCr.Credit),
            }));

        Assert.Equal(Money.FromRupees(40_00_000m), svc.ProjectPriorCumulative(buyer.Id, legacy.Id, new DateOnly(2024, 6, 1)));

        // Second sale ₹20,00,000 → FY aggregate ₹60 lakh > ₹50 lakh ⇒ collect on the EXCESS over ₹50 lakh only
        // (§206C(1H): "sale consideration exceeding fifty lakh rupees"), i.e. on ₹10,00,000; no-PAN cap = 1%.
        var second = svc.Compute(Money.FromRupees(20_00_000m), Money.Zero, legacy, buyer, new DateOnly(2024, 6, 1));
        Assert.True(second.Applies);
        Assert.Equal(100, second.RateBasisPoints);                    // 1% no-PAN cap (NOT 20%)
        Assert.Equal(Money.FromRupees(20_00_000m), second.AssessableValue); // FULL receipts recorded for the FY projection
        Assert.Equal(Money.FromRupees(10_000m), second.TcsAmount);    // 1% of the ₹10,00,000 that exceeds ₹50 lakh
    }

    [Fact]
    public void Legacy_206c_1h_charges_only_receipts_exceeding_the_fifty_lakh_threshold()
    {
        var c = CompanyFactory.CreateSeeded("Legacy 1H Excess Co", new DateOnly(2024, 4, 1));
        new TdsTcsService(c).EnableTcs(new TcsConfig { Tan = ValidTan });
        var post = new LedgerService(c);
        var legacy = c.FindNatureOfGoodsByCode("6CR")!; // ₹50-lakh cumulative; 0.1% with PAN
        var svc = new TcsService(c);

        var buyer = Buyer(c, BuyerPan);                 // PAN present ⇒ 0.1%
        var sales = AddLedger(c, "Goods Sales", "Sales Accounts", false);
        var journalType = c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Journal).Id;

        // Prior FY receipts ₹40,00,000 (below ₹50 lakh) — recorded, no collection.
        var first = svc.BuildCollection(Money.FromRupees(40_00_000m), Money.Zero, legacy, buyer, new DateOnly(2024, 5, 1));
        Assert.False(first.Applies);
        post.Post(new Voucher(Guid.NewGuid(), journalType, new DateOnly(2024, 5, 1), new[]
        {
            new EntryLine(buyer.Id, Money.FromRupees(40_00_000m), DrCr.Debit, tcs: first.Detail),
            new EntryLine(sales.Id, Money.FromRupees(40_00_000m), DrCr.Credit),
        }));

        // Straddling sale ₹20,00,000 → aggregate ₹60 lakh. Charge on the ₹10,00,000 excess only ⇒ 0.1% = ₹1,000
        // (NOT ₹2,000, which the full-base bug produced).
        var straddle = svc.Compute(Money.FromRupees(20_00_000m), Money.Zero, legacy, buyer, new DateOnly(2024, 6, 1));
        Assert.True(straddle.Applies);
        Assert.Equal(Money.FromRupees(1_000m), straddle.TcsAmount);
        // Record it so the running FY total reflects the full ₹60 lakh of receipts.
        var straddlePost = svc.BuildCollection(Money.FromRupees(20_00_000m), Money.Zero, legacy, buyer, new DateOnly(2024, 6, 1));
        post.Post(new Voucher(Guid.NewGuid(), journalType, new DateOnly(2024, 6, 1), new[]
        {
            new EntryLine(buyer.Id, Money.FromRupees(20_00_000m) + straddlePost.TcsAmount, DrCr.Debit, tcs: straddlePost.Detail),
            new EntryLine(sales.Id, Money.FromRupees(20_00_000m), DrCr.Credit),
            straddlePost.TcsPayableLine!,
        }));

        // A later sale ₹5,00,000, now wholly above ₹50 lakh ⇒ charged in full ⇒ 0.1% = ₹500.
        var above = svc.Compute(Money.FromRupees(5_00_000m), Money.Zero, legacy, buyer, new DateOnly(2024, 7, 1));
        Assert.True(above.Applies);
        Assert.Equal(Money.FromRupees(500m), above.TcsAmount);
    }

    // ---- nearest-rupee rounding (round-half-up), the income-tax rule ----

    [Fact]
    public void Tcs_rounds_to_the_nearest_rupee_half_up()
    {
        var c = NewTcsCompany();
        var nature = c.FindNatureOfGoodsByCode("6CE")!; // 1% with PAN
        var buyer = Buyer(c, BuyerPan);

        // Base ₹1,00,050 @ 1% = ₹1,000.50 ⇒ round-half-up = ₹1,001 (base excludes GST here: gst = 0).
        var col = new TcsService(c).Compute(Money.FromRupees(1_00_050m), Money.Zero, nature, buyer, D1);
        Assert.Equal(Money.FromRupees(1_001m), col.TcsAmount);
    }

    // ---- requires TCS enabled before building the collection legs ----

    [Fact]
    public void Requires_tcs_enabled_before_collecting()
    {
        var c = CompanyFactory.CreateSeeded("No-TCS Co", Fy); // TCS not enabled
        Assert.Throws<InvalidOperationException>(() => new TcsService(c).RequirePayableLedger());
    }

    // ---- ER-13: a plain sale with no TCS-applicable item/ledger carries no TCS detail ----

    [Fact]
    public void A_plain_voucher_carries_no_tcs_detail()
    {
        var c = NewTcsCompany();
        var a = AddLedger(c, "Rent", "Indirect Expenses", true);
        var b = AddLedger(c, "Cash Box", "Cash-in-Hand", true);
        var v = new LedgerService(c).Post(new Voucher(Guid.NewGuid(),
            c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Journal).Id, D1,
            new[] { new EntryLine(a.Id, Money.FromRupees(500m), DrCr.Debit), new EntryLine(b.Id, Money.FromRupees(500m), DrCr.Credit) }));
        Assert.All(v.Lines, l => Assert.False(l.HasTcs));
    }
}
