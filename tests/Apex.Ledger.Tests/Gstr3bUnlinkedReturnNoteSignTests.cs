using Apex.Ledger.Domain;
using Apex.Ledger.Reports;
using Apex.Ledger.Services;
using Xunit;

namespace Apex.Ledger.Tests;

/// <summary>
/// 🔴 <b>T2-92 / T2-93 — GSTR-3B's outward sweep read POSTED MAGNITUDES, so a sales return was FILED AS A SALE
/// in a SECOND filed return; and a MIXED invoice filed ZERO exempt turnover.</b>
///
/// <para><b>Two defects, one fixture, both measured first-hand on <c>origin/main</c> b754016.</b></para>
///
/// <list type="number">
/// <item><b>T2-92 — the unsigned outward sweep.</b> <c>Gstr3b.ReadSide</c> summed <c>line.Amount.Amount</c>, a
/// positive MAGNITUDE, and <c>Gstr3b.cs</c> contained <b>zero</b> references to
/// <see cref="VoucherEffects.IsReturnNote"/> or <see cref="EntryLine.Side"/> — verified by literal grep, not
/// inferred. It excluded only a <i>formalised</i> §34 note (<c>CdnLinkFor(...) is not null ⇒ continue</c>), but the
/// §34 link is OPT-IN, so an ordinary keyboard-entered Credit Note has no link, is not excluded, and was re-added
/// to §3.1(a) as though it were a fresh supply. This is the IDENTICAL defect T2-59 fixed in GSTR-1 — the
/// sign-of-a-return-note class, now closed at its fifth site.</item>
/// <item><b>T2-93 — the wholesale exempt skip.</b> <c>ExemptOutwardValue</c> dropped any voucher where
/// <c>v.Lines.Any(l =&gt; l.HasGst)</c>, i.e. ANY voucher carrying ANY taxed line. A single invoice with one taxed
/// and one exempt line therefore lost its <b>whole</b> exempt leg: §3.1(c) filed ZERO while GSTR-1 declared the
/// real exempt turnover off the same books.</item>
/// </list>
///
/// <para><b>🔴 Figures worked BY HAND; these are the money.</b> One intra-state (27→27) sales invoice: Widget
/// 2,500 Nos × ₹20 = ₹50,000 @ 18% + <b>EXEMPT</b> Fresh Milk 2,000 × ₹10 = ₹20,000 — a MIXED invoice, which is
/// what reaches T2-93. Posted tax = 18% × 50,000 = ₹9,000 ⇒ CGST ₹4,500 + SGST ₹4,500. Then ONE <b>unlinked</b>
/// Credit Note (a sales return): Widget 500 × ₹20 = ₹10,000 @ 18% + Milk 500 × ₹10 = ₹5,000 exempt. Posted tax =
/// 18% × 10,000 = ₹1,800 ⇒ CGST ₹900 + SGST ₹900, both on the DEBIT (reducing) side.</para>
/// <list type="bullet">
/// <item><b>Correct, and what these tests assert.</b> §3.1(a) taxable 50,000 − 10,000 = <b>₹40,000</b>; CGST
/// 4,500 − 900 = <b>₹3,600</b>; SGST <b>₹3,600</b>. §3.1(c) exempt 20,000 − 5,000 = <b>₹15,000</b>.</item>
/// <item><b>What main filed.</b> §3.1(a) taxable <b>₹60,000</b>, CGST <b>₹5,400</b>, SGST <b>₹5,400</b> against an
/// Output CGST ledger that had netted to ₹3,600; §3.1(c) exempt <b>₹0</b>. So the return overstated output tax by
/// ₹1,800, overstated taxable turnover by ₹20,000, and under-declared exempt turnover by ₹15,000 — in the
/// direction of declaring supplies that were reversed while hiding supplies that were made.</item>
/// </list>
///
/// <para><b>🔴 The annual return is the reason this is BLOCKING.</b> <c>Gstr9.Build</c> takes Table 4/5 from
/// <c>Gstr3b.Build</c> (Gstr9.cs:159) and Table 17 from <c>Gstr1.Build</c> (Gstr9.cs:171). Once T2-59 fixed GSTR-1
/// alone, ONE annual return declared Table 4 tax ₹10,800 against Table 17 tax ₹7,200, and Table 5N turnover
/// ₹60,000 against Table 17 turnover ₹85,000 — rendered on one screen and emitted into one JSON. A document that
/// contradicts itself is a queried or rejected filing. These tests pin the reconciliation, not just the figure.</para>
///
/// <para><b>🔴 Grounding, and what is OURS.</b> <c>help.tallysolutions.com</c>'s GSTR-3B page attests the
/// §3.1(a) "Outward taxable supplies (other than zero rated, nil rated and exempted)" and §3.1(c) "Other outward
/// supplies (Nil rated, exempted)" rows, and its GSTR-1 page attests the "Credit or Debit Notes" §34 sections. The
/// vendor is <b>SILENT</b> on how an UNLINKED note is treated — it has no opt-in §34 toggle to be silent about —
/// so <b>the netting rule is OURS</b>, carried over unchanged from T2-59 rather than re-decided here: AGGREGATE
/// cells net a reversal, DOCUMENT-level tables exclude it. It is chosen because it is the only reading under which
/// §3.1(a) reconciles to the Output tax-ledger postings, which is the invariant asserted below against the BOOKS.
/// <b>No vendor rule is asserted here that a page does not publish.</b></para>
///
/// <para><b>Scope refused, explicitly.</b> <c>ReadSide</c> is shared with the INWARD (ITC) direction, and
/// <see cref="VoucherEffects.IsReturnNote"/> is true for a <b>Debit Note</b> too — so signing it unconditionally
/// would change filed ITC figures. That surface is owned by a sibling branch, so the sign here is gated to the
/// OUTWARD direction and the inward twin is reported rather than edited. See the note on
/// <c>Gstr3b.ReadSide</c>.</para>
/// </summary>
public class Gstr3bUnlinkedReturnNoteSignTests
{
    private static readonly DateOnly FyStart = new(2024, 4, 1);
    private static readonly DateOnly From = new(2024, 4, 1);
    private static readonly DateOnly To = new(2024, 4, 30);
    private static readonly DateOnly SaleDate = new(2024, 4, 9);
    private static readonly DateOnly ReturnDate = new(2024, 4, 20);

