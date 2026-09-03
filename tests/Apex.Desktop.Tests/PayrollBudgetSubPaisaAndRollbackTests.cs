using System;
using System.Data.Common;
using System.IO;
using System.Linq;
using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Desktop.Services;
using Apex.Desktop.ViewModels;
using Xunit;

namespace Apex.Desktop.Tests;

/// <summary>
/// <b>The three master screens of the payroll/budget family — Budget Creation, Salary Details and Pay Head — must
/// refuse a sub-paisa figure at the field the operator typed it into, and must roll the shared Company aggregate
/// back when the save that follows fails.</b> (W0-13 PART 2, paths <c>A1</c>, <c>B6</c>, <c>B7</c>.)
///
/// <para><b>Why this file exists.</b> All three parse with a bare <c>decimal.TryParse</c>, hand the result to a
/// domain constructor that validates SIGN but not paisa-exactness, and the aggregate then persists through
/// <c>Paisa.FromMoney</c> ⇒ <c>PaisaConversion.ToPaisaExact</c>, which throws. The three differ only in how loudly
/// they fail:</para>
/// <list type="bullet">
///   <item><b><c>A1</c> Budget — CLASS A, it CRASHES.</b> <c>BudgetMasterViewModel</c> contained no <c>catch</c>
///     block at all (measured: zero), so <c>_company.AddBudget(budget); _storage.Save(_company);</c> let the
///     store's <c>InvalidOperationException</c> escape as an unhandled exception out of a Ctrl+A keystroke — and
///     the budget stayed on the aggregate, so every LATER save threw too.</item>
///   <item><b><c>B6</c> Salary Details and <c>B7</c> Pay Head — CLASS B, they report and POISON.</b> Their narrow
///     <c>when (ex is InvalidOperationException or ArgumentException)</c> filter does match the store's throw, so
///     the operator got a message — but the engine had already written the structure / pay head to the company
///     and nothing took it back off, which is the exact divergence W0-12 existed to remove. The same narrow
///     filter also let a <c>SqliteException</c> (SQLITE_BUSY from a second instance holding the write lock,
///     READONLY, FULL) escape as a crash.</item>
/// </list>
///
/// <para><b>The levers, and why they cannot interfere with what they measure.</b> A save failure is forced by
/// planting a bad cost centre on the company: <c>InsertCostCentres</c> runs at the store's step 1770, BEFORE
/// <c>InsertBudgets</c> (1796), <c>InsertPayHeads</c> (1829) and <c>InsertSalaryStructures</c> (1830), and nothing
/// on these three screens or in their rollbacks reads or writes a cost centre. Two distinct levers are needed
/// because they pin different halves:</para>
/// <list type="bullet">
///   <item><see cref="PlantNonReportableSaveFailure"/> — a <c>null</c> centre ⇒ <see cref="NullReferenceException"/>,
///     which <c>SaveFailure.IsReportable</c> deliberately does NOT list. It pins that the restore runs
///     <b>unconditionally</b>, ahead of and independent of the report-vs-crash decision, and that the unknown
///     failure still reaches the caller.</item>
///   <item><see cref="PlantReportableSaveFailure"/> — the same centre twice ⇒ a PRIMARY KEY violation ⇒
///     <c>SqliteException</c>, a <see cref="DbException"/>. It is reportable but sits OUTSIDE the old narrow
///     <c>InvalidOperationException or ArgumentException</c> filter, so it is the one lever that discriminates a
///     widened filter from the shipped one: on the old code it is a crash, on the new one a message.</item>
/// </list>
///
/// <para><b>Odd-paisa fixtures throughout.</b> Every sub-paisa stem is chosen so that truncating, rounding or
/// re-formatting it to two places lands on a DIFFERENT number than any figure the test also asserts — a ±0.50
/// defect survived this project's whole life under round-number assertions.</para>
/// </summary>
public sealed class PayrollBudgetSubPaisaAndRollbackTests : IDisposable
{
    private readonly string _tempDir;
    private readonly CompanyStorage _storage;

