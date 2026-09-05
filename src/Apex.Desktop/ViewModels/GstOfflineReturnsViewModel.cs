using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Io;
using Apex.Ledger.Reports;
using Apex.Desktop.Services;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Apex.Desktop.ViewModels;

/// <summary>The GST returns this application can write as an offline JSON file.</summary>
public enum GstOfflineReturnKind
{
    /// <summary>Form GSTR-1 — outward supplies (monthly / quarterly). Regular dealer.</summary>
    Gstr1,
    /// <summary>Form GSTR-3B — the summary return (monthly / quarterly). Regular dealer.</summary>
    Gstr3b,
    /// <summary>Form GSTR-9 — the annual return. Regular dealer.</summary>
    Gstr9,
    /// <summary>Form GSTR-9C — the annual reconciliation statement. Regular dealer.</summary>
    Gstr9c,
    /// <summary>Form CMP-08 — the composition quarterly self-assessed statement.</summary>
    Cmp08,
    /// <summary>Form GSTR-4 — the composition annual return.</summary>
    Gstr4,
    /// <summary>Form GSTR-9A — the composition annual return (the older annual form).</summary>
    Gstr9a,
}

/// <summary>One selectable return on the offline-returns page (its kind + the form label shown in the picker).</summary>
public sealed class GstOfflineReturnOption
{
    public GstOfflineReturnKind Kind { get; init; }
    public string Label { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public override string ToString() => Label;
}

/// <summary>One selectable filing period on the offline-returns page (a month, a quarter, or the whole year).</summary>
public sealed class GstReturnPeriodOption
{
    public DateOnly From { get; init; }
    public DateOnly To { get; init; }
    public string Label { get; init; } = string.Empty;
    public override string ToString() => Label;
}

/// <summary>One label/value line of the selected return's figure summary (already formatted for display).</summary>
public sealed class GstReturnFigureRow
{
    public string Label { get; init; } = string.Empty;
    public string Value { get; init; } = string.Empty;
}

/// <summary>
/// The <b>GST offline return files</b> page (Reports → Statutory Reports → GST Returns (Advanced) → Offline Return
/// Files (JSON), and → Composition Returns → Offline Return Files (JSON)). Pick a return form, a financial year and a
/// filing period; the page projects that return's figures from the pure engines and writes the offline JSON file
/// through <see cref="GstReturnJson"/>.
///
/// <para><b>Why this page exists (census row 6.10 / T1-11).</b> <see cref="GstReturnJson"/> shipped with five writers
/// and <b>zero production callers</b> — no screen, no menu, no keystroke reached any of them, so the return files a
/// dealer actually uploads could not be produced at all. This page is the route: it wires <b>all seven</b> writers
/// (the five that existed plus GSTR-1 and GSTR-3B) to one keyboard-first screen.</para>
///
/// <para><b>Gated by registration type (ER-13).</b> A Regular dealer is offered GSTR-1 / 3B / 9 / 9C; a Composition
/// dealer CMP-08 / GSTR-4 / GSTR-9A; a company with GST off is offered nothing and the page never opens.</para>
///
/// <para>🔴 <b>R7 / RULING 9.</b> The GSTN upload-payload schema for these forms is published only behind the
/// authenticated GST developer portal, so the JSON key names are <b>ours</b>, flagged in every file by its
/// <c>schemaStatus</c> field, and must not be recorded as source-verified. The <i>figures</i> come straight off the
/// pure engines and are locked by test.</para>
///
/// <para>MVVM boundary: engine + IO only, no Avalonia types (headlessly testable); deterministic (no clock/RNG beyond
/// the default financial year). Opening the page writes nothing — only <see cref="ExportJson"/> touches disk.</para>
/// </summary>
public sealed partial class GstOfflineReturnsViewModel : ViewModelBase
{
    private readonly Company _company;

    [ObservableProperty] private string _title = "GST Offline Return Files";
    [ObservableProperty] private string _subtitle = string.Empty;
    [ObservableProperty] private string _gstinText = string.Empty;
    [ObservableProperty] private string _statusText = string.Empty;
    [ObservableProperty] private string _schemaNoteText =
        "The portal upload schema for these forms is published only behind the authenticated GST developer portal, " +
        "so these files use this application's own key names and every file states that in its schemaStatus field.";
    [ObservableProperty] private string _exportFolder = string.Empty;
    [ObservableProperty] private string _exportStatus = string.Empty;

