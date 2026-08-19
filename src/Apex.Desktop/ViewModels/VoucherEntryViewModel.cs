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
    /// True for a <b>Sales or Purchase</b> voucher — the accounting-(service)-invoice mode (G-7; SG p.80 names its
    /// two use cases: service purchases and fixed-asset purchases).
    /// <para><b>History, kept because it is the reason this predicate is dangerous to touch.</b> The purchase arm was
    /// written, then gated off, because shipping it silently BROKE MONEY: <c>TdsPossible</c> and
    /// <c>DetectTdsShape</c> read the plain <c>Lines</c> collection, which is EMPTY in accounting mode, so a
    /// professional-fee purchase posted <c>Cr Consultant 1,18,000 / Dr Professional Fees 1,00,000 / Dr Input CGST
    /// 9,000 / Dr Input SGST 9,000</c> with <b>no §194J TDS carve-out at all</b> (and RCM mis-evaluated the same
    /// way). The stated precondition for flipping this predicate was "wire TDS/RCM to the Particulars lines first".
    /// That is now done — see <see cref="DetectAccountingTdsShape"/>, <see cref="DetectAccountingRcmShape"/>,
    /// <see cref="AssessableExGst"/>'s accounting branch, and the carve-out/RCM application in
    /// <see cref="AcceptAccountingInvoice"/>. <c>PurchaseAccountingInvoiceTdsTests</c> is the regression guard: it
    /// proves §194J still fires, still rounds to the rupee, still records a below-threshold assessment, and still
    /// declines on the sentinel. <b>Do not widen this predicate further without the equivalent proof.</b></para>
    /// </summary>
    public bool CanBeAccountingInvoice =>
        _type.BaseType is VoucherBaseType.Sales or VoucherBaseType.Purchase;

    /// <summary>
    /// The per-voucher <b>entry mode</b> (catalog §10; Tally "Change Mode", Ctrl+H) — the single source of truth for
    /// which grid/render the screen shows: the classic Dr/Cr grid (<see cref="VoucherEntryMode.AsVoucher"/>), the
    /// cash/bank <see cref="VoucherEntryMode.SingleEntry"/> re-render of those same lines, the stock-item Item Invoice
    /// (<see cref="VoucherEntryMode.ItemInvoice"/>, Ctrl+I), or the service/accounting-ledger Accounting Invoice
    /// (<see cref="VoucherEntryMode.AccountingInvoice"/>). All post ordinary balanced <c>Voucher</c> legs; the mode
    /// is transient screen state, never persisted (inferred downstream from the posted legs — see the print/GSTR-1 paths).
    ///
    /// <para><b>This field initialiser is NOT the opening mode.</b> It is only the resting value the object holds
    /// before the constructor runs. The mode a screen actually OPENS in is seeded per voucher type — see
    /// <see cref="SeedOpeningMode"/> — because Payment, Receipt and Contra open in Single Entry.</para>
    /// </summary>
    [ObservableProperty] private VoucherEntryMode _mode = VoucherEntryMode.AsVoucher;

    /// <summary>
    /// Seeds the mode the screen OPENS in, per voucher type: <see cref="VoucherEntryMode.SingleEntry"/> on the three
    /// cash/bank vouchers (<see cref="CanBeSingleEntry"/> — Contra F4, Payment F5, Receipt F6),
    /// <see cref="VoucherEntryMode.AsVoucher"/> on the other twenty.
    ///
    /// <para><b>The evidence is inference from absence, so here is its exact shape.</b> Three separate walkthroughs in
    /// <c>703679456-TALLY-PRIME-WITH-GST-Notes-PDF.pdf</c> (<c>pdftotext -layout</c>) reach the Dr/Cr screen by turning
    /// the single-entry setting <b>off</b>, and each then keys Cr and Dr fields — i.e. Double Entry:
    /// <list type="bullet">
    ///   <item>line 334 — "Use single entry mode for payment/receipt/contra vouchers? <b>NO</b>", followed at line 337
    ///     onward by "In Credit field …", "In Debit field …";</item>
    ///   <item>line 1634 — "Press F12 &amp; Activate Use Single Entry Mode for Pymt/Rcpt/Contra <b>set to No</b>",
    ///     followed by "In the Cr field select …";</item>
    ///   <item>line 1965 — "In F12: Configure 'Use single entry mode for pymt/Rect/Contra?' <b>set to No</b>",
    ///     followed by "In Dr. field …".</item>
    /// </list>
    /// An instruction to turn a setting off is only meaningful if the shipped state is on. Layout corroboration:
    /// <c>664311548-Tally-Prime-Book.pdf</c> pp.26-27, 29, 31-32; <c>696054070-TALLY-PRIME-STUDY-GUIDE.pdf</c> p.76.</para>
    ///
    /// <para><b>The one apparent counter-example, recorded rather than buried.</b> GSTN line 330 reads "4. Select
    /// single entry mode for payment/receipt/contra vouchers", which in isolation looks like an instruction to switch
    /// Single Entry ON. It is not: it names the setting being navigated to (steps 3-6 are a list of settings to visit),
    /// and line 334 — four lines later, in the same numbered walkthrough — supplies its value, <b>NO</b>. Steps 7-12 of
    /// that same walkthrough then key Credit and Debit fields. Reading line 330 as "turn it on" contradicts the twelve
    /// steps beneath it. So the claim is not that the string never appears affirmatively; it is that <b>no walkthrough
    /// in the corpus ever ends up in Single Entry by switching it on</b> — every one that mentions the setting is
    /// switching it off.</para>
    ///
    /// <para><b>Residual uncertainty.</b> The corpus reaches Double Entry through an F12 flag, which is the ERP-9-era
    /// control. That evidence establishes the <b>shipped state</b>, not which control changes it. We therefore seed the
    /// state and leave Ctrl+H ("Change Voucher Mode", cited at GSTN line 328) as the way out; no F12 flag is invented,
    /// and nothing here is persisted. Adding that F12 toggle later is a separate additive change — it would alter which
    /// control reaches Double Entry, never which state the screen opens in.</para>
    ///
    /// <para><b>Assigns through the generated property, not the backing field.</b> The field-assignment form trips
    /// analyzer MVVMTK0034, and more importantly it would skip <c>OnModeChanged</c> — whose
    /// <see cref="SyncSingleEntrySides"/> call is what stamps the documented Dr/Cr polarity onto the starter lines.
    /// Must therefore be called AFTER the two starter lines exist, or there is nothing to stamp.</para>
    /// </summary>
    private void SeedOpeningMode()
    {
        if (CanBeSingleEntry) Mode = VoucherEntryMode.SingleEntry;
    }

    /// <summary>
    /// Ctrl+I — whether this Purchase/Sales voucher is being entered <b>as an item invoice</b> (catalog §10):
    /// the user enters a party + inventory lines (Stock Item / Godown / Qty / Rate / Batch) and the VM
    /// auto-derives the two balancing accounting legs, so the pairing invariant always holds without any
    /// hand-balancing. When off, the plain Dr/Cr grid is used and the voucher behaves exactly as before.
    /// Only ever true when <see cref="CanBeItemInvoice"/>. Now a <b>derived alias</b> of <see cref="Mode"/> so every
    /// existing binding, test and code path is unchanged; the source of truth is <see cref="Mode"/>.
    /// </summary>
    public bool IsItemInvoice => Mode == VoucherEntryMode.ItemInvoice;

    /// <summary>
    /// Whether this Purchase/Sales voucher is being entered <b>as an accounting (service) invoice</b>: the user enters
    /// a party + service-income <b>ledger</b> lines under Particulars (no stock item) and the VM resolves auto SAC-based
    /// GST from each ledger's GST block, splitting CGST/SGST (intra) vs IGST (inter). No stock/godown/valuation is ever
    /// entered (<c>HasInventoryLines</c> stays false). Only ever true when <see cref="CanBeAccountingInvoice"/>.
    /// <para>The <see cref="CanBeAccountingInvoice"/> conjunct is the <b>whole</b> deferral gate, deliberately placed
    /// here rather than only in <see cref="ChangeMode"/>: every downstream consumer (the Accept routing, the
    /// Recalculate routing, the GST gate, the grid gates) reads THIS property, so even forcing <see cref="Mode"/>
    /// directly cannot arm the deferred purchase path.</para>
    /// </summary>
    public bool IsAccountingInvoice => Mode == VoucherEntryMode.AccountingInvoice && CanBeAccountingInvoice;

    /// <summary>Whether this voucher is in the classic Dr/Cr "As Voucher" mode — the default, and the plain-grid gate.
    /// Defined as the COMPLEMENT of the two invoice modes (not <c>Mode == AsVoucher</c>) so the three gates stay a
    /// total, mutually-exclusive partition: a Purchase forced to <c>Mode == AccountingInvoice</c> renders — and posts
    /// as — the plain Dr/Cr voucher rather than showing no grid at all.</summary>
    public bool IsAsVoucherMode => !IsItemInvoice && !IsAccountingInvoice;

    /// <summary>
    /// Whether the classic <b>Dr/Cr grid</b> is the visible render. Single Entry (G-6) is a re-render of the SAME
    /// lines, so it deliberately leaves <see cref="IsAsVoucherMode"/> true — that is what keeps Accept routing to the
    /// unchanged plain-grid posting path — and only swaps which grid is on screen. Without this split gate the two
    /// grids would render on top of each other.
    /// </summary>
    public bool ShowPlainDrCrGrid => IsAsVoucherMode && !IsSingleEntry;

    /// <summary>Whether the invoice overlay (party header + line grid + GST band) is shown — true in Item OR
    /// Accounting mode; the plain Dr/Cr grid shows in its complement (<see cref="IsAsVoucherMode"/>).</summary>
    public bool ShowInvoiceOverlay => !IsAsVoucherMode;

    /// <summary>The caption of the shared running-total figure beside the derived Dr/Cr summary. Mode-aware: an
    /// accounting (service) invoice has no items, so reading "Items Total" on it was simply wrong.</summary>
    public string LineTotalCaption => IsAccountingInvoice ? "Services Total ₹ " : "Items Total ₹ ";

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

    // ============================================ Bill-wise Details on the INVOICE modes (G-1; SG pp.79–82)

    /// <summary>
    /// The <b>Bill-wise Details</b> allocation rows for the party leg of an invoice-mode voucher (G-1).
    ///
    /// <para><b>Why this exists separately from <see cref="VoucherLineViewModel.BillAllocations"/>.</b> The plain
    /// Dr/Cr grid hangs allocations off the <i>line</i>, because the party there IS a line. In the two invoice modes
    /// there is no party line to hang them from — the party leg is <b>derived</b> at Accept from the running total —
    /// so the allocations belong to the SCREEN and reconcile against <see cref="InvoicePartyTotal"/>.</para>
    ///
    /// <para><b>The gap this closes.</b> Both invoice Accept paths previously built the party <c>EntryLine</c> with
    /// no allocations at all, while <c>Outstandings</c> only counts lines that HAVE them — so a company invoicing
    /// normally had an empty Receivables report, empty ageing, no overdue tracking and nothing to settle against,
    /// with no error and no warning. The corpus puts the sub-screen squarely on both modes: SG p.79 step 7 (Purchase
    /// Item Invoice), p.80 step 6 (Purchase Accounting Invoice), p.81 step 6 (Sales Item Invoice), p.82 step 5
    /// (Sales Accounting Invoice).</para>
    /// </summary>
    public ObservableCollection<BillAllocationRowViewModel> InvoiceBillAllocations { get; } = new();

    /// <summary>
    /// The amount the invoice bill split must foot to — the <b>party total</b> (taxable + additional cost + GST +
    /// cess + TCS), i.e. exactly what the derived party leg will carry. Restamped by each mode's recalc, so the
    /// running split summary tracks the invoice as it is typed.
    /// </summary>
    [ObservableProperty] private decimal _invoicePartyTotal;

    /// <summary>The running "Allocated X of Y" summary for the invoice bill-wise panel.</summary>
    [ObservableProperty] private string _invoiceBillSummary = string.Empty;

    /// <summary>
    /// The <b>four-layer gate</b> for the invoice Bill-wise panel, per the spec's four-layer config model — a field
    /// appears only when every layer permits it:
    /// <list type="number">
    ///   <item><b>F11 capability</b> — no company-level "Enable Bill-wise entry" flag exists on
    ///     <see cref="Company"/> yet (spec C-04: layers 1 and 3 are collapsed in this codebase), so this layer is
    ///     permissive. Adding it is schema work and is deliberately NOT done here.</item>
    ///   <item><b>F12 on the ledger master</b> — no layer-2 concept exists anywhere in this codebase (spec §1.1),
    ///     so this layer is permissive too.</item>
    ///   <item><b>The master's own field value</b> — <see cref="Ledger.MaintainBillByBill"/> on the SELECTED PARTY.
    ///     This is the operative gate and is enforced here.</item>
    ///   <item><b>F12 on the voucher screen</b> — <see cref="UseDefaultBillWiseAllocation"/> (spec C-41, "Use
    ///     default Bill-wise details for Bill Allocation"): Yes (the SHIPPED default) ⇒ the screen does NOT appear
    ///     and the allocation is derived silently. This layer governs <b>visibility only</b> — see
    ///     <see cref="InvoiceBillWiseApplies"/>.</item>
    /// </list>
    /// Plus the structural precondition that we are actually in an invoice mode (the plain grid keeps its own
    /// per-line panel).
    /// </summary>
    public bool ShowInvoiceBillWise => InvoiceBillWiseApplies && !UseDefaultBillWiseAllocation;

    /// <summary>
    /// Whether bill-wise allocation <b>applies</b> to this invoice at all — the structural precondition (an invoice
    /// mode) plus the operative master gate (<see cref="Ledger.MaintainBillByBill"/> on the selected party).
    ///
    /// <para><b>Why this is separate from <see cref="ShowInvoiceBillWise"/>.</b> The allocation and the SUB-SCREEN are
    /// different things. TallyPrime's default bill allocation posts a real bill-wise allocation while showing the
    /// operator nothing: with "Use default Bill-wise details for Bill Allocation" set to Yes, "you will not see any
    /// difference in the voucher … On saving the sales transaction, the bill gets linked to the party as default bill
    /// allocation. The voucher number appears as the bill reference" (official TallyPrime, <i>How to Manage
    /// Outstanding Receivables in TallyPrime</i> → Change Bill Allocation). So THIS property gates the allocation —
    /// seeding, validation and posting — and <see cref="UseDefaultBillWiseAllocation"/> gates only whether the
    /// operator gets to see and edit it. One seeding path serves both, which is why the revealed panel opens
    /// pre-filled rather than blank.</para>
    /// </summary>
    public bool InvoiceBillWiseApplies =>
        ShowInvoiceOverlay
        && SelectedParty?.Ledger is { MaintainBillByBill: true };

    /// <summary>
    /// Voucher-screen F12 "Use default Bill-wise details for Bill Allocation" (spec C-41) — Yes ⇒ the allocation is
    /// derived automatically and the Bill-wise screen never appears; No ⇒ the screen appears, pre-filled with that
    /// same derivation, for the operator to change.
    ///
    /// <para><b>Default Yes, because that is what TallyPrime ships.</b> Official TallyPrime (Change Bill Allocation):
    /// with it Yes "you will not see any difference in the voucher"; set it to No and "you can select the bill
    /// references in the Bill-wise Details screen". The corpus shows the same default from the other side —
    /// <c>719244897-Tally-Book.pdf</c> p.81 has the author explicitly set "F12: Use default bill-wise details for bill
    /// allocation — No" precisely IN ORDER to make the sub-screen appear for teaching. This flag previously defaulted
    /// to No with a comment claiming that matched TallyPrime; it was backwards, and the symptom was an extra column
    /// demanding a bill reference that TallyPrime fills in silently.</para>
    ///
    /// <para>Transient screen state, never persisted. Switching it back ON abandons any hand-made split and returns
    /// to the single derived allocation — "default" means default.</para>
    /// </summary>
    [ObservableProperty] private bool _useDefaultBillWiseAllocation = true;

    partial void OnUseDefaultBillWiseAllocationChanged(bool value)
    {
        // Back to the DEFAULT allocation ⇒ discard whatever the operator built by hand, so the hidden state is always
        // the derived one. Leaving a stale multi-row split behind would post a split the operator can no longer see.
        if (value)
        {
            _invoiceBillDirty = false;
            InvoiceBillAllocations.Clear();
            _autoBillName = string.Empty;
            _autoBillDueDateText = string.Empty;
        }
        OnPropertyChanged(nameof(ShowInvoiceBillWise));
        Recalculate();
    }

    /// <summary>Σ of the invoice allocation row magnitudes.</summary>
    public decimal InvoiceBillAllocatedTotal
    {
        get
        {
            var sum = 0m;
            foreach (var a in InvoiceBillAllocations) sum += a.ParsedAmount;
            return sum;
        }
    }

    /// <summary>
    /// True when the invoice bill split is valid: bill-wise does not apply (no constraint), or every touched row is
    /// complete and the complete rows sum EXACTLY to <see cref="InvoicePartyTotal"/> — the same exact-sum rule the
    /// plain grid and <c>VoucherValidator</c> already enforce (spec C-28, SG p.92).
    ///
    /// <para>The gate is deliberately keyed on <see cref="InvoiceBillWiseApplies"/>, not on panel visibility, so the
    /// DEFAULT (hidden) allocation is held to the identical exact-sum rule. It is exact by construction there — the
    /// row is stamped from the party total the Accept path just computed — but a silent path is exactly the kind that
    /// must not be exempt from the invariant that stops a mis-footed allocation posting.</para>
    /// </summary>
    public bool InvoiceBillSplitOk
    {
        get
        {
            if (!InvoiceBillWiseApplies) return true;
            if (InvoiceBillAllocations.Any(a => !a.IsBlank && !a.IsComplete)) return false;
            var complete = InvoiceBillAllocations.Where(a => a.IsComplete).ToList();
            if (complete.Count == 0) return false;
            return complete.Sum(a => a.ParsedAmount) == InvoicePartyTotal && InvoicePartyTotal > 0m;
        }
    }

    /// <summary>Set once the operator has touched the split themselves — after which the auto-fill stops restamping
    /// the single seeded row from the running total, so a deliberate split is never silently overwritten.</summary>
    private bool _invoiceBillDirty;

    /// <summary>The bill reference this screen last auto-stamped. A row still carrying it is still OURS to restamp
    /// (so capturing the Supplier Invoice No. after the party replaces the provisional voucher-number reference);
    /// anything else was typed by the operator and is never clobbered.</summary>
    private string _autoBillName = string.Empty;

    /// <summary>The due date this screen last auto-stamped — same ownership rule as <see cref="_autoBillName"/>.</summary>
    private string _autoBillDueDateText = string.Empty;

    /// <summary>
    /// The bill reference TallyPrime fills in for you (SG p.92 field spec; official TallyPrime "the voucher number
    /// appears as the bill reference"). Per base type:
    /// <list type="bullet">
    ///   <item><b>Purchase</b> ⇒ the <b>Supplier Invoice No.</b> when one has been captured — the counterparty's own
    ///     document number is the bill (<c>719244897-Tally-Book.pdf</c> p.81 works it end to end: Supplier Invoice No.
    ///     311 ⇒ <c>New Ref | Name: 311 | 30 days | 25,000 Cr</c>).</item>
    ///   <item><b>Sales</b> ⇒ our own <b>rendered</b> voucher number: a sale has no counterparty document number, and
    ///     the number we render (prefix/pad/suffix and all, via <see cref="FormattedVoucherNumber"/>) IS the document
    ///     number this app prints on the invoice, so the bill reference must be the same string.</item>
    /// </list>
    /// <para><b>INFERENCE (not sourced):</b> a Purchase whose Supplier Invoice No. was left blank falls back to our
    /// own rendered voucher number. The corpus only covers the case where the number IS captured; the fallback is
    /// this codebase's choice, made because the alternative — an unnamed New Ref — opens a payable that can never be
    /// matched by a later Agst Ref.</para>
    /// <para>The last resort (<see cref="VoucherNumber"/> as plain digits) exists only for a voucher type numbered
    /// <see cref="NumberingMethod.None"/>, where the render is legitimately empty: without it the derived allocation
    /// would be nameless, hence incomplete, and Accept would refuse behind a panel the operator cannot see.</para>
    /// </summary>
    private string AutoBillReferenceName()
    {
        if (IsPurchaseInvoice && !string.IsNullOrWhiteSpace(ReferenceNo))
            return ReferenceNo.Trim();

        var rendered = FormattedVoucherNumber;
        if (!string.IsNullOrWhiteSpace(rendered)) return rendered;

        return VoucherNumber > 0
            ? VoucherNumber.ToString(System.Globalization.CultureInfo.InvariantCulture)
            : string.Empty;
    }

    /// <summary>
    /// The due date TallyPrime fills in for you — SG p.92: "Due Date, or Credit Days: reflected automatically as per
    /// the given credit period specified for the party ledger". Blank when the party specifies no credit period,
    /// which is also what a blank field already means downstream
    /// (<see cref="BillAllocation.EffectiveDueDate"/> derives it), so the two agree to the day.
    /// </summary>
    private string AutoBillDueDateText()
        => SelectedParty?.Ledger?.DefaultCreditPeriodDays is { } days && days > 0
            ? ApexDate.Format(Date.AddDays(days))
            : string.Empty;

    /// <summary>Re-entrancy guard: stamping a row's amount raises its change notification, which re-enters the sync.</summary>
    private bool _syncingInvoiceBills;

    /// <summary>Adds a Bill-wise row to the invoice panel and marks the split operator-owned.</summary>
    public BillAllocationRowViewModel AddInvoiceBillAllocation(BillRefType refType = BillRefType.NewRef)
    {
        var row = new BillAllocationRowViewModel(OnInvoiceBillRowChanged, refType);
        InvoiceBillAllocations.Add(row);
        if (!_syncingInvoiceBills) _invoiceBillDirty = true;
        RefreshInvoiceBillSummary();
        return row;
    }

    /// <summary>Removes a Bill-wise row from the invoice panel (keeps at least one while the panel is on).</summary>
    public void RemoveInvoiceBillAllocation(BillAllocationRowViewModel row)
    {
        if (InvoiceBillAllocations.Count <= 1) return;
        InvoiceBillAllocations.Remove(row);
        _invoiceBillDirty = true;
        RefreshInvoiceBillSummary();
        Recalculate();
    }

    private void OnInvoiceBillRowChanged()
    {
        if (_syncingInvoiceBills) return;

        // Dirtiness is judged on the AMOUNTS, not on "the operator touched something". Typing the bill reference
        // NAME into the auto-seeded row must NOT freeze the auto-fill — otherwise naming the bill first and adding
        // an item line second leaves the allocation stuck at the old total, and the split silently stops footing.
        if (InvoiceBillAllocations.Count != 1 || InvoiceBillAllocations[0].ParsedAmount != InvoicePartyTotal)
            _invoiceBillDirty = true;

        RefreshInvoiceBillSummary();
        OnPropertyChanged(nameof(InvoiceBillSplitOk));
        OnPropertyChanged(nameof(InvoiceBillAllocatedTotal));
        // Re-derive the Accept gate: InvoiceBillSplitOk is a CanAccept conjunct, so without this the Accept button
        // stayed greyed after the operator typed the bill name and only un-greyed on an unrelated field change.
        Recalculate();
    }

    /// <summary>
    /// Keeps the invoice Bill-wise allocation in step with the running party total. Called by BOTH invoice recalcs
    /// with the total the party leg will carry. It runs whenever <see cref="InvoiceBillWiseApplies"/> — panel shown or
    /// not — because the DEFAULT allocation is derived by exactly the same code that pre-fills the visible panel.
    /// <list type="bullet">
    ///   <item>Bill-wise does not apply ⇒ the rows are cleared, so switching party or mode never leaves stray
    ///     allocations behind (the posted voucher is then byte-identical to one entered before this feature existed —
    ///     ER-13).</item>
    ///   <item>It applies and the operator has NOT touched the split ⇒ the single New-Ref row is stamped with the
    ///     full party total, the derived reference (<see cref="AutoBillReferenceName"/>) and the derived due date
    ///     (<see cref="AutoBillDueDateText"/>) — SG p.92's field spec, all three "captured automatically". With the
    ///     panel hidden this IS TallyPrime's default bill allocation; with it shown it is the pre-fill the operator
    ///     corrects.</item>
    ///   <item>It applies and the operator HAS split it ⇒ nothing is restamped; only the summary refreshes.</item>
    /// </list>
    /// </summary>
    private void SyncInvoiceBillWise(decimal partyTotal)
    {
        if (_syncingInvoiceBills) return;
        _syncingInvoiceBills = true;
        try
        {
            InvoicePartyTotal = partyTotal;
            OnPropertyChanged(nameof(InvoiceBillWiseApplies));
            OnPropertyChanged(nameof(ShowInvoiceBillWise));

            if (!InvoiceBillWiseApplies)
            {
                if (InvoiceBillAllocations.Count > 0) InvoiceBillAllocations.Clear();
                _invoiceBillDirty = false;
                _autoBillName = string.Empty;
                _autoBillDueDateText = string.Empty;
                InvoiceBillSummary = string.Empty;
                return;
            }

            if (InvoiceBillAllocations.Count == 0)
            {
                AddInvoiceBillAllocation(BillRefType.NewRef);
                _invoiceBillDirty = false;
            }

            if (!_invoiceBillDirty && InvoiceBillAllocations.Count == 1)
            {
                var row = InvoiceBillAllocations[0];
                row.AmountText = partyTotal.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);

                // Restamp only while the field is still ours: blank, or still carrying what we last put there. A value
                // the operator typed is a deliberate override and outlives every later recalculation.
                var autoName = AutoBillReferenceName();
                if (row.NameRequired && (string.IsNullOrWhiteSpace(row.Name) || row.Name == _autoBillName))
                {
                    row.Name = autoName;
                    _autoBillName = autoName;
                }

                var autoDue = AutoBillDueDateText();
                if (string.IsNullOrWhiteSpace(row.DueDateText) || row.DueDateText == _autoBillDueDateText)
                {
                    row.DueDateText = autoDue;
                    _autoBillDueDateText = autoDue;
                }
            }
        }
        finally
        {
            _syncingInvoiceBills = false;
        }
        RefreshInvoiceBillSummary();
        OnPropertyChanged(nameof(InvoiceBillSplitOk));
        OnPropertyChanged(nameof(InvoiceBillAllocatedTotal));
    }

    private void RefreshInvoiceBillSummary()
    {
        if (!ShowInvoiceBillWise) { InvoiceBillSummary = string.Empty; return; }
        var allocated = InvoiceBillAllocatedTotal;
        var target = InvoicePartyTotal;
        var diff = target - allocated;
        string F(decimal v) => v.ToString("#,##0.00", Apex.Ledger.IndianMoneyFormat.Culture);
        InvoiceBillSummary = diff == 0m && target > 0m
            ? $"Allocated {F(allocated)} of {F(target)}  —  fully allocated"
            : diff > 0m
                ? $"Allocated {F(allocated)} of {F(target)}  —  {F(diff)} unallocated"
                : $"Allocated {F(allocated)} of {F(target)}  —  over-allocated by {F(-diff)}";
    }

    /// <summary>
    /// The domain <see cref="BillAllocation"/>s to stamp on the derived party leg, or <c>null</c> when bill-wise does
    /// not apply — <c>null</c> (not an empty list) so the built <see cref="EntryLine"/> is byte-identical to one built
    /// before this feature existed (ER-13). Keyed on <see cref="InvoiceBillWiseApplies"/>, so the DEFAULT (hidden)
    /// allocation posts exactly as the visible one does: "on saving … the bill gets linked to the party as default
    /// bill allocation" (official TallyPrime).
    /// </summary>
    private IReadOnlyList<BillAllocation>? ToInvoiceBillAllocations()
    {
        if (!InvoiceBillWiseApplies) return null;
        var rows = InvoiceBillAllocations.Where(a => a.IsComplete).Select(a => a.ToAllocation()).ToList();
        return rows.Count > 0 ? rows : null;
    }

    /// <summary>
    /// The first typed amount on the plain Dr/Cr grid that the INTEGER-paisa store could not carry — the line
    /// amount itself, then its bill-wise and cost allocation rows — or <c>null</c> when every figure is storable
    /// (W0-13 S2a).
    ///
    /// <para>Scoped exactly like the gates it precedes: blank rows are skipped (an untouched trailing row is not
    /// an error), allocation rows are read only on a line whose panel is actually in play, and the FIRST offender
    /// wins so the operator gets one specific message rather than a list.</para>
    ///
    /// <para>The line amount is checked before its allocations deliberately: a line that is itself unstorable will
    /// have unstorable allocations too (they must sum to it), and naming the line is the more useful diagnosis.</para>
    /// </summary>
    private string? UnstorableGridAmountError()
    {
        foreach (var line in Lines)
        {
            if (!line.IsBlank && line.AmountError is { } lineError) return lineError;

            // W0-13's fourth typed amount, added by finding L2-03: the FOREX magnitude. It is not reached through
            // AmountError — the base is DERIVED (forex x rate, snapped to the paisa), so it is paisa-exact however
            // fine the forex figure is — and a >2dp forex amount posted, validated and SAVED, after which the
            // canonical export threw on a company the app itself had produced.
            if (line.ForexAmountError is { } forexError) return forexError;

            if (line.IsBillWise)
                foreach (var bill in line.BillAllocations)
                    if (!bill.IsBlank && bill.AmountError is { } billError) return billError;

            if (line.IsCostApplicable)
                foreach (var cost in line.CostAllocations)
                    if (!cost.IsBlank && cost.AmountError is { } costError) return costError;
        }
        return null;
    }

    /// <summary>
    /// The shared Accept-time refusal for the invoice bill split, used by BOTH invoice Accept paths. Re-derives the
    /// split against the party total the Accept path actually computed — never against a stale display figure — so a
    /// tax or TCS change that moved the total after the last recalc cannot post a mis-footed allocation.
    /// </summary>
    private bool InvoiceBillAllocationsOk(decimal partyTotal)
    {
        if (!InvoiceBillWiseApplies) return true;
        SyncInvoiceBillWise(partyTotal);
        if (InvoiceBillSplitOk) return true;

        // W0-13 S2a — an unstorable row amount is named for what it is, ABOVE the two generic branches below.
        // This is the sharpest instance of the defect: the invoice Accept paths Post (which appends the voucher to
        // the shared Company) and only then Save — so before this guard a sub-paisa split that footed to the party
        // total EXACTLY left the refused invoice on the aggregate and made every later, unrelated save throw.
        // That is the CAUSE; the missing rollback it relied on is closed separately, in the save guard both Accept
        // paths now carry (S2b), so this is a front line over a closed site rather than over an open one.
        var unstorable = InvoiceBillAllocations
            .Where(a => !a.IsBlank)
            .Select(a => a.AmountError)
            .FirstOrDefault(e => e is not null);
        if (unstorable is not null)
        {
            Message = unstorable;
            return false;
        }

        Message = InvoiceBillAllocations.Any(a => !a.IsBlank && !a.IsComplete)
            ? "Every bill-wise row needs a positive amount and (except On Account) a bill reference name."
            : $"The bill-wise allocation must total {InvoicePartyTotal.ToString("#,##0.00", Apex.Ledger.IndianMoneyFormat.Culture)} " +
              $"— the party's invoice total. Currently allocated {InvoiceBillAllocatedTotal.ToString("#,##0.00", Apex.Ledger.IndianMoneyFormat.Culture)}.";

        // The derived allocation is exact by construction, so this branch means something upstream desynced. With the
        // panel hidden the operator has nothing to correct, so name the switch that reveals it rather than stranding
        // them behind a refusal with no visible cause.
        if (!ShowInvoiceBillWise)
            Message += " Clear \"Use default Bill-wise details for Bill Allocation\" to review the split.";
        return false;
    }

    // =============================================================== batch allocation (G-5; BOOK pp.130–132)

    /// <summary>
    /// <b>Layer 4</b> of the batch gate — the voucher-screen knob "Use batch-wise details for item allocation".
    /// TallyPrime abolished the single global "F12 &gt; Voucher Entry" page: configuration belongs to the screen
    /// you are standing on (spec §2.6), so this is per-entry-screen state, defaulted <b>on</b> so a batch-enabled
    /// company gets the corpus walkthrough (BOOK pp.131–132) without configuring anything. Turning it off
    /// suppresses the batch sub-screen on THIS screen only; a value already committed to a line keeps acting
    /// (C-15: a Yes feature that is then hidden stays Yes).
    /// <para><b>Not bound to the F12 KEY.</b> Our entire F12 surface on a voucher screen is the voucher-numbering
    /// config (spec §2.6 closing note; <c>MainWindowViewModel.IsVoucherNumberingContext</c>), so this knob is a
    /// field on the invoice options panel beside its sibling company/type switches instead of stealing that
    /// shipped screen.</para>
    /// </summary>
    [ObservableProperty] private bool _useBatchWiseDetails = true;

    partial void OnUseBatchWiseDetailsChanged(bool value) => RecalculateItemInvoice();

    /// <summary>True iff the layer-4 batch knob is worth showing at all — a Sales/Purchase item invoice on a
    /// company that maintains batch-wise details (C-06). Off ⇒ the checkbox never appears (ER-13).</summary>
    public bool CanUseBatchWiseDetails => CanBeItemInvoice && _company.MaintainBatchwiseDetails;

    /// <summary>
    /// Raised when the batch-allocation sub-screen should open for an item-invoice line whose item Maintains-in
    /// Batches: item, godown, line quantity, whether the movement is OUTWARD (Sales — so the sub-screen seeds the
    /// FEFO/FIFO issue plan, DP-1), and the callback that writes the committed allocations back to the line. The
    /// shell (not this VM) owns opening the cascade column — the same contract the stock screens use.
    /// </summary>
    public event Action<StockItem, Godown, decimal, bool,
        Action<IReadOnlyList<BatchAllocation>>>? BatchAllocationRequested;

    /// <summary>
    /// The full four-layer gate for the batch sub-screen on an item invoice (G-5, closing C-20):
    /// <list type="number">
    ///   <item><b>L1 — F11</b>: the company maintains batch-wise details (C-06);</item>
    ///   <item><b>L2 — mode</b>: this is a Purchase/Sales entered <i>as an item invoice</i> (the only screens
    ///     BOOK pp.131–132 walks);</item>
    ///   <item><b>L3 — master</b>: the picked stock item has "Maintain in Batches" on, with a godown and a
    ///     positive quantity known (the sub-screen allocates a real quantity, so it needs one);</item>
    ///   <item><b>L4 — screen</b>: <see cref="UseBatchWiseDetails"/> permits the field here (spec §2.6).</item>
    /// </list>
    /// A stock item without batches fails L3 and the whole feature is invisible to it (ER-13).
    /// </summary>
    public bool LineWantsBatchAllocation(InventoryVoucherLineViewModel line) =>
        _company.MaintainBatchwiseDetails
        && UseBatchWiseDetails
        && IsItemInvoice && CanBeItemInvoice
        && line is { ShowsBatch: true, SelectedItem: { MaintainInBatches: true }, SelectedGodown: not null }
        && line.ParsedQuantity > 0m;

    /// <summary>
    /// Alt+B / "⧉" on an item-invoice line — asks the shell for the batch-allocation sub-screen and writes the
    /// accepted allocations back onto the line. Outward for a Sales invoice (stock leaves, so the sub-screen
    /// seeds the FEFO/FIFO plan from existing batches), inward for a Purchase (nothing on hand to draw from, so
    /// the operator types the received batch — BOOK p.131). A hard no-op unless
    /// <see cref="LineWantsBatchAllocation"/>.
    /// </summary>
    public void RequestBatchAllocation(InventoryVoucherLineViewModel line)
    {
        if (line is null || !LineWantsBatchAllocation(line)) return;
        BatchAllocationRequested?.Invoke(
            line.SelectedItem!, line.SelectedGodown!, line.ParsedQuantity, !IsPurchaseInvoice,
            line.SetBatchAllocations);
    }

    /// <summary>
    /// Whole-screen Alt+B fallback (NFR-2): opens the sub-screen for the first line it applies to. Returns false
    /// — a safe no-op — when no line currently qualifies.
    /// </summary>
    public bool RequestBatchAllocationForFirstEligibleLine()
    {
        var line = InventoryLines.FirstOrDefault(LineWantsBatchAllocation);
        if (line is null) return false;
        RequestBatchAllocation(line);
        return true;
    }

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

    /// <summary>
    /// True when this Purchase/Sales <b>accounting (service) invoice</b> is GST-aware — accounting-invoice mode is on
    /// AND the company has GST enabled. Only then does the screen resolve each Particulars line's SAC-based GST from
    /// the ledger's GST block, DISPLAY the tax + party total, and POST the additive tax lines. The sibling of
    /// <see cref="IsGstInvoice"/> for the accounting path; on a GST-off company it stays <c>false</c> and the invoice
    /// posts the plain income + party legs with no tax (byte-identical to a hand-keyed ledger-only sale, ER-13).
    /// </summary>
    public bool IsAccountingGstInvoice => IsAccountingInvoice && _company.GstEnabled;

    /// <summary>The shared gate for the GST totals band (CGST/SGST/IGST/Cess + party total): shown for a GST-aware
    /// Item invoice OR a GST-aware Accounting invoice. Repoints the band that previously read <see cref="IsGstInvoice"/>
    /// alone so the accounting path's computed tax is visible.</summary>
    public bool ShowGstTotals => IsGstInvoice || IsAccountingGstInvoice;

    /// <summary>The Particulars (service ledger + amount) grid is shown on an accounting invoice (Sales only — see
    /// <see cref="CanBeAccountingInvoice"/>, which <see cref="IsAccountingInvoice"/> already folds in).</summary>
    public bool ShowParticularsGrid => IsAccountingInvoice;

    /// <summary>The editable Accounting-Invoice Particulars lines (service-income / expense ledger + amount).</summary>
    public ObservableCollection<AccountingInvoiceLineViewModel> AccountingInvoiceLines { get; } = new();

    /// <summary>
    /// The service-income (Sales) / expense (Purchase) ledgers the Particulars line pickers choose from —
    /// Income/Expense-nature ledgers that are not GST tax ledgers.
    /// <para><b>An <see cref="ObservableCollection{T}"/>, rebuilt IN PLACE.</b> It used to be a ctor-built
    /// <c>.ToList()</c> snapshot handed to every row, which <see cref="RefreshMasterPickers"/> never rebuilt — so
    /// Alt+C create-on-the-fly was dead on the Particulars ledger field (measured: <c>AccountingInvoiceLedgers contains
    /// new ledger = False</c> while Parties and StockLedgers both refreshed True), and on a company with no income
    /// ledger the whole mode was unusable. Every row binds to THIS instance, so an in-place rebuild reaches all of them.</para>
    /// </summary>
    public ObservableCollection<DomainLedger> AccountingInvoiceLedgers { get; } = new();

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
    /// True only for a Purchase or Sales voucher: the header exposes the <b>counterparty captured field</b>
    /// (numbering-design-v2 §8) — "Supplier Invoice No." on a Purchase, "Reference No." on a Sales. Drives the
    /// field's visibility.
    /// </summary>
    public bool ShowReferenceCapture =>
        _type.BaseType is VoucherBaseType.Purchase or VoucherBaseType.Sales;

    /// <summary>The label for the counterparty captured field, per base type: "Supplier Invoice No." on a Purchase
    /// (the other party's number is the supplier's invoice number), "Reference No." on a Sales.</summary>
    public string ReferenceNoCaption =>
        _type.BaseType == VoucherBaseType.Purchase ? "Supplier Invoice No." : "Reference No.";

    /// <summary>
    /// The counterparty document number (numbering-design-v2 §8) — the OTHER party's number, captured as free text.
    /// It receives NO auto prefix/suffix/numbering (that is our own <see cref="VoucherNumber"/>); flowed to
    /// <see cref="Voucher.ReferenceNo"/> on Accept. Blank ⇒ null ⇒ byte-identical to a voucher without one (ER-13).
    /// </summary>
    [ObservableProperty] private string _referenceNo = string.Empty;

    /// <summary>
    /// The counterparty document's date as editable text (dd-MMM-yyyy); optional. Blank ⇒ no date. Parsed on
    /// Accept and flowed to <see cref="Voucher.ReferenceDate"/>; unparseable non-blank input is rejected (never
    /// silently discarded).
    /// </summary>
    [ObservableProperty] private string _referenceDateText = string.Empty;

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

        // Accounting-invoice (service) Particulars pickers — the service-income (Sales) / expense (Purchase) ledgers,
        // Income/Expense-nature and never a GST tax ledger. Always populated so the Ctrl+H mode switch is cheap; inert
        // on a type where the mode is unreachable. A ledger-only accounting invoice never touches any of the
        // item-invoice stock machinery.
        RebuildAccountingInvoiceLedgers();

        BuildItemInvoicePickers();
        BuildSection34Pickers(); // §34 note pickers (a no-op on any non-Credit/Debit-Note type)
        BuildAdvancePickers();   // outstanding-advance pickers (a no-op unless this type adjusts/refunds one)
        AddAdditionalCostRow(); // one blank trailing row ready to type into
        AddAccountingInvoiceLine(); // one blank trailing Particulars row ready to type into

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

        // …then seed the OPENING mode, which on Payment/Receipt/Contra is Single Entry (see SeedOpeningMode for the
        // corpus evidence). Strictly after the two AddLine calls: entering the mode stamps the Account/Particulars
        // polarity onto line 0 and line 1, so it needs them to exist. On a Payment this re-stamps the pair Cr/Dr,
        // inverting the Dr/Cr default two lines above — which is the whole point (BOOK p.32).
        SeedOpeningMode();

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

    // =============================================================== Single Entry mode (G-6; BOOK pp.26,29,31-32)

    /// <summary>
    /// True for the three cash/bank vouchers the corpus teaches in Single Entry — Contra (F4), Payment (F5) and
    /// Receipt (F6). Every other type has no Single Entry layout (Journal has no single cash side; Purchase/Sales
    /// use the invoice modes instead).
    /// </summary>
    public bool CanBeSingleEntry =>
        _type.BaseType is VoucherBaseType.Contra or VoucherBaseType.Payment or VoucherBaseType.Receipt;

    /// <summary>
    /// Whether this voucher is being entered in <b>Single Entry</b> mode: an <c>Account</c> field plus a
    /// <c>Particulars</c> list, no Dr/Cr labels. Only ever true when <see cref="CanBeSingleEntry"/>, so forcing
    /// <see cref="Mode"/> on a Journal cannot produce a screen with no grid.
    /// </summary>
    public bool IsSingleEntry => Mode == VoucherEntryMode.SingleEntry && CanBeSingleEntry;

    /// <summary>
    /// The side the <b>Account</b> field posts to — <b>and the single most dangerous value in this class</b>.
    /// <para>The corpus states the inversion twice, in as many words: Receipt/Contra <i>"Dr means Account &amp; Cr
    /// means Particulars"</i> (BOOK p.29); Payment <i>"Cr means Account &amp; Dr means Particulars"</i> (BOOK p.32).
    /// Flipping this reverses every cash and bank entry in the books, silently and in bulk.</para>
    /// </summary>
    public DrCr SingleEntryAccountSide =>
        _type.BaseType == VoucherBaseType.Payment ? DrCr.Credit : DrCr.Debit;

    /// <summary>The side the Particulars lines post to — always the opposite of the Account side.</summary>
    public DrCr SingleEntryParticularsSide =>
        SingleEntryAccountSide == DrCr.Debit ? DrCr.Credit : DrCr.Debit;

    /// <summary>
    /// The Account line — the ONE cash/bank side. It is <see cref="Lines"/>[0] re-presented, not a separate object:
    /// Single Entry is a view over the same collection the classic grid posts from, which is what keeps the posting
    /// path, the validators and every sub-panel completely untouched (D5).
    /// </summary>
    public VoucherLineViewModel? SingleEntryAccountLine => Lines.Count > 0 ? Lines[0] : null;

    /// <summary>The ledger picked in the <c>Account</c> field (the cash/bank side).</summary>
    public DomainLedger? SingleEntryAccount
    {
        get => SingleEntryAccountLine?.SelectedLedger;
        set
        {
            if (SingleEntryAccountLine is not { } line) return;
            line.SelectedLedger = value;
            SyncSingleEntrySides();
            OnPropertyChanged();
            Recalculate();
        }
    }

    /// <summary>
    /// The <c>Particulars</c> rows (the many side) — <see cref="Lines"/> from index 1 onward. A live projection, so
    /// adding or removing a particular is an ordinary line operation and nothing has to be kept in step.
    /// </summary>
    public IReadOnlyList<VoucherLineViewModel> SingleEntryParticulars =>
        Lines.Skip(1).ToList();

    /// <summary>
    /// The Account amount — <b>derived</b>, never typed: it is Σ of the Particulars amounts, which is what makes the
    /// voucher balance by construction. (In Single Entry the operator states only the many side; Tally fills the one
    /// side. Typing it would let the two disagree, and the balance rule would reject the voucher with no obvious
    /// cause.)
    /// </summary>
    public decimal SingleEntryAccountTotal
    {
        get
        {
            var sum = 0m;
            foreach (var l in Lines.Skip(1)) sum += l.ParsedAmount;
            return sum;
        }
    }

    /// <summary>
    /// The polarity reminder shown under the Single-Entry grid. The corpus states it in as many words on each
    /// screen, because the inversion between Payment and Receipt/Contra is the documented trap.
    /// </summary>
    public string SingleEntryModeHint =>
        SingleEntryAccountSide == DrCr.Debit
            ? "Single Entry — Account is debited, Particulars are credited."
            : "Single Entry — Account is credited, Particulars are debited.";

    /// <summary>Adds a Particulars row on the correct (non-Account) side.</summary>
    public VoucherLineViewModel AddSingleEntryParticular()
    {
        var line = AddLine(SingleEntryParticularsSide);
        OnPropertyChanged(nameof(SingleEntryParticulars));
        Recalculate();
        return line;
    }

    // =============================================================== settlement pre-load (Phase 10.11 S2 / VL-4)

    /// <summary>
    /// The report as-of a settlement pre-load was validated against, or null on an ordinary voucher. Its presence
    /// is what arms <see cref="SettlementAllocationError"/>; on every hand-keyed voucher this class behaves
    /// exactly as before.
    /// </summary>
    private DateOnly? _settlementAsOf;

    /// <summary>
    /// Pre-loads the bills selected on the Outstandings report as Against-Reference allocations, so the operator
    /// confirms the date, the cash/bank ledger and every per-bill amount and then Accepts — which is what
    /// TallyPrime makes them do anyway [CORPUS-SG p.92 §5.5]. Replaces the deleted Ctrl+B path that posted the
    /// whole thing unasked (register row IV-5).
    ///
    /// <para><b>The Account (cash/bank) side is deliberately left EMPTY.</b> Defaulting it to a ledger named
    /// "Cash" is the defect this slice removes, wearing a new hat.</para>
    ///
    /// <para><b>Two orderings here are load-bearing, and getting either wrong fails silently.</b>
    /// (1) The mode is forced to Single Entry BEFORE anything is stamped: <see cref="SyncSingleEntrySides"/> is
    /// what derives the Account amount from Σ Particulars, and it returns immediately when
    /// <see cref="IsSingleEntry"/> is false — so a pre-load onto the wrong mode leaves the Account line at zero,
    /// Accept greyed, and no explanation on screen. (2) The party ledger is assigned BEFORE its allocations:
    /// assignment fires <see cref="VoucherLineViewModel.SyncBillWise"/>, which seeds one blank New-Ref row on a
    /// bill-wise ledger and CLEARS the collection on a non-bill-wise one, so allocations stamped first are
    /// wiped.</para>
    ///
    /// <para>The blank starter rows are REUSED, never appended beside: the screen opens with one blank Particulars
    /// line and the ledger assignment seeds one blank bill row. A leftover blank is <c>IsBlank</c>, so
    /// <see cref="VoucherLineViewModel.BillSplitOk"/> ignores it silently — it would pass every test while
    /// rendering on screen as an empty row that reads as a bug.</para>
    /// </summary>
    public void PreloadSettlement(SettlementPreload preload)
    {
        ArgumentNullException.ThrowIfNull(preload);
        if (!CanBeSingleEntry)
            throw new InvalidOperationException(
                $"A settlement pre-load needs a Single-Entry cash/bank voucher; '{_type.Name}' has no such layout.");

        Mode = VoucherEntryMode.SingleEntry;   // idempotent — guarantees the derived-Account stamp is live
        _settlementAsOf = preload.AsOf;

        foreach (var party in preload.Parties)
        {
            // A party with NO allocations describes nothing to settle. Skipping it is not just tidiness: the
            // cleanup loop below cannot reach a target of zero (see the note there), and the line it would
            // otherwise leave behind is a zero-amount party row the operator did not ask for.
            if (party.Allocations.Count == 0) continue;

            // Reuse the blank starter Particulars line for the FIRST party actually stamped, then add one per
            // party after it. Keyed on the line still being blank, NOT on the loop index: a skipped empty party
            // at index 0 would leave the starter unconsumed, so an index test would append beside it and strand
            // exactly the stray empty row this reuse exists to prevent.
            var line = Lines.Count > 1 && Lines[1].IsBlank ? Lines[1] : AddSingleEntryParticular();

            line.SelectedLedger = party.Party;   // FIRST — see the ordering note above
            var total = party.Allocations.Sum(a => a.Amount.Amount);
            line.AmountText = MoneyText(total);

            for (var i = 0; i < party.Allocations.Count; i++)
            {
                var allocation = party.Allocations[i];
                var row = i < line.BillAllocations.Count
                    ? line.BillAllocations[i]                                // reuse the seeded blank row
                    : line.AddBillAllocation(BillRefType.AgstRef);
                row.RefType = BillRefType.AgstRef;
                row.Name = allocation.Name;
                row.AmountText = MoneyText(allocation.Amount.Amount);
            }

            // Nothing should be left over, but a stale seed beside stamped rows is exactly the silent-blank-row
            // failure described above — so drop any, rather than trust the arithmetic above.
            //
            // THE SECOND CONDITION IS NOT REDUNDANT. VoucherLineViewModel.RemoveBillAllocation enforces a floor:
            // it RETURNS WITHOUT REMOVING when the line is down to one row. Without `Count > 1` the loop stops
            // making progress the moment it reaches that floor and spins forever on the UI thread — the app would
            // have to be killed. The `continue` above makes a target of zero unreachable today, but the floor is
            // owned by another class and this loop must be safe on its own terms.
            while (line.BillAllocations.Count > party.Allocations.Count && line.BillAllocations.Count > 1)
                line.RemoveBillAllocation(line.BillAllocations[^1]);
        }

        Recalculate();
    }

    private static string MoneyText(decimal value)
        => value.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>
    /// Re-validates every Against-Reference row of a PRE-LOADED settlement against the books, returning the
    /// engine's own message on the first failure and null when all is well.
    ///
    /// <para><b>Why this exists at all.</b> <c>BillSettlementService.SettleAndPost</c> was the only caller of
    /// <c>BuildSettlementAllocations</c>, i.e. the only path in the app that ever checked an Agst-Ref against a
    /// genuinely open bill or capped a knock at its pending amount. The in-voucher Bill-wise panel binds the bill
    /// name to a plain <c>TextBox</c> (register defect D5) and <c>VoucherValidator.EnsureBillAllocationsValid</c>
    /// only checks that allocations sum to the line amount — so deleting the posting without this guard would
    /// have made settlement strictly LESS safe than the defect being removed. One transposed character would post
    /// a knock against a bill that does not exist, and <c>Outstandings</c> drops a non-positive pending, so the
    /// real bill stays open while an orphan reference vanishes from the report entirely.</para>
    ///
    /// <para><b>Scope, stated so it is not mistaken for a D5 fix.</b> It arms only on a voucher this class
    /// pre-loaded (<see cref="_settlementAsOf"/> is set). A hand-keyed Receipt with a typed Agst-Ref is validated
    /// no more than before — that is D5's slice, and widening it needs
    /// <c>VoucherValidator.EnsureBillAllocationsValid</c> to take the <c>Company</c> it currently does not.</para>
    ///
    /// <para>The as-of is the report's, captured at pre-load: it is the exact set of open bills the operator was
    /// looking at when they chose. Re-deriving it from the (operator-editable) voucher date would refuse a
    /// legitimate settlement back-dated before the bill was raised.</para>
    /// </summary>
    private string? SettlementAllocationError()
    {
        if (_settlementAsOf is not { } asOf) return null;

        // POOLED PER PARTY, NOT PER LINE. BuildSettlementAllocations caps the knocks naming one bill by their
        // running TOTAL, which only works if it sees them all at once. Validating line-by-line would hand it two
        // separate single-knock batches whenever the operator adds a second Particulars row for the SAME party —
        // each batch passes, and their sum over-settles the bill exactly as two rows on one line would.
        var byParty = new Dictionary<Guid, (DomainLedger Ledger, List<BillSettlementService.Knock> Knocks)>();
        foreach (var line in Lines)
        {
            if (line.SelectedLedger is not { } ledger || !line.IsBillWise) continue;

            var knocks = line.BillAllocations
                .Where(a => a.RefType == BillRefType.AgstRef && !a.IsBlank)
                .Select(a => new BillSettlementService.Knock(
                    (a.Name ?? string.Empty).Trim(), new Money(a.ParsedAmount)))
                .ToList();
            if (knocks.Count == 0) continue;

            if (byParty.TryGetValue(ledger.Id, out var existing)) existing.Knocks.AddRange(knocks);
            else byParty[ledger.Id] = (ledger, knocks);
        }

        var service = new BillSettlementService(_company);
        foreach (var (ledger, knocks) in byParty.Values)
        {
            try { service.BuildSettlementAllocations(ledger, asOf, knocks); }
            catch (InvalidOperationException ex) { return ex.Message; }
        }
        return null;
    }

    /// <summary>Removes a Particulars row (never the Account line, and never below the two-line minimum).</summary>
    public void RemoveSingleEntryParticular(VoucherLineViewModel line)
    {
        if (Lines.Count <= 2 || ReferenceEquals(line, SingleEntryAccountLine)) return;
        Lines.Remove(line);
        OnPropertyChanged(nameof(SingleEntryParticulars));
        Recalculate();
    }

    /// <summary>
    /// Stamps the documented polarity onto the underlying lines and keeps the derived Account amount in step: line 0
    /// takes <see cref="SingleEntryAccountSide"/>, every other line the opposite, and the Account amount becomes
    /// Σ Particulars. A no-op outside Single Entry, so the classic grid keeps full manual control of both sides.
    /// </summary>
    private void SyncSingleEntrySides()
    {
        if (!IsSingleEntry) return;

        // 🔴 NEVER ON AN ALTERING SCREEN THAT DID NOT OPEN IN SINGLE ENTRY (finding L1-02). This stamp is a no-op by
        // construction on a voucher genuinely keyed in Single Entry — which is what SeedAlterationMode tests for —
        // but on one keyed in the Dr/Cr grid it FLIPS every side and REWRITES line 0's amount. One Ctrl+H did
        // exactly that: an expense became an income, cash went UP on a payment, and the replacement still balanced,
        // so nothing downstream objected.
        //
        // The guard is here rather than only on the accept, because the damage is done on the way IN and Ctrl+H
        // does not undo it: OnModeChanged's own comment records that "leaving it simply stops re-stamping, so the
        // lines survive the flip intact" — so a gate that only refused while IsSingleEntry was true would be walked
        // past by pressing Ctrl+H twice. Blocking the stamp itself makes the mode a pure view switch on an altering
        // screen, and AcceptAlteration still refuses to POST from that view (it does not describe the voucher).
        if (IsAltering && !_alteringPostedAsSingleEntry) return;

        if (_syncingSingleEntry) return;
        _syncingSingleEntry = true;
        try
        {
            for (var i = 0; i < Lines.Count; i++)
                Lines[i].Side = i == 0 ? SingleEntryAccountSide : SingleEntryParticularsSide;

            if (SingleEntryAccountLine is { } account)
            {
                var total = SingleEntryAccountTotal;
                var text = total.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);
                if (account.AmountText != text) account.AmountText = text;
            }
        }
        finally
        {
            _syncingSingleEntry = false;
        }
    }

    /// <summary>Re-entrancy guard: stamping a side/amount raises change notifications that re-enter Recalculate.</summary>
    private bool _syncingSingleEntry;

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
        // 🔴 S5b — inert while a posted voucher is being re-keyed line by line. In Single Entry this method stamps
        // line 0's amount to Σ of the remaining lines, so running it against a half-built collection would zero the
        // account line before the rest of the voucher existed. RehydrateFrom calls it once, at the end.
        if (_rehydrating) return;

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

        // Likewise in accounting-invoice (service) mode: the Particulars grid is the Accept gate, not the plain
        // Dr/Cr grid. BLOCKER-1 — without this route Recalculate() falls through to the plain-grid branch and stomps
        // CanAccept to false from the empty starter Lines, permanently disabling Accept in accounting mode.
        if (IsAccountingInvoice) { RecalculateAccountingInvoice(); return; }

        // G-6: Single Entry keeps posting through THIS branch (it is a re-render of the same Lines), so all it needs
        // is the polarity + derived-Account stamp before the totals are summed. Ordering matters: the Account amount
        // is Σ Particulars, so stamping it first is what makes the balance check below pass by construction.
        SyncSingleEntrySides();

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

        // On a PRE-LOADED settlement only, an Agst-Ref must still name a genuinely open bill and must not exceed
        // its pending amount — the check SettleAndPost used to be the sole owner of (see SettlementAllocationError).
        // A no-op on every hand-keyed voucher, so nothing else in the app changes shape.
        if (CanAccept && SettlementAllocationError() is not null) CanAccept = false;
    }

    // =============================================================== TDS withholding (catalog §13; Phase 7 slice 2)

    /// <summary>The <b>shape</b> of a potential TDS withholding on the plain grid: a complete <i>Is-TDS-Applicable</i>
    /// expense/purchase <b>debit</b> leg (which drives applicability AND the default section) plus a complete
    /// deductee-party <b>credit</b> line (positive amount = the gross obligation). When the shape holds the panel
    /// shows; the operator may still decline via the "Not Applicable" sentinel.</summary>
    /// <param name="PartyLine">The plain-grid party line the carve-out replaces. <b>Null in accounting-invoice
    /// mode</b> (G-7), where the party leg is DERIVED at Accept rather than typed — the accounting Accept path
    /// applies the carve-out to the leg it builds instead of matching a grid row by reference.</param>
    private readonly record struct TdsShape(
        VoucherLineViewModel? PartyLine, DomainLedger Deductee, Money Gross, DomainLedger Expense);

    /// <summary>The resolved context of a <b>firing</b> TDS withholding: the deductee party's Cr line, the deductee
    /// ledger, the gross obligation, and the Nature of Payment (section) — resolved from the EXPENSE ledger's default
    /// (or the operator's override), never the party's default.</summary>
    private readonly record struct TdsContext(
        VoucherLineViewModel? PartyLine, DomainLedger Deductee, Money Gross, NatureOfPayment Nature);

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

        // G-7: in accounting-invoice mode the plain grid is EMPTY — the expense legs are the Particulars lines and
        // the party is the header party. Reading Lines here is exactly what dropped the §194J carve-out.
        if (IsAccountingInvoice) return DetectAccountingTdsShape();

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
    /// G-7 — the TDS shape on a <b>Purchase accounting invoice</b>, read from the Particulars lines instead of the
    /// (empty) plain grid. The mapping is exact:
    /// <list type="bullet">
    ///   <item>the EXPENSE Dr legs are the complete Particulars lines — applicability and the default section come
    ///     from the first one flagged <see cref="Ledger.TdsApplicable"/>, exactly as on the grid;</item>
    ///   <item>the deductee party is the header party (it carries a <see cref="Ledger.DeducteeType"/>);</item>
    ///   <item>the gross obligation is the party total the derived Cr leg will carry (taxable + GST + cess) — the
    ///     party is obliged to be paid the tax-inclusive invoice, and the withholding is then assessed on the
    ///     GST-EXCLUSIVE base separately (<see cref="AssessableExGst"/>, CBDT Circular 23/2017).</item>
    /// </list>
    /// A <b>Sales</b> accounting invoice returns null: withholding is the purchaser's obligation, never the
    /// seller's, so the Sales arm of this mode is untouched by the rewire (ER-13).
    /// </summary>
    private TdsShape? DetectAccountingTdsShape()
    {
        if (!IsPurchaseInvoice) return null;
        if (SelectedParty?.Ledger is not { } party || !IsDeducteeLedger(party)) return null;

        var expense = AccountingInvoiceLines
            .FirstOrDefault(l => l.IsComplete && l.SelectedLedger is { TdsApplicable: true })?.SelectedLedger;
        if (expense is null) return null; // no Is-TDS-Applicable Particulars line ⇒ no withholding

        var gross = AccountingInvoicePartyAmount();
        if (gross.Amount <= 0m) return null;

        return new TdsShape(PartyLine: null, party, gross, expense);
    }

    /// <summary>Σ of the complete Particulars lines — the accounting invoice's taxable (GST-exclusive) value.</summary>
    private Money AccountingInvoiceTaxable()
    {
        var sum = 0m;
        foreach (var l in AccountingInvoiceLines)
            if (l.IsComplete && l.ParsedAmount is { } a) sum += a;
        return new Money(sum);
    }

    /// <summary>
    /// The amount the derived party leg of an accounting invoice will carry: taxable + GST + cess. Pure and
    /// exception-safe — an unresolvable GST input falls back to the bare taxable value here and is refused with a
    /// friendly message by the Accept path, which re-runs the same compute.
    /// </summary>
    private Money AccountingInvoicePartyAmount()
    {
        var taxable = AccountingInvoiceTaxable();
        if (!IsAccountingGstInvoice) return taxable;
        try
        {
            if (ComputeAccountingInvoiceGst() is not { HasUnresolved: false } gst) return taxable;
            return new Money(taxable.Amount + gst.Tax.TotalTax.Amount + gst.Tax.TotalCess.Amount);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            return taxable;
        }
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
        // G-7: on an accounting invoice the Particulars lines are ALREADY GST-exclusive — the tax legs are derived
        // additively and never appear as Particulars rows — so the taxable total IS the assessable base. Reading
        // the empty plain grid here would have assessed TDS on ₹0.
        if (IsAccountingInvoice) return AccountingInvoiceTaxable();

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
                // 🔴 ON AN ALTERING SCREEN THE PANEL MUST EXCLUDE THE VOUCHER'S OWN POSTED ASSESSMENT, exactly
                // as the accept path does (ER-4: one engine, and one set of arguments to it). Without this the panel
                // read the voucher back as its own "prior": a below-threshold 30,000.30 fee re-opened showing
                // "TDS 194J(b) @ 10%: 3,000.00 withheld - Net payable 27,000.30" while AcceptAlteration posted the
                // full gross and no payable leg at all. The figure was on screen before any keystroke, because
                // RehydrateFrom ends in Recalculate().
                carve = _tds.BuildCarveOut(
                    ctx.Gross, AssessableExGst(), ctx.Nature, ctx.Deductee, Date, AlterationProjectionMarker,
                    postedRateBasisPoints: AlterationPostedTdsRateBasisPoints,
                    postedAssessableValue: AlterationPostedTds?.AssessableValue,
                    postedTdsAmount: AlterationPostedTds?.TdsAmount);
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

    /// <summary>
    /// The voucher whose POSTING MOMENT the cumulative-FY threshold projection must be taken at — the voucher being
    /// altered, or <c>null</c> on a fresh entry screen (there is nothing to exclude: the voucher is not in the book
    /// yet). Every <see cref="TdsService.BuildCarveOut"/> call on this screen passes it, so the panel, the bill-wise
    /// preview and the accept path cannot disagree about which transactions count towards the threshold.
    /// </summary>
    private Guid? AlterationProjectionMarker => IsAltering ? _alteringVoucherId : null;

    /// <summary>
    /// 🔴 <b>The rate the voucher being altered was POSTED with</b> — read straight off its own stamped
    /// <c>TdsLineTax.RateBasisPoints</c>; <c>null</c> on a fresh entry screen. Handed to every
    /// <see cref="TdsService.BuildCarveOut"/> call on this screen so the advisory panel, the bill-wise net preview
    /// and the accept path cannot disagree about a <b>grandfathered §194C rate</b> (ER-4: one engine, one set of
    /// arguments to it). Without it the panel would read "§194C @ 2%" while Accept posted 1% — exactly the class
    /// of defect <c>VoucherAlterDerivedLegDriftTests.The_withholding_panel_on_an_altering_screen_states_what_accept
    /// _will_post</c> exists to catch.
    /// <para>The ACCEPT path does not use this property: <see cref="ApplyReCarve"/> passes
    /// <c>pin.RateBasisPoints</c>, which <c>VoucherAlterationDerivedLegs.Invert</c> already read off the posted
    /// voucher and validated on the way in. Both are the same number by construction; the pin is the stronger
    /// source, so the path that moves money uses it.</para>
    /// </summary>
    private int? AlterationPostedTdsRateBasisPoints => AlterationPostedTds?.RateBasisPoints;

    /// <summary>
    /// 🔴 <b>The withholding detail the voucher being altered was POSTED with</b> — its stamped
    /// <see cref="TdsLineTax"/>, or <c>null</c> on a fresh entry screen. It carries three facts about the voucher
    /// that the engine needs and cannot recover for itself: the rate it was posted at (the <b>§194C</b>
    /// grandfathering carrier) and the assessable base and TDS amount it was posted with (together the
    /// <b>§194-I</b> one — see <c>TdsService.GrandfatheredLiability</c>). Every
    /// <see cref="TdsService.BuildCarveOut"/> call on this screen feeds all three, so the advisory panel, the
    /// bill-wise net preview and the accept path cannot disagree (ER-4: one engine, one set of arguments).
    /// </summary>
    private TdsLineTax? AlterationPostedTds =>
        IsAltering ? PostedTdsDetail(_company.FindVoucher(_alteringVoucherId)) : null;

    /// <summary>The one <see cref="TdsLineTax"/> a posted voucher carries (on the TDS-Payable leg when it
    /// withheld, on the party leg when it was assessed below threshold); <c>null</c> when it carries none.</summary>
    private static TdsLineTax? PostedTdsDetail(Voucher? posted) =>
        posted?.Lines.Select(l => l.Tds).FirstOrDefault(t => t is not null);

    /// <summary>
    /// The deductee's grid row as an <see cref="EntryLine"/> — ledger, amount, side and every keyed child. Handed to
    /// <see cref="TdsService.BuildCarveOut"/> so the derived party leg keeps the bill-wise / cost-centre / bank /
    /// forex detail instead of the carve destroying it (both accept paths SPLICE the derived leg over this whole
    /// row, so anything the builder does not put back is gone).
    /// </summary>
    private static EntryLine? KeyedPartyTemplate(VoucherLineViewModel? line)
    {
        // Null on the accounting-invoice path, where the party leg is BUILT rather than keyed on a grid row (its
        // bill-wise panel is already targeted at the net, so there is nothing to carry and nothing to re-derive).
        if (line?.SelectedLedger is null) return null;
        var bills = line.ToBillAllocations();
        var costs = line.ToCostAllocations();
        return new EntryLine(
            line.SelectedLedger.Id, new Money(line.ParsedAmount), line.Side,
            bills.Count > 0 ? bills : null,
            costs.Count > 0 ? costs : null,
            line.ToBankAllocation(),
            line.ToForexInfo());
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
    /// <param name="PartyLine">The plain-grid supplier line. <b>Null in accounting-invoice mode</b> (G-7), where the
    /// supplier is the header party and no grid row exists.</param>
    private readonly record struct RcmShape(
        IReadOnlyList<RcmLeg> Legs, VoucherLineViewModel? PartyLine, DomainLedger Party)
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

        // G-7: same rewire as TDS — in accounting-invoice mode the expense legs are the Particulars lines and the
        // supplier is the header party. Reading Lines here made RCM mis-evaluate to "no shape" on every service
        // purchase, silently under-collecting the cash-only §49(4) liability.
        if (IsAccountingInvoice) return DetectAccountingRcmShape();

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

    /// <summary>
    /// G-7 — the reverse-charge shape on a <b>Purchase accounting invoice</b>, read from the Particulars lines.
    /// Mirrors the plain-grid detector clause for clause: RCM-flagged expense ledgers grouped by ledger (so one head
    /// booked across several rows is ONE dual leg on the summed value, while distinct notified heads keep their own
    /// rates), plus a genuine supplier — here the header party. A Sales accounting invoice returns null: reverse
    /// charge is an inward-supply mechanism.
    /// </summary>
    private RcmShape? DetectAccountingRcmShape()
    {
        if (!IsPurchaseInvoice) return null;
        if (SelectedParty?.Ledger is not { } party || !IsSupplierLedger(party)) return null;

        var legs = AccountingInvoiceLines
            .Where(l => l.IsComplete && l.SelectedLedger is { SalesPurchaseGst.ReverseChargeApplicable: true })
            .GroupBy(l => l.SelectedLedger!.Id)
            .Select(g => new RcmLeg(g.First().SelectedLedger!, new Money(g.Sum(l => l.ParsedAmount ?? 0m))))
            .Where(leg => leg.Taxable.Amount > 0m)
            .ToList();
        if (legs.Count == 0) return null;

        return new RcmShape(legs, PartyLine: null, party);
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
        // 🔴 Phase 10.11 S3 — `!v.Cancelled` IS LOAD-BEARING, and it went in with the slice that made Alt+X
        // reachable. This picker offers the ORIGINAL SUPPLY a §34 credit/debit note adjusts, and the filter was
        // base type ALONE. A cancelled invoice has zero effect on the books, so choosing one as the original
        // would write a GstCreditDebitNoteLink pointing at a supply that never happened — a note adjusting
        // nothing, carried into GSTR-1 against a document the recipient can never match. The leak was latent only
        // because nothing in the UI could cancel a voucher; it goes live the moment this slice ships, which is
        // why it is closed HERE and not deferred.
        foreach (var v in _company.Vouchers
                     .Where(v => !v.Cancelled
                         && _company.FindVoucherType(v.TypeId)?.BaseType
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
        // 🔴 Phase 10.11 S3 — THE THIRD PICKER LEAK, and it is inside the same method as the second. This method
        // builds TWO lists and only the invoice list below originally got the cancelled filter. `LedgerService.Cancel`
        // sets the voucher's flag and touches nothing else, so the `GstAdvanceReceipt` survives a cancelled booking
        // receipt untouched and this list went on offering it — on BOTH routes, adjust (Journal) and refund
        // (Payment). Harm is the mirror image of the invoice leak: once the receipt is cancelled its
        // `Cr Output {head}` / `Dr Output Tax on Advances` pair is off the books, so adjusting or refunding against
        // it releases suspense that was never recognised and marks the advance settled from a voucher with zero
        // effect. Filtering on the RECEIPT VOUCHER rather than on the record, because the record has no flag.
        foreach (var a in _company.AdvanceReceipts
                     .Where(a => a.AdjustedAgainstInvoiceVoucherId is null && a.RefundVoucherId is null
                         && _company.FindVoucher(a.ReceiptVoucherId) is { Cancelled: false }))
            OutstandingAdvances.Add(new AdvanceReceiptOption { Receipt = a, Display = AdvanceDisplay(a) });
        SelectedOutstandingAdvance = OutstandingAdvances.FirstOrDefault();

        AdvanceInvoices.Clear();
        AdvanceInvoices.Add(new AdvanceInvoiceOption { Display = "◦ (none selected)" });
        // 🔴 Phase 10.11 S3 — the SECOND of the two picker leaks, closed for the same reason as the §34 one
        // above: this list offers the invoice an outstanding advance is ADJUSTED AGAINST, and it filtered on base
        // type alone. Adjusting an advance against a cancelled sale would retire real advance tax against a
        // supply with no value, and the advance would be marked settled by an invoice that is not on the books.
        foreach (var v in _company.Vouchers
                     .Where(v => !v.Cancelled
                         && _company.FindVoucherType(v.TypeId)?.BaseType == VoucherBaseType.Sales)
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

    // =============================================================== Phase 10.11 S5b — ALTER a posted voucher

    /// <summary>The posted voucher this screen was opened to ALTER, or <see cref="Guid.Empty"/> for a fresh entry.</summary>
    private Guid _alteringVoucherId;

    /// <summary>
    /// Suppresses <see cref="Recalculate"/> while <see cref="RehydrateFrom(Voucher)"/> is mid-flight.
    ///
    /// <para>🔴 <b>Not an optimisation — a correctness guard.</b> In Single Entry, <c>Recalculate</c> calls
    /// <c>SyncSingleEntrySides</c>, which OVERWRITES line 0's amount with Σ of the remaining lines. Rehydration adds
    /// the posted lines one at a time, so the very first of those calls would see a single line, compute Σ of
    /// nothing, and stamp the account line's amount to <b>zero</b> — silently, before any of the other lines
    /// existed. One <c>Recalculate</c> runs at the end instead, when the collection is whole.</para>
    /// </summary>
    private bool _rehydrating;

    /// <summary>
    /// Whether the voucher being altered was POSTED in the Single-Entry shape — captured once by
    /// <see cref="RehydrateFrom(Voucher)"/> from the same predicate <see cref="SeedAlterationMode"/> used, and read
    /// by <see cref="SyncSingleEntrySides"/> so a Ctrl+H on a grid-keyed voucher cannot re-stamp its sides
    /// (finding L1-02). It is re-derived from the POSTED voucher rather than read off screen state, because screen
    /// state is precisely what a stray mode change has already corrupted.
    /// </summary>
    private bool _alteringPostedAsSingleEntry;

    /// <summary>True when this screen is altering a POSTED voucher rather than entering a new one.</summary>
    public bool IsAltering => _alteringVoucherId != Guid.Empty;

    /// <summary>The posted voucher being altered, or <see cref="Guid.Empty"/> for a fresh entry.</summary>
    public Guid AlteringVoucherId => _alteringVoucherId;

    /// <summary>
    /// 🔴 <b>S5b's entry door — opens this screen on a POSTED voucher, pre-filled, or refuses BY NAME.</b>
    ///
    /// <para>The result is never a bare <c>null</c> and never a silent no-op: it holds either a rehydrated view
    /// model or a family-specific sentence saying why this voucher's posted shape cannot be rebuilt from the entry
    /// screen. See <see cref="VoucherAlterationEligibility"/> for the thirty-row enumeration behind those refusals,
    /// and design §6.6a for their derivation.</para>
    ///
    /// <para><b>Accepting an alteration is <see cref="AcceptAlteration"/>, NOT <see cref="Accept"/></b>, and
    /// <see cref="Accept"/> hard-refuses on an altering screen. <c>Accept</c> is build + <c>Post</c> + REGISTRATION
    /// SIDE EFFECTS: it re-runs <c>DetectTdsContext</c>, <c>DetectRcmShape</c> and <c>BuildAdvanceLines</c> against
    /// <b>today's</b> masters, so a voucher that carried no withholding carve at posting could ACQUIRE one on a
    /// narration-only alteration — and one that carried a carve could lose it. It would also mint a fresh
    /// <see cref="Guid"/> and post a SECOND voucher, leaving the original standing (§6.6a.6, fourth thing).</para>
    /// </summary>
    public static VoucherAlterationOpen ForAlter(
        Company company,
        Guid voucherId,
        CompanyStorage storage,
        Action onSaved,
        Action onCancelled)
    {
        ArgumentNullException.ThrowIfNull(company);

        if (VoucherAlterationEligibility.RefusalFor(company, voucherId) is { } refusal)
            return VoucherAlterationOpen.Refused(refusal);

        // Both are non-null: RefusalFor returned null, which it only does after resolving each of them.
        var voucher = company.FindVoucher(voucherId)!;
        var type = company.FindVoucherType(voucher.TypeId)!;

        // The date is deliberately NOT passed to the constructor: RehydrateFrom sets it, and having exactly one
        // writer is what makes that assignment falsifiable by a test (a voucher that is not the latest in the book
        // would otherwise open on the constructor's default and no assertion could tell).
        var entry = new VoucherEntryViewModel(company, type, storage, onSaved, onCancelled);
        return entry.RehydrateFrom(voucher) is { } lineRefusal
            ? VoucherAlterationOpen.Refused(lineRefusal)
            : VoucherAlterationOpen.Opened(entry);
    }

    /// <summary>
    /// Re-keys this freshly-constructed screen from <paramref name="voucher"/>. Returns <c>null</c> on success, or a
    /// named refusal when a line cannot be re-keyed (the master-drift and forex cases
    /// <see cref="VoucherLineViewModel.RehydrateFrom"/> owns, which need the REAL panel gates and so cannot be
    /// decided from the posted voucher alone).
    ///
    /// <para>🔴 <b>The provisional-state vector is carried onto the live header properties, not into a frozen
    /// snapshot</b> (design §12.8 consequence 2). <c>Replace</c> REFUSES a change to <c>Optional</c>,
    /// <c>PostDated</c> or <c>ApplicableUpto</c>, so a rehydration that dropped one would turn a silent balance
    /// move into a loud failure — which is exactly why that refusal exists. Carrying them onto the live properties
    /// (rather than freezing them and rebuilding from the frozen copy) keeps the OTHER half honest too: an operator
    /// who really does press Ctrl+L gets the engine's refusal naming the verb they should have used, instead of
    /// having their keystroke silently ignored.</para>
    /// </summary>
    private string? RehydrateFrom(Voucher voucher)
    {
        // 🔴 S5c — INVERT the engine's own legs FIRST, so the grid holds what the operator KEYED. On a
        // TDS-carved voucher the posted party leg is the DERIVED net and a separate TDS-Payable leg sits beside it;
        // filling the grid from the posted lines would show the operator a net they never typed and, on accept,
        // re-carve THAT net — drifting the party credit by exactly the withholding. The refusal returned here is
        // the same one VoucherAlterationEligibility.DerivedLegRefusal already made, so this cannot open a shape the
        // predicate refuses; it is repeated because the two must never diverge and there is only one implementation.
        if (VoucherAlterationDerivedLegs.Invert(_company, voucher, out var inverted) is { } inversionRefusal)
            return inversionRefusal;

        _alteringVoucherId = voucher.Id;
        _rehydrating = true;
        try
        {
            SeedAlterationMode(voucher);

            // The voucher's OWN number, not the NextNumber preview the constructor computed — this screen is not
            // adding to the sequence. Replace accepts a replacement carrying the voucher's own number by name.
            VoucherNumber = voucher.Number;
            Date = voucher.Date;
            Narration = voucher.Narration ?? string.Empty;

            // 🔴 the provisional-state vector — see this method's summary.
            IsOptional = voucher.Optional;
            IsPostDated = voucher.PostDated;
            if (voucher.ApplicableUpto is { } upto) ApplicableUptoText = ApexDate.Format(upto);

            // The counterparty capture is only keyed on a Purchase/Sales; on every other type AcceptAlteration
            // carries the posted values straight through instead (TryResolveReferenceCapture hands back null/null
            // off those two natures, which would DROP a reference an import had put there).
            if (ShowReferenceCapture)
            {
                ReferenceNo = voucher.ReferenceNo ?? string.Empty;
                ReferenceDateText = voucher.ReferenceDate is { } refDate ? ApexDate.Format(refDate) : string.Empty;
            }

            Lines.Clear();
            foreach (var posted in inverted!.KeyedLines)
            {
                var line = AddLine(posted.Side);
                if (line.RehydrateFrom(posted) is { } refusal)
                    return "This voucher cannot be re-opened for alteration: " + refusal;
            }

            // 🔴 The withholding panel opens on the section that was POSTED, not on the expense ledger's
            // default. Without this the panel would default through DefaultNatureFor(expense) — and if that master
            // default has moved since posting, the operator would be shown a section the voucher never carried and
            // AcceptAlteration's pin check would refuse an alteration nobody had changed.
            if (inverted.Tds is { } tdsPin && _company.FindNatureOfPayment(tdsPin.NatureId) is { } postedNature)
                SelectedTdsNature = postedNature;
        }
        finally
        {
            _rehydrating = false;
        }

        Recalculate();
        return null;
    }

    /// <summary>
    /// 🔴 <b>Seeds the entry mode from the VOUCHER, never from the voucher type's opening default</b> (design
    /// §6.6a.6 answer 2). <c>HasInventoryLines</c> and <c>IsAccountingInvoice</c> are both PERSISTED, and
    /// <c>Replace</c> refuses a change to the latter by name — so a Sales type whose opening default is an item
    /// invoice would otherwise re-open a plain Dr/Cr Sales in the wrong grid and post a different voucher.
    ///
    /// <para><b>Single Entry is seeded only when the posted shape actually IS one, and that condition is not
    /// cosmetic.</b> Single Entry is not persisted — it is a re-render of the same lines — so it can only be
    /// inferred. <c>SyncSingleEntrySides</c> stamps line 0 to the account side, every other line to the opposite,
    /// and rewrites line 0's amount to Σ of the rest. On a voucher that genuinely was keyed in Single Entry those
    /// are all no-ops by construction; on a Payment keyed in the double-entry grid with two bank credits, they
    /// would silently FLIP a side and REWRITE an amount. So the shape is tested, and a voucher that does not match
    /// it re-opens in the plain Dr/Cr grid — which posts through the identical path.</para>
    /// </summary>
    private void SeedAlterationMode(Voucher voucher)
    {
        // 🔴 THESE TWO BRANCHES ARE UNREACHABLE TODAY, AND THAT IS STATED RATHER THAN IMPLIED (finding L2-10).
        // VoucherAlterationEligibility.EntryModeRefusal refuses BOTH families at the door — an item invoice by
        // HasInventoryLines and a service invoice by IsAccountingInvoice — so ForAlter never reaches this method
        // with either shape, and deleting either line reddens nothing. They are kept, not as a live safeguard, but
        // because S5c lifts exactly those two refusals: the branch that must exist on the day the family is served
        // is cheaper to keep than to remember. The live half of this method is the Single-Entry inference below,
        // which IS load-bearing and IS locked (dropping its shape clause kills five tests).
        if (voucher.HasInventoryLines) { Mode = VoucherEntryMode.ItemInvoice; return; }
        if (voucher.IsAccountingInvoice) { Mode = VoucherEntryMode.AccountingInvoice; return; }

        _alteringPostedAsSingleEntry = IsPostedAsSingleEntry(voucher);
        Mode = _alteringPostedAsSingleEntry ? VoucherEntryMode.SingleEntry : VoucherEntryMode.AsVoucher;
    }

    /// <summary>
    /// Whether <paramref name="voucher"/>'s posted lines are exactly the shape Single Entry produces — one
    /// account-side line FIRST, every other line on the opposite side. A balanced voucher of that shape has line
    /// 0's amount equal to Σ of the rest, so <c>SyncSingleEntrySides</c>'s stamp is provably a no-op on it.
    ///
    /// <para><b>One clause, deliberately, and here is why the obvious second one is not here.</b> The natural
    /// spelling adds <c>voucher.Lines[0].Side == SingleEntryAccountSide</c> — but for a POSTED voucher that is
    /// IMPLIED: the voucher is balanced, so if every line from index 1 onward sits on the particulars side, line 0
    /// must sit on the account side or Σ Dr ≠ Σ Cr. A mutation run confirmed it: deleting that clause reddened
    /// nothing, because no reachable voucher can fail it while passing the clause below. A guard no test can fail
    /// is dead code wearing the costume of safety, so it is stated in prose instead.</para>
    /// </summary>
    private bool IsPostedAsSingleEntry(Voucher voucher) =>
        CanBeSingleEntry
        && voucher.Lines.Skip(1).All(l => l.Side == SingleEntryParticularsSide);

    /// <summary>
    /// 🔴 <b>Accepts an ALTERATION — the same keying as <see cref="Accept"/>, with no registration side effect, and
    /// ending in <c>LedgerService.Replace</c>.</b> Returns <c>false</c> with <see cref="Message"/> set on any
    /// refusal.
    ///
    /// <para><b>Why it ends in <c>Replace</c> and never in <c>Post</c> (design §3.4, binding).</b>
    /// <c>BankAllocation.BankDate</c> is written onto a POSTED voucher by a later human action
    /// (<c>BankReconciliation.SetBankDate</c>) and exists NOWHERE on this screen, so
    /// <see cref="VoucherLineViewModel.ToBankAllocation"/> never writes one. A <c>Post</c> would therefore destroy
    /// every bank reconciliation date on the voucher, silently, with no test failing.
    /// <c>Replace.CarryBankDatesForward</c> is what compensates — including its ECHO rule, which exists precisely
    /// because this caller hands back the posted date it read.</para>
    ///
    /// <para><b>Why it re-runs eligibility.</b> The screen may have been open while a master moved. Eligibility is
    /// cheap and the alternative is a refusal from the engine phrased in terms the operator never saw.</para>
    ///
    /// <para><b>Line tax is not echoed, and cannot be.</b> Finding L3-07 binds this caller to RE-DERIVE line tax
    /// rather than carry a stale stamp, because GSTR-1 and GSTR-3B read the STAMPED taxable value, not the posted
    /// amounts — so an echo makes a return declare a figure the book does not hold. <see cref="BuildPlainEntryLines()"/>
    /// is constructed WITHOUT any <c>gst</c>/<c>tds</c>/<c>tcs</c> argument, so there is no code path here that could
    /// carry one forward; every derived leg on the replacement is built fresh by
    /// <see cref="ReDeriveEngineLegs"/> through the same engine that posted it.</para>
    ///
    /// <para>🔴 <b>S5c — and the DETECTION problem S5b left behind.</b> This method still runs NO detection of
    /// its own: the POSTED voucher decides WHETHER a leg is derived, the amended content decides HOW MUCH, and any
    /// disagreement is refused by name. That is why a narration-only alteration cannot ACQUIRE a withholding
    /// because a party master gained a <c>DeducteeType</c> after posting, and cannot LOSE one because a master
    /// lost it. See <see cref="VoucherAlterationDerivedLegs"/> for the rule in full.</para>
    /// </summary>
    public bool AcceptAlteration()
    {
        Message = null;

        // 🔴 THE SAME WHOLE-WINDOW ROLLBACK Accept HAS (see the `undo` stack there), and for the same measured
        // reason. ApplyReStamp calls the IMPURE RcmService.BuildReverseCharge — which calls
        // GstService.EnsureRcmOutputLedger — BEFORE the shape-drift check can refuse, so a REFUSED alteration was
        // leaving new tax ledgers on the company: measured, "RCM Output CGST" and "RCM Output SGST" added by a
        // refusal, the in-memory canonical export no longer identical, and then PERSISTED by the next unrelated
        // save. It reproduced on two unrelated fixtures — a supplier state moved after posting, and an
        // import-of-services voucher with NO master drift at all — so it is generic to any refusal whose
        // re-resolution reaches a tax head the book does not yet have.
        //
        // A LEDGER SNAPSHOT rather than a per-engine undo, on purpose: it catches every ledger any engine on this
        // path creates, including the ones a future family will add, without each engine having to report what it
        // made. And a WHOLE-WINDOW guard rather than a patch at each refusal exit, because this method has nine of
        // them and PostAndSave's own history is that a per-exit rollback leaks from the exits nobody thought of.
        var ledgersBefore = _company.Ledgers.Select(l => l.Id).ToHashSet();
        var committed = false;
        try
        {
            committed = AcceptAlterationCore();
            return committed;
        }
        finally
        {
            if (!committed) UnwindLedgersCreatedSince(ledgersBefore);
        }
    }

    /// <summary>
    /// The body of <see cref="AcceptAlteration"/>, run inside that method's rollback window. Returns false ⇒ refused
    /// with <see cref="Message"/> set, and every ledger the engines created on the way is unwound by the caller.
    /// </summary>
    private bool AcceptAlterationCore()
    {
        if (!IsAltering)
        {
            Message = "This screen is entering a new voucher, not altering a posted one — use Accept.";
            return false;
        }

        if (_company.FindVoucher(_alteringVoucherId) is not { } existing)
        {
            Message = "The voucher being altered is no longer in this company's books — it may have been deleted "
                    + "meanwhile. Nothing was changed.";
            return false;
        }

        // 🔴 Ctrl+H is still live on an altering screen, and it must not become a back door into a family S5b
        // refuses. Both invoice modes key their voucher from a DIFFERENT collection — InventoryLines and
        // AccountingInvoiceLines — which this method does not read at all, so accepting in one of them would post
        // the old plain-grid lines while the operator was looking at (and had possibly keyed) an invoice grid.
        // Cheaper and safer than trying to keep the two grids in step for a family whose inverse is not built yet.
        if (!IsAsVoucherMode)
        {
            Message = "This voucher is being altered on the plain Dr/Cr grid. Switch back to it before accepting — "
                    + "altering an item or service invoice is not available yet, and the invoice grids are not "
                    + "what this alteration would post.";
            return false;
        }

        // 🔴 AND CTRL+H'S OTHER HALF — the one the gate above does NOT close (finding L1-02, a measured BLOCKER).
        // Single Entry sits INSIDE IsAsVoucherMode by design (it is a re-render of the same lines, which is what
        // keeps Accept routing to the plain path), so one ChangeMode() on an altering Payment/Receipt/Contra walked
        // straight past the check above. Entering the mode runs SyncSingleEntrySides, which stamps line 0 to the
        // account side, EVERY other line to the opposite side, and rewrites line 0's amount to Σ of the rest — on a
        // voucher keyed in the Dr/Cr grid with two bank credits that silently FLIPS every side and REWRITES an
        // amount. The replacement still balances, so Replace accepted it and the alteration reported success while
        // an expense became an income and cash went UP on a payment.
        //
        // 🔴 THIS IS THE SECOND OF TWO HALVES, and on its own it is NOT enough — measured. OnModeChanged's own
        // comment records that "leaving it simply stops re-stamping, so the lines survive the flip intact", so a
        // gate that only fires while IsSingleEntry is true is walked past by pressing Ctrl+H TWICE: the sides are
        // flipped on the way in and stay flipped on the way out. The stamp is therefore blocked at its source (see
        // SyncSingleEntrySides), and this gate exists because accepting from a view that does not describe the
        // voucher is wrong even when it is no longer destructive.
        //
        // The shape is re-derived from the POSTED voucher, not read off screen state, by the same predicate
        // SeedAlterationMode used: on a voucher genuinely keyed in Single Entry the stamp is a no-op by
        // construction, so that shape is still free to accept.
        if (IsSingleEntry && !IsPostedAsSingleEntry(existing))
        {
            Message = "This voucher was keyed in the Dr/Cr grid, not in Single Entry. Accepting it here would "
                    + "re-stamp every line's side and rewrite the first line's amount, so switch back to the "
                    + "Dr/Cr grid before accepting.";
            return false;
        }

        if (VoucherAlterationEligibility.RefusalFor(_company, _alteringVoucherId) is { } refusal)
        {
            Message = refusal;
            return false;
        }

        if (PlainGridRefusal() is { } gridRefusal)
        {
            Message = gridRefusal;
            return false;
        }

        // 🔴 The engine's own legs, re-derived. Invert reads the POSTED voucher again (not screen state) so the
        // pin is a fact about the book rather than about anything the operator may have moved since it opened.
        if (VoucherAlterationDerivedLegs.Invert(_company, existing, out var inverted) is { } inversionRefusal)
        {
            Message = inversionRefusal;
            return false;
        }

        var entryLines = BuildPlainEntryLines(out var sources);
        if (ReDeriveEngineLegs(existing, inverted!, sources, entryLines) is { } deriveRefusal)
        {
            Message = deriveRefusal;
            return false;
        }

        if (entryLines.Count < 2)
        {
            Message = "A voucher needs at least two lines.";
            return false;
        }

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

        // Off a Purchase/Sales the screen never captures a reference, so the POSTED values are carried rather than
        // re-read as null — otherwise an imported Journal's reference would be dropped by the alteration.
        var referenceNo = existing.ReferenceNo;
        var referenceDate = existing.ReferenceDate;
        if (ShowReferenceCapture && !TryResolveReferenceCapture(out referenceNo, out referenceDate)) return false;

        var replacement = new Voucher(
            existing.Id,                 // clause 2 — the Guid is every outside link's only handle
            existing.TypeId,             // the preserved number belongs to THIS type's sequence
            Date,
            entryLines,
            number: existing.Number,     // clause 3 — Replace accepts the voucher's own number by name
            narration: string.IsNullOrWhiteSpace(Narration) ? null : Narration.Trim(),
            partyId: existing.PartyId,   // never keyed on the plain grid; dropping it would move the party analysis
            cancelled: existing.Cancelled,          // Cancel's verb, not Alter's — Replace refuses a change
            optional: IsOptional,                   // 🔴 the provisional-state vector, carried from the header
            postDated: IsPostDated,                 //    properties RehydrateFrom seeded from the posted voucher
            applicableUpto: applicableUpto,         //    (§12.8 — Replace refuses a change to any of the three)
            referenceNo: referenceNo,
            referenceDate: referenceDate,
            isAccountingInvoice: existing.IsAccountingInvoice); // get-only, and Replace refuses a change

        IReadOnlyList<VoucherAlterationWarning> warnings;
        try
        {
            _service.Replace(existing.Id, replacement, out warnings);
        }
        catch (UnbalancedVoucherException)
        {
            Message = $"Voucher is out of balance (Dr {TotalDebitText} ≠ Cr {TotalCreditText}). Not altered.";
            return false;
        }
        catch (Exception ex) when (ex is InvalidVoucherException or InvalidOperationException)
        {
            Message = $"Cannot alter: {ex.Message}";
            return false;
        }

        // 🔴 A FAILED SAVE ROLLS THE SWAP BACK, exactly as the Alt+X arm rolls its flag back. The engine mutates the
        // in-memory aggregate and the save happens after it, so without this the books would hold the amended
        // voucher, the .db the original, and every later save would carry the divergence. Restoring is a rollback of
        // a transaction that did not commit — the second Replace is safe because CarryBankDatesForward only ever
        // WRITES to the replacement it is handed and never to the outgoing voucher, so `existing` still holds the
        // reconcile ticks it was posted with.
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
                Message = $"Could not save the company: {ex.Message} Putting the original voucher back ALSO "
                        + $"failed ({rollbackFailure.Message}), so this company is now ahead of its file — close "
                        + "it without saving.";
            }
            return false;
        }

        SavedNumber = replacement.Number;
        Message = $"{_type.Name} No. {_company.FormatVoucherNumber(replacement)} altered."
                + WarningNote(warnings);
        _onSaved();
        return true;
    }

    /// <summary>
    /// Removes every ledger that appeared on the company since <paramref name="before"/> was taken — the
    /// compensating undo for the impure engines <see cref="ReDeriveEngineLegs"/> runs. A refused alteration has to
    /// leave the book exactly as it found it; a successful one keeps what it created, which is the point of
    /// creating it.
    /// </summary>
    private void UnwindLedgersCreatedSince(HashSet<Guid> before)
    {
        foreach (var created in _company.Ledgers.Where(l => !before.Contains(l.Id)).ToList())
            _company.RemoveLedger(created);
    }

    /// <summary>The operator-facing tail of the alteration message: the warnings <c>Replace</c> raised (a cleared
    /// bank reconciliation, a moved date, a diverged statutory record). Empty when it raised none, so an ordinary
    /// alteration reads exactly as a plain success.</summary>
    private static string WarningNote(IReadOnlyList<VoucherAlterationWarning> warnings) =>
        warnings.Count == 0 ? string.Empty : " " + string.Join(" ", warnings.Select(w => w.Message));

    // =============================================================== Phase 10.11 S5c — the RE-DERIVATION

    /// <summary>
    /// 🔴 <b>Rebuilds the engine-derived legs of an alteration — the whole point of S5c.</b> Nothing is copied
    /// forward: the withholding carve-out is re-computed from the RESTORED GROSS and the reverse-charge pair is
    /// re-stamped from the amended taxable value, both through the same engines that posted them (ER-4).
    ///
    /// <para><b>The detection rule, stated where it is enforced.</b> Detection is consulted <b>only for a family the
    /// POSTED voucher already carries</b> — <paramref name="inverted"/>'s pins are read off the book, never off
    /// today's masters. So a voucher posted with no carve never meets a detector at all (it cannot ACQUIRE one when
    /// a party master gains a <c>DeducteeType</c> after posting), and a voucher posted WITH one re-derives it and is
    /// refused by name if today's masters would produce a different SHAPE (it cannot silently LOSE one). The only
    /// thing an alteration may move is the AMOUNT.</para>
    /// </summary>
    private string? ReDeriveEngineLegs(
        Voucher existing,
        VoucherAlterationDerivedLegs.Inversion inverted,
        List<VoucherLineViewModel> sources,
        List<EntryLine> entryLines)
    {
        if (inverted.Tds is { } tdsPin && ApplyReCarve(existing, tdsPin, sources, entryLines) is { } carveRefusal)
            return carveRefusal;

        if (inverted.Rcm is { } rcmPin && ApplyReStamp(rcmPin, entryLines) is { } stampRefusal)
            return stampRefusal;

        return null;
    }

    /// <summary>
    /// 🔴 <b>Re-carves the withholding FROM THE RESTORED GROSS.</b> The rehydration put the withheld amount back
    /// onto the deductee's leg, so <c>DetectTdsContext</c> reads the gross the operator keyed (or the gross they
    /// have just amended it to) — exactly the figure the original posting carved from. Re-applying the stored carve
    /// to a new base instead would move the party credit by exactly the withholding, which is the single worst
    /// outcome available in this phase (design §3.2).
    ///
    /// <para>🔴 <b>And the cumulative-FY threshold is projected at this voucher's POSTING MOMENT, through
    /// <c>asPostedBefore</c>.</b> At posting the voucher was not in the book yet; at re-accept it is, carrying its
    /// own <c>TdsLineTax</c> — and so is everything posted after it. Handing this voucher's id to
    /// <c>TdsService.BuildCarveOut</c> as <c>asPostedBefore</c> makes <c>ProjectPriorCumulative</c> resolve that
    /// marker to a <b>list index</b> and project over <c>vouchers[0..limit)</c> only, which is exactly the set that
    /// stood in the book when this voucher was posted (<c>Company.Vouchers</c> is in posting order, and
    /// <c>LedgerService.Replace</c> deliberately preserves it). Without it, on §194J (₹50,000 cumulative) a
    /// ₹30,000 payment that was correctly BELOW threshold at posting reads 30,000 prior + 30,000 current =
    /// 60,000 and ACQUIRES a withholding on a narration-only alteration.</para>
    ///
    /// <para>🔴 <b>Do not reintroduce the "exclude this voucher's own id" form — it shipped as a
    /// blocker.</b> That earlier argument dropped only the named voucher and left the projection selecting by DATE,
    /// so a sibling posted LATER but dated on or before this voucher still counted as "prior" although it was not in
    /// the book at posting. Measured on §194J(b): two same-dated ₹30,000.30 journals, then a NARRATION-ONLY
    /// alteration of the first moved the party credit ₹30,000.30 → ₹27,000.30 and created a
    /// ₹3,000.00 TDS Payable leg — a statutory liability raised by editing a narration. The reachable
    /// window was "posted later, dated on or before", i.e. every same-day batch and every back-dated correction.
    /// Cutting the projection at the posting moment closes that window; excluding an id cannot, because what it
    /// leaves behind is still a date test.</para>
    /// </summary>
    private string? ApplyReCarve(
        Voucher existing,
        VoucherAlterationDerivedLegs.TdsPin pin,
        List<VoucherLineViewModel> sources,
        List<EntryLine> entryLines)
    {
        if (DetectTdsContext() is not { } ctx)
            return $"This voucher was posted with a {pin.SectionCode} TDS withholding, and the entry screen no "
                 + "longer finds one on it — the expense ledger's 'Is TDS Applicable' flag, the party's deductee "
                 + "status or the section may have been changed since it was posted, or TDS has been switched off. "
                 + "Accepting would drop the withholding and credit the party the full gross, so it is refused.";

        if (ctx.Deductee.Id != pin.DeducteeLedgerId)
            return $"This voucher withheld {pin.SectionCode} TDS from a different party than the one the grid now "
                 + $"shows as the deductee ('{ctx.Deductee.Name}'). Alter does not move a posted withholding to "
                 + "another party.";

        if (ctx.Nature.Id != pin.NatureId)
            return $"This voucher was posted under section {pin.SectionCode} and the withholding panel now shows "
                 + $"{ctx.Nature.SectionCode}. Alter re-computes the AMOUNT of a posted withholding, never its "
                 + "section — a re-sectioned deduction belongs to a different challan and a different return line.";

        TdsService.CarveOut carve;
        try
        {
            carve = _tds.BuildCarveOut(
                ctx.Gross, AssessableExGst(), ctx.Nature, ctx.Deductee, Date,
                asPostedBefore: existing.Id,
                keyedPartyLine: KeyedPartyTemplate(ctx.PartyLine),
                // 🔴 GRANDFATHERING, AND IT IS THE PIN ITSELF THAT CARRIES IT. A §194C voucher posted before the
                // deductee-type branch existed carries 100 bp although its deductee is a company or a firm; without
                // this argument the re-carve would resolve 200 bp, the rate pin four lines below would disagree, and
                // EVERY already-posted non-Ind/HUF §194C voucher would become unalterable — a rate defect turned into
                // a data-migration problem. The value is the POSTED voucher's own stamped RateBasisPoints, read back
                // by VoucherAlterationDerivedLegs.InvertWithholding, so the rule is a fact about this voucher and
                // never a date comparison. TdsService.GrandfatheredRate absorbs exactly one disagreement (posted on
                // the section's Ind/HUF arm, now resolving its other-than-individual arm) and refuses the rest, so
                // a moved rate master and a PAN added or removed since posting still reach the pin below.
                postedRateBasisPoints: pin.RateBasisPoints,
                // 🔴 THE SECOND GRANDFATHERING, AND IT IS NOT A RATE. §194-I's threshold is a PER-MONTH limb
                // (first proviso: rent "for a month or part of a month" against ₹50,000, and no annual limb at
                // all); the engine used to test an annualised ₹6,00,000 FY aggregate instead. So on §194-I the
                // drift a re-carve can meet is not "the percentage moved" but "the threshold was crossed at all":
                // a ₹60,000 rent bill posted under the old rule withheld NOTHING where the statute takes
                // ₹6,000.00, and twelve ₹40,000 months withheld ₹4,000 in the eleventh where the statute takes
                // nothing. Either way the refusal below would fire on a voucher nobody touched — a narration fix
                // or a cost-centre correction on any §194-I voucher in any existing book would be REFUSED. The
                // posted ASSESSABLE and the posted TDS travel together and pin the posted OUTCOME, so the
                // re-carve reproduces the posted figure and the voucher stays alterable. Facts about this
                // voucher, read off its own stamped TdsLineTax; never a date comparison. The pin releases the
                // moment the operator amends the base, which is the one case where the statutory answer for the
                // AMENDED figure is the right one — see TdsService.GrandfatheredLiability.
                postedAssessableValue: PostedTdsDetail(existing)?.AssessableValue,
                postedTdsAmount: PostedTdsDetail(existing)?.TdsAmount);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            return $"Cannot re-compute the TDS withholding: {ex.Message}";
        }

        if (carve.Withholding.RateBasisPoints != pin.RateBasisPoints)
            return $"This voucher withheld {pin.SectionCode} TDS at {pin.RateBasisPoints / 100m:0.##}% and the same "
                 + $"section now resolves to {carve.Withholding.RateBasisPoints / 100m:0.##}% — the section's rate, "
                 + "or the deductee's PAN (which decides whether the §206AA no-PAN rate applies), has changed since "
                 + "this voucher was posted. Re-computing at the new rate would restate a deduction that has already "
                 + "been reported, so it is refused.";

        // 🔴 NOTHING THE OPERATOR TYPED MOVED, BUT THE ANSWER DID — refused, in BOTH directions.
        //
        // The three refusals above compare the deductee, the section and the rate. None of them compared whether
        // the voucher WITHHELD AT ALL, and none of them compared the assessable base, so the applies/not-applies
        // transition and every input to that base were unguarded. Measured, each on an alteration that changed
        // nothing but the narration (or, in the third, nothing but the date) of a voucher that had withheld
        // 3,000.00 under 194J(b):
        //   * cancel a sibling voucher in the same FY  => party 27,000.30 / TDS Payable 3,000.00 / 3 lines became
        //     party 30,000.30 / no payable leg / 2 lines, AcceptAlteration true, and no warning of any kind;
        //   * delete that sibling                      => identical;
        //   * move the voucher's own DATE into the next FY (which S5a's contract makes warn-and-proceed) =>
        //     identical, and the success message reported the date change while saying nothing about the statutory
        //     liability it had just removed;
        //   * re-classify an ordinary debit ledger under Duties & Taxes, which shrinks AssessableExGst => a filed
        //     12,000.00 deduction restated to 10,000.00 with 2,000 moved back to the party.
        // The contract this breaks is stated in bold on VoucherAlterationDerivedLegs: a voucher posted WITH a
        // derived leg "can never silently LOSE it ... Silence is the one outcome that is not available."
        //
        // The rule is deliberately stated on the OPERATOR'S input rather than on any one master: while the restored
        // gross is unchanged the re-carve MUST reproduce the posted withholding, whatever moved underneath it. An
        // amendment that does move the gross is a legitimate re-carve and is not touched by this.
        if (ctx.Gross == pin.RestoredGross && carve.TdsAmount != pin.PostedTdsAmount)
            return $"This voucher withheld {pin.PostedTdsAmount} of {pin.SectionCode} TDS. Nothing on this grid has "
                 + $"moved the party's gross of {pin.RestoredGross}, and yet the same section now computes "
                 + $"{carve.TdsAmount} — a voucher cancelled, deleted or re-dated since posting has changed the "
                 + "year's aggregate for this deductee, or a master edit has moved the assessable base. "
                 + "Re-computing would restate a deduction that has already been reported, so it is refused. Amend "
                 + "the gross if the supply itself changed.";

        // The deductee's leg becomes the DERIVED net (or the full gross carrying the assessment detail, below
        // threshold) and the TDS-Payable leg is appended — the identical splice PostAndSave makes, over the same
        // ordered source rows, so no index can drift out of step with the builder.
        var index = sources.FindIndex(l => ReferenceEquals(l, ctx.PartyLine));
        if (index < 0)
            return "The deductee's line is no longer complete on this grid, so the withholding cannot be re-carved "
                 + "onto it.";

        entryLines[index] = carve.PartyLine;
        if (carve.TdsPayableLine is { } payableLine) entryLines.Add(payableLine);
        return null;
    }

    /// <summary>
    /// 🔴 <b>Re-stamps the reverse-charge pair — RECOMPUTED, never echoed</b> (design finding L3-07). GSTR-1 and
    /// GSTR-3B read the STAMPED <c>GstLineTax.TaxableValue</c>, not the posted amounts, so a replacement that
    /// carried the posted pair forward would let a filed return declare a figure the book no longer holds. The pair
    /// is therefore rebuilt from the amended expense legs through <see cref="RcmService.BuildReverseCharge"/>, the
    /// same call <c>PostAndSave</c> makes.
    ///
    /// <para><b>The drift guard is a SHAPE comparison, not an amount comparison.</b> The pin holds ledger, side,
    /// head, rate and ITC scheme; amounts and taxable values are excluded because they are exactly what an
    /// alteration moves. So an amended expense re-stamps cleanly, while a notified rate that moved, a supplier
    /// whose registration changed, a place of supply that flipped the intra/inter split, or an operator-only input
    /// this screen cannot recover from the posted voucher (the supply KIND, the promoter and body-corporate
    /// qualifiers — none of which is persisted anywhere) all change the shape and are refused by name.</para>
    /// </summary>
    private string? ApplyReStamp(VoucherAlterationDerivedLegs.RcmPin pin, List<EntryLine> entryLines)
    {
        if (IsRcmDeclined)
            return "This voucher self-accounts reverse charge, and the reverse-charge panel is now set to 'Not "
                 + "Applicable'. Accepting would drop the §49(4) liability and its matching input credit, so it is "
                 + "refused — Alter re-computes a posted reverse charge, it does not withdraw one.";

        if (DetectRcmShape() is not { } shape)
            return "This voucher self-accounts reverse charge, and the entry screen no longer finds a "
                 + "reverse-charge shape on it — the expense ledger's 'reverse charge applicable' flag or the "
                 + "supplier's identity may have changed since it was posted. Accepting would drop the §49(4) "
                 + "liability and its matching input credit, so it is refused.";

        var rebuilt = new List<EntryLine>();
        foreach (var leg in shape.Legs)
        {
            if (!ResolveRcm(shape, leg).Applies) continue;
            try
            {
                rebuilt.AddRange(_rcm.BuildReverseCharge(
                    leg.Taxable, item: null, leg.Expense, shape.Party.PartyGst, Date,
                    SelectedRcmSupplyKind?.Kind ?? RcmService.SupplyKind.Domestic,
                    RcmRecipientIsPromoter, RcmRecipientIsBodyCorporate).Lines);
            }
            catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
            {
                return $"Cannot re-compute reverse charge on '{leg.Expense.Name}': {ex.Message}";
            }
        }

        if (!pin.Matches(rebuilt))
            return "The reverse-charge tax this voucher self-accounted no longer re-computes to the same shape: "
                 + "the heads, the notified rate or the input-credit scheme have moved since it was posted, or it "
                 + "was posted under a supply routing this screen cannot read back from the voucher (the supply "
                 + "kind, and the promoter / body-corporate qualifiers, are keyed at entry and are not stored on "
                 + "the voucher). Restating an already-reported §49(4) liability is refused.";

        entryLines.AddRange(rebuilt);
        return null;
    }

    /// <summary>
    /// The up-front, plain-grid refusals made before the engine is touched — <b>the single copy</b> that both
    /// <see cref="Accept"/> and <see cref="AcceptAlteration"/> call. Returns the message, or <c>null</c> when the
    /// grid is fit to post.
    ///
    /// <para>🔴 <b>It says "the single copy" because it was not one</b> (finding L3-06). This method's previous
    /// summary said the checks had been "factored out so <c>AcceptAlteration</c> makes exactly the same ones";
    /// <c>Accept</c> in fact still carried its own inline re-implementation of all eight, in the same order with
    /// character-identical messages, and never called this at all. Identical-by-coincidence is exactly the state
    /// from which two lists drift silently, so <c>Accept</c> now calls this.</para>
    /// </summary>
    private string? PlainGridRefusal()
    {
        // W0-13 S2a — an amount the INTEGER-paisa store cannot carry is refused HERE, naming the field, before any
        // of the gates below. It has to come first: a sub-paisa line amount also reads as "half-filled" (the
        // storability test is folded into IsComplete), and a sub-paisa ALLOCATION reads as a bad split even though
        // the split foots exactly — so without this, the operator would be told to fix the one thing that is
        // already right. Left unguarded the figure reached Paisa.FromMoney and came back as a raw persistence
        // exception; see UnstorableGridAmountError.
        if (UnstorableGridAmountError() is { } unstorable) return unstorable;

        // Reject half-filled rows up front with a clear message (before touching the engine).
        if (Lines.Any(l => !l.IsBlank && !l.IsComplete))
            return "Every entered line needs a ledger and a positive amount.";

        // WI-5: reject an UNREADABLE typed date up front rather than silently banking a null. A blank
        // instrument / bill due date legitimately means "none"; text that cannot be read does not, and dropping it
        // would post a voucher whose dates disagree with what the operator typed.
        if (Lines.FirstOrDefault(l => l.HasUnreadableInstrumentDate) is { } badLineDate)
            return ApexDate.ErrorFor(badLineDate.InstrumentDateText);

        if (Lines.SelectMany(l => l.BillAllocations).FirstOrDefault(b => b.HasUnreadableDueDate) is { } badDueDate)
            return ApexDate.ErrorFor(badDueDate.DueDateText);

        // Reject an invalid bill-wise split up front (allocations must sum to the line amount).
        if (Lines.FirstOrDefault(l => l.IsComplete && !l.BillSplitOk) is { } badBill)
            return $"Bill-wise allocations for '{badBill.SelectedLedger!.Name}' must sum to the line amount "
                 + $"({IndianFormat.AmountAlways(badBill.ParsedAmount)}).";

        // Reject an Agst-Ref that no longer names an open bill of the party, or that over-settles one, on a
        // PRE-LOADED settlement (register row IV-5 + D5). The bill name is a free TextBox, so the operator can edit
        // the pre-loaded reference into something that is not a bill; nothing else in the app would catch it.
        // Deliberately AFTER the bill-split check, so the commoner "allocations must sum to the line amount"
        // message still wins when both are wrong.
        if (SettlementAllocationError() is { } settlementError) return settlementError;

        // 🔴 THE MESSAGE NAMES THE SHORT AXIS (finding L3-03). The old sentence stated the SUPERSEDED partition
        // rule — "must sum to the line amount (5,000.00)" — and a legacy cross-category voucher, the exact
        // population CostAllocationStrictness.Legacy admits through Load and import, arrives here with allocations
        // that sum to exactly 5,000.00 and still cannot be accepted, because Replace validates with Strict. The
        // operator was told to satisfy a rule the voucher already satisfied. This wording mirrors
        // VoucherValidator's own C-27 text, so the screen and the engine now say the same thing.
        if (Lines.FirstOrDefault(l => l.IsComplete && !l.CostSplitOk) is { } badCost)
            return badCost.ShortCostAxis is { } shortAxis
                ? $"Cost allocations for '{badCost.SelectedLedger!.Name}' total "
                + $"{IndianFormat.AmountAlways(shortAxis.Allocated)} under cost category "
                + $"'{shortAxis.Category.Name}' but the line amount is "
                + $"{IndianFormat.AmountAlways(badCost.ParsedAmount)}; each cost category must be allocated in "
                + "full (categories are parallel axes, not a split of the line)."
                : $"Cost allocations for '{badCost.SelectedLedger!.Name}' must sum to the line amount "
                + $"({IndianFormat.AmountAlways(badCost.ParsedAmount)}).";

        if (Lines.FirstOrDefault(l => l.SelectedLedger is not null && l.IsForexLine && !l.ForexOk) is { } badForex)
            return $"Forex details for '{badForex.SelectedLedger!.Name}' need both an amount in "
                 + $"{badForex.ForexCurrencyCode} and a rate of exchange.";

        return null;
    }

    /// <summary>
    /// The plain-grid <see cref="EntryLine"/> set — the four line writers run over every complete row, and
    /// <b>nothing else</b>. No withholding carve, no reverse-charge pair, no advance pair, and no stamped
    /// <c>Gst</c>/<c>Tds</c>/<c>Tcs</c> argument anywhere: those are the arguments an alteration must never echo,
    /// and the way to guarantee that is for this builder to have no way to supply one.
    /// </summary>
    private List<EntryLine> BuildPlainEntryLines() => BuildPlainEntryLines(out _);

    /// <summary>
    /// The same builder, also handing back the ROW VIEW MODELS it built from, in the same order. S5c's re-carve
    /// needs to splice the deductee's carved leg into the built list, and matching by index against a separately
    /// re-evaluated <c>Where(IsComplete)</c> would be a silent alignment bug waiting for the day the predicate
    /// changes. One enumeration, one order, no matching.
    /// </summary>
    private List<EntryLine> BuildPlainEntryLines(out List<VoucherLineViewModel> sources)
    {
        sources = Lines.Where(l => l.IsComplete).ToList();
        return sources
            .Select(l =>
            {
                var billAllocs = l.ToBillAllocations();
                var costAllocs = l.ToCostAllocations();
                return new EntryLine(
                    l.SelectedLedger!.Id, new Money(l.ParsedAmount), l.Side,
                    billAllocs.Count > 0 ? billAllocs : null,
                    costAllocs.Count > 0 ? costAllocs : null,
                    l.ToBankAllocation(),
                    l.ToForexInfo());
            })
            .ToList();
    }

    /// <summary>
    /// Ctrl+A accept: builds the voucher from the non-blank lines, posts it (engine rejects an
    /// unbalanced/invalid voucher — nothing persists on failure), then saves the company to its
    /// <c>.db</c>. On success surfaces the assigned number and returns to the Gateway.
    ///
    /// <para>🔴 <b>Hard-refuses on an ALTERING screen</b> (design §6.6a.6, fourth thing). This method is
    /// build + <c>Post</c> + REGISTRATION SIDE EFFECTS: it mints a fresh <see cref="Guid"/> and posts a SECOND
    /// voucher — leaving the original standing, so the book would hold the entry twice — and it re-runs
    /// <c>DetectTdsContext</c>, <c>DetectRcmShape</c> and <c>BuildAdvanceLines</c> against TODAY'S masters, so a
    /// narration-only alteration could acquire or lose a withholding carve. <see cref="AcceptAlteration"/> is the
    /// alteration verb.</para>
    /// </summary>
    public bool Accept()
    {
        Message = null;

        if (IsAltering)
        {
            Message = "This screen is altering a posted voucher — accepting it as a new entry would post a second "
                    + "voucher and re-run withholding and reverse-charge detection against today's masters. Use "
                    + "the alteration accept instead.";
            return false;
        }


        // Item-invoice mode routes to its own accept path (auto-derived legs + inventory lines).
        if (IsItemInvoice) return AcceptItemInvoice();

        // Accounting-invoice (service) mode routes to its own accept path (income ledger legs + auto SAC GST; no stock).
        if (IsAccountingInvoice) return AcceptAccountingInvoice();

        // 🔴 ONE COPY, NOT TWO (finding L3-06). This block used to be re-implemented inline here, eight checks
        // deep, character-identical to PlainGridRefusal — which AcceptAlteration calls and whose own doc comment
        // claimed the checks had been "factored out so AcceptAlteration makes exactly the same ones". They had not
        // been; they had been DUPLICATED, and the two lists agreed only by coincidence. That is not academic: the
        // cost message in one of them stated the superseded partition rule, and fixing it in one copy would have
        // left the other quoting the abolished rule at the operator.
        if (PlainGridRefusal() is { } gridRefusal)
        {
            Message = gridRefusal;
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
    /// <summary>
    /// Resolves the counterparty captured field (numbering-design-v2 §8) for the post: the free-text
    /// <see cref="ReferenceNo"/> (blank ⇒ null) and the optional <see cref="ReferenceDateText"/> (blank ⇒ null,
    /// unparseable ⇒ rejected with a message). Captured only on a Purchase/Sales voucher — every other type gets
    /// null/null so the posted voucher is byte-identical to today (ER-13). Returns false (and sets
    /// <see cref="Message"/>) only when a non-blank reference date fails to parse.
    /// </summary>
    private bool TryResolveReferenceCapture(out string? referenceNo, out DateOnly? referenceDate)
    {
        referenceNo = null;
        referenceDate = null;
        if (!ShowReferenceCapture) return true; // never captured off a Purchase/Sales voucher

        referenceNo = string.IsNullOrWhiteSpace(ReferenceNo) ? null : ReferenceNo.Trim();

        if (!string.IsNullOrWhiteSpace(ReferenceDateText))
        {
            if (!ApexDate.TryParse(ReferenceDateText, Date, out var refDate))
            {
                Message = ApexDate.ErrorFor(ReferenceDateText);
                return false;
            }
            referenceDate = refDate;
        }
        return true;
    }

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
                // The keyed party row goes IN so its bill-wise / cost / bank / forex children come back OUT on the
                // derived leg. Without it the splice below dropped every child the operator had keyed, silently: a
                // bill-by-bill creditor's New Ref vanished at posting and Outstandings then reported NO open bill at
                // all for a vendor the company owed 1,08,000.30, on BOTH the withheld and the below-threshold arm.
                carve = _tds.BuildCarveOut(
                    tctx.Gross, AssessableExGst(), tctx.Nature, tctx.Deductee, Date,
                    keyedPartyLine: KeyedPartyTemplate(tctx.PartyLine));
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

        // Counterparty captured field (numbering-design-v2 §8) — "Supplier Invoice No." / "Reference No.".
        if (!TryResolveReferenceCapture(out var referenceNo, out var referenceDate)) return false;

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
            applicableUpto: applicableUpto,
            referenceNo: referenceNo,
            referenceDate: referenceDate);

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

    /// <summary>Esc / the Cancel button: discards the in-progress voucher and returns to the Gateway. (Alt+X
    /// stopped reaching here in Phase 10.11 S3 — it now cancels a POSTED voucher from a report.)</summary>
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

        // The accounting-invoice Particulars picker refreshes with the others. Omitting it left Alt+C DEAD on that
        // field: the ledger was created and the operator returned to a blank ComboBox, because the row's option list
        // was a ctor-built snapshot that predated the new master.
        RebuildAccountingInvoiceLedgers();
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
        // G-1: the party drives the Bill-wise gate (layer 3 — Maintain balances bill-by-bill), so the ACCOUNTING
        // path must recalc through its own routine too. Previously this always ran the item recalc, which in
        // accounting mode returns before the Accept gate and would have stamped the panel from an item total of ₹0.
        if (IsAccountingInvoice) RecalculateAccountingInvoice();
        else RecalculateItemInvoice();
    }
    partial void OnSelectedStockLedgerChanged(DomainLedger? value) => RecalculateItemInvoice();

    /// <summary>
    /// Ctrl+I — toggles item-invoice mode on a Purchase/Sales (a no-op on any other type), redefined over the
    /// 3-value <see cref="Mode"/> as a 2-way As-Voucher↔Item-Invoice flip so its exact current behaviour (and all its
    /// tests) are preserved. Recomputes so the Accept gate reflects the new mode immediately.
    /// </summary>
    public void ToggleItemInvoice()
    {
        if (!CanBeItemInvoice) return;
        Mode = IsItemInvoice ? VoucherEntryMode.AsVoucher : VoucherEntryMode.ItemInvoice;
    }

    /// <summary>
    /// Ctrl+H "Change Mode" — cycles a Purchase/Sales voucher through the entry modes
    /// As Voucher → Item Invoice → Accounting Invoice → As Voucher (a no-op on any other type). Faithful to Tally's
    /// per-voucher mode switch on the same Sales/Purchase voucher type (NOT an F12 flag).
    /// <para>On a <b>Purchase</b> the Accounting arm is skipped entirely (<see cref="CanBeAccountingInvoice"/>), so the
    /// cycle degrades to the 2-way As Voucher ↔ Item Invoice flip: the purchase-side accounting invoice is DEFERRED
    /// scope and silently dropped §194J TDS.</para>
    /// </summary>
    public void ChangeMode()
    {
        // G-6: Ctrl+H is TallyPrime's ONE "Change Mode" picker — the same key on every voucher, with a different
        // mode list per type. On the cash/bank family it flips Single ⟷ Double Entry (BOOK pp.29, 32; SG p.76).
        if (CanBeSingleEntry)
        {
            Mode = IsSingleEntry ? VoucherEntryMode.AsVoucher : VoucherEntryMode.SingleEntry;
            return;
        }

        if (!CanBeItemInvoice) return;
        Mode = Mode switch
        {
            VoucherEntryMode.AsVoucher => VoucherEntryMode.ItemInvoice,
            VoucherEntryMode.ItemInvoice when CanBeAccountingInvoice => VoucherEntryMode.AccountingInvoice,
            _ => VoucherEntryMode.AsVoucher,
        };
    }

    /// <summary>The Accounting-Invoice checkbox affordance — flips As-Voucher↔Accounting, the direct-select sibling of
    /// <see cref="ToggleItemInvoice"/>. A no-op wherever the mode is unavailable, which includes every Purchase
    /// (<see cref="CanBeAccountingInvoice"/>).</summary>
    public void ToggleAccountingInvoice()
    {
        if (!CanBeAccountingInvoice) return;
        Mode = IsAccountingInvoice ? VoucherEntryMode.AsVoucher : VoucherEntryMode.AccountingInvoice;
    }

    partial void OnModeChanged(VoucherEntryMode value)
    {
        // Leaving accounting mode must not leave its band behind. ItemsTotalText / the GST texts / PartyTotalText /
        // DerivedSummary are SHARED with the item path, and the plain As-Voucher branch of Recalculate() writes none
        // of them — so without this the service invoice's CGST 450.00 and "Cr Services …" summary survived a Ctrl+H
        // into a mode that has no such figures. Cleared BEFORE the Recalculate() below, which then repopulates
        // whatever the new mode owns.
        if (value != VoucherEntryMode.AccountingInvoice) ResetAccountingDisplayState();

        // Switching mode changes which grid gates Accept AND whether GST / additional-cost tracking / the
        // Actual-Billed columns are wired in; notify every derived flag and re-derive. Carries the full old
        // OnIsItemInvoiceChanged notification set PLUS the new accounting-mode flags (dropping any leaves a stale band).
        OnPropertyChanged(nameof(IsItemInvoice));
        OnPropertyChanged(nameof(IsAccountingInvoice));
        OnPropertyChanged(nameof(IsAsVoucherMode));
        OnPropertyChanged(nameof(ShowInvoiceOverlay));
        OnPropertyChanged(nameof(IsGstInvoice));
        OnPropertyChanged(nameof(IsAccountingGstInvoice));
        OnPropertyChanged(nameof(ShowGstTotals));
        OnPropertyChanged(nameof(ShowParticularsGrid));
        OnPropertyChanged(nameof(IsTcsSalesInvoice));
        OnPropertyChanged(nameof(ShowAdditionalCosts));
        OnPropertyChanged(nameof(ShowActualBilledColumns));
        OnPropertyChanged(nameof(QuantityHeader));
        OnPropertyChanged(nameof(ShowPriceLevelSelector));
        OnPropertyChanged(nameof(LineTotalCaption));
        // G-6: the Single-Entry render gates + its projections. Entering the mode stamps the documented polarity on
        // the existing lines; leaving it simply stops re-stamping, so the lines (and their now-visible Dr/Cr labels)
        // survive the flip intact — Ctrl+H is a view switch, never data loss.
        OnPropertyChanged(nameof(IsSingleEntry));
        OnPropertyChanged(nameof(ShowPlainDrCrGrid));
        OnPropertyChanged(nameof(SingleEntryAccount));
        OnPropertyChanged(nameof(SingleEntryParticulars));
        OnPropertyChanged(nameof(SingleEntryAccountTotal));
        SyncSingleEntrySides();
        SyncActualBilledOnLines();
        // Recalculate() dispatches to the correct per-mode recalc (item / accounting / plain) and refreshes the
        // advisory panels — so the Accept gate is correct for the mode just entered.
        Recalculate();
    }

    /// <summary>Clears the display fields the accounting-invoice recalc owns, so none of them can outlive the mode
    /// (see <see cref="OnModeChanged"/>). Deliberately does NOT touch <c>CanAccept</c> — the Recalculate() that
    /// immediately follows re-derives the gate for the mode being entered.</summary>
    private void ResetAccountingDisplayState()
    {
        ItemsTotalText = "0.00";
        GstCgstText = "0.00";
        GstSgstText = "0.00";
        GstIgstText = "0.00";
        GstCessText = "0.00";
        PartyTotalText = "0.00";
        DerivedSummary = string.Empty;
    }

    /// <summary>Whether a ledger is a valid Particulars-line target on this nature — a service-income (Sales) /
    /// expense (Purchase) ledger by primary-ancestor nature, never a GST tax ledger, and never a ledger that declares
    /// a <b>GOODS</b> supply. Deliberately broad otherwise, so any user-defined service ledger (Sales Accounts /
    /// Direct or Indirect Income) is offered; a taxable ledger with no resolvable SAC/rate still fails fast at Accept
    /// (never a silent ₹0).
    ///
    /// <para><b>The goods exclusion is a Rule-46 validity guard, not a nicety.</b> An Accounting Invoice prints its
    /// lines with a BLANK Quantity and a BLANK Rate — a service has neither. Rule 46(f) requires the quantity <i>and</i>
    /// unit for a supply of GOODS, so billing a goods ledger here produces a tax invoice that is invalid on its face
    /// (measured before this guard: a Goods-supply ledger was offered, Accept succeeded, and the document printed
    /// <c>hsn/sac=847130 qty="" rate=""</c>). Goods belong on an item invoice, which carries real quantities.</para>
    ///
    /// <para>The test is "declares Goods", not "declares Services": a ledger with <b>no</b> <c>SalesPurchaseGst</c>
    /// block at all declares no supply type — that is every ledger in a GST-off company, and every ledger just created
    /// on the fly with Alt+C — and excluding those would empty the picker and break the feature. Only an explicit
    /// <see cref="GstSupplyType.Goods"/> declaration is refused.</para></summary>
    private bool IsAccountingLineLedger(DomainLedger ledger)
    {
        if (ledger.GstClassification is not null) return false; // never a GST tax (Duties &amp; Taxes) ledger
        if (ledger.SalesPurchaseGst is { SupplyType: GstSupplyType.Goods }) return false; // goods ⇒ item invoice
        var group = _company.FindGroup(ledger.GroupId);
        if (group is null) return false;
        var nature = ClassificationRules.PrimaryNatureOf(group, _company);
        return IsPurchaseInvoice ? nature == GroupNature.Expense : nature == GroupNature.Income;
    }

    /// <summary>
    /// Rebuilds the Particulars ledger picker IN PLACE (the rows bind to the live collection instance, so a
    /// re-assignment would not reach them). Called from the ctor and from <see cref="RefreshMasterPickers"/>, which is
    /// what makes Alt+C create-on-the-fly work on the Particulars ledger field.
    /// <para>Each row's already-picked ledger is captured and restored across the rebuild: a bound <c>ComboBox</c>
    /// nulls its <c>SelectedItem</c> when its <c>ItemsSource</c> is cleared, and that write would flow back through the
    /// TwoWay binding and silently blank a half-typed invoice. Restoring the SAME instance raises no change
    /// notification, so this costs nothing on the common path.</para>
    /// </summary>
    private void RebuildAccountingInvoiceLedgers()
    {
        var picked = AccountingInvoiceLines.Select(l => (Line: l, Id: l.SelectedLedger?.Id)).ToList();

        AccountingInvoiceLedgers.Clear();
        foreach (var l in _company.Ledgers
                     .Where(IsAccountingLineLedger)
                     .OrderBy(l => l.Name, StringComparer.OrdinalIgnoreCase))
            AccountingInvoiceLedgers.Add(l);

        foreach (var (line, id) in picked)
            if (id is { } ledgerId)
                line.SelectedLedger = AccountingInvoiceLedgers.FirstOrDefault(l => l.Id == ledgerId) ?? line.SelectedLedger;
    }

    /// <summary>Adds a Particulars line (service ledger + amount); mirrors <see cref="AddAdditionalCostRow"/> and keeps
    /// a single trailing blank row via <see cref="OnAccountingInvoiceLineChanged"/>.</summary>
    public AccountingInvoiceLineViewModel AddAccountingInvoiceLine()
    {
        var row = new AccountingInvoiceLineViewModel(AccountingInvoiceLedgers, OnAccountingInvoiceLineChanged);
        AccountingInvoiceLines.Add(row);
        return row;
    }

    /// <summary>Removes a Particulars line (keeping at least one); recomputes the invoice.</summary>
    public void RemoveAccountingInvoiceLine(AccountingInvoiceLineViewModel line)
    {
        if (AccountingInvoiceLines.Count <= 1) return;
        AccountingInvoiceLines.Remove(line);
        RecalculateAccountingInvoice();
    }

    private void OnAccountingInvoiceLineChanged()
    {
        // Keep exactly one trailing blank row so there is always a fresh line to type into (mirrors additional costs).
        if (AccountingInvoiceLines.Count == 0 || !AccountingInvoiceLines[^1].IsBlank)
            AddAccountingInvoiceLine();
        RecalculateAccountingInvoice();
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
                // discount/column, so a non-price-level line is byte-identical (DP-A; ER-13). LineValue is the
                // ONE definition of a line's figure (ER-4) — rate × BILLED qty, whether or not the line is split
                // across batches, because a split re-attributes the quantity and never revalues the line.
                if (l.IsComplete && l.EffectiveRate is not null)
                    sum += l.LineValue.Amount;
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
            // ER-4: the SAME LineValue the totals and the posting use, so a batch-split line's tax base is the
            // Σ of its posted batch rows rather than a separately-rounded figure.
            var lineValue = l.LineValue;

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

    // =============================================================== batch allocation → posted lines (G-5)

    /// <summary>
    /// Expands ONE batch-split item line into one <see cref="VoucherInventoryLine"/> per batch (BOOK pp.130–132
    /// <b>[verified-A1]</b>), so the stock genuinely moves lot by lot instead of hiding behind a "Multi (N)"
    /// label. Returns false — with a friendly <see cref="Message"/> and nothing appended — when the split cannot
    /// be posted safely:
    /// <list type="bullet">
    ///   <item>Σ batch qty ≠ the line's Actual qty (a stale split: the operator changed the quantity after
    ///     allocating). Refusing beats posting a quantity that is not the one on screen.</item>
    ///   <item>the Actual/Billed split is in play AND Billed ≠ Actual. TallyPrime carries Actual <i>and</i>
    ///     Billed inside the batch grid; ours captures one quantity per batch, so there is no defensible way to
    ///     decide WHICH lot was short-billed. Blocked explicitly rather than guessed at.</item>
    ///   <item>the per-batch rows cannot foot to the line's own value
    ///     (<see cref="InventoryVoucherLineViewModel.LineValue"/>) because each row snaps to the paisa
    ///     separately. A split re-attributes the quantity; it must never change what the line is worth.</item>
    /// </list>
    /// </summary>
    private bool TryAppendSplitBatchLines(
        InventoryVoucherLineViewModel line, Money rate, StockDirection direction,
        List<VoucherInventoryLine> into)
    {
        var itemName = line.SelectedItem!.Name;
        var allocated = line.BatchAllocations.Sum(a => a.Quantity);
        if (allocated != line.ParsedActualQuantity)
        {
            Message = $"Item '{itemName}': the batch allocation totals {allocated} but the line quantity is " +
                      $"{line.ParsedActualQuantity}. Re-open the batch allocation (Alt+B) and re-balance it.";
            return false;
        }

        if (line.ParsedBilledQuantity != line.ParsedActualQuantity)
        {
            Message = $"Item '{itemName}': a line split across several batches cannot also carry a Billed " +
                      "quantity different from the Actual one — allocate it on separate lines instead.";
            return false;
        }

        // A split RE-ATTRIBUTES the quantity across lots; it must never REVALUE the line (ER-4). Each posted row
        // is valued independently — VoucherInventoryLine.Value = ForexBase(Rate, BilledQuantity) — so N rows snap
        // to the paisa N times where the unsplit line snaps once, and Σ-of-rounded ≠ rounded-of-Σ as soon as a
        // batch quantity is fractional (1.5 × ₹19.75 = ₹29.625 twice ⇒ ₹59.26 against the line's ₹59.25).
        //
        // There is no way to absorb that residual inside the posted shape: Value is DERIVED from Rate ×
        // BilledQuantity, the rate is shared and must stay paisa-exact, and nudging a row's billed quantity would
        // move StockValuationUnitRate — i.e. it would change what the units COST, which batch selection must
        // never do. So the drift is refused here instead: posting it would either bill the customer a paisa they
        // do not owe (the stock leg is Σ of the posted rows, which is what the pairing invariant enforces) or
        // leave the screen's total, the GST base and the ledger disagreeing. The operator can re-cut the batch
        // quantities, or enter the lots on separate lines — where two lines genuinely are two line values.
        var lineValue = Money.ForexBase(rate, line.ParsedBilledQuantity);
        var splitValue = Money.Zero;
        foreach (var a in line.BatchAllocations) splitValue += Money.ForexBase(rate, a.Quantity);
        if (splitValue != lineValue)
        {
            Message = $"Item '{itemName}': splitting this line across {line.BatchAllocations.Count} batches " +
                      $"would value it at ₹{splitValue.Amount:0.00} instead of ₹{lineValue.Amount:0.00} — a " +
                      "batch split may re-attribute the quantity but must never change what the line is worth. " +
                      "Re-cut the batch quantities, or enter the lots on separate lines.";
            return false;
        }

        foreach (var a in line.BatchAllocations)
            into.Add(new VoucherInventoryLine(
                line.SelectedItem!.Id, line.SelectedGodown!.Id, a.Quantity, rate,
                direction: direction,
                batchLabel: a.BatchNumber,
                // Billed ≡ Actual here, and the foot-to-LineValue guard above has already PROVED that Σ of these
                // rows is precisely LineValue — the figure the screen showed and GST taxed (ER-4).
                billedQuantity: a.Quantity,
                unitId: line.UnitId));

        return true;
    }

    /// <summary>
    /// Creates the <see cref="BatchMaster"/> for every batch the operator raised inline on the sub-screen
    /// ("New Number" + Mfg Dt. + Expiry Date; BOOK p.131 <b>[verified-A1]</b>) that does not exist yet. A batch
    /// number is unique WITHIN an item (RQ-1), so an inline number that already exists is simply reused — the
    /// voucher stamps the existing lot rather than failing. Returns false with a friendly message if the master
    /// cannot be created, so the voucher is never posted against a batch that was rejected.
    /// </summary>
    private bool TryCreateInlineBatchMasters(IReadOnlyList<InventoryVoucherLineViewModel> lines)
    {
        var service = new BatchService(_company);
        var created = false;

        foreach (var line in lines)
        {
            if (line.SelectedItem is not { MaintainInBatches: true } item) continue;
            foreach (var a in line.BatchAllocations)
            {
                if (!a.IsNewBatch) continue;
                if (_company.FindBatchByNumber(item.Id, a.BatchNumber) is not null) continue;
                try
                {
                    service.CreateBatch(item.Id, a.BatchNumber,
                        manufacturingDate: a.ManufacturingDate,
                        expiryDate: a.ExpiryDate,
                        godownId: line.SelectedGodown?.Id);
                    created = true;
                }
                catch (InvalidOperationException ex)
                {
                    Message = ex.Message;
                    return false;
                }
            }
        }

        if (created) _storage.Save(_company);
        return true;
    }

    /// <summary>An empty (no-tax) <see cref="GstService.InvoiceTax"/> used when a line is unresolved.</summary>
    private static GstService.InvoiceTax EmptyInvoiceTax() => new()
    {
        TaxLines = Array.Empty<EntryLine>(),
        LineBreakdown = Array.Empty<GstService.LineTax>(),
    };

    // =============================================================== GST on the ACCOUNTING (service) invoice

    /// <summary>The outcome of computing GST over the current complete Particulars (service-income) lines — the
    /// sibling of <see cref="ItemInvoiceGst"/> for the accounting-invoice path. Carries the unresolved <b>ledger</b>
    /// (a taxable Particulars ledger with no resolvable SAC/rate) so the caller fails fast with a friendly message —
    /// never a silent ₹0.</summary>
    private readonly record struct AccountingInvoiceGst(
        GstService.InvoiceTax Tax, bool InterState, DomainLedger? UnresolvedLedger)
    {
        public bool HasUnresolved => UnresolvedLedger is not null;
    }

    /// <summary>
    /// Resolves each complete Particulars line's GST rate + taxability from the <b>ledger's SAC</b>
    /// (<see cref="GstService.ResolveRate"/> called with <c>item: null</c> ⇒ the ledger <c>SalesPurchaseGst</c> path),
    /// routes intra CGST/SGST vs inter IGST from the party's recorded State vs the company home State, and computes the
    /// additive per-(head,rate) tax via the SAME <see cref="GstService.ComputeInvoiceTax"/> the item path uses — so it
    /// inherits paisa-exact compute-then-split parity, per-rate grouping, Composition suppression and the ring-fenced
    /// Cess treatment for free. The line amount IS the taxable value (no qty×rate). Exempt/Nil/Non-GST ledgers contribute
    /// no taxable value (zero tax). A taxable ledger with no resolvable rate is flagged in
    /// <see cref="AccountingInvoiceGst.UnresolvedLedger"/> so the caller fails fast. Returns <c>null</c> when GST is not
    /// wired in (<see cref="IsAccountingGstInvoice"/> false).
    /// <para>
    /// <b>Reverse-charge lines are excluded</b> (see <see cref="FiringReverseChargeLedgerIds"/>). On a reverse-charge
    /// inward supply the SUPPLIER charges no tax — that is the entire mechanism — and
    /// <see cref="AcceptAccountingInvoice"/> already appends the self-accounting dual pair for it. Resolving an
    /// ordinary forward-charge rate here as well credited the supplier the tax it never charged AND debited Input tax
    /// twice against a single liability, on a voucher that still balanced. That combination first became reachable
    /// when <see cref="CanBeAccountingInvoice"/> widened to Purchase.
    /// </para>
    /// </summary>
    private AccountingInvoiceGst? ComputeAccountingInvoiceGst()
    {
        if (!IsAccountingGstInvoice) return null;

        var partyState = SelectedParty?.Ledger?.PartyGst?.StateCode;
        var interState = _gst.IsInterState(partyState);
        var reverseCharge = FiringReverseChargeLedgerIds();

        var taxable = new List<GstService.TaxableLine>();
        foreach (var l in AccountingInvoiceLines.Where(l => l.IsComplete))
        {
            if (l.ParsedAmount is not { } amt || amt <= 0m) continue;

            // Reverse charge ⇒ no forward-charge leg at all: the tax movement is the dual pair Accept appends, and the
            // party is owed the BARE taxable value. Per LINE, never per voucher — one invoice routinely carries a
            // notified head alongside ordinary forward-charge services, and those keep their Input tax.
            if (l.SelectedLedger is { } led && reverseCharge.Contains(led.Id)) continue;

            var value = new Money(amt); // the line amount IS the taxable value (a service carries no qty×rate)

            // Resolve the rate AS OF the voucher Date from the LEDGER's SAC (item: null ⇒ ResolveBase step-2). A rate
            // history override only fires when the ledger's HSN/SAC matches a dated row — else byte-identical.
            var res = _gst.ResolveRate(item: null, l.SelectedLedger, Date);
            if (GstService.IsUnresolved(res))
                return new AccountingInvoiceGst(EmptyInvoiceTax(), interState, l.SelectedLedger);
            if (!res.IsTaxable) continue; // Exempt/Nil/Non-GST service ⇒ no tax

            // Compensation Cess for a service is ad-valorem only (no quantity) — pass quantity 0. null ⇒ no cess.
            var cess = _gst.ResolveCess(item: null, l.SelectedLedger, Date, quantity: 0m);
            taxable.Add(new GstService.TaxableLine(value, res.RateBasisPoints, cess));
        }

        var tax = _gst.ComputeInvoiceTax(taxable, interState, GstDirection);
        return new AccountingInvoiceGst(tax, interState, UnresolvedLedger: null);
    }

    /// <summary>
    /// The Particulars ledgers on which reverse charge <b>actually fires</b> on this voucher — the exact set
    /// <see cref="AcceptAccountingInvoice"/> will build a dual pair for, resolved through the SAME
    /// <see cref="ResolveRcm"/> (ER-4: one resolver, never a second opinion).
    /// <para>
    /// Deliberately narrower than "the ledger carries <c>ReverseChargeApplicable</c>". The master flag only makes the
    /// panel visible; whether the supply IS reverse charge is the engine's call against the notified category, the
    /// supplier/recipient qualifiers and the date — a Sponsorship fee billed to a non-body-corporate, or any supply on
    /// which the operator has ticked "Not Applicable", is an ordinary forward-charge purchase and MUST keep its Input
    /// tax leg. Skipping on the flag alone would under-credit those suppliers by the whole tax.
    /// </para>
    /// Empty (and cheap) on every voucher with no reverse-charge shape, so the ordinary service invoice is
    /// byte-identical (ER-13). Pure — <see cref="ResolveRcm"/> resolves, it never builds, so no RCM ledger is conjured.
    /// </summary>
    private HashSet<Guid> FiringReverseChargeLedgerIds()
    {
        var ids = new HashSet<Guid>();
        if (IsRcmDeclined) return ids;
        if (DetectRcmShape() is not { } shape) return ids;
        foreach (var leg in shape.Legs)
            if (ResolveRcm(shape, leg).Applies) ids.Add(leg.Expense.Id);
        return ids;
    }

    /// <summary>
    /// Recomputes the accounting-invoice indicators: the running Particulars total (shown in the shared
    /// <see cref="LineTotalCaption"/>/"Taxable Value" band), the live CGST/SGST/IGST/Cess + party total, the derived
    /// Dr/Cr summary, and whether Accept is allowed (a party picked, ≥ 1 complete Particulars line, no half-filled
    /// row, positive total, and no unresolved taxable ledger). Mirrors <see cref="RecalculateItemInvoice"/> over the
    /// Particulars lines; never touches <c>InventoryLines</c> or <see cref="ComputeItemInvoiceGst"/>.
    /// <para>A NO-OP outside accounting mode. The display fields it writes are SHARED with the item path, so writing
    /// them from a Particulars-line change while another mode is live cross-contaminated that mode's band; and on a
    /// Purchase (where the mode is deferred) it must not run at all.</para>
    /// </summary>
    public void RecalculateAccountingInvoice()
    {
        if (!IsAccountingInvoice) return;

        // G-7: a Particulars-line change routes STRAIGHT here (OnAccountingInvoiceLineChanged), not through
        // Recalculate(), so without these the TDS/RCM advisory panels never appeared in accounting mode at all —
        // the operator would have had no warning that a §194J withholding was about to be applied. Both self-gate
        // and are re-entrancy-guarded, so this is a no-op wherever they do not apply.
        UpdateTdsPanel();
        UpdateRcmPanel();

        var total = 0m;
        foreach (var l in AccountingInvoiceLines)
            if (l.IsComplete && l.ParsedAmount is { } a) total += a;
        ItemsTotalText = IndianFormat.AmountAlways(total);

        var party = SelectedParty?.Ledger?.Name ?? "party";

        // Mirror the item recalc's fail-fast guard: an unresolvable-cess input (e.g. an RSP-factor cess service with
        // no declared price) must surface a message and clear the gate rather than propagate out of the change handler.
        AccountingInvoiceGst? gst;
        try
        {
            gst = ComputeAccountingInvoiceGst();
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            Message = ex.Message;
            GstCgstText = "0.00";
            GstSgstText = "0.00";
            GstIgstText = "0.00";
            GstCessText = "0.00";
            PartyTotalText = IndianFormat.AmountAlways(total);
            DerivedSummary = BuildAccountingDerivedSummary(party, total, 0m, 0m, 0m, 0m, total);
            CanAccept = false;
            return;
        }

        var cgst = gst?.Tax.TotalCgst.Amount ?? 0m;
        var sgst = gst?.Tax.TotalSgst.Amount ?? 0m;
        var igst = gst?.Tax.TotalIgst.Amount ?? 0m;
        var cess = gst?.Tax.TotalCess.Amount ?? 0m; // ring-fenced out of the tax total, still added to the party total
        var taxTotal = cgst + sgst + igst;
        var partyTotal = total + taxTotal + cess;

        GstCgstText = IndianFormat.AmountAlways(cgst);
        GstSgstText = IndianFormat.AmountAlways(sgst);
        GstIgstText = IndianFormat.AmountAlways(igst);
        GstCessText = IndianFormat.AmountAlways(cess);
        PartyTotalText = IndianFormat.AmountAlways(partyTotal);
        DerivedSummary = BuildAccountingDerivedSummary(party, total, cgst, sgst, igst, cess, partyTotal);

        // G-1: the accounting invoice carries the SAME Bill-wise sub-screen (SG p.80 step 6 / p.82 step 5).
        // The target is the NET the party will actually be owed after any previewed TDS withholding — Accept
        // reconciles the allocations against that same NET, and a panel demanding the GROSS reported a split as
        // "fully allocated" that Accept then refused against a figure the operator was never shown (ER-4).
        SyncInvoiceBillWise(PreviewNetPartyAmount(partyTotal));

        var completeLines = AccountingInvoiceLines.Count(l => l.IsComplete);
        var hasHalfFilled = AccountingInvoiceLines.Any(l => !l.IsBlank && !l.IsComplete);
        var hasUnresolved = gst?.HasUnresolved ?? false; // a taxable ledger with no SAC/rate blocks Accept (no silent ₹0)
        CanAccept =
            SelectedParty?.Ledger is not null
            && completeLines >= 1
            && !hasHalfFilled
            && !hasUnresolved
            && total > 0m
            && InvoiceBillSplitOk;
    }

    /// <summary>
    /// The party's <b>net</b> obligation after the TDS withholding this screen has previewed — i.e. exactly the
    /// <c>carve.NetPartyAmount</c> <see cref="AcceptAccountingInvoice"/> will stamp on the derived party leg, resolved
    /// through the SAME <see cref="TdsService.BuildCarveOut"/> the advisory panel uses (ER-4: one engine, never a
    /// second opinion). Returns <paramref name="partyTotal"/> unchanged whenever no withholding fires — no deductee,
    /// no Is-TDS-Applicable Particulars line, the operator declined, below threshold (a zero carve nets to the gross),
    /// or the carve-out itself refuses — so a non-TDS invoice is byte-identical (ER-13).
    /// <para>Used to target the Bill-wise panel, because what the operator is told to allocate must be what Accept
    /// demands: the bill is opened for the amount actually payable to the party, not the pre-withholding gross.</para>
    /// </summary>
    private decimal PreviewNetPartyAmount(decimal partyTotal)
    {
        if (DetectTdsContext() is not { } ctx) return partyTotal;
        try
        {
            // Same argument as UpdateTdsPanel: one engine, one set of arguments (ER-4). Today's alter path refuses
            // the accounting-invoice family before this is reachable, but the day that family is lifted a preview
            // that projected the voucher against itself would target the bill-wise panel at a net Accept does not
            // post.
            return _tds.BuildCarveOut(
                           ctx.Gross, AssessableExGst(), ctx.Nature, ctx.Deductee, Date, AlterationProjectionMarker,
                           postedRateBasisPoints: AlterationPostedTdsRateBasisPoints,
                           postedAssessableValue: AlterationPostedTds?.AssessableValue,
                           postedTdsAmount: AlterationPostedTds?.TdsAmount)
                       .NetPartyAmount.Amount;
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            return partyTotal; // mirrors UpdateTdsPanel's guard — never crash out of a keystroke handler
        }
    }

    /// <summary>
    /// Builds the accounting-invoice derived Dr/Cr summary. Sales ⇒ "Dr Party (taxable+tax) · Cr Services (taxable)
    /// [· Cr Output CGST/SGST or IGST · Cr Output Cess]"; Purchase ⇒ the mirror (Dr Services / Dr Input tax / Cr Party).
    /// The income legs are collapsed to a single "Services"/"Purchases" caption for the one-line summary — the posted
    /// voucher still carries one leg per Particulars line.
    /// </summary>
    private string BuildAccountingDerivedSummary(string party, decimal taxable, decimal cgst, decimal sgst, decimal igst, decimal cess, decimal partyTotal)
    {
        string A(decimal v) => IndianFormat.AmountAlways(v);
        var caption = IsPurchaseInvoice ? "Purchases" : "Services";
        var side = IsPurchaseInvoice ? "Dr" : "Cr"; // tax follows the income leg's side (Input Dr / Output Cr)
        var head = IsPurchaseInvoice ? "Input" : "Output";

        var extraLegs = new List<string>();
        if (igst != 0m) extraLegs.Add($"{side} {head} IGST {A(igst)}");
        else
        {
            if (cgst != 0m) extraLegs.Add($"{side} {head} CGST {A(cgst)}");
            if (sgst != 0m) extraLegs.Add($"{side} {head} SGST {A(sgst)}");
        }
        if (cess != 0m) extraLegs.Add($"{side} {head} Cess {A(cess)}");
        var taxPart = extraLegs.Count > 0 ? "  ·  " + string.Join("  ·  ", extraLegs) : string.Empty;

        return IsPurchaseInvoice
            ? $"Dr {caption} {A(taxable)}{taxPart}  ·  Cr {party} {A(partyTotal)}"
            : $"Dr {party} {A(partyTotal)}{taxPart}  ·  Cr {caption} {A(taxable)}";
    }

    /// <summary>
    /// Ctrl+A accept for accounting-invoice (service) mode: pre-validates (friendly message, before the engine),
    /// builds one income/expense leg per Particulars line + the auto SAC-based GST tax legs + the party leg (so the
    /// pairing invariant holds by construction), and posts it through <see cref="LedgerService.Post"/> — with
    /// <b>no inventory lines</b>, so <c>HasInventoryLines</c> stays false and the stock/godown/valuation machinery is
    /// never entered. Any domain error is surfaced to <see cref="Message"/> without crashing. Mirrors
    /// <see cref="AcceptItemInvoice"/>.
    /// </summary>
    private bool AcceptAccountingInvoice()
    {
        Message = null;

        // Belt-and-braces on the deferral gate: Accept() only routes here when IsAccountingInvoice (which folds in
        // CanBeAccountingInvoice), so this is unreachable today — it exists so that re-enabling the purchase side can
        // only ever be done deliberately, by flipping CanBeAccountingInvoice after wiring TDS/RCM to the Particulars
        // lines. Without those, a professional-fee purchase posts with NO §194J carve-out.
        if (!CanBeAccountingInvoice)
        {
            Message = "Accounting-invoice mode is available on Sales vouchers only.";
            return false;
        }

        if (SelectedParty?.Ledger is not { } party)
        {
            Message = $"Select the {PartyCaption.ToLowerInvariant()} for this accounting invoice.";
            return false;
        }

        // Reject half-filled (touched-but-incomplete) Particulars rows up front with a clear message.
        if (AccountingInvoiceLines.Any(l => !l.IsBlank && !l.IsComplete))
        {
            Message = "Every particulars line needs a ledger and a paisa-exact amount greater than zero.";
            return false;
        }

        var complete = AccountingInvoiceLines.Where(l => l.IsComplete).ToList();
        if (complete.Count == 0)
        {
            Message = "Enter at least one particulars line before accepting.";
            return false;
        }

        // One income (Sales ⇒ Cr) / expense (Purchase ⇒ Dr) leg per Particulars line — never a single collapsed leg,
        // so a Consultancy-Income row and a Freight-Income row post two separate legs (the correct accounting shape).
        var incomeLines = new List<EntryLine>(complete.Count);
        var taxable = Money.Zero;
        foreach (var l in complete)
        {
            var amt = new Money(l.ParsedAmount!.Value);
            taxable += amt;
            incomeLines.Add(new EntryLine(l.SelectedLedger!.Id, amt, IsPurchaseInvoice ? DrCr.Debit : DrCr.Credit));
        }

        // GST (only when enabled): resolve each line's SAC rate, split intra CGST/SGST vs inter IGST, and build the
        // additive tax lines (posted to the correct Output/Input tax ledgers, carrying GstLineTax so the invoice flows
        // into GSTR-1/3B). A taxable ledger with no resolvable rate fails fast (never a silent ₹0).
        var taxLines = new List<EntryLine>();
        var partyAmount = taxable;
        if (IsAccountingGstInvoice)
        {
            AccountingInvoiceGst gst;
            try
            {
                gst = ComputeAccountingInvoiceGst()!.Value;
            }
            catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
            {
                Message = $"Cannot accept: {ex.Message}";
                return false;
            }
            if (gst.HasUnresolved)
            {
                Message = $"Ledger '{gst.UnresolvedLedger!.Name}' is taxable but no GST rate/SAC is set on the ledger " +
                          "or the company. Set a rate before accepting.";
                return false;
            }
            taxLines.AddRange(gst.Tax.TaxLines);
            // party = taxable + tax + cess. TotalTax excludes the ring-fenced Cess, so add TotalCess explicitly or a
            // cess-bearing service voucher would be out of balance. TotalCess is 0 when off (ER-13).
            partyAmount = new Money(taxable.Amount + gst.Tax.TotalTax.Amount + gst.Tax.TotalCess.Amount);
        }

        // ---------------------------------------------------------------- G-7: TDS withholding on the purchase side
        //
        // THE defect this mode was disabled for. The carve-out is computed from the SAME engine the advisory panel
        // previewed (ER-4), on the party's gross obligation, assessed on the GST-EXCLUSIVE base (Circular 23/2017 —
        // AssessableExGst returns the Particulars total in this mode). Null ⇒ no withholding ⇒ the party is credited
        // in full and the voucher is byte-identical to one posted before this rewire (ER-13).
        TdsService.CarveOut? carve = null;
        if (DetectTdsContext() is { } tctx)
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

        // ---------------------------------------------------------------- G-7: reverse charge on the purchase side
        //
        // One self-balancing dual pair PER notified head (never just the first — a single supplier invoice routinely
        // carries two, and taking only the first under-collects the §49(4) liability on the rest). Resolution is
        // checked first because it is pure; the builder lazily creates the RCM ledgers, so it is never touched on a
        // supply that does not attract reverse charge (ER-13).
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

        // Party leg: Sales ⇒ Dr Party (taxable + tax); Purchase ⇒ Cr Party. Pairing holds by construction
        // (Σ income + Σ tax == party). G-1: the Bill-wise allocation rides on it exactly as on the item invoice
        // (SG p.80 step 6 / p.82 step 5); null when the panel is off ⇒ byte-identical leg (ER-13).
        //
        // When TDS fires the party leg is carved to the NET obligation and carries the withholding detail, so the
        // bill-wise split must foot to that NET figure — it is the amount actually payable to the party, and it is
        // what VoucherValidator reconciles the allocations against.
        var effectivePartyAmount = carve?.NetPartyAmount ?? partyAmount;
        if (!InvoiceBillAllocationsOk(effectivePartyAmount.Amount)) return false;
        var invoiceBills = ToInvoiceBillAllocations();

        var partyLine = IsPurchaseInvoice
            ? new EntryLine(party.Id, effectivePartyAmount, DrCr.Credit,
                            billAllocations: invoiceBills, tds: carve?.Detail)
            : new EntryLine(party.Id, partyAmount, DrCr.Debit, billAllocations: invoiceBills);

        var entryLines = new List<EntryLine>(1 + incomeLines.Count + taxLines.Count) { partyLine };
        entryLines.AddRange(incomeLines);
        entryLines.AddRange(taxLines);

        // The TDS-Payable credit leg — appended only when the threshold was actually crossed. Its absence on a
        // withholding purchase WAS the money defect.
        if (carve is { TdsPayableLine: { } payableLine })
            entryLines.Add(payableLine);

        // Every reverse-charge self-accounting pair (each is Cr RCM Output + Dr Input for the same amount, so the
        // voucher's own balance is untouched).
        foreach (var rcmPosting in rcmPostings)
            entryLines.AddRange(rcmPosting.Lines);

        // Counterparty captured field (numbering-design-v2 §8) — "Reference No." (Sales) / "Supplier Invoice No.".
        if (!TryResolveReferenceCapture(out var referenceNo, out var referenceDate)) return false;

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
            // No inventory lines — HasInventoryLines stays false; no stock is entered.
            referenceNo: referenceNo,
            referenceDate: referenceDate,
            // v49: stamp the ACCOUNTING-INVOICE fact on the voucher. This — not an inference from the posted GST
            // legs — is what makes the print path call it a tax invoice, so a zero-rated (LUT/export) or a
            // wholly-exempt service invoice, both of which post NO tax leg, still print as the Rule-46 tax invoices
            // they are; and a hand-keyed As-Voucher sale is excluded structurally (it never sets this).
            isAccountingInvoice: true);

        try
        {
            var posted = _service.Post(voucher); // enforces pairing/atomicity — never persisted on failure

            // W0-13 S2b — THE SAVE GETS ITS OWN GUARD, and the restore runs FIRST and UNCONDITIONALLY. This is the
            // shape PostAndSave already had; the two invoice Accepts never got it. Post has appended the voucher to
            // the shared Company, Save is transactional, and the narrow filter below matches neither a
            // SqliteException (SQLITE_BUSY / READONLY / FULL) nor an OverflowException — so an ordinary locked-file
            // failure escaped Accept UNHANDLED with the refused invoice still on the aggregate, and every LATER
            // save diverged from the .db. A type filter must never be what decides whether the rollback runs.
            try
            {
                _storage.Save(_company);
            }
            catch (Exception ex)
            {
                _company.RemoveVoucher(posted);
                if (!SaveFailure.IsReportable(ex)) throw;
                Message = $"Could not save the company: {ex.Message} " +
                          "The voucher was not kept — nothing was changed.";
                return false;
            }

            SavedNumber = posted.Number;
            Message = $"{_type.Name} No. {_company.FormatVoucherNumber(posted)} accepted.";
            _onSaved();
            return true;
        }
        catch (UnbalancedVoucherException)
        {
            Message = "The accounting invoice is out of balance. Not saved.";
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
            // ER-4: the same LineValue the totals / GST / posting use (see ComputeItemInvoiceGst).
            var lineValue = l.LineValue;
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

        // G-5: keep each line's "⧉ Allocate batches" affordance in sync with the full four-layer gate, so it is
        // shown only where it actually does something (the RQ-52 UI-leak discipline the stock screens already use).
        foreach (var l in InventoryLines)
            l.WantsBatchAllocation = LineWantsBatchAllocation(l);

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

        // G-1: keep the Bill-wise panel footing against the SAME party total the derived party leg will carry.
        SyncInvoiceBillWise(partyTotal);

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
            && (total > 0m || allowZero)
            // G-1: a bill-wise party's split must foot to the party total (spec C-28).
            && InvoiceBillSplitOk;
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

        // G-5: raise the batch masters the operator created inline on the sub-screen ("New Number", BOOK p.131)
        // BEFORE the lines are built, so the Mfg Dt. / Expiry Date typed beside the number are actually recorded
        // and the batch is a first-class master rather than a bare label. Done here — not when the sub-screen is
        // accepted — so abandoning the voucher leaves no orphan masters behind.
        if (!TryCreateInlineBatchMasters(complete)) return false;

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
            var direction = IsPurchaseInvoice ? StockDirection.Inward : StockDirection.Outward;
            var postedRate = l.EffectiveRate ?? new Money(rate);

            // G-5 — a line ALLOCATED ACROSS SEVERAL BATCHES posts as one item line PER BATCH, each carrying its
            // own batch number and quantity, so the stock genuinely moves in and out of the right lots (BOOK
            // pp.130–132). The sub-screen has already proved Σ batch qty = the line qty (C-29); this re-checks it
            // at the boundary rather than trusting a stale split, and refuses instead of silently posting a
            // different quantity from the one on screen.
            if (l.HasBatchSplit)
            {
                if (!TryAppendSplitBatchLines(l, postedRate, direction, inventoryLines)) return false;
                continue;
            }

            // Actual (ParsedActualQuantity) moves stock; Billed (ParsedBilledQuantity) drives value + GST (RQ-23).
            // When the A/B column is off, Billed ≡ Actual so the line is byte-identical to today (ER-13). The
            // posted rate is the NET (after Price-Level discount) rate (DP-A); equals raw when no discount (ER-13).
            inventoryLines.Add(new VoucherInventoryLine(
                l.SelectedItem!.Id, l.SelectedGodown!.Id, l.ParsedActualQuantity, postedRate,
                // Direction is stamped from the voucher nature by the posting service; a placeholder is fine.
                direction: direction,
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
        //
        // G-1: the Bill-wise allocation is stamped ON the derived party leg — this is the whole fix. Validated
        // against the party total computed RIGHT HERE (not a stale display figure), so a GST/TCS change that moved
        // the total since the last recalc cannot post a mis-footed allocation. Null when the panel is off, so a
        // non-bill-wise party posts a byte-identical leg (ER-13).
        if (!InvoiceBillAllocationsOk(partyAmount.Amount)) return false;
        var invoiceBills = ToInvoiceBillAllocations();

        var partyLine = IsPurchaseInvoice
            ? new EntryLine(party.Id, partyAmount, DrCr.Credit, billAllocations: invoiceBills)
            : new EntryLine(party.Id, partyAmount, DrCr.Debit, billAllocations: invoiceBills, tcs: belowThresholdDetail);
        var stockLine = IsPurchaseInvoice
            ? new EntryLine(valueLedger.Id, taxable, DrCr.Debit)
            : new EntryLine(valueLedger.Id, taxable, DrCr.Credit);

        var entryLines = new List<EntryLine>(2 + additionalCostLines.Count + taxLines.Count + tcsPayableLines.Count)
            { stockLine, partyLine };
        entryLines.AddRange(additionalCostLines);
        entryLines.AddRange(taxLines);
        entryLines.AddRange(tcsPayableLines);

        // Counterparty captured field (numbering-design-v2 §8) — "Supplier Invoice No." / "Reference No.".
        if (!TryResolveReferenceCapture(out var referenceNo, out var referenceDate)) return false;

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
            inventoryLines: inventoryLines,
            referenceNo: referenceNo,
            referenceDate: referenceDate);

        try
        {
            var posted = _service.Post(voucher); // enforces pairing + atomic stock + no-negative — never persisted on failure

            // W0-13 S2b — the same save guard as AcceptAccountingInvoice and PostAndSave: restore FIRST and
            // UNCONDITIONALLY, and only then let SaveFailure.IsReportable decide message-vs-rethrow. See the note
            // there; on this path Post has also applied the stock movement, which RemoveVoucher reverses with it.
            try
            {
                _storage.Save(_company);
            }
            catch (Exception ex)
            {
                _company.RemoveVoucher(posted);
                if (!SaveFailure.IsReportable(ex)) throw;
                Message = $"Could not save the company: {ex.Message} " +
                          "The voucher was not kept — nothing was changed.";
                return false;
            }

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
