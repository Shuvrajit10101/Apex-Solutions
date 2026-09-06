using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Reports;
using Apex.Ledger.Services;
using Apex.Desktop.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DomainLedger = Apex.Ledger.Domain.Ledger;

namespace Apex.Desktop.ViewModels;

/// <summary>
/// One <b>Cause of Payment</b> a DRC-03 can be filed under.
///
/// <para><b>Source, and why this is a closed list.</b> The twelve entries below are the <b>complete</b> "Cause of
/// Payment" drop-down of the statutory portal, transcribed <b>verbatim and in the portal's own order</b> from GSTN's
/// own user manual —
/// <c>https://tutorial.gst.gov.in/userguide/demandsandrecovery/Manual_GST_FORM_DRC-03.htm</c>, section
/// <i>"Cause of Payment"</i> (retrieved 2026-09-05). They are <b>not</b> our coinage and must never be re-worded,
/// re-ordered or extended: the string picked here is what is written into <see cref="GstDrc03.Cause"/> and read back
/// on every later reconciliation. <c>"Others"</c> is the portal's own eleventh option, not an escape hatch we added.
/// </para>
/// </summary>
public sealed record Drc03CauseOption(int Ordinal, string Text)
{
    /// <summary>What the picker shows — the portal's own numbering, then its own wording.</summary>
    public string Label => $"{Ordinal}. {Text}";

    public override string ToString() => Label;

    /// <summary>The portal's twelve causes, verbatim, in the portal's order.</summary>
    public static readonly Drc03CauseOption[] All =
    {
        new(1, "Annual return"),
        new(2, "Audit"),
        new(3, "Investigation/Enforcement"),
        new(4, "Intimation of tax ascertained through FORM GST DRC-01A"),
        new(5, "Mismatch between FORM GSTR-2B and FORM GSTR-3B"),
        new(6, "Mismatch between FORM GSTR-1 and FORM GSTR-3B"),
        new(7, "Reconciliation statement"),
        new(8, "After issuance of SCN/Statement but before issuance of the order"),
        new(9, "Scrutiny"),
        new(10, "Before issuance of SCN/Statement (Voluntary)"),
        new(11, "Others"),
        new(12, "Order"),
    };
}

/// <summary>One already-filed DRC-03 row (its cause / period / heads) for the screen's history list.
///
/// <para><b>Deliberately carries no <c>IsHighlighted</c> flag.</b> The filed list is a <c>ListBox</c> whose
/// <c>SelectedIndex</c> is two-way bound to <see cref="Drc03PaymentViewModel.HighlightedIndex"/>, so the keyboard
/// highlight is painted by the ListBox's own selection visual. A per-row flag was carried here for a while and
/// <b>nothing in the view ever bound it</b>: a second, invisible copy of the selection that only the view model
/// could see. Removing it also removes its keeper — <c>Rebuild</c> used to re-raise the change handler by hand
/// purely to refresh those flags. (The manual-highlight pattern, a Border bound to a per-row flag, belongs to the
/// <c>ItemsControl</c> reports, which have no selection of their own.)</para></summary>
public sealed class Drc03RowVm : ViewModelBase
{
    public Guid RecordId { get; init; }
    public string Cause { get; init; } = string.Empty;
    public string Period { get; init; } = string.Empty;
    public string Tax { get; init; } = string.Empty;
    public string Interest { get; init; } = string.Empty;
    public string Total { get; init; } = string.Empty;
    public string DemandRef { get; init; } = string.Empty;
}

