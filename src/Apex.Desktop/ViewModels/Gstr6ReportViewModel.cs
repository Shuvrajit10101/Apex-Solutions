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

/// <summary>A selectable financial year on the GSTR-6 screen (its start year + the "2025-26" label).</summary>
public sealed class Gstr6FyOption
{
    public int StartYear { get; init; }
    public string Label => $"{StartYear}-{(StartYear + 1) % 100:00}";
    public override string ToString() => Label;
}

/// <summary>A selectable return month. GSTR-6 is monthly — Rule 39(1)(a) requires the month's credit to be
/// distributed in that same month — so the return is built one calendar month at a time.</summary>
public sealed class Gstr6MonthOption
{
    public DateOnly FirstDay { get; init; }
    public DateOnly LastDay => FirstDay.AddMonths(1).AddDays(-1);
    public string Label => FirstDay.ToString("MMM yyyy", CultureInfo.InvariantCulture);
    public override string ToString() => Label;
}

/// <summary>A selectable Input Service Distributor registration — the GSTIN the return is filed under.</summary>
public sealed class Gstr6IsdOption
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string? Gstin { get; init; }
    public string Label => string.IsNullOrWhiteSpace(Gstin) ? Name : $"{Name} — {Gstin}";
    public override string ToString() => Label;
}

/// <summary>One distribution row on screen: a recipient registration and what it receives, by head.</summary>
public sealed class Gstr6RowVm
{
    public string Recipient { get; init; } = string.Empty;
    public string StateCode { get; init; } = string.Empty;
    public string Gstin { get; init; } = string.Empty;
    public string Eligibility { get; init; } = string.Empty;
    public string Cgst { get; init; } = "0.00";
    public string Sgst { get; init; } = "0.00";
    public string Igst { get; init; } = "0.00";
    public string Cess { get; init; } = "0.00";
    public string Total { get; init; } = "0.00";

    /// <summary>
    /// The row's second line — <b>State · eligibility · GSTIN</b>.
    ///
    /// <para>🔴 <b>Why the State and the eligibility are here and not in columns of their own.</b> The grid's first
    /// shape gave each a fixed track, which starved the star Recipient column to 34px in a 638px pane and
    /// ellipsised a registration name away entirely — caught by the repo's own
    /// <c>XamlLayoutInvariantTests.Star_columns_must_stay_wide_enough_to_read</c>. Both facts still have to be
    /// <i>visible</i>, because Rule 39(1)(j) turns on the recipient's State and Rule 39(1)(g) forbids merging
    /// eligible with ineligible credit — so they moved here rather than being dropped.</para>
    /// </summary>
    public string SubLine =>
        string.Join("  ·  ", new[] { StateCode, Eligibility, Gstin }.Where(s => !string.IsNullOrWhiteSpace(s)));
}

/// <summary>
/// The <b>FORM GSTR-6</b> report page (Reports → Statutory Reports → GST Returns (Advanced) → GSTR-6 (ISD);
/// census row 6.24). A read-only projection over the pure <see cref="Gstr6"/> engine: pick an ISD registration, a
/// financial year and a month, and it shows the credit received for distribution, the relevant period the pro rata
/// is based on, and one row per recipient registration per eligibility.
///
/// <para><b>Gated on the company actually holding an ISD registration</b> — the menu row and this open path both
/// check <c>Gst.HasIsdRegistration</c> — so a company that has never created one sees exactly the Gateway it saw
/// before this slice (ER-13).</para>
///
/// <para>🔴 <b>The two figures that matter most are deliberately given their own properties rather than left to be
/// added up off the grid.</b> <see cref="TotalReceivedText"/> against <see cref="TotalDistributedText"/> is the
/// Rule 39(1)(b) check — "<i>the amount of the credit distributed shall not exceed the amount of credit available for
/// distribution</i>" — and <see cref="UndistributedText"/> is their difference. A filer who cannot see that
/// difference cannot see the one error that matters.</para>
///
/// <para>MVVM boundary: engine only, no Avalonia types (headlessly testable); deterministic (no clock, no RNG).</para>
/// </summary>
public sealed partial class Gstr6ReportViewModel : ViewModelBase
{
    private readonly Company _company;

    [ObservableProperty] private string _title = "Form GSTR-6 — Input Service Distributor Return";
    [ObservableProperty] private string _subtitle = string.Empty;
    [ObservableProperty] private string _distributorText = string.Empty;
    [ObservableProperty] private string _dueDateText = string.Empty;
    [ObservableProperty] private string _relevantPeriodText = string.Empty;

    // Table 3/4 — credit received for distribution.
    [ObservableProperty] private string _receivedCgstText = "0.00";
    [ObservableProperty] private string _receivedSgstText = "0.00";
    [ObservableProperty] private string _receivedIgstText = "0.00";
    [ObservableProperty] private string _receivedCessText = "0.00";
    [ObservableProperty] private string _totalReceivedText = "0.00";
    [ObservableProperty] private string _eligibleCreditText = "0.00";
    [ObservableProperty] private string _ineligibleCreditText = "0.00";

