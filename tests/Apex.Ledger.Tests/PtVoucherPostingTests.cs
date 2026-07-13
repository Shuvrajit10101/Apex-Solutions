using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Reports;
using Apex.Ledger.Services;
using Xunit;

namespace Apex.Ledger.Tests;

/// <summary>
/// Phase-8 slice-6 <b>Professional-Tax payroll-voucher posting</b> contract (RQ-11; ER-1/ER-2) — the integrated,
/// balanced PT deduction through the S3 posting path. PT is an <b>employee deduction</b> that reduces net and is
/// credited to "Professional Tax" (Professional Tax Payable); there is <b>no employer contribution</b>. The headline
/// oracle is the golden slab: Maharashtra man, Basic ₹12,000 → PT ₹200 (₹300 in February), FY total ₹2,500; the
/// voucher balances Dr==Cr to the paisa and the PT reduces net pay.
/// </summary>
public sealed class PtVoucherPostingTests
{
    // GST state codes.
    private const string MH = "27";
    private const string KA = "29";
    private const string WB = "19";

    private static Company Seed()
        => CompanyFactory.CreateSeeded("PT Post Co", new DateOnly(2025, 4, 1), new DateOnly(2025, 4, 1));

    private static Guid IndirectExpenses(Company c) => c.FindGroupByName("Indirect Expenses")!.Id;
    private static Guid CurrentLiabilities(Company c) => c.FindGroupByName("Current Liabilities")!.Id;

    private readonly record struct PtHeads(Guid Basic, Guid Pt);

    private static PtHeads CreateHeads(PayHeadService ph, Company c)
    {
        var basic = ph.CreatePayHead("Basic", PayHeadType.Earnings, PayHeadCalculationType.FlatRate,
            underGroupId: IndirectExpenses(c));
        var pt = ph.CreatePayHead("Professional Tax", PayHeadType.EmployeesStatutoryDeductions,
            PayHeadCalculationType.AsUserDefinedValue, underGroupId: CurrentLiabilities(c),
            ptComponent: PtStatutoryComponent.ProfessionalTax);
        return new PtHeads(basic.Id, pt.Id);
    }

    private static SalaryStructureLine[] Lines(PtHeads h, decimal basic) => new[]
    {
        new SalaryStructureLine(h.Basic, 0, new Money(basic)),
        new SalaryStructureLine(h.Pt, 1),
    };

    private static (Company Company, Guid EmployeeId, PtHeads Heads) Build(
        string? state, decimal basic, string? gender = "Male")
    {
        var c = Seed();
        var pay = new PayrollService(c);
        pay.EnablePayroll();
        pay.EnableProfessionalTax(stateCode: state);
        var ph = new PayHeadService(c);
        var heads = CreateHeads(ph, c);
        var e = pay.CreateEmployee("Ravi Kumar", pay.CreateEmployeeGroup("Staff").Id);
        if (gender is not null) c.FindEmployee(e.Id)!.Gender = gender;
        new SalaryStructureService(c).DefineForEmployee(e.Id, new DateOnly(2025, 4, 1), Lines(heads, basic));
        return (c, e.Id, heads);
    }

    private static Voucher PostMonth(Company c, Guid empId, int year, int month)
    {
        var from = new DateOnly(year, month, 1);
        var to = new DateOnly(year, month, DateTime.DaysInMonth(year, month));
        return new PayrollVoucherService(c).Post(from, to, new[] { empId });
    }

    private static void AssertLine(Voucher v, Guid ledgerId, DrCr side, decimal amount, PayrollLineCategory category, Guid? payHeadId)
    {
        var line = v.Lines.Single(l => l.LedgerId == ledgerId && l.Side == side && l.Amount == new Money(amount));
        Assert.True(line.HasPayroll);
        Assert.Equal(category, line.Payroll!.Category);
        if (payHeadId is not null) Assert.Equal(payHeadId, line.Payroll!.PayHeadId);
    }

    // ---------------------------------------------------------------- golden MH man ₹12,000

    [Fact]
    public void Golden_mh_man_12000_deducts_200_reduces_net_and_balances()
    {
        var (c, empId, heads) = Build(MH, basic: 12000m);
        var v = PostMonth(c, empId, 2025, 4); // April

        Assert.True(VoucherValidator.IsBalanced(v));
        Assert.Equal(new Money(12000m), v.TotalDebit);
        Assert.Equal(new Money(12000m), v.TotalCredit);

        AssertLine(v, c.FindLedgerByName("Basic")!.Id, DrCr.Debit, 12000m, PayrollLineCategory.Earning, heads.Basic);
        // PT ₹200 is a deduction (Cr) that REDUCES NET; credited to "Professional Tax".
        AssertLine(v, c.FindLedgerByName("Professional Tax")!.Id, DrCr.Credit, 200m, PayrollLineCategory.Deduction, heads.Pt);
        // Net = gross 12,000 − PT 200 = 11,800.
        AssertLine(v, c.FindLedgerByName(PayrollVoucherService.SalaryPayableLedgerName)!.Id, DrCr.Credit, 11800m, PayrollLineCategory.NetPayable, null);
        // No employer leg for PT.
        Assert.DoesNotContain(v.Lines, l => l.Payroll?.Category == PayrollLineCategory.EmployerContributionExpense);
    }

