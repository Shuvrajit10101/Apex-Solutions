using System;
using System.IO;
using System.Linq;
using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Services;
using Apex.Desktop.Services;
using Apex.Desktop.ViewModels;
using DomainLedger = Apex.Ledger.Domain.Ledger;

namespace Apex.Desktop.Tests;

/// <summary>
/// WBB-1 (census row 2.12, and the multi-stock-item clause of row 3.3) — <b>the three missing Multi Masters
/// families and the Multi ALTER verb</b>.
///
/// <para><b>Vendor shape, each page opened BY CONTENT on 2026-10-13.</b>
/// <list type="bullet">
///   <item><c>help.tallysolutions.com/tally-prime/charts-of-accounts-tally/</c> — "<i>press <b>Alt+H</b>
///     (Multi-Masters) &gt; <b>Multi Create</b> and press <b>Enter</b></i>"; "<i>press <b>Alt+H</b>
///     (Multi-Masters) &gt; <b>Multi Alter</b> &gt; press <b>Enter</b></i>"; the <b>Multi Ledger Alteration</b>
///     screen, where the operator may "<i>change the name of ledger, <b>Group</b>, and <b>Opening
///     Balance</b></i>"; and "<i>To remove the opening balances from all the ledgers, press <b>Alt+B</b> (Zero
///     Opening Balance)</i>".</item>
///   <item><c>help.tallysolutions.com/stock-items-faq/</c> — "<i>Alt+H (Multi-Masters) to open the <b>Multi Stock
///     Item Creation</b> or <b>Multi Stock Item Alteration</b> screens</i>", and the vendor's own limit:
///     "<i>Currently, TallyPrime does not support viewing and editing all the details related to stock items in
///     the Multi Stock Item Alteration screen</i>".</item>
///   <item><c>help.tallysolutions.com/inventory-storage-using-godowns-locations-tally/</c> — "<i>In the Chart of
///     Accounts screen, press <b>Alt+H</b> (Multi-Masters) &gt; and select <b>Multi Create</b></i>", and "<i>If
///     you select <b>Under Godown &gt; All Items</b>, you will be prompted to select the parent godown for each
///     godown name that you specify under the <b>Name</b> column</i>".</item>
///   <item><c>help.tallysolutions.com/cost-centre-or-profit-centre-tally/</c> — "<i>You can create and alter
///     multiple Cost or Profit Centres from one screen. Use the <b>multi-master creation and alteration
///     feature</b> … from the Chart of Accounts</i>".</item>
/// </list></para>
///
/// <para>🔴 <b>WHAT THIS FILE DELIBERATELY DOES NOT ASSERT, because the census asked for the wrong thing.</b>
/// Row 2.12 says we ship "<b>two of the vendor's SIX</b>" families. <b>There is no vendor source for SIX.</b> The
/// Chart of Accounts page names Ledgers, stock items, groups and employees and then closes the list with
/// "<i>and so on</i>"; other pages add Godowns, Cost Centres and Cost Categories. So no test here pins a family
/// COUNT — pinning six would be a test that enshrines an uncited number as fidelity, which is exactly the
/// failure that cost row 3.5 its promotion. Each family is pinned individually, against its own citation.</para>
///
/// <para>🔴 <b>THE DEFECT SHAPE THIS FILE EXISTS TO CATCH.</b> T2-105: an alteration screen whose only button ran
/// a bare unconditional <c>Create()</c>, so a correction was duplicated or silently lost — four live sites, two
/// census demotions. The alter tests below therefore never assert on the view model's own state. They re-read the
/// company <b>from SQLite</b> and assert on (a) the master count, (b) the ORIGINAL <see cref="Guid"/>, and (c) the
/// corrected values. A grid that "altered" by appending a second master fails (a); one that wrote to a copy fails
/// (b); one that reported success without persisting fails (c).</para>
/// </summary>
public sealed class MultiMasterAlterAndFamiliesTests : IDisposable
{
    private readonly string _tempDir;
    private readonly CompanyStorage _storage;

    public MultiMasterAlterAndFamiliesTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "ApexMultiAlterTests_" + Guid.NewGuid().ToString("N"));
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

    /// <summary>🔴 Re-reads the company FROM DISK. Every alteration assertion goes through this, never the grid.</summary>
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

    /// <summary>Gives the company one stock group and one unit, which a new company does not have.</summary>
    private static (Guid GroupId, Guid UnitId) SeedInventoryBasics(Company company, string groupName = "Finished Goods")
    {
        var inventory = new InventoryService(company);
        var group = inventory.CreateStockGroup(groupName);
        var unit = inventory.CreateSimpleUnit("Nos", "Numbers");
        return (group.Id, unit.Id);
    }

    // =========================================================== (1) the three new CREATE families, by navigation

    /// <summary>
    /// The row only closes if a USER CAN REACH IT, so each new family is driven from the ROOT column with the
    /// real keyboard drill. A <c>ShowMultiStockItemCreate()</c> call would prove nothing about reachability —
    /// that is the "capability no user can reach" shape this project has filed repeatedly.
    /// </summary>
    [Theory]
    [InlineData("Multi Stock Item", "Multi Stock Item Creation", MultiMasterKind.StockItem)]
    [InlineData("Multi Godown", "Multi Godown Creation", MultiMasterKind.Godown)]
    [InlineData("Multi Cost Centre", "Multi Cost Centre Creation", MultiMasterKind.CostCentre)]
    public void Each_new_family_is_reachable_from_the_Gateway_by_arrows_and_Enter(
        string menuLabel, string expectedTitle, MultiMasterKind expectedKind)
    {
        var vm = NewSeededCompany("Multi Family Reach " + menuLabel);
        Assert.True(vm.Company!.EnableCostCentres, "fixture guard: the cost family's menu row is F11-gated.");

        SelectActiveItem(vm, "Create");
        vm.DrillIn();

        // Nested under the Multi Masters section header, never a flat dump (UI contract).
        Assert.Contains(vm.Menu, m => m.IsHeader && m.Label == "Multi Masters");

        SelectActiveItem(vm, menuLabel);
        vm.DrillIn();

        Assert.Equal(Screen.MultiMasterCreate, vm.CurrentScreen);
        Assert.NotNull(vm.MultiMasterCreate);
        Assert.Same(vm.MultiMasterCreate, vm.Columns[^1].MultiMasterCreate);
        Assert.Equal(expectedTitle, vm.Columns[^1].Title);
        Assert.Equal(expectedKind, vm.MultiMasterCreate!.Kind);
        Assert.Equal(MultiMasterMode.Create, vm.MultiMasterCreate.Mode);
    }

    /// <summary>
    /// The screen names are the vendor's own, on both verbs. These are the strings <c>stock-items-faq</c> and the
    /// Chart of Accounts page use verbatim ("Multi Stock Item Creation" / "Multi Stock Item Alteration" / "Multi
    /// Ledger Alteration").
    /// </summary>
    [Theory]
    [InlineData(MultiMasterKind.Ledger, MultiMasterMode.Create, "Multi Ledger Creation")]
    [InlineData(MultiMasterKind.Ledger, MultiMasterMode.Alter, "Multi Ledger Alteration")]
    [InlineData(MultiMasterKind.AccountGroup, MultiMasterMode.Alter, "Multi Group Alteration")]
    [InlineData(MultiMasterKind.StockItem, MultiMasterMode.Create, "Multi Stock Item Creation")]
    [InlineData(MultiMasterKind.StockItem, MultiMasterMode.Alter, "Multi Stock Item Alteration")]
    [InlineData(MultiMasterKind.Godown, MultiMasterMode.Alter, "Multi Godown Alteration")]
    [InlineData(MultiMasterKind.CostCentre, MultiMasterMode.Alter, "Multi Cost Centre Alteration")]
    public void The_screen_name_is_the_vendors_on_both_verbs(
        MultiMasterKind kind, MultiMasterMode mode, string expected)
    {
        var vm = NewSeededCompany("Multi Title Co " + kind + mode);
        var grid = new MultiMasterCreateViewModel(kind, mode, vm.Company!, _storage, onChanged: () => { });
        Assert.Equal(expected, grid.Title);
    }

    /// <summary>
    /// 🔴 <b>THE LABEL/VERB DRIFT DISCRIMINATOR AT THE VIEW-MODEL LEVEL.</b> T2-105's second half was a
    /// hard-coded <c>Content="Create (Ctrl+A)"</c> standing over an alteration screen — the alteration WORKED and
    /// the screen lied about what it was about to do, which is how it survived a verb-by-verb audit on four
    /// masters. The caption is derived from the mode here so it cannot be forgotten.
    /// </summary>
    [Fact]
    public void The_commit_label_names_the_verb_the_screen_will_actually_run()
    {
        var vm = NewSeededCompany("Multi Label Co");

        var create = new MultiMasterCreateViewModel(
            MultiMasterKind.Ledger, MultiMasterMode.Create, vm.Company!, _storage, () => { });
        var alter = new MultiMasterCreateViewModel(
            MultiMasterKind.Ledger, MultiMasterMode.Alter, vm.Company!, _storage, () => { });

        Assert.Equal("Create All (Ctrl+A)", create.CommitLabel);
        Assert.Equal("Alter All (Ctrl+A)", alter.CommitLabel);
        Assert.False(create.IsAltering);
        Assert.True(alter.IsAltering);
        Assert.NotEqual(create.CommitLabel, alter.CommitLabel);
    }

    // =========================================================== (2) Multi Stock Item Creation

    /// <summary>
    /// The feature's point for Area 3: two stock items, each with its own base unit and opening stock, created in
    /// ONE pass — and <b>read back off the book from SQLite</b>, including the opening allocation, which is the
    /// artefact that turns into closing stock on the Balance Sheet.
    /// </summary>
    [Fact]
    public void Two_stock_items_are_created_in_one_pass_with_opening_stock_and_persist()
    {
        const string companyName = "Multi Item Batch Co";
        var vm = NewSeededCompany(companyName);
        var (groupId, _) = SeedInventoryBasics(vm.Company!);
        _storage.Save(vm.Company!);

        vm.ShowMultiStockItemCreate();
        var m = vm.MultiMasterCreate!;

        Assert.Equal("Under Group", m.UnderHeaderLabel);
        Assert.Equal("Name of Item", m.NameColumnHeader);
        Assert.True(m.ShowsUnits);
        Assert.True(m.ShowsOpeningQuantity);
        Assert.False(m.ShowsOpeningBalance);   // an item has a quantity, not an opening BALANCE

        m.SelectedUnderGroup = m.UnderGroupOptions.Single(o => o.Parent?.Id == groupId);

        var nos = m.UnitOptions.Single(u => u.Display == "Nos");
        m.Rows[0].Name = "Widget A";
        m.Rows[0].Unit = nos;
        m.Rows[0].OpeningQuantityText = "12";
        m.Rows[0].OpeningRateText = "250.50";

        m.Rows[1].Name = "Widget B";
        m.Rows[1].Unit = nos;

        Assert.True(m.Accept(), m.Message);
        Assert.False(m.MessageIsError);
        Assert.Equal("2 stock items created under Finished Goods.", m.Message);

        // 🔴 Off the book, re-read from SQLite — not from the grid.
        var book = Reload(companyName);
        Assert.Equal(2, book.StockItems.Count);
        var a = book.StockItems.Single(i => i.Name == "Widget A");
        var b = book.StockItems.Single(i => i.Name == "Widget B");
        Assert.Equal(groupId, a.StockGroupId);
        Assert.Equal(groupId, b.StockGroupId);

        // Exactly ONE opening allocation, on Widget A only, at the keyed quantity and rate.
        var openings = book.StockOpeningBalances.Where(o => o.StockItemId == a.Id).ToList();
        Assert.Single(openings);
        Assert.Equal(12m, openings[0].Quantity);
        Assert.Equal(250.50m, openings[0].Rate.Amount);
        Assert.Empty(book.StockOpeningBalances.Where(o => o.StockItemId == b.Id));
    }

    /// <summary>
    /// <b>Amount is computed, not keyed</b> — Qty × Rate, blank whenever either input is missing or unparseable.
    /// Ours and labelled: no vendor page this project could open by content lists this grid's columns. It is
    /// read-only because two editable boxes over one derived third are three ways to disagree about an opening
    /// valuation, and an opening valuation is Balance-Sheet money.
    /// </summary>
    [Fact]
    public void The_stock_item_Amount_cell_is_computed_from_quantity_and_rate()
    {
        var vm = NewSeededCompany("Multi Amount Co");
        SeedInventoryBasics(vm.Company!);
        vm.ShowMultiStockItemCreate();
        var row = vm.MultiMasterCreate!.Rows[0];

        Assert.Equal(string.Empty, row.AmountText);          // nothing keyed

        row.OpeningQuantityText = "12";
        Assert.Equal(string.Empty, row.AmountText);          // a quantity with no rate values nothing

        row.OpeningRateText = "250.50";
        Assert.Equal("3006.00", row.AmountText);             // 12 × 250.50, by hand

        row.OpeningRateText = "not a number";
        Assert.Equal(string.Empty, row.AmountText);          // never shows a figure the batch would refuse
    }

    /// <summary>A rate with no quantity has nothing to value, and must be refused rather than discarded.</summary>
    [Fact]
    public void A_stock_item_rate_without_a_quantity_refuses_the_batch()
    {
        var vm = NewSeededCompany("Multi Rate Only Co");
        var (groupId, _) = SeedInventoryBasics(vm.Company!);
        vm.ShowMultiStockItemCreate();
        var m = vm.MultiMasterCreate!;
        m.SelectedUnderGroup = m.UnderGroupOptions.Single(o => o.Parent?.Id == groupId);

        m.Rows[0].Name = "Widget C";
        m.Rows[0].Unit = m.UnitOptions.Single();
        m.Rows[0].OpeningRateText = "99.00";

        Assert.False(m.Accept());
        Assert.True(m.MessageIsError);
        Assert.Contains("a rate needs an opening quantity", m.Message);
        Assert.Empty(vm.Company!.StockItems);               // all-or-nothing: nothing was written
    }

    /// <summary>An item needs a base unit, and the refusal must name the row rather than the screen.</summary>
    [Fact]
    public void A_stock_item_row_without_a_unit_refuses_the_batch_naming_the_row()
    {
        var vm = NewSeededCompany("Multi No Unit Co");
        var (groupId, _) = SeedInventoryBasics(vm.Company!);
        vm.ShowMultiStockItemCreate();
        var m = vm.MultiMasterCreate!;
        m.SelectedUnderGroup = m.UnderGroupOptions.Single(o => o.Parent?.Id == groupId);

        m.Rows[0].Name = "Widget D";
        m.Rows[0].Unit = m.UnitOptions.Single();
        m.Rows[1].Name = "Widget E";                         // no unit on row 2

        Assert.False(m.Accept());
        Assert.Contains("Row 2 (Widget E)", m.Message);
        Assert.Contains("base unit", m.Message);
        Assert.Empty(vm.Company!.StockItems);                // row 1 was valid and STILL nothing was written
    }

    // =========================================================== (3) Multi Godown / Multi Cost Centre creation

    /// <summary>
    /// The godown grid's header is the vendor's <b>"Under Godown"</b> and its name column the vendor's
    /// <b>"Name"</b> — both quoted verbatim from the godowns page. Created godowns are read back from SQLite.
    /// </summary>
    [Fact]
    public void Two_godowns_are_created_under_the_header_godown_and_persist()
    {
        const string companyName = "Multi Godown Co";
        var vm = NewSeededCompany(companyName);
        vm.ShowMultiGodownCreate();
        var m = vm.MultiMasterCreate!;

        Assert.Equal("Under Godown", m.UnderHeaderLabel);
        Assert.Equal("Name", m.NameColumnHeader);
        Assert.Equal("Under", m.UnderColumnHeader);
        Assert.Equal("All Items", m.UnderGroupOptions[0].Display);

        var main = m.UnderGroupOptions.Single(o => o.Parent?.Name == vm.Company!.MainLocationName);
        m.SelectedUnderGroup = main;
        m.Rows[0].Name = "Bay 1";
        m.Rows[1].Name = "Bay 2";

        Assert.True(m.Accept(), m.Message);
        Assert.Equal("2 godowns created under " + vm.Company!.MainLocationName + ".", m.Message);

        var book = Reload(companyName);
        var bay1 = book.Godowns.Single(g => g.Name == "Bay 1");
        Assert.Equal(main.Parent!.Id, bay1.ParentId);
        Assert.NotNull(book.Godowns.SingleOrDefault(g => g.Name == "Bay 2"));
    }

    /// <summary>
    /// The cost grid's parent IS the category — the vendor documents multi create/alter for Cost or Profit
    /// Centres but no column list, so the column SET is ours and labelled. Every centre it creates is top-level,
    /// also labelled: a per-row parent picker would have to be re-scoped per row to keep the domain's "a parent
    /// must be in the SAME category" invariant.
    /// </summary>
    [Fact]
    public void Two_cost_centres_are_created_under_the_header_category_and_persist()
    {
        const string companyName = "Multi Cost Co";
        var vm = NewSeededCompany(companyName);
        vm.ShowMultiCostCentreCreate();
        var m = vm.MultiMasterCreate!;

        Assert.Equal("Under Category", m.UnderHeaderLabel);
        Assert.Equal("Category", m.UnderColumnHeader);

        var primary = m.UnderGroupOptions.Single(o => o.Parent?.Name == vm.Company!.PrimaryCostCategoryName);
        m.SelectedUnderGroup = primary;
        m.Rows[0].Name = "North Region";
        m.Rows[1].Name = "South Region";

        Assert.True(m.Accept(), m.Message);

        var book = Reload(companyName);
        var north = book.CostCentres.Single(c => c.Name == "North Region");
        Assert.Equal(primary.Parent!.Id, north.CategoryId);
        Assert.Null(north.ParentId);                          // top-level, the labelled divergence
        Assert.NotNull(book.CostCentres.SingleOrDefault(c => c.Name == "South Region"));
    }

    // =========================================================== (4) 🔴 THE MULTI ALTER MUTATION PROOFS

    /// <summary>
    /// 🔴 <b>THE CENTRAL TEST OF THIS SLICE. It fails on the T2-105 mutant.</b>
    ///
    /// <para>Two ledgers exist. The alteration grid loads them, the operator renames one and changes its opening
    /// balance, and Ctrl+A is accepted. Then the company is <b>re-read from SQLite</b> and three things are
    /// asserted that together can only be true of a real ALTER:</para>
    /// <list type="number">
    ///   <item><b>the ledger COUNT is unchanged</b> — a grid that ran <c>Create()</c> would have appended a third
    ///     ledger (or been refused by the duplicate-name guard, which is the fifth shape of the same defect);</item>
    ///   <item><b>the ORIGINAL Guid still carries the row</b> — a grid that wrote to a fresh master would leave
    ///     the original untouched and every historical voucher pointing at the stale name;</item>
    ///   <item><b>a later read returns the CORRECTED values</b> — name, group and opening balance.</item>
    /// </list>
    /// <para><b>Mutation:</b> changing <c>Accept()</c> from <c>IsAltering ? AlterBatch() : CreateBatch()</c> to a
    /// bare <c>CreateBatch()</c> — byte-for-byte the T2-105 defect — fails this test on assertion (1).</para>
    /// </summary>
    [Fact]
    public void Multi_Ledger_Alteration_updates_the_ORIGINAL_ledger_and_a_later_read_returns_it()
    {
        const string companyName = "Multi Alter Ledger Co";
        var vm = NewSeededCompany(companyName);

        // Two ledgers under Sundry Debtors, created through the CREATE grid so the fixture uses the real path.
        vm.ShowMultiLedgerCreate();
        var create = vm.MultiMasterCreate!;
        var debtors = create.UnderGroupOptions.Single(o => o.Parent?.Name == "Sundry Debtors");
        create.SelectedUnderGroup = debtors;
        create.Rows[0].Name = "Acme Traders";
        create.Rows[0].OpeningBalanceText = "41237.53";
        create.Rows[1].Name = "Bharat Stores";
        Assert.True(create.Accept(), create.Message);

        var ledgerCountBefore = vm.Company!.Ledgers.Count;
        var acmeId = vm.Company!.FindLedgerByName("Acme Traders")!.Id;
        var creditors = vm.Company!.Groups.Single(g => g.Name == "Sundry Creditors");

        // --- now ALTER.
        vm.ShowMultiLedgerAlter();
        var m = vm.MultiMasterCreate!;
        Assert.True(m.IsAltering);
        Assert.Equal("Multi Ledger Alteration", m.Title);
        Assert.Equal("Alter All (Ctrl+A)", m.CommitLabel);

        // The grid is LOADED from the company, and carries NO trailing blank row — Multi Alter corrects what
        // exists and never creates.
        Assert.All(m.Rows, r => Assert.True(r.IsExisting, "an alteration row must carry the master's Guid."));
        Assert.DoesNotContain(m.Rows, r => r.IsBlank);

        // 🔴 It loads every ledger whose group this picker can REPRESENT, and exactly those. The seeded
        // "Profit & Loss A/c" sits under Company.ProfitAndLossHead, a reserved Group deliberately absent from
        // Company.Groups, so no group picker in this application can offer it. The first draft loaded it anyway
        // and refused the whole untouched batch with "Row 4 (Profit & Loss A/c): pick a group". Excluding it is
        // asserted here rather than left as a row-count coincidence.
        var listable = vm.Company!.Ledgers
            .Where(l => vm.Company!.Groups.Any(g => g.Id == l.GroupId))
            .ToList();
        Assert.Equal(listable.Count, m.Rows.Count);
        Assert.True(listable.Count < ledgerCountBefore,
            "fixture guard: a seeded ledger must sit under the reserved head for the exclusion to be tested.");
        var pnl = vm.Company!.FindLedgerByName("Profit & Loss A/c");
        Assert.NotNull(pnl);
        Assert.DoesNotContain(m.Rows, r => r.ExistingId == pnl!.Id);

        var acmeRow = m.Rows.Single(r => r.ExistingId == acmeId);
        Assert.Equal("Acme Traders", acmeRow.Name);
        Assert.Equal("41237.53", acmeRow.OpeningBalanceText);   // loaded as the box shows it
        Assert.False(acmeRow.IsChanged);

        acmeRow.Name = "Acme Traders (Renamed)";
        acmeRow.Under = m.UnderOptions.Single(p => p.Id == creditors.Id);
        acmeRow.OpeningBalanceText = "5000.00";
        acmeRow.OpeningIsCredit = true;
        Assert.True(acmeRow.IsChanged);

        Assert.True(m.Accept(), m.Message);
        Assert.False(m.MessageIsError);
        Assert.Equal("1 ledger altered.", m.Message);

        // ================= 🔴 off the book, re-read from SQLite.
        var book = Reload(companyName);

        // (1) nothing was appended — the T2-105 discriminator.
        Assert.Equal(ledgerCountBefore, book.Ledgers.Count);
        Assert.Null(book.FindLedgerByName("Acme Traders"));          // the old name is GONE, not duplicated

        // (2) the ORIGINAL Guid carries the correction, so every historical voucher follows the rename.
        var acme = book.Ledgers.Single(l => l.Id == acmeId);

        // (3) a later read returns the corrected values.
        Assert.Equal("Acme Traders (Renamed)", acme.Name);
        Assert.Equal(creditors.Id, acme.GroupId);
        Assert.Equal(5000.00m, acme.OpeningBalance.Amount);
        Assert.False(acme.OpeningIsDebit);

        // The untouched ledger was not rewritten into something else either.
        Assert.NotNull(book.FindLedgerByName("Bharat Stores"));
    }

    /// <summary>
    /// 🔴 <b>THE FIFTH SHAPE OF T2-105, pinned.</b> Where the operator edits only a SECONDARY field and leaves
    /// the name alone, a <c>Create()</c> would not duplicate — it would be REFUSED by the engine's duplicate-name
    /// guard, so the screen would show "already exists" over an alteration and <b>the correction would be
    /// silently lost</b>. That shape was found live on the inventory masters in wave 46 and it has its own test
    /// there; this is its multi-master twin.
    /// </summary>
    [Fact]
    public void Altering_only_the_opening_balance_without_renaming_succeeds_and_persists()
    {
        const string companyName = "Multi Alter Secondary Co";
        var vm = NewSeededCompany(companyName);

        vm.ShowMultiLedgerCreate();
        var create = vm.MultiMasterCreate!;
        create.SelectedUnderGroup =
            create.UnderGroupOptions.Single(o => o.Parent?.Name == "Sundry Debtors");
        create.Rows[0].Name = "Chetan & Co";
        Assert.True(create.Accept(), create.Message);

        var id = vm.Company!.FindLedgerByName("Chetan & Co")!.Id;
        var countBefore = vm.Company!.Ledgers.Count;

        vm.ShowMultiLedgerAlter();
        var m = vm.MultiMasterCreate!;
        var row = m.Rows.Single(r => r.ExistingId == id);
        row.OpeningBalanceText = "1234.56";                  // the name is NOT touched

        Assert.True(m.Accept(), m.Message);

        var book = Reload(companyName);
        Assert.Equal(countBefore, book.Ledgers.Count);
        var ledger = book.Ledgers.Single(l => l.Id == id);
        Assert.Equal("Chetan & Co", ledger.Name);            // unchanged, and not refused for existing
        Assert.Equal(1234.56m, ledger.OpeningBalance.Amount);
    }

    /// <summary>
    /// Accepting a grid in which nothing was edited writes nothing and says so. Without the
    /// <see cref="MultiMasterRowViewModel.IsChanged"/> filter this would rewrite every master on screen —
    /// harmless here, but it is also what would push an untouched PREDEFINED master through a guard it was never
    /// going to trip.
    /// </summary>
    [Fact]
    public void Accepting_an_unedited_alteration_grid_writes_nothing()
    {
        var vm = NewSeededCompany("Multi Alter NoOp Co");

        vm.ShowMultiLedgerCreate();
        var create = vm.MultiMasterCreate!;
        create.SelectedUnderGroup =
            create.UnderGroupOptions.Single(o => o.Parent?.Name == "Sundry Debtors");
        create.Rows[0].Name = "Delta Ltd";
        Assert.True(create.Accept(), create.Message);

        vm.ShowMultiLedgerAlter();
        var m = vm.MultiMasterCreate!;

        Assert.False(m.Accept());
        Assert.False(m.MessageIsError);                      // not a failure — there was simply nothing to do
        Assert.Equal("Nothing changed — edit a row, then press Ctrl+A.", m.Message);
    }

    /// <summary>
    /// 🔴 <b>A reserved ledger cannot be renamed through the batch either.</b> ~14 engine sites resolve a ledger
    /// through a hardcoded well-known name and fail SILENTLY afterwards — <c>B2cQrService</c> returns ₹0 rounding
    /// when it cannot find "Round Off". A Multi Alter that bypassed
    /// <c>MasterAlterationRules.EnsureLedgerRenameAllowed</c> would be the easiest possible way to trip that, in a
    /// batch of fifty where nobody is looking at any one row.
    /// </summary>
    [Fact]
    public void Renaming_a_predefined_ledger_through_the_batch_is_refused_and_nothing_is_written()
    {
        const string companyName = "Multi Alter Reserved Co";
        var vm = NewSeededCompany(companyName);
        var cash = vm.Company!.FindLedgerByName("Cash");
        Assert.NotNull(cash);
        Assert.True(cash!.IsPredefined, "fixture guard: Cash must be predefined for this test to mean anything.");

        vm.ShowMultiLedgerAlter();
        var m = vm.MultiMasterCreate!;
        var row = m.Rows.Single(r => r.ExistingId == cash.Id);
        row.Name = "Petty Cash";

        Assert.False(m.Accept());
        Assert.True(m.MessageIsError);
        Assert.Contains("cannot be renamed", m.Message);

        var book = Reload(companyName);
        Assert.NotNull(book.FindLedgerByName("Cash"));
        Assert.Null(book.FindLedgerByName("Petty Cash"));
    }

    /// <summary>One name typed over two rows of an alteration batch is a clash the engine cannot see — neither
    /// row is new, so each passes the except-self check on its own. The batch-level pass catches it.</summary>
    [Fact]
    public void Two_alteration_rows_renamed_to_the_same_name_refuse_the_batch()
    {
        const string companyName = "Multi Alter Clash Co";
        var vm = NewSeededCompany(companyName);

        vm.ShowMultiLedgerCreate();
        var create = vm.MultiMasterCreate!;
        create.SelectedUnderGroup =
            create.UnderGroupOptions.Single(o => o.Parent?.Name == "Sundry Debtors");
        create.Rows[0].Name = "Echo Ltd";
        create.Rows[1].Name = "Foxtrot Ltd";
        Assert.True(create.Accept(), create.Message);

        vm.ShowMultiLedgerAlter();
        var m = vm.MultiMasterCreate!;
        m.Rows.Single(r => r.Name == "Echo Ltd").Name = "Golf Ltd";
        m.Rows.Single(r => r.Name == "Foxtrot Ltd").Name = "Golf Ltd";

        Assert.False(m.Accept());
        Assert.True(m.MessageIsError);
        Assert.Contains("entered twice in this batch", m.Message);

        var book = Reload(companyName);
        Assert.NotNull(book.FindLedgerByName("Echo Ltd"));
        Assert.NotNull(book.FindLedgerByName("Foxtrot Ltd"));
        Assert.Null(book.FindLedgerByName("Golf Ltd"));
    }

    /// <summary>Clearing an alteration row's name is a refusal, not a silently skipped row.</summary>
    [Fact]
    public void Clearing_an_alteration_rows_name_refuses_the_batch()
    {
        var vm = NewSeededCompany("Multi Alter Blank Co");

        vm.ShowMultiLedgerCreate();
        var create = vm.MultiMasterCreate!;
        create.SelectedUnderGroup =
            create.UnderGroupOptions.Single(o => o.Parent?.Name == "Sundry Debtors");
        create.Rows[0].Name = "Hotel Ltd";
        Assert.True(create.Accept(), create.Message);

        vm.ShowMultiLedgerAlter();
        var m = vm.MultiMasterCreate!;
        var row = m.Rows.Single(r => r.Name == "Hotel Ltd");
        row.Name = string.Empty;

        Assert.False(row.IsBlank, "an alteration row stands for a master that exists — it is never 'blank'.");
        Assert.False(m.Accept());
        Assert.True(m.MessageIsError);
        Assert.Contains("name is required", m.Message);
    }

    // =========================================================== (5) 🔴 Alt+B — Zero Opening Balance

    /// <summary>
    /// 🔴 <b>Alt+B (Zero Opening Balance)</b> — vendor-attested verbatim: "<i>To remove the opening balances from
    /// all the ledgers, press Alt+B (Zero Opening Balance)</i>".
    ///
    /// <para>It <b>stages</b> the change and writes nothing until Ctrl+A, like every other edit on the screen —
    /// so the test asserts that the book is untouched immediately after the chord, and corrected only after the
    /// accept, re-read from SQLite.</para>
    /// </summary>
    [Fact]
    public void AltB_zeroes_every_opening_balance_on_the_grid_and_only_CtrlA_writes_it()
    {
        const string companyName = "Multi Zero OB Co";
        var vm = NewSeededCompany(companyName);

        vm.ShowMultiLedgerCreate();
        var create = vm.MultiMasterCreate!;
        create.SelectedUnderGroup =
            create.UnderGroupOptions.Single(o => o.Parent?.Name == "Sundry Debtors");
        create.Rows[0].Name = "India Ltd";
        create.Rows[0].OpeningBalanceText = "1000.00";
        create.Rows[1].Name = "Juliet Ltd";
        create.Rows[1].OpeningBalanceText = "2500.75";
        Assert.True(create.Accept(), create.Message);

        vm.ShowMultiLedgerAlter();
        var m = vm.MultiMasterCreate!;
        Assert.True(m.ShowsZeroOpeningBalance, "the chord's own surface is the ledger ALTERATION screen.");

        Assert.True(m.ZeroOpeningBalances());
        Assert.False(m.MessageIsError);
        Assert.All(m.Rows, r => Assert.Equal(string.Empty, r.OpeningBalanceText));

        // Staged only — the book still holds the balances.
        var staged = Reload(companyName);
        Assert.Equal(1000.00m, staged.FindLedgerByName("India Ltd")!.OpeningBalance.Amount);
        Assert.Equal(2500.75m, staged.FindLedgerByName("Juliet Ltd")!.OpeningBalance.Amount);

        Assert.True(m.Accept(), m.Message);

        var book = Reload(companyName);
        Assert.Equal(0m, book.FindLedgerByName("India Ltd")!.OpeningBalance.Amount);
        Assert.Equal(0m, book.FindLedgerByName("Juliet Ltd")!.OpeningBalance.Amount);
    }

    /// <summary>
    /// The chord belongs to the ledger ALTERATION screen only — the surface the vendor documents it on. It is a
    /// no-op on the create grid and on the other four families, and the gate lives in the view model so the key
    /// arm cannot be the only thing enforcing it.
    /// </summary>
    [Theory]
    [InlineData(MultiMasterKind.Ledger, MultiMasterMode.Create)]
    [InlineData(MultiMasterKind.AccountGroup, MultiMasterMode.Alter)]
    [InlineData(MultiMasterKind.Godown, MultiMasterMode.Alter)]
    [InlineData(MultiMasterKind.CostCentre, MultiMasterMode.Alter)]
    public void AltB_is_offered_nowhere_but_the_ledger_alteration_screen(
        MultiMasterKind kind, MultiMasterMode mode)
    {
        var vm = NewSeededCompany("Multi Zero Scope Co " + kind + mode);
        var grid = new MultiMasterCreateViewModel(kind, mode, vm.Company!, _storage, () => { });

        Assert.False(grid.ShowsZeroOpeningBalance);
        Assert.False(grid.ZeroOpeningBalances());
    }

    // =========================================================== (6) 🔴 THE T2-77 CLASS — preserved fields

    /// <summary>
    /// 🔴 <b>THE T2-77 CLASS, THIRD INSTANCE — AND THE WORST OF THE THREE.</b>
    ///
    /// <para><c>GroupService.AlterGroup</c> takes <c>alias</c>, <c>primaryNature</c> and <c>behaviour</c> as
    /// OPTIONAL parameters and writes every one of them unconditionally —
    /// <c>behaviour ?? GroupBehaviour.None</c> then <c>ApplyBehaviour</c>, which assigns all five flags. So a
    /// Multi Group Alter that called <c>AlterGroup(id, name, parentId)</c> to re-parent a batch would
    /// <b>silently wipe every behavioural flag and every alias in it</b>, with no error and no refusal.</para>
    ///
    /// <para><b>Mutation:</b> dropping the <c>alias</c> and <c>behaviour</c> arguments from the group arm of
    /// <c>WriteAlterBatch</c> — the natural way to write that call — fails this test on the alias and on all four
    /// flags.</para>
    /// </summary>
    [Fact]
    public void Altering_a_group_preserves_its_alias_and_every_behavioural_flag()
    {
        const string companyName = "Multi Alter Group Flags Co";
        var vm = NewSeededCompany(companyName);

        // A custom group under Sundry Debtors, carrying an alias and every behavioural flag this grid cannot see.
        var debtors = vm.Company!.Groups.Single(g => g.Name == "Sundry Debtors");
        var groups = new GroupService(vm.Company!);
        var regional = groups.CreateGroup(
            "Regional Debtors", debtors.Id, alias: "RD",
            behaviour: new GroupBehaviour(
                BehavesLikeSubLedger: true,
                NettBalancesForReporting: true,
                UsedForCalculation: true,
                AffectsGrossProfits: false));
        _storage.Save(vm.Company!);
        var regionalId = regional.Id;

        var creditors = vm.Company!.Groups.Single(g => g.Name == "Sundry Creditors");

        vm.ShowMultiGroupAlter();
        var m = vm.MultiMasterCreate!;

        // PRIMARY groups are deliberately not loaded — they have no parent to show and their Nature is not
        // alterable here, so a row for one could only ever be refused.
        Assert.DoesNotContain(m.Rows, r => vm.Company!.FindGroup(r.ExistingId)?.ParentId is null);

        var row = m.Rows.Single(r => r.ExistingId == regionalId);
        row.Name = "Regional Debtors (East)";
        row.Under = m.UnderOptions.Single(p => p.Id == creditors.Id);

        Assert.True(m.Accept(), m.Message);

        var book = Reload(companyName);
        var altered = book.Groups.Single(g => g.Id == regionalId);

        Assert.Equal("Regional Debtors (East)", altered.Name);
        Assert.Equal(creditors.Id, altered.ParentId);

        // 🔴 Everything the grid does not show survived the alteration.
        Assert.Equal("RD", altered.Alias);
        Assert.True(altered.BehavesLikeSubLedger);
        Assert.True(altered.NettBalancesForReporting);
        Assert.True(altered.UsedForCalculation);
        Assert.False(altered.AffectsGrossProfits);
    }

    /// <summary>
    /// 🔴 <b>THE T2-77 CLASS, SECOND INSTANCE.</b> <c>InventoryService.AlterGodown</c>'s <c>alias</c>,
    /// <c>thirdParty</c> and <c>jobCostCentreId</c> are optional and written unconditionally, so omitting them
    /// clears a stored alias, un-flags a third-party godown and detaches its job/project cost centre — none of
    /// which this grid shows.
    ///
    /// <para><b>Mutation:</b> reducing the call in the godown arm of <c>WriteAlterBatch</c> to
    /// <c>AlterGodown(p.Id, p.Name, p.Under.Id)</c> fails this test on all three.</para>
    /// </summary>
    [Fact]
    public void Altering_a_godown_preserves_its_alias_third_party_flag_and_job_cost_centre()
    {
        const string companyName = "Multi Alter Godown Fields Co";
        var vm = NewSeededCompany(companyName);

        var centre = new CostCentre(
            Guid.NewGuid(), "Site 9", vm.Company!.CostCategories.Single().Id, parentId: null, alias: null);
        vm.Company!.AddCostCentre(centre);

        var inventory = new InventoryService(vm.Company!);
        var warehouse = inventory.CreateGodown(
            "Contract Warehouse", parentId: null, alias: "CW", thirdParty: true, jobCostCentreId: centre.Id);
        _storage.Save(vm.Company!);
        var warehouseId = warehouse.Id;

        var main = vm.Company!.Godowns.Single(g => g.IsMainLocation);

        vm.ShowMultiGodownAlter();
        var m = vm.MultiMasterCreate!;
        var row = m.Rows.Single(r => r.ExistingId == warehouseId);
        row.Name = "Contract Warehouse (North)";
        row.Under = m.UnderOptions.Single(p => p.Id == main.Id);

        Assert.True(m.Accept(), m.Message);

        var book = Reload(companyName);
        var altered = book.Godowns.Single(g => g.Id == warehouseId);

        Assert.Equal("Contract Warehouse (North)", altered.Name);
        Assert.Equal(main.Id, altered.ParentId);

        // 🔴 Everything the grid does not show survived.
        Assert.Equal("CW", altered.Alias);
        Assert.True(altered.ThirdParty);
        Assert.Equal(centre.Id, altered.JobCostCentreId);
    }

    /// <summary>
    /// 🔴 <b>T2-77 ITSELF — the ticket named this exact caller.</b>
    /// <c>CostMasterService.AlterCostCentre</c> declared <c>string? alias = null</c> while writing
    /// <c>centre.Alias</c> unconditionally, and T2-77's stated failure scenario was "<i>the Multi Alter verb that
    /// row 2.12 names as a gap … calls the 4-argument overload to re-parent a batch and EVERY alias in the batch
    /// becomes null</i>". The parameter is now REQUIRED, so that overload no longer exists and the omission cannot
    /// compile. This test pins the behaviour the requirement protects — including the centre's PARENT, which the
    /// grid also does not show.
    /// </summary>
    [Fact]
    public void Altering_a_cost_centre_preserves_its_alias_and_its_parent()
    {
        const string companyName = "Multi Alter Cost Fields Co";
        var vm = NewSeededCompany(companyName);

        var categoryId = vm.Company!.CostCategories.Single().Id;
        var parent = new CostCentre(Guid.NewGuid(), "West Zone", categoryId, null, null);
        var child = new CostCentre(Guid.NewGuid(), "West Branch", categoryId, parent.Id, "WB");
        vm.Company!.AddCostCentre(parent);
        vm.Company!.AddCostCentre(child);
        _storage.Save(vm.Company!);

        vm.ShowMultiCostCentreAlter();
        var m = vm.MultiMasterCreate!;
        var row = m.Rows.Single(r => r.ExistingId == child.Id);
        row.Name = "West Branch (Renamed)";

        Assert.True(m.Accept(), m.Message);

        var book = Reload(companyName);
        var altered = book.CostCentres.Single(c => c.Id == child.Id);

        Assert.Equal("West Branch (Renamed)", altered.Name);
        Assert.Equal("WB", altered.Alias);                   // 🔴 T2-77's own failure, pinned
        Assert.Equal(parent.Id, altered.ParentId);           // the grid shows no parent column either
        Assert.Equal(categoryId, altered.CategoryId);
        Assert.Equal(2, book.CostCentres.Count);             // not duplicated
    }

    /// <summary>
    /// 🔴 <b>A stock-item alteration must NOT re-apply an opening quantity.</b> Re-running the opening allocation
    /// on every accept would add a second one and multiply the item's opening stock — which is closing stock,
    /// which is the Balance Sheet. The grid therefore does not offer the column on the alter verb at all, and
    /// the base unit is not re-assigned either (changing it would silently reinterpret every recorded quantity).
    /// </summary>
    [Fact]
    public void Altering_a_stock_item_renames_it_without_touching_its_opening_stock_or_base_unit()
    {
        const string companyName = "Multi Alter Item Co";
        var vm = NewSeededCompany(companyName);
        var (groupId, unitId) = SeedInventoryBasics(vm.Company!);
        var second = new InventoryService(vm.Company!).CreateStockGroup("Raw Materials");

        vm.ShowMultiStockItemCreate();
        var create = vm.MultiMasterCreate!;
        create.SelectedUnderGroup = create.UnderGroupOptions.Single(o => o.Parent?.Id == groupId);
        create.Rows[0].Name = "Kilo Widget";
        create.Rows[0].Unit = create.UnitOptions.Single();
        create.Rows[0].OpeningQuantityText = "7";
        create.Rows[0].OpeningRateText = "100.00";
        Assert.True(create.Accept(), create.Message);

        var itemId = vm.Company!.FindStockItemByName("Kilo Widget")!.Id;

        vm.ShowMultiStockItemAlter();
        var m = vm.MultiMasterCreate!;
        Assert.False(m.ShowsOpeningQuantity, "an alter must not offer a column that would double opening stock.");
        Assert.False(m.ShowsUnits, "re-assigning the base unit would reinterpret every recorded quantity.");

        var row = m.Rows.Single(r => r.ExistingId == itemId);
        row.Name = "Kilo Widget Mk II";
        row.Under = m.UnderOptions.Single(p => p.Id == second.Id);

        Assert.True(m.Accept(), m.Message);

        var book = Reload(companyName);
        Assert.Single(book.StockItems);                      // not duplicated
        var item = book.StockItems.Single(i => i.Id == itemId);
        Assert.Equal("Kilo Widget Mk II", item.Name);
        Assert.Equal(second.Id, item.StockGroupId);
        Assert.Equal(unitId, item.BaseUnitId);               // untouched

        // 🔴 STILL exactly ONE opening allocation, at the original quantity.
        var openings = book.StockOpeningBalances.Where(o => o.StockItemId == itemId).ToList();
        Assert.Single(openings);
        Assert.Equal(7m, openings[0].Quantity);
    }

    // =========================================================== (7) the header as a filter on Alter

    /// <summary>
    /// On the ALTER verb the header is a <b>filter</b> over which masters load, and changing it reloads the grid.
    /// Ours and labelled — the vendor page does not say what the header does on the alteration screen.
    /// </summary>
    [Fact]
    public void On_Alter_the_header_filters_which_masters_load()
    {
        var vm = NewSeededCompany("Multi Alter Filter Co");

        vm.ShowMultiLedgerCreate();
        var create = vm.MultiMasterCreate!;
        create.SelectedUnderGroup =
            create.UnderGroupOptions.Single(o => o.Parent?.Name == "Sundry Debtors");
        create.Rows[0].Name = "Kilo Ltd";
        create.Rows[1].Name = "Lima Ltd";
        Assert.True(create.Accept(), create.Message);

        vm.ShowMultiLedgerAlter();
        var m = vm.MultiMasterCreate!;
        var allItemsCount = m.Rows.Count;
        Assert.True(allItemsCount > 2, "All Items must load every ledger, including the seeded ones.");
        Assert.Contains("load", m.UnderHeaderHint);

        m.SelectedUnderGroup = m.UnderGroupOptions.Single(o => o.Parent?.Name == "Sundry Debtors");
        Assert.Equal(2, m.Rows.Count);
        Assert.Contains(m.Rows, r => r.Name == "Kilo Ltd");
        Assert.Contains(m.Rows, r => r.Name == "Lima Ltd");

        // A group with nothing under it says so rather than showing a silently blank screen.
        m.SelectedUnderGroup = m.UnderGroupOptions.Single(o => o.Parent?.Name == "Sundry Creditors");
        Assert.Empty(m.Rows);
        Assert.False(m.MessageIsError);
        Assert.Equal("No ledgers sit under Sundry Creditors.", m.Message);
    }

    /// <summary>
    /// A family with nothing to alter says so plainly. A new company has no stock items, so the Multi Stock Item
    /// Alteration screen must announce that rather than render an empty grid with an Alter button on it.
    /// </summary>
    [Fact]
    public void An_alteration_grid_with_nothing_to_alter_says_so_and_refuses()
    {
        var vm = NewSeededCompany("Multi Alter Empty Co");
        vm.ShowMultiStockItemAlter();
        var m = vm.MultiMasterCreate!;

        Assert.Empty(m.Rows);
        Assert.Equal("This company has no stock items to alter yet.", m.Message);

        Assert.False(m.Accept());
        Assert.True(m.MessageIsError);
        Assert.Contains("no stock items on this screen to alter", m.Message);
    }
}
