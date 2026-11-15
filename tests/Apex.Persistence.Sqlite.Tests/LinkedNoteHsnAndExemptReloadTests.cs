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
/// 🔴 <b>CENSUS 6.12, <c>T2-167</c> AND <c>T2-166</c> (wave 10, track ga, §2X) — A §34 NOTE LINKED TO A SALE NETS IN
/// THE HSN SUMMARY AND IN THE EXEMPT TURNOVER, NOT ONLY IN THE TAX.</b>
///
/// <para><b><c>T2-167</c>, on <c>origin/main</c> fdc94d7.</b> The GSTR-1 main sweep <c>continue</c>s on every linked
/// note (so its tax is folded once, by <c>BuildTable9B</c>), and nothing else netted the note into Table 12. A credit
/// note linked to a sale therefore reduced GSTR-3B 3.1(a) and the GSTR-1 header but not the HSN summary, and GSTR-9
/// Table 17 (Σ Table 12) filed CGST 77.78 beside Table 4 / 3.1(a) 44.45 — one annual return contradicting itself.</para>
///
/// <para><b><c>T2-166</c>.</b> <c>Gstr3b.ExemptOutwardValue</c> <c>continue</c>s on every linked note, and
/// <c>ReadCdn</c> reads only the TAX lines' taxable value — 0 for an exempt line — so an exempt credit note linked to
/// a sale reduced 3.1(c) by nothing while the books netted it: GSTR-9C 5R −370.35.</para>
///
/// <para>Odd-paisa figures (7 and 3 @ ₹123.45, so the 18% tax splits 77.78 / 77.77), and every assertion is read off
/// the emitted JSON bytes of a book saved and re-loaded through a <b>fresh</b> <see cref="SqliteCompanyStore"/>.</para>
/// </summary>
public sealed class LinkedNoteHsnAndExemptReloadTests
{
    private static readonly DateOnly FyFrom = new(2024, 4, 1);
    private static readonly DateOnly FyTo = new(2025, 3, 31);
    private const string GstinOwn = "27AAPFU0939F1ZV";
    private const string GstinBuyer = "27AAACR5055K1Z5";
    private const string WidgetHsn = "847130";
    private const string MilkHsn = "040110";

    // ================================================================ T2-167

    /// <summary>
    /// GSTR-9 Table 17 == Table 4 == GSTR-3B 3.1(a) on a taxed sale and a credit note linked to it, and Table 12 carries
    /// the NET row: 864.15 − 370.35 = 493.80 taxable, CGST 77.78 − 33.33 = 44.45, SGST 77.77 − 33.33 = 44.44, 4 Nos.
    /// On main Table 17 / Table 12 read 864.15 / 77.78 / 77.77 / 7.
    /// </summary>
    [Fact]
    public void A_sales_linked_credit_note_nets_in_table_12_so_table_17_equals_3_1_a()
    {
        var c = Reload(Book(exempt: false));
        Assert.NotNull(Assert.Single(c.CreditDebitNoteLinks).OriginalInvoiceVoucherId); // the linked shape survived

        using var g3b = JsonDocument.Parse(GstReturnJson.Gstr3b(c, FyFrom, FyTo));
        Assert.Equal(4_445L, g3b.RootElement.GetProperty("tbl3_1a_camt_paisa").GetInt64());
        Assert.Equal(4_444L, g3b.RootElement.GetProperty("tbl3_1a_samt_paisa").GetInt64());

        using var g1 = JsonDocument.Parse(GstReturnJson.Gstr1(c, FyFrom, FyTo));
        var row = Assert.Single(g1.RootElement.GetProperty("hsn").EnumerateArray());
        Assert.Equal(WidgetHsn, row.GetProperty("hsn_sac").GetString());
        Assert.Equal(49_380L, row.GetProperty("txval_paisa").GetInt64());     // main: 86,415
        Assert.Equal(4_445L, row.GetProperty("camt_paisa").GetInt64());       // main: 7,778
        Assert.Equal(4_444L, row.GetProperty("samt_paisa").GetInt64());       // main: 7,777
        Assert.Equal(4m, row.GetProperty("qty").GetDecimal());                // main: 7

        using var g9 = JsonDocument.Parse(GstReturnJson.Gstr9(c, FyFrom, FyTo));
        var t17 = Assert.Single(g9.RootElement.GetProperty("tbl17_hsn").EnumerateArray());
        Assert.Equal(49_380L, t17.GetProperty("taxable_value_paisa").GetInt64());
        Assert.Equal(4_445L, t17.GetProperty("cgst_paisa").GetInt64());
        Assert.Equal(4_444L, t17.GetProperty("sgst_paisa").GetInt64());
        // Table 17 == Table 4 == 3.1(a), head by head and in total.
        Assert.Equal(g9.RootElement.GetProperty("tbl4_out_cgst_paisa").GetInt64(), t17.GetProperty("cgst_paisa").GetInt64());
        Assert.Equal(g9.RootElement.GetProperty("tbl4_out_sgst_paisa").GetInt64(), t17.GetProperty("sgst_paisa").GetInt64());
        Assert.Equal(g9.RootElement.GetProperty("tbl4n_taxable_value_paisa").GetInt64(),
            t17.GetProperty("taxable_value_paisa").GetInt64());
        Assert.Equal(g9.RootElement.GetProperty("tbl4_total_tax_paisa").GetInt64(),
            t17.GetProperty("cgst_paisa").GetInt64() + t17.GetProperty("sgst_paisa").GetInt64()
            + t17.GetProperty("igst_paisa").GetInt64());

        // The note is still declared once, in 9B, and its tax is still folded once into the header (no double count).
        Assert.Equal(1, g1.RootElement.GetProperty("cdnr").GetArrayLength());
        var r = Gstr9c.Build(c, FyFrom, FyTo);
        Assert.Equal(0.00m, r.Table11UnreconciledTax.Amount);
        Assert.Equal(0.00m, r.Table5RUnreconciledTurnover.Amount);
    }

