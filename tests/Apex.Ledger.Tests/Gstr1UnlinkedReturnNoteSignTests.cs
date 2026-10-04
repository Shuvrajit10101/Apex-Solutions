using Apex.Ledger.Domain;
using Apex.Ledger.Reports;
using Apex.Ledger.Services;
using Xunit;

namespace Apex.Ledger.Tests;

/// <summary>
/// 🔴 <b>T2-59 — GSTR-1 read POSTED MAGNITUDES and ignored the side, so a sales return was FILED AS A SALE.</b>
///
/// <para><b>The root cause, measured first-hand rather than inferred.</b> <c>Gstr1.Build</c> excludes a
/// <i>formalised</i> §34 note from its ordinary sweep (<c>CdnLinkFor(...) is not null ⇒ continue</c>) because
/// Table 9B projects those, signed, off the link collection. But the §34 link is <b>opt-in</b>:
/// <c>VoucherEntryViewModel.IsSection34Note</c> defaults false and its own doc comment calls the link "an optional
/// annotation whose absence keeps the reports byte-identical". So a Credit Note posted with that toggle OFF has no
/// link, is NOT excluded, and falls through into the ordinary invoice sweep — where <c>ReadInvoiceHeads</c> and
/// <c>AddHsnRow</c> read <c>line.Amount.Amount</c>, a positive MAGNITUDE, and never look at
/// <see cref="DrCr"/>.</para>
///
/// <para><b>The posting was never wrong — only the report.</b> <c>ComputeItemInvoiceGst</c> passes
/// <c>reverseSides: IsReturnNote</c>, and <see cref="VoucherEffects.IsReturnNote"/> derives from the BASE TYPE
/// alone, wholly independent of the §34 toggle. So the note genuinely DEBITS Output CGST and its stock comes back
/// IN; the Output ledger nets down correctly. The report then re-added it. That breaks the invariant
/// <see cref="Gstr1"/>'s own summary promises in writing — "the section totals reconcile to the Output tax-ledger
/// postings for the period" — which is what settles the treatment here, because the vendor is silent on it (see
/// the grounding note below).</para>
///
/// <para><b>🔴 Figures worked BY HAND; these are the money.</b> One intra-state (27→27) sales invoice: Widget
/// 2,500 Nos × ₹20 = ₹50,000 @ 18% (HSN 847130) + EXEMPT Fresh Milk 2,000 × ₹10 = ₹20,000 (HSN 040110).
/// Posted tax = 18% × 50,000 = ₹9,000 ⇒ CGST ₹4,500 + SGST ₹4,500. Then ONE unlinked Credit Note (a sales
/// return): Widget 500 × ₹20 = ₹10,000 @ 18% + Milk 500 × ₹10 = ₹5,000 exempt. Posted tax = 18% × 10,000 =
/// ₹1,800 ⇒ CGST ₹900 + SGST ₹900, both on the DEBIT (reducing) side.</para>
/// <list type="bullet">
/// <item><b>Correct, and what these tests assert.</b> 847130 taxable 50,000 − 10,000 = ₹40,000, CGST
/// 4,500 − 900 = ₹3,600, SGST ₹3,600, quantity 2,500 − 500 = 2,000 Nos, Total Value 40,000 + 7,200 = ₹47,200.
/// 040110 taxable 20,000 − 5,000 = ₹15,000, quantity 1,500 Nos. Exempt bucket ₹15,000. TotalCgst/TotalSgst
/// ₹3,600 each — which is exactly the Output CGST / SGST ledger balance for the period.</item>
/// <item><b>What it filed before this fix.</b> 847130 taxable ₹60,000, CGST ₹5,400, SGST ₹5,400, quantity 3,000.
/// 040110 taxable ₹25,000, quantity 2,500. Exempt bucket ₹25,000. TotalCgst/TotalSgst ₹5,400. So the return
/// OVERSTATED output tax by ₹1,800, overstated taxable turnover by ₹20,000, overstated EXEMPT turnover by
/// ₹10,000, and overstated a mandatory filed QUANTITY by 1,000 Nos — a return that disagrees with the very
/// ledgers it claims to reconcile to, in the direction of declaring supplies that were reversed.</item>
/// </list>
///
/// <para><b>🔴 Grounding, and what is OURS.</b> <c>help.tallysolutions.com</c>'s GSTR-1 page attests the sections
/// "Credit or Debit Notes (Registered) – 9B" and "(Unregistered) – 9B", and its HSN/SAC Summary page describes
/// the report as displaying "the tax details for all the HSNs/SACs used in your transactions". The vendor is
/// <b>SILENT</b> on whether a note nets the HSN summary down, and it has no opt-in §34 toggle to be silent about.
/// 🔴 <b>A CLAIM WAS STRUCK FROM THIS PARAGRAPH 2026-10-04 BY A12 BECAUSE IT WAS NOT PUBLISHED AND WOULD HAVE
/// BEEN THE FIFTH FABRICATED VENDOR CLAIM CAUGHT ON THIS PROJECT.</b> It read, as vendor fact, that
/// <i>"in the vendor product a note missing its original-invoice details is held in 'Transactions with
/// Incomplete/Mismatch in Information' and excluded from the return until resolved."</i> The wave-42 reviewer
/// opened that page BY CONTENT: it is titled <b>Exception Types</b> and publishes <b>no such rule and no such
/// section name</b>; the GSTR-1 page says <b>"Uncertain Transactions (Corrections needed)"</b>. The sentence is
/// deleted rather than re-pointed, because <b>the netting rule below does not depend on it</b> — it is labelled
/// OURS and stands on this module's own invariant. <b>Do not reinstate it without a by-content citation.</b>
/// So <b>netting the aggregates is OURS</b>, chosen because this module's own documented invariant
/// (reconcile to the Output tax-ledger postings) admits no other answer, and because the alternative — silently
/// dropping the note — would hide a reversal that actually traded. <b>The remaining divergence is reported, not
/// hidden:</b> an unlinked note gets no Table 9B row of its own, because 9B needs the original-invoice reference
/// the user declined to give.</para>
///
/// <para><b>Why these tests are the deliverable as much as the fix.</b> Not one existing test posts a Credit Note
/// WITHOUT a §34 link and then reads GSTR-1 — every credit-note test in the suite goes through
/// <c>CreditDebitNoteService.BuildCreditDebitNote</c>, which ALWAYS creates the link and therefore always takes
/// the excluded path. The defect lived in the one shape the suite never built.</para>
/// </summary>
public class Gstr1UnlinkedReturnNoteSignTests
{
    private static readonly DateOnly FyStart = new(2024, 4, 1);
    private static readonly DateOnly From = new(2024, 4, 1);
    private static readonly DateOnly To = new(2024, 4, 30);
    private static readonly DateOnly SaleDate = new(2024, 4, 9);
    private static readonly DateOnly ReturnDate = new(2024, 4, 20);

