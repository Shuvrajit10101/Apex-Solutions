using System;
using System.IO;
using System.Linq;
using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Services;
using Apex.Desktop.Services;
using Apex.Desktop.ViewModels;
using DomainLedger = Apex.Ledger.Domain.Ledger;

namespace Apex.Desktop.Tests;

/// <summary>
/// Coverage for the Phase-9 slice-3 <b>Composition-scheme UI</b> surfaced in the cascade (RQ-4/RQ-10/RQ-16):
/// the F11 GST config captures the Composition registration + sub-type + opt-in date and persists it (Area 1);
/// a Composition dealer surfaces the CMP-08 (quarterly) and GSTR-4 (annual) return screens under
/// Reports → Statutory Reports → Composition Returns, each projecting the pure engine (Area 5); a composition
/// sale renders in the voucher-detail as a <b>Bill of Supply</b> with the §10 declaration (Area 5); and a
/// Regular company is unchanged (byte-identical, ER-13). Drives the real shell view models over a throwaway
/// <c>.db</c> — no UI toolkit.
/// </summary>
public sealed class CompositionUiViewModelTests : IDisposable
{
    private const string GstinMaharashtra = "27AAPFU0939F1ZV"; // state code 27
    private static readonly DateOnly FyStart = new(2024, 4, 1);

    private readonly string _tempDir;
    private readonly CompanyStorage _storage;

