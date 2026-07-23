namespace Apex.Ledger.Domain;

/// <summary>
/// Defines a class of vouchers with its base behaviour, default shortcut, and
/// numbering (catalog §4; plan.md §4.1). 24 are seeded; custom types may be added.
/// </summary>
public sealed class VoucherType
{
    private readonly List<VoucherNumberAffix> _prefixes;
    private readonly List<VoucherNumberAffix> _suffixes;

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

    /// <summary>
    /// "<b>Track Additional Costs for Purchases</b>" (Book pp.133–141; catalog §11; Phase 6 slice 3
    /// RQ-16..RQ-20). A voucher-type flag (NOT F11/F12): when <b>Yes</b> on a Purchase type, the additional-cost
    /// entry area appears and any entry line posting to an additional-cost ledger (one with a non-null
    /// <see cref="Ledger.MethodOfAppropriation"/>) is apportioned across the item lines to raise their landed
    /// stock rate. Defaults to <c>false</c>, so every existing Purchase is byte-identical (ER-13) — with the flag
    /// off, a Direct-Expenses freight line stays purely P&amp;L and touches no stock rate (RQ-19).
    /// </summary>
    public bool TrackAdditionalCosts { get; set; }

    /// <summary>
    /// "<b>Allow zero-valued transactions</b>" (Book pp.142–143; catalog §11; Phase 6 slice 4 RQ-21). A
    /// voucher-type flag (set per Sales and per Purchase type separately): when <b>Yes</b>, an item line entered
    /// as <i>free</i> — <see cref="VoucherInventoryLine.Rate"/> / <see cref="VoucherInventoryLine.Value"/> = ₹0 —
    /// is accepted, so the entry moves stock (Actual qty) but posts ₹0 to the accounting books and ₹0 to GST.
    /// Defaults to <c>false</c>, so a normal Sales/Purchase still rejects a fat-finger ₹0 line (ER-13). Only valid
    /// on a Purchase or Sales base type — <c>VoucherValidator</c> rejects it on any other base (a Journal /
    /// Stock-Journal can never carry it, RQ-21). A rate-less Stock-Journal transfer is an ordinary transfer, NOT
    /// this feature.
    /// </summary>
    public bool AllowZeroValuedTransactions { get; set; }

    /// <summary>
    /// "<b>Use for POS invoicing</b>" (catalog §11; Phase 6 slice 7 RQ-38). A user-created <b>Sales</b> voucher type
    /// may set this <b>Yes</b> to behave as a Point-of-Sale (retail-till) invoice: the single customer debit is
    /// replaced by a split of tender debits (<see cref="Voucher.PosTenders"/>), while the credit side (Cr Sales +
    /// Cr Output GST) and the item-invoice stock movement are byte-identical to a normal sale — so GST reuses the
    /// Phase-4 engine unchanged. Defaults to <c>false</c>, so every existing Sales type is byte-identical (ER-13).
    /// Only meaningful on a Sales base type. The retail-till configuration (default godown/party, print-after-save,
    /// title, messages, declaration, tender-ledger defaults) lives in the optional <see cref="PosConfig"/>.
    /// </summary>
    public bool UseForPos { get; set; }

    /// <summary>The POS configuration (RQ-38; DP-4) — non-null only on a <see cref="UseForPos"/> Sales type. Holds
    /// the retail-till defaults and the optional POS Voucher Class tender-ledger pre-map. <c>null</c> for every
    /// non-POS type (byte-identical, ER-13).</summary>
    public PosConfig? PosConfig { get; set; }

    /// <summary>True iff this is a POS Sales type — a Sales base type with <see cref="UseForPos"/> on (RQ-38).</summary>
    public bool IsPosSales => BaseType == VoucherBaseType.Sales && UseForPos;

    /// <summary>
    /// "<b>Use for Job Work = Yes</b>" (Book1 pp.88, 95; catalog §10; Phase 6 slice 8; RQ-45/RQ-48). Carried on the
    /// seeded <b>Material In</b> and <b>Material Out</b> voucher types and driven <b>on</b> by the F11 "Enable Job
    /// Order Processing" toggle (RQ-45). Defaults to <c>false</c>, so an existing type is byte-identical (ER-13).
    /// </summary>
    public bool UseForJobWork { get; set; }

