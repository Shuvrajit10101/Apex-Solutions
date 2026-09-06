using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Reports;
using Apex.Ledger.Services;
using Apex.Desktop.Services;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Apex.Desktop.ViewModels;

/// <summary>
/// A selectable <b>return period</b> on the Kerala Flood Cess screen — one calendar month, plus its statutory due
/// date.
///
/// <para>🔴 <b>Only months INSIDE the levy window are ever offered</b>, and that is the point of the type. The levy
/// ran 01-08-2019 → 31-07-2021 (see <see cref="KeralaFloodCess"/>), so there are exactly twenty-four return periods
/// that ever existed. A free-form date box would let an operator ask for August 2021 and read a zero back, which is
/// indistinguishable on screen from "we computed it and it was nil". Offering only the periods the levy had makes the
/// closed window visible in the UI itself rather than asserted in a caption.</para>
/// </summary>
public sealed class KfcPeriodOption
{
    /// <summary>First day of the return month.</summary>
    public DateOnly FirstDay { get; init; }

    /// <summary>Last day of the return month — clipped to the cessation date for the final, part-month period.</summary>
    public DateOnly LastDay { get; init; }

    /// <summary>The month label, e.g. "Aug 2019".</summary>
    public string Label => FirstDay.ToString("MMM yyyy", CultureInfo.InvariantCulture);

    /// <summary>
    /// The remittance due date: the 20th of the succeeding month. [KFC-FAQ] Q7 — "the tax due for a month has to be
    /// remitted on or before 20th of the succeeding month in GSTR 3B return. Due date for filing GSTR 3B shall be
    /// applicable for the Kerala Flood Cess return."
    /// </summary>
    public DateOnly DueDate => new DateOnly(FirstDay.Year, FirstDay.Month, 1).AddMonths(1).AddDays(19);

    public override string ToString() => Label;
}

/// <summary>One <b>rate slab</b> row of the Kerala Flood Cess return screen — the GST rate the turnover was taxed at,
/// the cess rate that slab bears, the leviable (GST-exclusive) turnover and the cess on it. Strings, formatted once
/// in the view model, so the view binds and never computes.</summary>
public sealed class KfcSlabRowVm
{
    /// <summary>The GST rate the turnover was taxed at, e.g. "12%".</summary>
    public string GstRate { get; init; } = string.Empty;

    /// <summary>Which S.R.O. 360/2017 schedule limb that rate falls in, in words.</summary>
    public string Schedule { get; init; } = string.Empty;

    /// <summary>The Kerala Flood Cess rate for the slab, e.g. "1%" or "0.25%".</summary>
    public string CessRate { get; init; } = string.Empty;

    /// <summary>The leviable turnover at this GST rate, GST-exclusive.</summary>
    public string Turnover { get; init; } = string.Empty;

    /// <summary>The Kerala Flood Cess on that turnover.</summary>
    public string Cess { get; init; } = string.Empty;
}