    private const string GstinMaharashtra = "27AAPFU0939F1ZV";
    private const string TaxedHsn = "847130";   // Widget @ 18%
    private const string ExemptHsn = "040110";  // Fresh Milk — exempt

    /// <summary>🔴 T2-92, the money. §3.1(a) filed 60,000 / 5,400 / 5,400 instead of 40,000 / 3,600 / 3,600.</summary>
    [Fact]
    public void An_unlinked_sales_return_nets_down_3_1_a_taxable_value_and_tax()
    {
        var r = Report.BuildGstr3b(BuildBook(), From, To);

        Assert.Equal(40_000.00m, r.TaxableOutwardValue.Amount);   // was 60,000
        Assert.Equal(3_600.00m, r.OutwardCgst.Amount);            // was 5,400
        Assert.Equal(3_600.00m, r.OutwardSgst.Amount);            // was 5,400
        Assert.Equal(0.00m, r.OutwardIgst.Amount);
    }

    /// <summary>
    /// 🔴 <b>THE INVARIANT THE DEFECT BROKE, asserted against the LEDGER rather than against another projection.</b>
    /// The engine posted the sale's tax legs on the CREDIT side of Output CGST and the return note's on the DEBIT
    /// side (<c>ComputeItemInvoiceGst</c> passes <c>reverseSides: IsReturnNote</c>, independent of the §34 toggle),
    /// so the ledger itself had already netted to ₹3,600. GSTR-3B §3.1(a) declared ₹5,400 against those very
    /// books. Comparing the filed figure to the POSTED ENTRIES is the only check that cannot be satisfied by two
    /// projections agreeing on the same wrong answer.
    /// </summary>
    [Fact]
    public void Filed_3_1_a_tax_equals_the_output_tax_ledger_net_movement()
    {
        var c = BuildBook();
        var r = Report.BuildGstr3b(c, From, To);

        var outputCgst = c.Ledgers.Single(l => l.Name == "Output CGST").Id;
        var outputSgst = c.Ledgers.Single(l => l.Name == "Output SGST").Id;

        Assert.Equal(3_600.00m, NetCredit(c, outputCgst));        // the BOOKS
        Assert.Equal(NetCredit(c, outputCgst), r.OutwardCgst.Amount);
        Assert.Equal(NetCredit(c, outputSgst), r.OutwardSgst.Amount);
    }

