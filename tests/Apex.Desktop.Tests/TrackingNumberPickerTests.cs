using System;
using System.IO;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Services;
using Apex.Desktop.Services;
using Apex.Desktop.ViewModels;
using Apex.Desktop.Views;
using Xunit;
using DomainLedger = Apex.Ledger.Domain.Ledger;

namespace Apex.Desktop.Tests;

/// <summary>
/// 🔴 <b>Census 9.8 / rows 4.13 / 4.14 (clause T1-8) — THE BILL-SIDE TRACKING-NUMBER SELECTION.</b>
///
/// <para><b>The vendor behaviour, opened BY CONTENT 2026-10-23.</b>
/// <c>help.tallysolutions.com/purchase-order-tally/</c>: <i>"You can select the tracking number of the receipt
/// note to track the received goods in the purchase invoice."</i> and <i>"Select New Number for the Tracking
/// No."</i> · <c>help.tallysolutions.com/sales-order-tally/</c>: <i>"You can select the tracking number of the
/// delivery note to track the delivery of goods in the sales invoice."</i></para>
///
/// <para>🔴 <b>THE CENSUS CALLS THIS A "List of Tracking Numbers" PICKER AND THAT PHRASE IS NOT THE
/// VENDOR'S.</b> Both pages were searched for it and neither carries it; the vendor names <i>"List of
/// Orders"</i> for the ORDER picker and never names the tracking list. The phrase is the census's own wording
/// for the gap, so no caption in this product uses it — see <c>docs/invented-vs-cloned.md</c> IV-149. That is
/// recorded here because a test file asserting a caption is exactly where a non-vendor phrase would become
/// load-bearing by accident.</para>
///
/// <para><b>What these tests assert.</b> The last two read the OBSERVABLE ARTEFACT — the tracking string on
/// the <see cref="VoucherInventoryLine"/> of the bill read back off the RELOADED book, and the realised
/// <see cref="ComboBox"/> in a laid-out <see cref="MainWindow"/>. Asserting only
/// <c>line.TrackingNumberChoices</c> would pass on a build where the picker is never bound to anything, which
/// is this project's measured failure mode: two fields once shipped with 29 occurrences and zero
/// assignments.</para>
/// </summary>
public sealed class TrackingNumberPickerTests : IDisposable
{
    private static readonly DateOnly FyStart = new(2025, 4, 1);

    private readonly string _tempDir;
    private readonly CompanyStorage _storage;

