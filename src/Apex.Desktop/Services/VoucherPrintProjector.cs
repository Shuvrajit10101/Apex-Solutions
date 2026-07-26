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
/// Projects a posted <see cref="Voucher"/> (with its <see cref="Company"/> context) into the framework-agnostic
/// print DTOs the <c>Apex.Ledger.Io</c> renderers consume (RQ-10 / RQ-11): a <see cref="VoucherPrintData"/> for a
/// plain accounting voucher, or an <see cref="InvoicePrintData"/> GST tax-invoice for a Sales voucher run in
/// item-invoice mode. The mapping is pure and Avalonia-free — it only resolves GUID→name masters, formats dates
/// and quantities to display strings, and runs the item lines through <see cref="GstService"/> so the printed
/// CGST/SGST/IGST reconcile to the posted tax ledgers to the paisa. It never touches disk, dialogs, OS-print or
/// the clock (ER-12): the whole IO path stays in <c>Apex.Ledger.Io</c>. No brand text is ever introduced.
/// </summary>
public static class VoucherPrintProjector
{
    /// <summary>
    /// True iff <paramref name="voucher"/> should print as a GST <b>tax invoice</b> rather than a plain voucher:
    /// a Sales voucher carrying item-invoice stock lines, <b>or</b> a Sales <b>accounting (service) invoice</b>
    /// (<see cref="IsServiceAccountingInvoice"/>). Purchase item-invoices and every other voucher print as the plain
    /// Dr/Cr voucher (RQ-10).
    /// </summary>
    public static bool IsTaxInvoice(Company company, Voucher voucher)
    {
        ArgumentNullException.ThrowIfNull(company);
        ArgumentNullException.ThrowIfNull(voucher);
        var type = company.FindVoucherType(voucher.TypeId);
        if (type?.BaseType != VoucherBaseType.Sales) return false;
        return voucher.HasInventoryLines || IsServiceAccountingInvoice(company, voucher);
    }

    /// <summary>
    /// True iff a <b>ledger-only</b> voucher is a SERVICE (Accounting Invoice) sale — the one ledger-only shape that
    /// may print as a tax invoice. Two conjuncts, both structural, both read off what was POSTED:
    /// <list type="number">
    /// <item>it posts at least one <b>forward GST tax leg carrying <see cref="GstLineTax"/> metadata</b>
    /// (<see cref="GstReportSupport.HasForwardTaxLines"/>) — the engine stamps that metadata; and</item>
    /// <item>it carries at least one SAC-bearing service-income leg (<see cref="Gstr1.ServiceLegs"/>), so the
    /// document has a line to print.</item>
    /// </list>
    ///
    /// <para><b>Conjunct 1 is the whole safety argument.</b> An EXISTING hand-keyed As-Voucher GST sale types its
    /// Output CGST/SGST legs by hand, as plain <c>EntryLine</c>s with <b>no</b> <see cref="GstLineTax"/> — so it fails
    /// this gate and keeps printing exactly as it does today, under exactly today's label. This is the same
    /// discriminator <c>Gstr1.AccumulateServiceHsn</c> already keys on (a hand-keyed voucher reads zero posted tax and
    /// short-circuits into the exempt branch), so the printed document and the filed return agree on what a service
    /// invoice IS. Relaxing this to "any ledger-only GST sale" would silently relabel vouchers the user already posted;
    /// recomputing tax at print time would make the printed figures diverge from the posted GL.</para>
    ///
    /// <para>A wholly EXEMPT service invoice posts no tax leg at all and is therefore structurally indistinguishable
    /// from a hand-keyed exempt sale; it keeps printing as a plain voucher. That is the conservative side of the
    /// ruling, deliberately chosen.</para>
    /// </summary>
    public static bool IsServiceAccountingInvoice(Company company, Voucher voucher)
    {
        ArgumentNullException.ThrowIfNull(company);
        ArgumentNullException.ThrowIfNull(voucher);
        if (voucher.HasInventoryLines) return false;
        if (!GstReportSupport.HasForwardTaxLines(voucher)) return false;
        return Gstr1.ServiceLegs(company, voucher).Any();
    }

    // ---------------------------------------------------------------- RQ-10: plain voucher

