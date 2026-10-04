using System.Text.Json;
using Apex.Ledger.Domain;
using Apex.Ledger.Services;

namespace Apex.Ledger.Io.Tests;

/// <summary>
/// 🔴 <b>T2-59, AT THE ARTEFACT.</b> The companion to
/// <c>Apex.Ledger.Tests.Gstr1UnlinkedReturnNoteSignTests</c>, which proves the defect and the fix on the
/// projection. This file proves it on <b>THE EMITTED BYTES</b> — the thing a user actually files — because a
/// correct projection that never reaches the payload would be worth nothing, and this project has graded
/// never-emitted work as progress before.
///
/// <para><b>The defect.</b> <c>Gstr1.Build</c> read posted MAGNITUDES and ignored <see cref="DrCr"/>, so a Credit
/// Note posted with the <b>opt-in</b> §34 toggle left off (no <c>GstCreditDebitNoteLink</c> ⇒ not excluded from
/// the ordinary sweep) was filed as though it were a SALE. The engine had posted it correctly — the entry screen
/// passes <c>reverseSides: IsReturnNote</c>, so the note DEBITS Output CGST — and the report then re-added it.</para>
///
/// <para><b>🔴 Figures by hand, and they are what gets filed.</b> Intra-state (27→27) sale: Widget 2,500 Nos × ₹20
/// = ₹50,000 @ 18% (HSN 847130) + EXEMPT Fresh Milk 2,000 × ₹10 = ₹20,000 (HSN 040110); posted CGST ₹4,500 + SGST
/// ₹4,500. Then an UNLINKED Credit Note (sales return): Widget 500 × ₹20 = ₹10,000 @ 18% + Milk 500 × ₹10 =
/// ₹5,000; posted CGST ₹900 + SGST ₹900 on the DEBIT side. In integer paisa (ER-10) the emitted payload must
/// carry, for HSN 847130, <c>txval_paisa</c> 4000000, <c>camt_paisa</c>/<c>samt_paisa</c> 360000 each,
/// <c>totval_paisa</c> 4720000 and <c>qty</c> 2000; for 040110, <c>txval_paisa</c> 1500000 and <c>qty</c> 1500;
/// and at the root <c>nil_exempt_nongst_paisa</c> 1500000 with <c>total_cgst_paisa</c>/<c>total_sgst_paisa</c>
/// 360000. <b>Before the fix the same payload filed</b> 847130 txval 6000000 / camt 540000 / qty 3000, 040110
/// txval 2500000 / qty 2500, nil_exempt 2500000 and total_cgst 540000 — a filed return overstating output tax by
/// ₹1,800, taxable turnover by ₹20,000, exempt turnover by ₹10,000 and a mandatory quantity by 1,000 Nos.</para>
///
/// <para><b>🔴 WHAT THIS FILE DOES NOT CLAIM.</b> Exactly as <c>Gstr1Gstr3bJsonTests</c> records, the GSTN GSTR-1
/// upload payload schema is published only behind the authenticated GST developer portal, so these <i>key names</i>
/// remain a <b>documented divergence, labelled as ours</b>, and nothing here joins the compared set as
/// portal-accepted. What is asserted is the one thing that is independent of the key names and that no source
/// disputes: <b>the FIGURES the artefact carries</b>, against amounts computed by hand and against the Output
/// tax-ledger. An accepted wrong file is worse than a rejected one, and these were the wrong figures.</para>
/// </summary>
public sealed class Gstr1UnlinkedReturnNoteEmittedJsonTests
{
    private const string GstinMaharashtra = "27AAPFU0939F1ZV";
    private static readonly DateOnly FyStart = new(2025, 4, 1);
    private static readonly DateOnly From = new(2025, 4, 1);
    private static readonly DateOnly To = new(2025, 4, 30);
    private static readonly DateOnly SaleDate = new(2025, 4, 9);
    private static readonly DateOnly ReturnDate = new(2025, 4, 20);

