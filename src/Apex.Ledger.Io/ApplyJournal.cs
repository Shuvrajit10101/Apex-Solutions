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
    private readonly List<BillOfMaterials> _billsOfMaterials = new();
    private readonly List<StockOpeningBalance> _openingBalances = new();
    private readonly List<Currency> _currencies = new();
    private readonly List<ExchangeRate> _exchangeRates = new();
    private readonly List<CostCategory> _costCategories = new();
    private readonly List<CostCentre> _costCentres = new();
    private readonly List<Budget> _budgets = new();
    private readonly List<Scenario> _scenarios = new();
    private readonly List<PriceLevel> _priceLevels = new();
    private readonly List<PriceList> _priceLists = new();
    private readonly List<ReorderDefinition> _reorderDefinitions = new();
    private readonly List<TdsChallan> _tdsChallans = new();
    private readonly List<ChallanVoucherLink> _challanVoucherLinks = new();
    private readonly List<TcsChallan> _tcsChallans = new();
    private readonly List<ChallanVoucherLink> _tcsChallanVoucherLinks = new();
    private readonly List<RcmDocument> _rcmDocuments = new();
    private readonly List<EInvoiceRecord> _eInvoiceRecords = new();
    private readonly List<GstCreditDebitNoteLink> _cdnLinks = new();
    private readonly List<GstAdvanceReceipt> _advanceReceipts = new();
    private readonly List<EmployeeCategory> _employeeCategories = new();
    private readonly List<EmployeeGroup> _employeeGroups = new();
    private readonly List<Employee> _employees = new();
    private readonly List<PayrollUnit> _payrollUnits = new();
    private readonly List<AttendanceType> _attendanceTypes = new();
    private readonly List<PayHead> _payHeads = new();
    private readonly List<SalaryStructure> _salaryStructures = new();
    private readonly List<AttendanceEntry> _attendanceEntries = new();
    private readonly List<TaxDeclaration> _taxDeclarations = new();

    // Enable Job Order Processing: whether it was already on before the import stamped the seeded voucher types, so a
    // rollback re-runs JobWorkService.SetEnabled(prior) to restore both the company flag and the seeded type flags.
    private bool? _jobOrderProcessingBefore;

    // GST enable: whether GST was already on, and the ledger ids present just before EnableGst ran (so its
    // auto-created tax + round-off ledgers can be pruned on rollback).
    private bool _gstRecorded;
    private bool _gstWasEnabled;
    private GstConfig? _priorGst;
    private HashSet<Guid> _ledgerIdsBeforeGst = new();

    // TDS/TCS enable (Phase 7 slice 1): mirror the GST enable rollback — snapshot the prior config + ledger set so
    // the auto-created "TDS Payable"/"TCS Payable" ledger can be pruned and the config restored on rollback.
    private bool _tdsRecorded;
    private bool _tdsWasEnabled;
    private TdsConfig? _priorTds;
    private HashSet<Guid> _ledgerIdsBeforeTds = new();
    private bool _tcsRecorded;
    private bool _tcsWasEnabled;
    private TcsConfig? _priorTcs;
    private HashSet<Guid> _ledgerIdsBeforeTcs = new();

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
    public void RecordBillOfMaterials(BillOfMaterials b) => _billsOfMaterials.Add(b);
    public void RecordStockOpeningBalance(StockOpeningBalance b) => _openingBalances.Add(b);
    public void RecordCurrency(Currency c) => _currencies.Add(c);
    public void RecordExchangeRate(ExchangeRate r) => _exchangeRates.Add(r);
    public void RecordCostCategory(CostCategory c) => _costCategories.Add(c);
    public void RecordCostCentre(CostCentre c) => _costCentres.Add(c);
    public void RecordBudget(Budget b) => _budgets.Add(b);
    public void RecordScenario(Scenario s) => _scenarios.Add(s);
    public void RecordPriceLevel(PriceLevel l) => _priceLevels.Add(l);
    public void RecordPriceList(PriceList l) => _priceLists.Add(l);
    public void RecordReorderDefinition(ReorderDefinition d) => _reorderDefinitions.Add(d);
    public void RecordTdsChallan(TdsChallan ch) => _tdsChallans.Add(ch);
    public void RecordChallanVoucherLink(ChallanVoucherLink l) => _challanVoucherLinks.Add(l);
    public void RecordTcsChallan(TcsChallan ch) => _tcsChallans.Add(ch);
    public void RecordTcsChallanVoucherLink(ChallanVoucherLink l) => _tcsChallanVoucherLinks.Add(l);
    public void RecordRcmDocument(RcmDocument d) => _rcmDocuments.Add(d);
    public void RecordEInvoiceRecord(EInvoiceRecord r) => _eInvoiceRecords.Add(r);
    public void RecordCreditDebitNoteLink(GstCreditDebitNoteLink l) => _cdnLinks.Add(l);
    public void RecordAdvanceReceipt(GstAdvanceReceipt a) => _advanceReceipts.Add(a);
    public void RecordEmployeeCategory(EmployeeCategory x) => _employeeCategories.Add(x);
    public void RecordEmployeeGroup(EmployeeGroup x) => _employeeGroups.Add(x);
    public void RecordEmployee(Employee x) => _employees.Add(x);
    public void RecordPayrollUnit(PayrollUnit x) => _payrollUnits.Add(x);
    public void RecordAttendanceType(AttendanceType x) => _attendanceTypes.Add(x);
    public void RecordPayHead(PayHead x) => _payHeads.Add(x);
    public void RecordSalaryStructure(SalaryStructure x) => _salaryStructures.Add(x);
    public void RecordAttendanceEntry(AttendanceEntry x) => _attendanceEntries.Add(x);
    public void RecordTaxDeclaration(TaxDeclaration x) => _taxDeclarations.Add(x);

    /// <summary>Snapshots the Enable-Job-Order-Processing flag as it was BEFORE the import toggled it (via
    /// <c>JobWorkService.SetEnabled</c>), so a rollback restores the flag AND the seeded voucher-type flags it
    /// stamps. Call before toggling.</summary>
    public void RecordJobOrderProcessingBefore(bool wasEnabled) => _jobOrderProcessingBefore ??= wasEnabled;

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

    /// <summary>Captures the pre-EnableTds state for rollback (mirror of <see cref="RecordGstEnabledBefore"/>).
    /// Call BEFORE <c>TdsTcsService.EnableTds</c> runs.</summary>
    public void RecordTdsEnabledBefore()
    {
        _tdsRecorded = true;
        _tdsWasEnabled = _company.Tds is not null;
        _priorTds = _company.Tds;
        _ledgerIdsBeforeTds = _company.Ledgers.Select(l => l.Id).ToHashSet();
    }

    /// <summary>Captures the pre-EnableTcs state for rollback. Call BEFORE <c>TdsTcsService.EnableTcs</c> runs.</summary>
    public void RecordTcsEnabledBefore()
    {
        _tcsRecorded = true;
        _tcsWasEnabled = _company.Tcs is not null;
        _priorTcs = _company.Tcs;
        _ledgerIdsBeforeTcs = _company.Ledgers.Select(l => l.Id).ToHashSet();
    }

    public void RecordCompanyHeader(Company t) => _header = CompanyHeaderSnapshot.Capture(t);

    public void Rollback()
    {
        // 0a2) Pay heads + salary structures (Phase 8 slice 2) — remove structures first (their lines FK pay heads),
        //      then pay heads (a computed-on head FKs another pay head; RemovePayHead is a plain list removal so the
        //      order among pay heads is immaterial). Both must go before the payroll masters they reference.
        // Attendance entries (Phase 8 slice 3) reference employees + attendance types → remove before those masters.
        for (var i = _attendanceEntries.Count - 1; i >= 0; i--) _company.RemoveAttendanceEntry(_attendanceEntries[i]);
        // §192 tax declarations (Phase 8 slice 7) reference employees → remove before the employee masters.
        for (var i = _taxDeclarations.Count - 1; i >= 0; i--) _company.RemoveTaxDeclaration(_taxDeclarations[i]);
        for (var i = _salaryStructures.Count - 1; i >= 0; i--) _company.RemoveSalaryStructure(_salaryStructures[i]);
        for (var i = _payHeads.Count - 1; i >= 0; i--) _company.RemovePayHead(_payHeads[i]);

        // 0a) Payroll masters (Phase 8 slice 1) — remove child-first: employees (reference groups + categories),
        //     then attendance types (reference payroll units), then payroll units, then employee groups, then
        //     employee categories. None reference a voucher, so ordering relative to the voucher undo is free.
        for (var i = _employees.Count - 1; i >= 0; i--) _company.RemoveEmployee(_employees[i]);
        for (var i = _attendanceTypes.Count - 1; i >= 0; i--) _company.RemoveAttendanceType(_attendanceTypes[i]);
        for (var i = _payrollUnits.Count - 1; i >= 0; i--) _company.RemovePayrollUnit(_payrollUnits[i]);
        for (var i = _employeeGroups.Count - 1; i >= 0; i--) _company.RemoveEmployeeGroup(_employeeGroups[i]);
        for (var i = _employeeCategories.Count - 1; i >= 0; i--) _company.RemoveEmployeeCategory(_employeeCategories[i]);

        // 0) TDS + TCS deposit challans + their voucher links (Phase 7 slices 3, 6) — remove the links first, then the
        //    challans, before the vouchers they point at are removed below.
        for (var i = _challanVoucherLinks.Count - 1; i >= 0; i--) _company.RemoveChallanVoucherLink(_challanVoucherLinks[i]);
        for (var i = _tdsChallans.Count - 1; i >= 0; i--) _company.RemoveTdsChallan(_tdsChallans[i]);
        for (var i = _tcsChallanVoucherLinks.Count - 1; i >= 0; i--) _company.RemoveTcsChallanVoucherLink(_tcsChallanVoucherLinks[i]);
        for (var i = _tcsChallans.Count - 1; i >= 0; i--) _company.RemoveTcsChallan(_tcsChallans[i]);
        // Phase 9 slice 2: RCM generated documents + §34-CDN links + GST-on-advance receipts — remove before the
        // vouchers they reference are removed below.
        for (var i = _rcmDocuments.Count - 1; i >= 0; i--) _company.RemoveRcmDocument(_rcmDocuments[i]);
        for (var i = _eInvoiceRecords.Count - 1; i >= 0; i--) _company.RemoveEInvoiceRecord(_eInvoiceRecords[i]);
        for (var i = _cdnLinks.Count - 1; i >= 0; i--) _company.RemoveCreditDebitNoteLink(_cdnLinks[i]);
        for (var i = _advanceReceipts.Count - 1; i >= 0; i--) _company.RemoveAdvanceReceipt(_advanceReceipts[i]);

        // 1) Vouchers first (reverse posting order): inventory/order vouchers, then accounting vouchers.
        for (var i = _inventoryVouchers.Count - 1; i >= 0; i--) _company.RemoveInventoryVoucher(_inventoryVouchers[i]);
        for (var i = _vouchers.Count - 1; i >= 0; i--) _company.RemoveVoucher(_vouchers[i]);

        // 1b) Budgets & scenarios reference groups/ledgers/voucher-types — remove them before those masters.
        for (var i = _scenarios.Count - 1; i >= 0; i--) _company.RemoveScenario(_scenarios[i]);
        for (var i = _budgets.Count - 1; i >= 0; i--) _company.RemoveBudget(_budgets[i]);

        // 1c) Phase 6 slice-5/6 masters: price lists (reference a level + item) and reorder definitions (reference an
        //     item/group/category) before those masters are removed; then the bare price levels.
        for (var i = _priceLists.Count - 1; i >= 0; i--) _company.RemovePriceList(_priceLists[i]);
        for (var i = _reorderDefinitions.Count - 1; i >= 0; i--) _company.RemoveReorderDefinition(_reorderDefinitions[i]);
        for (var i = _priceLevels.Count - 1; i >= 0; i--) _company.RemovePriceLevel(_priceLevels[i]);

        // 2) Opening-stock allocations, then BOMs + batch masters (reference items + godowns), then stock items,
        //    godowns, categories, stock groups, units.
        for (var i = _openingBalances.Count - 1; i >= 0; i--) _company.RemoveStockOpeningBalance(_openingBalances[i]);
        // BOMs before their finished-good item is removed. Creating a BOM turned the item's Set-Components flag on
        // (RQ-10); restore it to "has a BOM" for any finished good that SURVIVES the rollback (a pre-existing item
        // that the import merely added a BOM to), mirroring BomService.DeleteBom — so a pre-existing item whose
        // flag was false is left false again (byte-for-byte unchanged, RQ-23).
        for (var i = _billsOfMaterials.Count - 1; i >= 0; i--) _company.RemoveBillOfMaterials(_billsOfMaterials[i]);
        foreach (var bom in _billsOfMaterials)
            if (_company.FindStockItem(bom.StockItemId) is { } fg)
                fg.SetComponents = _company.BomsFor(bom.StockItemId).Any();
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

        // 5a) TDS/TCS enable undo (Phase 7 slice 1): mirror the GST undo. Runs AFTER the explicit journal ledger
        //     removals (step 3) and the GST prune, so pruning "ledgers not in the pre-enable snapshot" catches
        //     exactly the auto-created "TDS Payable"/"TCS Payable" ledger — never a pre-existing tagged ledger.
        if (_tdsRecorded && !_tdsWasEnabled)
            foreach (var l in _company.Ledgers.Where(l => !_ledgerIdsBeforeTds.Contains(l.Id)).ToList())
                _company.RemoveLedger(l);
        if (_tdsRecorded) _company.Tds = _priorTds;

        if (_tcsRecorded && !_tcsWasEnabled)
            foreach (var l in _company.Ledgers.Where(l => !_ledgerIdsBeforeTcs.Contains(l.Id)).ToList())
                _company.RemoveLedger(l);
        if (_tcsRecorded) _company.Tcs = _priorTcs;

        // 5b) Enable Job Order Processing undo: re-run SetEnabled(prior) so both the company flag and the seeded
        //     Material In/Out + Job Work Order voucher-type flags (IsActive / UseForJobWork / AllowConsumption) are
        //     restored to exactly what they were before the import stamped them.
        if (_jobOrderProcessingBefore is { } wasEnabled)
            new Services.JobWorkService(_company).SetEnabled(wasEnabled);

        // 6) Company header.
        _header?.RestoreTo(_company);
    }

    private sealed record CompanyHeaderSnapshot(
        string Name, string MailingName, string? Address, string Country, string? State, string? Pin,
        DateOnly FinancialYearStart, DateOnly BooksBeginFrom, string BaseCurrencySymbol, string BaseCurrencyName,
        int DecimalPlaces, string DecimalUnitName,
        bool UseSeparateActualBilledQuantity, bool EnableMultiplePriceLevels,
        bool PayrollEnabled, bool PayrollStatutoryEnabled, bool SalaryTdsEnabled,
        PfConfig? PfConfig, EsiConfig? EsiConfig, PtConfig? PtConfig,
        GratuityConfig? GratuityConfig, BonusConfig? BonusConfig)
    {
        public static CompanyHeaderSnapshot Capture(Company t) => new(
            t.Name, t.MailingName, t.Address, t.Country, t.State, t.Pin,
            t.FinancialYearStart, t.BooksBeginFrom, t.BaseCurrencySymbol, t.BaseCurrencyName,
            t.DecimalPlaces, t.DecimalUnitName,
            t.UseSeparateActualBilledQuantity, t.EnableMultiplePriceLevels,
            t.PayrollEnabled, t.PayrollStatutoryEnabled, t.SalaryTdsEnabled, t.PfConfig, t.EsiConfig, t.PtConfig,
            t.GratuityConfig, t.BonusConfig);

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
            t.UseSeparateActualBilledQuantity = UseSeparateActualBilledQuantity;
            t.EnableMultiplePriceLevels = EnableMultiplePriceLevels;
            t.PayrollEnabled = PayrollEnabled;
            t.PayrollStatutoryEnabled = PayrollStatutoryEnabled;
            t.SalaryTdsEnabled = SalaryTdsEnabled;
            t.PfConfig = PfConfig;
            t.EsiConfig = EsiConfig;
            t.PtConfig = PtConfig;
            t.GratuityConfig = GratuityConfig;
            t.BonusConfig = BonusConfig;
        }
    }
}
