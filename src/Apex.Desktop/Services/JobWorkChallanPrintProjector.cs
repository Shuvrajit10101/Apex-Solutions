using System;
using System.Collections.Generic;
using System.Linq;
using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Io;
using Apex.Ledger.Reports;
using Apex.Ledger.Services;

namespace Apex.Desktop.Services;

/// <summary>
/// 🔴 <b>CENSUS 5.11 (wave 9, IV-260) — THE JOB-WORK DELIVERY CHALLAN that travels with a Material Out.</b> A
/// Material Out is the principal's dispatch of goods to a job worker, and before this projector existed the
/// product said so and printed nothing (<see cref="DeliveryChallanPrintProjector.JobWorkChallanNotBuilt"/>).
///
/// <para><b>GROUNDING (R7, ruling 14 — official statutory sources, opened BY CONTENT 2026-10-29).</b> The vendor
/// pages are silent on what a Material Out prints (IV-161), so the order of sources falls to the statute.</para>
/// <list type="bullet">
///   <item><b>That the challan exists, and who issues it</b> — <c>cbic-gst.gov.in/pdf/circularno-38-cgst.pdf</c>
///     (CBIC Circular No. 38/12/2018, dated 26 March 2018), para 8.2: goods sent for job work "shall be sent under
///     the cover of a challan issued by the principal, containing the details specified in rule 55 of the CGST
///     Rules"; para 8.4(i): the principal "shall prepare in triplicate, the challan in terms of rules 45 and 55".
///     The same para 8.2 says section 143 lets the principal send the goods "without payment of tax", which is the
///     sentence printed under the figures.</item>
///   <item><b>What it carries</b> — <c>cbic-gst.gov.in/gst-invoice-rules.html</c>, the rule "Transportation of
///     goods without issue of invoice" (numbered Rule 8 on that CBIC page of the Invoice Rules; the circular above
///     cites the notified text as rule 55): the delivery challan carries (a) its date and number, (b) the name,
///     address and GSTIN of the consigner, if registered, (c) the name, address and GSTIN or UIN of the consignee,
///     if registered, (d) the HSN code and description of goods, (e) the quantity, (f) the taxable value, (g) the
///     tax rate and tax amount — central, State, integrated, Union territory tax or cess, (h) the place of supply,
///     in case of inter-State movement, and (i) a signature. Every one of (a)-(i) is a row or a column below.</item>
/// </list>
///
/// <para>🔴 <b>DIVERGENCE, OURS (ruling 9) — THE LAYOUT (IV-260).</b> The rule lists particulars; it prints no
/// form. So the title, the block order, the column set and order, the captions, the tax-head footer and the
/// signature wording are ours. The layout is deliberately NOT the Delivery-Note challan's
/// (<see cref="DeliveryChallanPrintProjector"/>): that one is a customer's goods note with no GSTIN, HSN, tax rate
/// or place of supply, which is exactly why printing it here was refused in wave 8.</para>
///
/// <para><b>The figures are the voucher's own.</b> The goods are the movement's OUTWARD lines (the source side,
/// what leaves the principal's godown); quantity, unit and taxable value are those lines AS STATED through the
/// shared <see cref="DeliveryChallanPrintProjector.StatedLine"/>, so the challan and the drilled pane cannot
/// disagree. The rate is the item's GST rate resolved AS OF the voucher date by
/// <see cref="GstService.ResolveRate"/>, the same resolver every invoice uses; the tax is <see cref="GstService.ComputeLineTax"/> on the line's
/// taxable value, so CGST + SGST == IGST to the paisa exactly as on an invoice (IV-261).</para>
///
/// <para>🔴 <b>IT REFUSES RATHER THAN ADDRESSES NOBODY</b> — a challan with no consignee names no job worker, so a
/// movement with no party (its own or its linked order's) yields <see cref="NoJobWorker"/>; one with no outward
/// line yields <see cref="NoGoods"/>.</para>
///
/// <para>Pure and Avalonia-free (ER-12). It never touches disk, the clock, or the company graph: rates are read
/// through the resolver, which creates nothing.</para>
/// </summary>
public static class JobWorkChallanPrintProjector
{
    /// <summary>The heading the challan carries. OURS (ruling 9), worded after the rule's "delivery challan" and
    /// the circular's "job work".</summary>
    public const string Title = "Delivery Challan for Job Work";

    /// <summary>The notice for a Material Out whose party (and whose linked order's party) is unset.</summary>
    public const string NoJobWorker =
        "This Material Out names no job worker, so there is nobody to address the job-work delivery challan to. "
        + "Choose the job worker as the party and save it again.";

    /// <summary>The notice for a Material Out with no outward (goods sent) line.</summary>
    public const string NoGoods =
        "This Material Out sends no goods out, so it has no job-work delivery challan to print.";

    /// <summary>Section 143's effect, stated on the page (circular 38/12/2018 para 8.2).</summary>
    public const string WithoutPaymentOfTax =
        "Goods sent for job work under section 143 of the CGST Act, without payment of tax.";

