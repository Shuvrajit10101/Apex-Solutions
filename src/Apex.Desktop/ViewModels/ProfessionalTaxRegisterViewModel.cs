using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Services;
using Apex.Desktop.Services;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Apex.Desktop.ViewModels;

/// <summary>A selectable financial year on the PT deduction-register screen (its 01-Apr start year + label).</summary>
public sealed class PtRegisterFyOption
{
    public int StartYear { get; init; }
    public string Label => $"{StartYear}-{(StartYear + 1) % 100:00}";
    public override string ToString() => Label;
}

/// <summary>A selectable wage month on the PT deduction-register screen (its first-of-month date + label). PT is a
/// monthly deduction, so the register is built for one wage month at a time.</summary>
public sealed class PtRegisterMonthOption
{
    public DateOnly FirstDay { get; init; }
    public DateOnly LastDay => FirstDay.AddMonths(1).AddDays(-1);
    public string Label => FirstDay.ToString("MMM yyyy", CultureInfo.InvariantCulture);
    public override string ToString() => Label;
}

/// <summary>One <b>employee row</b> of the monthly PT deduction register (Phase 8 slice 6): the member's name +
/// number, the whole-rupee PT wages (gross monthly earnings — the PT wage basis), the month's Professional Tax and
/// the FY-to-date cumulative PT (bounded by the ₹2,500 annual cap). Whole rupees — PT carries no paisa.</summary>
public sealed class PtRegisterRowVm
{
    public string EmployeeName { get; init; } = string.Empty;
    public string EmployeeNumber { get; init; } = string.Empty;
    public string PtWages { get; init; } = string.Empty;
    public string MonthlyPt { get; init; } = string.Empty;
    public string FyCumulative { get; init; } = string.Empty;
}

/// <summary>
/// The <b>PT Deduction Register</b> report page (Reports → Statutory Reports → Payroll → PT Deduction Register;
/// Phase 8 slice 6; RQ-11; catalog §14). A read-only projection over the same pure
/// <see cref="PayrollComputationService"/> the payroll voucher posts: pick a financial year + wage month and it
/// shows the establishment's PT state + registration number, the per-employee rows (name, number, PT wages, the
/// month's PT and the FY-to-date cumulative) and the PT-wages / PT footings. <b>Ctrl+A</b> / the Export button writes
/// the register as a deterministic CSV to the export folder (mirroring the PF ECR / ESI contribution export);
/// <b>Alt+B</b> saves that file and returns to the menu.
///
/// <para>Gated: only reachable when Payroll Statutory is enabled (the menu item + the open path are gated on
/// <see cref="Company.PayrollStatutoryEnabled"/>), so a non-payroll company is byte-identical (ER-13). Every figure
/// is the deterministic PT computation of the same wages the voucher posted (state slab band + February over-charge +
/// ₹2,500 annual cap), so the register reconciles to the "Professional Tax Payable" ledger by construction. A member
/// whose salary structure carries a PT head is listed even when the month's PT is ₹0 (an exempt / below-threshold
/// band), so the register is a complete view. MVVM boundary: engine + persistence only, no Avalonia types (headlessly
/// testable); deterministic — no clock/RNG.</para>
/// </summary>
public sealed partial class ProfessionalTaxRegisterViewModel : ViewModelBase
{
    private readonly Company _company;

    [ObservableProperty] private string _title = "Professional Tax Deduction Register";
    [ObservableProperty] private string _subtitle = string.Empty;
    [ObservableProperty] private string _stateText = string.Empty;
    [ObservableProperty] private string _registrationText = string.Empty;
    [ObservableProperty] private string _wageMonthText = string.Empty;

    // Register footings (whole rupees, always rendered).
    [ObservableProperty] private string _totalWagesText = "0";
    [ObservableProperty] private string _totalPtText = "0";

