using Apex.Ledger.Domain;
using Apex.Ledger.Reports;
using Apex.Ledger.Services;
using Xunit;

namespace Apex.Ledger.Tests;

/// <summary>
/// <b>GSTR-1 Table 12 — the Compensation-Cess column (census row 6.8).</b>
///
/// <para><b>What was wrong.</b> <c>Gstr1HsnRow</c> carried no cess member at all, so the HSN/SAC summary filed a
/// <b>blank</b> statutory cess cell on every cess-bearing supply — the grid had no column for it and
/// <c>GstReturnJson</c> had no field for it. The cause was structural rather than an omission: <c>Gstr1.cs</c>'s
/// <c>ReadInvoiceRateGroups</c> skips every <see cref="GstTaxHead.Cess"/> line (the <c>:561</c> guard), so cess
/// could never reach an HSN row through the tax-group path at all.</para>
///
/// <para><b>🔴 The <c>:561</c> guard is NOT removed, and these tests hold that.</b> It is load-bearing: a cess
/// leg's rate key is derived from the cess amount, so admitting it into the rate groups would invent a phantom
/// rate row and DOUBLE-COUNT the group's taxable value. Cess is instead read on its own
/// (<c>ReadPostedCessByRate</c>) and attributed separately, so both facts hold at once — which is exactly what
/// <see cref="Cess_does_not_double_count_the_taxable_value_of_its_rate_group"/> pins.</para>
///
/// <para><b>🔴 The attribution question these tests exist for.</b> Cess is levied at its own rate on its own
/// notified goods, so one GST rate group routinely mixes cess-bearing and cess-free lines. Apportioning the
/// group's posted cess by VALUE would smear it across every line and file cess against an HSN that bears none —
/// a positive misstatement on a filed cell. The implementation weights each line by the engine's own
/// <c>CessCharge.CessBeforeRounding</c>, so a cess-free line weighs zero and receives nothing.
/// <see cref="Cess_lands_only_on_the_cess_bearing_HSN_and_is_not_smeared_across_the_rate_group"/> is the test
/// that separates the two designs: under a value-share it would read 60 / 60 instead of 120 / 0.</para>
///
/// <para>Figures worked by hand. Cola ₹1,000 @ 28% + 12% ad-valorem cess (HSN 220210) and Mug ₹1,000 @ 28% with
/// NO cess (HSN 691200) sit in the SAME 28% rate group; Pen ₹500 @ 5% (HSN 960810) forms a second group that
/// bears no cess at all. Posted cess = 12% × 1,000 = ₹120.00 and nothing else.</para>
/// </summary>
public class Gstr1HsnCessAttributionTests
{
    private static readonly DateOnly FyStart = new(2024, 4, 1);
    private static readonly DateOnly From = new(2024, 4, 1);
    private static readonly DateOnly To = new(2024, 4, 30);
    private static readonly DateOnly D = new(2024, 4, 6);

    private const string GstinMaharashtra = "27AAPFU0939F1ZV";

    [Fact]
    public void Cess_lands_only_on_the_cess_bearing_HSN_and_is_not_smeared_across_the_rate_group()
    {
        var r = Report.BuildGstr1(BuildCessBook(), From, To);

        var cola = Row(r, "220210");
        var mug = Row(r, "691200");

        // 🔴 THE WHOLE POINT. Cola alone bears the cess; the Mug shares its 28% rate group and its taxable value
        // but bears none. A value-share apportionment would file 60.00 against each.
        Assert.Equal(120.00m, cola.Cess.Amount);
        Assert.Equal(0.00m, mug.Cess.Amount);

        // The taxable values are untouched by the cess attribution.
        Assert.Equal(1000.00m, cola.TaxableValue.Amount);
        Assert.Equal(1000.00m, mug.TaxableValue.Amount);
    }

    [Fact]
    public void A_rate_group_that_bears_no_cess_receives_none()
    {
        var r = Report.BuildGstr1(BuildCessBook(), From, To);
        Assert.Equal(0.00m, Row(r, "960810").Cess.Amount); // Pen @ 5%, a group with no cess leg at all
    }

