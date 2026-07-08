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

    /// <summary>
    /// "Set/alter cheque printing configuration = Yes" (catalog §8) — the minimal cheque-printing data
    /// model. When true, this bank ledger prints cheques using <see cref="ChequePrintingBankName"/> as
    /// the selected bank format. The physical print LAYOUT (positions on the cheque) is deferred to a
    /// later slice; this flag + bank-format name is the persisted configuration.
    /// </summary>
    public bool EnableChequePrinting { get; set; }

    /// <summary>
    /// The bank / cheque-format name used when <see cref="EnableChequePrinting"/> is on (catalog §8;
    /// e.g. "HDFC Bank", "SBI"). Optional; <c>null</c> when cheque printing is off or the format is
    /// unset. The concrete layout for a given format is a later-slice concern.
    /// </summary>
    public string? ChequePrintingBankName { get; set; }

    /// <summary>
    /// "Activate Interest Calculation = Yes" (catalog §7) — the optional interest-parameter block. <c>null</c>
    /// (or a block with <see cref="InterestParameters.Enabled"/> false) means no interest accrues, so existing
    /// ledgers default off. When set and enabled, the interest projection accrues interest on this ledger's
    /// outstanding balance per the block's Rate / Per / On / Applicability / Style / Rounding settings.
    /// </summary>
    public InterestParameters? Interest { get; set; }

    /// <summary>
    /// "Currency of ledger" (catalog §2/§20 Multi-currency). <c>null</c> ⇒ the company <b>base currency</b>
    /// (₹/INR) — the default for every existing ledger. When set to a foreign <see cref="Currency"/>, this
    /// ledger holds its balances in that currency; its open forex balance can be revalued at period-end to
    /// book unrealized forex gain/loss.
    /// </summary>
    public Guid? CurrencyId { get; set; }

    /// <summary>
    /// Optional GST details on a <b>party</b> ledger (Sundry Debtor/Creditor) — Registration Type, GSTIN/UIN
    /// and State (catalog §12; phase4 RQ-7). Drives place of supply and B2B/B2C. <c>null</c> ⇒ no GST party
    /// details (treated as B2C for GST). The default for every existing ledger.
    /// </summary>
    public PartyGstDetails? PartyGst { get; set; }

    /// <summary>
    /// Optional GST details on a <b>sales/purchase</b> ledger — HSN/SAC, taxability, rate (catalog §12; phase4
    /// RQ-9). Lets a service or accounting-only supply resolve a GST rate (DP-10). <c>null</c> ⇒ none.
    /// </summary>
    public StockItemGstDetails? SalesPurchaseGst { get; set; }

    /// <summary>
    /// Marks this ledger as a GST <b>tax ledger</b> (Output/Input CGST/SGST/IGST) under Duties &amp; Taxes
    /// (catalog §12; phase4 RQ-4). Auto-set by <c>GstService.EnableGst</c>. <c>null</c> ⇒ ordinary ledger.
    /// </summary>
    public LedgerGstClassification? GstClassification { get; set; }

    /// <summary>
    /// "<b>Method of Appropriation in Purchase invoice</b>" (Book pp.133–141; catalog §11; Phase 6 slice 3
    /// RQ-16..RQ-20). A <b>non-null</b> value MARKS this ledger as an <b>additional-cost ledger</b> — an ordinary
    /// Direct-Expenses ledger (Freight/Packing/…) whose amount, when used on a Purchase whose voucher type has
    /// <see cref="VoucherType.TrackAdditionalCosts"/>, is apportioned across the item lines to raise their landed
    /// stock rate. <c>null</c> (the default for every existing ledger) ⇒ a plain P&amp;L ledger that never touches
    /// any stock rate (RQ-19). The expense still hits P&amp;L either way; a non-null method ADDS the inventory
    /// valuation-adjustment side.
    /// </summary>
    public MethodOfAppropriation? MethodOfAppropriation { get; set; }

    /// <summary>
    /// Optional <b>default Price Level</b> on a <b>party</b> ledger (Sundry Debtor) — Book pp.34–35; Phase 6
    /// slice 5; requirement RQ-30. When a Sales voucher selects this party, its Price Level header field is
    /// initialised from this level (still overridable per voucher). <c>null</c> (the default for every existing
    /// ledger) ⇒ no default level; only meaningful while <see cref="Company.EnableMultiplePriceLevels"/> is on.
    /// Nullable FK to a <see cref="PriceLevel"/>.
    /// </summary>
    public Guid? DefaultPriceLevelId { get; set; }

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
        bool? costCentresApplicable = null,
        bool enableChequePrinting = false,
        string? chequePrintingBankName = null,
        InterestParameters? interest = null,
        Guid? currencyId = null,
        PartyGstDetails? partyGst = null,
        StockItemGstDetails? salesPurchaseGst = null,
        LedgerGstClassification? gstClassification = null,
        MethodOfAppropriation? methodOfAppropriation = null,
        Guid? defaultPriceLevelId = null)
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
        EnableChequePrinting = enableChequePrinting;
        ChequePrintingBankName = string.IsNullOrWhiteSpace(chequePrintingBankName) ? null : chequePrintingBankName.Trim();
        Interest = interest;
        CurrencyId = currencyId;
        PartyGst = partyGst;
        SalesPurchaseGst = salesPurchaseGst;
        GstClassification = gstClassification;
        MethodOfAppropriation = methodOfAppropriation;
        DefaultPriceLevelId = defaultPriceLevelId;
    }

    /// <summary>True iff this ledger holds balances in a foreign (non-base) currency.</summary>
    public bool IsForeignCurrency => CurrencyId is not null;

    /// <summary>True iff this ledger is an <b>additional-cost ledger</b> — it carries a non-null
    /// <see cref="MethodOfAppropriation"/> so its amount is apportioned onto item landed rates (RQ-16..RQ-20).</summary>
    public bool IsAdditionalCostLedger => MethodOfAppropriation is not null;

    /// <summary>True iff this ledger has an interest block that is activated.</summary>
    public bool InterestEnabled => Interest is { Enabled: true };

    /// <summary>Signed opening: positive when debit, negative when credit (Dr = +, Cr = −).</summary>
    public decimal SignedOpening => OpeningIsDebit ? OpeningBalance.Amount : -OpeningBalance.Amount;
}
