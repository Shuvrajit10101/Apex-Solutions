using System;
using System.IO;
using System.Linq;
using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Services;
using Apex.Desktop.Services;
using Apex.Desktop.ViewModels;
using DomainLedger = Apex.Ledger.Domain.Ledger;

namespace Apex.Desktop.Tests;

/// <summary>
/// W0-13 PART 2, slice S2a — the front-line sub-paisa guards for the VOUCHER-ENTRY family: bill allocations,
/// cost allocations, the plain-grid line amount and the POS tenders.
///
/// <para><b>The defect.</b> Each of these four screens hands a typed <see cref="Money"/> to a domain constructor
/// that validates sign but NOT paisa-exactness, and the value then reaches <c>Paisa.FromMoney</c> on the persist
/// path (<c>SqliteCompanyStore</c> :6698 entry line, :6823-6825 POS tender, :6907 bill allocation, :6930 cost
/// allocation), which throws. Two of the four screens — <c>AcceptItemInvoice</c> / <c>AcceptAccountingInvoice</c>
/// and <c>PosBillingViewModel.Accept</c> — call <c>Post</c> (which APPENDS the voucher to the shared in-memory
/// <see cref="Company"/>) and only THEN <c>Save</c>, with no rollback in the catch. So the refusal leaves the
/// voucher in the aggregate and every LATER save throws: exactly the W0-12 poisoning this campaign exists to
/// remove.</para>
///
/// <para><b>Why the existing validation does not catch it.</b> The only gate on an allocation is an EXACT-SUM
/// check (<c>VoucherLineViewModel.BillSplitOk</c> / <c>CostSplitOk</c>, <c>VoucherEntryViewModel
/// .InvoiceBillSplitOk</c>) — and two sub-paisa halves sum to an exact rupee figure. Every fixture below is
/// built on that shape, so the check that LOOKS like it would refuse the entry passes it through.</para>
///
/// <para><b>Odd-paisa fixtures throughout.</b> A round stem would let a truncation land on the same number.</para>
/// </summary>
public sealed class VoucherEntrySubPaisaFrontLineGuardTests : IDisposable
{
    private static readonly DateOnly FyStart = new(2024, 4, 1);
    private static readonly DateOnly D1 = new(2024, 4, 5);
    private const string GstinMaharashtra = "27AAPFU0939F1ZV";

    private readonly string _tempDir;
    private readonly CompanyStorage _storage;

