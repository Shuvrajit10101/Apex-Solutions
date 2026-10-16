using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Linq;
using System.Reflection;
using Apex.Ledger.Domain;

namespace Apex.Ledger.Services;

/// <summary>
/// 🔴 <b>A ROLLBACK POINT FOR A BATCH OF MASTER WRITES — the thing that makes a multi-row Accept genuinely
/// all-or-nothing instead of nearly all-or-nothing.</b>
///
/// <para><b>The defect this exists to kill, measured.</b> <c>MultiMasterCreateViewModel</c>'s ALTER arm wrote its
/// rows one at a time and relied on a pre-validation pass to guarantee that none of them could be refused. The
/// pre-validation covered the name, the in-batch duplicate and the parent-not-null; it did <b>not</b> cover the
/// ENGINE guards, which run inside each service call — <c>EnsureLedgerRenameAllowed</c>,
/// <c>EnsureLedgerGroupValid</c>, <c>EnsureGroupAlterAllowed</c>, <c>EnsureGroupReparentValid</c>,
/// <c>EnsureGodownParentValid</c>, and <c>AlterCostCentre</c>'s same-category and cycle checks. So a batch whose
/// third row tripped a guard had already written rows one and two <b>into the live aggregate</b>, and the screen
/// then told the operator the batch was refused. The company object was not saved by that screen — but it is the
/// shell's one live aggregate, so the next unrelated screen that saved made the half-batch <b>durable</b>. A
/// ledger rename is resolved by id, so the books silently followed a rename the operator was told did not happen.
/// The code's own defence for this — a comment claiming "the in-memory aggregate is reloaded from disk by the
/// caller's Escape/re-open" — was <b>false</b>: <c>CompanyStorage.Load</c> is reached only from company-open and
/// company-unlock, and Escape pops a UI column and reloads nothing.</para>
///
/// <para><b>Why a snapshot rather than a fuller pre-validation.</b> A row-by-row pre-validation pass is
/// <i>unsound</i> for groups, godowns and cost centres: their cycle and same-category guards judge the state the
/// aggregate will be in once the OTHER rows of the same batch have been applied, so validating every row against
/// the pre-batch state accepts combinations that are illegal together. The only way to know a batch is legal is
/// to apply it — which is safe exactly when applying it can be undone.</para>
///
/// <para>🔴 <b>Completeness by construction, which is the whole reason this is reflection and not a hand-written
/// field list.</b> The restore puts back <b>every writable property</b> of every master in the collections it
/// covers, discovered from the type at run time. A property added to <see cref="Domain.Ledger"/>,
/// <see cref="Group"/>, <see cref="StockItem"/>, <see cref="Godown"/> or <see cref="CostCentre"/> is snapshotted
/// and restored the day it is added, with no edit in this file. A hand-written list is the failure this project
/// keeps hitting: it is correct the day it is written and silently incomplete a wave later. The same argument is
/// made, and accepted, for <c>VoucherSnapshot</c>'s whole-object serialisation.</para>
///
/// <para><b>What it covers, stated exactly.</b> Six collections —
/// <see cref="Company.Ledgers"/>, <see cref="Company.Groups"/>, <see cref="Company.StockItems"/>,
/// <see cref="Company.Godowns"/>, <see cref="Company.CostCentres"/> and
/// <see cref="Company.StockOpeningBalances"/> — and for each: which objects were members, and the value of every
/// writable property on each member. <see cref="Restore"/> therefore undoes (a) a property written on an existing
/// master, including <c>RecomputeNatureFor</c>'s cascade over every descendant of an altered group, (b) a master
/// ADDED during the batch, and (c) a master REMOVED during the batch.</para>
///
/// <para>⚠️ <b>The one thing it does NOT undo, said plainly.</b> A writable property whose value is a mutable
/// object (for example <c>Ledger.ChequeLayout</c>) is captured <i>by reference</i>: replacing the object is
/// undone, mutating the object in place is not. No master create/alter path reaches one — those nested objects
/// are written by their own dedicated screens, not by a multi-row grid — but this is a real boundary and is
/// written down rather than left for a reader to discover. Nothing outside the six collections is covered at
/// all: this is a MASTER rollback, not a company one, and it must not be reached for to make a voucher-writing
/// path transactional.</para>
/// </summary>
public sealed class MasterStateSnapshot
{
    /// <summary>The writable properties of each master type, discovered once. Reflection is cheap when the
    /// metadata lookup is not repeated per row; a batch over a few thousand masters is then a few thousand
    /// delegate calls rather than a few thousand type queries.</summary>
    private static readonly ConcurrentDictionary<Type, PropertyInfo[]> PropertyCache = new();

    private readonly List<CollectionState> _collections;

    private MasterStateSnapshot(List<CollectionState> collections) => _collections = collections;

    /// <summary>How many master objects this snapshot can put back. Exposed so a test can prove the snapshot
    /// actually saw the book rather than silently capturing nothing.</summary>
    public int MemberCount => _collections.Sum(c => c.MemberCount);

    /// <summary>How many individual property values this snapshot holds.</summary>
    public int PropertyValueCount => _collections.Sum(c => c.PropertyValueCount);

