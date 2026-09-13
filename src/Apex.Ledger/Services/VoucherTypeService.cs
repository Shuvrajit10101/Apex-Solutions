using Apex.Ledger.Domain;

namespace Apex.Ledger.Services;

/// <summary>
/// <b>The Voucher Type master's engine half — create / alter / delete / activate (census 2.4).</b>
///
/// <para><b>Why this exists.</b> <see cref="VoucherType"/> carries roughly twenty configurable properties and
/// twenty-four seeded instances, and until this slice <b>not one of them could be edited by an operator</b>: the
/// only write sites in the whole application were <c>JobWorkService</c> flipping <see cref="VoucherType.IsActive"/>
/// on two seeded types and a rollback restore inside a <c>catch</c>. That is why the shipped payroll voucher types
/// — seeded inactive, exactly as the domain comment says they are — could never be posted: nothing in the product
/// could switch them on. <see cref="SetActive"/> is that route.</para>
///
/// <para><b>R7 — fidelity.</b> ATTESTED at help.tallysolutions.com (fetched 2026-09-05): the master's fields
/// (Name, Abbreviation, Method of Voucher Numbering, "Provide narration for each ledger in voucher", "Print
/// voucher after saving") and that alteration is reached through <i>Alter Master &gt; Voucher Type</i>.
/// 🔴 <b>OURS, the vendor pages being silent (ruling 9):</b>
/// <list type="bullet">
///   <item>a <b>predefined</b> type may be renamed, reconfigured and deactivated but never DELETED — the twenty-four
///     seeds are what every F-key accelerator and menu row resolves against
///     (<see cref="VoucherTypeResolver"/>), so removing one would break a route rather than a master;</item>
///   <item>a type named by any posted voucher, inventory voucher or scenario cannot be deleted — the same
///     referential shape as every other <see cref="MasterDeletionRules"/> guard, and here it is additionally a
///     hard database constraint (<c>vouchers.type_id REFERENCES voucher_types(id)</c>), so without the guard the
///     removal would succeed in memory and then make the open company permanently unsavable;</item>
///   <item>the <b>base kind is immutable</b> once the type exists — changing it would silently re-interpret the
///     accounting direction, the stock effect and the report bucket of every voucher already posted under it;</item>
///   <item>every refusal message string.</item>
/// </list></para>
/// </summary>
public sealed class VoucherTypeService
{
    private readonly Company _company;

    public VoucherTypeService(Company company)
        => _company = company ?? throw new ArgumentNullException(nameof(company));

    /// <summary>
    /// Creates a user-defined voucher type over an existing base kind. The name must be non-blank and unique
    /// (case-insensitively) among the company's types; the new type is ACTIVE, non-predefined, and takes the
    /// accounting/stock effects its base kind implies (<see cref="VoucherEffects"/>).
    /// </summary>
    /// <exception cref="ArgumentException">The name is blank.</exception>
    /// <exception cref="InvalidOperationException">Another type already carries the name.</exception>
    public VoucherType Create(
        string name,
        VoucherBaseType baseType,
        NumberingMethod numbering,
        string? abbreviation = null,
        bool printAfterSaving = false,
        bool provideNarrationForEachLedger = false)
    {
        var clean = Clean(name);
        EnsureNameIsFree(clean, exceptId: null);

        var type = new VoucherType(
            Guid.NewGuid(), clean, baseType, numbering,
            defaultShortcut: null,
            abbreviation: Trimmed(abbreviation),
            isActive: true,
            isPredefined: false,
            printAfterSaving: printAfterSaving,
            provideNarrationForEachLedger: provideNarrationForEachLedger);

        _company.AddVoucherType(type);
        return type;
    }

