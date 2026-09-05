using System;
using System.Collections.Generic;
using System.Linq;
using System.Collections.ObjectModel;
using System.IO;
using Apex.Desktop.Services;
using Apex.Ledger.Io;
using Apex.Ledger.Reports;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Apex.Desktop.ViewModels;

/// <summary>
/// The keyboard-first Print Preview page (RQ-9 / DP-8), hosted as its own cascading Miller-column to the
/// right of the report it prints. It renders the report the user is looking at to a de-branded PDF via
/// <see cref="ReportPdf"/> in <c>Apex.Ledger.Io</c> and shows the paginated layout on screen (a lightweight
/// text projection of the same page model — the actual PDF is <b>not</b> rasterised, per the slice). "Save PDF"
/// writes the already-rendered bytes to a path.
///
/// <para>All IO stays in <c>Apex.Ledger.Io</c>: this VM only builds the <see cref="PageConfig"/>, calls the
/// renderer, holds the resulting <see cref="PdfBytes"/>, and writes the stream on <see cref="SavePdf"/>. It
/// never re-computes figures and never touches the clock (ER-12). The rendered bytes are de-branded — the
/// header/footer and metadata say "Apex Solutions", never a third-party brand.</para>
/// </summary>
public sealed partial class PrintPreviewViewModel : ViewModelBase
{
    /// <summary>What this preview is printing: a report (RQ-9), a plain voucher (RQ-10), a tax invoice — or the
    /// bill of supply §31(3)(c) requires in its place (RQ-11; W0-1) — a POS receipt, a payroll Payslip (RQ-16),
    /// or a SET of reports printed as one collated job (W2-32 / census 12.6).
    /// The document mode selects the Io renderer and the F12 config knobs that apply.</summary>
    public enum PrintKind { Report, Voucher, Invoice, Receipt, Payslip, ReportSet }

    // Exactly one of these is set per instance (by the chosen ctor); it drives the render + preview.
    private readonly PrintReport? _report;

    /// <summary>The document SET a <see cref="PrintKind.ReportSet"/> preview prints (W2-32). Null on every other
    /// kind.</summary>
    private readonly IReadOnlyList<PrintReport>? _documents;
    private readonly VoucherPrintData? _voucher;
    private readonly InvoicePrintData? _invoice;
    private readonly PosReceiptData? _receipt;
    private readonly Payslip? _payslip;

    /// <summary>The page config the preview + PDF are rendered with. Rebuilt (and the document re-rendered) when
    /// the size/orientation is changed via the toggles below.</summary>
    private PageConfig _config;

    /// <summary>The report projection the on-screen preview paginates. In report mode it IS the report; in
    /// voucher/invoice mode it is a lightweight text projection of the voucher/invoice (rebuilt each render so
    /// the narration toggle / copy label / title override are reflected on screen too). The authoritative bytes
    /// always come from the Io renderer — this is presentation-only.</summary>
    private PrintReport _previewReport = new();

    /// <summary>
    /// The documents the on-screen preview paginates, in print order. On every single-document kind this is the
    /// one <see cref="_previewReport"/>; on a <see cref="PrintKind.ReportSet"/> it is the whole job.
    ///
    /// <para>It exists because the pane must mirror what <see cref="ReportPdf"/> actually does with a set: each
    /// document starts a FRESH SHEET and carries its OWN title band and column geometry. Paginating a job as one
    /// long row list would show the operator a single merged statement and then print a stack of separate ones.</para>
    /// </summary>
    private IReadOnlyList<PrintReport> _previewDocuments = Array.Empty<PrintReport>();

    /// <summary>Which document kind this preview renders.</summary>
    public PrintKind Kind { get; }

    public string Title => "Print Preview";

    /// <summary>True for a voucher / tax-invoice preview — the F12 print-config knobs (title override, narration
    /// toggle, copy marking) apply. False for a plain report preview (those knobs are inert there).</summary>
    public bool SupportsPrintConfig => Kind is PrintKind.Voucher or PrintKind.Invoice;

    /// <summary>The report title being printed (heading line).</summary>
    public string ReportTitle { get; }

    /// <summary>The rendered PDF bytes for the current config — non-empty, de-branded, deterministic.</summary>
    public byte[] PdfBytes { get; private set; } = Array.Empty<byte>();

    /// <summary>The paginated preview pages (each a header/subtitle band + laid-out text lines) shown on screen.</summary>
    public ObservableCollection<PreviewPage> Pages { get; } = new();

    /// <summary>A status line shown after a Save (or a failure).</summary>
    [ObservableProperty] private string _status = string.Empty;

    /// <summary>The chosen page size — A4 (default) or Letter. Toggling re-renders.</summary>
    [ObservableProperty] private bool _useLetter;

    /// <summary>Landscape orientation when true (portrait by default). Toggling re-renders.</summary>
    [ObservableProperty] private bool _landscape;

    // ---- F12 print-config knobs (RQ-12) — apply to voucher/invoice prints; inert for a report. ----

    /// <summary>F12: an optional document-title override (blank ⇒ the template default, e.g. "TAX INVOICE").
    /// Changing it re-renders.</summary>
    [ObservableProperty] private string _titleOverride = string.Empty;

    /// <summary>F12: whether the narration line prints (default on). Toggling re-renders.</summary>
    [ObservableProperty] private bool _showNarration = true;

    /// <summary>F12: the copy-marking label (None / Original / Duplicate / Triplicate). Changing it re-renders.</summary>
    [ObservableProperty] private CopyMarking _copyMarking = CopyMarking.None;

    // ---- W2-31 (census 12.4) print knobs. --------------------------------------------------------------
    //
    // 🔴 TWO CORRECTIONS TO WHAT THIS BLOCK USED TO CLAIM.
    //
    // 1. It said these "apply to EVERY preview kind". They do not. Only ReportPdf reads PageConfig's
    //    Formatted* / Draws* / IncludesPage / StartPageNumber members; InvoicePdf, VoucherPdf, PayslipPdf and
    //    PosReceiptPdf read ONLY EffectiveCopies. The copy count is the one knob that is universal. The panel
    //    now gates the rest on PrintConfigViewModel.SupportsPageKnobs.
    //
    // 2. Each summary below was tagged with a function key — F8, F9, F5, F10 — as though the key were bound.
    //    NONE of them is: `PrintConfigPanel` appears nowhere in MainWindow.axaml.cs, so with the panel open
    //    those keys fall through to the global F-key switch (F10 navigates away via ShowOtherVouchersMenu).
    //    The key names are removed rather than left to mislead; binding them is open work.

