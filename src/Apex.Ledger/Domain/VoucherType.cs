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

    public VoucherType(
        Guid id,
        string name,
        VoucherBaseType baseType,
        NumberingMethod numbering = NumberingMethod.Automatic,
        string? defaultShortcut = null,
        string? abbreviation = null,
        bool isActive = true,
        bool isPredefined = false)
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
    }
}