    /// <summary>
    /// "<b>Allow Consumption = Yes</b>" (Book1 p.88; catalog §10; Phase 6 slice 8; RQ-48/RQ-49). Carried on the
    /// seeded <b>Material In</b> voucher type and driven <b>on</b> by the F11 "Enable Job Order Processing" toggle.
    /// When on, a Material In is a <i>transform</i> (consume the third-party components, produce the finished good)
    /// and is EXEMPT from the balanced-transfer rule — exactly like a Manufacturing Journal (RQ-49). The
    /// no-negative-stock guard then blocks consuming raw material the third-party godown never received, so no
    /// phantom stock survives. Defaults to <c>false</c> (byte-identical, ER-13); only meaningful on a Material In.
    /// </summary>
    public bool AllowConsumption { get; set; }

    /// <summary>True iff this is a Material In carrying <see cref="AllowConsumption"/> — a consume-on-receipt
    /// transform, balance-exempt like a Manufacturing Journal (RQ-49).</summary>
    public bool IsConsumingMaterialIn =>
        BaseType == VoucherBaseType.MaterialIn && AllowConsumption;

    /// <summary>
    /// "<b>Use for Statutory Payment (Stat Payment)</b>" (catalog §13; Phase 7 slice 3). A <b>Payment</b> voucher
    /// type flagged to book a statutory-tax deposit — a TDS/TCS/GST payment against the "TDS Payable" (etc.) liability
    /// ledger (Dr TDS Payable / Cr Bank), the Tally "Ctrl+F Stat Payment" mode. It reuses the Payment
    /// <see cref="BaseType"/> unchanged (no new <see cref="VoucherBaseType"/>) — only this flag marks it — so
    /// <c>GstReportSupport.DirectionOf</c> and every exhaustive base-type switch are untouched. Defaults to
    /// <c>false</c>, so every existing Payment type is byte-identical (ER-13). Only meaningful on a Payment base.
    /// </summary>
    public bool IsStatPayment { get; set; }

    /// <summary>True iff this is a Stat-Payment type — a Payment base type with <see cref="IsStatPayment"/> on.</summary>
    public bool IsStatPaymentType => BaseType == VoucherBaseType.Payment && IsStatPayment;

    /// <summary>
    /// "<b>Use for RCM Payment Voucher (Rule 52)</b>" (Phase 9 slice 2; catalog §12; RQ-8). A <b>Payment</b> voucher type
    /// flagged to book a reverse-charge supplier payment (Dr Party / Cr Bank) that also generates the Rule-52 payment
    /// voucher document. It reuses the Payment <see cref="BaseType"/> unchanged (mirror <see cref="IsStatPayment"/>, no
    /// new <see cref="VoucherBaseType"/>) — so <c>GstReportSupport.DirectionOf</c> and every exhaustive base-type switch
    /// are untouched. Defaults to <c>false</c>, so every existing Payment type is byte-identical (ER-13). Only meaningful
    /// on a Payment base.
    /// </summary>
    public bool IsRcmPaymentVoucher { get; set; }

    /// <summary>True iff this is an RCM Payment-Voucher type — a Payment base type with <see cref="IsRcmPaymentVoucher"/> on.</summary>
    public bool IsRcmPaymentVoucherType => BaseType == VoucherBaseType.Payment && IsRcmPaymentVoucher;

    /// <summary>
    /// "<b>Use for GST Statutory Adjustment (Alt+J)</b>" (Phase 9 slice 7; catalog §12; RQ-21/RQ-27). A <b>Journal</b>
    /// voucher type flagged to book a Rule-88A ITC set-off (Dr Output {head} / Cr Input {head}) or an ITC reversal
    /// (Dr reversal-cost / Cr Input {head}) — the Tally "Alt+J Stat Adjustment" mode. It reuses the Journal
    /// <see cref="BaseType"/> unchanged (mirror <see cref="IsStatPayment"/>, no new <see cref="VoucherBaseType"/>) — so
    /// <c>GstReportSupport.DirectionOf</c> (Journal ⇒ null direction) keeps these adjustment vouchers OUT of the Table
    /// 3.1 / 4(A) sums, which is exactly why an S7 posting cannot corrupt the existing GSTR-3B figures. Defaults to
    /// <c>false</c>, so every existing Journal type is byte-identical (ER-13). Only meaningful on a Journal base.
    /// </summary>
    public bool IsGstStatAdjustment { get; set; }

