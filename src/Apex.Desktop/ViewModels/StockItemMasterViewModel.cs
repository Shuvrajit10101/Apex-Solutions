using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Services;
using Apex.Desktop.Services;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Apex.Desktop.ViewModels;

/// <summary>A stock-item row for the existing-items list on the master screen.</summary>
public sealed class StockItemListRow
{
    public string Name { get; init; } = string.Empty;
    public string Under { get; init; } = string.Empty;
    public string Unit { get; init; } = string.Empty;
    public string Valuation { get; init; } = string.Empty;
    public string OpeningValue { get; init; } = string.Empty;
}

/// <summary>
/// One option in a "Category" / "Godown" picker where a blank first entry means "none / default".
/// <see cref="Category"/> is null for the "(none)" option.
/// </summary>
public sealed class OptionalStockCategoryOption
{
    public StockCategory? Category { get; init; }
    public string Display { get; init; } = string.Empty;
    public bool IsNone => Category is null;
}

/// <summary>A valuation-method option for the picker (label + the enum value).</summary>
public sealed class ValuationMethodOption
{
    public StockValuationMethod Method { get; init; }
    public string Display { get; init; } = string.Empty;
}

/// <summary>A GST taxability option for the picker (label + the enum value).</summary>
public sealed class GstTaxabilityOption
{
    public GstTaxability Value { get; init; }
    public string Display { get; init; } = string.Empty;
}

/// <summary>
/// A GST rate-slab option for the item's rate picker: a seeded slab (0/5/18/40%) or the "(none)" entry
/// that leaves the item's rate unresolved (resolved from the sales/purchase ledger or company instead).
/// </summary>
public sealed class GstRateOption
{
    /// <summary>The integrated rate in basis points, or null for "(none) — unresolved here".</summary>
    public int? RateBasisPoints { get; init; }
    public string Display { get; init; } = string.Empty;
    public bool IsNone => RateBasisPoints is null;
}

/// <summary>
/// A Compensation-Cess valuation-mode option for the item's cess-override picker (Phase 9 slice 1): "(none) —
/// inherit from the dated cess master by HSN + date", or an explicit per-item ad-valorem / specific / RSP-factor
/// override. <see cref="Mode"/> is null for the "(none)" entry.
/// </summary>
public sealed class CessValuationModeOption
{
    public CessValuationMode? Mode { get; init; }
    public string Display { get; init; } = string.Empty;
    public bool IsNone => Mode is null;
}

/// <summary>
/// The Stock-Item creation master ("Masters → Create → Inventory Masters → Stock Item", catalog §9; RQ-6).
/// Captures a name + optional alias, a required <b>Under</b> stock group, an optional <b>Category</b>, a
/// required base <b>Unit</b>, GST placeholders (HSN/SAC + Taxable), a <b>Valuation method</b> (default
/// Average Cost, DP-1), and simple reorder fields (reorder level + minimum order qty). It also captures a
/// simple <b>Opening Balance</b> (all optional; if a quantity is entered): Godown (default Main Location),
/// Quantity, Rate, and an optional Batch/Lot label — wired to <see cref="InventoryService.AddOpeningBalance"/>.
///
/// <para>Pre-validates: opening-balance <b>Rate to the paisa</b> (2 dp), <b>Quantity / Reorder / MOQ to 6
/// dp</b>, and required fields present, BEFORE calling the engine — then wraps the engine calls in try/catch
/// and surfaces any domain error to <see cref="Message"/> so nothing crashes the UI. When the item is
/// created but a later opening-balance step fails, the item still persists and the message explains the skip.</para>
///
/// <para>MVVM boundary: references the domain + persistence but no Avalonia/UI types, so it is headlessly
/// unit-testable.</para>
/// </summary>
public sealed partial class StockItemMasterViewModel : ViewModelBase, IMasterListExportSource
{
    private readonly Company _company;
    private readonly CompanyStorage _storage;
    private readonly Action _onChanged;

