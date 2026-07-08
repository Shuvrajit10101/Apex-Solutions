using System.Globalization;
using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Services;

namespace Apex.Ledger.Io;

/// <summary>
/// How a master in the import that <b>already exists</b> in the target company (matched by name, the domain's own
/// lookup key) is handled (RQ-24).
/// </summary>
public enum DuplicatePolicy
{
    /// <summary>Ignore the incoming master; keep the existing one untouched, and route every reference to it.</summary>
    Skip,

    /// <summary>Keep the existing master but <b>add</b> the incoming opening balance to it (ledgers: signed opening
    /// += incoming; stock items: append the incoming opening allocations). Non-monetary attributes are left as-is.</summary>
    MergeOpeningBalance,

    /// <summary>Any duplicate master ⇒ reject the whole batch (Applied=false), applying nothing.</summary>
    RejectBatch,
}

/// <summary>The outcome of a <see cref="CompanyImportService.Apply"/> call.</summary>
public sealed record ImportResult
{
    /// <summary><c>true</c> iff the batch was applied in full (RQ-23: all-or-nothing). When <c>false</c> the target
    /// company is byte-for-byte unchanged and <see cref="Errors"/> explains why.</summary>
    public required bool Applied { get; init; }

    /// <summary>Per-record validation / apply messages. Empty on success.</summary>
    public required IReadOnlyList<string> Errors { get; init; }

    /// <summary>Masters newly created on the target (across all master kinds).</summary>
    public int MastersCreated { get; init; }

    /// <summary>Existing masters skipped / merged per the duplicate policy.</summary>
    public int MastersReused { get; init; }

    /// <summary>Vouchers posted through the engine.</summary>
    public int VouchersPosted { get; init; }

    internal static ImportResult Fail(IReadOnlyList<string> errors) =>
        new() { Applied = false, Errors = errors };
}

/// <summary>
/// Applies a parsed <see cref="CanonicalModel"/> into a live <see cref="Company"/> <b>through the domain engine</b>
/// (ER-6): masters are created via the master-create path and every voucher is posted through
/// <see cref="LedgerService.Post"/> (so <see cref="VoucherValidator"/> runs — balance, referential integrity,
/// GST/inventory pairing, no-negative-stock). It never touches a store directly.
/// <para>
/// <b>Validate-before-apply (RQ-21) + transactional (RQ-23).</b> A full pre-flight pass resolves every master
/// reference and checks that each voucher balances (Σ Dr = Σ Cr) and references only masters that exist or are
/// being imported; if <b>any</b> record is invalid, nothing is applied and <see cref="ImportResult.Applied"/> is
/// <c>false</c> with a per-record message. During apply, everything created/posted is tracked and rolled back on
/// any engine exception (e.g. a negative-stock rejection), so a half-import can never persist.
/// </para>
/// <para>
/// <b>Name-based identity.</b> References in the DTO (which use the source company's Guids) are translated to the
/// target's own ids by <b>name</b> — the same key the domain's <c>FindXByName</c> lookups use — so importing into
/// a fresh (differently-Guid'd) company reconciles to the paisa (PR-4). Predefined masters seeded by
/// <see cref="CompanyFactory.CreateSeeded"/> (the 28 groups, Cash, Profit &amp; Loss A/c, the 24 voucher types,
/// Main Location) and the GST tax ledgers auto-created by <see cref="GstService.EnableGst"/> are reused, never
/// duplicated.
/// </para>
/// </summary>
public sealed class CompanyImportService
{
    private readonly Company _target;

    public CompanyImportService(Company target)
        => _target = target ?? throw new ArgumentNullException(nameof(target));

