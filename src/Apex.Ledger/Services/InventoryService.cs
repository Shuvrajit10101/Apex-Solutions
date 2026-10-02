using Apex.Ledger.Domain;

namespace Apex.Ledger.Services;

/// <summary>
/// The inventory-masters service (catalog §9; requirements RQ-1..RQ-7, ER-7). Creates and deletes the
/// inventory masters — stock groups, stock categories, units, godowns, stock items and their opening
/// balances — enforcing the same discipline the accounting masters already ship with:
/// <list type="bullet">
///   <item>names are <b>unique within the company</b> (per master kind, case-insensitive, matching the
///     existing <c>FindXByName</c> convention);</item>
///   <item>a <b>parent must exist and cannot form a cycle</b> (stock-group / category / godown nesting);</item>
///   <item>a compound unit's components must exist and be simple units;</item>
///   <item>a master is <b>delete-blocked while referenced</b> (an item under a group; a unit/category/godown
///     used by an item or an opening allocation; a predefined master such as Main Location);</item>
/// </list>
/// The service throws <see cref="InvalidOperationException"/> on any violation (never mutating the company),
/// mirroring how <see cref="Company.AddCurrency"/> and the master ViewModels reject bad input. It is
/// framework- and DB-agnostic, so it is unit-tested exactly like the accounting core.
/// </summary>
public sealed class InventoryService
{
    private readonly Company _company;

    public InventoryService(Company company)
        => _company = company ?? throw new ArgumentNullException(nameof(company));

    // ------------------------------------------------------------------ Stock groups

    /// <summary>Creates a stock group; name unique, parent (if any) must exist and not cycle.</summary>
    public StockGroup CreateStockGroup(string name, Guid? parentId = null, string? alias = null, bool addQuantities = true)
    {
        var trimmed = RequireName(name, "stock group");
        if (_company.FindStockGroupByName(trimmed) is not null)
            throw new InvalidOperationException($"A stock group named '{trimmed}' already exists.");

        var group = new StockGroup(Guid.NewGuid(), trimmed, parentId, alias, addQuantities);
        EnsureStockGroupParentValid(group);
        _company.AddStockGroup(group);
        return group;
    }

    /// <summary>
    /// <b>Alters</b> an existing stock group in place — rename, re-alias, re-parent and the
    /// "<i>Should quantities be added?</i>" flag — resolved by its stable <paramref name="groupId"/>, so every
    /// child group and stock item that references it follows a rename automatically (they reference the Guid).
    ///
    /// <para>🔴 <b>THIS VERB DID NOT EXIST, AND ITS ABSENCE IS PART OF CENSUS ROW 3.13.</b> The Stock Group master
    /// shipped with Create only; the sole way to change one afterwards was <see cref="SetStockGroupParent"/>, which
    /// touches the parent and nothing else. A GST block captured at create time with no Alter route would have been
    /// a rate an operator could set once and never correct — on a rung <c>MasterAncestry.NearestStockGroupGst</c>
    /// reads at transaction time.</para>
    ///
    /// <para>Guards, all validated BEFORE anything is mutated so a rejected alteration leaves the company
    /// untouched: the name is required and unique excluding this group itself; a new parent must exist and must
    /// not sit inside this group's own sub-tree (the cycle check <see cref="EnsureStockGroupParentValid"/> already
    /// owns). Throws <see cref="InvalidOperationException"/> on any violation.</para>
    /// </summary>
    public StockGroup AlterStockGroup(
        Guid groupId, string name, Guid? parentId, string? alias = null, bool addQuantities = true)
    {
        var group = _company.FindStockGroup(groupId)
            ?? throw new InvalidOperationException($"Stock group {groupId} not found.");

        var trimmed = RequireName(name, "stock group");
        if (_company.FindStockGroupByName(trimmed) is { } clash && clash.Id != groupId)
            throw new InvalidOperationException($"A stock group named '{trimmed}' already exists.");

        // Validate the proposed parent against a COPY of the shape, then commit — mirroring SetStockGroupParent's
        // restore-on-throw, so a cyclic parent cannot leave the group half-altered.
        var previousParent = group.ParentId;
        group.ParentId = parentId;
        try { EnsureStockGroupParentValid(group); }
        catch { group.ParentId = previousParent; throw; }

        group.Name = trimmed;
        group.Alias = string.IsNullOrWhiteSpace(alias) ? null : alias.Trim();
        group.AddQuantities = addQuantities;
        return group;
    }

