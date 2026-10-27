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
/// <para><b>What is NOT a divergence: the figures.</b> Quantity, Rate and Amount are the voucher's own
/// <see cref="InventoryAllocation"/> figures AS ENTERED — the quantity in the unit it was stated in, WITH that
/// unit, and the rate per that unit (see <see cref="StatedLine"/>), so printed Quantity x printed Rate is the
/// printed Amount on every line (T2-160). <c>InventoryVoucherDetailViewModel</c> renders its rows through the
/// same <see cref="StatedLine"/>, so the challan and the pane it is printed from are locked by construction.</para>
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

    /// <summary>
    /// 🔴 <b>w8 fix (review ec MEDIUM-1) — THE NOTICE FOR A MATERIAL OUT, WHICH MUST NOT SAY "NO PRINTED
    /// DOCUMENT".</b> A Material Out is the principal's dispatch of goods to a job worker, and the law names the
    /// document that travels with it. <c>cbic-gst.gov.in/pdf/circularno-38-cgst.pdf</c> (CBIC Circular
    /// No. 38/12/2018, dated 26 March 2018, opened BY CONTENT 2026-10-27), para 8.2: <i>"Rule 45 of the CGST Rules
    /// provides that the inputs, semi-finished goods or capital goods being sent for job work ... shall be sent
    /// under the cover of a challan issued by the principal, containing the details specified in rule 55"</i>; and
    /// para 8.4(i): <i>"The principal shall prepare in triplicate, the challan in terms of rules 45 and 55 of the
    /// CGST Rules, for sending the goods to a job worker."</i>
    ///
    /// <para><b>This product does not build that challan</b> — the Rule 55 particulars (consignor/consignee GSTIN,
    /// HSN, taxable value, tax rate and amount, place of supply) are not on our <see cref="DeliveryChallanPrintProjector"/>
    /// layout, and printing the Delivery-Note challan in its place would hand the job worker a document the rule
    /// does not describe. So the notice says the document EXISTS, names its legal basis, and says plainly that we
    /// do not produce it. The gap is recorded as IV-238's open half (<c>docs/invented-vs-cloned.md</c>).</para>
    /// </summary>
    public const string JobWorkChallanNotBuilt =
        "A Material Out sends goods to a job worker under a job-work delivery challan (CGST Rules 45 and 55). "
        + "This product does not print that challan yet, so prepare it outside the product.";

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
    /// <para>A Material Out is still NOT given a document here, but <see cref="Project"/> refuses it with
    /// <see cref="JobWorkChallanNotBuilt"/>, because a statutory source does name its challan (w8 fix, MEDIUM-1).</para>
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

        if (!PrintsAChallan(company, voucher))
        {
            // w8 fix (MEDIUM-1): a Material Out HAS a statutory document; say so rather than deny it exists.
            var isMaterialOut = company.FindVoucherType(voucher.TypeId)?.BaseType == VoucherBaseType.MaterialOut;
            return new DeliveryChallanProjection(null, isMaterialOut ? JobWorkChallanNotBuilt : NotAChallan);
        }

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
        int lineNo = 0;
        foreach (var a in voucher.Allocations)
        {
            var line = StatedLine(company, a);
            if (line.Rate is not null) total += line.Value.Amount;
            lineNo++;

            // 🔴 T2-160 F1 — THE DESCRIPTION OF GOODS IS DRAWN WHOLE. It used to sit in cell 0 of the six-column
            // grid, and ReportPdf clips every cell to its column's width: a long description reached the
            // consignee as "Hot-Dip Galvanised Steel Angle 75x7..." — goods this document does not actually
            // describe. It now rides FULL-WIDTH prose rows above its figures, wrapped by the SAME measure the
            // clipper uses (PdfWriter.WrapToWidth), so no line of it can be ellipsised. Layout OURS (IV-205).
            var description = ReportPrintProjector.Ascii(Describe(company, a));
            foreach (var part in PdfWriter.WrapToWidth(lineNo + ". " + description, DescriptionWidth, DescriptionFontSize))
                rows.Add(PrintRow.FullWidthText(part));

            rows.Add(new PrintRow(
                string.Empty,
                ReportPrintProjector.Ascii(company.FindGodown(a.GodownId)?.Name ?? string.Empty),
                ReportPrintProjector.Ascii(a.TrackingNumber ?? string.Empty),
                ReportPrintProjector.Ascii(line.QuantityText),
                line.Rate is { } r ? IndianFormat.Amount(new Money(r)) : string.Empty,
                line.Rate is null ? string.Empty : IndianFormat.Amount(line.Value)));
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
    /// The width, in points, a Description-of-Goods line is wrapped to. The narrowest sheet this document can
    /// be printed on is A4 portrait with ReportPdf's fixed 36pt margins: 595.28 - 72 = 523.28pt of content, less
    /// the 2pt cell padding each side a full-width row is drawn inside. 500 leaves a margin under that, and every
    /// other page size and orientation the print panel offers is wider.
    /// </summary>
    public const double DescriptionWidth = 500;

    /// <summary>The body font size a description line is measured at — <see cref="PageConfig"/>'s default
    /// body size, the largest a body row is ever drawn at (dot-matrix draws at 8).</summary>
    public const double DescriptionFontSize = 9;

    /// <summary>
    /// One stock line as the CONSIGNEE reads it: the quantity <b>in the unit it was entered in, with that
    /// unit</b>, the rate per that same unit, and the amount they multiply out to.
    /// </summary>
    /// <param name="QuantityText">e.g. <c>"2 Doz"</c>, <c>"12,345.678 MT"</c> — never a bare number.</param>
    /// <param name="Rate">The rate per the stated unit, or <c>null</c> when the line carries none.</param>
    /// <param name="Value">Quantity x Rate, rounded once to the paisa (zero when there is no rate).</param>
    public readonly record struct ChallanLine(string QuantityText, decimal? Rate, Money Value);

    /// <summary>
    /// 🔴 <b>T2-160 F2 / F8 — THE LINE IS PRINTED IN THE UNIT IT WAS STATED IN, SO IT MULTIPLIES OUT.</b> This
    /// used to normalise both quantity and rate to the item's BASE unit, which kept the amount right and made
    /// the two visible figures wrong to a reader: "2 Doz @ 10.00" printed as <c>24 · 0.83 · 20.00</c>, and
    /// 24 x 0.83 is 19.92 — and the quantity carried no unit at all, so "24" of what was anyone's guess
    /// (12,345.678 MT printed as a bare <c>12,345.678</c>). The vendor's own entry screen states the
    /// <i>Quantity</i> and the <i>Rate</i> the operator typed
    /// (<c>help.tallysolutions.com/tally-prime/sales-process/sales-order-tally/</c>); printing those, with the
    /// unit, is the one rendering in which printed Quantity x printed Rate = printed Amount for every unit.
    ///
    /// <para>The rate on a line is per the unit the line is stated in (<c>Unit.RateInBaseMeasure</c>'s own
    /// contract), so no conversion is needed here at all; the base-unit route only ever existed to undo itself.
    /// A line with no unit of its own is stated in the item's base unit, and that unit's symbol is printed.</para>
    ///
    /// <para>Public because <see cref="ViewModels.InventoryVoucherDetailViewModel"/> renders its own rows through
    /// it: the challan and the pane it is printed from are locked by construction, not by two copies of a rule.
    /// </para>
    /// </summary>
    public static ChallanLine StatedLine(Company company, InventoryAllocation a)
    {
        ArgumentNullException.ThrowIfNull(company);
        ArgumentNullException.ThrowIfNull(a);
        var rate = a.Rate?.Amount;
        var value = rate is { } perUnit ? Money.ForexBase(new Money(perUnit), a.Quantity) : Money.Zero;
        return new ChallanLine(QuantityWithUnit(company, a.StockItemId, a.UnitId, a.Quantity), rate, value);
    }

    /// <summary>
    /// A quantity followed by the symbol of the unit it is stated in — the line's own unit when it has one,
    /// else the item's base unit. A unit that cannot be resolved prints the bare figure rather than a guess.
    /// </summary>
    public static string QuantityWithUnit(Company company, Guid stockItemId, Guid? unitId, decimal quantity)
    {
        ArgumentNullException.ThrowIfNull(company);
        var unit = unitId is { } uid
            ? company.FindUnit(uid)
            : company.FindStockItem(stockItemId) is { } item ? company.FindUnit(item.BaseUnitId) : null;
        var figure = IndianFormat.Quantity(quantity);
        return unit is null || string.IsNullOrWhiteSpace(unit.Symbol) ? figure : figure + " " + unit.Symbol;
    }

    private static string Describe(Company company, InventoryAllocation a)
    {
        var item = company.FindStockItem(a.StockItemId)?.Name ?? "(unknown item)";
        return string.IsNullOrWhiteSpace(a.BatchLabel) ? item : item + "  [" + a.BatchLabel + "]";
    }
}
