using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Desktop.Services;
using Apex.Desktop.ViewModels;
using Xunit;

namespace Apex.Desktop.Tests.Fixtures;

/// <summary>
/// 🔴 THE FIXTURE'S OWN COVERAGE LOCK — the thing whose ABSENCE made whole sweeps of this project undecidable.
///
/// <para><b>Why this file exists.</b> <see cref="PopulatedCompanyFixture"/> is the project's standard realistic
/// book: six test classes open it through the real SQLite + company-select path and measure the shipped UI on it.
/// For most of its life it was described in three separate doc comments as carrying "51 vouchers" of "every
/// type". It carried 51 accounting vouchers of <b>8</b> base kinds and <b>zero</b> inventory, order, provisional,
/// job-work, POS or payroll vouchers — so every report surface fed by those families rendered EMPTY, and
/// "no defect observed" on them proved nothing whatsoever. A previous UI sweep against a thin seed produced 256
/// CANNOT-TELL rows for exactly this reason.</para>
///
/// <para><b>What these tests lock, and why each one is here.</b> A fixture is only worth what a future edit
/// cannot silently take out of it. So the coverage is asserted <b>as data</b>, not as a comment:</para>
/// <list type="bullet">
///   <item>every base kind the company's own <see cref="VoucherType"/> seed defines must carry a posted
///     voucher — derived from <see cref="Company.VoucherTypes"/> rather than from a hard-coded list, so a new
///     seeded type fails this test on the day it is added rather than quietly widening the blind spot;</item>
///   <item>the two families that are NOT ordinary voucher types — the POS tender split (a Sales type flagged
///     <see cref="VoucherType.UseForPos"/>) and Attendance (stored as <see cref="AttendanceEntry"/> rows, never
///     as a posted voucher) — are asserted separately, because a base-kind sweep cannot see either;</item>
///   <item><b>odd-valued discipline</b> (a rule this project earned the hard way: a 50-paisa defect survived six
///     round-number assertions). Every family added by W0-7 must carry at least one non-integral money amount or
///     non-integral quantity, so a later "tidy-up" to round numbers fails loudly;</item>
///   <item>the whole book must survive the <b>real SQLite round trip</b>. Every consuming test reaches the
///     fixture through <see cref="CompanyStorage"/>, so coverage that exists only in memory is coverage the UI
///     tests never see.</item>
/// </list>
/// </summary>
public sealed class PopulatedFixtureCoverageTests
{
    // ------------------------------------------------------------------ helpers

    /// <summary>Every base kind that carries at least one posted voucher (accounting OR stock/order).</summary>
    private static HashSet<VoucherBaseType> PostedBaseTypes(Company c)
    {
        var posted = new HashSet<VoucherBaseType>();
        foreach (var v in c.Vouchers)
            if (c.FindVoucherType(v.TypeId) is { } t) posted.Add(t.BaseType);
        foreach (var v in c.InventoryVouchers)
            if (c.FindVoucherType(v.TypeId) is { } t) posted.Add(t.BaseType);
        return posted;
    }

    /// <summary>The base kinds the company's own seed defines — the denominator, read from data not a literal.</summary>
    private static HashSet<VoucherBaseType> SeededBaseTypes(Company c) =>
        c.VoucherTypes.Select(t => t.BaseType).ToHashSet();

    private static string Census(Company c)
    {
        var posted = PostedBaseTypes(c);
        var seeded = SeededBaseTypes(c);
        return $"seeded base kinds={seeded.Count}; covered={posted.Count}; "
             + $"accounting vouchers={c.Vouchers.Count}; inventory vouchers={c.InventoryVouchers.Count}; "
             + $"attendance entries={c.AttendanceEntries.Count}\n"
             + "  MISSING: " + string.Join(", ", seeded.Except(posted).OrderBy(b => b.ToString()));
    }

    private static bool HasOddPaisa(Voucher v) =>
        v.Lines.Any(l => l.Amount.Amount != decimal.Truncate(l.Amount.Amount));

