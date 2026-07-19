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

/// <summary>A Professional-Tax state picker option (Phase 8 slice 6): the 2-digit GST state code the seeded slab table
/// belongs to (<c>null</c> = "None", no PT levied) + the display label.</summary>
public sealed class PtStateOption
{
    /// <summary>The 2-digit GST state code (e.g. "27" Maharashtra); <c>null</c> for the "None" option.</summary>
    public string? Code { get; init; }
    public string Display { get; init; } = string.Empty;
}

/// <summary>One band row of the seeded Professional-Tax slab table for the active state (Phase 8 slice 6; read-only
/// view): who it applies to (all / men / women), the monthly PT-wage range, the flat monthly PT and any February
/// over-charge. All figures whole rupees (PT carries no paisa).</summary>
public sealed class PtSlabRow
{
    public string AppliesTo { get; init; } = string.Empty;
    public string FromText { get; init; } = string.Empty;
    public string ToText { get; init; } = string.Empty;
    public string MonthlyText { get; init; } = string.Empty;
    public string FebText { get; init; } = string.Empty;
}

/// <summary>A gratuity provision-population picker option (Phase 8 slice 9): which employees a provision run accrues
/// for — all active (the recommended default, liability builds pre-vesting) or vested-only (≥ 5 years).</summary>
public sealed class GratuityPopulationOption
{
    public GratuityProvisionPopulation Value { get; init; }
    public string Display { get; init; } = string.Empty;
}

/// <summary>A composition sub-type picker option (Phase 9 slice 3; RQ-4): the §10 / Rule 7 dealer kind that drives the
/// tax-on-turnover rate + turnover base (Manufacturer / Trader / Restaurant / §10(2A) Service Provider) + its label.</summary>
public sealed class CompositionSubTypeOption
{
    public CompositionSubType Value { get; init; }
    public string Display { get; init; } = string.Empty;
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

    // ---- Employees' State Insurance (Phase 8 slice 5; F11 Payroll Statutory → ESI; RQ-10) -------------------
    // The establishment's ESIC enrolment facts the ESI computation reads. Only meaningful (and only shown) while
    // PayrollStatutoryEnabled is on; enrolling sets Company.EsiConfig via the engine, disabling clears it. Every
    // field is gated: a company that never enrols for ESI is byte-identical to a pre-v34 company (ER-13).

    /// <summary>Whether the establishment is enrolled for Employees' State Insurance (the "Enable ESI" toggle;
    /// applied via <see cref="ApplyEsi"/>). Non-<c>null</c> <see cref="Company.EsiConfig"/> ⇔ enrolled.</summary>
    [ObservableProperty] private bool _esiEnabled;

    /// <summary>The ESIC <b>establishment / employer code</b> (17 digits) printed on the challan and monthly file;
    /// optional (may be captured later). Distinct from the per-employee 10-digit IP number.</summary>
    [ObservableProperty] private string _esiEmployerCode = string.Empty;

    /// <summary>The ESI Enable/disable result message (kept separate from the GST/TDS/TCS/PF messages).</summary>
    [ObservableProperty] private string? _esiMessage;

    /// <summary>True iff the ESI configuration block should render — only while Payroll Statutory is on (ESI lives
    /// under it). A payroll-off / statutory-off company never sees the ESI fields (ER-13).</summary>
    public bool ShowEsiConfig => PayrollStatutoryEnabled;

    // ---- Professional Tax (Phase 8 slice 6; F11 Payroll Statutory → PT; RQ-11) ------------------------------
    // A state slab deduction on the monthly PT-wages. Only meaningful (and only shown) while PayrollStatutoryEnabled
    // is on; enrolling sets Company.PtConfig via the engine (seeding the editable state slab tables), disabling clears
    // it. Every field is gated: a company that never enrols for PT is byte-identical to a pre-v35 company (ER-13).

    /// <summary>Whether the establishment is enrolled for Professional Tax (the "Enable Professional Tax" toggle;
    /// applied via <see cref="ApplyPt"/>). Non-<c>null</c> <see cref="Company.PtConfig"/> ⇔ enrolled.</summary>
    [ObservableProperty] private bool _ptEnabled;

    /// <summary>The PT <b>enrolment / registration number</b> (the PTEC/PTRC number printed on the challan); optional.</summary>
    [ObservableProperty] private string _ptRegistrationNumber = string.Empty;

    /// <summary>The active PT state (whose seeded slab table drives the deduction); "None" ⇒ no PT levied.</summary>
    [ObservableProperty] private PtStateOption? _selectedPtState;

    /// <summary>The PT Enable/disable result message (kept separate from the GST/TDS/TCS/PF/ESI messages).</summary>
    [ObservableProperty] private string? _ptMessage;

    /// <summary>True iff the PT configuration block should render — only while Payroll Statutory is on (PT lives
    /// under it). A payroll-off / statutory-off company never sees the PT fields (ER-13).</summary>
    public bool ShowPtConfig => PayrollStatutoryEnabled;

    /// <summary>The read-only display of the PT wage basis (only <see cref="PtWageBasis.GrossEarnings"/> today).</summary>
    public string PtWageBasisText => "Gross monthly earnings";

    /// <summary>The PT state picker options (None + the seeded states Maharashtra / Karnataka / West Bengal).</summary>
    public ObservableCollection<PtStateOption> PtStateOptions { get; } = new();

    /// <summary>The seeded slab bands of the currently-selected PT state (read-only view; rebuilt as the state
    /// changes). Empty for "None".</summary>
    public ObservableCollection<PtSlabRow> PtSlabBands { get; } = new();

    /// <summary>True when the selected PT state has seeded slab bands to show (drives the slab grid's visibility).</summary>
    public bool HasPtSlabBands => PtSlabBands.Count > 0;

