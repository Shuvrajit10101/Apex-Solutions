using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Io;
using Apex.Ledger.Reports;
using Apex.Ledger.Services;
using Apex.Desktop.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using DomainLedger = Apex.Ledger.Domain.Ledger;

namespace Apex.Desktop.ViewModels;

/// <summary>
/// One tender row on the POS payment panel (catalog §11; Phase 6 slice 7 RQ-39/RQ-40; DP-4). It owns the tender
/// kind, its ledger picker (pre-filtered to the required group so grouping is valid by construction — Gift →
/// Sundry Debtors, Card/Cheque → Bank, Cash → Cash-in-Hand), the posted amount, the kind-specific reference
/// fields (card no / bank + cheque no), and — for Cash — the tendered cash and read-only change. Parsing is
/// deferred to the parent; every edit calls back so the parent re-reconciles Σ tenders vs the bill total.
/// </summary>
public sealed partial class PosTenderRowViewModel : ViewModelBase
{
    private readonly Action _onChanged;
    private bool _suppress;

    /// <summary>The tender kind this row represents (Gift / Card / Cheque / Cash).</summary>
    public PosTenderType Type { get; }

    /// <summary>The de-branded caption shown for this tender (e.g. "Credit/Debit Card").</summary>
    public string Label { get; }

    /// <summary>The ledgers this tender may debit — already filtered to the required group (RQ-41).</summary>
    public IReadOnlyList<DomainLedger> LedgerChoices { get; }

    [ObservableProperty] private DomainLedger? _selectedLedger;
    [ObservableProperty] private string _amountText = "0.00";

    /// <summary>Cash carries the residual/bill total, which the parent computes — its amount box is read-only.</summary>
    [ObservableProperty] private bool _amountReadOnly;

    [ObservableProperty] private string _cardNo = string.Empty;
    [ObservableProperty] private string _bankName = string.Empty;
    [ObservableProperty] private string _chequeNo = string.Empty;
    [ObservableProperty] private string _cashTenderedText = string.Empty;
    [ObservableProperty] private string _changeText = "0.00";

    public bool ShowCardNo => Type == PosTenderType.Card;
    public bool ShowChequeRefs => Type == PosTenderType.Cheque;
    public bool ShowCashFields => Type == PosTenderType.Cash;

    public PosTenderRowViewModel(PosTenderType type, string label,
        IReadOnlyList<DomainLedger> ledgerChoices, Action onChanged, Guid? defaultLedgerId)
    {
        Type = type;
        Label = label;
        LedgerChoices = ledgerChoices ?? throw new ArgumentNullException(nameof(ledgerChoices));
        _onChanged = onChanged ?? throw new ArgumentNullException(nameof(onChanged));
        _amountReadOnly = type == PosTenderType.Cash;

        _selectedLedger = (defaultLedgerId is { } id ? ledgerChoices.FirstOrDefault(l => l.Id == id) : null)
            ?? ledgerChoices.FirstOrDefault();
    }

    partial void OnSelectedLedgerChanged(DomainLedger? value) { if (!_suppress) _onChanged(); }
    partial void OnAmountTextChanged(string value) { if (!_suppress) _onChanged(); }
    partial void OnCardNoChanged(string value) { if (!_suppress) _onChanged(); }
    partial void OnBankNameChanged(string value) { if (!_suppress) _onChanged(); }
    partial void OnChequeNoChanged(string value) { if (!_suppress) _onChanged(); }
    partial void OnCashTenderedTextChanged(string value) { if (!_suppress) _onChanged(); }

    /// <summary>The parsed posted amount (0 when blank/unparsable).</summary>
    public decimal ParsedAmount => PosBillingViewModel.ParseMoney(AmountText);

    /// <summary>The parsed cash tendered (0 when blank/unparsable). Cash rows only.</summary>
    public decimal ParsedCashTendered => PosBillingViewModel.ParseMoney(CashTenderedText);

    /// <summary>
    /// The field-level refusal for this row's TYPED tender amount, or <c>null</c> when it can be stored
    /// (W0-13 S2a). Only the Gift/Card/Cheque rows type this — the Cash box is parent-written
    /// (<see cref="SetAutoValues"/>) and read-only — so the parent reads it on the non-cash rows alone.
    ///
    /// <para><b>Why the row and not the parent.</b> Every tender flows through <c>TryBuildTenders</c>, which
    /// builds a <see cref="Apex.Ledger.Domain.PosTender"/> OUTSIDE the Accept try-block: a domain throw there
    /// escapes the keystroke entirely. The refusal has to happen while the value is still text, and the text
    /// lives here.</para>
    /// </summary>
    public string? AmountError =>
        StorableAmount.ErrorFor(ParsedAmount, AmountText, $"the {Label} tender amount");

    /// <summary>
    /// The field-level refusal for the typed <see cref="CashTenderedText"/>, or <c>null</c>. Blank legitimately
    /// means "exact tender", so it is never an error. Cash rows only.
    /// </summary>
    public string? CashTenderedError =>
        string.IsNullOrWhiteSpace(CashTenderedText)
            ? null
            : StorableAmount.ErrorFor(ParsedCashTendered, CashTenderedText, "the cash tendered");

    /// <summary>Sets the Cash amount / change WITHOUT re-triggering the change callback (parent-driven auto-fill).</summary>
    public void SetAutoValues(decimal amount, decimal change)
    {
        _suppress = true;
        try
        {
            AmountText = amount.ToString("0.00", CultureInfo.InvariantCulture);
            ChangeText = change.ToString("0.00", CultureInfo.InvariantCulture);
        }
        finally { _suppress = false; }
    }
}

/// <summary>
/// The <b>POS Billing</b> voucher-entry screen (catalog §11; Phase 6 slice 7 RQ-38..RQ-44, RQ-53; TOP RISK #6;
/// PR-9; DP-4/DP-6). A POS bill <b>is</b> a Sales item-invoice whose single customer debit is replaced by a split
/// of tender debits: the item grid + party/godown/Sales-ledger + the GST computation are exactly the item-invoice
/// path, and the credit side (Cr Sales + Cr Output CGST/SGST/IGST) and the stock movement are byte-identical to a
/// normal sale — so GST reuses the Phase-4 engine unchanged and the bill flows into the standard Sales/GST reports
/// (RQ-43). The one new thing is the <b>tender panel</b>:
/// <list type="bullet">
///   <item><b>Single mode</b> (RQ-39): one Cash tender for the whole bill; Cash Tendered → read-only Change.</item>
///   <item><b>Multi mode</b> (RQ-40): Gift + Card + Cheque + Cash, the Cash line auto-filling the residual
///     (bill − the non-cash tenders), with Σ tenders reconciled to the bill and the change informational.</item>
/// </list>
/// <b>Alt+I</b> toggles Single ⇄ Multi both ways (RQ-42), preserving the entered items/party/godown. <b>Alt+A</b>
/// surfaces the tax analysis (RQ-53). On <see cref="Accept"/> the voucher posts through <see cref="LedgerService.Post"/>
/// (which runs the load-bearing tender grouping + reconciliation via <see cref="PosTenderService"/>), then persists;
/// when the POS config's <see cref="PosConfig.PrintAfterSave"/> is set it raises <see cref="PrintReceiptRequested"/>
/// with the retail receipt to preview. MVVM boundary: engine + persistence + Io, no Avalonia types — headlessly
/// unit-testable.
/// </summary>
public sealed partial class PosBillingViewModel : ViewModelBase, ISetsWorkingDate
{

    /// <summary>
    /// WI-5 (4c): the working-date field <b>F2</b> targets on this screen — the bill date. Assigning routes
    /// through the one shared day-first parser and echoes the canonical spelling.
    /// </summary>
    public string WorkingDateText
    {
        get => DateText;
        set => DateText = value;
    }

    private readonly Company _company;
    private readonly VoucherType _type;
    private readonly LedgerService _service;
    private readonly GstService _gst;
    private readonly CompanyStorage _storage;
    private readonly Action _onSaved;
    private readonly Action _onCancelled;
    private bool _recomputing;

    /// <summary>Suppresses <see cref="Recalculate"/> while <see cref="RehydrateFrom"/> is mid-flight.</summary>
    private bool _rehydrating;

    /// <summary>The posted bill this screen was opened to ALTER, or <see cref="Guid.Empty"/> for a fresh bill.</summary>
    private Guid _alteringVoucherId;

    /// <summary>True when this screen is altering a POSTED bill rather than keying a new one.</summary>
    public bool IsAltering => _alteringVoucherId != Guid.Empty;

    /// <summary>The posted bill being altered, or <see cref="Guid.Empty"/> for a fresh bill.</summary>
    public Guid AlteringVoucherId => _alteringVoucherId;

    /// <summary>Raised after Accept when the POS config's print-after-save is on — carries the retail receipt to preview.</summary>
    public event Action<PosReceiptData>? PrintReceiptRequested;

    public VoucherType Type => _type;
    public string TypeName => _type.Name;

    /// <summary>The stock items the item-line pickers choose from.</summary>
    public IReadOnlyList<StockItem> StockItems { get; }

    /// <summary>The godowns the header/line pickers choose from.</summary>
    public IReadOnlyList<Godown> Godowns { get; }

    /// <summary>The party (customer) choices — a walk-in "(cash)" first, then every ledger (party is informational, B2C).</summary>
    public ObservableCollection<PartyOption> Parties { get; } = new();

    [ObservableProperty] private PartyOption? _selectedParty;

    /// <summary>The Sales (Sales Accounts) ledger the taxable value leg credits (auto-defaulted, overridable).</summary>
    public ObservableCollection<DomainLedger> SalesLedgers { get; } = new();

    [ObservableProperty] private DomainLedger? _selectedSalesLedger;

    /// <summary>The default godown pre-selected on the bill (from POS config), applied to each item line.</summary>
    [ObservableProperty] private Godown? _selectedGodown;

    /// <summary>The editable item lines (Stock Item / Qty / Rate).</summary>
    public ObservableCollection<InventoryVoucherLineViewModel> Items { get; } = new();

