using System.Text;
using System.Text.Json;
using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Services;

namespace Apex.Ledger.Io.Tests;

/// <summary>
/// W2-06 slice (a) — the <b>GSTR-1</b> and <b>GSTR-3B</b> offline-JSON emitters (census row 6.10, T1-11). Until this
/// slice <c>GstReturnJson</c> exposed five writers (CMP-08, GSTR-4, 9, 9A, 9C) and <b>no GSTR-1 or 3B emitter at
/// all</b> — the two artefacts a regular dealer actually files every period.
/// <para>
/// Every expected figure below is <b>derived by hand from the fixture</b>, never read off the code:
/// one intra-state purchase of ₹5,000 @18% (CGST 9% = ₹450.00, SGST 9% = ₹450.00) and one intra-state B2B sale of
/// ₹1,000 @18% (CGST ₹90.00, SGST ₹90.00) of 10 Nos of HSN 847130. In integer paisa (ER-10) that is
/// outward 100000 / 9000 / 9000 and ITC 45000 / 45000, so net CGST = 9000 − 45000 = <b>−36000</b>.
/// </para>
/// <para>
/// <b>R7 / RULING 9 — READ THIS BEFORE TREATING THE KEY NAMES AS VERIFIED.</b> The GSTN GSTR-1 / GSTR-3B upload
/// payload schema is published only behind the authenticated GST developer portal
/// (<c>developer.gst.gov.in/apiportal/taxpayer/returns</c> → "GSTR1 — Save GSTR1 data" → Request Payload); no
/// unauthenticated CBIC/GSTN source states the key names or their types. These emitters therefore ship as a
/// <b>documented divergence, labelled as ours</b>: they follow the SAME house convention as the five writers
/// already in this class (integer-paisa money keys, <c>gstin</c>/<c>fp</c>/<c>ret_period</c> envelope) and carry
/// the same <c>schemaStatus</c> flag. They are <b>not</b> claimed to be portal-accepted and must not join the
/// compared set. The engine <i>figures</i> are correct regardless — that is what these tests lock.
/// </para>
/// </summary>
public sealed class Gstr1Gstr3bJsonTests
{
    private const string GstinMaharashtra = "27AAPFU0939F1ZV";
    private static readonly DateOnly FyStart = new(2025, 4, 1);
    private static readonly DateOnly AprFrom = new(2025, 4, 1);
    private static readonly DateOnly AprTo = new(2025, 4, 30);

    /// <summary>A regular GST company: intra-state purchase ₹5,000 @18% (ITC 450 + 450) and an intra-state B2B sale
    /// ₹1,000 @18% (CGST 90 + SGST 90) of 10 Nos of HSN 847130 to a registered debtor.</summary>
    private static Company BuildRegular()
    {
        var c = CompanyFactory.CreateSeeded("Regular Co", FyStart);
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
        var pLines = new List<EntryLine>
        {
            new(purchases.Id, Money.FromRupees(5000m), DrCr.Debit),
            new(supplier.Id, new Money(5000m + pTax.TotalTax.Amount), DrCr.Credit),
        };
        pLines.AddRange(pTax.TaxLines);
        ledgers.Post(new Voucher(Guid.NewGuid(), c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Purchase).Id, new(2025, 4, 3), pLines,
            partyId: supplier.Id, inventoryLines: new[] { new VoucherInventoryLine(widget.Id, main, 100m, Money.FromRupees(50m)) }));

        var sTax = gst.ComputeInvoiceTax(new[] { new GstService.TaxableLine(Money.FromRupees(1000m), 1800) }, false, GstTaxDirection.Output);
        var sLines = new List<EntryLine>
        {
            new(debtor.Id, new Money(1000m + sTax.TotalTax.Amount), DrCr.Debit),
            new(sales.Id, Money.FromRupees(1000m), DrCr.Credit),
        };
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

    // ===================================================================================== GSTR-1