    [Fact]
    public void Every_paisa_of_the_posted_cess_reaches_exactly_one_HSN_row()
    {
        var company = BuildCessBook();
        var r = Report.BuildGstr1(company, From, To);

        // Σ over the filed rows must equal the cess actually POSTED to the ledgers — the report never recomputes
        // cess from a rate, it only decides which row each posted paisa belongs to.
        var posted = company.Vouchers
            .SelectMany(v => v.Lines)
            .Where(l => l.Gst is { TaxHead: GstTaxHead.Cess })
            .Sum(l => l.Amount.Amount);

        Assert.Equal(120.00m, posted);
        Assert.Equal(posted, r.HsnSummary.Sum(h => h.Cess.Amount));
    }

    /// <summary>
    /// 🔴 <b>The <c>:561</c> regression guard.</b> If a future change admits the Cess leg into
    /// <c>ReadInvoiceRateGroups</c> to get at the cess, the cess leg's own rate key injects a phantom rate row and
    /// the 28% group's taxable value is counted twice. This pins the taxable side of a cess-bearing invoice so
    /// that mistake cannot ship silently: three HSN rows, ₹2,500 of taxable value in total, and NO extra rate row.
    /// </summary>
    [Fact]
    public void Cess_does_not_double_count_the_taxable_value_of_its_rate_group()
    {
        var r = Report.BuildGstr1(BuildCessBook(), From, To);

        Assert.Equal(3, r.HsnSummary.Count);
        Assert.Equal(2500.00m, r.HsnSummary.Sum(h => h.TaxableValue.Amount));

        // The rate-wise summary sees exactly the two REAL rates — 28% and 5% — and the 28% group's taxable value
        // is ₹2,000 once, not ₹4,000.
        Assert.Equal(new[] { 500, 2800 }, r.RateSummary.Select(x => x.RateBasisPoints).ToArray());
        Assert.Equal(2000.00m, r.RateSummary.Single(x => x.RateBasisPoints == 2800).TaxableValue.Amount);
        Assert.Equal(500.00m, r.RateSummary.Single(x => x.RateBasisPoints == 500).TaxableValue.Amount);
    }

    /// <summary>Cess is NOT part of the row's tax amount — the return states it in its own column, and the cess
    /// ring-fence (ER-2) is the same distinction carried onto the filed row.</summary>
    [Fact]
    public void Cess_is_not_folded_into_the_rows_total_tax()
    {
        var cola = Row(Report.BuildGstr1(BuildCessBook(), From, To), "220210");
        Assert.Equal(280.00m, cola.TotalTax.Amount);   // 28% of 1000, intra ⇒ 140 CGST + 140 SGST
        Assert.Equal(120.00m, cola.Cess.Amount);
    }

    /// <summary>
    /// 🔴 <b>Table 12 "Total Value" DOES include cess, and that is the whole point of stating it separately from the
    /// tax amount.</b> <c>TotalTax</c> excludes cess because the return ring-fences it into its own column; Total
    /// Value is what the supply is WORTH, and the recipient is billed the cess. Getting this backwards would file a
    /// Total Value short by exactly the cess on every cess-bearing HSN — the same silent understatement as leaving
    /// the cell blank, only harder to notice because the cell would then look populated.
    /// </summary>
    [Fact]
    public void Table12_total_value_is_taxable_plus_tax_plus_cess_on_a_cess_bearing_row()
    {
        var r = Report.BuildGstr1(BuildCessBook(), From, To);

        var cola = Row(r, "220210");
        // ₹1,000 taxable + ₹280 tax + ₹120 cess = ₹1,400 — NOT ₹1,280.
        Assert.Equal(1400.00m, cola.TotalValue.Amount);
        Assert.NotEqual(cola.TaxableValue.Amount + cola.TotalTax.Amount, cola.TotalValue.Amount);

        // A cess-free row in the SAME rate group carries no cess, so its Total Value is taxable + tax exactly.
        var mug = Row(r, "691200");
        Assert.Equal(0.00m, mug.Cess.Amount);
        Assert.Equal(mug.TaxableValue.Amount + mug.TotalTax.Amount, mug.TotalValue.Amount);

        // Every row reconciles to its own parts, so Total Value can never drift from the cells filed beside it.
        Assert.All(r.HsnSummary, h => Assert.Equal(
            h.TaxableValue.Amount + h.Cgst.Amount + h.Sgst.Amount + h.Igst.Amount + h.Cess.Amount,
            h.TotalValue.Amount));
    }