    /// <summary>The print format (Neat / Dot Matrix / Quick-Draft). Changing it re-renders.
    /// Honoured by <see cref="ReportPdf"/> only.</summary>
    [ObservableProperty] private PrintFormat _printFormat = PrintFormat.Neat;

    /// <summary>Plain paper or pre-printed stationery. Changing it re-renders.
    /// Honoured by <see cref="ReportPdf"/> only.</summary>
    [ObservableProperty] private PaperKind _paper = PaperKind.Plain;

    /// <summary>How many collated copies of the whole document the file carries. Changing it re-renders.
    /// Honoured by <b>every</b> renderer.</summary>
    [ObservableProperty] private int _copies = 1;

    /// <summary>The first page of the document to print (1-based). Changing it re-renders.
    /// Honoured by <see cref="ReportPdf"/> only.</summary>
    [ObservableProperty] private int _firstPage = 1;

    /// <summary>The last page to print (1-based); 0 means to the end. Changing it re-renders.
    /// Honoured by <see cref="ReportPdf"/> only.</summary>
    [ObservableProperty] private int _lastPage;

    /// <summary>The page number the first sheet carries. Changing it re-renders.
    /// Honoured by <see cref="ReportPdf"/> only.</summary>
    [ObservableProperty] private int _startPageNumber = 1;

    /// <summary>The page count of the rendered PDF / preview (for the heading).</summary>
    public int PageCount => Pages.Count;

    public PrintPreviewViewModel(ReportsViewModel reportVm)
        : this(ReportPrintProjector.Project(reportVm), reportVm?.Title ?? string.Empty) { }

    /// <summary>Preview a payroll Payslip (RQ-16) via <c>PayslipPdf</c> — the same deterministic, de-branded PDF
    /// pipeline as the tax invoice / TDS certificates. A fixed payslip layout: the F12 knobs do not apply.</summary>
    public PrintPreviewViewModel(Payslip payslip, string reportTitle)
    {
        _payslip = payslip ?? throw new ArgumentNullException(nameof(payslip));
        Kind = PrintKind.Payslip;
        ReportTitle = reportTitle ?? string.Empty;
        _config = BuildConfig();
        Render();
    }

    /// <summary>Testable ctor: preview a pre-built report print model directly (RQ-9).</summary>
    public PrintPreviewViewModel(PrintReport report, string reportTitle)
    {
        _report = report ?? throw new ArgumentNullException(nameof(report));
        Kind = PrintKind.Report;
        ReportTitle = reportTitle ?? string.Empty;
        _config = BuildConfig();
        Render();
    }

    /// <summary>
    /// Preview a SET of already-projected documents as ONE collated print job (W2-32 / census 12.6) via
    /// <see cref="ReportPdf"/>'s multi-document overload — the multi-account / multi-voucher range print.
    ///
    /// <para>🔴 <b>This constructor is the whole reason row 12.6 was refused.</b> The engine half
    /// (<c>ReportPdf.Render</c> over a document set) and the projector half
    /// (<c>MultiAccountPrintProjector</c>) both shipped and were both correct; there was simply no way to get a
    /// SET into a preview, so <c>MultiAccountPrintViewModel</c> had nobody to hand its job to and the whole
    /// ~432 lines were reachable by nobody. It is added here rather than by widening the single-report
    /// constructor because the two render through different overloads and paginate differently.</para>
    ///
    /// <para>The W2-31 page knobs apply, because <see cref="ReportPdf"/> is the renderer that honours them; the
    /// F12 document knobs (title override, narration, copy marking) do not, exactly as for a single report.
    /// A one-document set renders byte-identically to that document alone (ER-13) — <c>ReportPdf</c>'s
    /// single-document overload delegates to the same code path.</para>
    /// </summary>
    /// <param name="documents">The job, in print order. Each document starts a fresh sheet.</param>
    /// <param name="reportTitle">The heading the preview column carries for the job as a whole.</param>
    public PrintPreviewViewModel(IReadOnlyList<PrintReport> documents, string reportTitle)
    {
        ArgumentNullException.ThrowIfNull(documents);
        // A job of NOTHING is refused rather than previewed as a blank sheet. The caller (the multi-account panel)
        // already reports "select at least one account"; letting an empty job through to here would put a blank
        // page on screen and call it output, which is the mistake-reported-as-a-document shape this project keeps
        // finding. The panel's own guard is the operator-facing message; this is the structural backstop.
        if (documents.Count == 0)
            throw new ArgumentException("a print job must contain at least one document", nameof(documents));
        _documents = documents;
        Kind = PrintKind.ReportSet;
        ReportTitle = reportTitle ?? string.Empty;
        _config = BuildConfig();
        Render();
    }

    /// <summary>Preview a plain voucher (RQ-10) via <c>VoucherPdf</c>; the F12 knobs apply.</summary>
    public PrintPreviewViewModel(VoucherPrintData voucher)
    {
        _voucher = voucher ?? throw new ArgumentNullException(nameof(voucher));
        Kind = PrintKind.Voucher;
        ReportTitle = string.IsNullOrEmpty(voucher.VoucherNumber)
            ? voucher.VoucherTypeName
            : $"{voucher.VoucherTypeName} No. {voucher.VoucherNumber}";
        _config = BuildConfig();
        Render();
    }

    /// <summary>Preview a GST tax invoice — or a <b>Bill of Supply</b> (W0-1) — via <c>InvoicePdf</c>; the F12 knobs
    /// apply, except that the title override cannot re-title a bill of supply (see <c>InvoicePdf.Render</c>).</summary>
    public PrintPreviewViewModel(InvoicePrintData invoice)
    {
        _invoice = invoice ?? throw new ArgumentNullException(nameof(invoice));
        Kind = PrintKind.Invoice;
        // The heading names the document the operator is actually looking at; a bill of supply must not be announced
        // as a tax invoice anywhere in the app, on screen or on paper.
        // T0-11 slice S2 — and a recipient-side record must not be announced as a tax invoice either. This heading is
        // also surfaced as PrintConfigViewModel.DocumentTitle and used as the DEFAULT SAVED FILE NAME, so leaving it
        // on the two-way literal would have filed a purchase record as "Tax Invoice No. 42.pdf" — the same defect
        // W0-1's own follow-up found on the POS receipt path.
        var kindName = invoice.IsRecipientRecord
            ? GstReportSupport.PurchaseRecordScreenLabel
            : invoice.IsBillOfSupply ? "Bill of Supply" : "Tax Invoice";
        ReportTitle = string.IsNullOrEmpty(invoice.InvoiceNumber)
            ? kindName
            : $"{kindName} No. {invoice.InvoiceNumber}";
        _config = BuildConfig();
        Render();
    }

