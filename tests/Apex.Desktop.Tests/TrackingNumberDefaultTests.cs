using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Services;
using Apex.Desktop.Services;
using Apex.Desktop.ViewModels;
using Apex.Desktop.Views;
using Xunit;

namespace Apex.Desktop.Tests;

/// <summary>
/// 🔴 <b>W44 · census 9.8, the gap named on rows 4.13 / 4.14 — THE TRACKING NO. BOX OPENED BLANK.</b>
///
/// <para><b>The vendor behaviour, quoted BY CONTENT from two TallyPrime pages that say it in the same words.</b>
/// On the Delivery Note step of the sales-order flow and the Receipt Note step of the purchase-order flow:
/// <c>"Enter a Tracking No. By default, the invoice number appears. You can change it if required by creating a
/// New Number."</c> — <c>help.tallysolutions.com/sales-order-tally/</c> and
/// <c>help.tallysolutions.com/purchase-order-tally/</c>.</para>
///
/// <para><b>What was wrong.</b> <c>InventoryVoucherLineViewModel.TrackingNumber</c> initialised to
/// <see cref="string.Empty"/> and nothing ever wrote it, while its own doc comment asserted the vendor default.
/// So every Receipt Note and Delivery Note posted UNTRACKED unless the operator typed a reference by hand — and
/// Purchase / Sales Bills Pending net <c>Received − Billed</c> on that string, so the common case reconciled
/// against nothing.</para>
///
/// <para><b>What these tests assert, and why they are shaped this way.</b> Every one of them reads the
/// OBSERVABLE ARTEFACT — <see cref="InventoryAllocation.TrackingNumber"/> on the voucher read back off the book,
/// and after a reload — rather than the view-model string that was just assigned. Asserting
/// <c>line.TrackingNumber</c> alone is the test that passes on a build where the value never reaches the posted
/// allocation, which is the defect class this project has shipped green three times.</para>
///
/// <para>🔴 <b>The two tests that matter most are the NEGATIVE ones.</b>
/// <see cref="A_rejection_note_is_NOT_seeded_because_no_TallyPrime_page_attests_a_default"/> pins a deliberate
/// divergence, and <see cref="Altering_a_voucher_posted_with_a_blank_tracking_number_does_not_invent_one"/>
/// pins the guard that stops the default rewriting a posted book.</para>
/// </summary>
public sealed class TrackingNumberDefaultTests : IDisposable
{
    private readonly string _tempDir;
    private readonly CompanyStorage _storage;