    /// <summary>
    /// Projects a voucher into a <see cref="VoucherPrintData"/> for <c>VoucherPdf</c>: company/title header,
    /// No/Date/Party line, the Dr/Cr posting lines (ledger names resolved) and the narration. Dates are
    /// formatted here so the renderer stays clock-free.
    /// </summary>
    public static VoucherPrintData ProjectVoucher(Company company, Voucher voucher)
    {
        ArgumentNullException.ThrowIfNull(company);
        ArgumentNullException.ThrowIfNull(voucher);

        var type = company.FindVoucherType(voucher.TypeId);
        var party = voucher.PartyId is Guid pid ? company.FindLedger(pid)?.Name : null;

        var lines = new List<VoucherPrintLine>(voucher.Lines.Count);
        foreach (var l in voucher.Lines)
            lines.Add(new VoucherPrintLine
            {
                LedgerName = ReportPrintProjector.Ascii(company.FindLedger(l.LedgerId)?.Name ?? "(unknown)"),
                IsDebit = l.Side == DrCr.Debit,
                Amount = l.Amount,
            });

        return new VoucherPrintData
        {
            CompanyName = ReportPrintProjector.Ascii(CompanyDisplayName(company)),
            VoucherTypeName = ReportPrintProjector.Ascii(type?.Name ?? string.Empty),
            VoucherNumber = company.FormatVoucherNumber(voucher),
            DateText = voucher.Date.ToString("dd-MM-yyyy", System.Globalization.CultureInfo.InvariantCulture),
            PartyName = ReportPrintProjector.Ascii(party ?? string.Empty),
            // Counterparty captured field (numbering §8): the other party's number, labelled per base type. Blank
            // when none was captured ⇒ nothing prints ⇒ byte-identical (ER-13).
            ReferenceNo = ReportPrintProjector.Ascii(voucher.ReferenceNo ?? string.Empty),
            ReferenceCaption = ReferenceCaption(type),
            ReferenceDateText = voucher.ReferenceDate is { } rd
                ? rd.ToString("dd-MM-yyyy", System.Globalization.CultureInfo.InvariantCulture)
                : string.Empty,
            Lines = lines,
            Narration = ReportPrintProjector.Ascii(voucher.Narration ?? string.Empty),
        };
    }

    // ---------------------------------------------------------------- RQ-11: tax invoice

