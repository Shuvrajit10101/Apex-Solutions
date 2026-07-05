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
    private readonly List<Budget> _budgets = new();
    private readonly List<Scenario> _scenarios = new();
    private readonly List<Currency> _currencies = new();
    private readonly List<ExchangeRate> _exchangeRates = new();
    private readonly List<StockGroup> _stockGroups = new();
    private readonly List<StockCategory> _stockCategories = new();
    private readonly List<Unit> _units = new();
    private readonly List<Godown> _godowns = new();
    private readonly List<StockItem> _stockItems = new();
    private readonly List<StockOpeningBalance> _stockOpeningBalances = new();
    private readonly List<InventoryVoucher> _inventoryVouchers = new();

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

    /// <summary>
    /// The company GST configuration (catalog §12; phase4 RQ-1/RQ-2). <c>null</c> (or a config with
    /// <see cref="GstConfig.Enabled"/> false) means GST is off — the default for every existing company, so
    /// the Phase-1/2/3 paths are byte-for-byte unchanged (ER-10). Set (and its tax ledgers auto-created) by
    /// <c>GstService.EnableGst</c>.
    /// </summary>
    public GstConfig? Gst { get; set; }

    /// <summary>True iff GST is enabled for this company.</summary>
    public bool GstEnabled => Gst is { Enabled: true };

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

    /// <summary>Budgets (catalog §7): named budget masters compared against actuals.</summary>
    public IReadOnlyList<Budget> Budgets => _budgets;

    /// <summary>Scenarios (catalog §7): what-if columns that surface provisional (Optional / Reversing /
    /// Memorandum) vouchers over the actuals.</summary>
    public IReadOnlyList<Scenario> Scenarios => _scenarios;

    /// <summary>Currencies (catalog §2/§20 Multi-currency): the base ₹/INR (seeded on create) plus any
    /// foreign currencies created for forex transactions.</summary>
    public IReadOnlyList<Currency> Currencies => _currencies;

    /// <summary>Rates of Exchange (catalog §2): dated base-per-foreign quotes for the foreign currencies.</summary>
    public IReadOnlyList<ExchangeRate> ExchangeRates => _exchangeRates;

    /// <summary>Stock groups (catalog §9): the inventory classification tree.</summary>
    public IReadOnlyList<StockGroup> StockGroups => _stockGroups;

    /// <summary>Stock categories (catalog §9): the independent stock-item classification axis.</summary>
    public IReadOnlyList<StockCategory> StockCategories => _stockCategories;

    /// <summary>Units of measure (catalog §9): simple + compound.</summary>
    public IReadOnlyList<Unit> Units => _units;

    /// <summary>Godowns / locations (catalog §9): includes the seeded "Main Location".</summary>
    public IReadOnlyList<Godown> Godowns => _godowns;

    /// <summary>Stock items (catalog §9): the things bought, sold and held.</summary>
    public IReadOnlyList<StockItem> StockItems => _stockItems;

    /// <summary>Opening-stock allocations (catalog §9): per item, per godown, per batch label.</summary>
    public IReadOnlyList<StockOpeningBalance> StockOpeningBalances => _stockOpeningBalances;

    /// <summary>Stock &amp; order vouchers (catalog §10): GRN/Delivery/Rejection/Stock-Journal/Physical/PO/SO.</summary>
    public IReadOnlyList<InventoryVoucher> InventoryVouchers => _inventoryVouchers;

    /// <summary>The seeded default godown ("Main Location"), or <c>null</c> if none is seeded yet.</summary>
    public Godown? MainLocation => _godowns.FirstOrDefault(g => g.IsMainLocation);

    /// <summary>The single base currency (₹/INR), or <c>null</c> if none has been seeded yet.</summary>
    public Currency? BaseCurrency => _currencies.FirstOrDefault(c => c.IsBaseCurrency);

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
    public void AddBudget(Budget budget) => _budgets.Add(budget);
    public void AddScenario(Scenario scenario) => _scenarios.Add(scenario);

    /// <summary>Adds a currency master. At most one currency may be the base (guarded).</summary>
    public void AddCurrency(Currency currency)
    {
        ArgumentNullException.ThrowIfNull(currency);
        if (currency.IsBaseCurrency && _currencies.Any(c => c.IsBaseCurrency))
            throw new InvalidOperationException("A base currency is already registered for this company.");
        _currencies.Add(currency);
    }

    /// <summary>Adds a dated exchange-rate quote for a foreign currency.</summary>
    public void AddExchangeRate(ExchangeRate rate) => _exchangeRates.Add(rate ?? throw new ArgumentNullException(nameof(rate)));

    public void AddStockGroup(StockGroup group) => _stockGroups.Add(group ?? throw new ArgumentNullException(nameof(group)));
    public void AddStockCategory(StockCategory category) => _stockCategories.Add(category ?? throw new ArgumentNullException(nameof(category)));
    public void AddUnit(Unit unit) => _units.Add(unit ?? throw new ArgumentNullException(nameof(unit)));
    public void AddGodown(Godown godown) => _godowns.Add(godown ?? throw new ArgumentNullException(nameof(godown)));
    public void AddStockItem(StockItem item) => _stockItems.Add(item ?? throw new ArgumentNullException(nameof(item)));
    public void AddStockOpeningBalance(StockOpeningBalance balance) => _stockOpeningBalances.Add(balance ?? throw new ArgumentNullException(nameof(balance)));

    /// <summary>Removes a stock opening-balance allocation (used when re-editing an item's opening stock).</summary>
    public bool RemoveStockOpeningBalance(StockOpeningBalance balance) => _stockOpeningBalances.Remove(balance);

    /// <summary>Adds a stock/order voucher (posting guards live in <c>InventoryPostingService</c>).</summary>
    internal void AddInventoryVoucherInternal(InventoryVoucher voucher) => _inventoryVouchers.Add(voucher ?? throw new ArgumentNullException(nameof(voucher)));

    /// <summary>Removes a stock/order voucher (delete guards live in <c>InventoryPostingService</c>).</summary>
    internal bool RemoveInventoryVoucherInternal(InventoryVoucher voucher) => _inventoryVouchers.Remove(voucher);

    /// <summary>Adds a rehydrated stock/order voucher on load (bypasses posting guards — the store is trusted).</summary>
    public void AddInventoryVoucher(InventoryVoucher voucher) => _inventoryVouchers.Add(voucher ?? throw new ArgumentNullException(nameof(voucher)));

    /// <summary>Removes a stock group (delete-guards live in <c>InventoryService</c>).</summary>
    public bool RemoveStockGroup(StockGroup group) => _stockGroups.Remove(group);
    /// <summary>Removes a stock category (delete-guards live in <c>InventoryService</c>).</summary>
    public bool RemoveStockCategory(StockCategory category) => _stockCategories.Remove(category);
    /// <summary>Removes a unit (delete-guards live in <c>InventoryService</c>).</summary>
    public bool RemoveUnit(Unit unit) => _units.Remove(unit);
    /// <summary>Removes a godown (delete-guards live in <c>InventoryService</c>).</summary>
    public bool RemoveGodown(Godown godown) => _godowns.Remove(godown);
    /// <summary>Removes a stock item (delete-guards live in <c>InventoryService</c>).</summary>
    public bool RemoveStockItem(StockItem item) => _stockItems.Remove(item);

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
    public Budget? FindBudget(Guid id) => _budgets.FirstOrDefault(b => b.Id == id);
    public Scenario? FindScenario(Guid id) => _scenarios.FirstOrDefault(s => s.Id == id);
    public Currency? FindCurrency(Guid id) => _currencies.FirstOrDefault(c => c.Id == id);
    public StockGroup? FindStockGroup(Guid id) => _stockGroups.FirstOrDefault(g => g.Id == id);
    public StockCategory? FindStockCategory(Guid id) => _stockCategories.FirstOrDefault(c => c.Id == id);
    public Unit? FindUnit(Guid id) => _units.FirstOrDefault(u => u.Id == id);
    public Godown? FindGodown(Guid id) => _godowns.FirstOrDefault(g => g.Id == id);
    public StockItem? FindStockItem(Guid id) => _stockItems.FirstOrDefault(i => i.Id == id);
    public StockOpeningBalance? FindStockOpeningBalance(Guid id) => _stockOpeningBalances.FirstOrDefault(b => b.Id == id);
    public InventoryVoucher? FindInventoryVoucher(Guid id) => _inventoryVouchers.FirstOrDefault(v => v.Id == id);

    /// <summary>
    /// The exchange rate in force for a foreign currency on <paramref name="asOf"/>: the latest-dated quote
    /// on or before that date, or <c>null</c> if the currency has no quote yet on/before the date.
    /// </summary>
    public ExchangeRate? RateInForce(Guid currencyId, DateOnly asOf)
    {
        ExchangeRate? best = null;
        foreach (var r in _exchangeRates)
        {
            if (r.CurrencyId != currencyId || r.Date > asOf) continue;
            if (best is null || r.Date > best.Date) best = r;
        }
        return best;
    }

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

    public Budget? FindBudgetByName(string name) =>
        _budgets.FirstOrDefault(b => string.Equals(b.Name, name, StringComparison.OrdinalIgnoreCase));

    public Scenario? FindScenarioByName(string name) =>
        _scenarios.FirstOrDefault(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>Finds a currency by its formal name or symbol (case-insensitive).</summary>
    public Currency? FindCurrencyByName(string name) =>
        _currencies.FirstOrDefault(c =>
            string.Equals(c.FormalName, name, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(c.Symbol, name, StringComparison.OrdinalIgnoreCase));

    public StockGroup? FindStockGroupByName(string name) =>
        _stockGroups.FirstOrDefault(g =>
            string.Equals(g.Name, name, StringComparison.OrdinalIgnoreCase) ||
            (g.Alias is not null && string.Equals(g.Alias, name, StringComparison.OrdinalIgnoreCase)));

    public StockCategory? FindStockCategoryByName(string name) =>
        _stockCategories.FirstOrDefault(c =>
            string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase) ||
            (c.Alias is not null && string.Equals(c.Alias, name, StringComparison.OrdinalIgnoreCase)));

    /// <summary>Finds a unit by its symbol or formal name (case-insensitive).</summary>
    public Unit? FindUnitByName(string name) =>
        _units.FirstOrDefault(u =>
            string.Equals(u.Symbol, name, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(u.FormalName, name, StringComparison.OrdinalIgnoreCase));

    public Godown? FindGodownByName(string name) =>
        _godowns.FirstOrDefault(g =>
            string.Equals(g.Name, name, StringComparison.OrdinalIgnoreCase) ||
            (g.Alias is not null && string.Equals(g.Alias, name, StringComparison.OrdinalIgnoreCase)));

    public StockItem? FindStockItemByName(string name) =>
        _stockItems.FirstOrDefault(i =>
            string.Equals(i.Name, name, StringComparison.OrdinalIgnoreCase) ||
            (i.Alias is not null && string.Equals(i.Alias, name, StringComparison.OrdinalIgnoreCase)));

    /// <summary>All opening-stock allocations that belong to a given stock item.</summary>
    public IEnumerable<StockOpeningBalance> OpeningBalancesFor(Guid stockItemId) =>
        _stockOpeningBalances.Where(b => b.StockItemId == stockItemId);
}
