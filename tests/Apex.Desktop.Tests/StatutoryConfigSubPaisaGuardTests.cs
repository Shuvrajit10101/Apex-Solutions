using System;
using System.IO;
using System.Linq;
using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Services;
using Apex.Desktop.Services;
using Apex.Desktop.ViewModels;
using Xunit;

namespace Apex.Desktop.Tests;

/// <summary>
/// <b>The F11 Statutory-Configuration page must reject a Gratuity / Bonus figure the paisa store cannot carry
/// BEFORE it touches the shared Company aggregate — and must put the aggregate back if the save fails for any
/// other reason, in either direction.</b>
///
/// <para><b>The defect this pins.</b> The "one rule, one home" slice replaced the SQLite store's three establishment
/// money writes — <c>$gratcap</c>, <c>$bonusceil</c>, <c>$bonusminwage</c>
/// (<c>SqliteCompanyStore.InsertCompany</c>) — with the THROWING <c>Paisa.FromDecimal</c>. That is the right call at
/// a persistence boundary, but the front-line guard the rest of the codebase pairs it with was never added here:
/// <c>GstConfigViewModel.TryParseWholeRupees</c> is a plain <c>decimal.TryParse</c> and the only further check is
/// <c>&lt; 0m</c>, unlike <c>LedgerMasterViewModel</c>, <c>StockItemMasterViewModel</c>, <c>TaxDeclarationViewModel</c>
/// and ~15 other typed-amount paths, all of which check <see cref="Money.IsPaisaExact"/>.</para>
///
/// <para><b>Why the consequence was worse than a bad error message.</b> <c>PayrollService.EnableGratuity</c> sets
/// <c>Company.PayrollStatutoryEnabled</c> and then <c>Company.GratuityConfig</c> BEFORE <c>_storage.Save</c>, and
/// neither config validates paisa-exactness (only <c>&lt; 0m</c>). So the bad config entered the SHARED in-memory
/// aggregate, the store then threw, the catch reported the message and returned <c>false</c> — and the poisoned
/// config STAYED. Every later save of that same Company then threw from the store, including from the ~99 save
/// sites that have no try/catch at all (<c>BudgetMasterViewModel.Save</c>, …), so an unrelated screen crashed with
/// an unhandled <c>InvalidOperationException</c> long after the F11 keystroke that caused it. The blast radius is
/// this SESSION, not this book: <c>Save</c> is fully transactional (<c>BeginTransaction</c>), so the .db on disk is
/// never corrupted — only the in-memory aggregate diverges from it.</para>
///
/// <para><b>The catch did not even revert the toggle.</b> <c>RevertGratuityToggle</c> re-derives
/// <c>GratuityEnabled</c> from <c>_company.GratuityConfig is not null</c> — which the failed apply had just made
/// TRUE. So the toggle stayed ON over a poisoned config, and <c>AcceptStatutoryConfig</c> re-runs
/// <c>ApplyGratuity()</c> on EVERY Ctrl+A / Enter accept of the page while the toggle is on, re-poisoning from the
/// keyboard accept too. Restoring the previous config first is what makes those two revert helpers start working.
/// </para>
///
/// <para><b>What is red against what.</b> Three defects were fixed together, so no single test is "the" red proof:
/// <b>T1</b> is red against the whole slice reverted (a poisoned config and a follow-up save that throws), but goes
/// green on the rollback alone — so <b>T2</b>, which asserts the message is the SCREEN's and not the store's, is the
/// pin for the guard on its own, and <b>T5 / T5b / T5c</b> are the pins for the rollback on its own (they force a
/// save failure the guard cannot prevent). <b>T9</b> is red against the magnitude bound, which is the branch whose
/// absence produced an unhandled <c>OverflowException</c> rather than a message.</para>
///
/// <para>Fixtures are ODD sub-paisa throughout (₹19,99,999.995 / ₹6,999.995 / ₹5,721.005) — a round figure is
/// paisa-exact and asserts nothing — and the valid figures are deliberately never the defaults, so a passing test
/// proves the typed value was carried rather than a default surviving.</para>
/// </summary>
public sealed class StatutoryConfigSubPaisaGuardTests : IDisposable
{
    private readonly string _tempDir;
    private readonly CompanyStorage _storage;

    public StatutoryConfigSubPaisaGuardTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "ApexStatConfigSubPaisa_" + Guid.NewGuid().ToString("N"));
        _storage = new CompanyStorage(_tempDir);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true); }
        catch (IOException) { /* a held SQLite handle must not fail the test */ }
    }

    // ---------------------------------------------------------------- harness

    /// <summary>A payroll company with Payroll + Payroll Statutory on — which is what makes the Gratuity and Bonus
    /// blocks visible on the F11 page — saved once so the baseline is known good.</summary>
    private MainWindowViewModel NewPayrollCompany(string name)
    {
        var vm = new MainWindowViewModel(_storage);
        vm.NewCompanyName = name;
        vm.CreateCompany();
        vm.ShowGstConfig();
        var page = vm.GstConfig!;
        page.PayrollEnabled = true;
        page.PayrollStatutoryEnabled = true;
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

    /// <summary>A fresh F11 page over an arbitrary company instance — the route a company that came straight off
    /// disk reaches the loaders (<c>MainWindowViewModel.ShowGstConfig</c> constructs the page exactly this way).</summary>
    private GstConfigViewModel PageOver(Company company) =>
        new GstConfigViewModel(company, _storage, onChanged: () => { });

    /// <summary>Makes the NEXT save of <paramref name="company"/> fail for a reason that has nothing to do with the
    /// three statutory fields, by a route no screen guard covers. The ledger is written in <c>InsertLedgers</c>,
    /// which runs AFTER <c>InsertCompany</c>, so the statutory columns are written successfully first and the throw
    /// genuinely lands on the rollback path. Precedent: <c>CanonicalGratuityBonusRoundTripTests.cs:96</c>.</summary>
    private static void MakeTheNextSaveFail(Company company) =>
        company.Ledgers.First().OpeningBalance = new Money(10.005m);

    /// <summary>Drives one of the three fields end-to-end and returns what the screen decided and said.</summary>
    private static (bool Applied, string? Message) ApplyField(GstConfigViewModel page, string fieldLabel, string text)
    {
        switch (fieldLabel)
        {
            case "the gratuity cap":
                page.GratuityEnabled = true;
                page.GratuityCapText = text;
                return (page.ApplyGratuity(), page.GratuityMessage);
            case "the calculation ceiling":
                page.BonusEnabled = true;
                page.BonusCalculationCeilingText = text;
                return (page.ApplyBonus(), page.BonusMessage);
            case "the minimum wage":
                page.BonusEnabled = true;
                page.BonusMinimumWageText = text;
                return (page.ApplyBonus(), page.BonusMessage);
            default: throw new InvalidOperationException($"unmapped field '{fieldLabel}'");
        }
    }

    /// <summary>
    /// <b>The screen's ceiling must BE the store's carrier bound, not a hand-derivation that happens to match it
    /// today</b> (W0-13, drift lock D3). <c>MaxStatutoryRupees</c> shipped as the literal
    /// <c>92_233_720_368_547_758m</c> — <see cref="PaisaConversion.MaxStorableRupees"/> floored, re-typed by hand
    /// in the very file the shared guard was extracted from — and is now
    /// <c>decimal.Floor(PaisaConversion.MaxStorableRupees)</c>.
    ///
    /// <para><b>Why the boundary and not the constant.</b> The field is <c>private static readonly</c>, so a test
    /// cannot read it; and asserting a computed expectation against a computed actual would pass on the
    /// hand-typed literal too. Driving the two figures either side of the floor through the real screen does
    /// discriminate: if the screen's ceiling ever drifts from the carrier IN EITHER DIRECTION, exactly one of
    /// these two assertions fails. Whole rupees are the screen's own constraint, which is why the bound is the
    /// FLOOR of the carrier and not the carrier itself.</para>
    /// </summary>
    [Fact]
    public void TheStatutoryScreensCeilingIsTheStoresCarrierBoundFlooredToTheWholeRupee()
    {
        var ceiling = decimal.Floor(PaisaConversion.MaxStorableRupees);

        var vm = NewPayrollCompany("Statutory Ceiling Boundary Co");
        var page = StatutoryPage(vm);

        // …one whole rupee past the carrier is refused, and says so as a magnitude, not as a paisa problem.
        var over = ApplyField(page, "the gratuity cap", (ceiling + 1m).ToString("0", System.Globalization.CultureInfo.InvariantCulture));
        Assert.False(over.Applied);
        Assert.NotNull(over.Message);
        Assert.Contains("too large", over.Message);
        Assert.DoesNotContain("finer than a paisa", over.Message);
        Assert.Null(vm.Company!.GratuityConfig);

        // …and the ceiling itself is accepted, so the guard is not merely over-refusing.
        var at = ApplyField(page, "the gratuity cap", ceiling.ToString("0", System.Globalization.CultureInfo.InvariantCulture));
        Assert.True(at.Applied, at.Message);
        Assert.Equal(ceiling, vm.Company!.GratuityConfig!.CapAmount.Amount);
    }

    /// <summary>The enrolment the named field belongs to — the one that must NOT appear after a refusal.</summary>
    private static object? EnrolmentFor(Company company, string fieldLabel) =>
        fieldLabel == "the gratuity cap" ? company.GratuityConfig : company.BonusConfig;

    /// <summary>Enrols the OTHER feature validly, so a refusal in <paramref name="fieldLabel"/> has something real
    /// to leave standing. Asserting "the sibling config is still null" would be unfalsifiable — nothing in the test
    /// ever touched it — whereas asserting a LIVE sibling enrolment survives genuinely bites.</summary>
    private static void EnrolTheSibling(GstConfigViewModel page, string fieldLabel)
    {
        if (fieldLabel == "the gratuity cap")
        {
            page.BonusEnabled = true;
            page.BonusCalculationCeilingText = "6999";
            page.BonusMinimumWageText = "5721";
            Assert.True(page.ApplyBonus());
        }
        else
        {
            page.GratuityEnabled = true;
            page.GratuityCapText = "1999999";
            Assert.True(page.ApplyGratuity());
        }
    }

    /// <summary>Asserts the sibling enrolment made by <see cref="EnrolTheSibling"/> is still there, unchanged.</summary>
    private static void AssertTheSiblingSurvived(Company company, string fieldLabel)
    {
        if (fieldLabel == "the gratuity cap")
        {
            Assert.NotNull(company.BonusConfig);
            Assert.Equal(6_999m, company.BonusConfig!.CalculationCeiling.Amount);
            Assert.Equal(5_721m, company.BonusConfig.MinimumWage.Amount);
        }
        else
        {
            Assert.NotNull(company.GratuityConfig);
            Assert.Equal(1_999_999m, company.GratuityConfig!.CapAmount.Amount);
        }
    }

    /// <summary>The field's pre-existing invalid/negative message, reproduced here byte-for-byte. The refactor that
    /// moved three inline checks into one shared helper is the classic place to lose a <c>|| value &lt; 0m</c> or to
    /// reword a message, and neither would otherwise be caught: a negative cap merely falls through to
    /// <c>PayrollService</c>'s own throw, so the only symptom is different wording.</summary>
    private static string ExpectedInvalidMessage(string fieldLabel) => fieldLabel switch
    {
        "the gratuity cap" => "The gratuity cap must be a non-negative whole-rupee amount (e.g. 2000000).",
        "the calculation ceiling" => "The calculation ceiling must be a non-negative whole-rupee amount (e.g. 7000).",
        "the minimum wage" => "The minimum wage must be a non-negative whole-rupee amount (0 ⇒ ceiling ₹7,000).",
        _ => throw new InvalidOperationException($"unmapped field '{fieldLabel}'"),
    };

    // ================================================================ (T1) the assertion that actually bites

    /// <summary>
    /// <b>A sub-paisa gratuity cap is refused and never enters the aggregate.</b> This is the assertion that fails
    /// on HEAD. Note that <c>Assert.False(ApplyGratuity())</c> PASSES on HEAD too — the store's own throw
    /// is caught and returned as false — so it is NOT the proof; the config and the follow-up save are. Note also
    /// that both of those go green on the ROLLBACK alone (which puts the config back after the store throws), so
    /// T2 below is what pins the guard by itself.
    /// </summary>
    [Fact]
    public void SubPaisaGratuityCapIsRejectedAndNeverEntersTheCompany()
    {
        var vm = NewPayrollCompany("SubPaisa Gratuity Co");
        var page = StatutoryPage(vm);

        page.GratuityEnabled = true;
        page.GratuityCapText = "1999999.995";

        Assert.False(page.ApplyGratuity());

        // 1. The aggregate is clean — the rejected cap never became an enrolment.
        Assert.Null(vm.Company!.GratuityConfig);

        // 2. …and it STAYS usable: an unrelated save of the same instance still succeeds. This is the step that
        //    threw an unhandled InvalidOperationException before the guard, from a screen with no try/catch.
        Assert.Null(Record.Exception(() => _storage.Save(vm.Company)));
    }

    // ================================================================ (T2) the message is the SCREEN's, not the store's

    /// <summary>
    /// The rejection message names the field and comes from the screen. <b>This is the pin for the front-line guard
    /// on its own</b> — with the guard removed but the rollback kept, the aggregate assertions of T1 still pass and
    /// only this test goes red.
    ///
    /// <para><b>The trap this closes.</b> <c>Assert.Contains("paisa", …)</c> PASSES on the unfixed code — the
    /// store's own throw is caught and shown, and its text is "Amount 1999999.995 is not paisa-exact (more than 2
    /// decimal places); cannot persist or serialise without loss.", which contains "paisa". So the discriminating
    /// assertions are the field name (which the store cannot know) and the ABSENCE of the store's wording.</para>
    /// </summary>
    [Fact]
    public void TheSubPaisaMessageNamesTheFieldAndIsNotTheRawPersistenceError()
    {
        var vm = NewPayrollCompany("SubPaisa Gratuity Message Co");
        var page = StatutoryPage(vm);

        page.GratuityEnabled = true;
        page.GratuityCapText = "1999999.995";

        Assert.False(page.ApplyGratuity());
        Assert.NotNull(page.GratuityMessage);
        Assert.Contains("gratuity cap", page.GratuityMessage!, StringComparison.Ordinal);
        Assert.Contains("paisa", page.GratuityMessage!, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("cannot persist", page.GratuityMessage!, StringComparison.Ordinal);
    }

    // ================================================================ (T3) all three fields, not just the first

    /// <summary>
    /// Each of the three establishment amounts the store writes through the throwing <c>Paisa.FromDecimal</c>
    /// carries the guard — a gap in any one of them reopens the same aggregate-poisoning path — and a refusal in
    /// one field leaves the OTHER feature's live enrolment untouched.
    /// </summary>
    [Theory]
    [InlineData("the gratuity cap", "1999999.995")]
    [InlineData("the calculation ceiling", "6999.995")]
    [InlineData("the minimum wage", "5721.005")]
    public void EveryStatutoryRupeeFieldRefusesASubPaisaAmount(string fieldLabel, string subPaisa)
    {
        var vm = NewPayrollCompany("SubPaisa Fields Co " + fieldLabel);
        var page = StatutoryPage(vm);
        EnrolTheSibling(page, fieldLabel);

        var (applied, message) = ApplyField(page, fieldLabel, subPaisa);

        Assert.False(applied);
        Assert.NotNull(message);
        Assert.Contains(fieldLabel, message!, StringComparison.Ordinal);

        // The refused enrolment never entered the aggregate — the guard runs before PayrollService touches it…
        Assert.Null(EnrolmentFor(vm.Company!, fieldLabel));
        // …and the sibling feature, which WAS enrolled, is still enrolled at its own figures.
        AssertTheSiblingSurvived(vm.Company!, fieldLabel);

        // …and the aggregate is still saveable, which is the damage the guard exists to prevent.
        Assert.Null(Record.Exception(() => _storage.Save(vm.Company!)));
    }

    // ================================================================ (T4) the guard is not over-eager

    /// <summary>
    /// Whole-rupee amounts still enrol, persist and survive a reload. A guard that rejected everything would pass
    /// every test above while breaking the screen. The figures are deliberately NOT the defaults (₹20,00,000 /
    /// ₹7,000 / ₹0), so a pass proves the typed value was carried rather than a default surviving.
    /// </summary>
    [Fact]
    public void WholeRupeeStatutoryAmountsStillEnrolAndRoundTrip()
    {
        const string companyName = "Whole Rupee Statutory Co";
        var vm = NewPayrollCompany(companyName);
        var page = StatutoryPage(vm);

        page.GratuityEnabled = true;
        page.GratuityCapText = "1999999";
        Assert.True(page.ApplyGratuity());

        page.BonusEnabled = true;
        page.BonusCalculationCeilingText = "6999";
        page.BonusMinimumWageText = "5721";
        Assert.True(page.ApplyBonus());

        Assert.Equal(1_999_999m, vm.Company!.GratuityConfig!.CapAmount.Amount);
        Assert.Equal(6_999m, vm.Company.BonusConfig!.CalculationCeiling.Amount);
        Assert.Equal(5_721m, vm.Company.BonusConfig.MinimumWage.Amount);

        var reloaded = Reload(companyName);
        Assert.Equal(1_999_999m, reloaded.GratuityConfig!.CapAmount.Amount);
        Assert.Equal(6_999m, reloaded.BonusConfig!.CalculationCeiling.Amount);
        Assert.Equal(5_721m, reloaded.BonusConfig.MinimumWage.Amount);
    }

    // ================================================================ (T5) the rollback, pinned independently

    /// <summary>
    /// <b>A save that fails for an UNRELATED reason must still leave the aggregate as it was.</b>
    ///
    /// <para>Once the front-line guard is in place a sub-paisa cap never reaches the store, so the catch block can
    /// no longer be exercised through the three guarded fields — yet <c>Save</c> can still throw for any number of
    /// other reasons (the seven other unguarded <c>Paisa.FromMoney</c> paths, an imported config, a .db another
    /// instance holds the write lock on), and whenever it does the pre-fix code left memory holding an enrolment
    /// the rolled-back .db does not have. So the rollback needs its own test: corrupt a ledger's opening balance
    /// DIRECTLY (bypassing every screen guard), then apply a perfectly VALID cap.</para>
    ///
    /// <para>The final assertion is the one the pre-fix code could never satisfy: <c>RevertGratuityToggle</c>
    /// re-derives the toggle from <c>_company.GratuityConfig</c>, so unless the config is restored FIRST the toggle
    /// stays ON over a phantom enrolment — and <c>AcceptStatutoryConfig</c> would re-apply it on the next Ctrl+A.</para>
    /// </summary>
    [Fact]
    public void AFailedSaveRollsTheEnrolmentBackOutOfTheAggregateAndTheToggleOff()
    {
        var vm = NewPayrollCompany("Rollback On Failure Co");
        var page = StatutoryPage(vm);

        MakeTheNextSaveFail(vm.Company!);

        page.GratuityEnabled = true;
        page.GratuityCapText = "1999999";   // a perfectly valid, paisa-exact, whole-rupee cap

        Assert.False(page.ApplyGratuity());

        // The enrolment was rolled back out of the shared aggregate…
        Assert.Null(vm.Company!.GratuityConfig);
        // …and the toggle followed it, instead of latching ON over a config that was never persisted.
        Assert.False(page.GratuityEnabled);
    }

    // ================================================================ (T5b) …and the same for Bonus

    /// <summary>
    /// The Bonus twin of T5. Not symmetry pedantry: <c>ApplyBonus</c> is the method with THREE parsed money fields
    /// and a clamped rate, it is reached from the same Ctrl+A accept, and its catch restores a config object that
    /// <c>PayrollService.EnableStatutoryBonus</c> replaces wholesale. Without this, deleting the bonus restore is a
    /// free, silent regression — measured: with only that line removed the whole Desktop project still passed.
    /// </summary>
    [Fact]
    public void AFailedSaveRollsTheBonusEnrolmentBackOutOfTheAggregateAndTheToggleOff()
    {
        var vm = NewPayrollCompany("Rollback On Failure Bonus Co");
        var page = StatutoryPage(vm);

        MakeTheNextSaveFail(vm.Company!);

        page.BonusEnabled = true;
        page.BonusCalculationCeilingText = "6999";
        page.BonusMinimumWageText = "5721";

        Assert.False(page.ApplyBonus());

        Assert.Null(vm.Company!.BonusConfig);
        Assert.False(page.BonusEnabled);
    }

    // ================================================================ (T5c) …and the Payroll-Statutory sibling flag

    /// <summary>
    /// <b>The rollback restores the sibling <c>PayrollStatutoryEnabled</c> flag, not only the config.</b>
    ///
    /// <para>Every other test in this file runs on a fixture with Payroll Statutory already ON, which makes that
    /// restore a write of <c>true</c> over <c>true</c> — a guaranteed no-op that pins nothing (measured: with the
    /// two restore lines removed the whole Desktop project still passed). The behaviour is real and reachable
    /// though: F11 with Payroll Statutory OFF, the user flips Gratuity on, <c>EnableGratuity</c> turns Statutory ON
    /// as a side effect, the save fails — and the sibling toggle must go back off with the enrolment. So this test
    /// uses a deliberately different harness: Payroll on, Payroll Statutory left OFF.</para>
    /// </summary>
    [Fact]
    public void AFailedSaveTurnsThePayrollStatutorySiblingFlagBackOffToo()
    {
        var vm = new MainWindowViewModel(_storage);
        vm.NewCompanyName = "Statutory Sibling Rollback Co";
        vm.CreateCompany();
        vm.ShowGstConfig();
        var page = vm.GstConfig!;
        page.PayrollEnabled = true;                            // …and PayrollStatutoryEnabled deliberately NOT set
        Assert.False(vm.Company!.PayrollStatutoryEnabled);      // the precondition this whole test rests on

        MakeTheNextSaveFail(vm.Company);

        page.GratuityEnabled = true;
        page.GratuityCapText = "1999999";
        Assert.False(page.ApplyGratuity());

        Assert.Null(vm.Company.GratuityConfig);
        // EnableGratuity turned Payroll Statutory ON before the store was reached; the rollback turns it back off.
        Assert.False(vm.Company.PayrollStatutoryEnabled);
    }

    // ================================================================ (T5d/T5e) the DISABLE direction

    /// <summary>
    /// <b>The mirror-image divergence: a failed save must not make memory LOSE an enrolment the .db still holds.</b>
    ///
    /// <para><c>ApplyGratuity</c>'s disable branch nulls <c>Company.GratuityConfig</c> BEFORE the save. <c>Save</c>
    /// is transactional, so a throw rolls the whole write back and the enrolment is still on disk — but the
    /// in-memory aggregate every other screen shares had already lost it, and <c>RevertGratuityToggle</c>, reading
    /// that same nulled field, latched the toggle OFF over it. No test in the repository ever set either toggle to
    /// FALSE, so this branch shipped with literally zero coverage (measured: with both disable-branch restores
    /// stripped, the whole Desktop project still passed).</para>
    /// </summary>
    [Fact]
    public void AFailedSaveKeepsTheGratuityEnrolmentTheDatabaseStillHolds()
    {
        const string companyName = "Disable Rollback Gratuity Co";
        var vm = NewPayrollCompany(companyName);
        var page = StatutoryPage(vm);

        page.GratuityEnabled = true;
        page.GratuityCapText = "1999999";
        Assert.True(page.ApplyGratuity());

        MakeTheNextSaveFail(vm.Company!);

        page.GratuityEnabled = false;
        Assert.False(page.ApplyGratuity());

        // The .db still holds the enrolment (the transaction rolled back)…
        Assert.NotNull(Reload(companyName).GratuityConfig);
        // …so memory must too, and the toggle must follow memory rather than the abandoned clear.
        Assert.NotNull(vm.Company!.GratuityConfig);
        Assert.Equal(1_999_999m, vm.Company.GratuityConfig!.CapAmount.Amount);
        Assert.True(page.GratuityEnabled);
    }

    /// <summary>The Bonus twin of the disable-direction test above.</summary>
    [Fact]
    public void AFailedSaveKeepsTheBonusEnrolmentTheDatabaseStillHolds()
    {
        const string companyName = "Disable Rollback Bonus Co";
        var vm = NewPayrollCompany(companyName);
        var page = StatutoryPage(vm);

        page.BonusEnabled = true;
        page.BonusCalculationCeilingText = "6999";
        page.BonusMinimumWageText = "5721";
        Assert.True(page.ApplyBonus());

        MakeTheNextSaveFail(vm.Company!);

        page.BonusEnabled = false;
        Assert.False(page.ApplyBonus());

        Assert.NotNull(Reload(companyName).BonusConfig);
        Assert.NotNull(vm.Company!.BonusConfig);
        Assert.Equal(6_999m, vm.Company.BonusConfig!.CalculationCeiling.Amount);
        Assert.True(page.BonusEnabled);
    }

    // ================================================================ (T5f) a failure that is NOT a domain error

    /// <summary>
    /// <b>A read-only company file is an ordinary operational state, and it must be a message — not a crash, and
    /// not a diverged aggregate.</b>
    ///
    /// <para>Every other rollback test here forces the failure with a sub-paisa ledger amount, which surfaces as an
    /// <c>InvalidOperationException</c> — the one type the screen's original <c>when (ex is
    /// InvalidOperationException or ArgumentException)</c> filter matched. That filter decided two things at once:
    /// whether to show a message AND whether the rollback ran at all. <c>SqliteCompanyStore</c> contains no catch
    /// blocks, so Microsoft.Data.Sqlite's <c>SqliteException</c> (SQLITE_READONLY here; SQLITE_BUSY when a second
    /// Apex instance holds the write lock; SQLITE_FULL) propagates raw and matched neither type — the enrolment
    /// stayed in memory and the exception left the Ctrl+A handler unhandled. This test drives that exact class of
    /// failure by marking the .db read-only.</para>
    /// </summary>
    [Fact]
    public void AReadOnlyCompanyFileIsReportedAndRolledBackRatherThanCrashingTheAccept()
    {
        const string companyName = "Read Only Db Co";
        var vm = NewPayrollCompany(companyName);
        var page = StatutoryPage(vm);
        var dbPath = _storage.PathForName(companyName);

        File.SetAttributes(dbPath, File.GetAttributes(dbPath) | FileAttributes.ReadOnly);
        try
        {
            page.GratuityEnabled = true;
            page.GratuityCapText = "1999999";   // a perfectly valid cap — the FILE is what refuses

            var applied = true;
            var escaped = Record.Exception(() => { applied = page.ApplyGratuity(); });

            Assert.Null(escaped);                                   // it is a message, not an unhandled throw
            Assert.False(applied);
            Assert.NotNull(page.GratuityMessage);
            Assert.Null(vm.Company!.GratuityConfig);                // …and the aggregate went back
            Assert.False(page.GratuityEnabled);
        }
        finally
        {
            File.SetAttributes(dbPath, File.GetAttributes(dbPath) & ~FileAttributes.ReadOnly);
        }
    }

    // ================================================================ (T6) the parser honours its own name

    /// <summary>
    /// <b>A fractional amount is refused even when it IS paisa-exact</b> — because the rest of this contract
    /// already says whole rupees: the three property docs and all three error messages. (The three XAML
    /// placeholders do NOT say it — they are example values — and the domain's <c>BonusConfig.MinimumWage</c>
    /// validates only <c>&lt; 0m</c>; the constraint is the screen's, and is stated where the screen states it.)
    /// <c>TryParseWholeRupees</c> was the lone dissenter, a plain <c>decimal.TryParse</c>.
    ///
    /// <para><b>Why this is a wrong-FIGURES defect and not mere strictness.</b> A paisa-exact ₹7,000.57 was
    /// accepted and stored, but the loader rendered it through a <c>(long)</c> cast — "7000" — so the very next
    /// Ctrl+A (which <c>AcceptStatutoryConfig</c> turns into another <c>ApplyGratuity()</c>/<c>ApplyBonus()</c>
    /// while the toggle is on) re-applied the TRUNCATED figure and silently rewrote the establishment's cap. The
    /// user was shown one number, the book held another, and the difference vanished with no message. T7 drives
    /// that exact gesture.</para>
    ///
    /// <para>The sub-paisa branch must be reached FIRST, so this asserts the whole-rupee message and the ABSENCE
    /// of the sub-paisa wording — a helper that ordered the two branches the other way round would report
    /// "must be a non-negative whole-rupee amount" for ₹1,999,999.995 and break T3's field-name assertion. The
    /// field name is asserted case-INsensitively here because the reused message opens with a capital ("The
    /// calculation ceiling…"), and without it a mislabelled call site would slip through this branch.</para>
    /// </summary>
    [Theory]
    [InlineData("the gratuity cap", "1999999.50")]
    [InlineData("the calculation ceiling", "6999.57")]
    [InlineData("the minimum wage", "5721.25")]
    public void EveryStatutoryRupeeFieldRefusesAFractionalAmountEvenWhenItIsPaisaExact(string fieldLabel, string fractional)
    {
        var vm = NewPayrollCompany("Fractional Fields Co " + fieldLabel);
        var page = StatutoryPage(vm);
        EnrolTheSibling(page, fieldLabel);

        var (applied, message) = ApplyField(page, fieldLabel, fractional);

        // On HEAD+the sub-paisa guard alone this is TRUE — the value is paisa-exact, so that guard waves it through.
        Assert.False(applied);
        Assert.NotNull(message);
        Assert.Contains("whole-rupee", message!, StringComparison.Ordinal);
        Assert.Contains(fieldLabel, message!, StringComparison.OrdinalIgnoreCase);
        // …and it is the whole-rupee message, not the sub-paisa one: the branch order is load-bearing.
        Assert.DoesNotContain("finer than a paisa", message!, StringComparison.Ordinal);

        // Nothing entered the aggregate, the sibling enrolment survived, and the company is still saveable.
        Assert.Null(EnrolmentFor(vm.Company!, fieldLabel));
        AssertTheSiblingSurvived(vm.Company!, fieldLabel);
        Assert.Null(Record.Exception(() => _storage.Save(vm.Company!)));
    }

    // ================================================================ (T7) the loader must not truncate

    /// <summary>
    /// <b>A stored fractional figure is displayed IN FULL and refused out loud — never truncated and silently
    /// re-saved.</b> This is the half of the defect the parser alone does not fix.
    ///
    /// <para>The three loaders rendered the stored amount through a <c>(long)</c> cast, which TRUNCATES toward
    /// zero. No UI path can produce a fractional figure once the parser rejects one, but the paisa store carries
    /// ₹7,000.55 perfectly well, and import / a pre-fix book can put it there. With the truncating cast still in
    /// place the tightened parser makes things WORSE, not better: the page would show "7000", the parser would
    /// accept "7000" as a good whole rupee, and the accept would quietly write ₹7,000 over ₹7,000.55 — the exact
    /// wrong-figure the parser was tightened to prevent, now with the parser's blessing.</para>
    ///
    /// <para>Rendering with <c>"0.##"</c> (the idiom this same method already uses for the bonus rate) is lossless
    /// for anything the INTEGER-paisa store can hold, so the user sees the real figure and the parser then refuses
    /// it with a message. Loud refusal over silent mutation.</para>
    ///
    /// <para>Step 4 drives <c>AcceptStatutoryConfig</c> itself — the Ctrl+A this whole narrative rests on. Without
    /// it the mechanism was asserted in prose and demonstrated nowhere, and the re-apply conditions at the top of
    /// that method (which fire on EVERY accept while a toggle is on, and which DISCARD the bool the Apply methods
    /// return) had no coverage at all.</para>
    /// </summary>
    [Fact]
    public void AStoredFractionalFigureIsShownInFullAndRefusedRatherThanSilentlyTruncated()
    {
        const string companyName = "Legacy Fractional Statutory Co";
        var vm = NewPayrollCompany(companyName);
        var page = StatutoryPage(vm);

        page.GratuityEnabled = true;
        page.GratuityCapText = "7000";
        Assert.True(page.ApplyGratuity());
        page.BonusEnabled = true;
        page.BonusCalculationCeilingText = "6999";
        page.BonusMinimumWageText = "5721";
        Assert.True(page.ApplyBonus());

        // Legacy / imported data: fractional but paisa-exact, written by a route no screen guard covers.
        vm.Company!.GratuityConfig!.CapAmount = new Money(7000.55m);
        vm.Company.BonusConfig!.CalculationCeiling = new Money(6999.57m);
        vm.Company.BonusConfig.MinimumWage = new Money(5721.25m);
        _storage.Save(vm.Company);   // the paisa store carries it happily — this really is a reachable stored shape

        var reloaded = Reload(companyName);
        Assert.Equal(7000.55m, reloaded.GratuityConfig!.CapAmount.Amount);
        Assert.Equal(6999.57m, reloaded.BonusConfig!.CalculationCeiling.Amount);
        Assert.Equal(5721.25m, reloaded.BonusConfig.MinimumWage.Amount);

        var reopened = PageOver(reloaded);

        // 1. The loader shows what is stored. The (long) cast rendered "7000" / "6999" / "5721".
        Assert.Equal("7000.55", reopened.GratuityCapText);
        Assert.Equal("6999.57", reopened.BonusCalculationCeilingText);
        Assert.Equal("5721.25", reopened.BonusMinimumWageText);

        // 2. …and the accept refuses it instead of writing the truncated figure back over it.
        Assert.False(reopened.ApplyGratuity());
        Assert.Contains("whole-rupee", reopened.GratuityMessage!, StringComparison.Ordinal);
        Assert.False(reopened.ApplyBonus());
        Assert.Contains("whole-rupee", reopened.BonusMessage!, StringComparison.Ordinal);

        // 3. The figures are untouched — no silent 55-paisa haircut on the establishment's own cap.
        Assert.Equal(7000.55m, reloaded.GratuityConfig!.CapAmount.Amount);
        Assert.Equal(6999.57m, reloaded.BonusConfig!.CalculationCeiling.Amount);
        Assert.Equal(5721.25m, reloaded.BonusConfig.MinimumWage.Amount);

        // 4. THE KEYBOARD ACCEPT — the gesture the narrative above names. Clear the two messages first so a pass
        //    proves AcceptStatutoryConfig produced them, not the direct calls in step 2.
        reopened.GratuityMessage = null;
        reopened.BonusMessage = null;
        reopened.AcceptStatutoryConfig();

        Assert.Equal(7000.55m, reloaded.GratuityConfig!.CapAmount.Amount);
        Assert.Equal(6999.57m, reloaded.BonusConfig!.CalculationCeiling.Amount);
        Assert.Equal(5721.25m, reloaded.BonusConfig.MinimumWage.Amount);
        Assert.Contains("whole-rupee", reopened.GratuityMessage!, StringComparison.Ordinal);
        Assert.Contains("whole-rupee", reopened.BonusMessage!, StringComparison.Ordinal);
    }

    // ================================================================ (T8) the defaults still render as before

    /// <summary>
    /// The lossless <c>"0.##"</c> rendering must not change the normal path: a never-enrolled company still shows
    /// the plain whole-rupee defaults, with no ".00" tail appearing in the three boxes.
    /// </summary>
    [Fact]
    public void ANeverEnrolledCompanyStillShowsPlainWholeRupeeDefaults()
    {
        var vm = NewPayrollCompany("Default Statutory Text Co");
        var page = StatutoryPage(vm);

        Assert.Equal("2000000", page.GratuityCapText);
        Assert.Equal("7000", page.BonusCalculationCeilingText);
        Assert.Equal("0", page.BonusMinimumWageText);
    }

    // ================================================================ (T9) an amount too big for INTEGER paisa

    /// <summary>
    /// <b>An amount past what INTEGER paisa can hold is refused with a message, not an unhandled crash.</b>
    ///
    /// <para>A 17-digit figure passes every other branch of the guard — it parses, it is non-negative, it is
    /// paisa-exact, and it is a whole rupee — and then ₹99,99,99,99,99,99,999.99 × 100 exceeds
    /// <c>long.MaxValue</c> paisa, so the store's <c>(long)</c> narrowing raises an <b>OverflowException</b>. That
    /// is an <c>ArithmeticException</c>, so the old <c>when (ex is InvalidOperationException or
    /// ArgumentException)</c> filter did not match: the exception escaped <c>ApplyGratuity</c> — and
    /// <c>AcceptStatutoryConfig</c>, and the Ctrl+A handler above it — completely unhandled, with the enrolment
    /// left in the aggregate because the restore lines sit inside that same unmatched catch. The magnitude branch
    /// closes it in the guard; the widened reportable-failure set closes it in the catch.</para>
    /// </summary>
    [Theory]
    [InlineData("the gratuity cap")]
    [InlineData("the calculation ceiling")]
    [InlineData("the minimum wage")]
    public void AnAmountTooLargeForIntegerPaisaIsRefusedInsteadOfEscapingAsAnOverflow(string fieldLabel)
    {
        var vm = NewPayrollCompany("Overflow Statutory Co " + fieldLabel);
        var page = StatutoryPage(vm);

        var applied = true;
        string? message = null;
        var escaped = Record.Exception(() =>
        {
            (applied, message) = ApplyField(page, fieldLabel, "99999999999999999");
        });

        Assert.Null(escaped);                 // pre-fix: OverflowException, straight out through the Ctrl+A handler
        Assert.False(applied);
        Assert.Contains(fieldLabel, message!, StringComparison.Ordinal);
        Assert.Null(EnrolmentFor(vm.Company!, fieldLabel));
        Assert.Null(Record.Exception(() => _storage.Save(vm.Company!)));
    }

    // ================================================================ (T10) the three messages, word for word

    /// <summary>
    /// <b>The pre-existing invalid/negative messages are byte-identical after the refactor.</b> Three inline
    /// <c>!TryParseWholeRupees(...) || x &lt; 0m</c> checks became one shared helper — the classic place to lose the
    /// negative test or reword a string — and nothing tested either. Dropping the negative test would not even
    /// crash: a negative cap falls through to <c>PayrollService.EnableGratuity</c>'s own throw and a negative
    /// ceiling to <c>BonusConfig</c>'s, both of which the catch reports, so the ONLY symptom is different wording.
    /// Hence <c>Assert.Equal</c>, not <c>Contains</c>.
    /// </summary>
    [Theory]
    [InlineData("the gratuity cap", "-1")]
    [InlineData("the gratuity cap", "abc")]
    [InlineData("the calculation ceiling", "-1")]
    [InlineData("the calculation ceiling", "abc")]
    [InlineData("the minimum wage", "-1")]
    [InlineData("the minimum wage", "abc")]
    public void ANegativeOrNonNumericAmountKeepsItsExistingMessageWordForWord(string fieldLabel, string bad)
    {
        var vm = NewPayrollCompany($"Invalid Statutory Co {fieldLabel} {bad}");
        var page = StatutoryPage(vm);

        var (applied, message) = ApplyField(page, fieldLabel, bad);

        Assert.False(applied);
        Assert.Equal(ExpectedInvalidMessage(fieldLabel), message);
        Assert.Null(EnrolmentFor(vm.Company!, fieldLabel));
        Assert.Null(Record.Exception(() => _storage.Save(vm.Company!)));
    }

    // ================================================================ (T11) a decimal comma is not a group separator

    /// <summary>
    /// <b>A decimal comma is refused, not silently stripped into a hundredfold figure.</b>
    ///
    /// <para><c>TryParseWholeRupees</c> removed EVERY comma with no positional check, so "7000,55" became "700055":
    /// the user's ₹7,000.55 was accepted with no message and stored as ₹7,00,055.00. That is a wrong figure
    /// manufactured by the very parser this slice hardened — a 100× error, larger than anything the sub-paisa case
    /// could produce. The group after the last comma is exactly three digits in every convention this app renders,
    /// so a shorter trailing group can only be a decimal comma.</para>
    /// </summary>
    [Theory]
    [InlineData("the gratuity cap", "7000,55")]
    [InlineData("the calculation ceiling", "6999,57")]
    [InlineData("the minimum wage", "5721,25")]
    public void ADecimalCommaIsRefusedRatherThanStrippedIntoAHundredfoldFigure(string fieldLabel, string decimalComma)
    {
        var vm = NewPayrollCompany("Decimal Comma Co " + fieldLabel);
        var page = StatutoryPage(vm);

        var (applied, message) = ApplyField(page, fieldLabel, decimalComma);

        Assert.False(applied);
        Assert.Equal(ExpectedInvalidMessage(fieldLabel), message);
        Assert.Null(EnrolmentFor(vm.Company!, fieldLabel));
    }

    /// <summary>
    /// …and the comma rule is not over-eager: a genuine Indian grouping still parses, and to the right figure.
    /// "19,99,999" must be ₹19,99,999 — not refused, and certainly not ₹1,999,999,00.
    /// </summary>
    [Fact]
    public void GenuineGroupingCommasAreStillAccepted()
    {
        var vm = NewPayrollCompany("Grouped Statutory Co");
        var page = StatutoryPage(vm);

        page.GratuityEnabled = true;
        page.GratuityCapText = "19,99,999";
        Assert.True(page.ApplyGratuity());
        Assert.Equal(1_999_999m, vm.Company!.GratuityConfig!.CapAmount.Amount);

        page.BonusEnabled = true;
        page.BonusCalculationCeilingText = "6,999";
        page.BonusMinimumWageText = "5,721";
        Assert.True(page.ApplyBonus());
        Assert.Equal(6_999m, vm.Company.BonusConfig!.CalculationCeiling.Amount);
        Assert.Equal(5_721m, vm.Company.BonusConfig.MinimumWage.Amount);
    }
}