    /// <summary>Re-parents a stock group, rejecting a move that would create a cycle.</summary>
    public void SetStockGroupParent(Guid groupId, Guid? parentId)
    {
        var group = _company.FindStockGroup(groupId)
            ?? throw new InvalidOperationException($"Stock group {groupId} not found.");
        var previous = group.ParentId;
        group.ParentId = parentId;
        try { EnsureStockGroupParentValid(group); }
        catch { group.ParentId = previous; throw; }
    }

    /// <summary>
    /// Deletes a stock group, blocked while it has child groups or items under it —
    /// <see cref="MasterDeletionRules.EnsureStockGroupDeletable"/> owns the refusal (W28 C2, census 3.1).
    ///
    /// <para>The two conditions are unchanged; what moved is WHERE they are stated. Until wave 28 this method was
    /// the only caller-free delete service in the file and its refusals were worded in its own voice. Now that
    /// Alt+D reaches six masters through one shell arm, an operator who cannot delete a stock group and cannot
    /// delete a godown should be reading two sentences built the same way, with the same counts and the same
    /// named remedy — so all six refusals are built in <see cref="MasterDeletionRules"/>.</para>
    /// </summary>
    public void DeleteStockGroup(Guid groupId)
    {
        var group = _company.FindStockGroup(groupId)
            ?? throw new InvalidOperationException($"Stock group {groupId} not found.");
        MasterDeletionRules.EnsureStockGroupDeletable(_company, group);
        _company.RemoveStockGroup(group);
    }

    private void EnsureStockGroupParentValid(StockGroup group)
    {
        if (group.ParentId is not { } parentId) return;
        if (parentId == group.Id)
            throw new InvalidOperationException("A stock group cannot be its own parent.");
        // Walk up from the parent; a cycle means we meet `group` again.
        var seen = new HashSet<Guid> { group.Id };
        var cursor = _company.FindStockGroup(parentId)
            ?? throw new InvalidOperationException($"Parent stock group {parentId} not found.");
        while (true)
        {
            if (!seen.Add(cursor.Id))
                throw new InvalidOperationException($"Stock group '{group.Name}' would form a nesting cycle.");
            if (cursor.ParentId is not { } next) break;
            cursor = _company.FindStockGroup(next)
                ?? throw new InvalidOperationException($"Parent stock group {next} not found.");
        }
    }

    // ------------------------------------------------------------------ Stock categories

    /// <summary>Creates a stock category; name unique, parent (if any) must exist and not cycle.</summary>
    public StockCategory CreateStockCategory(string name, Guid? parentId = null, string? alias = null)
    {
        var trimmed = RequireName(name, "stock category");
        if (_company.FindStockCategoryByName(trimmed) is not null)
            throw new InvalidOperationException($"A stock category named '{trimmed}' already exists.");

        var category = new StockCategory(Guid.NewGuid(), trimmed, parentId, alias);
        EnsureStockCategoryParentValid(category);
        _company.AddStockCategory(category);
        return category;
    }

    /// <summary>Re-parents a stock category, rejecting a move that would create a cycle.</summary>
    public void SetStockCategoryParent(Guid categoryId, Guid? parentId)
    {
        var category = _company.FindStockCategory(categoryId)
            ?? throw new InvalidOperationException($"Stock category {categoryId} not found.");
        var previous = category.ParentId;
        category.ParentId = parentId;
        try { EnsureStockCategoryParentValid(category); }
        catch { category.ParentId = previous; throw; }
    }

