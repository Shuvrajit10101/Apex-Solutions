using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Services;
using Apex.Desktop.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using DomainLedger = Apex.Ledger.Domain.Ledger;

namespace Apex.Desktop.ViewModels;

/// <summary>A ledger row for the existing-ledgers list on the ledger-master screen.</summary>
public sealed class LedgerListRow
{
    public string Name { get; init; } = string.Empty;
    public string Under { get; init; } = string.Empty;
    public string Opening { get; init; } = string.Empty;

    /// <summary>An interest summary ("18% p.a. Simple") for an interest-enabled ledger, else blank.</summary>
    public string Interest { get; init; } = string.Empty;

    /// <summary>The ledger currency ("USD" for a forex ledger, blank/"₹" for a base-currency ledger).</summary>
    public string Currency { get; init; } = string.Empty;
}

/// <summary>
/// A combo option wrapping one of the interest enums (Per / On balance / Applicability / Style) with a
/// human display label, so the interest sub-form can bind a friendly list yet write the enum value.
/// </summary>
public sealed class InterestChoice<T> where T : struct, Enum
{
    public T Value { get; }
    public string Display { get; }
    public InterestChoice(T value, string display) { Value = value; Display = display; }
    public override string ToString() => Display;
}

/// <summary>
/// A "Currency of ledger" picker option (catalog §2/§20 Multi-currency): the company base currency (₹/INR,
/// <see cref="CurrencyId"/> null) or a created foreign currency. Wraps the currency's display for the combo
/// and the <see cref="CurrencyId"/> the ledger stores (null for the base).
/// </summary>
public sealed class CurrencyChoice
{
    /// <summary>The stored ledger currency id — null means the company base currency.</summary>
    public Guid? CurrencyId { get; }
    public string Display { get; }
    public CurrencyChoice(Guid? currencyId, string display) { CurrencyId = currencyId; Display = display; }
    public override string ToString() => Display;
}

/// <summary>
/// A "Method of Appropriation in Purchase invoice" picker option (Book pp.133–141; catalog §11; Phase 6 slice 3):
/// None (a plain Direct-Expenses ledger — pure P&amp;L, RQ-19), Appropriate by Quantity, or Appropriate by Value.
/// A non-null <see cref="Value"/> MARKS the ledger as an additional-cost ledger.
/// </summary>
public sealed class MethodOfAppropriationChoice
{
    /// <summary>The stored method — null means "None" (not an additional-cost ledger).</summary>
    public MethodOfAppropriation? Value { get; }
    public string Display { get; }
    public MethodOfAppropriationChoice(MethodOfAppropriation? value, string display) { Value = value; Display = display; }
    public override string ToString() => Display;
}

/// <summary>
/// A "Default Price Level" picker option (Book pp.34–35; Phase 6 slice 5; RQ-30): "(none)"
/// (<see cref="PriceLevelId"/> null — no default level, the norm) or a defined <see cref="PriceLevel"/>. When a
/// Sales voucher selects a party carrying a non-null level, its Price-Level header defaults to it (still
/// overridable per voucher). Only offered while <see cref="Company.EnableMultiplePriceLevels"/> is on.
/// </summary>
public sealed class PriceLevelChoice
{
    /// <summary>The stored party default price-level id — null means "(none)".</summary>
    public Guid? PriceLevelId { get; }
    public string Display { get; }
    public PriceLevelChoice(Guid? priceLevelId, string display) { PriceLevelId = priceLevelId; Display = display; }
    public override string ToString() => Display;
}

/// <summary>
/// The Ledger-creation master ("Create → Ledger", Alt+C): pick a name and an under-group
/// from the 28 predefined groups, create the ledger on the current company, and see it appear in
/// the list. Persists the company to its <c>.db</c> via <see cref="CompanyStorage.Save"/> on create.
/// Engine/DB logic stays here (no UI types) so it is headlessly testable.
/// </summary>
public sealed partial class LedgerMasterViewModel : ViewModelBase, IMasterListExportSource
{
    private readonly Company _company;
    private readonly CompanyStorage _storage;
    private readonly Action _onChanged;

    /// <inheritdoc/>
    /// <remarks>The generic snapshot (used only if this master is exported through the source-agnostic path).
    /// Ledger export normally uses the bespoke <see cref="Services.MasterListTabularProjector.ProjectLedgers"/>,
    /// which also splits the Dr/Cr side into its own column; here the numeric Opening carries the amount (its
    /// side stripped by the projector).</remarks>
    public MasterListSnapshot ToMasterListSnapshot() => new(
        "Ledgers",
        new[]
        {
            MasterListColumn.Text("Name"),
            MasterListColumn.Text("Under"),
            MasterListColumn.Number("Opening"),
            MasterListColumn.Text("Currency"),
            MasterListColumn.Text("Interest"),
        },
        Existing.Select(r => (IReadOnlyList<string>)new[]
        {
            r.Name, r.Under, r.Opening, r.Currency, r.Interest,
        }).ToList());

    /// <summary>
    /// The groups the Under-picker offers, name-sorted. Observable (not a fixed snapshot) because WI-1 lets the
    /// operator create a GROUP on the fly from this very field — the list has to grow under the picker.
    /// </summary>
    public ObservableCollection<Group> Groups { get; } = new();

    /// <summary>
    /// WI-1 — rebuilds the Under-picker from the company, keeping the current choice. Called after an Alt+C
    /// "create on the fly" launched from this screen's Under field adds a group; without it the new group would
    /// not be an option and selecting it back would silently do nothing.
    /// </summary>
    public void RefreshGroups()
    {
        var previousId = SelectedGroup?.Id;
        Groups.Clear();
        foreach (var g in _company.Groups.OrderBy(g => g.Name, StringComparer.OrdinalIgnoreCase))
            Groups.Add(g);
        SelectedGroup = Groups.FirstOrDefault(g => g.Id == previousId) ?? SelectedGroup;
    }

    /// <summary>The existing ledgers, refreshed after each create.</summary>
    public ObservableCollection<LedgerListRow> Existing { get; } = new();

    [ObservableProperty] private string _name = string.Empty;
    [ObservableProperty] private Group? _selectedGroup;
    [ObservableProperty] private string? _message;

    /// <summary>
    /// "Maintain balances bill-by-bill" (catalog §5) — shown for a party ledger. When on, party lines
    /// posting to this ledger capture bill-wise allocations and the ledger's open bills feed Outstandings.
    /// </summary>
    [ObservableProperty] private bool _maintainBillByBill;

