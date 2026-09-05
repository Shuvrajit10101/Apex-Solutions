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
    /// True when the W2-31 <b>page RANGE</b> knobs apply — the first/last page and the starting page number.
    ///
    /// <para>🔴 <b>This is a SEPARATE predicate from <see cref="SupportsPageKnobs"/>, and the split is the whole
    /// point.</b> W2-31's finishing pass taught <c>InvoicePdf</c>, <c>VoucherPdf</c>, <c>PayslipPdf</c> and
    /// <c>PosReceiptPdf</c> the F10 range and starting number — they read <c>PageConfig.IncludesPage</c> and
    /// <c>StartPageNumber</c> exactly as <c>ReportPdf</c> does, under the same rule (renumber, then select; a
    /// selection of nothing is one blank sheet, never the whole document). The FORMAT and the PAPER are still
    /// <c>ReportPdf</c>-only.</para>
    ///
    /// <para>Widening the single old predicate would therefore have re-offered two knobs that remain inert —
    /// re-creating the exact defect the withdrawal fixed. One boolean cannot carry two answers; that is the same
    /// lesson <c>PrintedDocumentClass</c> learned one layer down.
    /// <c>PrintConfigKnobsMoveTheBytesTests</c> now holds BOTH pairings by comparing rendered bytes.</para>
    /// </summary>
    public bool SupportsPageRange => true;

    /// <summary>
    /// True when the W2-31 <b>page-layout</b> knobs apply — the print format and the paper toggle.
    ///
    /// <para>🔴 <b>This returned a bare <c>true</c>, and that was wrong.</b> Measured against the renderers,
    /// only <see cref="ReportPdf"/> reads <c>PageConfig</c>'s <c>Formatted*</c>, <c>Draws*</c>,
    /// <c>IncludesPage</c> and <c>StartPageNumber</c> members. <c>InvoicePdf</c>, <c>VoucherPdf</c>,
    /// <c>PayslipPdf</c> and <c>PosReceiptPdf</c> read <b>only</b> <c>EffectiveCopies</c>. So over a voucher or
    /// invoice the panel showed a format, a paper choice, a page range and a starting number that the renderer
    /// then ignored: the operator could set them, apply, and get byte-identical output. Offering a control that
    /// does nothing is the same defect as a caption naming a key that is not bound, and it is now gated on the
    /// renderer that will actually be asked to honour it. <c>PrintConfigKnobsMoveTheBytesTests</c> holds this
    /// by rendering and comparing bytes, so it cannot be satisfied by relabelling.</para>
    ///
    /// <para><b>Half of that has since been done, and it is why this summary no longer mentions the range.</b>
    /// The four document renderers now honour <c>IncludesPage</c> and <c>StartPageNumber</c>, so the range moved
    /// out to <see cref="SupportsPageRange"/> and is offered everywhere. What is still <c>ReportPdf</c>-only is
    /// the FORMAT and the PAPER — <c>Formatted*</c> and <c>Draws*</c> — so those two stay gated here. Teaching the
    /// document renderers a dot-matrix pitch and a pre-printed suppression remains open work, and when it is done
    /// this predicate widens the same way the range did.</para>
    /// </summary>
    /// <remarks>
    /// W2-32 widened this from <c>== Report</c> to include <c>ReportSet</c>. That is not a relabelling: a set
    /// renders through <see cref="ReportPdf"/>'s multi-document overload, which reads the same
    /// <c>Formatted*</c> / <c>Draws*</c> / <c>IncludesPage</c> / <c>StartPageNumber</c> members — and the F10
    /// range is at its most useful over a job, where "reprint sheets 4-6" is the whole point.
    /// <c>PrintConfigKnobsMoveTheBytesTests</c> holds the pairing by comparing rendered bytes, so this widening
    /// is only legal because the renderer really does honour them.
    /// </remarks>
    public bool SupportsPageKnobs =>
        _preview.Kind is PrintPreviewViewModel.PrintKind.Report or PrintPreviewViewModel.PrintKind.ReportSet;

    /// <summary>
    /// True whenever the copy count applies — which is <b>always</b>: every one of the five renderers ends with
    /// <c>writer.RepeatAllPages(page.EffectiveCopies)</c>. Kept separate from <see cref="SupportsPageKnobs"/> so
    /// withdrawing the inert layout knobs from a document cannot also withdraw the one that works.
    /// </summary>
    public bool SupportsCopies => true;

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

    // ---- W2-31 (census 12.4): the page knobs. The key names F8 / F9 / F5 / F10 are NOT used here: no key
    // is routed for this panel (`PrintConfigPanel` appears nowhere in MainWindow.axaml.cs), so naming one
    // would assert a binding that does not exist. ----------------------------------------

    /// <summary>The print format (Neat / Dot Matrix / Quick-Draft). Applied on <see cref="Apply"/>.</summary>
    [ObservableProperty] private PrintFormat _printFormat = PrintFormat.Neat;

    /// <summary>Plain paper or pre-printed stationery. Applied on <see cref="Apply"/>.</summary>
    [ObservableProperty] private PaperKind _paper = PaperKind.Plain;

    /// <summary>How many collated copies of the whole document. Applied on <see cref="Apply"/>.</summary>
    [ObservableProperty] private int _copies = 1;

    /// <summary>The first page to print (1-based). Applied on <see cref="Apply"/>.</summary>
    [ObservableProperty] private int _firstPage = 1;

    /// <summary>The last page to print; 0 = to the end. Applied on <see cref="Apply"/>.</summary>
    [ObservableProperty] private int _lastPage;

    /// <summary>The number the first printed sheet carries. Applied on <see cref="Apply"/>.</summary>
    [ObservableProperty] private int _startPageNumber = 1;

    // Radio-style bindings for the print format (one true at a time).
    public bool IsNeat { get => PrintFormat == PrintFormat.Neat; set { if (value) PrintFormat = PrintFormat.Neat; } }
    public bool IsDotMatrix { get => PrintFormat == PrintFormat.DotMatrix; set { if (value) PrintFormat = PrintFormat.DotMatrix; } }
    public bool IsQuickDraft { get => PrintFormat == PrintFormat.QuickDraft; set { if (value) PrintFormat = PrintFormat.QuickDraft; } }

    // Radio-style bindings for the paper axis.
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
