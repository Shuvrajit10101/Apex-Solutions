using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using Apex.Desktop.Services;
using Apex.Ledger.Io;
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
    /// <summary>What this preview is printing: a report (RQ-9), a plain voucher (RQ-10) or a tax invoice (RQ-11).
    /// The document mode selects the Io renderer and the F12 config knobs that apply.</summary>
    public enum PrintKind { Report, Voucher, Invoice, Receipt }

    // Exactly one of these is set per instance (by the chosen ctor); it drives the render + preview.
    private readonly PrintReport? _report;
    private readonly VoucherPrintData? _voucher;
    private readonly InvoicePrintData? _invoice;
    private readonly PosReceiptData? _receipt;

    /// <summary>The page config the preview + PDF are rendered with. Rebuilt (and the document re-rendered) when
    /// the size/orientation is changed via the toggles below.</summary>
    private PageConfig _config;

    /// <summary>The report projection the on-screen preview paginates. In report mode it IS the report; in
    /// voucher/invoice mode it is a lightweight text projection of the voucher/invoice (rebuilt each render so
    /// the narration toggle / copy label / title override are reflected on screen too). The authoritative bytes
    /// always come from the Io renderer — this is presentation-only.</summary>
    private PrintReport _previewReport = new();

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

    /// <summary>The page count of the rendered PDF / preview (for the heading).</summary>
    public int PageCount => Pages.Count;

    public PrintPreviewViewModel(ReportsViewModel reportVm)
        : this(ReportPrintProjector.Project(reportVm), reportVm?.Title ?? string.Empty) { }

    /// <summary>Testable ctor: preview a pre-built report print model directly (RQ-9).</summary>
    public PrintPreviewViewModel(PrintReport report, string reportTitle)
    {
        _report = report ?? throw new ArgumentNullException(nameof(report));
        Kind = PrintKind.Report;
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

    /// <summary>Preview a GST tax invoice (RQ-11) via <c>InvoicePdf</c>; the F12 knobs apply.</summary>
    public PrintPreviewViewModel(InvoicePrintData invoice)
    {
        _invoice = invoice ?? throw new ArgumentNullException(nameof(invoice));
        Kind = PrintKind.Invoice;
        ReportTitle = string.IsNullOrEmpty(invoice.InvoiceNumber)
            ? "Tax Invoice"
            : $"Tax Invoice No. {invoice.InvoiceNumber}";
        _config = BuildConfig();
        Render();
    }

    /// <summary>Preview a POS retail receipt (Phase 6 slice 7 RQ-44) via <c>PosReceiptPdf</c>. A receipt is a fixed
    /// retail bill layout — the F12 title/narration/copy knobs do not apply.</summary>
    public PrintPreviewViewModel(PosReceiptData receipt)
    {
        _receipt = receipt ?? throw new ArgumentNullException(nameof(receipt));
        Kind = PrintKind.Receipt;
        ReportTitle = string.IsNullOrEmpty(receipt.BillNumber)
            ? "Retail Receipt"
            : $"Retail Receipt No. {receipt.BillNumber}";
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
            _ => ReportPdf.Render(_report!, _config),
        };
        OnPropertyChanged(nameof(PdfBytes));

        _previewReport = Kind switch
        {
            PrintKind.Voucher => BuildVoucherPreviewReport(),
            PrintKind.Invoice => BuildInvoicePreviewReport(),
            PrintKind.Receipt => BuildReceiptPreviewReport(),
            _ => _report!,
        };

        Pages.Clear();
        int pageNo = 0;
        foreach (var rows in PaginateForPreview())
        {
            pageNo++;
            Pages.Add(BuildPreviewPage(rows, pageNo));
        }
        if (Pages.Count == 0)
            Pages.Add(BuildPreviewPage(new List<PrintRow>(), 1));

        // Backfill the "of N" now the total is known.
        foreach (var p in Pages) p.SetTotalPages(Pages.Count);

        OnPropertyChanged(nameof(PageCount));
    }

    partial void OnUseLetterChanged(bool value) => Render();
    partial void OnLandscapeChanged(bool value) => Render();

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

    private IEnumerable<List<PrintRow>> PaginateForPreview()
    {
        // Approximate the renderer's rows-per-page from the content height and row height so the preview page
        // breaks read like the PDF. This is presentation-only; the authoritative bytes come from ReportPdf.
        double contentHeight = _config.PageHeight - _config.MarginTop - _config.MarginBottom
            - (_config.TitleFontSize + _config.SubtitleFontSize + _config.HeaderFontSize + 20)
            - (_config.FooterFontSize + 6);
        int perPage = Math.Max(1, (int)(contentHeight / _config.RowHeight));

        var current = new List<PrintRow>();
        foreach (var row in _previewReport.Rows)
        {
            if (current.Count >= perPage)
            {
                yield return current;
                current = new List<PrintRow>();
            }
            current.Add(row);
        }
        if (current.Count > 0) yield return current;
    }

    private PreviewPage BuildPreviewPage(List<PrintRow> rows, int pageNo)
    {
        var lines = new List<PreviewLine>(rows.Count);
        foreach (var r in rows)
        {
            var cells = new List<string>(_previewReport.Columns.Count);
            for (int i = 0; i < _previewReport.Columns.Count; i++)
            {
                string text = i < r.Cells.Count ? (r.Cells[i] ?? string.Empty) : string.Empty;
                if (i == 0 && r.Indent > 0) text = new string(' ', r.Indent) + text;
                cells.Add(text);
            }
            lines.Add(new PreviewLine(cells, r.IsHeader, r.IsTotal));
        }

        var headers = new List<string>(_previewReport.Columns.Count);
        foreach (var c in _previewReport.Columns) headers.Add(c.Header);

        return new PreviewPage(_previewReport.Title, _previewReport.Subtitle, headers, lines, pageNo);
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
        if (!string.IsNullOrEmpty(cfg.CopyMarkingLabel))
            rows.Add(PrintRow.Header(cfg.CopyMarkingLabel, string.Empty, string.Empty));
        rows.Add(PrintRow.Header($"Invoice No. {inv.InvoiceNumber}", "Date", inv.InvoiceDateText));
        rows.Add(PrintRow.Header("Buyer: " + inv.Buyer.Name, string.Empty, string.Empty));
        rows.Add(PrintRow.Header("Place of Supply: " + inv.PlaceOfSupply, string.Empty, string.Empty));

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
        rows.Add(PrintRow.Total("Taxable Value", string.Empty, IndianFormat.AmountAlways(inv.TotalTaxable)));
        if (inv.IsInterState)
            rows.Add(new PrintRow(new[] { "IGST", string.Empty, IndianFormat.AmountAlways(inv.TotalIgst) }));
        else
        {
            rows.Add(new PrintRow(new[] { "CGST", string.Empty, IndianFormat.AmountAlways(inv.TotalCgst) }));
            rows.Add(new PrintRow(new[] { "SGST", string.Empty, IndianFormat.AmountAlways(inv.TotalSgst) }));
        }
        if (inv.RoundOff.Amount != 0m)
            rows.Add(new PrintRow(new[] { "Round Off", string.Empty, IndianFormat.AmountAlways(inv.RoundOff) }));
        rows.Add(PrintRow.Total("Grand Total", string.Empty, IndianFormat.AmountAlways(inv.GrandTotal)));
        if (cfg.ShowNarration && !string.IsNullOrWhiteSpace(inv.Narration))
            rows.Add(PrintRow.Header("Narration: " + inv.Narration, string.Empty, string.Empty));

        var title = string.IsNullOrEmpty(cfg.TitleOverride) ? "TAX INVOICE" : cfg.TitleOverride!;
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

        rows.Add(PrintRow.Total("Taxable", string.Empty, IndianFormat.AmountAlways(r.TotalTaxable)));
        if (r.IsInterState)
            rows.Add(new PrintRow(new[] { "IGST", string.Empty, IndianFormat.AmountAlways(r.TotalIgst) }));
        else
        {
            rows.Add(new PrintRow(new[] { "CGST", string.Empty, IndianFormat.AmountAlways(r.TotalCgst) }));
            rows.Add(new PrintRow(new[] { "SGST", string.Empty, IndianFormat.AmountAlways(r.TotalSgst) }));
        }
        rows.Add(PrintRow.Total("Grand Total", string.Empty, IndianFormat.AmountAlways(r.GrandTotal)));

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

        var title = string.IsNullOrWhiteSpace(r.Title) ? "RETAIL INVOICE" : r.Title;
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
}

