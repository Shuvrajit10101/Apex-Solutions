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

/// <summary>An employee row on the Payroll voucher, with an include toggle for the pay run.</summary>
public sealed partial class PayrollEmployeeSelection : ViewModelBase
{
    private readonly Action _onChanged;

    public Employee Employee { get; }

    [ObservableProperty] private bool _isIncluded;

    public PayrollEmployeeSelection(Employee employee, Action onChanged)
    {
        Employee = employee ?? throw new ArgumentNullException(nameof(employee));
        _onChanged = onChanged ?? throw new ArgumentNullException(nameof(onChanged));
    }

    public string Name => Employee.Name;

    partial void OnIsIncludedChanged(bool value) => _onChanged();
}

/// <summary>
/// One line of the computed salary breakdown shown before posting — a pay head (or the net Salary-Payable residual)
/// for one employee, with its amount on the correct Dr/Cr side so the "balances (Dr==Cr)" story reads directly off
/// the grid (the mirror of the posted <see cref="PayrollLineDetail"/>).
/// </summary>
public sealed class PayrollBreakdownRow
{
    public string Employee { get; init; } = string.Empty;
    public string PayHead { get; init; } = string.Empty;
    public string Category { get; init; } = string.Empty;
    public string DebitText { get; init; } = string.Empty;
    public string CreditText { get; init; } = string.Empty;
    public bool IsEmployeeHeader { get; init; }
}

/// <summary>
/// The <b>Payroll voucher</b> entry screen (Transactions → Vouchers → Payroll → Payroll · Ctrl+F4; Phase 8 slice 3;
/// RQ-7; ER-1/ER-2) — a pay run. Pick a period + voucher date, tick the employees to pay, then <b>Compute</b> runs
/// the pure <see cref="PayrollComputationService"/> for each and shows the per-employee earnings/deductions/net
/// breakdown with the derived Dr/Cr summary and a live <b>balances (Dr==Cr)</b> indicator. <b>Accept</b> posts one
/// balanced integrated accounting voucher through <see cref="PayrollVoucherService"/> (auto-creating the payroll
/// ledgers) — the engine rejects any imbalance, so an unbalanced run is never persisted.
///
/// <para>Compute is a <b>non-mutating preview</b> (the salary engine is pure); only Accept posts + persists. Gated
/// by <see cref="Company.PayrollEnabled"/> (ER-13). MVVM boundary: engine + persistence only, no Avalonia types ⇒
/// headlessly unit-testable. Every engine guard (no structure in force, a formula cycle, a missing value, a
/// negative net) surfaces to <see cref="Message"/> without crashing the UI.</para>
/// </summary>
public sealed partial class PayrollVoucherEntryViewModel : ViewModelBase
{
    private readonly Company _company;
    private readonly CompanyStorage _storage;
    private readonly Action _onChanged;

    /// <summary>The employees offered for the pay run (each with an include toggle).</summary>
    public ObservableCollection<PayrollEmployeeSelection> EmployeeSelections { get; } = new();

    /// <summary>The computed salary breakdown (populated by <see cref="Compute"/>).</summary>
    public ObservableCollection<PayrollBreakdownRow> Breakdown { get; } = new();

    [ObservableProperty] private string _periodFromText = string.Empty;
    [ObservableProperty] private string _periodToText = string.Empty;
    [ObservableProperty] private string _voucherDateText = string.Empty;
    [ObservableProperty] private string _narration = string.Empty;
    [ObservableProperty] private string? _message;

    [ObservableProperty] private bool _hasComputed;
    [ObservableProperty] private bool _isBalanced;
    [ObservableProperty] private string _grossText = string.Empty;
    [ObservableProperty] private string _deductionsText = string.Empty;
    [ObservableProperty] private string _netText = string.Empty;
    [ObservableProperty] private string _employerText = string.Empty;
    [ObservableProperty] private string _totalDebitText = string.Empty;
    [ObservableProperty] private string _totalCreditText = string.Empty;
    [ObservableProperty] private string _balanceText = string.Empty;
    [ObservableProperty] private bool _lastPostSucceeded;

