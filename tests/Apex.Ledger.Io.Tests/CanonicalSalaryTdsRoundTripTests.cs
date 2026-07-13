using System.Text;
using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Services;

namespace Apex.Ledger.Io.Tests;

/// <summary>
/// Phase-8 slice-7 <b>§192 salary-TDS Io fold-in</b> gate (RQ-12; losslessness): a company with salary-TDS enabled,
/// an old-regime employee carrying a §192 income-tax deduction pay head and a Form-12BB tax declaration —
/// <b>exports and re-imports exact in JSON AND XML</b>, both byte-stable and into a fresh (differently-Guid'd)
/// company through the engine-routed <see cref="CompanyImportService"/> (the employee ref on the declaration is
/// re-mapped). A salary-TDS-off company carries no declarations and the toggle defaults off (ER-13).
/// </summary>
public sealed class CanonicalSalaryTdsRoundTripTests
{
    private static Company BuildTdsCompany()
    {
        var c = CompanyFactory.CreateSeeded("TDS Traders", new DateOnly(2025, 4, 1), new DateOnly(2025, 4, 1));
        var pay = new PayrollService(c);
        pay.EnablePayroll();
        pay.EnableSalaryTds();

        var ph = new PayHeadService(c);
        var indirect = c.FindGroupByName("Indirect Expenses")!.Id;
        var liab = c.FindGroupByName("Current Liabilities")!.Id;
        ph.CreatePayHead("Basic", PayHeadType.Earnings, PayHeadCalculationType.FlatRate, underGroupId: indirect);
        ph.CreatePayHead("TDS on Salary", PayHeadType.EmployeesStatutoryDeductions, PayHeadCalculationType.AsUserDefinedValue,
            underGroupId: liab, incomeTaxComponent: IncomeTaxComponent.TaxDeductedAtSource);

        var e = pay.CreateEmployee("Anita Rao", pay.CreateEmployeeGroup("Staff").Id);
        c.FindEmployee(e.Id)!.ApplicableTaxRegime = TaxRegime.Old;
        c.AddTaxDeclaration(new TaxDeclaration
        {
            EmployeeId = e.Id,
            Section80C = new Money(150_000m),
            Section80D = new Money(25_000m),
            Section80CCD1B = new Money(50_000m),
            HouseRentAllowanceExempt = new Money(120_000m),
            HomeLoanInterest24b = new Money(200_000m),
            OtherIncome = new Money(40_000m),
            PreviousEmployerSalary = new Money(300_000m),
            PreviousEmployerTds = new Money(12_345m),
        });
        return c;
    }

    private static Company Fresh() =>
        CompanyFactory.CreateSeeded("Fresh TDS Co", new DateOnly(2025, 4, 1), new DateOnly(2025, 4, 1));

    [Fact]
    public void Json_round_trips_byte_stable()
    {
        var c = BuildTdsCompany();
        var first = CanonicalJson.Export(c);
        var (model, errors) = CanonicalJson.Parse(first);
        Assert.Empty(errors);
        Assert.Equal(first, CanonicalJson.Export(model!));
        Assert.DoesNotContain("Tally", Encoding.UTF8.GetString(first), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Xml_round_trips_byte_stable()
    {
        var c = BuildTdsCompany();
        var first = CanonicalXml.Export(c);
        var (model, errors) = CanonicalXml.Parse(first);
        Assert.Empty(errors);
        Assert.Equal(first, CanonicalXml.Export(model!));
    }

    [Fact]
    public void Json_and_xml_carry_identical_tds_payload()
    {
        var c = BuildTdsCompany();
        var (jm, je) = CanonicalJson.Parse(CanonicalJson.Export(c));
        var (xm, xe) = CanonicalXml.Parse(CanonicalXml.Export(c));
        Assert.Empty(je);
        Assert.Empty(xe);
        Assert.Equal(CanonicalJson.Export(jm!), CanonicalJson.Export(xm!));
    }

    [Fact]
    public void Tds_company_export_import_into_fresh_company_reconciles_json_and_xml()
    {
        var source = BuildTdsCompany();

        foreach (var bytes in new[] { CanonicalJson.Export(source), CanonicalXml.Export(source) })
        {
            var (model, errors) = bytes[0] == (byte)'{' ? CanonicalJson.Parse(bytes) : CanonicalXml.Parse(bytes);
            Assert.Empty(errors);

            var fresh = Fresh();
            var result = new CompanyImportService(fresh).Apply(model!);
            Assert.True(result.Applied, string.Join("; ", result.Errors));

            // The establishment salary-TDS toggle + the §192 deduction pay-head tag survive.
            Assert.True(fresh.SalaryTdsEnabled);
            Assert.Equal(IncomeTaxComponent.TaxDeductedAtSource, fresh.FindPayHeadByName("TDS on Salary")!.IncomeTaxComponent);

            // The declaration survives with its employee ref RE-MAPPED to the fresh company's employee id.
            var e = fresh.Employees.Single(x => x.Name == "Anita Rao");
            var d = fresh.FindTaxDeclaration(e.Id)!;
            Assert.NotNull(d);
            Assert.Equal(e.Id, d.EmployeeId);                                    // re-mapped, not the source id
            Assert.Equal(new Money(150_000m), d.Section80C);
            Assert.Equal(new Money(25_000m), d.Section80D);
            Assert.Equal(new Money(50_000m), d.Section80CCD1B);
            Assert.Equal(new Money(120_000m), d.HouseRentAllowanceExempt);
            Assert.Equal(new Money(200_000m), d.HomeLoanInterest24b);
            Assert.Equal(new Money(40_000m), d.OtherIncome);
            Assert.Equal(new Money(300_000m), d.PreviousEmployerSalary);
            Assert.Equal(new Money(12_345m), d.PreviousEmployerTds);
        }
    }

    [Fact]
    public void Import_rejects_a_tax_declaration_referencing_an_unknown_employee()
    {
        var source = BuildTdsCompany();
        // Corrupt the declaration to reference a non-existent employee (bypassing the aggregate helper).
        source.AddTaxDeclaration(new TaxDeclaration { EmployeeId = Guid.NewGuid(), Section80C = new Money(1_000m) });

        var (model, errors) = CanonicalJson.Parse(CanonicalJson.Export(source));
        Assert.Empty(errors);

        var fresh = Fresh();
        var result = new CompanyImportService(fresh).Apply(model!);
        Assert.False(result.Applied);
        Assert.Contains(result.Errors, e => e.Contains("tax declaration", StringComparison.OrdinalIgnoreCase));
        Assert.Null(fresh.FindEmployeeByName("Anita Rao")); // nothing applied — atomic
    }

    [Fact]
    public void Company_without_salary_tds_carries_no_declarations_and_defaults_off()
    {
        // ER-13: a company not deducting salary-TDS carries no declarations and the toggle is off.
        var c = Fresh();
        var (model, errors) = CanonicalJson.Parse(CanonicalJson.Export(c));
        Assert.Empty(errors);
        Assert.False(model!.Company.SalaryTdsEnabled);
        Assert.Empty(model.Payload.TaxDeclarations);
    }
}
