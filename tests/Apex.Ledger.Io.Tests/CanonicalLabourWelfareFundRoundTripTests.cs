using System.Text;
using System.Xml.Linq;
using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Services;

namespace Apex.Ledger.Io.Tests;

/// <summary>
/// Census 7.19 (<b>Labour Welfare Fund</b>) — the schema-v63 <b>effective window</b> on a pay-head computation
/// slab survives export and re-import in JSON <b>and</b> XML, byte-stable, and into a fresh, differently-Guid'd
/// company through the engine-routed <see cref="CompanyImportService"/>.
///
/// <para>🔴 <b>WHY THIS IS NOT ROUTINE LOSSLESSNESS BOOK-KEEPING.</b> The window is the only thing separating a
/// once-a-year levy from a monthly one. If export or import dropped it, the imported book would compute a
/// <b>different payslip</b> from the exported one — an annual Labour Welfare Fund contribution taken in all
/// twelve months instead of one — and nothing on any screen would show why. Losing a date here is losing money
/// off somebody's salary, so it is pinned on its own rather than left to the general pay-head round trip.</para>
///
/// <para><b>The undated case is asserted too (ER-13):</b> a slab with no dates must emit <b>no</b> attributes at
/// all, so a book that uses no dated slabs — every book that exists today — exports byte-identically to the way
/// it did before v63.</para>
///
/// <para>🔴 <b>No rate is asserted.</b> The amount below is a fixture an operator typed, not any State's Labour
/// Welfare Fund contribution; this product seeds no LWF rates because they are per-State law.</para>
/// </summary>
public class CanonicalLabourWelfareFundRoundTripTests
{
    private static readonly DateOnly FyStart = new(2026, 4, 1);
    private static readonly DateOnly LevyFrom = new(2026, 12, 1);
    private static readonly DateOnly LevyTo = new(2026, 12, 31);

    /// <summary>A fixture contribution — NOT a statutory rate.</summary>
    private const decimal OperatorTypedContribution = 100m;

    /// <summary>A book with one dated (December-only) deduction head and one deliberately UNDATED head, so both
    /// halves of the contract travel through the same export.</summary>
    private static Company Build()
    {
        var c = CompanyFactory.CreateSeeded("LWF Traders", FyStart, FyStart);
        var payroll = new PayrollService(c);
        payroll.EnablePayroll();
        var grp = payroll.CreateEmployeeGroup("Staff");
        var emp = payroll.CreateEmployee("Rajkumar Sharma", grp.Id);

        var svc = new PayHeadService(c);
        var indirect = c.FindGroupByName("Indirect Expenses")!.Id;
        var liabilities = c.FindGroupByName("Current Liabilities")!.Id;

        var basic = svc.CreatePayHead("Basic", PayHeadType.Earnings, PayHeadCalculationType.FlatRate,
            underGroupId: indirect, incomeTaxComponent: IncomeTaxComponent.BasicSalary);

        // The vendor's LWF shape: "Deductions From Employees", computed, with a dated Computation Information row.
        var lwf = svc.CreatePayHead("Labour Welfare Fund", PayHeadType.Deductions,
            PayHeadCalculationType.AsComputedValue, underGroupId: liabilities,
            computation: new PayHeadComputation(
                new[] { new PayHeadComputationComponent(basic.Id) },
                new[] { PayHeadComputationSlab.ForSingleMonth(new Money(OperatorTypedContribution), 2026, 12) }));

        // The control: an ordinary perpetual deduction, which must stay perpetual and emit no date attributes.
        var advance = svc.CreatePayHead("Advance Recovery", PayHeadType.Deductions,
            PayHeadCalculationType.AsComputedValue, underGroupId: liabilities,
            computation: new PayHeadComputation(
                new[] { new PayHeadComputationComponent(basic.Id) },
                new[] { PayHeadComputationSlab.FlatValue(new Money(500m)) }));

        new SalaryStructureService(c).DefineForEmployee(emp.Id, FyStart, new[]
        {
            new SalaryStructureLine(basic.Id, 0, Money.FromRupees(30_000m)),
            new SalaryStructureLine(lwf.Id, 1),
            new SalaryStructureLine(advance.Id, 2),
        });

        return c;
    }

    private static Company FreshTarget() => CompanyFactory.CreateSeeded("Fresh LWF Import Co", FyStart, FyStart);

    [Fact]
    public void Json_round_trips_byte_stable_and_keeps_the_effective_window()
    {
        var c = Build();
        var first = CanonicalJson.Export(c);

        var (model, errors) = CanonicalJson.Parse(first);
        Assert.Empty(errors);
        Assert.Equal(first, CanonicalJson.Export(model!));   // byte-for-byte

        AssertWindowSurvived(model!);
        AssertNoTally(first);
    }

    [Fact]
    public void Xml_round_trips_byte_stable_and_keeps_the_effective_window()
    {
        var c = Build();
        var first = CanonicalXml.Export(c);

        var (model, errors) = CanonicalXml.Parse(first);
        Assert.Empty(errors);
        Assert.Equal(first, CanonicalXml.Export(model!));

        AssertWindowSurvived(model!);
        AssertNoTally(first);
    }

