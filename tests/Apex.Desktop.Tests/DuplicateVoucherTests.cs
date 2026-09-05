using System;
using System.IO;
using System.Linq;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Reports;
using Apex.Desktop.Services;
using Apex.Desktop.ViewModels;
using Apex.Desktop.Views;
using Xunit;
using DomainLedger = Apex.Ledger.Domain.Ledger;

namespace Apex.Desktop.Tests;

/// <summary>
/// W2-15 — <b>Duplicate Voucher (Alt+2)</b>, census row 5.4, driven through the REAL
/// <see cref="MainWindow"/> key tunnel (<c>window.KeyPressQwerty</c>) rather than by asserting a view-model
/// method exists.
///
/// <para><b>FIDELITY (R7 / RULING 14 — the corpus is gone, so this is grounded on the vendor's own help).</b>
/// <i>help.tallysolutions.com/day-book-tally/</i> states the Day Book verb verbatim as
/// <i>"Press <b>Alt</b>+<b>2</b> (Duplicate Vch)"</i> and describes its numbering as: on the <b>same date</b>
/// the duplicate <i>"receives the next available number for that voucher type"</i>. That same-date limb is the
/// one this screen exercises, because a duplicate opens on the SOURCE voucher's date, and it is exactly what
/// <c>LedgerService.NextNumber</c> (max + 1) already produces.</para>
///
/// <para><b>🔴 THE DIVERGENCE, LABELLED AS OURS (RULING 9).</b> The vendor's other limb — <i>changed date: the
/// duplicate "takes the last voucher number for that type on the new date"</i> — is a property of a DATE-SCOPED
/// numbering engine. This application numbers a voucher type globally as max + 1 with no date scope
/// (<c>LedgerService.NextNumber</c>), so a duplicate whose date the operator moves still gets max + 1. That is
/// a numbering-engine divergence that predates this slice and is not re-engineered by it; it is recorded rather
/// than papered over, and <see cref="Duplicate_on_a_moved_date_still_takes_max_plus_one_OURS"/> pins it so a
/// later numbering slice cannot change it silently.</para>
///
/// <para><b>Every money figure below is derived by hand and asserted to the paisa.</b> The seed receipt is
/// Dr Cash 12,345.67 / Cr Capital 12,345.67; a duplicate of it must carry the identical two legs, so the second
/// posted voucher's totals are the same literal 12345.67m — not "whatever the first one had", which would pass
/// against an empty duplicate.</para>
/// </summary>
public sealed class DuplicateVoucherTests
{
    private const decimal Amount = 12345.67m;

