using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Reports;
using Apex.Ledger.Services;
using Apex.Desktop.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using DomainLedger = Apex.Ledger.Domain.Ledger;

namespace Apex.Desktop.ViewModels;

/// <summary>
/// The reusable voucher-entry screen — one view model for Contra (F4), Payment (F5),
/// Receipt (F6), Journal (F7), Sales (F8) and Purchase (F9). It owns the header (voucher-type
/// name, auto voucher number, date), a grid of Dr/Cr particulars lines, a live balance indicator
/// (Σ Dr vs Σ Cr — accept is blocked while unbalanced), and a narration field.
///
/// <para>MVVM boundary: this class references the engine (<see cref="LedgerService"/>) and the
/// persistence via <see cref="CompanyStorage"/>, but no Avalonia/UI types — so it is unit-testable
/// headlessly. On <see cref="Accept"/> it builds a <see cref="Voucher"/>, posts it through
/// <see cref="LedgerService.Post"/> (which rejects an unbalanced/invalid voucher), then persists the
/// whole company aggregate to its <c>.db</c> via <see cref="CompanyStorage.Save"/>.</para>
/// </summary>
public sealed partial class VoucherEntryViewModel : ViewModelBase
{
    private readonly Company _company;
    private readonly VoucherType _type;
    private readonly LedgerService _service;
    private readonly GstService _gst;
    private readonly CompanyStorage _storage;
    private readonly Action _onSaved;
    private readonly Action _onCancelled;

    /// <summary>The voucher type this screen is entering (Payment, Receipt, …).</summary>
    public VoucherType Type => _type;

    /// <summary>Voucher-type display name for the header, e.g. "Payment".</summary>
    public string TypeName => _type.Name;

    /// <summary>The company's ledgers each line's picker chooses from.</summary>
    public IReadOnlyList<DomainLedger> Ledgers { get; }

    /// <summary>The editable Dr/Cr particulars lines.</summary>
    public ObservableCollection<VoucherLineViewModel> Lines { get; } = new();

    // =============================================================== item-invoice mode (catalog §10; slice 3.4c)

    /// <summary>
    /// True only for a Purchase or Sales voucher — the two natures that can be entered "as invoice"
    /// (item-invoice mode). For every other voucher type item-invoice mode is unavailable (Ctrl+I is a
    /// no-op and the inventory panel never shows), so those screens behave exactly as before.
    /// </summary>
    public bool CanBeItemInvoice =>
        _type.BaseType is VoucherBaseType.Purchase or VoucherBaseType.Sales;

    /// <summary>
    /// Ctrl+I — whether this Purchase/Sales voucher is being entered <b>as an item invoice</b> (catalog §10):
    /// the user enters a party + inventory lines (Stock Item / Godown / Qty / Rate / Batch) and the VM
    /// auto-derives the two balancing accounting legs, so the pairing invariant always holds without any
    /// hand-balancing. When off, the plain Dr/Cr grid is used and the voucher behaves exactly as before.
    /// Only ever true when <see cref="CanBeItemInvoice"/>.
    /// </summary>
    [ObservableProperty] private bool _isItemInvoice;

    /// <summary>True for a Purchase item-invoice (stock inward; party = supplier; Dr Purchases / Cr Supplier).</summary>
    public bool IsPurchaseInvoice => _type.BaseType == VoucherBaseType.Purchase;

    /// <summary>The party-field caption for the current nature ("Supplier" for Purchase, "Customer" for Sales).</summary>
    public string PartyCaption => IsPurchaseInvoice ? "Supplier" : "Customer";

    /// <summary>The accounting-leg (Purchases/Sales) caption for the derived-summary line.</summary>
    public string StockLedgerCaption => IsPurchaseInvoice ? "Purchases" : "Sales";

    /// <summary>The stock items the item-invoice line pickers choose from.</summary>
    public IReadOnlyList<StockItem> StockItems { get; }

    /// <summary>The godowns the item-invoice line pickers choose from.</summary>
    public IReadOnlyList<Godown> Godowns { get; }

    /// <summary>The party (supplier/customer) choices — "(none)" first, then every ledger.</summary>
    public ObservableCollection<PartyOption> Parties { get; } = new();

    /// <summary>The chosen party (supplier for a Purchase, customer for a Sales); null ⇒ not yet picked.</summary>
    [ObservableProperty] private PartyOption? _selectedParty;

    /// <summary>The Purchases-/Sales-accounts ledger the value leg posts to (auto-defaulted, user-overridable).</summary>
    public ObservableCollection<DomainLedger> StockLedgers { get; } = new();

    /// <summary>The chosen Purchases (for Purchase) / Sales (for Sales) accounting ledger the value leg posts to.</summary>
    [ObservableProperty] private DomainLedger? _selectedStockLedger;

    /// <summary>The editable item-invoice inventory lines (Stock Item / Godown / Qty / Rate / Batch).</summary>
    public ObservableCollection<InventoryVoucherLineViewModel> InventoryLines { get; } = new();

    /// <summary>Running Σ of the item-line values (each qty × rate) — the amount the two derived legs carry.</summary>
    [ObservableProperty] private string _itemsTotalText = "0.00";

    /// <summary>The derived-Dr/Cr summary line shown under the items total (e.g. "Dr Purchases 5,000.00 · Cr Acme 5,000.00").</summary>
    [ObservableProperty] private string _derivedSummary = string.Empty;

    // =============================================================== GST on the item-invoice (catalog §12; slice 4e)

    /// <summary>
    /// True when this Purchase/Sales <b>item invoice</b> is GST-aware — i.e. item-invoice mode is on AND the
    /// company has GST enabled (<see cref="Company.GstEnabled"/>). Only then does the screen resolve each line's
    /// GST rate, split intra CGST/SGST vs inter IGST, DISPLAY the tax + party total, and POST the additive tax
    /// lines. On a GST-off company this stays <c>false</c> and the invoice behaves exactly as the Phase-3
    /// item-invoice (two accounting legs, no tax).
    /// </summary>
    public bool IsGstInvoice => IsItemInvoice && _company.GstEnabled;

    /// <summary>The invoice CGST total (paisa-exact display); "0.00" when off/inter-state/exempt.</summary>
    [ObservableProperty] private string _gstCgstText = "0.00";

    /// <summary>The invoice SGST total (paisa-exact display); "0.00" when off/inter-state/exempt.</summary>
    [ObservableProperty] private string _gstSgstText = "0.00";

