namespace Apex.Ledger.Domain;

/// <summary>
/// How a <see cref="VoucherType"/> assigns voucher numbers (catalog §4).
/// </summary>
public enum NumberingMethod
{
    /// <summary>Engine assigns the next sequential number per type.</summary>
    Automatic,

    /// <summary>Caller supplies the number; uniqueness is checked.</summary>
    Manual,

    /// <summary>No number is assigned (<c>Number = 0</c>).</summary>
    None,
}
