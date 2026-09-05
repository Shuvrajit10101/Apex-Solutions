using System;
using Apex.Ledger.Io;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Apex.Desktop.ViewModels;

/// <summary>
/// The F12 print Configuration panel for a voucher / tax-invoice <see cref="PrintPreviewViewModel"/> (RQ-12),
/// hosted as its own cascading Miller-column to the right of the preview it configures — never a stacked
/// overlay, mirroring <see cref="ReportConfigViewModel"/>. It edits the print-time knobs:
/// <list type="bullet">
///   <item>a document <see cref="TitleOverride"/> (e.g. "TAX INVOICE" ⇒ "PROFORMA INVOICE"; blank ⇒ default);</item>
///   <item><see cref="ShowNarration"/> — whether the narration line prints;</item>
///   <item><see cref="CopyMarking"/> — the CGST Rule 48(1) copy label (Original for Recipient / Duplicate for
///     Transporter / Triplicate for Supplier), or None. The pairing is the rule's, verbatim; it shipped
///     transposed and is corrected in <see cref="Apex.Ledger.Io.CopyMarking"/>, which carries the rule text and
///     its CBIC source (T0-11 review C10/L1-10).</item>
/// </list>
/// On <see cref="Apply"/> the values are pushed back onto the preview VM, which re-renders the PDF + on-screen
/// preview in place. The panel opens seeded from the preview's current knobs, so opening → applying with no
/// edits is a no-op that preserves the current output exactly.
///
/// <para><b>Deferred (DP-9):</b> company-logo image embedding is a later polish slice and is not offered here.</para>
/// </summary>
public sealed partial class PrintConfigViewModel : ViewModelBase
{
    private readonly PrintPreviewViewModel _preview;

    /// <summary>The column title / heading for the config panel.</summary>
    public string Title => "Print Config — F12";

    /// <summary>The document being configured (its heading line).</summary>
    public string DocumentTitle => _preview.ReportTitle;

    /// <summary>
    /// True when the RQ-12 <b>document</b> knobs apply — the title override, the narration toggle and the CGST
    /// Rule 48(1) copy marking. Those are voucher/invoice-only; a report has no narration and no statutory copy.
    /// </summary>
    public bool SupportsDocumentKnobs => _preview.SupportsPrintConfig;

    /// <summary>
    /// True when the W2-31 <b>page</b> knobs apply — the F8 print format, the F9 paper toggle, the F5 copy count
    /// and the F10 range/starting number. They apply to EVERY preview kind, which is why this panel now opens
    /// over a report preview as well; before this slice a report had no configuration column at all.
    /// </summary>
    public bool SupportsPageKnobs => true;

    /// <summary>F12: an optional document-title override (blank ⇒ the template default). Applied on <see cref="Apply"/>.</summary>
    [ObservableProperty] private string _titleOverride = string.Empty;

    /// <summary>F12: whether the narration line prints (default on). Applied on <see cref="Apply"/>.</summary>
    [ObservableProperty] private bool _showNarration = true;

    /// <summary>F12: the copy-marking selection. Applied on <see cref="Apply"/>.</summary>
    [ObservableProperty] private CopyMarking _copyMarking = CopyMarking.None;

    // Radio-style bindings for the copy-marking choices (one true at a time).
    public bool IsCopyNone { get => CopyMarking == CopyMarking.None; set { if (value) CopyMarking = CopyMarking.None; } }
    public bool IsCopyOriginal { get => CopyMarking == CopyMarking.Original; set { if (value) CopyMarking = CopyMarking.Original; } }
    public bool IsCopyDuplicate { get => CopyMarking == CopyMarking.Duplicate; set { if (value) CopyMarking = CopyMarking.Duplicate; } }
    public bool IsCopyTriplicate { get => CopyMarking == CopyMarking.Triplicate; set { if (value) CopyMarking = CopyMarking.Triplicate; } }

    // ---- W2-31 (census 12.4): the F8 / F9 / F5 / F10 page knobs ----------------------------------------

    /// <summary>F8: the print format (Neat / Dot Matrix / Quick-Draft). Applied on <see cref="Apply"/>.</summary>
    [ObservableProperty] private PrintFormat _printFormat = PrintFormat.Neat;

