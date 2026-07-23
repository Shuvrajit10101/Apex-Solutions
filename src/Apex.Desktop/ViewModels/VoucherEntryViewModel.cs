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
public sealed partial class VoucherEntryViewModel : ViewModelBase, ISetsWorkingDate
{

    /// <summary>
    /// WI-5 (4c): the working-date field <b>F2</b> targets on this screen — the voucher date. Assigning routes
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

    // =============================================================== Price Levels (Book pp.34–35; catalog §11; slice 5)

    /// <summary>
    /// The Price-Level header choices for a Sales item-invoice (slice 5; RQ-30): a "Not Applicable" sentinel
    /// (no auto-fill) first, then every defined <see cref="PriceLevel"/>. Populated only when the feature is on.
    /// </summary>
    public ObservableCollection<PriceLevelSelectorOption> PriceLevelOptions { get; } = new();

    /// <summary>
    /// The chosen Price-Level header (slice 5; RQ-30): initialised from the selected party's
    /// <see cref="Ledger.DefaultPriceLevelId"/>, freely overridable, or "Not Applicable" for no auto-fill. On
    /// change the item lines re-resolve their auto-filled Rate/Discount (a user-dirtied line is left alone).
    /// </summary>
    [ObservableProperty] private PriceLevelSelectorOption? _selectedPriceLevel;

    /// <summary>
    /// Guards the Price-Level auto-fill against re-entrancy: stamping a line's Rate/Discount raises change
    /// notifications that re-enter <see cref="RecalculateItemInvoice"/>; this bool makes the nested
    /// <see cref="RefreshPriceLevelDefaults"/> a no-op so the pass terminates.
    /// </summary>
    private bool _refreshingPrices;

    /// <summary>
    /// True when the Price-Level header selector + per-line Discount column are shown (slice 5; RQ-30/RQ-52): a
    /// <b>Sales</b> item-invoice on a company whose "Enable multiple Price Levels" flag is on. Off ⇒ no header
    /// field, no auto-fill, no discount column — a non-price-level Sales screen is byte-identical (ER-13).
    /// </summary>
    public bool ShowPriceLevelSelector =>
        IsItemInvoice && CanBeItemInvoice && !IsPurchaseInvoice && _company.EnableMultiplePriceLevels;

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

    /// <summary>
    /// The invoice Compensation-Cess total (paisa-exact display; Phase 9 slice 1). "0.00" for a company that bears
    /// no cess (byte-identical when advanced-GST off, ER-13) — a cess line resolves only when a dated
    /// <see cref="GstCessRate"/> window (or a per-item override) matches the item's HSN on the voucher date.
    /// </summary>
    [ObservableProperty] private string _gstCessText = "0.00";

    /// <summary>The invoice party total = Σ taxable + Σ additional cost + Σ tax + Σ cess + Σ TCS (what the party is owed).</summary>
    [ObservableProperty] private string _partyTotalText = "0.00";

    // =============================================================== TCS additive collection on the Sales item-invoice (catalog §13; Phase 7 slice 5)

    /// <summary>
    /// True when this is a TCS-aware <b>Sales item invoice</b>: item-invoice mode is on, the nature is Sales (never
    /// Purchase — TCS is seller-side), and the company has TCS enabled (<see cref="Company.TcsEnabled"/>). Only then
    /// does the screen resolve each line's §206C Nature of Goods (goods-driven — from the STOCK ITEM's
    /// <see cref="StockItem.TcsNatureOfGoodsId"/> or the sales ledger, NOT the party), compute the additive TCS via
    /// <see cref="TcsService.BuildCollection"/>, DISPLAY the collection code + rate + amount, and POST the "TCS Payable"
    /// credit leg. On a TCS-off company (or a Purchase) this stays <c>false</c> and the invoice is byte-identical
    /// to the Phase-4 GST item-invoice (ER-13).
    /// </summary>
    public bool IsTcsSalesInvoice =>
        IsItemInvoice && CanBeItemInvoice && !IsPurchaseInvoice && _company.TcsEnabled;

    /// <summary>
    /// True when the TCS collection band is shown on the Sales item-invoice: <see cref="IsTcsSalesInvoice"/>, the
    /// chosen party is a <b>collectee</b> (carries a <see cref="Ledger.CollecteeType"/>), and at least one complete
    /// item line resolves to a §206C Nature of Goods that is selectable for the voucher date (the legacy §206C(1H)
    /// nature is non-selectable on/after 01-Apr-2025). Off ⇒ the band is hidden and the sale posts byte-identically
    /// (ER-13).
    /// </summary>
    [ObservableProperty] private bool _showTcs;

    /// <summary>The TCS band caption, FY-gated (CA S9) — the TCS charging section is <b>§206C</b> under the 1961 Act
    /// and <b>§394</b> under the 2025 Act, so the caption cannot be a literal in the view. Note this is §206C, the
    /// charging section — <b>not</b> §206CC, the (unverified, deliberately unmapped) no-PAN higher-rate section.</summary>
    public string TcsNatureOfGoodsCaption =>
        $"TCS — Nature of Goods (§{StatuteVocabulary.SectionLabel("206C", _company.FinancialYearStart.Year)})";

    /// <summary>The resolved §206C collection code for the band header (e.g. "6CE" scrap, or "Multiple" on a mixed
    /// invoice); empty when no TCS.</summary>
    [ObservableProperty] private string _tcsCollectionCodeText = string.Empty;

    /// <summary>The applied TCS rate for the band (e.g. "1%", or "5% (No PAN)" under §206CC); empty when no TCS or a
    /// mixed-rate invoice.</summary>
    [ObservableProperty] private string _tcsRateText = string.Empty;

    /// <summary>The TCS collected (nearest rupee), paisa-exact display; "0.00" below threshold / no TCS.</summary>
    [ObservableProperty] private string _tcsAmountText = "0.00";

    /// <summary>The one-line human summary of the collection shown under the band figures.</summary>
    [ObservableProperty] private string _tcsSummary = string.Empty;

    // =============================================================== TDS withholding on plain-grid vouchers (catalog §13; Phase 7 slice 2)

    /// <summary>
    /// The <b>TDS compute + auto-deduct</b> engine (Phase 7 slice 2) — the SAME service the posting uses (ER-4). The
    /// screen never re-implements the maths: it calls <see cref="TdsService.BuildCarveOut"/> for both the live panel
    /// and the accepted voucher, so what the operator sees is exactly what posts.
    /// </summary>
    private readonly TdsService _tds;

    /// <summary>
    /// The TCS <b>compute + auto-collect</b> engine (Phase 7 slice 5) — the SAME service the posting uses (ER-4). TCS
    /// is <b>additive</b> (collected on top, the mirror of GST, unlike the TDS carve-out): on a Sales item-invoice
    /// where a stock item / sales ledger is TCS-applicable under a §206C Nature of Goods AND the party is a collectee,
    /// the party total rises by the collected TCS. The screen never re-implements the maths: it calls
    /// <see cref="TcsService.BuildCollection"/> for both the live panel and the accepted voucher.
    /// </summary>
    private readonly TcsService _tcs;

    /// <summary>
    /// The <b>reverse-charge (RCM)</b> engine (Phase 9 slice 2) — the SAME service the posting uses (ER-4). The screen
    /// never re-implements applicability or the maths: it calls <see cref="RcmService.Resolve"/> for the live panel and
    /// <see cref="RcmService.BuildReverseCharge"/> for the accepted voucher's dual leg.
    /// </summary>
    private readonly RcmService _rcm;

    /// <summary>Re-entrancy guard for the TDS panel refresh (auto-defaulting the nature selector raises a change
    /// notification that would re-enter <see cref="Recalculate"/>); mirrors <see cref="_refreshingPrices"/>.</summary>
    private bool _updatingTds;

    /// <summary>
    /// The Nature-of-Payment (TDS section) choices for the withholding panel — every seeded/defined
    /// <see cref="NatureOfPayment"/> on the company. Empty (and the panel never shows) when TDS is not enabled.
    /// </summary>
    public ObservableCollection<NatureOfPayment> TdsNatureOptions { get; } = new();

    /// <summary>
    /// The "Not Applicable" sentinel in <see cref="TdsNatureOptions"/> — lets the operator <b>decline</b> TDS on a
    /// mixed/edge voucher (mirrors the Price-Level Not-Applicable option). Reference-identity compared; never a real
    /// section, never posts. Present in the picker only when TDS is enabled (natures exist).
    /// </summary>
    public static readonly NatureOfPayment TdsNotApplicable =
        new(Guid.Empty, "N/A", "Not Applicable (decline TDS)", 0, 0, "NA");

    /// <summary>
    /// The chosen Nature of Payment (TDS section) for this voucher's withholding — defaulted from the
    /// <b>expense</b> (Dr) ledger's own <see cref="Ledger.TdsNatureOfPaymentId"/> (the section is expense-driven,
    /// NOT party-driven), else a sensible first-seeded fallback, freely overridable in the panel (including the
    /// "Not Applicable" sentinel to decline). Changing it re-computes the deduction via the engine.
    /// </summary>
    [ObservableProperty] private NatureOfPayment? _selectedTdsNature;

    /// <summary>
    /// True when the TDS withholding panel is shown: TDS is enabled on the company, this is a plain-grid
    /// Payment/Journal/Purchase (never item-invoice), and the grid holds a complete expense (Dr) line plus a
    /// deductee-party (Cr) line. Off ⇒ the panel is hidden and the voucher posts byte-identically (ER-13).
    /// </summary>
    [ObservableProperty] private bool _showTdsPanel;

    /// <summary>The resolved TDS section code for the panel header (e.g. "194J(b)"); empty when no TDS.</summary>
    [ObservableProperty] private string _tdsSectionText = string.Empty;

    /// <summary>The applied rate for the panel (e.g. "10%", or "20% (No PAN)"); empty when no TDS.</summary>
    [ObservableProperty] private string _tdsRateText = string.Empty;

    /// <summary>The TDS amount withheld (nearest rupee), paisa-exact display; "0.00" below threshold / no TDS.</summary>
    [ObservableProperty] private string _tdsAmountText = "0.00";

    /// <summary>The net amount payable to the deductee after the carve-out (= gross − TDS); "0.00" when no TDS.</summary>
    [ObservableProperty] private string _tdsNetPayableText = "0.00";

    /// <summary>The one-line human summary of the withholding shown under the panel figures.</summary>
    [ObservableProperty] private string _tdsSummary = string.Empty;

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

    /// <summary>The <b>rendered</b> preview of the number Accept will post (numbering-design-v2 §4) — the affixed/padded
    /// "Voucher No." for the previewed <see cref="VoucherNumber"/> on the current <see cref="Date"/>. It is EQUAL to the
    /// number the engine assigns and renders on Accept (both compute <c>max+1</c> for this type and render with the same
    /// (type, Date)); it refreshes in <see cref="OnDateChanged"/> so crossing an affix-row boundary updates the previewed
    /// prefix in lock-step. With an empty numbering config this is byte-identical to <c>VoucherNumber</c>.</summary>
    public string FormattedVoucherNumber =>
        Apex.Ledger.Services.VoucherNumberFormatter.Render(_type, VoucherNumber, Date);

