using Apex.Ledger.Domain;

namespace Apex.Ledger.Services;

/// <summary>
/// Resolves WHICH <see cref="VoucherType"/> a base-kind route opens — an F-key accelerator, a Gateway menu row,
/// or any other caller that knows only "a Sales voucher" rather than a specific series.
/// <para>
/// It exists because the shape it replaces was wrong in three ways. The old form, repeated at five call sites,
/// was <c>FirstOrDefault(t =&gt; t.BaseType == x &amp;&amp; t.IsActive) ?? FirstOrDefault(t =&gt; t.BaseType == x)</c>:
/// </para>
/// <list type="number">
/// <item>the second arm silently opened a <b>deactivated</b> type when no active one existed, which made
/// <see cref="VoucherType.IsActive"/> decorative — a type could be switched off and still be entered, so the
/// documented "show inactive → activate" gesture meant nothing;</item>
/// <item>the first arm returned whichever row happened to come first, so a company running a second series
/// (export sales, a branch series, a separate numbering series) got a route whose target depended on list order
/// rather than on any stated rule;</item>
/// <item>neither arm distinguished a <b>specialised variant</b> that merely reuses a base kind — a Manufacturing
/// Journal is a Stock Journal, a POS till is a Sales — so a base route could land one of those in the ordinary
/// entry screen, which is the wrong screen with the wrong posting rules.</item>
/// </list>
/// <para>
/// The rule here is: <b>only ACTIVE, non-specialised types are ever opened</b>, and among those the
/// <b>predefined</b> one wins — so the F-keys and menu rows always land on the seeded series, deterministically,
/// while a company that deactivates the seeded series falls to its remaining active one. Callers that know the
/// exact type the operator chose (a picker row, a report drill-down) must pass that instance instead of a base
/// kind: identity beats resolution, always.
/// </para>
/// </summary>
public static class VoucherTypeResolver
{
    /// <summary>
    /// Whether <paramref name="type"/> is a specialised variant of its base kind — a type that reuses a base kind
    /// but belongs to its OWN entry screen: a Manufacturing Journal (Stock Journal base, RQ-11) or a POS Sales
    /// till (Sales base, RQ-38). A base-kind route must never resolve to one of these, because the ordinary
    /// screen would apply the ordinary rules to it (a manufacture forced to balance by quantity; a till billed
    /// without its tender split, under the wrong number series).
    /// </summary>
    public static bool IsSpecialisedVariant(VoucherType type)
    {
        ArgumentNullException.ThrowIfNull(type);
        return type.IsManufacturingJournal || type.IsPosSales;
    }

    /// <summary>
    /// The type a <paramref name="baseType"/> route should open on <paramref name="company"/>, or <c>null</c>
    /// when the company has no ACTIVE, non-specialised type of that kind (an inactive one is never returned —
    /// the caller is expected to tell the operator rather than open it behind their back).
    /// </summary>
    public static VoucherType? ResolveForEntry(Company company, VoucherBaseType baseType)
    {
        ArgumentNullException.ThrowIfNull(company);

        VoucherType? firstActive = null;
        foreach (var type in company.VoucherTypes)
        {
            if (type.BaseType != baseType || !type.IsActive) continue;
            if (IsSpecialisedVariant(type)) continue;
            if (type.IsPredefined) return type;   // the seeded series wins outright
            firstActive ??= type;
        }
        return firstActive;
    }

    /// <summary>
    /// The message to show when <see cref="ResolveForEntry"/> finds nothing. Phrased so the operator can act on
    /// it: the fix for a deactivated type is to activate it, not to hunt for a missing master.
    /// <para>
    /// Prefer the <see cref="NoActiveTypeMessage(Company, VoucherBaseType)"/> overload wherever a company is in
    /// hand — this one can only fall back to the enum name, and the operator has never seen an enum.
    /// </para>
    /// </summary>
    public static string NoActiveTypeMessage(VoucherBaseType baseType) =>
        Message(DisplayName(baseType));

    /// <summary>
    /// The same message, naming the type the way the rest of the UI does: the company's own type of that base
    /// kind lends its NAME ("Credit Note", "Physical Stock", or a company's renamed series), and only when the
    /// company has no such row at all does the enum name — split into words — stand in.
    /// </summary>
    public static string NoActiveTypeMessage(Company company, VoucherBaseType baseType) =>
        Message(DisplayName(company, baseType));