    /// <summary>
    /// <b>Alters</b> an existing stock category in place — rename, re-alias and re-parent — resolved by its
    /// stable <paramref name="categoryId"/>, so every child category and stock item that references it follows a
    /// rename automatically (they reference the Guid). Census 3.2 / W28 C3.
    ///
    /// <para>Deliberately built as <see cref="AlterStockGroup"/>'s twin, down to the restore-on-throw: the
    /// proposed parent is written, validated by the cycle check, and put back if the check refuses, so a rejected
    /// alteration leaves the company exactly as it was. The name is required and unique EXCLUDING this category
    /// itself — without that exclusion an operator could never alter a category while leaving its name alone,
    /// which is the commonest alteration there is.</para>
    /// </summary>
    public StockCategory AlterStockCategory(Guid categoryId, string name, Guid? parentId, string? alias = null)
    {
        var category = _company.FindStockCategory(categoryId)
            ?? throw new InvalidOperationException($"Stock category {categoryId} not found.");

        var trimmed = RequireName(name, "stock category");
        if (_company.FindStockCategoryByName(trimmed) is { } clash && clash.Id != categoryId)
            throw new InvalidOperationException($"A stock category named '{trimmed}' already exists.");

        var previousParent = category.ParentId;
        category.ParentId = parentId;
        try { EnsureStockCategoryParentValid(category); }
        catch { category.ParentId = previousParent; throw; }

        category.Name = trimmed;
        category.Alias = string.IsNullOrWhiteSpace(alias) ? null : alias.Trim();
        return category;
    }

    /// <summary>
    /// Deletes a stock category, blocked while it has child categories or items using it —
    /// <see cref="MasterDeletionRules.EnsureStockCategoryDeletable"/> owns the refusal (W28 C2, census 3.2).
    /// </summary>
    public void DeleteStockCategory(Guid categoryId)
    {
        var category = _company.FindStockCategory(categoryId)
            ?? throw new InvalidOperationException($"Stock category {categoryId} not found.");
        MasterDeletionRules.EnsureStockCategoryDeletable(_company, category);
        _company.RemoveStockCategory(category);
    }

    private void EnsureStockCategoryParentValid(StockCategory category)
    {
        if (category.ParentId is not { } parentId) return;
        if (parentId == category.Id)
            throw new InvalidOperationException("A stock category cannot be its own parent.");
        var seen = new HashSet<Guid> { category.Id };
        var cursor = _company.FindStockCategory(parentId)
            ?? throw new InvalidOperationException($"Parent stock category {parentId} not found.");
        while (true)
        {
            if (!seen.Add(cursor.Id))
                throw new InvalidOperationException($"Stock category '{category.Name}' would form a nesting cycle.");
            if (cursor.ParentId is not { } next) break;
            cursor = _company.FindStockCategory(next)
                ?? throw new InvalidOperationException($"Parent stock category {next} not found.");
        }
    }

    // ------------------------------------------------------------------ Units

    /// <summary>Creates a simple unit (symbol + formal name + optional UQC + decimals 0–4).</summary>
    public Unit CreateSimpleUnit(string symbol, string formalName, int decimalPlaces = 0, string? unitQuantityCode = null)
    {
        var trimmed = RequireName(symbol, "unit symbol");
        if (_company.FindUnitByName(trimmed) is not null)
            throw new InvalidOperationException($"A unit '{trimmed}' already exists.");
        var unit = Unit.Simple(Guid.NewGuid(), trimmed, formalName, decimalPlaces, unitQuantityCode);
        _company.AddUnit(unit);
        return unit;
    }

