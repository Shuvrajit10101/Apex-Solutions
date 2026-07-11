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
        bool isStatPayment = false)
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
    }
}