    [Fact]
    public void Gstr1_json_is_deterministic_and_carries_the_envelope()
    {
        var c = BuildRegular();
        var a = GstReturnJson.Gstr1(c, AprFrom, AprTo);
        Assert.Equal(a, GstReturnJson.Gstr1(c, AprFrom, AprTo)); // byte-identical on repeat (no clock, no RNG)

        var json = Encoding.UTF8.GetString(a);
        Assert.Contains("\"gstin\": \"27AAPFU0939F1ZV\"", json);
        Assert.Contains("\"fp\": \"042025\"", json);                       // MMYYYY, period end
        Assert.Contains("\"ret_period\": \"2025-04-01/2025-04-30\"", json);
        Assert.Contains("\"schemaStatus\"", json);
        Assert.DoesNotContain("Tally", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Gstr1_json_emits_the_b2b_invoice_in_integer_paisa()
    {
        var c = BuildRegular();
        using var doc = JsonDocument.Parse(GstReturnJson.Gstr1(c, AprFrom, AprTo));
        var b2b = doc.RootElement.GetProperty("b2b");

        Assert.Equal(1, b2b.GetArrayLength());
        var row = b2b[0];
        Assert.Equal("27AAPFU0939F1ZV", row.GetProperty("ctin").GetString());
        Assert.Equal("27", row.GetProperty("pos").GetString());
        Assert.Equal(100000L, row.GetProperty("txval_paisa").GetInt64());   // ₹1,000.00
        Assert.Equal(9000L, row.GetProperty("camt_paisa").GetInt64());      // CGST 9% = ₹90.00
        Assert.Equal(9000L, row.GetProperty("samt_paisa").GetInt64());      // SGST 9% = ₹90.00
        Assert.Equal(0L, row.GetProperty("iamt_paisa").GetInt64());         // intra-state ⇒ no IGST
    }

    [Fact]
    public void Gstr1_json_totals_and_hsn_summary_foot_to_the_engine()
    {
        var c = BuildRegular();
        using var doc = JsonDocument.Parse(GstReturnJson.Gstr1(c, AprFrom, AprTo));
        var root = doc.RootElement;

        Assert.Equal(9000L, root.GetProperty("total_cgst_paisa").GetInt64());
        Assert.Equal(9000L, root.GetProperty("total_sgst_paisa").GetInt64());
        Assert.Equal(0L, root.GetProperty("total_igst_paisa").GetInt64());
        Assert.Equal(0, root.GetProperty("b2cs").GetArrayLength());        // no consumer sale in the fixture

        var hsn = root.GetProperty("hsn");
        Assert.Equal(1, hsn.GetArrayLength());
        Assert.Equal("847130", hsn[0].GetProperty("hsn_sac").GetString());
        Assert.Equal("NOS", hsn[0].GetProperty("uqc").GetString());
        Assert.Equal(100000L, hsn[0].GetProperty("txval_paisa").GetInt64());
        Assert.Equal(9000L, hsn[0].GetProperty("camt_paisa").GetInt64());
        Assert.Equal(9000L, hsn[0].GetProperty("samt_paisa").GetInt64());
    }

    // ===================================================================================== GSTR-3B

    [Fact]
    public void Gstr3b_json_is_deterministic_and_carries_the_envelope()
    {
        var c = BuildRegular();
        var a = GstReturnJson.Gstr3b(c, AprFrom, AprTo);
        Assert.Equal(a, GstReturnJson.Gstr3b(c, AprFrom, AprTo));

        var json = Encoding.UTF8.GetString(a);
        Assert.Contains("\"gstin\": \"27AAPFU0939F1ZV\"", json);
        Assert.Contains("\"fp\": \"042025\"", json);
        Assert.Contains("\"schemaStatus\"", json);
        Assert.DoesNotContain("Tally", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Gstr3b_json_emits_3_1a_outward_and_4a5_itc_in_integer_paisa()
    {
        var c = BuildRegular();
        using var doc = JsonDocument.Parse(GstReturnJson.Gstr3b(c, AprFrom, AprTo));
        var root = doc.RootElement;

        // Table 3.1(a) — outward taxable supplies (other than zero-rated/nil/exempt).
        Assert.Equal(100000L, root.GetProperty("tbl3_1a_txval_paisa").GetInt64());  // ₹1,000.00
        Assert.Equal(9000L, root.GetProperty("tbl3_1a_camt_paisa").GetInt64());     // ₹90.00
        Assert.Equal(9000L, root.GetProperty("tbl3_1a_samt_paisa").GetInt64());     // ₹90.00
        Assert.Equal(0L, root.GetProperty("tbl3_1a_iamt_paisa").GetInt64());

        // Table 4(A)(5) — all other ITC (the purchase's ₹450 + ₹450).
        Assert.Equal(45000L, root.GetProperty("tbl4a5_camt_paisa").GetInt64());
        Assert.Equal(45000L, root.GetProperty("tbl4a5_samt_paisa").GetInt64());
        Assert.Equal(0L, root.GetProperty("tbl4a5_iamt_paisa").GetInt64());
    }

    [Fact]
    public void Gstr3b_json_net_payable_is_outward_minus_itc_and_may_be_negative()
    {
        var c = BuildRegular();
        using var doc = JsonDocument.Parse(GstReturnJson.Gstr3b(c, AprFrom, AprTo));
        var root = doc.RootElement;

        // DP-9: display-only arithmetic. 9000 − 45000 = −36000 paisa; a negative head is a carried-forward
        // credit and is emitted VERBATIM (never floored to zero) — the same rule GSTR-9C's unreconciled lines follow.
        Assert.Equal(-36000L, root.GetProperty("tbl6_1_net_camt_paisa").GetInt64());
        Assert.Equal(-36000L, root.GetProperty("tbl6_1_net_samt_paisa").GetInt64());
        Assert.Equal(0L, root.GetProperty("tbl6_1_net_iamt_paisa").GetInt64());
    }

    [Fact]
    public void Gstr1_and_Gstr3b_agree_on_the_period_output_tax()
    {
        // The two returns are filed for the same period off the same posted tax lines, so their outward tax MUST
        // reconcile — the single most-checked cross-return invariant a GST officer applies.
        var c = BuildRegular();
        using var one = JsonDocument.Parse(GstReturnJson.Gstr1(c, AprFrom, AprTo));
        using var threeB = JsonDocument.Parse(GstReturnJson.Gstr3b(c, AprFrom, AprTo));

        Assert.Equal(
            one.RootElement.GetProperty("total_cgst_paisa").GetInt64(),
            threeB.RootElement.GetProperty("tbl3_1a_camt_paisa").GetInt64());
        Assert.Equal(
            one.RootElement.GetProperty("total_sgst_paisa").GetInt64(),
            threeB.RootElement.GetProperty("tbl3_1a_samt_paisa").GetInt64());
        Assert.Equal(
            one.RootElement.GetProperty("total_igst_paisa").GetInt64(),
            threeB.RootElement.GetProperty("tbl3_1a_iamt_paisa").GetInt64());
    }
}
