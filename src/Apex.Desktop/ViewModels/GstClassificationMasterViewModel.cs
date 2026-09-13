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

/// <summary>A GST-classification row for the master's existing-list grid.</summary>
public sealed class GstClassificationListRow
{
    public string Name { get; init; } = string.Empty;
    public string HsnSac { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string Taxability { get; init; } = string.Empty;
    public string IntegratedTax { get; init; } = string.Empty;
    public string CentralTax { get; init; } = string.Empty;
    public string StateTax { get; init; } = string.Empty;
    public string Cess { get; init; } = string.Empty;
    public string NatureOfTransaction { get; init; } = string.Empty;
}

/// <summary>
/// The <b>GST Classification</b> master (census row 6.25) — the vendor's <i>Gateway of Tally &gt; Create &gt; …
/// GST Classification</i> screen, "<i>a powerful tool for recording tax rates and other details for categories of
/// goods and services that attract a common GST rate</i>"
/// (<c>help.tallysolutions.com/tally-prime/gst-master-setup/india-gst-create-use-and-update-gst-classifications-tally/</c>).
///
/// <para>The form mirrors the vendor's two sections: <b>HSN/SAC &amp; Related Details</b> (HSN/SAC, Description,
/// Nature of Transaction) and <b>GST Rate &amp; Related Details</b> (Integrated Tax rate, with Central and State
/// shown auto-calculated, plus Cess and its valuation type and per-unit rate).</para>
///
/// <para>🔴 <b>Central and State tax are DISPLAY-ONLY here because they are derived, not stored</b> — see
/// <see cref="GstClassification"/>. Typing an integrated rate updates both immediately, which is the vendor's
/// "auto-calculated" behaviour and also makes it visible that they can never disagree with the integrated rate.
/// <b>Nature of Transaction is captured and inert</b>: the vendor names the field but publishes no value set and
/// no arithmetic that follows from one, so it is stored and shown and read by no computation.</para>
///
/// <para>Only reachable when GST is enabled (the Create-menu item is gated on <see cref="Company.GstEnabled"/>).
/// MVVM boundary: domain + persistence only, no Avalonia types.</para>
/// </summary>
public sealed partial class GstClassificationMasterViewModel : ViewModelBase, IMasterListExportSource
{
    private readonly Company _company;
    private readonly CompanyStorage _storage;
    private readonly Action _onChanged;

    /// <inheritdoc/>
    public MasterListSnapshot ToMasterListSnapshot() => new(
        "GST Classifications",
        new[]
        {
            MasterListColumn.Text("Name"), MasterListColumn.Text("HSN/SAC"),
            MasterListColumn.Text("Description"), MasterListColumn.Text("Taxability"),
            MasterListColumn.Text("Integrated Tax"), MasterListColumn.Text("Central Tax"),
            MasterListColumn.Text("State Tax"), MasterListColumn.Text("Cess"),
            MasterListColumn.Text("Nature of Transaction"),
        },
        Classifications.Select(c => (IReadOnlyList<string>)new[]
        {
            c.Name, c.HsnSac, c.Description, c.Taxability, c.IntegratedTax, c.CentralTax, c.StateTax,
            c.Cess, c.NatureOfTransaction,
        }).ToList());

    /// <summary>The existing classifications, refreshed after each create.</summary>
    public ObservableCollection<GstClassificationListRow> Classifications { get; } = new();

    public ObservableCollection<string> TaxabilityOptions { get; } =
        new(new[] { "Taxable", "Exempt", "Nil Rated", "Non-GST" });

    public ObservableCollection<string> SupplyTypeOptions { get; } = new(new[] { "Goods", "Services" });

    /// <summary>The vendor's "Cess Valuation Type" — "based on value and/or quantity".</summary>
    public ObservableCollection<string> CessValuationOptions { get; } =
        new(new[] { "Based on Value", "Based on Quantity" });

    // ---- Create form ----
    [ObservableProperty] private string _name = string.Empty;
    [ObservableProperty] private string _hsnSac = string.Empty;
    [ObservableProperty] private string _description = string.Empty;
    [ObservableProperty] private string _natureOfTransaction = string.Empty;
    [ObservableProperty] private string _selectedTaxability = "Taxable";
    [ObservableProperty] private string _selectedSupplyType = "Goods";
    [ObservableProperty] private string _integratedRateText = string.Empty;
    [ObservableProperty] private string _selectedCessValuation = "Based on Value";
    [ObservableProperty] private string _cessRateText = string.Empty;
    [ObservableProperty] private string _cessPerUnitText = string.Empty;
    [ObservableProperty] private string? _message;

    /// <summary>The auto-calculated Central Tax half of the typed integrated rate — the vendor's read-only field.</summary>
    public string CentralTaxDisplay => HalfDisplay(central: true);

    /// <summary>The auto-calculated State Tax half of the typed integrated rate — the vendor's read-only field.</summary>
    public string StateTaxDisplay => HalfDisplay(central: false);

    partial void OnIntegratedRateTextChanged(string value)
    {
        OnPropertyChanged(nameof(CentralTaxDisplay));
        OnPropertyChanged(nameof(StateTaxDisplay));
    }

    private string HalfDisplay(bool central)
    {
        if (!TryParseRateBp(IntegratedRateText, out var bp) || bp is null) return "—";
        // The SAME split GstClassification derives, so the screen cannot show a half the domain would not produce.
        var half = central ? bp.Value - bp.Value / 2 : bp.Value / 2;
        return $"{half / 100m:0.##}%";
    }

