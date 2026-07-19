using Apex.Ledger.Domain;

namespace Apex.Ledger.Io;

/// <summary>
/// The versioned, framework-agnostic DTO that mirrors a <see cref="Company"/>'s data for the canonical
/// lossless round-trip (RQ-19). It is the single in-memory shape both <see cref="CanonicalJson"/> and
/// <see cref="CanonicalXml"/> serialise to / parse from, so JSON and XML carry the <b>identical</b> payload.
/// <para>
/// <b>Money is integer paisa.</b> Every monetary field is a <see cref="long"/> paisa value (rupees × 100),
/// which is exact and unambiguous on the wire (RQ-19 "integer-paisa"); the engine stage converts back to
/// <see cref="Money"/> via <see cref="MoneyCodec.FromPaisa"/>. Quantities and rates that are not
/// <see cref="Money"/> (stock quantities, exchange factors) stay <see cref="decimal"/> serialised
/// culture-invariantly.
/// </para>
/// <para>
/// <b>Ids are the stable keys.</b> Masters and vouchers carry their domain <see cref="System.Guid"/> ids and
/// reference each other by id (ledger→group, item→stock-group/unit, voucher-line→ledger, …), exactly as the
/// in-memory aggregate does. The engine stage re-creates masters preserving these ids (or, for the fresh-import
/// round-trip, re-maps names → new ids) — see the notes returned with the slice.
/// </para>
/// This DTO is deliberately a flat set of records with no behaviour: no clock, no RNG, no Avalonia, no NuGet.
/// </summary>
public sealed record CanonicalModel
{
    /// <summary>The bump-on-shape-change canonical format version (envelope, not the DB schema).</summary>
    public required int FormatVersion { get; init; }

    /// <summary>The persistence schema version the export was produced against (v14 at time of writing).</summary>
    public required int SchemaVersion { get; init; }

    /// <summary>The company header (identity + books/currency settings).</summary>
    public required CompanyDto Company { get; init; }

    /// <summary>The company data: masters + vouchers.</summary>
    public required PayloadDto Payload { get; init; }
}

/// <summary>Company header fields needed to reconstruct the <see cref="Company"/> shell.</summary>
public sealed record CompanyDto
{
    public required Guid Id { get; init; }
    public required string Name { get; init; }
    public string? MailingName { get; init; }
    public string? Address { get; init; }
    public string? Country { get; init; }
    public string? State { get; init; }
    public string? Pin { get; init; }

    /// <summary>ISO yyyy-MM-dd.</summary>
    public required string FinancialYearStart { get; init; }

    /// <summary>ISO yyyy-MM-dd.</summary>
    public required string BooksBeginFrom { get; init; }

    public string? BaseCurrencySymbol { get; init; }
    public string? BaseCurrencyName { get; init; }
    public int DecimalPlaces { get; init; }
    public string? DecimalUnitName { get; init; }

    /// <summary>Company GST config, or <c>null</c> when GST is off (the default).</summary>
    public GstConfigDto? Gst { get; init; }

    /// <summary>Company TDS deductor config (Phase 7 slice 1), or <c>null</c> when TDS is off (the default).</summary>
    public TdsConfigDto? Tds { get; init; }

    /// <summary>Company TCS collector config (Phase 7 slice 1), or <c>null</c> when TCS is off (the default).</summary>
    public TcsConfigDto? Tcs { get; init; }

    // ---- Phase 6 persisted company feature toggles (real companies columns; cannot be inferred). ----

    /// <summary>F11 "Use separate Actual &amp; Billed Quantity columns" (Phase 6 slice 4; RQ-22; DP-7). Default false.</summary>
    public bool UseSeparateActualBilledQuantity { get; init; }

    /// <summary>F11 "Enable multiple Price Levels" (Phase 6 slice 5; RQ-26). Default false.</summary>
    public bool EnableMultiplePriceLevels { get; init; }

    /// <summary>F11 "Enable Job Order Processing" (Phase 6 slice 8; RQ-45). Default false.</summary>
    public bool EnableJobOrderProcessing { get; init; }

    /// <summary>F11 "Maintain Payroll" (Phase 8 slice 1; RQ-1). Default false.</summary>
    public bool PayrollEnabled { get; init; }

    /// <summary>F11 "Enable Payroll Statutory" (Phase 8 slice 1; RQ-1). Default false.</summary>
    public bool PayrollStatutoryEnabled { get; init; }

    /// <summary>§192 salary-TDS toggle (Phase 8 slice 7; RQ-12). Default false, so a company that never deducts
    /// salary-TDS is byte-identical (ER-13). The per-employee Form-12BB declarations live in the payload's
    /// <c>TaxDeclarations</c>.</summary>
    public bool SalaryTdsEnabled { get; init; }

    /// <summary>The establishment Provident-Fund config (Phase 8 slice 4); null when the establishment is not
    /// enrolled for PF (a PF-off company is byte-identical — ER-13).</summary>
    public PfConfigDto? Pf { get; init; }

    /// <summary>The establishment Employees'-State-Insurance config (Phase 8 slice 5); null when the establishment is
    /// not enrolled for ESI (an ESI-off company is byte-identical — ER-13).</summary>
    public EsiConfigDto? Esi { get; init; }

    /// <summary>The establishment Professional-Tax config (Phase 8 slice 6); null when the establishment is not
    /// enrolled for PT (a PT-off company is byte-identical — ER-13).</summary>
    public PtConfigDto? Pt { get; init; }

    /// <summary>The establishment Gratuity config (Phase 8 slice 9); null when the establishment does not provision for
    /// gratuity (a gratuity-off company is byte-identical — ER-13).</summary>
    public GratuityConfigDto? Gratuity { get; init; }

    /// <summary>The establishment statutory-Bonus config (Phase 8 slice 9); null when the establishment does not
    /// compute statutory bonus (a bonus-off company is byte-identical — ER-13).</summary>
    public BonusConfigDto? Bonus { get; init; }
}

/// <summary>The company Provident-Fund config (Phase 8 slice 4), mirroring the domain <c>PfConfig</c>.</summary>
public sealed record PfConfigDto
{
    public int EpfRateBasisPoints { get; init; } = 1200;
    public string? EstablishmentCode { get; init; }
    public bool CapWagesAtCeiling { get; init; } = true;
}

/// <summary>The company Employees'-State-Insurance config (Phase 8 slice 5), mirroring the domain <c>EsiConfig</c>.</summary>
public sealed record EsiConfigDto
{
    public int EmployeeRateBasisPoints { get; init; } = 75;
    public int EmployerRateBasisPoints { get; init; } = 325;
    public string? EmployerCode { get; init; }
}

/// <summary>The company Professional-Tax config (Phase 8 slice 6), mirroring the domain <c>PtConfig</c> — the active
/// state, registration number, wage basis and the editable per-state slab tables.</summary>
public sealed record PtConfigDto
{
    /// <summary>The active PT state (2-digit GST state code), or null = "None" (no PT levied).</summary>
    public string? StateCode { get; init; }

    /// <summary>The PT enrolment/registration number, or null.</summary>
    public string? RegistrationNumber { get; init; }

    /// <summary>The wage basis a slab is selected against (<c>PtWageBasis</c> name); default "GrossEarnings".</summary>
    public string WageBasis { get; init; } = nameof(Apex.Ledger.Domain.PtWageBasis.GrossEarnings);

    /// <summary>The per-state slab tables (order-preserved).</summary>
    public IReadOnlyList<PtSlabDto> SlabTables { get; init; } = [];
}

/// <summary>One PT state slab table (Phase 8 slice 6), mirroring the domain <c>PtSlab</c>.</summary>
public sealed record PtSlabDto
{
    public Guid Id { get; init; }

    /// <summary>The 2-digit GST state code the table belongs to.</summary>
    public required string StateCode { get; init; }

    /// <summary>The gender scope (<c>PtGenderScope</c> name); "Any" for a gender-agnostic state.</summary>
    public string GenderScope { get; init; } = nameof(Apex.Ledger.Domain.PtGenderScope.Any);

    /// <summary>The bands, low-to-high (order-preserved).</summary>
    public IReadOnlyList<PtSlabBandDto> Bands { get; init; } = [];
}

/// <summary>One PT slab band (Phase 8 slice 6), mirroring the domain <c>PtSlabBand</c>. Money is integer paisa.</summary>
public sealed record PtSlabBandDto
{
    /// <summary>The inclusive lower bound of the monthly PT-wage band, in paisa.</summary>
    public long FromWagePaisa { get; init; }

    /// <summary>The inclusive upper bound, in paisa; null = open-ended top band (∞).</summary>
    public long? ToWagePaisa { get; init; }

    /// <summary>The flat PT amount for the band, in paisa (before any month override).</summary>
    public long MonthlyAmountPaisa { get; init; }

    /// <summary>Per-month overrides (order-preserved), e.g. a single February over-charge.</summary>
    public IReadOnlyList<PtMonthOverrideDto> MonthOverrides { get; init; } = [];
}

/// <summary>One PT per-month override (Phase 8 slice 6), mirroring the domain <c>PtMonthOverride</c>.</summary>
public sealed record PtMonthOverrideDto
{
    /// <summary>The calendar month (1–12) the override applies to.</summary>
    public int Month { get; init; }

    /// <summary>The PT amount charged in that month, in paisa.</summary>
    public long AmountPaisa { get; init; }
}

/// <summary>The company Gratuity config (Phase 8 slice 9), mirroring the domain <c>GratuityConfig</c>. Money is integer
/// paisa.</summary>
public sealed record GratuityConfigDto
{
    /// <summary>The §4(3) cap in paisa (default 200000000 = ₹20,00,000).</summary>
    public long CapPaisa { get; init; } = 200_000_000L;

    /// <summary>The wage basis (<c>GratuityWageBasis</c> name); default "BasicAndDearnessAllowance".</summary>
    public string WageBasis { get; init; } = nameof(Apex.Ledger.Domain.GratuityWageBasis.BasicAndDearnessAllowance);

    /// <summary>The provision population (<c>GratuityProvisionPopulation</c> name); default "AllActiveEmployees".</summary>
    public string Population { get; init; } = nameof(Apex.Ledger.Domain.GratuityProvisionPopulation.AllActiveEmployees);
}

/// <summary>The company statutory-Bonus config (Phase 8 slice 9), mirroring the domain <c>BonusConfig</c>. Money is
/// integer paisa.</summary>
public sealed record BonusConfigDto
{
    /// <summary>The bonus rate in basis points (default 833 = 8.33%); clamped to [833, 2000] on rehydration.</summary>
    public int RateBasisPoints { get; init; } = 833;

    /// <summary>The §12 calculation ceiling in paisa (default 700000 = ₹7,000).</summary>
    public long CalculationCeilingPaisa { get; init; } = 700_000L;

    /// <summary>The state minimum wage in paisa (default 0).</summary>
    public long MinimumWagePaisa { get; init; }

    /// <summary>Whether a mid-year joiner's bonus is prorated by months worked (default true).</summary>
    public bool Prorate { get; init; } = true;
}

/// <summary>The masters + vouchers payload. Every list is deterministically ordered on export.</summary>
public sealed record PayloadDto
{
    public IReadOnlyList<GroupDto> Groups { get; init; } = [];
    public IReadOnlyList<LedgerDto> Ledgers { get; init; } = [];
    public IReadOnlyList<VoucherTypeDto> VoucherTypes { get; init; } = [];

