using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Services;
using Apex.Desktop.Services;
using Apex.Desktop.ViewModels;
using Xunit;
using DomainLedger = Apex.Ledger.Domain.Ledger;

namespace Apex.Desktop.Tests;

/// <summary>
/// 🔴 <b>Phase 10.11 S5e — altering a PURCHASE item invoice and a POS bill.</b>
///
/// <para><b>The finding the slice rests on, pinned here rather than asserted in prose.</b>
/// <c>ShowPriceLevelSelector</c> is <c>IsItemInvoice &amp;&amp; CanBeItemInvoice &amp;&amp; !IsPurchaseInvoice
/// &amp;&amp; EnableMultiplePriceLevels</c>, and it is the SOLE writer of
/// <c>InventoryVoucherLineViewModel.ShowDiscount</c>; <c>ParsedDiscountPercent</c> returns 0 whenever
/// <c>ShowDiscount</c> is false. So on a Purchase item invoice and on a POS bill the posted <c>Rate</c> IS the
/// keyed rate, unconditionally — which is what makes the inverse possible at all. Sales item invoices stay
/// refused by name.</para>
///
/// <para><b>Every fixture figure is a NONCE, not a constant.</b> Each quantity, rate, cost and tender carries its
/// own distinctive paise, so a before/after comparison can actually discriminate: a rehydration that cross-wired
/// two lines, echoed one line's rate onto another, or dropped a field and re-derived it from a neighbour would
/// still be self-consistent under round figures and is caught here.</para>
/// </summary>
public sealed class PurchaseAndPosAlterationTests
{
    // ---------------------------------------------------------------- the purchase fixture

    private sealed class PurchaseKit
    {
        public required AlterationBook Book { get; init; }
        public required StockItem Widget { get; init; }
        public required StockItem Gadget { get; init; }

        /// <summary>The COMPENSATION-CESS specimen — see <see cref="CoalCessPerUnit"/> for why it exists.</summary>
        public required StockItem Coal { get; init; }

        public required Godown Main { get; init; }
        public required Unit Nos { get; init; }
        public required Unit BoxOfNos { get; init; }
        public required DomainLedger Supplier { get; init; }
        public required DomainLedger Purchases { get; init; }
        public required DomainLedger Freight { get; init; }
        public required VoucherType PurchaseType { get; init; }
    }

    // The nonce set. Nothing repeats, nothing is round, and no two fields share a value.
    private const string WidgetRate = "1234.57";
    private const string WidgetActualQty = "3.5";
    private const string WidgetBilledQty = "3.25";
    private const string GadgetRate = "987.61";
    private const string GadgetQty = "4";
    private const decimal GadgetBatchA = 1m;
    private const decimal GadgetBatchB = 3m;
    private const string FreightAmount = "555.53";

    // ---- the COMPENSATION-CESS nonce set (Phase 10.11 review, must-fix 8b) ---------------------------------
    //
    // 🔴 WHY THIS EXISTS AT ALL. The 1,067-line S5e fixture set carried no Compensation Cess anywhere, and that
    // single absence hid TWO wrong-money defects at once: a moved cess master restating the cess on a
    // narration-only alteration (the tax-shape signature cannot see it), and a no-op alteration of a batch-split
    // line drifting the cess by a paisa (the cess was rounded per grid row where the GST heads are rounded on the
    // rate-group subtotal). One specimen covers both, and it has to be shaped deliberately to do so:
    //
    //   · a SPECIFIC (per-unit) cess, because that is the mode whose stamped rate on the posted Cess tax leg is
    //     the constant sentinel 0 — the mode the signature is blind to;
    //   · keyed as ONE grid row and ALLOCATED ACROSS TWO BATCH LOTS, because that is what makes the posted line
    //     set (two rows) differ from the keyed line set (one row) and so exposes the rounding boundary;
    //   · with lots (1.5 / 3.5) that do NOT divide the per-unit tail evenly, because equal or whole lots round
    //     identically either way and would prove nothing.
    //
    // 🔴 EVERY FIGURE BELOW IS A FIXTURE NONCE. None is a statutory rate, threshold or per-unit cess amount, and
    // none is read from any rate table — so no R7 rate claim arises from this fixture and none should be inferred
    // from it. The item name is likewise a nonce; the cess is declared as a PER-ITEM override, which is one of the
    // two routes GstService.ResolveCess takes (the other being a dated HSN cess row) and the cheaper one to move.
    private const string CoalRate = "100.06";
    private const string CoalQty = "5";
    private const decimal CoalBatchA = 1.5m;
    private const decimal CoalBatchB = 3.5m;
    private const decimal CoalCessPerUnit = 40.05m;
    private const decimal CoalCessMovedPerUnit = 90.05m;

    private static PurchaseKit SeedPurchaseKit(AlterationBook book)
    {
        var c = book.Company;
        book.EnableGst();
        c.MaintainBatchwiseDetails = true;

        var masters = new InventoryService(c);
        var group = masters.CreateStockGroup("Goods");
        var nos = masters.CreateSimpleUnit("Nos", "Numbers", decimalPlaces: 3);
        var box = masters.CreateSimpleUnit("Box", "Box");
        var boxOfNos = masters.CreateCompoundUnit("Box-Nos", "Box of 12 Nos", box.Id, nos.Id, 12);

        var widget = masters.CreateStockItem("Widget", group.Id, nos.Id);
        widget.Gst = new StockItemGstDetails { Taxability = GstTaxability.Taxable, RateBasisPoints = 1800 };

        var gadget = masters.CreateStockItem("Gadget", group.Id, nos.Id);
        gadget.Gst = new StockItemGstDetails { Taxability = GstTaxability.Taxable, RateBasisPoints = 1200 };
        gadget.MaintainInBatches = true;

        // The Compensation-Cess specimen. Its GST rate is a THIRD distinct slab so its cess sits in its own rate
        // group, where the group's rounding boundary is the only thing that can move it.
        var coal = masters.CreateStockItem("Coal", group.Id, nos.Id);
        coal.Gst = new StockItemGstDetails
        {
            Taxability = GstTaxability.Taxable,
            RateBasisPoints = 500,
            CessApplicable = true,
            CessValuationMode = CessValuationMode.Specific,
            CessPerUnit = new Money(CoalCessPerUnit),
        };
        coal.MaintainInBatches = true;

        var supplier = book.Ledger("Nonce Suppliers", "Sundry Creditors", billWise: true);
        var purchases = book.Ledger("Purchases", "Purchase Accounts");

        // The additional-cost ledger must exist BEFORE the entry screen is constructed: AdditionalCostLedgers is
        // built once, in the constructor, from Ledger.IsAdditionalCostLedger.
        var freight = new DomainLedger(
            Guid.NewGuid(), "Inward Freight", c.FindGroupByName("Direct Expenses")!.Id, Money.Zero,
            openingIsDebit: true, methodOfAppropriation: MethodOfAppropriation.ByValue);
        c.AddLedger(freight);

        var type = book.Type(VoucherBaseType.Purchase);
        book.Storage.Save(c);

        return new PurchaseKit
        {
            Book = book, Widget = widget, Gadget = gadget, Coal = coal, Main = c.MainLocation!,
            Nos = nos, BoxOfNos = boxOfNos, Supplier = supplier, Purchases = purchases,
            Freight = freight, PurchaseType = type,
        };
    }

