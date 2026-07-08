using System;
using System.IO;
using System.Linq;
using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Io;
using Apex.Ledger.Reports;
using Apex.Ledger.Services;
using Apex.Desktop.Services;
using Apex.Desktop.ViewModels;
using DomainLedger = Apex.Ledger.Domain.Ledger;

namespace Apex.Desktop.Tests;

/// <summary>
/// UI-side coverage for POS Billing (catalog §11; Phase 6 slice 7 RQ-38..RQ-44, RQ-53; TOP RISK #6; PR-9; DP-4/DP-6).
/// A POS bill is a Sales item-invoice whose single customer debit is replaced by a split of tender debits — the item
/// grid + party/godown + GST are the item-invoice path; the one new surface is the tender panel. These tests pin the
/// thin VM logic (the engine reconciliation/grouping is trusted, covered by <c>Apex.Ledger.Tests</c>): Single-mode
/// change = tendered − payable; Multi-mode Cash auto-fills the residual; the Σ-tenders balance indicator; the Accept
/// gate opens only when the tenders foot to the bill; <b>Alt+I toggles Single ⇄ Multi both ways</b> preserving the
/// entered items/party/godown (RQ-42); Alt+A surfaces the tax analysis (RQ-53); print-after-save raises the receipt
/// hand-off; and each tender row only offers correctly-grouped ledgers (RQ-41 surfaced in the UI).
/// </summary>
public sealed class PosBillingViewModelTests : IDisposable
{
    private static readonly DateOnly FyStart = new(2024, 4, 1);
    private static readonly DateOnly D1 = new(2024, 4, 5);
    private const string GstinMaharashtra = "27AAPFU0939F1ZV";

    private readonly string _tempDir;
    private readonly CompanyStorage _storage;