    // Cost accounting (catalog §6).
    public IReadOnlyList<CostCategoryDto> CostCategories { get; init; } = [];
    public IReadOnlyList<CostCentreDto> CostCentres { get; init; } = [];

    // Multi-currency (catalog §2/§20).
    public IReadOnlyList<CurrencyDto> Currencies { get; init; } = [];
    public IReadOnlyList<ExchangeRateDto> ExchangeRates { get; init; } = [];

    // Budgets & scenarios (catalog §7).
    public IReadOnlyList<BudgetDto> Budgets { get; init; } = [];
    public IReadOnlyList<ScenarioDto> Scenarios { get; init; } = [];

    public IReadOnlyList<UnitDto> Units { get; init; } = [];
    public IReadOnlyList<StockGroupDto> StockGroups { get; init; } = [];
    public IReadOnlyList<StockCategoryDto> StockCategories { get; init; } = [];
    public IReadOnlyList<GodownDto> Godowns { get; init; } = [];
    public IReadOnlyList<StockItemDto> StockItems { get; init; } = [];

    // Batch tracking (Phase 6 Cluster 1; catalog §9): first-class batch/lot masters per stock item.
    public IReadOnlyList<BatchMasterDto> BatchMasters { get; init; } = [];

    // Bill of Materials (Phase 6 Cluster 2; catalog §11): named manufacturing recipes on a finished-good item.
    public IReadOnlyList<BillOfMaterialsDto> BillsOfMaterials { get; init; } = [];

    public IReadOnlyList<StockOpeningBalanceDto> StockOpeningBalances { get; init; } = [];

    // Price Levels / Price Lists (Phase 6 slice 5; catalog §11): named rate tiers + dated slab-rate history.
    public IReadOnlyList<PriceLevelDto> PriceLevels { get; init; } = [];
    public IReadOnlyList<PriceListDto> PriceLists { get; init; } = [];

    // Reorder-Level definitions (Phase 6 slice 6; catalog §11): per item / group / category.
    public IReadOnlyList<ReorderDefinitionDto> ReorderDefinitions { get; init; } = [];

    // Payroll masters (Phase 8 slice 1; catalog §14): employee classification, hierarchy, units, attendance types
    // and the workforce. Categories/groups/units precede attendance types (unit refs) + employees (group/category refs).
    public IReadOnlyList<EmployeeCategoryDto> EmployeeCategories { get; init; } = [];
    public IReadOnlyList<EmployeeGroupDto> EmployeeGroups { get; init; } = [];
    public IReadOnlyList<PayrollUnitDto> PayrollUnits { get; init; } = [];
    public IReadOnlyList<AttendanceTypeDto> AttendanceTypes { get; init; } = [];
    public IReadOnlyList<EmployeeDto> Employees { get; init; } = [];

    // Pay heads + dated salary structures (Phase 8 slice 2; catalog §14). Pay heads precede structures (lines FK
    // pay heads); a pay head's computed-on components reference other pay heads (resolved on import).
    public IReadOnlyList<PayHeadDto> PayHeads { get; init; } = [];
    public IReadOnlyList<SalaryStructureDto> SalaryStructures { get; init; } = [];

    /// <summary>Recorded attendance/production values (Phase 8 slice 3; RQ-6) — the data of a non-accounting
    /// Attendance voucher. Empty when Payroll is unused (ER-13).</summary>
    public IReadOnlyList<AttendanceEntryDto> AttendanceEntries { get; init; } = [];

    /// <summary>Per-employee §192 income-tax declarations (Phase 8 slice 7; Form 12BB) — the investment / exemption /
    /// prior-income figures the salary-TDS estimate uses. Empty when no employee declared (ER-13).</summary>
    public IReadOnlyList<TaxDeclarationDto> TaxDeclarations { get; init; } = [];

    public IReadOnlyList<VoucherDto> Vouchers { get; init; } = [];

    /// <summary>Stock/order vouchers (catalog §10): GRN/Delivery/Rejection/Stock-Journal/Physical/PO/SO.</summary>
    public IReadOnlyList<InventoryVoucherDto> InventoryVouchers { get; init; } = [];

    /// <summary>TDS deposit challans (Phase 7 slice 3; ITNS-281): one per TDS payment into the bank.</summary>
    public IReadOnlyList<TdsChallanDto> TdsChallans { get; init; } = [];

    /// <summary>Challan ↔ Stat-Payment-voucher links (Phase 7 slice 3): which deposit voucher each challan booked.</summary>
    public IReadOnlyList<ChallanVoucherLinkDto> ChallanVoucherLinks { get; init; } = [];

    /// <summary>TCS deposit challans (Phase 7 slice 6; ITNS-281): one per TCS payment into the bank.</summary>
    public IReadOnlyList<TcsChallanDto> TcsChallans { get; init; } = [];

    /// <summary>TCS challan ↔ Stat-Payment-voucher links (Phase 7 slice 6): which deposit voucher each TCS challan
    /// booked. Reuses <see cref="ChallanVoucherLinkDto"/>, in its own list (a TCS-specific link set).</summary>
    public IReadOnlyList<ChallanVoucherLinkDto> TcsChallanVoucherLinks { get; init; } = [];

    /// <summary>RCM generated documents (Phase 9 slice 2; RQ-8): Rule-47A self-invoices + Rule-52 payment vouchers.
    /// Empty when reverse charge is unused (ER-13).</summary>
    public IReadOnlyList<RcmDocumentDto> RcmDocuments { get; init; } = [];

    /// <summary>e-Invoice IRP artefacts (Phase 9 slice 4a; RQ-5). Empty when e-invoicing is unused (ER-13). The
    /// IRP-signed artefacts (IRN/QR/SignedJson) ARE exported — they are portal-issued receipts, NOT secrets. The NIC API
    /// credentials are the only secret and are deliberately absent from the canonical model (ER-16).</summary>
    public IReadOnlyList<EInvoiceRecordDto> EInvoiceRecords { get; init; } = [];

    /// <summary>e-Way Bill artefacts (Phase 9 slice 5; RQ-6). Empty when e-Way is unused (ER-13). The portal-issued EWB
    /// number + validity ARE exported — they are portal receipts, NOT secrets. No NIC credential appears here (ER-16).</summary>
    public IReadOnlyList<EWayBillRecordDto> EWayBillRecords { get; init; } = [];

    /// <summary>§34 credit/debit-note links (Phase 9; RQ-24). Empty until the S2b CDN engine (ER-13).</summary>
    public IReadOnlyList<GstCdnLinkDto> CreditDebitNoteLinks { get; init; } = [];

    /// <summary>GST-on-advance receipts (Phase 9; RQ-25). Empty until the S2b advance engine (ER-13).</summary>
    public IReadOnlyList<GstAdvanceReceiptDto> AdvanceReceipts { get; init; } = [];

    /// <summary>Imported GSTR-2B/2A statements (Phase 9 slice 6; RQ-12). External portal data the taxpayer downloaded —
    /// worth preserving, so it round-trips. Empty when 2B is never imported (ER-13). No NIC credential appears here — the
    /// only secret flows through <c>INicCredentialStore</c> alone (ER-16).</summary>
    public IReadOnlyList<Gstr2bSnapshotDto> Gstr2bSnapshots { get; init; } = [];

    /// <summary>Persisted GSTR-2B reconciliation results (Phase 9 slice 6; RQ-13). Advisory audit rows keyed to the 2B
    /// lines. Empty until a reconciliation is run (ER-13).</summary>
    public IReadOnlyList<Gstr2bReconResultDto> Gstr2bReconResults { get; init; } = [];

    /// <summary>Offline IMS decisions (Phase 9 slice 6b; RQ-14). The taxpayer's Accept/Reject/Pending + Oct-2025 CDN
    /// reversal declaration, keyed to the 2B lines. Empty until the user acts (a line with no action is deemed-accepted,
    /// ER-13). ADVISORY only — no NIC credential appears here (ER-16).</summary>
    public IReadOnlyList<ImsActionDto> ImsActions { get; init; } = [];

    /// <summary>Posted Rule-88A set-off Table-6.1 allocation rows (Phase 9 slice 7; RQ-21). The audit of a posted
    /// set-off Journal, so it is always co-exported with that voucher. Empty until a period is set off (ER-13).</summary>
    public IReadOnlyList<GstSetoffLineDto> GstSetoffLines { get; init; } = [];

    /// <summary>Posted ITC-reversal audit rows (Phase 9 slice 7; RQ-27). The reversal engine is S7b, so this stays
    /// empty in S7a (ER-13).</summary>
    public IReadOnlyList<ItcReversalDto> ItcReversals { get; init; } = [];

    /// <summary>PMT-06 GST deposit challans (Phase 9 slice 7; RQ-22). The record of a posted cash-ledger deposit.
    /// Empty until the company deposits GST (ER-13).</summary>
    public IReadOnlyList<GstChallanDto> GstChallans { get; init; } = [];

    /// <summary>DRC-03 voluntary GST payments (Phase 9 slice 7; RQ-22). Empty until one is raised (ER-13).</summary>
    public IReadOnlyList<GstDrc03Dto> GstDrc03s { get; init; } = [];
}

/// <summary>An imported GSTR-2B/2A statement (Phase 9 slice 6), mirroring the domain <c>Gstr2bSnapshot</c> + the SQLite
/// <c>gstr2b_snapshots</c> row, owning its <see cref="Lines"/> (the <c>gstr2b_lines</c> children). Money is integer
/// paisa; <c>GeneratedOn</c> is ISO or null; <c>ImportedAt</c> is ISO round-trip (o). External portal data (not a
/// secret) — exported. No NIC credential appears (ER-16).</summary>
public sealed record Gstr2bSnapshotDto
{
    public required Guid Id { get; init; }
    public required string StatementType { get; init; }          // GstStatementType name
    public required string ReturnPeriod { get; init; }           // "yyyy-MM"
    public required string RecipientGstin { get; init; }
    public string? GeneratedOn { get; init; }                    // ISO yyyy-MM-dd, or null
    public required string SourceFileHash { get; init; }         // SHA-256 hex
    public required string ImportedAt { get; init; }             // ISO round-trip (o)
    public long SummaryIgstPaisa { get; init; }
    public long SummaryCgstPaisa { get; init; }
    public long SummarySgstPaisa { get; init; }
    public long SummaryCessPaisa { get; init; }
    public IReadOnlyList<Gstr2bLineDto> Lines { get; init; } = [];
}

/// <summary>One imported inward-supply record (Phase 9 slice 6), mirroring the domain <c>Gstr2bLine</c> + the SQLite
/// <c>gstr2b_lines</c> row. Money is integer paisa.</summary>
public sealed record Gstr2bLineDto
{
    public required Guid Id { get; init; }
    public required string SupplierGstin { get; init; }
    public string? SupplierTradeName { get; init; }
    public required string DocType { get; init; }                // Gstr2bDocType name
    public required string DocNumber { get; init; }
    public string? DocNumberNorm { get; init; }
    public required string DocDate { get; init; }                // ISO yyyy-MM-dd
    public string? PosStateCode { get; init; }
    public long TaxableValuePaisa { get; init; }
    public long IgstPaisa { get; init; }
    public long CgstPaisa { get; init; }
    public long SgstPaisa { get; init; }
    public long CessPaisa { get; init; }
    public bool ItcAvailable { get; init; }
    public string? ItcUnavailableReason { get; init; }
    public bool ReverseCharge { get; init; }
}

