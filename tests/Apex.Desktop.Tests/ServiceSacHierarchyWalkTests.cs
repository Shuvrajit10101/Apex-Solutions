using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using Apex.Desktop.Services;
using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Io;
using Apex.Ledger.Reports;
using Apex.Ledger.Services;
using Apex.Persistence.Sqlite;
using Xunit;
using Path = System.IO.Path;

namespace Apex.Desktop.Tests;

/// <summary>
/// 🔴 <b>CENSUS 6.4, IV-254 (wave 10, track ga, §2X) — A SERVICE (LEDGER) LEG FILES ITS SAC FROM THE SAME HIERARCHY
/// WALK A STOCK LINE DOES.</b>
///
/// <para><b>The defect, on <c>origin/main</c> fdc94d7.</b> <c>Gstr1.ServiceSacOf</c> and <c>AddServiceHsnRow</c> read the
/// service ledger's OWN SAC block and nothing else, and the INV-01 writer and the printed invoice read
/// <c>ServiceSacOf</c>. So a service ledger that declared no SAC, under a group declaring 998311, filed GSTR-1 Table 12
/// under <c>(none)</c>, had its INV-01 REFUSED ("declares no HSN/SAC code") and printed a blank HSN/SAC cell, while
/// wave 9 had already routed every stock line through the hierarchy.</para>
///
/// <para><b>VENDOR (R7, opened by content 2026-11-02,
/// <c>help.tallysolutions.com/india-gst-manage-hsn-code-sac-and-tax-rates-tally/</c>, "How to Manage HSN Codes/SAC and
/// Tax Rates in TallyPrime"):</b> "TallyPrime first checks the ledger for the details. If not found there, it will move
/// to the Group, then Stock Item, and so on"; the orders are <c>Ledger → Group → Stock Item → Stock Group → Company</c>
/// and <c>Stock Item → Stock Group → Ledger → Group → Company</c>; "The same explanation is true for even HSN/SAC
/// details."</para>
///
/// <para>Every assertion is on an emitted artefact of a book saved and re-loaded through a <b>fresh</b>
/// <see cref="SqliteCompanyStore"/>: the GSTR-1 and GSTR-9 JSON bytes, the INV-01 JSON bytes and the tax-invoice PDF
/// bytes (its <c>Tj</c> text operands). Odd-paisa taxable value (₹12,345.67 @ 18%).</para>
/// </summary>
public sealed class ServiceSacHierarchyWalkTests
{
    private const string SellerGstin = "27AAPFU0939F1ZV";
    private const string BuyerGstin = "27AAACR5055K1Z5";
    private static readonly DateOnly FyStart = new(2025, 4, 1);
    private static readonly DateOnly FyEnd = new(2026, 3, 31);
    private static readonly DateOnly SaleDate = new(2025, 4, 10);
    private const decimal Taxed = 12_345.67m;
    private const decimal Exempt = 4_321.09m;

    private const string LedgerSac = "998312";
    private const string GroupSac = "998311";
    private const string CompanySac = "998313";
    private const string ExemptGroupSac = "999293";

    /// <summary>Which master carries the SAC on the fixture.</summary>
    public enum Carrier { Group, Company }

    /// <summary>
    /// A service ledger with NO SAC under a group (or a company) declaring one files that code on GSTR-1 Table 12,
    /// GSTR-9 Table 17, INV-01 <c>HsnCd</c> and the printed invoice, under BOTH published orders (with no stock item
    /// the two item rungs declare nothing, so both orders reach the group, then the company). On main: Table 12
    /// <c>(none)</c>, INV-01 refused, the PDF blank.
    /// </summary>
    [Theory]
    [InlineData(GstDetailSource.LedgerFirst, Carrier.Group, GroupSac)]
    [InlineData(GstDetailSource.StockItemFirst, Carrier.Group, GroupSac)]
    [InlineData(GstDetailSource.LedgerFirst, Carrier.Company, CompanySac)]
    [InlineData(GstDetailSource.StockItemFirst, Carrier.Company, CompanySac)]
    public void A_service_ledger_with_no_sac_files_its_group_or_company_sac_on_every_document(
        GstDetailSource order, Carrier carrier, string expected)
    {
        var (c, sale) = Reload(Build(order, ledgerSac: null, carrier, withExemptLeg: false));
        Assert.Equal(order, c.Gst!.SourceOfHsnSacDetails);
        var consultancy = c.FindLedgerByName("Consultancy Income")!;
        Assert.Null(consultancy.SalesPurchaseGst!.HsnSac);                 // the fixture's premise survived the reload

        using (var g1 = JsonDocument.Parse(GstReturnJson.Gstr1(c, FyStart, FyEnd)))
        {
            var row = Assert.Single(g1.RootElement.GetProperty("hsn").EnumerateArray());
            Assert.Equal(expected, row.GetProperty("hsn_sac").GetString());   // main: "(none)"
            Assert.Equal(1_234_567L, row.GetProperty("txval_paisa").GetInt64());
        }
        using (var g9 = JsonDocument.Parse(GstReturnJson.Gstr9(c, FyStart, FyEnd)))
        {
            var row = Assert.Single(g9.RootElement.GetProperty("tbl17_hsn").EnumerateArray());
            Assert.Equal(expected, row.GetProperty("hsn_sac").GetString());
        }
        using (var inv = JsonDocument.Parse(Encoding.UTF8.GetString(EInvoiceJson.BuildInv01(c, sale))))   // main: throws
        {
            var item = Assert.Single(inv.RootElement.GetProperty("ItemList").EnumerateArray());
            Assert.Equal(expected, item.GetProperty("HsnCd").GetString());
            Assert.Equal("Y", item.GetProperty("IsServc").GetString());
        }
        var pdf = Pdf(c, sale);
        Assert.Contains($"({expected}) Tj", pdf, StringComparison.Ordinal);   // main: absent
    }

