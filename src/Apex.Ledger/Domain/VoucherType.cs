namespace Apex.Ledger.Domain;

/// <summary>
/// Defines a class of vouchers with its base behaviour, default shortcut, and
/// numbering (catalog §4; plan.md §4.1). 24 are seeded; custom types may be added.
/// </summary>
public sealed class VoucherType
{
    /// <summary>Stable surrogate key.</summary>
    public Guid Id { get; }

    /// <summary>e.g. "Payment", "Sales".</summary>
    public string Name { get; set; }

    /// <summary>The built-in kind it derives from.</summary>
    public VoucherBaseType BaseType { get; set; }

    /// <summary>e.g. "F5", "Alt+F6"; <c>null</c> for types without a default shortcut.</summary>
    public string? DefaultShortcut { get; set; }

    /// <summary>Automatic / Manual / None.</summary>
    public NumberingMethod Numbering { get; set; }

    /// <summary>e.g. "Pymt", "Sale".</summary>
    public string? Abbreviation { get; set; }

    /// <summary>Payroll/Job-Work types are inactive until their F11 feature is enabled.</summary>
    public bool IsActive { get; set; }

    /// <summary>True for the 24 seeds.</summary>
    public bool IsPredefined { get; }

    /// <summary>
    /// Whether posting a voucher of this type produces a double-entry accounting effect (catalog §10;
    /// phase3-inventory-requirements RQ-8..RQ-15). Order and pure stock vouchers (PO/SO, GRN, Delivery,
    /// Rejection In/Out, Stock Journal, Physical Stock) do <b>not</b> affect accounts in Phase 3 (DP-5);
    /// accounting types (Payment, Receipt, Journal, Sales, Purchase, …) do. Defaulted per
    /// <see cref="VoucherEffects"/> when not specified so existing callers are unaffected.
    /// </summary>
    public bool AffectsAccounts { get; set; }

    /// <summary>
    /// Whether posting a voucher of this type moves stock (catalog §10). GRN/Delivery/Rejection/Stock
    /// Journal/Physical Stock move stock; PO/SO and pure accounting types do not. Defaulted per
    /// <see cref="VoucherEffects"/> when not specified.
    /// </summary>
    public bool AffectsStock { get; set; }

    /// <summary>
    /// <b>Use as Manufacturing Journal</b> (catalog §11; Phase 6 Cluster 2 RQ-11). A user-created voucher type
    /// whose <see cref="BaseType"/> is <see cref="VoucherBaseType.StockJournal"/> may set this <b>Yes</b> to
    /// behave as a Manufacturing Journal: it consumes BOM components (source) and produces the finished good +
    /// by-products/scrap (destination), and — unlike a plain Stock Journal — its source and destination need
    /// <b>not</b> balance by quantity (a manufacture transforms inputs into a different output, RQ-13). The
    /// finished-good production line is valued = Σ component cost + Σ additional cost − Σ carve-out value
    /// (RQ-13). Defaults to <c>false</c>, so every existing Stock Journal is byte-identical (ER-13). Only
    /// meaningful on a Stock-Journal base type.
    /// </summary>
    public bool UseAsManufacturingJournal { get; set; }

    /// <summary>True iff this is a Manufacturing Journal — a Stock-Journal base type with
    /// <see cref="UseAsManufacturingJournal"/> on (RQ-11).</summary>
    public bool IsManufacturingJournal =>
        BaseType == VoucherBaseType.StockJournal && UseAsManufacturingJournal;

    public VoucherType(
        Guid id,
        string name,
        VoucherBaseType baseType,
        NumberingMethod numbering = NumberingMethod.Automatic,
        string? defaultShortcut = null,
        string? abbreviation = null,
        bool isActive = true,
        bool isPredefined = false,
        bool? affectsAccounts = null,
        bool? affectsStock = null,
        bool useAsManufacturingJournal = false)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Voucher type name is required.", nameof(name));

        Id = id;
        Name = name;
        BaseType = baseType;
        Numbering = numbering;
        DefaultShortcut = defaultShortcut;
        Abbreviation = abbreviation;
        IsActive = isActive;
        IsPredefined = isPredefined;
        AffectsAccounts = affectsAccounts ?? VoucherEffects.AffectsAccounts(baseType);
        AffectsStock = affectsStock ?? VoucherEffects.AffectsStock(baseType);
        UseAsManufacturingJournal = useAsManufacturingJournal;
    }
}