    /// <summary>The four tender rows (Gift, Card, Cheque, Cash) — always present; visibility is gated per mode.</summary>
    public ObservableCollection<PosTenderRowViewModel> Tenders { get; } = new();

    private PosTenderRowViewModel Gift => Tenders[0];
    private PosTenderRowViewModel Card => Tenders[1];
    private PosTenderRowViewModel Cheque => Tenders[2];
    private PosTenderRowViewModel Cash => Tenders[3];

    /// <summary>The Cash tender row — the only tender shown in single mode (bound by the single-tender panel).</summary>
    public PosTenderRowViewModel CashRow => Cash;

    /// <summary>Alt+I — true when the bill is split across multiple tenders (multi mode); false = single Cash tender.</summary>
    [ObservableProperty] private bool _isMultiTender;

    // ---- live totals ----
    [ObservableProperty] private string _itemsTotalText = "0.00";
    [ObservableProperty] private string _gstCgstText = "0.00";
    [ObservableProperty] private string _gstSgstText = "0.00";
    [ObservableProperty] private string _gstIgstText = "0.00";

    /// <summary>
    /// The Compensation Cess on the bill (census T0-16). Ring-fenced out of the GST heads exactly as
    /// <c>GstService.InvoiceTax.TotalTax</c> ring-fences it (ER-2), and shown on its own row so the operator can
    /// see WHY a cess-bearing bill totals more than its heads — but it IS part of the bill total the tenders must
    /// reconcile to, because the customer pays it.
    /// </summary>
    [ObservableProperty] private string _gstCessText = "0.00";

    [ObservableProperty] private string _billTotalText = "0.00";
    [ObservableProperty] private string _tendersTotalText = "0.00";
    [ObservableProperty] private string _tenderBalanceText = "Balanced";
    [ObservableProperty] private string _changeText = "0.00";

    // ---- tax analysis (Alt+A; RQ-53) ----
    [ObservableProperty] private bool _isTaxAnalysisVisible;
    [ObservableProperty] private string _taxAnalysisText = string.Empty;

    [ObservableProperty] private string _title = string.Empty;
    [ObservableProperty] private DateOnly _date;
    [ObservableProperty] private string _narration = string.Empty;
    [ObservableProperty] private bool _canAccept;
    [ObservableProperty] private string? _message;
    [ObservableProperty] private int _savedNumber;

    /// <summary>The date as editable text (dd-MMM-yyyy) for the header TextBox.</summary>
    public string DateText
    {
        get => ApexDate.Format(Date);
        set
        {
            // WI-5: shared DAY-FIRST parse; reject-and-keep rather than silently discard.
            if (ApexDate.TryParse(value, Date, out var p))
            {
                if (p != Date) Date = p;
            }
            else
            {
                Message = ApexDate.ErrorFor(value);
            }

            OnPropertyChanged(nameof(DateText));
        }
    }

    public PosBillingViewModel(
        Company company,
        VoucherType type,
        CompanyStorage storage,
        Action onSaved,
        Action onCancelled,
        DateOnly? date = null)
    {
        _company = company ?? throw new ArgumentNullException(nameof(company));
        _type = type ?? throw new ArgumentNullException(nameof(type));
        _storage = storage ?? throw new ArgumentNullException(nameof(storage));
        _onSaved = onSaved ?? throw new ArgumentNullException(nameof(onSaved));
        _onCancelled = onCancelled ?? throw new ArgumentNullException(nameof(onCancelled));

        _service = new LedgerService(company);
        _gst = new GstService(company);
        StockItems = company.StockItems;
        Godowns = company.Godowns;

        var cfg = _type.PosConfig ?? new PosConfig();

        // Party choices: a walk-in "(cash)" sentinel first (B2C default), then every ledger.
        Parties.Add(new PartyOption { Ledger = null, Display = "◦ (cash) walk-in" });
        foreach (var l in company.Ledgers.OrderBy(l => l.Name, StringComparer.OrdinalIgnoreCase))
            Parties.Add(new PartyOption { Ledger = l, Display = l.Name });
        SelectedParty = (cfg.DefaultPartyId is { } pid ? Parties.FirstOrDefault(o => o.Ledger?.Id == pid) : null)
            ?? Parties.FirstOrDefault();

        // Sales-accounts ledgers for the value leg.
        foreach (var l in company.Ledgers
                     .Where(IsSalesLegLedger)
                     .OrderBy(l => l.Name, StringComparer.OrdinalIgnoreCase))
            SalesLedgers.Add(l);
        SelectedSalesLedger = SalesLedgers.FirstOrDefault();

        // Default godown from config, else the main location.
        SelectedGodown = (cfg.DefaultGodownId is { } gid ? Godowns.FirstOrDefault(g => g.Id == gid) : null)
            ?? Godowns.FirstOrDefault(g => g.IsMainLocation) ?? Godowns.FirstOrDefault();

        // Tender ledger candidate lists (pre-filtered to the required group, so grouping is valid by construction).
        var giftLedgers = company.Ledgers
            .Where(l => ClassificationRules.GroupIsUnder(l.GroupId, "Sundry Debtors", company))
            .OrderBy(l => l.Name, StringComparer.OrdinalIgnoreCase).ToList();
        var bankLedgers = company.Ledgers
            .Where(l => ClassificationRules.IsBankLedger(l, company))
            .OrderBy(l => l.Name, StringComparer.OrdinalIgnoreCase).ToList();
        var cashLedgers = company.Ledgers
            .Where(l => ClassificationRules.IsCashLedger(l, company))
            .OrderBy(l => l.Name, StringComparer.OrdinalIgnoreCase).ToList();

        Tenders.Add(new PosTenderRowViewModel(PosTenderType.GiftVoucher, "Gift Voucher", giftLedgers, Recalculate, cfg.TenderLedgerDefault(PosTenderType.GiftVoucher)));
        Tenders.Add(new PosTenderRowViewModel(PosTenderType.Card, "Credit/Debit Card", bankLedgers, Recalculate, cfg.TenderLedgerDefault(PosTenderType.Card)));
        Tenders.Add(new PosTenderRowViewModel(PosTenderType.Cheque, "Cheque/DD", bankLedgers, Recalculate, cfg.TenderLedgerDefault(PosTenderType.Cheque)));
        Tenders.Add(new PosTenderRowViewModel(PosTenderType.Cash, "Cash", cashLedgers, Recalculate, cfg.TenderLedgerDefault(PosTenderType.Cash)));

        // Default date: last voucher date, else books-begin.
        var last = company.Vouchers.Count == 0 ? (DateOnly?)null : company.Vouchers.Max(v => v.Date);
        Date = date ?? last ?? company.BooksBeginFrom;

        Title = $"{type.Name} — POS Billing";
        AddItemLine();
        Recalculate();
    }

    // =============================================================== POS config proxies (RQ-38; edit on the type)

    /// <summary>The live POS config on the voucher type (always non-null on a POS type; created on demand).</summary>
    private PosConfig Config => _type.PosConfig ??= new PosConfig();

    /// <summary>Open the retail-receipt preview after Accept (RQ-38). Persisted on the voucher type.</summary>
    public bool PrintAfterSave
    {
        get => _type.PosConfig?.PrintAfterSave ?? false;
        set { if (Config.PrintAfterSave == value) return; Config.PrintAfterSave = value; _storage.Save(_company); OnPropertyChanged(); }
    }

    /// <summary>Receipt title (RQ-38); blank ⇒ the default. Persisted on the voucher type.</summary>
    public string ReceiptTitle
    {
        get => _type.PosConfig?.DefaultTitle ?? string.Empty;
        set { var v = string.IsNullOrWhiteSpace(value) ? null : value.Trim(); if (Config.DefaultTitle == v) return; Config.DefaultTitle = v; _storage.Save(_company); OnPropertyChanged(); }
    }

    /// <summary>Thank-you message line 1 (RQ-38). Persisted on the voucher type.</summary>
    public string Message1
    {
        get => _type.PosConfig?.Message1 ?? string.Empty;
        set { var v = string.IsNullOrWhiteSpace(value) ? null : value.Trim(); if (Config.Message1 == v) return; Config.Message1 = v; _storage.Save(_company); OnPropertyChanged(); }
    }

    /// <summary>Thank-you message line 2 (RQ-38). Persisted on the voucher type.</summary>
    public string Message2
    {
        get => _type.PosConfig?.Message2 ?? string.Empty;
        set { var v = string.IsNullOrWhiteSpace(value) ? null : value.Trim(); if (Config.Message2 == v) return; Config.Message2 = v; _storage.Save(_company); OnPropertyChanged(); }
    }

    /// <summary>The declaration line printed on the receipt (RQ-38). Persisted on the voucher type.</summary>
    public string Declaration
    {
        get => _type.PosConfig?.Declaration ?? string.Empty;
        set { var v = string.IsNullOrWhiteSpace(value) ? null : value.Trim(); if (Config.Declaration == v) return; Config.Declaration = v; _storage.Save(_company); OnPropertyChanged(); }
    }

    // =============================================================== item lines

    /// <summary>Adds a blank item line (Stock Item / Qty / Rate), defaulting its godown to the POS default.</summary>
    public InventoryVoucherLineViewModel AddItemLine()
    {
        var line = new InventoryVoucherLineViewModel(InventoryLineKind.Movement, StockItems, Godowns, Recalculate);
        if (SelectedGodown is not null) line.SelectedGodown = SelectedGodown;
        Items.Add(line);
        Recalculate();
        return line;
    }

    /// <summary>Removes an item line (keeping at least one); recomputes.</summary>
    public void RemoveItemLine(InventoryVoucherLineViewModel line)
    {
        if (Items.Count <= 1) return;
        Items.Remove(line);
        Recalculate();
    }

    partial void OnSelectedPartyChanged(PartyOption? value) => Recalculate();
    partial void OnSelectedSalesLedgerChanged(DomainLedger? value) => Recalculate();
    partial void OnDateChanged(DateOnly value) => OnPropertyChanged(nameof(DateText));

