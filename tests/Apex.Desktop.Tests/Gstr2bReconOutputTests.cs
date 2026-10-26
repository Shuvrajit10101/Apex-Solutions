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
/// 🔴 <b>Census 6.16 — the GSTR-2B reconciliation can be printed and exported.</b> The row was regraded down
/// because <i>"the screen can be neither printed nor exported, so a reconciliation an operator cannot hand to an
/// auditor is graded as reaching the whole capability."</i> <c>Gstr2bReconReportViewModel</c> now implements
/// <see cref="IMasterListExportSource"/>, which is what the shell's <c>TopMasterExportSource()</c> reads.
///
/// <para><b>Every test here drives the real <see cref="MainWindow"/> key tunnel with the real chord</b> (Ctrl+P,
/// Ctrl+E, Alt+E then the menu's Current row, Ctrl+A) from the real opener, and reads the ARTEFACT — the emitted CSV
/// bytes on disk, and the strings the production PDF renderer actually drew (its <c>Tj</c> operands). A
/// property assertion would pass on a page whose export dropped a bucket.</para>
///
/// <para><b>The fixture populates all four buckets with distinct figures</b>, so every figure asserted can only
/// have come from the row it belongs to: Matched SUP-INV-001 (5,000 / 900), Partial mismatch SUP-INV-002 (portal
/// 2,100 / 378 against books 2,000 / 360 ⇒ variance 100 / 18), In portal only SUP-INV-009 (7,000 / 1,260),
/// In books only SUP-INV-003 (3,000 / 540).</para>
///
/// <para><b>Mutation that reddens every test in this file:</b> remove <c>, IMasterListExportSource</c> from the
/// class declaration (and its method) — the page is then neither printable nor exportable and each chord is inert.</para>
/// </summary>
public sealed class Gstr2bReconOutputTests : IDisposable
{
    private const string GstinMaharashtra = "27AAPFU0939F1ZV";
    private const string GstinSupplier = "27AAACC1206D1ZM";
    private static readonly DateOnly FyStart = new(2024, 4, 1);

    private readonly string _tempDir;
    private readonly string _outDir;
    private readonly CompanyStorage _storage;

