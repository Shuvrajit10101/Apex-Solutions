using System;
using System.IO;
using System.Linq;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Services;
using Apex.Desktop.Services;
using Apex.Desktop.ViewModels;
using Apex.Desktop.Views;
using Xunit;
using DomainLedger = Apex.Ledger.Domain.Ledger;

namespace Apex.Desktop.Tests;

/// <summary>
/// 🔴 <b>WAVE 33 / CLUSTER C3 — THE EIGHT RESIDUAL MASTERS' DELETE VERB, DRIVEN BY REAL KEYSTROKES ON A REAL
/// WINDOW.</b> Census 2.9, 2.10, 2.11, 3.8, 3.9, 3.10, 3.11, 3.12.
///
/// <para><b>Why not one assertion here calls a delete service directly.</b> The defect this cluster closes is
/// not "the engine cannot delete a batch" — five of the eight delete services already existed in
/// <c>Apex.Ledger</c>, and every one of them had <b>zero callers in <c>Apex.Desktop</c></b>. A test that called
/// <c>BatchService.DeleteBatch</c> would have been green for the whole period in which no operator could delete
/// a batch by any sequence of keys. So every test below opens the real <see cref="MainWindow"/>, walks the
/// Gateway cascade with arrows and Enter, walks the existing-list with ArrowDown, and presses the real
/// accelerator — Alt+D, then Y — exactly as an operator would.</para>
///
/// <para>🔴 <b>EVERY TEST IN THIS FILE FAILS ON TODAY'S <c>main</c>, and it fails at a specific place worth
/// naming.</b> On main <c>MainWindowViewModel.MasterListScreen</c> resolves <c>null</c> on all eight of these
/// screens (its switch falls through to <c>PayrollMasterScreen</c>, which is also null there), so:
/// <c>IsDeleteTargetPage</c> is false, the window's Alt+D arm never fires, <c>RequestDeleteHighlighted</c>
/// returns false from its <c>_ =&gt; false</c> default, and ArrowDown falls through to the Gateway-cascade
/// branch instead of moving a list highlight. The <c>Assert.NotNull(vm.MasterListScreen)</c> in
/// <see cref="OpenMaster"/> is therefore the line that goes red first, on every one of them.</para>
///
/// <para><b>THE REFUSAL TESTS ARE THE IMPORTANT HALF, and the notice assertions more than the survival ones.</b>
/// A sibling review found that no test anywhere asserted the refusal message was actually SHOWN: suppressing the
/// notice left thirteen of fifteen tests green while the operator would press Alt+D, answer Y, and read an empty
/// notice bar — a destructive verb that silently does nothing is worse than one that refuses out loud. So the
/// refusal tests here assert on <c>vm.Notice</c> by content, not merely that the master survived.</para>
///
/// <para><b>FIDELITY (R7 / ruling 14). 🔴 AMENDED — AN EARLIER DRAFT OF THIS PARAGRAPH SAID "NO VENDOR CLAIM IS
/// MADE IN THIS FILE … no URL is cited, because none was opened for these eight masters", AND THAT WAS TRUE OF
/// THE SLICE BUT UNDERSTATED THE AVAILABLE GROUNDING.</b> The vendor page for ONE of the eight was later opened
/// and read (2026-09-25) and it attests both the keystroke and the rule:
/// <i>"Press Alt+G (Go To) &gt; Alter Master &gt; Currency &gt; select the currency &gt; Press Alt+D (Delete)"</i>
/// and <i>"You can delete a currency if it is not used in transactions or opening balance for ledgers"</i>
/// [help.tallysolutions.com/create-alter-or-delete-currencies/]. So the CURRENCY row's delete verb, its
/// accelerator and its transaction-based refusal are <b>VENDOR-ATTESTED</b>, not ours.</para>
///
/// <para><b>The other seven remain OURS and are labelled so.</b> Alt+D is this application's established single
/// master-delete accelerator — attested for a godown by name, and now for a currency — and extending it to the
/// batch, BOM, budget, scenario, price level, price list and reorder masters, which the vendor documentation was
/// not consulted about, is an extension of an existing shape rather than an attested behaviour. <b>No vendor
/// citation is made or implied for those seven.</b> Where a refusal's exact wording is ours it is ours on the
/// currency too: see <c>MasterDeletionRules.EnsureCurrencyDeletable</c> for the base-currency and
/// rate-of-exchange clauses, which the vendor page does not describe.</para>
/// </summary>
public sealed class MasterVerbsW33ResidualDeleteTests
{
    private static (MainWindow Window, MainWindowViewModel Vm, string TempDir) NewWindow(string company)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "ApexW33C3_" + Guid.NewGuid().ToString("N"));
        var vm = new MainWindowViewModel(new CompanyStorage(tempDir));
        vm.NewCompanyName = company;
        vm.CreateCompany();
        vm.ShowGateway();

        var window = new MainWindow { DataContext = vm };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return (window, vm, tempDir);
    }

    private static void Key(MainWindow window, PhysicalKey key, RawInputModifiers mods = RawInputModifiers.None)
    {
        window.KeyPressQwerty(key, mods);
        Dispatcher.UIThread.RunJobs();
    }

    private static void Cleanup(string dir)
    {
        try
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private static string? ActiveLabel(MainWindowViewModel vm) =>
        vm.Columns[vm.ActiveColumnIndex].Selected?.Label;

    private static void ArrowToAndEnter(MainWindow window, MainWindowViewModel vm, string label)
    {
        var rows = vm.Columns[vm.ActiveColumnIndex].Items.Count + 2;
        for (var i = 0; i < rows; i++)
        {
            if (ActiveLabel(vm) == label) { Key(window, PhysicalKey.Enter); return; }
            Key(window, PhysicalKey.ArrowDown);
        }
        Assert.Fail($"'{label}' was not reachable by arrow navigation from the active column.");
    }

    /// <summary>Masters → Create → <paramref name="label"/>, by keyboard only, then the assertion that this
    /// whole cluster is about: the screen is ON the shared arm. On main that <c>NotNull</c> is what fails.</summary>
    private static void OpenMaster(MainWindow window, MainWindowViewModel vm, string label, Screen expected)
    {
        ArrowToAndEnter(window, vm, "Create");
        ArrowToAndEnter(window, vm, label);
        Assert.Equal(expected, vm.CurrentScreen);
        Assert.NotNull(vm.MasterListScreen);
        Assert.True(vm.IsDeleteTargetPage, "Alt+D must be live on this master's existing-list.");
    }

    /// <summary>Arrow-Down the existing-masters list until the highlight lands on <paramref name="name"/>,
    /// resolving through the SAME shared property the arrows themselves go through.</summary>
    private static void ArrowTo(MainWindow window, MainWindowViewModel vm, string name)
    {
        for (var i = 0; i < 60; i++)
        {
            if (vm.MasterListScreen?.HighlightedMasterRow?.MasterName == name) return;
            Key(window, PhysicalKey.ArrowDown);
        }
        Assert.Fail($"'{name}' was not reachable by arrow navigation on the existing-masters list.");
    }

    /// <summary>Alt+D then Y — the real destructive accelerator and the real single confirmation.</summary>
    private static void DeleteHighlighted(MainWindow window)
    {
        Key(window, PhysicalKey.D, RawInputModifiers.Alt);
        Key(window, PhysicalKey.Y);
    }

    /// <summary>
    /// 🔴 <b>THE UNSAVABLE-COMPANY CANARY, and it is not the same assertion as "the master is gone".</b> The shell
    /// saves inside the delete and catches a failing save into a NOTICE
    /// (<c>"Cannot delete: … Re-open the company before continuing."</c>) with the row <b>already removed from
    /// memory</b>. A test that asserted only the absence of the master would therefore pass on exactly the worst
    /// outcome this wave can produce: a foreign-key orphan making the open company permanently unsavable, reported
    /// to the operator as a refusal. So every SUCCESSFUL delete asserts the notice says <i>deleted</i> and then
    /// re-reads the book from SQLite.
    /// </summary>
    private static Company AssertDeletedAndPersisted(MainWindowViewModel vm, string dir, string company)
    {
        Assert.False(string.IsNullOrWhiteSpace(vm.Notice), "A completed delete must announce itself.");
        Assert.DoesNotContain("Cannot delete", vm.Notice!);
        Assert.Contains("deleted", vm.Notice!);

        var storage = new CompanyStorage(dir);
        storage.Save(vm.Company!);                       // a second save too — an orphan fails on EVERY save
        return storage.Load(new CompanyEntry(company, storage.PathForName(company)));
    }

    // ---------------------------------------------------------------- feature flags + domain seeding
    //
    // The masters are seeded through the ENGINE rather than through each screen's create form. That is a
    // deliberate boundary: the create forms already have their own tests, and what is unproven — and what this
    // file exists to prove — is that a master which EXISTS can now be reached and removed from the keyboard.

    private static void EnableInventoryFeatures(MainWindowViewModel vm)
    {
        vm.ShowGstConfig();
        var cfg = vm.GstConfig!;
        cfg.MaintainBatchwiseDetails = true;
        cfg.SetComponentsBom = true;
        cfg.EnableMultiplePriceLevels = true;
        vm.Back();
        vm.ShowGateway();
        Dispatcher.UIThread.RunJobs();
    }

    private static (StockItem Finished, StockItem Part) SeedItems(MainWindowViewModel vm)
    {
        var c = vm.Company!;
        var inv = new InventoryService(c);
        var group = c.StockGroups.FirstOrDefault() ?? inv.CreateStockGroup("Primary");
        var unit = c.Units.FirstOrDefault() ?? inv.CreateSimpleUnit("Nos", "Numbers");
        var finished = inv.CreateStockItem("Cabinet", group.Id, unit.Id);
        var part = inv.CreateStockItem("Hinge", group.Id, unit.Id);
        return (finished, part);
    }

    // ===================================================================== census 3.8 — BATCH

    [AvaloniaFact]
    public void A_batch_deletes_with_AltD_then_Y()
    {
        var (window, vm, dir) = NewWindow("W33 Batch Co");
        try
        {
            EnableInventoryFeatures(vm);
            var (finished, _) = SeedItems(vm);
            new BatchService(vm.Company!).CreateBatch(finished.Id, "LOT-1");

            OpenMaster(window, vm, "Batch", Screen.BatchMaster);
            ArrowTo(window, vm, "LOT-1 of Cabinet");
            DeleteHighlighted(window);

            Assert.DoesNotContain(vm.Company!.BatchMasters, b => b.BatchNumber == "LOT-1");
            var reloaded = AssertDeletedAndPersisted(vm, dir, "W33 Batch Co");
            Assert.DoesNotContain(reloaded.BatchMasters, b => b.BatchNumber == "LOT-1");
        }
        finally { window.Close(); Cleanup(dir); }
    }

    /// <summary>🔴 THE REFUSAL REACHES THE OPERATOR. A batch whose number is on a posted invoice line survives
    /// Alt+D + Y, the application does not crash, and the guard's own message is ON THE NOTICE BAR — not merely
    /// thrown somewhere the operator cannot see.</summary>
    [AvaloniaFact]
    public void A_batch_with_stock_movements_is_refused_and_says_why_on_the_notice_bar()
    {
        var (window, vm, dir) = NewWindow("W33 Batch Guard Co");
        try
        {
            EnableInventoryFeatures(vm);
            var (finished, _) = SeedItems(vm);
            var c = vm.Company!;
            new BatchService(c).CreateBatch(finished.Id, "LOT-1");

            var party = new DomainLedger(Guid.NewGuid(), "W33 Buyer", c.FindGroupByName("Sundry Debtors")!.Id,
                                         Money.Zero, openingIsDebit: true);
            var sales = new DomainLedger(Guid.NewGuid(), "W33 Sales", c.FindGroupByName("Sales Accounts")!.Id,
                                         Money.Zero, openingIsDebit: false);
            c.AddLedger(party);
            c.AddLedger(sales);
            var godown = c.Godowns.First();
            var type = c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Sales);
            new LedgerService(c).Post(new Voucher(
                Guid.NewGuid(), type.Id, c.BooksBeginFrom,
                new[]
                {
                    new EntryLine(party.Id, Money.FromRupees(100m), DrCr.Debit),
                    new EntryLine(sales.Id, Money.FromRupees(100m), DrCr.Credit),
                },
                inventoryLines: new[]
                {
                    new VoucherInventoryLine(finished.Id, godown.Id, 1m, Money.FromRupees(100m),
                                             batchLabel: "LOT-1"),
                }));

            OpenMaster(window, vm, "Batch", Screen.BatchMaster);
            ArrowTo(window, vm, "LOT-1 of Cabinet");
            DeleteHighlighted(window);

            Assert.Contains(vm.Company!.BatchMasters, b => b.BatchNumber == "LOT-1");
            Assert.False(string.IsNullOrWhiteSpace(vm.Notice));
            Assert.Contains("LOT-1", vm.Notice!);
            Assert.Contains("cannot be deleted", vm.Notice!);
        }
        finally { window.Close(); Cleanup(dir); }
    }

    // ===================================================================== census 3.9 — BILL OF MATERIALS

    [AvaloniaFact]
    public void A_BOM_deletes_with_AltD_then_Y()
    {
        var (window, vm, dir) = NewWindow("W33 Bom Co");
        try
        {
            EnableInventoryFeatures(vm);
            var (finished, part) = SeedItems(vm);
            new BomService(vm.Company!).CreateBom(
                finished.Id, "Cabinet Standard", 1m,
                new[] { new BomLine(BomLineType.Component, part.Id, 4m) });

            OpenMaster(window, vm, "Bill of Materials", Screen.BomMaster);
            ArrowTo(window, vm, "Cabinet Standard of Cabinet");
            DeleteHighlighted(window);

            Assert.DoesNotContain(vm.Company!.BillsOfMaterials, b => b.Name == "Cabinet Standard");
            var reloaded = AssertDeletedAndPersisted(vm, dir, "W33 Bom Co");
            Assert.DoesNotContain(reloaded.BillsOfMaterials, b => b.Name == "Cabinet Standard");
        }
        finally { window.Close(); Cleanup(dir); }
    }

    /// <summary>
    /// 🔴 <b>THE MOST IMPORTANT TEST IN THIS FILE.</b> A BOM a job-work order was filled from must survive
    /// Alt+D + Y. On main this keystroke did not exist; the moment it did, without
    /// <c>MasterDeletionRules.EnsureBomDeletable</c> the BOM would have left memory while
    /// <c>job_work_orders.fill_components_bom_id</c> still held its Guid, and the OPEN COMPANY COULD NEVER HAVE
    /// BEEN SAVED AGAIN. The assertion that the book survives is the last line, and it is the point.
    /// </summary>
    [AvaloniaFact]
    public void A_BOM_a_job_work_order_was_filled_from_survives_AltD_and_the_book_still_saves()
    {
        var (window, vm, dir) = NewWindow("W33 Bom Guard Co");
        try
        {
            EnableInventoryFeatures(vm);
            var (finished, part) = SeedItems(vm);
            var c = vm.Company!;
            var bom = new BomService(c).CreateBom(
                finished.Id, "Cabinet Standard", 1m,
                new[] { new BomLine(BomLineType.Component, part.Id, 4m) });

            var jwType = c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.JobWorkOutOrder);
            c.AddInventoryVoucher(InventoryVoucher.JobWork(
                Guid.NewGuid(), jwType.Id, c.BooksBeginFrom,
                new JobWorkOrder(
                    JobWorkDirection.Out, "JW-1", finished.Id, 10m,
                    new[] { new JobWorkOrderLine(part.Id, JobWorkComponentTrack.PendingToIssue, 40m) },
                    fillComponentsBomId: bom.Id)));

            OpenMaster(window, vm, "Bill of Materials", Screen.BomMaster);
            ArrowTo(window, vm, "Cabinet Standard of Cabinet");
            DeleteHighlighted(window);

            Assert.Contains(vm.Company!.BillsOfMaterials, b => b.Id == bom.Id);
            Assert.False(string.IsNullOrWhiteSpace(vm.Notice));
            Assert.Contains("Cabinet Standard", vm.Notice!);
            Assert.Contains("job-work order", vm.Notice!);

            // 🔴 THE BOOK STILL WRITES. A refused delete that had already mutated the aggregate would fail here
            // with SQLITE_CONSTRAINT_FOREIGNKEY, and would fail on every later save too.
            new CompanyStorage(dir).Save(vm.Company!);
        }
        finally { window.Close(); Cleanup(dir); }
    }

    // ===================================================================== census 2.11 — CURRENCY

    [AvaloniaFact]
    public void An_unreferenced_currency_deletes_with_AltD_then_Y()
    {
        var (window, vm, dir) = NewWindow("W33 Currency Co");
        try
        {
            vm.Company!.AddCurrency(new Currency(Guid.NewGuid(), "€", "EUR"));

            OpenMaster(window, vm, "Currency", Screen.CurrencyMaster);
            ArrowTo(window, vm, "EUR (€)");
            DeleteHighlighted(window);

            Assert.DoesNotContain(vm.Company!.Currencies, c => c.FormalName == "EUR");
            var reloaded = AssertDeletedAndPersisted(vm, dir, "W33 Currency Co");
            Assert.DoesNotContain(reloaded.Currencies, c => c.FormalName == "EUR");
        }
        finally { window.Close(); Cleanup(dir); }
    }

    /// <summary>🔴 THE BASE CURRENCY SURVIVES Alt+D AND THE OPERATOR IS TOLD WHY. Deleting it would leave every
    /// figure in the book with no unit, so the refusal is outright rather than a count.</summary>
    [AvaloniaFact]
    public void The_base_currency_is_refused_at_the_keyboard_and_says_why()
    {
        var (window, vm, dir) = NewWindow("W33 Base Currency Co");
        try
        {
            var baseName = vm.Company!.BaseCurrency!;
            OpenMaster(window, vm, "Currency", Screen.CurrencyMaster);
            ArrowTo(window, vm, $"{baseName.FormalName} ({baseName.Symbol})");
            DeleteHighlighted(window);

            Assert.Contains(vm.Company!.Currencies, c => c.IsBaseCurrency);
            Assert.False(string.IsNullOrWhiteSpace(vm.Notice));
            Assert.Contains("base currency", vm.Notice!);
        }
        finally { window.Close(); Cleanup(dir); }
    }

    /// <summary>A currency a ledger is denominated in survives, and the notice names the blocking category —
    /// <c>ledgers.currency_id</c> is a real foreign key and an orphan there is an unsavable book.</summary>
    [AvaloniaFact]
    public void A_currency_a_ledger_is_denominated_in_is_refused_at_the_keyboard()
    {
        var (window, vm, dir) = NewWindow("W33 Currency Guard Co");
        try
        {
            var c = vm.Company!;
            var usd = new Currency(Guid.NewGuid(), "$", "USD");
            c.AddCurrency(usd);
            c.AddLedger(new DomainLedger(Guid.NewGuid(), "US Buyer", c.FindGroupByName("Sundry Debtors")!.Id,
                                         Money.Zero, openingIsDebit: true) { CurrencyId = usd.Id });

            OpenMaster(window, vm, "Currency", Screen.CurrencyMaster);
            ArrowTo(window, vm, "USD ($)");
            DeleteHighlighted(window);

            Assert.Contains(vm.Company!.Currencies, x => x.Id == usd.Id);
            Assert.Contains("ledger denominated in it", vm.Notice!);
            new CompanyStorage(dir).Save(vm.Company!);
        }
        finally { window.Close(); Cleanup(dir); }
    }

    // ===================================================================== census 2.9 — BUDGET

    [AvaloniaFact]
    public void A_budget_deletes_with_AltD_then_Y()
    {
        var (window, vm, dir) = NewWindow("W33 Budget Co");
        try
        {
            var c = vm.Company!;
            var ledger = c.Ledgers.First();
            c.AddBudget(new Budget(Guid.NewGuid(), "FY Budget", c.BooksBeginFrom, c.BooksBeginFrom.AddYears(1),
                lines: new[] { BudgetLine.ForLedger(ledger.Id, BudgetType.OnClosingBalance,
                                                    Money.FromRupees(1000m)) }));

            OpenMaster(window, vm, "Budget", Screen.BudgetMaster);
            ArrowTo(window, vm, "FY Budget");
            DeleteHighlighted(window);

            Assert.DoesNotContain(vm.Company!.Budgets, b => b.Name == "FY Budget");

            // The lines went with it — nothing is left to be re-inserted against a budget that is gone.
            new CompanyStorage(dir).Save(vm.Company!);
        }
        finally { window.Close(); Cleanup(dir); }
    }

    // ===================================================================== census 2.10 — SCENARIO

    [AvaloniaFact]
    public void A_scenario_deletes_with_AltD_then_Y_and_its_voucher_types_survive()
    {
        var (window, vm, dir) = NewWindow("W33 Scenario Co");
        try
        {
            var c = vm.Company!;
            var type = c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Journal);
            c.AddScenario(new Scenario(Guid.NewGuid(), "Provisional", includedTypeIds: new[] { type.Id }));

            OpenMaster(window, vm, "Scenario", Screen.ScenarioMaster);
            ArrowTo(window, vm, "Provisional");
            DeleteHighlighted(window);

            Assert.DoesNotContain(vm.Company!.Scenarios, s => s.Name == "Provisional");
            // The reference runs scenario → type, so the type is untouched.
            Assert.Contains(vm.Company!.VoucherTypes, t => t.Id == type.Id);
            new CompanyStorage(dir).Save(vm.Company!);
        }
        finally { window.Close(); Cleanup(dir); }
    }

    // ===================================================================== census 3.10 — PRICE LEVEL

    [AvaloniaFact]
    public void A_price_level_deletes_with_AltD_then_Y()
    {
        var (window, vm, dir) = NewWindow("W33 Price Level Co");
        try
        {
            EnableInventoryFeatures(vm);
            new PriceListService(vm.Company!).CreateLevel("Wholesale");

            OpenMaster(window, vm, "Price Level", Screen.PriceLevelsMaster);
            ArrowTo(window, vm, "Wholesale");
            DeleteHighlighted(window);

            Assert.DoesNotContain(vm.Company!.PriceLevels, l => l.Name == "Wholesale");
            var reloaded = AssertDeletedAndPersisted(vm, dir, "W33 Price Level Co");
            Assert.DoesNotContain(reloaded.PriceLevels, l => l.Name == "Wholesale");
        }
        finally { window.Close(); Cleanup(dir); }
    }

    /// <summary>A price level a party default points at survives — <c>ledgers.default_price_level_id</c> — and
    /// the operator is told which category blocked it.</summary>
    [AvaloniaFact]
    public void A_price_level_that_is_a_party_default_is_refused_at_the_keyboard()
    {
        var (window, vm, dir) = NewWindow("W33 Price Level Guard Co");
        try
        {
            EnableInventoryFeatures(vm);
            var c = vm.Company!;
            var level = new PriceListService(c).CreateLevel("Wholesale");
            c.AddLedger(new DomainLedger(Guid.NewGuid(), "Trade Buyer", c.FindGroupByName("Sundry Debtors")!.Id,
                                         Money.Zero, openingIsDebit: true) { DefaultPriceLevelId = level.Id });

            OpenMaster(window, vm, "Price Level", Screen.PriceLevelsMaster);
            ArrowTo(window, vm, "Wholesale");
            DeleteHighlighted(window);

            Assert.Contains(vm.Company!.PriceLevels, l => l.Id == level.Id);
            Assert.False(string.IsNullOrWhiteSpace(vm.Notice));
            Assert.Contains("Wholesale", vm.Notice!);
            new CompanyStorage(dir).Save(vm.Company!);
        }
        finally { window.Close(); Cleanup(dir); }
    }

    // ===================================================================== census 3.11 — PRICE LIST VERSION

    /// <summary>
    /// One dated version is withdrawn from the append-only history and the EARLIER version survives. The list
    /// the arrows walk here is the history for the chosen (level, item), which is the only list on this screen
    /// that holds deletable records.
    /// </summary>
    [AvaloniaFact]
    public void A_price_list_version_deletes_with_AltD_then_Y_and_the_earlier_version_survives()
    {
        var (window, vm, dir) = NewWindow("W33 Price List Co");
        try
        {
            EnableInventoryFeatures(vm);
            var (finished, _) = SeedItems(vm);
            var c = vm.Company!;
            var svc = new PriceListService(c);
            var level = svc.CreateLevel("Wholesale");
            var v1 = svc.AddOrReviseList(level.Id, finished.Id, c.BooksBeginFrom,
                new[] { new PriceListSlab(0m, null, Money.FromRupees(100m)) });
            var v2 = svc.AddOrReviseList(level.Id, finished.Id, c.BooksBeginFrom.AddMonths(3),
                new[] { new PriceListSlab(0m, null, Money.FromRupees(110m)) });

            OpenMaster(window, vm, "Price List", Screen.PriceListsMaster);

            // The screen opens with a level and an item selected; the history is rebuilt from that pair.
            var screen = vm.PriceLists!;
            screen.SelectedLevel = screen.Levels.First(l => l.Id == level.Id);
            screen.SelectedItem = screen.Items.First(i => i.Id == finished.Id);
            Dispatcher.UIThread.RunJobs();

            var newest = $"Wholesale / Cabinet applicable from " +
                         $"{ApexDate.Format(c.BooksBeginFrom.AddMonths(3))}";
            ArrowTo(window, vm, newest);
            DeleteHighlighted(window);

            Assert.DoesNotContain(vm.Company!.PriceLists, pl => pl.Id == v2.Id);
            Assert.Contains(vm.Company!.PriceLists, pl => pl.Id == v1.Id);
            new CompanyStorage(dir).Save(vm.Company!);
        }
        finally { window.Close(); Cleanup(dir); }
    }

    // ===================================================================== census 3.12 — REORDER LEVELS

    [AvaloniaFact]
    public void A_reorder_definition_deletes_with_AltD_then_Y()
    {
        var (window, vm, dir) = NewWindow("W33 Reorder Co");
        try
        {
            var (finished, _) = SeedItems(vm);
            new ReorderLevelsService(vm.Company!)
                .CreateOrUpdate(ReorderScope.Item, finished.Id, reorderQuantity: 25m);

            OpenMaster(window, vm, "Reorder Levels", Screen.ReorderLevelsMaster);
            ArrowTo(window, vm, "Item 'Cabinet'");
            DeleteHighlighted(window);

            Assert.Empty(vm.Company!.ReorderDefinitions);
            Assert.NotNull(vm.Company!.FindStockItem(finished.Id));
            var reloaded = AssertDeletedAndPersisted(vm, dir, "W33 Reorder Co");
            Assert.Empty(reloaded.ReorderDefinitions);
            Assert.NotNull(reloaded.FindStockItem(finished.Id));
        }
        finally { window.Close(); Cleanup(dir); }
    }

    // ===================================================================== the CLUSTER-WIDE invariants

    /// <summary>
    /// 🔴 <b>THE CLUSTER ASSERTION — the one that makes this a class fix rather than eight instance fixes.</b>
    /// Every one of the eight screens resolves through the SHARED <c>MasterListScreen</c> arm. A screen that was
    /// added to <c>RequestDeleteHighlighted</c> but forgotten here (or the reverse) would be half-wired, which is
    /// exactly how one master ends up gated differently from its siblings, and it is caught here rather than by
    /// whichever operator happened to press Alt+D on it first.
    /// </summary>
    [AvaloniaTheory]
    [InlineData("Batch", Screen.BatchMaster)]
    [InlineData("Bill of Materials", Screen.BomMaster)]
    [InlineData("Currency", Screen.CurrencyMaster)]
    [InlineData("Budget", Screen.BudgetMaster)]
    [InlineData("Scenario", Screen.ScenarioMaster)]
    [InlineData("Price Level", Screen.PriceLevelsMaster)]
    [InlineData("Price List", Screen.PriceListsMaster)]
    [InlineData("Reorder Levels", Screen.ReorderLevelsMaster)]
    public void Every_residual_master_is_on_the_shared_arm_and_Alt_D_is_live(string label, Screen screen)
    {
        var (window, vm, dir) = NewWindow("W33 Arm Co");
        try
        {
            EnableInventoryFeatures(vm);
            OpenMaster(window, vm, label, screen);

            var list = vm.MasterListScreen!;
            Assert.False(string.IsNullOrWhiteSpace(list.MasterKindLabel));
            Assert.False(list.IsAltering);

            // The arrows must not throw on an EMPTY list — a company with none of this master yet is the
            // ordinary case, and a destructive accelerator that crashes on it is worse than one that is inert.
            Key(window, PhysicalKey.ArrowDown);
            Key(window, PhysicalKey.D, RawInputModifiers.Alt);
            Key(window, PhysicalKey.Y);
            Assert.Equal(screen, vm.CurrentScreen);
        }
        finally { window.Close(); Cleanup(dir); }
    }

    /// <summary>
    /// Alt+D with nothing highlighted is a quiet no-op rather than a delete of row zero. The highlight starts at
    /// −1 on an untouched list, so an operator who opens a master and reflexively presses the chord removes
    /// nothing — the one behaviour that would make every test above a liability.
    /// </summary>
    [AvaloniaFact]
    public void AltD_with_nothing_highlighted_deletes_nothing()
    {
        var (window, vm, dir) = NewWindow("W33 NoHighlight Co");
        try
        {
            var c = vm.Company!;
            c.AddScenario(new Scenario(Guid.NewGuid(), "Provisional"));

            OpenMaster(window, vm, "Scenario", Screen.ScenarioMaster);
            Assert.Null(vm.MasterListScreen!.HighlightedMasterRow);

            DeleteHighlighted(window);

            Assert.Contains(vm.Company!.Scenarios, s => s.Name == "Provisional");
        }
        finally { window.Close(); Cleanup(dir); }
    }

    /// <summary>
    /// The highlight survives a list rebuild BY ID, not by index. Deleting the alphabetically-first of three
    /// scenarios must leave the remaining two intact — a restore by index would slide the highlight onto a
    /// neighbour, and the next Alt+D would remove the wrong master.
    /// </summary>
    [AvaloniaFact]
    public void Deleting_one_scenario_leaves_its_siblings_alone()
    {
        var (window, vm, dir) = NewWindow("W33 Sibling Co");
        try
        {
            var c = vm.Company!;
            foreach (var name in new[] { "Alpha", "Beta", "Gamma" })
                c.AddScenario(new Scenario(Guid.NewGuid(), name));

            OpenMaster(window, vm, "Scenario", Screen.ScenarioMaster);
            ArrowTo(window, vm, "Beta");
            DeleteHighlighted(window);

            Assert.DoesNotContain(vm.Company!.Scenarios, s => s.Name == "Beta");
            Assert.Contains(vm.Company!.Scenarios, s => s.Name == "Alpha");
            Assert.Contains(vm.Company!.Scenarios, s => s.Name == "Gamma");
        }
        finally { window.Close(); Cleanup(dir); }
    }

    /// <summary>
    /// Answering <b>N</b> to the confirmation keeps the master. A prompt that deleted either way would pass
    /// every "deletes with Alt+D then Y" test above.
    /// </summary>
    [AvaloniaFact]
    public void Answering_N_to_the_confirmation_keeps_the_master()
    {
        var (window, vm, dir) = NewWindow("W33 Cancel Co");
        try
        {
            vm.Company!.AddScenario(new Scenario(Guid.NewGuid(), "Provisional"));

            OpenMaster(window, vm, "Scenario", Screen.ScenarioMaster);
            ArrowTo(window, vm, "Provisional");
            Key(window, PhysicalKey.D, RawInputModifiers.Alt);
            Key(window, PhysicalKey.N);

            Assert.Contains(vm.Company!.Scenarios, s => s.Name == "Provisional");
        }
        finally { window.Close(); Cleanup(dir); }
    }

    /// <summary>
    /// The confirmation NAMES the master, in the operator's words, before anything is removed. A prompt reading
    /// "Delete? (Y/N)" on a screen with three similar rows is a prompt people learn to answer without reading.
    /// </summary>
    [AvaloniaFact]
    public void The_confirmation_names_the_master_kind_and_the_master()
    {
        var (window, vm, dir) = NewWindow("W33 Prompt Co");
        try
        {
            vm.Company!.AddScenario(new Scenario(Guid.NewGuid(), "Provisional"));

            OpenMaster(window, vm, "Scenario", Screen.ScenarioMaster);
            ArrowTo(window, vm, "Provisional");
            Key(window, PhysicalKey.D, RawInputModifiers.Alt);

            Assert.True(vm.IsAcceptPromptOpen);
            Assert.Contains("scenario", vm.AcceptPromptText);
            Assert.Contains("Provisional", vm.AcceptPromptText);

            Key(window, PhysicalKey.N);
        }
        finally { window.Close(); Cleanup(dir); }
    }
}