    /// <summary>"Default credit period (days)" (catalog §5), typed as text; blank ⇒ none.</summary>
    [ObservableProperty] private string _defaultCreditPeriodText = string.Empty;

    // --------------------------------------------------------------- interest (catalog §7)

    /// <summary>
    /// "Activate Interest Calculation = Yes" (catalog §7). When ticked, the Rate/Per/On/Applicability/
    /// Style sub-form is shown and an <see cref="InterestParameters"/> block is attached on Create.
    /// </summary>
    [ObservableProperty] private bool _enableInterest;

    /// <summary>Interest rate percentage (per the chosen <see cref="SelectedPer"/> basis), typed as text.</summary>
    [ObservableProperty] private string _interestRateText = string.Empty;

    /// <summary>The rate-basis ("Per") choices — 365-day year, 30-day month, calendar year/month.</summary>
    public IReadOnlyList<InterestChoice<InterestPer>> PerChoices { get; } = new[]
    {
        new InterestChoice<InterestPer>(InterestPer.ThreeSixtyFiveDayYear, "365-Day Year"),
        new InterestChoice<InterestPer>(InterestPer.ThirtyDayMonth, "30-Day Month (360)"),
        new InterestChoice<InterestPer>(InterestPer.CalendarYear, "Calendar Year"),
        new InterestChoice<InterestPer>(InterestPer.CalendarMonth, "Calendar Month"),
    };

    /// <summary>Which side of the balance interest accrues on (all / debit-only / credit-only).</summary>
    public IReadOnlyList<InterestChoice<InterestOnBalance>> OnBalanceChoices { get; } = new[]
    {
        new InterestChoice<InterestOnBalance>(InterestOnBalance.All, "All Balances"),
        new InterestChoice<InterestOnBalance>(InterestOnBalance.DebitOnly, "Debit Balances only"),
        new InterestChoice<InterestOnBalance>(InterestOnBalance.CreditOnly, "Credit Balances only"),
    };

    /// <summary>Whether interest runs for the whole period or only after a bill's due date.</summary>
    public IReadOnlyList<InterestChoice<InterestApplicability>> ApplicabilityChoices { get; } = new[]
    {
        new InterestChoice<InterestApplicability>(InterestApplicability.Always, "Always"),
        new InterestChoice<InterestApplicability>(InterestApplicability.PostDue, "Past Due Date"),
    };

    /// <summary>Simple or compound interest.</summary>
    public IReadOnlyList<InterestChoice<InterestStyle>> StyleChoices { get; } = new[]
    {
        new InterestChoice<InterestStyle>(InterestStyle.Simple, "Simple"),
        new InterestChoice<InterestStyle>(InterestStyle.Compound, "Compound"),
    };

    [ObservableProperty] private InterestChoice<InterestPer>? _selectedPer;
    [ObservableProperty] private InterestChoice<InterestOnBalance>? _selectedOnBalance;
    [ObservableProperty] private InterestChoice<InterestApplicability>? _selectedApplicability;
    [ObservableProperty] private InterestChoice<InterestStyle>? _selectedStyle;

    // --------------------------------------------------------------- currency (catalog §2/§20)

    /// <summary>
    /// The "Currency of ledger" choices (catalog §2/§20): the company base currency (₹/INR) plus every
    /// created foreign currency. Defaults to the base — every existing ledger stays base-currency.
    /// </summary>
    public IReadOnlyList<CurrencyChoice> CurrencyChoices { get; }

    /// <summary>The chosen "Currency of ledger" — base by default; a foreign currency holds forex balances.</summary>
    [ObservableProperty] private CurrencyChoice? _selectedCurrency;

    // --------------------------------------------------------------- additional-cost method (Book pp.133–141; catalog §11)

    /// <summary>
    /// F12 ledger-screen configuration toggle (Book pp.133–141; Phase 6 slice 3). The
    /// "Method of Appropriation in Purchase invoice" field renders on the ledger master ONLY when this is on
    /// (and the chosen group is under Direct Expenses). Off by default, so an untracked ledger screen is
    /// byte-unchanged (ER-13).
    /// </summary>
    [ObservableProperty] private bool _showConfiguration;

    /// <summary>
    /// The "Method of Appropriation in Purchase invoice" choices — None (a plain P&amp;L Direct-Expenses ledger),
    /// Appropriate by Quantity, Appropriate by Value. A non-None pick makes the ledger an additional-cost ledger.
    /// </summary>
    public IReadOnlyList<MethodOfAppropriationChoice> MethodChoices { get; } = new[]
    {
        new MethodOfAppropriationChoice(null, "None (plain expense)"),
        new MethodOfAppropriationChoice(MethodOfAppropriation.ByQuantity, "Appropriate by Quantity"),
        new MethodOfAppropriationChoice(MethodOfAppropriation.ByValue, "Appropriate by Value"),
    };

    /// <summary>The chosen appropriation method — None by default (every existing ledger stays a plain expense).</summary>
    [ObservableProperty] private MethodOfAppropriationChoice? _selectedMethod;

    /// <summary>
    /// True iff the chosen group is under <b>Direct Expenses</b> — an additional-cost ledger (Freight/Packing/…)
    /// lives there. The Method-of-Appropriation field only ever applies to such a ledger.
    /// </summary>
    public bool IsDirectExpensesGroup => SelectedGroup is not null && IsUnderDirectExpenses(SelectedGroup);

    /// <summary>
    /// True iff the "Method of Appropriation" field should render: the ledger-screen F12 configuration is on
    /// AND the chosen group is under Direct Expenses. Gated so an untracked screen is byte-unchanged (ER-13).
    /// </summary>
    public bool ShowAppropriation => ShowConfiguration && IsDirectExpensesGroup;

    // --------------------------------------------------------------- party GST (catalog §12; phase4 RQ-7)

    /// <summary>True iff GST is enabled for the company — the party-GST sub-form is only offered then.</summary>
    public bool GstEnabled => _company.GstEnabled;

    /// <summary>
    /// True iff the party-GST sub-form should be shown: GST is enabled AND the chosen group is a party
    /// group (Sundry Debtors/Creditors). Off ⇒ no party-GST fields captured (a B2C/unregistered party).
    /// </summary>
    public bool ShowPartyGst => GstEnabled && IsPartyGroup;

    /// <summary>The party GSTIN/UIN (validated on Create when set); blank ⇒ a B2C party.</summary>
    [ObservableProperty] private string _partyGstin = string.Empty;

    /// <summary>The party's registration type (Regular / Composition / Unregistered / Consumer).</summary>
    [ObservableProperty] private GstRegistrationTypeOption? _partyRegistrationType;