    /// <summary>
    /// Projects a Sales item-invoice voucher into an <see cref="InvoicePrintData"/> GST tax invoice for
    /// <c>InvoicePdf</c>: the seller (company) and buyer (party) name/address/GSTIN/State blocks, the item rows
    /// (Sr resolved by row order, Description/HSN from the stock item, Qty/Rate formatted), the per-rate GST
    /// breakup and the money totals — all paisa-exact figures the <see cref="GstService"/> produced, so the
    /// printed tax reconciles to the posted tax ledgers. Intra vs inter is routed from the party's recorded
    /// State vs the company home State.
    /// </summary>
    public static InvoicePrintData ProjectInvoice(Company company, Voucher voucher)
    {
        ArgumentNullException.ThrowIfNull(company);
        ArgumentNullException.ThrowIfNull(voucher);

        var gst = new GstService(company);
        var partyLedger = voucher.PartyId is Guid pid ? company.FindLedger(pid) : null;
        var partyState = partyLedger?.PartyGst?.StateCode;
        bool interState = gst.IsInterState(partyState);

        // A SERVICE (Accounting Invoice) sale has no stock lines, so the item pass below would project an EMPTY
        // invoice. It takes its own projection, built entirely from the POSTED legs. The item path is untouched.
        if (IsServiceAccountingInvoice(company, voucher))
            return ProjectServiceInvoice(company, voucher, partyLedger);

        // The sales value ledger drives rate resolution (item → ledger → company). It is the posted entry line
        // whose ledger carries a Sales/Purchase GST block; fall back to the first non-party, non-tax ledger.
        var valueLedger = ResolveValueLedger(company, voucher, partyLedger?.Id);

        var items = new List<InvoiceItemRow>(voucher.InventoryLines.Count);
        var taxableByRate = new List<(int Bp, decimal Taxable)>();
        // Σ of EVERY item line's value — rated AND exempt/nil/non-GST/unresolved. This is the invoice's goods
        // (taxable-value) total that the Grand Total must foot to; the per-rate `taxableByRate` only drives the
        // GST tax, which is charged on rated lines alone (exempt/nil lines contribute their value at 0 tax).
        decimal totalGoodsValue = 0m;

        foreach (var il in voucher.InventoryLines)
        {
            var item = company.FindStockItem(il.StockItemId);
            // WI-10 Gap 2: label the quantity with the unit the LINE is actually stated in, not the item's base
            // unit — the printed quantity IS the line quantity, and the printed Rate is per that same unit, so
            // "2 Doz @ ₹10.00 = ₹20.00" reads correctly and foots. Falling back to the item's base unit keeps a
            // line that carries no unit byte-identical to before (ER-13). Printing "2 Nos @ ₹10 = ₹20" would be
            // internally consistent arithmetic on a QUANTITY THAT IS NOT WHAT MOVED (24 Nos did) — a document
            // the buyer, the auditor and the e-way bill would all read differently.
            var unit = il.UnitId is { } lineUnitId
                ? company.FindUnit(lineUnitId)?.Symbol
                : item is not null ? company.FindUnit(item.BaseUnitId)?.Symbol : null;
            var qtyText = IndianFormat.Quantity(il.Quantity);
            if (!string.IsNullOrEmpty(unit)) qtyText += " " + unit;

            items.Add(new InvoiceItemRow
            {
                Description = ReportPrintProjector.Ascii(item?.Name ?? "(item)"),
                HsnSac = ReportPrintProjector.Ascii(item?.Gst?.HsnSac ?? item?.HsnSacCode ?? string.Empty),
                QuantityText = ReportPrintProjector.Ascii(qtyText),
                RateText = IndianFormat.Amount(il.Rate),
                TaxableValue = il.Value,
            });
            totalGoodsValue += il.Value.Amount;

            var res = gst.ResolveRate(item, valueLedger);
            if (!res.IsTaxable || GstService.IsUnresolved(res)) continue; // Exempt/Nil/Non-GST/unresolved ⇒ no tax
            AccumulateRate(taxableByRate, res.RateBasisPoints, il.Value.Amount);
        }

        // Compute the whole-invoice tax once (all taxable lines, one call) so the head totals + round-off match the
        // engine exactly, then compute each rate group's tax separately for the per-rate breakup rows.
        var allTaxable = taxableByRate
            .Select(g => new GstService.TaxableLine(new Money(g.Taxable), g.Bp))
            .ToList();
        var invoiceTax = gst.ComputeInvoiceTax(allTaxable, interState, GstTaxDirection.Output, applyInvoiceRoundOff: true);

        var taxRows = new List<InvoiceTaxRow>(taxableByRate.Count);
        foreach (var (bp, taxable) in taxableByRate)
        {
            var lt = GstService.ComputeLineTax(new Money(taxable), bp, interState);
            taxRows.Add(new InvoiceTaxRow
            {
                RateLabel = RateLabel(bp),
                TaxableValue = new Money(taxable),
                Cgst = lt.Cgst,
                Sgst = lt.Sgst,
                Igst = lt.Igst,
            });
        }

        return new InvoicePrintData
        {
            Seller = SellerBlock(company),
            Buyer = BuyerBlock(company, partyLedger),
            InvoiceNumber = company.FormatVoucherNumber(voucher),
            InvoiceDateText = voucher.Date.ToString("dd-MM-yyyy", System.Globalization.CultureInfo.InvariantCulture),
            // Counterparty captured field (numbering §8): on a Sales tax invoice this is the buyer's "Reference No.".
            // Blank when none was captured ⇒ nothing prints ⇒ byte-identical (ER-13).
            ReferenceNo = ReportPrintProjector.Ascii(voucher.ReferenceNo ?? string.Empty),
            ReferenceCaption = ReferenceCaption(company.FindVoucherType(voucher.TypeId)),
            ReferenceDateText = voucher.ReferenceDate is { } rd
                ? rd.ToString("dd-MM-yyyy", System.Globalization.CultureInfo.InvariantCulture)
                : string.Empty,
            PlaceOfSupply = PlaceOfSupply(company, partyLedger),
            IsInterState = interState,
            Items = items,
            TaxRows = taxRows,
            // The taxable/goods total = sum of ALL line values (rated + exempt/nil), so exempt lines are never
            // silently dropped from the Grand Total (GrandTotal = TotalTaxable + TotalTax + RoundOff).
            TotalTaxable = new Money(totalGoodsValue),
            TotalCgst = invoiceTax.TotalCgst,
            TotalSgst = invoiceTax.TotalSgst,
            TotalIgst = invoiceTax.TotalIgst,
            RoundOff = invoiceTax.RoundOffAmount,
            Narration = ReportPrintProjector.Ascii(voucher.Narration ?? string.Empty),
        };
    }

