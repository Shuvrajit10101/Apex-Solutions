using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Apex.Desktop.ViewModels;

/// <summary>
/// W2-14 — one destination in the Go To index: a Page row somewhere in the Gateway cascade, remembered by the
/// PATH of Group labels that reaches it rather than by its label alone.
///
/// <para><b>Why the path and not the label.</b> Two labels are ambiguous in this menu — "Batch" is the batch
/// MASTER under Create and the batch REPORT family under Inventory Reports; "Ledger" is the ledger master
/// almost everywhere and the all-ledgers book picker under Account Books. A label-keyed index would have to
/// pick one meaning and would silently open the wrong screen for the other. The path disambiguates by
/// construction, and it is also exactly what the jump REPLAYS.</para>
/// </summary>
public sealed partial class GoToDestination : ViewModelBase
{
    public GoToDestination(string label, string section, IReadOnlyList<string> path, bool opensSubmenu)
    {
        Label = label;
        Section = section;
        Path = path;
        OpensSubmenu = opensSubmenu;
    }

    /// <summary>The menu row's own label — what the operator types and what Enter opens.</summary>
    public string Label { get; }

    /// <summary>The parent breadcrumb, e.g. "Reports → Statements of Accounts → Outstandings".</summary>
    public string Section { get; }

    /// <summary>The Group labels, root-first, that must be drilled to reach this row's column.</summary>
    public IReadOnlyList<string> Path { get; }

    /// <summary>
    /// True when this destination is a menu GROUP (a hub such as "Cash Book" or "Statutory Reports") rather
    /// than a page. Jumping to one lands on its submenu column with the highlight on its first row.
    /// <para>Hubs are indexed deliberately: several report families — the Account-Books pickers among them —
    /// are groups, not pages, so an index of pages alone could not reach the Cash Book at all.</para>
    /// </summary>
    public bool OpensSubmenu { get; }

    /// <summary>
    /// True while this row is the highlighted result — the row background binds straight to it.
    ///
    /// <para>🔴 It is an <c>[ObservableProperty]</c> on a <see cref="ViewModelBase"/> for the same reason
    /// <c>MenuItemViewModel.IsSelected</c> is: the results list is an <c>ItemsControl</c> whose collection
    /// instance never changes as the highlight moves, so the ONLY thing that can repaint a row is that row
    /// raising <c>PropertyChanged</c> for itself. As a plain auto-property this compiled, bound, and left the
    /// painted highlight stuck on row one while Up/Down moved an invisible selection and Enter opened a screen
    /// the operator never saw named. Locked by
    /// <c>GoToChordReachabilityTests.The_painted_highlight_follows_the_arrow_keys_and_marks_exactly_one_row</c>,
    /// which reads the background out of the realised visual tree rather than out of this flag.</para>
    /// </summary>
    [ObservableProperty] private bool _isSelected;
}

/// <summary>
/// W2-14 (census 14.1) — the <b>Go To (Alt+G)</b> jump-anywhere overlay.
///
/// <para><b>Vendor grounding (help.tallysolutions.com, fetched 2026-09-05).</b> The shortcut table reads
/// <b>Alt+G</b> — <i>"To primarily open a report, and create masters and vouchers in the flow of work."</i>
/// The feature description adds that it <i>"lists all the reports by default in different groups under a
/// common selection table"</i> and that you <i>"simply type a report name and search the report, without
/// having to move out of the screen you have already opened"</i>. Master creation is reachable through it too
/// (<i>"Alt+G (Go To) &gt; Create Master &gt; Voucher Type"</i>).</para>
///
/// <para><b>Ctrl+G ("Switch To") is deliberately NOT built here.</b> That is census row 14.2 and it sits
/// behind the open multi-company-shell ruling; this slice takes Alt+G only.</para>
///
/// <para><b>Ranking is ours, labelled (RULING 9).</b> The source says "type a report name and search" but does
/// not state the match rule. We rank a label PREFIX match first, then a label substring, then a section match,
/// and keep menu order within each band — so typing "balance" offers Balance Sheet before Trial Balance. A
/// pure prefix rule would have made "Trial Balance" unfindable by its distinctive word; a pure substring rule
/// would have buried the exact hit.</para>
///
/// <para>Holds no Avalonia types: the whole overlay is unit-testable headlessly.</para>
/// </summary>
public sealed partial class GoToViewModel : ViewModelBase
{
    private readonly IReadOnlyList<GoToDestination> _index;