/// <summary>
/// The <b>Kerala Flood Cess return</b> report page (census row 6.26), reached at
/// <b>Reports → Statutory Reports → Kerala Flood Cess</b>. A read-only projection over the pure
/// <see cref="KeralaFloodCessReturnBuilder"/>: pick one of the levy's own return periods and it shows the outward
/// turnover leviable to the cess grouped by GST rate, the cess payable, the remittance due date, and the two figures
/// the projection excluded. <b>Ctrl+A</b> / the Export button writes the return as a deterministic CSV to the export
/// folder; <b>Alt+B</b> saves that file and returns to the menu.
///
/// <para>🔴 <b>THIS SCREEN IS A HISTORICAL ONE AND SAYS SO ON ITS FACE.</b> The levy lapsed on 31 July 2021, so no
/// voucher posted today can bear it. The screen exists because a Kerala book kept during 2019-21 must still be
/// projectable and reprintable — which is what accounting software is for — and it is surfaced only for a company
/// that could actually have such a book (see <c>MainWindowViewModel.OpenKeralaFloodCessReturn</c>'s gate). It never
/// offers a period outside the window, so it cannot render a zero that an operator could mistake for a computed nil.</para>
///
/// <para><b>Every statutory rule applied here is cited in <see cref="KeralaFloodCess"/>, not here.</b> This type
/// chooses a period, calls the engine and formats the answer; it decides nothing about the law. MVVM boundary:
/// engine + formatting only, no Avalonia types (headlessly testable); deterministic — no clock, no RNG.</para>
/// </summary>
public sealed partial class KeralaFloodCessReturnViewModel : ViewModelBase
{
    /// <summary>
    /// 🔴 The verbatim statement of the one judgement the projection makes that a filer must be able to see, shown on
    /// the screen rather than buried in a comment. [KFC-FAQ] Q12 exempts a supply to a Kerala-registered taxable
    /// person only where it is made <b>in furtherance of business</b>, and Q18/Q19 levy the cess where it is not.
    /// No master or voucher in this application records that fact, so the ordinary reading is taken — and named.
    /// </summary>
    public const string FurtheranceOfBusinessNote =
        "Supplies to a Kerala-registered buyer are treated as made in furtherance of business and excluded "
        + "(FAQ Q12). This book records no per-supply \"furtherance of business\" flag, so a supply to a "
        + "registered buyer for non-business use (FAQ Q18) is not separately identified here.";

    /// <summary>🔴 The lapse, stated on the screen. The levy is not in force and no current voucher can bear it.</summary>
    public const string LapseNote =
        "The Kerala Flood Cess was levied from 01-Aug-2019 for two years (S.R.O. 436/2019) and lapsed on "
        + "31-Jul-2021. This return is available for those periods only.";

    private readonly Company _company;

    [ObservableProperty] private string _title = "Kerala Flood Cess Return";
    [ObservableProperty] private string _subtitle = string.Empty;
    [ObservableProperty] private string _periodText = string.Empty;
    [ObservableProperty] private string _dueDateText = string.Empty;

    // Return footings — always rendered, so a nil return is a stated nil rather than a blank screen.
    [ObservableProperty] private string _totalTurnoverText = "0.00";
    [ObservableProperty] private string _totalCessText = "0.00";

    // The two figures the projection EXCLUDED, disclosed rather than silently dropped.
    [ObservableProperty] private string _exemptedBusinessTurnoverText = "0.00";
    [ObservableProperty] private string _turnoverOutsideTheSchedulesText = "0.00";

    [ObservableProperty] private bool _isEmpty;
    [ObservableProperty] private string _statusText = string.Empty;

    // Export knobs (the same seam every other statutory report uses; the CSV is byte-stable off the engine figures).
    [ObservableProperty] private string _exportFolder = string.Empty;
    [ObservableProperty] private string _exportStatus = string.Empty;

    private KfcPeriodOption? _selectedPeriod;

    /// <summary>🔴 The return periods the levy actually had — Aug 2019 … Jul 2021, and nothing else.</summary>
    public ObservableCollection<KfcPeriodOption> Periods { get; } = new();

    /// <summary>The leviable rate slabs of the selected period, ordered by GST rate.</summary>
    public ObservableCollection<KfcSlabRowVm> Rows { get; } = new();

    /// <summary>The honesty line about the "in furtherance of business" reading — bound by the view, not a comment.</summary>
    public string FurtheranceNote => FurtheranceOfBusinessNote;

    /// <summary>The lapse statement — bound by the view.</summary>
    public string LapseText => LapseNote;