    // ---------------------------------------------------------------- service (Accounting Invoice) tax invoice

    /// <summary>
    /// Projects a Sales <b>accounting (service) invoice</b> into an <see cref="InvoicePrintData"/> GST tax invoice —
    /// the service mirror of the item pass above. The seller/buyer/place-of-supply blocks are the SAME master reads;
    /// what differs is where the lines and the tax come from:
    /// <list type="bullet">
    /// <item><b>Lines</b> — one printed row per service-income leg (<see cref="Gstr1.ServiceLegs"/>), described by its
    /// ledger, carrying the SAC that <c>Gstr1</c>'s Table-12 row and the e-invoice <c>HsnCd</c> already use
    /// (<see cref="Gstr1.ServiceSacOf"/>) so the document, the return and the payload cannot disagree, and valued at
    /// the posted leg amount. A service has neither a quantity nor a per-unit rate, so those cells print blank.</item>
    /// <item><b>Tax</b> — read verbatim off the posted <see cref="GstLineTax"/> legs
    /// (<see cref="ReadPostedRateGroups"/>), <b>never recomputed</b>. Re-rating the service ledger's master after
    /// posting therefore cannot move a printed figure: the invoice the customer holds always states the tax the GL
    /// actually carries.</item>
    /// <item><b>Intra vs inter</b> — decided by which HEAD was posted, not re-derived from the party's (editable)
    /// State, for the same reason.</item>
    /// </list>
    /// <para>No round-off is printed: the accounting-invoice accept path computes its tax with
    /// <c>applyInvoiceRoundOff: false</c> and posts no round-off leg, so <c>GrandTotal</c> = Σ service legs + Σ posted
    /// tax foots to the posted party leg exactly.</para>
    /// </summary>
    private static InvoicePrintData ProjectServiceInvoice(
        Company company, Voucher voucher, Apex.Ledger.Domain.Ledger? partyLedger)
    {
        var items = new List<InvoiceItemRow>();
        // Σ of EVERY service leg — taxed AND exempt/nil — so an exempt line is never silently dropped from the
        // Grand Total (the same rule the item pass keeps with `totalGoodsValue`).
        decimal totalServiceValue = 0m;
        foreach (var (ledger, value) in Gstr1.ServiceLegs(company, voucher))
        {
            items.Add(new InvoiceItemRow
            {
                Description = ReportPrintProjector.Ascii(ledger.Name),
                HsnSac = ReportPrintProjector.Ascii(Gstr1.ServiceSacOf(ledger) ?? string.Empty),
                QuantityText = string.Empty, // a service carries no quantity …
                RateText = string.Empty,     // … and no per-unit rate
                TaxableValue = new Money(value),
            });
            totalServiceValue += value;
        }

        var groups = ReadPostedRateGroups(voucher);
        bool interState = PostedInterState(voucher);

        var taxRows = new List<InvoiceTaxRow>(groups.Count);
        decimal totalCgst = 0m, totalSgst = 0m, totalIgst = 0m;
        foreach (var g in groups)
        {
            taxRows.Add(new InvoiceTaxRow
            {
                RateLabel = RateLabel(g.Rate),
                TaxableValue = new Money(g.Taxable),
                Cgst = new Money(g.Cgst),
                Sgst = new Money(g.Sgst),
                Igst = new Money(g.Igst),
            });
            totalCgst += g.Cgst; totalSgst += g.Sgst; totalIgst += g.Igst;
        }

        return new InvoicePrintData
        {
            Seller = SellerBlock(company),
            Buyer = BuyerBlock(company, partyLedger),
            InvoiceNumber = company.FormatVoucherNumber(voucher),
            InvoiceDateText = voucher.Date.ToString("dd-MM-yyyy", System.Globalization.CultureInfo.InvariantCulture),
            ReferenceNo = ReportPrintProjector.Ascii(voucher.ReferenceNo ?? string.Empty),
            ReferenceCaption = ReferenceCaption(company.FindVoucherType(voucher.TypeId)),
            ReferenceDateText = voucher.ReferenceDate is { } rd
                ? rd.ToString("dd-MM-yyyy", System.Globalization.CultureInfo.InvariantCulture)
                : string.Empty,
            PlaceOfSupply = PlaceOfSupply(company, partyLedger),
            IsInterState = interState,
            Items = items,
            TaxRows = taxRows,
            TotalTaxable = new Money(totalServiceValue),
            TotalCgst = new Money(totalCgst),
            TotalSgst = new Money(totalSgst),
            TotalIgst = new Money(totalIgst),
            RoundOff = Money.Zero,
            Narration = ReportPrintProjector.Ascii(voucher.Narration ?? string.Empty),
        };
    }

