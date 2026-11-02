using System.Text.Json;
using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Io;
using Apex.Ledger.Services;
using Apex.Persistence.Sqlite;
using Xunit;
using Domain = Apex.Ledger.Domain;

namespace Apex.Persistence.Sqlite.Tests;

/// <summary>
/// 🔴 <b>WAVE 10 `ga` REVIEW — THE GUARDS ON THE TWO NEW LINKED-NOTE WALKS WERE DEAD (census 6.12, IV-297 / IV-298).</b>
/// <c>Gstr1.BuildTable9B</c> now nets each linked §34 note into Table 12 and the exempt bucket (T2-167), and
/// <c>Gstr3b.ExemptOutwardValue</c> nets its exempt value into 3.1(c) (T2-166). Each walk carries guards copied from its
/// voucher sweep — skip a note linked to a PURCHASE (T2-162), skip an outward REVERSE-CHARGE note — and the reviewer
/// deleted each guard in turn: every existing test stayed green. These two tests pin them on the JSON bytes of a book
/// reloaded through a fresh <see cref="SqliteCompanyStore"/>, odd paisa.
/// <list type="bullet">
/// <item><b>RM5</b> (the inward-linked skip in the 3B walk deleted) reddens
/// <see cref="An_exempt_credit_note_linked_to_a_purchase_moves_neither_3_1_c_nor_the_gstr1_exempt_bucket"/>.</item>
/// <item><b>RM3</b> (the GSTR-1 reverse-charge guard deleted) and <b>RM4</b> (the 3B reverse-charge guard deleted) each
/// redden <see cref="A_linked_credit_note_of_an_outward_reverse_charge_supply_files_no_hsn_row_and_no_exempt_value"/>.</item>
/// </list>
/// </summary>
public sealed class LinkedNoteGuardReloadTests
{
    private static readonly DateOnly FyFrom = new(2024, 4, 1);
    private static readonly DateOnly FyTo = new(2025, 3, 31);
    private const string GstinOwn = "27AAPFU0939F1ZV";
    private const string GstinOther = "27AAACR5055K1Z5";

    /// <summary>
    /// A credit note linked to a PURCHASE is the supplier's document (T2-162): GSTR-1 skips it and so must 3.1(c). The
    /// book holds one exempt SALE (7 @ 71.43 = 500.01) and an exempt credit note (3 @ 71.43 = 214.29) linked to an
    /// exempt purchase. Both returns must file 500.01, and agree.
    /// </summary>
    [Fact]
    public void An_exempt_credit_note_linked_to_a_purchase_moves_neither_3_1_c_nor_the_gstr1_exempt_bucket()
    {
        var c = Book(out var item, out var sales, out var party, out var ledgers, exemptItem: true);
        Guid TypeOf(VoucherBaseType b) => c.VoucherTypes.First(t => t.BaseType == b).Id;
        var purchases = Add(c, "Purchases", "Purchase Accounts", true);
        var supplier = Add(c, "Supplier", "Sundry Creditors", false);
        supplier.PartyGst = new PartyGstDetails
        { RegistrationType = GstRegistrationType.Regular, Gstin = GstinOther, StateCode = "27" };

        ledgers.Post(new Voucher(Guid.NewGuid(), TypeOf(VoucherBaseType.Sales), new DateOnly(2024, 5, 10),
            new List<EntryLine> { new(party.Id, new Money(500.01m), DrCr.Debit), new(sales.Id, new Money(500.01m), DrCr.Credit) },
            number: 1, partyId: party.Id,
            inventoryLines: new[] { new VoucherInventoryLine(item.Id, c.MainLocation!.Id, 7m, new Money(71.43m)) }));
        var purchaseDate = new DateOnly(2024, 5, 12);
        var purchase = ledgers.Post(new Voucher(Guid.NewGuid(), TypeOf(VoucherBaseType.Purchase), purchaseDate,
            new List<EntryLine> { new(purchases.Id, new Money(500.01m), DrCr.Debit), new(supplier.Id, new Money(500.01m), DrCr.Credit) },
            partyId: supplier.Id, referenceNo: "SUP-9", referenceDate: new DateOnly(2024, 5, 12),
            inventoryLines: new[] { new VoucherInventoryLine(item.Id, c.MainLocation!.Id, 7m, new Money(71.43m)) }));
        var noteId = Guid.NewGuid();
        c.AddCreditDebitNoteLink(new GstCreditDebitNoteLink(
            Guid.NewGuid(), noteId, CdnType.Credit, purchase.Id, "SUP-9", purchaseDate, "01 Sales return"));
        ledgers.Post(new Voucher(noteId, TypeOf(VoucherBaseType.CreditNote), new DateOnly(2024, 6, 10),
            new List<EntryLine> { new(sales.Id, new Money(214.29m), DrCr.Debit), new(supplier.Id, new Money(214.29m), DrCr.Credit) },
            partyId: supplier.Id,
            inventoryLines: new[] { new VoucherInventoryLine(item.Id, c.MainLocation!.Id, 3m, new Money(71.43m)) }));

        var r = Reload(c);
        Assert.True(Apex.Ledger.Reports.GstReportSupport.IsInwardLinkedCreditDebitNote(r, r.FindVoucher(noteId)!));
        using var g3b = JsonDocument.Parse(GstReturnJson.Gstr3b(r, FyFrom, FyTo));
        using var g1 = JsonDocument.Parse(GstReturnJson.Gstr1(r, FyFrom, FyTo));
        Assert.Equal(50_001L, g1.RootElement.GetProperty("nil_exempt_nongst_paisa").GetInt64());
        Assert.Equal(50_001L, g3b.RootElement.GetProperty("tbl3_1c_nil_exempt_nongst_paisa").GetInt64()); // RM5: 28,572
    }