    partial void OnSelectedGodownChanged(Godown? value)
    {
        // Push the header default godown to every item line (each line stays individually overridable).
        if (value is not null)
            foreach (var l in Items) l.SelectedGodown = value;
        Recalculate();
    }

    partial void OnIsMultiTenderChanged(bool value)
    {
        OnPropertyChanged(nameof(PaymentModeText));
        Recalculate();
    }

    /// <summary>The current payment-mode caption ("Multi Tender" / "Single Tender").</summary>
    public string PaymentModeText => IsMultiTender ? "Multi Tender" : "Single Tender";

    /// <summary>Alt+I — toggles Single ⇄ Multi payment mode (both ways, RQ-42), preserving items/party/godown.</summary>
    public void TogglePaymentMode() => IsMultiTender = !IsMultiTender;

    // =============================================================== GST + totals

    /// <summary>Σ of the complete item lines' values (billed qty × effective rate, paisa-exact).</summary>
    private decimal ItemsTotal()
    {
        var sum = 0m;
        foreach (var l in Items)
            if (l.IsComplete && l.EffectiveRate is { } rate)
                sum += Money.ForexBase(rate, l.ParsedBilledQuantity).Amount;
        return sum;
    }

    private readonly record struct PosGst(GstService.InvoiceTax Tax, bool InterState, StockItem? Unresolved)
    {
        public bool HasUnresolved => Unresolved is not null;
    }

    /// <summary>
    /// Computes GST over the complete item lines (Output direction) — identical to a normal sales invoice (RQ-43).
    ///
    /// <para>🔴 <b>INCLUDING the Compensation Cess (census T0-16).</b> This method used to build
    /// <c>TaxableLine(value, rate)</c> with no cess argument at all, where
    /// <c>VoucherEntryViewModel.ComputeItemInvoiceGst</c> resolves one and passes it — so the SAME cess-bearing
    /// item collected the cess on a Sales item invoice and ZERO of it over the counter, on the bill, on the
    /// tenders and in the GSTR-1 cess column. It now calls the same <see cref="GstService.ResolveCess"/> with the
    /// same arguments the accounting screen passes, so the two screens agree by construction.</para>
    ///
    /// <para><b>The cess AND the rate are both resolved as of <see cref="Date"/>.</b>
    /// ~~<i>"The cess is resolved as of <c>Date</c>, and the RATE deliberately is not. <c>ResolveCess</c> has no
    /// date-blind overload — a dated HSN cess row cannot be selected without one — while <c>ResolveRate</c> does,
    /// and both POS rate resolutions still use it. That is census <b>T0-19</b>, a separate open defect with its own
    /// row."</i>~~ — 🟡 <b>struck at the merge, 2026-09-04: T0-19 IS CLOSED.</b> A parallel track gave both POS rate
    /// resolutions their date and DELETED the date-blind two-argument <c>ResolveRate</c> overload outright, so the
    /// premise of the struck sentence no longer exists. The paragraph is struck rather than deleted because it was
    /// this method's stated reason for a deliberate asymmetry, and a reader who met the asymmetry elsewhere needs
    /// to find out here that it is gone. Kept as the record; the behaviour it describes does not survive.</para>
    /// </summary>
    private PosGst? ComputeGst()
    {
        if (!_company.GstEnabled) return null;
        var partyState = SelectedParty?.Ledger?.PartyGst?.StateCode;
        var interState = _gst.IsInterState(partyState);

        var taxable = new List<GstService.TaxableLine>();
        foreach (var l in Items.Where(l => l.IsComplete))
        {
            if (l.ParsedRate is not { } rate || rate <= 0m) continue;
            var lineValue = Money.ForexBase(l.EffectiveRate ?? new Money(rate), l.ParsedBilledQuantity);
            // 🔴 T0-19 — RESOLVE AS OF THE BILL DATE. This used to call the date-blind two-argument overload, which
            // forwarded `voucherDate: null` and so skipped the dated GstRateHistory override entirely: the same item
            // sold at this counter and on a Sales item invoice on the SAME DAY carried different tax whenever a rate
            // revision was in force, and the counter kept the pre-revision rate for ever. The overload is deleted.
            var res = _gst.ResolveRate(l.SelectedItem, SelectedSalesLedger, Date);
            if (GstService.IsUnresolved(res))
                return new PosGst(EmptyTax(), interState, l.SelectedItem);
            if (!res.IsTaxable) continue;   // Exempt/Nil/Non-GST ⇒ no cess either (ResolveCess agrees)
            // Specific / RSP-factor cess is valued per UNIT, so it takes the BILLED quantity — the same figure the
            // taxable value is built from, never the actual.
            var cess = _gst.ResolveCess(l.SelectedItem, SelectedSalesLedger, Date, l.ParsedBilledQuantity);
            taxable.Add(new GstService.TaxableLine(lineValue, res.RateBasisPoints, cess));
        }
        return new PosGst(_gst.ComputeInvoiceTax(taxable, interState, GstTaxDirection.Output), interState, null);
    }

    /// <summary>
    /// 🔴 The GST <b>this screen's own derivation</b> puts on the POSTED item rows — the tax the alteration compares
    /// the STAMPED tax against, on BOTH of the axes the shape signature cannot see: the Compensation-Cess magnitude
    /// (<see cref="VoucherAlterationDerivedLegs.CessMagnitudeDriftRefusal"/>, which reads <c>TotalCess</c>) and the
    /// per-leg amount and taxable value (<see cref="VoucherAlterationDerivedLegs.TaxMagnitudeDriftRefusal"/>, which
    /// reads <c>TaxLines</c>). ONE re-derivation feeds both, as on the accounting door.
    /// The POSTED rows, not the amended ones, so an ordinary amendment (which moves the tax freely) is not seen
    /// here and only a master that moved underneath can make the two figures disagree.
    ///
    /// <para>🔴 <b>IT MIRRORS <see cref="ComputeGst"/> LINE FOR LINE, CESS INCLUDED — and that is now what makes
    /// the cess arm a real master-drift pin</b> (census T0-16, closed). This paragraph used to record the opposite:
    /// <c>ComputeGst</c> resolved no cess, so this mirror resolved none either, and the cess comparison could only
    /// ever refuse a posted bill carrying a cess leg the screen would have dropped. Now that the counter collects
    /// the cess, both halves resolve it through the same <c>GstService.ResolveCess</c> as of the posted bill's own
    /// date, and the comparison says what its accounting twin says: a cess master that moved under a bill nobody
    /// touched is refused by name, while an ordinary amendment moves the cess freely.</para>
    /// </summary>
    private GstService.InvoiceTax? ReDerivedTaxOnPostedRows(Voucher existing)
    {
        if (!_company.GstEnabled) return EmptyTax();

        var partyState = SelectedParty?.Ledger?.PartyGst?.StateCode;
        var interState = _gst.IsInterState(partyState);

        var taxable = new List<GstService.TaxableLine>(existing.InventoryLines.Count);
        foreach (var posted in existing.InventoryLines)
        {
            if (posted.Rate.Amount <= 0m) continue;
            if (StockItems.FirstOrDefault(i => i.Id == posted.StockItemId) is not { } item) return null;

            // T0-19 — the same dated resolution as ComputeGst above (this mirrors it line for line, and a mirror
            // that resolved on a different date would refuse every dated bill as "drifted").
            //
            // 🔴 BOTH HALVES RESOLVE ON `Date`, AND THAT AGREEMENT IS A MERGE RESOLUTION, NOT AN INHERITED FACT.
            // Two parallel tracks touched these two lines: one gave the RATE a date (it was date-blind, census
            // T0-19), the other gave the line a CESS and resolved it on `existing.Date`. Git merged both cleanly,
            // and the result resolved ONE re-derivation at TWO dates — reachable, because the POS date field is
            // editable (`DateText`, TwoWay) and `RehydrateFrom` only SEEDS it from the voucher. The tie is broken
            // by the ACCOUNTING DOOR'S TWIN, which both tracks cite as the reference and neither changed:
            // `VoucherEntryViewModel.ReDerivedTaxOnPostedRows` passes `Date` to `ResolveRate` AND to `ResolveCess`.
            // Matching it is what makes this method's own doc claim — "it mirrors ComputeGst line for line", "as on
            // the accounting door" — true rather than aspirational.
            var res = _gst.ResolveRate(item, SelectedSalesLedger, Date);
            if (GstService.IsUnresolved(res)) return null;
            if (!res.IsTaxable) continue;
            var cess = _gst.ResolveCess(item, SelectedSalesLedger, Date, posted.BilledQuantity);
            taxable.Add(new GstService.TaxableLine(posted.Value, res.RateBasisPoints, cess));
        }

        return _gst.ComputeInvoiceTax(taxable, interState, GstTaxDirection.Output);
    }

    private static GstService.InvoiceTax EmptyTax() => new()
    {
        TaxLines = Array.Empty<EntryLine>(),
        LineBreakdown = Array.Empty<GstService.LineTax>(),
    };

    /// <summary>
    /// The current bill total = Σ item value + Σ GST + Compensation Cess (the amount the tenders must reconcile
    /// to). The cess is ADDED here although it is ring-fenced out of <c>InvoiceTax.TotalTax</c>: the ring-fence is
    /// about which GST head a figure belongs to, not about who pays it, and <c>BuildPosBill</c> posts a Cess tax
    /// leg that the tender debits have to fund or the voucher does not balance (census T0-16).
    /// </summary>
    private decimal BillTotal(
        out decimal taxable, out decimal cgst, out decimal sgst, out decimal igst, out decimal cess)
    {
        taxable = ItemsTotal();
        var gst = ComputeGst();
        cgst = gst?.Tax.TotalCgst.Amount ?? 0m;
        sgst = gst?.Tax.TotalSgst.Amount ?? 0m;
        igst = gst?.Tax.TotalIgst.Amount ?? 0m;
        cess = gst?.Tax.TotalCess.Amount ?? 0m;
        return taxable + cgst + sgst + igst + cess;
    }

