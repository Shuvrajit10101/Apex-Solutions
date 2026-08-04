using System;
using System.Globalization;
using System.IO;
using System.Linq;
using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Reports;
using Apex.Ledger.Services;
using Apex.Desktop.Services;
using Apex.Desktop.ViewModels;
using Microsoft.Data.Sqlite;
using DomainLedger = Apex.Ledger.Domain.Ledger;

namespace Apex.Desktop.Tests;

/// <summary>
/// <b>Cross-stream interaction tests.</b> Five features (G-1 bill-wise in the invoice modes, G-2 parallel-set cost
/// allocation, G-3 backup/restore, G-4 voucher-type identity, G-5 real batch allocation on the invoice screens) were
/// built in parallel in isolated worktrees and merged together. Each was individually gated and reviewed — but no
/// stream could test its interaction with another, because until the merge those combinations did not exist.
///
/// <para><b>These tests only cover where two streams MEET.</b> Nothing here re-tests a single feature; each stream's
/// own suite already does that (<c>InvoiceBillWiseViewModelTests</c>, <c>ItemInvoiceBatchAllocationTests</c>,
/// <c>CostAllocationParallelSetViewModelTests</c>, <c>VoucherTypeNavigationIdentityTests</c>,
/// <c>BackupRestoreViewModelTests</c>).</para>
///
/// <para><b>The interaction that matters most</b> (identified at merge, previously uncovered): G-5 made the line
/// value — and therefore the GST/TCS base and the invoice total — follow <c>ParsedBilledQuantity</c> via
/// <c>Money.ForexBase</c>; G-1 made that same invoice total become <see cref="VoucherEntryViewModel.InvoicePartyTotal"/>
/// and gates Accept on <b>exact decimal equality</b> against it. There is no tolerance anywhere on that path, by
/// design — so a batch split that moved the total by a single paisa would not produce a rounding wobble, it would
/// produce a <b>refused Accept</b> on a voucher the operator has every reason to believe is correct.</para>
///
/// <para><b>Odd-paisa fixtures throughout.</b> Round figures assert nothing — a ±₹0.50 defect survived this
/// project's entire life under six round-number assertions.</para>
/// </summary>
public sealed class CrossStreamInteractionTests : IDisposable
{
    private readonly string _tempDir;
    private readonly CompanyStorage _storage;

