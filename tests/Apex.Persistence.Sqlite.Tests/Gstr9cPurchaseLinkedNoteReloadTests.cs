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
/// 🔴 <b>T2-162 (census 6.12) — a §34 note LINKED TO A PURCHASE was filed as an OUTWARD note.</b>
///
/// <para><b>The defect, on <c>origin/main</c> 0fbf8e2.</b> <c>Gstr3b.ReadCdn</c> walked every link in
/// <c>company.CreditDebitNoteLinks</c> and folded the note's tax legs into OUTWARD 3.1(a), signed by note type, while
/// <c>Gstr3b.ReadSide</c> excluded every linked note from the ITC sweep. A note linked to a PURCHASE posts INPUT legs
/// (it adjusts a supply made TO us), so its tax moved 3.1(a) — up for a debit note, down for a credit note — and never
/// reduced ITC. The 9C books anchors were already right (they net the credit-side Input leg), so the return side was
/// the wrong one: measured on a reloaded book, purchase ₹10,000 @ 18% and a linked note ₹2,000 (₹360) filed
/// <b>12F = −360.00</b>, and Table 11 ±360.00 by note type. GSTR-1 Table 9B (the <c>cdnr</c> section of the outward
/// return) also listed the purchase-side note as one of OUR credit/debit notes.</para>
///
/// <para><b>Grounding.</b> CGST Act §34(1)/(3) give the note to "the registered person who has supplied" the goods or
/// services — a note on a purchase is the SUPPLIER's document, so it belongs to our inward side; GSTR-1 is the
/// statement of OUTWARD supplies. Every figure here is read off a book saved and re-loaded through a fresh
/// <see cref="SqliteCompanyStore"/>, and the filed artefacts are asserted as emitted JSON bytes.</para>
///
/// <para><b>Figures worked by hand</b> (intra-state 27→27, 18% = CGST 9% + SGST 9%): sale ₹33,333.00 ⇒ ₹2,999.97 +
/// ₹2,999.97; purchase ₹10,000.00 ⇒ ₹900.00 + ₹900.00; linked note ₹2,000.00 ⇒ ₹180.00 + ₹180.00 on the CREDIT side
/// of Input. Tax per books = per return = ₹5,999.94; ITC per books = per return = 1,800.00 − 360.00 = ₹1,440.00.</para>
/// </summary>
public sealed class Gstr9cPurchaseLinkedNoteReloadTests
{
    private static readonly DateOnly FyFrom = new(2024, 4, 1);
    private static readonly DateOnly FyTo = new(2025, 3, 31);
    private const string GstinOwn = "27AAPFU0939F1ZV";
    private const string GstinSupplier = "27AAACR5055K1Z5";

    [Theory]
    [InlineData(CdnType.Debit)]
    [InlineData(CdnType.Credit)]
    public void Table_12F_and_Table_11_read_zero_for_a_purchase_linked_note_on_the_reloaded_book(CdnType type)
    {
        var c = Reload(Book(type));
        Assert.NotNull(Assert.Single(c.CreditDebitNoteLinks).OriginalInvoiceVoucherId); // the linked shape survived

        var r = Gstr9c.Build(c, FyFrom, FyTo);
        Assert.Equal(1_440.00m, r.Table12ABooksItc.Amount);
        Assert.Equal(1_440.00m, r.Table12EReturnItc.Amount);          // main: 1,800.00
        Assert.Equal(0.00m, r.Table12FUnreconciledItc.Amount);        // main: −360.00
        Assert.Equal(5_999.94m, r.Table9TaxPerBooks.Amount);
        Assert.Equal(5_999.94m, r.Table9TaxPerReturn.Amount);         // main: 6,359.94 (debit) / 5,639.94 (credit)
        Assert.Equal(0.00m, r.Table11UnreconciledTax.Amount);         // main: ±360.00
    }

    [Theory]
    [InlineData(CdnType.Debit)]
    [InlineData(CdnType.Credit)]
    public void The_emitted_9C_json_bytes_carry_zero_unreconciled_itc_and_tax(CdnType type)
    {
        using var doc = JsonDocument.Parse(GstReturnJson.Gstr9c(Reload(Book(type)), FyFrom, FyTo));
        var root = doc.RootElement;
        Assert.Equal(144_000L, root.GetProperty("tbl12a_books_itc_paisa").GetInt64());
        Assert.Equal(144_000L, root.GetProperty("tbl12e_return_itc_paisa").GetInt64());
        Assert.Equal(0L, root.GetProperty("tbl12f_unreconciled_itc_paisa").GetInt64());
        Assert.Equal(0L, root.GetProperty("tbl11_unreconciled_tax_paisa").GetInt64());
    }

