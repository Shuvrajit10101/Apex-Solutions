using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Services;
using Apex.Desktop.Services;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Apex.Desktop.ViewModels;

/// <summary>A GST rate-class picker option (label + the enum value) for the rate-setup add form.</summary>
public sealed class GstRateClassOption
{
    public GstRateClass Value { get; init; }
    public string Display { get; init; } = string.Empty;
}

/// <summary>A GST valuation-basis picker option (Transaction Value / Retail Sale Price).</summary>
public sealed class GstValuationBasisOption
{
    public GstValuationBasis Value { get; init; }
    public string Display { get; init; } = string.Empty;
}

/// <summary>A cess valuation-mode picker option for the cess add form (ad-valorem / specific / RSP-factor).</summary>
public sealed class CessModeOption
{
    public CessValuationMode Value { get; init; }
    public string Display { get; init; } = string.Empty;
}

/// <summary>One read-only display row of the dated GST rate-history grid.</summary>
public sealed class GstRateHistoryDisplayRow
{
    public string Hsn { get; init; } = string.Empty;
    public string RateText { get; init; } = string.Empty;
    public string ClassText { get; init; } = string.Empty;
    public string FromText { get; init; } = string.Empty;
    public string ToText { get; init; } = string.Empty;
    public string BasisText { get; init; } = string.Empty;
    public string Label { get; init; } = string.Empty;
}

/// <summary>One read-only display row of the dated Compensation-Cess grid.</summary>
public sealed class GstCessDisplayRow
{
    public string Hsn { get; init; } = string.Empty;
    public string ModeText { get; init; } = string.Empty;
    public string ValueText { get; init; } = string.Empty;
    public string FromText { get; init; } = string.Empty;
    public string ToText { get; init; } = string.Empty;
    public string Label { get; init; } = string.Empty;
}

/// <summary>
/// The <b>GST Rate Setup</b> bulk-maintenance screen (Statutory → GST Rate Setup; Phase 9 slice 1; plan.md C-6 /
/// DP-24 / RQ-24). A keyboard-first master for the dated GST 2.0 rate framework: it lists the company's dated
/// <see cref="GstRateHistoryEntry"/> windows (HSN, rate %, class, effective from/to, valuation basis) and dated
/// <see cref="GstCessRate"/> windows, and lets the user <b>seed the GST 2.0 defaults</b> (0/5/18/40 + specials +
/// legacy inactive-by-date + the three FY2025-26 cess windows via <see cref="GstService.SeedAdvancedGst"/>) and
/// <b>append</b> new dated rate/cess windows en masse (a rate change is a new dated window, never an in-place edit —
/// so a pre-cut-over voucher always reprints at its historic rate).
///
/// <para>A company that never opts in keeps empty rate-history/cess and resolves exactly as Phase-4/8 (ER-13
/// byte-identical when off). MVVM boundary: domain + persistence only, no Avalonia types, headlessly unit-testable.</para>
/// </summary>
public sealed partial class GstRateSetupViewModel : ViewModelBase
{
    private readonly Company _company;
    private readonly CompanyStorage _storage;
    private readonly Action _onChanged;

    /// <summary>True iff GST is enabled — the rate-setup screen is only meaningful then.</summary>
    public bool GstEnabled => _company.GstEnabled;

    /// <summary>True once the advanced GST 2.0 rate-history / cess windows have been seeded (or added).</summary>
    public bool AdvancedGstSeeded =>
        (_company.Gst?.RateHistory.Count ?? 0) > 0 || (_company.Gst?.CessRates.Count ?? 0) > 0;

    /// <summary>True when no advanced-GST rows exist yet — drives the "seed the GST 2.0 defaults" prompt.</summary>
    public bool CanSeedDefaults => GstEnabled && !AdvancedGstSeeded;

    /// <summary>The dated rate-history rows (read-only display; newest window first).</summary>
    public ObservableCollection<GstRateHistoryDisplayRow> RateHistoryRows { get; } = new();

    /// <summary>The dated cess-rate rows (read-only display; newest window first).</summary>
    public ObservableCollection<GstCessDisplayRow> CessRows { get; } = new();

