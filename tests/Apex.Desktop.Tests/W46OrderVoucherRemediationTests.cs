using System;
using System.Globalization;
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
/// 🔴 <b>W46 REMEDIATION OF THE WITHHELD ORDER-VOUCHERS BRANCH — the register's finding list is `T2-128`.</b>
/// These are the findings that were PROVED in the W45 adversarial review and left open, each now pinned on an
/// OBSERVABLE artefact rather than on an internal this screen just set.
///
/// <list type="bullet">
///   <item><b>(v) MEDIUM, pre-existing on <c>main</c> and amplified by the Tracking No. seed</b> — a typed
///   Voucher No. on a MANUAL-numbering inventory type was discarded and the voucher posted as <c>0</c>. The
///   review's probe measured <i>"Expected: 77 / Actual: 0"</i> and was WITHDRAWN rather than left red or
///   rewritten to assert <c>0</c>. It is reinstated here against the fix, and it reads the number off the
///   RELOADED book.</item>
///   <item><b>(vi) LOW</b> — the shipped doc comments described a protection mechanism that does not exist. 🔴
///   The review's replacement account (<i>"the <c>IsAltering</c> guard is the SOLE protection"</i>) was measured
///   in W46 and <b>also does not hold</b>; what the book leans on is the per-line operator-set flag's early
///   return. Both corrections are recorded on <c>SeedTrackingNumberDefaults</c>, and the invariant is asserted
///   here on the tracking string read back off the reloaded book after <c>AcceptAlteration</c>.</item>
///   <item><b>(viii) LOW</b> — the seed writes through a setter whose hook re-enters <c>Recalculate</c>, which
///   calls the seed again. Nothing stated the invariant that makes that terminate; it is stated and pinned
///   here.</item>
/// </list>
///
/// <para>Deliberately its own scaffolding, as the review file is: a shared kit is how a control and the thing it
/// controls for end up sharing a bug.</para>
/// </summary>
public sealed class W46OrderVoucherRemediationTests : IDisposable
{
    private readonly string _tempDir;
    private readonly CompanyStorage _storage;

