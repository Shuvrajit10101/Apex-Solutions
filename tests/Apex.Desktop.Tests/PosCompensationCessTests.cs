using System;
using System.Linq;
using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Io;
using Apex.Ledger.Services;
using Apex.Desktop.ViewModels;
using Xunit;
using DomainLedger = Apex.Ledger.Domain.Ledger;

namespace Apex.Desktop.Tests;

/// <summary>
/// 🔴 <b>Census T0-16 — a cess-bearing item sold OVER THE COUNTER collected ZERO Compensation Cess, while the
/// identical item on a Sales item invoice collected it.</b> <c>PosBillingViewModel.ComputeGst</c> built
/// <c>new GstService.TaxableLine(lineValue, res.RateBasisPoints)</c> with no cess argument at all, where
/// <c>VoucherEntryViewModel.ComputeItemInvoiceGst</c> calls <c>GstService.ResolveCess</c> and passes one. The
/// under-collection reached the posted voucher, the bill total, the tenders and the GSTR-1 cess column.
///
/// <para>🔴 <b>EVERY FIGURE IN THIS FILE IS A FIXTURE NONCE.</b> The cess is declared as a PER-ITEM override with
/// an invented per-unit amount; none of these numbers is a statutory rate, threshold or per-unit cess and none is
/// read from any rate table. No R7 statutory claim arises from this fixture and none may be inferred from it —
/// the fix wires the resolver the operator's own cess master feeds, it does not ship a rate.</para>
/// </summary>
public sealed class PosCompensationCessTests
{
    // ---- the nonce set -------------------------------------------------------------------------------------
    private const string CessItemRate = "100.06";
    private const string CessItemQty = "5";
    private const decimal CessPerUnit = 40.05m;          // a NONCE, not a statutory figure
    private const int RateBasisPoints = 500;

    // Derived by hand from the nonces above, never read off the engine.
    //   line value          = 5 × 100.06                       = 500.30
    //   GST @ 5%            = round(500.30 × 500/10000)
    //                       = round(25.015)                    =  25.02   (away from zero, to the paisa)
    //   CGST                = round(25.02 / 2) = round(12.51)  =  12.51
    //   SGST                = 25.02 − 12.51                    =  12.51
    //   Compensation Cess   = round(5 × 40.05) = round(200.25) = 200.25
    //   bill total          = 500.30 + 12.51 + 12.51 + 200.25  = 725.57
    private const decimal LineValue = 500.30m;
    private const decimal Cgst = 12.51m;
    private const decimal Sgst = 12.51m;
    private const decimal Cess = 200.25m;
    private const decimal BillTotal = 725.57m;

    private sealed class Kit
    {
        public required AlterationBook Book { get; init; }
        public required VoucherType PosType { get; init; }
        public required StockItem CessItem { get; init; }
        public required Godown Main { get; init; }
        public required DomainLedger Sales { get; init; }
        public required DomainLedger Cash { get; init; }
    }

    private static Kit Seed(AlterationBook book)
    {
        var c = book.Company;
        book.EnableGst();

        var masters = new InventoryService(c);
        var group = masters.CreateStockGroup("Retail Goods");
        var nos = masters.CreateSimpleUnit("Nos", "Numbers", decimalPlaces: 3);

        var item = masters.CreateStockItem("Counter Cess Good", group.Id, nos.Id);
        item.Gst = new StockItemGstDetails
        {
            Taxability = GstTaxability.Taxable,
            RateBasisPoints = RateBasisPoints,
            CessApplicable = true,
            CessValuationMode = CessValuationMode.Specific,
            CessPerUnit = new Money(CessPerUnit),
        };

        var kit = new Kit
        {
            Book = book,
            PosType = new VoucherType(Guid.NewGuid(), "Sales (POS)", VoucherBaseType.Sales, useForPos: true,
                posConfig: new PosConfig()),
            CessItem = item,
            Main = c.MainLocation!,
            Sales = book.Ledger("Retail Sales", "Sales Accounts"),
            Cash = book.Ledger("Till Cash", "Cash-in-Hand"),
        };
        c.AddVoucherType(kit.PosType);
        book.Storage.Save(c);
        return kit;
    }

    private static PosBillingViewModel NewPos(Kit kit)
    {
        var vm = new PosBillingViewModel(
            kit.Book.Company, kit.PosType, kit.Book.Storage, onSaved: () => { }, onCancelled: () => { });
        vm.Date = kit.Book.On(4);
        vm.SelectedSalesLedger = vm.SalesLedgers.Single(l => l.Id == kit.Sales.Id);
        vm.SelectedGodown = vm.Godowns.Single(g => g.Id == kit.Main.Id);

        var row = vm.Items[0];
        row.SelectedItem = vm.StockItems.Single(i => i.Id == kit.CessItem.Id);
        row.SelectedGodown = vm.Godowns.Single(g => g.Id == kit.Main.Id);
        row.QuantityText = CessItemQty;
        row.RateText = CessItemRate;
        return vm;
    }

