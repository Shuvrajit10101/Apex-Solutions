using Apex.Ledger.Domain;
using Apex.Ledger.Reports;

namespace Apex.Ledger.Services;

/// <summary>The kind of master an alteration guard is being asked about (drives the message wording and which
/// name index is consulted).</summary>
public enum MasterKind
{
    Ledger,
    Group,
    StockItem,
}

/// <summary>
/// The shared guards for the <b>Alter</b> verb (WI-3; CA audit points 5 + 10) — Tally's second universal action
/// verb alongside Create (<c>docs/tally-feature-catalog.md:51</c>). Alter is the first feature in this codebase
/// that <b>mutates a master already referenced by posted vouchers</b>, so every rule that keeps that safe lives
/// here, in one pure, directly unit-tested place, instead of being re-implemented (and drifting) across the 20+
/// master screens.
///
/// <para><b>Identity, and why a rename propagates for free.</b> Every master's stable key is its
/// <see cref="Guid"/>, never its name (<c>Ledger.Name</c>: "a rename does not change identity";
/// <c>Group</c>: "the Id is the stable key — the Name is not, so an Alter renames in place"). Vouchers store
/// <c>ledger_id</c> Guids, and <c>SqliteCompanyStore.Save</c> is a delete-all + full re-insert snapshot. So an
/// alteration is: mutate the aggregate in memory, then Save — and a rename is retroactively visible in every
/// historical voucher and report with zero SQL and no migration.</para>
///
/// <para><b>What these guards exist to stop.</b>
/// <list type="number">
///   <item><b>The except-self uniqueness bug.</b> A create-time check ("does a master with this name already
///     exist?") reused verbatim on an Alter fails closed on the commonest alteration of all — open a master,
///     change one unrelated field, accept without renaming. <see cref="EnsureNameAvailable"/> excludes the master
///     being altered.</item>
///   <item><b>Renaming a ledger the engine finds BY NAME.</b> ~14 engine sites resolve a ledger through a
///     hardcoded well-known name, and they fail <i>silently</i>. The worst is <c>B2cQrService</c>, which does
///     <c>FindLedgerByName("Round Off") is not { } roundOff → return 0m</c>: rename "Round Off" and B2C-QR
///     rounding silently becomes zero, with no error anywhere. An <c>IsPredefined</c>-only guard does NOT cover
///     this — only two ledgers (Cash, Profit &amp; Loss A/c) are ever flagged predefined, while "Round Off" and
///     the GST/TDS/payroll ledgers are created without the flag. <see cref="WellKnownLedgerNames"/> is that
///     missing guard set, and <see cref="EnsureLedgerRenameAllowed"/> enforces it.</item>
///   <item><b>Cyclic re-parenting.</b> <c>Company.AddGroup</c> is a bare list add with no validation; a cycle
///     surfaces only much later as a raw <c>InvalidOperationException</c> thrown out of a <i>report</i> (the
///     1024-iteration guard in <c>ClassificationRules.PrimaryAncestorOf</c>). <see cref="EnsureGroupReparentValid"/>
///     rejects it at the master, where the user can act on it.</item>
/// </list></para>
///
/// <para>Every guard throws <see cref="InvalidOperationException"/> and never mutates the company, mirroring
/// <see cref="InventoryService"/> / <see cref="GroupService"/>. Pure, framework- and DB-agnostic.</para>
///
/// <para><b>Out of scope by ruling:</b> the alteration <b>audit trail</b> (who altered what, when, from what to
/// what). Tally keeps one, and it belongs with the Phase-10 security/roles/audit infrastructure this project has
/// not built yet — writing half of it here would leave an audit log no one can query or protect. Deferred to
/// Phase 10 deliberately; nothing in this file records history.</para>
/// </summary>
public static class MasterAlterationRules
{
    /// <summary>
    /// Ledger names the engine resolves by <b>hardcoded string</b> rather than by id. Renaming one of these
    /// breaks a code path <b>silently</b> — no exception, just a wrong (usually zero) number — so a rename away
    /// from any of them is refused outright.
    ///
    /// <para>Sourced from the by-name lookups in the engine and shell:
    /// <c>GstService.RoundOffLedgerName</c> and the GST tax-ledger names (read by <c>B2cQrService</c>,
    /// <c>RunSetOffViewModel</c>, <c>ElectronicLedgersView</c>, <c>GstDepositService</c>,
    /// <c>AdvanceReceiptService</c>, <c>GstConfigViewModel</c>), "Cash" (<c>OutstandingsViewModel</c>,
    /// <c>SeedLedgers</c>), "Profit &amp; Loss A/c", the forex gain/loss ledger (<c>ForexGainLoss</c>) and the
    /// payroll control ledgers (<c>PayrollVoucherService</c>). Matched case-insensitively.</para>
    /// </summary>
    public static readonly IReadOnlySet<string> WellKnownLedgerNames =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Cash",
            "Profit & Loss A/c",
            "Round Off",
            "Output CGST", "Output SGST", "Output IGST", "Output Cess",
            "Input CGST", "Input SGST", "Input IGST", "Input Cess",
            "TDS Payable", "TCS Payable",
            "Forex Gain/Loss",
            "Salary Payable", "PF Payable", "ESI Payable", "Professional Tax Payable",
        };

    // ------------------------------------------------------------------ name uniqueness (except self)

    /// <summary>
    /// The except-self name-uniqueness check every Alter needs and every create-time check lacks: the trimmed
    /// <paramref name="name"/> must be non-empty and must not be taken by a <b>different</b> master of the same
    /// kind. Passing <see cref="Guid.Empty"/> for <paramref name="exceptId"/> makes it the plain create-time check.
    ///
    /// <para>For <see cref="MasterKind.Group"/> the lookup is the <b>head-INCLUDING</b> one
    /// (<c>Company.FindGroupOrHeadByName</c>), because <c>FindGroupByName</c> deliberately excludes the reserved
    /// Profit &amp; Loss head — without this a user could create a second "Profit &amp; Loss A/c" group and fork
    /// the report classification.</para>
    /// </summary>
    /// <returns>The trimmed, validated name (use this, not the raw input).</returns>
    public static string EnsureNameAvailable(Company company, string? name, Guid exceptId, MasterKind kind)
    {
        ArgumentNullException.ThrowIfNull(company);

        var trimmed = (name ?? string.Empty).Trim();
        if (trimmed.Length == 0)
            throw new InvalidOperationException($"A {Label(kind)} name is required.");

        var clashId = kind switch
        {
            MasterKind.Ledger => company.FindLedgerByName(trimmed)?.Id,
            // Head-INCLUDING: a group must not collide with the reserved P&L head either.
            MasterKind.Group => company.FindGroupOrHeadByName(trimmed)?.Id,
            MasterKind.StockItem => company.FindStockItemByName(trimmed)?.Id,
            _ => null,
        };

        if (clashId is { } id && id != exceptId)
            throw new InvalidOperationException($"A {Label(kind)} named '{trimmed}' already exists.");

        return trimmed;
    }

    // ------------------------------------------------------------------ ledger guards

    /// <summary>
    /// Guards a ledger <b>rename</b>. A predefined ledger (Cash, Profit &amp; Loss A/c) and any ledger carrying a
    /// <see cref="WellKnownLedgerNames"/> name may not be renamed, because engine code resolves them by that exact
    /// string and would fail silently afterwards. Renaming to a *different* well-known name is likewise refused —
    /// it would shadow the real one. A no-op "rename" (same name, the accept-without-renaming case) always passes.
    /// </summary>
    public static void EnsureLedgerRenameAllowed(Domain.Ledger ledger, string newName)
    {
        ArgumentNullException.ThrowIfNull(ledger);
        var trimmed = (newName ?? string.Empty).Trim();

        // Not a rename at all — every other field on a protected ledger stays freely alterable.
        if (string.Equals(ledger.Name, trimmed, StringComparison.OrdinalIgnoreCase)) return;

        if (ledger.IsPredefined)
            throw new InvalidOperationException(
                $"'{ledger.Name}' is a predefined ledger and cannot be renamed.");

        if (WellKnownLedgerNames.Contains(ledger.Name))
            throw new InvalidOperationException(
                $"'{ledger.Name}' is a reserved ledger that the engine resolves by name (GST / round-off / payroll " +
                "posting would silently stop finding it). It cannot be renamed.");

        if (WellKnownLedgerNames.Contains(trimmed))
            throw new InvalidOperationException(
                $"'{trimmed}' is a reserved ledger name used by the engine; pick a different name.");
    }

    /// <summary>
    /// Guards a ledger <b>re-group</b> (changing its Under). The target group must exist. Re-grouping is otherwise
    /// permitted — it is a legitimate correction — but it is genuinely consequential: moving a ledger between a
    /// Balance-Sheet-nature group and a P&amp;L-nature group reclassifies <b>every historical transaction</b> on
    /// it. <see cref="DescribesReclassification"/> lets a screen warn about that before it happens.
    /// </summary>
    public static Group EnsureLedgerGroupValid(Company company, Guid newGroupId)
    {
        ArgumentNullException.ThrowIfNull(company);
        return company.FindGroup(newGroupId)
            ?? throw new InvalidOperationException($"Under group {newGroupId} not found.");
    }

    /// <summary>
    /// True iff moving a ledger from <paramref name="fromGroup"/> to <paramref name="toGroup"/> crosses the
    /// Balance-Sheet / P&amp;L divide — i.e. its primary-ancestor nature changes between {Asset, Liability} and
    /// {Income, Expense}. Such a move retroactively restates prior-period profit, so a screen should say so out
    /// loud rather than let the financials silently change.
    /// </summary>
    public static bool DescribesReclassification(Company company, Group fromGroup, Group toGroup)
    {
        ArgumentNullException.ThrowIfNull(company);
        ArgumentNullException.ThrowIfNull(fromGroup);
        ArgumentNullException.ThrowIfNull(toGroup);

        return IsProfitAndLoss(ClassificationRules.PrimaryNatureOf(fromGroup, company))
            != IsProfitAndLoss(ClassificationRules.PrimaryNatureOf(toGroup, company));

        static bool IsProfitAndLoss(GroupNature n) => n is GroupNature.Income or GroupNature.Expense;
    }

    // ------------------------------------------------------------------ group guards

    /// <summary>
    /// Guards a group <b>re-parent</b>: the new parent must exist, must not be the group itself, and must not be
    /// one of the group's own descendants (which would create a cycle). Follows the four existing cycle-guards in
    /// <see cref="InventoryService"/> / <c>PayrollService</c> — same shape, same 1024-step bound.
    /// </summary>
    public static void EnsureGroupReparentValid(Company company, Guid groupId, Guid? newParentId)
    {
        ArgumentNullException.ThrowIfNull(company);
        if (newParentId is not { } pid) return;   // becoming a primary group has no cycle to check.

        if (pid == groupId)
            throw new InvalidOperationException("A group cannot be its own parent.");

        _ = company.FindGroup(pid)
            ?? throw new InvalidOperationException($"Parent group {pid} not found.");

        // Walk UP from the proposed parent: if we meet the group being altered, the parent is a descendant of it.
        var cursor = newParentId;
        var guard = 0;
        while (cursor is { } cid && guard++ < 1024)
        {
            if (cid == groupId)
                throw new InvalidOperationException(
                    "That parent is inside this group's own sub-tree — the change would create a cycle.");
            cursor = company.FindGroup(cid)?.ParentId;
        }
    }

    /// <summary>
    /// Guards altering a <b>predefined</b> group. Tally ships 28 reserved groups; the catalogue states they cannot
    /// be deleted, and re-parenting one would move a whole primary head of the Balance Sheet underneath another.
    /// A predefined group may not be renamed or re-parented; a custom group is fully alterable.
    /// </summary>
    public static void EnsureGroupAlterAllowed(Group group, string newName, Guid? newParentId)
    {
        ArgumentNullException.ThrowIfNull(group);
        if (!group.IsPredefined) return;

        var trimmed = (newName ?? string.Empty).Trim();
        if (!string.Equals(group.Name, trimmed, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                $"'{group.Name}' is a predefined group and cannot be renamed.");

        if (newParentId != group.ParentId)
            throw new InvalidOperationException(
                $"'{group.Name}' is a predefined group and cannot be moved under another group.");
    }

    // ------------------------------------------------------------------ nature cascade

    /// <summary>
    /// Re-derives <see cref="Group.Nature"/> for <paramref name="groupId"/> from its parent's primary ancestor and
    /// <b>cascades to every descendant</b>. Required after a re-parent: <see cref="GroupService.DeriveNature"/>
    /// stamps the nature at create time, so without this cascade a re-parented group (and every group beneath it)
    /// would keep the nature of its OLD ancestry and land on the wrong side of the Balance Sheet — the exact
    /// silent-misclassification failure <see cref="GroupService.ValidateNatureAgainstParent"/> guards at import.
    /// </summary>
    /// <returns>The number of groups whose nature actually changed.</returns>
    public static int RecomputeNatureFor(Company company, Guid groupId)
    {
        ArgumentNullException.ThrowIfNull(company);

        var group = company.FindGroup(groupId)
            ?? throw new InvalidOperationException($"Group {groupId} not found.");

        var changed = 0;
        var queue = new Queue<Group>();
        queue.Enqueue(group);
        var guard = 0;

        while (queue.Count > 0 && guard++ < 4096)
        {
            var g = queue.Dequeue();
            if (g.ParentId is { } pid && company.FindGroup(pid) is { } parent)
            {
                var derived = GroupService.DeriveNature(parent, company);
                if (g.Nature != derived) { g.Nature = derived; changed++; }
            }

            foreach (var child in company.Groups.Where(c => c.ParentId == g.Id))
                queue.Enqueue(child);
        }

        return changed;
    }

    private static string Label(MasterKind kind) => kind switch
    {
        MasterKind.Ledger => "ledger",
        MasterKind.Group => "group",
        MasterKind.StockItem => "stock item",
        _ => "master",
    };
}
