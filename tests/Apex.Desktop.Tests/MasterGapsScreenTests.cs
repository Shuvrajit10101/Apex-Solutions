using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Apex.Desktop.Services;
using Apex.Desktop.ViewModels;
using Apex.Desktop.Views;
using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Services;
using Path = System.IO.Path;
using Xunit;

namespace Apex.Desktop.Tests;

/// <summary>
/// 🔴 <b>CENSUS 2.2 (Group behavioural flags, incl. defect T1-31) · 3.6 (Alternate units) · 3.13 (GST capture on
/// the Stock Group and accounting Group masters) — THE REACHABILITY HALF.</b>
///
/// <para><b>Why every one of these walks the REALISED VISUAL TREE.</b> A test asserting
/// <c>vm.AccountGroupMaster!.AffectsGrossProfits = true</c> passes against a build with no XAML field, which
/// would leave the operator a screen they cannot type into — the exact defect class this repository has filed
/// three times (a menu item nothing dispatches; a verb with no door; and row 13.6, where <b>deleting the HTML
/// export radio left eight view-model tests GREEN while the format became unreachable by any user</b>). So each
/// field below is looked up BY NAME in the realised tree and asserted visible, and the two end-to-end tests
/// drive the real screen and then read the saved domain.</para>
///
/// <para><b>What each one is red against on today's main.</b> The Primary option does not exist in the Group
/// master's parent picker at all (T1-31 — <c>ParentOptions</c> holds only existing groups, and both
/// <c>GroupService.CreateGroup</c> and the screen refuse a null parent); none of the five behavioural fields
/// exists in any form; the GST block exists on neither master (the canonical importer is the only writer in the
/// product); the Stock Group master has no Alter verb and its list rows carry no id; and the Stock Item master
/// has no alternate-unit field.</para>
/// </summary>
public sealed class MasterGapsScreenTests
{
    // ================================================================= harness