    private GstOfflineReturnOption? _selectedReturn;
    private GstAdvFyOption? _selectedYear;
    private GstReturnPeriodOption? _selectedPeriod;

    /// <summary>The return forms this company may file (empty when GST is off).</summary>
    public ObservableCollection<GstOfflineReturnOption> Returns { get; } = new();

    /// <summary>The financial years a return can be built for (the company FY + the two prior).</summary>
    public ObservableCollection<GstAdvFyOption> FinancialYears { get; } = new();

    /// <summary>The filing periods valid for the selected return within the selected year.</summary>
    public ObservableCollection<GstReturnPeriodOption> Periods { get; } = new();

    /// <summary>The selected return's figure summary (label + already-formatted value).</summary>
    public ObservableCollection<GstReturnFigureRow> Figures { get; } = new();

    public GstOfflineReturnsViewModel(Company company, GstOfflineReturnKind? preselect = null)
    {
        _company = company ?? throw new ArgumentNullException(nameof(company));

        foreach (var option in ApplicableReturns(company))
            Returns.Add(option);

        var fyStart = company.FinancialYearStart.Year;
        for (var y = fyStart; y >= fyStart - 2; y--)
            FinancialYears.Add(new GstAdvFyOption { StartYear = y });

        _selectedYear = FinancialYears.FirstOrDefault();
        _selectedReturn = (preselect is { } k ? Returns.FirstOrDefault(r => r.Kind == k) : null)
                          ?? Returns.FirstOrDefault();

        // Seed a real, findable export destination, exactly as every other shipped export page on this app does
        // (Form 16 / 16A / 24Q / 26Q, the ESI contribution report). Left empty, Path.Combine collapses to a bare file
        // name and File.WriteAllBytes drops the return file into the process working directory with no picker and no
        // way for the user to find it again. ExportFolderDefault is the one rule that guarantees a non-empty result
        // on every platform — My Documents resolves to "" on Linux when XDG user dirs are unconfigured.
        ExportFolder = Apex.Desktop.Services.ExportFolderDefault.Resolve();

        RebuildPeriods();
        Rebuild();
    }

    /// <summary>The return forms a company of this registration type actually files (ER-13).</summary>
    private static GstOfflineReturnOption[] ApplicableReturns(Company company) =>
        company.Gst switch
        {
            { Enabled: true, RegistrationType: GstRegistrationType.Regular } =>
            [
                new() { Kind = GstOfflineReturnKind.Gstr1, Label = "GSTR-1", Description = "Outward supplies" },
                new() { Kind = GstOfflineReturnKind.Gstr3b, Label = "GSTR-3B", Description = "Summary return" },
                new() { Kind = GstOfflineReturnKind.Gstr9, Label = "GSTR-9", Description = "Annual return" },
                new() { Kind = GstOfflineReturnKind.Gstr9c, Label = "GSTR-9C", Description = "Reconciliation statement" },
            ],
            { Enabled: true, RegistrationType: GstRegistrationType.Composition } =>
            [
                new() { Kind = GstOfflineReturnKind.Cmp08, Label = "CMP-08", Description = "Quarterly statement" },
                new() { Kind = GstOfflineReturnKind.Gstr4, Label = "GSTR-4", Description = "Composition annual return" },
                new() { Kind = GstOfflineReturnKind.Gstr9a, Label = "GSTR-9A", Description = "Composition annual return" },
            ],
            _ => [],
        };

    /// <summary>The selected return form; changing it re-derives the valid periods and re-projects the figures.</summary>
    public GstOfflineReturnOption? SelectedReturn
    {
        get => _selectedReturn;
        set
        {
            if (!SetProperty(ref _selectedReturn, value)) return;
            RebuildPeriods();
            Rebuild();
        }
    }