    private static bool HasOddQuantityOrRate(InventoryVoucher v) =>
        v.Allocations.Concat(v.DestinationAllocations).Any(
            a => a.Quantity != decimal.Truncate(a.Quantity)
              || (a.Rate is { } r && r.Amount != decimal.Truncate(r.Amount)))
        || v.OrderLines.Any(
            o => o.Quantity != decimal.Truncate(o.Quantity)
              || (o.Rate is { } r && r.Amount != decimal.Truncate(r.Amount)))
        || v.PhysicalLines.Any(p => p.CountedQuantity != decimal.Truncate(p.CountedQuantity))
        || (v.JobWorkOrder is { } jwo
            && (jwo.FinishedGoodQuantity != decimal.Truncate(jwo.FinishedGoodQuantity)
                || jwo.Lines.Any(l => l.Quantity != decimal.Truncate(l.Quantity))));

    // ------------------------------------------------------------------ A: the coverage itself

    /// <summary>
    /// A — 🔴 EVERY SEEDED BASE KIND IS EXERCISED. The denominator is the company's own voucher-type seed, so
    /// this cannot drift out of date: adding a seeded type without adding a specimen voucher fails here.
    /// </summary>
    [Fact]
    public void Regular_fixture_posts_a_voucher_of_every_seeded_base_kind()
    {
        var c = PopulatedCompanyFixture.BuildRegular();
        var missing = SeededBaseTypes(c).Except(PostedBaseTypes(c)).OrderBy(b => b.ToString()).ToList();

        Assert.True(
            missing.Count == 0,
            "The populated fixture posts no voucher of these seeded base kinds, so every report, print and "
            + "export surface fed by them renders EMPTY and any sweep over them is undecidable:\n  "
            + string.Join(", ", missing) + "\n" + Census(c));
    }

    /// <summary>
    /// B — the two families a base-kind sweep is blind to. A POS bill is an ordinary Sales base kind wearing a
    /// <see cref="VoucherType.UseForPos"/> flag, and Attendance posts no <see cref="Voucher"/> at all — so both
    /// would pass test A while being entirely absent.
    /// </summary>
    [Fact]
    public void Regular_fixture_carries_a_pos_bill_and_recorded_attendance()
    {
        var c = PopulatedCompanyFixture.BuildRegular();

        var posType = c.VoucherTypes.FirstOrDefault(t => t.IsPosSales);
        Assert.True(posType != null,
            "The fixture defines no POS-flagged Sales voucher type, so the POS Register, the retail receipt and "
            + "the tender-split posting path are all unreachable on it. " + Census(c));

        var posBills = c.Vouchers.Where(v => v.TypeId == posType!.Id && v.HasPosTenders).ToList();
        Assert.True(posBills.Count > 0,
            "A POS voucher type exists but no POS bill is posted against it, so the tender split — the only "
            + "thing that makes a POS sale different from an ordinary one — is never exercised. " + Census(c));
        Assert.True(posBills.Any(b => b.PosTenders.Count > 1),
            "Every POS bill is single-tender; the multi-tender split (gift + card + cheque + cash residual) is "
            + "the case the reconciliation invariant exists for. " + Census(c));

        Assert.True(c.AttendanceEntries.Count > 0,
            "The fixture records no attendance, so the Attendance Register and every On-Attendance / "
            + "On-Production pay head compute over an empty period. " + Census(c));
    }

    /// <summary>
    /// C — the payroll leg, which is the one family that posts through a computation engine rather than a
    /// hand-built voucher. A Payroll voucher with no self-describing payroll lines would leave the payslip, the
    /// pay sheet and the payroll register empty even though a voucher exists.
    /// </summary>
    [Fact]
    public void Regular_fixture_posts_a_payroll_voucher_carrying_per_employee_detail()
    {
        var c = PopulatedCompanyFixture.BuildRegular();
        var payrollType = c.VoucherTypes.Single(t => t.BaseType == VoucherBaseType.Payroll);
        var runs = c.Vouchers.Where(v => v.TypeId == payrollType.Id).ToList();

        Assert.True(runs.Count > 0, "No Payroll voucher is posted. " + Census(c));

        var employeesPaid = runs
            .SelectMany(v => v.Lines)
            .Select(l => l.Payroll?.EmployeeId)
            .Where(id => id is not null)
            .Distinct()
            .Count();
        // FLOOR PINNED AT THE WHOLE ROSTER, not a token `>= 4`. The fixture seeds 8 employees and pays all 8
        // deterministically, so anything less is a silent half-run — an off-by-one in RunPayroll, or a salary
        // structure that fails validation for one tax regime — and the payslip / pay-sheet / payroll-register
        // surfaces would then be measured on half the book. `>= 4` could not tell those apart.
        Assert.True(employeesPaid == c.Employees.Count,
            $"The payroll run carries per-employee detail for {employeesPaid} of the {c.Employees.Count} "
            + "employees the fixture seeds; a partial run means the payslip / pay-sheet / payroll-register "
            + "surfaces are measured on a book smaller than the one intended. " + Census(c));
    }

