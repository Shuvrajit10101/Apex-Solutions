using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Services;

namespace Apex.Ledger.Io;

/// <summary>
/// The resolved, pre-validated import plan produced by <see cref="CompanyImportService"/>'s pre-flight pass. It
/// holds the DTO-id → target-name mapping decisions per master kind and, at <see cref="Execute"/> time, creates the
/// masters through the domain services (in dependency order) and posts the vouchers through
/// <see cref="LedgerService"/>. All FK translation is by name (the domain's own lookup key), so a fresh
/// differently-Guid'd company reconciles to the paisa (PR-4).
/// </summary>
internal sealed class ImportPlan
{
    private readonly CanonicalModel _model;
    private readonly DuplicatePolicy _policy;

    // DTO-id → { target name, duplicate? } per master kind (filled by the planner).
    public Dictionary<Guid, MasterTarget> GroupTargets { get; } = new();
    public Dictionary<Guid, MasterTarget> LedgerTargets { get; } = new();
    public Dictionary<Guid, MasterTarget> VoucherTypeTargets { get; } = new();
    public Dictionary<Guid, MasterTarget> UnitTargets { get; } = new();
    public Dictionary<Guid, MasterTarget> StockGroupTargets { get; } = new();
    public Dictionary<Guid, MasterTarget> StockCategoryTargets { get; } = new();
    public Dictionary<Guid, MasterTarget> GodownTargets { get; } = new();
    public Dictionary<Guid, MasterTarget> StockItemTargets { get; } = new();
    public Dictionary<Guid, MasterTarget> CostCategoryTargets { get; } = new();
    public Dictionary<Guid, MasterTarget> CostCentreTargets { get; } = new();
    public Dictionary<Guid, MasterTarget> CurrencyTargets { get; } = new();
    public Dictionary<Guid, MasterTarget> BudgetTargets { get; } = new();
    public Dictionary<Guid, MasterTarget> ScenarioTargets { get; } = new();

    public ImportPlan(CanonicalModel model, DuplicatePolicy policy)
    {
        _model = model;
        _policy = policy;
    }

    // ---- reference resolvability (used by the pre-flight, no mutation) ----

    public bool CanResolveGroup(Guid id, Company t) =>
        GroupTargets.ContainsKey(id) || t.FindGroup(id) is not null;
    public bool CanResolveLedger(Guid id, Company t) =>
        LedgerTargets.ContainsKey(id) || t.FindLedger(id) is not null;
    public bool CanResolveVoucherType(Guid id, Company t) =>
        VoucherTypeTargets.ContainsKey(id) || t.FindVoucherType(id) is not null;
    public bool CanResolveUnit(Guid id, Company t) =>
        UnitTargets.ContainsKey(id) || t.FindUnit(id) is not null;
    public bool CanResolveStockGroup(Guid id, Company t) =>
        StockGroupTargets.ContainsKey(id) || t.FindStockGroup(id) is not null;
    public bool CanResolveStockCategory(Guid id, Company t) =>
        StockCategoryTargets.ContainsKey(id) || t.FindStockCategory(id) is not null;
    public bool CanResolveGodown(Guid id, Company t) =>
        GodownTargets.ContainsKey(id) || t.FindGodown(id) is not null;
    public bool CanResolveStockItem(Guid id, Company t) =>
        StockItemTargets.ContainsKey(id) || t.FindStockItem(id) is not null;
    public bool CanResolveCostCategory(Guid id, Company t) =>
        CostCategoryTargets.ContainsKey(id) || t.FindCostCategory(id) is not null;
    public bool CanResolveCostCentre(Guid id, Company t) =>
        CostCentreTargets.ContainsKey(id) || t.FindCostCentre(id) is not null;
    public bool CanResolveCurrency(Guid id, Company t) =>
        CurrencyTargets.ContainsKey(id) || t.FindCurrency(id) is not null;

    // ---- execute: create masters (engine-routed) then post vouchers (engine-routed) ----

