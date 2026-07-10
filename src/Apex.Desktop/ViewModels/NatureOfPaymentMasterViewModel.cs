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

/// <summary>A Nature-of-Payment (TDS section) row for the master's existing-list grid.</summary>
public sealed class NatureOfPaymentListRow
{
    public string SectionCode { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string RateWithPan { get; init; } = string.Empty;
    public string RateWithoutPan { get; init; } = string.Empty;
    public string Threshold { get; init; } = string.Empty;
    public string FvuCode { get; init; } = string.Empty;

    /// <summary>"Predefined" for a seeded nature, else "Custom".</summary>
    public string Kind { get; init; } = string.Empty;
}

/// <summary>
/// The <b>Nature of Payment</b> master ("Masters → Create → Statutory Masters → Nature of Payment"; Phase 7
/// slice 1; mirrors <see cref="CurrencyMasterViewModel"/>/<see cref="LedgerMasterViewModel"/>). Lists the seeded
/// predefined TDS section set (194A/194C/194H/194I(a·b)/194J(a·b)/194Q — FY 2025-26) and lets the user create a
/// new <b>custom</b> nature (a section code, a name, the with-PAN and no-PAN §206AA rates in %, the Form-26Q/FVU
/// section code, and optional single-transaction / cumulative-FY thresholds). A created nature is added to the
/// company's <see cref="TdsConfig.NaturesOfPayment"/> and the whole company is persisted.
///
/// <para>Only reachable when TDS is enabled (the Create-menu item is gated on <see cref="Company.TdsEnabled"/>);
/// the predefined masters are immutable (add-only domain), so this slice lists them and creates customs — it does
/// not edit a seeded nature. MVVM boundary: domain + persistence only, no Avalonia types (headlessly testable).</para>
/// </summary>
public sealed partial class NatureOfPaymentMasterViewModel : ViewModelBase, IMasterListExportSource
{
    private readonly Company _company;
    private readonly CompanyStorage _storage;
    private readonly Action _onChanged;

    /// <inheritdoc/>
    public MasterListSnapshot ToMasterListSnapshot() => new(
        "Nature of Payment",
        new[]
        {
            MasterListColumn.Text("Section"), MasterListColumn.Text("Name"),
            MasterListColumn.Text("Rate (PAN)"), MasterListColumn.Text("Rate (no-PAN)"),
            MasterListColumn.Text("Threshold"), MasterListColumn.Text("FVU"), MasterListColumn.Text("Kind"),
        },
        Natures.Select(r => (IReadOnlyList<string>)new[]
        {
            r.SectionCode, r.Name, r.RateWithPan, r.RateWithoutPan, r.Threshold, r.FvuCode, r.Kind,
        }).ToList());

    /// <summary>The existing natures (seeded predefined + any created customs), refreshed after each create.</summary>
    public ObservableCollection<NatureOfPaymentListRow> Natures { get; } = new();

    // ---- Create form ----
    [ObservableProperty] private string _sectionCode = string.Empty;
    [ObservableProperty] private string _name = string.Empty;
    [ObservableProperty] private string _rateWithPanText = string.Empty;
    [ObservableProperty] private string _rateWithoutPanText = "20";
    [ObservableProperty] private string _fvuSectionCode = string.Empty;
    [ObservableProperty] private string _singleThresholdText = string.Empty;
    [ObservableProperty] private string _cumulativeThresholdText = string.Empty;
    [ObservableProperty] private string? _message;

    public NatureOfPaymentMasterViewModel(Company company, CompanyStorage storage, Action onChanged)
    {
        _company = company ?? throw new ArgumentNullException(nameof(company));
        _storage = storage ?? throw new ArgumentNullException(nameof(storage));
        _onChanged = onChanged ?? throw new ArgumentNullException(nameof(onChanged));
        RefreshList();
    }