    /// <summary>
    /// Creates a compound unit (first × factor + tail). Both components must exist and be <b>simple</b>
    /// units; the factor must be &gt; 0 and the first unit must differ from the tail (RQ-4).
    /// </summary>
    public Unit CreateCompoundUnit(
        string symbol,
        string formalName,
        Guid firstUnitId,
        Guid tailUnitId,
        int conversionNumerator,
        int conversionDenominator = 1)
    {
        var trimmed = RequireName(symbol, "unit symbol");
        if (_company.FindUnitByName(trimmed) is not null)
            throw new InvalidOperationException($"A unit '{trimmed}' already exists.");

        var first = _company.FindUnit(firstUnitId)
            ?? throw new InvalidOperationException($"First unit {firstUnitId} not found.");
        var tail = _company.FindUnit(tailUnitId)
            ?? throw new InvalidOperationException($"Tail unit {tailUnitId} not found.");
        if (first.IsCompound || tail.IsCompound)
            throw new InvalidOperationException("A compound unit's first and tail units must both be simple units.");

        var unit = Unit.Compound(Guid.NewGuid(), trimmed, formalName, firstUnitId, tailUnitId,
            conversionNumerator, conversionDenominator);
        _company.AddUnit(unit);
        return unit;
    }

    /// <summary>
    /// <b>Alters</b> an existing unit in place — symbol, formal name, UQC and decimal places — resolved by its
    /// stable <paramref name="unitId"/>, so every stock item and voucher line that measures in it follows a
    /// rename automatically (they reference the Guid). Census 3.5 / W28 C3.
    ///
    /// <para>🔴 <b>SCOPE, STATED HONESTLY: A COMPOUND UNIT'S COMPOSITION CANNOT BE ALTERED HERE, AND THAT IS A
    /// LIMIT RATHER THAN AN OVERSIGHT.</b> <see cref="Unit.IsCompound"/>, <see cref="Unit.FirstUnitId"/>,
    /// <see cref="Unit.TailUnitId"/>, <see cref="Unit.ConversionNumerator"/> and
    /// <see cref="Unit.ConversionDenominator"/> are all get-only on the domain type — deliberately, because the
    /// conversion is the arithmetic every quantity in that unit has already been stored against. Re-pointing a
    /// Dozen from 12 Nos to 10 Nos would silently restate every posted line measured in Dozens, which is a
    /// wrong-money change dressed as a master edit. Altering the composition therefore needs its own slice with
    /// its own restatement decision; this verb changes only what is safe to change under a stable conversion.
    /// The wave 28 report records it as NOT reached rather than claiming row 3.5 is closed.</para>
    ///
    /// <para><paramref name="unitQuantityCode"/> and <paramref name="decimalPlaces"/> are applied to SIMPLE units
    /// only, mirroring <see cref="Unit.Simple"/>: a compound unit carries no UQC and inherits precision from its
    /// components, so writing either onto one would invent state the create path cannot produce.</para>
    /// </summary>
    public Unit AlterUnit(
        Guid unitId, string symbol, string formalName, int decimalPlaces = 0, string? unitQuantityCode = null)
    {
        var unit = _company.FindUnit(unitId)
            ?? throw new InvalidOperationException($"Unit {unitId} not found.");

        var trimmedSymbol = RequireName(symbol, "unit symbol");
        if (_company.FindUnitByName(trimmedSymbol) is { } clash && clash.Id != unitId)
            throw new InvalidOperationException($"A unit '{trimmedSymbol}' already exists.");
        if (string.IsNullOrWhiteSpace(formalName))
            throw new InvalidOperationException("A unit formal name is required.");
        if (!unit.IsCompound && decimalPlaces is < 0 or > 4)
            throw new InvalidOperationException("Decimal places must be between 0 and 4.");

        unit.Symbol = trimmedSymbol;
        unit.FormalName = formalName.Trim();
        if (!unit.IsCompound)
        {
            unit.DecimalPlaces = decimalPlaces;
            unit.UnitQuantityCode = string.IsNullOrWhiteSpace(unitQuantityCode) ? null : unitQuantityCode.Trim();
        }
        return unit;
    }

