using System;
using System.IO;
using System.Linq;
using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Desktop.Services;
using Apex.Desktop.ViewModels;
using Xunit;

namespace Apex.Desktop.Tests;

/// <summary>
/// <b>The other five F11 Statutory-Configuration writers must roll the shared Company aggregate back on a failed
/// save too — they carry the identical mutate-then-save shape, on the same screen, under the same Ctrl+A.</b>
///
/// <para><b>Why this file exists.</b> The sub-paisa slice fixed <c>ApplyGratuity</c> / <c>ApplyBonus</c> and named
/// the defect "mutate the shared aggregate, then persist, and revert with a helper that re-derives the toggle from
/// the field the failed mutation just wrote". Four siblings in the SAME file were left with exactly that shape —
/// <c>ApplyPf</c>, <c>ApplyEsi</c>, <c>ApplyPt</c>, <c>ApplySalaryTds</c> — plus three
/// <c>partial void On…Changed</c> handlers whose revert is a comparison that can never be true. And
/// <c>AcceptStatutoryConfig</c> runs the four Apply methods BEFORE the two the slice fixed, so ONE keystroke was
/// enough: gratuity and bonus rolled back correctly while PF, ESI and PT stayed in memory and the transactionally
/// rolled-back .db held none of them.</para>
///
/// <para><b><c>ApplyPt</c> is the worst of them and is why this is a wrong-FIGURES defect, not a stale toggle.</b>
/// On an already-enrolled company it does not replace the config — it edits <c>StateCode</c> and
/// <c>RegistrationNumber</c> IN PLACE. <c>PtConfig.ResolveSlab</c> selects the deduction slab table BY
/// <c>StateCode</c>, so a failed save left the rest of the session computing Professional Tax off a state the book
/// does not have, silently (a message shown, <c>false</c> returned, the toggle looking right). Restoring the
/// reference alone is not enough there; the two mutated fields have to be captured and put back.</para>
///
/// <para>Every test forces the failure the same way as the gratuity/bonus rollback pins: a ledger opening balance
/// set to a sub-paisa amount directly, bypassing every screen guard. Ledgers are written in <c>InsertLedgers</c>,
/// which runs AFTER <c>InsertCompany</c>, so the statutory columns are written successfully first and the throw
/// genuinely lands on the rollback path rather than short-circuiting before it.</para>
/// </summary>
public sealed class StatutoryConfigSiblingRollbackTests : IDisposable
{
    private readonly string _tempDir;
    private readonly CompanyStorage _storage;

    public StatutoryConfigSiblingRollbackTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "ApexStatConfigSiblings_" + Guid.NewGuid().ToString("N"));
        _storage = new CompanyStorage(_tempDir);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true); }
        catch (IOException) { /* a held SQLite handle must not fail the test */ }
    }

    // ---------------------------------------------------------------- harness

    private MainWindowViewModel NewPayrollCompany(string name, bool statutory = true)
    {
        var vm = new MainWindowViewModel(_storage);
        vm.NewCompanyName = name;
        vm.CreateCompany();
        vm.ShowGstConfig();
        var page = vm.GstConfig!;
        page.PayrollEnabled = true;
        if (statutory) page.PayrollStatutoryEnabled = true;
        vm.Back();
        return vm;
    }

    private GstConfigViewModel StatutoryPage(MainWindowViewModel vm)
    {
        vm.ShowGstConfig();
        return vm.GstConfig!;
    }

    private Company Reload(string companyName) =>
        _storage.Load(_storage.ListCompanies().Single(e => e.Name == companyName));

    /// <summary>Makes the NEXT save fail for a reason unrelated to any statutory field.</summary>
    private static void MakeTheNextSaveFail(Company company) =>
        company.Ledgers.First().OpeningBalance = new Money(10.005m);

    // ================================================================ ApplyPf

    [Fact]
    public void AFailedSaveRollsThePfEnrolmentBackOutOfTheAggregateAndTheToggleOff()
    {
        var vm = NewPayrollCompany("Pf Rollback Co");
        var page = StatutoryPage(vm);

        MakeTheNextSaveFail(vm.Company!);

        page.PfEnabled = true;
        Assert.False(page.ApplyPf());

        // EnableProvidentFund set Company.PfConfig before the store was reached; the rollback takes it back out,
        // and only then can RevertPfToggle — which reads that very field — turn the toggle off.
        Assert.Null(vm.Company!.PfConfig);
        Assert.False(page.PfEnabled);
        Assert.Null(Record.Exception(() => { vm.Company.Ledgers.First().OpeningBalance = Money.Zero; _storage.Save(vm.Company); }));
    }

    [Fact]
    public void AFailedSaveKeepsThePfEnrolmentTheDatabaseStillHolds()
    {
        const string companyName = "Pf Disable Rollback Co";
        var vm = NewPayrollCompany(companyName);
        var page = StatutoryPage(vm);

        page.PfEnabled = true;
        Assert.True(page.ApplyPf());

        MakeTheNextSaveFail(vm.Company!);

        page.PfEnabled = false;
        Assert.False(page.ApplyPf());

        Assert.NotNull(Reload(companyName).PfConfig);   // the transaction rolled back — the .db still has it…
        Assert.NotNull(vm.Company!.PfConfig);           // …so memory must not have lost it
        Assert.True(page.PfEnabled);
    }

    // ================================================================ ApplyEsi

    [Fact]
    public void AFailedSaveRollsTheEsiEnrolmentBackOutOfTheAggregateAndTheToggleOff()
    {
        var vm = NewPayrollCompany("Esi Rollback Co");
        var page = StatutoryPage(vm);

        MakeTheNextSaveFail(vm.Company!);

        page.EsiEnabled = true;
        Assert.False(page.ApplyEsi());

        Assert.Null(vm.Company!.EsiConfig);
        Assert.False(page.EsiEnabled);
    }

    // ================================================================ ApplyPt — the in-place edit

    /// <summary>
    /// <b>The wrong-figure one.</b> An already-enrolled company switches PT state and enrolment number; the save
    /// fails; the in-memory config must still name the state and registration the book actually holds, because the
    /// slab table — and therefore every Professional-Tax deduction for the rest of the session — is selected by it.
    /// Restoring only <c>Company.PtConfig</c> would not do: <c>SetProfessionalTaxState</c> and the line beside it
    /// mutate the SAME object the capture points at.
    /// </summary>
    [Fact]
    public void AFailedPtStateSwitchLeavesTheStateAndRegistrationTheBookActuallyHolds()
    {
        const string companyName = "Pt State Rollback Co";
        var vm = NewPayrollCompany(companyName);
        var page = StatutoryPage(vm);

        page.PtEnabled = true;
        page.SelectedPtState = page.PtStateOptions.Single(o => o.Code == "27");   // Maharashtra
        page.PtRegistrationNumber = "PTREG-ORIGINAL";
        Assert.True(page.ApplyPt());

        MakeTheNextSaveFail(vm.Company!);

        page.SelectedPtState = page.PtStateOptions.Single(o => o.Code == "29");   // Karnataka
        page.PtRegistrationNumber = "PTREG-CHANGED";
        Assert.False(page.ApplyPt());

        // Memory and disk agree — both still Maharashtra with the original enrolment number.
        Assert.Equal("27", vm.Company!.PtConfig!.StateCode);
        Assert.Equal("PTREG-ORIGINAL", vm.Company.PtConfig.RegistrationNumber);
        var onDisk = Reload(companyName);
        Assert.Equal("27", onDisk.PtConfig!.StateCode);
        Assert.Equal("PTREG-ORIGINAL", onDisk.PtConfig.RegistrationNumber);
    }

    [Fact]
    public void AFailedSaveRollsTheFirstPtEnrolmentBackOutOfTheAggregateAndTheToggleOff()
    {
        var vm = NewPayrollCompany("Pt Rollback Co");
        var page = StatutoryPage(vm);

        MakeTheNextSaveFail(vm.Company!);

        page.PtEnabled = true;
        page.SelectedPtState = page.PtStateOptions.Single(o => o.Code == "27");
        Assert.False(page.ApplyPt());

        Assert.Null(vm.Company!.PtConfig);
        Assert.False(page.PtEnabled);
    }

    // ================================================================ ApplySalaryTds

    [Fact]
    public void AFailedSaveRollsTheSalaryTdsSwitchBackOutOfTheAggregateAndTheToggleOff()
    {
        var vm = NewPayrollCompany("Salary Tds Rollback Co");
        var page = StatutoryPage(vm);

        MakeTheNextSaveFail(vm.Company!);

        page.SalaryTdsEnabled = true;
        Assert.False(page.ApplySalaryTds());

        Assert.False(vm.Company!.SalaryTdsEnabled);
        Assert.False(page.SalaryTdsEnabled);
    }

    // ================================================================ the self-triggering toggle handlers

    /// <summary>
    /// <b><c>OnPayrollStatutoryEnabledChanged</c>'s revert was a comparison that could never be true.</b> It wrote
    /// <c>_company.PayrollStatutoryEnabled = value</c> and then, in the catch, compared the VM property against
    /// that same freshly-written field — both are <c>value</c>, so the revert was a guaranteed no-op and memory
    /// kept a statutory flag the .db never received. That matters beyond this handler: <c>ApplyGratuity</c> and
    /// <c>ApplyBonus</c> capture this very field as their own rollback baseline, so a divergence here propagated
    /// silently into theirs.
    /// </summary>
    [Fact]
    public void AFailedSaveOfThePayrollStatutoryToggleLeavesNeitherMemoryNorTheToggleAhead()
    {
        var vm = NewPayrollCompany("Statutory Toggle Rollback Co", statutory: false);
        var page = StatutoryPage(vm);
        Assert.False(vm.Company!.PayrollStatutoryEnabled);

        MakeTheNextSaveFail(vm.Company);

        page.PayrollStatutoryEnabled = true;   // the property setter itself saves, and the save fails

        Assert.False(vm.Company.PayrollStatutoryEnabled);
        Assert.False(page.PayrollStatutoryEnabled);
    }

    /// <summary>
    /// The same defect in <c>OnPayrollEnabledChanged</c> — and here the capture has to cover TWO fields, because
    /// <c>DisablePayroll</c> clears <c>PayrollStatutoryEnabled</c> as well as <c>PayrollEnabled</c>.
    /// </summary>
    [Fact]
    public void AFailedSaveOfThePayrollToggleRestoresBothPayrollFlags()
    {
        var vm = NewPayrollCompany("Payroll Toggle Rollback Co");
        var page = StatutoryPage(vm);
        Assert.True(vm.Company!.PayrollEnabled);
        Assert.True(vm.Company.PayrollStatutoryEnabled);

        MakeTheNextSaveFail(vm.Company);

        page.PayrollEnabled = false;   // DisablePayroll clears both flags, then the save fails

        Assert.True(vm.Company.PayrollEnabled);
        Assert.True(vm.Company.PayrollStatutoryEnabled);
        Assert.True(page.PayrollEnabled);
    }

    /// <summary>
    /// The same defect once more, in the four plain feature toggles this page also owns — <c>DefineBomComponentType</c>
    /// (F12) and the three F11/F12 flags beside it. Not payroll, but the identical shape: write the company field,
    /// save, and "revert" by comparing the VM property against the field just written.
    ///
    /// <para><b>Deliberately NOT covered here: <c>EnableJobOrderProcessing</c>.</b> It routes through
    /// <c>JobWorkService.SetEnabled</c>, which also stamps <c>IsActive</c> / <c>UseForJobWork</c> /
    /// <c>AllowConsumption</c> on every Job-Work voucher type — restoring one bool would not restore the mutation,
    /// so it needs a capture of those per-type flags and is recorded as residue in plan.md rather than half-fixed.</para>
    /// </summary>
    [Theory]
    [InlineData("DefineBomComponentType")]
    [InlineData("MaintainBatchwiseDetails")]
    [InlineData("SetComponentsBom")]
    [InlineData("EnableMultiplePriceLevels")]
    public void AFailedSaveOfAPlainFeatureToggleLeavesNeitherMemoryNorTheToggleAhead(string toggle)
    {
        var vm = NewPayrollCompany("Feature Toggle Rollback Co " + toggle);
        var page = StatutoryPage(vm);

        MakeTheNextSaveFail(vm.Company!);

        switch (toggle)
        {
            case "DefineBomComponentType":
                Assert.False(vm.Company!.DefineBomComponentType);
                page.DefineBomComponentType = true;
                Assert.False(vm.Company.DefineBomComponentType);
                Assert.False(page.DefineBomComponentType);
                break;
            case "MaintainBatchwiseDetails":
                Assert.False(vm.Company!.MaintainBatchwiseDetails);
                page.MaintainBatchwiseDetails = true;
                Assert.False(vm.Company.MaintainBatchwiseDetails);
                Assert.False(page.MaintainBatchwiseDetails);
                break;
            case "SetComponentsBom":
                Assert.False(vm.Company!.SetComponentsBom);
                page.SetComponentsBom = true;
                Assert.False(vm.Company.SetComponentsBom);
                Assert.False(page.SetComponentsBom);
                break;
            case "EnableMultiplePriceLevels":
                Assert.False(vm.Company!.EnableMultiplePriceLevels);
                page.EnableMultiplePriceLevels = true;
                Assert.False(vm.Company.EnableMultiplePriceLevels);
                Assert.False(page.EnableMultiplePriceLevels);
                break;
            default: throw new InvalidOperationException($"unmapped toggle '{toggle}'");
        }
    }

    // ================================================================ one keystroke, seven Apply methods

    /// <summary>
    /// <b>The reach the slice's own record understated: <c>AcceptStatutoryConfig</c> runs SEVEN Apply methods, not
    /// two, and the four sibling ones run FIRST.</b> One Ctrl+A on a page with PF, ESI, PT, salary-TDS, Gratuity
    /// and Bonus all switched on, against a company whose save will fail, must leave the whole aggregate exactly
    /// where the (transactionally rolled-back) .db left it — not three enrolments in memory and none on disk.
    /// </summary>
    [Fact]
    public void OneFailedKeyboardAcceptLeavesNoStatutoryEnrolmentBehindInMemory()
    {
        var vm = NewPayrollCompany("Accept All Rollback Co");
        var page = StatutoryPage(vm);

        MakeTheNextSaveFail(vm.Company!);

        page.PfEnabled = true;
        page.EsiEnabled = true;
        page.PtEnabled = true;
        page.SelectedPtState = page.PtStateOptions.Single(o => o.Code == "27");
        page.SalaryTdsEnabled = true;
        page.GratuityEnabled = true;
        page.GratuityCapText = "1999999";
        page.BonusEnabled = true;
        page.BonusCalculationCeilingText = "6999";
        page.BonusMinimumWageText = "5721";

        page.AcceptStatutoryConfig();

        Assert.Null(vm.Company!.PfConfig);
        Assert.Null(vm.Company.EsiConfig);
        Assert.Null(vm.Company.PtConfig);
        Assert.False(vm.Company.SalaryTdsEnabled);
        Assert.Null(vm.Company.GratuityConfig);
        Assert.Null(vm.Company.BonusConfig);
    }
}