    public CrossStreamInteractionTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "ApexCrossStream_" + Guid.NewGuid().ToString("N"));
        _storage = new CompanyStorage(_tempDir);
    }

    // ================================================================ scaffolding

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
    /// The shared fixture for the batch × bill-wise interaction: batch-wise details ON (G-5 layer 1), one
    /// batch-tracked "Ibuprofen" and one plain "Bolt", and a supplier and customer that BOTH
    /// <see cref="DomainLedger.MaintainBillByBill"/> (G-1 layer 3). Neither stream's own fixture had both.
    /// </summary>
    private Kit NewKit(string companyName)
    {
        var vm = new MainWindowViewModel(_storage);
        vm.NewCompanyName = companyName;
        vm.CreateCompany();
        Assert.Equal(Screen.Gateway, vm.CurrentScreen);

        var c = vm.Company!;
        c.MaintainBatchwiseDetails = true;

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
        var supplier = AddLedger(c, "Ritesh Mishra", "Sundry Creditors", billWise: true);
        var customer = AddLedger(c, "Ritesh Gupta", "Sundry Debtors", billWise: true);

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

    /// <summary>Two lots on hand: IB-2401 (40 Strips) and IB-2312 (30 Strips, already expired at <paramref name="asOf"/>).</summary>
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

    /// <summary>
    /// Splits the first eligible line across the two seeded lots (IB-2312 then IB-2401) through the REAL sub-screen,
    /// applies, and returns to the live invoice — i.e. exactly the keystrokes an operator makes.
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

    /// <summary>
    /// Allocates the whole line to ONE lot through the real sub-screen (<c>HasBatchSplit</c> stays false — a single
    /// allocation posts as one line carrying that batch number). Needed because a batch-tracked item whose stock
    /// exists only in lots cannot post an unallocated issue at all: the no-batch bucket is its own balance, so an
    /// unallocated 30-Strip sale is refused with "on-hand -30" even though 70 Strips sit in the godown.
    /// </summary>
    private static VoucherEntryViewModel AllocateWholeLineToOneBatch(
        Kit k, VoucherEntryViewModel entry, string batchNumber)
    {
        Assert.True(entry.RequestBatchAllocationForFirstEligibleLine());
        var sub = k.Vm.BatchAllocation!;
        // Clear EVERY field the row's IsBlank looks at — the FEFO seed fills RateText too, and a row that keeps a
        // rate but loses its batch is "touched but incomplete", which Apply refuses.
        foreach (var l in sub.Lines)
        {
            l.SelectedBatch = null;
            l.NewBatchNumber = string.Empty;
            l.QuantityText = string.Empty;
            l.RateText = string.Empty;
        }
        sub.Lines[0].SelectedBatch = sub.BatchOptions.Single(o => o.Batch?.BatchNumber == batchNumber);
        sub.Lines[0].QuantityText = entry.InventoryLines[0].ParsedActualQuantity
            .ToString(CultureInfo.InvariantCulture);
        Assert.True(sub.IsBalanced, sub.RemainingText);
        Assert.True(sub.Apply(), sub.Message);
        k.Vm.Back();
        return k.Vm.VoucherEntry!;
    }

    private Company Reload(string companyName)
    {
        var entry = _storage.ListCompanies().Single(e => e.Name == companyName);
        return _storage.Load(entry);
    }

    private static DateOnly AsOf(Company c) => c.FinancialYearStart.AddYears(1).AddDays(-1);

    private static decimal OnHand(Company c, Guid itemId, Guid godownId, string? batch, DateOnly asOf) =>
        new InventoryLedger(c).OnHand(itemId, godownId, batch, asOf);

    // ================================================================================================
    // (I-1)  G-5 batch split  ×  G-1 invoice bill-wise   — THE interaction identified at merge
    // ================================================================================================

    /// <summary>
    /// <b>The headline interaction.</b> One Sales item invoice that exercises both streams at once: a bill-by-bill
    /// customer (G-1), a batch-tracked item whose single grid line is split across two lots through the real
    /// sub-screen (G-5), and a two-reference bill-wise split that must foot to the invoice total EXACTLY.
    ///
    /// <para>ODD-PAISA fixture: 7 Strips @ ₹1,234.57 = <b>₹8,641.99</b>, split 4 / 3 across the lots
    /// (₹4,938.28 + ₹3,703.71 = ₹8,641.99 — foots to the paisa, so G-5's re-attribution guard permits it) and
    /// billed as ₹5,000.37 + ₹3,641.62.</para>
    ///
    /// <para>The claim under test is the JOIN: after the split, <see cref="VoucherEntryViewModel.InvoicePartyTotal"/>
    /// — the figure G-1 gates Accept on — is still the billed-basis invoice total to the paisa, and the operator's
    /// hand-cut bill split survives the round trip out to the batch sub-screen and back.</para>
    /// </summary>
    [Fact]
    public void Batch_split_line_and_a_two_reference_bill_split_reconcile_to_the_same_odd_paisa_total()
    {
        var k = NewKit("Batch And BillWise Co");
        var date = k.C.FinancialYearStart.AddDays(200);
        SeedTwoBatchesOnHand(k, date);

        var entry = OpenItemInvoice(k, VoucherBaseType.Sales, date);
        SelectParty(entry, k.CustomerId);
        FillLine(entry, k.BatchItem, k.Main, 7m, "1234.57");
        entry.RecalculateItemInvoice();

        // Both panels are live on ONE screen — the combination neither stream's fixture could produce.
        Assert.True(entry.ShowInvoiceBillWise);
        Assert.Equal("8,641.99", entry.ItemsTotalText);
        Assert.Equal(8641.99m, entry.InvoicePartyTotal);

        // …now split the line across two lots through the real sub-screen and come back.
        var live = SplitAcrossTheTwoSeededBatches(k, entry, "4", "3");
        Assert.True(live.InventoryLines[0].HasBatchSplit);
        Assert.Equal("Multi (2)", live.InventoryLines[0].BatchLabel);

        // THE JOIN: re-attributing the quantity across lots must not move the money by a paisa, and the bill-wise
        // target that Accept is gated on must still be that untouched total.
        Assert.Equal("8,641.99", live.ItemsTotalText);
        Assert.Equal(8641.99m, live.InvoicePartyTotal);
        Assert.True(live.ShowInvoiceBillWise);       // the trip to the sub-screen did not tear the panel down
        Assert.Single(live.InvoiceBillAllocations);  // …nor duplicate its auto-seeded row

        // A hand-cut two-reference split, odd paisa on BOTH sides: 5,000.37 + 3,641.62 = 8,641.99.
        live.InvoiceBillAllocations[0].Name = "GUPTA/8801";
        live.InvoiceBillAllocations[0].AmountText = "5000.37";
        Assert.False(live.InvoiceBillSplitOk);                  // under-allocated until the second row exists
        var second = live.AddInvoiceBillAllocation(BillRefType.NewRef);
        second.Name = "GUPTA/8802";
        second.AmountText = "3641.62";

        Assert.Equal(8641.99m, live.InvoiceBillAllocatedTotal);
        Assert.True(live.InvoiceBillSplitOk);
        Assert.True(live.Accept(), live.Message);

        var c = Reload(k.CompanyName);
        var v = c.Vouchers.Single(x => x.InventoryLines.Count > 0 && x.Lines.Count > 0);

        // G-5 side: one grid line posted as two batch-stamped rows whose Σ value is the unsplit value exactly.
        Assert.Equal(2, v.InventoryLines.Count);
        Assert.Equal(7m, v.InventoryLines.Sum(l => l.Quantity));
        Assert.Equal(4m, v.InventoryLines.Single(l => l.BatchLabel == "IB-2312").Quantity);
        Assert.Equal(3m, v.InventoryLines.Single(l => l.BatchLabel == "IB-2401").Quantity);
        Assert.Equal(8641.99m, v.InventoryLines.Sum(l => l.Value.Amount));

        // G-1 side: the derived party leg carries that same figure AND both bill references.
        var partyLine = v.Lines.Single(l => l.LedgerId == k.CustomerId);
        Assert.Equal(DrCr.Debit, partyLine.Side);
        Assert.Equal(8641.99m, partyLine.Amount.Amount);
        Assert.True(partyLine.HasBillAllocations);
        Assert.Equal(2, partyLine.BillAllocations.Count);
        Assert.Equal(8641.99m, partyLine.BillAllocations.Sum(a => a.Amount.Amount));

        // The receivable is real and split — the whole point of G-1 — on a voucher whose stock moved out of the
        // right lots, the whole point of G-5.
        var bills = Outstandings.OpenBillsFor(c, c.FindLedger(k.CustomerId)!, AsOf(c));
        Assert.Equal(2, bills.Count);
        Assert.Equal(5000.37m, bills.Single(b => b.Reference == "GUPTA/8801").Pending.Amount);
        Assert.Equal(3641.62m, bills.Single(b => b.Reference == "GUPTA/8802").Pending.Amount);

        Assert.Equal(26m, OnHand(c, k.BatchItem.Id, k.Main.Id, "IB-2312", date));   // 30 − 4
        Assert.Equal(37m, OnHand(c, k.BatchItem.Id, k.Main.Id, "IB-2401", date));   // 40 − 3
    }

    /// <summary>
    /// The refusal half of the same join. A genuinely mis-footed bill split — ONE PAISA short of the batch-split
    /// invoice total — is still refused, and nothing reaches storage: no voucher, and both lots untouched.
    /// The paisa is the point: G-1 gates on exact decimal equality, so this must fail, not round.
    /// </summary>
    [Fact]
    public void A_bill_split_one_paisa_short_of_the_batch_split_total_is_refused_and_posts_nothing()
    {
        var k = NewKit("One Paisa Short Co");
        var date = k.C.FinancialYearStart.AddDays(200);
        SeedTwoBatchesOnHand(k, date);

        var entry = OpenItemInvoice(k, VoucherBaseType.Sales, date);
        SelectParty(entry, k.CustomerId);
        FillLine(entry, k.BatchItem, k.Main, 7m, "1234.57");
        entry.RecalculateItemInvoice();

        var live = SplitAcrossTheTwoSeededBatches(k, entry, "4", "3");
        live.InvoiceBillAllocations[0].Name = "GUPTA/8801";
        live.InvoiceBillAllocations[0].AmountText = "5000.37";
        var second = live.AddInvoiceBillAllocation(BillRefType.NewRef);
        second.Name = "GUPTA/8802";
        second.AmountText = "3641.61";                      // ← one paisa short of 8,641.99

        Assert.Equal(8641.98m, live.InvoiceBillAllocatedTotal);
        Assert.False(live.InvoiceBillSplitOk);
        Assert.False(live.Accept());
        Assert.NotNull(live.Message);
        Assert.Contains("8,641.99", live.Message!, StringComparison.Ordinal);
        Assert.Contains("8,641.98", live.Message!, StringComparison.Ordinal);

        var c = Reload(k.CompanyName);
        Assert.DoesNotContain(c.Vouchers, x => x.InventoryLines.Count > 0 && x.Lines.Count > 0);
        Assert.Equal(30m, OnHand(c, k.BatchItem.Id, k.Main.Id, "IB-2312", date));
        Assert.Equal(40m, OnHand(c, k.BatchItem.Id, k.Main.Id, "IB-2401", date));
    }

    /// <summary>
    /// <b>The ordering claim.</b> When a batch split cannot foot to the paisa (G-5 refuses it), the invoice is
    /// refused by the BATCH guard even though the bill-wise split is perfectly footed against what the screen shows
    /// — the phantom paisa never reaches the party leg, and no half-posted voucher or stock movement escapes.
    ///
    /// <para>Fixture: 3 Strips @ ₹19.75 = ₹59.25 unsplit, but 1.5 + 1.5 values the two posted rows at
    /// ₹29.63 + ₹29.63 = ₹59.26. The operator's bill allocation foots to ₹59.25 — the figure on screen — so
    /// <see cref="VoucherEntryViewModel.InvoiceBillSplitOk"/> is TRUE and it is G-5's guard that must stop this.</para>
    /// </summary>
    [Fact]
    public void A_batch_split_that_cannot_foot_is_refused_before_the_phantom_paisa_reaches_the_bill_wise_party_leg()
    {
        var k = NewKit("Phantom Paisa Co");
        var date = k.C.FinancialYearStart.AddDays(200);
        SeedTwoBatchesOnHand(k, date);

        var entry = OpenItemInvoice(k, VoucherBaseType.Sales, date);
        SelectParty(entry, k.CustomerId);
        FillLine(entry, k.BatchItem, k.Main, 3m, "19.75");
        entry.RecalculateItemInvoice();
        Assert.Equal(59.25m, entry.InvoicePartyTotal);

        var live = SplitAcrossTheTwoSeededBatches(k, entry, "1.5", "1.5");

        // The bill-wise side is CLEAN — it foots to the ₹59.25 the screen shows.
        live.InvoiceBillAllocations[0].Name = "GUPTA/9001";
        Assert.Equal(59.25m, live.InvoiceBillAllocatedTotal);
        Assert.True(live.InvoiceBillSplitOk);

        // …and the voucher is still refused, by G-5's guard, naming the ₹59.26 the rows would have valued.
        Assert.False(live.Accept());
        Assert.NotNull(live.Message);
        Assert.Contains("59.26", live.Message!, StringComparison.Ordinal);
        Assert.Contains("59.25", live.Message!, StringComparison.Ordinal);

        var c = Reload(k.CompanyName);
        Assert.DoesNotContain(c.Vouchers, x => x.InventoryLines.Count > 0);
        Assert.Empty(Outstandings.OpenBillsFor(c, c.FindLedger(k.CustomerId)!, AsOf(c)));
        Assert.Equal(30m, OnHand(c, k.BatchItem.Id, k.Main.Id, "IB-2312", date));
        Assert.Equal(40m, OnHand(c, k.BatchItem.Id, k.Main.Id, "IB-2401", date));
    }

    /// <summary>
    /// <b>Short-billed × bill-wise.</b> G-5 values the line on the BILLED quantity, and G-1 gates on the resulting
    /// total. A short-billed line therefore must move the bill-wise target with it — a panel still pointing at the
    /// Actual-basis figure would refuse every legitimate short-billed invoice.
    ///
    /// <para>ODD-PAISA fixture: 30 Strips leave the shelves, only 7 are billed at ₹1,234.57 ⇒ ₹8,641.99, NOT the
    /// ₹37,037.10 Actual basis. (No batch split here: G-5 deliberately refuses split + short-bill together, since
    /// its grid captures one quantity per lot — the interaction below pins that refusal.)</para>
    /// </summary>
    [Fact]
    public void Short_billing_moves_the_bill_wise_target_onto_the_billed_basis()
    {
        var k = NewKit("Short Billed BillWise Co");
        var date = k.C.FinancialYearStart.AddDays(200);
        SeedTwoBatchesOnHand(k, date);

        var entry = OpenItemInvoice(k, VoucherBaseType.Sales, date);
        entry.UseSeparateActualBilledQuantity = true;
        SelectParty(entry, k.CustomerId);
        FillLine(entry, k.BatchItem, k.Main, 30m, "1234.57");

        // One lot, not a split — G-5 refuses split + short-bill together (pinned by the next test).
        var live = AllocateWholeLineToOneBatch(k, entry, "IB-2401");
        var line = live.InventoryLines[0];
        Assert.False(line.HasBatchSplit);
        line.BilledQuantityText = "7";
        live.RecalculateItemInvoice();

        Assert.Equal(30m, line.ParsedActualQuantity);
        Assert.Equal(7m, line.ParsedBilledQuantity);
        Assert.Equal(8641.99m, line.LineValue.Amount);

        // THE JOIN: the bill-wise panel tracks the BILLED basis, to the paisa.
        Assert.Equal(8641.99m, live.InvoicePartyTotal);
        Assert.Equal(8641.99m, live.InvoiceBillAllocations[0].ParsedAmount);

        live.InvoiceBillAllocations[0].Name = "GUPTA/8810";
        Assert.True(live.Accept(), live.Message);

        var c = Reload(k.CompanyName);
        var v = c.Vouchers.Single(x => x.InventoryLines.Count > 0 && x.Lines.Count > 0);
        var partyLine = v.Lines.Single(l => l.LedgerId == k.CustomerId);
        Assert.Equal(8641.99m, partyLine.Amount.Amount);
        Assert.Equal(8641.99m, Assert.Single(partyLine.BillAllocations).Amount.Amount);

        // 30 Strips still left the shelves — the money moved to the billed basis, the stock did not.
        Assert.Equal(30m, v.InventoryLines.Sum(l => l.Quantity));
        Assert.Equal(8641.99m, Assert.Single(Outstandings.OpenBillsFor(
            c, c.FindLedger(k.CustomerId)!, AsOf(c))).Pending.Amount);
    }

    /// <summary>
    /// The deliberate block, now proved to be reached with a bill-wise party attached: a line that is BOTH split
    /// across lots and short-billed is refused at the posting boundary (there is no defensible way to decide which
    /// lot was short-billed), and the bill-wise party leg is not written either.
    /// </summary>
    [Fact]
    public void Split_plus_short_bill_is_still_refused_when_the_party_is_bill_wise()
    {
        var k = NewKit("Split Plus Short Bill BillWise Co");
        var date = k.C.FinancialYearStart.AddDays(200);
        SeedTwoBatchesOnHand(k, date);

        var entry = OpenItemInvoice(k, VoucherBaseType.Sales, date);
        entry.UseSeparateActualBilledQuantity = true;
        SelectParty(entry, k.CustomerId);
        FillLine(entry, k.BatchItem, k.Main, 30m, "1234.57");

        var live = SplitAcrossTheTwoSeededBatches(k, entry, "18", "12");
        live.InventoryLines[0].BilledQuantityText = "7";
        live.RecalculateItemInvoice();
        live.InvoiceBillAllocations[0].Name = "GUPTA/8811";

        Assert.False(live.Accept());
        Assert.NotNull(live.Message);
        Assert.Contains("Billed", live.Message!, StringComparison.Ordinal);

        var c = Reload(k.CompanyName);
        Assert.DoesNotContain(c.Vouchers, x => x.InventoryLines.Count > 0);
        Assert.Empty(Outstandings.OpenBillsFor(c, c.FindLedger(k.CustomerId)!, AsOf(c)));
    }

    /// <summary>
    /// <b>Safety pin for the un-allocated batch line.</b> Opening the batch sub-screen is OPTIONAL — G-5 gates it
    /// behind four layers and the operator may simply never press Alt+B. When they don't, and the item's stock
    /// exists only inside lots, the sale is REFUSED and nothing is written: no voucher, no stock movement, and no
    /// receivable for the bill-wise party. That safety property is what this test locks.
    ///
    /// <para><b>Observation, deliberately NOT asserted here.</b> The refusal arrives as a NEGATIVE-STOCK diagnosis —
    /// "on-hand -30 … Negative stock is not allowed" — naming a godown that is holding 70 Strips, because the
    /// no-batch bucket is its own balance. The refusal is right; the diagnosis is misleading and would read to an
    /// operator as a stock problem rather than "pick a lot (Alt+B)". This predates the merge (an unallocated
    /// batch-item line behaved the same before G-5 wired the sub-screen), so it is reported rather than fixed, and
    /// the message text is left unasserted so improving it does not break this test.</para>
    /// </summary>
    [Fact]
    public void An_unallocated_batch_line_is_refused_outright_and_leaves_no_receivable_behind()
    {
        var k = NewKit("Unallocated Batch Line Co");
        var date = k.C.FinancialYearStart.AddDays(200);
        SeedTwoBatchesOnHand(k, date);

        var entry = OpenItemInvoice(k, VoucherBaseType.Sales, date);
        SelectParty(entry, k.CustomerId);
        FillLine(entry, k.BatchItem, k.Main, 30m, "1234.57");
        entry.RecalculateItemInvoice();

        // The bill-wise side is complete and correct — 30 × ₹1,234.57 = ₹37,037.10.
        Assert.Equal(37037.10m, entry.InvoicePartyTotal);
        entry.InvoiceBillAllocations[0].Name = "GUPTA/9100";
        Assert.True(entry.InvoiceBillSplitOk);

        // 70 Strips ARE on hand in that godown — 30 + 40 across the two lots…
        Assert.Equal(30m, OnHand(k.C, k.BatchItem.Id, k.Main.Id, "IB-2312", date));
        Assert.Equal(40m, OnHand(k.C, k.BatchItem.Id, k.Main.Id, "IB-2401", date));
        // …while the NO-BATCH bucket an unallocated line draws from is empty. That is the whole mechanism: the
        // balance is kept per lot, so an unallocated issue is measured against 0 rather than against the 70.
        Assert.Equal(0m, OnHand(k.C, k.BatchItem.Id, k.Main.Id, null, date));

        Assert.False(entry.Accept());
        Assert.NotNull(entry.Message);

        var c = Reload(k.CompanyName);
        Assert.DoesNotContain(c.Vouchers, x => x.InventoryLines.Count > 0);
        Assert.Empty(Outstandings.OpenBillsFor(c, c.FindLedger(k.CustomerId)!, AsOf(c)));
        Assert.Equal(30m, OnHand(c, k.BatchItem.Id, k.Main.Id, "IB-2312", date));
        Assert.Equal(40m, OnHand(c, k.BatchItem.Id, k.Main.Id, "IB-2401", date));
    }

    // ================================================================================================
    // (I-2)  G-1 invoice bill-wise  ×  TDS carve-out  ×  G-5 batch mode
    // ================================================================================================

    /// <summary>
    /// A TDS-enabled company that ALSO maintains batches, with a bill-by-bill §194J vendor and a batch-tracked
    /// item — the fixture neither the G-1/TDS stream nor the G-5 stream had.
    /// </summary>
    private (MainWindowViewModel Vm, Guid FeesId, Guid VendorId, StockItem BatchItem, Godown Main, string Name)
        NewTdsBatchKit(string companyName)
    {
        var vm = new MainWindowViewModel(_storage);
        vm.NewCompanyName = companyName;
        vm.CreateCompany();
        var c = vm.Company!;
        c.MaintainBatchwiseDetails = true;

        new TdsTcsService(c).EnableTds(new TdsConfig { Tan = "MUMA12345B" });

        var fees = AddLedger(c, "Professional Fees", "Indirect Expenses");
        fees.TdsApplicable = true;
        fees.TdsNatureOfPaymentId = c.FindNatureOfPaymentByCode("194J(b)")!.Id;

        var vendor = AddLedger(c, "Acme Consultants", "Sundry Creditors", billWise: true);
        vendor.DeducteeType = DeducteeType.Firm;
        vendor.PartyPan = "AAPFU0939F";

        AddLedger(c, "Purchases", "Purchase Accounts");

        var masters = new InventoryService(c);
        var grp = masters.CreateStockGroup("Medicine");
        var strip = masters.CreateSimpleUnit("Strip", "Strips");
        var med = masters.CreateStockItem("Ibuprofen", grp.Id, strip.Id);
        med.MaintainInBatches = true;

        _storage.Save(c);
        return (vm, fees.Id, vendor.Id, med, c.MainLocation!, companyName);
    }

    /// <summary>
    /// <b>The bill-wise target on a TDS-carved purchase is the NET, not the gross.</b> G-1 stamps the allocation on
    /// the derived party leg, and on the accounting-invoice path that leg carries <c>carve.NetPartyAmount</c> — so a
    /// panel still targeting the gross would refuse every §194J invoice ever entered.
    ///
    /// <para>ODD-PAISA fixture: ₹1,23,456.70 professional fees, §194J(b) @ 10% ⇒ ₹12,345.67 raw, ₹12,346 posted
    /// (nearest rupee, half-up), leaving <b>₹1,11,110.70</b> payable — split across two references
    /// ₹60,000.37 + ₹51,110.33.</para>
    /// </summary>
    [Fact]
    public void Bill_wise_on_a_TDS_carved_purchase_accounting_invoice_targets_the_net_and_splits_across_two_refs()
    {
        var k = NewTdsBatchKit("TDS BillWise Co");

        k.Vm.OpenVoucher(VoucherBaseType.Purchase);
        var e = k.Vm.VoucherEntry!;
        e.Mode = VoucherEntryMode.AccountingInvoice;
        e.SelectedParty = e.Parties.Single(p => p.Ledger?.Id == k.VendorId);
        var line = e.AccountingInvoiceLines[0];
        line.SelectedLedger = e.AccountingInvoiceLedgers.Single(l => l.Id == k.FeesId);
        line.AmountText = "123456.70";
        e.RecalculateAccountingInvoice();

        Assert.True(e.ShowTdsPanel);
        Assert.Contains("194J(b)", e.TdsSectionText);

        // THE JOIN: the bill-wise target is the CARVED NET, to the paisa — not the ₹1,23,456.70 gross.
        Assert.True(e.ShowInvoiceBillWise);
        Assert.Equal(111110.70m, e.InvoicePartyTotal);
        Assert.Equal(111110.70m, e.InvoiceBillAllocations[0].ParsedAmount);

        e.InvoiceBillAllocations[0].Name = "ACME/A";
        e.InvoiceBillAllocations[0].AmountText = "60000.37";
        var second = e.AddInvoiceBillAllocation(BillRefType.NewRef);
        second.Name = "ACME/B";
        second.AmountText = "51110.33";
        Assert.Equal(111110.70m, e.InvoiceBillAllocatedTotal);
        Assert.True(e.InvoiceBillSplitOk);
        Assert.True(e.Accept(), e.Message);

        var c = Reload(k.Name);
        var v = c.Vouchers.Single(x => x.Lines.Any(l => l.LedgerId == k.VendorId));
        var partyLine = v.Lines.Single(l => l.LedgerId == k.VendorId);
        Assert.Equal(111110.70m, partyLine.Amount.Amount);
        Assert.Equal(2, partyLine.BillAllocations.Count);
        Assert.Equal(111110.70m, partyLine.BillAllocations.Sum(a => a.Amount.Amount));

        // The payable that actually opens is the NET — the deductee is owed what is left after withholding.
        var bills = Outstandings.OpenBillsFor(c, c.FindLedger(k.VendorId)!, AsOf(c));
        Assert.Equal(2, bills.Count);
        Assert.Equal(60000.37m, bills.Single(b => b.Reference == "ACME/A").Pending.Amount);
        Assert.Equal(51110.33m, bills.Single(b => b.Reference == "ACME/B").Pending.Amount);
        Assert.Equal(111110.70m, bills.Sum(b => b.Pending.Amount));
    }

    /// <summary>
    /// <b>The mode boundary between the two streams.</b> The very same TDS-enabled, batch-enabled company entering a
    /// purchase as an ITEM invoice instead: the TDS panel is off (the carve-out lives on the accounting path), the
    /// bill-wise target is the item-invoice total rather than a net, and a batch-split line still reconciles onto it.
    ///
    /// <para>This is the "batch-split line on a TDS purchase" combination in the only shape the product actually
    /// permits — an accounting invoice has no item lines at all, so a batched item can never appear on the carved
    /// path. What must be proved is that the bill-wise target follows the MODE, not a remembered net.</para>
    ///
    /// <para>ODD-PAISA fixture: 7 Strips @ ₹1,234.57 = <b>₹8,641.99</b>, split 4 / 3 across two received lots.</para>
    /// </summary>
    [Fact]
    public void Switching_the_same_purchase_to_item_invoice_moves_the_bill_wise_target_off_the_TDS_net()
    {
        var k = NewTdsBatchKit("TDS Mode Boundary Co");

        k.Vm.OpenVoucher(VoucherBaseType.Purchase);
        var e = k.Vm.VoucherEntry!;

        // First, in accounting mode, the target IS the carved net (as above).
        e.Mode = VoucherEntryMode.AccountingInvoice;
        e.SelectedParty = e.Parties.Single(p => p.Ledger?.Id == k.VendorId);
        var acct = e.AccountingInvoiceLines[0];
        acct.SelectedLedger = e.AccountingInvoiceLedgers.Single(l => l.Id == k.FeesId);
        acct.AmountText = "123456.70";
        e.RecalculateAccountingInvoice();
        Assert.Equal(111110.70m, e.InvoicePartyTotal);

        // …now switch to ITEM invoice. A Purchase item invoice is INWARD, so the two lots are received here rather
        // than issued — no opening stock is needed and nothing is drawn from an existing balance.
        e.Mode = VoucherEntryMode.ItemInvoice;
        Assert.True(e.IsItemInvoice);
        Assert.False(e.ShowTdsPanel);                    // the carve-out does not follow the mode switch

        var lineVm = e.InventoryLines[0];
        lineVm.SelectedItem = e.StockItems.Single(i => i.Id == k.BatchItem.Id);
        lineVm.SelectedGodown = e.Godowns.Single(g => g.Id == k.Main.Id);
        lineVm.QuantityText = "7";
        lineVm.RateText = "1234.57";
        e.RecalculateItemInvoice();

        // THE JOIN: the target has moved off the ₹1,11,110.70 net onto the item-invoice total, to the paisa.
        Assert.Equal(8641.99m, e.InvoicePartyTotal);

        // Split the received quantity across two NEW lots typed inline on the sub-screen (BOOK p.131).
        Assert.True(e.RequestBatchAllocationForFirstEligibleLine());
        var sub = k.Vm.BatchAllocation!;
        foreach (var l in sub.Lines)
        {
            l.SelectedBatch = null; l.NewBatchNumber = string.Empty;
            l.QuantityText = string.Empty; l.RateText = string.Empty;
        }
        sub.Lines[0].SelectedBatch = sub.BatchOptions.Single(o => o.IsNew);
        sub.Lines[0].NewBatchNumber = "RCV-A";
        sub.Lines[0].QuantityText = "4";
        sub.Lines[1].SelectedBatch = sub.BatchOptions.Single(o => o.IsNew);
        sub.Lines[1].NewBatchNumber = "RCV-B";
        sub.Lines[1].QuantityText = "3";
        Assert.True(sub.IsBalanced, sub.RemainingText);
        Assert.True(sub.Apply(), sub.Message);
        k.Vm.Back();

        var live = k.Vm.VoucherEntry!;
        Assert.True(live.InventoryLines[0].HasBatchSplit);
        Assert.Equal(8641.99m, live.InvoicePartyTotal);
        live.InvoiceBillAllocations[0].Name = "ACME/ITEM-1";
        Assert.True(live.InvoiceBillSplitOk);
        Assert.True(live.Accept(), live.Message);

        var c = Reload(k.Name);
        var v = c.Vouchers.Single(x => x.InventoryLines.Count > 0);
        Assert.Equal(2, v.InventoryLines.Count);
        Assert.Equal(8641.99m, v.InventoryLines.Sum(l => l.Value.Amount));
        Assert.Equal(8641.99m, v.Lines.Single(l => l.LedgerId == k.VendorId).Amount.Amount);
        Assert.Equal(8641.99m, Assert.Single(Outstandings.OpenBillsFor(
            c, c.FindLedger(k.VendorId)!, AsOf(c))).Pending.Amount);
        Assert.Equal(4m, OnHand(c, k.BatchItem.Id, k.Main.Id, "RCV-A", AsOf(c)));
        Assert.Equal(3m, OnHand(c, k.BatchItem.Id, k.Main.Id, "RCV-B", AsOf(c)));
    }

    // ================================================================================================
    // (I-3)  G-4 voucher-type identity  ×  G-1 invoice modes  ×  G-5 batches
    // ================================================================================================

    /// <summary>
    /// <b>A second, non-predefined Sales series must carry the new invoice behaviour too.</b> G-4 made the Day-Book
    /// picker capture the TYPE rather than re-resolving its base kind; G-1 and G-5 hang their behaviour off the
    /// screen the type opens. Nothing proved the two meet — that an invoice entered under "Export Sales" gets
    /// bill-wise and batch allocation and POSTS UNDER THAT TYPE'S ID rather than the predefined one.
    ///
    /// <para>ODD-PAISA fixture: 7 Strips @ ₹1,234.57 = ₹8,641.99, split 4 / 3 across the two seeded lots.</para>
    /// </summary>
    [Fact]
    public void An_invoice_entered_under_a_second_Sales_type_carries_bill_wise_and_batch_and_posts_under_that_type()
    {
        var k = NewKit("Second Series Co");
        var date = k.C.FinancialYearStart.AddDays(200);
        SeedTwoBatchesOnHand(k, date);

        var export = new VoucherType(Guid.NewGuid(), "Export Sales", VoucherBaseType.Sales);
        k.C.AddVoucherType(export);
        _storage.Save(k.C);

        var predefined = k.C.VoucherTypes.Single(t => t.BaseType == VoucherBaseType.Sales && t.IsPredefined);
        Assert.NotEqual(predefined.Id, export.Id);

        // Reachability (G-4): the second series is listed by the Day-Book Add-Voucher picker and opens ITSELF.
        k.Vm.OpenReport(ReportKind.DayBook);
        k.Vm.OpenAddVoucherFromReport();
        Assert.Equal(Screen.AddVoucherPicker, k.Vm.CurrentScreen);
        var row = k.Vm.Columns[^1].Items.Single(i => i.Label == "Export Sales");
        row.Activate();
        Assert.Equal(Screen.VoucherEntry, k.Vm.CurrentScreen);

        var entry = k.Vm.VoucherEntry!;
        Assert.Equal(export.Id, entry.Type.Id);      // the row's OWN type, not the resolved predefined one

        // …and the two new invoice features are fully alive on it.
        Assert.True(entry.CanBeItemInvoice);
        k.Vm.ToggleItemInvoice();
        Assert.True(entry.IsItemInvoice);
        entry.Date = date;
        SelectParty(entry, k.CustomerId);
        FillLine(entry, k.BatchItem, k.Main, 7m, "1234.57");
        entry.RecalculateItemInvoice();
        Assert.True(entry.ShowInvoiceBillWise);
        Assert.Equal(8641.99m, entry.InvoicePartyTotal);

        var live = SplitAcrossTheTwoSeededBatches(k, entry, "4", "3");
        Assert.True(live.InventoryLines[0].HasBatchSplit);
        Assert.Equal(8641.99m, live.InvoicePartyTotal);
        live.InvoiceBillAllocations[0].Name = "EXP/0001";
        Assert.True(live.Accept(), live.Message);

        var c = Reload(k.CompanyName);
        var v = c.Vouchers.Single(x => x.InventoryLines.Count > 0);
        Assert.Equal(export.Id, v.TypeId);                       // posted under the SECOND series
        Assert.NotEqual(predefined.Id, v.TypeId);
        Assert.Equal(2, v.InventoryLines.Count);
        Assert.Equal(8641.99m, v.InventoryLines.Sum(l => l.Value.Amount));
        Assert.Equal(8641.99m, Assert.Single(v.Lines.Single(l => l.LedgerId == k.CustomerId).BillAllocations)
            .Amount.Amount);
        Assert.Equal(26m, OnHand(c, k.BatchItem.Id, k.Main.Id, "IB-2312", date));
        Assert.Equal(37m, OnHand(c, k.BatchItem.Id, k.Main.Id, "IB-2401", date));
    }

    /// <summary>
    /// The refusal half of G-4, proved to still hold once the invoice features are in play: a DEACTIVATED series is
    /// not offered by the picker, and with every Sales type deactivated the base-kind route refuses by name rather
    /// than opening some other series' screen.
    /// </summary>
    [Fact]
    public void A_deactivated_second_Sales_type_is_neither_listed_nor_openable()
    {
        var k = NewKit("Deactivated Series Co");

        var export = new VoucherType(Guid.NewGuid(), "Export Sales", VoucherBaseType.Sales);
        k.C.AddVoucherType(export);
        _storage.Save(k.C);

        // Listed while active…
        k.Vm.OpenReport(ReportKind.DayBook);
        k.Vm.OpenAddVoucherFromReport();
        Assert.Equal(Screen.AddVoucherPicker, k.Vm.CurrentScreen);
        Assert.Contains(k.Vm.Columns[^1].Items, i => i.Label == "Export Sales");
        k.Vm.Back();

        // …gone once deactivated. The predefined "Sales" row is the POSITIVE CONTROL: it proves we are still
        // looking at a populated picker, so the absence of "Export Sales" means absence and not an empty column.
        export.IsActive = false;
        _storage.Save(k.C);
        k.Vm.OpenReport(ReportKind.DayBook);
        k.Vm.OpenAddVoucherFromReport();
        Assert.Equal(Screen.AddVoucherPicker, k.Vm.CurrentScreen);
        Assert.Contains(k.Vm.Columns[^1].Items, i => i.Label == "Sales");
        Assert.DoesNotContain(k.Vm.Columns[^1].Items, i => i.Label == "Export Sales");
        k.Vm.Back();

        // With EVERY Sales series off, the base-kind route refuses by name and opens no screen at all.
        foreach (var t in k.C.VoucherTypes.Where(t => t.BaseType == VoucherBaseType.Sales)) t.IsActive = false;
        k.Vm.OpenVoucher(VoucherBaseType.Sales);
        Assert.NotEqual(Screen.VoucherEntry, k.Vm.CurrentScreen);
        Assert.Null(k.Vm.VoucherEntry);
        Assert.Equal("No active 'Sales' voucher type is configured for this company.", k.Vm.Message);
    }

    // ================================================================================================
    // (I-4)  G-2 parallel-set cost allocation  ×  G-1 bill-wise, on ONE voucher line pair
    // ================================================================================================

    /// <summary>
    /// <b>Two exact-sum invariants on one voucher, reconciling independently.</b> G-2 made cost categories PARALLEL
    /// AXES — every category must foot to the FULL line amount, not a share of it — while bill-wise splits the party
    /// line into references that must foot to that same amount once. Both are exact-decimal rules with no tolerance,
    /// and they attach to the two halves of the same voucher; nothing had ever carried both.
    ///
    /// <para>ODD-PAISA fixture: ₹5,000.37 of professional fees. <b>Branch</b> axis = Kolkata ₹5,000.37 (whole);
    /// <b>Department</b> axis = Marketing ₹3,000.11 + Sales ₹2,000.26 (also ₹5,000.37); party bill split =
    /// ₹2,500.19 + ₹2,500.18. Three different decompositions of the same paisa-exact figure.</para>
    /// </summary>
    [Fact]
    public void A_voucher_carrying_both_a_parallel_cost_set_and_a_bill_split_posts_and_both_reconcile()
    {
        var vm = new MainWindowViewModel(_storage);
        vm.NewCompanyName = "Cost And BillWise Co";
        vm.CreateCompany();
        var c = vm.Company!;

        var branch = new CostCategory(Guid.NewGuid(), "Branch");
        var department = new CostCategory(Guid.NewGuid(), "Department");
        c.AddCostCategory(branch);
        c.AddCostCategory(department);
        var kolkata = new CostCentre(Guid.NewGuid(), "Kolkata", branch.Id);
        var marketing = new CostCentre(Guid.NewGuid(), "Marketing", department.Id);
        var salesTeam = new CostCentre(Guid.NewGuid(), "Sales Team", department.Id);
        c.AddCostCentre(kolkata);
        c.AddCostCentre(marketing);
        c.AddCostCentre(salesTeam);

        var fees = AddLedger(c, "Consultancy Charges", "Indirect Expenses");
        var vendor = AddLedger(c, "Acme Consultants", "Sundry Creditors", billWise: true);
        _storage.Save(c);

        // A plain-grid (As Voucher) Purchase: Dr expense / Cr bill-wise vendor.
        vm.OpenVoucher(VoucherBaseType.Purchase);
        var e = vm.VoucherEntry!;
        Assert.True(e.IsAsVoucherMode);

        var expense = e.Lines[0];
        expense.SelectedLedger = e.Ledgers.Single(l => l.Id == fees.Id);
        expense.Side = DrCr.Debit;
        expense.AmountText = "5000.37";

        var party = e.Lines[1];
        party.SelectedLedger = e.Ledgers.Single(l => l.Id == vendor.Id);
        party.Side = DrCr.Credit;
        party.AmountText = "5000.37";

        // G-2 side — TWO parallel axes on the expense line, each footing to the WHOLE amount.
        Assert.True(expense.IsCostApplicable);
        SetCostRow(vm, expense, 0, branch, kolkata, "5000.37");
        SetCostRow(vm, expense, 1, department, marketing, "3000.11");
        SetCostRow(vm, expense, 2, department, salesTeam, "2000.26");
        Assert.Equal(5000.37m, expense.CostAllocatedTotalFor(branch.Id));
        Assert.Equal(5000.37m, expense.CostAllocatedTotalFor(department.Id));
        Assert.True(expense.CostSplitOk);

        // G-1 side — the party line split across two references footing to that same amount ONCE.
        Assert.True(party.IsBillWise);
        party.BillAllocations[0].Name = "ACME/C1";
        party.BillAllocations[0].AmountText = "2500.19";
        var secondBill = party.AddBillAllocation(BillRefType.NewRef);
        secondBill.Name = "ACME/C2";
        secondBill.AmountText = "2500.18";
        Assert.True(party.BillSplitOk);

        Assert.True(e.CanAccept);
        Assert.True(e.Accept(), e.Message);

        var reloaded = Reload("Cost And BillWise Co");
        var v = reloaded.Vouchers.Single(x => x.Lines.Any(l => l.LedgerId == vendor.Id));

        // Each invariant survives the round trip, independently.
        var postedExpense = v.Lines.Single(l => l.LedgerId == fees.Id);
        Assert.Equal(3, postedExpense.CostAllocations.Count);
        Assert.Equal(2, postedExpense.CostAllocationCategoryIds.Count);
        Assert.Equal(5000.37m, postedExpense.CostAllocationTotalFor(branch.Id).Amount);
        Assert.Equal(5000.37m, postedExpense.CostAllocationTotalFor(department.Id).Amount);
        // The cross-category Σ is DOUBLE the line — that is what "parallel axes" means, and why the
        // once-per-line grand total below must NOT be it.
        Assert.Equal(10000.74m, postedExpense.CostAllocationTotal.Amount);

        var postedParty = v.Lines.Single(l => l.LedgerId == vendor.Id);
        Assert.Equal(2, postedParty.BillAllocations.Count);
        Assert.Equal(5000.37m, postedParty.BillAllocations.Sum(a => a.Amount.Amount));

        var bills = Outstandings.OpenBillsFor(reloaded, reloaded.FindLedger(vendor.Id)!, AsOf(reloaded));
        Assert.Equal(2500.19m, bills.Single(b => b.Reference == "ACME/C1").Pending.Amount);
        Assert.Equal(2500.18m, bills.Single(b => b.Reference == "ACME/C2").Pending.Amount);

        // The cost report counts the spend ONCE despite two axes — the G-2 fix — on a voucher that is also
        // carrying a bill split, which is the combination that had never been exercised.
        var from = reloaded.FinancialYearStart;
        var summary = CostReports.BuildCategorySummary(reloaded, from, AsOf(reloaded));
        Assert.Equal(5000.37m, summary.GrandTotal.Amount);
        Assert.True(summary.CategoryTotalsOverlap);
    }

    // ================================================================================================
    // (I-5)  G-3 backup/restore  ×  every new persisted shape the other four streams introduced
    // ================================================================================================

    /// <summary>
    /// <b>The proof that the new persisted shapes actually round-trip.</b> G-3's own suite backed up and restored a
    /// company built before the other four streams existed — so nothing had ever verified that a
    /// <c>.apexbak</c> carries an invoice-mode bill allocation on a DERIVED party leg, a one-grid-line-to-two-rows
    /// batch split, a PARALLEL-SET cost allocation (two categories on one line, each footing to the full amount)
    /// or a second, non-predefined voucher type.
    ///
    /// <para>The database is destroyed between backup and restore, so every figure asserted afterwards came out of
    /// the archive and nowhere else. ODD PAISA throughout: the Sales invoice is ₹8,641.99 split 4 / 3 across two
    /// lots and billed ₹5,000.37 + ₹3,641.62; the purchase is ₹5,000.37 on two parallel cost axes.</para>
    /// </summary>
    [Fact]
    public void A_backup_round_trip_carries_bill_wise_batch_splits_parallel_cost_sets_and_a_second_voucher_type()
    {
        var k = NewKit("Round Trip Everything Co");
        var date = k.C.FinancialYearStart.AddDays(200);
        SeedTwoBatchesOnHand(k, date);

        // --- (a) a second, non-predefined Sales series -------------------------------------------------
        var export = new VoucherType(Guid.NewGuid(), "Export Sales", VoucherBaseType.Sales);
        k.C.AddVoucherType(export);

        // --- (b) cost categories + centres for the parallel set ----------------------------------------
        var branch = new CostCategory(Guid.NewGuid(), "Branch");
        var department = new CostCategory(Guid.NewGuid(), "Department");
        k.C.AddCostCategory(branch);
        k.C.AddCostCategory(department);
        var kolkata = new CostCentre(Guid.NewGuid(), "Kolkata", branch.Id);
        var marketing = new CostCentre(Guid.NewGuid(), "Marketing", department.Id);
        var salesTeam = new CostCentre(Guid.NewGuid(), "Sales Team", department.Id);
        k.C.AddCostCentre(kolkata);
        k.C.AddCostCentre(marketing);
        k.C.AddCostCentre(salesTeam);
        var fees = AddLedger(k.C, "Consultancy Charges", "Indirect Expenses");
        _storage.Save(k.C);

        // --- (c) a Sales item invoice UNDER the second series: batch split + two-reference bill split ---
        k.Vm.OpenVoucher(export, date);
        var sales = k.Vm.VoucherEntry!;
        Assert.Equal(export.Id, sales.Type.Id);
        k.Vm.ToggleItemInvoice();
        SelectParty(sales, k.CustomerId);
        FillLine(sales, k.BatchItem, k.Main, 7m, "1234.57");
        sales.RecalculateItemInvoice();
        var liveSales = SplitAcrossTheTwoSeededBatches(k, sales, "4", "3");
        liveSales.InvoiceBillAllocations[0].Name = "EXP/RT-1";
        liveSales.InvoiceBillAllocations[0].AmountText = "5000.37";
        var secondBill = liveSales.AddInvoiceBillAllocation(BillRefType.NewRef);
        secondBill.Name = "EXP/RT-2";
        secondBill.AmountText = "3641.62";
        Assert.True(liveSales.Accept(), liveSales.Message);

        // --- (d) a plain-grid purchase carrying a PARALLEL cost set + a bill split ---------------------
        k.Vm.OpenVoucher(VoucherBaseType.Purchase, date);
        var purchase = k.Vm.VoucherEntry!;
        var expense = purchase.Lines[0];
        expense.SelectedLedger = purchase.Ledgers.Single(l => l.Id == fees.Id);
        expense.Side = DrCr.Debit;
        expense.AmountText = "5000.37";
        var party = purchase.Lines[1];
        party.SelectedLedger = purchase.Ledgers.Single(l => l.Id == k.SupplierId);
        party.Side = DrCr.Credit;
        party.AmountText = "5000.37";
        SetCostRow(k.Vm, expense, 0, branch, kolkata, "5000.37");
        SetCostRow(k.Vm, expense, 1, department, marketing, "3000.11");
        SetCostRow(k.Vm, expense, 2, department, salesTeam, "2000.26");
        party.BillAllocations[0].Name = "MISHRA/RT";
        party.BillAllocations[0].AmountText = "5000.37";
        Assert.True(party.BillSplitOk);
        Assert.True(purchase.Accept(), purchase.Message);

        // ============================== BACK UP ==============================
        var outDir = Path.Combine(_tempDir, "archives");
        Directory.CreateDirectory(outDir);
        var backup = new BackupCompanyViewModel(
            k.Vm.Company!, _storage, outDir, new DateTime(2026, 8, 3, 11, 45, 0));
        Assert.True(backup.Apply(), backup.Status);
        var archive = backup.FullPath;
        Assert.True(File.Exists(archive));

        // ============================== DESTROY ==============================
        // Everything asserted after this point can only have come out of the archive.
        var dbPath = _storage.PathForName("Round Trip Everything Co");
        Assert.True(File.Exists(dbPath), $"the live database should exist at {dbPath}");
        SqliteConnection.ClearAllPools();
        File.WriteAllBytes(dbPath, System.Text.Encoding.ASCII.GetBytes(new string('X', 40_000)));
        SqliteConnection.ClearAllPools();

        // CONTROL — without this the whole test could pass on a stale read. The destroyed database must be
        // genuinely unreadable, so every figure verified below demonstrably came out of the archive.
        Assert.ThrowsAny<Exception>(() => Reload("Round Trip Everything Co"));

        // ============================== RESTORE ==============================
        var restore = new RestoreCompanyViewModel(k.Vm.Company!, _storage, onRestored: null)
        {
            FilePath = archive,
        };
        Assert.True(restore.Examine(), restore.Status);
        Assert.True(restore.CanRestore);
        restore.Confirmed = true;
        Assert.True(restore.Apply(), restore.Status);
        SqliteConnection.ClearAllPools();

        // ============================== VERIFY ===============================
        var c = Reload("Round Trip Everything Co");

        // (a) the second series survived, still non-predefined and distinct from the seeded one.
        var restoredExport = c.VoucherTypes.Single(t => t.Name == "Export Sales");
        Assert.Equal(export.Id, restoredExport.Id);
        Assert.False(restoredExport.IsPredefined);
        Assert.Equal(VoucherBaseType.Sales, restoredExport.BaseType);
        Assert.True(restoredExport.IsActive);

        // (c) the item invoice: posted under that series, split across two lots, billed across two references.
        var v = c.Vouchers.Single(x => x.InventoryLines.Count > 0);
        Assert.Equal(export.Id, v.TypeId);
        Assert.Equal(2, v.InventoryLines.Count);
        Assert.Equal(4m, v.InventoryLines.Single(l => l.BatchLabel == "IB-2312").Quantity);
        Assert.Equal(3m, v.InventoryLines.Single(l => l.BatchLabel == "IB-2401").Quantity);
        Assert.Equal(8641.99m, v.InventoryLines.Sum(l => l.Value.Amount));

        var customerLine = v.Lines.Single(l => l.LedgerId == k.CustomerId);
        Assert.Equal(8641.99m, customerLine.Amount.Amount);
        Assert.Equal(2, customerLine.BillAllocations.Count);
        Assert.Equal(5000.37m, customerLine.BillAllocations.Single(a => a.Name == "EXP/RT-1").Amount.Amount);
        Assert.Equal(3641.62m, customerLine.BillAllocations.Single(a => a.Name == "EXP/RT-2").Amount.Amount);

        // …and the stock genuinely moved out of the right lots in the restored company.
        Assert.Equal(26m, OnHand(c, k.BatchItem.Id, k.Main.Id, "IB-2312", date));
        Assert.Equal(37m, OnHand(c, k.BatchItem.Id, k.Main.Id, "IB-2401", date));

        // (d) the PARALLEL cost set: both axes still foot to the whole line, and the cross-axis Σ is still double.
        var p = c.Vouchers.Single(x => x.Lines.Any(l => l.LedgerId == fees.Id));
        var restoredExpense = p.Lines.Single(l => l.LedgerId == fees.Id);
        Assert.Equal(3, restoredExpense.CostAllocations.Count);
        Assert.Equal(2, restoredExpense.CostAllocationCategoryIds.Count);
        Assert.Equal(5000.37m, restoredExpense.CostAllocationTotalFor(branch.Id).Amount);
        Assert.Equal(5000.37m, restoredExpense.CostAllocationTotalFor(department.Id).Amount);
        Assert.Equal(10000.74m, restoredExpense.CostAllocationTotal.Amount);
        Assert.Equal(2000.26m, restoredExpense.CostAllocations
            .Single(a => a.CentreId == salesTeam.Id).Amount.Amount);

        // …and the report built over the restored data still counts the spend ONCE.
        var summary = CostReports.BuildCategorySummary(c, c.FinancialYearStart, AsOf(c));
        Assert.Equal(5000.37m, summary.GrandTotal.Amount);
        Assert.True(summary.CategoryTotalsOverlap);

        // Both receivable and payable survive as real, settleable bills.
        var receivables = Outstandings.OpenBillsFor(c, c.FindLedger(k.CustomerId)!, AsOf(c));
        Assert.Equal(2, receivables.Count);
        Assert.Equal(8641.99m, receivables.Sum(b => b.Pending.Amount));
        Assert.Equal(5000.37m, Assert.Single(Outstandings.OpenBillsFor(
            c, c.FindLedger(k.SupplierId)!, AsOf(c))).Pending.Amount);
    }

    private static void SetCostRow(
        MainWindowViewModel vm, VoucherLineViewModel line, int index,
        CostCategory category, CostCentre centre, string amount)
    {
        while (line.CostAllocations.Count <= index) vm.AddCostAllocation(line);
        var row = line.CostAllocations[index];
        row.SelectedCategory = row.Categories.Single(x => x.Id == category.Id);
        row.SelectedCentre = row.Centres.Single(x => x.Id == centre.Id);
        row.AmountText = amount;
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true); }
        catch { /* best-effort cleanup */ }
    }
}