    public CompositionUiViewModelTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "ApexCompositionUiTests_" + Guid.NewGuid().ToString("N"));
        _storage = new CompanyStorage(_tempDir);
    }

    // ---------------------------------------------------------------- helpers

    private MainWindowViewModel NewSeededCompany(string name)
    {
        var vm = new MainWindowViewModel(_storage);
        vm.NewCompanyName = name;
        vm.CreateCompany();
        Assert.Equal(Screen.Gateway, vm.CurrentScreen);
        var c = vm.Company!;
        c.FinancialYearStart = FyStart;
        c.BooksBeginFrom = FyStart;
        return vm;
    }

    private Company Reload(string companyName)
    {
        var entry = _storage.ListCompanies().Single(e => e.Name == companyName);
        return _storage.Load(entry);
    }

    /// <summary>Enables GST directly with a chosen registration type (used to seed a Composition / Regular company for
    /// the report + Bill-of-Supply tests, bypassing the config screen).</summary>
    private static void EnableGst(Company c, GstRegistrationType type, CompositionSubType? subType = null)
    {
        new GstService(c).EnableGst(new GstConfig
        {
            HomeStateCode = "27",
            Gstin = GstinMaharashtra,
            RegistrationType = type,
            CompositionSubType = type == GstRegistrationType.Composition ? (subType ?? CompositionSubType.Trader) : null,
            ApplicableFrom = FyStart,
            Periodicity = GstReturnPeriodicity.Monthly,
        });
    }

    private static DomainLedger AddLedger(Company c, string name, string groupName)
    {
        var group = c.FindGroupByName(groupName) ?? throw new InvalidOperationException($"No group '{groupName}'.");
        var ledger = new DomainLedger(Guid.NewGuid(), name, group.Id, Money.Zero, openingIsDebit: false);
        c.AddLedger(ledger);
        return ledger;
    }

    // ================================================================ Area 1: F11 Composition config

    [Fact]
    public void Enable_composition_captures_subtype_and_optin_and_persists()
    {
        const string companyName = "Comp Config Co";
        var vm = NewSeededCompany(companyName);

        vm.ShowGstConfig();
        Assert.Equal(Screen.GstConfig, vm.CurrentScreen);
        var page = vm.GstConfig!;

        page.GstEnabled = true;
        page.Gstin = GstinMaharashtra;               // auto-fills the home state (27)
        page.RegistrationType = page.RegistrationTypes.Single(o => o.Value == GstRegistrationType.Composition);
        Assert.True(page.IsComposition);              // the composition block is now shown
        page.SelectedCompositionSubType = page.CompositionSubTypes.Single(o => o.Value == CompositionSubType.Trader);
        page.CompositionOptInDateText = "2024-04-01";

        // The advisory rate/threshold text reflects the chosen sub-type (Trader = 1% on taxable supplies).
        Assert.Contains("1%", page.CompositionRateText);
        Assert.Contains("taxable supplies", page.CompositionRateText);
        Assert.False(string.IsNullOrWhiteSpace(page.CompositionThresholdText));

        Assert.True(page.Apply());

        var c = vm.Company!;
        Assert.True(c.GstEnabled);
        Assert.Equal(GstRegistrationType.Composition, c.Gst!.RegistrationType);
        Assert.Equal(CompositionSubType.Trader, c.Gst.CompositionSubType);
        Assert.Equal(new DateOnly(2024, 4, 1), c.Gst.CompositionOptInDate);

        // Persists across a reload.
        var reloaded = Reload(companyName);
        Assert.Equal(GstRegistrationType.Composition, reloaded.Gst!.RegistrationType);
        Assert.Equal(CompositionSubType.Trader, reloaded.Gst.CompositionSubType);
    }

    [Fact]
    public void Switching_registration_away_from_composition_clears_the_subtype()
    {
        const string companyName = "Comp Switch Co";
        var vm = NewSeededCompany(companyName);

        vm.ShowGstConfig();
        var page = vm.GstConfig!;
        page.GstEnabled = true;
        page.Gstin = GstinMaharashtra;
        page.RegistrationType = page.RegistrationTypes.Single(o => o.Value == GstRegistrationType.Composition);
        page.SelectedCompositionSubType = page.CompositionSubTypes.Single(o => o.Value == CompositionSubType.Restaurant);
        Assert.True(page.Apply());
        Assert.Equal(CompositionSubType.Restaurant, vm.Company!.Gst!.CompositionSubType);

        // Flip back to Regular — the sub-type + opt-in date are cleared (ER-13 byte-identical to a Regular company).
        page.RegistrationType = page.RegistrationTypes.Single(o => o.Value == GstRegistrationType.Regular);
        Assert.False(page.IsComposition);
        Assert.True(page.Apply());
        Assert.Equal(GstRegistrationType.Regular, vm.Company!.Gst!.RegistrationType);
        Assert.Null(vm.Company.Gst.CompositionSubType);
    }

    // ================================================================ Area 5: Composition Returns nav + reports

    [Fact]
    public void Composition_dealer_surfaces_composition_returns_menu_and_opens_cmp08_and_gstr4()
    {
        const string companyName = "Comp Returns Co";
        var vm = NewSeededCompany(companyName);
        EnableGst(vm.Company!, GstRegistrationType.Composition, CompositionSubType.Manufacturer);
        _storage.Save(vm.Company!);
        vm.ShowGateway();

        // Reports → Statutory Reports → Composition Returns surfaces the two returns.
        vm.ShowCompositionReturnsMenu();
        Assert.Equal(GatewayMenu.CompositionReturns, vm.CurrentGatewayMenu);
        var labels = vm.Columns[^1].Items.Where(i => i.IsSelectable).Select(i => i.Label).ToList();
        Assert.Contains("CMP-08", labels);
        Assert.Contains("GSTR-4", labels);

        // CMP-08 opens as its own page.
        vm.OpenCmp08Report();
        Assert.Equal(Screen.Cmp08Report, vm.CurrentScreen);
        Assert.NotNull(vm.Cmp08Report);
        Assert.Equal(4, vm.Cmp08Report!.Quarters.Count);
        Assert.True(vm.Cmp08Report.Statement.Applicable);
        Assert.Contains("Manufacturer", vm.Cmp08Report.SubTypeText);

        // GSTR-4 opens as its own page.
        vm.OpenGstr4Report();
        Assert.Equal(Screen.Gstr4Report, vm.CurrentScreen);
        Assert.NotNull(vm.Gstr4Report);
        Assert.True(vm.Gstr4Report!.Return.Applicable);
        Assert.Equal(4, vm.Gstr4Report.Quarters.Count);
    }

    [Fact]
    public void Regular_company_never_surfaces_composition_returns()
    {
        const string companyName = "Reg No Comp Co";
        var vm = NewSeededCompany(companyName);
        EnableGst(vm.Company!, GstRegistrationType.Regular);
        _storage.Save(vm.Company!);
        vm.ShowGateway();

        // The menu path is a no-op and neither report opens for a Regular company (ER-13).
        vm.ShowCompositionReturnsMenu();
        Assert.NotEqual(GatewayMenu.CompositionReturns, vm.CurrentGatewayMenu);

        vm.OpenCmp08Report();
        Assert.Null(vm.Cmp08Report);
        Assert.NotEqual(Screen.Cmp08Report, vm.CurrentScreen);

        vm.OpenGstr4Report();
        Assert.Null(vm.Gstr4Report);
    }

    // ================================================================ Area 5: Bill of Supply (voucher detail)

    [Fact]
    public void Composition_sale_renders_as_bill_of_supply_with_declaration()
    {
        const string companyName = "BoS Comp Co";
        var vm = NewSeededCompany(companyName);
        var c = vm.Company!;
        EnableGst(c, GstRegistrationType.Composition, CompositionSubType.Trader);
        var customer = AddLedger(c, "Local Customer", "Sundry Debtors");
        var sales = AddLedger(c, "Sales", "Sales Accounts");

        var salesType = c.VoucherTypes.Single(t => t.BaseType == VoucherBaseType.Sales && t.IsActive);
        var voucher = new Voucher(
            Guid.NewGuid(), salesType.Id, FyStart,
            new[]
            {
                new EntryLine(customer.Id, Money.FromRupees(1000m), DrCr.Debit),
                new EntryLine(sales.Id, Money.FromRupees(1000m), DrCr.Credit),
            },
            number: 1, partyId: customer.Id);

        var detail = new VoucherDetailViewModel(c, voucher);
        Assert.True(detail.IsBillOfSupply);
        Assert.Equal("Bill of Supply", detail.DocumentLabel);
        Assert.Equal("Composition taxable person, not eligible to collect tax on supplies", detail.BillOfSupplyDeclaration);
    }

    [Fact]
    public void Regular_company_sale_is_not_a_bill_of_supply()
    {
        const string companyName = "BoS Reg Co";
        var vm = NewSeededCompany(companyName);
        var c = vm.Company!;
        EnableGst(c, GstRegistrationType.Regular);
        var customer = AddLedger(c, "Local Customer", "Sundry Debtors");
        var sales = AddLedger(c, "Sales", "Sales Accounts");

        var salesType = c.VoucherTypes.Single(t => t.BaseType == VoucherBaseType.Sales && t.IsActive);
        var voucher = new Voucher(
            Guid.NewGuid(), salesType.Id, FyStart,
            new[]
            {
                new EntryLine(customer.Id, Money.FromRupees(1000m), DrCr.Debit),
                new EntryLine(sales.Id, Money.FromRupees(1000m), DrCr.Credit),
            },
            number: 1, partyId: customer.Id);

        var detail = new VoucherDetailViewModel(c, voucher);
        Assert.False(detail.IsBillOfSupply);
        Assert.NotEqual("Bill of Supply", detail.DocumentLabel);
        Assert.Equal(string.Empty, detail.BillOfSupplyDeclaration);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true); }
        catch { /* best-effort cleanup */ }
    }
}
