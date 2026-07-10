using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Services;
using Xunit;
using Domain = Apex.Ledger.Domain;

namespace Apex.Persistence.Sqlite.Tests;

/// <summary>
/// Phase-7 slice-3 persistence contract (ER-1, ER-13): a Stat-Payment Payment voucher type (flagged
/// <see cref="VoucherType.IsStatPayment"/>), the deposit voucher it books, an ITNS-281 <see cref="TdsChallan"/> and
/// its challan↔voucher link all SAVE and RELOAD at <see cref="Schema.CurrentVersion"/> preserving every field
/// paisa-exact. A company that never deposits TDS round-trips with no challan state and no stat-payment flag set.
/// </summary>
public sealed class ChallanRoundTripTests
{
    private const string ValidTan = "MUMA12345B";
    private const string DeducteePan = "AAPFU0939F";
    private static readonly DateOnly Fy = new(2025, 4, 1);
    private static readonly DateOnly Deduct = new(2025, 5, 10);
    private static readonly DateOnly Deposit = new(2025, 6, 5);

    [Fact]
    [Trait("Category", "RoundTrip")]
    public void Stat_payment_challan_and_link_survive_save_reload_paisa_exact()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"apex-challan-{Guid.NewGuid():N}.db");
        try
        {
            var c = CompanyFactory.CreateSeeded("Deposit Persist Co", Fy);
            new TdsTcsService(c).EnableTds(new TdsConfig { Tan = ValidTan });

            var fees = new Domain.Ledger(Guid.NewGuid(), "Professional Fees", c.FindGroupByName("Indirect Expenses")!.Id, Money.Zero, true);
            c.AddLedger(fees);
            var vendor = new Domain.Ledger(Guid.NewGuid(), "Consultant", c.FindGroupByName("Sundry Creditors")!.Id, Money.Zero, false)
            { TdsApplicable = true, TdsNatureOfPaymentId = c.FindNatureOfPaymentByCode("194J(b)")!.Id, DeducteeType = DeducteeType.Firm, PartyPan = DeducteePan };
            c.AddLedger(vendor);
            var bank = new Domain.Ledger(Guid.NewGuid(), "HDFC Bank", c.FindGroupByName("Bank Accounts")!.Id, Money.Zero, true);
            c.AddLedger(bank);

            var nop = c.FindNatureOfPaymentByCode("194J(b)")!;
            var carve = new TdsService(c).BuildCarveOut(Money.FromRupees(1_00_000m), Money.FromRupees(1_00_000m), nop, vendor, Deduct);
            new LedgerService(c).Post(new Voucher(Guid.NewGuid(),
                c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Journal).Id, Deduct,
                new[] { new EntryLine(fees.Id, Money.FromRupees(1_00_000m), DrCr.Debit), carve.PartyLine, carve.TdsPayableLine! }));

            var dep = new TdsDepositService(c);
            var statType = dep.EnsureStatPaymentType();
            var posted = new LedgerService(c).Post(dep.BuildStatPayment(Money.FromRupees(10_000m), bank, Deposit, statType));
            var challan = dep.RecordChallan("00123", "0510308", Deposit, Money.FromRupees(10_000m), "194J(b)", "200", posted);

            using (var write = new SqliteCompanyStore(dbPath))
            {
                write.Save(c);
                write.Save(c); // re-save (delete-then-insert) must not trip an FK
            }

            Company r;
            using (var read = new SqliteCompanyStore(dbPath)) r = read.Load(c.Id)!;

            // Stat-payment voucher type survived with its flag on a Payment base.
            var rStat = r.VoucherTypes.Single(t => t.IsStatPaymentType);
            Assert.Equal(VoucherBaseType.Payment, rStat.BaseType);
            Assert.True(rStat.IsStatPayment);

            // Challan survived figure-exact.
            var rCh = Assert.Single(r.TdsChallans);
            Assert.Equal("00123", rCh.ChallanNo);
            Assert.Equal("0510308", rCh.BsrCode);
            Assert.Equal(Deposit, rCh.DepositDate);
            Assert.Equal(Money.FromRupees(10_000m), rCh.Amount);
            Assert.Equal("194J(b)", rCh.Section);
            Assert.Equal("200", rCh.MinorHead);

            // The link survived and resolves to the reloaded stat-payment voucher.
            var rPosted = r.FindVoucher(posted.Id)!;
            Assert.Contains(rPosted.Id, r.VouchersLinkedToChallan(rCh.Id));
            Assert.Contains(rCh.Id, r.ChallansLinkedToVoucher(rPosted.Id));
        }
        finally { TempDbFile.Delete(dbPath); }
    }

    [Fact]
    [Trait("Category", "RoundTrip")]
    public void A_company_without_a_deposit_round_trips_with_no_challan_state()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"apex-nochallan-{Guid.NewGuid():N}.db");
        try
        {
            var c = CompanyFactory.CreateSeeded("No-Deposit Co", Fy);
            using (var write = new SqliteCompanyStore(dbPath)) write.Save(c);
            Company r;
            using (var read = new SqliteCompanyStore(dbPath)) r = read.Load(c.Id)!;

            Assert.Empty(r.TdsChallans);
            Assert.Empty(r.ChallanVoucherLinks);
            Assert.DoesNotContain(r.VoucherTypes, t => t.IsStatPayment);
        }
        finally { TempDbFile.Delete(dbPath); }
    }
}