    /// <summary>The invoice IGST total (paisa-exact display); "0.00" when off/intra-state/exempt.</summary>
    [ObservableProperty] private string _gstIgstText = "0.00";

    /// <summary>The invoice party total = Σ taxable + Σ additional cost + Σ tax (what the supplier is owed).</summary>
    [ObservableProperty] private string _partyTotalText = "0.00";

    // =============================================================== additional cost of purchase (Book pp.133–141; catalog §11; slice 6.3)

    /// <summary>
    /// "Track Additional Costs for Purchases" (Book pp.133–141; Phase 6 slice 3 RQ-16..RQ-20) — the voucher-type
    /// flag proxied for the voucher-type-editor checkbox on the Purchase entry screen. Reading returns the live
    /// <see cref="VoucherType.TrackAdditionalCosts"/>; setting it mutates the (persisted) type and saves the
    /// company, then refreshes the additional-cost gating. Only meaningful on a Purchase type.
    /// </summary>
    public bool TrackAdditionalCosts
    {
        get => _type.TrackAdditionalCosts;
        set
        {
            if (_type.TrackAdditionalCosts == value) return;
            _type.TrackAdditionalCosts = value;
            _storage.Save(_company);
            OnPropertyChanged();
            OnPropertyChanged(nameof(ShowAdditionalCosts));
            RecalculateItemInvoice();
        }
    }

    /// <summary>True iff the voucher-type-editor "Track Additional Costs" checkbox is shown — only on a Purchase
    /// that can be entered as an item invoice (never on a Sales or a non-invoiceable type).</summary>
    public bool CanTrackAdditionalCosts => IsPurchaseInvoice && CanBeItemInvoice;

    // =============================================================== Actual vs Billed qty (Book pp.145–147; slice 6.4)

    /// <summary>
    /// "Use separate Actual and Billed Quantity columns in invoices" (Book pp.145–147; Phase 6 slice 4 RQ-22) —
    /// the company/F11 flag proxied for the checkbox on the Sales/Purchase item-invoice screen. Reading returns
    /// the live <see cref="Company.UseSeparateActualBilledQuantity"/>; setting it mutates the (persisted) company
    /// and saves it, then re-gates each item line's Billed column + recomputes the totals. Off ⇒ one Qty column
    /// and Billed ≡ Actual (byte-identical to today, ER-13).
    /// </summary>
    public bool UseSeparateActualBilledQuantity
    {
        get => _company.UseSeparateActualBilledQuantity;
        set
        {
            if (_company.UseSeparateActualBilledQuantity == value) return;
            _company.UseSeparateActualBilledQuantity = value;
            _storage.Save(_company);
            OnPropertyChanged();
            OnPropertyChanged(nameof(ShowActualBilledColumns));
            OnPropertyChanged(nameof(QuantityHeader));
            SyncActualBilledOnLines();
            RecalculateItemInvoice();
        }
    }

    /// <summary>True iff the "Use separate Actual &amp; Billed Qty" checkbox is shown — a Sales/Purchase that can be
    /// entered as an item invoice (Note 2: Actual/Billed is Sales/Purchase-only). Never on a non-invoiceable type.</summary>
    public bool CanUseSeparateActualBilled => CanBeItemInvoice;

    /// <summary>True when the Billed-quantity column band + the "Qty (Actual)" relabel are shown — the company flag
    /// is on AND this is a Sales/Purchase item invoice. Drives the header column's visibility (per-line visibility
    /// is <see cref="InventoryVoucherLineViewModel.ShowActualBilled"/>).</summary>
    public bool ShowActualBilledColumns =>
        IsItemInvoice && CanBeItemInvoice && _company.UseSeparateActualBilledQuantity;

    /// <summary>The Quantity column header: "Qty (Actual)" when the A/B split is shown, plain "Quantity" otherwise
    /// (brand-neutral — never any "Tally" text).</summary>
    public string QuantityHeader => ShowActualBilledColumns ? "Qty (Actual)" : "Quantity";

    // =============================================================== zero-valued transactions (Book pp.142–143; slice 6.4)

    /// <summary>
    /// "Allow zero-valued transactions" (Book pp.142–143; Phase 6 slice 4 RQ-21) — the voucher-type flag proxied
    /// for the checkbox on the Sales/Purchase item-invoice screen (mirrors <see cref="TrackAdditionalCosts"/>).
    /// Reading returns the live <see cref="VoucherType.AllowZeroValuedTransactions"/>; setting it mutates the
    /// (persisted) type and saves the company, then re-gates. When on, an item line entered <i>free</i> (Rate/Value
    /// = ₹0) is accepted — it moves stock but posts ₹0 to accounts and ₹0 to GST. Off ⇒ a fat-finger ₹0 line is
    /// still rejected (ER-13). Only surfaced on a Sales/Purchase base type.
    /// </summary>
    public bool AllowZeroValued
    {
        get => _type.AllowZeroValuedTransactions;
        set
        {
            if (_type.AllowZeroValuedTransactions == value) return;
            _type.AllowZeroValuedTransactions = value;
            _storage.Save(_company);
            OnPropertyChanged();
            RecalculateItemInvoice();
        }
    }

    /// <summary>True iff the "Allow zero-valued transactions" checkbox is shown — only a Sales/Purchase that can be
    /// entered as an item invoice (RQ-21: Sales/Purchase-only). Never on a non-invoiceable type.</summary>
    public bool CanAllowZeroValued => CanBeItemInvoice;

    /// <summary>Pushes the company's Actual/Billed flag to every item line so its Billed column shows/hides in sync.</summary>
    private void SyncActualBilledOnLines()
    {
        var on = CanBeItemInvoice && _company.UseSeparateActualBilledQuantity;
        foreach (var l in InventoryLines) l.ShowActualBilled = on;
    }

    /// <summary>
    /// True when the additional-cost entry area is shown (Book pp.133–141; RQ-16): a Purchase entered as an item
    /// invoice whose voucher type has <see cref="VoucherType.TrackAdditionalCosts"/> on. Off ⇒ the area is hidden
    /// and no additional cost loads any stock rate (a plain freight line stays purely P&amp;L, RQ-19 / ER-13).
    /// </summary>
    public bool ShowAdditionalCosts => IsItemInvoice && IsPurchaseInvoice && _type.TrackAdditionalCosts;

    /// <summary>The additional-cost ledgers the row pickers choose from — ledgers whose
    /// <see cref="Ledger.MethodOfAppropriation"/> is non-null (a plain Direct-Expenses ledger stays out, RQ-19).</summary>
    public IReadOnlyList<DomainLedger> AdditionalCostLedgers { get; }