    /// <summary>
    /// 🔴 <b>THE LEDGER'S OWN SAC STILL WINS.</b> With the ledger declaring 998312 and its group 998311, every
    /// document files 998312 — the group rung is reached only when the ledger is silent. A walk that read the group
    /// first would redden this.
    /// </summary>
    [Theory]
    [InlineData(GstDetailSource.LedgerFirst)]
    [InlineData(GstDetailSource.StockItemFirst)]
    public void A_service_ledger_that_declares_its_own_sac_files_it_over_the_groups(GstDetailSource order)
    {
        var (c, sale) = Reload(Build(order, ledgerSac: LedgerSac, Carrier.Group, withExemptLeg: false));
        using (var g1 = JsonDocument.Parse(GstReturnJson.Gstr1(c, FyStart, FyEnd)))
            Assert.Equal(LedgerSac, Assert.Single(g1.RootElement.GetProperty("hsn").EnumerateArray())
                .GetProperty("hsn_sac").GetString());
        using (var inv = JsonDocument.Parse(Encoding.UTF8.GetString(EInvoiceJson.BuildInv01(c, sale))))
            Assert.Equal(LedgerSac, Assert.Single(inv.RootElement.GetProperty("ItemList").EnumerateArray())
                .GetProperty("HsnCd").GetString());
        var pdf = Pdf(c, sale);
        Assert.Contains($"({LedgerSac}) Tj", pdf, StringComparison.Ordinal);
        Assert.DoesNotContain($"({GroupSac}) Tj", pdf, StringComparison.Ordinal);
    }

    /// <summary>
    /// The EXEMPT service leg of a mixed invoice (the second INV-01 path, <c>NonTaxableServiceLegs</c>, and GSTR-1's
    /// exempt SAC row) walks the hierarchy too: Education, with no SAC, under a sub-group declaring 999293, files
    /// 999293 beside Consultancy's 998311 on Table 12, INV-01 and the PDF.
    /// </summary>
    [Fact]
    public void An_exempt_service_leg_with_no_sac_files_its_groups_sac_too()
    {
        var (c, sale) = Reload(Build(GstDetailSource.LedgerFirst, ledgerSac: null, Carrier.Group, withExemptLeg: true));

        using (var g1 = JsonDocument.Parse(GstReturnJson.Gstr1(c, FyStart, FyEnd)))
        {
            var rows = g1.RootElement.GetProperty("hsn").EnumerateArray()
                .ToDictionary(r => r.GetProperty("hsn_sac").GetString()!, r => r.GetProperty("txval_paisa").GetInt64());
            Assert.Equal(2, rows.Count);                                       // main: one "(none)" row of both legs
            Assert.Equal(1_234_567L, rows[GroupSac]);
            Assert.Equal(432_109L, rows[ExemptGroupSac]);
            Assert.Equal(432_109L, g1.RootElement.GetProperty("nil_exempt_nongst_paisa").GetInt64());
        }
        using (var inv = JsonDocument.Parse(Encoding.UTF8.GetString(EInvoiceJson.BuildInv01(c, sale))))
        {
            var codes = inv.RootElement.GetProperty("ItemList").EnumerateArray()
                .Select(i => i.GetProperty("HsnCd").GetString()).ToList();
            Assert.Equal(new[] { GroupSac, ExemptGroupSac }, codes);
        }
        var pdf = Pdf(c, sale);
        Assert.Contains($"({GroupSac}) Tj", pdf, StringComparison.Ordinal);
        Assert.Contains($"({ExemptGroupSac}) Tj", pdf, StringComparison.Ordinal);
    }

    /// <summary>
    /// The refusal is kept for the case it was written for: when NO rung declares a SAC the INV-01 is still refused,
    /// and the message now says the group and the company were looked at too.
    /// </summary>
    [Fact]
    public void When_no_rung_declares_a_sac_the_inv01_is_still_refused()
    {
        var (c, sale) = Reload(Build(GstDetailSource.LedgerFirst, ledgerSac: null, carrier: null, withExemptLeg: false));
        var ex = Assert.Throws<InvalidOperationException>(() => EInvoiceJson.BuildInv01(c, sale));
        Assert.Contains("declares no HSN/SAC code, and neither does its group nor the company", ex.Message,
            StringComparison.Ordinal);
        using var g1 = JsonDocument.Parse(GstReturnJson.Gstr1(c, FyStart, FyEnd));
        Assert.Equal("(none)", Assert.Single(g1.RootElement.GetProperty("hsn").EnumerateArray())
            .GetProperty("hsn_sac").GetString());
    }

