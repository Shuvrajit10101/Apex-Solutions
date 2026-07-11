using System;
using System.IO;
using System.Linq;
using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Reports;
using Apex.Ledger.Services;
using Apex.Desktop.Services;
using Apex.Desktop.ViewModels;
using Xunit;
using DomainLedger = Apex.Ledger.Domain.Ledger;

namespace Apex.Desktop.Tests;

/// <summary>
/// Phase 7 slice 5 — the TCS additive-collection <b>voucher-entry UI</b> (<see cref="VoucherEntryViewModel"/>) on a
/// Sales item invoice. Proves the screen surfaces the auto-computed collection via the SAME
/// <see cref="TcsService.BuildCollection"/> the posting uses (ER-4, no re-implementation), and that on Accept the
/// party debit rises by the collected TCS while a "TCS Payable" credit leg is appended — so
/// <c>Dr Party value+GST+TCS == Cr Sales + Cr Output GST + Cr TCS Payable</c> to the paisa and the engine accepts it,
/// with the Sales (stock) leg still at Σ item value (no GSTR-1 vs 27EQ double-count). Also proves: TCS is
/// <b>goods-driven</b> (the band shows only when the STOCK ITEM / sales ledger carries a §206C nature; the party drives
/// only PAN/rate + the collectee gate), the §206CC no-PAN higher rate flows through the UI, the §206C(1H) legacy nature
/// is not offered on/after 01-Apr-2025, and the ER-13 gate (no TCS-applicable item / TCS-off company ⇒ no band, sale
/// posts byte-identically). Drives the real shell + entry VM over a throwaway <c>.db</c> — no UI toolkit. All amounts
/// worked by hand and reconciled to the paisa.
/// </summary>
public sealed class TcsVoucherEntryViewModelTests : IDisposable
{
    private const string ValidTan = "MUMA12345B";
    private const string BuyerPan = "AAQCS1234K";
    private const string GstinMaharashtra = "27AAPFU0939F1ZV";

    private readonly string _tempDir;
    private readonly CompanyStorage _storage;

    public TcsVoucherEntryViewModelTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "ApexTcsVoucherTests_" + Guid.NewGuid().ToString("N"));
        _storage = new CompanyStorage(_tempDir);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true); }
        catch { /* best-effort cleanup */ }
    }

    // ---------------------------------------------------------------- scaffolding

    private sealed class Kit
    {
        public required MainWindowViewModel Vm { get; init; }
        public required Company Company { get; init; }
        public required Guid ScrapId { get; init; }       // taxable @ 18%, §206C nature 6CE (scrap)
        public required Guid PlainId { get; init; }        // taxable @ 18%, NO §206C nature (non-TCS)
        public required Guid MainGodownId { get; init; }
        public required Guid SalesLedgerId { get; init; }
        public required Guid BuyerId { get; init; }        // collectee, in-state (27)
    }

    private static DomainLedger AddLedger(Company c, string name, string groupName)
    {
        var group = c.FindGroupByName(groupName) ?? throw new InvalidOperationException($"No group '{groupName}'.");
        var ledger = new DomainLedger(Guid.NewGuid(), name, group.Id, Money.Zero, openingIsDebit: false);
        c.AddLedger(ledger);
        return ledger;
    }

    /// <summary>
    /// A GST- and TCS-enabled (home Maharashtra 27) company with two 18% items — Scrap (§206C 6CE) and a Plain item
    /// (no §206C nature) — opening stock, a Sales ledger and one in-state collectee buyer (PAN optional; the PARTY
    /// drives only the rate). Books begin in <paramref name="fyStart"/> so the default entry date lands in that FY.
    /// </summary>
    private Kit NewTcsKit(string companyName, string? buyerPan = BuyerPan, DateOnly? fyStart = null)
    {
        var fy = fyStart ?? new DateOnly(2025, 4, 1);
        var vm = new MainWindowViewModel(_storage);
        vm.NewCompanyName = companyName;
        vm.CreateCompany();

        var c = vm.Company!;
        c.FinancialYearStart = fy;
        c.BooksBeginFrom = fy;

        new GstService(c).EnableGst(new GstConfig
        {
            HomeStateCode = "27", Gstin = GstinMaharashtra, RegistrationType = GstRegistrationType.Regular,
            ApplicableFrom = fy, Periodicity = GstReturnPeriodicity.Monthly,
        });
        new TdsTcsService(c).EnableTcs(new TcsConfig { Tan = ValidTan });

        var inv = new InventoryService(c);
        var grp = inv.CreateStockGroup("Goods");
        var kg = inv.CreateSimpleUnit("Kg", "Kilogram", unitQuantityCode: "KGS");
        var main = c.MainLocation!.Id;

        var scrap = inv.CreateStockItem("Scrap Metal", grp.Id, kg.Id);
        scrap.Gst = new StockItemGstDetails { HsnSac = "720449", Taxability = GstTaxability.Taxable, RateBasisPoints = 1800 };
        scrap.TcsNatureOfGoodsId = c.FindNatureOfGoodsByCode("6CE")!.Id; // goods-driven §206C nature

        var plain = inv.CreateStockItem("Plain Widget", grp.Id, kg.Id);
        plain.Gst = new StockItemGstDetails { HsnSac = "847130", Taxability = GstTaxability.Taxable, RateBasisPoints = 1800 };
        // No TcsNatureOfGoodsId ⇒ a non-TCS good.

        inv.AddOpeningBalance(scrap.Id, main, 5000m, Money.FromRupees(50m));
        inv.AddOpeningBalance(plain.Id, main, 5000m, Money.FromRupees(50m));

        var sales = AddLedger(c, "Scrap Sales", "Sales Accounts");

        var buyer = AddLedger(c, "Industrial Buyer", "Sundry Debtors");
        buyer.PartyGst = new PartyGstDetails { RegistrationType = GstRegistrationType.Regular, Gstin = GstinMaharashtra, StateCode = "27" };
        buyer.TcsApplicable = true;
        buyer.CollecteeType = CollecteeType.Individual; // a collectee (the party gate)
        buyer.PartyPan = buyerPan;

        _storage.Save(c);

        return new Kit
        {
            Vm = vm, Company = c, ScrapId = scrap.Id, PlainId = plain.Id,
            MainGodownId = main, SalesLedgerId = sales.Id, BuyerId = buyer.Id,
        };
    }

    private static VoucherEntryViewModel OpenSalesInvoice(Kit k)
    {
        k.Vm.OpenVoucher(VoucherBaseType.Sales);
        var entry = k.Vm.VoucherEntry!;
        k.Vm.ToggleItemInvoice();
        Assert.True(entry.IsItemInvoice);
        entry.SelectedParty = entry.Parties.Single(p => p.Ledger?.Id == k.BuyerId);
        entry.SelectedStockLedger = k.Company.FindLedger(k.SalesLedgerId);
        return entry;
    }

    private static void FillItemLine(VoucherEntryViewModel entry, Guid itemId, Guid godownId, decimal qty, string rate, int index = 0)
    {
        while (entry.InventoryLines.Count <= index) entry.AddInventoryLine();
        var line = entry.InventoryLines[index];
        line.SelectedItem = entry.StockItems.Single(i => i.Id == itemId);
        line.SelectedGodown = entry.Godowns.Single(g => g.Id == godownId);
        line.QuantityText = qty.ToString(System.Globalization.CultureInfo.InvariantCulture);
        line.RateText = rate;
    }

    private static decimal Signed(Company c, Guid ledgerId, DateOnly asOf) =>
        LedgerBalances.SignedClosing(c, c.FindLedger(ledgerId)!, asOf);

    private static DateOnly AsOf(Company c) => c.FinancialYearStart.AddYears(1).AddDays(-1);

    // ================================================================ (1) golden: scrap sale collects 1% of the GST-inclusive base, additively

    [Fact]
    public void Golden_scrap_sale_shows_and_posts_additive_tcs()
    {
        var k = NewTcsKit("TCS Scrap Co");
        var entry = OpenSalesInvoice(k);

        // 1000 Kg @ ₹100 = ₹1,00,000 taxable @ 18% intra ⇒ CGST 9,000 + SGST 9,000 (GST 18,000).
        // TCS 6CE 1% of the GST-INCLUSIVE base (1,18,000) = ₹1,180. Party total = 1,00,000 + 18,000 + 1,180.
        FillItemLine(entry, k.ScrapId, k.MainGodownId, 1000m, "100.00");

        Assert.True(entry.IsTcsSalesInvoice);
        Assert.True(entry.ShowTcs);
        Assert.Equal("6CE", entry.TcsCollectionCodeText);
        Assert.Equal("1%", entry.TcsRateText);
        Assert.Equal("1,180.00", entry.TcsAmountText);
        Assert.Equal("9,000.00", entry.GstCgstText);
        Assert.Equal("9,000.00", entry.GstSgstText);
        Assert.Equal("1,19,180.00", entry.PartyTotalText);   // taxable + GST + TCS (collected on top)

        Assert.True(entry.CanAccept);
        Assert.True(entry.Accept());

        var c = k.Vm.Company!;
        var asOf = AsOf(c);
        var type = c.VoucherTypes.Single(t => t.BaseType == VoucherBaseType.Sales && t.IsActive);
        var posted = c.Vouchers.Single(v => v.TypeId == type.Id);

        Assert.True(VoucherValidator.IsBalanced(posted));
        // Sales (stock) leg stays at Σ item value — no double-count with 27EQ.
        Assert.Equal(1_00_000m, posted.InventoryLinesValue.Amount);
        Assert.Equal(-1_00_000m, Signed(c, k.SalesLedgerId, asOf));      // Cr Sales (taxable only)
        Assert.Equal(1_19_180m, Signed(c, k.BuyerId, asOf));            // Dr Buyer (taxable + GST + TCS)

        // TCS Payable is its own Duties & Taxes liability (credit) carrying the collection detail.
        var payable = new TcsService(c).RequirePayableLedger();
        Assert.True(ClassificationRules.IsDutiesAndTaxesLedger(payable, c));
        Assert.Equal(-1_180m, Signed(c, payable.Id, asOf));
        var tcsLine = Assert.Single(posted.Lines, l => l.HasTcs);
        Assert.Equal("6CE", tcsLine.Tcs!.CollectionCode);
        Assert.Equal(k.BuyerId, tcsLine.Tcs.CollecteeLedgerId);
        Assert.True(tcsLine.Tcs.PanApplied);
    }

    // ================================================================ (2) ER-13: a non-TCS good shows no band and posts byte-identically

    [Fact]
    public void No_tcs_band_when_item_is_not_tcs_applicable_and_posts_byte_identical()
    {
        var k = NewTcsKit("TCS Plain Co");
        var entry = OpenSalesInvoice(k);

        // The Plain item carries NO §206C nature — a goods-driven non-TCS sale even though TCS is enabled + the buyer
        // is a collectee. No band; party total = taxable + GST only (no TCS collected on top).
        FillItemLine(entry, k.PlainId, k.MainGodownId, 1000m, "100.00");

        Assert.True(entry.IsTcsSalesInvoice);   // company is TCS-aware…
        Assert.False(entry.ShowTcs);            // …but the goods are not TCS-applicable ⇒ no band
        Assert.Equal("0.00", entry.TcsAmountText);
        Assert.Equal("1,18,000.00", entry.PartyTotalText); // taxable + GST only

        Assert.True(entry.Accept());

        var c = k.Vm.Company!;
        var type = c.VoucherTypes.Single(t => t.BaseType == VoucherBaseType.Sales && t.IsActive);
        var posted = c.Vouchers.Single(v => v.TypeId == type.Id);
        Assert.True(VoucherValidator.IsBalanced(posted));
        Assert.All(posted.Lines, l => Assert.False(l.HasTcs)); // no TCS leg / detail anywhere
        Assert.Equal(1_18_000m, Signed(c, k.BuyerId, AsOf(c)));  // Dr Buyer = taxable + GST, TCS excluded
    }

    // ================================================================ (3) band hidden on a TCS-off company (ER-13)

    [Fact]
    public void No_tcs_band_on_tcs_disabled_company()
    {
        // Same scrap shape, but the company never enabled TCS → not a TCS invoice, no band, byte-identical sale.
        var vm = new MainWindowViewModel(_storage);
        vm.NewCompanyName = "TCS-Off Co";
        vm.CreateCompany();
        var c = vm.Company!;
        c.FinancialYearStart = new DateOnly(2025, 4, 1);
        c.BooksBeginFrom = new DateOnly(2025, 4, 1);
        new GstService(c).EnableGst(new GstConfig
        {
            HomeStateCode = "27", Gstin = GstinMaharashtra, RegistrationType = GstRegistrationType.Regular,
            ApplicableFrom = c.FinancialYearStart, Periodicity = GstReturnPeriodicity.Monthly,
        });

        var inv = new InventoryService(c);
        var grp = inv.CreateStockGroup("Goods");
        var kg = inv.CreateSimpleUnit("Kg", "Kilogram", unitQuantityCode: "KGS");
        var main = c.MainLocation!.Id;
        var scrap = inv.CreateStockItem("Scrap Metal", grp.Id, kg.Id);
        scrap.Gst = new StockItemGstDetails { Taxability = GstTaxability.Taxable, RateBasisPoints = 1800 };
        inv.AddOpeningBalance(scrap.Id, main, 5000m, Money.FromRupees(50m));
        var sales = AddLedger(c, "Scrap Sales", "Sales Accounts");
        var buyer = AddLedger(c, "Industrial Buyer", "Sundry Debtors");
        buyer.PartyGst = new PartyGstDetails { RegistrationType = GstRegistrationType.Regular, Gstin = GstinMaharashtra, StateCode = "27" };

        vm.OpenVoucher(VoucherBaseType.Sales);
        var entry = vm.VoucherEntry!;
        vm.ToggleItemInvoice();
        entry.SelectedParty = entry.Parties.Single(p => p.Ledger?.Id == buyer.Id);
        entry.SelectedStockLedger = c.FindLedger(sales.Id);
        FillItemLine(entry, scrap.Id, main, 1000m, "100.00");

        Assert.False(entry.IsTcsSalesInvoice);
        Assert.False(entry.ShowTcs);
        Assert.Equal("1,18,000.00", entry.PartyTotalText); // taxable + GST only

        Assert.True(entry.Accept());
        var type = c.VoucherTypes.Single(t => t.BaseType == VoucherBaseType.Sales && t.IsActive);
        var posted = c.Vouchers.Single(v => v.TypeId == type.Id);
        Assert.All(posted.Lines, l => Assert.False(l.HasTcs));
    }

    // ================================================================ (4) band hidden when the party is not a collectee

    [Fact]
    public void No_tcs_band_when_party_is_not_a_collectee()
    {
        var k = NewTcsKit("TCS Non-Collectee Co");
        // Strip the collectee status from the buyer — the party gate fails even though the goods are §206C.
        k.Company.FindLedger(k.BuyerId)!.CollecteeType = null;

        var entry = OpenSalesInvoice(k);
        FillItemLine(entry, k.ScrapId, k.MainGodownId, 1000m, "100.00");

        Assert.True(entry.IsTcsSalesInvoice);
        Assert.False(entry.ShowTcs);
        Assert.Equal("1,18,000.00", entry.PartyTotalText); // no TCS collected on top
    }

    // ================================================================ (5) no-PAN §206CC higher rate flows through the UI

    [Fact]
    public void No_pan_scrap_uses_the_higher_206cc_rate_in_the_ui()
    {
        var k = NewTcsKit("TCS No-PAN Co", buyerPan: null); // no PAN ⇒ §206CC higher of 2×1%=2% or 5% ⇒ 5%
        var entry = OpenSalesInvoice(k);
        FillItemLine(entry, k.ScrapId, k.MainGodownId, 1000m, "100.00");

        Assert.True(entry.ShowTcs);
        Assert.Equal("6CE", entry.TcsCollectionCodeText);
        Assert.Equal("5% (No PAN)", entry.TcsRateText);
        Assert.Equal("5,900.00", entry.TcsAmountText);             // 5% of 1,18,000
        Assert.Equal("1,23,900.00", entry.PartyTotalText);         // 1,00,000 + 18,000 + 5,900

        Assert.True(entry.Accept());
        var c = k.Vm.Company!;
        var payable = new TcsService(c).RequirePayableLedger();
        Assert.Equal(-5_900m, Signed(c, payable.Id, AsOf(c)));
        var posted = c.Vouchers.Single(v => v.TypeId == c.VoucherTypes.Single(t => t.BaseType == VoucherBaseType.Sales && t.IsActive).Id);
        Assert.False(Assert.Single(posted.Lines, l => l.HasTcs).Tcs!.PanApplied);
    }

    // ================================================================ (6) §206C(1H) legacy nature is not offered on/after 01-Apr-2025

    [Fact]
    public void Legacy_206c_1h_nature_is_not_offered_on_or_after_the_fa2025_cutoff()
    {
        // A company whose books begin on the FA2025 cutoff (default entry date = 01-Apr-2025). Point the scrap item's
        // nature at the legacy §206C(1H) (6CR) entry — it is year-gated OFF from the cutoff, so the goods-driven
        // resolution skips it and no TCS is offered (byte-identical sale).
        var k = NewTcsKit("TCS Legacy Co", fyStart: new DateOnly(2025, 4, 1));
        k.Company.FindStockItem(k.ScrapId)!.TcsNatureOfGoodsId = k.Company.FindNatureOfGoodsByCode("6CR")!.Id;

        var entry = OpenSalesInvoice(k);
        Assert.Equal(new DateOnly(2025, 4, 1), entry.Date); // on/after the cutoff
        FillItemLine(entry, k.ScrapId, k.MainGodownId, 1000m, "100.00");

        Assert.True(entry.IsTcsSalesInvoice);
        Assert.False(entry.ShowTcs);                       // legacy nature non-selectable ⇒ not offered
        Assert.Equal("1,18,000.00", entry.PartyTotalText); // taxable + GST only, no TCS

        Assert.True(entry.Accept());
        var c = k.Vm.Company!;
        var posted = c.Vouchers.Single(v => v.TypeId == c.VoucherTypes.Single(t => t.BaseType == VoucherBaseType.Sales && t.IsActive).Id);
        Assert.All(posted.Lines, l => Assert.False(l.HasTcs));
    }

    // ================================================================ (7) same legacy nature IS offered before the cutoff (FY 2024-25)

    [Fact]
    public void Legacy_206c_1h_nature_is_offered_before_the_cutoff()
    {
        // Books begin in FY 2024-25 (pre-cutoff), so the legacy 6CR nature is selectable. A ₹1,00,000 sale is below
        // the ₹50-lakh cumulative threshold ⇒ the band shows (assessed) but nothing is collected yet.
        var k = NewTcsKit("TCS Legacy Pre Co", fyStart: new DateOnly(2024, 4, 1));
        k.Company.FindStockItem(k.ScrapId)!.TcsNatureOfGoodsId = k.Company.FindNatureOfGoodsByCode("6CR")!.Id;

        var entry = OpenSalesInvoice(k);
        Assert.True(entry.Date < new DateOnly(2025, 4, 1)); // before the cutoff
        FillItemLine(entry, k.ScrapId, k.MainGodownId, 1000m, "100.00");

        Assert.True(entry.ShowTcs);                         // offered (selectable), assessed…
        Assert.Equal("6CR", entry.TcsCollectionCodeText);
        Assert.Equal("0.00", entry.TcsAmountText);          // …but below ₹50-lakh threshold ⇒ no collection
        Assert.Equal("1,18,000.00", entry.PartyTotalText);  // party total unchanged (nothing collected on top)

        Assert.True(entry.Accept());
        var c = k.Vm.Company!;
        var posted = c.Vouchers.Single(v => v.TypeId == c.VoucherTypes.Single(t => t.BaseType == VoucherBaseType.Sales && t.IsActive).Id);
        // The below-threshold assessment rides the party (Dr) leg so the §206C(1H) cumulative-FY projection stays exact.
        var partyLine = Assert.Single(posted.Lines, l => l.HasTcs);
        Assert.Equal("6CR", partyLine.Tcs!.CollectionCode);
        Assert.Equal(Money.Zero, partyLine.Tcs.TcsAmount);
    }

    // ================================================================ (8) editing the HEADER DATE across the FA2025 cutoff re-derives the band (ER-4: WYSIWYP)

    [Fact]
    public void Editing_the_header_date_across_the_cutoff_re_derives_the_tcs_band_to_match_posting()
    {
        // Pre-cutoff (FY 2024-25) legacy 6CR sale: the band shows. The date is a live input to the band
        // (NatureOfGoods.IsSelectableOn honours the FA2025 year-gate), so moving the header date onto/after
        // 01-Apr-2025 must flip the band OFF in lock-step with what Accept posts — what-you-see-is-what-you-post.
        var k = NewTcsKit("TCS Date-Edit Co", fyStart: new DateOnly(2024, 4, 1));
        k.Company.FindStockItem(k.ScrapId)!.TcsNatureOfGoodsId = k.Company.FindNatureOfGoodsByCode("6CR")!.Id;

        var entry = OpenSalesInvoice(k);
        Assert.True(entry.Date < new DateOnly(2025, 4, 1));
        FillItemLine(entry, k.ScrapId, k.MainGodownId, 1000m, "100.00");
        Assert.True(entry.ShowTcs);                          // band shown pre-cutoff
        Assert.Equal("6CR", entry.TcsCollectionCodeText);

        // Edit the header date TextBox across the FA2025 cutoff (the exact user gesture).
        entry.DateText = "01-Apr-2025";
        Assert.Equal(new DateOnly(2025, 4, 1), entry.Date);

        // Display must have re-derived: the legacy nature is now non-selectable ⇒ no band, no TCS in the party total.
        Assert.False(entry.ShowTcs);
        Assert.Equal("0.00", entry.TcsAmountText);
        Assert.Equal("1,18,000.00", entry.PartyTotalText);   // taxable + GST only, TCS dropped

        // …and Accept posts exactly that — no TCS line anywhere (display == posting).
        Assert.True(entry.Accept());
        var c = k.Vm.Company!;
        var posted = c.Vouchers.Single(v => v.TypeId == c.VoucherTypes.Single(t => t.BaseType == VoucherBaseType.Sales && t.IsActive).Id);
        Assert.All(posted.Lines, l => Assert.False(l.HasTcs));
    }
}