    /// <summary>
    /// Recomputes the live totals, auto-fills the Cash tender (residual in multi mode, bill total in single mode),
    /// computes the informational change, reconciles Σ tenders vs the bill and refreshes the Accept gate. Re-entrancy
    /// guarded — auto-filling the Cash amount raises change notifications that would otherwise re-enter this.
    /// </summary>
    public void Recalculate()
    {
        if (_recomputing) return;
        // 🔴 S5e — suppressed while ForAlter is mid-flight. The rehydration fills the party, the sales ledger, the
        // item rows and the four tender rows one assignment at a time, and every one of those raises a change
        // notification that re-enters here — where SetAutoValues would stamp the Cash tender from a bill total
        // computed off the rows that happen to exist so far. One pass runs at the end, when the screen is whole.
        //
        // 🔴 MUTATION RESULT: deleting this line reddens nothing - the final pass overwrites every figure a
        // mid-flight pass could have stamped. Kept as a suppression, not claimed as a live safeguard, exactly as
        // its sibling in VoucherEntryViewModel.RecalculateItemInvoice is.
        if (_rehydrating) return;
        // Guard against the change-notifications the constructor's header assignments (SelectedParty / SalesLedger /
        // Godown) raise BEFORE the four tender rows are added — the ctor calls Recalculate() once at the end, when the
        // Tenders list is fully populated. Without this the very first party/ledger/godown default crashes the screen.
        if (Tenders.Count < 4) return;
        _recomputing = true;
        try
        {
            // 🔴 An unresolvable-cess input (an RSP-factor cess item with no declared Retail Sale Price) is a
            // fail-fast domain error inside ResolveCess, and this method is reached from a KEYSTROKE handler — so
            // it must surface as a message and a closed Accept gate, never as an unhandled crash of the counter.
            // The accounting screen's RecalculateAccountingInvoice guards its own cess resolution the same way.
            decimal bill, taxable, cgst, sgst, igst, cess;
            try
            {
                bill = BillTotal(out taxable, out cgst, out sgst, out igst, out cess);
            }
            catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
            {
                Message = ex.Message;
                ItemsTotalText = IndianFormat.AmountAlways(ItemsTotal());
                GstCgstText = GstSgstText = GstIgstText = GstCessText = "0.00";
                BillTotalText = ItemsTotalText;
                CanAccept = false;
                return;
            }

            ItemsTotalText = IndianFormat.AmountAlways(taxable);
            GstCgstText = IndianFormat.AmountAlways(cgst);
            GstSgstText = IndianFormat.AmountAlways(sgst);
            GstIgstText = IndianFormat.AmountAlways(igst);
            GstCessText = IndianFormat.AmountAlways(cess);
            BillTotalText = IndianFormat.AmountAlways(bill);

            // Non-cash tenders (only in multi mode).
            var gift = IsMultiTender ? Gift.ParsedAmount : 0m;
            var card = IsMultiTender ? Card.ParsedAmount : 0m;
            var cheque = IsMultiTender ? Cheque.ParsedAmount : 0m;

            // Cash gets the residual (multi) or the whole bill (single). A negative residual (non-cash over-tender)
            // is clamped to 0 for display; Accept rejects it with a friendly message.
            var cashPayable = IsMultiTender ? bill - (gift + card + cheque) : bill;
            var residualNegative = cashPayable < 0m;
            var cashShown = residualNegative ? 0m : cashPayable;

            // Change = cash tendered − cash payable (>= 0). Blank tendered ⇒ exact (change 0).
            var tendered = Cash.CashTenderedText.Trim().Length == 0 ? cashShown : Cash.ParsedCashTendered;
            var change = tendered - cashShown;
            Cash.SetAutoValues(cashShown, change < 0m ? 0m : change);
            ChangeText = IndianFormat.AmountAlways(change < 0m ? 0m : change);

            // Σ tenders that are actually in play.
            var tenderSum = SumActiveTenders(gift, card, cheque, cashShown);
            TendersTotalText = IndianFormat.AmountAlways(tenderSum);
            var diff = tenderSum - bill;
            TenderBalanceText = diff == 0m
                ? "Balanced"
                : diff > 0m
                    ? $"Over by {IndianFormat.AmountAlways(diff)}"
                    : $"Short by {IndianFormat.AmountAlways(-diff)}";

            RefreshCanAccept(bill, taxable, cashShown, tendered, residualNegative, change);
        }
        finally { _recomputing = false; }
    }

    private decimal SumActiveTenders(decimal gift, decimal card, decimal cheque, decimal cashShown)
    {
        if (!IsMultiTender) return cashShown;
        return gift + card + cheque + cashShown;
    }

    private void RefreshCanAccept(decimal bill, decimal taxable, decimal cashShown, decimal tendered, bool residualNegative, decimal change)
    {
        var completeLines = Items.Count(l => l.IsComplete);
        var hasHalfFilled = Items.Any(l => !l.IsBlank && !l.IsComplete);
        var everyLineRateOk = Items.Where(l => l.IsComplete).All(l => l.ParsedRate is { } r && r > 0m);

        var ok = SelectedSalesLedger is not null
                 && completeLines >= 1
                 && !hasHalfFilled
                 && everyLineRateOk
                 && bill > 0m
                 && !residualNegative
                 && change >= 0m
                 // W0-13 S2a — the Accept gate must close on a tender the paisa store cannot carry. The
                 // Σ-tenders reconciliation below does NOT catch it: a sub-paisa tender and its sub-paisa
                 // residual foot to the bill exactly.
                 && UnstorableTenderError() is null;

        if (ok)
        {
            // Σ tenders must reconcile to the bill, and every in-play tender needs a ledger.
            var gift = IsMultiTender ? Gift.ParsedAmount : 0m;
            var card = IsMultiTender ? Card.ParsedAmount : 0m;
            var cheque = IsMultiTender ? Cheque.ParsedAmount : 0m;
            var sum = SumActiveTenders(gift, card, cheque, cashShown);
            ok = sum == bill;
            if (ok)
            {
                if (IsMultiTender)
                {
                    if (gift > 0m && Gift.SelectedLedger is null) ok = false;
                    if (card > 0m && Card.SelectedLedger is null) ok = false;
                    if (cheque > 0m && Cheque.SelectedLedger is null) ok = false;
                }
                if (cashShown > 0m && Cash.SelectedLedger is null) ok = false;
            }
        }

        CanAccept = ok;
    }

    // =============================================================== tax analysis (Alt+A; RQ-53)

    /// <summary>Alt+A — surfaces the per-rate tax analysis for the current bill (RQ-53). Identical to a normal sale.</summary>
    public string ShowTaxAnalysis()
    {
        var gst = ComputeGst();
        if (gst is not { } g || !_company.GstEnabled)
        {
            TaxAnalysisText = "GST is not enabled for this company.";
            IsTaxAnalysisVisible = true;
            return TaxAnalysisText;
        }
        if (g.HasUnresolved)
        {
            TaxAnalysisText = $"Item '{g.Unresolved!.Name}' is taxable but has no resolvable GST rate.";
            IsTaxAnalysisVisible = true;
            return TaxAnalysisText;
        }

        var lines = new List<string>();
        foreach (var grp in g.Tax.LineBreakdown
                     .GroupBy(l => l.IntegratedBasisPoints)
                     .OrderBy(gr => gr.Key))
        {
            var rateLabel = (grp.Key / 100m).ToString("0.##", CultureInfo.InvariantCulture) + "%";
            var taxable = grp.Aggregate(Money.Zero, (a, l) => a + l.TaxableValue);
            var cgst = grp.Aggregate(Money.Zero, (a, l) => a + l.Cgst);
            var sgst = grp.Aggregate(Money.Zero, (a, l) => a + l.Sgst);
            var igst = grp.Aggregate(Money.Zero, (a, l) => a + l.Igst);
            lines.Add(g.InterState
                ? $"{rateLabel}: taxable {IndianFormat.AmountAlways(taxable.Amount)}, IGST {IndianFormat.AmountAlways(igst.Amount)}"
                : $"{rateLabel}: taxable {IndianFormat.AmountAlways(taxable.Amount)}, CGST {IndianFormat.AmountAlways(cgst.Amount)}, SGST {IndianFormat.AmountAlways(sgst.Amount)}");
        }
        TaxAnalysisText = lines.Count == 0
            ? "No taxable lines on this bill."
            : string.Join("\n", lines);
        IsTaxAnalysisVisible = true;
        return TaxAnalysisText;
    }

    // =============================================================== accept

    /// <summary>
    /// Assembles the POS Sales voucher — item lines to outward inventory lines; Cr Sales(taxable) + Cr Output GST
    /// (identical to a normal sale); the customer Dr replaced by the tender debits — and pre-validates it for
    /// friendly messages.
    /// </summary>
    /// <summary>
    /// One built POS bill: the balanced accounting legs (tender debits + Cr Sales + Cr Output GST), the outward
    /// stock lines, the tender records, and the figures the retail receipt needs. <b>Everything that decides a
    /// FIGURE is in here</b>, so the Post caller and the Replace caller cannot drift apart on what a POS bill IS.
    /// </summary>
    private sealed record PosBillBuild(
        List<EntryLine> EntryLines,
        List<VoucherInventoryLine> InventoryLines,
        List<PosTender> Tenders,
        Guid? PartyId,
        Money Taxable,
        GstService.InvoiceTax? InvoiceTax,
        bool InterState,
        decimal Change);