    /// <summary>The party's State/UT (its place of supply for goods); null ⇒ unset.</summary>
    [ObservableProperty] private IndianStateOption? _partyState;

    /// <summary>The registration-type options for a party (default Unregistered — a plain B2C party).</summary>
    public IReadOnlyList<GstRegistrationTypeOption> PartyRegistrationTypes { get; } = new[]
    {
        new GstRegistrationTypeOption { Value = GstRegistrationType.Regular, Display = "Regular" },
        new GstRegistrationTypeOption { Value = GstRegistrationType.Composition, Display = "Composition" },
        new GstRegistrationTypeOption { Value = GstRegistrationType.Unregistered, Display = "Unregistered" },
        new GstRegistrationTypeOption { Value = GstRegistrationType.Consumer, Display = "Consumer" },
    };

    /// <summary>The State/UT options for the party place-of-supply picker (the GST state-code list).</summary>
    public IReadOnlyList<IndianStateOption> PartyStates { get; }

    // --------------------------------------------------------------- default price level (Book pp.34–35; slice 5 RQ-30)

    /// <summary>
    /// The "Default Price Level" choices for a party ledger (RQ-30): "(none)" plus every defined
    /// <see cref="PriceLevel"/>. Empty of levels when none are defined (only the "(none)" sentinel).
    /// </summary>
    public IReadOnlyList<PriceLevelChoice> PriceLevelChoices { get; }

    /// <summary>The chosen party default price level — "(none)" by default (every existing party keeps no default).</summary>
    [ObservableProperty] private PriceLevelChoice? _selectedPriceLevel;

    /// <summary>
    /// True iff the "Default Price Level" picker should render: the company's "Enable multiple Price Levels" F11
    /// flag is on AND the chosen group is a party group (Sundry Debtors). Gated so a non-price-level screen is
    /// byte-unchanged (ER-13).
    /// </summary>
    public bool ShowDefaultPriceLevel => _company.EnableMultiplePriceLevels && IsPartyGroup;

    // --------------------------------------------------------------- TDS / TCS (Phase 7 slice 1; catalog §13)

    /// <summary>True iff TDS is enabled for the company — the ledger-TDS fields are only offered then.</summary>
    public bool TdsEnabled => _company.TdsEnabled;

    /// <summary>True iff TCS is enabled for the company — the ledger-TCS fields are only offered then.</summary>
    public bool TcsEnabled => _company.TcsEnabled;

    /// <summary>True iff the TDS/TCS sub-form should render at all (either feature on). Gated for ER-13.</summary>
    public bool ShowTdsTcs => TdsEnabled || TcsEnabled;

    /// <summary>
    /// True iff the party deductee/collectee fields (deductee/collectee type + PAN + "deduct in same voucher")
    /// should render: a TDS/TCS feature is on AND the chosen group is a party group (Sundry Debtors/Creditors).
    /// </summary>
    public bool ShowPartyTdsTcs => ShowTdsTcs && IsPartyGroup;

    /// <summary>"Is TDS Applicable" for this (expense) ledger — a Journal/Payment line on it triggers withholding.</summary>
    [ObservableProperty] private bool _tdsApplicable;

    /// <summary>The default Nature of Payment (TDS section) for this ledger — "(none)" leaves it unset.</summary>
    [ObservableProperty] private NatureOfPaymentChoice? _selectedTdsNature;

    /// <summary>"Is TCS Applicable" for this (sales) ledger — a Sales line on it collects TCS on top.</summary>
    [ObservableProperty] private bool _tcsApplicable;

    /// <summary>The default Nature of Goods (§206C) for this ledger — "(none)" leaves it unset.</summary>
    [ObservableProperty] private NatureOfGoodsChoice? _selectedTcsNature;

    /// <summary>The party's deductee type (selects the §194C 1%/2% rate branch) — "(not set)" leaves it null.</summary>
    [ObservableProperty] private DeducteeTypeChoice? _selectedDeducteeType;

    /// <summary>The party's collectee type — "(not set)" leaves it null.</summary>
    [ObservableProperty] private CollecteeTypeChoice? _selectedCollecteeType;

    /// <summary>The party's PAN (validated on Create when set); drives §206AA/§206CC no-PAN rates. Blank ⇒ no PAN.</summary>
    [ObservableProperty] private string _partyPan = string.Empty;

    /// <summary>Whether TDS is deducted in the same voucher as the party payment (Tally's "deduct in same voucher").</summary>
    [ObservableProperty] private bool _deductTdsInSameVoucher;

    /// <summary>The Nature-of-Payment picker options ("(none)" + every defined nature).</summary>
    public IReadOnlyList<NatureOfPaymentChoice> TdsNatureChoices { get; }

    /// <summary>The Nature-of-Goods picker options ("(none)" + every defined nature).</summary>
    public IReadOnlyList<NatureOfGoodsChoice> TcsNatureChoices { get; }

    /// <summary>The deductee-type picker options ("(not set)" + every legal person).</summary>
    public IReadOnlyList<DeducteeTypeChoice> DeducteeTypeChoices { get; } = TdsTcsDisplay.DeducteeTypeChoices();

    /// <summary>The collectee-type picker options ("(not set)" + every legal person).</summary>
    public IReadOnlyList<CollecteeTypeChoice> CollecteeTypeChoices { get; } = TdsTcsDisplay.CollecteeTypeChoices();

    // --------------------------------------------------------------- party Mailing Details (WI-4; catalog §3)

    /// <summary>
    /// True iff the Mailing Details block should render: the chosen group resolves — through the full ancestry,
    /// not just the direct parent — to Sundry Debtors or Sundry Creditors. <b>Deliberately NOT feature-gated</b>:
    /// unlike <see cref="ShowPartyGst"/>, Tally's mailing block does not sit behind an F11 flag.
    /// </summary>
    public bool ShowMailingDetails => IsPartyGroup;

    /// <summary>"Mailing Name" — defaults from the ledger Name as it is typed, then becomes independently
    /// editable the moment the operator touches it (Tally's "Mailing Name (auto, editable)").</summary>
    [ObservableProperty] private string _mailingName = string.Empty;

    /// <summary>The party's postal address, free text; multi-line (each line prints on its own invoice line).</summary>
    [ObservableProperty] private string _mailingAddress = string.Empty;

    /// <summary>The party's country. Defaults to India, matching the Company block's "Country (India)".</summary>
    [ObservableProperty] private string _mailingCountry = "India";

    /// <summary>The party's PIN code — validated as a 6-digit Indian PIN on accept; blank ⇒ unset.</summary>
    [ObservableProperty] private string _mailingPincode = string.Empty;

