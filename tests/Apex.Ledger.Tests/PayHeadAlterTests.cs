using System.Reflection;
using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Services;
using Xunit;

namespace Apex.Ledger.Tests;

/// <summary>
/// 🔴 <b>CENSUS ROW 7.6 / DEFECT T2-38 — <c>PayHeadService.AlterPayHead</c>.</b>
///
/// <para><b>The defect.</b> The service shipped with <c>CreatePayHead</c>, <c>RenamePayHead</c>,
/// <c>SetComputation</c> and <c>DeletePayHead</c> and <b>no Alter</b>. A pay head whose <b>rate</b>, Under-group,
/// pay-head type, statutory tag, rounding or attendance link was mistyped could therefore never be corrected:
/// renaming does not touch the rate, <c>SetComputation</c> reaches only the formula, and delete-and-recreate is
/// refused the moment a salary structure references the head or another head computes on it — which is the normal
/// state of a configured book. The wrong figure then came off <i>every payslip thereafter</i>.</para>
///
/// <para><b>What this fixture is for, and what it deliberately is not.</b> Reach is proved in
/// <c>PayrollMasterAlterDeleteTests.Pay_head_alters_a_mistyped_rate_by_identity_and_deletes</c>, which drives
/// real keystrokes; a service call proves nothing about reach. This file proves the three ENGINE properties that
/// a keystroke test cannot see: every guard still bites on an alteration, a refused alteration leaves the
/// aggregate byte-identical, and an accepted one cannot restate a period already paid.</para>
/// </summary>
public sealed class PayHeadAlterTests
{
    private static Company Seed()
        => CompanyFactory.CreateSeeded("Pay Head Alter Co", new DateOnly(2025, 4, 1), new DateOnly(2025, 4, 1));

    private static Guid IndirectExpenses(Company c) => c.FindGroupByName("Indirect Expenses")!.Id;
    private static Guid CurrentLiabilities(Company c) => c.FindGroupByName("Current Liabilities")!.Id;

    private static (PayrollService Pay, PayHeadService PayHead) MinimalPayroll(Company c)
    {
        var pay = new PayrollService(c);
        pay.EnablePayroll();
        return (pay, new PayHeadService(c));
    }

    // ================================================================= the headline: a mistyped rate is corrected

    /// <summary>
    /// 🔴 <b>THE POINT OF THE SLICE.</b> A Professional Tax head typed at 12% where 1.2% was meant is corrected
    /// in place, keeping its identity — so every salary structure line and every historical reference that names
    /// it still names the same head. The rate is asserted in basis points off the STORED slab.
    /// </summary>
    [Fact]
    public void Alters_a_mistyped_computed_rate_in_place_keeping_the_identity()
    {
        var c = Seed();
        var (_, ph) = MinimalPayroll(c);
        var basic = ph.CreatePayHead("Basic", PayHeadType.Earnings, PayHeadCalculationType.FlatRate,
            underGroupId: IndirectExpenses(c));
        var pt = ph.CreatePayHead("Professional Tax", PayHeadType.EmployeesStatutoryDeductions,
            PayHeadCalculationType.AsComputedValue, underGroupId: CurrentLiabilities(c),
            computation: new PayHeadComputation(
                new[] { new PayHeadComputationComponent(basic.Id) },
                new[] { PayHeadComputationSlab.Percentage(1200) }));

        ph.AlterPayHead(pt.Id, PayHeadEdit.From(pt) with
        {
            Computation = new PayHeadComputation(
                new[] { new PayHeadComputationComponent(basic.Id) },
                new[] { PayHeadComputationSlab.Percentage(120) }),
        });

        var after = c.FindPayHead(pt.Id)!;
        Assert.Equal(120, after.Computation!.Slabs.Single().RateBasisPoints);
        Assert.Same(pt, after);                       // same instance: no fork
        Assert.Equal(2, c.PayHeads.Count);            // and no second head created
    }

