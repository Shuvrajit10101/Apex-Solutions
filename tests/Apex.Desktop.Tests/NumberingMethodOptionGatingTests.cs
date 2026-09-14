using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Apex.Desktop.Services;
using Apex.Desktop.ViewModels;
using Apex.Desktop.Views;
using Apex.Ledger;
using Apex.Ledger.Domain;
using Xunit;

namespace Apex.Desktop.Tests;

/// <summary>
/// Census 2.5 / defect <c>T2-21</c> — <b>the numbering options are gated PER METHOD, as the vendor gates them.</b>
///
/// <para><b>What was wrong.</b> The F12 numbering column offered <i>Prevent creating duplicate Voucher Nos</i> and
/// the <i>prefix / suffix / width</i> editors on EVERY numbering method. The vendor offers them on a strict subset,
/// and states the subset as a property of each option
/// (<c>help.tallysolutions.com/tally-prime/accounting/voucher-types-tally/</c>, fetched 2026-09-14):</para>
/// <list type="bullet">
///   <item><i>Set/Alter additional numbering details</i> — <i>"(Applicable to Automatic, Automatic (Manual
///   Override), and Multi-user Auto)"</i> — i.e. <b>never on Manual</b>.</item>
///   <item><i>Prevent creating duplicate Voucher Nos</i> — <i>"(Applicable to Automatic (Manual Override), and
///   Multi-user Auto)"</i>.</item>
/// </list>
///
/// <para>🔴 <b>Why this is a correctness defect and not a cosmetic one.</b> The affixes are what
/// <c>VoucherNumberFormatter.Render</c> prints, so a prefix accepted on a Manual type puts a document number on an
/// issued invoice that the same configuration would never produce in the reference product — a wrong-filed-document
/// risk, which is how the census grades it.</para>
///
/// <para>These assert the <b>REALISED VISUAL TREE</b> of the real headless <see cref="MainWindow"/>, not a view-model
/// flag, so they fail on a tree where the predicate exists but the XAML still renders the control.</para>
/// </summary>
public sealed class NumberingMethodOptionGatingTests
{
    private static (MainWindow Window, MainWindowViewModel Vm, string Dir) NewWindow(string company)
    {
        var dir = Path.Combine(Path.GetTempPath(), "ApexNumGate_" + Guid.NewGuid().ToString("N"));
        var vm = new MainWindowViewModel(new CompanyStorage(dir));
        var window = new MainWindow { DataContext = vm, Width = 1920, Height = 1080 };
        window.Show();
        vm.NewCompanyName = company;
        vm.CreateCompany();
        vm.ShowGateway();
        Pump(window);
        return (window, vm, dir);
    }

    private static void Pump(Window window)
    {
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
    }

    private static IEnumerable<Visual> Descendants(Visual v)
    {
        foreach (var c in v.GetVisualChildren())
        {
            yield return c;
            foreach (var g in Descendants(c)) yield return g;
        }
    }

    private static void Cleanup(Window window, string dir)
    {
        window.Close();
        try
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
        catch (IOException) { /* best effort */ }
    }

    /// <summary>
    /// The captions of every control that is actually VISIBLE in the rendered tree — a control whose
    /// <c>IsVisible</c> is false, or any of whose ancestors' is, does not count as offered to the operator.
    /// </summary>
    private static List<string> VisibleTexts(Window window)
    {
        var texts = new List<string>();
        foreach (var v in Descendants(window))
        {
            if (!IsEffectivelyVisible(v)) continue;
            switch (v)
            {
                case TextBlock tb when !string.IsNullOrEmpty(tb.Text): texts.Add(tb.Text!); break;
                case CheckBox cb when cb.Content is string s: texts.Add(s); break;
            }
        }
        return texts;
    }

    private static bool IsEffectivelyVisible(Visual v)
    {
        for (Visual? cur = v; cur is not null; cur = cur.GetVisualParent())
            if (!cur.IsVisible) return false;
        return true;
    }

    private static VoucherType AddType(Company c, string name, NumberingMethod method)
    {
        var t = new VoucherType(Guid.NewGuid(), name, VoucherBaseType.Journal, numbering: method);
        c.AddVoucherType(t);
        return t;
    }

    // ================================================================ the rendered form, method by method