/// <summary>A persisted GSTR-2B reconciliation result (Phase 9 slice 6), mirroring the domain <c>Gstr2bReconResult</c> +
/// the SQLite <c>gstr2b_recon</c> row. <c>MatchedVoucherId</c> is an optional read-only pointer to a books voucher
/// (ER-14); it re-links via the voucher map on import (null ⇒ the pin is dropped, unless the bucket requires it). Money
/// is integer paisa.</summary>
public sealed record Gstr2bReconResultDto
{
    public required Guid Id { get; init; }
    public required Guid LineId { get; init; }                   // FK the imported Gstr2bLine DTO id
    public required string Bucket { get; init; }                 // ReconBucket name (Matched/PartialMismatch/InPortalOnly)
    public Guid? MatchedVoucherId { get; init; }
    public long TaxableVariancePaisa { get; init; }
    public long TaxVariancePaisa { get; init; }
    public bool MatchPinned { get; init; }
    public string? ReconciledAt { get; init; }                   // ISO round-trip (o), or null
}

/// <summary>An offline IMS decision (Phase 9 slice 6b), mirroring the domain <c>ImsAction</c> + the SQLite
/// <c>ims_status</c> row. Keyed to the imported <c>Gstr2bLine</c> by <c>LineId</c>; re-links through the same line-id
/// remap the recon results use on import. Money is integer paisa; <c>ActedOn</c> is ISO or null.</summary>
public sealed record ImsActionDto
{
    public required Guid Id { get; init; }
    public required Guid LineId { get; init; }                   // FK the imported Gstr2bLine DTO id
    public required string Status { get; init; }                 // ImsStatus name (NoAction/Accepted/Rejected/Pending)
    public string? Remarks { get; init; }
    public long? DeclaredReversalPaisa { get; init; }            // Oct-2025 partial CDN reversal, or null
    public bool NoReversalDeclared { get; init; }
    public string? ActedOn { get; init; }                        // ISO yyyy-MM-dd, or null
}

/// <summary>A posted Rule-88A set-off Table-6.1 allocation row (Phase 9 slice 7), mirroring the domain
/// <c>GstSetoffLine</c> + the SQLite <c>gst_setoff_lines</c> row. <c>VoucherId</c> FKs the posted set-off Journal (it
/// re-links through the voucher map on import). Money is integer paisa; heads are enum names.</summary>
public sealed record GstSetoffLineDto
{
    public required Guid Id { get; init; }
    public required Guid VoucherId { get; init; }                // FK the posted set-off Journal
    public required string Period { get; init; }                 // "yyyy-MM"
    public required string CreditHead { get; init; }             // GstTaxHead name
    public required string LiabilityHead { get; init; }          // GstTaxHead name
    public bool IsCash { get; init; }
    public long AmountPaisa { get; init; }
}

/// <summary>A posted ITC-reversal audit row (Phase 9 slice 7), mirroring the domain <c>ItcReversal</c> + the SQLite
/// <c>itc_reversals</c> row. The reversal engine is S7b, so this stays empty in S7a. Money is integer paisa; the rule +
/// bucket are enum names. Every voucher FK re-links through the voucher map on import.</summary>
public sealed record ItcReversalDto
{
    public required Guid Id { get; init; }
    public required string Rule { get; init; }                   // ItcReversalRule name
    public required string Period { get; init; }
    public long CgstPaisa { get; init; }
    public long SgstPaisa { get; init; }
    public long IgstPaisa { get; init; }
    public long CessPaisa { get; init; }
    public long? D1BasisPaisa { get; init; }
    public long? D2BasisPaisa { get; init; }
    public Guid? SourceVoucherId { get; init; }
    public Guid? SourceLineId { get; init; }                     // FK an imported Gstr2bLine DTO id
    public required Guid ReversalVoucherId { get; init; }        // FK the posted stat-adjustment Journal
    public Guid? ReclaimOfId { get; init; }                      // FK another ItcReversal DTO id (a reclaim)
    public Guid? Drc03Id { get; init; }                          // FK a GstDrc03 DTO id
    public required string Table4bBucket { get; init; }          // Table4bBucket name
    public required string CreatedAt { get; init; }              // ISO round-trip (o)
}

/// <summary>A PMT-06 GST deposit challan (Phase 9 slice 7), mirroring the domain <c>GstChallan</c> + the SQLite
/// <c>gst_challans</c> row. <c>VoucherId</c> FKs the deposit voucher (re-links through the voucher map on import).
/// Money is integer paisa; the heads are enum names; <c>DepositDate</c> is ISO.</summary>
public sealed record GstChallanDto
{
    public required Guid Id { get; init; }
    public required string Cpin { get; init; }
    public string? Cin { get; init; }
    public string? Brn { get; init; }
    public required string DepositDate { get; init; }            // ISO yyyy-MM-dd
    public required string MajorHead { get; init; }              // GstTaxHead name
    public required string MinorHead { get; init; }              // GstMinorHead name
    public long AmountPaisa { get; init; }
    public required Guid VoucherId { get; init; }                // FK the deposit voucher
    public bool InterestFlag { get; init; }
}

/// <summary>A DRC-03 voluntary GST payment (Phase 9 slice 7), mirroring the domain <c>GstDrc03</c> + the SQLite
/// <c>gst_drc03</c> row. <c>VoucherId</c> (if any) FKs the payment voucher (re-links through the voucher map on
/// import). Money is integer paisa; <c>CreatedAt</c> is ISO round-trip (o).</summary>
public sealed record GstDrc03Dto
{
    public required Guid Id { get; init; }
    public string? Drc03Ref { get; init; }
    public required string Cause { get; init; }
    public required string Period { get; init; }
    public long CgstPaisa { get; init; }
    public long SgstPaisa { get; init; }
    public long IgstPaisa { get; init; }
    public long CessPaisa { get; init; }
    public long InterestPaisa { get; init; }
    public string? Drc03aDemandRef { get; init; }
    public Guid? VoucherId { get; init; }                        // FK the payment voucher, or null
    public required string CreatedAt { get; init; }              // ISO round-trip (o)
}

/// <summary>An RCM generated document (Phase 9 slice 2), mirroring the domain <c>RcmDocument</c> and the SQLite
/// <c>rcm_documents</c> row.</summary>
public sealed record RcmDocumentDto
{
    public required Guid Id { get; init; }
    public required string Kind { get; init; }             // RcmDocumentKind name
    public required Guid SourceVoucherId { get; init; }
    public int SeriesNumber { get; init; }
    public required string DocDate { get; init; }          // ISO yyyy-MM-dd
    public Guid? SupplierLedgerId { get; init; }
}

/// <summary>An e-invoice IRP artefact (Phase 9 slice 4a), mirroring the domain <c>EInvoiceRecord</c> and the SQLite
/// <c>einvoice_records</c> row. The IRP-issued IRN/AckNo/AckDate/SignedQr/SignedJson are portal receipts (exported); no
/// NIC credential appears here — the only secret flows through <c>INicCredentialStore</c> alone (ER-16).</summary>
public sealed record EInvoiceRecordDto
{
    public required Guid Id { get; init; }
    public required Guid SourceVoucherId { get; init; }
    public required string DocumentNumberUpper { get; init; }
    public required string Status { get; init; }              // EInvoiceStatus name
    public string? Irn { get; init; }
    public string? AckNo { get; init; }
    public string? AckDate { get; init; }                     // ISO or null
    public string? SignedQr { get; init; }
    public string? SignedJsonBase64 { get; init; }            // signed INV-01 response, base64 (an IRP artefact, NOT a secret)
    public string? CancelledOn { get; init; }
    public string? CancelReasonCode { get; init; }
    public string? ErrorCode { get; init; }
    public string? ErrorMessage { get; init; }
}

/// <summary>An e-Way Bill artefact (Phase 9 slice 5), mirroring the domain <c>EWayBillRecord</c> and the SQLite
/// <c>eway_bills</c> row. The portal-issued EWB number + validity ARE exported (portal receipts, not secrets); no NIC
/// credential appears here — the only secret flows through <c>INicCredentialStore</c> alone (ER-16). <c>GeneratedAt</c> /
/// <c>ValidUpto</c> are ISO round-trip (o) strings.</summary>
public sealed record EWayBillRecordDto
{
    public required Guid Id { get; init; }
    public required Guid SourceVoucherId { get; init; }
    public required string DocumentNumberUpper { get; init; }
    public required string Status { get; init; }              // EWayStatus name
    public string? SupplyType { get; init; }
    public string? SubSupplyType { get; init; }
    public string? DocType { get; init; }
    public long ConsignmentValuePaisa { get; init; }
    public string? TransporterId { get; init; }
    public string? TransMode { get; init; }                   // EWayTransportMode name, or null
    public string? VehicleNumber { get; init; }
    public int DistanceKm { get; init; }
    public string? TransportDocNo { get; init; }
    public string? ShipFromStateCode { get; init; }
    public string? ShipToStateCode { get; init; }
    public bool IsOverDimensionalCargo { get; init; }
    public string? ShipToGstin { get; init; }
    public bool ClosureRequested { get; init; }
    public string? ClosedOn { get; init; }                    // ISO yyyy-MM-dd, or null
    public string? EwbNumber { get; init; }                   // 12-digit, FROM the portal
    public string? GeneratedAt { get; init; }                 // ISO round-trip (o), or null
    public string? ValidUpto { get; init; }                   // ISO round-trip (o), or null
    public string? CancelledOn { get; init; }                 // ISO yyyy-MM-dd, or null
    public string? CancelReasonCode { get; init; }
    public string? ErrorCode { get; init; }
    public string? ErrorMessage { get; init; }
}

/// <summary>A per-state / per-transaction-type e-Way consignment-threshold override (Phase 9 slice 5), mirroring the
/// domain <c>EWayStateThreshold</c> and the SQLite <c>eway_state_thresholds</c> row. Empty on the flat ₹50,000 default.</summary>
public sealed record EWayStateThresholdDto
{
    public required Guid Id { get; init; }
    public required string StateCode { get; init; }
    public required string TxnType { get; init; }             // EWayTransactionType name
    public long ThresholdPaisa { get; init; }
}

/// <summary>A §34 credit/debit-note link (Phase 9), mirroring the domain <c>GstCreditDebitNoteLink</c> and the SQLite
/// <c>gst_cdn_links</c> row. Empty until S2b.</summary>
public sealed record GstCdnLinkDto
{
    public required Guid Id { get; init; }
    public required Guid CdnVoucherId { get; init; }
    public required string CdnType { get; init; }          // CdnType name
    public Guid? OriginalInvoiceVoucherId { get; init; }
    public string? OriginalInvoiceNumber { get; init; }
    public string? OriginalInvoiceDate { get; init; }      // ISO or null
    public required string ReasonCode { get; init; }
    public bool Is9BTarget { get; init; }
}

/// <summary>A GST-on-advance receipt record (Phase 9), mirroring the domain <c>GstAdvanceReceipt</c> and the SQLite
/// <c>gst_advance_receipts</c> row. Money is integer paisa. Empty until S2b.</summary>
public sealed record GstAdvanceReceiptDto
{
    public required Guid Id { get; init; }
    public required Guid ReceiptVoucherId { get; init; }
    public bool IsService { get; init; }
    public long AdvanceAmountPaisa { get; init; }
    public int RateBasisPoints { get; init; }
    public bool InterState { get; init; }
    public string? PlaceOfSupplyStateCode { get; init; }
    public long AdvanceTaxPaisa { get; init; }
    public Guid? AdjustedAgainstInvoiceVoucherId { get; init; }
    public Guid? RefundVoucherId { get; init; }
}

