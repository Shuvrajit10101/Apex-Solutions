using System.Globalization;
using Apex.Ledger;
using Apex.Ledger.Domain;

namespace Apex.Ledger.Io;

/// <summary>
/// Projects a <see cref="Company"/> aggregate into a <see cref="CanonicalModel"/> (masters + vouchers), with
/// <b>deterministic ordering</b> so the serialised bytes are stable across runs and machines (ER-8). Money is
/// captured as integer paisa via <see cref="MoneyCodec"/>; dates are ISO <c>yyyy-MM-dd</c>, culture-invariant;
/// enums are their member names. This is the shared, format-agnostic step both JSON and XML export call, so the
/// two formats carry the identical payload.
/// </summary>
public static class CanonicalMapper
{
    /// <summary>The canonical envelope format version — bump on any breaking shape change.</summary>
    public const int FormatVersion = 1;

    /// <summary>The persistence schema version this export targets (SQLite schema v29).</summary>
    public const int SchemaVersion = 29;

    /// <summary>The scale forex amounts and rates are captured at (× 1,000,000 = "micros"), mirroring the SQLite
    /// store, so a non-round rate round-trips exactly with no binary float.</summary>
    private const decimal MicroScale = 1_000_000m;

    private static string Iso(DateOnly d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    private static string? Iso(DateOnly? d) => d is { } v ? Iso(v) : null;

    /// <summary>Exact rate → integer micros (rate × 1,000,000); throws if the rate carries a sub-micro tail.</summary>
    private static long ToMicro(decimal rate)
    {
        decimal micro = rate * MicroScale;
        if (micro != decimal.Truncate(micro))
            throw new InvalidOperationException($"Rate {rate} is finer than micros and cannot be serialised losslessly.");
        return (long)micro;
    }

    private static long? ToMicro(decimal? rate) => rate is { } r ? ToMicro(r) : null;

    public static CanonicalModel ToModel(Company company)
    {
        ArgumentNullException.ThrowIfNull(company);

        return new CanonicalModel
        {
            FormatVersion = FormatVersion,
            SchemaVersion = SchemaVersion,
            Company = MapCompany(company),
            Payload = MapPayload(company),
        };
    }

    private static CompanyDto MapCompany(Company c) => new()
    {
        Id = c.Id,
        Name = c.Name,
        MailingName = c.MailingName,
        Address = c.Address,
        Country = c.Country,
        State = c.State,
        Pin = c.Pin,
        FinancialYearStart = Iso(c.FinancialYearStart),
        BooksBeginFrom = Iso(c.BooksBeginFrom),
        BaseCurrencySymbol = c.BaseCurrencySymbol,
        BaseCurrencyName = c.BaseCurrencyName,
        DecimalPlaces = c.DecimalPlaces,
        DecimalUnitName = c.DecimalUnitName,
        Gst = c.Gst is { } g ? MapGstConfig(g) : null,
        Tds = c.Tds is { } td ? MapTdsConfig(td) : null,
        Tcs = c.Tcs is { } tc ? MapTcsConfig(tc) : null,
        UseSeparateActualBilledQuantity = c.UseSeparateActualBilledQuantity,
        EnableMultiplePriceLevels = c.EnableMultiplePriceLevels,
        EnableJobOrderProcessing = c.EnableJobOrderProcessing,
    };

    private static PayloadDto MapPayload(Company c) => new()
    {
        // Masters — ordered by name then id so the byte stream is stable regardless of insertion order.
        Groups = OrderById(c.Groups.Concat(c.ProfitAndLossHead is { } pl ? [pl] : Array.Empty<Group>()),
                    g => g.Name, g => g.Id)
                 .Select(MapGroup).ToList(),
        Ledgers = OrderById(c.Ledgers, l => l.Name, l => l.Id).Select(MapLedger).ToList(),
        VoucherTypes = OrderById(c.VoucherTypes, t => t.Name, t => t.Id).Select(MapVoucherType).ToList(),
        CostCategories = OrderById(c.CostCategories, x => x.Name, x => x.Id).Select(MapCostCategory).ToList(),
        CostCentres = OrderById(c.CostCentres, x => x.Name, x => x.Id).Select(MapCostCentre).ToList(),
        Currencies = OrderById(c.Currencies, x => x.FormalName, x => x.Id).Select(MapCurrency).ToList(),
        ExchangeRates = c.ExchangeRates
            .OrderBy(r => r.CurrencyId).ThenBy(r => r.Date).ThenBy(r => r.Id)
            .Select(MapExchangeRate).ToList(),
        Budgets = OrderById(c.Budgets, x => x.Name, x => x.Id).Select(MapBudget).ToList(),
        Scenarios = OrderById(c.Scenarios, x => x.Name, x => x.Id).Select(MapScenario).ToList(),
        Units = OrderById(c.Units, u => u.Symbol, u => u.Id).Select(MapUnit).ToList(),
        StockGroups = OrderById(c.StockGroups, g => g.Name, g => g.Id).Select(MapStockGroup).ToList(),
        StockCategories = OrderById(c.StockCategories, g => g.Name, g => g.Id).Select(MapStockCategory).ToList(),
        Godowns = OrderById(c.Godowns, g => g.Name, g => g.Id).Select(MapGodown).ToList(),
        StockItems = OrderById(c.StockItems, i => i.Name, i => i.Id).Select(MapStockItem).ToList(),
        // Batch masters — ordered by (item id, batch number, id) so the stream is stable and human-legible.
        BatchMasters = c.BatchMasters
            .OrderBy(b => b.StockItemId).ThenBy(b => b.BatchNumber, StringComparer.Ordinal).ThenBy(b => b.Id)
            .Select(MapBatchMaster).ToList(),
        // Bill-of-Materials masters — ordered by (item id, name, id) so the stream is stable and human-legible.
        // Line order within a BOM is preserved verbatim (it is load-bearing for the recipe).
        BillsOfMaterials = c.BillsOfMaterials
            .OrderBy(b => b.StockItemId).ThenBy(b => b.Name, StringComparer.Ordinal).ThenBy(b => b.Id)
            .Select(MapBom).ToList(),
        StockOpeningBalances = c.StockOpeningBalances.OrderBy(b => b.Id).Select(MapStockOpeningBalance).ToList(),
        // Price Levels — ordered by (name, id) so the stream is stable regardless of insertion order.
        PriceLevels = OrderById(c.PriceLevels, x => x.Name, x => x.Id).Select(MapPriceLevel).ToList(),
        // Price Lists — ordered by (level id, item id, applicable-from, id); the slab order within a list is the
        // list's own ascending slab order, preserved verbatim (it is load-bearing).
        PriceLists = c.PriceLists
            .OrderBy(p => p.PriceLevelId).ThenBy(p => p.StockItemId).ThenBy(p => p.ApplicableFrom).ThenBy(p => p.Id)
            .Select(MapPriceList).ToList(),
        // Reorder definitions — ordered by (scope, target id, id) so the stream is stable.
        ReorderDefinitions = c.ReorderDefinitions
            .OrderBy(d => d.Scope).ThenBy(d => d.TargetId).ThenBy(d => d.Id)
            .Select(MapReorderDefinition).ToList(),
        // Vouchers — ordered by (date, number, id) so the stream is deterministic and human-legible.
        Vouchers = c.Vouchers
            .OrderBy(v => v.Date).ThenBy(v => v.Number).ThenBy(v => v.Id)
            .Select(MapVoucher).ToList(),
        InventoryVouchers = c.InventoryVouchers
            .OrderBy(v => v.Date).ThenBy(v => v.Number).ThenBy(v => v.Id)
            .Select(MapInventoryVoucher).ToList(),
        // TDS deposit challans — ordered by (deposit date, challan no, id) so the stream is stable and human-legible.
        TdsChallans = c.TdsChallans
            .OrderBy(ch => ch.DepositDate).ThenBy(ch => ch.ChallanNo, StringComparer.Ordinal).ThenBy(ch => ch.Id)
            .Select(MapTdsChallan).ToList(),
        // Challan-voucher links — ordered by (challan id, voucher id) so the stream is deterministic.
        ChallanVoucherLinks = c.ChallanVoucherLinks
            .OrderBy(l => l.ChallanId).ThenBy(l => l.VoucherId)
            .Select(l => new ChallanVoucherLinkDto { ChallanId = l.ChallanId, VoucherId = l.VoucherId }).ToList(),
        // TCS deposit challans — same deterministic ordering as the TDS ones (Phase 7 slice 6).
        TcsChallans = c.TcsChallans
            .OrderBy(ch => ch.DepositDate).ThenBy(ch => ch.ChallanNo, StringComparer.Ordinal).ThenBy(ch => ch.Id)
            .Select(MapTcsChallan).ToList(),
        TcsChallanVoucherLinks = c.TcsChallanVoucherLinks
            .OrderBy(l => l.ChallanId).ThenBy(l => l.VoucherId)
            .Select(l => new ChallanVoucherLinkDto { ChallanId = l.ChallanId, VoucherId = l.VoucherId }).ToList(),
    };

    private static TdsChallanDto MapTdsChallan(TdsChallan ch) => new()
    {
        Id = ch.Id,
        ChallanNo = ch.ChallanNo,
        BsrCode = ch.BsrCode,
        DepositDate = Iso(ch.DepositDate),
        AmountPaisa = MoneyCodec.ToPaisa(ch.Amount),
        Section = ch.Section,
        MinorHead = ch.MinorHead,
    };

    private static TcsChallanDto MapTcsChallan(TcsChallan ch) => new()
    {
        Id = ch.Id,
        ChallanNo = ch.ChallanNo,
        BsrCode = ch.BsrCode,
        DepositDate = Iso(ch.DepositDate),
        AmountPaisa = MoneyCodec.ToPaisa(ch.Amount),
        CollectionCode = ch.CollectionCode,
        MinorHead = ch.MinorHead,
    };

    private static IEnumerable<T> OrderById<T>(IEnumerable<T> src, Func<T, string> name, Func<T, Guid> id) =>
        src.OrderBy(name, StringComparer.Ordinal).ThenBy(id);

    // ------------------------------------------------------------- masters

    private static GroupDto MapGroup(Group g) => new()
    {
        Id = g.Id, Name = g.Name, Nature = g.Nature.ToString(),
        ParentId = g.ParentId, Alias = g.Alias, IsPredefined = g.IsPredefined,
    };

    private static LedgerDto MapLedger(Domain.Ledger l) => new()
    {
        Id = l.Id, Name = l.Name, GroupId = l.GroupId,
        OpeningBalancePaisa = MoneyCodec.ToPaisa(l.OpeningBalance),
        OpeningIsDebit = l.OpeningIsDebit, Alias = l.Alias, IsPredefined = l.IsPredefined,
        MaintainBillByBill = l.MaintainBillByBill,
        DefaultCreditPeriodDays = l.DefaultCreditPeriodDays,
        CostCentresApplicable = l.CostCentresApplicable,
        EnableChequePrinting = l.EnableChequePrinting,
        ChequePrintingBankName = l.ChequePrintingBankName,
        CurrencyId = l.CurrencyId,
        Interest = l.Interest is { } i ? MapInterest(i) : null,
        PartyGst = l.PartyGst is { } p ? MapPartyGst(p) : null,
        SalesPurchaseGst = l.SalesPurchaseGst is { } s ? MapStockItemGst(s) : null,
        GstClassification = l.GstClassification is { } gc ? MapGstClassification(gc) : null,
        MethodOfAppropriation = l.MethodOfAppropriation is { } m ? m.ToString() : null,
        DefaultPriceLevelId = l.DefaultPriceLevelId,
        TdsApplicable = l.TdsApplicable,
        TdsNatureOfPaymentId = l.TdsNatureOfPaymentId,
        DeducteeType = l.DeducteeType is { } dt ? dt.ToString() : null,
        PartyPan = l.PartyPan,
        DeductTdsInSameVoucher = l.DeductTdsInSameVoucher,
        TcsApplicable = l.TcsApplicable,
        TcsNatureOfGoodsId = l.TcsNatureOfGoodsId,
        CollecteeType = l.CollecteeType is { } ct ? ct.ToString() : null,
        TdsTcsClassification = l.TdsTcsClassification is { } k ? k.ToString() : null,
    };

    private static InterestParametersDto MapInterest(InterestParameters i) => new()
    {
        Enabled = i.Enabled, RatePercent = i.RatePercent, Per = i.Per.ToString(),
        OnBalance = i.OnBalance.ToString(), Applicability = i.Applicability.ToString(),
        CalculateFrom = Iso(i.CalculateFrom), Style = i.Style.ToString(),
        RoundingMethod = i.RoundingMethod.ToString(), RoundingDecimals = i.RoundingDecimals,
    };

    private static CostCategoryDto MapCostCategory(CostCategory x) => new()
    {
        Id = x.Id, Name = x.Name, AllocateRevenueItems = x.AllocateRevenueItems,
        AllocateNonRevenueItems = x.AllocateNonRevenueItems, IsPredefined = x.IsPredefined,
    };

    private static CostCentreDto MapCostCentre(CostCentre x) => new()
    {
        Id = x.Id, Name = x.Name, CategoryId = x.CategoryId, ParentId = x.ParentId, Alias = x.Alias,
    };

    private static CurrencyDto MapCurrency(Currency x) => new()
    {
        Id = x.Id, Symbol = x.Symbol, FormalName = x.FormalName,
        DecimalPlaces = x.DecimalPlaces, IsBaseCurrency = x.IsBaseCurrency,
    };

    private static ExchangeRateDto MapExchangeRate(ExchangeRate x) => new()
    {
        Id = x.Id, CurrencyId = x.CurrencyId, Date = Iso(x.Date),
        StandardRateMicro = ToMicro(x.StandardRate),
        SellingRateMicro = ToMicro(x.SellingRate), BuyingRateMicro = ToMicro(x.BuyingRate),
    };

    private static BudgetDto MapBudget(Budget x) => new()
    {
        Id = x.Id, Name = x.Name, UnderId = x.UnderId,
        PeriodFrom = Iso(x.PeriodFrom), PeriodTo = Iso(x.PeriodTo),
        Lines = x.Lines.Select(MapBudgetLine).ToList(),
    };

    private static BudgetLineDto MapBudgetLine(BudgetLine l) => new()
    {
        GroupId = l.GroupId, LedgerId = l.LedgerId, BudgetType = l.Type.ToString(),
        AmountPaisa = MoneyCodec.ToPaisa(l.Amount),
    };

    private static ScenarioDto MapScenario(Scenario x) => new()
    {
        Id = x.Id, Name = x.Name, IncludeActuals = x.IncludeActuals,
        IncludedTypeIds = x.IncludedTypeIds.OrderBy(g => g).ToList(),
        ExcludedTypeIds = x.ExcludedTypeIds.OrderBy(g => g).ToList(),
    };

    private static VoucherTypeDto MapVoucherType(VoucherType t) => new()
    {
        Id = t.Id, Name = t.Name, BaseType = t.BaseType.ToString(), Numbering = t.Numbering.ToString(),
        DefaultShortcut = t.DefaultShortcut, Abbreviation = t.Abbreviation,
        IsActive = t.IsActive, IsPredefined = t.IsPredefined,
        AffectsAccounts = t.AffectsAccounts, AffectsStock = t.AffectsStock,
        UseAsManufacturingJournal = t.UseAsManufacturingJournal,
        TrackAdditionalCosts = t.TrackAdditionalCosts,
        AllowZeroValuedTransactions = t.AllowZeroValuedTransactions,
        UseForPos = t.UseForPos,
        UseForJobWork = t.UseForJobWork,
        AllowConsumption = t.AllowConsumption,
        IsStatPayment = t.IsStatPayment,
        PosConfig = t.PosConfig is { } pc ? MapPosConfig(pc) : null,
    };

    private static PosConfigDto MapPosConfig(PosConfig c) => new()
    {
        DefaultGodownId = c.DefaultGodownId,
        DefaultPartyId = c.DefaultPartyId,
        PrintAfterSave = c.PrintAfterSave,
        DefaultTitle = c.DefaultTitle,
        Message1 = c.Message1,
        Message2 = c.Message2,
        Declaration = c.Declaration,
        // Ordered by tender-type ordinal so the byte stream is stable regardless of dictionary insertion order.
        TenderLedgerDefaults = c.TenderLedgerDefaults
            .OrderBy(kv => (int)kv.Key)
            .Select(kv => new PosTenderLedgerDefaultDto { TenderType = kv.Key.ToString(), LedgerId = kv.Value })
            .ToList(),
    };

    private static PriceLevelDto MapPriceLevel(PriceLevel x) => new() { Id = x.Id, Name = x.Name };

    private static PriceListDto MapPriceList(PriceList x) => new()
    {
        Id = x.Id, PriceLevelId = x.PriceLevelId, StockItemId = x.StockItemId,
        ApplicableFrom = Iso(x.ApplicableFrom),
        // Slab order is the list's own ascending slab order — preserved verbatim (NOT reordered).
        Slabs = x.Slabs.Select(s => new PriceListSlabDto
        {
            FromQty = s.FromQty, ToQty = s.ToQty, RatePaisa = MoneyCodec.ToPaisa(s.Rate),
            DiscountPercent = s.DiscountPercent,
        }).ToList(),
    };

    private static ReorderDefinitionDto MapReorderDefinition(ReorderDefinition d) => new()
    {
        Id = d.Id, Scope = d.Scope.ToString(), TargetId = d.TargetId,
        ReorderAdvanced = d.ReorderAdvanced, ReorderQuantity = d.ReorderQuantity,
        MinQtyAdvanced = d.MinQtyAdvanced, MinOrderQuantity = d.MinOrderQuantity,
        PeriodCount = d.PeriodCount,
        PeriodUnit = d.PeriodUnit is { } u ? u.ToString() : null,
        Criteria = d.Criteria is { } cr ? cr.ToString() : null,
    };

    private static UnitDto MapUnit(Unit u) => new()
    {
        Id = u.Id, Symbol = u.Symbol, FormalName = u.FormalName, IsCompound = u.IsCompound,
        UnitQuantityCode = u.UnitQuantityCode, DecimalPlaces = u.DecimalPlaces,
        FirstUnitId = u.FirstUnitId, TailUnitId = u.TailUnitId,
        ConversionNumerator = u.ConversionNumerator, ConversionDenominator = u.ConversionDenominator,
    };

    private static StockGroupDto MapStockGroup(StockGroup g) => new()
    {
        Id = g.Id, Name = g.Name, ParentId = g.ParentId, Alias = g.Alias, AddQuantities = g.AddQuantities,
    };

    private static StockCategoryDto MapStockCategory(StockCategory g) => new()
    {
        Id = g.Id, Name = g.Name, ParentId = g.ParentId, Alias = g.Alias,
    };

    private static GodownDto MapGodown(Godown g) => new()
    {
        Id = g.Id, Name = g.Name, ParentId = g.ParentId, Alias = g.Alias,
        ThirdParty = g.ThirdParty, IsMainLocation = g.IsMainLocation,
    };

    private static StockItemDto MapStockItem(StockItem i) => new()
    {
        Id = i.Id, Name = i.Name, StockGroupId = i.StockGroupId, BaseUnitId = i.BaseUnitId,
        CategoryId = i.CategoryId, Alias = i.Alias, ValuationMethod = i.ValuationMethod.ToString(),
        HsnSacCode = i.HsnSacCode, IsTaxable = i.IsTaxable,
        StandardCostPaisa = MoneyCodec.ToPaisa(i.StandardCost),
        ReorderLevel = i.ReorderLevel, MinimumOrderQuantity = i.MinimumOrderQuantity,
        Gst = i.Gst is { } g ? MapStockItemGst(g) : null,
        MaintainInBatches = i.MaintainInBatches,
        TrackManufacturingDate = i.TrackManufacturingDate,
        UseExpiryDates = i.UseExpiryDates,
        SetComponents = i.SetComponents,
        TcsNatureOfGoodsId = i.TcsNatureOfGoodsId,
    };

    private static BatchMasterDto MapBatchMaster(BatchMaster b) => new()
    {
        Id = b.Id, StockItemId = b.StockItemId, BatchNumber = b.BatchNumber,
        ManufacturingDate = Iso(b.ManufacturingDate), ExpiryDate = Iso(b.ExpiryDate),
        ExpiryPeriod = b.ExpiryPeriod?.RawText, GodownId = b.GodownId,
        InwardQuantity = b.InwardQuantity, InwardRatePaisa = MoneyCodec.ToPaisa(b.InwardRate),
    };

    private static BillOfMaterialsDto MapBom(BillOfMaterials b) => new()
    {
        Id = b.Id, StockItemId = b.StockItemId, Name = b.Name, UnitOfManufacture = b.UnitOfManufacture,
        // Line order is the recipe's own order — preserved verbatim (NOT reordered).
        Lines = b.Lines.Select(MapBomLine).ToList(),
    };

    private static BomLineDto MapBomLine(BomLine l) => new()
    {
        LineType = l.LineType.ToString(), ComponentStockItemId = l.ComponentStockItemId, GodownId = l.GodownId,
        QuantityPerBlock = l.QuantityPerBlock,
        RatePaisa = l.Rate is { } r ? MoneyCodec.ToPaisa(r) : null,
        PercentOfFinishedGoodCost = l.PercentOfFinishedGoodCost,
    };

    private static StockOpeningBalanceDto MapStockOpeningBalance(StockOpeningBalance b) => new()
    {
        Id = b.Id, StockItemId = b.StockItemId, GodownId = b.GodownId,
        Quantity = b.Quantity, RatePaisa = MoneyCodec.ToPaisa(b.Rate), BatchLabel = b.BatchLabel,
        ManufacturingDate = Iso(b.ManufacturingDate), ExpiryDate = Iso(b.ExpiryDate),
    };

    // ------------------------------------------------------------- gst value objects

    private static GstConfigDto MapGstConfig(GstConfig g) => new()
    {
        Enabled = g.Enabled, Gstin = g.Gstin, HomeStateCode = g.HomeStateCode,
        RegistrationType = g.RegistrationType.ToString(), ApplicableFrom = Iso(g.ApplicableFrom),
        Periodicity = g.Periodicity.ToString(),
        RateSlabs = g.RateSlabs.OrderBy(s => s.RateBasisPoints).ThenBy(s => s.Id)
            .Select(s => new GstRateSlabDto
            {
                Id = s.Id, RateBasisPoints = s.RateBasisPoints, Label = s.Label, IsPredefined = s.IsPredefined,
            }).ToList(),
    };

    private static PartyGstDto MapPartyGst(PartyGstDetails p) => new()
    {
        RegistrationType = p.RegistrationType.ToString(), Gstin = p.Gstin, StateCode = p.StateCode,
    };

    private static StockItemGstDto MapStockItemGst(StockItemGstDetails s) => new()
    {
        HsnSac = s.HsnSac, Taxability = s.Taxability.ToString(),
        RateBasisPoints = s.RateBasisPoints, SupplyType = s.SupplyType.ToString(),
    };

    private static LedgerGstClassificationDto MapGstClassification(LedgerGstClassification c) => new()
    {
        TaxHead = c.TaxHead.ToString(), Direction = c.Direction.ToString(),
    };

    // ------------------------------------------------------------- tds / tcs value objects (Phase 7 slice 1)

    private static TdsConfigDto MapTdsConfig(TdsConfig t) => new()
    {
        Enabled = t.Enabled, Tan = t.Tan, DeductorType = t.DeductorType.ToString(),
        ResponsiblePersonName = t.ResponsiblePersonName, ResponsiblePersonPan = t.ResponsiblePersonPan,
        ResponsiblePersonDesignation = t.ResponsiblePersonDesignation, ResponsiblePersonAddress = t.ResponsiblePersonAddress,
        SurchargeApplicable = t.SurchargeApplicable, CessApplicable = t.CessApplicable,
        Periodicity = t.Periodicity.ToString(), ApplicableFrom = Iso(t.ApplicableFrom),
        // Ordered by section code then id so the byte stream is stable regardless of insertion order.
        NaturesOfPayment = t.NaturesOfPayment
            .OrderBy(n => n.SectionCode, StringComparer.Ordinal).ThenBy(n => n.Id)
            .Select(MapNatureOfPayment).ToList(),
    };

    private static TcsConfigDto MapTcsConfig(TcsConfig t) => new()
    {
        Enabled = t.Enabled, Tan = t.Tan, CollectorType = t.CollectorType.ToString(),
        ResponsiblePersonName = t.ResponsiblePersonName, ResponsiblePersonPan = t.ResponsiblePersonPan,
        ResponsiblePersonDesignation = t.ResponsiblePersonDesignation, ResponsiblePersonAddress = t.ResponsiblePersonAddress,
        SurchargeApplicable = t.SurchargeApplicable, CessApplicable = t.CessApplicable,
        Periodicity = t.Periodicity.ToString(), ApplicableFrom = Iso(t.ApplicableFrom),
        NaturesOfGoods = t.NaturesOfGoods
            .OrderBy(n => n.CollectionCode, StringComparer.Ordinal).ThenBy(n => n.Id)
            .Select(MapNatureOfGoods).ToList(),
    };

    private static NatureOfPaymentDto MapNatureOfPayment(NatureOfPayment n) => new()
    {
        Id = n.Id, SectionCode = n.SectionCode, Name = n.Name,
        RateWithPanBp = n.RateWithPanBp, RateWithoutPanBp = n.RateWithoutPanBp,
        SingleThresholdPaisa = n.SingleTransactionThreshold is { } s ? MoneyCodec.ToPaisa(s) : null,
        CumulativeThresholdPaisa = n.CumulativeThreshold is { } c ? MoneyCodec.ToPaisa(c) : null,
        FvuSectionCode = n.FvuSectionCode, EffectiveFrom = Iso(n.EffectiveFrom), IsPredefined = n.IsPredefined,
    };

    private static NatureOfGoodsDto MapNatureOfGoods(NatureOfGoods n) => new()
    {
        Id = n.Id, CollectionCode = n.CollectionCode, Name = n.Name,
        RateWithPanBp = n.RateWithPanBp, RateWithoutPanBp = n.RateWithoutPanBp,
        ThresholdPaisa = n.Threshold is { } th ? MoneyCodec.ToPaisa(th) : null,
        BaseIncludesGst = n.BaseIncludesGst, FvuCode = n.FvuCode, EffectiveFrom = Iso(n.EffectiveFrom),
        IsPredefined = n.IsPredefined, IsLegacy = n.IsLegacy, LegacyCutoff = Iso(n.LegacyCutoff),
    };

    // ------------------------------------------------------------- vouchers

    private static VoucherDto MapVoucher(Voucher v) => new()
    {
        Id = v.Id, TypeId = v.TypeId, Number = v.Number, Date = Iso(v.Date),
        Narration = v.Narration, PartyId = v.PartyId,
        Cancelled = v.Cancelled, Optional = v.Optional, PostDated = v.PostDated,
        ApplicableUpto = Iso(v.ApplicableUpto),
        Lines = v.Lines.Select(MapEntryLine).ToList(),
        InventoryLines = v.InventoryLines.Select(MapVoucherInventoryLine).ToList(),
        // POS tenders preserved in their declared (stable) order — Gift, Card, Cheque, Cash.
        PosTenders = v.PosTenders.Select(MapPosTender).ToList(),
    };

    private static PosTenderDto MapPosTender(PosTender t) => new()
    {
        TenderType = t.Type.ToString(), LedgerId = t.LedgerId, AmountPaisa = MoneyCodec.ToPaisa(t.Amount),
        TenderedPaisa = t.Tendered is { } td ? MoneyCodec.ToPaisa(td) : null,
        ChangePaisa = t.Change is { } ch ? MoneyCodec.ToPaisa(ch) : null,
        CardNo = t.CardNo, BankName = t.BankName, ChequeNo = t.ChequeNo,
    };

    private static EntryLineDto MapEntryLine(EntryLine l) => new()
    {
        LedgerId = l.LedgerId, AmountPaisa = MoneyCodec.ToPaisa(l.Amount), Side = l.Side.ToString(),
        BillAllocations = l.BillAllocations.Select(MapBillAllocation).ToList(),
        CostAllocations = l.CostAllocations.Select(MapCostAllocation).ToList(),
        BankAllocation = l.BankAllocation is { } b ? MapBankAllocation(b) : null,
        Forex = l.Forex is { } f ? MapForex(f) : null,
        Gst = l.Gst is { } g ? MapGstLineTax(g) : null,
        Tds = l.Tds is { } t ? MapTdsLineTax(t) : null,
        Tcs = l.Tcs is { } tc ? MapTcsLineTax(tc) : null,
    };

    private static TdsLineTaxDto MapTdsLineTax(TdsLineTax t) => new()
    {
        NatureId = t.NatureId, SectionCode = t.SectionCode,
        AssessableValuePaisa = MoneyCodec.ToPaisa(t.AssessableValue), RateBasisPoints = t.RateBasisPoints,
        TdsAmountPaisa = MoneyCodec.ToPaisa(t.TdsAmount), DeducteeLedgerId = t.DeducteeLedgerId,
        PanApplied = t.PanApplied,
    };

    private static TcsLineTaxDto MapTcsLineTax(TcsLineTax t) => new()
    {
        NatureId = t.NatureId, CollectionCode = t.CollectionCode,
        AssessableValuePaisa = MoneyCodec.ToPaisa(t.AssessableValue), RateBasisPoints = t.RateBasisPoints,
        TcsAmountPaisa = MoneyCodec.ToPaisa(t.TcsAmount), CollecteeLedgerId = t.CollecteeLedgerId,
        PanApplied = t.PanApplied,
    };

    private static BillAllocationDto MapBillAllocation(BillAllocation a) => new()
    {
        RefType = a.RefType.ToString(), Name = a.Name, AmountPaisa = MoneyCodec.ToPaisa(a.Amount),
        DueDate = Iso(a.DueDate), CreditPeriodDays = a.CreditPeriodDays,
    };

    private static CostAllocationDto MapCostAllocation(CostAllocation a) => new()
    {
        CategoryId = a.CategoryId, CentreId = a.CentreId, AmountPaisa = MoneyCodec.ToPaisa(a.Amount),
    };

    private static BankAllocationDto MapBankAllocation(BankAllocation b) => new()
    {
        TransactionType = b.TransactionType.ToString(), InstrumentNumber = b.InstrumentNumber,
        InstrumentDate = Iso(b.InstrumentDate), BankDate = Iso(b.BankDate),
    };

    private static ForexDto MapForex(ForexInfo f) => new()
    {
        CurrencyId = f.CurrencyId, ForexAmountPaisa = MoneyCodec.ToPaisa(f.ForexAmount), RateMicro = ToMicro(f.Rate),
    };

    private static GstLineTaxDto MapGstLineTax(GstLineTax g) => new()
    {
        TaxHead = g.TaxHead.ToString(), RateBasisPoints = g.RateBasisPoints,
        TaxableValuePaisa = MoneyCodec.ToPaisa(g.TaxableValue),
    };

    private static VoucherInventoryLineDto MapVoucherInventoryLine(VoucherInventoryLine l) => new()
    {
        StockItemId = l.StockItemId, GodownId = l.GodownId, Quantity = l.Quantity,
        RatePaisa = MoneyCodec.ToPaisa(l.Rate), Direction = l.Direction.ToString(), BatchLabel = l.BatchLabel,
        // Emit Billed only when it differs from Actual (feature off ⇒ null ⇒ byte-identical, ER-13).
        BilledQuantity = l.BilledQuantity == l.Quantity ? null : l.BilledQuantity,
    };

    // ------------------------------------------------------------- inventory / order vouchers

    private static InventoryVoucherDto MapInventoryVoucher(InventoryVoucher v) => new()
    {
        Id = v.Id, TypeId = v.TypeId, Number = v.Number, Date = Iso(v.Date),
        Narration = v.Narration, PartyId = v.PartyId, Cancelled = v.Cancelled, PostDated = v.PostDated,
        Allocations = v.Allocations.Select(MapInventoryAllocation).ToList(),
        DestinationAllocations = v.DestinationAllocations.Select(MapInventoryAllocation).ToList(),
        OrderLines = v.OrderLines.Select(MapOrderLine).ToList(),
        PhysicalLines = v.PhysicalLines.Select(MapPhysicalStockLine).ToList(),
        // Additional-cost lines preserve their own order (load-bearing for apportionment reporting).
        AdditionalCostLines = v.AdditionalCostLines
            .Select(a => new AdditionalCostLineDto { LedgerId = a.LedgerId, AmountPaisa = MoneyCodec.ToPaisa(a.Amount) })
            .ToList(),
        JobWorkOrder = v.JobWorkOrder is { } jwo ? MapJobWorkOrder(jwo) : null,
        // Order links preserved verbatim (each is a source Job Work Order voucher id).
        OrderLinks = v.OrderLinks.ToList(),
    };

    private static JobWorkOrderDto MapJobWorkOrder(JobWorkOrder j) => new()
    {
        Direction = j.Direction.ToString(), OrderNo = j.OrderNo,
        DurationOfProcess = j.DurationOfProcess, NatureOfProcessing = j.NatureOfProcessing,
        FinishedGoodStockItemId = j.FinishedGoodStockItemId, FinishedGoodQuantity = j.FinishedGoodQuantity,
        FinishedGoodDueDate = Iso(j.FinishedGoodDueDate), FinishedGoodGodownId = j.FinishedGoodGodownId,
        FinishedGoodRatePaisa = j.FinishedGoodRate is { } r ? MoneyCodec.ToPaisa(r) : null,
        TrackingComponents = j.TrackingComponents, FillComponentsBomId = j.FillComponentsBomId,
        // Component line order is the order's own order — preserved verbatim.
        Lines = j.Lines.Select(l => new JobWorkOrderLineDto
        {
            ComponentStockItemId = l.ComponentStockItemId, Track = l.Track.ToString(),
            DueDate = Iso(l.DueDate), GodownId = l.GodownId, Quantity = l.Quantity,
            RatePaisa = l.Rate is { } r ? MoneyCodec.ToPaisa(r) : null,
        }).ToList(),
    };

    private static InventoryAllocationDto MapInventoryAllocation(InventoryAllocation a) => new()
    {
        StockItemId = a.StockItemId, GodownId = a.GodownId, Quantity = a.Quantity,
        Direction = a.Direction.ToString(), RatePaisa = MoneyCodec.ToPaisa(a.Rate),
        BatchLabel = a.BatchLabel, UnitId = a.UnitId,
    };

    private static OrderLineDto MapOrderLine(OrderLine l) => new()
    {
        StockItemId = l.StockItemId, GodownId = l.GodownId, Quantity = l.Quantity,
        RatePaisa = MoneyCodec.ToPaisa(l.Rate),
    };

    private static PhysicalStockLineDto MapPhysicalStockLine(PhysicalStockLine l) => new()
    {
        StockItemId = l.StockItemId, GodownId = l.GodownId, CountedQuantity = l.CountedQuantity, BatchLabel = l.BatchLabel,
    };
}