    /// <summary>
    /// Posts the full-fat purchase item invoice through the REAL screen: two items, one of them split across two
    /// batches, one of them stated in a compound unit with Actual != Billed, an additional-cost leg, GST at two
    /// different rates, and a bill-wise party. Returns the posted voucher.
    /// </summary>
    private static Voucher PostFatPurchaseInvoice(PurchaseKit kit, string narration = "Nonce narration ONE")
    {
        var entry = NewPurchaseEntry(kit);

        entry.SelectedParty = entry.Parties.Single(p => p.Ledger?.Id == kit.Supplier.Id);
        entry.SelectedStockLedger = entry.StockLedgers.Single(l => l.Id == kit.Purchases.Id);
        entry.Narration = narration;
        entry.ReferenceNo = "SUP/NONCE/41";
        entry.ReferenceDateText = ApexDate.Format(kit.Book.On(3));

        // Line 1 — Widget, in the COMPOUND unit, with Actual != Billed.
        var widgetLine = entry.InventoryLines[0];
        widgetLine.SelectedItem = entry.StockItems.Single(i => i.Id == kit.Widget.Id);
        widgetLine.SelectedGodown = entry.Godowns.Single(g => g.Id == kit.Main.Id);
        Assert.True(widgetLine.ShowUnit);   // the compound unit really does reduce to the item's base unit
        widgetLine.SelectedUnit = widgetLine.UnitOptions.Single(u => u.Id == kit.BoxOfNos.Id);
        widgetLine.QuantityText = WidgetActualQty;
        widgetLine.BilledQuantityText = WidgetBilledQty;
        widgetLine.RateText = WidgetRate;

        // Line 2 — Gadget, allocated across TWO batches (so it posts TWO item lines).
        var gadgetLine = entry.AddInventoryLine();
        gadgetLine.SelectedItem = entry.StockItems.Single(i => i.Id == kit.Gadget.Id);
        gadgetLine.SelectedGodown = entry.Godowns.Single(g => g.Id == kit.Main.Id);
        gadgetLine.QuantityText = GadgetQty;
        gadgetLine.RateText = GadgetRate;
        gadgetLine.SetBatchAllocations(new[]
        {
            new BatchAllocation("BN-77", GadgetBatchA, null, IsNewBatch: true),
            new BatchAllocation("BN-88", GadgetBatchB, null, IsNewBatch: true),
        });
        Assert.True(gadgetLine.HasBatchSplit);

        entry.AdditionalCosts[0].SelectedLedger = kit.Freight;
        entry.AdditionalCosts[0].AmountText = FreightAmount;

        Assert.True(entry.Accept(), entry.Message);
        return kit.Book.Company.Vouchers.Last(v => v.TypeId == kit.PurchaseType.Id);
    }

    /// <summary>
    /// Posts the COMPENSATION-CESS purchase item invoice through the REAL screen: ONE keyed grid row of
    /// <see cref="CoalQty"/> units, allocated across two batch lots, bearing a per-unit cess. It posts TWO
    /// inventory lines out of one keyed row, which is the whole point.
    ///
    /// <para><b>The figures, derived by hand from the fixture constants — never read off the engine.</b>
    /// Line value = round(5 × 100.06) = 500.30, and the two lots foot to it exactly
    /// (round(1.5 × 100.06) = 150.09, round(3.5 × 100.06) = 350.21, Σ = 500.30). GST at 5% on the group subtotal:
    /// round(500.30 × 500/10000) = round(25.015) = 25.02, split CGST = round(25.02/2) = 12.51 and
    /// SGST = 25.02 − 12.51 = 12.51. Cess = round(5 × 40.05) = round(200.25) = 200.25. Party credit =
    /// 500.30 + 12.51 + 12.51 + 200.25 = 725.57.</para>
    /// </summary>
    private static Voucher PostCessPurchaseInvoice(PurchaseKit kit, string narration = "Cess nonce ONE")
    {
        var entry = NewPurchaseEntry(kit);

        entry.SelectedParty = entry.Parties.Single(p => p.Ledger?.Id == kit.Supplier.Id);
        entry.SelectedStockLedger = entry.StockLedgers.Single(l => l.Id == kit.Purchases.Id);
        entry.Narration = narration;

        var row = entry.InventoryLines[0];
        row.SelectedItem = entry.StockItems.Single(i => i.Id == kit.Coal.Id);
        row.SelectedGodown = entry.Godowns.Single(g => g.Id == kit.Main.Id);
        row.QuantityText = CoalQty;
        row.RateText = CoalRate;
        row.SetBatchAllocations(new[]
        {
            new BatchAllocation("BN-91", CoalBatchA, null, IsNewBatch: true),
            new BatchAllocation("BN-92", CoalBatchB, null, IsNewBatch: true),
        });
        Assert.True(row.HasBatchSplit);

        Assert.True(entry.Accept(), entry.Message);
        return kit.Book.Company.Vouchers.Last(v => v.TypeId == kit.PurchaseType.Id);
    }

    /// <summary>Σ of the engine-stamped Compensation-Cess legs on <paramref name="v"/>, to the paisa.</summary>
    private static decimal CessOn(Voucher v) =>
        v.Lines.Where(l => l.Gst is { TaxHead: GstTaxHead.Cess }).Sum(l => l.Amount.Amount);

    private static VoucherEntryViewModel NewPurchaseEntry(PurchaseKit kit)
    {
        var entry = new VoucherEntryViewModel(
            kit.Book.Company, kit.PurchaseType, kit.Book.Storage,
            onSaved: () => { }, onCancelled: () => { }, kit.Book.On());
        entry.Mode = VoucherEntryMode.ItemInvoice;
        entry.TrackAdditionalCosts = true;
        entry.UseSeparateActualBilledQuantity = true;
        return entry;
    }

    // ================================================================ (A) the round trip

    /// <summary>
    /// 🔴 <b>THE HEART OF THE SLICE.</b> The fat purchase item invoice — multi-line, batch-split, additional cost,
    /// Actual != Billed, compound unit, two GST rates, bill-wise party — re-opens and re-accepts with NOTHING
    /// changed, and the canonical export is BYTE-IDENTICAL, in memory and on disk.
    /// </summary>
    [Fact]
    public void A_purchase_item_invoice_re_accepted_unchanged_is_byte_identical()
    {
        using var book = AlterationBook.New("purchasealter");
        var kit = SeedPurchaseKit(book);
        var posted = PostFatPurchaseInvoice(kit);

        // The posted shape really is the hard one: three item rows out of two keyed rows.
        Assert.Equal(3, posted.InventoryLines.Count);

        var before = book.Export();
        var beforeOnDisk = book.ExportReloaded();

        var open = book.ForAlter(posted.Id);
        Assert.False(open.IsRefused, open.Refusal);
        var entry = open.Entry!;
        Assert.True(entry.IsItemInvoice);
        Assert.True(entry.AcceptAlteration(), entry.Message);

        Assert.Equal(before, book.Export());
        Assert.Equal(beforeOnDisk, book.ExportReloaded());
    }