    public VoucherEntrySubPaisaFrontLineGuardTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "ApexSubPaisaEntry_" + Guid.NewGuid().ToString("N"));
        _storage = new CompanyStorage(_tempDir);
    }

    // ================================================================ shell scaffolding

    private MainWindowViewModel NewSeededCompany(string name)
    {
        var vm = new MainWindowViewModel(_storage);
        vm.NewCompanyName = name;
        vm.CreateCompany();
        Assert.Equal(Screen.Gateway, vm.CurrentScreen);
        return vm;
    }

    private static DomainLedger CreateLedger(MainWindowViewModel vm, string name, string groupName)
    {
        vm.ShowLedgerMaster();
        var master = vm.LedgerMaster!;
        master.Name = name;
        master.SelectedGroup = vm.Company!.FindGroupByName(groupName);
        Assert.True(master.Create());
        return vm.Company!.FindLedgerByName(name)!;
    }

    /// <summary>A bill-by-bill debtor — selecting it on a line opens the Bill-wise sub-panel.</summary>
    private static DomainLedger CreateBillWiseParty(MainWindowViewModel vm, string name)
    {
        vm.ShowLedgerMaster();
        var master = vm.LedgerMaster!;
        master.Name = name;
        master.SelectedGroup = vm.Company!.FindGroupByName("Sundry Debtors");
        Assert.True(master.IsPartyGroup);
        Assert.True(master.MaintainBillByBill);
        Assert.True(master.Create());
        return vm.Company!.FindLedgerByName(name)!;
    }

    private static CostCategory CreateCategory(MainWindowViewModel vm, string name)
    {
        vm.ShowCostCategoryMaster();
        var master = vm.CostCategoryMaster!;
        master.Name = name;
        master.AllocateRevenueItems = true;
        Assert.True(master.Create());
        return vm.Company!.FindCostCategoryByName(name)!;
    }

    private static CostCentre CreateCentre(MainWindowViewModel vm, CostCategory category, string name)
    {
        vm.ShowCostCentreMaster();
        var master = vm.CostCentreMaster!;
        master.SelectedCategory = master.Categories.Single(c => c.Id == category.Id);
        master.Name = name;
        Assert.True(master.Create());
        return vm.Company!.FindCostCentreByName(name)!;
    }

    // ================================================================ the DbException lever (W0-13 S2b)

    /// <summary>
    /// Makes the next <c>Save</c> throw a <c>SqliteException</c> — a PRIMARY KEY violation raised by
    /// <c>InsertCostCentres</c> when the same centre is added twice.
    ///
    /// <para><b>Why this lever and not the sub-paisa one.</b> A <c>DbException</c> is on
    /// <c>SaveFailure.IsReportable</c>'s list but sits OUTSIDE the old narrow
    /// <c>when (ex is InvalidOperationException or ArgumentException)</c> filter these three Accept paths shipped,
    /// so it is <b>the only lever that discriminates the new save guard from the shipped one</b>: on the old code
    /// the exception escaped <c>Accept()</c> unhandled with the voucher already Posted onto the shared
    /// <see cref="Company"/>; on the new code the restore runs first and the failure becomes a message. A
    /// sub-paisa lever cannot tell them apart, because the narrow filter already matched it — which is exactly why
    /// the five sub-paisa tests above, all green, left these three restores unpinned.</para>
    ///
    /// <para>Cost centres are deliberate: <c>InsertCostCentres</c> runs after <c>InsertCompany</c> /
    /// <c>InsertLedgers</c> / <c>InsertVouchers</c>, so the throw genuinely lands on the rollback path, and nothing
    /// on any of these three screens walks the cost-centre list, so the lever cannot perturb the code under
    /// test.</para>
    /// </summary>
    private static void PlantReportableSaveFailure(Company company)
    {
        var category = company.CostCategories[0];
        var centre = new CostCentre(Guid.NewGuid(), "Duplicated Centre", category.Id);
        company.AddCostCentre(centre);
        company.AddCostCentre(centre);   // same primary key twice
    }

    /// <summary>Asserts the message is the planted UNIQUE-constraint violation — only it says "cost_centres" —
    /// and NOT a front-line refusal, which would mean the screen never reached the save the restore guards.</summary>
    private static void AssertReportedTheDbFailure(string? message)
    {
        Assert.NotNull(message);
        Assert.Contains("cost_centres", message);
        Assert.DoesNotContain("finer than a paisa", message);
    }

    // ================================================================ (1) bill allocation — plain Dr/Cr grid
    //
    // 1,234.565 + 3,086.525 == 4,321.09 EXACTLY. The exact-sum gate is satisfied; both halves are sub-paisa.

    [Fact]
    public void ASubPaisaBillSplitThatFootsExactlyIsRefusedAtTheRowRatherThanAtTheStore()
    {
        var vm = NewSeededCompany("SubPaisa BillWise Co");
        var party = CreateBillWiseParty(vm, "Beta Buyers");
        var sales = CreateLedger(vm, "Sales A/c", "Sales Accounts");

        vm.OpenVoucher(VoucherBaseType.Sales);
        var entry = vm.VoucherEntry!;

        var l0 = entry.Lines[0];
        l0.SelectedLedger = party;
        l0.Side = DrCr.Debit;
        l0.AmountText = "4321.09";
        Assert.True(l0.IsBillWise);

        var a0 = l0.BillAllocations[0];
        a0.Name = "INV-9001";
        a0.AmountText = "1234.565";
        vm.AddBillAllocation(l0);
        var a1 = l0.BillAllocations[1];
        a1.Name = "INV-9002";
        a1.AmountText = "3086.525";

        // The gate that LOOKS like it catches this: the two halves foot to the line amount exactly.
        Assert.Equal(4321.09m, l0.BillAllocatedTotal);
        // …and nothing re-formatted what the operator typed into the rows.
        Assert.Equal("1234.565", a0.AmountText);
        Assert.Equal("3086.525", a1.AmountText);

        var l1 = entry.Lines[1];
        l1.SelectedLedger = sales;
        l1.Side = DrCr.Credit;
        l1.AmountText = "4321.09";

        // THE GUARD: the split is invalid because a row amount is finer than a paisa.
        Assert.False(l0.BillSplitOk);
        Assert.False(entry.CanAccept);
        Assert.False(entry.Accept());

        // …and the refusal is a FIELD-level message, not the store's raw persistence exception relayed.
        Assert.Contains("finer than a paisa", entry.Message!);
        Assert.DoesNotContain("cannot persist", entry.Message!);
        Assert.DoesNotContain("Could not save", entry.Message!);

        // Nothing reached the aggregate.
        Assert.Empty(vm.Company!.Vouchers);
    }

    // ================================================================ (2) bill allocation — ITEM INVOICE
    //
    // THE LOUDEST: AcceptItemInvoice Posts (mutating the shared Company) and only then Saves, and its catch has
    // NO rollback. On HEAD the refusal leaves the voucher in the aggregate and poisons every later save.

    [Fact]
    public void ASubPaisaInvoiceBillSplitNeitherPostsNorPoisonsTheAggregate()
    {
        var vm = NewSeededCompany("SubPaisa Item Invoice Co");
        var customer = CreateBillWiseParty(vm, "Beta Buyers");
        CreateLedger(vm, "Sales A/c", "Sales Accounts");
        var c = vm.Company!;

        var masters = new InventoryService(c);
        var grp = masters.CreateStockGroup("Goods");
        var nos = masters.CreateSimpleUnit("Nos", "Numbers");
        var item = masters.CreateStockItem("Widget", grp.Id, nos.Id);
        masters.AddOpeningBalance(item.Id, c.MainLocation!.Id, 500m, Money.FromRupees(100m));
        _storage.Save(c);

        vm.OpenVoucher(VoucherBaseType.Sales);
        var entry = vm.VoucherEntry!;
        vm.ToggleItemInvoice();
        Assert.True(entry.IsItemInvoice);

        entry.SelectedParty = entry.Parties.Single(p => p.Ledger?.Id == customer.Id);
        var line = entry.InventoryLines[0];
        line.SelectedItem = entry.StockItems.Single(i => i.Id == item.Id);
        line.SelectedGodown = entry.Godowns.Single(g => g.Id == c.MainLocation!.Id);
        line.QuantityText = "3";
        line.RateText = "1234.57";
        entry.RecalculateItemInvoice();

        entry.UseDefaultBillWiseAllocation = false;   // reveal the sub-screen (SG p.81 step 6)
        Assert.True(entry.ShowInvoiceBillWise);
        Assert.Equal(3703.71m, entry.InvoicePartyTotal);

        // 1,234.565 + 2,469.145 == 3,703.71 EXACTLY — the party total, to the paisa.
        entry.InvoiceBillAllocations[0].Name = "INV-Beta-001";
        entry.InvoiceBillAllocations[0].AmountText = "1234.565";
        var second = entry.AddInvoiceBillAllocation(BillRefType.NewRef);
        second.Name = "INV-Beta-002";
        second.AmountText = "2469.145";
        Assert.Equal(3703.71m, entry.InvoiceBillAllocatedTotal);

        Assert.False(entry.InvoiceBillSplitOk);
        Assert.False(entry.Accept());
        Assert.Contains("finer than a paisa", entry.Message!);

        // THE POISONING HALF: the voucher must never have entered the shared aggregate…
        Assert.Empty(c.Vouchers);

        // …so an ordinary, unrelated save still works. On HEAD this throws, because the refused invoice is
        // still on the company and its sub-paisa allocation cannot be written.
        _storage.Save(c);
    }

    /// <summary>
    /// <b>THE SAVE-GUARD PROOF for <c>AcceptItemInvoice</c> — the half the sub-paisa test above cannot reach.</b>
    /// Every figure here is paisa-exact and every front-line guard passes, so the invoice Posts (appending it to
    /// the shared <see cref="Company"/>) and the failure lands on <c>_storage.Save</c> itself.
    ///
    /// <para>On the shipped code the catch was
    /// <c>when (ex is InvalidOperationException or ArgumentException)</c>, which does not match a
    /// <c>SqliteException</c>: the exception escaped the Ctrl+A keystroke UNHANDLED and the Posted voucher stayed
    /// on the aggregate the transactionally rolled-back .db never received. Both halves are asserted — nothing
    /// escaped (<c>Record.Exception</c>), and nothing was left behind.</para>
    /// </summary>
    [Fact]
    public void AReportableSaveFailureOfAnItemInvoiceIsAMessageAndLeavesNoVoucherBehind()
    {
        var vm = NewSeededCompany("Item Invoice Db Failure Co");
        var customer = CreateBillWiseParty(vm, "Beta Buyers");
        CreateLedger(vm, "Sales A/c", "Sales Accounts");
        var c = vm.Company!;

        var masters = new InventoryService(c);
        var grp = masters.CreateStockGroup("Goods");
        var nos = masters.CreateSimpleUnit("Nos", "Numbers");
        var item = masters.CreateStockItem("Widget", grp.Id, nos.Id);
        masters.AddOpeningBalance(item.Id, c.MainLocation!.Id, 500m, Money.FromRupees(100m));
        _storage.Save(c);

        vm.OpenVoucher(VoucherBaseType.Sales);
        var entry = vm.VoucherEntry!;
        vm.ToggleItemInvoice();
        Assert.True(entry.IsItemInvoice);

        entry.SelectedParty = entry.Parties.Single(p => p.Ledger?.Id == customer.Id);
        var line = entry.InventoryLines[0];
        line.SelectedItem = entry.StockItems.Single(i => i.Id == item.Id);
        line.SelectedGodown = entry.Godowns.Single(g => g.Id == c.MainLocation!.Id);
        line.QuantityText = "3";
        line.RateText = "1234.57";            // odd paisa, EXACT — party total 3,703.71
        entry.RecalculateItemInvoice();
        Assert.Equal(3703.71m, entry.InvoicePartyTotal);

        PlantReportableSaveFailure(c);

        var accepted = true;
        var escaped = Record.Exception(() => accepted = entry.Accept());
        Assert.Null(escaped);                 // on HEAD the SqliteException came out of the keystroke
        Assert.False(accepted);
        AssertReportedTheDbFailure(entry.Message);

        // THE POISONING HALF: Post had already appended it — the restore must have taken it back off.
        Assert.Empty(c.Vouchers);
    }

    /// <summary>
    /// The same proof for <c>AcceptAccountingInvoice</c> — a ledger-only service sale, no stock. Its catch carried
    /// the identical narrow filter and the identical missing restore, and no test in the repository reached it.
    /// </summary>
    [Fact]
    public void AReportableSaveFailureOfAnAccountingInvoiceIsAMessageAndLeavesNoVoucherBehind()
    {
        var vm = NewSeededCompany("Accounting Invoice Db Failure Co");
        var customer = CreateBillWiseParty(vm, "Beta Buyers");
        var consulting = CreateLedger(vm, "Consulting Fees", "Sales Accounts");
        var c = vm.Company!;
        _storage.Save(c);

        vm.OpenVoucher(VoucherBaseType.Sales);
        var entry = vm.VoucherEntry!;
        entry.ChangeMode();   // As Voucher   -> Item Invoice
        entry.ChangeMode();   // Item Invoice -> Accounting Invoice
        Assert.True(entry.IsAccountingInvoice);

        entry.SelectedParty = entry.Parties.Single(p => p.Ledger?.Id == customer.Id);
        while (entry.AccountingInvoiceLines.Count == 0) entry.AddAccountingInvoiceLine();
        var line = entry.AccountingInvoiceLines[0];
        line.SelectedLedger = entry.AccountingInvoiceLedgers.Single(l => l.Id == consulting.Id);
        line.AmountText = "4321.09";          // odd paisa, EXACT

        PlantReportableSaveFailure(c);

        var accepted = true;
        var escaped = Record.Exception(() => accepted = entry.Accept());
        Assert.Null(escaped);
        Assert.False(accepted);
        AssertReportedTheDbFailure(entry.Message);

        Assert.Empty(c.Vouchers);
    }

    // ================================================================ (3) cost allocation
    //
    // 1,666.785 + 3,333.585 == 5,000.37 EXACTLY, both within ONE category (the per-axis rule C-27).

    [Fact]
    public void ASubPaisaCostSplitThatFootsExactlyIsRefusedAtTheRow()
    {
        var vm = NewSeededCompany("SubPaisa Cost Co");
        var branch = CreateCategory(vm, "Branch");
        var kolkata = CreateCentre(vm, branch, "Kolkata");
        var howrah = CreateCentre(vm, branch, "Howrah");
        var travelling = CreateLedger(vm, "Travelling Expenses", "Indirect Expenses");

        // JOURNAL, not Payment: Payment/Receipt/Contra open in SINGLE-ENTRY mode, where SyncSingleEntrySides
        // auto-stamps Lines[0]'s amount from Σ particulars through a "0.00" format — which ROUNDS a sub-paisa
        // figure away and would make this test pass for the wrong reason. A Journal is double-entry, so every
        // amount below is the operator's own text.
        vm.OpenVoucher(VoucherBaseType.Journal);
        var entry = vm.VoucherEntry!;
        Assert.True(entry.ShowPlainDrCrGrid);

        var l0 = entry.Lines[0];
        l0.SelectedLedger = travelling;
        l0.Side = DrCr.Debit;
        l0.AmountText = "5000.37";
        Assert.True(l0.IsCostApplicable);

        var l1 = entry.Lines[1];
        l1.SelectedLedger = vm.Company!.FindLedgerByName("Cash")!;
        l1.Side = DrCr.Credit;
        l1.AmountText = "5000.37";

        var r0 = l0.CostAllocations[0];
        r0.SelectedCategory = r0.Categories.Single(x => x.Id == branch.Id);
        r0.SelectedCentre = r0.Centres.Single(x => x.Id == kolkata.Id);
        r0.AmountText = "1666.785";

        vm.AddCostAllocation(l0);
        var r1 = l0.CostAllocations[1];
        r1.SelectedCategory = r1.Categories.Single(x => x.Id == branch.Id);
        r1.SelectedCentre = r1.Centres.Single(x => x.Id == howrah.Id);
        r1.AmountText = "3333.585";

        // The per-axis exact-sum gate is satisfied: 1,666.785 + 3,333.585 == 5,000.37.
        Assert.Equal(5000.37m, r0.ParsedAmount + r1.ParsedAmount);

        Assert.False(l0.CostSplitOk);
        Assert.False(entry.CanAccept);
        Assert.False(entry.Accept());
        Assert.Contains("finer than a paisa", entry.Message!);
        Assert.DoesNotContain("Could not save", entry.Message!);
        Assert.Empty(vm.Company!.Vouchers);
    }

    // ================================================================ (4) the plain-grid LINE amount (EntryLine)

    [Fact]
    public void ASubPaisaLineAmountIsRefusedAtTheLineRatherThanAtTheStore()
    {
        var vm = NewSeededCompany("SubPaisa Line Co");
        var travelling = CreateLedger(vm, "Travelling Expenses", "Indirect Expenses");

        // Journal — see the cost test for why not Payment.
        vm.OpenVoucher(VoucherBaseType.Journal);
        var entry = vm.VoucherEntry!;

        var l0 = entry.Lines[0];
        l0.SelectedLedger = travelling;
        l0.Side = DrCr.Debit;
        l0.AmountText = "4321.095";

        var l1 = entry.Lines[1];
        l1.SelectedLedger = vm.Company!.FindLedgerByName("Cash")!;
        l1.Side = DrCr.Credit;
        l1.AmountText = "4321.095";

        // Nothing rewrote what the operator typed — the guard, not a re-format, is what refuses it.
        Assert.Equal("4321.095", l0.AmountText);

        // A sub-paisa line is NOT a complete line — so it can never be built into an EntryLine.
        Assert.False(l0.IsComplete);
        Assert.False(l0.IsBlank);
        Assert.False(entry.CanAccept);
        Assert.False(entry.Accept());
        Assert.Contains("finer than a paisa", entry.Message!);
        Assert.DoesNotContain("Could not save", entry.Message!);
        Assert.Empty(vm.Company!.Vouchers);
    }

    /// <summary>
    /// The magnitude twin: a 17-digit figure parses, is non-negative AND is paisa-exact, then overflows
    /// <c>long</c> inside the store. It must be refused at the line with its own message.
    /// </summary>
    [Fact]
    public void AnUnstorablyLargeLineAmountIsRefusedAtTheLineWithItsOwnMessage()
    {
        var vm = NewSeededCompany("Oversized Line Co");
        var travelling = CreateLedger(vm, "Travelling Expenses", "Indirect Expenses");

        vm.OpenVoucher(VoucherBaseType.Journal);
        var entry = vm.VoucherEntry!;

        var l0 = entry.Lines[0];
        l0.SelectedLedger = travelling;
        l0.Side = DrCr.Debit;
        l0.AmountText = "99999999999999999";

        var l1 = entry.Lines[1];
        l1.SelectedLedger = vm.Company!.FindLedgerByName("Cash")!;
        l1.Side = DrCr.Credit;
        l1.AmountText = "99999999999999999";

        // It IS paisa-exact — the sub-paisa branch alone would let it through.
        Assert.True(new Money(99999999999999999m).IsPaisaExact);

        Assert.False(l0.IsComplete);
        Assert.False(entry.Accept());
        Assert.Contains("too large", entry.Message!);
        Assert.DoesNotContain("finer than a paisa", entry.Message!);
        Assert.Empty(vm.Company!.Vouchers);
    }

    // ================================================================ (5) POS tenders

    private static Company NewPosCompany(out VoucherType posType, out Guid itemId)
    {
        var c = CompanyFactory.CreateSeeded("POS SubPaisa Co", FyStart);
        new GstService(c).EnableGst(new GstConfig
        {
            HomeStateCode = "27",
            Gstin = GstinMaharashtra,
            RegistrationType = GstRegistrationType.Regular,
            ApplicableFrom = FyStart,
            Periodicity = GstReturnPeriodicity.Monthly,
        });

        var masters = new InventoryService(c);
        var grp = masters.CreateStockGroup("Goods");
        var nos = masters.CreateSimpleUnit("Nos", "Numbers");
        var item = masters.CreateStockItem("Widget", grp.Id, nos.Id, valuationMethod: StockValuationMethod.Fifo);
        item.Gst = new StockItemGstDetails { Taxability = GstTaxability.Taxable, RateBasisPoints = 1800 };

        AddPosLedger(c, "Sales (POS)", "Sales Accounts", false);
        AddPosLedger(c, "Gift Voucher", "Sundry Debtors", true);
        AddPosLedger(c, "ICICI Card", "Bank Accounts", true);
        AddPosLedger(c, "SBI Cheque", "Bank Accounts", true);
        AddPosLedger(c, "Cash", "Cash-in-Hand", true);

        posType = new VoucherType(Guid.NewGuid(), "Sales (POS)", VoucherBaseType.Sales, useForPos: true,
            posConfig: new PosConfig { PrintAfterSave = false, Message1 = "Thank you" });
        c.AddVoucherType(posType);

        var purchases = AddPosLedger(c, "Purchases", "Purchase Accounts", true);
        var creditor = AddPosLedger(c, "Creditor", "Sundry Creditors", false);
        new LedgerService(c).Post(new Voucher(Guid.NewGuid(),
            c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Purchase).Id, D1,
            new[]
            {
                new EntryLine(purchases.Id, Money.FromRupees(20000m), DrCr.Debit),
                new EntryLine(creditor.Id, Money.FromRupees(20000m), DrCr.Credit),
            },
            inventoryLines: new[] { new VoucherInventoryLine(item.Id, c.MainLocation!.Id, 10m, Money.FromRupees(2000m)) }));

        itemId = item.Id;
        return c;
    }

    private static DomainLedger AddPosLedger(Company c, string name, string groupName, bool openingIsDebit)
    {
        var l = new DomainLedger(Guid.NewGuid(), name, c.FindGroupByName(groupName)!.Id, Money.Zero, openingIsDebit);
        c.AddLedger(l);
        return l;
    }

    /// <summary>
    /// A sub-paisa CARD tender leaves the cash residual sub-paisa too, and the Σ-tenders reconciliation still
    /// foots to the bill exactly (33.335 + 12,032.165 == 12,065.50) — so the screen's own balance check passes
    /// it. On HEAD the bill Posts, the Save throws, and the catch (:645-647) has no rollback: the POS voucher
    /// stays on the company and every later save throws.
    /// </summary>
    [Fact]
    public void ASubPaisaPosTenderNeitherPostsNorPoisonsTheAggregate()
    {
        var c = NewPosCompany(out var posType, out var itemId);
        var storage = _storage;
        storage.Save(c);

        var vm = new PosBillingViewModel(c, posType, storage, () => { }, () => { });
        var line = vm.Items[0];
        line.SelectedItem = c.StockItems.First(i => i.Id == itemId);
        line.QuantityText = "1";
        line.RateText = "10225";
        Assert.Equal("12,065.50", vm.BillTotalText);

        vm.TogglePaymentMode();
        Assert.True(vm.IsMultiTender);
        vm.Tenders[1].AmountText = "33.335";      // Card

        // The screen's own reconciliation is satisfied: 33.335 + 12,032.165 == 12,065.50.
        Assert.Equal("Balanced", vm.TenderBalanceText);

        Assert.False(vm.CanAccept);
        Assert.False(vm.Accept());
        Assert.Contains("finer than a paisa", vm.Message!);

        Assert.DoesNotContain(c.Vouchers, v => v.TypeId == posType.Id);
        storage.Save(c);      // on HEAD this throws — the refused POS bill is still on the company
    }

    /// <summary>
    /// The cash-tendered twin, and the sharpest crash of the five: a sub-paisa TENDERED figure makes the
    /// informational CHANGE sub-paisa (12,100.005 − 12,065.50 = 34.505), and <c>TryBuildTenders</c> constructs
    /// the <c>PosTender</c> carrying it OUTSIDE the Accept try-block — so the domain refusal would escape the
    /// keystroke entirely. It has to be refused while it is still text.
    /// </summary>
    [Fact]
    public void ASubPaisaCashTenderedIsRefusedBeforeItReachesTheTenderRecord()
    {
        var c = NewPosCompany(out var posType, out var itemId);
        _storage.Save(c);

        var vm = new PosBillingViewModel(c, posType, _storage, () => { }, () => { });
        var line = vm.Items[0];
        line.SelectedItem = c.StockItems.First(i => i.Id == itemId);
        line.QuantityText = "1";
        line.RateText = "10225";
        Assert.Equal("12,065.50", vm.BillTotalText);

        // Single mode — the operator types only the cash handed over.
        Assert.False(vm.IsMultiTender);
        vm.CashRow.CashTenderedText = "12100.005";

        Assert.False(vm.CanAccept);
        Assert.False(vm.Accept());
        Assert.Contains("finer than a paisa", vm.Message!);
        Assert.Contains("cash tendered", vm.Message!);

        Assert.DoesNotContain(c.Vouchers, v => v.TypeId == posType.Id);
        _storage.Save(c);
    }

    /// <summary>
    /// <b>THE SAVE-GUARD PROOF for POS <c>Accept</c>.</b> The bill is entirely valid — paisa-exact rate, single
    /// Cash tender, every front-line guard satisfied — so <c>Post</c> appends it to the shared
    /// <see cref="Company"/> and the planted <c>SqliteException</c> lands on <c>_storage.Save</c>.
    ///
    /// <para>On the shipped code this was the sharpest of the three: the narrow filter did not match a
    /// <c>DbException</c>, so the exception came straight out of a Ctrl+A keystroke with the refused bill still on
    /// the aggregate — and every later save then diverged from the .db. Message-only assertions would prove
    /// nothing here, so the voucher count on the aggregate is asserted too.</para>
    /// </summary>
    [Fact]
    public void AReportableSaveFailureOfAPosBillIsAMessageAndLeavesNoBillBehind()
    {
        var c = NewPosCompany(out var posType, out var itemId);
        _storage.Save(c);
        var vouchersBefore = c.Vouchers.Count;

        var vm = new PosBillingViewModel(c, posType, _storage, () => { }, () => { });
        var line = vm.Items[0];
        line.SelectedItem = c.StockItems.First(i => i.Id == itemId);
        line.QuantityText = "1";
        line.RateText = "10225";
        Assert.Equal("12,065.50", vm.BillTotalText);
        Assert.True(vm.CanAccept);            // the bill itself is entirely valid

        PlantReportableSaveFailure(c);

        var accepted = true;
        var escaped = Record.Exception(() => accepted = vm.Accept());
        Assert.Null(escaped);                 // on HEAD the SqliteException escaped the keystroke
        Assert.False(accepted);
        AssertReportedTheDbFailure(vm.Message);

        Assert.DoesNotContain(c.Vouchers, v => v.TypeId == posType.Id);
        Assert.Equal(vouchersBefore, c.Vouchers.Count);
    }

    /// <summary>
    /// The POS RATE has no ceiling of its own — <c>InventoryVoucherLineViewModel.ParsedRate</c> is a bare
    /// <c>TryParse</c> and <c>RefreshCanAccept</c> tests only <c>r &gt; 0m</c>. A 17-digit rate is PAISA-EXACT, so
    /// it is invisible to every sub-paisa guard, and it reached the store's <c>(long)</c> narrowing cast: an
    /// <c>OverflowException</c>, which is an <c>ArithmeticException</c> that the Accept filter never matched
    /// either — an unhandled crash with the bill already Posted.
    /// </summary>
    [Fact]
    public void AnUnstorablyLargePosRateIsRefusedAtTheLineRatherThanOverflowingTheStore()
    {
        var c = NewPosCompany(out var posType, out var itemId);
        _storage.Save(c);

        var vm = new PosBillingViewModel(c, posType, _storage, () => { }, () => { });
        var line = vm.Items[0];
        line.SelectedItem = c.StockItems.First(i => i.Id == itemId);
        line.QuantityText = "1";
        line.RateText = "99999999999999999.13";        // 17 rupee digits, and paisa-EXACT
        Assert.True(new Money(99999999999999999.13m).IsPaisaExact);

        var accepted = true;
        var escaped = Record.Exception(() => accepted = vm.Accept());
        Assert.Null(escaped);
        Assert.False(accepted);
        Assert.NotNull(vm.Message);
        Assert.Contains("too large", vm.Message);
        Assert.Contains("Widget", vm.Message);                       // the rate's own field label
        Assert.DoesNotContain("finer than a paisa", vm.Message);
        Assert.DoesNotContain("Int64", vm.Message);                  // …not the store's raw overflow text

        Assert.DoesNotContain(c.Vouchers, v => v.TypeId == posType.Id);
        _storage.Save(c);
    }

    /// <summary>
    /// The DERIVED figure the rate guard alone does not cover: the bill total is a PRODUCT, so two individually
    /// storable rates can still foot past the carrier. It becomes the Cash tender amount, so the "storable by
    /// construction" claim on the cash residual rests on this guard existing.
    /// </summary>
    [Fact]
    public void AStorableRateThatFootsToAnUnstorableBillTotalIsRefusedAtTheBillTotal()
    {
        var c = NewPosCompany(out var posType, out var itemId);
        _storage.Save(c);

        var vm = new PosBillingViewModel(c, posType, _storage, () => { }, () => { });
        var line = vm.Items[0];
        line.SelectedItem = c.StockItems.First(i => i.Id == itemId);
        line.QuantityText = "2";
        line.RateText = "50000000000000000.13";        // storable on its own…
        Assert.True(PaisaConversion.FitsPaisaStore(50000000000000000.13m));
        Assert.False(PaisaConversion.FitsPaisaStore(100000000000000000.26m));   // …but not ×2

        var accepted = true;
        var escaped = Record.Exception(() => accepted = vm.Accept());
        Assert.Null(escaped);
        Assert.False(accepted);
        Assert.NotNull(vm.Message);
        Assert.Contains("too large", vm.Message);
        Assert.Contains("the bill total", vm.Message);
        Assert.DoesNotContain("Int64", vm.Message);

        Assert.DoesNotContain(c.Vouchers, v => v.TypeId == posType.Id);
        _storage.Save(c);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
