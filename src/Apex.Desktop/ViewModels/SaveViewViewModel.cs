using System;
using Apex.Desktop.Services;
using Apex.Ledger.Domain;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Apex.Desktop.ViewModels;

/// <summary>
/// The "Save View" panel (RQ-8), hosted as its own cascading Miller-column page column to the right of the
/// report it saves — never a stacked overlay, mirroring the F12 config panel. It prompts for a name and, on
/// <see cref="Apply"/>, captures the live <see cref="ReportsViewModel"/>'s CONFIGURATION TUPLE (kind + period/
/// as-of + detail + F12 options + sort/filter + comparative columns) as a config-only
/// <see cref="Apex.Ledger.Reports.SavedReportView"/> and upserts it (overwrite by name) into the company's own
/// store via <see cref="CompanyStorage"/>. No computed figure is stored — the report is always recomputed when
/// the view is later applied (ER-9).
/// </summary>
public sealed partial class SaveViewViewModel : ViewModelBase
{
    private readonly ReportsViewModel _report;
    private readonly Company _company;
    private readonly CompanyStorage _storage;

    /// <summary>The column title / heading for the panel.</summary>
    /// 🔴 THE CAPTION NAMES Ctrl+L, THE VENDOR'S CHORD, NOT THE INVENTED Ctrl+S IT USED TO NAME. The vendor
    /// writes this verb as "Press Ctrl+L (Save View) to save the report with the specific configurations"
    /// (help.tallysolutions.com/use-save-view-feature-in-tallyprime/, re-opened by content 2026-09-23), and this
    /// slice bound Ctrl+L on a report. Ctrl+S still opens the panel as the legacy alias it always was — nothing
    /// is taken away from an operator who learned it — but a caption is a PROMISE about a keystroke, and the
    /// panel was promising a key the reference product does not have while the one it does have went unnamed.
    /// The same correction was already made to SavedViewsViewModel's empty-state line; this is the other half of
    /// it, and leaving the two disagreeing is how a half-corrected string survives a green gate.
    public string Title => "Save View — Ctrl+L";

    /// <summary>The report this panel saves (its title, for the heading line).</summary>
    public string ReportTitle => _report.Title;

    /// <summary>The name the view is saved under; an existing name overwrites (upsert).</summary>
    [ObservableProperty] private string _name = string.Empty;

    /// <summary>A short status line shown after saving (or a validation failure).</summary>
    [ObservableProperty] private string _status = string.Empty;

    public SaveViewViewModel(ReportsViewModel report, Company company, CompanyStorage storage)
    {
        _report = report ?? throw new ArgumentNullException(nameof(report));
        _company = company ?? throw new ArgumentNullException(nameof(company));
        _storage = storage ?? throw new ArgumentNullException(nameof(storage));
        Name = _report.Title; // a sensible default the user can overwrite
    }

    /// <summary>
    /// Saves the report's current view under <see cref="Name"/> (RQ-8): captures the config tuple and upserts it
    /// for this company. A blank name is rejected (nothing is written). An existing name is overwritten. Returns
    /// whether a view was actually saved, so the shell can pop the panel only on success.
    /// </summary>
    public bool Apply()
    {
        var name = (Name ?? string.Empty).Trim();
        if (name.Length == 0)
        {
            Status = "Enter a name for the view.";
            return false;
        }

        _storage.SaveView(_company, name, _report.ToSavedView());
        Status = $"Saved view “{name}”.";
        return true;
    }
}