    /// <summary>True when the selected PT state levies no PT (no slab bands) — drives the "None" note.</summary>
    public bool PtStateHasNoSlab => PtSlabBands.Count == 0;

    // ---- §192 Salary TDS (Phase 8 slice 7; F11 Payroll Statutory → Income-Tax; RQ-12) -----------------------
    // The establishment's §192 salary-TDS switch + the salary deductor category. Only meaningful (and only shown)
    // while PayrollStatutoryEnabled is on; enabling flips Company.SalaryTdsEnabled via the engine, disabling clears
    // it. The deductor IDENTITY (TAN / responsible person) is the SHARED Phase-7 deductor config — no parallel one.
    // Gated: a company that never enables salary-TDS is byte-identical to a pre-v36 company (ER-13).

    /// <summary>Whether the establishment deducts <b>§192 salary TDS</b> (the "Enable Salary TDS" toggle; applied via
    /// <see cref="ApplySalaryTds"/>). Mirrors <see cref="Company.SalaryTdsEnabled"/>.</summary>
    [ObservableProperty] private bool _salaryTdsEnabled;

    /// <summary>The salary <b>deductor category</b> = the Form-24Q section code (92B private default / 92A govt /
    /// 92C union-govt). Surfaced here for the establishment; the Form 24Q / Form 16 screens carry the same picker.</summary>
    [ObservableProperty] private SalarySectionCodeOption? _selectedSalarySectionCode;

    /// <summary>The §192 Enable/disable result message (kept separate from the GST/TDS/TCS/PF/ESI/PT messages).</summary>
    [ObservableProperty] private string? _salaryTdsMessage;

    /// <summary>True iff the §192 salary-TDS block should render — only while Payroll Statutory is on (it lives under
    /// it). A payroll-off / statutory-off company never sees the salary-TDS fields (ER-13).</summary>
    public bool ShowSalaryTdsConfig => PayrollStatutoryEnabled;

    /// <summary>The financial-year label the salary-TDS estimate runs for (e.g. "2025-26").</summary>
    public string SalaryTdsFinancialYearLabel => FyLabel(_company.FinancialYearStart.Year);

    /// <summary>The period label the §192 return is filed for — the assessment year (FY + 1, e.g. "2026-27") under the
    /// 1961 Act, or the tax year (== the financial year) from FY 2026-27 onward under the 2025 Act. Always render it
    /// beside <see cref="SalaryTdsPeriodCaption"/>: the two vocabularies collide numerically (CA S9).</summary>
    public string SalaryTdsAssessmentYearLabel => StatuteVocabulary.PeriodLabel(_company.FinancialYearStart.Year);

    /// <summary>The FY-gated caption for <see cref="SalaryTdsAssessmentYearLabel"/> — "Assessment Year" / "Tax Year".</summary>
    public string SalaryTdsPeriodCaption => StatuteVocabulary.PeriodCaption(_company.FinancialYearStart.Year);

    /// <summary>The abbreviated caption ("AY" / "Tax Year") for the enable/disable confirmation message.</summary>
    public string SalaryTdsPeriodCaptionShort => StatuteVocabulary.PeriodCaptionShort(_company.FinancialYearStart.Year);

    /// <summary>The reused Phase-7 deductor identity the Form 24Q / Form 16 print (TAN + responsible person), or a
    /// prompt to enable TDS when no TAN is captured yet — §192 does not fork a parallel deductor config.</summary>
    public string SalaryTdsDeductorText
    {
        get
        {
            var tds = _company.Tds;
            if (tds is null || string.IsNullOrWhiteSpace(tds.Tan))
                return "No TAN captured yet — enable TDS above to file Form 24Q / issue Form 16.";
            var who = string.IsNullOrWhiteSpace(tds.ResponsiblePersonName) ? "—" : tds.ResponsiblePersonName!;
            return $"TAN {tds.Tan}  ·  {who}";
        }
    }

    /// <summary>The salary deductor-category options (92B private / 92A govt / 92C union-govt), shared with the reports.</summary>
    public ObservableCollection<SalarySectionCodeOption> SalarySectionCodes { get; } = new();

    // ---- Gratuity (Phase 8 slice 9; F11 Payroll Statutory → Gratuity; RQ-14) --------------------------------
    // The establishment's gratuity-provision policy the deterministic accrual reads (Payment of Gratuity Act 1972):
    // the ₹20,00,000 §4(3) cap, the Basic + DA wage basis and which employees a run accrues for. Only meaningful
    // (and only shown) while PayrollStatutoryEnabled is on; enrolling sets Company.GratuityConfig via the engine,
    // disabling clears it. A company that never enrols for gratuity is byte-identical to a pre-v37 company (ER-13).

    /// <summary>Whether the establishment provisions for Gratuity (the "Enable Gratuity" toggle; applied via
    /// <see cref="ApplyGratuity"/>). Non-<c>null</c> <see cref="Company.GratuityConfig"/> ⇔ enrolled.</summary>
    [ObservableProperty] private bool _gratuityEnabled;

    /// <summary>The statutory gratuity ceiling in whole rupees (§4(3); default ₹20,00,000). Configurable so a revised
    /// government notification is a data change, not a code change.</summary>
    [ObservableProperty] private string _gratuityCapText = "2000000";

    /// <summary>Which employees a provision run accrues for (all active [default] / vested-only ≥ 5 years).</summary>
    [ObservableProperty] private GratuityPopulationOption? _selectedGratuityPopulation;

    /// <summary>The Gratuity Enable/disable result message (kept separate from the other statutory messages).</summary>
    [ObservableProperty] private string? _gratuityMessage;