    /// <inheritdoc/>
    /// <remarks>Stock-item export normally uses the bespoke
    /// <see cref="Services.MasterListTabularProjector.ProjectStockItems"/>; this snapshot is the generic-path
    /// equivalent (identical columns, Opening Value as a numeric cell).</remarks>
    public MasterListSnapshot ToMasterListSnapshot() => new(
        "Stock Items",
        new[]
        {
            MasterListColumn.Text("Name"),
            MasterListColumn.Text("Under"),
            MasterListColumn.Text("Unit"),
            MasterListColumn.Text("Valuation"),
            MasterListColumn.Number("Opening Value"),
        },
        Existing.Select(r => (IReadOnlyList<string>)new[]
        {
            r.Name, r.Under, r.Unit, r.Valuation, r.OpeningValue,
        }).ToList());

    /// <summary>The stock groups an item can sit under (required — the item needs one).</summary>
    public ObservableCollection<StockGroup> Groups { get; } = new();

    /// <summary>The category options: "(none)" plus every existing category (optional axis).</summary>
    public ObservableCollection<OptionalStockCategoryOption> CategoryOptions { get; } = new();

    /// <summary>The units an item can be measured in (required base unit).</summary>
    public ObservableCollection<Unit> Units { get; } = new();

    /// <summary>The godowns the opening balance can sit in (defaults to Main Location).</summary>
    public ObservableCollection<Godown> Godowns { get; } = new();

    /// <summary>The valuation methods offered (Average Cost first / default, DP-1).</summary>
    public ObservableCollection<ValuationMethodOption> ValuationMethods { get; } = new();

    /// <summary>The existing stock items, refreshed after each create.</summary>
    public ObservableCollection<StockItemListRow> Existing { get; } = new();

    // ---- Item identity ----
    [ObservableProperty] private string _name = string.Empty;
    [ObservableProperty] private string _alias = string.Empty;
    [ObservableProperty] private StockGroup? _selectedGroup;
    [ObservableProperty] private OptionalStockCategoryOption? _selectedCategory;
    [ObservableProperty] private Unit? _selectedUnit;
    [ObservableProperty] private ValuationMethodOption? _selectedValuation;
    [ObservableProperty] private string _hsnSacCode = string.Empty;
    [ObservableProperty] private bool _isTaxable;
    [ObservableProperty] private string _reorderLevelText = string.Empty;
    [ObservableProperty] private string _minimumOrderQtyText = string.Empty;

    // ---- Batch switches (Phase 6 Cluster 1; RQ-2) — only offered when the company flag is on ----
    [ObservableProperty] private bool _maintainInBatches;
    [ObservableProperty] private bool _trackManufacturingDate;
    [ObservableProperty] private bool _useExpiryDates;

    /// <summary>
    /// True iff the company flag <see cref="Company.MaintainBatchwiseDetails"/> is on (RQ-52) — the three item
    /// batch switches (<see cref="MaintainInBatches"/> / <see cref="TrackManufacturingDate"/> /
    /// <see cref="UseExpiryDates"/>) are surfaced only then, per the F11/F12 config-driven visibility model.
    /// </summary>
    public bool ShowBatchSwitches => _company.MaintainBatchwiseDetails;

    // ---- BOM switch (Phase 6 Cluster 2; RQ-10) — only offered when the F12 "Set Components (BOM)" config is on ----

    /// <summary>
    /// The item's <b>Set Components (BOM)</b> field (RQ-10): when on, the item is a manufactured finished good
    /// with one or more Bills of Materials. Offered only when the F12 config <see cref="ShowBomSwitch"/> is on.
    /// Creating a BOM through the BOM master also turns the persisted flag on, so this master's checkbox is the
    /// declaration a user makes up front.
    /// </summary>
    [ObservableProperty] private bool _setComponents;

    /// <summary>
    /// True iff the F12 company config <see cref="Company.SetComponentsBom"/> is on (RQ-10/RQ-52) — the item's
    /// "Set Components (BOM)" switch is surfaced only then, per the config-driven visibility model.
    /// </summary>
    public bool ShowBomSwitch => _company.SetComponentsBom;

    // ---- GST details (catalog §12; phase4 RQ-8) — only offered when GST is enabled ----
    [ObservableProperty] private GstTaxabilityOption? _taxability;
    [ObservableProperty] private GstRateOption? _gstRate;

