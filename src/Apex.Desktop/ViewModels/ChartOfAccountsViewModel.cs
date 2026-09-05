using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Services;
using Apex.Desktop.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using DomainLedger = Apex.Ledger.Domain.Ledger;

namespace Apex.Desktop.ViewModels;

/// <summary>The kind of node a <see cref="ChartRow"/> represents (drives indent + styling).</summary>
public enum ChartNodeKind
{
    /// <summary>One of the 15 primary heads (no parent).</summary>
    Primary,
    /// <summary>A sub-group nested under a primary (or deeper).</summary>
    SubGroup,
    /// <summary>A ledger sitting directly under its group.</summary>
    Ledger,
}

/// <summary>
/// One presentation row in the Chart-of-Accounts tree: a name, its <see cref="ChartNodeKind"/>,
/// an indent <see cref="Depth"/> (0 = primary head), and an optional right-aligned detail
/// (nature for groups, opening balance for ledgers). Purely display data — no engine types leak.
/// </summary>
public sealed partial class ChartRow : ObservableObject
{
    public string Name { get; init; } = string.Empty;
    public ChartNodeKind Kind { get; init; }
    public int Depth { get; init; }
    public string Detail { get; init; } = string.Empty;

    /// <summary>
    /// The <b>stable identity</b> of the master this row represents — the ledger's Guid on a ledger row, the
    /// group's Guid on a group row (WI-3). Before this the tree was ID-less, so a row could not be resolved back to
    /// the master it displayed and Enter had nothing to open. Additive: the export projector and its tests read
    /// only Name/Kind/Depth/Detail and are unaffected.
    /// </summary>
    public Guid? LedgerId { get; init; }

    /// <summary>The group's stable id on a group row; <c>null</c> on a ledger row. See <see cref="LedgerId"/>.</summary>
    public Guid? GroupId { get; init; }

    /// <summary>True while this row carries the keyboard highlight (arrow keys move it, Enter opens it for Alter).</summary>
    [ObservableProperty] private bool _isHighlighted;

    public bool IsGroup => Kind != ChartNodeKind.Ledger;
    public bool IsPrimary => Kind == ChartNodeKind.Primary;

    /// <summary>True iff Enter on this row can open a master for alteration — every row does, since a ledger row
    /// carries a <see cref="LedgerId"/> and a group row a <see cref="GroupId"/>.</summary>
    public bool IsAlterable => LedgerId is not null || GroupId is not null;

    /// <summary>Left indent in device pixels — 18 px per level, deepened for ledgers.</summary>
    public double Indent => Depth * 18.0;
}

/// <summary>
/// A read-only Chart-of-Accounts tree for the current company. Renders the 15 primary heads with
/// their sub-groups nested/indented under their parent (via <see cref="Group.ParentId"/>) and every
/// ledger indented under its group. The reserved Profit &amp; Loss head (and its ledger) appear last.
/// Kept UI-toolkit-free so it can be asserted headlessly: a test walks <see cref="Rows"/> and checks
/// each sub-group's row sits deeper than — and after — its primary parent's row.
/// </summary>
public sealed partial class ChartOfAccountsViewModel : ViewModelBase, IMasterListExportSource
{
    private readonly Company _company;

    [ObservableProperty] private string _title = "Chart of Accounts";
    [ObservableProperty] private string _subtitle = string.Empty;

    public ObservableCollection<ChartRow> Rows { get; } = new();

    /// <inheritdoc/>
    /// <remarks>Chart of Accounts normally exports through its bespoke tree projector
    /// (<see cref="Services.MasterListTabularProjector.ProjectChartOfAccounts"/>); this snapshot exists so the
    /// shell can DETECT the screen as a master-list export source uniformly. It carries the same columns —
    /// indented Name, Type, Nature, Opening (numeric), Dr/Cr — reading only the already-built tree rows.</remarks>
    public MasterListSnapshot ToMasterListSnapshot() => new(
        Title,
        new[]
        {
            MasterListColumn.Text("Name"), MasterListColumn.Text("Type"), MasterListColumn.Text("Nature"),
            MasterListColumn.Number("Opening"), MasterListColumn.Text("Dr/Cr"),
        },
        Rows.Select(r =>
        {
            string indent = r.Depth <= 0 ? string.Empty : new string(' ', r.Depth * 2);
            string type = r.Kind switch
            {
                ChartNodeKind.Primary => "Primary",
                ChartNodeKind.SubGroup => "Sub-Group",
                _ => "Ledger",
            };
            // A group row carries its nature (Detail) and no opening; a ledger row carries "<amount> Dr/Cr".
            string nature = r.IsGroup ? r.Detail : string.Empty;
            string opening = r.IsGroup ? string.Empty : r.Detail;     // "1,05,000.00 Dr" (side stripped by the projector)
            string side = opening.EndsWith(" Dr", StringComparison.OrdinalIgnoreCase) ? "Dr"
                        : opening.EndsWith(" Cr", StringComparison.OrdinalIgnoreCase) ? "Cr" : string.Empty;
            return (IReadOnlyList<string>)new[] { indent + r.Name, type, nature, opening, side };
        }).ToList());

