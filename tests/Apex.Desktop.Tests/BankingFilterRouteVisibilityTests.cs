using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Desktop.Services;
using Apex.Desktop.ViewModels;
using Apex.Desktop.Views;
using Xunit;
using DomainLedger = Apex.Ledger.Domain.Ledger;

namespace Apex.Desktop.Tests;

/// <summary>
/// 🔴 <b>THE TWO WAVE-7 BANKING FILTERS HAVE TO BE ON THE SCREEN, NOT JUST IN THE VIEW MODEL.</b>
///
/// <para>Both of the controls this file guards were, until now, the exact failure this project has already filed
/// three times — a working capability with <b>no route in</b>:</para>
/// <list type="bullet">
///   <item><b>The bank filter (census 8.4).</b> <c>ChequePrinting.Build</c> shipped a <c>bankLedgerId</c>
///     parameter and a green engine test proving it narrows the list, with no caller in
///     <c>src/Apex.Desktop</c> able to pass a value. The vendor scopes that report by a <b>List of Banks</b>
///     (<c>help.tallysolutions.com/print-cheques/</c>, "Cheque Printing Report").</item>
///   <item><b>The fresh-page toggle (census 8.7).</b> <c>PrintPreviewViewModel.AdviceFreshPageEach</c> shipped
///     with a re-render hook, a <c>PaymentAdvicePdf</c> parameter and a green PDF test, and <b>no binding in
///     <c>MainWindow.axaml</c></b> — so the letters always printed one per page and the vendor's "Print each
///     transaction on a fresh page" (<c>help.tallysolutions.com/payment-advice/</c>) was unreachable.</item>
/// </list>
///
/// <para><b>Why these walk the realised visual tree.</b> A test that asserted <c>ShowChequeBankPicker</c> or
/// <c>IsPaymentAdviceLetter</c> would have passed on the build where neither control existed in the XAML at all —
/// that is precisely the test that goes green over a broken screen, and this project has shipped three slices
/// that way. So each test below opens the real <see cref="MainWindow"/>, walks in from the Gateway with the
/// arrow keys, and requires the control to be realised, effectively visible and laid out with non-zero bounds
/// before it touches its behaviour.</para>
///
/// <para>Headless-safe: visual-tree, visibility and layout-bounds inspection only. No Skia, no rendered frame,
/// and no printer — the advice test reads the preview's own page model, never an OS print queue.</para>
/// </summary>
public sealed class BankingFilterRouteVisibilityTests
{
    // ---------------------------------------------------------------- visual-tree harness

    private static IEnumerable<Avalonia.Visual> Descendants(Avalonia.Visual v)
    {
        foreach (var c in v.GetVisualChildren())
        {
            yield return c;
            foreach (var g in Descendants(c)) yield return g;
        }
    }

    private static void Pump(MainWindow w)
    {
        w.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        w.UpdateLayout();
    }

    /// <summary>A realised, visible, laid-out control matching the predicate. All three conditions together are
    /// what "the operator can see and use it" means: present-but-collapsed and visible-but-zero-sized both
    /// satisfy one of them alone.</summary>
    private static bool HasLiveControl<T>(MainWindow window, Func<T, bool> match) where T : Control =>
        Descendants(window).OfType<T>().Any(c =>
            c.IsEffectivelyVisible && c.Bounds.Width > 0 && c.Bounds.Height > 0 && match(c));

    private static T? LiveControl<T>(MainWindow window, Func<T, bool> match) where T : Control =>
        Descendants(window).OfType<T>().FirstOrDefault(c =>
            c.IsEffectivelyVisible && c.Bounds.Width > 0 && c.Bounds.Height > 0 && match(c));

    // ---------------------------------------------------------------- company / navigation harness

    private static (MainWindow Window, MainWindowViewModel Vm, string Dir) NewCompanyWindow(string name)
    {
        var dir = Path.Combine(Path.GetTempPath(), "ApexBankFilter_" + Guid.NewGuid().ToString("N"));
        var vm = new MainWindowViewModel(new CompanyStorage(dir));
        var window = new MainWindow { DataContext = vm, Width = 1280, Height = 720 };
        window.Show();

        vm.NewCompanyName = name;
        vm.CreateCompany();
        Pump(window);
        return (window, vm, dir);
    }