    /// <summary>The selected financial year; changing it re-derives the periods and re-projects the figures.</summary>
    public GstAdvFyOption? SelectedYear
    {
        get => _selectedYear;
        set
        {
            if (!SetProperty(ref _selectedYear, value)) return;
            RebuildPeriods();
            Rebuild();
        }
    }

    /// <summary>The selected filing period; changing it re-projects the figures.</summary>
    public GstReturnPeriodOption? SelectedPeriod
    {
        get => _selectedPeriod;
        set { if (SetProperty(ref _selectedPeriod, value)) Rebuild(); }
    }

    /// <summary>The financial year's first day for the selected year (the company's FY start month, that year).</summary>
    private DateOnly FyFrom =>
        new(SelectedYear?.StartYear ?? _company.FinancialYearStart.Year, _company.FinancialYearStart.Month, 1);

    /// <summary>Re-derives <see cref="Periods"/> for the selected return: the annual forms take the whole year, CMP-08
    /// the four quarters, GSTR-1 / GSTR-3B the twelve months.</summary>
    private void RebuildPeriods()
    {
        var previous = _selectedPeriod?.Label;
        Periods.Clear();
        var fyFrom = FyFrom;
        var fyTo = fyFrom.AddYears(1).AddDays(-1);

        switch (SelectedReturn?.Kind)
        {
            case GstOfflineReturnKind.Gstr1:
            case GstOfflineReturnKind.Gstr3b:
                for (var i = 0; i < 12; i++)
                {
                    var from = fyFrom.AddMonths(i);
                    Periods.Add(new GstReturnPeriodOption
                    {
                        From = from,
                        To = from.AddMonths(1).AddDays(-1),
                        Label = from.ToString("MMMM yyyy", CultureInfo.InvariantCulture),
                    });
                }
                break;

            case GstOfflineReturnKind.Cmp08:
                for (var i = 0; i < 4; i++)
                {
                    var from = fyFrom.AddMonths(3 * i);
                    var to = fyFrom.AddMonths(3 * (i + 1)).AddDays(-1);
                    Periods.Add(new GstReturnPeriodOption
                    {
                        From = from,
                        To = to,
                        Label = $"Q{i + 1} ({from.ToString("MMM", CultureInfo.InvariantCulture)}-" +
                                $"{to.ToString("MMM", CultureInfo.InvariantCulture)} {to.Year})",
                    });
                }
                break;

            default:
                Periods.Add(new GstReturnPeriodOption { From = fyFrom, To = fyTo, Label = "Full year" });
                break;
        }

        _selectedPeriod = Periods.FirstOrDefault(p => p.Label == previous) ?? Periods.FirstOrDefault();
        OnPropertyChanged(nameof(SelectedPeriod));
    }

    /// <summary>(Re)projects the selected return's figure summary. Never posts, never writes.</summary>
    public void Rebuild()
    {
        Figures.Clear();
        ExportStatus = string.Empty;
        GstinText = string.IsNullOrWhiteSpace(_company.Gst?.Gstin) ? "GSTIN —" : $"GSTIN {_company.Gst!.Gstin}";

        // Both are computed from the selected form + period and are BOUND in the view (the file-name placeholder).
        // Raised here — on every path, including the no-return-applies early return below — so the placeholder can
        // never keep naming a file the Export button is no longer going to write.
        OnPropertyChanged(nameof(FinancialPeriodCode));
        OnPropertyChanged(nameof(ExportFileName));

        if (SelectedReturn is null || SelectedPeriod is null)
        {
            Subtitle = _company.Name;
            StatusText = "No GST return applies — enable GST for this company first.";
            return;
        }

        var from = SelectedPeriod.From;
        var to = SelectedPeriod.To;
        Subtitle = $"{_company.Name}  —  Form {SelectedReturn.Label} ({SelectedReturn.Description})  —  " +
                   $"{ApexDate.Format(from)} to {ApexDate.Format(to)}";

        switch (SelectedReturn.Kind)
        {
            case GstOfflineReturnKind.Gstr1: ProjectGstr1(from, to); break;
            case GstOfflineReturnKind.Gstr3b: ProjectGstr3b(from, to); break;
            case GstOfflineReturnKind.Gstr9: ProjectGstr9(from, to); break;
            case GstOfflineReturnKind.Gstr9c: ProjectGstr9c(from, to); break;
            case GstOfflineReturnKind.Cmp08: ProjectCmp08(from, to); break;
            case GstOfflineReturnKind.Gstr4: ProjectGstr4(from, to); break;
            case GstOfflineReturnKind.Gstr9a: ProjectGstr9a(from, to); break;
        }
    }