    private const string GstinMaharashtra = "27AAPFU0939F1ZV";
    private const string TaxedHsn = "847130";   // Widget @ 18%
    private const string ExemptHsn = "040110";  // Fresh Milk — exempt

    /// <summary>🔴 THE DEFECT, on the taxed HSN. Filed 60,000 / 5,400 / 5,400 instead of 40,000 / 3,600 / 3,600.</summary>
    [Fact]
    public void An_unlinked_sales_return_nets_the_taxed_HSN_row_down()
    {
        var widget = Row(Report.BuildGstr1(BuildBook(), From, To), TaxedHsn);

        Assert.Equal(40_000.00m, widget.TaxableValue.Amount);   // was 60,000
        Assert.Equal(3_600.00m, widget.Cgst.Amount);            // was 5,400
        Assert.Equal(3_600.00m, widget.Sgst.Amount);            // was 5,400
        Assert.Equal(0.00m, widget.Igst.Amount);
        Assert.Equal(47_200.00m, widget.TotalValue.Amount);     // was 70,800
    }

    /// <summary>🔴 T2-59 AS FILED: Table 12 read magnitudes, so the EXEMPT row and the exempt bucket both grew when
    /// an exempt supply was RETURNED. Asserted on both the row and the bucket — they are separate filed cells and a
    /// fix that corrected one and not the other would still file a wrong return.</summary>
    [Fact]
    public void An_unlinked_sales_return_nets_exempt_turnover_down_on_the_row_and_in_the_bucket()
    {
        var r = Report.BuildGstr1(BuildBook(), From, To);

        Assert.Equal(15_000.00m, Row(r, ExemptHsn).TaxableValue.Amount);   // was 25,000
        Assert.Equal(15_000.00m, Row(r, ExemptHsn).TotalValue.Amount);     // was 25,000
        Assert.Equal(15_000.00m, r.ExemptNilNonGstValue.Amount);           // was 25,000
    }

