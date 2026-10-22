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
    /// ~~<c>Filed_6_1_net_is_unmoved_by_4a4_which_is_the_preexisting_shape</c>~~ 🔴 <b>THE DEFECT THIS TEST PINNED IS
    /// NOW FIXED, SO THE TEST IS INVERTED RATHER THAN DELETED.</b> Its own summary recorded the shape as
    /// "Pre-existing (6.1 already ignores 4(A)(2)/(3) and 4(B)/4(D)), recorded not fixed" — the fix is this slice.
    ///
    /// <para><b>What changed.</b> <c>tbl6_1_net_*</c> was our own invention: Outward − <c>ItcCgst</c>, a net taken
    /// over the raw posted credit instead of Table 4(C), with the cash-only 3.1(d) leg omitted entirely (CGST Act
    /// §2(82) + §49(4)). The vendor files nothing there either —
    /// help.tallysolutions.com/tally-prime/india-gst/gstr-3b-report-tally/ ("How to View GSTR-3B Report in
    /// TallyPrime"), opened by content: "6.1 Payment of tax This section will be left blank." The keys are gone and
    /// <c>tbl4c_net_itc_*</c> stands in their place, carrying the figure that is credited to the ECL (CBIC Circular
    /// No. 170/02/2022-GST para 4.2; formula at para 4.3(D), "(4A - [4B (1) + 4B (2)])").</para>
    ///
    /// <para><b>So the assertion flips:</b> 4(A)(4) now DOES move the filed net-credit figure, by exactly the ISD
    /// credit, which is what the review was complaining about when it pinned the opposite.</para>
    /// </summary>
    [Fact]
    public void Filed_4c_moves_by_exactly_the_isd_credit_and_no_6_1_is_invented()
    {
        var withoutBytes = GstReturnJson.Gstr3b(Book(withIsd: false), From, To);
        var withBytes = GstReturnJson.Gstr3b(Book(withIsd: true), From, To);
        using var without = JsonDocument.Parse(withoutBytes);
        using var with = JsonDocument.Parse(withBytes);

        // The invented payment table is gone from the bytes, in both books.
        Assert.DoesNotContain("tbl6_1", Encoding.UTF8.GetString(withoutBytes), StringComparison.Ordinal);
        Assert.DoesNotContain("tbl6_1", Encoding.UTF8.GetString(withBytes), StringComparison.Ordinal);

        // 4(C) without ISD is the bare purchase credit: CGST 900.00, SGST 900.00.
        Assert.Equal(90_000L, without.RootElement.GetProperty("tbl4c_net_itc_camt_paisa").GetInt64());

        // With ISD it moves by the ISD head figures from Book(): 1,111.11 CGST / 2,222.22 SGST / 3,333.33 IGST.
        Assert.Equal(
            without.RootElement.GetProperty("tbl4c_net_itc_camt_paisa").GetInt64() + 111_111L,
            with.RootElement.GetProperty("tbl4c_net_itc_camt_paisa").GetInt64());
        Assert.Equal(
            without.RootElement.GetProperty("tbl4c_net_itc_samt_paisa").GetInt64() + 222_222L,
            with.RootElement.GetProperty("tbl4c_net_itc_samt_paisa").GetInt64());
        Assert.Equal(
            without.RootElement.GetProperty("tbl4c_net_itc_iamt_paisa").GetInt64() + 333_333L,
            with.RootElement.GetProperty("tbl4c_net_itc_iamt_paisa").GetInt64());
    }
}