    // ================================================================ T2-166

    /// <summary>
    /// An EXEMPT sale and an exempt credit note linked to it: 3.1(c) files 864.15 − 370.35 = 493.80, GSTR-1's
    /// exempt bucket agrees, Table 12 nets the exempt HSN row, and GSTR-9C 5R is 0. On main 3.1(c) and the GSTR-1
    /// bucket read 864.15 and 5R −370.35.
    /// </summary>
    [Fact]
    public void An_exempt_sales_linked_credit_note_reduces_3_1_c_so_9c_5r_is_zero()
    {
        var c = Reload(Book(exempt: true));
        Assert.NotNull(Assert.Single(c.CreditDebitNoteLinks).OriginalInvoiceVoucherId);

        using var g3b = JsonDocument.Parse(GstReturnJson.Gstr3b(c, FyFrom, FyTo));
        Assert.Equal(49_380L, g3b.RootElement.GetProperty("tbl3_1c_nil_exempt_nongst_paisa").GetInt64()); // main: 86,415
        Assert.Equal(0L, g3b.RootElement.GetProperty("tbl3_1a_camt_paisa").GetInt64());

        using var g1 = JsonDocument.Parse(GstReturnJson.Gstr1(c, FyFrom, FyTo));
        Assert.Equal(49_380L, g1.RootElement.GetProperty("nil_exempt_nongst_paisa").GetInt64());          // main: 86,415
        var row = Assert.Single(g1.RootElement.GetProperty("hsn").EnumerateArray());
        Assert.Equal(MilkHsn, row.GetProperty("hsn_sac").GetString());
        Assert.Equal(49_380L, row.GetProperty("txval_paisa").GetInt64());
        Assert.Equal(4m, row.GetProperty("qty").GetDecimal());

        using var g9 = JsonDocument.Parse(GstReturnJson.Gstr9(c, FyFrom, FyTo));
        Assert.Equal(49_380L, g9.RootElement.GetProperty("tbl5_exempt_paisa").GetInt64());

        using var g9c = JsonDocument.Parse(GstReturnJson.Gstr9c(c, FyFrom, FyTo));
        Assert.Equal(0L, g9c.RootElement.GetProperty("tbl5r_unreconciled_turnover_paisa").GetInt64());     // main: −37,035
    }

    /// <summary>
    /// The control: an UNLINKED exempt credit note already reduced 3.1(c) on main (T2-93's sign), and still does —
    /// by exactly the same figure, so the linked and unlinked shapes of one return now agree.
    /// </summary>
    [Fact]
    public void An_unlinked_exempt_credit_note_files_the_same_3_1_c_as_the_linked_one()
    {
        var c = Reload(Book(exempt: true, link: false));
        Assert.Empty(c.CreditDebitNoteLinks);
        using var g3b = JsonDocument.Parse(GstReturnJson.Gstr3b(c, FyFrom, FyTo));
        Assert.Equal(49_380L, g3b.RootElement.GetProperty("tbl3_1c_nil_exempt_nongst_paisa").GetInt64());
        using var g1 = JsonDocument.Parse(GstReturnJson.Gstr1(c, FyFrom, FyTo));
        Assert.Equal(49_380L, g1.RootElement.GetProperty("nil_exempt_nongst_paisa").GetInt64());
    }

