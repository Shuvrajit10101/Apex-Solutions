using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Io;
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
/// <para>🔴 <b>IT NOW CARRIES ONE PRINT ROUTE — THE DELIVERY CHALLAN (census 12.7), AND THE NOTE THAT USED TO
/// STAND HERE WAS RIGHT ABOUT THE PRINCIPLE AND WRONG ABOUT THE FACT.</b> It said this pane carries "no print
/// route", and that "a Ctrl+P here that silently produced an invoice-shaped document would be a wrong document
/// rather than a missing one". The principle holds and is why the route below is narrow. The FACT did not:
/// <c>MainWindowViewModel.OpenPrintPreview</c> had no arm for <see cref="Screen.InventoryVoucherDetail"/> at
/// all, so Ctrl+P on a drilled stock voucher fell through to its <c>Reports is not null</c> arm and printed
/// <b>the whole register the operator had drilled from</b> — every other party's movements on a sheet raised
/// from one voucher. That is the wrong document, and it was already shipping. Measured on emitted bytes by
/// <c>DeliveryChallanRouteTests</c>. The arm now produces the challan for a <b>Delivery Note</b> and REFUSES
/// (with a notice) for every other pure-stock kind, because refusing is what the note above actually
/// asked for.</para>
///
/// <para>🔴 <b>EXPORT AND SHARE NOW ACT ON THIS VOUCHER TOO (T2-159) — AND UNTIL THEY DID, THEY ACTED ON THE
/// REGISTER BEHIND IT.</b> The note here used to say the e-mail route was "deliberately absent". It was not
/// absent: <c>MainWindowViewModel.IsReportContext</c> excluded the two accounting drill screens by id but not
/// this one, so with a register still bound beneath the pane, Ctrl+E exported <b>the whole Day Book</b> and
/// Alt+M &gt; E-Mail attached it — every other consignee's movements, to whichever address the operator typed.
/// The shell now routes all three verbs here: <see cref="BuildExportPanel"/> exports THIS voucher's lines (the
/// challan as its PDF when it has one), and the share channels attach the same challan Print produces or refuse
/// with <see cref="DeliveryChallanProjection.Refusal"/> — never the register.</para>
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

    /// <summary>The same lines as <see cref="Rows"/>, typed for export (rate and value as exact Number cells),
    /// built in the same pass so the exported file cannot disagree with the pane.</summary>
    private readonly List<TabularRow> _exportRows = new();

    /// <summary>
    /// 🔴 <b>Census 12.7 — the delivery challan this voucher prints, or why it prints none.</b> Read off the
    /// voucher this pane is CURRENTLY pointed at (<see cref="Refresh"/> re-points it), so an altered Delivery
    /// Note cannot print its superseded quantities — the staleness defect
    /// <see cref="VoucherDetailViewModel"/> records for the accounting pane.
    ///
    /// <para>Exactly one half is non-null, which is what lets the shell either open a preview or say something
    /// rather than silently do neither — or, as it did before this slice, print the register behind the
    /// pane.</para>
    /// </summary>
    public DeliveryChallanProjection ChallanProjection =>
        DeliveryChallanPrintProjector.Project(_company, _voucher);

    /// <summary>
    /// Builds the print-preview VM for this voucher's delivery challan, or <c>null</c> when it has none (the
    /// caller then shows <see cref="DeliveryChallanProjection.Refusal"/> instead). The job is a ONE-document
    /// set so the challan renders through exactly the <see cref="ReportPdf"/> path this row's reminder letter
    /// and confirmation of accounts already render through.
    /// </summary>
    public PrintPreviewViewModel? BuildPrintPreview()
    {
        if (ChallanProjection.Document is not { } challan) return null;
        return new PrintPreviewViewModel(challan, DeliveryChallanPrintProjector.Title);
    }

    /// <summary>
    /// 🔴 <b>T2-159 — Ctrl+E on this pane exports THIS voucher, and nothing else.</b> The vendor's Ctrl+E is
    /// <i>"To export the current voucher or report"</i>
    /// (<c>help.tallysolutions.com/tally-prime/keyboard-shortcuts/keyboard-shortcuts-tally-prime/</c>). Every
    /// tabular format carries the pane's own lines under a heading naming the voucher; PDF is the delivery
    /// challan when this voucher prints one (so Export &gt; PDF and Print are the same document), and otherwise
    /// the same lines laid out by <see cref="ExportViewModel.TabularToPrint"/>.
    /// </summary>
    public ExportViewModel BuildExportPanel(string folder, DateTime now, Action<string, byte[]>? writeBytes = null)
    {
        Func<PrintReport>? challan = ChallanProjection.Document is null
            ? null
            : () => ChallanProjection.Document ?? ExportViewModel.TabularToPrint(ProjectTabular());
        return new ExportViewModel(ExportTitle, ProjectTabular, challan, folder, now, writeBytes);
    }

    /// <summary>The document title the export and share panels carry: the voucher type and its number.</summary>
    public string ExportTitle => $"{Title} No. {_company.FormatVoucherNumber(_voucher)}";

    /// <summary>This voucher's lines as a <see cref="TabularExport"/> — the pane, typed.</summary>
    public TabularExport ProjectTabular()
    {
        var rows = new List<TabularRow>(_exportRows.Count + 1)
        {
            TabularRow.Header(TabularCell.Text(Title + "  —  " + Subtitle)),
        };
        rows.AddRange(_exportRows);
        return new TabularExport(
            ExportTitle,
            new[]
            {
                new TabularColumn("Particulars"),
                new TabularColumn("Direction"),
                new TabularColumn("Quantity"),
                new TabularColumn("Rate", CellType.Number),
                new TabularColumn("Value", CellType.Number),
            },
            rows);
    }

    /// <summary>
    /// The PDF the share channels (Alt+M &gt; E-Mail / WhatsApp, M, Ctrl+M, W) attach: the SAME bytes Print
    /// renders for the challan, or <c>null</c> when this voucher prints none — the caller then refuses with
    /// <see cref="DeliveryChallanProjection.Refusal"/>, never falling back to anything else.
    /// </summary>
    public byte[]? RenderSharedDocument() => BuildPrintPreview()?.PdfBytes;

    /// <summary>Why <see cref="RenderSharedDocument"/> returned <c>null</c> — the sentence the shell shows.</summary>
    public string ShareRefusal => ChallanProjection.Refusal ?? DeliveryChallanPrintProjector.NotAChallan;

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
        _exportRows.Clear();

        // A Stock Journal / Material movement carries BOTH sides and they mean different things, so each side
        // gets a named heading. A one-sided movement (Receipt, Delivery, Rejection) gets none: a single
        // "Source"/"Destination" heading over the only list in the pane is noise, and mislabels a Receipt Note
        // (whose one list is INWARD) as a source.
        var twoSided = _voucher.Allocations.Count > 0 && _voucher.DestinationAllocations.Count > 0;

        if (twoSided) AddHeader("Source (consumed / transferred out)");
        foreach (var a in _voucher.Allocations) AllocationRow(a);

        if (twoSided) AddHeader("Destination (produced / transferred in)");
        foreach (var a in _voucher.DestinationAllocations) AllocationRow(a);

        foreach (var line in _voucher.OrderLines) OrderRow(line);

        foreach (var line in _voucher.PhysicalLines) PhysicalRow(line);

        if (Rows.Count == 0)
            AddHeader("This voucher carries no stock lines.");
    }

    private void AddHeader(string text)
    {
        Rows.Add(new ReportRow { Col1 = text, Particulars = text, IsHeader = true });
        _exportRows.Add(TabularRow.Header(TabularCell.Text(text)));
    }

    /// <summary>Adds one line to the pane AND to the export rows, from the same figures.</summary>
    private void AddLine(string particulars, string direction, string quantity, decimal? rate, Money? value)
    {
        Rows.Add(new ReportRow
        {
            Col1 = particulars,
            Col2 = direction,
            Col3 = quantity,
            Col4 = rate is { } r ? IndianFormat.Amount(new Money(r)) : string.Empty,
            Col5 = value is { } v ? IndianFormat.Amount(v) : string.Empty,
            Particulars = particulars,
        });
        _exportRows.Add(TabularRow.Of(
            TabularCell.Text(particulars),
            TabularCell.Text(direction),
            TabularCell.Text(quantity),
            rate is { } nr ? TabularCell.Number(nr) : TabularCell.Empty,
            value is { } nv ? TabularCell.Money(nv) : TabularCell.Empty));
    }

    private void AllocationRow(InventoryAllocation a)
    {
        // 🔴 T2-160 — STATED UNIT, WITH THE UNIT. This normalised quantity AND rate to the item's base unit, which
        // kept Value right and broke the arithmetic a reader checks by eye: "2 Doz @ 10.00" showed 24 · 0.83 ·
        // 20.00, and 24 x 0.83 is 19.92. It also showed "24" with no unit at all. The line is now shown as it was
        // ENTERED — "2 Doz" · 10.00 · 20.00 — through the one helper the delivery challan prints with, so the pane
        // and the document printed from it cannot disagree.
        var line = DeliveryChallanPrintProjector.StatedLine(_company, a);
        AddLine(
            Describe(a.StockItemId, a.GodownId, a.BatchLabel),
            a.Direction == StockDirection.Inward ? "Inward" : "Outward",
            line.QuantityText,
            line.Rate,
            line.Rate is null ? null : line.Value);
    }

    // An order moves NOTHING — neither stock nor accounts — so it has no direction to state. The word "Ordered" is
    // stated rather than the cell left blank, so a reader cannot mistake an order line for a movement whose
    // direction we failed to compute. An order line carries no unit of its own: it is in the item's base unit.
    private void OrderRow(OrderLine line) => AddLine(
        Describe(line.StockItemId, line.GodownId, batch: null),
        "Ordered",
        DeliveryChallanPrintProjector.QuantityWithUnit(_company, line.StockItemId, null, line.Quantity),
        line.Rate?.Amount,
        line.Rate is { } rate ? Money.ForexBase(rate, line.Quantity) : null);

    // A physical count is a statement about the shelf (DP-3): it asserts a quantity, it does not move one, and it
    // carries no rate. The Rate and Value cells stay EMPTY rather than showing 0.00 — a zero there would read as
    // "this stock is worth nothing", which is a different and false claim.
    private void PhysicalRow(PhysicalStockLine line) => AddLine(
        Describe(line.StockItemId, line.GodownId, line.BatchLabel),
        "Counted",
        DeliveryChallanPrintProjector.QuantityWithUnit(_company, line.StockItemId, null, line.CountedQuantity),
        null,
        null);

    private string Describe(Guid stockItemId, Guid godownId, string? batch)
    {
        var item = _company.FindStockItem(stockItemId)?.Name ?? "(unknown item)";
        var godown = _company.FindGodown(godownId)?.Name ?? "(unknown godown)";
        var text = $"{item}  @  {godown}";
        return string.IsNullOrWhiteSpace(batch) ? text : $"{text}  [{batch}]";
    }
}
