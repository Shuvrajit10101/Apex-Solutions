using System;
using System.IO;
using System.Linq;
using Apex.Ledger.Domain;
using Apex.Desktop.Services;
using Apex.Desktop.ViewModels;

namespace Apex.Desktop.Tests;

/// <summary>
/// End-to-end coverage for the Phase-7 slice-1 TDS/TCS UI surfaced in the cascade (catalog §13; RQ P7-S1):
/// the F11 config enables TDS/TCS (seeding the Nature-of-Payment / Nature-of-Goods masters + auto-creating the
/// TDS/TCS Payable ledgers under Duties &amp; Taxes) and persists across a reload; a bad TAN/PAN is a friendly
/// message, not an enable; the two Statutory masters list the seeded predefined set and create customs; the
/// Ledger master captures ledger/party TDS/TCS applicability + PAN + deductee/collectee type; the Stock Item
/// master captures a TCS Nature of Goods; the Create-menu items are gated on the feature; and every TDS/TCS
/// field is hidden / no-op on a company that never enables them (ER-13). Drives the real shell view models over
/// a throwaway .db — no UI toolkit.
/// </summary>
public sealed class TdsTcsConfigViewModelTests : IDisposable
{
    private const string ValidTan = "MUMA12345B";   // 4 letters + 5 digits + 1 letter
    private const string ValidPan = "AAPFU0939F";   // 5 letters + 4 digits + 1 letter
    private const string ValidPan2 = "AAGCB7383J";

    private readonly string _tempDir;
    private readonly CompanyStorage _storage;

    public TdsTcsConfigViewModelTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "ApexTdsTcsTests_" + Guid.NewGuid().ToString("N"));
        _storage = new CompanyStorage(_tempDir);
    }

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

    /// <summary>Opens the F11 config page and enables TDS with a valid TAN; returns the page VM.</summary>
    private GstConfigViewModel EnableTds(MainWindowViewModel vm, string tan = ValidTan)
    {
        vm.ShowGstConfig();
        Assert.Equal(Screen.GstConfig, vm.CurrentScreen);
        var page = vm.GstConfig!;
        page.TdsEnabled = true;
        page.Tan = tan;
        Assert.True(page.ApplyTds());
        return page;
    }

    private GstConfigViewModel EnableTcs(MainWindowViewModel vm, string tan = ValidTan)
    {
        vm.ShowGstConfig();
        var page = vm.GstConfig!;
        page.TcsEnabled = true;
        page.Tan = tan;
        Assert.True(page.ApplyTcs());
        return page;
    }

    private static string[] CreateMenuLabels(MainWindowViewModel vm)
    {
        vm.ShowCreateMenu();
        return vm.Menu.Where(m => m.IsSelectable).Select(m => m.Label).ToArray();
    }

    // ============================================================ (1) F11: enable TDS/TCS + persist

    [Fact]
    public void Enable_tds_seeds_natures_creates_payable_ledger_and_persists()
    {
        const string companyName = "TDS Enable Co";
        var vm = NewSeededCompany(companyName);
        Assert.False(vm.Company!.TdsEnabled);

        // F11 opens the config page.
        var f11 = vm.ButtonBar.Single(b => b.Key == "F11");
        Assert.True(f11.Enabled);
        f11.Action();
        Assert.Equal(Screen.GstConfig, vm.CurrentScreen);

        var page = vm.GstConfig!;
        page.TdsEnabled = true;
        page.Tan = ValidTan;
        page.ResponsiblePersonName = "R. Bright";
        page.DeductorType = page.DeductorTypes.First(o => o.Value == DeductorType.Company);
        Assert.True(page.ApplyTds());

        // Company TDS is on, TAN captured, 8 predefined Nature-of-Payment masters seeded, payable ledger created.
        Assert.True(vm.Company.TdsEnabled);
        Assert.Equal(ValidTan, vm.Company.Tds!.Tan);
        Assert.Equal(8, vm.Company.NaturesOfPayment.Count);
        Assert.Contains(vm.Company.NaturesOfPayment, n => n.SectionCode == "194J(b)");
        Assert.NotNull(vm.Company.FindLedgerByName("TDS Payable"));
        Assert.Contains(page.TdsTcsLedgers, r => r.Name == "TDS Payable");

        // PERSISTED across reload.
        var reloaded = Reload(companyName);
        Assert.True(reloaded.TdsEnabled);
        Assert.Equal(ValidTan, reloaded.Tds!.Tan);
        Assert.Equal(8, reloaded.NaturesOfPayment.Count);
        Assert.NotNull(reloaded.FindLedgerByName("TDS Payable"));
    }

    [Fact]
    public void Enable_tcs_seeds_natures_creates_payable_ledger_and_persists()
    {
        const string companyName = "TCS Enable Co";
        var vm = NewSeededCompany(companyName);

        var page = EnableTcs(vm);
        Assert.True(vm.Company!.TcsEnabled);
        Assert.Equal(ValidTan, vm.Company.Tcs!.Tan);
        Assert.Equal(8, vm.Company.NaturesOfGoods.Count);
        Assert.Contains(vm.Company.NaturesOfGoods, n => n.CollectionCode == "6CE"); // scrap
        Assert.NotNull(vm.Company.FindLedgerByName("TCS Payable"));
        Assert.Contains(page.TdsTcsLedgers, r => r.Name == "TCS Payable");

        var reloaded = Reload(companyName);
        Assert.True(reloaded.TcsEnabled);
        Assert.Equal(8, reloaded.NaturesOfGoods.Count);
        Assert.NotNull(reloaded.FindLedgerByName("TCS Payable"));
    }

    [Fact]
    public void Missing_tan_blocks_enable_with_a_message_and_reverts_toggle()
    {
        var vm = NewSeededCompany("No TAN Co");
        vm.ShowGstConfig();
        var page = vm.GstConfig!;

        page.TdsEnabled = true;
        page.Tan = "";
        Assert.False(page.ApplyTds());

        Assert.NotNull(page.TdsMessage);
        Assert.Contains("TAN", page.TdsMessage!);
        Assert.False(vm.Company!.TdsEnabled);
        Assert.False(page.TdsEnabled); // toggle reverted to the real state
    }

    [Fact]
    public void Invalid_tan_blocks_enable_with_a_message()
    {
        var vm = NewSeededCompany("Bad TAN Co");
        vm.ShowGstConfig();
        var page = vm.GstConfig!;

        page.TcsEnabled = true;
        page.Tan = "MUM12345B"; // only 3 leading letters — invalid
        Assert.False(page.ApplyTcs());

        Assert.NotNull(page.TcsMessage);
        Assert.Contains("not a valid TAN", page.TcsMessage!);
        Assert.False(vm.Company!.TcsEnabled);
    }

    [Fact]
    public void Invalid_responsible_person_pan_blocks_enable()
    {
        var vm = NewSeededCompany("Bad PAN Co");
        vm.ShowGstConfig();
        var page = vm.GstConfig!;

        page.TdsEnabled = true;
        page.Tan = ValidTan;
        page.ResponsiblePersonPan = "AAPFU0939"; // 9 chars — invalid
        Assert.False(page.ApplyTds());

        Assert.NotNull(page.TdsMessage);
        Assert.Contains("not a valid PAN", page.TdsMessage!);
        Assert.False(vm.Company!.TdsEnabled);
    }

    [Fact]
    public void Disabling_tds_turns_it_off_and_persists()
    {
        const string companyName = "TDS Toggle Off Co";
        var vm = NewSeededCompany(companyName);
        var page = EnableTds(vm);
        Assert.True(vm.Company!.TdsEnabled);

        page.TdsEnabled = false;
        Assert.True(page.ApplyTds());
        Assert.False(vm.Company.TdsEnabled);

        // A disabled config is not rehydrated on load (the store gates on tds_enabled), so a reloaded
        // company reads as fully TDS-off — no config, no natures — exactly like a company that never enabled.
        var reloaded = Reload(companyName);
        Assert.False(reloaded.TdsEnabled);
        Assert.Null(reloaded.Tds);
        Assert.Empty(reloaded.NaturesOfPayment);

        // Re-enabling on that reloaded company re-seeds the 8 predefined masters cleanly (no duplication).
        new Apex.Ledger.Services.TdsTcsService(reloaded).EnableTds(new TdsConfig { Tan = ValidTan });
        Assert.Equal(8, reloaded.NaturesOfPayment.Count);
    }

    [Fact]
    public void Enabling_both_tds_and_tcs_shares_the_same_tan()
    {
        var vm = NewSeededCompany("Both Co");
        var page = EnableTds(vm);
        page.TcsEnabled = true;
        Assert.True(page.ApplyTcs());

        Assert.Equal(ValidTan, vm.Company!.Tds!.Tan);
        Assert.Equal(ValidTan, vm.Company.Tcs!.Tan);
    }

    [Fact]
    public void Enable_tds_toggle_is_committed_by_the_keyboard_accept_shortcut()
    {
        // Regression (Phase-7 slice-1 keyboard-fidelity finding): on the Statutory Configuration (F11) page the
        // keyboard accept (Ctrl+A / Enter, both → ActivateSelected) must commit the "Enable TDS" toggle, not only
        // the GST config. Before the fix ActivateSelected called GstConfig.Apply() (GST only), so a keyboard-driven
        // user who toggled Enable TDS and typed a TAN got no effect and had no key to commit it.
        const string companyName = "Kbd TDS Co";
        var vm = NewSeededCompany(companyName);
        vm.ShowGstConfig();
        var page = vm.GstConfig!;
        page.TdsEnabled = true;   // toggle reveals the TAN/deductor block
        page.Tan = ValidTan;

        vm.ActivateSelected();    // the F11-page keyboard accept (Ctrl+A / Enter)

        Assert.True(vm.Company!.TdsEnabled);
        Assert.Equal(ValidTan, vm.Company.Tds!.Tan);
        Assert.Equal(8, vm.Company.NaturesOfPayment.Count);
        Assert.NotNull(vm.Company.FindLedgerByName("TDS Payable"));

        // And it persists across a reload, exactly like the button path.
        var reloaded = Reload(companyName);
        Assert.True(reloaded.TdsEnabled);
        Assert.Equal(ValidTan, reloaded.Tds!.Tan);
    }

    [Fact]
    public void Enable_tcs_toggle_is_committed_by_the_keyboard_accept_shortcut()
    {
        var vm = NewSeededCompany("Kbd TCS Co");
        vm.ShowGstConfig();
        var page = vm.GstConfig!;
        page.TcsEnabled = true;
        page.Tan = ValidTan;

        vm.ActivateSelected();

        Assert.True(vm.Company!.TcsEnabled);
        Assert.Equal(ValidTan, vm.Company.Tcs!.Tan);
        Assert.Equal(8, vm.Company.NaturesOfGoods.Count);
        Assert.NotNull(vm.Company.FindLedgerByName("TCS Payable"));
    }

    [Fact]
    public void Keyboard_accept_leaves_a_neither_enabled_company_untouched()
    {
        // ER-13: the shared keyboard accept must not seed or persist anything for a company that toggles neither.
        var vm = NewSeededCompany("Kbd Neither Co");
        vm.ShowGstConfig();
        vm.ActivateSelected();

        Assert.False(vm.Company!.TdsEnabled);
        Assert.False(vm.Company.TcsEnabled);
        Assert.Empty(vm.Company.NaturesOfPayment);
        Assert.Empty(vm.Company.NaturesOfGoods);
        Assert.Null(vm.GstConfig!.TdsMessage);
        Assert.Null(vm.GstConfig.TcsMessage);
    }

    // ============================================================ (2) Nature masters: seeded + create + gating

    [Fact]
    public void Nature_of_payment_master_lists_the_seeded_set_and_creates_a_custom()
    {
        const string companyName = "NoP Co";
        var vm = NewSeededCompany(companyName);
        EnableTds(vm);

        vm.ShowNatureOfPaymentMaster();
        Assert.Equal(Screen.NatureOfPaymentMaster, vm.CurrentScreen);
        var m = vm.NatureOfPaymentMaster!;
        Assert.Equal(8, m.Natures.Count);
        Assert.Contains(m.Natures, r => r.SectionCode == "194Q" && r.Kind == "Predefined");

        // Create a custom nature.
        m.SectionCode = "194K";
        m.Name = "Income from units";
        m.RateWithPanText = "10";
        m.RateWithoutPanText = "20";
        m.FvuSectionCode = "94K";
        m.CumulativeThresholdText = "5000";
        Assert.True(m.Create());

        var nature = vm.Company!.NaturesOfPayment.Single(n => n.SectionCode == "194K");
        Assert.Equal(1000, nature.RateWithPanBp);   // 10%
        Assert.Equal(2000, nature.RateWithoutPanBp); // 20%
        Assert.False(nature.IsPredefined);
        Assert.Equal(5000m, nature.CumulativeThreshold!.Value.Amount);

        // Duplicate section code is rejected.
        m.SectionCode = "194Q";
        m.Name = "dup";
        m.RateWithPanText = "1";
        m.FvuSectionCode = "94Q";
        Assert.False(m.Create());
        Assert.Contains("already exists", m.Message!);

        // Persisted.
        var reloaded = Reload(companyName);
        Assert.Contains(reloaded.NaturesOfPayment, n => n.SectionCode == "194K");
    }

    [Fact]
    public void Nature_of_goods_master_lists_the_seeded_set_including_the_legacy_206c1h()
    {
        var vm = NewSeededCompany("NoG Co");
        EnableTcs(vm);

        vm.ShowNatureOfGoodsMaster();
        Assert.Equal(Screen.NatureOfGoodsMaster, vm.CurrentScreen);
        var m = vm.NatureOfGoodsMaster!;
        Assert.Equal(8, m.Natures.Count);
        // The legacy §206C(1H) sale-of-goods (6CR) is listed and flagged.
        var legacy = m.Natures.Single(r => r.CollectionCode == "6CR");
        Assert.Contains("legacy", legacy.Kind);

        // Create a custom nature.
        m.CollectionCode = "6CK";
        m.Name = "Custom goods";
        m.RateWithPanText = "0.75";
        m.RateWithoutPanText = "5";
        Assert.True(m.Create());
        var created = vm.Company!.NaturesOfGoods.Single(n => n.CollectionCode == "6CK");
        Assert.Equal(75, created.RateWithPanBp); // 0.75%
        Assert.False(created.IsLegacy);
    }

    [Fact]
    public void Create_menu_gates_the_statutory_masters_on_the_feature_flags()
    {
        var vm = NewSeededCompany("Gate Co");

        // Off by default — neither Statutory master surfaces.
        var off = CreateMenuLabels(vm);
        Assert.DoesNotContain("Nature of Payment", off);
        Assert.DoesNotContain("Nature of Goods", off);

        // Enable TDS only — Nature of Payment appears; Nature of Goods still hidden.
        EnableTds(vm);
        var tdsOn = CreateMenuLabels(vm);
        Assert.Contains("Nature of Payment", tdsOn);
        Assert.DoesNotContain("Nature of Goods", tdsOn);

        // Enable TCS too — both appear.
        EnableTcs(vm);
        var bothOn = CreateMenuLabels(vm);
        Assert.Contains("Nature of Payment", bothOn);
        Assert.Contains("Nature of Goods", bothOn);
    }

    [Fact]
    public void Show_nature_masters_are_no_ops_when_the_feature_is_off()
    {
        var vm = NewSeededCompany("No-Feature Co");
        vm.ShowNatureOfPaymentMaster();
        Assert.NotEqual(Screen.NatureOfPaymentMaster, vm.CurrentScreen);
        vm.ShowNatureOfGoodsMaster();
        Assert.NotEqual(Screen.NatureOfGoodsMaster, vm.CurrentScreen);
    }

    [Fact]
    public void Nature_master_create_is_reachable_via_activate_selected()
    {
        var vm = NewSeededCompany("Activate Co");
        EnableTds(vm);
        vm.ShowNatureOfPaymentMaster();
        var m = vm.NatureOfPaymentMaster!;
        m.SectionCode = "194N";
        m.Name = "Cash withdrawal";
        m.RateWithPanText = "2";
        m.FvuSectionCode = "94N";
        // Ctrl+A drives ActivateSelected → Create() on this screen.
        vm.ActivateSelected();
        Assert.Contains(vm.Company!.NaturesOfPayment, n => n.SectionCode == "194N");
    }

    // ============================================================ (3) Ledger master TDS/TCS fields

    [Fact]
    public void Ledger_tds_tcs_fields_are_hidden_when_the_features_are_off()
    {
        var vm = NewSeededCompany("Ledger Off Co");
        vm.ShowLedgerMaster();
        var m = vm.LedgerMaster!;
        Assert.False(m.TdsEnabled);
        Assert.False(m.TcsEnabled);
        Assert.False(m.ShowTdsTcs);

        m.SelectedGroup = m.Groups.First(g => g.Name == "Sundry Debtors");
        Assert.False(m.ShowPartyTdsTcs);

        // Creating still works and captures no TDS/TCS data.
        m.Name = "Plain Party";
        Assert.True(m.Create());
        var led = vm.Company!.FindLedgerByName("Plain Party")!;
        Assert.False(led.TdsApplicable);
        Assert.False(led.TcsApplicable);
        Assert.Null(led.PartyPan);
        Assert.Null(led.DeducteeType);
    }

    [Fact]
    public void Party_ledger_captures_deductee_type_pan_and_persists()
    {
        const string companyName = "Deductee Co";
        var vm = NewSeededCompany(companyName);
        EnableTds(vm);

        vm.ShowLedgerMaster();
        var m = vm.LedgerMaster!;
        Assert.True(m.TdsEnabled);
        m.SelectedGroup = m.Groups.First(g => g.Name == "Sundry Creditors");
        Assert.True(m.ShowPartyTdsTcs);

        m.Name = "Consultant A";
        m.SelectedDeducteeType = m.DeducteeTypeChoices.First(o => o.Value == DeducteeType.Individual);
        m.PartyPan = ValidPan;
        m.DeductTdsInSameVoucher = true;
        Assert.True(m.Create());

        var led = vm.Company!.FindLedgerByName("Consultant A")!;
        Assert.Equal(DeducteeType.Individual, led.DeducteeType);
        Assert.Equal(ValidPan, led.PartyPan);
        Assert.True(led.DeductTdsInSameVoucher);

        var reloaded = Reload(companyName);
        var rLed = reloaded.FindLedgerByName("Consultant A")!;
        Assert.Equal(DeducteeType.Individual, rLed.DeducteeType);
        Assert.Equal(ValidPan, rLed.PartyPan);
    }

    [Fact]
    public void Expense_ledger_captures_tds_applicable_and_default_nature()
    {
        var vm = NewSeededCompany("Expense TDS Co");
        EnableTds(vm);

        vm.ShowLedgerMaster();
        var m = vm.LedgerMaster!;
        m.SelectedGroup = m.Groups.First(g => g.Name == "Indirect Expenses");
        m.Name = "Professional Fees";
        m.TdsApplicable = true;
        var natureId = vm.Company!.NaturesOfPayment.First(n => n.SectionCode == "194J(b)").Id;
        m.SelectedTdsNature = m.TdsNatureChoices.First(c => c.NatureId == natureId);
        Assert.True(m.Create());

        var led = vm.Company.FindLedgerByName("Professional Fees")!;
        Assert.True(led.TdsApplicable);
        Assert.Equal(natureId, led.TdsNatureOfPaymentId);
    }

    [Fact]
    public void Sales_ledger_captures_tcs_applicable_and_default_nature()
    {
        var vm = NewSeededCompany("Sales TCS Co");
        EnableTcs(vm);

        vm.ShowLedgerMaster();
        var m = vm.LedgerMaster!;
        Assert.True(m.TcsEnabled);
        m.SelectedGroup = m.Groups.First(g => g.Name == "Sales Accounts");
        m.Name = "Sale of Scrap";
        m.TcsApplicable = true;
        var natureId = vm.Company!.NaturesOfGoods.First(n => n.CollectionCode == "6CE").Id;
        m.SelectedTcsNature = m.TcsNatureChoices.First(c => c.NatureId == natureId);
        Assert.True(m.Create());

        var led = vm.Company.FindLedgerByName("Sale of Scrap")!;
        Assert.True(led.TcsApplicable);
        Assert.Equal(natureId, led.TcsNatureOfGoodsId);
    }

    [Fact]
    public void Invalid_party_pan_blocks_ledger_create()
    {
        var vm = NewSeededCompany("Bad Party PAN Co");
        EnableTds(vm);

        vm.ShowLedgerMaster();
        var m = vm.LedgerMaster!;
        m.SelectedGroup = m.Groups.First(g => g.Name == "Sundry Debtors");
        m.Name = "Bad PAN Party";
        m.PartyPan = "AAPFU09399"; // last char is a digit — invalid
        Assert.False(m.Create());
        Assert.Contains("not a valid PAN", m.Message!);
        Assert.Null(vm.Company!.FindLedgerByName("Bad PAN Party"));
    }

    // ============================================================ (4) Stock Item TCS nature

    [Fact]
    public void Stock_item_captures_tcs_nature_when_enabled_and_persists()
    {
        const string companyName = "Item TCS Co";
        var vm = NewSeededCompany(companyName);
        EnableTcs(vm);

        var group = CreateStockGroup(vm, "Finished Goods");
        var unit = CreateUnit(vm, "Kg", "Kilograms");

        vm.ShowStockItemMaster();
        var m = vm.StockItemMaster!;
        Assert.True(m.TcsEnabled);
        m.Name = "Metal Scrap";
        m.SelectedGroup = m.Groups.Single(g => g.Id == group);
        m.SelectedUnit = m.Units.Single(u => u.Id == unit);
        var natureId = vm.Company!.NaturesOfGoods.First(n => n.CollectionCode == "6CE").Id;
        m.SelectedTcsNature = m.TcsNatureChoices.First(c => c.NatureId == natureId);
        Assert.True(m.Create());

        var item = vm.Company.FindStockItemByName("Metal Scrap")!;
        Assert.Equal(natureId, item.TcsNatureOfGoodsId);

        var reloaded = Reload(companyName);
        Assert.Equal(natureId, reloaded.FindStockItemByName("Metal Scrap")!.TcsNatureOfGoodsId);
    }

    [Fact]
    public void Stock_item_tcs_field_is_hidden_and_no_op_when_tcs_is_off()
    {
        var vm = NewSeededCompany("Item No-TCS Co");
        var group = CreateStockGroup(vm, "Finished Goods");
        var unit = CreateUnit(vm, "Nos", "Numbers");

        vm.ShowStockItemMaster();
        var m = vm.StockItemMaster!;
        Assert.False(m.TcsEnabled);

        m.Name = "Plain Item";
        m.SelectedGroup = m.Groups.Single(g => g.Id == group);
        m.SelectedUnit = m.Units.Single(u => u.Id == unit);
        Assert.True(m.Create());
        Assert.Null(vm.Company!.FindStockItemByName("Plain Item")!.TcsNatureOfGoodsId);
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