    /// <summary>Preview a POS retail receipt (Phase 6 slice 7 RQ-44) via <c>PosReceiptPdf</c> — or a <b>Bill of
    /// Supply</b> (W0-1b). A receipt is a fixed retail bill layout, so the F12 title/narration/copy knobs do not
    /// apply.</summary>
    public PrintPreviewViewModel(PosReceiptData receipt)
    {
        _receipt = receipt ?? throw new ArgumentNullException(nameof(receipt));
        Kind = PrintKind.Receipt;
        // W0-1 follow-up (review findings #2/#5) — the SAME principle the invoice ctor above applies: the heading
        // names the document the operator is actually looking at, and a bill of supply must not be announced as
        // something else anywhere in the app. This one was left on the old literal, so a composition dealer's POS
        // sale headed its pane "Retail Receipt No. 1" — and, because ReportTitle is also surfaced as
        // PrintConfigViewModel.DocumentTitle and used as the DEFAULT SAVED FILE NAME, the operator filed
        // "Retail Receipt No. 1.pdf" for a document whose title band, number caption and closing declaration all read
        // bill of supply.
        var kindName = receipt.IsBillOfSupply ? "Bill of Supply" : "Retail Receipt";
        ReportTitle = string.IsNullOrEmpty(receipt.BillNumber)
            ? kindName
            : $"{kindName} No. {receipt.BillNumber}";
        _config = BuildConfig();
        Render();
    }

    /// <summary>The F12 knobs assembled into the Io <see cref="PrintConfig"/> the renderers honour.</summary>
    private PrintConfig BuildPrintConfig() => new()
    {
        TitleOverride = string.IsNullOrWhiteSpace(TitleOverride) ? null : TitleOverride.Trim(),
        ShowNarration = ShowNarration,
        CopyMarking = CopyMarking,
    };

    private PageConfig BuildConfig() => new()
    {
        Size = UseLetter ? PageSize.Letter : PageSize.A4,
        Orientation = Landscape ? PageOrientation.Landscape : PageOrientation.Portrait,
        // A brand-safe footer with no clock: page numbers come from pagination, never DateTime.Now.
        FooterText = "Apex Solutions  -  Page {page} of {pages}",
        // W2-31 (census 12.4): the F8/F9/F5/F10 knobs. Their defaults reproduce the shipped output exactly, so a
        // preview the operator never configures renders the bytes it always did (ER-13).
        Format = PrintFormat,
        Paper = Paper,
        Copies = Copies,
        FirstPage = FirstPage,
        LastPage = LastPage,
        StartPageNumber = StartPageNumber,
    };

    /// <summary>Renders the PDF bytes and (re)builds the on-screen preview pages for the current config.</summary>
    private void Render()
    {
        _config = BuildConfig();
        PdfBytes = Kind switch
        {
            PrintKind.Voucher => VoucherPdf.Render(_voucher!, BuildPrintConfig(), _config),
            PrintKind.Invoice => InvoicePdf.Render(_invoice!, BuildPrintConfig(), _config),
            PrintKind.Receipt => PosReceiptPdf.Render(_receipt!, _config),
            PrintKind.Payslip => PayslipPdf.Render(_payslip!, _config),
            // W2-32: the SET goes through the multi-document overload, so the pane and the paper agree about a
            // job — one PDF, each document on its own sheet, numbering running across the whole job.
            PrintKind.ReportSet => ReportPdf.Render(_documents!, _config),
            _ => ReportPdf.Render(_report!, _config),
        };
        OnPropertyChanged(nameof(PdfBytes));

        _previewReport = Kind switch
        {
            PrintKind.Voucher => BuildVoucherPreviewReport(),
            PrintKind.Invoice => BuildInvoicePreviewReport(),
            PrintKind.Receipt => BuildReceiptPreviewReport(),
            PrintKind.Payslip => BuildPayslipPreviewReport(),
            // A set has no single preview report; the first document stands in for the pane's own bookkeeping
            // (nothing reads it on this path — the pagination below walks _previewDocuments instead).
            PrintKind.ReportSet => _documents![0],
            _ => _report!,
        };
        _previewDocuments = Kind == PrintKind.ReportSet ? _documents! : new[] { _previewReport };

        Pages.Clear();
        int pageNo = 0;
        foreach (var (document, rows) in PaginateForPreview())
        {
            pageNo++;
            Pages.Add(BuildPreviewPage(document, rows, pageNo));
        }
        if (Pages.Count == 0)
            Pages.Add(BuildPreviewPage(_previewReport, new List<PrintRow>(), 1));

        // Backfill the "of N" now the total is known.
        foreach (var p in Pages) p.SetTotalPages(Pages.Count);

        OnPropertyChanged(nameof(PageCount));
    }

    partial void OnUseLetterChanged(bool value) => Render();
    partial void OnLandscapeChanged(bool value) => Render();

    // W2-31: the print knobs apply to EVERY document kind (a copy count on an invoice is the case the F5 knob
    // exists for), so unlike the F12 document knobs below they re-render unconditionally.
    partial void OnPrintFormatChanged(PrintFormat value) => Render();
    partial void OnPaperChanged(PaperKind value) => Render();
    partial void OnCopiesChanged(int value) => Render();
    partial void OnFirstPageChanged(int value) => Render();
    partial void OnLastPageChanged(int value) => Render();
    partial void OnStartPageNumberChanged(int value) => Render();

    // The F12 knobs only affect a voucher/invoice render; re-render on change (a no-op guard keeps the report
    // preview from re-rendering pointlessly since those bytes never read the print config).
    partial void OnTitleOverrideChanged(string value) { if (SupportsPrintConfig) Render(); }
    partial void OnShowNarrationChanged(bool value) { if (SupportsPrintConfig) Render(); }
    partial void OnCopyMarkingChanged(CopyMarking value) { if (SupportsPrintConfig) Render(); }

