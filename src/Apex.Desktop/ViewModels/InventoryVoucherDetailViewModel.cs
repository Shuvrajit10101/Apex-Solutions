using System;
using System.Collections.ObjectModel;
using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Desktop.Services;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Apex.Desktop.ViewModels;

/// <summary>
/// The RQ-7 drill target for a <b>pure-stock</b> voucher — census rows 4.9–4.16. A read-only view of one
/// <see cref="InventoryVoucher"/>, opened when a Day Book row standing for a Stock Journal, Physical Stock,
/// Delivery/Receipt Note, Sales/Purchase Order, Rejection In/Out, Job Work order or Material movement is
/// drilled into (Enter). A terminal (non-drillable) leaf column in the cascade — read-only, so it never mutates
/// the books; UI-toolkit-free so it is unit-testable.
///
/// <para>🔴 <b>WHY THIS IS A SIBLING OF <see cref="VoucherDetailViewModel"/> AND NOT A MODE ON IT.</b> The
/// wave brief's standing warning is "reuse the existing voucher machinery — do not build a parallel set", and
/// this slice obeys it everywhere the machinery is shared: the edit log, the Cancel/Delete verbs, the ONE Y/N
/// confirmation channel, the notice bar and the deletion-target arm are all the existing ones. The read-only
/// PANE is the one place reuse is impossible rather than merely inconvenient, and the reason is structural:
/// <see cref="VoucherDetailViewModel"/> is typed to <see cref="Voucher"/> throughout and its whole surface —
/// <c>IsTaxInvoice</c>, <c>IsBillOfSupply</c>, <c>DocumentLabel</c>, <c>BillOfSupplyDeclaration</c>, the print
/// projection and the e-mail attachment — is GST document classification over balanced Dr/Cr entry lines. A
/// pure-stock voucher has no entry lines, no debit, no credit and no tax character at all (DP-5: it posts no
/// accounting entry), so every one of those members would have to acquire a null branch that is dead for the
/// accounting case and meaningless for this one. That is not reuse; it is two shapes wearing one type.</para>
///
/// <para><b>What it deliberately does NOT carry, so the absence is not read as an oversight:</b> no print
/// route and no e-mail route. Printing a stock voucher is its own census question with its own vendor
/// templates, and a Ctrl+P here that silently produced an invoice-shaped document would be a wrong document
/// rather than a missing one.</para>
/// </summary>
public sealed partial class InventoryVoucherDetailViewModel : ViewModelBase
{
    private readonly Company _company;

    /// <summary>
    /// The voucher this pane projects. 🔴 <b>Not <c>readonly</c>, for the same reason
    /// <see cref="VoucherDetailViewModel"/>'s field is not</b>: if a future alteration route replaces the
    /// object in <c>Company.InventoryVouchers</c> rather than mutating it, this field would hold a discarded
    /// instance and the pane would silently show pre-alteration content. <see cref="Refresh"/> re-points it and
    /// every read below goes through it, so re-pointing once is enough.
    /// </summary>
    private InventoryVoucher _voucher;

    [ObservableProperty] private string _title = string.Empty;
    [ObservableProperty] private string _subtitle = string.Empty;

    /// <summary>The voucher's stable id — the identity the header and the lifecycle routes key on.</summary>
    public Guid VoucherId { get; }

    /// <summary>True iff this voucher has been cancelled (Alt+X). The pane states it in words as well as in
    /// ink: colour alone is never the only carrier of a fact this material.</summary>
    public bool IsCancelled => _voucher.Cancelled;

    /// <summary>The cancelled badge text, or empty when the voucher is live — bound directly by the view so the
    /// template needs no converter and cannot disagree with <see cref="IsCancelled"/>.</summary>
    public string StatusLabel => _voucher.Cancelled ? "CANCELLED" : string.Empty;

    /// <summary>The projected lines: one row per stock movement / order line / counted line, with section
    /// headers where a voucher has two sides.</summary>
    public ObservableCollection<ReportRow> Rows { get; } = new();

    public InventoryVoucherDetailViewModel(Company company, InventoryVoucher voucher)
    {
        _company = company ?? throw new ArgumentNullException(nameof(company));
        _voucher = voucher ?? throw new ArgumentNullException(nameof(voucher));
        VoucherId = voucher.Id;
        Rebuild();
    }

    /// <summary>
    /// Re-reads the voucher from the company and re-projects the pane. Called after a lifecycle verb runs on
    /// the voucher this pane is showing, so a cancellation is visible where the operator performed it rather
    /// than only after they navigate away and back.
    ///
    /// <para>A voucher that is GONE (deleted) cannot be re-read; the pane keeps the last object it held and the
    /// shell pops the column, which is why this returns <c>false</c> rather than throwing — the caller decides
    /// whether a missing voucher means "refresh failed" or "the column should close".</para>
    /// </summary>
    public bool Refresh()
    {
        if (_company.FindInventoryVoucher(VoucherId) is not { } live) return false;

        _voucher = live;
        Rebuild();
        OnPropertyChanged(nameof(IsCancelled));
        OnPropertyChanged(nameof(StatusLabel));
        return true;
    }

