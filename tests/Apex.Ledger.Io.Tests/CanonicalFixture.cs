using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Services;

namespace Apex.Ledger.Io.Tests;

/// <summary>
/// Builds a rich "Bright"-style trading company entirely through the domain services + masters, exercising
/// EVERY field the canonical envelope must round-trip: seeded groups/ledgers/voucher-types, custom ledgers with
/// opening balances, GST enabled (config + tax ledgers + a GST party ledger with a valid GSTIN), inventory
/// masters (stock group, unit, godown, stock item with GST details + opening stock), an ordinary accounting
/// voucher, and an <b>item-invoice</b> Sales voucher that both posts accounting Dr/Cr AND moves stock with a
/// hand-built GST tax line (so a <see cref="GstLineTax"/> is present). All figures are chosen paisa-exact so the
/// round-trip tests can assert equality to the paisa. The Io.Tests project references Apex.Ledger but not the
/// fixture harness, so this local builder stands in for the JSON study fixture.
/// </summary>
internal static class CanonicalFixture
{
    // A structurally-valid GSTIN for Maharashtra (27) so PartyGst.EnsureValid / GstConfig pass.
    // 27 + PAN "AAPFU0939F" + entity "1" + "Z" + checksum. Checksum computed by Gstin.ComputeCheckDigit.
    private const string CompanyPan = "AAPFU0939F";

    public static Company BuildBright()
    {
        var fyStart = new DateOnly(2021, 4, 1);
        var books = new DateOnly(2021, 4, 1);
        var company = CompanyFactory.CreateSeeded("Bright Traders", fyStart, books);
        company.MailingName = "Bright Traders Pvt Ltd";
        company.Address = "12 MG Road";
        company.State = "Maharashtra";
        company.Pin = "400001";

        // --- GST on (config + 6 tax ledgers + Round-Off) ---
        var gst = new GstService(company);
        gst.EnableGst(new GstConfig
        {
            Gstin = MakeGstin("27", CompanyPan, '1'),
            HomeStateCode = "27",
            RegistrationType = GstRegistrationType.Regular,
            ApplicableFrom = new DateOnly(2021, 4, 1),
            Periodicity = GstReturnPeriodicity.Monthly,
        });

        // --- accounting ledgers (opening balances, self-balancing) ---
        var capital = company.FindGroupByName("Capital Account")!;
        var cashInHand = company.FindGroupByName("Cash-in-Hand")!;
        var salesGrp = company.FindGroupByName("Sales Accounts")!;
        var debtors = company.FindGroupByName("Sundry Debtors")!;
        var stockInHand = company.FindGroupByName("Stock-in-Hand")!;

        var capitalLedger = new Domain.Ledger(Guid.NewGuid(), "Bright's Capital", capital.Id,
            Money.FromRupees(150000m), openingIsDebit: false);
        var cash = company.FindLedgerByName("Cash")!; // predefined
        cash.OpeningBalance = Money.FromRupees(20000m);
        cash.OpeningIsDebit = true;
        cash.GroupId = cashInHand.Id;

        var salesLedger = new Domain.Ledger(Guid.NewGuid(), "Sales", salesGrp.Id, Money.Zero, openingIsDebit: false,
            salesPurchaseGst: new StockItemGstDetails { HsnSac = "998877", Taxability = GstTaxability.Taxable, RateBasisPoints = 1800, SupplyType = GstSupplyType.Services });

        // A GST party ledger (Regular, in-state) with a valid GSTIN → carries PartyGst.
        var party = new Domain.Ledger(Guid.NewGuid(), "Ram & Co", debtors.Id, Money.Zero, openingIsDebit: true,
            maintainBillByBill: true, defaultCreditPeriodDays: 30,
            partyGst: new PartyGstDetails
            {
                RegistrationType = GstRegistrationType.Regular,
                Gstin = MakeGstin("27", "AAQCS1234K", '1'),
                StateCode = "27",
            });

        var stockLedger = new Domain.Ledger(Guid.NewGuid(), "Closing Stock", stockInHand.Id,
            Money.FromRupees(25000m), openingIsDebit: true);

        company.AddLedger(capitalLedger);
        company.AddLedger(salesLedger);
        company.AddLedger(party);
        company.AddLedger(stockLedger);

        // --- inventory masters ---
        var inv = new InventoryService(company);
        var sg = inv.CreateStockGroup("Electronics");
        var nos = inv.CreateSimpleUnit("Nos", "Numbers", decimalPlaces: 0);
        var mainGodown = company.MainLocation!;
        var item = inv.CreateStockItem("Widget", sg.Id, nos.Id);
        item.Gst = new StockItemGstDetails
        {
            HsnSac = "84713010", Taxability = GstTaxability.Taxable, RateBasisPoints = 1800, SupplyType = GstSupplyType.Goods,
        };
        item.ReorderLevel = 5m;
        item.MinimumOrderQuantity = 10m;
        inv.AddOpeningBalance(item.Id, mainGodown.Id, 100m, Money.FromRupees(150m), batchLabel: "B1");

        var service = new LedgerService(company);

        // --- an ordinary accounting voucher: opening capital receipt into cash (balanced) ---
        var receiptType = company.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Receipt);
        service.Post(new Voucher(Guid.NewGuid(), receiptType.Id, new DateOnly(2021, 4, 2),
            new[]
            {
                new EntryLine(cash.Id, Money.FromRupees(5000m), DrCr.Debit),
                new EntryLine(capitalLedger.Id, Money.FromRupees(5000m), DrCr.Credit),
            },
            number: 1, narration: "Additional capital introduced"));

