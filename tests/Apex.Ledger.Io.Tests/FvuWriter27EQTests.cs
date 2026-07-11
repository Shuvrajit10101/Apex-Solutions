using System.Linq;
using System.Text;
using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Io;
using Apex.Ledger.Reports;
using Apex.Ledger.Services;
using Domain = Apex.Ledger.Domain;

namespace Apex.Ledger.Io.Tests;

/// <summary>
/// Phase 7 slice 6 — the <see cref="FvuWriter"/> NSDL FVU-compatible flat-file for <b>Form 27EQ</b> (TCS). The exact
/// mirror of the 26Q <see cref="FvuWriterTests"/>. Proves the file is <b>deterministic + byte-stable</b>
/// (byte-identical across two runs, no clock/RNG), <b>de-branded</b> (no third-party accounting brand can leak, even
/// from a buyer name typed with it), and structurally faithful (FH / BH / CD / CL / FT records, caret-delimited) with
/// the golden worked example's figures. ER-13: an empty return still yields a valid header-only file. The S4 fixes
/// mirrored: an undeposited in-quarter collection never overstates the file's own record counts/totals; a
/// challan-boundary split is never double-counted.
/// </summary>
public class FvuWriter27EQTests
{
    private const string ValidTan = "MUMA12345B";
    private const string BuyerPan = "AAQCS1234K";
    private const string GstinMaharashtra = "27AAPFU0939F1ZV";
    private static readonly DateOnly FyStart = new(2025, 4, 1);

