using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Reports;
using CommunityToolkit.Mvvm.ComponentModel;
using DomainLedger = Apex.Ledger.Domain.Ledger;
using Apex.Desktop.Services;

namespace Apex.Desktop.ViewModels;

/// <summary>
/// One Dr/Cr particulars line in the voucher-entry grid: the picked ledger, the side
/// (Debit/Credit — Dr/By and Cr/To), and the amount typed as text. Parsing/validation
/// is deferred to the parent <see cref="VoucherEntryViewModel"/>; this class only holds the
/// editable state and raises change notifications so the live balance updates as the user types.
///
/// <para><b>Bill-wise (catalog §5):</b> when the picked ledger maintains balances bill-by-bill,
/// <see cref="IsBillWise"/> turns on and the line owns a "Bill-wise Details" sub-panel — a list of
/// <see cref="BillAllocationRowViewModel"/> whose amounts must <b>sum to the line amount</b> (the
/// split). A non-bill-wise line carries no allocations, so existing vouchers are unaffected.</para>
///
/// <para><b>Cost allocation (catalog §6):</b> when the picked ledger has cost centres applicable
/// (resolved by nature via <see cref="ClassificationRules.CostCentresApplicableFor"/>) and the company
/// has at least one cost centre defined, <see cref="IsCostApplicable"/> turns on and the line owns a
/// "Cost Allocation" sub-panel — a list of <see cref="CostAllocationRowViewModel"/> (Category → Centre →
/// Amount) whose amounts must <b>sum to the line amount</b>. It is optional: a line with no cost
/// allocations posts none, so existing vouchers are unaffected.</para>
///
/// <para><b>Bank allocation (catalog §8):</b> when the picked ledger is a bank account (under Bank
/// Accounts / Bank OD A/c, resolved via <see cref="ClassificationRules.IsBankLedger"/>),
/// <see cref="IsBankLine"/> turns on and the line owns a single "Bank Allocation" sub-panel — a
/// Transaction Type (cheque/DD, NEFT, RTGS, cash, other), an Instrument No. and an Instrument Date. A
/// bank line carries at most <b>one</b> allocation (it is not split), so it maps to the line's single
/// <see cref="Apex.Ledger.Domain.BankAllocation"/>. A non-bank line carries none, so existing vouchers
/// are unaffected.</para>
/// </summary>
public sealed partial class VoucherLineViewModel : ViewModelBase
{
    private readonly Action _onChanged;
    private readonly Company? _company;
    private readonly IReadOnlyList<CostCategory> _costCategories;
    private readonly IReadOnlyList<CostCentre> _costCentres;

    /// <summary>The company's ledgers the picker chooses from (shared list, set by the parent).</summary>
    public IReadOnlyList<DomainLedger> Ledgers { get; }

    /// <summary>The two sides a line can post to (Dr = Debit, Cr = Credit).</summary>
    public IReadOnlyList<DrCr> Sides { get; } = new[] { DrCr.Debit, DrCr.Credit };

    [ObservableProperty] private DomainLedger? _selectedLedger;
    [ObservableProperty] private DrCr _side = DrCr.Debit;
    [ObservableProperty] private string _amountText = string.Empty;

    /// <summary>True when the picked ledger maintains balances bill-by-bill ⇒ show the sub-panel.</summary>
    [ObservableProperty] private bool _isBillWise;

    /// <summary>Running text under the sub-panel: allocated total vs the line amount, with the shortfall.</summary>
    [ObservableProperty] private string _billSummary = string.Empty;

    /// <summary>The editable bill-wise allocation rows for this line (empty for a non-bill-wise line).</summary>
    public ObservableCollection<BillAllocationRowViewModel> BillAllocations { get; } = new();

    /// <summary>
    /// True when the picked ledger has cost centres applicable AND the company has ≥1 cost centre defined
    /// ⇒ show the "Cost Allocation" sub-panel. False (and the panel hidden) otherwise.
    /// </summary>
    [ObservableProperty] private bool _isCostApplicable;

    /// <summary>Running text under the cost sub-panel: allocated total vs the line amount, with the shortfall.</summary>
    [ObservableProperty] private string _costSummary = string.Empty;

    /// <summary>The editable cost-allocation rows for this line (empty for a non-cost line).</summary>
    public ObservableCollection<CostAllocationRowViewModel> CostAllocations { get; } = new();

    // =============================================================== bank allocation (catalog §8)

    /// <summary>
    /// True when the picked ledger is a bank account (under Bank Accounts / Bank OD A/c) ⇒ show the
    /// single "Bank Allocation" sub-panel (Transaction Type / Instrument No. / Instrument Date). False
    /// (and the panel hidden) otherwise.
    /// </summary>
    [ObservableProperty] private bool _isBankLine;

    /// <summary>The transaction (instrument) types the "Transaction Type" picker offers.</summary>
    public IReadOnlyList<BankTransactionType> BankTransactionTypes { get; } = new[]
    {
        BankTransactionType.ChequeOrDD, BankTransactionType.NEFT, BankTransactionType.RTGS,
        BankTransactionType.Cash, BankTransactionType.Other,
    };

    /// <summary>The chosen bank transaction type (defaults to cheque/DD, the classic instrument).</summary>
    [ObservableProperty] private BankTransactionType _bankTransactionType = BankTransactionType.ChequeOrDD;

    /// <summary>The instrument number (cheque no. / UTR / reference); optional (blank for a cash deposit).</summary>
    [ObservableProperty] private string _instrumentNumber = string.Empty;

    /// <summary>The instrument date typed as text (dd-MMM-yyyy); blank ⇒ no explicit instrument date.</summary>
    [ObservableProperty] private string _instrumentDateText = string.Empty;

