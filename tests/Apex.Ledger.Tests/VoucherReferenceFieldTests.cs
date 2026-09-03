using Apex.Ledger.Domain;
using Apex.Ledger.Services;
using Xunit;
using Domain = Apex.Ledger.Domain;

namespace Apex.Ledger.Tests;

/// <summary>
/// Numbering slice S5 (numbering-design-v2 §8) — the <b>counterparty captured field</b> boundary. The reference is
/// the OTHER party's document number: it is free text that receives NO auto prefix/suffix/width/numbering (that is
/// our own <see cref="Voucher.Number"/>). This pins that boundary at the engine: posting a voucher under a
/// fully-affixed voucher type leaves the captured <see cref="Voucher.ReferenceNo"/>/<see cref="Voucher.ReferenceDate"/>
/// untouched, while our own number renders the affix — the two are independent.
/// </summary>
public sealed class VoucherReferenceFieldTests
{
    private static readonly DateOnly FyStart = new(2025, 4, 1);

    [Fact]
    public void ReferenceNo_notRunThroughNumbering()
    {
        var c = CompanyFactory.CreateSeeded("Ref Boundary Co", FyStart);
        var sales = new Domain.Ledger(Guid.NewGuid(), "Sales", c.FindGroupByName("Sales Accounts")!.Id,
            Money.Zero, openingIsDebit: false);
        c.AddLedger(sales);
        var debtor = new Domain.Ledger(Guid.NewGuid(), "Acme Ltd", c.FindGroupByName("Sundry Debtors")!.Id,
            Money.Zero, openingIsDebit: true);
        c.AddLedger(debtor);

        // A FULLY-affixed Sales type: prefix "25-26/", width 3, prefill on ⇒ our own number renders "25-26/001".
        var affixSales = new VoucherType(Guid.NewGuid(), "Affix Sales", VoucherBaseType.Sales,
            numberWidth: 3, prefillWithZero: true,
            prefixes: new[] { new VoucherNumberAffix(Guid.NewGuid(), FyStart, "25-26/") });
        c.AddVoucherType(affixSales);

        var posted = new LedgerService(c).Post(new Voucher(Guid.NewGuid(), affixSales.Id, new DateOnly(2025, 4, 10),
            new[]
            {
                new EntryLine(debtor.Id, Money.FromRupees(1000m), DrCr.Debit),
                new EntryLine(sales.Id, Money.FromRupees(1000m), DrCr.Credit),
            }, partyId: debtor.Id, referenceNo: "ABC/123", referenceDate: new DateOnly(2025, 4, 8)));

        // Our own number is auto-assigned and renders the affix.
        Assert.Equal(1, posted.Number);
        Assert.Equal("25-26/001", c.FormatVoucherNumber(posted));

        // The counterparty reference is UNTOUCHED — exactly the typed value, no prefix, no pad, no rendering.
        Assert.Equal("ABC/123", posted.ReferenceNo);
        Assert.Equal(new DateOnly(2025, 4, 8), posted.ReferenceDate);

        // It is NOT our rendered number, and the affix was never applied to it (the mutation — running the reference
        // through VoucherNumberFormatter — would make one of these fail).
        Assert.NotEqual(c.FormatVoucherNumber(posted), posted.ReferenceNo);
        Assert.DoesNotContain("25-26/", posted.ReferenceNo);
    }
}
