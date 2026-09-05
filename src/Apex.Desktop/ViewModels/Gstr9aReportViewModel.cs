using System;
using System.Collections.ObjectModel;
using System.Linq;
using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Reports;
using Apex.Desktop.Services;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Apex.Desktop.ViewModels;

/// <summary>
/// The <b>Form GSTR-9A</b> report page (Reports → Statutory Reports → Composition Returns → GSTR-9A; census row
/// 6.13). A read-only projection over the pure <see cref="Gstr9a"/> engine — which was complete, deterministic and
/// tested but had <b>no route a user could reach it by</b>: before this page the only <c>Gstr9a</c> references in
/// the whole product were the engine record itself and an <b>uncalled</b> offline-JSON writer.
///
/// <para><b>🔴 THE APPLICABILITY STATEMENT IS THE FIDELITY CONTENT OF THIS PAGE, AND IT IS RENDERED, NOT COMMENTED.</b>
/// See <see cref="ApplicabilityText"/>. A composition dealer who opened a page headed "annual return" and filed what
/// it showed would file the wrong form: the operative annual return for a person paying tax under section 10 is
/// <b>GSTR-4</b>, which this product already ships one menu row above. This page therefore states, on its face,
/// that it is a computation for reconciliation and for prior years — never a "file this" surface.</para>
///
/// <para><b>Sources</b> (Ruling 14 tier 2 — the statutory text itself, from CBIC's own consolidation
/// <c>https://cbic-gst.gov.in/pdf/amended-01012022-CGST-Rules-2017-Part-A.pdf</c>, the CGST Rules as amended to
/// 01.01.2022, retrieved 2026-09-05):
/// <list type="bullet">
/// <item><b>Rule 80(1) proviso</b> (as substituted w.e.f. 01.08.2021 by Notification 30/2021-CT dt. 30.07.2021),
/// verbatim: <i>"Provided that a person paying tax under section 10 shall furnish the annual return in FORM
/// GSTR-9A."</i> The form is therefore <b>still prescribed</b> — it has not been deleted from the Rules.</item>
/// <item><b>Rule 62(1)(ii)</b> (as substituted by Notification 20/2019-CT dt. 23.04.2019), verbatim: a person paying
/// tax under section 10 shall <i>"furnish a return for every financial year … in FORM GSTR-4, till the thirtieth day
/// of April following the end of such financial year."</i></item>
/// </list>
/// 🔴 <b>The waiver notification for FY 2019-20 onward was NOT retrieved</b> (<c>cbic-gst.gov.in/pdf/notification-47-2019-central-tax-english.pdf</c>
/// 404s), so <see cref="ApplicabilityText"/> says <i>"waived by notification"</i> and <b>deliberately names none</b>.
/// Naming a notification nobody here read is the <c>SeedTdsTcsRates</c> mistake this project has already had to strip
/// out of shipped code. Do not "improve" that sentence by adding a number.</para>
///
/// <para>Gated: only reachable when the company is a Composition dealer (byte-identical for a Regular company,
/// ER-13). MVVM boundary: engine only, no Avalonia types (headlessly testable); deterministic (no clock/RNG).</para>
/// </summary>
public sealed partial class Gstr9aReportViewModel : ViewModelBase
{
    /// <summary>
    /// The applicability statement the page renders — the one thing on this screen a wrong reading of which would
    /// make an operator file the wrong form. Every clause below is quoted or paraphrased from the CGST Rules
    /// consolidation cited in the type remarks; <b>no notification number appears for the post-2018-19 waiver,
    /// because none was retrieved.</b>
    /// </summary>
    public const string ApplicabilityText =
        "Rule 80(1) proviso (CGST Rules, as amended to 01.01.2022) prescribes FORM GSTR-9A as the annual return for " +
        "a person paying tax under section 10. Rule 62(1)(ii), as substituted by Notification 20/2019-CT dt. " +
        "23.04.2019, requires a composition taxpayer to furnish GSTR-4 annually by 30 April. GSTR-9A filing has " +
        "been waived by notification for years after FY 2018-19. This computation is provided for reconciliation " +
        "and for prior years; it is not a filing artefact.";

    /// <summary>The instance face of <see cref="ApplicabilityText"/> — a compiled XAML binding cannot resolve a
    /// <c>const</c>, and the constant is what the tests assert on, so both exist and cannot drift apart.</summary>
    public string Applicability => ApplicabilityText;

    private readonly Company _company;

    [ObservableProperty] private string _title = "Form GSTR-9A — Composition Annual Return";
    [ObservableProperty] private string _subtitle = string.Empty;

    // The turnover block — additive figures, taken from the whole-FY composition compute unchanged (no re-round).
    [ObservableProperty] private string _totalTurnoverText = "0.00";
    [ObservableProperty] private string _taxableTurnoverText = "0.00";

