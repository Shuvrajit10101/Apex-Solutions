namespace Apex.Ledger.Domain;

/// <summary>
/// The tenant/dataset boundary; owns all masters and vouchers (catalog §2; plan.md §4.1).
/// Framework- and DB-agnostic: the domain object carries the in-memory posted set;
/// persistence is a separate adapter concern.
/// </summary>
public sealed class Company
{
    private readonly List<Group> _groups = new();
    private readonly List<Ledger> _ledgers = new();
    private readonly List<VoucherType> _voucherTypes = new();
    private readonly List<Voucher> _vouchers = new();
    private readonly List<CostCategory> _costCategories = new();
    private readonly List<CostCentre> _costCentres = new();

    /// <summary>Stable surrogate key.</summary>
    public Guid Id { get; }

    /// <summary>Company name; required, non-empty.</summary>
    public string Name { get; set; }

    /// <summary>Defaults to <see cref="Name"/>, editable.</summary>
    public string MailingName { get; set; }

    public string? Address { get; set; }
    public string Country { get; set; } = "India";
    public string? State { get; set; }
    public string? Pin { get; set; }

    /// <summary>Default 1-Apr of the working year.</summary>
    public DateOnly FinancialYearStart { get; set; }

    /// <summary>≥ <see cref="FinancialYearStart"/>; mid-year start allowed.</summary>
    public DateOnly BooksBeginFrom { get; set; }

    public string BaseCurrencySymbol { get; set; } = "₹";
    public string BaseCurrencyName { get; set; } = "INR";
    public int DecimalPlaces { get; set; } = 2;
    public string DecimalUnitName { get; set; } = "Paisa";

    /// <summary>Default cost category seeded on create (catalog §6/§22); unused by Phase-1 reports.</summary>
    public string PrimaryCostCategoryName { get; set; } = "Primary Cost Category";

    /// <summary>Default godown seeded on create (catalog §9/§22); unused by Phase-1 reports.</summary>
    public string MainLocationName { get; set; } = "Main Location";

    /// <summary>
    /// The reserved Profit &amp; Loss head that the "Profit &amp; Loss A/c" ledger sits under
    /// (verification §A8). It is a reserved head, <b>not</b> one of the 28 groups, so it is
    /// stored separately and excluded from <see cref="Groups"/>. Its Balance-Sheet line is
    /// computed (brought-forward P&amp;L + current net profit), never entered.
    /// </summary>
    public Group? ProfitAndLossHead { get; private set; }

    /// <summary>Registers the reserved P&amp;L head (kept out of the 28-count).</summary>
    public void SetProfitAndLossHead(Group head) => ProfitAndLossHead = head;

    public IReadOnlyList<Group> Groups => _groups;
    public IReadOnlyList<Ledger> Ledgers => _ledgers;
    public IReadOnlyList<VoucherType> VoucherTypes => _voucherTypes;
    public IReadOnlyList<Voucher> Vouchers => _vouchers;

    /// <summary>Cost categories (catalog §6); includes the seeded "Primary Cost Category".</summary>
    public IReadOnlyList<CostCategory> CostCategories => _costCategories;

    /// <summary>Cost centres (catalog §6), hierarchical within their category.</summary>
    public IReadOnlyList<CostCentre> CostCentres => _costCentres;

    public Company(Guid id, string name, DateOnly financialYearStart, DateOnly booksBeginFrom)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Company name is required.", nameof(name));
        if (booksBeginFrom < financialYearStart)
            throw new ArgumentException("BooksBeginFrom must be ≥ FinancialYearStart.", nameof(booksBeginFrom));

        Id = id;
        Name = name;
        MailingName = name;
        FinancialYearStart = financialYearStart;
        BooksBeginFrom = booksBeginFrom;
    }

    // ---- Master mutation (used by the seed + factory; kept internal-friendly via public adders) ----

    public void AddGroup(Group group) => _groups.Add(group);
    public void AddLedger(Ledger ledger) => _ledgers.Add(ledger);
    public void AddVoucherType(VoucherType type) => _voucherTypes.Add(type);
    public void AddCostCategory(CostCategory category) => _costCategories.Add(category);
    public void AddCostCentre(CostCentre centre) => _costCentres.Add(centre);
    internal void AddVoucherInternal(Voucher voucher) => _vouchers.Add(voucher);
    internal bool RemoveVoucherInternal(Voucher voucher) => _vouchers.Remove(voucher);

    // ---- Lookups ----

    public Group? FindGroup(Guid id) =>
        _groups.FirstOrDefault(g => g.Id == id)
        ?? (ProfitAndLossHead is not null && ProfitAndLossHead.Id == id ? ProfitAndLossHead : null);
    public Ledger? FindLedger(Guid id) => _ledgers.FirstOrDefault(l => l.Id == id);
    public VoucherType? FindVoucherType(Guid id) => _voucherTypes.FirstOrDefault(t => t.Id == id);
    public Voucher? FindVoucher(Guid id) => _vouchers.FirstOrDefault(v => v.Id == id);
    public CostCategory? FindCostCategory(Guid id) => _costCategories.FirstOrDefault(c => c.Id == id);
    public CostCentre? FindCostCentre(Guid id) => _costCentres.FirstOrDefault(c => c.Id == id);

    public Group? FindGroupByName(string name) =>
        _groups.FirstOrDefault(g =>
            string.Equals(g.Name, name, StringComparison.OrdinalIgnoreCase) ||
            (g.Alias is not null && string.Equals(g.Alias, name, StringComparison.OrdinalIgnoreCase)));

    public Ledger? FindLedgerByName(string name) =>
        _ledgers.FirstOrDefault(l =>
            string.Equals(l.Name, name, StringComparison.OrdinalIgnoreCase) ||
            (l.Alias is not null && string.Equals(l.Alias, name, StringComparison.OrdinalIgnoreCase)));

    public VoucherType? FindVoucherTypeByName(string name) =>
        _voucherTypes.FirstOrDefault(t =>
            string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase) ||
            (t.Abbreviation is not null && string.Equals(t.Abbreviation, name, StringComparison.OrdinalIgnoreCase)));

    public CostCategory? FindCostCategoryByName(string name) =>
        _costCategories.FirstOrDefault(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase));

    public CostCentre? FindCostCentreByName(string name) =>
        _costCentres.FirstOrDefault(c =>
            string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase) ||
            (c.Alias is not null && string.Equals(c.Alias, name, StringComparison.OrdinalIgnoreCase)));
}