    /// <summary>
    /// The party's State/UT as shown in the Mailing Details block. <b>This is the SAME field as the party-GST
    /// State</b> (<see cref="PartyState"/>): both the Mailing and the GST sub-forms bind this one property, which
    /// writes the single stored <c>PartyGstDetails.StateCode</c> through <c>Ledger.MailingStateCode</c>. Binding
    /// one property to both places is what makes it structurally impossible for the mailing State and the GST
    /// place-of-supply State to disagree — there is no second value to fall out of step.
    /// </summary>
    public IndianStateOption? MailingState
    {
        get => PartyState;
        set => PartyState = value;
    }

    /// <summary>True once the operator has edited the Mailing Name by hand; after that it stops tracking Name.</summary>
    private bool _mailingNameTouched;

    // --------------------------------------------------------------- alteration state (WI-3)

    /// <summary>
    /// The id of the ledger being ALTERED, or <see cref="Guid.Empty"/> when this screen is in Create mode. This is
    /// the <b>stable identity</b> the alteration saves against: a rename mutates this same ledger in place, so every
    /// historical voucher (which stores <c>ledger_id</c>, never the name) follows the rename automatically.
    /// </summary>
    private Guid _editingId = Guid.Empty;

    /// <summary>True iff this screen is altering an existing ledger rather than creating a new one — drives the
    /// screen title/caption and which verb <c>ActivateSelected</c> (Ctrl+A) runs.</summary>
    public bool IsAltering => _editingId != Guid.Empty;

    /// <summary>The ledger under alteration, or <c>null</c> in Create mode.</summary>
    public DomainLedger? EditingLedger => _editingId == Guid.Empty ? null : _company.FindLedger(_editingId);

    /// <summary>
    /// Opens this master in <b>Alter</b> mode over an existing ledger (WI-3): the same form, pre-filled from the
    /// ledger's current values, saving back against its stable Guid. Returns <c>null</c> if the id does not resolve.
    /// </summary>
    public static LedgerMasterViewModel? ForAlter(
        Company company, CompanyStorage storage, Guid ledgerId, Action onChanged)
    {
        ArgumentNullException.ThrowIfNull(company);
        if (company.FindLedger(ledgerId) is not { } ledger) return null;

        var vm = new LedgerMasterViewModel(company, storage, onChanged);
        vm._editingId = ledgerId;
        vm.LoadFrom(ledger);
        vm.OnPropertyChanged(nameof(IsAltering));
        return vm;
    }

    public LedgerMasterViewModel(Company company, CompanyStorage storage, Action onChanged)
    {
        _company = company ?? throw new ArgumentNullException(nameof(company));
        _storage = storage ?? throw new ArgumentNullException(nameof(storage));
        _onChanged = onChanged ?? throw new ArgumentNullException(nameof(onChanged));

        foreach (var g in company.Groups.OrderBy(g => g.Name, StringComparer.OrdinalIgnoreCase))
            Groups.Add(g);
        SelectedGroup = company.FindGroupByName("Sundry Debtors") ?? Groups.FirstOrDefault();

        _selectedPer = PerChoices[0];
        _selectedOnBalance = OnBalanceChoices[0];
        _selectedApplicability = ApplicabilityChoices[0];
        _selectedStyle = StyleChoices[0];

        CurrencyChoices = BuildCurrencyChoices(company);
        _selectedCurrency = CurrencyChoices[0]; // base currency
        _selectedMethod = MethodChoices[0]; // None (a plain expense)

        PartyStates = IndianState.All.Select(s => new IndianStateOption { State = s }).ToList();
        _partyRegistrationType = PartyRegistrationTypes[2]; // Unregistered (a plain B2C party) by default

        PriceLevelChoices = BuildPriceLevelChoices(company);
        _selectedPriceLevel = PriceLevelChoices[0]; // (none)

        TdsNatureChoices = TdsTcsDisplay.NatureOfPaymentChoices(company);
        TcsNatureChoices = TdsTcsDisplay.NatureOfGoodsChoices(company);
        _selectedTdsNature = TdsNatureChoices[0];         // (none)
        _selectedTcsNature = TcsNatureChoices[0];         // (none)
        _selectedDeducteeType = DeducteeTypeChoices[0];   // (not set)
        _selectedCollecteeType = CollecteeTypeChoices[0]; // (not set)

        RefreshList();
    }

    /// <summary>
    /// Builds the currency-picker list: the base currency first (stored as null), then each foreign
    /// currency (stored as its id). A base-only company shows just the one base option.
    /// </summary>
    private static IReadOnlyList<CurrencyChoice> BuildCurrencyChoices(Company company)
    {
        var list = new List<CurrencyChoice>();
        var baseCur = company.BaseCurrency;
        var baseLabel = baseCur is not null
            ? $"{baseCur.FormalName} ({baseCur.Symbol}) — base"
            : $"{company.BaseCurrencyName} ({company.BaseCurrencySymbol}) — base";
        list.Add(new CurrencyChoice(null, baseLabel));

        foreach (var c in company.Currencies
                     .Where(c => !c.IsBaseCurrency)
                     .OrderBy(c => c.FormalName, StringComparer.OrdinalIgnoreCase))
            list.Add(new CurrencyChoice(c.Id, $"{c.FormalName} ({c.Symbol})"));

        return list;
    }

    /// <summary>
    /// Builds the default-price-level picker list: "(none)" first (stored as null), then every defined price
    /// level. A company with no levels shows just the "(none)" option.
    /// </summary>
    private static IReadOnlyList<PriceLevelChoice> BuildPriceLevelChoices(Company company)
    {
        var list = new List<PriceLevelChoice> { new(null, "◦ (none)") };
        foreach (var level in company.PriceLevels.OrderBy(l => l.Name, StringComparer.OrdinalIgnoreCase))
            list.Add(new PriceLevelChoice(level.Id, level.Name));
        return list;
    }

    /// <summary>
    /// A party group (Sundry Debtors / Sundry Creditors, or a sub-group under one) — the bill-wise
    /// prompts are shown only for these, where "Maintain bill-by-bill" surfaces for party
    /// ledgers. When the chosen group is a party group the flag defaults on.
    /// </summary>
    public bool IsPartyGroup => SelectedGroup is not null && IsUnderParty(SelectedGroup);

