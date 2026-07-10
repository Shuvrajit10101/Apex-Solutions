using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Reports;
using Apex.Ledger.Services;
using Xunit;
using Domain = Apex.Ledger.Domain;

namespace Apex.Ledger.Tests;

/// <summary>
/// Phase 7 slice 6 — the TCS <b>deposit</b> flow (<see cref="TcsDepositService"/>) + <b>TCS Challan Reconciliation</b>
/// (<see cref="TcsChallanReconciliation"/>). The exact mirror of the TDS deposit (slice 3), for the collector's side:
/// after a scrap sale collects ₹1,180 TCS (1% of the ₹1,18,000 GST-inclusive base) the "TCS Payable" liability is
/// ₹1,180; a Stat Payment (a Payment voucher, Ctrl+F, marked <see cref="VoucherType.IsStatPayment"/>) of ₹1,180
/// debits TCS Payable and credits Bank, zeroing the liability; an ITNS-281 challan is recorded + linked (keyed on the
/// §206C collection code); and the reconciliation matches deposit to collection (remaining 0). A cancelled
/// stat-payment drops the deposit (the slice-3 fix). The Stat Payment reuses the Payment base type — no new
/// VoucherBaseType — and is a distinct type from the TDS Stat Payment.
/// </summary>
public class TcsDepositServiceTests
{
    private const string ValidTan = "MUMA12345B";
    private const string BuyerPan = "AAQCS1234K";
    private const string GstinMaharashtra = "27AAPFU0939F1ZV";

    private static readonly DateOnly Fy = new(2025, 4, 1);
    private static readonly DateOnly D1 = new(2025, 5, 10);
    private static readonly DateOnly Deposit = new(2025, 6, 5);
    private static readonly DateOnly Q1End = new(2025, 6, 30);

    private static Company NewTcsCompany()
    {
        var c = CompanyFactory.CreateSeeded("Collecting Co", Fy);
        new TdsTcsService(c).EnableTcs(new TcsConfig { Tan = ValidTan });
        new GstService(c).EnableGst(new GstConfig
        {
            HomeStateCode = "27", Gstin = GstinMaharashtra, RegistrationType = GstRegistrationType.Regular,
            ApplicableFrom = Fy, Periodicity = GstReturnPeriodicity.Monthly,
        });
        return c;
    }

    private static Domain.Ledger AddLedger(Company c, string name, string groupName, bool openingIsDebit)
    {
        var l = new Domain.Ledger(Guid.NewGuid(), name, c.FindGroupByName(groupName)!.Id, Money.Zero, openingIsDebit);
        c.AddLedger(l);
        return l;
    }

    private static Domain.Ledger Buyer(Company c)
    {
        var b = AddLedger(c, "Scrap Buyer", "Sundry Debtors", true);
        b.PartyGst = new PartyGstDetails { RegistrationType = GstRegistrationType.Regular, Gstin = GstinMaharashtra, StateCode = "27" };
        b.TcsApplicable = true; b.CollecteeType = CollecteeType.Individual; b.PartyPan = BuyerPan;
        return b;
    }

    /// <summary>Posts the golden scrap sale (1000 Kg @ ₹100 = ₹1,00,000 + 18% GST → 1% TCS on ₹1,18,000 = ₹1,180)
    /// and returns the company + the "TCS Payable" ledger holding the ₹1,180 credit.</summary>
    private static (Company Company, Domain.Ledger Payable) CollectElevenEighty(Company c)
    {
        var gst = new GstService(c);
        var post = new LedgerService(c);
        var inv = new InventoryService(c);

        var scrap = inv.CreateStockItem("Scrap Metal", inv.CreateStockGroup("Waste").Id, inv.CreateSimpleUnit("Kg", "Kilogram").Id);
        scrap.Gst = new StockItemGstDetails { Taxability = GstTaxability.Taxable, RateBasisPoints = 1800 };
        scrap.TcsNatureOfGoodsId = c.FindNatureOfGoodsByCode("6CE")!.Id;
        var main = c.MainLocation!.Id;

        var sales = AddLedger(c, "Scrap Sales", "Sales Accounts", false);
        var buyer = Buyer(c);

        // Buy first so there is stock to sell.
        var purchases = AddLedger(c, "Purchases", "Purchase Accounts", true);
        var creditor = AddLedger(c, "Creditor", "Sundry Creditors", false);
        post.Post(new Voucher(Guid.NewGuid(), c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Purchase).Id, D1,
            new[] { new EntryLine(purchases.Id, Money.FromRupees(50_000m), DrCr.Debit), new EntryLine(creditor.Id, Money.FromRupees(50_000m), DrCr.Credit) },
            inventoryLines: new[] { new VoucherInventoryLine(scrap.Id, main, 1000m, Money.FromRupees(50m)) }));