    /// <summary>
    /// Builds the POS bill from the screen, or returns <c>null</c> with <see cref="Message"/> set on any refusal.
    /// Shared verbatim by <see cref="Accept"/> (which Posts a new bill) and <see cref="AcceptAlteration"/> (which
    /// Replaces a posted one), so GST, the tender split and the storability front lines are derived ONCE.
    /// </summary>
    private PosBillBuild? BuildPosBill()
    {
        Message = null;
        Recalculate();

        if (SelectedSalesLedger is not { } salesLedger)
        {
            Message = "No Sales ledger is configured to post the value leg to.";
            return null;
        }
        if (Items.Any(l => !l.IsBlank && !l.IsComplete))
        {
            Message = "Every item line needs a stock item, a godown, a positive quantity and a positive rate.";
            return null;
        }
        var complete = Items.Where(l => l.IsComplete).ToList();
        if (complete.Count == 0)
        {
            Message = "Enter at least one item line before accepting.";
            return null;
        }

        // Outward inventory lines + taxable value (the pairing invariant holds by construction: Σ item == Cr Sales).
        var inventoryLines = new List<VoucherInventoryLine>(complete.Count);
        var taxable = Money.Zero;
        foreach (var l in complete)
        {
            if (l.ParsedRate is not { } rate || rate <= 0m)
            {
                Message = $"Item '{l.SelectedItem!.Name}' needs a rate greater than zero.";
                return null;
            }
            // W0-13 S2a, FRONT LINE ON THE RATE. InventoryVoucherLineViewModel.ParsedRate is a bare TryParse and
            // RefreshCanAccept tests only `r > 0m`, so a 17-digit rate — PAISA-EXACT, therefore invisible to every
            // sub-paisa guard — sailed through to the store and raised OverflowException from the (long) narrowing
            // cast inside PaisaConversion.ToPaisaExact. OverflowException is an ArithmeticException, so it is NOT
            // an InvalidVoucherException and it was not matched by the narrow filter this Accept used to carry:
            // the bill was already on the shared Company and the keystroke crashed.
            if (StorableAmount.ErrorFor(rate, l.RateText, $"the rate for '{l.SelectedItem!.Name}'") is { } rateError)
            {
                Message = rateError;
                return null;
            }
            var effRate = l.EffectiveRate ?? new Money(rate);
            inventoryLines.Add(new VoucherInventoryLine(
                l.SelectedItem!.Id, l.SelectedGodown!.Id, l.ParsedActualQuantity, effRate,
                direction: StockDirection.Outward, batchLabel: l.Batch, billedQuantity: l.ParsedBilledQuantity));
            taxable += Money.ForexBase(effRate, l.ParsedBilledQuantity);
        }

        // GST (identical to a normal sale) — a taxable line with no resolvable rate fails fast.
        var taxLines = new List<EntryLine>();
        var billTotal = taxable;
        bool interState = false;
        GstService.InvoiceTax? invoiceTax = null;
        if (_company.GstEnabled)
        {
            PosGst gst;
            try { gst = ComputeGst()!.Value; }
            catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
            { Message = $"Cannot accept: {ex.Message}"; return null; }
            if (gst.HasUnresolved)
            {
                Message = $"Item '{gst.Unresolved!.Name}' is taxable but no GST rate is set on the item, the Sales " +
                          "ledger, or the company. Set a rate before accepting.";
                return null;
            }
            taxLines.AddRange(gst.Tax.TaxLines);
            invoiceTax = gst.Tax;
            interState = gst.InterState;
            // 🔴 + TotalCess (census T0-16). TaxLines already CONTAINS the Cess leg, so a bill total that added
            // only TotalTax — which ring-fences the cess out (ER-2) — funded the tender debits short by exactly
            // the cess and the voucher could not balance. The accounting item invoice adds it to the party total
            // for the same reason.
            billTotal = new Money(taxable.Amount + gst.Tax.TotalTax.Amount + gst.Tax.TotalCess.Amount);
        }

        // W0-13 S2a — the DERIVED bill total is a persisted paisa figure too (it becomes the Cash tender amount),
        // and it is a product: two storable rates over two lines can still foot to more than the store can carry.
        // Guarding it here is what makes the "storable by construction" claim on UnstorableTenderError TRUE for the
        // cash residual and the change, both of which are bounded by it.
        if (StorableAmount.ErrorFor(billTotal.Amount, IndianFormat.AmountAlways(billTotal.Amount), "the bill total")
            is { } billError)
        {
            Message = billError;
            return null;
        }

        // W0-13 S2a — refuse an unstorable typed tender BEFORE TryBuildTenders, which constructs PosTender records
        // outside this method's try-block: a domain throw there would escape Accept as an unhandled crash. Before
        // this guard a sub-paisa card tender left the cash residual sub-paisa too, Σ tenders still footed to the
        // bill EXACTLY, so the screen accepted it — then Post appended the bill to the shared Company and Save
        // threw. The refused bill stayed on the aggregate and bricked every later save. This guard closes the
        // sub-paisa CAUSE; the missing rollback it used to rely on is closed separately, at the save below.
        if (UnstorableTenderError() is { } unstorableTender)
        {
            Message = unstorableTender;
            return null;
        }

        // Build the tender records (Cash posts the residual/bill — never the tendered; change is informational).
        if (!TryBuildTenders(billTotal.Amount, out var tenders, out var change))
            return null; // Message already set

        var entryLines = new List<EntryLine>();
        entryLines.AddRange(PosTenderService.BuildTenderDebitLines(tenders));
        entryLines.Add(new EntryLine(salesLedger.Id, taxable, DrCr.Credit));
        entryLines.AddRange(taxLines);

        return new PosBillBuild(
            entryLines, inventoryLines, tenders, SelectedParty?.Ledger?.Id,
            taxable, invoiceTax, interState, change);
    }

    /// <summary>
    /// Ctrl+A accept: builds the POS Sales voucher (see <see cref="BuildPosBill"/>), posts it through
    /// <see cref="LedgerService.Post"/> (which enforces the tender grouping + reconciliation; nothing persists on
    /// failure) and saves. When the POS config's print-after-save is set it raises
    /// <see cref="PrintReceiptRequested"/> with the retail receipt.
    ///
    /// <para>🔴 <b>Hard-refuses on an ALTERING screen</b>, mirroring <c>VoucherEntryViewModel.Accept</c>. This
    /// method is build + <c>Post</c>: it mints a fresh <see cref="Guid"/> and posts a SECOND bill under the next
    /// number in the series, leaving the original standing — so the day's turnover, its output GST and the units
    /// issued all double. <see cref="AcceptAlteration"/> is the alteration verb, and
    /// <c>AcceptAlterationCore</c> already refuses the mirror case (a NON-altering screen); this is the half that
    /// duplicates a document, and it is asserted in-method rather than left to the shell's branch so a later
    /// caller cannot re-open it.</para>
    /// </summary>
    public bool Accept()
    {
        if (IsAltering)
        {
            Message = "This screen is altering a posted bill — accepting it as a new bill would post a second one "
                    + "beside it under the next number, doubling the sale, its output GST and the stock issued. "
                    + "Use the alteration accept instead.";
            return false;
        }

        if (BuildPosBill() is not { } built) return false;
        var (entryLines, inventoryLines, tenders, partyId, taxable, invoiceTax, interState, change) = built;

        var voucher = new Voucher(
            Guid.NewGuid(), _type.Id, Date, entryLines,
            number: 0,
            narration: string.IsNullOrWhiteSpace(Narration) ? null : Narration.Trim(),
            partyId: partyId,
            inventoryLines: inventoryLines,
            posTenders: tenders);

        try
        {
            var posted = _service.Post(voucher);   // appends the bill to the SHARED Company; throws ⇒ nothing added

            // W0-13 S2b — THE SAVE GETS ITS OWN GUARD, and the restore runs FIRST and UNCONDITIONALLY. Post has
            // already appended the bill to the shared aggregate; Save is transactional, so a store failure leaves
            // the .db without it. Under the old single narrow `when (ex is InvalidOperationException or
            // ArgumentException)` filter a SqliteException (SQLITE_BUSY from a second instance holding the write
            // lock, READONLY, FULL) — or an OverflowException from a figure past long paisa — escaped Accept
            // UNHANDLED with the refused bill still on the aggregate, so every LATER save diverged. This is the
            // shape VoucherEntryViewModel.PostAndSave already had; a type filter must never be what decides
            // whether the rollback runs.
            try
            {
                _storage.Save(_company);
            }
            catch (Exception ex)
            {
                _company.RemoveVoucher(posted);
                if (!SaveFailure.IsReportable(ex)) throw;
                Message = $"Could not save the bill: {ex.Message} The bill was not kept — nothing was changed.";
                return false;
            }

            SavedNumber = posted.Number;
            Message = $"{_type.Name} No. {_company.FormatVoucherNumber(posted)} accepted.";

            if (PrintAfterSave)
                PrintReceiptRequested?.Invoke(BuildReceipt(posted, tenders, taxable, invoiceTax, interState, change));

            _onSaved();
            return true;
        }
        catch (UnbalancedVoucherException)
        {
            Message = "The POS bill is out of balance. Not saved.";
            return false;
        }
        catch (InvalidVoucherException ex)
        {
            Message = $"Cannot accept: {ex.Message}";
            return false;
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            // Reached only by a PRE-POST refusal (Post's own domain throws) — the save has its own guard above.
            Message = $"Cannot accept: {ex.Message}";
            return false;
        }
    }

    /// <summary>
    /// Builds the ordered tender records (Gift, Card, Cheque, Cash) for the current mode. Single mode = one Cash
    /// tender for the whole bill; multi mode = the entered non-cash tenders + Cash for the residual. Sets a friendly
    /// <see cref="Message"/> and returns false on a bad split (over-tender / short cash / missing ledger).
    /// </summary>
    private bool TryBuildTenders(decimal billTotal, out List<PosTender> tenders, out decimal change)
    {
        tenders = new List<PosTender>();
        change = 0m;

        decimal nonCash = 0m;
        if (IsMultiTender)
        {
            if (!AddNonCash(Gift, tenders, ref nonCash)) return false;
            if (!AddNonCash(Card, tenders, ref nonCash)) return false;
            if (!AddNonCash(Cheque, tenders, ref nonCash)) return false;
        }

        var cashPayable = billTotal - nonCash;
        if (cashPayable < 0m)
        {
            Message = "The non-cash tenders over-pay the bill. Reduce a tender so the cash residual is not negative.";
            return false;
        }

        if (cashPayable > 0m)
        {
            if (Cash.SelectedLedger is not { } cashLedger)
            {
                Message = "Select the Cash ledger for the residual payable.";
                return false;
            }
            var tendered = Cash.CashTenderedText.Trim().Length == 0 ? cashPayable : Cash.ParsedCashTendered;
            change = tendered - cashPayable;
            if (change < 0m)
            {
                Message = "Cash tendered is less than the cash payable. The customer must tender at least the residual.";
                return false;
            }
            tenders.Add(new PosTender(PosTenderType.Cash, cashLedger.Id, new Money(cashPayable),
                Tendered: new Money(tendered), Change: new Money(change)));
        }

        if (tenders.Count == 0)
        {
            Message = "Enter at least one payment tender.";
            return false;
        }
        return true;
    }