    partial void OnVoucherNumberChanged(int value) => OnPropertyChanged(nameof(FormattedVoucherNumber));

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
    /// The voucher date as editable text, in the one canonical <see cref="ApexDate.Canonical"/> spelling
    /// (WI-5). Input is read by the shared DAY-FIRST parser, so "03/04/2024" is 3-Apr — never the 4-Mar
    /// month-first misread the old InvariantCulture parse produced.
    /// <para>
    /// Unparseable input is <b>rejected, never silently discarded</b>: <see cref="Date"/> keeps its last
    /// valid value, <see cref="Message"/> names the problem, and the field is re-notified so the rejected
    /// text snaps back to the canonical rendering of the date actually held. (Previously the typed text
    /// stayed on screen while a DIFFERENT date posted — screen and stored value silently disagreed.)
    /// </para>
    /// </summary>
    public string DateText
    {
        get => ApexDate.Format(Date);
        set
        {
            if (ApexDate.TryParse(value, Date, out var parsed))
                Date = parsed;
            else
                Message = ApexDate.ErrorFor(value);

            // Re-notify UNCONDITIONALLY. On success this echoes the canonical spelling even when the parsed
            // date equals the current one (Date would not raise); on failure it replaces the rejected text
            // with the date actually held. The property-changed path alone cannot do this — it only fires
            // when Date CHANGES, which is exactly why the discard used to be silent.
            OnPropertyChanged(nameof(DateText));
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
        _tds = new TdsService(company);
        _tcs = new TcsService(company);
        _rcm = new RcmService(company);
        _advance = new AdvanceReceiptService(company);
        Ledgers = company.Ledgers;

        // Reverse-charge inward-supply routing (Phase 9 slice 2; RQ-11). Import of goods is deliberately NOT offered —
        // it is never reverse charge (customs IGST → GSTR-3B 4A(1)). The panel itself only shows when an RCM-flagged
        // expense leg is on a GST company's plain grid, so these options are inert otherwise (ER-13).
        //
        // The "Not Applicable" decline sentinel LEADS the list, exactly as it does in the TDS Nature-of-Payment picker —
        // but, exactly as there, it is never the DEFAULT (see UpdateRcmPanel): reverse charge is mandatory when a
        // notified category fires, so it must self-account unless the operator actively says otherwise.
        RcmSupplyKinds.Add(RcmNotApplicable);
        RcmSupplyKinds.Add(RcmDomestic);
        RcmSupplyKinds.Add(new RcmSupplyKindOption
        {
            Kind = RcmService.SupplyKind.ImportOfServices,
            Display = "Import of services (§5(3) — always IGST)",
        });

        // TDS Nature-of-Payment choices (Phase 7 slice 2). Empty when TDS is not enabled, so the withholding
        // panel never shows and a plain voucher is byte-identical (ER-13). When natures exist, the "Not Applicable"
        // sentinel leads the list so the operator can decline TDS on a mixed/edge voucher.
        if (company.NaturesOfPayment.Any())
        {
            TdsNatureOptions.Add(TdsNotApplicable);
            foreach (var n in company.NaturesOfPayment.OrderBy(n => n.SectionCode, StringComparer.OrdinalIgnoreCase))
                TdsNatureOptions.Add(n);
        }

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
        BuildSection34Pickers(); // §34 note pickers (a no-op on any non-Credit/Debit-Note type)
        BuildAdvancePickers();   // outstanding-advance pickers (a no-op unless this type adjusts/refunds one)
        AddAdditionalCostRow(); // one blank trailing row ready to type into

        // Default date: last voucher date, else books-begin (never before books, which Post rejects).
        var last = company.Vouchers.Count == 0
            ? (DateOnly?)null
            : company.Vouchers.Max(v => v.Date);
        Date = date ?? last ?? company.BooksBeginFrom;

        VoucherNumber = _service.NextNumber(type.Id);
        Title = $"{type.Name} Voucher";

        // A Reversing Journal defaults its "Applicable Upto" to the financial-year end.
        _applicableUptoText = ApexDate.Format(company.FinancialYearStart.AddYears(1).AddDays(-1));

        // Seed two starter lines: the first Dr, the second Cr (opens with a By/To pair).
        AddLine(DrCr.Debit);
        AddLine(DrCr.Credit);
        Recalculate();
    }

    partial void OnDateChanged(DateOnly value)
    {
        OnPropertyChanged(nameof(DateText));
        // numbering-design-v2 §4: the previewed number must track the date so an affix-row boundary crossing updates
        // the previewed prefix in lock-step with what Accept posts.
        OnPropertyChanged(nameof(FormattedVoucherNumber));
        // Push the new date to every line so a forex line can default its rate from the rate in force.
        foreach (var line in Lines) line.SetVoucherDate(value);

        // The date now feeds date-dependent derivations — the TCS band's 206C(1H) FA2025 year-gate
        // (NatureOfGoods.IsSelectableOn) and the §206C(1H) ₹50-lakh cumulative-FY projection both read Date. Re-derive
        // the invoice so what is SHOWN matches what Accept POSTS (ER-4): editing the header date across the 01-Apr-2025
        // cutoff (or an FY boundary) must flip ShowTcs / the collection band in lock-step with the posting.
        if (IsItemInvoice) RecalculateItemInvoice(); else Recalculate();
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
        // TDS withholding panel (Phase 7 slice 2): refresh first so it is cleared in item-invoice mode too (the
        // helper self-gates via TdsPossible, which is false when item-invoice is on). Cheap + pure.
        UpdateTdsPanel();

        // RCM panel (Phase 9 slice 2): likewise self-gates via RcmPossible (false in item-invoice mode / GST off), and
        // is deliberately side-effect-free — see UpdateRcmPanel's note on why it never previews through the builder.
        UpdateRcmPanel();

        // §34 advisory (Phase 9 slice 2b): the 30-Nov cut-off is a function of the VOUCHER DATE, so it must refresh
        // whenever the date does — not only when a §34 field is touched. Wired only to its own field handlers, it went
        // stale on a re-date and kept asserting "within the limit" on a note Accept would refuse. Self-gates on
        // ShowSection34Details and is pure, so it is a no-op on every other screen (ER-13).
        UpdateSection34Panel();

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

    // =============================================================== TDS withholding (catalog §13; Phase 7 slice 2)

    /// <summary>The <b>shape</b> of a potential TDS withholding on the plain grid: a complete <i>Is-TDS-Applicable</i>
    /// expense/purchase <b>debit</b> leg (which drives applicability AND the default section) plus a complete
    /// deductee-party <b>credit</b> line (positive amount = the gross obligation). When the shape holds the panel
    /// shows; the operator may still decline via the "Not Applicable" sentinel.</summary>
    private readonly record struct TdsShape(
        VoucherLineViewModel PartyLine, DomainLedger Deductee, Money Gross, DomainLedger Expense);

    /// <summary>The resolved context of a <b>firing</b> TDS withholding: the deductee party's Cr line, the deductee
    /// ledger, the gross obligation, and the Nature of Payment (section) — resolved from the EXPENSE ledger's default
    /// (or the operator's override), never the party's default.</summary>
    private readonly record struct TdsContext(
        VoucherLineViewModel PartyLine, DomainLedger Deductee, Money Gross, NatureOfPayment Nature);

    /// <summary>True when TDS could apply on this screen: TDS is enabled, this is a plain-grid Payment/Journal/
    /// Purchase (never item-invoice). The concrete applicability (an Is-TDS-Applicable expense Dr leg + a deductee
    /// party Cr leg) is tested in <see cref="DetectTdsShape"/>; when absent the voucher posts byte-identically (ER-13).</summary>
    private bool TdsPossible =>
        _company.TdsEnabled
        && !IsItemInvoice
        && _type.BaseType is VoucherBaseType.Payment or VoucherBaseType.Journal or VoucherBaseType.Purchase;

    /// <summary>Whether a Cr-side ledger is a TDS <b>deductee</b> party — per its documented meaning it carries a
    /// <see cref="Ledger.DeducteeType"/> (legal status). This is deliberately NOT the expense ledger's
    /// <see cref="Ledger.TdsApplicable"/> flag (that gates the Dr/expense leg): the party drives only the RATE
    /// (PAN present ⇒ with-PAN, no PAN ⇒ 20% / 5% for 194Q), never applicability or the section.</summary>
    private static bool IsDeducteeLedger(DomainLedger l) => l.DeducteeType is not null;

    /// <summary>True when the operator has <b>declined</b> TDS on this voucher via the "Not Applicable" sentinel.</summary>
    private bool IsTdsDeclined => SelectedTdsNature is { } s && ReferenceEquals(s, TdsNotApplicable);

    /// <summary>
    /// Detects the TDS <b>shape</b> on the current plain grid (the panel-visibility gate): on a TDS-enabled
    /// Payment/Journal/Purchase, at least one complete <i>Is-TDS-Applicable</i> expense/purchase <b>debit</b> leg
    /// AND a complete deductee-party <b>credit</b> line with a positive gross. A non-TDS expense paid to a deductee
    /// (no Is-TDS-Applicable Dr leg) does <b>not</b> qualify — no withholding. Returns <c>null</c> ⇒ the panel hides
    /// and the voucher posts byte-identically (ER-13).
    /// </summary>
    private TdsShape? DetectTdsShape()
    {
        if (!TdsPossible) return null;

        // The EXPENSE (Dr) leg drives applicability: a complete debit line whose ledger is *Is TDS Applicable*.
        var expenseLine = Lines.FirstOrDefault(l =>
            l.IsComplete && l.Side == DrCr.Debit && l.SelectedLedger is { TdsApplicable: true });
        if (expenseLine is null) return null; // no Is-TDS-Applicable expense leg ⇒ no withholding

        // The PARTY (Cr) leg must be a deductee (carries a DeducteeType); it drives only the rate, not the section.
        var partyLine = Lines.FirstOrDefault(l =>
            l.IsComplete && l.Side == DrCr.Credit && l.SelectedLedger is { } led && IsDeducteeLedger(led));
        if (partyLine is null) return null;

        var gross = new Money(partyLine.ParsedAmount);
        if (gross.Amount <= 0m) return null;

        return new TdsShape(partyLine, partyLine.SelectedLedger!, gross, expenseLine.SelectedLedger!);
    }

    /// <summary>
    /// Resolves the <b>firing</b> TDS context from the shape: <c>null</c> when there is no shape or the operator
    /// declined via "Not Applicable"; otherwise the Nature of Payment comes from the operator's override, else the
    /// EXPENSE ledger's default section (<see cref="DefaultNatureFor"/>) — never the party's default. Drives the
    /// carve-out on Accept; <c>null</c> ⇒ byte-identical posting (ER-13).
    /// </summary>
    private TdsContext? DetectTdsContext()
    {
        if (DetectTdsShape() is not { } shape) return null;
        if (IsTdsDeclined) return null; // operator chose "Not Applicable"

        var nature = SelectedTdsNature is { } sel && !ReferenceEquals(sel, TdsNotApplicable)
            ? sel
            : DefaultNatureFor(shape.Expense);
        if (nature is null) return null;

        return new TdsContext(shape.PartyLine, shape.Deductee, shape.Gross, nature);
    }

    /// <summary>The default Nature of Payment resolved from the <b>expense</b> ledger's own default section
    /// (<see cref="Ledger.TdsNatureOfPaymentId"/>) — the section is expense-driven. When the expense ledger has no
    /// default, a sensible fallback to the first seeded nature (operator-selectable in the panel), but <b>never</b>
    /// the party's default. <c>null</c> only when no nature exists at all.</summary>
    private NatureOfPayment? DefaultNatureFor(DomainLedger expense)
    {
        if (expense.TdsNatureOfPaymentId is { } id && _company.FindNatureOfPayment(id) is { } n) return n;
        return _company.NaturesOfPayment.FirstOrDefault();
    }

    /// <summary>
    /// The <b>GST-exclusive</b> assessable base for the current plain grid (CBDT Circular 23/2017 — TDS is computed
    /// on the value excluding GST): the sum of the complete <b>debit</b> (expense/purchase) lines, EXCLUDING any leg
    /// that posts to a <b>Duties &amp; Taxes</b> ledger (the Input CGST/SGST/IGST legs of a GST bill booked through a
    /// Journal). Equals the party's gross obligation when no GST leg is on the grid, so a plain non-GST voucher is
    /// unchanged; when Input-GST debit lines are present it nets them out so TDS is not over-withheld on the tax.
    /// </summary>
    private Money AssessableExGst()
    {
        var sum = 0m;
        foreach (var l in Lines.Where(l => l.IsComplete && l.Side == DrCr.Debit))
            if (l.SelectedLedger is { } led && !ClassificationRules.IsDutiesAndTaxesLedger(led, _company))
                sum += l.ParsedAmount;
        return new Money(sum);
    }

    /// <summary>
    /// Refreshes the TDS withholding panel from the SAME <see cref="TdsService.BuildCarveOut"/> the accept path
    /// uses (ER-4): resolves the deduction on the deductee's gross obligation, with the TDS assessed on the
    /// <b>GST-exclusive</b> base (<see cref="AssessableExGst"/> — Input GST debit legs netted out, Circular
    /// 23/2017), and surfaces the section, rate, withheld amount and net payable.
    /// A no-op (panel hidden, figures cleared) when no TDS applies, so a non-TDS voucher is byte-identical (ER-13).
    /// Re-entrancy-guarded: auto-defaulting the nature selector raises a change notification.
    /// </summary>
    private void UpdateTdsPanel()
    {
        if (_updatingTds) return;
        _updatingTds = true;
        try
        {
            if (DetectTdsShape() is not { } shape)
            {
                ShowTdsPanel = false;
                TdsSectionText = string.Empty;
                TdsRateText = string.Empty;
                TdsAmountText = "0.00";
                TdsNetPayableText = "0.00";
                TdsSummary = string.Empty;
                return;
            }

            // The shape holds ⇒ the panel shows (the operator may still decline via "Not Applicable").
            ShowTdsPanel = true;

            // Default the selector to the EXPENSE ledger's section on first sight (only when unset — any override,
            // including the "Not Applicable" decline, sticks).
            if (SelectedTdsNature is null) SelectedTdsNature = DefaultNatureFor(shape.Expense);

            if (DetectTdsContext() is not { } ctx)
            {
                // Declined ("Not Applicable") or no nature to resolve — show a zeroed, byte-identical-posting state
                // (the full gross is payable) while keeping the panel visible so the operator can re-enable TDS.
                TdsSectionText = string.Empty;
                TdsRateText = string.Empty;
                TdsAmountText = "0.00";
                TdsNetPayableText = IndianFormat.AmountAlways(shape.Gross.Amount);
                TdsSummary = $"TDS not applied — full ₹{IndianFormat.AmountAlways(shape.Gross.Amount)} " +
                             $"payable to {shape.Deductee.Name}.";
                return;
            }

            TdsService.CarveOut carve;
            try
            {
                carve = _tds.BuildCarveOut(ctx.Gross, AssessableExGst(), ctx.Nature, ctx.Deductee, Date);
            }
            catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
            {
                // e.g. a not-paisa-exact typed amount, or TDS ≥ obligation — hide the panel rather than crash.
                ShowTdsPanel = false;
                return;
            }

            var w = carve.Withholding;
            ShowTdsPanel = true;
            TdsSectionText = ctx.Nature.SectionCode;
            TdsRateText = (w.RateBasisPoints / 100m).ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)
                          + "%" + (w.PanApplied ? string.Empty : " (No PAN)");
            TdsAmountText = IndianFormat.AmountAlways(carve.TdsAmount.Amount);
            TdsNetPayableText = IndianFormat.AmountAlways(carve.NetPartyAmount.Amount);
            TdsSummary = carve.Applies
                ? $"TDS {ctx.Nature.SectionCode} @ {TdsRateText}: ₹{TdsAmountText} withheld · " +
                  $"Net payable to {ctx.Deductee.Name} ₹{TdsNetPayableText}"
                : $"{ctx.Nature.SectionCode}: below threshold — no TDS, full " +
                  $"₹{IndianFormat.AmountAlways(ctx.Gross.Amount)} payable to {ctx.Deductee.Name}";
        }
        finally
        {
            _updatingTds = false;
        }
    }

    /// <summary>The operator changing the TDS section re-computes the deduction (unless the change came from the
    /// auto-default inside <see cref="UpdateTdsPanel"/>, which is guarded to avoid re-entrancy).</summary>
    partial void OnSelectedTdsNatureChanged(NatureOfPayment? value)
    {
        if (_updatingTds) return;
        Recalculate();
    }

    // =============================================================== RCM inward supply (catalog §12; Phase 9 slice 2; RQ-3/RQ-7/RQ-8/RQ-11)

    /// <summary>Re-entrancy guard for the RCM panel refresh (auto-defaulting the supply-kind selector raises a change
    /// notification that would re-enter <see cref="Recalculate"/>); mirrors <see cref="_updatingTds"/>.</summary>
    private bool _updatingRcm;

    /// <summary>
    /// The inward-supply routing choices for the RCM panel (RQ-11). <b>Import of goods is deliberately absent</b>: it is
    /// never a reverse-charge supply (IGST is paid at customs on the Bill of Entry → GSTR-3B 4A(1)) and
    /// <see cref="RcmService.BuildReverseCharge"/> hard-fails on it — offering it could only ever earn a refusal.
    /// The list leads with the <see cref="RcmNotApplicable"/> decline sentinel.
    /// </summary>
    public ObservableCollection<RcmSupplyKindOption> RcmSupplyKinds { get; } = new();

    /// <summary>The "Not Applicable" decline sentinel — the mirror of the TDS picker's own. Identified by its null
    /// <see cref="RcmSupplyKindOption.Kind"/>; per-screen (never shared) so one voucher's decline cannot leak into
    /// another's.</summary>
    public RcmSupplyKindOption RcmNotApplicable { get; } = new()
    {
        Kind = null,
        Display = "◦ Not Applicable — forward charge / not a supply",
    };

    /// <summary>The ordinary domestic routing — the DEFAULT selection whenever an RCM shape appears.</summary>
    private RcmSupplyKindOption RcmDomestic { get; } = new()
    {
        Kind = RcmService.SupplyKind.Domestic,
        Display = "Domestic inward supply (§9(3) / §9(4))",
    };

    /// <summary>The chosen inward-supply routing — Domestic (RCM by place of supply) or Import of services (always
    /// IGST, §5(3)). Changing it re-resolves applicability through the engine.</summary>
    [ObservableProperty] private RcmSupplyKindOption? _selectedRcmSupplyKind;

    /// <summary>
    /// §9(4) — true iff <b>we</b> (the recipient) are a real-estate <b>promoter</b>, the sole surviving §9(4) trigger
    /// (Notn 7/2019). Default false, matching the engine default, so the blanket §9(4) stays OFF (RQ-3).
    /// </summary>
    [ObservableProperty] private bool _rcmRecipientIsPromoter;

    /// <summary>True iff <b>we</b> (the recipient) are a <b>body corporate</b> — drives the recipient qualifier on the
    /// GTA / security / renting-of-motor-vehicle categories. Defaults to true, matching the engine default.</summary>
    [ObservableProperty] private bool _rcmRecipientIsBodyCorporate = true;

    /// <summary>Rule 47A — generate a <b>self-invoice</b> for this inward supply on accept. A <b>registered</b> supplier
    /// issues its own tax invoice, so the engine declines (and the message says so) rather than raising a bogus one.</summary>
    [ObservableProperty] private bool _generateRcmSelfInvoice;

    /// <summary>Rule 52 — generate a <b>payment voucher</b> for this reverse-charge supplier payment on accept.</summary>
    [ObservableProperty] private bool _generateRcmPaymentVoucher;

    /// <summary>
    /// True when the RCM panel is shown: GST is enabled, this is a plain-grid Purchase/Journal (never an item invoice),
    /// and the grid holds a complete <i>reverse-charge-applicable</i> expense (Dr) line plus a complete supplier (Cr)
    /// line. Off ⇒ the panel is hidden and the voucher posts byte-identically (ER-13).
    /// </summary>
    [ObservableProperty] private bool _showRcmPanel;

    /// <summary>"Yes — reverse charge applies" / "No — forward charge" for the panel header.</summary>
    [ObservableProperty] private string _rcmAppliesText = string.Empty;

    /// <summary>The matched notified category (or "Import of services — §5(3)"); empty when RCM does not apply.</summary>
    [ObservableProperty] private string _rcmCategoryText = string.Empty;

    /// <summary>The resolved integrated RCM rate for the panel (e.g. "18%"); empty when RCM does not apply.</summary>
    [ObservableProperty] private string _rcmRateText = string.Empty;

    /// <summary>The resolved place-of-supply routing ("Inter-State (IGST)" / "Intra-State (CGST+SGST)").</summary>
    [ObservableProperty] private string _rcmPosText = string.Empty;

    /// <summary>The self-accounted RCM tax (paisa-exact display) — the amount of BOTH legs of the dual pair. This is the
    /// <b>total</b> cash liability, Compensation Cess included (<see cref="RcmCessText"/> breaks the cess out).</summary>
    [ObservableProperty] private string _rcmTaxText = "0.00";

    /// <summary>True when this reverse-charge supply bears Compensation Cess — the cess line shows only then (ER-13).</summary>
    [ObservableProperty] private bool _showRcmCess;

    /// <summary>The self-accounted RCM Compensation Cess (paisa-exact display); "0.00" when the supply bears none.</summary>
    [ObservableProperty] private string _rcmCessText = "0.00";

    /// <summary>The one-line human summary of the dual leg shown under the panel figures.</summary>
    [ObservableProperty] private string _rcmSummary = string.Empty;

    /// <summary>One reverse-charge <b>leg</b>: a distinct <i>ReverseChargeApplicable</i> expense ledger (whose GST block
    /// drives applicability, the rate and the category) and the assessable value booked to it on this voucher.</summary>
    private readonly record struct RcmLeg(DomainLedger Expense, Money Taxable);

    /// <summary>The <b>shape</b> of a potential reverse-charge inward supply on the plain grid: <b>every</b> complete
    /// <i>ReverseChargeApplicable</i> expense/purchase <b>debit</b> leg (the supplier charges no tax, so each Dr expense
    /// IS its own assessable value), plus the complete <b>supplier</b> credit line they were bought from.
    /// <para>
    /// <see cref="Legs"/> is a SET, not a single leg: one supplier invoice routinely carries two notified heads (legal
    /// @18% + GTA @5%), and each attracts its own dual leg at its own rate. Taking only the first silently
    /// under-collected the cash-only §49(4) liability on the rest.
    /// </para></summary>
    private readonly record struct RcmShape(
        IReadOnlyList<RcmLeg> Legs, VoucherLineViewModel PartyLine, DomainLedger Party)
    {
        /// <summary>The total assessable value across every reverse-charge leg (the panel's headline base).</summary>
        public Money Taxable => Legs.Aggregate(Money.Zero, (a, l) => a + l.Taxable);
    }

    /// <summary>True when reverse charge could apply on this screen: GST is enabled and this is a plain-grid
    /// Purchase/Journal. The concrete applicability (an RCM-flagged expense Dr leg + a supplier Cr leg + a matching
    /// notified category on the date) is tested by <see cref="DetectRcmShape"/> + the engine's own
    /// <see cref="RcmService.Resolve"/>; absent either, the voucher posts byte-identically (ER-13).</summary>
    private bool RcmPossible =>
        _company.GstEnabled
        && !IsItemInvoice
        && _type.BaseType is VoucherBaseType.Purchase or VoucherBaseType.Journal;

    /// <summary>
    /// Whether a Cr-side ledger is a genuine <b>supplier</b>: it carries party GST details, or it sits under <b>Sundry
    /// Creditors</b> (the payables nature — the same test the rest of the app uses to identify a party, mirroring
    /// <see cref="PosBillingViewModel"/>'s Sundry-Debtors lookup).
    /// <para>
    /// This deliberately rejects "any complete credit line". A reverse-charge supply is a supply <i>from a supplier</i>;
    /// without one there is nothing to self-account against. Accepting any credit leg meant a plain accrual Journal
    /// (Dr Expense / Cr Outstanding Expenses) — which has no supplier on it at all — silently posted a cash-only §49(4)
    /// liability against an accrual head. A false posting on an ORDINARY voucher is the worst failure this screen has.
    /// </para>
    /// </summary>
    private bool IsSupplierLedger(DomainLedger l) =>
        l.PartyGst is not null
        || ClassificationRules.GroupIsUnder(l.GroupId, "Sundry Creditors", _company);

    /// <summary>True when the operator has <b>declined</b> reverse charge on this voucher via the "Not Applicable"
    /// sentinel — the mirror of <see cref="IsTdsDeclined"/>. The screen cannot know every reason a notified-looking
    /// inward supply is really forward charge, so the decline must exist and must post nothing.</summary>
    private bool IsRcmDeclined => SelectedRcmSupplyKind is { Kind: null };

    /// <summary>
    /// Detects the reverse-charge <b>shape</b> on the current plain grid (the panel-visibility gate): <b>every</b>
    /// complete debit leg whose ledger's GST block is flagged
    /// <see cref="StockItemGstDetails.ReverseChargeApplicable"/> — the master flag, exactly mirroring TDS's
    /// <c>TdsApplicable</c> gate — plus a complete <b>supplier</b> (Cr) line. A company with no RCM-flagged ledger, or a
    /// voucher with no supplier on it, never sees the panel (ER-13). Note the flag only makes the panel <i>visible</i>:
    /// whether RCM actually fires is the engine's call (a matching effective category + qualifiers).
    /// </summary>
    private RcmShape? DetectRcmShape()
    {
        if (!RcmPossible) return null;

        // The EXPENSE (Dr) legs drive applicability, the rate and the category — their GST block is what Resolve reads.
        // Grouped by ledger so one head booked across several lines is ONE dual leg on the summed value, while distinct
        // heads (each with its own notified rate) keep their own.
        var legs = Lines
            .Where(l => l.IsComplete && l.Side == DrCr.Debit
                        && l.SelectedLedger is { SalesPurchaseGst.ReverseChargeApplicable: true })
            .GroupBy(l => l.SelectedLedger!.Id)
            .Select(g => new RcmLeg(g.First().SelectedLedger!, new Money(g.Sum(l => l.ParsedAmount))))
            .Where(leg => leg.Taxable.Amount > 0m)
            .ToList();
        if (legs.Count == 0) return null;

        // The SUPPLIER (Cr) leg: prefer one carrying party GST details (its state code drives the intra/inter split);
        // else any genuine payables-nature party. No supplier ⇒ no shape (see IsSupplierLedger).
        var partyLine =
            Lines.FirstOrDefault(l => l.IsComplete && l.Side == DrCr.Credit && l.SelectedLedger is { PartyGst: not null })
            ?? Lines.FirstOrDefault(l => l.IsComplete && l.Side == DrCr.Credit
                                         && l.SelectedLedger is { } led && IsSupplierLedger(led));
        if (partyLine is null) return null;

        return new RcmShape(legs, partyLine, partyLine.SelectedLedger!);
    }

    /// <summary>Resolves reverse-charge applicability for ONE leg of a shape through the engine (pure; no posting, no
    /// company mutation) — the SAME <see cref="RcmService.Resolve"/> the dual-leg build calls internally (ER-4). Each
    /// leg resolves independently against the shape's supplier: its own category, its own rate.</summary>
    private RcmService.RcmResolution ResolveRcm(RcmShape shape, RcmLeg leg) =>
        _rcm.Resolve(
            leg.Expense.SalesPurchaseGst, shape.Party.PartyGst, item: null, leg.Expense, Date,
            SelectedRcmSupplyKind?.Kind ?? RcmService.SupplyKind.Domestic,
            RcmRecipientIsPromoter, RcmRecipientIsBodyCorporate);

    /// <summary>
    /// Refreshes the RCM panel from the engine's own <see cref="RcmService.Resolve"/> + the static
    /// <see cref="GstService.ComputeLineTax"/>.
    /// <para>
    /// <b>Why not preview through <see cref="RcmService.BuildReverseCharge"/>?</b> Because it is <i>not</i> pure: it
    /// lazily creates the "RCM Output {head}" ledgers (<see cref="GstService.EnsureRcmOutputLedger"/>). Previewing
    /// through it would mutate the company on every keystroke — conjuring RCM ledgers on a company that may never post
    /// an RCM voucher (an ER-13 break). Resolve + ComputeLineTax are <b>exactly</b> what BuildReverseCharge computes
    /// internally, so the previewed figures are the posted figures to the paisa (ER-4) with no side effect.
    /// </para>
    /// A no-op (panel hidden, figures cleared) when no RCM shape exists, so a non-RCM voucher is byte-identical (ER-13).
    /// </summary>
    private void UpdateRcmPanel()
    {
        if (_updatingRcm) return;
        _updatingRcm = true;
        try
        {
            if (DetectRcmShape() is not { } shape)
            {
                ShowRcmPanel = false;
                RcmAppliesText = string.Empty;
                RcmCategoryText = string.Empty;
                RcmRateText = string.Empty;
                RcmPosText = string.Empty;
                RcmTaxText = "0.00";
                ShowRcmCess = false;
                RcmCessText = "0.00";
                RcmSummary = string.Empty;
                return;
            }

            // The shape holds ⇒ the panel shows (the engine may still resolve "does not apply" — shown as such).
            // The default is DOMESTIC, never the decline sentinel: reverse charge is mandatory when a notified category
            // fires, so it must self-account unless the operator actively declines (mirrors the TDS default).
            ShowRcmPanel = true;
            SelectedRcmSupplyKind ??= RcmDomestic;

            if (IsRcmDeclined)
            {
                // Declined — show a zeroed, byte-identical-posting state while keeping the panel visible so the operator
                // can re-enable reverse charge (mirrors UpdateTdsPanel's declined branch).
                RcmAppliesText = "No — declined by the operator";
                RcmCategoryText = string.Empty;
                RcmRateText = string.Empty;
                RcmPosText = string.Empty;
                RcmTaxText = "0.00";
                ShowRcmCess = false;
                RcmCessText = "0.00";
                RcmSummary =
                    "Reverse charge declined — no self-accounting pair is posted and the supplier's own tax (if any) "
                    + "applies in the ordinary way. Pick a supply kind above to re-enable it.";
                return;
            }

            // Resolve EVERY leg (each has its own category and rate) and total what would actually post.
            var firing = shape.Legs
                .Select(leg => (Leg: leg, Res: ResolveRcm(shape, leg)))
                .Where(x => x.Res.Applies)
                .ToList();

            if (firing.Count == 0)
            {
                RcmAppliesText = "No — forward charge";
                RcmCategoryText = string.Empty;
                RcmRateText = string.Empty;
                RcmPosText = string.Empty;
                RcmTaxText = "0.00";
                ShowRcmCess = false;
                RcmCessText = "0.00";
                RcmSummary =
                    $"No notified reverse-charge category fires for this supply on {DateText} — the supplier charges "
                    + $"tax in the ordinary way. No self-accounting pair is posted.";
                return;
            }

            // The previewed figure must be the POSTED figure to the paisa (ER-4). BuildReverseCharge also resolves and
            // posts a Compensation-Cess pair, so previewing through ComputeLineTax alone understated the cash liability
            // — a preview that lies about the posting. The SAME dated resolver the builder uses is called here.
            // Only the AD-VALOREM mode is previewable: a per-unit (Specific / RSP-factor) cess needs a quantity the
            // plain grid does not carry, and the builder itself fail-fasts on it rather than posting a silent ₹0.
            var tax = Money.Zero;
            var cess = Money.Zero;
            foreach (var (leg, res) in firing)
            {
                tax += GstService.ComputeLineTax(leg.Taxable, res.RateBasisPoints, res.InterState).Total;
                if (_gst.ResolveCess(item: null, leg.Expense, Date, quantity: 0m) is { Mode: CessValuationMode.AdValorem } c)
                    cess += c.ComputeCess(leg.Taxable);
            }

            var total = tax + cess;
            var interState = firing[0].Res.InterState;
            var heads = interState ? "IGST" : "CGST+SGST";
            var scheme = firing[0].Res.Scheme == RcmItcScheme.ImportOfServices ? "GSTR-3B 4A(2)" : "GSTR-3B 4A(3)";

            RcmAppliesText = "Yes — reverse charge applies";
            // Import of services is reverse charge BY LAW (§5(3)) — the engine matches no category for it, so name the
            // statutory basis rather than leaving the operator staring at a blank category on a firing RCM. With several
            // notified heads on one voucher, each is named so the operator can see what was matched.
            RcmCategoryText = string.Join(" · ", firing
                .Select(x => x.Res.Category is { } cat
                    ? $"{cat.Label} ({cat.Notification})"
                    : SelectedRcmSupplyKind?.Kind == RcmService.SupplyKind.ImportOfServices
                        ? "Import of services — §5(3) IGST Act"
                        : string.Empty)
                .Where(s => s.Length > 0)
                .Distinct());
            // One rate is a rate; several heads at several rates is a blend, so name each rather than imply a single one.
            RcmRateText = string.Join(" / ", firing
                .Select(x => (x.Res.RateBasisPoints / 100m).ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) + "%")
                .Distinct());
            RcmPosText = interState ? "Inter-State (IGST)" : "Intra-State (CGST+SGST)";
            RcmTaxText = IndianFormat.AmountAlways(total.Amount);
            ShowRcmCess = cess.Amount != 0m;
            RcmCessText = IndianFormat.AmountAlways(cess.Amount);

            var taxableTotal = firing.Aggregate(Money.Zero, (a, x) => a + x.Leg.Taxable);
            var legNote = firing.Count > 1 ? $" across {firing.Count} reverse-charge legs" : string.Empty;
            var cessNote = ShowRcmCess
                ? $" (including Compensation Cess ₹{RcmCessText}, ring-fenced to its own head)"
                : string.Empty;
            RcmSummary =
                $"Self-accounted on ₹{IndianFormat.AmountAlways(taxableTotal.Amount)} @ {RcmRateText}{legNote}: "
                + $"Cr RCM Output {heads} ₹{RcmTaxText}{cessNote} — payable in CASH (§49(4) bars any ITC set-off against "
                + $"the reverse-charge liability) · Dr Input {heads} ₹{RcmTaxText} — the matching credit, claimed "
                + $"separately ({scheme}). The supplier charges no tax.";
        }
        finally
        {
            _updatingRcm = false;
        }
    }

