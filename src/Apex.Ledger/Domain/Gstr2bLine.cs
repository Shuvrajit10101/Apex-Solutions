namespace Apex.Ledger.Domain;

/// <summary>
/// One imported inward-supply record from a portal GSTR-2B/2A statement (Phase 9 slice 6; RQ-12; ER-6) — a child of the
/// immutable <see cref="Gstr2bSnapshot"/>. This is <b>external data</b> the taxpayer downloaded from the portal, NOT one
/// of the app's own postings: it exists precisely to be reconciled <i>against</i> the books, and it is <b>never edited</b>
/// (no mutator; a re-import creates a fresh snapshot). Money is <b>integer paisa</b> at every boundary (NFR-3).
/// <para>
/// The IMS decision (<c>ImsAction</c>, S6b) and the reconciliation result (<see cref="Gstr2bReconResult"/>) are kept as
/// separate records keyed to this line's <see cref="Id"/> — never a field on the line — so the imported statement stays
/// immutable (ER-6).
/// </para>
/// </summary>
/// <remarks>Framework- and DB-agnostic; a pure value object with a stable identity for the recon/IMS foreign keys.</remarks>
public sealed class Gstr2bLine
{
    /// <summary>Stable surrogate key (the recon/IMS records reference this).</summary>
    public Guid Id { get; }

    /// <summary>The supplier's GSTIN as reported on the portal (upper-cased/trimmed at the match boundary).</summary>
    public string SupplierGstin { get; }

    /// <summary>The supplier's trade name, or <c>null</c> when the portal omits it.</summary>
    public string? SupplierTradeName { get; }

    /// <summary>The document class (B2B / CDN / import / ISD, incl. amendments).</summary>
    public Gstr2bDocType DocType { get; }

    /// <summary>The supplier's document number as reported (verbatim, for display).</summary>
    public string DocNumber { get; }

    /// <summary>The <b>normalised</b> document number used as a match key (upper-cased, non-alphanumerics stripped, leading
    /// zeros trimmed); <c>null</c> when the portal reported no document number.</summary>
    public string? DocNumberNorm { get; }

    /// <summary>The supplier's document date.</summary>
    public DateOnly DocDate { get; }

    /// <summary>The place-of-supply 2-digit state code, or <c>null</c>.</summary>
    public string? PosStateCode { get; }

    /// <summary>The taxable value of the supply, in paisa.</summary>
    public long TaxableValuePaisa { get; }

    /// <summary>IGST, in paisa.</summary>
    public long IgstPaisa { get; }

    /// <summary>CGST, in paisa.</summary>
    public long CgstPaisa { get; }

    /// <summary>SGST/UTGST, in paisa.</summary>
    public long SgstPaisa { get; }

    /// <summary>Compensation Cess, in paisa (FY2025-26 tobacco 2Bs still carry cess — §2.7).</summary>
    public long CessPaisa { get; }

    /// <summary>The portal's "ITC Available" (Y/N) bifurcation — the statutory §16(2)(aa) gate lives in the portal's 2B.</summary>
    public bool ItcAvailable { get; }

    /// <summary>The portal's reason code when ITC is not available (POS/time-bar/etc.), or <c>null</c>.</summary>
    public string? ItcUnavailableReason { get; }

    /// <summary>The supplier-flagged reverse-charge indicator (an RCM inward supply bypasses IMS, §2.7).</summary>
    public bool ReverseCharge { get; }

    /// <summary>The total tax on this line (IGST + CGST + SGST + Cess), in paisa — the books-vs-portal match compares this.</summary>
    public long TotalTaxPaisa => IgstPaisa + CgstPaisa + SgstPaisa + CessPaisa;

    public Gstr2bLine(
        Guid id, string supplierGstin, string? supplierTradeName, Gstr2bDocType docType, string docNumber,
        string? docNumberNorm, DateOnly docDate, string? posStateCode, long taxableValuePaisa, long igstPaisa,
        long cgstPaisa, long sgstPaisa, long cessPaisa, bool itcAvailable, string? itcUnavailableReason,
        bool reverseCharge)
    {
        if (string.IsNullOrWhiteSpace(supplierGstin))
            throw new ArgumentException("A GSTR-2B line requires a supplier GSTIN.", nameof(supplierGstin));

        Id = id;
        SupplierGstin = supplierGstin;
        SupplierTradeName = supplierTradeName;
        DocType = docType;
        DocNumber = docNumber ?? string.Empty;
        DocNumberNorm = string.IsNullOrEmpty(docNumberNorm) ? null : docNumberNorm;
        DocDate = docDate;
        PosStateCode = posStateCode;
        TaxableValuePaisa = taxableValuePaisa;
        IgstPaisa = igstPaisa;
        CgstPaisa = cgstPaisa;
        SgstPaisa = sgstPaisa;
        CessPaisa = cessPaisa;
        ItcAvailable = itcAvailable;
        ItcUnavailableReason = itcUnavailableReason;
        ReverseCharge = reverseCharge;
    }
}
