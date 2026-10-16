using System;
using System.IO;
using System.Linq;
using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Services;
using Apex.Desktop.Services;
using Apex.Desktop.ViewModels;

namespace Apex.Desktop.Tests;

/// <summary>
/// 🔴 <b>WBB-2 — A REFUSED MULTI ALTER BATCH MUST LEAVE THE BOOK EXACTLY AS IT WAS.</b>
///
/// <para><b>The defect these tests were written against, stated as it was found.</b>
/// <c>MultiMasterCreateViewModel</c>'s ALTER arm pre-validated the name, the in-batch duplicate, the
/// parent-not-null and the opening-balance parse, and then wrote the rows one at a time. The refusals it did
/// NOT pre-validate are the ones the engine owns — <c>EnsureLedgerRenameAllowed</c>,
/// <c>EnsureGroupAlterAllowed</c>, <c>EnsureGroupReparentValid</c>, <c>EnsureGodownParentValid</c> and
/// <c>AlterCostCentre</c>'s same-category and cycle checks — and those fire INSIDE the write loop. A batch
/// refused on its second row had therefore already written its first row into <c>_company</c>, which is the
/// shell's one live aggregate. The screen then told the operator the batch was refused, and the <b>next
/// unrelated screen that saved made the half-batch durable</b>. A ledger rename resolves by id for every
/// historical voucher, so the books followed a rename the operator had just been told did not happen.</para>
///
/// <para>🔴 <b>WHY THE ASSERTIONS ARE SHAPED THE WAY THEY ARE.</b> The refusal is asserted twice over, because
/// each half alone would pass against a broken build: the <b>live aggregate</b> must be clean (otherwise the
/// grid shows the new name beside the refusal message), AND the book <b>re-read from SQLite after a later,
/// unrelated save</b> must be clean (otherwise the rename is on disk whatever the screen shows). No assertion
/// in this file reads the view model's own state as evidence that a write did or did not happen.</para>
///
/// <para><b>Mutation, stated.</b> Deleting the <c>MasterStateSnapshot.Capture</c> / <c>Restore</c> pair from
/// <c>AlterBatch</c> reddens every test in sections (1)–(4); the recorded result is in the branch's review
/// artefact. A build that merely stops throwing — for instance by widening the pre-validation — still fails,
/// because each test asserts the whole batch was refused as well as that nothing moved.</para>
///
/// <para><b>Vendor grounding.</b> This file pins a correctness property of our own batch screen, not a vendor
/// behaviour, so it quotes nothing. The screen itself is attested on
/// <c>help.tallysolutions.com/tally-prime/charts-of-accounts-tally/</c> ("<i>press <b>Alt+H</b> (Multi-Masters)
/// &gt; <b>Multi Alter</b> &gt; press <b>Enter</b></i>"), quoted where it is used, on <c>MultiMasterKind</c>.
/// All-or-nothing commit is <b>ours, labelled</b> — see the divergence list on
/// <c>MultiMasterCreateViewModel</c>.</para>
/// </summary>
public sealed class MultiMasterAlterRollbackTests : IDisposable
{
    private readonly string _tempDir;
    private readonly CompanyStorage _storage;

    public MultiMasterAlterRollbackTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "ApexMultiRollback_" + Guid.NewGuid().ToString("N"));
        _storage = new CompanyStorage(_tempDir);
    }

    public void Dispose()
    {
        try
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true);
        }
        catch { /* temp */ }
    }

    private MainWindowViewModel NewSeededCompany(string name)
    {
        var vm = new MainWindowViewModel(_storage);
        vm.NewCompanyName = name;
        vm.CreateCompany();
        Assert.Equal(Screen.Gateway, vm.CurrentScreen);
        return vm;
    }

    /// <summary>🔴 Re-reads the company FROM DISK. The durability half of every test goes through this.</summary>
    private Company Reload(string companyName)
    {
        var entry = _storage.ListCompanies().Single(e => e.Name == companyName);
        return _storage.Load(entry);
    }

    private static MultiMasterRowViewModel Row(MultiMasterCreateViewModel grid, Guid id)
        => grid.Rows.Single(r => r.ExistingId == id);

    // ============================================================ (1) ledgers — the reported blocking defect

    /// <summary>
    /// P2 + P3, the reviewer's own probe, as a permanent test. Row 1 is a legal rename that sorts FIRST, so it is
    /// written before row 2 trips the reserved-name guard. Before the fix, "Aaa Probe Ltd" came back as "Zzz
    /// Probe Ltd" from both the live aggregate and — after an unrelated create saved the company — from SQLite.
    /// </summary>
    [Fact]
    public void A_refused_ledger_batch_leaves_the_live_book_untouched_and_never_reaches_disk()
    {
        const string companyName = "Rollback Ledger Co";
        var vm = NewSeededCompany(companyName);

        vm.ShowMultiLedgerCreate();
        var create = vm.MultiMasterCreate!;
        create.SelectedUnderGroup = create.UnderGroupOptions.Single(o => o.Parent?.Name == "Sundry Debtors");
        create.Rows[0].Name = "Aaa Probe Ltd";
        create.Rows[0].OpeningBalanceText = "1234.50";
        Assert.True(create.Accept(), create.Message);

        var company = vm.Company!;
        var probe = company.FindLedgerByName("Aaa Probe Ltd")!;
        var probeId = probe.Id;
        var originalGroupId = probe.GroupId;
        var originalOpening = probe.OpeningBalance;
        var cash = company.FindLedgerByName("Cash")!;
        var cashId = cash.Id;
        var ledgerCountBefore = company.Ledgers.Count;

        // --- the batch: one legal row, then one the ENGINE refuses. "Aaa…" sorts before "Cash", so the legal
        // row is written first and the aggregate is already dirty when the guard fires.
        vm.ShowMultiLedgerAlter();
        var alter = vm.MultiMasterCreate!;
        var probeRow = Row(alter, probeId);
        var cashRow = Row(alter, cashId);
        Assert.True(alter.Rows.IndexOf(probeRow) < alter.Rows.IndexOf(cashRow),
            "fixture guard: the LEGAL row must be written before the refused one, or nothing is being tested.");

        probeRow.Name = "Zzz Probe Ltd";
        probeRow.OpeningBalanceText = "9999.00";
        cashRow.Name = "Petty Cash";          // "Cash" is a WellKnownLedgerName — the engine refuses the rename
        Assert.True(probeRow.IsChanged);
        Assert.True(cashRow.IsChanged);

        Assert.False(alter.Accept());
        Assert.True(alter.MessageIsError);
        Assert.Contains("Cash", alter.Message!);

        // --- (a) the LIVE aggregate is exactly as it was.
        Assert.Equal(ledgerCountBefore, company.Ledgers.Count);
        Assert.Null(company.FindLedgerByName("Zzz Probe Ltd"));
        Assert.Null(company.FindLedgerByName("Petty Cash"));
        var liveProbe = company.FindLedger(probeId)!;
        Assert.Equal("Aaa Probe Ltd", liveProbe.Name);
        Assert.Equal(originalGroupId, liveProbe.GroupId);
        Assert.Equal(originalOpening, liveProbe.OpeningBalance);
        Assert.Equal("Cash", company.FindLedger(cashId)!.Name);

        // --- (b) and the refusal does not become durable on the NEXT UNRELATED SAVE. This is the half the old
        // code failed: it skipped _storage.Save, but left the mutation in the aggregate every other screen uses.
        vm.ShowMultiLedgerCreate();
        var later = vm.MultiMasterCreate!;
        later.SelectedUnderGroup = later.UnderGroupOptions.Single(o => o.Parent?.Name == "Sundry Debtors");
        later.Rows[0].Name = "Unrelated Ledger";
        Assert.True(later.Accept(), later.Message);

        var onDisk = Reload(companyName);
        Assert.Null(onDisk.FindLedgerByName("Zzz Probe Ltd"));
        Assert.Null(onDisk.FindLedgerByName("Petty Cash"));
        var diskProbe = onDisk.FindLedger(probeId)!;
        Assert.Equal("Aaa Probe Ltd", diskProbe.Name);
        Assert.Equal(originalGroupId, diskProbe.GroupId);
        Assert.Equal(originalOpening, diskProbe.OpeningBalance);
        Assert.Equal("Cash", onDisk.FindLedger(cashId)!.Name);
        Assert.NotNull(onDisk.FindLedgerByName("Unrelated Ledger"));   // the later batch DID commit
    }

    /// <summary>
    /// The grid the operator is left looking at must agree with the book. Before the fix it reloaded itself from
    /// the HALF-MUTATED aggregate, so the refused new name was displayed beside the message saying it had been
    /// refused — the worst possible reading.
    /// </summary>
    [Fact]
    public void After_a_refusal_the_grid_redisplays_the_ORIGINAL_values()
    {
        var vm = NewSeededCompany("Rollback Grid Text Co");

        vm.ShowMultiLedgerCreate();
        var create = vm.MultiMasterCreate!;
        create.SelectedUnderGroup = create.UnderGroupOptions.Single(o => o.Parent?.Name == "Sundry Debtors");
        create.Rows[0].Name = "Aaa Display Ltd";
        Assert.True(create.Accept(), create.Message);

        var probeId = vm.Company!.FindLedgerByName("Aaa Display Ltd")!.Id;
        var cashId = vm.Company!.FindLedgerByName("Cash")!.Id;

        vm.ShowMultiLedgerAlter();
        var alter = vm.MultiMasterCreate!;
        Row(alter, probeId).Name = "Zzz Display Ltd";
        Row(alter, cashId).Name = "Petty Cash";

        Assert.False(alter.Accept());

        Assert.True(alter.MessageIsError);
        Assert.Equal("Aaa Display Ltd", Row(alter, probeId).Name);
        Assert.Equal("Cash", Row(alter, cashId).Name);
        Assert.DoesNotContain(alter.Rows, r => r.Name == "Zzz Display Ltd");
    }

    // ============================================================ (2) groups — and the NATURE CASCADE

    /// <summary>
    /// 🔴 The group arm is the one a hand-written undo would get wrong, and that is exactly why it is pinned.
    /// <c>GroupService.AlterGroup</c> ends in <c>MasterAlterationRules.RecomputeNatureFor</c>, which walks DOWN
    /// from the altered group and rewrites <c>Nature</c> on <b>every descendant</b>. So a refused batch does not
    /// only have to put the altered group back — it has to put back a cascade over groups that were never named
    /// in the batch at all. The child group asserted below is the proof of that: it is not a row of the grid.
    /// </summary>
    [Fact]
    public void A_refused_group_batch_rolls_back_the_nature_cascade_over_descendants_too()
    {
        const string companyName = "Rollback Group Co";
        var vm = NewSeededCompany(companyName);
        var company = vm.Company!;

        var groups = new GroupService(company);
        var debtors = company.Groups.Single(g => g.Name == "Sundry Debtors");
        var parent = groups.CreateGroup("Aaa Probe Group", debtors.Id);
        var child = groups.CreateGroup("Aab Probe Child", parent.Id);
        _storage.Save(company);

        var expenses = company.Groups.Single(g => g.Name == "Indirect Expenses");
        Assert.Equal(GroupNature.Expense, expenses.Nature);
        var natureBefore = parent.Nature;
        var childNatureBefore = child.Nature;
        Assert.NotEqual(expenses.Nature, natureBefore);   // fixture guard: the move must actually cascade
        Assert.Equal(natureBefore, childNatureBefore);

        var bank = company.Groups.Single(g => g.Name == "Bank Accounts");
        Assert.True(bank.IsPredefined, "fixture guard: the refusing row must be a predefined group.");

        vm.ShowMultiGroupAlter();
        var alter = vm.MultiMasterCreate!;
        var parentRow = Row(alter, parent.Id);
        var bankRow = Row(alter, bank.Id);
        Assert.True(alter.Rows.IndexOf(parentRow) < alter.Rows.IndexOf(bankRow),
            "fixture guard: the LEGAL re-parent must be written before the refused rename.");

        parentRow.Under = alter.UnderOptions.Single(p => p.Id == expenses.Id);   // legal, and cascades
        bankRow.Name = "Bank A/cs";                                              // predefined — refused

        Assert.False(alter.Accept());
        Assert.True(alter.MessageIsError);

        // The altered group is back…
        Assert.Equal(debtors.Id, company.FindGroup(parent.Id)!.ParentId);
        Assert.Equal(natureBefore, company.FindGroup(parent.Id)!.Nature);
        // …and so is the descendant the batch never named.
        Assert.Equal(childNatureBefore, company.FindGroup(child.Id)!.Nature);
        Assert.Equal("Bank Accounts", company.FindGroup(bank.Id)!.Name);

        // Durability: an unrelated save must not carry any of it to disk.
        _storage.Save(company);
        var onDisk = Reload(companyName);
        Assert.Equal(debtors.Id, onDisk.FindGroup(parent.Id)!.ParentId);
        Assert.Equal(natureBefore, onDisk.FindGroup(parent.Id)!.Nature);
        Assert.Equal(childNatureBefore, onDisk.FindGroup(child.Id)!.Nature);
        Assert.Equal("Bank Accounts", onDisk.FindGroup(bank.Id)!.Name);
    }

    // ============================================================ (3) cost centres

    /// <summary>
    /// The cost arm's guard is <c>AlterCostCentre</c>'s "a parent centre must be in the SAME cost category",
    /// which the multi grid can trip because its Under column IS the category. Row 1 renames legally; row 2 moves
    /// a child centre to a category its parent is not in.
    /// </summary>
    [Fact]
    public void A_refused_cost_centre_batch_leaves_the_earlier_rows_rename_undone()
    {
        const string companyName = "Rollback Cost Co";
        var vm = NewSeededCompany(companyName);
        var company = vm.Company!;
        Assert.True(company.EnableCostCentres, "fixture guard: the cost family is F11-gated.");

        var catA = new CostCategory(Guid.NewGuid(), "Probe Category A");
        var catB = new CostCategory(Guid.NewGuid(), "Probe Category B");
        company.AddCostCategory(catA);
        company.AddCostCategory(catB);
        var head = new CostCentre(Guid.NewGuid(), "Aaa Probe Centre", catA.Id);
        var sub = new CostCentre(Guid.NewGuid(), "Bbb Probe Sub", catA.Id, parentId: head.Id);
        company.AddCostCentre(head);
        company.AddCostCentre(sub);
        _storage.Save(company);

        vm.ShowMultiCostCentreAlter();
        var alter = vm.MultiMasterCreate!;
        var headRow = Row(alter, head.Id);
        var subRow = Row(alter, sub.Id);
        Assert.True(alter.Rows.IndexOf(headRow) < alter.Rows.IndexOf(subRow),
            "fixture guard: the LEGAL rename must be written before the refused re-categorisation.");

        headRow.Name = "Aaa Probe Centre Renamed";                            // legal
        subRow.Under = alter.UnderOptions.Single(p => p.Id == catB.Id);       // parent is in A — refused

        Assert.False(alter.Accept());
        Assert.True(alter.MessageIsError);

        Assert.Equal("Aaa Probe Centre", company.FindCostCentre(head.Id)!.Name);
        Assert.Equal(catA.Id, company.FindCostCentre(sub.Id)!.CategoryId);

        _storage.Save(company);
        var onDisk = Reload(companyName);
        Assert.Equal("Aaa Probe Centre", onDisk.FindCostCentre(head.Id)!.Name);
        Assert.Equal(catA.Id, onDisk.FindCostCentre(sub.Id)!.CategoryId);
    }

    // ============================================================ (4) godowns

    /// <summary>
    /// The godown arm's guard is <c>EnsureGodownParentValid</c>'s cycle check. Row 1 renames legally; row 2 is
    /// re-parented under its own descendant.
    ///
    /// <para><b>The stock-item arm is deliberately not given a scenario here, and the reason is written down
    /// rather than left as a gap.</b> Its write loop has no engine guard an operator can trip from this grid —
    /// the name and the in-batch duplicate are pre-validated, the stock group comes from a picker built out of
    /// the company's own groups, and the base unit and opening allocation are not re-written on alter at all. A
    /// scenario would have to fabricate an unreachable state, which is a test that pins nothing. The arm's
    /// rollback is covered generically by <c>MasterStateSnapshotTests</c>, which exercises stock items
    /// directly.</para>
    /// </summary>
    [Fact]
    public void A_refused_godown_batch_leaves_the_earlier_rows_rename_undone()
    {
        const string companyName = "Rollback Godown Co";
        var vm = NewSeededCompany(companyName);
        var company = vm.Company!;

        var inventory = new InventoryService(company);
        var top = inventory.CreateGodown("Aaa Probe Store");
        var mid = inventory.CreateGodown("Bbb Probe Mid", top.Id);
        var leaf = inventory.CreateGodown("Ccc Probe Leaf", mid.Id);
        _storage.Save(company);

        vm.ShowMultiGodownAlter();
        var alter = vm.MultiMasterCreate!;
        var topRow = Row(alter, top.Id);
        var midRow = Row(alter, mid.Id);
        Assert.True(alter.Rows.IndexOf(topRow) < alter.Rows.IndexOf(midRow),
            "fixture guard: the LEGAL rename must be written before the refused re-parent.");

        topRow.Name = "Aaa Probe Store Renamed";                             // legal
        midRow.Under = alter.UnderOptions.Single(p => p.Id == leaf.Id);      // leaf is mid's own child — a cycle

        Assert.False(alter.Accept());
        Assert.True(alter.MessageIsError);

        Assert.Equal("Aaa Probe Store", company.FindGodown(top.Id)!.Name);
        Assert.Equal(top.Id, company.FindGodown(mid.Id)!.ParentId);

        _storage.Save(company);
        var onDisk = Reload(companyName);
        Assert.Equal("Aaa Probe Store", onDisk.FindGodown(top.Id)!.Name);
        Assert.Equal(top.Id, onDisk.FindGodown(mid.Id)!.ParentId);
    }

    // ============================================================ (5) a LEGAL batch still commits

    /// <summary>
    /// 🔴 <b>The discriminator against the cheapest wrong fix.</b> A rollback that fired on every Accept — or an
    /// Accept hardened into never writing at all — would pass every test above. So one legal multi-row batch is
    /// committed here and read back FROM SQLITE, across two families.
    /// </summary>
    [Fact]
    public void A_wholly_legal_batch_is_unaffected_by_the_rollback_point_and_persists()
    {
        const string companyName = "Rollback Happy Path Co";
        var vm = NewSeededCompany(companyName);

        vm.ShowMultiLedgerCreate();
        var create = vm.MultiMasterCreate!;
        create.SelectedUnderGroup = create.UnderGroupOptions.Single(o => o.Parent?.Name == "Sundry Debtors");
        create.Rows[0].Name = "Aaa Happy Ltd";
        create.Rows[1].Name = "Aab Happy Ltd";
        Assert.True(create.Accept(), create.Message);

        var company = vm.Company!;
        var firstId = company.FindLedgerByName("Aaa Happy Ltd")!.Id;
        var secondId = company.FindLedgerByName("Aab Happy Ltd")!.Id;
        var creditors = company.Groups.Single(g => g.Name == "Sundry Creditors");
        var countBefore = company.Ledgers.Count;

        vm.ShowMultiLedgerAlter();
        var alter = vm.MultiMasterCreate!;
        Row(alter, firstId).Name = "Aaa Happy Renamed";
        Row(alter, secondId).Under = alter.UnderOptions.Single(p => p.Id == creditors.Id);

        Assert.True(alter.Accept(), alter.Message);
        Assert.False(alter.MessageIsError);

        var onDisk = Reload(companyName);
        Assert.Equal(countBefore, onDisk.Ledgers.Count);              // altered, never appended
        Assert.Equal("Aaa Happy Renamed", onDisk.FindLedger(firstId)!.Name);
        Assert.Equal(creditors.Id, onDisk.FindLedger(secondId)!.GroupId);
        Assert.Null(onDisk.FindLedgerByName("Aaa Happy Ltd"));
    }

    // ============================================================ (6) the Chart of Accounts refresh

    /// <summary>
    /// P5 — the MEDIUM finding, and 🔴 <b>a DISPROOF rather than a fix, stated as one.</b> The review read
    /// <c>ShowMultiMaster</c>'s <c>onChanged: () =&gt; { }</c> as leaving the operator on a Chart of Accounts
    /// showing the OLD names after a committed Multi Alter. It does not: <c>OpenPageColumn</c> keeps at most ONE
    /// page column and the Chart of Accounts is one, so opening the grid trims the tree away instead of leaving
    /// a stale instance beneath it, and the next Chart of Accounts is built fresh off the live company.
    /// <b>Measured: with the argument reverted to <c>() =&gt; { }</c> this test still passes.</b> It is kept
    /// because the property it pins is the one that matters to an operator — after altering twenty ledgers, the
    /// tree shows the twenty new names — and that property must not regress however the column model changes.
    /// What it must NOT be read as is evidence for the <c>onChanged</c> wiring; that wiring is consistency with
    /// the two sibling routes, and is labelled as such on <c>ShowMultiMaster</c>.
    /// </summary>
    [Fact]
    public void Escaping_back_from_a_committed_Multi_Alter_shows_the_Chart_of_Accounts_refreshed()
    {
        var vm = NewSeededCompany("Rollback Tree Refresh Co");

        vm.ShowMultiLedgerCreate();
        var create = vm.MultiMasterCreate!;
        create.SelectedUnderGroup = create.UnderGroupOptions.Single(o => o.Parent?.Name == "Sundry Debtors");
        create.Rows[0].Name = "Tree Probe Ltd";
        Assert.True(create.Accept(), create.Message);
        var probeId = vm.Company!.FindLedgerByName("Tree Probe Ltd")!.Id;

        // The VENDOR'S OWN ROUTE, so the refresh is proved on the path that made this defect visible: Chart of
        // Accounts -> Alt+H -> Multi Alter -> Multi Ledger Alteration. Driven by arrows and Enter, not by
        // calling ShowMultiLedgerAlter directly — which from the Gateway opens a column that REPLACES the
        // cascade, so Back() would land on the Gateway and prove nothing about the tree.
        vm.ShowChartOfAccounts();
        Assert.NotNull(vm.ChartOfAccounts);
        Assert.Contains(vm.ChartOfAccounts!.Rows, r => r.Name == "Tree Probe Ltd");

        vm.OpenMultiMasters();
        Assert.Equal(Screen.MultiMastersMenu, vm.CurrentScreen);
        for (var i = 0; i < vm.Menu.Count + 2 && vm.Menu[vm.SelectedIndex].Label != "Multi Ledger Alteration"; i++)
            vm.MoveDown();
        Assert.Equal("Multi Ledger Alteration", vm.Menu[vm.SelectedIndex].Label);
        vm.DrillIn();

        Assert.Equal(Screen.MultiMasterCreate, vm.CurrentScreen);
        var alter = vm.MultiMasterCreate!;
        Assert.True(alter.IsAltering);
        Row(alter, probeId).Name = "Tree Probe Renamed";
        Assert.True(alter.Accept(), alter.Message);

        vm.Back();
        Assert.NotEqual(Screen.MultiMasterCreate, vm.CurrentScreen);

        vm.ShowChartOfAccounts();
        Assert.Equal(Screen.ChartOfAccounts, vm.CurrentScreen);
        Assert.NotNull(vm.ChartOfAccounts);
        Assert.Contains(vm.ChartOfAccounts!.Rows, r => r.Name == "Tree Probe Renamed");
        Assert.DoesNotContain(vm.ChartOfAccounts!.Rows, r => r.Name == "Tree Probe Ltd");
    }

    // ============================================================ (7) the shortcut hint that did nothing

    /// <summary>
    /// The LOW finding. The Masters &gt; Create column's "Multi Ledger" row advertised "Alt+H". The chord is
    /// scoped to the Chart of Accounts — <c>OpenMultiMasters</c> early-returns unless
    /// <c>IsChartOfAccountsScreen</c> — so it is inert on the only screen that ever displayed the hint, and even
    /// where it fires it opens the Multi Masters MENU rather than Multi Ledger Creation.
    ///
    /// <para>The "Ledger / Alt+C" row is asserted alongside it so this does not degrade into "no row has a
    /// hint": that hint IS correct, its arm is global, and it must survive.</para>
    /// </summary>
    [Fact]
    public void The_Create_column_advertises_no_shortcut_it_cannot_honour()
    {
        var vm = NewSeededCompany("Create Column Hint Co");
        Assert.False(vm.IsChartOfAccountsScreen, "the Create column is never shown on the Chart of Accounts.");

        for (var i = 0; i < vm.Menu.Count + 2 && vm.Menu[vm.SelectedIndex].Label != "Create"; i++) vm.MoveDown();
        Assert.Equal("Create", vm.Menu[vm.SelectedIndex].Label);
        vm.DrillIn();
        Assert.False(vm.IsChartOfAccountsScreen);

        var multiLedger = vm.Menu.Single(m => m.Label == "Multi Ledger" && !m.IsHeader);
        Assert.Equal(string.Empty, multiLedger.Hint);
        Assert.DoesNotContain(vm.Menu, m => m.Hint.Contains("Alt+H", StringComparison.OrdinalIgnoreCase));

        var ledger = vm.Menu.Single(m => m.Label == "Ledger" && !m.IsHeader);
        Assert.Equal("Alt+C", ledger.Hint);
    }
}