    [ObservableProperty] private string _memberCountText = string.Empty;
    [ObservableProperty] private bool _isEmpty;
    [ObservableProperty] private string _statusText = string.Empty;
    [ObservableProperty] private string? _message;

    // Export knobs (mirror the PF ECR / ESI export: folder + name; the CSV is byte-stable off the engine figures).
    [ObservableProperty] private string _exportFolder = string.Empty;
    [ObservableProperty] private string _exportStatus = string.Empty;

    private PtRegisterFyOption? _selectedYear;
    private PtRegisterMonthOption? _selectedMonth;

    /// <summary>The financial years the register can be built for (the company FY + the two prior).</summary>
    public ObservableCollection<PtRegisterFyOption> FinancialYears { get; } = new();

    /// <summary>The 12 wage months of the selected financial year (Apr … Mar).</summary>
    public ObservableCollection<PtRegisterMonthOption> Months { get; } = new();

    /// <summary>The per-employee PT rows of the selected wage month.</summary>
    public ObservableCollection<PtRegisterRowVm> Rows { get; } = new();

    public ProfessionalTaxRegisterViewModel(Company company)
    {
        _company = company ?? throw new ArgumentNullException(nameof(company));

        var fyStart = company.FinancialYearStart.Year;
        for (var y = fyStart; y >= fyStart - 2; y--)
            FinancialYears.Add(new PtRegisterFyOption { StartYear = y });

        _selectedYear = FinancialYears.FirstOrDefault();
        RebuildMonths();
        _selectedMonth = Months.FirstOrDefault();

        try { ExportFolder = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments); }
        catch { ExportFolder = string.Empty; }