    private void Add(string label, Money value) => Figures.Add(new GstReturnFigureRow
    {
        Label = label,
        Value = IndianFormat.AmountAlways(value),
    });

    private void AddCount(string label, int count) => Figures.Add(new GstReturnFigureRow
    {
        Label = label,
        Value = count.ToString(CultureInfo.InvariantCulture),
    });

    private void ProjectGstr1(DateOnly from, DateOnly to)
    {
        var r = Gstr1.Build(_company, from, to);
        AddCount("B2B invoices", r.B2B.Count);
        Add("B2B taxable value", new Money(r.B2B.Sum(b => b.TaxableValue.Amount)));
        AddCount("B2C rate rows", r.B2C.Count);
        Add("B2C taxable value", new Money(r.B2C.Sum(b => b.TaxableValue.Amount)));
        AddCount("Credit / debit notes (9B)", r.Table9B.Count);
        Add("Exempt / nil / non-GST value", r.ExemptNilNonGstValue);
        Add("Advance tax received (11A)", r.AdvanceTaxReceived);
        Add("Advance tax adjusted (11B)", r.AdvanceTaxAdjusted);
        AddCount("HSN summary rows (12)", r.HsnSummary.Count);
        Add("Total CGST", r.TotalCgst);
        Add("Total SGST", r.TotalSgst);
        Add("Total IGST", r.TotalIgst);
        Add("Total tax", r.TotalTax);
        StatusText = $"GSTR-1 ready — {r.B2B.Count} B2B invoice(s), total tax ₹{IndianFormat.AmountAlways(r.TotalTax)}.";
    }

    private void ProjectGstr3b(DateOnly from, DateOnly to)
    {
        var r = Gstr3b.Build(_company, from, to);
        Add("3.1(a) Taxable outward value", r.TaxableOutwardValue);
        Add("3.1(a) CGST", r.OutwardCgst);
        Add("3.1(a) SGST", r.OutwardSgst);
        Add("3.1(a) IGST", r.OutwardIgst);
        Add("3.1(c) Nil / exempt / non-GST", r.ExemptNilNonGstOutward);
        Add("3.1(d) Reverse-charge CGST", r.RcmOutwardCgst);
        Add("3.1(d) Reverse-charge SGST", r.RcmOutwardSgst);
        Add("3.1(d) Reverse-charge IGST", r.RcmOutwardIgst);
        Add("3.1(d) Reverse-charge Cess", r.RcmOutwardCess);
        Add("4(A)(2) ITC on import of services", r.RcmItcImportIgst);
        // 4(A)(3) is expressly "other than 1 & 2 above" (CBIC Circular No. 170/02/2022-GST, Table 2, table 4(A) row 3),
        // so it must NOT re-count the import-of-services IGST shown on the 4(A)(2) row above. TotalRcmItc spans BOTH
        // rows and is the wrong source here — TotalRcmItcOther is 4(A)(3) alone.
        Add("4(A)(3) ITC on other reverse-charge inward", r.TotalRcmItcOther);
        Add("4(A)(5) ITC CGST", r.ItcCgst);
        Add("4(A)(5) ITC SGST", r.ItcSgst);
        Add("4(A)(5) ITC IGST", r.ItcIgst);
        Add("4(B) ITC reversed", r.TotalItcReversed);
        Add("4(D)(1) ITC reclaimed", r.TotalItcReclaimed);
        Add("6.1 Net CGST", r.NetCgst);
        Add("6.1 Net SGST", r.NetSgst);
        Add("6.1 Net IGST", r.NetIgst);
        StatusText = "GSTR-3B ready — a negative net head is a carried-forward credit, shown as it stands.";
    }