    public TrackingNumberDefaultTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "ApexTrkDefault_" + Guid.NewGuid().ToString("N"));
        _storage = new CompanyStorage(_tempDir);
    }

    // ---------------------------------------------------------------- scaffolding

    private sealed class Kit
    {
        public required MainWindowViewModel Vm { get; init; }
        public required string CompanyName { get; init; }
        public required Guid ItemId { get; init; }
        public required Guid MainGodownId { get; init; }
    }

    /// <summary>A seeded company with one "Widget" (Nos), 100 units opening, and F11 tracking ON by default —
    /// the configuration census row 9.8 describes.</summary>
    private Kit NewKit(string companyName, bool useTrackingNumbers = true)
    {
        var vm = new MainWindowViewModel(_storage);
        vm.NewCompanyName = companyName;
        vm.CreateCompany();

        var c = vm.Company!;
        var masters = new InventoryService(c);
        var grp = masters.CreateStockGroup("Goods");
        var nos = masters.CreateSimpleUnit("Nos", "Numbers");
        var item = masters.CreateStockItem("Widget", grp.Id, nos.Id);
        masters.AddOpeningBalance(item.Id, c.MainLocation!.Id, 100m, Money.FromRupees(100m));
        c.UseTrackingNumbers = useTrackingNumbers;
        _storage.Save(c);

        return new Kit
        {
            Vm = vm,
            CompanyName = companyName,
            ItemId = item.Id,
            MainGodownId = c.MainLocation!.Id,
        };
    }

    private Company Reload(string companyName) =>
        _storage.Load(_storage.ListCompanies().Single(e => e.Name == companyName));

    private static void FillLine(InventoryVoucherEntryViewModel entry, Kit k, decimal qty, string? rate = "100.00")
    {
        var line = entry.Lines[0];
        line.SelectedItem = entry.StockItems.Single(i => i.Id == k.ItemId);
        line.SelectedGodown = entry.Godowns.Single(g => g.Id == k.MainGodownId);
        line.QuantityText = qty.ToString(System.Globalization.CultureInfo.InvariantCulture);
        if (rate is not null) line.RateText = rate;
    }

    /// <summary>The tracking number the book holds for the single posted voucher of <paramref name="baseType"/>,
    /// read back after a full reload so persistence is part of the assertion.</summary>
    private string? PostedTrackingAfterReload(Kit k, VoucherBaseType baseType)
    {
        var reloaded = Reload(k.CompanyName);
        var type = reloaded.VoucherTypes.Single(t => t.BaseType == baseType);
        var voucher = reloaded.InventoryVouchers.Single(v => v.TypeId == type.Id);
        return voucher.Allocations[0].TrackingNumber;
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

    // ---------------------------------------------------------------- the vendor default

    /// <summary>
    /// 🔴 <b>THE CENTRAL ASSERTION.</b> An operator who never touches the Tracking No. box on a Receipt Note or
    /// a Delivery Note posts the voucher's OWN number as its tracking reference — <c>"By default, the invoice
    /// number appears"</c> — and it survives a reload.
    /// </summary>
    [Theory]
    [InlineData(VoucherBaseType.ReceiptNote)]
    [InlineData(VoucherBaseType.DeliveryNote)]
    public void An_untouched_Tracking_No_box_posts_the_vouchers_own_number(VoucherBaseType baseType)
    {
        var k = NewKit($"Trk Default {baseType} Co");

        k.Vm.OpenInventoryVoucher(baseType);
        var entry = k.Vm.InventoryVoucherEntry!;
        var expected = entry.FormattedVoucherNumber;

        Assert.False(string.IsNullOrWhiteSpace(expected),
            "The screen has no rendered voucher number, so this test could not tell a default from a blank.");
        Assert.Equal(expected, entry.TrackingNumberDefault);

        // The box the operator is looking at already holds it, BEFORE they key anything.
        Assert.Equal(expected, entry.Lines[0].TrackingNumber);
        Assert.False(entry.Lines[0].TrackingNumberIsOperatorSet,
            "The seeded default was recorded as an operator edit, so a later re-seed would never correct it.");

        FillLine(entry, k, 40m);
        Assert.True(entry.Accept());

        Assert.Equal(expected, PostedTrackingAfterReload(k, baseType));
    }

    /// <summary>
    /// 🔴 <b>THE DELIBERATE DIVERGENCE, PINNED SO NOBODY "FIXES" IT.</b> The Tracking No. COLUMN also shows on
    /// Rejection In / Rejection Out, but it is NOT seeded there.
    ///
    /// <para>No vendor page attests a DEFAULT on a rejection. 🔴 <b>W45 REVIEW CORRECTION:</b> an earlier draft
    /// of this comment said only Tally.ERP 9 pages describe a rejection's tracking number. They are not the
    /// only ones — both cited TallyPrime pages do, and what they describe is a SELECTION, not a default:
    /// <c>"the same appears if you had recorded a delivery note with a tracking number. You can select the
    /// relevant tracking number"</c> (<c>help.tallysolutions.com/sales-order-tally/</c>, Rejections In), and the
    /// same of a receipt note on <c>help.tallysolutions.com/purchase-order-tally/</c> (Rejections Out). A
    /// rejection returns goods that arrived under someone else's reference, so seeding it with its own number
    /// would invent a reference that pairs with nothing, which is worse than blank. The picker those pages do
    /// attest remains an open gap on census 9.8 / T1-8.</para>
    /// </summary>
    [Theory]
    [InlineData(VoucherBaseType.RejectionIn)]
    [InlineData(VoucherBaseType.RejectionOut)]
    public void A_rejection_note_is_NOT_seeded_because_no_TallyPrime_page_attests_a_default(VoucherBaseType baseType)
    {
        var k = NewKit($"Trk Rejection {baseType} Co");

        k.Vm.OpenInventoryVoucher(baseType);
        var entry = k.Vm.InventoryVoucherEntry!;

        Assert.True(entry.ShowTrackingNumber,
            "The Tracking No. column is not even shown on a rejection, so this test is no longer measuring "
            + "the scope split it was written for.");
        Assert.Equal(string.Empty, entry.TrackingNumberDefault);
        Assert.Equal(string.Empty, entry.Lines[0].TrackingNumber);

        FillLine(entry, k, 5m);
        Assert.True(entry.Accept());

        Assert.Null(PostedTrackingAfterReload(k, baseType));
    }

    /// <summary>The F11 gate still governs: with "Use tracking numbers" OFF nothing is seeded and the posted
    /// allocation is byte-identical to a company that never heard of the feature (ER-13).</summary>
    [Fact]
    public void With_the_F11_feature_off_nothing_is_seeded()
    {
        var k = NewKit("Trk Feature Off Co", useTrackingNumbers: false);

        k.Vm.OpenInventoryVoucher(VoucherBaseType.ReceiptNote);
        var entry = k.Vm.InventoryVoucherEntry!;

        Assert.False(entry.ShowTrackingNumber);
        Assert.Equal(string.Empty, entry.TrackingNumberDefault);
        Assert.Equal(string.Empty, entry.Lines[0].TrackingNumber);

        FillLine(entry, k, 10m);
        Assert.True(entry.Accept());

        Assert.Null(PostedTrackingAfterReload(k, VoucherBaseType.ReceiptNote));
    }

    // ---------------------------------------------------------------- the operator still wins

    /// <summary><c>"You can change it if required by creating a New Number."</c> — a typed reference posts
    /// instead of the default, and a later recalculation does not reinstate the number over it.</summary>
    [Fact]
    public void A_typed_Tracking_No_beats_the_default_and_survives_a_recalculation()
    {
        var k = NewKit("Trk Typed Co");

        k.Vm.OpenInventoryVoucher(VoucherBaseType.ReceiptNote);
        var entry = k.Vm.InventoryVoucherEntry!;

        entry.Lines[0].TrackingNumber = "SUPP/INV/77";
        Assert.True(entry.Lines[0].TrackingNumberIsOperatorSet);

        // Anything that re-runs the seed must not win over the operator. Filling the line recalculates, and
        // moving the date re-renders the voucher number the default is built from.
        FillLine(entry, k, 12m);
        entry.Date = entry.Date.AddDays(1);
        Assert.Equal("SUPP/INV/77", entry.Lines[0].TrackingNumber);

        Assert.True(entry.Accept());
        Assert.Equal("SUPP/INV/77", PostedTrackingAfterReload(k, VoucherBaseType.ReceiptNote));
    }

    /// <summary>
    /// 🔴 <b>A CLEARED BOX STAYS CLEARED.</b> Emptying the Tracking No. is how an operator marks a note
    /// untracked. A seed that re-ran over it would make the control look dead — the operator deletes the text,
    /// it reappears, and nothing they do can post an untracked note.
    /// </summary>
    [Fact]
    public void A_deliberately_cleared_Tracking_No_is_not_refilled_and_posts_untracked()
    {
        var k = NewKit("Trk Cleared Co");

        k.Vm.OpenInventoryVoucher(VoucherBaseType.ReceiptNote);
        var entry = k.Vm.InventoryVoucherEntry!;
        Assert.NotEqual(string.Empty, entry.Lines[0].TrackingNumber);   // the default is there to clear

        entry.Lines[0].TrackingNumber = string.Empty;

        FillLine(entry, k, 8m);
        entry.Date = entry.Date.AddDays(1);
        entry.Lines[0].QuantityText = "9";
        Assert.Equal(string.Empty, entry.Lines[0].TrackingNumber);

        Assert.True(entry.Accept());
        Assert.Null(PostedTrackingAfterReload(k, VoucherBaseType.ReceiptNote));
    }

    /// <summary>The default follows the voucher number while the box is un-keyed, because the default IS the
    /// voucher number — a Manual-numbering type is typed over by the operator.</summary>
    [Fact]
    public void An_un_keyed_box_follows_the_voucher_number()
    {
        var k = NewKit("Trk Follows Number Co");

        k.Vm.OpenInventoryVoucher(VoucherBaseType.ReceiptNote);
        var entry = k.Vm.InventoryVoucherEntry!;
        var opened = entry.Lines[0].TrackingNumber;

        entry.VoucherNumber += 41;
        var moved = entry.Lines[0].TrackingNumber;

        Assert.NotEqual(opened, moved);
        Assert.Equal(entry.FormattedVoucherNumber, moved);

        FillLine(entry, k, 3m);
        Assert.True(entry.Accept());
        Assert.Equal(moved, PostedTrackingAfterReload(k, VoucherBaseType.ReceiptNote));
    }

    // ---------------------------------------------------------------- the alteration guard

    /// <summary>
    /// 🔴 <b>THE GUARD THAT PROTECTS A POSTED BOOK.</b> A Receipt Note posted with a BLANK tracking number is
    /// re-opened for alteration and re-saved; it must still be blank afterwards.
    ///
    /// <para>This is the failure the per-line operator-set flag CANNOT catch on its own, and it is why
    /// <c>SeedTrackingNumberDefaults</c> refuses outright while <c>IsAltering</c>. Rehydration assigns the posted
    /// value through the ordinary setter, but blank-over-blank is suppressed by the generated setter's equality
    /// check, so the flag stays false — and an unguarded seed would then write this voucher's number into a line
    /// that was billed against nothing. Purchase / Sales Bills Pending net <c>Received − Billed</c> on this very
    /// string, so that would silently change what the report says is outstanding.</para>
    /// </summary>
    [Fact]
    public void Altering_a_voucher_posted_with_a_blank_tracking_number_does_not_invent_one()
    {
        var k = NewKit("Trk Alter Blank Co");

        // ---- post one with the box deliberately cleared.
        k.Vm.OpenInventoryVoucher(VoucherBaseType.ReceiptNote);
        var entry = k.Vm.InventoryVoucherEntry!;
        entry.Lines[0].TrackingNumber = string.Empty;
        FillLine(entry, k, 20m);
        Assert.True(entry.Accept());

        var c = k.Vm.Company!;
        var posted = c.InventoryVouchers.Single();
        Assert.Null(posted.Allocations[0].TrackingNumber);

        // ---- re-open it for alteration through the real factory and re-save.
        var open = InventoryVoucherEntryViewModel.ForAlter(
            c, posted.Id, _storage, onSaved: () => { }, onCancelled: () => { });
        Assert.False(open.IsRefused, $"The alteration was refused: {open.Refusal}");

        var alter = open.Entry!;
        Assert.True(alter.IsAltering);
        Assert.Equal(string.Empty, alter.Lines[0].TrackingNumber);

        alter.Lines[0].QuantityText = "21";
        Assert.True(alter.AcceptAlteration(), $"The alteration would not save: {alter.Message}");

        // ---- the amended voucher is STILL untracked, and the quantity proves the alteration really landed.
        var amended = k.Vm.Company!.FindInventoryVoucher(posted.Id)!;
        Assert.Equal(21m, amended.Allocations[0].Quantity);
        Assert.Null(amended.Allocations[0].TrackingNumber);
        Assert.Null(PostedTrackingAfterReload(k, VoucherBaseType.ReceiptNote));
    }

    /// <summary>
    /// 🔴 <b>THE ONE INDEPENDENTLY OBSERVABLE EFFECT OF THE <c>IsAltering</c> GUARD — a line ADDED while
    /// altering is not stamped with the voucher's number. ⚠️ THIS RULE IS OURS, NOT THE VENDOR'S.</b>
    ///
    /// <para><b>Why this test exists, stated against itself.</b> The guard in
    /// <c>SeedTrackingNumberDefaults</c> is defence-in-depth, and on the EXISTING lines of an altered voucher it
    /// is currently redundant: the constructor seeds line 0 with the new-entry default before
    /// <c>RehydrateFrom</c> runs, so rehydrating a posted blank writes <see cref="string.Empty"/> OVER a
    /// non-empty string, which is a real change, which fires the hook and marks the line operator-set. Removing
    /// the guard was measured and left every other test in this file green. A guard no test can see is a defect
    /// class this project has shipped before, so the one behaviour it does own is pinned here.</para>
    ///
    /// <para><b>No TallyPrime page says what a line added during an alteration should default to</b>, so the
    /// conservative rule is ours: lines on an amended voucher keep whatever they were posted with, and a new line
    /// on that same voucher is keyed by the operator rather than silently stamped. If a vendor source later
    /// speaks, this test is the thing to change.</para>
    /// </summary>
    [Fact]
    public void A_line_added_during_an_alteration_is_not_stamped_with_the_voucher_number()
    {
        var k = NewKit("Trk Alter AddLine Co");

        k.Vm.OpenInventoryVoucher(VoucherBaseType.ReceiptNote);
        var entry = k.Vm.InventoryVoucherEntry!;
        entry.Lines[0].TrackingNumber = "GRN/FIRST/1";
        FillLine(entry, k, 20m);
        Assert.True(entry.Accept());

        var c = k.Vm.Company!;
        var posted = c.InventoryVouchers.Single();

        var open = InventoryVoucherEntryViewModel.ForAlter(
            c, posted.Id, _storage, onSaved: () => { }, onCancelled: () => { });
        Assert.False(open.IsRefused, $"The alteration was refused: {open.Refusal}");

        var alter = open.Entry!;
        Assert.True(alter.IsAltering);
        Assert.True(alter.ShowTrackingNumber,
            "The Tracking No. column is not shown on this altering screen, so the guard this test measures "
            + "could not have any effect and the test is no longer meaningful.");

        var added = alter.AddLine();
        Assert.Equal(string.Empty, added.TrackingNumber);

        // And the line the book already holds is untouched by the whole exercise.
        Assert.Equal("GRN/FIRST/1", alter.Lines[0].TrackingNumber);
    }

    /// <summary>An alteration of a voucher that DOES carry a tracking number keeps exactly that one — the seed
    /// must not overwrite a live reference either.</summary>
    [Fact]
    public void Altering_a_tracked_voucher_keeps_the_tracking_number_the_book_holds()
    {
        var k = NewKit("Trk Alter Tracked Co");

        k.Vm.OpenInventoryVoucher(VoucherBaseType.ReceiptNote);
        var entry = k.Vm.InventoryVoucherEntry!;
        entry.Lines[0].TrackingNumber = "GRN/ORIGINAL/5";
        FillLine(entry, k, 20m);
        Assert.True(entry.Accept());

        var c = k.Vm.Company!;
        var posted = c.InventoryVouchers.Single();

        var open = InventoryVoucherEntryViewModel.ForAlter(
            c, posted.Id, _storage, onSaved: () => { }, onCancelled: () => { });
        Assert.False(open.IsRefused, $"The alteration was refused: {open.Refusal}");

        var alter = open.Entry!;
        Assert.Equal("GRN/ORIGINAL/5", alter.Lines[0].TrackingNumber);

        alter.Date = alter.Date.AddDays(1);          // re-renders the number the default is built from
        alter.Lines[0].QuantityText = "22";
        Assert.True(alter.AcceptAlteration(), $"The alteration would not save: {alter.Message}");

        var amended = k.Vm.Company!.FindInventoryVoucher(posted.Id)!;
        Assert.Equal(22m, amended.Allocations[0].Quantity);
        Assert.Equal("GRN/ORIGINAL/5", amended.Allocations[0].TrackingNumber);
    }

    // ---------------------------------------------------------------- the operator can SEE it

    /// <summary>
    /// 🔴 <b>The REALISED control carries the default</b>, not merely the view model. A Receipt Note opened in a
    /// real window draws a visible Tracking No. <see cref="TextBox"/> whose text is the voucher's own number —
    /// which is what <c>"the invoice number appears"</c> means to an operator.
    /// </summary>
    [AvaloniaFact]
    public void The_Tracking_No_box_on_screen_shows_the_voucher_number()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "ApexTrkUi_" + Guid.NewGuid().ToString("N"));
        var storage = new CompanyStorage(tempDir);
        var vm = new MainWindowViewModel(storage);
        var window = new MainWindow { DataContext = vm, Width = 1280, Height = 720 };
        window.Show();
        try
        {
            vm.NewCompanyName = "Trk Ui Co";
            vm.CreateCompany();

            var c = vm.Company!;
            var masters = new InventoryService(c);
            var grp = masters.CreateStockGroup("Goods");
            var nos = masters.CreateSimpleUnit("Nos", "Numbers");
            masters.CreateStockItem("Widget", grp.Id, nos.Id);
            c.UseTrackingNumbers = true;

            vm.OpenInventoryVoucher(c.FindVoucherTypeByName("Receipt Note")!);
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();

            var expected = vm.InventoryVoucherEntry!.FormattedVoucherNumber;
            Assert.False(string.IsNullOrWhiteSpace(expected));

            var carried = Descendants(window).OfType<TextBox>().Any(t =>
                t.IsEffectivelyVisible
                && t.Bounds.Width > 0 && t.Bounds.Height > 0
                && string.Equals(t.Text, expected, StringComparison.Ordinal));

            Assert.True(carried,
                "No visible TextBox on the Receipt Note screen holds the voucher's own number, so the vendor's "
                + "Tracking No. default is not on screen even if the view model computes it.");
        }
        finally
        {
            window.Close();
            try
            {
                Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
                if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private static IEnumerable<Avalonia.Visual> Descendants(Avalonia.Visual v)
    {
        foreach (var child in v.GetVisualChildren())
        {
            yield return child;
            foreach (var g in Descendants(child)) yield return g;
        }
    }
}
