using System;
using System.IO;
using System.Linq;
using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Desktop.Services;
using Apex.Desktop.ViewModels;
using DomainLedger = Apex.Ledger.Domain.Ledger;

namespace Apex.Desktop.Tests;

/// <summary>
/// W2-20 (census 2.12) — <b>Multi-master create</b>: the grid-entry screen that creates many ledgers (or many
/// groups) in ONE pass, instead of one screen per master.
///
/// <para><b>Vendor shape (help.tallysolutions.com, "How to Use Chart of Accounts in TallyPrime", fetched
/// 2026-09-05).</b> Multi Masters is reached with <b>Alt+H</b> and offers <i>Multi Create</i> / <i>Multi
/// Alter</i>. The Multi Ledger Creation screen carries an <b>"Under Group"</b> header field which
/// <b>defaults to "All Items"</b>, and a grid whose columns are <b>"Name of Ledger"</b>, <b>"Under"</b> and
/// <b>"Opening Balance"</b>. When the header names a real group, every row inherits it; under "All Items"
/// each row picks its own Under. The Multi Stock Item screen is described with the identical header + grid
/// shape, which is the evidence that the header/grid pattern is the screen family's, not one screen's.</para>
///
/// <para><b>Documented divergences, LABELLED AS OURS (RULING 9)</b> — the help page does not speak to either:
/// <list type="bullet">
///   <item>the <b>Dr/Cr side</b> beside each row's Opening Balance. Our single-ledger master captures
///     opening balance as an unsigned magnitude plus a side (<c>Ledger.OpeningBalance</c> is "always ≥ 0"),
///     so a multi-row screen that captured only a number could not express a credit opening at all. We carry
///     the side per row and default it from the chosen group's nature, exactly as the single-ledger screen
///     does;</item>
///   <item><b>all-or-nothing</b> Accept. The vendor page does not state what happens when one row of twenty
///     is bad. We validate EVERY row first and write only if all pass, because a partial batch leaves the
///     operator guessing which of twenty names landed — and the census's own standard is that a half-applied
///     master write is worse than a refused one.</item>
/// </list></para>
///
/// <para>Drives the real shell view models over a throwaway .db — no UI toolkit.</para>
/// </summary>
public sealed class MultiMasterCreateViewModelTests : IDisposable
{
    private readonly string _tempDir;
    private readonly CompanyStorage _storage;

    public MultiMasterCreateViewModelTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "ApexMultiMasterTests_" + Guid.NewGuid().ToString("N"));
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

    /// <summary>
    /// Fills row <paramref name="i"/> the way a keyboard operator would — typing into the grid cells, and
    /// touching ONLY the cells named. <paramref name="credit"/> is deliberately nullable: leaving it null is
    /// the operator who never went near the Dr/Cr checkbox, which is the case that must show the side DERIVED
    /// from the group. Assigning it unconditionally would overwrite that derivation and the test would then
    /// assert nothing about it.
    /// </summary>
    private static void TypeRow(
        MultiMasterCreateViewModel m, int i, string name, string? under = null,
        string opening = "", bool? credit = null)
    {
        var row = m.Rows[i];
        row.Name = name;
        if (under is not null)
            row.Under = m.UnderOptions.Single(g => g.Name == under);
        if (opening.Length > 0) row.OpeningBalanceText = opening;
        if (credit is { } c) row.OpeningIsCredit = c;
    }

    // ------------------------------------------------------------------ (1) REACHABILITY, by real navigation

    /// <summary>
    /// The row only closes if a USER CAN REACH IT. Driven from the ROOT column with the real keyboard drill —
    /// a <c>ShowMultiLedgerCreate()</c> call would prove nothing about reachability.
    /// </summary>
    [Fact]
    public void Multi_Ledger_is_reachable_from_the_Gateway_by_arrows_and_Enter()
    {
        var vm = NewSeededCompany("Multi Reach Co");

        SelectActiveItem(vm, "Create");
        vm.DrillIn();
        Assert.Equal(GatewayMenu.Create, vm.CurrentGatewayMenu);

        // Nested under a parent section, never a flat dump (UI contract).
        Assert.Contains(vm.Menu, m => m.IsHeader && m.Label == "Multi Masters");

        SelectActiveItem(vm, "Multi Ledger");
        vm.DrillIn();

        Assert.Equal(Screen.MultiMasterCreate, vm.CurrentScreen);
        Assert.NotNull(vm.MultiMasterCreate);
        Assert.Same(vm.MultiMasterCreate, vm.Columns[^1].MultiMasterCreate);
        Assert.Equal("Multi Ledger Creation", vm.Columns[^1].Title);
        Assert.Equal(MultiMasterKind.Ledger, vm.MultiMasterCreate!.Kind);
    }

    [Fact]
    public void Multi_Group_is_reachable_from_the_Gateway_by_arrows_and_Enter()
    {
        var vm = NewSeededCompany("Multi Group Reach Co");

        SelectActiveItem(vm, "Create");
        vm.DrillIn();
        SelectActiveItem(vm, "Multi Group");
        vm.DrillIn();

        Assert.Equal(Screen.MultiMasterCreate, vm.CurrentScreen);
        Assert.Equal("Multi Group Creation", vm.Columns[^1].Title);
        Assert.Equal(MultiMasterKind.AccountGroup, vm.MultiMasterCreate!.Kind);
    }

    // ------------------------------------------------------------------ (2) the header "Under Group" field

    /// <summary>
    /// The header field defaults to <b>"All Items"</b> (vendor), and the grid always offers a trailing BLANK
    /// row so the operator never has to reach for an "add row" affordance — Down into the blank row is enough.
    /// </summary>
    [Fact]
    public void Under_Group_header_defaults_to_All_Items_and_the_grid_opens_with_one_blank_row()
    {
        var vm = NewSeededCompany("Multi Default Co");
        vm.ShowMultiLedgerCreate();
        var m = vm.MultiMasterCreate!;

        Assert.Equal("All Items", m.SelectedUnderGroup!.Display);
        Assert.Null(m.SelectedUnderGroup.Group);
        Assert.True(m.IsAllItems);
        Assert.Single(m.Rows);
        Assert.Equal(string.Empty, m.Rows[0].Name);

        // "All Items" is the FIRST option and every company group follows it.
        Assert.Equal("All Items", m.UnderGroupOptions[0].Display);
        Assert.Contains(m.UnderGroupOptions, o => o.Group?.Name == "Sundry Debtors");
    }

    /// <summary>Typing into the last row appends a fresh blank one — the grid grows as the operator types.</summary>
    [Fact]
    public void Typing_a_name_in_the_last_row_appends_a_new_blank_row()
    {
        var vm = NewSeededCompany("Multi Grow Co");
        vm.ShowMultiLedgerCreate();
        var m = vm.MultiMasterCreate!;

        m.Rows[0].Name = "Alpha";
        Assert.Equal(2, m.Rows.Count);
        m.Rows[1].Name = "Beta";
        Assert.Equal(3, m.Rows.Count);
        Assert.Equal(string.Empty, m.Rows[2].Name);
    }

    // ------------------------------------------------------------------ (3) the batch create, header group

    /// <summary>
    /// The point of the feature: three ledgers under ONE chosen group, created in a single pass and persisted.
    /// Expected values derived by hand: 41237.53 rupees = 4,123,753 paisa, debit side (Sundry Debtors is an
    /// Asset group).
    /// </summary>
    [Fact]
    public void Three_ledgers_are_created_in_one_pass_under_the_header_group_and_persist()
    {
        const string companyName = "Multi Batch Co";
        var vm = NewSeededCompany(companyName);
        vm.ShowMultiLedgerCreate();
        var m = vm.MultiMasterCreate!;

        m.SelectedUnderGroup = m.UnderGroupOptions.Single(o => o.Group?.Name == "Sundry Debtors");
        Assert.False(m.IsAllItems);

        TypeRow(m, 0, "Acme Traders", opening: "41237.53");
        TypeRow(m, 1, "Bharat Stores");
        TypeRow(m, 2, "Chetan & Co");

        Assert.True(m.Accept(), m.Message);
        Assert.Equal("3 ledgers created under Sundry Debtors.", m.Message);

        var debtors = vm.Company!.FindGroupByName("Sundry Debtors")!;
        foreach (var name in new[] { "Acme Traders", "Bharat Stores", "Chetan & Co" })
        {
            var l = vm.Company!.FindLedgerByName(name);
            Assert.NotNull(l);
            Assert.Equal(debtors.Id, l!.GroupId);
        }

        var acme = vm.Company!.FindLedgerByName("Acme Traders")!;
        Assert.Equal(41237.53m, acme.OpeningBalance.Amount);
        Assert.True(acme.OpeningIsDebit);                       // Asset group ⇒ Dr by default
        Assert.Equal(Money.Zero, vm.Company!.FindLedgerByName("Bharat Stores")!.OpeningBalance);

        // PERSISTED: reload and all three survive under the same group.
        var reloaded = Reload(companyName);
        var rDebtors = reloaded.FindGroupByName("Sundry Debtors")!;
        Assert.Equal(3, new[] { "Acme Traders", "Bharat Stores", "Chetan & Co" }
            .Count(n => reloaded.FindLedgerByName(n)?.GroupId == rDebtors.Id));
        Assert.Equal(41237.53m, reloaded.FindLedgerByName("Acme Traders")!.OpeningBalance.Amount);

        // The grid resets to a single blank row, ready for the next batch.
        Assert.Single(m.Rows);
        Assert.Equal(string.Empty, m.Rows[0].Name);
    }

    /// <summary>Under "All Items" each row carries its OWN Under — the mixed-list case the vendor calls out.</summary>
    [Fact]
    public void Under_All_Items_each_row_carries_its_own_Under_group()
    {
        var vm = NewSeededCompany("Multi Mixed Co");
        vm.ShowMultiLedgerCreate();
        var m = vm.MultiMasterCreate!;

        Assert.True(m.IsAllItems);
        TypeRow(m, 0, "Delta Debtor", under: "Sundry Debtors");
        TypeRow(m, 1, "Echo Creditor", under: "Sundry Creditors");

        Assert.True(m.Accept(), m.Message);

        Assert.Equal(vm.Company!.FindGroupByName("Sundry Debtors")!.Id,
                     vm.Company!.FindLedgerByName("Delta Debtor")!.GroupId);
        Assert.Equal(vm.Company!.FindGroupByName("Sundry Creditors")!.Id,
                     vm.Company!.FindLedgerByName("Echo Creditor")!.GroupId);

        // A Liability group defaults the row's side to CREDIT — derived, never typed.
        Assert.False(vm.Company!.FindLedgerByName("Echo Creditor")!.OpeningIsDebit);
    }

    /// <summary>A row with a name but no Under, under "All Items", is refused and NAMES the row.</summary>
    [Fact]
    public void A_row_with_no_Under_under_All_Items_is_refused_by_row_number()
    {
        var vm = NewSeededCompany("Multi NoUnder Co");
        vm.ShowMultiLedgerCreate();
        var m = vm.MultiMasterCreate!;

        TypeRow(m, 0, "Foxtrot", under: "Sundry Debtors");
        TypeRow(m, 1, "Golf");                                   // no Under picked

        Assert.False(m.Accept());
        Assert.Equal("Row 2 (Golf): pick an Under group.", m.Message);
        Assert.Null(vm.Company!.FindLedgerByName("Foxtrot"));     // nothing written
        Assert.Null(vm.Company!.FindLedgerByName("Golf"));
    }

    // ------------------------------------------------------------------ (4) ALL-OR-NOTHING (our divergence)

    /// <summary>
    /// 🔴 The clause that makes the screen safe: ONE bad row writes NOTHING. Row 2 collides with the seeded
    /// "Cash" ledger; rows 1 and 3 are perfectly valid and must still not exist afterwards.
    /// </summary>
    [Fact]
    public void One_invalid_row_creates_nothing_at_all()
    {
        const string companyName = "Multi Atomic Co";
        var vm = NewSeededCompany(companyName);
        Assert.NotNull(vm.Company!.FindLedgerByName("Cash"));     // the collision target really is seeded

        vm.ShowMultiLedgerCreate();
        var m = vm.MultiMasterCreate!;
        m.SelectedUnderGroup = m.UnderGroupOptions.Single(o => o.Group?.Name == "Sundry Debtors");

        TypeRow(m, 0, "Hotel Ledger");
        TypeRow(m, 1, "Cash");                                    // already exists
        TypeRow(m, 2, "India Ledger");

        Assert.False(m.Accept());
        Assert.Equal("Row 2 (Cash): a ledger named 'Cash' already exists.", m.Message);

        Assert.Null(vm.Company!.FindLedgerByName("Hotel Ledger"));
        Assert.Null(vm.Company!.FindLedgerByName("India Ledger"));

        // and nothing reached the store either.
        var reloaded = Reload(companyName);
        Assert.Null(reloaded.FindLedgerByName("Hotel Ledger"));
        Assert.Null(reloaded.FindLedgerByName("India Ledger"));

        // The operator's typing SURVIVES the refusal — three rows still filled, ready to be corrected.
        Assert.Equal("Hotel Ledger", m.Rows[0].Name);
        Assert.Equal("India Ledger", m.Rows[2].Name);
    }

    /// <summary>
    /// Two rows in the SAME batch naming the same ledger. Neither exists yet, so the engine's uniqueness guard
    /// cannot see the clash — only a batch-level check can, and without it the second row would silently
    /// create a duplicate ledger name.
    /// </summary>
    [Fact]
    public void Two_rows_naming_the_same_master_are_refused_before_anything_is_written()
    {
        var vm = NewSeededCompany("Multi Dup Co");
        vm.ShowMultiLedgerCreate();
        var m = vm.MultiMasterCreate!;
        m.SelectedUnderGroup = m.UnderGroupOptions.Single(o => o.Group?.Name == "Sundry Debtors");

        TypeRow(m, 0, "Juliet Ltd");
        TypeRow(m, 1, "juliet ltd");                              // same name, different case

        Assert.False(m.Accept());
        Assert.Equal("Row 2 (juliet ltd): 'juliet ltd' is entered twice in this batch.", m.Message);
        Assert.Null(vm.Company!.FindLedgerByName("Juliet Ltd"));
    }

    /// <summary>
    /// 🔴 A confirmation is not a refusal. The screen renders one text block per severity, so the view model
    /// has to say which one this is — the first draft bound a single block to <c>Message</c> in the alert
    /// colour and printed "3 ledgers created under Sundry Debtors." in RED, which is exactly the defect
    /// <c>CompanyProfileViewModel</c> already carries a note about having paid for once.
    /// </summary>
    [Fact]
    public void A_created_batch_reads_as_a_confirmation_and_a_refused_one_as_an_error()
    {
        var vm = NewSeededCompany("Multi Severity Co");
        vm.ShowMultiLedgerCreate();
        var m = vm.MultiMasterCreate!;
        m.SelectedUnderGroup = m.UnderGroupOptions.Single(o => o.Group?.Name == "Sundry Debtors");

        // --- a refusal is an error and shows on the error line only.
        TypeRow(m, 0, "Sierra Ltd", opening: "1.005");
        Assert.False(m.Accept());
        Assert.True(m.MessageIsError);
        Assert.Equal(m.Message, m.ErrorMessage);
        Assert.Null(m.ConfirmationMessage);

        // --- the same screen, once the batch lands, shows on the confirmation line only.
        m.Rows[0].OpeningBalanceText = "1.00";
        Assert.True(m.Accept());
        Assert.False(m.MessageIsError);
        Assert.Equal("1 ledger created under Sundry Debtors.", m.Message);
        Assert.Equal(m.Message, m.ConfirmationMessage);
        Assert.Null(m.ErrorMessage);
    }

    /// <summary>A blank grid refuses rather than silently succeeding with a "0 created" message.</summary>
    [Fact]
    public void An_empty_grid_is_refused()
    {
        var vm = NewSeededCompany("Multi Empty Co");
        vm.ShowMultiLedgerCreate();
        var m = vm.MultiMasterCreate!;

        Assert.False(m.Accept());
        Assert.Equal("Nothing to create — type at least one name.", m.Message);
    }

    /// <summary>
    /// Opening balance keeps the single-ledger master's three refusals, and reports them BY ROW. A sub-paisa
    /// opening cannot round-trip the INTEGER-paisa store, so it is refused here rather than three layers down.
    /// </summary>
    [Fact]
    public void A_sub_paisa_opening_balance_is_refused_by_row_and_writes_nothing()
    {
        var vm = NewSeededCompany("Multi Paisa Co");
        vm.ShowMultiLedgerCreate();
        var m = vm.MultiMasterCreate!;
        m.SelectedUnderGroup = m.UnderGroupOptions.Single(o => o.Group?.Name == "Sundry Debtors");

        TypeRow(m, 0, "Kilo Ltd", opening: "1234.567");

        Assert.False(m.Accept());
        Assert.Equal(
            "Row 1 (Kilo Ltd): opening balance cannot be finer than a paisa (at most two decimal places).",
            m.Message);
        Assert.Null(vm.Company!.FindLedgerByName("Kilo Ltd"));
    }

    /// <summary>An explicit Cr side on a Dr-natured group is honoured — the operator's choice wins.</summary>
    [Fact]
    public void An_explicit_Cr_side_overrides_the_group_default()
    {
        var vm = NewSeededCompany("Multi Side Co");
        vm.ShowMultiLedgerCreate();
        var m = vm.MultiMasterCreate!;
        m.SelectedUnderGroup = m.UnderGroupOptions.Single(o => o.Group?.Name == "Sundry Debtors");

        TypeRow(m, 0, "Lima Ltd", opening: "500", credit: true);
        Assert.True(m.Accept(), m.Message);

        var lima = vm.Company!.FindLedgerByName("Lima Ltd")!;
        Assert.Equal(500m, lima.OpeningBalance.Amount);
        Assert.False(lima.OpeningIsDebit);
    }

    // ------------------------------------------------------------------ (5) the GROUP flavour

    /// <summary>
    /// Multi Group Creation: two groups under Current Liabilities in one pass, natures DERIVED from the parent
    /// (never typed), persisted. The screen carries no Opening Balance column for groups.
    /// </summary>
    [Fact]
    public void Two_groups_are_created_in_one_pass_and_derive_their_nature_from_the_parent()
    {
        const string companyName = "Multi Groups Co";
        var vm = NewSeededCompany(companyName);
        vm.ShowMultiGroupCreate();
        var m = vm.MultiMasterCreate!;

        Assert.False(m.ShowsOpeningBalance);
        m.SelectedUnderGroup = m.UnderGroupOptions.Single(o => o.Group?.Name == "Current Liabilities");

        TypeRow(m, 0, "Salary Payable");
        TypeRow(m, 1, "Bonus Payable");

        Assert.True(m.Accept(), m.Message);
        Assert.Equal("2 groups created under Current Liabilities.", m.Message);

        foreach (var name in new[] { "Salary Payable", "Bonus Payable" })
        {
            var g = vm.Company!.FindGroupByName(name);
            Assert.NotNull(g);
            Assert.False(g!.IsPredefined);
            Assert.Equal(GroupNature.Liability, g.Nature);        // derived from Current Liabilities
        }

        var reloaded = Reload(companyName);
        Assert.Equal(GroupNature.Liability, reloaded.FindGroupByName("Salary Payable")!.Nature);
        Assert.Equal(reloaded.FindGroupByName("Current Liabilities")!.Id,
                     reloaded.FindGroupByName("Bonus Payable")!.ParentId);
    }

    // ------------------------------------------------------------------ (6) keyboard: Ctrl+A accepts the batch

    /// <summary>
    /// Keyboard-first: the shell's Ctrl+A path (<see cref="MainWindowViewModel.ActivateSelected"/>) accepts the
    /// whole grid, exactly as it saves every other master — no mouse anywhere in this test.
    /// </summary>
    [Fact]
    public void CtrlA_through_the_shell_accepts_the_whole_grid()
    {
        var vm = NewSeededCompany("Multi CtrlA Co");

        SelectActiveItem(vm, "Create");
        vm.DrillIn();
        SelectActiveItem(vm, "Multi Ledger");
        vm.DrillIn();
        Assert.Equal(Screen.MultiMasterCreate, vm.CurrentScreen);

        var m = vm.MultiMasterCreate!;
        m.SelectedUnderGroup = m.UnderGroupOptions.Single(o => o.Group?.Name == "Sundry Debtors");
        TypeRow(m, 0, "Mike Ltd");
        TypeRow(m, 1, "November Ltd");

        vm.ActivateSelected();                                    // ← Ctrl+A

        Assert.NotNull(vm.Company!.FindLedgerByName("Mike Ltd"));
        Assert.NotNull(vm.Company!.FindLedgerByName("November Ltd"));
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, true); } catch { /* best effort */ }
    }
}