        // --- an item-invoice Purchase voucher (moves stock INWARD + posts accounting + a GST Input tax line) ---
        // 10 Widgets @ 200 = 2000 taxable; 18% intra → CGST 180 + SGST 180 (Input ITC); party Cr 2360.
        // Purchase ⇒ Inward, so there is no negative-stock risk (and Input tax exercises the other direction).
        var purchaseType = company.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Purchase);
        var purchaseLedger = new Domain.Ledger(Guid.NewGuid(), "Purchase", company.FindGroupByName("Purchase Accounts")!.Id,
            Money.Zero, openingIsDebit: true,
            salesPurchaseGst: new StockItemGstDetails { HsnSac = "84713010", Taxability = GstTaxability.Taxable, RateBasisPoints = 1800, SupplyType = GstSupplyType.Goods });
        company.AddLedger(purchaseLedger);
        var cgstIn = gst.FindTaxLedger(GstTaxHead.Central, GstTaxDirection.Input)!;
        var sgstIn = gst.FindTaxLedger(GstTaxHead.State, GstTaxDirection.Input)!;

        var taxable = Money.FromRupees(2000m);
        var purchaseLines = new[]
        {
            new EntryLine(purchaseLedger.Id, taxable, DrCr.Debit),
            new EntryLine(cgstIn.Id, Money.FromRupees(180m), DrCr.Debit,
                gst: new GstLineTax(GstTaxHead.Central, 900, taxable)),
            new EntryLine(sgstIn.Id, Money.FromRupees(180m), DrCr.Debit,
                gst: new GstLineTax(GstTaxHead.State, 900, taxable)),
            new EntryLine(party.Id, Money.FromRupees(2360m), DrCr.Credit),
        };
        var invLines = new[]
        {
            new VoucherInventoryLine(item.Id, mainGodown.Id, 10m, Money.FromRupees(200m)),
        };
        service.Post(new Voucher(Guid.NewGuid(), purchaseType.Id, new DateOnly(2021, 4, 5),
            purchaseLines, number: 1, narration: "Bought 10 widgets from Ram & Co", partyId: party.Id,
            inventoryLines: invLines));

        AddCostAndBankAndForex(company, service, inv, cash, sg, nos, mainGodown, item);
        AddBatches(company, inv, sg, nos, mainGodown);
        AddBomAndManufacture(company, inv, sg, nos, mainGodown);

        return company;
    }

    /// <summary>
    /// Adds Phase-6 Cluster-2 data so the canonical round-trip proves a <b>Bill of Materials</b> master AND a
    /// posted <b>Manufacturing Journal</b> survive (RQ-9..RQ-15, DP-3, PR-4): a finished good with a multi-line
    /// BOM (two Component lines + one Scrap carve-out line with an explicit paisa rate, plus a Co-Product line
    /// carved out by percent, and a unit-of-manufacture of 10 to exercise the scaling path), a user-created
    /// Manufacturing-Journal voucher type (base Stock Journal + Use as Manufacturing Journal = Yes, RQ-11), and a
    /// manufacture of 20 finished units that posts one Stock-Journal inventory voucher (source components out,
    /// destination FG + carve-outs in). Every quantity/rate is chosen paisa-exact so the round-trip reconciles.
    /// </summary>
    private static void AddBomAndManufacture(Company company, InventoryService inv, StockGroup sg, Unit nos, Godown mainGodown)
    {
        // Finished good + its raw components (all in the same stock group / unit / godown for simplicity).
        var fg = inv.CreateStockItem("Assembled Gadget", sg.Id, nos.Id);
        var compA = inv.CreateStockItem("Raw Part A", sg.Id, nos.Id);
        var compB = inv.CreateStockItem("Raw Part B", sg.Id, nos.Id);
        var scrap = inv.CreateStockItem("Metal Scrap", sg.Id, nos.Id);
        var coProduct = inv.CreateStockItem("Side Product", sg.Id, nos.Id);
        // A carve-out item may default its value to its own standard cost (DP-3) — give the co-product one so the
        // percent-basis and the default-standard-cost paths are both exercised deterministically.
        coProduct.StandardCost = Money.FromRupees(3m);

        // Opening stock so the components can be consumed by the manufacture (never negative — ER-7).
        inv.AddOpeningBalance(compA.Id, mainGodown.Id, 1000m, Money.FromRupees(10m));
        inv.AddOpeningBalance(compB.Id, mainGodown.Id, 1000m, Money.FromRupees(5m));

        // A BOM stated per a block of 10 finished units (unit-of-manufacture = 10 exercises RQ-12 scaling):
        //   • 2 × Raw Part A per block   (Component)
        //   • 3 × Raw Part B per block   (Component)
        //   • 1 × Metal Scrap per block  (Scrap, carved out at an explicit ₹2/unit rate — DP-3 rate basis)
        //   • 1 × Side Product per block (Co-Product, carved out at 5% of finished-good pre-carve cost — DP-3 %)
        var lines = new[]
        {
            new BomLine(BomLineType.Component, compA.Id, quantityPerBlock: 2m, godownId: mainGodown.Id),
            new BomLine(BomLineType.Component, compB.Id, quantityPerBlock: 3m),
            new BomLine(BomLineType.Scrap, scrap.Id, quantityPerBlock: 1m, godownId: mainGodown.Id,
                rate: Money.FromRupees(2m)),
            new BomLine(BomLineType.CoProduct, coProduct.Id, quantityPerBlock: 1m,
                percentOfFinishedGoodCost: 5m),
        };
        var bom = new BomService(company).CreateBom(fg.Id, "Standard", unitOfManufacture: 10m, lines);

        // A user-created Manufacturing-Journal voucher type (RQ-11) and a manufacture of 20 finished units.
        var mfgJournal = new ManufacturingJournalService(company);
        var mfgType = mfgJournal.CreateManufacturingJournalType("Manufacturing Journal");
        mfgJournal.Manufacture(mfgType.Id, bom.Id, quantity: 20m, date: new DateOnly(2021, 4, 15),
            consumptionGodownId: mainGodown.Id, productionGodownId: mainGodown.Id,
            additionalCosts: new[] { new ManufacturingAdditionalCost("Labour", Money.FromRupees(100m)) });
    }

    /// <summary>
    /// Adds Phase-6 batch data so the canonical round-trip proves batch masters survive (RQ-1/RQ-3/RQ-6): a
    /// batch-tracked item (all three switches on) with two batch masters — one dated (mfg + absolute expiry +
    /// per-batch inward cost layer + godown) and one carrying an expiry PERIOD instead of an absolute date — plus
    /// a Receipt-Note inventory voucher that moves stock in against a batch label (a "batch sale/movement" line).
    /// </summary>
    private static void AddBatches(Company company, InventoryService inv, StockGroup sg, Unit nos, Godown mainGodown)
    {
        var med = inv.CreateStockItem("Paracetamol", sg.Id, nos.Id);
        med.MaintainInBatches = true;
        med.TrackManufacturingDate = true;
        med.UseExpiryDates = true;

        var batches = new BatchService(company);
        batches.CreateBatch(med.Id, "LOT-A",
            manufacturingDate: new DateOnly(2021, 1, 10),
            expiryDate: new DateOnly(2023, 1, 10),
            godownId: mainGodown.Id,
            inwardQuantity: 250m,
            inwardRate: Money.FromRupees(12.34m));
        batches.CreateBatch(med.Id, "LOT-B",
            manufacturingDate: new DateOnly(2021, 3, 1),
            expiryPeriod: new ExpiryPeriod(18, ExpiryPeriodUnit.Months));

        // Opening batch layer carrying a batch label (per-line batch allocation).
        inv.AddOpeningBalance(med.Id, mainGodown.Id, 40m, Money.FromRupees(12.34m), batchLabel: "LOT-A");

        // A Receipt Note moving 20 units IN against batch LOT-B (a batch movement line).
        var receiptNoteType = company.VoucherTypes.First(t => t.BaseType == VoucherBaseType.ReceiptNote);
        var invPosting = new InventoryPostingService(company);
        invPosting.Post(new InventoryVoucher(Guid.NewGuid(), receiptNoteType.Id, new DateOnly(2021, 4, 12),
            new[] { new InventoryAllocation(med.Id, mainGodown.Id, 20m, StockDirection.Inward,
                rate: Money.FromRupees(12.34m), batchLabel: "LOT-B") },
            number: 2, narration: "GRN for 20 paracetamol (batch LOT-B)"));
    }

    /// <summary>
    /// Extends the fixture with EVERY previously-dropped data type so the round-trip proves it survives: a cost
    /// category + centre (with a cost-allocated payment line), a bank ledger (with a bank allocation on a bank line),
    /// a foreign currency + exchange rate (with a forex line on a foreign-currency ledger), a budget, a scenario,
    /// and a Receipt-Note inventory voucher (inward, so it never risks negative stock). Every voucher stays balanced.
    /// </summary>
    private static void AddCostAndBankAndForex(
        Company company, LedgerService service, InventoryService inv, Domain.Ledger cash,
        StockGroup sg, Unit nos, Godown mainGodown, StockItem item)
    {
        // --- cost accounting: a category + a centre, and a Salary expense ledger with cost centres applicable ---
        var category = new CostCategory(Guid.NewGuid(), "Departments", allocateRevenueItems: true,
            allocateNonRevenueItems: true);
        company.AddCostCategory(category);
        var centre = new CostCentre(Guid.NewGuid(), "Sales Dept", category.Id);
        company.AddCostCentre(centre);

        var indirectExp = company.FindGroupByName("Indirect Expenses")!;
        var salary = new Domain.Ledger(Guid.NewGuid(), "Salary", indirectExp.Id, Money.Zero, openingIsDebit: true,
            costCentresApplicable: true);
        company.AddLedger(salary);

        // --- banking: an HDFC bank ledger (cheque printing on) under Bank Accounts ---
        var bankGrp = company.FindGroupByName("Bank Accounts")!;
        var bank = new Domain.Ledger(Guid.NewGuid(), "HDFC Bank", bankGrp.Id, Money.FromRupees(50000m),
            openingIsDebit: true, enableChequePrinting: true, chequePrintingBankName: "HDFC Bank");
        company.AddLedger(bank);

        // Payment: Dr Salary 3000 (allocated 100% to Sales Dept) / Cr HDFC Bank 3000 (bank allocation: cheque).
        var paymentType = company.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Payment);
        var salaryLine = new EntryLine(salary.Id, Money.FromRupees(3000m), DrCr.Debit,
            costAllocations: new[] { new CostAllocation(category.Id, centre.Id, Money.FromRupees(3000m)) });
        var bankLine = new EntryLine(bank.Id, Money.FromRupees(3000m), DrCr.Credit,
            bankAllocation: new BankAllocation(BankTransactionType.ChequeOrDD, "000123",
                instrumentDate: new DateOnly(2021, 4, 6), bankDate: new DateOnly(2021, 4, 8)));
        service.Post(new Voucher(Guid.NewGuid(), paymentType.Id, new DateOnly(2021, 4, 6),
            new[] { salaryLine, bankLine }, number: 1, narration: "April salary by cheque"));

        // --- multi-currency: a USD currency + a dated rate; a USD-denominated expense ledger + a forex line ---
        var usd = new Currency(Guid.NewGuid(), "$", "USD", decimalPlaces: 2);
        company.AddCurrency(usd);
        company.AddExchangeRate(new ExchangeRate(Guid.NewGuid(), usd.Id, new DateOnly(2021, 4, 1),
            standardRate: 75m, sellingRate: 75.5m, buyingRate: 74.5m));

        var foreignExp = new Domain.Ledger(Guid.NewGuid(), "Foreign Consulting", indirectExp.Id, Money.Zero,
            openingIsDebit: true, currencyId: usd.Id);
        company.AddLedger(foreignExp);

        // Payment: Dr Foreign Consulting US$100 × 75 = ₹7500 (forex line) / Cr Cash ₹7500.
        var forexLine = new EntryLine(foreignExp.Id, Money.FromRupees(7500m), DrCr.Debit,
            forex: new ForexInfo(usd.Id, Money.FromRupees(100m), 75m));
        var cashPayLine = new EntryLine(cash.Id, Money.FromRupees(7500m), DrCr.Credit);
        service.Post(new Voucher(Guid.NewGuid(), paymentType.Id, new DateOnly(2021, 4, 7),
            new[] { forexLine, cashPayLine }, number: 2, narration: "US consulting fee"));

        // --- budget: a target on the Salary ledger over the year ---
        var budget = new Budget(Guid.NewGuid(), "FY Budget", new DateOnly(2021, 4, 1), new DateOnly(2022, 3, 31));
        budget.AddLine(BudgetLine.ForLedger(salary.Id, BudgetType.OnNettTransactions, Money.FromRupees(36000m)));
        company.AddBudget(budget);

        // --- scenario: a what-if column including the Payment voucher type ---
        var scenario = new Scenario(Guid.NewGuid(), "Provisional", includeActuals: true,
            includedTypeIds: new[] { paymentType.Id });
        company.AddScenario(scenario);

        // --- inventory voucher: a Receipt Note (inward) moving 5 more widgets in (never negative) ---
        var receiptNoteType = company.VoucherTypes.First(t => t.BaseType == VoucherBaseType.ReceiptNote);
        var invPosting = new InventoryPostingService(company);
        invPosting.Post(new InventoryVoucher(Guid.NewGuid(), receiptNoteType.Id, new DateOnly(2021, 4, 9),
            new[] { new InventoryAllocation(item.Id, mainGodown.Id, 5m, StockDirection.Inward,
                rate: Money.FromRupees(200m), batchLabel: "B2") },
            number: 1, narration: "GRN for 5 widgets"));
    }

    private static string MakeGstin(string stateCode, string pan, char entity)
    {
        var body = stateCode + pan + entity + "Z";
        var check = Gstin.ComputeCheckDigit(body + "0"); // 15th char ignored by ComputeCheckDigit
        return body + check;
    }
}