    /// <summary>🔴 The filed QUANTITY is a mandatory Table-12 cell in its own right, and it was overstated by the
    /// returned quantity rather than reduced by it — a separate misstatement from the money, so it is asserted
    /// separately.</summary>
    [Fact]
    public void An_unlinked_sales_return_nets_the_filed_Table_12_quantity_down()
    {
        var r = Report.BuildGstr1(BuildBook(), From, To);

        Assert.Equal(2_000m, Row(r, TaxedHsn).Quantity);    // 2,500 sold − 500 returned; was 3,000
        Assert.Equal(1_500m, Row(r, ExemptHsn).Quantity);   // 2,000 sold − 500 returned; was 2,500
    }

    /// <summary>
    /// 🔴 <b>THE INVARIANT THE DEFECT BROKE, asserted against the LEDGER rather than against another projection.</b>
    /// <see cref="Gstr1"/> promises its totals "reconcile to the Output tax-ledger postings for the period". This
    /// reads the Output CGST/SGST ledger's own net movement — debits subtracted, exactly as the engine posted them —
    /// and requires the filed totals to equal it. A magnitude-summing report cannot satisfy this, which is why it is
    /// the test that matters most here: it would still fail even if someone "fixed" the HSN rows alone.
    /// </summary>
    [Fact]
    public void The_filed_output_tax_totals_equal_the_Output_tax_ledger_net_movement()
    {
        var company = BuildBook();
        var r = Report.BuildGstr1(company, From, To);
        var gst = new GstService(company);

        var outputCgst = gst.FindTaxLedger(GstTaxHead.Central, GstTaxDirection.Output)!;
        var outputSgst = gst.FindTaxLedger(GstTaxHead.State, GstTaxDirection.Output)!;

        Assert.Equal(3_600.00m, NetCredit(company, outputCgst.Id));   // the fixture's own premise, held here
        Assert.Equal(3_600.00m, NetCredit(company, outputSgst.Id));

        Assert.Equal(NetCredit(company, outputCgst.Id), r.TotalCgst.Amount);   // filed 5,400 against a ledger of 3,600
        Assert.Equal(NetCredit(company, outputSgst.Id), r.TotalSgst.Amount);
        Assert.Equal(0.00m, r.TotalIgst.Amount);
    }

    /// <summary>
    /// A reversal is not an invoice, so the unlinked note must not be filed as a positive B2B invoice row either —
    /// the vendor states notes in 9B, never in 4A. Pinned so the netting fix cannot be "simplified" later into
    /// signing the B2B row instead, which would put a negative invoice in the document-level table.
    /// <para>And the sale itself must still be there, unchanged — a fix that dropped BOTH documents would make every
    /// assertion above pass against an empty return.</para>
    /// </summary>
    [Fact]
    public void The_unlinked_note_is_not_filed_as_a_B2B_invoice_but_the_sale_still_is()
    {
        var r = Report.BuildGstr1(BuildBook(), From, To);

        var b2b = Assert.Single(r.B2B);
        Assert.Equal(50_000.00m, b2b.TaxableValue.Amount);   // the SALE's own row, untouched by the netting
        Assert.Equal(4_500.00m, b2b.Cgst.Amount);
        Assert.Equal(4_500.00m, b2b.Sgst.Amount);

        // 🔴 AND NOT IN B2C EITHER — the assertion that catches the real mistake. B2B/B2C is an if/else, so
        // gating only the B2B arm pushes the reversal down the else into the B2C consolidation, filed POSITIVELY.
        // I wrote that exact bug while tidying the branch and every other test here stayed green, because this
        // fixture's party is registered. A reversal belongs in NEITHER invoice table.
        Assert.Empty(r.B2C);

        // Both HSNs are still filed — netting must reduce the rows, never drop the supply from Table 12.
        Assert.Equal(2, r.HsnSummary.Count);
    }

