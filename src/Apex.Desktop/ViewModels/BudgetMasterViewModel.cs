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
public sealed partial class BudgetListRow : ObservableObject, IMasterListRow
{
    public string Name { get; init; } = string.Empty;
    public string Period { get; init; } = string.Empty;
    public string Lines { get; init; } = string.Empty;

    /// <inheritdoc/>
    public Guid MasterId { get; init; }

    /// <inheritdoc/>
    public string MasterName => Name;

    /// <inheritdoc/>
    [ObservableProperty] private bool _isHighlighted;
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
public sealed partial class BudgetMasterViewModel : ViewModelBase, IMasterListExportSource, IMasterListScreen
{
    // WI-5: rendering goes through the shared canonical formatter; this alias keeps the display call sites terse.
    private const string DateFormat = ApexDate.Canonical;

    private readonly Company _company;
    private readonly CompanyStorage _storage;
    private readonly Action _onChanged;

    // ------------------------------------------------- W33 C3 (census 2.9): the shared master-list arm

    /// <inheritdoc/>
    public string MasterKindLabel => "budget";

    // ------------------------------------------- W46 AF (census 2.9): the ALTER verb, and WHY IT ARRIVES NOW
    //
    // 🔴 THIS SCREEN WAS CREATE+DELETE ONLY, AND THE REASON WAS A GROUNDING GAP, NOT A CODE GAP. The W33 C3
    // block in MainWindowViewModel.AlterHighlightedMasterListRow said so in as many words: an alter verb is
    // "only honest where a VENDOR PAGE says what altering that master means", and no page had then been found
    // for a budget, so "budget, scenario … keep create+delete only and their census rows stay PARTIAL".
    //
    // THE PAGE EXISTS, and it was opened and read BY CONTENT on 2026-10-11:
    // help.tallysolutions.com/tally-prime/budgets-and-scenario/budgets-tally/ carries the heading
    // "Alter a Budget" and these clauses verbatim —
    //   · "Based on the financial situation of the company, market inflation, a need to cut costs, or to
    //      increase or decrease investment in a particular function, you may need to alter budgets created for
    //      groups, ledgers, or cost centres."
    //   · "You can enable the facility to alter a budget in TallyPrime and then make the necessary changes, as
    //      per your requirement."
    //   · "As always press Ctrl+A to save the Budget Alteration screen."
    // The vendor's own route is "Gateway of Tally > Alter > Budgets"; OURS is arrows + Ctrl+Enter on the
    // existing-list, this product's established shape, and it is labelled ours here rather than passed off.
    //
    // 🔴 WHAT IS STILL NOT CLOSED, SAID HERE SO NOBODY READS THIS AS COMPLETING ROW 2.9. The same vendor
    // sentence quoted above names COST CENTRES as a budget target, and `budget_lines` has group_id and
    // ledger_id and no cost_centre_id, so that target needs a SCHEMA MIGRATION. v66 ownership is an open user
    // decision (T2-125) and this track did not take it. Removing a line from the pending grid is also still
    // absent. 2.9 stays PARTIAL.

    /// <summary>The id of the budget being ALTERED, or <see cref="Guid.Empty"/> in Create mode.</summary>
    private Guid _editingId = Guid.Empty;

    /// <inheritdoc/>
    public bool IsAltering => _editingId != Guid.Empty;

    /// <summary>The screen heading — it names the VERB that is running, because the form is identical in both
    /// modes. The altering caption is the vendor's own screen name, <i>"Budget Alteration"</i>.</summary>
    public string Caption => IsAltering ? "Budget Alteration" : "Budget Creation";

    /// <summary>The commit button's label, matching the verb <see cref="Caption"/> names. The button carries a
    /// bound label rather than a literal "Create (Ctrl+A)" for the reason
    /// <see cref="CostCentreMasterViewModel.CommitLabel"/> records.</summary>
    public string CommitLabel => IsAltering ? "Save (Ctrl+A)" : "Create (Ctrl+A)";