    partial void OnSelectedGroupChanged(Group? value)
    {
        MaintainBillByBill = value is not null && IsUnderParty(value);
        OnPropertyChanged(nameof(IsPartyGroup));
        OnPropertyChanged(nameof(ShowPartyGst));
        OnPropertyChanged(nameof(IsDirectExpensesGroup));
        OnPropertyChanged(nameof(ShowAppropriation));
        OnPropertyChanged(nameof(ShowDefaultPriceLevel));
        OnPropertyChanged(nameof(ShowPartyTdsTcs));
        // WI-4: the Mailing Details block appears/disappears with the party-group test (ancestry-walking).
        OnPropertyChanged(nameof(ShowMailingDetails));
    }

    /// <summary>WI-4: the Mailing Name tracks the ledger Name until the operator edits it by hand ("auto,
    /// editable"). Once touched it stops following, so a deliberate mailing name is never silently overwritten.</summary>
    partial void OnNameChanged(string value)
    {
        if (!_mailingNameTouched) MailingName = value ?? string.Empty;
    }

    partial void OnMailingNameChanged(string value)
    {
        // Only a divergence from Name counts as a manual edit — the auto-fill above assigns them equal.
        if (!string.Equals(value ?? string.Empty, Name ?? string.Empty, StringComparison.Ordinal))
            _mailingNameTouched = true;
    }

    /// <summary>WI-4: the party State is a single value shared by the Mailing and GST sub-forms — notify both
    /// bindings whenever it changes so the two views can never display different States.</summary>
    partial void OnPartyStateChanged(IndianStateOption? value) => OnPropertyChanged(nameof(MailingState));

    partial void OnShowConfigurationChanged(bool value) => OnPropertyChanged(nameof(ShowAppropriation));

    /// <summary>F12 on the ledger master — toggles the ledger-screen configuration (reveals the
    /// Method-of-Appropriation field for a Direct-Expenses ledger).</summary>
    public void ToggleConfiguration() => ShowConfiguration = !ShowConfiguration;

    private bool IsUnderParty(Group group)
    {
        var g = group;
        var guard = 0;
        while (g is not null && guard++ < 64)
        {
            if (g.Name.Equals("Sundry Debtors", StringComparison.OrdinalIgnoreCase) ||
                g.Name.Equals("Sundry Creditors", StringComparison.OrdinalIgnoreCase))
                return true;
            g = g.ParentId is { } pid ? _company.FindGroup(pid) : null;
        }
        return false;
    }

    private bool IsUnderDirectExpenses(Group group)
    {
        var g = group;
        var guard = 0;
        while (g is not null && guard++ < 64)
        {
            if (g.Name.Equals("Direct Expenses", StringComparison.OrdinalIgnoreCase))
                return true;
            g = g.ParentId is { } pid ? _company.FindGroup(pid) : null;
        }
        return false;
    }

    /// <summary>
    /// Ctrl+A create: validates and builds a brand-new ledger through the SHARED
    /// <see cref="TryBuildInto"/> mapping, adds it and persists the company. Refreshes the list and clears the
    /// form for the next entry. Nothing is added to the company unless the build fully succeeded, so a rejected
    /// create can never leave a half-built ledger behind for <c>Save</c> to persist.
    /// </summary>
    public bool Create()
    {
        Message = null;
        if (SelectedGroup is null)
        {
            Message = "Pick an Under group.";
            return false;
        }

        // Opening balance defaults to 0; the natural side follows the group's nature
        // (Asset/Expense = Debit, Liability/Income = Credit) — the conventional default.
        var openingIsDebit = SelectedGroup.Nature is GroupNature.Asset or GroupNature.Expense;

        // Built detached: TryBuildInto validates everything and only then writes, and the ledger joins the company
        // solely on success — so a validation failure adds nothing and Save has nothing half-built to persist.
        var ledger = new DomainLedger(
            Guid.NewGuid(), "(pending)", SelectedGroup.Id, Money.Zero, openingIsDebit);

        if (!TryBuildInto(ledger)) return false;

        _company.AddLedger(ledger);
        _storage.Save(_company);

        RefreshList();
        var currencyNote = SelectedCurrency?.CurrencyId is null ? string.Empty
            : $" in {SelectedCurrency!.Display}";
        Message = $"Ledger '{ledger.Name}' created under {SelectedGroup.Name}{currencyNote}.";
        ResetForNextEntry();
        _onChanged();
        return true;
    }

    /// <summary>
    /// Ctrl+A <b>alter</b> (WI-3): re-validates and re-writes every field of the ledger this screen was opened
    /// over — resolved by its stable Guid — then persists. Because vouchers reference the ledger by that Guid and
    /// <c>Save</c> is a full-snapshot replace, a <b>rename applies retroactively</b>: every historical voucher and
    /// every report shows the new name immediately, with no re-pointing and no migration.
    ///
    /// <para>Uses the SAME <see cref="TryBuildInto"/> mapping as <see cref="Create"/> — deliberately, because a
    /// hand-written second mapping would drift, and a field omitted from it would silently WIPE that field the
    /// first time a user altered an unrelated one.</para>
    /// </summary>
    public bool Alter()
    {
        Message = null;
        if (_editingId == Guid.Empty)
        {
            Message = "This screen is not altering an existing ledger.";
            return false;
        }
        if (_company.FindLedger(_editingId) is not { } ledger)
        {
            Message = "The ledger being altered no longer exists.";
            return false;
        }
        if (SelectedGroup is null)
        {
            Message = "Pick an Under group.";
            return false;
        }

        // Alter-only guards: a reserved/predefined ledger may not be renamed (engine code resolves those by name
        // and would fail silently), and the new Under must exist.
        try
        {
            MasterAlterationRules.EnsureLedgerRenameAllowed(ledger, (Name ?? string.Empty).Trim());
            MasterAlterationRules.EnsureLedgerGroupValid(_company, SelectedGroup.Id);
        }
        catch (InvalidOperationException ex)
        {
            Message = ex.Message;
            return false;
        }

        // Warn — but do not block — when the re-group crosses the Balance-Sheet / P&L divide: that retroactively
        // reclassifies every historical transaction on this ledger and restates prior-period profit.
        var previousGroup = _company.FindGroup(ledger.GroupId);
        var reclassifies = previousGroup is not null
            && MasterAlterationRules.DescribesReclassification(_company, previousGroup, SelectedGroup);

        if (!TryBuildInto(ledger)) return false;

        _storage.Save(_company);
        RefreshList();

        Message = reclassifies
            ? $"Ledger '{ledger.Name}' altered — moved from {previousGroup!.Name} to {SelectedGroup.Name}, which " +
              "reclassifies its historical transactions between the Balance Sheet and Profit & Loss."
            : $"Ledger '{ledger.Name}' altered.";
        _onChanged();
        return true;
    }

