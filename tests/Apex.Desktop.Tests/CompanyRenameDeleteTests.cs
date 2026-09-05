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
/// <b>Company Rename and Company Delete</b> (census row 1.4), driven through the real Company Alteration screen
/// and the real <see cref="MainWindow"/> key tunnel.
///
/// <para><b>FIDELITY (R7 / RULING 14 — grounded on the vendor's own help, the corpus being gone).</b>
/// <i>help.tallysolutions.com/…/set-up-company-tally/</i> gives both verbs on one screen: a company is renamed by
/// <i>"press <b>Alt</b>+<b>K</b> (Company) &gt; Alter"</i> and editing the Name, and deleted by
/// <i>"…&gt; Alter. In the Company Alteration screen, press <b>Alt</b>+<b>D</b>."</i></para>
///
/// <para>🔴 <b>THE SCREEN IS THE REFERENCE ONE; ONLY THE ROUTE TO IT IS OURS, AND THAT IS A REPORTED CONFLICT
/// RATHER THAN A DECISION MADE HERE (breadth-design ruling R12, inside open user ruling U-6).</b> <b>Alt+K is
/// already spent</b> on the RQ-8 Saved Views panel, and a build agent must not re-assign a chord on its own
/// authority — so the Alt+K company TOP MENU is <b>not built</b> (census row 14.9 stays open) and both verbs are
/// reached the way this application already reaches that screen: Gateway → Masters → <i>Alter Company</i>. The
/// one chord taken is <b>Alt+D on the Company Alteration screen</b>, which is attested for precisely this and was
/// <b>free</b>: <c>IsDeleteTargetPage</c> excludes <see cref="Screen.AlterCompany"/>, so nothing is displaced.
/// </para>
///
/// <para><b>What this class deliberately does NOT assert.</b> W2-18 also added a "Company" SECTION to the Gateway
/// root and three tests driving it. Both are gone — see the block below the fixture helpers for the two
/// authorities that removed them.</para>
/// </summary>
public sealed class CompanyRenameDeleteTests
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

    // ============================================================ (a) 14.9 — NOT BUILT, and the tests are gone
    //
    // 🔴 W2-18's three Company-menu tests were DELETED on 2026-09-05 rather than fixed, because the feature they
    // drove was removed as UNFAITHFUL. They asserted a "Company" SECTION on the Gateway root holding Create /
    // Alter / Select / Shut. Two independent authorities say the reference product has no such section:
    //   • RULING 14 / R7 — help.tallysolutions.com/…/set-up-company-tally/ reads "press Alt+K (Company) > Create"
    //     and "press Alt+K (Company) > Alter", and …/company-faq-tally/ gives Alt+F3 (Select Company). Every
    //     company verb is on the TOP MENU, not on the Gateway.
    //   • docs/invented-vs-cloned.md IV-29 states the reference Gateway verbatim — Masters · Transactions ·
    //     Utilities · Reports — and its †† 2026-08-17 block records that this very section was added once by
    //     W0-2b and corrected out, diagnosing "the menu GREW A SECTION PER PHASE".
    // The shipped inventory test `GatewayHierarchyTests.Gateway_exposes_the_sections_with_their_items_nested` is
    // what caught it, and it was RIGHT — so the CODE was fixed and that test was left exactly as it stands.
    // Census row 14.9 remains OPEN: what it needs is the Alt+K top-menu shell, whose chord is inside open user
    // ruling U-6 and is not a build agent's to assign.

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

    /// <summary>
    /// 🔴 <b>A STRUCTURAL REACH ASSERTION, in the shape <c>CompanyCaptureReachTests</c> establishes: it makes the
    /// claim a behaviour test CANNOT make, and this one was very nearly missed.</b>
    ///
    /// <para>The test above drives the rename by assigning <c>page.Name</c> from C#. That proves the view model
    /// and the storage move work — it proves <b>nothing at all</b> about whether an operator can type the name,
    /// and on this screen they could not: <c>MainWindow.axaml</c> painted the Name as a read-only
    /// <c>TextBlock</c>, so <c>IsNameEditable</c> returning true reached nobody. <b>A rename only a unit test can
    /// perform is not a shipped capability</b> — it is the same defect as <c>CompanyStorage.Rename</c> sitting
    /// there written, careful and callerless, one layer up.</para>
    ///
    /// <para>So the control itself is pinned: the Company Alteration template's Name row must be a
    /// <c>TextBox</c> with a <b>TwoWay</b> binding. <i>Mutation that reddens it:</i> put the <c>TextBlock</c>
    /// back, or drop <c>Mode=TwoWay</c> so what is typed never reaches the view model.</para>
    /// </summary>
    [Fact]
    public void The_company_alteration_Name_row_is_a_typeable_TwoWay_TextBox()
    {
        var repoRoot = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(ThisFile())!, "..", ".."));
        var xaml = File.ReadAllText(Path.Combine(repoRoot, "src", "Apex.Desktop", "Views", "MainWindow.axaml"));

        // Anchor on the Company Alteration page comment, then take the template that follows it — the file has
        // ~1,400 TextBoxes and a bare search would prove nothing about THIS screen.
        var anchor = xaml.IndexOf("PAGE: Company Alteration", StringComparison.Ordinal);
        Assert.True(anchor >= 0, "the Company Alteration page marker comment is gone — re-anchor this test.");
        var template = xaml.Substring(anchor, Math.Min(4000, xaml.Length - anchor));

        var nameRow = template.IndexOf("Text=\"Name\"", StringComparison.Ordinal);
        Assert.True(nameRow >= 0, "the Company Alteration screen no longer has a row labelled 'Name'.");
        var row = template.Substring(nameRow, Math.Min(400, template.Length - nameRow));

        Assert.Contains("<TextBox", row, StringComparison.Ordinal);
        Assert.Contains("{Binding Name, Mode=TwoWay}", row, StringComparison.Ordinal);
    }

    private static string ThisFile([System.Runtime.CompilerServices.CallerFilePath] string path = "") => path;

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