    /// <summary>The operator changing the supply kind / the §9(4) promoter / body-corporate qualifiers re-resolves the
    /// applicability (guarded against the auto-default inside <see cref="UpdateRcmPanel"/>).</summary>
    partial void OnSelectedRcmSupplyKindChanged(RcmSupplyKindOption? value)
    {
        if (_updatingRcm) return;
        Recalculate();
    }

    partial void OnRcmRecipientIsPromoterChanged(bool value)
    {
        if (_updatingRcm) return;
        Recalculate();
    }

    partial void OnRcmRecipientIsBodyCorporateChanged(bool value)
    {
        if (_updatingRcm) return;
        Recalculate();
    }

    // =============================================================== §34 credit / debit note (catalog §12; Phase 9 slice 2b; RQ-24; ER-12; DP-27)

    /// <summary>
    /// True when this voucher <b>can</b> carry §34 GST details: a Credit-Note / Debit-Note on a GST company. The details
    /// themselves are opt-in (<see cref="IsSection34Note"/>) — not every note on a GST company is a §34 GST note (an
    /// inter-branch or exempt-supply adjustment is not), and the engine treats the link as an optional annotation whose
    /// absence keeps the reports byte-identical (ER-13).
    /// </summary>
    public bool CanBeSection34Note =>
        _company.GstEnabled && _type.BaseType is VoucherBaseType.CreditNote or VoucherBaseType.DebitNote;

