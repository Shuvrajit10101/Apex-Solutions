using System;
using System.IO;
using System.Linq;
using Avalonia.Headless.XUnit;
using Apex.Desktop.Services;
using Apex.Desktop.ViewModels;
using Apex.Ledger.Domain;
using Apex.Ledger.Services;
using Xunit;

namespace Apex.Desktop.Tests;

/// <summary>
/// 🔴 <b>W46 TRACK AF — REVIEW FINDING: THE SCENARIO ALTERATION MUST NOT NARROW A SCENARIO TO THE VOUCHER
/// TYPES THAT HAPPEN TO BE ACTIVE TODAY.</b>
///
/// <para><b>THE MECHANISM, MEASURED.</b> <c>ScenarioMasterViewModel.BuildTypes</c> builds the tick list from
/// <c>_company.VoucherTypes.Where(t =&gt; t.IsActive)</c> — so a DEACTIVATED type has no row at all.
/// <c>LoadFrom</c> then sets Include/Exclude on the rows that exist, and <c>Alter</c> collects
/// <c>Types.Where(r =&gt; r.Include)</c> and hands it to <c>Scenario.ReplaceTypes</c>, which is a WHOLESALE
/// REPLACE. A voucher type the scenario names but the screen cannot show is therefore dropped from the saved
/// scenario — silently, permanently, and on an alteration whose only keystroke may have been a rename.</para>
///
/// <para><b>WHY IT IS NOT HYPOTHETICAL.</b> Deactivating a voucher type is a shipped single-keystroke gesture
/// (<c>VoucherTypeMasterViewModel.ToggleActiveOnHighlighted</c> → <c>VoucherTypeService.SetActive</c>, census
/// 5.11) with no guard against types that already carry history or that a scenario names, and
/// <c>MainWindowViewModel</c>'s own <c>VoucherTypeFilterPicker</c> remark records the measured fact that
/// deactivated types keep listing their historical vouchers. The dropped inclusion is not cosmetic either:
/// <c>LedgerBalance</c> consults the scenario's sets per voucher
/// (<c>src/Apex.Ledger/Reports/LedgerBalance.cs</c>), so the scenario column's figures change.</para>
///
/// <para><b>THE FIX UNDER TEST</b> keeps the ids the screen cannot represent and writes them back untouched —
/// it does NOT invent an affordance for inactive types, which no vendor page describes.</para>
/// </summary>
public sealed class ScenarioAlterInactiveVoucherTypeTests
{
    private static (MainWindowViewModel Vm, CompanyStorage Storage, string Dir) NewCo(string name)
    {
        var dir = Path.Combine(Path.GetTempPath(), "ApexScenInact_" + Guid.NewGuid().ToString("N"));
        var storage = new CompanyStorage(dir);
        var vm = new MainWindowViewModel(storage);
        vm.NewCompanyName = name;
        vm.CreateCompany();
        return (vm, storage, dir);
    }

    private static Company Reload(CompanyStorage s, string name) =>
        s.Load(s.ListCompanies().Single(e => string.Equals(e.Name, name, StringComparison.Ordinal)));

    private static void Cleanup(string dir)
    {
        try
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
        catch (IOException) { /* best effort */ }
        catch (UnauthorizedAccessException) { /* best effort */ }
    }