    public KeralaFloodCessReturnViewModel(Company company)
    {
        _company = company ?? throw new ArgumentNullException(nameof(company));

        // Exactly the months the levy covered. The final period is a part month (01-Jul-2021 … 31-Jul-2021 happens to
        // be whole, but the clip is written so a future correction to Cessation cannot silently over-run the window).
        for (var first = new DateOnly(KeralaFloodCess.Commencement.Year, KeralaFloodCess.Commencement.Month, 1);
             first <= KeralaFloodCess.Cessation;
             first = first.AddMonths(1))
        {
            var start = first < KeralaFloodCess.Commencement ? KeralaFloodCess.Commencement : first;
            var monthEnd = first.AddMonths(1).AddDays(-1);
            var end = monthEnd > KeralaFloodCess.Cessation ? KeralaFloodCess.Cessation : monthEnd;
            Periods.Add(new KfcPeriodOption { FirstDay = start, LastDay = end });
        }

        _selectedPeriod = Periods.FirstOrDefault();

        // W2-03: ONE seam. Environment.GetFolderPath returns "" on a platform with no such folder (a Linux CI
        // container with no HOME), and an empty folder makes Path.Combine collapse to a bare file name.
        ExportFolder = Apex.Desktop.Services.ExportFolderDefault.Resolve();

        Rebuild();
    }

    /// <summary>The selected return period; changing it rebuilds the return.</summary>
    public KfcPeriodOption? SelectedPeriod
    {
        get => _selectedPeriod;
        set
        {
            if (!SetProperty(ref _selectedPeriod, value)) return;
            Rebuild();
        }
    }

    /// <summary>Rebuilds the return for the selected period off the pure engine.</summary>
    public void Rebuild()
    {
        Rows.Clear();
        ExportStatus = string.Empty;

        var period = _selectedPeriod;
        if (period is null)
        {
            // Unreachable while the window has months in it, but a null period must not throw.
            PeriodText = "—";
            DueDateText = "—";
            IsEmpty = true;
            StatusText = LapseNote;
            Subtitle = _company.Name;
            return;
        }

        var ret = KeralaFloodCessReturnBuilder.Build(_company, period.FirstDay, period.LastDay);

        PeriodText = $"{Day(period.FirstDay)} to {Day(period.LastDay)}";
        DueDateText = Day(period.DueDate);
        Subtitle = $"{_company.Name} · GSTIN {_company.Gst?.Gstin ?? "—"} · Kerala (State code 32)";

        foreach (var slab in ret.Slabs)
        {
            Rows.Add(new KfcSlabRowVm
            {
                GstRate = RatePercent(slab.GstRateBasisPoints),
                Schedule = ScheduleWords(slab.Limb),
                CessRate = RatePercent(slab.CessRateBasisPoints),
                Turnover = IndianFormat.AmountAlways(slab.Turnover),
                Cess = IndianFormat.AmountAlways(slab.Cess),
            });
        }

        // AmountAlways, not Amount: IndianFormat.Amount renders a zero as BLANK (the empty-cell convention in the
        // report grids), and a footing that goes blank on a nil return is indistinguishable from one that failed to
        // compute. A nil return must STATE its nil, so every footing here is always rendered.
        TotalTurnoverText = IndianFormat.AmountAlways(ret.TotalTurnover);
        TotalCessText = IndianFormat.AmountAlways(ret.TotalCess);
        ExemptedBusinessTurnoverText = IndianFormat.AmountAlways(ret.ExemptedBusinessTurnover);
        TurnoverOutsideTheSchedulesText = IndianFormat.AmountAlways(ret.TurnoverOutsideTheSchedules);

        IsEmpty = ret.IsEmpty;
        StatusText = ret.IsEmpty
            ? "No outward supply leviable to the Kerala Flood Cess in this period — a nil return."
            : $"{ret.Slabs.Count} rate slab(s) leviable. Remit on or before {DueDateText} (FAQ Q7).";
    }

    /// <summary>
    /// 🔴 <b>Formats a date the ONE way this screen and its CSV are allowed to.</b> Interpolating a
    /// <see cref="DateOnly"/> directly (<c>$"{d:dd-MMM-yyyy}"</c>) formats it in the <b>current culture</b>, which
    /// changes both the month abbreviation and — under a culture whose default calendar is not Gregorian, e.g.
    /// <c>ar-SA</c> — the <b>year and month numbers themselves</b>. The gate runs on ubuntu and macOS as well as
    /// Windows, and seven platform assumptions have already escaped it here, so every date this type renders or
    /// writes goes through this method and is pinned to the invariant culture.
    /// </summary>
    private static string Day(DateOnly date) => date.ToString("dd-MMM-yyyy", CultureInfo.InvariantCulture);