    /// <summary>
    /// Creates the masters and posts the vouchers on <paramref name="t"/> through the domain services, recording
    /// every mutation in <paramref name="journal"/> for rollback. Returns (created, reused, posted) counts. Throws
    /// on any engine rejection — the caller rolls the journal back.
    /// </summary>
    public (int Created, int Reused, int Posted) Execute(Company t, ApplyJournal journal)
    {
        var created = 0;
        var reused = 0;

        // Live DTO-id → target-domain-id maps, filled as each master is created / resolved.
        var groupId = new Dictionary<Guid, Guid>();
        var ledgerId = new Dictionary<Guid, Guid>();
        var voucherTypeId = new Dictionary<Guid, Guid>();
        var unitId = new Dictionary<Guid, Guid>();
        var stockGroupId = new Dictionary<Guid, Guid>();
        var stockCategoryId = new Dictionary<Guid, Guid>();
        var godownId = new Dictionary<Guid, Guid>();
        var stockItemId = new Dictionary<Guid, Guid>();
        var costCategoryId = new Dictionary<Guid, Guid>();
        var costCentreId = new Dictionary<Guid, Guid>();
        var currencyId = new Dictionary<Guid, Guid>();
        var priceLevelId = new Dictionary<Guid, Guid>();   // Phase 6 slice 5
        var bomId = new Dictionary<Guid, Guid>();          // Phase 6 slice 2 (needed to resolve Job Work fill-BOM refs)
        var tdsNatureId = new Dictionary<Guid, Guid>();    // Phase 7 slice 1 (DTO nature id → re-minted target id)
        var tcsNatureId = new Dictionary<Guid, Guid>();

        // Phase 6 slice 7: POS retail-till configs reference godowns + ledgers created later, so they are attached in
        // a deferred pass once those masters exist. Each pair is a newly-created POS voucher type + its exported config.
        var posConfigWork = new List<(VoucherType Domain, PosConfigDto Dto)>();

        var inv = new InventoryService(t);
        var gstService = new GstService(t);

        // 0) Company header + GST config FIRST. EnableGst auto-creates the 6 tax ledgers + Round Off, so those
        //    ledgers must be resolved to the seeded/auto-created instances (never re-created from the ledger list).
        ApplyCompanyHeader(t, journal);
        if (_model.Company.Gst is { Enabled: true } gstDto)
        {
            var config = BuildGstConfig(gstDto);
            journal.RecordGstEnabledBefore();          // snapshot the ORIGINAL config + ledger set BEFORE EnableGst mutates
            gstService.EnableGst(config); // idempotent; auto-creates tax + round-off ledgers under Duties & Taxes
        }

        // 0a) TDS/TCS config (Phase 7 slice 1). EnableTds/EnableTcs auto-create the "TDS Payable"/"TCS Payable"
        //     ledgers (reused by name from the ledger list below, like the GST tax ledgers) and preserve the
        //     exported Nature-of-Payment/Goods masters. Each master is re-minted with a fresh Guid; the DTO id →
        //     target id map lets a ledger's/item's default-nature reference resolve across companies.
        var tdsTcsService = new TdsTcsService(t);
        if (_model.Company.Tds is { Enabled: true } tdsDto)
        {
            var (config, map) = BuildTdsConfig(tdsDto);
            journal.RecordTdsEnabledBefore();
            tdsTcsService.EnableTds(config);
            foreach (var kv in map) tdsNatureId[kv.Key] = kv.Value;
        }
        if (_model.Company.Tcs is { Enabled: true } tcsDto)
        {
            var (config, map) = BuildTcsConfig(tcsDto);
            journal.RecordTcsEnabledBefore();
            tdsTcsService.EnableTcs(config);
            foreach (var kv in map) tcsNatureId[kv.Key] = kv.Value;
        }

        // 1) Groups — parents before children (topological by ParentId within the imported set).
        foreach (var g in OrderGroupsParentsFirst(_model.Payload.Groups))
        {
            var target = GroupTargets[g.Id];
            if (target.IsDuplicate)
            {
                // Reuse the existing predefined/seeded group by name — including the reserved Profit & Loss head,
                // which lives outside the 28 Groups and so needs the head-aware lookup (else it would be re-created).
                var existing = t.FindGroupOrHeadByName(target.Name)!;
                groupId[g.Id] = existing.Id;
                reused++;
                continue;
            }
            var parent = g.ParentId is { } pid ? ResolveGroupId(pid, groupId, t) : (Guid?)null;
            var domain = new Group(Guid.NewGuid(), g.Name, ParseEnum<GroupNature>(g.Nature), parent, g.Alias,
                isPredefined: false);
            t.AddGroup(domain);
            journal.RecordGroup(domain);
            groupId[g.Id] = domain.Id;
            created++;
        }

        // 2) Voucher types.
        foreach (var vt in _model.Payload.VoucherTypes)
        {
            var target = VoucherTypeTargets[vt.Id];
            if (target.IsDuplicate)
            {
                voucherTypeId[vt.Id] = t.FindVoucherTypeByName(target.Name)!.Id;
                reused++;
                continue;
            }
            // Phase 6: the additional-cost / zero-valued / POS / job-work flags ride the type verbatim. The POS
            // retail-till config (which references godowns + ledgers created later) is attached in a deferred pass.
            var domain = new VoucherType(Guid.NewGuid(), vt.Name,
                ParseEnum<VoucherBaseType>(vt.BaseType), ParseEnum<NumberingMethod>(vt.Numbering),
                vt.DefaultShortcut, vt.Abbreviation, vt.IsActive, isPredefined: false,
                affectsAccounts: vt.AffectsAccounts, affectsStock: vt.AffectsStock,
                useAsManufacturingJournal: vt.UseAsManufacturingJournal,
                trackAdditionalCosts: vt.TrackAdditionalCosts,
                allowZeroValuedTransactions: vt.AllowZeroValuedTransactions,
                useForPos: vt.UseForPos,
                useForJobWork: vt.UseForJobWork,
                allowConsumption: vt.AllowConsumption,
                isStatPayment: vt.IsStatPayment);
            t.AddVoucherType(domain);
            journal.RecordVoucherType(domain);
            voucherTypeId[vt.Id] = domain.Id;
            if (vt.PosConfig is { } pcDto) posConfigWork.Add((domain, pcDto));
            created++;
        }

        // 2a) Price Levels (Phase 6 slice 5; RQ-26) — bare id+name masters referenced by a party ledger's default
        //     level and by every dated price list, so they are created BEFORE ledgers. Reused by name on an overlay
        //     import (a fresh company seeds none, so all are new here).
        foreach (var pl in _model.Payload.PriceLevels)
        {
            if (t.FindPriceLevelByName(pl.Name) is { } existing)
            {
                priceLevelId[pl.Id] = existing.Id;
                reused++;
                continue;
            }
            var domain = new PriceLevel(Guid.NewGuid(), pl.Name);
            t.AddPriceLevel(domain);
            journal.RecordPriceLevel(domain);
            priceLevelId[pl.Id] = domain.Id;
            created++;
        }

        // 2b) Currencies FIRST among the remaining masters — a ledger's CurrencyId and a forex line reference them,
        //     and an exchange rate hangs off one. The base ₹/INR is seeded on CreateSeeded, so it is reused by name.
        foreach (var cur in _model.Payload.Currencies)
        {
            var target = CurrencyTargets[cur.Id];
            if (target.IsDuplicate)
            {
                currencyId[cur.Id] = t.FindCurrencyByName(target.Name)!.Id;
                reused++;
                continue;
            }
            var domain = new Currency(Guid.NewGuid(), cur.Symbol, cur.FormalName, cur.DecimalPlaces, cur.IsBaseCurrency);
            t.AddCurrency(domain);
            journal.RecordCurrency(domain);
            currencyId[cur.Id] = domain.Id;
            created++;
        }

        // 2c) Exchange rates (dated quotes hung off a currency). Non-master rows; always added fresh.
        foreach (var r in _model.Payload.ExchangeRates)
        {
            var domain = new ExchangeRate(Guid.NewGuid(), ResolveCurrencyId(r.CurrencyId, currencyId, t),
                CompanyImportService.ParseDate(r.Date), FromMicro(r.StandardRateMicro),
                r.SellingRateMicro is { } s ? FromMicro(s) : null,
                r.BuyingRateMicro is { } b ? FromMicro(b) : null);
            t.AddExchangeRate(domain);
            journal.RecordExchangeRate(domain);
        }

        // 2d) Cost categories — the seeded "Primary Cost Category" is reused by name.
        foreach (var cc in _model.Payload.CostCategories)
        {
            var target = CostCategoryTargets[cc.Id];
            if (target.IsDuplicate)
            {
                costCategoryId[cc.Id] = t.FindCostCategoryByName(target.Name)!.Id;
                reused++;
                continue;
            }
            var domain = new CostCategory(Guid.NewGuid(), cc.Name, cc.AllocateRevenueItems,
                cc.AllocateNonRevenueItems, isPredefined: false);
            t.AddCostCategory(domain);
            journal.RecordCostCategory(domain);
            costCategoryId[cc.Id] = domain.Id;
            created++;
        }

        // 2e) Cost centres — parents before children, each under a resolved category.
        foreach (var cn in OrderCostCentresParentsFirst(_model.Payload.CostCentres))
        {
            var target = CostCentreTargets[cn.Id];
            if (target.IsDuplicate)
            {
                costCentreId[cn.Id] = t.FindCostCentreByName(target.Name)!.Id;
                reused++;
                continue;
            }
            var parent = cn.ParentId is { } pid ? ResolveCostCentreId(pid, costCentreId, t) : (Guid?)null;
            var domain = new CostCentre(Guid.NewGuid(), cn.Name,
                ResolveCostCategoryId(cn.CategoryId, costCategoryId, t), parent, cn.Alias);
            t.AddCostCentre(domain);
            journal.RecordCostCentre(domain);
            costCentreId[cn.Id] = domain.Id;
            created++;
        }

        // 3) Ledgers. Some ledgers already exist in the (freshly seeded) target BY NAME even though the pre-flight
        //    saw them as new: the 2 predefined seed ledgers (Cash, Profit & Loss A/c) and — created just above by
        //    EnableGst — the 6 GST tax ledgers + Round Off. Re-check by name here and REUSE any such instance
        //    (never duplicate it), overlaying the source's opening balance + group so a predefined/auto ledger the
        //    seed created at zero adopts the exported figure (PR-4 reconciliation). A genuine re-import duplicate
        //    (an ordinary custom ledger already present from an earlier import) is left as-is under Skip.
        foreach (var l in _model.Payload.Ledgers)
        {
            var target = LedgerTargets[l.Id];
            var existingByName = t.FindLedgerByName(l.Name);

            if (target.IsDuplicate || existingByName is not null)
            {
                var existing = existingByName ?? t.FindLedgerByName(target.Name)!;
                var seedProvided = existing.IsPredefined || existing.GstClassification is not null ||
                    existing.TdsTcsClassification is not null ||
                    string.Equals(existing.Name, GstService.RoundOffLedgerName, StringComparison.OrdinalIgnoreCase);

                if (_policy == DuplicatePolicy.MergeOpeningBalance)
                    MergeLedgerOpening(existing, l, journal);
                else if (seedProvided)
                    // A seed/auto ledger (Cash, P&L A/c, GST tax, Round Off) the target created at zero adopts the
                    // exported opening + group so the round-trip reconciles. Idempotent on re-import (same values).
                    OverlayLedger(existing, l, ResolveGroupId(l.GroupId, groupId, t), journal);

                ledgerId[l.Id] = existing.Id;
                reused++;
                continue;
            }

            var domain = new Domain.Ledger(
                Guid.NewGuid(), l.Name, ResolveGroupId(l.GroupId, groupId, t),
                MoneyCodec.FromPaisa(l.OpeningBalancePaisa), l.OpeningIsDebit, l.Alias,
                isPredefined: false, maintainBillByBill: l.MaintainBillByBill,
                defaultCreditPeriodDays: l.DefaultCreditPeriodDays,
                costCentresApplicable: l.CostCentresApplicable,
                enableChequePrinting: l.EnableChequePrinting,
                chequePrintingBankName: l.ChequePrintingBankName,
                interest: BuildInterest(l.Interest),
                currencyId: l.CurrencyId is { } cid ? ResolveCurrencyId(cid, currencyId, t) : null,
                partyGst: BuildPartyGst(l.PartyGst),
                salesPurchaseGst: BuildStockItemGst(l.SalesPurchaseGst),
                gstClassification: BuildGstClassification(l.GstClassification),
                // Phase 6 slice 3: a non-null method MARKS an additional-cost ledger (Freight/Packing/…).
                methodOfAppropriation: l.MethodOfAppropriation is { } m ? ParseEnum<MethodOfAppropriation>(m) : null,
                // Phase 6 slice 5: a party ledger's default Price Level, resolved by name across companies.
                defaultPriceLevelId: l.DefaultPriceLevelId is { } plid ? ResolvePriceLevelId(plid, priceLevelId, t) : null);
            // Phase 7 slice 1: TDS/TCS ledger applicability flags (plain properties). Nature ids resolve via the
            // re-minted maps (fallback: an already-present target nature id). A non-TDS/TCS ledger stays all-off.
            domain.TdsApplicable = l.TdsApplicable;
            domain.TdsNatureOfPaymentId = l.TdsNatureOfPaymentId is { } tn ? ResolveTdsNatureId(tn, tdsNatureId, t) : null;
            domain.DeducteeType = l.DeducteeType is { } dt ? ParseEnum<DeducteeType>(dt) : null;
            domain.PartyPan = l.PartyPan;
            domain.DeductTdsInSameVoucher = l.DeductTdsInSameVoucher;
            domain.TcsApplicable = l.TcsApplicable;
            domain.TcsNatureOfGoodsId = l.TcsNatureOfGoodsId is { } gn ? ResolveTcsNatureId(gn, tcsNatureId, t) : null;
            domain.CollecteeType = l.CollecteeType is { } ct ? ParseEnum<CollecteeType>(ct) : null;
            // A payable-ledger classification only appears on the auto-created TDS/TCS Payable ledgers (reused above);
            // an ordinary imported ledger never carries one, so this stays null here.
            domain.TdsTcsClassification = l.TdsTcsClassification is { } k ? ParseEnum<TdsTcsLedgerKind>(k) : null;
            t.AddLedger(domain);
            journal.RecordLedger(domain);
            ledgerId[l.Id] = domain.Id;
            created++;
        }

        // 4) Units — simple before compound (compound references first/tail unit ids).
        foreach (var u in OrderUnitsSimpleFirst(_model.Payload.Units))
        {
            var target = UnitTargets[u.Id];
            if (target.IsDuplicate)
            {
                unitId[u.Id] = t.FindUnitByName(target.Name)!.Id;
                reused++;
                continue;
            }
            Unit domain = u.IsCompound
                ? inv.CreateCompoundUnit(u.Symbol, u.FormalName,
                    ResolveUnitId(u.FirstUnitId!.Value, unitId, t),
                    ResolveUnitId(u.TailUnitId!.Value, unitId, t),
                    u.ConversionNumerator!.Value, u.ConversionDenominator ?? 1)
                : inv.CreateSimpleUnit(u.Symbol, u.FormalName, u.DecimalPlaces, u.UnitQuantityCode);
            journal.RecordUnit(domain);
            unitId[u.Id] = domain.Id;
            created++;
        }

        // 5) Stock groups — parents before children.
        foreach (var sg in OrderStockGroupsParentsFirst(_model.Payload.StockGroups))
        {
            var target = StockGroupTargets[sg.Id];
            if (target.IsDuplicate)
            {
                stockGroupId[sg.Id] = t.FindStockGroupByName(target.Name)!.Id;
                reused++;
                continue;
            }
            var parent = sg.ParentId is { } pid ? ResolveStockGroupId(pid, stockGroupId, t) : (Guid?)null;
            var domain = inv.CreateStockGroup(sg.Name, parent, sg.Alias, sg.AddQuantities);
            journal.RecordStockGroup(domain);
            stockGroupId[sg.Id] = domain.Id;
            created++;
        }

        // 6) Stock categories — parents before children.
        foreach (var sc in OrderStockCategoriesParentsFirst(_model.Payload.StockCategories))
        {
            var target = StockCategoryTargets[sc.Id];
            if (target.IsDuplicate)
            {
                stockCategoryId[sc.Id] = t.FindStockCategoryByName(target.Name)!.Id;
                reused++;
                continue;
            }
            var parent = sc.ParentId is { } pid ? ResolveStockCategoryId(pid, stockCategoryId, t) : (Guid?)null;
            var domain = inv.CreateStockCategory(sc.Name, parent, sc.Alias);
            journal.RecordStockCategory(domain);
            stockCategoryId[sc.Id] = domain.Id;
            created++;
        }

        // 7) Godowns — parents before children; Main Location is predefined (reuse the seeded instance).
        foreach (var gd in OrderGodownsParentsFirst(_model.Payload.Godowns))
        {
            var target = GodownTargets[gd.Id];
            if (target.IsDuplicate)
            {
                godownId[gd.Id] = t.FindGodownByName(target.Name)!.Id;
                reused++;
                continue;
            }
            var parent = gd.ParentId is { } pid ? ResolveGodownId(pid, godownId, t) : (Guid?)null;
            var domain = inv.CreateGodown(gd.Name, parent, gd.Alias, gd.ThirdParty);
            journal.RecordGodown(domain);
            godownId[gd.Id] = domain.Id;
            created++;
        }

        // 8) Stock items.
        foreach (var i in _model.Payload.StockItems)
        {
            var target = StockItemTargets[i.Id];
            if (target.IsDuplicate)
            {
                var existing = t.FindStockItemByName(target.Name)!;
                if (_policy == DuplicatePolicy.MergeOpeningBalance)
                    MergeStockItemOpening(t, inv, existing, i.Id, godownId, journal);
                stockItemId[i.Id] = existing.Id;
                reused++;
                continue;
            }
            var domain = inv.CreateStockItem(i.Name,
                ResolveStockGroupId(i.StockGroupId, stockGroupId, t),
                ResolveUnitId(i.BaseUnitId, unitId, t),
                i.CategoryId is { } cid ? ResolveStockCategoryId(cid, stockCategoryId, t) : null,
                i.Alias, ParseEnum<StockValuationMethod>(i.ValuationMethod), i.HsnSacCode, i.IsTaxable,
                i.ReorderLevel, i.MinimumOrderQuantity, MoneyCodec.FromPaisaNullable(i.StandardCostPaisa));
            domain.Gst = BuildStockItemGst(i.Gst);
            // v16 batch switches (RQ-2) + v18 Set-Components (RQ-10) — plain model flags, carried verbatim. The
            // Set-Components flag is authoritative from the export; a BOM created below keeps it true (idempotent).
            domain.MaintainInBatches = i.MaintainInBatches;
            domain.TrackManufacturingDate = i.TrackManufacturingDate;
            domain.UseExpiryDates = i.UseExpiryDates;
            domain.SetComponents = i.SetComponents;
            // Phase 7 slice 1: the item's default §206C Nature-of-Goods, resolved via the re-minted map.
            domain.TcsNatureOfGoodsId = i.TcsNatureOfGoodsId is { } gn ? ResolveTcsNatureId(gn, tcsNatureId, t) : null;
            journal.RecordStockItem(domain);
            stockItemId[i.Id] = domain.Id;
            created++;
        }

        // 8b) Batch masters (Phase 6 Cluster 1; RQ-1). Each references a resolved stock item (+ optional godown),
        //     both created above. A batch is created only for a NEWLY-created item — a duplicate item reused under
        //     Skip already owns its batches (re-importing must not duplicate them); the item switches (Maintain/
        //     Track/Use-Expiry) ride along on the item created above and so need no separate reuse handling.
        foreach (var bm in _model.Payload.BatchMasters)
        {
            // Skip batches whose item was a reused duplicate (its batches already exist on the target).
            if (!stockItemId.TryGetValue(bm.StockItemId, out _) || IsReusedStockItem(bm.StockItemId)) continue;
            var batch = new BatchMaster(Guid.NewGuid(),
                ResolveStockItemId(bm.StockItemId, stockItemId, t), bm.BatchNumber,
                CompanyImportService.ParseDateOpt(bm.ManufacturingDate),
                CompanyImportService.ParseDateOpt(bm.ExpiryDate),
                ExpiryPeriod.Parse(bm.ExpiryPeriod),
                bm.GodownId is { } gid ? ResolveGodownId(gid, godownId, t) : null,
                bm.InwardQuantity,
                bm.InwardRatePaisa is { } rp ? MoneyCodec.FromPaisa(rp) : null);
            t.AddBatchMaster(batch);
            journal.RecordBatchMaster(batch);
        }

        // 8c) Bill-of-Materials masters (Phase 6 Cluster 2; RQ-9, DP-3). Each references a resolved finished-good
        //     item, resolved component items, and optional line godowns — all created above. A BOM is created only
        //     for a NEWLY-created finished good — a duplicate item reused under Skip already owns its BOMs (re-
        //     importing must not duplicate them). Routed through BomService so its guards run (item exists, name
        //     unique per item, components exist, ≥ 1 Component line) and the finished good's Set-Components flag is
        //     turned on (RQ-10). The BOM's own Guid is re-minted; FKs resolve by the DTO→target id maps.
        var bomService = new BomService(t);
        foreach (var bomDto in _model.Payload.BillsOfMaterials)
        {
            if (!stockItemId.TryGetValue(bomDto.StockItemId, out _) || IsReusedStockItem(bomDto.StockItemId)) continue;
            var lines = bomDto.Lines.Select(l => new BomLine(
                ParseEnum<BomLineType>(l.LineType),
                ResolveStockItemId(l.ComponentStockItemId, stockItemId, t),
                l.QuantityPerBlock,
                godownId: l.GodownId is { } gid ? ResolveGodownId(gid, godownId, t) : null,
                rate: l.RatePaisa is { } rp ? MoneyCodec.FromPaisa(rp) : null,
                percentOfFinishedGoodCost: l.PercentOfFinishedGoodCost)).ToList();
            var bom = bomService.CreateBom(
                ResolveStockItemId(bomDto.StockItemId, stockItemId, t), bomDto.Name, bomDto.UnitOfManufacture, lines);
            journal.RecordBillOfMaterials(bom);
            bomId[bomDto.Id] = bom.Id;
        }

        // 8d) Price Lists (Phase 6 slice 5; RQ-27) — append-only dated versions scoped to a (level, item) pair, both
        //     resolved above. The slab order is the list's own ascending order (preserved verbatim on export).
        foreach (var plDto in _model.Payload.PriceLists)
        {
            var slabs = plDto.Slabs
                .Select(s => new PriceListSlab(s.FromQty, s.ToQty, MoneyCodec.FromPaisa(s.RatePaisa), s.DiscountPercent))
                .ToList();
            var domain = new PriceList(Guid.NewGuid(),
                ResolvePriceLevelId(plDto.PriceLevelId, priceLevelId, t),
                ResolveStockItemId(plDto.StockItemId, stockItemId, t),
                CompanyImportService.ParseDate(plDto.ApplicableFrom), slabs);
            t.AddPriceList(domain);
            journal.RecordPriceList(domain);
        }

        // 8e) Reorder-Level definitions (Phase 6 slice 6; RQ-32) — per item / group / category; the target resolves
        //     by scope. Quantity-only (no money); the shared period + criteria govern both Advanced figures.
        foreach (var rd in _model.Payload.ReorderDefinitions)
        {
            var scope = ParseEnum<ReorderScope>(rd.Scope);
            var targetId = scope switch
            {
                ReorderScope.Item => ResolveStockItemId(rd.TargetId, stockItemId, t),
                ReorderScope.Group => ResolveStockGroupId(rd.TargetId, stockGroupId, t),
                ReorderScope.Category => ResolveStockCategoryId(rd.TargetId, stockCategoryId, t),
                _ => rd.TargetId,
            };
            var domain = new ReorderDefinition(Guid.NewGuid(), scope, targetId,
                rd.ReorderAdvanced, rd.ReorderQuantity, rd.MinQtyAdvanced, rd.MinOrderQuantity,
                rd.PeriodCount,
                rd.PeriodUnit is { } u ? ParseEnum<ExpiryPeriodUnit>(u) : null,
                rd.Criteria is { } cr ? ParseEnum<ReorderCriteria>(cr) : null);
            t.AddReorderDefinition(domain);
            journal.RecordReorderDefinition(domain);
        }

        // 9) Stock opening balances (only for newly-created items — a duplicate item's opening rode the merge path).
        foreach (var b in _model.Payload.StockOpeningBalances)
        {
            // Skip an opening allocation whose item was a reused duplicate under Skip (its opening already exists);
            // under MergeOpeningBalance the merge path added it.
            if (!stockItemId.TryGetValue(b.StockItemId, out var itemTargetId)) continue;
            if (IsReusedStockItem(b.StockItemId)) continue;

            var balance = inv.AddOpeningBalance(itemTargetId, ResolveGodownId(b.GodownId, godownId, t),
                b.Quantity, MoneyCodec.FromPaisa(b.RatePaisa), b.BatchLabel,
                CompanyImportService.ParseDateOpt(b.ManufacturingDate),
                CompanyImportService.ParseDateOpt(b.ExpiryDate));
            journal.RecordStockOpeningBalance(balance);
        }

        // 9b) Budgets — masters that reference resolved groups/ledgers; the "Primary Cost Category"-style seed does
        //     not apply here, so a budget is always created fresh (no predefined budgets exist).
        foreach (var bd in _model.Payload.Budgets)
        {
            var target = BudgetTargets[bd.Id];
            if (target.IsDuplicate)
            {
                reused++;
                continue;
            }
            var domain = new Budget(Guid.NewGuid(), bd.Name,
                CompanyImportService.ParseDate(bd.PeriodFrom), CompanyImportService.ParseDate(bd.PeriodTo),
                bd.UnderId is { } uid ? ResolveGroupId(uid, groupId, t) : null);
            foreach (var bl in bd.Lines)
            {
                var type = ParseEnum<BudgetType>(bl.BudgetType);
                var amount = MoneyCodec.FromPaisa(bl.AmountPaisa);
                domain.AddLine(bl.GroupId is { } gid
                    ? BudgetLine.ForGroup(ResolveGroupId(gid, groupId, t), type, amount)
                    : BudgetLine.ForLedger(ResolveLedgerId(bl.LedgerId!.Value, ledgerId, t), type, amount));
            }
            t.AddBudget(domain);
            journal.RecordBudget(domain);
            created++;
        }

        // 9c) Scenarios — reference resolved voucher types.
        foreach (var sc in _model.Payload.Scenarios)
        {
            var target = ScenarioTargets[sc.Id];
            if (target.IsDuplicate)
            {
                reused++;
                continue;
            }
            var domain = new Scenario(Guid.NewGuid(), sc.Name, sc.IncludeActuals,
                sc.IncludedTypeIds.Select(id => ResolveVoucherTypeId(id, voucherTypeId, t)),
                sc.ExcludedTypeIds.Select(id => ResolveVoucherTypeId(id, voucherTypeId, t)));
            t.AddScenario(domain);
            journal.RecordScenario(domain);
            created++;
        }

        // 9d) Attach POS retail-till configs (Phase 6 slice 7; DP-4) now that godowns + ledgers exist. Each config
        //     hangs off a newly-created POS Sales voucher type; its refs (default godown/party, tender-ledger map)
        //     resolve by name across companies. A rollback removes the owning voucher type, taking the config with it.
        foreach (var (domainType, pcDto) in posConfigWork)
        {
            var pc = new PosConfig
            {
                DefaultGodownId = pcDto.DefaultGodownId is { } g ? ResolveGodownId(g, godownId, t) : null,
                DefaultPartyId = pcDto.DefaultPartyId is { } p ? ResolveLedgerId(p, ledgerId, t) : null,
                PrintAfterSave = pcDto.PrintAfterSave,
                DefaultTitle = pcDto.DefaultTitle,
                Message1 = pcDto.Message1,
                Message2 = pcDto.Message2,
                Declaration = pcDto.Declaration,
            };
            foreach (var d in pcDto.TenderLedgerDefaults)
                pc.SetTenderLedgerDefault(ParseEnum<PosTenderType>(d.TenderType), ResolveLedgerId(d.LedgerId, ledgerId, t));
            domainType.PosConfig = pc;
        }

        // 10) Vouchers LAST — post through LedgerService so the full validator runs (balance, refs, pairing, stock,
        //     POS tender split).
        var posting = new LedgerService(t);
        var posted = 0;
        var voucherId = new Dictionary<Guid, Guid>();   // Phase 7 slice 3: DTO voucher id → re-minted target id (for challan links)
        foreach (var v in _model.Payload.Vouchers)
        {
            var domain = BuildVoucher(v, ledgerId, voucherTypeId, stockItemId, godownId,
                costCategoryId, costCentreId, currencyId, tdsNatureId, tcsNatureId, t);
            posting.Post(domain);
            journal.RecordVoucher(domain);
            voucherId[v.Id] = domain.Id;
            posted++;
        }

        // 11) Inventory / order vouchers — post through InventoryPostingService (type-vs-content, Stock-Journal
        //     balance, no-negative-stock guards). They carry no accounting entry, so they post after accounting.
        //     Job Work In/Out Order vouchers post FIRST (a Material In/Out links them by id — RQ-48), so a Material
        //     voucher's order links resolve to an already-posted order. The DTO id → new domain id map backs that.
        var invPosting = new InventoryPostingService(t);
        var invVoucherId = new Dictionary<Guid, Guid>();

        void PostInventoryVoucher(InventoryVoucherDto iv)
        {
            var domain = BuildInventoryVoucher(iv, voucherTypeId, ledgerId, stockItemId, godownId, unitId, bomId, invVoucherId, t);
            invPosting.Post(domain);
            journal.RecordInventoryVoucher(domain);
            invVoucherId[iv.Id] = domain.Id;
            posted++;
        }

        foreach (var iv in _model.Payload.InventoryVouchers.Where(x => x.JobWorkOrder is not null))
            PostInventoryVoucher(iv);
        foreach (var iv in _model.Payload.InventoryVouchers.Where(x => x.JobWorkOrder is null))
            PostInventoryVoucher(iv);

        // 12) TDS deposit challans + their Stat-Payment-voucher links (Phase 7 slice 3). A challan is re-minted with a
        //     fresh Guid; its links resolve the DTO voucher id to the re-minted target voucher posted in step 10 (the
        //     Phase-6 carry-forward defect class — a new child silently dropped on export/import). Recorded in the
        //     journal so a rollback prunes them.
        var challanId = new Dictionary<Guid, Guid>();
        foreach (var chDto in _model.Payload.TdsChallans)
        {
            var challan = new TdsChallan(Guid.NewGuid(), chDto.ChallanNo, chDto.BsrCode,
                CompanyImportService.ParseDate(chDto.DepositDate), MoneyCodec.FromPaisa(chDto.AmountPaisa),
                chDto.Section, chDto.MinorHead);
            t.AddTdsChallan(challan);
            journal.RecordTdsChallan(challan);
            challanId[chDto.Id] = challan.Id;
        }
        foreach (var linkDto in _model.Payload.ChallanVoucherLinks)
        {
            if (!challanId.TryGetValue(linkDto.ChallanId, out var chId)) continue; // orphan link — challan not imported
            var vId = voucherId.TryGetValue(linkDto.VoucherId, out var mapped) ? mapped
                : t.FindVoucher(linkDto.VoucherId)?.Id;
            if (vId is null) continue; // orphan link — voucher not imported/present
            t.LinkChallanToVoucher(chId, vId.Value);
            journal.RecordChallanVoucherLink(new ChallanVoucherLink(chId, vId.Value));
        }

        // 13) TCS deposit challans + their Stat-Payment-voucher links (Phase 7 slice 6). The exact sibling of the TDS
        //     challan re-mint above: a fresh Guid per challan; each link resolves the DTO voucher id to the re-minted
        //     target voucher posted in step 10. Recorded in the journal so a rollback prunes them.
        var tcsChallanId = new Dictionary<Guid, Guid>();
        foreach (var chDto in _model.Payload.TcsChallans)
        {
            var challan = new TcsChallan(Guid.NewGuid(), chDto.ChallanNo, chDto.BsrCode,
                CompanyImportService.ParseDate(chDto.DepositDate), MoneyCodec.FromPaisa(chDto.AmountPaisa),
                chDto.CollectionCode, chDto.MinorHead);
            t.AddTcsChallan(challan);
            journal.RecordTcsChallan(challan);
            tcsChallanId[chDto.Id] = challan.Id;
        }
        foreach (var linkDto in _model.Payload.TcsChallanVoucherLinks)
        {
            if (!tcsChallanId.TryGetValue(linkDto.ChallanId, out var chId)) continue; // orphan link — challan not imported
            var vId = voucherId.TryGetValue(linkDto.VoucherId, out var mapped) ? mapped
                : t.FindVoucher(linkDto.VoucherId)?.Id;
            if (vId is null) continue; // orphan link — voucher not imported/present
            t.LinkTcsChallanToVoucher(chId, vId.Value);
            journal.RecordTcsChallanVoucherLink(new ChallanVoucherLink(chId, vId.Value));
        }

        return (created, reused, posted);
    }

