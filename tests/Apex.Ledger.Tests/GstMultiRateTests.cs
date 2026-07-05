using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Reports;
using Apex.Ledger.Services;
using Xunit;

namespace Apex.Ledger.Tests;

/// <summary>
/// Phase 4 — multi-rate invoice fidelity (adversarial-review fix). A single invoice whose lines carry
/// <b>different</b> GST rates must keep per-rate identity end-to-end: the engine posts <b>one tax line per
/// (tax head, GST rate) group</b> (not one blended head line), each carrying its own correct
/// <see cref="GstLineTax.RateBasisPoints"/> and that rate group's taxable subtotal, so GSTR-1 rate/HSN
/// summaries, B2C consolidation and Tax Analysis attribute tax to the correct rate. Tax stays additive
/// (CGST+SGST==IGST==round(subtotal×rate) per rate group) and the item-invoice pairing invariant holds
/// (stock leg = Σ taxable value, tax excluded). Single-rate invoices are unchanged (one line per head).
/// Repro: Widget ₹1000@18% + Gadget ₹500@5% on one invoice ⇒ 18%: taxable 1000 / tax 180 and 5%: taxable
/// 500 / tax 25 (NOT a bogus blended 0% row of taxable 1500 / tax 205). All figures worked by hand.
/// </summary>
public class GstMultiRateTests
{
    private static readonly DateOnly FyStart = new(2024, 4, 1);
    private static readonly DateOnly From = new(2024, 4, 1);
    private static readonly DateOnly To = new(2024, 4, 30);
    private static readonly DateOnly D = new(2024, 4, 6);

    private const string GstinMaharashtra = "27AAPFU0939F1ZV";
    private const string GstinGujarat = "24AAACC1206D1ZM";

    // ---- engine: one tax line per (head, rate) group ----

    [Fact]
    public void Multi_rate_intra_invoice_posts_one_tax_line_per_head_per_rate()
    {
        var c = NewGstCompany();
        var gst = new GstService(c);

        // Widget ₹1000 @ 18% + Gadget ₹500 @ 5%, intra.
        var tax = gst.ComputeInvoiceTax(new[]
        {
            new GstService.TaxableLine(Money.FromRupees(1000m), 1800),
            new GstService.TaxableLine(Money.FromRupees(500m), 500),
        }, interState: false, GstTaxDirection.Output);

        // FOUR tax lines: CGST@9% + SGST@9% (for the 18% group) and CGST@2.5% + SGST@2.5% (for the 5% group).
        Assert.Equal(4, tax.TaxLines.Count);

        // The 18% group posts CGST 90 + SGST 90 at half-rate 900; the 5% group posts CGST 12.50 + SGST 12.50
        // at half-rate 250. Each tax line carries its OWN rate group's taxable subtotal (1000 or 500).
        var cgst18 = SingleGst(tax, GstTaxHead.Central, 900);
        Assert.Equal(Money.FromRupees(90m), cgst18.Amount);
        Assert.Equal(Money.FromRupees(1000m), cgst18.Gst!.TaxableValue);
        var sgst18 = SingleGst(tax, GstTaxHead.State, 900);
        Assert.Equal(Money.FromRupees(90m), sgst18.Amount);

        var cgst5 = SingleGst(tax, GstTaxHead.Central, 250);
        Assert.Equal(Money.FromRupees(12.50m), cgst5.Amount);
        Assert.Equal(Money.FromRupees(500m), cgst5.Gst!.TaxableValue);
        var sgst5 = SingleGst(tax, GstTaxHead.State, 250);
        Assert.Equal(Money.FromRupees(12.50m), sgst5.Amount);

        // Head totals still foot: CGST 90 + 12.50 = 102.50; SGST 102.50; total 205.
        Assert.Equal(Money.FromRupees(102.50m), tax.TotalCgst);
        Assert.Equal(Money.FromRupees(102.50m), tax.TotalSgst);
        Assert.Equal(Money.FromRupees(205m), tax.TotalTax);
    }

    [Fact]
    public void Multi_rate_inter_invoice_posts_one_igst_line_per_rate()
    {
        var c = NewGstCompany();
        var gst = new GstService(c);

        // Widget ₹1000 @ 18% + Gadget ₹500 @ 5%, inter (IGST).
        var tax = gst.ComputeInvoiceTax(new[]
        {
            new GstService.TaxableLine(Money.FromRupees(1000m), 1800),
            new GstService.TaxableLine(Money.FromRupees(500m), 500),
        }, interState: true, GstTaxDirection.Output);

        // TWO IGST lines, one per rate, each at its FULL integrated rate carrying its own taxable subtotal.
        Assert.Equal(2, tax.TaxLines.Count);
        var igst18 = SingleGst(tax, GstTaxHead.Integrated, 1800);
        Assert.Equal(Money.FromRupees(180m), igst18.Amount);
        Assert.Equal(Money.FromRupees(1000m), igst18.Gst!.TaxableValue);
        var igst5 = SingleGst(tax, GstTaxHead.Integrated, 500);
        Assert.Equal(Money.FromRupees(25m), igst5.Amount);
        Assert.Equal(Money.FromRupees(500m), igst5.Gst!.TaxableValue);

        Assert.Equal(Money.FromRupees(205m), tax.TotalIgst);
        Assert.Equal(Money.FromRupees(205m), tax.TotalTax);
    }