    private void ProjectGstr9(DateOnly from, DateOnly to)
    {
        var r = Gstr9.Build(_company, from, to);
        Add("Table 4 taxable value", r.Table4TaxableValue);
        Add("Table 4 total tax", r.Table4TotalTax);
        Add("Table 5 exempt / nil / non-GST", r.Table5ExemptNilNonGst);
        Add("Table 5N total turnover", r.Table5NTurnover);
        Add("Table 6 ITC availed", r.Table6ItcAvailed);
        Add("Table 7 ITC reversed", r.Table7ItcReversed);
        Add("Table 8A ITC as per GSTR-2B", r.Table8A);
        Add("Table 8B ITC availed", r.Table8B);
        Add("Table 8D difference (8A − 8B)", r.Table8D);
        Add("Table 9 paid through ITC", r.Table9PaidThroughItc);
        Add("Table 9 paid in cash", r.Table9PaidInCash);
        AddCount("Table 17 HSN rows", r.Table17Hsn.Count);
        StatusText = r.Applicable
            ? "GSTR-9 ready."
            : "Not applicable — GSTR-9 is filed only by a Regular GST taxpayer.";
    }

    private void ProjectGstr9c(DateOnly from, DateOnly to)
    {
        var r = Gstr9c.Build(_company, from, to);
        Add("5A Turnover as per books", r.Table5ABooksTurnover);
        Add("5Q Turnover as per annual return", r.Table5QReturnTurnover);
        Add("5R Unreconciled turnover", r.Table5RUnreconciledTurnover);
        Add("9 Tax payable as per return", r.Table9TaxPerReturn);
        Add("9 Tax payable as per books", r.Table9TaxPerBooks);
        Add("11 Unreconciled tax", r.Table11UnreconciledTax);
        Add("12A ITC as per books", r.Table12ABooksItc);
        Add("12E Net ITC as per annual return", r.Table12EReturnItc);
        Add("12F Unreconciled ITC", r.Table12FUnreconciledItc);
        StatusText = r.Applicable
            ? "GSTR-9C ready — the unreconciled lines are reported as they stand, never forced to zero."
            : "Not applicable — GSTR-9C is filed only by a Regular GST taxpayer.";
    }

    private void ProjectCmp08(DateOnly from, DateOnly to)
    {
        var r = Cmp08.Build(_company, from, to);
        Add("Turnover base", r.TurnoverBase);
        Add("Outward turnover CGST", r.OutwardCgst);
        Add("Outward turnover SGST", r.OutwardSgst);
        Add("Inward reverse-charge CGST", r.InwardRcmCgst);
        Add("Inward reverse-charge SGST", r.InwardRcmSgst);
        Add("Inward reverse-charge IGST", r.InwardRcmIgst);
        Add("Inward reverse-charge Cess", r.InwardRcmCess);
        Add("3(iii) Payable CGST", r.PayableCgst);
        Add("3(iii) Payable SGST", r.PayableSgst);
        Add("3(iii) Payable IGST", r.PayableIgst);
        Add("3(iii) Payable Cess", r.PayableCess);
        Add("3(iv) Interest", r.Interest);
        StatusText = r.Applicable
            ? $"CMP-08 ready — composition rate {r.RateBasisPoints / 100m:0.##}%."
            : "Not applicable — CMP-08 is filed only by a Composition dealer.";
    }

    private void ProjectGstr4(DateOnly from, DateOnly to)
    {
        var r = Gstr4.Build(_company, from, to);
        AddCount("Table 5 quarters", r.Quarters.Count);
        Add("4A Registered inward value", r.Inward.RegisteredValue);
        Add("4B Reverse-charge inward value", r.Inward.ReverseChargeValue);
        Add("4B Reverse-charge inward tax", r.Inward.ReverseChargeTax);
        Add("4C Unregistered inward value", r.Inward.UnregisteredValue);
        Add("4D Import-of-services value", r.Inward.ImportServiceValue);
        Add("Table 6 annual composition tax", r.AnnualCompositionTax);
        Add("Table 6 annual reverse-charge tax", r.AnnualRcmTax);
        StatusText = r.Applicable
            ? "GSTR-4 ready — Table 6 is the sum of the four quarterly CMP-08 figures by construction."
            : "Not applicable — GSTR-4 is filed only by a Composition dealer.";
    }

