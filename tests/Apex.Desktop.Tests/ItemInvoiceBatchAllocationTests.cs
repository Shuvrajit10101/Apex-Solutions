using System;
using System.Globalization;
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
/// G-5 — <b>batch allocation on the two invoice screens</b> (Purchase F9 / Sales F8 in item-invoice mode).
/// BOOK pp.130–132 <b>[verified-A1]</b> walks batch entry through F9 then F8 with <c>Mfg Dt.</c> /
/// <c>Expiry Date</c> / <c>New Number</c>; before this slice those two screens carried only a free-text batch
/// label (<c>InventoryVoucherLineViewModel.Batch</c>) while the real sub-screen
/// (<see cref="BatchAllocationViewModel"/>) was wired only to the stock/movement screens
/// (spec §4.3, C-20 "<b>WRONG</b>").
///
/// <para>These tests drive the REAL shell (<see cref="MainWindowViewModel"/>) + entry view model against a
/// seeded company on a throwaway <c>.db</c> — no UI toolkit. They cover the four-layer config gate (F11
/// company flag · item "Maintain in Batches" · item-invoice mode · the voucher-screen batch knob), selecting an
/// existing batch with its available balance, creating a new batch number inline with mfg/expiry, splitting one
/// line across several batches so the quantities reconcile to the line quantity (C-29), the non-blocking expiry
/// warning on an outward movement (U-6), and the ER-13 no-op guarantee for a non-batch item.</para>
///
/// <para><b>Odd-paisa fixtures throughout.</b> Every money assertion uses a rate that does NOT round to a
/// whole rupee, so a split line's Σ value is proved equal to the unsplit value to the paisa rather than by
/// accident.</para>
/// </summary>
public sealed class ItemInvoiceBatchAllocationTests : IDisposable
{
    private readonly string _tempDir;
    private readonly CompanyStorage _storage;

    public ItemInvoiceBatchAllocationTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "ApexItemInvoiceBatchTests_" + Guid.NewGuid().ToString("N"));
        _storage = new CompanyStorage(_tempDir);
    }

    // ---------------------------------------------------------------- scaffolding

    private sealed class Kit
    {
        public required MainWindowViewModel Vm { get; init; }
        public required string CompanyName { get; init; }
        public required StockItem BatchItem { get; init; }
        public required StockItem PlainItem { get; init; }
        public required Godown Main { get; init; }
        public required Guid SupplierId { get; init; }
        public required Guid CustomerId { get; init; }
        public Company C => Vm.Company!;
    }

    /// <summary>
    /// A seeded company with the batch feature ON (F11), one batch-tracked "Ibuprofen" (Strip, Maintain in
    /// Batches + Track Mfg + Use Expiry — BOOK p.130's own example) and one plain "Bolt" item, plus the four
    /// ledgers an item invoice needs.
    /// </summary>
    private Kit NewKit(string companyName, bool batchFeature = true)
    {
        var vm = new MainWindowViewModel(_storage);
        vm.NewCompanyName = companyName;
        vm.CreateCompany();
        Assert.Equal(Screen.Gateway, vm.CurrentScreen);

        var c = vm.Company!;
        // MaintainBatchwiseDetails is INFERRED when never set explicitly (a batch master or a batch-tracked item
        // implies it, Company.cs:154-163), so the flag-off fixture must set it explicitly false — otherwise the
        // batch item below would silently switch layer 1 back on and the gate test would prove nothing.
        c.MaintainBatchwiseDetails = batchFeature;

        var masters = new InventoryService(c);
        var grp = masters.CreateStockGroup("Medicine");
        var strip = masters.CreateSimpleUnit("Strip", "Strips");

        var med = masters.CreateStockItem("Ibuprofen", grp.Id, strip.Id);
        med.MaintainInBatches = true;
        med.TrackManufacturingDate = true;
        med.UseExpiryDates = true;

        var bolt = masters.CreateStockItem("Bolt", grp.Id, strip.Id);

        AddLedger(c, "Purchases", "Purchase Accounts");
        AddLedger(c, "Sales", "Sales Accounts");
        var supplier = AddLedger(c, "Ritesh Mishra", "Sundry Creditors");
        var customer = AddLedger(c, "Ritesh Gupta", "Sundry Debtors");

        _storage.Save(c);

        return new Kit
        {
            Vm = vm,
            CompanyName = companyName,
            BatchItem = med,
            PlainItem = bolt,
            Main = c.MainLocation!,
            SupplierId = supplier.Id,
            CustomerId = customer.Id,
        };
    }

    private static DomainLedger AddLedger(Company c, string name, string groupName)
    {
        var group = c.FindGroupByName(groupName) ?? throw new InvalidOperationException($"No group '{groupName}'.");
        var ledger = new DomainLedger(Guid.NewGuid(), name, group.Id, Money.Zero, openingIsDebit: false);
        c.AddLedger(ledger);
        return ledger;
    }

    /// <summary>
    /// Brings two batches of the batch item on hand with a GRN so the outward tests have real per-batch
    /// balances: <c>IB-2401</c> (40 Strips, expires late) and <c>IB-2312</c> (30 Strips, ALREADY EXPIRED as of
    /// <paramref name="asOf"/>). <see cref="BatchService.CreateBatch"/> raises the MASTER only (its
    /// inward qty/rate is a declared cost layer, not a movement — <c>BatchMaster.cs:49</c>), so the GRN is the
    /// single source of the on-hand figures asserted below.
    /// </summary>
    private void SeedTwoBatchesOnHand(Kit k, DateOnly asOf)
    {
        var c = k.C;
        var batches = new BatchService(c);
        batches.CreateBatch(k.BatchItem.Id, "IB-2401",
            manufacturingDate: asOf.AddMonths(-6), expiryDate: asOf.AddMonths(9),
            godownId: k.Main.Id, inwardQuantity: 40m, inwardRate: Money.FromRupees(11.37m));
        batches.CreateBatch(k.BatchItem.Id, "IB-2312",
            manufacturingDate: asOf.AddMonths(-18), expiryDate: asOf.AddDays(-7),
            godownId: k.Main.Id, inwardQuantity: 30m, inwardRate: Money.FromRupees(11.37m));

        var posting = new InventoryPostingService(c);
        var grn = c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.ReceiptNote);
        posting.Post(new InventoryVoucher(Guid.NewGuid(), grn.Id, asOf.AddDays(-30), new[]
        {
            new InventoryAllocation(k.BatchItem.Id, k.Main.Id, 40m, StockDirection.Inward, Money.FromRupees(11.37m), "IB-2401"),
            new InventoryAllocation(k.BatchItem.Id, k.Main.Id, 30m, StockDirection.Inward, Money.FromRupees(11.37m), "IB-2312"),
        }, number: 0));
        _storage.Save(c);
    }

    private static VoucherEntryViewModel OpenItemInvoice(Kit k, VoucherBaseType baseType, DateOnly? date = null)
    {
        k.Vm.OpenVoucher(baseType, date);
        Assert.Equal(Screen.VoucherEntry, k.Vm.CurrentScreen);
        var entry = k.Vm.VoucherEntry!;
        Assert.True(entry.CanBeItemInvoice);
        k.Vm.ToggleItemInvoice();
        Assert.True(entry.IsItemInvoice);
        return entry;
    }

    private static void SelectParty(VoucherEntryViewModel entry, Guid partyId) =>
        entry.SelectedParty = entry.Parties.Single(p => p.Ledger?.Id == partyId);

    private static InventoryVoucherLineViewModel FillLine(
        VoucherEntryViewModel entry, StockItem item, Godown godown, decimal qty, string rate)
    {
        var line = entry.InventoryLines[0];
        line.SelectedItem = entry.StockItems.Single(i => i.Id == item.Id);
        line.SelectedGodown = entry.Godowns.Single(g => g.Id == godown.Id);
        line.QuantityText = qty.ToString(CultureInfo.InvariantCulture);
        line.RateText = rate;
        return line;
    }

    private Company Reload(string companyName)
    {
        var entry = _storage.ListCompanies().Single(e => e.Name == companyName);
        return _storage.Load(entry);
    }

    private static decimal OnHand(Company c, Guid itemId, Guid godownId, string? batch, DateOnly asOf) =>
        new InventoryLedger(c).OnHand(itemId, godownId, batch, asOf);

    // ================================================================ (1) the four-layer config gate

    [Fact]
    public void Batch_sub_screen_is_gated_by_all_four_layers_on_the_item_invoice()
    {
        var k = NewKit("Four Layer Co");
        var entry = OpenItemInvoice(k, VoucherBaseType.Purchase);
        SelectParty(entry, k.SupplierId);

        // Layer 3 (item) — a PLAIN item never offers the sub-screen, however the other layers stand.
        var plain = FillLine(entry, k.PlainItem, k.Main, 5m, "12.35");
        Assert.False(entry.LineWantsBatchAllocation(plain));
        Assert.False(plain.WantsBatchAllocation);

        // Layer 3 satisfied — the batch item with item + godown + a positive qty turns the affordance on.
        plain.SelectedItem = entry.StockItems.Single(i => i.Id == k.BatchItem.Id);
        var line = entry.InventoryLines[0];
        line.QuantityText = "5";
        Assert.True(entry.LineWantsBatchAllocation(line));
        Assert.True(line.WantsBatchAllocation);

        // Layer 4 (the voucher-screen batch knob) — off ⇒ the batch field is not offered on THIS screen.
        entry.UseBatchWiseDetails = false;
        Assert.False(entry.LineWantsBatchAllocation(line));
        Assert.False(line.WantsBatchAllocation);
        entry.UseBatchWiseDetails = true;
        Assert.True(entry.LineWantsBatchAllocation(line));

        // Layer 2 (mode) — leaving item-invoice mode takes the batch field with it.
        k.Vm.ToggleItemInvoice();                    // Item -> Accounting/plain (Purchase flips two-way)
        Assert.False(entry.IsItemInvoice);
        Assert.False(entry.LineWantsBatchAllocation(line));
    }

    [Fact]
    public void Company_flag_off_keeps_the_batch_sub_screen_off_the_item_invoice()
    {
        // Layer 1 (F11 "Enable Batches", C-06) — off ⇒ no batch sub-screen anywhere, even on a batch item.
        var k = NewKit("No Feature Co", batchFeature: false);
        Assert.False(k.C.MaintainBatchwiseDetails);

        var entry = OpenItemInvoice(k, VoucherBaseType.Purchase);
        SelectParty(entry, k.SupplierId);
        var line = FillLine(entry, k.BatchItem, k.Main, 5m, "12.35");

        Assert.False(entry.LineWantsBatchAllocation(line));
        Assert.False(line.WantsBatchAllocation);
        Assert.False(entry.CanUseBatchWiseDetails);

        // And the request is a hard no-op — no column is pushed, the voucher screen stays live.
        entry.RequestBatchAllocation(line);
        Assert.Equal(Screen.VoucherEntry, k.Vm.CurrentScreen);
        Assert.Null(k.Vm.BatchAllocation);
    }

    // ================================================================ (2) F9 — new batch number inline

    [Fact]
    public void Purchase_item_invoice_creates_a_new_batch_inline_with_mfg_and_expiry()
    {
        // BOOK p.131 [verified-A1]: F9 → item → "New Number" → batch no → Quantity / Rate / Mfg Dt. / Expiry.
        var k = NewKit("F9 New Batch Co");
        var date = k.C.FinancialYearStart.AddDays(40);
        var entry = OpenItemInvoice(k, VoucherBaseType.Purchase, date);
        SelectParty(entry, k.SupplierId);

        // ODD-PAISA fixture: 12 Strips @ ₹137.45 = ₹1,649.40 (not a round figure in either operand or product).
        var line = FillLine(entry, k.BatchItem, k.Main, 12m, "137.45");
        Assert.True(entry.LineWantsBatchAllocation(line));

        // Alt+B / "⧉" opens the real sub-screen as a cascade column to the right of the live invoice.
        Assert.True(entry.RequestBatchAllocationForFirstEligibleLine());
        Assert.Equal(Screen.BatchAllocation, k.Vm.CurrentScreen);
        var sub = k.Vm.BatchAllocation!;

        // Inward ⇒ no existing stock to draw from ⇒ a single blank row seeded for the received batch.
        var row = sub.Lines[0];
        row.SelectedBatch = sub.BatchOptions.Single(o => o.IsNew);      // "◦ New Number…"
        row.NewBatchNumber = "IB-2405";
        row.ManufacturingDateText = "01-05-2020";
        row.ExpiryText = "01-05-2021";
        row.QuantityText = "12";
        Assert.True(sub.IsBalanced);
        Assert.True(sub.Apply(), sub.Message);

        // The committed allocation carries the DATES, not just the number (the pre-slice sub-screen dropped them).
        var committed = sub.Allocations.Single();
        Assert.Equal("IB-2405", committed.BatchNumber);
        Assert.True(committed.IsNewBatch);
        Assert.Equal(new DateOnly(2020, 5, 1), committed.ManufacturingDate);
        Assert.Equal(new DateOnly(2021, 5, 1), committed.ExpiryDate);

        // Back to the live invoice; the line shows the batch and accepts.
        k.Vm.Back();
        Assert.Equal(Screen.VoucherEntry, k.Vm.CurrentScreen);
        var live = k.Vm.VoucherEntry!;
        Assert.Equal("IB-2405", live.InventoryLines[0].BatchLabel);
        Assert.Equal("1,649.40", live.ItemsTotalText);
        Assert.True(live.Accept(), live.Message);

        // The batch MASTER was created on Accept with the captured mfg/expiry.
        var c = Reload(k.CompanyName);
        var master = c.FindBatchByNumber(k.BatchItem.Id, "IB-2405");
        Assert.NotNull(master);
        Assert.Equal(new DateOnly(2020, 5, 1), master!.ManufacturingDate);
        Assert.Equal(new DateOnly(2021, 5, 1), master.ExpiryDate);

        // …and the posted item line carries the batch, so the stock landed IN that batch, to the paisa.
        var v = c.Vouchers.Single(x => x.InventoryLines.Count > 0);
        var il = v.InventoryLines.Single();
        Assert.Equal("IB-2405", il.BatchLabel);
        Assert.Equal(12m, il.Quantity);
        Assert.Equal(1649.40m, il.Value.Amount);
        Assert.Equal(12m, OnHand(c, k.BatchItem.Id, k.Main.Id, "IB-2405", date));
    }

    // ================================================================ (3) F8 — pick an existing batch + balance

    [Fact]
    public void Sales_item_invoice_offers_existing_batches_with_their_available_balance()
    {
        var k = NewKit("F8 Pick Co");
        var date = k.C.FinancialYearStart.AddDays(200);
        SeedTwoBatchesOnHand(k, date);

        var entry = OpenItemInvoice(k, VoucherBaseType.Sales, date);
        SelectParty(entry, k.CustomerId);
        FillLine(entry, k.BatchItem, k.Main, 10m, "19.75");

        Assert.True(entry.RequestBatchAllocationForFirstEligibleLine());
        var sub = k.Vm.BatchAllocation!;

        // Every existing batch is offered, and each option states the balance available to issue (RQ-5).
        var opt2401 = sub.BatchOptions.Single(o => o.Batch?.BatchNumber == "IB-2401");
        var opt2312 = sub.BatchOptions.Single(o => o.Batch?.BatchNumber == "IB-2312");
        Assert.Equal(40m, opt2401.AvailableQuantity);
        Assert.Equal(30m, opt2312.AvailableQuantity);
        Assert.Contains("40", opt2401.Display);
        Assert.Contains("30", opt2312.Display);
        // The "New Number" entry is not a real lot, so it carries no balance.
        Assert.Equal(0m, sub.BatchOptions.Single(o => o.IsNew).AvailableQuantity);
    }

    // ================================================================ (4) split one line across several batches

    [Fact]
    public void Sales_item_invoice_splits_one_line_across_two_batches_and_reconciles()
    {
        var k = NewKit("Split Co");
        var date = k.C.FinancialYearStart.AddDays(200);
        SeedTwoBatchesOnHand(k, date);

        var entry = OpenItemInvoice(k, VoucherBaseType.Sales, date);
        SelectParty(entry, k.CustomerId);

        // ODD-PAISA fixture: 30 Strips @ ₹19.75 = ₹592.50 for the WHOLE line; the split must foot to the paisa.
        FillLine(entry, k.BatchItem, k.Main, 30m, "19.75");
        var unsplitTotal = entry.ItemsTotalText;
        Assert.Equal("592.50", unsplitTotal);

        Assert.True(entry.RequestBatchAllocationForFirstEligibleLine());
        var sub = k.Vm.BatchAllocation!;

        // Replace the FEFO default with an explicit 18 / 12 split across the two batches.
        foreach (var l in sub.Lines) { l.SelectedBatch = null; l.QuantityText = string.Empty; }
        sub.Lines[0].SelectedBatch = sub.BatchOptions.Single(o => o.Batch?.BatchNumber == "IB-2312");
        sub.Lines[0].QuantityText = "18";
        sub.Lines[1].SelectedBatch = sub.BatchOptions.Single(o => o.Batch?.BatchNumber == "IB-2401");
        sub.Lines[1].QuantityText = "12";
        Assert.True(sub.IsBalanced);            // Σ 30 == the line quantity (C-29)
        Assert.True(sub.Apply(), sub.Message);

        k.Vm.Back();
        var live = k.Vm.VoucherEntry!;
        // The grid summarises the split; the money is UNCHANGED by splitting (Σ = the unsplit total, to the paisa).
        Assert.Equal("Multi (2)", live.InventoryLines[0].BatchLabel);
        Assert.True(live.InventoryLines[0].HasBatchSplit);
        Assert.Equal(unsplitTotal, live.ItemsTotalText);
        Assert.True(live.Accept(), live.Message);

        var c = Reload(k.CompanyName);
        var v = c.Vouchers.Single(x => x.InventoryLines.Count > 0);

        // ONE grid line posted as TWO batch-stamped item lines whose quantities reconcile to the line quantity…
        Assert.Equal(2, v.InventoryLines.Count);
        Assert.Equal(30m, v.InventoryLines.Sum(l => l.Quantity));
        Assert.Equal(18m, v.InventoryLines.Single(l => l.BatchLabel == "IB-2312").Quantity);
        Assert.Equal(12m, v.InventoryLines.Single(l => l.BatchLabel == "IB-2401").Quantity);
        // …and whose Σ value is the unsplit value EXACTLY (no rounding drift from the split).
        Assert.Equal(592.50m, v.InventoryLines.Sum(l => l.Value.Amount));

        // Stock moved out of the RIGHT batches: 30 - 18 = 12 left in IB-2312, 40 - 12 = 28 in IB-2401.
        Assert.Equal(12m, OnHand(c, k.BatchItem.Id, k.Main.Id, "IB-2312", date));
        Assert.Equal(28m, OnHand(c, k.BatchItem.Id, k.Main.Id, "IB-2401", date));
    }

    [Fact]
    public void Split_that_does_not_add_up_to_the_line_quantity_is_refused()
    {
        var k = NewKit("Unbalanced Split Co");
        var date = k.C.FinancialYearStart.AddDays(200);
        SeedTwoBatchesOnHand(k, date);

        var entry = OpenItemInvoice(k, VoucherBaseType.Sales, date);
        SelectParty(entry, k.CustomerId);
        FillLine(entry, k.BatchItem, k.Main, 30m, "19.75");
        Assert.True(entry.RequestBatchAllocationForFirstEligibleLine());
        var sub = k.Vm.BatchAllocation!;

        foreach (var l in sub.Lines) { l.SelectedBatch = null; l.QuantityText = string.Empty; }
        sub.Lines[0].SelectedBatch = sub.BatchOptions.Single(o => o.Batch?.BatchNumber == "IB-2401");
        sub.Lines[0].QuantityText = "18";          // 18 of 30 — short

        Assert.False(sub.IsBalanced);
        Assert.False(sub.Apply());
        Assert.NotNull(sub.Message);
    }

    // ================================================================ (5) expiry warning on an outward invoice

    [Fact]
    public void Expired_batch_on_an_outward_item_invoice_warns_but_never_blocks()
    {
        // U-6 is UNVERIFIED in the corpus (help documents earliest-expiry GUIDANCE, never a block); our design
        // choice is warn-not-block (RQ-7, spec §7) and this test locks it: the sale of an expired batch posts.
        var k = NewKit("Expiry Warn Co");
        var date = k.C.FinancialYearStart.AddDays(200);
        SeedTwoBatchesOnHand(k, date);

        var entry = OpenItemInvoice(k, VoucherBaseType.Sales, date);
        SelectParty(entry, k.CustomerId);
        FillLine(entry, k.BatchItem, k.Main, 10m, "19.75");
        Assert.True(entry.RequestBatchAllocationForFirstEligibleLine());
        var sub = k.Vm.BatchAllocation!;

        // The item uses expiry ⇒ FEFO default ⇒ the already-EXPIRED IB-2312 is drawn first and flagged.
        var flagged = sub.Lines.Single(l => l.SelectedBatch?.Batch?.BatchNumber == "IB-2312");
        Assert.True(flagged.IsExpired);
        Assert.NotNull(flagged.Warning);
        Assert.Contains("EXPIRED", flagged.Warning!, StringComparison.Ordinal);

        // Warn, never block: the allocation applies and the invoice posts.
        Assert.True(sub.Apply(), sub.Message);
        k.Vm.Back();
        // Hold the entry: a successful Accept navigates away and nulls the shell's VoucherEntry, so re-reading it
        // for the failure message would NRE on the happy path.
        var live = k.Vm.VoucherEntry!;
        Assert.True(live.Accept(), live.Message);

        var c = Reload(k.CompanyName);
        Assert.Equal(20m, OnHand(c, k.BatchItem.Id, k.Main.Id, "IB-2312", date));   // 30 - 10 issued
    }

    // ================================================================ (6) ER-13 — a non-batch item is unchanged

    [Fact]
    public void Non_batch_item_posts_exactly_as_before_the_batch_wiring()
    {
        var k = NewKit("Unchanged Co");
        var date = k.C.FinancialYearStart.AddDays(40);
        var entry = OpenItemInvoice(k, VoucherBaseType.Purchase, date);
        SelectParty(entry, k.SupplierId);

        // ODD-PAISA fixture: 7 @ ₹13.33 = ₹93.31.
        var line = FillLine(entry, k.PlainItem, k.Main, 7m, "13.33");
        Assert.False(line.WantsBatchAllocation);
        Assert.Empty(line.BatchAllocations);
        Assert.False(line.HasBatchSplit);
        Assert.Equal("93.31", entry.ItemsTotalText);
        Assert.True(entry.Accept(), entry.Message);

        var c = Reload(k.CompanyName);
        var il = c.Vouchers.Single(x => x.InventoryLines.Count > 0).InventoryLines.Single();
        Assert.Null(il.BatchLabel);                 // no batch stamped — byte-identical to a pre-slice line
        Assert.Equal(7m, il.Quantity);
        Assert.Equal(93.31m, il.Value.Amount);
    }

    // ================================================================ (7) a stale split is dropped, never posted

    [Fact]
    public void Changing_the_quantity_after_allocating_drops_the_split_and_its_summary_label()
    {
        // A PURCHASE (inward) so the scenario is about the stale split alone — an outward of an unbatched
        // quantity would additionally trip the negative-stock guard and mask what is being proved here.
        var k = NewKit("Stale Split Co");
        var date = k.C.FinancialYearStart.AddDays(40);

        var entry = OpenItemInvoice(k, VoucherBaseType.Purchase, date);
        SelectParty(entry, k.SupplierId);
        FillLine(entry, k.BatchItem, k.Main, 30m, "19.75");
        Assert.True(entry.RequestBatchAllocationForFirstEligibleLine());
        var sub = k.Vm.BatchAllocation!;
        sub.Lines[0].SelectedBatch = sub.BatchOptions.Single(o => o.IsNew);
        sub.Lines[0].NewBatchNumber = "RX-A";
        sub.Lines[0].QuantityText = "18";
        sub.Lines[1].SelectedBatch = sub.BatchOptions.Single(o => o.IsNew);
        sub.Lines[1].NewBatchNumber = "RX-B";
        sub.Lines[1].QuantityText = "12";
        Assert.True(sub.Apply(), sub.Message);

        k.Vm.Back();
        var live = k.Vm.VoucherEntry!;
        var line = live.InventoryLines[0];
        Assert.Equal("Multi (2)", line.BatchLabel);

        // Re-typing the quantity invalidates the 18/12 split. It must be DROPPED — and so must the derived
        // "Multi (2)" summary, which is not a batch number and must never reach a posted line as one.
        line.QuantityText = "25";
        Assert.Empty(line.BatchAllocations);
        Assert.False(line.HasBatchSplit);
        Assert.Equal(string.Empty, line.BatchLabel);

        Assert.True(live.Accept(), live.Message);
        var c = Reload(k.CompanyName);
        var il = c.Vouchers.Single(x => x.InventoryLines.Count > 0).InventoryLines.Single();
        Assert.Null(il.BatchLabel);              // NOT "Multi (2)"
        Assert.Equal(25m, il.Quantity);
    }

    [Fact]
    public void Typing_over_the_derived_label_overrides_the_allocation()
    {
        var k = NewKit("Override Co");
        var date = k.C.FinancialYearStart.AddDays(200);
        SeedTwoBatchesOnHand(k, date);

        var entry = OpenItemInvoice(k, VoucherBaseType.Sales, date);
        SelectParty(entry, k.CustomerId);
        FillLine(entry, k.BatchItem, k.Main, 30m, "19.75");
        Assert.True(entry.RequestBatchAllocationForFirstEligibleLine());
        var sub = k.Vm.BatchAllocation!;
        foreach (var l in sub.Lines) { l.SelectedBatch = null; l.QuantityText = string.Empty; }
        sub.Lines[0].SelectedBatch = sub.BatchOptions.Single(o => o.Batch?.BatchNumber == "IB-2312");
        sub.Lines[0].QuantityText = "18";
        sub.Lines[1].SelectedBatch = sub.BatchOptions.Single(o => o.Batch?.BatchNumber == "IB-2401");
        sub.Lines[1].QuantityText = "12";
        Assert.True(sub.Apply(), sub.Message);

        k.Vm.Back();
        var live = k.Vm.VoucherEntry!;
        var line = live.InventoryLines[0];

        // The operator overtypes the summary with a single batch — an explicit override, so the split goes.
        line.BatchLabel = "IB-2401";
        Assert.Empty(line.BatchAllocations);
        Assert.False(line.HasBatchSplit);

        Assert.True(live.Accept(), live.Message);
        var c = Reload(k.CompanyName);
        var il = c.Vouchers.Single(x => x.InventoryLines.Count > 0).InventoryLines.Single();
        Assert.Equal("IB-2401", il.BatchLabel);
        Assert.Equal(30m, il.Quantity);
    }

    // ================================================================ (8) the item's own date switches

    [Fact]
    public void Item_that_does_not_use_expiry_never_offers_or_captures_an_expiry_date()
    {
        var k = NewKit("No Expiry Co");
        // Batch-tracked but with BOTH date switches off (BOOK p.130 makes the three switches independent).
        k.BatchItem.TrackManufacturingDate = false;
        k.BatchItem.UseExpiryDates = false;

        var date = k.C.FinancialYearStart.AddDays(40);
        var entry = OpenItemInvoice(k, VoucherBaseType.Purchase, date);
        SelectParty(entry, k.SupplierId);
        FillLine(entry, k.BatchItem, k.Main, 12m, "137.45");
        Assert.True(entry.RequestBatchAllocationForFirstEligibleLine());
        var sub = k.Vm.BatchAllocation!;

        Assert.False(sub.ShowManufacturingDate);
        Assert.False(sub.ShowExpiryDate);
        Assert.False(sub.Lines[0].ShowExpiryDate);
        Assert.False(sub.Lines[0].ShowManufacturingDate);

        var row = sub.Lines[0];
        row.SelectedBatch = sub.BatchOptions.Single(o => o.IsNew);
        row.NewBatchNumber = "PL-01";
        row.ExpiryText = "01-05-2021";     // even if a date reaches the field, the item does not use expiry
        row.QuantityText = "12";
        Assert.True(sub.Apply(), sub.Message);
        Assert.Null(sub.Allocations.Single().ExpiryDate);

        k.Vm.Back();
        var live = k.Vm.VoucherEntry!;
        Assert.True(live.Accept(), live.Message);

        var c = Reload(k.CompanyName);
        var master = c.FindBatchByNumber(k.BatchItem.Id, "PL-01");
        Assert.NotNull(master);
        Assert.Null(master!.ExpiryDate);
        Assert.Null(master.ManufacturingDate);
    }

    [Fact]
    public void An_unreadable_expiry_date_is_rejected_never_silently_discarded()
    {
        var k = NewKit("Bad Date Co");
        var date = k.C.FinancialYearStart.AddDays(40);
        var entry = OpenItemInvoice(k, VoucherBaseType.Purchase, date);
        SelectParty(entry, k.SupplierId);
        FillLine(entry, k.BatchItem, k.Main, 12m, "137.45");
        Assert.True(entry.RequestBatchAllocationForFirstEligibleLine());
        var sub = k.Vm.BatchAllocation!;

        var row = sub.Lines[0];
        row.SelectedBatch = sub.BatchOptions.Single(o => o.IsNew);
        row.NewBatchNumber = "IB-9999";
        row.ExpiryText = "not-a-date";
        row.QuantityText = "12";

        Assert.False(sub.Apply());
        Assert.NotNull(sub.Message);
        Assert.Contains("expiry date", sub.Message!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void A_batch_that_expires_before_it_was_manufactured_is_rejected()
    {
        var k = NewKit("Impossible Dates Co");
        var date = k.C.FinancialYearStart.AddDays(40);
        var entry = OpenItemInvoice(k, VoucherBaseType.Purchase, date);
        SelectParty(entry, k.SupplierId);
        FillLine(entry, k.BatchItem, k.Main, 12m, "137.45");
        Assert.True(entry.RequestBatchAllocationForFirstEligibleLine());
        var sub = k.Vm.BatchAllocation!;

        var row = sub.Lines[0];
        row.SelectedBatch = sub.BatchOptions.Single(o => o.IsNew);
        row.NewBatchNumber = "IB-8888";
        row.ManufacturingDateText = "01-05-2021";
        row.ExpiryText = "01-05-2020";
        row.QuantityText = "12";

        Assert.False(sub.Apply());
        Assert.NotNull(sub.Message);
        Assert.Contains("manufactured", sub.Message!, StringComparison.OrdinalIgnoreCase);
    }

    // ================================================================ (9) F12 on the invoice screen is UNCHANGED

    [Fact]
    public void F12_on_the_invoice_screen_still_opens_the_voucher_numbering_config()
    {
        // Regression guard. The layer-4 batch knob is per-entry-screen state (spec §2.6), but our WHOLE F12
        // surface on a voucher screen is the voucher-numbering config (spec §2.6 closing note;
        // MainWindowViewModel.IsVoucherNumberingContext). Binding the batch knob to the F12 KEY would have
        // silently stolen that shipped screen, so the knob is a field on the invoice options panel instead.
        var k = NewKit("F12 Co");
        var entry = OpenItemInvoice(k, VoucherBaseType.Purchase);
        SelectParty(entry, k.SupplierId);
        FillLine(entry, k.BatchItem, k.Main, 5m, "12.35");

        Assert.True(entry.CanUseBatchWiseDetails);
        Assert.True(entry.UseBatchWiseDetails);     // default ON — a batch company needs no configuring

        var f12 = k.Vm.ButtonBar.First(b => b.Key == "F12");
        f12.Action();
        Assert.Equal(Screen.VoucherNumberingConfig, k.Vm.CurrentScreen);
        Assert.True(entry.UseBatchWiseDetails);     // untouched by F12
    }

    // ============================================ (10) a split RE-ATTRIBUTES the quantity; it never REVALUES

    /// <summary>
    /// Splits the first line of a Sales item invoice across the two seeded batches with the supplied quantities,
    /// leaving the sub-screen committed and the shell back on the live invoice. Returns the live entry VM.
    /// </summary>
    private static VoucherEntryViewModel SplitAcrossTheTwoSeededBatches(
        Kit k, VoucherEntryViewModel entry, string firstQty, string secondQty)
    {
        Assert.True(entry.RequestBatchAllocationForFirstEligibleLine());
        var sub = k.Vm.BatchAllocation!;
        foreach (var l in sub.Lines) { l.SelectedBatch = null; l.QuantityText = string.Empty; }
        sub.Lines[0].SelectedBatch = sub.BatchOptions.Single(o => o.Batch?.BatchNumber == "IB-2312");
        sub.Lines[0].QuantityText = firstQty;
        sub.Lines[1].SelectedBatch = sub.BatchOptions.Single(o => o.Batch?.BatchNumber == "IB-2401");
        sub.Lines[1].QuantityText = secondQty;
        Assert.True(sub.IsBalanced, sub.RemainingText);
        Assert.True(sub.Apply(), sub.Message);
        k.Vm.Back();
        return k.Vm.VoucherEntry!;
    }

    [Fact]
    public void Splitting_a_line_across_batches_never_changes_its_value_or_the_invoice_total()
    {
        // THE INVARIANT: choosing WHICH lots the goods come out of is a re-attribution of quantity. It must not
        // move the money by a paisa — the customer is billed for 3 Strips @ ₹19.75 either way.
        //
        // FRACTIONAL + ODD-PAISA fixture, deliberately: 1.5 × ₹19.75 = ₹29.625, a SUB-PAISA product. Rounding
        // each batch row independently (₹29.63 + ₹29.63) invents a paisa the unsplit line never had
        // (3 × ₹19.75 = ₹59.25 exactly). Whole quantities can never expose this, which is why every earlier
        // split fixture (18/12) was blind to it.
        var k = NewKit("Re-attribution Co");
        var date = k.C.FinancialYearStart.AddDays(200);
        SeedTwoBatchesOnHand(k, date);

        var entry = OpenItemInvoice(k, VoucherBaseType.Sales, date);
        SelectParty(entry, k.CustomerId);
        var line = FillLine(entry, k.BatchItem, k.Main, 3m, "19.75");

        var unsplitValue = line.LineValue;
        var unsplitTotal = entry.ItemsTotalText;
        Assert.Equal(59.25m, unsplitValue.Amount);
        Assert.Equal("59.25", unsplitTotal);

        var live = SplitAcrossTheTwoSeededBatches(k, entry, "1.5", "1.5");
        var split = live.InventoryLines[0];
        Assert.True(split.HasBatchSplit);

        // ER-4 asserted DIRECTLY on the one definition, not only through the posted rows (which recompute it):
        // the split line is worth exactly what the unsplit line was worth.
        Assert.Equal(unsplitValue.Amount, split.LineValue.Amount);
        Assert.Equal(unsplitTotal, live.ItemsTotalText);
    }

    [Fact]
    public void A_split_whose_batch_rows_cannot_foot_to_the_line_value_is_refused_and_posts_nothing()
    {
        // The posting-boundary counterpart of the invariant above. Each posted row is valued independently
        // (VoucherInventoryLine.Value = Rate × BilledQuantity, snapped to the paisa), so N rows round N times
        // while the unsplit line rounds once. When the two disagree the split cannot be posted without either
        // over-billing the customer or breaking the accounts↔inventory pairing invariant — so it is REFUSED,
        // with nothing written, rather than silently re-priced.
        var k = NewKit("Cannot Foot Co");
        var date = k.C.FinancialYearStart.AddDays(200);
        SeedTwoBatchesOnHand(k, date);

        var entry = OpenItemInvoice(k, VoucherBaseType.Sales, date);
        SelectParty(entry, k.CustomerId);
        FillLine(entry, k.BatchItem, k.Main, 3m, "19.75");

        var live = SplitAcrossTheTwoSeededBatches(k, entry, "1.5", "1.5");
        Assert.False(live.Accept());
        Assert.NotNull(live.Message);
        Assert.Contains("59.26", live.Message!, StringComparison.Ordinal);   // what the rows would have valued
        Assert.Contains("59.25", live.Message!, StringComparison.Ordinal);   // what the line is actually worth
        Assert.Contains("re-attribute", live.Message!, StringComparison.OrdinalIgnoreCase);

        // NOTHING reached storage — no voucher, and the batches are untouched.
        var c = Reload(k.CompanyName);
        Assert.DoesNotContain(c.Vouchers, x => x.InventoryLines.Count > 0);
        Assert.Equal(30m, OnHand(c, k.BatchItem.Id, k.Main.Id, "IB-2312", date));
        Assert.Equal(40m, OnHand(c, k.BatchItem.Id, k.Main.Id, "IB-2401", date));
    }

    [Fact]
    public void A_fractional_split_that_foots_exactly_posts_the_unsplit_total_to_the_paisa()
    {
        // The permissive half of the contract: a FRACTIONAL split at an ODD-PAISA rate posts normally when the
        // rows foot — 1.25 × ₹19.72 = ₹24.65 exactly, twice, = ₹49.30 = 2.5 × ₹19.72. Displayed total, posted
        // item value and the PARTY ledger amount are all the unsplit figure.
        var k = NewKit("Fractional Foots Co");
        var date = k.C.FinancialYearStart.AddDays(200);
        SeedTwoBatchesOnHand(k, date);

        var entry = OpenItemInvoice(k, VoucherBaseType.Sales, date);
        SelectParty(entry, k.CustomerId);
        FillLine(entry, k.BatchItem, k.Main, 2.5m, "19.72");
        Assert.Equal("49.30", entry.ItemsTotalText);

        var live = SplitAcrossTheTwoSeededBatches(k, entry, "1.25", "1.25");
        Assert.Equal("49.30", live.ItemsTotalText);
        Assert.Equal(49.30m, live.InventoryLines[0].LineValue.Amount);
        Assert.True(live.Accept(), live.Message);

        var c = Reload(k.CompanyName);
        var v = c.Vouchers.Single(x => x.InventoryLines.Count > 0);
        Assert.Equal(2, v.InventoryLines.Count);
        Assert.Equal(2.5m, v.InventoryLines.Sum(l => l.Quantity));
        Assert.Equal(49.30m, v.InventoryLines.Sum(l => l.Value.Amount));
        // The party is billed the unsplit figure — the money the split re-attributed, not a paisa more.
        Assert.Equal(49.30m, v.Lines.Where(l => l.Side == DrCr.Debit).Sum(l => l.Amount.Amount));
        Assert.Equal(28.75m, OnHand(c, k.BatchItem.Id, k.Main.Id, "IB-2312", date));  // 30 − 1.25
        Assert.Equal(38.75m, OnHand(c, k.BatchItem.Id, k.Main.Id, "IB-2401", date));  // 40 − 1.25
    }

    [Fact]
    public void Short_billing_a_batch_split_line_values_it_on_the_BILLED_quantity()
    {
        // RQ-23: Billed is what the customer pays for and what GST/TCS is computed on. A split allocates the
        // ACTUAL quantity across lots; it must not drag the line's VALUE onto the Actual basis.
        var k = NewKit("Short Billed Split Co");
        var date = k.C.FinancialYearStart.AddDays(200);
        SeedTwoBatchesOnHand(k, date);

        var entry = OpenItemInvoice(k, VoucherBaseType.Sales, date);
        entry.UseSeparateActualBilledQuantity = true;
        SelectParty(entry, k.CustomerId);
        FillLine(entry, k.BatchItem, k.Main, 30m, "19.75");

        var live = SplitAcrossTheTwoSeededBatches(k, entry, "18", "12");
        var line = live.InventoryLines[0];
        Assert.True(line.HasBatchSplit);

        // 30 Strips leave the shelves across two lots, but only 5 are billed: 5 × ₹19.75 = ₹98.75 (odd paisa).
        line.BilledQuantityText = "5";
        Assert.Equal(5m, line.ParsedBilledQuantity);
        Assert.Equal(30m, line.ParsedActualQuantity);
        Assert.Equal(98.75m, line.LineValue.Amount);      // NOT ₹592.50, the Actual basis
        Assert.Equal("98.75", live.ItemsTotalText);
    }

    [Fact]
    public void A_split_line_with_a_different_billed_quantity_is_refused_at_the_posting_boundary()
    {
        // The deliberate block (our batch grid captures ONE quantity per lot, so there is no defensible way to
        // decide WHICH lot was short-billed). Reaching Accept is what proves the boundary guard exists.
        var k = NewKit("Split Plus Short Bill Co");
        var date = k.C.FinancialYearStart.AddDays(200);
        SeedTwoBatchesOnHand(k, date);

        var entry = OpenItemInvoice(k, VoucherBaseType.Sales, date);
        entry.UseSeparateActualBilledQuantity = true;
        SelectParty(entry, k.CustomerId);
        FillLine(entry, k.BatchItem, k.Main, 30m, "19.75");

        var live = SplitAcrossTheTwoSeededBatches(k, entry, "18", "12");
        live.InventoryLines[0].BilledQuantityText = "5";

        Assert.False(live.Accept());
        Assert.NotNull(live.Message);
        Assert.Contains("Billed", live.Message!, StringComparison.Ordinal);
        Assert.Contains("separate lines", live.Message!, StringComparison.OrdinalIgnoreCase);

        var c = Reload(k.CompanyName);
        Assert.DoesNotContain(c.Vouchers, x => x.InventoryLines.Count > 0);
        Assert.Equal(30m, OnHand(c, k.BatchItem.Id, k.Main.Id, "IB-2312", date));
        Assert.Equal(40m, OnHand(c, k.BatchItem.Id, k.Main.Id, "IB-2401", date));
    }

    [Fact]
    public void A_split_whose_quantities_no_longer_add_up_is_refused_at_the_posting_boundary()
    {
        // The C-29 re-check at the boundary. The VM drops a stale split on every edit it can see, so this drives
        // the SAME public API the sub-screen commits through (SetBatchAllocations) to plant one it cannot.
        //
        // The discrepancy is deliberately SUB-MICRO — 18 + 12.000001 against a line of 30. ₹19.75 × 0.000001 is
        // ₹0.00001975, far below the paisa, so the VALUE check cannot see it (both sides foot to ₹592.50): only
        // the quantity re-check stands between a 30.000001-Strip posting and the 30 on screen.
        var k = NewKit("Stale At Boundary Co");
        var date = k.C.FinancialYearStart.AddDays(200);
        SeedTwoBatchesOnHand(k, date);

        var entry = OpenItemInvoice(k, VoucherBaseType.Sales, date);
        SelectParty(entry, k.CustomerId);
        var line = FillLine(entry, k.BatchItem, k.Main, 30m, "19.75");

        line.SetBatchAllocations(new[]
        {
            new BatchAllocation("IB-2312", 18m, null),
            new BatchAllocation("IB-2401", 12.000001m, null),
        });
        Assert.True(line.HasBatchSplit);
        Assert.Equal(30m, line.ParsedActualQuantity);

        Assert.False(entry.Accept());
        Assert.NotNull(entry.Message);
        Assert.Contains("30.000001", entry.Message!, StringComparison.Ordinal);
        Assert.Contains("re-balance", entry.Message!, StringComparison.OrdinalIgnoreCase);

        var c = Reload(k.CompanyName);
        Assert.DoesNotContain(c.Vouchers, x => x.InventoryLines.Count > 0);
        Assert.Equal(30m, OnHand(c, k.BatchItem.Id, k.Main.Id, "IB-2312", date));
        Assert.Equal(40m, OnHand(c, k.BatchItem.Id, k.Main.Id, "IB-2401", date));
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true); }
        catch { /* best-effort cleanup */ }
    }
}
