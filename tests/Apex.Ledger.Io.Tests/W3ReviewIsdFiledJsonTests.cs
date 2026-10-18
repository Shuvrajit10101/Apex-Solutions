using System.Text;
using System.Text.Json;
using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Services;

namespace Apex.Ledger.Io.Tests;

/// <summary>
/// WAVE-46 WAVE-3 ADVERSARIAL REVIEW FIXTURE — written by the reviewer, not by the branch.
/// Every head carries a DIFFERENT figure so a CGST/SGST transposition cannot hide (the branch's own
/// fixture uses 1,500 / 1,500 for CGST / SGST and is blind to that swap), and the book also carries a
/// real 4(A)(5) purchase so the no-leak claim is tested against a NON-ZERO 4(A)(5).
/// </summary>
public sealed class W3ReviewIsdFiledJsonTests
{
    private const string Gstin = "27AAPFU0939F1ZV";
    private static readonly DateOnly FyStart = new(2025, 4, 1);
    private static readonly DateOnly From = new(2025, 4, 1);
    private static readonly DateOnly To = new(2025, 4, 30);

    private static Domain.Ledger Add(Company c, string name, string group, bool debit)
    {
        var l = new Domain.Ledger(Guid.NewGuid(), name, c.FindGroupByName(group)!.Id, Money.Zero, debit);
        c.AddLedger(l);
        return l;
    }

    private static Company Book(bool withIsd)
    {
        var c = CompanyFactory.CreateSeeded("W3 Review Co", FyStart);
        var gst = new GstService(c);
        gst.EnableGst(new GstConfig
        {
            HomeStateCode = "27",
            Gstin = Gstin,
            RegistrationType = GstRegistrationType.Regular,
            ApplicableFrom = FyStart,
            Periodicity = GstReturnPeriodicity.Monthly,
        });

        // A REAL local purchase -> non-zero 4(A)(5): Rs.10,000 @18% = CGST 900 + SGST 900.
        var purchases = c.FindLedgerByName("Purchase Accounts") is null
            ? Add(c, "Purchases", "Purchase Accounts", true)
            : Add(c, "Purchases W3", "Purchase Accounts", true);
        var supplier = Add(c, "W3 Supplier", "Sundry Creditors", false);
        supplier.PartyGst = new PartyGstDetails
        { RegistrationType = GstRegistrationType.Regular, Gstin = "27AAACC1206D1ZM", StateCode = "27" };
        var tax = gst.ComputeInvoiceTax(
            new[] { new GstService.TaxableLine(Money.FromRupees(10_000m), 1800) }, false, GstTaxDirection.Input);
        var lines = new List<EntryLine>
        {
            new(purchases.Id, Money.FromRupees(10_000m), DrCr.Debit),
            new(supplier.Id, Money.FromRupees(11_800m), DrCr.Credit),
        };
        lines.AddRange(tax.TaxLines);
        var ptype = c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Purchase).Id;
        new LedgerService(c).Post(new Voucher(Guid.NewGuid(), ptype, new DateOnly(2025, 4, 6), lines, partyId: supplier.Id));

        if (withIsd)
        {
            var ho = Add(c, "W3 Head Office", "Indirect Expenses", true);
            // DISTINCT per head: 1,111.11 / 2,222.22 / 3,333.33 / 444.44  = Rs.7,111.10
            new GstIsdCreditService(c).Post(
                "ISD/W3/REVIEW/001", new DateOnly(2025, 4, 8),
                new GstIsdCreditService.IsdCreditAmount(111_111L, 222_222L, 333_333L, 44_444L),
                ho.Id, new DateOnly(2025, 4, 9));
        }
        return c;
    }

    [Fact]
    public void Filed_bytes_carry_each_4a4_head_at_its_own_distinct_paisa()
    {
        var bytes = GstReturnJson.Gstr3b(Book(withIsd: true), From, To);
        using var doc = JsonDocument.Parse(bytes);
        var root = doc.RootElement;

        Assert.Equal(111_111L, root.GetProperty("tbl4a4_isd_camt_paisa").GetInt64());
        Assert.Equal(222_222L, root.GetProperty("tbl4a4_isd_samt_paisa").GetInt64());
        Assert.Equal(333_333L, root.GetProperty("tbl4a4_isd_iamt_paisa").GetInt64());
        Assert.Equal(44_444L, root.GetProperty("tbl4a4_isd_csamt_paisa").GetInt64());
    }

    [Fact]
    public void Filed_4a5_is_the_purchase_itc_alone_and_does_not_move_when_isd_arrives()
    {
        using var without = JsonDocument.Parse(GstReturnJson.Gstr3b(Book(withIsd: false), From, To));
        using var with = JsonDocument.Parse(GstReturnJson.Gstr3b(Book(withIsd: true), From, To));

        Assert.Equal(90_000L, without.RootElement.GetProperty("tbl4a5_camt_paisa").GetInt64());
        Assert.Equal(90_000L, with.RootElement.GetProperty("tbl4a5_camt_paisa").GetInt64());
        Assert.Equal(90_000L, with.RootElement.GetProperty("tbl4a5_samt_paisa").GetInt64());
        Assert.Equal(0L, without.RootElement.GetProperty("tbl4a4_isd_camt_paisa").GetInt64());
    }

    /// <summary>
    /// The emitted keys must be in the bytes as TEXT, so a revert of the emitter cannot be masked by a
    /// parser defaulting a missing long to zero.
    /// </summary>
    [Fact]
    public void The_four_4a4_keys_are_literally_in_the_emitted_text()
    {
        var json = Encoding.UTF8.GetString(GstReturnJson.Gstr3b(Book(withIsd: true), From, To));
        Assert.Contains("\"tbl4a4_isd_camt_paisa\": 111111", json, StringComparison.Ordinal);
        Assert.Contains("\"tbl4a4_isd_samt_paisa\": 222222", json, StringComparison.Ordinal);
        Assert.Contains("\"tbl4a4_isd_iamt_paisa\": 333333", json, StringComparison.Ordinal);
        Assert.Contains("\"tbl4a4_isd_csamt_paisa\": 44444", json, StringComparison.Ordinal);
    }

    /// <summary>
    /// 🔴 THE FILE'S OWN 6.1 NET. 6.1 is emitted from Gstr3b.NetCgst = Outward - ItcCgst, which counts
    /// ONLY 4(A)(5). This pins the SHAPE so the next reader sees it: the filed 6.1 does NOT move when
    /// 4(A)(4) arrives. Pre-existing (6.1 already ignores 4(A)(2)/(3) and 4(B)/4(D)), recorded not fixed.
    /// </summary>
    [Fact]
    public void Filed_6_1_net_is_unmoved_by_4a4_which_is_the_preexisting_shape()
    {
        using var without = JsonDocument.Parse(GstReturnJson.Gstr3b(Book(withIsd: false), From, To));
        using var with = JsonDocument.Parse(GstReturnJson.Gstr3b(Book(withIsd: true), From, To));

        Assert.Equal(
            without.RootElement.GetProperty("tbl6_1_net_camt_paisa").GetInt64(),
            with.RootElement.GetProperty("tbl6_1_net_camt_paisa").GetInt64());
    }
}