    /// <summary>
    /// The item detail survives FIELD BY FIELD, not merely "lines are present". Each assertion names one field of
    /// one posted row, and every value is a distinct nonce, so a rehydration that carried a neighbour's figure
    /// across would fail here rather than pass a count check.
    /// </summary>
    [Fact]
    public void A_purchase_item_invoice_rehydrates_every_field_of_every_row()
    {
        using var book = AlterationBook.New("purchasefields");
        var kit = SeedPurchaseKit(book);
        var posted = PostFatPurchaseInvoice(kit);

        var open = book.ForAlter(posted.Id);
        Assert.False(open.IsRefused, open.Refusal);
        var entry = open.Entry!;

        // Header.
        Assert.Equal(posted.Number, entry.VoucherNumber);
        Assert.Equal(posted.Date, entry.Date);
        Assert.Equal("Nonce narration ONE", entry.Narration);
        Assert.Equal("SUP/NONCE/41", entry.ReferenceNo);
        Assert.Equal(kit.Supplier.Id, entry.SelectedParty!.Ledger!.Id);
        Assert.Equal(kit.Purchases.Id, entry.SelectedStockLedger!.Id);

        // 🔴 FLAT: one grid row per POSTED line (never a reconstructed split), plus one blank trailing row.
        var rows = entry.InventoryLines.Where(l => !l.IsBlank).ToList();
        Assert.Equal(3, rows.Count);
        Assert.True(entry.InventoryLines[^1].IsBlank);

        // Row 1 — Widget, compound unit, Actual != Billed.
        Assert.Equal(kit.Widget.Id, rows[0].SelectedItem!.Id);
        Assert.Equal(kit.Main.Id, rows[0].SelectedGodown!.Id);
        Assert.Equal(kit.BoxOfNos.Id, rows[0].UnitId);
        Assert.Equal(3.5m, rows[0].ParsedQuantity);
        Assert.Equal(3.25m, rows[0].ParsedBilledQuantity);
        Assert.Equal(1234.57m, rows[0].EffectiveRate!.Value.Amount);
        Assert.Null(rows[0].Batch);

        // Rows 2 and 3 — the two batch lots, each carrying its OWN quantity and its own batch number.
        Assert.Equal(kit.Gadget.Id, rows[1].SelectedItem!.Id);
        Assert.Equal("BN-77", rows[1].Batch);
        Assert.Equal(GadgetBatchA, rows[1].ParsedQuantity);
        Assert.Equal(GadgetBatchA, rows[1].ParsedBilledQuantity);
        Assert.Equal(987.61m, rows[1].EffectiveRate!.Value.Amount);
        Assert.Null(rows[1].UnitId);

        Assert.Equal(kit.Gadget.Id, rows[2].SelectedItem!.Id);
        Assert.Equal("BN-88", rows[2].Batch);
        Assert.Equal(GadgetBatchB, rows[2].ParsedQuantity);
        Assert.Equal(987.61m, rows[2].EffectiveRate!.Value.Amount);

        // …and the rehydrated rows are NOT split objects: a split is never reconstructed.
        Assert.All(rows, r => Assert.False(r.HasBatchSplit));

        // The additional-cost leg, recovered by the same predicate the valuation engine classifies it with.
        var costs = entry.AdditionalCosts.Where(r => !r.IsBlank).ToList();
        var cost = Assert.Single(costs);
        Assert.Equal(kit.Freight.Id, cost.SelectedLedger!.Id);
        Assert.Equal(555.53m, cost.ParsedAmount);

        // The derived party leg's bill-wise split, re-keyed off the posted allocation.
        var postedParty = posted.Lines.Single(l => l.LedgerId == kit.Supplier.Id);
        var postedBill = Assert.Single(postedParty.BillAllocations);
        var billRow = Assert.Single(entry.InvoiceBillAllocations);
        Assert.Equal(postedBill.Name, billRow.Name);
        Assert.Equal(postedBill.Amount.Amount, billRow.ParsedAmount);
        Assert.Equal(postedBill.RefType, billRow.RefType);
    }

    /// <summary>
    /// Altering ONE unrelated field moves ONLY that field. The instrument is the whole canonical export with the
    /// old narration textually swapped for the new one: if any other figure had moved — a rate, a batch quantity,
    /// the apportioned freight, a tax leg, the bill reference — the two documents would still differ.
    /// </summary>
    [Fact]
    public void Altering_one_unrelated_field_on_a_purchase_item_invoice_moves_only_that_field()
    {
        using var book = AlterationBook.New("purchaseonefield");
        var kit = SeedPurchaseKit(book);
        var posted = PostFatPurchaseInvoice(kit);

        var before = System.Text.Encoding.UTF8.GetString(book.Export());

        var open = book.ForAlter(posted.Id);
        Assert.False(open.IsRefused, open.Refusal);
        var entry = open.Entry!;
        entry.Narration = "Nonce narration TWO";
        Assert.True(entry.AcceptAlteration(), entry.Message);

        var after = System.Text.Encoding.UTF8.GetString(book.Export());
        Assert.Contains("Nonce narration ONE", before, StringComparison.Ordinal);
        Assert.Equal(before.Replace("Nonce narration ONE", "Nonce narration TWO", StringComparison.Ordinal), after);
    }

    /// <summary>An amount really does move when the operator moves it — the round-trip tests above would pass on a
    /// screen that ignored every edit, so the opposite direction is pinned too.</summary>
    [Fact]
    public void Altering_an_item_quantity_moves_the_posted_invoice()
    {
        using var book = AlterationBook.New("purchaseqty");
        var kit = SeedPurchaseKit(book);
        var posted = PostFatPurchaseInvoice(kit);
        var supplierBefore = posted.Lines.Single(l => l.LedgerId == kit.Supplier.Id).Amount.Amount;

        var open = book.ForAlter(posted.Id);
        var entry = open.Entry!;
        entry.InventoryLines[0].BilledQuantityText = "3.75";
        Assert.True(entry.AcceptAlteration(), entry.Message);

        var altered = book.Company.FindVoucher(posted.Id)!;
        Assert.NotEqual(supplierBefore, altered.Lines.Single(l => l.LedgerId == kit.Supplier.Id).Amount.Amount);
        Assert.Equal(3, altered.InventoryLines.Count);
        Assert.Equal(3.75m, altered.InventoryLines[0].BilledQuantity);
        Assert.Equal(3.5m, altered.InventoryLines[0].Quantity);      // Actual is untouched
    }

    /// <summary>
    /// 🔴 <b>THE BILL-WISE DIRTINESS RULE, BOTH DIRECTIONS — and it is a MEASURED defect, not a nicety.</b>
    ///
    /// <para>The first cut of the rehydration marked the invoice bill-wise panel dirty unconditionally ("the
    /// posted split is the operator's, whatever produced it"). That FROZE the default single New-Ref row at the
    /// total the invoice had WHEN IT WAS POSTED, so amending any quantity was then refused with "the bill-wise
    /// allocation must total X … currently allocated Y" — a refusal about a split the operator had never touched
    /// and, with the panel hidden by its shipped default, could not even see. Two tests in this file reddened on
    /// it, which is how it was found.</para>
    ///
    /// <para>This test pins the OTHER direction, which the fix must not break: a split the operator really did cut
    /// by hand must survive the round trip untouched, and must NOT be restamped back to one full-total row.</para>
    /// </summary>
    [Fact]
    public void A_hand_cut_bill_wise_split_survives_the_round_trip_untouched()
    {
        using var book = AlterationBook.New("handsplit");
        var kit = SeedPurchaseKit(book);

        var entry = NewPurchaseEntry(kit);
        entry.SelectedParty = entry.Parties.Single(p => p.Ledger?.Id == kit.Supplier.Id);
        entry.SelectedStockLedger = entry.StockLedgers.Single(l => l.Id == kit.Purchases.Id);
        var line = entry.InventoryLines[0];
        line.SelectedItem = entry.StockItems.Single(i => i.Id == kit.Gadget.Id);
        line.SelectedGodown = entry.Godowns.Single(g => g.Id == kit.Main.Id);
        line.QuantityText = "7";
        line.RateText = "531.79";

        entry.UseDefaultBillWiseAllocation = false;   // reveal the panel (SG p.81 step 6)
        Assert.True(entry.ShowInvoiceBillWise);
        var total = entry.InvoicePartyTotal;
        Assert.True(total > 1000m);

        entry.InvoiceBillAllocations[0].Name = "SPLIT-A";
        entry.InvoiceBillAllocations[0].AmountText = "1000.00";
        var second = entry.AddInvoiceBillAllocation(BillRefType.NewRef);
        second.Name = "SPLIT-B";
        second.AmountText = (total - 1000m).ToString("0.00", CultureInfo.InvariantCulture);
        Assert.True(entry.InvoiceBillSplitOk);
        Assert.True(entry.Accept(), entry.Message);

        var posted = book.Company.Vouchers.Last(v => v.TypeId == kit.PurchaseType.Id);
        Assert.Equal(2, posted.Lines.Single(l => l.LedgerId == kit.Supplier.Id).BillAllocations.Count);
        var before = book.Export();

        var open = book.ForAlter(posted.Id);
        Assert.False(open.IsRefused, open.Refusal);
        var altering = open.Entry!;

        // Both rows came back, in order, with their own names and their own nonce amounts…
        Assert.Equal(2, altering.InvoiceBillAllocations.Count);
        Assert.Equal("SPLIT-A", altering.InvoiceBillAllocations[0].Name);
        Assert.Equal(1000m, altering.InvoiceBillAllocations[0].ParsedAmount);
        Assert.Equal("SPLIT-B", altering.InvoiceBillAllocations[1].Name);
        Assert.Equal(total - 1000m, altering.InvoiceBillAllocations[1].ParsedAmount);

        // …and re-accepting does not collapse them back to one auto row.
        Assert.True(altering.AcceptAlteration(), altering.Message);
        Assert.Equal(before, book.Export());
    }

