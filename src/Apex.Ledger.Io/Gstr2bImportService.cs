using Apex.Ledger.Domain;

namespace Apex.Ledger.Io;

/// <summary>
/// Materialises a deterministically-parsed <see cref="Gstr2bStatementDto"/> (from a connector's
/// <see cref="IGstPortalConnector.FetchStatement"/>) into an immutable domain <see cref="Gstr2bSnapshot"/> and stages it
/// on the company (Phase 9 slice 6; RQ-12). The parse stays clock-/id-free (deterministic golden); this step assigns the
/// snapshot + line ids and the caller-supplied <c>importedAt</c>.
/// <para>
/// <b>ADVISORY only (ER-14):</b> this service takes a <see cref="Company"/> and adds to its <b>staging</b> collection —
/// it holds no <c>LedgerService</c>, writes no <c>EntryLine</c>, and posts nothing. The imported statement is external
/// data physically separate from the ledger.
/// </para>
/// </summary>
public static class Gstr2bImportService
{
    /// <summary>Builds an immutable <see cref="Gstr2bSnapshot"/> (with fresh ids + the supplied import instant) from a
    /// parsed statement DTO. Pure — mutates nothing (the caller stages it).</summary>
    /// <param name="gstRegistrationId">🔴 <b>v66 — WHICH company registration the statement is being imported FOR</b>
    /// (census 6.23; gap register <b>T2-121(ii)</b>). <c>null</c> or <see cref="GstRegistration.PrimaryId"/> ⇒ the
    /// company's own first registration, which is what every pre-v66 import wrote because the import path hard-coded
    /// the primary's GSTIN and the parser refused any other.
    /// <para><b>R7, opened BY CONTENT 2026-10-13.</b> The vendor's own <i>Import GST Returns</i> screen carries this
    /// field: <i>"GST Registration: It will be selected by default. In case of multiple GST registrations, select the
    /// required one."</i> (<c>help.tallysolutions.com/india-gst-status-gstr-2b-reconciliation-tally-2/</c>). So
    /// importing a branch's or an ISD's own 2B is ATTESTED, and refusing it would have been a divergence.</para></param>
    public static Gstr2bSnapshot Materialize(
        Gstr2bStatementDto dto, DateTimeOffset importedAt, Func<Guid>? idFactory = null,
        Guid? gstRegistrationId = null)
    {
        ArgumentNullException.ThrowIfNull(dto);
        var newId = idFactory ?? Guid.NewGuid;

        var lines = dto.Lines.Select(l => new Gstr2bLine(
            newId(), l.SupplierGstin, l.SupplierTradeName, l.DocType, l.DocNumber, l.DocNumberNorm, l.DocDate,
            l.PosStateCode, l.TaxableValuePaisa, l.IgstPaisa, l.CgstPaisa, l.SgstPaisa, l.CessPaisa, l.ItcAvailable,
            l.ItcUnavailableReason, l.ReverseCharge)).ToList();

        return new Gstr2bSnapshot(
            newId(), dto.StatementType, dto.ReturnPeriod, dto.RecipientGstin, dto.GeneratedOn, dto.SourceFileHash,
            importedAt, dto.SummaryIgstPaisa, dto.SummaryCgstPaisa, dto.SummarySgstPaisa, dto.SummaryCessPaisa, lines,
            // v66: NORMALISED to null for the primary, so the stored column means what its declaration says and
            // never carries the all-zero Guid (which the REFERENCES gst_registrations(id) clause would reject —
            // the primary registration deliberately has no row in that table).
            gstRegistrationId: gstRegistrationId is { } reg && reg != GstRegistration.PrimaryId ? reg : null);
    }

    /// <summary>Materialises + stages the parsed statement on the company (Phase 9 slice 6). Adds to the immutable
    /// snapshot collection only — no ledger mutation (ER-14). Returns the staged snapshot.</summary>
    /// <param name="gstRegistrationId">v66 — see <see cref="Materialize"/>.</param>
    public static Gstr2bSnapshot Import(
        Company company, Gstr2bStatementDto dto, DateTimeOffset importedAt, Func<Guid>? idFactory = null,
        Guid? gstRegistrationId = null)
    {
        ArgumentNullException.ThrowIfNull(company);
        var snapshot = Materialize(dto, importedAt, idFactory, gstRegistrationId);
        company.AddGstr2bSnapshot(snapshot);
        return snapshot;
    }
}