    /// <summary>A head saved without renaming it is not refused by its own name — the uniqueness check has to
    /// exclude the head under alteration. Without this the screen could never accept a rate-only correction.</summary>
    [Fact]
    public void Saving_an_alteration_without_renaming_is_not_refused_by_its_own_name()
    {
        var c = Seed();
        var (_, ph) = MinimalPayroll(c);
        var head = ph.CreatePayHead("Basic", PayHeadType.Earnings, PayHeadCalculationType.FlatRate);

        ph.AlterPayHead(head.Id, PayHeadEdit.From(head) with { UnderGroupId = IndirectExpenses(c) });

        Assert.Equal(IndirectExpenses(c), c.FindPayHead(head.Id)!.UnderGroupId);
    }

    [Fact]
    public void An_alteration_onto_another_heads_name_is_refused()
    {
        var c = Seed();
        var (_, ph) = MinimalPayroll(c);
        var basic = ph.CreatePayHead("Basic", PayHeadType.Earnings, PayHeadCalculationType.FlatRate);
        var hra = ph.CreatePayHead("HRA", PayHeadType.Earnings, PayHeadCalculationType.FlatRate);

        Assert.Throws<InvalidOperationException>(() =>
            ph.AlterPayHead(hra.Id, PayHeadEdit.From(hra) with { Name = "basic" }));

        Assert.Equal("HRA", c.FindPayHead(hra.Id)!.Name);
        Assert.Equal("Basic", c.FindPayHead(basic.Id)!.Name);
    }

    [Fact]
    public void Altering_a_pay_head_that_does_not_exist_is_refused()
    {
        var c = Seed();
        var (_, ph) = MinimalPayroll(c);
        var real = ph.CreatePayHead("Basic", PayHeadType.Earnings, PayHeadCalculationType.FlatRate);
        Assert.Throws<InvalidOperationException>(() =>
            ph.AlterPayHead(Guid.NewGuid(), PayHeadEdit.From(real) with { Name = "Ghost" }));
        Assert.Equal("Basic", c.FindPayHead(real.Id)!.Name);   // and the real head was not touched on the way past
    }

    // ================================================================= every guard still bites on the alter path

    /// <summary>
    /// 🔴 <b>The adversarial one (ER-3).</b> A computed-on CYCLE introduced by an alteration must be rejected, and
    /// the walk has to see the head as the company holds it. A→B is fine; altering B to compute on A closes the
    /// cycle and must be refused — otherwise the payroll computation recurses on itself.
    /// </summary>
    [Fact]
    public void An_alteration_that_closes_a_computed_on_cycle_is_refused_and_rolled_back()
    {
        var c = Seed();
        var (_, ph) = MinimalPayroll(c);
        var a = ph.CreatePayHead("A", PayHeadType.Earnings, PayHeadCalculationType.FlatRate);
        var b = ph.CreatePayHead("B", PayHeadType.Earnings, PayHeadCalculationType.AsComputedValue,
            computation: new PayHeadComputation(
                new[] { new PayHeadComputationComponent(a.Id) },
                new[] { PayHeadComputationSlab.Percentage(1000) }));

        // A computed on B, while B is computed on A.
        Assert.Throws<InvalidOperationException>(() =>
            ph.AlterPayHead(a.Id, PayHeadEdit.From(a) with
            {
                CalculationType = PayHeadCalculationType.AsComputedValue,
                Computation = new PayHeadComputation(
                    new[] { new PayHeadComputationComponent(b.Id) },
                    new[] { PayHeadComputationSlab.Percentage(500) }),
            }));

        // Rolled back exactly: A is a Flat Rate head with no formula again.
        var after = c.FindPayHead(a.Id)!;
        Assert.Equal(PayHeadCalculationType.FlatRate, after.CalculationType);
        Assert.Null(after.Computation);
    }

    [Fact]
    public void An_alteration_that_makes_a_head_compute_on_itself_is_refused()
    {
        var c = Seed();
        var (_, ph) = MinimalPayroll(c);
        var basic = ph.CreatePayHead("Basic", PayHeadType.Earnings, PayHeadCalculationType.FlatRate);

        Assert.Throws<InvalidOperationException>(() =>
            ph.AlterPayHead(basic.Id, PayHeadEdit.From(basic) with
            {
                CalculationType = PayHeadCalculationType.AsComputedValue,
                Computation = new PayHeadComputation(
                    new[] { new PayHeadComputationComponent(basic.Id) },
                    new[] { PayHeadComputationSlab.Percentage(1000) }),
            }));

        Assert.Equal(PayHeadCalculationType.FlatRate, c.FindPayHead(basic.Id)!.CalculationType);
    }