    /// <summary>
    /// Deletes a unit, blocked while it is in use — <see cref="MasterDeletionRules.EnsureUnitDeletable"/> owns
    /// the refusal (W28 C2, census 3.5).
    ///
    /// <para>🔴 <b>THE GUARD THIS METHOD USED TO CARRY WAS INCOMPLETE, AND WIRING Alt+D ONTO IT UNCHANGED WOULD
    /// HAVE BEEN THE WORST OUTCOME AVAILABLE.</b> It counted <c>stock_items.base_unit_id</c> and the compound
    /// components and stopped, so a unit named by <c>stock_items.alternate_unit_id</c> (census 3.6, v60),
    /// <c>voucher_inventory_lines.unit_id</c> or <c>inventory_allocations.unit_id</c> was deletable. Those three
    /// columns declare <c>REFERENCES units(id)</c>, so the removal succeeded in memory and the next Save threw
    /// <c>SQLITE_CONSTRAINT_FOREIGNKEY</c>, after which the open company could never be written again.</para>
    /// </summary>
    public void DeleteUnit(Guid unitId)
    {
        var unit = _company.FindUnit(unitId)
            ?? throw new InvalidOperationException($"Unit {unitId} not found.");
        MasterDeletionRules.EnsureUnitDeletable(_company, unit);
        _company.RemoveUnit(unit);
    }

    // ------------------------------------------------------------------ Godowns

    /// <summary>Creates a godown; name unique, parent (if any) must exist and not cycle.
    /// <para><paramref name="jobCostCentreId"/> is the vendor's <b>"Set job/project for job costing"</b>
    /// (census 9.6): the cost centre this godown IS, as a job/project. It is validated to exist — a link to a
    /// cost centre that is not in the book would produce a Job Work Analysis row named "(unknown)" carrying real
    /// money, and would break the schema's foreign key at Save time with a raw persistence error instead of a
    /// clean domain one.</para></summary>
    public Godown CreateGodown(
        string name, Guid? parentId = null, string? alias = null, bool thirdParty = false,
        Guid? jobCostCentreId = null)
    {
        var trimmed = RequireName(name, "godown");
        if (_company.FindGodownByName(trimmed) is not null)
            throw new InvalidOperationException($"A godown named '{trimmed}' already exists.");
        if (jobCostCentreId is { } centreId && _company.FindCostCentre(centreId) is null)
            throw new InvalidOperationException($"Cost centre {centreId} not found.");

        var godown = new Godown(Guid.NewGuid(), trimmed, parentId, alias, thirdParty)
        {
            JobCostCentreId = jobCostCentreId,
        };
        EnsureGodownParentValid(godown);
        _company.AddGodown(godown);
        return godown;
    }

    /// <summary>
    /// Sets (or clears, with <c>null</c>) a godown's <b>"Set job/project for job costing"</b> cost centre
    /// (census 9.6). The centre must exist, for the reasons given on <see cref="CreateGodown"/>.
    /// </summary>
    public void SetGodownJobCostCentre(Guid godownId, Guid? costCentreId)
    {
        var godown = _company.FindGodown(godownId)
            ?? throw new InvalidOperationException($"Godown {godownId} not found.");
        if (costCentreId is { } centreId && _company.FindCostCentre(centreId) is null)
            throw new InvalidOperationException($"Cost centre {centreId} not found.");
        godown.JobCostCentreId = costCentreId;
    }

    /// <summary>Re-parents a godown, rejecting a move that would create a cycle.</summary>
    public void SetGodownParent(Guid godownId, Guid? parentId)
    {
        var godown = _company.FindGodown(godownId)
            ?? throw new InvalidOperationException($"Godown {godownId} not found.");
        var previous = godown.ParentId;
        godown.ParentId = parentId;
        try { EnsureGodownParentValid(godown); }
        catch { godown.ParentId = previous; throw; }
    }