    // ================================================================ (B) what still refuses, and why

    /// <summary>
    /// 🔴 The SALES arm stays refused, BY NAME, and the sentence states the REAL reason: only the effective rate is
    /// posted, so the list rate and the price-level discount cannot be read back.
    /// </summary>
    [Fact]
    public void A_sales_item_invoice_is_still_refused_and_the_sentence_names_the_rate_not_the_batch()
    {
        using var book = AlterationBook.New("salesstillrefused");
        var kit = SeedPurchaseKit(book);
        var sales = book.Type(VoucherBaseType.Sales);
        var customer = book.Ledger("Nonce Buyers", "Sundry Debtors");
        var salesLedger = book.Ledger("Sales", "Sales Accounts");

        var entry = new VoucherEntryViewModel(
            book.Company, sales, book.Storage, onSaved: () => { }, onCancelled: () => { }, book.On());
        entry.Mode = VoucherEntryMode.ItemInvoice;
        entry.SelectedParty = entry.Parties.Single(p => p.Ledger?.Id == customer.Id);
        entry.SelectedStockLedger = entry.StockLedgers.Single(l => l.Id == salesLedger.Id);
        var line = entry.InventoryLines[0];
        line.SelectedItem = entry.StockItems.Single(i => i.Id == kit.Widget.Id);
        line.SelectedGodown = entry.Godowns.Single(g => g.Id == kit.Main.Id);
        line.QuantityText = "2";
        line.RateText = "4321.09";
        Assert.True(entry.Accept(), entry.Message);
        var invoice = book.Company.Vouchers.Last(v => v.TypeId == sales.Id);

        var refusal = VoucherAlterationEligibility.RefusalFor(book.Company, invoice.Id);
        Assert.False(string.IsNullOrWhiteSpace(refusal));
        Assert.Contains("SALES ITEM INVOICE", refusal!, StringComparison.Ordinal);
        Assert.Contains("list rate", refusal!, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("discount", refusal!, StringComparison.OrdinalIgnoreCase);

        // 🔴 AND THE DISSOLVED BLOCKER IS GONE FROM THE SENTENCE. The batch split was never an irrecoverability —
        // TryAppendSplitBatchLines refuses any split that is not value-identical to the N-separate-rows keying —
        // so citing it here would be citing a reason that is not true.
        Assert.DoesNotContain("batch", refusal!, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("arrives in a later slice", refusal!, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>The Sales refusal does not depend on the LIVE price-level flag — reading it would be the
    /// master-drift trap, because switching it off after posting a discounted invoice would make the same voucher
    /// look recoverable while its list rate stayed lost.</summary>
    [Fact]
    public void The_sales_refusal_does_not_read_the_live_price_level_flag()
    {
        using var book = AlterationBook.New("salesflag");
        var kit = SeedPurchaseKit(book);
        var sales = book.Type(VoucherBaseType.Sales);
        var voucher = new Voucher(
            Guid.NewGuid(), sales.Id, book.On(),
            new[]
            {
                new EntryLine(book.Ledger("Flag Dr", "Sundry Debtors").Id, Money.FromRupees(100m), DrCr.Debit),
                new EntryLine(book.Ledger("Flag Cr", "Sales Accounts").Id, Money.FromRupees(100m), DrCr.Credit),
            },
            inventoryLines: new[] { new VoucherInventoryLine(kit.Widget.Id, kit.Main.Id, 2m, Money.FromRupees(50m)) });

        book.Company.EnableMultiplePriceLevels = false;
        var off = VoucherAlterationEligibility.RefusalFor(book.Company, voucher, sales);
        book.Company.EnableMultiplePriceLevels = true;
        var on = VoucherAlterationEligibility.RefusalFor(book.Company, voucher, sales);

        Assert.False(string.IsNullOrWhiteSpace(off));
        Assert.Equal(off, on);
    }

    /// <summary>A Purchase item invoice is NOT refused — the predicate's own answer, so the door and the sentence
    /// can never disagree.</summary>
    [Fact]
    public void A_purchase_item_invoice_is_not_refused()
    {
        using var book = AlterationBook.New("purchaseopens");
        var kit = SeedPurchaseKit(book);
        var posted = PostFatPurchaseInvoice(kit);
        Assert.Null(VoucherAlterationEligibility.RefusalFor(book.Company, posted.Id));
    }

    /// <summary>
    /// 🔴 The Actual/Billed company flag turned OFF after posting is REFUSED BY NAME. Without this the rehydrated
    /// line's Billed column would be hidden, <c>ParsedBilledQuantity</c> would fall back to Actual, and the
    /// re-accept would silently re-bill the invoice at the actual quantity.
    /// </summary>
    [Fact]
    public void Turning_the_actual_billed_columns_off_after_posting_is_refused_by_name()
    {
        using var book = AlterationBook.New("abdrift");
        var kit = SeedPurchaseKit(book);
        var posted = PostFatPurchaseInvoice(kit);

        book.Company.UseSeparateActualBilledQuantity = false;

        var open = book.ForAlter(posted.Id);
        Assert.True(open.IsRefused);
        Assert.Contains("Actual/Billed", open.Refusal!, StringComparison.Ordinal);
    }

    /// <summary>Turning additional-cost tracking off after posting is refused by name — the legs would drop out of
    /// the supplier's total and out of the item landed rates.</summary>
    [Fact]
    public void Turning_additional_cost_tracking_off_after_posting_is_refused_by_name()
    {
        using var book = AlterationBook.New("costdrift");
        var kit = SeedPurchaseKit(book);
        var posted = PostFatPurchaseInvoice(kit);

        kit.PurchaseType.TrackAdditionalCosts = false;

        var open = book.ForAlter(posted.Id);
        Assert.True(open.IsRefused);
        Assert.Contains("Track Additional Costs", open.Refusal!, StringComparison.Ordinal);
    }

    /// <summary>Clearing the additional-cost ledger's Method of Appropriation is refused by name: the valuation
    /// engine classifies the leg by exactly that predicate, so the landed rates would move with no figure on the
    /// screen changing.</summary>
    [Fact]
    public void Clearing_the_cost_ledgers_method_of_appropriation_is_refused_by_name()
    {
        using var book = AlterationBook.New("methoddrift");
        var kit = SeedPurchaseKit(book);
        var posted = PostFatPurchaseInvoice(kit);

        kit.Freight.MethodOfAppropriation = null;

        var open = book.ForAlter(posted.Id);
        Assert.True(open.IsRefused);
        Assert.Contains("Method of Appropriation", open.Refusal!, StringComparison.Ordinal);
    }

    /// <summary>Turning the party's bill-by-bill flag off after posting is refused by name — the posted allocation
    /// would silently vanish from the replacement.</summary>
    [Fact]
    public void Turning_the_partys_bill_wise_flag_off_after_posting_is_refused_by_name()
    {
        using var book = AlterationBook.New("billwisedrift");
        var kit = SeedPurchaseKit(book);
        var posted = PostFatPurchaseInvoice(kit);

        kit.Supplier.MaintainBillByBill = false;

        var open = book.ForAlter(posted.Id);
        Assert.True(open.IsRefused);
        Assert.Contains("bill-by-bill", open.Refusal!, StringComparison.Ordinal);
    }

    /// <summary>
    /// 🔴 A GST RATE MASTER MOVED SINCE POSTING IS REFUSED AT ACCEPT, by name. The alteration re-derives the tax
    /// (it must — GSTR-1 and GSTR-3B read the STAMPED figure, so echoing it would let a return declare something
    /// the book does not hold), and re-deriving under a moved master would silently restate a filed figure.
    /// </summary>
    [Fact]
    public void A_moved_gst_rate_master_is_refused_at_accept_by_name()
    {
        using var book = AlterationBook.New("gstdrift");
        var kit = SeedPurchaseKit(book);
        var posted = PostFatPurchaseInvoice(kit);
        var before = book.Export();

        var open = book.ForAlter(posted.Id);
        Assert.False(open.IsRefused, open.Refusal);
        var entry = open.Entry!;

        kit.Widget.Gst!.RateBasisPoints = 500;   // 18% → 5% after the screen opened

        Assert.False(entry.AcceptAlteration());
        Assert.Contains("not the shape it was posted with", entry.Message!, StringComparison.Ordinal);

        // …and the refused alteration left the book exactly as it found it.
        kit.Widget.Gst!.RateBasisPoints = 1800;
        Assert.Equal(before, book.Export());
    }

    // ================================================================ (B2) COMPENSATION CESS

    /// <summary>
    /// 🔴 <b>A CESS-BEARING BATCH-SPLIT INVOICE, RE-ACCEPTED WITH NOTHING CHANGED, IS BYTE-IDENTICAL.</b>
    ///
    /// <para>Cess used to be rounded ONCE PER GRID ROW while the GST heads are rounded on the rate-GROUP subtotal.
    /// A batch-split line posts N inventory lines out of ONE keyed row and the alteration screen rebuilds the grid
    /// FLAT — one row per POSTED line — so the re-derivation replaced round(Σ line) with Σ round(line) and the
    /// cess, and the supplier's credit with it, moved on an alteration where the operator touched nothing.</para>
    ///
    /// <para><b>The arithmetic, derived from the defect and not from the code.</b> Posted from one row of 5 units:
    /// cess = round(5 × 40.05) = round(200.25) = <b>200.25</b>. Rehydrated flat into lots of 1.5 and 3.5 and
    /// rounded per row: round(1.5 × 40.05) + round(3.5 × 40.05) = round(60.075) + round(140.175) = 60.08 + 140.18 =
    /// <b>200.26</b> — one paisa of Input Cess and one paisa of supplier credit conjured out of a no-op. The GST
    /// heads do NOT move (both sides compute on the same 500.30 group subtotal), which is exactly why nothing
    /// downstream reports it: the voucher still balances.</para>
    /// </summary>
    [Fact]
    public void A_cess_bearing_batch_split_invoice_re_accepted_unchanged_is_byte_identical()
    {
        using var book = AlterationBook.New("cessroundtrip");
        var kit = SeedPurchaseKit(book);
        var posted = PostCessPurchaseInvoice(kit);

        // The posted shape really is the one that exposes the boundary: TWO inventory lines from ONE keyed row.
        Assert.Equal(2, posted.InventoryLines.Count);

        // The posted figures, to the paisa, every one derived by hand above.
        Assert.Equal(500.30m, posted.Lines.Single(l => l.LedgerId == kit.Purchases.Id).Amount.Amount);
        Assert.Equal(200.25m, CessOn(posted));
        Assert.Equal(725.57m, posted.Lines.Single(l => l.LedgerId == kit.Supplier.Id).Amount.Amount);

        var before = book.Export();
        var beforeOnDisk = book.ExportReloaded();

        var open = book.ForAlter(posted.Id);
        Assert.False(open.IsRefused, open.Refusal);
        var entry = open.Entry!;

        // FLAT: one grid row per posted lot, which is the partition the re-derivation now sees.
        Assert.Equal(2, entry.InventoryLines.Count(l => !l.IsBlank));

        Assert.True(entry.AcceptAlteration(), entry.Message);

        var after = book.Company.FindVoucher(posted.Id)!;
        Assert.Equal(200.25m, CessOn(after));                                                     // NOT 200.26
        Assert.Equal(725.57m, after.Lines.Single(l => l.LedgerId == kit.Supplier.Id).Amount.Amount); // NOT 725.58

        Assert.Equal(before, book.Export());
        Assert.Equal(beforeOnDisk, book.ExportReloaded());
    }

    /// <summary>
    /// 🔴 Ctrl+H on an altering item invoice must not become a back door to the plain grid, which does not hold
    /// the stock lines at all — accepting there would replace the invoice with a stock-free pair of rows.
    /// </summary>
    [Fact]
    public void Accepting_a_posted_item_invoice_from_the_plain_grid_is_refused_by_name()
    {
        using var book = AlterationBook.New("modeflip");
        var kit = SeedPurchaseKit(book);
        var posted = PostFatPurchaseInvoice(kit);
        var before = book.Export();

        var entry = book.ForAlter(posted.Id).Entry!;
        entry.Mode = VoucherEntryMode.AsVoucher;

        Assert.False(entry.AcceptAlteration());
        Assert.Contains("ITEM INVOICE", entry.Message!, StringComparison.Ordinal);
        Assert.Equal(before, book.Export());
    }

    /// <summary>
    /// 🔴 A line stated in a COMPOUND unit that the item no longer offers is refused by name. Falling back to the
    /// base unit silently would restate "3.5 Box @ 1,234.57" as "3.5 Nos @ 1,234.57" — the value leg would still
    /// foot, so nothing downstream would catch it; only the stock quantity would move, by the conversion factor.
    /// </summary>
    [Fact]
    public void A_line_unit_the_item_no_longer_offers_is_refused_by_name()
    {
        using var book = AlterationBook.New("unitdrift");
        var kit = SeedPurchaseKit(book);
        var posted = PostFatPurchaseInvoice(kit);
        Assert.Equal(kit.BoxOfNos.Id, posted.InventoryLines[0].UnitId);

        // Repoint the item's base unit: "Box-Nos" no longer reduces to it, so the picker stops offering it.
        kit.Widget.BaseUnitId = book.Company.Units.Single(u => u.Symbol == "Box").Id;

        var open = book.ForAlter(posted.Id);
        Assert.True(open.IsRefused);
        Assert.Contains("a unit this item no longer offers", open.Refusal!, StringComparison.Ordinal);
    }

    /// <summary>
    /// 🔴 A posted leg that is NONE of the four an item invoice builds is refused by name. Silently ignoring it
    /// would drop it from the replacement AND the voucher would still balance — the party leg is derived from the
    /// item rows, so it re-foots without the missing leg and nothing downstream would report the loss.
    /// </summary>
    [Fact]
    public void An_item_invoice_carrying_a_leg_the_screen_does_not_build_is_refused_by_name()
    {
        using var book = AlterationBook.New("strayleg");
        var kit = SeedPurchaseKit(book);
        var other = book.Ledger("Retention Payable", "Sundry Creditors");
        var voucher = new Voucher(
            Guid.NewGuid(), kit.PurchaseType.Id, book.On(),
            new[]
            {
                new EntryLine(kit.Purchases.Id, new Money(100m), DrCr.Debit),
                new EntryLine(kit.Supplier.Id, new Money(60m), DrCr.Credit),
                new EntryLine(other.Id, new Money(40m), DrCr.Credit),
            },
            partyId: kit.Supplier.Id,
            inventoryLines: new[] { new VoucherInventoryLine(kit.Widget.Id, kit.Main.Id, 2m, new Money(50m)) });
        new LedgerService(book.Company).Post(voucher);

        var open = book.ForAlter(voucher.Id);
        Assert.True(open.IsRefused);
        Assert.Contains("Retention Payable", open.Refusal!, StringComparison.Ordinal);
        Assert.Contains("none of the four", open.Refusal!, StringComparison.Ordinal);
    }

    // ---------------------------------------------------------------- the item grid's own derived-leg arms
    //
    // Each of these shapes is UNREACHABLE from the screen — the item grid runs no withholding, collects no TCS and
    // stamps no reverse charge — so each can only arrive from an import. They are asserted on the PREDICATE, over a
    // hand-built voucher, for the same reason An_item_invoice_is_refused_by_name is: the refusal is about what the
    // screen cannot re-derive, not about what a posting path can produce. Without these the four arms would be
    // guards no test can fail, which is dead code wearing the costume of safety.

    private static Voucher HandBuiltPurchaseItemInvoice(
        AlterationBook book, PurchaseKit kit, Func<Guid, Guid, EntryLine[]> legs)
    {
        var dr = book.Ledger("HB Dr", "Purchase Accounts");
        var cr = book.Ledger("HB Cr", "Sundry Creditors");
        return new Voucher(
            Guid.NewGuid(), kit.PurchaseType.Id, book.On(), legs(dr.Id, cr.Id),
            inventoryLines: new[] { new VoucherInventoryLine(kit.Widget.Id, kit.Main.Id, 2m, new Money(50m)) });
    }

    [Fact]
    public void An_item_invoice_carrying_a_withholding_is_refused_by_name()
    {
        using var book = AlterationBook.New("itemtds");
        var kit = SeedPurchaseKit(book);
        var voucher = HandBuiltPurchaseItemInvoice(book, kit, (dr, cr) => new[]
        {
            new EntryLine(dr, new Money(100m), DrCr.Debit),
            new EntryLine(cr, new Money(100m), DrCr.Credit,
                tds: new TdsLineTax(Guid.NewGuid(), "194J(b)", new Money(100m), 1000, new Money(10m), cr, true)),
        });

        var refusal = VoucherAlterationEligibility.RefusalFor(book.Company, voucher, kit.PurchaseType);
        Assert.False(string.IsNullOrWhiteSpace(refusal));
        Assert.Contains("194J(b)", refusal!, StringComparison.Ordinal);
        Assert.Contains("computes no withholding", refusal!, StringComparison.Ordinal);
    }

    [Fact]
    public void An_item_invoice_carrying_a_TCS_collection_is_refused_by_name()
    {
        using var book = AlterationBook.New("itemtcs");
        var kit = SeedPurchaseKit(book);
        var voucher = HandBuiltPurchaseItemInvoice(book, kit, (dr, cr) => new[]
        {
            new EntryLine(dr, new Money(100m), DrCr.Debit,
                tcs: new TcsLineTax(Guid.NewGuid(), "206C(1H)", new Money(100m), 10, new Money(1m), dr, true)),
            new EntryLine(cr, new Money(100m), DrCr.Credit),
        });

        var refusal = VoucherAlterationEligibility.RefusalFor(book.Company, voucher, kit.PurchaseType);
        Assert.False(string.IsNullOrWhiteSpace(refusal));
        Assert.Contains("206C(1H)", refusal!, StringComparison.Ordinal);
        Assert.Contains("Form 27EQ", refusal!, StringComparison.Ordinal);
        Assert.Contains("runs the collection engine", refusal!, StringComparison.Ordinal);
    }

    [Fact]
    public void An_item_invoice_carrying_a_reverse_charge_pair_is_refused_by_name()
    {
        using var book = AlterationBook.New("itemrcm");
        var kit = SeedPurchaseKit(book);
        var voucher = HandBuiltPurchaseItemInvoice(book, kit, (dr, cr) => new[]
        {
            new EntryLine(dr, new Money(100m), DrCr.Debit),
            new EntryLine(cr, new Money(100m), DrCr.Credit,
                gst: new GstLineTax(GstTaxHead.Integrated, 1800, new Money(100m), isReverseCharge: true)),
        });

        var refusal = VoucherAlterationEligibility.RefusalFor(book.Company, voucher, kit.PurchaseType);
        Assert.False(string.IsNullOrWhiteSpace(refusal));
        Assert.Contains("reverse-charge", refusal!, StringComparison.Ordinal);
    }

    /// <summary>
    /// 🔴 And the routing that makes the four arms above the RIGHT question for this family: an ORDINARY GST stamp
    /// on an item invoice must NOT be refused. <c>VoucherAlterationDerivedLegs.Invert</c> refuses exactly that
    /// shape — correctly, for the Dr/Cr grid, which has no engine that writes one — so routing an item invoice
    /// through it would refuse every GST-bearing purchase invoice in existence.
    /// </summary>
    [Fact]
    public void An_ordinary_gst_stamp_on_an_item_invoice_is_not_refused_although_the_plain_grid_refuses_it()
    {
        using var book = AlterationBook.New("itemgstok");
        var kit = SeedPurchaseKit(book);
        var posted = PostFatPurchaseInvoice(kit);

        // The invoice really does carry engine-stamped GST…
        Assert.Contains(posted.Lines, l => l.HasGst);
        // …the plain-grid inverse really does refuse that shape…
        Assert.NotNull(VoucherAlterationDerivedLegs.Invert(book.Company, posted, out _));
        // …and the item grid opens it anyway, because ComputeItemInvoiceGst re-derives it.
        Assert.Null(VoucherAlterationEligibility.RefusalFor(book.Company, posted.Id));
    }

    /// <summary>An item invoice with no party recorded cannot derive its party leg, and is refused by name rather
    /// than opened over a party field the screen would then fill with "(none)".</summary>
    [Fact]
    public void An_item_invoice_with_no_party_is_refused_by_name()
    {
        using var book = AlterationBook.New("itemnoparty");
        var kit = SeedPurchaseKit(book);
        var dr = book.Ledger("NP Dr", "Purchase Accounts");
        var cr = book.Ledger("NP Cr", "Sundry Creditors");
        var voucher = new Voucher(
            Guid.NewGuid(), kit.PurchaseType.Id, book.On(),
            new[]
            {
                new EntryLine(dr.Id, new Money(100m), DrCr.Debit),
                new EntryLine(cr.Id, new Money(100m), DrCr.Credit),
            },
            inventoryLines: new[] { new VoucherInventoryLine(kit.Widget.Id, kit.Main.Id, 2m, new Money(50m)) });
        new LedgerService(book.Company).Post(voucher);

        var open = book.ForAlter(voucher.Id);
        Assert.True(open.IsRefused);
        Assert.Contains("no party ledger", open.Refusal!, StringComparison.Ordinal);
    }

    // ================================================================ (C) POS

    private sealed class PosKit
    {
        public required AlterationBook Book { get; init; }
        public required VoucherType PosType { get; init; }
        public required StockItem Widget { get; init; }
        public required Godown Main { get; init; }
        public required DomainLedger Sales { get; init; }
        public required DomainLedger Gift { get; init; }
        public required DomainLedger Card { get; init; }
        public required DomainLedger Cheque { get; init; }
        public required DomainLedger Cash { get; init; }
        public required DomainLedger Customer { get; init; }
    }

    private static PosKit SeedPosKit(AlterationBook book)
    {
        var c = book.Company;
        book.EnableGst();

        var masters = new InventoryService(c);
        var group = masters.CreateStockGroup("Retail Goods");
        var nos = masters.CreateSimpleUnit("Nos", "Numbers", decimalPlaces: 3);
        var widget = masters.CreateStockItem("Shelf Widget", group.Id, nos.Id);
        widget.Gst = new StockItemGstDetails { Taxability = GstTaxability.Taxable, RateBasisPoints = 1800 };

        var kit = new PosKit
        {
            Book = book,
            PosType = new VoucherType(Guid.NewGuid(), "Sales (POS)", VoucherBaseType.Sales, useForPos: true,
                posConfig: new PosConfig()),
            Widget = widget,
            Main = c.MainLocation!,
            Sales = book.Ledger("Retail Sales", "Sales Accounts"),
            Gift = book.Ledger("Gift Vouchers Issued", "Sundry Debtors"),
            Card = book.Ledger("Card Settlement A/c", "Bank Accounts"),
            Cheque = book.Ledger("Cheque Collection A/c", "Bank Accounts"),
            Cash = book.Ledger("Till Cash", "Cash-in-Hand"),
            Customer = book.Ledger("Loyalty Customer", "Sundry Debtors"),
        };
        c.AddVoucherType(kit.PosType);
        book.Storage.Save(c);
        return kit;
    }

    private static PosBillingViewModel NewPos(PosKit kit) =>
        new(kit.Book.Company, kit.PosType, kit.Book.Storage, onSaved: () => { }, onCancelled: () => { });

    /// <summary>
    /// Posts a MULTI-TENDER POS bill through the real screen: two item rows, a gift + card + cheque split with the
    /// residual in cash, an over-tender producing change, and the card/bank/cheque references filled. Every figure
    /// is a distinct nonce.
    /// </summary>
    private static Voucher PostFatPosBill(PosKit kit, string narration = "POS nonce ONE")
    {
        var vm = NewPos(kit);
        vm.Date = kit.Book.On(4);
        vm.Narration = narration;
        vm.SelectedParty = vm.Parties.Single(p => p.Ledger?.Id == kit.Customer.Id);
        vm.SelectedSalesLedger = vm.SalesLedgers.Single(l => l.Id == kit.Sales.Id);
        vm.SelectedGodown = vm.Godowns.Single(g => g.Id == kit.Main.Id);

        var first = vm.Items[0];
        first.SelectedItem = vm.StockItems.Single(i => i.Id == kit.Widget.Id);
        first.QuantityText = "2";
        first.RateText = "1111.13";

        var second = vm.AddItemLine();
        second.SelectedItem = vm.StockItems.Single(i => i.Id == kit.Widget.Id);
        second.SelectedGodown = vm.Godowns.Single(g => g.Id == kit.Main.Id);
        second.QuantityText = "3";
        second.RateText = "222.27";

        vm.IsMultiTender = true;
        vm.Tenders[0].SelectedLedger = kit.Gift;
        vm.Tenders[0].AmountText = "101.03";
        vm.Tenders[1].SelectedLedger = kit.Card;
        vm.Tenders[1].AmountText = "1007.09";
        vm.Tenders[1].CardNo = "XXXX-4417";
        vm.Tenders[2].SelectedLedger = kit.Cheque;
        vm.Tenders[2].AmountText = "503.11";
        vm.Tenders[2].BankName = "Nonce Bank";
        vm.Tenders[2].ChequeNo = "CHQ-90213";
        vm.Tenders[3].SelectedLedger = kit.Cash;
        vm.Tenders[3].CashTenderedText = "2500";

        Assert.True(vm.Accept(), vm.Message);
        return kit.Book.Company.Vouchers.Last(v => v.TypeId == kit.PosType.Id);
    }

    /// <summary>🔴 The POS round trip: re-opened and re-accepted with nothing changed, the canonical export is
    /// BYTE-IDENTICAL, in memory and on disk — tender split, change, card/cheque references and all.</summary>
    [Fact]
    public void A_pos_bill_re_accepted_unchanged_is_byte_identical()
    {
        using var book = AlterationBook.New("posalter");
        var kit = SeedPosKit(book);
        var posted = PostFatPosBill(kit);
        Assert.Equal(4, posted.PosTenders.Count);

        var before = book.Export();
        var beforeOnDisk = book.ExportReloaded();

        var open = PosBillingViewModel.ForAlter(
            book.Company, posted.Id, book.Storage, onSaved: () => { }, onCancelled: () => { });
        Assert.False(open.IsRefused, open.Refusal);
        var vm = open.Entry!;
        Assert.True(vm.IsAltering);
        Assert.True(vm.AcceptAlteration(), vm.Message);

        Assert.Equal(before, book.Export());
        Assert.Equal(beforeOnDisk, book.ExportReloaded());
    }

    /// <summary>The POS detail survives FIELD BY FIELD — every tender's ledger, amount, reference and the cash
    /// tendered/change, plus both item rows.</summary>
    [Fact]
    public void A_pos_bill_rehydrates_every_tender_and_item_field()
    {
        using var book = AlterationBook.New("posfields");
        var kit = SeedPosKit(book);
        var posted = PostFatPosBill(kit);

        var open = PosBillingViewModel.ForAlter(
            book.Company, posted.Id, book.Storage, onSaved: () => { }, onCancelled: () => { });
        Assert.False(open.IsRefused, open.Refusal);
        var vm = open.Entry!;

        Assert.Equal(posted.Date, vm.Date);
        Assert.Equal("POS nonce ONE", vm.Narration);
        Assert.Equal(kit.Customer.Id, vm.SelectedParty!.Ledger!.Id);
        Assert.Equal(kit.Sales.Id, vm.SelectedSalesLedger!.Id);
        Assert.True(vm.IsMultiTender);

        var rows = vm.Items.Where(l => !l.IsBlank).ToList();
        Assert.Equal(2, rows.Count);
        Assert.Equal(2m, rows[0].ParsedQuantity);
        Assert.Equal(1111.13m, rows[0].EffectiveRate!.Value.Amount);
        Assert.Equal(3m, rows[1].ParsedQuantity);
        Assert.Equal(222.27m, rows[1].EffectiveRate!.Value.Amount);
        Assert.Equal(kit.Main.Id, rows[1].SelectedGodown!.Id);

        Assert.Equal(kit.Gift.Id, vm.Tenders[0].SelectedLedger!.Id);
        Assert.Equal(101.03m, vm.Tenders[0].ParsedAmount);
        Assert.Equal(kit.Card.Id, vm.Tenders[1].SelectedLedger!.Id);
        Assert.Equal(1007.09m, vm.Tenders[1].ParsedAmount);
        Assert.Equal("XXXX-4417", vm.Tenders[1].CardNo);
        Assert.Equal(kit.Cheque.Id, vm.Tenders[2].SelectedLedger!.Id);
        Assert.Equal(503.11m, vm.Tenders[2].ParsedAmount);
        Assert.Equal("Nonce Bank", vm.Tenders[2].BankName);
        Assert.Equal("CHQ-90213", vm.Tenders[2].ChequeNo);
        Assert.Equal(kit.Cash.Id, vm.CashRow.SelectedLedger!.Id);
        Assert.Equal(2500m, vm.CashRow.ParsedCashTendered);

        var postedCash = posted.PosTenders.Single(t => t.Type == PosTenderType.Cash);
        Assert.Equal(postedCash.Amount.Amount, vm.CashRow.ParsedAmount);
        Assert.Equal(IndianFormat.AmountAlways(postedCash.Change!.Value.Amount), vm.ChangeText);
    }

    /// <summary>Altering ONE unrelated field on a POS bill moves ONLY that field.</summary>
    [Fact]
    public void Altering_one_unrelated_field_on_a_pos_bill_moves_only_that_field()
    {
        using var book = AlterationBook.New("posonefield");
        var kit = SeedPosKit(book);
        var posted = PostFatPosBill(kit);
        var before = System.Text.Encoding.UTF8.GetString(book.Export());

        var vm = PosBillingViewModel.ForAlter(
            book.Company, posted.Id, book.Storage, onSaved: () => { }, onCancelled: () => { }).Entry!;
        vm.Narration = "POS nonce TWO";
        Assert.True(vm.AcceptAlteration(), vm.Message);

        var after = System.Text.Encoding.UTF8.GetString(book.Export());
        Assert.Contains("POS nonce ONE", before, StringComparison.Ordinal);
        Assert.Equal(before.Replace("POS nonce ONE", "POS nonce TWO", StringComparison.Ordinal), after);
    }

    /// <summary>The accounting entry screen still refuses a POS bill, by name — its grids have no tender panel —
    /// and the sentence now points at the screen that DOES open it.</summary>
    [Fact]
    public void The_accounting_entry_screen_still_refuses_a_pos_bill_and_names_the_pos_screen()
    {
        using var book = AlterationBook.New("posrefused");
        var kit = SeedPosKit(book);
        var posted = PostFatPosBill(kit);

        var refusal = VoucherAlterationEligibility.RefusalFor(book.Company, posted.Id);
        Assert.False(string.IsNullOrWhiteSpace(refusal));
        Assert.Contains("POS bill", refusal!, StringComparison.Ordinal);
        Assert.Contains("tender", refusal!, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("POS billing screen", refusal!, StringComparison.Ordinal);

        // …and the POS door does open it.
        Assert.Null(PosAlterationEligibility.RefusalFor(book.Company, posted.Id));
    }

    /// <summary>The POS door refuses a voucher that is not a POS bill, by name, rather than opening a tender panel
    /// over a voucher that has no tenders.</summary>
    [Fact]
    public void The_pos_door_refuses_a_non_pos_voucher_by_name()
    {
        using var book = AlterationBook.New("posdoor");
        var journal = book.PostPlainPair(VoucherBaseType.Journal);

        var refusal = PosAlterationEligibility.RefusalFor(book.Company, journal.Id);
        Assert.False(string.IsNullOrWhiteSpace(refusal));
        Assert.Contains("not a POS voucher type", refusal!, StringComparison.Ordinal);
    }

    /// <summary>A tender ledger moved out of its required group after posting is refused by name — re-accepting
    /// would move the payment to another ledger, or be refused by the engine in words the operator never saw.</summary>
    [Fact]
    public void A_tender_ledger_moved_out_of_its_group_is_refused_by_name()
    {
        using var book = AlterationBook.New("tenderdrift");
        var kit = SeedPosKit(book);
        var posted = PostFatPosBill(kit);

        kit.Card.GroupId = book.Company.FindGroupByName("Indirect Expenses")!.Id;

        var open = PosBillingViewModel.ForAlter(
            book.Company, posted.Id, book.Storage, onSaved: () => { }, onCancelled: () => { });
        Assert.True(open.IsRefused);
        Assert.Contains("tender debits is no longer", open.Refusal!, StringComparison.Ordinal);
    }

    /// <summary>
    /// Editing a POS bill really does move it: dropping an item quantity lowers the bill, the cash residual is
    /// re-cut against the unchanged non-cash tenders, and the change rises. The round-trip tests above would pass
    /// on a screen that silently ignored every edit, so this is the opposite direction.
    /// </summary>
    [Fact]
    public void Altering_a_pos_item_quantity_re_cuts_the_cash_residual()
    {
        using var book = AlterationBook.New("posqty");
        var kit = SeedPosKit(book);
        var posted = PostFatPosBill(kit);
        var cashBefore = posted.PosTenders.Single(t => t.Type == PosTenderType.Cash).Amount.Amount;
        var cardBefore = posted.PosTenders.Single(t => t.Type == PosTenderType.Card).Amount.Amount;

        var vm = PosBillingViewModel.ForAlter(
            book.Company, posted.Id, book.Storage, onSaved: () => { }, onCancelled: () => { }).Entry!;
        vm.Items[0].QuantityText = "1";        // was 2
        Assert.True(vm.AcceptAlteration(), vm.Message);

        var altered = book.Company.FindVoucher(posted.Id)!;
        Assert.Equal(posted.Id, altered.Id);
        Assert.Equal(1m, altered.InventoryLines[0].Quantity);

        var cashAfter = altered.PosTenders.Single(t => t.Type == PosTenderType.Cash);
        Assert.True(cashAfter.Amount.Amount < cashBefore);                       // the residual fell…
        Assert.Equal(cardBefore, altered.PosTenders.Single(t => t.Type == PosTenderType.Card).Amount.Amount);
        Assert.Equal(2500m, cashAfter.Tendered!.Value.Amount);                   // …the tendered cash did not…
        Assert.Equal(2500m - cashAfter.Amount.Amount, cashAfter.Change!.Value.Amount);  // …so the change rose.

        // The tender split still foots to the bill, which is what the engine enforces.
        Assert.Equal(altered.TotalDebit.Amount, altered.PosTenders.Sum(t => t.Amount.Amount));
    }

    /// <summary>A POS bill crediting two separate value legs cannot be re-keyed — the screen derives exactly one —
    /// so it is refused by name rather than silently collapsing the two into the picked Sales ledger.</summary>
    [Fact]
    public void A_pos_bill_with_two_value_legs_is_refused_by_name()
    {
        using var book = AlterationBook.New("postwolegs");
        var kit = SeedPosKit(book);
        var other = book.Ledger("Second Sales", "Sales Accounts");
        var voucher = new Voucher(
            Guid.NewGuid(), kit.PosType.Id, book.On(),
            new[]
            {
                new EntryLine(kit.Cash.Id, new Money(100m), DrCr.Debit),
                new EntryLine(kit.Sales.Id, new Money(60m), DrCr.Credit),
                new EntryLine(other.Id, new Money(40m), DrCr.Credit),
            },
            inventoryLines: new[] { new VoucherInventoryLine(kit.Widget.Id, kit.Main.Id, 2m, new Money(50m)) },
            posTenders: new[]
            {
                new PosTender(PosTenderType.Cash, kit.Cash.Id, new Money(100m),
                    Tendered: new Money(100m), Change: Money.Zero),
            });
        new LedgerService(book.Company).Post(voucher);

        var open = PosBillingViewModel.ForAlter(
            book.Company, voucher.Id, book.Storage, onSaved: () => { }, onCancelled: () => { });
        Assert.True(open.IsRefused);
        Assert.Contains("2 separate value legs", open.Refusal!, StringComparison.Ordinal);
    }

    /// <summary>A POS-typed voucher carrying NO tender records is refused by name — the payment panel would have to
    /// invent a split the bill never had.</summary>
    [Fact]
    public void A_pos_typed_voucher_with_no_tenders_is_refused_by_name()
    {
        using var book = AlterationBook.New("postenderless");
        var kit = SeedPosKit(book);
        var voucher = new Voucher(
            Guid.NewGuid(), kit.PosType.Id, book.On(),
            new[]
            {
                new EntryLine(kit.Customer.Id, new Money(100m), DrCr.Debit),
                new EntryLine(kit.Sales.Id, new Money(100m), DrCr.Credit),
            },
            partyId: kit.Customer.Id,
            inventoryLines: new[] { new VoucherInventoryLine(kit.Widget.Id, kit.Main.Id, 2m, new Money(50m)) });
        new LedgerService(book.Company).Post(voucher);

        var refusal = PosAlterationEligibility.RefusalFor(book.Company, voucher.Id);
        Assert.False(string.IsNullOrWhiteSpace(refusal));
        Assert.Contains("no tender records", refusal!, StringComparison.Ordinal);
    }

    // ================================================================ (D) ER-13

    /// <summary>
    /// 🔴 <b>ER-13.</b> A book with NO item invoices and no POS bills is untouched by this slice: a plain Dr/Cr
    /// voucher still re-opens on the plain grid, still re-accepts, and the canonical export is byte-identical —
    /// in memory and on disk.
    /// </summary>
    [Fact]
    public void A_book_with_no_item_invoices_is_byte_identical_after_an_alteration()
    {
        using var book = AlterationBook.New("er13");
        var rent = book.Ledger("Rent", "Indirect Expenses");
        var bank = book.Ledger("Cheque Bank", "Bank Accounts");
        var journal = book.Post(VoucherBaseType.Journal, book.On(),
            new[] { (rent, DrCr.Debit, "9876.51"), (bank, DrCr.Credit, "9876.51") },
            narration: "ER13 nonce");
        book.PostPlainPair(VoucherBaseType.Payment, 4321.09m);
        book.PostPlainPair(VoucherBaseType.Receipt, 1357.91m);

        var before = book.Export();
        var beforeOnDisk = book.ExportReloaded();

        var open = book.ForAlter(journal.Id);
        Assert.False(open.IsRefused, open.Refusal);
        Assert.True(open.Entry!.IsAsVoucherMode);
        Assert.True(open.Entry!.AcceptAlteration(), open.Entry!.Message);

        Assert.Equal(before, book.Export());
        Assert.Equal(beforeOnDisk, book.ExportReloaded());
    }
}
