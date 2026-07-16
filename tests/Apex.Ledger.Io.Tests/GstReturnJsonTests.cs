using System.Text;
using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Services;

namespace Apex.Ledger.Io.Tests;

/// <summary>
/// Phase-9 slice-3 offline-JSON writer gate (RQ-16; R7). The composition CMP-08 / GSTR-4 offline JSON is deterministic
/// (identical bytes on repeat), carries the government envelope (<c>gstin</c> + <c>fp</c>), reports money as integer
/// paisa (ER-10), and is de-branded (no "Tally"). The exact GSTN offline-tool JSON keys are A14-gated (flagged via
/// <c>schemaStatus</c>) — the projection records are correct regardless.
/// </summary>
public sealed class GstReturnJsonTests
{
    private const string GstinMaharashtra = "27AAPFU0939F1ZV";
    private static readonly DateOnly FyStart = new(2025, 4, 1);
    private static readonly DateOnly Q1From = new(2025, 4, 1);
    private static readonly DateOnly Q1To = new(2025, 6, 30);
    private static readonly DateOnly FyEnd = new(2026, 3, 31);

    private static Company BuildComposition()
    {
        var c = CompanyFactory.CreateSeeded("Composition Co", FyStart);
        new GstService(c).EnableGst(new GstConfig
        {
            HomeStateCode = "27", Gstin = GstinMaharashtra, RegistrationType = GstRegistrationType.Composition,
            CompositionSubType = CompositionSubType.Trader, ApplicableFrom = FyStart, Periodicity = GstReturnPeriodicity.Quarterly,
        });
        var sales = new Domain.Ledger(Guid.NewGuid(), "Sales", c.FindGroupByName("Sales Accounts")!.Id, Money.Zero, false)
        {
            SalesPurchaseGst = new StockItemGstDetails { Taxability = GstTaxability.Taxable, RateBasisPoints = 1800 },
        };
        c.AddLedger(sales);
        var party = new Domain.Ledger(Guid.NewGuid(), "Walk-in", c.FindGroupByName("Sundry Debtors")!.Id, Money.Zero, true);
        c.AddLedger(party);
        new LedgerService(c).Post(new Voucher(Guid.NewGuid(), c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Sales).Id, new DateOnly(2025, 4, 5), new[]
        {
            new EntryLine(party.Id, Money.FromRupees(100001m), DrCr.Debit),
            new EntryLine(sales.Id, Money.FromRupees(100001m), DrCr.Credit),
        }, partyId: party.Id));
        return c;
    }

    [Fact]
    public void Cmp08_json_is_deterministic_and_carries_the_envelope()
    {
        var c = BuildComposition();
        var a = GstReturnJson.Cmp08(c, Q1From, Q1To);
        var b = GstReturnJson.Cmp08(c, Q1From, Q1To);
        Assert.Equal(a, b); // deterministic

        var json = Encoding.UTF8.GetString(a);
        Assert.Contains("\"gstin\": \"27AAPFU0939F1ZV\"", json);
        Assert.Contains("\"fp\": \"062025\"", json);                  // MMYYYY, quarter end
        Assert.Contains("\"tbl3i_out_cgst_paisa\": 50001", json);     // CGST 500.01 → paisa
        Assert.Contains("\"tbl3i_out_sgst_paisa\": 50000", json);     // SGST 500.00 → paisa
        Assert.DoesNotContain("Tally", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Gstr4_json_is_deterministic_and_rolls_up_four_quarters()
    {
        var c = BuildComposition();
        var a = GstReturnJson.Gstr4(c, FyStart, FyEnd);
        Assert.Equal(a, GstReturnJson.Gstr4(c, FyStart, FyEnd));

        var json = Encoding.UTF8.GetString(a);
        Assert.Contains("\"gstin\": \"27AAPFU0939F1ZV\"", json);
        Assert.Contains("\"fp\": \"032026\"", json);                  // FY end month
        Assert.Contains("\"tbl5_quarters\"", json);
        Assert.Contains("\"tbl6_annual_comp_tax_paisa\": 100001", json); // 1000.01 → paisa
        Assert.DoesNotContain("Tally", json, StringComparison.OrdinalIgnoreCase);
    }

    // ================================================================ S8a — annual-return offline JSON (RQ-17)

    /// <summary>A regular GST company (intra B2B sale ₹1000 @18% ⇒ CGST 90 + SGST 90; purchase ₹5000 @18% ⇒ ITC 900).</summary>
    private static Company BuildRegularAnnual()
    {
        var c = CompanyFactory.CreateSeeded("Annual Co", FyStart);
        var gst = new GstService(c);
        gst.EnableGst(new GstConfig
        {
            HomeStateCode = "27", Gstin = GstinMaharashtra, RegistrationType = GstRegistrationType.Regular,
            ApplicableFrom = FyStart, Periodicity = GstReturnPeriodicity.Monthly,
        });
        var inv = new InventoryService(c);
        var grp = inv.CreateStockGroup("Goods");
        var nos = inv.CreateSimpleUnit("Nos", "Numbers", unitQuantityCode: "NOS");
        var main = c.MainLocation!.Id;
        var widget = inv.CreateStockItem("Widget", grp.Id, nos.Id);
        widget.Gst = new StockItemGstDetails { HsnSac = "847130", Taxability = GstTaxability.Taxable, RateBasisPoints = 1800 };
        inv.AddOpeningBalance(widget.Id, main, 100m, Money.FromRupees(50m));

        var sales = AddLedger(c, "Sales", "Sales Accounts", false);
        var purchases = AddLedger(c, "Purchases", "Purchase Accounts", true);
        var debtor = AddLedger(c, "Debtor", "Sundry Debtors", true);
        debtor.PartyGst = new PartyGstDetails { RegistrationType = GstRegistrationType.Regular, Gstin = GstinMaharashtra, StateCode = "27" };
        var supplier = AddLedger(c, "Supplier", "Sundry Creditors", false);
        supplier.PartyGst = new PartyGstDetails { RegistrationType = GstRegistrationType.Regular, Gstin = GstinMaharashtra, StateCode = "27" };
        var ledgers = new LedgerService(c);

        var pTax = gst.ComputeInvoiceTax(new[] { new GstService.TaxableLine(Money.FromRupees(5000m), 1800) }, false, GstTaxDirection.Input);
        var pLines = new List<EntryLine> { new(purchases.Id, Money.FromRupees(5000m), DrCr.Debit), new(supplier.Id, new Money(5000m + pTax.TotalTax.Amount), DrCr.Credit) };
        pLines.AddRange(pTax.TaxLines);
        ledgers.Post(new Voucher(Guid.NewGuid(), c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Purchase).Id, new(2025, 4, 3), pLines,
            partyId: supplier.Id, inventoryLines: new[] { new VoucherInventoryLine(widget.Id, main, 100m, Money.FromRupees(50m)) }));

        var sTax = gst.ComputeInvoiceTax(new[] { new GstService.TaxableLine(Money.FromRupees(1000m), 1800) }, false, GstTaxDirection.Output);
        var sLines = new List<EntryLine> { new(debtor.Id, new Money(1000m + sTax.TotalTax.Amount), DrCr.Debit), new(sales.Id, Money.FromRupees(1000m), DrCr.Credit) };
        sLines.AddRange(sTax.TaxLines);
        ledgers.Post(new Voucher(Guid.NewGuid(), c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Sales).Id, new(2025, 4, 5), sLines,
            partyId: debtor.Id, inventoryLines: new[] { new VoucherInventoryLine(widget.Id, main, 10m, Money.FromRupees(100m)) }));
        return c;
    }

    private static Domain.Ledger AddLedger(Company c, string name, string group, bool dr)
    {
        var l = new Domain.Ledger(Guid.NewGuid(), name, c.FindGroupByName(group)!.Id, Money.Zero, dr);
        c.AddLedger(l);
        return l;
    }

    [Fact]
    public void Gstr9_json_is_deterministic_integer_paisa_and_de_branded()
    {
        var c = BuildRegularAnnual();
        var a = GstReturnJson.Gstr9(c, FyStart, FyEnd);
        Assert.Equal(a, GstReturnJson.Gstr9(c, FyStart, FyEnd)); // deterministic (byte-identical on repeat)

        var json = Encoding.UTF8.GetString(a);
        Assert.Contains("\"gstin\": \"27AAPFU0939F1ZV\"", json);
        Assert.Contains("\"fp\": \"032026\"", json);                        // FY-end month, MMYYYY
        Assert.Contains("\"applicable\": true", json);
        Assert.Contains("\"tbl4_total_tax_paisa\": 18000", json);           // CGST 90 + SGST 90 → integer paisa
        Assert.Contains("\"tbl6_itc_availed_paisa\": 90000", json);         // ITC ₹900 → paisa
        Assert.Contains("\"tbl17_hsn\"", json);
        Assert.Contains("\"hsn_sac\": \"847130\"", json);
        Assert.Contains("\"schemaStatus\"", json);
        Assert.DoesNotContain("Tally", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Gstr9a_json_is_deterministic_and_carries_the_envelope()
    {
        var c = BuildComposition();
        var a = GstReturnJson.Gstr9a(c, FyStart, FyEnd);
        Assert.Equal(a, GstReturnJson.Gstr9a(c, FyStart, FyEnd));

        var json = Encoding.UTF8.GetString(a);
        Assert.Contains("\"gstin\": \"27AAPFU0939F1ZV\"", json);
        Assert.Contains("\"fp\": \"032026\"", json);
        Assert.Contains("\"comp_tax_paid_paisa\": 100001", json);           // 1000.01 → paisa (Σ CMP-08)
        Assert.DoesNotContain("Tally", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Gstr9c_json_is_deterministic_and_shows_the_unreconciled_lines()
    {
        var c = BuildRegularAnnual();
        var a = GstReturnJson.Gstr9c(c, FyStart, FyEnd);
        Assert.Equal(a, GstReturnJson.Gstr9c(c, FyStart, FyEnd));

        var json = Encoding.UTF8.GetString(a);
        Assert.Contains("\"gstin\": \"27AAPFU0939F1ZV\"", json);
        Assert.Contains("\"tbl5a_books_turnover_paisa\": 100000", json);    // ₹1000 P&L income → paisa
        Assert.Contains("\"tbl5q_return_turnover_paisa\": 100000", json);   // ₹1000 GSTR-9 turnover
        Assert.Contains("\"tbl5r_unreconciled_turnover_paisa\": 0", json);  // reconciles here (shown, not hidden)
        Assert.Contains("\"tbl12a_books_itc_paisa\": 90000", json);         // Input-ledger closings ₹900
        Assert.DoesNotContain("Tally", json, StringComparison.OrdinalIgnoreCase);
    }
}
