using System.Text.Json;
using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Io;
using Apex.Ledger.Reports;
using Apex.Ledger.Services;
using Apex.Persistence.Sqlite;
using Xunit;
using Domain = Apex.Ledger.Domain;

namespace Apex.Persistence.Sqlite.Tests;

/// <summary>
/// <b>Wave 13 track ib, REVIEWER's fixes (R-F1, R-F2) to IV-368.</b> Admitting a NO-block ledger to
/// <c>Gstr1.ServiceLegs</c> on a walk-declared SAC closed IV-319(a), but it also let two shapes through that main kept
/// out. Each test below is red on the builder's head 90566bf and green with the fix; every figure is read off a book
/// re-opened through a fresh <see cref="SqliteCompanyStore"/>.
/// <list type="bullet">
/// <item><b>R-F1 (F9 regression).</b> A service invoice posted with NO tax leg (GST was off when it was keyed) on a
/// no-block ledger, under a group later classified 998311 / 18%, became <c>IsServiceAccountingInvoice</c> True, so it
/// printed as a "Tax Invoice" for a taxable supply at NIL GST. The F9 guarantee
/// (<c>InvoicePrintFootingTests.ServiceInvoice_postedWithGstOff_thenLedgerClassifiedTaxable_printsAsAPlainVoucher</c>)
/// only held for a ledger carrying its own block. Main: False.</item>
/// <item><b>R-F2 (Table 12 inflation).</b> A no-block Discount Allowed DEBIT on a sale, under a company-level SAC, was
/// admitted as a service leg and its magnitude ADDED to Table 12 / GSTR-9 Table 17: 11,000.01 against 3.1(a) 9,000.01
/// (main 10,000.01).</item>
/// </list>
/// </summary>
public sealed class NoBlockServiceLegReviewTests
{
    private static readonly DateOnly Fy = new(2025, 4, 1);
    private static readonly DateOnly FyTo = new(2026, 3, 31);
    private static readonly DateOnly On = new(2025, 7, 3);

    /// <summary>R-F1. <b>Mutation</b>: make <c>GstReportSupport.DeclaresNonZeroRate</c>'s no-block arm return false
    /// (the builder's reading) and the first assertion goes red (Expected False, Actual True).</summary>
    [Fact]
    public void An_untaxed_invoice_on_a_no_block_ledger_under_a_taxed_sac_group_is_not_a_tax_invoice()
    {
        var (c, buyer) = Book("No Block Nil GST Co");
        c.FindGroupByName("Sales Accounts")!.Gst = new MasterGstDetails
        { HsnSac = "998311", Taxability = GstTaxability.Taxable, RateBasisPoints = 1800 };
        var income = Add(c, "Consulting", "Sales Accounts", false);
        var id = PostTwoLegInvoice(c, buyer, income, 5_000.01m);

        var r = Reload(c);
        var v = r.FindVoucher(id)!;
        Assert.Null(r.FindLedgerByName("Consulting")!.SalesPurchaseGst);   // the premise: no block of its own
        Assert.All(v.Lines, l => Assert.Null(l.Gst));                      // no tax was posted
        Assert.False(GstReportSupport.IsServiceAccountingInvoice(r, v));   // head 90566bf: True
        Assert.False(GstReportSupport.IsBillOfSupply(r, v));
    }

    /// <summary>R-F1 control: the demotion reads the DECLARED rate, never "no tax was posted", so a no-block leg under a
    /// group declaring a ZERO rate stays a tax invoice (the F1 behaviour F9 must not regress).</summary>
    [Fact]
    public void An_untaxed_invoice_on_a_no_block_ledger_under_a_zero_rated_sac_group_is_still_a_tax_invoice()
    {
        var (c, buyer) = Book("No Block Zero Rated Co");
        c.FindGroupByName("Sales Accounts")!.Gst = new MasterGstDetails
        { HsnSac = "998313", Taxability = GstTaxability.Taxable, RateBasisPoints = 0 };
        var income = Add(c, "Export Consulting", "Sales Accounts", false);
        var id = PostTwoLegInvoice(c, buyer, income, 4_321.09m);

        var r = Reload(c);
        Assert.True(GstReportSupport.IsServiceAccountingInvoice(r, r.FindVoucher(id)!));
    }

