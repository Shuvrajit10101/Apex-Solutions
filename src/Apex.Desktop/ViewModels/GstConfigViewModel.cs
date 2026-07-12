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

    /// <summary>
    /// The company feature flag <b>"Maintain Payroll"</b> (F11 Company Features; Phase 8 slice 1; RQ-1). The
    /// master gate for the whole Payroll module — the Payroll Masters section (Employee Category / Group /
    /// Employee, Payroll Unit, Attendance type) and, in later slices, the Attendance/Payroll voucher types and
    /// payroll reports are all hidden/inert when it is off. Applied to the live company by
    /// <see cref="OnPayrollEnabledChanged"/> through <see cref="PayrollService.EnablePayroll"/> /
    /// <see cref="PayrollService.DisablePayroll"/> and persisted; a company that never enables it is byte-identical
    /// and carries no payroll masters (ER-13). Turning it off never deletes payroll data — the UI simply hides.
    /// </summary>
    [ObservableProperty] private bool _payrollEnabled;

    /// <summary>
    /// The company feature flag <b>"Enable Payroll Statutory"</b> (F11 Company Features; Phase 8 slice 1; RQ-1) —
    /// surfaces the Company Payroll Statutory Details (PF/ESI/NPS/IT codes) captured in the later statutory slices.
    /// Only meaningful (and only shown) while <see cref="PayrollEnabled"/> is on; applied by
    /// <see cref="OnPayrollStatutoryEnabledChanged"/> and persisted. Defaults to <c>false</c> (ER-13).
    /// </summary>
    [ObservableProperty] private bool _payrollStatutoryEnabled;

    // ---- Provident Fund (Phase 8 slice 4; F11 Payroll Statutory → PF; RQ-9) --------------------------------
    // The establishment's EPFO enrolment facts the PF computation reads. Only meaningful (and only shown) while
    // PayrollStatutoryEnabled is on; enrolling sets Company.PfConfig via the engine, disabling clears it. Every
    // field is gated: a company that never enrols for PF is byte-identical to a pre-v33 company (ER-13).

    /// <summary>Whether the establishment is enrolled for Provident Fund (the "Enable Provident Fund" toggle;
    /// applied via <see cref="ApplyPf"/>). Non-<c>null</c> <see cref="Company.PfConfig"/> ⇔ enrolled.</summary>
    [ObservableProperty] private bool _pfEnabled;

    /// <summary>Whether the reduced <b>10%</b> EPF rate applies (a special establishment — &lt;20 employees / sick /
    /// jute-beedi-brick-coir-guar-gum). Off ⇒ the 12% default. Only the EPF rate varies; EPS 8.33% / EDLI 0.5% /
    /// admin 0.5% are fixed.</summary>
    [ObservableProperty] private bool _pfReducedRate;

    /// <summary>The EPFO <b>establishment / PF code</b> printed on the ECR and challan; optional.</summary>
    [ObservableProperty] private string _pfEstablishmentCode = string.Empty;

    /// <summary>Whether EPF wages are capped at the ₹15,000 statutory ceiling by default (the recommended default);
    /// a per-employee "contribute on higher wages" opt-in overrides it for that member.</summary>
    [ObservableProperty] private bool _pfCapWagesAtCeiling = true;

    /// <summary>The PF Enable/disable result message (kept separate from the GST/TDS/TCS messages).</summary>
    [ObservableProperty] private string? _pfMessage;

    /// <summary>True iff the PF configuration block should render — only while Payroll Statutory is on (PF lives
    /// under it). A payroll-off / statutory-off company never sees the PF fields (ER-13).</summary>
    public bool ShowPfConfig => PayrollStatutoryEnabled;

    /// <summary>The company GSTIN/UIN (validated on Enable); blank ⇒ unset.</summary>
    [ObservableProperty] private string _gstin = string.Empty;

    /// <summary>The chosen Home State/UT (supplier place-of-supply); required on Enable.</summary>
    [ObservableProperty] private IndianStateOption? _homeState;

    /// <summary>The chosen registration type (Regular is the working type in Phase 4).</summary>
    [ObservableProperty] private GstRegistrationTypeOption? _registrationType;

    /// <summary>The chosen GSTR-1 (and paired 3B) return periodicity.</summary>
    [ObservableProperty] private GstPeriodicityOption? _periodicity;

    [ObservableProperty] private string? _message;

    // ---- TDS / TCS (Phase 7 slice 1; F11 "Enable TDS" / "Enable TCS" + shared deductor identity) ----
    // A company files Form 26Q (TDS) and 27EQ (TCS) under the SAME TAN/responsible-person, so the deductor
    // identity fields below are shared and written to whichever of TdsConfig/TcsConfig is enabled. Every field is
    // gated: a company that enables neither is byte-for-byte unchanged (ER-13).

    /// <summary>Whether TDS is enabled for the company (the "Enable TDS" toggle; applied via <see cref="ApplyTds"/>).</summary>
    [ObservableProperty] private bool _tdsEnabled;

    /// <summary>Whether TCS is enabled for the company (the "Enable TCS" toggle; applied via <see cref="ApplyTcs"/>).</summary>
    [ObservableProperty] private bool _tcsEnabled;

    /// <summary>The deductor/collector TAN (validated on Enable via <see cref="Tan"/>); blank ⇒ unset.</summary>
    [ObservableProperty] private string _tan = string.Empty;

    /// <summary>The deductor/collector legal status (drives the 26Q/27EQ deductor block).</summary>
    [ObservableProperty] private DeductorTypeOption? _deductorType;

    /// <summary>The person responsible for deduction/collection; blank ⇒ unset.</summary>
    [ObservableProperty] private string _responsiblePersonName = string.Empty;

    /// <summary>The responsible person's PAN (validated on Enable when set); blank ⇒ unset.</summary>
    [ObservableProperty] private string _responsiblePersonPan = string.Empty;

    /// <summary>The responsible person's designation; blank ⇒ unset.</summary>
    [ObservableProperty] private string _responsiblePersonDesignation = string.Empty;

    /// <summary>The responsible person's address; blank ⇒ unset.</summary>
    [ObservableProperty] private string _responsiblePersonAddress = string.Empty;

    /// <summary>Whether surcharge applies to the deductor's computations (forward-compat seam for slice 2/5).</summary>
    [ObservableProperty] private bool _surchargeApplicable;

    /// <summary>Whether health &amp; education cess applies (forward-compat seam).</summary>
    [ObservableProperty] private bool _cessApplicable;

    /// <summary>The TDS Enable/disable result message (kept separate from the GST/TCS messages).</summary>
    [ObservableProperty] private string? _tdsMessage;

    /// <summary>The TCS Enable/disable result message.</summary>
    [ObservableProperty] private string? _tcsMessage;

    /// <summary>True iff the shared deductor-details block should render (either TDS or TCS is toggled on).</summary>
    public bool ShowDeductorDetails => TdsEnabled || TcsEnabled;

    partial void OnTdsEnabledChanged(bool value) => OnPropertyChanged(nameof(ShowDeductorDetails));
    partial void OnTcsEnabledChanged(bool value) => OnPropertyChanged(nameof(ShowDeductorDetails));

    /// <summary>The Home State/UT options (the official GST state-code list).</summary>
    public ObservableCollection<IndianStateOption> HomeStates { get; } = new();

    /// <summary>The registration-type options (Regular working; others stored but inert in Phase 4).</summary>
    public ObservableCollection<GstRegistrationTypeOption> RegistrationTypes { get; } = new();

    /// <summary>The return-periodicity options (Monthly / Quarterly).</summary>
    public ObservableCollection<GstPeriodicityOption> Periodicities { get; } = new();

    /// <summary>The tax ledgers created on Enable (Output/Input CGST/SGST/IGST + Round-Off), for confirmation.</summary>
    public ObservableCollection<GstTaxLedgerRow> TaxLedgers { get; } = new();

    /// <summary>The deductor/collector legal-status options (Company / Individual / HUF / Firm …).</summary>
    public ObservableCollection<DeductorTypeOption> DeductorTypes { get; } = new();

    /// <summary>The TDS/TCS payable ledgers auto-created on Enable (TDS Payable / TCS Payable), for confirmation.</summary>
    public ObservableCollection<GstTaxLedgerRow> TdsTcsLedgers { get; } = new();

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

        foreach (var opt in TdsTcsDisplay.DeductorTypeOptions())
            DeductorTypes.Add(opt);

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
        PayrollEnabled = _company.PayrollEnabled;
        PayrollStatutoryEnabled = _company.PayrollStatutoryEnabled;
        LoadPfFromCompany();
        Gstin = cfg?.Gstin ?? string.Empty;
        HomeState = HomeStates.FirstOrDefault(o => o.Code == cfg?.HomeStateCode);
        RegistrationType = RegistrationTypes.FirstOrDefault(o => o.Value == (cfg?.RegistrationType ?? GstRegistrationType.Regular))
                           ?? RegistrationTypes.First();
        Periodicity = Periodicities.FirstOrDefault(o => o.Value == (cfg?.Periodicity ?? GstReturnPeriodicity.Monthly))
                      ?? Periodicities.First();
        LoadTdsTcsFromCompany();
        RefreshTaxLedgers();
    }

    /// <summary>
    /// Seeds the TDS/TCS form from the company's current config. The deductor identity (TAN, type, responsible
    /// person, surcharge/cess) is shared, so it is read from whichever of <see cref="Company.Tds"/>/<see cref="Company.Tcs"/>
    /// carries it (TDS first). An off/never-set company loads defaults (blank TAN, Company type).
    /// </summary>
    private void LoadTdsTcsFromCompany()
    {
        var tds = _company.Tds;
        var tcs = _company.Tcs;
        TdsEnabled = _company.TdsEnabled;
        TcsEnabled = _company.TcsEnabled;

        Tan = tds?.Tan ?? tcs?.Tan ?? string.Empty;
        var dtype = tds?.DeductorType ?? tcs?.CollectorType ?? Apex.Ledger.Domain.DeductorType.Company;
        DeductorType = DeductorTypes.FirstOrDefault(o => o.Value == dtype) ?? DeductorTypes.First();
        ResponsiblePersonName = tds?.ResponsiblePersonName ?? tcs?.ResponsiblePersonName ?? string.Empty;
        ResponsiblePersonPan = tds?.ResponsiblePersonPan ?? tcs?.ResponsiblePersonPan ?? string.Empty;
        ResponsiblePersonDesignation = tds?.ResponsiblePersonDesignation ?? tcs?.ResponsiblePersonDesignation ?? string.Empty;
        ResponsiblePersonAddress = tds?.ResponsiblePersonAddress ?? tcs?.ResponsiblePersonAddress ?? string.Empty;
        SurchargeApplicable = tds?.SurchargeApplicable ?? tcs?.SurchargeApplicable ?? false;
        CessApplicable = tds?.CessApplicable ?? tcs?.CessApplicable ?? false;
        RefreshTdsTcsLedgers();
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
    /// Applies the "Maintain Payroll" F11 toggle to the live company the moment it changes (Phase 8 slice 1; RQ-1),
    /// through the engine's idempotent <see cref="PayrollService.EnablePayroll"/> /
    /// <see cref="PayrollService.DisablePayroll"/>, so the Payroll Masters section surfaces (or hides) immediately,
    /// and persists the company. Enabling preserves the current statutory sub-toggle; disabling also clears
    /// statutory (it is meaningless without Payroll) and reflects that in the UI. Errors are surfaced without
    /// crashing and the toggle reverts to the company's real state. Independent of GST.
    /// </summary>
    partial void OnPayrollEnabledChanged(bool value)
    {
        try
        {
            var service = new PayrollService(_company);
            if (value) service.EnablePayroll(enableStatutory: PayrollStatutoryEnabled);
            else service.DisablePayroll();
            _storage.Save(_company);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            Message = ex.Message;
            if (PayrollEnabled != _company.PayrollEnabled)
                PayrollEnabled = _company.PayrollEnabled;
            return;
        }
        // DisablePayroll turns statutory off too — keep the sub-toggle in sync with the persisted state.
        if (PayrollStatutoryEnabled != _company.PayrollStatutoryEnabled)
            PayrollStatutoryEnabled = _company.PayrollStatutoryEnabled;
        OnPropertyChanged(nameof(ShowPfConfig));
        _onChanged();
    }

    /// <summary>
    /// Applies the "Enable Payroll Statutory" F11 sub-toggle to the live company (Phase 8 slice 1; RQ-1) and
    /// persists. Only shown while <see cref="PayrollEnabled"/> is on; surfaces the Company Payroll Statutory
    /// Details in the later statutory slices. Errors are surfaced without crashing and the toggle reverts.
    /// </summary>
    partial void OnPayrollStatutoryEnabledChanged(bool value)
    {
        _company.PayrollStatutoryEnabled = value;
        try
        {
            _storage.Save(_company);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            Message = ex.Message;
            if (PayrollStatutoryEnabled != _company.PayrollStatutoryEnabled)
                PayrollStatutoryEnabled = _company.PayrollStatutoryEnabled;
            return;
        }
        OnPropertyChanged(nameof(ShowPfConfig)); // the PF block appears/hides with the statutory sub-toggle
        _onChanged();
    }

    /// <summary>Seeds the PF form from the company's current <see cref="Company.PfConfig"/> (defaults for a
    /// never-enrolled company: 12% rate, no code, cap-at-ceiling on).</summary>
    private void LoadPfFromCompany()
    {
        var pf = _company.PfConfig;
        PfEnabled = pf is not null;
        PfReducedRate = pf?.EpfRateBasisPoints == PfConfig.ReducedEpfRateBasisPoints;
        PfEstablishmentCode = pf?.EstablishmentCode ?? string.Empty;
        PfCapWagesAtCeiling = pf?.CapWagesAtCeiling ?? true;
    }

    /// <summary>
    /// Applies the "Enable Provident Fund" toggle (F11 → Payroll Statutory; Phase 8 slice 4; RQ-9), mirroring
    /// <see cref="Apply"/>/<see cref="ApplyTds"/>. On enable: enrol the establishment via the engine's idempotent
    /// <see cref="PayrollService.EnableProvidentFund"/> (EPF 12% / 10%, the establishment code and the cap flag) —
    /// which also turns Payroll Statutory on — and persist. On disable: clear <see cref="Company.PfConfig"/> and
    /// persist (per-employee PF details are retained, inert). Any domain error is surfaced to
    /// <see cref="PfMessage"/> without crashing, and the toggle reverts to the real company state.
    /// </summary>
    public bool ApplyPf()
    {
        PfMessage = null;

        if (!PfEnabled)
        {
            _company.PfConfig = null; // enrolment cleared; per-employee PF details retained (harmless, inert)
            if (!TrySave(m => PfMessage = m)) { RevertPfToggle(); return false; }
            PfMessage = "Provident Fund is now OFF for this company. Employee PF details are unchanged.";
            _onChanged();
            return true;
        }

        var rate = PfReducedRate ? PfConfig.ReducedEpfRateBasisPoints : PfConfig.DefaultEpfRateBasisPoints;
        var code = BlankToNull(PfEstablishmentCode);
        try
        {
            new PayrollService(_company).EnableProvidentFund(rate, code, PfCapWagesAtCeiling);
            _storage.Save(_company);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            PfMessage = ex.Message;
            RevertPfToggle();
            return false;
        }

        // EnableProvidentFund flips Payroll Statutory on — keep the sibling toggle + the PF block in sync.
        if (PayrollStatutoryEnabled != _company.PayrollStatutoryEnabled)
            PayrollStatutoryEnabled = _company.PayrollStatutoryEnabled;
        var rateLabel = PfReducedRate ? "10%" : "12%";
        PfMessage = $"Provident Fund enabled for {_company.Name} (EPF {rateLabel}; EPS 8.33% / EDLI 0.5% / admin 0.5%).";
        _onChanged();
        return true;
    }

    /// <summary>Reverts the <see cref="PfEnabled"/> toggle to reflect the company's real (unchanged) state.</summary>
    private void RevertPfToggle()
    {
        var real = _company.PfConfig is not null;
        if (PfEnabled != real) PfEnabled = real;
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

    // =========================================================== TDS / TCS (Phase 7 slice 1)

    /// <summary>
    /// Applies the "Enable TDS" toggle (F11; Phase 7 slice 1), mirroring <see cref="Apply"/>. On enable:
    /// pre-validate the TAN (required, structural) and the responsible-person PAN (when set), then call the
    /// engine's <see cref="TdsTcsService.EnableTds"/> (idempotent — seeds the Nature-of-Payment masters +
    /// auto-creates "TDS Payable" under Duties &amp; Taxes) and persist. On disable: clear
    /// <see cref="TdsConfig.Enabled"/> and persist (masters retained, inert). Any domain error is surfaced to
    /// <see cref="TdsMessage"/> without crashing, and the toggle reverts to the real company state.
    /// </summary>
    public bool ApplyTds()
    {
        TdsMessage = null;

        if (!TdsEnabled)
        {
            if (_company.Tds is { } existing)
            {
                existing.Enabled = false;
                if (!TrySave(m => TdsMessage = m)) return false;
            }
            RefreshTdsTcsLedgers();
            TdsMessage = "TDS is now OFF for this company. Existing masters are unchanged.";
            _onChanged();
            return true;
        }

        var tan = Apex.Ledger.Domain.Tan.Normalize(Tan);
        if (string.IsNullOrEmpty(tan))
        {
            TdsMessage = "A TAN is required to enable TDS (10 chars, e.g. MUMA12345B).";
            RevertTdsToggle();
            return false;
        }
        if (!Apex.Ledger.Domain.Tan.IsValid(tan))
        {
            TdsMessage = $"'{tan}' is not a valid TAN (4 letters + 5 digits + 1 letter, e.g. MUMA12345B).";
            RevertTdsToggle();
            return false;
        }
        var pan = NormalizePanOrNull(ResponsiblePersonPan);
        if (pan is not null && !Pan.IsValid(pan))
        {
            TdsMessage = $"'{pan}' is not a valid PAN (5 letters + 4 digits + 1 letter, e.g. AAPFU0939F).";
            RevertTdsToggle();
            return false;
        }

        var config = _company.Tds ?? new TdsConfig();
        WriteDeductorIdentity(config, tan, pan);

        try
        {
            new TdsTcsService(_company).EnableTds(config);
            SyncSharedIdentityToTcs(tan, pan);   // keep 27EQ under the same TAN if TCS is already on
            _storage.Save(_company);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            TdsMessage = ex.Message;
            RevertTdsToggle();
            return false;
        }

        RefreshTdsTcsLedgers();
        TdsMessage = $"TDS enabled for {_company.Name}. {_company.NaturesOfPayment.Count} Nature-of-Payment "
                     + "masters seeded; TDS Payable ledger ready under Duties & Taxes.";
        _onChanged();
        return true;
    }

    /// <summary>
    /// Applies the "Enable TCS" toggle (F11; Phase 7 slice 1), the TCS mirror of <see cref="ApplyTds"/>: on
    /// enable, pre-validate TAN + PAN, call <see cref="TdsTcsService.EnableTcs"/> (seeds Nature-of-Goods §206C
    /// masters + auto-creates "TCS Payable") and persist; on disable, clear <see cref="TcsConfig.Enabled"/>.
    /// </summary>
    public bool ApplyTcs()
    {
        TcsMessage = null;

        if (!TcsEnabled)
        {
            if (_company.Tcs is { } existing)
            {
                existing.Enabled = false;
                if (!TrySave(m => TcsMessage = m)) return false;
            }
            RefreshTdsTcsLedgers();
            TcsMessage = "TCS is now OFF for this company. Existing masters are unchanged.";
            _onChanged();
            return true;
        }

        var tan = Apex.Ledger.Domain.Tan.Normalize(Tan);
        if (string.IsNullOrEmpty(tan))
        {
            TcsMessage = "A TAN is required to enable TCS (a collector uses the same TAN as a deductor).";
            RevertTcsToggle();
            return false;
        }
        if (!Apex.Ledger.Domain.Tan.IsValid(tan))
        {
            TcsMessage = $"'{tan}' is not a valid TAN (4 letters + 5 digits + 1 letter, e.g. MUMA12345B).";
            RevertTcsToggle();
            return false;
        }
        var pan = NormalizePanOrNull(ResponsiblePersonPan);
        if (pan is not null && !Pan.IsValid(pan))
        {
            TcsMessage = $"'{pan}' is not a valid PAN (5 letters + 4 digits + 1 letter, e.g. AAPFU0939F).";
            RevertTcsToggle();
            return false;
        }

        var config = _company.Tcs ?? new TcsConfig();
        WriteCollectorIdentity(config, tan, pan);

        try
        {
            new TdsTcsService(_company).EnableTcs(config);
            SyncSharedIdentityToTds(tan, pan);   // keep 26Q under the same TAN if TDS is already on
            _storage.Save(_company);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            TcsMessage = ex.Message;
            RevertTcsToggle();
            return false;
        }

        RefreshTdsTcsLedgers();
        TcsMessage = $"TCS enabled for {_company.Name}. {_company.NaturesOfGoods.Count} Nature-of-Goods (§206C) "
                     + "masters seeded; TCS Payable ledger ready under Duties & Taxes.";
        _onChanged();
        return true;
    }

    /// <summary>
    /// Commits the whole <b>Statutory Configuration</b> (F11) page from a single keyboard accept (Ctrl+A / Enter,
    /// both routed through <c>ActivateSelected</c>), mirroring Tally where F11 features are accepted via Ctrl+A/Enter.
    /// Applies the GST config (as before), then commits the "Enable TDS" / "Enable TCS" toggles whenever they differ
    /// from the company's persisted state — so a keyboard-driven user who toggles Enable TDS/TCS gets it applied
    /// without reaching for the mouse, matching the auto-applying sibling feature toggles on the same panel. An
    /// unchanged toggle is left untouched (no spurious re-seed, no message), so a neither-enabled company stays
    /// byte-identical (ER-13). Toggling on still reveals the TAN/deductor block first; the accept commits it (and
    /// <see cref="ApplyTds"/>/<see cref="ApplyTcs"/> revert the toggle on a bad TAN/PAN, exactly as the button does).
    /// </summary>
    public void AcceptStatutoryConfig()
    {
        Apply();
        if (TdsEnabled != _company.TdsEnabled)
            ApplyTds();
        if (TcsEnabled != _company.TcsEnabled)
            ApplyTcs();
        // Commit the PF enrolment whenever the toggle differs from the persisted state (or PF is on, to persist
        // edited rate/code/cap), mirroring the auto-applying sibling feature toggles on the same panel.
        if (ShowPfConfig && (PfEnabled != (_company.PfConfig is not null) || PfEnabled))
            ApplyPf();
    }

    /// <summary>Persists the company, surfacing a domain error via <paramref name="setMessage"/>; false on failure.</summary>
    private bool TrySave(Action<string> setMessage)
    {
        try
        {
            _storage.Save(_company);
            return true;
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            setMessage(ex.Message);
            return false;
        }
    }

    /// <summary>Writes the shared deductor identity from the form into a <see cref="TdsConfig"/>.</summary>
    private void WriteDeductorIdentity(TdsConfig config, string tan, string? pan)
    {
        config.Tan = tan;
        config.DeductorType = (DeductorType ?? DeductorTypes.First()).Value;
        config.ResponsiblePersonName = BlankToNull(ResponsiblePersonName);
        config.ResponsiblePersonPan = pan;
        config.ResponsiblePersonDesignation = BlankToNull(ResponsiblePersonDesignation);
        config.ResponsiblePersonAddress = BlankToNull(ResponsiblePersonAddress);
        config.SurchargeApplicable = SurchargeApplicable;
        config.CessApplicable = CessApplicable;
    }

    /// <summary>Writes the shared collector identity from the form into a <see cref="TcsConfig"/>.</summary>
    private void WriteCollectorIdentity(TcsConfig config, string tan, string? pan)
    {
        config.Tan = tan;
        config.CollectorType = (DeductorType ?? DeductorTypes.First()).Value;
        config.ResponsiblePersonName = BlankToNull(ResponsiblePersonName);
        config.ResponsiblePersonPan = pan;
        config.ResponsiblePersonDesignation = BlankToNull(ResponsiblePersonDesignation);
        config.ResponsiblePersonAddress = BlankToNull(ResponsiblePersonAddress);
        config.SurchargeApplicable = SurchargeApplicable;
        config.CessApplicable = CessApplicable;
    }

    /// <summary>If TCS is already enabled, mirror the just-saved TDS deductor identity onto it (same TAN).</summary>
    private void SyncSharedIdentityToTcs(string tan, string? pan)
    {
        if (_company.Tcs is { } tcs) WriteCollectorIdentity(tcs, tan, pan);
    }

    /// <summary>If TDS is already enabled, mirror the just-saved TCS collector identity onto it (same TAN).</summary>
    private void SyncSharedIdentityToTds(string tan, string? pan)
    {
        if (_company.Tds is { } tds) WriteDeductorIdentity(tds, tan, pan);
    }

    private void RevertTdsToggle()
    {
        if (TdsEnabled != _company.TdsEnabled) TdsEnabled = _company.TdsEnabled;
    }

    private void RevertTcsToggle()
    {
        if (TcsEnabled != _company.TcsEnabled) TcsEnabled = _company.TcsEnabled;
    }

    private static string? BlankToNull(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    private static string? NormalizePanOrNull(string? pan)
    {
        var p = Pan.Normalize(pan ?? string.Empty);
        return string.IsNullOrEmpty(p) ? null : p;
    }

    /// <summary>Rebuilds the TDS/TCS payable-ledger confirmation list (ledgers tagged by classification).</summary>
    private void RefreshTdsTcsLedgers()
    {
        TdsTcsLedgers.Clear();
        foreach (var l in _company.Ledgers
                     .Where(l => l.TdsTcsClassification is not null)
                     .OrderBy(l => l.Name, StringComparer.OrdinalIgnoreCase))
            TdsTcsLedgers.Add(new GstTaxLedgerRow
            {
                Name = l.Name,
                Under = _company.FindGroup(l.GroupId)?.Name ?? "—",
            });
    }
}
