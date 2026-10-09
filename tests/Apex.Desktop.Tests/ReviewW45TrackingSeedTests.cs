using System;
using System.IO;
using System.Linq;
using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Services;
using Apex.Desktop.Services;
using Apex.Desktop.ViewModels;
using Xunit;

namespace Apex.Desktop.Tests;

/// <summary>
/// W45 ADVERSARIAL REVIEW of claude/apex-ab-order-vouchers — written by the reviewer, NOT by the builder, and
/// deliberately NOT sharing scaffolding with <c>TrackingNumberDefaultTests</c>.
///
/// <para>The hypothesis under test: <c>SeedTrackingNumberDefaults</c> writes the voucher number into
/// <c>TrackingNumber</c> on EVERY line in the grid, and <c>InventoryVoucherLineViewModel.IsBlank</c> counts a
/// non-blank <c>TrackingNumber</c> as "touched". So an extra row the operator adds and leaves untouched is no
/// longer blank, becomes a half-filled row, and the parent's Accept gate refuses the whole voucher.</para>
/// </summary>
public sealed class ReviewW45TrackingSeedTests : IDisposable
{
    private readonly string _tempDir;
    private readonly CompanyStorage _storage;

    public ReviewW45TrackingSeedTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "ApexRevW45_" + Guid.NewGuid().ToString("N"));
        _storage = new CompanyStorage(_tempDir);
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

    private (MainWindowViewModel Vm, Guid ItemId, Guid GodownId) Seed(string name, bool tracking)
    {
        var vm = new MainWindowViewModel(_storage);
        vm.NewCompanyName = name;
        vm.CreateCompany();

        var c = vm.Company!;
        var masters = new InventoryService(c);
        var grp = masters.CreateStockGroup("Goods");
        var nos = masters.CreateSimpleUnit("Nos", "Numbers");
        var item = masters.CreateStockItem("Widget", grp.Id, nos.Id);
        masters.AddOpeningBalance(item.Id, c.MainLocation!.Id, 500m, Money.FromRupees(100m));
        c.UseTrackingNumbers = tracking;
        _storage.Save(c);
        return (vm, item.Id, c.MainLocation!.Id);
    }

    private static void Fill(InventoryVoucherEntryViewModel e, InventoryVoucherLineViewModel l,
                             Guid itemId, Guid godownId, decimal qty)
    {
        l.SelectedItem = e.StockItems.Single(i => i.Id == itemId);
        l.SelectedGodown = e.Godowns.Single(g => g.Id == godownId);
        l.QuantityText = qty.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// CONTROL: with the F11 tracking feature OFF, one filled line plus one untouched extra line accepts.
    /// This is the behaviour the existing suite already pins, and it is why nothing reddened.
    /// </summary>
    [Fact]
    public void Control_feature_off_an_untouched_extra_line_does_not_block_accept()
    {
        var (vm, itemId, godownId) = Seed("Rev W45 Control Co", tracking: false);
        vm.OpenInventoryVoucher(VoucherBaseType.ReceiptNote);
        var e = vm.InventoryVoucherEntry!;

        Fill(e, e.Lines[0], itemId, godownId, 10m);
        e.AddLine();                       // the "+ Add line" button, then the operator changes their mind

        Assert.True(e.Lines[1].IsBlank);
        Assert.True(e.CanAccept);
        Assert.True(e.Accept(), e.Message);
    }

    /// <summary>
    /// 🔴 THE DEFECT. Same keystrokes, F11 tracking ON: the seed stamps the extra row's Tracking No., the row
    /// stops being blank, and the voucher cannot be accepted at all.
    /// </summary>
    [Theory]
    [InlineData(VoucherBaseType.ReceiptNote)]
    [InlineData(VoucherBaseType.DeliveryNote)]
    public void Tracking_on_an_untouched_extra_line_blocks_accept(VoucherBaseType baseType)
    {
        var (vm, itemId, godownId) = Seed($"Rev W45 Defect {baseType} Co", tracking: true);
        vm.OpenInventoryVoucher(baseType);
        var e = vm.InventoryVoucherEntry!;

        Fill(e, e.Lines[0], itemId, godownId, 10m);
        var extra = e.AddLine();

        Assert.True(extra.IsBlank,
            "The untouched extra row is not blank, so the parent treats it as half-filled: "
            + $"TrackingNumber='{extra.TrackingNumber}'");
        Assert.True(e.CanAccept, "Accept is disabled by an untouched extra row.");
        Assert.True(e.Accept(), e.Message);
    }

    /// <summary>
    /// The seeded default must equal the number the BOOK ends up holding, not the preview the screen opened on.
    /// The builder's own test compares the posted tracking number to <c>entry.FormattedVoucherNumber</c> — the
    /// pre-post preview — so it would pass on a build where the engine assigned a different number. This asserts
    /// the two sides independently: the tracking string off the reloaded book vs that same voucher's own rendered
    /// number off the same reloaded company.
    /// </summary>
    [Theory]
    [InlineData(VoucherBaseType.ReceiptNote)]
    [InlineData(VoucherBaseType.DeliveryNote)]
    public void The_seeded_default_equals_the_number_the_book_holds(VoucherBaseType baseType)
    {
        var (vm, itemId, godownId) = Seed($"Rev W45 Book {baseType} Co", tracking: true);
        vm.OpenInventoryVoucher(baseType);
        var e = vm.InventoryVoucherEntry!;

        Fill(e, e.Lines[0], itemId, godownId, 7m);
        Assert.True(e.Accept(), e.Message);

        var reloaded = _storage.Load(_storage.ListCompanies().Single(c => c.Name == $"Rev W45 Book {baseType} Co"));
        var type = reloaded.VoucherTypes.Single(t => t.BaseType == baseType);
        var posted = reloaded.InventoryVouchers.Single(v => v.TypeId == type.Id);

        Assert.Equal(reloaded.FormatVoucherNumber(posted), posted.Allocations[0].TrackingNumber);
    }

    // 🔴 W45 REVIEW FINDING F7 — PRE-EXISTING MAIN DEFECT, PROVED AND THEN WITHDRAWN FROM THE SUITE.
    //
    // A probe named Manual_numbering_discards_the_typed_number_and_the_seed_then_references_a_number_the_book_lacks
    // ran here and FAILED on this branch AND on main: "Assert.Equal() Failure: Values differ / Expected: 77 /
    // Actual: 0". InventoryVoucherEntryViewModel.Accept() hands TryBuild a hard-coded `number: 0` and never
    // passes VoucherNumber, while InventoryPostingService.Post numbers only a type whose method
    // AssignsNumberAutomatically — so on a MANUAL-numbering inventory type the operator's typed Voucher No. is
    // discarded and the voucher posts as 0. The probe's earlier assertion PASSED: the new Tracking No. seed had
    // already stamped "77" on the line, so the posted allocation would carry a reference to a number the book
    // never held, and Purchase / Sales Bills Pending net Received − Billed on that string.
    //
    // It is NOT left in the suite as a red test, and NOT rewritten to assert 0 — that would pin the wrong answer.
    // The probe source and its output are at
    // C:/Users/dkpho/OneDrive/Desktop/Apex-Review-Artifacts/w45-orders-repro-ALL-including-red-manual-numbering-probe.cs.txt
    // and w45-orders-baseline.txt. Fixing it is a numbering-track change (pass VoucherNumber when the type
    // AllowsManualNumberEntry), outside this branch's scope.

    /// <summary>
    /// The other half of the fixed predicate, pinned so nobody "simplifies" it to dropping TrackingNumber from
    /// <c>IsBlank</c> altogether. W-K1's original rule stands: a row where the OPERATOR typed only a tracking
    /// number is touched, not blank, and the parent must refuse it rather than silently discard the row.
    /// </summary>
    [Fact]
    public void A_row_with_only_an_operator_typed_tracking_number_is_still_touched()
    {
        var (vm, itemId, godownId) = Seed("Rev W45 Typed Only Co", tracking: true);
        vm.OpenInventoryVoucher(VoucherBaseType.ReceiptNote);
        var e = vm.InventoryVoucherEntry!;

        Fill(e, e.Lines[0], itemId, godownId, 10m);
        var extra = e.AddLine();
        extra.TrackingNumber = "GRN/TYPED/9";

        Assert.False(extra.IsBlank);
        Assert.False(e.CanAccept);
        Assert.False(e.Accept());
        Assert.Contains("needs a stock item", e.Message ?? string.Empty);
    }

    /// <summary>
    /// Falsifies the mechanism the shipped doc comments assert. They say the per-line operator-set flag is what
    /// protects a posted blank on alteration ("the constructor seeds line 0 before RehydrateFrom runs, so
    /// rehydrating writes Empty over a non-empty string"), and that the <c>IsAltering</c> guard is only
    /// defence-in-depth. But <c>RehydrateFrom</c> calls <c>Lines.Clear()</c> and builds FRESH lines, so the
    /// constructor's seeded line 0 is discarded and the rehydrate writes Empty over Empty — a no-op the setter
    /// swallows. The flag therefore stays FALSE and the guard is the only thing protecting that line.
    /// </summary>
    [Fact]
    public void On_alteration_the_operator_set_flag_is_false_so_the_guard_is_the_only_protection()
    {
        var (vm, itemId, godownId) = Seed("Rev W45 Alter Flag Co", tracking: true);
        vm.OpenInventoryVoucher(VoucherBaseType.ReceiptNote);
        var e = vm.InventoryVoucherEntry!;

        e.Lines[0].TrackingNumber = string.Empty;          // post it untracked
        Fill(e, e.Lines[0], itemId, godownId, 20m);
        Assert.True(e.Accept(), e.Message);

        var c = vm.Company!;
        var posted = c.InventoryVouchers.Single();
        Assert.Null(posted.Allocations[0].TrackingNumber);

        var open = InventoryVoucherEntryViewModel.ForAlter(
            c, posted.Id, _storage, onSaved: () => { }, onCancelled: () => { });
        Assert.False(open.IsRefused, open.Refusal);

        var alter = open.Entry!;
        Assert.True(alter.IsAltering);
        Assert.Equal(string.Empty, alter.Lines[0].TrackingNumber);
        Assert.False(alter.Lines[0].TrackingNumberIsOperatorSet,
            "The flag IS set, so the shipped comment's account of the mechanism holds after all.");
    }

    /// <summary>
    /// The same shape on the Stock Journal's DESTINATION grid is unaffected only because the Tracking No.
    /// column is not shown there — recorded so the scope is pinned rather than assumed.
    /// </summary>
    [Fact]
    public void Stock_journal_is_untouched_because_the_column_is_not_shown_there()
    {
        var (vm, _, _) = Seed("Rev W45 SJ Co", tracking: true);
        vm.OpenInventoryVoucher(VoucherBaseType.StockJournal);
        var e = vm.InventoryVoucherEntry!;

        Assert.False(e.ShowTrackingNumber);
        Assert.Equal(string.Empty, e.TrackingNumberDefault);
        Assert.True(e.Lines[0].IsBlank);
        Assert.True(e.DestinationLines[0].IsBlank);
    }
}
