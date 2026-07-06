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

    public IReadOnlyList<StockOpeningBalanceDto> StockOpeningBalances { get; init; } = [];

    public IReadOnlyList<VoucherDto> Vouchers { get; init; } = [];

    /// <summary>Stock/order vouchers (catalog §10): GRN/Delivery/Rejection/Stock-Journal/Physical/PO/SO.</summary>
    public IReadOnlyList<InventoryVoucherDto> InventoryVouchers { get; init; } = [];
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

public sealed record VoucherInventoryLineDto
{
    public required Guid StockItemId { get; init; }
    public required Guid GodownId { get; init; }
    public decimal Quantity { get; init; }
    public long RatePaisa { get; init; }
    public required string Direction { get; init; }    // StockDirection name
    public string? BatchLabel { get; init; }
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
