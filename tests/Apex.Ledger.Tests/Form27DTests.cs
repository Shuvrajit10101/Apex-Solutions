using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Reports;
using Apex.Ledger.Services;
using Xunit;
using Domain = Apex.Ledger.Domain;

namespace Apex.Ledger.Tests;

/// <summary>
/// Phase 7 slice 7 — <b>Form 27D</b> (<see cref="Form27D"/>), the per-collectee-per-quarter TCS certificate, as the
/// exact mirror of <see cref="Form16A"/> over <see cref="Form27EQ"/>. Proves the golden worked example (a scrap
/// ₹1,00,000 + 18% GST sale collecting ₹1,180 TCS at 1% on the ₹1,18,000 GST-inclusive base, plus its stat-payment
/// challan) yields a certificate whose collector block, collectee block (PAN/name), collection summary (amount
/// received / TCS / rate / code 6CE) and challan block match the corresponding <see cref="Form27EQ"/> row to the
/// paisa; and ER-13 (no TCS ⇒ an empty certificate).
/// </summary>
public class Form27DTests
{
    private const string ValidTan = "MUMA12345B";
    private const string BuyerPan = "AAQCS1234K";
    private const string GstinMaharashtra = "27AAPFU0939F1ZV";

    private static readonly DateOnly FyStart = new(2025, 4, 1);

    private static Company NewTcsCompany(DateOnly booksFrom)
    {
        var c = CompanyFactory.CreateSeeded("Collecting Co", booksFrom);
        new TdsTcsService(c).EnableTcs(new TcsConfig
        {
            Tan = ValidTan, CollectorType = DeductorType.Company,
            ResponsiblePersonName = "A. Sharma", ResponsiblePersonPan = "AAPFU0939F",
            ResponsiblePersonDesignation = "Director", ResponsiblePersonAddress = "12 MG Road",
        });
        new GstService(c).EnableGst(new GstConfig
        {
            HomeStateCode = "27", Gstin = GstinMaharashtra, RegistrationType = GstRegistrationType.Regular,
            ApplicableFrom = booksFrom, Periodicity = GstReturnPeriodicity.Monthly,
        });
        return c;
    }

    private static Domain.Ledger AddLedger(Company c, string name, string groupName, bool openingIsDebit)
    {
        var l = new Domain.Ledger(Guid.NewGuid(), name, c.FindGroupByName(groupName)!.Id, Money.Zero, openingIsDebit);
        c.AddLedger(l);
        return l;
    }

    private sealed record Scene(Company C, StockItem Scrap, Domain.Ledger Sales, Domain.Ledger Buyer, Guid Main);

    private static Scene BuildScene(Company c, string buyerName = "Scrap Buyer")
    {
        var inv = new InventoryService(c);
        var scrap = inv.CreateStockItem("Scrap Metal", inv.CreateStockGroup("Waste").Id, inv.CreateSimpleUnit("Kg", "Kilogram").Id);
        scrap.Gst = new StockItemGstDetails { Taxability = GstTaxability.Taxable, RateBasisPoints = 1800 };
        scrap.TcsNatureOfGoodsId = c.FindNatureOfGoodsByCode("6CE")!.Id;
        var main = c.MainLocation!.Id;

        var sales = AddLedger(c, "Scrap Sales", "Sales Accounts", false);
        var buyer = AddLedger(c, buyerName, "Sundry Debtors", true);
        buyer.PartyGst = new PartyGstDetails { RegistrationType = GstRegistrationType.Regular, Gstin = GstinMaharashtra, StateCode = "27" };
        buyer.TcsApplicable = true; buyer.CollecteeType = CollecteeType.Individual; buyer.PartyPan = BuyerPan;

        var purchases = AddLedger(c, "Purchases", "Purchase Accounts", true);
        var creditor = AddLedger(c, "Creditor", "Sundry Creditors", false);
        new LedgerService(c).Post(new Voucher(Guid.NewGuid(), c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Purchase).Id,
            c.BooksBeginFrom,
            new[] { new EntryLine(purchases.Id, Money.FromRupees(5_00_000m), DrCr.Debit), new EntryLine(creditor.Id, Money.FromRupees(5_00_000m), DrCr.Credit) },
            inventoryLines: new[] { new VoucherInventoryLine(scrap.Id, main, 10_000m, Money.FromRupees(50m)) }));