    /// <summary>The ROOT, on the monthly return: the note reduces 4(A)(5) ITC and leaves 3.1(a) alone.</summary>
    [Theory]
    [InlineData(CdnType.Debit)]
    [InlineData(CdnType.Credit)]
    public void The_emitted_3B_json_bytes_reduce_itc_and_leave_outward_tax_alone(CdnType type)
    {
        using var doc = JsonDocument.Parse(GstReturnJson.Gstr3b(Reload(Book(type)), FyFrom, FyTo));
        var root = doc.RootElement;
        long P(string key) => root.GetProperty(key).GetInt64();
        Assert.Equal(299_997L, P("tbl3_1a_camt_paisa"));   // main: 317,997 (debit) / 281,997 (credit)
        Assert.Equal(299_997L, P("tbl3_1a_samt_paisa"));
        Assert.Equal(3_333_300L, P("tbl3_1a_txval_paisa")); // main: ±2,00,000 paisa
        Assert.Equal(72_000L, P("tbl4a5_camt_paisa"));     // main: 90,000
        Assert.Equal(72_000L, P("tbl4a5_samt_paisa"));
    }

    /// <summary>GSTR-1 is the statement of OUR outward supplies; the supplier's note on a purchase is not one of them.</summary>
    [Theory]
    [InlineData(CdnType.Debit)]
    [InlineData(CdnType.Credit)]
    public void The_emitted_GSTR1_json_bytes_carry_no_cdnr_row_for_a_purchase_linked_note(CdnType type)
    {
        using var doc = JsonDocument.Parse(GstReturnJson.Gstr1(Reload(Book(type)), FyFrom, FyTo));
        Assert.Equal(0, doc.RootElement.GetProperty("cdnr").GetArrayLength());   // main: 1
    }

    /// <summary>🔴 The guard on the fix: a SALES-linked note (the engine's own <see cref="CreditDebitNoteService"/>
    /// shape, Output legs) still folds into 3.1(a) and still appears in Table 9B. A fix that dropped every linked note
    /// would pass the four tests above and redden this one.</summary>
    [Fact]
    public void A_sales_linked_credit_note_still_reduces_outward_tax_and_is_still_a_cdnr_row()
    {
        var c = Reload(Book(CdnType.Credit, linkToSale: true));
        using var g3b = JsonDocument.Parse(GstReturnJson.Gstr3b(c, FyFrom, FyTo));
        Assert.Equal(299_997L - 18_000L, g3b.RootElement.GetProperty("tbl3_1a_camt_paisa").GetInt64());
        Assert.Equal(90_000L, g3b.RootElement.GetProperty("tbl4a5_camt_paisa").GetInt64());
        using var g1 = JsonDocument.Parse(GstReturnJson.Gstr1(c, FyFrom, FyTo));
        Assert.Equal(1, g1.RootElement.GetProperty("cdnr").GetArrayLength());

        var r = Gstr9c.Build(c, FyFrom, FyTo);
        Assert.Equal(0.00m, r.Table11UnreconciledTax.Amount);
        Assert.Equal(0.00m, r.Table12FUnreconciledItc.Amount);
    }

    /// <summary>
    /// 🔴 <b>The sibling walk (wave 9 review, fa).</b> <c>Gstr1Amendments.BuildTable9C</c> walks the same link list
    /// <c>Gstr1.BuildTable9B</c> does, and listed the purchase-linked note as an amended outward CDN (CDNRA) for the
    /// June return because its original purchase is in May. GSTR-1 is the statement of outward supplies in its
    /// amendment tables too. The sales-linked guard keeps its CDNRA row.
    /// </summary>
    [Theory]
    [InlineData(CdnType.Debit, false, 0)]
    [InlineData(CdnType.Credit, false, 0)]
    [InlineData(CdnType.Credit, true, 1)]
    public void Gstr1_table_9C_lists_no_amended_cdn_for_a_purchase_linked_note(CdnType type, bool linkToSale, int rows)
    {
        var c = Reload(Book(type, linkToSale));
        var june = Gstr1Amendments.Build(c, new DateOnly(2024, 6, 1), new DateOnly(2024, 6, 30));
        Assert.Equal(rows, june.Table9C.Count);   // branch a380a2c: 1 CDNRA row for the purchase-linked note
    }

    // ================================================================ helpers