    /// <summary>
    /// Captures the state of every master in the six covered collections. Call this immediately before a batch of
    /// writes; call <see cref="Restore"/> if any of them is refused.
    /// </summary>
    public static MasterStateSnapshot Capture(Company company)
    {
        ArgumentNullException.ThrowIfNull(company);

        var collections = new List<CollectionState>
        {
            CollectionState.Of(company.Ledgers, company.AddLedger, l => company.RemoveLedger(l)),
            CollectionState.Of(company.Groups, company.AddGroup, g => company.RemoveGroup(g)),
            CollectionState.Of(company.StockItems, company.AddStockItem, i => company.RemoveStockItem(i)),
            CollectionState.Of(company.Godowns, company.AddGodown, g => company.RemoveGodown(g)),
            CollectionState.Of(company.CostCentres, company.AddCostCentre, c => company.RemoveCostCentre(c)),
            CollectionState.Of(
                company.StockOpeningBalances,
                company.AddStockOpeningBalance,
                b => company.RemoveStockOpeningBalance(b)),
        };

        return new MasterStateSnapshot(collections);
    }

    /// <summary>
    /// Puts the six collections back exactly as <see cref="Capture"/> found them: additions removed, removals
    /// reinstated, and every writable property of every surviving master written back to its captured value.
    ///
    /// <para>Idempotent — restoring twice is the same as restoring once — so a caller may call it on a path that
    /// might already have restored without reasoning about whether it did.</para>
    /// </summary>
    public void Restore()
    {
        foreach (var collection in _collections) collection.Restore();
    }

    /// <summary>The writable instance properties of <paramref name="type"/>, cached.
    ///
    /// <para><b>Non-public setters are included deliberately.</b> The question this class answers is "what could
    /// a write have changed", not "what can a caller in another assembly change" — a domain service in this same
    /// assembly can reach an internal setter, and a value restored through one is still the pre-batch value.
    /// Including them can only make the restore more complete.</para></summary>
    private static PropertyInfo[] WritablePropertiesOf(Type type)
        => PropertyCache.GetOrAdd(type, static t => t
            .GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            .Where(p => p.CanRead && p.CanWrite && p.GetIndexParameters().Length == 0)
            .ToArray());

    /// <summary>One collection's captured state: its membership, and every member's property values.</summary>
    private sealed class CollectionState
    {
        private readonly Func<IEnumerable<object>> _readLive;
        private readonly Action<object> _add;
        private readonly Action<object> _remove;
        private readonly List<object> _members;
        private readonly List<PropertyInfo[]> _properties;
        private readonly List<object?[]> _values;

        private CollectionState(
            Func<IEnumerable<object>> readLive,
            Action<object> add,
            Action<object> remove,
            List<object> members,
            List<PropertyInfo[]> properties,
            List<object?[]> values)
        {
            _readLive = readLive;
            _add = add;
            _remove = remove;
            _members = members;
            _properties = properties;
            _values = values;
        }

        public int MemberCount => _members.Count;

        public int PropertyValueCount => _values.Sum(v => v.Length);

        /// <summary>Captures one of the company's master lists. The three delegates are passed in rather than
        /// discovered, so the compiler — not a string — ties each list to its own Add and Remove.</summary>
        public static CollectionState Of<T>(IReadOnlyList<T> live, Action<T> add, Func<T, bool> remove)
            where T : class
        {
            var members = new List<object>(live.Count);
            var properties = new List<PropertyInfo[]>(live.Count);
            var values = new List<object?[]>(live.Count);

            foreach (var member in live)
            {
                // The member's RUNTIME type, not T: a derived master would otherwise have its extra properties
                // silently left out of the snapshot and so left mutated by a restore.
                var props = WritablePropertiesOf(member.GetType());
                var captured = new object?[props.Length];
                for (var i = 0; i < props.Length; i++) captured[i] = props[i].GetValue(member);

                members.Add(member);
                properties.Add(props);
                values.Add(captured);
            }

            return new CollectionState(
                () => live.Cast<object>(),
                o => add((T)o),
                o => remove((T)o),
                members,
                properties,
                values);
        }

        public void Restore()
        {
            var captured = new HashSet<object>(_members, ReferenceEqualityComparer.Instance);

            // (a) anything ADDED during the batch is not in the captured set — take it back out.
            foreach (var live in _readLive().ToList())
                if (!captured.Contains(live))
                    _remove(live);

            // (b) anything REMOVED during the batch is captured but no longer live — put it back. No master
            // create/alter path removes, so this is a completeness arm rather than a hot one; it restores the
            // object, and the list position it lands in is the end rather than its original index.
            var liveNow = new HashSet<object>(_readLive(), ReferenceEqualityComparer.Instance);
            foreach (var member in _members)
                if (!liveNow.Contains(member))
                    _add(member);

            // (c) every surviving member's writable properties go back to the captured values.
            for (var m = 0; m < _members.Count; m++)
            {
                var props = _properties[m];
                var vals = _values[m];
                for (var p = 0; p < props.Length; p++) props[p].SetValue(_members[m], vals[p]);
            }
        }
    }
}