    private void Rebuild()
    {
        var type = _company.FindVoucherType(_voucher.TypeId);
        var typeName = type?.Name ?? "(unknown)";

        Title = typeName;

        var number = _company.FormatVoucherNumber(_voucher);
        var party = _voucher.PartyId is { } pid ? _company.FindLedger(pid)?.Name : null;
        var parts = $"No. {number}  —  {_voucher.Date:dd-MMM-yyyy}";
        if (!string.IsNullOrWhiteSpace(party)) parts += $"  —  {party}";
        if (!string.IsNullOrWhiteSpace(_voucher.Narration)) parts += $"  —  {_voucher.Narration}";
        Subtitle = parts;

        Rows.Clear();

        // A Stock Journal / Material movement carries BOTH sides and they mean different things, so each side
        // gets a named heading. A one-sided movement (Receipt, Delivery, Rejection) gets none: a single
        // "Source"/"Destination" heading over the only list in the pane is noise, and mislabels a Receipt Note
        // (whose one list is INWARD) as a source.
        var twoSided = _voucher.Allocations.Count > 0 && _voucher.DestinationAllocations.Count > 0;

        if (twoSided) Rows.Add(Header("Source (consumed / transferred out)"));
        foreach (var a in _voucher.Allocations) Rows.Add(AllocationRow(a));

        if (twoSided) Rows.Add(Header("Destination (produced / transferred in)"));
        foreach (var a in _voucher.DestinationAllocations) Rows.Add(AllocationRow(a));

        foreach (var line in _voucher.OrderLines) Rows.Add(OrderRow(line));

        foreach (var line in _voucher.PhysicalLines) Rows.Add(PhysicalRow(line));

        if (Rows.Count == 0)
            Rows.Add(Header("This voucher carries no stock lines."));
    }

    private static ReportRow Header(string text) => new() { Col1 = text, Particulars = text, IsHeader = true };

    private ReportRow AllocationRow(InventoryAllocation a)
    {
        // Base-unit normalisation, exactly as InventoryRegisters and DayBook perform it: a line's rate is per
        // the unit the LINE is stated in, so quantity and rate must be converted by the same factor or the
        // Value column stops equalling Quantity x Rate — the one arithmetic a reader checks by eye.
        var quantity = a.Quantity;
        var rate = a.Rate?.Amount;
        if (a.UnitId is { } unitId && _company.FindUnit(unitId) is { } unit)
        {
            quantity = unit.QuantityInBaseMeasure(a.Quantity);
            if (rate is { } r) rate = unit.RateInBaseMeasure(r);
        }

        var value = rate is { } perUnit ? Money.ForexBase(new Money(perUnit), quantity) : Money.Zero;

        return new ReportRow
        {
            Col1 = Describe(a.StockItemId, a.GodownId, a.BatchLabel),
            Col2 = a.Direction == StockDirection.Inward ? "Inward" : "Outward",
            Col3 = IndianFormat.Quantity(quantity),
            Col4 = rate is { } pr ? IndianFormat.Amount(new Money(pr)) : string.Empty,
            Col5 = rate is null ? string.Empty : IndianFormat.Amount(value),
            Particulars = Describe(a.StockItemId, a.GodownId, a.BatchLabel),
        };
    }

    private ReportRow OrderRow(OrderLine line) => new()
    {
        Col1 = Describe(line.StockItemId, line.GodownId, batch: null),
        // An order moves NOTHING — neither stock nor accounts — so it has no direction to state. The word
        // "Ordered" is stated rather than the cell left blank, so a reader cannot mistake an order line for a
        // movement whose direction we failed to compute.
        Col2 = "Ordered",
        Col3 = IndianFormat.Quantity(line.Quantity),
        Col4 = line.Rate is { } r ? IndianFormat.Amount(r) : string.Empty,
        Col5 = line.Rate is { } rate ? IndianFormat.Amount(Money.ForexBase(rate, line.Quantity)) : string.Empty,
        Particulars = Describe(line.StockItemId, line.GodownId, batch: null),
    };

    private ReportRow PhysicalRow(PhysicalStockLine line) => new()
    {
        Col1 = Describe(line.StockItemId, line.GodownId, line.BatchLabel),
        // A physical count is a statement about the shelf (DP-3): it asserts a quantity, it does not move one,
        // and it carries no rate. The Rate and Value cells stay EMPTY rather than showing 0.00 — a zero there
        // would read as "this stock is worth nothing", which is a different and false claim.
        Col2 = "Counted",
        Col3 = IndianFormat.Quantity(line.CountedQuantity),
        Particulars = Describe(line.StockItemId, line.GodownId, line.BatchLabel),
    };

    private string Describe(Guid stockItemId, Guid godownId, string? batch)
    {
        var item = _company.FindStockItem(stockItemId)?.Name ?? "(unknown item)";
        var godown = _company.FindGodown(godownId)?.Name ?? "(unknown godown)";
        var text = $"{item}  @  {godown}";
        return string.IsNullOrWhiteSpace(batch) ? text : $"{text}  [{batch}]";
    }
}