    public W46OrderVoucherRemediationTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "ApexW46Rem_" + Guid.NewGuid().ToString("N"));
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

    private (MainWindowViewModel Vm, Guid ItemId, Guid GodownId) Seed(string name)
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
        c.UseTrackingNumbers = true;
        _storage.Save(c);
        return (vm, item.Id, c.MainLocation!.Id);
    }

    private static void Fill(InventoryVoucherEntryViewModel e, InventoryVoucherLineViewModel l,
                             Guid itemId, Guid godownId, decimal qty)
    {
        l.SelectedItem = e.StockItems.Single(i => i.Id == itemId);
        l.SelectedGodown = e.Godowns.Single(g => g.Id == godownId);
        l.QuantityText = qty.ToString(CultureInfo.InvariantCulture);
    }

    private Company Reload(string companyName) =>
        _storage.Load(_storage.ListCompanies().Single(x => x.Name == companyName));

    private void SetNumbering(MainWindowViewModel vm, VoucherBaseType baseType, NumberingMethod method)
    {
        var c = vm.Company!;
        c.VoucherTypes.Single(t => t.BaseType == baseType).Numbering = method;
        _storage.Save(c);
    }

    // ------------------------------------------------------------------ T2-128 (v): the typed number is posted

    /// <summary>
    /// 🔴 <b>THE WITHDRAWN PROBE, REINSTATED AGAINST THE FIX.</b> On a MANUAL-numbering Receipt Note the operator
    /// types 77; the book must hold 77, and the Tracking No. the seed stamped must equal that same voucher's
    /// rendered number read off the SAME reloaded book — not the screen's pre-post preview.
    ///
    /// <para><b>Why both halves matter.</b> Before the fix the first assertion measured <c>0</c>, and the second
    /// one PASSED anyway: the seed had stamped "77" on the line, so the allocation carried a reference to a
    /// number the book never held. Purchase / Sales Bills Pending net <c>Received − Billed</c> on that string,
    /// so a silent 0 is not a cosmetic number — it is a pending figure that can never be cleared.</para>
    ///
    /// <para><b>Mutation:</b> restore <c>number: 0</c> in <c>InventoryVoucherEntryViewModel.Accept</c> and this
    /// reddens on both base kinds with <i>"Expected: 77 / Actual: 0"</i>.</para>
    /// </summary>
    [Theory]
    [InlineData(VoucherBaseType.ReceiptNote)]
    [InlineData(VoucherBaseType.DeliveryNote)]
    public void Manual_numbering_posts_the_typed_voucher_number(VoucherBaseType baseType)
    {
        var name = $"W46 Manual {baseType} Co";
        var (vm, itemId, godownId) = Seed(name);
        SetNumbering(vm, baseType, NumberingMethod.Manual);

        vm.OpenInventoryVoucher(baseType);
        var e = vm.InventoryVoucherEntry!;
        Assert.True(e.IsVoucherNumberEditable,
            "A Manual-numbering type must let the operator key the number, or this test is not measuring "
            + "the path the defect was on.");

        e.VoucherNumber = 77;
        Fill(e, e.Lines[0], itemId, godownId, 3m);
        Assert.True(e.Accept(), e.Message);

        var reloaded = Reload(name);
        var type = reloaded.VoucherTypes.Single(t => t.BaseType == baseType);
        var posted = reloaded.InventoryVouchers.Single(v => v.TypeId == type.Id);

        Assert.Equal(77, posted.Number);

        // And the seeded reference now points at a number the book actually holds.
        Assert.Equal(reloaded.FormatVoucherNumber(posted), posted.Allocations[0].TrackingNumber);
    }

    /// <summary>
    /// The same on an <b>Automatic (Manual Override)</b> type, which is the method the override is FOR: the
    /// engine suggests, the operator types over it, and the book holds what they typed.
    /// </summary>
    [Fact]
    public void Automatic_manual_override_posts_the_number_the_operator_typed_over_the_suggestion()
    {
        const string name = "W46 Override Co";
        var (vm, itemId, godownId) = Seed(name);
        SetNumbering(vm, VoucherBaseType.ReceiptNote, NumberingMethod.AutomaticManualOverride);

        vm.OpenInventoryVoucher(VoucherBaseType.ReceiptNote);
        var e = vm.InventoryVoucherEntry!;
        Assert.True(e.IsVoucherNumberEditable);
        Assert.Equal(1, e.VoucherNumber);          // the engine's suggestion, on an empty book

        e.VoucherNumber = 4201;                    // typed over it
        Fill(e, e.Lines[0], itemId, godownId, 2m);
        Assert.True(e.Accept(), e.Message);

        var reloaded = Reload(name);
        Assert.Equal(4201, reloaded.InventoryVouchers.Single().Number);
    }

    /// <summary>
    /// 🔴 <b>THE CONTROL THAT STOPS THE FIX FROM PINNING THE WRONG ANSWER, AND IT IS WRITTEN ON THE ONE SHAPE
    /// THAT CAN TELL THE TWO APART.</b> Under plain <b>Automatic</b> numbering the box is read-only, so the
    /// screen must hand the engine nothing and let <c>InventoryPostingService.Post</c> assign at POST time —
    /// never freeze the preview the screen opened on.
    ///
    /// <para><b>Two screens posted one after another cannot show this</b>, because each fresh screen's
    /// constructor reads <c>NextNumber</c> and its preview already agrees with the engine. Measured: a mutation
    /// passing <c>VoucherNumber</c> unconditionally left that version of this test GREEN. So the discriminating
    /// shape is TWO SCREENS OPEN AT ONCE over the same company — screen A's preview goes stale the moment
    /// screen B posts — and A must still come out as 2.</para>
    ///
    /// <para><b>Mutation:</b> pass <c>number: VoucherNumber</c> unconditionally and this reddens with two
    /// vouchers numbered 1.</para>
    /// </summary>
    [Fact]
    public void Automatic_numbering_is_assigned_at_post_time_not_from_a_stale_preview()
    {
        const string name = "W46 Automatic Co";
        var (vm, itemId, godownId) = Seed(name);
        SetNumbering(vm, VoucherBaseType.ReceiptNote, NumberingMethod.Automatic);

        var company = vm.Company!;
        var type = company.VoucherTypes.Single(t => t.BaseType == VoucherBaseType.ReceiptNote);

        var screenA = new InventoryVoucherEntryViewModel(
            company, type, _storage, onSaved: () => { }, onCancelled: () => { });
        var screenB = new InventoryVoucherEntryViewModel(
            company, type, _storage, onSaved: () => { }, onCancelled: () => { });

        Assert.False(screenA.IsVoucherNumberEditable);
        Assert.Equal(1, screenA.VoucherNumber);     // both previews say 1 on an empty book
        Assert.Equal(1, screenB.VoucherNumber);

        Fill(screenB, screenB.Lines[0], itemId, godownId, 1m);
        Assert.True(screenB.Accept(), screenB.Message);     // B takes 1; A's preview is now stale

        Fill(screenA, screenA.Lines[0], itemId, godownId, 1m);
        Assert.True(screenA.Accept(), screenA.Message);

        var reloaded = Reload(name);
        Assert.Equal(new[] { 1, 2 }, reloaded.InventoryVouchers.Select(v => v.Number).OrderBy(n => n).ToArray());
    }

    /// <summary>
    /// A <b>None</b>-numbering type stays unnumbered, and the box it shows is read-only. Pinned so the fix cannot
    /// be read as "always post whatever is in the box".
    /// </summary>
    [Fact]
    public void A_None_numbering_type_still_posts_unnumbered()
    {
        const string name = "W46 None Co";
        var (vm, itemId, godownId) = Seed(name);
        SetNumbering(vm, VoucherBaseType.ReceiptNote, NumberingMethod.None);

        vm.OpenInventoryVoucher(VoucherBaseType.ReceiptNote);
        var e = vm.InventoryVoucherEntry!;
        Assert.False(e.IsVoucherNumberEditable);

        Fill(e, e.Lines[0], itemId, godownId, 1m);
        Assert.True(e.Accept(), e.Message);

        Assert.Equal(0, Reload(name).InventoryVouchers.Single().Number);
    }

    /// <summary>
    /// 🔴 <b>THE FIX ALSO BRINGS A DEAD GUARD BACK TO LIFE, AND THAT IS WORTH ITS OWN TEST — "a dead guard" is a
    /// defect class this project has shipped before.</b> <c>InventoryPostingService.Post</c> enforces
    /// <b>Prevent Duplicates</b> by rendering the number and comparing it to every other voucher of the type, and
    /// <c>VoucherNumberFormatter.Render</c> returns <c>""</c> for any <c>number &lt;= 0</c>.
    ///
    /// <para><b>So while the screen posted every Manual voucher as 0, the rendered string was always empty, the
    /// guard's <c>rendered.Length &gt; 0</c> test was never true, and Prevent Duplicates on a Manual inventory
    /// type could not refuse anything.</b> An operator could key the same Receipt Note number twice and both
    /// would post. With the typed number passed, the second one is refused by name.</para>
    ///
    /// <para><b>Mutation:</b> restore <c>number: 0</c> and this reddens — the duplicate is accepted and the book
    /// holds two Receipt Notes keyed 77.</para>
    /// </summary>
    [Fact]
    public void Prevent_duplicates_now_refuses_a_repeated_manual_number_instead_of_silently_accepting_it()
    {
        const string name = "W46 Manual Dup Co";
        var (vm, itemId, godownId) = Seed(name);

        var company = vm.Company!;
        var type = company.VoucherTypes.Single(t => t.BaseType == VoucherBaseType.ReceiptNote);
        type.Numbering = NumberingMethod.Manual;
        type.PreventDuplicate = true;
        _storage.Save(company);

        vm.OpenInventoryVoucher(VoucherBaseType.ReceiptNote);
        var first = vm.InventoryVoucherEntry!;
        first.VoucherNumber = 77;
        Fill(first, first.Lines[0], itemId, godownId, 5m);
        Assert.True(first.Accept(), first.Message);

        vm.OpenInventoryVoucher(VoucherBaseType.ReceiptNote);
        var second = vm.InventoryVoucherEntry!;
        second.VoucherNumber = 77;                     // the same number, keyed again
        Fill(second, second.Lines[0], itemId, godownId, 6m);

        Assert.False(second.Accept(),
            "Prevent Duplicates accepted a repeated Manual number, so the guard is dead on this path.");
        Assert.Contains("already exists", second.Message ?? string.Empty);

        Assert.Single(Reload(name).InventoryVouchers);
    }

    // ------------------------------------------------- T2-128 (vi): what actually protects an altered book

    /// <summary>
    /// 🔴 <b>PINNED ON THE BOOK RATHER THAN ON AN INTERNAL FLAG.</b> A Receipt Note posted with the Tracking No.
    /// deliberately CLEARED is untracked, and that is how an operator marks it so. Re-open it, change only the
    /// quantity, save the alteration — and the reloaded book must STILL hold no tracking number. Purchase Bills
    /// Pending nets <c>Received − Billed</c> on that string, so a number invented anywhere along this route is a
    /// pending figure that can never be cleared.
    ///
    /// <para>The W45 review's own test for this asserted on <c>TrackingNumberIsOperatorSet</c> — an internal the
    /// screen had just set. This one asserts on the artefact: the allocation's tracking string read off the
    /// RELOADED company after <c>AcceptAlteration</c>.</para>
    ///
    /// <para>🔴 <b>MUTATION, AND IT IS NOT THE ONE THE REVIEW PREDICTED.</b> Deleting
    /// <c>if (TrackingNumberIsOperatorSet) return;</c> from
    /// <c>InventoryVoucherLineViewModel.SeedTrackingNumberDefault</c> reddens this test (the cleared box refills
    /// itself and the voucher posts tracked), along with eight others. <b>Deleting
    /// <c>if (IsAltering) return;</c> from <c>SeedTrackingNumberDefaults</c> does NOT redden it</b> — measured —
    /// because that removal also lets <c>AddLine</c>'s <c>Recalculate</c> seed each rehydrated row before the
    /// posted value overwrites it, which makes the overwrite a real change and sets the flag. Written down here
    /// because the review recorded the opposite, and a mutation claim nobody re-ran is how a dead guard ships.
    /// </para>
    /// </summary>
    [Fact]
    public void Altering_a_voucher_posted_untracked_leaves_the_book_untracked()
    {
        const string name = "W46 Alter Untracked Co";
        var (vm, itemId, godownId) = Seed(name);

        vm.OpenInventoryVoucher(VoucherBaseType.ReceiptNote);
        var entry = vm.InventoryVoucherEntry!;
        Fill(entry, entry.Lines[0], itemId, godownId, 20m);
        entry.Lines[0].TrackingNumber = string.Empty;        // the operator clears it: this receipt is untracked
        Assert.True(entry.Accept(), entry.Message);

        var company = vm.Company!;
        var postedId = company.InventoryVouchers.Single().Id;
        Assert.Null(company.InventoryVouchers.Single().Allocations[0].TrackingNumber);

        var open = InventoryVoucherEntryViewModel.ForAlter(
            company, postedId, _storage, onSaved: () => { }, onCancelled: () => { });
        Assert.False(open.IsRefused, open.Refusal);

        var alter = open.Entry!;
        Assert.True(alter.IsAltering);
        alter.Lines[0].QuantityText = "25";                  // the only thing the operator changes
        Assert.True(alter.AcceptAlteration(), alter.Message);

        var reloaded = Reload(name);
        var allocation = reloaded.InventoryVouchers.Single().Allocations[0];
        Assert.Equal(25m, allocation.Quantity);
        Assert.Null(allocation.TrackingNumber);
    }

    /// <summary>
    /// The other side of the same guard, so it is not read as "alterations never touch tracking numbers": a line
    /// the book posted WITH a tracking number keeps exactly that string across an amendment. This half is
    /// protected by the per-line flag (the rehydrate writes a real value, which fires the hook), and it is the
    /// half the corrected comment says the flag does protect.
    /// </summary>
    [Fact]
    public void Altering_a_tracked_voucher_keeps_the_tracking_number_the_book_holds()
    {
        const string name = "W46 Alter Tracked Co";
        var (vm, itemId, godownId) = Seed(name);

        vm.OpenInventoryVoucher(VoucherBaseType.ReceiptNote);
        var entry = vm.InventoryVoucherEntry!;
        Fill(entry, entry.Lines[0], itemId, godownId, 20m);
        entry.Lines[0].TrackingNumber = "GRN/OPERATOR/5";
        Assert.True(entry.Accept(), entry.Message);

        var company = vm.Company!;
        var postedId = company.InventoryVouchers.Single().Id;

        var open = InventoryVoucherEntryViewModel.ForAlter(
            company, postedId, _storage, onSaved: () => { }, onCancelled: () => { });
        Assert.False(open.IsRefused, open.Refusal);

        var alter = open.Entry!;
        Assert.Equal("GRN/OPERATOR/5", alter.Lines[0].TrackingNumber);
        alter.Lines[0].QuantityText = "30";
        Assert.True(alter.AcceptAlteration(), alter.Message);

        var allocation = Reload(name).InventoryVouchers.Single().Allocations[0];
        Assert.Equal(30m, allocation.Quantity);
        Assert.Equal("GRN/OPERATOR/5", allocation.TrackingNumber);
    }

    // ------------------------------------------------------ T2-128 (viii): the re-entrant seed's stated invariant

    /// <summary>
    /// 🔴 <b>THE INVARIANT NOTHING STATED.</b> <c>SeedTrackingNumberDefault</c> writes through the ordinary
    /// setter, whose hook calls the parent's <c>Recalculate</c>, which calls the seed again — so one keystroke on
    /// an N-line note runs nested seeds. It terminates on the ordinal-equality early return (a line already
    /// carrying the value is not written, so no hook fires), and the depth is bounded by the number of lines not
    /// yet carrying it.
    ///
    /// <para>Asserted as a REACHABILITY property rather than a depth count, because depth is not observable and a
    /// count would pin an implementation detail: after one date keystroke on a 40-line note, EVERY unwritten line
    /// carries the new default and the one the operator keyed is untouched. A seed that failed to terminate would
    /// not return at all, and one that stopped early would leave later lines on the old number.</para>
    /// </summary>
    [Fact]
    public void The_re_entrant_seed_terminates_and_reaches_every_unwritten_line()
    {
        const string name = "W46 Many Lines Co";
        var (vm, _, _) = Seed(name);
        SetNumbering(vm, VoucherBaseType.ReceiptNote, NumberingMethod.Manual);

        vm.OpenInventoryVoucher(VoucherBaseType.ReceiptNote);
        var e = vm.InventoryVoucherEntry!;

        for (var i = 0; i < 39; i++) e.AddLine();
        Assert.Equal(40, e.Lines.Count);

        e.Lines[7].TrackingNumber = "KEYED/BY/HAND";          // one line the operator owns

        e.VoucherNumber = 512;                                 // one keystroke, 39 unwritten lines to reach

        Assert.Equal("512", e.TrackingNumberDefault);
        Assert.Equal("KEYED/BY/HAND", e.Lines[7].TrackingNumber);
        Assert.All(e.Lines.Where((_, i) => i != 7), l => Assert.Equal("512", l.TrackingNumber));
    }
}