    /// <summary>True iff the Gratuity configuration block should render — only while Payroll Statutory is on (gratuity
    /// lives under it). A payroll-off / statutory-off company never sees the Gratuity fields (ER-13).</summary>
    public bool ShowGratuityConfig => PayrollStatutoryEnabled;

    /// <summary>The read-only display of the gratuity wage basis (only Basic + DA today).</summary>
    public string GratuityWageBasisText => "Last-drawn Basic + Dearness Allowance (15 / 26 formula)";

    /// <summary>The provision-population picker options (all active / vested-only).</summary>
    public ObservableCollection<GratuityPopulationOption> GratuityPopulations { get; } = new();

    // ---- Statutory Bonus (Phase 8 slice 9; F11 Payroll Statutory → Bonus; RQ-15) ----------------------------
    // The establishment's statutory-bonus policy the deterministic computation reads (Payment of Bonus Act 1965):
    // the §10–§11 rate (8.33%–20%; default 8.33%), the §12 ₹7,000 calc ceiling, the state minimum wage and whether a
    // mid-year joiner's bonus is prorated. Only meaningful (and only shown) while PayrollStatutoryEnabled is on;
    // enrolling sets Company.BonusConfig via the engine, disabling clears it. Byte-identical when never enrolled (ER-13).

    /// <summary>Whether the establishment computes statutory Bonus (the "Enable Bonus" toggle; applied via
    /// <see cref="ApplyBonus"/>). Non-<c>null</c> <see cref="Company.BonusConfig"/> ⇔ enrolled.</summary>
    [ObservableProperty] private bool _bonusEnabled;

    /// <summary>The bonus rate as a percent (clamped to the §10–§11 8.33%–20% band on Apply; default 8.33%).</summary>
    [ObservableProperty] private string _bonusRatePercentText = "8.33";

    /// <summary>The §12 monthly calculation ceiling in whole rupees (default ₹7,000).</summary>
    [ObservableProperty] private string _bonusCalculationCeilingText = "7000";

    /// <summary>The applicable state minimum wage per month (default ₹0 ⇒ the ceiling falls back to ₹7,000).</summary>
    [ObservableProperty] private string _bonusMinimumWageText = "0";

    /// <summary>Whether a mid-year joiner's annual bonus is prorated by the months actually worked (default true).</summary>
    [ObservableProperty] private bool _bonusProrate = true;

    /// <summary>The Bonus Enable/disable result message (kept separate from the other statutory messages).</summary>
    [ObservableProperty] private string? _bonusMessage;

    /// <summary>True iff the Bonus configuration block should render — only while Payroll Statutory is on (bonus lives
    /// under it). A payroll-off / statutory-off company never sees the Bonus fields (ER-13).</summary>
    public bool ShowBonusConfig => PayrollStatutoryEnabled;

    private static string FyLabel(int startYear) => $"{startYear}-{(startYear + 1) % 100:00}";

    /// <summary>The company GSTIN/UIN (validated on Enable); blank ⇒ unset.</summary>
    [ObservableProperty] private string _gstin = string.Empty;

    /// <summary>The chosen Home State/UT (supplier place-of-supply); required on Enable.</summary>
    [ObservableProperty] private IndianStateOption? _homeState;

    /// <summary>The chosen registration type (Regular is the working type in Phase 4; Composition works from Phase 9).</summary>
    [ObservableProperty] private GstRegistrationTypeOption? _registrationType;

    /// <summary>The chosen GSTR-1 (and paired 3B) return periodicity.</summary>
    [ObservableProperty] private GstPeriodicityOption? _periodicity;

    // ---- Composition scheme (Phase 9 slice 3; §10 + Rule 7; RQ-4) --------------------------------------------
    // Only meaningful (and only shown) while RegistrationType is Composition. The sub-type drives the tax-on-turnover
    // rate + turnover base; the opt-in date (CMP-02) is advisory. Persisted onto GstConfig by Apply(); a Regular
    // company leaves both null and is byte-identical (ER-13).

    /// <summary>The composition sub-type (Manufacturer / Trader / Restaurant / §10(2A) Service Provider). Non-null once
    /// GST is enabled; only written to the config when <see cref="IsComposition"/>.</summary>
    [ObservableProperty] private CompositionSubTypeOption? _selectedCompositionSubType;

    /// <summary>The date the dealer opted into composition (CMP-02; advisory) — ISO yyyy-MM-dd; blank ⇒ unset.</summary>
    [ObservableProperty] private string _compositionOptInDateText = string.Empty;

    /// <summary>The composition sub-type options (Manufacturer / Trader / Restaurant / §10(2A) Service Provider).</summary>
    public ObservableCollection<CompositionSubTypeOption> CompositionSubTypes { get; } = new();

    /// <summary>True iff the chosen registration type is Composition — drives the composition block's visibility.</summary>
    public bool IsComposition => RegistrationType?.Value == GstRegistrationType.Composition;

    /// <summary>The advisory resolved tax-on-turnover rate + turnover base for the selected sub-type (never a posting
    /// gate — a composition dealer posts no tax; this is guidance only).</summary>
    public string CompositionRateText
    {
        get
        {
            if (SelectedCompositionSubType?.Value is not { } st) return "Select a composition sub-type to see the rate.";
            var bp = CompositionThreshold.RateBasisPoints(st);
            var basis = CompositionThreshold.TaxesTotalTurnover(st)
                ? "total turnover in State/UT (incl. exempt)"
                : "taxable supplies only";
            return $"Tax on turnover {bp / 100m:0.##}%  (CGST {bp / 200m:0.##}% + SGST {bp / 200m:0.##}%) on {basis}.";
        }
    }