    public ChartOfAccountsViewModel(Company company)
    {
        _company = company ?? throw new ArgumentNullException(nameof(company));
        Subtitle = $"{company.Name}  —  Groups & Ledgers";
        Build();
    }

    // ------------------------------------------------- census 2.13: the derived "Unused" view (Ctrl+J)

    /// <summary>
    /// <b>Census 2.13 — the Ctrl+J "Show Unused" filter.</b> While true the tree shows only ledgers that
    /// <see cref="UnusedMasters.IsLedgerUnused"/> reports unused, with every group that has nothing unused beneath
    /// it pruned away, and the heading becomes the vendor's own caption
    /// <see cref="UnusedMasters.UnusedLedgersCaption"/>.
    ///
    /// <para>Nothing is stored. The answer is derived from the vouchers on every rebuild, so posting against a
    /// listed ledger and pressing Ctrl+J again drops it out of the list with no master edit anywhere.</para>
    ///
    /// <para><b>Groups are pruned, not flattened</b> (OURS, ruling 9). The vendor's caption says "List of
    /// Ledgers", but this screen IS a tree and the operator needs to know which head an unused ledger sits under
    /// before deciding anything about it. So the surviving ledgers keep their parents; a head with nothing unused
    /// under it disappears entirely rather than standing empty.</para>
    /// </summary>
    [ObservableProperty] private bool _showUnusedOnly;

    partial void OnShowUnusedOnlyChanged(bool value)
    {
        Title = value ? UnusedMasters.UnusedLedgersCaption : "Chart of Accounts";
        Subtitle = value
            ? $"{_company.Name}  —  ledgers with no recorded transactions"
            : $"{_company.Name}  —  Groups & Ledgers";
        Refresh();
        OnPropertyChanged(nameof(ShowsEmptyUnusedNotice));
    }

    /// <summary>True when the filter is on but nothing survived it — the pane must SAY so rather than render an
    /// empty box the operator reads as a broken screen.</summary>
    public bool ShowsEmptyUnusedNotice => ShowUnusedOnly && Rows.Count == 0;

    /// <summary>How many of the company's ledgers are unused right now, independent of whether the filter is on.
    /// Derived on every read; the Ctrl+J panel quotes it in its status line so the operator can see the answer
    /// before committing to the filtered view.</summary>
    public int UnusedLedgerCount => _company.Ledgers.Count(l => UnusedMasters.IsLedgerUnused(_company, l));

    // --------------------------------------------------------------- keyboard selection + drill (WI-3)

    /// <summary>The index of the highlighted row, or -1 when nothing is highlighted.</summary>
    [ObservableProperty] private int _highlightedIndex = -1;

    /// <summary>The highlighted row, or <c>null</c>. Enter on it opens that master for alteration.</summary>
    public ChartRow? HighlightedRow =>
        HighlightedIndex >= 0 && HighlightedIndex < Rows.Count ? Rows[HighlightedIndex] : null;

    partial void OnHighlightedIndexChanged(int value)
    {
        for (var i = 0; i < Rows.Count; i++)
            Rows[i].IsHighlighted = i == value;
        OnPropertyChanged(nameof(HighlightedRow));
    }

    /// <summary>
    /// Moves the highlight by <paramref name="direction"/>, wrapping. Every row is a drillable master (group or
    /// ledger), so nothing is skipped — the CA asked to reach ledgers <i>and</i> groups from here (point 10:
    /// "alterations in all items, ledgers, and groups").
    /// </summary>
    public void MoveHighlight(int direction)
    {
        if (Rows.Count == 0) { HighlightedIndex = -1; return; }
        var i = HighlightedIndex < 0 ? (direction > 0 ? -1 : 0) : HighlightedIndex;
        HighlightedIndex = (i + direction + Rows.Count) % Rows.Count;
    }