/// <summary>A TDS deposit challan (Phase 7 slice 3), mirroring the domain <c>TdsChallan</c> and the SQLite
/// <c>tds_challans</c> row. Money is integer paisa (the canonical wire scale).</summary>
public sealed record TdsChallanDto
{
    public required Guid Id { get; init; }
    public required string ChallanNo { get; init; }
    public required string BsrCode { get; init; }

    /// <summary>ISO yyyy-MM-dd.</summary>
    public required string DepositDate { get; init; }

    public long AmountPaisa { get; init; }
    public required string Section { get; init; }
    public required string MinorHead { get; init; }
}

/// <summary>A challan ↔ Stat-Payment-voucher link (Phase 7 slice 3), mirroring the domain
/// <c>ChallanVoucherLink</c> and the SQLite <c>challan_voucher_links</c> row. Reused (in a distinct list) for the
/// Phase 7 slice 6 TCS challan links.</summary>
public sealed record ChallanVoucherLinkDto
{
    public required Guid ChallanId { get; init; }
    public required Guid VoucherId { get; init; }
}

/// <summary>A TCS deposit challan (Phase 7 slice 6), mirroring the domain <c>TcsChallan</c> and the SQLite
/// <c>tcs_challans</c> row. Money is integer paisa (the canonical wire scale).</summary>
public sealed record TcsChallanDto
{
    public required Guid Id { get; init; }
    public required string ChallanNo { get; init; }
    public required string BsrCode { get; init; }

    /// <summary>ISO yyyy-MM-dd.</summary>
    public required string DepositDate { get; init; }

    public long AmountPaisa { get; init; }
    public required string CollectionCode { get; init; }
    public required string MinorHead { get; init; }
}

// ----------------------------------------------------------------- masters

public sealed record GroupDto
{
    public required Guid Id { get; init; }
    public required string Name { get; init; }
    public required string Nature { get; init; }      // GroupNature name
    public Guid? ParentId { get; init; }
    public string? Alias { get; init; }
    public bool IsPredefined { get; init; }
}

public sealed record LedgerDto
{
    public required Guid Id { get; init; }
    public required string Name { get; init; }
    public required Guid GroupId { get; init; }
    public long OpeningBalancePaisa { get; init; }
    public bool OpeningIsDebit { get; init; }
    public string? Alias { get; init; }
    public bool IsPredefined { get; init; }
    public bool MaintainBillByBill { get; init; }
    public int? DefaultCreditPeriodDays { get; init; }
    public bool? CostCentresApplicable { get; init; }
    public bool EnableChequePrinting { get; init; }
    public string? ChequePrintingBankName { get; init; }
    public Guid? CurrencyId { get; init; }
    public InterestParametersDto? Interest { get; init; }
    public PartyGstDto? PartyGst { get; init; }
    public StockItemGstDto? SalesPurchaseGst { get; init; }
    public LedgerGstClassificationDto? GstClassification { get; init; }

    /// <summary>"Method of Appropriation" on an additional-cost ledger (Phase 6 slice 3; RQ-16..RQ-20). A non-null
    /// value (ByQuantity/ByValue) MARKS this Direct-Expenses ledger as an additional-cost ledger; <c>null</c> (the
    /// default) = a plain P&amp;L ledger that never touches a stock rate.</summary>
    public string? MethodOfAppropriation { get; init; }

    /// <summary>A party ledger's default Price Level (Phase 6 slice 5; RQ-30); <c>null</c> = no default level.</summary>
    public Guid? DefaultPriceLevelId { get; init; }

    // ---- Phase 7 slice 1: TDS/TCS ledger applicability flags (all default off/null). ----

    /// <summary>"Is TDS Applicable" (Phase 7 slice 1). Default false.</summary>
    public bool TdsApplicable { get; init; }

    /// <summary>Default Nature-of-Payment (TDS section) id; <c>null</c> ⇒ none.</summary>
    public Guid? TdsNatureOfPaymentId { get; init; }

    /// <summary>The party's deductee legal status (DeducteeType name); <c>null</c> ⇒ unset.</summary>
    public string? DeducteeType { get; init; }

    /// <summary>The party's PAN; <c>null</c> ⇒ none.</summary>
    public string? PartyPan { get; init; }

    /// <summary>"Deduct TDS in same voucher" (Phase 7 slice 1). Default false.</summary>
    public bool DeductTdsInSameVoucher { get; init; }

    /// <summary>"Is TCS Applicable" (Phase 7 slice 1). Default false.</summary>
    public bool TcsApplicable { get; init; }

    /// <summary>Default Nature-of-Goods (§206C) id; <c>null</c> ⇒ none.</summary>
    public Guid? TcsNatureOfGoodsId { get; init; }

    /// <summary>The party's collectee legal status (CollecteeType name); <c>null</c> ⇒ unset.</summary>
    public string? CollecteeType { get; init; }

    /// <summary>The auto-created payable-ledger tag (TdsTcsLedgerKind name: "Tds"/"Tcs"); <c>null</c> ⇒ ordinary ledger.</summary>
    public string? TdsTcsClassification { get; init; }

    /// <summary>
    /// The party Mailing Details block (WI-4; schema v45); <c>null</c> ⇒ no mailing details captured, which is the
    /// default for every pre-v45 ledger.
    /// <para><b>Appended at the END so existing field order is unchanged, and marked
    /// <see cref="System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull"/> so a ledger without mailing
    /// details emits NO key at all.</b> The canonical JSON options set <c>DefaultIgnoreCondition = Never</c>, so
    /// without this attribute every ledger in every existing export would gain a <c>"mailing": null</c> line and the
    /// bytes would change for companies that never use the feature — breaking ER-13. XML gets the same treatment for
    /// free (<c>CanonicalXml.BuildLedger</c> only adds the child element when the block is non-null).</para>
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore(
        Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public PartyMailingDto? Mailing { get; init; }
}

/// <summary>The optional interest-calculation block on a ledger (catalog §7). <c>null</c> ⇒ no interest.</summary>
public sealed record InterestParametersDto
{
    public bool Enabled { get; init; }
    public decimal RatePercent { get; init; }
    public required string Per { get; init; }              // InterestPer name
    public required string OnBalance { get; init; }        // InterestOnBalance name
    public required string Applicability { get; init; }    // InterestApplicability name
    public string? CalculateFrom { get; init; }             // ISO yyyy-MM-dd or null
    public required string Style { get; init; }            // InterestStyle name
    public required string RoundingMethod { get; init; }   // InterestRoundingMethod name
    public int RoundingDecimals { get; init; }
}

public sealed record VoucherTypeDto
{
    public required Guid Id { get; init; }
    public required string Name { get; init; }
    public required string BaseType { get; init; }    // VoucherBaseType name
    public required string Numbering { get; init; }    // NumberingMethod name
    public string? DefaultShortcut { get; init; }
    public string? Abbreviation { get; init; }
    public bool IsActive { get; init; }
    public bool IsPredefined { get; init; }
    public bool AffectsAccounts { get; init; }
    public bool AffectsStock { get; init; }

    /// <summary>"Use as Manufacturing Journal" (Phase 6 Cluster 2; RQ-11). Default false ⇒ a plain Stock Journal
    /// (or any non-manufacturing type) serialises byte-identically (ER-13). Only meaningful on a Stock-Journal base.</summary>
    public bool UseAsManufacturingJournal { get; init; }

    /// <summary>"Track Additional Costs for Purchases" (Phase 6 slice 3; RQ-16). Default false.</summary>
    public bool TrackAdditionalCosts { get; init; }

    /// <summary>"Allow zero-valued transactions" (Phase 6 slice 4; RQ-21). Default false.</summary>
    public bool AllowZeroValuedTransactions { get; init; }

    /// <summary>"Use for POS invoicing" (Phase 6 slice 7; RQ-38) — a Sales voucher-type flag. Default false.</summary>
    public bool UseForPos { get; init; }

    /// <summary>"Use for Job Work" (Material In/Out) (Phase 6 slice 8; RQ-45/RQ-48). Default false.</summary>
    public bool UseForJobWork { get; init; }

    /// <summary>"Allow Consumption" (Material In) (Phase 6 slice 8; RQ-48). Default false.</summary>
    public bool AllowConsumption { get; init; }

    /// <summary>"Use for Statutory Payment (Stat Payment)" (Phase 7 slice 3) — a Payment voucher-type flag. Default false.</summary>
    public bool IsStatPayment { get; init; }

    /// <summary>"Use for RCM Payment Voucher (Rule 52)" (Phase 9 slice 2) — a Payment voucher-type flag. Default false.</summary>
    public bool IsRcmPaymentVoucher { get; init; }

    /// <summary>"Use for GST Statutory Adjustment (Alt+J)" (Phase 9 slice 7) — a Journal voucher-type flag. Default false.</summary>
    public bool IsGstStatAdjustment { get; init; }

    /// <summary>The POS retail-till configuration (Phase 6 slice 7; RQ-38; DP-4), non-null only on a POS Sales type.</summary>
    public PosConfigDto? PosConfig { get; init; }
}

/// <summary>The POS retail-till configuration carried by a POS-flagged Sales voucher type (Phase 6 slice 7; RQ-38;
/// DP-4), mirroring the domain <c>PosConfig</c> and the SQLite <c>pos_voucher_type_config</c> +
/// <c>pos_tender_ledger_defaults</c> rows.</summary>
public sealed record PosConfigDto
{
    public Guid? DefaultGodownId { get; init; }
    public Guid? DefaultPartyId { get; init; }         // null = walk-in "(cash)"
    public bool PrintAfterSave { get; init; }
    public string? DefaultTitle { get; init; }
    public string? Message1 { get; init; }
    public string? Message2 { get; init; }
    public string? Declaration { get; init; }

    /// <summary>The POS Voucher Class tender-ledger pre-map (DP-4): a default ledger per tender kind.</summary>
    public IReadOnlyList<PosTenderLedgerDefaultDto> TenderLedgerDefaults { get; init; } = [];
}

/// <summary>One tender-ledger default in a <see cref="PosConfigDto"/> (Phase 6 slice 7; DP-4).</summary>
public sealed record PosTenderLedgerDefaultDto
{
    public required string TenderType { get; init; }   // PosTenderType name
    public required Guid LedgerId { get; init; }
}

public sealed record UnitDto
{
    public required Guid Id { get; init; }
    public required string Symbol { get; init; }
    public required string FormalName { get; init; }
    public bool IsCompound { get; init; }
    public string? UnitQuantityCode { get; init; }
    public int DecimalPlaces { get; init; }
    public Guid? FirstUnitId { get; init; }
    public Guid? TailUnitId { get; init; }
    public int? ConversionNumerator { get; init; }
    public int? ConversionDenominator { get; init; }
}

public sealed record StockGroupDto
{
    public required Guid Id { get; init; }
    public required string Name { get; init; }
    public Guid? ParentId { get; init; }
    public string? Alias { get; init; }
    public bool AddQuantities { get; init; }
}

public sealed record StockCategoryDto
{
    public required Guid Id { get; init; }
    public required string Name { get; init; }
    public Guid? ParentId { get; init; }
    public string? Alias { get; init; }
}

public sealed record GodownDto
{
    public required Guid Id { get; init; }
    public required string Name { get; init; }
    public Guid? ParentId { get; init; }
    public string? Alias { get; init; }
    public bool ThirdParty { get; init; }
    public bool IsMainLocation { get; init; }
}

public sealed record StockItemDto
{
    public required Guid Id { get; init; }
    public required string Name { get; init; }
    public required Guid StockGroupId { get; init; }
    public required Guid BaseUnitId { get; init; }
    public Guid? CategoryId { get; init; }
    public string? Alias { get; init; }
    public required string ValuationMethod { get; init; } // StockValuationMethod name
    public string? HsnSacCode { get; init; }
    public bool IsTaxable { get; init; }
    public long? StandardCostPaisa { get; init; }
    public decimal? ReorderLevel { get; init; }
    public decimal? MinimumOrderQuantity { get; init; }
    public StockItemGstDto? Gst { get; init; }