    /// <summary>Every destination the walk found, in menu order — the unfiltered index.</summary>
    public IReadOnlyList<GoToDestination> AllDestinations => _index;

    /// <summary>The destinations matching <see cref="SearchText"/>, best match first.</summary>
    public ObservableCollection<GoToDestination> Results { get; } = new();

    [ObservableProperty] private string _searchText = string.Empty;

    private int _selectedIndex = -1;

    /// <summary>The highlighted result's index, or −1 when the list is empty.</summary>
    public int SelectedIndex
    {
        get => _selectedIndex;
        set
        {
            var clamped = Results.Count == 0 ? -1 : Math.Clamp(value, 0, Results.Count - 1);
            if (clamped == _selectedIndex) { Repaint(); return; }
            _selectedIndex = clamped;
            OnPropertyChanged();
            OnPropertyChanged(nameof(Selected));
            Repaint();
        }
    }

    /// <summary>The highlighted destination, or null when nothing matches.</summary>
    public GoToDestination? Selected =>
        _selectedIndex >= 0 && _selectedIndex < Results.Count ? Results[_selectedIndex] : null;

    /// <summary>True when the current search matched nothing (the view shows a "no match" line).</summary>
    public bool HasNoMatch => Results.Count == 0;

    public GoToViewModel(IReadOnlyList<GoToDestination> index)
    {
        _index = index ?? throw new ArgumentNullException(nameof(index));
        ApplyFilter();
    }

    partial void OnSearchTextChanged(string value) => ApplyFilter();

    /// <summary>
    /// Rebuilds <see cref="Results"/> for the current search and puts the highlight back on the FIRST row.
    /// Resetting the highlight is load-bearing: without it, retyping while row 7 was highlighted would leave
    /// Enter aimed at whatever landed at index 7 in the NEW list — a jump to a screen the operator never read.
    /// </summary>
    private void ApplyFilter()
    {
        var text = (SearchText ?? string.Empty).Trim();

        // Rows are the SAME destination objects across searches, so a row that was highlighted and is then
        // filtered out keeps its flag and comes back amber the next time it matches — two painted highlights,
        // one of them un-drivable. Clear the outgoing list before rebuilding.
        foreach (var d in Results) d.IsSelected = false;

        Results.Clear();
        foreach (var d in Rank(text)) Results.Add(d);

        _selectedIndex = Results.Count == 0 ? -1 : 0;
        OnPropertyChanged(nameof(SelectedIndex));
        OnPropertyChanged(nameof(Selected));
        OnPropertyChanged(nameof(HasNoMatch));
        Repaint();
    }

    private IEnumerable<GoToDestination> Rank(string text)
    {
        if (text.Length == 0) return _index;

        // Three bands, menu order preserved inside each. Band 0 is the exact-prefix hit the operator meant.
        var prefix = new List<GoToDestination>();
        var contains = new List<GoToDestination>();
        var section = new List<GoToDestination>();

        foreach (var d in _index)
        {
            if (d.Label.StartsWith(text, StringComparison.OrdinalIgnoreCase)) prefix.Add(d);
            else if (d.Label.Contains(text, StringComparison.OrdinalIgnoreCase)) contains.Add(d);
            else if (d.Section.Contains(text, StringComparison.OrdinalIgnoreCase)) section.Add(d);
        }

        return prefix.Concat(contains).Concat(section);
    }

    /// <summary>
    /// Marks exactly the highlighted row and clears every other. Each row raises its own
    /// <c>PropertyChanged</c>, which is what actually repaints it; re-announcing <see cref="Results"/> here
    /// would only re-hand the ItemsControl the very same collection instance and cannot move a highlight.
    /// </summary>
    private void Repaint()
    {
        for (var i = 0; i < Results.Count; i++) Results[i].IsSelected = i == _selectedIndex;
    }

    /// <summary>Down: moves the highlight one row, wrapping. A no-op on an empty list.</summary>
    public void MoveDown()
    {
        if (Results.Count == 0) return;
        SelectedIndex = (_selectedIndex + 1) % Results.Count;
    }

    /// <summary>Up: moves the highlight one row back, wrapping. A no-op on an empty list.</summary>
    public void MoveUp()
    {
        if (Results.Count == 0) return;
        SelectedIndex = (_selectedIndex - 1 + Results.Count) % Results.Count;
    }
}