    // ================================================================ fixture

    private static string Pdf(Company c, Voucher sale) =>
        Encoding.Latin1.GetString(
            InvoicePdf.Render(VoucherPrintProjector.ProjectInvoice(c, sale), new PrintConfig(), new PageConfig()));

    private static (Company Company, Voucher Sale) Build(
        GstDetailSource order, string? ledgerSac, Carrier? carrier, bool withExemptLeg)
    {
        var c = CompanyFactory.CreateSeeded("Service SAC Walk Co", FyStart);
        c.Address = "Unit 4, Fort Industrial Estate\nBallard Pier";
        c.Pin = "400001";
        var gst = new GstService(c);
        gst.EnableGst(new GstConfig
        {
            HomeStateCode = "27", Gstin = SellerGstin, RegistrationType = GstRegistrationType.Regular,
            ApplicableFrom = FyStart, Periodicity = GstReturnPeriodicity.Monthly,
            EInvoicingEnabled = true, EInvoiceApplicableFrom = FyStart,
        });
        c.Gst!.SourceOfHsnSacDetails = order;

        var salesAccounts = c.FindGroupByName("Sales Accounts")!;
        if (carrier == Carrier.Group)
            salesAccounts.Gst = new MasterGstDetails
            { HsnSac = GroupSac, Taxability = GstTaxability.Taxable, RateBasisPoints = 1800 };
        else if (carrier == Carrier.Company)
            c.Gst!.DefaultGst = new MasterGstDetails
            { HsnSac = CompanySac, Taxability = GstTaxability.Taxable, RateBasisPoints = 1800 };

        var consultancy = Add(c, "Consultancy Income", salesAccounts.Id, openingIsDebit: false);
        consultancy.SalesPurchaseGst = new StockItemGstDetails
        {
            HsnSac = ledgerSac, SupplyType = GstSupplyType.Services,
            Taxability = GstTaxability.Taxable, RateBasisPoints = 1800,
        };

        var buyer = Add(c, "Buyer", c.FindGroupByName("Sundry Debtors")!.Id, openingIsDebit: true);
        buyer.PartyGst = new PartyGstDetails
        { RegistrationType = GstRegistrationType.Regular, Gstin = BuyerGstin, StateCode = "27" };
        buyer.Mailing = new PartyMailingDetails
        {
            MailingName = "Buyer Trading Co", Address = "12 Residency Road\nShivajinagar",
            Country = "India", Pincode = "400012",
        };

        var tax = gst.ComputeInvoiceTax(
            new[] { new GstService.TaxableLine(new Money(Taxed), 1800) }, interState: false, GstTaxDirection.Output);
        var exemptValue = 0m;
        var lines = new List<EntryLine> { new(consultancy.Id, new Money(Taxed), DrCr.Credit) };
        if (withExemptLeg)
        {
            var exemptGroup = new Group(Guid.NewGuid(), "Exempt Services", salesAccounts.Nature, salesAccounts.Id)
            {
                Gst = new MasterGstDetails { HsnSac = ExemptGroupSac, Taxability = GstTaxability.Exempt },
            };
            c.AddGroup(exemptGroup);
            var education = Add(c, "Education Income", exemptGroup.Id, openingIsDebit: false);
            education.SalesPurchaseGst = new StockItemGstDetails
            { HsnSac = null, SupplyType = GstSupplyType.Services, Taxability = GstTaxability.Exempt };
            lines.Add(new EntryLine(education.Id, new Money(Exempt), DrCr.Credit));
            exemptValue = Exempt;
        }
        lines.Insert(0, new EntryLine(buyer.Id, new Money(Taxed + exemptValue + tax.TotalTax.Amount), DrCr.Debit));
        lines.AddRange(tax.TaxLines);

        var sale = new LedgerService(c).Post(new Voucher(
            Guid.NewGuid(), c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Sales).Id, SaleDate, lines,
            number: 1, partyId: buyer.Id, isAccountingInvoice: true));
        Assert.Equal(EInvoiceCoverage.Covered, new EInvoiceService(c).CoverageOf(sale));
        return (c, sale);
    }

    private static (Company Company, Voucher Sale) Reload((Company Company, Voucher Sale) original)
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"apex-service-sac-walk-{Guid.NewGuid():N}.db");
        try
        {
            using (var write = new SqliteCompanyStore(dbPath))
                write.Save(original.Company);
            using var read = new SqliteCompanyStore(dbPath);
            var c = read.Load(original.Company.Id)!;
            return (c, c.FindVoucher(original.Sale.Id)!);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            try { File.Delete(dbPath); } catch (IOException) { }
        }
    }

    private static Apex.Ledger.Domain.Ledger Add(Company c, string name, Guid groupId, bool openingIsDebit)
    {
        var l = new Apex.Ledger.Domain.Ledger(Guid.NewGuid(), name, groupId, Money.Zero, openingIsDebit);
        c.AddLedger(l);
        return l;
    }
}