    private static string Day(DateOnly date) =>
        date.ToString("dd-MM-yyyy", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>
    /// The job worker the challan is addressed to: the movement's own party, else the party of the Job Work Out
    /// Order it fulfils. <c>null</c> when neither names one.
    /// </summary>
    public static Apex.Ledger.Domain.Ledger? Consignee(Company company, InventoryVoucher voucher)
    {
        ArgumentNullException.ThrowIfNull(company);
        ArgumentNullException.ThrowIfNull(voucher);
        if (voucher.PartyId is { } own && company.FindLedger(own) is { } party) return party;
        foreach (var orderId in voucher.OrderLinks)
            if (company.FindInventoryVoucher(orderId)?.PartyId is { } op && company.FindLedger(op) is { } orderParty)
                return orderParty;
        return null;
    }

    /// <summary>Projects a Material Out into its job-work delivery challan, or states why it has none.</summary>
    public static DeliveryChallanProjection Project(Company company, InventoryVoucher voucher)
    {
        ArgumentNullException.ThrowIfNull(company);
        ArgumentNullException.ThrowIfNull(voucher);

        var goods = voucher.Allocations.Where(a => a.Direction == StockDirection.Outward).ToList();
        if (goods.Count == 0) return new DeliveryChallanProjection(null, NoGoods);
        if (Consignee(company, voucher) is not { } worker) return new DeliveryChallanProjection(null, NoJobWorker);

        var workerState = worker.PartyGst?.StateCode;
        // Inter-State routing from the SAME read-only rule every return uses. null = the book cannot route (no
        // home State); the head is then left unstated rather than guessed.
        bool? interState = company.Gst is null ? null : GstReportSupport.RoutingOf(company, workerState);

        var number = company.FormatVoucherNumber(voucher);
        var rows = new List<PrintRow>
        {
            // (a) number and date — also in the subtitle, but stated as the rule's own particular too.
            PrintRow.FullWidthHeader("Challan No. " + ReportPrintProjector.Ascii(number) + "   Date " + Day(voucher.Date)),
            PrintRow.FullWidthText(string.Empty),
        };

        // (b) the consigner — the principal, i.e. this company.
        rows.Add(PrintRow.FullWidthHeader("Consigner (Principal): " + ReportPrintProjector.Ascii(
            string.IsNullOrWhiteSpace(company.MailingName) ? company.Name : company.MailingName)));
        foreach (var line in AddressLines(company.Address, string.IsNullOrWhiteSpace(company.Address) ? null : company.Country, company.Pin))
            rows.Add(PrintRow.FullWidthText(line));
        rows.Add(PrintRow.FullWidthText("GSTIN: " + GstinText(company.Gst?.Gstin)));
        rows.Add(PrintRow.FullWidthText("State: " + StateText(company.Gst?.HomeStateCode)));
        rows.Add(PrintRow.FullWidthText(string.Empty));

        // (c) the consignee — the job worker.
        rows.Add(PrintRow.FullWidthHeader("Consignee (Job Worker): " + ReportPrintProjector.Ascii(
            string.IsNullOrWhiteSpace(worker.Mailing?.MailingName) ? worker.Name : worker.Mailing!.MailingName!)));
        foreach (var line in AddressLines(worker.Mailing?.Address, worker.Mailing?.Country, worker.Mailing?.Pincode))
            rows.Add(PrintRow.FullWidthText(line));
        rows.Add(PrintRow.FullWidthText("GSTIN/UIN: " + GstinText(worker.PartyGst?.Gstin)));
        rows.Add(PrintRow.FullWidthText("State: " + StateText(workerState)));
        // (h) the place of supply — the rule asks for it on an inter-State movement; we state it whenever the
        // job worker's State is known, and say "(inter-State)" when the routing says so.
        var pos = StateText(workerState);
        rows.Add(PrintRow.FullWidthText("Place of Supply: " + (pos.Length == 0 ? "Not stated" : pos)
            + (interState == true ? "  (inter-State)" : string.Empty)));
        rows.Add(PrintRow.FullWidthText(string.Empty));

        var gst = new GstService(company);
        decimal totalValue = 0m, totalTax = 0m, totalCentral = 0m, totalState = 0m;
        int lineNo = 0;
        foreach (var a in goods)
        {
            lineNo++;
            var stated = DeliveryChallanPrintProjector.StatedLine(company, a);
            var item = company.FindStockItem(a.StockItemId);
            var rateBp = RateBasisPoints(gst, item, voucher.Date);
            var value = stated.Rate is null ? (Money?)null : stated.Value;
            // The SAME per-line rule every invoice uses (GstService.ComputeLineTax): the line's tax computed once,
            // then split CGST = round(total / 2), SGST = the remainder, or wholly IGST across States.
            GstService.LineTax? lineTax = value is { } v && rateBp is { } bp
                ? GstService.ComputeLineTax(v, bp, interState == true)
                : null;
            Money? tax = lineTax is { } lt ? lt.Cgst + lt.Sgst + lt.Igst : null;
            if (value is { } vv) totalValue += vv.Amount;
            if (lineTax is { } l2)
            {
                totalTax += (l2.Cgst + l2.Sgst + l2.Igst).Amount;
                totalCentral += l2.Cgst.Amount;
                totalState += l2.Sgst.Amount;
            }

            // (d) description of goods, drawn whole on its own full-width rows so a long name is never clipped.
            var description = ReportPrintProjector.Ascii(
                (item?.Name ?? "(unknown item)")
                + (string.IsNullOrWhiteSpace(a.BatchLabel) ? string.Empty : "  [" + a.BatchLabel + "]"));
            foreach (var part in PdfWriter.WrapToWidth(
                         lineNo + ". " + description,
                         DeliveryChallanPrintProjector.DescriptionWidth,
                         DeliveryChallanPrintProjector.DescriptionFontSize))
                rows.Add(PrintRow.FullWidthText(part));

            rows.Add(new PrintRow(
                string.Empty,
                ReportPrintProjector.Ascii(GstReportSupport.HsnSacOf(item) ?? string.Empty),   // (d) HSN
                ReportPrintProjector.Ascii(stated.QuantityText),                                // (e) quantity
                value is { } x ? IndianFormat.Amount(x) : string.Empty,                       // (f) taxable value
                rateBp is { } r ? RatePercent(r) : string.Empty,                               // (g) rate
                tax is { } t ? IndianFormat.Amount(t) : string.Empty));                       // (g) tax amount
        }

        rows.Add(PrintRow.Total(
            "Total", string.Empty, string.Empty,
            IndianFormat.AmountAlways(totalValue), string.Empty, IndianFormat.AmountAlways(totalTax)));

        // (g) the tax heads, summed from the per-line split. Stating them at the foot is OURS (IV-261).
        rows.Add(PrintRow.FullWidthText(string.Empty));
        if (interState == true)
            rows.Add(PrintRow.FullWidthText("Integrated tax: " + IndianFormat.AmountAlways(totalTax)));
        else if (interState == false)
            rows.Add(PrintRow.FullWidthText("Central tax: " + IndianFormat.AmountAlways(totalCentral)
                + "   State tax: " + IndianFormat.AmountAlways(totalState)));
        rows.Add(PrintRow.FullWidthText(WithoutPaymentOfTax));
        rows.Add(PrintRow.FullWidthText(string.Empty));
        // (i) the signature.
        rows.Add(PrintRow.FullWidthText("For " + ReportPrintProjector.Ascii(company.Name)));
        rows.Add(PrintRow.FullWidthText("Signature of the principal ____________________"));

        return new DeliveryChallanProjection(
            new PrintReport
            {
                Title = Title,
                Subtitle = ReportPrintProjector.Ascii(company.Name) + "  -  No. " + ReportPrintProjector.Ascii(number)
                    + " dated " + Day(voucher.Date),
                Columns = new[]
                {
                    new PrintColumn("Description of Goods", 2.2, CellAlign.Left),
                    new PrintColumn("HSN", 1.2, CellAlign.Left),
                    new PrintColumn("Quantity", 1.4, CellAlign.Right),
                    new PrintColumn("Taxable Value", 1.6, CellAlign.Right),
                    new PrintColumn("GST Rate", 1.0, CellAlign.Right),
                    new PrintColumn("Tax Amount", 1.5, CellAlign.Right),
                },
                Rows = rows,
            },
            null);
    }

    /// <summary>The item's GST rate in basis points as of <paramref name="date"/>, or <c>null</c> when the book
    /// has no GST, the rate cannot be resolved, or the goods are not taxable (an Exempt/Nil line prints no rate).</summary>
    private static int? RateBasisPoints(GstService gst, StockItem? item, DateOnly date)
    {
        if (item is null) return null;
        try
        {
            var res = gst.ResolveRate(item, salesPurchaseLedger: null, date);
            if (GstService.IsUnresolved(res) || !res.IsTaxable) return null;
            return res.RateBasisPoints;
        }
        catch (InvalidOperationException) { return null; }
    }

    /// <summary>"18%" / "2.5%" — basis points as a percentage, never a trailing ".00".</summary>
    public static string RatePercent(int basisPoints) =>
        (basisPoints / 100m).ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) + "%";

    private static string GstinText(string? gstin) =>
        string.IsNullOrWhiteSpace(gstin) ? "Unregistered" : ReportPrintProjector.Ascii(gstin.Trim());

    private static string StateText(string? code)
    {
        var st = IndianState.FromCode(code);
        return st is null ? string.Empty : ReportPrintProjector.Ascii($"{st.Name} ({st.Code})");
    }

    private static IEnumerable<string> AddressLines(string? address, string? country, string? pin)
    {
        if (!string.IsNullOrWhiteSpace(address))
            foreach (var part in address.Replace("\r\n", "\n").Replace('\r', '\n')
                         .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                yield return ReportPrintProjector.Ascii(part);
        if (!string.IsNullOrWhiteSpace(country)) yield return ReportPrintProjector.Ascii(country.Trim());
        if (!string.IsNullOrWhiteSpace(pin)) yield return "PIN: " + ReportPrintProjector.Ascii(pin.Trim());
    }
}