    /// <summary>
    /// Opens this master in <b>Alter</b> mode over an existing budget — the same form, pre-filled, with the
    /// budget's existing lines already in the pending grid. Returns <c>null</c> if the id does not resolve.
    /// </summary>
    public static BudgetMasterViewModel? ForAlter(
        Company company, CompanyStorage storage, Guid budgetId, Action onChanged)
    {
        ArgumentNullException.ThrowIfNull(company);
        if (company.FindBudget(budgetId) is not { } budget) return null;

        var vm = new BudgetMasterViewModel(company, storage, onChanged);
        vm._editingId = budgetId;
        vm.LoadFrom(budget);
        vm.OnPropertyChanged(nameof(IsAltering));
        vm.OnPropertyChanged(nameof(Caption));
        vm.OnPropertyChanged(nameof(CommitLabel));
        return vm;
    }

    /// <summary>
    /// Loads an existing budget's values into the form — the name, both period dates, and every existing line
    /// into the pending grid, so <see cref="Alter"/> writing the grid back is a no-op until the operator
    /// actually changes something.
    /// </summary>
    public void LoadFrom(Budget budget)
    {
        ArgumentNullException.ThrowIfNull(budget);
        Name = budget.Name;
        PeriodFromText = budget.PeriodFrom.ToString(DateFormat, CultureInfo.InvariantCulture);
        PeriodToText = budget.PeriodTo.ToString(DateFormat, CultureInfo.InvariantCulture);

        PendingLines.Clear();
        foreach (var line in budget.Lines)
            PendingLines.Add(RowFor(line));
    }

    /// <summary>
    /// Ctrl+A <b>alter</b>: re-validates the name (unique <i>excluding this budget</i>), the period and the
    /// line set, then writes all of them back onto the existing budget and persists.
    /// </summary>
    /// <remarks>
    /// 🔴 <b>THE NAME CHECK EXCLUDES THIS BUDGET'S OWN ROW, AND THAT IS THE WHOLE DIFFERENCE FROM
    /// <see cref="Create"/>.</b> <c>Create</c>'s check is <c>FindBudgetByName(name) is not null</c>, which on an
    /// alteration screen refuses the budget's OWN unchanged name — the exact shape that got census rows 3.2 and
    /// 3.5 demoted (an alteration button that could not save). Accepting Ctrl+A with the name untouched is
    /// therefore a test in its own right, not an incidental.
    /// </remarks>
    public bool Alter()
    {
        Message = null;

        if (_editingId == Guid.Empty)
        {
            Message = "This screen is not altering an existing budget.";
            return false;
        }
        if (_company.FindBudget(_editingId) is not { } budget)
        {
            Message = "The budget being altered no longer exists.";
            return false;
        }

        var name = (Name ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            Message = "A budget name is required.";
            return false;
        }
        if (_company.Budgets.Any(b => b.Id != _editingId
                                      && string.Equals(b.Name, name, StringComparison.OrdinalIgnoreCase)))
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

        // The restore runs FIRST and UNCONDITIONALLY if the save throws — the shape W0-13 A1 earned on Create
        // just below, for the same reason: a failed save must not leave the altered figures on the shared
        // aggregate, or every LATER save carries them.
        var beforeName = budget.Name;
        var beforeFrom = budget.PeriodFrom;
        var beforeTo = budget.PeriodTo;
        var beforeLines = budget.Lines.ToList();

        budget.Name = name;
        budget.PeriodFrom = from;
        budget.PeriodTo = to;
        budget.ReplaceLines(PendingLines.Select(r => r.Line));
        try
        {
            _storage.Save(_company);
        }
        catch (Exception ex)
        {
            budget.Name = beforeName;
            budget.PeriodFrom = beforeFrom;
            budget.PeriodTo = beforeTo;
            budget.ReplaceLines(beforeLines);
            if (!SaveFailure.IsReportable(ex)) throw;
            Message = ex.Message;
            return false;
        }

        RefreshList();
        Message = $"Budget '{name}' altered ({budget.Lines.Count} line(s)).";
        _onChanged();
        return true;
    }

    /// <inheritdoc/>
    public IMasterListRow? HighlightedMasterRow => HighlightedRow;

