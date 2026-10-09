using System.Text.Json;
using Apex.Ledger.Domain;
using Apex.Ledger.Services;

namespace Apex.Ledger.Io.Tests;

/// <summary>
/// 🔴 <b>T2-92 / T2-93, AT THE ARTEFACT.</b> The companion to
/// <c>Apex.Ledger.Tests.Gstr3bUnlinkedReturnNoteSignTests</c>, which proves the two defects and the fix on the
/// projection and against the POSTED ENTRY. This file proves them on <b>THE EMITTED BYTES</b> — the thing a user
/// actually files — because a correct projection that never reaches the payload would be worth nothing, and this
/// project has graded never-emitted work as progress before.
///
/// <para><b>The defects.</b> <c>Gstr3b.ReadSide</c> summed <c>line.Amount.Amount</c>, a positive MAGNITUDE, and
/// <c>Gstr3b.cs</c> held <b>zero</b> references to <see cref="VoucherEffects.IsReturnNote"/> or
/// <see cref="EntryLine.Side"/>, so an UNLINKED Credit Note (the §34 link is opt-in, so this is the ordinary
/// keyboard-entered shape) was filed in §3.1(a) as though it were a fresh supply. Separately,
/// <c>ExemptOutwardValue</c> skipped any voucher carrying ANY taxed line, so a MIXED invoice filed its exempt leg
/// as ZERO in §3.1(c).</para>
///
/// <para><b>🔴 Figures by hand, and they are what gets filed.</b> Intra-state (27→27) MIXED sale: Widget 2,500 Nos
/// × ₹20 = ₹50,000 @ 18% + EXEMPT Fresh Milk 2,000 × ₹10 = ₹20,000; posted CGST ₹4,500 + SGST ₹4,500. Then an
/// UNLINKED Credit Note (sales return): Widget 500 × ₹20 = ₹10,000 @ 18% + Milk 500 × ₹10 = ₹5,000; posted CGST
/// ₹900 + SGST ₹900 on the DEBIT side. In integer paisa (ER-10) the emitted payload must carry
/// <c>tbl3_1a_txval_paisa</c> 4000000, <c>tbl3_1a_camt_paisa</c>/<c>tbl3_1a_samt_paisa</c> 360000 each,
/// <c>tbl3_1a_iamt_paisa</c> 0 and <c>tbl3_1c_nil_exempt_nongst_paisa</c> 1500000.
/// <b>Before the fix the same payload filed</b> txval 6000000, camt/samt 540000 each and
/// tbl3_1c 0 — a filed return overstating output tax by ₹1,800 and taxable turnover by ₹20,000 while
/// under-declaring exempt turnover by ₹15,000.</para>
///
/// <para><b>🔴 Grounding.</b> <c>help.tallysolutions.com</c>'s TallyPrime GSTR-3B page states under
/// "(a) Outward taxable supplies (other than zero rated, nil rated and exempted)" that <i>"Total Taxable value is
/// the sum of all taxable values including the value of the debit and credit notes, and advance liabilities,
/// excluding tax"</i> — which grounds the note's INCLUSION in the 3.1(a) aggregate. The <b>DIRECTION</b> (that a
/// credit note reduces) is <b>OURS</b>, taken from the posted entry and this module's documented invariant that
/// §3.1 reconciles to the Output tax-ledger postings; main also "included" the note, with the wrong sign, so that
/// sentence alone does not settle direction and nothing here claims it does. The same page is <b>SILENT</b> on
/// per-line versus per-voucher granularity and on mixed invoices, so <b>the T2-93 split is OURS</b> too, forced by
/// the invariant that GSTR-1 and GSTR-3B are filed off the same books and may not disagree.</para>
///
/// <para><b>🔴 WHAT THIS FILE DOES NOT CLAIM.</b> The GSTN GSTR-3B upload payload schema is published only behind
/// the authenticated GST developer portal, so these <i>key names</i> remain a <b>documented divergence, labelled as
/// ours</b>, and nothing here joins the compared set as portal-accepted. What is asserted is the one thing that is
/// independent of the key names and that no source disputes: <b>the FIGURES the artefact carries</b>, against
/// amounts computed by hand and against the Output tax-ledger. An accepted wrong file is worse than a rejected
/// one, and these were the wrong figures.</para>
/// </summary>
public sealed class Gstr3bUnlinkedReturnNoteEmittedJsonTests
{
    private const string GstinMaharashtra = "27AAPFU0939F1ZV";
    private static readonly DateOnly FyStart = new(2025, 4, 1);
    private static readonly DateOnly From = new(2025, 4, 1);
    private static readonly DateOnly To = new(2025, 4, 30);
    private static readonly DateOnly SaleDate = new(2025, 4, 9);
    private static readonly DateOnly ReturnDate = new(2025, 4, 20);

