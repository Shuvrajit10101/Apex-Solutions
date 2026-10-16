using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Reports;
using Apex.Ledger.Services;
using Apex.Desktop.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using DomainLedger = Apex.Ledger.Domain.Ledger;

namespace Apex.Desktop.ViewModels;

/// <summary>One ISD credit receipt already recorded on this book, for the screen's history list.</summary>
public sealed class IsdCreditRowVm : ViewModelBase
{
    public Guid VoucherId { get; init; }
    public string Document { get; init; } = string.Empty;
    public string Date { get; init; } = string.Empty;
    public string Counterpart { get; init; } = string.Empty;
    public string Cgst { get; init; } = string.Empty;
    public string Sgst { get; init; } = string.Empty;
    public string Igst { get; init; } = string.Empty;
    public string Cess { get; init; } = string.Empty;
    public string Total { get; init; } = string.Empty;
}

/// <summary>
/// The <b>Record ISD Credit</b> action screen — Reports → Statutory Reports → <b>GST Actions</b> → <b>Record ISD
/// Credit</b>. The interactive front end for <see cref="GstIsdCreditService.Post"/>: a branch recording Input Tax
/// Credit distributed to it by its head office against an ISD invoice or ISD debit note, which then files in
/// GSTR-3B <b>Table 4(A)(4) "Inward supplies from ISD"</b>.
///
/// <para><b>Grounding (Ruling 14 tier 1 — the vendor's own page), verified by content at
/// <c>help.tallysolutions.com/tally-prime/india-gst/isd-credit/</c>.</b> The capability:
/// <i>"You can account for Input Service Distributor Credit (ISD) Credit in TallyPrime when your branch receives
/// Input Tax Credit distributed by the head office"</i>. The fields this screen collects are the fields that page
/// collects — the ISD invoice / debit-note number and date, the counterpart ledger (<i>"the purchase, expense, or
/// branch account ledger, as applicable"</i>) and the per-head credit — under the Stat Adjustment the page names:
/// <i>Nature of Adjustment</i> = <i>"Increase in Input Tax Credit"</i>, <i>Additional Nature of Adjustment</i> =
/// <i>"ISD Invoice/Debit Note"</i>. The destination is the page's own sentence: <i>"The voucher will be included
/// in GSTR-3B and appear under 4A: Input Tax Credit Available (either in part or in full) &gt; Inward supplies
/// from ISD."</i></para>
///
/// <para>🔴 <b>WHAT THIS SCREEN DOES NOT OFFER, AND WHY THAT IS FIDELITY RATHER THAN A GAP.</b> It does not
/// distribute credit and it does not file a GSTR-6, because <b>the vendor does neither</b>. Measured across three
/// of the vendor's own pages: the ISD page above describes only credit <b>received</b> and never mentions GSTR-6;
/// the GST-returns FAQ (<c>help.tallysolutions.com/india-gst-faq-tally/</c>) enumerates GSTR-1, GSTR-2/2A/2B,
/// GSTR-3B, GSTR-9/9C and CMP-08 with <b>no GSTR-6</b>; and the release-wise feature list
/// (<c>help.tallysolutions.com/tallyprime-features-release-wise/</c>) contains no occurrence of "GSTR-6", "ISD"
/// or "Input Service Distributor" at all. A distribution screen here would be invention, not cloning.</para>
///
/// <para><b>Two things are ours and are labelled on the face of the screen</b> (see
/// <see cref="DivergenceText"/>): the refusal of a duplicate ISD document number, and the fact that the document
/// number is carried in the voucher narration rather than a typed field — so a receipt cannot be listed or
/// amended by document number, and editing the narration defeats the duplicate guard. A typed table is DDL and
/// this track took no schema version.</para>
///
/// <para><b>Opening this screen posts nothing</b> — only the explicit Record action (Ctrl+A) mutates. Gated to a
/// Regular GST company (ER-13). MVVM boundary: engine only, no Avalonia types, headlessly testable.</para>
/// </summary>
public sealed partial class RecordIsdCreditViewModel : ViewModelBase, IMasterListExportSource
{
    /// <summary>The vendor's own Nature-of-Adjustment wording, shown so the operator can match the page.</summary>
    public const string NatureOfAdjustment = "Increase in Input Tax Credit";

    /// <summary>The vendor's own Additional-Nature-of-Adjustment wording.</summary>
    public const string AdditionalNature = "ISD Invoice/Debit Note";

    /// <summary>The vendor's bound on the mechanism, quoted — ISD is input <b>services</b> only.</summary>
    public const string ServicesOnlyRule =
        "Credit related to goods or capital goods cannot be distributed through ISD.";