    /// <summary>The GST taxability options (Taxable / Exempt / Nil-Rated / Non-GST).</summary>
    public ObservableCollection<GstTaxabilityOption> Taxabilities { get; } = new();

    /// <summary>The GST rate options: "(none)" plus the company's seeded slabs (0/5/18/40%).</summary>
    public ObservableCollection<GstRateOption> GstRates { get; } = new();

    /// <summary>True iff GST is enabled for the company — the item-GST sub-form is only offered then.</summary>
    public bool GstEnabled => _company.GstEnabled;

    // ---- GST 2.0 RSP valuation + Compensation-Cess override (Phase 9 slice 1; RQ-1/RQ-2) — only offered when GST
    // is enabled. All default off/blank so a plain GST item is byte-identical to a Phase-4/8 item (ER-13). ----

    /// <summary>
    /// The item's GST valuation basis: off ⇒ the §15 transaction value (the default for every item); on ⇒ the
    /// declared Retail Sale Price (the tobacco/pan-masala carve-out). Drives <see cref="StockItemGstDetails.ValuationBasis"/>.
    /// </summary>
    [ObservableProperty] private bool _valuationIsRsp;

    /// <summary>The declared Retail Sale Price per unit (₹, paisa-exact) — drives RSP-factor cess and RSP GST
    /// valuation; blank ⇒ unset.</summary>
    [ObservableProperty] private string _retailSalePriceText = string.Empty;

    /// <summary>Whether the item declares an <b>explicit per-item Compensation-Cess override</b> (its own valuation
    /// mode + figures). Off ⇒ the item still inherits any dated cess-master row for its HSN (cess is HSN-driven in
    /// law); what suppresses cess entirely is a non-Taxable taxability, not this flag.</summary>
    [ObservableProperty] private bool _cessApplicable;

    /// <summary>The per-item cess valuation-mode override; "(none)" ⇒ inherit from the dated cess master by HSN + date.</summary>
    [ObservableProperty] private CessValuationModeOption? _cessMode;

    /// <summary>The ad-valorem cess rate override as a percent (e.g. "22"); only for the ad-valorem mode.</summary>
    [ObservableProperty] private string _cessRatePercentText = string.Empty;

    /// <summary>The specific per-unit cess override (₹/unit, paisa-exact); only for the specific mode.</summary>
    [ObservableProperty] private string _cessPerUnitText = string.Empty;

    /// <summary>The RSP-factor cess override (e.g. "0.32" ⇒ 0.32 × RSP per unit); only for the RSP-factor mode.</summary>
    [ObservableProperty] private string _cessRspFactorText = string.Empty;

    /// <summary>The per-item cess valuation-mode options ("(none)" + ad-valorem / specific / RSP-factor).</summary>
    public ObservableCollection<CessValuationModeOption> CessValuationModes { get; } = new();

    // ---- TCS Nature of Goods (Phase 7 slice 1; catalog §13) — only offered when TCS is enabled ----

    /// <summary>True iff TCS is enabled for the company — the item-TCS nature field is only offered then.</summary>
    public bool TcsEnabled => _company.TcsEnabled;

    /// <summary>The item's default Nature of Goods (§206C) — "(none)" leaves it unset (no auto-TCS on its sale).</summary>
    [ObservableProperty] private NatureOfGoodsChoice? _selectedTcsNature;

    /// <summary>The Nature-of-Goods picker options ("(none)" + every defined §206C nature).</summary>
    public IReadOnlyList<NatureOfGoodsChoice> TcsNatureChoices { get; private set; } =
        System.Array.Empty<NatureOfGoodsChoice>();

    // ---- Opening balance (all optional) ----
    [ObservableProperty] private Godown? _openingGodown;
    [ObservableProperty] private string _openingQuantityText = string.Empty;
    [ObservableProperty] private string _openingRateText = string.Empty;
    [ObservableProperty] private string _openingBatchLabel = string.Empty;

    [ObservableProperty] private string? _message;