    // Batch switches (Phase 6 Cluster 1; RQ-2). Default false ⇒ a non-batch item serialises byte-identically.
    public bool MaintainInBatches { get; init; }
    public bool TrackManufacturingDate { get; init; }
    public bool UseExpiryDates { get; init; }

    /// <summary>"Set Components (BOM)" (Phase 6 Cluster 2; RQ-10): the item is a manufactured finished good with
    /// ≥1 BOM. Default false ⇒ a non-manufactured item serialises byte-identically (ER-13).</summary>
    public bool SetComponents { get; init; }

    /// <summary>The item's default Nature-of-Goods (§206C TCS) id (Phase 7 slice 1); <c>null</c> ⇒ none (default).</summary>
    public Guid? TcsNatureOfGoodsId { get; init; }
}

/// <summary>
/// A first-class batch/lot master (Phase 6 Cluster 1; RQ-1/RQ-4/RQ-6, DP-8), mirroring the domain
/// <c>BatchMaster</c> and the SQLite <c>batch_masters</c> row. The batch number is unique <b>within its item</b>
/// (not globally). Expiry may be an absolute date <b>and/or</b> a raw period text (e.g. "18 Months") that the
/// engine resolves from the mfg date. The optional per-batch inward cost layer is integer paisa (rate) + exact
/// decimal (quantity); both <c>null</c> when the batch is a pure label with no layer yet.
/// </summary>
public sealed record BatchMasterDto
{
    public required Guid Id { get; init; }
    public required Guid StockItemId { get; init; }
    public required string BatchNumber { get; init; }
    public string? ManufacturingDate { get; init; }  // ISO yyyy-MM-dd or null
    public string? ExpiryDate { get; init; }          // resolved absolute ISO yyyy-MM-dd or null
    public string? ExpiryPeriod { get; init; }        // raw period text ("18 Months") or null
    public Guid? GodownId { get; init; }               // inward-layer location or null
    public decimal? InwardQuantity { get; init; }      // per-batch inward qty or null
    public long? InwardRatePaisa { get; init; }        // per-batch inward rate in paisa or null
}

/// <summary>
/// A first-class <b>Bill of Materials</b> master (Phase 6 Cluster 2; RQ-9, DP-3), mirroring the domain
/// <c>BillOfMaterials</c> and the SQLite <c>bill_of_materials</c> + <c>bom_lines</c> rows. It is a named recipe
/// for a finished-good <see cref="StockItemId"/> (the BOM name is unique <b>within its item</b>), with a
/// <see cref="UnitOfManufacture"/> block size the line quantities are stated against, and an ordered set of
/// component/output <see cref="Lines"/>. Quantities are exact decimals (6-dp), carve-out money is integer paisa.
/// </summary>
public sealed record BillOfMaterialsDto
{
    public required Guid Id { get; init; }
    public required Guid StockItemId { get; init; }        // the finished good
    public required string Name { get; init; }             // unique within the item (case-insensitive)
    public decimal UnitOfManufacture { get; init; }        // block size the line quantities are stated per (> 0)
    public IReadOnlyList<BomLineDto> Lines { get; init; } = [];
}

/// <summary>
/// One line of a <see cref="BillOfMaterialsDto"/> (Phase 6 Cluster 2; RQ-9, DP-3): a consumed Component or a
/// carved-out By-Product/Co-Product/Scrap output. <see cref="QuantityPerBlock"/> is stated per unit-of-manufacture
/// block. A carve-out line may carry a value basis — an explicit per-unit <see cref="RatePaisa"/> (integer paisa)
/// OR a <see cref="PercentOfFinishedGoodCost"/> (exact decimal percent). Both <c>null</c> ⇒ default to the item's
/// standard cost. A Component line never carries either (its cost comes from valuation at manufacture time).
/// </summary>
public sealed record BomLineDto
{
    public required string LineType { get; init; }         // BomLineType name (Component/ByProduct/CoProduct/Scrap)
    public required Guid ComponentStockItemId { get; init; }
    public Guid? GodownId { get; init; }                    // consumption/output location, null = resolve at posting
    public decimal QuantityPerBlock { get; init; }         // per-block quantity (> 0), exact 6-dp
    public long? RatePaisa { get; init; }                  // carve-out per-unit rate in paisa (DP-3), or null
    public decimal? PercentOfFinishedGoodCost { get; init; } // carve-out % of FG cost (DP-3), or null
}

public sealed record StockOpeningBalanceDto
{
    public required Guid Id { get; init; }
    public required Guid StockItemId { get; init; }
    public required Guid GodownId { get; init; }
    public decimal Quantity { get; init; }
    public long RatePaisa { get; init; }
    public string? BatchLabel { get; init; }
    public string? ManufacturingDate { get; init; } // ISO or null
    public string? ExpiryDate { get; init; }          // ISO or null
}

// ----------------------------------------------------------------- price levels / lists (catalog §11; slice 5)

/// <summary>A named Price Level (Phase 6 slice 5; RQ-26), mirroring the domain <c>PriceLevel</c> and the SQLite
/// <c>price_levels</c> row. A level is nothing but an id + name; the party default and the dated price lists
/// reference it.</summary>
public sealed record PriceLevelDto
{
    public required Guid Id { get; init; }
    public required string Name { get; init; }
}

/// <summary>A dated Price List version scoped to a (level, item) pair (Phase 6 slice 5; RQ-27), mirroring the
/// domain <c>PriceList</c> + <c>PriceListSlab</c> and the SQLite <c>price_lists</c> + <c>price_list_lines</c> rows.
/// Append-only history: a revision appends a new version with a later <see cref="ApplicableFrom"/>.</summary>
public sealed record PriceListDto
{
    public required Guid Id { get; init; }
    public required Guid PriceLevelId { get; init; }
    public required Guid StockItemId { get; init; }
    public required string ApplicableFrom { get; init; }   // ISO yyyy-MM-dd
    public IReadOnlyList<PriceListSlabDto> Slabs { get; init; } = [];
}

/// <summary>One quantity slab of a <see cref="PriceListDto"/> (Phase 6 slice 5; RQ-27/RQ-28). From is inclusive,
/// To is exclusive; <c>ToQty</c> null = open-ended top slab. Rate is integer paisa; the discount is a percent.</summary>
public sealed record PriceListSlabDto
{
    public decimal FromQty { get; init; }
    public decimal? ToQty { get; init; }               // null = open-ended top slab
    public long RatePaisa { get; init; }
    public decimal DiscountPercent { get; init; }
}

// ----------------------------------------------------------------- reorder definitions (catalog §11; slice 6)

/// <summary>A Reorder-Level definition per stock item / group / category (Phase 6 slice 6; RQ-32..RQ-35),
/// mirroring the domain <c>ReorderDefinition</c> and the SQLite <c>reorder_definitions</c> row. Quantity-only
/// (no money); the shared period + criteria govern both Advanced figures.</summary>
public sealed record ReorderDefinitionDto
{
    public required Guid Id { get; init; }
    public required string Scope { get; init; }        // ReorderScope name (Item/Group/Category)
    public required Guid TargetId { get; init; }
    public bool ReorderAdvanced { get; init; }
    public decimal? ReorderQuantity { get; init; }
    public bool MinQtyAdvanced { get; init; }
    public decimal? MinOrderQuantity { get; init; }
    public int? PeriodCount { get; init; }
    public string? PeriodUnit { get; init; }           // ExpiryPeriodUnit name, or null
    public string? Criteria { get; init; }             // ReorderCriteria name, or null
}

// ----------------------------------------------------------------- cost accounting (catalog §6)

public sealed record CostCategoryDto
{
    public required Guid Id { get; init; }
    public required string Name { get; init; }
    public bool AllocateRevenueItems { get; init; }
    public bool AllocateNonRevenueItems { get; init; }
    public bool IsPredefined { get; init; }
}

public sealed record CostCentreDto
{
    public required Guid Id { get; init; }
    public required string Name { get; init; }
    public required Guid CategoryId { get; init; }
    public Guid? ParentId { get; init; }
    public string? Alias { get; init; }
}

// ----------------------------------------------------------------- payroll masters (Phase 8 slice 1; catalog §14)

public sealed record EmployeeCategoryDto
{
    public required Guid Id { get; init; }
    public required string Name { get; init; }
    public bool AllocateRevenueItems { get; init; }
    public bool AllocateNonRevenueItems { get; init; }
    public bool IsPredefined { get; init; }
}

public sealed record EmployeeGroupDto
{
    public required Guid Id { get; init; }
    public required string Name { get; init; }
    public Guid? ParentId { get; init; }
    public string? Alias { get; init; }
    public bool DefineSalaryDetails { get; init; }
}

public sealed record PayrollUnitDto
{
    public required Guid Id { get; init; }
    public required string Symbol { get; init; }
    public required string FormalName { get; init; }
    public bool IsCompound { get; init; }
    public int DecimalPlaces { get; init; }
    public Guid? FirstUnitId { get; init; }
    public Guid? TailUnitId { get; init; }
    public int? ConversionNumerator { get; init; }
    public int? ConversionDenominator { get; init; }
}

public sealed record AttendanceTypeDto
{
    public required Guid Id { get; init; }
    public required string Name { get; init; }
    public Guid? ParentId { get; init; }
    public required string Kind { get; init; }             // AttendanceTypeKind name
    public Guid? PayrollUnitId { get; init; }
}

public sealed record EmployeeDto
{
    public required Guid Id { get; init; }
    public required string Name { get; init; }
    public required Guid EmployeeGroupId { get; init; }
    public Guid? EmployeeCategoryId { get; init; }
    public string? EmployeeNumber { get; init; }
    public string? DateOfJoining { get; init; }            // ISO or null
    public string? DateOfLeaving { get; init; }            // ISO or null
    public string? Designation { get; init; }
    public string? Function { get; init; }
    public string? Location { get; init; }
    public string? Gender { get; init; }
    public string? DateOfBirth { get; init; }              // ISO or null
    public string? Pan { get; init; }
    public string? Aadhaar { get; init; }
    public string? Uan { get; init; }
    public string? PfAccountNumber { get; init; }
    public string? EsiNumber { get; init; }
    public string? BankAccountNumber { get; init; }
    public string? BankName { get; init; }
    public string? BankIfsc { get; init; }
    public required string ApplicableTaxRegime { get; init; }   // TaxRegime name

    // Provident Fund per-employee details (Phase 8 slice 4). All default off, so a pre-v33 employee is byte-identical.
    public bool PfApplicable { get; init; }
    public bool PfContributeOnHigherWages { get; init; }
    public string? PfJoinDate { get; init; }                    // ISO yyyy-MM-dd or null

    /// <summary>ESI applicability (Phase 8 slice 5). Default off, so a pre-v34 employee is byte-identical. The
    /// 10-digit IP number is carried by <see cref="EsiNumber"/> above.</summary>
    public bool EsiApplicable { get; init; }