    /// <summary>
    /// 🔴 The same rule with an <b>UNREGISTERED</b> party, which is the path that actually reaches the B2C arm.
    /// With a B2C party the sale itself consolidates into <c>B2C</c> and <c>B2B</c> is empty, so this is the only
    /// shape in which a reversal leaking into the B2C consolidation is visible as a WRONG FIGURE rather than just
    /// an extra row: B2C must carry the sale's ₹50,000 @18% and nothing of the ₹10,000 return.
    /// </summary>
    [Fact]
    public void An_unlinked_return_note_to_an_UNREGISTERED_party_does_not_reach_the_B2C_consolidation()
    {
        var r = Report.BuildGstr1(BuildBook(registeredParty: false), From, To);

        Assert.Empty(r.B2B);
        var b2c = Assert.Single(r.B2C);
        Assert.Equal(1800, b2c.RateBasisPoints);
        Assert.Equal(50_000.00m, b2c.TaxableValue.Amount);   // the SALE only — 60,000 if the reversal leaked in
        Assert.Equal(4_500.00m, b2c.Cgst.Amount);            // 5,400 if it leaked in
        Assert.Equal(4_500.00m, b2c.Sgst.Amount);

        // The aggregates still net, exactly as for a registered party.
        Assert.Equal(40_000.00m, Row(r, TaxedHsn).TaxableValue.Amount);
        Assert.Equal(3_600.00m, r.TotalCgst.Amount);
        Assert.Equal(15_000.00m, r.ExemptNilNonGstValue.Amount);
    }

    /// <summary>
    /// <b>ER-13 — an ordinary book with no return note must be byte-identical.</b> The whole fix is a sign that is
    /// +1 on every Sales voucher, so a book of plain invoices must file exactly what it filed before. Without this,
    /// a sign-threading error that flipped the invoice path too would be invisible behind the netted assertions.
    /// </summary>
    [Fact]
    public void A_book_with_no_return_note_is_unchanged()
    {
        var r = Report.BuildGstr1(BuildBook(withReturnNote: false), From, To);

        Assert.Equal(50_000.00m, Row(r, TaxedHsn).TaxableValue.Amount);
        Assert.Equal(4_500.00m, Row(r, TaxedHsn).Cgst.Amount);
        Assert.Equal(2_500m, Row(r, TaxedHsn).Quantity);
        Assert.Equal(20_000.00m, Row(r, ExemptHsn).TaxableValue.Amount);
        Assert.Equal(20_000.00m, r.ExemptNilNonGstValue.Amount);
        Assert.Equal(4_500.00m, r.TotalCgst.Amount);
        Assert.Equal(4_500.00m, r.TotalSgst.Amount);
    }

    /// <summary>
    /// A <b>LINKED</b> §34 note must keep taking the Table 9B path and must NOT be netted twice. This is the
    /// regression that a careless fix breaks: if the sign were applied to the main sweep without the CDN-link
    /// exclusion still short-circuiting first, the linked note would net the aggregates AND be projected into the
    /// totals by <c>BuildTable9B</c>, double-reducing the return.
    /// </summary>
    [Fact]
    public void A_linked_section_34_note_is_still_projected_once_through_Table_9B()
    {
        var company = BuildBook(linkTheNote: true);
        var r = Report.BuildGstr1(company, From, To);

        // Projected as a 9B row, signed negative, exactly as before this change.
        var note = Assert.Single(r.Table9B);
        Assert.Equal(CdnType.Credit, note.NoteType);
        Assert.Equal(-10_000.00m, note.TaxableValue.Amount);
        Assert.Equal(-900.00m, note.Cgst.Amount);

        // Netted into the totals ONCE — 4,500 − 900 — not twice (which would read 2,700).
        Assert.Equal(3_600.00m, r.TotalCgst.Amount);
        Assert.Equal(3_600.00m, r.TotalSgst.Amount);

        // And the linked note stays OUT of the HSN sweep, as it always has: Table 12 shows the sale only.
        Assert.Equal(50_000.00m, Row(r, TaxedHsn).TaxableValue.Amount);
        Assert.Equal(20_000.00m, r.ExemptNilNonGstValue.Amount);
    }

