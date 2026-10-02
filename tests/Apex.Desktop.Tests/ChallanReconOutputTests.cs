using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Apex.Desktop.Services;
using Apex.Desktop.ViewModels;
using Apex.Desktop.Views;
using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Io;
using Apex.Ledger.Services;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Xunit;

using DomainLedger = Apex.Ledger.Domain.Ledger;

namespace Apex.Desktop.Tests;

/// <summary>
/// <b>Census 6.31 and 6.38 — the two Challan Reconciliations can now leave the screen.</b>
///
/// <para>🔴 <b>What was wrong.</b> Both pages were filed as "output dead ends": the engine reconciliation was
/// right, the grid was right, and there was no way to get either off the screen. Neither view-model wrote a
/// file and neither was a report page, so <c>IsExportablePage</c> and <c>IsPrintablePage</c> were both false on
/// them and E / Alt+E and P / Ctrl+P did nothing at all — not a refusal an operator could learn from, just a key
/// that had no effect. A challan reconciliation exists to be handed to somebody (an auditor, a client, the
/// person who has to pay the shortfall), so a version of it that can only be looked at has done half its job.</para>
///
/// <para><b>Why these tests press keys instead of calling the methods.</b> This project has three features on
/// file whose only callers were test files — code that was complete and that no user could reach. Calling
/// <c>vm.OpenExport()</c> directly would prove exactly nothing about whether a user can export: the gate is
/// <c>IsExportablePage</c>, evaluated inside <c>MainWindow</c>'s own key tunnel. So the export and print cases
/// here go through <see cref="MainWindow"/>'s real <c>KeyDown</c> handler, with the real key and the real
/// modifiers, and the export case then writes a real file to disk and reads its bytes back.</para>
/// </summary>
public sealed class ChallanReconOutputTests : IDisposable
{
    private const string ValidTan = "MUMA12345B";
    private const string DeducteePan = "AAPFU0939F";
    private const string CollecteePan = "AAACC1206D";

    private readonly string _tempDir;
    private readonly string _outDir;
    private readonly CompanyStorage _storage;