    // =============================================================== forex (catalog §2/§20 Multi-currency)

    /// <summary>
    /// True when the picked ledger holds a foreign currency ⇒ show the "Forex Details" sub-panel (Amount in
    /// Forex / Rate of Exchange / Amount in ₹). False (and the panel hidden) for a base-currency ledger.
    /// </summary>
    [ObservableProperty] private bool _isForexLine;

    /// <summary>The ledger's foreign-currency symbol ("$", "€") for the forex-field prefixes; blank when base.</summary>
    [ObservableProperty] private string _forexSymbol = string.Empty;

    /// <summary>The ledger's foreign-currency code ("USD"); blank when base.</summary>
    [ObservableProperty] private string _forexCurrencyCode = string.Empty;

    /// <summary>The amount in the foreign currency typed as text; drives the base <see cref="AmountText"/>.</summary>
    [ObservableProperty] private string _forexAmountText = string.Empty;

    /// <summary>The rate of exchange typed as text (base ₹ per 1 foreign unit); defaulted from the rate in force.</summary>
    [ObservableProperty] private string _forexRateText = string.Empty;

    /// <summary>The computed base ₹ value ("Amount in ₹ = forex × rate"), shown read-only under the fields.</summary>
    [ObservableProperty] private string _forexBaseText = string.Empty;

    public VoucherLineViewModel(IReadOnlyList<DomainLedger> ledgers, Action onChanged, DrCr side = DrCr.Debit)
        : this(ledgers, onChanged, company: null, side)
    {
    }

    public VoucherLineViewModel(
        IReadOnlyList<DomainLedger> ledgers, Action onChanged, Company? company, DrCr side = DrCr.Debit)
    {
        Ledgers = ledgers ?? throw new ArgumentNullException(nameof(ledgers));
        _onChanged = onChanged ?? throw new ArgumentNullException(nameof(onChanged));
        _company = company;
        _costCategories = company?.CostCategories ?? Array.Empty<CostCategory>();
        _costCentres = company?.CostCentres ?? Array.Empty<CostCentre>();
        _side = side;
    }

    partial void OnSelectedLedgerChanged(DomainLedger? value)
    {
        SyncForexLine();
        SyncBillWise();
        SyncCostApplicable();
        SyncBankLine();
        _onChanged();
    }

    partial void OnSideChanged(DrCr value) => _onChanged();

    partial void OnAmountTextChanged(string value)
    {
        RecomputeBillSummary();
        RecomputeCostSummary();
        _onChanged();
    }

    /// <summary>
    /// Reflects the picked ledger's bill-by-bill flag: when a bill-wise ledger is chosen the sub-panel
    /// turns on and a first New-Ref row is seeded (defaulting its amount + name to the line so the common
    /// single-bill case needs no typing); when a non-bill-wise ledger is chosen the panel and its rows are
    /// cleared, so switching a line back never leaves stray allocations behind.
    /// </summary>
    private void SyncBillWise()
    {
        var on = SelectedLedger?.MaintainBillByBill == true;
        if (on == IsBillWise && (!on || BillAllocations.Count > 0))
        {
            RecomputeBillSummary();
            return;
        }

        IsBillWise = on;
        if (on)
        {
            if (BillAllocations.Count == 0)
                AddBillAllocation(BillRefType.NewRef);
        }
        else
        {
            BillAllocations.Clear();
        }
        RecomputeBillSummary();
    }

    /// <summary>Adds a blank bill-wise allocation row; recomputes the split summary and the balance.</summary>
    public BillAllocationRowViewModel AddBillAllocation(BillRefType refType = BillRefType.NewRef)
    {
        var row = new BillAllocationRowViewModel(OnBillRowChanged, refType);
        BillAllocations.Add(row);
        RecomputeBillSummary();
        return row;
    }

    /// <summary>Removes a bill-wise allocation row (keeps at least one on a bill-wise line).</summary>
    public void RemoveBillAllocation(BillAllocationRowViewModel row)
    {
        if (BillAllocations.Count <= 1) return;
        BillAllocations.Remove(row);
        RecomputeBillSummary();
        _onChanged();
    }

    private void OnBillRowChanged()
    {
        RecomputeBillSummary();
        _onChanged();
    }

    /// <summary>Σ of the allocation row magnitudes on this line.</summary>
    public decimal BillAllocatedTotal
    {
        get
        {
            var sum = 0m;
            foreach (var a in BillAllocations) sum += a.ParsedAmount;
            return sum;
        }
    }

    /// <summary>
    /// True when the bill-wise split is valid: not bill-wise (no constraint), or the touched rows are all
    /// complete and their amounts sum EXACTLY to the line amount (the split, enforced by the engine too).
    /// </summary>
    public bool BillSplitOk
    {
        get
        {
            if (!IsBillWise) return true;
            if (BillAllocations.Any(a => !a.IsBlank && !a.IsComplete)) return false;
            var complete = BillAllocations.Where(a => a.IsComplete).ToList();
            if (complete.Count == 0) return false;
            return complete.Sum(a => a.ParsedAmount) == ParsedAmount && ParsedAmount > 0m;
        }
    }

    private void RecomputeBillSummary()
    {
        if (!IsBillWise) { BillSummary = string.Empty; return; }

        var allocated = BillAllocatedTotal;
        var line = ParsedAmount;
        var diff = line - allocated;
        if (diff == 0m && line > 0m)
            BillSummary = $"Allocated {Fmt(allocated)} of {Fmt(line)}  —  fully allocated";
        else if (diff > 0m)
            BillSummary = $"Allocated {Fmt(allocated)} of {Fmt(line)}  —  {Fmt(diff)} unallocated";
        else
            BillSummary = $"Allocated {Fmt(allocated)} of {Fmt(line)}  —  over-allocated by {Fmt(-diff)}";
    }

