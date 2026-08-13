using System;
using System.IO;
using System.Linq;
using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Io;
using Apex.Ledger.Reports;
using Apex.Ledger.Services;
using Apex.Desktop.Services;
using Apex.Desktop.ViewModels;
using DomainLedger = Apex.Ledger.Domain.Ledger;

namespace Apex.Desktop.Tests;

/// <summary>
/// <b>W0-10 — THE PRINTED TOTAL MUST EQUAL THE POSTED DEBT. One projector, ONE source of truth for money.</b>
///
/// <para><c>VoucherPrintProjector</c> had two. <c>ProjectServiceInvoice</c> read the <b>POSTED</b> legs
/// (<c>GstReportSupport.ReadPostedRateGroups</c> / <c>PostedForwardRouting</c> / <c>PostedCessTotal</c>);
/// <c>ProjectInvoice</c>'s ITEM pass re-derived its head totals, its per-rate breakup rows AND its intra/inter routing
/// from a <b>LIVE</b> <c>GstService.ComputeInvoiceTax</c> over masters that are editable long after the document was
/// issued. Wherever a master had moved since posting, the two disagreed — and the ITEM document then stated a demand
/// that was not the debt the general ledger recorded.</para>
///
/// <para><b>Which source is right, and why it is not a toss-up.</b> A tax invoice is <i>evidence of a liability</i>:
/// CGST Rule 46(m) requires it to state "the amount of tax charged in respect of taxable goods or services", i.e. the
/// tax this supply actually bore, and CGST Act §34 says a figure that later needs changing is changed by a
/// <b>credit/debit note</b> — a NEW document — never by silently reprinting the old one with today's master. The
/// posted legs ARE that history; a live recomputation is a re-derivation of an issued document from mutable data. The
/// same reasoning already forced two earlier figures on this very path onto the posted legs (F4, the ring-fenced
/// Compensation Cess; FIX-F10, the round-off), so this closes the last live one. Sources: CGST Rules 46/49 —
/// <c>https://cbic-gst.gov.in/pdf/01062021-CGST-Rules-2017-Part-A-Rules.pdf</c>; CGST Act §31, §34 —
/// <c>https://cbic-gst.gov.in/pdf/CGST-Act-Updated-30092020.pdf</c>.
/// </para>
///
/// <para><b>Every drift below is reachable through the shipped UI — no import, no tampering.</b> Re-rating a stock
/// item, adding a cess to it, and editing a party's State are all ordinary master edits the app invites; each one used
/// to silently rewrite the money on every already-issued invoice that touched that master.</para>
///
/// <para>Fixtures are deliberately odd-valued (60.125 Nos @ ₹786.64 = ₹47,296.73, ₹4.25 @ ₹63.48 = ₹269.79) — round
/// numbers assert nothing; a 50-paisa defect survived this project's whole life under six round-number assertions.</para>
/// </summary>
public sealed class ItemInvoicePostedTaxTests : IDisposable
{
    private const string GstinMaharashtra = "27AAPFU0939F1ZV";
    private const string GstinGujarat = "24AAACC1206D1ZM";
    private static readonly DateOnly FyStart = new(2024, 4, 1);

    // The one odd-valued supply every fixture bills: 60.125 Nos @ ₹786.64 = ₹47,296.73 exactly.
    private const decimal Qty = 60.125m;
    private const string RateText = "786.64";
    private static readonly Money SupplyValue = Money.FromRupees(47_296.73m);

    // 18% intra on that value: 8,513.41, split 4,256.71 / 4,256.70 (the odd paisa lands on Central).
    private const decimal PostedCgst = 4_256.71m;
    private const decimal PostedSgst = 4_256.70m;
    private const decimal PostedPartyLeg = 55_810.14m;   // 47,296.73 + 4,256.71 + 4,256.70

    private readonly string _tempDir;
    private readonly CompanyStorage _storage;

    public ItemInvoicePostedTaxTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "ApexItemPostedTaxTests_" + Guid.NewGuid().ToString("N"));
        _storage = new CompanyStorage(_tempDir);
    }

    // ================================================================ 1 — a re-rated item may not move an issued total

    /// <summary>
    /// The GST rate on the item master is revised AFTER the invoice was issued — the single commonest master edit
    /// there is, and the one a rate notification forces on every dealer. The already-issued document must keep stating
    /// the tax its own general-ledger legs carry.
    /// <para><b>Bite:</b> take the head totals and the breakup rows from <c>gst.ComputeInvoiceTax</c> (the live
    /// recomputation) instead of <c>GstReportSupport.ReadPostedRateGroups</c> and the reprint states a 28% breakup —
    /// CGST 6,621.54 + SGST 6,621.54 under a Grand Total of ₹60,539.81 — against a posted party debit that never moved
    /// from ₹55,810.14. The customer is billed ₹4,729.67 the books never recorded.</para>
    /// </summary>
    [Fact]
    public void A_rate_revised_on_the_master_after_posting_cannot_move_an_issued_invoice()
    {
        var k = NewKit("Posted RateRevised Co");
        var v = PostSale(k, e => FillItemLine(e, k.TaxableItemId, k.MainGodownId, Qty, RateText));
        var c = k.Company;

        // What the books recorded — the debt the document is evidence of.
        Assert.Equal(PostedCgst, PostedHead(v, GstTaxHead.Central));
        Assert.Equal(PostedSgst, PostedHead(v, GstTaxHead.State));
        Assert.Equal(PostedPartyLeg, PartyLeg(v));

        var before = VoucherPrintProjector.ProjectInvoice(c, v);
        Assert.Equal(PostedPartyLeg, before.GrandTotal.Amount);   // the undrifted invoice already foots

        // The notification lands: this HSN moves 18% -> 28% on the live master.
        c.FindStockItem(k.TaxableItemId)!.Gst!.RateBasisPoints = 2800;

        var after = VoucherPrintProjector.ProjectInvoice(c, v);
        var row = Assert.Single(after.TaxRows);
        Assert.Equal("18%", row.RateLabel);                       // the rate this supply BORE, not today's
        Assert.Equal(SupplyValue, row.TaxableValue);
        Assert.Equal(PostedCgst, row.Cgst.Amount);
        Assert.Equal(PostedSgst, row.Sgst.Amount);
        Assert.Equal(Money.Zero, row.Igst);
        Assert.Equal(PostedCgst, after.TotalCgst.Amount);
        Assert.Equal(PostedSgst, after.TotalSgst.Amount);
        Assert.Equal(SupplyValue, after.TotalTaxable);
        Assert.Equal(Money.Zero, after.RoundOff);
        Assert.Equal(PostedPartyLeg, after.GrandTotal.Amount);
        Assert.Equal(PartyLeg(v), after.GrandTotal.Amount);       // THE INVARIANT

        // …and it reaches BOTH surfaces the fix can move: the bytes the customer holds, and the mirror the operator
        // approves on screen. They are built from this one DTO, so they cannot state different totals.
        var preview = PrintDrilled(k.Vm, v.Id);
        var text = System.Text.Encoding.Latin1.GetString(preview.PdfBytes);
        Assert.Contains("55,810.14", text);
        Assert.DoesNotContain("60,539.81", text);
        var cells = preview.Pages[0].Lines.SelectMany(r => r.Cells).ToList();
        Assert.Contains("55,810.14", cells);
        Assert.DoesNotContain("60,539.81", cells);
    }

    // ================================================================ 2 — a mixed taxable + exempt supply

    /// <summary>
    /// A mixed taxable + exempt invoice, where the EXEMPT item is later reclassified taxable — so the live
    /// recomputation would tax a line the voucher never taxed, while the exempt line's value stays in the goods total
    /// either way. The rate row must keep the base the tax leg declares (₹47,296.73), never the combined ₹47,566.52.
    /// <para><b>Bite:</b> recompute live and the reprint charges 18% on the milk too — a breakup row reading
    /// taxable ₹47,566.52 / CGST 4,280.99 / SGST 4,280.98 under a Grand Total of ₹56,128.49, against a posted party
    /// debit of ₹56,079.93.</para>
    /// </summary>
    [Fact]
    public void A_mixed_taxable_and_exempt_invoice_charges_only_the_tax_its_legs_recorded()
    {
        var k = NewKit("Posted MixedExempt Co");
        var v = PostSale(k, e =>
        {
            FillItemLine(e, k.TaxableItemId, k.MainGodownId, Qty, RateText, index: 0);
            FillItemLine(e, k.ExemptItemId, k.MainGodownId, 4.25m, "63.48", index: 1);   // = 269.79, odd paisa
        });
        var c = k.Company;
        var partyLeg = Money.FromRupees(56_079.93m);   // 47,296.73 + 269.79 + 4,256.71 + 4,256.70
        Assert.Equal(partyLeg.Amount, PartyLeg(v));
        Assert.Equal(PostedCgst, PostedHead(v, GstTaxHead.Central));

        // The exempt line's item is reclassified taxable on the master AFTER the invoice was issued.
        c.FindStockItem(k.ExemptItemId)!.Gst = new StockItemGstDetails
        {
            HsnSac = "040110", Taxability = GstTaxability.Taxable, RateBasisPoints = 1800,
        };

        var data = VoucherPrintProjector.ProjectInvoice(c, v);
        Assert.False(data.IsBillOfSupply);
        Assert.Equal("TAX INVOICE", data.DocumentTitle);
        // Every line's value is in the goods total — the exempt one is never dropped …
        Assert.Equal(new Money(SupplyValue.Amount + 269.79m), data.TotalTaxable);
        // … but only the leg that was taxed carries tax, on the base that leg declares.
        var row = Assert.Single(data.TaxRows);
        Assert.Equal("18%", row.RateLabel);
        Assert.Equal(SupplyValue, row.TaxableValue);
        Assert.Equal(PostedCgst, data.TotalCgst.Amount);
        Assert.Equal(PostedSgst, data.TotalSgst.Amount);
        Assert.Equal(partyLeg.Amount, data.GrandTotal.Amount);
        Assert.Equal(PartyLeg(v), data.GrandTotal.Amount);        // THE INVARIANT
    }

    // ================================================================ 3 — cess: posted, and only posted

    /// <summary>
    /// <b>The half of F4 the posted-cess preference did NOT cover.</b> F4 made the item path prefer the POSTED cess
    /// legs — but only when the voucher HAD posted one; a voucher that posted none still fell through to a LIVE
    /// <c>ResolveCess</c>. So adding a Compensation Cess to an item master conjured cess onto every already-issued
    /// invoice of that item, out of nothing in the general ledger.
    /// <para><b>Bite:</b> restore the live fallback (<c>hasPostedCess ? PostedCessTotal : invoiceTax.TotalCess</c>) and
    /// this reprint charges ₹5,675.61 of cess that is in no ledger, for a Grand Total of ₹61,485.75 against a posted
    /// party debit of ₹55,810.14.</para>
    /// </summary>
    [Fact]
    public void A_cess_added_to_the_master_after_posting_cannot_appear_on_the_reprint()
    {
        var k = NewKit("Posted LateCess Co");
        var v = PostSale(k, e => FillItemLine(e, k.TaxableItemId, k.MainGodownId, Qty, RateText));
        var c = k.Company;
        Assert.Equal(0m, PostedHead(v, GstTaxHead.Cess));         // no cess leg was ever posted
        Assert.Equal(PostedPartyLeg, PartyLeg(v));

        // A 12% ad-valorem Compensation Cess is declared on the item master AFTER the invoice was issued.
        var gstBlock = c.FindStockItem(k.TaxableItemId)!.Gst!;
        gstBlock.CessApplicable = true;
        gstBlock.CessValuationMode = CessValuationMode.AdValorem;
        gstBlock.CessRateBasisPoints = 1200;

        var data = VoucherPrintProjector.ProjectInvoice(c, v);
        Assert.Equal(Money.Zero, data.TotalCess);
        Assert.Equal(PostedPartyLeg, data.GrandTotal.Amount);
        Assert.Equal(PartyLeg(v), data.GrandTotal.Amount);        // THE INVARIANT
    }

    /// <summary>
    /// The other direction, and the slice's "where a cess IS posted" lock: a genuinely cess-bearing item invoice whose
    /// GST rate <i>and</i> cess rate are both revised afterwards still prints the ring-fenced cess and the GST its own
    /// legs carry, and still foots.
    /// <para><b>Bite:</b> take the GST heads from the live recomputation and the cess stays right while the GST goes to
    /// 28% — a Grand Total of ₹66,215.56 against a posted party debit of ₹61,485.75.</para>
    /// </summary>
    [Fact]
    public void A_cess_bearing_invoice_prints_the_posted_cess_and_the_posted_gst_after_both_are_re_rated()
    {
        var k = NewKit("Posted PostedCess Co");
        var item = k.Company.FindStockItem(k.TaxableItemId)!;
        item.Gst!.CessApplicable = true;
        item.Gst!.CessValuationMode = CessValuationMode.AdValorem;
        item.Gst!.CessRateBasisPoints = 1200;                     // 12% of 47,296.73 = 5,675.61

        var v = PostSale(k, e => FillItemLine(e, k.TaxableItemId, k.MainGodownId, Qty, RateText));
        var c = k.Company;
        var partyLeg = Money.FromRupees(61_485.75m);              // 47,296.73 + 4,256.71 + 4,256.70 + 5,675.61
        Assert.Equal(5_675.61m, PostedHead(v, GstTaxHead.Cess));
        Assert.Equal(partyLeg.Amount, PartyLeg(v));

        // Both rates are revised on the master after the invoice was issued.
        item.Gst!.RateBasisPoints = 2800;
        item.Gst!.CessRateBasisPoints = 6000;

        var data = VoucherPrintProjector.ProjectInvoice(c, v);
        Assert.Equal("18%", Assert.Single(data.TaxRows).RateLabel);
        Assert.Equal(PostedCgst, data.TotalCgst.Amount);
        Assert.Equal(PostedSgst, data.TotalSgst.Amount);
        Assert.Equal(5_675.61m, data.TotalCess.Amount);
        Assert.Equal(partyLeg.Amount, data.GrandTotal.Amount);
        Assert.Equal(PartyLeg(v), data.GrandTotal.Amount);        // THE INVARIANT
    }

    // ================================================================ 4 — the routing follows the posted heads

    /// <summary>
    /// <b>The routing is money too.</b> The item pass derived intra-vs-inter from the party's LIVE recorded State, so
    /// editing a customer's State after posting reprinted an already-issued intra-state sale as an INTER-state one: the
    /// CGST+SGST the ledger carries vanished into an IGST column, under a Place of Supply naming a State the posted tax
    /// contradicts. The service pass has read <c>PostedForwardRouting</c> since F1; the item pass now does too, and the
    /// printed buyer block is reconciled to it by the same FIX-3 rule (a blank GSTIN is a Rule-46 omission, a
    /// self-contradicting one is a Rule-46 falsehood).
    /// <para><b>Bite:</b> route from <c>gst.IsInterState(partyState)</c> and this prints IGST ₹8,513.41 with CGST/SGST
    /// zero, a Gujarat Place of Supply and <c>IsInterState = true</c>, on a voucher whose GL carries Central + State
    /// legs.</para>
    /// </summary>
    [Fact]
    public void A_party_state_edited_after_posting_cannot_flip_the_printed_routing()
    {
        var k = NewKit("Posted RoutingFlip Co");
        var v = PostSale(k, e => FillItemLine(e, k.TaxableItemId, k.MainGodownId, Qty, RateText));
        var c = k.Company;
        Assert.Equal(false, GstReportSupport.PostedForwardRouting(v));   // the GL says INTRA

        // The customer's master is corrected to Gujarat AFTER the invoice was issued.
        var customer = c.FindLedger(k.CustomerId)!;
        customer.PartyGst = new PartyGstDetails
        {
            RegistrationType = GstRegistrationType.Regular, Gstin = GstinGujarat, StateCode = "24",
        };

        var data = VoucherPrintProjector.ProjectInvoice(c, v);
        Assert.False(data.IsInterState);
        Assert.Equal(PostedCgst, data.TotalCgst.Amount);
        Assert.Equal(PostedSgst, data.TotalSgst.Amount);
        Assert.Equal(Money.Zero, data.TotalIgst);
        Assert.Equal("Maharashtra (27)", data.PlaceOfSupply);            // what CGST+SGST already asserts
        Assert.Equal("Maharashtra (27)", data.Buyer.StateText);
        Assert.Equal(string.Empty, data.Buyer.Gstin);                    // a 24… GSTIN under State 27 would be a lie
        Assert.Equal(PostedPartyLeg, data.GrandTotal.Amount);
        Assert.Equal(PartyLeg(v), data.GrandTotal.Amount);               // THE INVARIANT
    }

    // ================================================================ 5 — inter-state AND multi-rate, both at once

    /// <summary>
    /// <b>The two branches of the posted read that nothing else exercised on the ITEM path.</b> Found by review, not by
    /// the suite: every pre-existing item-invoice print assertion was single-rate and INTRA-state, so the IGST
    /// accumulation and the multi-group loop were rewritten by this slice with no coverage at all (the service path had
    /// both; the item path had neither). Here one inter-state invoice carries two rate groups — 18% and 5%, each on an
    /// odd-paisa base — and then BOTH drifts are applied at once: the 5% item is re-rated to 12% and the customer is
    /// moved back into the home State.
    /// <para><b>Bite:</b> recompute live and this prints a 12% row instead of 5%, under CGST+SGST heads with IGST zero
    /// (the live party State says intra) on a voucher whose GL carries two Integrated legs.</para>
    /// </summary>
    [Fact]
    public void An_inter_state_multi_rate_invoice_prints_both_posted_groups_as_igst()
    {
        var k = NewKit("Posted InterMultiRate Co");
        var c = k.Company;
        // The customer is in Gujarat at posting time ⇒ the sale posts IGST.
        c.FindLedger(k.CustomerId)!.PartyGst = new PartyGstDetails
        {
            RegistrationType = GstRegistrationType.Regular, Gstin = GstinGujarat, StateCode = "24",
        };

        var v = PostSale(k, e =>
        {
            FillItemLine(e, k.TaxableItemId, k.MainGodownId, Qty, RateText, index: 0);          // 47,296.73 @18%
            FillItemLine(e, k.LowRatedItemId, k.MainGodownId, 12.375m, "213.47", index: 1);     // 2,641.69 @5%
        });

        // What the books recorded: two Integrated groups, nothing on Central/State.
        Assert.True(GstReportSupport.PostedForwardRouting(v));
        Assert.Equal(0m, PostedHead(v, GstTaxHead.Central));
        Assert.Equal(0m, PostedHead(v, GstTaxHead.State));
        Assert.Equal(8_645.49m, PostedHead(v, GstTaxHead.Integrated));   // 8,513.41 @18% + 132.08 @5%
        Assert.Equal(58_583.91m, PartyLeg(v));                           // 47,296.73 + 2,641.69 + 8,645.49

        // Both drifts, after the invoice was issued: the 5% HSN is re-rated to 12% and the customer moves home.
        c.FindStockItem(k.LowRatedItemId)!.Gst!.RateBasisPoints = 1200;
        c.FindLedger(k.CustomerId)!.PartyGst = new PartyGstDetails
        {
            RegistrationType = GstRegistrationType.Regular, Gstin = GstinMaharashtra, StateCode = "27",
        };

        var data = VoucherPrintProjector.ProjectInvoice(c, v);
        Assert.True(data.IsInterState);
        Assert.Equal(Money.Zero, data.TotalCgst);
        Assert.Equal(Money.Zero, data.TotalSgst);
        Assert.Equal(8_645.49m, data.TotalIgst.Amount);

        // Two rows, ordered by rate, each on the base and at the rate its own legs declare.
        Assert.Equal(2, data.TaxRows.Count);
        Assert.Equal("5%", data.TaxRows[0].RateLabel);
        Assert.Equal(Money.FromRupees(2_641.69m), data.TaxRows[0].TaxableValue);
        Assert.Equal(Money.FromRupees(132.08m), data.TaxRows[0].Igst);
        Assert.Equal("18%", data.TaxRows[1].RateLabel);
        Assert.Equal(SupplyValue, data.TaxRows[1].TaxableValue);
        Assert.Equal(Money.FromRupees(8_513.41m), data.TaxRows[1].Igst);
        // W0-10 review, finding #11: this used to read `Assert.Equal(data.TotalIgst.Amount, data.TaxRows.Sum(…))`,
        // commented "the rows foot to the head". It was a TAUTOLOGY — `ReadPostedMoney` emits the rows and accumulates
        // the head in ONE loop over the same iteration variable, so no input and no mutation that preserves that loop
        // shape could make it fail. The rows are now footed against a SOURCE THE PROJECTOR'S LOOP NEVER TOUCHED: the
        // Integrated tax read straight off the voucher's own legs. Drop a rate group, halve a group's tax, or feed the
        // rows and the accumulator from a live recompute, and this fires.
        Assert.Equal(PostedHead(v, GstTaxHead.Integrated), data.TaxRows.Sum(r => r.Igst.Amount));

        Assert.Equal(Money.FromRupees(49_938.42m), data.TotalTaxable);   // 47,296.73 + 2,641.69, every line's value
        Assert.Equal(58_583.91m, data.GrandTotal.Amount);
        Assert.Equal(PartyLeg(v), data.GrandTotal.Amount);               // THE INVARIANT
    }

    // ================================================================ 6 — ONE source of truth, both paths

    /// <summary>
    /// <b>The slice's whole thesis, stated as one assertion.</b> The SAME supply — ₹47,296.73 at 18% intra to the same
    /// registered customer — billed once as an ITEM invoice and once as a SERVICE (Accounting) invoice, with BOTH
    /// masters re-rated to 28% after posting, must produce the SAME printed money. Before the fix the service document
    /// held its posted 18% while the item document jumped to 28%: one projector, two answers, for one supply.
    /// <para><b>Bite:</b> restore the live recomputation on the item pass and the item invoice prints ₹60,539.81 while
    /// the service invoice prints ₹55,810.14 — and only one of them is the debt either voucher recorded.</para>
    /// </summary>
    [Fact]
    public void The_item_and_service_paths_print_the_same_money_for_the_same_supply()
    {
        // --- the ITEM document ---
        var k = NewKit("Posted OneTruth Item Co");
        var itemVoucher = PostSale(k, e => FillItemLine(e, k.TaxableItemId, k.MainGodownId, Qty, RateText));
        k.Company.FindStockItem(k.TaxableItemId)!.Gst!.RateBasisPoints = 2800;
        var itemData = VoucherPrintProjector.ProjectInvoice(k.Company, itemVoucher);

        // --- the SERVICE document, for the same value at the same rate ---
        var s = NewServiceKit("Posted OneTruth Service Co");
        var serviceVoucher = PostServiceSale(s, s.ConsultancyId, "47296.73");
        s.Company.FindLedger(s.ConsultancyId)!.SalesPurchaseGst!.RateBasisPoints = 2800;
        var serviceData = VoucherPrintProjector.ProjectInvoice(s.Company, serviceVoucher);

        // Each states its own recorded debt …
        Assert.Equal(PostedPartyLeg, PartyLeg(itemVoucher));
        Assert.Equal(PostedPartyLeg, PartyLeg(serviceVoucher));
        Assert.Equal(PartyLeg(itemVoucher), itemData.GrandTotal.Amount);
        Assert.Equal(PartyLeg(serviceVoucher), serviceData.GrandTotal.Amount);

        // … and therefore they state the SAME money, head for head and row for row.
        Assert.Equal(serviceData.TotalTaxable, itemData.TotalTaxable);
        Assert.Equal(serviceData.TotalCgst, itemData.TotalCgst);
        Assert.Equal(serviceData.TotalSgst, itemData.TotalSgst);
        Assert.Equal(serviceData.TotalIgst, itemData.TotalIgst);
        Assert.Equal(serviceData.TotalCess, itemData.TotalCess);
        Assert.Equal(serviceData.RoundOff, itemData.RoundOff);
        Assert.Equal(serviceData.IsInterState, itemData.IsInterState);
        Assert.Equal(serviceData.GrandTotal, itemData.GrandTotal);

        var itemRow = Assert.Single(itemData.TaxRows);
        var serviceRow = Assert.Single(serviceData.TaxRows);
        Assert.Equal(serviceRow.RateLabel, itemRow.RateLabel);
        Assert.Equal(serviceRow.TaxableValue, itemRow.TaxableValue);
        Assert.Equal(serviceRow.Cgst, itemRow.Cgst);
        Assert.Equal(serviceRow.Sgst, itemRow.Sgst);
        Assert.Equal(serviceRow.Igst, itemRow.Igst);
    }

    // ================================================================ 7 — W0-10 REVIEW: the ODD basis-point rate

    /// <summary>
    /// <b>W0-10 review findings #1/#6/#8 — the printed RATE must describe the same supply as the printed MONEY.</b>
    /// The item pass now labels its breakup row from the POSTED leg, and an intra-state leg carries the <b>half</b>
    /// rate: <c>GstService.ComputeInvoiceTax</c> stamps <c>halfBp = integratedBp / 2</c> with INTEGER division, so an
    /// ODD integrated rate loses a basis point and the naive recovery (double the half) reads 25 bp back as 24.
    /// Measured before the fix: this invoice printed <c>"0.24%"</c> beside CGST 59.12 + SGST 59.12 — a rate that yields
    /// ₹113.51 on this base, not the ₹118.24 printed next to it, so the row contradicted itself on the face of a CGST
    /// Rule 46(m) particular. The pre-W0-10 code printed <c>res.RateBasisPoints</c> (the full integrated rate) and was
    /// right; the switch to the posted legs made the document AGREE with an engine-wide loss instead of curing it.
    /// <para>0.25% is a real, surviving GST rate (rough diamonds, HSN 7102) that this app itself seeds a history row
    /// for, and the route here is the shipped one: GST Rate Setup → an HSN-dated 0.25% window.</para>
    /// <para><b>Bite:</b> revert <c>GstReportSupport.IntegratedRateOf</c> to <c>RateBasisPoints * 2</c> and the label
    /// reads "0.24%" while the money stays 59.12 / 59.12.</para>
    /// </summary>
    [Fact]
    public void An_odd_basis_point_rate_prints_the_rate_the_supply_actually_bore()
    {
        var k = NewKit("Posted OddRate Co");
        var v = PostSale(k, e => FillItemLine(e, k.RoughDiamondId, k.MainGodownId, Qty, RateText));
        var c = k.Company;

        // What the books recorded: 0.25% of 47,296.73 = 118.24, split 59.12 / 59.12 …
        Assert.Equal(59.12m, PostedHead(v, GstTaxHead.Central));
        Assert.Equal(59.12m, PostedHead(v, GstTaxHead.State));
        Assert.Equal(47_414.97m, PartyLeg(v));
        // … on legs whose own metadata says 12 bp, because 25 / 2 == 12 in integer arithmetic. This is the loss.
        Assert.All(
            v.Lines.Where(l => l.Gst is not null),
            l => Assert.Equal(12, l.Gst!.RateBasisPoints));

        var data = VoucherPrintProjector.ProjectInvoice(c, v);
        var row = Assert.Single(data.TaxRows);
        Assert.Equal("0.25%", row.RateLabel);                  // the rate charged — NOT the doubled half
        Assert.Equal(SupplyValue, row.TaxableValue);
        Assert.Equal(59.12m, row.Cgst.Amount);
        Assert.Equal(59.12m, row.Sgst.Amount);
        // The row and its money describe ONE supply: half of round(base x 25/10000) is exactly what is printed.
        Assert.Equal(row.Cgst.Amount + row.Sgst.Amount, new Money(SupplyValue.Amount * 25m / 10000m).RoundToPaisa().Amount);
        Assert.Equal(47_414.97m, data.GrandTotal.Amount);
        Assert.Equal(PartyLeg(v), data.GrandTotal.Amount);     // THE INVARIANT
    }

    /// <summary>
    /// <b>The secondary half of the same defect (finding #8): two rate groups one basis point apart COLLAPSED into
    /// one row.</b> 25 bp and 24 bp both stamp <c>12</c> on their intra heads, so keying the breakup on the doubled
    /// half merged them: one row labelled "0.24%" whose taxable was the MAX of the two bases (₹47,296.73, silently
    /// dropping ₹1,579.47 of supply from the breakup) carrying the SUM of both groups' tax. The money totals were
    /// unaffected — the accumulator sums every group — so a green suite saw nothing.
    /// <para><b>Bite:</b> revert <c>IntegratedRateOf</c> to the doubled half; <c>data.TaxRows</c> collapses to one row
    /// and <c>Assert.Equal(2, …)</c> fails.</para>
    /// </summary>
    [Fact]
    public void Two_rate_groups_one_basis_point_apart_do_not_collapse_into_one_row()
    {
        var k = NewKit("Posted OddRatePair Co");
        var v = PostSale(k, e =>
        {
            FillItemLine(e, k.RoughDiamondId, k.MainGodownId, Qty, RateText, index: 0);   // 47,296.73 @ 0.25%
            FillItemLine(e, k.CutDiamondId, k.MainGodownId, 3.4m, "464.55", index: 1);    //  1,579.47 @ 0.24%
        });
        var c = k.Company;

        Assert.Equal(61.02m, PostedHead(v, GstTaxHead.Central));   // 59.12 @0.25% + 1.90 @0.24%
        Assert.Equal(61.01m, PostedHead(v, GstTaxHead.State));     // 59.12 @0.25% + 1.89 @0.24%
        Assert.Equal(48_998.23m, PartyLeg(v));                     // 47,296.73 + 1,579.47 + 118.24 + 3.79

        var data = VoucherPrintProjector.ProjectInvoice(c, v);
        Assert.Equal(2, data.TaxRows.Count);
        Assert.Equal("0.24%", data.TaxRows[0].RateLabel);
        Assert.Equal(Money.FromRupees(1_579.47m), data.TaxRows[0].TaxableValue);
        Assert.Equal(1.90m, data.TaxRows[0].Cgst.Amount);
        Assert.Equal(1.89m, data.TaxRows[0].Sgst.Amount);
        Assert.Equal("0.25%", data.TaxRows[1].RateLabel);
        Assert.Equal(SupplyValue, data.TaxRows[1].TaxableValue);
        Assert.Equal(59.12m, data.TaxRows[1].Cgst.Amount);
        Assert.Equal(59.12m, data.TaxRows[1].Sgst.Amount);

        // Footed against a source the projector's own loop never touched.
        Assert.Equal(PostedHead(v, GstTaxHead.Central), data.TaxRows.Sum(r => r.Cgst.Amount));
        Assert.Equal(PostedHead(v, GstTaxHead.State), data.TaxRows.Sum(r => r.Sgst.Amount));
        Assert.Equal(Money.FromRupees(48_876.20m), data.TotalTaxable);
        Assert.Equal(PartyLeg(v), data.GrandTotal.Amount);         // THE INVARIANT
    }

    // ================================================================ 8 — W0-10 REVIEW: the untagged Output GST legs

    /// <summary>
    /// <b>W0-10 review finding #5 — the item pass reads 100% of its tax from <c>EntryLine.Gst</c> metadata, so a
    /// voucher whose Output GST legs carry NONE printed a Grand Total short of the posted party leg by the whole
    /// tax.</b> Before W0-10 the live <c>ComputeInvoiceTax</c> reconstructed that tax from the item masters and the
    /// document happened to foot; the switch reversed the direction of failure for this shape, and plan.md's
    /// carry-forward (b) — which defers the FULL footing guard behind the TCS row — did not record that.
    /// <para><b>Reachable, not theoretical:</b> <c>CanonicalXml</c> makes <c>&lt;gst&gt;</c> OPTIONAL on an entryLine
    /// and <c>ImportPlan.BuildGstLineTax</c> returns null when it is absent, so this voucher imports cleanly; the
    /// shipped Sales As-Voucher screen likewise builds every leg with no <c>gst:</c> argument. Measured before the fix:
    /// <c>IsTaxInvoice</c> said true and the projection printed ₹47,296.73 against a posted party debit of ₹55,810.14 —
    /// the exact ₹8,513.41 understatement class W0-10 exists to prevent, reached from the other side.</para>
    /// <para>The cure is deliberately NARROWER than the deferred footing guard, and that is what makes it safe to land
    /// now: it asks only "is every rupee this voucher posted to one of the company's own ordinary Output GST ledgers
    /// visible to the projector as a tagged leg?". TCS Payable is not a GST ledger, so a §206C(1H) invoice cannot trip
    /// it — see <see cref="A_tcs_bearing_invoice_still_prints_as_a_tax_invoice_and_pins_the_known_shortfall"/>.</para>
    /// <para><b>Bite:</b> drop the <c>PostedOutputTaxIsFullyTagged</c> conjunct from
    /// <c>GstReportSupport.IsTaxInvoice</c> and this voucher prints a TAX INVOICE demanding ₹47,296.73.</para>
    /// </summary>
    [Fact]
    public void An_item_invoice_whose_output_tax_legs_carry_no_metadata_prints_as_the_plain_voucher()
    {
        var k = NewKit("Posted UntaggedLegs Co");
        var c = k.Company;
        var gst = new GstService(c);
        var salesType = c.VoucherTypes.Single(t => t.BaseType == VoucherBaseType.Sales && t.IsActive);
        var salesLedger = c.FindLedgerByName("Sales")!;

        var v = new Voucher(Guid.NewGuid(), salesType.Id, FyStart.AddDays(20), new[]
        {
            new EntryLine(k.CustomerId, Money.FromRupees(55_810.14m), DrCr.Debit),
            new EntryLine(salesLedger.Id, SupplyValue, DrCr.Credit),
            // Hand-typed Output CGST/SGST: real money in the general ledger, NO GstLineTax metadata at all.
            new EntryLine(gst.FindTaxLedger(GstTaxHead.Central, GstTaxDirection.Output)!.Id,
                Money.FromRupees(PostedCgst), DrCr.Credit),
            new EntryLine(gst.FindTaxLedger(GstTaxHead.State, GstTaxDirection.Output)!.Id,
                Money.FromRupees(PostedSgst), DrCr.Credit),
        }, partyId: k.CustomerId, narration: "untagged output tax",
           inventoryLines: new[]
           {
               new VoucherInventoryLine(
                   k.TaxableItemId, k.MainGodownId, Qty, Money.FromRupees(786.64m), StockDirection.Outward),
           });
        new LedgerService(c).Post(v);

        // The general ledger DOES carry the tax — it is only the metadata that is missing.
        Assert.Equal(PostedPartyLeg, PartyLeg(v));
        Assert.All(v.Lines, l => Assert.Null(l.Gst));
        Assert.True(GstReportSupport.PostsToAnOrdinaryOutputTaxLedger(c, v));
        Assert.Equal(0m, GstReportSupport.PostedForwardTaxTotal(v).Amount);   // …and the projector can see none of it

        // So no statutory document may be issued: it prints as the plain Dr/Cr voucher, which states every posted leg.
        Assert.False(VoucherPrintProjector.IsTaxInvoice(c, v));
        Assert.False(VoucherPrintProjector.IsBillOfSupply(c, v));
        Assert.Equal(PrintPreviewViewModel.PrintKind.Voucher,
            new VoucherDetailViewModel(c, v).BuildPrintPreview().Kind);
    }

    // ================================================================ 9 — W0-10 REVIEW: TCS is the ONE documented gap

    /// <summary>
    /// <b>W0-10 review findings #3/#10 — the class doc asserted "the printed Grand Total is the debt the general
    /// ledger recorded — always, and by construction", while plan.md's own carry-forward (a) for this slice says the
    /// invariant "holds for every <i>non-TCS</i> sale, and the class doc must not claim more".</b> The doc now carries
    /// the caveat; this test is the assertion that marks the boundary, so the day the TCS row lands on
    /// <c>InvoicePrintData</c> it fails BY DESIGN and must be restated — exactly the discipline W0-10 applied to the
    /// pinned ₹8,513.41.
    /// <para>§206C(1H)/§206C(1) TCS is collected ON TOP of the GST-inclusive total and rides the party debit
    /// (<c>VoucherEntryViewModel.AcceptItemInvoice</c>: <c>partyAmount = partyAmount + tcs.TotalTcs</c>), while
    /// <c>InvoicePrintData</c> has no TCS member at all. This is NOT something W0-10 caused or could reach — TCS is not
    /// GST tax and the posted-legs switch cannot see it.</para>
    /// <para>It also pins the SAFETY property of the new untagged-legs guard: a TCS invoice's extra credit leg goes to
    /// TCS Payable, which is not a GST ledger, so the guard cannot demote it — the regression plan.md warns against.</para>
    /// </summary>
    [Fact]
    public void A_tcs_bearing_invoice_still_prints_as_a_tax_invoice_and_pins_the_known_shortfall()
    {
        var (vm, scrapId, godownId, buyerId) = NewTcsKit("Posted Tcs Co");
        vm.OpenVoucher(VoucherBaseType.Sales);
        var entry = vm.VoucherEntry!;
        entry.ToggleItemInvoice();
        entry.SelectedParty = entry.Parties.Single(p => p.Ledger?.Id == buyerId);
        FillItemLine(entry, scrapId, godownId, Qty, RateText);
        Assert.True(entry.Accept());

        var c = vm.Company!;
        var v = LastSale(c);
        // 47,296.73 @18% intra ⇒ CGST 4,256.71 + SGST 4,256.70; TCS 6CE 1% of the GST-INCLUSIVE 55,810.14 = 558.1014,
        // rounded to the rupee (§288B) ⇒ ₹558. The GST base is odd-paisa, which is what makes the shortfall visible.
        Assert.Equal(PostedCgst, PostedHead(v, GstTaxHead.Central));
        Assert.Equal(PostedSgst, PostedHead(v, GstTaxHead.State));
        var postedTcs = v.Lines.Single(l => l.HasTcs && l.Tcs!.TcsAmount.Amount > 0m).Tcs!.TcsAmount.Amount;
        Assert.Equal(558m, postedTcs);
        Assert.Equal(56_368.14m, PartyLeg(v));                     // 55,810.14 + 558

        // The guard added for finding #5 must NOT fire here: TCS Payable is not one of the company's GST ledgers.
        Assert.True(VoucherPrintProjector.IsTaxInvoice(c, v));

        var data = VoucherPrintProjector.ProjectInvoice(c, v);
        Assert.Equal(SupplyValue, data.TotalTaxable);
        Assert.Equal(PostedCgst, data.TotalCgst.Amount);
        Assert.Equal(PostedSgst, data.TotalSgst.Amount);
        // 🔴 THE PIN: the printed demand is short by the whole collected TCS. Restate this the day the DTO field lands.
        Assert.Equal(PostedPartyLeg, data.GrandTotal.Amount);
        Assert.Equal(postedTcs, PartyLeg(v) - data.GrandTotal.Amount);
    }

    // ================================================================ 10 — W0-10 REVIEW: taxable AT 0%, both passes

    /// <summary>
    /// <b>W0-10 review findings #2/#4/#9 — a TAXABLE-at-0% supply posts no tax leg, so it prints no rate row. That is
    /// now true of BOTH passes, and the review's proposed cure is deliberately NOT taken.</b>
    /// <para>The facts the review states are correct: <c>GstService.AddHead</c> early-returns on a zero amount, so a
    /// 0%-rated group leaves no posted footprint; the item pass therefore stopped emitting the <c>"0% | value | 0.00 |
    /// 0.00"</c> row the pre-W0-10 live resolve produced, and the class comment's "an ordinary reprint is
    /// byte-identical" sentence was false for exactly this shape (it now names the exception).</para>
    /// <para><b>But restoring the row on the ITEM pass alone would re-open the defect W0-10 closed.</b> The SERVICE
    /// pass has never emitted it, and that is a settled, shipped, separately-pinned decision:
    /// <c>ServiceAccountingInvoicePrintTests.ZeroRatedServiceInvoice_printsAsTaxInvoice</c> asserts
    /// <c>Assert.Empty(data.TaxRows)</c> on a 0% LUT/export invoice with the comment "no rate row — there is no tax",
    /// and <c>GstReportSupport.RateBreakupReconciles</c> is built on the same premise ("an exempt leg and a zero-rated
    /// leg each carry value into the invoice taxable total while posting no tax line"). The only source for "this line
    /// was rated 0%" is the LIVE item master — the very read this slice removed. So the two passes now AGREE, and this
    /// test is what holds them together.</para>
    /// <para>Whether a 0%-rated supply should state a rate row at all (CGST Rule 46(m)) is a statutory question for
    /// BOTH passes at once, and answering it needs a rate snapshotted onto the posted line — a schema change. Recorded
    /// as a plan.md carry-forward, not decided here.</para>
    /// </summary>
    [Fact]
    public void A_taxable_at_zero_percent_supply_prints_no_rate_row_on_the_item_and_service_passes_alike()
    {
        // --- the ITEM document ---
        var k = NewKit("Posted ZeroRated Item Co");
        var itemVoucher = PostSale(k, e => FillItemLine(e, k.ZeroRatedItemId, k.MainGodownId, Qty, RateText));
        Assert.All(itemVoucher.Lines, l => Assert.Null(l.Gst));          // not one posted tax leg …
        Assert.Equal(SupplyValue.Amount, PartyLeg(itemVoucher));
        var itemData = VoucherPrintProjector.ProjectInvoice(k.Company, itemVoucher);

        // --- the SERVICE document, same value, same 0% ---
        var s = NewServiceKit("Posted ZeroRated Service Co");
        var serviceVoucher = PostServiceSale(s, s.ZeroRatedId, "47296.73");
        Assert.All(serviceVoucher.Lines, l => Assert.Null(l.Gst));
        var serviceData = VoucherPrintProjector.ProjectInvoice(s.Company, serviceVoucher);

        // … and both are still Rule-46 TAX INVOICES stating their own recorded debt.
        Assert.False(itemData.IsBillOfSupply);
        Assert.Equal("TAX INVOICE", itemData.DocumentTitle);
        Assert.Equal(SupplyValue, itemData.TotalTaxable);
        Assert.Equal(Money.Zero, itemData.TotalCgst);
        Assert.Equal(Money.Zero, itemData.TotalSgst);
        Assert.Equal(Money.Zero, itemData.TotalIgst);
        Assert.Equal(PartyLeg(itemVoucher), itemData.GrandTotal.Amount);   // THE INVARIANT

        // THE CONVERGENCE: one projector, one answer to "does a 0%-rated supply carry a rate row?".
        Assert.Empty(itemData.TaxRows);
        Assert.Empty(serviceData.TaxRows);
        Assert.Equal(serviceData.TaxRows.Count, itemData.TaxRows.Count);
        Assert.Equal(serviceData.TotalTaxable, itemData.TotalTaxable);
        Assert.Equal(serviceData.GrandTotal, itemData.GrandTotal);
    }

    // ---------------------------------------------------------------- scaffolding

    private sealed class Kit
    {
        public required MainWindowViewModel Vm { get; init; }
        public required Guid TaxableItemId { get; init; }   // Widget, HSN 847130 @18%
        public required Guid LowRatedItemId { get; init; }  // Copper Wire, HSN 854411 @5% — the second rate group
        public required Guid ExemptItemId { get; init; }    // Fresh Milk, HSN 040110, exempt
        public required Guid RoughDiamondId { get; init; }  // HSN 710231, ODD rate 0.25% (25 bp) via rate history
        public required Guid CutDiamondId { get; init; }    // HSN 710239, EVEN rate 0.24% (24 bp) — the 1-bp neighbour
        public required Guid ZeroRatedItemId { get; init; } // HSN 1006, TAXABLE at 0% — posts no tax leg at all
        public required Guid MainGodownId { get; init; }
        public required Guid CustomerId { get; init; }      // in-state (27), registered
        public Company Company => Vm.Company!;
    }

    private Kit NewKit(string companyName)
    {
        var vm = NewGstCompany(companyName);
        var c = vm.Company!;

        var inv = new InventoryService(c);
        var grp = inv.CreateStockGroup("Goods");
        var nos = inv.CreateSimpleUnit("Nos", "Numbers", unitQuantityCode: "NOS");
        var main = c.MainLocation!.Id;

        var taxable = inv.CreateStockItem("Widget", grp.Id, nos.Id);
        taxable.Gst = new StockItemGstDetails
        { HsnSac = "847130", Taxability = GstTaxability.Taxable, RateBasisPoints = 1800 };
        var lowRated = inv.CreateStockItem("Copper Wire", grp.Id, nos.Id);
        lowRated.Gst = new StockItemGstDetails
        { HsnSac = "854411", Taxability = GstTaxability.Taxable, RateBasisPoints = 500 };
        var exempt = inv.CreateStockItem("Fresh Milk", grp.Id, nos.Id);
        exempt.Gst = new StockItemGstDetails { HsnSac = "040110", Taxability = GstTaxability.Exempt };

        // W0-10 review — the ODD basis-point pair. Both rates are entered exactly as the shipped GST Rate Setup screen
        // enters them (an HSN-dated rate-history window); the items' own slab rate is overridden by the dated window,
        // which is what `GstService.ResolveRate` does on the accept path. 25 bp is a REAL surviving GST rate — the app
        // itself seeds "0.25% (rough diamonds)" in `SeedGstRates.BuildDefaultRateHistory`. 24 bp is its 1-bp neighbour:
        // `ComputeInvoiceTax` stamps `integratedBp / 2` on the intra heads, so BOTH stamp 12 and the two groups are
        // indistinguishable from the stamped metadata alone.
        var roughDiamond = inv.CreateStockItem("Rough Diamond", grp.Id, nos.Id);
        roughDiamond.Gst = new StockItemGstDetails
        { HsnSac = "710231", Taxability = GstTaxability.Taxable, RateBasisPoints = 1800 };
        var cutDiamond = inv.CreateStockItem("Cut Diamond", grp.Id, nos.Id);
        cutDiamond.Gst = new StockItemGstDetails
        { HsnSac = "710239", Taxability = GstTaxability.Taxable, RateBasisPoints = 1800 };
        c.Gst!.AddRateHistory(new GstRateHistoryEntry(
            Guid.NewGuid(), "710231", 25, GstRateClass.Special, FyStart, null,
            GstValuationBasis.TransactionValue, "0.25% (rough diamonds)"));
        c.Gst!.AddRateHistory(new GstRateHistoryEntry(
            Guid.NewGuid(), "710239", 24, GstRateClass.Special, FyStart, null,
            GstValuationBasis.TransactionValue, "0.24% (test neighbour)"));

        // TAXABLE at 0% — not exempt. `GstService.AddHead` early-returns on a zero amount, so this posts NO tax leg.
        var zeroRated = inv.CreateStockItem("Zero Rated Grain", grp.Id, nos.Id);
        zeroRated.Gst = new StockItemGstDetails
        { HsnSac = "1006", Taxability = GstTaxability.Taxable, RateBasisPoints = 0 };

        inv.AddOpeningBalance(taxable.Id, main, 500m, Money.FromRupees(311.17m));
        inv.AddOpeningBalance(lowRated.Id, main, 500m, Money.FromRupees(88.91m));
        inv.AddOpeningBalance(exempt.Id, main, 500m, Money.FromRupees(29.43m));
        inv.AddOpeningBalance(roughDiamond.Id, main, 500m, Money.FromRupees(507.83m));
        inv.AddOpeningBalance(cutDiamond.Id, main, 500m, Money.FromRupees(233.09m));
        inv.AddOpeningBalance(zeroRated.Id, main, 500m, Money.FromRupees(41.77m));

        AddLedger(c, "Sales", "Sales Accounts");
        var customer = AddLedger(c, "Local Customer", "Sundry Debtors");
        customer.PartyGst = new PartyGstDetails
        { RegistrationType = GstRegistrationType.Regular, Gstin = GstinMaharashtra, StateCode = "27" };
        _storage.Save(c);

        return new Kit
        {
            Vm = vm,
            TaxableItemId = taxable.Id,
            LowRatedItemId = lowRated.Id,
            ExemptItemId = exempt.Id,
            RoughDiamondId = roughDiamond.Id,
            CutDiamondId = cutDiamond.Id,
            ZeroRatedItemId = zeroRated.Id,
            MainGodownId = main,
            CustomerId = customer.Id,
        };
    }

    private sealed class ServiceKit
    {
        public required MainWindowViewModel Vm { get; init; }
        public required Guid ConsultancyId { get; init; }   // Income, taxable @18% (SAC 998311)
        public required Guid ZeroRatedId { get; init; }     // Income, taxable @0% (LUT/export) — no tax leg at all
        public required Guid CustomerId { get; init; }
        public Company Company => Vm.Company!;
    }

    private ServiceKit NewServiceKit(string companyName)
    {
        var vm = NewGstCompany(companyName);
        var c = vm.Company!;

        var consultancy = AddLedger(c, "Consultancy Income", "Sales Accounts");
        consultancy.SalesPurchaseGst = new StockItemGstDetails
        {
            HsnSac = "998311", Taxability = GstTaxability.Taxable, RateBasisPoints = 1800,
            SupplyType = GstSupplyType.Services,
        };
        var zeroRated = AddLedger(c, "Export Service (LUT)", "Sales Accounts");
        zeroRated.SalesPurchaseGst = new StockItemGstDetails
        {
            HsnSac = "998313", Taxability = GstTaxability.Taxable, RateBasisPoints = 0,
            SupplyType = GstSupplyType.Services,
        };
        var customer = AddLedger(c, "Local Customer", "Sundry Debtors");
        customer.PartyGst = new PartyGstDetails
        { RegistrationType = GstRegistrationType.Regular, Gstin = GstinMaharashtra, StateCode = "27" };
        _storage.Save(c);

        return new ServiceKit
        { Vm = vm, ConsultancyId = consultancy.Id, ZeroRatedId = zeroRated.Id, CustomerId = customer.Id };
    }

    /// <summary>A GST- <b>and</b> TCS-enabled company with one §206C(1) scrap item (nature 6CE) at 18% and one
    /// collectee buyer in the home State — the smallest fixture that posts a party leg carrying collected TCS. FY
    /// 2025-26 so the shipped 6CE nature is in force.</summary>
    private (MainWindowViewModel Vm, Guid ScrapId, Guid GodownId, Guid BuyerId) NewTcsKit(string companyName)
    {
        const string ValidTan = "MUMA12345B";
        const string BuyerPan = "AAQCS1234K";
        var fy = new DateOnly(2025, 4, 1);

        var vm = new MainWindowViewModel(_storage);
        vm.NewCompanyName = companyName;
        vm.CreateCompany();
        var c = vm.Company!;
        c.MailingName = "Acme Traders Pvt Ltd";
        c.FinancialYearStart = fy;
        c.BooksBeginFrom = fy;
        new GstService(c).EnableGst(new GstConfig
        {
            HomeStateCode = "27", Gstin = GstinMaharashtra, RegistrationType = GstRegistrationType.Regular,
            ApplicableFrom = fy, Periodicity = GstReturnPeriodicity.Monthly,
        });
        new TdsTcsService(c).EnableTcs(new TcsConfig { Tan = ValidTan });

        var inv = new InventoryService(c);
        var grp = inv.CreateStockGroup("Goods");
        var kg = inv.CreateSimpleUnit("Kg", "Kilogram", unitQuantityCode: "KGS");
        var main = c.MainLocation!.Id;
        var scrap = inv.CreateStockItem("Scrap Metal", grp.Id, kg.Id);
        scrap.Gst = new StockItemGstDetails
        { HsnSac = "720449", Taxability = GstTaxability.Taxable, RateBasisPoints = 1800 };
        scrap.TcsNatureOfGoodsId = c.FindNatureOfGoodsByCode("6CE")!.Id;
        inv.AddOpeningBalance(scrap.Id, main, 500m, Money.FromRupees(311.17m));

        AddLedger(c, "Sales", "Sales Accounts");
        var buyer = AddLedger(c, "Industrial Buyer", "Sundry Debtors");
        buyer.PartyGst = new PartyGstDetails
        { RegistrationType = GstRegistrationType.Regular, Gstin = GstinMaharashtra, StateCode = "27" };
        buyer.TcsApplicable = true;
        buyer.CollecteeType = CollecteeType.Individual;
        buyer.PartyPan = BuyerPan;
        _storage.Save(c);

        return (vm, scrap.Id, main, buyer.Id);
    }

    private MainWindowViewModel NewGstCompany(string companyName)
    {
        var vm = new MainWindowViewModel(_storage);
        vm.NewCompanyName = companyName;
        vm.CreateCompany();
        var c = vm.Company!;
        c.MailingName = "Acme Traders Pvt Ltd";
        c.Address = "12 Industrial Estate\nPune, Maharashtra 411001";
        c.FinancialYearStart = FyStart;
        c.BooksBeginFrom = FyStart;
        new GstService(c).EnableGst(new GstConfig
        {
            HomeStateCode = "27", Gstin = GstinMaharashtra, RegistrationType = GstRegistrationType.Regular,
            ApplicableFrom = FyStart, Periodicity = GstReturnPeriodicity.Monthly,
        });
        return vm;
    }

    private static DomainLedger AddLedger(Company c, string name, string groupName)
    {
        var group = c.FindGroupByName(groupName) ?? throw new InvalidOperationException($"No group '{groupName}'.");
        var ledger = new DomainLedger(Guid.NewGuid(), name, group.Id, Money.Zero, openingIsDebit: false);
        c.AddLedger(ledger);
        return ledger;
    }

    private static void FillItemLine(
        VoucherEntryViewModel entry, Guid itemId, Guid godownId, decimal qty, string rate, int index = 0)
    {
        while (entry.InventoryLines.Count <= index) entry.AddInventoryLine();
        var line = entry.InventoryLines[index];
        line.SelectedItem = entry.StockItems.Single(i => i.Id == itemId);
        line.SelectedGodown = entry.Godowns.Single(g => g.Id == godownId);
        line.QuantityText = qty.ToString(System.Globalization.CultureInfo.InvariantCulture);
        line.RateText = rate;
    }

    /// <summary>Posts a Sales ITEM invoice through the real entry VM and returns the posted voucher.</summary>
    private static Voucher PostSale(Kit k, Action<VoucherEntryViewModel> fill)
    {
        k.Vm.OpenVoucher(VoucherBaseType.Sales);
        var entry = k.Vm.VoucherEntry!;
        entry.ToggleItemInvoice();
        entry.SelectedParty = entry.Parties.Single(p => p.Ledger?.Id == k.CustomerId);
        fill(entry);
        Assert.True(entry.Accept());
        return LastSale(k.Company);
    }

    /// <summary>Posts a ledger-only SERVICE sale through the real Accounting Invoice entry mode.</summary>
    private static Voucher PostServiceSale(ServiceKit k, Guid serviceLedgerId, string amount)
    {
        k.Vm.OpenVoucher(VoucherBaseType.Sales);
        var entry = k.Vm.VoucherEntry!;
        entry.ChangeMode();   // As Voucher   -> Item Invoice
        entry.ChangeMode();   // Item Invoice -> Accounting Invoice
        Assert.True(entry.IsAccountingInvoice);
        entry.SelectedParty = entry.Parties.Single(p => p.Ledger?.Id == k.CustomerId);
        while (entry.AccountingInvoiceLines.Count == 0) entry.AddAccountingInvoiceLine();
        var line = entry.AccountingInvoiceLines[0];
        line.SelectedLedger = entry.AccountingInvoiceLedgers.Single(l => l.Id == serviceLedgerId);
        line.AmountText = amount;
        Assert.True(entry.Accept());
        return LastSale(k.Company);
    }

    private static Voucher LastSale(Company c)
    {
        var type = c.VoucherTypes.Single(t => t.BaseType == VoucherBaseType.Sales && t.IsActive);
        return c.Vouchers.Last(v => v.TypeId == type.Id);
    }

    private PrintPreviewViewModel PrintDrilled(MainWindowViewModel vm, Guid voucherId)
    {
        vm.OpenVoucherDetail(voucherId);
        vm.OpenPrintPreview();
        Assert.Equal(Screen.PrintPreview, vm.CurrentScreen);
        return vm.PrintPreview!;
    }

    private static decimal PostedHead(Voucher v, GstTaxHead head)
    {
        var total = 0m;
        foreach (var l in v.Lines)
            if (l.Gst is { IsReverseCharge: false } g && g.TaxHead == head) total += l.Amount.Amount;
        return total;
    }

    private static decimal PartyLeg(Voucher v) =>
        v.Lines.Single(l => l.LedgerId == v.PartyId!.Value).Amount.Amount;

    public void Dispose()
    {
        try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true); }
        catch { /* best-effort temp cleanup */ }
    }
}
