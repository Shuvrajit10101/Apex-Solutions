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
/// <para><b>Fidelity (Ruling 14 tier 1).</b> help.tallysolutions.com/tally-prime/keyboard-shortcuts-tally/
/// lists <c>Ctrl+I</c> in the <i>Right button area</i> as <i>"To add more details to a master or voucher for
/// the current instance"</i>; and, on the contra/banking pages, verbatim: <i>"press <b>Ctrl+I</b> (More
/// Details) to enter any of the values <b>without activating the options in F12 (Configure)</b>."</i></para>
///
/// <para>🔴 <b>THE DEFINING BEHAVIOUR IS THE HALF IN BOLD, and it is what this panel is built around.</b>
/// More Details is not a shortcut to the options screen: it reaches an option-gated field for THIS voucher and
/// leaves the option exactly as it found it. Every row here therefore writes a per-instance override flag
/// (<see cref="VoucherEntryViewModel.MoreDetailsBillWiseRequested"/> /
/// <see cref="VoucherEntryViewModel.MoreDetailsBatchRequested"/>) and NEVER the knob beside it. A test asserts
/// the knob is unchanged before and after, and that test is the one that must never be deleted.</para>
///
/// <para>🔴 <b>AND THE LIMIT OF THAT CLAIM, MEASURED — because "per instance" promises more than this build
/// can currently deliver.</b> The vendor's phrase implies the next voucher is unaffected. That holds here, but
/// only because <c>MainWindowViewModel.OpenVoucher</c> builds a FRESH <see cref="VoucherEntryViewModel"/> each
/// time, so the SCREEN OPTION is itself per-voucher and re-defaults too — there is no persistent option store
/// for either value to outlive. What the separate flag actually buys, today, is that the operator's setting is
/// not rewritten underneath them: the checkbox they are looking at still reads as they left it, and every code
/// path that branches on the knob still sees it. That is the property the test locks, and it is the one a
/// later slice introducing persistent screen options must preserve.</para>
///
/// <para>This application has no single global "F12 &gt; Voucher Entry" page — <see cref="VoucherEntryViewModel"/>
/// records that the reference product abolished it and that "configuration belongs to the screen you are
/// standing on" — so the options More Details works around are this screen's own knobs. That is a difference
/// in where the option lives, not in what More Details does.</para>
///
/// <para>🔴 <b>WHAT THIS PANEL CANNOT OFFER, MEASURED AGAINST THE VENDOR'S OWN EXAMPLES.</b> The disclosure is
/// a first-class part of the row, because a panel that silently omits three of the vendor's four documented
/// field groups would read as complete. Each omission is a MISSING STORE, not a missing screen — see
/// <see cref="WithheldFields"/>, which <see cref="Footnote"/> and the test that locks it both read:</para>
/// <list type="bullet">
///   <item><b>Ledger Narration (per line)</b> — the vendor's headline More Details example on an invoice line.
///     <c>Apex.Ledger.Domain.Voucher</c> carries <c>Narration</c> at the VOUCHER level and
///     <c>Apex.Ledger.Domain.EntryLine</c> has no narration field at all, so a per-line narration has nowhere
///     to be stored. Adding it is a schema change, and this track takes none.</item>
///   <item><b>Voucher No. Details &gt; Select Unused Voucher Nos.</b> — nothing in this build tracks unused or
///     skipped voucher numbers; <c>NumberingMethod</c> and the numbering services model the NEXT number, never
///     the gaps behind it. The list would have no source to draw from.</item>
///   <item><b>PAN / CIN (company)</b> — <c>Apex.Ledger.Domain.Company</c> has neither field. (The <c>Pan</c>
///     on <c>Employee</c> and the <c>Cin</c> on <c>GstChallan</c> are different identifiers on different
///     aggregates and are NOT the company's.)</item>
/// </list>
///
/// <para>🔴 <b>AND ONE VENDOR FIELD IS DELIBERATELY ABSENT FOR THE OPPOSITE REASON.</b> "Provide Reference No.
/// and Date" is NOT withheld — <c>Voucher.ReferenceNo</c> / <c>Voucher.ReferenceDate</c> both exist and are
/// captured. It is simply not a More Details row here, because on this screen the field is not option-gated at
/// all: <c>VoucherEntryViewModel.ShowReferenceCapture</c> is driven purely by the voucher's base type, so on
/// every Purchase/Sales the field is ALREADY on the voucher. There is nothing to reveal, and offering a row
/// that re-reveals a visible field would be theatre.</para>
/// </summary>
public sealed partial class MoreDetailsViewModel : ViewModelBase
{
    /// <summary>
    /// The vendor-attested field groups this panel does NOT reach, each with the reason. Held as data so the
    /// footnote and the test that locks the footnote read the SAME list rather than two copies of one sentence
    /// that can drift apart.
    /// </summary>
    public static IReadOnlyList<string> WithheldFields { get; } = new[]
    {
        "Ledger Narration (per line) — this book stores narration on the voucher, not on the line",
        "Voucher No. Details / Select Unused Voucher Nos. — unused numbers are not tracked",
        "PAN / CIN — not stored on the company",
    };

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
        // Same shape, opposite default: UseBatchWiseDetails ships ON, so this row appears only once the
        // operator has turned it off on this screen.
        if (entry.CanUseBatchWiseDetails && !entry.UseBatchWiseDetails)
        {
            Rows.Add(new MoreDetailsRowViewModel(
                "Batch / Lot Details",
                "Use batch-wise details for item allocation",
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
    /// that reads like a failure. This is also why the panel OPENS on a voucher that has nothing to reveal
    /// (a Journal, say) instead of the chord silently doing nothing: a keystroke that answers is honest, a
    /// keystroke that is swallowed is the exact defect census 14.4 was graded unreachable for.
    /// </summary>
    public string Status => Rows.Count == 0
        ? "Every optional field that applies to this voucher is already on the screen."
        : "Enter — show the field on this voucher. The screen option is not changed.";

    /// <summary>
    /// 🔴 THE HONEST FOOTER. It names the vendor-attested field groups this build cannot reach, and why, so a
    /// later slice cannot quietly drop the disclosure and claim census row 14.4 complete. A test locks it
    /// against <see cref="WithheldFields"/>.
    /// </summary>
    public string Footnote =>
        "Not available in this build: " + string.Join("; ", WithheldFields) + ".";

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
