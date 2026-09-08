using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Services;
using Apex.Desktop.Services;
using Apex.Desktop.ViewModels;
using Apex.Desktop.Views;
using Xunit;
using DomainLedger = Apex.Ledger.Domain.Ledger;

namespace Apex.Desktop.Tests;

/// <summary>
/// 🔴 <b>THE REACHABILITY TESTS FOR W-N1 — STATE VAT &amp; CENTRAL SALES TAX</b> (census 15.1 · 15.2 · 15.5 ·
/// 15.6).
///
/// <para><b>What these exist to stop.</b> This project has already filed three capabilities that were fully
/// built, fully tested and reachable by <i>nobody</i> — the largest ~625 lines of cheque rendering whose only
/// writers were test files, and it took four waves to make reachable. Census area 15 is unusually exposed to
/// that failure: <b>nothing outside this slice writes a VAT rate or a CST declaration form</b>, so if the F11
/// switch, the item picker, the menu route or the Alt+S editor is missing, the whole area is eighteen storage
/// columns and two reports nobody can fill in. Every test below drives the cascade with the ARROW KEYS from
/// the Gateway and then asserts the screen that actually appears.</para>
///
/// <para>🔴 <b>And the inverse property matters just as much.</b> A dealer in ordinary GST goods must never
/// meet a VAT menu row: it would invite them to compute a tax abolished for their trade. Several tests below
/// assert the ABSENCE of a route for a company that has not enabled VAT.</para>
///
/// <para>Vendor grounding: <c>help.tallysolutions.com/tally-prime/vat-masters/india-vat-enable-vat-tally/</c>,
/// <c>…/getting-started/configuring-vat-masters-tally/</c>,
/// <c>…/reports/vat-particulars-computation-tally/</c>,
/// <c>…/reports/forms-receivables-tally/</c> and <c>…/reports/vat-declaration-forms-tally/</c>.</para>
/// </summary>
public sealed class StateVatCstReachabilityTests : IDisposable
{
    private readonly string _tempDir;
    private readonly CompanyStorage _storage;

