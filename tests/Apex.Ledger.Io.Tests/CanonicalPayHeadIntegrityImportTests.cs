using System;
using System.Collections.Generic;
using System.Linq;
using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Services;

namespace Apex.Ledger.Io.Tests;

/// <summary>
/// Phase-8 slice-2 <b>import integrity</b> regression guard (adversarial): the engine-bypassing import path
/// (<see cref="CompanyImportService"/> + <see cref="ImportPlan"/> new up pay heads directly and assign
/// <c>Computation</c> without routing through <see cref="PayHeadService"/>) must re-run the SAME computed-on
/// integrity guards the service enforces, or reject the batch — otherwise a hand-edited export could load a
/// graph the slice-3 engine cannot compute (a computed-on cycle, a self-reference, a computed head with no slab,
/// or a non-computed head carrying a formula). A valid file still imports; every invalid graph is rejected
/// all-or-nothing (RQ-21/RQ-23) with a helpful message, and the target company is left untouched.
/// </summary>
public sealed class CanonicalPayHeadIntegrityImportTests
{
    private static Company BuildTwoComputedHeads()
    {
        var c = CompanyFactory.CreateSeeded("PayHead Integrity Co", new DateOnly(2025, 4, 1), new DateOnly(2025, 4, 1));
        var payroll = new PayrollService(c);
        payroll.EnablePayroll();
        var svc = new PayHeadService(c);
        var indirect = c.FindGroupByName("Indirect Expenses")!.Id;

        var basic = svc.CreatePayHead("Basic", PayHeadType.Earnings, PayHeadCalculationType.FlatRate, underGroupId: indirect);
        svc.CreatePayHead("A", PayHeadType.Earnings, PayHeadCalculationType.AsComputedValue, underGroupId: indirect,
            computation: new PayHeadComputation(
                new[] { new PayHeadComputationComponent(basic.Id) }, new[] { PayHeadComputationSlab.Percentage(4000) }));
        svc.CreatePayHead("B", PayHeadType.Earnings, PayHeadCalculationType.AsComputedValue, underGroupId: indirect,
            computation: new PayHeadComputation(
                new[] { new PayHeadComputationComponent(basic.Id) }, new[] { PayHeadComputationSlab.Percentage(2000) }));
        return c;
    }

    private static CanonicalModel ParseFrom(Company c)
    {
        var (model, errors) = CanonicalJson.Parse(CanonicalJson.Export(c));
        Assert.Empty(errors);
        return model!;
    }

    private static Guid IdOf(CanonicalModel model, string name) =>
        model.Payload.PayHeads.Single(p => p.Name == name).Id;

    private static CanonicalModel RewritePayHead(
        CanonicalModel model, string name, Func<PayHeadDto, PayHeadDto> edit)
    {
        var payHeads = model.Payload.PayHeads.Select(p => p.Name == name ? edit(p) : p).ToList();
        return model with { Payload = model.Payload with { PayHeads = payHeads } };
    }

    private static PayHeadComputationComponentDto Component(Guid id) => new() { PayHeadId = id };

    private static Company FreshTarget() =>
        CompanyFactory.CreateSeeded("Fresh Integrity Import Co", new DateOnly(2025, 4, 1), new DateOnly(2025, 4, 1));

    [Fact]
    public void Rejects_a_hand_edited_import_that_closes_a_computed_on_cycle()
    {
        var model = ParseFrom(BuildTwoComputedHeads());
        var aId = IdOf(model, "A");
        var bId = IdOf(model, "B");

        // A computed on B, B computed on A ⇒ A→B→A (the exact graph PayHeadService rejects as a cycle).
        model = RewritePayHead(model, "A", p => p with { ComputationComponents = new[] { Component(bId) } });
        model = RewritePayHead(model, "B", p => p with { ComputationComponents = new[] { Component(aId) } });

        var fresh = FreshTarget();
        var result = new CompanyImportService(fresh).Apply(model);

        Assert.False(result.Applied);
        Assert.Contains(result.Errors, e => e.Contains("cycle", StringComparison.OrdinalIgnoreCase));
        Assert.Empty(fresh.PayHeads);   // all-or-nothing: nothing applied
    }

    [Fact]
    public void Rejects_a_hand_edited_import_with_a_self_referential_computed_head()
    {
        var model = ParseFrom(BuildTwoComputedHeads());
        var aId = IdOf(model, "A");
        model = RewritePayHead(model, "A", p => p with { ComputationComponents = new[] { Component(aId) } });

        var fresh = FreshTarget();
        var result = new CompanyImportService(fresh).Apply(model);

        Assert.False(result.Applied);
        Assert.Contains(result.Errors, e =>
            e.Contains("itself", StringComparison.OrdinalIgnoreCase) ||
            e.Contains("cycle", StringComparison.OrdinalIgnoreCase));
        Assert.Empty(fresh.PayHeads);
    }