    /// <summary>
    /// The per-(integrated rate) posted tax of a voucher, read straight off its <see cref="GstLineTax"/> legs — the
    /// print-side twin of <c>Gstr1.ReadInvoiceRateGroups</c>, with the same head exclusions so the printed breakup and
    /// the filed return can never disagree: the ring-fenced Compensation Cess is not a CGST/SGST/IGST rate row (it
    /// records the SAME taxable value on its own doubled key and would inject a phantom row), and reverse-charge legs
    /// are their own bucket, not forward tax. Within one rate group every leg records the same group taxable, so the
    /// max dedups the intra CGST+SGST pair. Ordered by rate for determinism.
    /// </summary>
    private static List<(int Rate, decimal Taxable, decimal Cgst, decimal Sgst, decimal Igst)> ReadPostedRateGroups(Voucher voucher)
    {
        var byRate = new Dictionary<int, (decimal Taxable, decimal Cgst, decimal Sgst, decimal Igst)>();
        foreach (var line in voucher.Lines)
        {
            if (line.Gst is not { } g || g.IsReverseCharge) continue;
            if (g.TaxHead == GstTaxHead.Cess) continue;
            var rate = GstReportSupport.IntegratedRateOf(g);
            var acc = byRate.TryGetValue(rate, out var cur) ? cur : default;
            switch (g.TaxHead)
            {
                case GstTaxHead.Central: acc.Cgst += line.Amount.Amount; break;
                case GstTaxHead.State: acc.Sgst += line.Amount.Amount; break;
                case GstTaxHead.Integrated: acc.Igst += line.Amount.Amount; break;
                default: continue;
            }
            if (g.TaxableValue.Amount > acc.Taxable) acc.Taxable = g.TaxableValue.Amount;
            byRate[rate] = acc;
        }
        return byRate
            .OrderBy(kv => kv.Key)
            .Select(kv => (kv.Key, kv.Value.Taxable, kv.Value.Cgst, kv.Value.Sgst, kv.Value.Igst))
            .ToList();
    }

    /// <summary>True iff the voucher POSTED its forward tax under the Integrated head — the printed intra/inter
    /// routing, taken from the GL rather than re-derived from the party's (editable) State master, so an edit to the
    /// party after posting can never make the document contradict the books.</summary>
    private static bool PostedInterState(Voucher voucher)
    {
        foreach (var line in voucher.Lines)
            if (line.Gst is { IsReverseCharge: false, TaxHead: GstTaxHead.Integrated }) return true;
        return false;
    }

    // ---------------------------------------------------------------- helpers

    /// <summary>The counterparty-reference label per base type (numbering §8): "Supplier Invoice No." on a Purchase
    /// (the other party's number is the supplier's invoice number), "Reference No." on every other type.</summary>
    private static string ReferenceCaption(VoucherType? type) =>
        type?.BaseType == VoucherBaseType.Purchase ? "Supplier Invoice No." : "Reference No.";

    private static void AccumulateRate(List<(int Bp, decimal Taxable)> acc, int bp, decimal taxable)
    {
        for (int i = 0; i < acc.Count; i++)
            if (acc[i].Bp == bp) { acc[i] = (bp, acc[i].Taxable + taxable); return; }
        acc.Add((bp, taxable));
    }

    /// <summary>The sales value ledger for rate resolution: the posted line ledger carrying a Sales/Purchase GST
    /// block, else the first non-party, non-tax ledger on the voucher.</summary>
    private static Apex.Ledger.Domain.Ledger? ResolveValueLedger(Company company, Voucher voucher, Guid? partyId)
    {
        Apex.Ledger.Domain.Ledger? fallback = null;
        foreach (var l in voucher.Lines)
        {
            var led = company.FindLedger(l.LedgerId);
            if (led is null) continue;
            if (led.SalesPurchaseGst is not null) return led;
            if (led.Id != partyId && led.GstClassification is null && fallback is null) fallback = led;
        }
        return fallback;
    }