    /// <summary>
    /// Saves the rendered PDF bytes to <paramref name="path"/> (the Avalonia layer chose the path; the writer
    /// itself never touches disk). Returns true on success. The bytes are the exact <see cref="PdfBytes"/> the
    /// preview reflects, so what is saved is what was previewed.
    /// </summary>
    public bool SavePdf(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            Status = "Choose a file path to save the PDF.";
            return false;
        }
        try
        {
            File.WriteAllBytes(path, PdfBytes);
            Status = $"Saved PDF ({PdfBytes.Length:#,0} bytes) to {path}";
            return true;
        }
        catch (Exception ex)
        {
            Status = "Could not save PDF: " + ex.Message;
            return false;
        }
    }

    // ---- lightweight preview pagination (mirrors ReportPdf's row-per-height overflow) ----

    private IEnumerable<(PrintReport Document, List<PrintRow> Rows)> PaginateForPreview()
    {
        // Approximate the renderer's rows-per-page from the content height and row height so the preview page
        // breaks read like the PDF. This is presentation-only; the authoritative bytes come from ReportPdf.
        double contentHeight = _config.PageHeight - _config.MarginTop - _config.MarginBottom
            - (_config.TitleFontSize + _config.SubtitleFontSize + _config.HeaderFontSize + 20)
            - (_config.FooterFontSize + 6);
        int perPage = Math.Max(1, (int)(contentHeight / _config.RowHeight));

        // W2-32: EACH DOCUMENT STARTS A FRESH SHEET, mirroring ReportPdf.Render(IReadOnlyList<PrintReport>, …).
        // On the single-document kinds the outer loop runs once and the row-splitting below is character-for-
        // character what it always was, so every existing preview paginates exactly as it did (ER-13).
        foreach (var document in _previewDocuments)
        {
            var current = new List<PrintRow>();
            bool yielded = false;
            foreach (var row in document.Rows)
            {
                if (current.Count >= perPage)
                {
                    yield return (document, current);
                    yielded = true;
                    current = new List<PrintRow>();
                }
                current.Add(row);
            }
            if (current.Count > 0)
                yield return (document, current);
            // A document with no rows at all still occupies its sheet — ReportPdf gives it one, so the pane must
            // show one. Without this a job of three statements, one of them empty, would preview as two sheets
            // and print as three.
            else if (!yielded)
                yield return (document, current);
        }
    }

    /// <summary>
    /// The width one preview column gets, in DIP, from the print model's own declared
    /// <see cref="PrintColumn.Weight"/>.
    ///
    /// <para>🔴 <b>T0-11 review CRITIC-01 and C17/L3-03.</b> Every cell of this pane used to be a literal 120 DIP.
    /// The shell is monospace (<c>MainWindow.axaml</c>'s <c>FontFamily="Consolas, …"</c>, 6.0479 DIP/glyph at
    /// <c>FontSize="11"</c>, measured under Skia), so a cell held eighteen glyphs and an ellipsis — and because the
    /// width was a literal inside a horizontal StackPanel with no star track, the cut was invariant under every
    /// window size and every DPI. Rendered at 1280x720 DIP, the purchase record's three item lines — 2 @ 12,345.67,
    /// 3 @ 8,901.23 and 1 @ 45,678.91 — all painted as "N. Widget  (HSN 84…", i.e. the slice's whole subject matter
    /// was unreachable on the pane the operator approves, and "Tax Charged by the Supplier" lost the word that says
    /// whose tax it is.</para>
    ///
    /// <para><b>The weights were already there and this pane was throwing them away.</b> <c>PrintColumn.Weight</c>
    /// is documented as "columns share the content width in proportion to their weights" and <c>ReportPdf</c> has
    /// always split the PAPER that way; only the screen mirror ignored it, so the screen gave "Particulars"
    /// (weight 4) exactly what it gave a two-figure amount column. Reading the same weights is the same
    /// mirror-follows-the-bytes discipline as every other row in this file.</para>
    ///
    /// <para><b>The floor is what keeps this from being a trade.</b> A purely proportional split would NARROW the
    /// columns of a wide report (a twelve-column payroll matrix), buying the invoice's readability with somebody
    /// else's. Flooring at the 120 the pane has always used makes the change monotone: every column either widens
    /// or stays exactly as it was, on every report kind.</para>
    ///
    /// <para><b>Why 80 per weight unit.</b> It is the largest round scale whose total for the widest of these
    /// reports still fits the sheet at the narrowest supported viewport. Measured on the shipped shell at
    /// 1280x720 DIP (== 1920x1080 at 150%, an ordinary full-HD laptop) the preview sheet gives its rows 572.00 DIP;
    /// the invoice mirror's weights (4 / 1 / 1.5) come to 320 + 120 + 120 = 560, so the Grand Total still lands
    /// inside the sheet with no horizontal scrolling. At 90 it would be 615 and the money column would go behind a
    /// scrollbar at that size, which is not a trade worth making for eight more glyphs.</para>
    ///
    /// <para><b>The residue, stated rather than hidden:</b> a stock-item name long enough to push the composed row
    /// past 320 DIP still trims. That is why the cell now carries <c>ToolTip.Tip</c> — the pattern the sheet's own
    /// Subtitle has used all along — and why <c>InvoicePdf</c> remains the authority on the full particulars.</para>
    /// </summary>
    private const double PreviewWidthPerWeightUnit = 80.0;

    /// <summary>The width every preview cell had before the column budget existed. Now a FLOOR: no column may be
    /// narrower than the pane has always drawn it, so widening one can never starve another.</summary>
    private const double PreviewMinimumCellWidth = 120.0;

    private static double PreviewColumnWidth(PrintColumn column) =>
        Math.Max(PreviewMinimumCellWidth, Math.Round(column.Weight * PreviewWidthPerWeightUnit));

    /// <summary>
    /// Lays out one preview sheet for <paramref name="document"/> — its own title band, its own column captions
    /// and its own weights.
    ///
    /// <para>W2-32: the document is a PARAMETER rather than the field it used to read, because a job's sheets do
    /// not share a layout. A ledger account (six columns) and a reminder letter (four) print in one job, and
    /// laying the letter's cells out on the statement's column widths would put its figures under captions that
    /// do not govern them.</para>
    /// </summary>
    private PreviewPage BuildPreviewPage(PrintReport document, List<PrintRow> rows, int pageNo)
    {
        var widths = new double[document.Columns.Count];
        for (int i = 0; i < widths.Length; i++) widths[i] = PreviewColumnWidth(document.Columns[i]);

        var lines = new List<PreviewLine>(rows.Count);
        foreach (var r in rows)
        {
            var cells = new List<PreviewCell>(document.Columns.Count);
            for (int i = 0; i < document.Columns.Count; i++)
            {
                string text = i < r.Cells.Count ? (r.Cells[i] ?? string.Empty) : string.Empty;
                if (i == 0 && r.Indent > 0) text = new string(' ', r.Indent) + text;
                cells.Add(new PreviewCell(text, widths[i]));
            }
            lines.Add(new PreviewLine(cells, r.IsHeader, r.IsTotal));
        }

        var headers = new List<PreviewCell>(document.Columns.Count);
        for (int i = 0; i < document.Columns.Count; i++)
            headers.Add(new PreviewCell(document.Columns[i].Header, widths[i]));

        return new PreviewPage(document.Title, document.Subtitle, headers, lines, pageNo);
    }

    // ---- voucher / invoice preview projections (presentation-only text mirror of the PDF) ----

    private PrintReport BuildVoucherPreviewReport()
    {
        var v = _voucher!;
        var cfg = BuildPrintConfig();
        var rows = new List<PrintRow>();
        if (!string.IsNullOrEmpty(cfg.CopyMarkingLabel))
            rows.Add(PrintRow.Header(cfg.CopyMarkingLabel, string.Empty, string.Empty));
        rows.Add(PrintRow.Header($"No. {v.VoucherNumber}", "Date", v.DateText));
        if (!string.IsNullOrEmpty(v.PartyName))
            rows.Add(PrintRow.Header("Party: " + v.PartyName, string.Empty, string.Empty));
        foreach (var l in v.Lines)
            rows.Add(new PrintRow(new[]
            {
                l.LedgerName,
                l.IsDebit ? IndianFormat.Amount(l.Amount) : string.Empty,
                l.IsDebit ? string.Empty : IndianFormat.Amount(l.Amount),
            }));
        rows.Add(PrintRow.Total("Total",
            IndianFormat.AmountAlways(v.TotalDebit), IndianFormat.AmountAlways(v.TotalCredit)));
        if (cfg.ShowNarration && !string.IsNullOrWhiteSpace(v.Narration))
            rows.Add(PrintRow.Header("Narration: " + v.Narration, string.Empty, string.Empty));

        var title = string.IsNullOrEmpty(cfg.TitleOverride) ? v.VoucherTypeName : cfg.TitleOverride!;
        return new PrintReport
        {
            Title = title,
            Subtitle = v.CompanyName,
            Columns = new[]
            {
                new PrintColumn("Particulars", 3, CellAlign.Left),
                new PrintColumn("Debit", 1.5, CellAlign.Right),
                new PrintColumn("Credit", 1.5, CellAlign.Right),
            },
            Rows = rows,
        };
    }

    private PrintReport BuildInvoicePreviewReport()
    {
        var inv = _invoice!;
        var cfg = BuildPrintConfig();
        var rows = new List<PrintRow>();
        // T0-11 review C3/L1-03 — the CGST Rule 48(1) copy marking is an issuer particular, and the mirror drops it
        // on a record for the same reason and off the SAME axis as InvoicePdf.DrawFirstHeader does. The two sites
        // must move together: a fix applied to the bytes alone is exactly the preview/paper drift this file's own
        // comments name as the thing to avoid, and a mirror still offering "DUPLICATE FOR TRANSPORTER" over a page
        // that carries none would have the operator approving a statutory copy marking the paper does not bear.
        if (!string.IsNullOrEmpty(cfg.CopyMarkingLabel) && inv.StatesOurDeclarationAndSignature)
            rows.Add(PrintRow.Header(cfg.CopyMarkingLabel, string.Empty, string.Empty));
        // W0-1: CGST Rule 5(1)(f) puts the composition declaration at the TOP of the bill of supply — so it is the
        // first row of the on-screen mirror too, exactly as InvoicePdf draws it under the title. W0-1 follow-up
        // (finding #6): gated on the structural flag, in lockstep with InvoicePdf.TopDeclarationLines — if the mirror
        // showed a declaration the bytes suppress, the operator would approve one document and issue another.
        if (inv.IsBillOfSupply && !string.IsNullOrWhiteSpace(inv.TopDeclaration))
            rows.Add(PrintRow.Header(inv.TopDeclaration, string.Empty, string.Empty));
        // T0-11 slice S2: the mirror re-derives the record's three suppressions from the SAME structural flag
        // InvoicePdf reads — the number caption (RQ-11a forbids "Invoice No." over OUR number on HIS document), the
        // counterparty the operator is being shown, and the place of supply (CGST Rule 46(n), a supplier particular).
        // If the mirror and the bytes disagreed the operator would approve one document and issue another.
        rows.Add(PrintRow.Header(
            (inv.IsRecipientRecord ? GstReportSupport.RecordNumberCaption + " "
                : inv.IsBillOfSupply ? "Bill of Supply No. " : "Invoice No. ") + inv.InvoiceNumber,
            "Date", inv.InvoiceDateText));
        // On a record the counterparty is the SUPPLIER, and he is the party in the block that HEADS the document.
        // T0-11 review C24/L3-10 — that is the ORIENTATION question (Rule 46(a)), so it reads `Heads`, exactly as
        // `InvoicePdf` now does for the same row. The mirror re-derives the record's presentation from the same
        // structural axes as the bytes; if the two disagreed the operator would approve one document and issue
        // another.
        rows.Add(inv.Heads == PartyOrientation.WeAreRecipient
            ? PrintRow.Header("Supplier: " + inv.Seller.Name, string.Empty, string.Empty)
            : PrintRow.Header("Buyer: " + inv.Buyer.Name, string.Empty, string.Empty));
        if (!inv.IsRecipientRecord)
            rows.Add(PrintRow.Header("Place of Supply: " + inv.PlaceOfSupply, string.Empty, string.Empty));
        // census T0-9 - the mirror states the e-invoice particulars the bytes carry, gated on the SAME predicate
        // (InvoicePrintData.StatesEInvoice) InvoicePdf measures and draws with. The QR itself is a raster mark and
        // this mirror is a text grid, so the mirror says that the signed QR prints rather than trying to show it: an
        // operator approving a preview that was silent about the QR would be approving a different document from the
        // one that leaves the printer, which is the exact divergence W0-1 and W0-15 each had to close elsewhere.
        if (inv.StatesEInvoice)
        {
            if (!string.IsNullOrWhiteSpace(inv.EInvoiceIrn))
                rows.Add(PrintRow.Header("e-Invoice IRN: " + inv.EInvoiceIrn, string.Empty, string.Empty));
            if (!string.IsNullOrWhiteSpace(inv.EInvoiceAckNo))
                rows.Add(PrintRow.Header("IRP Ack No: " + inv.EInvoiceAckNo, "Ack Date", inv.EInvoiceAckDateText));
            if (!string.IsNullOrWhiteSpace(inv.EInvoiceSignedQr))
                rows.Add(PrintRow.Header("Signed QR code: printed on the invoice", string.Empty, string.Empty));
        }

        int sr = 0;
        foreach (var it in inv.Items)
        {
            sr++;
            rows.Add(new PrintRow(new[]
            {
                $"{sr}. {it.Description}  (HSN {it.HsnSac})  {it.QuantityText} @ {it.RateText}",
                string.Empty,
                IndianFormat.Amount(it.TaxableValue),
            }));
        }
        // W0-1: the on-screen mirror suppresses exactly what InvoicePdf suppresses — a bill of supply shows Rule 49(g)'s
        // value of supply and no tax head at all. If the two disagreed the operator would approve one document and
        // issue another.
        rows.Add(PrintRow.Total(inv.IsBillOfSupply ? "Value of Supply" : "Taxable Value",
            string.Empty, IndianFormat.AmountAlways(inv.TotalTaxable)));
        if (!inv.IsBillOfSupply)
        {
            // W0-15: three-valued, and the mirror states exactly what InvoicePdf.HeadRows states. A null routing names
            // NO head — if the two disagreed the operator would approve one document and issue another. Where a null
            // routing nonetheless carries tax (only reachable from a hand-built DTO — the projector's null routing
            // means no forward leg was posted), both surfaces state the AMOUNT under the head-free label "Tax", so
            // neither shows a Grand Total exceeding the visible rows by an unexplained figure.
            // T0-11 slice S2 — WHOSE tax the head rows below state. The mirror has no per-rate breakup table, so
            // it cannot carry the caption where InvoicePdf carries it; without this the operator would approve a
            // screen showing "IGST 17,473.31" with nothing on it saying the charge is the supplier's, which is the
            // one thing RQ-11a makes binding about a record's tax.
            // T0-11 review C24/L3-10 — "whose tax" is the ORIENTATION question, the same axis `InvoicePdf` reads for
            // the same caption. The wording is untouched (open R12 question, plan.md Phase 10.13).
            if (inv.Heads == PartyOrientation.WeAreRecipient)
                rows.Add(PrintRow.Header(GstReportSupport.SupplierTaxCaption, string.Empty, string.Empty));
            if (inv.IsInterState is true)
                rows.Add(new PrintRow(new[] { "IGST", string.Empty, IndianFormat.AmountAlways(inv.TotalIgst) }));
            else if (inv.IsInterState is false)
            {
                rows.Add(new PrintRow(new[] { "CGST", string.Empty, IndianFormat.AmountAlways(inv.TotalCgst) }));
                rows.Add(new PrintRow(new[] { "SGST", string.Empty, IndianFormat.AmountAlways(inv.TotalSgst) }));
            }
            else if (inv.TotalTax.Amount != 0m)
                rows.Add(new PrintRow(new[] { "Tax", string.Empty, IndianFormat.AmountAlways(inv.TotalTax) }));
            // W0-1 follow-up (review finding #3): the ring-fenced Compensation Cess, which InvoicePdf.DrawClosingBlock
            // has printed on its own line since FIX-1 and this mirror did not. Cess is OUT of TotalTax but IN
            // GrandTotal (the accept path adds it to the party leg), so omitting the row left the operator approving a
            // page whose visible money rows summed to 55,810.14 under a printed Grand Total of 64,323.55 — the whole
            // 8,513.41 of cess invisible. Same condition and same position as the renderer, so a cess-free invoice
            // mirrors exactly as before (ER-13).
            if (inv.TotalCess.Amount != 0m)
                rows.Add(new PrintRow(new[] { "Compensation Cess", string.Empty, IndianFormat.AmountAlways(inv.TotalCess) }));
        }
        // T0-11 review C1 (L1-01): the posted party-side charges that are neither goods nor GST/cess — an additional
        // cost of purchase on a record, §206C TCS on an outward invoice. Same condition and same POSITION as
        // InvoicePdf.DrawClosingBlock (outside the bill-of-supply branch, after the heads, before the round-off): the
        // whole defect was a Grand Total that exceeded the visible rows by an amount the page never mentioned, and a
        // mirror that reproduced it would have the operator approve one document and issue another. Empty on every
        // document that bears none ⇒ the pane is unchanged (ER-13).
        foreach (var charge in inv.OtherCharges)
            rows.Add(new PrintRow(new[] { charge.Caption, string.Empty, IndianFormat.AmountAlways(charge.Amount) }));
        if (inv.RoundOff.Amount != 0m)
            rows.Add(new PrintRow(new[] { "Round Off", string.Empty, IndianFormat.AmountAlways(inv.RoundOff) }));
        rows.Add(PrintRow.Total(inv.IsBillOfSupply ? "Total" : "Grand Total",
            string.Empty, IndianFormat.AmountAlways(inv.GrandTotal)));
        if (cfg.ShowNarration && !string.IsNullOrWhiteSpace(inv.Narration))
            rows.Add(PrintRow.Header("Narration: " + inv.Narration, string.Empty, string.Empty));

        // The title override cannot re-title a bill of supply — mirroring InvoicePdf.Render, so the preview and the
        // bytes can never differ on what document this is.
        // FIX-W1h: mirror the renderer's FALLBACKS too, not just its override rule. `DocumentTitle` now defaults to
        // empty (so a caller who sets only IsBillOfSupply cannot be handed a page titled TAX INVOICE), which means
        // both branches need the same structural derivation InvoicePdf.Render applies — otherwise the operator's
        // screen would show a blank heading over bytes that are correctly titled.
        string title;
        if (inv.IsRecipientRecord)
        {
            // T0-11 slice S2 — the same three-branch derivation InvoicePdf.Render applies, record first and both
            // outward titles refused, so the pane the operator approves and the bytes that leave the building cannot
            // name two different documents.
            title = inv.DocumentTitle;
            if (string.IsNullOrWhiteSpace(title)
                || title.Trim().Equals(GstReportSupport.TaxInvoiceTitle, StringComparison.OrdinalIgnoreCase)
                || title.Trim().Equals(GstReportSupport.BillOfSupplyTitle, StringComparison.OrdinalIgnoreCase))
                title = GstReportSupport.PurchaseRecordTitle;
        }
        else if (inv.StatesSection34Note)
        {
            // T0-11 slice S4 — the same structural refusal InvoicePdf.Render applies to a §34 note: the NATURE OF
            // THE DOCUMENT is a mandatory Rule-53 particular, so the F12 title override does not reach it, and a
            // note carrying anything but a note title is left untitled rather than titled with a guess (there are
            // two note titles and the DTO does not say which). If the mirror and the bytes disagreed here the
            // operator would approve one document and issue another.
            title = inv.DocumentTitle?.Trim() ?? string.Empty;
            if (!title.Equals(GstReportSupport.CreditNoteTitle, StringComparison.OrdinalIgnoreCase)
                && !title.Equals(GstReportSupport.DebitNoteTitle, StringComparison.OrdinalIgnoreCase))
                title = string.Empty;
        }
        else if (inv.IsBillOfSupply)
        {
            // FIX-W2b: case-insensitive (and trimmed), mirroring InvoicePdf.Render — an ordinal compare let the
            // spelling "Tax Invoice" through and headed the operator's own approval screen with it.
            title = inv.DocumentTitle;
            if (string.IsNullOrWhiteSpace(title) ||
                title.Trim().Equals(GstReportSupport.TaxInvoiceTitle, StringComparison.OrdinalIgnoreCase))
                title = GstReportSupport.BillOfSupplyTitle;
        }
        else
        {
            title = string.IsNullOrEmpty(cfg.TitleOverride) ? inv.DocumentTitle : cfg.TitleOverride!;
            if (string.IsNullOrWhiteSpace(title)) title = GstReportSupport.TaxInvoiceTitle;
        }
        return new PrintReport
        {
            Title = title,
            Subtitle = inv.Seller.Name,
            Columns = new[]
            {
                new PrintColumn("Particulars", 4, CellAlign.Left),
                new PrintColumn(string.Empty, 1, CellAlign.Right),
                new PrintColumn("Amount", 1.5, CellAlign.Right),
            },
            Rows = rows,
        };
    }

    private PrintReport BuildReceiptPreviewReport()
    {
        var r = _receipt!;
        var rows = new List<PrintRow>();
        // Rule 5(1)(f): a composition taxable person's wording sits at the TOP of the bill of supply — so it is the
        // mirror's first row too, matching where PosReceiptPdf draws it (finding #6: gated on the structural flag in
        // lockstep with the renderer, so the mirror can never show a declaration the bytes suppress).
        if (r.IsBillOfSupply && !string.IsNullOrWhiteSpace(r.TopDeclaration))
            rows.Add(PrintRow.Header(r.TopDeclaration, string.Empty, string.Empty));
        rows.Add(PrintRow.Header($"Bill No. {r.BillNumber}", "Date", r.DateText));
        rows.Add(PrintRow.Header("Customer: " + (string.IsNullOrWhiteSpace(r.Party) ? "(cash)" : r.Party),
            string.Empty, string.Empty));

        foreach (var it in r.Items)
            rows.Add(new PrintRow(new[]
            {
                $"{it.Description}  {it.QuantityText} @ {it.RateText}",
                string.Empty,
                IndianFormat.Amount(it.Value),
            }));

        // W0-1b: the mirror suppresses exactly what PosReceiptPdf suppresses — a bill of supply shows Rule 49(g)'s
        // value of supply and no tax head at all. If the two disagreed the operator would approve one document over
        // the counter and hand the customer another (the same failure FIX-W1f caught on the invoice path).
        rows.Add(PrintRow.Total(r.IsBillOfSupply ? "Value of Supply" : "Taxable",
            string.Empty, IndianFormat.AmountAlways(r.TotalTaxable)));
        if (!r.IsBillOfSupply)
        {
            if (r.IsInterState)
                rows.Add(new PrintRow(new[] { "IGST", string.Empty, IndianFormat.AmountAlways(r.TotalIgst) }));
            else
            {
                rows.Add(new PrintRow(new[] { "CGST", string.Empty, IndianFormat.AmountAlways(r.TotalCgst) }));
                rows.Add(new PrintRow(new[] { "SGST", string.Empty, IndianFormat.AmountAlways(r.TotalSgst) }));
            }
        }
        rows.Add(PrintRow.Total(r.IsBillOfSupply ? "Total" : "Grand Total",
            string.Empty, IndianFormat.AmountAlways(r.GrandTotal)));

        rows.Add(PrintRow.Header("Payment", string.Empty, string.Empty));
        foreach (var t in r.Tenders)
            rows.Add(new PrintRow(new[] { "  " + t.Label, string.Empty, IndianFormat.AmountAlways(t.Amount) }));
        if (r.CashTendered.Amount > 0m)
        {
            rows.Add(new PrintRow(new[] { "  Cash Tendered", string.Empty, IndianFormat.AmountAlways(r.CashTendered) }));
            rows.Add(new PrintRow(new[] { "  Change", string.Empty, IndianFormat.AmountAlways(r.Change) }));
        }
        if (!string.IsNullOrWhiteSpace(r.Message1)) rows.Add(PrintRow.Header(r.Message1, string.Empty, string.Empty));
        if (!string.IsNullOrWhiteSpace(r.Message2)) rows.Add(PrintRow.Header(r.Message2, string.Empty, string.Empty));
        if (!string.IsNullOrWhiteSpace(r.Declaration)) rows.Add(PrintRow.Header(r.Declaration, string.Empty, string.Empty));

        // W0-1b: derived structurally, mirroring PosReceiptPdf.Render — the POS config's DefaultTitle is a print
        // preference and may not re-title a §31(3)(c) bill of supply.
        var title = r.IsBillOfSupply
            ? GstReportSupport.BillOfSupplyTitle
            : (string.IsNullOrWhiteSpace(r.Title) ? "RETAIL INVOICE" : r.Title);
        return new PrintReport
        {
            Title = title,
            Subtitle = r.StoreName,
            Columns = new[]
            {
                new PrintColumn("Particulars", 4, CellAlign.Left),
                new PrintColumn(string.Empty, 1, CellAlign.Right),
                new PrintColumn("Amount", 1.5, CellAlign.Right),
            },
            Rows = rows,
        };
    }

    /// <summary>A lightweight on-screen text mirror of the Payslip PDF (the authoritative bytes come from
    /// <c>PayslipPdf</c>): the identity line, the earnings, the deductions, the net pay and the amount in words.</summary>
    private PrintReport BuildPayslipPreviewReport()
    {
        var s = _payslip!;
        var rows = new List<PrintRow>
        {
            PrintRow.Header($"{s.EmployeeName}  ({(string.IsNullOrEmpty(s.EmployeeNumber) ? "-" : s.EmployeeNumber)})", string.Empty),
            PrintRow.Header("Earnings", string.Empty),
        };
        foreach (var e in s.Earnings) rows.Add(new PrintRow(ReportPrintProjector.Ascii(e.Name), IndianFormat.AmountAlways(e.Amount)));
        rows.Add(PrintRow.Total("Gross Earnings", IndianFormat.AmountAlways(s.GrossEarnings)));
        rows.Add(PrintRow.Header("Deductions", string.Empty));
        foreach (var d in s.Deductions) rows.Add(new PrintRow(ReportPrintProjector.Ascii(d.Name), IndianFormat.AmountAlways(d.Amount)));
        rows.Add(PrintRow.Total("Total Deductions", IndianFormat.AmountAlways(s.TotalDeductions)));
        rows.Add(PrintRow.Total("Net Pay", IndianFormat.AmountAlways(s.NetPayable)));
        rows.Add(PrintRow.Header("Net Pay (in words): " + ReportPrintProjector.Ascii(IndianAmountInWords.Convert(s.NetPayable.Amount)), string.Empty));

        return new PrintReport
        {
            Title = "Payslip",
            Subtitle = ReportPrintProjector.Ascii(s.EmployerName),
            Columns = new[]
            {
                new PrintColumn("Particulars", 3, CellAlign.Left),
                new PrintColumn("Amount", 1.5, CellAlign.Right),
            },
            Rows = rows,
        };
    }
}