    /// <summary>
    /// Reconfigures an existing type in place — name, numbering method, abbreviation, the active flag and the two
    /// user flags. The SAME instance is kept (mirroring <see cref="VoucherType.SetAffixes"/>), so every posted
    /// voucher's <c>TypeId</c> and every menu route stay valid and immediately re-render against the edit.
    ///
    /// <para>There is deliberately no base-kind parameter — see the class remarks.</para>
    /// </summary>
    /// <exception cref="InvalidOperationException">No such type, or another type already carries the name.</exception>
    /// <exception cref="ArgumentException">The name is blank.</exception>
    public void Alter(
        Guid id,
        string name,
        NumberingMethod numbering,
        string? abbreviation,
        bool isActive,
        bool printAfterSaving,
        bool provideNarrationForEachLedger)
    {
        var type = Require(id);
        var clean = Clean(name);
        EnsureNameIsFree(clean, exceptId: id);

        type.Name = clean;
        type.Numbering = numbering;
        type.Abbreviation = Trimmed(abbreviation);
        type.IsActive = isActive;
        type.PrintAfterSaving = printAfterSaving;
        type.ProvideNarrationForEachLedger = provideNarrationForEachLedger;
    }

    /// <summary>
    /// Removes a user-defined, unreferenced voucher type. Asks
    /// <see cref="MasterDeletionRules.EnsureVoucherTypeDeletable"/> first, so the refusal names the blocking
    /// documents rather than surfacing as a foreign-key failure at the next save.
    /// </summary>
    /// <exception cref="InvalidOperationException">No such type, it is predefined, or it is in use.</exception>
    public void Delete(Guid id)
    {
        var type = Require(id);
        MasterDeletionRules.EnsureVoucherTypeDeletable(_company, type);
        _company.RemoveVoucherType(type);
    }

    /// <summary>
    /// Switches a type on or off — the single-purpose verb behind the master list's activate/deactivate gesture,
    /// and the ONLY route by which a seeded-inactive type (the payroll family) can ever be reached for entry.
    /// </summary>
    /// <exception cref="InvalidOperationException">No such type.</exception>
    public void SetActive(Guid id, bool active) => Require(id).IsActive = active;

