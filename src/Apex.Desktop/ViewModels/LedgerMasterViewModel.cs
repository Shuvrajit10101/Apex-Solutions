using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Apex.Ledger;
using Apex.Ledger.Domain;
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
/// The Ledger-creation master ("Create → Ledger", Alt+C): pick a name and an under-group
/// from the 28 predefined groups, create the ledger on the current company, and see it appear in
/// the list. Persists the company to its <c>.db</c> via <see cref="CompanyStorage.Save"/> on create.
/// Engine/DB logic stays here (no UI types) so it is headlessly testable.
/// </summary>
public sealed partial class LedgerMasterViewModel : ViewModelBase
{
    private readonly Company _company;
    private readonly CompanyStorage _storage;
    private readonly Action _onChanged;

    /// <summary>The 28 groups (excluding the reserved P&amp;L head) the Under-picker offers, name-sorted.</summary>
    public IReadOnlyList<Group> Groups { get; }

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

    public LedgerMasterViewModel(Company company, CompanyStorage storage, Action onChanged)
    {
        _company = company ?? throw new ArgumentNullException(nameof(company));
        _storage = storage ?? throw new ArgumentNullException(nameof(storage));
        _onChanged = onChanged ?? throw new ArgumentNullException(nameof(onChanged));

        Groups = company.Groups.OrderBy(g => g.Name, StringComparer.OrdinalIgnoreCase).ToList();
        SelectedGroup = company.FindGroupByName("Sundry Debtors") ?? Groups.FirstOrDefault();

        _selectedPer = PerChoices[0];
        _selectedOnBalance = OnBalanceChoices[0];
        _selectedApplicability = ApplicabilityChoices[0];
        _selectedStyle = StyleChoices[0];

        CurrencyChoices = BuildCurrencyChoices(company);
        _selectedCurrency = CurrencyChoices[0]; // base currency

        PartyStates = IndianState.All.Select(s => new IndianStateOption { State = s }).ToList();
        _partyRegistrationType = PartyRegistrationTypes[2]; // Unregistered (a plain B2C party) by default

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
    }

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

    /// <summary>
    /// Ctrl+A create: validates the name is non-empty, unique, and a group is chosen, then adds the
    /// ledger (opening 0, natural side from the group's nature) and persists the company. Refreshes
    /// the list and clears the name for the next entry.
    /// </summary>
    public bool Create()
    {
        Message = null;
        var name = (Name ?? string.Empty).Trim();

        if (string.IsNullOrWhiteSpace(name))
        {
            Message = "A ledger name is required.";
            return false;
        }
        if (SelectedGroup is null)
        {
            Message = "Pick an Under group.";
            return false;
        }
        if (_company.FindLedgerByName(name) is not null)
        {
            Message = $"A ledger named '{name}' already exists.";
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
            // registration type, or a state) — otherwise leave it null (a plain B2C party).
            if (pGstinOrNull is not null || regType != GstRegistrationType.Unregistered || PartyState is not null)
            {
                partyGst = new PartyGstDetails
                {
                    Gstin = pGstinOrNull,
                    RegistrationType = regType,
                    StateCode = PartyState?.Code,
                };
            }
        }

        // Opening balance defaults to 0; the natural side follows the group's nature
        // (Asset/Expense = Debit, Liability/Income = Credit) — the conventional default.
        var openingIsDebit = SelectedGroup.Nature is GroupNature.Asset or GroupNature.Expense;
        var ledger = new DomainLedger(
            Guid.NewGuid(), name, SelectedGroup.Id, Money.Zero, openingIsDebit,
            maintainBillByBill: MaintainBillByBill,
            defaultCreditPeriodDays: MaintainBillByBill ? creditDays : null,
            interest: interest,
            // "Currency of ledger" — null (base ₹/INR) for every existing ledger; a foreign currency
            // makes this a forex ledger whose lines carry forex amounts + rates.
            currencyId: SelectedCurrency?.CurrencyId,
            partyGst: partyGst);

        _company.AddLedger(ledger);
        _storage.Save(_company);

        RefreshList();
        var currencyNote = SelectedCurrency?.CurrencyId is null ? string.Empty
            : $" in {SelectedCurrency!.Display}";
        Message = $"Ledger '{name}' created under {SelectedGroup.Name}{currencyNote}.";
        Name = string.Empty;
        DefaultCreditPeriodText = string.Empty;
        EnableInterest = false;
        InterestRateText = string.Empty;
        SelectedCurrency = CurrencyChoices[0]; // reset to base for the next entry
        PartyGstin = string.Empty;
        PartyRegistrationType = PartyRegistrationTypes[2]; // back to Unregistered
        PartyState = null;
        _onChanged();
        return true;
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