    private bool AddNonCash(PosTenderRowViewModel row, List<PosTender> tenders, ref decimal nonCash)
    {
        var amt = row.ParsedAmount;
        if (amt <= 0m) return true; // blank tender is simply not used
        if (row.SelectedLedger is not { } ledger)
        {
            Message = $"Select a ledger for the {row.Label} tender (or clear its amount).";
            return false;
        }
        nonCash += amt;
        tenders.Add(row.Type switch
        {
            PosTenderType.GiftVoucher => new PosTender(PosTenderType.GiftVoucher, ledger.Id, new Money(amt)),
            PosTenderType.Card => new PosTender(PosTenderType.Card, ledger.Id, new Money(amt),
                CardNo: string.IsNullOrWhiteSpace(row.CardNo) ? null : row.CardNo.Trim()),
            PosTenderType.Cheque => new PosTender(PosTenderType.Cheque, ledger.Id, new Money(amt),
                BankName: string.IsNullOrWhiteSpace(row.BankName) ? null : row.BankName.Trim(),
                ChequeNo: string.IsNullOrWhiteSpace(row.ChequeNo) ? null : row.ChequeNo.Trim()),
            _ => throw new InvalidOperationException("Non-cash tender expected."),
        });
        return true;
    }


    // =============================================================== Phase 10.11 S5e — ALTER a posted POS bill

    /// <summary>
    /// 🔴 <b>The POS screen's entry door — opens it on a POSTED bill, pre-filled, or refuses BY NAME.</b> The
    /// sibling of <c>VoucherEntryViewModel.ForAlter</c>, and it exists because the POS screen is the ONLY screen
    /// that keys a tender split: every field of <see cref="Voucher.PosTenders"/> is persisted, so a POS bill is
    /// fully recoverable, but only here.
    ///
    /// <para><b>Accepting is <see cref="AcceptAlteration"/>, not <see cref="Accept"/></b>, and for the same reason
    /// the accounting screen splits them: <c>Accept</c> mints a fresh <see cref="Guid"/> and Posts a SECOND bill
    /// beside the original, and only <c>LedgerService.Replace</c> preserves the id every outside link holds and
    /// carries the bank reconciliation dates forward.</para>
    /// </summary>
    public static PosAlterationOpen ForAlter(
        Company company,
        Guid voucherId,
        CompanyStorage storage,
        Action onSaved,
        Action onCancelled)
    {
        ArgumentNullException.ThrowIfNull(company);

        if (PosAlterationEligibility.RefusalFor(company, voucherId) is { } refusal)
            return PosAlterationOpen.Refused(refusal);

        // Both are non-null: RefusalFor returned null, which it only does after resolving each of them.
        var voucher = company.FindVoucher(voucherId)!;
        var type = company.FindVoucherType(voucher.TypeId)!;

        var entry = new PosBillingViewModel(company, type, storage, onSaved, onCancelled);
        return entry.RehydrateFrom(voucher) is { } lineRefusal
            ? PosAlterationOpen.Refused(lineRefusal)
            : PosAlterationOpen.Opened(entry);
    }

    /// <summary>
    /// Re-keys this freshly-constructed screen from <paramref name="voucher"/>. Returns <c>null</c> on success, or
    /// a named refusal when something posted cannot be re-keyed.
    ///
    /// <para><b>Every posted leg is CLASSIFIED and an unclassified one is REFUSED.</b> A POS bill's accounting
    /// legs are exactly three kinds — the tender debits, the single Cr Sales value leg, and the engine's Cr Output
    /// tax lines — so the inverse partitions them into those three and names anything left over. Silently ignoring
    /// a leg would drop it from the replacement, and the bill would still reconcile (Σ tenders is checked against
    /// the DEBIT total, which the tender rebuild reproduces), so nothing downstream would notice.</para>
    ///
    /// <para>🔴 <b>The tender MODE is inferred, not stored, and the inference is exact where it matters.</b> Multi
    /// mode is "at least one non-cash tender was posted", because that is the only shape multi mode can produce
    /// that single mode cannot: single mode posts exactly one Cash tender for the whole bill. A bill keyed in multi
    /// mode with the non-cash rows all left blank posts that same single Cash tender and re-opens as single — the
    /// SAME class of loss as the flat batch rehydration on the item grid, and the same answer: the rebuilt voucher
    /// is byte-identical, and only the operator's knowledge of which panel they used is gone.</para>
    /// </summary>
    private string? RehydrateFrom(Voucher voucher)
    {
        _alteringVoucherId = voucher.Id;
        _rehydrating = true;
        try
        {
            Date = voucher.Date;
            Narration = voucher.Narration ?? string.Empty;
            Title = $"{_type.Name} — POS Bill Alteration";

            // The party is informational on a B2C bill; a bill posted without one re-opens on the walk-in sentinel,
            // which is exactly what re-posts partyId: null.
            if (voucher.PartyId is { } partyId)
            {
                if (Parties.FirstOrDefault(o => o.Ledger?.Id == partyId) is not { } option)
                    return "This bill cannot be re-opened: the customer it was billed to is no longer in this "
                         + "company.";
                SelectedParty = option;
            }
            else
            {
                SelectedParty = Parties.FirstOrDefault(o => o.Ledger is null) ?? Parties.FirstOrDefault();
            }

            var valueLegs = voucher.Lines.Where(l => l.Side == DrCr.Credit && !l.HasGst).ToList();
            if (valueLegs.Count != 1)
                return valueLegs.Count == 0
                    ? "This bill cannot be re-opened: it carries no Sales value leg, so the item total has nothing "
                    + "to post against."
                    : $"This bill cannot be re-opened: it credits {valueLegs.Count} separate value legs, and the "
                    + "POS screen derives exactly one — so it can only have arrived from an import.";
            if (SalesLedgers.FirstOrDefault(l => l.Id == valueLegs[0].LedgerId) is not { } salesLedger)
                return "This bill cannot be re-opened: the Sales ledger its value leg posts to is no longer one "
                     + "this screen offers, so re-accepting would move the sale to a different ledger.";
            SelectedSalesLedger = salesLedger;

            foreach (var line in voucher.Lines)
            {
                if (line.HasGst) continue;                        // re-derived at accept from the item rows
                if (ReferenceEquals(line, valueLegs[0])) continue;
                if (line.Side != DrCr.Debit || !voucher.PosTenders.Any(t => t.LedgerId == line.LedgerId))
                    return "This bill cannot be re-opened: it carries a leg that is none of the three a POS bill "
                         + "builds (a tender debit, the Sales value leg or an engine tax line), so the screen "
                         + "cannot re-key it and re-accepting would drop it.";
            }

            // The header godown is pushed onto every item row when it changes, so it is set BEFORE the rows are
            // rehydrated — each row then states the godown it was actually posted against.
            if (Godowns.FirstOrDefault(g => g.Id == voucher.InventoryLines[0].GodownId) is { } godown)
                SelectedGodown = godown;

            Items.Clear();
            foreach (var posted in voucher.InventoryLines)
            {
                var row = AddItemLine();
                if (row.RehydrateFrom(posted) is { } lineRefusal)
                    return "This bill cannot be re-opened: " + lineRefusal;
            }
            AddItemLine();   // one blank trailing row, so the grid is ready to type into (as a fresh bill is)

            if (RehydrateTenders(voucher) is { } tenderRefusal) return tenderRefusal;
        }
        finally
        {
            _rehydrating = false;
        }

        Recalculate();
        return null;
    }

    /// <summary>
    /// Re-keys the four tender rows from <see cref="Voucher.PosTenders"/>. The Cash row's AMOUNT is deliberately
    /// not written: it is DERIVED (the residual in multi mode, the whole bill in single) and
    /// <see cref="Recalculate"/> stamps it — writing it here would be a second source for one figure. What is
    /// written is the cash TENDERED, which is keyed and from which the change follows.
    ///
    /// <para>🔴 <b>ONE ROW PER KIND — and a bill carrying TWO tenders of one kind never reaches here</b>, because
    /// <see cref="PosAlterationEligibility"/> refuses it by name at the door. This loop would write both into the
    /// one row their TYPE selects and the second would silently overwrite the first's amount, ledger and
    /// reference, after which <see cref="Recalculate"/> re-cuts the cash residual over the survivor and the bill
    /// foots again — Rs 600.00 measured out of a bank and into the drawer on a bill nobody touched. Do NOT "fix"
    /// that here by matching rows positionally or by growing the list: the four rows are the screen's shape in the
    /// AXAML and in <c>TryBuildTenders</c> as much as in this loop, so representing N tenders of one kind is a
    /// payment-panel design (an R6 row), not a rehydration change.</para>
    /// </summary>
    private string? RehydrateTenders(Voucher voucher)
    {
        // The mode inference — see RehydrateFrom's summary for why this clause is the exact one.
        IsMultiTender = voucher.PosTenders.Any(t => t.Type != PosTenderType.Cash);

        foreach (var tender in voucher.PosTenders)
        {
            var row = Tenders.FirstOrDefault(r => r.Type == tender.Type);
            if (row is null)
                return $"This bill cannot be re-opened: it carries a {tender.Type} tender, which this screen has "
                     + "no panel for.";

            if (row.LedgerChoices.FirstOrDefault(l => l.Id == tender.LedgerId) is not { } ledger)
                return $"This bill cannot be re-opened: the ledger its {row.Label} tender debits is no longer "
                     + "under the group that tender requires, so re-accepting would move the payment to another "
                     + "ledger.";
            row.SelectedLedger = ledger;

            if (tender.Type == PosTenderType.Cash)
            {
                row.CashTenderedText = ExactDecimalText((tender.Tendered ?? tender.Amount).Amount);
                continue;
            }

            row.AmountText = ExactDecimalText(tender.Amount.Amount);
            if (tender.CardNo is { } card) row.CardNo = card;
            if (tender.BankName is { } bank) row.BankName = bank;
            if (tender.ChequeNo is { } cheque) row.ChequeNo = cheque;
        }

        return null;
    }