    [Fact]
    public void Golden_mh_man_12000_charges_300_in_february()
    {
        var (c, empId, heads) = Build(MH, basic: 12000m);
        var feb = PostMonth(c, empId, 2026, 2); // February over-charge
        Assert.True(VoucherValidator.IsBalanced(feb));
        AssertLine(feb, c.FindLedgerByName("Professional Tax")!.Id, DrCr.Credit, 300m, PayrollLineCategory.Deduction, heads.Pt);
        AssertLine(feb, c.FindLedgerByName(PayrollVoucherService.SalaryPayableLedgerName)!.Id, DrCr.Credit, 11700m, PayrollLineCategory.NetPayable, null);
    }

    [Fact]
    public void Mh_man_9000_deducts_175_with_no_february_quirk()
    {
        var (c, empId, heads) = Build(MH, basic: 9000m);
        var apr = PostMonth(c, empId, 2025, 4);
        AssertLine(apr, c.FindLedgerByName("Professional Tax")!.Id, DrCr.Credit, 175m, PayrollLineCategory.Deduction, heads.Pt);
        var feb = PostMonth(c, empId, 2026, 2);
        AssertLine(feb, c.FindLedgerByName("Professional Tax")!.Id, DrCr.Credit, 175m, PayrollLineCategory.Deduction, heads.Pt);
    }

    // ---------------------------------------------------------------- gender: MH woman exempt

    [Fact]
    public void Mh_woman_12000_pays_no_pt()
    {
        var (c, empId, heads) = Build(MH, basic: 12000m, gender: "Female");
        var v = PostMonth(c, empId, 2025, 4);
        Assert.True(VoucherValidator.IsBalanced(v));
        Assert.DoesNotContain(v.Lines, l => l.Payroll?.PayHeadId == heads.Pt); // 0 ⇒ not posted
        AssertLine(v, c.FindLedgerByName(PayrollVoucherService.SalaryPayableLedgerName)!.Id, DrCr.Credit, 12000m, PayrollLineCategory.NetPayable, null);
    }

    // ---------------------------------------------------------------- Karnataka

    [Fact]
    public void Ka_30000_deducts_200_and_ka_20000_is_exempt()
    {
        var (c, empId, heads) = Build(KA, basic: 30000m, gender: null); // KA ignores gender
        AssertLine(PostMonth(c, empId, 2025, 4), c.FindLedgerByName("Professional Tax")!.Id, DrCr.Credit, 200m, PayrollLineCategory.Deduction, heads.Pt);

        var (c2, e2, h2) = Build(KA, basic: 20000m, gender: null);
        var v = PostMonth(c2, e2, 2025, 4);
        Assert.DoesNotContain(v.Lines, l => l.Payroll?.PayHeadId == h2.Pt); // exempt below ₹25,000
    }

    // ---------------------------------------------------------------- West Bengal

    [Fact]
    public void Wb_15000_deducts_110_and_wb_30000_deducts_150()
    {
        var (c, empId, heads) = Build(WB, basic: 15000m, gender: null);
        AssertLine(PostMonth(c, empId, 2025, 4), c.FindLedgerByName("Professional Tax")!.Id, DrCr.Credit, 110m, PayrollLineCategory.Deduction, heads.Pt);

        var (c2, e2, h2) = Build(WB, basic: 30000m, gender: null);
        AssertLine(PostMonth(c2, e2, 2025, 4), c2.FindLedgerByName("Professional Tax")!.Id, DrCr.Credit, 150m, PayrollLineCategory.Deduction, h2.Pt);
    }

    // ---------------------------------------------------------------- ER-13 gating

    [Fact]
    public void Pt_is_inert_until_the_establishment_is_enrolled()
    {
        var c = Seed();
        var pay = new PayrollService(c);
        pay.EnablePayroll(); // NB: PT NOT enrolled
        var ph = new PayHeadService(c);
        var heads = CreateHeads(ph, c);
        var e = pay.CreateEmployee("Ravi Kumar", pay.CreateEmployeeGroup("Staff").Id);
        c.FindEmployee(e.Id)!.Gender = "Male";
        new SalaryStructureService(c).DefineForEmployee(e.Id, new DateOnly(2025, 4, 1), Lines(heads, 12000m));

        var v = PostMonth(c, e.Id, 2025, 4);
        Assert.True(VoucherValidator.IsBalanced(v));
        Assert.DoesNotContain(v.Lines, l => l.Payroll?.PayHeadId == heads.Pt);
        Assert.Null(c.FindLedgerByName("Professional Tax"));
        AssertLine(v, c.FindLedgerByName(PayrollVoucherService.SalaryPayableLedgerName)!.Id, DrCr.Credit, 12000m, PayrollLineCategory.NetPayable, null);
    }

