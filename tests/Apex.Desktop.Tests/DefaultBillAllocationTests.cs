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
/// <b>Default Bill Allocation</b> — TallyPrime's shipped behaviour, which this codebase had backwards.
///
/// <para><b>The complaint.</b> "While entering sales voucher, the bill by detail is the bill number itself. There is
/// no need to put an extra column for entering new bill reference." The operator is right on both counts.</para>
///
/// <para><b>What TallyPrime does.</b> Official TallyPrime, <i>How to Manage Outstanding Receivables in TallyPrime</i>
/// (Change Bill Allocation): with <b>Use default Bill-wise details for Bill Allocation</b> set to <b>Yes</b> in the
/// F12 configuration options of a sales invoice, "you will not see any difference in the voucher"; on saving, the
/// bill gets linked to the party as the default bill allocation and <b>the voucher number appears as the bill
/// reference</b>. Set it to <b>No</b> and the Bill-wise Details screen appears for manual selection. That Yes is the
/// SHIPPED default is visible in the corpus: <c>719244897-Tally-Book.pdf</c> p.81 has the author explicitly set
/// "F12: Use default bill-wise details for bill allocation — No" precisely IN ORDER to make the sub-screen appear
/// for teaching.</para>
///
/// <para><b>And the Name field is the document number.</b> Same official page: "In <b>Name</b>, the sales voucher
/// number appears." Corpus <c>696054070-TALLY-PRIME-STUDY-GUIDE.pdf</c> p.92 field-by-field: "Name: Supplier Invoice
/// No. will be captured automatically … Due Date, or Credit Days: reflected automatically as per the given credit
/// period specified for the party ledger … Amount: captured automatically as per the Total Invoice Amount."
/// <c>719244897-Tally-Book.pdf</c> p.81 works it: Supplier Invoice No. 311 ⇒ <c>New Ref | Name: 311 | 30 days |
/// 25,000 Cr</c>.</para>
///
/// <para><b>Money fixtures are odd-paisa on purpose.</b> Round figures assert nothing — a ±₹0.50 defect survived
/// this codebase's whole life under six round-number assertions.</para>
/// </summary>
public sealed class DefaultBillAllocationTests : IDisposable
{
    private readonly string _tempDir;
    private readonly CompanyStorage _storage;