    public GstClassificationMasterViewModel(Company company, CompanyStorage storage, Action onChanged)
    {
        _company = company ?? throw new ArgumentNullException(nameof(company));
        _storage = storage ?? throw new ArgumentNullException(nameof(storage));
        _onChanged = onChanged ?? throw new ArgumentNullException(nameof(onChanged));
        RefreshList();
    }

    /// <summary>
    /// Ctrl+A: validates the form and adds a <see cref="GstClassification"/>, then persists. The domain enforces
    /// the HSN/SAC shape, the rate floor and the "a non-taxable class carries no positive rate" rule — the same
    /// three rules a Stock Group's GST block obeys — and <see cref="GstConfig.EnsureValid"/> enforces name
    /// uniqueness.
    /// </summary>
    public bool Create()
    {
        Message = null;

        if (_company.Gst is not { Enabled: true } gst)
        {
            Message = "Enable GST (F11 → Taxation) before creating a GST Classification.";
            return false;
        }

        var name = (Name ?? string.Empty).Trim();
        if (name.Length == 0) { Message = "A name is required (e.g. Textiles 5%)."; return false; }

        if (!TryParseRateBp(IntegratedRateText, out var rateBp))
        {
            Message = "Integrated Tax rate must be a percentage ≥ 0 (e.g. 18), or blank.";
            return false;
        }
        if (!TryParseRateBp(CessRateText, out var cessBp))
        {
            Message = "Cess rate must be a percentage ≥ 0, or blank.";
            return false;
        }
        if (!TryParseMoney(CessPerUnitText, out var cessPerUnit))
        {
            Message = "Cess rate per unit must be a rupee amount ≥ 0, or blank.";
            return false;
        }

        var hsn = (HsnSac ?? string.Empty).Trim();
        var description = (Description ?? string.Empty).Trim();
        var nature = (NatureOfTransaction ?? string.Empty).Trim();

        GstClassification classification;
        try
        {
            classification = new GstClassification(
                Guid.NewGuid(), name,
                hsn.Length == 0 ? null : hsn,
                description.Length == 0 ? null : description,
                ParseTaxability(SelectedTaxability),
                rateBp,
                SelectedSupplyType == "Services" ? GstSupplyType.Services : GstSupplyType.Goods,
                nature.Length == 0 ? null : nature,
                SelectedCessValuation == "Based on Quantity"
                    ? CessValuationMode.Specific
                    : CessValuationMode.AdValorem,
                cessBp ?? 0,
                cessPerUnit);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            Message = ex.Message;
            return false;
        }

        gst.AddClassification(classification);
        try
        {
            // Name uniqueness is a set-level rule on EnsureValid, so validate BEFORE persisting and roll back a
            // rejected addition rather than leaving a duplicate sitting in memory looking accepted.
            gst.EnsureValid();
            _storage.Save(_company);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            gst.RemoveClassification(classification.Id);
            Message = ex.Message;
            return false;
        }

        RefreshList();
        Message = $"GST Classification '{classification.Name}' created.";
        Name = string.Empty;
        HsnSac = string.Empty;
        Description = string.Empty;
        NatureOfTransaction = string.Empty;
        IntegratedRateText = string.Empty;
        CessRateText = string.Empty;
        CessPerUnitText = string.Empty;
        _onChanged();
        return true;
    }

    private static GstTaxability ParseTaxability(string? text) => text switch
    {
        "Exempt" => GstTaxability.Exempt,
        "Nil Rated" => GstTaxability.NilRated,
        "Non-GST" => GstTaxability.NonGst,
        _ => GstTaxability.Taxable,
    };

    /// <summary>Parses a percentage into basis points. Blank yields <c>null</c> (no rate declared) and true.</summary>
    private static bool TryParseRateBp(string? text, out int? basisPoints)
    {
        basisPoints = null;
        var t = (text ?? string.Empty).Trim();
        if (t.Length == 0) return true;
        if (!decimal.TryParse(t, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var pct) || pct < 0m)
            return false;
        basisPoints = (int)Math.Round(pct * 100m, MidpointRounding.AwayFromZero);
        return true;
    }

    private static bool TryParseMoney(string? text, out Money money)
    {
        money = Money.Zero;
        var t = (text ?? string.Empty).Trim();
        if (t.Length == 0) return true;
        if (!decimal.TryParse(t, NumberStyles.AllowDecimalPoint | NumberStyles.AllowThousands,
                CultureInfo.InvariantCulture, out var rupees) || rupees < 0m)
            return false;
        money = Money.FromRupees(rupees);
        return true;
    }

    private void RefreshList()
    {
        Classifications.Clear();
        if (_company.Gst is not { Enabled: true } gst) return;

        foreach (var c in gst.Classifications.OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase))
            Classifications.Add(new GstClassificationListRow
            {
                Name = c.Name,
                HsnSac = c.HsnSac ?? "—",
                Description = c.Description ?? "—",
                Taxability = c.Taxability.ToString(),
                IntegratedTax = c.RateBasisPoints is { } bp ? $"{bp / 100m:0.##}%" : "—",
                CentralTax = c.CentralTaxBasisPoints is { } cbp ? $"{cbp / 100m:0.##}%" : "—",
                StateTax = c.StateTaxBasisPoints is { } sbp ? $"{sbp / 100m:0.##}%" : "—",
                Cess = c.CessValuationMode == CessValuationMode.Specific
                    ? (c.CessPerUnit.Amount > 0 ? $"₹{IndianFormat.RupeesAlways(c.CessPerUnit)}/unit" : "—")
                    : (c.CessRateBasisPoints > 0 ? $"{c.CessRateBasisPoints / 100m:0.##}%" : "—"),
                NatureOfTransaction = c.NatureOfTransaction ?? "—",
            });
    }
}