    /// <summary>
    /// The statutory-role guard (F3) applies to the alter path too. A Professional-Tax component must post as an
    /// employee deduction; moving the head onto an Employer's-Statutory-Contributions type must be refused, or the
    /// head posts a phantom self-balancing pair on the wrong side of the P&amp;L.
    /// </summary>
    [Fact]
    public void An_alteration_that_moves_a_statutory_head_onto_the_wrong_side_is_refused()
    {
        var c = Seed();
        var (_, ph) = MinimalPayroll(c);
        var pt = ph.CreatePayHead("Professional Tax", PayHeadType.EmployeesStatutoryDeductions,
            PayHeadCalculationType.FlatRate, ptComponent: PtStatutoryComponent.ProfessionalTax);

        Assert.Throws<InvalidOperationException>(() =>
            ph.AlterPayHead(pt.Id, PayHeadEdit.From(pt) with
            {
                Type = PayHeadType.EmployersStatutoryContributions,
            }));

        Assert.Equal(PayHeadType.EmployeesStatutoryDeductions, c.FindPayHead(pt.Id)!.Type);
    }

    /// <summary>An On-Attendance head altered onto a PRODUCTION type is refused — the kind guard is not bypassed
    /// by going through Alter rather than Create.</summary>
    [Fact]
    public void An_alteration_linking_the_wrong_attendance_kind_is_refused()
    {
        var c = Seed();
        var (pay, ph) = MinimalPayroll(c);
        var present = pay.CreateAttendanceType("Present", AttendanceTypeKind.AttendancePaid);
        var units = pay.CreateAttendanceType("Units", AttendanceTypeKind.Production);
        var ot = ph.CreatePayHead("Overtime", PayHeadType.Earnings, PayHeadCalculationType.OnAttendance,
            attendanceTypeId: present.Id);

        Assert.Throws<InvalidOperationException>(() =>
            ph.AlterPayHead(ot.Id, PayHeadEdit.From(ot) with { AttendanceTypeId = units.Id }));

        Assert.Equal(present.Id, c.FindPayHead(ot.Id)!.AttendanceTypeId);
    }

    /// <summary>A rounding limit that is not a whole number of paisa is refused on the alter path as on create —
    /// the integer-paisa store would otherwise reject the save after the aggregate had already been mutated.</summary>
    [Fact]
    public void An_alteration_to_a_sub_paisa_rounding_limit_is_refused()
    {
        var c = Seed();
        var (_, ph) = MinimalPayroll(c);
        var head = ph.CreatePayHead("Basic", PayHeadType.Earnings, PayHeadCalculationType.FlatRate);

        Assert.Throws<InvalidOperationException>(() =>
            ph.AlterPayHead(head.Id, PayHeadEdit.From(head) with
            {
                RoundingMethod = PayHeadRoundingMethod.Normal,
                RoundingLimit = new Money(0.005m),
            }));

        Assert.Equal(PayHeadRoundingMethod.NotApplicable, c.FindPayHead(head.Id)!.RoundingMethod);
    }

    // ================================================================= the rollback is COMPLETE, not partial

