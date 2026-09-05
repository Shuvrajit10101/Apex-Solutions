using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Apex.Desktop.ViewModels;

/// <summary>One optional field group More Details can reach on the open voucher.</summary>
public sealed partial class MoreDetailsRowViewModel : ViewModelBase
{
    private readonly Func<bool> _isRevealed;
    private readonly Action _reveal;

    public MoreDetailsRowViewModel(
        string label, string owningOption, Func<bool> isRevealed, Action reveal)
    {
        Label = label;
        OwningOption = owningOption;
        _isRevealed = isRevealed;
        _reveal = reveal;
    }

    /// <summary>The field group's name, as the screen that owns it spells it.</summary>
    public string Label { get; }

    /// <summary>
    /// The screen option that is currently hiding this field group, named so the operator can see WHY the
    /// field was not on the voucher — and can see that More Details is not the same as turning it on.
    /// </summary>
    public string OwningOption { get; }

    /// <summary>True once this instance's override is set. The row template DRAWS this.</summary>
    public bool IsRevealed => _isRevealed();

    /// <summary>True while the keyboard cursor is on this row. The row template DRAWS this.</summary>
    [ObservableProperty] private bool _isHighlighted;

    /// <summary>Reveals the field group for this voucher only. Idempotent.</summary>
    public void Reveal()
    {
        _reveal();
        OnPropertyChanged(nameof(IsRevealed));
    }
}

/// <summary>
/// 14.4 — <b>MORE DETAILS (Ctrl+I)</b>.
///
/// <para><b>Fidelity (Ruling 14 tier 1).</b> help.tallysolutions.com/tally-prime/keyboard-shortcuts-tally/:
/// <c>Ctrl+I</c> — <i>"To add more details to a master or voucher for the current instance"</i>; and, on the
/// contra/banking pages, verbatim: <i>"press <b>Ctrl+I</b> (More Details) to enter any of the values
/// <b>without activating the options in F12 (Configure)</b>."</i></para>
///
/// <para>🔴 <b>THE DEFINING BEHAVIOUR IS THE HALF IN BOLD, and it is what this panel is built around.</b>
/// More Details is not a shortcut to the options screen: it reaches an option-gated field for THIS voucher and
/// leaves the option exactly as it found it. Every row here therefore writes a per-instance override flag
/// (<c>VoucherEntryViewModel.MoreDetailsBillWiseRequested</c> /
/// <c>…MoreDetailsBatchRequested</c>) and NEVER the knob beside it. A test asserts the knob is byte-identical
/// before and after, and that test is the one that must never be deleted.</para>
///
/// <para>This application has no single global "F12 &gt; Voucher Entry" page — <c>VoucherEntryViewModel</c>
/// records that the reference product abolished it and that "configuration belongs to the screen you are
/// standing on" — so the options More Details works around are this screen's own knobs. That is a difference
/// in where the option lives, not in what More Details does.</para>
///
/// <para>🔴 <b>WHAT THIS PANEL CANNOT OFFER, and it is the vendor's own headline example.</b> The vendor's
/// More Details on an invoice line reaches <b>Ledger Narration</b> — a narration PER LINE.
/// <c>Apex.Ledger.Domain.Voucher</c> carries <c>Narration</c> at the VOUCHER level and <c>EntryLine</c> has
/// none at all, so per-line narration has nowhere to be stored: adding it is a schema change, and this track
/// takes none. The panel names the omission in <see cref="Footnote"/> rather than presenting a subset as if it
/// were the feature.</para>
/// </summary>
public sealed partial class MoreDetailsViewModel : ViewModelBase
{
    /// <summary>
    /// The vendor-attested field group this panel does NOT reach. Named as a constant so the footnote and the
    /// test that locks the footnote read the same list rather than two copies of the same sentence.
    /// </summary>
    public const string WithheldField = "Ledger Narration (per line)";

    public MoreDetailsViewModel(VoucherEntryViewModel entry)
    {
        Entry = entry ?? throw new ArgumentNullException(nameof(entry));

        // ── Bill-wise Details ────────────────────────────────────────────────────────────────────────────
        // Offered only when the allocation APPLIES (an invoice on a bill-by-bill party) but the screen knob is
        // hiding it. When the knob is already off the field is on the voucher and there is nothing to reveal.
        if (entry.InvoiceBillWiseApplies && entry.UseDefaultBillWiseAllocation)
        {
            Rows.Add(new MoreDetailsRowViewModel(
                "Bill-wise Details",
                "Use default Bill-wise details for Bill Allocation",
                () => entry.MoreDetailsBillWiseRequested,
                () => entry.MoreDetailsBillWiseRequested = true));
        }

        // ── Batch / Lot Details ──────────────────────────────────────────────────────────────────────────
        if (entry.CanUseBatchWiseDetails && !entry.UseBatchWiseDetails)
        {
            Rows.Add(new MoreDetailsRowViewModel(
                "Batch / Lot Details",
                "Maintain Batch-wise details on this screen",
                () => entry.MoreDetailsBatchRequested,
                () => entry.MoreDetailsBatchRequested = true));
        }

        SetHighlight(Rows.Count == 0 ? -1 : 0);
    }

    /// <summary>The voucher this panel is standing on.</summary>
    public VoucherEntryViewModel Entry { get; }

    /// <summary>The panel's column header.</summary>
    public string Title => "More Details";

    /// <summary>The optional field groups this voucher currently hides. May be empty — see <see cref="Status"/>.</summary>
    public ObservableCollection<MoreDetailsRowViewModel> Rows { get; } = new();

    /// <summary>
    /// The line under the list. An EMPTY list is a normal, correct outcome — it means every optional field
    /// that applies to this voucher is already on the screen — and saying so is better than an empty panel
    /// that reads like a failure.
    /// </summary>
    public string Status => Rows.Count == 0
        ? "Every optional field that applies to this voucher is already on the screen."
        : "Enter ▸ show the field on this voucher. The screen option is not changed.";

    /// <summary>
    /// 🔴 THE HONEST FOOTER. It names the vendor-attested field this build cannot reach and why, so a later
    /// slice cannot quietly drop the disclosure and claim census row 14.4 complete. A test locks it.
    /// </summary>
    public string Footnote =>
        $"Not available in this build: {WithheldField} — this book stores narration on the voucher, not on the line.";

    private int _highlight = -1;

    /// <summary>The highlighted row, or null when there is nothing to reveal.</summary>
    public MoreDetailsRowViewModel? Highlighted =>
        _highlight >= 0 && _highlight < Rows.Count ? Rows[_highlight] : null;

    public void MoveDown()
    {
        if (Rows.Count == 0) return;
        SetHighlight(Math.Min(_highlight + 1, Rows.Count - 1));
    }

    public void MoveUp()
    {
        if (Rows.Count == 0) return;
        SetHighlight(Math.Max(_highlight - 1, 0));
    }

    /// <summary>Reveals the highlighted field group for this voucher. False when there is nothing highlighted.</summary>
    public bool Activate()
    {
        if (Highlighted is not { } row) return false;
        row.Reveal();
        return true;
    }

    /// <summary>The option names this panel works AROUND — what the "does not flip the knob" test reads.</summary>
    public IReadOnlyList<string> OwningOptions => Rows.Select(r => r.OwningOption).ToArray();

    private void SetHighlight(int index)
    {
        for (var i = 0; i < Rows.Count; i++) Rows[i].IsHighlighted = i == index;
        _highlight = index;
        OnPropertyChanged(nameof(Highlighted));
    }
}