    /// <summary>
    /// Adds a named <b>Voucher Class</b> to a type (census 9.9) — the vendor's "Name of Class" plus its one
    /// attested flag, "Use Class for Inter-Godown Transfers".
    ///
    /// <para>🔴 <b>The inter-godown flag is refused on anything but a Stock Journal.</b> A class carrying it on a
    /// Sales type would advertise a transfer the entry screen cannot perform — the mirroring needs a source and a
    /// destination ARM, and only a Stock Journal has two. Refusing at the master is the only place an operator
    /// gets told; refusing later, at entry, would let the class be created, listed and picked before failing.</para>
    ///
    /// <para>The name must be non-blank and unique within the type (case-insensitively), matching the schema's
    /// unique index on (voucher_type_id, name) — enforcing it here turns what would otherwise surface as a raw
    /// constraint violation at Save time into a clean domain error naming the clash.</para>
    /// </summary>
    /// <exception cref="ArgumentException">The name is blank.</exception>
    /// <exception cref="InvalidOperationException">No such type, the name is taken on that type, or the
    /// inter-godown flag was asked for on a type that is not a Stock Journal.</exception>
    public VoucherClass AddClass(Guid typeId, string name, bool useClassForInterGodownTransfers)
    {
        var type = Require(typeId);
        var clean = Clean(name);

        if (useClassForInterGodownTransfers && type.BaseType != VoucherBaseType.StockJournal)
            throw new InvalidOperationException(
                "\"Use Class for Inter-Godown Transfers\" applies only to a Stock Journal voucher type.");

        if (type.Classes.Any(c => string.Equals(c.Name, clean, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException(
                $"Voucher type '{type.Name}' already has a class named '{clean}'.");

        var updated = type.Classes.ToList();
        updated.Add(new VoucherClass(Guid.NewGuid(), clean, useClassForInterGodownTransfers));
        type.SetClasses(updated);
        return updated[^1];
    }

    /// <summary>Removes a named voucher class from a type (census 9.9). A quiet no-op when the type has no such
    /// class, so a double-press of the remove gesture cannot throw at the operator.</summary>
    /// <exception cref="InvalidOperationException">No such type.</exception>
    public void RemoveClass(Guid typeId, Guid classId)
    {
        var type = Require(typeId);
        var updated = type.Classes.Where(c => c.Id != classId).ToList();
        if (updated.Count == type.Classes.Count) return;
        type.SetClasses(updated);
    }

    // ──────────────────────────────────────────── census 2.6 (v62): the general voucher-class machinery

    /// <summary>
    /// Adds a row to a class's vendor <b>"Default Accounting Allocations for all items in Invoice"</b> (census
    /// 2.6) — the ledger pre-map — and re-validates the WHOLE class through
    /// <see cref="VoucherClassService.Validate"/>.
    ///
    /// <para>🔴 <b>THE RE-VALIDATION IS THE POINT, AND IT MEANS ADDING A ROW CAN LEGITIMATELY FAIL.</b> Some rules
    /// are properties of the SET rather than of any one row — the same ledger must not take two allocations, for
    /// instance — so the row is appended, the class is checked, and on refusal the append is ROLLED BACK before
    /// the exception leaves: an operator told "no" must not be left with the bad row silently in place.</para>
    ///
    /// <para><b>What is deliberately NOT checked here is COMPLETENESS.</b> A 50/30/20 pre-map passes through 50%
    /// and 80% on its way to 100%, so enforcing the total on every append would refuse the first row of almost
    /// every class. <see cref="VoucherClassService.ValidateStructure"/> is used here and the 100% rule is enforced
    /// where it cannot be evaded — at the master-save gate and inside
    /// <see cref="VoucherClassPosting.Compute"/>, which refuses to post from an incomplete class.</para>
    /// </summary>
    /// <exception cref="InvalidOperationException">No such type or class, or the resulting class is invalid.</exception>
    public VoucherClassLedgerAllocation AddClassAllocation(
        Guid typeId, Guid classId, Guid ledgerId, int percentBasisPoints)
    {
        var cls = RequireClass(typeId, classId);
        var row = new VoucherClassLedgerAllocation(
            Guid.NewGuid(), ledgerId, percentBasisPoints, cls.LedgerAllocations.Count);

        cls.AddLedgerAllocation(row);
        try { VoucherClassService.ValidateStructure(cls, id => _company.FindLedger(id) is not null); }
        catch { RewriteAllocations(cls, cls.LedgerAllocations.Where(a => a.Id != row.Id).ToList()); throw; }
        return row;
    }

    /// <summary>Removes one default accounting allocation. <b>Not</b> re-validated: a class on its way from three
    /// allocations to two passes through a state that does not total 100%, and refusing the first removal would
    /// make the table impossible to edit. The set is checked again when a row is ADDED and at save.</summary>
    public void RemoveClassAllocation(Guid typeId, Guid classId, Guid allocationId)
    {
        var cls = RequireClass(typeId, classId);
        RewriteAllocations(cls, cls.LedgerAllocations.Where(a => a.Id != allocationId).ToList());
    }

    /// <summary>
    /// Adds a row to a class's vendor <b>"Additional Accounting Entries"</b> (census 2.6) — freight, a per-unit
    /// duty, the invoice round-off — and re-validates the whole class, rolling the append back on refusal exactly
    /// as <see cref="AddClassAllocation"/> does. The set-level rule here is that a class may carry at most ONE
    /// total-amount-rounding entry.
    /// </summary>
    public VoucherClassAdditionalEntry AddClassAdditionalEntry(
        Guid typeId,
        Guid classId,
        Guid ledgerId,
        VoucherClassCalculationType calculationType,
        Money valueBasis,
        VoucherClassRoundingMethod roundingMethod,
        Money roundingLimit,
        bool removeIfZero)
    {
        var cls = RequireClass(typeId, classId);
        var row = new VoucherClassAdditionalEntry(
            Guid.NewGuid(), ledgerId, calculationType, valueBasis, roundingMethod, roundingLimit,
            removeIfZero, cls.AdditionalEntries.Count);

        cls.AddAdditionalEntry(row);
        try { VoucherClassService.ValidateStructure(cls, id => _company.FindLedger(id) is not null); }
        catch { RewriteEntries(cls, cls.AdditionalEntries.Where(e => e.Id != row.Id).ToList()); throw; }
        return row;
    }

    /// <summary>Removes one additional accounting entry. Not re-validated, for the reason on
    /// <see cref="RemoveClassAllocation"/>.</summary>
    public void RemoveClassAdditionalEntry(Guid typeId, Guid classId, Guid entryId)
    {
        var cls = RequireClass(typeId, classId);
        RewriteEntries(cls, cls.AdditionalEntries.Where(e => e.Id != entryId).ToList());
    }

    private VoucherClass RequireClass(Guid typeId, Guid classId)
        => Require(typeId).Classes.FirstOrDefault(c => c.Id == classId)
           ?? throw new InvalidOperationException("The voucher class no longer exists.");

    /// <summary>Replaces a class's allocations, RENUMBERING <see cref="VoucherClassLedgerAllocation.Order"/> from
    /// zero. The renumber is load-bearing: the split's remainder lands on the LAST allocation, so leaving a gap
    /// after a removal would keep the order stable but is renumbered anyway so the stored order and the list
    /// position can never disagree across a reload.</summary>
    private static void RewriteAllocations(VoucherClass cls, IReadOnlyList<VoucherClassLedgerAllocation> rows)
    {
        var entries = cls.AdditionalEntries.ToList();
        cls.ClearAllocationsAndEntries();
        for (var i = 0; i < rows.Count; i++) { rows[i].Order = i; cls.AddLedgerAllocation(rows[i]); }
        foreach (var e in entries) cls.AddAdditionalEntry(e);
    }

    /// <summary>Replaces a class's additional entries, renumbering <see cref="VoucherClassAdditionalEntry.Order"/>
    /// from zero — see <see cref="RewriteAllocations"/>.</summary>
    private static void RewriteEntries(VoucherClass cls, IReadOnlyList<VoucherClassAdditionalEntry> rows)
    {
        var allocations = cls.LedgerAllocations.ToList();
        cls.ClearAllocationsAndEntries();
        foreach (var a in allocations) cls.AddLedgerAllocation(a);
        for (var i = 0; i < rows.Count; i++) { rows[i].Order = i; cls.AddAdditionalEntry(rows[i]); }
    }

    // ────────────────────────────────────────────────────────────────────────── guards

    private VoucherType Require(Guid id)
        => _company.FindVoucherType(id)
           ?? throw new InvalidOperationException("The voucher type no longer exists.");

    private static string Clean(string name)
        => string.IsNullOrWhiteSpace(name)
            ? throw new ArgumentException("A voucher type name is required.", nameof(name))
            : name.Trim();

    private static string? Trimmed(string? s)
        => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    /// <summary>
    /// Refuses a name another type already carries. Case-insensitive, because the name is what the operator picks
    /// from a menu and what <c>Company.FindVoucherTypeByName</c> resolves — two types differing only in case are
    /// two rows the operator cannot tell apart and one lookup cannot choose between.
    /// </summary>
    private void EnsureNameIsFree(string name, Guid? exceptId)
    {
        foreach (var other in _company.VoucherTypes)
        {
            if (exceptId is { } id && other.Id == id) continue;
            if (!string.Equals(other.Name, name, StringComparison.OrdinalIgnoreCase)) continue;

            throw new InvalidOperationException(
                $"A voucher type named '{other.Name}' already exists. Voucher type names must be unique.");
        }
    }
}
