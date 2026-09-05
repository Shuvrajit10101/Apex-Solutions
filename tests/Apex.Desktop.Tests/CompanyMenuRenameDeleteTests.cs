using System;
using System.IO;
using System.Linq;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Apex.Ledger.Domain;
using Apex.Desktop.Services;
using Apex.Desktop.ViewModels;
using Apex.Desktop.Views;
using Xunit;

namespace Apex.Desktop.Tests;

/// <summary>
/// W2-18 — the <b>Company menu</b> (census row 14.9) and <b>Company Rename / Company Delete</b> (census row
/// 1.4), driven through the real cascade and the real <see cref="MainWindow"/> key tunnel.
///
/// <para><b>FIDELITY (R7 / RULING 14 — grounded on the vendor's own help, the corpus being gone).</b>
/// <i>help.tallysolutions.com/set-up-company-tally/</i> and <i>…/company-faq-tally/</i> give the menu and both
/// verbs: <i>"press <b>Alt</b>+<b>K</b> (Company) &gt; Create"</i>, <i>"press <b>Alt</b>+<b>K</b> (Company) &gt;
/// Alter"</i>, and for deletion <i>"press <b>Alt</b>+<b>K</b> (Company) &gt; Alter. In the Company Alteration
/// screen, press <b>Alt</b>+<b>D</b>."</i> Selecting and shutting are <b>Alt+F3</b> and <b>Alt+F1</b>.</para>
///
/// <para>🔴 <b>THE CHORD IS NOT TAKEN, AND THAT IS A REPORTED CONFLICT RATHER THAN A DECISION MADE HERE
/// (breadth-design ruling R12, inside open user ruling U-6).</b> <b>Alt+K is already spent</b> on the RQ-8 Saved
/// Views panel, and <b>Alt+F1 / Alt+F3</b> are spent on the report detail toggle and the report period window.
/// A build agent must not re-assign a chord on its own authority, so this slice builds the <b>menu and both
/// verbs</b> and gives them a <b>cascade route</b> — a "Company" group on the Gateway root, which is where this
/// product's navigation lives — and leaves every one of those chords exactly where it is. The one chord taken
/// is <b>Alt+D on the Company Alteration screen</b>, which is attested for precisely this and was <b>free</b>:
/// <c>IsDeleteTargetPage</c> excludes <see cref="Screen.AlterCompany"/>, so nothing is displaced.</para>
/// </summary>
public sealed class CompanyMenuRenameDeleteTests
{
    private static (MainWindow Window, MainWindowViewModel Vm, CompanyStorage Storage, string TempDir) NewWindow()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "ApexCompanyMenu_" + Guid.NewGuid().ToString("N"));
        var storage = new CompanyStorage(tempDir);
        var vm = new MainWindowViewModel(storage);
        var window = new MainWindow { DataContext = vm };
        window.Show();
        return (window, vm, storage, tempDir);
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

    /// <summary>Steps the ACTIVE cascade column's highlight (real Down keys) until it lands on a label.</summary>
    private static void NavigateMenuTo(MainWindowViewModel vm, MainWindow window, string label)
    {
        for (var i = 0; i < vm.Menu.Count + 2; i++)
        {
            if (vm.Menu[vm.SelectedIndex].Label == label) return;
            window.KeyPressQwerty(PhysicalKey.ArrowDown, RawInputModifiers.None);
        }
        Assert.Equal(label, vm.Menu[vm.SelectedIndex].Label);
    }

    // ============================================================ (a) 14.9 — the Company menu column

    /// <summary>
    /// 🔴 <b>THE DRIVING TEST for row 14.9.</b> The Gateway root carries a <b>Company</b> group, and drilling it
    /// (real Down/Enter keys, never a direct method call) opens a column holding the reference product's four
    /// company verbs — Create, Alter, Select, Shut — <b>nested under a section header</b>, never a flat dump.
    ///
    /// <para>Before the slice this fails at the first assertion: the root builder has no Company row at all
    /// (Create Company lives only on the Company-Select screen and "Alter Company" is an orphan under Masters).
    /// </para>
    /// </summary>
    [AvaloniaFact]
    public void The_gateway_root_carries_a_Company_menu_holding_Create_Alter_Select_and_Shut()
    {
        var (window, vm, _, tempDir) = NewWindow();
        try
        {
            vm.NewCompanyName = "Menu Co";
            vm.CreateCompany();
            vm.ShowGateway();

            // The root row exists and is a GROUP (it drills into a column of its own).
            var row = vm.Menu.SingleOrDefault(m => m.Label == "Company");
            Assert.NotNull(row);
            Assert.Equal(MenuItemKind.Group, row!.Kind);

            NavigateMenuTo(vm, window, "Company");
            window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);

            var column = vm.Columns[^1];
            Assert.True(column.IsMenu);
            Assert.Contains(column.Items, i => !i.IsSelectable && i.Label == "Company");
            var items = column.Items.Where(i => i.IsSelectable).Select(i => i.Label).ToList();
            Assert.Equal(
                new[] { "Create Company", "Alter Company", "Select Company", "Shut Company" },
                items);
        }
        finally { Close(window, tempDir); }
    }

    /// <summary>Each of the four rows actually goes somewhere — a menu of dead labels is worse than no menu.</summary>
    [AvaloniaFact]
    public void Company_menu_Alter_opens_the_alteration_screen_and_Select_returns_to_company_select()
    {
        var (window, vm, _, tempDir) = NewWindow();
        try
        {
            vm.NewCompanyName = "Route Co";
            vm.CreateCompany();

            vm.ShowGateway();
            NavigateMenuTo(vm, window, "Company");
            window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
            NavigateMenuTo(vm, window, "Alter Company");
            window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
            Assert.Equal(Screen.AlterCompany, vm.CurrentScreen);
            Assert.NotNull(vm.AlterCompany);

            vm.ShowGateway();
            NavigateMenuTo(vm, window, "Company");
            window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
            NavigateMenuTo(vm, window, "Select Company");
            window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
            Assert.Equal(Screen.CompanySelect, vm.CurrentScreen);
        }
        finally { Close(window, tempDir); }
    }

    /// <summary>
    /// <b>Shut Company</b> closes the OPEN book rather than merely navigating away: the shell's
    /// <see cref="MainWindowViewModel.Company"/> goes null and the status line stops naming it. Navigating to
    /// Company Select without releasing the aggregate is what "Select" does; the two verbs must differ, or one
    /// of them is a lie on the menu.
    /// </summary>
    [AvaloniaFact]
    public void Company_menu_Shut_releases_the_open_company()
    {
        var (window, vm, _, tempDir) = NewWindow();
        try
        {
            vm.NewCompanyName = "Shut Co";
            vm.CreateCompany();
            Assert.NotNull(vm.Company);

            vm.ShowGateway();
            NavigateMenuTo(vm, window, "Company");
            window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
            NavigateMenuTo(vm, window, "Shut Company");
            window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);

            Assert.Null(vm.Company);
            Assert.Equal(Screen.CompanySelect, vm.CurrentScreen);
            Assert.Equal("No company loaded", vm.StatusCompany);
            // The book is still ON DISK — Shut is not Delete.
            Assert.Contains(new CompanyStorage(tempDir).ListCompanies(), c => c.Name == "Shut Co");
        }
        finally { Close(window, tempDir); }
    }

    // ============================================================ (b) 1.4 — RENAME

    /// <summary>
    /// 🔴 <b>THE DRIVING TEST for the rename half of row 1.4.</b> <c>CompanyStorage.Rename</c> is a STORAGE
    /// operation, and the whole reason renaming was carved out of the company-profile slice is stated in
    /// <c>CompanyProfileViewModel.IsNameEditable</c>: the <c>.db</c> path is derived from the NAME and the
    /// company-select list reads each display name back OUT of the filename, so a rename that only rewrote the
    /// stored name would leave the old file standing and put TWO entries carrying one company id in front of the
    /// operator. This asserts the file actually MOVES and the old one is gone.
    /// </summary>
    [Fact]
    public void Rename_moves_the_db_file_and_rewrites_the_stored_name()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ApexRename_" + Guid.NewGuid().ToString("N"));
        try
        {
            var storage = new CompanyStorage(dir);
            var vm = new MainWindowViewModel(storage);
            vm.NewCompanyName = "Old Traders";
            vm.CreateCompany();
            var oldPath = storage.PathForName("Old Traders");
            Assert.True(File.Exists(oldPath));

            var entry = storage.ListCompanies().Single(c => c.Name == "Old Traders");
            var renamed = storage.Rename(entry, "New Traders");

            Assert.Equal("New Traders", renamed.Name);
            Assert.Equal(storage.PathForName("New Traders"), renamed.DatabasePath);
            Assert.True(File.Exists(renamed.DatabasePath));
            Assert.False(File.Exists(oldPath));

            // Exactly ONE company is discoverable, it carries the new name, and the name INSIDE the book was
            // rewritten too — not only the filename.
            var listed = storage.ListCompanies();
            Assert.Single(listed);
            Assert.Equal("New Traders", listed[0].Name);
            Assert.Equal("New Traders", storage.Load(listed[0]).Name);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            try { if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true); } catch { }
        }
    }

    /// <summary>
    /// A rename onto a name that ALREADY has a book is refused, and refused BEFORE anything moves — otherwise
    /// the move would silently overwrite another company's file, which is the data-loss shape
    /// <c>CompanyStorage.Load</c>'s two-row refusal exists to catch after the fact.
    /// </summary>
    [Fact]
    public void Rename_onto_an_existing_name_is_refused_and_moves_nothing()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ApexRenameClash_" + Guid.NewGuid().ToString("N"));
        try
        {
            var storage = new CompanyStorage(dir);
            var vm = new MainWindowViewModel(storage);
            vm.NewCompanyName = "Alpha";
            vm.CreateCompany();
            vm.NewCompanyName = "Beta";
            vm.CreateCompany();

            var alpha = storage.ListCompanies().Single(c => c.Name == "Alpha");
            var ex = Assert.Throws<InvalidOperationException>(() => storage.Rename(alpha, "Beta"));
            Assert.Contains("Beta", ex.Message);

            // Both books survive, untouched.
            Assert.Equal(2, storage.ListCompanies().Count);
            Assert.True(File.Exists(storage.PathForName("Alpha")));
            Assert.True(File.Exists(storage.PathForName("Beta")));
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            try { if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true); } catch { }
        }
    }

    /// <summary>A blank (or whitespace-only) new name is refused: it would sanitise to the literal fallback
    /// "Company" and silently rename the book to something the operator never typed.</summary>
    [Fact]
    public void Rename_to_a_blank_name_is_refused()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ApexRenameBlank_" + Guid.NewGuid().ToString("N"));
        try
        {
            var storage = new CompanyStorage(dir);
            var vm = new MainWindowViewModel(storage);
            vm.NewCompanyName = "Gamma";
            vm.CreateCompany();
            var gamma = storage.ListCompanies().Single();

            Assert.Throws<ArgumentException>(() => storage.Rename(gamma, "   "));
            Assert.Equal("Gamma", storage.ListCompanies().Single().Name);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            try { if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true); } catch { }
        }
    }

    /// <summary>
    /// The rename is REACHABLE: the Name field is editable on the Company Alteration screen (it was deliberately
    /// read-only until this slice), and accepting the screen performs the storage rename. This is the vendor's
    /// own route — you rename a company by altering its Name — rather than an invented "Rename" screen.
    /// </summary>
    [AvaloniaFact]
    public void Renaming_on_the_alteration_screen_moves_the_book_and_keeps_it_open()
    {
        var (window, vm, storage, tempDir) = NewWindow();
        try
        {
            vm.NewCompanyName = "Before Ltd";
            vm.CreateCompany();
            var oldPath = storage.PathForName("Before Ltd");

            vm.ShowAlterCompany();
            var page = vm.AlterCompany!;
            Assert.True(page.IsNameEditable);   // ← the carve-out this slice retires
            page.Name = "After Ltd";
            window.KeyPressQwerty(PhysicalKey.A, RawInputModifiers.Control);

            Assert.Equal("After Ltd", vm.Company!.Name);
            Assert.Equal("After Ltd", vm.StatusCompany);
            Assert.True(File.Exists(storage.PathForName("After Ltd")));
            Assert.False(File.Exists(oldPath));
            Assert.Single(storage.ListCompanies());
        }
        finally { Close(window, tempDir); }
    }

    // ============================================================ (c) 1.4 — DELETE

    /// <summary>
    /// 🔴 <b>THE DRIVING TEST for the delete half of row 1.4.</b> <c>CompanyStorage.Delete</c> shipped with ZERO
    /// callers anywhere in <c>src/</c> — dead code no operator could reach by any sequence of keys. Real
    /// <b>Alt+D</b> on the Company Alteration screen raises the one Y/N confirmation, and a bare <b>Y</b> removes
    /// the book, releases the open company and returns to Company Select.
    /// </summary>
    [AvaloniaFact]
    public void AltD_on_the_alteration_screen_deletes_the_company_after_a_Y_confirmation()
    {
        var (window, vm, storage, tempDir) = NewWindow();
        try
        {
            vm.NewCompanyName = "Doomed Ltd";
            vm.CreateCompany();
            vm.NewCompanyName = "Survivor Ltd";
            vm.CreateCompany();
            Assert.Equal(2, storage.ListCompanies().Count);

            // Re-open the doomed one through the REAL Company-Select menu, then alter it.
            vm.ShowCompanySelect();
            NavigateMenuTo(vm, window, "Doomed Ltd");
            window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
            Assert.Equal("Doomed Ltd", vm.Company!.Name);
            vm.ShowAlterCompany();
            Assert.Equal(Screen.AlterCompany, vm.CurrentScreen);

            window.KeyPressQwerty(PhysicalKey.D, RawInputModifiers.Alt);

            // The question is up and NAMES the company — nothing has been removed yet.
            Assert.True(vm.IsAcceptPromptOpen);
            Assert.Contains("Doomed Ltd", vm.AcceptPromptText);
            Assert.Equal(2, storage.ListCompanies().Count);

            window.KeyPressQwerty(PhysicalKey.Y, RawInputModifiers.None);

            Assert.False(vm.IsAcceptPromptOpen);
            var left = storage.ListCompanies();
            Assert.Single(left);
            Assert.Equal("Survivor Ltd", left[0].Name);
            Assert.Null(vm.Company);
            Assert.Equal(Screen.CompanySelect, vm.CurrentScreen);
        }
        finally { Close(window, tempDir); }
    }

    /// <summary>
    /// <b>N keeps the book.</b> A destructive verb whose "No" is not proven is a coin toss — and this one deletes
    /// a whole company file, not a row.
    /// </summary>
    [AvaloniaFact]
    public void Answering_N_to_the_company_delete_keeps_the_book_and_stays_on_the_screen()
    {
        var (window, vm, storage, tempDir) = NewWindow();
        try
        {
            vm.NewCompanyName = "Spared Ltd";
            vm.CreateCompany();
            vm.ShowAlterCompany();

            window.KeyPressQwerty(PhysicalKey.D, RawInputModifiers.Alt);
            Assert.True(vm.IsAcceptPromptOpen);

            window.KeyPressQwerty(PhysicalKey.N, RawInputModifiers.None);

            Assert.False(vm.IsAcceptPromptOpen);
            Assert.Single(storage.ListCompanies());
            Assert.Equal("Spared Ltd", vm.Company!.Name);
            Assert.Equal(Screen.AlterCompany, vm.CurrentScreen);
        }
        finally { Close(window, tempDir); }
    }

    /// <summary>
    /// 🔴 <b>Alt+D KEEPS ITS OLD MEANING EVERYWHERE ELSE.</b> The company arm is scoped to
    /// <see cref="Screen.AlterCompany"/>, which <c>IsDeleteTargetPage</c> excludes — so the master/voucher Alt+D
    /// is untouched. Proven by standing on the Chart of Accounts and confirming the prompt still names a LEDGER.
    /// </summary>
    [AvaloniaFact]
    public void AltD_elsewhere_still_deletes_the_master_not_the_company()
    {
        var (window, vm, storage, tempDir) = NewWindow();
        try
        {
            vm.NewCompanyName = "Scoped Ltd";
            vm.CreateCompany();

            vm.ShowLedgerMaster();
            vm.LedgerMaster!.Name = "Spare Ledger";
            vm.LedgerMaster!.SelectedGroup = vm.Company!.FindGroupByName("Capital Account");
            window.KeyPressQwerty(PhysicalKey.A, RawInputModifiers.Control);

            vm.ShowChartOfAccounts();
            var chart = vm.ChartOfAccounts!;
            chart.HighlightedIndex = chart.Rows
                .Select((r, i) => (r, i))
                .First(t => t.r.Name.Contains("Spare Ledger", StringComparison.Ordinal)).i;

            window.KeyPressQwerty(PhysicalKey.D, RawInputModifiers.Alt);

            Assert.True(vm.IsAcceptPromptOpen);
            Assert.Contains("Spare Ledger", vm.AcceptPromptText);
            Assert.DoesNotContain("Scoped Ltd", vm.AcceptPromptText);
            // The company file is untouched whatever the operator answers next.
            Assert.Single(storage.ListCompanies());
        }
        finally { Close(window, tempDir); }
    }
}
