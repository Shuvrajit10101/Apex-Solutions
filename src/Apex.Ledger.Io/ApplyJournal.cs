using Apex.Ledger;
using Apex.Ledger.Domain;

namespace Apex.Ledger.Io;

/// <summary>
/// Records every mutation an <see cref="ImportPlan.Execute"/> makes to the target company so a mid-apply failure can
/// be fully <see cref="Rollback"/>'d, leaving the target byte-for-byte unchanged (RQ-23 transactional / all-or-
/// nothing). Undo runs in reverse: vouchers, then masters (children before parents by virtue of reverse creation
/// order), then merged-opening restores, GST-enable undo, and finally the company header snapshot. Only the roll-
/// back path uses the domain's raw list removals — the happy path never removes anything.
/// </summary>
internal sealed class ApplyJournal
{
    private readonly Company _company;

    private readonly List<Voucher> _vouchers = new();
    private readonly List<InventoryVoucher> _inventoryVouchers = new();
    private readonly List<Group> _groups = new();
    private readonly List<Domain.Ledger> _ledgers = new();
    private readonly List<VoucherType> _voucherTypes = new();
    private readonly List<Unit> _units = new();
    private readonly List<StockGroup> _stockGroups = new();
    private readonly List<StockCategory> _stockCategories = new();
    private readonly List<Godown> _godowns = new();
    private readonly List<StockItem> _stockItems = new();
    private readonly List<BatchMaster> _batchMasters = new();
    private readonly List<StockOpeningBalance> _openingBalances = new();
    private readonly List<Currency> _currencies = new();
    private readonly List<ExchangeRate> _exchangeRates = new();
    private readonly List<CostCategory> _costCategories = new();
    private readonly List<CostCentre> _costCentres = new();
    private readonly List<Budget> _budgets = new();
    private readonly List<Scenario> _scenarios = new();

    // GST enable: whether GST was already on, and the ledger ids present just before EnableGst ran (so its
    // auto-created tax + round-off ledgers can be pruned on rollback).
    private bool _gstRecorded;
    private bool _gstWasEnabled;
    private GstConfig? _priorGst;
    private HashSet<Guid> _ledgerIdsBeforeGst = new();

    // Ledger-opening merges / overlays: the pre-change (magnitude, side, group) so it can be restored.
    private readonly List<(Domain.Ledger Ledger, Money Opening, bool IsDebit, Guid? GroupId)> _openingSnapshots = new();

    // Company header snapshot.
    private CompanyHeaderSnapshot? _header;

    public ApplyJournal(Company company) => _company = company;

    public void RecordVoucher(Voucher v) => _vouchers.Add(v);
    public void RecordInventoryVoucher(InventoryVoucher v) => _inventoryVouchers.Add(v);
    public void RecordGroup(Group g) => _groups.Add(g);
    public void RecordLedger(Domain.Ledger l) => _ledgers.Add(l);
    public void RecordVoucherType(VoucherType t) => _voucherTypes.Add(t);
    public void RecordUnit(Unit u) => _units.Add(u);
    public void RecordStockGroup(StockGroup g) => _stockGroups.Add(g);
    public void RecordStockCategory(StockCategory c) => _stockCategories.Add(c);
    public void RecordGodown(Godown g) => _godowns.Add(g);
    public void RecordStockItem(StockItem i) => _stockItems.Add(i);
    public void RecordBatchMaster(BatchMaster b) => _batchMasters.Add(b);
    public void RecordStockOpeningBalance(StockOpeningBalance b) => _openingBalances.Add(b);
    public void RecordCurrency(Currency c) => _currencies.Add(c);
    public void RecordExchangeRate(ExchangeRate r) => _exchangeRates.Add(r);
    public void RecordCostCategory(CostCategory c) => _costCategories.Add(c);
    public void RecordCostCentre(CostCentre c) => _costCentres.Add(c);
    public void RecordBudget(Budget b) => _budgets.Add(b);
    public void RecordScenario(Scenario s) => _scenarios.Add(s);

    public void RecordLedgerOpeningSnapshot(Domain.Ledger l, bool captureGroup) =>
        _openingSnapshots.Add((l, l.OpeningBalance, l.OpeningIsDebit, captureGroup ? l.GroupId : null));

    /// <summary>
    /// Captures the pre-EnableGst state for rollback. <b>Call this BEFORE <c>GstService.EnableGst</c> runs</b>: it
    /// snapshots whether GST was already enabled and the exact prior <see cref="GstConfig"/> instance, so a rollback
    /// can restore it faithfully even when the target already had GST on with a different config. It also snapshots
    /// the ledger-id set present before EnableGst, so on rollback of a <i>newly</i>-enabled GST only the ledgers
    /// EnableGst actually auto-created (tax + Round Off) are pruned — never a pre-existing ledger that merely carries
    /// a GST classification.
    /// </summary>
    public void RecordGstEnabledBefore()
    {
        _gstRecorded = true;
        _gstWasEnabled = _company.Gst is not null;
        _priorGst = _company.Gst;                    // the ORIGINAL config (or null), captured before EnableGst mutates
        _ledgerIdsBeforeGst = _company.Ledgers.Select(l => l.Id).ToHashSet();
    }

    public void RecordCompanyHeader(Company t) => _header = CompanyHeaderSnapshot.Capture(t);