    // ================================================================ fixture

    private static Company Book(bool exempt, bool link = true)
    {
        var c = CompanyFactory.CreateSeeded("Linked Note HSN Co", FyFrom);
        var gst = new GstService(c);
        gst.EnableGst(new GstConfig
        {
            HomeStateCode = "27", Gstin = GstinOwn, RegistrationType = GstRegistrationType.Regular,
            ApplicableFrom = FyFrom, Periodicity = GstReturnPeriodicity.Monthly,
        });

        var inv = new InventoryService(c);
        var grp = inv.CreateStockGroup("Goods");
        var nos = inv.CreateSimpleUnit("Nos", "Numbers", unitQuantityCode: "NOS");
        var item = inv.CreateStockItem(exempt ? "Fresh Milk" : "Widget", grp.Id, nos.Id);
        item.Gst = exempt
            ? new StockItemGstDetails { HsnSac = MilkHsn, Taxability = GstTaxability.Exempt }
            : new StockItemGstDetails { HsnSac = WidgetHsn, Taxability = GstTaxability.Taxable, RateBasisPoints = 1800 };
        inv.AddOpeningBalance(item.Id, c.MainLocation!.Id, 100m, Money.FromRupees(5_000m));

        var ledgers = new LedgerService(c);
        var sales = Add(c, "Sales", "Sales Accounts", false);
        var debtor = Add(c, "Buyer", "Sundry Debtors", true);
        debtor.PartyGst = new PartyGstDetails
        { RegistrationType = GstRegistrationType.Regular, Gstin = GstinBuyer, StateCode = "27" };
        Guid TypeOf(VoucherBaseType b) => c.VoucherTypes.First(t => t.BaseType == b).Id;

        // Sale: 7 @ 123.45 = 864.15.
        const decimal saleValue = 864.15m;
        var saleTax = exempt ? null
            : gst.ComputeInvoiceTax(new[] { new GstService.TaxableLine(new Money(saleValue), 1800) }, false,
                GstTaxDirection.Output);
        var saleTaxTotal = saleTax?.TotalTax.Amount ?? 0m;
        if (!exempt) Assert.Equal(155.55m, saleTaxTotal);
        var saleLines = new List<EntryLine>
        {
            new(debtor.Id, new Money(saleValue + saleTaxTotal), DrCr.Debit),
            new(sales.Id, new Money(saleValue), DrCr.Credit),
        };
        if (saleTax is not null) saleLines.AddRange(saleTax.TaxLines);
        var saleDate = new DateOnly(2024, 5, 10);
        var sale = ledgers.Post(new Voucher(Guid.NewGuid(), TypeOf(VoucherBaseType.Sales), saleDate, saleLines,
            number: 1, partyId: debtor.Id,
            inventoryLines: new[] { new VoucherInventoryLine(item.Id, c.MainLocation!.Id, 7m, new Money(123.45m)) }));

        // Credit note: 3 @ 123.45 = 370.35 returned — the engine's outward shape, Output legs on the DEBIT side.
        const decimal noteValue = 370.35m;
        var noteTax = exempt ? null
            : gst.ComputeInvoiceTax(new[] { new GstService.TaxableLine(new Money(noteValue), 1800) }, false,
                GstTaxDirection.Output, reverseSides: true);
        var noteTaxTotal = noteTax?.TotalTax.Amount ?? 0m;
        if (!exempt) Assert.Equal(66.66m, noteTaxTotal);
        var noteLines = new List<EntryLine>
        {
            new(sales.Id, new Money(noteValue), DrCr.Debit),
            new(debtor.Id, new Money(noteValue + noteTaxTotal), DrCr.Credit),
        };
        if (noteTax is not null) noteLines.AddRange(noteTax.TaxLines);
        var noteId = Guid.NewGuid();
        var noteDate = new DateOnly(2024, 6, 10);
        if (link)
            c.AddCreditDebitNoteLink(new GstCreditDebitNoteLink(
                Guid.NewGuid(), noteId, CdnType.Credit, sale.Id, "1", saleDate, "01 Sales return"));
        ledgers.Post(new Voucher(noteId, TypeOf(VoucherBaseType.CreditNote), noteDate, noteLines,
            partyId: debtor.Id,
            inventoryLines: new[] { new VoucherInventoryLine(item.Id, c.MainLocation!.Id, 3m, new Money(123.45m)) }));
        return c;
    }

    private static Company Reload(Company original)
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"apex-linked-note-hsn-{Guid.NewGuid():N}.db");
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