    private static Company NewTcsCompany(DateOnly booksFrom, string responsible = "A. Sharma")
    {
        var c = CompanyFactory.CreateSeeded("Collecting Co", booksFrom);
        new TdsTcsService(c).EnableTcs(new TcsConfig
        {
            Tan = ValidTan, CollectorType = DeductorType.Company,
            ResponsiblePersonName = responsible, ResponsiblePersonPan = "AAPFU0939F",
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

    private static void Deposit(Company c, Money amount, DateOnly on, string challanNo, string code = "6CE")
    {
        var dep = new TcsDepositService(c);
        var statType = dep.EnsureStatPaymentType();
        var bank = c.FindLedgerByName("HDFC Bank") ?? AddLedger(c, "HDFC Bank", "Bank Accounts", true);
        var posted = new LedgerService(c).Post(dep.BuildStatPayment(amount, bank, on, statType));
        dep.RecordChallan(challanNo, "0510308", on, amount, code, "200", posted);
    }

    private static Company GoldenCompany(string buyerName = "Scrap Buyer")
    {
        var s = BuildScene(NewTcsCompany(FyStart), buyerName);
        BookScrapSale(s, new DateOnly(2025, 5, 10));
        Deposit(s.C, Money.FromRupees(1_180m), new DateOnly(2025, 6, 5), "00123");
        return s.C;
    }

    [Fact]
    public void File_is_byte_identical_across_two_runs()
    {
        var q1 = Form27EQ.Build(GoldenCompany(), 2025, 1);
        var a = FvuWriter.Write(q1);
        var b = FvuWriter.Write(q1);
        Assert.Equal(a, b);           // deterministic — no clock/RNG
        Assert.NotEmpty(a);
    }

    [Fact]
    public void File_never_contains_the_third_party_brand_even_from_a_party_name()
    {
        // A user types the forbidden brand into a buyer name — it must be scrubbed out of the produced file (ER-11).
        var q1 = Form27EQ.Build(GoldenCompany("Tally Traders"), 2025, 1);
        var text = Encoding.UTF8.GetString(FvuWriter.Write(q1));
        Assert.DoesNotContain("tally", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void File_has_the_expected_fvu_record_structure_and_figures()
    {
        var q1 = Form27EQ.Build(GoldenCompany(), 2025, 1);
        var text = Encoding.UTF8.GetString(FvuWriter.Write(q1));
        var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        // FH ^ 27EQ ^ version ^ TAN ^ FY ^ quarter ^ collectorType ^ recordCount
        Assert.StartsWith($"FH^27EQ^{FvuWriter.FvuVersion}^{ValidTan}^2025-26^Q1^Company^", lines[0]);
        // BH carries the responsible person.
        Assert.StartsWith("BH^", lines[1]);
        Assert.Contains("A. Sharma", lines[1]);

        // One challan detail then one collectee detail, then the trailer.
        var cd = Assert.Single(lines, l => l.StartsWith("CD^"));
        Assert.Contains("^00123^0510308^05062025^1180.00^6CE^200^", cd);
        var cl = Assert.Single(lines, l => l.StartsWith("CL^"));
        Assert.Contains(BuyerPan, cl);
        Assert.Contains("^6CE^6CE^10052025^118000.00^1180.00^1.00^Y^", cl);

        // File Trailer: collectee count, challan count, total TCS, total received, total deposited.
        var ft = Assert.Single(lines, l => l.StartsWith("FT^"));
        Assert.Equal("FT^1^1^1180.00^118000.00^1180.00", ft);
    }

    [Fact]
    public void Empty_return_still_yields_a_valid_header_only_file()
    {
        var c = NewTcsCompany(FyStart);
        var q1 = Form27EQ.Build(c, 2025, 1);
        var text = Encoding.UTF8.GetString(FvuWriter.Write(q1));
        var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        Assert.StartsWith("FH^27EQ^", lines[0]);
        Assert.StartsWith("BH^", lines[1]);
        Assert.DoesNotContain(lines, l => l.StartsWith("CD^"));
        Assert.DoesNotContain(lines, l => l.StartsWith("CL^"));
        Assert.Equal("FT^0^0^0.00^0.00^0.00", lines[^1]);
    }

    [Fact]
    public void Undeposited_collection_does_not_overstate_the_file_collectee_count()
    {
        // An in-quarter ₹1,180 collection with NO deposit: the file has no challan and therefore no CL (collectee)
        // record. The BH/FT collectee counts and the FT money totals must describe the file's actual CL rows (zero),
        // never the full-quarter projection — otherwise the trailer claims a collectee record the file doesn't hold.
        var s = BuildScene(NewTcsCompany(FyStart));
        BookScrapSale(s, new DateOnly(2025, 5, 10));

        var q1 = Form27EQ.Build(s.C, 2025, 1);
        var text = Encoding.UTF8.GetString(FvuWriter.Write(q1));
        var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        Assert.DoesNotContain(lines, l => l.StartsWith("CD^"));
        var clCount = lines.Count(l => l.StartsWith("CL^"));
        Assert.Equal(0, clCount);

        // File Trailer must equal zero collectee records / zero money — not FT^1^0^1180.00^118000.00^0.00.
        var ft = Assert.Single(lines, l => l.StartsWith("FT^"));
        Assert.Equal("FT^0^0^0.00^0.00^0.00", ft);

        // Batch Header's trailing collectee count must equal the CL lines actually written.
        var bh = Assert.Single(lines, l => l.StartsWith("BH^"));
        Assert.Equal(clCount.ToString(), bh.Split('^')[^1]);
    }

    [Fact]
    public void Split_challan_does_not_double_count_the_file_totals()
    {
        // Two ₹1,180 collections; a ₹1,500 then an ₹860 challan (the S4 regression, applied to TCS). The file's CL
        // totals must sum to the true ₹2,360 collected / ₹2,36,000 received — never a phantom over-count.
        var s = BuildScene(NewTcsCompany(FyStart));
        BookScrapSale(s, new DateOnly(2025, 5, 10));
        BookScrapSale(s, new DateOnly(2025, 5, 11));
        Deposit(s.C, Money.FromRupees(1_500m), new DateOnly(2025, 6, 5), "AA");
        Deposit(s.C, Money.FromRupees(860m), new DateOnly(2025, 6, 6), "BB");

        var text = Encoding.UTF8.GetString(FvuWriter.Write(Form27EQ.Build(s.C, 2025, 1)));
        var ft = text.Split('\n', StringSplitOptions.RemoveEmptyEntries).Single(l => l.StartsWith("FT^"));
        // 3 CL rows (C1 whole, C2 split into two portions), 2 challans, ₹2,360 TCS, ₹2,36,000 received, ₹2,360 deposited.
        Assert.Equal("FT^3^2^2360.00^236000.00^2360.00", ft);
    }

    [Fact]
    public void Cross_fy_challan_is_written_into_the_collection_quarter_file()
    {
        var s = BuildScene(NewTcsCompany(new DateOnly(2024, 4, 1)));
        BookScrapSale(s, new DateOnly(2025, 3, 20));
        Deposit(s.C, Money.FromRupees(1_180m), new DateOnly(2025, 4, 7), "00777");

        // Q4 FY2024-25 file has the April challan; the Q1 FY2025-26 file does not.
        var q4Text = Encoding.UTF8.GetString(FvuWriter.Write(Form27EQ.Build(s.C, 2024, 4)));
        Assert.Contains("00777", q4Text);
        Assert.Contains("07042025", q4Text);  // deposit date printed, but in the Q4 file

        var q1Text = Encoding.UTF8.GetString(FvuWriter.Write(Form27EQ.Build(s.C, 2025, 1)));
        Assert.DoesNotContain("00777", q1Text);
        Assert.EndsWith("FT^0^0^0.00^0.00^0.00\n", q1Text);
    }
}