    /// <summary>The rate-class options for the rate add form.</summary>
    public ObservableCollection<GstRateClassOption> RateClasses { get; } = new();

    /// <summary>The valuation-basis options for the rate add form.</summary>
    public ObservableCollection<GstValuationBasisOption> ValuationBases { get; } = new();

    /// <summary>The cess valuation-mode options for the cess add form.</summary>
    public ObservableCollection<CessModeOption> CessModes { get; } = new();

    // ---- Add-a-rate-window form (append a new dated GST rate row) ----
    [ObservableProperty] private string _newRateHsn = string.Empty;
    [ObservableProperty] private string _newRatePercentText = string.Empty;
    [ObservableProperty] private GstRateClassOption? _newRateClass;
    [ObservableProperty] private string _newRateFromText = string.Empty;
    [ObservableProperty] private string _newRateToText = string.Empty;
    [ObservableProperty] private GstValuationBasisOption? _newRateBasis;
    [ObservableProperty] private string _newRateLabel = string.Empty;

    // ---- Add-a-cess-window form (append a new dated cess row) ----
    [ObservableProperty] private string _newCessHsn = string.Empty;
    [ObservableProperty] private CessModeOption? _newCessMode;
    [ObservableProperty] private string _newCessRatePercentText = string.Empty;
    [ObservableProperty] private string _newCessPerUnitText = string.Empty;
    [ObservableProperty] private string _newCessRspFactorText = string.Empty;
    [ObservableProperty] private string _newCessFromText = string.Empty;
    [ObservableProperty] private string _newCessToText = string.Empty;
    [ObservableProperty] private string _newCessLabel = string.Empty;

    [ObservableProperty] private string? _message;

    public GstRateSetupViewModel(Company company, CompanyStorage storage, Action onChanged)
    {
        _company = company ?? throw new ArgumentNullException(nameof(company));
        _storage = storage ?? throw new ArgumentNullException(nameof(storage));
        _onChanged = onChanged ?? throw new ArgumentNullException(nameof(onChanged));

        foreach (var (v, d) in new[]
        {
            (GstRateClass.Standard, "Standard (0/18)"),
            (GstRateClass.Merit, "Merit (5)"),
            (GstRateClass.Special, "Special (3/1.5/0.25)"),
            (GstRateClass.DeMerit, "De-Merit (40)"),
            (GstRateClass.CarveOut, "Carve-Out (28+cess)"),
            (GstRateClass.Legacy, "Legacy (12/28)"),
        })
            RateClasses.Add(new GstRateClassOption { Value = v, Display = d });
        NewRateClass = RateClasses.First();

        ValuationBases.Add(new GstValuationBasisOption { Value = GstValuationBasis.TransactionValue, Display = "Transaction Value" });
        ValuationBases.Add(new GstValuationBasisOption { Value = GstValuationBasis.RetailSalePrice, Display = "Retail Sale Price (RSP)" });
        NewRateBasis = ValuationBases.First();

        CessModes.Add(new CessModeOption { Value = CessValuationMode.AdValorem, Display = "Ad-valorem (% of value)" });
        CessModes.Add(new CessModeOption { Value = CessValuationMode.Specific, Display = "Specific (₹ per unit)" });
        CessModes.Add(new CessModeOption { Value = CessValuationMode.RetailSalePriceFactor, Display = "RSP-factor (× retail price)" });
        NewCessMode = CessModes.First();

        Refresh();
    }

    /// <summary>Seeds the GST 2.0 default rate-history + cess windows (idempotent) via the engine's advanced-GST
    /// opt-in and persists. Requires GST enabled. Any domain error is surfaced without crashing.</summary>
    public bool SeedDefaults()
    {
        Message = null;
        if (!GstEnabled)
        {
            Message = "Enable GST first (F11 → GST) before seeding the GST 2.0 rate framework.";
            return false;
        }
        try
        {
            new GstService(_company).SeedAdvancedGst();
            _storage.Save(_company);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            Message = ex.Message;
            return false;
        }
        Refresh();
        Message = "Seeded the GST 2.0 rate framework (0/5/18/40 + specials + legacy windows) and the FY2025-26 "
                  + "Compensation-Cess windows.";
        _onChanged();
        return true;
    }

