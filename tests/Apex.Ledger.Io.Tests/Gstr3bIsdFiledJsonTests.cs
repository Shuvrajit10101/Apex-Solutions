using System.Text;
using System.Text.Json;
using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Services;

namespace Apex.Ledger.Io.Tests;

/// <summary>
/// 🔴 <b>THE FILED ARTEFACT CARRIES TABLE 4(A)(4) — the statutory-filing defect R-M1.</b>
///
/// <para><b>What was wrong.</b> The ISD-credit slice wired <c>Itc4A4Isd*</c> into <c>Gstr3b.Build</c>, into the
/// Table 4(C) net and (after review) into the on-screen GSTR-3B — and <b>not</b> into
/// <see cref="GstReturnJson.Gstr3b"/>, the envelope that is actually <b>filed</b>. The emitted object carried
/// <c>tbl4a2</c>, <c>tbl4a3</c>, <c>tbl4a5</c>, <c>tbl4b1</c>, <c>tbl4b2</c>, <c>tbl4d1</c> and ~~<c>tbl6_1</c>~~, and
/// <b>no 4(A)(4) key at all</b>. So the screen showed the ISD credit and the file did not: on the fixture below the
/// filed ITC was understated by the whole of <b>₹5,500.00</b> (CGST ₹1,500.00 + SGST ₹1,500.00 + IGST ₹2,000.00 +
/// Cess ₹500.00). The GSTN offline tool derives 4(C) from the rows it is handed, so nothing inside the file
/// contradicted itself — which is exactly why no existing test saw it.
/// <para>⚠️ <b><c>tbl6_1</c> IS STRUCK ABOVE: the envelope no longer carries it.</b> That key was our own invention
/// — Outward − <c>ItcCgst</c>, netting the raw posted credit instead of Table 4(C) and omitting the cash-only
/// 3.1(d) leg (CGST Act §2(82) + §49(4)) — and the vendor files nothing there either
/// (<c>help.tallysolutions.com/tally-prime/india-gst/gstr-3b-report-tally/</c>, by content: "6.1 Payment of tax
/// This section will be left blank."). It was replaced by <c>tbl4c_net_itc_*</c>, which IS the figure credited to
/// the electronic credit ledger. The sentence above is kept because it records what was measured at the time.</para>
///
/// <para><b>These tests assert on the EMITTED BYTES</b> — <c>GstReturnJson.Gstr3b</c>'s UTF-8 output, parsed and
/// also matched as text — never on the DTO the emitter just populated.</para>
///
/// <para><b>Grounding (R7, ruling 14 tier 1), read BY CONTENT.</b>
/// <c>help.tallysolutions.com/tally-prime/india-gst/isd-credit/</c>, verbatim: <i>"The voucher will be included in
/// GSTR-3B and appear under 4A: Input Tax Credit Available (either in part or in full) &gt; Inward supplies from
/// ISD."</i> The Table 4(C) formula those heads feed is CBIC Circular No. 170/02/2022-GST, Annexure, row <i>"(C)
/// Net ITC Available (A)-(B)"</i>: <c>C = A1+A2+A3+A4+A5-B1-B2</c> — <b>A4 is this row</b>.</para>
///
/// <para><b>THE KEY NAMES ARE OURS AND DO NOT JOIN THE COMPARED SET</b>, exactly as the class-level note on
/// <see cref="Gstr1Gstr3bJsonTests"/> records for every other <c>tbl*</c> key: the GSTN upload payload schema is
/// published only behind the authenticated developer portal, so these follow the house convention and ride the
/// same <c>schemaStatus</c> flag. What <b>is</b> verified here is that the figure the engine holds reaches the
/// file — the omission of a held figure is a defect under any key spelling.</para>
/// </summary>
public sealed class Gstr3bIsdFiledJsonTests
{
    private const string GstinMaharashtra = "27AAPFU0939F1ZV";
    private static readonly DateOnly FyStart = new(2025, 4, 1);
    private static readonly DateOnly From = new(2025, 4, 1);
    private static readonly DateOnly To = new(2025, 4, 30);
    private static readonly DateOnly DocDate = new(2025, 4, 10);
    private static readonly DateOnly PostDate = new(2025, 4, 15);

    // The credit the head office distributes to this registration, in integer paisa (ER-10) — the one place these
    // figures are written down. ₹1,500.00 + ₹1,500.00 + ₹2,000.00 + ₹500.00 = ₹5,500.00.
    private const long Cgst = 150_000L;
    private const long Sgst = 150_000L;
    private const long Igst = 200_000L;
    private const long Cess = 50_000L;

    /// <summary>A Regular GST company with a "Head Office Account" ledger to take the credit against — the
    /// vendor's <i>"branch account ledger"</i> — and nothing else, so 4(A)(5) is provably untouched at zero.</summary>
    private static Company BuildWithIsdReceipt()
    {
        var c = CompanyFactory.CreateSeeded("ISD Filing Co", FyStart);
        new GstService(c).EnableGst(new GstConfig
        {
            HomeStateCode = "27",
            Gstin = GstinMaharashtra,
            RegistrationType = GstRegistrationType.Regular,
            ApplicableFrom = FyStart,
            Periodicity = GstReturnPeriodicity.Monthly,
        });

        var headOffice = new Domain.Ledger(
            Guid.NewGuid(), "Head Office Account", c.FindGroupByName("Indirect Expenses")!.Id, Money.Zero, true);
        c.AddLedger(headOffice);

        new GstIsdCreditService(c).Post(
            "ISD/2025-26/001", DocDate,
            new GstIsdCreditService.IsdCreditAmount(Cgst, Sgst, Igst, Cess),
            headOffice.Id, PostDate);

        return c;
    }

