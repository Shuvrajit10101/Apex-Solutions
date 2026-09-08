using System;
using System.Linq;
using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Services;

namespace Apex.Ledger.Io.Tests;

/// <summary>
/// <b>Census row 7.18 — the NPS statutory pay type across the Io boundary.</b>
///
/// <para>Two things can go silently wrong here and neither shows up in the domain tests.</para>
/// <list type="number">
///   <item><b>The tag can vanish in transit.</b> It rides the existing <c>income_tax_component</c> field and is
///   round-tripped <b>by name</b>. A name that does not survive export→parse→import comes back as
///   <see cref="IncomeTaxComponent.NotApplicable"/> — an NPS head silently demoted to an ordinary one, with the
///   Payroll Statutory Summary quietly dropping its row and nothing failing.</item>
///   <item><b>The wrong-side guard can be bypassed.</b> The import path news up pay heads directly rather than
///   going through <see cref="PayHeadService"/>, so a hand-edited export pairing an employer pay-head type with the
///   Tier-II statutory pay type would load a side of the scheme that does not exist — and would foot, because the
///   phantom employer expense/payable pair balances itself. The pre-flight has to re-run the guard.</item>
/// </list>
/// </summary>
public sealed class NpsPayHeadIoTests
{
    private static readonly DateOnly Start = new(2025, 4, 1);

    private static Company BuildWithNpsHeads()
    {
        var c = CompanyFactory.CreateSeeded("NPS Io Co", Start, Start);
        new PayrollService(c).EnablePayroll();
        var svc = new PayHeadService(c);
        var indirect = c.FindGroupByName("Indirect Expenses")!.Id;
        var liab = c.FindGroupByName("Current Liabilities")!.Id;

        svc.CreatePayHead("Employee NPS Deduction", PayHeadType.EmployeesStatutoryDeductions,
            PayHeadCalculationType.AsUserDefinedValue, underGroupId: liab,
            incomeTaxComponent: IncomeTaxComponent.NationalPensionSchemeTierI);
        svc.CreatePayHead("Employer NPS Contribution", PayHeadType.EmployersStatutoryContributions,
            PayHeadCalculationType.AsUserDefinedValue, underGroupId: indirect,
            incomeTaxComponent: IncomeTaxComponent.NationalPensionSchemeTierI);
        svc.CreatePayHead("Employee NPS Tier II", PayHeadType.EmployeesStatutoryDeductions,
            PayHeadCalculationType.AsUserDefinedValue, underGroupId: liab,
            incomeTaxComponent: IncomeTaxComponent.NationalPensionSchemeTierII);
        return c;
    }

    private static CanonicalModel ParseFrom(Company c)
    {
        var (model, errors) = CanonicalJson.Parse(CanonicalJson.Export(c));
        Assert.Empty(errors);
        return model!;
    }

    private static Company FreshTarget() => CompanyFactory.CreateSeeded("Fresh NPS Import Co", Start, Start);

    /// <summary>
    /// Both NPS tiers survive export → parse → import <b>by name</b>, on both accounting sides. The exported name
    /// is asserted directly too: a rename of the enum member is a silent breaking change to every file already
    /// written, so the wire name is pinned rather than left to <c>ToString()</c>.
    /// </summary>
    [Fact]
    public void Both_nps_statutory_pay_types_round_trip_by_name_on_both_sides()
    {
        var model = ParseFrom(BuildWithNpsHeads());

        Assert.Equal(nameof(IncomeTaxComponent.NationalPensionSchemeTierI),
            model.Payload.PayHeads.Single(p => p.Name == "Employee NPS Deduction").IncomeTaxComponent);
        Assert.Equal(nameof(IncomeTaxComponent.NationalPensionSchemeTierI),
            model.Payload.PayHeads.Single(p => p.Name == "Employer NPS Contribution").IncomeTaxComponent);
        Assert.Equal(nameof(IncomeTaxComponent.NationalPensionSchemeTierII),
            model.Payload.PayHeads.Single(p => p.Name == "Employee NPS Tier II").IncomeTaxComponent);

        var fresh = FreshTarget();
        var result = new CompanyImportService(fresh).Apply(model);
        Assert.True(result.Applied, string.Join(" | ", result.Errors));

        Assert.Equal(IncomeTaxComponent.NationalPensionSchemeTierI,
            fresh.FindPayHeadByName("Employee NPS Deduction")!.IncomeTaxComponent);
        Assert.Equal(IncomeTaxComponent.NationalPensionSchemeTierI,
            fresh.FindPayHeadByName("Employer NPS Contribution")!.IncomeTaxComponent);
        Assert.Equal(IncomeTaxComponent.NationalPensionSchemeTierII,
            fresh.FindPayHeadByName("Employee NPS Tier II")!.IncomeTaxComponent);
    }

    /// <summary>
    /// 🔴 A hand-edited export moving the Tier-II head to the employer side is rejected all-or-nothing. The generic
    /// statutory-role guard cannot see this: it keys off <c>RequiredStatutoryRole</c>, which returns a single role
    /// and therefore says nothing at all about NPS, whose Tier-I is legitimately two-sided.
    /// </summary>
    [Fact]
    public void Rejects_a_hand_edited_import_that_puts_nps_tier_two_on_the_employer_side()
    {
        var model = ParseFrom(BuildWithNpsHeads());
        var payHeads = model.Payload.PayHeads
            .Select(p => p.Name == "Employee NPS Tier II"
                ? p with { PayHeadType = nameof(PayHeadType.EmployersStatutoryContributions), AffectsNetSalary = false }
                : p)
            .ToList();
        model = model with { Payload = model.Payload with { PayHeads = payHeads } };

        var fresh = FreshTarget();
        var result = new CompanyImportService(fresh).Apply(model);

        Assert.False(result.Applied);
        Assert.Contains(result.Errors, e => e.Contains(NationalPensionScheme.TierIIName, StringComparison.Ordinal));
        Assert.Empty(fresh.PayHeads);   // all-or-nothing: the valid heads in the same batch did not land either
    }

    /// <summary>
    /// The mirror of the test above, so the guard is not simply refusing every NPS head that reaches the import
    /// path: an <b>employer Tier-I</b> head — the side the scheme does have — imports cleanly.
    /// </summary>
    [Fact]
    public void Accepts_an_imported_employer_nps_tier_one_head()
    {
        var model = ParseFrom(BuildWithNpsHeads());
        var fresh = FreshTarget();

        var result = new CompanyImportService(fresh).Apply(model);

        Assert.True(result.Applied, string.Join(" | ", result.Errors));
        var employer = fresh.FindPayHeadByName("Employer NPS Contribution")!;
        Assert.Equal(PayHeadType.EmployersStatutoryContributions, employer.Type);
        Assert.Equal(IncomeTaxComponent.NationalPensionSchemeTierI, employer.IncomeTaxComponent);
        Assert.False(employer.AffectsNetSalary);
    }
}
