using Apex.Ledger.Domain;

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
