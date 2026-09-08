using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Reports;
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

/// <summary>One cheque book in the bank ledger master's Cheque Books list (census 8.5; schema v57). The id is
/// carried so Remove acts on the book itself rather than on a display string that two books could share.</summary>
public sealed class ChequeBookListItem
{
    /// <summary>The stored cheque book's id.</summary>
    public Guid Id { get; init; }

    /// <summary>"Name — from to to (N leaves)", the one-line summary the list shows.</summary>
    public string Display { get; init; } = string.Empty;
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

    // ------------------------------------------------------------ census 10.1: the Credit Limits block

    /// <summary>
    /// <b>Census 10.1 — "Credit Limit"</b>, typed as text; <b>blank ⇒ NO limit</b>, which is not the same answer as
    /// <c>0</c>.
    ///
    /// <para>🔴 <b>Blank and "0" are DIFFERENT and this screen keeps them apart.</b> A limit of zero is a real,
    /// blocking value — "this party may take nothing on credit" — so it cannot double as "unset". Typing <c>0</c>
    /// stores a limit of zero and every credit sale to the party will then be refused at save; clearing the box
    /// stores NULL and nothing is checked. That distinction is why <c>credit_limit_paisa</c> is the one v54 column
    /// declared NULLable with no DEFAULT.</para>
    ///
    /// <para><b>ATTESTED</b> (help.tallysolutions.com): the field caption, and that limits belong to ledgers
    /// <i>"created under the groups Sundry Debtors and Sundry Creditors"</i> — hence <see cref="IsPartyGroup"/>
    /// gates the whole block.</para>
    /// </summary>
    [ObservableProperty] private string _creditLimitText = string.Empty;

    /// <summary>
    /// <b>Census 10.1 — "Check For Credit Dates During Voucher Entry".</b> ATTESTED caption.
    ///
    /// <para>⚠️ <b>Stored, but nothing consumes it yet, and the screen SAYS so</b> (see
    /// <see cref="CreditDaysNotice"/>). The vendor's credit-DAYS check is a <i>warning</i>, a different severity
    /// from the amount limit's hard block, and it is not built in this wave. A switch that silently did nothing
    /// would be the dead-capability defect this project has already filed twice; a switch that says what it does
    /// not yet do is honest.</para>
    /// </summary>
    [ObservableProperty] private bool _checkCreditDaysOnEntry;

    /// <summary>
    /// <b>Census 10.1 — "Override credit limit using post-dated transactions".</b> ATTESTED caption, and one of the
    /// vendor's exactly two named escapes from a breach. Live: with it on, a post-dated voucher for this party is
    /// exempt from the limit (<c>CreditLimitRules.Check</c>).
    /// </summary>
    [ObservableProperty] private bool _overrideCreditLimitWithPostDated;

    /// <summary>
    /// The standing notice under the Credit Limits block. Two jobs, both of them about not over-claiming:
    /// it states that the credit-DAYS warning is not built yet, and — the R-9 trap — that the credit period is
    /// <b>discarded</b> when bill-wise is off, because <see cref="ApplyTo"/> writes <c>null</c> to
    /// <c>DefaultCreditPeriodDays</c> in that case. Without the second half an operator sets a period, switches
    /// bill-wise off, saves, and silently loses it.
    /// </summary>
    public string CreditDaysNotice =>
        MaintainBillByBill
            ? "A blank limit means no limit; 0 is a real limit that blocks every credit sale. "
            + "The credit-days check is recorded but does not warn yet."
            : "A blank limit means no limit; 0 is a real limit that blocks every credit sale. "
            + "Credit period needs bill-by-bill balances — with it off, the period is not kept.";

    partial void OnMaintainBillByBillChanged(bool value) => OnPropertyChanged(nameof(CreditDaysNotice));

    // --------------------------------------------------------------- opening balance (Study Guide pp.65–66)

    /// <summary>
    /// "<b>Opening Balance</b>" (TallyPrime Study Guide pp.65–66; catalog §5) — the magnitude the ledger was
    /// carrying on day one, typed as text; blank ⇒ nil. The Dr/Cr side lives in <see cref="OpeningIsDebit"/>,
    /// exactly mirroring the domain's <see cref="DomainLedger.OpeningBalance"/> / <c>OpeningIsDebit</c> pair.
    ///
    /// <para>This is the field that lets a set of books be <b>opened</b>. Both the domain and the SQLite store have
    /// carried an opening since v1 (<c>ledgers.opening_balance_paisa</c> / <c>opening_is_debit</c>) and the Balance
    /// Sheet, Trial Balance and every closing balance already read it through <see cref="DomainLedger.SignedOpening"/>
    /// — but the CREATION screen never captured one, hard-coding <see cref="Money.Zero"/>, so an accountant
    /// migrating onto the app had no way to state day-one balances. Purely a UI gap; no schema change.</para>
    ///
    /// <para>Not feature-gated: Tally shows Opening Balance on every ledger screen unconditionally, so no F11
    /// capability and no F12 visibility toggle stands between the operator and it.</para>
    /// </summary>
    [ObservableProperty] private string _openingBalanceText = string.Empty;

    /// <summary>
    /// The opening's <b>Dr/Cr side</b> (Study Guide p.66: "along with Dr/Cr depending on the nature"). Proposed from
    /// the chosen group's nature (Asset/Expense ⇒ Dr, Liability/Income ⇒ Cr) and thereafter follows a re-pick of the
    /// Under group — until the operator states it by hand, after which it stops tracking. That last clause is what
    /// protects a deliberate CONTRA opening (an overdrawn bank; a debit balance sitting with a creditor) from being
    /// silently flipped back by an unrelated change of group.
    /// </summary>
    /// <remarks>
    /// Hand-written rather than <c>[ObservableProperty]</c> on purpose. The latch below must fire on ASSIGNMENT, and
    /// the generated setter suppresses its callback whenever the assigned value equals the current one — so an
    /// operator who picks the side that was already PROPOSED (picking "Cr" under Sundry Creditors) would never latch
    /// it, and a later re-pick of the Under group would silently move a side the operator had explicitly stated.
    /// Confirming a proposal is a statement, not a no-op.
    /// </remarks>
    public bool OpeningIsDebit
    {
        get => _openingIsDebit;
        set
        {
            if (!_syncingOpeningSide) _openingSideTouched = true;
            if (_openingIsDebit == value) return;
            _openingIsDebit = value;
            OnPropertyChanged();
            // Keep the Dr/Cr picker's projection in step — notably for the nature proposal and the LoadFrom
            // pre-fill, neither of which goes through the picker.
            OnPropertyChanged(nameof(OpeningSide));
        }
    }