    /// <summary>R-F2. <b>Mutation</b>: drop the natural-side conjunct from <c>Gstr1.ServiceLegs</c>'s no-block admission
    /// and this goes red at the leg list (the discount appears) and at Table 12 (1,100,001 is out of range).</summary>
    [Fact]
    public void A_no_block_discount_debit_on_a_sale_under_a_company_sac_is_not_added_to_table_12()
    {
        var (c, buyer) = Book("No Block Discount Co");
        c.Gst!.DefaultGst = new MasterGstDetails
        { HsnSac = "998311", Taxability = GstTaxability.Taxable, RateBasisPoints = 1800 };
        var income = Add(c, "Consulting", "Sales Accounts", false);
        income.SalesPurchaseGst = new StockItemGstDetails
        {
            HsnSac = "998311", Taxability = GstTaxability.Taxable, RateBasisPoints = 1800,
            SupplyType = GstSupplyType.Services,
        };
        var discount = Add(c, "Discount Allowed", "Indirect Expenses", true);
        var gst = new GstService(c);
        var tax = gst.ComputeInvoiceTax(new[] { new GstService.TaxableLine(new Money(9_000.01m), 1800) }, false,
            GstTaxDirection.Output);
        var lines = new List<EntryLine>
        {
            new(buyer.Id, new Money(9_000.01m + tax.TotalTax.Amount), DrCr.Debit),
            new(discount.Id, new Money(1_000.00m), DrCr.Debit),
            new(income.Id, new Money(10_000.01m), DrCr.Credit),
        };
        lines.AddRange(tax.TaxLines);
        var id = new LedgerService(c).Post(new Voucher(Guid.NewGuid(), SalesType(c), On, lines,
            number: 1, partyId: buyer.Id)).Id;

        var r = Reload(c);
        Assert.Null(r.FindLedgerByName("Discount Allowed")!.SalesPurchaseGst);
        Assert.Equal(new[] { "Consulting" },
            Gstr1.ServiceLegs(r, r.FindVoucher(id)!).Select(x => x.Ledger.Name).ToArray());

        using var g1 = JsonDocument.Parse(GstReturnJson.Gstr1(r, Fy, FyTo));
        var row = Assert.Single(g1.RootElement.GetProperty("hsn").EnumerateArray(),
            x => x.GetProperty("hsn_sac").GetString() == "998311");
        // The discount must never be ADDED. Whether Table 12 should carry the gross or the net is T1-79's question
        // (pre-existing on a blocked leg), so this bounds the figure rather than pinning either answer.
        Assert.InRange(row.GetProperty("txval_paisa").GetInt64(), 0L, 1_000_001L);       // head 90566bf: 1,100,001
        using var g9 = JsonDocument.Parse(GstReturnJson.Gstr9(r, Fy, FyTo));
        var t17 = Assert.Single(g9.RootElement.GetProperty("tbl17_hsn").EnumerateArray());
        Assert.InRange(t17.GetProperty("taxable_value_paisa").GetInt64(), 0L, 1_000_001L);
    }

    // ================================================================ fixture

    private static (Company C, Domain.Ledger Buyer) Book(string name)
    {
        var c = CompanyFactory.CreateSeeded(name, Fy);
        new GstService(c).EnableGst(new GstConfig
        {
            HomeStateCode = "27", Gstin = "27AAPFU0939F1ZV", RegistrationType = GstRegistrationType.Regular,
            ApplicableFrom = Fy, Periodicity = GstReturnPeriodicity.Monthly,
        });
        var buyer = Add(c, "Buyer", "Sundry Debtors", true);
        buyer.PartyGst = new PartyGstDetails
        { RegistrationType = GstRegistrationType.Regular, Gstin = "27AAACR5055K1Z5", StateCode = "27" };
        return (c, buyer);
    }

    private static Guid PostTwoLegInvoice(Company c, Domain.Ledger buyer, Domain.Ledger income, decimal amount)
    {
        var lines = new List<EntryLine>
        {
            new(buyer.Id, new Money(amount), DrCr.Debit),
            new(income.Id, new Money(amount), DrCr.Credit),
        };
        return new LedgerService(c).Post(new Voucher(Guid.NewGuid(), SalesType(c), On, lines,
            number: 1, partyId: buyer.Id, isAccountingInvoice: true)).Id;
    }

    private static Guid SalesType(Company c) => c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Sales).Id;

    private static Domain.Ledger Add(Company c, string name, string group, bool dr)
    {
        var l = new Domain.Ledger(Guid.NewGuid(), name, c.FindGroupByName(group)!.Id, Money.Zero, openingIsDebit: dr);
        c.AddLedger(l);
        return l;
    }

    private static Company Reload(Company original)
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"apex-noblock-review-{Guid.NewGuid():N}.db");
        try
        {
            using (var write = new SqliteCompanyStore(dbPath))
                write.Save(original);
            using var read = new SqliteCompanyStore(dbPath);
            return read.Load(original.Id)!;
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            try { File.Delete(dbPath); } catch (IOException) { }
        }
    }
}
