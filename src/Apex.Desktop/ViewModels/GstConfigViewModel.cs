using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Apex.Ledger.Domain;
using Apex.Ledger.Services;
using Apex.Desktop.Services;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Apex.Desktop.ViewModels;

/// <summary>A Home State/UT picker option (the 2-digit GST code + the State/UT name).</summary>
public sealed class IndianStateOption
{
    public IndianState State { get; init; } = null!;
    public string Display => $"{State.Code} — {State.Name}";
    public string Code => State.Code;
}

/// <summary>A GST registration-type picker option (label + the enum value).</summary>
public sealed class GstRegistrationTypeOption
{
    public GstRegistrationType Value { get; init; }
    public string Display { get; init; } = string.Empty;
}

/// <summary>A GST return-periodicity picker option (label + the enum value).</summary>
public sealed class GstPeriodicityOption
{
    public GstReturnPeriodicity Value { get; init; }
    public string Display { get; init; } = string.Empty;
}

/// <summary>A created GST tax-ledger row shown after Enable (the confirmation list).</summary>
public sealed class GstTaxLedgerRow
{
    public string Name { get; init; } = string.Empty;
    public string Under { get; init; } = string.Empty;
}

/// <summary>
/// The company-level GST configuration screen ("F11 Features → GST"; Statutory → GST; catalog §12; phase4
/// slice 4c). Toggles GST on/off for the current company and, when enabling, captures the GSTIN (validated
/// via <see cref="Gstin"/>), the Home State/UT (auto-filled from the GSTIN's first two digits), the
/// registration type (Regular in Phase 4) and the return periodicity, then calls
/// <see cref="GstService.EnableGst"/> — which seeds the 0/5/18/40 rate slabs and auto-creates the six
/// Output/Input tax ledgers (+ Round-Off) under Duties &amp; Taxes — and persists the company via
/// <see cref="CompanyStorage.Save"/>.
///
/// <para>Pre-validates BEFORE touching the engine: a valid GSTIN (Luhn-mod-36) and a Home State are required
/// to enable; a bad value is surfaced to <see cref="Message"/> and the company stays unchanged (the engine's
/// own validators are the backstop). Toggling off clears <see cref="GstConfig.Enabled"/> and persists; the
/// already-seeded tax ledgers/slabs are left in place (harmless — they simply go unused) so re-enabling never
/// duplicates them.</para>
///
/// <para>MVVM boundary: domain + persistence only, no Avalonia types, so it is headlessly unit-testable.</para>
/// </summary>
public sealed partial class GstConfigViewModel : ViewModelBase
{
    private readonly Company _company;
    private readonly CompanyStorage _storage;
    private readonly Action _onChanged;

    /// <summary>Whether GST is enabled for the company (the "Enable GST" toggle).</summary>
    [ObservableProperty] private bool _gstEnabled;

    /// <summary>
    /// The company feature flag <b>"Maintain Batch-wise details"</b> (F11 Company Features; Phase 6 Cluster 1;
    /// requirements RQ-2/RQ-52). The master gate for the whole batch/expiry feature — the per-item batch
    /// switches, the Batch master, the batch-allocation sub-screen and the batch reports are all hidden/inert
    /// when it is off. Applied to the live company by <see cref="ApplyBatchFeature"/> (and by
    /// <see cref="Apply"/>) and persisted; turning it off deletes no batch data (harmless, the UI simply hides).
    /// </summary>
    [ObservableProperty] private bool _maintainBatchwiseDetails;

    /// <summary>
    /// The F12-configuration flag <b>"Set Components (BOM)"</b> (Phase 6 Cluster 2; requirements
    /// RQ-9/RQ-10/RQ-52). The master gate for the whole Bill-of-Materials / Manufacturing feature — the per-item
    /// "Set Components (BOM)" switch, the BOM master, and the Manufacturing-Journal voucher are all hidden/inert
    /// when it is off. Applied to the live company the moment it changes and persisted; turning it off deletes no
    /// BOM data (harmless, the UI simply hides).
    /// </summary>
    [ObservableProperty] private bool _setComponentsBom;

    /// <summary>
    /// The F12-configuration flag <b>"Define type of component for BOM"</b> (Phase 6 Cluster 2; requirement
    /// RQ-10). When on, a BOM line may be typed as a By-Product / Co-Product / Scrap carve-out (the type picker
    /// is surfaced); only meaningful while <see cref="SetComponentsBom"/> is on.
    /// </summary>
    [ObservableProperty] private bool _defineBomComponentType;