    /// <summary>Person-with-disability flag (Phase 8 slice 5) — the higher ₹25,000 ESI coverage ceiling. Default off,
    /// so a pre-v34 employee is byte-identical.</summary>
    public bool IsPersonWithDisability { get; init; }
}

// ----------------------------------------------------------------- pay heads + salary structures (Phase 8 slice 2)

/// <summary>A Pay Head master. Enums are member names; money is integer paisa. The computed-on formula of an
/// As-Computed-Value head is carried as <see cref="ComputationComponents"/> (the basis) +
/// <see cref="ComputationSlabs"/> (the bands) — both empty for a non-computed head.</summary>
public sealed record PayHeadDto
{
    public required Guid Id { get; init; }
    public required string Name { get; init; }
    public string? DisplayName { get; init; }
    public required string PayHeadType { get; init; }           // PayHeadType name
    public required string CalculationType { get; init; }       // PayHeadCalculationType name
    public bool AffectsNetSalary { get; init; }
    public Guid? UnderGroupId { get; init; }
    public Guid? LedgerId { get; init; }

    /// <summary>The employer-contribution expense (Dr) ledger (Phase 8 slice 3); null for a non-employer head or
    /// until first posted. Paired with <see cref="LedgerId"/> (the employer payable, Cr).</summary>
    public Guid? EmployerExpenseLedgerId { get; init; }

    public required string IncomeTaxComponent { get; init; }    // IncomeTaxComponent name
    public bool UseForGratuity { get; init; }
    public required string RoundingMethod { get; init; }        // PayHeadRoundingMethod name
    public long RoundingLimitPaisa { get; init; }
    public required string CalculationPeriod { get; init; }     // PayHeadCalculationPeriod name
    public Guid? AttendanceTypeId { get; init; }
    public int? PerDayCalculationBasisDays { get; init; }

    /// <summary>The PF statutory role (Phase 8 slice 4), a <see cref="Apex.Ledger.Domain.PfStatutoryComponent"/>
    /// name; "None" for a non-PF head.</summary>
    public string PfComponent { get; init; } = nameof(Apex.Ledger.Domain.PfStatutoryComponent.None);

    /// <summary>Whether this earning counts toward PF (EPF/EPS/EDLI) wages (Phase 8 slice 4). Default false.</summary>
    public bool PartOfPfWages { get; init; }

    /// <summary>The ESI statutory role (Phase 8 slice 5), a <see cref="Apex.Ledger.Domain.EsiStatutoryComponent"/>
    /// name; "None" for a non-ESI head.</summary>
    public string EsiComponent { get; init; } = nameof(Apex.Ledger.Domain.EsiStatutoryComponent.None);

    /// <summary>Whether this earning counts toward ESI wages — HRA included (Phase 8 slice 5). Default false.</summary>
    public bool PartOfEsiWages { get; init; }

    /// <summary>Whether this earning is overtime — in the ESI contribution base but out of the coverage test
    /// (Phase 8 slice 5). Default false.</summary>
    public bool IsOvertime { get; init; }

    /// <summary>The PT statutory role (Phase 8 slice 6), a <see cref="Apex.Ledger.Domain.PtStatutoryComponent"/> name;
    /// "None" for a non-PT head.</summary>
    public string PtComponent { get; init; } = nameof(Apex.Ledger.Domain.PtStatutoryComponent.None);