    public StockItemMasterViewModel(Company company, CompanyStorage storage, Action onChanged)
    {
        _company = company ?? throw new ArgumentNullException(nameof(company));
        _storage = storage ?? throw new ArgumentNullException(nameof(storage));
        _onChanged = onChanged ?? throw new ArgumentNullException(nameof(onChanged));

        ValuationMethods.Add(new ValuationMethodOption { Method = StockValuationMethod.AverageCost, Display = "Average Cost" });
        ValuationMethods.Add(new ValuationMethodOption { Method = StockValuationMethod.Fifo, Display = "FIFO" });
        ValuationMethods.Add(new ValuationMethodOption { Method = StockValuationMethod.Lifo, Display = "LIFO" });
        ValuationMethods.Add(new ValuationMethodOption { Method = StockValuationMethod.StandardCost, Display = "Standard Cost" });
        ValuationMethods.Add(new ValuationMethodOption { Method = StockValuationMethod.LastPurchaseCost, Display = "Last Purchase Cost" });
        ValuationMethods.Add(new ValuationMethodOption { Method = StockValuationMethod.LastSaleCost, Display = "Last Sale Cost" });
        SelectedValuation = ValuationMethods.First();

        Taxabilities.Add(new GstTaxabilityOption { Value = GstTaxability.Taxable, Display = "Taxable" });
        Taxabilities.Add(new GstTaxabilityOption { Value = GstTaxability.Exempt, Display = "Exempt" });
        Taxabilities.Add(new GstTaxabilityOption { Value = GstTaxability.NilRated, Display = "Nil-Rated" });
        Taxabilities.Add(new GstTaxabilityOption { Value = GstTaxability.NonGst, Display = "Non-GST" });
        Taxability = Taxabilities.First();

        GstRates.Add(new GstRateOption { RateBasisPoints = null, Display = "◦ (none)" });
        var slabs = _company.Gst?.RateSlabs ?? Array.Empty<GstRateSlab>();
        foreach (var slab in slabs.OrderBy(s => s.RateBasisPoints))
            GstRates.Add(new GstRateOption { RateBasisPoints = slab.RateBasisPoints, Display = slab.Label });
        GstRate = GstRates.First();

        // Cess valuation-mode override picker (Phase 9 slice 1): "(none)" inherits the dated cess master by HSN.
        CessValuationModes.Add(new CessValuationModeOption { Mode = null, Display = "◦ (inherit by HSN)" });
        CessValuationModes.Add(new CessValuationModeOption { Mode = CessValuationMode.AdValorem, Display = "Ad-valorem (% of value)" });
        CessValuationModes.Add(new CessValuationModeOption { Mode = CessValuationMode.Specific, Display = "Specific (₹ per unit)" });
        CessValuationModes.Add(new CessValuationModeOption { Mode = CessValuationMode.RetailSalePriceFactor, Display = "RSP-factor (× retail price)" });
        CessMode = CessValuationModes.First();

        TcsNatureChoices = TdsTcsDisplay.NatureOfGoodsChoices(company);
        SelectedTcsNature = TcsNatureChoices[0]; // (none)

        RefreshPickers();
        RefreshList();
    }

    /// <summary>True once at least one stock group AND one unit exist — an item needs both to be created.</summary>
    public bool CanCreate => Groups.Count > 0 && Units.Count > 0;