    private static decimal HeadOn(Voucher v, GstTaxHead head) =>
        v.Lines.Where(l => l.Gst is { } g && g.TaxHead == head).Sum(l => l.Amount.Amount);

    /// <summary>
    /// 🔴 <b>T0-16.</b> The counter collects the cess. Measured before the fix: the posted bill carried CGST 12.51
    /// and SGST 12.51 and <b>no Cess leg at all</b>, and the cash tender footed to 525.32 instead of 725.57.
    /// </summary>
    [Fact]
    public void A_cess_bearing_item_sold_over_the_counter_collects_the_cess()
    {
        using var book = AlterationBook.New("t0_16_pos_cess");
        var kit = Seed(book);

        var vm = NewPos(kit);
        vm.CashRow.SelectedLedger = kit.Cash;
        vm.CashRow.CashTenderedText = "725.57";

        // The live totals the operator reads BEFORE accepting already carry it — a bill total that excluded the
        // cess would take the correct tender and post an unbalanced voucher, or refuse a correct payment.
        Assert.Equal(BillTotal, decimal.Parse(vm.BillTotalText, System.Globalization.NumberStyles.Any,
            IndianMoneyFormat.Culture));

        Assert.True(vm.Accept(), vm.Message);
        var posted = book.Company.Vouchers.Last(v => v.TypeId == kit.PosType.Id);

        Assert.Equal(LineValue, posted.Lines.Single(l => l.LedgerId == kit.Sales.Id).Amount.Amount);
        Assert.Equal(Cgst, HeadOn(posted, GstTaxHead.Central));
        Assert.Equal(Sgst, HeadOn(posted, GstTaxHead.State));
        Assert.Equal(Cess, HeadOn(posted, GstTaxHead.Cess));

        // The cess is FUNDED: the tender debit foots to the whole bill, cess included.
        Assert.Equal(BillTotal, posted.Lines.Where(l => l.Side == DrCr.Debit).Sum(l => l.Amount.Amount));
        Assert.Equal(BillTotal, posted.PosTenders.Sum(t => t.Amount.Amount));
    }

    /// <summary>
    /// 🔴 <b>The parity statement T0-16 is really about:</b> the SAME item, the SAME quantity and the SAME rate,
    /// sold over the counter and on a Sales item invoice on the SAME day, carry the SAME Compensation Cess. Before
    /// the fix the counter carried 0.00 and the invoice 200.25 — this test is what makes "identical" checkable
    /// rather than asserted.
    /// </summary>
    [Fact]
    public void The_counter_and_the_sales_item_invoice_collect_the_same_cess()
    {
        using var book = AlterationBook.New("t0_16_parity");
        var kit = Seed(book);
        var customer = book.Ledger("Counter Customer", "Sundry Debtors");

        // Stock to sell: one inward purchase of the same item, so the outward sale is backed.
        var purchases = book.Ledger("Purchases", "Purchase Accounts");
        var supplier = book.Ledger("Nonce Suppliers", "Sundry Creditors");
        var purchase = new VoucherEntryViewModel(
            book.Company, book.Type(VoucherBaseType.Purchase), book.Storage,
            onSaved: () => { }, onCancelled: () => { }, book.On())
        { Mode = VoucherEntryMode.ItemInvoice };
        purchase.SelectedParty = purchase.Parties.Single(p => p.Ledger?.Id == supplier.Id);
        purchase.SelectedStockLedger = purchase.StockLedgers.Single(l => l.Id == purchases.Id);
        purchase.InventoryLines[0].SelectedItem = purchase.StockItems.Single(i => i.Id == kit.CessItem.Id);
        purchase.InventoryLines[0].SelectedGodown = purchase.Godowns.Single(g => g.Id == kit.Main.Id);
        purchase.InventoryLines[0].QuantityText = "20";
        purchase.InventoryLines[0].RateText = CessItemRate;
        Assert.True(purchase.Accept(), purchase.Message);

        // The accounting Sales item invoice.
        var salesType = book.Type(VoucherBaseType.Sales);
        var invoice = new VoucherEntryViewModel(
            book.Company, salesType, book.Storage,
            onSaved: () => { }, onCancelled: () => { }, kit.Book.On(4))
        { Mode = VoucherEntryMode.ItemInvoice };
        invoice.SelectedParty = invoice.Parties.Single(p => p.Ledger?.Id == customer.Id);
        invoice.SelectedStockLedger = invoice.StockLedgers.Single(l => l.Id == kit.Sales.Id);
        invoice.InventoryLines[0].SelectedItem = invoice.StockItems.Single(i => i.Id == kit.CessItem.Id);
        invoice.InventoryLines[0].SelectedGodown = invoice.Godowns.Single(g => g.Id == kit.Main.Id);
        invoice.InventoryLines[0].QuantityText = CessItemQty;
        invoice.InventoryLines[0].RateText = CessItemRate;
        Assert.True(invoice.Accept(), invoice.Message);
        var invoiceVoucher = book.Company.Vouchers.Last(v => v.TypeId == salesType.Id);

        // The counter bill.
        var vm = NewPos(kit);
        vm.CashRow.SelectedLedger = kit.Cash;
        vm.CashRow.CashTenderedText = "725.57";
        Assert.True(vm.Accept(), vm.Message);
        var posBill = book.Company.Vouchers.Last(v => v.TypeId == kit.PosType.Id);

        Assert.Equal(Cess, HeadOn(invoiceVoucher, GstTaxHead.Cess));
        Assert.Equal(HeadOn(invoiceVoucher, GstTaxHead.Cess), HeadOn(posBill, GstTaxHead.Cess));
    }

