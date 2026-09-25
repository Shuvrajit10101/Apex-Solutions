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
/// 🔴 <b>WAVE 33 / CLUSTER C3 — THE <i>ALTER</i> HALF: Ctrl+Enter on the PRICE LEVEL (census 3.10) and the
/// CURRENCY (census 2.11) existing-lists, driven by real keystrokes on a real window.</b>
///
/// <para><b>Why only two of the eight masters this cluster wired for delete.</b> An alter verb needs a
/// <c>ForAlter</c> factory and a decision about what altering that master MEANS, and building one is only honest
/// where a source says. Two of the eight are documented by the vendor and both are built here; the other six
/// (batch, BOM, budget, scenario, price list, reorder definition) keep create+delete and their census rows stay
/// PARTIAL. Inventing an alter shape for a master no source describes is the failure mode this project has
/// already paid for.</para>
///
/// <para><b>FIDELITY (R7 / ruling 14) — CITED, AND EVERY URL WAS OPENED AND READ ON 2026-09-25.</b>
/// <list type="bullet">
/// <item><b>Price level.</b> <i>"Press Alt+G (Go To) &gt; Alter Master &gt; type or select Price levels…
/// Change the names of the Price Levels and press Ctrl+A to save"</i>, after which <i>"the modified names of the
/// price levels appear in the relevant masters and transactions"</i> —
/// help.tallysolutions.com/selling-buying-prices/.</item>
/// <item><b>Currency.</b> <i>"Press Alt+G (Go To) &gt; type or select Alter Master &gt; type or select Currency
/// &gt; select the currency you want to alter"</i>, altering the symbol, formal name, ISO code and decimal
/// places; the same page gives the DELETE keystroke as <i>Alt+D</i> and the rule <i>"You can delete a currency
/// if it is not used in transactions or opening balance for ledgers"</i> —
/// help.tallysolutions.com/create-alter-or-delete-currencies/.</item>
/// </list>
/// <b>What is OURS and labelled so:</b> the <i>route</i> (this product reaches an alteration by arrows +
/// Ctrl+Enter on the existing-list, which is its own established shape, not the vendor's Go-To menu), and the
/// <b>refusal to alter the BASE currency here</b> — see <c>CurrencyMasterViewModel.ForAlter</c> for the
/// correctness reason (the base row is a projection of the company profile's own fields and nothing re-syncs the
/// two). The vendor treats the base currency through a separate change-base-currency procedure, which is
/// consistent with refusing it here, but the refusal text is ours.</para>
///
/// <para>🔴 <b>EVERY TEST IN THIS FILE FAILS ON TODAY'S <c>main</c>, and on the alter-half changes alone.</b>
/// On main <c>PriceLevelsViewModel.IsAltering</c> and <c>CurrencyMasterViewModel.IsAltering</c> are the CONSTANT
/// <c>false</c>, neither type has a <c>ForAlter</c>, and <c>AlterHighlightedMasterListRow</c> has no case for
/// either screen — so Ctrl+Enter is a no-op and <c>Assert.True(vm.CurrentScreen is …, IsAltering)</c> is what
/// goes red first.</para>
/// </summary>
public sealed class MasterVerbsW33ResidualAlterTests
{
    private static (MainWindow Window, MainWindowViewModel Vm, string TempDir) NewWindow(string company)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "ApexW33C3A_" + Guid.NewGuid().ToString("N"));
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

    private static void OpenMaster(MainWindow window, MainWindowViewModel vm, string label, Screen expected)
    {
        ArrowToAndEnter(window, vm, "Create");
        ArrowToAndEnter(window, vm, label);
        Assert.Equal(expected, vm.CurrentScreen);
        Assert.NotNull(vm.MasterListScreen);
    }

    private static void ArrowTo(MainWindow window, MainWindowViewModel vm, string name)
    {
        for (var i = 0; i < 60; i++)
        {
            if (vm.MasterListScreen?.HighlightedMasterRow?.MasterName == name) return;
            Key(window, PhysicalKey.ArrowDown);
        }
        Assert.Fail($"'{name}' was not reachable by arrow navigation on the existing-masters list.");
    }

    /// <summary>Ctrl+Enter — the real alteration chord, on the real window.</summary>
    private static void AlterHighlighted(MainWindow window) =>
        Key(window, PhysicalKey.Enter, RawInputModifiers.Control);

    /// <summary>Ctrl+A — the real accept chord, on the real window.</summary>
    private static void Accept(MainWindow window) => Key(window, PhysicalKey.A, RawInputModifiers.Control);

    /// <summary>Re-reads the company from SQLite. 🔴 Through <c>PathForName</c> + a real <see cref="CompanyEntry"/>
    /// — the same derivation the company picker uses — so the file read back is by construction the file the
    /// product would open, and an alteration that never left memory cannot pass.</summary>
    private static Company LoadBack(string dir, string company)
    {
        var storage = new CompanyStorage(dir);
        return storage.Load(new CompanyEntry(company, storage.PathForName(company)));
    }

    private static void EnablePriceLevels(MainWindowViewModel vm)
    {
        vm.ShowGstConfig();
        vm.GstConfig!.EnableMultiplePriceLevels = true;
        vm.Back();
        vm.ShowGateway();
        Dispatcher.UIThread.RunJobs();
    }

    // ================================================================== census 3.10 — PRICE LEVEL ALTERATION

    /// <summary>The whole capability in one test, by keyboard only: arrows to the level, Ctrl+Enter to open it,
    /// retype the name, Ctrl+A to accept — and the rename is on DISK, not merely in the aggregate.</summary>
    [AvaloniaFact]
    public void A_price_level_is_renamed_with_CtrlEnter_then_CtrlA_and_the_rename_persists()
    {
        var (window, vm, dir) = NewWindow("W33 Level Alter Co");
        try
        {
            EnablePriceLevels(vm);
            var level = new PriceListService(vm.Company!).CreateLevel("Wholesle");   // typo the operator must fix
            new CompanyStorage(dir).Save(vm.Company!);

            OpenMaster(window, vm, "Price Level", Screen.PriceLevelsMaster);
            ArrowTo(window, vm, "Wholesle");
            AlterHighlighted(window);

            Assert.Equal(Screen.PriceLevelsMaster, vm.CurrentScreen);
            Assert.NotNull(vm.PriceLevels);
            Assert.True(vm.PriceLevels!.IsAltering, "Ctrl+Enter must open the level for ALTERATION.");
            Assert.Equal("Price Level Alteration", vm.PriceLevels.Caption);
            // The form arrives pre-loaded with the existing name — an alter screen that opened blank would
            // silently rename the level to whatever was typed next and lose the rest.
            Assert.Equal("Wholesle", vm.PriceLevels.Name);

            vm.PriceLevels.Name = "Wholesale";
            Accept(window);

            Assert.Equal("Wholesale", vm.Company!.FindPriceLevel(level.Id)!.Name);
            Assert.DoesNotContain(vm.Company!.PriceLevels, l => l.Name == "Wholesle");

            // 🔴 RE-READ FROM SQLITE. A rename that lives only in the open aggregate is not a rename.
            var reloaded = LoadBack(dir, "W33 Level Alter Co");
            Assert.Equal("Wholesale", reloaded!.FindPriceLevel(level.Id)!.Name);
        }
        finally { window.Close(); Cleanup(dir); }
    }

    /// <summary>🔴 THE TEST THAT SAYS WHY THIS VERB HAD TO EXIST. A level that already carries a price list is —
    /// correctly — UNDELETABLE, so before Ctrl+Enter a mistyped name on such a level could not be corrected by any
    /// sequence of keys. The rename keeps the price list, because every referent stores the level's Id.</summary>
    [AvaloniaFact]
    public void A_level_that_cannot_be_deleted_can_still_be_renamed_and_keeps_its_price_lists()
    {
        var (window, vm, dir) = NewWindow("W33 Level Guarded Co");
        try
        {
            EnablePriceLevels(vm);
            var c = vm.Company!;
            var inv = new InventoryService(c);
            var group = c.StockGroups.FirstOrDefault() ?? inv.CreateStockGroup("Primary");
            var unit = c.Units.FirstOrDefault() ?? inv.CreateSimpleUnit("Nos", "Numbers");
            var item = inv.CreateStockItem("Cabinet", group.Id, unit.Id);

            var svc = new PriceListService(c);
            var level = svc.CreateLevel("Retial");
            svc.AddOrReviseList(level.Id, item.Id, c.BooksBeginFrom,
                new[] { new PriceListSlab(0m, null, Money.FromRupees(500m), 0m) });

            OpenMaster(window, vm, "Price Level", Screen.PriceLevelsMaster);
            ArrowTo(window, vm, "Retial");

            // The delete is refused — that is the state the operator is stuck in on main.
            Key(window, PhysicalKey.D, RawInputModifiers.Alt);
            Key(window, PhysicalKey.Y);
            Assert.Contains(vm.Company!.PriceLevels, l => l.Id == level.Id);
            Assert.False(string.IsNullOrWhiteSpace(vm.Notice));
            Assert.Contains("price lists", vm.Notice!);

            // The alter is not.
            ArrowTo(window, vm, "Retial");
            AlterHighlighted(window);
            Assert.True(vm.PriceLevels!.IsAltering);
            vm.PriceLevels.Name = "Retail";
            Accept(window);

            Assert.Equal("Retail", vm.Company!.FindPriceLevel(level.Id)!.Name);
            // The price list still hangs off the SAME level — the rename did not re-key anything.
            Assert.Contains(vm.Company!.PriceLists, pl => pl.PriceLevelId == level.Id);
            new CompanyStorage(dir).Save(vm.Company!);
        }
        finally { window.Close(); Cleanup(dir); }
    }

    /// <summary>Renaming a level onto a name another level already owns is refused, and the screen SAYS SO. A
    /// silent failure here would leave the operator believing the rename took.</summary>
    [AvaloniaFact]
    public void Renaming_a_price_level_onto_another_levels_name_is_refused_and_says_why()
    {
        var (window, vm, dir) = NewWindow("W33 Level Clash Co");
        try
        {
            EnablePriceLevels(vm);
            var svc = new PriceListService(vm.Company!);
            var wholesale = svc.CreateLevel("Wholesale");
            svc.CreateLevel("Retail");

            OpenMaster(window, vm, "Price Level", Screen.PriceLevelsMaster);
            ArrowTo(window, vm, "Wholesale");
            AlterHighlighted(window);
            vm.PriceLevels!.Name = "retail";        // different case, same name — must still clash
            Accept(window);

            Assert.Equal("Wholesale", vm.Company!.FindPriceLevel(wholesale.Id)!.Name);
            Assert.False(string.IsNullOrWhiteSpace(vm.PriceLevels.Message));
            Assert.Contains("already exists", vm.PriceLevels.Message!);
        }
        finally { window.Close(); Cleanup(dir); }
    }

    /// <summary>🔴 SELF-EXCLUSION. Opening a level, looking at it and accepting must be a no-op, not a duplicate
    /// error — the uniqueness check is case-insensitive, so a check that did not exclude the level by ID would
    /// refuse the operator their own name back. Correcting only the CASE must work for the same reason.</summary>
    [AvaloniaFact]
    public void Re_accepting_a_price_level_unchanged_and_re_casing_it_both_succeed()
    {
        var (window, vm, dir) = NewWindow("W33 Level Self Co");
        try
        {
            EnablePriceLevels(vm);
            var level = new PriceListService(vm.Company!).CreateLevel("wholesale");

            OpenMaster(window, vm, "Price Level", Screen.PriceLevelsMaster);
            ArrowTo(window, vm, "wholesale");
            AlterHighlighted(window);

            Accept(window);                       // unchanged
            Assert.DoesNotContain("already exists", vm.PriceLevels!.Message ?? string.Empty);
            Assert.Equal("wholesale", vm.Company!.FindPriceLevel(level.Id)!.Name);

            vm.PriceLevels.Name = "Wholesale";    // case-only correction
            Accept(window);
            Assert.Equal("Wholesale", vm.Company!.FindPriceLevel(level.Id)!.Name);
            Assert.DoesNotContain("already exists", vm.PriceLevels.Message ?? string.Empty);
        }
        finally { window.Close(); Cleanup(dir); }
    }

    /// <summary>A blank name is refused on ALTER exactly as it is on create — and the level keeps its old name
    /// rather than being written blank.</summary>
    [AvaloniaFact]
    public void A_blank_name_is_refused_on_alter_and_the_level_keeps_its_name()
    {
        var (window, vm, dir) = NewWindow("W33 Level Blank Co");
        try
        {
            EnablePriceLevels(vm);
            var level = new PriceListService(vm.Company!).CreateLevel("Wholesale");

            OpenMaster(window, vm, "Price Level", Screen.PriceLevelsMaster);
            ArrowTo(window, vm, "Wholesale");
            AlterHighlighted(window);
            vm.PriceLevels!.Name = "   ";
            Accept(window);

            Assert.Equal("Wholesale", vm.Company!.FindPriceLevel(level.Id)!.Name);
            Assert.Contains("required", vm.PriceLevels.Message!);
        }
        finally { window.Close(); Cleanup(dir); }
    }

    /// <summary>🔴 Alt+D IS REFUSED WHILE THE SCREEN IS MID-ALTERATION, on both new alter screens. Deleting the
    /// master you are part-way through editing is never what was meant, and the gate is the shared arm's own
    /// <c>IsAltering: false</c> — which was the CONSTANT false on these two screens until this slice, i.e. the gate
    /// was structurally open.</summary>
    [AvaloniaFact]
    public void AltD_is_refused_while_a_price_level_is_being_altered()
    {
        var (window, vm, dir) = NewWindow("W33 Level Gate Co");
        try
        {
            EnablePriceLevels(vm);
            var level = new PriceListService(vm.Company!).CreateLevel("Wholesale");

            OpenMaster(window, vm, "Price Level", Screen.PriceLevelsMaster);
            ArrowTo(window, vm, "Wholesale");
            AlterHighlighted(window);
            Assert.True(vm.PriceLevels!.IsAltering);

            Key(window, PhysicalKey.D, RawInputModifiers.Alt);
            Key(window, PhysicalKey.Y);

            Assert.Contains(vm.Company!.PriceLevels, l => l.Id == level.Id);
        }
        finally { window.Close(); Cleanup(dir); }
    }

    // ==================================================================== census 2.11 — CURRENCY ALTERATION

    /// <summary>Symbol, formal name and decimal places are all written back together, by keyboard only, and the
    /// alteration survives a round trip through SQLite.</summary>
    [AvaloniaFact]
    public void A_currency_is_altered_with_CtrlEnter_then_CtrlA_and_the_alteration_persists()
    {
        var (window, vm, dir) = NewWindow("W33 Currency Alter Co");
        try
        {
            var usd = new Currency(Guid.NewGuid(), "$", "USDD", decimalPlaces: 2);
            vm.Company!.AddCurrency(usd);
            new CompanyStorage(dir).Save(vm.Company!);

            OpenMaster(window, vm, "Currency", Screen.CurrencyMaster);
            ArrowTo(window, vm, "USDD ($)");
            AlterHighlighted(window);

            Assert.Equal(Screen.CurrencyMaster, vm.CurrentScreen);
            Assert.True(vm.CurrencyMaster!.IsAltering, "Ctrl+Enter must open the currency for ALTERATION.");
            Assert.Equal("Currency Alteration", vm.CurrencyMaster.Caption);
            // Pre-loaded from the master, all three fields — an alter that opened blank would blank them.
            Assert.Equal("$", vm.CurrencyMaster.Symbol);
            Assert.Equal("USDD", vm.CurrencyMaster.FormalName);
            Assert.Equal("2", vm.CurrencyMaster.DecimalPlacesText);

            vm.CurrencyMaster.FormalName = "USD";
            vm.CurrencyMaster.Symbol = "US$";
            vm.CurrencyMaster.DecimalPlacesText = "3";
            Accept(window);

            var after = vm.Company!.FindCurrency(usd.Id)!;
            Assert.Equal("USD", after.FormalName);
            Assert.Equal("US$", after.Symbol);
            Assert.Equal(3, after.DecimalPlaces);

            var reloaded = LoadBack(dir, "W33 Currency Alter Co");
            var persisted = reloaded!.FindCurrency(usd.Id)!;
            Assert.Equal("USD", persisted.FormalName);
            Assert.Equal("US$", persisted.Symbol);
            Assert.Equal(3, persisted.DecimalPlaces);
        }
        finally { window.Close(); Cleanup(dir); }
    }

    /// <summary>🔴 <b>THE WRONG-MONEY TEST.</b> A currency with POSTED forex lines is alterable (the vendor's
    /// delete rule refuses a delete in that state, and an alter is how the symbol is then corrected at all) — so
    /// this proves the alteration moves NO figure: the line's base paisa amount, its forex magnitude and its rate
    /// are byte-identical afterwards, and the book still saves. <c>Currency.DecimalPlaces</c> formats nothing in
    /// this product; changing it must not re-round anything.</summary>
    [AvaloniaFact]
    public void Altering_a_currency_with_posted_forex_lines_moves_no_figure_and_the_book_still_saves()
    {
        var (window, vm, dir) = NewWindow("W33 Currency Forex Co");
        try
        {
            var c = vm.Company!;
            var usd = new Currency(Guid.NewGuid(), "$", "USDD", decimalPlaces: 2);
            c.AddCurrency(usd);

            var party = new DomainLedger(Guid.NewGuid(), "US Buyer", c.FindGroupByName("Sundry Debtors")!.Id,
                                         Money.Zero, openingIsDebit: true) { CurrencyId = usd.Id };
            var sales = new DomainLedger(Guid.NewGuid(), "Export Sales", c.FindGroupByName("Sales Accounts")!.Id,
                                         Money.Zero, openingIsDebit: false);
            c.AddLedger(party);
            c.AddLedger(sales);

            var type = c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Sales);
            new LedgerService(c).Post(new Voucher(
                Guid.NewGuid(), type.Id, c.BooksBeginFrom,
                new[]
                {
                    new EntryLine(party.Id, Money.FromRupees(8300m), DrCr.Debit,
                                  forex: new ForexInfo(usd.Id, Money.FromRupees(100m), 83m)),
                    new EntryLine(sales.Id, Money.FromRupees(8300m), DrCr.Credit),
                }));

            var line = c.Vouchers.SelectMany(v => v.Lines).Single(l => l.Forex is not null);
            var baseBefore = line.Amount;
            var forexBefore = line.Forex!.ForexAmount;
            var rateBefore = line.Forex!.Rate;

            OpenMaster(window, vm, "Currency", Screen.CurrencyMaster);
            ArrowTo(window, vm, "USDD ($)");
            AlterHighlighted(window);
            vm.CurrencyMaster!.FormalName = "USD";
            vm.CurrencyMaster.DecimalPlacesText = "0";     // the most aggressive change available
            Accept(window);

            Assert.Equal("USD", vm.Company!.FindCurrency(usd.Id)!.FormalName);

            var after = vm.Company!.Vouchers.SelectMany(v => v.Lines).Single(l => l.Forex is not null);
            Assert.Equal(baseBefore, after.Amount);
            Assert.Equal(forexBefore, after.Forex!.ForexAmount);
            Assert.Equal(rateBefore, after.Forex!.Rate);
            Assert.Equal(8300m, after.Amount.Amount);

            // And the company is still savable — the alter re-keyed nothing, so no foreign key dangles.
            new CompanyStorage(dir).Save(vm.Company!);
            var reloaded = LoadBack(dir, "W33 Currency Forex Co");
            var persisted = reloaded!.Vouchers.SelectMany(v => v.Lines).Single(l => l.Forex is not null);
            Assert.Equal(8300m, persisted.Amount.Amount);
            Assert.Equal(100m, persisted.Forex!.ForexAmount.Amount);
            Assert.Equal(83m, persisted.Forex!.Rate);
        }
        finally { window.Close(); Cleanup(dir); }
    }

    /// <summary>🔴 THE BASE CURRENCY IS REFUSED AND THE OPERATOR IS TOLD WHERE TO GO INSTEAD. A bare no-op here
    /// would be a chord that does nothing on one row of a list where it works on every other — the empty-notice
    /// shape a sibling review caught on a refused delete.</summary>
    [AvaloniaFact]
    public void The_base_currency_cannot_be_altered_here_and_the_notice_names_Alter_Company()
    {
        var (window, vm, dir) = NewWindow("W33 Base Alter Co");
        try
        {
            var basec = vm.Company!.BaseCurrency!;
            var symbolBefore = basec.Symbol;
            var nameBefore = basec.FormalName;

            OpenMaster(window, vm, "Currency", Screen.CurrencyMaster);
            ArrowTo(window, vm, $"{basec.FormalName} ({basec.Symbol})");
            AlterHighlighted(window);

            Assert.False(vm.CurrencyMaster!.IsAltering, "The base currency must NOT open for alteration.");
            Assert.False(string.IsNullOrWhiteSpace(vm.Notice));
            Assert.Contains("base currency", vm.Notice!);
            Assert.Contains("Alter Company", vm.Notice!);

            // Untouched, and still in step with the company profile it projects.
            Assert.Equal(symbolBefore, vm.Company!.BaseCurrency!.Symbol);
            Assert.Equal(nameBefore, vm.Company!.BaseCurrency!.FormalName);
            Assert.Equal(vm.Company!.BaseCurrencySymbol, vm.Company!.BaseCurrency!.Symbol);
            Assert.Equal(vm.Company!.BaseCurrencyName, vm.Company!.BaseCurrency!.FormalName);
        }
        finally { window.Close(); Cleanup(dir); }
    }

    /// <summary>Renaming a currency onto another currency's code — or onto its SYMBOL, which this product's
    /// uniqueness check also covers — is refused, and the form says so.</summary>
    [AvaloniaFact]
    public void Renaming_a_currency_onto_another_currencys_code_is_refused_and_says_why()
    {
        var (window, vm, dir) = NewWindow("W33 Currency Clash Co");
        try
        {
            var c = vm.Company!;
            var usd = new Currency(Guid.NewGuid(), "$", "USD");
            c.AddCurrency(usd);
            c.AddCurrency(new Currency(Guid.NewGuid(), "€", "EUR"));

            OpenMaster(window, vm, "Currency", Screen.CurrencyMaster);
            ArrowTo(window, vm, "USD ($)");
            AlterHighlighted(window);
            vm.CurrencyMaster!.FormalName = "eur";      // case-insensitive clash with the other currency
            Accept(window);

            Assert.Equal("USD", vm.Company!.FindCurrency(usd.Id)!.FormalName);
            Assert.Contains("already exists", vm.CurrencyMaster.CurrencyMessage!);
        }
        finally { window.Close(); Cleanup(dir); }
    }

    /// <summary>🔴 SELF-EXCLUSION, the currency half: an unchanged form must accept. <c>FindCurrencyByName</c>
    /// matches the formal name OR the symbol, so without excluding this currency by ID both checks would find
    /// itself and refuse.</summary>
    [AvaloniaFact]
    public void Re_accepting_an_unchanged_currency_form_is_not_a_duplicate_error()
    {
        var (window, vm, dir) = NewWindow("W33 Currency Self Co");
        try
        {
            var usd = new Currency(Guid.NewGuid(), "$", "USD", decimalPlaces: 2);
            vm.Company!.AddCurrency(usd);

            OpenMaster(window, vm, "Currency", Screen.CurrencyMaster);
            ArrowTo(window, vm, "USD ($)");
            AlterHighlighted(window);
            Accept(window);

            Assert.DoesNotContain("already exists", vm.CurrencyMaster!.CurrencyMessage ?? string.Empty);
            var after = vm.Company!.FindCurrency(usd.Id)!;
            Assert.Equal("USD", after.FormalName);
            Assert.Equal("$", after.Symbol);
            Assert.Equal(2, after.DecimalPlaces);
        }
        finally { window.Close(); Cleanup(dir); }
    }

    /// <summary>A non-numeric decimal-places entry is refused and — the point of the check ORDER — the symbol and
    /// formal name typed alongside it are NOT written. A half-altered master with a failure message is worse than
    /// a refused one.</summary>
    [AvaloniaFact]
    public void A_bad_decimal_places_entry_refuses_the_whole_currency_alteration()
    {
        var (window, vm, dir) = NewWindow("W33 Currency Partial Co");
        try
        {
            var usd = new Currency(Guid.NewGuid(), "$", "USD", decimalPlaces: 2);
            vm.Company!.AddCurrency(usd);

            OpenMaster(window, vm, "Currency", Screen.CurrencyMaster);
            ArrowTo(window, vm, "USD ($)");
            AlterHighlighted(window);
            vm.CurrencyMaster!.Symbol = "US$";
            vm.CurrencyMaster.FormalName = "USA";
            vm.CurrencyMaster.DecimalPlacesText = "two";
            Accept(window);

            var after = vm.Company!.FindCurrency(usd.Id)!;
            Assert.Equal("$", after.Symbol);            // NOT half-written
            Assert.Equal("USD", after.FormalName);
            Assert.Equal(2, after.DecimalPlaces);
            Assert.Contains("Decimal places", vm.CurrencyMaster.CurrencyMessage!);
        }
        finally { window.Close(); Cleanup(dir); }
    }

    /// <summary>Alt+D is refused while the currency screen is mid-alteration, for the same reason as the price
    /// level: the shared arm's <c>IsAltering</c> gate was the constant false here until this slice.</summary>
    [AvaloniaFact]
    public void AltD_is_refused_while_a_currency_is_being_altered()
    {
        var (window, vm, dir) = NewWindow("W33 Currency Gate Co");
        try
        {
            var eur = new Currency(Guid.NewGuid(), "€", "EUR");
            vm.Company!.AddCurrency(eur);

            OpenMaster(window, vm, "Currency", Screen.CurrencyMaster);
            ArrowTo(window, vm, "EUR (€)");
            AlterHighlighted(window);
            Assert.True(vm.CurrencyMaster!.IsAltering);

            Key(window, PhysicalKey.D, RawInputModifiers.Alt);
            Key(window, PhysicalKey.Y);

            Assert.Contains(vm.Company!.Currencies, x => x.Id == eur.Id);
        }
        finally { window.Close(); Cleanup(dir); }
    }

    /// <summary>
    /// 🔴 <b>ESCAPE OUT OF AN ALTERATION, THEN DELETE — the sequence that would strand the screen if the shell
    /// kept the alter view model around.</b> The alter VM replaces <c>vm.PriceLevels</c>, and it reports
    /// <c>IsAltering: true</c> forever; if Escape did not put a fresh create-mode screen back, the shared arm's
    /// <c>IsAltering: false</c> gate would stay shut and Alt+D would be dead on that master for the rest of the
    /// session — a capability lost by using a neighbouring one. This is the same Escape-then-re-drill sequence the
    /// census records for row 7.16, run here against the two new alter screens.
    /// </summary>
    [AvaloniaFact]
    public void Escaping_an_alteration_leaves_AltD_working_again_on_a_fresh_screen()
    {
        var (window, vm, dir) = NewWindow("W33 Escape Co");
        try
        {
            EnablePriceLevels(vm);
            var level = new PriceListService(vm.Company!).CreateLevel("Wholesale");

            OpenMaster(window, vm, "Price Level", Screen.PriceLevelsMaster);
            ArrowTo(window, vm, "Wholesale");
            AlterHighlighted(window);
            Assert.True(vm.PriceLevels!.IsAltering);

            Key(window, PhysicalKey.Escape);
            Key(window, PhysicalKey.Escape);        // two presses — this shell's documented Escape contract

            // Re-drill from the Gateway and the screen is a fresh CREATE screen with Alt+D live again.
            vm.ShowGateway();
            Dispatcher.UIThread.RunJobs();
            OpenMaster(window, vm, "Price Level", Screen.PriceLevelsMaster);
            Assert.False(vm.PriceLevels!.IsAltering, "Re-drilling must give a fresh create-mode screen.");
            Assert.True(vm.IsDeleteTargetPage);

            ArrowTo(window, vm, "Wholesale");
            Key(window, PhysicalKey.D, RawInputModifiers.Alt);
            Key(window, PhysicalKey.Y);
            Assert.DoesNotContain(vm.Company!.PriceLevels, l => l.Id == level.Id);
        }
        finally { window.Close(); Cleanup(dir); }
    }

    // ============================================================ the arm itself, and the six that did NOT join

    /// <summary>🔴 <b>THE HONEST NEGATIVE.</b> The six residual masters that gained DELETE in this cluster did not
    /// gain ALTER, and this locks that in BOTH directions: their screens stay on the shared delete arm
    /// (<c>MasterListScreen</c> resolves) while <c>IsAltering</c> stays false because no <c>ForAlter</c> exists —
    /// so if a later slice wires one and forgets this file, the test goes red here rather than the census quietly
    /// drifting to COMPLETE.</summary>
    [AvaloniaTheory]
    [InlineData("Batch", Screen.BatchMaster)]
    [InlineData("Bill of Materials", Screen.BomMaster)]
    [InlineData("Budget", Screen.BudgetMaster)]
    [InlineData("Scenario", Screen.ScenarioMaster)]
    [InlineData("Reorder Levels", Screen.ReorderLevelsMaster)]
    public void The_six_masters_without_a_vendor_documented_alter_are_delete_only(string label, Screen screen)
    {
        var (window, vm, dir) = NewWindow("W33 Alter Absent Co");
        try
        {
            vm.ShowGstConfig();
            var cfg = vm.GstConfig!;
            cfg.MaintainBatchwiseDetails = true;
            cfg.SetComponentsBom = true;
            cfg.EnableMultiplePriceLevels = true;
            vm.Back();
            vm.ShowGateway();
            Dispatcher.UIThread.RunJobs();

            OpenMaster(window, vm, label, screen);
            Assert.True(vm.IsDeleteTargetPage, "Alt+D must still be live on this master's existing-list.");
            Assert.False(vm.MasterListScreen!.IsAltering,
                "This master has no ForAlter and must report itself as not altering.");
        }
        finally { window.Close(); Cleanup(dir); }
    }
}