    /// <summary>Appends a new dated GST rate-history window from the add form and persists. A blank HSN ⇒ a generic
    /// slab row; a blank "to" ⇒ open-ended. Rate is a percent (e.g. 18 ⇒ 1800 bp). Ctrl+A on the screen.</summary>
    public bool AddRateHistory()
    {
        Message = null;
        if (_company.Gst is not { } gst)
        {
            Message = "Enable GST first (F11 → GST) before adding a rate window.";
            return false;
        }

        var hsn = BlankToNull(NewRateHsn);
        if (hsn is not null && (hsn.Length is not (4 or 6 or 8) || !hsn.All(char.IsDigit)))
        {
            Message = $"HSN/SAC '{hsn}' must be 4, 6 or 8 digits (numeric), or blank for a generic slab.";
            return false;
        }
        if (!TryParsePercent(NewRatePercentText, "GST rate", out var bp)) return false;
        if (!TryParseDate(NewRateFromText, "Effective-from", required: true, out var from) || from is null) return false;
        if (!TryParseDate(NewRateToText, "Effective-to", required: false, out var to)) return false;

        var cls = (NewRateClass ?? RateClasses.First()).Value;
        var basis = (NewRateBasis ?? ValuationBases.First()).Value;
        var label = BlankToNull(NewRateLabel)
                    ?? $"{FormatPercent(bp)} ({(hsn is null ? "generic" : hsn)}, from {FormatDate(from.Value)})";

        // A10 fix (finding #5): resolution stays deterministic (most-recent effective-from wins), so an overlapping
        // window is NOT a correctness bug — surface an INFORMATIONAL note (never block the add) so the user knows the
        // new window shadows an existing same-HSN one for the overlapping dates.
        var overlaps = gst.RateHistory.Any(h => h.HsnSac == hsn
            && RangesOverlap(from.Value, to, h.EffectiveFrom, h.EffectiveTo));

        try
        {
            gst.AddRateHistory(new GstRateHistoryEntry(
                Guid.NewGuid(), hsn, bp, cls, from.Value, to, basis, label, isPredefined: false));
            _storage.Save(_company);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            Message = ex.Message;
            return false;
        }

        Refresh();
        Message = $"Added GST rate window '{label}'."
                  + (overlaps ? $" This window overlaps an existing one for HSN {(hsn ?? "generic")}; "
                                + "the most-recent effective-from wins." : string.Empty);
        NewRateHsn = string.Empty;
        NewRatePercentText = string.Empty;
        NewRateFromText = string.Empty;
        NewRateToText = string.Empty;
        NewRateLabel = string.Empty;
        _onChanged();
        return true;
    }

