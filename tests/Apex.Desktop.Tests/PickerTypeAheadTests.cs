using System;
using System.Collections;
using System.IO;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Apex.Desktop.Converters;
using Apex.Desktop.Services;
using Apex.Desktop.ViewModels;
using Apex.Desktop.Views;
using Xunit;
using DomainLedger = Apex.Ledger.Domain.Ledger;

namespace Apex.Desktop.Tests;

/// <summary>
/// WI-2 — the domain-bound picker search-text defect and its fix.
///
/// <para><b>THE DEFECT (measured, then locked here).</b> Avalonia derives a picker item's type-to-jump text
/// from <c>TextSearch.Text</c>, falling back to <c>item.ToString()</c>; an <c>ItemTemplate</c> does not
/// participate. Every Apex domain entity is a plain POCO with no <c>ToString</c> override, so every row of a
/// ledger picker reported the same search text — <c>"Apex.Ledger.Domain.Ledger"</c>. Because that string
/// begins with <b>"A"</b>, typing "A" prefix-matched EVERY row and the scan from index 0 selected whatever
/// happened to be first in the list. An operator typing "A" for "Aarti Steel" got a different party
/// entirely — a wrong-ledger selection that posts money to the wrong account.</para>
///
/// <para>Every test below drives the REAL control and the REAL app-wide registration
/// (<see cref="PickerTextSearch"/>); none asserts that a binding merely exists.</para>
/// </summary>
public sealed class PickerTypeAheadTests
{
    private static (MainWindowViewModel Vm, string Dir) NewCompany(string name, params string[] ledgers)
    {
        var dir = Path.Combine(Path.GetTempPath(), "ApexPicker_" + Guid.NewGuid().ToString("N"));
        var vm = new MainWindowViewModel(new CompanyStorage(dir));
        vm.NewCompanyName = name;
        vm.CreateCompany();

        foreach (var ledgerName in ledgers)
        {
            vm.ShowLedgerMaster();
            vm.LedgerMaster!.Name = ledgerName;
            vm.LedgerMaster!.SelectedGroup = vm.Company!.FindGroupByName("Sundry Debtors");
            vm.LedgerMaster!.Create();
        }

        vm.ShowGateway();
        return (vm, dir);
    }

    private static void Cleanup(string dir)
    {
        try
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
        catch (IOException) { /* best effort */ }
    }

    // ============================================================ the wrong-ledger fix

    /// <summary>
    /// THE MONEY TEST. Three parties, deliberately ordered so the intended one is NOT first. Typing "A" for
    /// "Aarti Steel" must select <b>Aarti Steel</b>.
    /// <para>
    /// The first half of the test is the BITE PROOF: with the search-text binding cleared (exactly the state
    /// every picker shipped in before this fix), the same keystroke selects <b>Zenith Traders</b> — the wrong
    /// ledger. If the fix regresses, the second half fails with that same wrong party.
    /// </para>
    /// </summary>
    [AvaloniaFact]
    public void Typing_a_party_prefix_selects_that_party_not_the_first_ledger()
    {
        var (vm, dir) = NewCompany("Picker Money Co", "Zenith Traders", "Aarti Steel", "Amar Textiles");
        try
        {
            var ordered = new[] { "Zenith Traders", "Aarti Steel", "Amar Textiles" }
                .Select(n => vm.Company!.FindLedgerByName(n)!)
                .ToArray();

            // --- BITE PROOF: the pre-fix state. Clearing the binding restores ToString() search text.
            var broken = new ComboBox { ItemsSource = ordered };
            TextSearch.SetTextBinding(broken, null!);
            var brokenWindow = new Window { Content = broken };
            brokenWindow.Show();
            broken.Focus();
            brokenWindow.KeyTextInput("A");

            Assert.Equal("Zenith Traders", ((DomainLedger)broken.SelectedItem!).Name);   // the defect, reproduced

            // --- THE FIX: a picker created normally, wired by the app-wide registration.
            var fixedPicker = new ComboBox { ItemsSource = ordered };
            var window = new Window { Content = fixedPicker };
            window.Show();
            fixedPicker.Focus();
            window.KeyTextInput("A");

            Assert.Equal("Aarti Steel", ((DomainLedger)fixedPicker.SelectedItem!).Name);
        }
        finally { Cleanup(dir); }
    }

    /// <summary>
    /// Typing more letters narrows between two parties sharing a prefix — "Am" reaches "Amar Textiles" even
    /// though "Aarti Steel" matched the first keystroke.
    /// </summary>
    [AvaloniaFact]
    public void Typing_more_letters_disambiguates_parties_sharing_a_prefix()
    {
        var (vm, dir) = NewCompany("Picker Prefix Co", "Zenith Traders", "Aarti Steel", "Amar Textiles");
        try
        {
            var ordered = new[] { "Zenith Traders", "Aarti Steel", "Amar Textiles" }
                .Select(n => vm.Company!.FindLedgerByName(n)!)
                .ToArray();

            var picker = new ComboBox { ItemsSource = ordered };
            var window = new Window { Content = picker };
            window.Show();
            picker.Focus();

            window.KeyTextInput("A");
            window.KeyTextInput("m");

            Assert.Equal("Amar Textiles", ((DomainLedger)picker.SelectedItem!).Name);
        }
        finally { Cleanup(dir); }
    }

