using System;
using System.Collections.ObjectModel;
using Apex.Ledger.Reports;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Apex.Desktop.ViewModels;

/// <summary>
/// The <b>Ctrl+B "Basis of Values"</b> panel (W2-13a, census row 14.5), hosted as its own cascading
/// Miller-column page column to the right of the report it configures — never a stacked overlay, exactly like
/// the F12 <see cref="ReportConfigViewModel"/> it sits beside.
///
/// <para><b>Fidelity (RULING 14 — help.tallysolutions.com).</b> The vendor's keyboard-shortcut page gives
/// Ctrl+B as <i>"To views values in different ways in a report"</i>; the reports guide names the panel
/// <i>Basis of Values</i> — <i>"configure the values in your report for that instance, based on different
/// business needs"</i> — and the report pages reach the <b>Scale Factor</b> through it. This app's own
/// <c>OutstandingsViewModel</c> already recorded that Ctrl+B is Basis of Values in the reference product and left the
/// chord free for exactly this; the chord was verified unbound before it was taken (zero <c>Key.B</c> arms
/// without Alt in the window's tunnel handler).</para>
///
/// <para><b>🔴 WHAT THIS PANEL DOES NOT CARRY, STATED RATHER THAN IMPLIED.</b> The reference product's Ctrl+B
/// also offers a <i>Stock Valuation Method</i>, a <i>Godown type</i> and <i>Stock Position</i> on the stock
/// reports, <i>Type of Voucher Entries</i> and <i>Forex Transactions</i> elsewhere, and a <i>#New Number</i>
/// free-entry factor beside the named ones. None of those is built here. Census row 14.5 asks for eight
/// options and this slice delivers two of them; the row stays open with the residual named in the slice
/// artefact.</para>
///
/// <para>🟢 <b>TYPE OF BILLS (census 11.13, wave 9 track fd).</b> On Bills Receivable / Bills Payable the panel
/// carries <i>Type of Bills</i> with <i>All Bills</i> and <i>Overdue Bills</i>. Vendor
/// (help.tallysolutions.com/manage-receivables-outstanding-tally/, opened by content 2026-10-29): <i>"To view only
/// overdue bills, use Basis of Values to select Overdue Bills in Type of Bills."</i> The choice is handed to the
/// engine (<c>Outstandings.Build</c>), which applies the one overdue predicate <c>OverdueBills</c> also uses. The
/// vendor's other bill types ("Range of Bills Type", help.tallysolutions.com/tally-prime/analysis-verification/
/// outstandings-tally/) are not built (IV-287). Bills Receivable carries no Scale Factor, so on it the panel
/// shows Type of Bills alone.</para>
///
/// <para>It edits only the DISPLAY of the live <see cref="ReportsViewModel"/>: on <see cref="Apply"/> the
/// projection re-runs through the engine unchanged and the divide is applied on the way into each row cell,
/// so the figures stay engine-computed and are never mutated here.</para>
/// </summary>
public sealed partial class BasisOfValuesViewModel : ViewModelBase
{
    private readonly ReportsViewModel _report;

    /// <summary>The column title / heading for the panel.</summary>
    public string Title => "Basis of Values — Ctrl+B";

    /// <summary>The report this panel configures (its title, for the heading line).</summary>
    public string ReportTitle => _report.Title;

    /// <summary>The Scale Factor options offered, in the vendor's order (Default first).</summary>
    public ObservableCollection<ReportScaleOption> ScaleOptions { get; } = new();

    /// <summary>The chosen Scale Factor; seeded from the report so a no-edit apply is a no-op.</summary>
    [ObservableProperty] private ReportScaleOption? _selectedScale;

    /// <summary>Whether the Scale Factor section applies to this report (Trial Balance / Balance Sheet / P&amp;L).</summary>
    public bool ShowScaleFactor => _report.SupportsScaleFactor;

    /// <summary>Whether the Type of Bills section applies to this report (Bills Receivable / Payable).</summary>
    public bool ShowTypeOfBills => _report.SupportsTypeOfBills;

    /// <summary>The Type of Bills options offered: All Bills first, then Overdue Bills.</summary>
    public ObservableCollection<TypeOfBillsOption> TypeOfBillsOptions { get; } = new();

    /// <summary>The chosen Type of Bills; seeded from the report so a no-edit apply is a no-op.</summary>
    [ObservableProperty] private TypeOfBillsOption? _selectedTypeOfBills;

    /// <summary>A short status line shown after applying (feedback that the projection re-ran).</summary>
    [ObservableProperty] private string _status = string.Empty;

    public BasisOfValuesViewModel(ReportsViewModel report)
    {
        _report = report ?? throw new ArgumentNullException(nameof(report));

        foreach (var scale in ReportScales.All)
            ScaleOptions.Add(new ReportScaleOption(scale));
        if (_report.SupportsTypeOfBills)
            foreach (var type in new[] { TypeOfBills.AllBills, TypeOfBills.OverdueBills })
                TypeOfBillsOptions.Add(new TypeOfBillsOption(type, ReportsViewModel.TypeOfBillsLabel(type)));

        SeedFromReport();
    }

    /// <summary>Seeds the panel from the report's current scale (so opening → applying changes nothing).</summary>
    private void SeedFromReport()
    {
        foreach (var option in ScaleOptions)
            if (option.Scale == _report.Scale)
            {
                SelectedScale = option;
                break;
            }
        foreach (var option in TypeOfBillsOptions)
            if (option.Value == _report.TypeOfBills)
            {
                SelectedTypeOfBills = option;
                break;
            }
    }

    /// <summary>
    /// Applies the chosen Scale Factor to the live report and re-projects it. The engine is re-run over the
    /// UNSCALED books and only the displayed cells are divided, so every figure remains reconcilable to the
    /// underlying statement and the report's own header declares the unit it is now in.
    /// </summary>
    public void Apply()
    {
        if (ShowTypeOfBills)
        {
            var type = SelectedTypeOfBills?.Value ?? _report.TypeOfBills;
            _report.SetTypeOfBills(type);
            Status = $"Applied — Type of Bills: {ReportsViewModel.TypeOfBillsLabel(type)}.";
            return;
        }

        var scale = SelectedScale?.Scale ?? _report.Scale;
        _report.ApplyScale(scale);

        Status = scale == ReportScale.Default
            ? "Applied — figures shown in rupees."
            : $"Applied — figures shown in {ReportScales.Label(scale)}.";
    }
}

/// <summary>One Type of Bills row of the Ctrl+B panel (census 11.13).</summary>
public sealed record TypeOfBillsOption(TypeOfBills Value, string Display)
{
    /// <inheritdoc />
    public override string ToString() => Display;
}
