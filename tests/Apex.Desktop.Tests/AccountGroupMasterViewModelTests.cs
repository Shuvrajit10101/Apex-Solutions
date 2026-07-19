using System;
using System.IO;
using System.Linq;
using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Reports;
using Apex.Desktop.Services;
using Apex.Desktop.ViewModels;
using DomainLedger = Apex.Ledger.Domain.Ledger;

namespace Apex.Desktop.Tests;

/// <summary>
/// WI-7 — the accounting-Group creation master surfaced under Masters → Create → Group. Proves:
/// <list type="bullet">
///   <item>the <b>mis-wire fix</b>: activating "Group" from the Gateway → Create menu opens the Group master
///     (<see cref="Screen.AccountGroupMaster"/>), NOT the Ledger master — driven by real navigation from the ROOT
///     column (a <c>ShowXMaster()</c> call would prove nothing about reachability);</item>
///   <item>a custom group ("Salary Payable" under Current Liabilities) is created through the master, its
///     <b>nature is DERIVED read-only from the parent</b> (never chosen), and it persists across a reload;</item>
///   <item>the new group is offered in the Ledger master's Under-picker, and a ledger under it prints on the
///     <b>liabilities</b> side of the Balance Sheet, grouped under "Salary Payable";</item>
///   <item>the master validates (blank name, duplicate) and opens as exactly ONE page column.</item>
/// </list>
/// Drives the real shell view models over a throwaway .db — no UI toolkit.
/// </summary>
public sealed class AccountGroupMasterViewModelTests : IDisposable
{
    private readonly string _tempDir;
    private readonly CompanyStorage _storage;

    public AccountGroupMasterViewModelTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "ApexGroupMasterTests_" + Guid.NewGuid().ToString("N"));
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

    /// <summary>Walks the ACTIVE cascade column with Down until the highlighted row is <paramref name="label"/>.</summary>
    private static void SelectActiveItem(MainWindowViewModel vm, string label)
    {
        for (var i = 0; i < vm.Menu.Count + 2; i++)
        {
            if (vm.Menu[vm.SelectedIndex].Label == label) return;
            vm.MoveDown();
        }
        Assert.Fail($"menu item '{label}' was not reachable by arrow navigation");
    }

    // ---------------------------------------------------------------- (1) the mis-wire fix + reachability

    [Fact]
    public void Create_Group_menu_opens_the_group_master_not_the_ledger_master()
    {
        var vm = NewSeededCompany("Group Mis-wire Co");

        // Navigate from the ROOT: Create → Group, activating each with the real keyboard drill (Right/Enter).
        SelectActiveItem(vm, "Create");
        vm.DrillIn();                              // Create submenu column is now active
        Assert.Equal(GatewayMenu.Create, vm.CurrentGatewayMenu);

        SelectActiveItem(vm, "Group");
        vm.DrillIn();                              // activate "Group" → dispatch → ShowAccountGroupMaster

        // The mis-wire is fixed: "Group" opens the Group master, NOT the Ledger master.
        Assert.Equal(Screen.AccountGroupMaster, vm.CurrentScreen);
        Assert.NotNull(vm.AccountGroupMaster);
        Assert.Null(vm.LedgerMaster);
        // Reachable as the rightmost page column, hosting the Group master.
        Assert.Same(vm.AccountGroupMaster, vm.Columns[^1].AccountGroupMaster);
        Assert.Equal("Group Creation", vm.Columns[^1].Title);
    }

    // ---------------------------------------------------------------- (2) create + derived nature + persist

    [Fact]
    public void Group_is_created_through_the_master_derives_nature_and_persists()
    {
        const string companyName = "Group Create Co";
        var vm = NewSeededCompany(companyName);

        vm.ShowAccountGroupMaster();
        Assert.Equal(Screen.AccountGroupMaster, vm.CurrentScreen);
        var m = vm.AccountGroupMaster!;

        // Default parent is Current Liabilities → nature shows read-only as Liability.
        Assert.Equal("Current Liabilities", m.SelectedParent!.Name);
        Assert.Equal("Liability", m.DerivedNature);

        m.Name = "Salary Payable";
        Assert.True(m.Create());

        var created = vm.Company!.FindGroupByName("Salary Payable")!;
        Assert.False(created.IsPredefined);
        Assert.Equal(GroupNature.Liability, created.Nature);          // derived, never chosen
        Assert.Equal(GroupNature.Liability, ClassificationRules.PrimaryNatureOf(created, vm.Company!));
        Assert.Contains(m.Existing, r => r.Name == "Salary Payable" && r.Under == "Current Liabilities"
                                         && r.Nature == "Liability");

        // PERSISTED: reload and the custom group survives with its parent + nature.
        var reloaded = Reload(companyName);
        var rGroup = reloaded.FindGroupByName("Salary Payable");
        Assert.NotNull(rGroup);
        Assert.Equal(GroupNature.Liability, rGroup!.Nature);
        Assert.Equal(reloaded.FindGroupByName("Current Liabilities")!.Id, rGroup.ParentId);
    }

    // ---------------------------------------------------------------- (3) offered to the ledger master + BS side

    [Fact]
    public void Custom_group_is_offered_in_the_ledger_picker_and_a_ledger_under_it_prints_on_liabilities()
    {
        var vm = NewSeededCompany("Group BS Co");

        vm.ShowAccountGroupMaster();
        vm.AccountGroupMaster!.Name = "Salary Payable";
        Assert.True(vm.AccountGroupMaster!.Create());
        var salaryPayable = vm.Company!.FindGroupByName("Salary Payable")!;

        // The new group is offered in the Ledger master's Under-picker (a ledger can now nest under it).
        var ledgerMaster = new LedgerMasterViewModel(vm.Company!, _storage, onChanged: () => { });
        Assert.Contains(ledgerMaster.Groups, g => g.Id == salaryPayable.Id);

        // A ledger under it (opening credit ₹10,000) prints on the LIABILITIES side, grouped under "Salary Payable".
        vm.Company!.AddLedger(new DomainLedger(
            Guid.NewGuid(), "Salary — Alice", salaryPayable.Id, Money.FromRupees(10000m), openingIsDebit: false));

        // The lone credit opening is deliberately one-sided (this fixture tests PLACEMENT, not balance): the payable
        // lands on the LIABILITIES side, grouped under its custom "Salary Payable" head, never on the assets side.
        var bs = BalanceSheet.Build(vm.Company!, new DateOnly(2024, 3, 31));
        Assert.Contains(bs.Liabilities, l => l.Name == "Salary — Alice" && l.GroupName == "Salary Payable");
        Assert.DoesNotContain(bs.Assets, l => l.Name == "Salary — Alice");
    }

    // ---------------------------------------------------------------- (4) nature is derived read-only from the parent

    [Fact]
    public void Nature_is_derived_read_only_and_tracks_the_chosen_parent()
    {
        var vm = NewSeededCompany("Group Nature Co");
        vm.ShowAccountGroupMaster();
        var m = vm.AccountGroupMaster!;

        m.SelectedParent = vm.Company!.FindGroupByName("Fixed Assets");
        Assert.Equal("Asset", m.DerivedNature);

        m.SelectedParent = vm.Company!.FindGroupByName("Sales Accounts");
        Assert.Equal("Income", m.DerivedNature);

        m.SelectedParent = vm.Company!.FindGroupByName("Indirect Expenses");
        Assert.Equal("Expense", m.DerivedNature);

        m.SelectedParent = vm.Company!.FindGroupByName("Current Liabilities");
        Assert.Equal("Liability", m.DerivedNature);

        // Creating under Fixed Assets yields an Asset group — the nature followed the parent, not any user input.
        m.SelectedParent = vm.Company!.FindGroupByName("Fixed Assets");
        m.Name = "Plant & Machinery — Custom";
        Assert.True(m.Create());
        Assert.Equal(GroupNature.Asset, vm.Company!.FindGroupByName("Plant & Machinery — Custom")!.Nature);
    }

    // ---------------------------------------------------------------- (5) validation

    [Fact]
    public void Group_master_requires_a_name_and_rejects_duplicates()
    {
        var vm = NewSeededCompany("Group Validate Co");
        vm.ShowAccountGroupMaster();
        var m = vm.AccountGroupMaster!;

        m.Name = "   ";
        Assert.False(m.Create());                       // blank name rejected
        Assert.NotNull(m.Message);

        m.Name = "Salary Payable";
        Assert.True(m.Create());
        m.Name = "salary payable";                      // case-insensitive duplicate
        Assert.False(m.Create());
        Assert.Contains("already exists", m.Message!);
    }

    // ---------------------------------------------------------------- (6) single page column

    [Fact]
    public void Group_master_opens_as_a_single_page_column_replacing_a_prior_page()
    {
        var vm = NewSeededCompany("Group Nav Column Co");

        vm.ShowLedgerMaster();
        Assert.Equal(1, vm.Columns.Count(c => c.IsPage));

        vm.ShowAccountGroupMaster();
        Assert.Equal(1, vm.Columns.Count(c => c.IsPage));   // replaced the ledger page, not stacked
        Assert.Null(vm.LedgerMaster);
        Assert.Same(vm.AccountGroupMaster, vm.Columns[^1].AccountGroupMaster);

        // Menu screens are hidden while a page column is open (the null-chain includes the new master).
        Assert.False(vm.IsMenuScreen);
    }

    public void Dispose()
    {
        try
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true);
        }
        catch (IOException) { /* best effort */ }
    }
}