    /// <summary>
    /// <b>Alters</b> an existing godown in place — rename, re-alias, re-parent, the third-party flag and the
    /// job/project cost centre — resolved by its stable <paramref name="godownId"/>, so every line stored there
    /// follows a rename automatically (they reference the Guid). Census 3.7 / W28 C3, and the verb the vendor
    /// names at <i>"Gateway of Tally &gt; Inventory Info. &gt; Godowns &gt; and select Alter"</i>.
    ///
    /// <para>Built as <see cref="AlterStockGroup"/>'s twin, with the same restore-on-throw around the parent so a
    /// cyclic re-parent leaves the godown untouched, plus <see cref="CreateGodown"/>'s own existence check on the
    /// job cost centre — a link to a centre that is not in the book would break the schema's foreign key at Save
    /// time with a raw persistence error instead of a clean domain one.</para>
    ///
    /// <para><b><see cref="Godown.IsMainLocation"/> is get-only and is NOT altered.</b> The seeded default is
    /// renameable and re-aliasable like any other godown, but it cannot be demoted, because the flag is what every
    /// inventory line falls back to when no godown is named.</para>
    /// </summary>
    public Godown AlterGodown(
        Guid godownId, string name, Guid? parentId, string? alias = null, bool thirdParty = false,
        Guid? jobCostCentreId = null)
    {
        var godown = _company.FindGodown(godownId)
            ?? throw new InvalidOperationException($"Godown {godownId} not found.");

        var trimmed = RequireName(name, "godown");
        if (_company.FindGodownByName(trimmed) is { } clash && clash.Id != godownId)
            throw new InvalidOperationException($"A godown named '{trimmed}' already exists.");
        if (jobCostCentreId is { } centreId && _company.FindCostCentre(centreId) is null)
            throw new InvalidOperationException($"Cost centre {centreId} not found.");

        var previousParent = godown.ParentId;
        godown.ParentId = parentId;
        try { EnsureGodownParentValid(godown); }
        catch { godown.ParentId = previousParent; throw; }

        godown.Name = trimmed;
        godown.Alias = string.IsNullOrWhiteSpace(alias) ? null : alias.Trim();
        godown.ThirdParty = thirdParty;
        godown.JobCostCentreId = jobCostCentreId;
        return godown;
    }

    /// <summary>
    /// Deletes a godown — <see cref="MasterDeletionRules.EnsureGodownDeletable"/> owns the refusal (W28 C2,
    /// census 3.7), which is the vendor's three stated conditions plus the six further columns our schema has.
    ///
    /// <para>🔴 <b>THE GUARD THIS METHOD USED TO CARRY COUNTED THREE OF TWELVE REFERENCES.</b> It refused the
    /// default location, child godowns and opening balances. It did NOT count
    /// <c>voucher_inventory_lines.godown_id</c>, <c>inventory_allocations.godown_id</c>,
    /// <c>order_lines.godown_id</c>, <c>physical_stock_lines.godown_id</c>, <c>batch_masters.godown_id</c>,
    /// <c>bom_lines.godown_id</c>, <c>pos_voucher_type_config.default_godown_id</c>,
    /// <c>job_work_orders.fg_godown_id</c> or <c>job_work_order_lines.godown_id</c> — so a godown holding every
    /// movement the business had made was deletable, and the next Save would fail its foreign key and keep
    /// failing. The vendor's own wording is the plainest statement that this was wrong: a godown may be deleted
    /// only if it <i>"does not store any stock items"</i> and <i>"was not used in any transaction"</i>.</para>
    /// </summary>
    public void DeleteGodown(Guid godownId)
    {
        var godown = _company.FindGodown(godownId)
            ?? throw new InvalidOperationException($"Godown {godownId} not found.");
        MasterDeletionRules.EnsureGodownDeletable(_company, godown);
        _company.RemoveGodown(godown);
    }