    // ================================================================ fixture

    private static Gstr1HsnRow Row(Gstr1 r, string hsn) =>
        r.HsnSummary.Single(h => h.HsnSac == hsn);

    /// <summary>
    /// One intra-state outward invoice: Cola ₹1,000 @ 28% + 12% ad-valorem cess, Mug ₹1,000 @ 28% with no cess
    /// (same rate group), Pen ₹500 @ 5% (second rate group, no cess). The cess charge is resolved through
    /// <see cref="GstService.ResolveCess"/> rather than hand-built, so the fixture posts exactly what the engine
    /// would post for these masters.
    /// </summary>
    private static Company BuildCessBook()
    {
        var c = CompanyFactory.CreateSeeded("Cess Co", FyStart);
        var gst = new GstService(c);
        gst.EnableGst(new GstConfig
        {
            HomeStateCode = "27",
            Gstin = GstinMaharashtra,
            RegistrationType = GstRegistrationType.Regular,
            ApplicableFrom = FyStart,
            Periodicity = GstReturnPeriodicity.Monthly,
        });

        var ledgers = new LedgerService(c);
        var inv = new InventoryService(c);
        var grp = inv.CreateStockGroup("Goods");
        var nos = inv.CreateSimpleUnit("Nos", "Numbers", unitQuantityCode: "NOS");
        var main = c.MainLocation!.Id;

        var cola = inv.CreateStockItem("Cola", grp.Id, nos.Id);
        cola.Gst = new StockItemGstDetails
        {
            HsnSac = "220210",
            Taxability = GstTaxability.Taxable,
            RateBasisPoints = 2800,
            CessApplicable = true,
            CessValuationMode = CessValuationMode.AdValorem,
            CessRateBasisPoints = 1200,
        };
        var mug = inv.CreateStockItem("Mug", grp.Id, nos.Id);
        mug.Gst = new StockItemGstDetails
        {
            HsnSac = "691200", Taxability = GstTaxability.Taxable, RateBasisPoints = 2800,
        };
        var pen = inv.CreateStockItem("Pen", grp.Id, nos.Id);
        pen.Gst = new StockItemGstDetails
        {
            HsnSac = "960810", Taxability = GstTaxability.Taxable, RateBasisPoints = 500,
        };

        inv.AddOpeningBalance(cola.Id, main, 1000m, Money.FromRupees(5m));
        inv.AddOpeningBalance(mug.Id, main, 1000m, Money.FromRupees(5m));
        inv.AddOpeningBalance(pen.Id, main, 1000m, Money.FromRupees(2m));

        var sales = Add(c, "Sales", "Sales Accounts", false);
        var debtor = Add(c, "Debtor", "Sundry Debtors", true);
        debtor.PartyGst = new PartyGstDetails
        {
            RegistrationType = GstRegistrationType.Regular, Gstin = GstinMaharashtra, StateCode = "27",
        };

        var salesType = c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Sales).Id;
        var tax = gst.ComputeInvoiceTax(new[]
        {
            new GstService.TaxableLine(Money.FromRupees(1000m), 2800,
                gst.ResolveCess(cola, sales, D, quantity: 100m)),
            new GstService.TaxableLine(Money.FromRupees(1000m), 2800),
            new GstService.TaxableLine(Money.FromRupees(500m), 500),
        }, interState: false, GstTaxDirection.Output);

        var lines = new List<EntryLine>
        {
            new(debtor.Id, Money.FromRupees(2500m + tax.TotalTax.Amount + tax.TotalCess.Amount), DrCr.Debit),
            new(sales.Id, Money.FromRupees(2500m), DrCr.Credit),
        };
        lines.AddRange(tax.TaxLines);

        ledgers.Post(new Voucher(Guid.NewGuid(), salesType, D, lines, partyId: debtor.Id, inventoryLines: new[]
        {
            new VoucherInventoryLine(cola.Id, main, 100m, Money.FromRupees(10m)),  // 100 × 10 = 1000
            new VoucherInventoryLine(mug.Id, main, 100m, Money.FromRupees(10m)),   // 100 × 10 = 1000
            new VoucherInventoryLine(pen.Id, main, 100m, Money.FromRupees(5m)),    // 100 ×  5 =  500
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
