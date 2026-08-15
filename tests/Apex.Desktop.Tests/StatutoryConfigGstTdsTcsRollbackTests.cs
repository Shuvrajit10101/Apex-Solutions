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
/// <b>The last four writers on the F11 Statutory-Configuration screen — <c>Apply</c> (GST), <c>ApplyTds</c>,
/// <c>ApplyTcs</c> and the "Enable Job Order Processing" toggle — must roll the shared Company aggregate back on a
/// failed save, and must not swallow a failure they do not recognise.</b>
///
/// <para><b>Why this file exists.</b> The sibling slice widened the report-vs-crash decision into
/// <c>IsReportableSaveFailure</c> and gave <c>TrySave</c> an unconditional <c>restore</c>, then claimed these three
/// Apply methods "inherit" it. They did not. <c>Apply</c> reached <c>TrySave</c> on neither of its two saves, and
/// <c>ApplyTds</c> / <c>ApplyTcs</c> reached it only on their DISABLE branch — and even there without a restore.
/// Five inline <c>when (ex is InvalidOperationException or ArgumentException)</c> filters survived, so a
/// <c>SqliteException</c> (SQLITE_BUSY from a second instance holding the write lock, READONLY, FULL) on any of
/// those paths was an UNHANDLED CRASH, and every other failure left the aggregate mutated-but-unpersisted.</para>
///
/// <para><b>Three of these are wrong-FIGURES divergences, not stale toggles</b>, and that is why restoring a
/// reference is not enough:</para>
/// <list type="bullet">
///   <item><c>Apply</c> writes <c>HomeStateCode</c> IN PLACE on the live <c>GstConfig</c> before the store is
///     reached. The home state decides intra- vs inter-state supply — CGST+SGST versus IGST — on every invoice for
///     the rest of the session.</item>
///   <item><c>ApplyTds</c> calls <c>SyncSharedIdentityToTcs</c>, which rewrites the live <b>TcsConfig</b>'s TAN and
///     responsible person in place. A failed TDS enable left the 27EQ side filing under a TAN the book does not
///     hold — in a screen the user never touched. <c>ApplyTcs</c> is the mirror.</item>
///   <item>Both enables auto-create ledgers (the six GST tax ledgers + Round Off; TDS/TCS Payable) — or, when a
///     same-named ledger already exists, TAG it and MOVE it under Duties &amp; Taxes. Neither is undone by putting a
///     config reference back.</item>
/// </list>
///
/// <para>The Job-Order toggle is the one the sibling slice explicitly deferred: <c>JobWorkService.SetEnabled</c>
/// stamps <c>IsActive</c> / <c>UseForJobWork</c> / <c>AllowConsumption</c> on four voucher types as well as setting
/// the company flag, so one bool was never the mutation. W0-13 overturned that deferral and built the per-type
/// capture; the three tests at the end of the Job-Order section are what pin it.</para>
///
/// <para><b>THREE levers, because each pins something the others cannot.</b>
/// <see cref="MakeTheNextSaveFail"/> plants a sub-paisa opening balance ⇒ <c>InvalidOperationException</c>, which
/// the OLD narrow filter already matched — it pins the RESTORE, never the widening.
/// <see cref="MakeTheNextSaveFailUnreportably"/> plants a null cost centre ⇒ <c>NullReferenceException</c>,
/// non-reportable under both lists — it pins that the restore runs unconditionally and the unknown failure still
/// reaches the caller. <see cref="PlantReportableSaveFailure"/> plants the same cost centre twice ⇒ a
/// <c>SqliteException</c> (a <c>DbException</c>): reportable under the new list, OUTSIDE the old filter, and
/// therefore <b>the only lever that turns red if the widening is reverted</b>. Measured: narrowing
/// <c>SaveFailure.IsReportable</c> back to the shipped pair left all 22 of this file's original tests green.</para>
///
/// <para><b>Every refusal assertion carries a message discriminator.</b> <c>Apply</c> has three pre-<c>try</c>
/// refusal branches that produce byte-for-byte the state a "leaves nothing behind" assertion looks for, so
/// without one, a change that made the screen refuse EARLY — before the mutation the rollback exists to undo —
/// would keep every such assertion trivially green while the rollback rotted into dead code. Measured: widening
/// the <c>HomeState is null</c> gate so it fired on exactly the planted company left 22/22 green.</para>
/// </summary>
public sealed class StatutoryConfigGstTdsTcsRollbackTests : IDisposable
{
    private const string GstinMaharashtra = "27AAPFU0939F1ZV";  // state code 27
    private const string GstinKarnataka = "29AAGCB7383J1Z4";    // state code 29
    private const string ValidTan = "MUMA12345B";
    private const string OtherTan = "DELA98765C";

    private static readonly VoucherBaseType[] JobWorkBaseTypes =
    {
        VoucherBaseType.JobWorkInOrder, VoucherBaseType.MaterialIn,
        VoucherBaseType.JobWorkOutOrder, VoucherBaseType.MaterialOut,
    };

    private readonly string _tempDir;
    private readonly CompanyStorage _storage;

