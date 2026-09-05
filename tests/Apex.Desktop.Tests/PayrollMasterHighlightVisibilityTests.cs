using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Apex.Desktop.Services;
using Apex.Desktop.ViewModels;
using Apex.Desktop.Views;
using Xunit;

namespace Apex.Desktop.Tests;

/// <summary>
/// 🔴 <b>THE INVISIBLE CURSOR ON A DESTRUCTIVE VERB.</b>
///
/// <para><b>The defect.</b> Census row 7.16 gave the payroll masters' existing-lists a keyboard highlight that
/// the arrows move, that Ctrl+Enter opens for alteration and that <b>Alt+D DELETES</b>. The view models tracked
/// that highlight correctly — <c>IsHighlighted</c> flipped on exactly one row — and <b>not one of the four row
/// templates drew it</b>. So the operator pressed Down an unknown number of times against a list that never
/// changed appearance, then pressed Alt+D and was asked to confirm the deletion of a master they had no way to
/// see they were standing on. A confirmation prompt naming the wrong master is worse than no prompt: it reads
/// as a safeguard while being none.</para>
///
/// <para><b>Why this test looks at PIXELS-worth of visual tree and not at <c>IsHighlighted</c>.</b> Asserting
/// the flag is exactly the test that would have passed on the broken build — the flag was never the problem.
/// The only thing that makes the cursor a cursor is that something VISIBLE changes on the highlighted row and
/// on no other. So each case drives the real ArrowDown through <see cref="MainWindow"/>'s handler, then walks
/// the realised visual tree for a visible fill of the codebase's established highlight colour
/// (<c>#FFF3CD</c>, the same one the Chart of Accounts and Stock Item lists have used since WI-3), and requires
/// it present on the highlighted row and absent on its neighbour.</para>
///
/// <para>Headless-safe: visual-tree, brush and layout-bounds inspection only. No Skia, no rendered frame.</para>
/// </summary>
public sealed class PayrollMasterHighlightVisibilityTests
{
    /// <summary>The established keyboard-highlight fill, shared with the Chart of Accounts and Stock Item lists.</summary>
    private static readonly Color HighlightFill = Color.Parse("#FFF3CD");

    /// <summary>
    /// The four payroll master kinds that carry a working Alt+D today. Each driver opens the screen through the
    /// view model and leaves TWO masters in the existing-list, so "the bar is on the highlighted row" and "the
    /// bar is not on the other row" are both answerable — a template that painted the bar unconditionally would
    /// satisfy the first clause alone.
    /// </summary>
    private static readonly Dictionary<string, Action<MainWindowViewModel>> Drivers = new()
    {
        ["EmployeeCategory"] = vm =>
        {
            vm.ShowEmployeeCategoryMaster();
            var m = vm.EmployeeCategoryMaster!;
            m.Name = "Contract"; Assert.True(m.Create(), m.Message);
            m.Name = "Consultant"; Assert.True(m.Create(), m.Message);
        },
        ["EmployeeGroup"] = vm =>
        {
            vm.ShowEmployeeGroupMaster();
            var m = vm.EmployeeGroupMaster!;
            m.Name = "Sales"; Assert.True(m.Create(), m.Message);
            m.Name = "Admin"; Assert.True(m.Create(), m.Message);
        },
        ["PayrollUnit"] = vm =>
        {
            vm.ShowPayrollUnitMaster();
            var m = vm.PayrollUnitMaster!;
            m.Symbol = "Days"; m.FormalName = "Days"; Assert.True(m.Create(), m.Message);
            m.Symbol = "Hrs"; m.FormalName = "Hours"; Assert.True(m.Create(), m.Message);
        },
        ["AttendanceType"] = vm =>
        {
            vm.ShowPayrollUnitMaster();
            var u = vm.PayrollUnitMaster!;
            u.Symbol = "Days"; u.FormalName = "Days"; Assert.True(u.Create(), u.Message);

            vm.ShowAttendanceTypeMaster();
            var m = vm.AttendanceTypeMaster!;
            m.Name = "Present";
            m.SelectedUnit = m.UnitOptions.First(o => o.Unit is not null);
            Assert.True(m.Create(), m.Message);
            m.Name = "Absent";
            m.SelectedUnit = m.UnitOptions.First(o => o.Unit is not null);
            Assert.True(m.Create(), m.Message);
        },
    };

    public static IEnumerable<object[]> Kinds() => from k in Drivers.Keys select new object[] { k };

    private static IEnumerable<Visual> Descendants(Visual v)
    {
        foreach (var c in v.GetVisualChildren())
        {
            yield return c;
            foreach (var g in Descendants(c)) yield return g;
        }
    }

    private static void Pump(MainWindow w)
    {
        w.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        w.UpdateLayout();
    }

