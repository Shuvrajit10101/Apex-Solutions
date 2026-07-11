using System.Globalization;
using System.Text;
using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Io;
using Apex.Ledger.Reports;
using Apex.Ledger.Services;
using Xunit;
using Domain = Apex.Ledger.Domain;

namespace Apex.Ledger.Tests;

/// <summary>
/// Phase 7 <b>golden worked-example</b> regression tests — the two end-to-end oracles that walk the FULL TDS and TCS
/// chains and prove every stage reconciles to the rupee against a <b>hand-derived</b> (law-derived, never
/// engine-echoed) expected figure:
/// <list type="number">
///   <item><b>Golden 1 — TDS §194J(b) professional fee</b>: a ₹1,00,000 fee to a PAN-holding consultant →
///     withholding voucher (Dr Fees ₹1,00,000 / Cr Vendor net ₹90,000 / Cr TDS Payable ₹10,000, TDS = 10% ×
///     1,00,000) → ₹10,000 deposited via a stat-payment challan → Form 26Q (one deductee row, ₹10,000 deducted =
///     ₹10,000 deposited) → FVU flat-file control totals → Form 16A certificate. The single ₹10,000 threads
///     unchanged through <b>deduction == 26Q == FVU == 16A</b>.</item>
///   <item><b>Golden 2 — TCS scrap §206C(1) code 6CE</b>: a GST-free ₹1,00,000 scrap sale to a PAN-holding buyer →
///     additive collection (buyer charged ₹1,01,000; Cr TCS Payable ₹1,000, TCS = 1% × 1,00,000, collect-on-top —
///     NOT a carve-out of the sale) → ₹1,000 deposited via a stat-payment challan → Form 27EQ (one collectee row,
///     ₹1,000 collected = ₹1,000 deposited) → FVU flat-file control totals → Form 27D certificate. The single ₹1,000
///     threads unchanged through <b>collection == 27EQ == FVU == 27D</b>.</item>
/// </list>
/// The seeded natures used (confirmed against <c>SeedTdsTcsRates</c>): §194J(b) with-PAN rate 10% (1000 bp), FVU
/// section code "94J-B", ₹50,000 FY cumulative threshold; scrap "6CE" §206C(1) with-PAN rate 1% (100 bp), no
/// threshold, GST-inclusive base. Golden 2 keeps the oracle a clean round number by pricing a <b>GST-free</b> scrap
/// sale (base = value = ₹1,00,000), so the GST-inclusive-base flag never fractionalises the ₹1,000.
/// </summary>
public class Phase7GoldenExamplesTests
{
    private const string ValidTan = "MUMA12345B";
    private const string DeducteePan = "AAPFU0939F";  // vendor / scrap-buyer PAN (valid → with-PAN rate)

    private static readonly DateOnly FyStart = new(2025, 4, 1);   // FY 2025-26
    private static readonly DateOnly TxnDate = new(2025, 5, 10);  // Q1 transaction date
    private static readonly DateOnly DepositDate = new(2025, 6, 5); // Q1 deposit date

    private static Domain.Ledger AddLedger(Company c, string name, string groupName, bool openingIsDebit)
    {
        var l = new Domain.Ledger(Guid.NewGuid(), name, c.FindGroupByName(groupName)!.Id, Money.Zero, openingIsDebit);
        c.AddLedger(l);
        return l;
    }

    /// <summary>Extracts the single caret-delimited record whose type tag is <paramref name="tag"/> from FVU bytes.</summary>
    private static string FvuRecord(byte[] file, string tag)
    {
        var lines = Encoding.UTF8.GetString(file).Split('\n', StringSplitOptions.RemoveEmptyEntries);
        return Assert.Single(lines, l => l.StartsWith(tag + "^"));
    }

    private static decimal ParseInvariant(string s) => decimal.Parse(s, CultureInfo.InvariantCulture);

    // =====================================================================================================
    // GOLDEN 1 — TDS §194J(b) professional fee. Hand-derived oracle: fee ₹1,00,000; TDS = 10% × 1,00,000 =
    // ₹10,000 (withheld); net to vendor = 1,00,000 − 10,000 = ₹90,000.
    // =====================================================================================================

