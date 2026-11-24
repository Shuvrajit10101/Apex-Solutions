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
/// 🔴 <b>CENSUS 6.12 <c>T2-167</c> — THE SERVICE HALF, ON A NO-BLOCK LEDGER (wave 13, track ib, §2AI, IV-370).</b>
///
/// <para>Wave 10 (§2X, IV-297) made <c>Gstr1.BuildTable9B</c> net every linked outward note into the HSN summary,
/// which closed the goods shape (<c>LinkedNoteHsnAndExemptReloadTests</c>). The note's own lines are read through the
/// SAME <c>Gstr1.ServiceLegs</c> definition as the sale's, so on <c>origin/main</c> 6f8c936 a service sale and a credit
/// note linked to it, both through a ledger with NO GST block under a group declaring 998311 / 18% (the Ledger
/// master's default state, census 6.4 IV-319(a)), filed NOTHING in Table 12 — GSTR-9 Table 17 empty against Table 4
/// and 3.1(a). With IV-368 the sale and the note are both read, signed, so Table 12 carries the NET row and
/// Table 17 == Table 4 == 3.1(a).</para>
///
/// <para>Odd paisa: sale ₹12,345.67 (CGST 1,111.11 + SGST 1,111.11), linked credit note ₹4,000.01 (CGST 360.00 +
/// SGST 360.00); net ₹8,345.66 / 751.11 / 751.11. Every assertion reads emitted JSON bytes of a book saved and
/// re-loaded through a fresh <see cref="SqliteCompanyStore"/>.</para>
/// </summary>
public sealed class NoBlockServiceLinkedNoteReloadTests
{
    private static readonly DateOnly FyFrom = new(2025, 4, 1);
    private static readonly DateOnly FyTo = new(2026, 3, 31);
    private const string Sac = "998311";

    /// <summary>
    /// The linked note nets the 998311 row: 834,566 / 75,111 / 75,111 paisa, and Table 17 == Table 4 == 3.1(a) head by
    /// head. <b>Mutation B1</b> (<c>IsNoBlockServiceLedger</c> returns false — main) reddens this at the Table 12 row;
    /// <b>B7</b> (the <c>AccumulateHsn</c> call in <c>BuildTable9B</c> disabled) reddens it at the net taxable
    /// (expected 834,566, actual 1,234,567).
    /// </summary>
    [Fact]
    public void A_linked_credit_note_on_a_no_block_service_ledger_nets_table_12_so_table_17_equals_3_1_a()
    {
        var c = Reload(Book());
        Assert.Null(c.FindLedgerByName("Consultancy Income")!.SalesPurchaseGst);   // the premise
        Assert.NotNull(Assert.Single(c.CreditDebitNoteLinks).OriginalInvoiceVoucherId);

        using var g3b = JsonDocument.Parse(GstReturnJson.Gstr3b(c, FyFrom, FyTo));
        var cgst31a = g3b.RootElement.GetProperty("tbl3_1a_camt_paisa").GetInt64();
        var sgst31a = g3b.RootElement.GetProperty("tbl3_1a_samt_paisa").GetInt64();
        Assert.Equal(75_111L, cgst31a);
        Assert.Equal(75_111L, sgst31a);

        using var g1 = JsonDocument.Parse(GstReturnJson.Gstr1(c, FyFrom, FyTo));
        var row = Assert.Single(g1.RootElement.GetProperty("hsn").EnumerateArray());   // main: no row
        Assert.Equal(Sac, row.GetProperty("hsn_sac").GetString());
        Assert.Equal(834_566L, row.GetProperty("txval_paisa").GetInt64());
        Assert.Equal(75_111L, row.GetProperty("camt_paisa").GetInt64());
        Assert.Equal(75_111L, row.GetProperty("samt_paisa").GetInt64());

        using var g9 = JsonDocument.Parse(GstReturnJson.Gstr9(c, FyFrom, FyTo));
        var root = g9.RootElement;
        var t17 = Assert.Single(root.GetProperty("tbl17_hsn").EnumerateArray());
        Assert.Equal(root.GetProperty("tbl4_out_cgst_paisa").GetInt64(), t17.GetProperty("cgst_paisa").GetInt64());
        Assert.Equal(root.GetProperty("tbl4_out_sgst_paisa").GetInt64(), t17.GetProperty("sgst_paisa").GetInt64());
        Assert.Equal(root.GetProperty("tbl4n_taxable_value_paisa").GetInt64(),
            t17.GetProperty("taxable_value_paisa").GetInt64());
        Assert.Equal(cgst31a, t17.GetProperty("cgst_paisa").GetInt64());
        Assert.Equal(sgst31a, t17.GetProperty("sgst_paisa").GetInt64());
        Assert.Equal(834_566L, t17.GetProperty("taxable_value_paisa").GetInt64());

        Assert.Equal(1, g1.RootElement.GetProperty("cdnr").GetArrayLength());     // still one 9B document
        var r = Gstr9c.Build(c, FyFrom, FyTo);
        Assert.Equal(0.00m, r.Table11UnreconciledTax.Amount);
        Assert.Equal(0.00m, r.Table5RUnreconciledTurnover.Amount);
    }

