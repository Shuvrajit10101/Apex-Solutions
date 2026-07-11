using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Reports;
using Apex.Ledger.Services;
using Xunit;
using Domain = Apex.Ledger.Domain;

namespace Apex.Ledger.Tests;

/// <summary>
/// Phase 7 slice 6 — <b>Form 27EQ</b> (<see cref="Form27EQ"/>) as a pure quarterly projection over the posted TCS
/// collections + recorded challans. The exact mirror of <see cref="Form26QTests"/> for the collector's side. Proves
/// the golden worked example (a scrap ₹1,00,000 + 18% GST sale collecting ₹1,180 TCS at 1% on the ₹1,18,000
/// GST-inclusive base, plus its stat-payment challan) yields exactly one collectee row + one challan block that
/// reconcile to the "TCS Payable" postings and whose control totals tally; that a <b>cross-FY</b> deposit is
/// attributed to the <b>collection</b> quarter (never double-windowed); that a challan-boundary split is counted
/// once (the S4 regression, applied to TCS); and ER-13 (a non-TCS / empty quarter yields an empty return).
/// </summary>
public class Form27EQTests
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

    /// <summary>A scrap seller's world: a §206C(6CE) scrap item with GST, a Scrap Sales ledger, a collectee buyer, and
    /// a large purchased stock so many sales can be posted. Returned as a bag the sale helper reads.</summary>
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

        // Buy a large stock so any number of ₹1,00,000 (1000 Kg) sales have goods to move.
        var purchases = AddLedger(c, "Purchases", "Purchase Accounts", true);
        var creditor = AddLedger(c, "Creditor", "Sundry Creditors", false);
        new LedgerService(c).Post(new Voucher(Guid.NewGuid(), c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Purchase).Id,
            c.BooksBeginFrom,
            new[] { new EntryLine(purchases.Id, Money.FromRupees(5_00_000m), DrCr.Debit), new EntryLine(creditor.Id, Money.FromRupees(5_00_000m), DrCr.Credit) },
            inventoryLines: new[] { new VoucherInventoryLine(scrap.Id, main, 10_000m, Money.FromRupees(50m)) }));

        return new Scene(c, scrap, sales, buyer, main);
    }

    /// <summary>Posts a scrap sale (1000 Kg @ ₹100 = ₹1,00,000 + 18% GST → 1% TCS on ₹1,18,000 = ₹1,180) dated
    /// <paramref name="on"/>. Collects ₹1,180 into "TCS Payable".</summary>
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

    /// <summary>Deposits <paramref name="amount"/> via a Stat Payment on <paramref name="on"/> and records + links an
    /// ITNS-281 challan for collection code <paramref name="code"/>.</summary>
    private static TcsChallan Deposit(Company c, Money amount, DateOnly on, string challanNo, string code = "6CE")
    {
        var dep = new TcsDepositService(c);
        var statType = dep.EnsureStatPaymentType();
        var bank = c.FindLedgerByName("HDFC Bank") ?? AddLedger(c, "HDFC Bank", "Bank Accounts", true);
        var posted = new LedgerService(c).Post(dep.BuildStatPayment(amount, bank, on, statType));
        return dep.RecordChallan(challanNo, "0510308", on, amount, code, "200", posted);
    }

    // ---- the golden worked example: one collectee row + one challan block, reconciled, totals tally ----

    [Fact]
    public void Golden_quarter_has_one_collectee_row_and_one_challan_reconciling_to_payable()
    {
        var s = BuildScene(NewTcsCompany(FyStart));
        BookScrapSale(s, new DateOnly(2025, 5, 10));                                    // Q1 collection
        Deposit(s.C, Money.FromRupees(1_180m), new DateOnly(2025, 6, 5), "00123");      // deposited in Q1

        var q1 = Form27EQ.Build(s.C, 2025, 1);

        // Collector block from F11.
        Assert.Equal(ValidTan, q1.Collector.Tan);
        Assert.Equal(DeductorType.Company, q1.Collector.CollectorType);
        Assert.Equal("A. Sharma", q1.Collector.ResponsiblePersonName);
        Assert.Equal("2025-26", q1.FinancialYearLabel);
        Assert.Equal("Q1", q1.QuarterLabel);

        // Exactly one collectee row: ₹1,180 TCS on ₹1,18,000, code 6CE, 1%.
        var row = Assert.Single(q1.Collectees);
        Assert.Equal(BuyerPan, row.CollecteePan);
        Assert.Equal("Scrap Buyer", row.CollecteeName);
        Assert.Equal("6CE", row.CollectionCode);
        Assert.Equal("6CE", row.FvuCollectionCode);
        Assert.Equal(new DateOnly(2025, 5, 10), row.CollectionDate);
        Assert.Equal(Money.FromRupees(1_18_000m), row.AmountReceived);
        Assert.Equal(Money.FromRupees(1_180m), row.TcsAmount);
        Assert.Equal(100, row.RateBasisPoints);
        Assert.Equal(1.00m, row.RatePercent);
        Assert.True(row.PanApplied);

        // Exactly one challan block carrying that collectee row.
        var challan = Assert.Single(q1.Challans);
        Assert.Equal("00123", challan.ChallanNo);
        Assert.Equal("0510308", challan.BsrCode);
        Assert.Equal(Money.FromRupees(1_180m), challan.Amount);
        Assert.Equal("6CE", challan.CollectionCode);
        Assert.Same(row, Assert.Single(challan.CollecteeRows));

        // Reconciles to the "TCS Payable" credit postings for the quarter, by construction.
        Assert.Equal(Money.FromRupees(1_180m), q1.TotalTcsCollected);
        Assert.Equal(PayableCreditsInQuarter(s.C, 2025, 1), q1.TotalTcsCollected);
        Assert.Equal(Money.FromRupees(1_180m), q1.TotalDepositedAsPerChallans);
    }

    [Fact]
    public void Golden_control_totals_tally()
    {
        var s = BuildScene(NewTcsCompany(FyStart));
        BookScrapSale(s, new DateOnly(2025, 5, 10));
        Deposit(s.C, Money.FromRupees(1_180m), new DateOnly(2025, 6, 5), "00123");

        var ct = Form27EQ.Build(s.C, 2025, 1).ControlTotals;
        Assert.Equal(1, ct.CollecteeRecordCount);
        Assert.Equal(1, ct.ChallanRecordCount);
        Assert.Equal(Money.FromRupees(1_180m), ct.TotalTcsCollected);
        Assert.Equal(Money.FromRupees(1_18_000m), ct.TotalAmountReceived);
        Assert.Equal(Money.FromRupees(1_180m), ct.TotalDepositedAsPerChallans);
        Assert.True(ct.Tallies);
        Assert.Empty(ct.Validate());
    }

    [Fact]
    public void Control_totals_flag_an_undeposited_collection()
    {
        var s = BuildScene(NewTcsCompany(FyStart));
        BookScrapSale(s, new DateOnly(2025, 5, 10));
        // No deposit at all — the ₹1,180 is undeposited.

        var q1 = Form27EQ.Build(s.C, 2025, 1);
        Assert.Single(q1.Collectees);
        Assert.Empty(q1.Challans);
        var ct = q1.ControlTotals;
        Assert.False(ct.Tallies);
        var problem = Assert.Single(ct.Validate());
        Assert.Contains("undeposited", problem);
    }

    // ---- challan-boundary splits: a portion is counted once, never at full across two challans (the S4 fix) ----

    [Fact]
    public void A_challan_boundary_split_across_two_collections_is_not_double_counted()
    {
        // Two ₹1,180 Q1 collections; a ₹1,500 challan then an ₹860 challan. FIFO: the first challan discharges C1 in
        // full and ₹320 of C2; the second discharges C2's remaining ₹860. C2 must be counted once at its ₹1,180 (as a
        // ₹320 + ₹860 split), never twice at full ₹1,180.
        var s = BuildScene(NewTcsCompany(FyStart));
        BookScrapSale(s, new DateOnly(2025, 5, 10));   // C1
        BookScrapSale(s, new DateOnly(2025, 5, 11));   // C2
        Deposit(s.C, Money.FromRupees(1_500m), new DateOnly(2025, 6, 5), "AA");
        Deposit(s.C, Money.FromRupees(860m), new DateOnly(2025, 6, 6), "BB");

        var q1 = Form27EQ.Build(s.C, 2025, 1);

        Assert.Equal(Money.FromRupees(2_360m), q1.TotalTcsCollected);
        // Deposited against the quarter's collections equals the gross collected — no phantom over-count.
        Assert.Equal(Money.FromRupees(2_360m), q1.TotalTcsDepositedForQuarter);
        var perChallanTcs = q1.Challans.Sum(ch => ch.CollecteeRows.Sum(r => r.TcsAmount.Amount));
        Assert.Equal(2_360m, perChallanTcs);
        // The assessable amount-received is attributed once per collection (opening portion), never double-counted.
        var perChallanReceived = q1.Challans.Sum(ch => ch.CollecteeRows.Sum(r => r.AmountReceived.Amount));
        Assert.Equal(2_36_000m, perChallanReceived);
        Assert.True(q1.ControlTotals.Tallies);
        Assert.Empty(q1.ControlTotals.Validate());
    }

    [Fact]
    public void A_short_deposited_collection_is_flagged_not_falsely_tallied()
    {
        // A ₹1,180 collection with only ₹800 deposited: the challan block must reflect the ₹800 actually covered (not
        // the full ₹1,180), so the return correctly flags ₹380 undeposited.
        var s = BuildScene(NewTcsCompany(FyStart));
        BookScrapSale(s, new DateOnly(2025, 5, 10));
        Deposit(s.C, Money.FromRupees(800m), new DateOnly(2025, 6, 5), "AA");

        var q1 = Form27EQ.Build(s.C, 2025, 1);
        Assert.Equal(Money.FromRupees(800m), q1.TotalTcsDepositedForQuarter);
        Assert.False(q1.ControlTotals.Tallies);
        Assert.Contains("undeposited", Assert.Single(q1.ControlTotals.Validate()));
    }

    // ---- the cross-FY carry: a March collection deposited in April belongs to Q4 (collection quarter) ----

    [Fact]
    public void Cross_fy_deposit_is_attributed_to_the_collection_quarter_not_the_deposit_date()
    {
        // Books open in FY2024-25 so a 20-Mar-2025 (Q4) collection is valid.
        var s = BuildScene(NewTcsCompany(new DateOnly(2024, 4, 1)));
        BookScrapSale(s, new DateOnly(2025, 3, 20));                                       // Q4 FY2024-25
        Deposit(s.C, Money.FromRupees(1_180m), new DateOnly(2025, 4, 7), "00777");         // deposited 7-Apr (next FY window)

        // Q4 FY2024-25: the collection is here AND the April challan (which covers it) is listed here.
        var q4 = Form27EQ.Build(s.C, 2024, 4);
        var row = Assert.Single(q4.Collectees);
        Assert.Equal(new DateOnly(2025, 3, 20), row.CollectionDate);
        var ch = Assert.Single(q4.Challans);
        Assert.Equal("00777", ch.ChallanNo);
        Assert.Equal(new DateOnly(2025, 4, 7), ch.DepositDate);   // deposited in April but attributed to Q4
        Assert.Same(row, Assert.Single(ch.CollecteeRows));
        Assert.True(q4.ControlTotals.Tallies);

        // Q1 FY2025-26: NOT double-windowed — no collection and the April challan is NOT re-listed here.
        var q1Next = Form27EQ.Build(s.C, 2025, 1);
        Assert.Empty(q1Next.Collectees);
        Assert.Empty(q1Next.Challans);
        Assert.True(q1Next.IsEmpty);
    }

    // ---- a single challan covering >1 quarter is attributed per quarter, never double-counted ----

    [Fact]
    public void A_single_challan_covering_two_quarters_is_attributed_per_quarter_not_double_counted()
    {
        // One ₹2,360 challan FIFO-covers a Q1 ₹1,180 collection and a Q2 ₹1,180 collection. It must be listed in each
        // quarter at only that quarter's ₹1,180 covered portion — never at its full ₹2,360 in BOTH returns (which would
        // double-count the same deposit across the two quarterly returns and make the CD deposit total disagree with the
        // Σ CL tax within each return, an FVU-rejecting file).
        var s = BuildScene(NewTcsCompany(FyStart));
        BookScrapSale(s, new DateOnly(2025, 5, 10));   // C1 — Q1
        BookScrapSale(s, new DateOnly(2025, 8, 15));   // C2 — Q2
        Deposit(s.C, Money.FromRupees(2_360m), new DateOnly(2025, 10, 5), "MULTIQ");   // one challan discharges both

        var q1 = Form27EQ.Build(s.C, 2025, 1);
        var ch1 = Assert.Single(q1.Challans);
        Assert.Equal(Money.FromRupees(1_180m), ch1.Amount);                              // in-quarter portion, NOT ₹2,360
        Assert.Equal(1_180m, ch1.CollecteeRows.Sum(r => r.TcsAmount.Amount));            // CD deposit == Σ CL tax
        Assert.Equal(Money.FromRupees(1_180m), q1.TotalDepositedAsPerChallans);
        Assert.True(q1.ControlTotals.Tallies);
        Assert.Empty(q1.ControlTotals.Validate());

        var q2 = Form27EQ.Build(s.C, 2025, 2);
        var ch2 = Assert.Single(q2.Challans);
        Assert.Equal(Money.FromRupees(1_180m), ch2.Amount);
        Assert.Equal(1_180m, ch2.CollecteeRows.Sum(r => r.TcsAmount.Amount));
        Assert.Equal(Money.FromRupees(1_180m), q2.TotalDepositedAsPerChallans);
        Assert.True(q2.ControlTotals.Tallies);

        // The ₹2,360 deposit is reported exactly ONCE across the two quarterly returns — never twice (₹4,720).
        Assert.Equal(2_360m, q1.TotalDepositedAsPerChallans.Amount + q2.TotalDepositedAsPerChallans.Amount);

        // Q3 carries neither the collections nor the challan (it discharges nothing in Q3).
        Assert.True(Form27EQ.Build(s.C, 2025, 3).IsEmpty);
    }

    // ---- ER-13: a non-TCS / empty return is inert ----

    [Fact]
    public void Empty_return_for_a_company_with_no_collections()
    {
        var c = NewTcsCompany(FyStart);
        var q1 = Form27EQ.Build(c, 2025, 1);
        Assert.True(q1.IsEmpty);
        Assert.Empty(q1.Collectees);
        Assert.Empty(q1.Challans);
        Assert.Equal(Money.Zero, q1.TotalTcsCollected);
        Assert.True(q1.ControlTotals.Tallies);
    }

    [Fact]
    public void A_collection_only_appears_in_its_own_quarter()
    {
        var s = BuildScene(NewTcsCompany(FyStart));
        BookScrapSale(s, new DateOnly(2025, 8, 15));  // Q2

        Assert.Empty(Form27EQ.Build(s.C, 2025, 1).Collectees);    // not in Q1
        Assert.Single(Form27EQ.Build(s.C, 2025, 2).Collectees);   // in Q2
        Assert.Empty(Form27EQ.Build(s.C, 2025, 3).Collectees);    // not in Q3
    }

    /// <summary>Σ "TCS Payable" credit postings on non-cancelled vouchers in the quarter window — the ledger figure
    /// the return must reconcile to.</summary>
    private static Money PayableCreditsInQuarter(Company c, int fy, int quarter)
    {
        var (from, to) = Form27EQ.QuarterWindow(fy, quarter);
        var payable = new TcsService(c).RequirePayableLedger();
        var sum = 0m;
        foreach (var v in c.Vouchers)
        {
            if (v.Cancelled || v.Date < from || v.Date > to) continue;
            foreach (var line in v.Lines)
                if (line.LedgerId == payable.Id && line.Side == DrCr.Credit)
                    sum += line.Amount.Amount;
        }
        return new Money(sum);
    }
}