/// <summary>
/// The <b>DRC-03 (voluntary / self-ascertained payment)</b> action screen — Reports → Statutory Reports →
/// <b>GST Actions</b> → <b>DRC-03 Voluntary Payment</b>. The interactive front end for
/// <see cref="GstDepositService.PostDrc03"/>, which was complete, guarded and tested but had <b>no route a user
/// could reach it by</b> (census row 6.20; register item T2-9 — "a complete engine verb with zero production
/// callers"). Nothing here re-implements the engine: the screen collects the portal's own inputs, surfaces the
/// engine's two hard refusals <i>before</i> they are hit, and calls the one posting method.
///
/// <para><b>Grounding (Ruling 14 tier 2 — the statutory portal itself).</b> Every caption, the cause enumeration and
/// the cash-only rule come from GSTN's user manual for the form,
/// <c>https://tutorial.gst.gov.in/userguide/demandsandrecovery/Manual_GST_FORM_DRC-03.htm</c> (retrieved 2026-09-05).
/// The instrument itself is governed by <b>Rule 142(2) / 142(3)</b> of the CGST Rules.</para>
///
/// <para>🔴 <b>Three portal fields are DELIBERATELY ABSENT, and the screen says so on its face rather than dropping
/// them quietly.</b> The portal's liability grid has five columns — <i>Tax/Cess · Interest · Penalty · Fee ·
/// Others</i> — and its header carries a <i>Section Number</i> and a <i>Communication Reference Number</i>.
/// <see cref="GstDrc03"/> has fields for the first two grid columns only, and none for a section number or a
/// communication reference. Storing them is a <b>schema change</b>, which this track has no budget for, and writing
/// them into the free-text <see cref="GstDrc03.Cause"/> would corrupt the verbatim enumeration above — so they are
/// not offered, and <see cref="DivergenceText"/> states the gap to the operator. A form that accepted a penalty
/// figure and then discarded it would be worse than one that admits it cannot hold one.</para>
///
/// <para><b>The two engine guards, surfaced rather than crashed into.</b> (1) <b>Credit is tax-only</b> (§49(4) /
/// Rule 86(2)); the portal states it as <i>"Interest and penalty amount shall be paid out of cash ledger only."</i>
/// When the payment method is Credit the interest box is disabled and carries that sentence, instead of accepting a
/// number the engine will throw on. (2) <b>Cash minor-head isolation</b> — a cash draw is refused unless the exact
/// (major, minor) cell holds enough deposited-and-unutilised cash. The screen <b>projects</b> that availability
/// through the engine's own <see cref="GstDepositService.AvailableCash"/> and shows the shortfall up front.</para>
///
/// <para><b>Opening this screen posts nothing</b> — only the explicit Post action (Ctrl+A) mutates. Gated: Regular
/// GST company (ER-13). MVVM boundary: engine only, no Avalonia types (headlessly testable); deterministic.</para>
/// </summary>
public sealed partial class Drc03PaymentViewModel : ViewModelBase
{
    /// <summary>The portal's own sentence on which ledger may discharge interest and penalty — quoted verbatim.</summary>
    public const string CashOnlyRule = "Interest and penalty amount shall be paid out of cash ledger only.";

    /// <summary>What a cash cell shows when the balance could <b>not be read</b>. Deliberately not a number, and
    /// deliberately not "0.00": see <see cref="Cash"/>.</summary>
    public const string CashUnreadable = "not read";

    private readonly Company _company;

    /// <summary>The first cash-cell read failure of the current <see cref="Rebuild"/>, or null when all five read
    /// cleanly. Reset at the top of every rebuild so a fixed book clears the warning.</summary>
    private string? _cashReadError;
    private readonly CompanyStorage _storage;
    private readonly Action _onChanged;
    private readonly GstDepositService _deposit;
    private readonly Func<GstTaxHead, GstMinorHead, Money> _availableCash;

    [ObservableProperty] private string _title = "DRC-03 — Voluntary / Self-Ascertained Payment";
    [ObservableProperty] private string _subtitle = string.Empty;
    [ObservableProperty] private string _statusText = string.Empty;
    [ObservableProperty] private string? _message;
    [ObservableProperty] private bool _lastActionSucceeded;

    // ---------------------------------------------------------------- the form (the portal's own field order)

    [ObservableProperty] private Drc03CauseOption _selectedCause = Drc03CauseOption.All[9]; // 10. …(Voluntary)

    /// <summary>The portal's "Please specify" free text, live only under cause 11 ("Others").</summary>
    [ObservableProperty] private string _othersSpecifyText = string.Empty;

