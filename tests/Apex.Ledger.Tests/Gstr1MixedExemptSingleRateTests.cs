using Apex.Ledger.Domain;
using Apex.Ledger.Reports;
using Apex.Ledger.Services;
using Xunit;

namespace Apex.Ledger.Tests;

/// <summary>
/// 🔴 <b>T1-59 — GSTR-1 Table 12 taxed an EXEMPT HSN, and the whole suite was blind to it.</b>
///
/// <para><b>The root cause.</b> <c>Gstr1.AccumulateHsn</c> shortcut per-line rate resolution whenever a voucher
/// posted exactly ONE GST rate group — <c>rateGroups.Count == 1 ? rateGroups[0].Rate : null</c> — reasoning that
/// every line of a single-rate invoice belongs to that one group. That is false on a MIXED invoice: an exempt line
/// posted no tax and contributed no taxable value, yet the collapse bucketed it into the posted group and the
/// group's CGST/SGST was then apportioned across it BY VALUE. The whole-invoice exempt branch could not catch it —
/// it fires only when the invoice posted NO tax at all, so a one-taxable/one-exempt invoice fell straight through.
/// The discriminator that fixes it, <c>GstReportSupport.IsNonTaxableStockLine</c>, is the GOODS mirror of
/// <c>Gstr1.IsNonTaxableServiceLedger</c>, which already existed for ledger legs — the service path
/// (<c>AccumulateServiceHsn</c>) had been fixed for this exact defect and its own comment claimed to mirror
/// <c>AccumulateHsn</c>, which it did not.</para>
///
/// <para><b>🔴 Figures worked by hand, and they are the money.</b> ONE intra-state outward invoice: Widget
/// ₹50,000 @ 18% (HSN 847130) and EXEMPT Fresh Milk ₹20,000 (HSN 040110). Posted tax = 18% × 50,000 = ₹9,000 ⇒
/// CGST ₹4,500 + SGST ₹4,500, a single 1800 rate group.</para>
/// <list type="bullet">
/// <item><b>Correct (what these tests assert).</b> 847130 taxable 50,000 / CGST 4,500 / SGST 4,500 / total value
/// 59,000. 040110 taxable 20,000 / CGST 0 / SGST 0 / total value 20,000. Exempt bucket 20,000.</item>
/// <item><b>What it filed before the fix.</b> Group value 70,000, so Widget (index 0) took
/// <c>round(4500 × 50000 / 70000, 2)</c> = 3,214.29 per head and the Milk (the group's LAST line) absorbed the
/// remainder 4,500 − 3,214.29 = 1,285.71 per head. So: 040110 CGST 1,285.71 / SGST 1,285.71 / total value
/// 22,571.42, 847130 CGST 3,214.29 / SGST 3,214.29, and the exempt bucket EMPTY. Three misstatements on one filed
/// return — ₹2,571.42 of tax declared against an exempt HSN, the taxed HSN short by the same ₹2,571.42, and
/// ₹20,000 of exempt turnover gone.</item>
/// </list>
///
/// <para><b>🔴 Why these tests are the deliverable as much as the fix.</b> A reviewer applied the one-line
/// correction and re-ran both owning suites — Apex.Ledger.Io.Tests 724 passed, Apex.Ledger.Tests 2,860 passed, zero
/// failures. NOT ONE existing test distinguished the collapse from correct per-line resolution: there was no mixed
/// taxable/exempt single-rate item invoice anywhere in the suite. A green gate was never evidence here.</para>
/// </summary>
public class Gstr1MixedExemptSingleRateTests
{
    private static readonly DateOnly FyStart = new(2024, 4, 1);
    private static readonly DateOnly From = new(2024, 4, 1);
    private static readonly DateOnly To = new(2024, 4, 30);
    private static readonly DateOnly D = new(2024, 4, 9);

    private const string GstinMaharashtra = "27AAPFU0939F1ZV";
    private const string TaxedHsn = "847130";   // Widget @ 18%
    private const string ExemptHsn = "040110";  // Fresh Milk — exempt

