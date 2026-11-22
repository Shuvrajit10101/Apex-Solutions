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
/// 🔴 <b>CENSUS 6.12 RESIDUAL (wave 12, track ha, §2AB, IV-317) — AN EXEMPT SERVICE (LEDGER) LEG REACHES GSTR-3B
/// 3.1(c), AND SO GSTR-9 TABLE 5, NOT ONLY GSTR-1'S EXEMPT BUCKET.</b>
///
/// <para><b>The defect, on <c>origin/main</c> 33ade75.</b> <c>Gstr3b.ExemptValueOf</c> read stock lines only. GSTR-1
/// counts an exempt service leg (a ledger whose GST block is Exempt / Nil / Non-GST) in its exempt bucket on both
/// the wholly-untaxed and the mixed path, so the two returns disagreed by the whole exempt service turnover and GSTR-9C
/// 5R (books − returns) read it as unreconciled. The wave-10 reviewer measured the linked-note shape: exempt service
/// sale 1,234.57 with a linked credit note 400.01, GSTR-1 exempt 834.56 against 3.1(c) and Table 5 0.</para>
///
/// <para>No vendor page speaks to the return arithmetic; the ground is the returns' own structure (GSTR-9 Table 5 is
/// Σ 3B 3.1(c); 9C 5R = books − returns), as for IV-297 / IV-298. Every figure is read off the emitted JSON bytes of a
/// book saved and re-loaded through a fresh <see cref="SqliteCompanyStore"/>. Odd paisa throughout.</para>
/// </summary>
public sealed class ExemptServiceLegReturnsReloadTests
{
    private static readonly DateOnly FyFrom = new(2024, 4, 1);
    private static readonly DateOnly FyTo = new(2025, 3, 31);
    private const string GstinOwn = "27AAPFU0939F1ZV";
    private const string GstinBuyer = "27AAACR5055K1Z5";

    /// <summary>
    /// The MIXED service invoice: Consultancy ₹7,500.01 @ 18% and Education (exempt) ₹1,234.57 on one accounting
    /// invoice. 3.1(c) = GSTR-1 exempt = GSTR-9 Table 5 = 1,234.57 and 5R = 0. On main 3.1(c) read 0.
    /// <para><b>Mutation H2</b> (the service-leg loop in <c>ExemptValueOf</c> disabled) reddens this at 3.1(c).</para>
    /// </summary>
    [Fact]
    public void A_mixed_service_invoice_files_its_exempt_leg_in_3_1_c_and_table_5()
    {
        var c = Reload(Book(mixed: true, linkedNote: false, taxableLedgerNoTax: false));
        AssertExempt(c, 123_457L);
        using var g3b = JsonDocument.Parse(GstReturnJson.Gstr3b(c, FyFrom, FyTo));
        Assert.Equal(67_500L, g3b.RootElement.GetProperty("tbl3_1a_camt_paisa").GetInt64());   // the taxed leg untouched
    }

    /// <summary>
    /// The wave-10 reviewer's shape: an exempt service sale ₹1,234.57 and a credit note ₹400.01 linked to it. Every
    /// return files 834.56 and 5R is 0. On main 3.1(c) and Table 5 read 0 and 5R 834.56.
    /// </summary>
    [Fact]
    public void An_exempt_service_sale_and_its_linked_credit_note_net_in_3_1_c_as_in_gstr1()
    {
        var c = Reload(Book(mixed: false, linkedNote: true, taxableLedgerNoTax: false));
        Assert.Single(c.CreditDebitNoteLinks);
        AssertExempt(c, 83_456L);
    }

    /// <summary>
    /// 🔴 <b>THE GATE IS NOT WIDENED.</b> A TAXABLE (18%) service ledger posted on a plain voucher with no tax legs is
    /// not an exempt supply, and GSTR-1 keeps it out of the exempt bucket on the <c>IsTaxable:false</c> discriminator.
    /// 3.1(c) must keep it out too: both 0.
    /// <para><b>Mutation H6</b> (<c>IsNonTaxableServiceLedger</c> dropped from the new loop, counting every service
    /// leg) reddens this at 3.1(c).</para>
    /// </summary>
    [Fact]
    public void A_taxable_service_ledger_posted_with_no_tax_is_not_exempt_in_either_return()
    {
        var c = Reload(Book(mixed: false, linkedNote: false, taxableLedgerNoTax: true));
        AssertExempt(c, 0L, checkFiveR: false);   // its 5R is a books-vs-returns gap of a mis-posted sale, not ours
    }

