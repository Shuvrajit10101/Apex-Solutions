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

/// <summary>A Nature-of-Goods (§206C TCS category) row for the master's existing-list grid.</summary>
public sealed class NatureOfGoodsListRow
{
    public string CollectionCode { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string RateWithPan { get; init; } = string.Empty;
    public string RateWithoutPan { get; init; } = string.Empty;
    public string Threshold { get; init; } = string.Empty;

    /// <summary>"Predefined" / "Custom", plus " · legacy" for the year-gated §206C(1H) sale-of-goods entry.</summary>
    public string Kind { get; init; } = string.Empty;
}

/// <summary>
/// The <b>Nature of Goods</b> master ("Masters → Create → Statutory Masters → Nature of Goods (§206C)"; Phase 7
/// slice 1; mirrors <see cref="NatureOfPaymentMasterViewModel"/>). Lists the seeded predefined §206C set (scrap /
/// timber / tendu / liquor / minerals / 206C(1F) / 206C(1H)-legacy — FY 2025-26) and lets the user create a new
/// <b>custom</b> nature (a 27EQ collection code, a name, the with-PAN and no-PAN §206CC rates in %, and an optional
/// value threshold). A created nature is added to <see cref="TcsConfig.NaturesOfGoods"/> and the company persisted.
///
/// <para>Only reachable when TCS is enabled (the Create-menu item is gated on <see cref="Company.TcsEnabled"/>).
/// The legacy year-gated §206C(1H) nature is listed but flagged (non-operative for dates ≥ 01-Apr-2025). The
/// predefined masters are immutable (add-only domain), so this slice lists them and creates customs — it does not
/// edit a seeded nature. MVVM boundary: domain + persistence only, no Avalonia types.</para>
/// </summary>
public sealed partial class NatureOfGoodsMasterViewModel : ViewModelBase, IMasterListExportSource
{
    private readonly Company _company;
    private readonly CompanyStorage _storage;
    private readonly Action _onChanged;

    /// <inheritdoc/>
    public MasterListSnapshot ToMasterListSnapshot() => new(
        "Nature of Goods",
        new[]
        {
            MasterListColumn.Text("Code"), MasterListColumn.Text("Name"),
            MasterListColumn.Text("Rate (PAN)"), MasterListColumn.Text("Rate (no-PAN)"),
            MasterListColumn.Text("Threshold"), MasterListColumn.Text("Kind"),
        },
        Natures.Select(r => (IReadOnlyList<string>)new[]
        {
            r.CollectionCode, r.Name, r.RateWithPan, r.RateWithoutPan, r.Threshold, r.Kind,
        }).ToList());

    /// <summary>The existing natures (seeded predefined + any created customs), refreshed after each create.</summary>
    public ObservableCollection<NatureOfGoodsListRow> Natures { get; } = new();

    // ---- Create form ----
    [ObservableProperty] private string _collectionCode = string.Empty;
    [ObservableProperty] private string _name = string.Empty;
    [ObservableProperty] private string _rateWithPanText = string.Empty;
    [ObservableProperty] private string _rateWithoutPanText = "5";
    [ObservableProperty] private string _thresholdText = string.Empty;
    [ObservableProperty] private string? _message;

    public NatureOfGoodsMasterViewModel(Company company, CompanyStorage storage, Action onChanged)
    {
        _company = company ?? throw new ArgumentNullException(nameof(company));
        _storage = storage ?? throw new ArgumentNullException(nameof(storage));
        _onChanged = onChanged ?? throw new ArgumentNullException(nameof(onChanged));
        RefreshList();
    }

    /// <summary>
    /// Ctrl+A: validates the collection code + name are non-empty, the code is unique, the rates parse to a
    /// percentage ≥ 0, and any threshold parses to money ≥ 0; then adds a custom <see cref="NatureOfGoods"/>
    /// (base-includes-GST per §206C, non-legacy) and persists. Refreshes the list + clears the form.
    /// </summary>
    public bool Create()
    {
        Message = null;

        if (_company.Tcs is not { Enabled: true })
        {
            Message = "Enable TCS (F11 → Enable TCS) before adding a Nature of Goods.";
            return false;
        }

        var code = (CollectionCode ?? string.Empty).Trim();
        var name = (Name ?? string.Empty).Trim();

        if (string.IsNullOrWhiteSpace(code)) { Message = "A collection code is required (e.g. 6CE for scrap)."; return false; }
        if (string.IsNullOrWhiteSpace(name)) { Message = "A name is required (e.g. Scrap)."; return false; }
        if (_company.NaturesOfGoods.Any(n => string.Equals(n.CollectionCode, code, StringComparison.OrdinalIgnoreCase)))
        {
            Message = $"A Nature of Goods '{code}' already exists.";
            return false;
        }
        if (!TryParseRateBp(RateWithPanText, out var withPanBp))
        {
            Message = "Rate (with PAN) must be a percentage ≥ 0 (e.g. 1 for 1%).";
            return false;
        }
        if (!TryParseRateBp(RateWithoutPanText, out var withoutPanBp))
        {
            Message = "Rate (without PAN, §206CC) must be a percentage ≥ 0 (e.g. 5).";
            return false;
        }
        if (!TryParseThreshold(ThresholdText, out var threshold))
        {
            Message = "Threshold must be a rupee amount ≥ 0, or blank.";
            return false;
        }

        try
        {
            var nature = new NatureOfGoods(
                Guid.NewGuid(), code, name, withPanBp, withoutPanBp, code,
                threshold: threshold, baseIncludesGst: true,
                effectiveFrom: _company.FinancialYearStart, isPredefined: false);
            _company.Tcs.AddNatureOfGoods(nature);
            _storage.Save(_company);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            Message = ex.Message;
            return false;
        }

        RefreshList();
        Message = $"Nature of Goods '{code}' created ({withPanBp / 100m:0.##}% with PAN).";
        CollectionCode = string.Empty;
        Name = string.Empty;
        RateWithPanText = string.Empty;
        RateWithoutPanText = "5";
        ThresholdText = string.Empty;
        _onChanged();
        return true;
    }

    private static bool TryParseRateBp(string? text, out int basisPoints)
    {
        basisPoints = 0;
        var t = (text ?? string.Empty).Trim();
        if (t.Length == 0) return true;
        if (!decimal.TryParse(t, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var pct) || pct < 0m)
            return false;
        basisPoints = (int)Math.Round(pct * 100m, MidpointRounding.AwayFromZero);
        return true;
    }

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
        foreach (var n in _company.NaturesOfGoods
                     .OrderBy(n => n.CollectionCode, StringComparer.OrdinalIgnoreCase))
        {
            var kind = n.IsPredefined ? "Predefined" : "Custom";
            if (n.IsLegacy) kind += " · legacy";
            Natures.Add(new NatureOfGoodsListRow
            {
                CollectionCode = n.CollectionCode,
                Name = n.Name,
                RateWithPan = $"{n.RateWithPanBp / 100m:0.##}%",
                RateWithoutPan = $"{n.RateWithoutPanBp / 100m:0.##}%",
                Threshold = n.Threshold is { } t ? $"₹{t.Amount:#,##0}" : "—",
                Kind = kind,
            });
        }
    }
}