    public IReadOnlyList<PayHeadComputationComponentDto> ComputationComponents { get; init; } = [];
    public IReadOnlyList<PayHeadComputationSlabDto> ComputationSlabs { get; init; } = [];
}

/// <summary>One computed-on basis term (a pay-head reference, added or subtracted).</summary>
public sealed record PayHeadComputationComponentDto
{
    public required Guid PayHeadId { get; init; }
    public bool IsSubtraction { get; init; }
}

/// <summary>One computation slab band. Money is integer paisa; a null bound = open-ended.</summary>
public sealed record PayHeadComputationSlabDto
{
    public required string SlabType { get; init; }              // PayHeadComputationSlabType name
    public int RateBasisPoints { get; init; }
    public long ValuePaisa { get; init; }
    public long? FromAmountPaisa { get; init; }
    public long? ToAmountPaisa { get; init; }
}

/// <summary>A dated Salary Structure ("Salary Details") for an employee or employee group, with ordered lines.</summary>
public sealed record SalaryStructureDto
{
    public required Guid Id { get; init; }
    public required string Scope { get; init; }                 // SalaryStructureScope name
    public required Guid ScopeId { get; init; }
    public required string EffectiveFrom { get; init; }         // ISO yyyy-MM-dd
    public required string StartType { get; init; }             // SalaryStructureStartType name
    public IReadOnlyList<SalaryStructureLineDto> Lines { get; init; } = [];
}

/// <summary>One salary-structure line: a pay head + its ordered per-employee amount (integer paisa; null when the
/// pay head is computed / user-defined).</summary>
public sealed record SalaryStructureLineDto
{
    public required Guid PayHeadId { get; init; }
    public int Order { get; init; }
    public long? AmountPaisa { get; init; }
}

/// <summary>A recorded attendance/production value (Phase 8 slice 3; RQ-6), mirroring the domain
/// <c>AttendanceEntry</c> and the SQLite <c>attendance_entries</c> row. The value is exact micro-units
/// (units × 1,000,000, so a half-day / fractional hour round-trips).</summary>
public sealed record AttendanceEntryDto
{
    public required Guid Id { get; init; }
    public required Guid EmployeeId { get; init; }
    public required Guid AttendanceTypeId { get; init; }
    public required string FromDate { get; init; }         // ISO yyyy-MM-dd
    public required string ToDate { get; init; }           // ISO yyyy-MM-dd
    public long ValueMicro { get; init; }                  // units × 1,000,000
}

/// <summary>A per-employee §192 income-tax declaration (Phase 8 slice 7; Form 12BB), mirroring the domain
/// <c>TaxDeclaration</c> and the SQLite <c>employee_tax_declarations</c> row. All money is exact integer paisa via
/// <see cref="MoneyCodec"/>. The <see cref="EmployeeId"/> is remapped to the target employee on import.</summary>
public sealed record TaxDeclarationDto
{
    public required Guid EmployeeId { get; init; }
    public long Section80CPaisa { get; init; }
    public long Section80DPaisa { get; init; }
    public long Section80CCD1BPaisa { get; init; }
    public long Section80CCD2EmployerPaisa { get; init; }
    public long HraExemptPaisa { get; init; }
    public long HomeLoanInterestPaisa { get; init; }
    public long OtherIncomePaisa { get; init; }
    public long PrevEmployerSalaryPaisa { get; init; }
    public long PrevEmployerTdsPaisa { get; init; }
}

// ----------------------------------------------------------------- multi-currency (catalog §2/§20)

public sealed record CurrencyDto
{
    public required Guid Id { get; init; }
    public required string Symbol { get; init; }
    public required string FormalName { get; init; }
    public int DecimalPlaces { get; init; }
    public bool IsBaseCurrency { get; init; }
}

public sealed record ExchangeRateDto
{
    public required Guid Id { get; init; }
    public required Guid CurrencyId { get; init; }
    public required string Date { get; init; }             // ISO yyyy-MM-dd
    /// <summary>Rate × 1,000,000 ("micros"), integer — exact, no float (mirrors the SQLite store).</summary>
    public long StandardRateMicro { get; init; }
    public long? SellingRateMicro { get; init; }
    public long? BuyingRateMicro { get; init; }
}

// ----------------------------------------------------------------- budgets & scenarios (catalog §7)

public sealed record BudgetDto
{
    public required Guid Id { get; init; }
    public required string Name { get; init; }
    public Guid? UnderId { get; init; }
    public required string PeriodFrom { get; init; }       // ISO yyyy-MM-dd
    public required string PeriodTo { get; init; }         // ISO yyyy-MM-dd
    public IReadOnlyList<BudgetLineDto> Lines { get; init; } = [];
}

public sealed record BudgetLineDto
{
    public Guid? GroupId { get; init; }                     // exactly one of GroupId / LedgerId
    public Guid? LedgerId { get; init; }
    public required string BudgetType { get; init; }       // BudgetType name
    public long AmountPaisa { get; init; }
}

public sealed record ScenarioDto
{
    public required Guid Id { get; init; }
    public required string Name { get; init; }
    public bool IncludeActuals { get; init; }
    public IReadOnlyList<Guid> IncludedTypeIds { get; init; } = [];
    public IReadOnlyList<Guid> ExcludedTypeIds { get; init; } = [];
}

// ----------------------------------------------------------------- gst value objects

public sealed record GstConfigDto
{
    public bool Enabled { get; init; }
    public string? Gstin { get; init; }
    public string? HomeStateCode { get; init; }
    public required string RegistrationType { get; init; } // GstRegistrationType name
    public string? ApplicableFrom { get; init; }              // ISO or null
    public required string Periodicity { get; init; }        // GstReturnPeriodicity name
    public IReadOnlyList<GstRateSlabDto> RateSlabs { get; init; } = [];
    // Phase 9 slice 1 (RQ-1/RQ-2): dated rate-history + Compensation-Cess windows. Default empty ⇒ an advanced-GST-off
    // company serialises identically to a Phase-8 company (ER-13). Appended at the END so existing field order is
    // unchanged.
    public IReadOnlyList<GstRateHistoryDto> RateHistory { get; init; } = [];
    public IReadOnlyList<GstCessRateDto> CessRates { get; init; } = [];
    // Phase 9 slice 2 (RQ-3/RQ-7): dated reverse-charge categories. Default empty ⇒ an RCM-off company serialises
    // identically to a v38 company (ER-13). Appended at the END so existing field order is unchanged.
    public IReadOnlyList<RcmCategoryDto> RcmCategories { get; init; } = [];
    // Phase 9 slice 3 (RQ-4): composition sub-type + opt-in date. Null when not a composition dealer ⇒ byte-identical
    // (ER-13). Appended at the END so existing field order is unchanged.
    public string? CompositionSubType { get; init; }   // CompositionSubType name, or null
    public string? CompositionOptInDate { get; init; } // ISO yyyy-MM-dd, or null
    // Phase 9 slice 4a (RQ-5/RQ-28/RQ-30): the NON-SECRET e-invoice / B2C-QR / connector-mode config. Defaulting
    // off/empty/"OfflineJson"/"None" ⇒ an e-invoicing-off company is byte-identical to a v40 company (ER-13). Appended
    // at the END so existing field order is unchanged.
    // DELIBERATE OMISSION (ER-16): there is NO Nic* / credential member here. The NIC API credentials are the ONLY secret
    // and NEVER appear in the canonical model — they flow only through INicCredentialStore. Do NOT "helpfully" add them.
    public bool EInvoicingEnabled { get; init; }
    public string? EInvoiceApplicableFrom { get; init; }      // ISO yyyy-MM-dd, or null
    public long EInvoiceAatoThresholdPaisa { get; init; } = 50_000_000_00L;   // ₹5 cr
    public bool EInvoiceApplicabilityOverride { get; init; }
    public string EInvoiceExemptionClasses { get; init; } = "None";           // EInvoiceExemptionClass [Flags] name
    public bool EInvoiceReportingAgeApplies { get; init; }
    public string ConnectorMode { get; init; } = "OfflineJson";               // GstConnectorMode name
    public bool B2cDynamicQrEnabled { get; init; }
    public long B2cQrAatoThresholdPaisa { get; init; } = 500_000_000_000L;     // ₹500 cr
    public string? B2cQrUpiId { get; init; }
    public string? B2cQrPayeeName { get; init; }
    // Phase 9 slice 5 (RQ-6): the NON-SECRET e-Way Bill config + per-state overrides. Defaulting off/NULL/₹50,000/
    // Rule138Default/true ⇒ an e-Way-off company is byte-identical to a v41 company (ER-13). Appended at the END so
    // existing field order is unchanged. No NIC credential here — the live path reuses ConnectorMode + the store (ER-16).
    public bool EWayBillEnabled { get; init; }
    public string? EWayApplicableFrom { get; init; }                          // ISO yyyy-MM-dd, or null
    public long EWayThresholdPaisa { get; init; } = 5_000_000L;               // ₹50,000
    public string ConsignmentBasis { get; init; } = "Rule138Default";        // EWayConsignmentBasis name
    public bool EWayIntraStateApplicable { get; init; } = true;
    public IReadOnlyList<EWayStateThresholdDto> EWayStateThresholds { get; init; } = [];
    // Phase 9 slice 6 (RQ-13): the GSTR-2B reconciliation tolerance (paisa slack + ± day window). Default 0/0 (exact) ⇒ a
    // company that never touches 2B serialises identically to a v42 company (ER-13). A matching parameter only (ER-14).
    // Appended at the END so existing field order is unchanged (finding #5).
    public long ReconValueTolerancePaisa { get; init; }
    public int ReconDateWindowDays { get; init; }
}

/// <summary>A dated notified reverse-charge category (Phase 9 slice 2; RQ-3/RQ-7). Dates are ISO yyyy-MM-dd.</summary>
public sealed record RcmCategoryDto
{
    public required Guid Id { get; init; }
    public required string Notification { get; init; }
    public required string Stream { get; init; }            // RcmStream name
    public required string SupplyNature { get; init; }
    public required string SupplyType { get; init; }        // GstSupplyType name
    public string? HsnSac { get; init; }
    public int RateBasisPoints { get; init; }
    public required string SupplierQualifier { get; init; } // RcmParty name
    public required string RecipientQualifier { get; init; }// RcmParty name
    public required string EffectiveFrom { get; init; }     // ISO
    public string? EffectiveTo { get; init; }               // ISO or null
    public required string Label { get; init; }
    public bool IsPredefined { get; init; }
}

public sealed record GstRateSlabDto
{
    public required Guid Id { get; init; }
    public int RateBasisPoints { get; init; }
    public required string Label { get; init; }
    public bool IsPredefined { get; init; }
}

/// <summary>A dated GST rate-history window (Phase 9 slice 1; RQ-1). Dates are ISO yyyy-MM-dd.</summary>
public sealed record GstRateHistoryDto
{
    public required Guid Id { get; init; }
    public string? HsnSac { get; init; }
    public int RateBasisPoints { get; init; }
    public required string RateClass { get; init; }        // GstRateClass name
    public required string EffectiveFrom { get; init; }     // ISO
    public string? EffectiveTo { get; init; }               // ISO or null
    public required string ValuationBasis { get; init; }    // GstValuationBasis name
    public required string Label { get; init; }
    public bool IsPredefined { get; init; }
}

/// <summary>A dated Compensation-Cess window (Phase 9 slice 1; RQ-2/RQ-9). Money is integer paisa.</summary>
public sealed record GstCessRateDto
{
    public required Guid Id { get; init; }
    public string? HsnSac { get; init; }
    public required string ValuationMode { get; init; }     // CessValuationMode name
    public int CessRateBasisPoints { get; init; }
    public long CessPerUnitPaisa { get; init; }
    public int CessRspFactorMillis { get; init; }
    public required string EffectiveFrom { get; init; }     // ISO
    public string? EffectiveTo { get; init; }               // ISO or null
    public required string Label { get; init; }
    public bool IsPredefined { get; init; }
}

/// <summary>
/// The party Mailing Details block on a ledger (WI-4; schema v45): Mailing Name, Address, Country, PIN code.
/// <para><b>No State field, deliberately.</b> The party's State/UT is <see cref="PartyGstDto.StateCode"/> — the
/// GST place-of-supply driver. Duplicating it here would let an exported document carry two contradicting States
/// and silently mis-compute tax on import, so the mailing State is read and written through that one value.</para>
/// </summary>
public sealed record PartyMailingDto
{
    public string? MailingName { get; init; }
    public string? Address { get; init; }
    public string? Country { get; init; }
    public string? Pincode { get; init; }
}

public sealed record PartyGstDto
{
    public required string RegistrationType { get; init; } // GstRegistrationType name
    public string? Gstin { get; init; }
    public string? StateCode { get; init; }
    // Phase 9 slice 2 (RQ-3): reverse-charge qualifiers. Default false ⇒ byte-identical to a v38 party (ER-13).
    public bool IsPromoter { get; init; }
    public bool IsBodyCorporate { get; init; }
}

public sealed record StockItemGstDto
{
    public string? HsnSac { get; init; }
    public required string Taxability { get; init; }  // GstTaxability name
    public int? RateBasisPoints { get; init; }
    public required string SupplyType { get; init; }   // GstSupplyType name
    // Phase 9 slice 1 (RQ-1/RQ-2): GST 2.0 RSP valuation + Compensation-Cess. Appended at the END so existing field
    // order is unchanged; defaults keep an off item byte-identical to a Phase-8 item (ER-13). Money is integer paisa.
    public string ValuationBasis { get; init; } = "TransactionValue"; // GstValuationBasis name
    public bool CessApplicable { get; init; }
    public string? CessValuationMode { get; init; }    // CessValuationMode name, or null
    public int? CessRateBasisPoints { get; init; }
    public long? CessPerUnitPaisa { get; init; }
    public int? CessRspFactorMillis { get; init; }
    public long? RspPaisa { get; init; }
    // Phase 9 slice 2 (RQ-3): reverse-charge flags on the shared item / S-P-ledger GST block. Default false/null ⇒
    // byte-identical to a v38 block (ER-13). Appended at the END so existing field order is unchanged.
    public bool ReverseChargeApplicable { get; init; }
    public bool GtaForwardCharge { get; init; }
    public Guid? RcmCategoryId { get; init; }
    // Phase 9 slice 6b (RQ-26): §17(5) ITC-eligibility on the shared item / S-P-ledger GST block. Default
    // Eligible/None ⇒ byte-identical to a v42 block (ER-13). Appended at the END so existing field order is unchanged.
    public string ItcEligibility { get; init; } = "Eligible";              // ItcEligibility name
    public string BlockedCreditCategory { get; init; } = "None";           // BlockedCreditCategory name
}

public sealed record LedgerGstClassificationDto
{
    public required string TaxHead { get; init; }   // GstTaxHead name
    public required string Direction { get; init; } // GstTaxDirection name
    // Phase 9 slice 2 (RQ-7): the RCM Output-ledger discriminator. Default false ⇒ an ordinary tax ledger (ER-13).
    public bool IsReverseCharge { get; init; }
}

// ----------------------------------------------------------------- tds / tcs value objects (Phase 7 slice 1)

/// <summary>The company TDS deductor config (mirrors <see cref="GstConfigDto"/>). Money is integer paisa.</summary>
public sealed record TdsConfigDto
{
    public bool Enabled { get; init; }
    public string? Tan { get; init; }
    public required string DeductorType { get; init; }   // DeductorType name
    public string? ResponsiblePersonName { get; init; }
    public string? ResponsiblePersonPan { get; init; }
    public string? ResponsiblePersonDesignation { get; init; }
    public string? ResponsiblePersonAddress { get; init; }
    public bool SurchargeApplicable { get; init; }
    public bool CessApplicable { get; init; }
    public required string Periodicity { get; init; }     // TdsTcsPeriodicity name
    public string? ApplicableFrom { get; init; }           // ISO or null
    public IReadOnlyList<NatureOfPaymentDto> NaturesOfPayment { get; init; } = [];
}

/// <summary>The company TCS collector config (mirrors <see cref="TdsConfigDto"/>).</summary>
public sealed record TcsConfigDto
{
    public bool Enabled { get; init; }
    public string? Tan { get; init; }
    public required string CollectorType { get; init; }   // DeductorType name
    public string? ResponsiblePersonName { get; init; }
    public string? ResponsiblePersonPan { get; init; }
    public string? ResponsiblePersonDesignation { get; init; }
    public string? ResponsiblePersonAddress { get; init; }
    public bool SurchargeApplicable { get; init; }
    public bool CessApplicable { get; init; }
    public required string Periodicity { get; init; }
    public string? ApplicableFrom { get; init; }
    public IReadOnlyList<NatureOfGoodsDto> NaturesOfGoods { get; init; } = [];
}

/// <summary>A Nature-of-Payment (TDS section) master. Thresholds are integer paisa (null ⇒ no threshold).</summary>
public sealed record NatureOfPaymentDto
{
    public required Guid Id { get; init; }
    public required string SectionCode { get; init; }
    public required string Name { get; init; }
    public int RateWithPanBp { get; init; }
    public int RateWithoutPanBp { get; init; }
    public long? SingleThresholdPaisa { get; init; }
    public long? CumulativeThresholdPaisa { get; init; }
    public required string FvuSectionCode { get; init; }
    public string? EffectiveFrom { get; init; }            // ISO or null
    public bool IsPredefined { get; init; }
}

/// <summary>A Nature-of-Goods (§206C) master. Threshold is integer paisa (null ⇒ collected on full value).</summary>
public sealed record NatureOfGoodsDto
{
    public required Guid Id { get; init; }
    public required string CollectionCode { get; init; }
    public required string Name { get; init; }
    public int RateWithPanBp { get; init; }
    public int RateWithoutPanBp { get; init; }
    public long? ThresholdPaisa { get; init; }
    public bool BaseIncludesGst { get; init; }
    public required string FvuCode { get; init; }
    public string? EffectiveFrom { get; init; }
    public bool IsPredefined { get; init; }
    public bool IsLegacy { get; init; }
    public string? LegacyCutoff { get; init; }             // ISO or null
}

// ----------------------------------------------------------------- vouchers

public sealed record VoucherDto
{
    public required Guid Id { get; init; }
    public required Guid TypeId { get; init; }
    public int Number { get; init; }
    public required string Date { get; init; }        // ISO yyyy-MM-dd
    public string? Narration { get; init; }
    public Guid? PartyId { get; init; }
    public bool Cancelled { get; init; }
    public bool Optional { get; init; }
    public bool PostDated { get; init; }
    public string? ApplicableUpto { get; init; }       // ISO or null
    public IReadOnlyList<EntryLineDto> Lines { get; init; } = [];
    public IReadOnlyList<VoucherInventoryLineDto> InventoryLines { get; init; } = [];