    /// <summary>The repeatable additional-cost entry rows (ledger + amount); always one blank trailing row.</summary>
    public ObservableCollection<AdditionalCostRowViewModel> AdditionalCosts { get; } = new();

    /// <summary>The running Σ of the complete additional-cost rows (paisa-exact display).</summary>
    [ObservableProperty] private string _additionalCostTotalText = "0.00";

    [ObservableProperty] private string _title = string.Empty;
    [ObservableProperty] private DateOnly _date;
    [ObservableProperty] private int _voucherNumber;
    [ObservableProperty] private string _narration = string.Empty;

    /// <summary>
    /// Ctrl+T — marks this voucher <b>post-dated</b> (catalog §8, post-dated cheques): the posted voucher
    /// is excluded from current balances until its date is reached (<see cref="Voucher.PostDated"/> ⇒ the
    /// engine's CountsAsOf skips it while its date is in the future). Toggled on the header; the built
    /// voucher carries the flag.
    /// </summary>
    [ObservableProperty] private bool _isPostDated;

    /// <summary>
    /// Ctrl+L — marks this voucher <b>Optional</b> (catalog §7): a provisional entry that stays out of the
    /// real books (<see cref="Voucher.Optional"/> ⇒ the engine's CountsAsOf skips it) until it is
    /// regularised, and is surfaced only through a Scenario that includes its voucher type. Toggled on the
    /// header alongside Post-Dated; the built voucher carries the flag.
    /// </summary>
    [ObservableProperty] private bool _isOptional;

    /// <summary>
    /// True only for a <b>Reversing Journal</b> (catalog §7): the voucher is provisional (never in the real
    /// books) and carries an <b>Applicable Upto</b> date (<see cref="ApplicableUptoText"/>) — under a
    /// scenario it counts only while the as-of date is ≤ that date, then it reverses out. Drives the
    /// "Applicable Upto" field's visibility on the header.
    /// </summary>
    public bool IsReversing => _type.BaseType == VoucherBaseType.ReversingJournal;

    /// <summary>
    /// True for any provisional voucher type (<b>Memorandum</b> / <b>Reversing Journal</b>): it never
    /// affects the real books, so the header shows a "provisional" hint and the Optional toggle is hidden
    /// (it is already off-books by nature).
    /// </summary>
    public bool IsProvisionalType =>
        _type.BaseType is VoucherBaseType.Memorandum or VoucherBaseType.ReversingJournal;

    /// <summary>
    /// The Reversing Journal's "Applicable Upto" date as editable text (dd-MMM-yyyy). Defaults to the
    /// financial-year end; parsed on <see cref="Accept"/>. Ignored for every non-reversing voucher.
    /// </summary>
    [ObservableProperty] private string _applicableUptoText = string.Empty;

    /// <summary>
    /// The date as editable text (dd-MMM-yyyy) for the header TextBox. Setting it with a parseable
    /// value updates <see cref="Date"/>; an unparseable value is kept as-typed and left for Accept
    /// to surface (the engine also rejects a date before books-begin).
    /// </summary>
    public string DateText
    {
        get => Date.ToString("dd-MMM-yyyy", System.Globalization.CultureInfo.InvariantCulture);
        set
        {
            if (DateOnly.TryParse(value, System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.None, out var parsed))
                Date = parsed;
        }
    }

    // Live totals / balance indicator.
    [ObservableProperty] private string _totalDebitText = "0.00";
    [ObservableProperty] private string _totalCreditText = "0.00";
    [ObservableProperty] private string _differenceText = "Balanced";
    [ObservableProperty] private bool _isBalanced;
    [ObservableProperty] private bool _canAccept;

    /// <summary>Error/status line surfaced under the grid (rejected posting, blank rows, …).</summary>
    [ObservableProperty] private string? _message;

    /// <summary>The number assigned to the voucher once accepted (0 until then).</summary>
    [ObservableProperty] private int _savedNumber;

