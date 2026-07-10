using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Reports;
using Apex.Ledger.Services;
using Xunit;
using Domain = Apex.Ledger.Domain;

namespace Apex.Ledger.Tests;

/// <summary>
/// Phase 7 slice 7 — <b>Form 27A</b> (<see cref="Form27A"/>), the return <b>control chart</b> that a filer cross-checks
/// before FVU validation. Proves the control totals (deductee/collectee record count + total tax + total amount)
/// projected from a <see cref="Form26Q"/> / <see cref="Form27EQ"/> tally with the return by construction, and that a
/// mismatch (an undeposited deduction) is surfaced as a validation message.
/// </summary>
public class Form27ATests
{
    private const string ValidTan = "MUMA12345B";
    private const string DeducteePan = "AAPFU0939F";
    private static readonly DateOnly FyStart = new(2025, 4, 1);

    private static Company NewTdsCompany(DateOnly booksFrom)
    {
        var c = CompanyFactory.CreateSeeded("Return Co", booksFrom);
        new TdsTcsService(c).EnableTds(new TdsConfig
        {
            Tan = ValidTan, DeductorType = DeductorType.Company,
            ResponsiblePersonName = "A. Sharma", ResponsiblePersonPan = DeducteePan,
            ResponsiblePersonDesignation = "Director", ResponsiblePersonAddress = "12 MG Road",
        });
        return c;
    }

    private static Domain.Ledger AddLedger(Company c, string name, string groupName, bool openingIsDebit)
    {
        var l = new Domain.Ledger(Guid.NewGuid(), name, c.FindGroupByName(groupName)!.Id, Money.Zero, openingIsDebit);
        c.AddLedger(l);
        return l;
    }

    private static Domain.Ledger Vendor(Company c, string name = "Consultant")
    {
        var nop = c.FindNatureOfPaymentByCode("194J(b)")!;
        var v = AddLedger(c, name, "Sundry Creditors", false);
        v.TdsApplicable = true; v.TdsNatureOfPaymentId = nop.Id; v.DeducteeType = DeducteeType.Firm; v.PartyPan = DeducteePan;
        return v;
    }

    private static Guid JournalTypeId(Company c) => c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Journal).Id;

    private static void BookDeduction(Company c, DateOnly on, Domain.Ledger vendor)
    {
        var fees = AddLedger(c, $"Professional Fees {on:yyyyMMdd}", "Indirect Expenses", true);
        var nop = c.FindNatureOfPaymentByCode("194J(b)")!;
        var gross = Money.FromRupees(1_00_000m);
        var carve = new TdsService(c).BuildCarveOut(gross, gross, nop, vendor, on);
        new LedgerService(c).Post(new Voucher(Guid.NewGuid(), JournalTypeId(c), on,
            new[] { new EntryLine(fees.Id, gross, DrCr.Debit), carve.PartyLine, carve.TdsPayableLine! }));
    }

    private static void Deposit(Company c, Money amount, DateOnly on, string challanNo)
    {
        var dep = new TdsDepositService(c);
        var statType = dep.EnsureStatPaymentType();
        var bank = c.FindLedgerByName("HDFC Bank") ?? AddLedger(c, "HDFC Bank", "Bank Accounts", true);
        var posted = new LedgerService(c).Post(dep.BuildStatPayment(amount, bank, on, statType));
        dep.RecordChallan(challanNo, "0510308", on, amount, "194J(b)", "200", posted);
    }

    [Fact]
    public void Control_chart_from_26Q_tallies_with_the_return()
    {
        var c = NewTdsCompany(FyStart);
        var vendor = Vendor(c);
        BookDeduction(c, new DateOnly(2025, 5, 10), vendor);
        Deposit(c, Money.FromRupees(10_000m), new DateOnly(2025, 6, 5), "00123");

        var q1 = Form26Q.Build(c, 2025, 1);
        var chart = Form27A.FromForm26Q(q1);

        Assert.Equal("26Q", chart.ReturnFormName);
        Assert.Equal(ValidTan, chart.Tan);
        Assert.Equal("2025-26", chart.FinancialYearLabel);
        Assert.Equal("Q1", chart.QuarterLabel);
        Assert.Equal(1, chart.DeducteeRecordCount);
        Assert.Equal(1, chart.ChallanRecordCount);
        Assert.Equal(Money.FromRupees(10_000m), chart.TotalTax);
        Assert.Equal(Money.FromRupees(1_00_000m), chart.TotalAmount);
        Assert.Equal(Money.FromRupees(10_000m), chart.TotalDeposited);

        // Cross-check: the chart tallies exactly with the return's own control-total validation.
        Assert.True(chart.Tallies);
        Assert.Empty(chart.ControlValidationMessages);
        Assert.Equal(q1.ControlTotals.Tallies, chart.Tallies);
    }

    [Fact]
    public void Control_chart_surfaces_an_undeposited_deduction()
    {
        var c = NewTdsCompany(FyStart);
        var vendor = Vendor(c);
        BookDeduction(c, new DateOnly(2025, 5, 10), vendor);
        // No deposit.

        var q1 = Form26Q.Build(c, 2025, 1);
        var chart = Form27A.FromForm26Q(q1);
        Assert.False(chart.Tallies);
        Assert.Contains("undeposited", Assert.Single(chart.ControlValidationMessages));
    }

    [Fact]
    public void Control_chart_from_an_empty_27EQ_is_inert()
    {
        var c = CompanyFactory.CreateSeeded("Collecting Co", FyStart);
        new TdsTcsService(c).EnableTcs(new TcsConfig { Tan = ValidTan, CollectorType = DeductorType.Company });
        var q1 = Form27EQ.Build(c, 2025, 1);
        var chart = Form27A.FromForm27EQ(q1);

        Assert.Equal("27EQ", chart.ReturnFormName);
        Assert.Equal(0, chart.DeducteeRecordCount);
        Assert.Equal(0, chart.ChallanRecordCount);
        Assert.Equal(Money.Zero, chart.TotalTax);
        Assert.Equal(Money.Zero, chart.TotalAmount);
        Assert.True(chart.Tallies);
    }
}