    /// <summary>
    /// Applies <paramref name="model"/> into the target company. See the type doc for the contract.
    /// </summary>
    public ImportResult Apply(CanonicalModel model, DuplicatePolicy policy = DuplicatePolicy.Skip)
    {
        ArgumentNullException.ThrowIfNull(model);

        var errors = new List<string>();

        // 0) Envelope sanity (Parse already validated the structural shape; this is a defensive re-check so the
        //    service is safe even when handed a model built in-memory rather than via Parse).
        CanonicalValidation.Validate(model, errors);
        if (errors.Count > 0) return ImportResult.Fail(errors);

        // 1) PRE-FLIGHT (no mutation): resolve every master reference + check every voucher balances & refs.
        //    Build the DTO-id → target-id maps so the reference checks and the later apply share one translation.
        var plan = PlanImport(model, policy, errors);
        if (errors.Count > 0) return ImportResult.Fail(errors);

        // 2) APPLY (mutating). Track everything for a full rollback on any engine exception (RQ-23).
        var journal = new ApplyJournal(_target);
        try
        {
            var (created, reused, posted) = plan!.Execute(_target, journal);
            return new ImportResult
            {
                Applied = true,
                Errors = Array.Empty<string>(),
                MastersCreated = created,
                MastersReused = reused,
                VouchersPosted = posted,
            };
        }
        catch (Exception ex)
        {
            journal.Rollback();
            return ImportResult.Fail(new[]
            {
                $"Import aborted and rolled back (nothing applied): {ex.Message}",
            });
        }
    }

    // ============================================================ pre-flight planning

    /// <summary>
    /// Resolves the whole import against the target <b>without mutating it</b>: it decides, per master, whether it
    /// is new or a duplicate (by name) and how the duplicate policy treats it; it maps each DTO id to the target id
    /// it will resolve to; and it validates every voucher (balance + resolvable ledger/type/party/item/godown refs).
    /// Errors are appended to <paramref name="errors"/>; returns the executable plan (only meaningful when no errors).
    /// </summary>
    private ImportPlan? PlanImport(CanonicalModel model, DuplicatePolicy policy, List<string> errors)
    {
        var plan = new ImportPlan(model, policy);

        // ---- Groups (parents before children handled at execute; here we just map names) ----
        //      FindGroupOrHeadByName also matches the reserved Profit & Loss head (stored outside the 28), so the
        //      seeded predefined P&L head is recognised as an existing master and REUSED, never re-created (PR-4).
        MapMasters(model.Payload.Groups, g => g.Name, name => _target.FindGroupOrHeadByName(name) is not null,
            plan.GroupTargets, policy, errors, "group");

        // ---- Ledgers ----
        MapMasters(model.Payload.Ledgers, l => l.Name, name => _target.FindLedgerByName(name) is not null,
            plan.LedgerTargets, policy, errors, "ledger");

        // ---- Voucher types ----
        MapMasters(model.Payload.VoucherTypes, t => t.Name, name => _target.FindVoucherTypeByName(name) is not null,
            plan.VoucherTypeTargets, policy, errors, "voucher type");

        // ---- Cost categories / centres ----
        MapMasters(model.Payload.CostCategories, x => x.Name, name => _target.FindCostCategoryByName(name) is not null,
            plan.CostCategoryTargets, policy, errors, "cost category");
        MapMasters(model.Payload.CostCentres, x => x.Name, name => _target.FindCostCentreByName(name) is not null,
            plan.CostCentreTargets, policy, errors, "cost centre");

        // ---- Currencies (matched by formal name — the domain's own currency lookup key) ----
        MapMasters(model.Payload.Currencies, x => x.FormalName, name => _target.FindCurrencyByName(name) is not null,
            plan.CurrencyTargets, policy, errors, "currency");

        // ---- Budgets / scenarios ----
        MapMasters(model.Payload.Budgets, x => x.Name, name => _target.FindBudgetByName(name) is not null,
            plan.BudgetTargets, policy, errors, "budget");
        MapMasters(model.Payload.Scenarios, x => x.Name, name => _target.FindScenarioByName(name) is not null,
            plan.ScenarioTargets, policy, errors, "scenario");

        // ---- Units ----
        MapMasters(model.Payload.Units, u => u.Symbol, name => _target.FindUnitByName(name) is not null,
            plan.UnitTargets, policy, errors, "unit");

        // ---- Stock groups ----
        MapMasters(model.Payload.StockGroups, g => g.Name, name => _target.FindStockGroupByName(name) is not null,
            plan.StockGroupTargets, policy, errors, "stock group");

        // ---- Stock categories ----
        MapMasters(model.Payload.StockCategories, g => g.Name, name => _target.FindStockCategoryByName(name) is not null,
            plan.StockCategoryTargets, policy, errors, "stock category");

        // ---- Godowns ----
        MapMasters(model.Payload.Godowns, g => g.Name, name => _target.FindGodownByName(name) is not null,
            plan.GodownTargets, policy, errors, "godown");

        // ---- Stock items ----
        MapMasters(model.Payload.StockItems, i => i.Name, name => _target.FindStockItemByName(name) is not null,
            plan.StockItemTargets, policy, errors, "stock item");

        if (errors.Count > 0) return plan; // don't attempt reference/voucher checks on an already-broken batch

        // ---- Cross-reference resolvability (every FK must resolve to a mapped or existing master) ----
        ValidateReferences(model, plan, errors);
        if (errors.Count > 0) return plan;

        // ---- Voucher validation: balance + resolvable refs (RQ-21), without posting ----
        foreach (var v in model.Payload.Vouchers)
            ValidateVoucher(v, plan, errors);

        return plan;
    }