    /// <summary>The advisory preceding-FY aggregate-turnover eligibility threshold for the selected sub-type + home state.</summary>
    public string CompositionThresholdText
    {
        get
        {
            if (SelectedCompositionSubType?.Value is not { } st) return string.Empty;
            var t = CompositionThreshold.Threshold(st, HomeState?.Code);
            return $"Eligibility ≤ ₹{IndianFormat.RupeesAlways(t.Amount)} aggregate turnover (preceding FY).";
        }
    }

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

        // PT state picker: None + the seeded states (Maharashtra 27 / Karnataka 29 / West Bengal 19). PT is
        // state-configurable; the seeded set is the DP default (any state is addable via its own slab table).
        PtStateOptions.Add(new PtStateOption { Code = null, Display = "None (no Professional Tax)" });
        foreach (var code in new[] { "27", "29", "19" })
        {
            var st = IndianState.FromCode(code);
            if (st is not null) PtStateOptions.Add(new PtStateOption { Code = st.Code, Display = $"{st.Code} — {st.Name}" });
        }

        // §192 salary deductor category (92B private default / 92A govt / 92C union-govt) — shared with the reports.
        foreach (var opt in SalarySectionCodeOption.All) SalarySectionCodes.Add(opt);

        // Gratuity provision-population picker (all active [default] / vested-only).
        GratuityPopulations.Add(new GratuityPopulationOption { Value = GratuityProvisionPopulation.AllActiveEmployees, Display = "All active employees (accrue pre-vesting)" });
        GratuityPopulations.Add(new GratuityPopulationOption { Value = GratuityProvisionPopulation.VestedOnly, Display = "Vested only (≥ 5 years' service)" });

        // Composition sub-type picker (Phase 9 slice 3; §10 + Rule 7) — the four dealer kinds + their rate/base.
        CompositionSubTypes.Add(new CompositionSubTypeOption { Value = CompositionSubType.Manufacturer, Display = "Manufacturer — 1% on total turnover" });
        CompositionSubTypes.Add(new CompositionSubTypeOption { Value = CompositionSubType.Trader, Display = "Trader / other goods — 1% on taxable supplies" });
        CompositionSubTypes.Add(new CompositionSubTypeOption { Value = CompositionSubType.Restaurant, Display = "Restaurant (non-alcohol) — 5% on total turnover" });
        CompositionSubTypes.Add(new CompositionSubTypeOption { Value = CompositionSubType.ServiceProvider, Display = "§10(2A) Service Provider — 6% on taxable supplies" });

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
        LoadEsiFromCompany();
        LoadPtFromCompany();
        LoadSalaryTdsFromCompany();
        LoadGratuityFromCompany();
        LoadBonusFromCompany();
        Gstin = cfg?.Gstin ?? string.Empty;
        HomeState = HomeStates.FirstOrDefault(o => o.Code == cfg?.HomeStateCode);
        RegistrationType = RegistrationTypes.FirstOrDefault(o => o.Value == (cfg?.RegistrationType ?? GstRegistrationType.Regular))
                           ?? RegistrationTypes.First();
        Periodicity = Periodicities.FirstOrDefault(o => o.Value == (cfg?.Periodicity ?? GstReturnPeriodicity.Monthly))
                      ?? Periodicities.First();
        // Composition (Phase 9 slice 3): seed the sub-type (default Manufacturer for a not-yet-composition company) + the
        // advisory opt-in date. Both are only written back to the config when RegistrationType is Composition.
        SelectedCompositionSubType = CompositionSubTypes.FirstOrDefault(o => o.Value == cfg?.CompositionSubType)
                                     ?? CompositionSubTypes.First();
        CompositionOptInDateText = cfg?.CompositionOptInDate is { } optIn ? ApexDate.Format(optIn) : string.Empty;
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
        OnPropertyChanged(nameof(SalaryTdsDeductorText)); // §192 reuses the just-loaded deductor identity
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

    /// <summary>The registration type changed — surface/hide the composition block and refresh its advisory text.</summary>
    partial void OnRegistrationTypeChanged(GstRegistrationTypeOption? value)
    {
        OnPropertyChanged(nameof(IsComposition));
        RefreshCompositionAdvisory();
    }

    /// <summary>The composition sub-type changed — refresh the advisory rate/threshold display.</summary>
    partial void OnSelectedCompositionSubTypeChanged(CompositionSubTypeOption? value) => RefreshCompositionAdvisory();

    /// <summary>The home state changed — refresh the advisory threshold (the special-category states carry ₹75 L).</summary>
    partial void OnHomeStateChanged(IndianStateOption? value) => RefreshCompositionAdvisory();

    /// <summary>Raises change notifications for the advisory composition rate + threshold text.</summary>
    private void RefreshCompositionAdvisory()
    {
        OnPropertyChanged(nameof(CompositionRateText));
        OnPropertyChanged(nameof(CompositionThresholdText));
    }

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
        OnPropertyChanged(nameof(ShowEsiConfig));
        OnPropertyChanged(nameof(ShowPtConfig));
        OnPropertyChanged(nameof(ShowSalaryTdsConfig));
        OnPropertyChanged(nameof(ShowGratuityConfig));
        OnPropertyChanged(nameof(ShowBonusConfig));
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
        OnPropertyChanged(nameof(ShowEsiConfig)); // the ESI block appears/hides with the statutory sub-toggle
        OnPropertyChanged(nameof(ShowPtConfig)); // the PT block appears/hides with the statutory sub-toggle
        OnPropertyChanged(nameof(ShowSalaryTdsConfig)); // the §192 salary-TDS block appears/hides with the sub-toggle
        OnPropertyChanged(nameof(ShowGratuityConfig)); // the Gratuity block appears/hides with the sub-toggle
        OnPropertyChanged(nameof(ShowBonusConfig)); // the Bonus block appears/hides with the sub-toggle
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

