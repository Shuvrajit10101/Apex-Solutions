using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Apex.Desktop.Services;
using Apex.Desktop.ViewModels;
using Apex.Desktop.Views;
using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Io;
using Apex.Ledger.Services;
using Xunit;
using DomainLedger = Apex.Ledger.Domain.Ledger;

namespace Apex.Desktop.Tests;

/// <summary>
/// Wave-7 adversarial review of track <c>dg</c> (census 6.16 / 6.19), written from OUTSIDE the branch's own
/// fixture. Differences from the builder's: a different financial year and return period; an INTER-STATE (IGST)
/// supplier beside the local one; TWO matched rows; a partial mismatch whose variance is NEGATIVE (books above
/// portal); an in-books-only purchase with NO supplier doc number; a reverse-charge portal line; the page reached
/// by Alt+G and typing (not by calling the opener); and the multi-registration case read back off a book RELOADED
/// through a fresh <see cref="CompanyStorage"/> handle.
/// </summary>
public sealed class ReviewDgGstr2bReconOutsideFixtureTests : IDisposable
{
    private const string GstinHome = "27AAPFU0939F1ZV";
    private const string GstinLocal = "27AAACC1206D1ZM";
    private static readonly string GstinKarnataka = "29AABCU9603R1Z" + Gstin.ComputeCheckDigit("29AABCU9603R1Z0");
    private static readonly DateOnly FyStart = new(2025, 4, 1);
    private static readonly DateOnly DocDate = new(2025, 6, 10);

    private readonly string _tempDir;
    private readonly string _outDir;
    private readonly CompanyStorage _storage;

