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
/// <summary>
/// The five behavioural fields the vendor's Group master carries below <i>Name / Under</i>, passed as one value so
/// the create and alter paths cannot drift into accepting different subsets (census 2.2; schema v60).
///
/// <para><b>R7 — every caption is vendor-verbatim</b> from
/// <c>help.tallysolutions.com/groups-in-tallyprime/</c>; see <see cref="Group"/> for the individual quotes and for
/// which of them any report in this build actually reads (exactly one does).</para>
///
/// <para><see cref="None"/> is the all-off value every pre-v60 group has and every caller that does not care about
/// these fields should pass, so "not supplied" and "explicitly all off" are the same thing rather than two
/// behaviours.</para>
/// </summary>
public sealed record GroupBehaviour(
    bool BehavesLikeSubLedger = false,
    bool NettBalancesForReporting = false,
    bool UsedForCalculation = false,
    bool AffectsGrossProfits = false,
    MethodOfAppropriation? PurchaseAllocationMethod = null)
{
    /// <summary>All flags off, no allocation method — what every group created before v60 is.</summary>
    public static readonly GroupBehaviour None = new();
}

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
    public Group CreateGroup(
        string name,
        Guid? parentId,
        string? alias = null,
        GroupNature? primaryNature = null,
        GroupBehaviour? behaviour = null)
    {
        var trimmed = RequireName(name);

        // Uniqueness uses the HEAD-including lookup so a group cannot collide with the reserved P&L head either.
        if (_company.FindGroupOrHeadByName(trimmed) is not null)
            throw new InvalidOperationException($"A group named '{trimmed}' already exists.");

        var behave = behaviour ?? GroupBehaviour.None;
        GroupNature nature;

        if (parentId is { } pid)
        {
            var parent = _company.FindGroup(pid)
                ?? throw new InvalidOperationException($"Parent group {pid} not found.");

            // The vendor shows "Nature of Group" ONLY under Primary; a child's nature is its ancestry's, and
            // accepting one here would give a sub-tree two sources of truth for its Balance-Sheet side.
            if (primaryNature is not null)
                throw new InvalidOperationException(
                    "Nature of Group is set only on a PRIMARY group — a group under a parent derives its nature "
                    + "from that parent's primary ancestor.");

            nature = DeriveNature(parent, _company);
        }
        else
        {
            // Census 2.2 / defect T1-31: a NEW PRIMARY group. Until v60 this threw outright, which is why the
            // vendor's "Nature of Group" and "Does it affect Gross Profits" were structurally unreachable —
            // both live on the primary-group screen. A primary group has no ancestry to derive from, so the
            // nature is the operator's own choice and is REQUIRED rather than defaulted: silently defaulting it
            // would put a whole new sub-tree on a side of the Balance Sheet nobody chose.
            nature = primaryNature
                ?? throw new InvalidOperationException(
                    "A primary group needs a Nature of Group (Assets, Liabilities, Income or Expenses) — "
                    + "it has no parent to derive one from.");

            if (!Enum.IsDefined(nature))
                throw new InvalidOperationException($"'{nature}' is not a valid Nature of Group.");
        }

        EnsureBehaviourValid(behave, nature, isPrimary: parentId is null, name: trimmed);

        var aliasOrNull = string.IsNullOrWhiteSpace(alias) ? null : alias.Trim();
        var group = new Group(Guid.NewGuid(), trimmed, nature, parentId, aliasOrNull, isPredefined: false);
        ApplyBehaviour(group, behave);

        // Belt-and-suspenders: the shared invariant must hold for the group we just built (it always will here,
        // since the nature was derived — but this runs the SAME guard the import runs, so the two paths agree).
        // For a primary group it is a no-op by design: there is no parent to contradict.
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
    public Group AlterGroup(
        Guid groupId,
        string name,
        Guid? parentId,
        string? alias = null,
        GroupNature? primaryNature = null,
        GroupBehaviour? behaviour = null)
    {
        var group = _company.FindGroup(groupId)
            ?? throw new InvalidOperationException($"Group {groupId} not found.");

        // Validate EVERYTHING before mutating anything, so a rejected alteration leaves the company untouched.
        var trimmed = MasterAlterationRules.EnsureNameAvailable(_company, name, groupId, MasterKind.Group);
        MasterAlterationRules.EnsureGroupAlterAllowed(group, trimmed, parentId);
        MasterAlterationRules.EnsureGroupReparentValid(_company, groupId, parentId);

        var behave = behaviour ?? GroupBehaviour.None;

        // The nature the group will END UP with, computed BEFORE anything is mutated so the behaviour guard below
        // judges the post-alteration state rather than the pre-alteration one.
        GroupNature natureAfter;
        if (parentId is { } pid)
        {
            var parent = _company.FindGroup(pid)
                ?? throw new InvalidOperationException($"Parent group {pid} not found.");
            if (primaryNature is not null)
                throw new InvalidOperationException(
                    "Nature of Group is set only on a PRIMARY group — a group under a parent derives its nature "
                    + "from that parent's primary ancestor.");
            natureAfter = DeriveNature(parent, _company);
        }
        else
        {
            // Census 2.2 / T1-31: altering a PRIMARY group. Its nature is its own, so it is required here for the
            // same reason it is on create — and a re-parent that MOVES a group up to Primary lands in this branch
            // too, where the operator must state the nature the whole moved sub-tree will inherit.
            natureAfter = primaryNature
                ?? throw new InvalidOperationException(
                    "A primary group needs a Nature of Group (Assets, Liabilities, Income or Expenses) — "
                    + "it has no parent to derive one from.");

            if (!Enum.IsDefined(natureAfter))
                throw new InvalidOperationException($"'{natureAfter}' is not a valid Nature of Group.");

            // 🔴 A PREDEFINED primary head's nature is NOT alterable, and this is the same reasoning
            // MasterAlterationRules.EnsureGroupAlterAllowed applies to renaming and re-parenting one. Flipping
            // "Sales Accounts" from Income to Expense would cascade to every group and ledger beneath it and
            // move the whole head to the other side of the statement — silently, because the book would still
            // balance. Nothing in the vendor documentation asks for it, and a custom head is the supported way
            // to introduce a differently-natured top-level head.
            if (group.IsPredefined && natureAfter != group.Nature)
                throw new InvalidOperationException(
                    $"'{group.Name}' is a predefined group and its Nature of Group cannot be changed — "
                    + "every group and ledger beneath it would move to the other side of the statement.");
        }

        EnsureBehaviourValid(behave, natureAfter, isPrimary: parentId is null, name: trimmed);

        group.Name = trimmed;
        group.ParentId = parentId;
        group.Alias = string.IsNullOrWhiteSpace(alias) ? null : alias.Trim();
        ApplyBehaviour(group, behave);

        if (parentId is null)
        {
            // A primary group's nature is stated, not derived; cascade it so every descendant follows the head
            // onto the side of the statement it now prints on. RecomputeNatureFor walks DOWN from this group, so
            // setting the head first and cascading after is what makes the sub-tree agree with it.
            group.Nature = natureAfter;
        }

        // Re-derive this group's nature from its (possibly new) parent and cascade to every descendant. For a
        // primary group this leaves the head alone and cascades the stated nature downwards.
        MasterAlterationRules.RecomputeNatureFor(_company, groupId);

        // The shared invariant must hold afterwards — the same guard the canonical import runs.
        ValidateNatureAgainstParent(group.Nature, group.ParentId, _company);
        return group;
    }

    /// <summary>
    /// The guards on the five behavioural fields, shared by create and alter so the two cannot drift.
    ///
    /// <para><b>Both refusals exist to prevent a stored setting NOTHING will ever read</b> — the "dead flag"
    /// failure this project has filed before — rather than to invent a vendor rule:</para>
    /// <list type="bullet">
    ///   <item><i>"Does it affect Gross Profits"</i> is shown by the vendor <b>only under a Primary group</b>. On a
    ///     child it is meaningless: placement above or below the Gross Profit line is decided by the primary
    ///     ancestor, so a value here would be written and never consulted.</item>
    ///   <item>It is refused on an <b>Assets / Liabilities</b> primary for the same reason: those are Balance-Sheet
    ///     heads, <c>ClassificationRules.IsProfitAndLossGroup</c> excludes them, and the Gross Profit computation
    ///     never reaches them. 🔴 <b>This second guard is OURS, labelled as such</b> — the vendor page reached
    ///     names the field and its Direct/Indirect meaning but does not say it is restricted by nature. Refusing
    ///     is the smaller, honest thing: it tells the operator the setting would do nothing instead of accepting
    ///     one that silently does nothing.</item>
    /// </list>
    /// </summary>
    private static void EnsureBehaviourValid(
        GroupBehaviour behaviour, GroupNature nature, bool isPrimary, string name)
    {
        if (behaviour.PurchaseAllocationMethod is { } method && !Enum.IsDefined(method))
            throw new InvalidOperationException(
                $"'{method}' is not a valid Method to allocate when used in purchase invoice.");

        if (!behaviour.AffectsGrossProfits) return;

        if (!isPrimary)
            throw new InvalidOperationException(
                "\"Does it affect Gross Profits\" is set only on a PRIMARY group — a group under a parent is "
                + "placed above or below the Gross Profit line by its primary ancestor.");

        if (nature is not (GroupNature.Income or GroupNature.Expense))
            throw new InvalidOperationException(
                $"\"Does it affect Gross Profits\" does not apply to '{name}': a group of nature '{nature}' is a "
                + "Balance-Sheet head and never reaches the Gross Profit computation.");
    }

    /// <summary>Copies the five behavioural fields onto <paramref name="group"/>, in one place so create and
    /// alter write exactly the same set and a field added later cannot be wired into only one of them.</summary>
    private static void ApplyBehaviour(Group group, GroupBehaviour behaviour)
    {
        group.BehavesLikeSubLedger = behaviour.BehavesLikeSubLedger;
        group.NettBalancesForReporting = behaviour.NettBalancesForReporting;
        group.UsedForCalculation = behaviour.UsedForCalculation;
        group.AffectsGrossProfits = behaviour.AffectsGrossProfits;
        group.PurchaseAllocationMethod = behaviour.PurchaseAllocationMethod;
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