    /// <summary>
    /// Loads an existing ledger's values INTO the form (the read direction of the alteration round-trip). Every
    /// field <see cref="TryBuildInto"/> writes is mirrored here; the two are kept adjacent on purpose, because an
    /// omission here shows the user a default, and accepting then writes that default back — silent data loss.
    /// </summary>
    public void LoadFrom(DomainLedger ledger)
    {
        ArgumentNullException.ThrowIfNull(ledger);

        SelectedGroup = Groups.FirstOrDefault(g => g.Id == ledger.GroupId) ?? SelectedGroup;
        Name = ledger.Name;

        MaintainBillByBill = ledger.MaintainBillByBill;
        DefaultCreditPeriodText = ledger.DefaultCreditPeriodDays?.ToString() ?? string.Empty;

        var interest = ledger.Interest;
        EnableInterest = interest is { Enabled: true };
        InterestRateText = interest is null ? string.Empty
            : interest.RatePercent.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture);
        if (interest is not null)
        {
            SelectedPer = PerChoices.FirstOrDefault(c => c.Value == interest.Per) ?? PerChoices[0];
            SelectedOnBalance = OnBalanceChoices.FirstOrDefault(c => c.Value == interest.OnBalance) ?? OnBalanceChoices[0];
            SelectedApplicability = ApplicabilityChoices.FirstOrDefault(c => c.Value == interest.Applicability) ?? ApplicabilityChoices[0];
            SelectedStyle = StyleChoices.FirstOrDefault(c => c.Value == interest.Style) ?? StyleChoices[0];
        }

        SelectedCurrency = CurrencyChoices.FirstOrDefault(c => c.CurrencyId == ledger.CurrencyId) ?? CurrencyChoices[0];
        SelectedMethod = MethodChoices.FirstOrDefault(m => m.Value == ledger.MethodOfAppropriation) ?? MethodChoices[0];
        SelectedPriceLevel = PriceLevelChoices.FirstOrDefault(p => p.PriceLevelId == ledger.DefaultPriceLevelId)
            ?? PriceLevelChoices[0];

        // Party GST. The State is read through the SINGLE stored value (MailingStateCode == PartyGst.StateCode),
        // so the Mailing and GST sub-forms are loaded from one source and open in agreement by construction.
        var gst = ledger.PartyGst;
        PartyGstin = gst?.Gstin ?? string.Empty;
        PartyRegistrationType = PartyRegistrationTypes.FirstOrDefault(
            t => t.Value == (gst?.RegistrationType ?? GstRegistrationType.Unregistered)) ?? PartyRegistrationTypes[2];
        PartyState = PartyStates.FirstOrDefault(s => s.Code == ledger.MailingStateCode);

        // WI-4 party Mailing Details.
        var mailing = ledger.Mailing;
        MailingAddress = mailing?.Address ?? string.Empty;
        MailingCountry = mailing?.Country ?? string.Empty;
        MailingPincode = mailing?.Pincode ?? string.Empty;
        // Assign the mailing name LAST and mark it touched, so the Name-tracking auto-fill cannot overwrite a
        // stored mailing name that deliberately differs from the ledger name.
        MailingName = mailing?.MailingName ?? ledger.Name;
        _mailingNameTouched = !string.Equals(MailingName, ledger.Name, StringComparison.Ordinal);