    private static string Fmt(decimal v) => v.ToString("#,##0.00", Apex.Ledger.IndianMoneyFormat.Culture);

    /// <summary>
    /// The domain bill allocations for this line — the complete rows turned into <see cref="BillAllocation"/>.
    /// Empty for a non-bill-wise line (so the built <see cref="EntryLine"/> carries none).
    /// </summary>
    public IReadOnlyList<BillAllocation> ToBillAllocations()
    {
        if (!IsBillWise) return Array.Empty<BillAllocation>();
        return BillAllocations.Where(a => a.IsComplete).Select(a => a.ToAllocation()).ToList();
    }

    // =============================================================== cost allocation (catalog §6)

    /// <summary>
    /// Reflects whether the picked ledger has cost centres applicable (by nature/override) AND the company
    /// has ≥1 cost centre defined. When it turns on a first blank allocation row is seeded (defaulting its
    /// amount to the line, so the common single-centre case needs one centre pick); when it turns off the
    /// panel and its rows are cleared so switching a line's ledger never leaves stray allocations behind.
    /// </summary>
    private void SyncCostApplicable()
    {
        var on = _company is not null
                 && _costCentres.Count > 0
                 && SelectedLedger is not null
                 && ClassificationRules.CostCentresApplicableFor(SelectedLedger, _company);

        if (on == IsCostApplicable && (!on || CostAllocations.Count > 0))
        {
            RecomputeCostSummary();
            return;
        }

        IsCostApplicable = on;
        if (on)
        {
            if (CostAllocations.Count == 0)
                AddCostAllocation();
        }
        else
        {
            CostAllocations.Clear();
        }
        RecomputeCostSummary();
    }

    /// <summary>Adds a blank cost-allocation row; recomputes the split summary and the balance.</summary>
    public CostAllocationRowViewModel AddCostAllocation()
    {
        // Default to the first category that actually has centres (so the common single-category case
        // needs no Category click), falling back to the first category.
        var defaultCat = _costCategories.FirstOrDefault(c => _costCentres.Any(ce => ce.CategoryId == c.Id))
                         ?? _costCategories.FirstOrDefault();
        var row = new CostAllocationRowViewModel(
            OnCostRowChanged, _costCategories, _costCentres, defaultCategory: defaultCat);
        CostAllocations.Add(row);
        RecomputeCostSummary();
        return row;
    }

    /// <summary>Removes a cost-allocation row (keeps at least one on a cost-applicable line).</summary>
    public void RemoveCostAllocation(CostAllocationRowViewModel row)
    {
        if (CostAllocations.Count <= 1) return;
        CostAllocations.Remove(row);
        RecomputeCostSummary();
        _onChanged();
    }

    private void OnCostRowChanged()
    {
        RecomputeCostSummary();
        _onChanged();
    }

    // NOTE: there is deliberately no cross-category "CostAllocatedTotal" here. Summing the rows across
    // categories is exactly the check rule C-27 forbids: on a parallel set it is a MULTIPLE of the line
    // amount, and comparing it to the line is what made the corpus entry impossible. Use
    // CostAllocatedTotalFor(categoryId) — one axis at a time. (The domain twin
    // EntryLine.CostAllocationTotal survives only because rehydration still recognises the superseded
    // partition rule via CostAllocationStrictness.Legacy; nothing in the UI has that excuse.)

    /// <summary>Σ of the complete rows' magnitudes under one cost category — i.e. one allocation axis.</summary>
    public decimal CostAllocatedTotalFor(Guid categoryId)
    {
        var sum = 0m;
        foreach (var a in CostAllocations)
            if (a.IsComplete && a.SelectedCategory!.Id == categoryId)
                sum += a.ParsedAmount;
        return sum;
    }

    /// <summary>The distinct categories the complete rows use, in first-appearance order.</summary>
    private List<CostCategory> UsedCostCategories()
    {
        var seen = new List<CostCategory>();
        foreach (var a in CostAllocations)
            if (a.IsComplete && !seen.Any(c => c.Id == a.SelectedCategory!.Id))
                seen.Add(a.SelectedCategory!);
        return seen;
    }

    /// <summary>
    /// True when the cost split is valid: not cost-applicable (no constraint), OR the user left it fully
    /// blank (cost allocation is OPTIONAL), OR the touched rows are all complete and — <b>within each cost
    /// category independently</b> — their amounts sum EXACTLY to the line amount.
    /// <para>Cost categories are parallel allocation axes, not a partition (spec §4.2 rule C-27): the
    /// corpus allocates one ₹5,000 expense in full to Branch → Kolkata AND in full to Department →
    /// Marketing. Requiring the cross-category sum to equal the line — which this used to do — makes that
    /// entry impossible. The engine enforces the same per-axis rule, so this stays a faithful mirror.</para>
    /// </summary>
    public bool CostSplitOk
    {
        get
        {
            if (!IsCostApplicable) return true;
            // Optional: an untouched panel (every row blank) posts no cost allocations — valid.
            if (CostAllocations.All(a => a.IsBlank)) return true;
            if (CostAllocations.Any(a => !a.IsBlank && !a.IsComplete)) return false;
            var categories = UsedCostCategories();
            if (categories.Count == 0) return false;
            if (ParsedAmount <= 0m) return false;
            return categories.All(cat => CostAllocatedTotalFor(cat.Id) == ParsedAmount);
        }
    }

