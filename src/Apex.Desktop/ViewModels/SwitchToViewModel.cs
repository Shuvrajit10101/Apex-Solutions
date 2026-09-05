using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Apex.Desktop.ViewModels;

/// <summary>One row in the Switch To list: a destination plus the highlight flag the row template draws.</summary>
public sealed partial class SwitchToRowViewModel : ViewModelBase
{
    public SwitchToRowViewModel(ShellDestination destination) => Destination = destination;

    public ShellDestination Destination { get; }

    /// <summary>The section (and any submenus) above this page — "Reports › GST Reports".</summary>
    public string Section => Destination.Section;

    /// <summary>The page row's own label.</summary>
    public string Label => Destination.Label;

    /// <summary>How the row reads as one string — "Reports › Balance Sheet".</summary>
    public string Display => Destination.Display;

    /// <summary>True while the keyboard cursor is standing on this row. The row template DRAWS this.</summary>
    [ObservableProperty] private bool _isHighlighted;
}

/// <summary>
/// 14.2 — <b>SWITCH TO (Ctrl+G)</b>.
///
/// <para><b>Fidelity (Ruling 14 tier 1).</b> help.tallysolutions.com/tally-prime/keyboard-shortcuts-tally/,
/// verbatim: <c>Ctrl+G</c> — <i>"To switch to a different report, and create masters and vouchers in the flow
/// of work."</i> The sibling chord <c>Alt+G</c> (Go To) reads <i>"To primarily open a report, and create
/// masters and vouchers in the flow of work"</i>, and the ONE documented difference between them is that
/// <b>Switch To does not return you to where you were</b>. This screen therefore REPLACES the cascade.</para>
///
/// <para>🔴 <b>It is not a multi-company feature</b>, whatever the row title suggests. Company switching is
/// the vendor's <c>F3</c> / <c>Alt+F3</c> / <c>Ctrl+F3</c> family and lives on the company menu (14.9).</para>
///
/// <para><b>PREFIX filtering, with the typed text visible</b> — the settled keyboard contract. Typing filters
/// the list on a case-insensitive <c>StartsWith</c> over the page label AND over the section breadcrumb (so
/// "gst" reaches both "GST Reports › …" and a page whose own name starts with GST), and the prefix is shown in
/// the panel header so the operator can see why the list shrank. Backspace removes one character; Escape (the
/// shell's own Back) leaves. This is deliberately NOT type-to-JUMP: jumping moves a cursor and leaves the
/// operator to work out which of ~120 rows they landed on.</para>
/// </summary>
public sealed partial class SwitchToViewModel : ViewModelBase
{
    private readonly IReadOnlyList<ShellDestination> _all;
    private readonly Action<ShellDestination> _open;

    /// <param name="destinations">Every destination the cascade can reach — derived, see
    /// <see cref="ShellDestinations"/>.</param>
    /// <param name="open">What the shell does when a destination is taken.</param>
    /// <param name="keepReturnPath">
    /// 🔴 The ONE axis on which Switch To and Go To differ, kept as a flag rather than a second class so the
    /// two can never drift apart. <c>false</c> (Switch To, the vendor's Ctrl+G) replaces the cascade and
    /// leaves no way back to the pre-jump page; <c>true</c> would keep it. Only <c>false</c> is constructed
    /// today — <b>Go To (Alt+G) is not claimed as built by this track</b> and nothing here should be read as
    /// claiming it.</param>
    public SwitchToViewModel(
        IReadOnlyList<ShellDestination> destinations, Action<ShellDestination> open,
        bool keepReturnPath = false)
    {
        _all = destinations ?? Array.Empty<ShellDestination>();
        _open = open ?? throw new ArgumentNullException(nameof(open));
        KeepReturnPath = keepReturnPath;
        Rebuild();
    }

    /// <summary>See the <c>keepReturnPath</c> constructor parameter.</summary>
    public bool KeepReturnPath { get; }

    /// <summary>The panel's column header.</summary>
    public string Title => "Switch To";

    /// <summary>The rows currently shown — the whole registry, or what the typed prefix leaves of it.</summary>
    public ObservableCollection<SwitchToRowViewModel> Rows { get; } = new();

    /// <summary>The prefix typed so far. Empty ⇒ no filter.</summary>
    [ObservableProperty] private string _prefix = string.Empty;

    /// <summary>
    /// The prefix as the header shows it. 🔴 The typed text MUST be visible: a list that silently shrinks
    /// under an invisible filter is the defect this panel exists to avoid, not a feature.
    /// </summary>
    public string PrefixDisplay => Prefix.Length == 0
        ? "Type to filter · ↑↓ to move · Enter to switch"
        : $"Filter: {Prefix}";

    /// <summary>The status line under the list (a count, or the no-match explanation).</summary>
    [ObservableProperty] private string _status = string.Empty;

    private int _highlight = -1;

    /// <summary>The highlighted row, or null when the filter matched nothing.</summary>
    public SwitchToRowViewModel? Highlighted =>
        _highlight >= 0 && _highlight < Rows.Count ? Rows[_highlight] : null;

    /// <summary>Appends one typed character to the prefix and re-filters.</summary>
    public void TypePrefix(char c)
    {
        Prefix += c;
        Rebuild();
    }

    /// <summary>Removes the last character of the prefix (Backspace). A no-op on an empty prefix.</summary>
    public void BackspacePrefix()
    {
        if (Prefix.Length == 0) return;
        Prefix = Prefix[..^1];
        Rebuild();
    }

    /// <summary>Moves the highlight one row down (wrapping is deliberately NOT done — the list is long).</summary>
    public void MoveDown()
    {
        if (Rows.Count == 0) return;
        SetHighlight(Math.Min(_highlight + 1, Rows.Count - 1));
    }

    /// <summary>Moves the highlight one row up.</summary>
    public void MoveUp()
    {
        if (Rows.Count == 0) return;
        SetHighlight(Math.Max(_highlight - 1, 0));
    }

    /// <summary>Takes the highlighted destination. A no-op when the filter matched nothing.</summary>
    public bool Open()
    {
        if (Highlighted is not { } row) return false;
        _open(row.Destination);
        return true;
    }

    private void SetHighlight(int index)
    {
        for (var i = 0; i < Rows.Count; i++) Rows[i].IsHighlighted = i == index;
        _highlight = index;
        OnPropertyChanged(nameof(Highlighted));
    }

    private void Rebuild()
    {
        var matches = _all.Where(Matches).ToList();

        Rows.Clear();
        foreach (var d in matches) Rows.Add(new SwitchToRowViewModel(d));

        SetHighlight(Rows.Count == 0 ? -1 : 0);
        OnPropertyChanged(nameof(PrefixDisplay));

        Status = Rows.Count switch
        {
            0 => $"No destination starts with \"{Prefix}\".",
            1 => "1 destination.",
            _ => $"{Rows.Count} destinations.",
        };
    }

    /// <summary>
    /// PREFIX match, case-insensitive, over the page label and over each segment of the section breadcrumb.
    /// Matching the breadcrumb segments (rather than the whole "Reports › GST Reports" string) is what lets
    /// "gst" find the GST family without the operator first typing "reports ›".
    /// </summary>
    private bool Matches(ShellDestination d)
    {
        if (Prefix.Length == 0) return true;
        if (d.Label.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase)) return true;
        foreach (var segment in d.Section.Split('›', StringSplitOptions.RemoveEmptyEntries))
            if (segment.Trim().StartsWith(Prefix, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }
}