    /// <summary>
    /// 🔴 <b>THE ANNUAL RETURN CARRIED THE SAME OVERSTATEMENT, AND IT IS A SEPARATE FILED DOCUMENT.</b>
    /// <c>Gstr9.Build</c> folds <c>Gstr1.Build(...).HsnSummary</c> period by period into <b>Table 17</b>, so the
    /// magnitude defect propagated straight into GSTR-9 — and GSTR-9 is filed once a year against twelve months of
    /// returns, which is exactly where a reversal that was added instead of subtracted compounds. It is asserted
    /// here rather than assumed from the shared code path, because "the fold inherits the fix" is the kind of claim
    /// this project has had to retract before.
    /// <para>Same fixture, read over the whole FY: Table 17 must show the taxed HSN at ₹40,000 taxable / ₹3,600 per
    /// head and the exempt HSN at ₹15,000 — not ₹60,000 / ₹5,400 / ₹25,000.</para>
    /// </summary>
    [Fact]
    public void The_annual_return_Table_17_nets_the_unlinked_sales_return_down_too()
    {
        var r = Gstr9.Build(BuildBook(), FyStart, new DateOnly(2025, 3, 31));

        var widget = r.Table17Hsn.Single(h => h.HsnSac == TaxedHsn);
        Assert.Equal(40_000.00m, widget.TaxableValue.Amount);   // was 60,000
        Assert.Equal(3_600.00m, widget.Cgst.Amount);            // was 5,400
        Assert.Equal(3_600.00m, widget.Sgst.Amount);            // was 5,400
        Assert.Equal(2_000m, widget.Quantity);                  // was 3,000

        Assert.Equal(15_000.00m, r.Table17Hsn.Single(h => h.HsnSac == ExemptHsn).TaxableValue.Amount);

        // And the annual roll-ups derived from those rows move with them.
        Assert.Equal(55_000.00m, r.Table17TaxableValue.Amount);   // 40,000 + 15,000; was 85,000
        Assert.Equal(7_200.00m, r.Table17TotalTax.Amount);        // 3,600 + 3,600; was 10,800
    }

    // ================================================================ fixture

    private static Gstr1HsnRow Row(Gstr1 r, string hsn) => r.HsnSummary.Single(h => h.HsnSac == hsn);

    /// <summary>The ledger's own NET credit movement in the window — credits less debits, which is how the engine
    /// actually posted the sale (credit) and the return note (debit). Deliberately NOT a report projection: the
    /// point of the invariant test is to compare the filed figure against the BOOKS.</summary>
    private static decimal NetCredit(Company company, Guid ledgerId) =>
        company.Vouchers
            .Where(v => v.Date >= From && v.Date <= To)
            .SelectMany(v => v.Lines)
            .Where(l => l.LedgerId == ledgerId)
            .Sum(l => l.Side == DrCr.Credit ? l.Amount.Amount : -l.Amount.Amount);