    /// <summary>
    /// The first cost category (in first-appearance order) whose own axis does NOT total the line amount, with what
    /// it does total — or <c>null</c> when every used axis foots (or the panel fails for some other reason).
    ///
    /// <para>🔴 <b>Exists so the refusal can state the rule the line actually breaks</b> (finding L3-03). A voucher
    /// carrying LEGACY cross-category allocations — the population <c>CostAllocationStrictness.Legacy</c> exists
    /// for, admitted by <c>SqliteCompanyStore.Load</c> and by the canonical import — reaches the alteration screen
    /// with allocations that sum ACROSS axes to the line and foot under no single one. The refusal it used to get
    /// said they "must sum to the line amount (5,000.00)" while they summed to exactly 5,000.00: the superseded
    /// partition rule C-27 abolished, quoted back at the operator on the one screen that can remediate it. The
    /// wording below mirrors <c>VoucherValidator</c>'s own C-27 text, and the first-short-axis-in-first-appearance
    /// -order choice mirrors its determinism.</para>
    /// </summary>
    public (CostCategory Category, decimal Allocated)? ShortCostAxis
    {
        get
        {
            if (!IsCostApplicable || ParsedAmount <= 0m) return null;
            foreach (var category in UsedCostCategories())
            {
                var allocated = CostAllocatedTotalFor(category.Id);
                if (allocated != ParsedAmount) return (category, allocated);
            }
            return null;
        }
    }

    private void RecomputeCostSummary()
    {
        if (!IsCostApplicable) { CostSummary = string.Empty; return; }

        if (CostAllocations.All(a => a.IsBlank))
        {
            CostSummary = "Cost allocation is optional — leave blank, or allocate the amount in full under each cost category.";
            return;
        }

        var line = ParsedAmount;
        var categories = UsedCostCategories();
        if (categories.Count == 0)
        {
            CostSummary = $"Allocated {Fmt(0m)} of {Fmt(line)}  —  {Fmt(line)} unallocated";
            return;
        }

        // Single axis — the wording every existing book sees, unchanged.
        if (categories.Count == 1)
        {
            CostSummary = $"Allocated {AxisState(categories[0].Id, line)}";
            return;
        }

        // Parallel axes: report each on its own. They are never added together.
        var parts = categories.Select(cat => $"{cat.Name}: {AxisState(cat.Id, line)}");
        CostSummary = string.Join("   |   ", parts) +
                      "   (each cost category is allocated in full — categories are parallel, not a split)";
    }

    /// <summary>"₹x of ₹y — fully allocated / n unallocated / over-allocated by n" for one axis.</summary>
    private string AxisState(Guid categoryId, decimal line)
    {
        var allocated = CostAllocatedTotalFor(categoryId);
        var diff = line - allocated;
        var state = diff == 0m && line > 0m
            ? "fully allocated"
            : diff > 0m
                ? $"{Fmt(diff)} unallocated"
                : $"over-allocated by {Fmt(-diff)}";
        return $"{Fmt(allocated)} of {Fmt(line)}  —  {state}";
    }

    /// <summary>
    /// The domain cost allocations for this line — the complete rows turned into <see cref="CostAllocation"/>.
    /// Empty for a non-cost line or an untouched (optional) panel.
    /// </summary>
    public IReadOnlyList<CostAllocation> ToCostAllocations()
    {
        if (!IsCostApplicable) return Array.Empty<CostAllocation>();
        return CostAllocations.Where(a => a.IsComplete).Select(a => a.ToAllocation()).ToList();
    }

    // =============================================================== bank allocation (catalog §8)

    partial void OnBankTransactionTypeChanged(BankTransactionType value) => _onChanged();
    partial void OnInstrumentNumberChanged(string value) => _onChanged();
    partial void OnInstrumentDateTextChanged(string value) => _onChanged();

    /// <summary>
    /// Reflects whether the picked ledger is a bank account (under Bank Accounts / Bank OD A/c). When it
    /// turns on the "Bank Allocation" sub-panel appears (defaulting to a cheque/DD). When it turns off the
    /// panel hides and its captured details are cleared, so switching a line's ledger never leaves a stray
    /// bank allocation behind.
    /// </summary>
    private void SyncBankLine()
    {
        var on = _company is not null
                 && SelectedLedger is not null
                 && ClassificationRules.IsBankLedger(SelectedLedger, _company);

        if (on == IsBankLine) return;

        IsBankLine = on;
        if (!on)
        {
            BankTransactionType = BankTransactionType.ChequeOrDD;
            InstrumentNumber = string.Empty;
            InstrumentDateText = string.Empty;
        }
    }

    /// <summary>The parsed instrument date (WI-5 shared day-first parser), or null when blank/unparsable.</summary>
    public DateOnly? ParsedInstrumentDate =>
        ApexDate.TryParse(InstrumentDateText, _voucherDate ?? DateOnly.FromDateTime(DateTime.Today), out var d)
            ? d
            : (DateOnly?)null;

    /// <summary>
    /// True when an instrument date was TYPED but cannot be read (WI-5). Blank is legitimate (no instrument
    /// date); unreadable text is not, and the parent's Accept refuses on it rather than silently dropping the
    /// operator's input and banking a null allocation date.
    /// </summary>
    public bool HasUnreadableInstrumentDate =>
        !string.IsNullOrWhiteSpace(InstrumentDateText) && ParsedInstrumentDate is null;

    /// <summary>
    /// The domain <see cref="BankAllocation"/> for this line — the captured transaction type, instrument
    /// number and instrument date. <c>null</c> for a non-bank line (so the built <see cref="EntryLine"/>
    /// carries none). A bank line always yields an allocation (it is how the BRS lists the transaction),
    /// even when the instrument fields are left blank.
    /// </summary>
    public BankAllocation? ToBankAllocation()
    {
        if (!IsBankLine) return null;
        return new BankAllocation(
            BankTransactionType,
            instrumentNumber: string.IsNullOrWhiteSpace(InstrumentNumber) ? null : InstrumentNumber.Trim(),
            instrumentDate: ParsedInstrumentDate);
    }

