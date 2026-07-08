using Apex.Ledger.Domain;
using Apex.Ledger.Reports;

namespace Apex.Ledger.Persistence;

/// <summary>
/// Persistence ports (design §2). These are implemented by a separate SQLite adapter
/// project — the domain library depends only on the abstractions, never on a DB.
/// Phase 1 keeps them minimal; they exist to fix the seam so the shell can swap stores.
/// </summary>
public interface ICompanyRepository
{
    Company? Load(Guid companyId);
    void Save(Company company);
}

/// <summary>Read/write access to a company's posted vouchers.</summary>
public interface IVoucherRepository
{
    IReadOnlyList<Voucher> GetAll(Guid companyId);
    void Add(Guid companyId, Voucher voucher);
    void Remove(Guid companyId, Guid voucherId);
}

/// <summary>Read/write access to a company's masters (groups, ledgers, voucher types).</summary>
public interface IMasterRepository
{
    IReadOnlyList<Group> GetGroups(Guid companyId);
    IReadOnlyList<Domain.Ledger> GetLedgers(Guid companyId);
    IReadOnlyList<VoucherType> GetVoucherTypes(Guid companyId);
}

/// <summary>
/// One persisted saved report view (RQ-8): its per-company <see cref="Name"/> plus the config-only
/// <see cref="View"/>. The name keys the view within a company (unique, case-insensitive upsert). This carries
/// no computed figure — the report is always recomputed when the view is applied (ER-9).
/// </summary>
public sealed record SavedReportViewEntry(string Name, SavedReportView View);

/// <summary>
/// Read/write access to a company's <b>saved report views</b> (RQ-8 Save View, ER-9, DP-7). A view is stored
/// per company as a name + a config-only <see cref="SavedReportView"/> (no figures — always recomputed on
/// apply). <see cref="Save"/> is an upsert by (company, name); listing/getting/deleting are scoped to a single
/// company, so a view saved in one company is never visible in another.
/// </summary>
public interface ISavedReportViewRepository
{
    /// <summary>Upserts <paramref name="view"/> under <paramref name="name"/> for <paramref name="companyId"/>:
    /// creates it, or overwrites the existing view of that name (case-insensitive match).</summary>
    void Save(Guid companyId, string name, SavedReportView view);

    /// <summary>Lists the company's saved views, ordered by name (culture-invariant, case-insensitive).</summary>
    IReadOnlyList<SavedReportViewEntry> List(Guid companyId);

    /// <summary>Gets the company's saved view of <paramref name="name"/> (case-insensitive), or <c>null</c>.</summary>
    SavedReportView? Get(Guid companyId, string name);

    /// <summary>Deletes the company's saved view of <paramref name="name"/> (case-insensitive). No-op if absent.</summary>
    void Delete(Guid companyId, string name);
}