    private bool _openingIsDebit = true;

    /// <summary>True once the operator has set the Dr/Cr side by hand; after that it stops following the group.</summary>
    private bool _openingSideTouched;

    /// <summary>The two sides offered by the Dr/Cr picker — the same pair, in the same order, as a voucher line's.</summary>
    public IReadOnlyList<DrCr> OpeningSides { get; } = new[] { DrCr.Debit, DrCr.Credit };

    /// <summary>
    /// The opening's side as a <see cref="DrCr"/>, for the picker. This is a PROJECTION over
    /// <see cref="OpeningIsDebit"/>, not a second stored value: there is exactly one side in this view model and
    /// both spellings of it read and write that one bool, so the picker and the domain can never disagree. Same
    /// device as <c>MailingState</c> over <c>PartyState</c>, and for the same reason.
    /// </summary>
    public DrCr OpeningSide
    {
        get => OpeningIsDebit ? DrCr.Debit : DrCr.Credit;
        set => OpeningIsDebit = value == DrCr.Debit;
    }

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

    /// <summary>
    /// True iff the bill-wise block ("Maintain balances bill-by-bill" + the default credit period) should
    /// render: the company's F11 → Accounting <b>"Enable Bill-wise entry"</b> feature is on AND the chosen
    /// group is a party group. Census row 1.7 — before this gate the block answered to the group alone and the
    /// company feature had no door at all.
    /// </summary>
    public bool ShowBillWiseOptions => _company.EnableBillWiseEntry && IsPartyGroup;

    /// <summary>
    /// True iff the "Activate Interest Calculation" block should render — the company's F11 → Accounting
    /// <b>"Enable Interest Calculation"</b> feature is on. Unlike the bill-wise block this one is not
    /// party-only: the vendor offers interest parameters on any ledger, so only the company feature gates it.
    /// </summary>
    public bool ShowInterestBlock => _company.EnableInterestCalculation;

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

    // --------------------------------------------------------------- cheque printing (catalog §8; census 8.4)

    /// <summary>
    /// True iff the <b>Cheque Printing</b> block should render: the chosen group resolves — through the full
    /// ancestry — to Bank Accounts or Bank OD A/c. Asked of the GROUP, not of a saved ledger, so the block is
    /// offered while the bank ledger is still being created.
    ///
    /// <para><b>Vendor grounding.</b> <c>help.tallysolutions.com/cheque-payments-set-up/</c>, section "Specify
    /// Cheque Range and Format in Bank Ledger" — the cheque-printing settings are captured on the BANK LEDGER
    /// master, which is the screen this block belongs to.</para>
    ///
    /// <para><b>🔴 Why this exists.</b> <c>Ledger.EnableChequePrinting</c> and
    /// <c>Ledger.ChequePrintingBankName</c> are schema-v5 columns that persisted, exported and imported for
    /// nine phases with <b>no way for any operator to set either of them</b> — seventeen references across
    /// Domain / Io / Sqlite and not one in <c>src/Apex.Desktop</c>. Until this block existed, the Cheque
    /// Printing report was structurally empty for every real company.</para>
    /// </summary>
    public bool ShowChequePrinting => ClassificationRules.IsBankGroup(SelectedGroup?.Id, _company);

    /// <summary>
    /// "Enable Cheque Printing" (<c>help.tallysolutions.com/cheque-payments-set-up/</c>). A payment drawn on this
    /// bank by Cheque/DD appears on the Cheque Printing report only while this is on — off is the state of every
    /// ledger that existed before this block, so an untouched company persists byte-identically.
    /// </summary>
    [ObservableProperty] private bool _enableChequePrinting;

    /// <summary>"Name of Bank" as it should read on cheque stationery — free text, blank ⇒ the ledger's own name
    /// is used. Captured only when <see cref="EnableChequePrinting"/> is on.</summary>
    [ObservableProperty] private string _chequePrintingBankName = string.Empty;

    // ------------------------------------------------------------- Cheque Dimensions + bank identity (v57)

    /// <summary>
    /// True iff the <b>Cheque Dimensions</b> sub-block should render: a bank ledger with cheque printing enabled.
    ///
    /// <para><b>🔴 THIS BLOCK IS THE ONLY WRITER OF <c>Ledger.ChequeLayout</c> IN THE ENTIRE PRODUCT, AND THAT IS
    /// WHY IT EXISTS.</b> <c>ChequeLayout</c>, <c>ChequePrintData</c>, <c>ChequePdf</c> and
    /// <c>ChequePrintProjector</c> shipped correct, tested and deterministic — and completely DEAD: nothing
    /// assigned a layout, nothing persisted one, so <c>ChequePdf.Validate</c> refused every render with "Cheque
    /// dimensions are not set for this bank" and no operator could ever print a cheque leaf. Deleting this block
    /// would make ~625 lines of shipped code unreachable again.</para>
    ///
    /// <para><b>Vendor grounding.</b> <c>help.tallysolutions.com/cheque-payments-set-up/</c>, "Specify User
    /// Defined Cheque Format" (<b>Alt+L</b>) — the dimensions are captured on the BANK LEDGER master. The field
    /// list is
    /// <c>help.tallysolutions.com/docs/te9rel51/Advanced_Features/Advanced_Accounting_Features/Creation_Mode.htm</c>,
    /// "Cheque Dimensions".</para>
    /// </summary>
    public bool ShowChequeDimensions => ShowChequePrinting && EnableChequePrinting;

    /// <summary>
    /// When the <b>Bank Identity</b> block (account number / branch / IFSC) is on screen. Two audiences, one set
    /// of columns:
    /// <list type="bullet">
    ///   <item>a <b>BANK</b> ledger, whose own account identifies the company on a deposit slip and a payment
    ///     advice — the pre-existing case, unchanged;</item>
    ///   <item>a <b>PARTY</b> ledger (Sundry Debtor / Creditor), whose account is the <b>beneficiary's</b> and is
    ///     what a bank payment-instruction file pays to (census row 8.10).</item>
    /// </list>
    ///
    /// <para>🔴 <b>The party half is what makes row 8.10 reachable at all, and it was measured.</b>
    /// <c>ledgers.bank_account_number</c> and <c>ledgers.bank_ifsc</c> have existed since v57 on EVERY ledger, but
    /// the only block that wrote them was nested inside the cheque-dimensions panel — visible solely for a bank
    /// group with cheque printing switched ON. So no operator could record a supplier's account number, every
    /// e-payment would have been stuck in "Incomplete/Incorrect Transaction Details" for ever, and the report
    /// would have been the third dead feature filed on this project. No schema change was needed to fix it: the
    /// columns were always there, the door was not.</para>
    ///
    /// <para>Vendor grounding for the party half: <c>help.tallysolutions.com/e-payments-report/</c>, whose
    /// exception bucket is <i>"Missing or incorrect party's bank details such as Account No. or IFS Code"</i> —
    /// the reference product holds these on the party master too.</para>
    /// </summary>
    public bool ShowBankIdentity => ShowChequeDimensions || IsPartyGroup;