    public StatutoryConfigGstTdsTcsRollbackTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "ApexGstTdsTcsRollback_" + Guid.NewGuid().ToString("N"));
        _storage = new CompanyStorage(_tempDir);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true); }
        catch (IOException) { /* a held SQLite handle must not fail the test */ }
    }

    // ---------------------------------------------------------------- harness

    private MainWindowViewModel NewSeededCompany(string name)
    {
        var vm = new MainWindowViewModel(_storage);
        vm.NewCompanyName = name;
        vm.CreateCompany();
        return vm;
    }

    private GstConfigViewModel StatutoryPage(MainWindowViewModel vm)
    {
        vm.ShowGstConfig();
        return vm.GstConfig!;
    }

    private Company Reload(string companyName) =>
        _storage.Load(_storage.ListCompanies().Single(e => e.Name == companyName));

    /// <summary>
    /// Makes the next save fail for a reason unrelated to any statutory field, with an exception this screen DOES
    /// report (the paisa store's <see cref="InvalidOperationException"/>). Ledgers are written in
    /// <c>InsertLedgers</c>, which runs after <c>InsertCompany</c>, so the statutory columns are written
    /// successfully first and the throw genuinely lands on the rollback path.
    /// </summary>
    private static void MakeTheNextSaveFail(Company company) =>
        company.Ledgers.First().OpeningBalance = new Money(10.005m);

    /// <summary>
    /// Makes the next save fail with an exception <c>IsReportableSaveFailure</c> does NOT recognise — the stand-in
    /// for any failure this screen has not anticipated, and the only way to tell "restore, then rethrow" apart from
    /// "restore, then report".
    ///
    /// <para>A null entry in the <b>cost-centre</b> collection reaches <c>InsertCostCentres</c> and dereferences to a
    /// <see cref="NullReferenceException"/>. Cost centres are deliberate: <c>InsertCostCentres</c> runs after
    /// <c>InsertCompany</c> / <c>InsertLedgers</c>, so the statutory columns are written successfully first and the
    /// throw genuinely lands on the rollback path — and nothing on this screen (nor the rollback capture itself)
    /// walks the cost-centre list, so the lever cannot perturb the code under test. A null <i>ledger</i> would: the
    /// GST/TDS enable paths call <c>FindLedgerByName</c>, and the rollback snapshots every ledger.</para>
    /// </summary>
    private static void MakeTheNextSaveFailUnreportably(Company company) => company.AddCostCentre(null!);

    /// <summary>
    /// Makes the next save fail with a <c>SqliteException</c> (PRIMARY KEY violation in <c>InsertCostCentres</c>) —
    /// a <see cref="DbException"/>. It is on <c>SaveFailure.IsReportable</c>'s list but sits OUTSIDE the old narrow
    /// <c>InvalidOperationException or ArgumentException</c> filter these four paths shipped, so it is <b>the only
    /// lever that discriminates the widened list from the shipped one</b>: on the old code it is a crash, on the
    /// new one a message. The sub-paisa lever above cannot tell them apart, because the narrow filter already
    /// matched it.
    ///
    /// <para>Cost centres for the same reason as above: <c>InsertCostCentres</c> runs after <c>InsertCompany</c> /
    /// <c>InsertLedgers</c>, and nothing on this screen — nor the rollback capture — walks the cost-centre list.</para>
    /// </summary>
    private static void PlantReportableSaveFailure(Company company)
    {
        var category = company.CostCategories[0];
        var centre = new CostCentre(Guid.NewGuid(), "Duplicated Centre", category.Id);
        company.AddCostCentre(centre);
        company.AddCostCentre(centre);   // same primary key twice
    }

    /// <summary>
    /// Asserts the reported message is the PLANTED sub-paisa store failure and nothing else.
    ///
    /// <para><b>Why every <c>Assert.False(Apply())</c> in this file needs it.</b> <c>Apply</c> has three
    /// pre-<c>try</c> refusal branches that each do <c>Message = …; RevertToggle(); return false;</c> and produce
    /// byte-for-byte the state the "leaves nothing behind" assertions look for. Without a discriminator, a future
    /// change that made the screen refuse EARLY — before the mutation the rollback exists to undo — would keep
    /// every one of those assertions trivially green while the rollback itself rotted into dead code. The wording
    /// asserted is <c>PaisaConversion.ToPaisaExact</c>'s own, and the figure is the one
    /// <see cref="MakeTheNextSaveFail"/> plants, so the assertion names the planted cause rather than "a failure".</para>
    /// </summary>
    private static void AssertReportedThePlantedSubPaisaFailure(string? message)
    {
        Assert.NotNull(message);
        Assert.Contains("not paisa-exact", message);
        Assert.Contains("10.005", message);
    }

    /// <summary>Asserts the message is the planted UNIQUE-constraint violation — only it says "cost_centres".</summary>
    private static void AssertReportedTheDbFailure(string? message)
    {
        Assert.NotNull(message);
        Assert.Contains("cost_centres", message);
    }

    private static int GstTaxLedgerCount(Company c) => c.Ledgers.Count(l => l.GstClassification is not null);

    // ================================================================ GST — Apply()

    /// <summary>
    /// <b>THE RED PROOF for the widened filter.</b> On the pre-fix tree the <see cref="NullReferenceException"/>
    /// escaped the narrow <c>when</c> filter — an unhandled crash out of an F11 keystroke — AND the aggregate kept a
    /// <c>GstConfig</c> the transactionally rolled-back .db does not have. The rethrow is still the contract (a
    /// failure this screen does not recognise must never be swallowed); what changes is that the rollback now runs
    /// on the way out.
    /// </summary>
    [Fact]
    public void ANonReportableSaveFailureStillRollsTheGstEnrolmentOutOfTheAggregate()
    {
        var vm = NewSeededCompany("Gst Unreportable Rollback Co");
        var page = StatutoryPage(vm);

        MakeTheNextSaveFailUnreportably(vm.Company!);

        page.GstEnabled = true;
        page.Gstin = GstinMaharashtra;

        Assert.Throws<NullReferenceException>(() => page.Apply());

        Assert.Null(vm.Company!.Gst);
        Assert.False(vm.Company.GstEnabled);
    }

    [Fact]
    public void AFailedGstEnableTakesTheConfigBackOutOfTheAggregateAndRevertsTheToggle()
    {
        var vm = NewSeededCompany("Gst Enable Rollback Co");
        var page = StatutoryPage(vm);

        MakeTheNextSaveFail(vm.Company!);

        page.GstEnabled = true;
        page.Gstin = GstinMaharashtra;
        Assert.False(page.Apply());
        AssertReportedThePlantedSubPaisaFailure(page.Message);

        // EnableGst set Company.Gst before the store was reached; the rollback takes it back out, and only then can
        // RevertToggle — which reads that very field — turn the toggle off.
        Assert.Null(vm.Company!.Gst);
        Assert.False(page.GstEnabled);
    }

    /// <summary>
    /// The half a config-reference rollback misses: <c>EnableGst</c> auto-creates the six Output/Input tax ledgers
    /// and Round Off. Left behind, they would be persisted by the NEXT (successful, unrelated) save — a company that
    /// never enabled GST silently acquiring a GST ledger set.
    /// </summary>
    [Fact]
    public void AFailedGstEnableTakesTheAutoCreatedTaxLedgersBackOutToo()
    {
        var vm = NewSeededCompany("Gst Ledger Rollback Co");
        var page = StatutoryPage(vm);
        var ledgersBefore = vm.Company!.Ledgers.Count;
        Assert.Equal(0, GstTaxLedgerCount(vm.Company));

        MakeTheNextSaveFail(vm.Company);

        page.GstEnabled = true;
        page.Gstin = GstinMaharashtra;
        Assert.False(page.Apply());
        AssertReportedThePlantedSubPaisaFailure(page.Message);

        Assert.Equal(0, GstTaxLedgerCount(vm.Company));
        Assert.Null(vm.Company.FindLedgerByName("Round Off"));
        Assert.Equal(ledgersBefore, vm.Company.Ledgers.Count);
    }

    /// <summary>
    /// <b>The wrong-FIGURES one.</b> An already-registered company switches home state / GSTIN / periodicity; the
    /// save fails; the in-memory config must still name what the book holds, because <c>HomeStateCode</c> decides
    /// intra- vs inter-state supply — CGST+SGST versus IGST — on every invoice for the rest of the session.
    /// Restoring only <c>Company.Gst</c> would not do: the four writes mutate the SAME object the capture points at.
    /// </summary>
    [Fact]
    public void AFailedGstReconfigureLeavesTheHomeStateAndGstinTheBookActuallyHolds()
    {
        const string companyName = "Gst Reconfigure Rollback Co";
        var vm = NewSeededCompany(companyName);
        var page = StatutoryPage(vm);

        page.GstEnabled = true;
        page.Gstin = GstinMaharashtra;   // auto-fills home state 27
        Assert.True(page.Apply());
        Assert.Equal("27", vm.Company!.Gst!.HomeStateCode);

        MakeTheNextSaveFail(vm.Company);

        page.Gstin = GstinKarnataka;     // auto-fills home state 29
        page.Periodicity = page.Periodicities.Single(o => o.Value == GstReturnPeriodicity.Quarterly);
        // …and the other THREE fields Apply overwrites in place. Without these three assertions the
        // RegistrationType / CompositionSubType / CompositionOptInDate lines of RestoreGstFields are dead code a
        // full green suite cannot distinguish from absent — measured: neutralising them left all 2101 green.
        page.RegistrationType = page.RegistrationTypes.Single(o => o.Value == GstRegistrationType.Composition);
        page.SelectedCompositionSubType =
            page.CompositionSubTypes.Single(o => o.Value == CompositionSubType.Restaurant);
        page.CompositionOptInDateText = "2025-07-09";
        Assert.False(page.Apply());
        AssertReportedThePlantedSubPaisaFailure(page.Message);

        // Memory and disk agree — both still Maharashtra, the original GSTIN, monthly, Regular, no composition.
        Assert.Equal("27", vm.Company.Gst!.HomeStateCode);
        Assert.Equal(GstinMaharashtra, vm.Company.Gst.Gstin);
        Assert.Equal(GstReturnPeriodicity.Monthly, vm.Company.Gst.Periodicity);
        Assert.Equal(GstRegistrationType.Regular, vm.Company.Gst.RegistrationType);
        Assert.Null(vm.Company.Gst.CompositionSubType);
        Assert.Null(vm.Company.Gst.CompositionOptInDate);
        var onDisk = Reload(companyName);
        Assert.Equal("27", onDisk.Gst!.HomeStateCode);
        Assert.Equal(GstinMaharashtra, onDisk.Gst.Gstin);
        Assert.Equal(GstReturnPeriodicity.Monthly, onDisk.Gst.Periodicity);
        Assert.Equal(GstRegistrationType.Regular, onDisk.Gst.RegistrationType);
        Assert.Null(onDisk.Gst.CompositionSubType);
    }

    /// <summary>
    /// The <c>Enabled</c> field of <c>RestoreGstFields</c>, which only a DISABLED-then-RE-ENABLED company can
    /// exercise: <c>_company.Gst</c> is non-null but <c>Enabled</c> is false, <c>EnableGst</c> sets it true, and
    /// putting the config REFERENCE back leaves the flag true. The session would then compute with GST on over a
    /// book that has it off — a wrong-figures divergence, not a stale toggle.
    /// </summary>
    [Fact]
    public void AFailedGstReEnableLeavesTheEnrolmentOffTheWayTheBookHasIt()
    {
        const string companyName = "Gst Re Enable Rollback Co";
        var vm = NewSeededCompany(companyName);
        var page = StatutoryPage(vm);

        page.GstEnabled = true;
        page.Gstin = GstinMaharashtra;
        Assert.True(page.Apply());
        page.GstEnabled = false;
        Assert.True(page.Apply());
        Assert.NotNull(vm.Company!.Gst);          // the config object survives a disable…
        Assert.False(vm.Company.GstEnabled);      // …carrying Enabled = false

        MakeTheNextSaveFail(vm.Company);

        page.GstEnabled = true;
        Assert.False(page.Apply());
        AssertReportedThePlantedSubPaisaFailure(page.Message);

        Assert.False(vm.Company.Gst!.Enabled);
        Assert.False(vm.Company.GstEnabled);
        Assert.False(page.GstEnabled);
        Assert.False(Reload(companyName).GstEnabled);
    }

    /// <summary>
    /// <b>The GST half of the re-tag path</b> — the mirror of
    /// <see cref="AFailedTdsEnableLeavesAPreCreatedPayableLedgerWhereTheBookHasIt"/>, and the ONLY thing that pins
    /// <c>RestoreLedgers</c>'s <c>GstClassification</c> line. <c>GstService.EnsureTaxLedger</c> does not always
    /// ADD: when a ledger of that name already exists it TAGS it with a <c>LedgerGstClassification</c> in place.
    /// No GST test ever pre-created a same-named ledger, so that branch was never entered and the classification
    /// restore had nothing to undo — measured dead against the whole Desktop project before this test existed.
    ///
    /// <para><b>Classification only, not the group.</b> The GST path relocates a pre-created ledger only when its
    /// <c>GroupId</c> is <c>Guid.Empty</c> (<c>EnsureTaxLedger</c>), unlike <c>EnsurePayableLedger</c>, which
    /// relocates unconditionally. So a group assertion here would pass on a deleted restore and prove nothing;
    /// the discriminating assertion is that the tag is gone.</para>
    ///
    /// <para>A pre-created <b>"Round Off"</b> would prove nothing either: <c>EnsureRoundOffLedger</c> returns
    /// early when the name exists and never touches it.</para>
    /// </summary>
    [Fact]
    public void AFailedGstEnableLeavesAPreCreatedTaxLedgerUntaggedTheWayTheBookHasIt()
    {
        var vm = NewSeededCompany("Gst Pre Created Ledger Co");
        var creditors = vm.Company!.FindGroupByName("Sundry Creditors")!;
        var preCreated = new Apex.Ledger.Domain.Ledger(
            Guid.NewGuid(), "Output CGST", creditors.Id, Money.Zero, openingIsDebit: false);
        vm.Company.AddLedger(preCreated);
        var ledgersBefore = vm.Company.Ledgers.Count;
        var page = StatutoryPage(vm);

        MakeTheNextSaveFail(vm.Company);

        page.GstEnabled = true;
        page.Gstin = GstinMaharashtra;
        Assert.False(page.Apply());
        AssertReportedThePlantedSubPaisaFailure(page.Message);

        Assert.Null(preCreated.GstClassification);         // the tag EnsureTaxLedger stamped must be gone
        Assert.Equal(creditors.Id, preCreated.GroupId);
        Assert.Contains(preCreated, vm.Company.Ledgers);   // …and the rollback must not DELETE a pre-existing ledger
        Assert.Equal(ledgersBefore, vm.Company.Ledgers.Count);
        Assert.Equal(0, GstTaxLedgerCount(vm.Company));
    }

    /// <summary>
    /// The mirror-image divergence: Save is transactional, so a failed DISABLE leaves the .db still holding the
    /// enrolment — memory must not silently lose it, or the session computes with GST off over a book that has it on.
    /// </summary>
    [Fact]
    public void AFailedGstDisableKeepsTheEnrolmentTheDatabaseStillHolds()
    {
        const string companyName = "Gst Disable Rollback Co";
        var vm = NewSeededCompany(companyName);
        var page = StatutoryPage(vm);

        page.GstEnabled = true;
        page.Gstin = GstinMaharashtra;
        Assert.True(page.Apply());

        MakeTheNextSaveFail(vm.Company!);

        page.GstEnabled = false;
        Assert.False(page.Apply());
        AssertReportedThePlantedSubPaisaFailure(page.Message);

        Assert.True(Reload(companyName).GstEnabled);   // the transaction rolled back — the .db still has it…
        Assert.True(vm.Company!.GstEnabled);           // …so memory must not have lost it
        Assert.True(page.GstEnabled);
    }

    /// <summary>
    /// The fifth narrow filter, on the DISABLE branch: <c>Apply</c> reached <c>TrySave</c> on neither of its two
    /// saves, so this path crashed on an unrecognised failure exactly like the enable one — and lost the enrolment
    /// the .db still holds on the way out.
    /// </summary>
    [Fact]
    public void ANonReportableSaveFailureOfAGstDisableKeepsTheEnrolmentAndRethrows()
    {
        const string companyName = "Gst Disable Unreportable Co";
        var vm = NewSeededCompany(companyName);
        var page = StatutoryPage(vm);

        page.GstEnabled = true;
        page.Gstin = GstinMaharashtra;
        Assert.True(page.Apply());

        MakeTheNextSaveFailUnreportably(vm.Company!);

        page.GstEnabled = false;
        Assert.Throws<NullReferenceException>(() => page.Apply());

        Assert.True(vm.Company!.GstEnabled);
        Assert.True(Reload(companyName).GstEnabled);
    }

    // ================================================================ TDS — ApplyTds()

    [Fact]
    public void ANonReportableSaveFailureStillRollsTheTdsEnrolmentOutOfTheAggregate()
    {
        var vm = NewSeededCompany("Tds Unreportable Rollback Co");
        var page = StatutoryPage(vm);

        MakeTheNextSaveFailUnreportably(vm.Company!);

        page.TdsEnabled = true;
        page.Tan = ValidTan;

        Assert.Throws<NullReferenceException>(() => page.ApplyTds());

        Assert.Null(vm.Company!.Tds);
        Assert.False(vm.Company.TdsEnabled);
    }

    [Fact]
    public void AFailedTdsEnableTakesTheConfigBackOutOfTheAggregateAndRevertsTheToggle()
    {
        var vm = NewSeededCompany("Tds Enable Rollback Co");
        var page = StatutoryPage(vm);

        MakeTheNextSaveFail(vm.Company!);

        page.TdsEnabled = true;
        page.Tan = ValidTan;
        Assert.False(page.ApplyTds());
        AssertReportedThePlantedSubPaisaFailure(page.TdsMessage);

        Assert.Null(vm.Company!.Tds);
        Assert.False(page.TdsEnabled);
    }

    [Fact]
    public void AFailedTdsEnableTakesTheAutoCreatedPayableLedgerBackOutToo()
    {
        var vm = NewSeededCompany("Tds Ledger Rollback Co");
        var page = StatutoryPage(vm);
        var ledgersBefore = vm.Company!.Ledgers.Count;

        MakeTheNextSaveFail(vm.Company);

        page.TdsEnabled = true;
        page.Tan = ValidTan;
        Assert.False(page.ApplyTds());
        AssertReportedThePlantedSubPaisaFailure(page.TdsMessage);

        Assert.Null(vm.Company.FindLedgerByName("TDS Payable"));
        Assert.Equal(ledgersBefore, vm.Company.Ledgers.Count);
    }

    /// <summary>
    /// The other half of the ledger mutation, and the one a "remove what was added" rollback misses entirely:
    /// <c>EnsurePayableLedger</c> does not always ADD. When a ledger of that name already exists it TAGS it
    /// <c>TdsTcsClassification</c> and RELOCATES it under Duties &amp; Taxes unconditionally — so a failed save
    /// otherwise left the user's own "TDS Payable" creditor moved out of Sundry Creditors in memory while the book
    /// still has it there.
    /// </summary>
    [Fact]
    public void AFailedTdsEnableLeavesAPreCreatedPayableLedgerWhereTheBookHasIt()
    {
        var vm = NewSeededCompany("Tds Pre Created Ledger Co");
        var creditors = vm.Company!.FindGroupByName("Sundry Creditors")!;
        var preCreated = new Apex.Ledger.Domain.Ledger(
            Guid.NewGuid(), "TDS Payable", creditors.Id, Money.Zero, openingIsDebit: false);
        vm.Company.AddLedger(preCreated);
        var page = StatutoryPage(vm);

        MakeTheNextSaveFail(vm.Company);

        page.TdsEnabled = true;
        page.Tan = ValidTan;
        Assert.False(page.ApplyTds());
        AssertReportedThePlantedSubPaisaFailure(page.TdsMessage);

        Assert.Equal(creditors.Id, preCreated.GroupId);
        Assert.Null(preCreated.TdsTcsClassification);
        Assert.Contains(preCreated, vm.Company.Ledgers);   // …and the rollback must not DELETE a pre-existing ledger
    }

    /// <summary>
    /// <b>The silent cross-screen one.</b> Enabling TDS calls <c>SyncSharedIdentityToTcs</c>, which rewrites the
    /// live <c>TcsConfig</c>'s TAN and responsible person IN PLACE — a collector identity the user never touched on
    /// this keystroke. A failed save left the 27EQ side of the session filing under a TAN the book does not hold.
    /// </summary>
    [Fact]
    public void AFailedTdsEnableLeavesTheTcsCollectorIdentityTheBookActuallyHolds()
    {
        const string companyName = "Tds Sync Rollback Co";
        var vm = NewSeededCompany(companyName);
        var page = StatutoryPage(vm);

        page.TcsEnabled = true;
        page.Tan = ValidTan;
        Assert.True(page.ApplyTcs());
        Assert.Equal(ValidTan, vm.Company!.Tcs!.Tan);

        MakeTheNextSaveFail(vm.Company);

        page.Tan = OtherTan;
        page.TdsEnabled = true;
        Assert.False(page.ApplyTds());
        AssertReportedThePlantedSubPaisaFailure(page.TdsMessage);

        Assert.Equal(ValidTan, vm.Company.Tcs!.Tan);
        Assert.True(vm.Company.TcsEnabled);
        Assert.Equal(ValidTan, Reload(companyName).Tcs!.Tan);
    }

    /// <summary>
    /// The deductor's own in-place edit: an already-enrolled company changing its TAN. <c>WriteDeductorIdentity</c>
    /// writes into the SAME object the capture points at, so restoring the reference alone changes nothing.
    ///
    /// <para><b>ALL NINE captured fields are asserted, not the two that happened to be easy.</b> Seven of them —
    /// deductor type, PAN, designation, address, surcharge, cess and <c>Enabled</c> — were measured DEAD: their
    /// lines in <c>RestoreIdentity</c> could be neutralised with all 2101 Desktop tests still green. A rollback a
    /// full green suite cannot distinguish from absent is the shape this campaign already shipped twice.</para>
    /// </summary>
    [Fact]
    public void AFailedTdsReconfigureLeavesTheDeductorIdentityTheBookActuallyHolds()
    {
        const string companyName = "Tds Reconfigure Rollback Co";
        var vm = NewSeededCompany(companyName);
        var page = StatutoryPage(vm);

        page.TdsEnabled = true;
        page.Tan = ValidTan;
        page.ResponsiblePersonName = "R. Bright";
        page.ResponsiblePersonPan = "AAPFU0939F";
        page.ResponsiblePersonDesignation = "Finance Controller";
        page.ResponsiblePersonAddress = "14 Marine Lines, Mumbai 400020";
        page.DeductorType = page.DeductorTypes.Single(o => o.Value == DeductorType.Company);
        page.SurchargeApplicable = true;
        page.CessApplicable = true;
        Assert.True(page.ApplyTds(), page.TdsMessage);

        MakeTheNextSaveFail(vm.Company!);

        page.Tan = OtherTan;
        page.ResponsiblePersonName = "S. Robert";
        page.ResponsiblePersonPan = "AAGCB7383J";
        page.ResponsiblePersonDesignation = "Accounts Manager";
        page.ResponsiblePersonAddress = "9 Residency Road, Bengaluru 560025";
        page.DeductorType = page.DeductorTypes.Single(o => o.Value == DeductorType.Individual);
        page.SurchargeApplicable = false;
        page.CessApplicable = false;
        Assert.False(page.ApplyTds());
        AssertReportedThePlantedSubPaisaFailure(page.TdsMessage);

        var tds = vm.Company!.Tds!;
        Assert.Equal(ValidTan, tds.Tan);
        Assert.Equal("R. Bright", tds.ResponsiblePersonName);
        Assert.Equal("AAPFU0939F", tds.ResponsiblePersonPan);
        Assert.Equal("Finance Controller", tds.ResponsiblePersonDesignation);
        Assert.Equal("14 Marine Lines, Mumbai 400020", tds.ResponsiblePersonAddress);
        Assert.Equal(DeductorType.Company, tds.DeductorType);
        Assert.True(tds.SurchargeApplicable);
        Assert.True(tds.CessApplicable);

        var onDisk = Reload(companyName).Tds!;
        Assert.Equal(ValidTan, onDisk.Tan);
        Assert.Equal("R. Bright", onDisk.ResponsiblePersonName);
        Assert.Equal("AAPFU0939F", onDisk.ResponsiblePersonPan);
        Assert.Equal("Finance Controller", onDisk.ResponsiblePersonDesignation);
        Assert.Equal(DeductorType.Company, onDisk.DeductorType);
        Assert.True(onDisk.SurchargeApplicable);
        Assert.True(onDisk.CessApplicable);
    }

    /// <summary>
    /// The <c>Enabled</c> field of <c>RestoreIdentity(TdsConfig)</c> — only a DISABLED-then-RE-ENABLED company can
    /// exercise it, because on every other path the flag is already true. Putting the config REFERENCE back leaves
    /// <c>Enabled</c> true, so the session would file 26Q for a book that has TDS off.
    /// </summary>
    [Fact]
    public void AFailedTdsReEnableLeavesTheEnrolmentOffTheWayTheBookHasIt()
    {
        const string companyName = "Tds Re Enable Rollback Co";
        var vm = NewSeededCompany(companyName);
        var page = StatutoryPage(vm);

        page.TdsEnabled = true;
        page.Tan = ValidTan;
        Assert.True(page.ApplyTds(), page.TdsMessage);
        page.TdsEnabled = false;
        Assert.True(page.ApplyTds(), page.TdsMessage);
        Assert.NotNull(vm.Company!.Tds);
        Assert.False(vm.Company.TdsEnabled);

        MakeTheNextSaveFail(vm.Company);

        page.TdsEnabled = true;
        Assert.False(page.ApplyTds());
        AssertReportedThePlantedSubPaisaFailure(page.TdsMessage);

        Assert.False(vm.Company.Tds!.Enabled);
        Assert.False(vm.Company.TdsEnabled);
        Assert.False(page.TdsEnabled);
        Assert.False(Reload(companyName).TdsEnabled);
    }

    [Fact]
    public void AFailedTdsDisableKeepsTheEnrolmentTheDatabaseStillHolds()
    {
        const string companyName = "Tds Disable Rollback Co";
        var vm = NewSeededCompany(companyName);
        var page = StatutoryPage(vm);

        page.TdsEnabled = true;
        page.Tan = ValidTan;
        Assert.True(page.ApplyTds());

        MakeTheNextSaveFail(vm.Company!);

        page.TdsEnabled = false;
        Assert.False(page.ApplyTds());
        AssertReportedThePlantedSubPaisaFailure(page.TdsMessage);

        Assert.True(Reload(companyName).TdsEnabled);
        Assert.True(vm.Company!.TdsEnabled);
        Assert.True(page.TdsEnabled);
    }

    // ================================================================ TCS — ApplyTcs()

    [Fact]
    public void ANonReportableSaveFailureStillRollsTheTcsEnrolmentOutOfTheAggregate()
    {
        var vm = NewSeededCompany("Tcs Unreportable Rollback Co");
        var page = StatutoryPage(vm);

        MakeTheNextSaveFailUnreportably(vm.Company!);

        page.TcsEnabled = true;
        page.Tan = ValidTan;

        Assert.Throws<NullReferenceException>(() => page.ApplyTcs());

        Assert.Null(vm.Company!.Tcs);
        Assert.False(vm.Company.TcsEnabled);
    }

    [Fact]
    public void AFailedTcsEnableTakesTheConfigAndPayableLedgerBackOutOfTheAggregate()
    {
        var vm = NewSeededCompany("Tcs Enable Rollback Co");
        var page = StatutoryPage(vm);
        var ledgersBefore = vm.Company!.Ledgers.Count;

        MakeTheNextSaveFail(vm.Company);

        page.TcsEnabled = true;
        page.Tan = ValidTan;
        Assert.False(page.ApplyTcs());
        AssertReportedThePlantedSubPaisaFailure(page.TcsMessage);

        Assert.Null(vm.Company.Tcs);
        Assert.False(page.TcsEnabled);
        Assert.Null(vm.Company.FindLedgerByName("TCS Payable"));
        Assert.Equal(ledgersBefore, vm.Company.Ledgers.Count);
    }

    /// <summary>The mirror of the TDS cross-screen defect: enabling TCS rewrites the live deductor identity.</summary>
    [Fact]
    public void AFailedTcsEnableLeavesTheTdsDeductorIdentityTheBookActuallyHolds()
    {
        const string companyName = "Tcs Sync Rollback Co";
        var vm = NewSeededCompany(companyName);
        var page = StatutoryPage(vm);

        page.TdsEnabled = true;
        page.Tan = ValidTan;
        Assert.True(page.ApplyTds());

        MakeTheNextSaveFail(vm.Company!);

        page.Tan = OtherTan;
        page.TcsEnabled = true;
        Assert.False(page.ApplyTcs());
        AssertReportedThePlantedSubPaisaFailure(page.TcsMessage);

        Assert.Equal(ValidTan, vm.Company!.Tds!.Tan);
        Assert.True(vm.Company.TdsEnabled);
        Assert.Equal(ValidTan, Reload(companyName).Tds!.Tan);
    }

    /// <summary>The collector mirror, with all nine captured fields asserted — see the TDS note.</summary>
    [Fact]
    public void AFailedTcsReconfigureLeavesTheCollectorIdentityTheBookActuallyHolds()
    {
        const string companyName = "Tcs Reconfigure Rollback Co";
        var vm = NewSeededCompany(companyName);
        var page = StatutoryPage(vm);

        page.TcsEnabled = true;
        page.Tan = ValidTan;
        page.ResponsiblePersonName = "R. Bright";
        page.ResponsiblePersonPan = "AAPFU0939F";
        page.ResponsiblePersonDesignation = "Finance Controller";
        page.ResponsiblePersonAddress = "14 Marine Lines, Mumbai 400020";
        page.DeductorType = page.DeductorTypes.Single(o => o.Value == DeductorType.Company);
        page.SurchargeApplicable = true;
        page.CessApplicable = true;
        Assert.True(page.ApplyTcs(), page.TcsMessage);

        MakeTheNextSaveFail(vm.Company!);

        page.Tan = OtherTan;
        page.ResponsiblePersonName = "S. Robert";
        page.ResponsiblePersonPan = "AAGCB7383J";
        page.ResponsiblePersonDesignation = "Accounts Manager";
        page.ResponsiblePersonAddress = "9 Residency Road, Bengaluru 560025";
        page.DeductorType = page.DeductorTypes.Single(o => o.Value == DeductorType.Individual);
        page.SurchargeApplicable = false;
        page.CessApplicable = false;
        Assert.False(page.ApplyTcs());
        AssertReportedThePlantedSubPaisaFailure(page.TcsMessage);

        var tcs = vm.Company!.Tcs!;
        Assert.Equal(ValidTan, tcs.Tan);
        Assert.Equal("R. Bright", tcs.ResponsiblePersonName);
        Assert.Equal("AAPFU0939F", tcs.ResponsiblePersonPan);
        Assert.Equal("Finance Controller", tcs.ResponsiblePersonDesignation);
        Assert.Equal("14 Marine Lines, Mumbai 400020", tcs.ResponsiblePersonAddress);
        Assert.Equal(DeductorType.Company, tcs.CollectorType);
        Assert.True(tcs.SurchargeApplicable);
        Assert.True(tcs.CessApplicable);

        var onDisk = Reload(companyName).Tcs!;
        Assert.Equal(ValidTan, onDisk.Tan);
        Assert.Equal("AAPFU0939F", onDisk.ResponsiblePersonPan);
        Assert.Equal("Finance Controller", onDisk.ResponsiblePersonDesignation);
        Assert.Equal(DeductorType.Company, onDisk.CollectorType);
        Assert.True(onDisk.SurchargeApplicable);
        Assert.True(onDisk.CessApplicable);
    }

    /// <summary>The <c>Enabled</c> field of <c>RestoreIdentity(TcsConfig)</c> — see the TDS mirror.</summary>
    [Fact]
    public void AFailedTcsReEnableLeavesTheEnrolmentOffTheWayTheBookHasIt()
    {
        const string companyName = "Tcs Re Enable Rollback Co";
        var vm = NewSeededCompany(companyName);
        var page = StatutoryPage(vm);

        page.TcsEnabled = true;
        page.Tan = ValidTan;
        Assert.True(page.ApplyTcs(), page.TcsMessage);
        page.TcsEnabled = false;
        Assert.True(page.ApplyTcs(), page.TcsMessage);
        Assert.NotNull(vm.Company!.Tcs);
        Assert.False(vm.Company.TcsEnabled);

        MakeTheNextSaveFail(vm.Company);

        page.TcsEnabled = true;
        Assert.False(page.ApplyTcs());
        AssertReportedThePlantedSubPaisaFailure(page.TcsMessage);

        Assert.False(vm.Company.Tcs!.Enabled);
        Assert.False(vm.Company.TcsEnabled);
        Assert.False(page.TcsEnabled);
        Assert.False(Reload(companyName).TcsEnabled);
    }

    [Fact]
    public void AFailedTcsDisableKeepsTheEnrolmentTheDatabaseStillHolds()
    {
        const string companyName = "Tcs Disable Rollback Co";
        var vm = NewSeededCompany(companyName);
        var page = StatutoryPage(vm);

        page.TcsEnabled = true;
        page.Tan = ValidTan;
        Assert.True(page.ApplyTcs());

        MakeTheNextSaveFail(vm.Company!);

        page.TcsEnabled = false;
        Assert.False(page.ApplyTcs());
        AssertReportedThePlantedSubPaisaFailure(page.TcsMessage);

        Assert.True(Reload(companyName).TcsEnabled);
        Assert.True(vm.Company!.TcsEnabled);
        Assert.True(page.TcsEnabled);
    }

    // ================================================================ the Job-Order toggle

    [Fact]
    public void AFailedSaveOfTheJobOrderToggleLeavesNeitherTheCompanyFlagNorTheToggleAhead()
    {
        var vm = NewSeededCompany("Job Order Flag Rollback Co");
        var page = StatutoryPage(vm);
        Assert.False(vm.Company!.EnableJobOrderProcessing);

        MakeTheNextSaveFail(vm.Company);

        page.EnableJobOrderProcessing = true;   // the property setter itself saves, and the save fails

        AssertReportedThePlantedSubPaisaFailure(page.Message);
        Assert.False(vm.Company.EnableJobOrderProcessing);
        Assert.False(page.EnableJobOrderProcessing);
    }

    /// <summary>
    /// <b>The part one bool never covered.</b> <c>JobWorkService.SetEnabled</c> also activates the four seeded
    /// Job-Work voucher types and stamps <c>UseForJobWork</c> / <c>AllowConsumption</c> on the two Material types.
    ///
    /// <para><b>⚠️ The prior state here is deliberately NON-UNIFORM, and an all-off company would prove nothing.</b>
    /// On a reportable failure the catch re-syncs the toggle, which re-enters the handler once with <c>false</c> —
    /// and that pass runs <c>SetEnabled(false)</c>, which switches all four types off by itself. On an all-off
    /// company that accident lands on the right answer, so the assertion passes with the per-type restore deleted.
    /// One type left active beforehand (reachable from the Voucher Type master, or from an imported book) makes the
    /// two answers differ: the re-entry would switch it off, and only a real capture puts it back. This is exactly
    /// the case the source comment gives for capturing the per-type triple instead of re-calling
    /// <c>SetEnabled(previous)</c> — a uniform rewrite would destroy it the same way.</para>
    /// </summary>
    [Fact]
    public void AFailedSaveOfTheJobOrderToggleRestoresTheFourJobWorkVoucherTypeFlags()
    {
        var vm = NewSeededCompany("Job Order Types Rollback Co");
        var page = StatutoryPage(vm);
        var jobWork = vm.Company!.VoucherTypes.Where(t => JobWorkBaseTypes.Contains(t.BaseType)).ToList();
        Assert.Equal(4, jobWork.Count);
        Assert.All(jobWork, t => Assert.False(t.IsActive));

        var alreadyActive = jobWork.Single(t => t.BaseType == VoucherBaseType.JobWorkInOrder);
        alreadyActive.IsActive = true;

        MakeTheNextSaveFail(vm.Company);

        page.EnableJobOrderProcessing = true;

        AssertReportedThePlantedSubPaisaFailure(page.Message);
        Assert.True(alreadyActive.IsActive);   // the rollback must not switch off what the company already had on
        Assert.All(jobWork.Where(t => t != alreadyActive), t => Assert.False(t.IsActive));
        Assert.All(jobWork, t => Assert.False(t.UseForJobWork));
        Assert.All(jobWork, t => Assert.False(t.AllowConsumption));
    }

    /// <summary>
    /// The toggle's own narrow filter: a failure this screen does not recognise crashed out of the setter with the
    /// four voucher types left switched on. It still rethrows — after the rollback.
    /// </summary>
    [Fact]
    public void ANonReportableSaveFailureOfTheJobOrderToggleRethrowsAfterRestoring()
    {
        var vm = NewSeededCompany("Job Order Unreportable Co");
        var page = StatutoryPage(vm);
        var jobWork = vm.Company!.VoucherTypes.Where(t => JobWorkBaseTypes.Contains(t.BaseType)).ToList();

        MakeTheNextSaveFailUnreportably(vm.Company);

        Assert.Throws<NullReferenceException>(() => page.EnableJobOrderProcessing = true);

        Assert.False(vm.Company.EnableJobOrderProcessing);
        Assert.All(jobWork, t => Assert.False(t.IsActive));
    }

    // ================================================================ the WIDENED reportable list
    //
    // 🔴 These four are the only tests in this file that discriminate the widened SaveFailure.IsReportable set from
    // the narrow `when (ex is InvalidOperationException or ArgumentException)` filter these paths shipped. Both
    // levers above are matched by BOTH lists — the sub-paisa one is an InvalidOperationException the old filter
    // already caught, and the null-centre one is non-reportable under old and new alike — so re-narrowing the list
    // left every other test in this file green (measured: 22/22 passed with IsReportable narrowed back).
    // A SqliteException is a DbException: reportable now, an UNHANDLED CRASH out of an F11 keystroke before.
    // Record.Exception is used deliberately: `Assert.False(Apply())` alone cannot tell a message from an escape,
    // because an escaping exception fails the test for the wrong reason and a future refactor could mask it.

    [Fact]
    public void ADbFailureOfTheGstEnableIsAMessageAndNotACrash()
    {
        var vm = NewSeededCompany("Gst Db Failure Co");
        var page = StatutoryPage(vm);
        var ledgersBefore = vm.Company!.Ledgers.Count;

        PlantReportableSaveFailure(vm.Company);

        page.GstEnabled = true;
        page.Gstin = GstinMaharashtra;

        var escaped = Record.Exception(() => Assert.False(page.Apply()));
        Assert.Null(escaped);
        AssertReportedTheDbFailure(page.Message);

        Assert.Null(vm.Company.Gst);
        Assert.False(page.GstEnabled);
        Assert.Equal(0, GstTaxLedgerCount(vm.Company));
        Assert.Equal(ledgersBefore, vm.Company.Ledgers.Count);
    }

    [Fact]
    public void ADbFailureOfTheTdsEnableIsAMessageAndNotACrash()
    {
        var vm = NewSeededCompany("Tds Db Failure Co");
        var page = StatutoryPage(vm);
        var ledgersBefore = vm.Company!.Ledgers.Count;

        PlantReportableSaveFailure(vm.Company);

        page.TdsEnabled = true;
        page.Tan = ValidTan;

        var escaped = Record.Exception(() => Assert.False(page.ApplyTds()));
        Assert.Null(escaped);
        AssertReportedTheDbFailure(page.TdsMessage);

        Assert.Null(vm.Company.Tds);
        Assert.False(page.TdsEnabled);
        Assert.Null(vm.Company.FindLedgerByName("TDS Payable"));
        Assert.Equal(ledgersBefore, vm.Company.Ledgers.Count);
    }

    [Fact]
    public void ADbFailureOfTheTcsEnableIsAMessageAndNotACrash()
    {
        var vm = NewSeededCompany("Tcs Db Failure Co");
        var page = StatutoryPage(vm);
        var ledgersBefore = vm.Company!.Ledgers.Count;

        PlantReportableSaveFailure(vm.Company);

        page.TcsEnabled = true;
        page.Tan = ValidTan;

        var escaped = Record.Exception(() => Assert.False(page.ApplyTcs()));
        Assert.Null(escaped);
        AssertReportedTheDbFailure(page.TcsMessage);

        Assert.Null(vm.Company.Tcs);
        Assert.False(page.TcsEnabled);
        Assert.Null(vm.Company.FindLedgerByName("TCS Payable"));
        Assert.Equal(ledgersBefore, vm.Company.Ledgers.Count);
    }

    /// <summary>
    /// The fourth newly-widened path — the toggle handler, whose filter plan.md scoped as "widen only". The prior
    /// state is non-uniform for the reason the sibling test above spells out.
    /// </summary>
    [Fact]
    public void ADbFailureOfTheJobOrderToggleIsAMessageAndNotACrash()
    {
        var vm = NewSeededCompany("Job Order Db Failure Co");
        var page = StatutoryPage(vm);
        var jobWork = vm.Company!.VoucherTypes.Where(t => JobWorkBaseTypes.Contains(t.BaseType)).ToList();
        var alreadyActive = jobWork.Single(t => t.BaseType == VoucherBaseType.JobWorkInOrder);
        alreadyActive.IsActive = true;

        PlantReportableSaveFailure(vm.Company);

        var escaped = Record.Exception(() => page.EnableJobOrderProcessing = true);
        Assert.Null(escaped);
        AssertReportedTheDbFailure(page.Message);

        Assert.False(vm.Company.EnableJobOrderProcessing);
        Assert.False(page.EnableJobOrderProcessing);
        Assert.True(alreadyActive.IsActive);
        Assert.All(jobWork.Where(t => t != alreadyActive), t => Assert.False(t.IsActive));
        Assert.All(jobWork, t => Assert.False(t.UseForJobWork));
    }

    // ================================================================ one keystroke, three enables

    /// <summary>
    /// <c>AcceptStatutoryConfig</c> (Ctrl+A / Enter) runs GST, TDS and TCS in sequence. One accept against a company
    /// whose save will fail must leave the whole aggregate exactly where the (transactionally rolled-back) .db left
    /// it — not three enrolments and a GST ledger set in memory and none of it on disk.
    /// </summary>
    [Fact]
    public void OneFailedKeyboardAcceptLeavesNoGstTdsOrTcsEnrolmentBehindInMemory()
    {
        var vm = NewSeededCompany("Accept Gst Tds Tcs Rollback Co");
        var page = StatutoryPage(vm);
        var ledgersBefore = vm.Company!.Ledgers.Count;

        MakeTheNextSaveFail(vm.Company);

        page.GstEnabled = true;
        page.Gstin = GstinMaharashtra;
        page.TdsEnabled = true;
        page.TcsEnabled = true;
        page.Tan = ValidTan;

        page.AcceptStatutoryConfig();

        // All THREE must have actually reached the store and been refused there — without these the assertions
        // below pass identically on a screen that refused each one before it ever mutated anything.
        AssertReportedThePlantedSubPaisaFailure(page.Message);
        AssertReportedThePlantedSubPaisaFailure(page.TdsMessage);
        AssertReportedThePlantedSubPaisaFailure(page.TcsMessage);

        Assert.Null(vm.Company.Gst);
        Assert.Null(vm.Company.Tds);
        Assert.Null(vm.Company.Tcs);
        Assert.Equal(ledgersBefore, vm.Company.Ledgers.Count);
    }
}
