using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Apex.Ledger;
using Apex.Ledger.Domain;
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
public sealed class ChartRow
{
    public string Name { get; init; } = string.Empty;
    public ChartNodeKind Kind { get; init; }
    public int Depth { get; init; }
    public string Detail { get; init; } = string.Empty;

    public bool IsGroup => Kind != ChartNodeKind.Ledger;
    public bool IsPrimary => Kind == ChartNodeKind.Primary;

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
public sealed partial class ChartOfAccountsViewModel : ViewModelBase
{
    private readonly Company _company;

    [ObservableProperty] private string _title = "Chart of Accounts";
    [ObservableProperty] private string _subtitle = string.Empty;

    public ObservableCollection<ChartRow> Rows { get; } = new();

    public ChartOfAccountsViewModel(Company company)
    {
        _company = company ?? throw new ArgumentNullException(nameof(company));
        Subtitle = $"{company.Name}  —  Groups & Ledgers";
        Build();
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
            EmitGroup(primary, depth: 0, childGroups, ledgersByGroup);

        // The reserved P&L head is kept out of Groups; show it (and its ledger) at the end.
        if (_company.ProfitAndLossHead is { } plHead)
            EmitGroup(plHead, depth: 0, childGroups, ledgersByGroup);
    }

    private void EmitGroup(
        Group group,
        int depth,
        IReadOnlyDictionary<Guid, List<Group>> childGroups,
        IReadOnlyDictionary<Guid, List<DomainLedger>> ledgersByGroup)
    {
        Rows.Add(new ChartRow
        {
            Name = group.Name,
            Kind = depth == 0 ? ChartNodeKind.Primary : ChartNodeKind.SubGroup,
            Depth = depth,
            Detail = group.Nature.ToString(),
        });

        // Ledgers sit directly under their group, one level deeper than the group.
        if (ledgersByGroup.TryGetValue(group.Id, out var ledgers))
            foreach (var l in ledgers)
                Rows.Add(new ChartRow
                {
                    Name = l.Name,
                    Kind = ChartNodeKind.Ledger,
                    Depth = depth + 1,
                    Detail = OpeningText(l),
                });

        // Recurse into sub-groups (nested/indented under this parent).
        if (childGroups.TryGetValue(group.Id, out var subs))
            foreach (var sub in subs)
                EmitGroup(sub, depth + 1, childGroups, ledgersByGroup);
    }

    private static string OpeningText(DomainLedger l)
        => l.OpeningBalance == Money.Zero
            ? string.Empty
            : $"{IndianFormat.Amount(l.OpeningBalance)} {(l.OpeningIsDebit ? "Dr" : "Cr")}";
}