    // =============================================================== forex (catalog §2/§20 Multi-currency)

    partial void OnForexAmountTextChanged(string value) => RecomputeForexBase();
    partial void OnForexRateTextChanged(string value) => RecomputeForexBase();

    /// <summary>
    /// Reflects whether the picked ledger holds a foreign currency (its <c>CurrencyId</c> is set and the
    /// company knows that currency). When it turns on the "Forex Details" sub-panel appears, the currency's
    /// symbol/code are captured and the rate is defaulted from the rate in force on the parent voucher's
    /// current date (when known). When it turns off the panel hides and the forex fields are cleared, so
    /// switching a line's ledger never leaves a stray forex detail behind.
    /// </summary>
    private void SyncForexLine()
    {
        var currency = _company is not null
                       && SelectedLedger?.CurrencyId is { } cid
            ? _company.FindCurrency(cid)
            : null;

        var on = currency is not null;
        if (on == IsForexLine && (!on || !string.IsNullOrEmpty(ForexSymbol)))
        {
            RecomputeForexBase();
            return;
        }

        IsForexLine = on;
        if (on && currency is not null)
        {
            ForexSymbol = currency.Symbol;
            ForexCurrencyCode = currency.FormalName;
            // Default the rate from the latest quote on/before the voucher date, if one exists.
            if (string.IsNullOrWhiteSpace(ForexRateText) && _voucherDate is { } d
                && _company!.RateInForce(currency.Id, d) is { } inForce)
                ForexRateText = inForce.RateOf(ExchangeRateKind.Standard)
                    .ToString("0.####", CultureInfo.InvariantCulture);
        }
        else
        {
            ForexSymbol = string.Empty;
            ForexCurrencyCode = string.Empty;
            ForexAmountText = string.Empty;
            ForexRateText = string.Empty;
            ForexBaseText = string.Empty;
        }
        RecomputeForexBase();
    }