    /// <summary>F9: plain paper or pre-printed stationery. Applied on <see cref="Apply"/>.</summary>
    [ObservableProperty] private PaperKind _paper = PaperKind.Plain;

    /// <summary>F5: how many collated copies of the whole document. Applied on <see cref="Apply"/>.</summary>
    [ObservableProperty] private int _copies = 1;

    /// <summary>F10: the first page to print (1-based). Applied on <see cref="Apply"/>.</summary>
    [ObservableProperty] private int _firstPage = 1;

    /// <summary>F10: the last page to print; 0 = to the end. Applied on <see cref="Apply"/>.</summary>
    [ObservableProperty] private int _lastPage;

    /// <summary>F10: the number the first printed sheet carries. Applied on <see cref="Apply"/>.</summary>
    [ObservableProperty] private int _startPageNumber = 1;

    // Radio-style bindings for the F8 format (one true at a time).
    public bool IsNeat { get => PrintFormat == PrintFormat.Neat; set { if (value) PrintFormat = PrintFormat.Neat; } }
    public bool IsDotMatrix { get => PrintFormat == PrintFormat.DotMatrix; set { if (value) PrintFormat = PrintFormat.DotMatrix; } }
    public bool IsQuickDraft { get => PrintFormat == PrintFormat.QuickDraft; set { if (value) PrintFormat = PrintFormat.QuickDraft; } }

    // Radio-style bindings for the F9 paper axis.
    public bool IsPlainPaper { get => Paper == PaperKind.Plain; set { if (value) Paper = PaperKind.Plain; } }
    public bool IsPrePrinted { get => Paper == PaperKind.PrePrinted; set { if (value) Paper = PaperKind.PrePrinted; } }

    public PrintConfigViewModel(PrintPreviewViewModel preview)
    {
        _preview = preview ?? throw new ArgumentNullException(nameof(preview));
        // Seed from the preview's current knobs so re-opening reflects prior edits.
        TitleOverride = preview.TitleOverride;
        ShowNarration = preview.ShowNarration;
        CopyMarking = preview.CopyMarking;
        PrintFormat = preview.PrintFormat;
        Paper = preview.Paper;
        Copies = preview.Copies;
        FirstPage = preview.FirstPage;
        LastPage = preview.LastPage;
        StartPageNumber = preview.StartPageNumber;
    }

    partial void OnPrintFormatChanged(PrintFormat value)
    {
        OnPropertyChanged(nameof(IsNeat));
        OnPropertyChanged(nameof(IsDotMatrix));
        OnPropertyChanged(nameof(IsQuickDraft));
    }

    partial void OnPaperChanged(PaperKind value)
    {
        OnPropertyChanged(nameof(IsPlainPaper));
        OnPropertyChanged(nameof(IsPrePrinted));
    }

    partial void OnCopyMarkingChanged(CopyMarking value)
    {
        OnPropertyChanged(nameof(IsCopyNone));
        OnPropertyChanged(nameof(IsCopyOriginal));
        OnPropertyChanged(nameof(IsCopyDuplicate));
        OnPropertyChanged(nameof(IsCopyTriplicate));
    }

    /// <summary>Pushes the edited knobs onto the preview VM, which re-renders the PDF + preview in place.</summary>
    public void Apply()
    {
        _preview.TitleOverride = TitleOverride ?? string.Empty;
        _preview.ShowNarration = ShowNarration;
        _preview.CopyMarking = CopyMarking;
        // W2-31: the page knobs. Guarded so a nonsense entry cannot make a document unprintable — a copy count
        // below one is one copy, and a first page below one is page one, exactly as the Io layer reads them.
        _preview.PrintFormat = PrintFormat;
        _preview.Paper = Paper;
        _preview.Copies = Copies < 1 ? 1 : Copies;
        _preview.FirstPage = FirstPage < 1 ? 1 : FirstPage;
        _preview.LastPage = LastPage < 0 ? 0 : LastPage;
        _preview.StartPageNumber = StartPageNumber < 1 ? 1 : StartPageNumber;
    }
}