    /// <summary>
    /// An outward REVERSE-CHARGE supply files in GSTR-1 4B only — never Table 12, never the exempt bucket, never 3.1(c)
    /// — and its linked credit note must stay out the same way. Sale 7 @ 123.45 = 864.15 under a reverse-charge sales
    /// ledger (no forward tax), linked credit note 3 @ 123.45 = 370.35.
    /// </summary>
    [Fact]
    public void A_linked_credit_note_of_an_outward_reverse_charge_supply_files_no_hsn_row_and_no_exempt_value()
    {
        var c = Book(out var item, out var sales, out var party, out var ledgers, exemptItem: false);
        Guid TypeOf(VoucherBaseType b) => c.VoucherTypes.First(t => t.BaseType == b).Id;
        sales.SalesPurchaseGst = new StockItemGstDetails
        {
            HsnSac = "847130", Taxability = GstTaxability.Taxable, RateBasisPoints = 1800,
            SupplyType = GstSupplyType.Goods, ReverseChargeApplicable = true,
        };

        var saleDate = new DateOnly(2024, 5, 10);
        var sale = ledgers.Post(new Voucher(Guid.NewGuid(), TypeOf(VoucherBaseType.Sales), saleDate,
            new List<EntryLine> { new(party.Id, new Money(864.15m), DrCr.Debit), new(sales.Id, new Money(864.15m), DrCr.Credit) },
            number: 1, partyId: party.Id,
            inventoryLines: new[] { new VoucherInventoryLine(item.Id, c.MainLocation!.Id, 7m, new Money(123.45m)) }));
        var noteId = Guid.NewGuid();
        c.AddCreditDebitNoteLink(new GstCreditDebitNoteLink(
            Guid.NewGuid(), noteId, CdnType.Credit, sale.Id, "1", saleDate, "01 Sales return"));
        ledgers.Post(new Voucher(noteId, TypeOf(VoucherBaseType.CreditNote), new DateOnly(2024, 6, 10),
            new List<EntryLine> { new(sales.Id, new Money(370.35m), DrCr.Debit), new(party.Id, new Money(370.35m), DrCr.Credit) },
            partyId: party.Id,
            inventoryLines: new[] { new VoucherInventoryLine(item.Id, c.MainLocation!.Id, 3m, new Money(123.45m)) }));

        var r = Reload(c);
        Assert.True(Apex.Ledger.Reports.GstReportSupport.IsOutwardReverseChargeSupply(r, r.FindVoucher(noteId)!));
        using var g1 = JsonDocument.Parse(GstReturnJson.Gstr1(r, FyFrom, FyTo));
        Assert.Empty(g1.RootElement.GetProperty("hsn").EnumerateArray());                                   // RM3: −37,035 row
        Assert.Equal(0L, g1.RootElement.GetProperty("nil_exempt_nongst_paisa").GetInt64());
        using var g3b = JsonDocument.Parse(GstReturnJson.Gstr3b(r, FyFrom, FyTo));
        Assert.Equal(0L, g3b.RootElement.GetProperty("tbl3_1c_nil_exempt_nongst_paisa").GetInt64());     // RM4: −37,035
    }

    // ================================================================ fixture

    private static Company Book(out StockItem item, out Domain.Ledger sales, out Domain.Ledger party,
        out LedgerService ledgers, bool exemptItem)
    {
        var c = CompanyFactory.CreateSeeded("Linked Note Guard Co", FyFrom);
        new GstService(c).EnableGst(new GstConfig
        {
            HomeStateCode = "27", Gstin = GstinOwn, RegistrationType = GstRegistrationType.Regular,
            ApplicableFrom = FyFrom, Periodicity = GstReturnPeriodicity.Monthly,
        });
        var inv = new InventoryService(c);
        var grp = inv.CreateStockGroup("Goods");
        var nos = inv.CreateSimpleUnit("Nos", "Numbers", unitQuantityCode: "NOS");
        item = inv.CreateStockItem(exemptItem ? "Fresh Milk" : "Widget", grp.Id, nos.Id);
        item.Gst = exemptItem
            ? new StockItemGstDetails { HsnSac = "040110", Taxability = GstTaxability.Exempt }
            : new StockItemGstDetails { HsnSac = "847130", Taxability = GstTaxability.Taxable, RateBasisPoints = 1800 };
        inv.AddOpeningBalance(item.Id, c.MainLocation!.Id, 100m, Money.FromRupees(5_000m));
        ledgers = new LedgerService(c);
        sales = Add(c, "Sales", "Sales Accounts", false);
        party = Add(c, "Buyer", "Sundry Debtors", true);
        party.PartyGst = new PartyGstDetails
        { RegistrationType = GstRegistrationType.Regular, Gstin = "27AAACB2894G1ZJ", StateCode = "27" };
        return c;
    }

    private static Company Reload(Company original)
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"apex-linked-note-guard-{Guid.NewGuid():N}.db");
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