    /// <summary>
    /// Opt-in: this note carries §34 GST details (RQ-24). Off ⇒ no <see cref="GstCreditDebitNoteLink"/> is created and
    /// the note posts exactly as it does today (ER-13). On ⇒ the original-invoice reference becomes <b>mandatory</b>
    /// (ER-12: a §34 note is never a free-floating tax delta) and the §34(2) cut-off is enforced.
    /// </summary>
    [ObservableProperty] private bool _isSection34Note;

    /// <summary>The §34 note direction, derived from the voucher's own base type — a Credit Note <b>reduces</b> the
    /// supplier's output tax (capped by the §34(2) 30-Nov cut-off); a Debit Note <b>increases</b> it (uncapped).</summary>
    public CdnType Section34Type =>
        _type.BaseType == VoucherBaseType.CreditNote ? CdnType.Credit : CdnType.Debit;

    /// <summary>True when the §34 detail fields (original-invoice picker, reason, 9B target, override) are shown.</summary>
    public bool ShowSection34Details => CanBeSection34Note && IsSection34Note;

    /// <summary>
    /// True when the §34(2) <b>Override</b> affordance is shown — on any liability-reducing <b>credit</b> note carrying
    /// §34 details.
    /// <para>
    /// Deliberately <b>not</b> gated on <see cref="CdnPastTimeLimit"/>: §34(2) also refuses a credit note whose original
    /// supply <i>date is unknown</i> (the cut-off cannot be verified), and in that state "past the limit" is false. Gating
    /// the override on it would leave the operator refused by a guard whose only stated escape is a control that is not
    /// on screen — the dead-guard defect UI-2 shipped three times.
    /// </para>
    /// </summary>
    public bool ShowCdnOverride => ShowSection34Details && Section34Type == CdnType.Credit;

    /// <summary>The original-invoice choices: a "(none)" sentinel (so the ER-12 guard can actually fire), a
    /// consolidated/unregistered reference option, then every posted Sales/Purchase invoice dated on or before this note.</summary>
    public ObservableCollection<CdnOriginalInvoiceOption> CdnOriginalInvoices { get; } = new();

    /// <summary>The chosen original invoice this note adjusts — the link GSTR-1 Table 9B / 9C reads.</summary>
    [ObservableProperty] private CdnOriginalInvoiceOption? _selectedCdnOriginalInvoice;

    /// <summary>The original invoice number, typed for a <b>consolidated / unregistered</b> reference (ER-12's second
    /// limb — used when no voucher link is available).</summary>
    [ObservableProperty] private string _cdnOriginalInvoiceNumber = string.Empty;

    /// <summary>The original supply date (dd-MMM-yyyy) for a consolidated reference — it drives the §34(2) FY basis.</summary>
    [ObservableProperty] private string _cdnOriginalInvoiceDateText = string.Empty;

    /// <summary>The standard §34 reason vocabulary the note is issued under (required by the link record).</summary>
    public ObservableCollection<string> CdnReasonCodes { get; } = new();

    /// <summary>The chosen §34 reason (e.g. "01 Sales return"); required when §34 details are on.</summary>
    [ObservableProperty] private string? _selectedCdnReasonCode;

    /// <summary>True ⇒ a registered-party note (GSTR-1 Table 9B); false ⇒ an unregistered CDN.</summary>
    [ObservableProperty] private bool _cdnIs9BTarget = true;

    /// <summary>Explicitly permit a credit note past the §34(2) 30-Nov cut-off (house style: the default blocks).</summary>
    [ObservableProperty] private bool _cdnOverrideTimeLimit;

    /// <summary>True when the typed consolidated-reference fields are shown (the consolidated option is chosen).</summary>
    [ObservableProperty] private bool _showCdnConsolidatedFields;

    /// <summary>True when the resolved note is past its §34(2) cut-off — drives the override affordance.</summary>
    [ObservableProperty] private bool _cdnPastTimeLimit;

    /// <summary>The §34 advisory shown under the picker (the 30-Nov cut-off, or why the note is refused).</summary>
    [ObservableProperty] private string _cdnSummary = string.Empty;

    /// <summary>
    /// Populates the §34 pickers. The candidates are the posted <b>Sales/Purchase</b> invoices (either nature can be the
    /// original supply a note adjusts), most recent first. Called once from the constructor; a no-op on a non-note type.
    /// </summary>
    private void BuildSection34Pickers()
    {
        if (!CanBeSection34Note) return;

        CdnOriginalInvoices.Clear();
        CdnOriginalInvoices.Add(new CdnOriginalInvoiceOption { Display = "◦ (none selected)" });
        CdnOriginalInvoices.Add(new CdnOriginalInvoiceOption
        {
            IsConsolidated = true,
            Display = "◦ Consolidated / unregistered — enter the reference",
        });
        foreach (var v in _company.Vouchers
                     .Where(v => _company.FindVoucherType(v.TypeId)?.BaseType
                         is VoucherBaseType.Sales or VoucherBaseType.Purchase)
                     .OrderByDescending(v => v.Date).ThenByDescending(v => v.Number))
            CdnOriginalInvoices.Add(new CdnOriginalInvoiceOption { Invoice = v, Display = CdnCandidateDisplay(v) });
        SelectedCdnOriginalInvoice = CdnOriginalInvoices.FirstOrDefault();

        // The standard GST §34 reason vocabulary (the link record requires a reason).
        CdnReasonCodes.Clear();
        foreach (var r in new[]
                 {
                     "01 Sales return",
                     "02 Post-supply discount",
                     "03 Deficiency in services",
                     "04 Correction in invoice",
                     "05 Change in place of supply",
                     "06 Finalisation of provisional assessment",
                     "07 Others",
                 })
            CdnReasonCodes.Add(r);
    }

    /// <summary>A one-line description of a candidate original invoice (type, number, date, party, value).</summary>
    private string CdnCandidateDisplay(Voucher v)
    {
        var typeName = _company.FindVoucherType(v.TypeId)?.Name ?? "Voucher";
        var party = v.PartyId is { } pid ? _company.FindLedger(pid)?.Name : null;
        var total = v.Lines.Where(l => l.Side == DrCr.Debit).Aggregate(Money.Zero, (a, l) => a + l.Amount);
        var partyPart = string.IsNullOrWhiteSpace(party) ? string.Empty : $" · {party}";
        return $"{typeName} No. {_company.FormatVoucherNumber(v)} · {v.Date:dd-MMM-yyyy}{partyPart} · ₹{IndianFormat.AmountAlways(total.Amount)}";
    }

    /// <summary>
    /// The resolved original-invoice reference (ER-12): a picked voucher contributes its id + number + date; the
    /// consolidated option contributes only what the operator typed. A "(none)" selection resolves to nothing — which is
    /// exactly what the Accept guard refuses on.
    /// </summary>
    private (Guid? VoucherId, string? Number, DateOnly? Date) ResolveCdnOriginal()
    {
        if (SelectedCdnOriginalInvoice is not { } opt || opt.IsNone) return (null, null, null);

        if (opt.Invoice is { } invoice)
            return (invoice.Id, _company.FormatVoucherNumber(invoice), invoice.Date);

        var number = string.IsNullOrWhiteSpace(CdnOriginalInvoiceNumber) ? null : CdnOriginalInvoiceNumber.Trim();
        // WI-5: the shared lenient day-first parser, so a typed original-invoice date accepts the same
        // spellings as every other date field in the app.
        DateOnly? date = ApexDate.TryParse(CdnOriginalInvoiceDateText, Date, out var parsed) ? parsed : null;
        return (null, number, date);
    }

    /// <summary>
    /// Refreshes the §34 advisory. The <b>30-November cut-off itself comes from the engine</b>
    /// (<see cref="CreditDebitNoteService.NovemberThirtyFollowing"/>, ER-4) — the screen never re-derives the Indian-FY
    /// rule. A debit note is uncapped (no issuance cut-off), so it simply says so.
    /// </summary>
    private void UpdateSection34Panel()
    {
        OnPropertyChanged(nameof(ShowSection34Details));
        OnPropertyChanged(nameof(ShowCdnOverride));
        ShowCdnConsolidatedFields = ShowSection34Details && SelectedCdnOriginalInvoice is { IsConsolidated: true };

        if (!ShowSection34Details)
        {
            CdnSummary = string.Empty;
            CdnPastTimeLimit = false;
            return;
        }

        var (voucherId, number, date) = ResolveCdnOriginal();
        if (voucherId is null && string.IsNullOrWhiteSpace(number))
        {
            CdnPastTimeLimit = false;
            CdnSummary = "Select the original invoice this note adjusts (or choose 'Consolidated…' and type the original "
                         + "invoice number) — a §34 note is never a free-floating tax delta.";
            return;
        }

        if (Section34Type == CdnType.Debit)
        {
            CdnPastTimeLimit = false;
            CdnSummary = "§34 debit note — increases the original supply's output tax. No §34(2) issuance cut-off applies "
                         + "to a debit note.";
            return;
        }

        // A liability-reducing credit note is capped by §34(2). Without the original supply date the cut-off cannot be
        // verified at all — refusing (rather than waving it through) mirrors the engine's own guard.
        if (date is not { } originalDate)
        {
            CdnPastTimeLimit = false;
            CdnSummary = "A liability-reducing §34 credit note needs the original supply date to verify the 30-November "
                         + "declaration cut-off — type the original invoice date (dd-MMM-yyyy).";
            return;
        }

        var deadline = CreditDebitNoteService.NovemberThirtyFollowing(originalDate);
        CdnPastTimeLimit = Date > deadline;
        CdnSummary = CdnPastTimeLimit
            ? $"§34(2): this credit note (dated {DateText}) is PAST the {deadline:dd-MMM-yyyy} declaration cut-off "
              + $"(30-November following the original supply's FY) — a liability-reducing credit note declared after the "
              + $"cut-off is not permitted. Tick Override to force."
            : $"§34(2): the declaration cut-off for the {originalDate:dd-MMM-yyyy} supply is {deadline:dd-MMM-yyyy} — "
              + $"this note is within the limit.";
    }

    partial void OnIsSection34NoteChanged(bool value) => UpdateSection34Panel();
    partial void OnSelectedCdnOriginalInvoiceChanged(CdnOriginalInvoiceOption? value) => UpdateSection34Panel();
    partial void OnCdnOriginalInvoiceNumberChanged(string value) => UpdateSection34Panel();
    partial void OnCdnOriginalInvoiceDateTextChanged(string value) => UpdateSection34Panel();