    /// <summary>
    /// 🔴 <b>T2-93 — the MIXED invoice.</b> The sale carries one taxed line (Widget) and one exempt line (Milk), so
    /// <c>v.Lines.Any(l =&gt; l.HasGst)</c> was true and the voucher was skipped WHOLESALE: §3.1(c) filed ₹0 while
    /// the exempt supply had genuinely been made. Asserted as the absolute figure AND against GSTR-1, because the
    /// two returns are filed off the same books for the same period and may not disagree.
    /// </summary>
    [Fact]
    public void A_mixed_invoice_files_its_exempt_leg_in_3_1_c_instead_of_zero()
    {
        var c = BuildBook();

        var exempt = Report.BuildGstr3b(c, From, To).ExemptNilNonGstOutward.Amount;

        Assert.Equal(15_000.00m, exempt);   // 20,000 sold − 5,000 returned; was 0
        Assert.Equal(Report.BuildGstr1(c, From, To).ExemptNilNonGstValue.Amount, exempt);
    }

    /// <summary>
    /// 🔴 T2-93 without the return note, so the exempt leg of a mixed invoice is pinned on its own rather than only
    /// in combination with the signing fix. A fix that netted correctly but still skipped the mixed voucher would
    /// pass the test above by arithmetic accident (0 − 0); this one cannot be satisfied that way.
    /// </summary>
    [Fact]
    public void A_mixed_invoice_alone_files_its_full_exempt_leg()
    {
        var r = Report.BuildGstr3b(BuildBook(withReturnNote: false), From, To);

        Assert.Equal(20_000.00m, r.ExemptNilNonGstOutward.Amount);   // was 0
        Assert.Equal(50_000.00m, r.TaxableOutwardValue.Amount);      // the taxed leg is unchanged by the T2-93 fix
    }

    /// <summary>
    /// 🔴 <b>THE ANNUAL RETURN MUST NOT CONTRADICT ITSELF — this is the assertion that catches the regression the
    /// GSTR-1-only fix introduced.</b> Table 4/5 come from <c>Gstr3b.Build</c>, Table 17 from <c>Gstr1.Build</c>.
    /// On main both read ₹10,800 (wrong, but agreeing); with GSTR-1 fixed alone they read ₹10,800 against ₹7,200 —
    /// a ₹3,600 hole inside ONE filed document. Both the absolute figure and the equality are asserted: the
    /// absolute figure reddens on main, the equality reddens on the GSTR-1-only branch.
    /// </summary>
    [Fact]
    public void The_annual_return_Table_4_reconciles_with_Table_17()
    {
        var r = Gstr9.Build(BuildBook(), FyStart, new DateOnly(2025, 3, 31));

        Assert.Equal(7_200.00m, r.Table4TotalTax.Amount);            // was 10,800
        Assert.Equal(40_000.00m, r.Table4TaxableValue.Amount);       // was 60,000
        Assert.Equal(r.Table17TotalTax.Amount, r.Table4TotalTax.Amount);
    }

    /// <summary>
    /// 🔴 The turnover half of the same reconciliation, and it was wrong on main TOO: Table 5N
    /// (taxable + exempt) read ₹60,000 + ₹0 = ₹60,000 against a Table 17 turnover of ₹85,000. Correct is
    /// ₹40,000 + ₹15,000 = ₹55,000 on both sides. Asserted separately because 5N and Table 17 are separate filed
    /// cells and the exempt defect alone is enough to split them.
    /// </summary>
    [Fact]
    public void The_annual_return_Table_5N_turnover_reconciles_with_Table_17()
    {
        var r = Gstr9.Build(BuildBook(), FyStart, new DateOnly(2025, 3, 31));

        Assert.Equal(15_000.00m, r.Table5ExemptNilNonGst.Amount);    // was 0
        Assert.Equal(55_000.00m, r.Table5NTurnover.Amount);          // was 60,000
        Assert.Equal(r.Table17TaxableValue.Amount, r.Table5NTurnover.Amount);
    }