    /// <summary>
    /// The company feature flag <b>"Enable multiple Price Levels"</b> (F11 Company Features → Inventory; Phase 6
    /// slice 5; RQ-26/RQ-52). The master gate for the whole Price-Levels/Price-Lists feature — the Price Level
    /// master, the Price List master, the party-default-level picker, the Sales Price-Level header + discount
    /// column and the Price List report are all hidden/inert when it is off (ER-13). A pure user toggle (it
    /// cannot be inferred from data — a company may enable it before defining any level), applied to the live
    /// company by <see cref="OnEnableMultiplePriceLevelsChanged"/> and persisted; turning it off deletes no
    /// price data (harmless, the UI simply hides).
    /// </summary>
    [ObservableProperty] private bool _enableMultiplePriceLevels;

    /// <summary>
    /// The company feature flag <b>"Enable Job Order Processing"</b> (F11 Company Features; Phase 6 slice 8;
    /// RQ-45/RQ-52). The master gate for the whole Job-Work feature. Turning it <b>on</b> also activates the four
    /// seeded-but-inactive Job-Work voucher types (Job Work In/Out Order, Material In/Out) and stamps their
    /// per-type flags ("Use for Job Work" on both Material types, "Allow Consumption" on Material In) — the one
    /// side-effect that is more than a plain flag set, driven through <see cref="JobWorkService.SetEnabled"/>.
    /// When off the four types re-hide and every non-job-work screen is byte-identical (ER-13). A pure user
    /// toggle (it cannot be inferred from data — a company may enable it before entering any order), applied to
    /// the live company by <see cref="OnEnableJobOrderProcessingChanged"/> and persisted.
    /// </summary>
    [ObservableProperty] private bool _enableJobOrderProcessing;

    /// <summary>The company GSTIN/UIN (validated on Enable); blank ⇒ unset.</summary>
    [ObservableProperty] private string _gstin = string.Empty;

    /// <summary>The chosen Home State/UT (supplier place-of-supply); required on Enable.</summary>
    [ObservableProperty] private IndianStateOption? _homeState;

    /// <summary>The chosen registration type (Regular is the working type in Phase 4).</summary>
    [ObservableProperty] private GstRegistrationTypeOption? _registrationType;

    /// <summary>The chosen GSTR-1 (and paired 3B) return periodicity.</summary>
    [ObservableProperty] private GstPeriodicityOption? _periodicity;

    [ObservableProperty] private string? _message;

    /// <summary>The Home State/UT options (the official GST state-code list).</summary>
    public ObservableCollection<IndianStateOption> HomeStates { get; } = new();

    /// <summary>The registration-type options (Regular working; others stored but inert in Phase 4).</summary>
    public ObservableCollection<GstRegistrationTypeOption> RegistrationTypes { get; } = new();

    /// <summary>The return-periodicity options (Monthly / Quarterly).</summary>
    public ObservableCollection<GstPeriodicityOption> Periodicities { get; } = new();

    /// <summary>The tax ledgers created on Enable (Output/Input CGST/SGST/IGST + Round-Off), for confirmation.</summary>
    public ObservableCollection<GstTaxLedgerRow> TaxLedgers { get; } = new();

    public GstConfigViewModel(Company company, CompanyStorage storage, Action onChanged)
    {
        _company = company ?? throw new ArgumentNullException(nameof(company));
        _storage = storage ?? throw new ArgumentNullException(nameof(storage));
        _onChanged = onChanged ?? throw new ArgumentNullException(nameof(onChanged));

        foreach (var s in IndianState.All)
            HomeStates.Add(new IndianStateOption { State = s });

        RegistrationTypes.Add(new GstRegistrationTypeOption { Value = GstRegistrationType.Regular, Display = "Regular" });
        RegistrationTypes.Add(new GstRegistrationTypeOption { Value = GstRegistrationType.Composition, Display = "Composition" });
        RegistrationTypes.Add(new GstRegistrationTypeOption { Value = GstRegistrationType.Unregistered, Display = "Unregistered" });
        RegistrationTypes.Add(new GstRegistrationTypeOption { Value = GstRegistrationType.Consumer, Display = "Consumer" });

        Periodicities.Add(new GstPeriodicityOption { Value = GstReturnPeriodicity.Monthly, Display = "Monthly" });
        Periodicities.Add(new GstPeriodicityOption { Value = GstReturnPeriodicity.Quarterly, Display = "Quarterly (QRMP)" });

        LoadFromCompany();
    }