    // The tax-paid block — the Σ of the four ALREADY-ROUNDED quarterly CMP-08 figures, so 9A reconciles to Σ CMP-08
    // by construction (the engine's own guarantee; never a whole-FY re-round that could diverge on odd paisa).
    [ObservableProperty] private string _taxPaidCgstText = "0.00";
    [ObservableProperty] private string _taxPaidSgstText = "0.00";
    [ObservableProperty] private string _compositionTaxPaidText = "0.00";
    [ObservableProperty] private string _rcmInwardTaxText = "0.00";
    [ObservableProperty] private string _lateFeeText = "0.00";
    [ObservableProperty] private string _annualTotalText = "0.00";

    /// <summary>The §47 late fee is <b>carried forward, not computed</b> (DP-18 "light projections"). Said in words
    /// so a zero in that box is never read as "nil late fee was due".</summary>
    [ObservableProperty] private string _lateFeeNoteText =
        "Late fee under section 47 is not computed by this book — the figure above is nil because nothing has been " +
        "entered, not because none is due.";

    [ObservableProperty] private string _statusText = string.Empty;
    [ObservableProperty] private string? _message;

    private CompositionFyOption? _selectedYear;

    /// <summary>The financial years the return can be built for (the company FY + the two prior).</summary>
    public ObservableCollection<CompositionFyOption> FinancialYears { get; } = new();

    public Gstr9aReportViewModel(Company company)
    {
        _company = company ?? throw new ArgumentNullException(nameof(company));

        var fyStart = company.FinancialYearStart.Year;
        for (var y = fyStart; y >= fyStart - 2; y--)
            FinancialYears.Add(new CompositionFyOption { StartYear = y });

        _selectedYear = FinancialYears.FirstOrDefault();
        Rebuild();
    }

    /// <summary>The selected financial year; changing it rebuilds the annual return.</summary>
    public CompositionFyOption? SelectedYear
    {
        get => _selectedYear;
        set { if (SetProperty(ref _selectedYear, value)) Rebuild(); }
    }

    /// <summary>The currently-built GSTR-9A (rebuilt on selection change). Never null after construction.</summary>
    public Gstr9a Return { get; private set; } = default!;

    /// <summary>(Re)builds GSTR-9A for the selected financial year.</summary>
    public void Rebuild()
    {
        var startYear = SelectedYear?.StartYear ?? _company.FinancialYearStart.Year;
        var fyFrom = new DateOnly(startYear, _company.FinancialYearStart.Month, 1);
        var fyTo = fyFrom.AddYears(1).AddDays(-1);
        Message = null;

        Gstr9a ret;
        try
        {
            ret = Gstr9a.Build(_company, fyFrom, fyTo);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            Return = new Gstr9a(fyFrom, fyTo, false, Money.Zero, Money.Zero, Money.Zero, Money.Zero, Money.Zero, Money.Zero);
            Message = ex.Message;
            SetNotApplicable();
            return;
        }

        Return = ret;
        Subtitle = $"{_company.Name}  —  FY {startYear}-{(startYear + 1) % 100:00}  ({ApexDate.Format(fyFrom)} to {ApexDate.Format(fyTo)})";

        if (!ret.Applicable)
        {
            SetNotApplicable();
            return;
        }

        TotalTurnoverText = A(ret.TotalTurnover);
        TaxableTurnoverText = A(ret.TaxableTurnover);
        TaxPaidCgstText = A(ret.TaxPaidCgst);
        TaxPaidSgstText = A(ret.TaxPaidSgst);
        CompositionTaxPaidText = A(ret.CompositionTaxPaid);
        RcmInwardTaxText = A(ret.RcmInwardTax);
        LateFeeText = A(ret.LateFee);
        AnnualTotalText = A(new Money(ret.CompositionTaxPaid.Amount + ret.RcmInwardTax.Amount));

        StatusText = $"Annual tax ₹{AnnualTotalText} (composition ₹{CompositionTaxPaidText} + inward RCM " +
                     $"₹{RcmInwardTaxText}); the composition figure is the Σ of the four quarterly CMP-08 " +
                     "statements and reconciles to them by construction.";
    }

    private void SetNotApplicable()
    {
        TotalTurnoverText = TaxableTurnoverText = "0.00";
        TaxPaidCgstText = TaxPaidSgstText = CompositionTaxPaidText = "0.00";
        RcmInwardTaxText = LateFeeText = AnnualTotalText = "0.00";
        StatusText = "GSTR-9A applies only to a Composition dealer.";
    }

    private static string A(Money m) => IndianFormat.AmountAlways(m);
}