    // ------------------------------------------------------------------ C2: the voucher types are REACHABLE

    /// <summary>
    /// C2 — 🔴 THE TYPES ARE ACTIVE, NOT MERELY SEEDED. W0-7 replaced the raw
    /// <c>c.EnableJobOrderProcessing = true</c> flag with the real <c>JobWorkService.SetEnabled(true)</c>,
    /// because the raw flag turns the MENU rows on while leaving the four Job-Work voucher types
    /// <see cref="VoucherType.IsActive"/> = <c>false</c> and unstamped — a fixture claiming a feature whose
    /// voucher types no screen lists and no Alt+A picker offers.
    ///
    /// <para><b>Exactly how much of that was unlocked — measured, because a review overstated it.</b> The claim
    /// was that reverting the line leaves the whole suite green. It does not: reverting it makes
    /// <c>BuildRegular()</c> <b>throw</b> — without <c>AllowConsumption</c> on the Material In type the
    /// consuming job-work return stops being balance-exempt and <c>EnsureStockJournalBalances</c> rejects it
    /// ("source total 306.500 and destination total 2400 do not balance"), so every test touching the fixture
    /// fails loudly. The <c>UseForJobWork</c> / <c>AllowConsumption</c> half was therefore already locked.</para>
    ///
    /// <para><b>The <c>IsActive</c> half was NOT.</b> <c>TypeFor</c> does not filter on it and
    /// <c>InventoryPostingService.Post</c> does not require it, so a fixture that stamps the two flags but
    /// leaves the four types inactive builds and posts perfectly — <b>measured: 67 of the 68 tests in this file
    /// and <see cref="ReportEmptyStateShapeTests"/> stayed green, and only this test went red.</b> That is the
    /// gap this closes. <b>Measured</b> on the pre-W0-7 fixture: 23 types, <b>18</b> active — the four Job-Work
    /// types and Payroll inactive. Measured now: 24 types, <b>23</b> active, Payroll alone inactive.</para>
    ///
    /// <para>The Payroll exception is DELIBERATE and is pinned here rather than only asserted in prose: the seed
    /// ships it inactive and nothing in the product can activate it (census T1-4 — there is no Voucher Type
    /// master, and <c>PayrollService.EnablePayroll</c> does not flip the flag), so activating it in the fixture
    /// would let these tests assert a reachability the shipped app does not have.</para>
    /// </summary>
    [Fact]
    public void Every_seeded_voucher_type_is_active_except_the_deliberately_inactive_payroll()
    {
        var c = PopulatedCompanyFixture.BuildRegular();

        var inactive = c.VoucherTypes.Where(t => !t.IsActive).Select(t => t.BaseType).ToList();
        Assert.True(
            inactive.Count == 1 && inactive[0] == VoucherBaseType.Payroll,
            "Exactly one seeded voucher type may be inactive — Payroll, which the product has no way to "
            + "activate (census T1-4). These are inactive instead, so the fixture claims features whose "
            + "voucher types no screen lists and no Alt+A picker offers:\n  "
            + string.Join(", ", inactive.Select(b => b.ToString())) + "\n" + Census(c));

        // The four Job-Work types must also carry the stamps the real F11 toggle applies — IsActive alone would
        // still leave Material In unable to consume, which is what makes a job-work transform legal.
        foreach (var bt in new[]
        {
            VoucherBaseType.JobWorkInOrder, VoucherBaseType.JobWorkOutOrder,
            VoucherBaseType.MaterialIn, VoucherBaseType.MaterialOut,
        })
        {
            var t = c.VoucherTypes.Single(v => v.BaseType == bt);
            Assert.True(t.IsActive, $"{bt} is inactive — job-order processing was switched on by assigning the "
                + "raw Company.EnableJobOrderProcessing flag instead of driving JobWorkService.SetEnabled.");
        }

        foreach (var bt in new[] { VoucherBaseType.MaterialIn, VoucherBaseType.MaterialOut })
            Assert.True(c.VoucherTypes.Single(v => v.BaseType == bt).UseForJobWork,
                $"{bt} is not stamped UseForJobWork, so it is not the job-work movement type the registers and "
                + "the order-fulfilment tracking read.");

        Assert.True(c.VoucherTypes.Single(v => v.BaseType == VoucherBaseType.MaterialIn).AllowConsumption,
            "Material In is not stamped AllowConsumption, so the consuming (transform) Material In — the whole "
            + "point of a job-work return — is not the shape the fixture claims to model.");
    }

