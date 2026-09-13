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

/// <summary>A GST-registration row for the master's existing-list grid.</summary>
public sealed class GstRegistrationListRow
{
    public string Name { get; init; } = string.Empty;
    public string StateName { get; init; } = string.Empty;
    public string Gstin { get; init; } = string.Empty;
    public string RegistrationType { get; init; } = string.Empty;
    public string Periodicity { get; init; } = string.Empty;
    public string ApplicableFrom { get; init; } = string.Empty;

    /// <summary>"Company (first)" for the registration held on the company itself, "Additional" for the rest.</summary>
    public string Kind { get; init; } = string.Empty;
}

/// <summary>
/// The <b>GST Registrations</b> master ("Masters → Create → Statutory Masters → GST Registration"; census row
/// 6.23) — the vendor's "<i>Create another GST Registration for the Company</i>"
/// (<c>help.tallysolutions.com/set-up-gst-details-in-company/</c>). Lists every registration the company holds,
/// the first one included, and creates additional ones in other States.
///
/// <para>The first registration is shown but not created here: it is the company's own GST block, keyed on
/// F11 → Taxation, and this screen surfaces it through <see cref="GstConfig.PrimaryRegistration"/> so the operator
/// sees one uniform list rather than having to know where each one is stored. Its <i>Registration Name</i> IS
/// editable here (<see cref="RenamePrimary"/>), because that field exists nowhere else.</para>
///
/// <para>Only reachable when GST is enabled (the Create-menu item is gated on <see cref="Company.GstEnabled"/>).
/// MVVM boundary: domain + persistence only, no Avalonia types.</para>
/// </summary>
public sealed partial class GstRegistrationsMasterViewModel : ViewModelBase, IMasterListExportSource
{
    private readonly Company _company;
    private readonly CompanyStorage _storage;
    private readonly Action _onChanged;

    /// <inheritdoc/>
    public MasterListSnapshot ToMasterListSnapshot() => new(
        "GST Registrations",
        new[]
        {
            MasterListColumn.Text("Registration Name"), MasterListColumn.Text("State"),
            MasterListColumn.Text("GSTIN/UIN"), MasterListColumn.Text("Type"),
            MasterListColumn.Text("GSTR-1 Periodicity"), MasterListColumn.Text("Applicable From"),
            MasterListColumn.Text("Kind"),
        },
        Registrations.Select(r => (IReadOnlyList<string>)new[]
        {
            r.Name, r.StateName, r.Gstin, r.RegistrationType, r.Periodicity, r.ApplicableFrom, r.Kind,
        }).ToList());

    /// <summary>Every registration the company holds — the first one, then the additional ones.</summary>
    public ObservableCollection<GstRegistrationListRow> Registrations { get; } = new();

    /// <summary>The recognised State/UT codes, offered as "&lt;code&gt; — &lt;name&gt;" for the picker.</summary>
    public ObservableCollection<string> StateOptions { get; } = new();

    /// <summary>The registration types the vendor offers on this screen.</summary>
    public ObservableCollection<string> RegistrationTypeOptions { get; } =
        new(new[] { "Regular", "Composition", "Unregistered", "Consumer" });

    /// <summary>The vendor's "Periodicity of GSTR-1" choices — "Monthly or Quarterly".</summary>
    public ObservableCollection<string> PeriodicityOptions { get; } = new(new[] { "Monthly", "Quarterly" });

    // ---- Create form ----
    [ObservableProperty] private string _name = string.Empty;
    [ObservableProperty] private string? _selectedState;
    [ObservableProperty] private string _gstin = string.Empty;
    [ObservableProperty] private string _selectedRegistrationType = "Regular";
    [ObservableProperty] private string _selectedPeriodicity = "Monthly";
    [ObservableProperty] private string _applicableFromText = string.Empty;
    [ObservableProperty] private bool _assesseeOfOtherTerritory;
    [ObservableProperty] private string _primaryNameText = string.Empty;
    [ObservableProperty] private string? _message;

    public GstRegistrationsMasterViewModel(Company company, CompanyStorage storage, Action onChanged)
    {
        _company = company ?? throw new ArgumentNullException(nameof(company));
        _storage = storage ?? throw new ArgumentNullException(nameof(storage));
        _onChanged = onChanged ?? throw new ArgumentNullException(nameof(onChanged));

        foreach (var s in IndianState.All.OrderBy(s => s.Code, StringComparer.Ordinal))
            StateOptions.Add($"{s.Code} — {s.Name}");

        PrimaryNameText = _company.Gst?.PrimaryRegistration?.Name ?? string.Empty;
        RefreshList();
    }