    /// <summary>
    /// 🔴 <b>THE PRINTED DOCUMENT HAS TO SAY WHAT THE CUSTOMER PAID.</b> <c>PosReceiptData.GrandTotal</c> is
    /// <c>TotalTaxable + TotalTax</c>, and <c>InvoiceTax.TotalTax</c> ring-fences the cess out (ER-2) — so the
    /// moment the counter starts COLLECTING a cess, a receipt that carries no cess field prints a grand total
    /// short of the tenders printed beside it on the same slip. (The accounting invoice print already carries
    /// <c>TotalCess</c> for exactly this reason — <c>VoucherPrintProjector</c>'s FIX-1.) This is the printed-vs-
    /// posted class of defect, so it is asserted against the SAME hand-derived figure the bill total uses.
    /// </summary>
    [Fact]
    public void The_counter_receipt_prints_the_cess_and_foots_to_what_was_tendered()
    {
        using var book = AlterationBook.New("t0_16_receipt");
        var kit = Seed(book);
        kit.PosType.PosConfig!.PrintAfterSave = true;

        var vm = NewPos(kit);
        PosReceiptData? receipt = null;
        vm.PrintReceiptRequested += r => receipt = r;
        vm.CashRow.SelectedLedger = kit.Cash;
        vm.CashRow.CashTenderedText = "725.57";
        Assert.True(vm.Accept(), vm.Message);

        Assert.NotNull(receipt);
        Assert.Equal(LineValue, receipt!.TotalTaxable.Amount);
        Assert.Equal(Cgst, receipt.TotalCgst.Amount);
        Assert.Equal(Sgst, receipt.TotalSgst.Amount);
        Assert.Equal(Cess, receipt.TotalCess.Amount);
        Assert.Equal(BillTotal, receipt.GrandTotal.Amount);
        Assert.Equal(BillTotal, receipt.CashTendered.Amount);
        Assert.Equal(0m, receipt.Change.Amount);
        Assert.Equal(BillTotal, receipt.Tenders.Sum(t => t.Amount.Amount));
    }

    // ============================================================ the ALTER mirror the fix turns live

    /// <summary>
    /// Posts the cess bill and returns it — the shared fixture for the two alteration tests below.
    /// </summary>
    private static Voucher PostCessBill(Kit kit, string narration = "Counter nonce ONE")
    {
        var vm = NewPos(kit);
        vm.Narration = narration;
        vm.CashRow.SelectedLedger = kit.Cash;
        vm.CashRow.CashTenderedText = "725.57";
        Assert.True(vm.Accept(), vm.Message);
        return kit.Book.Company.Vouchers.Last(v => v.TypeId == kit.PosType.Id);
    }

    /// <summary>
    /// 🔴 <b>THE MUTATION THIS TEST EXISTS FOR.</b> <c>PosBillingViewModel.ReDerivedTaxOnPostedRows</c> is the
    /// mirror of <c>ComputeGst</c> that <c>CessMagnitudeDriftRefusal</c> compares the STAMPED cess against. Wiring
    /// the cess into <c>ComputeGst</c> alone would leave the mirror deriving ZERO, so every cess-bearing counter
    /// bill would then be REFUSED on a narration-only alteration — a fix that breaks alteration. Measured: with
    /// the mirror's cess argument deleted this test fails and the whole rest of the POS suite still passes, which
    /// is precisely why it is written.
    /// </summary>
    [Fact]
    public void A_narration_only_alteration_of_a_cess_bearing_counter_bill_is_still_accepted()
    {
        using var book = AlterationBook.New("t0_16_alter_ok");
        var kit = Seed(book);
        var posted = PostCessBill(kit);
        Assert.Equal(Cess, HeadOn(posted, GstTaxHead.Cess));

        var open = PosBillingViewModel.ForAlter(
            book.Company, posted.Id, book.Storage, onSaved: () => { }, onCancelled: () => { });
        Assert.False(open.IsRefused, open.Refusal);
        var vm = open.Entry!;
        vm.Narration = "Counter nonce TWO";
        Assert.True(vm.AcceptAlteration(), vm.Message);

        var after = book.Company.FindVoucher(posted.Id)!;
        Assert.Equal(Cess, HeadOn(after, GstTaxHead.Cess));
        Assert.Equal(BillTotal, after.PosTenders.Sum(t => t.Amount.Amount));
    }