    /// <summary>
    /// Ctrl+A create: validates the required name/group/unit, pre-validates reorder/MOQ to 6 dp and any
    /// opening-balance quantity (6 dp) + rate (paisa/2 dp), then creates the item via the engine and
    /// persists. If an opening quantity is entered, adds the opening allocation too. Any domain error is
    /// surfaced to <see cref="Message"/> without crashing the UI.
    /// </summary>
    public bool Create()
    {
        Message = null;
        var name = (Name ?? string.Empty).Trim();

        if (string.IsNullOrWhiteSpace(name))
        {
            Message = "A stock item name is required.";
            return false;
        }
        if (SelectedGroup is null)
        {
            Message = "Pick a stock group to place the item under (create a Stock Group first).";
            return false;
        }
        if (SelectedUnit is null)
        {
            Message = "Pick a base unit for the item (create a Unit first).";
            return false;
        }

        // Pre-validate reorder level + minimum order qty (6 dp) so the engine's domain error never fires.
        if (!TryParseOptionalQuantity(ReorderLevelText, "Reorder level", out var reorderLevel))
            return false;
        if (!TryParseOptionalQuantity(MinimumOrderQtyText, "Minimum order quantity", out var minimumOrderQty))
            return false;

        // Pre-validate the opening balance (only if a quantity was entered).
        var wantsOpening = !string.IsNullOrWhiteSpace(OpeningQuantityText);
        decimal openingQty = 0m;
        Money openingRate = Money.Zero;
        if (wantsOpening)
        {
            if (!TryParseQuantity(OpeningQuantityText, "Opening quantity", out openingQty) || openingQty < 0m)
            {
                if (Message is null) Message = "Opening quantity must be a number ≥ 0.";
                return false;
            }
            if (OpeningGodown is null)
            {
                Message = "Pick a godown for the opening stock (default: Main Location).";
                return false;
            }
            if (!TryParseRate(OpeningRateText, out var rate) || rate < 0m)
            {
                Message = "Opening rate must be a number ≥ 0 (₹ per unit).";
                return false;
            }
            openingRate = Money.FromRupees(rate);
            if (!openingRate.IsPaisaExact)
            {
                Message = $"Opening stock rate {rate} must be to the paisa (2 decimal places).";
                return false;
            }
        }

        var categoryId = SelectedCategory?.Category?.Id;
        var alias = string.IsNullOrWhiteSpace(Alias) ? null : Alias.Trim();
        var hsn = string.IsNullOrWhiteSpace(HsnSacCode) ? null : HsnSacCode.Trim();
        var valuation = SelectedValuation?.Method ?? StockValuationMethod.AverageCost;

        // GST details (only offered when GST is enabled). Pre-validate the HSN length + taxable/rate pairing
        // BEFORE the engine so a bad value is a friendly message, not a crash.
        StockItemGstDetails? gstBlock = null;
        var isTaxableFlag = IsTaxable;
        if (GstEnabled)
        {
            var taxability = (Taxability ?? Taxabilities.First()).Value;
            var rateBp = GstRate?.RateBasisPoints;

            if (hsn is not null && (hsn.Length is not (4 or 6 or 8) || !hsn.All(char.IsDigit)))
            {
                Message = $"HSN/SAC '{hsn}' must be 4, 6 or 8 digits (numeric).";
                return false;
            }
            // A non-taxable item carries no positive rate (the engine rejects that pairing).
            if (taxability != GstTaxability.Taxable) rateBp = null;

            gstBlock = new StockItemGstDetails
            {
                HsnSac = hsn,
                Taxability = taxability,
                RateBasisPoints = rateBp,
                SupplyType = GstSupplyType.Goods,
            };
            isTaxableFlag = taxability == GstTaxability.Taxable; // keep the Phase-3 placeholder consistent

            // GST 2.0 RSP valuation + Compensation-Cess override (Phase 9 slice 1). All optional; pre-validate to a
            // friendly message before the engine's EnsureValid backstop. Blank/off ⇒ byte-identical to a Phase-4/8
            // item (transaction-value basis, no cess override), ER-13.
            gstBlock.ValuationBasis = ValuationIsRsp ? GstValuationBasis.RetailSalePrice : GstValuationBasis.TransactionValue;

            if (!string.IsNullOrWhiteSpace(RetailSalePriceText))
            {
                if (!TryParseRate(RetailSalePriceText, out var rsp) || rsp < 0m)
                {
                    Message = "Retail Sale Price must be a number ≥ 0 (₹ per unit, to the paisa).";
                    return false;
                }
                var rspMoney = Money.FromRupees(rsp);
                if (!rspMoney.IsPaisaExact)
                {
                    Message = $"Retail Sale Price {rsp} must be to the paisa (2 decimal places).";
                    return false;
                }
                gstBlock.RetailSalePrice = rspMoney;
            }

            // A10 fix (finding #4): an RSP valuation basis has nothing to value against without a declared Retail
            // Sale Price — reject the "valuation is RSP" + blank-RSP combination up front (also enforced by
            // StockItemGstDetails.EnsureValid) instead of persisting an item that claims RSP valuation with no RSP.
            if (gstBlock.ValuationBasis == GstValuationBasis.RetailSalePrice && gstBlock.RetailSalePrice is null)
            {
                Message = "An RSP valuation basis needs a declared Retail Sale Price on the item.";
                return false;
            }

            gstBlock.CessApplicable = CessApplicable;
            if (CessApplicable && CessMode?.Mode is { } cessMode)
            {
                gstBlock.CessValuationMode = cessMode;
                switch (cessMode)
                {
                    case CessValuationMode.AdValorem:
                        if (!TryParseCessPercent(CessRatePercentText, out var cessBp)) return false;
                        gstBlock.CessRateBasisPoints = cessBp;
                        break;
                    case CessValuationMode.Specific:
                        if (!TryParseRate(CessPerUnitText, out var perUnit) || perUnit < 0m)
                        {
                            Message = "Specific cess needs a per-unit amount ≥ 0 (₹ per unit, to the paisa).";
                            return false;
                        }
                        var perUnitMoney = Money.FromRupees(perUnit);
                        if (!perUnitMoney.IsPaisaExact)
                        {
                            Message = $"Specific cess per-unit {perUnit} must be to the paisa (2 decimal places).";
                            return false;
                        }
                        gstBlock.CessPerUnit = perUnitMoney;
                        break;
                    case CessValuationMode.RetailSalePriceFactor:
                        if (!decimal.TryParse((CessRspFactorText ?? string.Empty).Trim(),
                                NumberStyles.Number, CultureInfo.InvariantCulture, out var factor) || factor < 0m)
                        {
                            Message = "RSP-factor cess needs a factor ≥ 0 (e.g. 0.32 for 0.32 × RSP).";
                            return false;
                        }
                        gstBlock.CessRspFactorMillis = (int)Math.Round(factor * 1000m, MidpointRounding.AwayFromZero);
                        if (gstBlock.RetailSalePrice is null)
                        {
                            Message = "An RSP-factor cess needs a declared Retail Sale Price on the item.";
                            return false;
                        }
                        break;
                }
            }

            // A10 fix (finding #3): a taxable item whose HSN attracts RSP-factor Compensation Cess but carries no
            // Retail Sale Price would persist cleanly (EnsureValid only demands an RSP for an EXPLICIT per-item
            // override, not for an HSN-INHERITED cess row) then fail fast at EVERY sale voucher (ResolveCess →
            // BuildCess refuses a silent ₹0). Pre-validate against the dated cess master and reject up front.
            // Skipped when the item declares its own explicit cess override (that override wins in ResolveCess, so
            // the HSN RSP-factor row is never consulted). Date-agnostic: any window counts. No-op for a company
            // with no RSP-factor cess rows (byte-identical when advanced-GST off, ER-13).
            var hasExplicitCessOverride = gstBlock.CessApplicable && gstBlock.CessValuationMode is not null;
            if (taxability == GstTaxability.Taxable && hsn is not null && gstBlock.RetailSalePrice is null
                && !hasExplicitCessOverride
                && (_company.Gst?.CessRates ?? Array.Empty<GstCessRate>())
                    .Any(r => r.HsnSac == hsn && r.ValuationMode == CessValuationMode.RetailSalePriceFactor))
            {
                Message = $"HSN {hsn} attracts RSP-factor Compensation Cess — a Retail Sale Price is required on the item.";
                return false;
            }
        }

        StockItem item;
        try
        {
            var service = new InventoryService(_company);
            item = service.CreateStockItem(name, SelectedGroup.Id, SelectedUnit.Id, categoryId, alias,
                valuation, hsn, isTaxableFlag, reorderLevel, minimumOrderQty);
            if (gstBlock is not null)
            {
                gstBlock.EnsureValid();  // backstop; already pre-validated above
                item.Gst = gstBlock;
            }

            // Batch switches (RQ-2) — captured only when the company flag is on; the three switches are
            // independent (Use-Expiry may be on without Track-Mfg, subtlety a). When the company flag is off the
            // switches stay false so an existing (non-batch) company is byte-identical (ER-13).
            if (ShowBatchSwitches)
            {
                item.MaintainInBatches = MaintainInBatches;
                item.TrackManufacturingDate = TrackManufacturingDate;
                item.UseExpiryDates = UseExpiryDates;
            }

            // BOM switch (RQ-10) — captured only when the F12 config is on. When off the flag stays false so an
            // existing (non-BOM) company is byte-identical (ER-13). A BOM created later also flips this on.
            if (ShowBomSwitch)
                item.SetComponents = SetComponents;

            // TCS Nature of Goods (Phase 7 slice 1) — captured only when TCS is enabled; null (no auto-TCS) for
            // every non-TCS company, so a non-TCS item is byte-identical (ER-13).
            if (TcsEnabled)
                item.TcsNatureOfGoodsId = SelectedTcsNature?.NatureId;

            if (wantsOpening && openingQty > 0m)
            {
                var batch = string.IsNullOrWhiteSpace(OpeningBatchLabel) ? null : OpeningBatchLabel.Trim();
                service.AddOpeningBalance(item.Id, OpeningGodown!.Id, openingQty, openingRate, batch);
            }

            _storage.Save(_company);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            Message = ex.Message;
            return false;
        }

        RefreshPickers();
        RefreshList();
        var openingNote = wantsOpening && openingQty > 0m
            ? $" with opening {openingQty:0.######} {SelectedUnit.Symbol}"
            : string.Empty;
        Message = $"Stock item '{name}' created under {SelectedGroup.Name}{openingNote}.";
        Name = string.Empty;
        Alias = string.Empty;
        HsnSacCode = string.Empty;
        IsTaxable = false;
        ReorderLevelText = string.Empty;
        MinimumOrderQtyText = string.Empty;
        OpeningQuantityText = string.Empty;
        OpeningRateText = string.Empty;
        OpeningBatchLabel = string.Empty;
        Taxability = Taxabilities.First();
        GstRate = GstRates.First();
        ValuationIsRsp = false;
        RetailSalePriceText = string.Empty;
        CessApplicable = false;
        CessMode = CessValuationModes.First();
        CessRatePercentText = string.Empty;
        CessPerUnitText = string.Empty;
        CessRspFactorText = string.Empty;
        SelectedTcsNature = TcsNatureChoices.Count > 0 ? TcsNatureChoices[0] : null;
        MaintainInBatches = false;
        TrackManufacturingDate = false;
        UseExpiryDates = false;
        SetComponents = false;
        _onChanged();
        return true;
    }