    // Table 5/6 — the distribution footing.
    [ObservableProperty] private string _totalDistributedText = "0.00";
    [ObservableProperty] private string _undistributedText = "0.00";

    [ObservableProperty] private string _statusText = string.Empty;
    [ObservableProperty] private string? _message;

    private Gstr6FyOption? _selectedYear;
    private Gstr6MonthOption? _selectedMonth;
    private Gstr6IsdOption? _selectedIsd;

    /// <summary>The financial years the return can be built for (the company FY and the two prior).</summary>
    public ObservableCollection<Gstr6FyOption> FinancialYears { get; } = new();

    /// <summary>The twelve months of the selected financial year.</summary>
    public ObservableCollection<Gstr6MonthOption> Months { get; } = new();

    /// <summary>The company's ISD registrations. Empty ⇒ this screen has nothing to show.</summary>
    public ObservableCollection<Gstr6IsdOption> IsdRegistrations { get; } = new();

    /// <summary>The distribution rows — one per (recipient, eligibility) that received anything.</summary>
    public ObservableCollection<Gstr6RowVm> Distribution { get; } = new();

    /// <summary>Every caveat and refusal the engine raised, verbatim — never summarised away.</summary>
    public ObservableCollection<string> Diagnostics { get; } = new();

    public Gstr6ReportViewModel(Company company)
    {
        _company = company ?? throw new ArgumentNullException(nameof(company));

        foreach (var r in company.Gst?.IsdRegistrations ?? Array.Empty<GstRegistration>())
            IsdRegistrations.Add(new Gstr6IsdOption { Id = r.Id, Name = r.Name, Gstin = r.Gstin });
        _selectedIsd = IsdRegistrations.FirstOrDefault();

        var fyStart = company.FinancialYearStart.Year;
        for (var y = fyStart; y >= fyStart - 2; y--)
            FinancialYears.Add(new Gstr6FyOption { StartYear = y });
        _selectedYear = FinancialYears.FirstOrDefault();

        RebuildMonths();
        _selectedMonth = Months.FirstOrDefault();

        Rebuild();
    }

    /// <summary>The ISD registration the return is filed for; changing it rebuilds.</summary>
    public Gstr6IsdOption? SelectedIsd
    {
        get => _selectedIsd;
        set { if (SetProperty(ref _selectedIsd, value)) Rebuild(); }
    }

    /// <summary>The selected financial year; changing it re-derives the months and rebuilds.</summary>
    public Gstr6FyOption? SelectedYear
    {
        get => _selectedYear;
        set
        {
            if (!SetProperty(ref _selectedYear, value)) return;
            RebuildMonths();
            _selectedMonth = Months.FirstOrDefault();
            OnPropertyChanged(nameof(SelectedMonth));
            Rebuild();
        }
    }

    /// <summary>The selected return month; changing it rebuilds.</summary>
    public Gstr6MonthOption? SelectedMonth
    {
        get => _selectedMonth;
        set { if (SetProperty(ref _selectedMonth, value)) Rebuild(); }
    }

    /// <summary>The currently-built return. Never null after construction.</summary>
    public Gstr6 Return { get; private set; } = default!;

    private void RebuildMonths()
    {
        var startYear = SelectedYear?.StartYear ?? _company.FinancialYearStart.Year;
        var first = new DateOnly(startYear, _company.FinancialYearStart.Month, 1);
        Months.Clear();
        for (var i = 0; i < 12; i++)
            Months.Add(new Gstr6MonthOption { FirstDay = first.AddMonths(i) });
    }

