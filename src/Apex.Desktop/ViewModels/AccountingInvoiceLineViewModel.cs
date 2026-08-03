using System;
using System.Collections.Generic;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using DomainLedger = Apex.Ledger.Domain.Ledger;

namespace Apex.Desktop.ViewModels;

/// <summary>
/// The per-voucher <b>entry mode</b> of a <see cref="VoucherEntryViewModel"/> (catalog §10; the Tally
/// "Change Mode", Ctrl+H). Three renderings of the SAME Sales/Purchase document, all posting ordinary balanced
/// <c>Voucher</c> legs — the mode is a transient screen state, never a persisted attribute (it is inferred at
/// print/GSTR-1 from the posted legs, exactly as Item Invoice always has been):
/// <list type="bullet">
/// <item><see cref="AsVoucher"/> — the classic Dr/Cr particulars grid (the default; every voucher type).</item>
/// <item><see cref="ItemInvoice"/> — stock <b>item</b> lines with auto-derived accounting legs (Purchase/Sales).</item>
/// <item><see cref="AccountingInvoice"/> — service/accounting <b>ledger</b> lines under Particulars with auto
/// SAC-based GST and NO stock effect (Purchase/Sales) — a service is billed as a service-income ledger line.</item>
/// </list>
/// </summary>
public enum VoucherEntryMode
{
    /// <summary>Classic Dr/Cr accounting voucher (the default).</summary>
    AsVoucher,

    /// <summary>Item invoice — stock-item lines; the accounting legs are auto-derived (Ctrl+I).</summary>
    ItemInvoice,

    /// <summary>Accounting invoice — service/income ledger lines under Particulars, auto SAC-based GST, no stock.</summary>
    AccountingInvoice,

    /// <summary>
    /// Single Entry (G-6) — the Contra/Payment/Receipt layout the corpus teaches first: one <c>Account</c> field
    /// (the cash/bank side) plus a <c>Particulars</c> list (the many side), with <b>no Dr/Cr labels</b>
    /// (BOOK pp.26, 29, 31–32; SG pp.75–76).
    /// <para><b>It is a DISPLAY mode only.</b> It re-renders the same <c>Lines</c> collection the classic Dr/Cr grid
    /// uses, so it introduces no posting path, no validator change and no schema change — the balance rule,
    /// bill-wise, cost, bank and forex sub-panels all continue to apply untouched.</para>
    /// </summary>
    SingleEntry,
}

/// <summary>
/// One <b>Particulars</b> line on an Accounting Invoice (service mode): a picked <b>service-income / expense
/// ledger</b> (whose <c>SalesPurchaseGst</c> block carries the SAC + rate) plus the line <b>amount</b>. Each line
/// posts its OWN income/expense leg (a Consultancy-Income row and a Freight-Income row post two separate legs — the
/// correct accounting-invoice shape); the party is the single header field, and the auto GST (CGST/SGST intra vs
/// IGST inter) is resolved from each line's ledger SAC by the parent VM. There is deliberately no stock item, godown,
/// quantity or rate — a service carries none.
///
/// <para>Parsing/validation is deferred to the parent VM; this class holds the editable state and raises change
/// notifications so the parent's live GST recompute + Accept gate refresh as the user types. MVVM boundary:
/// references only the domain, no Avalonia/UI types, so it is headlessly unit-testable — mirrors
/// <see cref="AdditionalCostRowViewModel"/> exactly.</para>
/// </summary>
public sealed partial class AccountingInvoiceLineViewModel : ViewModelBase
{
    private readonly Action _onChanged;

    /// <summary>The ledgers this line's picker chooses from — service-income ledgers for Sales / expense ledgers
    /// for Purchase (Income/Expense-nature, never a GST tax ledger), built by the parent VM.</summary>
    public IReadOnlyList<DomainLedger> Ledgers { get; }

    [ObservableProperty] private DomainLedger? _selectedLedger;
    [ObservableProperty] private string _amountText = string.Empty;

    public AccountingInvoiceLineViewModel(IReadOnlyList<DomainLedger> ledgers, Action onChanged)
    {
        Ledgers = ledgers ?? throw new ArgumentNullException(nameof(ledgers));
        _onChanged = onChanged ?? throw new ArgumentNullException(nameof(onChanged));
    }

    partial void OnSelectedLedgerChanged(DomainLedger? value) => _onChanged();
    partial void OnAmountTextChanged(string value) => _onChanged();

    /// <summary>The parsed amount (null when blank/unparsable).</summary>
    public decimal? ParsedAmount =>
        string.IsNullOrWhiteSpace(AmountText)
            ? null
            : decimal.TryParse(AmountText.Trim(),
                NumberStyles.AllowDecimalPoint | NumberStyles.AllowThousands | NumberStyles.AllowLeadingSign,
                CultureInfo.InvariantCulture, out var a) ? a : null;

    /// <summary>True once the row has been touched at all — a wholly blank trailing row is ignored by the parent.</summary>
    public bool IsBlank => SelectedLedger is null && string.IsNullOrWhiteSpace(AmountText);

    /// <summary>True when the row is a valid, complete particulars line: a ledger picked and a paisa-exact
    /// amount &gt; 0. The parent posts only complete lines.</summary>
    public bool IsComplete =>
        SelectedLedger is not null
        && ParsedAmount is { } a && a > 0m
        && new Apex.Ledger.Money(a).IsPaisaExact;
}