    /// <summary>The rate label for the breakup group (e.g. 1800 bp -> "18%"); trims a trailing ".00".</summary>
    private static string RateLabel(int bp)
    {
        decimal pct = bp / 100m;
        var s = pct.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
        return s + "%";
    }

    private static string CompanyDisplayName(Company company) =>
        string.IsNullOrWhiteSpace(company.MailingName) ? company.Name : company.MailingName;

    private static InvoicePartyBlock SellerBlock(Company company) => new()
    {
        Name = ReportPrintProjector.Ascii(CompanyDisplayName(company)),
        AddressLines = SplitAddress(company.Address),
        Gstin = ReportPrintProjector.Ascii(company.Gst?.Gstin ?? string.Empty),
        StateText = StateText(company.Gst?.HomeStateCode),
    };

    /// <summary>
    /// The printed invoice's recipient block. The name is the party's <b>Mailing Name</b> when one was captured
    /// (Tally's "Mailing Name (auto, editable)" convention), else the ledger's own Name; the address lines come
    /// from the WI-4 Mailing Details block through the same <see cref="SplitAddress"/> the seller uses.
    /// <para>Before v45 this hardcoded <c>Array.Empty&lt;string&gt;()</c> with a comment explaining that a party
    /// ledger had no address field — so every invoice this app printed carried a blank recipient address. The
    /// field now exists, and <c>InvoicePdf</c> already renders whatever lines it is given.</para>
    /// </summary>
    private static InvoicePartyBlock BuyerBlock(Company company, Apex.Ledger.Domain.Ledger? party) => new()
    {
        Name = ReportPrintProjector.Ascii(
            string.IsNullOrWhiteSpace(party?.Mailing?.MailingName)
                ? party?.Name ?? string.Empty
                : party!.Mailing!.MailingName!),
        AddressLines = SplitAddress(BuyerAddressText(party)),
        Gstin = ReportPrintProjector.Ascii(party?.PartyGst?.Gstin ?? string.Empty),
        StateText = StateText(party?.PartyGst?.StateCode),
    };

    /// <summary>
    /// The buyer's printable address text: the Mailing Details address, with the PIN code appended as its own
    /// final line when one was captured (the CA's "along with PIN code" — a recipient block without it is not a
    /// complete postal address). Blank when the party has no mailing block, which reproduces the pre-v45 output.
    /// </summary>
    private static string? BuyerAddressText(Apex.Ledger.Domain.Ledger? party)
    {
        var mailing = party?.Mailing;
        if (mailing is null) return null;

        var lines = new List<string>(mailing.AddressLines);
        if (!string.IsNullOrWhiteSpace(mailing.Country)) lines.Add(mailing.Country.Trim());
        if (!string.IsNullOrWhiteSpace(mailing.Pincode)) lines.Add("PIN: " + mailing.Pincode.Trim());
        return lines.Count == 0 ? null : string.Join("\n", lines);
    }

    /// <summary>Place of supply = the buyer's State (drives intra/inter); falls back to the company home State
    /// for a B2C recipient with no recorded State (DP-8).</summary>
    private static string PlaceOfSupply(Company company, Apex.Ledger.Domain.Ledger? party)
    {
        var code = party?.PartyGst?.StateCode;
        if (string.IsNullOrWhiteSpace(code)) code = company.Gst?.HomeStateCode;
        return StateText(code);
    }

    /// <summary>"West Bengal (19)" for a recognised code; blank when unset/unrecognised.</summary>
    private static string StateText(string? code)
    {
        var st = IndianState.FromCode(code);
        return st is null ? string.Empty : ReportPrintProjector.Ascii($"{st.Name} ({st.Code})");
    }

    /// <summary>Splits a free-text address into printable lines (newline- or comma-separated); empty when blank.</summary>
    private static IReadOnlyList<string> SplitAddress(string? address)
    {
        if (string.IsNullOrWhiteSpace(address)) return Array.Empty<string>();
        var parts = address
            .Replace("\r\n", "\n").Replace('\r', '\n')
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return parts.Length > 0
            ? parts.Select(ReportPrintProjector.Ascii).ToArray()
            : Array.Empty<string>();
    }
}