    /// <summary>
    /// 🔴 <b>THE ROLLBACK COMPLETENESS TEST.</b> A refused alteration must leave the head byte-identical, not
    /// merely leave the one field the failing guard happened to read. This sets EVERY field to a different value
    /// and then trips the cycle guard last, so a rollback that restored only some fields would leave the others
    /// changed — and those changes would then persist on the next unrelated save, with nothing on screen to say so.
    ///
    /// <para>🔴 <b>"EVERY FIELD" USED TO MEAN SEVENTEEN OF TWENTY-TWO, AND THAT IS WHY THE STATUTORY FIVE ARE
    /// SPELLED OUT BELOW.</b> The head was created with the DEFAULT PF/ESI/PT values and the refused alteration
    /// passed the same defaults, so <c>PfComponent</c>, <c>PartOfPfWages</c>, <c>EsiComponent</c>,
    /// <c>PartOfEsiWages</c> and <c>PtComponent</c> were never VARIED — a <c>RestoreTo</c> that silently dropped
    /// one of them would have survived this test green. They are the four statutory tags that decide whether a
    /// head appears in a PF, ESI or Professional Tax return, so a stale one is a filing defect. Each is now
    /// created non-default and altered to a different non-default value.
    /// (<c>LedgerId</c> and <c>EmployerExpenseLedgerId</c> are genuinely out of scope: <c>AlterPayHead</c> never
    /// writes them — <see cref="PayHeadEdit"/> does not carry them — so there is nothing for a rollback to miss.)</para>
    /// </summary>
    [Fact]
    public void A_refused_alteration_leaves_every_field_exactly_as_it_was()
    {
        var c = Seed();
        var (pay, ph) = MinimalPayroll(c);
        var present = pay.CreateAttendanceType("Present", AttendanceTypeKind.AttendancePaid);
        var basic = ph.CreatePayHead("Basic", PayHeadType.Earnings, PayHeadCalculationType.FlatRate);

        // 🔴 The TYPE is EmployeesStatutoryDeductions rather than Earnings, and that is forced by the fixture
        // carrying real statutory tags: ValidateStatutoryComponentRole requires a head tagged with employee PF /
        // ESI / PT to post on the employee-deduction side. Tagging a head and typing it Earnings is the very
        // mis-configuration that guard exists to reject, so the fixture cannot be built the other way round.
        var head = ph.CreatePayHead("Overtime", PayHeadType.EmployeesStatutoryDeductions,
            PayHeadCalculationType.OnAttendance,
            underGroupId: IndirectExpenses(c),
            incomeTaxComponent: IncomeTaxComponent.SpecialAllowance,
            useForGratuity: true,
            roundingMethod: PayHeadRoundingMethod.Normal,
            roundingLimit: new Money(1m),
            calculationPeriod: PayHeadCalculationPeriod.Day,
            attendanceTypeId: present.Id,
            perDayCalculationBasisDays: 26,
            displayName: "OT",
            pfComponent: PfStatutoryComponent.EmployeeProvidentFund,
            partOfPfWages: true,
            esiComponent: EsiStatutoryComponent.EmployeeStateInsurance,
            partOfEsiWages: true,
            isOvertime: true,
            ptComponent: PtStatutoryComponent.ProfessionalTax);

        // The fixture is only worth anything if the statutory five really start non-default — assert that rather
        // than assume it, since a default creeping into CreatePayHead would quietly re-hollow this test.
        Assert.NotEqual(PfStatutoryComponent.None, head.PfComponent);
        Assert.NotEqual(EsiStatutoryComponent.None, head.EsiComponent);
        Assert.NotEqual(PtStatutoryComponent.None, head.PtComponent);
        Assert.True(head.PartOfPfWages);
        Assert.True(head.PartOfEsiWages);

        var before = PayHeadState.Capture(head);

        // Every field different AND a cycle-closing formula, so the refusal comes from the last guard to run.
        var circular = ph.CreatePayHead("Circular", PayHeadType.Earnings, PayHeadCalculationType.AsComputedValue,
            computation: new PayHeadComputation(
                new[] { new PayHeadComputationComponent(head.Id) },
                new[] { PayHeadComputationSlab.Percentage(1000) }));

        Assert.Throws<InvalidOperationException>(() =>
            ph.AlterPayHead(head.Id, PayHeadEdit.From(head) with
            {
                Name = "Overtime Renamed",
                DisplayName = "Changed",
                Type = PayHeadType.Deductions,
                CalculationType = PayHeadCalculationType.AsComputedValue,
                AffectsNetSalary = false,
                UnderGroupId = CurrentLiabilities(c),
                IncomeTaxComponent = IncomeTaxComponent.Bonus,
                UseForGratuity = false,
                RoundingMethod = PayHeadRoundingMethod.Upward,
                RoundingLimit = new Money(5m),
                CalculationPeriod = PayHeadCalculationPeriod.Month,
                AttendanceTypeId = null,
                PerDayCalculationBasisDays = null,
                Computation = new PayHeadComputation(
                    new[] { new PayHeadComputationComponent(circular.Id) },
                    new[] { PayHeadComputationSlab.Percentage(2500) }),
                // The statutory five, VARIED — see the remarks. Each moves to a different non-default value, so a
                // rollback that dropped any one of them leaves it changed and this test reddens.
                PfComponent = PfStatutoryComponent.EmployerProvidentFund,
                PartOfPfWages = false,
                EsiComponent = EsiStatutoryComponent.EmployerStateInsurance,
                PartOfEsiWages = false,
                IsOvertime = false,
                PtComponent = PtStatutoryComponent.None,
            }));

        Assert.Equal(before, PayHeadState.Capture(head));
        Assert.Equal("Basic", c.FindPayHead(basic.Id)!.Name);   // and nothing else moved either
    }

