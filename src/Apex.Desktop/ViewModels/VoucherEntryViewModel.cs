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
        Ledgers = company.Ledgers;

        // Item-invoice masters (only meaningful on a Purchase/Sales, but always populated so the toggle is cheap).
        StockItems = company.StockItems;
        Godowns = company.Godowns;
        BuildItemInvoicePickers();

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
        // Turning the mode on/off changes which grid gates Accept; recompute both indicators.
        RecalculateItemInvoice();
        Recalculate();
    }

    /// <summary>Adds a blank item-invoice inventory line (Movement kind: Item / Godown / Qty / Rate / Batch).</summary>
    public InventoryVoucherLineViewModel AddInventoryLine()
    {
        var line = new InventoryVoucherLineViewModel(
            InventoryLineKind.Movement, StockItems, Godowns, RecalculateItemInvoice);
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

    /// <summary>The Σ of the complete item lines' values (each qty × rate, paisa-exact).</summary>
    public decimal ItemsTotal
    {
        get
        {
            var sum = 0m;
            foreach (var l in InventoryLines)
                if (l.IsComplete && l.ParsedRate is { } rate)
                    sum += Money.ForexBase(new Money(rate), l.ParsedQuantity).Amount;
            return sum;
        }
    }

    /// <summary>
    /// Recomputes the item-invoice indicators: the running items total, the derived Dr/Cr summary line, and —
    /// while in item-invoice mode — whether Accept is allowed (a party + a value ledger picked, ≥ 1 complete
    /// item line each with a positive rate, and no half-filled row).
    /// </summary>
    public void RecalculateItemInvoice()
    {
        var total = ItemsTotal;
        ItemsTotalText = IndianFormat.AmountAlways(total);

        var party = SelectedParty?.Ledger?.Name ?? "party";
        DerivedSummary = IsPurchaseInvoice
            ? $"Dr {StockLedgerCaption} {IndianFormat.AmountAlways(total)}  ·  Cr {party} {IndianFormat.AmountAlways(total)}"
            : $"Dr {party} {IndianFormat.AmountAlways(total)}  ·  Cr {StockLedgerCaption} {IndianFormat.AmountAlways(total)}";

        if (!IsItemInvoice) return; // plain-mode Accept is governed by Recalculate()

        var completeLines = InventoryLines.Count(l => l.IsComplete);
        var hasHalfFilled = InventoryLines.Any(l => !l.IsBlank && !l.IsComplete);
        var everyLineHasPositiveRate = InventoryLines
            .Where(l => l.IsComplete)
            .All(l => l.ParsedRate is { } r && r > 0m);

        CanAccept =
            SelectedParty?.Ledger is not null
            && SelectedStockLedger is not null
            && completeLines >= 1
            && !hasHalfFilled
            && everyLineHasPositiveRate
            && total > 0m;
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

        // Build the item-invoice stock lines; every line must carry a positive rate (the engine rejects a
        // zero-rate line, but we pre-validate so the message is friendly).
        var inventoryLines = new List<VoucherInventoryLine>(complete.Count);
        foreach (var l in complete)
        {
            if (l.ParsedRate is not { } rate || rate <= 0m)
            {
                Message = $"Item '{l.SelectedItem!.Name}' needs a rate greater than zero " +
                          "(a zero-rate line would move stock with no accounting backing).";
                return false;
            }
            inventoryLines.Add(new VoucherInventoryLine(
                l.SelectedItem!.Id, l.SelectedGodown!.Id, l.ParsedQuantity, new Money(rate),
                // Direction is stamped from the voucher nature by the posting service; a placeholder is fine.
                direction: IsPurchaseInvoice ? StockDirection.Inward : StockDirection.Outward,
                batchLabel: l.Batch));
        }

        // Σ item value — the amount BOTH auto-derived legs carry, so Σ Dr == Σ Cr AND the pairing invariant
        // (value leg == Σ item value) both hold by construction.
        var total = Money.Zero;
        foreach (var il in inventoryLines) total += il.Value;

        // Auto-derive the two accounting legs (no hand-balancing): Purchase → Dr Purchases / Cr Supplier;
        // Sales → Dr Customer / Cr Sales.
        var (drLedger, crLedger) = IsPurchaseInvoice ? (valueLedger, party) : (party, valueLedger);
        var entryLines = new[]
        {
            new EntryLine(drLedger.Id, total, DrCr.Debit),
            new EntryLine(crLedger.Id, total, DrCr.Credit),
        };

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