    /// <summary>
    /// Ctrl+A: validates the form and adds an additional <see cref="GstRegistration"/>, then persists. The
    /// domain enforces the per-registration rules (valid State, valid GSTIN, a GSTIN present for a
    /// Regular/Composition type) and <see cref="GstConfig.EnsureValid"/> the set-level ones (no two registrations
    /// in one State, no shared GSTIN) — this method surfaces those as messages rather than re-implementing them.
    /// </summary>
    public bool Create()
    {
        Message = null;

        if (_company.Gst is not { Enabled: true } gst)
        {
            Message = "Enable GST (F11 → Taxation) before adding a GST registration.";
            return false;
        }

        var stateCode = CodeOf(SelectedState);
        if (stateCode is null)
        {
            Message = "Select the State/UT this registration is in.";
            return false;
        }

        var gstin = (Gstin ?? string.Empty).Trim();
        var name = (Name ?? string.Empty).Trim();
        if (name.Length == 0) name = GstRegistration.DefaultNameFor(stateCode);   // the vendor auto-generates it

        DateOnly? applicableFrom = null;
        var fromText = (ApplicableFromText ?? string.Empty).Trim();
        if (fromText.Length > 0)
        {
            if (!DateOnly.TryParse(fromText, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
            {
                Message = "Applicable from must be a date (yyyy-MM-dd), or blank.";
                return false;
            }
            applicableFrom = parsed;
        }

        GstRegistration registration;
        try
        {
            registration = new GstRegistration(
                Guid.NewGuid(), name, stateCode,
                gstin.Length == 0 ? null : gstin,
                ParseRegistrationType(SelectedRegistrationType),
                applicableFrom,
                SelectedPeriodicity == "Quarterly" ? GstReturnPeriodicity.Quarterly : GstReturnPeriodicity.Monthly,
                AssesseeOfOtherTerritory);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            Message = ex.Message;
            return false;
        }

        gst.AddRegistration(registration);
        try
        {
            // The set-level rules live on EnsureValid, so validate BEFORE persisting and roll the addition back
            // if it is rejected — otherwise a duplicate State would sit in memory looking accepted.
            gst.EnsureValid();
            _storage.Save(_company);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            gst.RemoveRegistration(registration.Id);
            Message = ex.Message;
            return false;
        }

        RefreshList();
        Message = $"GST registration '{registration.Name}' created ({stateCode}). " +
                  "Vouchers can now be recorded under it (F3 on a voucher), and every GST return must name a registration.";
        Name = string.Empty;
        Gstin = string.Empty;
        ApplicableFromText = string.Empty;
        AssesseeOfOtherTerritory = false;
        _onChanged();
        return true;
    }

    /// <summary>
    /// Renames the company's FIRST registration — the vendor's editable <i>Registration Name</i>. Blanking it
    /// restores the auto-generated "&lt;State&gt; Registration".
    /// </summary>
    public bool RenamePrimary()
    {
        Message = null;
        if (_company.Gst is not { Enabled: true } gst)
        {
            Message = "Enable GST (F11 → Taxation) first.";
            return false;
        }

        var text = (PrimaryNameText ?? string.Empty).Trim();
        gst.PrimaryRegistrationName = text.Length == 0 ? null : text;
        _storage.Save(_company);
        RefreshList();
        PrimaryNameText = gst.PrimaryRegistration?.Name ?? string.Empty;
        Message = $"The company's own registration is now named '{PrimaryNameText}'.";
        _onChanged();
        return true;
    }

    private static GstRegistrationType ParseRegistrationType(string? text) => text switch
    {
        "Composition" => GstRegistrationType.Composition,
        "Unregistered" => GstRegistrationType.Unregistered,
        "Consumer" => GstRegistrationType.Consumer,
        _ => GstRegistrationType.Regular,
    };

    /// <summary>The 2-digit code out of a "&lt;code&gt; — &lt;name&gt;" picker entry, or null.</summary>
    private static string? CodeOf(string? option)
    {
        var text = (option ?? string.Empty).Trim();
        if (text.Length < 2) return null;
        var code = text[..2];
        return IndianState.IsValidCode(code) ? code : null;
    }

    private void RefreshList()
    {
        Registrations.Clear();
        if (_company.Gst is not { Enabled: true } gst) return;

        foreach (var r in gst.AllRegistrations)
            Registrations.Add(new GstRegistrationListRow
            {
                Name = r.Name,
                StateName = r.State is { } s ? $"{s.Code} — {s.Name}" : r.StateCode,
                Gstin = r.Gstin ?? "—",
                RegistrationType = r.RegistrationType.ToString(),
                Periodicity = r.Periodicity.ToString(),
                ApplicableFrom = r.ApplicableFrom is { } d ? d.ToString("dd-MMM-yyyy", CultureInfo.InvariantCulture) : "—",
                Kind = r.IsPrimary ? "Company (first)" : "Additional",
            });
    }
}
