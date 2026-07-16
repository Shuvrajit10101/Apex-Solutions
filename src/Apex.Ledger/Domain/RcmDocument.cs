namespace Apex.Ledger.Domain;

/// <summary>
/// An <b>RCM generated document</b> (Phase 9 slice 2; RQ-8) — a Rule-47A self-invoice or a Rule-52 payment voucher the
/// recipient raises for a reverse-charge inward supply. It is a voucher-linked value-object-with-identity (mirrors
/// <see cref="TdsChallan"/>): the actual accounting is the single Purchase voucher that books the dual leg, and this
/// record only captures the generated document's own consecutive number series + date (and, for a self-invoice, the
/// Rule-47A ≤ 30-day constraint), linked back to the source voucher. A registered §9(3) supplier issues its own tax
/// invoice, so <b>no</b> self-invoice is generated (only unregistered-supplier RCM triggers one, RQ-8).
/// </summary>
/// <remarks>Immutable value object with identity; framework-, DB- and clock-free.</remarks>
public sealed class RcmDocument
{
    /// <summary>Stable surrogate key.</summary>
    public Guid Id { get; }

    /// <summary>Self-invoice (Rule 47A) or payment voucher (Rule 52).</summary>
    public RcmDocumentKind Kind { get; }

    /// <summary>The source accounting voucher this document was generated for (the RCM Purchase / supplier Payment).</summary>
    public Guid SourceVoucherId { get; }

    /// <summary>The consecutive per-company self-invoice / payment-voucher series number (Rule 47A/52).</summary>
    public int SeriesNumber { get; }

    /// <summary>The document date (a self-invoice date must be ≤ the receipt date + 30 days, Rule 47A).</summary>
    public DateOnly DocDate { get; }

    /// <summary>The supplier ledger the document was raised against; <c>null</c> when not applicable.</summary>
    public Guid? SupplierLedgerId { get; }

    public RcmDocument(
        Guid id, RcmDocumentKind kind, Guid sourceVoucherId, int seriesNumber, DateOnly docDate,
        Guid? supplierLedgerId = null)
    {
        if (seriesNumber < 1)
            throw new ArgumentException("RCM document series number must be ≥ 1.", nameof(seriesNumber));

        Id = id;
        Kind = kind;
        SourceVoucherId = sourceVoucherId;
        SeriesNumber = seriesNumber;
        DocDate = docDate;
        SupplierLedgerId = supplierLedgerId;
    }
}