    // ================================================================= the ALTER argument cannot destroy by omission

    /// <summary>
    /// 🔴 <b>REVIEW FINDING F9 — AN ALTERATION THAT MENTIONS NOTHING MUST CHANGE NOTHING.</b>
    ///
    /// <para><b>The defect this pins out.</b> <c>AlterPayHead</c> used to take twenty-one loose parameters of
    /// which sixteen carried defaults, so <c>AlterPayHead(id, name, type, calcType)</c> compiled and silently
    /// RESET the computation formula (the rate), all four statutory tags, the rounding method and limit, the
    /// calculation period, the attendance link, the per-day basis and the display name. That is wrong money on a
    /// payslip produced by a call that looks innocent, and the shipped screen was safe only by hand-copying six
    /// fields under a comment. The argument is now a single <see cref="PayHeadEdit"/> whose members are all
    /// <c>required</c>, built with <c>PayHeadEdit.From(head)</c>, so omission means KEEP.</para>
    ///
    /// <para>This applies an edit taken straight from a fully-populated head and changes nothing, then asserts the
    /// head is byte-identical. It reddens if any field stops being carried by <c>From</c> — which is the exact
    /// shape the old defaults had.</para>
    /// </summary>
    [Fact]
    public void An_alteration_built_from_the_head_and_left_unchanged_alters_nothing()
    {
        var c = Seed();
        var (pay, ph) = MinimalPayroll(c);
        var present = pay.CreateAttendanceType("Present", AttendanceTypeKind.AttendancePaid);
        var basic = ph.CreatePayHead("Basic", PayHeadType.Earnings, PayHeadCalculationType.FlatRate);

        // Every field that AlterPayHead writes, set to a NON-default value — a field carried by accident (because
        // its default happens to match) would prove nothing.
        // EmployeesStatutoryDeductions for the same reason the fixture above gives: a head carrying employee
        // PF / ESI / PT tags must post on the employee-deduction side or ValidateStatutoryComponentRole rejects it.
        var head = ph.CreatePayHead("Overtime", PayHeadType.EmployeesStatutoryDeductions,
            PayHeadCalculationType.AsComputedValue,
            underGroupId: IndirectExpenses(c),
            affectsNetSalary: false,
            incomeTaxComponent: IncomeTaxComponent.SpecialAllowance,
            useForGratuity: true,
            roundingMethod: PayHeadRoundingMethod.Normal,
            roundingLimit: new Money(1m),
            calculationPeriod: PayHeadCalculationPeriod.Day,
            perDayCalculationBasisDays: 26,
            computation: new PayHeadComputation(
                new[] { new PayHeadComputationComponent(basic.Id) },
                new[] { PayHeadComputationSlab.Percentage(1234) }),
            displayName: "OT",
            pfComponent: PfStatutoryComponent.EmployeeProvidentFund,
            partOfPfWages: true,
            esiComponent: EsiStatutoryComponent.EmployeeStateInsurance,
            partOfEsiWages: true,
            isOvertime: true,
            ptComponent: PtStatutoryComponent.ProfessionalTax);
        _ = present;   // the attendance link is exercised by the refused-alteration fixture above

        var before = PayHeadState.Capture(head);

        ph.AlterPayHead(head.Id, PayHeadEdit.From(head));

        Assert.Equal(before, PayHeadState.Capture(head));

        // And the rate specifically, named, because it is the field the old shape most often wiped.
        Assert.Equal(1234, c.FindPayHead(head.Id)!.Computation!.Slabs.Single().RateBasisPoints);
    }

    // ================================================================= F11: altering a head a structure USES

