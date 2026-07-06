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

        return company;
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