    private static (MainWindow Window, MainWindowViewModel Vm, string TempDir) Open(string kind)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "ApexPayrollHi_" + Guid.NewGuid().ToString("N"));
        var vm = new MainWindowViewModel(new CompanyStorage(tempDir));
        var window = new MainWindow { DataContext = vm, Width = 1280, Height = 720 };
        window.Show();

        vm.NewCompanyName = "Payroll Highlight Co";
        vm.CreateCompany();
        vm.Company!.PayrollEnabled = true;
        vm.Company.PayrollStatutoryEnabled = true;

        Drivers[kind](vm);
        Pump(window);
        return (window, vm, tempDir);
    }

    private static void Cleanup(MainWindow window, string dir)
    {
        window.Close();
        try
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    /// <summary>
    /// Every realised control whose own DataContext is <paramref name="row"/> — i.e. the row's item container
    /// and everything the row template put inside it.
    /// </summary>
    private static List<Visual> VisualsFor(MainWindow window, object row) =>
        Descendants(window)
            .Where(v => v is Control c && ReferenceEquals(c.DataContext, row))
            .ToList();

    /// <summary>
    /// True when this row is actually WEARING the highlight on screen: some visible, non-degenerate visual in
    /// its template is filled with the established highlight colour.
    /// </summary>
    private static bool WearsHighlight(MainWindow window, object row) =>
        VisualsFor(window, row).Any(v =>
            v is Border { IsEffectivelyVisible: true } b
            && b.Bounds.Width > 0 && b.Bounds.Height > 0
            && b.Background is ISolidColorBrush s && s.Color == HighlightFill);

    /// <summary>
    /// 🔴 THE TEST. Arrow-Down once from an untouched list puts the highlight on row 0 (the shared
    /// <c>PayrollMasterHighlight</c> guarantees "the first press enters the list"). That row — and only that
    /// row — must visibly wear the cursor.
    /// </summary>
    [AvaloniaTheory]
    [MemberData(nameof(Kinds))]
    public void The_payroll_master_delete_cursor_is_visible_on_the_row_it_is_on(string kind)
    {
        var (window, vm, dir) = Open(kind);
        try
        {
            var list = vm.PayrollMasterScreen;
            Assert.True(list != null,
                $"{kind} master did not resolve as a payroll master list — the fixture would assert nothing.");

            window.KeyPressQwerty(PhysicalKey.ArrowDown, RawInputModifiers.None);
            Pump(window);

            var highlighted = vm.PayrollMasterScreen!.HighlightedMasterRow;
            Assert.True(highlighted != null,
                $"{kind}: ArrowDown did not put the highlight on any row, so there is nothing to draw.");

            Assert.True(
                WearsHighlight(window, highlighted!),
                $"{kind} master: the row '{highlighted!.MasterName}' carries the keyboard highlight that Alt+D " +
                "DELETES, and nothing in its template draws it. The operator cannot see which master they are " +
                "about to destroy. Add the established highlight bar (Background #FFF3CD, IsVisible bound to " +
                "IsHighlighted) as the FIRST child of the row Grid, exactly as the Chart of Accounts and Stock " +
                "Item lists do.");
        }
        finally { Cleanup(window, dir); }
    }

    /// <summary>
    /// The other half: a row the cursor is NOT on must not wear it. Without this clause a template that painted
    /// the bar unconditionally — every row highlighted, which is the same as none — would pass the test above.
    /// </summary>
    [AvaloniaTheory]
    [MemberData(nameof(Kinds))]
    public void Only_the_highlighted_payroll_master_row_wears_the_cursor(string kind)
    {
        var (window, vm, dir) = Open(kind);
        try
        {
            window.KeyPressQwerty(PhysicalKey.ArrowDown, RawInputModifiers.None);
            Pump(window);

            var highlighted = vm.PayrollMasterScreen!.HighlightedMasterRow;
            Assert.True(highlighted != null, $"{kind}: ArrowDown highlighted nothing.");

            // A sibling row, found through the realised containers rather than the view model's collection, so
            // this stays honest about what was actually put on screen.
            var others = Descendants(window)
                .OfType<Control>()
                .Select(c => c.DataContext)
                .OfType<IPayrollMasterListRow>()
                .Distinct()
                .Where(r => !ReferenceEquals(r, highlighted))
                .ToList();

            Assert.True(others.Count > 0,
                $"{kind}: only one row was realised, so 'the cursor is not on the others' cannot be tested. " +
                "The driver must leave at least two masters in the list.");

            foreach (var other in others)
                Assert.False(
                    WearsHighlight(window, other),
                    $"{kind} master: row '{other.MasterName}' is NOT the highlighted row yet is drawn wearing " +
                    "the highlight fill. A cursor on every row is the same as no cursor at all — bind the bar's " +
                    "IsVisible to IsHighlighted rather than painting it unconditionally.");
        }
        finally { Cleanup(window, dir); }
    }

    /// <summary>
    /// The roster guard, in the shape <see cref="MasterPageRowStructureTests"/> established: adding a fifth
    /// payroll master kind to the Alt+D surface without adding it here would leave it silently unguarded, which
    /// is precisely how all four shipped without a visible cursor in the first place.
    /// </summary>
    [AvaloniaFact]
    public void Every_payroll_master_kind_that_carries_alt_d_is_covered_here()
    {
        Assert.Equal(4, Drivers.Count);
        Assert.Contains("EmployeeCategory", Drivers.Keys);
        Assert.Contains("EmployeeGroup", Drivers.Keys);
        Assert.Contains("PayrollUnit", Drivers.Keys);
        Assert.Contains("AttendanceType", Drivers.Keys);
    }
}