    private static (MainWindow Window, MainWindowViewModel Vm, string TempDir) NewWindow()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "ApexDupVch_" + Guid.NewGuid().ToString("N"));
        var storage = new CompanyStorage(tempDir);
        var vm = new MainWindowViewModel(storage);
        var window = new MainWindow { DataContext = vm };
        window.Show();
        return (window, vm, tempDir);
    }

    private static void Close(MainWindow window, string tempDir)
    {
        window.Close();
        try { if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true); } catch { /* temp */ }
    }

    /// <summary>
    /// A fresh company carrying ONE posted Receipt — Dr Cash 12,345.67 / Cr Capital 12,345.67, narration
    /// "Opening float", dated financial-year-start + 5. Returns the two ledgers and that date.
    /// </summary>
    private static (DomainLedger Cash, DomainLedger Capital, DateOnly On) SeedOneReceipt(
        MainWindow window, MainWindowViewModel vm, string name)
    {
        vm.NewCompanyName = name;
        vm.CreateCompany();
        var on = vm.Company!.FinancialYearStart.AddDays(5);

        vm.ShowLedgerMaster();
        vm.LedgerMaster!.Name = "Capital A/c";
        vm.LedgerMaster!.SelectedGroup = vm.Company!.FindGroupByName("Capital Account");
        window.KeyPressQwerty(PhysicalKey.A, RawInputModifiers.Control);
        var capital = vm.Company!.FindLedgerByName("Capital A/c")!;
        var cash = vm.Company!.FindLedgerByName("Cash")!;

        vm.OpenVoucher(VoucherBaseType.Receipt);
        var e = vm.VoucherEntry!;
        e.Date = on;
        e.Lines[0].SelectedLedger = cash;
        e.Lines[0].Side = DrCr.Debit;
        e.Lines[0].AmountText = "12345.67";
        e.Lines[1].SelectedLedger = capital;
        e.Lines[1].Side = DrCr.Credit;
        e.Lines[1].AmountText = "12345.67";
        e.Narration = "Opening float";
        window.KeyPressQwerty(PhysicalKey.A, RawInputModifiers.Control);

        Assert.Single(vm.Company!.Vouchers);
        Assert.Equal(Amount, vm.Company!.Vouchers[0].TotalDebit.Amount);
        return (cash, capital, on);
    }

    /// <summary>Opens the Day Book with its one voucher row highlighted.</summary>
    private static ReportRow HighlightTheDayBookRow(MainWindowViewModel vm)
    {
        vm.OpenReport(ReportKind.DayBook);
        var row = vm.Reports!.Rows.First(r => r.DrillVoucherId != Guid.Empty);
        vm.Reports!.SelectedRow = row;
        return row;
    }

    // ============================================================ (a) THE DRIVING TEST — the key and the copy

    /// <summary>
    /// 🔴 <b>THE DRIVING TEST.</b> Real <b>Alt+2</b> on the highlighted Day-Book row opens a voucher-entry screen
    /// that is a <b>fresh entry</b> (<c>IsAltering</c> false — it must NOT be an alteration door in disguise),
    /// pre-filled with the source voucher's date, narration and BOTH legs to the paisa, and carrying the NEXT
    /// voucher number (2, because the book holds exactly Receipt No. 1).
    ///
    /// <para>Before the binding exists this fails at the first assertion: the screen stays
    /// <see cref="Screen.Report"/> because nothing in the tunnel consumes Alt+2 (<c>Key.D2</c> has zero hits in
    /// <c>src/</c>).</para>
    /// </summary>
    [AvaloniaFact]
    public void Alt2_on_a_day_book_row_opens_a_FRESH_entry_prefilled_from_that_voucher()
    {
        var (window, vm, tempDir) = NewWindow();
        try
        {
            var (cash, capital, on) = SeedOneReceipt(window, vm, "Dup Driving Co");
            HighlightTheDayBookRow(vm);

            window.KeyPressQwerty(PhysicalKey.Digit2, RawInputModifiers.Alt);

            Assert.Equal(Screen.VoucherEntry, vm.CurrentScreen);
            var e = vm.VoucherEntry!;

            // A DUPLICATE IS A NEW VOUCHER, not an amendment of the posted one.
            Assert.False(e.IsAltering);
            Assert.Equal(Guid.Empty, e.AlteringVoucherId);

            // The next available number for this type — the book holds Receipt No. 1, so max + 1 = 2.
            Assert.Equal(2, e.VoucherNumber);

            // The copy: same date, same narration, both legs to the paisa.
            Assert.Equal(on, e.Date);
            Assert.Equal("Opening float", e.Narration);
            Assert.Equal(2, e.Lines.Count);
            Assert.Same(cash, e.Lines[0].SelectedLedger);
            Assert.Equal(DrCr.Debit, e.Lines[0].Side);
            Assert.Equal(Amount, decimal.Parse(e.Lines[0].AmountText, System.Globalization.CultureInfo.InvariantCulture));
            Assert.Same(capital, e.Lines[1].SelectedLedger);
            Assert.Equal(DrCr.Credit, e.Lines[1].Side);
            Assert.Equal(Amount, decimal.Parse(e.Lines[1].AmountText, System.Globalization.CultureInfo.InvariantCulture));

            // And the ORIGINAL is untouched — opening a duplicate posts nothing on its own.
            Assert.Single(vm.Company!.Vouchers);
        }
        finally { Close(window, tempDir); }
    }

    /// <summary>
    /// The round trip that makes it a real feature: Alt+2 → Ctrl+A → the book holds <b>TWO</b> vouchers, the
    /// original Receipt No. 1 standing unchanged beside the new Receipt No. 2, both carrying the identical
    /// 12,345.67 on each side, and the Day Book beneath re-rendered so both are on screen.
    /// </summary>
    [AvaloniaFact]
    public void Accepting_the_duplicate_posts_a_SECOND_voucher_and_leaves_the_original_standing()
    {
        var (window, vm, tempDir) = NewWindow();
        try
        {
            var (_, _, on) = SeedOneReceipt(window, vm, "Dup RoundTrip Co");
            var originalId = vm.Company!.Vouchers[0].Id;
            HighlightTheDayBookRow(vm);

            window.KeyPressQwerty(PhysicalKey.Digit2, RawInputModifiers.Alt);
            Assert.Equal(Screen.VoucherEntry, vm.CurrentScreen);

            window.KeyPressQwerty(PhysicalKey.A, RawInputModifiers.Control);

            Assert.Equal(2, vm.Company!.Vouchers.Count);

            var original = vm.Company!.FindVoucher(originalId)!;
            Assert.Equal(1, original.Number);
            Assert.Equal(Amount, original.TotalDebit.Amount);
            Assert.Equal(Amount, original.TotalCredit.Amount);
            Assert.Equal(on, original.Date);

            var copy = vm.Company!.Vouchers.Single(v => v.Id != originalId);
            Assert.Equal(2, copy.Number);
            Assert.Equal(original.TypeId, copy.TypeId);
            Assert.Equal(on, copy.Date);
            Assert.Equal(Amount, copy.TotalDebit.Amount);
            Assert.Equal(Amount, copy.TotalCredit.Amount);
            Assert.Equal("Opening float", copy.Narration);

            // Both legs copied, same ledgers, same sides, same paisa.
            Assert.Equal(2, copy.Lines.Count);
            Assert.Equal(
                original.Lines.Select(l => (l.LedgerId, l.Side, l.Amount.Amount)).OrderBy(t => t.Item1).ToList(),
                copy.Lines.Select(l => (l.LedgerId, l.Side, l.Amount.Amount)).OrderBy(t => t.Item1).ToList());

            // The Day Book beneath was refreshed, so the operator sees the new voucher without re-opening it.
            Assert.Equal(Screen.Report, vm.CurrentScreen);
            Assert.Equal(2, vm.Reports!.Rows.Count(r => r.DrillVoucherId != Guid.Empty));
        }
        finally { Close(window, tempDir); }
    }

    /// <summary>
    /// The duplicate is reachable from the READ-ONLY voucher-detail column too — the pane an operator lands on
    /// after drilling a Day-Book row with Enter. Same three surfaces Ctrl+Enter (alteration) admits, so the two
    /// verbs cannot disagree about which document the highlight means.
    /// </summary>
    [AvaloniaFact]
    public void Alt2_works_from_the_voucher_detail_drill_column()
    {
        var (window, vm, tempDir) = NewWindow();
        try
        {
            SeedOneReceipt(window, vm, "Dup Detail Co");
            HighlightTheDayBookRow(vm);

            // Enter drills the row into the read-only voucher-detail column.
            window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
            Assert.Equal(Screen.VoucherDetail, vm.CurrentScreen);

            window.KeyPressQwerty(PhysicalKey.Digit2, RawInputModifiers.Alt);

            Assert.Equal(Screen.VoucherEntry, vm.CurrentScreen);
            Assert.False(vm.VoucherEntry!.IsAltering);
            Assert.Equal(2, vm.VoucherEntry!.VoucherNumber);
            Assert.Equal(Amount,
                decimal.Parse(vm.VoucherEntry!.Lines[0].AmountText, System.Globalization.CultureInfo.InvariantCulture));
        }
        finally { Close(window, tempDir); }
    }

    // ============================================================ (b) scope — the key must not fire elsewhere

    /// <summary>
    /// Alt+2 is INERT where there is no highlighted voucher: on the bare Gateway it does nothing at all (no
    /// screen change, no column). A key that opened a blank entry from the Gateway would be a new global
    /// accelerator wearing a report verb's name.
    /// </summary>
    [AvaloniaFact]
    public void Alt2_is_inert_on_the_gateway()
    {
        var (window, vm, tempDir) = NewWindow();
        try
        {
            SeedOneReceipt(window, vm, "Dup Scope Co");
            vm.ShowGateway();
            var columns = vm.Columns.Count;

            window.KeyPressQwerty(PhysicalKey.Digit2, RawInputModifiers.Alt);

            Assert.Equal(Screen.Gateway, vm.CurrentScreen);
            Assert.Equal(columns, vm.Columns.Count);
            Assert.Null(vm.VoucherEntry);
        }
        finally { Close(window, tempDir); }
    }

    /// <summary>
    /// 🔴 <b>OURS, and pinned so a later numbering slice cannot move it silently.</b> The vendor's changed-date
    /// limb gives the duplicate <i>"the last voucher number for that type on the new date"</i>. This application's
    /// numbering has no date scope at all — <c>LedgerService.NextNumber</c> is max + 1 over the whole type — so
    /// moving the duplicate's date leaves the number at 2. Asserted as the DIVERGENCE it is, not as fidelity.
    /// </summary>
    [AvaloniaFact]
    public void Duplicate_on_a_moved_date_still_takes_max_plus_one_OURS()
    {
        var (window, vm, tempDir) = NewWindow();
        try
        {
            var (_, _, on) = SeedOneReceipt(window, vm, "Dup Divergence Co");
            HighlightTheDayBookRow(vm);
            window.KeyPressQwerty(PhysicalKey.Digit2, RawInputModifiers.Alt);

            var e = vm.VoucherEntry!;
            e.Date = on.AddDays(10);          // the operator moves the date, as the vendor's flow expects
            Assert.Equal(2, e.VoucherNumber);  // …and OUR number does not follow the date.

            window.KeyPressQwerty(PhysicalKey.A, RawInputModifiers.Control);
            var copy = vm.Company!.Vouchers.Single(v => v.Number == 2);
            Assert.Equal(on.AddDays(10), copy.Date);
        }
        finally { Close(window, tempDir); }
    }

    // ============================================================ (c) discoverability — not a key-only verb

    /// <summary>
    /// The verb is ADVERTISED, not key-only: the button bar carries an "Alt+2 / Duplicate" row wherever the
    /// chord bites, and the row is DIMMED where it does not. This codebase's own standing rule is that a
    /// keystroke nobody can find is not a feature (the Data section's comment: "a safety net nobody can find is
    /// not a safety net"), and its opposite — an enabled badge that fires nothing — is register defect IV-31.
    /// </summary>
    [AvaloniaFact]
    public void The_duplicate_verb_is_advertised_on_the_button_bar_and_dimmed_off_its_surfaces()
    {
        var (window, vm, tempDir) = NewWindow();
        try
        {
            SeedOneReceipt(window, vm, "Dup ButtonBar Co");

            // On the bare Gateway the row exists but is dimmed — there is no highlighted voucher to copy.
            vm.ShowGateway();
            var onGateway = vm.ButtonBar.Single(b => b.Key == "Alt+2");
            Assert.Equal("Duplicate", onGateway.Caption);
            Assert.False(onGateway.Enabled);

            // On the Day Book with a row highlighted it is live, and clicking it does what the chord does.
            HighlightTheDayBookRow(vm);
            var onDayBook = vm.ButtonBar.Single(b => b.Key == "Alt+2");
            Assert.True(onDayBook.Enabled);

            onDayBook.Invoke.Execute(null);
            Assert.Equal(Screen.VoucherEntry, vm.CurrentScreen);
            Assert.False(vm.VoucherEntry!.IsAltering);
            Assert.Equal(2, vm.VoucherEntry!.VoucherNumber);
        }
        finally { Close(window, tempDir); }
    }

    // ============================================================ (d) refusals reach the operator

    /// <summary>
    /// With nothing highlighted the door is a <b>quiet no-op</b> — <c>NoVoucherHere</c>, no notice, no screen
    /// change. It is deliberately NOT a refusal: a refusal writes a sentence on the notice bar, and there is
    /// nothing to say about a row the operator has not chosen.
    /// </summary>
    [AvaloniaFact]
    public void Duplicate_with_no_row_highlighted_is_a_quiet_no_op()
    {
        var (window, vm, tempDir) = NewWindow();
        try
        {
            SeedOneReceipt(window, vm, "Dup NoRow Co");
            vm.OpenReport(ReportKind.DayBook);
            vm.Reports!.SelectedRow = null;

            Assert.Equal(VoucherAlterationRequest.NoVoucherHere, vm.RequestDuplicateHighlightedVoucher());
            Assert.Equal(Screen.Report, vm.CurrentScreen);
            Assert.Null(vm.VoucherEntry);
        }
        finally { Close(window, tempDir); }
    }

    /// <summary>
    /// 🔴 <b>The refusal is SHOWN, never swallowed</b> — the duplicate door asks the very same
    /// <see cref="VoucherAlterationEligibility"/> question the alteration door does, because a duplicate is
    /// re-keyed on exactly the grid an alteration is re-keyed on. Here the cheapest reachable arm: a voucher id
    /// the books do not hold, which the predicate answers by name rather than with a bare null.
    /// </summary>
    [AvaloniaFact]
    public void ForDuplicate_returns_the_eligibility_refusal_rather_than_a_bare_null()
    {
        var (window, vm, tempDir) = NewWindow();
        try
        {
            SeedOneReceipt(window, vm, "Dup Refusal Co");

            var open = VoucherEntryViewModel.ForDuplicate(
                vm.Company!, Guid.NewGuid(), new CompanyStorage(tempDir),
                onSaved: () => { }, onCancelled: () => { });

            Assert.Null(open.Entry);
            Assert.NotNull(open.Refusal);
            Assert.Contains("no longer in this company's books", open.Refusal!);

            // …and the door for a voucher that IS there opens instead of refusing.
            var good = VoucherEntryViewModel.ForDuplicate(
                vm.Company!, vm.Company!.Vouchers[0].Id, new CompanyStorage(tempDir),
                onSaved: () => { }, onCancelled: () => { });
            Assert.Null(good.Refusal);
            Assert.NotNull(good.Entry);
            Assert.False(good.Entry!.IsAltering);
        }
        finally { Close(window, tempDir); }
    }
}