    /// <summary>🔴 T2-92 IN THE EMITTED PAYLOAD. Filed 6000000 / 540000 / 540000 before the fix.</summary>
    [Fact]
    public void The_emitted_GSTR3B_payload_nets_3_1_a_down()
    {
        using var doc = JsonDocument.Parse(GstReturnJson.Gstr3b(BuildBook(), From, To));
        var root = doc.RootElement;

        Assert.Equal(4_000_000L, root.GetProperty("tbl3_1a_txval_paisa").GetInt64());   // was 6000000
        Assert.Equal(360_000L, root.GetProperty("tbl3_1a_camt_paisa").GetInt64());      // was 540000
        Assert.Equal(360_000L, root.GetProperty("tbl3_1a_samt_paisa").GetInt64());      // was 540000
        Assert.Equal(0L, root.GetProperty("tbl3_1a_iamt_paisa").GetInt64());
    }

    /// <summary>🔴 T2-93 IN THE EMITTED PAYLOAD — the mixed invoice's exempt leg was filed as a literal zero.</summary>
    [Fact]
    public void The_emitted_GSTR3B_payload_files_the_mixed_invoice_exempt_leg()
    {
        using var doc = JsonDocument.Parse(GstReturnJson.Gstr3b(BuildBook(), From, To));

        Assert.Equal(
            1_500_000L,
            doc.RootElement.GetProperty("tbl3_1c_nil_exempt_nongst_paisa").GetInt64());   // was 0
    }

    /// <summary>
    /// 🔴 <b>THE FILED BYTES, AGAINST THE BOOKS.</b> The engine posted the sale's tax legs on the CREDIT side of
    /// Output CGST and the note's on the DEBIT side, so the ledger had already netted to ₹3,600 — and the EMITTED
    /// file declared ₹5,400 against those very books. Comparing the artefact to the POSTED ENTRIES is the only
    /// check that cannot be satisfied by two projections agreeing on one wrong answer.
    /// </summary>
    [Fact]
    public void The_emitted_GSTR3B_3_1_a_tax_reconciles_to_the_Output_tax_ledger()
    {
        var company = BuildBook();
        using var doc = JsonDocument.Parse(GstReturnJson.Gstr3b(company, From, To));

        var outputCgst = company.Ledgers.Single(l => l.Name == "Output CGST").Id;
        var netCgstPaisa = (long)Math.Round(NetCredit(company, outputCgst) * 100m);

        Assert.Equal(360_000L, netCgstPaisa);   // the BOOKS, in paisa
        Assert.Equal(netCgstPaisa, doc.RootElement.GetProperty("tbl3_1a_camt_paisa").GetInt64());
    }

    /// <summary>
    /// 🔴 ER-13 BASELINE — a book with no return note must be unchanged by the sign, and its mixed invoice must
    /// still file its full exempt leg. This is the test that would catch a fix which signed something it should
    /// not have, or which double-counted the exempt lines it newly admits.
    /// </summary>
    [Fact]
    public void The_emitted_GSTR3B_payload_is_unchanged_for_a_book_with_no_return_note()
    {
        using var doc = JsonDocument.Parse(GstReturnJson.Gstr3b(BuildBook(withReturnNote: false), From, To));
        var root = doc.RootElement;

        Assert.Equal(5_000_000L, root.GetProperty("tbl3_1a_txval_paisa").GetInt64());
        Assert.Equal(450_000L, root.GetProperty("tbl3_1a_camt_paisa").GetInt64());
        Assert.Equal(450_000L, root.GetProperty("tbl3_1a_samt_paisa").GetInt64());
        Assert.Equal(2_000_000L, root.GetProperty("tbl3_1c_nil_exempt_nongst_paisa").GetInt64());   // was 0
    }

    // ================================================================ fixture

    /// <summary>The ledger's own NET credit movement in the window — credits less debits, which is how the engine
    /// actually posted the sale (credit) and the return note (debit). Deliberately NOT a report projection.</summary>
    private static decimal NetCredit(Company company, Guid ledgerId) =>
        company.Vouchers
            .Where(v => v.Date >= From && v.Date <= To)
            .SelectMany(v => v.Lines)
            .Where(l => l.LedgerId == ledgerId)
            .Sum(l => l.Side == DrCr.Credit ? l.Amount.Amount : -l.Amount.Amount);