    [Fact]
    public void Golden_TDS_194J_fee_ties_deduction_to_26Q_to_FVU_to_16A()
    {
        // ---- seed: a company with TDS enabled + the F11 person-responsible identity the return/cert print. ----
        var c = CompanyFactory.CreateSeeded("Golden TDS Co", FyStart);
        new TdsTcsService(c).EnableTds(new TdsConfig
        {
            Tan = ValidTan, DeductorType = DeductorType.Company,
            ResponsiblePersonName = "A. Sharma", ResponsiblePersonPan = DeducteePan,
            ResponsiblePersonDesignation = "Director", ResponsiblePersonAddress = "12 MG Road",
        });

        var fees = AddLedger(c, "Professional Fees", "Indirect Expenses", true);
        var vendor = AddLedger(c, "Consultant", "Sundry Creditors", false);
        var nop = c.FindNatureOfPaymentByCode("194J(b)")!;
        vendor.TdsApplicable = true; vendor.TdsNatureOfPaymentId = nop.Id;
        vendor.DeducteeType = DeducteeType.Firm; vendor.PartyPan = DeducteePan;

        // Confirm the seeded nature we rely on: §194J(b), with-PAN 10% (1000 bp), FVU code 94J-B.
        Assert.Equal(1000, nop.RateWithPanBp);
        Assert.Equal("94J-B", nop.FvuSectionCode);

        // ---- STAGE 1: the withholding voucher. Hand-derived: gross ₹1,00,000, TDS 10% = ₹10,000, net ₹90,000. ----
        var gross = Money.FromRupees(1_00_000m);
        var carve = new TdsService(c).BuildCarveOut(gross, gross, nop, vendor, TxnDate);

        Assert.True(carve.Applies);
        Assert.True(carve.Withholding.PanApplied);
        Assert.Equal(1000, carve.Withholding.RateBasisPoints);
        Assert.Equal(Money.FromRupees(10_000m), carve.TdsAmount);      // 10% × 1,00,000 (hand-derived)
        Assert.Equal(Money.FromRupees(90_000m), carve.NetPartyAmount); // 1,00,000 − 10,000 (derived, exact)
        // The load-bearing invariant: gross Dr == net Cr + TDS Cr, to the paisa.
        Assert.Equal(gross, carve.NetPartyAmount + carve.TdsAmount);

        var v = new LedgerService(c).Post(new Voucher(Guid.NewGuid(),
            c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Journal).Id, TxnDate,
            new[] { new EntryLine(fees.Id, gross, DrCr.Debit), carve.PartyLine, carve.TdsPayableLine! }));
        Assert.True(VoucherValidator.IsBalanced(v));

        // Assert the three posted legs by ledger: Dr Fees 1,00,000 / Cr Vendor 90,000 / Cr TDS Payable 10,000.
        var payable = new TdsService(c).RequirePayableLedger();
        var drFees = Assert.Single(v.Lines, l => l.LedgerId == fees.Id);
        Assert.Equal(DrCr.Debit, drFees.Side);
        Assert.Equal(Money.FromRupees(1_00_000m), drFees.Amount);
        var crVendor = Assert.Single(v.Lines, l => l.LedgerId == vendor.Id);
        Assert.Equal(DrCr.Credit, crVendor.Side);
        Assert.Equal(Money.FromRupees(90_000m), crVendor.Amount);
        var crPayable = Assert.Single(v.Lines, l => l.LedgerId == payable.Id);
        Assert.Equal(DrCr.Credit, crPayable.Side);
        Assert.Equal(Money.FromRupees(10_000m), crPayable.Amount);
        // TDS Payable accrues the ₹10,000 withheld (credit → −10,000 signed).
        Assert.Equal(-10_000m, LedgerBalances.SignedClosing(c, payable, TxnDate));

