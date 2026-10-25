using System;
using System.Collections.Generic;
using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Io;

namespace Apex.Desktop.Services;

/// <summary>
/// The outcome of asking a pure-stock voucher for its printed document. <b>Exactly one of the two members is
/// non-null</b> — a projection either produced a document or stated why it produced none, and "neither" would
/// leave the shell with nothing to show and nothing to say. Pinned by
/// <c>DeliveryChallanProjectionTests.Every_pure_stock_kind_yields_either_a_document_or_a_reason</c>, which
/// drives every base type through it.
/// </summary>
/// <param name="Document">The challan, or <c>null</c> when none could be produced.</param>
/// <param name="Refusal">Why no document was produced, or <c>null</c> when one was.</param>
public readonly record struct DeliveryChallanProjection(PrintReport? Document, string? Refusal);

/// <summary>
/// 🔴 <b>CENSUS 12.7's THIRD DOCUMENT — the DELIVERY CHALLAN.</b> Projects a posted <b>Delivery Note</b>
/// voucher into the challan that travels with the goods.
///
/// <para><b>GROUNDING (R7, ruling 14 — the vendor's own published documentation, opened BY CONTENT
/// 2026-10-23).</b></para>
/// <list type="bullet">
///   <item><b>The delivery note IS the challan</b>, which is why this is a voucher document and not a fourth
///     entry in the multi-account print panel: <c>help.tallysolutions.com/tally-prime/sales-process/faq-sales/</c>
///     — <i>"A delivery note is an inventory document/challan sent with the goods to be delivered to the
///     customer."</i> The same page: <i>"In such a case the sales invoice is issued on a later date referring
///     the delivery note/challan issued."</i></item>
///   <item><b>The fields it captures</b> —
///     <c>help.tallysolutions.com/tally-prime/sales-process/sales-order-tally/</c>, "Record a Delivery Note for
///     Goods Dispatched": the <i>Party A/c name</i>, a <i>Tracking No.</i>, an <i>Order No.</i>, and per line a
///     <i>Quantity</i> and a <i>Rate</i>; a <i>Godown</i> is named when an order is pre-closed. Those are the
///     columns below, minus Order No. — see the divergence note.</item>
///   <item><b>It moves no money</b>, which is why no Dr/Cr table appears and why the figures are not read from
///     any posting: same page — <i>"A Delivery Note with Tracking No. marked as Not Applicable is an inventory
///     document affecting only the inventory values"</i>, <i>"This will not affect the accounts."</i></item>
///   <item><b>The route</b> — <c>help.tallysolutions.com/keyboard-shortcuts-tally-prime/</c>: Delivery Note is
///     <i>"To open Delivery Note"</i> at <b>Alt+F8</b> under <i>"F10 &gt; Inventory Vouchers"</i>, and
///     <b>Ctrl+P</b> is <i>"To print the current voucher or report"</i>. We print it from the drilled voucher
///     with Ctrl+P, which is that row.</item>
/// </list>
///
/// <para>🔴 <b>DIVERGENCE, OURS (ruling 9) — THE LAYOUT, AND IT IS NOT A SMALL PART OF THE DOCUMENT.</b> Four
/// TallyPrime pages were opened by content and <b>none states a printed layout for a delivery note</b>: the two
/// cited above, <c>help.tallysolutions.com/inventory-vouchers-tallyprime/</c> (which mentions the delivery note
/// once and links away), and <c>help.tallysolutions.com/configure-for-print-export-share/</c> (whose print
/// options are stated for invoices and never name a delivery note). <b>That is a bounded statement about four
/// pages actually opened, not a claim that no such page exists</b> — a negative is not a ground. So the title
/// string, the column set, the column order, the acknowledgement line and the wording are OURS and are recorded
/// in <c>docs/invented-vs-cloned.md</c> (IV-159). They can never join the compared set.</para>
///
/// <para><b>What is NOT a divergence: the figures.</b> Quantity, Rate and Amount come from the voucher's own
/// <see cref="InventoryAllocation"/>s with exactly the base-unit normalisation
/// <c>InventoryVoucherDetailViewModel.AllocationRow</c> performs — pairing a displayed quantity with a per-base
/// rate understates by the conversion factor and the reverse overstates by it, so the two sites are locked
/// together by <c>DeliveryChallanFiguresMatchTheDrilledPaneTests</c> rather than merely written alike.</para>
///
/// <para>🔴 <b>IT REFUSES RATHER THAN ADDRESSES NOBODY.</b> A challan is handed to a third party, so a Delivery
/// Note that names no party yields no document and a reason instead. Our entry screen makes the party optional
/// (<c>InventoryVoucherEntryViewModel.BuildMovementNote</c> posts a null for "(none)"), and a printed challan
/// headed "To:" with nothing after it is a document the books cannot support. This is the same direction
/// <c>MultiAccountPrintProjector.MayProduce</c> takes for this row's other two documents.</para>
///
/// <para>Pure and Avalonia-free (ER-12): it reads the aggregate and shapes rows, and never touches disk,
/// dialogs, OS-print or the clock.</para>
/// </summary>
public static class DeliveryChallanPrintProjector
{
    /// <summary>The heading the challan carries. OURS (ruling 9) — see the type note.</summary>
    public const string Title = "Delivery Challan";