    /// <inheritdoc/>
    public void ReloadExisting() => RefreshList();

    /// <inheritdoc/>
    /// <remarks>
    /// <para>🔴 <b>NO REFERENTIAL GUARD, AND THAT IS A MEASURED ABSENCE RATHER THAN AN OMISSION — the reason is
    /// written out in <c>MasterDeletionRules</c>'s W33 block.</b> The only foreign key in the schema into
    /// <c>budgets(id)</c> is <c>budget_lines.budget_id</c>, and a budget line is written from
    /// <c>Budget.Lines</c> — the parent's own object graph — so a deleted budget is simply not enumerated and no
    /// orphan row is ever written. Nothing else in the book points at a budget: the Budget Variance report
    /// recomputes from the budgets that exist. Inventing a refusal with no rule behind it is the defect this
    /// product's own delete doctrine names.</para>
    /// <para>Engine-only — the shell saves and reloads after this returns. A budget has no service of its own
    /// (its create path goes straight to <c>Company.AddBudget</c>), so this is the matching direct call rather
    /// than a service invented for symmetry. The unknown-id throw is what the shared arm turns into a notice
    /// instead of a silent no-op.</para></remarks>
    public void DeleteMaster(Guid id)
    {
        var budget = _company.Budgets.FirstOrDefault(b => b.Id == id)
            ?? throw new InvalidOperationException($"Budget {id} not found.");
        _company.RemoveBudget(budget);
    }

    private PayrollMasterHighlight<BudgetListRow>? _highlight;

    private PayrollMasterHighlight<BudgetListRow> Highlight =>
        _highlight ??= new PayrollMasterHighlight<BudgetListRow>(
            Existing, () => OnPropertyChanged(nameof(HighlightedRow)));

    /// <summary>The arrow-highlighted existing budget, or null.</summary>
    public BudgetListRow? HighlightedRow => Highlight.Row;

    /// <inheritdoc/>
    public void MoveHighlight(int direction) => Highlight.Move(direction);

    /// <inheritdoc/>
    public MasterListSnapshot ToMasterListSnapshot() => new(
        "Budgets",
        new[] { MasterListColumn.Text("Name"), MasterListColumn.Text("Period"), MasterListColumn.Text("Lines") },
        Existing.Select(r => (IReadOnlyList<string>)new[] { r.Name, r.Period, r.Lines }).ToList());

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
        // FRONT LINE (W0-13 A1). The parse above tests only "a number ≥ 0", and BudgetLine's own ctor validated
        // only the target and the sign — so a sub-paisa figure was accepted here, built into a line, and only
        // refused by Paisa.FromMoney deep inside the store, where this file had no catch block to turn it into a
        // message. Refusing at the field the operator typed into is the whole point: the message names the field
        // and echoes the text, which the store's own wording cannot do. Shared with the voucher-entry family via
        // StorableAmount, whose magnitude-then-exactness branch order is load-bearing.
        if (StorableAmount.ErrorFor(amount, LineAmountText, "the budget amount") is { } error)
        {
            Message = error;
            return false;
        }

        var line = SelectedTarget.IsGroup
            ? BudgetLine.ForGroup(SelectedTarget.GroupId!.Value, SelectedType, new Money(amount))
            : BudgetLine.ForLedger(SelectedTarget.LedgerId!.Value, SelectedType, new Money(amount));

        var row = RowFor(line);

        // 🔴 W46 AF — RE-STATING A TARGET OVERWRITES ITS FIGURE, IT DOES NOT APPEND A SECOND LINE. This is
        // OURS, not a vendor clause. 🔴 CORRECTED BY THE W46 REVIEW: this comment used to say "BudgetVariance
        // sums every line that matches a target … and the variance silently halves", and that is FALSE — read
        // `BudgetVarianceReport.Build`, which emits ONE ROW PER LINE and sums nothing. The real fault is just as
        // disqualifying and is what the rule exists for: two lines for the same group+measure render the SAME
        // target TWICE, each measured against the same actual, so the report shows two contradictory budgeted
        // figures and two variances for one target with nothing saying which is in force. (The vendor's own
        // Ledger/Group Budget screen is a grid of one row per Account Name, so one figure per target is also
        // the shape the page describes.) Before the Alter verb existed the bug was unreachable in practice
        // (a budget was built once, in one sitting); the moment the pending grid is PRE-FILLED from an existing
        // budget, correcting one amount is the ordinary act and appending is the wrong answer to it.
        var existing = PendingLines.FirstOrDefault(r => SameTarget(r.Line, line));
        if (existing is not null)
            PendingLines[PendingLines.IndexOf(existing)] = row;
        else
            PendingLines.Add(row);