    /// <summary>The POS payment tenders on a POS Sales voucher (Phase 6 slice 7; RQ-39/RQ-40; DP-6). Empty for
    /// every non-POS voucher.</summary>
    public IReadOnlyList<PosTenderDto> PosTenders { get; init; } = [];
}

/// <summary>One POS payment tender on a POS Sales voucher (Phase 6 slice 7; RQ-39/RQ-40; DP-6), mirroring the
/// domain <c>PosTender</c> and the SQLite <c>pos_tender_allocations</c> row. Money is integer paisa.</summary>
public sealed record PosTenderDto
{
    public required string TenderType { get; init; }   // PosTenderType name
    public required Guid LedgerId { get; init; }
    public long AmountPaisa { get; init; }             // posted payable share (Cash = residual, not tendered)
    public long? TenderedPaisa { get; init; }          // Cash only
    public long? ChangePaisa { get; init; }            // Cash only (informational)
    public string? CardNo { get; init; }               // Card only
    public string? BankName { get; init; }             // Cheque/DD only
    public string? ChequeNo { get; init; }             // Cheque/DD only
}

public sealed record EntryLineDto
{
    public required Guid LedgerId { get; init; }
    public long AmountPaisa { get; init; }
    public required string Side { get; init; }         // DrCr name
    public IReadOnlyList<BillAllocationDto> BillAllocations { get; init; } = [];
    public IReadOnlyList<CostAllocationDto> CostAllocations { get; init; } = [];
    public BankAllocationDto? BankAllocation { get; init; }
    public ForexDto? Forex { get; init; }
    public GstLineTaxDto? Gst { get; init; }

    /// <summary>The TDS withholding detail (Phase 7 slice 2), or null for a non-TDS line. Money is integer paisa.</summary>
    public TdsLineTaxDto? Tds { get; init; }

    /// <summary>The TCS collection detail (Phase 7 slice 5), or null for a non-TCS line. Money is integer paisa.</summary>
    public TcsLineTaxDto? Tcs { get; init; }

    /// <summary>The payroll detail (Phase 8 slice 3), or null for a non-payroll line. Money is integer paisa.</summary>
    public PayrollLineDto? Payroll { get; init; }
}

/// <summary>The per-employee computed salary detail on a Payroll-voucher entry line (Phase 8 slice 3), mirroring
/// the domain <c>PayrollLineDetail</c> and the SQLite <c>payroll_lines</c> row. Money is integer paisa; the pay
/// head is null for the net Salary-Payable line.</summary>
public sealed record PayrollLineDto
{
    public required Guid EmployeeId { get; init; }
    public Guid? PayHeadId { get; init; }
    public required string Category { get; init; }       // PayrollLineCategory name
    public long AmountPaisa { get; init; }
}

public sealed record BillAllocationDto
{
    public required string RefType { get; init; }      // BillRefType name
    public required string Name { get; init; }
    public long AmountPaisa { get; init; }
    public string? DueDate { get; init; }               // ISO or null
    public int? CreditPeriodDays { get; init; }
}

public sealed record CostAllocationDto
{
    public required Guid CategoryId { get; init; }
    public required Guid CentreId { get; init; }
    public long AmountPaisa { get; init; }
}

public sealed record BankAllocationDto
{
    public required string TransactionType { get; init; } // BankTransactionType name
    public required string InstrumentNumber { get; init; }
    public string? InstrumentDate { get; init; }           // ISO or null
    public string? BankDate { get; init; }                  // ISO or null
}

public sealed record ForexDto
{
    public required Guid CurrencyId { get; init; }
    /// <summary>Foreign magnitude in integer paisa (minor units × 100), exact — same codec as base money.</summary>
    public long ForexAmountPaisa { get; init; }
    /// <summary>Rate (base per 1 foreign) × 1,000,000 ("micros"), integer — exact, no float.</summary>
    public long RateMicro { get; init; }
}

public sealed record GstLineTaxDto
{
    public required string TaxHead { get; init; }      // GstTaxHead name
    public int RateBasisPoints { get; init; }
    public long TaxableValuePaisa { get; init; }
    // Phase 9 slice 2 (RQ-7): reverse-charge tag. Default false/null ⇒ a forward-charge line is byte-identical (ER-13).
    public bool IsReverseCharge { get; init; }
    public string? RcmScheme { get; init; }            // RcmItcScheme name, or null
    public string? Adjustment { get; init; }           // GstAdjustmentKind name (SetOff/CashPayment/Reversal…/Reclaim), or null
}

/// <summary>The TDS withholding detail carried on an entry line (Phase 7 slice 2), mirroring the domain
/// <c>TdsLineTax</c> and the SQLite <c>tds_lines</c> row. Money is integer paisa (the canonical wire scale — the
/// domain amounts are paisa-exact, so this is lossless).</summary>
public sealed record TdsLineTaxDto
{
    public required Guid NatureId { get; init; }
    public required string SectionCode { get; init; }
    public long AssessableValuePaisa { get; init; }
    public int RateBasisPoints { get; init; }
    public long TdsAmountPaisa { get; init; }
    public required Guid DeducteeLedgerId { get; init; }
    public bool PanApplied { get; init; }
}

/// <summary>The TCS collection detail carried on an entry line (Phase 7 slice 5), mirroring the domain
/// <c>TcsLineTax</c> and the SQLite <c>tcs_lines</c> row. TCS is additive (the mirror of GST). Money is integer paisa
/// (the canonical wire scale — the domain amounts are paisa-exact, so this is lossless).</summary>
public sealed record TcsLineTaxDto
{
    public required Guid NatureId { get; init; }
    public required string CollectionCode { get; init; }
    public long AssessableValuePaisa { get; init; }
    public int RateBasisPoints { get; init; }
    public long TcsAmountPaisa { get; init; }
    public required Guid CollecteeLedgerId { get; init; }
    public bool PanApplied { get; init; }
}

public sealed record VoucherInventoryLineDto
{
    public required Guid StockItemId { get; init; }
    public required Guid GodownId { get; init; }
    public decimal Quantity { get; init; }             // the Actual (stock) quantity
    public long RatePaisa { get; init; }
    public required string Direction { get; init; }    // StockDirection name
    public string? BatchLabel { get; init; }

    /// <summary>The Billed quantity (Phase 6 slice 4; RQ-22/RQ-23; DP-7) when it differs from <see cref="Quantity"/>
    /// (the Actual); <c>null</c> ⇒ Billed ≡ Actual, so a feature-off line round-trips byte-identically (ER-13).</summary>
    public decimal? BilledQuantity { get; init; }

    /// <summary>
    /// The unit both quantities and <see cref="RatePaisa"/> are stated in (WI-10 Gap 2; schema v46); <c>null</c> ⇒
    /// the stock item's own base unit.
    /// <para><b>Appended at the END and marked
    /// <see cref="System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull"/>, exactly like
    /// <c>LedgerDto.Mailing</c>.</b> The canonical JSON options set <c>DefaultIgnoreCondition = Never</c>, so
    /// without the attribute EVERY item line in every existing export would gain a <c>"unitId": null</c> line and
    /// the bytes would change for companies that never state a line unit — breaking ER-13. XML gets it free
    /// (<c>OptId</c> emits no attribute for null).</para>
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore(
        Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public Guid? UnitId { get; init; }
}

// ----------------------------------------------------------------- inventory / order vouchers (catalog §10)

/// <summary>
/// A stock/order voucher (catalog §10) — kept separate from the accounting <see cref="VoucherDto"/> because a
/// Phase-3 stock/order voucher posts no accounting entry. Its content depends on its type's base kind: stock
/// movements (<see cref="Allocations"/> + Stock-Journal <see cref="DestinationAllocations"/>), order lines
/// (<see cref="OrderLines"/>) or counted quantities (<see cref="PhysicalLines"/>).
/// </summary>
public sealed record InventoryVoucherDto
{
    public required Guid Id { get; init; }
    public required Guid TypeId { get; init; }
    public int Number { get; init; }
    public required string Date { get; init; }         // ISO yyyy-MM-dd
    public string? Narration { get; init; }
    public Guid? PartyId { get; init; }
    public bool Cancelled { get; init; }
    public bool PostDated { get; init; }
    public IReadOnlyList<InventoryAllocationDto> Allocations { get; init; } = [];
    public IReadOnlyList<InventoryAllocationDto> DestinationAllocations { get; init; } = [];
    public IReadOnlyList<OrderLineDto> OrderLines { get; init; } = [];
    public IReadOnlyList<PhysicalStockLineDto> PhysicalLines { get; init; } = [];

    /// <summary>Additional-cost lines on a Stock-Journal transfer (Phase 6 slice 3; RQ-20). Empty otherwise.</summary>
    public IReadOnlyList<AdditionalCostLineDto> AdditionalCostLines { get; init; } = [];

    /// <summary>The Job Work Order payload on a Job Work In/Out Order voucher (Phase 6 slice 8; RQ-47); null otherwise.</summary>
    public JobWorkOrderDto? JobWorkOrder { get; init; }

    /// <summary>The Job Work order(s) a Material In/Out voucher fulfils (Phase 6 slice 8; RQ-48) — each id is the
    /// source Job Work Order voucher's id. Empty otherwise.</summary>
    public IReadOnlyList<Guid> OrderLinks { get; init; } = [];
}

/// <summary>One additional-cost line on a Stock-Journal transfer (Phase 6 slice 3; RQ-20), mirroring the domain
/// <c>AdditionalCostLine</c> and the SQLite <c>additional_cost_lines</c> row. Money is integer paisa.</summary>
public sealed record AdditionalCostLineDto
{
    public required Guid LedgerId { get; init; }
    public long AmountPaisa { get; init; }
}

/// <summary>The Job Work Order payload (Phase 6 slice 8; RQ-47), mirroring the domain <c>JobWorkOrder</c> and the
/// SQLite <c>job_work_orders</c> + <c>job_work_order_lines</c> rows. Quantities are exact decimals; money is
/// integer paisa.</summary>
public sealed record JobWorkOrderDto
{
    public required string Direction { get; init; }    // JobWorkDirection name (In/Out)
    public required string OrderNo { get; init; }
    public string? DurationOfProcess { get; init; }
    public string? NatureOfProcessing { get; init; }
    public required Guid FinishedGoodStockItemId { get; init; }
    public decimal FinishedGoodQuantity { get; init; }
    public string? FinishedGoodDueDate { get; init; }  // ISO or null
    public Guid? FinishedGoodGodownId { get; init; }
    public long? FinishedGoodRatePaisa { get; init; }
    public bool TrackingComponents { get; init; }
    public Guid? FillComponentsBomId { get; init; }    // Slice-2 BOM provenance, or null (manual)
    public IReadOnlyList<JobWorkOrderLineDto> Lines { get; init; } = [];
}

/// <summary>One tracked-component line on a <see cref="JobWorkOrderDto"/> (Phase 6 slice 8; RQ-47).</summary>
public sealed record JobWorkOrderLineDto
{
    public required Guid ComponentStockItemId { get; init; }
    public required string Track { get; init; }        // JobWorkComponentTrack name
    public string? DueDate { get; init; }              // ISO or null
    public Guid? GodownId { get; init; }
    public decimal Quantity { get; init; }
    public long? RatePaisa { get; init; }
}

public sealed record InventoryAllocationDto
{
    public required Guid StockItemId { get; init; }
    public required Guid GodownId { get; init; }
    public decimal Quantity { get; init; }
    public required string Direction { get; init; }    // StockDirection name
    public long? RatePaisa { get; init; }               // optional per-unit rate
    public string? BatchLabel { get; init; }
    public Guid? UnitId { get; init; }                  // null = item base unit
}

public sealed record OrderLineDto
{
    public required Guid StockItemId { get; init; }
    public required Guid GodownId { get; init; }
    public decimal Quantity { get; init; }
    public long? RatePaisa { get; init; }               // optional per-unit rate
}

public sealed record PhysicalStockLineDto
{
    public required Guid StockItemId { get; init; }
    public required Guid GodownId { get; init; }
    public decimal CountedQuantity { get; init; }
    public string? BatchLabel { get; init; }
}