    public PayrollBudgetSubPaisaAndRollbackTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "ApexPayrollBudgetGuard_" + Guid.NewGuid().ToString("N"));
        _storage = new CompanyStorage(_tempDir);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true); }
        catch (IOException) { /* a held SQLite handle must not fail the test */ }
    }

    // ---------------------------------------------------------------- harness

    private MainWindowViewModel NewCompany(string name)
    {
        var vm = new MainWindowViewModel(_storage);
        vm.NewCompanyName = name;
        vm.CreateCompany();
        return vm;
    }

    private MainWindowViewModel NewPayrollCompany(string name)
    {
        var vm = NewCompany(name);
        vm.ShowGstConfig();
        var page = vm.GstConfig!;
        page.PayrollEnabled = true;
        page.PayrollStatutoryEnabled = true;
        return vm;
    }

    /// <summary>
    /// Makes the next <c>Save</c> throw a <see cref="NullReferenceException"/> inside <c>InsertCostCentres</c> —
    /// a failure <c>SaveFailure.IsReportable</c> does NOT recognise, so the screen must restore and then rethrow.
    /// </summary>
    private static void PlantNonReportableSaveFailure(Company company) => company.AddCostCentre(null!);

    /// <summary>
    /// Makes the next <c>Save</c> throw a <c>SqliteException</c> (PRIMARY KEY violation) inside
    /// <c>InsertCostCentres</c> — a <see cref="DbException"/>, which is reportable but is NOT matched by the old
    /// narrow <c>InvalidOperationException or ArgumentException</c> filter these screens shipped.
    /// </summary>
    private static void PlantReportableSaveFailure(Company company)
    {
        var category = company.CostCategories[0];
        var centre = new CostCentre(Guid.NewGuid(), "Duplicated Centre", category.Id);
        company.AddCostCentre(centre);
        company.AddCostCentre(centre);   // same primary key twice
    }

    /// <summary>
    /// Asserts the reported message is the PLANTED store failure and nothing else. Without this, a future change
    /// that made the screen refuse EARLY — before the mutation the rollback exists to undo — would keep the
    /// "leaves nothing behind" assertion trivially green while the rollback itself rotted away.
    /// </summary>
    private static void AssertReportedTheStoreFailure(string? message)
    {
        Assert.NotNull(message);
        Assert.Contains("cost_centres", message);   // only the planted UNIQUE-constraint violation says this
    }

    /// <summary>Asserts the message names the field and is NOT the store's own wording (trap iv).</summary>
    private static void AssertFrontLineMessage(string? message, string fieldLabel, string typedText)
    {
        Assert.NotNull(message);
        Assert.Contains(fieldLabel, message);
        Assert.Contains(typedText, message);
        Assert.Contains("enter at most two decimal places", message);
        Assert.DoesNotContain("cannot persist or serialise without loss", message);
    }

    // ============================================================ A1 — Budget, front line

    /// <summary>
    /// <b>THE RED PROOF FOR <c>A1</c>.</b> On the shipped code <c>AddLine</c> accepts ₹12,345.675, the budget is
    /// assembled, and <c>Create</c> then throws an UNHANDLED <c>InvalidOperationException</c> out of the store —
    /// there is no catch block anywhere in <c>BudgetMasterViewModel</c> to turn it into a message. The refusal
    /// must happen at <c>AddLine</c>, naming the amount field.
    /// </summary>
    [Fact]
    public void ASubPaisaBudgetLineIsRefusedAtAddLineInsteadOfCrashingTheCreateKeystroke()
    {
        var vm = NewCompany("BudgetSubPaisa");
        vm.ShowBudgetMaster();
        var page = vm.BudgetMaster!;

        page.SelectedTarget = page.Targets.First();
        page.LineAmountText = "12345.675";

        Assert.False(page.AddLine());
        AssertFrontLineMessage(page.Message, "budget amount", "12345.675");
        Assert.Empty(page.PendingLines);

        // …and the amount the operator typed is still in the box, so they can correct it.
        Assert.Equal("12345.675", page.LineAmountText);
    }

    /// <summary>A 17-digit budget figure passes parse, sign AND the paisa test, then overflows <c>long</c> inside
    /// the store. The magnitude branch must reject it first — see <c>StorableAmount</c>'s branch order.</summary>
    [Fact]
    public void AnOverLargeBudgetLineIsRefusedByTheMagnitudeCeilingRatherThanOverflowing()
    {
        var vm = NewCompany("BudgetTooLarge");
        vm.ShowBudgetMaster();
        var page = vm.BudgetMaster!;

        page.SelectedTarget = page.Targets.First();
        page.LineAmountText = "999999999999999999.13";   // > long.MaxValue paisa, and paisa-EXACT

        Assert.False(page.AddLine());
        Assert.NotNull(page.Message);
        Assert.Contains("budget amount", page.Message);
        Assert.Contains("too large", page.Message);
        Assert.Empty(page.PendingLines);
    }

    /// <summary>A paisa-exact odd figure must still be accepted — the guard must not over-refuse.</summary>
    [Fact]
    public void APaisaExactOddBudgetLineIsStillAccepted()
    {
        var vm = NewCompany("BudgetOddButExact");
        vm.ShowBudgetMaster();
        var page = vm.BudgetMaster!;

        page.SelectedTarget = page.Targets.First();
        page.LineAmountText = "12345.67";

        Assert.True(page.AddLine(), page.Message);
        Assert.Null(page.Message);
        var row = Assert.Single(page.PendingLines);
        Assert.Equal(12345.67m, row.Line.Amount.Amount);
    }

    // ============================================================ A1 — Budget, rollback + filter

    /// <summary>
    /// <b>The CLASS-A rollback.</b> A failure the screen does not recognise must still take the budget back off
    /// the aggregate before it escapes — otherwise every LATER save throws on a budget the <c>.db</c> never got.
    /// </summary>
    [Fact]
    public void ANonReportableBudgetSaveFailureRollsTheBudgetOffTheAggregateAndRethrows()
    {
        var vm = NewCompany("BudgetNonReportable");
        vm.ShowBudgetMaster();
        var page = vm.BudgetMaster!;
        var company = vm.Company!;

        page.SelectedTarget = page.Targets.First();
        page.LineAmountText = "8642.31";
        Assert.True(page.AddLine(), page.Message);
        page.Name = "Marketing FY26";

        PlantNonReportableSaveFailure(company);

        Assert.Throws<NullReferenceException>(() => page.Create());
        Assert.DoesNotContain(company.Budgets, b => b.Name == "Marketing FY26");
    }

    /// <summary>
    /// <b>The CLASS-A widened filter.</b> A <c>SqliteException</c> — the ordinary operational failure of a second
    /// instance holding the write lock — must be a message, not a crash, and the budget must not survive it.
    /// </summary>
    [Fact]
    public void AReportableBudgetSaveFailureIsAMessageAndLeavesNoBudgetBehind()
    {
        var vm = NewCompany("BudgetReportable");
        vm.ShowBudgetMaster();
        var page = vm.BudgetMaster!;
        var company = vm.Company!;

        page.SelectedTarget = page.Targets.First();
        page.LineAmountText = "7531.29";
        Assert.True(page.AddLine(), page.Message);
        page.Name = "Logistics FY26";

        PlantReportableSaveFailure(company);

        Assert.False(page.Create());
        AssertReportedTheStoreFailure(page.Message);
        Assert.DoesNotContain(company.Budgets, b => b.Name == "Logistics FY26");

        // The pending lines survive the failure, so the operator can retry without retyping.
        Assert.Single(page.PendingLines);
    }

    /// <summary>The happy path must keep working: a budget created through the screen reaches the <c>.db</c>.</summary>
    [Fact]
    public void ABudgetWithAnOddPaisaExactLineStillPersists()
    {
        var vm = NewCompany("BudgetPersists");
        vm.ShowBudgetMaster();
        var page = vm.BudgetMaster!;

        page.SelectedTarget = page.Targets.First();
        page.LineAmountText = "4321.09";
        Assert.True(page.AddLine(), page.Message);
        page.Name = "Ops FY26";
        Assert.True(page.Create(), page.Message);

        var reloaded = _storage.Load(_storage.ListCompanies().Single(e => e.Name == "BudgetPersists"));
        var budget = Assert.Single(reloaded.Budgets, b => b.Name == "Ops FY26");
        Assert.Equal(4321.09m, Assert.Single(budget.Lines).Amount.Amount);
    }

    // ============================================================ B6 — Salary Details, front line

    /// <summary>
    /// <b>THE RED PROOF FOR <c>B6</c>.</b> On the shipped code a sub-paisa flat-rate amount passes the screen's
    /// only numeric gate (<c>amount &lt; 0m</c>), passes <c>SalaryStructureService</c>'s only numeric gate
    /// (<c>&lt; Money.Zero</c>), and is refused by the STORE — whose message names no field. It must be refused
    /// here, naming the pay head and the figure.
    /// </summary>
    [Fact]
    public void ASubPaisaSalaryStructureAmountIsRefusedAtTheLineInsteadOfByTheStore()
    {
        var vm = NewPayrollCompany("SalarySubPaisa");
        var payHead = CreateFlatEarning(vm, "Basic");
        var employee = CreateEmployee(vm, "Staff", "Asha Menon");

        vm.ShowSalaryStructureMaster();
        var page = vm.SalaryDetails!;
        page.SelectedEmployee = employee;
        page.EffectiveFromText = "2025-04-01";

        var row = page.Lines[0];
        row.SelectedPayHead = page.PayHeadOptions.Single(o => o.PayHead.Id == payHead.Id);
        row.AmountText = "31250.005";

        Assert.False(page.Save());
        // The label must be a FIELD noun-phrase carrying the row identity, not the row identity alone — this is
        // the only one of the nine ErrorFor sites whose label names a row, and this screen carries other money
        // columns a later slice will guard. `Assert.Contains("Basic", …)` alone passes on a bare "pay head
        // 'Basic'" label and pins nothing about that convention.
        AssertFrontLineMessage(page.Message, "the amount for pay head 'Basic'", "31250.005");
        Assert.Empty(vm.Company!.SalaryStructures);
    }

    /// <summary>A paisa-exact odd salary amount must still save.</summary>
    [Fact]
    public void APaisaExactOddSalaryStructureAmountIsStillAccepted()
    {
        var vm = NewPayrollCompany("SalaryOddButExact");
        var payHead = CreateFlatEarning(vm, "Basic");
        var employee = CreateEmployee(vm, "Staff", "Ravi Nair");

        vm.ShowSalaryStructureMaster();
        var page = vm.SalaryDetails!;
        page.SelectedEmployee = employee;
        page.EffectiveFromText = "2025-04-01";

        var row = page.Lines[0];
        row.SelectedPayHead = page.PayHeadOptions.Single(o => o.PayHead.Id == payHead.Id);
        row.AmountText = "31250.07";

        Assert.True(page.Save(), page.Message);
        var structure = Assert.Single(vm.Company!.SalaryStructures);
        Assert.Equal(31250.07m, Assert.Single(structure.Lines).Amount!.Value.Amount);
    }

    // ============================================================ B6 — Salary Details, rollback + filter

    /// <summary>
    /// <b>The CLASS-B poisoning, on the salary screen.</b> <c>DefineForEmployee</c> writes the structure to the
    /// company BEFORE the save; a failed save must take it back off.
    /// </summary>
    [Fact]
    public void ANonReportableSalaryStructureSaveFailureRollsTheStructureOffTheAggregateAndRethrows()
    {
        var vm = NewPayrollCompany("SalaryNonReportable");
        var payHead = CreateFlatEarning(vm, "Basic");
        var employee = CreateEmployee(vm, "Staff", "Nita Bose");

        vm.ShowSalaryStructureMaster();
        var page = vm.SalaryDetails!;
        page.SelectedEmployee = employee;
        page.EffectiveFromText = "2025-04-01";
        var row = page.Lines[0];
        row.SelectedPayHead = page.PayHeadOptions.Single(o => o.PayHead.Id == payHead.Id);
        row.AmountText = "27431.53";

        PlantNonReportableSaveFailure(vm.Company!);

        Assert.Throws<NullReferenceException>(() => page.Save());
        Assert.Empty(vm.Company!.SalaryStructures);
    }

    /// <summary>
    /// <b>The CLASS-B widened filter, on the salary screen.</b> A <c>SqliteException</c> was a crash under the
    /// narrow filter at the shipped <c>:332</c>; it must now be a message, and must leave nothing behind.
    /// </summary>
    [Fact]
    public void AReportableSalaryStructureSaveFailureIsAMessageAndLeavesNoStructureBehind()
    {
        var vm = NewPayrollCompany("SalaryReportable");
        var payHead = CreateFlatEarning(vm, "Basic");
        var employee = CreateEmployee(vm, "Staff", "Imran Qadri");

        vm.ShowSalaryStructureMaster();
        var page = vm.SalaryDetails!;
        page.SelectedEmployee = employee;
        page.EffectiveFromText = "2025-04-01";
        var row = page.Lines[0];
        row.SelectedPayHead = page.PayHeadOptions.Single(o => o.PayHead.Id == payHead.Id);
        row.AmountText = "27431.53";

        PlantReportableSaveFailure(vm.Company!);

        Assert.False(page.Save());
        AssertReportedTheStoreFailure(page.Message);
        Assert.Empty(vm.Company!.SalaryStructures);
    }

    // ============================================================ B7 — Pay Head, front line

    /// <summary>
    /// <b>THE RED PROOF FOR <c>B7</c>, value slab.</b> <c>PayHeadService.ValidateComputation</c> guards calc-type,
    /// self-reference, duplicates and cycles but never the slab money; the file's ONLY <c>IsPaisaExact</c> covers
    /// <c>RoundingLimit</c> alone. A sub-paisa flat-value slab reached <c>Paisa.FromMoney</c> at the store.
    /// </summary>
    [Fact]
    public void ASubPaisaValueSlabIsRefusedAtAddSlabInsteadOfByTheStore()
    {
        var vm = NewPayrollCompany("SlabValueSubPaisa");
        vm.ShowPayHeadMaster();
        var page = vm.PayHeadMaster!;

        page.SelectedSlabType = page.SlabTypes.Single(s => s.Value == PayHeadComputationSlabType.FlatValue);
        page.SlabRateOrValueText = "1875.005";
        page.AddSlab();

        AssertFrontLineMessage(page.Message, "value slab amount", "1875.005");
        Assert.Empty(page.Slabs);
    }

    /// <summary>The band bounds are money too, and persist through the same conversion (<c>from_amount_paisa</c>).</summary>
    [Fact]
    public void ASubPaisaSlabOverBoundIsRefusedAtAddSlab()
    {
        var vm = NewPayrollCompany("SlabFromSubPaisa");
        vm.ShowPayHeadMaster();
        var page = vm.PayHeadMaster!;

        page.SelectedSlabType = page.SlabTypes.Single(s => s.Value == PayHeadComputationSlabType.Percentage);
        page.SlabFromText = "10000.125";
        page.SlabRateOrValueText = "12";
        page.AddSlab();

        AssertFrontLineMessage(page.Message, "slab 'over' amount", "10000.125");
        Assert.Empty(page.Slabs);
    }

    /// <summary>…and the upper bound (<c>to_amount_paisa</c>).</summary>
    [Fact]
    public void ASubPaisaSlabUpToBoundIsRefusedAtAddSlab()
    {
        var vm = NewPayrollCompany("SlabToSubPaisa");
        vm.ShowPayHeadMaster();
        var page = vm.PayHeadMaster!;

        page.SelectedSlabType = page.SlabTypes.Single(s => s.Value == PayHeadComputationSlabType.Percentage);
        page.SlabFromText = "10000.13";
        page.SlabToText = "50000.335";
        page.SlabRateOrValueText = "12";
        page.AddSlab();

        AssertFrontLineMessage(page.Message, "slab 'up to' amount", "50000.335");
        Assert.Empty(page.Slabs);
    }

    /// <summary>An odd but paisa-exact band + value slab must still be accepted.</summary>
    [Fact]
    public void APaisaExactOddSlabIsStillAccepted()
    {
        var vm = NewPayrollCompany("SlabOddButExact");
        vm.ShowPayHeadMaster();
        var page = vm.PayHeadMaster!;

        page.SelectedSlabType = page.SlabTypes.Single(s => s.Value == PayHeadComputationSlabType.FlatValue);
        page.SlabFromText = "10000.13";
        page.SlabToText = "50000.33";
        page.SlabRateOrValueText = "1875.07";
        page.AddSlab();

        Assert.Null(page.Message);
        var slab = Assert.Single(page.Slabs);
        Assert.Equal(1875.07m, slab.Value.Amount);
        Assert.Equal(10000.13m, slab.FromAmount!.Value.Amount);
        Assert.Equal(50000.33m, slab.ToAmount!.Value.Amount);
    }

    /// <summary>
    /// The FOURTH money field on this screen, and the one the brief marked "ALREADY GUARDED".
    /// <c>PayHeadService.cs:156</c> guards sub-paisa only (<c>!payHead.RoundingLimit.IsPaisaExact</c>) — the same
    /// HALF-rule the six domain guards carried: no magnitude ceiling. <c>rounding_limit_paisa</c> is written
    /// through <c>Paisa.FromMoney</c>, so a paisa-exact 18-digit limit passed the parse, passed
    /// <c>limit &lt;= 0m</c>, passed <c>ValidatePayHead</c> and overflowed <c>long</c> inside the store.
    ///
    /// <para>The broad catch this slice added does now CONTAIN that — it is a message, not a crash — which is why
    /// the assertion cannot be a bare <c>Assert.False(Create())</c>: that passes on the un-guarded code and proves
    /// nothing. What the guard adds is the FIELD-NAMED refusal its three slab siblings twelve lines above already
    /// give, instead of the store's raw <c>"too large or too small for an Int64"</c>.</para>
    /// </summary>
    [Fact]
    public void AnOverLargeRoundingLimitIsRefusedByItsOwnFieldGuardRatherThanTheStoresOverflow()
    {
        var vm = NewPayrollCompany("RoundingLimitTooLarge");
        vm.ShowPayHeadMaster();
        var page = vm.PayHeadMaster!;
        page.Name = "Conveyance";
        page.SelectedType = page.Types.Single(t => t.Value == PayHeadType.Earnings);
        page.SelectedCalcType = page.CalcTypes.Single(c => c.Value == PayHeadCalculationType.FlatRate);
        page.SelectedRoundingMethod = page.RoundingMethods.Single(r => r.Value == PayHeadRoundingMethod.Normal);
        page.RoundingLimitText = "999999999999999999.13";   // > long.MaxValue paisa, and paisa-EXACT

        Assert.False(page.Create());
        Assert.NotNull(page.Message);
        Assert.Contains("the rounding limit", page.Message);
        Assert.Contains("too large", page.Message);
        Assert.DoesNotContain("Int64", page.Message);       // …the store's wording, which is what shipped
        Assert.Null(vm.Company!.FindPayHeadByName("Conveyance"));
    }

    /// <summary>The sub-paisa half of the same field, and an odd-paisa-exact limit that must still be accepted —
    /// so the guard is shown to refuse for the stated reason rather than to refuse everything.</summary>
    [Fact]
    public void ASubPaisaRoundingLimitIsRefusedButAnOddPaisaExactOneIsAccepted()
    {
        var vm = NewPayrollCompany("RoundingLimitSubPaisa");
        vm.ShowPayHeadMaster();
        var page = vm.PayHeadMaster!;
        page.Name = "Conveyance";
        page.SelectedType = page.Types.Single(t => t.Value == PayHeadType.Earnings);
        page.SelectedCalcType = page.CalcTypes.Single(c => c.Value == PayHeadCalculationType.FlatRate);
        page.SelectedRoundingMethod = page.RoundingMethods.Single(r => r.Value == PayHeadRoundingMethod.Normal);
        page.RoundingLimitText = "0.125";

        Assert.False(page.Create());
        AssertFrontLineMessage(page.Message, "the rounding limit", "0.125");
        Assert.Null(vm.Company!.FindPayHeadByName("Conveyance"));

        page.RoundingLimitText = "0.13";
        Assert.True(page.Create(), page.Message);
        Assert.Equal(0.13m, vm.Company!.FindPayHeadByName("Conveyance")!.RoundingLimit.Amount);
    }

    // ============================================================ B7 — Pay Head, rollback + filter

    /// <summary>
    /// <b>The CLASS-B poisoning, on the pay-head screen.</b> <c>PayHeadService.CreatePayHead</c> adds the head to
    /// the company BEFORE the save; a failed save must take it back off.
    /// </summary>
    [Fact]
    public void ANonReportablePayHeadSaveFailureRollsThePayHeadOffTheAggregateAndRethrows()
    {
        var vm = NewPayrollCompany("PayHeadNonReportable");
        vm.ShowPayHeadMaster();
        var page = vm.PayHeadMaster!;
        page.Name = "Conveyance";
        page.SelectedType = page.Types.Single(t => t.Value == PayHeadType.Earnings);
        page.SelectedCalcType = page.CalcTypes.Single(c => c.Value == PayHeadCalculationType.FlatRate);

        PlantNonReportableSaveFailure(vm.Company!);

        Assert.Throws<NullReferenceException>(() => page.Create());
        Assert.Null(vm.Company!.FindPayHeadByName("Conveyance"));
    }

    /// <summary>
    /// <b>The CLASS-B widened filter, on the pay-head screen.</b> A <c>SqliteException</c> was a crash under the
    /// narrow filter at the shipped <c>:488</c>; it must now be a message, and must leave no pay head behind.
    /// </summary>
    [Fact]
    public void AReportablePayHeadSaveFailureIsAMessageAndLeavesNoPayHeadBehind()
    {
        var vm = NewPayrollCompany("PayHeadReportable");
        vm.ShowPayHeadMaster();
        var page = vm.PayHeadMaster!;
        page.Name = "Conveyance";
        page.SelectedType = page.Types.Single(t => t.Value == PayHeadType.Earnings);
        page.SelectedCalcType = page.CalcTypes.Single(c => c.Value == PayHeadCalculationType.FlatRate);

        PlantReportableSaveFailure(vm.Company!);

        Assert.False(page.Create());
        AssertReportedTheStoreFailure(page.Message);
        Assert.Null(vm.Company!.FindPayHeadByName("Conveyance"));
    }

    // ---------------------------------------------------------------- shared master helpers

    private PayHead CreateFlatEarning(MainWindowViewModel vm, string name)
    {
        vm.ShowPayHeadMaster();
        var m = vm.PayHeadMaster!;
        m.Name = name;
        m.SelectedType = m.Types.Single(t => t.Value == PayHeadType.Earnings);
        m.SelectedCalcType = m.CalcTypes.Single(c => c.Value == PayHeadCalculationType.FlatRate);
        Assert.True(m.Create(), m.Message);
        return vm.Company!.FindPayHeadByName(name)!;
    }

    private Employee CreateEmployee(MainWindowViewModel vm, string group, string name)
    {
        vm.ShowEmployeeGroupMaster();
        var g = vm.EmployeeGroupMaster!;
        if (vm.Company!.FindEmployeeGroupByName(group) is null)
        {
            g.Name = group;
            Assert.True(g.Create(), g.Message);
        }

        vm.ShowEmployeeMaster();
        var e = vm.EmployeeMaster!;
        e.Name = name;
        e.SelectedGroup = e.GroupOptions.Single(o => o.Group.Name == group);
        Assert.True(e.Create(), e.Message);
        return vm.Company!.FindEmployeeByName(name)!;
    }
}