    /// <summary>True iff this is a GST Stat-Adjustment type — a Journal base type with <see cref="IsGstStatAdjustment"/> on.</summary>
    public bool IsGstStatAdjustmentType => BaseType == VoucherBaseType.Journal && IsGstStatAdjustment;

    /// <summary>
    /// "<b>Prevent Duplicates</b>" (numbering-design-v2 §7; catalog §4). When <b>Yes</b>, posting/importing a voucher
    /// whose fully-rendered number (<see cref="Services.VoucherNumberFormatter.Render"/>) collides with an existing
    /// non-deleted voucher of this type is rejected. Defaults to <c>false</c>, so an existing type is byte-identical
    /// (ER-13). Not yet enforced by any post path in slice S1 (the flag is carried; enforcement lands in a later slice).
    /// </summary>
    public bool PreventDuplicate { get; set; }

    /// <summary>
    /// "<b>Width of numerical part</b>" (numbering-design-v2 §1.3, §2.1; catalog §4). The minimum width the numeric
    /// core is left-padded to; <c>0</c> means <b>no left-pad</b> (today's behaviour). The pad NEVER truncates a number
    /// wider than this. Defaults to <c>0</c>, so an existing type renders the bare <c>int</c> (ER-13).
    /// </summary>
    public int NumberWidth { get; set; }

    /// <summary>
    /// "<b>Prefill with zero</b>" (numbering-design-v2 §1.3, §2.1; catalog §4). Only meaningful when
    /// <see cref="NumberWidth"/> &gt; 0: the pad character is <c>'0'</c> when <b>Yes</b> (e.g. <c>007</c>), otherwise a
    /// space (e.g. <c>"  7"</c>). Defaults to <c>false</c> (ER-13).
    /// </summary>
    public bool PrefillWithZero { get; set; }

    /// <summary>
    /// The date-effective <b>Prefix</b> rows (numbering-design-v2 §1.2, §1.3). Get-only and ctor-injected (mirrors
    /// <see cref="Voucher.InventoryLines"/>/<see cref="Voucher.PosTenders"/>): the rendered number selects the row whose
    /// <see cref="VoucherNumberAffix.ApplicableFrom"/> is the latest on/before the voucher date. May be empty (ER-13:
    /// a type with no prefix rows renders no prefix).
    /// </summary>
    public IReadOnlyList<VoucherNumberAffix> Prefixes => _prefixes;

    /// <summary>
    /// The date-effective <b>Suffix</b> rows (numbering-design-v2 §1.2, §1.3). Same shape and selection rule as
    /// <see cref="Prefixes"/>; get-only and ctor-injected. May be empty (ER-13).
    /// </summary>
    public IReadOnlyList<VoucherNumberAffix> Suffixes => _suffixes;

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
        bool useAsManufacturingJournal = false,
        bool trackAdditionalCosts = false,
        bool allowZeroValuedTransactions = false,
        bool useForPos = false,
        PosConfig? posConfig = null,
        bool useForJobWork = false,
        bool allowConsumption = false,
        bool isStatPayment = false,
        bool isRcmPaymentVoucher = false,
        bool isGstStatAdjustment = false,
        bool preventDuplicate = false,
        int numberWidth = 0,
        bool prefillWithZero = false,
        IEnumerable<VoucherNumberAffix>? prefixes = null,
        IEnumerable<VoucherNumberAffix>? suffixes = null)
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
        TrackAdditionalCosts = trackAdditionalCosts;
        AllowZeroValuedTransactions = allowZeroValuedTransactions;
        UseForPos = useForPos;
        PosConfig = posConfig;
        UseForJobWork = useForJobWork;
        AllowConsumption = allowConsumption;
        IsStatPayment = isStatPayment;
        IsRcmPaymentVoucher = isRcmPaymentVoucher;
        IsGstStatAdjustment = isGstStatAdjustment;
        PreventDuplicate = preventDuplicate;
        NumberWidth = numberWidth;
        PrefillWithZero = prefillWithZero;
        _prefixes = prefixes?.ToList() ?? new List<VoucherNumberAffix>();
        _suffixes = suffixes?.ToList() ?? new List<VoucherNumberAffix>();
    }
}