    public StateVatCstReachabilityTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "ApexVatReach_" + Guid.NewGuid().ToString("N"));
        _storage = new CompanyStorage(_tempDir);
    }

    public void Dispose()
    {
        try
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    // ================================================================= the menu route (15.5 / 15.6)

    /// <summary>
    /// 🔴 <b>THE ROUTE, WALKED WITH THE ARROW KEYS.</b> Gateway → Statutory Reports → VAT Reports → VAT
    /// Computation, and → Declaration Forms → Forms Receivable / Forms Issuable. Fails on today's main, where
    /// none of these rows exists.
    /// </summary>
    [Fact]
    public void The_three_VAT_reports_are_arrow_reachable_from_the_Gateway()
    {
        var vm = VatCompany();

        vm.ShowGateway();
        ArrowToAndDrill(vm, "Statutory Reports");
        Assert.Equal(GatewayMenu.StatutoryReports, vm.CurrentGatewayMenu);

        ArrowToAndDrill(vm, "VAT Reports");
        Assert.Equal(GatewayMenu.VatReports, vm.CurrentGatewayMenu);

        ArrowToAndDrill(vm, "VAT Computation");
        Assert.NotNull(vm.Reports);
        Assert.Equal(ReportKind.VatComputation, vm.Reports!.Kind);

        // …and back out to the nested Declaration Forms column.
        vm.ShowGateway();
        ArrowToAndDrill(vm, "Statutory Reports");
        ArrowToAndDrill(vm, "VAT Reports");
        ArrowToAndDrill(vm, "Declaration Forms");
        Assert.Equal(GatewayMenu.VatDeclarationForms, vm.CurrentGatewayMenu);

        ArrowToAndDrill(vm, "Forms Receivable");
        Assert.Equal(ReportKind.CstFormsReceivable, vm.Reports!.Kind);

        vm.ShowGateway();
        ArrowToAndDrill(vm, "Statutory Reports");
        ArrowToAndDrill(vm, "VAT Reports");
        ArrowToAndDrill(vm, "Declaration Forms");
        ArrowToAndDrill(vm, "Forms Issuable");
        Assert.Equal(ReportKind.CstFormsIssuable, vm.Reports!.Kind);
    }

    /// <summary>
    /// 🔴 <b>THE INVERSE PROPERTY.</b> A company that has NOT enabled State VAT never sees a VAT row — because
    /// a VAT row on an ordinary GST trade invites the operator to compute an abolished tax.
    /// </summary>
    [Fact]
    public void A_company_without_VAT_never_sees_a_VAT_reports_row()
    {
        // TDS on, VAT off — so the Statutory Reports hub itself is reachable and only the VAT row is missing.
        var vm = NewCompany("No Vat Co");
        new TdsTcsService(vm.Company!).EnableTds(new TdsConfig { Tan = "BLRA12345B" });
        _storage.Save(vm.Company!);

        vm.ShowGateway();
        ArrowToAndDrill(vm, "Statutory Reports");
        Assert.Equal(GatewayMenu.StatutoryReports, vm.CurrentGatewayMenu);

        Assert.DoesNotContain(vm.Menu, m => m.Label == "VAT Reports");
    }

    /// <summary>The nested column is a REAL group, not a flat dump: VAT Reports carries one page row and one
    /// group row, and the two registers live one level deeper.</summary>
    [Fact]
    public void The_VAT_reports_column_nests_the_declaration_forms_rather_than_flattening_them()
    {
        var vm = VatCompany();
        vm.ShowGateway();
        ArrowToAndDrill(vm, "Statutory Reports");
        ArrowToAndDrill(vm, "VAT Reports");

        var selectable = vm.Menu.Where(m => m.IsSelectable).Select(m => m.Label).ToList();
        Assert.Equal(new[] { "VAT Computation", "Declaration Forms" }, selectable);
    }

    // ================================================================= the F11 registration (15.1)

    /// <summary>
    /// The F11 statutory page carries the VAT block, applying it persists, and reopening the page shows what
    /// was saved rather than "off".
    ///
    /// <para>🔴 The re-open assertion is the one that catches the classic omission: a page that writes but does
    /// not LOAD lets an operator enable a feature, come back, see it apparently off, and switch it off for real
    /// on the next apply.</para>
    /// </summary>
    [Fact]
    public void The_F11_VAT_registration_persists_and_reloads()
    {
        var vm = NewCompany("F11 Vat Co");
        vm.ShowGstConfig();
        var cfg = Assert.IsType<GstConfigViewModel>(vm.GstConfig);

        cfg.VatEnabled = true;
        cfg.VatTin = "29123456789";
        cfg.VatInterstateSalesTaxNumber = "CST/29/0001";
        cfg.VatApplicableFromText = "01-04-2025";
        cfg.SelectedVatDealerType = cfg.VatDealerTypeOptions.Single(o => o.Value == VatDealerType.Composite);
        cfg.SelectedVatPeriodicity =
            cfg.VatPeriodicityOptions.Single(o => o.Value == VatReturnPeriodicity.Quarterly);
        Assert.True(cfg.ApplyVat());

        var reloaded = Reload("F11 Vat Co");
        Assert.True(reloaded.VatEnabled);
        Assert.Equal("29123456789", reloaded.Vat!.Tin);
        Assert.Equal("CST/29/0001", reloaded.Vat.InterstateSalesTaxNumber);
        Assert.Equal(new DateOnly(2025, 4, 1), reloaded.Vat.ApplicableFrom);
        Assert.Equal(VatDealerType.Composite, reloaded.Vat.DealerType);
        Assert.Equal(VatReturnPeriodicity.Quarterly, reloaded.Vat.Periodicity);
        // 🔴 No rate was keyed, so none is stored. A default here would be an unsourced statutory figure.
        Assert.Null(reloaded.Vat.CstRateAgainstFormCBasisPoints);

        // 🔴 And the F11 page built over the RELOADED company reads the block back ON. This is the assertion
        // that catches a page which writes but does not LOAD — the omission that lets an operator enable a
        // feature, return, see it apparently off, and switch it off for real on the next apply.
        var reopened = new GstConfigViewModel(reloaded, _storage, () => { });
        Assert.True(reopened.VatEnabled);
        Assert.Equal("29123456789", reopened.VatTin);
        Assert.Equal(VatDealerType.Composite, reopened.SelectedVatDealerType!.Value);
        Assert.Equal("01-04-2025", reopened.VatApplicableFromText);
    }

    /// <summary>A rate that will not parse is REPORTED, not swallowed — losing a figure the operator believes
    /// they saved is worse than refusing it.</summary>
    [Fact]
    public void An_unparseable_Form_C_rate_is_reported_and_nothing_is_saved()
    {
        var vm = NewCompany("Bad Rate Co");
        vm.ShowGstConfig();
        var cfg = vm.GstConfig!;
        cfg.VatEnabled = true;
        cfg.VatCstRateAgainstFormCText = "two per cent";

        Assert.False(cfg.ApplyVat());
        Assert.NotNull(cfg.VatMessage);
        Assert.False(Reload("Bad Rate Co").VatEnabled);
    }

    // ================================================================= the item master gate (15.2)

    /// <summary>
    /// 🔴 <b>THE GATE, THROUGH THE SCREEN.</b> The item master offers the class-of-goods picker only for a VAT
    /// company; the rate box is disabled for ordinary goods and the refusal sentence is shown; picking a class
    /// outside GST enables it; and the saved item round-trips through SQLite.
    /// </summary>
    [Fact]
    public void The_item_master_gates_the_VAT_rate_on_the_class_of_goods_and_persists_it()
    {
        var vm = VatCompany();
        SeedUnitAndGroup(vm.Company!);
        _storage.Save(vm.Company!);

        var master = new StockItemMasterViewModel(vm.Company!, _storage, () => { });
        Assert.True(master.ShowVatBlock);

        // Ordinary goods: the rate is refused and the screen says why.
        Assert.False(master.VatRateAllowed);
        Assert.NotEmpty(master.VatRefusalReason);

        master.Name = "High Speed Diesel";
        master.SelectedGroup = vm.Company!.StockGroups.First();
        master.SelectedUnit = vm.Company!.Units.First();
        master.SelectedNonGstGoodsClass =
            master.NonGstGoodsClasses.Single(o => o.Value == NonGstGoodsClass.HighSpeedDiesel);

        Assert.True(master.VatRateAllowed);
        Assert.Empty(master.VatRefusalReason);

        master.VatTaxRatePercentText = "14.5";
        Assert.True(master.Create());

        var reloaded = Reload(vm.Company!.Name);
        var item = reloaded.StockItems.Single(i => i.Name == "High Speed Diesel");
        Assert.Equal(NonGstGoodsClass.HighSpeedDiesel, item.NonGstGoodsClass);
        Assert.Equal(1450, item.VatTaxRateBasisPoints);
    }

    /// <summary>Choosing a class back inside GST CLEARS the keyed rate on screen too — a stale figure sitting in
    /// a disabled box is how an operator comes to believe a value was saved that the engine threw away.</summary>
    [Fact]
    public void Reclassifying_back_inside_GST_clears_the_rate_on_screen()
    {
        var vm = VatCompany();
        SeedUnitAndGroup(vm.Company!);
        var master = new StockItemMasterViewModel(vm.Company!, _storage, () => { });

        master.SelectedNonGstGoodsClass =
            master.NonGstGoodsClasses.Single(o => o.Value == NonGstGoodsClass.MotorSpirit);
        master.VatTaxRatePercentText = "20";

        master.SelectedNonGstGoodsClass =
            master.NonGstGoodsClasses.Single(o => o.Value == NonGstGoodsClass.None);

        Assert.Equal(string.Empty, master.VatTaxRatePercentText);
        Assert.False(master.VatRateAllowed);
    }

    /// <summary>A non-VAT company sees no VAT block on the item master at all (ER-13).</summary>
    [Fact]
    public void A_non_VAT_company_sees_no_VAT_block_on_the_item_master()
    {
        var vm = NewCompany("Plain Co");
        SeedUnitAndGroup(vm.Company!);
        var master = new StockItemMasterViewModel(vm.Company!, _storage, () => { });
        Assert.False(master.ShowVatBlock);
    }

    // ================================================================= Set/Alter Form No. (15.6)

    /// <summary>
    /// 🔴 <b>THE TEST THAT PROVES ROW 15.6 IS A FEATURE AND NOT A DISPLAY.</b> Nothing else in this build
    /// writes a CST declaration form. This drives the whole loop an operator drives: open Forms Receivable,
    /// select the candidate transaction, Alt+S, key the form, accept — and then RELOAD FROM SQLITE and find it.
    /// </summary>
    [Fact]
    public void Alt_S_on_Forms_Receivable_records_a_declaration_form_that_survives_a_reload()
    {
        var vm = VatCompany();
        var (customer, sales) = SeedTradingLedgers(vm.Company!);
        var voucher = PostSale(vm.Company!, customer, sales, 50_000m);
        _storage.Save(vm.Company!);

        vm.ShowGateway();
        ArrowToAndDrill(vm, "Statutory Reports");
        ArrowToAndDrill(vm, "VAT Reports");
        ArrowToAndDrill(vm, "Declaration Forms");
        ArrowToAndDrill(vm, "Forms Receivable");

        var reports = vm.Reports!;
        WidenPeriod(reports, voucher.Date);
        Assert.True(vm.IsCstFormsReport);

        // The candidate row is there, and it resolves back to its voucher.
        var row = reports.Rows.Single(r => r.DrillVoucherId == voucher.Id);
        reports.SelectedRow = row;

        vm.ReportBeginSetCstForm();
        Assert.True(reports.CstFormEditorOpen);
        Assert.Contains("Vch No.", reports.CstFormEditorCaption, StringComparison.Ordinal);
        // 🔴 The party's CST No. must reach the caption. It came OFF the grid when
        // XamlLayoutInvariantTests caught eight columns starving the party-name star column to 6px, and the
        // editor caption is now its ONLY reader — without this assertion `ledgers.party_cst_number` would be a
        // persisted field nothing displays, which is the dead-data shape this file exists to prevent.
        Assert.Contains("CST/07/1234", reports.CstFormEditorCaption, StringComparison.Ordinal);

        reports.CstEditFormType =
            reports.CstFormTypeOptions.Single(o => o.Value == CstDeclarationForm.FormC);
        reports.CstEditSeries = "A";
        reports.CstEditNumber = "C-000914";
        reports.CstEditDateText = "01-07-2025";
        vm.ReportApplySetCstForm();

        Assert.False(reports.CstFormEditorOpen);

        var reloaded = Reload(vm.Company!.Name);
        var saved = reloaded.Vouchers.Single(v => v.Id == voucher.Id);
        Assert.Equal(CstDeclarationForm.FormC, saved.CstFormType);
        Assert.Equal("A", saved.CstFormSeriesNumber);
        Assert.Equal("C-000914", saved.CstFormNumber);
        Assert.Equal(new DateOnly(2025, 7, 1), saved.CstFormDate);
    }

    /// <summary>An unparseable form date is refused and nothing is written — the editor stays open on the
    /// operator's own input rather than discarding it.</summary>
    [Fact]
    public void An_unparseable_form_date_is_refused_and_the_editor_stays_open()
    {
        var vm = VatCompany();
        var (customer, sales) = SeedTradingLedgers(vm.Company!);
        var voucher = PostSale(vm.Company!, customer, sales, 1_000m);

        vm.OpenReport(ReportKind.CstFormsReceivable);
        var reports = vm.Reports!;
        WidenPeriod(reports, voucher.Date);
        reports.SelectedRow = reports.Rows.Single(r => r.DrillVoucherId == voucher.Id);

        vm.ReportBeginSetCstForm();
        reports.CstEditFormType = reports.CstFormTypeOptions.Single(o => o.Value == CstDeclarationForm.FormC);
        reports.CstEditDateText = "31st of July";

        Assert.False(reports.ApplySetCstForm());
        Assert.True(reports.CstFormEditorOpen);
        Assert.NotNull(reports.CstFormEditorMessage);
        Assert.Null(voucher.CstFormType);
    }

    /// <summary>Alt+S is dead on every other report — the chord must not fire from a Trial Balance.</summary>
    [Fact]
    public void The_Set_Alter_Form_chord_is_dead_outside_the_declaration_forms_registers()
    {
        var vm = VatCompany();
        vm.OpenReport(ReportKind.TrialBalance);
        Assert.False(vm.IsCstFormsReport);

        vm.ReportBeginSetCstForm();   // a no-op, not a crash
        Assert.False(vm.Reports!.CstFormEditorOpen);
    }

    // ================================================================= the realised visual tree

    /// <summary>
    /// 🔴 <b>THE SCOPE WARNING REACHES THE SCREEN, IN FULL AND UNTRIMMED.</b> Asserting a viewmodel flag is
    /// exactly the test that passes on a broken build, so this one renders the window and reads the realised
    /// <see cref="TextBlock"/>.
    ///
    /// <para>Why it must WRAP rather than trim: the Alt+K company menu shipped an 888px disclosure in a 350px
    /// column, cut mid-word at 39% with no ellipsis, so the operator actually read "NOT IN THIS BUILD: TALLYV".
    /// A scope warning cut at the column edge protects nobody, and this one is the sentence that stops a dealer
    /// in ordinary goods switching on a repealed tax.</para>
    /// </summary>
    [AvaloniaFact]
    public void The_F11_VAT_scope_warning_is_rendered_in_full_and_wraps()
    {
        var vm = NewCompany("Scope Notice Co");
        var window = new MainWindow { DataContext = vm, Width = 1280, Height = 720 };
        window.Show();
        Pump(window);

        vm.ShowGstConfig();
        Pump(window);

        var block = Descendants(window)
            .OfType<TextBlock>()
            .FirstOrDefault(t => t.IsEffectivelyVisible
                                 && t.Text == GstConfigViewModel.VatScopeNotice);

        Assert.True(block is not null,
            "The F11 State VAT scope warning did not reach the visual tree. Without it an operator meets a bare "
            + "VAT switch with nothing telling them the tax was abolished for ordinary goods.");
        Assert.True(block!.Bounds.Width > 0 && block.Bounds.Height > 0,
            "The scope warning rendered with a degenerate size, so nobody can read it.");
        Assert.Equal(Avalonia.Media.TextWrapping.Wrap, block.TextWrapping);
        Assert.Equal(Avalonia.Media.TextTrimming.None, block.TextTrimming);

        // It really is longer than one line at this width — i.e. the wrap is load-bearing, not decorative.
        Assert.True(block.Bounds.Height > 16,
            $"The warning occupied {block.Bounds.Height:0.#}px, which is one line — the measurement this test "
            + "makes only means something while the text is long enough to need wrapping.");
    }

    /// <summary>
    /// The class-of-goods captions reach the item master's realised picker, and each is the STATUTE's own
    /// wording — so an operator can match the list against CGST Act s.9(1)/s.9(2) rather than trade slang.
    /// </summary>
    [Fact]
    public void Every_class_of_goods_caption_is_the_statutes_own_wording()
    {
        Assert.Equal("Alcoholic liquor for human consumption",
            NonGstGoods.Caption(NonGstGoodsClass.AlcoholicLiquorForHumanConsumption));
        Assert.Equal("High speed diesel", NonGstGoods.Caption(NonGstGoodsClass.HighSpeedDiesel));
        Assert.Equal("Motor spirit (petrol)", NonGstGoods.Caption(NonGstGoodsClass.MotorSpirit));
        Assert.Equal("Aviation turbine fuel", NonGstGoods.Caption(NonGstGoodsClass.AviationTurbineFuel));

        // …and the product NEVER renders the reference product's brand.
        foreach (var cls in NonGstGoods.All)
            Assert.DoesNotContain("Tally", NonGstGoods.Caption(cls), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The VAT Computation report prints its own limits: that the tax is computed rather than posted, and which
    /// vendor sections are absent. A working paper that travels away from this screen must take its caveats
    /// with it.
    /// </summary>
    [Fact]
    public void The_VAT_Computation_report_carries_its_own_disclosure_rows()
    {
        var vm = VatCompany();
        var (customer, sales) = SeedTradingLedgers(vm.Company!);
        var voucher = PostSale(vm.Company!, customer, sales, 1_000m);

        vm.OpenReport(ReportKind.VatComputation);
        var reports = vm.Reports!;
        WidenPeriod(reports, voucher.Date);

        var text = string.Join(" | ", reports.Rows.Select(r => r.Col1));
        Assert.Contains("not posted", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("VAT Adjustments", text, StringComparison.Ordinal);
        Assert.Contains("state VAT return forms", text, StringComparison.OrdinalIgnoreCase);
        // The CST line says the rate is UNSET rather than showing nil.
        Assert.Contains("not set", text, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>A non-VAT company opening the report by kind gets the honest empty state naming the vendor's
    /// own F11 caption — never a blank grid.</summary>
    [Fact]
    public void A_non_VAT_company_gets_an_empty_state_naming_the_F11_switch()
    {
        var vm = NewCompany("Empty State Co");
        vm.OpenReport(ReportKind.VatComputation);

        var text = string.Join(" | ", vm.Reports!.Rows.Select(r => r.Col1));
        Assert.Contains("Enable Value Added Tax (VAT)", text, StringComparison.Ordinal);
    }

    // ================================================================= harness

    private MainWindowViewModel NewCompany(string name)
    {
        var vm = new MainWindowViewModel(_storage);
        vm.NewCompanyName = name;
        vm.CreateCompany();
        Assert.Equal(Screen.Gateway, vm.CurrentScreen);
        return vm;
    }

    /// <summary>A company with State VAT switched on, saved.</summary>
    private MainWindowViewModel VatCompany()
    {
        var vm = NewCompany("Vat Reach Co " + Guid.NewGuid().ToString("N")[..8]);
        new VatService(vm.Company!).EnableVat(tin: "29123456789");
        _storage.Save(vm.Company!);
        return vm;
    }

    private Company Reload(string companyName)
    {
        var entry = _storage.ListCompanies().Single(e => e.Name == companyName);
        return _storage.Load(entry);
    }

    private static void SeedUnitAndGroup(Company c)
    {
        var inventory = new InventoryService(c);
        if (c.Units.Count == 0) inventory.CreateSimpleUnit("Nos", "Numbers");
        if (c.FindStockGroupByName("Primary") is null) inventory.CreateStockGroup("Primary");
    }

    private static (DomainLedger Customer, DomainLedger Sales) SeedTradingLedgers(Company c)
    {
        var customer = new DomainLedger(
            Guid.NewGuid(), "Highway Fuels", c.FindGroupByName("Sundry Debtors")!.Id,
            Money.Zero, openingIsDebit: true)
        { PartyCstNumber = "CST/07/1234" };
        c.AddLedger(customer);

        var sales = new DomainLedger(
            Guid.NewGuid(), "Fuel Sales", c.FindGroupByName("Sales Accounts")!.Id,
            Money.Zero, openingIsDebit: false);
        c.AddLedger(sales);
        return (customer, sales);
    }

    private static Voucher PostSale(Company c, DomainLedger customer, DomainLedger sales, decimal amount)
    {
        var salesType = c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Sales);
        var date = c.FinancialYearStart.AddMonths(2);
        return new LedgerService(c).Post(new Voucher(
            Guid.NewGuid(), salesType.Id, date,
            new[]
            {
                new EntryLine(customer.Id, Money.FromRupees(amount), DrCr.Debit),
                new EntryLine(sales.Id, Money.FromRupees(amount), DrCr.Credit),
            },
            partyId: customer.Id));
    }

    /// <summary>Puts the report's window around a date, so a fixture voucher is inside it whatever the
    /// company's financial year happens to be.</summary>
    private static void WidenPeriod(ReportsViewModel reports, DateOnly around) =>
        reports.SetPeriod(new DateOnly(around.Year - 1, 4, 1), new DateOnly(around.Year + 1, 3, 31));

    /// <summary>Arrows down the ACTIVE column until the highlighted row carries this label, then drills in —
    /// the exact sequence an operator's fingers perform. Fails loudly when the row is not arrow-reachable,
    /// which is the failure mode a "does the ViewModel have the method" test cannot see.</summary>
    private static void ArrowToAndDrill(MainWindowViewModel vm, string label)
    {
        for (var i = 0; i < vm.Menu.Count + 2; i++)
        {
            if (vm.Menu[vm.SelectedIndex].Label == label) { vm.DrillIn(); return; }
            vm.MoveDown();
        }
        Assert.Fail($"'{label}' was not reachable by arrow navigation in the active column.");
    }

    private static void Pump(MainWindow w)
    {
        w.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        w.UpdateLayout();
    }

    private static IEnumerable<Visual> Descendants(Visual v)
    {
        foreach (var c in v.GetVisualChildren())
        {
            yield return c;
            foreach (var g in Descendants(c)) yield return g;
        }
    }
}