    /// <summary>Seeds the form from the company's current GST config (defaults for an off/never-set company).</summary>
    private void LoadFromCompany()
    {
        var cfg = _company.Gst;
        GstEnabled = cfg is { Enabled: true };
        MaintainBatchwiseDetails = _company.MaintainBatchwiseDetails;
        SetComponentsBom = _company.SetComponentsBom;
        DefineBomComponentType = _company.DefineBomComponentType;
        EnableMultiplePriceLevels = _company.EnableMultiplePriceLevels;
        EnableJobOrderProcessing = _company.EnableJobOrderProcessing;
        Gstin = cfg?.Gstin ?? string.Empty;
        HomeState = HomeStates.FirstOrDefault(o => o.Code == cfg?.HomeStateCode);
        RegistrationType = RegistrationTypes.FirstOrDefault(o => o.Value == (cfg?.RegistrationType ?? GstRegistrationType.Regular))
                           ?? RegistrationTypes.First();
        Periodicity = Periodicities.FirstOrDefault(o => o.Value == (cfg?.Periodicity ?? GstReturnPeriodicity.Monthly))
                      ?? Periodicities.First();
        RefreshTaxLedgers();
    }

    /// <summary>
    /// When the GSTIN's first two digits are a valid state code and no Home State is chosen yet (or it
    /// disagrees), auto-fill/keep the Home State in sync with the GSTIN — the supplier state is embedded in
    /// the GSTIN, so this saves a redundant second entry (RQ-2).
    /// </summary>
    partial void OnGstinChanged(string value)
    {
        var normalized = Domain_Normalize(value);
        if (normalized.Length >= 2)
        {
            var code = normalized.Substring(0, 2);
            var match = HomeStates.FirstOrDefault(o => o.Code == code);
            if (match is not null && !ReferenceEquals(HomeState, match))
                HomeState = match;
        }
    }

    private static string Domain_Normalize(string? gstin) => (gstin ?? string.Empty).Trim().ToUpperInvariant();

    /// <summary>
    /// Applies the "Maintain Batch-wise details" toggle to the live company the moment it changes (RQ-52), so the
    /// per-item batch switches / Batch master / batch reports surface (or hide) immediately, and persists the
    /// company. Errors are surfaced without crashing and the toggle reverts to the company's real state. Kept
    /// independent of the GST <see cref="Apply"/> so flipping the batch feature does not require enabling GST.
    /// </summary>
    partial void OnMaintainBatchwiseDetailsChanged(bool value)
    {
        _company.MaintainBatchwiseDetails = value;
        try
        {
            _storage.Save(_company);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            Message = ex.Message;
            // Reflect the company's real (persisted) state on failure.
            if (MaintainBatchwiseDetails != _company.MaintainBatchwiseDetails)
                MaintainBatchwiseDetails = _company.MaintainBatchwiseDetails;
            return;
        }
        _onChanged();
    }

    /// <summary>
    /// Applies the "Set Components (BOM)" F12 toggle to the live company the moment it changes (RQ-52), so the
    /// per-item BOM switch / BOM master / Manufacturing-Journal voucher surface (or hide) immediately, and
    /// persists the company. Errors are surfaced without crashing and the toggle reverts to the company's real
    /// state. Independent of GST — flipping the BOM feature does not require enabling GST.
    /// </summary>
    partial void OnSetComponentsBomChanged(bool value)
    {
        _company.SetComponentsBom = value;
        try
        {
            _storage.Save(_company);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            Message = ex.Message;
            if (SetComponentsBom != _company.SetComponentsBom)
                SetComponentsBom = _company.SetComponentsBom;
            return;
        }
        _onChanged();
    }

    /// <summary>
    /// Applies the "Enable multiple Price Levels" F11 toggle to the live company the moment it changes (RQ-26/RQ-52),
    /// so the Price Level / Price List masters, the party-default-level picker, the Sales Price-Level header + discount
    /// column and the Price List report surface (or hide) immediately, and persists the company. Errors are surfaced
    /// without crashing and the toggle reverts to the company's real state. Independent of GST.
    /// </summary>
    partial void OnEnableMultiplePriceLevelsChanged(bool value)
    {
        _company.EnableMultiplePriceLevels = value;
        try
        {
            _storage.Save(_company);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            Message = ex.Message;
            if (EnableMultiplePriceLevels != _company.EnableMultiplePriceLevels)
                EnableMultiplePriceLevels = _company.EnableMultiplePriceLevels;
            return;
        }
        _onChanged();
    }

    /// <summary>
    /// Applies the "Enable Job Order Processing" F11 toggle to the live company the moment it changes (RQ-45/RQ-52).
    /// Beyond persisting the flag, turning it on activates the four seeded Job-Work voucher types and stamps their
    /// per-type flags (via <see cref="JobWorkService.SetEnabled"/>), so the Job Work In/Out Order + Material In/Out
    /// screens and the four registers surface (or hide) immediately. Errors are surfaced without crashing and the
    /// toggle reverts to the company's real state. Independent of GST.
    /// </summary>
    partial void OnEnableJobOrderProcessingChanged(bool value)
    {
        try
        {
            new JobWorkService(_company).SetEnabled(value);
            _storage.Save(_company);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            Message = ex.Message;
            if (EnableJobOrderProcessing != _company.EnableJobOrderProcessing)
                EnableJobOrderProcessing = _company.EnableJobOrderProcessing;
            return;
        }
        _onChanged();
    }