    /// <summary>Seeds the ESI form from the company's current <see cref="Company.EsiConfig"/> (defaults for a
    /// never-enrolled company: 0.75%/3.25% rates, no employer code).</summary>
    private void LoadEsiFromCompany()
    {
        var esi = _company.EsiConfig;
        EsiEnabled = esi is not null;
        EsiEmployerCode = esi?.EmployerCode ?? string.Empty;
    }

    /// <summary>
    /// Applies the "Enable ESI" toggle (F11 → Payroll Statutory; Phase 8 slice 5; RQ-10), mirroring
    /// <see cref="ApplyPf"/>. On enable: enrol the establishment via the engine's idempotent
    /// <see cref="PayrollService.EnableEsi"/> (EE 0.75% / ER 3.25% defaults + the optional 17-digit ESIC employer
    /// code, structurally validated) — which also turns Payroll Statutory on — and persist. On disable: clear
    /// <see cref="Company.EsiConfig"/> and persist (per-employee ESI details are retained, inert). Any domain error
    /// is surfaced to <see cref="EsiMessage"/> without crashing, and the toggle reverts to the real company state.
    /// </summary>
    public bool ApplyEsi()
    {
        EsiMessage = null;

        if (!EsiEnabled)
        {
            _company.EsiConfig = null; // enrolment cleared; per-employee ESI details retained (harmless, inert)
            if (!TrySave(m => EsiMessage = m)) { RevertEsiToggle(); return false; }
            EsiMessage = "Employees' State Insurance is now OFF for this company. Employee ESI details are unchanged.";
            _onChanged();
            return true;
        }

        var code = BlankToNull(EsiEmployerCode);
        try
        {
            new PayrollService(_company).EnableEsi(employerCode: code);
            _storage.Save(_company);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            EsiMessage = ex.Message;
            RevertEsiToggle();
            return false;
        }

        // EnableEsi flips Payroll Statutory on — keep the sibling toggle + the config blocks in sync.
        if (PayrollStatutoryEnabled != _company.PayrollStatutoryEnabled)
            PayrollStatutoryEnabled = _company.PayrollStatutoryEnabled;
        EsiMessage = $"Employees' State Insurance enabled for {_company.Name} (employee 0.75% / employer 3.25%; "
                     + "coverage ceiling ₹21,000 gross, ₹25,000 for a person with disability).";
        _onChanged();
        return true;
    }

    /// <summary>Reverts the <see cref="EsiEnabled"/> toggle to reflect the company's real (unchanged) state.</summary>
    private void RevertEsiToggle()
    {
        var real = _company.EsiConfig is not null;
        if (EsiEnabled != real) EsiEnabled = real;
    }

    // =========================================================== Professional Tax (Phase 8 slice 6)

    /// <summary>Seeds the PT form from the company's current <see cref="Company.PtConfig"/> (defaults for a
    /// never-enrolled company: not enabled, "None" state, blank enrolment number) and rebuilds the slab preview.</summary>
    private void LoadPtFromCompany()
    {
        var pt = _company.PtConfig;
        PtEnabled = pt is not null;
        PtRegistrationNumber = pt?.RegistrationNumber ?? string.Empty;
        SelectedPtState = PtStateOptions.FirstOrDefault(o => o.Code == pt?.StateCode) ?? PtStateOptions.FirstOrDefault();
        RebuildSlabBands();
    }

    /// <summary>The active PT state changed — refresh the read-only slab preview so the selected state's bands show.</summary>
    partial void OnSelectedPtStateChanged(PtStateOption? value) => RebuildSlabBands();

    /// <summary>
    /// Applies the "Enable Professional Tax" toggle (F11 → Payroll Statutory; Phase 8 slice 6; RQ-11), mirroring
    /// <see cref="ApplyEsi"/>. On enable: enrol the establishment via the engine's idempotent
    /// <see cref="PayrollService.EnableProfessionalTax"/> (seeds the editable state slab tables — Maharashtra
    /// men/women, Karnataka, West Bengal) when not yet enrolled, or update the active state + enrolment number on the
    /// existing config (preserving its slab tables) — which also turns Payroll Statutory on — and persist. On disable:
    /// clear <see cref="Company.PtConfig"/> and persist. Any domain error is surfaced to <see cref="PtMessage"/>
    /// without crashing, and the toggle reverts to the real company state.
    /// </summary>
    public bool ApplyPt()
    {
        PtMessage = null;

        if (!PtEnabled)
        {
            _company.PtConfig = null; // enrolment cleared; the company is byte-identical to a pre-v35 company (ER-13)
            if (!TrySave(m => PtMessage = m)) { RevertPtToggle(); return false; }
            PtMessage = "Professional Tax is now OFF for this company.";
            RebuildSlabBands();
            _onChanged();
            return true;
        }

        var stateCode = SelectedPtState?.Code;
        var registration = BlankToNull(PtRegistrationNumber);
        try
        {
            if (_company.PtConfig is { } existing)
            {
                // Already enrolled — switch the active state + enrolment number in place (keeps the slab tables the
                // company may have edited); mirror EnableProfessionalTax's Payroll-Statutory-on invariant.
                new PayrollService(_company).SetProfessionalTaxState(stateCode);
                existing.RegistrationNumber = registration;
                _company.PayrollStatutoryEnabled = true;
            }
            else
            {
                new PayrollService(_company).EnableProfessionalTax(stateCode, registration);
            }
            _storage.Save(_company);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            PtMessage = ex.Message;
            RevertPtToggle();
            return false;
        }

        // EnableProfessionalTax flips Payroll Statutory on — keep the sibling toggle + the config blocks in sync.
        if (PayrollStatutoryEnabled != _company.PayrollStatutoryEnabled)
            PayrollStatutoryEnabled = _company.PayrollStatutoryEnabled;
        var stateName = stateCode is null ? "None" : (IndianState.FromCode(stateCode)?.Name ?? stateCode);
        PtMessage = $"Professional Tax enabled for {_company.Name} (state {stateName}; annual cap ₹2,500 per person).";
        RebuildSlabBands();
        _onChanged();
        return true;
    }