        // ---- STAGE 2: deposit the ₹10,000 via a stat-payment + ITNS-281 challan. ----
        var dep = new TdsDepositService(c);
        var statType = dep.EnsureStatPaymentType();
        var bank = AddLedger(c, "HDFC Bank", "Bank Accounts", true);
        var deposit = new LedgerService(c).Post(dep.BuildStatPayment(Money.FromRupees(10_000m), bank, DepositDate, statType));
        dep.RecordChallan("00123", "0510308", DepositDate, Money.FromRupees(10_000m), "194J(b)", "200", deposit);

        // ---- STAGE 3: Form 26Q for Q1. One deductee row; deducted == deposited == ₹10,000. ----
        var q1 = Form26Q.Build(c, 2025, 1);
        var row = Assert.Single(q1.Deductees);
        Assert.Equal(DeducteePan, row.DeducteePan);
        Assert.Equal("Consultant", row.DeducteeName);
        Assert.Equal("194J(b)", row.SectionCode);
        Assert.Equal("94J-B", row.FvuSectionCode);
        Assert.Equal(TxnDate, row.DeductionDate);
        Assert.Equal(Money.FromRupees(1_00_000m), row.AmountPaid);   // amount paid ₹1,00,000
        Assert.Equal(Money.FromRupees(10_000m), row.TdsAmount);      // TDS deducted ₹10,000
        Assert.Equal(1000, row.RateBasisPoints);
        Assert.Equal(10.00m, row.RatePercent);
        Assert.True(row.PanApplied);

        var ch = Assert.Single(q1.Challans);
        Assert.Equal("00123", ch.ChallanNo);
        Assert.Equal(Money.FromRupees(10_000m), ch.Amount);         // TDS deposited ₹10,000
        Assert.Same(row, Assert.Single(ch.DeducteeRows));

        // Return control totals: 1 deductee, 1 challan, total deducted == total deposited == ₹10,000; amount ₹1,00,000.
        var ct = q1.ControlTotals;
        Assert.Equal(1, ct.DeducteeRecordCount);
        Assert.Equal(1, ct.ChallanRecordCount);
        Assert.Equal(Money.FromRupees(10_000m), ct.TotalTdsDeducted);
        Assert.Equal(Money.FromRupees(1_00_000m), ct.TotalAmountPaid);
        Assert.Equal(Money.FromRupees(10_000m), ct.TotalDepositedAsPerChallans);
        Assert.True(ct.Tallies);
        Assert.Empty(ct.Validate());
        Assert.Equal(Money.FromRupees(10_000m), q1.TotalTdsDeducted);
        Assert.Equal(Money.FromRupees(10_000m), q1.TotalDepositedAsPerChallans);

        // ---- STAGE 4: FVU flat-file control totals. Hand-derived trailer: 1 deductee, 1 challan, ₹10,000 TDS,
        //      ₹1,00,000 paid, ₹10,000 deposited → "FT^1^1^10000.00^100000.00^10000.00". ----
        var file = FvuWriter.Write(q1);
        var dd = FvuRecord(file, "DD");
        Assert.Contains(DeducteePan, dd);
        Assert.Contains("^94J-B^10052025^100000.00^10000.00^10.00^Y^", dd);  // FVU code / date / paid / TDS / rate / PAN
        var ft = FvuRecord(file, "FT");
        Assert.Equal("FT^1^1^10000.00^100000.00^10000.00", ft);              // full hand-derived trailer
        var ftFields = ft.Split('^');
        Assert.Equal(1m, ParseInvariant(ftFields[1]));                       // deductee/line count
        Assert.Equal(1m, ParseInvariant(ftFields[2]));                       // challan count
        var fvuTotalTds = ParseInvariant(ftFields[3]);
        Assert.Equal(10_000m, fvuTotalTds);                                  // FVU control total TDS ₹10,000

        // ---- STAGE 5: Form 16A certificate. Figures equal the 26Q figures. ----
        var cert = Form16A.Build(c, 2025, 1, vendor.Id);
        Assert.False(cert.IsEmpty);
        Assert.Equal(ValidTan, cert.Deductor.Tan);
        Assert.Equal(DeducteePan, cert.Deductee.Pan);
        var certRow = Assert.Single(cert.Deductions);
        Assert.Equal("94J-B", certRow.FvuSectionCode);
        Assert.Equal(Money.FromRupees(1_00_000m), certRow.AmountPaid);
        Assert.Equal(Money.FromRupees(10_000m), certRow.TdsAmount);
        Assert.Equal(Money.FromRupees(1_00_000m), cert.TotalAmountPaid);
        Assert.Equal(Money.FromRupees(10_000m), cert.TotalTdsDeducted);
        Assert.Equal(Money.FromRupees(10_000m), cert.TotalTdsDeposited);