    private bool IsReusedStockItem(Guid dtoId) =>
        StockItemTargets.TryGetValue(dtoId, out var m) && m.IsDuplicate;

    // ---- company header + gst config ----

    private void ApplyCompanyHeader(Company t, ApplyJournal journal)
    {
        var c = _model.Company;
        journal.RecordCompanyHeader(t); // snapshot for rollback
        t.Name = c.Name;
        t.MailingName = c.MailingName ?? c.Name;
        t.Address = c.Address;
        if (c.Country is not null) t.Country = c.Country;
        t.State = c.State;
        t.Pin = c.Pin;
        t.FinancialYearStart = CompanyImportService.ParseDate(c.FinancialYearStart);
        t.BooksBeginFrom = CompanyImportService.ParseDate(c.BooksBeginFrom);
        if (c.BaseCurrencySymbol is not null) t.BaseCurrencySymbol = c.BaseCurrencySymbol;
        if (c.BaseCurrencyName is not null) t.BaseCurrencyName = c.BaseCurrencyName;
        t.DecimalPlaces = c.DecimalPlaces;
        if (c.DecimalUnitName is not null) t.DecimalUnitName = c.DecimalUnitName;

        // Phase 6 company feature toggles. The two plain flags are captured by the header snapshot for rollback.
        t.UseSeparateActualBilledQuantity = c.UseSeparateActualBilledQuantity;   // slice 4 (RQ-22)
        t.EnableMultiplePriceLevels = c.EnableMultiplePriceLevels;               // slice 5 (RQ-26)

        // Enable Job Order Processing through the engine (slice 8; RQ-45) so the seeded Material In/Out + Job Work
        // Order voucher types get their IsActive / UseForJobWork / AllowConsumption flags stamped exactly as the app
        // does — the export captures those stamped flags, so a reused seeded type reconciles without an overlay. The
        // prior state is recorded so a rollback fully restores both the company flag and the seeded type flags.
        if (c.EnableJobOrderProcessing != t.EnableJobOrderProcessing)
        {
            journal.RecordJobOrderProcessingBefore(t.EnableJobOrderProcessing);
            new JobWorkService(t).SetEnabled(c.EnableJobOrderProcessing);
        }
    }