    /// <summary>Reverts the <see cref="PtEnabled"/> toggle to reflect the company's real (unchanged) state.</summary>
    private void RevertPtToggle()
    {
        var real = _company.PtConfig is not null;
        if (PtEnabled != real) PtEnabled = real;
    }

    /// <summary>Rebuilds the read-only slab preview for the selected PT state: the seeded bands of the active state's
    /// tables (Maharashtra shows its Men + Women tables; Karnataka / West Bengal one gender-agnostic table). Reads the
    /// company's live tables when enrolled, else the seed preview; "None" ⇒ empty.</summary>
    private void RebuildSlabBands()
    {
        PtSlabBands.Clear();
        var state = SelectedPtState?.Code;
        if (state is null) return;

        var tables = _company.PtConfig is { } cfg
            ? cfg.SlabTables
            : ProfessionalTax.SeedSlabTables();

        foreach (var table in tables.Where(t => string.Equals(t.StateCode, state, StringComparison.Ordinal))
                                     .OrderBy(t => (int)t.GenderScope))
        {
            foreach (var band in table.Bands)
            {
                var feb = band.MonthOverrides.FirstOrDefault(o => o.Month == ProfessionalTax.FebruaryOverrideMonth);
                PtSlabBands.Add(new PtSlabRow
                {
                    AppliesTo = GenderScopeLabel(table.GenderScope),
                    FromText = IndianFormat.RupeesAlways(band.FromWage.Amount),
                    ToText = band.ToWage is { } t ? IndianFormat.RupeesAlways(t.Amount) : "and above",
                    MonthlyText = IndianFormat.RupeesAlways(band.MonthlyAmount.Amount),
                    FebText = feb is null ? "—" : IndianFormat.RupeesAlways(feb.Amount.Amount),
                });
            }
        }
        OnPropertyChanged(nameof(HasPtSlabBands));
        OnPropertyChanged(nameof(PtStateHasNoSlab));
    }

    private static string GenderScopeLabel(PtGenderScope scope) => scope switch
    {
        PtGenderScope.Male => "Men",
        PtGenderScope.Female => "Women",
        _ => "All employees",
    };

    // =========================================================== §192 Salary TDS (Phase 8 slice 7)

    /// <summary>Seeds the §192 form from the company's current <see cref="Company.SalaryTdsEnabled"/> (defaults for a
    /// never-enabled company: not enabled, deductor category 92B private).</summary>
    private void LoadSalaryTdsFromCompany()
    {
        SalaryTdsEnabled = _company.SalaryTdsEnabled;
        SelectedSalarySectionCode ??= SalarySectionCodes.FirstOrDefault(o => o.Code == "92B")
                                      ?? SalarySectionCodes.FirstOrDefault();
        OnPropertyChanged(nameof(SalaryTdsDeductorText));
    }

    /// <summary>
    /// Applies the "Enable Salary TDS" toggle (F11 → Payroll Statutory; Phase 8 slice 7; RQ-12), mirroring
    /// <see cref="ApplyPt"/>. On enable: turn on §192 salary-TDS via the engine's idempotent
    /// <see cref="PayrollService.EnableSalaryTds"/> (which also turns Payroll Statutory on) and persist. On disable:
    /// <see cref="PayrollService.DisableSalaryTds"/> + persist (per-employee tax declarations are retained, inert).
    /// Any domain error is surfaced to <see cref="SalaryTdsMessage"/> without crashing, and the toggle reverts.
    /// </summary>
    public bool ApplySalaryTds()
    {
        SalaryTdsMessage = null;

        try
        {
            var service = new PayrollService(_company);
            if (SalaryTdsEnabled) service.EnableSalaryTds();
            else service.DisableSalaryTds();
            _storage.Save(_company);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            SalaryTdsMessage = ex.Message;
            RevertSalaryTdsToggle();
            return false;
        }

        // EnableSalaryTds flips Payroll Statutory on — keep the sibling toggle + the config blocks in sync.
        if (PayrollStatutoryEnabled != _company.PayrollStatutoryEnabled)
            PayrollStatutoryEnabled = _company.PayrollStatutoryEnabled;
        var category = SelectedSalarySectionCode?.Code ?? "92B";
        SalaryTdsMessage = SalaryTdsEnabled
            ? $"§192 salary TDS enabled for {_company.Name} (average-rate monthly withholding; deductor {category}; "
              + $"{SalaryTdsPeriodCaptionShort} {SalaryTdsAssessmentYearLabel})."
            : "§192 salary TDS is now OFF for this company. Employee tax declarations are unchanged.";
        OnPropertyChanged(nameof(SalaryTdsDeductorText));
        _onChanged();
        return true;
    }

    /// <summary>Reverts the <see cref="SalaryTdsEnabled"/> toggle to reflect the company's real (unchanged) state.</summary>
    private void RevertSalaryTdsToggle()
    {
        if (SalaryTdsEnabled != _company.SalaryTdsEnabled) SalaryTdsEnabled = _company.SalaryTdsEnabled;
    }

    // =========================================================== Gratuity (Phase 8 slice 9)

    /// <summary>Seeds the Gratuity form from the company's current <see cref="Company.GratuityConfig"/> (defaults for a
    /// never-enrolled company: ₹20,00,000 cap, Basic + DA basis, all-active population).</summary>
    private void LoadGratuityFromCompany()
    {
        var g = _company.GratuityConfig;
        GratuityEnabled = g is not null;
        GratuityCapText = ((long)(g?.CapAmount.Amount ?? GratuityConfig.DefaultCapAmount)).ToString(CultureInfo.InvariantCulture);
        var population = g?.Population ?? GratuityProvisionPopulation.AllActiveEmployees;
        SelectedGratuityPopulation = GratuityPopulations.FirstOrDefault(o => o.Value == population)
                                     ?? GratuityPopulations.FirstOrDefault();
    }