        // ---- CHAIN TIE: the single ₹10,000 is the SAME at every stage. ----
        Assert.Equal(carve.TdsAmount.Amount, q1.TotalTdsDeducted.Amount);         // deduction == 26Q
        Assert.Equal(q1.TotalTdsDeducted.Amount, fvuTotalTds);                    // 26Q == FVU
        Assert.Equal(fvuTotalTds, cert.TotalTdsDeducted.Amount);                  // FVU == 16A
        Assert.Equal(10_000m, cert.TotalTdsDeposited.Amount);                     // 16A deposited == ₹10,000
    }

    // =====================================================================================================
    // GOLDEN 2 — TCS scrap §206C(1) code 6CE. Hand-derived oracle: GST-free scrap sale, assessable base =
    // value = ₹1,00,000 (6CE base is GST-inclusive, but a GST-free sale keeps base == value); TCS = 1% ×
    // 1,00,000 = ₹1,000, collected ADDITIVELY (buyer pays 1,00,000 + 1,000 = ₹1,01,000).
    // =====================================================================================================

    [Fact]
    public void Golden_TCS_scrap_6CE_ties_collection_to_27EQ_to_FVU_to_27D()
    {
        // ---- seed: a company with TCS enabled (no GST — a GST-free scrap sale keeps the ₹1,000 a clean number). ----
        var c = CompanyFactory.CreateSeeded("Golden TCS Co", FyStart);
        new TdsTcsService(c).EnableTcs(new TcsConfig
        {
            Tan = ValidTan, CollectorType = DeductorType.Company,
            ResponsiblePersonName = "A. Sharma", ResponsiblePersonPan = DeducteePan,
            ResponsiblePersonDesignation = "Director", ResponsiblePersonAddress = "12 MG Road",
        });

        var inv = new InventoryService(c);
        var scrap = inv.CreateStockItem("Scrap Metal", inv.CreateStockGroup("Waste").Id, inv.CreateSimpleUnit("Kg", "Kilogram").Id);
        var nature = c.FindNatureOfGoodsByCode("6CE")!;
        scrap.TcsNatureOfGoodsId = nature.Id;
        var main = c.MainLocation!.Id;

        var sales = AddLedger(c, "Scrap Sales", "Sales Accounts", false);
        var buyer = AddLedger(c, "Scrap Buyer", "Sundry Debtors", true);
        buyer.TcsApplicable = true; buyer.CollecteeType = CollecteeType.Individual; buyer.PartyPan = DeducteePan;

        // Confirm the seeded nature we rely on: 6CE, with-PAN 1% (100 bp), GST-inclusive base, no threshold.
        Assert.Equal(100, nature.RateWithPanBp);
        Assert.Equal("6CE", nature.CollectionCode);
        Assert.True(nature.BaseIncludesGst);
        Assert.Null(nature.Threshold);

        // Buy opening stock so the sale has goods to move (10,000 Kg @ ₹50).
        var purchases = AddLedger(c, "Purchases", "Purchase Accounts", true);
        var creditor = AddLedger(c, "Creditor", "Sundry Creditors", false);
        new LedgerService(c).Post(new Voucher(Guid.NewGuid(),
            c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Purchase).Id, FyStart,
            new[] { new EntryLine(purchases.Id, Money.FromRupees(5_00_000m), DrCr.Debit), new EntryLine(creditor.Id, Money.FromRupees(5_00_000m), DrCr.Credit) },
            inventoryLines: new[] { new VoucherInventoryLine(scrap.Id, main, 10_000m, Money.FromRupees(50m)) }));

        // ---- STAGE 1: the additive collection. Hand-derived: base ₹1,00,000, GST ₹0, TCS 1% = ₹1,000. ----
        var value = Money.FromRupees(1_00_000m);
        var col = new TcsService(c).BuildCollection(value, Money.Zero, nature, buyer, TxnDate);

        Assert.True(col.Applies);
        Assert.True(col.Collection.PanApplied);
        Assert.Equal(100, col.Collection.RateBasisPoints);
        Assert.Equal(Money.FromRupees(1_00_000m), col.Collection.AssessableValue); // GST-free ⇒ base == value
        Assert.Equal(Money.FromRupees(1_000m), col.TcsAmount);                      // 1% × 1,00,000 (hand-derived)

        // Additive posting: Dr Buyer = value + TCS = ₹1,01,000; Cr Sales ₹1,00,000 (unchanged); Cr TCS Payable ₹1,000.
        var buyerDebit = value + col.TcsAmount; // ₹1,01,000 — buyer charged sale + TCS on top (NOT a carve-out)
        Assert.Equal(Money.FromRupees(1_01_000m), buyerDebit);
        var sale = new LedgerService(c).Post(new Voucher(Guid.NewGuid(),
            c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Sales).Id, TxnDate,
            new[] { new EntryLine(buyer.Id, buyerDebit, DrCr.Debit), new EntryLine(sales.Id, value, DrCr.Credit), col.TcsPayableLine! },
            inventoryLines: new[] { new VoucherInventoryLine(scrap.Id, main, 1000m, Money.FromRupees(100m)) }));
        Assert.True(VoucherValidator.IsBalanced(sale));

        var payable = new TcsService(c).RequirePayableLedger();
        var drBuyer = Assert.Single(sale.Lines, l => l.LedgerId == buyer.Id);
        Assert.Equal(DrCr.Debit, drBuyer.Side);
        Assert.Equal(Money.FromRupees(1_01_000m), drBuyer.Amount);   // sale + TCS on top
        var crSales = Assert.Single(sale.Lines, l => l.LedgerId == sales.Id);
        Assert.Equal(DrCr.Credit, crSales.Side);
        Assert.Equal(Money.FromRupees(1_00_000m), crSales.Amount);   // sale itself is UNCHANGED (additive, not carved)
        var crPayable = Assert.Single(sale.Lines, l => l.LedgerId == payable.Id);
        Assert.Equal(DrCr.Credit, crPayable.Side);
        Assert.Equal(Money.FromRupees(1_000m), crPayable.Amount);    // Cr TCS Payable ₹1,000
        Assert.Equal(-1_000m, LedgerBalances.SignedClosing(c, payable, TxnDate));
        // The Sales pairing invariant still foots: Sales credit == Σ item value (TCS Payable excluded).
        Assert.Equal(value, sale.InventoryLinesValue);

        // ---- STAGE 2: deposit the ₹1,000 via a stat-payment + ITNS-281 challan (collection code 6CE). ----
        var dep = new TcsDepositService(c);
        var statType = dep.EnsureStatPaymentType();
        var bank = AddLedger(c, "HDFC Bank", "Bank Accounts", true);
        var deposit = new LedgerService(c).Post(dep.BuildStatPayment(Money.FromRupees(1_000m), bank, DepositDate, statType));
        dep.RecordChallan("00123", "0510308", DepositDate, Money.FromRupees(1_000m), "6CE", "200", deposit);

        // ---- STAGE 3: Form 27EQ for Q1. One collectee row; collected == deposited == ₹1,000. ----
        var q1 = Form27EQ.Build(c, 2025, 1);
        var row = Assert.Single(q1.Collectees);
        Assert.Equal(DeducteePan, row.CollecteePan);
        Assert.Equal("Scrap Buyer", row.CollecteeName);
        Assert.Equal("6CE", row.CollectionCode);
        Assert.Equal("6CE", row.FvuCollectionCode);
        Assert.Equal(TxnDate, row.CollectionDate);
        Assert.Equal(Money.FromRupees(1_00_000m), row.AmountReceived); // amount received ₹1,00,000 (GST-free base)
        Assert.Equal(Money.FromRupees(1_000m), row.TcsAmount);         // TCS collected ₹1,000
        Assert.Equal(100, row.RateBasisPoints);
        Assert.Equal(1.00m, row.RatePercent);
        Assert.True(row.PanApplied);

        var ch = Assert.Single(q1.Challans);
        Assert.Equal("00123", ch.ChallanNo);
        Assert.Equal(Money.FromRupees(1_000m), ch.Amount);            // TCS deposited ₹1,000
        Assert.Equal("6CE", ch.CollectionCode);
        Assert.Same(row, Assert.Single(ch.CollecteeRows));

        // Return control totals: 1 collectee, 1 challan, total collected == total deposited == ₹1,000; amount ₹1,00,000.
        var ct = q1.ControlTotals;
        Assert.Equal(1, ct.CollecteeRecordCount);
        Assert.Equal(1, ct.ChallanRecordCount);
        Assert.Equal(Money.FromRupees(1_000m), ct.TotalTcsCollected);
        Assert.Equal(Money.FromRupees(1_00_000m), ct.TotalAmountReceived);
        Assert.Equal(Money.FromRupees(1_000m), ct.TotalDepositedAsPerChallans);
        Assert.True(ct.Tallies);
        Assert.Empty(ct.Validate());
        Assert.Equal(Money.FromRupees(1_000m), q1.TotalTcsCollected);
        Assert.Equal(Money.FromRupees(1_000m), q1.TotalDepositedAsPerChallans);

        // ---- STAGE 4: TCS FVU flat-file control totals. Hand-derived trailer: 1 collectee, 1 challan, ₹1,000 TCS,
        //      ₹1,00,000 received, ₹1,000 deposited → "FT^1^1^1000.00^100000.00^1000.00". ----
        var file = FvuWriter.Write(q1);
        var cl = FvuRecord(file, "CL");
        Assert.Contains(DeducteePan, cl);
        Assert.Contains("^6CE^6CE^10052025^100000.00^1000.00^1.00^Y^", cl);  // code / fvu code / date / recd / TCS / rate / PAN
        var ft = FvuRecord(file, "FT");
        Assert.Equal("FT^1^1^1000.00^100000.00^1000.00", ft);                // full hand-derived trailer
        var ftFields = ft.Split('^');
        Assert.Equal(1m, ParseInvariant(ftFields[1]));                       // collectee/line count
        Assert.Equal(1m, ParseInvariant(ftFields[2]));                       // challan count
        var fvuTotalTcs = ParseInvariant(ftFields[3]);
        Assert.Equal(1_000m, fvuTotalTcs);                                   // FVU control total TCS ₹1,000

        // ---- STAGE 5: Form 27D certificate. Figures equal the 27EQ figures. ----
        var cert = Form27D.Build(c, 2025, 1, buyer.Id);
        Assert.False(cert.IsEmpty);
        Assert.Equal(ValidTan, cert.Collector.Tan);
        Assert.Equal(DeducteePan, cert.Collectee.Pan);
        var certRow = Assert.Single(cert.Collections);
        Assert.Equal("6CE", certRow.CollectionCode);
        Assert.Equal(Money.FromRupees(1_00_000m), certRow.AmountReceived);
        Assert.Equal(Money.FromRupees(1_000m), certRow.TcsAmount);
        Assert.Equal(Money.FromRupees(1_00_000m), cert.TotalAmountReceived);
        Assert.Equal(Money.FromRupees(1_000m), cert.TotalTcsCollected);
        Assert.Equal(Money.FromRupees(1_000m), cert.TotalTcsDeposited);

        // ---- CHAIN TIE: the single ₹1,000 is the SAME at every stage. ----
        Assert.Equal(col.TcsAmount.Amount, q1.TotalTcsCollected.Amount);         // collection == 27EQ
        Assert.Equal(q1.TotalTcsCollected.Amount, fvuTotalTcs);                  // 27EQ == FVU
        Assert.Equal(fvuTotalTcs, cert.TotalTcsCollected.Amount);               // FVU == 27D
        Assert.Equal(1_000m, cert.TotalTcsDeposited.Amount);                    // 27D deposited == ₹1,000
    }
}
