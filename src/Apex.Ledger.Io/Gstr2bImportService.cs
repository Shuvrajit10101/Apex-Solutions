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
    public static Gstr2bSnapshot Materialize(Gstr2bStatementDto dto, DateTimeOffset importedAt, Func<Guid>? idFactory = null)
    {
        ArgumentNullException.ThrowIfNull(dto);
        var newId = idFactory ?? Guid.NewGuid;

        var lines = dto.Lines.Select(l => new Gstr2bLine(
            newId(), l.SupplierGstin, l.SupplierTradeName, l.DocType, l.DocNumber, l.DocNumberNorm, l.DocDate,
            l.PosStateCode, l.TaxableValuePaisa, l.IgstPaisa, l.CgstPaisa, l.SgstPaisa, l.CessPaisa, l.ItcAvailable,
            l.ItcUnavailableReason, l.ReverseCharge)).ToList();

        return new Gstr2bSnapshot(
            newId(), dto.StatementType, dto.ReturnPeriod, dto.RecipientGstin, dto.GeneratedOn, dto.SourceFileHash,
            importedAt, dto.SummaryIgstPaisa, dto.SummaryCgstPaisa, dto.SummarySgstPaisa, dto.SummaryCessPaisa, lines);
    }

    /// <summary>Materialises + stages the parsed statement on the company (Phase 9 slice 6). Adds to the immutable
    /// snapshot collection only — no ledger mutation (ER-14). Returns the staged snapshot.</summary>
    public static Gstr2bSnapshot Import(Company company, Gstr2bStatementDto dto, DateTimeOffset importedAt, Func<Guid>? idFactory = null)
    {
        ArgumentNullException.ThrowIfNull(company);
        var snapshot = Materialize(dto, importedAt, idFactory);
        company.AddGstr2bSnapshot(snapshot);
        return snapshot;
    }
}