    /// <summary>
    /// A scenario includes voucher type T. T is then deactivated through the product's own service. The
    /// operator alters the scenario and changes only its NAME. T's inclusion must still be on the book
    /// afterwards — asserted on the reloaded <c>.db</c>, not on the graph the screen just mutated.
    /// </summary>
    [AvaloniaFact]
    public void Altering_a_scenario_keeps_an_inactive_voucher_types_inclusion()
    {
        const string co = "Scen Inactive Keep Co";
        var (vm, storage, dir) = NewCo(co);
        try
        {
            vm.ShowScenarioMaster();
            var create = vm.ScenarioMaster!;
            create.Name = "Fcast";
            Assert.True(create.Create(), create.Message);

            var id = vm.Company!.FindScenarioByName("Fcast")!.Id;
            var includedBefore = Reload(storage, co).FindScenario(id)!.IncludedTypeIds.ToList();
            Assert.True(includedBefore.Count >= 2, "The create defaults must pre-tick at least two types.");

            var victim = includedBefore[0];
            var victimName = vm.Company!.FindVoucherType(victim)!.Name;
            new VoucherTypeService(vm.Company!).SetActive(victim, false);
            storage.Save(vm.Company!);

            // Reopen the company exactly as the picker does, so the alteration screen is built from stored bytes.
            var company2 = Reload(storage, co);
            var alter = ScenarioMasterViewModel.ForAlter(company2, storage, id, onChanged: () => { })!;
            Assert.DoesNotContain(alter.Types, t => t.TypeId == victim);   // the row genuinely is not there
            alter.Name = "Forecast";
            Assert.True(alter.Alter(), alter.Message);

            var stored = Reload(storage, co).FindScenario(id)!;
            Assert.Equal("Forecast", stored.Name);
            Assert.True(stored.Includes(victim),
                $"A rename silently removed the deactivated voucher type '{victimName}' from the scenario. "
                + $"Included before = {includedBefore.Count}, after = {stored.IncludedTypeIds.Count}.");
            Assert.Equal(includedBefore.Count, stored.IncludedTypeIds.Count);
        }
        finally { Cleanup(dir); }
    }

    /// <summary>
    /// The same shape taken to its end: when EVERY included type has been deactivated the alteration is
    /// refused outright — <c>Types.Where(r =&gt; r.Include)</c> is empty, so the screen reports "Tick at least
    /// one voucher type" about a scenario the operator never untouched and the rename cannot be saved at all.
    /// </summary>
    [AvaloniaFact]
    public void A_scenario_whose_included_types_are_all_inactive_can_still_be_renamed()
    {
        const string co = "Scen AllInactive Co";
        var (vm, storage, dir) = NewCo(co);
        try
        {
            vm.ShowScenarioMaster();
            var create = vm.ScenarioMaster!;
            create.Name = "Fc2";
            Assert.True(create.Create(), create.Message);
            var id = vm.Company!.FindScenarioByName("Fc2")!.Id;

            var svc = new VoucherTypeService(vm.Company!);
            var included = Reload(storage, co).FindScenario(id)!.IncludedTypeIds.ToList();
            foreach (var t in included) svc.SetActive(t, false);
            storage.Save(vm.Company!);

            var company2 = Reload(storage, co);
            var alter = ScenarioMasterViewModel.ForAlter(company2, storage, id, onChanged: () => { })!;
            alter.Name = "Fc2 renamed";
            Assert.True(alter.Alter(), "The alteration was refused: " + alter.Message);

            var stored = Reload(storage, co).FindScenario(id)!;
            Assert.Equal("Fc2 renamed", stored.Name);
            Assert.Equal(included.Count, stored.IncludedTypeIds.Count);
        }
        finally { Cleanup(dir); }
    }

    /// <summary>
    /// The EXCLUDE half of the same hole: an exclusion of a type that is later deactivated must also survive
    /// an alteration, because <c>LedgerBalance</c> gives exclusion PRECEDENCE — losing one silently puts a
    /// voucher back into the scenario column.
    /// </summary>
    [AvaloniaFact]
    public void Altering_a_scenario_keeps_an_inactive_voucher_types_exclusion()
    {
        const string co = "Scen Inactive Excl Co";
        var (vm, storage, dir) = NewCo(co);
        try
        {
            vm.ShowScenarioMaster();
            var create = vm.ScenarioMaster!;
            create.Name = "Excl";
            var excludedRow = create.Types.First(t => !t.Include);
            excludedRow.Exclude = true;
            var victim = excludedRow.TypeId;
            Assert.True(create.Create(), create.Message);

            var id = vm.Company!.FindScenarioByName("Excl")!.Id;
            new VoucherTypeService(vm.Company!).SetActive(victim, false);
            storage.Save(vm.Company!);

            var company2 = Reload(storage, co);
            var alter = ScenarioMasterViewModel.ForAlter(company2, storage, id, onChanged: () => { })!;
            Assert.DoesNotContain(alter.Types, t => t.TypeId == victim);
            alter.Name = "Exclusions";
            Assert.True(alter.Alter(), alter.Message);

            var stored = Reload(storage, co).FindScenario(id)!;
            Assert.True(stored.Excludes(victim),
                "A rename silently removed the deactivated voucher type's EXCLUSION, which puts its vouchers "
                + "back into the scenario column.");
        }
        finally { Cleanup(dir); }
    }
}