    /// <summary>Appends a new dated Compensation-Cess window from the add form and persists. The figure needed
    /// depends on the mode: ad-valorem % / specific ₹-per-unit / RSP-factor.</summary>
    public bool AddCess()
    {
        Message = null;
        if (_company.Gst is not { } gst)
        {
            Message = "Enable GST first (F11 → GST) before adding a cess window.";
            return false;
        }

        var hsn = BlankToNull(NewCessHsn);
        if (hsn is not null && (hsn.Length is not (4 or 6 or 8) || !hsn.All(char.IsDigit)))
        {
            Message = $"HSN/SAC '{hsn}' must be 4, 6 or 8 digits (numeric), or blank for a generic row.";
            return false;
        }

        var mode = (NewCessMode ?? CessModes.First()).Value;
        int cessBp = 0;
        Money perUnit = Money.Zero;
        int rspMillis = 0;
        switch (mode)
        {
            case CessValuationMode.AdValorem:
                if (!TryParsePercent(NewCessRatePercentText, "Ad-valorem cess", out cessBp)) return false;
                break;
            case CessValuationMode.Specific:
                if (!TryParseMoney(NewCessPerUnitText, "Specific cess per-unit", out perUnit)) return false;
                break;
            case CessValuationMode.RetailSalePriceFactor:
                if (!TryParseFactorMillis(NewCessRspFactorText, out rspMillis)) return false;
                break;
        }

        if (!TryParseDate(NewCessFromText, "Effective-from", required: true, out var from) || from is null) return false;
        if (!TryParseDate(NewCessToText, "Effective-to", required: false, out var to)) return false;

        var label = BlankToNull(NewCessLabel)
                    ?? $"Cess {(hsn is null ? "generic" : hsn)} (from {FormatDate(from.Value)})";

        // A10 fix (finding #5): informational-only overlap note (never blocks) — resolution is deterministic
        // (most-recent effective-from wins).
        var overlaps = gst.CessRates.Any(r => r.HsnSac == hsn
            && RangesOverlap(from.Value, to, r.EffectiveFrom, r.EffectiveTo));

        try
        {
            gst.AddCessRate(new GstCessRate(
                Guid.NewGuid(), hsn, mode, cessBp, perUnit, rspMillis, from.Value, to, label, isPredefined: false));
            _storage.Save(_company);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            Message = ex.Message;
            return false;
        }

        Refresh();
        Message = $"Added Compensation-Cess window '{label}'."
                  + (overlaps ? $" This window overlaps an existing one for HSN {(hsn ?? "generic")}; "
                                + "the most-recent effective-from wins." : string.Empty);
        NewCessHsn = string.Empty;
        NewCessRatePercentText = string.Empty;
        NewCessPerUnitText = string.Empty;
        NewCessRspFactorText = string.Empty;
        NewCessFromText = string.Empty;
        NewCessToText = string.Empty;
        NewCessLabel = string.Empty;
        _onChanged();
        return true;
    }

    /// <summary>Rebuilds the read-only display grids from the company's current rate-history + cess windows.</summary>
    private void Refresh()
    {
        RateHistoryRows.Clear();
        var history = _company.Gst?.RateHistory ?? Array.Empty<GstRateHistoryEntry>();
        foreach (var h in history.OrderByDescending(h => h.EffectiveFrom)
                                 .ThenBy(h => h.HsnSac ?? string.Empty, StringComparer.Ordinal)
                                 .ThenBy(h => h.RateBasisPoints))
            RateHistoryRows.Add(new GstRateHistoryDisplayRow
            {
                Hsn = h.HsnSac ?? "— generic",
                RateText = FormatPercent(h.RateBasisPoints),
                ClassText = h.RateClass.ToString(),
                FromText = FormatDate(h.EffectiveFrom),
                ToText = h.EffectiveTo is { } t ? FormatDate(t) : "open",
                BasisText = h.ValuationBasis == GstValuationBasis.RetailSalePrice ? "RSP" : "Txn Value",
                Label = h.Label,
            });

        CessRows.Clear();
        var cess = _company.Gst?.CessRates ?? Array.Empty<GstCessRate>();
        foreach (var c in cess.OrderByDescending(c => c.EffectiveFrom)
                              .ThenBy(c => c.HsnSac ?? string.Empty, StringComparer.Ordinal))
            CessRows.Add(new GstCessDisplayRow
            {
                Hsn = c.HsnSac ?? "— generic",
                ModeText = c.ValuationMode switch
                {
                    CessValuationMode.AdValorem => "Ad-valorem",
                    CessValuationMode.Specific => "Specific",
                    CessValuationMode.RetailSalePriceFactor => "RSP-factor",
                    _ => c.ValuationMode.ToString(),
                },
                ValueText = c.ValuationMode switch
                {
                    CessValuationMode.AdValorem => FormatPercent(c.CessRateBasisPoints),
                    CessValuationMode.Specific => $"₹{c.CessPerUnit.Amount.ToString("0.00", CultureInfo.InvariantCulture)}/unit",
                    CessValuationMode.RetailSalePriceFactor => $"{(c.CessRspFactorMillis / 1000m).ToString("0.###", CultureInfo.InvariantCulture)} × RSP",
                    _ => string.Empty,
                },
                FromText = FormatDate(c.EffectiveFrom),
                ToText = c.EffectiveTo is { } t ? FormatDate(t) : "open",
                Label = c.Label,
            });

        OnPropertyChanged(nameof(AdvancedGstSeeded));
        OnPropertyChanged(nameof(CanSeedDefaults));
    }