    private static GstConfig BuildGstConfig(GstConfigDto g)
    {
        var config = new GstConfig
        {
            Enabled = true,
            Gstin = g.Gstin,
            HomeStateCode = g.HomeStateCode,
            RegistrationType = ParseEnum<GstRegistrationType>(g.RegistrationType),
            ApplicableFrom = CompanyImportService.ParseDateOpt(g.ApplicableFrom),
            Periodicity = ParseEnum<GstReturnPeriodicity>(g.Periodicity),
        };
        // Preserve the exported slabs (EnableGst only seeds defaults when none are present).
        foreach (var s in g.RateSlabs)
            config.AddRateSlab(new GstRateSlab(Guid.NewGuid(), s.RateBasisPoints, s.Label, s.IsPredefined));
        return config;
    }

    /// <summary>Builds a <see cref="TdsConfig"/> from its DTO (Phase 7 slice 1), preserving the exported
    /// Nature-of-Payment masters with FRESH ids and returning the DTO-id → new-id map so a ledger's default-nature
    /// reference resolves across companies.</summary>
    private static (TdsConfig Config, Dictionary<Guid, Guid> NatureIdMap) BuildTdsConfig(TdsConfigDto g)
    {
        var config = new TdsConfig
        {
            Enabled = true, Tan = g.Tan, DeductorType = ParseEnum<DeductorType>(g.DeductorType),
            ResponsiblePersonName = g.ResponsiblePersonName, ResponsiblePersonPan = g.ResponsiblePersonPan,
            ResponsiblePersonDesignation = g.ResponsiblePersonDesignation, ResponsiblePersonAddress = g.ResponsiblePersonAddress,
            SurchargeApplicable = g.SurchargeApplicable, CessApplicable = g.CessApplicable,
            Periodicity = ParseEnum<TdsTcsPeriodicity>(g.Periodicity),
            ApplicableFrom = CompanyImportService.ParseDateOpt(g.ApplicableFrom),
        };
        var map = new Dictionary<Guid, Guid>();
        foreach (var n in g.NaturesOfPayment)
        {
            var nature = new NatureOfPayment(Guid.NewGuid(), n.SectionCode, n.Name, n.RateWithPanBp, n.RateWithoutPanBp,
                n.FvuSectionCode,
                singleTransactionThreshold: n.SingleThresholdPaisa is { } s ? MoneyCodec.FromPaisa(s) : null,
                cumulativeThreshold: n.CumulativeThresholdPaisa is { } c ? MoneyCodec.FromPaisa(c) : null,
                effectiveFrom: CompanyImportService.ParseDateOpt(n.EffectiveFrom), isPredefined: n.IsPredefined);
            config.AddNatureOfPayment(nature);
            map[n.Id] = nature.Id;
        }
        return (config, map);
    }

