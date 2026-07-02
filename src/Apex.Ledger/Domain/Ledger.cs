namespace Apex.Ledger.Domain;

/// <summary>
/// A transactional account — the thing a voucher line actually posts to
/// (catalog §3; plan.md §4.1). Opening balance is stored as a magnitude plus a
/// side (<see cref="OpeningIsDebit"/>), mirroring the fixtures'
/// <c>{openingBalance, openingSide}</c> shape and Tally's "Opening Balance … Dr/Cr".
/// </summary>
public sealed class Ledger
{
    /// <summary>Stable surrogate key.</summary>
    public Guid Id { get; }

    /// <summary>Unique within a company; a rename does not change identity.</summary>
    public string Name { get; set; }

    /// <summary>The group this ledger is <c>Under</c>; required.</summary>
    public Guid GroupId { get; set; }

    /// <summary>Opening magnitude, always ≥ 0. The side lives in <see cref="OpeningIsDebit"/>.</summary>
    public Money OpeningBalance { get; set; }

    /// <summary><c>true</c> = opening Dr, <c>false</c> = opening Cr.</summary>
    public bool OpeningIsDebit { get; set; }

    /// <summary>Optional short name.</summary>
    public string? Alias { get; set; }

    /// <summary>True for the 2 predefined ledgers (Cash, Profit &amp; Loss A/c) — cannot be deleted.</summary>
    public bool IsPredefined { get; }

    public Ledger(
        Guid id,
        string name,
        Guid groupId,
        Money openingBalance,
        bool openingIsDebit,
        string? alias = null,
        bool isPredefined = false)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Ledger name is required.", nameof(name));
        if (openingBalance.Amount < 0m)
            throw new ArgumentException("Opening balance magnitude must be ≥ 0.", nameof(openingBalance));

        Id = id;
        Name = name;
        GroupId = groupId;
        OpeningBalance = openingBalance;
        OpeningIsDebit = openingIsDebit;
        Alias = alias;
        IsPredefined = isPredefined;
    }

    /// <summary>Signed opening: positive when debit, negative when credit (Dr = +, Cr = −).</summary>
    public decimal SignedOpening => OpeningIsDebit ? OpeningBalance.Amount : -OpeningBalance.Amount;
}