    public DefaultBillAllocationTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "ApexDefaultBillAlloc_" + Guid.NewGuid().ToString("N"));
        _storage = new CompanyStorage(_tempDir);
    }

    // ---------------------------------------------------------------- scaffolding

    private sealed class Kit
    {
        public required MainWindowViewModel Vm { get; init; }
        public required string CompanyName { get; init; }
        public required Guid ItemId { get; init; }
        public required Guid MainGodownId { get; init; }
        public required Guid SupplierId { get; init; }
        public required Guid CustomerId { get; init; }
        public required Guid CreditCustomerId { get; init; }
        public required Guid PlainCustomerId { get; init; }
        public required Guid ServiceIncomeId { get; init; }
    }

    /// <summary>
    /// A seeded company with a "Widget" (Nos) item, Purchases/Sales/service-income ledgers, a bill-by-bill supplier
    /// and customer, a bill-by-bill customer carrying a <b>45-day credit period</b>, and a NON-bill-by-bill customer
    /// as the ER-13 control.
    /// </summary>
    private Kit NewKit(string companyName)
    {
        var vm = new MainWindowViewModel(_storage);
        vm.NewCompanyName = companyName;
        vm.CreateCompany();
        var c = vm.Company!;

        var masters = new InventoryService(c);
        var grp = masters.CreateStockGroup("Goods");
        var nos = masters.CreateSimpleUnit("Nos", "Numbers");
        var item = masters.CreateStockItem("Widget", grp.Id, nos.Id);
        masters.AddOpeningBalance(item.Id, c.MainLocation!.Id, 500m, Money.FromRupees(100m));

        AddLedger(c, "Purchases", "Purchase Accounts");
        AddLedger(c, "Sales", "Sales Accounts");
        var serviceIncome = AddLedger(c, "Consultancy Income", "Sales Accounts");
        var supplier = AddLedger(c, "Acme Supplies", "Sundry Creditors", billWise: true);
        var customer = AddLedger(c, "Beta Buyers", "Sundry Debtors", billWise: true);
        var creditCustomer = AddLedger(c, "Delta Credit Buyers", "Sundry Debtors", billWise: true, creditDays: 45);
        var plainCustomer = AddLedger(c, "Gamma Cash Buyers", "Sundry Debtors", billWise: false);

        _storage.Save(c);

        return new Kit
        {
            Vm = vm,
            CompanyName = companyName,
            ItemId = item.Id,
            MainGodownId = c.MainLocation!.Id,
            SupplierId = supplier.Id,
            CustomerId = customer.Id,
            CreditCustomerId = creditCustomer.Id,
            PlainCustomerId = plainCustomer.Id,
            ServiceIncomeId = serviceIncome.Id,
        };
    }

    private static DomainLedger AddLedger(
        Company c, string name, string groupName, bool billWise = false, int? creditDays = null)
    {
        var group = c.FindGroupByName(groupName) ?? throw new InvalidOperationException($"No group '{groupName}'.");
        var ledger = new DomainLedger(Guid.NewGuid(), name, group.Id, Money.Zero, openingIsDebit: false)
        {
            MaintainBillByBill = billWise,
            DefaultCreditPeriodDays = creditDays,
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

    private static VoucherType ActiveType(Company c, VoucherBaseType baseType) =>
        c.VoucherTypes.Single(t => t.BaseType == baseType && t.IsActive);

    // ============================================================ (1) the complaint, verbatim

    /// <summary>
    /// The defect the operator hit: entering a Sales invoice demanded a bill reference in an extra column.
    /// TallyPrime fills it silently from the voucher number. 3 @ ₹1,234.57 = ₹3,703.71 (odd paisa).
    /// </summary>
    [Fact]
    public void A_sales_invoice_needs_no_bill_reference_typing_and_names_the_bill_after_the_voucher_number()
    {
        var k = NewKit("Default Alloc Sales Co");
        k.Vm.OpenVoucher(VoucherBaseType.Sales);
        var entry = k.Vm.VoucherEntry!;
        k.Vm.ToggleItemInvoice();

        // The shipped default is Yes — the sub-screen does NOT appear.
        Assert.True(entry.UseDefaultBillWiseAllocation);

        SelectParty(entry, k.CustomerId);
        FillItemLine(entry, k, "3", "1234.57");
        entry.RecalculateItemInvoice();

        Assert.False(entry.ShowInvoiceBillWise);
        Assert.Equal(3703.71m, entry.InvoicePartyTotal);

        // No typing at all — Accept is already open.
        var number = entry.FormattedVoucherNumber;
        Assert.Equal("1", number);
        Assert.True(entry.CanAccept);
        Assert.True(entry.Accept());

        var c = k.Vm.Company!;
        var posted = c.Vouchers.Single(v => v.TypeId == ActiveType(c, VoucherBaseType.Sales).Id);
        var partyLine = posted.Lines.Single(l => l.LedgerId == k.CustomerId);

        Assert.True(partyLine.HasBillAllocations);
        var alloc = Assert.Single(partyLine.BillAllocations);
        Assert.Equal(BillRefType.NewRef, alloc.RefType);
        Assert.Equal(number, alloc.Name);
        Assert.Equal(3703.71m, alloc.Amount.Amount);

        // The receivable is tracked — with the voucher number as the reference.
        var bill = Assert.Single(Outstandings.OpenBillsFor(c, c.FindLedger(k.CustomerId)!, AsOf(c)));
        Assert.Equal(number, bill.Reference);
        Assert.Equal(3703.71m, bill.Pending.Amount);

        var reloaded = Reload(k.CompanyName);
        var rBill = Assert.Single(Outstandings.OpenBillsFor(reloaded, reloaded.FindLedger(k.CustomerId)!, AsOf(reloaded)));
        Assert.Equal(number, rBill.Reference);
        Assert.Equal(3703.71m, rBill.Pending.Amount);
    }

    // ============================================================ (2) the RENDERED number, affixes and all

    /// <summary>
    /// The auto reference is the voucher's <b>rendered</b> number, so a configured prefix/suffix/width is honoured —
    /// the rendered number IS the document number this app prints everywhere else. ₹8,765.43 (odd paisa).
    /// </summary>
    [Fact]
    public void A_configured_prefix_suffix_and_width_are_honoured_in_the_auto_bill_reference()
    {
        var k = NewKit("Default Alloc Affix Co");
        var c = k.Vm.Company!;
        var salesType = ActiveType(c, VoucherBaseType.Sales);
        salesType.NumberWidth = 4;
        salesType.PrefillWithZero = true;
        salesType.SetAffixes(
            new[] { new VoucherNumberAffix(Guid.NewGuid(), c.BooksBeginFrom, "APX/") },
            new[] { new VoucherNumberAffix(Guid.NewGuid(), c.BooksBeginFrom, "/26-27") });

        k.Vm.OpenVoucher(VoucherBaseType.Sales);
        var entry = k.Vm.VoucherEntry!;
        entry.Mode = VoucherEntryMode.AccountingInvoice;

        SelectParty(entry, k.CustomerId);
        var line = entry.AccountingInvoiceLines[0];
        line.SelectedLedger = entry.AccountingInvoiceLedgers.Single(l => l.Id == k.ServiceIncomeId);
        line.AmountText = "8765.43";
        entry.RecalculateAccountingInvoice();

        Assert.Equal("APX/0001/26-27", entry.FormattedVoucherNumber);
        Assert.True(entry.Accept());

        var posted = c.Vouchers.Single(v => v.TypeId == salesType.Id);
        var alloc = Assert.Single(posted.Lines.Single(l => l.LedgerId == k.CustomerId).BillAllocations);
        Assert.Equal("APX/0001/26-27", alloc.Name);
        Assert.Equal(8765.43m, alloc.Amount.Amount);

        var bill = Assert.Single(Outstandings.OpenBillsFor(c, c.FindLedger(k.CustomerId)!, AsOf(c)));
        Assert.Equal("APX/0001/26-27", bill.Reference);
    }

    // ============================================================ (3) Purchase prefers the Supplier Invoice No.

    /// <summary>
    /// SG p.92 / BOOK p.81: on a Purchase the Name is captured from the <b>Supplier Invoice No.</b> — the
    /// counterparty's document number, not ours. 7 @ ₹987.65 = ₹6,913.55.
    /// </summary>
    [Fact]
    public void A_purchase_names_the_default_allocation_after_the_supplier_invoice_number()
    {
        var k = NewKit("Default Alloc Purchase Co");
        k.Vm.OpenVoucher(VoucherBaseType.Purchase);
        var entry = k.Vm.VoucherEntry!;
        k.Vm.ToggleItemInvoice();

        SelectParty(entry, k.SupplierId);
        entry.ReferenceNo = "311";           // SG's own worked example number
        FillItemLine(entry, k, "7", "987.65");
        entry.RecalculateItemInvoice();

        Assert.False(entry.ShowInvoiceBillWise);
        Assert.Equal(6913.55m, entry.InvoicePartyTotal);
        Assert.True(entry.Accept());

        var c = k.Vm.Company!;
        var bill = Assert.Single(Outstandings.OpenBillsFor(c, c.FindLedger(k.SupplierId)!, AsOf(c)));
        Assert.Equal("311", bill.Reference);
        Assert.Equal(6913.55m, bill.Pending.Amount);
    }

    /// <summary>
    /// The purchase FALLBACK (an inference, marked as such in the code): no Supplier Invoice No. was captured, so the
    /// reference falls back to our own rendered voucher number rather than opening an unnamed, unmatchable bill.
    /// 7 @ ₹987.65 = ₹6,913.55.
    /// </summary>
    [Fact]
    public void A_purchase_without_a_supplier_invoice_number_falls_back_to_our_own_voucher_number()
    {
        var k = NewKit("Default Alloc Purchase Fallback Co");
        k.Vm.OpenVoucher(VoucherBaseType.Purchase);
        var entry = k.Vm.VoucherEntry!;
        k.Vm.ToggleItemInvoice();

        SelectParty(entry, k.SupplierId);
        Assert.Equal(string.Empty, entry.ReferenceNo);
        FillItemLine(entry, k, "7", "987.65");
        entry.RecalculateItemInvoice();

        var number = entry.FormattedVoucherNumber;
        Assert.Equal("1", number);
        Assert.True(entry.Accept());

        var c = k.Vm.Company!;
        var bill = Assert.Single(Outstandings.OpenBillsFor(c, c.FindLedger(k.SupplierId)!, AsOf(c)));
        Assert.Equal(number, bill.Reference);
        Assert.Equal(6913.55m, bill.Pending.Amount);
    }

    // ============================================================ (4) the auto allocation foots EXACTLY

    /// <summary>
    /// <b>The exact-sum gate still holds on the auto path.</b> The auto allocation is derived FROM the party total the
    /// Accept path computed, so Σ allocations must equal the posted party leg to the paisa — asserted against the
    /// POSTED voucher, not a screen figure. 9 @ ₹1,111.11 = ₹9,999.99 (every digit odd-paisa).
    /// </summary>
    [Fact]
    public void The_auto_allocation_foots_to_the_posted_party_leg_to_the_paisa()
    {
        var k = NewKit("Default Alloc Exact Sum Co");
        k.Vm.OpenVoucher(VoucherBaseType.Sales);
        var entry = k.Vm.VoucherEntry!;
        k.Vm.ToggleItemInvoice();

        SelectParty(entry, k.CustomerId);
        FillItemLine(entry, k, "9", "1111.11");
        entry.RecalculateItemInvoice();

        Assert.Equal(9999.99m, entry.InvoicePartyTotal);
        Assert.True(entry.InvoiceBillSplitOk);
        Assert.Equal(entry.InvoicePartyTotal, entry.InvoiceBillAllocatedTotal);
        Assert.True(entry.Accept());

        var c = k.Vm.Company!;
        var posted = c.Vouchers.Single(v => v.TypeId == ActiveType(c, VoucherBaseType.Sales).Id);
        var partyLine = posted.Lines.Single(l => l.LedgerId == k.CustomerId);

        Assert.Equal(9999.99m, partyLine.Amount.Amount);
        Assert.Equal(partyLine.Amount.Amount, partyLine.BillAllocations.Sum(a => a.Amount.Amount));
    }

    // ============================================================ (5) turning it OFF still shows and still validates

    /// <summary>
    /// Set to <b>No</b> and the Bill-wise Details screen appears exactly as before — but PRE-FILLED (SG p.92: name,
    /// due date and amount are all captured automatically), so the operator is correcting a default, never authoring
    /// from scratch. The exact-sum gate is untouched: one paisa short is refused, one paisa long is refused.
    /// ₹3,703.71 = 1,111.11 + 2,592.60.
    /// </summary>
    [Fact]
    public void Turning_default_allocation_off_reveals_a_prefilled_panel_that_still_enforces_the_exact_sum()
    {
        var k = NewKit("Default Alloc Off Co");
        k.Vm.OpenVoucher(VoucherBaseType.Sales);
        var entry = k.Vm.VoucherEntry!;
        k.Vm.ToggleItemInvoice();
        SelectParty(entry, k.CustomerId);
        FillItemLine(entry, k, "3", "1234.57");
        entry.RecalculateItemInvoice();

        entry.UseDefaultBillWiseAllocation = false;

        Assert.True(entry.ShowInvoiceBillWise);
        var row = Assert.Single(entry.InvoiceBillAllocations);
        Assert.Equal(BillRefType.NewRef, row.RefType);
        Assert.Equal(entry.FormattedVoucherNumber, row.Name);   // pre-filled, not blank
        Assert.Equal(3703.71m, row.ParsedAmount);
        Assert.True(entry.InvoiceBillSplitOk);

        // Split it by hand — ONE PAISA SHORT is refused.
        row.Name = "PART-A";
        row.AmountText = "1111.11";
        var second = entry.AddInvoiceBillAllocation(BillRefType.NewRef);
        second.Name = "PART-B";
        second.AmountText = "2592.59";
        Assert.False(entry.InvoiceBillSplitOk);
        Assert.False(entry.Accept());
        Assert.Empty(k.Vm.Company!.Vouchers);

        // ONE PAISA LONG is refused too.
        second.AmountText = "2592.61";
        Assert.False(entry.InvoiceBillSplitOk);
        Assert.False(entry.Accept());
        Assert.Empty(k.Vm.Company!.Vouchers);

        // EXACT posts.
        second.AmountText = "2592.60";
        Assert.True(entry.InvoiceBillSplitOk);
        Assert.True(entry.Accept());

        var c = k.Vm.Company!;
        var bills = Outstandings.OpenBillsFor(c, c.FindLedger(k.CustomerId)!, AsOf(c));
        Assert.Equal(2, bills.Count);
        Assert.Equal(1111.11m, bills.Single(b => b.Reference == "PART-A").Pending.Amount);
        Assert.Equal(2592.60m, bills.Single(b => b.Reference == "PART-B").Pending.Amount);
    }

    // ============================================================ (6) ER-13 control: a non-bill-wise party

    /// <summary>
    /// A party that does not maintain balances bill-by-bill is <b>completely unaffected</b>: no panel, no rows, no
    /// allocations on the posted leg, nothing in Outstandings — byte-identical to before this feature existed.
    /// </summary>
    [Fact]
    public void A_non_billwise_party_is_completely_unaffected_by_default_allocation()
    {
        var k = NewKit("Default Alloc No BillWise Co");
        k.Vm.OpenVoucher(VoucherBaseType.Sales);
        var entry = k.Vm.VoucherEntry!;
        k.Vm.ToggleItemInvoice();
        SelectParty(entry, k.PlainCustomerId);
        FillItemLine(entry, k, "3", "1234.57");
        entry.RecalculateItemInvoice();

        Assert.True(entry.UseDefaultBillWiseAllocation);
        Assert.False(entry.ShowInvoiceBillWise);
        Assert.Empty(entry.InvoiceBillAllocations);
        Assert.True(entry.Accept());

        var c = k.Vm.Company!;
        var posted = c.Vouchers.Single(v => v.TypeId == ActiveType(c, VoucherBaseType.Sales).Id);
        Assert.False(posted.Lines.Single(l => l.LedgerId == k.PlainCustomerId).HasBillAllocations);
        Assert.Empty(Outstandings.OpenBillsFor(c, c.FindLedger(k.PlainCustomerId)!, AsOf(c)));

        // …and the control holds with the panel deliberately switched on, too.
        entry.UseDefaultBillWiseAllocation = false;
        Assert.False(entry.ShowInvoiceBillWise);
    }

    // ============================================================ (7) the due date comes from the credit period

    /// <summary>
    /// SG p.92: "Due Date, or Credit Days: reflected automatically as per the given credit period specified for the
    /// party ledger." Delta Credit Buyers carries 45 days, so the default allocation is due voucher-date + 45.
    /// ₹3,703.71.
    /// </summary>
    [Fact]
    public void The_default_allocation_takes_its_due_date_from_the_party_ledgers_credit_period()
    {
        var k = NewKit("Default Alloc Credit Period Co");
        k.Vm.OpenVoucher(VoucherBaseType.Sales);
        var entry = k.Vm.VoucherEntry!;
        k.Vm.ToggleItemInvoice();
        SelectParty(entry, k.CreditCustomerId);
        FillItemLine(entry, k, "3", "1234.57");
        entry.RecalculateItemInvoice();

        var expectedDue = entry.Date.AddDays(45);

        // Visible on the panel when it is opened (SG's field spec is about what the operator SEES).
        entry.UseDefaultBillWiseAllocation = false;
        Assert.Equal(ApexDate.Format(expectedDue), entry.InvoiceBillAllocations[0].DueDateText);
        entry.UseDefaultBillWiseAllocation = true;

        Assert.True(entry.Accept());

        var c = k.Vm.Company!;
        var posted = c.Vouchers.Single(v => v.TypeId == ActiveType(c, VoucherBaseType.Sales).Id);
        var alloc = Assert.Single(posted.Lines.Single(l => l.LedgerId == k.CreditCustomerId).BillAllocations);
        Assert.Equal(expectedDue, alloc.DueDate);
        Assert.Equal(3703.71m, alloc.Amount.Amount);

        var bill = Assert.Single(Outstandings.OpenBillsFor(c, c.FindLedger(k.CreditCustomerId)!, AsOf(c)));
        Assert.Equal(expectedDue, bill.DueDate);
    }

    // ============================================================ (8) a typed name is never clobbered

    /// <summary>
    /// The auto reference is a DEFAULT, not a lock: once the operator types their own name on the revealed panel, no
    /// later recalculation restamps it — while the AMOUNT keeps tracking the running total (the regression the G-1
    /// suite already pins). 3 @ ₹1,234.57 = ₹3,703.71, then +1 @ ₹99.99 ⇒ ₹3,803.70.
    /// </summary>
    [Fact]
    public void An_operator_typed_reference_survives_later_recalculation()
    {
        var k = NewKit("Default Alloc Typed Name Co");
        k.Vm.OpenVoucher(VoucherBaseType.Sales);
        var entry = k.Vm.VoucherEntry!;
        k.Vm.ToggleItemInvoice();
        SelectParty(entry, k.CustomerId);
        FillItemLine(entry, k, "3", "1234.57");
        entry.RecalculateItemInvoice();

        entry.UseDefaultBillWiseAllocation = false;
        entry.InvoiceBillAllocations[0].Name = "INV-MINE";

        var line2 = entry.AddInventoryLine();
        line2.SelectedItem = entry.StockItems.Single(i => i.Id == k.ItemId);
        line2.SelectedGodown = entry.Godowns.Single(g => g.Id == k.MainGodownId);
        line2.QuantityText = "1";
        line2.RateText = "99.99";
        entry.RecalculateItemInvoice();

        Assert.Equal(3803.70m, entry.InvoicePartyTotal);
        Assert.Equal("INV-MINE", entry.InvoiceBillAllocations[0].Name);
        Assert.Equal(3803.70m, entry.InvoiceBillAllocations[0].ParsedAmount);
        Assert.True(entry.Accept());

        var c = k.Vm.Company!;
        var bill = Assert.Single(Outstandings.OpenBillsFor(c, c.FindLedger(k.CustomerId)!, AsOf(c)));
        Assert.Equal("INV-MINE", bill.Reference);
        Assert.Equal(3803.70m, bill.Pending.Amount);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true); }
        catch (IOException) { }
    }
}