    private void EnsureGodownParentValid(Godown godown)
    {
        if (godown.ParentId is not { } parentId) return;
        if (parentId == godown.Id)
            throw new InvalidOperationException("A godown cannot be its own parent.");
        var seen = new HashSet<Guid> { godown.Id };
        var cursor = _company.FindGodown(parentId)
            ?? throw new InvalidOperationException($"Parent godown {parentId} not found.");
        while (true)
        {
            if (!seen.Add(cursor.Id))
                throw new InvalidOperationException($"Godown '{godown.Name}' would form a nesting cycle.");
            if (cursor.ParentId is not { } next) break;
            cursor = _company.FindGodown(next)
                ?? throw new InvalidOperationException($"Parent godown {next} not found.");
        }
    }

    // ------------------------------------------------------------------ Stock items

    /// <summary>
    /// Creates a stock item under a group (required, must exist), an optional category (must exist) and a
    /// base unit (required, must exist). Name unique; valuation method defaults to Average Cost (DP-1).
    /// </summary>
    public StockItem CreateStockItem(
        string name,
        Guid stockGroupId,
        Guid baseUnitId,
        Guid? categoryId = null,
        string? alias = null,
        StockValuationMethod valuationMethod = StockValuationMethod.AverageCost,
        string? hsnSacCode = null,
        bool isTaxable = false,
        decimal? reorderLevel = null,
        decimal? minimumOrderQuantity = null,
        Money? standardCost = null)
    {
        var trimmed = RequireName(name, "stock item");
        if (_company.FindStockItemByName(trimmed) is not null)
            throw new InvalidOperationException($"A stock item named '{trimmed}' already exists.");
        if (_company.FindStockGroup(stockGroupId) is null)
            throw new InvalidOperationException($"Stock group {stockGroupId} not found.");
        if (_company.FindUnit(baseUnitId) is null)
            throw new InvalidOperationException($"Base unit {baseUnitId} not found.");
        if (categoryId is { } cid && _company.FindStockCategory(cid) is null)
            throw new InvalidOperationException($"Stock category {cid} not found.");

        var item = new StockItem(Guid.NewGuid(), trimmed, stockGroupId, baseUnitId, categoryId, alias,
            valuationMethod, hsnSacCode, isTaxable, reorderLevel, minimumOrderQuantity, standardCost);
        _company.AddStockItem(item);
        return item;
    }

    /// <summary>
    /// Census 3.6 — sets (or clears) a stock item's <b>Alternate Unit</b> and its conversion factor, as one
    /// operation, and refuses every half-state (schema v60).
    ///
    /// <para><b>R7 — ATTESTED.</b> <c>help.tallysolutions.com/manage-stock-item-tally/</c>: after the base unit is
    /// chosen "<i>The <b>Alternate units</b> field appears</i>" and the operator "<i>provide[s] the conversion
    /// factor between the simple or compound units and alternative units</i>".</para>
    ///
    /// <para>🔴 <b>THIS IS THE CHOKE POINT, AND IT EXISTS BECAUSE EVERY REFUSAL BELOW IS A WRONG-QUANTITY BUG IF
    /// IT LANDS INSTEAD IN A SCREEN.</b> Each of the four states it rejects would otherwise persist and read back
    /// as a plausible item:</para>
    /// <list type="bullet">
    ///   <item>an <b>alternate unit with no factor</b> — the derived quantity has nothing to divide by;</item>
    ///   <item>a <b>factor with no alternate unit</b> — a number stored against no unit at all;</item>
    ///   <item>a <b>zero or negative factor</b> — zero divides by zero on every display, and a negative one prints
    ///     a negative quantity for stock that is physically present;</item>
    ///   <item>the <b>alternate unit equal to the base unit</b>, which is either a no-op dressed up as a
    ///     conversion or, with a factor other than 1, a claim that a unit converts into itself at some other
    ///     rate.</item>
    /// </list>
    ///
    /// <para>Passing <c>null</c> for <paramref name="alternateUnitId"/> clears BOTH fields, so "the operator
    /// removed the alternate unit" cannot leave an orphan factor behind.</para>
    /// </summary>
    public void SetAlternateUnit(StockItem item, Guid? alternateUnitId, decimal? conversion)
    {
        ArgumentNullException.ThrowIfNull(item);

        if (alternateUnitId is not { } altId)
        {
            if (conversion is not null)
                throw new InvalidOperationException(
                    "A conversion factor needs an Alternate Unit — pick one, or clear the factor.");
            item.AlternateUnitId = null;
            item.AlternateUnitConversion = null;
            return;
        }

        var alternate = _company.FindUnit(altId)
            ?? throw new InvalidOperationException($"Alternate unit {altId} not found.");

        if (altId == item.BaseUnitId)
            throw new InvalidOperationException(
                $"The Alternate Unit cannot be the item's base unit ('{alternate.Symbol}') — an alternate unit "
                + "exists to express the same quantity in a DIFFERENT unit.");

        if (conversion is not { } factor)
            throw new InvalidOperationException(
                "An Alternate Unit needs a conversion factor — how many base units make one alternate unit.");

        if (factor <= 0m)
            throw new InvalidOperationException(
                "The conversion factor must be greater than zero — it is how many base units make one alternate "
                + "unit, so zero or a negative figure cannot describe any real quantity.");

        item.AlternateUnitId = altId;
        item.AlternateUnitConversion = factor;
    }