        return new Scene(c, scrap, sales, buyer, main);
    }

    private static void BookScrapSale(Scene s, DateOnly on)
    {
        var c = s.C;
        var gst = new GstService(c);
        var value = Money.FromRupees(1_00_000m);
        var interState = gst.IsInterState(s.Buyer.PartyGst!.StateCode);
        var tax = gst.ComputeInvoiceTax(new[] { new GstService.TaxableLine(value, 1800) }, interState, GstTaxDirection.Output);
        var nature = new TcsService(c).ResolveNature(s.Scrap, s.Sales)!;
        var col = new TcsService(c).BuildCollection(value, tax.TotalTax, nature, s.Buyer, on);

        var lines = new List<EntryLine>
        {
            new(s.Buyer.Id, value + tax.TotalTax + col.TcsAmount, DrCr.Debit),
            new(s.Sales.Id, value, DrCr.Credit),
        };
        lines.AddRange(tax.TaxLines);
        lines.Add(col.TcsPayableLine!);
        new LedgerService(c).Post(new Voucher(Guid.NewGuid(), c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Sales).Id, on, lines,
            inventoryLines: new[] { new VoucherInventoryLine(s.Scrap.Id, s.Main, 1000m, Money.FromRupees(100m)) }));
    }

    private static TcsChallan Deposit(Company c, Money amount, DateOnly on, string challanNo, string code = "6CE")
    {
        var dep = new TcsDepositService(c);
        var statType = dep.EnsureStatPaymentType();
        var bank = c.FindLedgerByName("HDFC Bank") ?? AddLedger(c, "HDFC Bank", "Bank Accounts", true);
        var posted = new LedgerService(c).Post(dep.BuildStatPayment(amount, bank, on, statType));
        return dep.RecordChallan(challanNo, "0510308", on, amount, code, "200", posted);
    }

    [Fact]
    public void Golden_certificate_matches_the_27EQ_collectee_row_and_challan()
    {
        var s = BuildScene(NewTcsCompany(FyStart));
        BookScrapSale(s, new DateOnly(2025, 5, 10));
        Deposit(s.C, Money.FromRupees(1_180m), new DateOnly(2025, 6, 5), "00123");

        var q1 = Form27EQ.Build(s.C, 2025, 1);
        var cert = Form27D.Build(s.C, 2025, 1, s.Buyer.Id);

        Assert.Equal(ValidTan, cert.Collector.Tan);
        Assert.Equal("Collecting Co", cert.Collector.Name);
        Assert.Equal("2025-26", cert.FinancialYearLabel);
        Assert.Equal("Q1", cert.QuarterLabel);

        Assert.Equal(s.Buyer.Id, cert.Collectee.LedgerId);
        Assert.Equal("Scrap Buyer", cert.Collectee.Name);
        Assert.Equal(BuyerPan, cert.Collectee.Pan);

        var q27 = Assert.Single(q1.Collectees);
        var row = Assert.Single(cert.Collections);
        Assert.Equal("6CE", row.CollectionCode);
        Assert.Equal("6CE", row.FvuCollectionCode);
        Assert.Equal(q27.CollectionDate, row.Date);
        Assert.Equal(Money.FromRupees(1_18_000m), row.AmountReceived);
        Assert.Equal(Money.FromRupees(1_180m), row.TcsAmount);
        Assert.Equal(100, row.RateBasisPoints);
        Assert.Equal(1.00m, row.RatePercent);
        Assert.True(row.PanApplied);

        var ch27 = Assert.Single(q1.Challans);
        var ch = Assert.Single(cert.Challans);
        Assert.Equal(ch27.ChallanNo, ch.ChallanNo);
        Assert.Equal(ch27.BsrCode, ch.BsrCode);
        Assert.Equal(ch27.DepositDate, ch.DepositDate);
        Assert.Equal(Money.FromRupees(1_180m), ch.TcsDeposited);

        Assert.Equal(Money.FromRupees(1_18_000m), cert.TotalAmountReceived);
        Assert.Equal(Money.FromRupees(1_180m), cert.TotalTcsCollected);
        Assert.Equal(Money.FromRupees(1_180m), cert.TotalTcsDeposited);
        Assert.False(cert.IsEmpty);
    }

    [Fact]
    public void No_TCS_yields_an_empty_certificate_er13()
    {
        var s = BuildScene(NewTcsCompany(FyStart));
        var cert = Form27D.Build(s.C, 2025, 1, s.Buyer.Id);
        Assert.True(cert.IsEmpty);
        Assert.Empty(cert.Collections);
        Assert.Empty(cert.Challans);
        Assert.Equal(Money.Zero, cert.TotalTcsCollected);
        Assert.Empty(Form27D.BuildAll(s.C, 2025, 1));
    }
}