/// <summary>One rendered preview page: the repeated title/subtitle band, the column headers and the body lines,
/// plus its 1-based page number and the total page count for the "Page x of N" caption.</summary>
public sealed class PreviewPage
{
    public string Title { get; }
    public string Subtitle { get; }
    public IReadOnlyList<string> Headers { get; }
    public IReadOnlyList<PreviewLine> Lines { get; }
    public int PageNumber { get; }
    public int TotalPages { get; private set; }

    public PreviewPage(string title, string subtitle, IReadOnlyList<string> headers,
        IReadOnlyList<PreviewLine> lines, int pageNumber)
    {
        Title = title;
        Subtitle = subtitle;
        Headers = headers;
        Lines = lines;
        PageNumber = pageNumber;
        TotalPages = pageNumber;
    }

    public void SetTotalPages(int total) => TotalPages = total;

    /// <summary>The brand-safe footer caption for this page.</summary>
    public string Footer => $"Apex Solutions  -  Page {PageNumber} of {TotalPages}";
}

/// <summary>One preview body line: the per-column cell texts, plus header/total styling flags.</summary>
public sealed class PreviewLine
{
    public IReadOnlyList<string> Cells { get; }
    public bool IsHeader { get; }
    public bool IsTotal { get; }

    public PreviewLine(IReadOnlyList<string> cells, bool isHeader, bool isTotal)
    {
        Cells = cells;
        IsHeader = isHeader;
        IsTotal = isTotal;
    }
}
