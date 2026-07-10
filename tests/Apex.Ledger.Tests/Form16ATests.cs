using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Reports;
using Apex.Ledger.Services;
using Xunit;
using Domain = Apex.Ledger.Domain;

namespace Apex.Ledger.Tests;

/// <summary>
/// Phase 7 slice 7 — <b>Form 16A</b> (<see cref="Form16A"/>), the per-deductee-per-quarter TDS certificate, as a pure
/// projection over the same posted withholdings + challans that drive <see cref="Form26Q"/>. Proves the golden worked
/// example (a 194J ₹1,00,000 deduction, ₹10,000 TDS at 10%, plus its stat-payment challan) yields a certificate whose
/// deductor block (TAN/name), deductee block (PAN/name), quarter TDS summary (amount paid / TDS / rate / section
/// 94J-B) and challan/deposit block match the corresponding <see cref="Form26Q"/> row <b>to the paisa, by
/// construction</b>; and ER-13 (no TDS ⇒ an empty certificate).
/// </summary>
public class Form16ATests
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

    private static TdsChallan Deposit(Company c, Money amount, DateOnly on, string challanNo)
    {
        var dep = new TdsDepositService(c);
        var statType = dep.EnsureStatPaymentType();
        var bank = c.FindLedgerByName("HDFC Bank") ?? AddLedger(c, "HDFC Bank", "Bank Accounts", true);
        var posted = new LedgerService(c).Post(dep.BuildStatPayment(amount, bank, on, statType));
        return dep.RecordChallan(challanNo, "0510308", on, amount, "194J(b)", "200", posted);
    }

    [Fact]
    public void Golden_certificate_matches_the_26Q_deductee_row_and_challan()
    {
        var c = NewTdsCompany(FyStart);
        var vendor = Vendor(c);
        BookDeduction(c, new DateOnly(2025, 5, 10), vendor);
        Deposit(c, Money.FromRupees(10_000m), new DateOnly(2025, 6, 5), "00123");

        var q1 = Form26Q.Build(c, 2025, 1);
        var cert = Form16A.Build(c, 2025, 1, vendor.Id);

        // Deductor block (TAN + name from F11 / company).
        Assert.Equal(ValidTan, cert.Deductor.Tan);
        Assert.Equal("Return Co", cert.Deductor.Name);
        Assert.Equal(DeductorType.Company, cert.Deductor.DeductorType);
        Assert.Equal("2025-26", cert.FinancialYearLabel);
        Assert.Equal("Q1", cert.QuarterLabel);

        // Deductee block.
        Assert.Equal(vendor.Id, cert.Deductee.LedgerId);
        Assert.Equal("Consultant", cert.Deductee.Name);
        Assert.Equal(DeducteePan, cert.Deductee.Pan);

        // The quarter TDS summary row equals the 26Q deductee row (amount / TDS / rate / section / FVU code).
        var q26 = Assert.Single(q1.Deductees);
        var row = Assert.Single(cert.Deductions);
        Assert.Equal(q26.SectionCode, row.SectionCode);
        Assert.Equal("94J-B", row.FvuSectionCode);
        Assert.Equal(q26.DeductionDate, row.Date);
        Assert.Equal(q26.AmountPaid, row.AmountPaid);
        Assert.Equal(q26.TdsAmount, row.TdsAmount);
        Assert.Equal(q26.RateBasisPoints, row.RateBasisPoints);
        Assert.Equal(10.00m, row.RatePercent);
        Assert.True(row.PanApplied);

        // The challan/deposit block matches the 26Q challan (BSR / serial / date / TDS deposited for this deductee).
        var ch26 = Assert.Single(q1.Challans);
        var ch = Assert.Single(cert.Challans);
        Assert.Equal(ch26.ChallanNo, ch.ChallanNo);
        Assert.Equal(ch26.BsrCode, ch.BsrCode);
        Assert.Equal(ch26.DepositDate, ch.DepositDate);
        Assert.Equal(Money.FromRupees(10_000m), ch.TdsDeposited);

        // Totals.
        Assert.Equal(Money.FromRupees(1_00_000m), cert.TotalAmountPaid);
        Assert.Equal(Money.FromRupees(10_000m), cert.TotalTdsDeducted);
        Assert.Equal(Money.FromRupees(10_000m), cert.TotalTdsDeposited);
        Assert.False(cert.IsEmpty);
    }

    [Fact]
    public void BuildAll_emits_one_certificate_per_deductee()
    {
        var c = NewTdsCompany(FyStart);
        var v1 = Vendor(c, "Consultant A");
        var v2 = Vendor(c, "Consultant B");
        BookDeduction(c, new DateOnly(2025, 5, 10), v1);
        BookDeduction(c, new DateOnly(2025, 5, 12), v2);

        var certs = Form16A.BuildAll(c, 2025, 1);
        Assert.Equal(2, certs.Count);
        Assert.Contains(certs, x => x.Deductee.LedgerId == v1.Id);
        Assert.Contains(certs, x => x.Deductee.LedgerId == v2.Id);
        // Each certificate carries only its own deductee's deduction.
        Assert.All(certs, x => Assert.Single(x.Deductions));
    }

    [Fact]
    public void No_TDS_yields_an_empty_certificate_er13()
    {
        var c = NewTdsCompany(FyStart);
        var vendor = Vendor(c);
        // No deduction booked at all.

        var cert = Form16A.Build(c, 2025, 1, vendor.Id);
        Assert.True(cert.IsEmpty);
        Assert.Empty(cert.Deductions);
        Assert.Empty(cert.Challans);
        Assert.Equal(Money.Zero, cert.TotalTdsDeducted);

        // And there are no certificates to issue for the quarter.
        Assert.Empty(Form16A.BuildAll(c, 2025, 1));
    }
}