    public VoucherEntryViewModel(
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
        Ledgers = company.Ledgers;

        // Item-invoice masters (only meaningful on a Purchase/Sales, but always populated so the toggle is cheap).
        StockItems = company.StockItems;
        Godowns = company.Godowns;

        // Additional-cost ledgers (Book pp.133–141): the Direct-Expenses ledgers marked as additional-cost
        // ledgers (a non-null Method of Appropriation). A plain Direct-Expenses ledger stays out (RQ-19).
        AdditionalCostLedgers = company.Ledgers
            .Where(l => l.IsAdditionalCostLedger)
            .OrderBy(l => l.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        BuildItemInvoicePickers();
        AddAdditionalCostRow(); // one blank trailing row ready to type into

        // Default date: last voucher date, else books-begin (never before books, which Post rejects).
        var last = company.Vouchers.Count == 0
            ? (DateOnly?)null
            : company.Vouchers.Max(v => v.Date);
        Date = date ?? last ?? company.BooksBeginFrom;

        VoucherNumber = _service.NextNumber(type.Id);
        Title = $"{type.Name} Voucher";

        // A Reversing Journal defaults its "Applicable Upto" to the financial-year end.
        _applicableUptoText = company.FinancialYearStart.AddYears(1).AddDays(-1)
            .ToString("dd-MMM-yyyy", System.Globalization.CultureInfo.InvariantCulture);

        // Seed two starter lines: the first Dr, the second Cr (opens with a By/To pair).
        AddLine(DrCr.Debit);
        AddLine(DrCr.Credit);
        Recalculate();
    }

    partial void OnDateChanged(DateOnly value)
    {
        OnPropertyChanged(nameof(DateText));
        // Push the new date to every line so a forex line can default its rate from the rate in force.
        foreach (var line in Lines) line.SetVoucherDate(value);
    }

    /// <summary>Adds a blank particulars line (default side supplied); recomputes the balance.</summary>
    public VoucherLineViewModel AddLine(DrCr side = DrCr.Debit)
    {
        var line = new VoucherLineViewModel(Ledgers, Recalculate, _company, side);
        line.SetVoucherDate(Date);
        Lines.Add(line);
        return line;
    }

    /// <summary>Removes a line (keeping a minimum of two); recomputes the balance.</summary>
    public void RemoveLine(VoucherLineViewModel line)
    {
        if (Lines.Count <= 2) return;
        Lines.Remove(line);
        Recalculate();
    }

    /// <summary>Adds a bill-wise allocation row to a line (the sub-panel "+ Add bill" button).</summary>
    public void AddBillAllocation(VoucherLineViewModel line)
    {
        line.AddBillAllocation();
        Recalculate();
    }

    /// <summary>Adds a cost-allocation row to a line (the sub-panel "+ Add centre" button).</summary>
    public void AddCostAllocation(VoucherLineViewModel line)
    {
        line.AddCostAllocation();
        Recalculate();
    }

    /// <summary>Recomputes Σ Dr, Σ Cr, the difference indicator, and whether Accept is allowed.</summary>
    public void Recalculate()
    {
        // In item-invoice mode the plain Dr/Cr grid is not the Accept gate — the item-invoice indicators are
        // (a change to the always-present blank starter lines must not clobber that gate).
        if (IsItemInvoice) { RecalculateItemInvoice(); return; }

        decimal dr = 0m, cr = 0m;
        foreach (var l in Lines)
        {
            if (l.Side == DrCr.Debit) dr += l.ParsedAmount;
            else cr += l.ParsedAmount;
        }

        TotalDebitText = IndianFormat.AmountAlways(dr);
        TotalCreditText = IndianFormat.AmountAlways(cr);

        var diff = dr - cr;
        IsBalanced = diff == 0m && dr > 0m;

        if (diff == 0m)
            DifferenceText = dr > 0m ? "Balanced" : "Nil";
        else if (diff > 0m)
            DifferenceText = $"Debit short/Credit excess by {IndianFormat.AmountAlways(Math.Abs(diff))}";
        else
            DifferenceText = $"Credit short/Debit excess by {IndianFormat.AmountAlways(Math.Abs(diff))}";

        // Accept requires: at least two complete lines, no half-filled row, balanced (>0), and — for any
        // bill-wise / cost-applicable line — a valid split (allocations sum to the line amount; cost is
        // optional but, once touched, must sum exactly).
        var completeLines = Lines.Count(l => l.IsComplete);
        var hasHalfFilledRow = Lines.Any(l => !l.IsBlank && !l.IsComplete);
        var billSplitsOk = Lines.Where(l => l.IsComplete).All(l => l.BillSplitOk);
        var costSplitsOk = Lines.Where(l => l.IsComplete).All(l => l.CostSplitOk);
        CanAccept = IsBalanced && completeLines >= 2 && !hasHalfFilledRow && billSplitsOk && costSplitsOk;
    }

    /// <summary>
    /// Ctrl+A accept: builds the voucher from the non-blank lines, posts it (engine rejects an
    /// unbalanced/invalid voucher — nothing persists on failure), then saves the company to its
    /// <c>.db</c>. On success surfaces the assigned number and returns to the Gateway.
    /// </summary>
    public bool Accept()
    {
        Message = null;

        // Item-invoice mode routes to its own accept path (auto-derived legs + inventory lines).
        if (IsItemInvoice) return AcceptItemInvoice();

        // Reject half-filled rows up front with a clear message (before touching the engine).
        if (Lines.Any(l => !l.IsBlank && !l.IsComplete))
        {
            Message = "Every entered line needs a ledger and a positive amount.";
            return false;
        }

        // Reject an invalid bill-wise split up front (allocations must sum to the line amount).
        var badBill = Lines.FirstOrDefault(l => l.IsComplete && !l.BillSplitOk);
        if (badBill is not null)
        {
            Message = $"Bill-wise allocations for '{badBill.SelectedLedger!.Name}' must sum to the line amount " +
                      $"({IndianFormat.AmountAlways(badBill.ParsedAmount)}).";
            return false;
        }

        // Reject an invalid cost split up front (once touched, allocations must sum to the line amount).
        var badCost = Lines.FirstOrDefault(l => l.IsComplete && !l.CostSplitOk);
        if (badCost is not null)
        {
            Message = $"Cost allocations for '{badCost.SelectedLedger!.Name}' must sum to the line amount " +
                      $"({IndianFormat.AmountAlways(badCost.ParsedAmount)}).";
            return false;
        }

        // Reject a half-filled forex pair up front (a forex line needs both a forex amount and a rate).
        var badForex = Lines.FirstOrDefault(l => l.SelectedLedger is not null && l.IsForexLine && !l.ForexOk);
        if (badForex is not null)
        {
            Message = $"Forex details for '{badForex.SelectedLedger!.Name}' need both an amount in " +
                      $"{badForex.ForexCurrencyCode} and a rate of exchange.";
            return false;
        }

        var entryLines = Lines
            .Where(l => l.IsComplete)
            .Select(l =>
            {
                var billAllocs = l.ToBillAllocations();
                var costAllocs = l.ToCostAllocations();
                var bankAlloc = l.ToBankAllocation();
                var forex = l.ToForexInfo();
                return new EntryLine(
                    l.SelectedLedger!.Id, new Money(l.ParsedAmount), l.Side,
                    billAllocs.Count > 0 ? billAllocs : null,
                    costAllocs.Count > 0 ? costAllocs : null,
                    bankAlloc,
                    forex);
            })
            .ToList();

        if (entryLines.Count < 2)
        {
            Message = "A voucher needs at least two lines.";
            return false;
        }

        // A Reversing Journal must carry a valid "Applicable Upto" date (on/after the voucher date).
        DateOnly? applicableUpto = null;
        if (IsReversing)
        {
            if (!DateOnly.TryParseExact((ApplicableUptoText ?? string.Empty).Trim(), "dd-MMM-yyyy",
                    System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.None, out var upto))
            {
                Message = "Applicable Upto must be dd-MMM-yyyy (e.g. 30-Apr-2024).";
                return false;
            }
            if (upto < Date)
            {
                Message = "Applicable Upto must be on or after the voucher date.";
                return false;
            }
            applicableUpto = upto;
        }

        var voucher = new Voucher(
            Guid.NewGuid(),
            _type.Id,
            Date,
            entryLines,
            number: 0, // let the engine assign the automatic number
            narration: string.IsNullOrWhiteSpace(Narration) ? null : Narration.Trim(),
            // Provisional types (Memorandum / Reversing Journal) are off-books by nature; the Optional
            // toggle only applies to real voucher types.
            optional: !IsProvisionalType && IsOptional,
            postDated: IsPostDated,
            applicableUpto: applicableUpto);

        try
        {
            var posted = _service.Post(voucher); // throws on unbalanced/invalid — never persisted
            _storage.Save(_company);             // persist the whole aggregate to the .db
            SavedNumber = posted.Number;
            Message = $"{_type.Name} No. {posted.Number} accepted.";
            _onSaved();
            return true;
        }
        catch (UnbalancedVoucherException)
        {
            Message = $"Voucher is out of balance (Dr {TotalDebitText} ≠ Cr {TotalCreditText}). Not saved.";
            return false;
        }
        catch (InvalidVoucherException ex)
        {
            Message = $"Cannot accept: {ex.Message}";
            return false;
        }
    }

    /// <summary>Ctrl+T — toggles the post-dated flag for this voucher (post-dated cheque handling).</summary>
    public void TogglePostDated() => IsPostDated = !IsPostDated;

    /// <summary>
    /// Ctrl+L — toggles the Optional flag for this voucher (a provisional entry surfaced only through a
    /// scenario). No-op for a provisional type (Memorandum / Reversing Journal), which is off-books already.
    /// </summary>
    public void ToggleOptional()
    {
        if (IsProvisionalType) return;
        IsOptional = !IsOptional;
    }

    /// <summary>Esc / Alt+X cancel: discards the in-progress voucher and returns to the Gateway.</summary>
    public void Cancel() => _onCancelled();

    // =============================================================== item-invoice mode (catalog §10; slice 3.4c)

    /// <summary>
    /// Populates the item-invoice pickers for a Purchase/Sales: the party list ("(none)" + every ledger),
    /// the Purchases-/Sales-accounts ledger list (only ledgers under the right accounting head), and a
    /// sensible default for each. Called once from the constructor; no-op-safe on a non-invoice type (the
    /// lists simply go unused). Never touches the plain Dr/Cr <see cref="Lines"/>.
    /// </summary>
    private void BuildItemInvoicePickers()
    {
        Parties.Clear();
        Parties.Add(new PartyOption { Ledger = null, Display = "◦ (none)" });
        foreach (var l in _company.Ledgers.OrderBy(l => l.Name, StringComparer.OrdinalIgnoreCase))
            Parties.Add(new PartyOption { Ledger = l, Display = l.Name });
        SelectedParty = Parties.FirstOrDefault();

        // The value leg posts to a Purchases (Purchase Accounts, or Stock-in-Hand) ledger for a Purchase, or a
        // Sales (Sales Accounts) ledger for a Sales — the exact groups the pairing invariant recognises as the
        // stock leg. Offer only those ledgers and default to the first one.
        StockLedgers.Clear();
        foreach (var l in _company.Ledgers
                     .Where(IsStockLegLedger)
                     .OrderBy(l => l.Name, StringComparer.OrdinalIgnoreCase))
            StockLedgers.Add(l);
        SelectedStockLedger = StockLedgers.FirstOrDefault();

        // Seed one blank item line so the grid is ready to type into the moment the mode is turned on.
        if (InventoryLines.Count == 0) AddInventoryLine();
        RecalculateItemInvoice();
    }

    /// <summary>
    /// Whether a ledger is a valid value-leg target for this voucher's nature — Purchase: under Purchase
    /// Accounts (primary ancestor) or under Stock-in-Hand; Sales: under Sales Accounts (primary ancestor).
    /// Mirrors <c>VoucherValidator.IsStockLegLedger</c> so the auto-derived leg always satisfies the engine.
    /// </summary>
    private bool IsStockLegLedger(DomainLedger ledger)
    {
        var group = _company.FindGroup(ledger.GroupId);
        if (group is null) return false;
        if (IsPurchaseInvoice)
        {
            if (ClassificationRules.IsStockInHandLedger(ledger, _company)) return true;
            return string.Equals(ClassificationRules.PrimaryAncestorOf(group, _company).Name,
                "Purchase Accounts", StringComparison.OrdinalIgnoreCase);
        }
        return string.Equals(ClassificationRules.PrimaryAncestorOf(group, _company).Name,
            "Sales Accounts", StringComparison.OrdinalIgnoreCase);
    }

    partial void OnSelectedPartyChanged(PartyOption? value) => RecalculateItemInvoice();
    partial void OnSelectedStockLedgerChanged(DomainLedger? value) => RecalculateItemInvoice();

    /// <summary>
    /// Ctrl+I — toggles item-invoice mode on a Purchase/Sales (a no-op on any other type). Recomputes the
    /// items total / derived summary so the Accept gate reflects the new mode immediately.
    /// </summary>
    public void ToggleItemInvoice()
    {
        if (!CanBeItemInvoice) return;
        IsItemInvoice = !IsItemInvoice;
    }

    partial void OnIsItemInvoiceChanged(bool value)
    {
        // Turning the mode on/off changes which grid gates Accept AND whether GST / additional-cost tracking / the
        // Actual-Billed columns are wired in; recompute all.
        OnPropertyChanged(nameof(IsGstInvoice));
        OnPropertyChanged(nameof(ShowAdditionalCosts));
        OnPropertyChanged(nameof(ShowActualBilledColumns));
        OnPropertyChanged(nameof(QuantityHeader));
        SyncActualBilledOnLines();
        RecalculateItemInvoice();
        Recalculate();
    }

    /// <summary>Adds a blank additional-cost row (ledger + amount); keeps one trailing blank row.</summary>
    public AdditionalCostRowViewModel AddAdditionalCostRow()
    {
        var row = new AdditionalCostRowViewModel(AdditionalCostLedgers, OnAdditionalCostChanged);
        AdditionalCosts.Add(row);
        return row;
    }

    private void OnAdditionalCostChanged()
    {
        if (AdditionalCosts.Count == 0 || !AdditionalCosts[^1].IsBlank)
            AddAdditionalCostRow();
        RecalculateItemInvoice();
    }

    /// <summary>Adds a blank item-invoice inventory line (Movement kind: Item / Godown / Qty / Rate / Batch).</summary>
    public InventoryVoucherLineViewModel AddInventoryLine()
    {
        var line = new InventoryVoucherLineViewModel(
            InventoryLineKind.Movement, StockItems, Godowns, RecalculateItemInvoice)
        {
            ShowActualBilled = CanBeItemInvoice && _company.UseSeparateActualBilledQuantity,
        };
        InventoryLines.Add(line);
        RecalculateItemInvoice();
        return line;
    }

    /// <summary>Removes an item-invoice inventory line (keeping at least one); recomputes the total.</summary>
    public void RemoveInventoryLine(InventoryVoucherLineViewModel line)
    {
        if (InventoryLines.Count <= 1) return;
        InventoryLines.Remove(line);
        RecalculateItemInvoice();
    }

    /// <summary>The Σ of the complete item lines' values (each <b>Billed</b> qty × rate, paisa-exact). Value derives
    /// from Billed, NOT Actual (RQ-23): a short-billed / zero-valued line contributes its billed value only.</summary>
    public decimal ItemsTotal
    {
        get
        {
            var sum = 0m;
            foreach (var l in InventoryLines)
                if (l.IsComplete && l.ParsedRate is { } rate)
                    sum += Money.ForexBase(new Money(rate), l.ParsedBilledQuantity).Amount;
            return sum;
        }
    }

    /// <summary>
    /// The GST direction for this invoice's nature: a Purchase claims Input tax (ITC), a Sales charges Output tax.
    /// (In item-invoice mode <see cref="CanBeItemInvoice"/> restricts the nature to Purchase/Sales.)
    /// </summary>
    private GstTaxDirection GstDirection =>
        IsPurchaseInvoice ? GstTaxDirection.Input : GstTaxDirection.Output;

    /// <summary>The outcome of computing GST over the current complete item lines (for both display and posting).</summary>
    private readonly record struct ItemInvoiceGst(
        GstService.InvoiceTax Tax, bool InterState, StockItem? UnresolvedItem)
    {
        public bool HasUnresolved => UnresolvedItem is not null;
    }

    /// <summary>
    /// Resolves each complete item line's GST rate + taxability (item → value-ledger → company, most-granular-wins),
    /// routes intra vs inter from the party's recorded State vs the company home State, and computes the additive
    /// per-(head,rate) tax via <see cref="GstService.ComputeInvoiceTax"/>. Exempt/Nil/Non-GST lines contribute no
    /// taxable value (zero tax). A taxable line with no resolvable rate is flagged in
    /// <see cref="ItemInvoiceGst.UnresolvedItem"/> so the caller fails fast with a friendly message (never a
    /// silent zero, never a crash). Returns <c>null</c> when GST is not wired in (<see cref="IsGstInvoice"/> false).
    /// </summary>
    private ItemInvoiceGst? ComputeItemInvoiceGst()
    {
        if (!IsGstInvoice) return null;

        var valueLedger = SelectedStockLedger;
        var partyState = SelectedParty?.Ledger?.PartyGst?.StateCode;
        var interState = _gst.IsInterState(partyState);

        var taxable = new List<GstService.TaxableLine>();
        foreach (var l in InventoryLines.Where(l => l.IsComplete))
        {
            if (l.ParsedRate is not { } rate || rate <= 0m) continue;
            // GST taxable value derives from Billed, NOT Actual (RQ-23) — a short-billed line taxes only the
            // billed quantity, and a zero-valued (rate 0) free line is skipped above so it bears no GST.
            var lineValue = Money.ForexBase(new Money(rate), l.ParsedBilledQuantity);

            var res = _gst.ResolveRate(l.SelectedItem, valueLedger);
            if (GstService.IsUnresolved(res))
                return new ItemInvoiceGst(EmptyInvoiceTax(), interState, l.SelectedItem);
            if (!res.IsTaxable) continue; // Exempt/Nil/Non-GST ⇒ no tax
            taxable.Add(new GstService.TaxableLine(lineValue, res.RateBasisPoints));
        }

        var tax = _gst.ComputeInvoiceTax(taxable, interState, GstDirection);
        return new ItemInvoiceGst(tax, interState, UnresolvedItem: null);
    }

    /// <summary>An empty (no-tax) <see cref="GstService.InvoiceTax"/> used when a line is unresolved.</summary>
    private static GstService.InvoiceTax EmptyInvoiceTax() => new()
    {
        TaxLines = Array.Empty<EntryLine>(),
        LineBreakdown = Array.Empty<GstService.LineTax>(),
    };

    /// <summary>
    /// Recomputes the item-invoice indicators: the running items total, the derived Dr/Cr summary line, and —
    /// while in item-invoice mode — whether Accept is allowed (a party + a value ledger picked, ≥ 1 complete
    /// item line each with a positive rate, and no half-filled row). When GST is enabled it also recomputes the
    /// live tax totals (CGST/SGST/IGST) and the party total (taxable + tax) so the screen reflects the tax.
    /// </summary>
    public void RecalculateItemInvoice()
    {
        var total = ItemsTotal;
        ItemsTotalText = IndianFormat.AmountAlways(total);

        var party = SelectedParty?.Ledger?.Name ?? "party";

        // Additional cost of purchase (Book pp.133–141) — Σ of the complete additional-cost rows (0 when untracked),
        // added to the party total and apportioned onto the item landed rates below (RQ-16..RQ-20).
        var additionalTotal = AdditionalCostsTotal();
        AdditionalCostTotalText = IndianFormat.AmountAlways(additionalTotal);

        // GST summary (only when wired in) — computed once, shown as CGST/SGST/IGST + party total, and folded
        // into the derived-Dr/Cr summary so it reflects the additive tax legs.
        var gst = ComputeItemInvoiceGst();
        var cgst = gst?.Tax.TotalCgst.Amount ?? 0m;
        var sgst = gst?.Tax.TotalSgst.Amount ?? 0m;
        var igst = gst?.Tax.TotalIgst.Amount ?? 0m;
        var taxTotal = cgst + sgst + igst;
        var partyTotal = total + additionalTotal + taxTotal;

        GstCgstText = IndianFormat.AmountAlways(cgst);
        GstSgstText = IndianFormat.AmountAlways(sgst);
        GstIgstText = IndianFormat.AmountAlways(igst);
        PartyTotalText = IndianFormat.AmountAlways(partyTotal);

        // Stamp the read-only landed (effective) stock rate onto each complete item line via the SAME engine the
        // post/valuation uses (ER-4). No-op when tracking is off (columns collapse — untracked screen unchanged).
        RefreshLandedRates(InventoryLines.Where(l => l.IsComplete).ToList());

        DerivedSummary = BuildDerivedSummary(party, total, additionalTotal, cgst, sgst, igst, partyTotal);

        if (!IsItemInvoice) return; // plain-mode Accept is governed by Recalculate()

        var completeLines = InventoryLines.Count(l => l.IsComplete);
        var hasHalfFilled = InventoryLines.Any(l => !l.IsBlank && !l.IsComplete);
        // Every complete line needs a positive rate — UNLESS the voucher type allows zero-valued transactions
        // (RQ-21), in which case a ₹0 free-goods line (rate ≥ 0) is legitimate. Without the flag a 0 rate blocks
        // Accept exactly as before (ER-13).
        var allowZero = _type.AllowZeroValuedTransactions;
        var everyLineRateOk = InventoryLines
            .Where(l => l.IsComplete)
            .All(l => l.ParsedRate is { } r && (r > 0m || (allowZero && r >= 0m)));

        CanAccept =
            SelectedParty?.Ledger is not null
            && SelectedStockLedger is not null
            && completeLines >= 1
            && !hasHalfFilled
            && everyLineRateOk
            // A zero-valued invoice may total ₹0 (all lines free); otherwise the value must be positive.
            && (total > 0m || allowZero);
    }

    /// <summary>
    /// Builds the derived-Dr/Cr summary line. Without GST it is the plain two-leg summary (Dr Purchases/Cr party,
    /// or Dr party/Cr Sales). With GST the additive tax leg(s) are inserted — Input CGST/SGST on a purchase,
    /// Output CGST/SGST (or IGST) on a sale — and the party leg carries taxable + tax, e.g.
    /// "Dr Purchases 1,000.00 · Dr Input CGST 90.00 · Dr Input SGST 90.00 · Cr Supplier 1,180.00".
    /// </summary>
    private string BuildDerivedSummary(string party, decimal taxable, decimal additional, decimal cgst, decimal sgst, decimal igst, decimal partyTotal)
    {
        string A(decimal v) => IndianFormat.AmountAlways(v);
        var stock = StockLedgerCaption;
        var side = IsPurchaseInvoice ? "Dr" : "Cr"; // tax follows the value leg's side (Input Dr / Output Cr)

        var extraLegs = new List<string>();
        // Additional-cost legs (Purchase only) — each posts a Dr to its Direct-Expenses ledger (hits P&L, RQ-19).
        if (IsPurchaseInvoice && additional != 0m)
            extraLegs.Add($"Dr Additional Costs {A(additional)}");
        if (igst != 0m) extraLegs.Add($"{side} {(IsPurchaseInvoice ? "Input" : "Output")} IGST {A(igst)}");
        else
        {
            if (cgst != 0m) extraLegs.Add($"{side} {(IsPurchaseInvoice ? "Input" : "Output")} CGST {A(cgst)}");
            if (sgst != 0m) extraLegs.Add($"{side} {(IsPurchaseInvoice ? "Input" : "Output")} SGST {A(sgst)}");
        }
        var taxPart = extraLegs.Count > 0 ? "  ·  " + string.Join("  ·  ", extraLegs) : string.Empty;

        return IsPurchaseInvoice
            ? $"Dr {stock} {A(taxable)}{taxPart}  ·  Cr {party} {A(partyTotal)}"
            : $"Dr {party} {A(partyTotal)}{taxPart}  ·  Cr {stock} {A(taxable)}";
    }

    /// <summary>The Σ of the complete additional-cost rows (paisa-exact); 0 when the area is off/untracked.</summary>
    private decimal AdditionalCostsTotal()
    {
        if (!ShowAdditionalCosts) return 0m;
        var sum = 0m;
        foreach (var r in AdditionalCosts)
            if (r.IsComplete && r.ParsedAmount is { } a) sum += a;
        return sum;
    }

    /// <summary>
    /// Stamps each complete item line's read-only <b>landed</b> (effective) stock rate + value using the SAME
    /// engine the post/valuation uses (<see cref="AdditionalCostApportionment.ForPurchase"/>, ER-4): builds a
    /// throwaway Voucher of this type carrying the item lines + the additional-cost Dr lines and lets the engine
    /// derive the apportionment from each ledger's method. No-op (columns cleared/collapsed) when tracking is off
    /// or an item line is incomplete, so an untracked screen is byte-unchanged (ER-13).
    /// </summary>
    private void RefreshLandedRates(IReadOnlyList<InventoryVoucherLineViewModel> completeItems)
    {
        foreach (var l in InventoryLines)
        {
            l.ShowLanded = false;
            l.LandedRateText = string.Empty;
            l.LandedValueText = string.Empty;
        }
        if (!ShowAdditionalCosts || completeItems.Count == 0) return;

        var invLines = new List<VoucherInventoryLine>(completeItems.Count);
        var allowZero = _type.AllowZeroValuedTransactions;
        foreach (var l in completeItems)
        {
            // Wait for every item line to be valid; a ₹0 rate is only valid on a zero-valued-enabled type (RQ-21).
            if (l.ParsedRate is not { } rate || rate < 0m || (rate == 0m && !allowZero)) return;
            // Actual drives stock; Billed drives value — the landed apportionment uses each line's billed value.
            invLines.Add(new VoucherInventoryLine(
                l.SelectedItem!.Id, l.SelectedGodown!.Id, l.ParsedActualQuantity, new Money(rate),
                StockDirection.Inward, l.Batch, billedQuantity: l.ParsedBilledQuantity));
        }

        var costLines = new List<EntryLine>();
        foreach (var r in AdditionalCosts)
            if (r.IsComplete && r.SelectedLedger is { } led && r.ParsedAmount is { } amt)
                costLines.Add(new EntryLine(led.Id, new Money(amt), DrCr.Debit));
        if (costLines.Count == 0) return; // no additional cost ⇒ no landed columns (identical old valuation path)

        var temp = new Voucher(Guid.NewGuid(), _type.Id, Date, costLines, inventoryLines: invLines);
        var landed = AdditionalCostApportionment.ForPurchase(_company, temp);

        for (var i = 0; i < completeItems.Count && i < landed.Count; i++)
        {
            var ll = landed[i];
            completeItems[i].ShowLanded = true;
            completeItems[i].LandedRateText = IndianFormat.AmountAlways(ll.LandedUnitRate);
            completeItems[i].LandedValueText = IndianFormat.AmountAlways(ll.LandedValue.Amount);
        }
    }

    /// <summary>
    /// Ctrl+A accept for item-invoice mode: pre-validates (friendly message, before the engine), auto-derives
    /// the two balancing accounting legs so the pairing invariant is inherently satisfied, builds the
    /// <see cref="Voucher"/> with those legs + the <see cref="VoucherInventoryLine"/>s, and posts it through
    /// <see cref="LedgerService.Post"/> (which enforces pairing + atomicity + no-negative-stock — nothing
    /// persists on failure), then saves the company. Any domain error is surfaced to <see cref="Message"/>
    /// without crashing.
    /// </summary>
    private bool AcceptItemInvoice()
    {
        Message = null;

        if (SelectedParty?.Ledger is not { } party)
        {
            Message = $"Select the {PartyCaption.ToLowerInvariant()} for this item invoice.";
            return false;
        }
        if (SelectedStockLedger is not { } valueLedger)
        {
            Message = $"No {StockLedgerCaption} ledger is configured to post the value leg to.";
            return false;
        }

        // Reject half-filled (touched-but-incomplete) rows up front with a clear message.
        if (InventoryLines.Any(l => !l.IsBlank && !l.IsComplete))
        {
            Message = "Every item line needs a stock item, a godown, a positive quantity (≤ 6 dp) and a " +
                      "positive rate (≤ 2 dp / to the paisa).";
            return false;
        }

        var complete = InventoryLines.Where(l => l.IsComplete).ToList();
        if (complete.Count == 0)
        {
            Message = "Enter at least one item line before accepting.";
            return false;
        }

        // Build the item-invoice stock lines. Each line normally needs a positive rate; a ₹0 rate is accepted only
        // when the voucher type allows zero-valued transactions (RQ-21) — a legitimate free-goods line that moves
        // stock (Actual qty) but posts ₹0. Without the flag a ₹0 line is still rejected with a friendly message.
        var allowZero = _type.AllowZeroValuedTransactions;
        var inventoryLines = new List<VoucherInventoryLine>(complete.Count);
        foreach (var l in complete)
        {
            if (l.ParsedRate is not { } rate || rate < 0m || (rate == 0m && !allowZero))
            {
                Message = $"Item '{l.SelectedItem!.Name}' needs a rate greater than zero " +
                          "(enable 'Allow zero-valued transactions' to enter a free-goods line at ₹0).";
                return false;
            }
            // Actual (ParsedActualQuantity) moves stock; Billed (ParsedBilledQuantity) drives value + GST (RQ-23).
            // When the A/B column is off, Billed ≡ Actual so the line is byte-identical to today (ER-13).
            inventoryLines.Add(new VoucherInventoryLine(
                l.SelectedItem!.Id, l.SelectedGodown!.Id, l.ParsedActualQuantity, new Money(rate),
                // Direction is stamped from the voucher nature by the posting service; a placeholder is fine.
                direction: IsPurchaseInvoice ? StockDirection.Inward : StockDirection.Outward,
                batchLabel: l.Batch, billedQuantity: l.ParsedBilledQuantity));
        }

        // Σ item value (tax EXCLUDED) — the amount the STOCK leg carries, so the pairing invariant
        // (value leg == Σ item value) holds by construction; GST + additional cost are additive on top of it.
        var taxable = Money.Zero;
        foreach (var il in inventoryLines) taxable += il.Value;

        // Additional cost of purchase (Book pp.133–141; RQ-16): each additional-cost ledger posts its own Dr to
        // its Direct-Expenses ledger (so the expense hits P&L — it is NOT swallowed), AND its amount raises the
        // party total (it is part of the invoice payable to the supplier). The SAME amounts are apportioned onto
        // the item landed rates by the valuation engine — a valuation adjustment, not a second GL posting.
        var additionalCostLines = new List<EntryLine>();
        var additionalTotal = Money.Zero;
        if (ShowAdditionalCosts)
        {
            foreach (var r in AdditionalCosts.Where(r => !r.IsBlank))
            {
                if (!r.IsComplete || r.SelectedLedger is not { } led || r.ParsedAmount is not { } amt)
                {
                    Message = "Every additional-cost line needs a ledger and a paisa-exact amount greater than zero.";
                    return false;
                }
                additionalCostLines.Add(new EntryLine(led.Id, new Money(amt), DrCr.Debit));
                additionalTotal += new Money(amt);
            }
        }

        // GST (only when enabled): resolve each line's rate + taxability, split intra CGST/SGST vs inter IGST, and
        // build the additive tax entry lines (posted to the correct Output/Input ledgers, carrying GstLineTax so
        // the invoice flows into GSTR-1/3B/Tax Analysis). A taxable line with no resolvable rate fails fast.
        var taxLines = new List<EntryLine>();
        var partyAmount = new Money(taxable.Amount + additionalTotal.Amount);
        if (IsGstInvoice)
        {
            ItemInvoiceGst gst;
            try
            {
                gst = ComputeItemInvoiceGst()!.Value;
            }
            catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
            {
                Message = $"Cannot accept: {ex.Message}";
                return false;
            }
            if (gst.HasUnresolved)
            {
                Message = $"Item '{gst.UnresolvedItem!.Name}' is taxable but no GST rate is set on the item, " +
                          $"the {StockLedgerCaption} ledger, or the company. Set a rate before accepting.";
                return false;
            }
            taxLines.AddRange(gst.Tax.TaxLines);
            // party = taxable + additional cost + tax
            partyAmount = new Money(taxable.Amount + additionalTotal.Amount + gst.Tax.TotalTax.Amount);
        }

        // Auto-derive the accounting legs (no hand-balancing): the party carries taxable + additional + tax; the
        // stock/value leg carries taxable only; the additional-cost + tax lines are additive. Purchase → Dr
        // Purchases (taxable) / Dr Additional Costs / Dr Input tax / Cr Supplier (taxable+additional+tax).
        var partyLine = IsPurchaseInvoice
            ? new EntryLine(party.Id, partyAmount, DrCr.Credit)
            : new EntryLine(party.Id, partyAmount, DrCr.Debit);
        var stockLine = IsPurchaseInvoice
            ? new EntryLine(valueLedger.Id, taxable, DrCr.Debit)
            : new EntryLine(valueLedger.Id, taxable, DrCr.Credit);

        var entryLines = new List<EntryLine>(2 + additionalCostLines.Count + taxLines.Count) { stockLine, partyLine };
        entryLines.AddRange(additionalCostLines);
        entryLines.AddRange(taxLines);

        var voucher = new Voucher(
            Guid.NewGuid(),
            _type.Id,
            Date,
            entryLines,
            number: 0,
            narration: string.IsNullOrWhiteSpace(Narration) ? null : Narration.Trim(),
            partyId: party.Id,
            optional: IsOptional,
            postDated: IsPostDated,
            inventoryLines: inventoryLines);

        try
        {
            var posted = _service.Post(voucher); // enforces pairing + atomic stock + no-negative — never persisted on failure
            _storage.Save(_company);
            SavedNumber = posted.Number;
            Message = $"{_type.Name} No. {posted.Number} accepted.";
            _onSaved();
            return true;
        }
        catch (UnbalancedVoucherException)
        {
            Message = "The item invoice is out of balance. Not saved.";
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
}