    public void Rollback()
    {
        // 1) Vouchers first (reverse posting order): inventory/order vouchers, then accounting vouchers.
        for (var i = _inventoryVouchers.Count - 1; i >= 0; i--) _company.RemoveInventoryVoucher(_inventoryVouchers[i]);
        for (var i = _vouchers.Count - 1; i >= 0; i--) _company.RemoveVoucher(_vouchers[i]);

        // 1b) Budgets & scenarios reference groups/ledgers/voucher-types — remove them before those masters.
        for (var i = _scenarios.Count - 1; i >= 0; i--) _company.RemoveScenario(_scenarios[i]);
        for (var i = _budgets.Count - 1; i >= 0; i--) _company.RemoveBudget(_budgets[i]);

        // 2) Opening-stock allocations, then batch masters (reference items + godowns), then stock items, godowns,
        //    categories, stock groups, units.
        for (var i = _openingBalances.Count - 1; i >= 0; i--) _company.RemoveStockOpeningBalance(_openingBalances[i]);
        for (var i = _batchMasters.Count - 1; i >= 0; i--) _company.RemoveBatchMaster(_batchMasters[i]);
        for (var i = _stockItems.Count - 1; i >= 0; i--) _company.RemoveStockItem(_stockItems[i]);
        for (var i = _godowns.Count - 1; i >= 0; i--) _company.RemoveGodown(_godowns[i]);
        for (var i = _stockCategories.Count - 1; i >= 0; i--) _company.RemoveStockCategory(_stockCategories[i]);
        for (var i = _stockGroups.Count - 1; i >= 0; i--) _company.RemoveStockGroup(_stockGroups[i]);
        for (var i = _units.Count - 1; i >= 0; i--) _company.RemoveUnit(_units[i]);

        // 2b) Cost centres (children-before-parents via reverse order) then cost categories; exchange rates then
        //     currencies (a rate hangs off a currency, so remove the rate first).
        for (var i = _costCentres.Count - 1; i >= 0; i--) _company.RemoveCostCentre(_costCentres[i]);
        for (var i = _costCategories.Count - 1; i >= 0; i--) _company.RemoveCostCategory(_costCategories[i]);
        for (var i = _exchangeRates.Count - 1; i >= 0; i--) _company.RemoveExchangeRate(_exchangeRates[i]);
        for (var i = _currencies.Count - 1; i >= 0; i--) _company.RemoveCurrency(_currencies[i]);

        // 3) Accounting masters: ledgers, voucher types, groups (children-before-parents via reverse order).
        for (var i = _ledgers.Count - 1; i >= 0; i--) _company.RemoveLedger(_ledgers[i]);
        for (var i = _voucherTypes.Count - 1; i >= 0; i--) _company.RemoveVoucherType(_voucherTypes[i]);
        for (var i = _groups.Count - 1; i >= 0; i--) _company.RemoveGroup(_groups[i]);

        // 4) Restore any merged/overlaid ledger openings (and group, when it was overlaid).
        foreach (var (ledger, opening, isDebit, groupId) in _openingSnapshots)
        {
            ledger.OpeningBalance = opening;
            ledger.OpeningIsDebit = isDebit;
            if (groupId is { } g) ledger.GroupId = g;
        }

        // 5) GST enable undo: if GST was newly enabled (it was off before), prune EXACTLY the ledgers EnableGst
        //    auto-created — i.e. the ledgers that are present now but were NOT in the pre-enable snapshot — and
        //    restore the prior (null) config. Keying off the snapshot (not "has a GST classification / is Round
        //    Off") means a pre-existing GST-classified or Round-Off ledger that was already on the target is never
        //    wrongly removed. When GST was already on before, EnableGst was idempotent (no ledgers added, config
        //    unchanged), so nothing is pruned and the original config instance is restored as-is.
        if (_gstRecorded && !_gstWasEnabled)
        {
            foreach (var l in _company.Ledgers.Where(l => !_ledgerIdsBeforeGst.Contains(l.Id)).ToList())
                _company.RemoveLedger(l);
        }
        if (_gstRecorded)
            _company.Gst = _priorGst; // the ORIGINAL config (null when GST was off before EnableGst ran)

        // 6) Company header.
        _header?.RestoreTo(_company);
    }

    private sealed record CompanyHeaderSnapshot(
        string Name, string MailingName, string? Address, string Country, string? State, string? Pin,
        DateOnly FinancialYearStart, DateOnly BooksBeginFrom, string BaseCurrencySymbol, string BaseCurrencyName,
        int DecimalPlaces, string DecimalUnitName)
    {
        public static CompanyHeaderSnapshot Capture(Company t) => new(
            t.Name, t.MailingName, t.Address, t.Country, t.State, t.Pin,
            t.FinancialYearStart, t.BooksBeginFrom, t.BaseCurrencySymbol, t.BaseCurrencyName,
            t.DecimalPlaces, t.DecimalUnitName);

        public void RestoreTo(Company t)
        {
            t.Name = Name;
            t.MailingName = MailingName;
            t.Address = Address;
            t.Country = Country;
            t.State = State;
            t.Pin = Pin;
            t.FinancialYearStart = FinancialYearStart;
            t.BooksBeginFrom = BooksBeginFrom;
            t.BaseCurrencySymbol = BaseCurrencySymbol;
            t.BaseCurrencyName = BaseCurrencyName;
            t.DecimalPlaces = DecimalPlaces;
            t.DecimalUnitName = DecimalUnitName;
        }
    }
}