    /// <summary>
    /// Ctrl+A: validates the section code + name + FVU code are non-empty, the section code is unique, the
    /// rates parse to a percentage ≥ 0, and any thresholds parse to money ≥ 0; then adds a custom
    /// <see cref="NatureOfPayment"/> to the company's TDS config and persists. Refreshes the list + clears the form.
    /// </summary>
    public bool Create()
    {
        Message = null;

        if (_company.Tds is not { Enabled: true })
        {
            Message = "Enable TDS (F11 → Enable TDS) before adding a Nature of Payment.";
            return false;
        }

        var section = (SectionCode ?? string.Empty).Trim();
        var name = (Name ?? string.Empty).Trim();
        var fvu = (FvuSectionCode ?? string.Empty).Trim();

        if (string.IsNullOrWhiteSpace(section)) { Message = "A section code is required (e.g. 194J(b))."; return false; }
        if (string.IsNullOrWhiteSpace(name)) { Message = "A name is required (e.g. Fees for professional services)."; return false; }
        if (string.IsNullOrWhiteSpace(fvu)) { Message = "A Form-26Q / FVU section code is required (e.g. 94J-B)."; return false; }
        if (_company.NaturesOfPayment.Any(n => string.Equals(n.SectionCode, section, StringComparison.OrdinalIgnoreCase)))
        {
            Message = $"A Nature of Payment '{section}' already exists.";
            return false;
        }
        if (!TryParseRateBp(RateWithPanText, out var withPanBp))
        {
            Message = "Rate (with PAN) must be a percentage ≥ 0 (e.g. 10 for 10%).";
            return false;
        }
        if (!TryParseRateBp(RateWithoutPanText, out var withoutPanBp))
        {
            Message = "Rate (without PAN, §206AA) must be a percentage ≥ 0 (e.g. 20).";
            return false;
        }
        if (!TryParseThreshold(SingleThresholdText, out var single))
        {
            Message = "Single-transaction threshold must be a rupee amount ≥ 0, or blank.";
            return false;
        }
        if (!TryParseThreshold(CumulativeThresholdText, out var cumulative))
        {
            Message = "Cumulative-FY threshold must be a rupee amount ≥ 0, or blank.";
            return false;
        }

        try
        {
            var nature = new NatureOfPayment(
                Guid.NewGuid(), section, name, withPanBp, withoutPanBp, fvu,
                singleTransactionThreshold: single, cumulativeThreshold: cumulative,
                effectiveFrom: _company.FinancialYearStart, isPredefined: false);
            _company.Tds.AddNatureOfPayment(nature);
            _storage.Save(_company);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            Message = ex.Message;
            return false;
        }

        RefreshList();
        Message = $"Nature of Payment '{section}' created ({withPanBp / 100m:0.##}% with PAN).";
        SectionCode = string.Empty;
        Name = string.Empty;
        RateWithPanText = string.Empty;
        RateWithoutPanText = "20";
        FvuSectionCode = string.Empty;
        SingleThresholdText = string.Empty;
        CumulativeThresholdText = string.Empty;
        _onChanged();
        return true;
    }

    /// <summary>Parses a percentage (e.g. "10" or "0.1") to basis points (1000 / 10); false if not a number ≥ 0.</summary>
    private static bool TryParseRateBp(string? text, out int basisPoints)
    {
        basisPoints = 0;
        var t = (text ?? string.Empty).Trim();
        if (t.Length == 0) return true; // blank ⇒ 0%
        if (!decimal.TryParse(t, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var pct) || pct < 0m)
            return false;
        basisPoints = (int)Math.Round(pct * 100m, MidpointRounding.AwayFromZero);
        return true;
    }

    /// <summary>Parses an optional rupee threshold to <see cref="Money"/>; blank ⇒ null; false if invalid.</summary>
    private static bool TryParseThreshold(string? text, out Money? money)
    {
        money = null;
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
        Natures.Clear();
        foreach (var n in _company.NaturesOfPayment
                     .OrderBy(n => n.SectionCode, StringComparer.OrdinalIgnoreCase))
        {
            Natures.Add(new NatureOfPaymentListRow
            {
                SectionCode = n.SectionCode,
                Name = n.Name,
                RateWithPan = $"{n.RateWithPanBp / 100m:0.##}%",
                RateWithoutPan = $"{n.RateWithoutPanBp / 100m:0.##}%",
                Threshold = DescribeThreshold(n),
                FvuCode = n.FvuSectionCode,
                Kind = n.IsPredefined ? "Predefined" : "Custom",
            });
        }
    }

    private static string DescribeThreshold(NatureOfPayment n)
    {
        var single = n.SingleTransactionThreshold;
        var cumulative = n.CumulativeThreshold;
        if (single is null && cumulative is null) return "—";
        var parts = new List<string>();
        if (single is { } s) parts.Add($"₹{s.Amount:#,##0} single");
        if (cumulative is { } c) parts.Add($"₹{c.Amount:#,##0}/FY");
        return string.Join(" · ", parts);
    }
}