    /// <summary>
    /// Recomputes the base ₹ value from forex × rate and drives the line's base <see cref="AmountText"/> so
    /// the balance/engine see the exact paisa value. A base-currency line is untouched (the user types the
    /// amount directly). On an incomplete/invalid forex pair the base is left blank (the line stays
    /// incomplete until both forex amount and rate are valid).
    /// </summary>
    private void RecomputeForexBase()
    {
        if (!IsForexLine)
        {
            ForexBaseText = string.Empty;
            return;
        }

        var haveForex = TryParseDecimal(ForexAmountText, out var forex) && forex > 0m;
        var haveRate = TryParseDecimal(ForexRateText, out var rate) && rate > 0m;
        if (!haveForex || !haveRate)
        {
            ForexBaseText = string.Empty;
            AmountText = string.Empty; // no valid base yet ⇒ line incomplete
            return;
        }

        // Snap to the paisa (the same rounding ForexInfo.BaseValue uses) so the base the engine sees is
        // paisa-exact even on a non-round rate — an unrounded sub-paisa base cannot persist (INTEGER paisa).
        var baseValue = Money.ForexBase(new Money(forex), rate).Amount;
        ForexBaseText = $"₹ {baseValue.ToString("#,##0.00", Apex.Ledger.IndianMoneyFormat.Culture)}";
        // Drive the authoritative base amount (the engine enforces base == forex × rate rounded to the paisa).
        AmountText = baseValue.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>The parsed forex amount (0 when blank/unparsable).</summary>
    public decimal ParsedForexAmount => TryParseDecimal(ForexAmountText, out var v) ? v : 0m;

    /// <summary>The parsed rate of exchange (0 when blank/unparsable).</summary>
    public decimal ParsedForexRate => TryParseDecimal(ForexRateText, out var v) ? v : 0m;

    /// <summary>
    /// True when the forex detail is valid: not a forex line (no constraint), OR a positive forex amount and
    /// a positive rate are both entered (the base is then driven as forex × rate). A half-filled forex pair
    /// is invalid, so the line will not be accepted.
    /// </summary>
    public bool ForexOk => !IsForexLine || (ParsedForexAmount > 0m && ParsedForexRate > 0m);

    /// <summary>
    /// The domain <see cref="ForexInfo"/> for this line — currency + forex amount + rate. <c>null</c> for a
    /// base-currency line (so the built <see cref="EntryLine"/> carries none). The base amount the parent
    /// posts equals forex × rate (enforced by the engine).
    /// </summary>
    public ForexInfo? ToForexInfo()
    {
        if (!IsForexLine || SelectedLedger?.CurrencyId is not { } currencyId) return null;
        if (ParsedForexAmount <= 0m || ParsedForexRate <= 0m) return null;
        return new ForexInfo(currencyId, new Money(ParsedForexAmount), ParsedForexRate);
    }

    // =============================================================== Phase 10.11 S5b — the rehydration INVERSE

    /// <summary>
    /// 🔴 <b>The inverse of the four line writers</b> (<see cref="ToBillAllocations"/>,
    /// <see cref="ToCostAllocations"/>, <see cref="ToBankAllocation"/>, <see cref="ToForexInfo"/>): re-keys this
    /// blank row from a POSTED <see cref="EntryLine"/> so that re-running the writers reproduces it exactly.
    /// Returns <c>null</c> on success, or a <b>named refusal</b> when the posted line carries something this screen
    /// cannot express today.
    ///
    /// <para>🔴 <b>MASTER DRIFT is the failure this method exists to catch</b> (design §6.6a.5). All three named
    /// writers are lossless on CONTENT but lossy under drift, because each reads a <b>live master flag</b>:
    /// <c>SyncBillWise</c> gates on <c>SelectedLedger.MaintainBillByBill</c>, <c>SyncCostApplicable</c> on
    /// <c>ClassificationRules.CostCentresApplicableFor</c> plus the company having any centre at all, and
    /// <c>SyncBankLine</c> on <c>ClassificationRules.IsBankLedger</c>. Turn one of those OFF after posting and the
    /// rehydrated panel HIDES — so the writer returns empty and <b>the allocations vanish on re-accept with no
    /// message at all</b>. The gates are therefore read off the REAL line after the ledger is picked, never
    /// re-implemented here, so this check cannot drift out of step with the Sync methods it is policing.</para>
    ///
    /// <para><b>The drift is refused in BOTH directions, and they are not symmetrical.</b> Bill-wise turned ON after
    /// posting seeds a blank New-Ref row that no longer sums to the line, so Accept would refuse with a message
    /// about a split the operator never keyed; cost allocation is OPTIONAL, so an untouched panel is legitimate and
    /// only the OFF direction is a loss.</para>
    ///
    /// <para>🔴 <b><see cref="BankAllocation.BankDate"/> is deliberately NOT rehydrated.</b> It is written onto a
    /// posted voucher by a later human action (<c>BankReconciliation.SetBankDate</c>) and exists nowhere on this
    /// screen, so there is nothing to re-key. Carrying it is <c>LedgerService.Replace</c>'s job
    /// (<c>CarryBankDatesForward</c>, with its ECHO rule) — which is precisely why an alteration must end in
    /// <c>Replace</c> and never in <c>Post</c>.</para>
    /// </summary>
    public string? RehydrateFrom(EntryLine posted)
    {
        ArgumentNullException.ThrowIfNull(posted);

        var ledger = Ledgers.FirstOrDefault(l => l.Id == posted.LedgerId);
        if (ledger is null)
            return "one of its lines posts to a ledger that is no longer in this company, so the entry screen "
                 + "cannot show it.";

        // Assigning the ledger is what fires SyncForexLine / SyncBillWise / SyncCostApplicable / SyncBankLine —
        // i.e. it opens exactly the panels a fresh entry would open for this ledger TODAY. Everything below either
        // fills those panels or refuses because they no longer match what was posted.
        SelectedLedger = ledger;
        Side = posted.Side;

        if (RehydrateAmount(posted, ledger) is { } amountRefusal) return amountRefusal;
        if (RehydrateBillAllocations(posted, ledger) is { } billRefusal) return billRefusal;
        if (RehydrateCostAllocations(posted, ledger) is { } costRefusal) return costRefusal;
        if (RehydrateBank(posted, ledger) is { } bankRefusal) return bankRefusal;

        // The whole point of an inverse: what the writers will rebuild must be the amount that was posted. A forex
        // line's amount is DERIVED (forex x rate, snapped to the paisa) rather than typed, so this is the one check
        // that proves the derivation landed back on the posted figure.
        if (ParsedAmount != posted.Amount.Amount)
            return $"the amount on '{ledger.Name}' cannot be re-keyed exactly "
                 + $"({posted.Amount.Amount} was posted, the screen rebuilds {ParsedAmount}).";

        return null;
    }

    /// <summary>
    /// Re-keys the line's amount — through the forex pair when the line was posted in a foreign currency, because
    /// <c>RecomputeForexBase</c> DRIVES <see cref="AmountText"/> from forex x rate and would overwrite anything set
    /// directly.
    ///
    /// <para>🔴 <b>The rate format is widened here, and that is the whole fix for the one writer that was not
    /// losslessly invertible</b> (§6.6a.5). <c>ForexInfo.Rate</c> persists at <c>Schema.ForexScale</c> = 1,000,000
    /// (six decimal places), while this screen's own rate formatter is <c>"0.####"</c> — FOUR. An inverse reusing
    /// that format truncates a six-place rate, and because <c>Money.ForexBase</c> snaps forex x rate to the paisa,
    /// the rebuilt base amount can then differ from the posted one — which <c>VoucherValidator</c> rejects. Six
    /// places covers everything the store can hold; a rate carrying MORE (possible only in memory, before a save
    /// rounds it) falls back to the decimal's own exact round-trip rendering rather than being truncated or
    /// refused.</para>
    /// </summary>
    private string? RehydrateAmount(EntryLine posted, DomainLedger ledger)
    {
        if (IsForexLine != posted.HasForex)
            return posted.HasForex
                ? $"'{ledger.Name}' was posted in a foreign currency, but it no longer holds one — the forex "
                + "amount and rate on that line would be lost."
                : $"'{ledger.Name}' now holds a foreign currency but was posted in base currency, so the screen "
                + "would demand a forex amount and rate the posted line never carried.";

        // 🔴 WHICH currency, not merely WHETHER there is one (finding L3-01). The check above asks only that the
        // line still holds A foreign currency; ToForexInfo then rebuilds the ForexInfo from
        // `SelectedLedger.CurrencyId` — the LIVE master — so a ledger repointed from USD to EUR after posting
        // opened silently, accepted with a plain "altered." and restated the posted line in a currency it was never
        // denominated in. Nothing else caught it: EnsureForexValid checks only that the currency EXISTS and that
        // base ≈ forex × rate, both of which survive the swap, and LedgerMasterViewModel writes
        // `target.CurrencyId = SelectedCurrency?.CurrencyId` unconditionally, with no transacted-ledger guard.
        //
        // This is the FOURTH master-drift reader, and the only one whose drift reaches a posted EntryLine as a
        // VALUE rather than as a panel gate: the other three (SyncBillWise, SyncCostApplicable, SyncBankLine) are
        // compared against the posted shape above and below, and BillAllocationRowViewModel.ToAllocation /
        // ToBankAllocation read no live master at all.
        if (posted.Forex is { } postedForex && ledger.CurrencyId is { } liveCurrencyId
            && postedForex.CurrencyId != liveCurrencyId)
        {
            var postedCode = _company?.FindCurrency(postedForex.CurrencyId)?.FormalName ?? "another currency";
            var liveCode = _company?.FindCurrency(liveCurrencyId)?.FormalName ?? "a different currency";
            return $"'{ledger.Name}' was posted in {postedCode} but now holds {liveCode}, so re-accepting would "
                 + "restate the line in a currency it was never denominated in.";
        }

        if (posted.Forex is { } forex)
        {
            ForexAmountText = ExactDecimalText(forex.ForexAmount.Amount);
            ForexRateText = ExactDecimalText(forex.Rate, "0.######");
            return null; // RecomputeForexBase has driven AmountText; the caller verifies it landed on the posted figure
        }

        AmountText = ExactDecimalText(posted.Amount.Amount);
        return null;
    }

    /// <summary>
    /// Renders <paramref name="value"/> so that parsing it back yields the SAME decimal. Prefers
    /// <paramref name="preferred"/> (a tidy fixed-places form) and falls back to the decimal's own exact rendering
    /// when that would lose a digit — so the output is always lossless and usually also readable.
    /// </summary>
    private static string ExactDecimalText(decimal value, string? preferred = null)
    {
        if (preferred is not null)
        {
            var tidy = value.ToString(preferred, CultureInfo.InvariantCulture);
            if (decimal.TryParse(tidy, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var back)
                && back == value)
                return tidy;
        }
        return value.ToString(CultureInfo.InvariantCulture);
    }

    private string? RehydrateBillAllocations(EntryLine posted, DomainLedger ledger)
    {
        if (IsBillWise != posted.HasBillAllocations)
            return posted.HasBillAllocations
                ? $"'{ledger.Name}' no longer maintains balances bill-by-bill, so the bill-wise panel would not "
                + $"open and the {posted.BillAllocations.Count} allocation(s) posted on that line would silently "
                + "vanish on re-accept."
                : $"'{ledger.Name}' now maintains balances bill-by-bill but the posted line carries no allocation, "
                + "so the screen would demand a bill-wise split that was never keyed.";

        // The screen's own writer never states CreditPeriodDays (ToAllocation passes only an explicit due date), so
        // an allocation carrying one — from a canonical-XML import, or a book older than this screen — cannot be
        // re-keyed. Dropping it would move the ageing due date silently, which is exactly the class of loss this
        // method exists to refuse.
        if (posted.BillAllocations.FirstOrDefault(a => a.CreditPeriodDays is not null) is { } withPeriod)
            return $"a bill-wise allocation on '{ledger.Name}' ('{withPeriod.Name}') carries an explicit credit "
                 + "period, which this screen states as a due date rather than a number of days — re-accepting "
                 + "would drop it and move the bill's ageing.";

        BillAllocations.Clear();
        foreach (var a in posted.BillAllocations)
        {
            var row = AddBillAllocation(a.RefType);
            row.Name = a.Name;
            row.DueDateText = a.DueDate is { } due ? ApexDate.Format(due) : string.Empty;
            row.AmountText = ExactDecimalText(a.Amount.Amount);
        }
        RecomputeBillSummary();
        return null;
    }

    private string? RehydrateCostAllocations(EntryLine posted, DomainLedger ledger)
    {
        // Cost allocation is OPTIONAL, so the ON direction is not a loss: a cost-applicable line that posted no
        // allocation is a legitimate, round-tripping shape. Only the OFF direction destroys posted data.
        if (posted.HasCostAllocations && !IsCostApplicable)
            return $"cost centres no longer apply to '{ledger.Name}', so the cost panel would not open and the "
                 + $"{posted.CostAllocations.Count} allocation(s) posted on that line would silently vanish on "
                 + "re-accept.";

        // Only clear when there is something to replace the seed row WITH. A cost-applicable line that posted no
        // allocation keeps the blank starter row a fresh entry would show, and posts nothing (the panel is optional).
        if (!posted.HasCostAllocations) return null;

        CostAllocations.Clear();
        foreach (var a in posted.CostAllocations)
        {
            var row = AddCostAllocation();
            var category = _costCategories.FirstOrDefault(c => c.Id == a.CategoryId);
            if (category is null)
                return $"a cost allocation on '{ledger.Name}' names a cost category that is no longer in this "
                     + "company, so it cannot be re-keyed.";
            row.SelectedCategory = category;

            var centre = _costCentres.FirstOrDefault(c => c.Id == a.CentreId);
            if (centre is null || centre.CategoryId != category.Id)
                return $"a cost allocation on '{ledger.Name}' names a cost centre that is no longer under its "
                     + "category, so it cannot be re-keyed.";
            row.SelectedCentre = centre;

            row.AmountText = ExactDecimalText(a.Amount.Amount);
        }

        RecomputeCostSummary();
        return null;
    }

    private string? RehydrateBank(EntryLine posted, DomainLedger ledger)
    {
        if (IsBankLine != posted.HasBankAllocation)
            return posted.HasBankAllocation
                ? $"'{ledger.Name}' is no longer a bank account, so the bank panel would not open and the "
                + "instrument details posted on that line would be lost."
                : $"'{ledger.Name}' is now a bank account but the posted line carries no bank allocation, so "
                + "re-accepting would add banking detail the voucher never had.";

        if (posted.BankAllocation is not { } bank) return null;

        BankTransactionType = bank.TransactionType;
        InstrumentNumber = bank.InstrumentNumber ?? string.Empty;
        InstrumentDateText = bank.InstrumentDate is { } d ? ApexDate.Format(d) : string.Empty;
        // BankDate is NOT copied — see this method group's summary. LedgerService.Replace carries the reconcile
        // tick, and its ECHO rule exists precisely because a rehydration that DID copy it would defeat the guard.
        return null;
    }

    /// <summary>The parent voucher's current date, so a forex rate can be defaulted from the rate in force.</summary>
    private DateOnly? _voucherDate;

    /// <summary>Sets the voucher date used to default a forex rate; re-syncs a forex line's default rate.</summary>
    public void SetVoucherDate(DateOnly date)
    {
        _voucherDate = date;
        if (IsForexLine && string.IsNullOrWhiteSpace(ForexRateText)
            && SelectedLedger?.CurrencyId is { } cid && _company?.RateInForce(cid, date) is { } inForce)
        {
            ForexRateText = inForce.RateOf(ExchangeRateKind.Standard)
                .ToString("0.####", CultureInfo.InvariantCulture);
            RecomputeForexBase();
        }
    }

    /// <summary>
    /// True when this line is fully specified: a ledger picked and a positive, <b>storable</b> amount typed.
    ///
    /// <para>Storability is folded in (W0-13 S2a) because <c>VoucherEntryViewModel</c> builds its
    /// <see cref="EntryLine"/> set from <c>Lines.Where(l =&gt; l.IsComplete)</c>, and the line amount persists
    /// through <c>Paisa.FromMoney</c>. A touched-but-incomplete line is refused up front by the existing
    /// <c>Lines.Any(l =&gt; !l.IsBlank &amp;&amp; !l.IsComplete)</c> gate, so this can never silently DROP a line —
    /// which would be the far worse failure. <see cref="AmountError"/> supplies the discriminating message.</para>
    /// </summary>
    public bool IsComplete => SelectedLedger is not null && TryParseAmount(out var amt) && amt > 0m
        && StorableAmount.IsStorable(amt)
        && ForexOk;

    /// <summary>
    /// The field-level refusal for this line's own amount, or <c>null</c> when it can be stored.
    ///
    /// <para><b>Why the guard is here and NOT on the <see cref="EntryLine"/> constructor.</b> The three sibling
    /// value objects this slice guards (<c>BillAllocation</c>, <c>CostAllocation</c>, <c>PosTender</c>) are leaf
    /// records whose every non-screen caller — the canonical-XML import and the SQLite read path — builds from
    /// INTEGER paisa, so a domain guard there cannot regress anything. <see cref="EntryLine"/> is not that: it is
    /// the posting primitive, built at 62 sites across 17 engine services (GST, TDS, TCS, RCM, advance receipts,
    /// payroll, set-off, reversals, deposits) plus <c>ForexGainLoss</c>, which synthesises report-only lines that
    /// never persist. Moving the refusal into that constructor is a separate slice with its own engine sweep to
    /// justify. The value an OPERATOR types enters at exactly one place — <see cref="AmountText"/> — and that is
    /// what this guards.</para>
    /// </summary>
    public string? AmountError =>
        TryParseAmount(out var amt) ? StorableAmount.ErrorFor(amt, AmountText, "the line amount") : null;

    /// <summary>
    /// The field-level refusal for the <b>forex magnitude</b> on this line, or <c>null</c> when it can be carried.
    ///
    /// <para>🔴 <b>The one typed amount on this screen that <see cref="AmountError"/> did NOT cover</b> (finding
    /// L2-03). The line amount, the bill rows and the cost rows are all guarded; the forex amount was not, and
    /// <see cref="ForexOk"/> only asks that it be positive. So a forex amount carrying three decimal places posted,
    /// passed <c>VoucherValidator.EnsureForexValid</c> (which compares only base ≈ forex × rate) and SAVED — SQLite
    /// stores the magnitude at 1,000,000 scale and holds it happily — and then <c>CanonicalXml.Export</c> THREW,
    /// because the canonical model carries <c>ForexAmountPaisa</c> at two places. That is a company the app itself
    /// produced and cannot export, and the Export Data → XML path is the only door out of it.</para>
    ///
    /// <para>The base amount cannot catch it: <c>RecomputeForexBase</c> snaps forex × rate to the paisa, so the
    /// derived line amount is paisa-exact however fine the forex figure is.</para>
    /// </summary>
    public string? ForexAmountError =>
        IsForexLine && TryParseDecimal(ForexAmountText, out var fx) && fx > 0m
            ? StorableAmount.ErrorFor(
                fx, ForexAmountText,
                string.IsNullOrWhiteSpace(ForexCurrencyCode) ? "the amount in foreign currency"
                                                             : $"the {ForexCurrencyCode} amount")
            : null;

    /// <summary>True when the row has been touched at all (ledger or amount) — a blank row is ignored.</summary>
    public bool IsBlank => SelectedLedger is null && string.IsNullOrWhiteSpace(AmountText);

    /// <summary>The parsed amount (0 when unparsable/blank).</summary>
    public decimal ParsedAmount => TryParseAmount(out var amt) ? amt : 0m;

    /// <summary>Signed contribution to the Dr−Cr balance: +amount for a debit, −amount for a credit.</summary>
    public decimal Signed => Side == DrCr.Debit ? ParsedAmount : -ParsedAmount;

    private bool TryParseAmount(out decimal amount)
        => decimal.TryParse(
            (AmountText ?? string.Empty).Trim(),
            NumberStyles.AllowDecimalPoint | NumberStyles.AllowThousands,
            CultureInfo.InvariantCulture,
            out amount);

    private static bool TryParseDecimal(string? text, out decimal value)
        => decimal.TryParse(
            (text ?? string.Empty).Trim(),
            NumberStyles.AllowDecimalPoint | NumberStyles.AllowThousands,
            CultureInfo.InvariantCulture,
            out value);
}