    // ------------------------------------------------------------------ C3: no voucher lands on a specialised type

    /// <summary>
    /// C3 — 🔴 TYPE RESOLUTION IS ORDER-INDEPENDENT. The POS bill gives this fixture its only case of TWO types
    /// sharing one base kind, and <c>TypeFor</c> exists because a bare <c>First(t =&gt; t.BaseType == Sales)</c>
    /// starts returning the POS till the moment that second type exists.
    ///
    /// <para><b>⚠️ What is NOT true.</b> A review claimed the bare form was correct only because
    /// <c>BuildRegular</c> calls <c>PostVouchers</c> before <c>PostPosBill</c>, and that reordering those two
    /// lines would put all ten item-invoice sales on the POS till. Measured: it does not.
    /// <c>Company.AddVoucherType</c> is a plain <c>List.Add</c> and the predefined types are seeded first, so
    /// the POS type is always LAST and <c>First</c> keeps returning the predefined Sales row at any call order.
    /// The bare form is safe today; it is merely implicit, resting on an append order nothing asserts.</para>
    ///
    /// <para><b>What this DOES pin</b> is the invariant itself, independent of how resolution is written:
    /// exactly one voucher in the whole book may sit on a specialised type. Measured red — drop <c>TypeFor</c>'s
    /// <c>IsPredefined</c> preference and run <c>PostPosBill</c> first, and this names all eleven offenders,
    /// while <b>every other assertion in this file stays green</b>, because the base kind is unchanged and the
    /// coverage census reads base kinds. Those ten sales would carry the POS numbering series and
    /// <c>UseForPos</c> flag, which <c>VoucherValidator</c> and the print path both branch on.</para>
    /// </summary>
    [Fact]
    public void Only_the_pos_bill_is_posted_on_a_specialised_voucher_type()
    {
        var c = PopulatedCompanyFixture.BuildRegular();

        var specialised = c.Vouchers
            .Select(v => (Voucher: v, Type: c.FindVoucherType(v.TypeId)!))
            .Where(x => !x.Type.IsPredefined)
            .ToList();

        Assert.True(
            specialised.Count == 1,
            $"{specialised.Count} voucher(s) are posted on a NON-predefined (specialised) voucher type; exactly "
            + "one may be — the POS retail bill. Anything else means a bare "
            + "First(t => t.BaseType == x) picked up a specialised variant, which changes the numbering series "
            + "and the printed document while leaving the base kind (and therefore every coverage assertion "
            + "here) unchanged:\n  "
            + string.Join("\n  ", specialised.Select(x => $"{x.Type.Name} on {x.Voucher.Date:dd-MMM-yyyy}")));

        Assert.True(specialised[0].Type.IsPosSales && specialised[0].Voucher.HasPosTenders,
            $"The one specialised-type voucher is '{specialised[0].Type.Name}', which is not the POS bill.");

        Assert.True(
            c.InventoryVouchers.All(v => c.FindVoucherType(v.TypeId)!.IsPredefined),
            "A stock/order voucher is posted on a specialised voucher type. Every one of them must resolve "
            + "through TypeFor, which prefers the predefined seeded row exactly as VoucherTypeResolver does.");
    }