    // ---------------------------------------------------------------- the ₹2,500/year hard cap, posted

    [Fact]
    public void A_full_year_of_posted_pt_totals_exactly_2500_for_the_mh_man()
    {
        // Post all twelve months Apr-2025 … Mar-2026 in sequence; each month's Compute reads the FY-to-date PT from the
        // already-posted vouchers, so the running total is bounded at ₹2,500. MH man ₹12,000: ₹200×11 + ₹300 = ₹2,500.
        var (c, empId, heads) = Build(MH, basic: 12000m);
        decimal total = 0m;
        foreach (var (year, month) in Months())
        {
            var v = PostMonth(c, empId, year, month);
            Assert.True(VoucherValidator.IsBalanced(v));
            var pt = v.Lines.FirstOrDefault(l => l.Payroll?.PayHeadId == heads.Pt && l.Side == DrCr.Credit);
            if (pt is not null) total += pt.Amount.Amount;
        }
        Assert.Equal(2500m, total);
    }

    [Fact]
    public void A_misconfigured_over_2500_slab_is_trimmed_to_2500_across_the_posted_year()
    {
        // Edit the active MH-male slab to a flat ₹250/month (would sum to ₹3,000/yr); the posted year is capped at ₹2,500.
        var (c, empId, heads) = Build(MH, basic: 50000m);
        var mhMale = c.PtConfig!.SlabTables.Single(s => s.StateCode == MH && s.GenderScope == PtGenderScope.Male);
        // Replace the tables with a single flat-₹250 band table for MH-male.
        foreach (var s in c.PtConfig.SlabTables.ToList()) c.PtConfig.RemoveSlabTable(s);
        c.PtConfig.AddSlabTable(new PtSlab(mhMale.Id, MH, PtGenderScope.Male, new[]
        {
            new PtSlabBand(new Money(0m), null, new Money(250m)),
        }));

        decimal total = 0m;
        foreach (var (year, month) in Months())
        {
            var v = PostMonth(c, empId, year, month);
            Assert.True(VoucherValidator.IsBalanced(v));
            var pt = v.Lines.FirstOrDefault(l => l.Payroll?.PayHeadId == heads.Pt && l.Side == DrCr.Credit);
            if (pt is not null) total += pt.Amount.Amount;
        }
        Assert.Equal(2500m, total);
    }

    // ---------------------------------------------------------------- register reconciles to the posting

    [Fact]
    public void The_pt_register_reconciles_to_the_posted_deduction()
    {
        var (c, empId, _) = Build(MH, basic: 12000m);
        var from = new DateOnly(2025, 4, 1);
        var to = new DateOnly(2025, 4, 30);
        var reg = ProfessionalTaxRegister.Build(c, new[] { empId }, from, to);
        Assert.Equal(MH, reg.StateCode);
        Assert.Equal(200L, reg.TotalPt);
        var row = Assert.Single(reg.Rows);
        Assert.Equal(200L, row.ProfessionalTax);
        Assert.Equal(12000L, row.PtWages);
    }

    [Fact]
    public void A_fractional_ptwage_selects_the_band_matching_the_displayed_register_wage()
    {
        // F1: gross ₹10,000.50 → the register displays the wage rounded half-up to ₹10,001, and the band selected must
        // AGREE with that shown wage: >₹10,000 ⇒ the ₹200 band. Flooring would deduct ₹175 while displaying ₹10,001 —
        // an inconsistent one-band under-deduction.
        var (c, empId, _) = Build(MH, basic: 10000.50m);
        var reg = ProfessionalTaxRegister.Build(c, new[] { empId }, new DateOnly(2025, 4, 1), new DateOnly(2025, 4, 30));
        var row = Assert.Single(reg.Rows);
        Assert.Equal(10001L, row.PtWages);        // the displayed PT-wage (half-up)
        Assert.Equal(200L, row.ProfessionalTax);  // the band that wage selects — consistent with the shown wage
    }

    private static IEnumerable<(int Year, int Month)> Months()
    {
        var d = new DateOnly(2025, 4, 1);
        for (var i = 0; i < 12; i++)
        {
            yield return (d.Year, d.Month);
            d = d.AddMonths(1);
        }
    }
}
