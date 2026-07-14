using System;
using System.IO;
using System.Linq;
using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Reports;
using Apex.Ledger.Services;
using Apex.Desktop.Services;
using Apex.Desktop.ViewModels;
using DomainLedger = Apex.Ledger.Domain.Ledger;

namespace Apex.Desktop.Tests;

/// <summary>
/// Phase 9 slice 1 UI coverage — (A) the <b>GST Rate Setup</b> bulk screen (<see cref="GstRateSetupViewModel"/>),
/// (B) the stock-item cess/RSP master fields (<see cref="StockItemMasterViewModel"/>), and (C) <b>voucher-date
/// threading</b> in <see cref="VoucherEntryViewModel"/>: a car (HSN 8703) sale BEFORE 22-Sep-2025 resolves the
/// legacy 28% rate + 22% Compensation Cess, while one ON/AFTER resolves the GST 2.0 40% rate + zero cess — proving
/// the date-aware <c>ResolveRate</c>/<c>ResolveCess</c> overloads flow through the UI. All amounts worked by hand
/// and reconciled to the paisa. Drives the real shell over a throwaway <c>.db</c> — no UI toolkit.
/// </summary>
public sealed class GstDatedRateUiViewModelTests : IDisposable
{
    private const string GstinMaharashtra = "27AAPFU0939F1ZV";
    private static readonly DateOnly FyStart = new(2025, 4, 1);
    private static readonly DateOnly BeforeCutover = new(2025, 9, 20);
    private static readonly DateOnly AfterCutover = new(2025, 9, 25);

    private readonly string _tempDir;
    private readonly CompanyStorage _storage;

    public GstDatedRateUiViewModelTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "ApexGstDatedUiTests_" + Guid.NewGuid().ToString("N"));
        _storage = new CompanyStorage(_tempDir);
    }

    private sealed class Kit
    {
        public required MainWindowViewModel Vm { get; init; }
        public required string CompanyName { get; init; }
        public required Guid CarId { get; init; }
        public required Guid MainGodownId { get; init; }
        public required Guid SalesLedgerId { get; init; }
        public required Guid LocalCustomerId { get; init; }
    }

    /// <summary>A GST-enabled (home MH 27) company with the GST 2.0 advanced dated framework seeded and a Car
    /// stock item (HSN 8703, base 40% scalar) with opening stock, a Sales ledger and an in-state B2B customer.</summary>
    private Kit NewAdvancedGstKit(string companyName)
    {
        var vm = new MainWindowViewModel(_storage);
        vm.NewCompanyName = companyName;
        vm.CreateCompany();

        var c = vm.Company!;
        c.FinancialYearStart = FyStart;
        c.BooksBeginFrom = FyStart;

        var gst = new GstService(c);
        gst.EnableGst(new GstConfig
        {
            HomeStateCode = "27",
            Gstin = GstinMaharashtra,
            RegistrationType = GstRegistrationType.Regular,
            ApplicableFrom = FyStart,
            Periodicity = GstReturnPeriodicity.Monthly,
        });
        gst.SeedAdvancedGst(); // the advanced-GST opt-in — dated rate history + the three cess windows

        var inv = new InventoryService(c);
        var grp = inv.CreateStockGroup("Vehicles");
        var nos = inv.CreateSimpleUnit("Nos", "Numbers", unitQuantityCode: "NOS");
        var main = c.MainLocation!.Id;

        var car = inv.CreateStockItem("Car", grp.Id, nos.Id);
        // A base scalar rate keeps the line taxable; the dated 8703 history rows then override it by voucher date.
        car.Gst = new StockItemGstDetails { HsnSac = "8703", Taxability = GstTaxability.Taxable, RateBasisPoints = 4000 };
        inv.AddOpeningBalance(car.Id, main, 10m, Money.FromRupees(500000m));

        var sales = AddLedger(c, "Sales", "Sales Accounts");
        var customer = AddLedger(c, "Local Customer", "Sundry Debtors");
        customer.PartyGst = new PartyGstDetails { RegistrationType = GstRegistrationType.Regular, Gstin = GstinMaharashtra, StateCode = "27" };

        _storage.Save(c);

        return new Kit
        {
            Vm = vm, CompanyName = companyName, CarId = car.Id, MainGodownId = main,
            SalesLedgerId = sales.Id, LocalCustomerId = customer.Id,
        };
    }

    private static DomainLedger AddLedger(Company c, string name, string groupName)
    {
        var group = c.FindGroupByName(groupName) ?? throw new InvalidOperationException($"No group '{groupName}'.");
        var ledger = new DomainLedger(Guid.NewGuid(), name, group.Id, Money.Zero, openingIsDebit: false);
        c.AddLedger(ledger);
        return ledger;
    }

    private static DateOnly AsOf(Company c) => c.FinancialYearStart.AddYears(1).AddDays(-1);

    private static decimal Signed(Company c, Guid ledgerId, DateOnly asOf) =>
        LedgerBalances.SignedClosing(c, c.FindLedger(ledgerId)!, asOf);

    private static Guid TaxLedgerId(Company c, GstTaxHead head, GstTaxDirection dir) =>
        new GstService(c).FindTaxLedger(head, dir)!.Id;

    private static void SelectParty(VoucherEntryViewModel entry, Guid partyId) =>
        entry.SelectedParty = entry.Parties.Single(p => p.Ledger?.Id == partyId);

    private static void FillCarLine(VoucherEntryViewModel entry, Kit k, decimal qty, string rate)
    {
        while (entry.InventoryLines.Count <= 0) entry.AddInventoryLine();
        var line = entry.InventoryLines[0];
        line.SelectedItem = entry.StockItems.Single(i => i.Id == k.CarId);
        line.SelectedGodown = entry.Godowns.Single(g => g.Id == k.MainGodownId);
        line.QuantityText = qty.ToString(System.Globalization.CultureInfo.InvariantCulture);
        line.RateText = rate;
    }

    // ================================================================ (C) voucher-date threading

    [Fact]
    public void Car_sale_before_22_sep_2025_resolves_legacy_28pct_and_22pct_cess()
    {
        var k = NewAdvancedGstKit("GST Pre-Cutover Co");
        k.Vm.OpenVoucher(VoucherBaseType.Sales);
        var entry = k.Vm.VoucherEntry!;
        k.Vm.ToggleItemInvoice();
        Assert.True(entry.IsGstInvoice);

        entry.Date = BeforeCutover;                     // set the voucher date BEFORE filling the line
        SelectParty(entry, k.LocalCustomerId);
        FillCarLine(entry, k, 1m, "1000000.00");        // ₹10,00,000 taxable

        // 28% intra ⇒ CGST 1,40,000 + SGST 1,40,000; cess 22% ad-valorem ⇒ 2,20,000; party = 15,00,000.
        Assert.Equal("1,40,000.00", entry.GstCgstText);
        Assert.Equal("1,40,000.00", entry.GstSgstText);
        Assert.NotEqual("0.00", entry.GstCessText);     // cess resolves (window (a) still open on 20-Sep)
        Assert.Equal("15,00,000.00", entry.PartyTotalText);
        Assert.True(entry.Accept());

        var c = k.Vm.Company!;
        var asOf = AsOf(c);
        Assert.Equal(-140000m, Signed(c, TaxLedgerId(c, GstTaxHead.Central, GstTaxDirection.Output), asOf));
        Assert.Equal(-140000m, Signed(c, TaxLedgerId(c, GstTaxHead.State, GstTaxDirection.Output), asOf));
        Assert.Equal(-220000m, Signed(c, TaxLedgerId(c, GstTaxHead.Cess, GstTaxDirection.Output), asOf));
        Assert.Equal(1500000m, Signed(c, k.LocalCustomerId, asOf));   // party = taxable + tax + cess
        Assert.Equal(-1000000m, Signed(c, k.SalesLedgerId, asOf));    // pairing: stock leg = Σ taxable
        var posted = c.Vouchers.Single(v => v.PartyId == k.LocalCustomerId);
        Assert.True(VoucherValidator.IsBalanced(posted));
    }

    [Fact]
    public void Car_sale_on_or_after_22_sep_2025_resolves_gst2_40pct_and_zero_cess()
    {
        var k = NewAdvancedGstKit("GST Post-Cutover Co");
        k.Vm.OpenVoucher(VoucherBaseType.Sales);
        var entry = k.Vm.VoucherEntry!;
        k.Vm.ToggleItemInvoice();

        entry.Date = AfterCutover;
        SelectParty(entry, k.LocalCustomerId);
        FillCarLine(entry, k, 1m, "1000000.00");

        // 40% intra ⇒ CGST 2,00,000 + SGST 2,00,000; car cess window ended 21-Sep ⇒ cess 0; party = 14,00,000.
        Assert.Equal("2,00,000.00", entry.GstCgstText);
        Assert.Equal("2,00,000.00", entry.GstSgstText);
        Assert.Equal("0.00", entry.GstCessText);
        Assert.Equal("14,00,000.00", entry.PartyTotalText);
        Assert.True(entry.Accept());

        var c = k.Vm.Company!;
        var asOf = AsOf(c);
        Assert.Equal(-200000m, Signed(c, TaxLedgerId(c, GstTaxHead.Central, GstTaxDirection.Output), asOf));
        Assert.Equal(-200000m, Signed(c, TaxLedgerId(c, GstTaxHead.State, GstTaxDirection.Output), asOf));
        Assert.Equal(0m, Signed(c, TaxLedgerId(c, GstTaxHead.Cess, GstTaxDirection.Output), asOf));
        Assert.Equal(1400000m, Signed(c, k.LocalCustomerId, asOf));
        var posted = c.Vouchers.Single(v => v.PartyId == k.LocalCustomerId);
        Assert.True(VoucherValidator.IsBalanced(posted));
    }

    // ================================================================ (A) GST Rate Setup bulk screen

    [Fact]
    public void Rate_setup_seeds_gst2_defaults_and_lists_dated_rate_and_cess_windows()
    {
        // Enable GST but do NOT seed advanced data — the screen offers the "seed defaults" action.
        var vm = new MainWindowViewModel(_storage);
        vm.NewCompanyName = "Rate Setup Co";
        vm.CreateCompany();
        var c = vm.Company!;
        new GstService(c).EnableGst(new GstConfig
        {
            HomeStateCode = "27", Gstin = GstinMaharashtra, RegistrationType = GstRegistrationType.Regular,
        });
        _storage.Save(c);

        var setup = new GstRateSetupViewModel(c, _storage, onChanged: () => { });
        Assert.True(setup.GstEnabled);
        Assert.True(setup.CanSeedDefaults);
        Assert.Empty(setup.RateHistoryRows);

        Assert.True(setup.SeedDefaults());
        Assert.False(setup.CanSeedDefaults);              // now seeded
        Assert.True(setup.AdvancedGstSeeded);
        Assert.NotEmpty(setup.RateHistoryRows);           // dated windows now listed
        Assert.NotEmpty(setup.CessRows);
        Assert.Contains(setup.RateHistoryRows, r => r.Hsn == "8703");
        Assert.Contains(setup.CessRows, r => r.Hsn == "8703");
    }

    [Fact]
    public void Rate_setup_appends_a_new_dated_rate_window_and_persists()
    {
        var k = NewAdvancedGstKit("Rate Append Co");
        var c = k.Vm.Company!;
        var before = c.Gst!.RateHistory.Count;

        var setup = new GstRateSetupViewModel(c, _storage, onChanged: () => { });
        setup.NewRateHsn = "8528";
        setup.NewRatePercentText = "18";
        setup.NewRateFromText = "22-Sep-2025";
        setup.NewRateLabel = "TV 18% (GST 2.0)";
        Assert.True(setup.AddRateHistory());

        Assert.Equal(before + 1, c.Gst!.RateHistory.Count);
        var added = c.Gst!.RateHistory.Single(h => h.HsnSac == "8528");
        Assert.Equal(1800, added.RateBasisPoints);
        Assert.Equal(new DateOnly(2025, 9, 22), added.EffectiveFrom);
        Assert.Null(added.EffectiveTo);                    // blank "to" ⇒ open-ended

        // Persisted end-to-end.
        var entry = _storage.ListCompanies().Single(e => e.Name == "Rate Append Co");
        var reloaded = _storage.Load(entry);
        Assert.Contains(reloaded.Gst!.RateHistory, h => h.HsnSac == "8528" && h.RateBasisPoints == 1800);
    }

    [Fact]
    public void Rate_setup_appends_a_specific_cess_window_and_persists()
    {
        var k = NewAdvancedGstKit("Cess Append Co");
        var c = k.Vm.Company!;

        var setup = new GstRateSetupViewModel(c, _storage, onChanged: () => { });
        setup.NewCessHsn = "2710";
        setup.NewCessMode = setup.CessModes.Single(m => m.Value == CessValuationMode.Specific);
        setup.NewCessPerUnitText = "400.00";
        setup.NewCessFromText = "01-Apr-2025";
        setup.NewCessLabel = "Fuel cess ₹400/unit";
        Assert.True(setup.AddCess());

        var added = c.Gst!.CessRates.Single(r => r.HsnSac == "2710");
        Assert.Equal(CessValuationMode.Specific, added.ValuationMode);
        Assert.Equal(400m, added.CessPerUnit.Amount);

        var entry = _storage.ListCompanies().Single(e => e.Name == "Cess Append Co");
        var reloaded = _storage.Load(entry);
        Assert.Contains(reloaded.Gst!.CessRates, r => r.HsnSac == "2710" && r.CessPerUnit.Amount == 400m);
    }

    [Fact]
    public void Rate_setup_seed_is_a_no_op_when_gst_is_off()
    {
        var vm = new MainWindowViewModel(_storage);
        vm.NewCompanyName = "No GST Co";
        vm.CreateCompany();
        var c = vm.Company!;
        Assert.False(c.GstEnabled);

        var setup = new GstRateSetupViewModel(c, _storage, onChanged: () => { });
        Assert.False(setup.GstEnabled);
        Assert.False(setup.CanSeedDefaults);
        Assert.False(setup.SeedDefaults());               // surfaces a message, does nothing
        Assert.False(string.IsNullOrWhiteSpace(setup.Message));
    }

    // ================================================================ (B) stock-item cess/RSP master fields

    [Fact]
    public void Stock_item_master_persists_cess_and_rsp_override_fields()
    {
        var k = NewAdvancedGstKit("Item Cess Co");
        var c = k.Vm.Company!;

        var master = new StockItemMasterViewModel(c, _storage, onChanged: () => { });
        Assert.True(master.GstEnabled);
        master.Name = "Pan Masala";
        master.SelectedGroup = master.Groups.First();
        master.SelectedUnit = master.Units.First();
        master.HsnSacCode = "21069020";
        master.Taxability = master.Taxabilities.First(); // Taxable
        master.ValuationIsRsp = true;
        master.RetailSalePriceText = "100.00";
        master.CessApplicable = true;
        master.CessMode = master.CessValuationModes.Single(m => m.Mode == CessValuationMode.RetailSalePriceFactor);
        master.CessRspFactorText = "0.32";
        Assert.True(master.Create());

        var item = c.StockItems.Single(i => i.Name == "Pan Masala");
        Assert.NotNull(item.Gst);
        Assert.Equal(GstValuationBasis.RetailSalePrice, item.Gst!.ValuationBasis);
        Assert.Equal(100m, item.Gst!.RetailSalePrice!.Value.Amount);
        Assert.True(item.Gst!.CessApplicable);
        Assert.Equal(CessValuationMode.RetailSalePriceFactor, item.Gst!.CessValuationMode);
        Assert.Equal(320, item.Gst!.CessRspFactorMillis);

        // Persisted end-to-end.
        var entry = _storage.ListCompanies().Single(e => e.Name == "Item Cess Co");
        var reloaded = _storage.Load(entry);
        var r = reloaded.StockItems.Single(i => i.Name == "Pan Masala");
        Assert.Equal(320, r.Gst!.CessRspFactorMillis);
        Assert.Equal(100m, r.Gst!.RetailSalePrice!.Value.Amount);
    }

    // ================================================================ (D) A10 review fixes (Phase 9 slice 1 UI)

    /// <summary>A GST-2.0-seeded company with a Chewing-Tobacco item (HSN 2403 — a seeded RSP-factor cess HSN) that
    /// carries NO Retail Sale Price, plus a Sales ledger and an in-state (non-collectee) customer. The item is
    /// created via the engine directly (bypassing the master's new up-front guard) to mimic a landmine item
    /// persisted by an earlier build or an import — exactly what finding #1 concerns.</summary>
    private Kit NewRspFactorCessLandmineKit(string companyName)
    {
        var vm = new MainWindowViewModel(_storage);
        vm.NewCompanyName = companyName;
        vm.CreateCompany();

        var c = vm.Company!;
        c.FinancialYearStart = FyStart;
        c.BooksBeginFrom = FyStart;

        var gst = new GstService(c);
        gst.EnableGst(new GstConfig
        {
            HomeStateCode = "27", Gstin = GstinMaharashtra, RegistrationType = GstRegistrationType.Regular,
            ApplicableFrom = FyStart, Periodicity = GstReturnPeriodicity.Monthly,
        });
        gst.SeedAdvancedGst();

        var inv = new InventoryService(c);
        var grp = inv.CreateStockGroup("Tobacco");
        var nos = inv.CreateSimpleUnit("Nos", "Numbers", unitQuantityCode: "NOS");
        var main = c.MainLocation!.Id;

        var item = inv.CreateStockItem("Chewing Tobacco", grp.Id, nos.Id);
        // HSN 2403 is a seeded RSP-factor cess HSN; a base scalar rate keeps the line taxable. NO RetailSalePrice ⇒
        // the cess resolver fails fast the moment the line is priced within the cess window.
        item.Gst = new StockItemGstDetails { HsnSac = "2403", Taxability = GstTaxability.Taxable, RateBasisPoints = 2800 };
        inv.AddOpeningBalance(item.Id, main, 100m, Money.FromRupees(50m));

        var sales = AddLedger(c, "Sales", "Sales Accounts");
        var customer = AddLedger(c, "Local Customer", "Sundry Debtors");
        customer.PartyGst = new PartyGstDetails { RegistrationType = GstRegistrationType.Regular, Gstin = GstinMaharashtra, StateCode = "27" };

        _storage.Save(c);

        return new Kit
        {
            Vm = vm, CompanyName = companyName, CarId = item.Id, MainGodownId = main,
            SalesLedgerId = sales.Id, LocalCustomerId = customer.Id,
        };
    }

    [Fact]
    public void Live_recalc_on_rsp_factor_cess_item_with_no_rsp_surfaces_message_and_does_not_throw()
    {
        // Finding #1: the LIVE item-invoice recalc (each line-property set → RecalculateItemInvoice) must NOT let the
        // RSP-factor cess fail-fast propagate out of the property-change handler and break the voucher screen.
        var k = NewRspFactorCessLandmineKit("Cess Landmine Co");
        k.Vm.OpenVoucher(VoucherBaseType.Sales);
        var entry = k.Vm.VoucherEntry!;
        k.Vm.ToggleItemInvoice();
        Assert.True(entry.IsGstInvoice);

        entry.Date = BeforeCutover;             // 20-Sep-2025 — inside the 2403 cess window (01-Apr-2025 … 31-Jan-2026)
        SelectParty(entry, k.LocalCustomerId);

        // Pre-fix: filling the line threw (the RSP-factor cess resolver's InvalidOperationException escaped the
        // property setter). Post-fix: it is caught, so this completes without throwing.
        var ex = Record.Exception(() => FillCarLine(entry, k, 1m, "1000.00"));
        Assert.Null(ex);

        // The friendly fail-fast message is surfaced live, and the tax/cess display is cleared.
        Assert.False(string.IsNullOrWhiteSpace(entry.Message));
        Assert.Contains("Retail Sale Price", entry.Message);
        Assert.Equal("0.00", entry.GstCgstText);
        Assert.Equal("0.00", entry.GstCessText);

        // Accept re-runs the SAME compute and blocks the post with the same fail-fast message (never a silent ₹0).
        Assert.False(entry.Accept());
        Assert.Contains("Retail Sale Price", entry.Message);
    }

    [Fact]
    public void Stock_item_master_rejects_taxable_rsp_factor_cess_item_with_no_rsp()
    {
        // Finding #3: an item whose HSN attracts RSP-factor cess but carries no RSP is an unsellable landmine — the
        // master must reject it up front (EnsureValid alone does not, as the item declares no explicit cess override).
        var k = NewAdvancedGstKit("Cess Landmine Master Co");
        var c = k.Vm.Company!;

        var master = new StockItemMasterViewModel(c, _storage, onChanged: () => { });
        Assert.True(master.GstEnabled);
        master.Name = "Chewing Tobacco";
        master.SelectedGroup = master.Groups.First();
        master.SelectedUnit = master.Units.First();
        master.HsnSacCode = "2403";                       // a seeded RSP-factor cess HSN
        master.Taxability = master.Taxabilities.First();  // Taxable
        // No RetailSalePrice, no explicit cess override.

        Assert.False(master.Create());
        Assert.Contains("Retail Sale Price", master.Message);
        Assert.DoesNotContain(c.StockItems, i => i.Name == "Chewing Tobacco");   // not persisted

        // Control: declaring a Retail Sale Price lets the SAME item persist.
        master.RetailSalePriceText = "100.00";
        Assert.True(master.Create());
        Assert.Contains(c.StockItems, i => i.Name == "Chewing Tobacco");
    }

    [Fact]
    public void Stock_item_master_rejects_rsp_valuation_basis_with_no_rsp()
    {
        // Finding #4 (UI path): "valuation is RSP" with a blank Retail Sale Price is rejected up front (HSN 9999 is a
        // non-cess HSN, isolating the RSP-basis guard from the finding #3 cess-landmine guard).
        var k = NewAdvancedGstKit("RSP No-Price Co");
        var c = k.Vm.Company!;

        var master = new StockItemMasterViewModel(c, _storage, onChanged: () => { });
        master.Name = "RSP Widget";
        master.SelectedGroup = master.Groups.First();
        master.SelectedUnit = master.Units.First();
        master.HsnSacCode = "9999";                       // non-cess HSN
        master.Taxability = master.Taxabilities.First();  // Taxable
        master.ValuationIsRsp = true;                      // RSP valuation basis…
        // …but no RetailSalePrice.

        Assert.False(master.Create());
        Assert.Contains("Retail Sale Price", master.Message);
        Assert.DoesNotContain(c.StockItems, i => i.Name == "RSP Widget");
    }

    [Fact]
    public void Rate_setup_flags_an_overlapping_cess_window_but_still_adds_it()
    {
        // Finding #5: an overlapping same-HSN window is deterministic (most-recent effective-from wins), so it is NOT
        // blocked — but the user gets an informational note. Seeded: 8703 car cess 22% (01-Apr-2025 … 21-Sep-2025).
        var k = NewAdvancedGstKit("Cess Overlap Co");
        var c = k.Vm.Company!;
        var before = c.Gst!.CessRates.Count(r => r.HsnSac == "8703");

        var setup = new GstRateSetupViewModel(c, _storage, onChanged: () => { });
        setup.NewCessHsn = "8703";
        setup.NewCessMode = setup.CessModes.Single(m => m.Value == CessValuationMode.AdValorem);
        setup.NewCessRatePercentText = "18";
        setup.NewCessFromText = "01-Jul-2025";            // inside the existing 01-Apr … 21-Sep window ⇒ overlaps

        Assert.True(setup.AddCess());                      // still added (never blocked)
        Assert.Contains("overlaps", setup.Message);
        Assert.Equal(before + 1, c.Gst!.CessRates.Count(r => r.HsnSac == "8703"));
    }

    [Fact]
    public void Rate_setup_non_overlapping_rate_window_has_no_overlap_note()
    {
        // Finding #5 (negative control): a window on a fresh HSN overlaps nothing ⇒ no informational note.
        var k = NewAdvancedGstKit("Rate No-Overlap Co");
        var c = k.Vm.Company!;

        var setup = new GstRateSetupViewModel(c, _storage, onChanged: () => { });
        setup.NewRateHsn = "9988";                         // an HSN with no existing dated window
        setup.NewRatePercentText = "18";
        setup.NewRateFromText = "22-Sep-2025";

        Assert.True(setup.AddRateHistory());
        Assert.DoesNotContain("overlaps", setup.Message ?? string.Empty);
    }

    public void Dispose()
    {
        try
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true);
        }
        catch (IOException) { /* best effort */ }
    }
}
