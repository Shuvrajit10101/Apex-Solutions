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
/// 🔴 <b>WAVE 10 `ga` VERIFIER — THE REGISTRATION FILTER ON THE NEW GSTR-3B LINK WALK WAS DEAD (census 6.12, IV-298,
/// T2-166; census 6.23).</b> <c>Gstr3b.ExemptOutwardValue</c> now nets each linked §34 note's exempt value into 3.1(c)
/// off its own walk of <c>company.CreditDebitNoteLinks</c>, so it carries its own copy of the registration filter: the
/// link collection is not the voucher funnel, so nothing upstream scopes it. The verifier deleted that filter and every
/// test in <c>Apex.Ledger.Tests</c> and <c>Apex.Persistence.Sqlite.Tests</c> stayed green — so a Gujarat credit note
/// could net down the Maharashtra return's 3.1(c), and the two returns filed under one PAN would disagree with GSTR-1
/// for both GSTINs. (The walk's date floor and <c>CountsAsOf</c> ceiling are NOT dead: deleting either reddens
/// <c>LinkedNoteHsnAndExemptReloadTests.An_exempt_sales_linked_credit_note_reduces_3_1_c_so_9c_5r_is_zero</c> through
/// GSTR-9's month-by-month fold. The reverse-charge and purchase-linked guards are pinned by
/// <see cref="LinkedNoteGuardReloadTests"/>.)
/// <para>Pinned on the GSTR-1 and GSTR-3B JSON bytes of a book reloaded through a fresh <see cref="SqliteCompanyStore"/>,
/// odd paisa, asserting the two returns AGREE per registration (GSTR-1's walk shares its registration filter with the
/// note's tax, T2-119, so it is the control). <b>FM4</b> (the filter deleted) reddens
/// <see cref="An_exempt_linked_note_under_another_registration_does_not_move_this_registrations_3_1_c"/>: Maharashtra
/// 3.1(c) 14,286, expected 35,715.</para>
/// </summary>
public sealed class LinkedNoteRegistrationScopeReloadTests
{
    private static readonly DateOnly FyFrom = new(2024, 4, 1);
    private static readonly DateOnly MayFrom = new(2024, 5, 1);
    private static readonly DateOnly MayTo = new(2024, 5, 31);
    private const string GstinOwn = "27AAPFU0939F1ZV";
    private const string GstinGujarat = "24AAACC1206D1ZM";

    /// <summary>
    /// A Maharashtra company that also holds a Gujarat registration. Maharashtra: exempt sale 5 @ 71.43 = 357.15, no
    /// note. Gujarat: exempt sale 7 @ 71.43 = 500.01 and its linked credit note 3 @ 71.43 = 214.29, both stamped with
    /// the Gujarat registration. The Maharashtra return files 357.15; the Gujarat one 285.72.
    /// </summary>
    [Fact]
    public void An_exempt_linked_note_under_another_registration_does_not_move_this_registrations_3_1_c()
    {
        var c = Book(out var item, out var sales, out var party, out var ledgers);
        var gujarat = new GstRegistration(
            Guid.NewGuid(), "Gujarat Registration", "24", GstinGujarat,
            GstRegistrationType.Regular, FyFrom, GstReturnPeriodicity.Monthly);
        c.Gst!.AddRegistration(gujarat);
        var gjParty = Add(c, "Gujarat Buyer", "Sundry Debtors", true);
        gjParty.PartyGst = new PartyGstDetails
        { RegistrationType = GstRegistrationType.Regular, Gstin = "24AAACB2894G1ZM", StateCode = "24" };

        Sale(c, ledgers, item, sales, party, new DateOnly(2024, 5, 10), 5m, number: 1);
        var gjSale = Sale(c, ledgers, item, sales, gjParty, new DateOnly(2024, 5, 11), 7m, number: 2, gujarat.Id);
        Note(c, ledgers, item, sales, gjParty, gjSale, new DateOnly(2024, 5, 20), 3m, gujarat.Id);

        var r = Reload(c);
        using var mh1 = JsonDocument.Parse(GstReturnJson.Gstr1(r, MayFrom, MayTo, GstRegistration.PrimaryId));
        using var mh3b = JsonDocument.Parse(GstReturnJson.Gstr3b(r, MayFrom, MayTo, GstRegistration.PrimaryId));
        Assert.Equal(35_715L, mh1.RootElement.GetProperty("nil_exempt_nongst_paisa").GetInt64());
        Assert.Equal(35_715L, mh3b.RootElement.GetProperty("tbl3_1c_nil_exempt_nongst_paisa").GetInt64()); // FM4: 14,286
        using var gj1 = JsonDocument.Parse(GstReturnJson.Gstr1(r, MayFrom, MayTo, gujarat.Id));
        using var gj3b = JsonDocument.Parse(GstReturnJson.Gstr3b(r, MayFrom, MayTo, gujarat.Id));
        Assert.Equal(28_572L, gj1.RootElement.GetProperty("nil_exempt_nongst_paisa").GetInt64());
        Assert.Equal(28_572L, gj3b.RootElement.GetProperty("tbl3_1c_nil_exempt_nongst_paisa").GetInt64());
    }

