using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Desktop.Services;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Apex.Desktop.ViewModels;

/// <summary>A row of the existing-budgets list on the Budget master screen.</summary>
public sealed class BudgetListRow
{
    public string Name { get; init; } = string.Empty;
    public string Period { get; init; } = string.Empty;
    public string Lines { get; init; } = string.Empty;
}

/// <summary>
/// One entry in the budget-line "Target" picker: either a <b>Group</b> (rolls up its ledgers) or a
/// single <b>Ledger</b>. Groups are listed first, then ledgers, each name-sorted; the <see cref="Display"/>
/// carries a "(Group)"/"(Ledger)" suffix so the two never read ambiguously in the combo.
/// </summary>
public sealed class BudgetTargetOption
{
    public Guid? GroupId { get; init; }
    public Guid? LedgerId { get; init; }
    public string Display { get; init; } = string.Empty;
    public bool IsGroup => GroupId is not null;
}

/// <summary>One pending budget line shown in the "Budget Lines" grid before the budget is created.</summary>
public sealed class PendingBudgetLineRow
{
    public string Target { get; init; } = string.Empty;
    public string Type { get; init; } = string.Empty;
    public string Amount { get; init; } = string.Empty;

    /// <summary>The engine line this row was built from (used to assemble the budget on Create).</summary>
    public BudgetLine Line { get; init; } = null!;
}

/// <summary>
/// The Budget-creation master ("Masters → Create → Budget", catalog §7; plan.md §5): pick a Name and the
/// period (From / To), then add one or more budget lines — each picks a <b>Group or Ledger</b> target, a
/// <b>Type</b> (On Closing Balance / On Nett Transactions) and an <b>Amount</b> — and Create persists the
/// whole budget to the company's <c>.db</c> via <see cref="CompanyStorage.Save"/>. Existing budgets are
/// listed below. Mirrors <see cref="CostCentreMasterViewModel"/>; no Avalonia types ⇒ headlessly testable.
/// </summary>
public sealed partial class BudgetMasterViewModel : ViewModelBase
{
    private const string DateFormat = "dd-MMM-yyyy";

    private readonly Company _company;
    private readonly CompanyStorage _storage;
    private readonly Action _onChanged;

    /// <summary>The Group-or-Ledger targets the line picker offers (groups first, then ledgers).</summary>
    public ObservableCollection<BudgetTargetOption> Targets { get; } = new();

    /// <summary>The two budget-line measure kinds the Type picker offers.</summary>
    public IReadOnlyList<BudgetType> Types { get; } =
        new[] { BudgetType.OnClosingBalance, BudgetType.OnNettTransactions };

    /// <summary>The budget lines added so far (not yet persisted — committed on Create).</summary>
    public ObservableCollection<PendingBudgetLineRow> PendingLines { get; } = new();

    /// <summary>The existing budgets, refreshed after each create.</summary>
    public ObservableCollection<BudgetListRow> Existing { get; } = new();

    // ---- budget header ----
    [ObservableProperty] private string _name = string.Empty;
    [ObservableProperty] private string _periodFromText;
    [ObservableProperty] private string _periodToText;

    // ---- current line being added ----
    [ObservableProperty] private BudgetTargetOption? _selectedTarget;
    [ObservableProperty] private BudgetType _selectedType = BudgetType.OnClosingBalance;
    [ObservableProperty] private string _lineAmountText = string.Empty;

    [ObservableProperty] private string? _message;

    public BudgetMasterViewModel(Company company, CompanyStorage storage, Action onChanged)
    {
        _company = company ?? throw new ArgumentNullException(nameof(company));
        _storage = storage ?? throw new ArgumentNullException(nameof(storage));
        _onChanged = onChanged ?? throw new ArgumentNullException(nameof(onChanged));

        // Default the period to the company's financial year (1-Apr → 31-Mar).
        var fyStart = company.FinancialYearStart;
        _periodFromText = fyStart.ToString(DateFormat, CultureInfo.InvariantCulture);
        _periodToText = fyStart.AddYears(1).AddDays(-1).ToString(DateFormat, CultureInfo.InvariantCulture);

        BuildTargets();
        SelectedTarget = Targets.FirstOrDefault();
        RefreshList();
    }