    [ObservableProperty] private string _period = string.Empty;
    [ObservableProperty] private string _paymentDateText = string.Empty;
    [ObservableProperty] private string _reasonsText = string.Empty;
    [ObservableProperty] private string _demandRefText = string.Empty;

    [ObservableProperty] private string _cgstText = string.Empty;
    [ObservableProperty] private string _sgstText = string.Empty;
    [ObservableProperty] private string _igstText = string.Empty;
    [ObservableProperty] private string _cessText = string.Empty;
    [ObservableProperty] private string _interestText = string.Empty;

    [ObservableProperty] private GstDepositService.PaymentMethod _method = GstDepositService.PaymentMethod.Cash;
    [ObservableProperty] private DomainLedger? _selectedBank;

    [ObservableProperty] private int _highlightedIndex = -1;

    // ---------------------------------------------------------------- the read-outs

    /// <summary>The unutilised cash in each (major, Tax) cell plus the (IGST, Interest) cell the engine draws
    /// interest from — projected, never guessed, so the operator sees the refusal before it happens.</summary>
    [ObservableProperty] private string _availableCgstText = "0.00";
    [ObservableProperty] private string _availableSgstText = "0.00";
    [ObservableProperty] private string _availableIgstText = "0.00";
    [ObservableProperty] private string _availableCessText = "0.00";
    [ObservableProperty] private string _availableInterestText = "0.00";

    /// <summary>True when any of the five cash cells above could not be read. The view shows
    /// <see cref="CashReadErrorText"/> on this, so a screen that is about to take a payment never presents an
    /// unmeasured balance as a measured one.</summary>
    [ObservableProperty] private bool _cashReadFailed;

    /// <summary>What failed, in the operator's own words, and what a blank cell does and does not mean.</summary>
    [ObservableProperty] private string _cashReadErrorText = string.Empty;

    /// <summary>The portal's <b>twelve</b> causes the picker offers, verbatim and in the portal's own order.</summary>
    public ObservableCollection<Drc03CauseOption> Causes { get; } = new(Drc03CauseOption.All);

    /// <summary>The payment methods the engine funds a DRC-03 from.</summary>
    public ObservableCollection<GstDepositService.PaymentMethod> Methods { get; } =
        new((GstDepositService.PaymentMethod[])Enum.GetValues(typeof(GstDepositService.PaymentMethod)));

    /// <summary>The Bank / Cash ledgers a bank-funded DRC-03 can be paid from.</summary>
    public ObservableCollection<DomainLedger> BankOptions { get; } = new();

    /// <summary>Every DRC-03 already filed on this book (newest period first).</summary>
    public ObservableCollection<Drc03RowVm> Filed { get; } = new();

    /// <param name="availableCash">The per-cell cash projection. Null ⇒ the engine's own
    /// <see cref="GstDepositService.AvailableCash"/>, which is what production always uses. It is injectable for
    /// one reason: <see cref="Cash"/> has to decide what to show when the read FAILS, and that decision is on a
    /// screen that is about to take a payment — so it must be testable. The engine's own projection is a pure loop
    /// over challans and vouchers and cannot be made to throw from a Company, which is exactly how a wrong answer
    /// in that branch would otherwise ship unexercised. Same shape as
    /// <c>GstOfflineReturnsViewModel.ExportJson(writeBytes)</c>, and for the same reason.</param>
    public Drc03PaymentViewModel(Company company, CompanyStorage storage, Action? onChanged = null,
                                 Func<GstTaxHead, GstMinorHead, Money>? availableCash = null)
    {
        _company = company ?? throw new ArgumentNullException(nameof(company));
        _storage = storage ?? throw new ArgumentNullException(nameof(storage));
        _onChanged = onChanged ?? (() => { });
        _deposit = new GstDepositService(company);
        _availableCash = availableCash ?? ((major, minor) => _deposit.AvailableCash(major, minor));

        var fy = company.FinancialYearStart;
        _period = $"{fy.Year}-{(fy.Year + 1) % 100:00}";
        _paymentDateText = ApexDate.Format(fy);

        LoadBankOptions();
        Rebuild();
    }