    /// <summary>
    /// Rebuilds the tree from the (possibly altered) company, <b>preserving the highlight on the same master</b>
    /// by id rather than by position — a rename re-sorts the tree, so an index-based restore would silently land
    /// the highlight on a different account. Called after an alteration saves, so the user sees the new name
    /// immediately instead of a stale snapshot that looks like the save failed.
    /// </summary>
    public void Refresh()
    {
        var keepLedger = HighlightedRow?.LedgerId;
        var keepGroup = HighlightedRow?.GroupId;

        Build();

        var index = -1;
        if (keepLedger is { } lid)
            index = IndexOfRow(r => r.LedgerId == lid);
        else if (keepGroup is { } gid)
            index = IndexOfRow(r => r.GroupId == gid);

        HighlightedIndex = index >= 0 ? index : (Rows.Count > 0 ? 0 : -1);
        // Build() replaced every row object, so re-stamp the highlight flag onto the new instances.
        OnHighlightedIndexChanged(HighlightedIndex);
    }

    private int IndexOfRow(Func<ChartRow, bool> match)
    {
        for (var i = 0; i < Rows.Count; i++)
            if (match(Rows[i])) return i;
        return -1;
    }

    private void Build()
    {
        Rows.Clear();

        // Index children (sub-groups) and ledgers by parent for a stable, name-sorted walk.
        var childGroups = _company.Groups
            .Where(g => g.ParentId is not null)
            .GroupBy(g => g.ParentId!.Value)
            .ToDictionary(grp => grp.Key, grp => grp.OrderBy(g => g.Name, StringComparer.OrdinalIgnoreCase).ToList());

        var ledgersByGroup = _company.Ledgers
            .GroupBy(l => l.GroupId)
            .ToDictionary(grp => grp.Key, grp => grp.OrderBy(l => l.Name, StringComparer.OrdinalIgnoreCase).ToList());

        // Roots = the 15 primary heads (no parent), name-sorted.
        var primaries = _company.Groups
            .Where(g => g.ParentId is null)
            .OrderBy(g => g.Name, StringComparer.OrdinalIgnoreCase);

        foreach (var primary in primaries)
            foreach (var row in BuildGroup(primary, depth: 0, childGroups, ledgersByGroup))
                Rows.Add(row);

        // The reserved P&L head is kept out of Groups; show it (and its ledger) at the end.
        if (_company.ProfitAndLossHead is { } plHead)
            foreach (var row in BuildGroup(plHead, depth: 0, childGroups, ledgersByGroup))
                Rows.Add(row);
    }

    /// <summary>
    /// The rows for one group's whole subtree, in display order: the group, then its ledgers, then each sub-group's
    /// subtree — exactly the order the tree has always emitted.
    ///
    /// <para>Returning a list rather than appending straight to <see cref="Rows"/> is what makes the census-2.13
    /// filter possible: under <see cref="ShowUnusedOnly"/> a group is emitted <b>only when something survived
    /// beneath it</b>, and that is knowable only after its children have been built. With the filter OFF the
    /// method appends unconditionally, so the unfiltered tree is byte-identical to before.</para>
    /// </summary>
    private List<ChartRow> BuildGroup(
        Group group,
        int depth,
        IReadOnlyDictionary<Guid, List<Group>> childGroups,
        IReadOnlyDictionary<Guid, List<DomainLedger>> ledgersByGroup)
    {
        var below = new List<ChartRow>();

        // Ledgers sit directly under their group, one level deeper than the group.
        if (ledgersByGroup.TryGetValue(group.Id, out var ledgers))
            foreach (var l in ledgers)
            {
                if (ShowUnusedOnly && !UnusedMasters.IsLedgerUnused(_company, l)) continue;
                below.Add(new ChartRow
                {
                    Name = l.Name,
                    Kind = ChartNodeKind.Ledger,
                    Depth = depth + 1,
                    Detail = OpeningText(l),
                    LedgerId = l.Id,   // WI-3: Enter on this row opens the Ledger master for alteration.
                });
            }

        // Recurse into sub-groups (nested/indented under this parent).
        if (childGroups.TryGetValue(group.Id, out var subs))
            foreach (var sub in subs)
                below.AddRange(BuildGroup(sub, depth + 1, childGroups, ledgersByGroup));

        // Filtered: a head with nothing unused beneath it disappears rather than standing empty.
        if (ShowUnusedOnly && below.Count == 0) return below;

        var rows = new List<ChartRow>(below.Count + 1)
        {
            new ChartRow
            {
                Name = group.Name,
                Kind = depth == 0 ? ChartNodeKind.Primary : ChartNodeKind.SubGroup,
                Depth = depth,
                Detail = group.Nature.ToString(),
                GroupId = group.Id,   // WI-3: Enter on this row opens the Group master for alteration.
            },
        };
        rows.AddRange(below);
        return rows;
    }

    private static string OpeningText(DomainLedger l)
        => l.OpeningBalance == Money.Zero
            ? string.Empty
            : $"{IndianFormat.Amount(l.OpeningBalance)} {(l.OpeningIsDebit ? "Dr" : "Cr")}";
}
