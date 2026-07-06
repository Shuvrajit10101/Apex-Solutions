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
    private readonly PrintReport _report;

    /// <summary>The page config the preview + PDF are rendered with. Rebuilt (and the report re-rendered) when
    /// the size/orientation is changed via the toggles below.</summary>
    private PageConfig _config;

    public string Title => "Print Preview";

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

    /// <summary>The page count of the rendered PDF / preview (for the heading).</summary>
    public int PageCount => Pages.Count;

    public PrintPreviewViewModel(ReportsViewModel reportVm)
        : this(ReportPrintProjector.Project(reportVm), reportVm?.Title ?? string.Empty) { }

    /// <summary>Testable ctor: preview a pre-built print model directly.</summary>
    public PrintPreviewViewModel(PrintReport report, string reportTitle)
    {
        _report = report ?? throw new ArgumentNullException(nameof(report));
        ReportTitle = reportTitle ?? string.Empty;
        _config = BuildConfig();
        Render();
    }

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
        PdfBytes = ReportPdf.Render(_report, _config);
        OnPropertyChanged(nameof(PdfBytes));

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
        foreach (var row in _report.Rows)
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
            var cells = new List<string>(_report.Columns.Count);
            for (int i = 0; i < _report.Columns.Count; i++)
            {
                string text = i < r.Cells.Count ? (r.Cells[i] ?? string.Empty) : string.Empty;
                if (i == 0 && r.Indent > 0) text = new string(' ', r.Indent) + text;
                cells.Add(text);
            }
            lines.Add(new PreviewLine(cells, r.IsHeader, r.IsTotal));
        }

        var headers = new List<string>(_report.Columns.Count);
        foreach (var c in _report.Columns) headers.Add(c.Header);

        return new PreviewPage(_report.Title, _report.Subtitle, headers, lines, pageNo);
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