    /// <summary>Builds a <see cref="TcsConfig"/> from its DTO (Phase 7 slice 1), preserving the exported
    /// Nature-of-Goods masters with FRESH ids and returning the DTO-id → new-id map.</summary>
    private static (TcsConfig Config, Dictionary<Guid, Guid> NatureIdMap) BuildTcsConfig(TcsConfigDto g)
    {
        var config = new TcsConfig
        {
            Enabled = true, Tan = g.Tan, CollectorType = ParseEnum<DeductorType>(g.CollectorType),
            ResponsiblePersonName = g.ResponsiblePersonName, ResponsiblePersonPan = g.ResponsiblePersonPan,
            ResponsiblePersonDesignation = g.ResponsiblePersonDesignation, ResponsiblePersonAddress = g.ResponsiblePersonAddress,
            SurchargeApplicable = g.SurchargeApplicable, CessApplicable = g.CessApplicable,
            Periodicity = ParseEnum<TdsTcsPeriodicity>(g.Periodicity),
            ApplicableFrom = CompanyImportService.ParseDateOpt(g.ApplicableFrom),
        };
        var map = new Dictionary<Guid, Guid>();
        foreach (var n in g.NaturesOfGoods)
        {
            var nature = new NatureOfGoods(Guid.NewGuid(), n.CollectionCode, n.Name, n.RateWithPanBp, n.RateWithoutPanBp,
                n.FvuCode,
                threshold: n.ThresholdPaisa is { } th ? MoneyCodec.FromPaisa(th) : null,
                baseIncludesGst: n.BaseIncludesGst,
                effectiveFrom: CompanyImportService.ParseDateOpt(n.EffectiveFrom), isPredefined: n.IsPredefined,
                isLegacy: n.IsLegacy, legacyCutoff: CompanyImportService.ParseDateOpt(n.LegacyCutoff));
            config.AddNatureOfGoods(nature);
            map[n.Id] = nature.Id;
        }
        return (config, map);
    }

