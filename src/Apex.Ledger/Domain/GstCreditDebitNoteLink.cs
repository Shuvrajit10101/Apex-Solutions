namespace Apex.Ledger.Domain;

/// <summary>
/// A <b>§34 credit/debit-note link</b> (Phase 9; RQ-24; ER-12) — the first-class §34 GST metadata over an existing
/// Credit-Note / Debit-Note voucher: the link to the original invoice (or a consolidated-party reference), the §34(2)
/// FY basis (the original invoice date), the reason code and the GSTR-1 Table-9B target flag. It is a voucher-linked
/// value-object-with-identity (mirrors <see cref="TdsChallan"/>).
/// <para>
/// <b>The link table lands in the S2a v39 schema but stays empty/unused until S2b</b> (the CDN engine, §34(2) guard,
/// sign-aware projection and 9B feed are built there). Carrying the record + collection now keeps the migration a single
/// v38→v39 bump; an S2a company never creates a link, so it is byte-identical when off (ER-13).
/// </para>
/// </summary>
/// <remarks>Immutable value object with identity; framework-, DB- and clock-free. A note is <b>never</b> free-floating:
/// it carries either an original-invoice voucher link or a denormalised original-invoice reference (ER-12).</remarks>
public sealed class GstCreditDebitNoteLink
{
    /// <summary>Stable surrogate key.</summary>
    public Guid Id { get; }

    /// <summary>The credit/debit-note voucher this link annotates.</summary>
    public Guid CdnVoucherId { get; }

    /// <summary>Credit (reduces output tax) or Debit (increases output tax).</summary>
    public CdnType CdnType { get; }

    /// <summary>The linked original-invoice voucher; <c>null</c> ⇒ a consolidated-party reference (denormalised below).</summary>
    public Guid? OriginalInvoiceVoucherId { get; }

    /// <summary>The original invoice number, denormalised for Table 9B when the voucher link is unavailable.</summary>
    public string? OriginalInvoiceNumber { get; }

    /// <summary>The original invoice date — drives the §34(2) 30-Nov cut-off FY basis.</summary>
    public DateOnly? OriginalInvoiceDate { get; }

    /// <summary>The §34 reason code (e.g. "01 sales return", "02 post-supply discount"); required.</summary>
    public string ReasonCode { get; }

    /// <summary>Whether this note targets GSTR-1 Table 9B (a registered-party CDN) vs an unregistered CDN.</summary>
    public bool Is9BTarget { get; }

    public GstCreditDebitNoteLink(
        Guid id, Guid cdnVoucherId, CdnType cdnType, Guid? originalInvoiceVoucherId, string? originalInvoiceNumber,
        DateOnly? originalInvoiceDate, string reasonCode, bool is9BTarget = true)
    {
        if (string.IsNullOrWhiteSpace(reasonCode))
            throw new ArgumentException("CDN reason code is required.", nameof(reasonCode));
        if (originalInvoiceVoucherId is null && string.IsNullOrWhiteSpace(originalInvoiceNumber))
            throw new ArgumentException(
                "A §34 note must reference either an original-invoice voucher or a consolidated-party original-invoice number (ER-12).");

        Id = id;
        CdnVoucherId = cdnVoucherId;
        CdnType = cdnType;
        OriginalInvoiceVoucherId = originalInvoiceVoucherId;
        OriginalInvoiceNumber = originalInvoiceNumber?.Trim();
        OriginalInvoiceDate = originalInvoiceDate;
        ReasonCode = reasonCode.Trim();
        Is9BTarget = is9BTarget;
    }
}