    public ReviewDgGstr2bReconOutsideFixtureTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "ApexReviewDg_" + Guid.NewGuid().ToString("N"));
        _outDir = Path.Combine(_tempDir, "out");
        Directory.CreateDirectory(_outDir);
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

    // =============================================================== 6.16 — print via the go-to route

    [AvaloniaFact]
    public void Review_alt_g_then_ctrl_p_prints_every_bucket_including_igst_and_a_negative_variance()
    {
        var vm = FiveRowBook("Rv Print");
        var win = Show(vm);

        GoTo(win, vm, "GSTR-2B Reconciliation");
        Assert.Equal(Screen.Gstr2bReconReport, vm.CurrentScreen);
        var page = vm.Gstr2bReconReport!;
        Assert.Equal(2, page.MatchedCount);
        Assert.Equal(1, page.PartialCount);
        Assert.Equal(1, page.InPortalOnlyCount);
        Assert.Equal(1, page.InBooksOnlyCount);

        win.KeyPressQwerty(PhysicalKey.P, RawInputModifiers.Control);
        Pump(win);
        Assert.Equal(Screen.PrintPreview, vm.CurrentScreen);
        var drawn = DrawnStrings(vm.PrintPreview!.PdfBytes);

        foreach (var h in new[] { "Matched (2)", "Partial mismatch (1)", "In portal only (1)", "In books only (1)" })
            Assert.Contains(h, drawn);
        Assert.Contains($"{GstinLocal}  R-100", drawn);
        Assert.Contains($"{GstinKarnataka}  KA-7", drawn);
        Assert.Contains($"{GstinLocal}  R-101", drawn);
        Assert.Contains($"{GstinKarnataka}  KA-9", drawn);
        // the in-books-only row with no supplier doc number: its em dash is WinAnsi 0x97 in the content stream
        Assert.Contains($"{GstinLocal}  \u0097", drawn);
        // the IGST-only rows carry their tax
        Assert.Contains(drawn, s => s.Contains("720.00"));
        Assert.Contains(drawn, s => s.Contains("1170.00"));
        // the negative variance survives with its sign
        Assert.Contains(drawn, s => s.Contains("-200.00"));
        Assert.Contains(drawn, s => s.Contains("-36.00"));
        // the RCM portal line lands in no bucket (documented T2-22 caveat) and is not printed
        Assert.DoesNotContain(drawn, s => s.Contains("RC-5"));
        win.Close();
    }

    // =============================================================== 6.16 — export CSV via bare E? via Ctrl+E

    [AvaloniaFact]
    public void Review_ctrl_e_csv_rows_carry_their_own_figures_and_the_negative_variance()
    {
        var vm = FiveRowBook("Rv Csv");
        var win = Show(vm);
        GoTo(win, vm, "GSTR-2B Reconciliation");
        Assert.Equal(Screen.Gstr2bReconReport, vm.CurrentScreen);

        win.KeyPressQwerty(PhysicalKey.E, RawInputModifiers.Control);
        Pump(win);
        Assert.Equal(Screen.Export, vm.CurrentScreen);
        var panel = vm.ExportPanel!;
        panel.Folder = _outDir;
        panel.Format = ExportFormat.Csv;
        panel.FileName = "rv";
        panel.AppendTimestamp = false;
        win.KeyPressQwerty(PhysicalKey.A, RawInputModifiers.Control);
        Pump(win);

        var path = Path.Combine(_outDir, "rv.csv");
        Assert.True(File.Exists(path), panel.Status);
        var lines = File.ReadAllLines(path, Encoding.UTF8);

        string Row(string id) => lines.Single(l => l.Contains(id, StringComparison.Ordinal));
        Assert.Contains("10000.00", Row("R-100"));
        Assert.Contains("1800.00", Row("R-100"));
        Assert.Contains("4000.00", Row("KA-7"));
        Assert.Contains("720.00", Row("KA-7"));
        var partial = Row("R-101");
        Assert.Contains("1800.00", partial);
        Assert.Contains("324.00", partial);
        Assert.Contains("-200.00", partial);
        Assert.Contains("-36.00", partial);
        Assert.Contains("6500.00", Row("KA-9"));
        Assert.Contains("1170.00", Row("KA-9"));
        var booksOnly = Row($"{GstinLocal}  —");
        Assert.Contains("3300.00", booksOnly);
        Assert.Contains("594.00", booksOnly);
        Assert.DoesNotContain(lines, l => l.Contains("RC-5", StringComparison.Ordinal));
        win.Close();
    }

    // =============================================================== multi-registration, after a RELOAD

    [AvaloniaFact]
    public void Review_two_registration_book_reloaded_through_a_fresh_store_exports_the_branch_statement_only()
    {
        string name;
        Guid branchId;
        {
            var vm = RegularBook("Rv TwoReg");
            var c = vm.Company!;
            name = c.Name;
            var branch = new GstRegistration(Guid.NewGuid(), "Karnataka Branch", "29",
                "29AAPFU0939F1Z" + Gstin.ComputeCheckDigit("29AAPFU0939F1Z0"), GstRegistrationType.Regular, FyStart);
            c.Gst!.AddRegistration(branch);
            c.Gst!.EnsureValid();
            branchId = branch.Id;
            Purchase(c, "R-100", 10000m, interState: false);
            Purchase(c, "KA-7", 4000m, interState: true, registrationId: branch.Id);
            Purchase(c, "KA-8", 2500m, interState: true, registrationId: branch.Id); // branch books only
            Import2b(c, null, GstinHome, "p", L(GstinLocal, "R-100", 1_000_000, 0, 90_000));
            Import2b(c, branch.Id, branch.Gstin!, "b", L(GstinKarnataka, "KA-7", 400_000, 72_000, 0));
            _storage.Save(c);
        }

        // A FRESH store handle over the same directory, and a fresh shell.
        var storage2 = new CompanyStorage(_tempDir);
        var vm2 = new MainWindowViewModel(storage2);
        var win = Show(vm2);
        vm2.ShowCompanySelect();
        vm2.Menu.Single(m => m.Label == name).Activate();
        Pump(win);
        Assert.NotNull(vm2.Company);
        Assert.True(vm2.Company!.Gst!.IsMultiRegistration);

        GoTo(win, vm2, "GSTR-2B Reconciliation");
        Assert.Equal(Screen.Gstr2bReconReport, vm2.CurrentScreen);
        var page = vm2.Gstr2bReconReport!;
        page.SelectedSnapshot = page.Snapshots.Single(s => s.Snapshot.OwningRegistrationId == branchId);
        Pump(win);

        Assert.Null(page.Message);
        Assert.Equal(1, page.MatchedCount);
        Assert.Equal(1, page.InBooksOnlyCount);
        Assert.Equal(0, page.InPortalOnlyCount);
        Assert.Contains("Karnataka Branch", page.Subtitle, StringComparison.Ordinal);

        win.KeyPressQwerty(PhysicalKey.E, RawInputModifiers.Control);
        Pump(win);
        var panel = vm2.ExportPanel!;
        panel.Folder = _outDir;
        panel.Format = ExportFormat.Csv;
        panel.FileName = "rv2";
        panel.AppendTimestamp = false;
        win.KeyPressQwerty(PhysicalKey.A, RawInputModifiers.Control);
        Pump(win);
        var csv = File.ReadAllText(Path.Combine(_outDir, "rv2.csv"), Encoding.UTF8);

        Assert.Contains("KA-7", csv, StringComparison.Ordinal);
        Assert.Contains("KA-8", csv, StringComparison.Ordinal);
        Assert.Contains("Registration: Karnataka Branch", csv, StringComparison.Ordinal);
        Assert.DoesNotContain("R-100", csv, StringComparison.Ordinal);
        Assert.DoesNotContain("10000.00", csv, StringComparison.Ordinal);
        foreach (var r in page.Rows) Assert.Contains($"{r.Supplier}  {r.DocNo}", csv, StringComparison.Ordinal);
        win.Close();
    }

    // =============================================================== T2-151(1) with a February as-of

    [AvaloniaFact]
    public void Review_aged_book_with_a_february_last_voucher_opens_itc_set_off_on_that_year_via_go_to()
    {
        var vm = RegularBook("Rv Aged", fyStart: new DateOnly(2023, 4, 1), periodicity: GstReturnPeriodicity.Quarterly);
        var c = vm.Company!;
        var sales = Add(c, "Sales", "Sales Accounts", false);
        var debtor = Add(c, "Local Debtor", "Sundry Debtors", true);
        debtor.PartyGst = new PartyGstDetails
        { RegistrationType = GstRegistrationType.Regular, Gstin = GstinLocal, StateCode = "27" };
        var tax = new GstService(c).ComputeInvoiceTax(
            new[] { new GstService.TaxableLine(Money.FromRupees(2000m), 1800) }, false, GstTaxDirection.Output);
        var lines = new List<EntryLine>
        {
            new(debtor.Id, Money.FromRupees(2360m), DrCr.Debit),
            new(sales.Id, Money.FromRupees(2000m), DrCr.Credit),
        };
        lines.AddRange(tax.TaxLines);
        new LedgerService(c).Post(new Voucher(Guid.NewGuid(), c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Sales).Id,
            new DateOnly(2026, 2, 15), lines, number: 1, partyId: debtor.Id));
        _storage.Save(c);
        vm.ShowGateway();

        var win = Show(vm);
        foreach (var (label, kind) in new[]
                 {
                     ("ITC Set-Off", ReportKind.ItcSetOffProjection),
                     ("Electronic Ledgers", ReportKind.ElectronicLedgers),
                     ("QRMP / IFF", ReportKind.QrmpIffCadence),
                 })
        {
            vm.ShowGateway();
            Pump(win);
            GoTo(win, vm, label);
            Assert.Equal(Screen.Report, vm.CurrentScreen);
            var reports = vm.Reports!;
            Assert.Equal(kind, reports.Kind);
            Assert.False(reports.IsPayrollEmpty, $"{kind}: {reports.PayrollEmptyNote}");
            Assert.Contains("01-Apr-2025", reports.Subtitle);
            Assert.Contains("31-Mar-2026", reports.Subtitle);
        }
        win.Close();
    }

    // =============================================================== fixtures

    private MainWindowViewModel RegularBook(string name, DateOnly? fyStart = null,
        GstReturnPeriodicity periodicity = GstReturnPeriodicity.Monthly)
    {
        var fy = fyStart ?? FyStart;
        var vm = new MainWindowViewModel(_storage);
        vm.NewCompanyName = name + " " + Guid.NewGuid().ToString("N")[..6];
        vm.CreateCompany();
        var c = vm.Company!;
        c.FinancialYearStart = fy;
        c.BooksBeginFrom = fy;
        new GstService(c).EnableGst(new GstConfig
        {
            HomeStateCode = "27",
            Gstin = GstinHome,
            RegistrationType = GstRegistrationType.Regular,
            ApplicableFrom = fy,
            Periodicity = periodicity,
        });
        Add(c, "Purchases", "Purchase Accounts", true);
        var local = Add(c, "Local Supplier", "Sundry Creditors", false);
        local.PartyGst = new PartyGstDetails
        { RegistrationType = GstRegistrationType.Regular, Gstin = GstinLocal, StateCode = "27" };
        var ka = Add(c, "Karnataka Supplier", "Sundry Creditors", false);
        ka.PartyGst = new PartyGstDetails
        { RegistrationType = GstRegistrationType.Regular, Gstin = GstinKarnataka, StateCode = "29" };
        vm.ShowGateway();
        return vm;
    }

    private MainWindowViewModel FiveRowBook(string name)
    {
        var vm = RegularBook(name);
        var c = vm.Company!;
        Purchase(c, "R-100", 10000m, interState: false);     // matched (CGST+SGST)
        Purchase(c, "KA-7", 4000m, interState: true);        // matched (IGST)
        Purchase(c, "R-101", 2000m, interState: false);      // partial: portal says 1,800
        Purchase(c, null, 3300m, interState: false);         // in books only, NO doc number
        Import2b(c, null, GstinHome, "five",
            L(GstinLocal, "R-100", 1_000_000, 0, 90_000),
            L(GstinKarnataka, "KA-7", 400_000, 72_000, 0),
            L(GstinLocal, "R-101", 180_000, 0, 16_200),
            L(GstinKarnataka, "KA-9", 650_000, 117_000, 0),   // in portal only
            L(GstinLocal, "RC-5", 123_400, 0, 11_106, reverseCharge: true));
        _storage.Save(c);
        vm.ShowGateway();
        return vm;
    }

    private static void Purchase(Company c, string? docNo, decimal taxable, bool interState, Guid? registrationId = null)
    {
        var purchases = c.Ledgers.First(l => l.Name == "Purchases");
        var supplier = c.Ledgers.First(l => l.Name == (interState ? "Karnataka Supplier" : "Local Supplier"));
        var tax = new GstService(c).ComputeInvoiceTax(
            new[] { new GstService.TaxableLine(Money.FromRupees(taxable), 1800) }, interState, GstTaxDirection.Input);
        var lines = new List<EntryLine>
        {
            new(purchases.Id, Money.FromRupees(taxable), DrCr.Debit),
            new(supplier.Id, Money.FromRupees(taxable * 1.18m), DrCr.Credit),
        };
        lines.AddRange(tax.TaxLines);
        var type = c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Purchase).Id;
        new LedgerService(c).Post(new Voucher(Guid.NewGuid(), type, DocDate, lines, partyId: supplier.Id,
            narration: docNo) { GstRegistrationId = registrationId });
    }

    private static Gstr2bLine L(string gstin, string docNo, long taxable, long igst, long half, bool reverseCharge = false) => new(
        Guid.NewGuid(), gstin, "Supplier", Gstr2bDocType.B2b, docNo, Gstr2bReconciler.NormaliseDocNo(docNo), DocDate,
        gstin[..2], taxableValuePaisa: taxable, igstPaisa: igst, cgstPaisa: half, sgstPaisa: half, cessPaisa: 0,
        itcAvailable: !reverseCharge, itcUnavailableReason: null, reverseCharge: reverseCharge);

    private static void Import2b(Company c, Guid? registrationId, string recipient, string hash, params Gstr2bLine[] lines)
        => c.AddGstr2bSnapshot(new Gstr2bSnapshot(
            Guid.NewGuid(), GstStatementType.Gstr2b, "2025-06", recipient, new DateOnly(2025, 7, 14),
            sourceFileHash: "rv-" + hash, importedAt: new DateTimeOffset(2025, 7, 15, 0, 0, 0, TimeSpan.Zero),
            summaryIgstPaisa: lines.Sum(l => l.IgstPaisa), summaryCgstPaisa: lines.Sum(l => l.CgstPaisa),
            summarySgstPaisa: lines.Sum(l => l.SgstPaisa), summaryCessPaisa: 0, lines: lines,
            gstRegistrationId: registrationId));

    private static DomainLedger Add(Company c, string name, string groupName, bool debit)
    {
        var group = c.FindGroupByName(groupName) ?? throw new InvalidOperationException(groupName);
        var ledger = new DomainLedger(Guid.NewGuid(), name, group.Id, Money.Zero, openingIsDebit: debit);
        c.AddLedger(ledger);
        return ledger;
    }

    // =============================================================== harness

    private static void GoTo(MainWindow win, MainWindowViewModel vm, string label)
    {
        win.KeyPressQwerty(PhysicalKey.G, RawInputModifiers.Alt);
        Pump(win);
        var box = Descendants(win).OfType<TextBox>().First(t => t.Name == "GoToSearchBox");
        box.Text = label;
        Pump(win);
        Assert.NotNull(vm.GoTo!.Selected);
        Assert.Equal(label, vm.GoTo!.Selected!.Label);
        win.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        Pump(win);
    }

    private static IEnumerable<Visual> Descendants(Visual v)
    {
        foreach (var c in v.GetVisualChildren())
        {
            yield return c;
            foreach (var g in Descendants(c)) yield return g;
        }
    }

    private static MainWindow Show(MainWindowViewModel vm)
    {
        var win = new MainWindow { DataContext = vm, Width = 1400, Height = 900 };
        win.Show();
        Pump(win);
        return win;
    }

    private static void Pump(Window w)
    {
        Dispatcher.UIThread.RunJobs();
        w.Measure(new Size(w.Width, w.Height));
        w.Arrange(new Rect(0, 0, w.Width, w.Height));
        Dispatcher.UIThread.RunJobs();
    }

    private static List<string> DrawnStrings(byte[] pdf)
    {
        string text = Encoding.Latin1.GetString(pdf);
        var drawn = new List<string>();
        int i = 0;
        while (true)
        {
            int close = text.IndexOf(") Tj", i, StringComparison.Ordinal);
            if (close < 0) break;
            int open = close;
            do { open = text.LastIndexOf('(', open - 1); }
            while (open > 0 && text[open - 1] == '\\');
            if (open < 0) break;
            drawn.Add(text.Substring(open + 1, close - open - 1)
                .Replace("\\(", "(").Replace("\\)", ")").Replace("\\\\", "\\"));
            i = close + 4;
        }
        return drawn;
    }
}
