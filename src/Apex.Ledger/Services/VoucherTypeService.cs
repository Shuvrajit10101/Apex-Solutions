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
