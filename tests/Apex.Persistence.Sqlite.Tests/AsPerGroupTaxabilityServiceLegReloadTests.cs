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
/// 🔴 <b>WAVE 12, TRACK ha, IV-316 — A SERVICE LEDGER WHOSE BLOCK DECLARES NO RATE TAKES ITS TAXABILITY FROM THE
/// GROUP.</b> The Ledger master (IV-315) writes a block whose taxability defaults to Taxable; a blank rate is this
/// screen's "As per Company/Group". Under a group declaring Exempt / 999293, the rate walk resolved the supply Exempt
/// and the sale posted no tax, yet <c>Gstr1.IsNonTaxableServiceLedger</c> read only the ledger's own "Taxable": the
/// sale was filed NOWHERE. Measured on a probe book before the fix: GSTR-1 exempt 0, no Table 12 row, 3.1(c) 0,
/// 3.1(a) 0, GSTR-9C 5R 123,457 paisa.
///
/// <para>Every figure is read off the emitted JSON bytes of a book saved and re-loaded through a fresh
/// <see cref="SqliteCompanyStore"/>. Odd paisa: ₹1,234.57.</para>
/// <para><b>Mutation H9</b> (the new rung in <c>IsNonTaxableServiceLedger</c> removed, so a Taxable block with no rate
/// returns false as on the branch before) reddens
/// <see cref="A_rate_less_taxable_ledger_under_an_exempt_group_is_filed_as_exempt_in_every_return"/>.
/// <b>Mutation H10</b> (the <c>IsUnresolved</c> guard dropped) reddens
/// <see cref="A_rate_less_taxable_ledger_with_no_rate_anywhere_is_not_an_exemption"/>.</para>
/// </summary>
public sealed class AsPerGroupTaxabilityServiceLegReloadTests
{
    private static readonly DateOnly FyFrom = new(2024, 4, 1);
    private static readonly DateOnly FyTo = new(2025, 3, 31);

    [Fact]
    public void A_rate_less_taxable_ledger_under_an_exempt_group_is_filed_as_exempt_in_every_return()
    {
        var c = Reload(Book(groupExempt: true));

        using var g1 = JsonDocument.Parse(GstReturnJson.Gstr1(c, FyFrom, FyTo));
        Assert.Equal(123_457L, g1.RootElement.GetProperty("nil_exempt_nongst_paisa").GetInt64());     // was 0
        var row = Assert.Single(g1.RootElement.GetProperty("hsn").EnumerateArray());                   // was none
        Assert.Equal("999293", row.GetProperty("hsn_sac").GetString());
        Assert.Equal(123_457L, row.GetProperty("txval_paisa").GetInt64());
        using var g3b = JsonDocument.Parse(GstReturnJson.Gstr3b(c, FyFrom, FyTo));
        Assert.Equal(123_457L, g3b.RootElement.GetProperty("tbl3_1c_nil_exempt_nongst_paisa").GetInt64()); // was 0
        using var g9 = JsonDocument.Parse(GstReturnJson.Gstr9(c, FyFrom, FyTo));
        Assert.Equal(123_457L, g9.RootElement.GetProperty("tbl5_exempt_paisa").GetInt64());
        using var g9c = JsonDocument.Parse(GstReturnJson.Gstr9c(c, FyFrom, FyTo));
        Assert.Equal(0L, g9c.RootElement.GetProperty("tbl5r_unreconciled_turnover_paisa").GetInt64()); // was 123,457
    }

    /// <summary>ER-5 control: the same rate-less Taxable ledger under a group and a company that declare nothing.
    /// The walk is UNRESOLVED, and silence is not an exemption: neither return files it as exempt.</summary>
    [Fact]
    public void A_rate_less_taxable_ledger_with_no_rate_anywhere_is_not_an_exemption()
    {
        var c = Reload(Book(groupExempt: false));
        using var g1 = JsonDocument.Parse(GstReturnJson.Gstr1(c, FyFrom, FyTo));
        Assert.Equal(0L, g1.RootElement.GetProperty("nil_exempt_nongst_paisa").GetInt64());
        using var g3b = JsonDocument.Parse(GstReturnJson.Gstr3b(c, FyFrom, FyTo));
        Assert.Equal(0L, g3b.RootElement.GetProperty("tbl3_1c_nil_exempt_nongst_paisa").GetInt64());
    }

    private static Company Book(bool groupExempt)
    {
        var c = CompanyFactory.CreateSeeded("As Per Group Taxability Co", FyFrom);
        new GstService(c).EnableGst(new GstConfig
        {
            HomeStateCode = "27", Gstin = "27AAPFU0939F1ZV", RegistrationType = GstRegistrationType.Regular,
            ApplicableFrom = FyFrom, Periodicity = GstReturnPeriodicity.Monthly,
        });
        var sales = c.FindGroupByName("Sales Accounts")!;
        if (groupExempt)
            sales.Gst = new MasterGstDetails { HsnSac = "999293", Taxability = GstTaxability.Exempt };
        // Exactly what the Ledger master writes for GST Applicable + Services with HSN/SAC and rate left blank.
        var education = new Domain.Ledger(Guid.NewGuid(), "Education Income", sales.Id, Money.Zero, false)
        {
            SalesPurchaseGst = new StockItemGstDetails
            { Taxability = GstTaxability.Taxable, SupplyType = GstSupplyType.Services },
        };
        c.AddLedger(education);
        var buyer = new Domain.Ledger(Guid.NewGuid(), "Buyer", c.FindGroupByName("Sundry Debtors")!.Id, Money.Zero, true)
        {
            PartyGst = new PartyGstDetails
            { RegistrationType = GstRegistrationType.Regular, Gstin = "27AAACR5055K1Z5", StateCode = "27" },
        };
        c.AddLedger(buyer);
        var sale = new Voucher(Guid.NewGuid(), c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Sales).Id,
            new DateOnly(2024, 5, 10), new[]
            {
                new EntryLine(buyer.Id, new Money(1_234.57m), DrCr.Debit),
                new EntryLine(education.Id, new Money(1_234.57m), DrCr.Credit),
            }, number: 1, partyId: buyer.Id, isAccountingInvoice: true);
        new LedgerService(c).Post(sale);
        return c;
    }

    private static Company Reload(Company original)
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"apex-as-per-group-{Guid.NewGuid():N}.db");
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