/// <summary>One rendered preview page: the repeated title/subtitle band, the column headers and the body lines,
/// plus its 1-based page number and the total page count for the "Page x of N" caption.</summary>
public sealed class PreviewPage
{
    public string Title { get; }
    public string Subtitle { get; }

    /// <summary>The column headings as the pane lays them out — the caption AND the column's width, so the header
    /// band and the body cells below it can never line up under different captions.</summary>
    public IReadOnlyList<PreviewCell> HeaderColumns { get; }

    /// <summary>The heading TEXTS. A view over <see cref="HeaderColumns"/>, materialised once — never a second
    /// copy of the same answer.</summary>
    public IReadOnlyList<string> Headers { get; }

    /// <summary>The page's column layout. It IS <see cref="HeaderColumns"/> — the header band and every body cell
    /// are laid out from one set of widths, so a figure can never line up under a caption that does not govern
    /// it — and this name is here for readers (and tests) asking about the layout rather than about the captions.</summary>
    public IReadOnlyList<PreviewCell> Columns => HeaderColumns;

    public IReadOnlyList<PreviewLine> Lines { get; }
    public int PageNumber { get; }
    public int TotalPages { get; private set; }

    public PreviewPage(string title, string subtitle, IReadOnlyList<PreviewCell> headerColumns,
        IReadOnlyList<PreviewLine> lines, int pageNumber)
    {
        Title = title;
        Subtitle = subtitle;
        HeaderColumns = headerColumns;
        Headers = headerColumns.Select(c => c.Text).ToList();
        Lines = lines;
        PageNumber = pageNumber;
        TotalPages = pageNumber;
    }