        Rebuild();
    }

    /// <summary>The selected financial year; changing it re-derives the wage months and rebuilds the register.</summary>
    public PtRegisterFyOption? SelectedYear
    {
        get => _selectedYear;
        set
        {
            if (!SetProperty(ref _selectedYear, value)) return;
            RebuildMonths();
            _selectedMonth = Months.FirstOrDefault();
            OnPropertyChanged(nameof(SelectedMonth));
            Rebuild();
        }
    }

    /// <summary>The selected wage month; changing it rebuilds the register for that month.</summary>
    public PtRegisterMonthOption? SelectedMonth
    {
        get => _selectedMonth;
        set { if (SetProperty(ref _selectedMonth, value)) Rebuild(); }
    }

    /// <summary>The register file name the export will write (from the PT registration number / "PT" + FY + month).</summary>
    public string ExportFileName
    {
        get
        {
            var estab = string.IsNullOrWhiteSpace(_company.PtConfig?.RegistrationNumber)
                ? "PT" : _company.PtConfig!.RegistrationNumber!.Trim();
            var month = (SelectedMonth?.FirstDay ?? _company.FinancialYearStart).ToString("yyyy_MM", CultureInfo.InvariantCulture);
            return $"{estab}_{month}";
        }
    }

    /// <summary>The full register file name including the <c>.csv</c> extension.</summary>
    public string ExportResolvedFileName => ExportFileName + ".csv";

    /// <summary>Re-derives the 12 wage-month options for the selected financial year (Apr of the start year … Mar).</summary>
    private void RebuildMonths()
    {
        var startYear = SelectedYear?.StartYear ?? _company.FinancialYearStart.Year;
        var first = new DateOnly(startYear, _company.FinancialYearStart.Month, 1);
        Months.Clear();
        for (var i = 0; i < 12; i++)
            Months.Add(new PtRegisterMonthOption { FirstDay = first.AddMonths(i) });
    }

    /// <summary>(Re)builds the PT register for the selected FY + wage month and refreshes the rows + footings.</summary>
    public void Rebuild()
    {
        var month = SelectedMonth ?? Months.FirstOrDefault();
        var from = month?.FirstDay ?? _company.FinancialYearStart;
        var to = month?.LastDay ?? from.AddMonths(1).AddDays(-1);

        WageMonthText = from.ToString("MMMM yyyy", CultureInfo.InvariantCulture);
        var stateName = _company.PtConfig?.StateCode is { } code
            ? (IndianState.FromCode(code)?.Name ?? code)
            : "None (no Professional Tax)";
        StateText = stateName;
        RegistrationText = string.IsNullOrWhiteSpace(_company.PtConfig?.RegistrationNumber)
            ? "—" : _company.PtConfig!.RegistrationNumber!;
        Subtitle = $"{_company.Name}  —  Wage month {WageMonthText}  ·  State {StateText}  ·  Enrolment {RegistrationText}";

        Rows.Clear();
        Message = null;
        ExportStatus = string.Empty;

        long totalWages = 0, totalPt = 0;

        if (_company.PtConfig is not null)
        {
            // The FY window the ₹2,500 cumulative cap resets on, for the selected wage month.
            var fyStart = ProfessionalTax.FinancialYearStart(to);

            foreach (var employee in _company.Employees)
            {
                PayrollComputationResult result;
                try
                {
                    result = new PayrollComputationService(_company).Compute(employee.Id, from, to);
                }
                catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
                {
                    continue; // no effective structure for this member this month — not in the PT register
                }

                // Only members whose structure carries a PT head are in the PT scheme (shown even when the band is Nil).
                var hasPtHead = result.Lines.Any(l =>
                    l.Role == PayHeadPostingRole.Deduction && l.PayHead.PtComponent != PtStatutoryComponent.None);
                if (!hasPtHead) continue;

                var pt = WholeRupee(result.ProfessionalTaxDeducted);
                var wages = WholeRupee(result.GrossEarnings);
                var cumulative = CumulativeThroughMonth(employee.Id, fyStart, from, to);

                Rows.Add(new PtRegisterRowVm
                {
                    EmployeeName = employee.Name,
                    EmployeeNumber = string.IsNullOrWhiteSpace(employee.EmployeeNumber) ? "—" : employee.EmployeeNumber!,
                    PtWages = R(wages),
                    MonthlyPt = R(pt),
                    FyCumulative = R(cumulative),
                });
                totalWages += wages;
                totalPt += pt;
            }
        }

        // Order by employee name so the register is byte-stable regardless of input order.
        var ordered = Rows.OrderBy(r => r.EmployeeName, StringComparer.Ordinal)
                          .ThenBy(r => r.EmployeeNumber, StringComparer.Ordinal)
                          .ToList();
        Rows.Clear();
        foreach (var r in ordered) Rows.Add(r);

        RefreshTotals(totalWages, totalPt);
    }

    /// <summary>The member's FY-to-date cumulative PT through the selected wage month — the Σ of the deterministic
    /// monthly PT from the FY start up to and including this month, clamped at the ₹2,500 annual cap (Article 276(2))
    /// so the register surfaces the constitutional ceiling. A month with no effective structure contributes ₹0.</summary>
    private long CumulativeThroughMonth(Guid employeeId, DateOnly fyStart, DateOnly selectedFrom, DateOnly selectedTo)
    {
        decimal sum = 0m;
        var monthFrom = fyStart;
        while (monthFrom <= selectedFrom)
        {
            var monthTo = monthFrom.AddMonths(1).AddDays(-1);
            try
            {
                var r = new PayrollComputationService(_company).Compute(employeeId, monthFrom, monthTo);
                sum += r.ProfessionalTaxDeducted.Amount;
            }
            catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
            {
                // no structure that month — contributes nothing to the running total
            }
            monthFrom = monthFrom.AddMonths(1);
        }
        var whole = WholeRupee(new Money(sum));
        return Math.Min(whole, (long)ProfessionalTax.AnnualCap);
    }

    private void RefreshTotals(long totalWages, long totalPt)
    {
        TotalWagesText = R(totalWages);
        TotalPtText = R(totalPt);

        IsEmpty = Rows.Count == 0;
        MemberCountText = Rows.Count == 1 ? "1 employee" : $"{Rows.Count} employees";
        StatusText = IsEmpty
            ? "No Professional-Tax employees this month — nothing to remit."
            : $"PT register ready — {MemberCountText}; total PT to remit ₹{TotalPtText}.";
        OnPropertyChanged(nameof(ExportFileName));
        OnPropertyChanged(nameof(ExportResolvedFileName));
    }

    /// <summary>
    /// Ctrl+A / the Export button: writes the PT deduction register as a deterministic CSV to
    /// <see cref="ExportFolder"/> under <see cref="ExportResolvedFileName"/>. The bytes come straight off the engine
    /// figures (byte-stable, de-branded); the write goes through the injectable <paramref name="writeBytes"/> seam
    /// (null ⇒ real filesystem) so tests never touch disk. Returns true on success and sets
    /// <see cref="ExportStatus"/> either way.
    /// </summary>
    public bool ExportRegister(Action<string, byte[]>? writeBytes = null)
    {
        try
        {
            var bytes = BuildCsv();
            var folder = ExportFolder ?? string.Empty;
            var path = string.IsNullOrEmpty(folder)
                ? ExportResolvedFileName
                : Path.Combine(folder, ExportResolvedFileName);

            if (writeBytes is not null) writeBytes(path, bytes);
            else File.WriteAllBytes(path, bytes);

            ExportStatus = $"Exported {bytes.Length:#,0} bytes to {path}";
            return true;
        }
        catch (Exception ex)
        {
            ExportStatus = "Could not export the PT register: " + ex.Message;
            return false;
        }
    }

    /// <summary>Builds the deterministic register CSV (UTF-8, no BOM): a small header block (company / state /
    /// enrolment / wage month) then one line per employee (number, name, PT wages, monthly PT, FY cumulative) and a
    /// Total line. Whole rupees, no paisa; de-branded.</summary>
    private byte[] BuildCsv()
    {
        var sb = new StringBuilder();
        sb.Append("Professional Tax Deduction Register\n");
        sb.Append("Company,").Append(Csv(_company.Name)).Append('\n');
        sb.Append("State,").Append(Csv(StateText)).Append('\n');
        sb.Append("Enrolment No,").Append(Csv(RegistrationText)).Append('\n');
        sb.Append("Wage Month,").Append(Csv(WageMonthText)).Append('\n');
        sb.Append("Employee Number,Employee Name,PT Wages,Monthly PT,FY Cumulative PT\n");
        long totalWages = 0, totalPt = 0;
        foreach (var r in Rows)
        {
            sb.Append(Csv(r.EmployeeNumber)).Append(',')
              .Append(Csv(r.EmployeeName)).Append(',')
              .Append(Csv(r.PtWages)).Append(',')
              .Append(Csv(r.MonthlyPt)).Append(',')
              .Append(Csv(r.FyCumulative)).Append('\n');
            totalWages += ParseRupees(r.PtWages);
            totalPt += ParseRupees(r.MonthlyPt);
        }
        sb.Append("Total,,").Append(Csv(R(totalWages))).Append(',').Append(Csv(R(totalPt))).Append(",\n");
        return new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(sb.ToString());
    }

    private static long ParseRupees(string grouped) =>
        long.TryParse(grouped.Replace(",", string.Empty), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : 0L;

    private static string Csv(string value)
    {
        if (value.IndexOfAny(new[] { ',', '"', '\n' }) < 0) return value;
        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }

    /// <summary>Whole-rupee Indian-grouped display of a PT integer figure (always rendered, even zero).</summary>
    private static string R(long value) => IndianFormat.RupeesAlways(value);

    /// <summary>The whole-rupee integer of a PT money field (half-up); PT figures are whole rupees.</summary>
    private static long WholeRupee(Money value) => (long)Math.Round(value.Amount, 0, MidpointRounding.AwayFromZero);
}