    [Fact]
    public void Multi_rate_intra_cgst_plus_sgst_equals_igst_per_rate_group()
    {
        var c = NewGstCompany();
        var gst = new GstService(c);
        var lines = new[]
        {
            new GstService.TaxableLine(Money.FromRupees(1000m), 1800),
            new GstService.TaxableLine(Money.FromRupees(500m), 500),
        };
        var intra = gst.ComputeInvoiceTax(lines, interState: false, GstTaxDirection.Output);
        var inter = gst.ComputeInvoiceTax(lines, interState: true, GstTaxDirection.Output);

        // Per rate group: CGST + SGST == IGST == round(subtotal × rate).
        // 18%: 90 + 90 == 180; 5%: 12.50 + 12.50 == 25.
        Assert.Equal(SingleGst(inter, GstTaxHead.Integrated, 1800).Amount.Amount,
            SingleGst(intra, GstTaxHead.Central, 900).Amount.Amount + SingleGst(intra, GstTaxHead.State, 900).Amount.Amount);
        Assert.Equal(SingleGst(inter, GstTaxHead.Integrated, 500).Amount.Amount,
            SingleGst(intra, GstTaxHead.Central, 250).Amount.Amount + SingleGst(intra, GstTaxHead.State, 250).Amount.Amount);

        // And the whole-invoice CGST+SGST == whole-invoice IGST.
        Assert.Equal(inter.TotalIgst.Amount, intra.TotalCgst.Amount + intra.TotalSgst.Amount);
    }

    [Fact]
    public void Single_rate_invoice_still_posts_one_line_per_head_unchanged()
    {
        var c = NewGstCompany();
        var gst = new GstService(c);

        var intra = gst.ComputeInvoiceTax(new[]
        {
            new GstService.TaxableLine(Money.FromRupees(600m), 1800),
            new GstService.TaxableLine(Money.FromRupees(400m), 1800),
        }, interState: false, GstTaxDirection.Output);

        // Same rate on both lines ⇒ ONE CGST + ONE SGST line (unchanged shape), taxable = the invoice total 1000.
        Assert.Equal(2, intra.TaxLines.Count);
        Assert.Equal(Money.FromRupees(90m), SingleGst(intra, GstTaxHead.Central, 900).Amount);
        Assert.Equal(Money.FromRupees(1000m), SingleGst(intra, GstTaxHead.Central, 900).Gst!.TaxableValue);
        Assert.Equal(Money.FromRupees(90m), SingleGst(intra, GstTaxHead.State, 900).Amount);
    }

    // ---- GSTR-1 rate-wise + HSN + B2C attribute per rate correctly ----

    [Fact]
    public void Gstr1_multi_rate_b2b_invoice_splits_rate_and_hsn_summaries_by_rate()
    {
        var f = BuildMultiRate(interState: false, b2c: false);
        var r = Gstr1.Build(f, From, To);

        // Rate-wise summary: 18% row (taxable 1000, tax 180) and 5% row (taxable 500, tax 25) — NOT a 0% blend.
        var rate18 = r.RateSummary.Single(x => x.RateBasisPoints == 1800);
        Assert.Equal(Money.FromRupees(1000m), rate18.TaxableValue);
        Assert.Equal(Money.FromRupees(180m), rate18.TotalTax);
        var rate5 = r.RateSummary.Single(x => x.RateBasisPoints == 500);
        Assert.Equal(Money.FromRupees(500m), rate5.TaxableValue);
        Assert.Equal(Money.FromRupees(25m), rate5.TotalTax);
        Assert.DoesNotContain(r.RateSummary, x => x.RateBasisPoints == 0);

        // HSN summary: Widget 1000 / tax 180 at its own rate; Gadget 500 / tax 25 — NOT the value-share blend
        // (which would have been Widget 136.66 / Gadget 68.34).
        var widget = r.HsnSummary.Single(h => h.HsnSac == "847130");
        Assert.Equal(Money.FromRupees(1000m), widget.TaxableValue);
        Assert.Equal(Money.FromRupees(180m), widget.TotalTax);
        var gadget = r.HsnSummary.Single(h => h.HsnSac == "852990");
        Assert.Equal(Money.FromRupees(500m), gadget.TaxableValue);
        Assert.Equal(Money.FromRupees(25m), gadget.TotalTax);

        // The single B2B invoice carries the whole-invoice taxable (1500) and both heads' tax (102.50 each).
        var b2b = Assert.Single(r.B2B);
        Assert.Equal(Money.FromRupees(1500m), b2b.TaxableValue);
        Assert.Equal(Money.FromRupees(102.50m), b2b.Cgst);
        Assert.Equal(Money.FromRupees(102.50m), b2b.Sgst);
    }