    /// <summary>🔴 The taxed HSN row IN THE EMITTED PAYLOAD. Filed 6000000 / 540000 / 3000 before the fix.</summary>
    [Fact]
    public void The_emitted_GSTR1_payload_nets_the_taxed_HSN_row_down()
    {
        using var doc = JsonDocument.Parse(GstReturnJson.Gstr1(BuildBook(), From, To));
        var row = Hsn(doc, "847130");

        Assert.Equal(4_000_000L, row.GetProperty("txval_paisa").GetInt64());   // was 6000000
        Assert.Equal(360_000L, row.GetProperty("camt_paisa").GetInt64());      // was 540000
        Assert.Equal(360_000L, row.GetProperty("samt_paisa").GetInt64());      // was 540000
        Assert.Equal(0L, row.GetProperty("iamt_paisa").GetInt64());
        Assert.Equal(4_720_000L, row.GetProperty("totval_paisa").GetInt64());  // was 7080000
        Assert.Equal(2_000m, row.GetProperty("qty").GetDecimal());             // was 3000
    }

    /// <summary>🔴 T2-59 as filed — the exempt HSN row AND the root exempt cell both grew when an exempt supply was
    /// RETURNED. Both are separate filed cells, so both are asserted.</summary>
    [Fact]
    public void The_emitted_GSTR1_payload_nets_exempt_turnover_down()
    {
        using var doc = JsonDocument.Parse(GstReturnJson.Gstr1(BuildBook(), From, To));

        Assert.Equal(1_500_000L, Hsn(doc, "040110").GetProperty("txval_paisa").GetInt64());   // was 2500000
        Assert.Equal(1_500m, Hsn(doc, "040110").GetProperty("qty").GetDecimal());             // was 2500
        Assert.Equal(
            1_500_000L,
            doc.RootElement.GetProperty("nil_exempt_nongst_paisa").GetInt64());               // was 2500000
    }

    /// <summary>
    /// 🔴 The filed output-tax totals, and the invariant behind them: <c>Gstr1</c>'s own summary promises the
    /// section totals "reconcile to the Output tax-ledger postings for the period". The emitted payload is compared
    /// against the LEDGER's net movement (credits less debits, as the engine posted them), not against another
    /// projection — so a magnitude-summing emitter cannot satisfy it.
    /// </summary>
    [Fact]
    public void The_emitted_GSTR1_totals_reconcile_to_the_Output_tax_ledger()
    {
        var company = BuildBook();
        using var doc = JsonDocument.Parse(GstReturnJson.Gstr1(company, From, To));
        var gst = new GstService(company);

        var cgstLedger = gst.FindTaxLedger(GstTaxHead.Central, GstTaxDirection.Output)!;
        var sgstLedger = gst.FindTaxLedger(GstTaxHead.State, GstTaxDirection.Output)!;

        Assert.Equal(3_600.00m, NetCredit(company, cgstLedger.Id));   // the fixture's premise, held here
        Assert.Equal(3_600.00m, NetCredit(company, sgstLedger.Id));

        Assert.Equal(360_000L, doc.RootElement.GetProperty("total_cgst_paisa").GetInt64());   // was 540000
        Assert.Equal(360_000L, doc.RootElement.GetProperty("total_sgst_paisa").GetInt64());
        Assert.Equal(0L, doc.RootElement.GetProperty("total_igst_paisa").GetInt64());
    }

