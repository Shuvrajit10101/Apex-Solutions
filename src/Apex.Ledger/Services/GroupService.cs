using Apex.Ledger.Domain;
using Apex.Ledger.Reports;

namespace Apex.Ledger.Services;

/// <summary>
/// The accounting-Group masters service (catalog §3; WI-7). Creates a custom accounting <see cref="Group"/> —
/// e.g. a "Salary Payable" group under <b>Current Liabilities</b> holding one ledger per employee — enforcing the
/// same discipline the other masters ship with:
/// <list type="bullet">
///   <item>the name is <b>unique within the company</b> (case-insensitive, via the head-INCLUDING lookup, so a
///     group can never collide with the reserved Profit &amp; Loss head);</item>
///   <item>a <b>parent (Under) is required and must exist</b> — Tally creates a group "Under from the 28 lists of
///     groups", and the parent is what the nature is derived from;</item>
///   <item><b>the nature is DERIVED from the parent's primary ancestor, never accepted from the caller</b>
///     (<see cref="DeriveNature"/>) — the user picks only the parent, and Tally derives Asset/Liability/Income/
///     Expense from it.</item>
/// </list>
///
/// <para>
/// <b>The shared invariant.</b> <see cref="ValidateNatureAgainstParent"/> rejects a group whose declared nature
/// contradicts the nature it inherits from its parent ancestry. This is the same guard the canonical import must
/// run (<c>ImportPlan</c>): the import path historically accepted the caller-supplied nature verbatim, so a file
/// declaring <c>Nature=Asset</c> under Current Liabilities silently landed a payable on the <b>asset</b> side of
/// the Balance Sheet — the sheet still balanced, so nothing failed loudly (a financial-misread corruption). The
/// service and the import now share this one guard so the corruption cannot enter through either door.
/// </para>
///
/// <para>The service throws <see cref="InvalidOperationException"/> on any violation (never mutating the company),
/// mirroring <see cref="InventoryService"/>. It is framework- and DB-agnostic, so it is unit-tested like the core.</para>
/// </summary>
public sealed class GroupService
{
    private readonly Company _company;

    public GroupService(Company company)
        => _company = company ?? throw new ArgumentNullException(nameof(company));

    /// <summary>
    /// Creates a custom accounting group under <paramref name="parentId"/> (required, must exist). The name is
    /// trimmed + required + unique (head-including); the <b>nature is derived from the parent</b> — never accepted
    /// — so a "Salary Payable" under Current Liabilities is a Liability and prints on the Balance-Sheet liabilities
    /// side. <c>isPredefined: false</c>. Persistence, Io and report classification already handle custom groups.
    /// </summary>
    public Group CreateGroup(string name, Guid? parentId, string? alias = null)
    {
        var trimmed = RequireName(name);

        // Uniqueness uses the HEAD-including lookup so a group cannot collide with the reserved P&L head either.
        if (_company.FindGroupOrHeadByName(trimmed) is not null)
            throw new InvalidOperationException($"A group named '{trimmed}' already exists.");

        if (parentId is not { } pid)
            throw new InvalidOperationException(
                "A parent group (Under) is required — a group's nature is derived from its parent.");

        var parent = _company.FindGroup(pid)
            ?? throw new InvalidOperationException($"Parent group {pid} not found.");

        // Derive the nature from the parent's primary ancestor — the user never picks a nature (Tally derives it).
        var nature = DeriveNature(parent, _company);

        var aliasOrNull = string.IsNullOrWhiteSpace(alias) ? null : alias.Trim();
        var group = new Group(Guid.NewGuid(), trimmed, nature, pid, aliasOrNull, isPredefined: false);

        // Belt-and-suspenders: the shared invariant must hold for the group we just built (it always will here,
        // since the nature was derived — but this runs the SAME guard the import runs, so the two paths agree).
        ValidateNatureAgainstParent(group.Nature, group.ParentId, _company);

        _company.AddGroup(group);
        return group;
    }

