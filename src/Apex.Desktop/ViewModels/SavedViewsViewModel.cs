using System;
using System.Collections.ObjectModel;
using Apex.Desktop.Services;
using Apex.Ledger.Domain;
using Apex.Ledger.Reports;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Apex.Desktop.ViewModels;

/// <summary>One row of the Saved Views list: the view's name plus the config-only view it names (RQ-8).</summary>
public sealed class SavedViewItem
{
    public string Name { get; }
    public SavedReportView View { get; }

    /// <summary>The report-kind label shown beside the name (resolved from the stable token; "Unknown" when the
    /// token no longer maps to a report this build knows).</summary>
    public string KindLabel { get; }

    public SavedViewItem(string name, SavedReportView view)
    {
        Name = name;
        View = view;
        KindLabel = ReportsViewModel.KindFor(view.ReportKind) is { } k ? k.ToString() : "Unknown";
    }
}

/// <summary>
/// The "Saved Views" panel (RQ-8), hosted as its own cascading Miller-column page column nested under Reports —
/// keyboard-first, never a flat dump. It lists THIS company's saved report views (ordered by name), lets the
/// user OPEN (apply) one — which the shell turns into a fresh report of the saved kind with the config
/// re-applied and the figures recomputed (ER-9) — and DELETE one. It holds no figures; every list entry is a
/// config-only <see cref="SavedReportView"/> loaded from the company's own store via <see cref="CompanyStorage"/>.
/// </summary>
public sealed partial class SavedViewsViewModel : ViewModelBase
{
    private readonly Company _company;
    private readonly CompanyStorage _storage;

    /// <summary>The column title / heading for the panel.</summary>
    public string Title => "Saved Views";

    /// <summary>The company's saved views, ordered by name (case-insensitive). Refreshed after a delete.</summary>
    public ObservableCollection<SavedViewItem> Views { get; } = new();

    /// <summary>The highlighted list row (two-way bound to the list's SelectedItem); Open/Delete act on it.</summary>
    [ObservableProperty] private SavedViewItem? _selected;

    /// <summary>A short status / empty-state line.</summary>
    [ObservableProperty] private string _status = string.Empty;

    /// <summary>Raised when the user opens (applies) a saved view — the shell opens a fresh report of the saved
    /// kind and applies the config, recomputing the figures. Carries the config-only view.</summary>
    public event Action<SavedReportView>? OpenRequested;

    public SavedViewsViewModel(Company company, CompanyStorage storage)
    {
        _company = company ?? throw new ArgumentNullException(nameof(company));
        _storage = storage ?? throw new ArgumentNullException(nameof(storage));
        Reload();
    }

    /// <summary>(Re)loads the company's saved views into <see cref="Views"/>, preserving the ordered-by-name list.</summary>
    public void Reload()
    {
        Views.Clear();
        foreach (var entry in _storage.ListViews(_company))
            Views.Add(new SavedViewItem(entry.Name, entry.View));
        Selected = Views.Count > 0 ? Views[0] : null;
        // 🔴 THE CHORD NAMED HERE IS Ctrl+L, AND THAT IS A CORRECTION, NOT A PREFERENCE. This line used to read
        // "with Ctrl+S" — an invented chord. The vendor's Save View chord is Ctrl+L
        // (help.tallysolutions.com/use-save-view-feature-in-tallyprime/: "Press Ctrl+L (Save View) to save the
        // report with the specific configurations"), and the shell now binds it. Ctrl+S still works as the
        // legacy alias it always was, but the empty state must name the chord the product documents.
        Status = Views.Count == 0 ? "No saved views yet — save one from a report with Ctrl+L." : string.Empty;
    }

    /// <summary>
    /// The Ctrl+H &gt; <b>Delete Saved Views</b> intent (see <see cref="ChangeViewMenu"/>). The vendor's Change
    /// View menu reaches this SAME list by two rows — one to apply a view, one to remove one — so this sets the
    /// panel's status line to say which verb the operator came for rather than opening a second, near-identical
    /// screen. A no-op on an empty list, where the empty-state sentence is the more useful thing to read.
    /// </summary>
    public void EnterDeleteMode()
    {
        if (Views.Count == 0) return;
        Status = "Highlight a view and choose Delete to remove it.";
    }

    /// <summary>Opens (applies) the highlighted saved view: raises <see cref="OpenRequested"/> so the shell opens a
    /// fresh report of the saved kind and re-applies the config (recomputing). A no-op with no selection.</summary>
    public void Open()
    {
        if (Selected is not { } item) return;
        OpenRequested?.Invoke(item.View);
    }

    /// <summary>Deletes the highlighted saved view from the company's store, then refreshes the list. A no-op with
    /// no selection.</summary>
    public void Delete()
    {
        if (Selected is not { } item) return;
        _storage.DeleteView(_company, item.Name);
        var name = item.Name;
        Reload();
        Status = $"Deleted view “{name}”.";
    }
}