    private void ProjectGstr9a(DateOnly from, DateOnly to)
    {
        var r = Gstr9a.Build(_company, from, to);
        Add("Total turnover", r.TotalTurnover);
        Add("Taxable turnover", r.TaxableTurnover);
        Add("Tax paid CGST", r.TaxPaidCgst);
        Add("Tax paid SGST", r.TaxPaidSgst);
        Add("Composition tax paid", r.CompositionTaxPaid);
        Add("Reverse-charge inward tax", r.RcmInwardTax);
        Add("Late fee", r.LateFee);
        StatusText = r.Applicable
            ? "GSTR-9A ready — the tax paid is the sum of the four quarterly CMP-08 figures by construction."
            : "Not applicable — GSTR-9A is filed only by a Composition dealer.";
    }

    /// <summary>The government financial-period string <c>MMYYYY</c> for the selected period's end month.</summary>
    public string FinancialPeriodCode =>
        SelectedPeriod is { } p
            ? p.To.Month.ToString("D2", CultureInfo.InvariantCulture) + p.To.Year.ToString("D4", CultureInfo.InvariantCulture)
            : string.Empty;

    /// <summary>The file name the export writes, e.g. <c>GSTR-1_27AAPFU0939F1ZV_042024.json</c>.</summary>
    public string ExportFileName =>
        SelectedReturn is null || SelectedPeriod is null
            ? string.Empty
            : $"{SelectedReturn.Label}_{(string.IsNullOrWhiteSpace(_company.Gst?.Gstin) ? "NOGSTIN" : _company.Gst!.Gstin)}" +
              $"_{FinancialPeriodCode}.json";

    /// <summary>Builds the offline JSON bytes for the selected return + period. Pure — writes nothing.</summary>
    public byte[] BuildJson()
    {
        if (SelectedReturn is null || SelectedPeriod is null) return [];
        var from = SelectedPeriod.From;
        var to = SelectedPeriod.To;
        return SelectedReturn.Kind switch
        {
            GstOfflineReturnKind.Gstr1 => GstReturnJson.Gstr1(_company, from, to),
            GstOfflineReturnKind.Gstr3b => GstReturnJson.Gstr3b(_company, from, to),
            GstOfflineReturnKind.Gstr9 => GstReturnJson.Gstr9(_company, from, to),
            GstOfflineReturnKind.Gstr9c => GstReturnJson.Gstr9c(_company, from, to),
            GstOfflineReturnKind.Cmp08 => GstReturnJson.Cmp08(_company, from, to),
            GstOfflineReturnKind.Gstr4 => GstReturnJson.Gstr4(_company, from, to),
            GstOfflineReturnKind.Gstr9a => GstReturnJson.Gstr9a(_company, from, to),
            _ => [],
        };
    }

    /// <summary>
    /// Ctrl+A / the Export button: writes the selected return's offline JSON to <see cref="ExportFolder"/> under
    /// <see cref="ExportFileName"/>. The bytes come straight off <see cref="BuildJson"/>; the write goes through the
    /// injectable <paramref name="writeBytes"/> seam (null ⇒ the real filesystem) so tests never touch disk. Returns
    /// true on success and sets <see cref="ExportStatus"/> either way.
    /// </summary>
    public bool ExportJson(Action<string, byte[]>? writeBytes = null)
    {
        if (SelectedReturn is null || SelectedPeriod is null)
        {
            ExportStatus = "Choose a return form and a filing period first.";
            return false;
        }

        try
        {
            var bytes = BuildJson();
            var folder = ExportFolder ?? string.Empty;
            var path = string.IsNullOrEmpty(folder) ? ExportFileName : Path.Combine(folder, ExportFileName);

            if (writeBytes is not null) writeBytes(path, bytes);
            else File.WriteAllBytes(path, bytes);

            ExportStatus = $"Exported {bytes.Length:#,0} bytes to {path}";
            return true;
        }
        catch (Exception ex)
        {
            ExportStatus = "Could not write the return file: " + ex.Message;
            return false;
        }
    }
}
