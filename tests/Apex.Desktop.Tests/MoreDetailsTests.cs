using System;
using System.IO;
using System.Linq;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
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
/// <b>CENSUS 14.4 — MORE DETAILS (Ctrl+I).</b>
///
/// <para><b>Fidelity (Ruling 14 tier 1).</b> help.tallysolutions.com/tally-prime/keyboard-shortcuts-tally/
/// lists <c>Ctrl+I</c> in the Right button area: "To add more details to a master or voucher for the current
/// instance". The banking pages give the defining half verbatim: "press <b>Ctrl+I</b> (More Details) to enter
/// any of the values <b>without activating the options in F12 (Configure)</b>."</para>
///
/// <para><b>Why this file drives the REAL window.</b> This row has already been burned once by a green suite:
/// the whole suite passed at 2880 tests while <c>MoreDetailsViewModel.cs</c> DID NOT EXIST, because nothing
/// referenced it. A test that calls <c>vm.OpenMoreDetails()</c> directly would pass on a build where the chord
/// reaches nothing and the panel is unreachable, so the reachability tests here press the actual keystroke
/// through <see cref="MainWindow"/>'s real handler.</para>
///
/// <para><b>The chord itself is settled by user ruling 17 (2026-09-06)</b>, which closed the long-open U-6:
/// Ctrl+I is More Details, and the item-invoice toggle re-homes to Ctrl+H with no Ctrl+I alias. The re-homing's
/// own locks live in <c>ShellChordTableTests</c> and <c>ServiceAccountingInvoiceKeyboardTests</c>.</para>
/// </summary>
public sealed class MoreDetailsTests : IDisposable
{
    private readonly string _tempDir;
    private readonly CompanyStorage _storage;