    /// <summary>(Re)builds GSTR-6 for the selected ISD registration and month.</summary>
    public void Rebuild()
    {
        Message = null;
        Distribution.Clear();
        Diagnostics.Clear();

        var month = SelectedMonth ?? Months.FirstOrDefault();
        var from = month?.FirstDay ?? _company.FinancialYearStart;
        var to = month?.LastDay ?? from.AddMonths(1).AddDays(-1);

        Subtitle = $"{_company.Name}  —  {ApexDate.Format(from)} to {ApexDate.Format(to)}";

        if (SelectedIsd is not { } isd)
        {
            DistributorText = "No Input Service Distributor registration.";
            DueDateText = ApexDate.Format(Gstr6.DueDateFor(to));
            RelevantPeriodText = "—";
            SetZeroes();
            StatusText =
                "GSTR-6 is filed by an Input Service Distributor. Create a registration of type "
                + "'Input Service Distributor' (Masters → Create → GST Registration) to file one.";
            return;
        }

        Gstr6 ret;
        try
        {
            ret = Gstr6.Build(_company, from, to, isd.Id);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            Message = ex.Message;
            DistributorText = isd.Label;
            DueDateText = ApexDate.Format(Gstr6.DueDateFor(to));
            RelevantPeriodText = "—";
            SetZeroes();
            StatusText = "The return could not be built.";
            return;
        }

        Return = ret;

        DistributorText = string.IsNullOrWhiteSpace(ret.IsdGstin)
            ? ret.IsdName
            : $"{ret.IsdName} — {ret.IsdGstin} (State {ret.IsdStateCode})";
        DueDateText = ApexDate.Format(ret.DueDate);
        RelevantPeriodText =
            $"{ApexDate.Format(ret.RelevantPeriodFrom)} to {ApexDate.Format(ret.RelevantPeriodTo)}  —  {ret.RelevantPeriodBasis}";

        ReceivedCgstText = A(ret.ReceivedCgst);
        ReceivedSgstText = A(ret.ReceivedSgst);
        ReceivedIgstText = A(ret.ReceivedIgst);
        ReceivedCessText = A(ret.ReceivedCess);
        TotalReceivedText = A(ret.TotalReceived);
        EligibleCreditText = A(ret.EligibleCredit);
        IneligibleCreditText = A(ret.IneligibleCredit);
        TotalDistributedText = A(ret.TotalDistributed);
        UndistributedText = A(ret.UndistributedCredit);

        foreach (var row in ret.Distribution)
        {
            Distribution.Add(new Gstr6RowVm
            {
                Recipient = row.Name,
                StateCode = row.StateCode,
                Gstin = row.Gstin ?? string.Empty,
                Eligibility = row.IsEligible ? "Eligible" : "Ineligible",
                Cgst = A(row.Cgst),
                Sgst = A(row.Sgst),
                Igst = A(row.Igst),
                Cess = A(row.Cess),
                Total = A(row.Total),
            });
        }

        foreach (var d in ret.Diagnostics) Diagnostics.Add(d);

        StatusText = ret.UndistributedCredit.Amount == 0m
            ? $"₹{TotalReceivedText} received, ₹{TotalDistributedText} distributed across {ret.Distribution.Count} row(s); nothing undistributed. Due {DueDateText}."
            : $"₹{UndistributedText} of the ₹{TotalReceivedText} received was NOT distributed — see the notes below. Due {DueDateText}.";
    }

    // ==============================================================================================================
    //  The offline JSON export — Ctrl+A on this screen (census row 6.24)
    //
    //  🔴 THIS EXISTS SO THE EMITTER HAS A USER. GSTR-6 is a return that is FILED, and before this the ISD work
    //  produced a screen and nothing a filer could submit. The export lives HERE rather than on the shared
    //  "Offline Return Files (JSON)" page for a concrete reason: every other form on that page is scoped by the
    //  company's own GSTIN and a period, whereas GSTR-6 is filed BY A PARTICULAR ISD REGISTRATION — this screen is
    //  the only place that already knows which one. Routing it through the shared page would have meant inventing a
    //  registration picker there and risking a return filed under the wrong GSTIN.
    // ==============================================================================================================

    /// <summary>The folder the export writes into; empty ⇒ the working directory.</summary>
    [ObservableProperty] private string? _exportFolder;

    /// <summary>The outcome of the last export attempt, shown on the page.</summary>
    [ObservableProperty] private string _exportStatus = string.Empty;

    /// <summary>The government financial-period string <c>MMYYYY</c> for the selected month.</summary>
    public string FinancialPeriodCode =>
        SelectedMonth is { } m
            ? m.LastDay.Month.ToString("D2", CultureInfo.InvariantCulture)
              + m.LastDay.Year.ToString("D4", CultureInfo.InvariantCulture)
            : string.Empty;

    /// <summary>
    /// The file name the export writes, e.g. <c>GSTR-6_29AAACC1206D1Z5_052025.json</c>. Named for the <b>ISD's</b>
    /// GSTIN, not the company's — the return is filed by that registration.
    /// </summary>
    public string ExportFileName =>
        SelectedIsd is null || SelectedMonth is null
            ? string.Empty
            : $"GSTR-6_{(string.IsNullOrWhiteSpace(SelectedIsd.Gstin) ? "NOGSTIN" : SelectedIsd.Gstin)}" +
              $"_{FinancialPeriodCode}.json";

    /// <summary>Builds the offline JSON bytes for the selected ISD + month. Pure — writes nothing.</summary>
    public byte[] BuildJson()
        => SelectedIsd is null || SelectedMonth is null
            ? []
            : GstReturnJson.Gstr6(_company, SelectedMonth.FirstDay, SelectedMonth.LastDay, SelectedIsd.Id);

    /// <summary>
    /// Ctrl+A: writes the selected ISD's GSTR-6 offline JSON to <see cref="ExportFolder"/> under
    /// <see cref="ExportFileName"/>. The write goes through the injectable <paramref name="writeBytes"/> seam
    /// (null ⇒ the real filesystem) so tests never touch disk, exactly as the offline-returns page does.
    /// </summary>
    public bool ExportJson(Action<string, byte[]>? writeBytes = null)
    {
        if (SelectedIsd is null || SelectedMonth is null)
        {
            ExportStatus = "Choose an Input Service Distributor registration and a month first.";
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

    private void SetZeroes()
    {
        ReceivedCgstText = ReceivedSgstText = ReceivedIgstText = ReceivedCessText = "0.00";
        TotalReceivedText = EligibleCreditText = IneligibleCreditText = "0.00";
        TotalDistributedText = UndistributedText = "0.00";
    }

    private static string A(Money m) => IndianFormat.AmountAlways(m);
}
