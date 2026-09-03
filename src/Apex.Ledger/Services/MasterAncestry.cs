using Apex.Ledger.Domain;

namespace Apex.Ledger.Services;

/// <summary>
/// The <b>guarded nearest-ancestor-bearing-a-block</b> walk over the two master trees that carry GST details above
/// a Stock Item and above a Sales/Purchase Ledger — the accounting <see cref="Group"/> chain and the
/// <see cref="StockGroup"/> chain. Used by <see cref="GstService"/>'s rate hierarchy and by nothing else.
/// Framework-, DB- and clock-free, like the rest of the engine.
///
/// <para>🔴 <b>ANCESTRY IS OURS, AND IT IS A RULING-9 DIVERGENCE — not clone fidelity.</b> Neither the corpus nor
/// the vendor's published hierarchy says whether the "Accounting Group" and "Stock Group" rungs read the master's
/// IMMEDIATE parent only, or climb the parent chain to the nearest ancestor that declares a block. Grounding is
/// UNREACHED. <b>We climb.</b> Both <see cref="Group.ParentId"/> and <see cref="StockGroup.ParentId"/> are real
/// trees and a rate typed on a grandparent group is an ordinary book setup, which the immediate-parent reading
/// would silently drop. The two readings give DIFFERENT TAX, so the choice is pinned by named tests
/// (<c>GstHierarchyAncestryTests</c>) rather than left to whichever line of code happened to be written.</para>
///
/// <para>🔴 <b>WHY THIS IS A PREREQUISITE AND NOT A TIDY-UP.</b> The only other nearest-ancestor-with-a-value walk
/// in the tree, <c>ReorderStatus.ResolveDefinition</c>, has <b>NO CYCLE GUARD</b> — copying its shape onto the
/// money path would put an unbounded loop there. And a cyclic chain can already be present in a book:
/// <c>InventoryService.EnsureStockGroupParentValid</c> guards <c>CreateStockGroup</c> / <c>SetStockGroupParent</c>,
/// but the canonical import path does not go through either. <see cref="ClassificationRules"/>'s
/// <c>PrimaryAncestorOf</c> does carry a guard, but it walks to the PRIMARY (parent-less) ancestor, which is the
/// wrong shape here — it would step straight past every intermediate group that declares a rate.</para>
///
/// <para><b>The two failure shapes are answered differently, on purpose.</b> A <b>cycle</b> throws a named domain
/// error: it is a structurally impossible book, no ancestor on the loop declares anything (or the walk would have
/// returned before closing it), and falling silently through to a lower rung would rate the line off the wrong
/// master with no signal. A <b>dangling parent id</b> — an ancestor the book does not contain — simply ends the
/// climb: one broken reference should not make a book unpostable, and the walk still has the rungs below it plus
/// the ER-5 fail-fast behind them, so nothing becomes a silent zero. Both are pinned by test.</para>
/// </summary>
public static class MasterAncestry
{
    /// <summary>
    /// The nearest accounting <see cref="Group"/> at or above <paramref name="groupId"/> that declares a
    /// <see cref="MasterGstDetails"/> block, or <c>null</c> when no ancestor does (or the chain is broken).
    /// </summary>
    /// <exception cref="InvalidOperationException">The parent chain forms a nesting cycle.</exception>
    public static MasterGstDetails? NearestGroupGst(Company company, Guid? groupId)
    {
        ArgumentNullException.ThrowIfNull(company);
        return Climb(
            groupId,
            id => company.FindGroup(id) is { } g ? new Node(g.Name, g.Gst, g.ParentId) : null,
            "accounting group");
    }

    /// <summary>
    /// The nearest <see cref="StockGroup"/> at or above <paramref name="stockGroupId"/> that declares a
    /// <see cref="MasterGstDetails"/> block, or <c>null</c> when no ancestor does (or the chain is broken).
    /// </summary>
    /// <exception cref="InvalidOperationException">The parent chain forms a nesting cycle.</exception>
    public static MasterGstDetails? NearestStockGroupGst(Company company, Guid? stockGroupId)
    {
        ArgumentNullException.ThrowIfNull(company);
        return Climb(
            stockGroupId,
            id => company.FindStockGroup(id) is { } g ? new Node(g.Name, g.Gst, g.ParentId) : null,
            "stock group");
    }

    /// <summary>One rung of a master tree, flattened so the two trees share one walk.</summary>
    private readonly record struct Node(string Name, MasterGstDetails? Gst, Guid? ParentId);

    /// <summary>
    /// Climbs from <paramref name="startId"/> and returns the first block found. The visited set is modelled on
    /// <c>InventoryService.EnsureStockGroupParentValid</c>: every id is recorded before it is followed, so meeting
    /// one twice is a cycle and terminates in bounded time — the walk can visit each node at most once.
    /// </summary>
    private static MasterGstDetails? Climb(Guid? startId, Func<Guid, Node?> lookup, string kind)
    {
        if (startId is not { } cursor) return null;

        var seen = new HashSet<Guid>();
        var trail = string.Empty;
        while (true)
        {
            if (!seen.Add(cursor))
                throw new InvalidOperationException(
                    $"The {kind} ancestry above '{trail}' forms a nesting cycle — cannot resolve GST details.");

            if (lookup(cursor) is not { } node) return null; // a broken chain declares nothing; climb ends
            if (node.Gst is { } block) return block;         // stop at the nearest ancestor bearing a block
            if (node.ParentId is not { } parent) return null;

            trail = node.Name;
            cursor = parent;
        }
    }
}
