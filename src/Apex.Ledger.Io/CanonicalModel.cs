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
    public required string IncomeTaxComponent { get; init; }    // IncomeTaxComponent name
    public bool UseForGratuity { get; init; }
    public required string RoundingMethod { get; init; }        // PayHeadRoundingMethod name
    public long RoundingLimitPaisa { get; init; }
    public required string CalculationPeriod { get; init; }     // PayHeadCalculationPeriod name
    public Guid? AttendanceTypeId { get; init; }
    public int? PerDayCalculationBasisDays { get; init; }
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
}

public sealed record GstRateSlabDto
{
    public required Guid Id { get; init; }
    public int RateBasisPoints { get; init; }
    public required string Label { get; init; }
    public bool IsPredefined { get; init; }
}

public sealed record PartyGstDto
{
    public required string RegistrationType { get; init; } // GstRegistrationType name
    public string? Gstin { get; init; }
    public string? StateCode { get; init; }
}

public sealed record StockItemGstDto
{
    public string? HsnSac { get; init; }
    public required string Taxability { get; init; }  // GstTaxability name
    public int? RateBasisPoints { get; init; }
    public required string SupplyType { get; init; }   // GstSupplyType name
}

public sealed record LedgerGstClassificationDto
{
    public required string TaxHead { get; init; }   // GstTaxHead name
    public required string Direction { get; init; } // GstTaxDirection name
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