    // ---- voucher assembly ----

    private Voucher BuildVoucher(
        VoucherDto v,
        Dictionary<Guid, Guid> ledgerId,
        Dictionary<Guid, Guid> voucherTypeId,
        Dictionary<Guid, Guid> stockItemId,
        Dictionary<Guid, Guid> godownId,
        Dictionary<Guid, Guid> costCategoryId,
        Dictionary<Guid, Guid> costCentreId,
        Dictionary<Guid, Guid> currencyId,
        Dictionary<Guid, Guid> tdsNatureId,
        Dictionary<Guid, Guid> tcsNatureId,
        Company t)
    {
        var lines = v.Lines.Select(l => new EntryLine(
            ResolveLedgerId(l.LedgerId, ledgerId, t),
            MoneyCodec.FromPaisa(l.AmountPaisa),
            ParseEnum<DrCr>(l.Side),
            billAllocations: l.BillAllocations.Count == 0 ? null : l.BillAllocations.Select(BuildBillAllocation).ToList(),
            costAllocations: l.CostAllocations.Count == 0 ? null
                : l.CostAllocations.Select(a => BuildCostAllocation(a, costCategoryId, costCentreId, t)).ToList(),
            bankAllocation: BuildBankAllocation(l.BankAllocation),
            forex: BuildForex(l.Forex, currencyId, t),
            gst: BuildGstLineTax(l.Gst),
            tds: BuildTdsLineTax(l.Tds, tdsNatureId, ledgerId, t),
            tcs: BuildTcsLineTax(l.Tcs, tcsNatureId, ledgerId, t)));

        var invLines = v.InventoryLines.Count == 0
            ? null
            : v.InventoryLines.Select(il => new VoucherInventoryLine(
                ResolveStockItemId(il.StockItemId, stockItemId, t),
                ResolveGodownId(il.GodownId, godownId, t),
                il.Quantity, MoneyCodec.FromPaisa(il.RatePaisa),
                ParseEnum<StockDirection>(il.Direction), il.BatchLabel,
                // Phase 6 slice 4: Billed defaults to Actual when null (feature off ⇒ byte-identical, ER-13).
                billedQuantity: il.BilledQuantity)).ToList();

        // Phase 6 slice 7: POS payment tenders (empty for every non-POS voucher). The tender ledgers resolve by name.
        var posTenders = v.PosTenders.Count == 0
            ? null
            : v.PosTenders.Select(pt => new PosTender(
                ParseEnum<PosTenderType>(pt.TenderType),
                ResolveLedgerId(pt.LedgerId, ledgerId, t),
                MoneyCodec.FromPaisa(pt.AmountPaisa),
                pt.TenderedPaisa is { } td ? MoneyCodec.FromPaisa(td) : null,
                pt.ChangePaisa is { } ch ? MoneyCodec.FromPaisa(ch) : null,
                pt.CardNo, pt.BankName, pt.ChequeNo)).ToList();

        return new Voucher(
            Guid.NewGuid(), ResolveVoucherTypeId(v.TypeId, voucherTypeId, t),
            CompanyImportService.ParseDate(v.Date), lines, v.Number, v.Narration,
            v.PartyId is { } pid ? ResolveLedgerId(pid, ledgerId, t) : null,
            v.Cancelled, v.Optional, v.PostDated, CompanyImportService.ParseDateOpt(v.ApplicableUpto),
            invLines, posTenders);
    }

    private static BillAllocation BuildBillAllocation(BillAllocationDto a) => new(
        ParseEnum<BillRefType>(a.RefType), a.Name, MoneyCodec.FromPaisa(a.AmountPaisa),
        CompanyImportService.ParseDateOpt(a.DueDate), a.CreditPeriodDays);

    /// <summary>Micros ("× 1,000,000") → exact decimal (mirrors the SQLite forex/rate scale).</summary>
    private const decimal MicroScale = 1_000_000m;
    private static decimal FromMicro(long micro) => micro / MicroScale;

    private static CostAllocation BuildCostAllocation(
        CostAllocationDto a, Dictionary<Guid, Guid> costCategoryId, Dictionary<Guid, Guid> costCentreId, Company t) =>
        new(ResolveCostCategoryId(a.CategoryId, costCategoryId, t),
            ResolveCostCentreId(a.CentreId, costCentreId, t), MoneyCodec.FromPaisa(a.AmountPaisa));

    private static BankAllocation? BuildBankAllocation(BankAllocationDto? b) => b is null ? null : new BankAllocation(
        ParseEnum<BankTransactionType>(b.TransactionType), b.InstrumentNumber,
        CompanyImportService.ParseDateOpt(b.InstrumentDate), CompanyImportService.ParseDateOpt(b.BankDate));

    private static ForexInfo? BuildForex(ForexDto? f, Dictionary<Guid, Guid> currencyId, Company t) => f is null ? null
        : new ForexInfo(ResolveCurrencyId(f.CurrencyId, currencyId, t),
            MoneyCodec.FromPaisa(f.ForexAmountPaisa), FromMicro(f.RateMicro));

    private static GstLineTax? BuildGstLineTax(GstLineTaxDto? g) => g is null ? null : new GstLineTax(
        ParseEnum<GstTaxHead>(g.TaxHead), g.RateBasisPoints, MoneyCodec.FromPaisa(g.TaxableValuePaisa));

    /// <summary>Rebuilds a line's TDS withholding detail (Phase 7 slice 2), re-mapping the source nature id and
    /// deductee-ledger id into the target company's re-minted ids so the withholding reconciles across companies
    /// (paisa- and count-exact). Null for a non-TDS line.</summary>
    private static TdsLineTax? BuildTdsLineTax(
        TdsLineTaxDto? d, Dictionary<Guid, Guid> tdsNatureId, Dictionary<Guid, Guid> ledgerId, Company t) =>
        d is null ? null : new TdsLineTax(
            ResolveTdsNatureId(d.NatureId, tdsNatureId, t), d.SectionCode,
            MoneyCodec.FromPaisa(d.AssessableValuePaisa), d.RateBasisPoints,
            MoneyCodec.FromPaisa(d.TdsAmountPaisa),
            ResolveLedgerId(d.DeducteeLedgerId, ledgerId, t), d.PanApplied);