    /// <summary>
    /// 🔴 <b>REVIEW FINDING F11 — ALTERING A HEAD A SALARY STRUCTURE ALREADY USES IS ALLOWED, AND WHAT IT DOES
    /// NEXT IS ASSERTED RATHER THAN ASSUMED.</b>
    ///
    /// <para><c>DeletePayHead</c> REFUSES while a salary structure references the head; <c>AlterPayHead</c>
    /// deliberately does not, because a mistyped rate on a referenced head is the normal case and refusing it is
    /// precisely what made the wrong figure permanent (the defect this slice exists to close). What had no
    /// coverage at all was the FORWARD half: nothing asserted what the next payroll run does. This posts April at
    /// the wrong rate, corrects the head, posts May, and asserts May uses the NEW figure while April keeps the
    /// old — the two halves of "forward-only" in one fixture.</para>
    ///
    /// <para><see cref="PayHeadService.SalaryStructuresUsing"/> is asserted alongside, because that count is what
    /// the screen puts in front of the operator so the reach is not invisible.</para>
    /// </summary>
    [Fact]
    public void Altering_a_referenced_head_is_allowed_and_only_the_NEXT_run_uses_the_new_rate()
    {
        var c = Seed();
        var (pay, ph) = MinimalPayroll(c);
        var basic = ph.CreatePayHead("Basic", PayHeadType.Earnings, PayHeadCalculationType.FlatRate,
            underGroupId: IndirectExpenses(c));
        // A Professional Tax deduction typed at 12% of Basic where 1.2% was meant — the slice's own worked example.
        var pt = ph.CreatePayHead("Professional Tax", PayHeadType.EmployeesStatutoryDeductions,
            PayHeadCalculationType.AsComputedValue, underGroupId: CurrentLiabilities(c),
            computation: new PayHeadComputation(
                new[] { new PayHeadComputationComponent(basic.Id) },
                new[] { PayHeadComputationSlab.Percentage(1200) }));

        var emp = pay.CreateEmployee("Rajkumar Sharma", pay.CreateEmployeeGroup("Staff").Id);
        new SalaryStructureService(c).DefineForEmployee(emp.Id, new DateOnly(2025, 4, 1), new[]
        {
            new SalaryStructureLine(basic.Id, 0, new Money(30000m)),
            new SalaryStructureLine(pt.Id, 1, null),
        });

        // The head IS referenced, and the engine says so — this is the number the screen shows the operator.
        Assert.Equal(1, ph.SalaryStructuresUsing(pt.Id));
        Assert.Throws<InvalidOperationException>(() => ph.DeletePayHead(pt.Id));   // delete is refused; alter is not

        var payroll = new PayrollVoucherService(c);
        var april = payroll.Post(new DateOnly(2025, 4, 1), new DateOnly(2025, 4, 30), new[] { emp.Id });
        var aprilPt = april.Lines.Single(l => l.Payroll!.PayHeadId == pt.Id).Payroll!.Amount;
        Assert.Equal(new Money(3600m), aprilPt);         // 12% of 30000 — the wrong figure, as posted

        // The correction, on a head a structure references. NOT refused.
        ph.AlterPayHead(pt.Id, PayHeadEdit.From(pt) with
        {
            Computation = new PayHeadComputation(
                new[] { new PayHeadComputationComponent(basic.Id) },
                new[] { PayHeadComputationSlab.Percentage(120) }),
        });

        var may = payroll.Post(new DateOnly(2025, 5, 1), new DateOnly(2025, 5, 31), new[] { emp.Id });
        var mayPt = may.Lines.Single(l => l.Payroll!.PayHeadId == pt.Id).Payroll!.Amount;

        Assert.Equal(new Money(360m), mayPt);            // 1.2% of 30000 — the NEXT run uses the correction
        Assert.Equal(new Money(3600m), april.Lines.Single(l => l.Payroll!.PayHeadId == pt.Id).Payroll!.Amount);
    }

