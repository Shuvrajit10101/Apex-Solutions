using System.Text;
using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Services;
using Domain = Apex.Ledger.Domain;

namespace Apex.Ledger.Io.Tests;

/// <summary>
/// Phase-7 slice-6 <b>Io fold-in</b> gate (PR-4 losslessness): a company that has collected 6CE TCS on a scrap sale
/// and then deposited it via a TCS Stat Payment (a Payment type flagged <see cref="VoucherType.IsStatPayment"/>) with
/// an ITNS-281 <see cref="TcsChallan"/> + its challan↔voucher link <b>exports and re-imports paisa + count exact in
/// JSON AND XML</b>, both byte-stable, and into a fresh (differently-Guid'd) company through the engine-routed
/// CompanyImportService — the TCS challan link re-mapping to the target's re-minted stat-payment voucher (the
/// Phase-6 carry-forward defect class: a new child silently dropped on export/import), built in here rather than
/// deferred. The exact sibling of <c>CanonicalChallanRoundTripTests</c>.
/// </summary>
public class CanonicalTcsChallanRoundTripTests
{
    private const string ValidTan = "MUMA12345B";
    private const string BuyerPan = "AAQCS1234K";
    private const string GstinMaharashtra = "27AAPFU0939F1ZV";
    private static readonly DateOnly Fy = new(2025, 4, 1);
    private static readonly DateOnly D0 = new(2025, 5, 1);   // purchase (stock-in) — strictly before the sale so a
    private static readonly DateOnly D1 = new(2025, 5, 10);  // re-import posts the receipt before the issue (no negative stock)
    private static readonly DateOnly Deposit = new(2025, 6, 5);

    private static Company BuildDepositedCompany()
    {
        var c = CompanyFactory.CreateSeeded("TCS Deposit Traders", Fy, Fy);
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

        post.Post(new Voucher(Guid.NewGuid(), c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Purchase).Id, D0,
            new[] { new Domain.EntryLine(purchases.Id, Money.FromRupees(50_000m), DrCr.Debit), new Domain.EntryLine(creditor.Id, Money.FromRupees(50_000m), DrCr.Credit) },
            inventoryLines: new[] { new VoucherInventoryLine(scrap.Id, main, 1000m, Money.FromRupees(50m)) }));