    /// <summary>
    /// <b>Alters</b> an existing group in place (WI-3; Tally's Alter verb) — resolved by its stable
    /// <paramref name="groupId"/>, so a rename mutates the same node and every voucher, report and child group
    /// that references it follows automatically (they reference the Guid, never the name).
    ///
    /// <para>Guards, all delegated to <see cref="MasterAlterationRules"/> so the create path, the import path and
    /// every master screen share one implementation: the name is required and unique <b>excluding this group
    /// itself</b> (head-including, so it cannot collide with the reserved P&amp;L head); a predefined group may be
    /// neither renamed nor moved; a new parent must exist and must not sit inside this group's own sub-tree
    /// (cycle). After a successful re-parent the nature is <b>re-derived and cascaded to every descendant</b> —
    /// without that a moved sub-tree keeps its old ancestry's nature and silently lands on the wrong side of the
    /// Balance Sheet.</para>
    ///
    /// <para>Throws <see cref="InvalidOperationException"/> on any violation, having mutated nothing.</para>
    /// </summary>
    public Group AlterGroup(Guid groupId, string name, Guid? parentId, string? alias = null)
    {
        var group = _company.FindGroup(groupId)
            ?? throw new InvalidOperationException($"Group {groupId} not found.");

        // Validate EVERYTHING before mutating anything, so a rejected alteration leaves the company untouched.
        var trimmed = MasterAlterationRules.EnsureNameAvailable(_company, name, groupId, MasterKind.Group);
        MasterAlterationRules.EnsureGroupAlterAllowed(group, trimmed, parentId);

        if (parentId is not { } pid)
            throw new InvalidOperationException(
                "A parent group (Under) is required — a group's nature is derived from its parent.");

        MasterAlterationRules.EnsureGroupReparentValid(_company, groupId, pid);

        group.Name = trimmed;
        group.ParentId = pid;
        group.Alias = string.IsNullOrWhiteSpace(alias) ? null : alias.Trim();

        // Re-derive this group's nature from its (possibly new) parent and cascade to every descendant.
        MasterAlterationRules.RecomputeNatureFor(_company, groupId);

        // The shared invariant must hold afterwards — the same guard the canonical import runs.
        ValidateNatureAgainstParent(group.Nature, group.ParentId, _company);
        return group;
    }

    /// <summary>
    /// The nature a child group inherits: the nature of its parent's <b>primary ancestor</b>. Walks the parent's
    /// <c>ParentId</c> chain (with the classification rules' cycle guard). This is the value the create screen shows
    /// read-only and stores on the new group.
    /// </summary>
    public static GroupNature DeriveNature(Group parent, Company company)
        => ClassificationRules.PrimaryAncestorOf(parent, company).Nature;

    /// <summary>
    /// The shared WI-7 invariant: a group's <paramref name="declaredNature"/> MUST equal the nature it inherits
    /// from its parent ancestry. A primary group (null <paramref name="parentId"/>) has no parent to derive from,
    /// so its nature stands on its own and is accepted. Otherwise the parent must exist and its primary-ancestor
    /// nature must match; a mismatch throws <see cref="InvalidOperationException"/>.
    /// <para>This is the guard the canonical import (<c>ImportPlan</c>) runs so a contradicting-nature group — the
    /// live Balance-Sheet-corruption path — is rejected instead of silently landing on the wrong statement side.</para>
    /// </summary>
    public static void ValidateNatureAgainstParent(GroupNature declaredNature, Guid? parentId, Company company)
    {
        if (parentId is not { } pid) return; // a primary group has no parent to derive a nature from.

        var parent = company.FindGroup(pid)
            ?? throw new InvalidOperationException($"Parent group {pid} not found.");

        var derived = DeriveNature(parent, company);
        if (declaredNature != derived)
            throw new InvalidOperationException(
                $"Group nature '{declaredNature}' contradicts its parent '{parent.Name}', whose nature is '{derived}'. " +
                "A group's nature must match the nature derived from its parent (the derive-from-parent invariant).");
    }

    private static string RequireName(string? value)
    {
        var trimmed = (value ?? string.Empty).Trim();
        if (trimmed.Length == 0)
            throw new InvalidOperationException("A group name is required.");
        return trimmed;
    }
}
