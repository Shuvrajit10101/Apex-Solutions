using System;
using System.Globalization;
using System.IO;
using System.Linq;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Io;
using Apex.Ledger.Reports;
using Apex.Ledger.Services;
using Apex.Desktop.Services;
using Apex.Desktop.ViewModels;
using Apex.Desktop.Views;
using Xunit;
using DomainLedger = Apex.Ledger.Domain.Ledger;

namespace Apex.Desktop.Tests;

/// <summary>
/// 🔴 <b>S5d/S5e review — the surfaces an alteration leaves BEHIND it, and the F-keys that discard it.</b>
/// Companion to <see cref="VoucherAlterReachabilityTests"/>, which proves the door OPENS; this file proves what
/// happens to the rest of the window when the door closes again.
///
/// <para><b>ROOT C, named by the review's completeness critic.</b> S5d/S5e created a "this voucher just changed
/// underneath you" event and wired its consumers BY HAND — <c>onSaved</c> is a closure listing the surfaces its
/// author remembered (report + register), so three were missed: the read-only voucher-detail column the
/// alteration was raised FROM (and the Print / e-mail documents that column issues), and the POS
/// print-after-save receipt. All three are pinned below. The rule the next author needs: <b>every surface an
/// alteration can be raised from must be refreshed by the same <c>onSaved</c>, and a screen with no refresh
/// entry point is a screen that cannot be a surface.</b></para>
///
/// <para><b>The second half of the file is the WORK-LOSS half of the F-key finding.</b> It is deliberately NOT
/// scoped to <c>IsAltering</c>: plain F4–F9 destroyed a half-keyed NEW entry exactly as silently as they
/// destroyed an alteration, so the guard is scoped to the voucher-entry SCREEN and both halves are pinned.
/// The memorandum→payment CONVERSION the corpus attests on an alteration screen (Book 664311548: <i>"Click on
/// Payment (F5) button provided at memorandum alteration screen … The voucher will converted as payment voucher
/// with same entry."</i>) is a SEPARATE, corpus-scoped item and is deliberately not built here.</para>
/// </summary>
public sealed class VoucherAlterationSurfaceRefreshTests
{
    private static readonly DateOnly FyStart = new(2024, 4, 1);

    // ============================================================ harness (mirrors VoucherAlterReachabilityTests)