    public TrackingNumberPickerTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "ApexTrkPicker_" + Guid.NewGuid().ToString("N"));
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
    }

    private Kit NewKit(string companyName, bool useTrackingNumbers = true)
    {
        var vm = new MainWindowViewModel(_storage);
        vm.NewCompanyName = companyName;
        vm.CreateCompany();

        var c = vm.Company!;
        c.FinancialYearStart = FyStart;
        c.BooksBeginFrom = FyStart;

        var masters = new InventoryService(c);
        var grp = masters.CreateStockGroup("Goods");
        var nos = masters.CreateSimpleUnit("Nos", "Numbers");
        var item = masters.CreateStockItem("Widget", grp.Id, nos.Id);
        masters.AddOpeningBalance(item.Id, c.MainLocation!.Id, 500m, Money.FromRupees(100m));
        c.UseTrackingNumbers = useTrackingNumbers;

        AddLedger(c, "Purchases", "Purchase Accounts");
        var supplier = AddLedger(c, "Acme Supplies", "Sundry Creditors");
        _storage.Save(c);

        return new Kit
        {
            Vm = vm,
            CompanyName = companyName,
            ItemId = item.Id,
            MainGodownId = c.MainLocation!.Id,
            SupplierId = supplier.Id,
        };
    }

    private static DomainLedger AddLedger(Company c, string name, string groupName)
    {
        var ledger = new DomainLedger(
            Guid.NewGuid(), name, c.FindGroupByName(groupName)!.Id, Money.Zero, openingIsDebit: false);
        c.AddLedger(ledger);
        return ledger;
    }

    private Company Reload(string companyName) =>
        _storage.Load(_storage.ListCompanies().Single(e => e.Name == companyName));

    /// <summary>Posts a Receipt Note through the real entry screen and returns the Tracking No. it carried —
    /// the vendor default, which is the voucher's own number.</summary>
    private static string PostReceiptNote(Kit k, decimal qty)
    {
        k.Vm.OpenInventoryVoucher(VoucherBaseType.ReceiptNote);
        var entry = k.Vm.InventoryVoucherEntry!;
        var tracking = entry.Lines[0].TrackingNumber;

        Assert.False(string.IsNullOrWhiteSpace(tracking),
            "The Receipt Note posted without a tracking number, so there is nothing for the bill to select "
            + "and this test can no longer measure the picker.");

        var line = entry.Lines[0];
        line.SelectedItem = entry.StockItems.Single(i => i.Id == k.ItemId);
        line.SelectedGodown = entry.Godowns.Single(g => g.Id == k.MainGodownId);
        line.QuantityText = qty.ToString(System.Globalization.CultureInfo.InvariantCulture);
        line.RateText = "100.00";
        Assert.True(entry.Accept());
        return tracking;
    }

    /// <summary>Opens a Purchase voucher in item-invoice mode with the supplier selected.</summary>
    private static VoucherEntryViewModel OpenPurchaseInvoice(Kit k)
    {
        k.Vm.OpenVoucher(VoucherBaseType.Purchase);
        var entry = k.Vm.VoucherEntry!;
        k.Vm.ToggleItemInvoice();
        Assert.True(entry.CanBeItemInvoice);
        entry.SelectedParty = entry.Parties.Single(p => p.Ledger?.Id == k.SupplierId);
        return entry;
    }

    public void Dispose()
    {
        try
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    // ---------------------------------------------------------------- the selection

    /// <summary>
    /// 🔴 <b>THE CENTRAL ASSERTION, AND IT ENDS ON THE RELOADED BOOK.</b> A Receipt Note is posted, a Purchase
    /// invoice is opened, the note is SELECTED from the picker, and the number the operator never typed is on
    /// the bill's inventory line after a full save-and-reload.
    /// </summary>
    [Fact]
    public void Selecting_a_receipt_note_puts_its_number_on_the_reloaded_bill()
    {
        var k = NewKit("Trk Pick Co");
        var tracking = PostReceiptNote(k, 40m);

        var entry = OpenPurchaseInvoice(k);
        var line = entry.InventoryLines[0];

        var choice = Assert.Single(line.TrackingNumberChoices, c => c.Number == tracking);
        Assert.Contains(tracking, choice.Label);

        line.SelectedTrackingNumberChoice = choice;
        Assert.Equal(tracking, line.TrackingNumber);
        Assert.True(line.TrackingNumberIsOperatorSet,
            "A pick was not recorded as an operator edit, so a later recalculation could overwrite it.");

        line.SelectedItem = entry.StockItems.Single(i => i.Id == k.ItemId);
        line.SelectedGodown = entry.Godowns.Single(g => g.Id == k.MainGodownId);
        line.QuantityText = "40";
        line.RateText = "100.00";
        Assert.True(entry.Accept());

        var reloaded = Reload(k.CompanyName);
        var purchaseType = reloaded.VoucherTypes.Single(t => t.BaseType == VoucherBaseType.Purchase);
        var bill = reloaded.Vouchers.Single(v => v.TypeId == purchaseType.Id);
        Assert.Equal(tracking, Assert.Single(bill.InventoryLines).TrackingNumber);
    }

    /// <summary>
    /// 🔴 <b>THE CLOSING OF THE LOOP, AND THE REASON THE PICKER IS DERIVED FROM THE REPORT.</b> Once that bill
    /// is posted in full, a NEW Purchase invoice must not offer the number again — offering it would invite
    /// the operator to bill one consignment twice.
    /// </summary>
    [Fact]
    public void A_number_already_billed_in_full_is_not_offered_to_the_next_invoice()
    {
        var k = NewKit("Trk Settled Co");
        var tracking = PostReceiptNote(k, 40m);

        var first = OpenPurchaseInvoice(k);
        var line = first.InventoryLines[0];
        line.SelectedTrackingNumberChoice =
            Assert.Single(line.TrackingNumberChoices, c => c.Number == tracking);
        line.SelectedItem = first.StockItems.Single(i => i.Id == k.ItemId);
        line.SelectedGodown = first.Godowns.Single(g => g.Id == k.MainGodownId);
        line.QuantityText = "40";
        line.RateText = "100.00";
        Assert.True(first.Accept());

        var second = OpenPurchaseInvoice(k);
        Assert.DoesNotContain(second.InventoryLines[0].TrackingNumberChoices, c => c.Number == tracking);
        Assert.False(second.InventoryLines[0].ShowTrackingNumberPicker,
            "Nothing is open, so the picker should be hidden rather than offering New Number alone.");
    }

    /// <summary>
    /// The vendor's <b>New Number</b> entry is always the last option and clears the box for the operator to
    /// key one of their own — <i>"Select New Number for the Tracking No."</i>
    /// </summary>
    [Fact]
    public void New_Number_is_offered_last_and_clears_the_box()
    {
        var k = NewKit("Trk New Number Co");
        var tracking = PostReceiptNote(k, 40m);

        var entry = OpenPurchaseInvoice(k);
        var line = entry.InventoryLines[0];

        Assert.Equal("New Number", line.TrackingNumberChoices[^1].Label);
        Assert.True(line.TrackingNumberChoices[^1].IsNewNumber);

        line.SelectedTrackingNumberChoice =
            Assert.Single(line.TrackingNumberChoices, c => c.Number == tracking);
        Assert.Equal(tracking, line.TrackingNumber);

        line.SelectedTrackingNumberChoice = line.TrackingNumberChoices[^1];
        Assert.Equal(string.Empty, line.TrackingNumber);
    }

    /// <summary>Typing a number by hand selects the matching entry; typing over it deselects — the picker and
    /// the box beside it never disagree about what the line holds.</summary>
    [Fact]
    public void Typing_a_number_by_hand_keeps_the_picker_in_step()
    {
        var k = NewKit("Trk Typed Co");
        var tracking = PostReceiptNote(k, 40m);

        var entry = OpenPurchaseInvoice(k);
        var line = entry.InventoryLines[0];

        line.TrackingNumber = tracking;
        Assert.Equal(tracking, line.SelectedTrackingNumberChoice?.Number);

        line.TrackingNumber = "SOMETHING/ELSE";
        Assert.Null(line.SelectedTrackingNumberChoice);
    }

    /// <summary>With the F11 feature off nothing is offered and the picker is hidden (ER-13).</summary>
    [Fact]
    public void Nothing_is_offered_when_tracking_numbers_are_switched_off()
    {
        var k = NewKit("Trk Picker Off Co", useTrackingNumbers: false);

        var entry = OpenPurchaseInvoice(k);
        Assert.False(entry.ShowItemTrackingNumber);
        Assert.Empty(entry.InventoryLines[0].TrackingNumberChoices);
        Assert.False(entry.InventoryLines[0].ShowTrackingNumberPicker);
    }

    // ---------------------------------------------------------------- the drill

    /// <summary>
    /// 🔴 <b>PRESSING <c>Enter</c> ON A PENDING ROW ACTUALLY RAISES A DRILL, AND THIS IS THE ASSERTION THAT
    /// WOULD HAVE CAUGHT A DEAD FIELD.</b> <c>ReportsViewModel.Drill</c> is a switch with <b>no default</b>, so
    /// a kind that carries drill ids but has no case falls straight out and the keystroke does nothing — the
    /// exact trap its own comment records biting the four job-work registers, where the ids were already set
    /// and <c>Enter</c> was a silent no-op anyway. Asserting <c>row.DrillInventoryVoucherId != Guid.Empty</c>
    /// would pass on that build. This asserts the EVENT.
    /// </summary>
    [Fact]
    public void Enter_on_a_pending_row_drills_to_the_note_that_received_the_goods()
    {
        var k = NewKit("Trk Drill Co");
        PostReceiptNote(k, 40m);

        k.Vm.OpenReport(ReportKind.PurchaseBillsPending);
        var report = k.Vm.Reports!;

        var row = Assert.Single(report.Rows, r => !r.IsHeader && !r.IsTotal
                                                  && r.DrillInventoryVoucherId != Guid.Empty);

        Guid? drilled = null;
        report.DrillToInventoryVoucherRequested += id => drilled = id;
        report.Drill(row);

        Assert.NotNull(drilled);
        Assert.Equal(row.DrillInventoryVoucherId, drilled!.Value);

        // And the id it handed over really is a Receipt Note in the saved book, not just a non-empty Guid.
        var reloaded = Reload(k.CompanyName);
        var noteType = reloaded.VoucherTypes.Single(t => t.BaseType == VoucherBaseType.ReceiptNote);
        Assert.Equal(noteType.Id, reloaded.InventoryVouchers.Single(v => v.Id == drilled.Value).TypeId);
    }

    /// <summary>
    /// 🔴 <b>REACHABILITY, NOT PRESENCE — THE CONTROL IS REALISED AND VISIBLE IN A LAID-OUT WINDOW.</b> Every
    /// assertion above runs against the view model, and a view model property bound to nothing would satisfy
    /// all of them. This one lays out the real <see cref="MainWindow"/> and finds the ComboBox whose items are
    /// the choices.
    /// </summary>
    [AvaloniaFact]
    public void The_picker_is_a_realised_visible_control_on_the_item_invoice()
    {
        var k = NewKit("Trk Realised Co");
        var tracking = PostReceiptNote(k, 40m);

        var entry = OpenPurchaseInvoice(k);
        Assert.True(entry.InventoryLines[0].ShowTrackingNumberPicker);

        var window = new MainWindow { DataContext = k.Vm, Width = 1440, Height = 900 };
        window.Show();
        window.UpdateLayout();

        var picker = window.GetVisualDescendants().OfType<ComboBox>().FirstOrDefault(
            cb => cb.ItemsSource is System.Collections.IEnumerable items
                  && items.OfType<TrackingNumberChoice>().Any(c => c.Number == tracking));

        Assert.NotNull(picker);
        Assert.True(picker!.IsVisible,
            "The tracking-number picker is in the tree but collapsed, so no operator can reach it.");
        Assert.True(picker.Bounds.Width > 0 && picker.Bounds.Height > 0,
            "The tracking-number picker laid out with zero area, so it is on screen only in principle.");
    }
}