    /// <summary>
    /// 🔴 THE CENTRAL CASE, AND IT FAILS ON TODAY'S MAIN. On a <b>Manual</b> type the vendor offers neither the
    /// prefix/suffix editors nor Prevent-duplicate; before this change both were on screen.
    /// </summary>
    [AvaloniaTheory]
    [InlineData(NumberingMethod.Manual, false, false)]
    [InlineData(NumberingMethod.None, false, false)]
    [InlineData(NumberingMethod.Automatic, true, false)]
    [InlineData(NumberingMethod.AutomaticManualOverride, true, true)]
    [InlineData(NumberingMethod.MultiUserAuto, true, true)]
    public void The_numbering_form_offers_exactly_the_options_the_vendor_gives_that_method(
        NumberingMethod method, bool expectAffixes, bool expectPreventDuplicate)
    {
        var (window, vm, dir) = NewWindow("Num Gate " + method);
        try
        {
            var type = AddType(vm.Company!, "Gate " + method, method);
            vm.OpenVoucherNumberingConfig(type.Id);
            Pump(window);
            Assert.Equal(Screen.VoucherNumberingConfig, vm.CurrentScreen);
            Assert.Equal(type.Id, vm.VoucherNumberingConfig!.SelectedType!.Id);

            var visible = VisibleTexts(window);

            Assert.Equal(expectAffixes, visible.Contains("PREFIX DETAILS"));
            Assert.Equal(expectAffixes, visible.Contains("SUFFIX DETAILS"));
            Assert.Equal(expectAffixes, visible.Contains("Width of numerical part"));
            Assert.Equal(expectPreventDuplicate, visible.Contains("Prevent duplicate numbers"));

            // The view-model predicates and the rendered tree agree — so a later reader cannot "fix" one of them
            // in isolation and leave the other telling the operator something different.
            Assert.Equal(expectAffixes, vm.VoucherNumberingConfig!.CanConfigureAffixes);
            Assert.Equal(expectPreventDuplicate, vm.VoucherNumberingConfig!.CanPreventDuplicate);

            // The Method itself is always shown — the operator must be able to see WHY the options differ.
            Assert.Contains("Method", visible);
        }
        finally { Cleanup(window, dir); }
    }

    // ================================================================ the gate follows the SELECTION

    /// <summary>
    /// Stepping the N1 list from a method that offers an option to one that does not must re-render. Without the
    /// <c>OnPropertyChanged(nameof(CanPreventDuplicate))</c> in <c>LoadFromType</c> the box keeps the PREVIOUS
    /// type's visibility — the defect being fixed, reintroduced through stale notification rather than a wrong
    /// predicate, and invisible to a test that only ever opens one type.
    ///
    /// <para><b>Mutation-verified:</b> deleting that notification line reddens exactly this test.</para>
    /// </summary>
    [AvaloniaFact]
    public void Changing_the_selected_type_re_renders_the_gate()
    {
        var (window, vm, dir) = NewWindow("Num Gate Switch Co");
        try
        {
            var multiUser = AddType(vm.Company!, "Gate MultiUser", NumberingMethod.MultiUserAuto);
            var manual = AddType(vm.Company!, "Gate Manual", NumberingMethod.Manual);

            vm.OpenVoucherNumberingConfig(multiUser.Id);
            Pump(window);
            Assert.Contains("Prevent duplicate numbers", VisibleTexts(window));
            Assert.Contains("PREFIX DETAILS", VisibleTexts(window));

            vm.VoucherNumberingConfig!.SelectByTypeId(manual.Id);
            Pump(window);
            Assert.DoesNotContain("Prevent duplicate numbers", VisibleTexts(window));
            Assert.DoesNotContain("PREFIX DETAILS", VisibleTexts(window));

            // …and back, so the gate is a live function of the selection and not a one-way latch.
            vm.VoucherNumberingConfig!.SelectByTypeId(multiUser.Id);
            Pump(window);
            Assert.Contains("Prevent duplicate numbers", VisibleTexts(window));
        }
        finally { Cleanup(window, dir); }
    }

    // ================================================================ a hidden control is not a silent writer