    private static void Cleanup(MainWindow window, string dir)
    {
        window.Close();
        try
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    /// <summary>Arrows down the ACTIVE column to a label and drills in — the operator's own fingers, not a
    /// method call, so a row that exists but is not arrow-reachable fails here.</summary>
    private static void ArrowToAndDrill(MainWindowViewModel vm, string label)
    {
        for (var i = 0; i < vm.Menu.Count + 2; i++)
        {
            if (vm.Menu[vm.SelectedIndex].Label == label) { vm.DrillIn(); return; }
            vm.MoveDown();
        }
        Assert.Fail($"'{label}' was not reachable by arrow navigation in the active column.");
    }

    private static void ArrowToBankingMenu(MainWindowViewModel vm)
    {
        vm.ShowGateway();
        ArrowToAndDrill(vm, "Banking");
    }

    private static DomainLedger AddBank(MainWindowViewModel vm, string name)
    {
        var bank = new DomainLedger(
            Guid.NewGuid(), name, vm.Company!.FindGroupByName("Bank Accounts")!.Id,
            Money.FromRupees(500000m), openingIsDebit: true)
        { EnableChequePrinting = true };
        vm.Company!.AddLedger(bank);
        return bank;
    }

    private static DomainLedger AddSupplier(MainWindowViewModel vm, string name)
    {
        var party = new DomainLedger(
            Guid.NewGuid(), name, vm.Company!.FindGroupByName("Sundry Creditors")!.Id,
            Money.Zero, openingIsDebit: false);
        vm.Company!.AddLedger(party);
        return party;
    }

    private static void PostChequePayment(
        Company c, DomainLedger party, DomainLedger bank, decimal amount, string instrument, DateOnly date)
    {
        var payment = c.VoucherTypes.Single(t => t.BaseType == VoucherBaseType.Payment);
        new Apex.Ledger.Services.LedgerService(c).Post(new Voucher(
            Guid.NewGuid(), payment.Id, date,
            new[]
            {
                new EntryLine(party.Id, Money.FromRupees(amount), DrCr.Debit),
                new EntryLine(bank.Id, Money.FromRupees(amount), DrCr.Credit,
                    bankAllocation: new BankAllocation(
                        BankTransactionType.ChequeOrDD, instrument, date, bankDate: null)),
            },
            partyId: party.Id));
    }

    /// <summary>Sheets in a rendered PDF — the same counter <c>PaymentAdvicePdfTests</c> uses, so the two files
    /// cannot disagree about what "a page" is. Latin1 because the PDF byte stream is not UTF-8, and the negated
    /// class keeps <c>/Type /Pages</c> (the page-tree node) from being counted as a page.</summary>
    private static int PdfPageCount(byte[] pdf) =>
        System.Text.RegularExpressions.Regex
            .Matches(System.Text.Encoding.Latin1.GetString(pdf), @"/Type\s*/Page[^s]").Count;

    private static void WidenPeriod(ReportsViewModel reports, DateOnly around) =>
        reports.SetPeriod(new DateOnly(around.Year - 1, 4, 1), new DateOnly(around.Year + 1, 3, 31));

    // ================================================================ census 8.4 — the bank filter

    /// <summary>
    /// 🔴 THE TEST. Walk in to Cheque Printing from the Gateway; the vendor's List-of-Banks scope must be a
    /// control the operator can actually see, offering "All Banks" and every cheque-printing bank by name —
    /// and picking one must narrow the report to that bank's cheques.
    /// </summary>
    [AvaloniaFact]
    public void The_bank_picker_is_drawn_on_the_cheque_printing_report_and_narrows_it()
    {
        var (window, vm, dir) = NewCompanyWindow("Bank Filter Co");
        try
        {
            var hdfc = AddBank(vm, "HDFC Bank");
            var sbi = AddBank(vm, "State Bank");
            var acme = AddSupplier(vm, "Acme Supplies");
            var date = new DateOnly(2026, 5, 20);
            PostChequePayment(vm.Company!, acme, hdfc, 30000m, "100200", date);
            PostChequePayment(vm.Company!, acme, sbi, 40000m, "770880", date);

            ArrowToBankingMenu(vm);
            ArrowToAndDrill(vm, "Cheque Printing");
            Assert.Equal(ReportKind.ChequePrinting, vm.Reports!.Kind);
            WidenPeriod(vm.Reports, date);
            Pump(window);

            // (1) The caption and the picker are ON THE SCREEN — realised, visible and laid out.
            Assert.True(
                HasLiveControl<TextBlock>(window, t => t.Text == "Bank"),
                "The Cheque Printing report drew no visible 'Bank' caption.");
            var picker = LiveControl<ComboBox>(window, c => ReferenceEquals(c.ItemsSource, vm.Reports!.ChequeBanks));
            Assert.True(picker is not null,
                "The Cheque Printing report drew no visible bank picker bound to ChequeBanks.");

            // (2) It offers the unfiltered head plus BOTH cheque-printing banks, by name.
            var offered = picker!.ItemsSource!.Cast<ChequeBankOption>().Select(o => o.Display).ToList();
            Assert.Equal(new[] { "All Banks", "HDFC Bank", "State Bank" }, offered);
            Assert.Same(ChequeBankOption.AllBanks, picker.SelectedItem);

            // (3) Unfiltered, both cheques are listed.
            Assert.Contains(vm.Reports!.Rows, r => r.Secondary.Contains("100200", StringComparison.Ordinal));
            Assert.Contains(vm.Reports!.Rows, r => r.Secondary.Contains("770880", StringComparison.Ordinal));

            // (4) 🔴 Picking a bank on the PICKER narrows the report — the parameter that had no route in.
            picker.SelectedItem = picker.ItemsSource!.Cast<ChequeBankOption>().Single(o => o.LedgerId == sbi.Id);
            Pump(window);
            Assert.DoesNotContain(vm.Reports!.Rows, r => r.Secondary.Contains("100200", StringComparison.Ordinal));
            Assert.Contains(vm.Reports!.Rows, r => r.Secondary.Contains("770880", StringComparison.Ordinal));
            Assert.Contains("State Bank", vm.Reports!.Subtitle);

            // (5) And back to everything, so the filter is not a one-way door.
            picker.SelectedItem = ChequeBankOption.AllBanks;
            Pump(window);
            Assert.Contains(vm.Reports!.Rows, r => r.Secondary.Contains("100200", StringComparison.Ordinal));
            Assert.Contains(vm.Reports!.Rows, r => r.Secondary.Contains("770880", StringComparison.Ordinal));
        }
        finally { Cleanup(window, dir); }
    }

    /// <summary>
    /// A filtered-empty report must say WHICH filter emptied it. Otherwise the operator cannot tell "no cheques
    /// anywhere" from "no cheques on the bank I picked" — the same rule the supplier advice's F8 empty state
    /// keeps, and the reason an empty screen reads as an answer instead of a breakage.
    /// </summary>
    [AvaloniaFact]
    public void A_bank_filter_that_empties_the_report_names_the_bank_it_emptied_it_for()
    {
        var (window, vm, dir) = NewCompanyWindow("Empty Filter Co");
        try
        {
            var hdfc = AddBank(vm, "HDFC Bank");
            AddBank(vm, "State Bank");
            var acme = AddSupplier(vm, "Acme Supplies");
            var date = new DateOnly(2026, 5, 20);
            PostChequePayment(vm.Company!, acme, hdfc, 30000m, "100200", date);

            ArrowToBankingMenu(vm);
            ArrowToAndDrill(vm, "Cheque Printing");
            WidenPeriod(vm.Reports!, date);
            Pump(window);

            var picker = LiveControl<ComboBox>(window, c => ReferenceEquals(c.ItemsSource, vm.Reports!.ChequeBanks));
            Assert.NotNull(picker);
            picker!.SelectedItem = picker.ItemsSource!.Cast<ChequeBankOption>().Single(o => o.Display == "State Bank");
            Pump(window);

            var note = Assert.Single(vm.Reports!.Rows);
            Assert.Contains("State Bank", note.Particulars);
            Assert.Contains("All Banks", note.Particulars);
        }
        finally { Cleanup(window, dir); }
    }

    /// <summary>
    /// The picker belongs to ONE report. On the supplier Payment Advice — the other row this wave landed, and the
    /// report an operator reaches from the very same menu — it must not be on the screen at all: a filter that
    /// paints over a report it does not drive is worse than no filter.
    /// </summary>
    [AvaloniaFact]
    public void The_bank_picker_is_not_drawn_on_the_payment_advice_report()
    {
        var (window, vm, dir) = NewCompanyWindow("Picker Scope Co");
        try
        {
            ArrowToBankingMenu(vm);
            ArrowToAndDrill(vm, "Cheque Printing");
            Pump(window);
            Assert.True(HasLiveControl<ComboBox>(window, c => ReferenceEquals(c.ItemsSource, vm.Reports!.ChequeBanks)));

            ArrowToBankingMenu(vm);
            ArrowToAndDrill(vm, "Payment Advice (Suppliers)");
            Pump(window);
            Assert.Equal(ReportKind.SupplierPaymentAdvice, vm.Reports!.Kind);
            Assert.False(vm.Reports.ShowChequeBankPicker);
            Assert.False(
                HasLiveControl<ComboBox>(window, c => ReferenceEquals(c.ItemsSource, vm.Reports!.ChequeBanks)),
                "The bank picker was drawn on the Payment Advice, which it does not drive.");
        }
        finally { Cleanup(window, dir); }
    }

    // ================================================================ census 8.7 — the fresh-page toggle

    /// <summary>
    /// 🔴 THE TEST. Print the supplier advices and the vendor's "Print each transaction on a fresh page"
    /// (<c>help.tallysolutions.com/payment-advice/</c>) must be a visible checkbox on the preview — and
    /// unticking it must actually re-paginate the letters, not merely flip a flag.
    ///
    /// <para>No printer is touched: the assertion reads the preview's own page model, which is why it holds on a
    /// CI runner that has no print queue at all.</para>
    /// </summary>
    [AvaloniaFact]
    public void The_fresh_page_toggle_is_drawn_on_the_advice_preview_and_repaginates_the_letters()
    {
        var (window, vm, dir) = NewCompanyWindow("Fresh Page Co");
        try
        {
            var bank = AddBank(vm, "HDFC Bank");
            var acme = AddSupplier(vm, "Acme Supplies");
            var brio = AddSupplier(vm, "Brio Traders");
            var date = new DateOnly(2026, 5, 20);
            PostChequePayment(vm.Company!, acme, bank, 30000m, "100200", date);
            PostChequePayment(vm.Company!, brio, bank, 45000m, "100201", date);

            ArrowToBankingMenu(vm);
            ArrowToAndDrill(vm, "Payment Advice (Suppliers)");
            WidenPeriod(vm.Reports!, date);
            vm.OpenPrintPreview();
            Pump(window);

            Assert.Equal(PrintPreviewViewModel.PrintKind.PaymentAdviceLetter, vm.PrintPreview!.Kind);

            // (1) The toggle is ON THE SCREEN.
            var box = LiveControl<CheckBox>(window, c => c.Content as string == "Each advice on a fresh page");
            Assert.True(box is not null, "The advice preview drew no visible fresh-page toggle.");

            // (2) It starts at the vendor's default — one letter per sheet — and there are two suppliers, so the
            //     PRINTED document is two sheets.
            //
            //     🔴 The assertion is on PdfBytes, NOT on PrintPreviewViewModel.Pages. Pages is the on-screen
            //     mirror, a flat PrintReport paginated by row count, and it does not carry the per-letter page
            //     break at all — so `Pages.Count` (and the "Pages: N" label bound to it) reads 1 either way and
            //     would go green on a build where the toggle did nothing. That mirror-vs-bytes divergence is a
            //     pre-existing property of every bespoke preview in this product (receipt, payslip, cheque), not
            //     something this toggle introduced; it is reported, not silently asserted around. What the
            //     operator actually receives is the bytes, so the bytes are what this pins.
            Assert.True(vm.PrintPreview!.AdviceFreshPageEach);
            var freshPages = PdfPageCount(vm.PrintPreview!.PdfBytes);
            Assert.Equal(2, freshPages);

            // (3) 🔴 Unticking it on the CONTROL re-paginates: the two letters now flow onto one sheet.
            box!.IsChecked = false;
            Pump(window);
            Assert.False(vm.PrintPreview!.AdviceFreshPageEach);
            var flowedPages = PdfPageCount(vm.PrintPreview!.PdfBytes);
            Assert.True(
                flowedPages < freshPages,
                $"Unticking the fresh-page toggle left the printed page count at {flowedPages}.");

            // (4) And both letters survive the flow — a page break is not allowed to eat a supplier.
            var cells = vm.PrintPreview!.Pages.SelectMany(p => p.Lines).SelectMany(l => l.Cells).ToList();
            Assert.Contains(cells, c => c.Contains("Acme Supplies", StringComparison.Ordinal));
            Assert.Contains(cells, c => c.Contains("Brio Traders", StringComparison.Ordinal));
        }
        finally { Cleanup(window, dir); }
    }

    /// <summary>
    /// 🔴 <b>F8 PRESSED ON THE REAL WINDOW</b> — census 8.7, <c>help.tallysolutions.com/payment-advice/</c>.
    ///
    /// <para>The shipped filter test calls <c>vm.ReportToggleAdviceReconciledOnly()</c> directly and merely asserts
    /// that <c>IsSupplierPaymentAdviceReport</c> — the guard the window's arm tests — is true. That test passes
    /// with the <c>case Key.F8 when vm.IsSupplierPaymentAdviceReport</c> arm <b>deleted from
    /// <c>MainWindow.axaml.cs</c> entirely</b>: it never presses a key. And the fall-through is not benign — the
    /// bare F8 on the global button bar opens a <b>Sales voucher</b>, so a missing arm does not leave the report
    /// unfiltered, it throws the operator off the report onto a voucher-entry screen.</para>
    ///
    /// <para>So this presses the physical key and requires both halves: the filter engaged, and the operator still
    /// standing on the report.</para>
    /// </summary>
    [AvaloniaFact]
    public void Pressing_F8_on_the_advice_report_filters_it_instead_of_opening_a_sales_voucher()
    {
        var (window, vm, dir) = NewCompanyWindow("F8 Advice Co");
        try
        {
            var bank = AddBank(vm, "HDFC Bank");
            var acme = AddSupplier(vm, "Acme Supplies");
            var date = new DateOnly(2026, 5, 20);
            PostChequePayment(vm.Company!, acme, bank, 30000m, "100200", date);   // bankDate null ⇒ unreconciled

            ArrowToBankingMenu(vm);
            ArrowToAndDrill(vm, "Payment Advice (Suppliers)");
            WidenPeriod(vm.Reports!, date);
            Pump(window);

            Assert.Contains(vm.Reports!.Rows, r => r.Particulars.Contains("Acme Supplies", StringComparison.Ordinal));
            Assert.False(vm.Reports!.AdviceReconciledOnly);

            window.KeyPressQwerty(PhysicalKey.F8, RawInputModifiers.None);
            Pump(window);

            // (1) The operator is still on the report — F8 did NOT fall through to the Sales voucher.
            Assert.Equal(Screen.Report, vm.CurrentScreen);
            Assert.Equal(ReportKind.SupplierPaymentAdvice, vm.Reports!.Kind);

            // (2) And the vendor's reconciled-only filter engaged: the uncleared payment is gone, and the empty
            //     state says which key brings it back.
            Assert.True(vm.Reports!.AdviceReconciledOnly);
            Assert.DoesNotContain(
                vm.Reports!.Rows, r => r.Particulars.Contains("Acme Supplies", StringComparison.Ordinal));
            Assert.Contains("reconciled only (F8)", vm.Reports!.Subtitle);

            // (3) F8 again returns it — a filter that cannot be lifted is a trap.
            window.KeyPressQwerty(PhysicalKey.F8, RawInputModifiers.None);
            Pump(window);
            Assert.False(vm.Reports!.AdviceReconciledOnly);
            Assert.Contains(vm.Reports!.Rows, r => r.Particulars.Contains("Acme Supplies", StringComparison.Ordinal));
        }
        finally { Cleanup(window, dir); }
    }

    /// <summary>
    /// The toggle is hidden on every other document. A page-break rule for advices means nothing on a report
    /// preview, and a checkbox that governs nothing is the dead-knob defect in a different costume.
    /// </summary>
    [AvaloniaFact]
    public void The_fresh_page_toggle_is_not_drawn_on_an_ordinary_report_preview()
    {
        var (window, vm, dir) = NewCompanyWindow("Toggle Scope Co");
        try
        {
            vm.OpenReport(ReportKind.TrialBalance);
            vm.OpenPrintPreview();
            Pump(window);

            Assert.Equal(PrintPreviewViewModel.PrintKind.Report, vm.PrintPreview!.Kind);
            Assert.False(vm.PrintPreview!.IsPaymentAdviceLetter);
            Assert.False(
                HasLiveControl<CheckBox>(window, c => c.Content as string == "Each advice on a fresh page"),
                "The fresh-page toggle was drawn on a report preview it does not govern.");
        }
        finally { Cleanup(window, dir); }
    }
}