    /// <summary>
    /// For a master kind: for each DTO, record whether it maps to an existing target (duplicate) or will be created.
    /// A duplicate under <see cref="DuplicatePolicy.RejectBatch"/> is an error; under Skip/MergeOpeningBalance it is
    /// recorded as "reuse existing". A duplicate <b>within the batch itself</b> (two DTOs with the same name) is
    /// always an error.
    /// </summary>
    private static void MapMasters<TDto>(
        IReadOnlyList<TDto> dtos,
        Func<TDto, string> nameOf,
        Func<string, bool> existsInTarget,
        Dictionary<Guid, MasterTarget> targets,
        DuplicatePolicy policy,
        List<string> errors,
        string what)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var dto in dtos)
        {
            var name = nameOf(dto);
            var id = IdOf(dto);
            if (!seen.Add(name))
            {
                errors.Add($"Duplicate {what} '{name}' appears more than once in the import.");
                continue;
            }

            if (existsInTarget(name))
            {
                if (policy == DuplicatePolicy.RejectBatch)
                {
                    errors.Add($"Duplicate {what} '{name}' already exists in the target company (reject-batch policy).");
                    continue;
                }
                targets[id] = new MasterTarget(name, IsDuplicate: true);
            }
            else
            {
                targets[id] = new MasterTarget(name, IsDuplicate: false);
            }
        }
    }

    private static Guid IdOf<TDto>(TDto dto) => dto switch
    {
        GroupDto g => g.Id,
        LedgerDto l => l.Id,
        VoucherTypeDto t => t.Id,
        UnitDto u => u.Id,
        StockGroupDto sg => sg.Id,
        StockCategoryDto sc => sc.Id,
        GodownDto gd => gd.Id,
        StockItemDto si => si.Id,
        CostCategoryDto cc => cc.Id,
        CostCentreDto cn => cn.Id,
        CurrencyDto cur => cur.Id,
        BudgetDto bd => bd.Id,
        ScenarioDto scn => scn.Id,
        _ => throw new InvalidOperationException($"Unsupported master DTO {typeof(TDto).Name}."),
    };

    // ============================================================ reference + voucher validation

    private void ValidateReferences(CanonicalModel model, ImportPlan plan, List<string> errors)
    {
        // Ledger → group.
        foreach (var l in model.Payload.Ledgers)
            if (!plan.CanResolveGroup(l.GroupId, _target))
                errors.Add($"Ledger '{l.Name}' references a group that is neither imported nor present in the target.");

        // Group → parent.
        foreach (var g in model.Payload.Groups)
            if (g.ParentId is { } pid && !plan.CanResolveGroup(pid, _target))
                errors.Add($"Group '{g.Name}' references a parent group that is neither imported nor present.");

        // Stock item → stock group + base unit + optional category.
        foreach (var i in model.Payload.StockItems)
        {
            if (!plan.CanResolveStockGroup(i.StockGroupId, _target))
                errors.Add($"Stock item '{i.Name}' references a stock group that is neither imported nor present.");
            if (!plan.CanResolveUnit(i.BaseUnitId, _target))
                errors.Add($"Stock item '{i.Name}' references a base unit that is neither imported nor present.");
            if (i.CategoryId is { } cid && !plan.CanResolveStockCategory(cid, _target))
                errors.Add($"Stock item '{i.Name}' references a stock category that is neither imported nor present.");
        }

        // Opening stock → item + godown.
        foreach (var b in model.Payload.StockOpeningBalances)
        {
            if (!plan.CanResolveStockItem(b.StockItemId, _target))
                errors.Add("An opening-stock allocation references a stock item that is neither imported nor present.");
            if (!plan.CanResolveGodown(b.GodownId, _target))
                errors.Add("An opening-stock allocation references a godown that is neither imported nor present.");
        }

        // Batch master → item + optional inward-layer godown.
        foreach (var bm in model.Payload.BatchMasters)
        {
            if (!plan.CanResolveStockItem(bm.StockItemId, _target))
                errors.Add($"Batch '{bm.BatchNumber}' references a stock item that is neither imported nor present.");
            if (bm.GodownId is { } gid && !plan.CanResolveGodown(gid, _target))
                errors.Add($"Batch '{bm.BatchNumber}' references a godown that is neither imported nor present.");
        }

        // Bill of Materials → finished-good item, each line's component item + optional line godown.
        foreach (var bom in model.Payload.BillsOfMaterials)
        {
            if (!plan.CanResolveStockItem(bom.StockItemId, _target))
                errors.Add($"BOM '{bom.Name}' references a finished-good stock item that is neither imported nor present.");
            foreach (var line in bom.Lines)
            {
                if (!plan.CanResolveStockItem(line.ComponentStockItemId, _target))
                    errors.Add($"BOM '{bom.Name}' has a line referencing a stock item that is neither imported nor present.");
                if (line.GodownId is { } lgid && !plan.CanResolveGodown(lgid, _target))
                    errors.Add($"BOM '{bom.Name}' has a line referencing a godown that is neither imported nor present.");
            }
        }

        // Ledger → currency.
        foreach (var l in model.Payload.Ledgers)
            if (l.CurrencyId is { } cur && !plan.CanResolveCurrency(cur, _target))
                errors.Add($"Ledger '{l.Name}' references a currency that is neither imported nor present.");

        // Cost centre → category + parent.
        foreach (var cn in model.Payload.CostCentres)
        {
            if (!plan.CanResolveCostCategory(cn.CategoryId, _target))
                errors.Add($"Cost centre '{cn.Name}' references a cost category that is neither imported nor present.");
            if (cn.ParentId is { } pid && !plan.CanResolveCostCentre(pid, _target))
                errors.Add($"Cost centre '{cn.Name}' references a parent centre that is neither imported nor present.");
        }

        // Exchange rate → currency.
        foreach (var r in model.Payload.ExchangeRates)
            if (!plan.CanResolveCurrency(r.CurrencyId, _target))
                errors.Add("An exchange rate references a currency that is neither imported nor present.");

        // Budget → under-group + each line's group/ledger.
        foreach (var bd in model.Payload.Budgets)
        {
            if (bd.UnderId is { } uid && !plan.CanResolveGroup(uid, _target))
                errors.Add($"Budget '{bd.Name}' references an under-group that is neither imported nor present.");
            foreach (var bl in bd.Lines)
            {
                if (bl.GroupId is { } gid && !plan.CanResolveGroup(gid, _target))
                    errors.Add($"Budget '{bd.Name}' has a line referencing a group that is neither imported nor present.");
                if (bl.LedgerId is { } lid && !plan.CanResolveLedger(lid, _target))
                    errors.Add($"Budget '{bd.Name}' has a line referencing a ledger that is neither imported nor present.");
            }
        }

        // Scenario → included / excluded voucher types.
        foreach (var sc in model.Payload.Scenarios)
        {
            foreach (var id in sc.IncludedTypeIds.Concat(sc.ExcludedTypeIds))
                if (!plan.CanResolveVoucherType(id, _target))
                    errors.Add($"Scenario '{sc.Name}' references a voucher type that is neither imported nor present.");
        }

        // Entry-line cost/forex refs across every accounting voucher.
        foreach (var v in model.Payload.Vouchers)
            foreach (var line in v.Lines)
            {
                foreach (var a in line.CostAllocations)
                {
                    if (!plan.CanResolveCostCategory(a.CategoryId, _target))
                        errors.Add("A cost allocation references a cost category that is neither imported nor present.");
                    if (!plan.CanResolveCostCentre(a.CentreId, _target))
                        errors.Add("A cost allocation references a cost centre that is neither imported nor present.");
                }
                if (line.Forex is { } fx && !plan.CanResolveCurrency(fx.CurrencyId, _target))
                    errors.Add("A forex line references a currency that is neither imported nor present.");
            }

        // Inventory / order vouchers → type + party + items + godowns (+ line units).
        foreach (var iv in model.Payload.InventoryVouchers)
        {
            if (!plan.CanResolveVoucherType(iv.TypeId, _target))
                errors.Add("An inventory voucher references a voucher type that is neither imported nor present.");
            if (iv.PartyId is { } pid && !plan.CanResolveLedger(pid, _target))
                errors.Add("An inventory voucher references a party ledger that is neither imported nor present.");
            foreach (var a in iv.Allocations.Concat(iv.DestinationAllocations))
            {
                if (!plan.CanResolveStockItem(a.StockItemId, _target))
                    errors.Add("An inventory-voucher line references a stock item that is neither imported nor present.");
                if (!plan.CanResolveGodown(a.GodownId, _target))
                    errors.Add("An inventory-voucher line references a godown that is neither imported nor present.");
                if (a.UnitId is { } uid && !plan.CanResolveUnit(uid, _target))
                    errors.Add("An inventory-voucher line references a unit that is neither imported nor present.");
            }
            foreach (var l in iv.OrderLines)
            {
                if (!plan.CanResolveStockItem(l.StockItemId, _target))
                    errors.Add("An order line references a stock item that is neither imported nor present.");
                if (!plan.CanResolveGodown(l.GodownId, _target))
                    errors.Add("An order line references a godown that is neither imported nor present.");
            }
            foreach (var l in iv.PhysicalLines)
            {
                if (!plan.CanResolveStockItem(l.StockItemId, _target))
                    errors.Add("A physical-stock line references a stock item that is neither imported nor present.");
                if (!plan.CanResolveGodown(l.GodownId, _target))
                    errors.Add("A physical-stock line references a godown that is neither imported nor present.");
            }
        }
    }

    private void ValidateVoucher(VoucherDto v, ImportPlan plan, List<string> errors)
    {
        var label = $"Voucher #{v.Number} dated {v.Date}";

        if (!plan.CanResolveVoucherType(v.TypeId, _target))
            errors.Add($"{label} references a voucher type that is neither imported nor present.");

        if (v.Lines.Count < 2)
            errors.Add($"{label} has fewer than two entry lines.");

        long dr = 0, cr = 0;
        foreach (var line in v.Lines)
        {
            if (line.AmountPaisa <= 0)
                errors.Add($"{label} has a non-positive line amount.");
            if (!plan.CanResolveLedger(line.LedgerId, _target))
                errors.Add($"{label} references a ledger that is neither imported nor present.");

            if (string.Equals(line.Side, nameof(DrCr.Debit), StringComparison.Ordinal)) dr += line.AmountPaisa;
            else if (string.Equals(line.Side, nameof(DrCr.Credit), StringComparison.Ordinal)) cr += line.AmountPaisa;
            else errors.Add($"{label} has a line with an unknown side '{line.Side}'.");
        }

        // Σ Dr == Σ Cr, in exact integer paisa (RQ-21).
        if (dr != cr)
            errors.Add($"{label} is unbalanced: Σ Dr = {dr} paisa, Σ Cr = {cr} paisa.");

        if (v.PartyId is { } pid && !plan.CanResolveLedger(pid, _target))
            errors.Add($"{label} references a party ledger that is neither imported nor present.");

        foreach (var il in v.InventoryLines)
        {
            if (!plan.CanResolveStockItem(il.StockItemId, _target))
                errors.Add($"{label} has an item line referencing a stock item that is neither imported nor present.");
            if (!plan.CanResolveGodown(il.GodownId, _target))
                errors.Add($"{label} has an item line referencing a godown that is neither imported nor present.");
        }
    }

    // ============================================================ static parse helpers (shared with the plan)

    /// <summary>Strict ISO yyyy-MM-dd parse (InvariantCulture, exact) — a locale-ambiguous form throws, so the
    /// engine never silently mis-reads a date. The parse layer already validated the header/voucher dates; this
    /// guards every other dated field (bill due dates, budget periods, exchange-rate dates, mfg/expiry).</summary>
    internal static DateOnly ParseDate(string iso) =>
        DateOnly.TryParseExact(iso, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
            ? d
            : throw new FormatException($"Date '{iso}' is not a valid ISO yyyy-MM-dd value.");
    internal static DateOnly? ParseDateOpt(string? iso) => string.IsNullOrEmpty(iso) ? null : ParseDate(iso);
    internal static TEnum ParseEnum<TEnum>(string name) where TEnum : struct, Enum => Enum.Parse<TEnum>(name);
}

/// <summary>How a single incoming master resolves against the target: its name and whether it is a duplicate.</summary>
internal readonly record struct MasterTarget(string Name, bool IsDuplicate);
