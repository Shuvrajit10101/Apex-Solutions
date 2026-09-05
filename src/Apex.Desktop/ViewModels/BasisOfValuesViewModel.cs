using System;
using System.Collections.ObjectModel;
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

    /// <summary>A short status line shown after applying (feedback that the projection re-ran).</summary>
    [ObservableProperty] private string _status = string.Empty;

    public BasisOfValuesViewModel(ReportsViewModel report)
    {
        _report = report ?? throw new ArgumentNullException(nameof(report));

        foreach (var scale in ReportScales.All)
            ScaleOptions.Add(new ReportScaleOption(scale));

        SeedFromReport();
    }

    /// <summary>Seeds the panel from the report's current scale (so opening → applying changes nothing).</summary>
    private void SeedFromReport()
    {
        foreach (var option in ScaleOptions)
            if (option.Scale == _report.Scale)
            {
                SelectedScale = option;
                return;
            }
    }

    /// <summary>
    /// Applies the chosen Scale Factor to the live report and re-projects it. The engine is re-run over the
    /// UNSCALED books and only the displayed cells are divided, so every figure remains reconcilable to the
    /// underlying statement and the report's own header declares the unit it is now in.
    /// </summary>
    public void Apply()
    {
        var scale = SelectedScale?.Scale ?? _report.Scale;
        _report.ApplyScale(scale);

        Status = scale == ReportScale.Default
            ? "Applied — figures shown in rupees."
            : $"Applied — figures shown in {ReportScales.Label(scale)}.";
    }
}