        // TDS / TCS.
        TdsApplicable = ledger.TdsApplicable;
        SelectedTdsNature = TdsNatureChoices.FirstOrDefault(c => c.NatureId == ledger.TdsNatureOfPaymentId)
            ?? TdsNatureChoices[0];
        TcsApplicable = ledger.TcsApplicable;
        SelectedTcsNature = TcsNatureChoices.FirstOrDefault(c => c.NatureId == ledger.TcsNatureOfGoodsId)
            ?? TcsNatureChoices[0];
        SelectedDeducteeType = DeducteeTypeChoices.FirstOrDefault(c => c.Value == ledger.DeducteeType)
            ?? DeducteeTypeChoices[0];
        SelectedCollecteeType = CollecteeTypeChoices.FirstOrDefault(c => c.Value == ledger.CollecteeType)
            ?? CollecteeTypeChoices[0];
        PartyPan = ledger.PartyPan ?? string.Empty;
        DeductTdsInSameVoucher = ledger.DeductTdsInSameVoucher;
    }

    /// <summary>
    /// The <b>single</b> form → ledger mapping, shared by <see cref="Create"/> and <see cref="Alter"/>. Validates
    /// everything FIRST (returning false with a <see cref="Message"/> and writing nothing), then writes every
    /// captured field onto <paramref name="target"/>.
    ///
    /// <para>Opening balance and its Dr/Cr side are deliberately NOT written here: they are set once at creation
    /// from the group's nature, and an alteration must not silently restate a prior period. They therefore survive
    /// an alter untouched.</para>
    /// </summary>
    private bool TryBuildInto(DomainLedger target)
    {
        if (SelectedGroup is null)
        {
            Message = "Pick an Under group.";
            return false;
        }

        // Name: required, and unique EXCLUDING the ledger being altered. Without the exclusion, altering any
        // unrelated field on an existing ledger would fail with "a ledger named 'X' already exists" — the ledger
        // colliding with itself.
        string name;
        try
        {
            name = MasterAlterationRules.EnsureNameAvailable(_company, Name, _editingId, MasterKind.Ledger);
        }
        catch (InvalidOperationException ex)
        {
            Message = ex.Message;
            return false;
        }

        // Parse the optional default credit period; a non-empty non-numeric value is an error.
        int? creditDays = null;
        var creditText = (DefaultCreditPeriodText ?? string.Empty).Trim();
        if (!string.IsNullOrEmpty(creditText))
        {
            if (!int.TryParse(creditText, out var days) || days < 0)
            {
                Message = "Default credit period must be a whole number of days (≥ 0), or blank.";
                return false;
            }
            creditDays = days;
        }

        // Build the optional interest-parameter block when interest is activated.
        InterestParameters? interest = null;
        if (EnableInterest)
        {
            var rateText = (InterestRateText ?? string.Empty).Trim();
            if (!decimal.TryParse(rateText, out var rate) || rate < 0m)
            {
                Message = "Interest rate % must be a number ≥ 0.";
                return false;
            }

            interest = new InterestParameters(
                enabled: true,
                ratePercent: rate,
                per: (SelectedPer ?? PerChoices[0]).Value,
                onBalance: (SelectedOnBalance ?? OnBalanceChoices[0]).Value,
                applicability: (SelectedApplicability ?? ApplicabilityChoices[0]).Value,
                style: (SelectedStyle ?? StyleChoices[0]).Value);
        }

        // Party GST details (only for a party ledger while GST is enabled). Pre-validate the GSTIN so the
        // engine's domain error never fires; a Regular party requires a GSTIN.
        PartyGstDetails? partyGst = null;
        if (ShowPartyGst)
        {
            var pGstin = (PartyGstin ?? string.Empty).Trim().ToUpperInvariant();
            var pGstinOrNull = string.IsNullOrEmpty(pGstin) ? null : pGstin;
            var regType = (PartyRegistrationType ?? PartyRegistrationTypes[2]).Value;

            if (pGstinOrNull is not null && !Gstin.IsValid(pGstinOrNull))
            {
                Message = $"'{pGstinOrNull}' is not a valid party GSTIN (15 chars, checksum failed).";
                return false;
            }
            if (regType == GstRegistrationType.Regular && pGstinOrNull is null)
            {
                Message = "A Regular GST party requires a GSTIN (or pick Unregistered/Consumer).";
                return false;
            }

            // Attach a details block only when something meaningful was captured (a GSTIN, a non-default
            // registration type, or a state) — otherwise leave it null (a plain B2C party). The last clause is
            // the same asymmetry guard as the IsPromoter/IsBodyCorporate carry-over below: this screen cannot
            // SHOW the RCM qualifiers, so a party that carries one must keep its block even when the three
            // visible fields are cleared back to their defaults — otherwise clearing the GSTIN would silently
            // drop a qualifier the operator never saw.
            if (pGstinOrNull is not null || regType != GstRegistrationType.Unregistered || PartyState is not null
                || target.PartyGst is { IsPromoter: true } or { IsBodyCorporate: true })
            {
                partyGst = new PartyGstDetails
                {
                    Gstin = pGstinOrNull,
                    RegistrationType = regType,
                    StateCode = PartyState?.Code,
                    // This screen does not capture the RCM qualifiers, so an ALTER must carry the target's existing
                    // values across. Rebuilding the block from the form alone would silently wipe them — the exact
                    // asymmetric-mapping data loss this shared builder exists to prevent.
                    IsPromoter = target.PartyGst?.IsPromoter ?? false,
                    IsBodyCorporate = target.PartyGst?.IsBodyCorporate ?? false,
                };
            }
        }

        // WI-4 party Mailing Details — captured for any party ledger (NOT feature-gated, unlike party GST).
        PartyMailingDetails? mailing = null;
        if (ShowMailingDetails)
        {
            mailing = new PartyMailingDetails
            {
                MailingName = MailingName,
                Address = MailingAddress,
                Country = MailingCountry,
                Pincode = MailingPincode,
                // No State here by design — the party State is PartyGst.StateCode, written once from PartyState
                // above and surfaced to this block through Ledger.MailingStateCode.
            };
            mailing.Normalize();
            try
            {
                mailing.EnsureValid();
            }
            catch (ArgumentException ex)
            {
                Message = ex.Message;
                return false;
            }

            // A Mailing Name that merely mirrors the ledger Name is the AUTO-FILL, not a captured value — storing
            // it would give every party ledger a non-empty mailing block the moment the screen rendered, changing
            // the persisted and exported bytes of companies that never touch an address (ER-13). Printing already
            // falls back to the ledger's own Name when this is null, so dropping it loses nothing.
            if (string.Equals(mailing.MailingName, name, StringComparison.Ordinal))
                mailing.MailingName = null;

            // A block where nothing was actually typed stays null, so an untouched party ledger persists and
            // exports byte-identically to a pre-v45 ledger (ER-13).
            if (mailing.IsEmpty) mailing = null;
        }

        // "Method of Appropriation in Purchase invoice" — captured only when the F12 configuration is on AND
        // the ledger is under Direct Expenses; a non-null value marks it an additional-cost ledger (RQ-16..RQ-20).
        // NOT defaulted to null when the sub-form is hidden — see THE HIDDEN-SUB-FORM RULE at the write block.
        MethodOfAppropriation? methodOfAppropriation = ShowAppropriation ? SelectedMethod?.Value : null;

        // Party default Price Level (RQ-30) — captured only when the F11 flag is on AND the ledger is a party;
        // null (no default) for every non-price-level or non-party ledger (ER-13). Same hidden-sub-form rule.
        Guid? defaultPriceLevelId = ShowDefaultPriceLevel ? SelectedPriceLevel?.PriceLevelId : null;

        // TDS/TCS party PAN (Phase 7 slice 1) — pre-validate a non-empty PAN so the domain error never fires.
        // A blank PAN is a no-PAN party (§206AA/§206CC higher rate applies at compute; captured null here).
        string? partyPanOrNull = null;
        if (ShowPartyTdsTcs)
        {
            var pan = Pan.Normalize(PartyPan ?? string.Empty);
            if (pan.Length > 0)
            {
                if (!Pan.IsValid(pan))
                {
                    Message = $"'{pan}' is not a valid PAN (5 letters + 4 digits + 1 letter, e.g. AAPFU0939F).";
                    return false;
                }
                partyPanOrNull = pan;
            }
        }

        // ---- everything validated; only now write. Nothing above this line mutates the target. ----
        //
        // 🔴 THE HIDDEN-SUB-FORM RULE — READ BEFORE ADDING A FIELD HERE.
        // A sub-form that did not RENDER captured nothing, so writing its "value" is writing a DEFAULT over data
        // the user could not even see. Every conditionally-rendered block below is therefore written INSIDE its
        // own `if (Show…)` guard: hidden ⇒ the target keeps what it already had.
        //
        // This is not hypothetical. Before this guard, `target.PartyGst = partyGst;` ran unconditionally while
        // `partyGst` was only BUILT `if (ShowPartyGst)` (= GstEnabled && IsPartyGroup). Altering a party ledger in
        // a company whose GST flag is off — e.g. F11 toggled off after setup, or an Io import into a non-GST
        // company — therefore nulled the entire party GST block, and the MailingStateCode write below then
        // re-materialised a FRESH one carrying only the State at its default RegistrationType. Editing nothing
        // but the address turned Gstin=19AAACT2727Q1ZW / Regular / IsBodyCorporate=true into
        // Gstin=null / Unregistered / IsBodyCorporate=false: the GSTIN vanished from invoice printing and the
        // party silently flipped B2B → B2C for GSTR-1, with no error. Locked by
        // HiddenSubFormPreservationTests.Altering_a_party_while_GST_is_disabled_preserves_the_whole_GST_block.
        //
        // In CREATE mode `target` is a brand-new ledger whose every one of these fields is already at its default,
        // so skipping the write is byte-identical to writing the default (ER-13).

        target.Name = name;
        target.GroupId = SelectedGroup.Id;
        target.MaintainBillByBill = MaintainBillByBill;
        target.DefaultCreditPeriodDays = MaintainBillByBill ? creditDays : null;
        target.Interest = interest;
        // "Currency of ledger" — null (base ₹/INR) for every existing ledger; a foreign currency
        // makes this a forex ledger whose lines carry forex amounts + rates.
        target.CurrencyId = SelectedCurrency?.CurrencyId;

        // Party GST — hidden-sub-form rule. GST off (or a non-party group) ⇒ the block was never on screen and is
        // left EXACTLY as it was, GSTIN / registration type / RCM qualifiers and all.
        if (ShowPartyGst) target.PartyGst = partyGst;

        // Party Mailing Details — same rule (gated on IsPartyGroup only; it is not F11-gated).
        if (ShowMailingDetails) target.Mailing = mailing;

        // The party's State/UT — written LAST and through the single accessor, because the Mailing block offers it
        // even when GST is switched OFF (in which case the partyGst block built above is null and would otherwise
        // have thrown the State away). MailingStateCode materialises a PartyGstDetails carrying just the State, so
        // there is still exactly ONE stored State and it is the same value that drives place of supply.
        if (ShowMailingDetails)
            target.MailingStateCode = PartyState?.Code;

        // Method of Appropriation — hidden-sub-form rule, and the reason it matters MOST here: ShowAppropriation
        // includes ShowConfiguration, the F12 toggle, which starts FALSE on every freshly-opened screen. Writing
        // unconditionally therefore wiped the appropriation method of every additional-cost ledger on every alter
        // that did not happen to press F12 first — no unusual company configuration required.
        if (ShowAppropriation) target.MethodOfAppropriation = methodOfAppropriation;

        // Party default Price Level — hidden-sub-form rule; wiped on alter whenever the F11 "multiple price
        // levels" flag was off (or the ledger is not a party), i.e. exactly when the picker was not rendered.
        if (ShowDefaultPriceLevel) target.DefaultPriceLevelId = defaultPriceLevelId;

        // TDS applicability (expense side) + party deductee details (Phase 7 slice 1). Captured only when TDS is
        // enabled; every field stays at its default (false / null) otherwise, so a non-TDS ledger is byte-identical.
        if (TdsEnabled)
        {
            target.TdsApplicable = TdsApplicable;
            target.TdsNatureOfPaymentId = SelectedTdsNature?.NatureId;
            if (ShowPartyTdsTcs)
            {
                target.DeducteeType = SelectedDeducteeType?.Value;
                target.DeductTdsInSameVoucher = DeductTdsInSameVoucher;
            }
        }
        // TCS applicability (sales side) + party collectee details. Same gating discipline.
        if (TcsEnabled)
        {
            target.TcsApplicable = TcsApplicable;
            target.TcsNatureOfGoodsId = SelectedTcsNature?.NatureId;
            if (ShowPartyTdsTcs)
                target.CollecteeType = SelectedCollecteeType?.Value;
        }
        // PAN is shared by both deductee/collectee roles; set it whenever a party PAN was captured.
        if (ShowPartyTdsTcs)
            target.PartyPan = partyPanOrNull;

        // NOT written, on purpose — this screen does not own them, so an ALTER must leave them exactly as they
        // were: OpeningBalance / OpeningIsDebit (restating a prior period is not a side effect of editing a name),
        // Alias, IsPredefined, SalesPurchaseGst, GstClassification and TdsTcsClassification (engine-managed tags).
        return true;
    }

    /// <summary>Clears the form after a successful CREATE so the next entry starts blank (an ALTER keeps the
    /// values on screen, because the user is looking at the master they just saved).</summary>
    private void ResetForNextEntry()
    {
        Name = string.Empty;
        DefaultCreditPeriodText = string.Empty;
        EnableInterest = false;
        InterestRateText = string.Empty;
        SelectedCurrency = CurrencyChoices[0]; // reset to base for the next entry
        SelectedMethod = MethodChoices[0];     // reset to None for the next entry
        PartyGstin = string.Empty;
        PartyRegistrationType = PartyRegistrationTypes[2]; // back to Unregistered
        PartyState = null;
        SelectedPriceLevel = PriceLevelChoices[0];         // reset to (none)
        TdsApplicable = false;
        SelectedTdsNature = TdsNatureChoices[0];           // (none)
        TcsApplicable = false;
        SelectedTcsNature = TcsNatureChoices[0];           // (none)
        SelectedDeducteeType = DeducteeTypeChoices[0];     // (not set)
        SelectedCollecteeType = CollecteeTypeChoices[0];   // (not set)
        PartyPan = string.Empty;
        DeductTdsInSameVoucher = false;
        // WI-4: clear the mailing block and re-arm the Mailing-Name auto-fill for the next ledger.
        MailingAddress = string.Empty;
        MailingCountry = "India";
        MailingPincode = string.Empty;
        _mailingNameTouched = false;
        MailingName = string.Empty;
    }

    /// <summary>A short human summary of a ledger's interest block ("18% p.a. Simple"), or blank.</summary>
    private static string DescribeInterest(DomainLedger l)
    {
        var p = l.Interest;
        if (p is null || !p.Enabled) return string.Empty;
        var perLabel = p.Per switch
        {
            InterestPer.ThreeSixtyFiveDayYear => "p.a.",
            InterestPer.CalendarYear => "p.a.",
            InterestPer.ThirtyDayMonth => "p.m.-basis",
            InterestPer.CalendarMonth => "p.m.-basis",
            _ => string.Empty,
        };
        var style = p.Style == InterestStyle.Compound ? "Compound" : "Simple";
        return $"{p.RatePercent:0.##}% {perLabel} {style}".Trim();
    }

    private void RefreshList()
    {
        Existing.Clear();
        foreach (var l in _company.Ledgers.OrderBy(l => l.Name, StringComparer.OrdinalIgnoreCase))
        {
            var group = _company.FindGroup(l.GroupId);
            var opening = l.OpeningBalance == Money.Zero
                ? string.Empty
                : $"{IndianFormat.Amount(l.OpeningBalance)} {(l.OpeningIsDebit ? "Dr" : "Cr")}";
            var currency = l.CurrencyId is { } cid && _company.FindCurrency(cid) is { } cur
                ? cur.FormalName
                : string.Empty;
            Existing.Add(new LedgerListRow
            {
                Name = l.Name,
                Under = group?.Name ?? "(P&L)",
                Opening = opening,
                Interest = DescribeInterest(l),
                Currency = currency,
            });
        }
    }
}