    /// <summary>
    /// 🔴 <b>THE OMISSION GUARD.</b> <see cref="PayHeadState"/> lists the fields it restores BY NAME, so a
    /// property added to <see cref="PayHead"/> later is silently outside the rollback — and a refused alteration
    /// would then leave that one field changed. This walks <see cref="PayHead"/> by reflection and requires every
    /// settable property to have a same-named component on the snapshot, so the omission goes red instead.
    /// </summary>
    [Fact]
    public void Every_settable_pay_head_field_is_carried_by_the_rollback_snapshot()
    {
        var settable = typeof(PayHead).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.GetSetMethod() is not null)
            .Select(p => p.Name)
            .ToList();

        Assert.NotEmpty(settable);

        var carried = typeof(PayHeadState).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => p.Name)
            .ToHashSet(StringComparer.Ordinal);

        var missing = settable.Where(n => !carried.Contains(n)).ToList();

        Assert.True(missing.Count == 0,
            "PayHead has settable properties the alteration rollback does not carry: " +
            string.Join(", ", missing) +
            ". PayHeadService.AlterPayHead writes onto the LIVE pay head and restores it from PayHeadState when a " +
            "guard refuses, so a field missing from the snapshot survives a REFUSED alteration and is then " +
            "persisted by the next unrelated save, with nothing on screen to say so. Add it to PayHeadState's " +
            "parameter list, to Capture and to RestoreTo.");
    }

    // ================================================================= forward-only: the past is not restated

    /// <summary>
    /// 🔴 <b>THE SAFETY PROPERTY THAT MAKES ALTERATION ACCEPTABLE AT ALL.</b> Altering a pay head must not
    /// restate a period already paid. It does not, and the reason is structural rather than a promise: a posted
    /// payroll voucher carries its own immutable <see cref="PayrollLineDetail"/> per line — employee, pay head,
    /// accounting category and computed amount — and the payslip and registers read that posted detail instead of
    /// recomputing from the master.
    ///
    /// <para>This posts April, then makes the most violent alteration available — flipping an EARNING into a
    /// DEDUCTION and changing its group — and asserts the posted April voucher is untouched line for line. Had
    /// any report recomputed from the master, April's net pay would silently change months after it was paid.</para>
    /// </summary>
    [Fact]
    public void Altering_a_pay_head_does_not_restate_an_already_posted_payroll_voucher()
    {
        var c = Seed();
        var (pay, ph) = MinimalPayroll(c);
        var basic = ph.CreatePayHead("Basic", PayHeadType.Earnings, PayHeadCalculationType.FlatRate,
            underGroupId: IndirectExpenses(c));
        var emp = pay.CreateEmployee("Rajkumar Sharma", pay.CreateEmployeeGroup("Staff").Id);
        new SalaryStructureService(c).DefineForEmployee(emp.Id, new DateOnly(2025, 4, 1), new[]
        {
            new SalaryStructureLine(basic.Id, 0, new Money(30000m)),
        });

        var april = new PayrollVoucherService(c).Post(
            new DateOnly(2025, 4, 1), new DateOnly(2025, 4, 30), new[] { emp.Id });

        var before = april.Lines
            .Select(l => (l.LedgerId, l.Side, l.Amount, l.Payroll!.Category, l.Payroll!.Amount))
            .OrderBy(t => t.LedgerId).ThenBy(t => t.Side).ToList();
        var totalDebitBefore = april.TotalDebit;

        // The most violent alteration the screen allows: an earning becomes a deduction, under a new group.
        ph.AlterPayHead(basic.Id, PayHeadEdit.From(basic) with
        {
            Type = PayHeadType.Deductions,
            UnderGroupId = CurrentLiabilities(c),
            AffectsNetSalary = null,   // null = derive from the new Type, the create path's own rule
        });

        var after = april.Lines
            .Select(l => (l.LedgerId, l.Side, l.Amount, l.Payroll!.Category, l.Payroll!.Amount))
            .OrderBy(t => t.LedgerId).ThenBy(t => t.Side).ToList();

        Assert.Equal(before, after);
        Assert.Equal(totalDebitBefore, april.TotalDebit);
        Assert_Balanced(april);

        // And the master really did change — otherwise this test would pass on a no-op Alter.
        Assert.Equal(PayHeadType.Deductions, c.FindPayHead(basic.Id)!.Type);
    }

    private static void Assert_Balanced(Voucher v) => Assert.True(VoucherValidator.IsBalanced(v));
}
