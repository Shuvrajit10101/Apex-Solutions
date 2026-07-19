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

    /// <summary>Computes GST over the complete item lines (Output direction) — identical to a normal sales invoice (RQ-43).</summary>
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
            var res = _gst.ResolveRate(l.SelectedItem, SelectedSalesLedger);
            if (GstService.IsUnresolved(res))
                return new PosGst(EmptyTax(), interState, l.SelectedItem);
            if (!res.IsTaxable) continue;
            taxable.Add(new GstService.TaxableLine(lineValue, res.RateBasisPoints));
        }
        return new PosGst(_gst.ComputeInvoiceTax(taxable, interState, GstTaxDirection.Output), interState, null);
    }

    private static GstService.InvoiceTax EmptyTax() => new()
    {
        TaxLines = Array.Empty<EntryLine>(),
        LineBreakdown = Array.Empty<GstService.LineTax>(),
    };

    /// <summary>The current bill total = Σ item value + Σ GST (the amount the tenders must reconcile to).</summary>
    private decimal BillTotal(out decimal taxable, out decimal cgst, out decimal sgst, out decimal igst)
    {
        taxable = ItemsTotal();
        var gst = ComputeGst();
        cgst = gst?.Tax.TotalCgst.Amount ?? 0m;
        sgst = gst?.Tax.TotalSgst.Amount ?? 0m;
        igst = gst?.Tax.TotalIgst.Amount ?? 0m;
        return taxable + cgst + sgst + igst;
    }

    /// <summary>
    /// Recomputes the live totals, auto-fills the Cash tender (residual in multi mode, bill total in single mode),
    /// computes the informational change, reconciles Σ tenders vs the bill and refreshes the Accept gate. Re-entrancy
    /// guarded — auto-filling the Cash amount raises change notifications that would otherwise re-enter this.
    /// </summary>
    public void Recalculate()
    {
        if (_recomputing) return;
        // Guard against the change-notifications the constructor's header assignments (SelectedParty / SalesLedger /
        // Godown) raise BEFORE the four tender rows are added — the ctor calls Recalculate() once at the end, when the
        // Tenders list is fully populated. Without this the very first party/ledger/godown default crashes the screen.
        if (Tenders.Count < 4) return;
        _recomputing = true;
        try
        {
            var bill = BillTotal(out var taxable, out var cgst, out var sgst, out var igst);
            ItemsTotalText = IndianFormat.AmountAlways(taxable);
            GstCgstText = IndianFormat.AmountAlways(cgst);
            GstSgstText = IndianFormat.AmountAlways(sgst);
            GstIgstText = IndianFormat.AmountAlways(igst);
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
                 && change >= 0m;

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
    /// Ctrl+A accept: assembles the POS Sales voucher — item lines → outward inventory lines; Cr Sales(taxable) +
    /// Cr Output GST (identical to a normal sale); the customer Dr replaced by the tender debits — pre-validates
    /// for friendly messages, posts through <see cref="LedgerService.Post"/> (which enforces the tender grouping +
    /// reconciliation and no-negative-stock; nothing persists on failure) and saves. When the POS config's
    /// print-after-save is set it raises <see cref="PrintReceiptRequested"/> with the retail receipt.
    /// </summary>
    public bool Accept()
    {
        Message = null;
        Recalculate();

        if (SelectedSalesLedger is not { } salesLedger)
        {
            Message = "No Sales ledger is configured to post the value leg to.";
            return false;
        }
        if (Items.Any(l => !l.IsBlank && !l.IsComplete))
        {
            Message = "Every item line needs a stock item, a godown, a positive quantity and a positive rate.";
            return false;
        }
        var complete = Items.Where(l => l.IsComplete).ToList();
        if (complete.Count == 0)
        {
            Message = "Enter at least one item line before accepting.";
            return false;
        }

        // Outward inventory lines + taxable value (the pairing invariant holds by construction: Σ item == Cr Sales).
        var inventoryLines = new List<VoucherInventoryLine>(complete.Count);
        var taxable = Money.Zero;
        foreach (var l in complete)
        {
            if (l.ParsedRate is not { } rate || rate <= 0m)
            {
                Message = $"Item '{l.SelectedItem!.Name}' needs a rate greater than zero.";
                return false;
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
            { Message = $"Cannot accept: {ex.Message}"; return false; }
            if (gst.HasUnresolved)
            {
                Message = $"Item '{gst.Unresolved!.Name}' is taxable but no GST rate is set on the item, the Sales " +
                          "ledger, or the company. Set a rate before accepting.";
                return false;
            }
            taxLines.AddRange(gst.Tax.TaxLines);
            invoiceTax = gst.Tax;
            interState = gst.InterState;
            billTotal = new Money(taxable.Amount + gst.Tax.TotalTax.Amount);
        }

        // Build the tender records (Cash posts the residual/bill — never the tendered; change is informational).
        if (!TryBuildTenders(billTotal.Amount, out var tenders, out var change))
            return false; // Message already set

        var entryLines = new List<EntryLine>();
        entryLines.AddRange(PosTenderService.BuildTenderDebitLines(tenders));
        entryLines.Add(new EntryLine(salesLedger.Id, taxable, DrCr.Credit));
        entryLines.AddRange(taxLines);

        var party = SelectedParty?.Ledger;
        var voucher = new Voucher(
            Guid.NewGuid(), _type.Id, Date, entryLines,
            number: 0,
            narration: string.IsNullOrWhiteSpace(Narration) ? null : Narration.Trim(),
            partyId: party?.Id,
            inventoryLines: inventoryLines,
            posTenders: tenders);

        try
        {
            var posted = _service.Post(voucher);
            _storage.Save(_company);
            SavedNumber = posted.Number;
            Message = $"{_type.Name} No. {posted.Number} accepted.";

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

        return new PosReceiptData
        {
            Title = _type.PosConfig?.DefaultTitle ?? "Retail Invoice",
            StoreName = _company.Name,
            BillNumber = posted.Number.ToString(CultureInfo.InvariantCulture),
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
            CashTendered = cashTender?.Tendered ?? Money.Zero,
            Change = new Money(change),
            Message1 = _type.PosConfig?.Message1 ?? string.Empty,
            Message2 = _type.PosConfig?.Message2 ?? string.Empty,
            Declaration = _type.PosConfig?.Declaration ?? string.Empty,
        };
    }

    /// <summary>Esc / Alt+X cancel: discards the in-progress bill and returns to the Gateway.</summary>
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

    /// <summary>Parses a money string (invariant, allows thousands/sign); 0 on failure.</summary>
    public static decimal ParseMoney(string? text) =>
        decimal.TryParse((text ?? string.Empty).Trim(),
            NumberStyles.AllowDecimalPoint | NumberStyles.AllowThousands | NumberStyles.AllowLeadingSign,
            CultureInfo.InvariantCulture, out var v) ? v : 0m;
}