    // ---- parsing / formatting helpers ----

    private static string? BlankToNull(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    /// <summary>True iff the closed/open date ranges [<paramref name="aFrom"/>, <paramref name="aTo"/>] and
    /// [<paramref name="bFrom"/>, <paramref name="bTo"/>] intersect (a null "to" ⇒ open-ended / +∞). Used only for
    /// the informational overlap note on the add forms.</summary>
    private static bool RangesOverlap(DateOnly aFrom, DateOnly? aTo, DateOnly bFrom, DateOnly? bTo)
        => (bTo is null || aFrom <= bTo.Value) && (aTo is null || bFrom <= aTo.Value);

    private static string FormatPercent(int basisPoints) =>
        (basisPoints / 100m).ToString("0.##", CultureInfo.InvariantCulture) + "%";

    private static string FormatDate(DateOnly d) => ApexDate.Format(d);

    private bool TryParsePercent(string? text, string label, out int basisPoints)
    {
        basisPoints = 0;
        if (!decimal.TryParse((text ?? string.Empty).Trim(),
                NumberStyles.AllowDecimalPoint | NumberStyles.AllowThousands | NumberStyles.AllowLeadingSign,
                CultureInfo.InvariantCulture, out var percent) || percent < 0m)
        {
            Message = $"{label} must be a percent ≥ 0 (e.g. 18 for 18%).";
            return false;
        }
        basisPoints = (int)Math.Round(percent * 100m, MidpointRounding.AwayFromZero);
        return true;
    }

    private bool TryParseMoney(string? text, string label, out Money value)
    {
        value = Money.Zero;
        if (!decimal.TryParse((text ?? string.Empty).Trim(),
                NumberStyles.AllowDecimalPoint | NumberStyles.AllowThousands | NumberStyles.AllowLeadingSign,
                CultureInfo.InvariantCulture, out var amount) || amount < 0m)
        {
            Message = $"{label} must be a number ≥ 0 (₹, to the paisa).";
            return false;
        }
        var money = Money.FromRupees(amount);
        if (!money.IsPaisaExact)
        {
            Message = $"{label} {amount} must be to the paisa (2 decimal places).";
            return false;
        }
        value = money;
        return true;
    }

    private bool TryParseFactorMillis(string? text, out int millis)
    {
        millis = 0;
        if (!decimal.TryParse((text ?? string.Empty).Trim(),
                NumberStyles.AllowDecimalPoint | NumberStyles.AllowThousands | NumberStyles.AllowLeadingSign,
                CultureInfo.InvariantCulture, out var factor) || factor < 0m)
        {
            Message = "RSP-factor cess needs a factor ≥ 0 (e.g. 0.32 for 0.32 × RSP).";
            return false;
        }
        millis = (int)Math.Round(factor * 1000m, MidpointRounding.AwayFromZero);
        return true;
    }

    /// <summary>Parses an ISO (yyyy-MM-dd) or "dd-MMM-yyyy" date. A blank value is allowed only when
    /// <paramref name="required"/> is false (⇒ null = open-ended).</summary>
    private bool TryParseDate(string? text, string label, bool required, out DateOnly? value)
    {
        value = null;
        var t = (text ?? string.Empty).Trim();
        if (t.Length == 0)
        {
            if (required)
            {
                Message = $"{label} date is required (e.g. 22-Sep-2025 or 2025-09-22).";
                return false;
            }
            return true; // optional blank ⇒ open-ended
        }
        // WI-5: the ONE app-wide day-first parser. The old ladder fell through to a bare InvariantCulture
        // parse, so "03/04/2024" silently read as 4-Mar instead of 3-Apr.
        if (ApexDate.TryParse(t, out var d1))
        {
            value = d1;
            return true;
        }
        Message = $"{label}: {ApexDate.ErrorFor(t)}";
        return false;
    }
}