    public Gstr2bReconOutputTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "ApexRecon2bOut_" + Guid.NewGuid().ToString("N"));
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

    /// <summary>The four bucket headings, each with its count — every one must survive into every artefact.</summary>
    private static readonly string[] BucketHeadings =
    {
        "Matched (1)", "Partial mismatch (1)", "In portal only (1)", "In books only (1)",
    };

    /// <summary>The identity cell of every row (supplier GSTIN, two spaces, doc no).</summary>
    private static readonly string[] Identities =
    {
        $"{GstinSupplier}  SUP-INV-001", $"{GstinSupplier}  SUP-INV-002",
        $"{GstinSupplier}  SUP-INV-009", $"{GstinSupplier}  SUP-INV-003",
    };

    /// <summary>Every figure the screen shows, as the invariant number a spreadsheet and the PDF carry.</summary>
    private static readonly string[] Figures =
    {
        "5000.00", "900.00",            // Matched
        "2100.00", "378.00",            // Partial mismatch (portal side)
        "100.00", "18.00",              // its portal − books variance
        "7000.00", "1260.00",           // In portal only
        "3000.00", "540.00",            // In books only
    };

    // ================================================================ 1. print — Ctrl+P

    /// <summary>
    /// Ctrl+P over the open reconciliation renders the print preview, and the PDF the renderer DREW carries every
    /// bucket heading, every row's identity and every figure — whole, not clipped to an ellipsis.
    /// </summary>
    [AvaloniaFact]
    public void Ctrl_P_prints_every_bucket_row_and_figure_of_the_reconciliation()
    {
        var vm = FourBucketBook("Recon Print Co");
        var win = Show(vm);

        vm.OpenGstr2bReconReport();
        Pump(win);
        Assert.Equal(Screen.Gstr2bReconReport, vm.CurrentScreen);
        Assert.True(vm.IsPrintablePage, "the reconciliation page is still not printable");

        win.KeyPressQwerty(PhysicalKey.P, RawInputModifiers.Control);
        Pump(win);

        Assert.Equal(Screen.PrintPreview, vm.CurrentScreen);
        Assert.Equal("GSTR-2B Reconciliation", vm.PrintPreview!.ReportTitle);
        var drawn = DrawnStrings(vm.PrintPreview.PdfBytes);

        foreach (var heading in BucketHeadings) Assert.Contains(heading, drawn);
        foreach (var id in Identities) Assert.Contains(id, drawn);
        foreach (var figure in Figures) Assert.Contains(figure, drawn);
        Assert.Contains("GSTR-2B 2024-04", drawn);
        Assert.Contains($"Issued to {GstinMaharashtra}", drawn);
        Assert.DoesNotContain(drawn, s => s.Contains("tally", StringComparison.OrdinalIgnoreCase));

        win.Close();
    }

    // ================================================================ 2. export current — Ctrl+E, then Ctrl+A

    /// <summary>
    /// Ctrl+E (the vendor's "export the current report") opens the Export panel over the reconciliation; Ctrl+A
    /// writes it. The CSV read back off disk carries every heading, identity and figure — and the bucket of every
    /// row is the bucket the SCREEN put it in (each identity sits under its own heading, in screen order).
    /// </summary>
    [AvaloniaFact]
    public void Ctrl_E_then_Ctrl_A_writes_a_csv_carrying_every_bucket_in_screen_order()
    {
        var vm = FourBucketBook("Recon Csv Co");
        var win = Show(vm);
        vm.OpenGstr2bReconReport();
        Pump(win);
        var page = vm.Gstr2bReconReport!;
        Assert.True(vm.IsExportablePage, "the reconciliation page is still not exportable");

        win.KeyPressQwerty(PhysicalKey.E, RawInputModifiers.Control);
        Pump(win);
        Assert.Equal(Screen.Export, vm.CurrentScreen);
        var panel = vm.ExportPanel!;
        Assert.Equal("GSTR-2B Reconciliation", panel.DocumentTitle);
        panel.Folder = _outDir;
        panel.Format = ExportFormat.Csv;
        panel.FileName = "recon2b";
        panel.AppendTimestamp = false;

        win.KeyPressQwerty(PhysicalKey.A, RawInputModifiers.Control);
        Pump(win);

        var path = Path.Combine(_outDir, "recon2b.csv");
        Assert.True(File.Exists(path), panel.Status);
        var lines = File.ReadAllLines(path, Encoding.UTF8);
        var text = string.Join("\n", lines);

        foreach (var heading in BucketHeadings) Assert.Contains(heading, text, StringComparison.Ordinal);
        foreach (var id in Identities) Assert.Contains(id, text, StringComparison.Ordinal);
        foreach (var figure in Figures) Assert.Contains(figure, text, StringComparison.Ordinal);

        // Each screen row lands under ITS OWN bucket heading in the file: the nearest heading above its line names
        // the bucket the screen shows it in.
        foreach (var row in page.Rows)
        {
            var at = Array.FindIndex(lines, l => l.Contains($"{row.Supplier}  {row.DocNo}", StringComparison.Ordinal));
            Assert.True(at > 0, $"{row.DocNo} is on screen and not in the export");
            var heading = lines.Take(at).Last(l => BucketHeadings.Any(h => l.Contains(h, StringComparison.Ordinal)));
            Assert.Contains($"{row.Bucket} (", heading, StringComparison.Ordinal);
        }
        Assert.Equal(4, page.Rows.Count);

        win.Close();
    }

    // ================================================================ 3. Alt+E > Current, as PDF

    /// <summary>
    /// The vendor's own route, verbatim from the GSTR-2B reconciliation page
    /// (help.tallysolutions.com/india-gst-status-gstr-2b-reconciliation-tally-2/): <i>"Press Alt+E (Export) &gt;
    /// Current"</i>. Alt+E opens the export menu, its Current row opens the panel, and a PDF written from it carries
    /// every bucket heading and figure in its drawn text.
    /// </summary>
    [AvaloniaFact]
    public void Alt_E_current_exports_the_reconciliation_as_a_pdf_with_every_bucket()
    {
        var vm = FourBucketBook("Recon Pdf Co");
        var win = Show(vm);
        vm.OpenGstr2bReconReport();
        Pump(win);

        win.KeyPressQwerty(PhysicalKey.E, RawInputModifiers.Alt);
        Pump(win);
        Assert.Equal(Screen.ExportMenu, vm.CurrentScreen);

        var current = vm.Columns[^1].Items
            .FirstOrDefault(i => i.IsSelectable && i.Label == ReportExportMenu.CurrentVerb);
        Assert.NotNull(current);
        current!.Activate();
        Pump(win);

        Assert.Equal(Screen.Export, vm.CurrentScreen);
        var panel = vm.ExportPanel!;
        Assert.Equal("GSTR-2B Reconciliation", panel.DocumentTitle);
        panel.Folder = _outDir;
        panel.Format = ExportFormat.Pdf;
        panel.FileName = "recon2b";
        panel.AppendTimestamp = false;

        win.KeyPressQwerty(PhysicalKey.A, RawInputModifiers.Control);
        Pump(win);

        var path = Path.Combine(_outDir, "recon2b.pdf");
        Assert.True(File.Exists(path), panel.Status);
        var bytes = File.ReadAllBytes(path);
        Assert.StartsWith("%PDF-", Encoding.Latin1.GetString(bytes, 0, 5));
        var drawn = DrawnStrings(bytes);

        foreach (var heading in BucketHeadings) Assert.Contains(heading, drawn);
        foreach (var id in Identities) Assert.Contains(id, drawn);
        foreach (var figure in Figures) Assert.Contains(figure, drawn);

        win.Close();
    }

    // ================================================================ 4. an empty bucket is still stated

    /// <summary>
    /// A bucket with nothing in it is emitted as "(0)", never dropped: a reconciliation that omits an empty bucket
    /// cannot be told apart from one that never computed it. One matched line, nothing else.
    /// </summary>
    [Fact]
    public void An_empty_bucket_is_exported_as_a_zero_count_never_omitted()
    {
        var vm = RegularBook("Recon Zero Co");
        var c = vm.Company!;
        Purchase(c, "SUP-INV-001", 5000m);
        Import2b(c, "2024-04", null, Line("SUP-INV-001", 500_000, 45_000));
        _storage.Save(c);

        vm.OpenGstr2bReconReport();
        var page = vm.Gstr2bReconReport!;
        var csv = Encoding.UTF8.GetString(CsvWriter.Write(MasterListTabularProjector.ProjectSource(page)));

        Assert.Contains("Matched (1)", csv, StringComparison.Ordinal);
        Assert.Contains("Partial mismatch (0)", csv, StringComparison.Ordinal);
        Assert.Contains("In portal only (0)", csv, StringComparison.Ordinal);
        Assert.Contains("In books only (0)", csv, StringComparison.Ordinal);
    }

    // ================================================================ 5. the multi-registration scope

    /// <summary>
    /// 🔴 <b>On a two-registration book the export is the SCREEN'S scope, and the screen's scope is the selected
    /// statement's own registration.</b> Before this change the page reconciled UNSCOPED, the engine refused, and
    /// the whole page — and so any export of it — was that refusal. The primary's statement is now matched against
    /// the primary's books only: Gujarat's purchase GJ-INV-001 (₹4,000) is in neither the screen nor the file,
    /// and selecting Gujarat's own statement brings exactly that purchase in and takes the primary's out.
    ///
    /// <para><b>Mutation that reddens it:</b> drop <c>snap.OwningRegistrationId</c> from the
    /// <c>Gstr2bReconciler.Reconcile</c> call in <c>Rebuild</c> — the page's <c>Message</c> becomes the
    /// multi-registration refusal and its rows empty.</para>
    /// </summary>
    [Fact]
    public void On_a_two_registration_book_the_export_carries_the_statements_own_registration_only()
    {
        var vm = RegularBook("Recon Two Reg Co");
        var c = vm.Company!;
        var gujarat = AddGujaratRegistration(c);
        Purchase(c, "SUP-INV-001", 5000m);
        Purchase(c, "SUP-INV-003", 3000m);
        Purchase(c, "GJ-INV-001", 4000m, registrationId: gujarat.Id);
        Import2b(c, "2024-04", null, Line("SUP-INV-001", 500_000, 45_000));
        Import2b(c, "2024-04", gujarat.Id, Line("GJ-INV-001", 400_000, 36_000), hash: "gj",
            recipient: gujarat.Gstin!, importedDay: 16);
        _storage.Save(c);

        vm.OpenGstr2bReconReport();
        var page = vm.Gstr2bReconReport!;
        page.SelectedSnapshot = page.Snapshots.Single(s => s.Snapshot.GstRegistrationId is null);

        // The primary's statement against the primary's books — not a refusal.
        Assert.Null(page.Message);
        Assert.Equal(1, page.MatchedCount);
        Assert.Equal(1, page.InBooksOnlyCount);
        Assert.DoesNotContain(page.Rows, r => r.DocNo == "GJ-INV-001");
        Assert.Contains(GstinMaharashtra, page.Subtitle, StringComparison.Ordinal);

        var csv = Csv(page);
        foreach (var r in page.Rows)
            Assert.Contains($"{r.Supplier}  {r.DocNo}", csv, StringComparison.Ordinal);
        Assert.DoesNotContain("GJ-INV-001", csv, StringComparison.Ordinal);
        Assert.DoesNotContain("4000.00", csv, StringComparison.Ordinal);
        Assert.Contains("Registration: ", csv, StringComparison.Ordinal);

        // Gujarat's own statement: Gujarat's purchase matches, and the primary's purchases are out of scope.
        page.SelectedSnapshot = page.Snapshots.Single(s => s.Snapshot.GstRegistrationId == gujarat.Id);
        Assert.Null(page.Message);
        Assert.Equal(1, page.MatchedCount);
        Assert.Equal(0, page.InBooksOnlyCount);
        Assert.Single(page.Rows);
        Assert.Equal("GJ-INV-001", page.Rows[0].DocNo);
        Assert.Contains("Gujarat Registration", page.Subtitle, StringComparison.Ordinal);

        csv = Csv(page);
        Assert.Contains("GJ-INV-001", csv, StringComparison.Ordinal);
        Assert.Contains("4000.00", csv, StringComparison.Ordinal);
        Assert.Contains("Registration: Gujarat Registration", csv, StringComparison.Ordinal);
        Assert.DoesNotContain("SUP-INV-003", csv, StringComparison.Ordinal);
    }

    /// <summary>A single-registration book names no registration line and its subtitle is unchanged (ER-13).</summary>
    [Fact]
    public void A_single_registration_book_carries_no_registration_line()
    {
        var vm = FourBucketBook("Recon Single Co");
        vm.OpenGstr2bReconReport();
        var page = vm.Gstr2bReconReport!;

        Assert.DoesNotContain("Registration: ", Csv(page), StringComparison.Ordinal);
        Assert.StartsWith($"{vm.Company!.Name}  —  GSTR-2B 2024-04 ", page.Subtitle, StringComparison.Ordinal);
    }

    // ---------------------------------------------------------------- fixtures

    private MainWindowViewModel RegularBook(string name)
    {
        var vm = new MainWindowViewModel(_storage);
        vm.NewCompanyName = name + " " + Guid.NewGuid().ToString("N")[..6];
        vm.CreateCompany();
        var c = vm.Company!;
        c.FinancialYearStart = FyStart;
        c.BooksBeginFrom = FyStart;
        new GstService(c).EnableGst(new GstConfig
        {
            HomeStateCode = "27",
            Gstin = GstinMaharashtra,
            RegistrationType = GstRegistrationType.Regular,
            ApplicableFrom = FyStart,
            Periodicity = GstReturnPeriodicity.Monthly,
        });
        Add(c, "Purchases", "Purchase Accounts", true);
        var supplier = Add(c, "Local Supplier", "Sundry Creditors", false);
        supplier.PartyGst = new PartyGstDetails
        { RegistrationType = GstRegistrationType.Regular, Gstin = GstinSupplier, StateCode = "27" };
        vm.ShowGateway();
        return vm;
    }

    /// <summary>All four buckets populated, each with figures no other row shares.</summary>
    private MainWindowViewModel FourBucketBook(string name)
    {
        var vm = RegularBook(name);
        var c = vm.Company!;
        Purchase(c, "SUP-INV-001", 5000m);   // matched
        Purchase(c, "SUP-INV-002", 2000m);   // partial: the portal says 2,100
        Purchase(c, "SUP-INV-003", 3000m);   // in books only
        Import2b(c, "2024-04", null,
            Line("SUP-INV-001", 500_000, 45_000),
            Line("SUP-INV-002", 210_000, 18_900),
            Line("SUP-INV-009", 700_000, 63_000));  // in portal only
        _storage.Save(c);
        vm.ShowGateway();
        return vm;
    }

    /// <summary>An intra purchase at 18% (CGST 9% + SGST 9%) from the local supplier, its doc no in the narration.</summary>
    private static void Purchase(Company c, string docNo, decimal taxable, Guid? registrationId = null)
    {
        var purchases = c.Ledgers.First(l => l.Name == "Purchases");
        var supplier = c.Ledgers.First(l => l.Name == "Local Supplier");
        var tax = new GstService(c).ComputeInvoiceTax(
            new[] { new GstService.TaxableLine(Money.FromRupees(taxable), 1800) }, false, GstTaxDirection.Input);
        var lines = new List<EntryLine>
        {
            new(purchases.Id, Money.FromRupees(taxable), DrCr.Debit),
            new(supplier.Id, Money.FromRupees(taxable * 1.18m), DrCr.Credit),
        };
        lines.AddRange(tax.TaxLines);
        var type = c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Purchase).Id;
        var voucher = new Voucher(Guid.NewGuid(), type, new DateOnly(2024, 4, 3), lines, partyId: supplier.Id,
            narration: docNo) { GstRegistrationId = registrationId };
        new LedgerService(c).Post(voucher);
    }

    /// <summary>A 2B line for the local supplier: taxable value and each of CGST/SGST in paisa.</summary>
    private static Gstr2bLine Line(string docNo, long taxablePaisa, long halfTaxPaisa) => new(
        Guid.NewGuid(), GstinSupplier, "Local Supplier", Gstr2bDocType.B2b, docNo,
        Gstr2bReconciler.NormaliseDocNo(docNo), new DateOnly(2024, 4, 3), "27",
        taxableValuePaisa: taxablePaisa, igstPaisa: 0, cgstPaisa: halfTaxPaisa, sgstPaisa: halfTaxPaisa, cessPaisa: 0,
        itcAvailable: true, itcUnavailableReason: null, reverseCharge: false);

    private static void Import2b(
        Company c, string period, Guid? registrationId, params Gstr2bLine[] lines)
        => Import2b(c, period, registrationId, lines[0], lines.Skip(1).ToArray());

    private static void Import2b(Company c, string period, Guid? registrationId, Gstr2bLine first,
        Gstr2bLine[] rest, string hash = "primary", string? recipient = null, int importedDay = 14)
    {
        var all = new[] { first }.Concat(rest).ToArray();
        c.AddGstr2bSnapshot(new Gstr2bSnapshot(
            Guid.NewGuid(), GstStatementType.Gstr2b, period, recipient ?? GstinMaharashtra, new DateOnly(2024, 5, 14),
            sourceFileHash: "hash-" + hash, importedAt: new DateTimeOffset(2024, 5, importedDay, 0, 0, 0, TimeSpan.Zero),
            summaryIgstPaisa: 0, summaryCgstPaisa: all.Sum(l => l.CgstPaisa), summarySgstPaisa: all.Sum(l => l.SgstPaisa),
            summaryCessPaisa: 0, lines: all, gstRegistrationId: registrationId));
    }

    private static void Import2b(Company c, string period, Guid? registrationId, Gstr2bLine line, string hash,
        string recipient, int importedDay)
        => Import2b(c, period, registrationId, line, Array.Empty<Gstr2bLine>(), hash, recipient, importedDay);

    private static GstRegistration AddGujaratRegistration(Company c)
    {
        var r = new GstRegistration(
            Guid.NewGuid(), "Gujarat Registration", "24",
            "24AAACC1206D1Z" + Gstin.ComputeCheckDigit("24AAACC1206D1Z0"),
            GstRegistrationType.Regular, FyStart);
        c.Gst!.AddRegistration(r);
        c.Gst!.EnsureValid();
        return r;
    }

    private static DomainLedger Add(Company c, string name, string groupName, bool debit)
    {
        var group = c.FindGroupByName(groupName) ?? throw new InvalidOperationException($"No group '{groupName}'.");
        var ledger = new DomainLedger(Guid.NewGuid(), name, group.Id, Money.Zero, openingIsDebit: debit);
        c.AddLedger(ledger);
        return ledger;
    }

    // ---------------------------------------------------------------- harness

    private static string Csv(IMasterListExportSource page) =>
        Encoding.UTF8.GetString(CsvWriter.Write(MasterListTabularProjector.ProjectSource(page)));

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

    /// <summary>
    /// The strings a PDF actually DREW: <c>Apex.Ledger.Io</c> writes uncompressed content streams, so each
    /// <c>Tj</c> operand is the literal text of one cell, already passed through <c>FitToWidth</c> — a clipped
    /// cell would appear here with its ellipsis and fail an exact-match assertion.
    /// </summary>
    private static List<string> DrawnStrings(byte[] pdf)
    {
        string text = Encoding.Latin1.GetString(pdf);
        var drawn = new List<string>();
        int i = 0;
        while (true)
        {
            int close = text.IndexOf(") Tj", i, StringComparison.Ordinal);
            if (close < 0) break;
            // The operand's OPENING paren is the nearest one not escaped by a backslash: a caption such as
            // "Matched (1)" is written "(Matched \(1\)) Tj", so the plain LastIndexOf('(') would land inside it.
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