    private static Company Book(CdnType type, bool linkToSale = false)
    {
        var c = CompanyFactory.CreateSeeded("Purchase Linked Note Co", FyFrom);
        var gst = new GstService(c);
        gst.EnableGst(new GstConfig
        {
            HomeStateCode = "27",
            Gstin = GstinOwn,
            RegistrationType = GstRegistrationType.Regular,
            ApplicableFrom = FyFrom,
            Periodicity = GstReturnPeriodicity.Monthly,
        });
        var ledgers = new LedgerService(c);
        var sales = Add(c, "Sales", "Sales Accounts", false);
        var purchases = Add(c, "Purchases", "Purchase Accounts", true);
        var debtor = Add(c, "Debtor", "Sundry Debtors", true);
        debtor.PartyGst = new PartyGstDetails { RegistrationType = GstRegistrationType.Regular, Gstin = GstinSupplier, StateCode = "27" };
        var creditor = Add(c, "Supplier", "Sundry Creditors", false);
        creditor.PartyGst = new PartyGstDetails { RegistrationType = GstRegistrationType.Regular, Gstin = GstinSupplier, StateCode = "27" };
        Guid TypeOf(VoucherBaseType b) => c.VoucherTypes.First(t => t.BaseType == b).Id;

        var saleTax = gst.ComputeInvoiceTax(new[] { new GstService.TaxableLine(Money.FromRupees(33_333m), 1800) }, false, GstTaxDirection.Output);
        var saleLines = new List<EntryLine>
        {
            new(debtor.Id, Money.FromRupees(33_333m + saleTax.TotalTax.Amount), DrCr.Debit),
            new(sales.Id, Money.FromRupees(33_333m), DrCr.Credit),
        };
        saleLines.AddRange(saleTax.TaxLines);
        var saleDate = new DateOnly(2024, 5, 10);
        var sale = new Voucher(Guid.NewGuid(), TypeOf(VoucherBaseType.Sales), saleDate, saleLines, partyId: debtor.Id);
        ledgers.Post(sale);

        var purTax = gst.ComputeInvoiceTax(new[] { new GstService.TaxableLine(Money.FromRupees(10_000m), 1800) }, false, GstTaxDirection.Input);
        Assert.Equal(1_800.00m, purTax.TotalTax.Amount);
        var purLines = new List<EntryLine>
        {
            new(purchases.Id, Money.FromRupees(10_000m), DrCr.Debit),
            new(creditor.Id, Money.FromRupees(10_000m + purTax.TotalTax.Amount), DrCr.Credit),
        };
        purLines.AddRange(purTax.TaxLines);
        var purDate = new DateOnly(2024, 5, 3);
        var purchase = new Voucher(Guid.NewGuid(), TypeOf(VoucherBaseType.Purchase), purDate, purLines, partyId: creditor.Id);
        ledgers.Post(purchase);

        var noteId = Guid.NewGuid();
        var noteDate = new DateOnly(2024, 6, 10);
        var noteBase = type == CdnType.Credit ? VoucherBaseType.CreditNote : VoucherBaseType.DebitNote;
        List<EntryLine> noteLines;
        Guid originalId;
        string originalNo;
        DateOnly originalDate;
        if (linkToSale)
        {
            // The engine's own outward shape: Output legs on the reducing (DEBIT) side.
            var posting = new CreditDebitNoteService(c).BuildCreditDebitNote(CdnType.Credit,
                new[] { new GstService.TaxableLine(Money.FromRupees(2_000m), 1800) }, interState: false, noteId,
                sale.Id, "S-1", saleDate, noteDate, "01 Sales return");
            noteLines = new List<EntryLine>
            {
                new(sales.Id, Money.FromRupees(2_000m), DrCr.Debit),
                new(debtor.Id, Money.FromRupees(2_360m), DrCr.Credit),
            };
            noteLines.AddRange(posting.TaxLines);
            ledgers.Post(new Voucher(noteId, TypeOf(noteBase), noteDate, noteLines, partyId: debtor.Id));
            return c;
        }

        // A note that adjusts a PURCHASE's ITC: INPUT legs on the reducing (CREDIT) side, and the §34 link record
        // pointing at the purchase. For a DEBIT note this is the voucher-entry shape (VoucherEntryViewModel →
        // ComputeInvoiceTax(Input, reverseSides: IsReturnNote)). 🔴 For a CREDIT note it is NOT: voucher entry gives a
        // Credit Note OUTPUT legs whatever it is linked to, and that shape is pinned separately by
        // Apex.Desktop.Tests.PurchaseLinkedCreditNoteEntryReloadTests (wave 9 review, fa). The Input-legged credit
        // note here is the imported / engine-built shape.
        var noteTax = gst.ComputeInvoiceTax(new[] { new GstService.TaxableLine(Money.FromRupees(2_000m), 1800) }, false, GstTaxDirection.Input, reverseSides: true);
        Assert.Equal(360.00m, noteTax.TotalTax.Amount);
        Assert.All(noteTax.TaxLines, l => Assert.Equal(DrCr.Credit, l.Side));
        noteLines = new List<EntryLine>
        {
            new(creditor.Id, Money.FromRupees(2_360m), DrCr.Debit),
            new(purchases.Id, Money.FromRupees(2_000m), DrCr.Credit),
        };
        noteLines.AddRange(noteTax.TaxLines);
        originalId = purchase.Id; originalNo = "P-1"; originalDate = purDate;
        c.AddCreditDebitNoteLink(new GstCreditDebitNoteLink(Guid.NewGuid(), noteId, type, originalId, originalNo,
            originalDate, type == CdnType.Credit ? "01 Sales return" : "04 Correction in invoice"));
        ledgers.Post(new Voucher(noteId, TypeOf(noteBase), noteDate, noteLines, partyId: creditor.Id));
        return c;
    }

    private static Company Reload(Company original)
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"apex-9c-purlink-{Guid.NewGuid():N}.db");
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