    /// <summary>
    /// One intra-state <b>MIXED</b> sales invoice (Widget ₹50,000 @ 18% + EXEMPT Milk ₹20,000) and, unless
    /// suppressed, one <b>sales return</b> of part of it (Widget ₹10,000 @ 18% + Milk ₹5,000) as an <b>UNLINKED</b>
    /// Credit Note. Mirrors <c>Gstr1UnlinkedReturnNoteEmittedJsonTests</c>'s fixture on purpose: the two returns
    /// are emitted off the SAME books and must not disagree. The note is posted BY HAND rather than through
    /// <c>CreditDebitNoteService</c>, which ALWAYS creates the §34 link and therefore always takes the already
    /// excluded path — the defect lived in the one shape the suite never built.
    /// </summary>
    private static Company BuildBook(bool withReturnNote = true)
    {
        var c = CompanyFactory.CreateSeeded("Return Note 3B Io Co", FyStart);
        var gst = new GstService(c);
        gst.EnableGst(new GstConfig
        {
            HomeStateCode = "27",
            Gstin = GstinMaharashtra,
            RegistrationType = GstRegistrationType.Regular,
            ApplicableFrom = FyStart,
            Periodicity = GstReturnPeriodicity.Monthly,
        });

        var inv = new InventoryService(c);
        var ledgers = new LedgerService(c);
        var grp = inv.CreateStockGroup("Goods");
        var nos = inv.CreateSimpleUnit("Nos", "Numbers", unitQuantityCode: "NOS");
        var main = c.MainLocation!.Id;

        var widget = inv.CreateStockItem("Widget", grp.Id, nos.Id);
        widget.Gst = new StockItemGstDetails
        {
            HsnSac = "847130", Taxability = GstTaxability.Taxable, RateBasisPoints = 1800,
        };

        var milk = inv.CreateStockItem("Fresh Milk", grp.Id, nos.Id);
        milk.Gst = new StockItemGstDetails { HsnSac = "040110", Taxability = GstTaxability.Exempt };

        inv.AddOpeningBalance(widget.Id, main, 10_000m, Money.FromRupees(20m));
        inv.AddOpeningBalance(milk.Id, main, 10_000m, Money.FromRupees(10m));

        var sales = AddLedger(c, "Sales", "Sales Accounts", false);
        var debtor = AddLedger(c, "Debtor", "Sundry Debtors", true);
        debtor.PartyGst = new PartyGstDetails
        {
            RegistrationType = GstRegistrationType.Regular, Gstin = GstinMaharashtra, StateCode = "27",
        };

        var salesType = c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Sales).Id;

        // ---- the MIXED SALE. Only the Widget is taxable ⇒ ONE 1800 rate group on ₹50,000, with the Milk line
        // exempt — exactly the shape T2-93's wholesale skip discarded.
        var saleTax = gst.ComputeInvoiceTax(
            new[] { new GstService.TaxableLine(Money.FromRupees(50_000m), 1800) },
            interState: false, GstTaxDirection.Output);
        Assert.Equal(9_000.00m, saleTax.TotalTax.Amount);   // the fixture's premise, held at build time

        var saleLines = new List<EntryLine>
        {
            new(debtor.Id, Money.FromRupees(70_000m + saleTax.TotalTax.Amount), DrCr.Debit),
            new(sales.Id, Money.FromRupees(70_000m), DrCr.Credit),
        };
        saleLines.AddRange(saleTax.TaxLines);

        ledgers.Post(new Voucher(Guid.NewGuid(), salesType, SaleDate, saleLines, partyId: debtor.Id, inventoryLines: new[]
        {
            new VoucherInventoryLine(widget.Id, main, 2500m, Money.FromRupees(20m)),  // 2500 × 20 = 50,000
            new VoucherInventoryLine(milk.Id, main, 2000m, Money.FromRupees(10m)),    // 2000 × 10 = 20,000
        }));

        if (!withReturnNote) return c;

        // ---- the SALES RETURN as an UNLINKED Credit Note; reverseSides puts every tax leg on the DEBIT side.
        var noteTax = gst.ComputeInvoiceTax(
            new[] { new GstService.TaxableLine(Money.FromRupees(10_000m), 1800) },
            interState: false, GstTaxDirection.Output, reverseSides: true);
        Assert.Equal(1_800.00m, noteTax.TotalTax.Amount);

        var noteLines = new List<EntryLine>
        {
            new(sales.Id, Money.FromRupees(15_000m), DrCr.Debit),
            new(debtor.Id, Money.FromRupees(15_000m + noteTax.TotalTax.Amount), DrCr.Credit),
        };
        noteLines.AddRange(noteTax.TaxLines);

        var noteType = c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.CreditNote).Id;
        ledgers.Post(new Voucher(Guid.NewGuid(), noteType, ReturnDate, noteLines, partyId: debtor.Id, inventoryLines: new[]
        {
            new VoucherInventoryLine(widget.Id, main, 500m, Money.FromRupees(20m)),   // 500 × 20 = 10,000
            new VoucherInventoryLine(milk.Id, main, 500m, Money.FromRupees(10m)),     // 500 × 10 = 5,000
        }));

        return c;
    }

    private static Domain.Ledger AddLedger(Company c, string name, string groupName, bool openingIsDebit)
    {
        var l = new Domain.Ledger(Guid.NewGuid(), name, c.FindGroupByName(groupName)!.Id, Money.Zero, openingIsDebit);
        c.AddLedger(l);
        return l;
    }
}