    // ---- parsing helpers ----

    private bool TryParseOptionalQuantity(string? text, string label, out decimal? value)
    {
        value = null;
        if (string.IsNullOrWhiteSpace(text)) return true;
        if (!TryParseQuantity(text, label, out var q))
            return false;               // Message set by TryParseQuantity
        if (q < 0m)
        {
            Message = $"{label} must be ≥ 0.";
            return false;
        }
        value = q;
        return true;
    }

    private bool TryParseQuantity(string? text, string label, out decimal value)
    {
        if (!decimal.TryParse((text ?? string.Empty).Trim(),
                NumberStyles.AllowDecimalPoint | NumberStyles.AllowThousands | NumberStyles.AllowLeadingSign,
                CultureInfo.InvariantCulture, out value))
        {
            Message = $"{label} must be a number.";
            return false;
        }
        if (!Quantities.IsWithinPrecision(value))
        {
            Message = $"{label} {value} must be to {Quantities.DecimalPlaces} decimal places.";
            return false;
        }
        return true;
    }

    private static bool TryParseRate(string? text, out decimal value)
        => decimal.TryParse((text ?? string.Empty).Trim(),
            NumberStyles.AllowDecimalPoint | NumberStyles.AllowThousands | NumberStyles.AllowLeadingSign,
            CultureInfo.InvariantCulture, out value);

