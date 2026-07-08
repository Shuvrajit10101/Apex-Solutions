namespace Apex.Ledger.Domain;

/// <summary>
/// Orders a self-referential master collection (stock groups, stock categories, godowns) <b>topologically —
/// every parent before its children</b>, roots first. The persistence layer inserts in this order so a
/// row's self-FK (<c>parent_id → id</c>) is always already present, even when a parent was created after a
/// child in list order (e.g. a re-parent under a later-created sibling). Cycle detection guarantees the
/// method never loops forever and never lets a raw FK error surface: a cycle raises a clean domain
/// <see cref="InvalidOperationException"/>.
/// </summary>
public static class HierarchyOrdering
{
    /// <summary>
    /// Returns <paramref name="items"/> ordered parents-before-children. <paramref name="idOf"/> reads a
    /// node's id and <paramref name="parentOf"/> its parent id (<c>null</c> ⇒ a root). A parent that is not
    /// present in <paramref name="items"/> (an external/implicit root) is treated as a root anchor. Throws an
    /// <see cref="InvalidOperationException"/> naming <paramref name="kind"/> if the parent links form a cycle.
    /// </summary>
    public static IReadOnlyList<T> ParentsBeforeChildren<T>(
        IEnumerable<T> items,
        Func<T, Guid> idOf,
        Func<T, Guid?> parentOf,
        string kind)
    {
        ArgumentNullException.ThrowIfNull(items);
        var all = items.ToList();
        var byId = new Dictionary<Guid, T>(all.Count);
        foreach (var item in all)
            byId[idOf(item)] = item;

        var ordered = new List<T>(all.Count);
        var state = new Dictionary<Guid, int>(all.Count); // 0/absent = unvisited, 1 = in-progress, 2 = done

        void Visit(T node)
        {
            var id = idOf(node);
            if (state.TryGetValue(id, out var s))
            {
                if (s == 1)
                    throw new InvalidOperationException($"{kind} parent chain forms a cycle.");
                return; // already emitted
            }
            state[id] = 1;
            if (parentOf(node) is { } parentId && byId.TryGetValue(parentId, out var parent))
                Visit(parent); // emit the parent first
            state[id] = 2;
            ordered.Add(node);
        }

        foreach (var item in all)
            Visit(item);

        return ordered;
    }
}