    /// <summary>The export file's period stamp, <c>yyyy-MM</c>, invariant for the same reason as
    /// <see cref="Day"/> — a non-Gregorian current culture would otherwise name the file for a different month.</summary>
    private static string FileStamp(DateOnly date) => date.ToString("yyyy-MM", CultureInfo.InvariantCulture);

    /// <summary>Formats a basis-point rate as a percentage, e.g. 1200 → "12%", 25 → "0.25%".</summary>
    private static string RatePercent(int basisPoints) =>
        (basisPoints / 100m).ToString("0.####", CultureInfo.InvariantCulture) + "%";

    /// <summary>
    /// Names the S.R.O. 360/2017 schedule limb in words, so the screen states WHY a slab bears its rate. Kept short
    /// enough to render inside the 150px Schedule column without ellipsis — the full wording is in
    /// <see cref="KeralaFloodCess.LimbFor"/>, which is where the join between the FAQ's schedule names and the GST
    /// rates is set out and cited.
    /// </summary>
    private static string ScheduleWords(KfcRateLimb limb) => limb switch
    {
        KfcRateLimb.Standard => "Sch. II / III / IV",
        KfcRateLimb.Reduced => "Sch. V — gold etc.",
        _ => "—",
    };

    /// <summary>
    /// Writes the selected period's return as a deterministic CSV to <see cref="ExportFolder"/> and reports the path
    /// in <see cref="ExportStatus"/>. Byte-stable off the engine figures — no clock, no culture drift (the invariant
    /// culture is used throughout), so the same book and period always produce the same bytes.
    /// </summary>
    public void ExportReturn()
    {
        var period = _selectedPeriod;
        if (period is null) { ExportStatus = "No return period selected."; return; }

        var sb = new StringBuilder();
        sb.AppendLine("Kerala Flood Cess Return");
        sb.AppendLine($"Company,{Csv(_company.Name)}");
        sb.AppendLine($"GSTIN,{Csv(_company.Gst?.Gstin ?? string.Empty)}");
        sb.AppendLine($"Period,{Day(period.FirstDay)},{Day(period.LastDay)}");
        sb.AppendLine($"Due date,{Day(period.DueDate)}");
        sb.AppendLine();
        sb.AppendLine("GST Rate,Schedule,Cess Rate,Leviable Turnover,Cess");
        foreach (var r in Rows)
            sb.AppendLine($"{Csv(r.GstRate)},{Csv(r.Schedule)},{Csv(r.CessRate)},{Csv(r.Turnover)},{Csv(r.Cess)}");
        sb.AppendLine($"Total,,,{Csv(TotalTurnoverText)},{Csv(TotalCessText)}");
        sb.AppendLine();
        sb.AppendLine($"Excluded - supply to a Kerala-registered buyer,{Csv(ExemptedBusinessTurnoverText)}");
        sb.AppendLine($"Excluded - rates outside the cess schedules,{Csv(TurnoverOutsideTheSchedulesText)}");
        sb.AppendLine();
        sb.AppendLine(Csv(LapseNote));
        sb.AppendLine(Csv(FurtheranceOfBusinessNote));

        var file = $"KeralaFloodCess-{FileStamp(period.FirstDay)}.csv";
        var folder = string.IsNullOrWhiteSpace(ExportFolder)
            ? Apex.Desktop.Services.ExportFolderDefault.Resolve()
            : ExportFolder;
        try
        {
            Directory.CreateDirectory(folder);
            var path = Path.Combine(folder, file);
            File.WriteAllText(path, sb.ToString());
            ExportStatus = $"Saved {path}";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException
                                      or ArgumentException)
        {
            ExportStatus = $"Could not write the return: {ex.Message}";
        }
    }

    /// <summary>Quotes a CSV field. Everything is quoted, so a comma or a quote in a company name cannot shift a column.</summary>
    private static string Csv(string value) => "\"" + value.Replace("\"", "\"\"") + "\"";
}