    private static string Message(string displayName) =>
        $"No active '{displayName}' voucher type is configured for this company.";

    /// <summary>
    /// How a <paramref name="baseType"/> should be NAMED to an operator when the company is not in hand: the
    /// enum identifier split at its capitals, so "CreditNote" reads "Credit Note" and "JobWorkInOrder" reads
    /// "Job Work In Order". The unspaced identifiers appear nowhere in the UI, so they must not appear in a
    /// message either.
    /// </summary>
    public static string DisplayName(VoucherBaseType baseType)
    {
        var raw = baseType.ToString();
        var sb = new System.Text.StringBuilder(raw.Length + 4);
        for (var i = 0; i < raw.Length; i++)
        {
            if (i > 0 && char.IsUpper(raw[i])) sb.Append(' ');
            sb.Append(raw[i]);
        }
        return sb.ToString();
    }

    /// <summary>
    /// How a <paramref name="baseType"/> should be NAMED to an operator on <paramref name="company"/>: the name
    /// of its own type of that kind — the seeded predefined one first, then any other ordinary one — so a company
    /// that renamed or re-seriesed a kind is told about the thing it can actually see. A
    /// <see cref="IsSpecialisedVariant">specialised variant</see> never lends its name, because a base-kind route
    /// would never have opened it. Falls back to <see cref="DisplayName(VoucherBaseType)"/>.
    /// </summary>
    public static string DisplayName(Company company, VoucherBaseType baseType)
    {
        ArgumentNullException.ThrowIfNull(company);

        VoucherType? fallback = null;
        foreach (var type in company.VoucherTypes)
        {
            if (type.BaseType != baseType || IsSpecialisedVariant(type)) continue;
            if (string.IsNullOrWhiteSpace(type.Name)) continue;
            if (type.IsPredefined) return type.Name;
            fallback ??= type;
        }
        return fallback?.Name ?? DisplayName(baseType);
    }

    /// <summary>
    /// A seeded voucher-type shortcut that has since been CORRECTED, and the value it was corrected to. The
    /// string is persisted per company (<c>voucher_types.default_shortcut</c>), so a company created before the
    /// correction still carries the superseded one — and it is not merely stale text: the Day-Book Alt+A picker
    /// renders it verbatim, so the company is shown a key that opens a DIFFERENT screen (the superseded "F10"
    /// opens this app's Other Vouchers menu) while the authored menu row beside it shows the corrected key.
    /// </summary>
    private static readonly (VoucherBaseType BaseType, string Superseded, string Corrected)[] SupersededShortcuts =
    {
        // Physical Stock: seeded "F10" until the official key (Ctrl+F7) was bound — see SeedVoucherTypes.
        (VoucherBaseType.PhysicalStock, "F10", "Ctrl+F7"),
    };

    /// <summary>
    /// Repairs the superseded seeded shortcuts listed in <see cref="SupersededShortcuts"/> on a company loaded
    /// from disk, so the data-driven surfaces agree with the authored ones. Returns how many rows were changed;
    /// idempotent, so a second call returns 0.
    /// <para>
    /// This is a LOAD-TIME data repair, deliberately not a schema migration: the column's shape does not change,
    /// only one predefined row's value, and a version bump would cost a CreateV1 change, a migration, a schema
    /// parity test, a downgrade helper and an Io fold-in for a single hint string. It touches ONLY a
    /// <see cref="VoucherType.IsPredefined"/> row still carrying the exact superseded string, so a value anyone
    /// deliberately set is left alone.
    /// </para>
    /// </summary>
    public static int RepairSupersededSeedShortcuts(Company company)
    {
        ArgumentNullException.ThrowIfNull(company);

        var repaired = 0;
        foreach (var type in company.VoucherTypes)
        {
            if (!type.IsPredefined) continue;
            foreach (var (baseType, superseded, corrected) in SupersededShortcuts)
            {
                if (type.BaseType != baseType) continue;
                if (!string.Equals(type.DefaultShortcut, superseded, StringComparison.Ordinal)) continue;
                type.DefaultShortcut = corrected;
                repaired++;
            }
        }
        return repaired;
    }
}