    [Fact]
    public void Rejects_a_hand_edited_import_with_a_computed_head_that_has_no_slab()
    {
        var model = ParseFrom(BuildTwoComputedHeads());
        // A computed on Basic but with the slabs stripped — no rate/value ⇒ no deterministic amount for slice 3.
        model = RewritePayHead(model, "A", p => p with { ComputationSlabs = Array.Empty<PayHeadComputationSlabDto>() });

        var fresh = FreshTarget();
        var result = new CompanyImportService(fresh).Apply(model);

        Assert.False(result.Applied);
        Assert.Empty(fresh.PayHeads);
    }

    [Fact]
    public void Rejects_a_hand_edited_import_where_a_non_computed_head_carries_a_formula()
    {
        var model = ParseFrom(BuildTwoComputedHeads());
        var basicId = IdOf(model, "Basic");
        // Give the flat-rate "Basic" a computation formula it is not allowed to carry.
        model = RewritePayHead(model, "Basic", p => p with
        {
            ComputationComponents = new[] { Component(IdOf(model, "A")) },
            ComputationSlabs = new[] { new PayHeadComputationSlabDto { SlabType = nameof(PayHeadComputationSlabType.Percentage), RateBasisPoints = 1000 } },
        });

        var fresh = FreshTarget();
        var result = new CompanyImportService(fresh).Apply(model);

        Assert.False(result.Applied);
        Assert.Empty(fresh.PayHeads);
        _ = basicId;
    }

    [Fact]
    public void A_valid_computed_graph_still_imports_cleanly()
    {
        // Guard against over-rejection: the un-edited valid export must still apply.
        var model = ParseFrom(BuildTwoComputedHeads());
        var fresh = FreshTarget();
        var result = new CompanyImportService(fresh).Apply(model);

        Assert.True(result.Applied, string.Join("; ", result.Errors));
        Assert.Equal(3, fresh.PayHeads.Count);
    }

    // ---- salary-structure line integrity (SalaryStructureService.ValidateLines, also bypassed on import) ----

    private static Company BuildEmployeeStructure()
    {
        var c = CompanyFactory.CreateSeeded("Salary Integrity Co", new DateOnly(2025, 4, 1), new DateOnly(2025, 4, 1));
        var payroll = new PayrollService(c);
        payroll.EnablePayroll();
        var emp = payroll.CreateEmployee("Rajkumar", payroll.CreateEmployeeGroup("Marketing").Id);
        var svc = new PayHeadService(c);
        var indirect = c.FindGroupByName("Indirect Expenses")!.Id;
        var basic = svc.CreatePayHead("Basic", PayHeadType.Earnings, PayHeadCalculationType.FlatRate, underGroupId: indirect);
        var da = svc.CreatePayHead("DA", PayHeadType.Earnings, PayHeadCalculationType.AsComputedValue, underGroupId: indirect,
            computation: new PayHeadComputation(
                new[] { new PayHeadComputationComponent(basic.Id) }, new[] { PayHeadComputationSlab.Percentage(4000) }));

        new SalaryStructureService(c).DefineForEmployee(emp.Id, new DateOnly(2025, 4, 1),
            new[]
            {
                new SalaryStructureLine(basic.Id, 0, Money.FromRupees(80_000m)),   // flat → amount
                new SalaryStructureLine(da.Id, 1),                                  // computed → no amount
            });
        return c;
    }

    private static CanonicalModel RewriteStructureLines(
        CanonicalModel model, Func<IReadOnlyList<SalaryStructureLineDto>, IReadOnlyList<SalaryStructureLineDto>> edit)
    {
        var ss = model.Payload.SalaryStructures.Single();
        var updated = ss with { Lines = edit(ss.Lines) };
        return model with { Payload = model.Payload with { SalaryStructures = new[] { updated } } };
    }

    [Fact]
    public void Rejects_a_hand_edited_structure_line_whose_value_contradicts_its_calc_type()
    {
        // Put an amount against the COMPUTED head "DA" — the service forbids it; the import must too.
        var model = ParseFrom(BuildEmployeeStructure());
        model = RewriteStructureLines(model, lines => lines
            .Select(l => l.Order == 1 ? l with { AmountPaisa = 500000L } : l)
            .ToList());

        var fresh = FreshTarget();
        var result = new CompanyImportService(fresh).Apply(model);

        Assert.False(result.Applied);
        Assert.Empty(fresh.SalaryStructures);
    }

    [Fact]
    public void Rejects_a_hand_edited_structure_with_a_duplicate_pay_head_line()
    {
        var model = ParseFrom(BuildEmployeeStructure());
        var basicId = IdOf(model, "Basic");
        model = RewriteStructureLines(model, lines => new[]
        {
            lines[0],
            new SalaryStructureLineDto { PayHeadId = basicId, Order = 1, AmountPaisa = 100000L },   // Basic twice
        });

        var fresh = FreshTarget();
        var result = new CompanyImportService(fresh).Apply(model);

        Assert.False(result.Applied);
        Assert.Empty(fresh.SalaryStructures);
    }

    [Fact]
    public void A_valid_structure_still_imports_cleanly()
    {
        var model = ParseFrom(BuildEmployeeStructure());
        var fresh = FreshTarget();
        var result = new CompanyImportService(fresh).Apply(model);

        Assert.True(result.Applied, string.Join("; ", result.Errors));
        Assert.Single(fresh.SalaryStructures);
    }
}
