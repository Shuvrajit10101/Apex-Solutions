using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Services;
using Xunit;
using Domain = Apex.Ledger.Domain;

namespace Apex.Persistence.Sqlite.Tests;

/// <summary>
/// Phase-7 slice-6 persistence contract (ER-1, ER-13): a TCS Stat-Payment Payment voucher type (flagged
/// <see cref="VoucherType.IsStatPayment"/>), the deposit voucher it books, an ITNS-281 <see cref="TcsChallan"/> and
/// its challan↔voucher link all SAVE and RELOAD at <see cref="Schema.CurrentVersion"/> preserving every field
/// paisa-exact. A company that never deposits TCS round-trips with no challan state. The exact sibling of the TDS
/// <c>ChallanRoundTripTests</c>.
/// </summary>
public sealed class TcsChallanRoundTripTests
{
    private const string ValidTan = "MUMA12345B";
    private const string BuyerPan = "AAQCS1234K";
    private static readonly DateOnly Fy = new(2025, 4, 1);
    private static readonly DateOnly D1 = new(2025, 5, 10);
    private static readonly DateOnly Deposit = new(2025, 6, 5);
    private const string GstinMaharashtra = "27AAPFU0939F1ZV";

    [Fact]
    [Trait("Category", "RoundTrip")]
    public void Tcs_stat_payment_challan_and_link_survive_save_reload_paisa_exact()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"apex-tcschallan-{Guid.NewGuid():N}.db");
        try
        {
            var c = CompanyFactory.CreateSeeded("TCS Deposit Persist Co", Fy);
            new TdsTcsService(c).EnableTcs(new TcsConfig { Tan = ValidTan });
            new GstService(c).EnableGst(new GstConfig
            {
                HomeStateCode = "27", Gstin = GstinMaharashtra, RegistrationType = GstRegistrationType.Regular,
                ApplicableFrom = Fy, Periodicity = GstReturnPeriodicity.Monthly,
            });

            var gst = new GstService(c);
            var post = new LedgerService(c);
            var inv = new InventoryService(c);
            var scrap = inv.CreateStockItem("Scrap Metal", inv.CreateStockGroup("Waste").Id, inv.CreateSimpleUnit("Kg", "Kilogram").Id);
            scrap.Gst = new StockItemGstDetails { Taxability = GstTaxability.Taxable, RateBasisPoints = 1800 };
            scrap.TcsNatureOfGoodsId = c.FindNatureOfGoodsByCode("6CE")!.Id;
            var main = c.MainLocation!.Id;

            var sales = new Domain.Ledger(Guid.NewGuid(), "Scrap Sales", c.FindGroupByName("Sales Accounts")!.Id, Money.Zero, false);
            c.AddLedger(sales);
            var buyer = new Domain.Ledger(Guid.NewGuid(), "Scrap Buyer", c.FindGroupByName("Sundry Debtors")!.Id, Money.Zero, true)
            {
                PartyGst = new PartyGstDetails { RegistrationType = GstRegistrationType.Regular, Gstin = GstinMaharashtra, StateCode = "27" },
                TcsApplicable = true, CollecteeType = CollecteeType.Individual, PartyPan = BuyerPan,
            };
            c.AddLedger(buyer);
            var purchases = new Domain.Ledger(Guid.NewGuid(), "Purchases", c.FindGroupByName("Purchase Accounts")!.Id, Money.Zero, true);
            c.AddLedger(purchases);
            var creditor = new Domain.Ledger(Guid.NewGuid(), "Creditor", c.FindGroupByName("Sundry Creditors")!.Id, Money.Zero, false);
            c.AddLedger(creditor);
            var bank = new Domain.Ledger(Guid.NewGuid(), "HDFC Bank", c.FindGroupByName("Bank Accounts")!.Id, Money.Zero, true);
            c.AddLedger(bank);

            post.Post(new Voucher(Guid.NewGuid(), c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Purchase).Id, D1,
                new[] { new EntryLine(purchases.Id, Money.FromRupees(50_000m), DrCr.Debit), new EntryLine(creditor.Id, Money.FromRupees(50_000m), DrCr.Credit) },
                inventoryLines: new[] { new VoucherInventoryLine(scrap.Id, main, 1000m, Money.FromRupees(50m)) }));

            var value = Money.FromRupees(1_00_000m);
            var tax = gst.ComputeInvoiceTax(new[] { new GstService.TaxableLine(value, 1800) }, gst.IsInterState("27"), GstTaxDirection.Output);
            var nature = new TcsService(c).ResolveNature(scrap, sales)!;
            var col = new TcsService(c).BuildCollection(value, tax.TotalTax, nature, buyer, D1);
            var lines = new List<EntryLine> { new(buyer.Id, value + tax.TotalTax + col.TcsAmount, DrCr.Debit), new(sales.Id, value, DrCr.Credit) };
            lines.AddRange(tax.TaxLines);
            lines.Add(col.TcsPayableLine!);
            post.Post(new Voucher(Guid.NewGuid(), c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Sales).Id, D1, lines,
                inventoryLines: new[] { new VoucherInventoryLine(scrap.Id, main, 1000m, Money.FromRupees(100m)) }));

            var dep = new TcsDepositService(c);
            var statType = dep.EnsureStatPaymentType();
            var posted = new LedgerService(c).Post(dep.BuildStatPayment(Money.FromRupees(1_180m), bank, Deposit, statType));
            dep.RecordChallan("00777", "0510308", Deposit, Money.FromRupees(1_180m), "6CE", "200", posted);

            using (var write = new SqliteCompanyStore(dbPath))
            {
                write.Save(c);
                write.Save(c); // re-save (delete-then-insert) must not trip an FK
            }

            Company r;
            using (var read = new SqliteCompanyStore(dbPath)) r = read.Load(c.Id)!;

            // TCS Stat-payment voucher type survived with its flag on a Payment base.
            var rStat = r.VoucherTypes.Single(t => t.IsStatPaymentType && t.Name == TcsDepositService.StatPaymentTypeName);
            Assert.Equal(VoucherBaseType.Payment, rStat.BaseType);
            Assert.True(rStat.IsStatPayment);

            // Challan survived figure-exact.
            var rCh = Assert.Single(r.TcsChallans);
            Assert.Equal("00777", rCh.ChallanNo);
            Assert.Equal("0510308", rCh.BsrCode);
            Assert.Equal(Deposit, rCh.DepositDate);
            Assert.Equal(Money.FromRupees(1_180m), rCh.Amount);
            Assert.Equal("6CE", rCh.CollectionCode);
            Assert.Equal("200", rCh.MinorHead);

            // The link survived and resolves to the reloaded stat-payment voucher.
            var rPosted = r.FindVoucher(posted.Id)!;
            Assert.Contains(rPosted.Id, r.VouchersLinkedToTcsChallan(rCh.Id));
            Assert.Contains(rCh.Id, r.TcsChallansLinkedToVoucher(rPosted.Id));
        }
        finally { TempDbFile.Delete(dbPath); }
    }

    [Fact]
    [Trait("Category", "RoundTrip")]
    public void A_company_without_a_tcs_deposit_round_trips_with_no_challan_state()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"apex-notcschallan-{Guid.NewGuid():N}.db");
        try
        {
            var c = CompanyFactory.CreateSeeded("No-TCS-Deposit Co", Fy);
            using (var write = new SqliteCompanyStore(dbPath)) write.Save(c);
            Company r;
            using (var read = new SqliteCompanyStore(dbPath)) r = read.Load(c.Id)!;

            Assert.Empty(r.TcsChallans);
            Assert.Empty(r.TcsChallanVoucherLinks);
        }
        finally { TempDbFile.Delete(dbPath); }
    }
}