    // ---------------------------------------------------------------- derived state the view binds

    /// <summary>True under the portal's own cause 11 ("Others"), which is the only one that asks for free text.</summary>
    public bool NeedsOthersSpecify => SelectedCause.Ordinal == 11;

    /// <summary>True while the payment is bank-funded (the only method that needs a bank ledger).</summary>
    public bool NeedsBank => Method == GstDepositService.PaymentMethod.Bank;

    /// <summary>True while the cash cell read-out is meaningful (only a cash-funded payment draws on it).</summary>
    public bool ShowsCashAvailability => Method == GstDepositService.PaymentMethod.Cash;

    /// <summary>
    /// False when the electronic <b>credit</b> ledger is funding the payment — §49(4) / Rule 86(2) let credit settle
    /// the Tax minor head only. The view disables the interest box on this, and <see cref="InterestNoteText"/> says
    /// why in the portal's own words.
    /// </summary>
    public bool CanEnterInterest => Method != GstDepositService.PaymentMethod.Credit;

    /// <summary>The note under the interest box: the portal's cash-only sentence when credit is funding the payment,
    /// otherwise the §50 note that interest is a typed figure and is never computed for you (DP-34).</summary>
    public string InterestNoteText => CanEnterInterest
        ? "§50 interest is a figure you enter — it is never computed for you."
        : CashOnlyRule;

    /// <summary>
    /// The declared divergence, stated on the screen rather than buried in a comment: three fields the statutory
    /// portal has and this book cannot hold. See the type remarks.
    /// </summary>
    public string DivergenceText =>
        "Not offered on this screen: the portal's Penalty, Fee and Others liability columns, its Section Number and " +
        "its Communication Reference Number. This book records a DRC-03's tax heads and §50 interest only, so a " +
        "figure typed into any of those would be discarded. File those portions on the portal.";

    partial void OnSelectedCauseChanged(Drc03CauseOption value)
    {
        OnPropertyChanged(nameof(NeedsOthersSpecify));
        if (!NeedsOthersSpecify) OthersSpecifyText = string.Empty;
    }

    partial void OnMethodChanged(GstDepositService.PaymentMethod value)
    {
        OnPropertyChanged(nameof(NeedsBank));
        OnPropertyChanged(nameof(ShowsCashAvailability));
        OnPropertyChanged(nameof(CanEnterInterest));
        OnPropertyChanged(nameof(InterestNoteText));
        // Credit can never settle interest (§49(4)); clearing the box is honest — leaving a number in a disabled
        // field that will not be posted is exactly the shape that makes an operator think it was filed.
        if (!CanEnterInterest) InterestText = string.Empty;
    }

    /// <summary>Moves the filed-DRC-03 highlight (Up/Down within the page); wraps.</summary>
    public void MoveHighlight(int direction)
    {
        if (Filed.Count == 0) { HighlightedIndex = -1; return; }
        var i = HighlightedIndex < 0 ? (direction > 0 ? -1 : 0) : HighlightedIndex;
        HighlightedIndex = (i + direction + Filed.Count) % Filed.Count;
    }

    // ---------------------------------------------------------------- projection (posts nothing)