    public ChallanReconOutputTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "ApexChallanOut_" + Guid.NewGuid().ToString("N"));
        _outDir = Path.Combine(_tempDir, "out");
        Directory.CreateDirectory(_outDir);
        _storage = new CompanyStorage(_tempDir);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true); }
        catch (IOException) { /* a held handle must not fail the assertion under test */ }
    }

    // ---------------------------------------------------------------- fixtures

    private static DomainLedger AddLedger(Company c, string name, string group, bool openingIsDebit)
    {
        var l = new DomainLedger(Guid.NewGuid(), name, c.FindGroupByName(group)!.Id, Money.Zero, openingIsDebit);
        c.AddLedger(l);
        return l;
    }

    /// <summary>A TDS-enabled company carrying one posted §194J(b) withholding, so the reconciliation has a row.</summary>
    private MainWindowViewModel TdsCompanyWithAWithholding()
    {
        var vm = new MainWindowViewModel(_storage);
        vm.NewCompanyName = "Challan Out Co";
        vm.CreateCompany();

        var c = vm.Company!;
        new TdsTcsService(c).EnableTds(new TdsConfig { Tan = ValidTan });

        var fees = AddLedger(c, "Professional Fees", "Indirect Expenses", true);
        var vendor = AddLedger(c, "Acme Consultants", "Sundry Creditors", false);
        fees.TdsApplicable = true;
        fees.TdsNatureOfPaymentId = c.FindNatureOfPaymentByCode("194J(b)")!.Id;
        vendor.DeducteeType = DeducteeType.Firm;
        vendor.PartyPan = DeducteePan;

        vm.OpenVoucher(VoucherBaseType.Journal);
        var e = vm.VoucherEntry!;
        e.Lines[0].SelectedLedger = fees; e.Lines[0].Side = DrCr.Debit; e.Lines[0].AmountText = "100000";
        e.Lines[1].SelectedLedger = vendor; e.Lines[1].Side = DrCr.Credit; e.Lines[1].AmountText = "100000";
        e.Recalculate();
        Assert.True(e.ShowTdsPanel, "fixture is vacuous unless the withholding actually engages");
        Assert.True(e.Accept());

        vm.ShowGateway();
        return vm;
    }

    /// <summary>A TCS-enabled company — the mirror row 6.38 is graded on.</summary>
    private MainWindowViewModel TcsCompany()
    {
        var vm = new MainWindowViewModel(_storage);
        vm.NewCompanyName = "Challan Out Tcs Co";
        vm.CreateCompany();
        new TdsTcsService(vm.Company!).EnableTcs(new TcsConfig { Tan = ValidTan });
        vm.ShowGateway();
        return vm;
    }

    private static MainWindow Show(MainWindowViewModel vm)
    {
        var win = new MainWindow { DataContext = vm };
        win.MinWidth = 640; win.MinHeight = 480;
        win.Width = 1400; win.Height = 900;
        win.Show();
        win.Width = 1400; win.Height = 900;
        Dispatcher.UIThread.RunJobs();
        Dispatcher.UIThread.RunJobs();
        return win;
    }

    private static void Press(MainWindow win, Key key, KeyModifiers modifiers = KeyModifiers.None)
    {
        win.RaiseEvent(new KeyEventArgs
        {
            RoutedEvent = InputElement.KeyDownEvent,
            Key = key,
            KeyModifiers = modifiers,
            Source = win,
        });
        Dispatcher.UIThread.RunJobs();
    }

    // ---------------------------------------------------------------- 1. the snapshot itself

    /// <summary>
    /// The TDS reconciliation describes its own grid: five columns, three of them numeric, a Grand Total row
    /// and the cash-basis caveat. The numeric flags are load-bearing — a spreadsheet that stores "10,000.00" as
    /// text cannot re-add the column, and re-adding the column is the first thing anyone checking a
    /// reconciliation does.
    /// </summary>
    [Fact]
    public void Tds_reconciliation_snapshots_its_grid_with_numeric_money_columns_and_the_basis_note()
    {
        var vm = TdsCompanyWithAWithholding();
        var page = new ChallanReconciliationViewModel(vm.Company!);
        Assert.NotEmpty(page.Rows);   // not vacuous

        var snap = page.ToMasterListSnapshot();

        Assert.Equal("Challan Reconciliation", snap.Title);
        Assert.Equal(
            new[] { "Section", "Deducted", "Deposited", "Remaining", "Status" },
            snap.Columns.Select(c => c.Caption).ToArray());
        Assert.Equal(
            new[] { false, true, true, true, false },
            snap.Columns.Select(c => c.IsNumeric).ToArray());

        // Every on-screen section row survives, the total is carried rather than left to the reader,
        // and the caveat rides along.
        Assert.Contains(snap.Rows, r => r[0] == "194J(b)");
        var total = snap.Rows.Single(r => r[0] == "Grand Total");
        Assert.Equal(page.TotalDeducted, total[1]);
        Assert.Equal(page.TotalRemaining, total[3]);
        Assert.Contains(snap.Rows, r => r[0] == page.BasisNote);
    }

    /// <summary>The TCS mirror keeps the same shape — see the doc comment on its snapshot for why that matters.</summary>
    [Fact]
    public void Tcs_reconciliation_snapshots_the_same_shape_with_its_own_captions()
    {
        var vm = TcsCompany();
        var snap = new TcsChallanReconciliationViewModel(vm.Company!).ToMasterListSnapshot();

        Assert.Equal("TCS Challan Reconciliation", snap.Title);
        Assert.Equal(
            new[] { "Collection Code", "Collected", "Deposited", "Remaining", "Status" },
            snap.Columns.Select(c => c.Caption).ToArray());
        Assert.Equal(
            new[] { false, true, true, true, false },
            snap.Columns.Select(c => c.IsNumeric).ToArray());
        Assert.Contains(snap.Rows, r => r[0] == "Grand Total");
    }

    // ---------------------------------------------------------------- 2. reachable from the keyboard

    /// <summary>
    /// 🔴 <b>The reachability assertion.</b> Alt+R opens the reconciliation and bare E then opens the Export
    /// panel — both through <see cref="MainWindow"/>'s own key handler, because the gate that was false is
    /// evaluated there. On the pre-slice build the second press does nothing: <c>IsExportablePage</c> is false
    /// and <c>OpenExport</c> is never called.
    /// </summary>
    [AvaloniaFact]
    public void Alt_R_then_E_opens_the_export_panel_over_the_tds_reconciliation()
    {
        var vm = TdsCompanyWithAWithholding();
        var win = Show(vm);

        Press(win, Key.R, KeyModifiers.Alt);
        Assert.Equal(Screen.ChallanReconciliation, vm.CurrentScreen);
        Assert.True(vm.IsExportablePage);
        Assert.True(vm.IsPrintablePage);

        Press(win, Key.E);
        Assert.Equal(Screen.Export, vm.CurrentScreen);
        Assert.NotNull(vm.ExportPanel);
        Assert.Equal("Challan Reconciliation", vm.ExportPanel!.DocumentTitle);

        win.Close();
    }

    /// <summary>
    /// P opens a print preview of the same page. Print and Export are separate gestures and were separately
    /// dead; closing only one of them would have left the row honestly PARTIAL.
    /// </summary>
    [AvaloniaFact]
    public void P_opens_a_print_preview_of_the_tds_reconciliation()
    {
        var vm = TdsCompanyWithAWithholding();
        var win = Show(vm);

        Press(win, Key.R, KeyModifiers.Alt);
        Press(win, Key.P);

        Assert.Equal(Screen.PrintPreview, vm.CurrentScreen);
        Assert.NotNull(vm.PrintPreview);

        // `Title` is the constant column caption ("Print Preview"), so it proves nothing about WHAT is being
        // previewed — `ReportTitle` is the document's own heading and is what distinguishes this preview from
        // a preview of some other page that happened to be open.
        Assert.Equal("Challan Reconciliation", vm.PrintPreview!.ReportTitle);

        win.Close();
    }

    // ---------------------------------------------------------------- 3. bytes actually reach the disk

    /// <summary>
    /// 🔴 <b>A file, on disk, with the figures in it.</b> "Exportable" is a property; this asserts the product.
    /// CSV is read back as text so the assertion is about content, not just a non-zero length — a writer that
    /// produced an empty or header-only file would pass a size check and fail this.
    /// </summary>
    [Fact]
    public void Exporting_the_tds_reconciliation_writes_a_csv_carrying_the_section_and_the_total()
    {
        var vm = TdsCompanyWithAWithholding();
        var page = new ChallanReconciliationViewModel(vm.Company!);

        var panel = new ExportViewModel(
            page.Title,
            () => MasterListTabularProjector.ProjectSource(page),
            projectPrint: null,
            _outDir,
            new DateTime(2025, 6, 1, 10, 0, 0),
            writeBytes: null)
        {
            Format = ExportFormat.Csv,
            FileName = "tds-recon",
            AppendTimestamp = false,
        };

        Assert.True(panel.Apply(), panel.Status);

        var path = Path.Combine(_outDir, "tds-recon.csv");
        Assert.True(File.Exists(path));
        var text = File.ReadAllText(path);

        Assert.Contains("Section", text);
        Assert.Contains("194J(b)", text);
        Assert.Contains("Grand Total", text);
        Assert.Contains("Remaining", text);
        // The money reached the file as a bare decimal a spreadsheet can sum, not a grouped display string.
        Assert.Contains("10000.00", text);
    }

    /// <summary>
    /// The PDF path renders too, and — because Print and Export share
    /// <see cref="ExportViewModel.TabularToPrint"/> — what prints is what exports. A byte-length assertion is
    /// enough here only because the shared-layout claim is what is actually under test; the CSV case above
    /// carries the content assertion.
    /// </summary>
    [Fact]
    public void Exporting_the_tcs_reconciliation_as_pdf_writes_a_real_document()
    {
        var vm = TcsCompany();
        var page = new TcsChallanReconciliationViewModel(vm.Company!);

        var panel = new ExportViewModel(
            page.Title,
            () => MasterListTabularProjector.ProjectSource(page),
            projectPrint: null,
            _outDir,
            new DateTime(2025, 6, 1, 10, 0, 0),
            writeBytes: null)
        {
            Format = ExportFormat.Pdf,
            FileName = "tcs-recon",
            AppendTimestamp = false,
        };

        Assert.True(panel.Apply(), panel.Status);

        var path = Path.Combine(_outDir, "tcs-recon.pdf");
        var bytes = File.ReadAllBytes(path);
        Assert.True(bytes.Length > 500, $"a rendered PDF should not be {bytes.Length} bytes");
        Assert.Equal(new byte[] { 0x25, 0x50, 0x44, 0x46 }, bytes.Take(4).ToArray());   // "%PDF"
    }
}