    public PayrollVoucherEntryViewModel(Company company, CompanyStorage storage, Action? onChanged = null)
    {
        _company = company ?? throw new ArgumentNullException(nameof(company));
        _storage = storage ?? throw new ArgumentNullException(nameof(storage));
        _onChanged = onChanged ?? (() => { });

        var (from, to) = DefaultPeriod(company);
        _periodFromText = from.ToString("dd-MM-yyyy", CultureInfo.InvariantCulture);
        _periodToText = to.ToString("dd-MM-yyyy", CultureInfo.InvariantCulture);
        _voucherDateText = to.ToString("dd-MM-yyyy", CultureInfo.InvariantCulture);

        foreach (var e in _company.Employees.OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase))
            EmployeeSelections.Add(new PayrollEmployeeSelection(e, InvalidatePreview));
    }

    /// <summary>True once at least one employee exists (else there is nobody to pay).</summary>
    public bool CanRun => EmployeeSelections.Count > 0;

    partial void OnPeriodFromTextChanged(string value) => InvalidatePreview();
    partial void OnPeriodToTextChanged(string value) => InvalidatePreview();

    /// <summary>Editing the period or the employee set discards a stale preview (Accept requires a fresh Compute).</summary>
    private void InvalidatePreview()
    {
        if (!HasComputed) return;
        HasComputed = false;
        Breakdown.Clear();
    }

    /// <summary>
    /// The <b>Compute</b> action (a non-mutating preview): resolves each ticked employee's salary through the pure
    /// engine and builds the earnings/deductions/net breakdown + Dr/Cr summary + balance indicator. Surfaces any
    /// engine guard to <see cref="Message"/>. Returns true when a postable preview was produced.
    /// </summary>
    public bool Compute()
    {
        Message = null;
        LastPostSucceeded = false;
        HasComputed = false;
        Breakdown.Clear();
        ResetSummary();

        if (!_company.PayrollEnabled)
        {
            Message = "Enable Payroll (F11 → Maintain Payroll) before running payroll.";
            return false;
        }
        if (!TryParseDate(PeriodFromText, out var from) || !TryParseDate(PeriodToText, out var to))
        {
            Message = "The period From/To must be valid dates (dd-MM-yyyy).";
            return false;
        }
        if (to < from)
        {
            Message = "The period end must be on or after its start.";
            return false;
        }

        var included = EmployeeSelections.Where(s => s.IsIncluded).Select(s => s.Employee).ToList();
        if (included.Count == 0)
        {
            Message = "Tick at least one employee to include in the pay run.";
            return false;
        }

        var computation = new PayrollComputationService(_company);
        var results = new List<PayrollComputationResult>(included.Count);
        try
        {
            foreach (var employee in included)
                results.Add(computation.Compute(employee.Id, from, to));
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            Message = $"Cannot compute payroll: {ex.Message}";
            return false;
        }

        BuildBreakdown(included, results);

        // Mirror the engine's postability guards so the preview warns before Accept fails.
        var negative = results.FirstOrDefault(r => r.NetPayable.Amount < 0m);
        if (negative is not null)
        {
            var name = _company.FindEmployee(negative.EmployeeId)?.Name ?? "an employee";
            Message = $"{name}'s deductions exceed earnings (negative net pay) — adjust before posting.";
        }
        else if (Breakdown.Count(r => !r.IsEmployeeHeader) < 2)
        {
            Message = "The pay run has nothing to post (all computed amounts are zero).";
        }
        else
        {
            Message = $"Computed {included.Count} employee(s). Press Ctrl+A to post the payroll voucher.";
        }

        HasComputed = true;
        return true;
    }

    /// <summary>
    /// Ctrl+A / the Post button: posts the balanced integrated Payroll voucher for the ticked employees through
    /// <see cref="PayrollVoucherService"/> (auto-creating the payroll ledgers) and persists. Requires a fresh
    /// <see cref="Compute"/> preview. The engine rejects an unbalanced/negative run, so nothing is persisted on
    /// failure. Returns true on success.
    /// </summary>
    public bool Accept()
    {
        if (!HasComputed && !Compute())
            return false;

        Message = null;
        LastPostSucceeded = false;

        if (!TryParseDate(PeriodFromText, out var from) || !TryParseDate(PeriodToText, out var to))
        {
            Message = "The period From/To must be valid dates (dd-MM-yyyy).";
            return false;
        }
        if (!TryParseDate(VoucherDateText, out var voucherDate))
        {
            Message = "The voucher date must be a valid date (dd-MM-yyyy).";
            return false;
        }
        var included = EmployeeSelections.Where(s => s.IsIncluded).Select(s => s.Employee.Id).ToList();
        if (included.Count == 0)
        {
            Message = "Tick at least one employee to include in the pay run.";
            return false;
        }

        Voucher posted;
        try
        {
            var narration = string.IsNullOrWhiteSpace(Narration) ? null : Narration.Trim();
            posted = new PayrollVoucherService(_company).Post(from, to, included, voucherDate, null, narration);
            _storage.Save(_company);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            Message = $"Payroll posting failed: {ex.Message}";
            return false;
        }

        LastPostSucceeded = true;
        _onChanged();
        Message = $"Posted payroll voucher for {included.Count} employee(s): " +
                  $"Dr {IndianFormat.AmountAlways(posted.TotalDebit)} = Cr {IndianFormat.AmountAlways(posted.TotalCredit)}.";
        // Clear the preview so a second Accept requires a deliberate re-Compute (no accidental double post).
        HasComputed = false;
        Breakdown.Clear();
        ResetSummary();
        return true;
    }

    private void BuildBreakdown(IReadOnlyList<Employee> employees, IReadOnlyList<PayrollComputationResult> results)
    {
        var totalDr = 0m;
        var totalCr = 0m;
        var gross = 0m;
        var deductions = 0m;
        var net = 0m;
        var employer = 0m;

        for (var i = 0; i < results.Count; i++)
        {
            var result = results[i];
            Breakdown.Add(new PayrollBreakdownRow { Employee = employees[i].Name, IsEmployeeHeader = true });

            foreach (var line in result.Lines)
            {
                var amount = line.Amount.Amount;
                if (amount <= 0m) continue; // the engine skips zero lines

                switch (line.Role)
                {
                    case PayHeadPostingRole.Earning:
                        totalDr += amount; gross += amount;
                        Breakdown.Add(Row(employees[i].Name, line.PayHead, "Earning", debit: amount));
                        break;
                    case PayHeadPostingRole.Deduction:
                        totalCr += amount; deductions += amount;
                        Breakdown.Add(Row(employees[i].Name, line.PayHead, "Deduction", credit: amount));
                        break;
                    case PayHeadPostingRole.EmployerContribution:
                        totalDr += amount; totalCr += amount; employer += amount;
                        Breakdown.Add(Row(employees[i].Name, line.PayHead, "Employer Exp.", debit: amount));
                        Breakdown.Add(Row(employees[i].Name, line.PayHead, "Employer Pay.", credit: amount));
                        break;
                }
            }

            var employeeNet = result.NetPayable.Amount;
            if (employeeNet > 0m)
            {
                totalCr += employeeNet; net += employeeNet;
                Breakdown.Add(new PayrollBreakdownRow
                {
                    Employee = employees[i].Name,
                    PayHead = PayrollVoucherService.SalaryPayableLedgerName,
                    Category = "Net Pay",
                    CreditText = IndianFormat.AmountAlways(employeeNet),
                });
            }
        }

        GrossText = IndianFormat.AmountAlways(gross);
        DeductionsText = IndianFormat.AmountAlways(deductions);
        NetText = IndianFormat.AmountAlways(net);
        EmployerText = IndianFormat.AmountAlways(employer);
        TotalDebitText = IndianFormat.AmountAlways(totalDr);
        TotalCreditText = IndianFormat.AmountAlways(totalCr);
        // Paisa-exact comparison (Money) — the same conservation the engine enforces at Post.
        IsBalanced = new Money(totalDr) == new Money(totalCr);
        BalanceText = IsBalanced
            ? $"Balanced  ✓   Dr ₹{TotalDebitText} = Cr ₹{TotalCreditText}"
            : $"NOT balanced   Dr ₹{TotalDebitText} ≠ Cr ₹{TotalCreditText}";
    }

    private static PayrollBreakdownRow Row(string employee, PayHead head, string category,
        decimal debit = 0m, decimal credit = 0m) => new()
    {
        Employee = employee,
        PayHead = DisplayNameOf(head),
        Category = category,
        DebitText = debit > 0m ? IndianFormat.AmountAlways(debit) : string.Empty,
        CreditText = credit > 0m ? IndianFormat.AmountAlways(credit) : string.Empty,
    };

    private static string DisplayNameOf(PayHead head) =>
        string.IsNullOrWhiteSpace(head.DisplayName) ? head.Name : head.DisplayName!;

    private void ResetSummary()
    {
        IsBalanced = false;
        GrossText = DeductionsText = NetText = EmployerText = TotalDebitText = TotalCreditText = string.Empty;
        BalanceText = string.Empty;
    }

    private static (DateOnly From, DateOnly To) DefaultPeriod(Company company)
    {
        var from = company.FinancialYearStart;
        var to = from.AddMonths(1).AddDays(-1); // last day of the FY's first month
        return (from, to);
    }

    private static bool TryParseDate(string? text, out DateOnly date)
    {
        date = default;
        var t = (text ?? string.Empty).Trim();
        return DateOnly.TryParseExact(t, new[] { "dd-MM-yyyy", "dd-MMM-yyyy", "yyyy-MM-dd" },
                   CultureInfo.InvariantCulture, DateTimeStyles.None, out date)
               || DateOnly.TryParse(t, CultureInfo.InvariantCulture, DateTimeStyles.None, out date);
    }
}