    /// <summary>
    /// Applies the "Enable Gratuity" toggle (F11 → Payroll Statutory; Phase 8 slice 9; RQ-14), mirroring
    /// <see cref="ApplyPf"/>. On enable: enrol the establishment via the engine's idempotent
    /// <see cref="PayrollService.EnableGratuity"/> (the ₹20,00,000 §4(3) cap, Basic + DA basis, the chosen population)
    /// — which also turns Payroll Statutory on — and persist. On disable: clear <see cref="Company.GratuityConfig"/>
    /// and persist (the company is byte-identical to a pre-v37 company, ER-13). A negative / non-numeric cap is
    /// surfaced to <see cref="GratuityMessage"/> without crashing and the toggle reverts to the real company state.
    /// </summary>
    public bool ApplyGratuity()
    {
        GratuityMessage = null;

        if (!GratuityEnabled)
        {
            _company.GratuityConfig = null; // enrolment cleared; byte-identical to a pre-v37 company (ER-13)
            if (!TrySave(m => GratuityMessage = m)) { RevertGratuityToggle(); return false; }
            GratuityMessage = "Gratuity provisioning is now OFF for this company.";
            _onChanged();
            return true;
        }

        if (!TryParseWholeRupees(GratuityCapText, out var cap) || cap < 0m)
        {
            GratuityMessage = "The gratuity cap must be a non-negative whole-rupee amount (e.g. 2000000).";
            RevertGratuityToggle();
            return false;
        }
        var population = SelectedGratuityPopulation?.Value ?? GratuityProvisionPopulation.AllActiveEmployees;
        try
        {
            new PayrollService(_company).EnableGratuity(new Money(cap),
                GratuityWageBasis.BasicAndDearnessAllowance, population);
            _storage.Save(_company);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            GratuityMessage = ex.Message;
            RevertGratuityToggle();
            return false;
        }

        // EnableGratuity flips Payroll Statutory on — keep the sibling toggle + the config blocks in sync.
        if (PayrollStatutoryEnabled != _company.PayrollStatutoryEnabled)
            PayrollStatutoryEnabled = _company.PayrollStatutoryEnabled;
        var popLabel = population == GratuityProvisionPopulation.VestedOnly ? "vested-only (≥ 5 years)" : "all active employees";
        GratuityMessage = $"Gratuity enabled for {_company.Name} (15 / 26 formula on Basic + DA; cap "
                          + $"₹{IndianFormat.RupeesAlways(cap)}; accrue for {popLabel}).";
        _onChanged();
        return true;
    }

    /// <summary>Reverts the <see cref="GratuityEnabled"/> toggle to reflect the company's real (unchanged) state.</summary>
    private void RevertGratuityToggle()
    {
        var real = _company.GratuityConfig is not null;
        if (GratuityEnabled != real) GratuityEnabled = real;
    }

    // =========================================================== Statutory Bonus (Phase 8 slice 9)

    /// <summary>Seeds the Bonus form from the company's current <see cref="Company.BonusConfig"/> (defaults for a
    /// never-enrolled company: 8.33% rate, ₹7,000 calc ceiling, ₹0 minimum wage, prorate on).</summary>
    private void LoadBonusFromCompany()
    {
        var b = _company.BonusConfig;
        BonusEnabled = b is not null;
        var bp = b?.RateBasisPoints ?? BonusConfig.DefaultRateBasisPoints;
        BonusRatePercentText = (bp / 100m).ToString("0.##", CultureInfo.InvariantCulture);
        BonusCalculationCeilingText = ((long)(b?.CalculationCeiling.Amount ?? BonusConfig.DefaultCalculationCeiling)).ToString(CultureInfo.InvariantCulture);
        BonusMinimumWageText = ((long)(b?.MinimumWage.Amount ?? 0m)).ToString(CultureInfo.InvariantCulture);
        BonusProrate = b?.Prorate ?? true;
    }