        var value = Money.FromRupees(1_00_000m);
        var tax = gst.ComputeInvoiceTax(new[] { new GstService.TaxableLine(value, 1800) }, gst.IsInterState("27"), GstTaxDirection.Output);
        var nature = new TcsService(c).ResolveNature(scrap, sales)!;
        var col = new TcsService(c).BuildCollection(value, tax.TotalTax, nature, buyer, D1);
        var lines = new List<Domain.EntryLine> { new(buyer.Id, value + tax.TotalTax + col.TcsAmount, DrCr.Debit), new(sales.Id, value, DrCr.Credit) };
        lines.AddRange(tax.TaxLines);
        lines.Add(col.TcsPayableLine!);
        post.Post(new Voucher(Guid.NewGuid(), c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Sales).Id, D1, lines,
            inventoryLines: new[] { new VoucherInventoryLine(scrap.Id, main, 1000m, Money.FromRupees(100m)) }));

        var dep = new TcsDepositService(c);
        var statType = dep.EnsureStatPaymentType();
        var posted = new LedgerService(c).Post(dep.BuildStatPayment(Money.FromRupees(1_180m), bank, Deposit, statType));
        dep.RecordChallan("00777", "0510308", Deposit, Money.FromRupees(1_180m), "6CE", "200", posted);
        return c;
    }

    private static Company FreshTarget() => CompanyFactory.CreateSeeded("Fresh TCS Deposit Co", Fy, Fy);

    [Fact]
    public void Json_round_trips_byte_stable_with_tcs_challan_and_stat_payment()
    {
        var c = BuildDepositedCompany();
        var first = CanonicalJson.Export(c);
        var (model, errors) = CanonicalJson.Parse(first);
        Assert.Empty(errors);
        Assert.Equal(first, CanonicalJson.Export(model!)); // byte-for-byte
        AssertProjection(model!);
        AssertNoTally(first);
    }

    [Fact]
    public void Xml_round_trips_byte_stable_with_tcs_challan_and_stat_payment()
    {
        var c = BuildDepositedCompany();
        var first = CanonicalXml.Export(c);
        var (model, errors) = CanonicalXml.Parse(first);
        Assert.Empty(errors);
        Assert.Equal(first, CanonicalXml.Export(model!));
        AssertProjection(model!);
        AssertNoTally(first);
    }

    [Fact]
    public void Json_and_xml_carry_identical_tcs_challan_payload()
    {
        var c = BuildDepositedCompany();
        var (jm, je) = CanonicalJson.Parse(CanonicalJson.Export(c));
        var (xm, xe) = CanonicalXml.Parse(CanonicalXml.Export(c));
        Assert.Empty(je);
        Assert.Empty(xe);
        Assert.Equal(CanonicalJson.Export(jm!), CanonicalJson.Export(xm!));
    }

    [Fact]
    public void Tcs_deposit_company_export_import_into_fresh_company_reconciles_json_and_xml()
    {
        var source = BuildDepositedCompany();

        foreach (var bytes in new[] { CanonicalJson.Export(source), CanonicalXml.Export(source) })
        {
            var (model, errors) = bytes[0] == (byte)'{' ? CanonicalJson.Parse(bytes) : CanonicalXml.Parse(bytes);
            Assert.Empty(errors);

            var fresh = FreshTarget();
            var result = new CompanyImportService(fresh).Apply(model!);
            Assert.True(result.Applied, string.Join("; ", result.Errors));

            // The TCS stat-payment type + challan + link reconcile paisa/count-exact, the link re-mapped to the
            // target's re-minted stat-payment voucher (Dr TCS Payable / Cr Bank).
            var statType = Assert.Single(fresh.VoucherTypes, t => t.IsStatPaymentType && t.Name == TcsDepositService.StatPaymentTypeName);
            Assert.Equal(VoucherBaseType.Payment, statType.BaseType);

            var ch = Assert.Single(fresh.TcsChallans);
            Assert.Equal("00777", ch.ChallanNo);
            Assert.Equal("0510308", ch.BsrCode);
            Assert.Equal(Money.FromRupees(1_180m), ch.Amount);
            Assert.Equal("6CE", ch.CollectionCode);
            Assert.Equal("200", ch.MinorHead);

            var linkedVoucherId = Assert.Single(fresh.VouchersLinkedToTcsChallan(ch.Id));
            var linked = fresh.FindVoucher(linkedVoucherId)!;
            Assert.Equal(statType.Id, linked.TypeId);              // the link resolves to the re-minted stat voucher
            Assert.Equal(Money.FromRupees(1_180m), linked.TotalDebit);
        }
    }

    private static void AssertProjection(CanonicalModel model)
    {
        var stat = Assert.Single(model.Payload.VoucherTypes, t => t.IsStatPayment);
        Assert.Equal("Payment", stat.BaseType);

        var ch = Assert.Single(model.Payload.TcsChallans);
        Assert.Equal("00777", ch.ChallanNo);
        Assert.Equal(118_000L, ch.AmountPaisa); // ₹1,180 = 118,000 paisa
        Assert.Equal("6CE", ch.CollectionCode);

        var link = Assert.Single(model.Payload.TcsChallanVoucherLinks);
        Assert.Equal(ch.Id, link.ChallanId);
    }

    private static void AssertNoTally(byte[] bytes) =>
        Assert.DoesNotContain("tally", Encoding.UTF8.GetString(bytes), StringComparison.OrdinalIgnoreCase);
}