    /// <summary>
    /// Pre-validates the §34 details before the engine is touched (friendly refusals): the original-invoice reference
    /// (ER-12), the reason, and the §34(2) 30-Nov cut-off on a liability-reducing credit note. Returns false ⇒ Accept
    /// aborts with <see cref="Message"/> set. A no-op when §34 details are off (ER-13).
    /// </summary>
    private bool ValidateSection34()
    {
        if (!ShowSection34Details) return true;

        var (voucherId, number, date) = ResolveCdnOriginal();
        if (voucherId is null && string.IsNullOrWhiteSpace(number))
        {
            Message = "Select the original invoice this §34 note adjusts — or choose 'Consolidated / unregistered' and "
                      + "type the original invoice number. A §34 note is never a free-floating tax delta.";
            return false;
        }
        if (string.IsNullOrWhiteSpace(SelectedCdnReasonCode))
        {
            Message = "Select the §34 reason this credit/debit note is issued under.";
            return false;
        }

        // §34(2) applies only to a CREDIT note (it reduces the supplier's liability); debit notes are uncapped.
        if (Section34Type == CdnType.Credit && !CdnOverrideTimeLimit)
        {
            if (date is not { } originalDate)
            {
                Message = "A liability-reducing §34 credit note requires the original supply date to verify the §34(2) "
                          + "30-November declaration cut-off — type the original invoice date (dd-MMM-yyyy), or tick "
                          + "Override to bypass the check.";
                return false;
            }
            var deadline = CreditDebitNoteService.NovemberThirtyFollowing(originalDate);
            if (Date > deadline)
            {
                Message = $"Credit note dated {DateText} is past the §34(2) declaration cut-off of "
                          + $"{deadline:dd-MMM-yyyy} (30-November following the original supply's FY) — a "
                          + "liability-reducing credit note declared after the cut-off is not permitted (tick Override to force).";
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Registers the <see cref="GstCreditDebitNoteLink"/> for a just-posted §34 note (RQ-24) — the record GSTR-1 Table 9B
    /// and the amendment tables read.
    /// <para>
    /// <b>Why not <see cref="CreditDebitNoteService.BuildCreditDebitNote"/>?</b> Because it also <i>computes and returns
    /// the Output-tax legs</i> — and on this plain-grid screen the operator has already entered those legs by hand.
    /// Calling it would post the tax twice. The genuinely missing §34 essential here is the <b>link</b> (ER-12), so the
    /// link is created directly while the statutory §34(2) rule is still taken from the engine
    /// (<see cref="CreditDebitNoteService.NovemberThirtyFollowing"/>) rather than re-derived (ER-4).
    /// </para>
    /// </summary>
    private void RegisterSection34Link(Guid cdnVoucherId, Stack<Action> undo)
    {
        var (voucherId, number, date) = ResolveCdnOriginal();
        var link = new GstCreditDebitNoteLink(
            Guid.NewGuid(), cdnVoucherId, Section34Type, voucherId, number, date,
            SelectedCdnReasonCode!, CdnIs9BTarget);
        _company.AddCreditDebitNoteLink(link);
        // The link references the note voucher; if the save then fails and the voucher is unwound, this must go too.
        undo.Push(() => _company.RemoveCreditDebitNoteLink(link));
    }

    // =============================================================== GST on advances (catalog §12; Phase 9 slice 2b; RQ-25; Rule 50/51)

    /// <summary>The action a voucher type offers against an outstanding advance.</summary>
    public enum AdvanceAction
    {
        /// <summary>Neither — this voucher type does nothing to an advance.</summary>
        None,

        /// <summary>A Journal applies the advance to the tax invoice (→ GSTR-1 11B); the operator books
        /// <c>Dr Advance from customer / Cr Customer</c> and the suspense-releasing pair is appended.</summary>
        Adjust,

        /// <summary>A Payment returns the advance (Rule 51); the operator books <c>Dr Advance / Cr Bank</c> and the
        /// suspense-releasing pair is appended.</summary>
        Refund,
    }

    /// <summary>
    /// The <b>GST-on-advances</b> engine (Phase 9 slice 2b) — the SAME service the posting uses (ER-4). The screen never
    /// re-implements the maths; the live figures come from the pure <see cref="GstService.ComputeLineTax"/> the engine
    /// itself calls (see <see cref="UpdateAdvancePanel"/> for why the builder is never used to preview).
    /// </summary>
    private readonly AdvanceReceiptService _advance;

    // ---- (a) booking the advance on a Receipt voucher (Rule 50) ----

    /// <summary>True when this voucher <b>can</b> carry a GST advance: a Receipt on a GST company. Opt-in below.</summary>
    public bool CanBeAdvanceReceipt => _company.GstEnabled && _type.BaseType == VoucherBaseType.Receipt;

    /// <summary>Opt-in: this receipt is an <b>advance</b> against a future supply (RQ-25). Off ⇒ no advance record, no
    /// tax pair, no suspense ledger — an ordinary receipt posts exactly as before (ER-13).</summary>
    [ObservableProperty] private bool _isAdvanceReceipt;

    /// <summary>True when the advance-receipt fields are shown.</summary>
    public bool ShowAdvanceReceiptDetails => CanBeAdvanceReceipt && IsAdvanceReceipt;

    /// <summary>True ⇒ a <b>service</b> advance (GST due on receipt, §13); false ⇒ a <b>goods</b> advance, which is
    /// de-taxed by Notn 66/2017 — no tax pair and no 11A row.</summary>
    [ObservableProperty] private bool _advanceIsService = true;

    /// <summary>The <b>net (ex-tax)</b> advance the GST is computed on. Typed explicitly rather than back-derived from
    /// the receipt's gross: dividing a gross out by (1 + rate) does not generally land on a paisa, and the engine
    /// (rightly) refuses a non-paisa-exact advance — a silently-rounded base is exactly the kind of wrong number this
    /// screen must never invent.</summary>
    [ObservableProperty] private string _advanceAmountText = string.Empty;

    /// <summary>The integrated rate as a percentage (Rule-50 fallback 18% when the rate is not yet known).</summary>
    [ObservableProperty] private string _advanceRateText = "18";

    /// <summary>True ⇒ IGST; false ⇒ CGST+SGST. Rule 50 falls back to inter-state when the place of supply is unknown.</summary>
    [ObservableProperty] private bool _advanceInterState;

    /// <summary>The place-of-supply State/UT code recorded on the advance (optional).</summary>
    [ObservableProperty] private string _advancePlaceOfSupplyStateCode = string.Empty;

    /// <summary>The advance tax due on receipt (paisa-exact display); "0.00" for a de-taxed goods advance.</summary>
    [ObservableProperty] private string _advanceTaxText = "0.00";

    /// <summary>The gross the party actually remits = net advance + advance tax.</summary>
    [ObservableProperty] private string _advanceGrossText = "0.00";

    /// <summary>The one-line human summary of the advance shown under the figures.</summary>
    [ObservableProperty] private string _advanceSummary = string.Empty;

    // ---- (b) adjusting / refunding an outstanding advance (Rule 51; GSTR-1 11B) ----

    /// <summary>The action this voucher type offers against an outstanding advance — a Journal <b>adjusts</b> it against
    /// the tax invoice, a Payment <b>refunds</b> it. Every other type offers neither.</summary>
    public AdvanceAction AdvanceActionForType => _type.BaseType switch
    {
        VoucherBaseType.Journal => AdvanceAction.Adjust,
        VoucherBaseType.Payment => AdvanceAction.Refund,
        _ => AdvanceAction.None,
    };

    /// <summary>The outstanding (neither adjusted nor refunded) advances, plus a "(none)" sentinel. An already-adjusted
    /// advance is <b>absent</b> — the picker cannot offer a double adjustment in the first place.</summary>
    public ObservableCollection<AdvanceReceiptOption> OutstandingAdvances { get; } = new();

    /// <summary>The advance being adjusted / refunded by this voucher.</summary>
    [ObservableProperty] private AdvanceReceiptOption? _selectedOutstandingAdvance;

    /// <summary>The tax invoice an advance is being adjusted against (Adjust mode only) — the 11B anchor.</summary>
    public ObservableCollection<AdvanceInvoiceOption> AdvanceInvoices { get; } = new();

    /// <summary>The chosen tax invoice the advance is applied to.</summary>
    [ObservableProperty] private AdvanceInvoiceOption? _selectedAdvanceInvoice;

    /// <summary>True when the adjust/refund panel is shown: a GST company, a Journal (adjust) or Payment (refund), and
    /// at least one outstanding advance to act on. A company that never books an advance never sees it (ER-13).</summary>
    public bool ShowAdvanceActionPanel =>
        _company.GstEnabled
        && !IsItemInvoice
        && AdvanceActionForType != AdvanceAction.None
        && OutstandingAdvances.Any(o => !o.IsNone);

    /// <summary>True when the invoice picker is shown (adjusting, not refunding).</summary>
    public bool ShowAdvanceInvoicePicker =>
        ShowAdvanceActionPanel && AdvanceActionForType == AdvanceAction.Adjust;

    /// <summary>The adjust/refund panel caption + advisory.</summary>
    [ObservableProperty] private string _advanceActionSummary = string.Empty;

    /// <summary>
    /// Populates the advance pickers: the outstanding advances (never an adjusted/refunded one) and the candidate tax
    /// invoices. Called once from the constructor; a no-op on a type that offers no advance action.
    /// </summary>
    private void BuildAdvancePickers()
    {
        if (!_company.GstEnabled || AdvanceActionForType == AdvanceAction.None) return;

        OutstandingAdvances.Clear();
        OutstandingAdvances.Add(new AdvanceReceiptOption { Display = "◦ (none selected)" });
        foreach (var a in _company.AdvanceReceipts
                     .Where(a => a.AdjustedAgainstInvoiceVoucherId is null && a.RefundVoucherId is null))
            OutstandingAdvances.Add(new AdvanceReceiptOption { Receipt = a, Display = AdvanceDisplay(a) });
        SelectedOutstandingAdvance = OutstandingAdvances.FirstOrDefault();

        AdvanceInvoices.Clear();
        AdvanceInvoices.Add(new AdvanceInvoiceOption { Display = "◦ (none selected)" });
        foreach (var v in _company.Vouchers
                     .Where(v => _company.FindVoucherType(v.TypeId)?.BaseType == VoucherBaseType.Sales)
                     .OrderByDescending(v => v.Date).ThenByDescending(v => v.Number))
            AdvanceInvoices.Add(new AdvanceInvoiceOption { Invoice = v, Display = CdnCandidateDisplay(v) });
        SelectedAdvanceInvoice = AdvanceInvoices.FirstOrDefault();
    }

    /// <summary>A one-line description of an outstanding advance (its receipt voucher, kind, net amount and tax).</summary>
    private string AdvanceDisplay(GstAdvanceReceipt a)
    {
        // Kept compact: this string is shown inside a ComboBox, which ellipsizes — a longer label pushed the tax figure
        // out of sight. The full consequence is spelled out in AdvanceActionSummary underneath.
        var receipt = _company.FindVoucher(a.ReceiptVoucherId);
        var receiptPart = receipt is null ? "Advance" : $"Receipt {_company.FormatVoucherNumber(receipt)} · {receipt.Date:dd-MMM-yy}";
        var kind = a.IsService ? "service" : "goods";
        return $"{receiptPart} · {kind} · net ₹{IndianFormat.AmountAlways(a.AdvanceAmount.Amount)} · "
               + $"tax ₹{IndianFormat.AmountAlways(a.AdvanceTax.Amount)}";
    }

    /// <summary>The typed net advance, or null when blank/unparseable.</summary>
    private decimal? ParsedAdvanceAmount =>
        decimal.TryParse((AdvanceAmountText ?? string.Empty).Trim(),
            System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out var v)
            ? v
            : null;

    /// <summary>The typed rate as basis points (18 ⇒ 1800), or the Rule-50 fallback when blank/unparseable.</summary>
    private int ParsedAdvanceRateBasisPoints =>
        decimal.TryParse((AdvanceRateText ?? string.Empty).Trim(),
            System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out var pct)
            && pct >= 0m
            ? (int)Math.Round(pct * 100m, MidpointRounding.AwayFromZero)
            : AdvanceReceiptService.RuleFiftyFallbackRateBasisPoints;

    /// <summary>
    /// Refreshes the advance-receipt figures.
    /// <para>
    /// As with RCM, the preview goes through the pure static <see cref="GstService.ComputeLineTax"/> and <b>never</b>
    /// <see cref="AdvanceReceiptService.BuildAdvanceReceipt"/> — the builder lazily creates the "Output Tax on Advances"
    /// suspense ledger AND registers a <see cref="GstAdvanceReceipt"/> on the company, so previewing through it would
    /// conjure a suspense ledger and a phantom advance record on every keystroke. ComputeLineTax is exactly what the
    /// builder computes internally, so what is shown is what posts, to the paisa (ER-4).
    /// </para>
    /// </summary>
    private void UpdateAdvancePanel()
    {
        OnPropertyChanged(nameof(ShowAdvanceReceiptDetails));

        if (!ShowAdvanceReceiptDetails)
        {
            AdvanceTaxText = "0.00";
            AdvanceGrossText = "0.00";
            AdvanceSummary = string.Empty;
            return;
        }

        if (ParsedAdvanceAmount is not { } net || net <= 0m)
        {
            AdvanceTaxText = "0.00";
            AdvanceGrossText = "0.00";
            AdvanceSummary = "Enter the net (ex-tax) advance this receipt covers.";
            return;
        }

        // A goods advance is de-taxed (Notn 66/2017) — no tax pair, no 11A row.
        if (!AdvanceIsService)
        {
            AdvanceTaxText = "0.00";
            AdvanceGrossText = IndianFormat.AmountAlways(net);
            AdvanceSummary =
                "Goods advance — de-taxed (Notn 66/2017): no GST is due on receipt and no tax pair is posted. The "
                + "advance is recorded, but it raises no GSTR-1 11A row.";
            return;
        }

        var bp = ParsedAdvanceRateBasisPoints;
        var tax = GstService.ComputeLineTax(new Money(net), bp, AdvanceInterState);
        var heads = AdvanceInterState ? "IGST" : "CGST+SGST";

        AdvanceTaxText = IndianFormat.AmountAlways(tax.Total.Amount);
        AdvanceGrossText = IndianFormat.AmountAlways(net + tax.Total.Amount);
        AdvanceSummary =
            $"Service advance (§13 — GST due on receipt): Cr Output {heads} ₹{AdvanceTaxText} · Dr Output Tax on "
            + $"Advances ₹{AdvanceTaxText} (a self-balancing pair added on top of your receipt legs, so revenue is not "
            + $"inflated) → GSTR-1 11A. The party remits ₹{AdvanceGrossText} gross; the suspense is released (11B) when "
            + "the tax invoice adjusts this advance.";
    }

    /// <summary>Refreshes the adjust/refund advisory.</summary>
    private void UpdateAdvanceActionPanel()
    {
        OnPropertyChanged(nameof(ShowAdvanceActionPanel));
        OnPropertyChanged(nameof(ShowAdvanceInvoicePicker));

        if (!ShowAdvanceActionPanel || SelectedOutstandingAdvance is not { Receipt: { } adv })
        {
            AdvanceActionSummary = string.Empty;
            return;
        }

        var tax = IndianFormat.AmountAlways(adv.AdvanceTax.Amount);
        AdvanceActionSummary = AdvanceActionForType == AdvanceAction.Adjust
            ? $"Adjusting this advance releases the ₹{tax} suspense (Dr Output tax / Cr Output Tax on Advances) so the "
              + "invoice's own output tax is not double-counted → GSTR-1 11B. Book the ordinary application legs "
              + "(Dr Advance from customer / Cr the customer) on the grid; the release pair is appended automatically."
            : $"Refunding this advance (Rule 51) releases the ₹{tax} suspense and reverses the advance's output "
              + "recognition. Book the ordinary refund legs (Dr Advance from customer / Cr Bank) on the grid; the "
              + "release pair is appended automatically.";
    }

    partial void OnIsAdvanceReceiptChanged(bool value) => UpdateAdvancePanel();
    partial void OnAdvanceIsServiceChanged(bool value) => UpdateAdvancePanel();
    partial void OnAdvanceAmountTextChanged(string value) => UpdateAdvancePanel();
    partial void OnAdvanceRateTextChanged(string value) => UpdateAdvancePanel();
    partial void OnAdvanceInterStateChanged(bool value) => UpdateAdvancePanel();
    partial void OnSelectedOutstandingAdvanceChanged(AdvanceReceiptOption? value) => UpdateAdvanceActionPanel();

    /// <summary>
    /// Restores an advance record the engine mutated, after a rejected post. <see cref="AdvanceReceiptService"/> is
    /// <b>not</b> pure — <c>BuildAdvanceReceipt</c> registers a record and <c>AdjustAgainstInvoice</c>/<c>Refund</c>
    /// replace one — so a voucher the engine then refuses would otherwise leave a phantom or wrongly-adjusted advance
    /// behind on the in-memory company (and the next Accept would register a second one). This is the compensating undo.
    /// </summary>
    private void RestoreAdvance(GstAdvanceReceipt original)
    {
        if (_company.FindAdvanceReceipt(original.Id) is { } mutated) _company.RemoveAdvanceReceipt(mutated);
        _company.AddAdvanceReceipt(original);
    }

    /// <summary>Removes an advance record the engine registered, after a rejected post (the undo for a booked advance).</summary>
    private void UnregisterAdvance(GstAdvanceReceipt registered)
    {
        if (_company.FindAdvanceReceipt(registered.Id) is { } found) _company.RemoveAdvanceReceipt(found);
    }

    /// <summary>
    /// Generates the Rule-47A <b>self-invoice</b> and/or the Rule-52 <b>payment voucher</b> for a just-posted RCM
    /// voucher (RQ-8), returning the note to append to the accept message. Only ever called when reverse charge
    /// actually applied. A <b>registered</b> supplier issues its own tax invoice, so the engine returns <c>null</c> for
    /// the self-invoice — surfaced as an explanation rather than a silent no-op.
    /// <para>
    /// Both generators ADD a document to the company, so each pushes its compensating undo onto <paramref name="undo"/>:
    /// these documents link to the posted voucher id, and if the save then fails the voucher is unwound — a surviving
    /// document would point at a voucher that no longer exists (the same dangling-reference shape as the advance
    /// phantom this guard was built for).
    /// </para>
    /// </summary>
    private string GenerateRcmDocuments(Guid voucherId, RcmShape shape, Stack<Action> undo)
    {
        var notes = new List<string>();

        if (GenerateRcmSelfInvoice)
        {
            // Registered ⇔ the party carries GST details that are not B2C (a GSTIN + a Regular/Composition type).
            var supplierIsRegistered = shape.Party.PartyGst is { IsB2C: false };
            var doc = _rcm.GenerateSelfInvoice(voucherId, Date, Date, supplierIsRegistered, shape.Party.Id);
            if (doc is not null) undo.Push(() => _company.RemoveRcmDocument(doc));
            notes.Add(doc is null
                ? $"Self-invoice not raised — {shape.Party.Name} is registered and issues its own tax invoice (Rule 47A)."
                : $"Self-invoice No. {doc.SeriesNumber} generated (Rule 47A).");
        }

        if (GenerateRcmPaymentVoucher)
        {
            var doc = _rcm.GeneratePaymentVoucher(voucherId, Date, shape.Party.Id);
            undo.Push(() => _company.RemoveRcmDocument(doc));
            notes.Add($"Payment voucher No. {doc.SeriesNumber} generated (Rule 52).");
        }

        return notes.Count == 0 ? string.Empty : " " + string.Join(" ", notes);
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

        // WI-5: reject an UNREADABLE typed date up front rather than silently banking a null. A blank
        // instrument / bill due date legitimately means "none"; text that cannot be read does not, and
        // dropping it would post a voucher whose dates disagree with what the operator typed.
        var badLineDate = Lines.FirstOrDefault(l => l.HasUnreadableInstrumentDate);
        if (badLineDate is not null)
        {
            Message = ApexDate.ErrorFor(badLineDate.InstrumentDateText);
            return false;
        }

        var badDueDate = Lines.SelectMany(l => l.BillAllocations).FirstOrDefault(b => b.HasUnreadableDueDate);
        if (badDueDate is not null)
        {
            Message = ApexDate.ErrorFor(badDueDate.DueDateText);
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

        // §34 note essentials (RQ-24; ER-12): the original-invoice reference, the reason, and the §34(2) 30-Nov cut-off
        // on a liability-reducing credit note. A no-op unless the operator opted into §34 details (ER-13).
        if (!ValidateSection34()) return false;

        // The voucher id is minted up front: the GST-advance engine links its records to THIS voucher (a Rule-50 advance
        // record, or a Rule-51 refund), so the id must exist before the lines are built.
        var voucherId = Guid.NewGuid();

        // ---------------------------------------------------------------- the guarded mutation window
        // Everything PostAndSave does mutates the in-memory company through engines that are NOT pure: the advance engine
        // registers/replaces a GstAdvanceReceipt, Post appends the voucher, the RCM builder raises the Rule-47A/52
        // documents, the §34 link is registered. Each mutation pushes its compensating undo onto `undo`, and anything
        // short of an outright success unwinds the lot here — newest first.
        //
        // This is deliberately a WHOLE-WINDOW guard rather than a per-exit patch. The rollback used to run only from the
        // two engine-refusal catches, so the other five refusal exits leaked whatever the advance engine had already
        // registered. The narrowest of them was the deadliest: a GOODS advance is de-taxed (Notn 66/2017), so the engine
        // registers the record and hands back NO tax lines — the "needs at least two lines" gate then refused with the
        // record already on the company. That phantom pointed at a voucher id that was never posted, and
        // gst_advance_receipts.receipt_voucher_id is NOT NULL REFERENCES vouchers(id), so the operator doing exactly what
        // the refusal message asked (add the missing leg, Accept again) hit a FOREIGN KEY violation that escaped Accept
        // uncaught, lost the legitimate voucher, and bricked every save for the rest of the session.
        //
        // A no-op on the ordinary voucher: nothing is pushed, so a plain post is byte-identical (ER-13).
        var undo = new Stack<Action>();
        var committed = false;
        try
        {
            committed = PostAndSave(voucherId, undo);
            return committed;
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
        finally
        {
            if (!committed)
                while (undo.Count > 0) undo.Pop().Invoke();
        }
    }

    /// <summary>
    /// The mutating half of <see cref="Accept"/>, run inside that method's rollback guard: derives the withholding /
    /// reverse-charge / advance legs, posts the voucher, raises its RCM documents + §34 link, and saves the aggregate.
    /// Every company mutation pushes a compensating undo onto <paramref name="undo"/>, so the caller can unwind the
    /// whole window on ANY non-success exit. Returns false ⇒ refused with <see cref="Message"/> set; may throw
    /// <see cref="UnbalancedVoucherException"/> / <see cref="InvalidVoucherException"/> (the caller relays both).
    /// </summary>
    private bool PostAndSave(Guid voucherId, Stack<Action> undo)
    {
        // GST on advances (RQ-25). All three actions come from the SAME engine the panel previewed (ER-4), and all three
        // MUTATE the company (registering / replacing a GstAdvanceReceipt), so each pushes a compensating undo.
        var advanceLines = new List<EntryLine>();
        if (!BuildAdvanceLines(voucherId, advanceLines, undo)) return false;

        // TDS withholding carve-out (Phase 7 slice 2): when a deductee party + expense line are on the grid, the
        // party's Cr leg is replaced with the DERIVED net (gross − TDS) and a TDS-Payable Cr leg is appended — via
        // the SAME TdsService.BuildCarveOut the panel showed (ER-4), so gross Dr == net Cr + TDS Cr by construction
        // and VoucherValidator accepts the carve-out. Null (no TDS) ⇒ every line posts verbatim (byte-identical,
        // ER-13). Below threshold ⇒ the party is credited the full gross carrying the assessment detail (TDS 0).
        TdsService.CarveOut? carve = null;
        var tds = DetectTdsContext();
        if (tds is { } tctx)
        {
            try
            {
                carve = _tds.BuildCarveOut(tctx.Gross, AssessableExGst(), tctx.Nature, tctx.Deductee, Date);
            }
            catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
            {
                Message = $"Cannot compute TDS: {ex.Message}";
                return false;
            }
        }

        // Reverse-charge dual legs (Phase 9 slice 2): for EVERY RCM-flagged expense leg on the grid (paired with the
        // resolved supplier leg) that the engine resolves as firing, the self-accounting pair — Cr "RCM Output {head}"
        // (the cash-only §49(4) liability) + Dr "Input {head}" (the matching credit) — is appended on top of the
        // ordinary purchase legs. Each pair is the SAME amount on both sides, so it is self-balancing and the grid's own
        // balance is untouched. Resolution is checked FIRST (pure) so the builder — which lazily creates the RCM ledgers
        // — is never touched on a supply that does not attract reverse charge (ER-13).
        //
        // One pair PER LEG, never just the first: a single supplier invoice routinely carries two notified heads (legal
        // @18% + GTA @5%), and taking Lines.FirstOrDefault silently under-collected the §49(4) liability on the rest —
        // no warning, no refusal, Accept reporting success.
        var rcmPostings = new List<RcmService.RcmPosting>();
        var rcmShape = DetectRcmShape();
        if (rcmShape is { } rs && !IsRcmDeclined)
        {
            foreach (var leg in rs.Legs)
            {
                if (!ResolveRcm(rs, leg).Applies) continue;
                try
                {
                    rcmPostings.Add(_rcm.BuildReverseCharge(
                        leg.Taxable, item: null, leg.Expense, rs.Party.PartyGst, Date,
                        SelectedRcmSupplyKind?.Kind ?? RcmService.SupplyKind.Domestic,
                        RcmRecipientIsPromoter, RcmRecipientIsBodyCorporate));
                }
                catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
                {
                    Message = $"Cannot compute reverse charge on '{leg.Expense.Name}': {ex.Message}";
                    return false;
                }
            }
        }
        var rcmApplies = rcmPostings.Any(p => p.Applies);

        var entryLines = Lines
            .Where(l => l.IsComplete)
            .Select(l =>
            {
                // The deductee party leg is carved to NET (carrying the withholding detail); everything else verbatim.
                if (carve is { } cv && tds is { } t && ReferenceEquals(l, t.PartyLine))
                    return cv.PartyLine;

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

        // Append the TDS-Payable credit leg (only when the threshold was crossed).
        if (carve is { TdsPayableLine: { } payableLine })
            entryLines.Add(payableLine);

        // Append every reverse-charge self-accounting pair (each self-balancing, so the voucher stays balanced).
        foreach (var rcmPosting in rcmPostings)
            entryLines.AddRange(rcmPosting.Lines);

        // Append the GST-advance pair — the tax-on-advance pair (Rule 50) or the suspense-releasing reversal
        // (adjustment / Rule-51 refund). Self-balancing, so the grid's own balance is untouched.
        entryLines.AddRange(advanceLines);

        if (entryLines.Count < 2)
        {
            Message = "A voucher needs at least two lines.";
            return false;
        }

        // A Reversing Journal must carry a valid "Applicable Upto" date (on/after the voucher date).
        DateOnly? applicableUpto = null;
        if (IsReversing)
        {
            if (!ApexDate.TryParse(ApplicableUptoText, Date, out var upto))
            {
                Message = ApexDate.ErrorFor(ApplicableUptoText);
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
            voucherId,
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

        var posted = _service.Post(voucher); // throws on unbalanced/invalid — never persisted
        undo.Push(() => _company.RemoveVoucher(posted));

        // Rule-47A self-invoice / Rule-52 payment voucher (RQ-8) — only for a voucher that actually carries a
        // reverse-charge pair, and only once the post has succeeded (the documents link to the posted voucher id).
        // Generated BEFORE the save so they persist with the voucher in one aggregate write.
        var rcmDocNote = rcmApplies && rcmShape is { } shapeForDocs
            ? GenerateRcmDocuments(posted.Id, shapeForDocs, undo)
            : string.Empty;

        // The §34 link (already pre-validated by ValidateSection34) — registered against the posted note id, and
        // persisted with it in the same aggregate write below.
        if (ShowSection34Details) RegisterSection34Link(posted.Id, undo);

        // The save is INSIDE the guarded window on purpose. A store refusal (a constraint violation, a locked/missing
        // file, a full disk) must never escape Accept as a raw exception: the finally unwinds every mutation above —
        // voucher, documents, §34 link, advance record — so the in-memory company matches the .db that was never
        // written, and the operator gets a message instead of a crash with a company that can no longer be saved.
        try
        {
            _storage.Save(_company);         // persist the whole aggregate to the .db
        }
        catch (Exception ex)
        {
            Message = $"Could not save the company: {ex.Message} The voucher was not kept — nothing was changed.";
            return false;
        }

        SavedNumber = posted.Number;
        Message = $"{_type.Name} No. {_company.FormatVoucherNumber(posted)} accepted.{rcmDocNote}";
        _onSaved();
        return true;
    }

    /// <summary>
    /// Builds the GST-advance entry lines for this voucher (RQ-25) and hands back the compensating undo for the
    /// company mutation the engine performs. Three mutually-exclusive shapes:
    /// <list type="bullet">
    ///   <item><b>Receipt + advance opt-in</b> → <see cref="AdvanceReceiptService.BuildAdvanceReceipt"/>: the Rule-50
    ///     tax-on-advance pair (empty for a de-taxed goods advance) + a registered record.</item>
    ///   <item><b>Journal + an outstanding advance</b> → <see cref="AdvanceReceiptService.AdjustAgainstInvoice"/>: the
    ///     suspense-releasing reversal → GSTR-1 11B.</item>
    ///   <item><b>Payment + an outstanding advance</b> → <see cref="AdvanceReceiptService.Refund"/> (Rule 51).</item>
    /// </list>
    /// Returns false ⇒ Accept aborts with <see cref="Message"/> set. A no-op (no lines, no undo) when no advance is in
    /// play, so an ordinary receipt/journal/payment posts byte-identically (ER-13).
    /// <para>
    /// Each engine call that mutates the company pushes its compensating undo onto <paramref name="undo"/> IMMEDIATELY,
    /// before this method can take any further refusal exit — the mutation and its undo are never separated.
    /// </para>
    /// </summary>
    private bool BuildAdvanceLines(Guid voucherId, List<EntryLine> lines, Stack<Action> undo)
    {
        // ---- (a) booking a Rule-50 advance on this Receipt ----
        if (ShowAdvanceReceiptDetails)
        {
            if (ParsedAdvanceAmount is not { } net || net <= 0m)
            {
                Message = "Enter the net (ex-tax) advance amount this receipt covers.";
                return false;
            }

            AdvanceReceiptService.AdvanceReceiptPosting posting;
            try
            {
                posting = _advance.BuildAdvanceReceipt(
                    voucherId, AdvanceIsService, new Money(net), ParsedAdvanceRateBasisPoints, AdvanceInterState,
                    string.IsNullOrWhiteSpace(AdvancePlaceOfSupplyStateCode)
                        ? null
                        : AdvancePlaceOfSupplyStateCode.Trim());
            }
            catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
            {
                Message = $"Cannot book the advance: {ex.Message}";
                return false;
            }

            lines.AddRange(posting.TaxLines);
            var registered = posting.Receipt;
            undo.Push(() => UnregisterAdvance(registered));
            return true;
        }

        // ---- (b) adjusting / refunding an outstanding advance ----
        if (!ShowAdvanceActionPanel || SelectedOutstandingAdvance is not { Receipt: { } picked }) return true;

        // The picker holds the advance record as it was when THIS screen opened. That snapshot must never be handed to
        // the engine: an adjustment/refund replaces the record with a NEW object (same identity), leaving the snapshot
        // frozen in its original, still-outstanding-looking state. So if another screen adjusted the advance meanwhile,
        // the engine's own guards — which read the object passed in — would see "not yet adjusted" and wave the second
        // adjustment straight through; worse, the record it then tries to replace is no longer in the collection, so the
        // remove no-ops and the add leaves TWO records sharing one id (which the store rejects outright on save).
        // Re-resolving by id against the live company makes the guards read CURRENT state and fire correctly.
        var advance = _company.FindAdvanceReceipt(picked.Id);
        if (advance is null)
        {
            Message = "That advance receipt no longer exists — reopen this voucher to refresh the list.";
            return false;
        }

        // The undo is armed BEFORE the engine is asked to adjust/refund, so the mutation can never outlive a later
        // refusal. Restoring an unmutated record is a harmless same-object swap, so arming early costs nothing.
        undo.Push(() => RestoreAdvance(advance));

        try
        {
            if (AdvanceActionForType == AdvanceAction.Adjust)
            {
                if (SelectedAdvanceInvoice is not { Invoice: { } invoice })
                {
                    Message = "Select the tax invoice this advance is being adjusted against.";
                    return false;
                }
                lines.AddRange(_advance.AdjustAgainstInvoice(advance, invoice.Id));
            }
            else
            {
                lines.AddRange(_advance.Refund(advance, voucherId));
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            // e.g. an advance already adjusted / already refunded (a stale picker), or a partial adjustment the S2b
            // engine refuses. Surface the engine's own explanation rather than crashing.
            var verb = AdvanceActionForType == AdvanceAction.Adjust ? "adjust" : "refund";
            Message = $"Cannot {verb} the advance: {ex.Message}";
            return false;
        }

        return true;
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

        // Price-Level header choices (slice 5; RQ-30): "Not Applicable" first, then every defined level. Populated
        // regardless of the flag (cheap); the header field itself is gated by ShowPriceLevelSelector.
        PriceLevelOptions.Clear();
        PriceLevelOptions.Add(new PriceLevelSelectorOption { Level = null, Display = "◦ Not Applicable" });
        foreach (var lvl in _company.PriceLevels.OrderBy(l => l.Name, StringComparer.OrdinalIgnoreCase))
            PriceLevelOptions.Add(new PriceLevelSelectorOption { Level = lvl, Display = lvl.Name });
        SelectedPriceLevel = PriceLevelOptions.FirstOrDefault();

        // Seed one blank item line so the grid is ready to type into the moment the mode is turned on.
        if (InventoryLines.Count == 0) AddInventoryLine();
        RecalculateItemInvoice();
    }

    /// <summary>
    /// WI-1 — re-reads the company's ledgers into the party / stock-leg pickers WITHOUT disturbing the
    /// in-progress voucher, so a ledger created on the fly (Alt+C) is immediately selectable in the field that
    /// created it. <see cref="BuildItemInvoicePickers"/> cannot be reused here: it RESETS both selections to the
    /// first row and seeds a blank item line — on a half-typed invoice that is itself data loss.
    /// <para>The current selections are re-resolved by ledger id (not by object identity of the wrapper), so a
    /// party already chosen stays chosen across the refresh.</para>
    /// </summary>
    public void RefreshMasterPickers()
    {
        var partyId = SelectedParty?.Ledger?.Id;
        var stockLedgerId = SelectedStockLedger?.Id;

        Parties.Clear();
        Parties.Add(new PartyOption { Ledger = null, Display = "◦ (none)" });
        foreach (var l in _company.Ledgers.OrderBy(l => l.Name, StringComparer.OrdinalIgnoreCase))
            Parties.Add(new PartyOption { Ledger = l, Display = l.Name });
        SelectedParty = Parties.FirstOrDefault(p => p.Ledger?.Id == partyId) ?? Parties.FirstOrDefault();

        StockLedgers.Clear();
        foreach (var l in _company.Ledgers
                     .Where(IsStockLegLedger)
                     .OrderBy(l => l.Name, StringComparer.OrdinalIgnoreCase))
            StockLedgers.Add(l);
        SelectedStockLedger = StockLedgers.FirstOrDefault(l => l.Id == stockLedgerId)
                              ?? StockLedgers.FirstOrDefault();
    }

    /// <summary>Pushes the Price-Level Discount-column gate to every item line so it shows/hides in sync (ER-13).</summary>
    private void SyncPriceLevelOnLines()
    {
        var on = ShowPriceLevelSelector;
        foreach (var l in InventoryLines) l.ShowDiscount = on;
    }

    partial void OnSelectedPriceLevelChanged(PriceLevelSelectorOption? value)
    {
        // A new header level re-resolves every un-dirtied line's auto-fill (a user override sticks; RQ-30).
        RecalculateItemInvoice();
    }

    /// <summary>
    /// The Price-Level auto-fill (slice 5; RQ-30). For each item line with an item + a positive quantity, resolves
    /// the slab for (SelectedPriceLevel, item, qty, VoucherDate) and stamps the Rate (+ Discount %) into the line —
    /// but ONLY when the line has not been operator-dirtied (the "auto-fill clobbers the manual edit" trap). A
    /// no-op when the feature is off / no level is chosen ("Not Applicable") / no slab resolves — the line then
    /// keeps whatever the operator typed. Re-entrancy-guarded (stamping raises change notifications that re-enter
    /// this via RecalculateItemInvoice).
    /// </summary>
    private void RefreshPriceLevelDefaults()
    {
        if (_refreshingPrices) return;
        if (!ShowPriceLevelSelector || SelectedPriceLevel is not { Level: { } level }) return;

        _refreshingPrices = true;
        try
        {
            foreach (var l in InventoryLines)
            {
                if (l.SelectedItem is not { } item) continue;
                var qty = l.ParsedActualQuantity;
                if (qty <= 0m) continue;

                var resolved = PriceResolver.Resolve(_company, level.Id, item.Id, qty, Date);
                if (resolved is { } price)
                {
                    var rateText = IndianFormat.AmountAlways(price.Rate.Amount);
                    var discountText = price.DiscountPercent > 0m
                        ? price.DiscountPercent.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)
                        : string.Empty;
                    l.ApplyPriceAutoFill(rateText, discountText);
                }
                else
                {
                    // No slab resolves for this (level, item, qty, date) — clear any auto-fill previously stamped on
                    // this un-dirtied line, so a stale Rate/Discount belonging to a different item or level never
                    // lingers (e.g. switching the line to an item with no price list). The operator's own edit still
                    // sticks: ApplyPriceAutoFill writes only when the field is not user-dirty.
                    l.ApplyPriceAutoFill(string.Empty, string.Empty);
                }
            }
        }
        finally
        {
            _refreshingPrices = false;
        }
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

    partial void OnSelectedPartyChanged(PartyOption? value)
    {
        // Default the Price-Level header from the party's default level (slice 5; RQ-30), still overridable. Only
        // when the feature is on; otherwise the header is inert. Assigning re-runs the auto-fill via its handler.
        if (ShowPriceLevelSelector)
        {
            // Always reset the header to the NEW party's default level — a party with no default resets it to
            // "Not Applicable" rather than silently inheriting the previously selected party's level (RQ-30).
            var match = value?.Ledger?.DefaultPriceLevelId is { } levelId
                ? PriceLevelOptions.FirstOrDefault(o => o.Level?.Id == levelId)
                : null;
            SelectedPriceLevel = match ?? PriceLevelOptions.FirstOrDefault(o => o.IsNotApplicable);
        }
        RecalculateItemInvoice();
    }
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
        OnPropertyChanged(nameof(IsTcsSalesInvoice));
        OnPropertyChanged(nameof(ShowAdditionalCosts));
        OnPropertyChanged(nameof(ShowActualBilledColumns));
        OnPropertyChanged(nameof(QuantityHeader));
        OnPropertyChanged(nameof(ShowPriceLevelSelector));
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
        // WI-10 Gap 2: hand the line the company's units so its picker can offer the item's base unit plus every
        // compound unit that reduces to it. Without this argument the picker's option list is empty, ShowUnit is
        // false and the column never appears — which is exactly the state the item-invoice grid was in before
        // this slice, and why the CA's "2 Dozen @ ₹10" invoice line was unreachable.
        var line = new InventoryVoucherLineViewModel(
            InventoryLineKind.Movement, StockItems, Godowns, RecalculateItemInvoice, _company.Units)
        {
            ShowActualBilled = CanBeItemInvoice && _company.UseSeparateActualBilledQuantity,
            ShowDiscount = ShowPriceLevelSelector,
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
                // Value derives from the NET (after Price-Level discount) rate — equals the raw rate when no
                // discount/column, so a non-price-level line is byte-identical (DP-A; ER-13).
                if (l.IsComplete && l.EffectiveRate is { } rate)
                    sum += Money.ForexBase(rate, l.ParsedBilledQuantity).Amount;
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
            // billed quantity, and a zero-valued (rate 0) free line is skipped above so it bears no GST. The
            // value uses the NET (after Price-Level discount) rate (DP-A); equals raw when no discount (ER-13).
            var lineValue = Money.ForexBase(l.EffectiveRate ?? new Money(rate), l.ParsedBilledQuantity);

            // Phase 9 slice 1: resolve the rate AS OF the voucher Date so a supply before 22-Sep-2025 resolves the
            // legacy rate and one on/after resolves the GST 2.0 rate (the dated override only fires when the item's
            // HSN matches a dated rate-history row — else byte-identical to Phase-4/8, ER-13).
            var res = _gst.ResolveRate(l.SelectedItem, valueLedger, Date);
            if (GstService.IsUnresolved(res))
                return new ItemInvoiceGst(EmptyInvoiceTax(), interState, l.SelectedItem);
            if (!res.IsTaxable) continue; // Exempt/Nil/Non-GST ⇒ no tax
            // Resolve the ring-fenced Compensation Cess for this line as of the same Date (null ⇒ no cess ⇒
            // byte-identical when off). Specific/RSP cess needs the billed quantity; ad-valorem uses the value.
            var cess = _gst.ResolveCess(l.SelectedItem, valueLedger, Date, l.ParsedBilledQuantity);
            taxable.Add(new GstService.TaxableLine(lineValue, res.RateBasisPoints, cess));
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

    // =============================================================== TCS additive collection (catalog §13; Phase 7 slice 5)

    /// <summary>The outcome of assessing the current Sales item-invoice for TCS (for both display and posting): the
    /// per-nature collection posts (one per resolved §206C nature — collected or below-threshold), the total TCS
    /// collected, and the display fields for the band (single-nature ⇒ its code/rate; mixed ⇒ "Multiple").</summary>
    private readonly record struct ItemInvoiceTcs(
        IReadOnlyList<TcsService.CollectionPost> Posts, Money TotalTcs, string DisplayCode, string DisplayRate,
        int NatureCount, string CollecteeName)
    {
        /// <summary>True iff any nature crossed its §206C threshold so TCS was actually collected.</summary>
        public bool AnyCollected => TotalTcs.Amount > 0m;
    }

    /// <summary>
    /// Computes the additive TCS for the current Sales item-invoice via the SAME <see cref="TcsService"/> the posting
    /// uses (ER-4). <b>Goods-driven</b> (the S2 lesson applied to TCS): each complete, positively-rated item line's
    /// §206C <see cref="NatureOfGoods"/> comes from the STOCK ITEM (or the sales ledger), never the party; a line whose
    /// nature is the legacy §206C(1H) is skipped for dates ≥ 01-Apr-2025 (the year-gate). The <b>party</b> supplies
    /// only PAN/rate (PAN ⇒ with-PAN; no-PAN ⇒ §206CC higher rate) + the collectee gate. Lines are grouped by nature;
    /// each group's assessable base is its Σ value plus — per the nature's <see cref="NatureOfGoods.BaseIncludesGst"/>
    /// flag — its GST (computed by the SAME <see cref="GstService"/> engine, so it matches the invoice's Output tax to
    /// the paisa). Returns <c>null</c> when TCS is not wired in (off / a Purchase / no collectee / no TCS-applicable
    /// line) so the sale is byte-identical (ER-13).
    /// </summary>
    private ItemInvoiceTcs? ComputeItemInvoiceTcs()
    {
        if (!IsTcsSalesInvoice) return null;
        if (SelectedParty?.Ledger is not { CollecteeType: not null } collectee) return null;

        var salesLedger = SelectedStockLedger;
        var interState = _gst.IsInterState(collectee.PartyGst?.StateCode);

        // Group the complete, positively-rated item lines by their resolved, date-selectable §206C nature.
        var order = new List<NatureOfGoods>();
        var value = new Dictionary<Guid, decimal>();
        var taxable = new Dictionary<Guid, List<GstService.TaxableLine>>();
        foreach (var l in InventoryLines.Where(l => l.IsComplete))
        {
            if (l.ParsedRate is not { } rate || rate <= 0m) continue;
            var nature = _tcs.ResolveNature(l.SelectedItem, salesLedger);
            if (nature is null || !nature.IsSelectableOn(Date)) continue; // non-TCS line / legacy year-gated ⇒ skip

            if (!value.ContainsKey(nature.Id)) { order.Add(nature); value[nature.Id] = 0m; taxable[nature.Id] = new(); }
            var lineValue = Money.ForexBase(l.EffectiveRate ?? new Money(rate), l.ParsedBilledQuantity);
            value[nature.Id] += lineValue.Amount;

            // The GST attributable to this line (for the base-incl-GST natures) — only for a GST-taxable line.
            // Resolve the rate as of the voucher Date so the TCS-on-GST base tracks the dated rate too (Phase 9 S1).
            if (IsGstInvoice)
            {
                var res = _gst.ResolveRate(l.SelectedItem, salesLedger, Date);
                if (!GstService.IsUnresolved(res) && res.IsTaxable)
                    taxable[nature.Id].Add(new GstService.TaxableLine(lineValue, res.RateBasisPoints));
            }
        }

        if (order.Count == 0) return null; // no TCS-applicable line ⇒ byte-identical sale (ER-13)

        var posts = new List<TcsService.CollectionPost>(order.Count);
        var total = 0m;
        foreach (var nature in order)
        {
            var groupGst = IsGstInvoice && taxable[nature.Id].Count > 0
                ? _gst.ComputeInvoiceTax(taxable[nature.Id], interState, GstTaxDirection.Output).TotalTax
                : Money.Zero;
            var post = _tcs.BuildCollection(new Money(value[nature.Id]), groupGst, nature, collectee, Date);
            posts.Add(post);
            total += post.TcsAmount.Amount;
        }

        // Display: a single nature shows its code + rate; a mixed invoice shows "Multiple" (the total still foots).
        string code, rateText;
        if (order.Count == 1)
        {
            var col = posts[0].Collection;
            code = order[0].CollectionCode;
            rateText = (col.RateBasisPoints / 100m).ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)
                       + "%" + (col.PanApplied ? string.Empty : " (No PAN)");
        }
        else
        {
            code = $"Multiple ({order.Count})";
            rateText = string.Empty;
        }

        return new ItemInvoiceTcs(posts, new Money(total), code, rateText, order.Count, collectee.Name);
    }

    /// <summary>Refreshes the TCS band from a computed <see cref="ItemInvoiceTcs"/> (or clears it when null); shown
    /// only on a TCS-aware Sales item-invoice to a collectee with a TCS-applicable line (ER-13 when off).</summary>
    private void UpdateTcsDisplay(ItemInvoiceTcs? tcs)
    {
        if (tcs is not { } t)
        {
            ShowTcs = false;
            TcsCollectionCodeText = string.Empty;
            TcsRateText = string.Empty;
            TcsAmountText = "0.00";
            TcsSummary = string.Empty;
            return;
        }

        ShowTcs = true;
        TcsCollectionCodeText = t.DisplayCode;
        TcsRateText = t.DisplayRate;
        TcsAmountText = IndianFormat.AmountAlways(t.TotalTcs.Amount);
        TcsSummary = t.AnyCollected
            ? (t.NatureCount == 1
                ? $"TCS {t.DisplayCode} @ {t.DisplayRate}: ₹{TcsAmountText} collected on top from {t.CollecteeName} " +
                  $"(added to the party total)."
                : $"TCS on {t.NatureCount} natures of goods: ₹{TcsAmountText} collected on top from {t.CollecteeName} " +
                  $"(added to the party total).")
            : $"{t.DisplayCode}: below threshold — no TCS collected from {t.CollecteeName}.";
    }

    /// <summary>
    /// Recomputes the item-invoice indicators: the running items total, the derived Dr/Cr summary line, and —
    /// while in item-invoice mode — whether Accept is allowed (a party + a value ledger picked, ≥ 1 complete
    /// item line each with a positive rate, and no half-filled row). When GST is enabled it also recomputes the
    /// live tax totals (CGST/SGST/IGST) and the party total (taxable + tax) so the screen reflects the tax.
    /// </summary>
    public void RecalculateItemInvoice()
    {
        // Price Levels (slice 5; RQ-30): keep the per-line Discount column gate in sync, then auto-fill each
        // un-dirtied line's Rate/Discount from the resolver BEFORE the totals are computed (so they reflect the
        // stamped values). Both are no-ops when the feature is off, so a non-price-level screen is unchanged.
        SyncPriceLevelOnLines();
        RefreshPriceLevelDefaults();

        var total = ItemsTotal;
        ItemsTotalText = IndianFormat.AmountAlways(total);

        var party = SelectedParty?.Ledger?.Name ?? "party";

        // Additional cost of purchase (Book pp.133–141) — Σ of the complete additional-cost rows (0 when untracked),
        // added to the party total and apportioned onto the item landed rates below (RQ-16..RQ-20).
        var additionalTotal = AdditionalCostsTotal();
        AdditionalCostTotalText = IndianFormat.AmountAlways(additionalTotal);

        // GST summary (only when wired in) — computed once, shown as CGST/SGST/IGST + party total, and folded
        // into the derived-Dr/Cr summary so it reflects the additive tax legs.
        // Phase 9 slice 1 (A10 fix, finding #1): the compute fails fast when a cess valuation input is missing —
        // e.g. an RSP-factor Compensation-Cess item (HSN 2403 / 21069020 / …) carrying no declared Retail Sale
        // Price. The Accept path already wraps the SAME compute (see Accept()); mirror the guard on the LIVE recalc
        // so a mid-entry line does NOT let the exception propagate out of the property-change handler and break the
        // voucher screen before the friendly Accept message is reachable. Surface the message, clear the tax/cess
        // display + gate, and return; Accept re-runs the compute and blocks the post with the same message.
        ItemInvoiceGst? gst;
        try
        {
            gst = ComputeItemInvoiceGst();
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            Message = ex.Message;
            GstCgstText = "0.00";
            GstSgstText = "0.00";
            GstIgstText = "0.00";
            GstCessText = "0.00";
            var errorPartyTotal = total + additionalTotal;
            PartyTotalText = IndianFormat.AmountAlways(errorPartyTotal);
            UpdateTcsDisplay(null);
            DerivedSummary = BuildDerivedSummary(party, total, additionalTotal, 0m, 0m, 0m, 0m, errorPartyTotal);
            if (IsItemInvoice) CanAccept = false; // an unresolvable-cess line must not be acceptable
            return;
        }
        var cgst = gst?.Tax.TotalCgst.Amount ?? 0m;
        var sgst = gst?.Tax.TotalSgst.Amount ?? 0m;
        var igst = gst?.Tax.TotalIgst.Amount ?? 0m;
        // Compensation Cess (Phase 9 slice 1) is ring-fenced OUT of the CGST/SGST/IGST tax total but still added to
        // the party total (the party pays it). 0 for a company that bears no cess (byte-identical when off, ER-13).
        var cess = gst?.Tax.TotalCess.Amount ?? 0m;
        var taxTotal = cgst + sgst + igst;

        // TCS additive collection (Phase 7 slice 5) — Sales-only, goods-driven, collectee party. Computed via the
        // SAME engine the post uses (ER-4) and folded into the party total (collected on top). No-op (band hidden,
        // ₹0) on a Purchase / TCS-off company / non-collectee / non-TCS goods, so the sale is byte-identical (ER-13).
        var tcs = ComputeItemInvoiceTcs();
        var tcsTotal = tcs?.TotalTcs.Amount ?? 0m;
        UpdateTcsDisplay(tcs);

        var partyTotal = total + additionalTotal + taxTotal + cess + tcsTotal;

        GstCgstText = IndianFormat.AmountAlways(cgst);
        GstSgstText = IndianFormat.AmountAlways(sgst);
        GstIgstText = IndianFormat.AmountAlways(igst);
        GstCessText = IndianFormat.AmountAlways(cess);
        PartyTotalText = IndianFormat.AmountAlways(partyTotal);

        // Stamp the read-only landed (effective) stock rate onto each complete item line via the SAME engine the
        // post/valuation uses (ER-4). No-op when tracking is off (columns collapse — untracked screen unchanged).
        RefreshLandedRates(InventoryLines.Where(l => l.IsComplete).ToList());

        DerivedSummary = BuildDerivedSummary(party, total, additionalTotal, cgst, sgst, igst, cess, partyTotal);

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
    private string BuildDerivedSummary(string party, decimal taxable, decimal additional, decimal cgst, decimal sgst, decimal igst, decimal cess, decimal partyTotal)
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
        // Ring-fenced Compensation Cess leg (Phase 9 slice 1) — added only when a cess-bearing line resolves (0 ⇒
        // omitted, so a non-cess invoice's summary is byte-identical to Phase-4/8, ER-13).
        if (cess != 0m) extraLegs.Add($"{side} {(IsPurchaseInvoice ? "Input" : "Output")} Cess {A(cess)}");
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
            // The rate is the NET (after Price-Level discount) rate (DP-A); equals raw when no discount (ER-13).
            invLines.Add(new VoucherInventoryLine(
                l.SelectedItem!.Id, l.SelectedGodown!.Id, l.ParsedActualQuantity, l.EffectiveRate ?? new Money(rate),
                StockDirection.Inward, l.Batch, billedQuantity: l.ParsedBilledQuantity,
                // WI-10 Gap 2: the preview must model the SAME line the posting will build, unit included —
                // otherwise a by-quantity apportionment would weigh 2 where the posted voucher weighs 24 and the
                // operator would be shown a landed rate the books never use.
                unitId: l.UnitId));
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
            // WI-10 Gap 2: LandedUnitRate is per the item's BASE unit (the engine's unit). This column sits
            // beside the Rate column, which is per the LINE unit, so it is converted BACK with the documented
            // exact inverse — showing a per-Nos landed rate next to a per-Dozen rate would read as a 12× drop in
            // cost. LandedValue is a total and is unit-invariant, so it is displayed as-is. For a line with no
            // unit RateFromBaseMeasure is the identity, so the display is unchanged (ER-13).
            completeItems[i].LandedRateText =
                IndianFormat.AmountAlways(LandedRateInLineUnit(completeItems[i], ll.LandedUnitRate));
            completeItems[i].LandedValueText = IndianFormat.AmountAlways(ll.LandedValue.Amount);
        }
    }

    /// <summary>
    /// A per-BASE-unit landed rate from <see cref="AdditionalCostApportionment"/> re-expressed per the unit the
    /// LINE is stated in (WI-10 Gap 2), via the documented exact inverse <see cref="Unit.RateFromBaseMeasure"/>,
    /// so the Landed Rate column is directly comparable to the Rate column beside it. Identity for a line that
    /// carries no unit (ER-13).
    /// </summary>
    private decimal LandedRateInLineUnit(InventoryVoucherLineViewModel line, decimal baseRate)
    {
        if (line.UnitId is not { } unitId) return baseRate;
        var unit = _company.FindUnit(unitId);
        return unit is null ? baseRate : unit.RateFromBaseMeasure(baseRate);
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
            // When the A/B column is off, Billed ≡ Actual so the line is byte-identical to today (ER-13). The
            // posted rate is the NET (after Price-Level discount) rate (DP-A); equals raw when no discount (ER-13).
            inventoryLines.Add(new VoucherInventoryLine(
                l.SelectedItem!.Id, l.SelectedGodown!.Id, l.ParsedActualQuantity, l.EffectiveRate ?? new Money(rate),
                // Direction is stamped from the voucher nature by the posting service; a placeholder is fine.
                direction: IsPurchaseInvoice ? StockDirection.Inward : StockDirection.Outward,
                batchLabel: l.Batch, billedQuantity: l.ParsedBilledQuantity,
                // WI-10 Gap 2: the unit the typed quantity AND rate are stated in. l.UnitId is the gated field —
                // it returns null unless the picker is actually shown AND a non-base unit is chosen, so a hidden
                // picker can never stamp a unit onto the line (the hidden-sub-form discipline). The quantity is
                // posted AS TYPED (2), not base-normalised: Value = 2 × ₹10 = ₹20 must foot against the Sales
                // leg, and the engine converts to 24 Nos for stock on its own side.
                unitId: l.UnitId));
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
            // party = taxable + additional cost + tax + cess. The engine's TaxLines already INCLUDE the ring-fenced
            // Cess entry line(s) (Phase 9 slice 1), but TotalTax excludes cess — so the party leg must add TotalCess
            // explicitly or a cess-bearing voucher would be out of balance. TotalCess is 0 when off (ER-13).
            partyAmount = new Money(taxable.Amount + additionalTotal.Amount + gst.Tax.TotalTax.Amount + gst.Tax.TotalCess.Amount);
        }

        // TCS additive collection (Phase 7 slice 5) — Sales only, goods-driven, collectee party. Computed via the
        // SAME engine the band showed (ER-4): the party debit rises by the collected TCS, and a "TCS Payable" credit
        // leg is appended per nature so the sale still balances (Dr Party value+GST+TCS = Cr Sales + Cr Output GST +
        // Cr TCS Payable). A below-threshold nature rides its (TCS 0) detail on the party leg so the §206C(1H)
        // cumulative-FY receipts projection stays exact. Null (no TCS) ⇒ the sale posts byte-identically (ER-13).
        var tcsPayableLines = new List<EntryLine>();
        TcsLineTax? belowThresholdDetail = null;
        var tcsResult = ComputeItemInvoiceTcs();
        if (tcsResult is { } tcs)
        {
            foreach (var post in tcs.Posts)
            {
                if (post.Applies && post.TcsPayableLine is { } payable)
                    tcsPayableLines.Add(payable);
                else if (!post.Applies)
                    belowThresholdDetail ??= post.Detail; // ride the (first) below-threshold detail on the party leg
            }
            partyAmount = new Money(partyAmount.Amount + tcs.TotalTcs.Amount);
        }

        // Auto-derive the accounting legs (no hand-balancing): the party carries taxable + additional + tax + TCS; the
        // stock/value leg carries taxable only; the additional-cost + tax + TCS-payable lines are additive. Purchase →
        // Dr Purchases (taxable) / Dr Additional Costs / Dr Input tax / Cr Supplier. Sales → Dr Customer / Cr Sales /
        // Cr Output tax / Cr TCS Payable.
        var partyLine = IsPurchaseInvoice
            ? new EntryLine(party.Id, partyAmount, DrCr.Credit)
            : new EntryLine(party.Id, partyAmount, DrCr.Debit, tcs: belowThresholdDetail);
        var stockLine = IsPurchaseInvoice
            ? new EntryLine(valueLedger.Id, taxable, DrCr.Debit)
            : new EntryLine(valueLedger.Id, taxable, DrCr.Credit);

        var entryLines = new List<EntryLine>(2 + additionalCostLines.Count + taxLines.Count + tcsPayableLines.Count)
            { stockLine, partyLine };
        entryLines.AddRange(additionalCostLines);
        entryLines.AddRange(taxLines);
        entryLines.AddRange(tcsPayableLines);

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
            Message = $"{_type.Name} No. {_company.FormatVoucherNumber(posted)} accepted.";
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

/// <summary>
/// One option in the RCM <b>supply-kind</b> picker (Phase 9 slice 2; RQ-11) — the inward-supply routing the engine's
/// <see cref="RcmService.Resolve"/> takes. Only <see cref="RcmService.SupplyKind.Domestic"/> and
/// <see cref="RcmService.SupplyKind.ImportOfServices"/> are ever offered: import of <i>goods</i> is never reverse charge
/// (customs IGST on the Bill of Entry → GSTR-3B 4A(1)) and the engine hard-fails on it.
/// <para>
/// A <c>null</c> <see cref="Kind"/> is the <b>decline sentinel</b> ("Not Applicable — forward charge / not a supply"),
/// mirroring the TDS Nature-of-Payment picker's own sentinel: the screen cannot know every reason a notified-looking
/// inward supply is really forward charge, so the operator must be able to say so and have nothing post.
/// </para>
/// </summary>
public sealed class RcmSupplyKindOption
{
    /// <summary>The inward-supply routing, or <c>null</c> for the "Not Applicable" decline sentinel.</summary>
    public RcmService.SupplyKind? Kind { get; init; }
    public string Display { get; init; } = string.Empty;
}

/// <summary>
/// One option in the §34 note's <b>original-invoice</b> picker (Phase 9 slice 2b; RQ-24; ER-12) — the link GSTR-1
/// Table 9B / the amendment tables read. Three shapes: the <see cref="IsNone"/> sentinel (nothing chosen — the ER-12
/// guard fires on it), the <see cref="IsConsolidated"/> option (no voucher link; the operator types the original
/// invoice number + date), or a real posted <see cref="Voucher"/>.
/// </summary>
public sealed class CdnOriginalInvoiceOption
{
    public Voucher? Invoice { get; init; }
    public bool IsConsolidated { get; init; }
    public string Display { get; init; } = string.Empty;
    public bool IsNone => Invoice is null && !IsConsolidated;
}

/// <summary>
/// One option in the <b>outstanding-advance</b> picker (Phase 9 slice 2b; RQ-25) — an advance that has been neither
/// adjusted against an invoice nor refunded. The <see cref="IsNone"/> sentinel means "no advance action on this voucher".
/// </summary>
public sealed class AdvanceReceiptOption
{
    public GstAdvanceReceipt? Receipt { get; init; }
    public string Display { get; init; } = string.Empty;
    public bool IsNone => Receipt is null;
}

/// <summary>
/// One option in the <b>tax-invoice</b> picker an advance is adjusted against (Phase 9 slice 2b; RQ-25 → GSTR-1 11B).
/// The <see cref="IsNone"/> sentinel means nothing chosen — which the Accept guard refuses on.
/// </summary>
public sealed class AdvanceInvoiceOption
{
    public Voucher? Invoice { get; init; }
    public string Display { get; init; } = string.Empty;
    public bool IsNone => Invoice is null;
}