    /// <summary>
    /// 🔴 <b>THE MONEY TEST.</b> The four Table 4(A)(4) heads reach the emitted file at their exact paisa.
    /// Reverting the emitter (dropping the <c>Tbl4a4Isd*</c> assignments or the DTO members) makes
    /// <c>GetProperty</c> throw <c>KeyNotFoundException</c> here — the key is simply not in the bytes.
    /// </summary>
    [Fact]
    public void Gstr3b_json_emits_table_4a4_isd_credit_at_its_own_per_head_split()
    {
        var bytes = GstReturnJson.Gstr3b(BuildWithIsdReceipt(), From, To);

        using var doc = JsonDocument.Parse(bytes);
        var root = doc.RootElement;

        Assert.Equal(150_000L, root.GetProperty("tbl4a4_isd_camt_paisa").GetInt64());
        Assert.Equal(150_000L, root.GetProperty("tbl4a4_isd_samt_paisa").GetInt64());
        Assert.Equal(200_000L, root.GetProperty("tbl4a4_isd_iamt_paisa").GetInt64());
        Assert.Equal(50_000L, root.GetProperty("tbl4a4_isd_csamt_paisa").GetInt64());
    }

    /// <summary>
    /// The four keys are PRESENT IN THE BYTE STREAM, asserted as text rather than through a parser that would
    /// happily default a missing long to zero. This is the assertion that fails loudest on a revert.
    /// </summary>
    [Fact]
    public void The_four_table_4a4_keys_are_present_in_the_emitted_byte_stream()
    {
        var json = Encoding.UTF8.GetString(GstReturnJson.Gstr3b(BuildWithIsdReceipt(), From, To));

        Assert.Contains("\"tbl4a4_isd_camt_paisa\"", json, StringComparison.Ordinal);
        Assert.Contains("\"tbl4a4_isd_samt_paisa\"", json, StringComparison.Ordinal);
        Assert.Contains("\"tbl4a4_isd_iamt_paisa\"", json, StringComparison.Ordinal);
        Assert.Contains("\"tbl4a4_isd_csamt_paisa\"", json, StringComparison.Ordinal);
    }

    /// <summary>
    /// 🔴 <b>THE WRONG-ROW GUARD.</b> The vendor names 4(A)(4) specifically, so folding the credit into
    /// 4(A)(5) "All other ITC" would file the right total under the wrong statutory row and still look balanced.
    /// On this fixture 4(A)(5) has nothing in it, and must stay at zero in the file.
    /// </summary>
    [Fact]
    public void Isd_credit_does_not_leak_into_the_filed_4a5_all_other_itc_row()
    {
        using var doc = JsonDocument.Parse(GstReturnJson.Gstr3b(BuildWithIsdReceipt(), From, To));
        var root = doc.RootElement;

        Assert.Equal(0L, root.GetProperty("tbl4a5_camt_paisa").GetInt64());
        Assert.Equal(0L, root.GetProperty("tbl4a5_samt_paisa").GetInt64());
        Assert.Equal(0L, root.GetProperty("tbl4a5_iamt_paisa").GetInt64());
        Assert.Equal(0L, root.GetProperty("tbl4a2_import_services_iamt_paisa").GetInt64());
        Assert.Equal(0L, root.GetProperty("tbl4a3_rcm_other_camt_paisa").GetInt64());
    }

    /// <summary>
    /// <b>ER-13.</b> A book that never receives ISD credit still emits the four keys, at zero — a filed return
    /// names every row it reports, and a silently-absent row is what caused this defect in the first place.
    /// </summary>
    [Fact]
    public void A_book_with_no_isd_credit_still_emits_the_four_keys_at_zero()
    {
        var c = CompanyFactory.CreateSeeded("No ISD Co", FyStart);
        new GstService(c).EnableGst(new GstConfig
        {
            HomeStateCode = "27",
            Gstin = GstinMaharashtra,
            RegistrationType = GstRegistrationType.Regular,
            ApplicableFrom = FyStart,
            Periodicity = GstReturnPeriodicity.Monthly,
        });

        using var doc = JsonDocument.Parse(GstReturnJson.Gstr3b(c, From, To));
        var root = doc.RootElement;

        Assert.Equal(0L, root.GetProperty("tbl4a4_isd_camt_paisa").GetInt64());
        Assert.Equal(0L, root.GetProperty("tbl4a4_isd_samt_paisa").GetInt64());
        Assert.Equal(0L, root.GetProperty("tbl4a4_isd_iamt_paisa").GetInt64());
        Assert.Equal(0L, root.GetProperty("tbl4a4_isd_csamt_paisa").GetInt64());
    }

    /// <summary>
    /// The emitter stays deterministic with the new heads in it — byte-for-byte equal across two builds of the
    /// same period, which the offline-file writer relies on.
    /// </summary>
    [Fact]
    public void Gstr3b_json_with_isd_credit_is_byte_for_byte_deterministic()
    {
        var c = BuildWithIsdReceipt();
        Assert.Equal(GstReturnJson.Gstr3b(c, From, To), GstReturnJson.Gstr3b(c, From, To));
    }
}
