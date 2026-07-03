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

    public LedgerMasterViewModel(Company company, CompanyStorage storage, Action onChanged)
    {
        _company = company ?? throw new ArgumentNullException(nameof(company));
        _storage = storage ?? throw new ArgumentNullException(nameof(storage));
        _onChanged = onChanged ?? throw new ArgumentNullException(nameof(onChanged));

        Groups = company.Groups.OrderBy(g => g.Name, StringComparer.OrdinalIgnoreCase).ToList();
        SelectedGroup = company.FindGroupByName("Sundry Debtors") ?? Groups.FirstOrDefault();
        RefreshList();
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

        // Opening balance defaults to 0; the natural side follows the group's nature
        // (Asset/Expense = Debit, Liability/Income = Credit) — the conventional default.
        var openingIsDebit = SelectedGroup.Nature is GroupNature.Asset or GroupNature.Expense;
        var ledger = new DomainLedger(
            Guid.NewGuid(), name, SelectedGroup.Id, Money.Zero, openingIsDebit,
            maintainBillByBill: MaintainBillByBill,
            defaultCreditPeriodDays: MaintainBillByBill ? creditDays : null);

        _company.AddLedger(ledger);
        _storage.Save(_company);

        RefreshList();
        Message = $"Ledger '{name}' created under {SelectedGroup.Name}.";
        Name = string.Empty;
        DefaultCreditPeriodText = string.Empty;
        _onChanged();
        return true;
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
            Existing.Add(new LedgerListRow
            {
                Name = l.Name,
                Under = group?.Name ?? "(P&L)",
                Opening = opening,
            });
        }
    }
}