    [Fact]
    public void Gstr1_multi_rate_inter_invoice_splits_igst_rate_rows()
    {
        var f = BuildMultiRate(interState: true, b2c: false);
        var r = Gstr1.Build(f, From, To);

        var rate18 = r.RateSummary.Single(x => x.RateBasisPoints == 1800);
        Assert.Equal(Money.FromRupees(1000m), rate18.TaxableValue);
        Assert.Equal(Money.FromRupees(180m), rate18.TotalTax);
        var rate5 = r.RateSummary.Single(x => x.RateBasisPoints == 500);
        Assert.Equal(Money.FromRupees(500m), rate5.TaxableValue);
        Assert.Equal(Money.FromRupees(25m), rate5.TotalTax);

        var b2b = Assert.Single(r.B2B);
        Assert.Equal(Money.FromRupees(1500m), b2b.TaxableValue);
        Assert.Equal(Money.FromRupees(205m), b2b.Igst);
    }

    [Fact]
    public void Gstr1_multi_rate_b2c_consolidates_per_rate_not_one_zero_row()
    {
        var f = BuildMultiRate(interState: false, b2c: true);
        var r = Gstr1.Build(f, From, To);

        // Two consolidated B2C rows, one per rate — NOT a single bogus 0% row.
        Assert.Equal(2, r.B2C.Count);
        var b2c18 = r.B2C.Single(x => x.RateBasisPoints == 1800);
        Assert.Equal(Money.FromRupees(1000m), b2c18.TaxableValue);
        Assert.Equal(Money.FromRupees(90m), b2c18.Cgst);
        Assert.Equal(Money.FromRupees(90m), b2c18.Sgst);
        var b2c5 = r.B2C.Single(x => x.RateBasisPoints == 500);
        Assert.Equal(Money.FromRupees(500m), b2c5.TaxableValue);
        Assert.Equal(Money.FromRupees(12.50m), b2c5.Cgst);
        Assert.Equal(Money.FromRupees(12.50m), b2c5.Sgst);
        Assert.DoesNotContain(r.B2C, x => x.RateBasisPoints == 0);
    }

    // ---- TaxAnalysis splits outward rate rows by rate ----

    [Fact]
    public void TaxAnalysis_multi_rate_intra_splits_rows_by_rate()
    {
        var f = BuildMultiRate(interState: false, b2c: false);
        var ta = TaxAnalysis.Build(f, From, To);

        // 18% intra: CGST @900 taxable 1000 / tax 90; 5% intra: CGST @250 taxable 500 / tax 12.50.
        var c18 = ta.Outward.RateRows.Single(x => x.RateBasisPoints == 900 && x.Head == GstTaxHead.Central);
        Assert.Equal(Money.FromRupees(1000m), c18.TaxableValue);
        Assert.Equal(Money.FromRupees(90m), c18.Tax);
        var c5 = ta.Outward.RateRows.Single(x => x.RateBasisPoints == 250 && x.Head == GstTaxHead.Central);
        Assert.Equal(Money.FromRupees(500m), c5.TaxableValue);
        Assert.Equal(Money.FromRupees(12.50m), c5.Tax);
        Assert.DoesNotContain(ta.Outward.RateRows, x => x.RateBasisPoints == 0);

        // Head totals foot & reconcile.
        Assert.Equal(Money.FromRupees(102.50m), ta.Outward.TotalCgst);
        Assert.Equal(Money.FromRupees(102.50m), ta.Outward.TotalSgst);
    }

    // ---- reconciliation: Σ report output == Σ Output ledger, paisa-exact ----

    [Fact]
    public void Multi_rate_head_totals_reconcile_to_the_output_tax_ledgers()
    {
        var f = BuildMultiRate(interState: false, b2c: false);
        var gst = new GstService(f);
        var outCgst = gst.FindTaxLedger(GstTaxHead.Central, GstTaxDirection.Output)!;
        var outSgst = gst.FindTaxLedger(GstTaxHead.State, GstTaxDirection.Output)!;

        var r = Gstr1.Build(f, From, To);
        Assert.Equal(-LedgerBalances.SignedClosing(f, outCgst, To), r.TotalCgst.Amount);
        Assert.Equal(-LedgerBalances.SignedClosing(f, outSgst, To), r.TotalSgst.Amount);
        Assert.Equal(-102.50m, LedgerBalances.SignedClosing(f, outCgst, To));
        Assert.Equal(-102.50m, LedgerBalances.SignedClosing(f, outSgst, To));

        // GSTR-3B taxable outward value is the whole-invoice taxable (1500), not the max rate group (1000).
        var g3 = Gstr3b.Build(f, From, To);
        Assert.Equal(Money.FromRupees(1500m), g3.TaxableOutwardValue);
    }