    /// <summary>Rebuilds a line's TCS collection detail (Phase 7 slice 5; the additive mirror of the TDS carve-out),
    /// re-mapping the source Nature-of-Goods id and collectee-ledger id into the target company's re-minted ids so
    /// the collection reconciles across companies (paisa- and count-exact). Null for a non-TCS line.</summary>
    private static TcsLineTax? BuildTcsLineTax(
        TcsLineTaxDto? d, Dictionary<Guid, Guid> tcsNatureId, Dictionary<Guid, Guid> ledgerId, Company t) =>
        d is null ? null : new TcsLineTax(
            ResolveTcsNatureId(d.NatureId, tcsNatureId, t), d.CollectionCode,
            MoneyCodec.FromPaisa(d.AssessableValuePaisa), d.RateBasisPoints,
            MoneyCodec.FromPaisa(d.TcsAmountPaisa),
            ResolveLedgerId(d.CollecteeLedgerId, ledgerId, t), d.PanApplied);

    private static InterestParameters? BuildInterest(InterestParametersDto? i) => i is null ? null : new InterestParameters(
        i.Enabled, i.RatePercent, ParseEnum<InterestPer>(i.Per), ParseEnum<InterestOnBalance>(i.OnBalance),
        ParseEnum<InterestApplicability>(i.Applicability), CompanyImportService.ParseDateOpt(i.CalculateFrom),
        ParseEnum<InterestStyle>(i.Style), ParseEnum<InterestRoundingMethod>(i.RoundingMethod), i.RoundingDecimals);

    // ---- inventory / order voucher assembly (engine-routed via InventoryPostingService) ----

    private InventoryVoucher BuildInventoryVoucher(
        InventoryVoucherDto v,
        Dictionary<Guid, Guid> voucherTypeId,
        Dictionary<Guid, Guid> ledgerId,
        Dictionary<Guid, Guid> stockItemId,
        Dictionary<Guid, Guid> godownId,
        Dictionary<Guid, Guid> unitId,
        Dictionary<Guid, Guid> bomId,
        Dictionary<Guid, Guid> invVoucherId,
        Company t)
    {
        var typeId = ResolveVoucherTypeId(v.TypeId, voucherTypeId, t);
        var date = CompanyImportService.ParseDate(v.Date);
        var party = v.PartyId is { } pid ? ResolveLedgerId(pid, ledgerId, t) : (Guid?)null;

        InventoryAllocation MapAlloc(InventoryAllocationDto a) => new(
            ResolveStockItemId(a.StockItemId, stockItemId, t), ResolveGodownId(a.GodownId, godownId, t),
            a.Quantity, ParseEnum<StockDirection>(a.Direction),
            a.RatePaisa is { } rp ? MoneyCodec.FromPaisa(rp) : null, a.BatchLabel,
            a.UnitId is { } uid ? ResolveUnitId(uid, unitId, t) : null);

        // Reconstruct EVERY part verbatim (Phase 6: additional-cost lines, Job Work order payload, order links) and
        // rehydrate through FromStorage, then post through the engine (InventoryPostingService validates the
        // content-vs-base-type, Stock-Journal / Material balance, and the no-negative-stock guard). This preserves
        // the Stock-Journal-vs-Material distinction (both carry source + destination) without a lossy shape guess.
        var source = v.Allocations.Select(MapAlloc).ToList();
        var destination = v.DestinationAllocations.Select(MapAlloc).ToList();
        var orderLines = v.OrderLines.Select(l => new OrderLine(
            ResolveStockItemId(l.StockItemId, stockItemId, t), ResolveGodownId(l.GodownId, godownId, t),
            l.Quantity, l.RatePaisa is { } rp ? MoneyCodec.FromPaisa(rp) : null)).ToList();
        var physicalLines = v.PhysicalLines.Select(l => new PhysicalStockLine(
            ResolveStockItemId(l.StockItemId, stockItemId, t), ResolveGodownId(l.GodownId, godownId, t),
            l.CountedQuantity, l.BatchLabel)).ToList();

        // Additional-cost lines (Phase 6 slice 3; RQ-20) on a Stock-Journal transfer — resolve the cost ledger.
        var additionalCostLines = v.AdditionalCostLines.Select(a => new AdditionalCostLine(
            ResolveLedgerId(a.LedgerId, ledgerId, t), MoneyCodec.FromPaisa(a.AmountPaisa))).ToList();

        // Job Work In/Out Order payload (Phase 6 slice 8; RQ-47) — finished good + tracked component lines.
        var jobWorkOrder = v.JobWorkOrder is { } j ? BuildJobWorkOrder(j, stockItemId, godownId, bomId, t) : null;

        // Material In/Out order links (Phase 6 slice 8; RQ-48) — each is a source Job Work Order voucher's id,
        // resolved to its already-posted target voucher (order vouchers are posted first).
        var orderLinks = v.OrderLinks.Select(id => invVoucherId.TryGetValue(id, out var did) ? did
            : t.FindInventoryVoucher(id)?.Id
            ?? throw new InvalidOperationException($"Material voucher references unknown Job Work order {id}.")).ToList();

        return InventoryVoucher.FromStorage(Guid.NewGuid(), typeId, date,
            source, destination, orderLines, physicalLines,
            v.Number, v.Narration, party, v.Cancelled, v.PostDated,
            additionalCostLines, jobWorkOrder, orderLinks);
    }

    private JobWorkOrder BuildJobWorkOrder(
        JobWorkOrderDto j,
        Dictionary<Guid, Guid> stockItemId,
        Dictionary<Guid, Guid> godownId,
        Dictionary<Guid, Guid> bomId,
        Company t)
    {
        var lines = j.Lines.Select(l => new JobWorkOrderLine(
            ResolveStockItemId(l.ComponentStockItemId, stockItemId, t),
            ParseEnum<JobWorkComponentTrack>(l.Track),
            l.Quantity,
            l.GodownId is { } g ? ResolveGodownId(g, godownId, t) : null,
            CompanyImportService.ParseDateOpt(l.DueDate),
            l.RatePaisa is { } r ? MoneyCodec.FromPaisa(r) : null)).ToList();

        return new JobWorkOrder(
            ParseEnum<JobWorkDirection>(j.Direction), j.OrderNo,
            ResolveStockItemId(j.FinishedGoodStockItemId, stockItemId, t), j.FinishedGoodQuantity, lines,
            finishedGoodRate: j.FinishedGoodRatePaisa is { } fr ? MoneyCodec.FromPaisa(fr) : null,
            finishedGoodDueDate: CompanyImportService.ParseDateOpt(j.FinishedGoodDueDate),
            finishedGoodGodownId: j.FinishedGoodGodownId is { } fgg ? ResolveGodownId(fgg, godownId, t) : null,
            trackingComponents: j.TrackingComponents,
            // Provenance BOM link (Slice-2), resolved to the re-minted target BOM id.
            fillComponentsBomId: j.FillComponentsBomId is { } b
                ? (bomId.TryGetValue(b, out var bid) ? bid : t.FindBillOfMaterials(b)?.Id
                    ?? throw new InvalidOperationException($"Job Work order references unknown Bill of Materials {b}."))
                : null,
            durationOfProcess: j.DurationOfProcess, natureOfProcessing: j.NatureOfProcessing);
    }

    private static PartyGstDetails? BuildPartyGst(PartyGstDto? p) => p is null ? null : new PartyGstDetails
    {
        RegistrationType = ParseEnum<GstRegistrationType>(p.RegistrationType),
        Gstin = p.Gstin,
        StateCode = p.StateCode,
    };

    private static StockItemGstDetails? BuildStockItemGst(StockItemGstDto? s) => s is null ? null : new StockItemGstDetails
    {
        HsnSac = s.HsnSac,
        Taxability = ParseEnum<GstTaxability>(s.Taxability),
        RateBasisPoints = s.RateBasisPoints,
        SupplyType = ParseEnum<GstSupplyType>(s.SupplyType),
    };

    private static LedgerGstClassification? BuildGstClassification(LedgerGstClassificationDto? c) => c is null ? null
        : new LedgerGstClassification(ParseEnum<GstTaxHead>(c.TaxHead), ParseEnum<GstTaxDirection>(c.Direction));

    // ---- duplicate merges ----

    /// <summary>Overlays the exported opening balance + group onto a reused seed/auto ledger (Cash, P&amp;L A/c,
    /// GST tax ledgers, Round Off) so a fresh-company round-trip reconciles. Snapshots first for rollback.</summary>
    private static void OverlayLedger(Domain.Ledger existing, LedgerDto incoming, Guid groupId, ApplyJournal journal)
    {
        journal.RecordLedgerOpeningSnapshot(existing, captureGroup: true);
        existing.OpeningBalance = MoneyCodec.FromPaisa(incoming.OpeningBalancePaisa);
        existing.OpeningIsDebit = incoming.OpeningIsDebit;
        existing.GroupId = groupId;
    }