    /// <summary>
    /// 🔴 <b>ER-13 BASELINE — a company with no return note must be BYTE-IDENTICAL to before the fix.</b> The sign
    /// is +1 for every ordinary Sales invoice, so a book that never issued a note cannot move. This is the test
    /// that would catch a fix which signed something it should not have.
    /// </summary>
    [Fact]
    public void A_book_with_no_return_note_is_unchanged_by_the_sign()
    {
        var r = Report.BuildGstr3b(BuildBook(withReturnNote: false), From, To);

        Assert.Equal(50_000.00m, r.TaxableOutwardValue.Amount);
        Assert.Equal(4_500.00m, r.OutwardCgst.Amount);
        Assert.Equal(4_500.00m, r.OutwardSgst.Amount);
    }

    /// <summary>
    /// 🔴 <b>THE LOAD-BEARING §34 EXCLUSION MUST STAY FIRST AND STILL WORK.</b> A LINKED §34 note is projected —
    /// signed — by <c>ReadCdn</c> into §3.1(a), so the ordinary sweep must continue to skip it. If the new sign ran
    /// on a linked note as well it would be reduced TWICE: 4,500 − 900 − 900 = ₹2,700. This test pins ₹3,600 and is
    /// the one that reddens if the exclusion is moved after the sign.
    /// </summary>
    [Fact]
    public void A_linked_section_34_note_is_still_reduced_exactly_once()
    {
        var r = Report.BuildGstr3b(BuildBook(linkTheNote: true), From, To);

        Assert.Equal(3_600.00m, r.OutwardCgst.Amount);   // NOT 2,700 (the double reduction)
        Assert.Equal(3_600.00m, r.OutwardSgst.Amount);
    }

    // ================================================================ fixture

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
    /// One intra-state <b>MIXED</b> sales invoice (Widget ₹50,000 @ 18% + EXEMPT Milk ₹20,000) and, unless
    /// suppressed, one <b>sales return</b> of part of it (Widget ₹10,000 @ 18% + Milk ₹5,000).
    /// <para><b>The note is built BY HAND rather than through <c>CreditDebitNoteService</c>, deliberately.</b> That
    /// service ALWAYS calls <c>AddCreditDebitNoteLink</c>, so every existing credit-note test in the suite produces
    /// a LINKED note and therefore always exercises the excluded path. Posting the legs directly is what the entry
    /// screen does when <c>IsSection34Note</c> is left off — reversed sides via <c>reverseSides: true</c>, which is
    /// precisely <c>ComputeItemInvoiceGst</c>'s own <c>reverseSides: IsReturnNote</c> — and it is the only way to
    /// reach the defect. The fixture mirrors <c>Gstr1UnlinkedReturnNoteSignTests</c> on purpose: the two returns
    /// are filed off the SAME books and the tests above assert they agree.</para>
    /// </summary>
    private static Company BuildBook(bool withReturnNote = true, bool linkTheNote = false)
    {
        var c = CompanyFactory.CreateSeeded("Return Note 3B Co", FyStart);
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
        debtor.PartyGst = new PartyGstDetails
        {
            RegistrationType = GstRegistrationType.Regular, Gstin = GstinMaharashtra, StateCode = "27",
        };

        var salesType = c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Sales).Id;

        // ---- the MIXED SALE. Only the Widget is taxable, so the invoice posts ONE 1800 rate group on ₹50,000
        // while the Milk line is exempt — which is exactly the shape T2-93's wholesale skip discarded.
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

        // The §34 annotation is OPT-IN. Off (the default) ⇒ the defect's path. On ⇒ the ReadCdn path, which the
        // double-reduction regression above pins.
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