    /// <summary>
    /// Deletes a stock item, blocked by the SHARED delete guard.
    ///
    /// <para>🔴 <b>This used to carry its own, weaker rule</b> — "blocked while it carries any opening allocation",
    /// one of the eleven columns that reference <c>stock_items(id)</c>. Phase 10.11 S4 added a second delete route
    /// through <see cref="MasterDeletionRules.EnsureStockItemDeletable"/> and left this one in place with no caller,
    /// so the next caller would have picked the weak one and deleted an item a BOM, a batch or an invoice line
    /// still held — an unsavable company. It now delegates: one rule, one place to correct it, and the two routes
    /// cannot diverge.</para>
    /// </summary>
    public void DeleteStockItem(Guid stockItemId)
    {
        var item = _company.FindStockItem(stockItemId)
            ?? throw new InvalidOperationException($"Stock item {stockItemId} not found.");
        MasterDeletionRules.EnsureStockItemDeletable(_company, item);
        _company.RemoveStockItem(item);
    }

    // ------------------------------------------------------------------ Opening balances

    /// <summary>
    /// Adds an opening-stock allocation for an item at a godown (both must exist), with an optional batch
    /// label. Quantity/rate are validated by <see cref="StockOpeningBalance"/>; value = qty × rate to the paisa.
    /// </summary>
    public StockOpeningBalance AddOpeningBalance(
        Guid stockItemId,
        Guid godownId,
        decimal quantity,
        Money rate,
        string? batchLabel = null,
        DateOnly? manufacturingDate = null,
        DateOnly? expiryDate = null)
    {
        if (_company.FindStockItem(stockItemId) is null)
            throw new InvalidOperationException($"Stock item {stockItemId} not found.");
        if (_company.FindGodown(godownId) is null)
            throw new InvalidOperationException($"Godown {godownId} not found.");

        var balance = new StockOpeningBalance(Guid.NewGuid(), stockItemId, godownId, quantity, rate,
            batchLabel, manufacturingDate, expiryDate);
        _company.AddStockOpeningBalance(balance);
        return balance;
    }

    /// <summary>The paisa-exact total opening value of a stock item = Σ of its allocations' values.</summary>
    public Money OpeningValueOf(Guid stockItemId)
    {
        var total = Money.Zero;
        foreach (var b in _company.OpeningBalancesFor(stockItemId))
            total += b.Value;
        return total;
    }

    // ------------------------------------------------------------------ helpers

    private static string RequireName(string? value, string what)
    {
        var trimmed = (value ?? string.Empty).Trim();
        if (trimmed.Length == 0)
            throw new InvalidOperationException($"A {what} name is required.");
        return trimmed;
    }
}