    /// <summary>
    /// 🔴 <b>Accepts an ALTERATION of a posted POS bill</b> — the same build the fresh Accept runs, ending in
    /// <c>LedgerService.Replace</c> and never in <c>Post</c>.
    ///
    /// <para>🔴 <b>THE WHOLE-WINDOW LEDGER ROLLBACK the accounting screen's AcceptAlteration carries, and for the
    /// same measured reason.</b> The GST engine this path re-runs is IMPURE — it creates the Output tax ledgers it
    /// needs — and it runs BEFORE the shape-drift check can refuse, so a REFUSED alteration would otherwise leave
    /// new tax ledgers on the company, the in-memory canonical export no longer identical, and the additions then
    /// PERSISTED by the next unrelated save. A LEDGER SNAPSHOT rather than a per-engine undo, on purpose: it
    /// catches every ledger any engine on this path creates, including ones a future family adds.</para>
    /// </summary>
    public bool AcceptAlteration()
    {
        Message = null;

        var ledgersBefore = _company.Ledgers.Select(l => l.Id).ToHashSet();
        var committed = false;
        try
        {
            committed = AcceptAlterationCore();
            return committed;
        }
        finally
        {
            if (!committed)
                foreach (var created in _company.Ledgers.Where(l => !ledgersBefore.Contains(l.Id)).ToList())
                    _company.RemoveLedger(created);
        }
    }

    private bool AcceptAlterationCore()
    {
        if (!IsAltering)
        {
            Message = "This screen is keying a new bill, not altering a posted one — use Accept.";
            return false;
        }

        if (_company.FindVoucher(_alteringVoucherId) is not { } existing)
        {
            Message = "The bill being altered is no longer in this company's books — it may have been deleted "
                    + "meanwhile. Nothing was changed.";
            return false;
        }

        // Re-run eligibility: the screen may have been open while a master moved, and a refusal phrased in the
        // predicate's own words beats one phrased by the engine in terms the operator never saw.
        if (PosAlterationEligibility.RefusalFor(_company, _alteringVoucherId) is { } refusal)
        {
            Message = refusal;
            return false;
        }

        // 🔴 THE COMPENSATION-CESS MAGNITUDE IS PINNED SEPARATELY, AND FIRST — the shape signature below cannot see
        // it. A Cess leg's stamped rate is a SENTINEL 0 for a per-unit, an RSP-factor and any mixed ad-valorem
        // cess, so the whole cess axis is invisible to a ledger|side|head|rate comparison. Carried on this screen
        // in the SAME words and the SAME order as the accounting item invoice's accept path, deliberately: the
        // two doors consuming one guard, differently, is how the earlier asymmetries on this pair were built.
        //
        // 🔴 …AND ON THIS SCREEN IT RUNS **BEFORE THE BUILD**, WHICH IT DID NOT (census T0-16, measured). It reads
        // only the POSTED voucher and today's masters — never `built` — and running it after the build made it
        // STRUCTURALLY UNREACHABLE HERE: a cess master that moved moves the live bill total while the posted
        // TENDERS stay where they are, so BuildPosBill refused the reconciliation first and the operator was told
        // "Cash tendered is less than the cash payable" on a bill nobody touched — exactly the "a refusal in the
        // engine's words the operator never saw" failure the eligibility re-run above exists to avoid. The
        // ACCOUNTING door needs no hoist (its party leg is DERIVED, so a drift moves it instead of refusing) and
        // its order is left alone; this is the one place the two doors legitimately differ, and the cess sentence
        // is the same sentence in both.
        //
        // ⚠️ THE SHAPE AND MAGNITUDE PINS BELOW ARE STILL BEHIND THAT SAME TENDER REFUSAL, and the SHAPE one
        // genuinely needs `built`, so it cannot simply be hoisted the way this one was. Named here and reported
        // rather than fixed in passing: it is a different defect from T0-16 and wants its own slice and its own
        // tests, and the magnitude pin's documented position (AFTER the shape pin, so a drift that moved a head
        // or a rate is named by the shape sentence) must survive whatever closes it.
        //
        // 🔴 AND IT IS WRAPPED, because wiring the cess in put a THROW on this line that was not here before.
        // GstService.ResolveCess FAILS FAST on an RSP-factor cess whose item declares no Retail Sale Price — it
        // refuses to value a legitimately cess-bearing good at a silent ₹0 (ER-5's contract). That input is not
        // POSTABLE (the same fail-fast refuses it at Accept) but it is certainly CREATABLE under an already-posted
        // bill, which is exactly what an alteration re-prices against. BuildPosBill has carried its own catch for
        // this since the beginning; this call had none, so the counter would have gone down on Ctrl+A.
        GstService.InvoiceTax? reDerivedTax;
        try
        {
            reDerivedTax = ReDerivedTaxOnPostedRows(existing);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            Message = $"This bill cannot be re-priced under today's masters: {ex.Message} Alter has to compare the "
                    + "tax it would derive now against the tax stamped on the bill, and it cannot derive one. "
                    + "Correct the master, or raise a credit note and a fresh bill.";
            return false;
        }

        if (reDerivedTax is { } forCess
            && VoucherAlterationDerivedLegs.CessMagnitudeDriftRefusal(
                   VoucherAlterationDerivedLegs.StampedCessTotal(existing.Lines), forCess.TotalCess, "bill")
               is { } cessRefusal)
        {
            Message = cessRefusal;
            return false;
        }

        if (BuildPosBill() is not { } built) return false;

        // 🔴 THE SHAPE OF THE ENGINE'S TAX LEGS IS PINNED — the amounts are not. An alteration is ALLOWED to move a
        // tax figure (that is what amending a quantity does); what it must never do is silently restate the tax
        // because a MASTER moved under a bill nobody touched — GSTR-1 and GSTR-3B read the STAMPED figure.
        if (!VoucherAlterationDerivedLegs.TaxHeadSignature(existing.Lines)
                .SequenceEqual(VoucherAlterationDerivedLegs.TaxHeadSignature(built.EntryLines)))
        {
            Message = "The GST this bill would now be taxed under is not the shape it was posted with — the tax "
                    + "heads or their rates have moved since (a rate master, an item's HSN, or the place of "
                    + "supply). Alter re-computes the AMOUNT of a posted tax leg, never which legs there are, "
                    + "because the GST returns read the stamped figures. Correct the master, or raise a credit "
                    + "note and a fresh bill.";
            return false;
        }

        // 🔴 …AND THE MAGNITUDE OF THOSE LEGS IS PINNED TOO, on the two axes the shape above structurally cannot
        // see: an intra-state rate moved between an even bp and the odd one above it (the CGST/SGST legs carry
        // integratedBp / 2, an INTEGER division, so 1800 and 1801 both stamp 900), and a master flipped
        // Taxable → Exempt beside a same-rate sibling that keeps the leg alive. Both were MEASURED accepting a
        // narration-only alteration that moved the tax and the drawer. LAST, so a drift that DID move a head or a
        // rate is named by the shape sentence above rather than by this one. Held to the POSTED rows, so an
        // ordinary amendment is not seen here at all. Carried on this screen in the SAME words and the SAME order
        // as the accounting item invoice's accept path, deliberately: the two doors consuming one guard,
        // differently, is how the earlier asymmetries on this pair were built.
        if (reDerivedTax is { } forMagnitude
            && VoucherAlterationDerivedLegs.TaxMagnitudeDriftRefusal(
                   existing.Lines, forMagnitude.TaxLines, "bill")
               is { } magnitudeRefusal)
        {
            Message = magnitudeRefusal;
            return false;
        }

        var replacement = new Voucher(
            existing.Id,                 // the Guid is every outside link's only handle
            existing.TypeId,             // the preserved number belongs to THIS type's sequence
            Date,
            built.EntryLines,
            number: existing.Number,     // Replace accepts the voucher's own number by name
            narration: string.IsNullOrWhiteSpace(Narration) ? null : Narration.Trim(),
            partyId: built.PartyId,
            cancelled: existing.Cancelled,           // Cancel's verb, not Alter's — Replace refuses a change
            // 🔴 The provisional-state vector is CARRIED from the posted bill, not re-stated. This screen has no
            // Ctrl+L / Ctrl+T and no 'Applicable Upto' field at all, so there is nothing here to state it FROM —
            // and Replace refuses a change to any of the three by name, which would surface as an engine message
            // about fields the operator never saw. PosAlterationEligibility refuses an ApplicableUpto up front.
            optional: existing.Optional,
            postDated: existing.PostDated,
            applicableUpto: existing.ApplicableUpto,
            inventoryLines: built.InventoryLines,
            posTenders: built.Tenders,
            referenceNo: existing.ReferenceNo,       // never keyed on this screen; dropping it would lose an import's
            referenceDate: existing.ReferenceDate,
            isAccountingInvoice: existing.IsAccountingInvoice); // get-only, and Replace refuses a change

        IReadOnlyList<VoucherAlterationWarning> warnings;
        try
        {
            _service.Replace(existing.Id, replacement, out warnings);
        }
        catch (UnbalancedVoucherException)
        {
            Message = "The altered POS bill is out of balance. Not altered.";
            return false;
        }
        catch (Exception ex) when (ex is InvalidVoucherException or InvalidOperationException)
        {
            Message = $"Cannot alter: {ex.Message}";
            return false;
        }

        // A FAILED SAVE ROLLS THE SWAP BACK — the engine mutates the in-memory aggregate and the save happens
        // after it, so without this the books would hold the amended bill, the .db the original, and every later
        // save would carry the divergence.
        try
        {
            _storage.Save(_company);
        }
        catch (Exception ex) when (SaveFailure.IsReportable(ex))
        {
            try
            {
                _service.Replace(existing.Id, existing, out _);
                Message = $"Could not save the company: {ex.Message} The alteration was not kept — nothing was "
                        + "changed.";
            }
            catch (Exception rollbackFailure)
            {
                Message = $"Could not save the company: {ex.Message} Putting the original bill back ALSO failed "
                        + $"({rollbackFailure.Message}), so this company is now ahead of its file — close it "
                        + "without saving.";
            }
            return false;
        }

        SavedNumber = replacement.Number;
        Message = $"{_type.Name} No. {_company.FormatVoucherNumber(replacement)} altered."
                + (warnings.Count == 0 ? string.Empty : " " + string.Join(" ", warnings.Select(w => w.Message)));

        // 🔴 PRINT AFTER SAVE APPLIES TO THIS SAVE TOO (review finding C8 — MAJOR / fidelity). The one line
        // Accept() carries at :840 was absent here, so an amended bill raised no receipt at all: the customer's
        // only paper still named the ORIGINAL total while the book — and GSTR-1 — carried the amended one. Raised
        // BEFORE _onSaved(), exactly as Accept() does, because the shell's onSaved closure is what consumes the
        // pending receipt and opens its column.
        //
        // 🔴 FIDELITY (R7) — the SETTING is attested, the ALTERATION behaviour is an INFERENCE and is recorded as
        // one. The corpus instructs "Print voucher after saving - Set to `Yes'." for the POS type (Book
        // 664311548, and repeated in 696054070 / 703679456 / 719244897), and attests Ctrl+A as the save chord for
        // an alteration — but NO corpus page states print-after-save under alteration. The bridge is that the flag
        // is a property of the voucher TYPE, so it governs every save of that type. Ours, inferred, not attested.
        if (PrintAfterSave)
            PrintReceiptRequested?.Invoke(BuildReceipt(
                replacement, built.Tenders, built.Taxable, built.InvoiceTax, built.InterState, built.Change));

        _onSaved();
        return true;
    }