    private readonly Company _company;
    private readonly CompanyStorage _storage;
    private readonly Action _onChanged;
    private readonly GstIsdCreditService _service;

    [ObservableProperty] private string _title = "Record ISD Credit";
    [ObservableProperty] private string _subtitle = string.Empty;
    [ObservableProperty] private string _statusText = string.Empty;
    [ObservableProperty] private string? _message;
    [ObservableProperty] private bool _lastActionSucceeded;

    // ---------------------------------------------------------------- the form (the vendor's own field order)

    [ObservableProperty] private string _documentNumberText = string.Empty;
    [ObservableProperty] private string _documentDateText = string.Empty;
    [ObservableProperty] private string _voucherDateText = string.Empty;

    [ObservableProperty] private string _cgstText = string.Empty;
    [ObservableProperty] private string _sgstText = string.Empty;
    [ObservableProperty] private string _igstText = string.Empty;
    [ObservableProperty] private string _cessText = string.Empty;

    [ObservableProperty] private DomainLedger? _selectedCounterpart;

    [ObservableProperty] private int _highlightedIndex = -1;

    private GstRegistration? _selectedRegistration;

    /// <summary>The registrations this receipt may be recorded under. A single-registration book has exactly one
    /// entry and <see cref="ShowsRegistrationPicker"/> hides the picker, so that book sees no new affordance
    /// (ER-13).</summary>
    public ObservableCollection<GstRegistration> Registrations { get; } = new();

    /// <summary>True only on a book that holds more than one registration.</summary>
    public bool ShowsRegistrationPicker => Registrations.Count > 1;

    /// <summary>The registration the credit is received into — ITC lands in exactly one GSTIN's electronic credit
    /// ledger, so this is load-bearing on a multi-registration book.</summary>
    public GstRegistration? SelectedRegistration
    {
        get => _selectedRegistration;
        set { if (SetProperty(ref _selectedRegistration, value)) Rebuild(); }
    }

    /// <summary>The purchase / expense / branch-account ledgers the credit may be taken against — the vendor's
    /// <i>"the purchase, expense, or branch account ledger, as applicable"</i>.</summary>
    public ObservableCollection<DomainLedger> CounterpartOptions { get; } = new();

    /// <summary>Every ISD credit receipt already recorded under the selected registration (newest first).</summary>
    public ObservableCollection<IsdCreditRowVm> Recorded { get; } = new();

    public RecordIsdCreditViewModel(Company company, CompanyStorage storage, Action? onChanged = null)
    {
        _company = company ?? throw new ArgumentNullException(nameof(company));
        _storage = storage ?? throw new ArgumentNullException(nameof(storage));
        _onChanged = onChanged ?? (() => { });
        _service = new GstIsdCreditService(company);

        foreach (var registration in company.Gst?.AllRegistrations ?? [])
            Registrations.Add(registration);
        _selectedRegistration = Registrations.FirstOrDefault();

        var fy = company.FinancialYearStart;
        _documentDateText = ApexDate.Format(fy);
        _voucherDateText = ApexDate.Format(fy);

        LoadCounterpartOptions();
        Rebuild();
    }

    // ---------------------------------------------------------------- derived state the view binds

    /// <summary>The scope a post will be recorded under — the primary when no picker is shown.</summary>
    private Guid ScopedRegistrationId => _selectedRegistration?.Id ?? GstRegistration.PrimaryId;

    /// <summary>The ISD mechanism's own bound plus the two divergences this screen owns, stated on its face.</summary>
    public string DivergenceText =>
        ServicesOnlyRule + "  " +
        "Apex Solutions refuses a duplicate ISD document number under one registration, and carries that number " +
        "in the voucher narration rather than a typed field — so a receipt cannot be looked up or amended by " +
        "document number, and editing the narration defeats the duplicate check. Both are ours, not the vendor's.";

    /// <summary>The Stat Adjustment this screen posts under, in the vendor's own words.</summary>
    public string AdjustmentText => $"{NatureOfAdjustment}  >  {AdditionalNature}";

    // ---------------------------------------------------------------- the action