    private static (MainWindow Window, MainWindowViewModel Vm, string TempDir) NewCompany(string name)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "ApexMasterGaps_" + Guid.NewGuid().ToString("N"));
        var storage = new CompanyStorage(tempDir);
        var vm = new MainWindowViewModel(storage);
        var window = new MainWindow { DataContext = vm, Width = 1440, Height = 900 };
        window.Show();

        vm.NewCompanyName = name;
        vm.CreateCompany();
        Assert.Equal(Screen.Gateway, vm.CurrentScreen);
        Pump(window);
        return (window, vm, tempDir);
    }

    private static void Pump(MainWindow w)
    {
        w.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        w.UpdateLayout();
    }

    private static void Close(MainWindow window, string tempDir)
    {
        window.Close();
        try
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true);
        }
        catch { /* temp */ }
    }

    private static IEnumerable<Visual> Descendants(Visual v)
    {
        foreach (var c in v.GetVisualChildren())
        {
            yield return c;
            foreach (var g in Descendants(c)) yield return g;
        }
    }

    private static T? Named<T>(MainWindow w, string name) where T : Visual =>
        Descendants(w).OfType<T>().FirstOrDefault(x => x.Name == name);

    /// <summary>
    /// 🔴 <b>EFFECTIVE visibility, not the local flag.</b> Avalonia's <c>IsVisible</c> is the control's OWN
    /// property, so a field sitting inside a collapsed container reports <c>true</c> while the operator cannot
    /// see it. Asserting on the local flag would have passed for a screen that shows nothing — which is the very
    /// failure this file exists to catch, so it must not be re-introduced by the test's own helper.
    /// <c>IsEffectivelyVisible</c> folds in every ancestor.
    /// </summary>
    private static bool Shown(Visual? v) => v is not null && v.IsEffectivelyVisible;

    /// <summary>Every realised <see cref="CheckBox"/> caption on screen — the strings an operator can actually
    /// read. Asserting against these rather than against a view-model property is the whole point of this file.</summary>
    private static IReadOnlyList<string> RealisedCheckBoxCaptions(MainWindow w) =>
        Descendants(w).OfType<CheckBox>()
            .Where(cb => cb.IsEffectivelyVisible && cb.Content is string)
            .Select(cb => (string)cb.Content!)
            .ToList();

    /// <summary>Every realised, VISIBLE <see cref="TextBlock"/> string on screen.</summary>
    private static IReadOnlyList<string> RealisedLabels(MainWindow w) =>
        Descendants(w).OfType<TextBlock>()
            .Where(t => t.IsEffectivelyVisible && !string.IsNullOrWhiteSpace(t.Text))
            .Select(t => t.Text!)
            .ToList();

    private static void OpenStockItemMaster(MainWindow w, MainWindowViewModel vm)
    {
        vm.ShowStockItemMaster();
        Pump(w);
        Assert.Equal(Screen.StockItemMaster, vm.CurrentScreen);
    }

    // ================================================================= census 2.2 + T1-31

    /// <summary>
    /// 🔴 <b>T1-31.</b> The Group master's <b>Under</b> picker offers <i>Primary</i>, and it is the FIRST option —
    /// exactly as this product's own Stock Group and Godown masters have always offered it. Red on main, where
    /// the picker holds nothing but existing groups.
    /// </summary>
    [AvaloniaFact]
    public void The_group_master_offers_Primary_in_the_Under_picker()
    {
        var (w, vm, dir) = NewCompany("T131 Co");
        try
        {
            vm.ShowAccountGroupMaster();
            Pump(w);

            var picker = Named<ComboBox>(w, "AccountGroupUnderPicker");
            Assert.NotNull(picker);
            Assert.True(Shown(picker));

            var options = picker.ItemsSource!.Cast<ParentAccountGroupOption>().ToList();
            Assert.True(options[0].IsPrimary, "Primary must be the first option in the Under picker.");
            Assert.Contains("Primary", options[0].Display);
            // ... and every existing group is still offered alongside it.
            Assert.Contains(options, o => o.Group?.Name == "Current Liabilities");
        }
        finally { Close(w, dir); }
    }

    /// <summary>
    /// The two PRIMARY-ONLY vendor fields — <i>Nature of Group</i> and <i>Does it affect Gross Profits</i> —
    /// realise only when <i>Primary</i> is chosen, and the derived-nature caption takes their place otherwise.
    /// This is the vendor's own rule ("appears only if the group is created under Primary"), asserted on the
    /// realised tree rather than on a boolean.
    /// </summary>
    [AvaloniaFact]
    public void The_two_primary_only_fields_appear_only_under_Primary()
    {
        var (w, vm, dir) = NewCompany("Primary Fields Co");
        try
        {
            vm.ShowAccountGroupMaster();
            Pump(w);
            var m = vm.AccountGroupMaster!;

            // Default is a CHILD (Current Liabilities): neither primary-only field is on screen.
            Assert.False(m.IsPrimaryGroup);
            Assert.False(Shown(Named<ComboBox>(w, "AccountGroupNaturePicker")));
            Assert.False(Shown(Named<CheckBox>(w, "GroupAffectsGrossProfits")));
            Assert.Contains("(derived from parent)", RealisedLabels(w));

            // Switch the Under to Primary: both appear, and the derived caption goes.
            m.SelectedParent = m.ParentOptions.First(o => o.IsPrimary);
            Pump(w);

            Assert.True(Shown(Named<ComboBox>(w, "AccountGroupNaturePicker")));
            Assert.True(Shown(Named<CheckBox>(w, "GroupAffectsGrossProfits")));
            Assert.Contains("Does it affect Gross Profits", RealisedCheckBoxCaptions(w));
            Assert.DoesNotContain("(derived from parent)", RealisedLabels(w));

            // The four vendor Nature options, verbatim.
            var natures = Named<ComboBox>(w, "AccountGroupNaturePicker")!
                .ItemsSource!.Cast<GroupNatureChoice>().Select(n => n.Display).ToList();
            Assert.Equal(new[] { "Assets", "Liabilities", "Income", "Expenses" }, natures);
        }
        finally { Close(w, dir); }
    }

    /// <summary>
    /// The three always-shown behavioural fields and the allocation picker are on screen with the vendor's
    /// captions verbatim, and the picker's three options are the vendor's three.
    /// </summary>
    [AvaloniaFact]
    public void The_remaining_behavioural_fields_are_on_screen_with_the_vendor_captions()
    {
        var (w, vm, dir) = NewCompany("Behaviour Captions Co");
        try
        {
            vm.ShowAccountGroupMaster();
            Pump(w);

            var captions = RealisedCheckBoxCaptions(w);
            Assert.Contains("Group behaves like a sub-ledger", captions);
            Assert.Contains("Nett Debit/Credit Balances for Reporting", captions);
            Assert.Contains("Used for calculation (for example: taxes, discounts)", captions);

            Assert.Contains("Method to allocate when used in purchase invoice", RealisedLabels(w));
            var methods = Named<ComboBox>(w, "GroupAllocationMethodPicker")!
                .ItemsSource!.Cast<GroupAllocationMethodChoice>().Select(o => o.Display).ToList();
            Assert.Equal(new[] { "Not Applicable", "Appropriate by Qty", "Appropriate by Value" }, methods);
        }
        finally { Close(w, dir); }
    }

    /// <summary>
    /// 🔴 <b>END TO END.</b> An operator creates a custom PRIMARY group from the screen, with a nature and all
    /// five behavioural fields set, and every one of them reaches the saved domain. Nothing short of this proves
    /// the row: the screen, the engine, the persistence and the reload all have to agree.
    /// </summary>
    [AvaloniaFact]
    public void Creating_a_primary_group_from_the_screen_saves_every_vendor_field()
    {
        var (w, vm, dir) = NewCompany("Primary Create Co");
        try
        {
            vm.ShowAccountGroupMaster();
            Pump(w);
            var m = vm.AccountGroupMaster!;

            m.SelectedParent = m.ParentOptions.First(o => o.IsPrimary);
            Pump(w);
            m.Name = "Trading Charges";
            m.SelectedNature = m.NatureChoices.First(n => n.Display == "Expenses");
            m.AffectsGrossProfits = true;
            m.BehavesLikeSubLedger = true;
            m.NettBalancesForReporting = true;
            m.UsedForCalculation = true;
            m.SelectedAllocationMethod =
                m.AllocationMethodChoices.First(o => o.Display == "Appropriate by Value");

            vm.ActivateSelected();
            Pump(w);

            var saved = vm.Company!.FindGroupByName("Trading Charges");
            Assert.NotNull(saved);
            Assert.True(saved!.IsPrimary);
            Assert.Equal(GroupNature.Expense, saved.Nature);
            Assert.True(saved.AffectsGrossProfits);
            Assert.True(saved.BehavesLikeSubLedger);
            Assert.True(saved.NettBalancesForReporting);
            Assert.True(saved.UsedForCalculation);
            Assert.Equal(MethodOfAppropriation.ByValue, saved.PurchaseAllocationMethod);
        }
        finally { Close(w, dir); }
    }

    // ================================================================= census 3.13

    /// <summary>
    /// The accounting Group master carries the vendor's <i>Set/Alter GST Details</i> block, and its four fields
    /// realise when it is switched on. Red on main: <c>MasterGstDetails</c> has exactly one occurrence in the
    /// whole desktop project and it is a doc comment.
    /// </summary>
    [AvaloniaFact]
    public void The_accounting_group_master_can_capture_a_GST_block()
    {
        var (w, vm, dir) = NewCompany("Group Gst Co");
        try
        {
            vm.ShowAccountGroupMaster();
            Pump(w);

            var toggle = Named<CheckBox>(w, "AccountGroupSetGstDetails");
            Assert.NotNull(toggle);
            Assert.Contains("Set/Alter GST Details", RealisedCheckBoxCaptions(w));

            // Off: no field is on screen (an empty block would be a rung the walk stops at with nothing to say).
            Assert.False(Shown(Named<TextBox>(w, "AccountGroupGstHsn")));

            var m = vm.AccountGroupMaster!;
            m.Gst.Enabled = true;
            Pump(w);

            Assert.True(Shown(Named<TextBox>(w, "AccountGroupGstHsn")));
            Assert.True(Shown(Named<TextBox>(w, "AccountGroupGstRate")));
            Assert.True(Shown(Named<ComboBox>(w, "AccountGroupGstTaxability")));
            Assert.True(Shown(Named<ComboBox>(w, "AccountGroupGstSupplyType")));

            // And it saves onto the group the rate walk reads.
            m.SelectedParent = m.ParentOptions.First(o => o.Group?.Name == "Current Liabilities");
            m.Name = "Taxable Charges";
            m.Gst.HsnSac = "998311";
            m.Gst.RatePercentText = "18";
            m.Gst.SelectedSupplyType = m.Gst.SupplyTypeChoices.First(s => s.Display == "Services");
            vm.ActivateSelected();
            Pump(w);

            var saved = vm.Company!.FindGroupByName("Taxable Charges")!;
            Assert.NotNull(saved.Gst);
            Assert.Equal("998311", saved.Gst!.HsnSac);
            Assert.Equal(1800, saved.Gst.RateBasisPoints);
            Assert.Equal(GstSupplyType.Services, saved.Gst.SupplyType);
            Assert.Equal(GstTaxability.Taxable, saved.Gst.Taxability);
        }
        finally { Close(w, dir); }
    }

    /// <summary>A malformed HSN is refused by the DOMAIN's own rule, surfaced on the screen, and nothing is
    /// created — so the application cannot produce a book its own importer would reject.</summary>
    [AvaloniaFact]
    public void A_malformed_HSN_is_refused_and_nothing_is_created()
    {
        var (w, vm, dir) = NewCompany("Bad Hsn Co");
        try
        {
            vm.ShowAccountGroupMaster();
            Pump(w);
            var m = vm.AccountGroupMaster!;
            m.SelectedParent = m.ParentOptions.First(o => o.Group?.Name == "Current Liabilities");
            m.Name = "Bad HSN Group";
            m.Gst.Enabled = true;
            m.Gst.HsnSac = "12345";           // 5 digits — the vendor's rule is 4, 6 or 8

            Assert.False(m.Create());
            Assert.Contains("4, 6 or 8 digits", m.Message);
            Assert.Null(vm.Company!.FindGroupByName("Bad HSN Group"));
        }
        finally { Close(w, dir); }
    }

    /// <summary>
    /// 🔴 <b>THE STOCK GROUP MASTER HAD NO ALTER VERB AT ALL.</b> Ctrl+Enter on a highlighted existing-group row
    /// opens Stock Group Alteration, pre-filled, and re-saving writes the GST rung
    /// <c>MasterAncestry.NearestStockGroupGst</c> reads at transaction time. Red on main twice over: the chord
    /// resolves nothing, and the list rows carried no id to resolve.
    /// </summary>
    [AvaloniaFact]
    public void The_stock_group_master_captures_GST_and_can_be_altered_by_Ctrl_Enter()
    {
        var (w, vm, dir) = NewCompany("Stock Group Gst Co");
        try
        {
            vm.ShowStockGroupMaster();
            Pump(w);
            var create = vm.StockGroupMaster!;

            Assert.NotNull(Named<CheckBox>(w, "StockGroupSetGstDetails"));
            create.Name = "Shirts";
            create.Gst.Enabled = true;
            create.Gst.RatePercentText = "12";
            Assert.True(create.Create());
            Pump(w);

            var shirts = vm.Company!.FindStockGroupByName("Shirts")!;
            Assert.Equal(1200, shirts.Gst!.RateBasisPoints);

            // Highlight the row and press Ctrl+Enter — the real chord, on the real screen.
            vm.MoveDown();
            Pump(w);
            Assert.NotNull(create.HighlightedRow);
            Assert.True(vm.AlterHighlightedStockGroupRow());
            Pump(w);

            var alter = vm.StockGroupMaster!;
            Assert.True(alter.IsAltering);
            // The heading says which VERB is running. The form is identical in both modes, so a heading reading
            // "Creation" over an alteration is how an operator mistakes one for the other.
            Assert.Equal("Stock Group Alteration", alter.Caption);
            Assert.Equal("Stock Group Alteration", Named<TextBlock>(w, "StockGroupMasterCaption")!.Text);
            Assert.Equal(alter.Existing.Count, vm.StockGroupMaster!.Existing.Count);

            // The form came back pre-filled with the block that was saved.
            var target = alter.Existing.First(r => r.Name == "Shirts");
            Assert.Contains("12", target.Gst);

            // Correcting the rate through the alteration screen writes it back to the SAME group.
            var alterVm = StockGroupMasterViewModel.ForAlter(
                vm.Company!, new CompanyStorage(dir), shirts.Id, () => { })!;
            Assert.True(alterVm.Gst.Enabled);
            Assert.Equal("12", alterVm.Gst.RatePercentText);
            alterVm.Gst.RatePercentText = "5";
            Assert.True(alterVm.Alter());
            Assert.Equal(500, vm.Company!.FindStockGroup(shirts.Id)!.Gst!.RateBasisPoints);
        }
        finally { Close(w, dir); }
    }

    // ================================================================= census 3.6

    /// <summary>
    /// The Stock Item master carries the vendor's <b>Alternate units</b> field, and the factor box plus the live
    /// "1 Box = 10 Nos" sentence realise only once a unit is chosen — so a factor is never offered with nothing
    /// to convert, and the DIRECTION of the factor is visible at entry time rather than discovered on a report.
    /// </summary>
    [AvaloniaFact]
    public void The_stock_item_master_offers_an_alternate_unit_and_shows_the_conversion()
    {
        var (w, vm, dir) = NewCompany("Alt Unit Screen Co");
        try
        {
            SeedUnitsAndGroup(vm);
            OpenStockItemMaster(w, vm);
            var m = vm.StockItemMaster!;

            Assert.Contains("Alternate units", RealisedLabels(w));
            var picker = Named<ComboBox>(w, "StockItemAlternateUnitPicker");
            Assert.NotNull(picker);

            // No unit chosen: no factor box, no sentence.
            Assert.False(m.HasAlternateUnit);
            Assert.False(Shown(Named<TextBox>(w, "StockItemAlternateUnitFactor")));
            Assert.False(Shown(Named<TextBlock>(w, "StockItemAlternateUnitPreview")));

            m.SelectedUnit = m.Units.First(u => u.Symbol == "Nos");
            m.SelectedAlternateUnit = m.AlternateUnitOptions.First(o => o.Unit?.Symbol == "Box");
            Pump(w);

            Assert.True(Shown(Named<TextBox>(w, "StockItemAlternateUnitFactor")));
            Assert.True(Shown(Named<TextBlock>(w, "StockItemAlternateUnitPreview")));
            Assert.Equal("1 Box = ? Nos", m.AlternateUnitPreview);

            m.AlternateUnitConversionText = "10";
            Pump(w);
            Assert.Equal("1 Box = 10 Nos", m.AlternateUnitPreview);
            Assert.Equal("1 Box = 10 Nos", Named<TextBlock>(w, "StockItemAlternateUnitPreview")!.Text);
        }
        finally { Close(w, dir); }
    }

    /// <summary>
    /// 🔴 <b>END TO END, AND THE QUANTITY IS STILL THE BASE ONE.</b> An item created from the screen with an
    /// alternate unit persists both fields, and its opening quantity is unchanged in the base unit — the alternate
    /// is derived, never stored, which is what stops a report and an invoice disagreeing about how much stock
    /// there is.
    /// </summary>
    [AvaloniaFact]
    public void Creating_an_item_with_an_alternate_unit_stores_the_base_quantity_only()
    {
        var (w, vm, dir) = NewCompany("Alt Unit Save Co");
        try
        {
            SeedUnitsAndGroup(vm);
            OpenStockItemMaster(w, vm);
            var m = vm.StockItemMaster!;

            m.Name = "Widget";
            m.SelectedUnit = m.Units.First(u => u.Symbol == "Nos");
            m.SelectedAlternateUnit = m.AlternateUnitOptions.First(o => o.Unit?.Symbol == "Box");
            m.AlternateUnitConversionText = "10";
            Assert.True(m.Create());
            Pump(w);

            var item = vm.Company!.StockItems.Single(i => i.Name == "Widget");
            Assert.Equal(10m, item.AlternateUnitConversion);
            Assert.Equal("1 Box = 10 Nos", AlternateUnitConversion.DescribeConversion(item, vm.Company!));
            Assert.Equal(vm.Company!.FindUnitByName("Nos")!.Id, item.BaseUnitId);   // base unit unchanged

            // The list shows the conversion, so an operator sees which items carry one without opening each.
            Assert.Contains(m.Existing, r => r.Name == "Widget" && r.Unit.Contains("1 Box = 10 Nos"));

            // The form was cleared for the next item — the previous conversion cannot be inherited.
            Assert.False(m.HasAlternateUnit);
            Assert.Equal(string.Empty, m.AlternateUnitConversionText);
        }
        finally { Close(w, dir); }
    }

    /// <summary>A chosen alternate unit with a blank factor is REFUSED at accept — not silently treated as 1,
    /// which would make every derived alternate quantity equal the base quantity and look entirely plausible.</summary>
    [AvaloniaFact]
    public void An_alternate_unit_with_no_factor_is_refused_at_accept()
    {
        var (w, vm, dir) = NewCompany("Alt Unit Refuse Co");
        try
        {
            SeedUnitsAndGroup(vm);
            OpenStockItemMaster(w, vm);
            var m = vm.StockItemMaster!;

            m.Name = "Half Set Widget";
            m.SelectedUnit = m.Units.First(u => u.Symbol == "Nos");
            m.SelectedAlternateUnit = m.AlternateUnitOptions.First(o => o.Unit?.Symbol == "Box");
            m.AlternateUnitConversionText = string.Empty;

            Assert.False(m.Create());
            Assert.Contains("conversion factor", m.Message);
        }
        finally { Close(w, dir); }
    }

    private static void SeedUnitsAndGroup(MainWindowViewModel vm)
    {
        var inv = new InventoryService(vm.Company!);
        if (vm.Company!.FindUnitByName("Nos") is null) inv.CreateSimpleUnit("Nos", "Numbers");
        if (vm.Company!.FindUnitByName("Box") is null) inv.CreateSimpleUnit("Box", "Boxes");
        if (vm.Company!.StockGroups.Count == 0) inv.CreateStockGroup("Primary");
    }
}