    /// <summary>Renders <paramref name="value"/> so that parsing it back yields the SAME decimal.</summary>
    private static string ExactDecimalText(decimal value) => value.ToString(CultureInfo.InvariantCulture);

    /// <summary>Builds the de-branded retail receipt DTO for the just-posted POS bill (RQ-44).</summary>
    private PosReceiptData BuildReceipt(Voucher posted, IReadOnlyList<PosTender> tenders, Money taxable,
        GstService.InvoiceTax? tax, bool interState, decimal change)
    {
        var items = new List<PosReceiptItem>();
        foreach (var l in Items.Where(l => l.IsComplete))
        {
            var rate = l.EffectiveRate ?? new Money(l.ParsedRate ?? 0m);
            items.Add(new PosReceiptItem
            {
                Description = l.SelectedItem!.Name,
                QuantityText = l.ParsedBilledQuantity.ToString("0.######", CultureInfo.InvariantCulture),
                RateText = IndianFormat.AmountAlways(rate.Amount),
                Value = Money.ForexBase(rate, l.ParsedBilledQuantity),
            });
        }

        var taxRows = new List<PosReceiptTaxRow>();
        if (tax is { } t)
            foreach (var grp in t.LineBreakdown.GroupBy(l => l.IntegratedBasisPoints).OrderBy(gr => gr.Key))
                taxRows.Add(new PosReceiptTaxRow
                {
                    RateLabel = (grp.Key / 100m).ToString("0.##", CultureInfo.InvariantCulture) + "%",
                    TaxableValue = grp.Aggregate(Money.Zero, (a, l) => a + l.TaxableValue),
                    Cgst = grp.Aggregate(Money.Zero, (a, l) => a + l.Cgst),
                    Sgst = grp.Aggregate(Money.Zero, (a, l) => a + l.Sgst),
                    Igst = grp.Aggregate(Money.Zero, (a, l) => a + l.Igst),
                });

        var receiptTenders = new List<PosReceiptTender>();
        foreach (var tn in tenders)
        {
            var label = tn.Type switch
            {
                PosTenderType.GiftVoucher => "Gift Voucher",
                PosTenderType.Card => "Credit/Debit Card",
                PosTenderType.Cheque => "Cheque/DD",
                _ => "Cash",
            };
            var reference = tn.Type switch
            {
                PosTenderType.Card when !string.IsNullOrWhiteSpace(tn.CardNo) => "Card No. " + tn.CardNo,
                PosTenderType.Cheque => $"{tn.BankName} Cheque No. {tn.ChequeNo}".Trim(),
                _ => string.Empty,
            };
            receiptTenders.Add(new PosReceiptTender { Label = label, Amount = tn.Amount, Reference = reference });
        }

        var cashTender = tenders.FirstOrDefault(x => x.Type == PosTenderType.Cash);

        // W0-1b — WHICH DOCUMENT IS THIS, IN LAW? Routed from the SAME predicate the voucher-screen invoice path uses
        // (CGST Act §31(3)(c): a composition dealer's supply, or a wholly exempt/nil-rated/non-GST one). A second copy
        // of the rule here is exactly how one dealer came to get two different answers from two screens, so the POS
        // path asks the projector rather than re-deciding. The declaration is Rule 5(1)(f)'s §10-only wording, gated
        // on the document kind first — precisely as ProjectInvoice stamps it into InvoicePrintData.TopDeclaration.
        var billOfSupply = VoucherPrintProjector.IsBillOfSupply(_company, posted);
        return new PosReceiptData
        {
            Title = _type.PosConfig?.DefaultTitle ?? "Retail Invoice",
            IsBillOfSupply = billOfSupply,
            TopDeclaration = billOfSupply
                ? VoucherPrintProjector.TopDeclarationFor(_company, posted)
                : string.Empty,
            StoreName = _company.Name,
            BillNumber = _company.FormatVoucherNumber(posted),
            DateText = ApexDate.Format(Date),
            Party = SelectedParty?.Ledger?.Name ?? "(cash)",
            IsInterState = interState,
            Items = items,
            TaxRows = taxRows,
            Tenders = receiptTenders,
            TotalTaxable = taxable,
            TotalCgst = tax?.TotalCgst ?? Money.Zero,
            TotalSgst = tax?.TotalSgst ?? Money.Zero,
            TotalIgst = tax?.TotalIgst ?? Money.Zero,
            // Gated on billOfSupply exactly as the head lines are — a §31(3)(c) document states no tax particular
            // of any kind, and a composition dealer may collect none (§10(4)). Mirrors ProjectInvoice's own
            // `billOfSupply ? Money.Zero : money.TotalCess`.
            TotalCess = billOfSupply ? Money.Zero : tax?.TotalCess ?? Money.Zero,
            CashTendered = cashTender?.Tendered ?? Money.Zero,
            Change = new Money(change),
            Message1 = _type.PosConfig?.Message1 ?? string.Empty,
            Message2 = _type.PosConfig?.Message2 ?? string.Empty,
            Declaration = _type.PosConfig?.Declaration ?? string.Empty,
        };
    }

    /// <summary>Esc / the Cancel button: discards the in-progress bill and returns to the Gateway. (Alt+X
    /// stopped reaching here in Phase 10.11 S3 — it now cancels a POSTED voucher from a report.)</summary>
    public void Cancel() => _onCancelled();

    // =============================================================== helpers

    /// <summary>Whether a ledger is a valid Sales value-leg target (under Sales Accounts) — mirrors the item-invoice gate.</summary>
    private bool IsSalesLegLedger(DomainLedger ledger)
    {
        var group = _company.FindGroup(ledger.GroupId);
        if (group is null) return false;
        return string.Equals(ClassificationRules.PrimaryAncestorOf(group, _company).Name,
            "Sales Accounts", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The first TYPED tender figure the INTEGER-paisa store could not carry, or <c>null</c> (W0-13 S2a).
    ///
    /// <para>Only the figures the operator actually types are read: the three non-cash amounts (multi mode only —
    /// in single mode those boxes are not in play) and the cash tendered. The Cash tender AMOUNT is the derived
    /// residual (bill − non-cash) and the change is (tendered − residual). Those two are storable by construction
    /// ONLY because <see cref="Accept"/> separately refuses an unstorable per-line rate and an unstorable BILL
    /// TOTAL before it gets here: the residual is bounded by the bill total and the change by the tendered cash,
    /// so once those three are storable and non-negative, so are these. The
    /// <see cref="Apex.Ledger.Domain.PosTender"/> constructor is the backstop if that ever stops being true — and
    /// it is a real one, because it tests <c>PaisaConversion.FitsPaisaStore</c> (magnitude AND exactness), not
    /// exactness alone.</para>
    /// </summary>
    private string? UnstorableTenderError()
    {
        if (Tenders.Count < 4) return null;
        if (IsMultiTender)
        {
            if (Gift.AmountError is { } giftError) return giftError;
            if (Card.AmountError is { } cardError) return cardError;
            if (Cheque.AmountError is { } chequeError) return chequeError;
        }
        return Cash.CashTenderedError;
    }

    /// <summary>Parses a money string (invariant, allows thousands/sign); 0 on failure.</summary>
    public static decimal ParseMoney(string? text) =>
        decimal.TryParse((text ?? string.Empty).Trim(),
            NumberStyles.AllowDecimalPoint | NumberStyles.AllowThousands | NumberStyles.AllowLeadingSign,
            CultureInfo.InvariantCulture, out var v) ? v : 0m;
}
