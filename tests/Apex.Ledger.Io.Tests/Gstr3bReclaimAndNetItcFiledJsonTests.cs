using System.Text;
using System.Text.Json;
using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Services;

namespace Apex.Ledger.Io.Tests;

/// <summary>
/// Census row 6.9 — <b>the filed GSTR-3B must carry the ITC-reversal figures the screen shows.</b>
///
/// <para>Three surfaces rendered the same return and disagreed. The 3B screen
/// (<c>ReportsViewModel.BuildGstr3b</c>) renders <c>ItcReportedAllOther*</c> under the 4(A)(5) caption and
/// <c>NetItcAvailable*</c> under 4(C); the offline preview and <see cref="GstReturnJson.Gstr3b"/> rendered the bare
/// <c>Itc*</c> and emitted no 4(C) at all. An operator who reclaimed a Rule-37 reversal therefore SAW the credit and
/// FILED a 4(A)(5) short by exactly the reclaimed amount.</para>
///
/// <para><b>Grounding, read by content.</b> CBIC Circular No. 170/02/2022-GST (cbic-gst.gov.in) para 4.3(C): "Such ITC
/// may be reclaimed in Table 4(A)(5) on fulfilment of necessary conditions. Further, all such reclaimed ITC shall also
/// be shown in Table 4(D)(1)." Para 4.2: "the amount of Net ITC Available as per Table 4(C) of FORM GSTR-3B gets
/// credited into the electronic credit ledger (ECL) of the registered person." Para 4.3(D) gives the formula
/// "(4A - [4B (1) + 4B (2)])".</para>
///
/// <para><b>The book.</b> One local purchase of Rs.10,000 @18% ⇒ CGST 900 + SGST 900 (90,000 paisa a head). The whole
/// of that ITC is reversed under Rule 37 (⇒ 4(B)(2)) and then reclaimed (⇒ 4(D)(1), and re-reported inside 4(A)(5)).
/// The reclaim posts as a stat-adjustment Journal, which <c>ReadSide</c> excludes, so <c>ItcCgst</c> stays 90,000 and
/// 4(A)(5) is short by the reclaim unless the reclaim is added back — which is the whole point of the fix.</para>
/// </summary>
public sealed class Gstr3bReclaimAndNetItcFiledJsonTests
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

    /// <param name="reverse">Post the Rule-37 reversal (⇒ 4(B)(2)).</param>
    /// <param name="reclaim">Post the reclaim of that reversal (⇒ 4(D)(1) and back inside 4(A)(5)).</param>
    private static Company Book(bool reverse, bool reclaim)
    {
        var c = CompanyFactory.CreateSeeded("Reclaim 3B Co", FyStart);
        var gst = new GstService(c);
        gst.EnableGst(new GstConfig
        {
            HomeStateCode = "27",
            Gstin = Gstin,
            RegistrationType = GstRegistrationType.Regular,
            ApplicableFrom = FyStart,
            Periodicity = GstReturnPeriodicity.Monthly,
        });

        var purchases = Add(c, "Purchases 3B", "Purchase Accounts", true);
        var supplier = Add(c, "Reclaim Supplier", "Sundry Creditors", false);
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
        var purchaseId = Guid.NewGuid();
        new LedgerService(c).Post(
            new Voucher(purchaseId, ptype, new DateOnly(2025, 4, 6), lines, partyId: supplier.Id));

        if (reverse)
        {
            var svc = new GstReversalService(c);
            var row = svc.PostRule37(purchaseId, "2025-04", new DateOnly(2025, 4, 20));
            if (reclaim) svc.Reclaim(row.Id, "2025-04", new DateOnly(2025, 4, 25));
        }
        return c;
    }

    // ============================================================ 4(A)(5) carries the reclaim, in the EMITTED BYTES

    /// <summary>
    /// 🔴 THE HEADLINE. 4(A)(5) in the filed bytes is the ordinary credit PLUS the reclaim, and 4(D)(1) repeats the
    /// reclaim for information — "reported twice" is the circular's own instruction, not a double count, because
    /// 4(C) draws on 4(A) and never on 4(D).
    /// <para><b>Mutation:</b> revert <c>Tbl4a5CamtPaisa = MoneyCodec.ToPaisa(r.ItcReportedAllOtherCgst)</c> to
    /// <c>r.ItcCgst</c> in <c>GstReturnJson.Gstr3b</c> ⇒ this reads 90,000 and the assertion fails.</para>
    /// </summary>
    [Fact]
    public void Filed_4a5_carries_the_reclaim_and_4d1_repeats_it()
    {
        using var doc = JsonDocument.Parse(GstReturnJson.Gstr3b(Book(reverse: true, reclaim: true), From, To));
        var root = doc.RootElement;

        // 90,000 posted credit + 90,000 reclaimed = 180,000 a head.
        Assert.Equal(180_000L, root.GetProperty("tbl4a5_camt_paisa").GetInt64());
        Assert.Equal(180_000L, root.GetProperty("tbl4a5_samt_paisa").GetInt64());
        Assert.Equal(0L, root.GetProperty("tbl4a5_iamt_paisa").GetInt64());

        // The same reclaim, repeated for information in 4(D)(1).
        Assert.Equal(90_000L, root.GetProperty("tbl4d1_camt_paisa").GetInt64());
        Assert.Equal(90_000L, root.GetProperty("tbl4d1_samt_paisa").GetInt64());

        // And the reversal it reclaims is still declared in 4(B)(2).
        Assert.Equal(90_000L, root.GetProperty("tbl4b2_camt_paisa").GetInt64());
        Assert.Equal(90_000L, root.GetProperty("tbl4b2_samt_paisa").GetInt64());
    }

    /// <summary>
    /// The reclaim MOVES the filed 4(A)(5): the same book without the reclaim files 90,000, with it 180,000. Pinning
    /// the delta rather than one figure is what catches an emitter that happens to be right by accident.
    /// </summary>
    [Fact]
    public void Filed_4a5_moves_by_exactly_the_reclaim()
    {
        using var reversedOnly = JsonDocument.Parse(
            GstReturnJson.Gstr3b(Book(reverse: true, reclaim: false), From, To));
        using var reclaimed = JsonDocument.Parse(
            GstReturnJson.Gstr3b(Book(reverse: true, reclaim: true), From, To));

        Assert.Equal(90_000L, reversedOnly.RootElement.GetProperty("tbl4a5_camt_paisa").GetInt64());
        Assert.Equal(0L, reversedOnly.RootElement.GetProperty("tbl4d1_camt_paisa").GetInt64());

        Assert.Equal(
            reversedOnly.RootElement.GetProperty("tbl4a5_camt_paisa").GetInt64() + 90_000L,
            reclaimed.RootElement.GetProperty("tbl4a5_camt_paisa").GetInt64());
    }

    // ================================================================================ 4(C) is on the envelope at all

    /// <summary>
    /// 4(C) Net ITC Available is emitted, and it is the circular's formula — NOT the raw posted credit. With the
    /// reversal and its reclaim both in the period: 4(A)(5) 180,000 − 4(B)(2) 90,000 = 90,000 a head, which is the
    /// credit that actually reaches the electronic credit ledger.
    /// <para><b>Mutation:</b> delete the three <c>Tbl4cNetItc*</c> assignments ⇒ <c>GetProperty</c> throws
    /// <c>KeyNotFoundException</c> and all three assertions fail.</para>
    /// </summary>
    [Fact]
    public void Filed_4c_net_itc_is_the_circular_formula_not_the_raw_posted_credit()
    {
        using var doc = JsonDocument.Parse(GstReturnJson.Gstr3b(Book(reverse: true, reclaim: true), From, To));
        var root = doc.RootElement;

        Assert.Equal(90_000L, root.GetProperty("tbl4c_net_itc_camt_paisa").GetInt64());
        Assert.Equal(90_000L, root.GetProperty("tbl4c_net_itc_samt_paisa").GetInt64());
        Assert.Equal(0L, root.GetProperty("tbl4c_net_itc_iamt_paisa").GetInt64());

        // C = A5 − B2, read off the emitted bytes themselves rather than recomputed from the engine.
        Assert.Equal(
            root.GetProperty("tbl4a5_camt_paisa").GetInt64() - root.GetProperty("tbl4b2_camt_paisa").GetInt64(),
            root.GetProperty("tbl4c_net_itc_camt_paisa").GetInt64());
    }

    /// <summary>
    /// A reversal with NO reclaim must REDUCE 4(C) below the posted credit — the circular's whole point in para 4.2,
    /// that a reversal "should not get credited into the ECL". 90,000 availed − 90,000 reversed = nil.
    /// </summary>
    [Fact]
    public void Filed_4c_falls_to_nil_when_the_reversal_is_not_reclaimed()
    {
        using var doc = JsonDocument.Parse(GstReturnJson.Gstr3b(Book(reverse: true, reclaim: false), From, To));
        var root = doc.RootElement;

        Assert.Equal(90_000L, root.GetProperty("tbl4a5_camt_paisa").GetInt64());
        Assert.Equal(90_000L, root.GetProperty("tbl4b2_camt_paisa").GetInt64());
        Assert.Equal(0L, root.GetProperty("tbl4c_net_itc_camt_paisa").GetInt64());
    }

    /// <summary>
    /// The key must be in the bytes as TEXT, so a revert of the emitter cannot be masked by a parser defaulting a
    /// missing long to zero (the trap <c>W3ReviewIsdFiledJsonTests</c> records for 4(A)(4)).
    /// </summary>
    [Fact]
    public void The_three_4c_keys_are_literally_in_the_emitted_text()
    {
        var json = Encoding.UTF8.GetString(GstReturnJson.Gstr3b(Book(reverse: true, reclaim: true), From, To));

        Assert.Contains("\"tbl4c_net_itc_camt_paisa\": 90000", json, StringComparison.Ordinal);
        Assert.Contains("\"tbl4c_net_itc_samt_paisa\": 90000", json, StringComparison.Ordinal);
        Assert.Contains("\"tbl4c_net_itc_iamt_paisa\": 0", json, StringComparison.Ordinal);
        Assert.Contains("\"tbl4a5_camt_paisa\": 180000", json, StringComparison.Ordinal);
    }

    // ============================================================= Table 6.1 is NOT invented into the filed artefact

    /// <summary>
    /// 🔴 THE INVENTION THAT WAS REMOVED. The envelope used to carry <c>tbl6_1_net_*</c> = Outward − <c>ItcCgst</c>:
    /// the wrong credit base (not 4(C)) and no 3.1(d) leg at all, which CGST Act §2(82) + §49(4) make cash-only — so
    /// no single net figure can be correct there. The vendor does not fill the table either:
    /// help.tallysolutions.com/tally-prime/india-gst/gstr-3b-report-tally/ ("How to View GSTR-3B Report in
    /// TallyPrime"), opened by content — "6.1 Payment of tax This section will be left blank."
    /// <para>Asserted on the emitted TEXT as well as on the parsed object, because a removed key is exactly the thing
    /// a property-wise assertion cannot see.</para>
    /// </summary>
    [Fact]
    public void Filed_bytes_do_not_invent_a_table_6_1_payment_of_tax()
    {
        var bytes = GstReturnJson.Gstr3b(Book(reverse: true, reclaim: true), From, To);
        var json = Encoding.UTF8.GetString(bytes);

        Assert.DoesNotContain("tbl6_1", json, StringComparison.Ordinal);

        using var doc = JsonDocument.Parse(bytes);
        Assert.False(doc.RootElement.TryGetProperty("tbl6_1_net_camt_paisa", out _));
        Assert.False(doc.RootElement.TryGetProperty("tbl6_1_net_samt_paisa", out _));
        Assert.False(doc.RootElement.TryGetProperty("tbl6_1_net_iamt_paisa", out _));

        // The payment table is DERIVABLE from what is emitted instead — nothing was lost with it.
        Assert.True(doc.RootElement.TryGetProperty("tbl3_1a_camt_paisa", out _));
        Assert.True(doc.RootElement.TryGetProperty("tbl3_1d_rcm_camt_paisa", out _));
        Assert.True(doc.RootElement.TryGetProperty("tbl4c_net_itc_camt_paisa", out _));
    }
}
