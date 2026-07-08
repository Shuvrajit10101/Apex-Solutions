using System;
using System.IO;
using System.Linq;
using Apex.Ledger.Domain;
using Apex.Desktop.Services;
using Apex.Desktop.ViewModels;

namespace Apex.Desktop.Tests;

/// <summary>
/// End-to-end coverage for the Phase-4 slice-4c GST UI surfaced in the cascade (catalog §12; RQ-1/RQ-7/RQ-8):
/// the F11 Features → GST config page enables GST (seeding slabs + auto-creating the six tax ledgers) and
/// persists across a reload; a bad GSTIN is a friendly message, not an enable; the Ledger master captures
/// party GST (GSTIN/registration/state) for a party ledger when GST is on and persists it to
/// <see cref="Ledger.PartyGst"/>; the Stock Item master captures HSN/rate/taxability into
/// <see cref="StockItem.Gst"/>; and every GST field is hidden/no-op on a GST-off company. Drives the real
/// shell view models over a throwaway .db — no UI toolkit.
/// </summary>
public sealed class GstConfigViewModelTests : IDisposable
{
    // Known-good GSTINs (same constants the engine tests use).
    private const string GstinMaharashtra = "27AAPFU0939F1ZV"; // state code 27
    private const string GstinKarnataka = "29AAGCB7383J1Z4";   // state code 29

    private readonly string _tempDir;
    private readonly CompanyStorage _storage;

