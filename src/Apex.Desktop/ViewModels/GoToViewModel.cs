using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Apex.Desktop.ViewModels;

/// <summary>
/// One destination on the Go To index (W2-14, census row 14.1): the parent <see cref="Section"/> it is nested
/// under, the <see cref="Label"/> the user types towards, an optional <see cref="Hint"/> (the chord or the
/// cascade path it also lives on), and the <see cref="Open"/> action that actually travels there.
///
/// <para><b>The action is the same method the Gateway cascade calls</b> — <c>OpenReport(...)</c>,
/// <c>ShowLedgerMaster()</c>, <c>OpenVoucher(...)</c>. Go To is an INDEX over the existing dispatch, never a
/// second implementation of it, so a destination cannot drift away from the menu row that reaches the same
/// screen.</para>
/// </summary>
public sealed class GoToDestination
{
    /// <summary>The parent section this destination is nested under (the Gateway root's own headers:
    /// Masters / Statutory / Transactions / Reports / Data). Never blank — the standing UI contract is that
    /// every item hangs off a parent section rather than sitting in a flat dump.</summary>
    public string Section { get; }

    /// <summary>The destination's name, as it reads on the Gateway cascade. This is what type-to-filter matches.</summary>
    public string Label { get; }

    /// <summary>A short right-aligned hint (a function key, a chord, or the cascade path). May be empty.</summary>
    public string Hint { get; }

    /// <summary>Travels to the destination. Runs the shell's own opener — see the type remarks.</summary>
    public Action Open { get; }

    public GoToDestination(string section, string label, Action open, string hint = "")
    {
        Section = string.IsNullOrWhiteSpace(section)
            ? throw new ArgumentException("a Go To destination must be nested under a section", nameof(section))
            : section;
        Label = string.IsNullOrWhiteSpace(label)
            ? throw new ArgumentException("a Go To destination must have a label", nameof(label))
            : label;
        Open = open ?? throw new ArgumentNullException(nameof(open));
        Hint = hint ?? string.Empty;
    }

    /// <summary>The section + label as one line, for the list row's secondary text.</summary>
    public string Path => $"{Section} › {Label}";
}

/// <summary>
/// <b>Go To (Alt+G) — the jump-anywhere index (W2-14, census row 14.1).</b> Hosted as its own cascading
/// Miller-column page column over whatever surface was open, keyboard-first: type to narrow, arrow to choose,
/// Enter to travel, Esc to abandon.
///
/// <para><b>Fidelity — RULING 14, help.tallysolutions.com.</b> The vendor's keyboard-shortcut page defines
/// <b>Alt+G</b> as <i>"To primarily open a report, and create masters and vouchers in the flow of work"</i>, so
/// the index carries all three verbs (reports, masters, vouchers) and is reachable from any screen, not only
/// the Gateway. <b>Ctrl+G</b> is a DIFFERENT verb on that same page — <i>"To switch to a different report"</i>
/// (census row 14.2, Switch To) — and is deliberately NOT built here.</para>
///
/// <para><b>🔴 DOCUMENTED DIVERGENCE, LABELLED AS OURS (ruling 9).</b> The vendor documentation states what
/// Alt+G is FOR; it does not state the matching rule its search box uses. This index filters by <b>word
/// prefix</b> — a destination matches when the whole label, or any word inside it, starts with the typed text
/// — because that is this product's settled keyboard contract for every other type-to-filter surface, and one
/// product with two search behaviours is worse than a divergence from the reference. No admissible source
/// speaks, so the choice is recorded as ours rather than asserted as fidelity.</para>
/// </summary>
public sealed partial class GoToViewModel : ViewModelBase
{
    private readonly List<GoToDestination> _all;

    /// <summary>The column title / heading for the panel.</summary>
    public string Title => "Go To";

    /// <summary>Every destination in the index, in section order. The unfiltered set.</summary>
    public IReadOnlyList<GoToDestination> All => _all;

    /// <summary>The destinations matching <see cref="Query"/> — the whole index when the query is blank.</summary>
    public ObservableCollection<GoToDestination> Results { get; } = new();

    /// <summary>The typed text. Kept VISIBLE in the panel (the keyboard contract: type-to-filter shows what was
    /// typed), and re-runs the filter on every change.</summary>
    [ObservableProperty] private string _query = string.Empty;

    /// <summary>The highlighted result (two-way bound to the list's SelectedItem). Enter / the Go button act on it.</summary>
    [ObservableProperty] private GoToDestination? _selected;

    /// <summary>A short status / empty-state line.</summary>
    [ObservableProperty] private string _status = string.Empty;

    /// <summary>Raised when the user chooses a destination. The shell closes the panel and runs the action.</summary>
    public event Action<GoToDestination>? GoRequested;

    public GoToViewModel(IEnumerable<GoToDestination> destinations)
    {
        _all = (destinations ?? throw new ArgumentNullException(nameof(destinations))).ToList();
        ApplyFilter();
    }

    partial void OnQueryChanged(string value) => ApplyFilter();

    /// <summary>
    /// Re-runs the word-prefix filter into <see cref="Results"/> and keeps the first row highlighted so Enter
    /// never fires into nothing. A blank query restores the whole index.
    /// </summary>
    private void ApplyFilter()
    {
        var q = (Query ?? string.Empty).Trim();

        Results.Clear();
        foreach (var d in _all)
            if (Matches(d, q))
                Results.Add(d);

        Selected = Results.Count > 0 ? Results[0] : null;
        Status = Results.Count > 0
            ? string.Empty
            : $"Nothing matches “{q}”. Press Esc to go back.";
    }

    /// <summary>
    /// The match rule (see the type remarks — this is OURS, not the vendor's): case-insensitive, and true when
    /// the label, or any word within it, STARTS WITH the query. A multi-word query is matched word by word
    /// against the start of the label, so "trial bal" reaches "Trial Balance".
    /// </summary>
    internal static bool Matches(GoToDestination destination, string query)
    {
        if (string.IsNullOrEmpty(query)) return true;

        var words = destination.Label.Split(WordBreaks, StringSplitOptions.RemoveEmptyEntries);
        var terms = query.Split(WordBreaks, StringSplitOptions.RemoveEmptyEntries);
        if (terms.Length == 0) return true;

        // Every typed term must prefix-match SOME word of the label (or the label itself), so extra terms
        // narrow rather than widen — "trial bal" is more specific than "trial", never less.
        foreach (var term in terms)
        {
            var hit = destination.Label.StartsWith(term, StringComparison.OrdinalIgnoreCase)
                      || words.Any(w => w.StartsWith(term, StringComparison.OrdinalIgnoreCase));
            if (!hit) return false;
        }

        return true;
    }

    private static readonly char[] WordBreaks = { ' ', '\t', '/', '&', '(', ')', '—', '-', ',', '.' };

    /// <summary>Travels to the highlighted destination (raises <see cref="GoRequested"/>). A no-op with no
    /// selection, so Enter on an empty result list does nothing rather than throwing.</summary>
    public void Go()
    {
        if (Selected is not { } destination) return;
        GoRequested?.Invoke(destination);
    }
}