    /// <summary>Parses an ad-valorem cess rate as a percent (e.g. "22" ⇒ 2200 bp); surfaces a friendly message on a
    /// bad/negative value. Phase 9 slice 1.</summary>
    private bool TryParseCessPercent(string? text, out int basisPoints)
    {
        basisPoints = 0;
        if (!decimal.TryParse((text ?? string.Empty).Trim(),
                NumberStyles.AllowDecimalPoint | NumberStyles.AllowThousands | NumberStyles.AllowLeadingSign,
                CultureInfo.InvariantCulture, out var percent) || percent < 0m)
        {
            Message = "Ad-valorem cess needs a rate ≥ 0 (percent, e.g. 22 for 22%).";
            return false;
        }
        basisPoints = (int)Math.Round(percent * 100m, MidpointRounding.AwayFromZero);
        return true;
    }

    // ---- refresh ----

    /// <summary>
    /// Rebuilds the group / category / unit / godown pickers from the company, preserving the current choices.
    /// <para>WI-1 — PUBLIC because an Alt+C "create on the fly" launched FROM this screen (Under → Stock Group,
    /// Base unit → Unit, Category → Stock Category) adds a record while this view model is alive; without a
    /// refresh the new record would not be in the list and selecting it back would silently do nothing.</para>
    /// </summary>
    public void RefreshPickers()
    {
        var groupId = SelectedGroup?.Id;
        Groups.Clear();
        foreach (var g in _company.StockGroups.OrderBy(g => g.Name, StringComparer.OrdinalIgnoreCase))
            Groups.Add(g);
        SelectedGroup = Groups.FirstOrDefault(g => g.Id == groupId) ?? Groups.FirstOrDefault();

        var catId = SelectedCategory?.Category?.Id;
        CategoryOptions.Clear();
        CategoryOptions.Add(new OptionalStockCategoryOption { Category = null, Display = "◦ (none)" });
        foreach (var c in _company.StockCategories.OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase))
            CategoryOptions.Add(new OptionalStockCategoryOption { Category = c, Display = c.Name });
        SelectedCategory = CategoryOptions.FirstOrDefault(o => o.Category?.Id == catId)
                           ?? CategoryOptions.FirstOrDefault();

