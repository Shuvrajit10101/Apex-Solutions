namespace Apex.Ledger.Domain;

/// <summary>
/// A <b>GST-on-advance receipt</b> record (Phase 9; RQ-25; Rule 50/51) — the source of truth for GSTR-1 Table 11A
/// (advance tax received) and 11B (advance adjusted). A voucher-linked value-object-with-identity (mirrors
/// <see cref="TdsChallan"/>): it captures whether the advance is for a <see cref="IsService"/> supply (goods advances
/// are de-taxed by Notn 66/2017), the net advance, the rate/POS, the advance tax, and the later adjustment / refund
/// voucher links.
/// <para>
/// <b>The advance table lands in the S2a v39 schema but stays empty/unused until S2b</b> (the advance engine, suspense
/// posting, adjustment and 11A/11B projections are built there). Carrying the record + collection now keeps the migration
/// a single v38→v39 bump; an S2a company never creates an advance receipt, so it is byte-identical when off (ER-13).
/// </para>
/// </summary>
/// <remarks>Immutable value object with identity; framework-, DB- and clock-free. Amounts are paisa-exact.</remarks>
public sealed class GstAdvanceReceipt
{
    /// <summary>Stable surrogate key.</summary>
    public Guid Id { get; }

    /// <summary>The receipt voucher that booked the advance.</summary>
    public Guid ReceiptVoucherId { get; }

    /// <summary>True for a service advance (taxed on receipt); false for a goods advance (de-taxed, Notn 66/2017).</summary>
    public bool IsService { get; }

    /// <summary>The net (ex-tax) advance for a service, or the gross advance for a goods receipt. Paisa-exact.</summary>
    public Money AdvanceAmount { get; }

    /// <summary>The integrated rate in basis points the advance tax was computed at (0 for a goods advance).</summary>
    public int RateBasisPoints { get; }

    /// <summary>True iff the advance is an inter-state supply (IGST); false ⇒ intra (CGST+SGST).</summary>
    public bool InterState { get; }

    /// <summary>The place-of-supply state code; <c>null</c> when unset.</summary>
    public string? PlaceOfSupplyStateCode { get; }

    /// <summary>The advance tax computed on receipt (0 for a goods advance). Paisa-exact.</summary>
    public Money AdvanceTax { get; }

    /// <summary>The invoice voucher this advance was later adjusted against (→ Table 11B); <c>null</c> until adjusted.</summary>
    public Guid? AdjustedAgainstInvoiceVoucherId { get; }

    /// <summary>The Rule-51 refund voucher that returned the advance; <c>null</c> unless refunded.</summary>
    public Guid? RefundVoucherId { get; }

    public GstAdvanceReceipt(
        Guid id, Guid receiptVoucherId, bool isService, Money advanceAmount, int rateBasisPoints, bool interState,
        string? placeOfSupplyStateCode, Money advanceTax, Guid? adjustedAgainstInvoiceVoucherId = null,
        Guid? refundVoucherId = null)
    {
        if (advanceAmount.Amount < 0m)
            throw new ArgumentException("Advance amount must be ≥ 0.", nameof(advanceAmount));
        if (!advanceAmount.IsPaisaExact)
            throw new InvalidOperationException($"Advance amount {advanceAmount} must be paisa-exact.");
        if (rateBasisPoints < 0)
            throw new ArgumentException("Advance rate basis points must be ≥ 0.", nameof(rateBasisPoints));
        if (advanceTax.Amount < 0m)
            throw new ArgumentException("Advance tax must be ≥ 0.", nameof(advanceTax));
        if (!advanceTax.IsPaisaExact)
            throw new InvalidOperationException($"Advance tax {advanceTax} must be paisa-exact.");
        // Services-only invariant (Notn 66/2017): a goods advance bears no advance tax (ER-12-style fail-fast).
        if (!isService && advanceTax.Amount != 0m)
            throw new InvalidOperationException(
                "A goods advance is de-taxed (Notn 66/2017) and must carry zero advance tax.");

        Id = id;
        ReceiptVoucherId = receiptVoucherId;
        IsService = isService;
        AdvanceAmount = advanceAmount;
        RateBasisPoints = rateBasisPoints;
        InterState = interState;
        PlaceOfSupplyStateCode = placeOfSupplyStateCode;
        AdvanceTax = advanceTax;
        AdjustedAgainstInvoiceVoucherId = adjustedAgainstInvoiceVoucherId;
        RefundVoucherId = refundVoucherId;
    }
}