    private static Company Book()
    {
        var c = CompanyFactory.CreateSeeded("No Block Linked Note Co", FyFrom);
        var gst = new GstService(c);
        gst.EnableGst(new GstConfig
        {
            HomeStateCode = "27", Gstin = "27AAPFU0939F1ZV", RegistrationType = GstRegistrationType.Regular,
            ApplicableFrom = FyFrom, Periodicity = GstReturnPeriodicity.Monthly,
        });
        c.FindGroupByName("Sales Accounts")!.Gst = new MasterGstDetails
        { HsnSac = Sac, Taxability = GstTaxability.Taxable, RateBasisPoints = 1800 };

        var ledgers = new LedgerService(c);
        var income = Add(c, "Consultancy Income", "Sales Accounts", false);
        var debtor = Add(c, "Buyer", "Sundry Debtors", true);
        debtor.PartyGst = new PartyGstDetails
        { RegistrationType = GstRegistrationType.Regular, Gstin = "27AAACR5055K1Z5", StateCode = "27" };
        Guid TypeOf(VoucherBaseType b) => c.VoucherTypes.First(t => t.BaseType == b).Id;

        // The rate the posting path resolves for the no-block ledger is the group's (the walk this book relies on).
        var res = gst.ResolveRate(null, income, new DateOnly(2025, 5, 10));
        Assert.True(res.IsTaxable);
        Assert.Equal(1800, res.RateBasisPoints);

        const decimal saleValue = 12_345.67m;
        var saleTax = gst.ComputeInvoiceTax(new[] { new GstService.TaxableLine(new Money(saleValue), 1800) }, false,
            GstTaxDirection.Output);
        Assert.Equal(2_222.22m, saleTax.TotalTax.Amount);
        var saleLines = new List<EntryLine>
        {
            new(debtor.Id, new Money(saleValue + saleTax.TotalTax.Amount), DrCr.Debit),
            new(income.Id, new Money(saleValue), DrCr.Credit),
        };
        saleLines.AddRange(saleTax.TaxLines);
        var saleDate = new DateOnly(2025, 5, 10);
        var sale = ledgers.Post(new Voucher(Guid.NewGuid(), TypeOf(VoucherBaseType.Sales), saleDate, saleLines,
            number: 1, partyId: debtor.Id));

        const decimal noteValue = 4_000.01m;
        var noteTax = gst.ComputeInvoiceTax(new[] { new GstService.TaxableLine(new Money(noteValue), 1800) }, false,
            GstTaxDirection.Output, reverseSides: true);
        Assert.Equal(720.00m, noteTax.TotalTax.Amount);
        var noteLines = new List<EntryLine>
        {
            new(income.Id, new Money(noteValue), DrCr.Debit),
            new(debtor.Id, new Money(noteValue + noteTax.TotalTax.Amount), DrCr.Credit),
        };
        noteLines.AddRange(noteTax.TaxLines);
        var noteId = Guid.NewGuid();
        c.AddCreditDebitNoteLink(new GstCreditDebitNoteLink(
            Guid.NewGuid(), noteId, CdnType.Credit, sale.Id, "1", saleDate, "01 Sales return"));
        ledgers.Post(new Voucher(noteId, TypeOf(VoucherBaseType.CreditNote), new DateOnly(2025, 6, 10), noteLines,
            partyId: debtor.Id));
        return c;
    }

    private static Domain.Ledger Add(Company c, string name, string group, bool dr)
    {
        var l = new Domain.Ledger(Guid.NewGuid(), name, c.FindGroupByName(group)!.Id, Money.Zero, openingIsDebit: dr);
        c.AddLedger(l);
        return l;
    }

    private static Company Reload(Company original)
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"apex-noblock-linked-{Guid.NewGuid():N}.db");
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