    public void SetTotalPages(int total) => TotalPages = total;

    /// <summary>The brand-safe footer caption for this page.</summary>
    public string Footer => $"Apex Solutions  -  Page {PageNumber} of {TotalPages}";
}

/// <summary>
/// One cell of the preview sheet: the text, and the width of the column it sits in.
///
/// <para>The width is DATA rather than markup because a literal in the cell template made the truncation invariant
/// under every window size and every DPI (T0-11 review CRITIC-01 / C17-L3-03): a report whose model says its first
/// column is four times the width of its amount column got the same 120 DIP for both. It is computed once per page
/// from <see cref="PrintColumn.Weight"/> — the weights the PDF renderer has always split the paper by — so the pane
/// and the paper give the same column the same share.</para>
///
/// <para>This is the shell's own established shape for a width-carrying cell, not a new one: the payroll matrix grid
/// binds <c>PayrollMatrixCellVm</c>'s <c>Text</c>/<c>Width</c> pair exactly this way (<c>MainWindow.axaml</c>), for
/// the same reason — a horizontal StackPanel keeps its columns aligned only if every cell's width is stated.</para>
/// </summary>
/// <param name="Text">The already-formatted cell text.</param>
/// <param name="Width">The column's width in DIP.</param>
public sealed record PreviewCell(string Text, double Width);

/// <summary>One preview body line: the per-column cells, plus header/total styling flags.</summary>
public sealed class PreviewLine
{
    /// <summary>The cells as the pane lays them out: text plus the width of the column each sits in.</summary>
    public IReadOnlyList<PreviewCell> Columns { get; }

    /// <summary>The cell TEXTS. A view over <see cref="Columns"/>, materialised once in the constructor — one
    /// source of truth, not a parallel copy that could drift from it.</summary>
    public IReadOnlyList<string> Cells { get; }

    public bool IsHeader { get; }
    public bool IsTotal { get; }

    public PreviewLine(IReadOnlyList<PreviewCell> columns, bool isHeader, bool isTotal)
    {
        Columns = columns;
        Cells = columns.Select(c => c.Text).ToList();
        IsHeader = isHeader;
        IsTotal = isTotal;
    }
}