    public GstConfigViewModelTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "ApexGstTests_" + Guid.NewGuid().ToString("N"));
        _storage = new CompanyStorage(_tempDir);
    }

    // ---------------------------------------------------------------- helpers

    private MainWindowViewModel NewSeededCompany(string name)
    {
        var vm = new MainWindowViewModel(_storage);
        vm.NewCompanyName = name;
        vm.CreateCompany();
        Assert.Equal(Screen.Gateway, vm.CurrentScreen);
        return vm;
    }

    private Company Reload(string companyName)
    {
        var entry = _storage.ListCompanies().Single(e => e.Name == companyName);
        return _storage.Load(entry);
    }

    /// <summary>Enables GST via the config page with the given GSTIN + home state, returns the page VM.</summary>
    private GstConfigViewModel EnableGst(MainWindowViewModel vm, string gstin)
    {
        vm.ShowGstConfig();
        Assert.Equal(Screen.GstConfig, vm.CurrentScreen);
        var page = vm.GstConfig!;
        page.GstEnabled = true;
        page.Gstin = gstin; // auto-fills the home state from the first two digits
        Assert.True(page.Apply());
        return page;
    }

    // ---------------------------------------------------------------- (1) F11 GST config: enable + persist

    [Fact]
    public void Enable_gst_creates_tax_ledgers_and_persists_across_reload()
    {
        const string companyName = "GST Enable Co";
        var vm = NewSeededCompany(companyName);

        Assert.False(vm.Company!.GstEnabled);

        // F11 opens the GST config page (button-bar "F11 Features").
        var f11 = vm.ButtonBar.Single(b => b.Key == "F11");
        Assert.True(f11.Enabled);
        f11.Action();
        Assert.Equal(Screen.GstConfig, vm.CurrentScreen);

        var page = vm.GstConfig!;
        page.GstEnabled = true;
        page.Gstin = GstinMaharashtra;
        // The home state auto-filled from the GSTIN's leading "27" (Maharashtra).
        Assert.Equal("27", page.HomeState?.Code);
        Assert.True(page.Apply());

        // Company GST is now on, with the 6 tax ledgers + slabs seeded by the engine.
        Assert.True(vm.Company.GstEnabled);
        Assert.Equal(GstinMaharashtra, vm.Company.Gst!.Gstin);
        Assert.Equal("27", vm.Company.Gst.HomeStateCode);
        Assert.Equal(4, vm.Company.Gst.RateSlabs.Count); // 0/5/18/40
        Assert.NotNull(vm.Company.FindLedgerByName("Output CGST"));
        Assert.NotNull(vm.Company.FindLedgerByName("Input IGST"));
        // The confirmation list surfaces the created tax ledgers (6 GST + Round Off).
        Assert.Contains(page.TaxLedgers, r => r.Name == "Output SGST");
        Assert.True(page.TaxLedgers.Count >= 6);

        // PERSISTED: reload and GST config + tax ledgers survive.
        var reloaded = Reload(companyName);
        Assert.True(reloaded.GstEnabled);
        Assert.Equal(GstinMaharashtra, reloaded.Gst!.Gstin);
        Assert.Equal("27", reloaded.Gst.HomeStateCode);
        Assert.NotNull(reloaded.FindLedgerByName("Output CGST"));
    }

    [Fact]
    public void Invalid_gstin_shows_message_and_does_not_enable()
    {
        var vm = NewSeededCompany("Bad GSTIN Co");
        vm.ShowGstConfig();
        var page = vm.GstConfig!;

        page.GstEnabled = true;
        page.Gstin = "27AAPFU0939F1ZW"; // wrong check digit
        page.HomeState = page.HomeStates.First(s => s.Code == "27");
        Assert.False(page.Apply());

        Assert.NotNull(page.Message);
        Assert.Contains("not a valid", page.Message!);
        Assert.False(vm.Company!.GstEnabled);      // company stays GST-off
        Assert.False(page.GstEnabled);             // the toggle reverts to the real state
    }

    [Fact]
    public void Missing_home_state_blocks_enable_with_a_message()
    {
        var vm = NewSeededCompany("No Home State Co");
        vm.ShowGstConfig();
        var page = vm.GstConfig!;

        page.GstEnabled = true;
        // Set a GSTIN whose leading digits are NOT a state code path we auto-fill by clearing the state after.
        page.Gstin = GstinMaharashtra;
        page.HomeState = null; // simulate the user clearing it
        Assert.False(page.Apply());

        Assert.NotNull(page.Message);
        Assert.Contains("Home State", page.Message!);
        Assert.False(vm.Company!.GstEnabled);
    }

    [Fact]
    public void Disabling_gst_turns_it_off_and_persists()
    {
        const string companyName = "GST Toggle Off Co";
        var vm = NewSeededCompany(companyName);
        var page = EnableGst(vm, GstinMaharashtra);
        Assert.True(vm.Company!.GstEnabled);

        // Turn it off.
        page.GstEnabled = false;
        Assert.True(page.Apply());
        Assert.False(vm.Company.GstEnabled);

        // PERSISTED: reload and GST is off (config retained but disabled).
        var reloaded = Reload(companyName);
        Assert.False(reloaded.GstEnabled);
    }

    // ---------------------------------------------------------------- (2) party GST on the Ledger master

    [Fact]
    public void Party_ledger_captures_gst_details_and_persists()
    {
        const string companyName = "Party GST Co";
        var vm = NewSeededCompany(companyName);
        EnableGst(vm, GstinMaharashtra);

        vm.ShowLedgerMaster();
        Assert.Equal(Screen.LedgerMaster, vm.CurrentScreen);
        var m = vm.LedgerMaster!;
        Assert.True(m.GstEnabled);

        // A Sundry Debtors ledger is a party group → the party-GST sub-form is shown.
        m.SelectedGroup = m.Groups.First(g => g.Name == "Sundry Debtors");
        Assert.True(m.ShowPartyGst);

        m.Name = "Acme Traders";
        m.PartyGstin = GstinKarnataka; // a Karnataka (inter-state) party
        m.PartyRegistrationType = m.PartyRegistrationTypes.First(o => o.Value == GstRegistrationType.Regular);
        m.PartyState = m.PartyStates.First(s => s.Code == "29");
        Assert.True(m.Create());

        var led = vm.Company!.FindLedgerByName("Acme Traders")!;
        Assert.NotNull(led.PartyGst);
        Assert.Equal(GstinKarnataka, led.PartyGst!.Gstin);
        Assert.Equal("29", led.PartyGst.StateCode);
        Assert.Equal(GstRegistrationType.Regular, led.PartyGst.RegistrationType);

        // PERSISTED across a reload.
        var reloaded = Reload(companyName);
        var rLed = reloaded.FindLedgerByName("Acme Traders")!;
        Assert.NotNull(rLed.PartyGst);
        Assert.Equal(GstinKarnataka, rLed.PartyGst!.Gstin);
        Assert.Equal("29", rLed.PartyGst.StateCode);
    }

    [Fact]
    public void Invalid_party_gstin_shows_message_and_does_not_create()
    {
        var vm = NewSeededCompany("Bad Party GSTIN Co");
        EnableGst(vm, GstinMaharashtra);

        vm.ShowLedgerMaster();
        var m = vm.LedgerMaster!;
        m.SelectedGroup = m.Groups.First(g => g.Name == "Sundry Creditors");
        m.Name = "Bad Party";
        m.PartyGstin = "29AAGCB7383J1ZZ"; // wrong check digit
        m.PartyRegistrationType = m.PartyRegistrationTypes.First(o => o.Value == GstRegistrationType.Regular);
        Assert.False(m.Create());

        Assert.NotNull(m.Message);
        Assert.Contains("not a valid party GSTIN", m.Message!);
        Assert.Null(vm.Company!.FindLedgerByName("Bad Party"));
    }

    [Fact]
    public void Party_gst_fields_are_hidden_when_gst_is_off()
    {
        var vm = NewSeededCompany("GST Off Ledger Co");
        vm.ShowLedgerMaster();
        var m = vm.LedgerMaster!;
        Assert.False(m.GstEnabled);

        m.SelectedGroup = m.Groups.First(g => g.Name == "Sundry Debtors");
        Assert.False(m.ShowPartyGst); // hidden when GST is off, even for a party group

        // Creating still works and captures no party GST.
        m.Name = "Plain Party";
        Assert.True(m.Create());
        Assert.Null(vm.Company!.FindLedgerByName("Plain Party")!.PartyGst);
    }

    // ---------------------------------------------------------------- (3) stock-item GST on the Stock Item master

    [Fact]
    public void Stock_item_captures_hsn_rate_taxability_and_persists()
    {
        const string companyName = "Item GST Co";
        var vm = NewSeededCompany(companyName);
        EnableGst(vm, GstinMaharashtra);

        var group = CreateStockGroup(vm, "Finished Goods");
        var unit = CreateUnit(vm, "Nos", "Numbers");

        vm.ShowStockItemMaster();
        var m = vm.StockItemMaster!;
        Assert.True(m.GstEnabled);
        m.Name = "Widget-X";
        m.SelectedGroup = m.Groups.Single(g => g.Id == group);
        m.SelectedUnit = m.Units.Single(u => u.Id == unit);
        m.HsnSacCode = "8471";
        m.Taxability = m.Taxabilities.First(o => o.Value == GstTaxability.Taxable);
        m.GstRate = m.GstRates.First(o => o.RateBasisPoints == 1800); // 18%
        Assert.True(m.Create());

        var item = vm.Company!.FindStockItemByName("Widget-X")!;
        Assert.NotNull(item.Gst);
        Assert.Equal("8471", item.Gst!.HsnSac);
        Assert.Equal(GstTaxability.Taxable, item.Gst.Taxability);
        Assert.Equal(1800, item.Gst.RateBasisPoints);

        // PERSISTED across a reload.
        var reloaded = Reload(companyName);
        var rItem = reloaded.FindStockItemByName("Widget-X")!;
        Assert.NotNull(rItem.Gst);
        Assert.Equal("8471", rItem.Gst!.HsnSac);
        Assert.Equal(1800, rItem.Gst.RateBasisPoints);
    }

    [Fact]
    public void Bad_hsn_length_shows_message_and_does_not_create()
    {
        var vm = NewSeededCompany("Bad HSN Co");
        EnableGst(vm, GstinMaharashtra);
        var group = CreateStockGroup(vm, "Finished Goods");
        var unit = CreateUnit(vm, "Nos", "Numbers");

        vm.ShowStockItemMaster();
        var m = vm.StockItemMaster!;
        m.Name = "Bad-HSN-Item";
        m.SelectedGroup = m.Groups.Single(g => g.Id == group);
        m.SelectedUnit = m.Units.Single(u => u.Id == unit);
        m.HsnSacCode = "847"; // 3 digits (invalid — must be 4/6/8)
        Assert.False(m.Create());

        Assert.NotNull(m.Message);
        Assert.Contains("4, 6 or 8 digits", m.Message!);
        Assert.Null(vm.Company!.FindStockItemByName("Bad-HSN-Item"));
    }

    [Fact]
    public void Non_taxable_item_carries_no_rate()
    {
        var vm = NewSeededCompany("Exempt Item Co");
        EnableGst(vm, GstinMaharashtra);
        var group = CreateStockGroup(vm, "Finished Goods");
        var unit = CreateUnit(vm, "Nos", "Numbers");

        vm.ShowStockItemMaster();
        var m = vm.StockItemMaster!;
        m.Name = "Exempt-Item";
        m.SelectedGroup = m.Groups.Single(g => g.Id == group);
        m.SelectedUnit = m.Units.Single(u => u.Id == unit);
        m.Taxability = m.Taxabilities.First(o => o.Value == GstTaxability.Exempt);
        m.GstRate = m.GstRates.First(o => o.RateBasisPoints == 1800); // even if a rate is picked…
        Assert.True(m.Create());

        var item = vm.Company!.FindStockItemByName("Exempt-Item")!;
        Assert.NotNull(item.Gst);
        Assert.Equal(GstTaxability.Exempt, item.Gst!.Taxability);
        Assert.Null(item.Gst.RateBasisPoints); // …an Exempt item carries no positive rate
    }

    [Fact]
    public void Stock_item_gst_fields_are_hidden_when_gst_is_off()
    {
        var vm = NewSeededCompany("GST Off Item Co");
        var group = CreateStockGroup(vm, "Finished Goods");
        var unit = CreateUnit(vm, "Nos", "Numbers");

        vm.ShowStockItemMaster();
        var m = vm.StockItemMaster!;
        Assert.False(m.GstEnabled);

        // Creating still works and attaches no GST block (the Phase-3 placeholders remain).
        m.Name = "Plain-Item";
        m.SelectedGroup = m.Groups.Single(g => g.Id == group);
        m.SelectedUnit = m.Units.Single(u => u.Id == unit);
        Assert.True(m.Create());
        Assert.Null(vm.Company!.FindStockItemByName("Plain-Item")!.Gst);
    }

    // ---------------------------------------------------------------- inventory-master helpers

    private static Guid CreateStockGroup(MainWindowViewModel vm, string name)
    {
        vm.ShowStockGroupMaster();
        var m = vm.StockGroupMaster!;
        m.Name = name;
        Assert.True(m.Create());
        return vm.Company!.FindStockGroupByName(name)!.Id;
    }

    private static Guid CreateUnit(MainWindowViewModel vm, string symbol, string formal)
    {
        vm.ShowUnitMaster();
        var m = vm.UnitMaster!;
        m.IsCompound = false;
        m.Symbol = symbol;
        m.FormalName = formal;
        m.DecimalPlacesText = "0";
        Assert.True(m.Create());
        return vm.Company!.FindUnitByName(symbol)!.Id;
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true); }
        catch { /* best-effort cleanup */ }
    }
}