    /// <summary>🔴 THE DEFECT. The exempt HSN must carry NO tax. Before the fix it filed 1,285.71 per head.</summary>
    [Fact]
    public void An_exempt_stock_line_on_a_single_rate_invoice_is_not_taxed_in_Table_12()
    {
        var milk = Row(Report.BuildGstr1(BuildMixedBook(), From, To), ExemptHsn);

        Assert.Equal(0.00m, milk.Cgst.Amount);
        Assert.Equal(0.00m, milk.Sgst.Amount);
        Assert.Equal(0.00m, milk.Igst.Amount);
        Assert.Equal(0.00m, milk.TotalTax.Amount);

        // Its own taxable value is untouched either way — the defect was the tax, not the value.
        Assert.Equal(20_000.00m, milk.TaxableValue.Amount);
        // Total Value is the brand-new filed cell this branch added, so it propagated the defect: it read 22,571.42.
        Assert.Equal(20_000.00m, milk.TotalValue.Amount);
    }

    /// <summary>The other half of the same misstatement: the TAXED HSN was short by exactly what the exempt one
    /// wrongly received. A fix that zeroed the exempt row without returning the tax to the taxed row would leave the
    /// return understated, so this is asserted separately rather than folded into the test above.</summary>
    [Fact]
    public void The_taxed_HSN_keeps_the_whole_posted_tax_of_its_rate_group()
    {
        var widget = Row(Report.BuildGstr1(BuildMixedBook(), From, To), TaxedHsn);

        Assert.Equal(4_500.00m, widget.Cgst.Amount);   // was 3,214.29
        Assert.Equal(4_500.00m, widget.Sgst.Amount);   // was 3,214.29
        Assert.Equal(50_000.00m, widget.TaxableValue.Amount);
        Assert.Equal(59_000.00m, widget.TotalValue.Amount);
    }

    /// <summary>The third misstatement: ₹20,000 of exempt turnover disappeared from the exempt bucket, because the
    /// line was consumed by the rate group instead of reaching it.</summary>
    [Fact]
    public void The_exempt_turnover_reaches_the_exempt_bucket()
    {
        var r = Report.BuildGstr1(BuildMixedBook(), From, To);
        Assert.Equal(20_000.00m, r.ExemptNilNonGstValue.Amount);
    }

    /// <summary>Σ of the filed tax cells must equal the tax actually POSTED to the ledgers. Under the collapse this
    /// still held — the total was right while both rows were wrong — which is precisely why a footing check could
    /// not find the defect and the per-row assertions above are the ones that matter. Pinned so a future fix cannot
    /// buy a correct exempt row by inventing or losing tax.</summary>
    [Fact]
    public void Every_posted_rupee_still_foots_and_nothing_is_invented()
    {
        var company = BuildMixedBook();
        var r = Report.BuildGstr1(company, From, To);

        var posted = company.Vouchers
            .SelectMany(v => v.Lines)
            .Where(l => l.Gst is { } g && g.TaxHead != GstTaxHead.Cess)
            .Sum(l => l.Amount.Amount);

        Assert.Equal(9_000.00m, posted);
        Assert.Equal(posted, r.HsnSummary.Sum(h => h.Cgst.Amount + h.Sgst.Amount + h.Igst.Amount));

        // Both HSNs are still filed — the exempt line is zero-taxed, never dropped. Losing the row would hide the
        // supply from Table 12 altogether, which is the OTHER defect the service path once had.
        Assert.Equal(2, r.HsnSummary.Count);
        Assert.Equal(70_000.00m, r.HsnSummary.Sum(h => h.TaxableValue.Amount));
    }

    /// <summary>
    /// 🔴 <b>ER-5 — silence is not an exemption, and the fix must not quietly widen into one.</b> An item whose
    /// taxability cannot be resolved at all (no GST block anywhere in the chain) reports
    /// <c>IsTaxable == false</c> from the resolver exactly as a declared-exempt item does. Treating the two alike
    /// would newly declare an unresolved line EXEMPT on a filed return — a different misstatement in the opposite
    /// direction. <c>IsNonTaxableStockLine</c> therefore returns false for the unresolved sentinel, so an
    /// unresolved line keeps the collapse's shipped behaviour: here it stays in the one posted group, and the 18%
    /// tax is split across the two lines by value. This test pins that it is NOT in the exempt bucket.
    /// </summary>
    [Fact]
    public void An_UNRESOLVED_line_is_not_newly_declared_exempt()
    {
        var r = Report.BuildGstr1(BuildUnresolvedBook(), From, To);

        // Nothing landed in the exempt bucket: the unresolved line was not reclassified as an exempt supply.
        Assert.Equal(0.00m, r.ExemptNilNonGstValue.Amount);
        // And the posted tax is still fully filed, not dropped.
        Assert.Equal(9_000.00m, r.HsnSummary.Sum(h => h.Cgst.Amount + h.Sgst.Amount + h.Igst.Amount));
    }

