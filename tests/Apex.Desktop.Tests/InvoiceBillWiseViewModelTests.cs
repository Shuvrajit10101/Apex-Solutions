using System;
using System.IO;
using System.Linq;
using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Reports;
using Apex.Ledger.Services;
using Apex.Desktop.Services;
using Apex.Desktop.ViewModels;
using DomainLedger = Apex.Ledger.Domain.Ledger;

namespace Apex.Desktop.Tests;

/// <summary>
/// <b>G-1 (CRITICAL)</b> — Bill-wise Details must fire in the INVOICE modes, not only on the plain Dr/Cr grid.
///
/// <para>The defect these tests pin: <c>AcceptItemInvoice</c> and <c>AcceptAccountingInvoice</c> built the party
/// <see cref="EntryLine"/> with <b>no</b> bill allocations, while <see cref="Outstandings"/> only counts lines that
/// HAVE allocations. A company invoicing normally therefore got an empty Receivables report, empty ageing, no
/// overdue tracking and nothing for Ctrl+B to settle — with no error and no warning anywhere.</para>
///
/// <para>Corpus: the Bill-wise sub-screen sits on BOTH invoice modes — Study Guide p.79 step 7 (Purchase Item
/// Invoice), p.80 step 6 (Purchase Accounting Invoice), p.81 step 6 (Sales Item Invoice), p.82 step 5 (Sales
/// Accounting Invoice).</para>
///
/// <para><b>Money fixtures are odd-paisa on purpose.</b> Round figures assert nothing — a ±₹0.50 defect survived
/// this codebase's whole life under six round-number assertions.</para>
/// </summary>
public sealed class InvoiceBillWiseViewModelTests : IDisposable
{
    private readonly string _tempDir;
    private readonly CompanyStorage _storage;

    public InvoiceBillWiseViewModelTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "ApexInvoiceBillWise_" + Guid.NewGuid().ToString("N"));
        _storage = new CompanyStorage(_tempDir);
    }

    // ---------------------------------------------------------------- scaffolding

    private sealed class Kit
    {
        public required MainWindowViewModel Vm { get; init; }
        public required string CompanyName { get; init; }
        public required Guid ItemId { get; init; }
        public required Guid MainGodownId { get; init; }
        public required Guid PurchasesLedgerId { get; init; }
        public required Guid SalesLedgerId { get; init; }
        public required Guid SupplierId { get; init; }
        public required Guid CustomerId { get; init; }
        public required Guid PlainCustomerId { get; init; }
        public required Guid ServiceIncomeId { get; init; }
        public required Guid CashId { get; init; }
    }

    /// <summary>
    /// A seeded company with one "Widget" (Nos) item, a Purchases/Sales ledger, a <b>bill-by-bill</b> supplier and
    /// customer, a NON-bill-by-bill customer (the ER-13 control), a service-income ledger for the accounting
    /// invoice, and Cash for settlement receipts.
    /// </summary>
    private Kit NewKit(string companyName, decimal openingQty = 500m)
    {
        var vm = new MainWindowViewModel(_storage);
        vm.NewCompanyName = companyName;
        vm.CreateCompany();
        var c = vm.Company!;

        var masters = new InventoryService(c);
        var grp = masters.CreateStockGroup("Goods");
        var nos = masters.CreateSimpleUnit("Nos", "Numbers");
        var item = masters.CreateStockItem("Widget", grp.Id, nos.Id);
        masters.AddOpeningBalance(item.Id, c.MainLocation!.Id, openingQty, Money.FromRupees(100m));

        var purchases = AddLedger(c, "Purchases", "Purchase Accounts");
        var sales = AddLedger(c, "Sales", "Sales Accounts");
        var serviceIncome = AddLedger(c, "Consultancy Income", "Sales Accounts");
        var supplier = AddLedger(c, "Acme Supplies", "Sundry Creditors", billWise: true);
        var customer = AddLedger(c, "Beta Buyers", "Sundry Debtors", billWise: true);
        var plainCustomer = AddLedger(c, "Gamma Cash Buyers", "Sundry Debtors", billWise: false);
        var cash = AddLedger(c, "Cash-in-Hand A", "Cash-in-Hand");

        _storage.Save(c);

        return new Kit
        {
            Vm = vm,
            CompanyName = companyName,
            ItemId = item.Id,
            MainGodownId = c.MainLocation!.Id,
            PurchasesLedgerId = purchases.Id,
            SalesLedgerId = sales.Id,
            SupplierId = supplier.Id,
            CustomerId = customer.Id,
            PlainCustomerId = plainCustomer.Id,
            ServiceIncomeId = serviceIncome.Id,
            CashId = cash.Id,
        };
    }

    private static DomainLedger AddLedger(Company c, string name, string groupName, bool billWise = false)
    {
        var group = c.FindGroupByName(groupName) ?? throw new InvalidOperationException($"No group '{groupName}'.");
        var ledger = new DomainLedger(Guid.NewGuid(), name, group.Id, Money.Zero, openingIsDebit: false)
        {
            MaintainBillByBill = billWise,
        };
        c.AddLedger(ledger);
        return ledger;
    }

    private Company Reload(string companyName)
    {
        var entry = _storage.ListCompanies().Single(e => e.Name == companyName);
        return _storage.Load(entry);
    }

    private static DateOnly AsOf(Company c) => c.FinancialYearStart.AddYears(1).AddDays(-1);

    private static void SelectParty(VoucherEntryViewModel entry, Guid partyId) =>
        entry.SelectedParty = entry.Parties.Single(p => p.Ledger?.Id == partyId);

    private static void FillItemLine(VoucherEntryViewModel entry, Kit k, string qty, string rate)
    {
        var line = entry.InventoryLines[0];
        line.SelectedItem = entry.StockItems.Single(i => i.Id == k.ItemId);
        line.SelectedGodown = entry.Godowns.Single(g => g.Id == k.MainGodownId);
        line.QuantityText = qty;
        line.RateText = rate;
    }

    // ================================================================ (1) Sales ITEM invoice

    /// <summary>
    /// SG p.81 step 6. A Sales item invoice to a bill-by-bill customer must open the Bill-wise panel, post the
    /// allocation onto the party leg, and therefore appear in Receivables. 3 @ ₹1,234.57 = ₹3,703.71 (odd paisa).
    /// </summary>
    [Fact]
    public void Sales_item_invoice_to_a_billwise_party_posts_bill_allocations_and_opens_a_receivable()
    {
        var k = NewKit("Item Invoice BillWise Co");
        k.Vm.OpenVoucher(VoucherBaseType.Sales);
        var entry = k.Vm.VoucherEntry!;
        k.Vm.ToggleItemInvoice();
        Assert.True(entry.IsItemInvoice);

        SelectParty(entry, k.CustomerId);
        FillItemLine(entry, k, "3", "1234.57");
        entry.RecalculateItemInvoice();

        // The Bill-wise SUB-SCREEN is opt-in — TallyPrime ships "Use default Bill-wise details for Bill Allocation"
        // at Yes and allocates silently (see DefaultBillAllocationTests). Clearing it is exactly what Tally-Book p.81
        // does to make the sub-screen appear.
        entry.UseDefaultBillWiseAllocation = false;

        // The panel is on (layer 3: the ledger says Maintain balances bill-by-bill) and pre-seeded to the total.
        Assert.True(entry.ShowInvoiceBillWise);
        Assert.Single(entry.InvoiceBillAllocations);
        Assert.Equal(3703.71m, entry.InvoicePartyTotal);
        Assert.Equal(3703.71m, entry.InvoiceBillAllocations[0].ParsedAmount);

        // The seeded row opens PRE-FILLED with the voucher number as its reference (SG p.92), so it is already valid.
        // Blanking that field does not un-name the bill — the row is still screen-owned, so the default comes back.
        Assert.Equal(entry.FormattedVoucherNumber, entry.InvoiceBillAllocations[0].Name);
        entry.InvoiceBillAllocations[0].Name = string.Empty;
        Assert.Equal(entry.FormattedVoucherNumber, entry.InvoiceBillAllocations[0].Name);

        // A New Ref still needs a bill reference name (SG p.90) — an unnamed New Ref would open an unmatchable bill.
        // The rule bites on an OPERATOR-owned row, which the auto-fill never restamps.
        var unnamed = entry.AddInvoiceBillAllocation(BillRefType.NewRef);
        unnamed.AmountText = "1.00";
        Assert.False(entry.InvoiceBillSplitOk);
        Assert.False(entry.CanAccept);
        entry.RemoveInvoiceBillAllocation(unnamed);

        entry.InvoiceBillAllocations[0].Name = "INV-Beta-001";
        Assert.True(entry.InvoiceBillSplitOk);
        Assert.True(entry.CanAccept);
        Assert.True(entry.Accept());

        var c = k.Vm.Company!;
        var type = c.VoucherTypes.Single(t => t.BaseType == VoucherBaseType.Sales && t.IsActive);
        var posted = c.Vouchers.Single(v => v.TypeId == type.Id);
        var partyLine = posted.Lines.Single(l => l.LedgerId == k.CustomerId);

        Assert.True(partyLine.HasBillAllocations);
        var alloc = Assert.Single(partyLine.BillAllocations);
        Assert.Equal(BillRefType.NewRef, alloc.RefType);
        Assert.Equal("INV-Beta-001", alloc.Name);
        Assert.Equal(3703.71m, alloc.Amount.Amount);

        // THE POINT OF THE GAP: the receivable actually shows up.
        var customer = c.FindLedger(k.CustomerId)!;
        var bill = Assert.Single(Outstandings.OpenBillsFor(c, customer, AsOf(c)));
        Assert.Equal("INV-Beta-001", bill.Reference);
        Assert.Equal(3703.71m, bill.Pending.Amount);

        // …and survives a round-trip through the store.
        var reloaded = Reload(k.CompanyName);
        var rBill = Assert.Single(Outstandings.OpenBillsFor(reloaded, reloaded.FindLedger(k.CustomerId)!, AsOf(reloaded)));
        Assert.Equal(3703.71m, rBill.Pending.Amount);
    }

    // ================================================================ (2) Sales ACCOUNTING invoice

    /// <summary>SG p.82 step 5 — the same sub-screen on the Sales accounting (service) invoice. ₹8,765.43.</summary>
    [Fact]
    public void Sales_accounting_invoice_to_a_billwise_party_posts_bill_allocations()
    {
        var k = NewKit("Accounting Invoice BillWise Co");
        k.Vm.OpenVoucher(VoucherBaseType.Sales);
        var entry = k.Vm.VoucherEntry!;
        entry.Mode = VoucherEntryMode.AccountingInvoice;
        Assert.True(entry.IsAccountingInvoice);

        SelectParty(entry, k.CustomerId);
        var line = entry.AccountingInvoiceLines[0];
        line.SelectedLedger = entry.AccountingInvoiceLedgers.Single(l => l.Id == k.ServiceIncomeId);
        line.AmountText = "8765.43";
        entry.RecalculateAccountingInvoice();

        entry.UseDefaultBillWiseAllocation = false;   // the sub-screen is opt-in; this test names the bill by hand
        Assert.True(entry.ShowInvoiceBillWise);
        Assert.Equal(8765.43m, entry.InvoicePartyTotal);
        entry.InvoiceBillAllocations[0].Name = "SVC-2026-07";
        Assert.True(entry.Accept());

        var c = k.Vm.Company!;
        var type = c.VoucherTypes.Single(t => t.BaseType == VoucherBaseType.Sales && t.IsActive);
        var posted = c.Vouchers.Single(v => v.TypeId == type.Id);
        var partyLine = posted.Lines.Single(l => l.LedgerId == k.CustomerId);
        var alloc = Assert.Single(partyLine.BillAllocations);
        Assert.Equal("SVC-2026-07", alloc.Name);
        Assert.Equal(8765.43m, alloc.Amount.Amount);

        var bill = Assert.Single(Outstandings.OpenBillsFor(c, c.FindLedger(k.CustomerId)!, AsOf(c)));
        Assert.Equal(8765.43m, bill.Pending.Amount);
    }

    // ================================================================ (3) Purchase item invoice → payable

    /// <summary>SG p.79 step 7 — the supplier side. 7 @ ₹987.65 = ₹6,913.55.</summary>
    [Fact]
    public void Purchase_item_invoice_to_a_billwise_supplier_opens_a_payable()
    {
        var k = NewKit("Purchase BillWise Co");
        k.Vm.OpenVoucher(VoucherBaseType.Purchase);
        var entry = k.Vm.VoucherEntry!;
        k.Vm.ToggleItemInvoice();

        SelectParty(entry, k.SupplierId);
        FillItemLine(entry, k, "7", "987.65");
        entry.RecalculateItemInvoice();

        entry.UseDefaultBillWiseAllocation = false;   // the sub-screen is opt-in; this test names the bill by hand
        Assert.True(entry.ShowInvoiceBillWise);
        Assert.Equal(6913.55m, entry.InvoicePartyTotal);
        entry.InvoiceBillAllocations[0].Name = "ACME/4471";
        Assert.True(entry.Accept());

        var c = k.Vm.Company!;
        var bill = Assert.Single(Outstandings.OpenBillsFor(c, c.FindLedger(k.SupplierId)!, AsOf(c)));
        Assert.Equal("ACME/4471", bill.Reference);
        Assert.Equal(6913.55m, bill.Pending.Amount);
    }

    // ================================================================ (4) split across two bills

    /// <summary>SG p.92 — one party amount split across several references. ₹3,703.71 = 1,111.11 + 2,592.60.</summary>
    [Fact]
    public void Invoice_bill_split_across_two_references_must_foot_to_the_party_total()
    {
        var k = NewKit("Split BillWise Co");
        k.Vm.OpenVoucher(VoucherBaseType.Sales);
        var entry = k.Vm.VoucherEntry!;
        k.Vm.ToggleItemInvoice();
        SelectParty(entry, k.CustomerId);
        FillItemLine(entry, k, "3", "1234.57");
        entry.RecalculateItemInvoice();

        entry.InvoiceBillAllocations[0].Name = "PART-A";
        entry.InvoiceBillAllocations[0].AmountText = "1111.11";
        // Under-allocated ⇒ blocked.
        Assert.False(entry.InvoiceBillSplitOk);
        Assert.False(entry.Accept());
        Assert.Contains("bill-wise", entry.Message!, StringComparison.OrdinalIgnoreCase);

        var second = entry.AddInvoiceBillAllocation(BillRefType.NewRef);
        second.Name = "PART-B";
        second.AmountText = "2592.60";
        Assert.True(entry.InvoiceBillSplitOk);
        Assert.True(entry.Accept());

        var c = k.Vm.Company!;
        var bills = Outstandings.OpenBillsFor(c, c.FindLedger(k.CustomerId)!, AsOf(c));
        Assert.Equal(2, bills.Count);
        Assert.Equal(1111.11m, bills.Single(b => b.Reference == "PART-A").Pending.Amount);
        Assert.Equal(2592.60m, bills.Single(b => b.Reference == "PART-B").Pending.Amount);
    }

    // ================================================================ (5) Agst Ref settles the invoice

    /// <summary>
    /// The whole point of tracking: an invoice raised in item-invoice mode must be settleable. Invoice ₹3,703.71,
    /// then a receipt of ₹1,703.71 Agst Ref leaves ₹2,000.00 pending.
    /// </summary>
    [Fact]
    public void An_invoice_mode_bill_can_be_settled_by_a_later_receipt_against_reference()
    {
        var k = NewKit("Settle BillWise Co");
        k.Vm.OpenVoucher(VoucherBaseType.Sales);
        var entry = k.Vm.VoucherEntry!;
        k.Vm.ToggleItemInvoice();
        SelectParty(entry, k.CustomerId);
        FillItemLine(entry, k, "3", "1234.57");
        entry.RecalculateItemInvoice();
        entry.InvoiceBillAllocations[0].Name = "INV-9";
        Assert.True(entry.Accept());

        // A plain-grid Receipt knocking ₹1,703.71 off INV-9.
        k.Vm.OpenVoucher(VoucherBaseType.Receipt);
        var rec = k.Vm.VoucherEntry!;
        var dr = rec.Lines[0];
        dr.SelectedLedger = rec.Ledgers.Single(l => l.Id == k.CashId);
        dr.Side = DrCr.Debit;
        dr.AmountText = "1703.71";
        var cr = rec.Lines[1];
        cr.SelectedLedger = rec.Ledgers.Single(l => l.Id == k.CustomerId);
        cr.Side = DrCr.Credit;
        cr.AmountText = "1703.71";
        var alloc = cr.BillAllocations[0];
        alloc.RefType = BillRefType.AgstRef;
        alloc.Name = "INV-9";
        alloc.AmountText = "1703.71";
        Assert.True(rec.Accept());

        var c = k.Vm.Company!;
        var bill = Assert.Single(Outstandings.OpenBillsFor(c, c.FindLedger(k.CustomerId)!, AsOf(c)));
        Assert.Equal("INV-9", bill.Reference);
        Assert.Equal(2000.00m, bill.Pending.Amount);
    }

    // ================================================================ (6) ER-13 control: non-bill-wise party

    /// <summary>
    /// A party that does NOT maintain balances bill-by-bill (layer 3 says no) must see no panel and post a party
    /// leg with no allocations — byte-identical to before this feature existed.
    /// </summary>
    [Fact]
    public void A_non_billwise_party_shows_no_panel_and_posts_no_allocations()
    {
        var k = NewKit("No BillWise Co");
        k.Vm.OpenVoucher(VoucherBaseType.Sales);
        var entry = k.Vm.VoucherEntry!;
        k.Vm.ToggleItemInvoice();
        SelectParty(entry, k.PlainCustomerId);
        FillItemLine(entry, k, "3", "1234.57");
        entry.RecalculateItemInvoice();

        Assert.False(entry.ShowInvoiceBillWise);
        Assert.Empty(entry.InvoiceBillAllocations);
        Assert.True(entry.Accept());

        var c = k.Vm.Company!;
        var type = c.VoucherTypes.Single(t => t.BaseType == VoucherBaseType.Sales && t.IsActive);
        var posted = c.Vouchers.Single(v => v.TypeId == type.Id);
        Assert.False(posted.Lines.Single(l => l.LedgerId == k.PlainCustomerId).HasBillAllocations);
        Assert.Empty(Outstandings.OpenBillsFor(c, c.FindLedger(k.PlainCustomerId)!, AsOf(c)));
    }

    // ================================================================ (7) On Account needs no name

    /// <summary>
    /// "On Account" parks an amount with no bill reference (SG p.90). It must be accepted with the Name field
    /// blank, and — per <see cref="Outstandings"/> — must NOT open a named bill.
    /// </summary>
    [Fact]
    public void On_account_allocation_is_accepted_without_a_name_and_opens_no_named_bill()
    {
        var k = NewKit("OnAccount BillWise Co");
        k.Vm.OpenVoucher(VoucherBaseType.Sales);
        var entry = k.Vm.VoucherEntry!;
        k.Vm.ToggleItemInvoice();
        SelectParty(entry, k.CustomerId);
        FillItemLine(entry, k, "3", "1234.57");
        entry.RecalculateItemInvoice();

        entry.InvoiceBillAllocations[0].RefType = BillRefType.OnAccount;
        entry.InvoiceBillAllocations[0].Name = string.Empty;
        Assert.True(entry.InvoiceBillSplitOk);
        Assert.True(entry.Accept());

        var c = k.Vm.Company!;
        var type = c.VoucherTypes.Single(t => t.BaseType == VoucherBaseType.Sales && t.IsActive);
        var posted = c.Vouchers.Single(v => v.TypeId == type.Id);
        var alloc = Assert.Single(posted.Lines.Single(l => l.LedgerId == k.CustomerId).BillAllocations);
        Assert.Equal(BillRefType.OnAccount, alloc.RefType);
        Assert.Empty(Outstandings.OpenBillsFor(c, c.FindLedger(k.CustomerId)!, AsOf(c)));
    }

    // ================================================================ (8) auto-fill tracks the running total

    /// <summary>
    /// Regression, caught by this suite while G-1 was being written: dirtiness must be judged on the AMOUNTS, not on
    /// "the operator touched something". Naming the bill FIRST and adding the second item line SECOND used to freeze
    /// the seeded allocation at the old total, so the split silently stopped footing and Accept stayed blocked with
    /// a stale figure. 3 @ ₹1,234.57 = ₹3,703.71, then +1 @ ₹99.99 ⇒ ₹3,803.70.
    /// </summary>
    [Fact]
    public void Naming_the_bill_before_the_last_line_still_lets_the_allocation_track_the_total()
    {
        var k = NewKit("Track Total BillWise Co");
        k.Vm.OpenVoucher(VoucherBaseType.Sales);
        var entry = k.Vm.VoucherEntry!;
        k.Vm.ToggleItemInvoice();
        SelectParty(entry, k.CustomerId);
        FillItemLine(entry, k, "3", "1234.57");
        entry.RecalculateItemInvoice();

        // Name the bill BEFORE the invoice is finished.
        entry.InvoiceBillAllocations[0].Name = "INV-TRACK";
        Assert.Equal(3703.71m, entry.InvoiceBillAllocations[0].ParsedAmount);

        // Now add a second item line — the allocation must follow the new total, not stay at 3,703.71.
        var line2 = entry.AddInventoryLine();
        line2.SelectedItem = entry.StockItems.Single(i => i.Id == k.ItemId);
        line2.SelectedGodown = entry.Godowns.Single(g => g.Id == k.MainGodownId);
        line2.QuantityText = "1";
        line2.RateText = "99.99";
        entry.RecalculateItemInvoice();

        Assert.Equal(3803.70m, entry.InvoicePartyTotal);
        Assert.Equal(3803.70m, entry.InvoiceBillAllocations[0].ParsedAmount);
        Assert.Equal("INV-TRACK", entry.InvoiceBillAllocations[0].Name);
        Assert.True(entry.InvoiceBillSplitOk);
        Assert.True(entry.Accept());

        var c = k.Vm.Company!;
        var bill = Assert.Single(Outstandings.OpenBillsFor(c, c.FindLedger(k.CustomerId)!, AsOf(c)));
        Assert.Equal(3803.70m, bill.Pending.Amount);
    }

    // ================================================================ (9) the exact-sum rule, at PAISA resolution

    /// <summary>
    /// <b>The exact-sum rule is EXACT (spec C-28 / SG p.92) — no tolerance, not even one paisa.</b>
    /// <para>Every other near-miss fixture in this file is off by thousands of rupees, so a
    /// <c>Math.Abs(...) &lt;= 0.50m</c> tolerance substituted for the <c>==</c> at
    /// <c>VoucherEntryViewModel.InvoiceBillSplitOk</c> left all eight of them green. That is precisely the plus/minus
    /// ₹0.50 defect class that survived this project's entire life once already. ₹3,703.71 split
    /// 1,111.11 + 2,592.59 is one paisa short: the screen must say NO, and the domain validator must agree.</para>
    /// <para>The over-allocation half (2,592.61, one paisa long) is asserted too — a one-sided rule would pass the
    /// short case and still let a drifted split through the other way.</para>
    /// </summary>
    [Fact]
    public void A_split_one_paisa_short_or_long_is_refused_by_both_the_screen_and_accept()
    {
        var k = NewKit("Paisa Exact BillWise Co");
        k.Vm.OpenVoucher(VoucherBaseType.Sales);
        var entry = k.Vm.VoucherEntry!;
        k.Vm.ToggleItemInvoice();
        SelectParty(entry, k.CustomerId);
        FillItemLine(entry, k, "3", "1234.57");
        entry.RecalculateItemInvoice();
        Assert.Equal(3703.71m, entry.InvoicePartyTotal);

        entry.InvoiceBillAllocations[0].Name = "PART-A";
        entry.InvoiceBillAllocations[0].AmountText = "1111.11";
        var second = entry.AddInvoiceBillAllocation(BillRefType.NewRef);
        second.Name = "PART-B";

        // ONE PAISA SHORT — 1,111.11 + 2,592.59 = 3,703.70.
        second.AmountText = "2592.59";
        Assert.False(entry.InvoiceBillSplitOk);
        Assert.False(entry.CanAccept);
        Assert.False(entry.Accept());
        Assert.Empty(k.Vm.Company!.Vouchers);

        // ONE PAISA LONG — 1,111.11 + 2,592.61 = 3,703.72.
        second.AmountText = "2592.61";
        Assert.False(entry.InvoiceBillSplitOk);
        Assert.False(entry.CanAccept);
        Assert.False(entry.Accept());
        Assert.Empty(k.Vm.Company!.Vouchers);

        // EXACT — 1,111.11 + 2,592.60 = 3,703.71 ⇒ and only now does it post.
        second.AmountText = "2592.60";
        Assert.True(entry.InvoiceBillSplitOk);
        Assert.True(entry.Accept());
        Assert.Single(k.Vm.Company!.Vouchers);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true); }
        catch (IOException) { }
    }
}
