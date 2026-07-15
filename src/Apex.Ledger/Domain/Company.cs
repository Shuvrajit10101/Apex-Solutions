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
    private readonly List<BatchMaster> _batchMasters = new();
    private readonly List<BillOfMaterials> _billsOfMaterials = new();
    private readonly List<PriceLevel> _priceLevels = new();
    private readonly List<PriceList> _priceLists = new();
    private readonly List<ReorderDefinition> _reorderDefinitions = new();
    private readonly List<TdsChallan> _tdsChallans = new();
    private readonly List<ChallanVoucherLink> _challanVoucherLinks = new();
    private readonly List<RcmDocument> _rcmDocuments = new();
    private readonly List<EInvoiceRecord> _eInvoiceRecords = new();
    private readonly List<EWayBillRecord> _eWayBillRecords = new();
    private readonly List<GstCreditDebitNoteLink> _cdnLinks = new();
    private readonly List<GstAdvanceReceipt> _advanceReceipts = new();
    private readonly List<Gstr2bSnapshot> _gstr2bSnapshots = new();
    private readonly List<Gstr2bReconResult> _gstr2bReconResults = new();
    private readonly List<ImsAction> _imsActions = new();
    private readonly List<TcsChallan> _tcsChallans = new();
    private readonly List<ChallanVoucherLink> _tcsChallanVoucherLinks = new();
    private readonly List<EmployeeCategory> _employeeCategories = new();
    private readonly List<EmployeeGroup> _employeeGroups = new();
    private readonly List<Employee> _employees = new();
    private readonly List<PayrollUnit> _payrollUnits = new();
    private readonly List<AttendanceType> _attendanceTypes = new();
    private readonly List<PayHead> _payHeads = new();
    private readonly List<SalaryStructure> _salaryStructures = new();
    private readonly List<AttendanceEntry> _attendanceEntries = new();
    private readonly List<TaxDeclaration> _taxDeclarations = new();

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

    /// <summary>
    /// The company TDS deductor configuration (Phase 7 slice 1). <c>null</c> (or a config with
    /// <see cref="TdsConfig.Enabled"/> false) means TDS is off — the default for every existing company, so the
    /// pre-Phase-7 paths are byte-for-byte unchanged (ER-13). Set (and its "TDS Payable" ledger auto-created) by
    /// <c>TdsTcsService.EnableTds</c>.
    /// </summary>
    public TdsConfig? Tds { get; set; }

    /// <summary>The company TCS collector configuration (Phase 7 slice 1); <c>null</c>/disabled ⇒ TCS off. Set by
    /// <c>TdsTcsService.EnableTcs</c>.</summary>
    public TcsConfig? Tcs { get; set; }

    /// <summary>True iff TDS is enabled for this company.</summary>
    public bool TdsEnabled => Tds is { Enabled: true };

    /// <summary>True iff TCS is enabled for this company.</summary>
    public bool TcsEnabled => Tcs is { Enabled: true };

    /// <summary>All Nature-of-Payment (TDS section) masters, or empty when TDS is off.</summary>
    public IReadOnlyList<NatureOfPayment> NaturesOfPayment =>
        Tds?.NaturesOfPayment ?? (IReadOnlyList<NatureOfPayment>)Array.Empty<NatureOfPayment>();

    /// <summary>All Nature-of-Goods (§206C) masters, or empty when TCS is off.</summary>
    public IReadOnlyList<NatureOfGoods> NaturesOfGoods =>
        Tcs?.NaturesOfGoods ?? (IReadOnlyList<NatureOfGoods>)Array.Empty<NatureOfGoods>();

    /// <summary>Finds a Nature-of-Payment master by its id, or <c>null</c>.</summary>
    public NatureOfPayment? FindNatureOfPayment(Guid id) => NaturesOfPayment.FirstOrDefault(n => n.Id == id);

    /// <summary>Finds a Nature-of-Payment master by its section code (case-insensitive), or <c>null</c>.</summary>
    public NatureOfPayment? FindNatureOfPaymentByCode(string sectionCode) =>
        NaturesOfPayment.FirstOrDefault(n => string.Equals(n.SectionCode, sectionCode?.Trim(), StringComparison.OrdinalIgnoreCase));

    /// <summary>Finds a Nature-of-Goods master by its id, or <c>null</c>.</summary>
    public NatureOfGoods? FindNatureOfGoods(Guid id) => NaturesOfGoods.FirstOrDefault(n => n.Id == id);

    /// <summary>Finds a Nature-of-Goods master by its collection code (case-insensitive), or <c>null</c>.</summary>
    public NatureOfGoods? FindNatureOfGoodsByCode(string collectionCode) =>
        NaturesOfGoods.FirstOrDefault(n => string.Equals(n.CollectionCode, collectionCode?.Trim(), StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Backing field for <see cref="MaintainBatchwiseDetails"/>: <c>null</c> ⇒ "not explicitly set", so the
    /// getter falls back to inferring the flag from persisted batch state (see below).
    /// </summary>
    private bool? _maintainBatchwiseDetails;

    /// <summary>
    /// Company feature flag <b>"Maintain Batch-wise details"</b> (F11 Company Features; Phase 6 Cluster 1;
    /// requirements RQ-2/RQ-52). This is the master gate for the whole batch/expiry feature: the per-item batch
    /// switches, the Batch master, the batch-allocation sub-screen and the batch reports are all hidden/inert
    /// when it is off.
    /// <para>
    /// It is an <b>in-memory</b> flag (no schema column — Phase 6 slice 1 added no company column, ER-1). When
    /// never explicitly set it is <b>inferred</b> as true whenever the company already carries any batch state —
    /// a batch master exists, or an item has <see cref="StockItem.MaintainInBatches"/> on — so a company that was
    /// configured for batches keeps the flag on across a reload without a new column. The F11 toggle sets it
    /// explicitly; setting it back to its inferred value clears the override. Turning it off does not delete any
    /// batch data (harmless — the batch UI simply hides).
    /// </para>
    /// </summary>
    public bool MaintainBatchwiseDetails
    {
        get => _maintainBatchwiseDetails ?? HasAnyBatchState;
        set => _maintainBatchwiseDetails = value;
    }

    /// <summary>True iff the company already carries persisted batch state (a batch master or a batch-tracked
    /// item) — the basis for inferring <see cref="MaintainBatchwiseDetails"/> when it was never set explicitly.</summary>
    private bool HasAnyBatchState =>
        _batchMasters.Count > 0 || _stockItems.Any(i => i.MaintainInBatches);

    /// <summary>
    /// Backing field for <see cref="SetComponentsBom"/>: <c>null</c> ⇒ "not explicitly set", so the getter
    /// falls back to inferring the flag from persisted BOM state (see below).
    /// </summary>
    private bool? _setComponentsBom;

    /// <summary>
    /// F12-configuration flag <b>"Set Components (BOM)"</b> (Phase 6 Cluster 2; requirements RQ-9/RQ-10/RQ-52).
    /// This is the master gate for the whole Bill-of-Materials / Manufacturing feature: the per-item
    /// <see cref="StockItem.SetComponents"/> switch, the BOM master, and the Manufacturing-Journal voucher are
    /// all hidden/inert when it is off.
    /// <para>
    /// It is an <b>in-memory</b> flag (no schema column — the BOM backend added none on the company row, ER-1).
    /// When never explicitly set it is <b>inferred</b> as true whenever the company already carries any BOM state
    /// — a Bill of Materials exists, or an item has <see cref="StockItem.SetComponents"/> on — so a company that
    /// was configured for BOMs keeps the flag on across a reload without a new column. The F12 toggle sets it
    /// explicitly; turning it off does not delete any BOM data (harmless — the BOM UI simply hides).
    /// </para>
    /// </summary>
    public bool SetComponentsBom
    {
        get => _setComponentsBom ?? HasAnyBomState;
        set => _setComponentsBom = value;
    }

    /// <summary>True iff the company already carries persisted BOM state (a Bill of Materials or an item with
    /// <see cref="StockItem.SetComponents"/> on) — the basis for inferring <see cref="SetComponentsBom"/> when it
    /// was never set explicitly.</summary>
    private bool HasAnyBomState =>
        _billsOfMaterials.Count > 0 || _stockItems.Any(i => i.SetComponents);

    /// <summary>
    /// F12-configuration flag <b>"Define type of component for BOM"</b> (Phase 6 Cluster 2; requirement RQ-10).
    /// When on, a BOM line may be typed as a By-Product / Co-Product / Scrap carve-out (the type picker is
    /// surfaced); when off, every line is a plain Component. Defaults to <c>false</c>. In-memory (no schema
    /// column); only meaningful while <see cref="SetComponentsBom"/> is on.
    /// </summary>
    public bool DefineBomComponentType { get; set; }

    /// <summary>
    /// Company feature flag <b>"Use separate Actual and Billed Quantity columns in Invoices"</b> (F11 Company
    /// Features; Book p.145; Phase 6 slice 4; requirements RQ-22/RQ-52; DP-7). When on, each Sales/Purchase
    /// item-invoice line exposes a Quantity (Actual — updates stock) and a Quantity (Billed — updates accounts &amp;
    /// GST); when off, one quantity shows and Billed ≡ Actual, byte-identical to today (ER-13).
    /// <para>
    /// Unlike <see cref="MaintainBatchwiseDetails"/> / <see cref="SetComponentsBom"/> (inferred from data), this is
    /// a <b>pure user toggle</b> that <b>cannot be inferred</b> — a company may enable it before entering any
    /// Actual/Billed line — so it is a plain persisted <c>get; set;</c> backed by a real <c>companies</c> column
    /// (v20; DP-7). Defaults to <c>false</c>.
    /// </para>
    /// </summary>
    public bool UseSeparateActualBilledQuantity { get; set; }

    /// <summary>
    /// Company feature flag <b>"Enable multiple Price Levels"</b> (F11 Company Features → Inventory; Book pp.33–35;
    /// Phase 6 slice 5; requirements RQ-26/RQ-52). Master gate for the whole Price-Levels/Price-Lists feature: the
    /// levels master, the price-list master, the party-default-level field, the Sales header field, the discount
    /// column and the Price List report are all hidden/inert when it is off.
    /// <para>
    /// Like <see cref="UseSeparateActualBilledQuantity"/> (and unlike <see cref="MaintainBatchwiseDetails"/> /
    /// <see cref="SetComponentsBom"/>, which are inferred from data), this is a <b>pure user toggle</b> that
    /// <b>cannot be inferred</b> — a company may enable it before defining any level — so it is a plain persisted
    /// <c>get; set;</c> backed by a real <c>companies</c> column (v21). Defaults to <c>false</c>, so every existing
    /// company is byte-identical (ER-13).
    /// </para>
    /// </summary>
    public bool EnableMultiplePriceLevels { get; set; }

    /// <summary>
    /// Company feature flag <b>"Enable Job Order Processing"</b> (F11 Company Features; Phase 6 slice 8; RQ-45).
    /// Master gate for the whole Job-Work feature: turning it on activates the four seeded-but-inactive predefined
    /// voucher types (Job Work In/Out Order, Material In/Out), sets <see cref="VoucherType.UseForJobWork"/> on the
    /// Material In/Out types and <see cref="VoucherType.AllowConsumption"/> on Material In, and surfaces the
    /// Job-Work entry screens + registers.
    /// <para>
    /// Like <see cref="UseSeparateActualBilledQuantity"/> / <see cref="EnableMultiplePriceLevels"/> (and unlike the
    /// inferred <see cref="MaintainBatchwiseDetails"/> / <see cref="SetComponentsBom"/>), this is a <b>pure user
    /// toggle</b> that <b>cannot be inferred</b> — a company may enable it before entering any order — so it is a
    /// plain persisted <c>get; set;</c> backed by a real <c>companies</c> column (v24). Defaults to <c>false</c>, so
    /// every existing company is byte-identical (ER-13) and the four job-work types stay inactive and hidden.
    /// </para>
    /// </summary>
    public bool EnableJobOrderProcessing { get; set; }

    /// <summary>
    /// Company feature flag <b>"Maintain Payroll"</b> (F11 Company Features; Phase 8 slice 1; RQ-1). Master gate
    /// for the whole Payroll module: the employee/pay-head masters, the Attendance/Payroll voucher types and the
    /// payroll reports are all hidden/inert when it is off.
    /// <para>
    /// Like <see cref="EnableJobOrderProcessing"/> (a pure persisted user toggle that cannot be inferred — a
    /// company may enable it before defining any employee), it is a plain persisted <c>get; set;</c> backed by a
    /// real <c>companies</c> column (v30). Defaults to <c>false</c>, so every existing company is byte-identical
    /// and carries no payroll masters (ER-13).
    /// </para>
    /// </summary>
    public bool PayrollEnabled { get; set; }

    /// <summary>
    /// Company feature flag <b>"Enable Payroll Statutory"</b> (F11 Company Features; Phase 8 slice 1; RQ-1) —
    /// surfaces the Company Payroll Statutory Details screen (PF/ESI/NPS/IT codes) in the later statutory slices.
    /// A pure persisted toggle backed by a real <c>companies</c> column (v30); defaults to <c>false</c> (ER-13).
    /// Only meaningful while <see cref="PayrollEnabled"/> is on.
    /// </summary>
    public bool PayrollStatutoryEnabled { get; set; }

    /// <summary>
    /// The establishment's <b>Provident-Fund configuration</b> (Phase 8 slice 4; catalog §14) — EPF rate toggle,
    /// establishment code, default cap flag. Non-<c>null</c> once the establishment is enrolled for PF; the
    /// dedicated <c>PfContribution</c> engine reads the rate + cap from here, and the payroll voucher posts the
    /// establishment EPF-admin charge only when it is present. Defaults <c>null</c>, so a company that never enrols
    /// for PF serialises byte-identically to a pre-v33 company (ER-13).
    /// </summary>
    public PfConfig? PfConfig { get; set; }

    /// <summary>
    /// The establishment's <b>Employees'-State-Insurance configuration</b> (Phase 8 slice 5; catalog §14) — EE/ER
    /// rate defaults + the 17-digit employer code. Non-<c>null</c> once the establishment is enrolled for ESI; the
    /// dedicated <c>EsiContribution</c> engine reads the rates from here, and the payroll voucher posts the ESI legs
    /// only when it is present. Defaults <c>null</c>, so a company that never enrols for ESI serialises
    /// byte-identically to a pre-v34 company (ER-13).
    /// </summary>
    public EsiConfig? EsiConfig { get; set; }

    /// <summary>
    /// The establishment's <b>Professional-Tax configuration</b> (Phase 8 slice 6; catalog §14) — the active PT
    /// state, enrolment number, wage basis and the editable per-state slab tables. Non-<c>null</c> once the
    /// establishment is enrolled for PT; the dedicated <c>ProfessionalTax</c> engine resolves the slab table from
    /// the config's state + the employee's gender and the payroll voucher posts the PT deduction only when it is
    /// present. Defaults <c>null</c>, so a company that never enrols for PT serialises byte-identically to a pre-v35
    /// company (ER-13).
    /// </summary>
    public PtConfig? PtConfig { get; set; }

    /// <summary>
    /// Whether the establishment deducts <b>§192 salary TDS</b> (Phase 8 slice 7; catalog §14; F11 Payroll
    /// Statutory). When <c>true</c> the payroll voucher computes the average-rate monthly TDS for each employee whose
    /// structure carries a §192 income-tax deduction head. Additive, defaults <c>false</c> so a company that never
    /// enables salary-TDS is byte-identical to a pre-v36 company (ER-13). The deductor/TAN/responsible-person facts
    /// reuse the Phase-7 <c>TdsConfig</c> (no parallel deductor config).
    /// </summary>
    public bool SalaryTdsEnabled { get; set; }

    /// <summary>
    /// The establishment's <b>Gratuity configuration</b> (Phase 8 slice 9; catalog §14) — the statutory cap, wage
    /// basis and provision population the deterministic gratuity accrual reads. Non-<c>null</c> once the establishment
    /// provisions for gratuity; the <c>GratuityProvision</c> service posts the period-end provision voucher (Dr
    /// Gratuity Expense / Cr Gratuity Provision, the increase over the prior balance) only when it is present. Defaults
    /// <c>null</c>, so a company that never provisions for gratuity serialises byte-identically to a pre-v37 company
    /// (ER-13).
    /// </summary>
    public GratuityConfig? GratuityConfig { get; set; }

    /// <summary>
    /// The establishment's <b>statutory-Bonus configuration</b> (Phase 8 slice 9; catalog §14) — the bonus rate, §12
    /// calculation ceiling, state minimum wage and prorate flag the deterministic bonus computation reads.
    /// Non-<c>null</c> once the establishment computes statutory bonus; the bonus register projects the per-employee
    /// eligible/capped base + annual bonus only when it is present. Defaults <c>null</c>, so a company that never
    /// computes bonus serialises byte-identically to a pre-v37 company (ER-13).
    /// </summary>
    public BonusConfig? BonusConfig { get; set; }

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

    /// <summary>Batch / lot masters (catalog §11 Cluster 1; Phase 6 RQ-1): per stock item, per-item-unique.</summary>
    public IReadOnlyList<BatchMaster> BatchMasters => _batchMasters;

    /// <summary>Bills of Materials (catalog §11 Cluster 2; Phase 6 RQ-9): manufacturing recipes, per finished good.</summary>
    public IReadOnlyList<BillOfMaterials> BillsOfMaterials => _billsOfMaterials;

    /// <summary>Price Levels (catalog §11; Phase 6 slice 5; RQ-26): named rate tiers (Wholesale/Retail…).</summary>
    public IReadOnlyList<PriceLevel> PriceLevels => _priceLevels;

    /// <summary>Price Lists (catalog §11; Phase 6 slice 5; RQ-27): dated slab-rate versions per (level, item),
    /// append-only history.</summary>
    public IReadOnlyList<PriceList> PriceLists => _priceLists;

    /// <summary>Reorder-Level definitions (catalog §11; Phase 6 slice 6; RQ-32): per item / group / category,
    /// at most one per (scope, target). The Reorder-Status report resolves the most-specific one per item.</summary>
    public IReadOnlyList<ReorderDefinition> ReorderDefinitions => _reorderDefinitions;

    /// <summary>TDS deposit challans (catalog §13; Phase 7 slice 3; ITNS-281): one per TDS payment into the bank.</summary>
    public IReadOnlyList<TdsChallan> TdsChallans => _tdsChallans;

    /// <summary>Links between a <see cref="TdsChallan"/> and the Stat-Payment voucher that booked its deposit
    /// (Phase 7 slice 3; the <c>challan_voucher_links</c> set).</summary>
    public IReadOnlyList<ChallanVoucherLink> ChallanVoucherLinks => _challanVoucherLinks;

    /// <summary>TCS deposit challans (catalog §13; Phase 7 slice 6; ITNS-281): one per TCS payment into the bank.</summary>
    public IReadOnlyList<TcsChallan> TcsChallans => _tcsChallans;

    /// <summary>Links between a <see cref="TcsChallan"/> and the Stat-Payment voucher that booked its deposit
    /// (Phase 7 slice 6; the <c>tcs_challan_voucher_links</c> set — a TCS-specific sibling of the TDS one).</summary>
    public IReadOnlyList<ChallanVoucherLink> TcsChallanVoucherLinks => _tcsChallanVoucherLinks;

    /// <summary>RCM generated documents (Phase 9 slice 2; RQ-8): Rule-47A self-invoices + Rule-52 payment vouchers.
    /// Empty when reverse charge is unused (ER-13).</summary>
    public IReadOnlyList<RcmDocument> RcmDocuments => _rcmDocuments;

    /// <summary>e-Invoice IRP artefacts (Phase 9 slice 4a; RQ-5): one per covered outward document. Empty when
    /// e-invoicing is unused (ER-13).</summary>
    public IReadOnlyList<EInvoiceRecord> EInvoiceRecords => _eInvoiceRecords;

    /// <summary>e-Way Bill artefacts (Phase 9 slice 5; RQ-6): one per covered goods-movement document. Empty when
    /// e-Way is unused (ER-13).</summary>
    public IReadOnlyList<EWayBillRecord> EWayBillRecords => _eWayBillRecords;

    /// <summary>§34 credit/debit-note links (Phase 9; RQ-24): the original-invoice link + reason + 9B target. The
    /// table lands in the S2a schema but stays empty until the S2b CDN engine (ER-13).</summary>
    public IReadOnlyList<GstCreditDebitNoteLink> CreditDebitNoteLinks => _cdnLinks;

    /// <summary>GST-on-advance receipts (Phase 9; RQ-25): the 11A/11B source records. The table lands in the S2a
    /// schema but stays empty until the S2b advance engine (ER-13).</summary>
    public IReadOnlyList<GstAdvanceReceipt> AdvanceReceipts => _advanceReceipts;

    /// <summary>Imported GSTR-2B/2A statements (Phase 9 slice 6; RQ-12): immutable dated snapshots, each owning its
    /// inward-supply lines. External portal data (NOT the app's own postings). Empty when 2B is never imported (ER-13).</summary>
    public IReadOnlyList<Gstr2bSnapshot> Gstr2bSnapshots => _gstr2bSnapshots;

    /// <summary>Persisted GSTR-2B reconciliation results (Phase 9 slice 6; RQ-13): one per reconciled 2B line (the three
    /// portal-side buckets). ADVISORY only — a read-only pointer to the matched purchase, never a posting (ER-14). Empty
    /// until a reconciliation is run (ER-13).</summary>
    public IReadOnlyList<Gstr2bReconResult> Gstr2bReconResults => _gstr2bReconResults;

    /// <summary>Offline IMS decisions (Phase 9 slice 6b; RQ-14): one mutable <see cref="ImsAction"/> per 2B line the user
    /// acted on. A line with no action is <b>deemed-accepted</b> (derived, not stored) so this is empty until the user
    /// acts (ER-13). ADVISORY only — the IMS mirror posts nothing (ER-14).</summary>
    public IReadOnlyList<ImsAction> ImsActions => _imsActions;

    /// <summary>Employee categories (Phase 8 slice 1; RQ-2): the parallel employee classification axis. Empty
    /// unless Payroll is used.</summary>
    public IReadOnlyList<EmployeeCategory> EmployeeCategories => _employeeCategories;

    /// <summary>Employee groups (Phase 8 slice 1; RQ-2): the hierarchical department/division tree.</summary>
    public IReadOnlyList<EmployeeGroup> EmployeeGroups => _employeeGroups;

    /// <summary>Employees (Phase 8 slice 1; RQ-2): the workforce masters, under a group + optional category.</summary>
    public IReadOnlyList<Employee> Employees => _employees;

    /// <summary>Per-employee income-tax declarations (Phase 8 slice 7; RQ-12; Form 12BB): the §80C/§80D/HRA/24(b)/
    /// other-income/previous-employer figures the §192 engine estimates the salary TDS from. Empty until a
    /// declaration is captured (a new-regime employee needs none), so a company without declarations is
    /// byte-identical (ER-13).</summary>
    public IReadOnlyList<TaxDeclaration> TaxDeclarations => _taxDeclarations;

    /// <summary>Payroll units (Phase 8 slice 1; RQ-3): simple + compound units for attendance/production.</summary>
    public IReadOnlyList<PayrollUnit> PayrollUnits => _payrollUnits;

    /// <summary>Attendance/Production types (Phase 8 slice 1; RQ-3): the attendance/leave/production calendar
    /// types, hierarchical.</summary>
    public IReadOnlyList<AttendanceType> AttendanceTypes => _attendanceTypes;

    /// <summary>Pay heads (Phase 8 slice 2; RQ-4): the salary-structure building blocks (earnings/deductions/
    /// contributions) with their calculation type + computation formula. Empty unless Payroll is used.</summary>
    public IReadOnlyList<PayHead> PayHeads => _payHeads;

    /// <summary>Salary structures (Phase 8 slice 2; RQ-5): the dated per-employee / per-group pay-head assignments.</summary>
    public IReadOnlyList<SalaryStructure> SalaryStructures => _salaryStructures;

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

    /// <summary>Adds a batch master (per-item-uniqueness guard lives in <c>BatchService</c>).</summary>
    public void AddBatchMaster(BatchMaster batch) => _batchMasters.Add(batch ?? throw new ArgumentNullException(nameof(batch)));

    /// <summary>Removes a batch master (delete-guards live in <c>BatchService</c>; also used by import roll-back).</summary>
    public bool RemoveBatchMaster(BatchMaster batch) => _batchMasters.Remove(batch);

    /// <summary>Adds a Bill of Materials (per-item-name-uniqueness guard lives in <c>BomService</c>).</summary>
    public void AddBillOfMaterials(BillOfMaterials bom) => _billsOfMaterials.Add(bom ?? throw new ArgumentNullException(nameof(bom)));

    /// <summary>Removes a Bill of Materials (delete-guards live in <c>BomService</c>; also used by import roll-back).</summary>
    public bool RemoveBillOfMaterials(BillOfMaterials bom) => _billsOfMaterials.Remove(bom);

    /// <summary>Adds a price level (uniqueness guard lives in <c>PriceListService</c>).</summary>
    public void AddPriceLevel(PriceLevel level) => _priceLevels.Add(level ?? throw new ArgumentNullException(nameof(level)));

    /// <summary>Removes a price level (delete-guards live in <c>PriceListService</c>; also used by import roll-back).</summary>
    public bool RemovePriceLevel(PriceLevel level) => _priceLevels.Remove(level);

    /// <summary>Adds a dated price-list version (append-only history; guards live in <c>PriceListService</c>).</summary>
    public void AddPriceList(PriceList list) => _priceLists.Add(list ?? throw new ArgumentNullException(nameof(list)));

    /// <summary>Removes a price-list version (used by the transactional import roll-back).</summary>
    public bool RemovePriceList(PriceList list) => _priceLists.Remove(list);

    /// <summary>Adds a reorder-level definition (uniqueness/target guards live in <c>ReorderLevelsService</c>).</summary>
    public void AddReorderDefinition(ReorderDefinition definition) => _reorderDefinitions.Add(definition ?? throw new ArgumentNullException(nameof(definition)));

    /// <summary>Removes a reorder-level definition (delete-guards live in <c>ReorderLevelsService</c>; also used by import roll-back).</summary>
    public bool RemoveReorderDefinition(ReorderDefinition definition) => _reorderDefinitions.Remove(definition);

    /// <summary>Adds a TDS deposit challan (Phase 7 slice 3).</summary>
    public void AddTdsChallan(TdsChallan challan) => _tdsChallans.Add(challan ?? throw new ArgumentNullException(nameof(challan)));

    /// <summary>Removes a TDS deposit challan (delete-guards live in <c>TdsDepositService</c>; also used by import roll-back).</summary>
    public bool RemoveTdsChallan(TdsChallan challan) => _tdsChallans.Remove(challan);

    /// <summary>Finds a TDS challan by its id, or <c>null</c>.</summary>
    public TdsChallan? FindTdsChallan(Guid id) => _tdsChallans.FirstOrDefault(c => c.Id == id);

    /// <summary>Links a challan to the Stat-Payment voucher that booked its deposit (idempotent — a duplicate pair is
    /// ignored).</summary>
    public void LinkChallanToVoucher(Guid challanId, Guid voucherId)
    {
        var link = new ChallanVoucherLink(challanId, voucherId);
        if (!_challanVoucherLinks.Contains(link)) _challanVoucherLinks.Add(link);
    }

    /// <summary>Removes a challan-voucher link (used by import roll-back).</summary>
    public bool RemoveChallanVoucherLink(ChallanVoucherLink link) => _challanVoucherLinks.Remove(link);

    /// <summary>The Stat-Payment voucher ids linked to a given challan (Phase 7 slice 3).</summary>
    public IEnumerable<Guid> VouchersLinkedToChallan(Guid challanId) =>
        _challanVoucherLinks.Where(l => l.ChallanId == challanId).Select(l => l.VoucherId);

    /// <summary>The challan ids linked to a given voucher (Phase 7 slice 3).</summary>
    public IEnumerable<Guid> ChallansLinkedToVoucher(Guid voucherId) =>
        _challanVoucherLinks.Where(l => l.VoucherId == voucherId).Select(l => l.ChallanId);

    /// <summary>Adds a TCS deposit challan (Phase 7 slice 6).</summary>
    public void AddTcsChallan(TcsChallan challan) => _tcsChallans.Add(challan ?? throw new ArgumentNullException(nameof(challan)));

    /// <summary>Removes a TCS deposit challan (delete-guards live in <c>TcsDepositService</c>; also used by import roll-back).</summary>
    public bool RemoveTcsChallan(TcsChallan challan) => _tcsChallans.Remove(challan);

    /// <summary>Finds a TCS challan by its id, or <c>null</c>.</summary>
    public TcsChallan? FindTcsChallan(Guid id) => _tcsChallans.FirstOrDefault(c => c.Id == id);

    /// <summary>Links a TCS challan to the Stat-Payment voucher that booked its deposit (idempotent — a duplicate pair
    /// is ignored).</summary>
    public void LinkTcsChallanToVoucher(Guid challanId, Guid voucherId)
    {
        var link = new ChallanVoucherLink(challanId, voucherId);
        if (!_tcsChallanVoucherLinks.Contains(link)) _tcsChallanVoucherLinks.Add(link);
    }

    /// <summary>Removes a TCS challan-voucher link (used by import roll-back).</summary>
    public bool RemoveTcsChallanVoucherLink(ChallanVoucherLink link) => _tcsChallanVoucherLinks.Remove(link);

    /// <summary>The Stat-Payment voucher ids linked to a given TCS challan (Phase 7 slice 6).</summary>
    public IEnumerable<Guid> VouchersLinkedToTcsChallan(Guid challanId) =>
        _tcsChallanVoucherLinks.Where(l => l.ChallanId == challanId).Select(l => l.VoucherId);

    /// <summary>The TCS challan ids linked to a given voucher (Phase 7 slice 6).</summary>
    public IEnumerable<Guid> TcsChallansLinkedToVoucher(Guid voucherId) =>
        _tcsChallanVoucherLinks.Where(l => l.VoucherId == voucherId).Select(l => l.ChallanId);

    // ---- RCM / §34-CDN / advance records (Phase 9 slice 2; guards live in RcmService / CreditDebitNoteService /
    //      AdvanceReceiptService). The CDN + advance collections land here but stay empty until S2b. ----

    /// <summary>Adds an RCM generated document (Phase 9 slice 2; also used by the store/import rehydration).</summary>
    public void AddRcmDocument(RcmDocument document) => _rcmDocuments.Add(document ?? throw new ArgumentNullException(nameof(document)));
    /// <summary>Removes an RCM generated document (used by an edit / the transactional import roll-back).</summary>
    public bool RemoveRcmDocument(RcmDocument document) => _rcmDocuments.Remove(document);
    /// <summary>Finds an RCM generated document by its id, or <c>null</c>.</summary>
    public RcmDocument? FindRcmDocument(Guid id) => _rcmDocuments.FirstOrDefault(d => d.Id == id);
    /// <summary>The next consecutive per-company RCM self-invoice / payment-voucher series number for a document kind.</summary>
    public int NextRcmDocumentSeries(RcmDocumentKind kind) =>
        (_rcmDocuments.Where(d => d.Kind == kind).Select(d => (int?)d.SeriesNumber).Max() ?? 0) + 1;

    /// <summary>Adds an e-invoice IRP artefact (Phase 9 slice 4a; also used by the store/import rehydration).</summary>
    public void AddEInvoiceRecord(EInvoiceRecord record) => _eInvoiceRecords.Add(record ?? throw new ArgumentNullException(nameof(record)));
    /// <summary>Removes an e-invoice IRP artefact (used by an edit / the transactional import roll-back).</summary>
    public bool RemoveEInvoiceRecord(EInvoiceRecord record) => _eInvoiceRecords.Remove(record);
    /// <summary>Finds an e-invoice IRP artefact by its id, or <c>null</c>.</summary>
    public EInvoiceRecord? FindEInvoiceRecord(Guid id) => _eInvoiceRecords.FirstOrDefault(r => r.Id == id);
    /// <summary>The e-invoice IRP artefact for a given source voucher (at most one per covered voucher), or <c>null</c>.</summary>
    public EInvoiceRecord? FindEInvoiceRecordForVoucher(Guid sourceVoucherId) =>
        _eInvoiceRecords.FirstOrDefault(r => r.SourceVoucherId == sourceVoucherId);
    /// <summary>True iff any e-invoice record (even Cancelled) already carries the given uppercased document number — a
    /// cancelled/used doc-no is NEVER reusable (§2.5), so <c>EInvoiceService.PrepareRecord</c> refuses a second record.</summary>
    public bool HasEInvoiceDocumentNumber(string documentNumberUpper) =>
        _eInvoiceRecords.Any(r => string.Equals(r.DocumentNumberUpper, documentNumberUpper, StringComparison.Ordinal));

    /// <summary>Adds an e-Way Bill artefact (Phase 9 slice 5; also used by the store/import rehydration).</summary>
    public void AddEWayBillRecord(EWayBillRecord record) => _eWayBillRecords.Add(record ?? throw new ArgumentNullException(nameof(record)));
    /// <summary>Removes an e-Way Bill artefact (used by an edit / the transactional import roll-back).</summary>
    public bool RemoveEWayBillRecord(EWayBillRecord record) => _eWayBillRecords.Remove(record);
    /// <summary>Finds an e-Way Bill artefact by its id, or <c>null</c>.</summary>
    public EWayBillRecord? FindEWayBillRecord(Guid id) => _eWayBillRecords.FirstOrDefault(r => r.Id == id);
    /// <summary>The e-Way Bill artefact for a given source voucher (at most one active per movement), or <c>null</c>. Unlike
    /// e-invoice there is NO doc-no reuse-block — a cancelled EWB frees the movement to be re-billed.</summary>
    public EWayBillRecord? FindEWayBillRecordForVoucher(Guid sourceVoucherId) =>
        _eWayBillRecords.FirstOrDefault(r => r.SourceVoucherId == sourceVoucherId && r.Status != EWayStatus.Cancelled);

    /// <summary>Adds a §34 credit/debit-note link (Phase 9; also used by the store/import rehydration).</summary>
    public void AddCreditDebitNoteLink(GstCreditDebitNoteLink link) => _cdnLinks.Add(link ?? throw new ArgumentNullException(nameof(link)));
    /// <summary>Removes a §34 credit/debit-note link (used by an edit / the transactional import roll-back).</summary>
    public bool RemoveCreditDebitNoteLink(GstCreditDebitNoteLink link) => _cdnLinks.Remove(link);
    /// <summary>Finds a §34 credit/debit-note link by its id, or <c>null</c>.</summary>
    public GstCreditDebitNoteLink? FindCreditDebitNoteLink(Guid id) => _cdnLinks.FirstOrDefault(l => l.Id == id);

    /// <summary>Adds a GST-on-advance receipt record (Phase 9; also used by the store/import rehydration).</summary>
    public void AddAdvanceReceipt(GstAdvanceReceipt receipt) => _advanceReceipts.Add(receipt ?? throw new ArgumentNullException(nameof(receipt)));
    /// <summary>Removes a GST-on-advance receipt record (used by an edit / the transactional import roll-back).</summary>
    public bool RemoveAdvanceReceipt(GstAdvanceReceipt receipt) => _advanceReceipts.Remove(receipt);
    /// <summary>Finds a GST-on-advance receipt record by its id, or <c>null</c>.</summary>
    public GstAdvanceReceipt? FindAdvanceReceipt(Guid id) => _advanceReceipts.FirstOrDefault(a => a.Id == id);

    // ---- GSTR-2B/2A imported statements + reconciliation results (Phase 9 slice 6; guards live in Gstr2bImportService /
    //      Gstr2bReconciler). Both are staging collections physically separate from the ledger (ER-14). ----

    /// <summary>Adds an imported GSTR-2B/2A snapshot (Phase 9 slice 6; also used by the store/import rehydration).</summary>
    public void AddGstr2bSnapshot(Gstr2bSnapshot snapshot) => _gstr2bSnapshots.Add(snapshot ?? throw new ArgumentNullException(nameof(snapshot)));
    /// <summary>Removes an imported GSTR-2B/2A snapshot (used by an edit / the transactional import roll-back).</summary>
    public bool RemoveGstr2bSnapshot(Gstr2bSnapshot snapshot) => _gstr2bSnapshots.Remove(snapshot);
    /// <summary>Finds an imported GSTR-2B/2A snapshot by its id, or <c>null</c>.</summary>
    public Gstr2bSnapshot? FindGstr2bSnapshot(Guid id) => _gstr2bSnapshots.FirstOrDefault(s => s.Id == id);
    /// <summary>Finds the imported 2B line with the given id across every snapshot, or <c>null</c>.</summary>
    public Gstr2bLine? FindGstr2bLine(Guid lineId)
    {
        foreach (var s in _gstr2bSnapshots)
            if (s.FindLine(lineId) is { } line) return line;
        return null;
    }

    /// <summary>Adds a persisted reconciliation result (Phase 9 slice 6; also used by the store/import rehydration).</summary>
    public void AddGstr2bReconResult(Gstr2bReconResult result) => _gstr2bReconResults.Add(result ?? throw new ArgumentNullException(nameof(result)));
    /// <summary>Removes a persisted reconciliation result (used by a re-run / the transactional import roll-back).</summary>
    public bool RemoveGstr2bReconResult(Gstr2bReconResult result) => _gstr2bReconResults.Remove(result);
    /// <summary>Finds a persisted reconciliation result by its id, or <c>null</c>.</summary>
    public Gstr2bReconResult? FindGstr2bReconResult(Guid id) => _gstr2bReconResults.FirstOrDefault(r => r.Id == id);

    /// <summary>Adds an offline IMS decision (Phase 9 slice 6b; guards live in <c>ImsService</c>; also used by the
    /// store/import rehydration).</summary>
    public void AddImsAction(ImsAction action) => _imsActions.Add(action ?? throw new ArgumentNullException(nameof(action)));
    /// <summary>Removes an offline IMS decision (a Clear/re-decide, or the transactional import roll-back).</summary>
    public bool RemoveImsAction(ImsAction action) => _imsActions.Remove(action);
    /// <summary>Finds an IMS decision by its id, or <c>null</c>.</summary>
    public ImsAction? FindImsAction(Guid id) => _imsActions.FirstOrDefault(a => a.Id == id);
    /// <summary>Finds the IMS decision for a given 2B line, or <c>null</c> (⇒ the line is deemed-accepted).</summary>
    public ImsAction? FindImsActionForLine(Guid lineId) => _imsActions.FirstOrDefault(a => a.LineId == lineId);

    // ---- Payroll masters (Phase 8 slice 1; guards live in PayrollService) ----

    /// <summary>Adds an employee category (uniqueness guard lives in <c>PayrollService</c>).</summary>
    public void AddEmployeeCategory(EmployeeCategory category) => _employeeCategories.Add(category ?? throw new ArgumentNullException(nameof(category)));
    /// <summary>Removes an employee category (delete-guards live in <c>PayrollService</c>; also used by import roll-back).</summary>
    public bool RemoveEmployeeCategory(EmployeeCategory category) => _employeeCategories.Remove(category);

    /// <summary>Adds an employee group (uniqueness/parent guards live in <c>PayrollService</c>).</summary>
    public void AddEmployeeGroup(EmployeeGroup group) => _employeeGroups.Add(group ?? throw new ArgumentNullException(nameof(group)));
    /// <summary>Removes an employee group (delete-guards live in <c>PayrollService</c>; also used by import roll-back).</summary>
    public bool RemoveEmployeeGroup(EmployeeGroup group) => _employeeGroups.Remove(group);

    /// <summary>Adds an employee (uniqueness/reference guards live in <c>PayrollService</c>).</summary>
    public void AddEmployee(Employee employee) => _employees.Add(employee ?? throw new ArgumentNullException(nameof(employee)));
    /// <summary>Removes an employee (delete-guards live in <c>PayrollService</c>; also used by import roll-back).</summary>
    public bool RemoveEmployee(Employee employee) => _employees.Remove(employee);

    /// <summary>Adds a per-employee income-tax declaration (Phase 8 slice 7; also used by the store/import
    /// rehydration). Replaces any existing declaration for the same employee (one declaration per employee).</summary>
    public void AddTaxDeclaration(TaxDeclaration declaration)
    {
        ArgumentNullException.ThrowIfNull(declaration);
        _taxDeclarations.RemoveAll(d => d.EmployeeId == declaration.EmployeeId);
        _taxDeclarations.Add(declaration);
    }
    /// <summary>Removes a tax declaration (used by an edit / the transactional import roll-back).</summary>
    public bool RemoveTaxDeclaration(TaxDeclaration declaration) => _taxDeclarations.Remove(declaration);
    /// <summary>Finds an employee's income-tax declaration (Phase 8 slice 7), or <c>null</c> when none is captured
    /// (⇒ the §192 engine treats every declared figure as ₹0 — correct for a new-regime employee).</summary>
    public TaxDeclaration? FindTaxDeclaration(Guid employeeId) =>
        _taxDeclarations.FirstOrDefault(d => d.EmployeeId == employeeId);

    /// <summary>Adds a payroll unit (uniqueness guard lives in <c>PayrollService</c>).</summary>
    public void AddPayrollUnit(PayrollUnit unit) => _payrollUnits.Add(unit ?? throw new ArgumentNullException(nameof(unit)));
    /// <summary>Removes a payroll unit (delete-guards live in <c>PayrollService</c>; also used by import roll-back).</summary>
    public bool RemovePayrollUnit(PayrollUnit unit) => _payrollUnits.Remove(unit);

    /// <summary>Adds an attendance/production type (uniqueness/parent guards live in <c>PayrollService</c>).</summary>
    public void AddAttendanceType(AttendanceType type) => _attendanceTypes.Add(type ?? throw new ArgumentNullException(nameof(type)));
    /// <summary>Removes an attendance/production type (delete-guards live in <c>PayrollService</c>; also used by import roll-back).</summary>
    public bool RemoveAttendanceType(AttendanceType type) => _attendanceTypes.Remove(type);

    /// <summary>Finds an employee category by its id, or <c>null</c>.</summary>
    public EmployeeCategory? FindEmployeeCategory(Guid id) => _employeeCategories.FirstOrDefault(c => c.Id == id);
    /// <summary>Finds an employee category by its name (case-insensitive), or <c>null</c>.</summary>
    public EmployeeCategory? FindEmployeeCategoryByName(string name) =>
        _employeeCategories.FirstOrDefault(c => string.Equals(c.Name, name?.Trim(), StringComparison.OrdinalIgnoreCase));

    /// <summary>Finds an employee group by its id, or <c>null</c>.</summary>
    public EmployeeGroup? FindEmployeeGroup(Guid id) => _employeeGroups.FirstOrDefault(g => g.Id == id);
    /// <summary>Finds an employee group by its name or alias (case-insensitive), or <c>null</c>.</summary>
    public EmployeeGroup? FindEmployeeGroupByName(string name) =>
        _employeeGroups.FirstOrDefault(g =>
            string.Equals(g.Name, name?.Trim(), StringComparison.OrdinalIgnoreCase) ||
            (g.Alias is not null && string.Equals(g.Alias, name?.Trim(), StringComparison.OrdinalIgnoreCase)));

    /// <summary>Finds an employee by its id, or <c>null</c>.</summary>
    public Employee? FindEmployee(Guid id) => _employees.FirstOrDefault(e => e.Id == id);
    /// <summary>Finds an employee by its name (case-insensitive), or <c>null</c>.</summary>
    public Employee? FindEmployeeByName(string name) =>
        _employees.FirstOrDefault(e => string.Equals(e.Name, name?.Trim(), StringComparison.OrdinalIgnoreCase));

    /// <summary>Finds a payroll unit by its id, or <c>null</c>.</summary>
    public PayrollUnit? FindPayrollUnit(Guid id) => _payrollUnits.FirstOrDefault(u => u.Id == id);
    /// <summary>Finds a payroll unit by its symbol or formal name (case-insensitive), or <c>null</c>.</summary>
    public PayrollUnit? FindPayrollUnitByName(string name) =>
        _payrollUnits.FirstOrDefault(u =>
            string.Equals(u.Symbol, name?.Trim(), StringComparison.OrdinalIgnoreCase) ||
            string.Equals(u.FormalName, name?.Trim(), StringComparison.OrdinalIgnoreCase));

    /// <summary>Finds an attendance type by its id, or <c>null</c>.</summary>
    public AttendanceType? FindAttendanceType(Guid id) => _attendanceTypes.FirstOrDefault(a => a.Id == id);
    /// <summary>Finds an attendance type by its name (case-insensitive), or <c>null</c>.</summary>
    public AttendanceType? FindAttendanceTypeByName(string name) =>
        _attendanceTypes.FirstOrDefault(a => string.Equals(a.Name, name?.Trim(), StringComparison.OrdinalIgnoreCase));

    // ---- Pay heads + salary structures (Phase 8 slice 2; guards live in PayHeadService / SalaryStructureService) ----

    /// <summary>Adds a pay head (uniqueness/reference/cycle guards live in <c>PayHeadService</c>).</summary>
    public void AddPayHead(PayHead payHead) => _payHeads.Add(payHead ?? throw new ArgumentNullException(nameof(payHead)));
    /// <summary>Removes a pay head (delete-guards live in <c>PayHeadService</c>; also used by import roll-back).</summary>
    public bool RemovePayHead(PayHead payHead) => _payHeads.Remove(payHead);
    /// <summary>Finds a pay head by its id, or <c>null</c>.</summary>
    public PayHead? FindPayHead(Guid id) => _payHeads.FirstOrDefault(p => p.Id == id);
    /// <summary>Finds a pay head by its name (case-insensitive), or <c>null</c>.</summary>
    public PayHead? FindPayHeadByName(string name) =>
        _payHeads.FirstOrDefault(p => string.Equals(p.Name, name?.Trim(), StringComparison.OrdinalIgnoreCase));

    /// <summary>Adds a salary structure (validity/uniqueness guards live in <c>SalaryStructureService</c>).</summary>
    public void AddSalaryStructure(SalaryStructure structure) => _salaryStructures.Add(structure ?? throw new ArgumentNullException(nameof(structure)));
    /// <summary>Removes a salary structure (delete-guards live in <c>SalaryStructureService</c>; also used by import roll-back).</summary>
    public bool RemoveSalaryStructure(SalaryStructure structure) => _salaryStructures.Remove(structure);
    /// <summary>Finds a salary structure by its id, or <c>null</c>.</summary>
    public SalaryStructure? FindSalaryStructure(Guid id) => _salaryStructures.FirstOrDefault(s => s.Id == id);

    // ---- Attendance entries (Phase 8 slice 3; recorded by PayrollAttendanceService, read by the computation engine) ----

    /// <summary>Recorded attendance / production values (Phase 8 slice 3; RQ-6). Empty unless Payroll is used (ER-13).</summary>
    public IReadOnlyList<AttendanceEntry> AttendanceEntries => _attendanceEntries;

    /// <summary>Adds an attendance entry (guards live in <c>PayrollAttendanceService</c>; also used by import).</summary>
    public void AddAttendanceEntry(AttendanceEntry entry) => _attendanceEntries.Add(entry ?? throw new ArgumentNullException(nameof(entry)));
    /// <summary>Removes an attendance entry (also used by import roll-back).</summary>
    public bool RemoveAttendanceEntry(AttendanceEntry entry) => _attendanceEntries.Remove(entry);
    /// <summary>Finds an attendance entry by its id, or <c>null</c>.</summary>
    public AttendanceEntry? FindAttendanceEntry(Guid id) => _attendanceEntries.FirstOrDefault(a => a.Id == id);

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

    // ---- Master removal (used by the import roll-back so a rejected batch leaves no partial masters, RQ-23).
    //      Delete-guards for interactive Alter/Delete live in the services; these are the raw list removals the
    //      transactional importer needs to undo what it added within a single failed apply. ----

    /// <summary>Removes a group (used by the transactional import roll-back).</summary>
    public bool RemoveGroup(Group group) => _groups.Remove(group);
    /// <summary>Removes a ledger (used by the transactional import roll-back).</summary>
    public bool RemoveLedger(Ledger ledger) => _ledgers.Remove(ledger);
    /// <summary>Removes a voucher type (used by the transactional import roll-back).</summary>
    public bool RemoveVoucherType(VoucherType type) => _voucherTypes.Remove(type);
    /// <summary>Removes a posted voucher (used by the transactional import roll-back).</summary>
    public bool RemoveVoucher(Voucher voucher) => _vouchers.Remove(voucher);
    /// <summary>Removes a currency (used by the transactional import roll-back).</summary>
    public bool RemoveCurrency(Currency currency) => _currencies.Remove(currency);
    /// <summary>Removes a dated exchange-rate quote (used by the transactional import roll-back).</summary>
    public bool RemoveExchangeRate(ExchangeRate rate) => _exchangeRates.Remove(rate);
    /// <summary>Removes a cost category (used by the transactional import roll-back).</summary>
    public bool RemoveCostCategory(CostCategory category) => _costCategories.Remove(category);
    /// <summary>Removes a cost centre (used by the transactional import roll-back).</summary>
    public bool RemoveCostCentre(CostCentre centre) => _costCentres.Remove(centre);
    /// <summary>Removes a budget (used by the transactional import roll-back).</summary>
    public bool RemoveBudget(Budget budget) => _budgets.Remove(budget);
    /// <summary>Removes a scenario (used by the transactional import roll-back).</summary>
    public bool RemoveScenario(Scenario scenario) => _scenarios.Remove(scenario);
    /// <summary>Removes a stock/order voucher (used by the transactional import roll-back).</summary>
    public bool RemoveInventoryVoucher(InventoryVoucher voucher) => _inventoryVouchers.Remove(voucher);

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
    public BatchMaster? FindBatchMaster(Guid id) => _batchMasters.FirstOrDefault(b => b.Id == id);

    /// <summary>All batch masters that belong to a given stock item (RQ-1).</summary>
    public IEnumerable<BatchMaster> BatchesFor(Guid stockItemId) => _batchMasters.Where(b => b.StockItemId == stockItemId);

    /// <summary>
    /// Finds an item's batch by its number (case-insensitive), or <c>null</c>. Batch numbers are unique
    /// <i>within</i> an item (RQ-1), so this resolves at most one batch per item.
    /// </summary>
    public BatchMaster? FindBatchByNumber(Guid stockItemId, string batchNumber) =>
        _batchMasters.FirstOrDefault(b => b.StockItemId == stockItemId &&
            string.Equals(b.BatchNumber, batchNumber?.Trim(), StringComparison.OrdinalIgnoreCase));

    /// <summary>Finds a Bill of Materials by its id, or <c>null</c>.</summary>
    public BillOfMaterials? FindBillOfMaterials(Guid id) => _billsOfMaterials.FirstOrDefault(b => b.Id == id);

    /// <summary>All Bills of Materials that belong to a given finished-good stock item (RQ-9).</summary>
    public IEnumerable<BillOfMaterials> BomsFor(Guid stockItemId) =>
        _billsOfMaterials.Where(b => b.StockItemId == stockItemId);

    /// <summary>
    /// Finds a finished good's BOM by its name (case-insensitive), or <c>null</c>. BOM names are unique
    /// <i>within</i> a finished good (RQ-9), so this resolves at most one BOM per item.
    /// </summary>
    public BillOfMaterials? FindBomByName(Guid stockItemId, string name) =>
        _billsOfMaterials.FirstOrDefault(b => b.StockItemId == stockItemId &&
            string.Equals(b.Name, name?.Trim(), StringComparison.OrdinalIgnoreCase));

    /// <summary>Finds a price level by its id, or <c>null</c> (Phase 6 slice 5; RQ-26).</summary>
    public PriceLevel? FindPriceLevel(Guid id) => _priceLevels.FirstOrDefault(l => l.Id == id);

    /// <summary>Finds a price level by its name (case-insensitive), or <c>null</c> (RQ-26).</summary>
    public PriceLevel? FindPriceLevelByName(string name) =>
        _priceLevels.FirstOrDefault(l => string.Equals(l.Name, name?.Trim(), StringComparison.OrdinalIgnoreCase));

    /// <summary>All dated price-list versions for a given (level, item) — the append-only history the resolver
    /// picks the latest-applicable version from (RQ-27/RQ-29).</summary>
    public IEnumerable<PriceList> PriceListsFor(Guid priceLevelId, Guid stockItemId) =>
        _priceLists.Where(pl => pl.PriceLevelId == priceLevelId && pl.StockItemId == stockItemId);

    /// <summary>Finds a reorder-level definition by its id, or <c>null</c> (Phase 6 slice 6; RQ-32).</summary>
    public ReorderDefinition? FindReorderDefinition(Guid id) => _reorderDefinitions.FirstOrDefault(d => d.Id == id);

    /// <summary>Finds the single reorder-level definition for a (scope, target), or <c>null</c> — at most one
    /// definition exists per (scope, target), enforced by <c>ReorderLevelsService</c> + the unique index (RQ-32).</summary>
    public ReorderDefinition? FindReorderDefinition(ReorderScope scope, Guid targetId) =>
        _reorderDefinitions.FirstOrDefault(d => d.Scope == scope && d.TargetId == targetId);

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

    /// <summary>
    /// Like <see cref="FindGroupByName"/> but also matches the reserved <see cref="ProfitAndLossHead"/> (which is
    /// stored outside the 28 <see cref="Groups"/>). Import uses this so the seeded P&amp;L head is reused by name
    /// rather than re-created as a 29th group; the report/classification callers keep the head-excluding
    /// <see cref="FindGroupByName"/>.
    /// </summary>
    public Group? FindGroupOrHeadByName(string name) =>
        FindGroupByName(name)
        ?? (ProfitAndLossHead is { } pl &&
            (string.Equals(pl.Name, name, StringComparison.OrdinalIgnoreCase) ||
             (pl.Alias is not null && string.Equals(pl.Alias, name, StringComparison.OrdinalIgnoreCase)))
            ? pl : null);

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