    /// <summary>(Re)projects the per-cell cash availability and the filed-DRC-03 history. <b>Posts nothing.</b></summary>
    public void Rebuild()
    {
        var keep = HighlightedIndex;
        Filed.Clear();

        _cashReadError = null;
        AvailableCgstText = Cash(GstTaxHead.Central, GstMinorHead.Tax);
        AvailableSgstText = Cash(GstTaxHead.State, GstMinorHead.Tax);
        AvailableIgstText = Cash(GstTaxHead.Integrated, GstMinorHead.Tax);
        AvailableCessText = Cash(GstTaxHead.Cess, GstMinorHead.Tax);
        // The engine draws §50 interest from the (IGST, Interest) cell — mirror exactly that cell, not a guess.
        AvailableInterestText = Cash(GstTaxHead.Integrated, GstMinorHead.Interest);
        CashReadFailed = _cashReadError is not null;
        CashReadErrorText = _cashReadError is null
            ? string.Empty
            : "The electronic cash ledger could not be read, so the balances above are shown as \"" +
              CashUnreadable + "\" rather than as figures: " + _cashReadError +
              "  Do not read a blank cell as a nil balance — this screen does not know what is in the cell, and " +
              "the engine will still refuse a cash draw it cannot fund.";

        foreach (var d in _company.GstDrc03s.OrderByDescending(d => d.Period, StringComparer.Ordinal))
            Filed.Add(new Drc03RowVm
            {
                RecordId = d.Id,
                Cause = d.Cause,
                Period = d.Period,
                Tax = R(d.TotalTaxPaisa),
                Interest = R(d.InterestPaisa),
                Total = R(d.TotalTaxPaisa + d.InterestPaisa),
                DemandRef = d.Drc03aDemandRef ?? string.Empty,
            });

        // The index alone is the whole of the highlight — the ListBox's SelectedIndex is bound to it. There is no
        // per-row flag left to re-raise by hand (see Drc03RowVm), so the hand-call that used to sit here is gone.
        HighlightedIndex = Filed.Count == 0 ? -1 : Math.Clamp(keep < 0 ? 0 : keep, 0, Filed.Count - 1);

        Subtitle = $"{_company.Name}  —  Rule 142(2) / 142(3) voluntary payment";
        StatusText = $"{Filed.Count} DRC-03 already filed on this book. " +
                     "Opening this screen posts nothing — Ctrl+A files the form.";
    }