    private static void MergeLedgerOpening(Domain.Ledger existing, LedgerDto incoming, ApplyJournal journal)
    {
        journal.RecordLedgerOpeningSnapshot(existing, captureGroup: false);
        // Add signed openings, then normalise back to magnitude + side.
        var signed = existing.SignedOpening + (incoming.OpeningIsDebit
            ? MoneyCodec.FromPaisa(incoming.OpeningBalancePaisa).Amount
            : -MoneyCodec.FromPaisa(incoming.OpeningBalancePaisa).Amount);
        existing.OpeningIsDebit = signed >= 0m;
        existing.OpeningBalance = new Money(Math.Abs(signed));
    }

    private void MergeStockItemOpening(
        Company t, InventoryService inv, StockItem existing, Guid dtoItemId,
        Dictionary<Guid, Guid> godownId, ApplyJournal journal)
    {
        foreach (var b in _model.Payload.StockOpeningBalances.Where(x => x.StockItemId == dtoItemId))
        {
            var balance = inv.AddOpeningBalance(existing.Id, ResolveGodownId(b.GodownId, godownId, t),
                b.Quantity, MoneyCodec.FromPaisa(b.RatePaisa), b.BatchLabel,
                CompanyImportService.ParseDateOpt(b.ManufacturingDate), CompanyImportService.ParseDateOpt(b.ExpiryDate));
            journal.RecordStockOpeningBalance(balance);
        }
    }

    // ---- id resolution (DTO id → target domain id; a mapped import id or an already-present target id) ----

    private static Guid ResolveGroupId(Guid dtoId, Dictionary<Guid, Guid> map, Company t) =>
        map.TryGetValue(dtoId, out var id) ? id : t.FindGroup(dtoId)?.Id
            ?? throw new InvalidOperationException($"Group reference {dtoId} could not be resolved.");
    private static Guid ResolveLedgerId(Guid dtoId, Dictionary<Guid, Guid> map, Company t) =>
        map.TryGetValue(dtoId, out var id) ? id : t.FindLedger(dtoId)?.Id
            ?? throw new InvalidOperationException($"Ledger reference {dtoId} could not be resolved.");
    private static Guid ResolveVoucherTypeId(Guid dtoId, Dictionary<Guid, Guid> map, Company t) =>
        map.TryGetValue(dtoId, out var id) ? id : t.FindVoucherType(dtoId)?.Id
            ?? throw new InvalidOperationException($"Voucher type reference {dtoId} could not be resolved.");
    private static Guid ResolveUnitId(Guid dtoId, Dictionary<Guid, Guid> map, Company t) =>
        map.TryGetValue(dtoId, out var id) ? id : t.FindUnit(dtoId)?.Id
            ?? throw new InvalidOperationException($"Unit reference {dtoId} could not be resolved.");
    private static Guid ResolveStockGroupId(Guid dtoId, Dictionary<Guid, Guid> map, Company t) =>
        map.TryGetValue(dtoId, out var id) ? id : t.FindStockGroup(dtoId)?.Id
            ?? throw new InvalidOperationException($"Stock group reference {dtoId} could not be resolved.");
    private static Guid ResolveStockCategoryId(Guid dtoId, Dictionary<Guid, Guid> map, Company t) =>
        map.TryGetValue(dtoId, out var id) ? id : t.FindStockCategory(dtoId)?.Id
            ?? throw new InvalidOperationException($"Stock category reference {dtoId} could not be resolved.");
    private static Guid ResolveGodownId(Guid dtoId, Dictionary<Guid, Guid> map, Company t) =>
        map.TryGetValue(dtoId, out var id) ? id : t.FindGodown(dtoId)?.Id
            ?? throw new InvalidOperationException($"Godown reference {dtoId} could not be resolved.");
    private static Guid ResolveStockItemId(Guid dtoId, Dictionary<Guid, Guid> map, Company t) =>
        map.TryGetValue(dtoId, out var id) ? id : t.FindStockItem(dtoId)?.Id
            ?? throw new InvalidOperationException($"Stock item reference {dtoId} could not be resolved.");
    private static Guid ResolveCostCategoryId(Guid dtoId, Dictionary<Guid, Guid> map, Company t) =>
        map.TryGetValue(dtoId, out var id) ? id : t.FindCostCategory(dtoId)?.Id
            ?? throw new InvalidOperationException($"Cost category reference {dtoId} could not be resolved.");
    private static Guid ResolveCostCentreId(Guid dtoId, Dictionary<Guid, Guid> map, Company t) =>
        map.TryGetValue(dtoId, out var id) ? id : t.FindCostCentre(dtoId)?.Id
            ?? throw new InvalidOperationException($"Cost centre reference {dtoId} could not be resolved.");
    private static Guid ResolveCurrencyId(Guid dtoId, Dictionary<Guid, Guid> map, Company t) =>
        map.TryGetValue(dtoId, out var id) ? id : t.FindCurrency(dtoId)?.Id
            ?? throw new InvalidOperationException($"Currency reference {dtoId} could not be resolved.");
    private static Guid ResolvePriceLevelId(Guid dtoId, Dictionary<Guid, Guid> map, Company t) =>
        map.TryGetValue(dtoId, out var id) ? id : t.FindPriceLevel(dtoId)?.Id
            ?? throw new InvalidOperationException($"Price level reference {dtoId} could not be resolved.");
    private static Guid ResolveTdsNatureId(Guid dtoId, Dictionary<Guid, Guid> map, Company t) =>
        map.TryGetValue(dtoId, out var id) ? id : t.FindNatureOfPayment(dtoId)?.Id
            ?? throw new InvalidOperationException($"Nature-of-Payment reference {dtoId} could not be resolved.");
    private static Guid ResolveTcsNatureId(Guid dtoId, Dictionary<Guid, Guid> map, Company t) =>
        map.TryGetValue(dtoId, out var id) ? id : t.FindNatureOfGoods(dtoId)?.Id
            ?? throw new InvalidOperationException($"Nature-of-Goods reference {dtoId} could not be resolved.");

    // ---- dependency ordering (parents before children; simple units before compound) ----

    private IEnumerable<GroupDto> OrderGroupsParentsFirst(IReadOnlyList<GroupDto> groups)
        => TopoOrder(groups, g => g.Id, g => g.ParentId);
    private IEnumerable<StockGroupDto> OrderStockGroupsParentsFirst(IReadOnlyList<StockGroupDto> groups)
        => TopoOrder(groups, g => g.Id, g => g.ParentId);
    private IEnumerable<StockCategoryDto> OrderStockCategoriesParentsFirst(IReadOnlyList<StockCategoryDto> cats)
        => TopoOrder(cats, c => c.Id, c => c.ParentId);
    private IEnumerable<GodownDto> OrderGodownsParentsFirst(IReadOnlyList<GodownDto> godowns)
        => TopoOrder(godowns, g => g.Id, g => g.ParentId);
    private IEnumerable<CostCentreDto> OrderCostCentresParentsFirst(IReadOnlyList<CostCentreDto> centres)
        => TopoOrder(centres, c => c.Id, c => c.ParentId);

    private static IEnumerable<UnitDto> OrderUnitsSimpleFirst(IReadOnlyList<UnitDto> units)
        => units.Where(u => !u.IsCompound).Concat(units.Where(u => u.IsCompound));

    /// <summary>Stable topological sort: emit an item only after its parent (when the parent is in the set).
    /// A parent that lives in the target (not in the import) imposes no ordering constraint.</summary>
    private static IEnumerable<T> TopoOrder<T>(IReadOnlyList<T> items, Func<T, Guid> idOf, Func<T, Guid?> parentOf)
    {
        var byId = items.ToDictionary(idOf);
        var emitted = new HashSet<Guid>();
        var result = new List<T>(items.Count);

        void Emit(T item)
        {
            var id = idOf(item);
            if (!emitted.Add(id)) return;
            if (parentOf(item) is { } pid && byId.TryGetValue(pid, out var parent) && !emitted.Contains(pid))
                Emit(parent);
            result.Add(item);
        }

        foreach (var item in items) Emit(item);
        return result;
    }

    private static TEnum ParseEnum<TEnum>(string name) where TEnum : struct, Enum => Enum.Parse<TEnum>(name);
}