    /// <summary>
    /// One intra-state sales invoice (Widget ₹50,000 @ 18% + EXEMPT Milk ₹20,000) and, unless suppressed, one
    /// <b>sales return</b> of part of it (Widget ₹10,000 @ 18% + Milk ₹5,000).
    /// <para><b>The note is built BY HAND rather than through <c>CreditDebitNoteService</c>, deliberately.</b> That
    /// service ALWAYS calls <c>AddCreditDebitNoteLink</c>, so every existing credit-note test in the suite produces
    /// a LINKED note and therefore always exercises the excluded path. Posting the legs directly is what the entry
    /// screen does when <c>IsSection34Note</c> is left off — reversed sides via <c>reverseSides: true</c>, which is
    /// precisely <c>ComputeItemInvoiceGst</c>'s own <c>reverseSides: IsReturnNote</c> — and it is the only way to
    /// reach the defect.</para>
    /// </summary>
    private static Company BuildBook(bool withReturnNote = true, bool linkTheNote = false, bool registeredParty = true)
    {
        var c = CompanyFactory.CreateSeeded("Return Note Co", FyStart);
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
            HsnSac = TaxedHsn, Taxability = GstTaxability.Taxable, RateBasisPoints = 1800,
        };

        var milk = inv.CreateStockItem("Fresh Milk", grp.Id, nos.Id);
        milk.Gst = new StockItemGstDetails { HsnSac = ExemptHsn, Taxability = GstTaxability.Exempt };

        inv.AddOpeningBalance(widget.Id, main, 10_000m, Money.FromRupees(20m));
        inv.AddOpeningBalance(milk.Id, main, 10_000m, Money.FromRupees(10m));

        var sales = Add(c, "Sales", "Sales Accounts", false);
        var debtor = Add(c, "Debtor", "Sundry Debtors", true);
        // Registered ⇒ the sale lands in B2B. Unregistered ⇒ IsB2C is true (no GSTIN + Consumer), so the sale
        // consolidates into B2C instead — the arm a leaking reversal would corrupt. Same State either way, so the
        // supply stays intra-state and the tax split is unchanged.
        debtor.PartyGst = registeredParty
            ? new PartyGstDetails
            {
                RegistrationType = GstRegistrationType.Regular, Gstin = GstinMaharashtra, StateCode = "27",
            }
            : new PartyGstDetails { RegistrationType = GstRegistrationType.Consumer, StateCode = "27" };

        var salesType = c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Sales).Id;

        // ---- the SALE. Only the Widget is taxable, so the invoice posts ONE 1800 rate group on ₹50,000.
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

        // ---- the SALES RETURN, as a Credit Note. reverseSides puts every tax leg on the DEBIT side while keeping
        // the Output head, which is exactly what the entry screen posts for a return note.
        var noteTax = gst.ComputeInvoiceTax(
            new[] { new GstService.TaxableLine(Money.FromRupees(10_000m), 1800) },
            interState: false, GstTaxDirection.Output, reverseSides: true);
        Assert.Equal(1_800.00m, noteTax.TotalTax.Amount);

        var noteLines = new List<EntryLine>
        {
            new(sales.Id, Money.FromRupees(15_000m), DrCr.Debit),                                   // reduces income
            new(debtor.Id, Money.FromRupees(15_000m + noteTax.TotalTax.Amount), DrCr.Credit),       // reduces the debt
        };
        noteLines.AddRange(noteTax.TaxLines);

        var noteId = Guid.NewGuid();
        var noteType = c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.CreditNote).Id;
        ledgers.Post(new Voucher(noteId, noteType, ReturnDate, noteLines, partyId: debtor.Id, inventoryLines: new[]
        {
            new VoucherInventoryLine(widget.Id, main, 500m, Money.FromRupees(20m)),   // 500 × 20 = 10,000
            new VoucherInventoryLine(milk.Id, main, 500m, Money.FromRupees(10m)),     // 500 × 10 = 5,000
        }));

        // The §34 annotation is OPT-IN. Off (the default) ⇒ the defect's path. On ⇒ the Table 9B path, which the
        // linked-note regression above pins.
        if (linkTheNote)
            c.AddCreditDebitNoteLink(new GstCreditDebitNoteLink(
                Guid.NewGuid(), noteId, CdnType.Credit, null, "S/1", SaleDate, "01 sales return", is9BTarget: true));

        return c;
    }

    private static Domain.Ledger Add(Company c, string name, string groupName, bool openingIsDebit)
    {
        var l = new Domain.Ledger(Guid.NewGuid(), name, c.FindGroupByName(groupName)!.Id, Money.Zero, openingIsDebit);
        c.AddLedger(l);
        return l;
    }
}