        var value = Money.FromRupees(1_00_000m);
        var interState = gst.IsInterState(buyer.PartyGst!.StateCode);
        var tax = gst.ComputeInvoiceTax(new[] { new GstService.TaxableLine(value, 1800) }, interState, GstTaxDirection.Output);
        var nature = new TcsService(c).ResolveNature(scrap, sales)!;
        var col = new TcsService(c).BuildCollection(value, tax.TotalTax, nature, buyer, D1);
        Assert.Equal(Money.FromRupees(1_180m), col.TcsAmount);

        var partyTotal = value + tax.TotalTax + col.TcsAmount; // ₹1,19,180
        var lines = new List<EntryLine>
        {
            new(buyer.Id, partyTotal, DrCr.Debit),
            new(sales.Id, value, DrCr.Credit),
        };
        lines.AddRange(tax.TaxLines);
        lines.Add(col.TcsPayableLine!);
        post.Post(new Voucher(Guid.NewGuid(), c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Sales).Id, D1, lines,
            inventoryLines: new[] { new VoucherInventoryLine(scrap.Id, main, 1000m, Money.FromRupees(100m)) }));

        return (c, new TcsService(c).RequirePayableLedger());
    }

    // ---- Stat Payment reuses the Payment base type via the flag (no new VoucherBaseType); distinct from TDS ----

    [Fact]
    public void Ensure_stat_payment_type_is_a_payment_base_flagged_and_idempotent()
    {
        var c = NewTcsCompany();
        var dep = new TcsDepositService(c);
        var t1 = dep.EnsureStatPaymentType();

        Assert.Equal(VoucherBaseType.Payment, t1.BaseType);   // reuses Payment base — no invented base type
        Assert.True(t1.IsStatPayment);
        Assert.True(t1.IsStatPaymentType);
        Assert.Equal(TcsDepositService.StatPaymentTypeName, t1.Name);

        var t2 = dep.EnsureStatPaymentType();
        Assert.Same(t1, t2);                                   // idempotent — never duplicated
        Assert.Single(c.VoucherTypes, t => t.IsStatPaymentType && t.Name == TcsDepositService.StatPaymentTypeName);
    }

    [Fact]
    public void Tcs_stat_payment_is_a_distinct_type_from_the_tds_stat_payment()
    {
        var c = CompanyFactory.CreateSeeded("Both Co", Fy);
        new TdsTcsService(c).EnableTds(new TdsConfig { Tan = ValidTan });
        new TdsTcsService(c).EnableTcs(new TcsConfig { Tan = ValidTan });

        var tds = new TdsDepositService(c).EnsureStatPaymentType();
        var tcs = new TcsDepositService(c).EnsureStatPaymentType();

        Assert.NotSame(tds, tcs);
        Assert.NotEqual(tds.Id, tcs.Id);
        Assert.Equal("TDS Stat Payment", tds.Name);
        Assert.Equal("TCS Stat Payment", tcs.Name);
        // Two distinct stat-payment types coexist (one per tax).
        Assert.Equal(2, c.VoucherTypes.Count(t => t.IsStatPaymentType));
    }

    // ---- the golden worked example: deposit zeroes the payable; challan recorded + reconciled ----

    [Fact]
    public void Stat_payment_of_eleven_eighty_zeroes_the_tcs_payable_liability()
    {
        var (c, payable) = CollectElevenEighty(NewTcsCompany());

        // The liability accrued ₹1,180 (credit balance → −1,180 signed).
        Assert.Equal(-1_180m, LedgerBalances.SignedClosing(c, payable, D1));
        Assert.Equal(Money.FromRupees(1_180m), new TcsDepositService(c).OutstandingPayable(D1));

        var dep = new TcsDepositService(c);
        var statType = dep.EnsureStatPaymentType();
        var bank = AddLedger(c, "HDFC Bank", "Bank Accounts", true);
        var pay = dep.BuildStatPayment(Money.FromRupees(1_180m), bank, Deposit, statType);
        var posted = new LedgerService(c).Post(pay);

        Assert.True(VoucherValidator.IsBalanced(posted));
        // Dr TCS Payable 1,180 / Cr Bank 1,180 — the liability is discharged to zero.
        Assert.Equal(0m, LedgerBalances.SignedClosing(c, payable, Q1End));
        Assert.Equal(Money.Zero, dep.OutstandingPayable(Q1End));
    }

    [Fact]
    public void Challan_is_recorded_and_linked_to_the_stat_payment_voucher()
    {
        var (c, _) = CollectElevenEighty(NewTcsCompany());
        var dep = new TcsDepositService(c);
        var statType = dep.EnsureStatPaymentType();
        var bank = AddLedger(c, "HDFC Bank", "Bank Accounts", true);
        var posted = new LedgerService(c).Post(dep.BuildStatPayment(Money.FromRupees(1_180m), bank, Deposit, statType));

        var challan = dep.RecordChallan("00777", "0510308", Deposit, Money.FromRupees(1_180m), "6CE", "200", posted);

        var stored = Assert.Single(c.TcsChallans);
        Assert.Equal("00777", stored.ChallanNo);
        Assert.Equal("0510308", stored.BsrCode);
        Assert.Equal(Money.FromRupees(1_180m), stored.Amount);
        Assert.Equal("6CE", stored.CollectionCode);
        Assert.Equal("200", stored.MinorHead);
        // The challan is linked to the stat-payment voucher (both directions resolve).
        Assert.Contains(posted.Id, c.VouchersLinkedToTcsChallan(challan.Id));
        Assert.Contains(challan.Id, c.TcsChallansLinkedToVoucher(posted.Id));
    }

    [Fact]
    public void Reconciliation_matches_the_deposit_to_the_collection_remaining_zero()
    {
        var (c, _) = CollectElevenEighty(NewTcsCompany());
        var dep = new TcsDepositService(c);
        var statType = dep.EnsureStatPaymentType();
        var bank = AddLedger(c, "HDFC Bank", "Bank Accounts", true);
        var posted = new LedgerService(c).Post(dep.BuildStatPayment(Money.FromRupees(1_180m), bank, Deposit, statType));
        dep.RecordChallan("00777", "0510308", Deposit, Money.FromRupees(1_180m), "6CE", "200", posted);

        // Reconcile over Q1 (the collection on 10-May + the deposit on 5-Jun both fall in the window).
        var recon = TcsChallanReconciliation.Build(c, Fy, Q1End);
        var row = Assert.Single(recon.Codes);
        Assert.Equal("6CE", row.CollectionCode);
        Assert.Equal(Money.FromRupees(1_180m), row.Collected);
        Assert.Equal(Money.FromRupees(1_180m), row.Deposited);
        Assert.Equal(Money.Zero, row.Remaining);
        Assert.True(row.IsMatched);
        Assert.True(recon.IsFullyReconciled);
        Assert.Equal(Money.Zero, recon.TotalRemaining);
        Assert.Empty(recon.Unmatched);
    }

    [Fact]
    public void Reconciliation_flags_a_partial_deposit_as_underpaid()
    {
        var (c, _) = CollectElevenEighty(NewTcsCompany());
        var dep = new TcsDepositService(c);
        var statType = dep.EnsureStatPaymentType();
        var bank = AddLedger(c, "HDFC Bank", "Bank Accounts", true);
        // Deposit only ₹800 of the ₹1,180 due.
        var posted = new LedgerService(c).Post(dep.BuildStatPayment(Money.FromRupees(800m), bank, Deposit, statType));
        dep.RecordChallan("00777", "0510308", Deposit, Money.FromRupees(800m), "6CE", "200", posted);

        var recon = TcsChallanReconciliation.Build(c, Fy, Q1End);
        var row = Assert.Single(recon.Codes);
        Assert.Equal(Money.FromRupees(1_180m), row.Collected);
        Assert.Equal(Money.FromRupees(800m), row.Deposited);
        Assert.Equal(Money.FromRupees(380m), row.Remaining);   // ₹380 still payable
        Assert.False(row.IsMatched);
        Assert.True(row.IsUnderpaid);
        Assert.False(recon.IsFullyReconciled);
        Assert.Single(recon.Unmatched);
        // The outstanding ledger balance agrees with the reconciliation remaining.
        Assert.Equal(Money.FromRupees(380m), dep.OutstandingPayable(Q1End));
    }

    [Fact]
    public void Reconciliation_drops_a_challan_whose_stat_payment_voucher_was_cancelled()
    {
        var (c, _) = CollectElevenEighty(NewTcsCompany());
        var dep = new TcsDepositService(c);
        var statType = dep.EnsureStatPaymentType();
        var bank = AddLedger(c, "HDFC Bank", "Bank Accounts", true);
        var posted = new LedgerService(c).Post(dep.BuildStatPayment(Money.FromRupees(1_180m), bank, Deposit, statType));
        dep.RecordChallan("00777", "0510308", Deposit, Money.FromRupees(1_180m), "6CE", "200", posted);

        // Before cancelling: the deposit discharges the collection (matched, nothing owed).
        Assert.True(Assert.Single(TcsChallanReconciliation.Build(c, Fy, Q1End).Codes).IsMatched);

        // Cancel the Stat-Payment voucher — its Dr "TCS Payable" leg is reversed off the ledger, so the money is
        // owed again. The collected side + the ledger balance both skip cancelled vouchers; the deposited side must too.
        c.FindVoucher(posted.Id)!.Cancelled = true;
        Assert.Equal(Money.FromRupees(1_180m), dep.OutstandingPayable(Q1End)); // liability restored

        var recon = TcsChallanReconciliation.Build(c, Fy, Q1End);
        var row = Assert.Single(recon.Codes);
        Assert.Equal(Money.FromRupees(1_180m), row.Collected);
        Assert.Equal(Money.Zero, row.Deposited);           // the cancelled challan no longer counts as deposited
        Assert.Equal(Money.FromRupees(1_180m), row.Remaining);
        Assert.True(row.IsUnderpaid);
        Assert.False(recon.IsFullyReconciled);
    }

    [Fact]
    public void Reconciliation_flags_an_orphan_deposit_with_no_collection_as_overpaid()
    {
        var c = NewTcsCompany();
        // No collection at all — a stray challan deposit.
        var dep = new TcsDepositService(c);
        var statType = dep.EnsureStatPaymentType();
        var bank = AddLedger(c, "HDFC Bank", "Bank Accounts", true);
        var posted = new LedgerService(c).Post(dep.BuildStatPayment(Money.FromRupees(500m), bank, Deposit, statType));
        dep.RecordChallan("00999", "0510308", Deposit, Money.FromRupees(500m), "6CA", "200", posted);

        var recon = TcsChallanReconciliation.Build(c, Fy, Q1End);
        var row = Assert.Single(recon.Codes);
        Assert.Equal("6CA", row.CollectionCode);
        Assert.Equal(Money.Zero, row.Collected);
        Assert.Equal(Money.FromRupees(500m), row.Deposited);
        Assert.Equal(Money.FromRupees(-500m), row.Remaining);
        Assert.True(row.IsOverpaid);
        Assert.False(recon.IsFullyReconciled);
    }

    // ---- ER-13: a company that never collects TCS has an empty reconciliation ----

    [Fact]
    public void A_company_with_no_tcs_activity_reconciles_empty()
    {
        var c = NewTcsCompany();
        var recon = TcsChallanReconciliation.Build(c, Fy, Q1End);
        Assert.Empty(recon.Codes);
        Assert.True(recon.IsFullyReconciled);
        Assert.Equal(Money.Zero, recon.TotalCollected);
        Assert.Equal(Money.Zero, recon.TotalDeposited);
    }

    // ---- requires TCS enabled before building the deposit legs ----

    [Fact]
    public void Requires_tcs_enabled_before_depositing()
    {
        var c = CompanyFactory.CreateSeeded("No-TCS Co", Fy); // TCS not enabled
        var dep = new TcsDepositService(c);
        var statType = dep.EnsureStatPaymentType();
        var bank = AddLedger(c, "HDFC Bank", "Bank Accounts", true);
        Assert.Throws<InvalidOperationException>(() => dep.BuildStatPayment(Money.FromRupees(1_180m), bank, Deposit, statType));
    }
}
