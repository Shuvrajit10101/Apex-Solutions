using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Reports;
using Apex.Ledger.Services;
using Xunit;
using Domain = Apex.Ledger.Domain;

namespace Apex.Ledger.Tests;

/// <summary>
/// Phase 7 slice 3 — the TDS <b>deposit</b> flow (<see cref="TdsDepositService"/>) + <b>Challan Reconciliation</b>
/// (<see cref="ChallanReconciliation"/>). Proves the golden worked example numerically: after a 194J ₹10,000
/// deduction the "TDS Payable" liability is ₹10,000; a Stat Payment (a Payment voucher, Ctrl+F, marked
/// <see cref="VoucherType.IsStatPayment"/>) of ₹10,000 debits TDS Payable and credits Bank, zeroing the liability;
/// an ITNS-281 challan is recorded + linked; and the reconciliation matches deposit to deduction (remaining 0). An
/// unmatched/partial case is flagged. The Stat Payment reuses the Payment base type — no new VoucherBaseType.
/// </summary>
public class TdsDepositServiceTests
{
    private const string ValidTan = "MUMA12345B";
    private const string DeducteePan = "AAPFU0939F";

    private static readonly DateOnly Fy = new(2025, 4, 1);
    private static readonly DateOnly Deduct = new(2025, 5, 10);
    private static readonly DateOnly Deposit = new(2025, 6, 5);
    private static readonly DateOnly Q1End = new(2025, 6, 30);

    private static Company NewTdsCompany()
    {
        var c = CompanyFactory.CreateSeeded("Deposit Co", Fy);
        new TdsTcsService(c).EnableTds(new TdsConfig { Tan = ValidTan });
        return c;
    }

    private static Domain.Ledger AddLedger(Company c, string name, string groupName, bool openingIsDebit)
    {
        var l = new Domain.Ledger(Guid.NewGuid(), name, c.FindGroupByName(groupName)!.Id, Money.Zero, openingIsDebit);
        c.AddLedger(l);
        return l;
    }

    private static Domain.Ledger Vendor(Company c)
    {
        var nop = c.FindNatureOfPaymentByCode("194J(b)")!;
        var v = AddLedger(c, "Consultant", "Sundry Creditors", false);
        v.TdsApplicable = true; v.TdsNatureOfPaymentId = nop.Id; v.DeducteeType = DeducteeType.Firm; v.PartyPan = DeducteePan;
        return v;
    }

