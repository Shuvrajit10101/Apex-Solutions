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

    /// <summary>
    /// The effective "Cost centres applicable" flag for a ledger (catalog §6). Honours an explicit
    /// override on <see cref="Domain.Ledger.CostCentresApplicable"/> when set; otherwise defaults by
    /// nature: <c>true</c> for Income/Expense-nature (P&amp;L) ledgers, <c>false</c> otherwise.
    /// </summary>
    public static bool CostCentresApplicableFor(Domain.Ledger ledger, Company company)
        => ledger.CostCentresApplicable ?? IsProfitAndLossLedger(ledger, company);

    /// <summary>
    /// True iff <paramref name="ledger"/> sits under <paramref name="groupId"/> directly or transitively
    /// (walks the ledger's group's <c>ParentId</c> chain up to the primary ancestor). Used for group-target
    /// budget roll-ups (§7): a group budget aggregates every ledger under it, at any depth.
    /// </summary>
    public static bool LedgerIsUnderGroup(Domain.Ledger ledger, Guid groupId, Company company)
    {
        var group = company.FindGroup(ledger.GroupId);
        var guard = 0;
        while (group is not null)
        {
            if (group.Id == groupId) return true;
            group = group.ParentId is Guid pid ? company.FindGroup(pid) : null;
            if (++guard > 1024)
                throw new InvalidOperationException($"Cycle detected walking parents of ledger '{ledger.Name}'.");
        }
        return false;
    }

    /// <summary>
    /// True iff the group identified by <paramref name="groupId"/> is, or sits under (transitively), a group
    /// named <paramref name="ancestorName"/>. Resolution starts from the <b>actual group id</b> (never a
    /// name lookup), so two distinct groups that share a display name are told apart correctly. A <c>null</c>
    /// id (a synthetic Balance-Sheet head with no owning ledger) is never under any group.
    /// </summary>
    public static bool GroupIsUnder(Guid? groupId, string ancestorName, Company company)
    {
        var group = groupId is Guid gid ? company.FindGroup(gid) : null;
        var guard = 0;
        while (group is not null)
        {
            if (string.Equals(group.Name, ancestorName, StringComparison.OrdinalIgnoreCase)) return true;
            group = group.ParentId is Guid pid ? company.FindGroup(pid) : null;
            if (++guard > 1024)
                throw new InvalidOperationException($"Cycle detected walking parents of group {groupId}.");
        }
        return false;
    }

    /// <summary>
    /// True iff <paramref name="ledger"/> sits under (or below) the <b>Cash-in-Hand</b> group (catalog §8).
    /// A cash ledger together with a bank ledger (<see cref="IsBankLedger"/>) forms the "cash &amp; bank"
    /// pool the Cash-Flow statement reconciles.
    /// </summary>
    public static bool IsCashLedger(Domain.Ledger ledger, Company company)
    {
        var group = company.FindGroup(ledger.GroupId);
        var guard = 0;
        while (group is not null)
        {
            if (string.Equals(group.Name, "Cash-in-Hand", StringComparison.OrdinalIgnoreCase))
                return true;
            group = group.ParentId is Guid pid ? company.FindGroup(pid) : null;
            if (++guard > 1024)
                throw new InvalidOperationException($"Cycle detected walking parents of ledger '{ledger.Name}'.");
        }
        return false;
    }

    /// <summary>
    /// True iff <paramref name="ledger"/> is part of the cash &amp; bank pool: a Cash-in-Hand ledger
    /// (<see cref="IsCashLedger"/>) or a Bank Accounts / Bank OD ledger (<see cref="IsBankLedger"/>).
    /// This is the set whose opening→closing movement the Cash-Flow statement reconciles.
    /// </summary>
    public static bool IsCashOrBankLedger(Domain.Ledger ledger, Company company)
        => IsCashLedger(ledger, company) || IsBankLedger(ledger, company);

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

    /// <summary>
    /// True iff <paramref name="ledger"/> sits under (or below) the <b>Duties &amp; Taxes</b> group
    /// (catalog §12; phase4 ER-8). A GST tax ledger lives here, so it is <b>excluded</b> from the item-invoice
    /// stock-leg pairing sum (whose primary ancestor must be Sales/Purchase Accounts or Stock-in-Hand) — this
    /// is exactly why additive GST tax preserves the pairing invariant unchanged. Walks the group's parent
    /// chain, matching by group name.
    /// </summary>
    public static bool IsDutiesAndTaxesLedger(Domain.Ledger ledger, Company company)
    {
        var group = company.FindGroup(ledger.GroupId);
        var guard = 0;
        while (group is not null)
        {
            if (string.Equals(group.Name, "Duties & Taxes", StringComparison.OrdinalIgnoreCase))
                return true;
            group = group.ParentId is Guid pid ? company.FindGroup(pid) : null;
            if (++guard > 1024)
                throw new InvalidOperationException($"Cycle detected walking parents of ledger '{ledger.Name}'.");
        }
        return false;
    }

    /// <summary>
    /// The two predefined bank groups (catalog §8 / seed §22): <b>Bank Accounts</b> (an asset) and
    /// <b>Bank OD A/c</b> (a liability, alias "Bank OCC A/c"). A ledger under either — directly or via a
    /// custom sub-group — is a bank ledger that can carry bank allocations and appear in Bank Reconciliation.
    /// </summary>
    public static readonly IReadOnlyList<string> BankGroupNames = new[]
    {
        "Bank Accounts",
        "Bank OD A/c",
        "Bank OCC A/c", // alias of Bank OD A/c
    };

    /// <summary>
    /// True iff <paramref name="ledger"/> sits under (or below) one of the predefined bank groups
    /// (catalog §8): Bank Accounts or Bank OD A/c (alias Bank OCC A/c). Walks the group's parent chain,
    /// matching by group name or alias so a bank ledger under a custom sub-group is still recognised.
    /// </summary>
    public static bool IsBankLedger(Domain.Ledger ledger, Company company)
    {
        var group = company.FindGroup(ledger.GroupId);
        var guard = 0;
        while (group is not null)
        {
            foreach (var name in BankGroupNames)
            {
                if (string.Equals(group.Name, name, StringComparison.OrdinalIgnoreCase) ||
                    (group.Alias is not null && string.Equals(group.Alias, name, StringComparison.OrdinalIgnoreCase)))
                    return true;
            }
            group = group.ParentId is Guid pid ? company.FindGroup(pid) : null;
            if (++guard > 1024)
                throw new InvalidOperationException($"Cycle detected walking parents of ledger '{ledger.Name}'.");
        }
        return false;
    }
}