    [Fact]
    public void Json_and_xml_carry_an_identical_payload()
    {
        var c = Build();
        var (jm, je) = CanonicalJson.Parse(CanonicalJson.Export(c));
        var (xm, xe) = CanonicalXml.Parse(CanonicalXml.Export(c));
        Assert.Empty(je);
        Assert.Empty(xe);
        Assert.Equal(CanonicalJson.Export(jm!), CanonicalJson.Export(xm!));
    }

    /// <summary>
    /// 🔴 <b>THE ONE THAT MATTERS.</b> After a full export → import into a FRESH company (new Guids throughout),
    /// the Labour Welfare Fund head still deducts in December alone. Asserted through the <b>computed payslip</b>
    /// across a whole financial year, not by reading the imported date back — an import that stored the date but
    /// stopped the engine consulting it would pass the weaker check and still over-deduct eleven times.
    /// </summary>
    [Fact]
    public void Export_import_into_a_fresh_company_still_deducts_in_one_month_only()
    {
        var source = Build();

        foreach (var bytes in new[] { CanonicalJson.Export(source), CanonicalXml.Export(source) })
        {
            var (model, errors) = bytes[0] == (byte)'{' ? CanonicalJson.Parse(bytes) : CanonicalXml.Parse(bytes);
            Assert.Empty(errors);

            var fresh = FreshTarget();
            var result = new CompanyImportService(fresh).Apply(model!);
            Assert.True(result.Applied, string.Join("; ", result.Errors));

            var head = fresh.PayHeads.Single(p => p.Name == "Labour Welfare Fund");
            var employee = fresh.Employees.Single();
            var engine = new PayrollComputationService(fresh);

            decimal annual = 0m;
            for (var m = 0; m < 12; m++)
            {
                var first = FyStart.AddMonths(m);
                var last = new DateOnly(first.Year, first.Month, DateTime.DaysInMonth(first.Year, first.Month));
                var amount = engine.Compute(employee.Id, first, last)
                    .Lines.Single(l => l.PayHead.Id == head.Id).Amount.Amount;
                annual += amount;
                var expected = m == 8 ? OperatorTypedContribution : 0m;   // offset 8 = December 2026
                Assert.True(amount == expected,
                    $"After an import, {first:MMM yyyy} expected {expected} but deducted {amount}.");
            }

            Assert.Equal(OperatorTypedContribution, annual);
        }
    }

    /// <summary>
    /// <b>ER-13.</b> An undated slab emits NEITHER attribute, so a book that uses no dated slabs — which is every
    /// book that exists today — exports exactly the bytes it did before v63. A date attribute written as
    /// <c>null</c> or as an empty string would break that silently.
    /// </summary>
    [Fact]
    public void An_undated_slab_emits_no_effective_attributes_at_all()
    {
        var c = Build();
        var xml = Encoding.UTF8.GetString(CanonicalXml.Export(c));

        // 🔴 SCOPED TO <slab> ELEMENTS DELIBERATELY. A bare string count over the whole document also catches
        // salaryStructure/@effectiveFrom, which is a different, long-standing field — the first draft of this
        // test did exactly that and failed for the wrong reason. Counting the wrong thing would have made this
        // assertion unfalsifiable the moment any other dated element was added.
        var slabs = XDocument.Parse(xml).Descendants("slab").ToList();
        Assert.Equal(2, slabs.Count);   // one dated (LWF), one perpetual (Advance Recovery)

        var dated = slabs.Where(s => s.Attribute("effectiveFrom") is not null).ToList();
        var undated = slabs.Where(s => s.Attribute("effectiveFrom") is null).ToList();

        var only = Assert.Single(dated);
        Assert.Equal("2026-12-01", only.Attribute("effectiveFrom")!.Value);
        Assert.Equal("2026-12-31", only.Attribute("effectiveTo")!.Value);

        // The perpetual slab carries NEITHER attribute — not an empty one, not a null-valued one (ER-13).
        var perpetual = Assert.Single(undated);
        Assert.Null(perpetual.Attribute("effectiveFrom"));
        Assert.Null(perpetual.Attribute("effectiveTo"));
    }

    private static void AssertWindowSurvived(CanonicalModel model)
    {
        var payHeads = model.Payload.PayHeads;

        var lwf = payHeads.Single(p => p.Name == "Labour Welfare Fund");
        var dated = Assert.Single(lwf.ComputationSlabs);
        Assert.Equal("2026-12-01", dated.EffectiveFrom);
        Assert.Equal("2026-12-31", dated.EffectiveTo);

        var advance = payHeads.Single(p => p.Name == "Advance Recovery");
        var perpetual = Assert.Single(advance.ComputationSlabs);
        Assert.Null(perpetual.EffectiveFrom);
        Assert.Null(perpetual.EffectiveTo);
    }

    /// <summary>The shipped payload must never contain the reference product's brand (R7 / the no-branding rule).
    /// The product is "Apex Solutions".</summary>
    private static void AssertNoTally(byte[] payload) =>
        Assert.DoesNotContain("Tally", Encoding.UTF8.GetString(payload), StringComparison.OrdinalIgnoreCase);
}