    /// <summary>The Bank Identity block's heading, which names WHOSE account is being captured. A party ledger's
    /// account is the beneficiary's; captioning it "Bank Identity" would invite an operator to key the company's
    /// own account onto a supplier.</summary>
    public string BankIdentityHeading => IsPartyGroup
        ? "Beneficiary Bank Details (e-payment instructions)"
        : "Bank Identity (deposit slip / payment advice)";

    // Bank identity — help.tallysolutions.com/deposit-slips/, "Cash Deposit Slip" (Account Number, Bank Name,
    // Branch Name) and help.tallysolutions.com/payment-advice/ (the bank-transfer block's IFSC). These have no
    // derivation anywhere in the books, which is why the Deposit Slip could not be built before they existed.

    /// <summary>"Account Number" as printed on a deposit slip. Blank ⇒ not captured.</summary>
    [ObservableProperty] private string _bankAccountNumber = string.Empty;

    /// <summary>"Branch Name" as printed on a deposit slip. Blank ⇒ not captured.</summary>
    [ObservableProperty] private string _bankBranch = string.Empty;

    /// <summary>The bank's IFSC, printed in the payment advice's bank-transfer block. Blank ⇒ not captured.</summary>
    [ObservableProperty] private string _bankIfsc = string.Empty;

    // The geometry. 🔴 EVERY ONE OF THESE IS TYPED AND DISPLAYED IN MILLIMETRES, because the vendor's screen is in
    // millimetres — and STORED as tenths of a millimetre in an int, because a double millimetre would render two
    // different byte streams on two machines (see Domain/ChequeLayout.cs). The conversion happens exactly twice,
    // in TryReadTmm and FormatTmm below, and nowhere else.

    /// <summary>Physical leaf width, mm. 0/blank ⇒ not set, and printing is refused rather than guessed.</summary>
    [ObservableProperty] private string _chequeLeafWidthMm = string.Empty;

    /// <summary>Physical leaf height, mm. 0/blank ⇒ not set, and printing is refused rather than guessed.</summary>
    [ObservableProperty] private string _chequeLeafHeightMm = string.Empty;

    /// <summary>Cheque Date — "Distance of Line from Top Edge", mm.</summary>
    [ObservableProperty] private string _chequeDateTopMm = string.Empty;

    /// <summary>Cheque Date — "Starting location from left Edge", mm.</summary>
    [ObservableProperty] private string _chequeDateLeftMm = string.Empty;

    /// <summary>Cheque Date — "Distance between Characters", mm: the pitch of the NPCI boxed date field.</summary>
    [ObservableProperty] private string _chequeDateCharPitchMm = string.Empty;

    /// <summary>Party's Payee Name — "Distance of Line from Top Edge", mm.</summary>
    [ObservableProperty] private string _chequePayeeTopMm = string.Empty;

    /// <summary>Party's Payee Name — "Starting Location from Left Edge", mm.</summary>
    [ObservableProperty] private string _chequePayeeLeftMm = string.Empty;

    /// <summary>Party's Payee Name — "Width area", mm (vendor default 135).</summary>
    [ObservableProperty] private string _chequePayeeWidthMm = string.Empty;

    /// <summary>Amount in Words — distance of the FIRST line from the top edge, mm.</summary>
    [ObservableProperty] private string _chequeWordsLine1TopMm = string.Empty;

    /// <summary>Amount in Words — starting location of the first line from the left edge, mm.</summary>
    [ObservableProperty] private string _chequeWordsLine1LeftMm = string.Empty;

    /// <summary>Amount in Words — "Distance of 2nd Line from Top Edge", mm.</summary>
    [ObservableProperty] private string _chequeWordsLine2TopMm = string.Empty;

    /// <summary>Amount in Words — starting location of the second line from the left edge, mm.</summary>
    [ObservableProperty] private string _chequeWordsLine2LeftMm = string.Empty;

    /// <summary>Amount in Words — "Width area" each line wraps within, mm.</summary>
    [ObservableProperty] private string _chequeWordsWidthMm = string.Empty;

    /// <summary>Amount in Figures — "Distance from Top Edge", mm.</summary>
    [ObservableProperty] private string _chequeFiguresTopMm = string.Empty;

    /// <summary>Amount in Figures — "Starting Location from Left Edge", mm.</summary>
    [ObservableProperty] private string _chequeFiguresLeftMm = string.Empty;

    /// <summary>Amount in Figures — "Width area", mm.</summary>
    [ObservableProperty] private string _chequeFiguresWidthMm = string.Empty;

    /// <summary>Signatory Details — "Distance from Top Edge", mm.</summary>
    [ObservableProperty] private string _chequeSignTopMm = string.Empty;

    /// <summary>Signatory Details — "Starting Location from Left Edge", mm.</summary>
    [ObservableProperty] private string _chequeSignLeftMm = string.Empty;

    /// <summary>Signatory Details — width of the signature area, mm.</summary>
    [ObservableProperty] private string _chequeSignWidthMm = string.Empty;

    /// <summary>Signatory Details — height of the signature area, mm.</summary>
    [ObservableProperty] private string _chequeSignHeightMm = string.Empty;

    /// <summary>"Salutation of 1st Signatory" (e.g. "For Apex Solutions"). Blank ⇒ nothing printed.</summary>
    [ObservableProperty] private string _chequeSalutation1 = string.Empty;

    /// <summary>"Salutation of 2nd Signatory". Blank ⇒ nothing printed.</summary>
    [ObservableProperty] private string _chequeSalutation2 = string.Empty;

    /// <summary>"Print Currency Formal Name" on the amount in words.</summary>
    [ObservableProperty] private bool _chequePrintCurrencyFormalName;

    /// <summary>"Print Currency Symbol" on the amount in figures.</summary>
    [ObservableProperty] private bool _chequePrintCurrencySymbol;