    private static void AssertExempt(Company c, long expected, bool checkFiveR = true)
    {
        using var g1 = JsonDocument.Parse(GstReturnJson.Gstr1(c, FyFrom, FyTo));
        Assert.Equal(expected, g1.RootElement.GetProperty("nil_exempt_nongst_paisa").GetInt64());
        using var g3b = JsonDocument.Parse(GstReturnJson.Gstr3b(c, FyFrom, FyTo));
        Assert.Equal(expected, g3b.RootElement.GetProperty("tbl3_1c_nil_exempt_nongst_paisa").GetInt64());
        using var g9 = JsonDocument.Parse(GstReturnJson.Gstr9(c, FyFrom, FyTo));
        Assert.Equal(expected, g9.RootElement.GetProperty("tbl5_exempt_paisa").GetInt64());
        if (!checkFiveR) return;
        using var g9c = JsonDocument.Parse(GstReturnJson.Gstr9c(c, FyFrom, FyTo));
        Assert.Equal(0L, g9c.RootElement.GetProperty("tbl5r_unreconciled_turnover_paisa").GetInt64());
    }

    // ================================================================ fixture

    private static Company Book(bool mixed, bool linkedNote, bool taxableLedgerNoTax)
    {
        var c = CompanyFactory.CreateSeeded("Exempt Service Leg Co", FyFrom);
        var gst = new GstService(c);
        gst.EnableGst(new GstConfig
        {
            HomeStateCode = "27", Gstin = GstinOwn, RegistrationType = GstRegistrationType.Regular,
            ApplicableFrom = FyFrom, Periodicity = GstReturnPeriodicity.Monthly,
        });
        var ledgers = new LedgerService(c);
        var consultancy = Add(c, "Consultancy Income", "Sales Accounts", false);
        consultancy.SalesPurchaseGst = new StockItemGstDetails
        {
            HsnSac = "998311", Taxability = GstTaxability.Taxable, RateBasisPoints = 1800,
            SupplyType = GstSupplyType.Services,
        };
        var education = Add(c, "Education Income", "Sales Accounts", false);
        education.SalesPurchaseGst = new StockItemGstDetails
        { HsnSac = "999293", Taxability = GstTaxability.Exempt, SupplyType = GstSupplyType.Services };
        var buyer = Add(c, "Buyer", "Sundry Debtors", true);
        buyer.PartyGst = new PartyGstDetails
        { RegistrationType = GstRegistrationType.Regular, Gstin = GstinBuyer, StateCode = "27" };
        Guid TypeOf(VoucherBaseType b) => c.VoucherTypes.First(t => t.BaseType == b).Id;
        var saleDate = new DateOnly(2024, 5, 10);

        if (taxableLedgerNoTax)
        {
            ledgers.Post(new Voucher(Guid.NewGuid(), TypeOf(VoucherBaseType.Sales), saleDate, new[]
            {
                new EntryLine(buyer.Id, new Money(2_345.67m), DrCr.Debit),
                new EntryLine(consultancy.Id, new Money(2_345.67m), DrCr.Credit),
            }, number: 1, partyId: buyer.Id));
            return c;
        }

        if (mixed)
        {
            const decimal taxed = 7_500.01m;
            const decimal exempt = 1_234.57m;
            var tax = gst.ComputeInvoiceTax(
                new[] { new GstService.TaxableLine(new Money(taxed), 1800) }, false, GstTaxDirection.Output);
            var lines = new List<EntryLine>
            {
                new(buyer.Id, new Money(taxed + exempt + tax.TotalTax.Amount), DrCr.Debit),
                new(consultancy.Id, new Money(taxed), DrCr.Credit),
                new(education.Id, new Money(exempt), DrCr.Credit),
            };
            lines.AddRange(tax.TaxLines);
            ledgers.Post(new Voucher(Guid.NewGuid(), TypeOf(VoucherBaseType.Sales), saleDate, lines,
                number: 1, partyId: buyer.Id, isAccountingInvoice: true));
            return c;
        }

        var sale = ledgers.Post(new Voucher(Guid.NewGuid(), TypeOf(VoucherBaseType.Sales), saleDate, new[]
        {
            new EntryLine(buyer.Id, new Money(1_234.57m), DrCr.Debit),
            new EntryLine(education.Id, new Money(1_234.57m), DrCr.Credit),
        }, number: 1, partyId: buyer.Id, isAccountingInvoice: true));

        if (linkedNote)
        {
            var noteId = Guid.NewGuid();
            c.AddCreditDebitNoteLink(new GstCreditDebitNoteLink(
                Guid.NewGuid(), noteId, CdnType.Credit, sale.Id, "1", saleDate, "01 Sales return"));
            ledgers.Post(new Voucher(noteId, TypeOf(VoucherBaseType.CreditNote), new DateOnly(2024, 6, 10), new[]
            {
                new EntryLine(education.Id, new Money(400.01m), DrCr.Debit),
                new EntryLine(buyer.Id, new Money(400.01m), DrCr.Credit),
            }, partyId: buyer.Id));
        }
        return c;
    }

    private static Company Reload(Company original)
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"apex-exempt-service-leg-{Guid.NewGuid():N}.db");
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

    private static Domain.Ledger Add(Company c, string name, string groupName, bool openingIsDebit)
    {
        var l = new Domain.Ledger(Guid.NewGuid(), name, c.FindGroupByName(groupName)!.Id, Money.Zero, openingIsDebit);
        c.AddLedger(l);
        return l;
    }
}