    public MoreDetailsTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "ApexMoreDetails_" + Guid.NewGuid().ToString("N"));
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

    // ---------------------------------------------------------------- scaffolding

    private sealed class Kit
    {
        public required MainWindowViewModel Vm { get; init; }
        public required Guid ItemId { get; init; }
        public required Guid GodownId { get; init; }
        public required Guid CustomerId { get; init; }
        public required Guid PlainCustomerId { get; init; }
    }

    /// <summary>
    /// A company with batch-wise details ON, a batched "Widget", a BILL-BY-BILL customer (the party that makes
    /// the bill-wise row appear) and a NON-bill-by-bill customer as the ER-13 control.
    /// </summary>
    private Kit NewKit(string companyName)
    {
        var vm = new MainWindowViewModel(_storage);
        vm.NewCompanyName = companyName;
        vm.CreateCompany();
        var c = vm.Company!;
        c.MaintainBatchwiseDetails = true;

        var masters = new InventoryService(c);
        var grp = masters.CreateStockGroup("Goods");
        var nos = masters.CreateSimpleUnit("Nos", "Numbers");
        var item = masters.CreateStockItem("Widget", grp.Id, nos.Id);
        item.MaintainInBatches = true;
        masters.AddOpeningBalance(item.Id, c.MainLocation!.Id, 500m, Money.FromRupees(100m));

        AddLedger(c, "Sales", "Sales Accounts");
        var customer = AddLedger(c, "Beta Buyers", "Sundry Debtors", billWise: true);
        var plain = AddLedger(c, "Gamma Cash Buyers", "Sundry Debtors", billWise: false);
        _storage.Save(c);

        return new Kit
        {
            Vm = vm,
            ItemId = item.Id,
            GodownId = c.MainLocation!.Id,
            CustomerId = customer.Id,
            PlainCustomerId = plain.Id,
        };
    }

    private static DomainLedger AddLedger(Company c, string name, string groupName, bool billWise = false)
    {
        var group = c.FindGroupByName(groupName)!;
        var ledger = new DomainLedger(Guid.NewGuid(), name, group.Id, Money.Zero, openingIsDebit: false)
        {
            MaintainBillByBill = billWise,
        };
        c.AddLedger(ledger);
        return ledger;
    }

    /// <summary>A Sales ITEM INVOICE with the given party selected and one priced line — the surface where
    /// this build actually has optional field groups to reveal.</summary>
    private static VoucherEntryViewModel OpenSalesInvoice(Kit k, Guid partyId)
    {
        k.Vm.OpenVoucher(VoucherBaseType.Sales);
        var entry = k.Vm.VoucherEntry!;
        entry.ToggleItemInvoice();
        entry.SelectedParty = entry.Parties.Single(p => p.Ledger?.Id == partyId);
        var line = entry.InventoryLines[0];
        line.SelectedItem = entry.StockItems.Single(i => i.Id == k.ItemId);
        line.SelectedGodown = entry.Godowns.Single(g => g.Id == k.GodownId);
        line.QuantityText = "3";
        line.RateText = "1234.57";        // odd paisa: round figures assert nothing on this project
        entry.RecalculateItemInvoice();
        return entry;
    }

    // ============================================================ (1) the defining vendor behaviour

    /// <summary>
    /// 🔴 <b>THE TEST THAT MUST NEVER BE DELETED.</b> The vendor's More Details reaches an option-gated field
    /// "without activating the options in F12 (Configure)". So a reveal must do BOTH halves:
    /// the field appears, AND the screen option comes out byte-identical.
    ///
    /// <para>Both assertions are load-bearing and neither alone is enough. Without the first, a panel that
    /// silently did nothing would pass; without the second, the whole feature could be implemented as
    /// <c>UseDefaultBillWiseAllocation = false</c> — which shows the same field and is exactly what the vendor
    /// says More Details is NOT.</para>
    /// </summary>
    [Fact]
    public void Revealing_a_field_shows_it_without_touching_the_screen_option()
    {
        var k = NewKit("More Details Defining Co");
        var entry = OpenSalesInvoice(k, k.CustomerId);

        // Precondition: the option is ON (the shipped default), so the field is hidden.
        Assert.True(entry.UseDefaultBillWiseAllocation);
        Assert.True(entry.InvoiceBillWiseApplies);
        Assert.False(entry.ShowInvoiceBillWise);

        var panel = new MoreDetailsViewModel(entry);
        var row = Assert.Single(panel.Rows, r => r.Label == "Bill-wise Details");
        Assert.False(row.IsRevealed);

        Assert.True(panel.Activate());

        // Half one — the field is now on this voucher.
        Assert.True(entry.ShowInvoiceBillWise);
        Assert.True(row.IsRevealed);

        // 🔴 Half two — and the option the operator set is UNCHANGED. This is the whole feature.
        Assert.True(entry.UseDefaultBillWiseAllocation,
            "More Details flipped the screen option. The vendor's Ctrl+I reaches the field 'without "
            + "activating the options in F12 (Configure)' — writing the knob is the one thing it must not do.");
    }

    /// <summary>
    /// The batch row, same contract on the other knob — and it proves the panel is not hard-wired to one
    /// field group. <c>UseBatchWiseDetails</c> ships ON, so the row exists only once the operator turns it off.
    /// </summary>
    [Fact]
    public void The_batch_row_reveals_the_sub_screen_without_touching_its_knob()
    {
        var k = NewKit("More Details Batch Co");
        var entry = OpenSalesInvoice(k, k.PlainCustomerId);
        var line = entry.InventoryLines[0];

        Assert.True(entry.CanUseBatchWiseDetails);
        Assert.True(entry.LineWantsBatchAllocation(line));   // shipped ON

        entry.UseBatchWiseDetails = false;                    // the operator hides it on this screen
        Assert.False(entry.LineWantsBatchAllocation(line));

        var panel = new MoreDetailsViewModel(entry);
        var row = Assert.Single(panel.Rows, r => r.Label == "Batch / Lot Details");
        Assert.True(panel.Activate());

        Assert.True(row.IsRevealed);
        Assert.True(entry.LineWantsBatchAllocation(line));
        Assert.False(entry.UseBatchWiseDetails,
            "More Details turned the batch knob back on instead of overriding it for this voucher.");
    }

    // ============================================================ (2) which rows are offered

    /// <summary>
    /// The panel offers a row only where the field is BOTH applicable and hidden. A voucher whose optional
    /// field is already showing has nothing to reveal, and saying so beats offering a no-op row.
    /// </summary>
    [Fact]
    public void A_field_already_on_the_voucher_is_not_offered()
    {
        var k = NewKit("More Details Already Shown Co");
        var entry = OpenSalesInvoice(k, k.CustomerId);

        entry.UseDefaultBillWiseAllocation = false;   // the operator already revealed it the ordinary way
        Assert.True(entry.ShowInvoiceBillWise);

        var panel = new MoreDetailsViewModel(entry);
        Assert.DoesNotContain(panel.Rows, r => r.Label == "Bill-wise Details");
    }

    /// <summary>
    /// The ER-13 control: a party that does not maintain bill-by-bill has no bill-wise allocation at all, so
    /// the row must not be offered — More Details reveals an OPTION-gated field, it never overrides the
    /// MASTER's own gate. Without this, the panel would offer to show a screen that cannot apply.
    /// </summary>
    [Fact]
    public void A_field_the_master_gate_excludes_is_not_offered()
    {
        var k = NewKit("More Details Master Gate Co");
        var entry = OpenSalesInvoice(k, k.PlainCustomerId);

        Assert.False(entry.InvoiceBillWiseApplies);
        Assert.True(entry.UseDefaultBillWiseAllocation);

        var panel = new MoreDetailsViewModel(entry);
        Assert.DoesNotContain(panel.Rows, r => r.Label == "Bill-wise Details");

        // And forcing the override anyway must still not show it — the master gate outranks More Details.
        entry.MoreDetailsBillWiseRequested = true;
        Assert.False(entry.ShowInvoiceBillWise,
            "More Details overrode the party master's MaintainBillByBill. It may only reach an OPTION-gated "
            + "field; layer 3 is not its to override.");
    }

    /// <summary>
    /// An empty panel is a correct outcome, and it says so rather than presenting a blank column. This is the
    /// design decision recorded at <c>MainWindowViewModel.CanOpenMoreDetails</c>: an honest empty answer beats
    /// a silently dead keystroke.
    /// </summary>
    [Fact]
    public void A_voucher_with_nothing_to_reveal_says_so()
    {
        var k = NewKit("More Details Empty Co");
        k.Vm.OpenVoucher(VoucherBaseType.Journal);
        var entry = k.Vm.VoucherEntry!;

        var panel = new MoreDetailsViewModel(entry);

        Assert.Empty(panel.Rows);
        Assert.Contains("already on the screen", panel.Status);
        Assert.Null(panel.Highlighted);
        Assert.False(panel.Activate());   // Enter on an empty panel is a no-op, not a crash
    }

    // ============================================================ (3) the honest disclosure

    /// <summary>
    /// 🔴 <b>THE DISCLOSURE LOCK.</b> The vendor documents four More Details field groups; this build can
    /// populate one family of them. The footnote NAMES the ones it cannot, so a later slice cannot quietly
    /// drop the admission and grade 14.4 complete. Each entry is a MISSING STORE, verified against the domain:
    /// <c>EntryLine</c> has no narration field, nothing tracks unused voucher numbers, and <c>Company</c> has
    /// neither PAN nor CIN.
    /// </summary>
    [Fact]
    public void The_footnote_names_every_vendor_field_this_build_cannot_reach()
    {
        var k = NewKit("More Details Footnote Co");
        var entry = OpenSalesInvoice(k, k.CustomerId);
        var panel = new MoreDetailsViewModel(entry);

        Assert.Equal(3, MoreDetailsViewModel.WithheldFields.Count);
        foreach (var withheld in MoreDetailsViewModel.WithheldFields)
            Assert.Contains(withheld, panel.Footnote);

        // The three vendor field groups, each named so a silent deletion reddens here.
        Assert.Contains("Ledger Narration", panel.Footnote);
        Assert.Contains("Unused Voucher Nos.", panel.Footnote);
        Assert.Contains("PAN / CIN", panel.Footnote);
    }

    /// <summary>
    /// The claim behind the first footnote entry, asserted against the DOMAIN rather than trusted: narration
    /// lives on the voucher, not the line. If a later slice adds per-line narration this test reddens, which
    /// is the signal to promote that field from the footnote into a real row.
    /// </summary>
    [Fact]
    public void Per_line_narration_genuinely_has_nowhere_to_be_stored()
    {
        Assert.NotNull(typeof(Voucher).GetProperty(nameof(Voucher.Narration)));
        Assert.Null(typeof(EntryLine).GetProperty("Narration"));
        Assert.Null(typeof(Company).GetProperty("Pan"));
        Assert.Null(typeof(Company).GetProperty("Cin"));
    }

    // ============================================================ (4) reachability — the real keystroke

    /// <summary>
    /// 🔴 <b>REACHABILITY, DRIVEN THROUGH THE REAL WINDOW.</b> Menu route (the button bar, which is where the
    /// vendor puts this chord — its "Right button area"), screen, and keystroke. Green is not evidence on this
    /// row: the suite was green while the view model did not exist.
    /// </summary>
    [AvaloniaFact]
    public void Ctrl_I_opens_the_panel_on_a_voucher_and_Escape_returns_the_voucher_intact()
    {
        var k = NewKit("More Details Reachable Co");
        var entry = OpenSalesInvoice(k, k.CustomerId);
        var window = new MainWindow { DataContext = k.Vm, Width = 1280, Height = 720 };
        window.Show();
        try
        {
            // The button bar advertises the chord — the vendor's own placement for it.
            Assert.Contains(k.Vm.ButtonBar, b => b.Key == "Ctrl+I" && b.Caption == "More Details" && b.Enabled);
            // ...and no stale row still claims Ctrl+I for the item-invoice toggle.
            Assert.DoesNotContain(k.Vm.ButtonBar, b => b.Key == "Ctrl+I" && b.Caption == "As Invoice");

            window.KeyPressQwerty(PhysicalKey.I, RawInputModifiers.Control);

            Assert.Equal(Screen.MoreDetails, k.Vm.CurrentScreen);
            Assert.NotNull(k.Vm.MoreDetails);
            Assert.Contains(k.Vm.MoreDetails!.Rows, r => r.Label == "Bill-wise Details");

            // Escape pops the column and hands back the SAME half-keyed voucher, lines intact.
            window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);

            Assert.Equal(Screen.VoucherEntry, k.Vm.CurrentScreen);
            Assert.Null(k.Vm.MoreDetails);
            Assert.Same(entry, k.Vm.VoucherEntry);
            Assert.Equal("3", k.Vm.VoucherEntry!.InventoryLines[0].QuantityText);
        }
        finally { window.Close(); }
    }

    /// <summary>
    /// Enter on the panel reveals the field and pops back to the voucher, so the operator lands on the screen
    /// with the field now showing rather than on a spent column — the same "apply then return" shape
    /// <c>ApplyBasisOfValues</c> uses.
    /// </summary>
    [AvaloniaFact]
    public void Enter_reveals_the_field_and_returns_to_the_voucher()
    {
        var k = NewKit("More Details Enter Co");
        var entry = OpenSalesInvoice(k, k.CustomerId);
        var window = new MainWindow { DataContext = k.Vm, Width = 1280, Height = 720 };
        window.Show();
        try
        {
            window.KeyPressQwerty(PhysicalKey.I, RawInputModifiers.Control);
            Assert.Equal(Screen.MoreDetails, k.Vm.CurrentScreen);
            Assert.False(entry.ShowInvoiceBillWise);

            window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);

            Assert.Equal(Screen.VoucherEntry, k.Vm.CurrentScreen);
            Assert.True(entry.ShowInvoiceBillWise);
            Assert.True(entry.UseDefaultBillWiseAllocation);   // still not the knob
        }
        finally { window.Close(); }
    }

    /// <summary>
    /// 🔴 <b>Ctrl+A ON THE PANEL MUST NOT POST THE VOUCHER.</b> The voucher is still bound underneath (that is
    /// what makes Escape return it intact), so an unclaimed accept would reach the entry screen and save the
    /// very voucher the operator is still adding a field to. Ctrl+A is the shell's accept on every other
    /// column, so the panel has to answer it.
    /// </summary>
    [AvaloniaFact]
    public void Ctrl_A_on_the_panel_reveals_the_field_and_does_not_post_the_voucher()
    {
        var k = NewKit("More Details Accept Co");
        var entry = OpenSalesInvoice(k, k.CustomerId);
        var window = new MainWindow { DataContext = k.Vm, Width = 1280, Height = 720 };
        window.Show();
        try
        {
            Assert.True(entry.CanAccept);                     // the voucher WOULD post if the key leaked
            var postedBefore = k.Vm.Company!.Vouchers.Count;

            window.KeyPressQwerty(PhysicalKey.I, RawInputModifiers.Control);
            Assert.Equal(Screen.MoreDetails, k.Vm.CurrentScreen);

            window.KeyPressQwerty(PhysicalKey.A, RawInputModifiers.Control);

            Assert.Equal(postedBefore, k.Vm.Company!.Vouchers.Count);
            Assert.True(entry.ShowInvoiceBillWise);
            Assert.Equal(Screen.VoucherEntry, k.Vm.CurrentScreen);
        }
        finally { window.Close(); }
    }

    /// <summary>
    /// 🔴 <b>THE LAYOUT LOCK, AND IT IS HERE BECAUSE THE DEFECT WAS REAL.</b> The first version of the More
    /// Details column template put <c>TextWrapping="Wrap"</c> on the data-bound row text inside the panel's
    /// <c>ScrollViewer</c>. Content there is measured at UNBOUNDED width, and a wrapping <c>TextBlock</c> never
    /// settles against an infinite constraint: the layout pass span forever and took the whole headless test
    /// host with it — every test in this file timed out, and a blame-hang dump was what finally pinned it. The
    /// fix was fixed-height rows with <c>TextTrimming</c>, which is what the Switch To list beside it uses.
    ///
    /// <para>This test drives a real layout + dispatcher pass over the open panel, so a re-introduction hangs
    /// HERE with a named cause instead of hanging the suite anonymously. Note what would NOT have caught it:
    /// every view-model test in this file passed throughout, because none of them rendered anything.</para>
    /// </summary>
    [AvaloniaFact]
    public void The_open_panel_completes_a_layout_pass()
    {
        var k = NewKit("More Details Layout Co");
        var window = new MainWindow { DataContext = k.Vm, Width = 1280, Height = 720 };
        window.Show();
        try
        {
            OpenSalesInvoice(k, k.CustomerId);
            k.Vm.OpenMoreDetails();

            window.UpdateLayout();
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();

            Assert.Equal(Screen.MoreDetails, k.Vm.CurrentScreen);
            Assert.NotEmpty(k.Vm.MoreDetails!.Rows);
        }
        finally { window.Close(); }
    }

    /// <summary>
    /// A re-press must not stack a second column. Without the guard the cascade grows a column per keystroke
    /// and Escape then takes as many presses to get out as the operator made getting in.
    /// </summary>
    [AvaloniaFact]
    public void Pressing_the_chord_twice_does_not_stack_a_second_panel()
    {
        var k = NewKit("More Details Restack Co");
        var window = new MainWindow { DataContext = k.Vm, Width = 1280, Height = 720 };
        window.Show();
        OpenSalesInvoice(k, k.CustomerId);
        try
        {
            window.KeyPressQwerty(PhysicalKey.I, RawInputModifiers.Control);
            var columns = k.Vm.Columns.Count;
            var panel = k.Vm.MoreDetails;

            window.KeyPressQwerty(PhysicalKey.I, RawInputModifiers.Control);

            Assert.Equal(columns, k.Vm.Columns.Count);
            Assert.Same(panel, k.Vm.MoreDetails);
        }
        finally { window.Close(); }
    }
}