    /// <summary>
    /// 🔴 <b>A FAILED READ IS NOT A ZERO BALANCE, AND THIS SCREEN IS ABOUT TO TAKE A PAYMENT.</b> This used to
    /// swallow both exception types into <c>"0.00"</c>, which put a figure the app had not measured in front of an
    /// operator as though it had: a cell that really holds ₹4,00,000 would read ₹0.00, the operator would conclude
    /// the cash ledger was empty and fund the DRC-03 from the bank — paying twice — or, the other way about, read a
    /// genuine zero as trustworthy and be refused by the engine with no idea why. The refusal itself was never in
    /// doubt (<see cref="GstDepositService.PostDrc03"/> re-checks the cell and throws), so nothing here was ever
    /// load-bearing for correctness of the POSTING; what was wrong was the FIGURE.
    ///
    /// <para>The screen still must not tear on a read-out, so the throw is still caught — but the cell now renders
    /// <see cref="CashUnreadable"/> and <see cref="CashReadErrorText"/> says on the face of the page what failed
    /// and that a blank is not a nil. The exception message is kept rather than flattened, because "which cell and
    /// why" is the whole content of the warning.</para>
    /// </summary>
    private string Cash(GstTaxHead major, GstMinorHead minor)
    {
        try { return IndianFormat.AmountAlways(_availableCash(major, minor)); }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            _cashReadError ??= $"{major}/{minor}: {ex.Message}";
            return CashUnreadable;
        }
    }

    // ---------------------------------------------------------------- the one mutator

    [RelayCommand] private void PostAction() => Post();

    /// <summary>
    /// Files the form's DRC-03 through <see cref="GstDepositService.PostDrc03"/>: the per-head tax + §50 interest,
    /// funded from cash / bank / credit. Every engine refusal (credit-settles-interest, an unfunded cash cell, a
    /// zero payment) is returned as a message on the screen, never thrown at the operator.
    /// </summary>
    public bool Post()
    {
        Message = null;
        LastActionSucceeded = false;

        var cause = ComposeCause(out var causeError);
        if (cause is null) return Fail(causeError!);

        if (string.IsNullOrWhiteSpace(Period))
            return Fail("Enter the period this payment relates to (a financial year, e.g. 2024-25, or yyyy-MM).");
        if (!ApexDate.TryParse(PaymentDateText, out var date))
            return Fail($"'{PaymentDateText}' is not a valid payment date.");

        string? error = null;
        if (!Paisa(CgstText, "CGST", out var cgst, ref error)) return Fail(error!);
        if (!Paisa(SgstText, "SGST/UTGST", out var sgst, ref error)) return Fail(error!);
        if (!Paisa(IgstText, "IGST", out var igst, ref error)) return Fail(error!);
        if (!Paisa(CessText, "Cess", out var cess, ref error)) return Fail(error!);
        if (!Paisa(InterestText, "Interest", out var interest, ref error)) return Fail(error!);

        if (cgst + sgst + igst + cess + interest <= 0)
            return Fail("A DRC-03 must discharge a positive amount — enter at least one head.");

        // The credit tax-only rule, stated BEFORE the engine throws it (§49(4) / Rule 86(2)).
        if (Method == GstDepositService.PaymentMethod.Credit && interest > 0)
            return Fail(CashOnlyRule + " Pay the interest from cash or bank, or clear the interest box.");

        DomainLedger? bank = null;
        if (Method == GstDepositService.PaymentMethod.Bank)
        {
            bank = SelectedBank;
            if (bank is null)
                return Fail(BankOptions.Count == 0
                    ? "A bank-funded DRC-03 needs a Bank / Cash ledger, and this company has none."
                    : "Choose the Bank / Cash ledger this payment is made from.");
        }

        try
        {
            var (_, record) = _deposit.PostDrc03(
                cause, Period.Trim(), date,
                cgst, sgst, igst, cess, interest,
                Method, bank,
                drc03Ref: null,
                drc03aDemandRef: string.IsNullOrWhiteSpace(DemandRefText) ? null : DemandRefText.Trim(),
                createdAt: null,
                reasons: string.IsNullOrWhiteSpace(ReasonsText) ? null : ReasonsText.Trim());

            return Succeed($"DRC-03 filed for {record.Period} — ₹{R(record.TotalTaxPaisa + record.InterestPaisa)} " +
                           $"({record.Cause}).");
        }
        catch (ArgumentException ex) { return Fail(ex.Message); }
        catch (InvalidOperationException ex) { return Fail(ex.Message); }   // incl. the unfunded-cash-cell refusal
    }

    /// <summary>
    /// The cause string written into <see cref="GstDrc03.Cause"/>: the portal's verbatim wording, and — under the
    /// portal's own cause 11 — its "Please specify" text appended in parentheses. Nothing else is ever appended, so
    /// the stored cause always begins with a portal string a later reconciliation can match on.
    /// </summary>
    private string? ComposeCause(out string? error)
    {
        error = null;
        var text = SelectedCause.Text;
        if (!NeedsOthersSpecify) return text;
        if (string.IsNullOrWhiteSpace(OthersSpecifyText))
        {
            error = "Cause 11 is \"Others\" — specify the cause the portal is to be told.";
            return null;
        }
        return $"{text} — {OthersSpecifyText.Trim()}";
    }

    private bool Succeed(string message)
    {
        _storage.Save(_company);
        Rebuild();
        LastActionSucceeded = true;
        Message = message;
        _onChanged();
        return true;
    }

    private bool Fail(string message)
    {
        Message = message;
        LastActionSucceeded = false;
        return false;
    }

    private void LoadBankOptions()
    {
        BankOptions.Clear();
        foreach (var l in _company.Ledgers
                     .Where(l => ClassificationRules.IsCashOrBankLedger(l, _company))
                     .OrderBy(l => l.Name, StringComparer.OrdinalIgnoreCase))
            BankOptions.Add(l);
        SelectedBank = BankOptions.FirstOrDefault();
    }

    /// <summary>Parses one rupee box into paisa; blank ⇒ zero. ONE sub-paisa test (drift lock D3).</summary>
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
            error = $"{label}: a DRC-03 discharges a positive amount — '{text}' is negative.";
            return false;
        }
        if (!Apex.Ledger.PaisaConversion.TryToPaisaExact(r, out paisa))
        {
            error = $"{label}: '{text}' is finer than a paisa.";
            return false;
        }
        return true;
    }

    private static string R(long paisa) => IndianFormat.AmountAlways(new Money(paisa / 100m));
}