    /// <summary>
    /// "Adjust Distance From Top Edge (in mm)" —
    /// <c>help.tallysolutions.com/docs/te9rel53/Banking/Cheque_Printing.htm</c>. A render-time addend applied to
    /// every element, deliberately kept OUT of the layout itself because that page states the adjustment "does
    /// not affect the settings of cheque dimensions pre-configured for the selected cheque format".
    /// </summary>
    [ObservableProperty] private string _chequeAdjustTopMm = string.Empty;

    /// <summary>"Adjust Distance From Left Edge (in mm)" — the horizontal half of the same nudge.</summary>
    [ObservableProperty] private string _chequeAdjustLeftMm = string.Empty;

    /// <summary>"Disable Company Name in the Pre-printed Cheques" inverted: OFF by default, because a bank's leaf
    /// normally already carries the drawer's name and printing it twice is the defect that toggle exists to
    /// avoid (<c>help.tallysolutions.com/cheque-payments-set-up/</c>).</summary>
    [ObservableProperty] private bool _printCompanyNameOnCheque;

    partial void OnEnableChequePrintingChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowChequeDimensions));
        OnPropertyChanged(nameof(ShowBankIdentity));    // it rides on ShowChequeDimensions for a bank ledger
    }

    /// <summary>
    /// Parses one millimetre box into TENTHS OF A MILLIMETRE. Blank ⇒ 0, which is this feature's "not set".
    ///
    /// <para>🔴 Invariant culture, exactly like the opening-balance and credit-limit parses above: the gate runs
    /// on ubuntu and macos as well as Windows, and a comma-decimal runner must read "12.5" the same way. Refusals
    /// mirror those boxes — a typo must not silently become 0 (which would move the element to the corner of a
    /// negotiable instrument), a negative offset is meaningless on a leaf, and the store is INTEGER tenths so
    /// anything finer than 0.1 mm cannot round-trip.</para>
    /// </summary>
    private bool TryReadTmm(string? text, string caption, out int tmm)
    {
        tmm = 0;
        var t = (text ?? string.Empty).Trim();
        if (t.Length == 0) return true;

        if (!decimal.TryParse(t, System.Globalization.NumberStyles.Number,
                              System.Globalization.CultureInfo.InvariantCulture, out var mm))
        {
            Message = $"{caption} must be a measurement in millimetres (e.g. 12.5), or blank.";
            return false;
        }
        if (mm < 0m)
        {
            Message = $"{caption} cannot be negative — every cheque measurement is taken from an edge of the leaf.";
            return false;
        }
        if (decimal.Round(mm, 1) != mm)
        {
            Message = $"{caption} cannot be finer than a tenth of a millimetre.";
            return false;
        }
        tmm = (int)(mm * 10m);
        return true;
    }

    // ------------------------------------------------------------- Cheque books (census 8.5; schema v57)

    /// <summary>
    /// True iff the <b>Cheque Books</b> sub-block should render: a bank ledger that already EXISTS.
    ///
    /// <para>🔴 <b>Scoped to alteration on purpose, and the reason is a foreign key.</b> A cheque book carries its
    /// bank ledger's id, and during Create there is no saved ledger to carry — <c>Company.AddChequeBook</c>
    /// refuses a book whose ledger is not on the company, precisely so an orphan book can never be built. So the
    /// bank is created first and its books are recorded on the next open, which is also the order an operator
    /// works in: you open the account, then the bank sends you a cheque book.</para>
    ///
    /// <para><b>🔴 WITHOUT THIS BLOCK THE CHEQUE REGISTER WOULD BE PERMANENTLY EMPTY.</b> The register buckets
    /// leaves against a cheque book's number range, and nothing else in the product creates one. A report whose
    /// only possible content has no route in is the "capability no user can reach" shape this project has already
    /// filed three times; this block is the route.</para>
    ///
    /// <para><b>Vendor grounding.</b> <c>help.tallysolutions.com/cheque-payments-set-up/</c>, "Specify Cheque
    /// Range and Format in Bank Ledger" — <i>Name of Cheque Book</i>, <i>From Number</i>, <i>To Number</i> and an
    /// auto-calculated <i>Number of Cheques</i>, captured on the bank ledger master.</para>
    /// </summary>
    public bool ShowChequeBooks => ShowChequePrinting && IsAltering;

    /// <summary>The cheque books already recorded against the ledger being altered, newest last.</summary>
    public ObservableCollection<ChequeBookListItem> ChequeBooks { get; } = new();

    /// <summary>The row the Remove button acts on.</summary>
    [ObservableProperty] private ChequeBookListItem? _selectedChequeBook;

    /// <summary>"Name of Cheque Book" for the book being added.</summary>
    [ObservableProperty] private string _newChequeBookName = string.Empty;

    /// <summary>"From Number" for the book being added — keyed exactly as printed, leading zeros and all.</summary>
    [ObservableProperty] private string _newChequeBookFrom = string.Empty;

    /// <summary>"To Number" for the book being added.</summary>
    [ObservableProperty] private string _newChequeBookTo = string.Empty;

    /// <summary>
    /// Records a cheque book against the bank ledger being altered and persists immediately — the book is a row
    /// of its own, not a field of the ledger form, so it is not waiting on Ctrl+A.
    ///
    /// <para>Refusals, each of which would otherwise produce a register that lies: a book needs a name and both
    /// ends of its range; a range whose ends cannot be counted (a lettered series) would enumerate no leaves at
    /// all and is refused with that reason rather than silently stored; and a To below the From is a transposition
    /// that would report a book of zero cheques.</para>
    /// </summary>
    public bool AddChequeBook()
    {
        Message = null;
        if (!IsAltering || _company.FindLedger(_editingId) is null)
        {
            Message = "Save the bank ledger first — a cheque book is recorded against a bank that already exists.";
            return false;
        }

        var name = (NewChequeBookName ?? string.Empty).Trim();
        var from = (NewChequeBookFrom ?? string.Empty).Trim();
        var to = (NewChequeBookTo ?? string.Empty).Trim();
        if (name.Length == 0 || from.Length == 0 || to.Length == 0)
        {
            Message = "A cheque book needs a name and both ends of its number range (e.g. 000101 to 000200).";
            return false;
        }

        var candidate = new ChequeBook(Guid.NewGuid(), _editingId, name, from, to);
        if (candidate.Count == 0)
        {
            Message = $"'{from}' to '{to}' is not a countable range of cheque numbers. Both ends must be digits, "
                    + "and the To number must not be below the From number.";
            return false;
        }

        _company.AddChequeBook(candidate);
        _storage.Save(_company);
        RefreshChequeBooks();
        Message = $"Cheque book '{name}' recorded: {candidate.Count} leaves, {from} to {to}.";
        NewChequeBookName = string.Empty;
        NewChequeBookFrom = string.Empty;
        NewChequeBookTo = string.Empty;
        _onChanged();
        return true;
    }

    /// <summary>
    /// Removes the selected cheque book and every leaf status recorded against it, and persists.
    ///
    /// <para><b>The leaf statuses go with it, by construction</b> — <c>Company.RemoveChequeBook</c> takes them —
    /// because a status row whose book is gone is an orphan the next Save would try to write against a missing
    /// foreign key, which fails the whole Save and leaves the open company unwritable.</para>
    /// </summary>
    public bool RemoveChequeBook()
    {
        Message = null;
        if (SelectedChequeBook is not { } row || _company.FindChequeBook(row.Id) is not { } book)
        {
            Message = "Select a cheque book to remove.";
            return false;
        }

        _company.RemoveChequeBook(book);
        _storage.Save(_company);
        RefreshChequeBooks();
        Message = $"Cheque book '{book.Name}' removed.";
        _onChanged();
        return true;
    }

    /// <summary>Re-reads the cheque books of the ledger being altered into the list.</summary>
    private void RefreshChequeBooks()
    {
        ChequeBooks.Clear();
        SelectedChequeBook = null;
        if (!IsAltering) return;
        foreach (var b in _company.ChequeBooksOf(_editingId))
            ChequeBooks.Add(new ChequeBookListItem
            {
                Id = b.Id,
                Display = $"{b.Name}  —  {b.FromNumber} to {b.ToNumber}  ({b.Count} leaves)",
            });
    }

    /// <summary>Trims a text box, mapping a blank one to <c>null</c> — "not captured" rather than an empty
    /// string, so the stored column reads the same as it did before the field existed.</summary>
    private static string? Blank(string? text) =>
        string.IsNullOrWhiteSpace(text) ? null : text.Trim();

    /// <summary>Renders stored tenths-mm back into the millimetre box. 0 shows as blank, because 0 IS "not set"
    /// here and a box reading "0" invites an operator to believe an element is placed at the corner.</summary>
    private static string FormatTmm(int tmm) =>
        tmm == 0
            ? string.Empty
            : (tmm / 10m).ToString("0.#", System.Globalization.CultureInfo.InvariantCulture);

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
        // v57 (census 8.5): the Cheque Books list is only meaningful over a ledger that exists, so it is filled
        // here rather than in LoadFrom — and ShowChequeBooks is raised with it, or the block would stay hidden
        // until some unrelated property happened to notify.
        vm.RefreshChequeBooks();
        vm.OnPropertyChanged(nameof(ShowChequeBooks));
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
        // The opening's Dr/Cr side is PROPOSED from the group's nature (Study Guide p.66) and keeps following a
        // re-pick — but only until the operator states it by hand (see OnOpeningIsDebitChanged).
        if (!_openingSideTouched)
            SetOpeningSideFromNature(value);
        OnPropertyChanged(nameof(IsPartyGroup));
        OnPropertyChanged(nameof(ShowPartyGst));
        OnPropertyChanged(nameof(IsDirectExpensesGroup));
        OnPropertyChanged(nameof(ShowAppropriation));
        OnPropertyChanged(nameof(ShowDefaultPriceLevel));
        // Census 1.7: the bill-wise block rides on the same party-group test, so it must follow a re-pick just
        // like ShowPartyGst above — otherwise picking a party group leaves the block hidden until the screen is
        // reopened, which is the stale-derived-property defect this list exists to prevent.
        OnPropertyChanged(nameof(ShowBillWiseOptions));
        OnPropertyChanged(nameof(ShowPartyTdsTcs));
        // WI-4: the Mailing Details block appears/disappears with the party-group test (ancestry-walking).
        OnPropertyChanged(nameof(ShowMailingDetails));
        // Census 8.4: the Cheque Printing block appears/disappears with the bank-group test (ancestry-walking),
        // so picking "Bank Accounts" reveals it without leaving and re-entering the screen.
        OnPropertyChanged(nameof(ShowChequePrinting));
        // v57: the two sub-blocks ride on the same bank-group test, so they follow it.
        OnPropertyChanged(nameof(ShowChequeDimensions));
        OnPropertyChanged(nameof(ShowChequeBooks));
        // Census 8.10: Bank Identity now also appears for a PARTY group (the beneficiary's account), so it has to
        // follow a re-pick in BOTH directions — and so does its heading, which names whose account it is.
        OnPropertyChanged(nameof(ShowBankIdentity));
        OnPropertyChanged(nameof(BankIdentityHeading));
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

    /// <summary>True while the app is assigning the opening side itself — see <see cref="OpeningIsDebit"/>.</summary>
    private bool _syncingOpeningSide;

    /// <summary>Proposes the opening's Dr/Cr side from a group's nature: Asset/Expense ⇒ Dr, Liability/Income ⇒ Cr —
    /// the conventional default, and the same rule <see cref="Create"/> used before the field was captured.</summary>
    private void SetOpeningSideFromNature(Group? group)
    {
        _syncingOpeningSide = true;
        try { OpeningIsDebit = group is not null && group.Nature is GroupNature.Asset or GroupNature.Expense; }
        finally { _syncingOpeningSide = false; }
    }

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

        // Opening Balance. A nil opening shows as BLANK, not "0.00", so the round-trip is exact: the form re-writes
        // the same Money.Zero and an untouched ledger persists byte-identically (ER-13).
        //
        // The side is loaded under the same suppression flag the nature-proposal uses, and then LATCHED. Latching is
        // the load-direction half of the contra-opening guard: a stored side is a STATED FACT about day one, so
        // re-picking the Under group on the alteration screen must never flip it. Without the latch, opening an
        // overdrawn bank (Bank Accounts, Cr) and merely re-selecting its group would silently restate the opening
        // by twice its magnitude — the exact class of silent data loss this method's header warns about.
        OpeningBalanceText = ledger.OpeningBalance == Money.Zero
            ? string.Empty
            : ledger.OpeningBalance.Amount.ToString("0.00###", System.Globalization.CultureInfo.InvariantCulture);
        _syncingOpeningSide = true;
        try { OpeningIsDebit = ledger.OpeningIsDebit; }
        finally { _syncingOpeningSide = false; }
        _openingSideTouched = true;

        MaintainBillByBill = ledger.MaintainBillByBill;
        DefaultCreditPeriodText = ledger.DefaultCreditPeriodDays?.ToString() ?? string.Empty;
        // Census 10.1: NULL ⇒ blank box (no limit); a limit of 0 ⇒ the string "0", which is a DIFFERENT answer.
        // Invariant culture so the box round-trips the same text on every runner, matching the opening-balance box.
        CreditLimitText = ledger.CreditLimit is { } cl
            ? cl.Amount.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)
            : string.Empty;
        CheckCreditDaysOnEntry = ledger.CheckCreditDaysOnEntry;
        OverrideCreditLimitWithPostDated = ledger.OverrideCreditLimitWithPostDated;

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

        // Cheque printing (census 8.4). Loaded for EVERY ledger, not just a bank one: a ledger moved out of the
        // bank groups keeps its stored flag, and pre-filling it means re-opening the master shows the truth.
        EnableChequePrinting = ledger.EnableChequePrinting;
        ChequePrintingBankName = ledger.ChequePrintingBankName ?? string.Empty;

        // v57 (census 8.4/8.6): the bank identity trio and the Cheque Dimensions. Loaded for EVERY ledger for the
        // same reason the two above are — re-opening the master must show what is actually stored.
        BankAccountNumber = ledger.BankAccountNumber ?? string.Empty;
        BankBranch = ledger.BankBranch ?? string.Empty;
        BankIfsc = ledger.BankIfsc ?? string.Empty;
        ChequeAdjustTopMm = FormatTmm(ledger.ChequeAdjustTopTmm);
        ChequeAdjustLeftMm = FormatTmm(ledger.ChequeAdjustLeftTmm);
        PrintCompanyNameOnCheque = ledger.PrintCompanyNameOnCheque;
        LoadChequeLayout(ledger.ChequeLayout);
    }

    /// <summary>Fills the twenty millimetre boxes and the four switches from a stored layout, or clears them when
    /// the ledger has none. The payee width shows the vendor's 135 mm default only when a layout exists — a
    /// ledger that never captured dimensions shows an empty block, not a half-filled one.</summary>
    private void LoadChequeLayout(ChequeLayout? layout)
    {
        ChequeLeafWidthMm = FormatTmm(layout?.LeafWidthTmm ?? 0);
        ChequeLeafHeightMm = FormatTmm(layout?.LeafHeightTmm ?? 0);
        ChequeDateTopMm = FormatTmm(layout?.DateTopTmm ?? 0);
        ChequeDateLeftMm = FormatTmm(layout?.DateLeftTmm ?? 0);
        ChequeDateCharPitchMm = FormatTmm(layout?.DateCharPitchTmm ?? 0);
        ChequePayeeTopMm = FormatTmm(layout?.PayeeTopTmm ?? 0);
        ChequePayeeLeftMm = FormatTmm(layout?.PayeeLeftTmm ?? 0);
        ChequePayeeWidthMm = FormatTmm(layout?.PayeeWidthTmm ?? 0);
        ChequeWordsLine1TopMm = FormatTmm(layout?.WordsLine1TopTmm ?? 0);
        ChequeWordsLine1LeftMm = FormatTmm(layout?.WordsLine1LeftTmm ?? 0);
        ChequeWordsLine2TopMm = FormatTmm(layout?.WordsLine2TopTmm ?? 0);
        ChequeWordsLine2LeftMm = FormatTmm(layout?.WordsLine2LeftTmm ?? 0);
        ChequeWordsWidthMm = FormatTmm(layout?.WordsWidthTmm ?? 0);
        ChequeFiguresTopMm = FormatTmm(layout?.FiguresTopTmm ?? 0);
        ChequeFiguresLeftMm = FormatTmm(layout?.FiguresLeftTmm ?? 0);
        ChequeFiguresWidthMm = FormatTmm(layout?.FiguresWidthTmm ?? 0);
        ChequeSignTopMm = FormatTmm(layout?.SignTopTmm ?? 0);
        ChequeSignLeftMm = FormatTmm(layout?.SignLeftTmm ?? 0);
        ChequeSignWidthMm = FormatTmm(layout?.SignWidthTmm ?? 0);
        ChequeSignHeightMm = FormatTmm(layout?.SignHeightTmm ?? 0);
        ChequeSalutation1 = layout?.Salutation1 ?? string.Empty;
        ChequeSalutation2 = layout?.Salutation2 ?? string.Empty;
        ChequePrintCurrencyFormalName = layout?.PrintCurrencyFormalName ?? false;
        ChequePrintCurrencySymbol = layout?.PrintCurrencySymbol ?? false;
    }

    /// <summary>
    /// The <b>single</b> form → ledger mapping, shared by <see cref="Create"/> and <see cref="Alter"/>. Validates
    /// everything FIRST (returning false with a <see cref="Message"/> and writing nothing), then writes every
    /// captured field onto <paramref name="target"/>.
    ///
    /// <para>Opening balance and its Dr/Cr side ARE written here, through the same shared mapping as every other
    /// captured field — see the write block for why this one is not under a hidden-sub-form guard.</para>
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

        // Census 10.1 — the Credit Limit. 🔴 BLANK ⇒ null ("no limit"); "0" ⇒ Money.Zero ("a limit of zero", which
        // BLOCKS). The two must never collapse into one another, so the blank test comes first and there is no
        // `?? Money.Zero` anywhere on this path. Refusals mirror the opening-balance box: a typo must not silently
        // become "no limit", a negative limit is meaningless, and the store is INTEGER paisa so sub-paisa cannot
        // round-trip. Invariant culture, matching the opening-balance parse, so a comma-decimal runner reads it the
        // same way.
        Money? creditLimit = null;
        var limitText = (CreditLimitText ?? string.Empty).Trim();
        if (!string.IsNullOrEmpty(limitText))
        {
            if (!decimal.TryParse(limitText, System.Globalization.NumberStyles.Number,
                                  System.Globalization.CultureInfo.InvariantCulture, out var limitAmount))
            {
                Message = "Credit limit must be an amount (e.g. 50000), or blank for no limit.";
                return false;
            }
            if (limitAmount < 0m)
            {
                Message = "Credit limit cannot be negative. Leave it blank for no limit, or enter 0 to block "
                        + "every credit transaction for this party.";
                return false;
            }
            if (decimal.Round(limitAmount, 2) != limitAmount)
            {
                Message = "Credit limit cannot be finer than a paisa (two decimal places).";
                return false;
            }
            creditLimit = new Money(limitAmount);
        }

        // Census 8.4 (v57) — the Cheque Dimensions. 🔴 PARSED HERE, IN THE VALIDATION PHASE, not beside the write
        // below: this method's contract is "validate everything first, write nothing on a refusal", and by the
        // time the cheque block is written several other fields already are. Twenty millimetre boxes, each of
        // which refuses a typo rather than silently reading 0 — and 0 on this screen means "element not placed",
        // which the renderer honours by SKIPPING that element. A typo that became 0 would quietly drop the payee
        // name off a negotiable instrument.
        var chequeLayout = new ChequeLayout();
        int chequeAdjustTopTmm = 0, chequeAdjustLeftTmm = 0;
        if (ShowChequePrinting && EnableChequePrinting)
        {
            if (!TryReadTmm(ChequeLeafWidthMm, "Cheque leaf width", out var leafW)) return false;
            if (!TryReadTmm(ChequeLeafHeightMm, "Cheque leaf height", out var leafH)) return false;
            if (!TryReadTmm(ChequeDateTopMm, "Date — distance from top edge", out var dTop)) return false;
            if (!TryReadTmm(ChequeDateLeftMm, "Date — distance from left edge", out var dLeft)) return false;
            if (!TryReadTmm(ChequeDateCharPitchMm, "Date — distance between characters", out var dPitch)) return false;
            if (!TryReadTmm(ChequePayeeTopMm, "Payee name — distance from top edge", out var pTop)) return false;
            if (!TryReadTmm(ChequePayeeLeftMm, "Payee name — distance from left edge", out var pLeft)) return false;
            if (!TryReadTmm(ChequePayeeWidthMm, "Payee name — width area", out var pWidth)) return false;
            if (!TryReadTmm(ChequeWordsLine1TopMm, "Amount in words — 1st line from top edge", out var w1Top)) return false;
            if (!TryReadTmm(ChequeWordsLine1LeftMm, "Amount in words — 1st line from left edge", out var w1Left)) return false;
            if (!TryReadTmm(ChequeWordsLine2TopMm, "Amount in words — 2nd line from top edge", out var w2Top)) return false;
            if (!TryReadTmm(ChequeWordsLine2LeftMm, "Amount in words — 2nd line from left edge", out var w2Left)) return false;
            if (!TryReadTmm(ChequeWordsWidthMm, "Amount in words — width area", out var wWidth)) return false;
            if (!TryReadTmm(ChequeFiguresTopMm, "Amount in figures — distance from top edge", out var fTop)) return false;
            if (!TryReadTmm(ChequeFiguresLeftMm, "Amount in figures — distance from left edge", out var fLeft)) return false;
            if (!TryReadTmm(ChequeFiguresWidthMm, "Amount in figures — width area", out var fWidth)) return false;
            if (!TryReadTmm(ChequeSignTopMm, "Signatory — distance from top edge", out var sTop)) return false;
            if (!TryReadTmm(ChequeSignLeftMm, "Signatory — distance from left edge", out var sLeft)) return false;
            if (!TryReadTmm(ChequeSignWidthMm, "Signatory — width of signature area", out var sWidth)) return false;
            if (!TryReadTmm(ChequeSignHeightMm, "Signatory — height of signature area", out var sHeight)) return false;
            if (!TryReadTmm(ChequeAdjustTopMm, "Adjust distance from top edge", out chequeAdjustTopTmm)) return false;
            if (!TryReadTmm(ChequeAdjustLeftMm, "Adjust distance from left edge", out chequeAdjustLeftTmm)) return false;

            // 🔴 A leaf with only ONE of width/height is refused outright. The renderer needs both to open the
            // page, and half a leaf size is far more likely to be a half-finished entry than an intention.
            if ((leafW > 0) != (leafH > 0))
            {
                Message = "A cheque leaf needs BOTH a width and a height, in millimetres — measure the leaf you "
                        + "actually hold. Leave both blank until you have.";
                return false;
            }

            chequeLayout = new ChequeLayout
            {
                LeafWidthTmm = leafW,
                LeafHeightTmm = leafH,
                DateTopTmm = dTop,
                DateLeftTmm = dLeft,
                DateCharPitchTmm = dPitch,
                PayeeTopTmm = pTop,
                PayeeLeftTmm = pLeft,
                // Blank keeps the vendor's documented 135 mm default rather than collapsing to 0, which would
                // mean "no width area" and let a long payee name run off the leaf.
                PayeeWidthTmm = pWidth == 0 ? ChequeLayout.DefaultPayeeWidthTmm : pWidth,
                WordsLine1TopTmm = w1Top,
                WordsLine1LeftTmm = w1Left,
                WordsLine2TopTmm = w2Top,
                WordsLine2LeftTmm = w2Left,
                WordsWidthTmm = wWidth,
                FiguresTopTmm = fTop,
                FiguresLeftTmm = fLeft,
                FiguresWidthTmm = fWidth,
                SignTopTmm = sTop,
                SignLeftTmm = sLeft,
                SignWidthTmm = sWidth,
                SignHeightTmm = sHeight,
                Salutation1 = ChequeSalutation1,
                Salutation2 = ChequeSalutation2,
                PrintCurrencyFormalName = ChequePrintCurrencyFormalName,
                PrintCurrencySymbol = ChequePrintCurrencySymbol,
            };
            chequeLayout.Normalize();
        }

        // Opening Balance (Study Guide pp.65–66) — blank ⇒ nil, which is the norm and must stay byte-identical to
        // the Money.Zero this screen hard-coded before the field existed (ER-13). Three refusals, all of which would
        // otherwise corrupt a set of books at the moment it is opened:
        //   • unparseable  — a typo must not silently become a nil opening;
        //   • negative     — the magnitude is unsigned by construction (Ledger.OpeningBalance "always ≥ 0"); the
        //                    direction is the Dr/Cr side, so "-5,000 Dr" is an ambiguity, not a credit;
        //   • sub-paisa    — the store is INTEGER paisa (NFR-3), so 1234.567 cannot round-trip. Refused HERE with a
        //                    readable message rather than surfacing as a raw persistence error three layers down.
        var opening = Money.Zero;
        var openingText = (OpeningBalanceText ?? string.Empty).Trim();
        if (!string.IsNullOrEmpty(openingText))
        {
            if (!decimal.TryParse(openingText, System.Globalization.NumberStyles.Number,
                                  System.Globalization.CultureInfo.InvariantCulture, out var openingAmount))
            {
                Message = "Opening balance must be an amount (e.g. 41237.53), or blank for nil.";
                return false;
            }
            if (openingAmount < 0m)
            {
                Message = "Opening balance cannot be negative — enter the amount and pick Cr for a credit balance.";
                return false;
            }

            opening = Money.FromRupees(openingAmount);
            if (!opening.IsPaisaExact)
            {
                Message = "Opening balance cannot be finer than a paisa (at most two decimal places).";
                return false;
            }
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

        // Opening Balance + its Dr/Cr side. Written UNCONDITIONALLY — deliberately NOT under a `if (Show…)` guard,
        // because unlike every block below it this field is not feature-gated: the corpus puts Opening Balance on
        // the ledger screen always (Study Guide p.66), so it always rendered and therefore always captured. The
        // hidden-sub-form rule protects fields the operator could not see; this one is never hidden.
        //
        // On ALTER this is a real restatement of the opening — which is the point. An accountant's first opening is
        // very often wrong and the corpus's alteration screen IS the creation screen, pre-filled, so the figure must
        // be correctable from it. It is safe precisely because LoadFrom below pre-fills BOTH halves from the store:
        // an alter that touches only a name writes the same values back, so nothing is restated by accident.
        target.OpeningBalance = opening;
        target.OpeningIsDebit = OpeningIsDebit;

        target.MaintainBillByBill = MaintainBillByBill;
        target.DefaultCreditPeriodDays = MaintainBillByBill ? creditDays : null;

        // Census 10.1 — the Credit Limits block. Hidden-sub-form rule, the same one the Party GST block below
        // follows: on a NON-party group the block was never on screen, so it is left EXACTLY as it was rather than
        // silently cleared. On a party group the three values are written verbatim, INCLUDING a null limit — the
        // operator clearing the box is a deliberate "no limit" and must be honoured.
        if (IsPartyGroup)
        {
            target.CreditLimit = creditLimit;
            target.CheckCreditDaysOnEntry = CheckCreditDaysOnEntry;
            target.OverrideCreditLimitWithPostDated = OverrideCreditLimitWithPostDated;
        }
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

        // Cheque printing (census 8.4) — hidden-sub-form rule: the block renders only for a bank group, so a
        // non-bank ledger captured nothing and must keep whatever it already had. The bank NAME is written only
        // while the toggle is on, because the vendor's screen only asks for it then; switching the toggle off
        // therefore parks the name rather than discarding it, and switching it back on restores the stationery
        // the operator already set up.
        if (ShowChequePrinting)
        {
            target.EnableChequePrinting = EnableChequePrinting;
            if (EnableChequePrinting)
            {
                target.ChequePrintingBankName =
                    string.IsNullOrWhiteSpace(ChequePrintingBankName) ? null : ChequePrintingBankName.Trim();

                // v57 — the bank identity trio and the Cheque Dimensions. 🔴 THIS ASSIGNMENT IS THE ONE THAT MAKES
                // THE CHEQUE LEAF PRINTABLE: it is the only writer of Ledger.ChequeLayout in src/, and without it
                // ChequePdf.Validate refuses every render on every loaded company. An EMPTY layout stores as null
                // rather than as a row of zeros, so a bank that enabled cheque printing without measuring a leaf
                // persists exactly what it did before v57 (ER-13) and the refusal message stays truthful.
                target.BankAccountNumber = Blank(BankAccountNumber);
                target.BankBranch = Blank(BankBranch);
                target.BankIfsc = Blank(BankIfsc);
                target.ChequeAdjustTopTmm = chequeAdjustTopTmm;
                target.ChequeAdjustLeftTmm = chequeAdjustLeftTmm;
                target.PrintCompanyNameOnCheque = PrintCompanyNameOnCheque;
                target.ChequeLayout = chequeLayout.IsEmpty ? null : chequeLayout;
            }
        }

        // census 8.10 — the BENEFICIARY's bank details on a party ledger. Same three columns, different owner:
        // these are what a bank payment-instruction file pays TO. Guarded by the hidden-sub-form rule exactly as
        // the block above is, so a non-party ledger captured nothing here and keeps whatever it already had.
        if (IsPartyGroup)
        {
            target.BankAccountNumber = Blank(BankAccountNumber);
            target.BankBranch = Blank(BankBranch);
            target.BankIfsc = Blank(BankIfsc);
        }

        // NOT written, on purpose — this screen does not own them, so an ALTER must leave them exactly as they
        // were: Alias, IsPredefined, SalesPurchaseGst, GstClassification and TdsTcsClassification (engine-managed
        // tags). OpeningBalance / OpeningIsDebit USED to be on this list — the screen could not capture them, so
        // leaving them alone was all it could do. It owns them now (see the write above).
        return true;
    }

    /// <summary>Clears the form after a successful CREATE so the next entry starts blank (an ALTER keeps the
    /// values on screen, because the user is looking at the master they just saved).</summary>
    private void ResetForNextEntry()
    {
        Name = string.Empty;
        // The opening must NOT carry into the next ledger — leaving it on screen would silently give the following
        // master the previous one's day-one balance. Clearing the latch re-arms the nature proposal, and because
        // SelectedGroup deliberately survives a create (the operator usually enters a run of ledgers under one
        // group), the side is re-proposed from the group still on screen rather than snapping to a bare Dr.
        OpeningBalanceText = string.Empty;
        _openingSideTouched = false;
        SetOpeningSideFromNature(SelectedGroup);
        DefaultCreditPeriodText = string.Empty;
        // Census 10.1: the limit must NOT carry into the next ledger — leaving it on screen would silently give the
        // following party the previous one's limit and start refusing its invoices.
        CreditLimitText = string.Empty;
        CheckCreditDaysOnEntry = false;
        OverrideCreditLimitWithPostDated = false;
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
        // Census 8.4: the cheque-printing block must NOT carry into the next ledger — leaving it on would give
        // the following bank the previous one's cheque stationery without the operator ever saying so.
        EnableChequePrinting = false;
        ChequePrintingBankName = string.Empty;
        // v57: the bank identity and the cheque DIMENSIONS must not carry either. Handing the next bank the
        // previous bank's account number, or its leaf geometry, would put the wrong ink on real stationery.
        BankAccountNumber = string.Empty;
        BankBranch = string.Empty;
        BankIfsc = string.Empty;
        ChequeAdjustTopMm = string.Empty;
        ChequeAdjustLeftMm = string.Empty;
        PrintCompanyNameOnCheque = false;
        LoadChequeLayout(null);
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