    private static (MainWindow Window, MainWindowViewModel Vm, string TempDir) NewWindow()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "ApexAlterSurface_" + Guid.NewGuid().ToString("N"));
        var vm = new MainWindowViewModel(new CompanyStorage(tempDir));
        var window = new MainWindow { DataContext = vm };
        window.Show();
        return (window, vm, tempDir);
    }

    private static void Pump(MainWindow window)
    {
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
    }

    private static void Key(MainWindow window, PhysicalKey key, RawInputModifiers mods = RawInputModifiers.None)
    {
        window.KeyPressQwerty(key, mods);
        Pump(window);
    }

    private static void Close(MainWindow window, string dir)
    {
        window.Close();
        try
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
        catch (IOException) { /* best effort */ }
        catch (UnauthorizedAccessException) { /* best effort */ }
    }

    private static DomainLedger AddLedger(Company c, string name, string groupName)
    {
        var group = c.FindGroupByName(groupName) ?? throw new InvalidOperationException($"No group '{groupName}'.");
        var ledger = new DomainLedger(Guid.NewGuid(), name, group.Id, Money.Zero, openingIsDebit: false);
        c.AddLedger(ledger);
        return ledger;
    }

    private static decimal Closing(Company c, DomainLedger l) =>
        LedgerBalances.SignedClosing(c, l, c.FinancialYearStart.AddYears(1));

    private sealed record Book(
        MainWindowViewModel Vm, string Name, DomainLedger Landlord, DomainLedger Rent, Voucher Journal);

    /// <summary>
    /// One posted Journal (Dr Rent 8,431.55 / Cr Landlord 8,431.55) keyed through the real entry screen — the same
    /// fixture <see cref="VoucherAlterReachabilityTests"/> uses, and for the same reasons (a Journal never opens in
    /// Single Entry, so both legs stay directly editable; odd paise are the house rule).
    /// </summary>
    private static Book SeedOneJournal(MainWindow window, MainWindowViewModel vm, string name)
    {
        vm.NewCompanyName = name;
        vm.CreateCompany();
        var c = vm.Company!;
        c.FinancialYearStart = FyStart;
        c.BooksBeginFrom = FyStart;

        var rent = AddLedger(c, "Rent", "Indirect Expenses");
        var landlord = AddLedger(c, "Landlord", "Sundry Creditors");

        vm.OpenVoucher(VoucherBaseType.Journal);
        var e = vm.VoucherEntry!;
        e.Date = FyStart.AddDays(7);                       // 08-Apr-2024
        e.Lines[0].SelectedLedger = rent;
        e.Lines[0].Side = DrCr.Debit;
        e.Lines[0].AmountText = "8431.55";
        e.Lines[1].SelectedLedger = landlord;
        e.Lines[1].Side = DrCr.Credit;
        e.Lines[1].AmountText = "8431.55";
        Key(window, PhysicalKey.A, RawInputModifiers.Control);
        Assert.Single(c.Vouchers);
        Assert.Equal(8431.55m, Closing(c, rent));

        while (vm.CurrentScreen != Screen.Gateway && vm.Columns.Count > 1) vm.Back();
        Pump(window);
        return new Book(vm, name, landlord, rent, c.Vouchers[0]);
    }

    private static void OpenDayBookOn(MainWindow window, MainWindowViewModel vm, Guid voucherId)
    {
        vm.OpenReport(ReportKind.DayBook);
        Pump(window);
        vm.Reports!.SelectedRow = vm.Reports!.Rows.First(r => r.DrillVoucherId == voucherId);
        Pump(window);
    }

    /// <summary>Day Book → highlight the row → PLAIN Enter, which lands on the read-only voucher-detail column
    /// (USER DECISION 1 / VL-1) → REAL Ctrl+Enter, which opens the alteration OVER it.</summary>
    private static void AlterFromTheVoucherDetailColumn(MainWindow window, MainWindowViewModel vm, Guid voucherId)
    {
        OpenDayBookOn(window, vm, voucherId);
        Key(window, PhysicalKey.Enter);
        Assert.Equal(Screen.VoucherDetail, vm.CurrentScreen);
        Assert.NotNull(vm.VoucherDetail);
        Key(window, PhysicalKey.Enter, RawInputModifiers.Control);
    }

    /// <summary>Every cell of every rendered preview line, so an assertion can ask what the PAPER says.</summary>
    private static string PreviewText(PrintPreviewViewModel preview) =>
        string.Join(" | ", preview.Pages
            .SelectMany(p => new[] { p.Title, p.Subtitle }.Concat(p.Lines.SelectMany(l => l.Cells))));

    /// <summary>Every cell of the voucher-detail pane, so an assertion can ask what the SCREEN says.</summary>
    private static string PaneText(VoucherDetailViewModel detail) =>
        string.Join(" | ", detail.Rows.Select(r => $"{r.Particulars}:{r.Debit}/{r.Credit}"));

    // ==================================================================================================
    // (a) ITEM 5 — THE VOUCHER-DETAIL COLUMN THE ALTERATION WAS RAISED FROM
    // ==================================================================================================

    /// <summary>
    /// 🔴 <b>The pane the operator altered FROM must not survive as a stale snapshot.</b>
    ///
    /// <para><b>Derivation of every figure.</b> The seed posts Dr Rent 8,431.55 / Cr Landlord 8,431.55. The
    /// alteration retypes BOTH legs to 9,102.35, so after Ctrl+A the book — and therefore the pane rendered from
    /// it — must read 9,102.35 on both sides and the Total row must read 9,102.35 / 9,102.35. Nothing here is read
    /// off the code: 9,102.35 is the value this test types in.</para>
    ///
    /// <para><b>What it was before the fix.</b> <c>onSaved</c> ran <c>BackFromPage(); report?.Show(report.Kind);
    /// register?.Refresh();</c> and never touched <c>VoucherDetail</c>, whose <c>_voucher</c> is captured once in
    /// its constructor — and <c>LedgerService.Replace</c> puts a NEW <c>Voucher</c> object at the same index, so
    /// the pane's field is a DISCARDED object, not an aliased one that would have self-updated.</para>
    /// </summary>
    [AvaloniaFact]
    public void Altering_from_the_voucher_detail_column_refreshes_the_pane_it_was_raised_from()
    {
        var (window, vm, dir) = NewWindow();
        try
        {
            var b = SeedOneJournal(window, vm, "Detail Refresh Co");
            AlterFromTheVoucherDetailColumn(window, vm, b.Journal.Id);
            Assert.True(vm.VoucherEntry!.IsAltering);

            foreach (var line in vm.VoucherEntry!.Lines) line.AmountText = "9102.35";
            Key(window, PhysicalKey.A, RawInputModifiers.Control);

            // The book really moved — without this the pane assertion below could pass on a no-op.
            Assert.Equal(9102.35m, Closing(vm.Company!, b.Rent));
            Assert.Equal(-9102.35m, Closing(vm.Company!, b.Landlord));

            // …and the operator is standing on the voucher-detail column again, which must now agree with it.
            Assert.Equal(Screen.VoucherDetail, vm.CurrentScreen);
            var pane = PaneText(vm.VoucherDetail!);
            Assert.Contains("9,102.35", pane, StringComparison.Ordinal);
            Assert.DoesNotContain("8,431.55", pane, StringComparison.Ordinal);
        }
        finally { Close(window, dir); }
    }

    /// <summary>
    /// 🔴 <b>The document Print issues under the live voucher number must be the AMENDED one.</b> This is the
    /// half that leaves the building: <c>OpenPrintPreview</c> takes the <c>Screen.VoucherDetail</c> branch and
    /// calls <c>vd.BuildPrintPreview()</c>, and <c>EmailComposeViewModel.RenderVoucherPdf</c> attaches the
    /// identical bytes — so before the fix the counterparty received a document contradicting the book under the
    /// same document number, with nothing on screen to say so.
    ///
    /// <para>Asserted on the PLAIN Dr/Cr projection (a Journal, so <c>ProjectVoucher</c>), because the verifier
    /// established the staleness is in the constructor-time snapshot and therefore hits every family and BOTH
    /// projections — fixing it at the invoice path would have fixed one branch of two.</para>
    /// </summary>
    [AvaloniaFact]
    public void The_document_Print_issues_after_an_alteration_is_the_amended_one_not_the_superseded_snapshot()
    {
        var (window, vm, dir) = NewWindow();
        try
        {
            var b = SeedOneJournal(window, vm, "Detail Print Co");
            AlterFromTheVoucherDetailColumn(window, vm, b.Journal.Id);

            foreach (var line in vm.VoucherEntry!.Lines) line.AmountText = "9102.35";
            Key(window, PhysicalKey.A, RawInputModifiers.Control);
            Assert.Equal(Screen.VoucherDetail, vm.CurrentScreen);

            vm.OpenPrintPreview();
            Pump(window);
            Assert.Equal(Screen.PrintPreview, vm.CurrentScreen);
            var paper = PreviewText(vm.PrintPreview!);
            Assert.Contains("9,102.35", paper, StringComparison.Ordinal);
            Assert.DoesNotContain("8,431.55", paper, StringComparison.Ordinal);
        }
        finally { Close(window, dir); }
    }

    /// <summary>
    /// 🔴 <b>The E-MAIL door is the half that leaves the building, and it is a real, reachable door.</b>
    /// <c>OpenEmailCompose</c> gates on <c>CurrentScreen == Screen.VoucherDetail &amp;&amp; VoucherDetail is { } vd</c>
    /// and hands that same instance to <c>EmailComposeViewModel</c>, whose constructor renders the attachment from
    /// <see cref="VoucherDetailViewModel.BuildPrintPreview"/> — so before the fix the counterparty received the
    /// SUPERSEDED invoice as a PDF under the live document number.
    ///
    /// <para>Asserted on the BYTES rather than on extracted PDF text, and in both directions: the attachment must
    /// no longer be what it was before the alteration, and it must be exactly what the refreshed pane now
    /// projects. A one-directional assertion would pass on an attachment that had merely become garbage.</para>
    /// </summary>
    [AvaloniaFact]
    public void The_e_mail_attachment_after_an_alteration_is_the_amended_document_too()
    {
        var (window, vm, dir) = NewWindow();
        try
        {
            var b = SeedOneJournal(window, vm, "Detail Email Co");
            OpenDayBookOn(window, vm, b.Journal.Id);
            Key(window, PhysicalKey.Enter);
            Assert.Equal(Screen.VoucherDetail, vm.CurrentScreen);
            var supersededPdf = vm.VoucherDetail!.BuildPrintPreview().PdfBytes;
            Assert.NotEmpty(supersededPdf);

            Key(window, PhysicalKey.Enter, RawInputModifiers.Control);
            foreach (var line in vm.VoucherEntry!.Lines) line.AmountText = "9102.35";
            Key(window, PhysicalKey.A, RawInputModifiers.Control);
            Assert.Equal(Screen.VoucherDetail, vm.CurrentScreen);

            vm.OpenEmailCompose();
            Pump(window);
            var panel = vm.EmailCompose!;
            Assert.True(panel.HasAttachment);
            var attached = Assert.Single(panel.BuildMessage().Attachments).Content;

            Assert.NotEqual(supersededPdf, attached);                                  // no longer the old document…
            Assert.Equal(vm.VoucherDetail!.BuildPrintPreview().PdfBytes, attached);     // …and exactly the new one
        }
        finally { Close(window, dir); }
    }

    /// <summary>
    /// 🔴 <b>It is not only the money.</b> <c>Title</c>, <c>Subtitle</c> (date + party) and the
    /// (Cancelled)/(Optional)/(Post-dated) flags are all built once in the constructor too, so an alteration that
    /// moved the DATE — which <c>Replace</c> permits with a <c>DateChanged</c> warning rather than a refusal —
    /// left the pane AND the printed header showing the old date.
    ///
    /// <para><b>Derivation.</b> Posted on <c>FyStart.AddDays(7)</c> = 08-Apr-2024; the alteration moves it to
    /// <c>FyStart.AddDays(11)</c> = 12-Apr-2024. <c>ApexDate.Format</c>'s one canonical form is
    /// <c>dd-MMM-yyyy</c>, so the pane's subtitle must read "12-Apr-2024" and must no longer read "08-Apr-2024".
    /// </para>
    /// </summary>
    [AvaloniaFact]
    public void An_alteration_that_moves_the_date_moves_the_header_the_pane_shows()
    {
        var (window, vm, dir) = NewWindow();
        try
        {
            var b = SeedOneJournal(window, vm, "Detail Date Co");
            AlterFromTheVoucherDetailColumn(window, vm, b.Journal.Id);
            Assert.Contains("08-Apr-2024", vm.VoucherDetail!.Subtitle, StringComparison.Ordinal);

            vm.VoucherEntry!.Date = FyStart.AddDays(11);          // 12-Apr-2024
            Key(window, PhysicalKey.A, RawInputModifiers.Control);

            Assert.Equal(FyStart.AddDays(11), vm.Company!.FindVoucher(b.Journal.Id)!.Date);
            Assert.Equal(Screen.VoucherDetail, vm.CurrentScreen);
            Assert.Contains("12-Apr-2024", vm.VoucherDetail!.Subtitle, StringComparison.Ordinal);
            Assert.DoesNotContain("08-Apr-2024", vm.VoucherDetail!.Subtitle, StringComparison.Ordinal);
        }
        finally { Close(window, dir); }
    }

    // ==================================================================================================
    // (b) ITEM 5, POS HALF — the same three lines, character-for-character, on the POS door
    // ==================================================================================================

    private sealed record PosBook(Company Company, StockItem Widget, Voucher Bill);

    /// <summary>
    /// A POS-flagged Sales bill posted through the REAL POS screen: 2 × Till Widget @ 649.37 = 1,298.74, one cash
    /// tender. GST is deliberately NOT configured, so the bill is the taxable value alone and every figure below
    /// is a two-term product the reader can check by hand.
    /// </summary>
    private static PosBook SeedOnePosBill(MainWindow window, MainWindowViewModel vm, string name,
        bool printAfterSave)
    {
        vm.NewCompanyName = name;
        vm.CreateCompany();
        var c = vm.Company!;
        c.FinancialYearStart = FyStart;
        c.BooksBeginFrom = FyStart;

        var masters = new InventoryService(c);
        var group = masters.CreateStockGroup("Goods");
        var nos = masters.CreateSimpleUnit("Nos", "Numbers");
        var widget = masters.CreateStockItem("Till Widget", group.Id, nos.Id);
        AddLedger(c, "Retail Sales", "Sales Accounts");
        AddLedger(c, "Till Cash", "Cash-in-Hand");

        vm.OpenPosBilling();
        Pump(window);
        var pos = vm.PosBilling!;
        // The corpus's own instruction for a POS voucher type — "Print voucher after saving - Set to `Yes'."
        // (Book 664311548). Set through the screen's own property so the type is persisted with it.
        if (printAfterSave) pos.PrintAfterSave = true;
        pos.Date = FyStart.AddDays(9);
        pos.SelectedSalesLedger = pos.SalesLedgers.Single(l => l.Name == "Retail Sales");
        pos.CashRow.SelectedLedger = c.Ledgers.Single(l => l.Name == "Till Cash");
        var line = pos.Items[0];
        line.SelectedItem = pos.StockItems.Single(i => i.Id == widget.Id);
        line.SelectedGodown = pos.Godowns.Single(g => g.Id == c.MainLocation!.Id);
        line.QuantityText = "2";
        line.RateText = "649.37";
        Key(window, PhysicalKey.A, RawInputModifiers.Control);

        var bill = Assert.Single(c.Vouchers);
        Assert.Equal(1298.74m, bill.TotalDebit.Amount);          // 2 × 649.37, derived by hand
        while (vm.CurrentScreen != Screen.Gateway && vm.Columns.Count > 1) vm.Back();
        Pump(window);
        return new PosBook(c, widget, bill);
    }

    /// <summary>
    /// 🔴 <b>The POS door's <c>onSaved</c> is character-for-character the accounting one's, so it carries the same
    /// stale pane.</b> The verifier proved this half by INSPECTION only — here it is driven.
    ///
    /// <para><b>Derivation.</b> Posted 2 × 649.37 = 1,298.74. The alteration retypes the rate to 700.11, so the
    /// bill becomes 2 × 700.11 = 1,400.22 and the pane must read it.</para>
    /// </summary>
    [AvaloniaFact]
    public void Altering_a_POS_bill_from_the_voucher_detail_column_refreshes_that_pane_too()
    {
        var (window, vm, dir) = NewWindow();
        try
        {
            var p = SeedOnePosBill(window, vm, "POS Detail Refresh Co", printAfterSave: false);
            AlterFromTheVoucherDetailColumn(window, vm, p.Bill.Id);
            Assert.Equal(Screen.PosBilling, vm.CurrentScreen);
            Assert.True(vm.PosBilling!.IsAltering);

            vm.PosBilling!.Items[0].RateText = "700.11";
            vm.PosBilling!.CashRow.CashTenderedText = "1400.22";
            Key(window, PhysicalKey.A, RawInputModifiers.Control);

            Assert.Equal(1400.22m, p.Company.FindVoucher(p.Bill.Id)!.TotalDebit.Amount);
            Assert.Equal(Screen.VoucherDetail, vm.CurrentScreen);
            var pane = PaneText(vm.VoucherDetail!);
            Assert.Contains("1,400.22", pane, StringComparison.Ordinal);
            Assert.DoesNotContain("1,298.74", pane, StringComparison.Ordinal);
        }
        finally { Close(window, dir); }
    }

    // ==================================================================================================
    // (c) L2 — PRINT-AFTER-SAVE ON THE POS ALTERATION DOOR (the same missing-consumer root)
    // ==================================================================================================

    /// <summary>
    /// 🔴 <b>LAYER ONE of the two-layered defect: the alteration accept path must RAISE the receipt.</b>
    /// <c>AcceptAlterationCore</c> ended at <c>_onSaved()</c> with no <c>PrintReceiptRequested</c> invocation at
    /// all, so the operator's own configured <i>print after save</i> was ignored and the customer's only paper
    /// kept understating the bill.
    ///
    /// <para>Driven through <c>ForAlter</c> deliberately — this test owns the VIEW-MODEL half and must fail if the
    /// invocation is removed even when the shell subscription is present. The shell half has its own test below.
    /// </para>
    ///
    /// <para><b>Derivation.</b> 2 × 700.11 = 1,400.22 taxable; GST is not configured so there is no tax leg and
    /// the receipt's taxable total IS the bill total. Cash tendered 1,500.00 ⇒ change 1,500.00 − 1,400.22 =
    /// 99.78.</para>
    ///
    /// <para><b>FIDELITY (R7) — an INFERENCE, recorded as one.</b> The corpus attests the SETTING
    /// (<i>"Print voucher after saving - Set to `Yes'."</i>, Book 664311548; and three other books repeat it), and
    /// attests that Ctrl+A is the save chord for an alteration — but NO corpus page states print-after-save
    /// behaviour under alteration. The bridge (the setting is a property of the voucher TYPE, so it applies to
    /// every save of that type) is our inference, not an attestation.</para>
    /// </summary>
    [AvaloniaFact]
    public void An_altered_POS_bill_raises_the_print_after_save_receipt_the_operator_configured()
    {
        var (window, vm, dir) = NewWindow();
        try
        {
            var p = SeedOnePosBill(window, vm, "POS Receipt VM Co", printAfterSave: true);

            var raised = 0;
            PosReceiptData? receipt = null;
            var open = PosBillingViewModel.ForAlter(
                p.Company, p.Bill.Id, new CompanyStorage(dir), onSaved: () => { }, onCancelled: () => { });
            Assert.Null(open.Refusal);
            var screen = open.Entry!;
            screen.PrintReceiptRequested += r => { raised++; receipt = r; };
            Assert.True(screen.PrintAfterSave);

            screen.Items[0].RateText = "700.11";
            screen.CashRow.CashTenderedText = "1500";
            Assert.True(screen.AcceptAlteration());

            Assert.Equal(1400.22m, p.Company.FindVoucher(p.Bill.Id)!.TotalDebit.Amount);
            Assert.Equal(1, raised);
            Assert.Equal(1400.22m, receipt!.TotalTaxable.Amount);
            Assert.Equal(99.78m, receipt!.Change.Amount);              // 1,500.00 − 1,400.22
            // The receipt names the SAME document, not a new one: Replace preserves the number.
            Assert.Equal(p.Company.FormatVoucherNumber(p.Company.FindVoucher(p.Bill.Id)!), receipt!.BillNumber);
        }
        finally { Close(window, dir); }
    }

    /// <summary>
    /// 🔴 <b>LAYER TWO: the alteration DOOR must subscribe.</b> Adding the invocation alone would still produce no
    /// receipt in the running app — <c>OpenPosBilling</c> wires <c>PrintReceiptRequested</c> and
    /// <c>ShowPosBillAlteration</c> did not, so the event had no consumer on this route. Driven end-to-end through
    /// the real keyboard, and it also pins that the receipt arrives as a DRILL column: the Day Book the operator
    /// came from is still beneath it, so Esc returns to their row rather than to the Gateway.
    /// </summary>
    [AvaloniaFact]
    public void The_POS_alteration_door_subscribes_so_the_receipt_actually_reaches_the_operator()
    {
        var (window, vm, dir) = NewWindow();
        try
        {
            var p = SeedOnePosBill(window, vm, "POS Receipt Shell Co", printAfterSave: true);

            OpenDayBookOn(window, vm, p.Bill.Id);
            Key(window, PhysicalKey.Enter, RawInputModifiers.Control);
            Assert.Equal(Screen.PosBilling, vm.CurrentScreen);

            vm.PosBilling!.Items[0].RateText = "700.11";
            vm.PosBilling!.CashRow.CashTenderedText = "1400.22";
            Key(window, PhysicalKey.A, RawInputModifiers.Control);

            Assert.Equal(1400.22m, p.Company.FindVoucher(p.Bill.Id)!.TotalDebit.Amount);
            Assert.Equal(Screen.PrintPreview, vm.CurrentScreen);
            Assert.NotNull(vm.PrintPreview);
            Assert.Equal(PrintPreviewViewModel.PrintKind.Receipt, vm.PrintPreview!.Kind);
            Assert.Contains("1,400.22", PreviewText(vm.PrintPreview!), StringComparison.Ordinal);

            // The cascade beneath survived: Esc comes back to the Day Book, not to the Gateway.
            vm.Back();
            Pump(window);
            Assert.Equal(Screen.Report, vm.CurrentScreen);
            Assert.NotNull(vm.Reports);
        }
        finally { Close(window, dir); }
    }

    /// <summary>
    /// ER-13 — with print-after-save OFF, an altered bill raises NOTHING and the operator lands back where they
    /// were. Without this the fix above could have been written as an unconditional print.
    /// </summary>
    [AvaloniaFact]
    public void With_print_after_save_off_an_altered_POS_bill_raises_no_receipt()
    {
        var (window, vm, dir) = NewWindow();
        try
        {
            var p = SeedOnePosBill(window, vm, "POS No Receipt Co", printAfterSave: false);

            OpenDayBookOn(window, vm, p.Bill.Id);
            Key(window, PhysicalKey.Enter, RawInputModifiers.Control);
            vm.PosBilling!.Items[0].RateText = "700.11";
            vm.PosBilling!.CashRow.CashTenderedText = "1400.22";
            Key(window, PhysicalKey.A, RawInputModifiers.Control);

            Assert.Equal(1400.22m, p.Company.FindVoucher(p.Bill.Id)!.TotalDebit.Amount);
            Assert.Null(vm.PrintPreview);
            Assert.Equal(Screen.Report, vm.CurrentScreen);
        }
        finally { Close(window, dir); }
    }

    // ==================================================================================================
    // (d) ITEM 6 — THE WORK-LOSS HALF OF THE F-KEY DEFECT (scoped to the SCREEN, not to IsAltering)
    // ==================================================================================================

    /// <summary>
    /// 🔴 <b>THE HALF A GUARD ON <c>IsAltering</c> WOULD HAVE LEFT OPEN.</b> Measured by the verifier on a
    /// half-keyed NEW Journal: one plain F8 replaced it with a blank Sales entry — <c>Line0 amount now = ''</c>,
    /// <c>Narration now = ''</c>, <c>Notice</c> and <c>Message</c> both empty. No prompt, no notice, no message.
    ///
    /// <para>The F-key rows are enabled on <c>hasCompany</c> alone and <c>OpenVoucher</c> → <c>OpenPageColumn</c>
    /// → <c>ClearSubScreens</c> sets <c>VoucherEntry = null</c> unconditionally, so there is no unsaved-work
    /// question anywhere on the path.</para>
    /// </summary>
    [AvaloniaFact]
    public void A_plain_type_F_key_does_not_silently_discard_a_half_keyed_new_entry()
    {
        var (window, vm, dir) = NewWindow();
        try
        {
            var b = SeedOneJournal(window, vm, "FKey New Entry Co");

            vm.OpenVoucher(VoucherBaseType.Journal);
            Pump(window);
            var entry = vm.VoucherEntry!;
            entry.Lines[0].SelectedLedger = b.Rent;
            entry.Lines[0].Side = DrCr.Debit;
            entry.Lines[0].AmountText = "9000.00";
            entry.Narration = "half keyed";

            Key(window, PhysicalKey.F8);                       // plain F8 — "Sales"

            Assert.Equal(Screen.VoucherEntry, vm.CurrentScreen);
            Assert.Same(entry, vm.VoucherEntry);               // the SAME screen, not a blank replacement
            Assert.Equal("Journal", vm.VoucherEntry!.Type.BaseType.ToString());
            Assert.Equal("9000.00", vm.VoucherEntry!.Lines[0].AmountText);
            Assert.Equal("half keyed", vm.VoucherEntry!.Narration);
            Assert.NotEqual(string.Empty, vm.Notice);          // …and the operator is TOLD, not left with a dead key
        }
        finally { Close(window, dir); }
    }

    /// <summary>
    /// 🔴 The alteration case — the worse half only because it ALSO tears down the report column beneath it
    /// (measured: Columns 3 → 2, <c>Reports</c> null), which the new-entry case does not have.
    /// </summary>
    [AvaloniaFact]
    public void A_plain_type_F_key_does_not_discard_an_unsaved_alteration_or_the_report_beneath_it()
    {
        var (window, vm, dir) = NewWindow();
        try
        {
            var b = SeedOneJournal(window, vm, "FKey Alteration Co");
            OpenDayBookOn(window, vm, b.Journal.Id);
            Key(window, PhysicalKey.Enter, RawInputModifiers.Control);
            var alter = vm.VoucherEntry!;
            Assert.True(alter.IsAltering);
            var columnsBefore = vm.Columns.Count;

            foreach (var line in alter.Lines) line.AmountText = "9000.00";

            Key(window, PhysicalKey.F8);

            Assert.Equal(Screen.VoucherEntry, vm.CurrentScreen);
            Assert.Same(alter, vm.VoucherEntry);
            Assert.True(vm.VoucherEntry!.IsAltering);
            Assert.Equal("9000.00", vm.VoucherEntry!.Lines[0].AmountText);
            Assert.NotNull(vm.Reports);                        // the Day Book column is still beneath it
            Assert.Equal(columnsBefore, vm.Columns.Count);
            Assert.NotEqual(string.Empty, vm.Notice);

            // Nothing was posted and nothing was lost: the book still holds the original figure.
            Assert.Single(vm.Company!.Vouchers);
            Assert.Equal(8431.55m, Closing(vm.Company!, b.Rent));
        }
        finally { Close(window, dir); }
    }

    /// <summary>
    /// ER-13 — the guard is scoped to UNSAVED WORK on the voucher-entry screen, so an UNTOUCHED entry screen still
    /// switches voucher type on a type F-key exactly as before. Without this the fix would be a behaviour
    /// narrowing dressed as a defect fix.
    /// </summary>
    [AvaloniaFact]
    public void A_plain_type_F_key_still_switches_an_untouched_entry_screen()
    {
        var (window, vm, dir) = NewWindow();
        try
        {
            SeedOneJournal(window, vm, "FKey Untouched Co");

            vm.OpenVoucher(VoucherBaseType.Journal);
            Pump(window);
            Assert.Equal(VoucherBaseType.Journal, vm.VoucherEntry!.Type.BaseType);

            Key(window, PhysicalKey.F8);                       // nothing keyed — F8 may switch

            Assert.Equal(Screen.VoucherEntry, vm.CurrentScreen);
            Assert.Equal(VoucherBaseType.Sales, vm.VoucherEntry!.Type.BaseType);
            Assert.Equal(string.Empty, vm.Notice);
        }
        finally { Close(window, dir); }
    }

    /// <summary>
    /// ER-13 — off the voucher-entry screen the type F-keys are untouched: from the Gateway, F7 still opens a
    /// Journal. The guard is a SCREEN gate, not a global one.
    /// </summary>
    [AvaloniaFact]
    public void A_plain_type_F_key_still_opens_its_voucher_from_a_menu_screen()
    {
        var (window, vm, dir) = NewWindow();
        try
        {
            SeedOneJournal(window, vm, "FKey Menu Co");
            Assert.Equal(Screen.Gateway, vm.CurrentScreen);

            Key(window, PhysicalKey.F7);                       // plain F7 — "Journal"

            Assert.Equal(Screen.VoucherEntry, vm.CurrentScreen);
            Assert.Equal(VoucherBaseType.Journal, vm.VoucherEntry!.Type.BaseType);
        }
        finally { Close(window, dir); }
    }

    // ==================================================================================================
    // (e) UI TRUNCATION — the class this review did not hunt, on the channel these slices stream into
    // ==================================================================================================

    /// <summary>
    /// 🔴 <b>THE WINDOW-LEVEL NOTICE BAR MUST WRAP.</b> It shipped as <c>Height="26"</c> with a single
    /// <c>TextTrimming="CharacterEllipsis"</c> line, and Phase 10.11's lifecycle refusals are SENTENCES that end
    /// with the operator's instructions — so the half thrown away was the actionable half, on the one channel that
    /// exists because these refusals are otherwise invisible (the report page's <c>DataTemplate</c> is typed to
    /// <c>ReportsViewModel</c> and has no <c>Message</c> property).
    ///
    /// <para><b>MEASURED, headless through Skia (<c>UseSkia()</c> + <c>UseHeadlessDrawing = false</c>,
    /// <c>CaptureRenderedFrame</c> to PNG, PNGs read), then reverted.</b> At <b>1280×720 DIP — which is
    /// 1920×1080 @150%, an ordinary full-HD laptop</b> — the 372-character GST shape refusal was cut at
    /// <i>"…Alter re-computes the A"</i>; at <b>1920×1080 DIP</b> the same sentence was still cut, at
    /// <i>"…read the stamped figures. C"</i>. After the fix the LONGEST refusal that can reach this bar (the
    /// 481-character SALES ITEM INVOICE sentence) renders complete in three wrapped lines at both 1280 and 1024
    /// DIP.</para>
    ///
    /// <para>Pinned STATICALLY — parsed as plain XML, no Avalonia, no Skia, no headless platform — for the reason
    /// <see cref="XamlLayoutInvariantTests"/> gives: a render harness depends on discipline that has already
    /// lapsed once on this project, and a pin that cannot be defeated by <c>TestAppBuilder</c> drifting is worth
    /// more here than a prettier one that can.</para>
    /// </summary>
    [Fact]
    public void The_window_level_notice_bar_wraps_its_refusal_sentences_instead_of_trimming_them()
    {
        var thisFile = ThisFilePath();
        var repoRoot = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(thisFile)!, "..", ".."));
        var axaml = Path.Combine(repoRoot, "src", "Apex.Desktop", "Views", "MainWindow.axaml");
        Assert.True(File.Exists(axaml), $"MainWindow.axaml not found at '{axaml}'.");

        var doc = System.Xml.Linq.XDocument.Load(axaml);
        System.Xml.Linq.XNamespace av = "https://github.com/avaloniaui";

        // The bar is identified by its own slate background, the same way the AXAML comment beside it does — the
        // amber Border on the identical Grid.Row is the WI-11 question channel and is deliberately untouched.
        var bar = Assert.Single(doc.Root!.DescendantsAndSelf(),
            e => e.Name == av + "Border"
              && e.Attribute("Background")?.Value == "#3A4A5A"
              && (e.Attribute("IsVisible")?.Value ?? string.Empty).Contains("Binding Notice"));

        Assert.Null(bar.Attribute("Height"));                  // a fixed height is what clipped it
        Assert.Equal("26", bar.Attribute("MinHeight")?.Value);  // …and the one-line case stays pixel-identical

        var text = Assert.Single(bar.Descendants(av + "TextBlock"),
            t => (t.Attribute("Text")?.Value ?? string.Empty).Contains("Binding Notice"));
        Assert.Equal("Wrap", text.Attribute("TextWrapping")?.Value);
        Assert.Equal("4", text.Attribute("MaxLines")?.Value);   // capped so a notice cannot swallow the cascade
    }

    private static string ThisFilePath([System.Runtime.CompilerServices.CallerFilePath] string p = "") => p;
}