        var unitId = SelectedUnit?.Id;
        Units.Clear();
        foreach (var u in _company.Units.OrderBy(u => u.Symbol, StringComparer.OrdinalIgnoreCase))
            Units.Add(u);
        SelectedUnit = Units.FirstOrDefault(u => u.Id == unitId) ?? Units.FirstOrDefault();

        var godownId = OpeningGodown?.Id;
        Godowns.Clear();
        foreach (var g in _company.Godowns.OrderByDescending(g => g.IsMainLocation)
                     .ThenBy(g => g.Name, StringComparer.OrdinalIgnoreCase))
            Godowns.Add(g);
        OpeningGodown = Godowns.FirstOrDefault(g => g.Id == godownId)
                        ?? Godowns.FirstOrDefault(g => g.IsMainLocation)
                        ?? Godowns.FirstOrDefault();

        OnPropertyChanged(nameof(CanCreate));
    }

    private void RefreshList()
    {
        Existing.Clear();
        var service = new InventoryService(_company);
        foreach (var item in _company.StockItems)
        {
            var group = _company.FindStockGroup(item.StockGroupId)?.Name ?? "—";
            var unit = _company.FindUnit(item.BaseUnitId)?.Symbol ?? "—";
            var opening = service.OpeningValueOf(item.Id);
            Existing.Add(new StockItemListRow
            {
                Name = item.Name,
                Under = group,
                Unit = unit,
                Valuation = ValuationLabel(item.ValuationMethod),
                OpeningValue = opening == Money.Zero
                    ? "—"
                    : "₹" + opening.Amount.ToString("#,##0.00", CultureInfo.InvariantCulture),
            });
        }
    }

    private static string ValuationLabel(StockValuationMethod method) => method switch
    {
        StockValuationMethod.AverageCost => "Average Cost",
        StockValuationMethod.Fifo => "FIFO",
        StockValuationMethod.Lifo => "LIFO",
        StockValuationMethod.StandardCost => "Standard Cost",
        StockValuationMethod.LastPurchaseCost => "Last Purchase Cost",
        StockValuationMethod.LastSaleCost => "Last Sale Cost",
        _ => method.ToString(),
    };
}