    // ---- helpers ----

    private static EntryLine SingleGst(GstService.InvoiceTax tax, GstTaxHead head, int rateBp) =>
        tax.TaxLines.Single(l => l.Gst is { } g && g.TaxHead == head && g.RateBasisPoints == rateBp);

    private static Company NewGstCompany()
    {
        var c = CompanyFactory.CreateSeeded("Multi-Rate GST Co", FyStart);
        new GstService(c).EnableGst(new GstConfig
        {
            HomeStateCode = "27",
            Gstin = GstinMaharashtra,
            RegistrationType = GstRegistrationType.Regular,
            ApplicableFrom = FyStart,
            Periodicity = GstReturnPeriodicity.Monthly,
        });
        return c;
    }

    /// <summary>
    /// A company with a single posted outward invoice of two lines at different rates: Widget ₹1000 @ 18% and
    /// Gadget ₹500 @ 5%. <paramref name="interState"/> ⇒ IGST vs CGST/SGST; <paramref name="b2c"/> ⇒ the party
    /// is an unregistered consumer (no GSTIN) so the invoice lands in the B2C section.
    /// </summary>
    private static Company BuildMultiRate(bool interState, bool b2c)
    {
        var c = NewGstCompany();
        var gst = new GstService(c);
        var ledgers = new LedgerService(c);
        var inv = new InventoryService(c);
        var grp = inv.CreateStockGroup("Goods");
        var nos = inv.CreateSimpleUnit("Nos", "Numbers", unitQuantityCode: "NOS");
        var main = c.MainLocation!.Id;

        var widget = inv.CreateStockItem("Widget", grp.Id, nos.Id);
        widget.Gst = new StockItemGstDetails { HsnSac = "847130", Taxability = GstTaxability.Taxable, RateBasisPoints = 1800 };
        var gadget = inv.CreateStockItem("Gadget", grp.Id, nos.Id);
        gadget.Gst = new StockItemGstDetails { HsnSac = "852990", Taxability = GstTaxability.Taxable, RateBasisPoints = 500 };
        inv.AddOpeningBalance(widget.Id, main, 100m, Money.FromRupees(50m));
        inv.AddOpeningBalance(gadget.Id, main, 100m, Money.FromRupees(20m));

        var sales = Add(c, "Sales", "Sales Accounts", false);
        var debtor = Add(c, "Debtor", "Sundry Debtors", true);
        debtor.PartyGst = b2c
            ? new PartyGstDetails { RegistrationType = GstRegistrationType.Consumer, StateCode = interState ? "24" : "27" }
            : new PartyGstDetails
            {
                RegistrationType = GstRegistrationType.Regular,
                Gstin = interState ? GstinGujarat : GstinMaharashtra,
                StateCode = interState ? "24" : "27",
            };

        var salesType = c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Sales).Id;
        var tax = gst.ComputeInvoiceTax(new[]
        {
            new GstService.TaxableLine(Money.FromRupees(1000m), 1800),
            new GstService.TaxableLine(Money.FromRupees(500m), 500),
        }, interState, GstTaxDirection.Output);

        // Party = taxable 1500 + tax 205 = 1705.
        var lines = new List<EntryLine>
        {
            new(debtor.Id, Money.FromRupees(1500m + tax.TotalTax.Amount), DrCr.Debit),
            new(sales.Id, Money.FromRupees(1500m), DrCr.Credit),
        };
        lines.AddRange(tax.TaxLines);
        ledgers.Post(new Voucher(Guid.NewGuid(), salesType, D, lines, partyId: debtor.Id, inventoryLines: new[]
        {
            new VoucherInventoryLine(widget.Id, main, 10m, Money.FromRupees(100m)),   // 10 × 100 = 1000
            new VoucherInventoryLine(gadget.Id, main, 25m, Money.FromRupees(20m)),    // 25 × 20 = 500
        }));

        return c;
    }

    private static Domain.Ledger Add(Company c, string name, string groupName, bool openingIsDebit)
    {
        var l = new Domain.Ledger(Guid.NewGuid(), name, c.FindGroupByName(groupName)!.Id, Money.Zero, openingIsDebit);
        c.AddLedger(l);
        return l;
    }
}