    // ------------------------------------------------------------------ D: odd-valued discipline

    /// <summary>
    /// D — ODD VALUES, LOCKED. Round numbers assert nothing: a 50-paisa defect survived this project's entire
    /// life under six round-number assertions. Each stock/order family added by W0-7 must keep at least one
    /// non-integral quantity or rate, and the provisional accounting families at least one non-integral amount.
    ///
    /// <para>⚠️ <b>The two families a base-kind sweep is blind to are covered too</b>, because they are exactly
    /// the ones the loops below cannot reach: the <b>POS bill</b> is an accounting <c>Voucher</c> (so it is not
    /// in <c>InventoryVouchers</c>) whose base kind is Sales (so it is not in the Memorandum / Reversing-Journal
    /// arm), and the <b>payroll</b> leg posts one Payroll voucher plus <see cref="AttendanceEntry"/> rows that no
    /// voucher loop sees at all. Without these arms, rounding the POS counter rate to ₹248 or the eight seeded
    /// basics to whole rupees left the whole suite green — and the paisa-rounding, tender-reconciliation and
    /// per-day pro-rata paths those figures exist to expose became untestable on the shared fixture.</para>
    /// </summary>
    [Fact]
    public void Every_added_family_carries_at_least_one_odd_valued_figure()
    {
        var c = PopulatedCompanyFixture.BuildRegular();
        var byType = c.InventoryVouchers.GroupBy(v => c.FindVoucherType(v.TypeId)!.BaseType)
            .ToDictionary(g => g.Key, g => g.ToList());

        var flat = new List<string>();
        foreach (var bt in new[]
        {
            VoucherBaseType.PurchaseOrder, VoucherBaseType.SalesOrder, VoucherBaseType.ReceiptNote,
            VoucherBaseType.DeliveryNote, VoucherBaseType.RejectionIn, VoucherBaseType.RejectionOut,
            VoucherBaseType.StockJournal, VoucherBaseType.PhysicalStock, VoucherBaseType.JobWorkInOrder,
            VoucherBaseType.JobWorkOutOrder, VoucherBaseType.MaterialIn, VoucherBaseType.MaterialOut,
        })
        {
            if (!byType.TryGetValue(bt, out var vouchers)) { flat.Add($"{bt} (absent)"); continue; }
            if (!vouchers.Any(HasOddQuantityOrRate)) flat.Add($"{bt} (all round)");
        }

        foreach (var bt in new[] { VoucherBaseType.Memorandum, VoucherBaseType.ReversingJournal })
        {
            var vouchers = c.Vouchers
                .Where(v => c.FindVoucherType(v.TypeId)!.BaseType == bt).ToList();
            if (vouchers.Count == 0) { flat.Add($"{bt} (absent)"); continue; }
            if (!vouchers.Any(HasOddPaisa)) flat.Add($"{bt} (all round)");
        }

        // ---- POS: an accounting voucher, so neither loop above reaches it.
        //
        // ⚠️ ASSERT THE HAND-TYPED FIGURE, NOT THE DERIVED ONES. `HasOddPaisa(bill)` looks like the right check
        // and is worthless here: 18% of ANY whole-rupee taxable value lands on paisa, so the posted CGST/SGST
        // lines satisfy it whatever the counter rate is — measured, rounding the rate to ₹248 left that form of
        // the assertion green. The rate is what a "tidy-up" actually rounds, so the rate is what is pinned; the
        // cash residual is pinned separately because it is what the tender reconciliation produces.
        var posBills = c.Vouchers.Where(v => v.HasPosTenders).ToList();
        if (posBills.Count == 0) flat.Add("POS bill (absent)");
        else
        {
            if (!posBills.Any(b => b.InventoryLines.Any(
                    l => l.Rate is { } r && r.Amount != decimal.Truncate(r.Amount))))
                flat.Add("POS bill counter rate (whole rupee — the paisa path through tax and the cash residual "
                         + "is no longer driven from an odd base)");

            if (!posBills.Any(b => b.PosTenders.Any(
                    t => t.Amount.Amount != decimal.Truncate(t.Amount.Amount))))
                flat.Add("POS tenders (all round — the cash RESIDUAL the reconciliation computes carries no "
                         + "paisa, so a tender-split rounding defect is invisible)");
        }

        // ---- Payroll: the seeded SALARY STRUCTURE is the falsifiable half. The posted voucher's amounts are
        // largely DERIVED (HRA 40%, EPF 12%, ESI 0.75%), so they carry paisa almost whatever the inputs are —
        // asserting only on them would be a lock that cannot fail. Assert on what a "tidy-up" would actually
        // round: the hand-seeded structure amounts.
        var structureAmounts = c.SalaryStructures
            .SelectMany(s => s.Lines)
            .Select(l => l.Amount)
            .Where(a => a is not null)
            .Select(a => a!.Value.Amount)
            .ToList();
        if (structureAmounts.Count == 0) flat.Add("Salary structures (absent)");
        else if (!structureAmounts.Any(a => a != decimal.Truncate(a)))
            flat.Add("Salary structures (all round: every seeded basic / allowance / advance is a whole rupee)");

        // The posted payroll voucher: assert what a rounding tidy-up CAN destroy. "Some line carries paisa"
        // cannot fail (HRA is 40% of basic, EPF 12%, ESI 0.75% — those land on paisa from whole-rupee inputs;
        // measured, they stayed odd with every seeded figure rounded). What a collapse WOULD destroy is the
        // spread: eight members must produce eight DISTINCT net-payable figures, or the payslip / pay sheet is
        // eight identical rows and no per-employee pro-rata path is exercised.
        var payrollLines = c.Vouchers.SelectMany(v => v.Lines)
            .Select(l => l.Payroll).Where(p => p is not null).Select(p => p!).ToList();
        if (payrollLines.Count == 0) flat.Add("Payroll voucher detail (absent)");
        else
        {
            var netByEmployee = payrollLines
                .Where(p => p.Category == PayrollLineCategory.NetPayable)
                .GroupBy(p => p.EmployeeId)
                .Select(g => g.Sum(p => p.Amount.Amount))
                .ToList();
            if (netByEmployee.Distinct().Count() != netByEmployee.Count)
                flat.Add($"Payroll net pay ({netByEmployee.Count} member(s) but only "
                         + $"{netByEmployee.Distinct().Count()} distinct net figure(s) — the payslip and pay "
                         + "sheet are repeated rows, so no per-employee pro-rata path is exercised)");
        }

        // ---- Attendance: recorded as AttendanceEntry rows, never as a voucher. The fractional OVERTIME hours
        // are what the On-Production head pro-rates, so a whole-hour tidy-up hides every per-unit rounding path.
        if (c.AttendanceEntries.Count == 0) flat.Add("Attendance (absent)");
        else if (!c.AttendanceEntries.Any(a => a.Value != decimal.Truncate(a.Value)))
            flat.Add("Attendance (all round: no fractional overtime hour is recorded)");

        Assert.True(
            flat.Count == 0,
            "These families carry only whole-rupee / whole-unit figures, so a rounding, unit-conversion or "
            + "paisa-truncation defect in them is invisible to every test that reads this fixture:\n  "
            + string.Join("\n  ", flat));
    }

