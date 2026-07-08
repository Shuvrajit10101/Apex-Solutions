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
///   <item><see cref="CopyMarking"/> — the copy label (Original for Recipient / Duplicate for Supplier /
///     Triplicate for Transporter), or None.</item>
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

    public PrintConfigViewModel(PrintPreviewViewModel preview)
    {
        _preview = preview ?? throw new ArgumentNullException(nameof(preview));
        // Seed from the preview's current knobs so re-opening reflects prior edits.
        TitleOverride = preview.TitleOverride;
        ShowNarration = preview.ShowNarration;
        CopyMarking = preview.CopyMarking;
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
    }
}