    // ================================================================ fixture

    private static Voucher Sale(Company c, LedgerService ledgers, StockItem item, Domain.Ledger sales,
        Domain.Ledger party, DateOnly date, decimal qty, int number, Guid? registrationId = null)
    {
        var value = new Money(qty * 71.43m);
        var v = new Voucher(Guid.NewGuid(), TypeOf(c, VoucherBaseType.Sales), date,
            new List<EntryLine> { new(party.Id, value, DrCr.Debit), new(sales.Id, value, DrCr.Credit) },
            number: number, partyId: party.Id,
            inventoryLines: new[] { new VoucherInventoryLine(item.Id, c.MainLocation!.Id, qty, new Money(71.43m)) });
        if (registrationId is { } reg) v.GstRegistrationId = reg;
        return ledgers.Post(v);
    }

    private static void Note(Company c, LedgerService ledgers, StockItem item, Domain.Ledger sales,
        Domain.Ledger party, Voucher original, DateOnly date, decimal qty, Guid? registrationId = null)
    {
        var value = new Money(qty * 71.43m);
        var noteId = Guid.NewGuid();
        c.AddCreditDebitNoteLink(new GstCreditDebitNoteLink(
            Guid.NewGuid(), noteId, CdnType.Credit, original.Id, original.Number.ToString(), original.Date,
            "01 Sales return"));
        var v = new Voucher(noteId, TypeOf(c, VoucherBaseType.CreditNote), date,
            new List<EntryLine> { new(sales.Id, value, DrCr.Debit), new(party.Id, value, DrCr.Credit) },
            partyId: party.Id,
            inventoryLines: new[] { new VoucherInventoryLine(item.Id, c.MainLocation!.Id, qty, new Money(71.43m)) });
        if (registrationId is { } reg) v.GstRegistrationId = reg;
        ledgers.Post(v);
    }

    private static Guid TypeOf(Company c, VoucherBaseType b) => c.VoucherTypes.First(t => t.BaseType == b).Id;

    private static Company Book(out StockItem item, out Domain.Ledger sales, out Domain.Ledger party,
        out LedgerService ledgers)
    {
        var c = CompanyFactory.CreateSeeded("Linked Note Filter Co", FyFrom);
        new GstService(c).EnableGst(new GstConfig
        {
            HomeStateCode = "27", Gstin = GstinOwn, RegistrationType = GstRegistrationType.Regular,
            ApplicableFrom = FyFrom, Periodicity = GstReturnPeriodicity.Monthly,
        });
        var inv = new InventoryService(c);
        var grp = inv.CreateStockGroup("Goods");
        var nos = inv.CreateSimpleUnit("Nos", "Numbers", unitQuantityCode: "NOS");
        item = inv.CreateStockItem("Fresh Milk", grp.Id, nos.Id);
        item.Gst = new StockItemGstDetails { HsnSac = "040110", Taxability = GstTaxability.Exempt };
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
        var dbPath = Path.Combine(Path.GetTempPath(), $"apex-linked-note-filter-{Guid.NewGuid():N}.db");
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