    /// <summary>
    /// A reversal is not an invoice, so the unlinked note must not be EMITTED as a positive <c>b2b</c> invoice
    /// object — it was, carrying the returned value as though a second sale had been made. The sale's own object
    /// must still be there and unchanged, so that a fix which emitted an EMPTY <c>b2b</c> could not pass.
    /// </summary>
    [Fact]
    public void The_unlinked_note_is_not_emitted_as_a_b2b_invoice_object()
    {
        using var doc = JsonDocument.Parse(GstReturnJson.Gstr1(BuildBook(), From, To));
        var b2b = doc.RootElement.GetProperty("b2b");

        Assert.Equal(1, b2b.GetArrayLength());                                      // was 2
        Assert.Equal(5_000_000L, b2b[0].GetProperty("txval_paisa").GetInt64());     // the SALE, untouched
        Assert.Equal(450_000L, b2b[0].GetProperty("camt_paisa").GetInt64());

        // 🔴 And not in b2cs either. B2B/B2C is an if/else in Gstr1.Build, so gating only the B2B arm pushes the
        // reversal into the B2C consolidation instead — I made that exact mistake mid-track and every test here
        // stayed green, because this fixture's party is registered. Asserting the EMPTY side of the partition is
        // what catches it. (The wrong-FIGURE version of this is pinned in the Ledger suite on an unregistered party.)
        Assert.Equal(0, doc.RootElement.GetProperty("b2cs").GetArrayLength());

        // Both HSNs are still emitted — netting reduces the rows, it never drops a supply from Table 12.
        Assert.Equal(2, doc.RootElement.GetProperty("hsn").GetArrayLength());
    }

    /// <summary><b>ER-13</b> — a book with no return note must emit exactly what it emitted before, since the sign
    /// is +1 on every Sales voucher. Without this a sign-threading error on the invoice path would hide behind the
    /// netted assertions above.</summary>
    [Fact]
    public void A_book_with_no_return_note_emits_unchanged_figures()
    {
        using var doc = JsonDocument.Parse(GstReturnJson.Gstr1(BuildBook(withReturnNote: false), From, To));

        Assert.Equal(5_000_000L, Hsn(doc, "847130").GetProperty("txval_paisa").GetInt64());
        Assert.Equal(450_000L, Hsn(doc, "847130").GetProperty("camt_paisa").GetInt64());
        Assert.Equal(2_500m, Hsn(doc, "847130").GetProperty("qty").GetDecimal());
        Assert.Equal(2_000_000L, Hsn(doc, "040110").GetProperty("txval_paisa").GetInt64());
        Assert.Equal(2_000_000L, doc.RootElement.GetProperty("nil_exempt_nongst_paisa").GetInt64());
        Assert.Equal(450_000L, doc.RootElement.GetProperty("total_cgst_paisa").GetInt64());
    }

    // ================================================================ fixture

    private static JsonElement Hsn(JsonDocument doc, string hsnSac) =>
        doc.RootElement.GetProperty("hsn").EnumerateArray()
            .Single(h => h.GetProperty("hsn_sac").GetString() == hsnSac);

    /// <summary>The ledger's own NET credit movement in the window — credits less debits, which is how the engine
    /// posted the sale (credit) and the return note (debit). Deliberately NOT a report projection.</summary>
    private static decimal NetCredit(Company company, Guid ledgerId) =>
        company.Vouchers
            .Where(v => v.Date >= From && v.Date <= To)
            .SelectMany(v => v.Lines)
            .Where(l => l.LedgerId == ledgerId)
            .Sum(l => l.Side == DrCr.Credit ? l.Amount.Amount : -l.Amount.Amount);

    /// <summary>
    /// One intra-state sale (Widget ₹50,000 @ 18% + EXEMPT Milk ₹20,000) and, unless suppressed, a partial
    /// <b>sales return</b> (Widget ₹10,000 @ 18% + Milk ₹5,000) as an <b>UNLINKED</b> Credit Note.
    /// <para>The note is posted BY HAND rather than through <c>CreditDebitNoteService</c>, which always creates the
    /// §34 link and therefore always takes the already-correct excluded path — which is exactly why no existing
    /// test in any project reached this defect. <c>reverseSides: true</c> reproduces what the entry screen posts
    /// when the §34 toggle is left off (<c>ComputeItemInvoiceGst</c>'s own <c>reverseSides: IsReturnNote</c>).</para>
    /// </summary>
    private static Company BuildBook(bool withReturnNote = true)
    {
        var c = CompanyFactory.CreateSeeded("Return Note Io Co", FyStart);
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

        // ---- the SALE. Only the Widget is taxable ⇒ ONE 1800 rate group on ₹50,000.
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