    /// <summary>
    /// The notice the shell shows when a drilled pure-stock voucher is not a Delivery Note at all. Stated once
    /// so the message and the refusal cannot drift.
    /// </summary>
    public const string NotAChallan =
        "This voucher has no printed document. Only a Delivery Note prints a delivery challan.";

    /// <summary>The notice for a Delivery Note that names no party — see the type note.</summary>
    public const string NoParty =
        "This Delivery Note names no party, so there is nobody to address a delivery challan to.";

    /// <summary>
    /// 🔴 <b>THE ONE PLACE A DATE BECOMES TEXT ON THIS DOCUMENT — and it must never read the ambient
    /// culture.</b> <see cref="System.Globalization.CultureInfo.CurrentCulture"/> carries a CALENDAR, so a bare
    /// <c>ToString("dd-MM-yyyy")</c> prints <c>01-04-2563</c> on a <c>th-TH</c> machine for the
    /// <c>01-04-2020</c> the books hold. Measured and fixed on this row's other two documents already
    /// (<c>MultiAccountPrintProjector.Day</c>); the same rule, stated the same way, applies here.
    /// </summary>
    private static string Day(DateOnly date) =>
        date.ToString("dd-MM-yyyy", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>
    /// Whether <paramref name="voucher"/> is the kind of voucher that prints a delivery challan — a
    /// <b>Delivery Note</b>, and nothing else.
    ///
    /// <para>Deliberately narrow. Tally.ERP 9 pages describe printing a challan from a Material Out / Material
    /// Transfer Out voucher too, and <b>Tally.ERP 9 is not ground truth on this project</b>; no TallyPrime page
    /// we opened says it, so those kinds are left alone rather than given a document on a lead.</para>
    /// </summary>
    public static bool PrintsAChallan(Company company, InventoryVoucher voucher)
    {
        ArgumentNullException.ThrowIfNull(company);
        ArgumentNullException.ThrowIfNull(voucher);
        return company.FindVoucherType(voucher.TypeId)?.BaseType == VoucherBaseType.DeliveryNote;
    }

    /// <summary>
    /// Projects <paramref name="voucher"/> into its delivery challan, or states why it has none. Exactly one
    /// member of the result is non-null.
    /// </summary>
    public static DeliveryChallanProjection Project(Company company, InventoryVoucher voucher)
    {
        ArgumentNullException.ThrowIfNull(company);
        ArgumentNullException.ThrowIfNull(voucher);

        if (!PrintsAChallan(company, voucher)) return new DeliveryChallanProjection(null, NotAChallan);

        var party = voucher.PartyId is { } pid ? company.FindLedger(pid) : null;
        if (party is null) return new DeliveryChallanProjection(null, NoParty);

        var rows = new List<PrintRow>
        {
            // 🔴 RULING 18: the counterparty's name is BOOK DATA and is drawn verbatim. It reaches the page
            // through ReportPdf.DrawRowCells, which no longer scrubs body cells, so a customer whose legal name
            // happens to carry the forbidden token is still named correctly on the document handed to them.
            // FullWidth, because an addressee clipped to one column's width ("To: Bharat Heavy Electr...") is a
            // consignee this document does not actually name.
            PrintRow.FullWidthHeader("To: " + ReportPrintProjector.Ascii(party.Name)),
            PrintRow.FullWidthText(
                "The goods described below have been despatched to you. This challan is not a tax invoice "
                + "and demands no payment."),
            PrintRow.FullWidthText(string.Empty),
        };

        decimal total = 0m;
        foreach (var a in voucher.Allocations)
        {
            var (quantity, rate) = InBaseUnit(company, a);
            var value = rate is { } perUnit ? Money.ForexBase(new Money(perUnit), quantity) : Money.Zero;
            if (rate is not null) total += value.Amount;

            rows.Add(new PrintRow(
                ReportPrintProjector.Ascii(Describe(company, a)),
                ReportPrintProjector.Ascii(company.FindGodown(a.GodownId)?.Name ?? string.Empty),
                ReportPrintProjector.Ascii(a.TrackingNumber ?? string.Empty),
                IndianFormat.Quantity(quantity),
                rate is { } r ? IndianFormat.Amount(new Money(r)) : string.Empty,
                rate is null ? string.Empty : IndianFormat.Amount(value)));
        }

        // The quantity column is deliberately NOT totalled: lines may be stated in different stock units and a
        // sum of Nos and Kgs is a number that means nothing. The value column is paisa-exact across lines.
        rows.Add(PrintRow.Total(
            "Total", string.Empty, string.Empty, string.Empty, string.Empty,
            IndianFormat.AmountAlways(total)));

        rows.Add(PrintRow.FullWidthText(string.Empty));
        // OURS (ruling 9): the acknowledgement wording and the ruled signature line. No vendor page we opened
        // states either. It asserts nothing about the goods — it is a place for the consignee to sign.
        rows.Add(PrintRow.FullWidthText("Received the above goods in good order and condition."));
        rows.Add(PrintRow.FullWidthText("Received by ____________________   Date ____________"));

        var number = company.FormatVoucherNumber(voucher);
        return new DeliveryChallanProjection(
            new PrintReport
            {
                // A compile-time constant, so the ER-11 scrub over it can never alter anything and
                // TitleCarriesMasterName stays FALSE — unlike the multi-account ledger-account sheet, whose
                // heading concatenates a master name and must therefore waive the guard.
                Title = Title,
                Subtitle = ReportPrintProjector.Ascii(company.Name)
                    + "  -  No. " + number + " dated " + Day(voucher.Date),
                Columns = new[]
                {
                    new PrintColumn("Description of Goods", 3.2, CellAlign.Left),
                    new PrintColumn("Godown", 1.5, CellAlign.Left),
                    new PrintColumn("Tracking No.", 1.4, CellAlign.Left),
                    new PrintColumn("Quantity", 1.2, CellAlign.Right),
                    new PrintColumn("Rate", 1.3, CellAlign.Right),
                    new PrintColumn("Amount", 1.5, CellAlign.Right),
                },
                Rows = rows,
            },
            null);
    }

    /// <summary>
    /// 🔴 <b>Base-unit normalisation, byte-for-byte as <c>InventoryVoucherDetailViewModel.AllocationRow</c>
    /// performs it.</b> A line's rate is per the unit the LINE is stated in, so the quantity and the rate must be
    /// converted by the SAME factor or Amount stops equalling Quantity × Rate — the one arithmetic a consignee
    /// checks by eye. Converting only the quantity overstates "2 Doz @ 10" as 240; converting only the rate
    /// understates it as 1.67.
    /// </summary>
    private static (decimal Quantity, decimal? Rate) InBaseUnit(Company company, InventoryAllocation a)
    {
        var quantity = a.Quantity;
        var rate = a.Rate?.Amount;
        if (a.UnitId is { } unitId && company.FindUnit(unitId) is { } unit)
        {
            quantity = unit.QuantityInBaseMeasure(a.Quantity);
            if (rate is { } r) rate = unit.RateInBaseMeasure(r);
        }
        return (quantity, rate);
    }

    private static string Describe(Company company, InventoryAllocation a)
    {
        var item = company.FindStockItem(a.StockItemId)?.Name ?? "(unknown item)";
        return string.IsNullOrWhiteSpace(a.BatchLabel) ? item : item + "  [" + a.BatchLabel + "]";
    }
}