    /// <summary>
    /// 🔴 <b>And the drift arm the counter never had before.</b> A per-unit cess master moved after posting leaves
    /// the tax SHAPE byte-identical (a Specific cess stamps the sentinel rate 0 on its leg), so only the magnitude
    /// pin can see it. Before this slice the POS derived no cess at all and this pin could not fire on anything
    /// the screen itself had posted.
    ///
    /// <para><b>The figures, by hand.</b> The bill is stamped with round(5 × 40.05) = 200.25. Moved to ₹90.05/unit
    /// the SAME 5 units re-derive at round(5 × 90.05) = 450.25, so accepting would restate the counter's output
    /// cess by 250.00 on a bill nobody touched — and GSTR-1 reads the stamped figure. Refused by name.</para>
    /// </summary>
    [Fact]
    public void A_moved_cess_master_is_refused_by_name_on_a_counter_bill()
    {
        using var book = AlterationBook.New("t0_16_alter_drift");
        var kit = Seed(book);
        var posted = PostCessBill(kit);
        Assert.Equal(Cess, HeadOn(posted, GstTaxHead.Cess));

        var open = PosBillingViewModel.ForAlter(
            book.Company, posted.Id, book.Storage, onSaved: () => { }, onCancelled: () => { });
        Assert.False(open.IsRefused, open.Refusal);
        var vm = open.Entry!;
        vm.Narration = "Counter nonce TWO";                     // the ONLY thing the operator changes

        kit.CessItem.Gst!.CessPerUnit = new Money(90.05m);      // a NONCE move, not a statutory figure

        Assert.False(vm.AcceptAlteration());
        Assert.Contains("Compensation Cess", vm.Message!, StringComparison.Ordinal);
        Assert.Contains("450.25", vm.Message!, StringComparison.Ordinal);
        Assert.Contains("200.25", vm.Message!, StringComparison.Ordinal);

        // Refused ⇒ the book still holds the cess it was posted with.
        Assert.Equal(Cess, HeadOn(book.Company.FindVoucher(posted.Id)!, GstTaxHead.Cess));
    }

    /// <summary>
    /// 🔴 <b>THE HOLE THE FIX ITSELF OPENED, closed in the same slice.</b> <c>GstService.ResolveCess</c>
    /// deliberately <b>fails fast</b> on an RSP-factor cess whose item declares no Retail Sale Price — refusing to
    /// value a legitimately cess-bearing good at a silent ₹0. Wiring it into the counter therefore put a THROW on
    /// a path that had none: <c>ReDerivedTaxOnPostedRows</c> runs before <c>BuildPosBill</c> (which has its own
    /// catch) and was called bare, so flipping a posted bill's item to RSP-factor and clearing its price would
    /// have taken the whole POS screen down on Ctrl+A instead of refusing.
    ///
    /// <para><b>Reachable through masters alone</b> — an RSP-factor cess with no price cannot be POSTED (the same
    /// fail-fast refuses it at Accept), but it can certainly be created UNDER an already-posted bill, which is
    /// exactly what an alteration re-prices against.</para>
    /// </summary>
    [Fact]
    public void An_unvaluable_cess_master_under_a_posted_bill_is_refused_not_crashed()
    {
        using var book = AlterationBook.New("t0_16_unvaluable");
        var kit = Seed(book);
        var posted = PostCessBill(kit);

        var open = PosBillingViewModel.ForAlter(
            book.Company, posted.Id, book.Storage, onSaved: () => { }, onCancelled: () => { });
        Assert.False(open.IsRefused, open.Refusal);
        var vm = open.Entry!;

        // An RSP-factor cess with NO Retail Sale Price — the fail-fast input.
        kit.CessItem.Gst!.CessValuationMode = CessValuationMode.RetailSalePriceFactor;
        kit.CessItem.Gst.CessRspFactorMillis = 1234;
        kit.CessItem.Gst.RetailSalePrice = null;

        vm.Narration = "Counter nonce TWO";
        Assert.False(vm.AcceptAlteration());          // a refusal, NOT an unhandled throw
        Assert.Contains("Retail Sale Price", vm.Message!, StringComparison.Ordinal);

        Assert.Equal(Cess, HeadOn(book.Company.FindVoucher(posted.Id)!, GstTaxHead.Cess));
    }
}
