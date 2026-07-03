namespace Apex.Ledger.Domain;

/// <summary>
/// A transactional account — the thing a voucher line actually posts to
/// (catalog §3; plan.md §4.1). Opening balance is stored as a magnitude plus a
/// side (<see cref="OpeningIsDebit"/>), mirroring the fixtures'
/// <c>{openingBalance, openingSide}</c> shape and the "Opening Balance … Dr/Cr" convention.
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

    /// <summary>
    /// "Maintain balances bill-by-bill" (catalog §5). When true, party lines posting to this ledger
    /// carry bill-wise allocations and the ledger's open bills are tracked for Outstandings/ageing.
    /// </summary>
    public bool MaintainBillByBill { get; set; }

    /// <summary>
    /// Default credit period in days (catalog §5). When a New-Ref allocation omits an explicit due
    /// date and its own credit-period days, the due date derives from the voucher date + this value.
    /// </summary>
    public int? DefaultCreditPeriodDays { get; set; }

    /// <summary>
    /// "Cost centres applicable = Yes/No" (catalog §6). <c>null</c> ⇒ <b>auto</b>: the effective value
    /// follows the ledger's nature (true for Income/Expense-nature ledgers, false otherwise). Set a
    /// non-null value to <b>override</b> that default explicitly. Resolve the effective flag with
    /// <see cref="Reports.ClassificationRules.CostCentresApplicableFor"/> (which needs the company to
    /// walk the group's nature).
    /// </summary>
    public bool? CostCentresApplicable { get; set; }

    public Ledger(
        Guid id,
        string name,
        Guid groupId,
        Money openingBalance,
        bool openingIsDebit,
        string? alias = null,
        bool isPredefined = false,
        bool maintainBillByBill = false,
        int? defaultCreditPeriodDays = null,
        bool? costCentresApplicable = null)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Ledger name is required.", nameof(name));
        if (openingBalance.Amount < 0m)
            throw new ArgumentException("Opening balance magnitude must be ≥ 0.", nameof(openingBalance));

        if (defaultCreditPeriodDays is < 0)
            throw new ArgumentException("Default credit period days must be ≥ 0.", nameof(defaultCreditPeriodDays));

        Id = id;
        Name = name;
        GroupId = groupId;
        OpeningBalance = openingBalance;
        OpeningIsDebit = openingIsDebit;
        Alias = alias;
        IsPredefined = isPredefined;
        MaintainBillByBill = maintainBillByBill;
        DefaultCreditPeriodDays = defaultCreditPeriodDays;
        CostCentresApplicable = costCentresApplicable;
    }

    /// <summary>Signed opening: positive when debit, negative when credit (Dr = +, Cr = −).</summary>
    public decimal SignedOpening => OpeningIsDebit ? OpeningBalance.Amount : -OpeningBalance.Amount;
}