    /// <summary>
    /// Records the ISD credit receipt (Ctrl+A). Returns true when a voucher was posted. Every refusal is reported
    /// through <see cref="Message"/> with <see cref="LastActionSucceeded"/> false — the engine's guards are
    /// surfaced, never crashed into.
    /// </summary>
    public bool Record()
    {
        if (string.IsNullOrWhiteSpace(DocumentNumberText))
            return Fail("Enter the ISD invoice / debit-note number — it is the document the credit is received against.");

        if (!ApexDate.TryParse(DocumentDateText, out var documentDate))
            return Fail($"'{DocumentDateText}' is not a valid ISD invoice / debit-note date.");

        if (!ApexDate.TryParse(VoucherDateText, out var voucherDate))
            return Fail($"'{VoucherDateText}' is not a valid voucher date.");

        string? error = null;
        if (!Paisa(CgstText, "CGST", out var cgst, ref error)) return Fail(error!);
        if (!Paisa(SgstText, "SGST/UTGST", out var sgst, ref error)) return Fail(error!);
        if (!Paisa(IgstText, "IGST", out var igst, ref error)) return Fail(error!);
        if (!Paisa(CessText, "Cess", out var cess, ref error)) return Fail(error!);

        if (cgst + sgst + igst + cess <= 0)
            return Fail("An ISD credit receipt must carry credit in at least one head — enter an amount.");

        var counterpart = SelectedCounterpart;
        if (counterpart is null)
            return Fail(CounterpartOptions.Count == 0
                ? "No purchase, expense or branch-account ledger exists to take the credit against — create one first."
                : "Select the purchase, expense or branch-account ledger to credit.");

        Voucher posted;
        try
        {
            posted = _service.Post(
                DocumentNumberText,
                documentDate,
                new GstIsdCreditService.IsdCreditAmount(cgst, sgst, igst, cess),
                counterpart.Id,
                voucherDate,
                ScopedRegistrationId);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            return Fail(ex.Message);
        }

        _storage.Save(_company);

        DocumentNumberText = string.Empty;
        CgstText = SgstText = IgstText = CessText = string.Empty;

        Rebuild();
        LastActionSucceeded = true;
        Message =
            $"Recorded ISD credit of {Rupees(posted)} against {counterpart.Name}. " +
            "It files in GSTR-3B Table 4(A)(4) — Inward supplies from ISD.";
        _onChanged();
        return true;
    }

    private bool Fail(string message)
    {
        Message = message;
        LastActionSucceeded = false;
        return false;
    }

    // ---------------------------------------------------------------- projection

    private void Rebuild()
    {
        Recorded.Clear();

        var scope = ScopedRegistrationId;
        // 🔴 Accumulated in RUPEES, not paisa. An earlier revision summed `(long)Math.Round(total * 100m, …)`
        // here and drift-lock D3 (OneRuleDriftLockTests.RupeesToPaisaHasOneHome) correctly failed it: that is a
        // second copy of the rupees→paisa rule whose one home is PaisaConversion. This total is a DISPLAY sum of
        // figures that are already exact to the paisa — each row's heads came straight off posted line amounts —
        // so there is nothing to convert and no rounding decision to re-make.
        decimal totalCredit = 0m;

        foreach (var v in _company.Vouchers
                     .Where(v => !v.Cancelled)
                     .Where(v => v.Narration is { } n
                                 && n.StartsWith(GstIsdCreditService.NarrationPrefix, StringComparison.Ordinal))
                     .Where(v => GstReportSupport.RegistrationOf(v) == scope)
                     .OrderByDescending(v => v.Date))
        {
            var type = _company.FindVoucherType(v.TypeId);
            if (type is null || !type.IsGstStatAdjustmentType) continue;

            decimal c = 0m, s = 0m, i = 0m, cess = 0m, counterpartAmount = 0m;
            string counterpartName = string.Empty;

            foreach (var line in v.Lines)
            {
                if (line.Gst is { Adjustment: GstAdjustmentKind.IsdCreditReceived } g)
                {
                    switch (g.TaxHead)
                    {
                        case GstTaxHead.Central: c += line.Amount.Amount; break;
                        case GstTaxHead.State: s += line.Amount.Amount; break;
                        case GstTaxHead.Integrated: i += line.Amount.Amount; break;
                        case GstTaxHead.Cess: cess += line.Amount.Amount; break;
                    }
                }
                else if (line.Side == DrCr.Credit)
                {
                    counterpartAmount += line.Amount.Amount;
                    counterpartName = _company.FindLedger(line.LedgerId)?.Name ?? string.Empty;
                }
            }

            var total = c + s + i + cess;
            totalCredit += total;

            Recorded.Add(new IsdCreditRowVm
            {
                VoucherId = v.Id,
                Document = DocumentOf(v.Narration!),
                Date = ApexDate.Format(v.Date),
                Counterpart = counterpartName.Length > 0 ? counterpartName : Money(counterpartAmount),
                Cgst = Money(c),
                Sgst = Money(s),
                Igst = Money(i),
                Cess = Money(cess),
                Total = Money(total),
            });
        }

        var suffix = ShowsRegistrationPicker && _selectedRegistration is { } r ? $"  —  {r.Name}" : string.Empty;
        Subtitle = $"{_company.Name}{suffix}  —  ITC received from an Input Service Distributor";
        StatusText = Recorded.Count == 0
            ? "No ISD credit recorded. Enter the ISD invoice / debit note and the credit distributed to this registration."
            : $"{Recorded.Count} receipt(s)  ·  total credit {Money(totalCredit)}  ·  files in GSTR-3B Table 4(A)(4).";
    }

