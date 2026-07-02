using Apex.Ledger.Domain;

namespace Apex.Ledger.Reports;

/// <summary>
/// Centralises the Profit-and-Loss-vs-Balance-Sheet decision so every report agrees
/// (design §7.7). Walks a group's <c>ParentId</c> chain to the primary ancestor; the
/// six P&amp;L primaries are matched by the ancestor's <b>nature</b> (Income/Expense),
/// so a rename cannot break classification.
/// </summary>
public static class ClassificationRules
{
    /// <summary>Walks to the primary (parent-less) ancestor of <paramref name="group"/>.</summary>
    public static Group PrimaryAncestorOf(Group group, Company company)
    {
        var current = group;
        var guard = 0;
        while (current.ParentId is Guid parentId)
        {
            var parent = company.FindGroup(parentId)
                ?? throw new InvalidOperationException($"Group '{current.Name}' has unknown parent {parentId}.");
            current = parent;
            if (++guard > 1024)
                throw new InvalidOperationException($"Cycle detected walking parents of '{group.Name}'.");
        }
        return current;
    }

    /// <summary>The primary ancestor's nature (drives statement placement).</summary>
    public static GroupNature PrimaryNatureOf(Group group, Company company)
        => PrimaryAncestorOf(group, company).Nature;

    /// <summary>A group is a P&amp;L group iff its primary ancestor's nature is Income or Expense.</summary>
    public static bool IsProfitAndLossGroup(Group group, Company company)
    {
        var nature = PrimaryNatureOf(group, company);
        return nature is GroupNature.Income or GroupNature.Expense;
    }

    /// <summary>A group is a Balance-Sheet group iff it is not a P&amp;L group.</summary>
    public static bool IsBalanceSheetGroup(Group group, Company company)
        => !IsProfitAndLossGroup(group, company);

    /// <summary>Convenience: the P&amp;L classification for the group a ledger sits under.</summary>
    public static bool IsProfitAndLossLedger(Domain.Ledger ledger, Company company)
    {
        var group = company.FindGroup(ledger.GroupId)
            ?? throw new InvalidOperationException($"Ledger '{ledger.Name}' has unknown group {ledger.GroupId}.");
        return IsProfitAndLossGroup(group, company);
    }

    /// <summary>True iff the ledger sits under (or below) the Stock-in-Hand group.</summary>
    public static bool IsStockInHandLedger(Domain.Ledger ledger, Company company)
    {
        var group = company.FindGroup(ledger.GroupId);
        while (group is not null)
        {
            if (string.Equals(group.Name, "Stock-in-Hand", StringComparison.OrdinalIgnoreCase))
                return true;
            group = group.ParentId is Guid pid ? company.FindGroup(pid) : null;
        }
        return false;
    }
}