    /// <summary>
    /// Applies the "Define type of component for BOM" F12 toggle to the live company (RQ-10) and persists, so the
    /// By-Product/Co-Product/Scrap line-type picker on the BOM master surfaces (or hides) immediately.
    /// </summary>
    partial void OnDefineBomComponentTypeChanged(bool value)
    {
        _company.DefineBomComponentType = value;
        try
        {
            _storage.Save(_company);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            Message = ex.Message;
            if (DefineBomComponentType != _company.DefineBomComponentType)
                DefineBomComponentType = _company.DefineBomComponentType;
            return;
        }
        _onChanged();
    }

    /// <summary>
    /// Applies the toggle: on Enable, pre-validate the GSTIN + Home State, then call the engine's
    /// <see cref="GstService.EnableGst"/> (idempotent) and persist; on disable, clear
    /// <see cref="GstConfig.Enabled"/> and persist. Any domain error is surfaced to <see cref="Message"/>
    /// without crashing the UI, and the toggle reverts to reflect the real (unchanged) company state.
    /// </summary>
    public bool Apply()
    {
        Message = null;

        if (!GstEnabled)
        {
            // Turn GST off: keep the (already-seeded) config/tax ledgers but mark it disabled, and persist.
            if (_company.Gst is { } existing)
            {
                existing.Enabled = false;
                try
                {
                    _storage.Save(_company);
                }
                catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
                {
                    Message = ex.Message;
                    return false;
                }
            }
            RefreshTaxLedgers();
            Message = "GST is now OFF for this company. Existing masters are unchanged.";
            _onChanged();
            return true;
        }

        // Enabling — pre-validate BEFORE the engine so a bad value is a friendly message, not a crash.
        var gstin = Domain_Normalize(Gstin);
        var gstinOrNull = string.IsNullOrEmpty(gstin) ? null : gstin;

        if (gstinOrNull is null)
        {
            Message = "A GSTIN is required to enable GST (Regular registration).";
            RevertToggle();
            return false;
        }
        if (!Apex.Ledger.Domain.Gstin.IsValid(gstinOrNull))
        {
            Message = $"'{gstinOrNull}' is not a valid GSTIN (15 chars = state code + PAN + entity + 'Z' + checksum).";
            RevertToggle();
            return false;
        }
        if (HomeState is null)
        {
            Message = "Pick the Home State/UT (the supplier place of supply) to enable GST.";
            RevertToggle();
            return false;
        }

        var config = _company.Gst ?? new GstConfig();
        config.Gstin = gstinOrNull;
        config.HomeStateCode = HomeState.Code;
        config.RegistrationType = (RegistrationType ?? RegistrationTypes.First()).Value;
        config.Periodicity = (Periodicity ?? Periodicities.First()).Value;

        try
        {
            var service = new GstService(_company);
            service.EnableGst(config);           // idempotent: seeds slabs + auto-creates the 6 tax ledgers
            _storage.Save(_company);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            Message = ex.Message;
            RevertToggle();
            return false;
        }

        RefreshTaxLedgers();
        Message = $"GST enabled for {_company.Name}. {TaxLedgers.Count} tax ledgers ready; slabs 0/5/18/40 seeded.";
        _onChanged();
        return true;
    }

    /// <summary>Reverts the <see cref="GstEnabled"/> toggle to reflect the company's real (unchanged) state.</summary>
    private void RevertToggle()
    {
        var real = _company.GstEnabled;
        if (GstEnabled != real) GstEnabled = real;
    }

    /// <summary>Rebuilds the created-tax-ledgers confirmation list from the company's Duties &amp; Taxes ledgers.</summary>
    private void RefreshTaxLedgers()
    {
        TaxLedgers.Clear();
        if (_company.Gst is null) return;

        // The six GST tax ledgers (tagged by classification) + the Round-Off ledger.
        var rows = new List<GstTaxLedgerRow>();
        foreach (var l in _company.Ledgers.Where(l => l.GstClassification is not null))
        {
            var under = _company.FindGroup(l.GroupId)?.Name ?? "—";
            rows.Add(new GstTaxLedgerRow { Name = l.Name, Under = under });
        }
        if (_company.FindLedgerByName(GstService.RoundOffLedgerName) is { } ro)
            rows.Add(new GstTaxLedgerRow { Name = ro.Name, Under = _company.FindGroup(ro.GroupId)?.Name ?? "—" });

        foreach (var r in rows.OrderBy(r => r.Name, StringComparer.OrdinalIgnoreCase))
            TaxLedgers.Add(r);
    }
}