    private static Guid JournalTypeId(Company c) => c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Journal).Id;

    /// <summary>Books the 194J ₹1,00,000 deduction (₹10,000 TDS) and returns the vendor + payable ledgers.</summary>
    private static (Company Company, Domain.Ledger Payable) DeductTenThousand(Company c)
    {
        var fees = AddLedger(c, "Professional Fees", "Indirect Expenses", true);
        var vendor = Vendor(c);
        var nop = c.FindNatureOfPaymentByCode("194J(b)")!;
        var gross = Money.FromRupees(1_00_000m);
        var carve = new TdsService(c).BuildCarveOut(gross, gross, nop, vendor, Deduct);
        new LedgerService(c).Post(new Voucher(Guid.NewGuid(), JournalTypeId(c), Deduct,
            new[] { new EntryLine(fees.Id, gross, DrCr.Debit), carve.PartyLine, carve.TdsPayableLine! }));
        return (c, new TdsService(c).RequirePayableLedger());
    }

    // ---- Stat Payment reuses the Payment base type via the flag (no new VoucherBaseType) ----

    [Fact]
    public void Ensure_stat_payment_type_is_a_payment_base_flagged_and_idempotent()
    {
        var c = NewTdsCompany();
        var dep = new TdsDepositService(c);
        var t1 = dep.EnsureStatPaymentType();

        Assert.Equal(VoucherBaseType.Payment, t1.BaseType);   // reuses Payment base — no invented base type
        Assert.True(t1.IsStatPayment);
        Assert.True(t1.IsStatPaymentType);

        var t2 = dep.EnsureStatPaymentType();
        Assert.Same(t1, t2);                                   // idempotent — never duplicated
        Assert.Single(c.VoucherTypes, t => t.IsStatPaymentType);
    }

    // ---- the golden worked example: deposit zeroes the payable; challan recorded + reconciled ----

    [Fact]
    public void Stat_payment_of_ten_thousand_zeroes_the_tds_payable_liability()
    {
        var (c, payable) = DeductTenThousand(NewTdsCompany());

        // The liability accrued ₹10,000 (credit balance → −10,000 signed).
        Assert.Equal(-10_000m, LedgerBalances.SignedClosing(c, payable, Deduct));
        Assert.Equal(Money.FromRupees(10_000m), new TdsDepositService(c).OutstandingPayable(Deduct));

        var dep = new TdsDepositService(c);
        var statType = dep.EnsureStatPaymentType();
        var bank = AddLedger(c, "HDFC Bank", "Bank Accounts", true);
        var pay = dep.BuildStatPayment(Money.FromRupees(10_000m), bank, Deposit, statType);
        var posted = new LedgerService(c).Post(pay);

        Assert.True(VoucherValidator.IsBalanced(posted));
        // Dr TDS Payable 10,000 / Cr Bank 10,000 — the liability is discharged to zero.
        Assert.Equal(0m, LedgerBalances.SignedClosing(c, payable, Q1End));
        Assert.Equal(Money.Zero, dep.OutstandingPayable(Q1End));
    }

    [Fact]
    public void Challan_is_recorded_and_linked_to_the_stat_payment_voucher()
    {
        var (c, _) = DeductTenThousand(NewTdsCompany());
        var dep = new TdsDepositService(c);
        var statType = dep.EnsureStatPaymentType();
        var bank = AddLedger(c, "HDFC Bank", "Bank Accounts", true);
        var posted = new LedgerService(c).Post(dep.BuildStatPayment(Money.FromRupees(10_000m), bank, Deposit, statType));

        var challan = dep.RecordChallan("00123", "0510308", Deposit, Money.FromRupees(10_000m), "194J(b)", "200", posted);

        var stored = Assert.Single(c.TdsChallans);
        Assert.Equal("00123", stored.ChallanNo);
        Assert.Equal("0510308", stored.BsrCode);
        Assert.Equal(Money.FromRupees(10_000m), stored.Amount);
        Assert.Equal("194J(b)", stored.Section);
        Assert.Equal("200", stored.MinorHead);
        // The challan is linked to the stat-payment voucher (both directions resolve).
        Assert.Contains(posted.Id, c.VouchersLinkedToChallan(challan.Id));
        Assert.Contains(challan.Id, c.ChallansLinkedToVoucher(posted.Id));
    }

    [Fact]
    public void Reconciliation_matches_the_deposit_to_the_deduction_remaining_zero()
    {
        var (c, _) = DeductTenThousand(NewTdsCompany());
        var dep = new TdsDepositService(c);
        var statType = dep.EnsureStatPaymentType();
        var bank = AddLedger(c, "HDFC Bank", "Bank Accounts", true);
        var posted = new LedgerService(c).Post(dep.BuildStatPayment(Money.FromRupees(10_000m), bank, Deposit, statType));
        dep.RecordChallan("00123", "0510308", Deposit, Money.FromRupees(10_000m), "194J(b)", "200", posted);

        // Reconcile over Q1 (the deduction on 10-May + the deposit on 5-Jun both fall in the window).
        var recon = ChallanReconciliation.Build(c, Fy, Q1End);
        var row = Assert.Single(recon.Sections);
        Assert.Equal("194J(b)", row.Section);
        Assert.Equal(Money.FromRupees(10_000m), row.Deducted);
        Assert.Equal(Money.FromRupees(10_000m), row.Deposited);
        Assert.Equal(Money.Zero, row.Remaining);
        Assert.True(row.IsMatched);
        Assert.True(recon.IsFullyReconciled);
        Assert.Equal(Money.Zero, recon.TotalRemaining);
        Assert.Empty(recon.Unmatched);
    }

    [Fact]
    public void Reconciliation_flags_a_partial_deposit_as_underpaid()
    {
        var (c, _) = DeductTenThousand(NewTdsCompany());
        var dep = new TdsDepositService(c);
        var statType = dep.EnsureStatPaymentType();
        var bank = AddLedger(c, "HDFC Bank", "Bank Accounts", true);
        // Deposit only ₹6,000 of the ₹10,000 due.
        var posted = new LedgerService(c).Post(dep.BuildStatPayment(Money.FromRupees(6_000m), bank, Deposit, statType));
        dep.RecordChallan("00123", "0510308", Deposit, Money.FromRupees(6_000m), "194J(b)", "200", posted);

        var recon = ChallanReconciliation.Build(c, Fy, Q1End);
        var row = Assert.Single(recon.Sections);
        Assert.Equal(Money.FromRupees(10_000m), row.Deducted);
        Assert.Equal(Money.FromRupees(6_000m), row.Deposited);
        Assert.Equal(Money.FromRupees(4_000m), row.Remaining);   // ₹4,000 still payable
        Assert.False(row.IsMatched);
        Assert.True(row.IsUnderpaid);
        Assert.False(recon.IsFullyReconciled);
        Assert.Single(recon.Unmatched);
        // The outstanding ledger balance agrees with the reconciliation remaining.
        Assert.Equal(Money.FromRupees(4_000m), dep.OutstandingPayable(Q1End));
    }

    [Fact]
    public void Reconciliation_drops_a_challan_whose_stat_payment_voucher_was_cancelled()
    {
        var (c, _) = DeductTenThousand(NewTdsCompany());
        var dep = new TdsDepositService(c);
        var statType = dep.EnsureStatPaymentType();
        var bank = AddLedger(c, "HDFC Bank", "Bank Accounts", true);
        var posted = new LedgerService(c).Post(dep.BuildStatPayment(Money.FromRupees(10_000m), bank, Deposit, statType));
        dep.RecordChallan("00123", "0510308", Deposit, Money.FromRupees(10_000m), "194J(b)", "200", posted);

        // Before cancelling: the deposit discharges the deduction (matched, nothing owed).
        Assert.True(Assert.Single(ChallanReconciliation.Build(c, Fy, Q1End).Sections).IsMatched);

        // Cancel the Stat-Payment voucher — its Dr "TDS Payable" leg is reversed off the ledger, so the money is
        // owed again. The deducted side + the ledger balance both skip cancelled vouchers; the deposited side must too.
        c.FindVoucher(posted.Id)!.Cancelled = true;
        Assert.Equal(Money.FromRupees(10_000m), dep.OutstandingPayable(Q1End)); // liability restored

        var recon = ChallanReconciliation.Build(c, Fy, Q1End);
        var row = Assert.Single(recon.Sections);
        Assert.Equal(Money.FromRupees(10_000m), row.Deducted);
        Assert.Equal(Money.Zero, row.Deposited);           // the cancelled challan no longer counts as deposited
        Assert.Equal(Money.FromRupees(10_000m), row.Remaining);
        Assert.True(row.IsUnderpaid);
        Assert.False(recon.IsFullyReconciled);
    }

    [Fact]
    public void Reconciliation_flags_an_orphan_deposit_with_no_deduction_as_overpaid()
    {
        var c = NewTdsCompany();
        // No deduction at all — a stray challan deposit.
        var dep = new TdsDepositService(c);
        var statType = dep.EnsureStatPaymentType();
        var bank = AddLedger(c, "HDFC Bank", "Bank Accounts", true);
        var posted = new LedgerService(c).Post(dep.BuildStatPayment(Money.FromRupees(1_000m), bank, Deposit, statType));
        dep.RecordChallan("00999", "0510308", Deposit, Money.FromRupees(1_000m), "194C", "200", posted);

        var recon = ChallanReconciliation.Build(c, Fy, Q1End);
        var row = Assert.Single(recon.Sections);
        Assert.Equal("194C", row.Section);
        Assert.Equal(Money.Zero, row.Deducted);
        Assert.Equal(Money.FromRupees(1_000m), row.Deposited);
        Assert.Equal(Money.FromRupees(-1_000m), row.Remaining);
        Assert.True(row.IsOverpaid);
        Assert.False(recon.IsFullyReconciled);
    }
}