    /// <summary>The document number back out of the narration <c>GstIsdCreditService</c> wrote.</summary>
    private static string DocumentOf(string narration)
    {
        var body = narration[GstIsdCreditService.NarrationPrefix.Length..];
        var at = body.LastIndexOf(" dated ", StringComparison.Ordinal);
        return at < 0 ? body : body[..at];
    }

    private void LoadCounterpartOptions()
    {
        CounterpartOptions.Clear();
        foreach (var l in _company.Ledgers
                     .Where(IsCounterpartCandidate)
                     .OrderBy(l => l.Name, StringComparer.OrdinalIgnoreCase))
            CounterpartOptions.Add(l);
        SelectedCounterpart = CounterpartOptions.FirstOrDefault();
    }

    /// <summary>
    /// The vendor's <i>"purchase, expense, or branch account ledger"</i>. A Duties &amp; Taxes ledger is excluded:
    /// crediting one here would move a tax pool rather than the expense the ISD distributed against, and the four
    /// <c>Input {head}</c> ledgers are the very pools this receipt debits — offering them would let an operator
    /// post a self-cancelling voucher that files a real Table 4(A)(4) figure.
    /// </summary>
    private bool IsCounterpartCandidate(DomainLedger ledger)
    {
        if (ClassificationRules.IsCashOrBankLedger(ledger, _company)) return false;
        var group = _company.FindGroup(ledger.GroupId);
        while (group is not null)
        {
            if (string.Equals(group.Name, "Duties & Taxes", StringComparison.OrdinalIgnoreCase)) return false;
            group = group.ParentId is { } pid ? _company.FindGroup(pid) : null;
        }
        return true;
    }

    private static string Money(decimal amount) => amount.ToString("N2", CultureInfo.InvariantCulture);

    private static string Rupees(Voucher voucher)
    {
        decimal total = 0m;
        foreach (var line in voucher.Lines)
            if (line.Gst is { Adjustment: GstAdjustmentKind.IsdCreditReceived })
                total += line.Amount.Amount;
        return Money(total);
    }

    private static bool Paisa(string text, string label, out long paisa, ref string? error)
    {
        paisa = 0;
        if (string.IsNullOrWhiteSpace(text)) return true;
        if (!decimal.TryParse(text.Trim(), NumberStyles.Number, CultureInfo.InvariantCulture, out var r))
        {
            error = $"{label}: '{text}' is not a valid rupee amount.";
            return false;
        }
        if (r < 0)
        {
            error = $"{label}: an ISD credit receipt adds credit — '{text}' is negative.";
            return false;
        }
        if (!PaisaConversion.TryToPaisaExact(r, out paisa))
        {
            error = $"{label}: '{text}' carries a fraction of a paisa.";
            return false;
        }
        return true;
    }

    /// <summary>Moves the keyboard highlight over the recorded list.</summary>
    public void MoveHighlight(int direction)
    {
        if (Recorded.Count == 0) { HighlightedIndex = -1; return; }
        var next = HighlightedIndex + direction;
        HighlightedIndex = Math.Clamp(next, 0, Recorded.Count - 1);
    }

    /// <summary>Print / export (Alt+P, Ctrl+E) — the screen is a real report page, not an output dead end.</summary>
    public MasterListSnapshot ToMasterListSnapshot()
    {
        var rows = new List<IReadOnlyList<string>>(Recorded.Count + 1);

        foreach (var r in Recorded)
            rows.Add(new[] { r.Document, r.Date, r.Counterpart, r.Cgst, r.Sgst, r.Igst, r.Cess, r.Total });

        if (Recorded.Count == 0)
            rows.Add(new[]
            {
                "No ISD credit has been recorded for this registration.",
                string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty,
            });

        return new MasterListSnapshot(
            Title,
            new[]
            {
                MasterListColumn.Text("ISD Invoice / Debit Note"),
                MasterListColumn.Text("Date"),
                MasterListColumn.Text("Credited To"),
                MasterListColumn.Number("CGST"),
                MasterListColumn.Number("SGST/UTGST"),
                MasterListColumn.Number("IGST"),
                MasterListColumn.Number("Cess"),
                MasterListColumn.Number("Total"),
            },
            rows);
    }
}