    // ------------------------------------------------------------------ E: it survives the store

    /// <summary>
    /// E — 🔴 THE ROUND TRIP, which is the only form of the fixture the UI tests ever see. All six consuming
    /// classes call <c>storage.Save(BuildRegular())</c> and then open the company back through the real
    /// company-select path, so a family that builds in memory but does not persist is a family those tests still
    /// cannot see. Asserts the same coverage AFTER a genuine SQLite save + load.
    /// </summary>
    [Fact]
    public void The_whole_book_survives_a_real_sqlite_round_trip()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ApexFixtureCoverage_" + Guid.NewGuid().ToString("N"));
        try
        {
            var storage = new CompanyStorage(dir);
            var original = PopulatedCompanyFixture.BuildRegular();
            storage.Save(original);

            var reloaded = storage.Load(
                storage.ListCompanies().Single(e => e.Name == PopulatedCompanyFixture.RegularCompanyName));

            Assert.Equal(original.Vouchers.Count, reloaded.Vouchers.Count);
            Assert.Equal(original.InventoryVouchers.Count, reloaded.InventoryVouchers.Count);
            Assert.Equal(original.AttendanceEntries.Count, reloaded.AttendanceEntries.Count);

            var missing = SeededBaseTypes(reloaded).Except(PostedBaseTypes(reloaded))
                .OrderBy(b => b.ToString()).ToList();
            Assert.True(missing.Count == 0,
                "These base kinds survive BuildRegular() but not the SQLite round trip, so the six UI test "
                + "classes — which all load through CompanyStorage — never see them:\n  "
                + string.Join(", ", missing) + "\n" + Census(reloaded));

            Assert.True(reloaded.VoucherTypes.Any(t => t.IsPosSales),
                "The POS-flagged Sales voucher type did not survive the round trip.");
            Assert.True(
                reloaded.Vouchers.Any(v => v.PosTenders.Count > 1),
                "The multi-tender POS split did not survive the round trip.");
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            try { if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true); } catch { /* best effort */ }
        }
    }

    // ------------------------------------------------------------------ F: the book is coherent

    /// <summary>
    /// F — 🔴 NO (item, godown, batch) KEY GOES NEGATIVE. This is the one assertion that stops the coverage
    /// above being bought with nonsense: it is trivial to "post a Delivery Note" by despatching stock that was
    /// never received, and the engine will accept it (negative stock is a detector, not a block, since v50). A
    /// fixture that goes short is not a realistic book — it is a book with an exception on it, and every report
    /// measured on it is measuring that exception.
    ///
    /// <para>The trap this specifically guards: a batched item's outward line that omits its batch label opens a
    /// SEPARATE <c>(item, godown, "")</c> key which has never held anything, so it goes negative by the full
    /// quantity while the item's total on-hand still looks healthy.</para>
    /// </summary>
    [Fact]
    public void The_book_never_drives_a_stock_key_negative()
    {
        var c = PopulatedCompanyFixture.BuildRegular();
        var shortfalls = new Apex.Ledger.Services.InventoryPostingService(c).DetectNegativeStock();

        Assert.True(
            shortfalls.Count == 0,
            $"{shortfalls.Count} (item, godown, batch) key(s) go negative on the fixture, so it models a book "
            + "with a stock exception rather than a working one — and every inventory report measured on it "
            + "measures that exception:\n  "
            + string.Join("\n  ", shortfalls.Select(s => s.Message)));
    }

    // ------------------------------------------------------------------ G: the reports the layout locks measure

    /// <summary>
    /// G — 🔴 THE POINT OF THE WHOLE SLICE, STATED AS AN ASSERTION. <see cref="InventoryReportScrollReachabilityTests"/>
    /// drives all eleven inventory report bodies at four window sizes on this fixture and asserts that nothing is
    /// stranded past a scroller that cannot scroll. <b>FIVE of those eleven carried no real content</b> before
    /// W0-7 — so <b>20</b> of its 44 cases were measuring an empty pane and could not have failed however broken
    /// the scroller was. The split matters, because it is what defeated the first two attempts at this guard
    /// (figures measured by building the pre-W0-7 fixture, not inferred):
    /// <list type="bullet">
    ///   <item><b>Zero rows</b> — Order Register, Receipt Note Register, Job Work In Order Book (12 cases).</item>
    ///   <item><b>One placeholder row</b> — Physical Stock Register, Age Analysis of Expiring Batches (8 cases).
    ///     A <c>Rows.Count &gt; 0</c> guard passes on these; the Physical Stock Register in particular never had
    ///     "no rows at all", it had exactly one placeholder.</item>
    /// </list>
    ///
    /// <para><b>And prose matching does not close it either.</b> This test previously detected an empty state by
    /// the prefix <c>"No "</c>. <c>BuildReorderStatus</c>'s default branch renders <b>"All items are above their
    /// reorder levels."</b> — measured: with the fixture's reorder levels removed, this case passed over a report
    /// containing that one sentence. Detection is therefore STRUCTURAL and shared with the scroll locks via
    /// <see cref="ReportContentGuard"/>: count rows that are neither <c>IsHeader</c> (every placeholder, every
    /// group header, the Opening line) nor <c>IsTotal</c> (Grand Total, Closing), against a MEASURED per-kind
    /// floor — so Stock Summary and Godown Summary can no longer be satisfied by their unconditional Grand Total
    /// alone. <see cref="ReportEmptyStateShapeTests"/> pins the invariant the structural test rests on.</para>
    /// </summary>
    [Theory]
    [InlineData(ReportKind.StockSummary)]
    [InlineData(ReportKind.GodownSummary)]
    [InlineData(ReportKind.StockItemMovement)]
    [InlineData(ReportKind.ReorderStatus)]
    [InlineData(ReportKind.PhysicalStockRegister)]
    [InlineData(ReportKind.OrderRegister)]
    [InlineData(ReportKind.ReceiptNoteRegister)]
    [InlineData(ReportKind.JobWorkInOrderBook)]
    [InlineData(ReportKind.Batchwise)]
    [InlineData(ReportKind.BatchAgeAnalysis)]
    [InlineData(ReportKind.PriceList)]
    // ---- the registers W0-7 gave content to that no layout lock drives yet, pinned here so they stay populated
    [InlineData(ReportKind.DeliveryNoteRegister)]
    [InlineData(ReportKind.RejectionRegister)]
    [InlineData(ReportKind.JobWorkOutOrderBook)]
    [InlineData(ReportKind.MaterialInRegister)]
    [InlineData(ReportKind.MaterialOutRegister)]
    [InlineData(ReportKind.MemorandumRegister)]
    [InlineData(ReportKind.ReversingJournalRegister)]
    [InlineData(ReportKind.PosRegister)]
    [InlineData(ReportKind.AttendanceRegister)]
    [InlineData(ReportKind.PayrollRegister)]
    public void Inventory_and_new_family_reports_render_real_rows_on_the_fixture(ReportKind kind)
    {
        var vm = new ReportsViewModel(PopulatedCompanyFixture.BuildRegular(), kind);

        // The payroll MATRIX reports (Attendance Register, Payroll Register, Pay Sheet) do not fill `Rows` at
        // all — they build a column/cell matrix into PayrollRows. Asserting `Rows` for them would fail on a
        // perfectly populated report, and asserting only `Rows` for the others would have hidden that they
        // populate a different collection entirely. Branch on the shape rather than guessing.
        if (kind is ReportKind.AttendanceRegister or ReportKind.PayrollRegister or ReportKind.PaySheet)
        {
            // Floor pinned at the seeded roster: 8 members + the totals row. `> 1` was satisfied by a single
            // member, which proves nothing about a pay sheet that must be measured across real name lengths.
            Assert.True(vm.PayrollRows.Count >= 9,
                $"{kind} renders {vm.PayrollRows.Count} payroll-matrix row(s) on the populated fixture, below "
                + "the 9 it seeds (8 members + totals), so it exercises nothing like the surface the layout "
                + "locks measure.");
            return;
        }

        ReportContentGuard.RequireRealRows(vm.Rows, kind, "the populated fixture (in memory)");
    }

    // ------------------------------------------------------------------ H: what the old doc comments claimed

    /// <summary>
    /// F — the claim itself, pinned. Three doc comments described this fixture as "51 vouchers" of "every type".
    /// The count is now materially larger and the "every type" half is finally true; this asserts the FLOOR so a
    /// future edit cannot quietly shrink the book back under the size the layout locks were measured against
    /// (Price List overflow, Stock Summary scroll reachability, the Day Book pane).
    /// </summary>
    [Fact]
    public void The_book_stays_large_enough_for_the_layout_locks_that_measure_it()
    {
        var c = PopulatedCompanyFixture.BuildRegular();

        Assert.True(c.Vouchers.Count >= 51,
            $"The fixture posts {c.Vouchers.Count} accounting vouchers, fewer than the 51 the Day-Book and "
            + "register layout locks were measured against. " + Census(c));
        Assert.True(c.InventoryVouchers.Count >= 12,
            $"The fixture posts {c.InventoryVouchers.Count} stock/order vouchers — one per stock/order base "
            + "kind is the minimum that makes the inventory registers non-empty. " + Census(c));
    }
}