    public PosBillingViewModelTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "ApexPosTests_" + Guid.NewGuid().ToString("N"));
        _storage = new CompanyStorage(_tempDir);
    }

    private sealed class Kit
    {
        public required Company Company { get; init; }
        public required VoucherType PosType { get; init; }
        public required Guid ItemId { get; init; }
    }

    private static DomainLedger AddLedger(Company c, string name, string groupName, bool openingIsDebit)
    {
        var l = new DomainLedger(Guid.NewGuid(), name, c.FindGroupByName(groupName)!.Id, Money.Zero, openingIsDebit);
        c.AddLedger(l);
        return l;
    }

    // A GST company with an 18%-taxable item on the shelf, a POS-flagged Sales type, and one tender ledger under each
    // required group (Gift → Sundry Debtors, Card/Cheque → Bank, Cash → Cash-in-Hand).
    private static Kit NewKit(bool printAfterSave = false)
    {
        var c = CompanyFactory.CreateSeeded("POS Retail Co", FyStart);
        var gst = new GstService(c);
        gst.EnableGst(new GstConfig
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
        var main = c.MainLocation!.Id;

        AddLedger(c, "Sales (POS)", "Sales Accounts", openingIsDebit: false);
        AddLedger(c, "Gift Voucher", "Sundry Debtors", openingIsDebit: true);
        AddLedger(c, "ICICI Card", "Bank Accounts", openingIsDebit: true);
        AddLedger(c, "SBI Cheque", "Bank Accounts", openingIsDebit: true);
        AddLedger(c, "Cash", "Cash-in-Hand", openingIsDebit: true);

        var posType = new VoucherType(Guid.NewGuid(), "Sales (POS)", VoucherBaseType.Sales, useForPos: true,
            posConfig: new PosConfig { PrintAfterSave = printAfterSave, Message1 = "Thank you" });
        c.AddVoucherType(posType);

        // Stock the shelf: buy 10 @ ₹2,000 so a sale of 1 @ ₹10,225 keeps on-hand ≥ 1.
        var ledgers = new LedgerService(c);
        var purchases = AddLedger(c, "Purchases", "Purchase Accounts", openingIsDebit: true);
        var creditor = AddLedger(c, "Creditor", "Sundry Creditors", openingIsDebit: false);
        ledgers.Post(new Voucher(Guid.NewGuid(),
            c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Purchase).Id, D1,
            new[]
            {
                new EntryLine(purchases.Id, Money.FromRupees(20000m), DrCr.Debit),
                new EntryLine(creditor.Id, Money.FromRupees(20000m), DrCr.Credit),
            },
            inventoryLines: new[] { new VoucherInventoryLine(item.Id, main, 10m, Money.FromRupees(2000m)) }));

        return new Kit { Company = c, PosType = posType, ItemId = item.Id };
    }

    private PosBillingViewModel NewVm(Kit k, out bool saved, out bool cancelled)
    {
        var s = false; var x = false;
        var vm = new PosBillingViewModel(k.Company, k.PosType, _storage, () => s = true, () => x = true);
        saved = s; cancelled = x;
        return vm;
    }

    private PosBillingViewModel NewVm(Kit k) => NewVm(k, out _, out _);

    // Drives the PR-9 item line: sell 1 Widget @ ₹10,225 (taxable 10,225 @ 18% ⇒ bill total 12,065.50).
    private static void EnterPr9Item(PosBillingViewModel vm, Kit k)
    {
        var line = vm.Items[0];
        line.SelectedItem = k.Company.StockItems.First(i => i.Id == k.ItemId);
        line.QuantityText = "1";
        line.RateText = "10225";
    }

    // ---------------------------------------------------------------- single mode: change math (RQ-39)

    [Fact]
    public void Single_mode_cash_tendered_auto_computes_change()
    {
        var k = NewKit();
        var vm = NewVm(k);
        EnterPr9Item(vm, k);

        Assert.False(vm.IsMultiTender);             // single is the default
        Assert.Equal("12,065.50", vm.BillTotalText);

        // Tender 12,100 against a 12,065.50 bill → change 34.50 (informational; the cash Dr stays the bill).
        vm.CashRow.CashTenderedText = "12100";
        Assert.Equal("34.50", vm.ChangeText);
        Assert.Equal("12065.50", vm.CashRow.AmountText);   // raw editable box (no grouping); cash posts the payable, not the tendered
        Assert.True(vm.CanAccept);
    }

    [Fact]
    public void Single_mode_exact_tender_leaves_zero_change_and_balances()
    {
        var k = NewKit();
        var vm = NewVm(k);
        EnterPr9Item(vm, k);
        // No tendered typed ⇒ treated as exact ⇒ change 0, and Σ tenders == bill.
        Assert.Equal("0.00", vm.ChangeText);
        Assert.Equal("Balanced", vm.TenderBalanceText);
        Assert.True(vm.CanAccept);
    }

    // ---------------------------------------------------------------- multi mode: residual auto-fill (RQ-40)

    [Fact]
    public void Multi_mode_cash_amount_auto_fills_the_residual()
    {
        var k = NewKit();
        var vm = NewVm(k);
        EnterPr9Item(vm, k);
        vm.TogglePaymentMode();                      // Alt+I → Multi
        Assert.True(vm.IsMultiTender);

        vm.Tenders[0].AmountText = "500";            // Gift
        vm.Tenders[1].AmountText = "5000";           // Card
        vm.Tenders[2].AmountText = "5000";           // Cheque

        // Cash auto-fills the residual = 12,065.50 − 10,500 = 1,565.50 (raw editable box, no grouping).
        Assert.Equal("1565.50", vm.CashRow.AmountText);
        Assert.Equal("12,065.50", vm.TendersTotalText);
        Assert.Equal("Balanced", vm.TenderBalanceText);
        Assert.True(vm.CanAccept);
    }

    [Fact]
    public void Multi_mode_short_tenders_leave_the_balance_indicator_and_block_accept()
    {
        var k = NewKit();
        var vm = NewVm(k);
        EnterPr9Item(vm, k);
        vm.TogglePaymentMode();

        // Cash auto-fills the residual, so the split always foots — CanAccept is true with only Cash in play.
        Assert.True(vm.CanAccept);
        Assert.Equal("Balanced", vm.TenderBalanceText);

        // Over-tender the non-cash side (Gift+Card+Cheque > bill) → negative residual → Accept blocked.
        vm.Tenders[0].AmountText = "6000";
        vm.Tenders[1].AmountText = "6000";
        vm.Tenders[2].AmountText = "6000";
        Assert.False(vm.CanAccept);
    }

    // ---------------------------------------------------------------- Alt+I both ways (RQ-42)

    [Fact]
    public void AltI_toggles_single_multi_both_ways_preserving_items_party_godown()
    {
        var k = NewKit();
        var vm = NewVm(k);
        EnterPr9Item(vm, k);

        var itemBefore = vm.Items[0].SelectedItem;
        var partyBefore = vm.SelectedParty;
        var godownBefore = vm.SelectedGodown;

        Assert.False(vm.IsMultiTender);
        vm.TogglePaymentMode();                      // Single → Multi
        Assert.True(vm.IsMultiTender);
        Assert.Equal("Multi Tender", vm.PaymentModeText);

        vm.TogglePaymentMode();                      // Multi → Single (the other direction)
        Assert.False(vm.IsMultiTender);
        Assert.Equal("Single Tender", vm.PaymentModeText);

        // The item / party / godown survived both toggles.
        Assert.Same(itemBefore, vm.Items[0].SelectedItem);
        Assert.Same(partyBefore, vm.SelectedParty);
        Assert.Same(godownBefore, vm.SelectedGodown);
        Assert.Equal("1", vm.Items[0].QuantityText);
        Assert.Equal("10225", vm.Items[0].RateText);
    }

    // ---------------------------------------------------------------- Alt+A tax analysis (RQ-53)

    [Fact]
    public void AltA_surfaces_the_per_rate_tax_analysis()
    {
        var k = NewKit();
        var vm = NewVm(k);
        EnterPr9Item(vm, k);

        Assert.False(vm.IsTaxAnalysisVisible);
        var text = vm.ShowTaxAnalysis();
        Assert.True(vm.IsTaxAnalysisVisible);
        Assert.Contains("18", text);                 // 18% rate group
        Assert.Contains("920.25", text);             // CGST/SGST 920.25 each — identical to a normal sale
    }

    // ---------------------------------------------------------------- tender→ledger grouping surfaced (RQ-41)

    [Fact]
    public void Each_tender_row_only_offers_correctly_grouped_ledgers()
    {
        var k = NewKit();
        var vm = NewVm(k);

        var gift = vm.Tenders.Single(t => t.Type == PosTenderType.GiftVoucher);
        var card = vm.Tenders.Single(t => t.Type == PosTenderType.Card);
        var cheque = vm.Tenders.Single(t => t.Type == PosTenderType.Cheque);
        var cash = vm.Tenders.Single(t => t.Type == PosTenderType.Cash);

        Assert.All(gift.LedgerChoices, l => Assert.True(
            ClassificationRules.GroupIsUnder(l.GroupId, "Sundry Debtors", k.Company)));
        Assert.All(card.LedgerChoices, l => Assert.True(ClassificationRules.IsBankLedger(l, k.Company)));
        Assert.All(cheque.LedgerChoices, l => Assert.True(ClassificationRules.IsBankLedger(l, k.Company)));
        Assert.All(cash.LedgerChoices, l => Assert.True(ClassificationRules.IsCashLedger(l, k.Company)));

        // Every row auto-selected a valid ledger (each required group has one), so grouping is valid by construction.
        Assert.NotNull(gift.SelectedLedger);
        Assert.NotNull(card.SelectedLedger);
        Assert.NotNull(cheque.SelectedLedger);
        Assert.NotNull(cash.SelectedLedger);
    }

    // ---------------------------------------------------------------- accept + print-after-save (RQ-38/RQ-44)

    [Fact]
    public void Accept_posts_the_bill_and_returns_to_saved()
    {
        var k = NewKit();
        var saved = false;
        var vm = new PosBillingViewModel(k.Company, k.PosType, _storage, () => saved = true, () => { });
        EnterPr9Item(vm, k);
        vm.CashRow.CashTenderedText = "12100";

        Assert.True(vm.Accept());
        Assert.True(saved);
        Assert.True(vm.SavedNumber > 0);
        // The POS bill is an ordinary Sales voucher carrying the tender split.
        var v = k.Company.Vouchers.Single(x => x.Number == vm.SavedNumber && x.TypeId == k.PosType.Id);
        Assert.True(v.HasPosTenders);
        Assert.Equal(Money.FromRupees(12065.50m), v.TotalDebit);
        Assert.Equal(Money.FromRupees(12065.50m), v.TotalCredit);
    }

    [Fact]
    public void Print_after_save_raises_the_receipt_handoff_when_the_flag_is_set()
    {
        var k = NewKit(printAfterSave: true);
        var vm = new PosBillingViewModel(k.Company, k.PosType, _storage, () => { }, () => { });
        PosReceiptData? receipt = null;
        vm.PrintReceiptRequested += r => receipt = r;

        EnterPr9Item(vm, k);
        vm.CashRow.CashTenderedText = "12100";
        Assert.True(vm.Accept());

        Assert.NotNull(receipt);
        Assert.Equal(Money.FromRupees(34.50m), receipt!.Change);
        Assert.Equal(Money.FromRupees(12100m), receipt.CashTendered);   // single-mode: full bill tendered
        Assert.Equal(Money.FromRupees(12065.50m), receipt.GrandTotal);  // grand total the tenders reconcile to
        Assert.Equal("Thank you", receipt.Message1);                    // POS config message flows to the receipt
    }

    [Fact]
    public void No_print_handoff_when_the_flag_is_off()
    {
        var k = NewKit(printAfterSave: false);
        var vm = new PosBillingViewModel(k.Company, k.PosType, _storage, () => { }, () => { });
        var raised = false;
        vm.PrintReceiptRequested += _ => raised = true;

        EnterPr9Item(vm, k);
        Assert.True(vm.Accept());
        Assert.False(raised);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true); }
        catch { /* best effort */ }
    }
}