    /// <summary>Fills the target picker: every group (rolls up its ledgers), then every ledger.</summary>
    private void BuildTargets()
    {
        Targets.Clear();
        foreach (var g in _company.Groups.OrderBy(g => g.Name, StringComparer.OrdinalIgnoreCase))
            Targets.Add(new BudgetTargetOption { GroupId = g.Id, Display = $"{g.Name}  (Group)" });
        foreach (var l in _company.Ledgers.OrderBy(l => l.Name, StringComparer.OrdinalIgnoreCase))
            Targets.Add(new BudgetTargetOption { LedgerId = l.Id, Display = $"{l.Name}  (Ledger)" });
    }

    /// <summary>
    /// Adds the current target/type/amount as a budget line to the pending list (validated). Does not
    /// persist — the whole budget is committed on <see cref="Create"/>. Clears the amount for the next line.
    /// </summary>
    public bool AddLine()
    {
        Message = null;

        if (SelectedTarget is null)
        {
            Message = "Pick a group or ledger to budget.";
            return false;
        }
        if (!TryParseAmount(LineAmountText, out var amount))
        {
            Message = "Budget amount must be a number ≥ 0.";
            return false;
        }

        var line = SelectedTarget.IsGroup
            ? BudgetLine.ForGroup(SelectedTarget.GroupId!.Value, SelectedType, new Money(amount))
            : BudgetLine.ForLedger(SelectedTarget.LedgerId!.Value, SelectedType, new Money(amount));

        PendingLines.Add(new PendingBudgetLineRow
        {
            Target = SelectedTarget.Display,
            Type = TypeLabel(SelectedType),
            Amount = IndianFormat.AmountAlways(amount),
            Line = line,
        });

        LineAmountText = string.Empty;
        return true;
    }

    /// <summary>
    /// Ctrl+A create: validates the name (non-empty + unique), the period (From ≤ To) and that at least
    /// one line was added, then builds the budget, adds it to the company and persists. Resets the form.
    /// </summary>
    public bool Create()
    {
        Message = null;
        var name = (Name ?? string.Empty).Trim();

        if (string.IsNullOrWhiteSpace(name))
        {
            Message = "A budget name is required.";
            return false;
        }
        if (_company.FindBudgetByName(name) is not null)
        {
            Message = $"A budget named '{name}' already exists.";
            return false;
        }
        if (!TryParseDate(PeriodFromText, out var from))
        {
            Message = "From date must be dd-MMM-yyyy (e.g. 01-Apr-2024).";
            return false;
        }
        if (!TryParseDate(PeriodToText, out var to))
        {
            Message = "To date must be dd-MMM-yyyy (e.g. 31-Mar-2025).";
            return false;
        }
        if (to < from)
        {
            Message = "To date must be on or after the From date.";
            return false;
        }
        if (PendingLines.Count == 0)
        {
            Message = "Add at least one budget line (a group/ledger with an amount).";
            return false;
        }

        var budget = new Budget(
            Guid.NewGuid(), name, from, to,
            lines: PendingLines.Select(r => r.Line).ToList());

        _company.AddBudget(budget);
        _storage.Save(_company);

        RefreshList();
        Message = $"Budget '{name}' created with {budget.Lines.Count} line(s).";
        Name = string.Empty;
        PendingLines.Clear();
        _onChanged();
        return true;
    }

    private void RefreshList()
    {
        Existing.Clear();
        foreach (var b in _company.Budgets.OrderBy(b => b.Name, StringComparer.OrdinalIgnoreCase))
            Existing.Add(new BudgetListRow
            {
                Name = b.Name,
                Period = $"{b.PeriodFrom.ToString(DateFormat, CultureInfo.InvariantCulture)} to " +
                         $"{b.PeriodTo.ToString(DateFormat, CultureInfo.InvariantCulture)}",
                Lines = $"{b.Lines.Count} line(s)",
            });
    }

    /// <summary>The human label for a budget-line measure type (shared with the variance report).</summary>
    public static string TypeLabel(BudgetType type) => type switch
    {
        BudgetType.OnClosingBalance => "On Closing Balance",
        BudgetType.OnNettTransactions => "On Nett Transactions",
        _ => type.ToString(),
    };

    private static bool TryParseAmount(string? text, out decimal amount)
    {
        amount = 0m;
        var t = (text ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(t)) return false;
        return decimal.TryParse(t, NumberStyles.Number, CultureInfo.InvariantCulture, out amount) && amount >= 0m;
    }

    private static bool TryParseDate(string? text, out DateOnly date) =>
        DateOnly.TryParseExact((text ?? string.Empty).Trim(), DateFormat,
            CultureInfo.InvariantCulture, DateTimeStyles.None, out date);
}