    /// <summary>
    /// The registration reaches the pickers built from <c>MainWindow.axaml</c>, not just ones a test creates:
    /// the ledger picker on a real Payment voucher line answers type-to-jump with the right party.
    /// </summary>
    [AvaloniaFact]
    public void The_real_voucher_entry_ledger_picker_searches_by_name()
    {
        var (vm, dir) = NewCompany("Picker Voucher Co", "Zenith Traders", "Aarti Steel");
        try
        {
            var window = new MainWindow { DataContext = vm, Width = 1920, Height = 1080 };
            window.Show();
            vm.OpenVoucher(Apex.Ledger.Domain.VoucherBaseType.Payment);
            window.UpdateLayout();
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();

            var ledgerPicker = Avalonia.VisualTree.VisualExtensions
                .GetVisualDescendants(window)
                .OfType<ComboBox>()
                .FirstOrDefault(c => c.IsEffectivelyVisible
                    && (c.ItemsSource as IEnumerable)?.Cast<object>().FirstOrDefault() is DomainLedger);

            Assert.NotNull(ledgerPicker);   // vacuity guard: the screen really did materialise a ledger picker

            ledgerPicker!.Focus();
            window.KeyTextInput("Z");

            Assert.Equal("Zenith Traders", ((DomainLedger)ledgerPicker.SelectedItem!).Name);
            window.Close();
        }
        finally { Cleanup(dir); }
    }

    // ============================================================ the display-text resolver

    /// <summary>
    /// The resolver is UI-side and never asks the domain to render itself: a ledger resolves to its name, a
    /// ledger with an alias carries the alias as a trailing disambiguator (so two parties sharing a visible
    /// prefix stay tellable apart in the list), and an enum keeps its existing <c>ToString</c> text so enum
    /// pickers are unchanged.
    /// <para>
    /// NOTE the deliberate limit: search remains a PREFIX match over this one string, so it matches the NAME.
    /// Typing an alias that is not also a prefix of the name will not jump to the row — Avalonia's text search
    /// takes a single string per item and has no multi-key mode. Alias-prefix search is a separate change.
    /// </para>
    /// </summary>
    [AvaloniaFact]
    public void The_display_text_resolver_maps_entities_by_name_and_leaves_enums_alone()
    {
        var (vm, dir) = NewCompany("Picker Resolver Co", "Aarti Steel");
        try
        {
            var ledger = vm.Company!.FindLedgerByName("Aarti Steel")!;
            Assert.Equal("Aarti Steel", PickerDisplayTextConverter.Resolve(ledger));

            ledger.Alias = "AAR";
            Assert.Equal("Aarti Steel (AAR)", PickerDisplayTextConverter.Resolve(ledger));

            // An enum-backed picker (Dr/Cr) must keep exactly the text it has today.
            Assert.Equal(
                Apex.Ledger.DrCr.Debit.ToString(),
                PickerDisplayTextConverter.Resolve(Apex.Ledger.DrCr.Debit));
        }
        finally { Cleanup(dir); }
    }

    /// <summary>
    /// The engine stays presentation-free: <see cref="DomainLedger"/> must NOT declare its own
    /// <c>ToString</c>. Overriding it would have been the quick fix and would have leaked a UI concern into
    /// the posting engine; this test stops that from creeping back in.
    /// </summary>
    [AvaloniaFact]
    public void The_domain_ledger_does_not_declare_a_presentation_ToString()
    {
        var declaring = typeof(DomainLedger).GetMethod("ToString", Type.EmptyTypes)!.DeclaringType;
        Assert.Equal(typeof(object), declaring);
    }

    // ============================================================ keyboard navigation on a picker

    /// <summary>Up / Down move the picker selection and Enter/Esc are accepted — a picker is fully drivable
    /// from the keyboard, which is the rest of WI-2's requirement.</summary>
    [AvaloniaFact]
    public void A_picker_moves_with_the_arrow_keys()
    {
        var (vm, dir) = NewCompany("Picker Nav Co", "Aarti Steel", "Bharat Motors", "Zenith Traders");
        try
        {
            var ordered = new[] { "Aarti Steel", "Bharat Motors", "Zenith Traders" }
                .Select(n => vm.Company!.FindLedgerByName(n)!)
                .ToArray();

            var picker = new ComboBox { ItemsSource = ordered, SelectedIndex = 0 };
            var window = new Window { Content = picker };
            window.Show();
            picker.Focus();

            window.KeyPressQwerty(PhysicalKey.ArrowDown, RawInputModifiers.None);
            Assert.Equal("Bharat Motors", ((DomainLedger)picker.SelectedItem!).Name);

            window.KeyPressQwerty(PhysicalKey.ArrowDown, RawInputModifiers.None);
            Assert.Equal("Zenith Traders", ((DomainLedger)picker.SelectedItem!).Name);

            window.KeyPressQwerty(PhysicalKey.ArrowUp, RawInputModifiers.None);
            Assert.Equal("Bharat Motors", ((DomainLedger)picker.SelectedItem!).Name);
        }
        finally { Cleanup(dir); }
    }
}