    // ================================================================ fixture

    private static Gstr1HsnRow Row(Gstr1 r, string hsn) => r.HsnSummary.Single(h => h.HsnSac == hsn);

    /// <summary>One intra-state outward invoice: Widget ₹50,000 @ 18% (HSN 847130) + EXEMPT Fresh Milk ₹20,000
    /// (HSN 040110). The milk is ordered LAST so that, under the collapse, it is the group's remainder-absorbing
    /// line — which is what produced the 1,285.71 figure rather than a rounded share.</summary>
    private static Company BuildMixedBook() => Build(exemptSecondItem: true);

    /// <summary>The ER-5 control: the second item declares NO GST block at all and no ledger or company default
    /// supplies one, so its resolution is the unresolved sentinel rather than a declared exemption.</summary>
    private static Company BuildUnresolvedBook() => Build(exemptSecondItem: false);

    private static Company Build(bool exemptSecondItem)
    {
        var c = CompanyFactory.CreateSeeded("Mixed Supply Co", FyStart);
        var gst = new GstService(c);
        gst.EnableGst(new GstConfig
        {
            HomeStateCode = "27",
            Gstin = GstinMaharashtra,
            RegistrationType = GstRegistrationType.Regular,
            ApplicableFrom = FyStart,
            Periodicity = GstReturnPeriodicity.Monthly,
        });

        var inv = new InventoryService(c);
        var ledgers = new LedgerService(c);
        var grp = inv.CreateStockGroup("Goods");
        var nos = inv.CreateSimpleUnit("Nos", "Numbers", unitQuantityCode: "NOS");
        var main = c.MainLocation!.Id;

        var widget = inv.CreateStockItem("Widget", grp.Id, nos.Id);
        widget.Gst = new StockItemGstDetails
        {
            HsnSac = TaxedHsn, Taxability = GstTaxability.Taxable, RateBasisPoints = 1800,
        };

        var milk = inv.CreateStockItem("Fresh Milk", grp.Id, nos.Id);
        // Exempt ⇒ ResolveRate reports IsTaxable false with a resolved taxability. Null ⇒ the ER-5 unresolved
        // sentinel, which must NOT be read as an exemption.
        milk.Gst = exemptSecondItem
            ? new StockItemGstDetails { HsnSac = ExemptHsn, Taxability = GstTaxability.Exempt }
            : null;

        inv.AddOpeningBalance(widget.Id, main, 1000m, Money.FromRupees(20m));
        inv.AddOpeningBalance(milk.Id, main, 1000m, Money.FromRupees(10m));

        var sales = Add(c, "Sales", "Sales Accounts", false);
        var debtor = Add(c, "Debtor", "Sundry Debtors", true);
        debtor.PartyGst = new PartyGstDetails
        {
            RegistrationType = GstRegistrationType.Regular, Gstin = GstinMaharashtra, StateCode = "27",
        };

        var salesType = c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Sales).Id;

        // 🔴 The POSTING excludes the non-taxable line from the tax base — exactly what
        // VoucherEntryViewModel.ComputeItemInvoiceGst does ("if (!res.IsTaxable) continue"). So only the Widget's
        // ₹50,000 is a TaxableLine, and the invoice posts ONE 1800 rate group. That is the shape the report then
        // has to read correctly.
        var tax = gst.ComputeInvoiceTax(
            new[] { new GstService.TaxableLine(Money.FromRupees(50_000m), 1800) },
            interState: false, GstTaxDirection.Output);

        Assert.Equal(9_000.00m, tax.TotalTax.Amount);  // the fixture's own premise, held at build time

        var lines = new List<EntryLine>
        {
            new(debtor.Id, Money.FromRupees(70_000m + tax.TotalTax.Amount), DrCr.Debit),
            new(sales.Id, Money.FromRupees(70_000m), DrCr.Credit),
        };
        lines.AddRange(tax.TaxLines);

        ledgers.Post(new Voucher(Guid.NewGuid(), salesType, D, lines, partyId: debtor.Id, inventoryLines: new[]
        {
            new VoucherInventoryLine(widget.Id, main, 2500m, Money.FromRupees(20m)),  // 2500 × 20 = 50,000
            new VoucherInventoryLine(milk.Id, main, 2000m, Money.FromRupees(10m)),    // 2000 × 10 = 20,000
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