    /// <summary>
    /// 🔴 THE HALF THAT WOULD HAVE MADE THE FIX ITSELF A DEFECT. Gating only the VIEW leaves <c>Commit</c>
    /// assigning from working copies the operator can no longer see or correct — so saving an unrelated change on
    /// a Manual type would REWRITE its stored affixes, and the affixes are what the printed document number is made
    /// of. The stored value must survive a save untouched.
    ///
    /// <para><b>Mutation-verified:</b> replacing <c>if (CanConfigureAffixes)</c> in <c>Commit</c> with an
    /// unconditional assignment reddens exactly this test.</para>
    /// </summary>
    [AvaloniaFact]
    public void Saving_a_manual_type_does_not_rewrite_the_affixes_the_screen_no_longer_offers()
    {
        var (window, vm, dir) = NewWindow("Num Gate NoWrite Co");
        try
        {
            var c = vm.Company!;
            // A Manual type that ALREADY carries a prefix — the shape an imported book, or a type whose method was
            // changed on the Voucher Type master after its numbering was configured, arrives in.
            var manual = new VoucherType(Guid.NewGuid(), "Legacy Manual", VoucherBaseType.Journal,
                numbering: NumberingMethod.Manual, numberWidth: 4, prefillWithZero: true,
                prefixes: new[] { new VoucherNumberAffix(Guid.NewGuid(), c.BooksBeginFrom, "LEGACY/") });
            c.AddVoucherType(manual);

            vm.OpenVoucherNumberingConfig(manual.Id);
            Pump(window);

            // The operator cannot even see these — they are off the form — but the working copies exist. A save
            // must not push them (or a blank) back over the stored config.
            Assert.False(vm.VoucherNumberingConfig!.CanConfigureAffixes);
            Assert.Equal(NumberingSaveResult.Saved, vm.VoucherNumberingConfig!.Save());

            var stored = c.FindVoucherType(manual.Id)!;
            Assert.Equal("LEGACY/", stored.Prefixes.Single().Particulars);
            Assert.Equal(4, stored.NumberWidth);
            Assert.True(stored.PrefillWithZero);
        }
        finally { Cleanup(window, dir); }
    }

    /// <summary>
    /// The same invariant for <c>PreventDuplicate</c> on a plain <b>Automatic</b> type, where the vendor does not
    /// offer the check: a save that changes nothing else must not flip a validation rule nobody touched.
    ///
    /// <para><b>Mutation-verified:</b> replacing <c>if (CanPreventDuplicate)</c> in <c>Commit</c> with an
    /// unconditional assignment reddens exactly this test.</para>
    /// </summary>
    [AvaloniaFact]
    public void Saving_an_automatic_type_does_not_clear_a_prevent_duplicate_flag_it_never_showed()
    {
        var (window, vm, dir) = NewWindow("Num Gate Flag Co");
        try
        {
            var c = vm.Company!;
            var auto = AddType(c, "Legacy Automatic", NumberingMethod.Automatic);
            auto.PreventDuplicate = true;              // arrived set (import, or a method change on the master)

            vm.OpenVoucherNumberingConfig(auto.Id);
            Pump(window);
            Assert.False(vm.VoucherNumberingConfig!.CanPreventDuplicate);
            Assert.DoesNotContain("Prevent duplicate numbers", VisibleTexts(window));

            vm.VoucherNumberingConfig!.WidthText = "2";   // an UNRELATED, offered change
            Assert.Equal(NumberingSaveResult.Saved, vm.VoucherNumberingConfig!.Save());

            var stored = c.FindVoucherType(auto.Id)!;
            Assert.Equal(2, stored.NumberWidth);          // the offered change landed…
            Assert.True(stored.PreventDuplicate);         // …and the unoffered one was left alone
        }
        finally { Cleanup(window, dir); }
    }

    // ================================================================ the fifth method is NOT ours — do not delete it

    /// <summary>
    /// 🔴 A NEGATIVE RESULT, PINNED SO IT IS NOT "CORRECTED" AWAY. The wave brief for this row asserted that
    /// <see cref="NumberingMethod.None"/> is a fifth method <i>"the vendor does not have"</i> and invited its
    /// removal. <b>That is wrong, and the code was right.</b> <c>None</c> IS vendor-attested — the Voucher Type
    /// master page omits it, the dedicated voucher-numbering-methods page carries it (<i>"you can also disable the
    /// voucher numbering by selecting the None option"</i>) — and census row 5.10 already records that stopping at
    /// the first page would have filed a correct behaviour as ours. The method therefore stays, and stays
    /// selectable.
    /// </summary>
    [AvaloniaFact]
    public void The_None_numbering_method_is_retained_and_selectable()
    {
        var (window, vm, dir) = NewWindow("Num None Co");
        try
        {
            Assert.Contains(NumberingMethod.None, Enum.GetValues<NumberingMethod>());

            var none = AddType(vm.Company!, "No Numbering", NumberingMethod.None);
            vm.OpenVoucherNumberingConfig(none.Id);
            Pump(window);

            // It renders, and it renders as None — it is not silently coerced to Automatic on the way to the screen.
            Assert.Equal("None", vm.VoucherNumberingConfig!.MethodDisplay);
            Assert.Contains("None", VisibleTexts(window));
        }
        finally { Cleanup(window, dir); }
    }
}
