using System;
using System.Collections.Generic;
using System.Linq;
using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Io;
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
    /// a Sales voucher carrying item-invoice stock lines. Purchase item-invoices and every other voucher print
    /// as the plain Dr/Cr voucher (RQ-10).
    /// </summary>
    public static bool IsTaxInvoice(Company company, Voucher voucher)
    {
        ArgumentNullException.ThrowIfNull(company);
        ArgumentNullException.ThrowIfNull(voucher);
        if (!voucher.HasInventoryLines) return false;
        var type = company.FindVoucherType(voucher.TypeId);
        return type?.BaseType == VoucherBaseType.Sales;
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

    // ---------------------------------------------------------------- helpers

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