        LineAmountText = string.Empty;
        return true;
    }

    /// <summary>True iff two budget lines name the same target AND the same measure — the identity under which
    /// one line replaces another in the pending grid.</summary>
    private static bool SameTarget(BudgetLine a, BudgetLine b) =>
        a.Type == b.Type && a.GroupId == b.GroupId && a.LedgerId == b.LedgerId;

    /// <summary>
    /// The pending-grid row for an engine line — the one place the three display strings are derived, so a line
    /// loaded by <see cref="LoadFrom"/> reads identically to one just typed in by <see cref="AddLine"/>.
    /// </summary>
    private PendingBudgetLineRow RowFor(BudgetLine line)
    {
        var display = Targets.FirstOrDefault(t => t.GroupId == line.GroupId && t.LedgerId == line.LedgerId)
                             ?.Display
                      // A target whose group/ledger has since been deleted still has to render: naming it
                      // "(unknown)" keeps the row visible and countable instead of dropping a budgeted figure
                      // out of the grid, which an Alter would then silently write away.
                      ?? "(unknown target)";

        return new PendingBudgetLineRow
        {
            Target = display,
            Type = TypeLabel(line.Type),
            Amount = IndianFormat.AmountAlways(line.Amount.Amount),
            Line = line,
        };
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

        // W0-13 A1 — this file previously held NO catch block at all, so ANY failure of the two lines below was an
        // unhandled exception out of a Ctrl+A keystroke, and the budget stayed on the shared aggregate so every
        // LATER save threw too. The shape is the one TrySave earned on the statutory screen and must not be
        // re-invented: the restore runs FIRST and UNCONDITIONALLY — a type filter must never be what decides
        // whether the rollback happens — and only then does SaveFailure.IsReportable decide message vs rethrow.
        // The restore is exact rather than a list snapshot: AddBudget appends this one budget and nothing else.
        _company.AddBudget(budget);
        try
        {
            _storage.Save(_company);
        }
        catch (Exception ex)
        {
            _company.RemoveBudget(budget);
            if (!SaveFailure.IsReportable(ex)) throw;
            Message = ex.Message;
            return false;   // PendingLines are deliberately kept, so the operator can retry without retyping.
        }

        RefreshList();
        Message = $"Budget '{name}' created with {budget.Lines.Count} line(s).";
        Name = string.Empty;
        PendingLines.Clear();
        _onChanged();
        return true;
    }

    private void RefreshList()
    {
        // By ID, not by index — see PayrollMasterHighlight.RestoreTo.
        var previouslyHighlighted = Highlight.IdBeforeRebuild();

        Existing.Clear();
        foreach (var b in _company.Budgets.OrderBy(b => b.Name, StringComparer.OrdinalIgnoreCase))
            Existing.Add(new BudgetListRow
            {
                MasterId = b.Id,
                Name = b.Name,
                Period = $"{b.PeriodFrom.ToString(DateFormat, CultureInfo.InvariantCulture)} to " +
                         $"{b.PeriodTo.ToString(DateFormat, CultureInfo.InvariantCulture)}",
                Lines = $"{b.Lines.Count} line(s)",
            });

        Highlight.RestoreTo(previouslyHighlighted);
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

    // WI-5: the ONE app-wide day-first parser (was a strict dd-MMM-yyyy-only parse on this screen).
    private static bool TryParseDate(string? text, out DateOnly date) => ApexDate.TryParse(text, out date);
}