    /// <summary>
    /// Applies the "Enable Bonus" toggle (F11 → Payroll Statutory; Phase 8 slice 9; RQ-15), mirroring
    /// <see cref="ApplyGratuity"/>. On enable: enrol the establishment via the engine's idempotent
    /// <see cref="PayrollService.EnableStatutoryBonus"/> (the rate clamped to the §10–§11 8.33%–20% band, the §12
    /// ₹7,000 calc ceiling, the state minimum wage, the prorate flag) — which also turns Payroll Statutory on — and
    /// persist. On disable: clear <see cref="Company.BonusConfig"/> and persist (byte-identical, ER-13). A
    /// non-numeric rate / ceiling is surfaced to <see cref="BonusMessage"/> without crashing and the toggle reverts.
    /// </summary>
    public bool ApplyBonus()
    {
        BonusMessage = null;

        if (!BonusEnabled)
        {
            _company.BonusConfig = null; // enrolment cleared; byte-identical to a pre-v37 company (ER-13)
            if (!TrySave(m => BonusMessage = m)) { RevertBonusToggle(); return false; }
            BonusMessage = "Statutory Bonus is now OFF for this company.";
            _onChanged();
            return true;
        }

        if (!TryParsePercent(BonusRatePercentText, out var percent) || percent < 0m)
        {
            BonusMessage = "The bonus rate must be a percent between 8.33 and 20 (e.g. 8.33).";
            RevertBonusToggle();
            return false;
        }
        if (!TryParseWholeRupees(BonusCalculationCeilingText, out var ceiling) || ceiling < 0m)
        {
            BonusMessage = "The calculation ceiling must be a non-negative whole-rupee amount (e.g. 7000).";
            RevertBonusToggle();
            return false;
        }
        if (!TryParseWholeRupees(BonusMinimumWageText, out var minWage) || minWage < 0m)
        {
            BonusMessage = "The minimum wage must be a non-negative whole-rupee amount (0 ⇒ ceiling ₹7,000).";
            RevertBonusToggle();
            return false;
        }

        // Percent → basis points (100 bp = 1%); the engine clamps to the §10–§11 band on construction.
        var basisPoints = (int)Math.Round(percent * 100m, 0, MidpointRounding.AwayFromZero);
        try
        {
            new PayrollService(_company).EnableStatutoryBonus(basisPoints, new Money(ceiling), new Money(minWage), BonusProrate);
            _storage.Save(_company);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            BonusMessage = ex.Message;
            RevertBonusToggle();
            return false;
        }

        // EnableStatutoryBonus flips Payroll Statutory on — keep the sibling toggle + the config blocks in sync.
        if (PayrollStatutoryEnabled != _company.PayrollStatutoryEnabled)
            PayrollStatutoryEnabled = _company.PayrollStatutoryEnabled;
        var appliedBp = _company.BonusConfig?.RateBasisPoints ?? basisPoints;
        BonusMessage = $"Statutory Bonus enabled for {_company.Name} (rate {(appliedBp / 100m).ToString("0.##", CultureInfo.InvariantCulture)}%; "
                       + $"eligibility ≤ ₹21,000 · calc ceiling ₹{IndianFormat.RupeesAlways(ceiling)}).";
        // Reflect the clamped rate back into the field so the user sees the applied value.
        BonusRatePercentText = (appliedBp / 100m).ToString("0.##", CultureInfo.InvariantCulture);
        _onChanged();
        return true;
    }

    /// <summary>Reverts the <see cref="BonusEnabled"/> toggle to reflect the company's real (unchanged) state.</summary>
    private void RevertBonusToggle()
    {
        var real = _company.BonusConfig is not null;
        if (BonusEnabled != real) BonusEnabled = real;
    }

    /// <summary>Parses a whole-rupee amount (grouping commas tolerated); false on a non-numeric value.</summary>
    private static bool TryParseWholeRupees(string? text, out decimal value) =>
        decimal.TryParse((text ?? string.Empty).Replace(",", string.Empty).Trim(),
            NumberStyles.Number, CultureInfo.InvariantCulture, out value);

    /// <summary>Parses a percent value (e.g. "8.33"); false on a non-numeric value.</summary>
    private static bool TryParsePercent(string? text, out decimal value) =>
        decimal.TryParse((text ?? string.Empty).Trim(),
            NumberStyles.Number, CultureInfo.InvariantCulture, out value);

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

        // Composition (Phase 9 slice 3; RQ-4): a Composition registration carries the sub-type (drives the tax-on-turnover
        // rate + base) + an advisory CMP-02 opt-in date. The engine's EnsureValid fails-fast when Composition has no
        // sub-type — the try/catch below surfaces it gracefully. A non-composition registration clears both (ER-13).
        if (config.RegistrationType == GstRegistrationType.Composition)
        {
            config.CompositionSubType = (SelectedCompositionSubType ?? CompositionSubTypes.First()).Value;
            config.CompositionOptInDate = TryParseIsoDate(CompositionOptInDateText);
        }
        else
        {
            config.CompositionSubType = null;
            config.CompositionOptInDate = null;
        }

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
        Message = IsComposition
            ? $"GST enabled for {_company.Name} as a Composition dealer ({SelectedCompositionSubType?.Value}). "
              + "Sales issue a Bill of Supply (no tax collected); file CMP-08 (quarterly) + GSTR-4 (annual)."
            : $"GST enabled for {_company.Name}. {TaxLedgers.Count} tax ledgers ready; slabs 0/5/18/40 seeded.";
        _onChanged();
        return true;
    }

    /// <summary>Parses an ISO (yyyy-MM-dd) date; <c>null</c> for a blank or unparseable value (advisory only).</summary>
    private static DateOnly? TryParseIsoDate(string? text)
    {
        var s = (text ?? string.Empty).Trim();
        if (s.Length == 0) return null;
        // WI-5: shared DAY-FIRST parse (was a bare InvariantCulture parse — the MM/dd misread).
        return ApexDate.TryParse(s, out var d) ? d : null;
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
        // Commit the ESI enrolment on the same rule (toggle changed, or ESI on to persist an edited employer code).
        if (ShowEsiConfig && (EsiEnabled != (_company.EsiConfig is not null) || EsiEnabled))
            ApplyEsi();
        // Commit the PT enrolment on the same rule (toggle changed, or PT on to persist an edited state / enrolment no.).
        if (ShowPtConfig && (PtEnabled != (_company.PtConfig is not null) || PtEnabled))
            ApplyPt();
        // Commit the §192 salary-TDS switch whenever the toggle differs from the persisted state.
        if (ShowSalaryTdsConfig && SalaryTdsEnabled != _company.SalaryTdsEnabled)
            ApplySalaryTds();
        // Commit the Gratuity enrolment (toggle changed, or gratuity on to persist an edited cap / population).
        if (ShowGratuityConfig && (GratuityEnabled != (_company.GratuityConfig is not null) || GratuityEnabled))
            ApplyGratuity();
        // Commit the Bonus enrolment (toggle changed, or bonus on to persist an edited rate / ceiling / prorate).
        if (ShowBonusConfig && (BonusEnabled != (_company.BonusConfig is not null) || BonusEnabled))
            ApplyBonus();
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
