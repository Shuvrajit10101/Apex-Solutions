using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Io;
using Apex.Ledger.Services;
using Apex.Desktop.Services;
using Apex.Desktop.ViewModels;
using DomainLedger = Apex.Ledger.Domain.Ledger;

namespace Apex.Desktop.Tests;

/// <summary>
/// The adversarial-review findings on the "service invoices print as tax invoices" slice (F1–F5). Every test here
/// drives a REAL posted (or really imported) voucher through the REAL projector and asserts on the money and on the
/// Rule-46 descriptive fields the document states about itself — never "a field exists".
///
/// <list type="bullet">
/// <item><b>F1 (blocker, a statutory falsehood)</b> — a zero-rated (LUT/export) or wholly-exempt service invoice to an
/// OUT-OF-STATE party posts no forward tax leg, so the intra/inter routing read off the GL said "intra"; the buyer
/// block was then rewritten to the SELLER's own State, the real GSTIN was blanked and the Place of Supply named the
/// seller's State. Three false statements on an issued document.</item>
/// <item><b>F2 (money)</b> — a crafted import could set the accounting-invoice flag on a hand-keyed voucher, which then
/// printed a tax invoice understating the posted debt by the whole tax.</item>
/// <item><b>F3/F4 (crash + contract)</b> — the item path re-resolved Compensation Cess LIVE at print time: a reprint
/// could throw, and a post-posting master edit moved a printed figure off the GL.</item>
/// <item><b>F5 (test adequacy)</b> — three production mutations that survived the whole suite.</item>
/// </list>
/// </summary>
public sealed class ServiceAccountingInvoicePrintFixTests : IDisposable
{
    private const string GstinMaharashtra = "27AAPFU0939F1ZV";
    private const string GstinGujarat = "24AAACC1206D1ZM";
    private static readonly DateOnly FyStart = new(2024, 4, 1);

    private readonly string _tempDir;
    private readonly CompanyStorage _storage;

    public ServiceAccountingInvoicePrintFixTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "ApexServiceInvoiceFixTests_" + Guid.NewGuid().ToString("N"));
        _storage = new CompanyStorage(_tempDir);
    }

    // ================================================================ F1: no posted tax ⇒ the party's own State

    /// <summary>
    /// <b>F1 (BLOCKER) — a ZERO-RATED (LUT/export) service invoice to an out-of-state buyer stated three falsehoods.</b>
    /// Measured before the fix on exactly this shape (Gujarat party, ₹1,00,000 LUT export, through the real screen):
    /// <c>IsInterState=False BuyerState="Maharashtra (27)" BuyerGstin="" PoS="Maharashtra (27)"</c> — the SELLER's own
    /// State printed as the BUYER's, the buyer's real GSTIN blanked, and an export declared intra-state (so the PDF
    /// renders CGST+SGST 0.00 rows instead of IGST).
    ///
    /// <para>Root cause: <c>ProjectServiceInvoice</c> routed on <c>PostedInterState</c>, which is true only when an
    /// Integrated forward leg exists. Zero-rated and exempt supplies post NO tax leg, so the posted signal read
    /// "intra"; <c>ConsistentBuyerStateCode</c> then saw live(inter) vs posted(intra) and took the "intra ⇒ print the
    /// home State" branch. FIX-3 (reconcile the document to the posted tax) and FIX-0 (admit no-tax invoices)
    /// collided. When there is no posted tax signal there is nothing for the document to contradict.</para>
    ///
    /// <para><b>Bite:</b> restore <c>PostedInterState</c> as the SOLE routing signal (drop the
    /// "no forward tax leg ⇒ use the party's recorded State" branch) and all four assertions below flip back to the
    /// measured falsehoods.</para>
    /// </summary>
    [Fact]
    public void ZeroRatedServiceInvoice_toOutOfStateParty_statesTheRealBuyerStateGstinAndPlaceOfSupply()
    {
        var k = NewServiceKit("Fix ZeroRated Inter Co");
        var entry = OpenAccountingSale(k);
        SelectParty(entry, k.InterCustomerId);            // Gujarat (24) vs home Maharashtra (27)
        FillLine(entry, k.ZeroRatedId, "100000");
        Assert.True(entry.Accept());

        var c = k.Vm.Company!;
        var v = PostedSale(c);
        Assert.True(v.IsAccountingInvoice);
        Assert.All(v.Lines, l => Assert.Null(l.Gst));     // NOT ONE posted tax leg — nothing to route from
        Assert.Equal(100000m, PartyLegAmount(c, v));

        Assert.True(VoucherPrintProjector.IsTaxInvoice(c, v));
        var data = VoucherPrintProjector.ProjectInvoice(c, v);

        // The four statements the document makes about itself, all read from the party's RECORDED state.
        Assert.True(data.IsInterState);                          // an export to Gujarat is not an intra-state supply
        Assert.Equal("Gujarat (24)", data.Buyer.StateText);      // the BUYER's State, not the seller's
        Assert.Equal(GstinGujarat, data.Buyer.Gstin);            // the buyer's REAL GSTIN, never blanked
        Assert.Equal("Gujarat (24)", data.PlaceOfSupply);
        Assert.NotEqual(data.Seller.StateText, data.PlaceOfSupply);

        // …and the money is untouched: no tax exists, so the customer owes exactly the LUT value.
        Assert.Empty(data.TaxRows);
        Assert.Equal(0m, data.TotalTax.Amount);
        Assert.Equal(100000m, data.TotalTaxable.Amount);
        Assert.Equal(100000m, data.GrandTotal.Amount);
        Assert.Equal(PartyLegAmount(c, v), data.GrandTotal.Amount);
    }

    /// <summary>
    /// The other half of F1: a <b>WHOLLY EXEMPT</b> service invoice to an out-of-state buyer posts no tax leg either,
    /// and was misstated identically.
    /// <para><b>Bite:</b> the same one — restore <c>PostedInterState</c> as the sole signal.</para>
    /// <para><b>FIX-W1l — read the inter-State assertions below for what they are.</b> Since W0-1 this voucher is a
    /// BILL OF SUPPLY (CGST §31(3)(c) covers exempted <i>services</i>), and CGST Rule 49 prescribes no counterpart to
    /// Rule 46 clause (n) "place of supply … in the case of a supply in the course of inter-State trade" — so
    /// <c>InvoicePdf</c> suppresses the intra/inter HEAD CAPTION ("Inter-State (IGST)") on it. <c>IsInterState</c> and
    /// <c>PlaceOfSupply</c> are therefore still correct DTO values, and still the right guard on the BUYER-BLOCK
    /// routing this test exists for. The document-kind assertion is added so nobody reads this test as evidence that
    /// it prints as a tax invoice.</para>
    /// <para><b>W0-1 follow-up review, finding #9 — this note used to say the two values "no longer reach the printed
    /// page for this document", which is FALSE for <c>PlaceOfSupply</c>: <c>InvoicePdf.DrawFirstHeader</c> prints
    /// "Place of Supply: " + <c>data.PlaceOfSupply</c> UNCONDITIONALLY; only the caption beside it is gated on
    /// <c>!IsBillOfSupply</c>. A maintainer acting on the old wording could have deleted the <c>PlaceOfSupply</c>
    /// assertion below (the only lock on the F1 fix for the exempt limb) or "completed" the suppression by gating the
    /// row itself, dropping the recipient's place of supply from every bill of supply with nothing failing. That is
    /// the FIX-W1d failure mode — a comment claiming behaviour the code does not have — so the wording is corrected
    /// and the row's PRESENCE in the bytes is now pinned below and in
    /// <c>InvoicePdfTests.A_bill_of_supply_is_titled_correctly_and_shows_no_tax_particular_at_all</c>.</para>
    /// <para><b>W0-1 follow-up review, finding #8 — this test posts the only INTER-STATE bill of supply the suite has,
    /// and stopped at the DTO.</b> Nothing in the repository ever RENDERED one, so the <c>!IsBillOfSupply</c> gate
    /// around the IGST head line had zero coverage in all four renderers. It renders now — bytes AND the on-screen
    /// mirror the operator approves.</para>
    /// <para><b>SETTLED AND USER-RATIFIED (R12, 2026-08-10):</b> that a wholly exempt SERVICE supply is a bill of
    /// supply is now a decision of record, not W0-1's own call — CGST Act §31(3)(c) binds "a registered person
    /// supplying exempted goods <b>or services</b> or both" (<c>https://cbic-gst.gov.in/pdf/CGST-Act-Updated-30092020.pdf</c>).</para>
    /// </summary>
    [Fact]
    public void WhollyExemptServiceInvoice_toOutOfStateParty_statesTheRealBuyerStateGstinAndPlaceOfSupply()
    {
        var k = NewServiceKit("Fix Exempt Inter Co");
        var entry = OpenAccountingSale(k);
        SelectParty(entry, k.InterCustomerId);
        FillLine(entry, k.ExemptServiceId, "7500");
        Assert.True(entry.Accept());

        var c = k.Vm.Company!;
        var v = PostedSale(c);
        Assert.All(v.Lines, l => Assert.Null(l.Gst));

        var data = VoucherPrintProjector.ProjectInvoice(c, v);
        Assert.True(data.IsInterState);
        Assert.Equal("Gujarat (24)", data.Buyer.StateText);
        Assert.Equal(GstinGujarat, data.Buyer.Gstin);
        Assert.Equal("Gujarat (24)", data.PlaceOfSupply);
        Assert.Equal(7500m, data.GrandTotal.Amount);
        Assert.Equal(PartyLegAmount(c, v), data.GrandTotal.Amount);
        // FIX-W1l: the document kind, so this test cannot be misread — see the note above.
        Assert.True(data.IsBillOfSupply);
        Assert.Equal("BILL OF SUPPLY", data.DocumentTitle);

        // --- finding #8: RENDER the inter-State bill of supply, on paper and on the approval screen. ---
        var s = Encoding.Latin1.GetString(InvoicePdf.Render(data, new PrintConfig(), new PageConfig()));
        Assert.Contains("BILL OF SUPPLY", s);
        Assert.Contains("Bill of Supply No", s);
        Assert.DoesNotContain("TAX INVOICE", s);
        Assert.DoesNotContain("IGST", s);            // Rule 49 prescribes no tax-amount particular — on EITHER routing
        Assert.DoesNotContain("CGST", s);
        Assert.DoesNotContain("Inter-State", s);
        Assert.DoesNotContain("Intra-State", s);
        Assert.Contains("Value of Supply", s);
        // finding #9: the Place of Supply ROW is printed on a bill of supply — pinned so it cannot vanish silently.
        Assert.Contains(@"Place of Supply: Gujarat \(24\)", s);   // PDF literal-string escaping of ( )

        var preview = new PrintPreviewViewModel(data);
        Assert.Equal("BILL OF SUPPLY", preview.Pages[0].Title);
        var cells = preview.Pages.SelectMany(p => p.Lines).SelectMany(r => r.Cells).ToList();
        Assert.DoesNotContain("IGST", cells);
        Assert.DoesNotContain("CGST", cells);
        Assert.DoesNotContain("Taxable Value", cells);
        Assert.Contains("Value of Supply", cells);
        Assert.Contains("Place of Supply: Gujarat (24)", cells);
    }

    /// <summary>
    /// F1(c) — the posted-tax-WINS behaviour that FIX-3 exists for must survive untouched wherever forward tax legs DO
    /// exist. This is the INTER direction (the existing suite only pinned the intra one): an IGST invoice whose party
    /// is later "corrected" to the company's own State keeps printing IGST, and rather than restate a home State that
    /// would contradict it, the State/GSTIN/Place-of-Supply are left blank (a Rule-46 omission, not a falsehood).
    /// <para><b>Bite:</b> route the buyer block from the LIVE party state whenever a tax leg exists (i.e. apply the F1
    /// branch unconditionally) and this prints "Maharashtra (27)" beside IGST 900.</para>
    /// </summary>
    [Fact]
    public void TaxableServiceInvoice_postedIgstStillWins_whenThePartyStateIsEditedAfterPosting()
    {
        var k = NewServiceKit("Fix PostedWins Co");
        var entry = OpenAccountingSale(k);
        SelectParty(entry, k.InterCustomerId);            // Gujarat (24) ⇒ IGST
        FillLine(entry, k.ConsultancyId, "5000");
        Assert.True(entry.Accept());

        var c = k.Vm.Company!;
        var v = PostedSale(c);
        Assert.Equal(900m, PostedHead(v, GstTaxHead.Integrated));

        var before = VoucherPrintProjector.ProjectInvoice(c, v);
        Assert.True(before.IsInterState);
        Assert.Equal("Gujarat (24)", before.PlaceOfSupply);
        Assert.Equal(GstinGujarat, before.Buyer.Gstin);

        // The party master is edited to the company's OWN State after the invoice was issued.
        c.FindLedger(k.InterCustomerId)!.PartyGst = new PartyGstDetails
        { RegistrationType = GstRegistrationType.Regular, Gstin = GstinMaharashtra, StateCode = "27" };

        var after = VoucherPrintProjector.ProjectInvoice(c, v);
        Assert.True(after.IsInterState);                     // the posted IGST is history and still routes the document
        Assert.Equal(900m, after.TotalIgst.Amount);
        Assert.Equal(0m, after.TotalCgst.Amount);
        Assert.Equal(string.Empty, after.PlaceOfSupply);     // unrecoverable ⇒ omitted, never the home State
        Assert.Equal(string.Empty, after.Buyer.StateText);
        Assert.Equal(string.Empty, after.Buyer.Gstin);
        Assert.Equal(5900m, after.GrandTotal.Amount);
    }

    // ================================================================ F2: the footing invariant

    /// <summary>
    /// <b>F2 (HIGH, money) — a crafted import file could flag a hand-keyed voucher.</b> Round-tripping the flag is
    /// CORRECT for a genuine service invoice, so the import cannot simply refuse it; what was missing is a CONSISTENCY
    /// check. Measured: export a plain hand-keyed GST sale, insert <c>isAccountingInvoice="true"</c> on the
    /// <c>&lt;voucher&gt;</c> element, parse (0 errors) + import (Applied) ⇒ <c>IsAccountingInvoice=True</c> with zero
    /// legs carrying GST metadata; projecting then gave <c>TaxRows=0 Taxable=5000 Tax=0 Grand=5000</c> against a posted
    /// party leg of 5,900 — a tax invoice understating the recorded debt by the whole tax, with an empty rate breakup.
    ///
    /// <para>The fix is a FOOTING INVARIANT: the projected GrandTotal (Σ service legs + Σ posted forward tax + Σ posted
    /// cess) must equal the posted party leg. When it does not, the voucher is not a service tax invoice and prints as
    /// the plain Dr/Cr voucher a pre-slice build produced. Structural, so it also catches any future divergence.</para>
    ///
    /// <para><b>Bite (superseded):</b> dropping the footing conjunct from <c>IsServiceAccountingInvoice</c> used to be
    /// the only thing standing between this fixture and a 5,900-rupee debt printed as a 5,000-rupee tax invoice. The
    /// F9 conjunct (<c>TaxedLegsCarryTheirTax</c>) now rejects this same shape independently — the hand-keyed tax legs
    /// carry no <c>GstLineTax</c>, so the voucher posts no forward tax leg while its value ledger declares a taxable
    /// 18% supply — which is defence in depth, but means the footing mutation no longer bites HERE. The dedicated
    /// footing fixture is <c>InvoicePrintFootingTests.CraftedUnderStatedTotal_isCaughtByTheFootingInvariantAlone</c>,
    /// built so that F9 and F11 both pass and only the totals reconciliation can reject it.</para>
    /// </summary>
    [Fact]
    public void CraftedImport_flagOnAHandKeyedVoucher_printsAsAPlainVoucher()
    {
        // --- a plain hand-keyed GST sale: 5,000 + 450 + 450 = 5,900, tax legs typed by hand (no GstLineTax) ---
        var k = NewServiceKit("Fix CraftedImport Source Co");
        var source = k.Vm.Company!;
        var salesType = source.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Sales && t.IsActive);
        new LedgerService(source).Post(new Voucher(Guid.NewGuid(), salesType.Id, FyStart.AddDays(10), new[]
        {
            new EntryLine(k.LocalCustomerId, Money.FromRupees(5900m), DrCr.Debit),
            new EntryLine(k.PlainSalesId, Money.FromRupees(5000m), DrCr.Credit),
            new EntryLine(TaxLedgerId(source, GstTaxHead.Central, GstTaxDirection.Output), Money.FromRupees(450m), DrCr.Credit),
            new EntryLine(TaxLedgerId(source, GstTaxHead.State, GstTaxDirection.Output), Money.FromRupees(450m), DrCr.Credit),
        }, partyId: k.LocalCustomerId, narration: "hand keyed"));

        // --- the ATTACK: set the flag on the exported <voucher> element and re-import ---
        var doc = XDocument.Parse(System.Text.Encoding.UTF8.GetString(CanonicalXml.Export(source)));
        var voucherEl = doc.Descendants("voucher").Single();
        voucherEl.SetAttributeValue("isAccountingInvoice", "true");
        var tampered = System.Text.Encoding.UTF8.GetBytes(doc.ToString(SaveOptions.DisableFormatting));

        var (model, errors) = CanonicalXml.Parse(tampered);
        Assert.Empty(errors);                                        // the file is well-formed — nothing rejects it
        Assert.NotNull(model);

        var target = NewEmptyCompany("Fix CraftedImport Target Co");
        Assert.True(new CompanyImportService(target).Apply(model!, DuplicatePolicy.Skip).Applied);

        var imported = target.Vouchers.Single(x => x.Narration == "hand keyed");
        Assert.True(imported.IsAccountingInvoice);                   // the flag really did survive …
        Assert.All(imported.Lines, l => Assert.Null(l.Gst));         // … onto a voucher with NO GST metadata at all
        Assert.Equal(5900m, PartyLegAmount(target, imported));

        // THE GUARANTEE: it does not print as a tax invoice, because the projection cannot foot to the posted debt.
        Assert.False(VoucherPrintProjector.IsServiceAccountingInvoice(target, imported));
        Assert.False(VoucherPrintProjector.IsTaxInvoice(target, imported));
        var detail = new VoucherDetailViewModel(target, imported);
        Assert.Equal(string.Empty, detail.DocumentLabel);
        Assert.Equal(PrintPreviewViewModel.PrintKind.Voucher, detail.BuildPrintPreview().Kind);

        // …and it prints as the plain Dr/Cr voucher a pre-slice build produced: all four posted legs, verbatim.
        var plain = VoucherPrintProjector.ProjectVoucher(target, imported);
        Assert.Equal(4, plain.Lines.Count);
        Assert.Single(plain.Lines, l => l.IsDebit && l.Amount.Amount == 5900m);
        Assert.Single(plain.Lines, l => !l.IsDebit && l.Amount.Amount == 5000m);
        Assert.Equal(2, plain.Lines.Count(l => !l.IsDebit && l.Amount.Amount == 450m));
    }

    /// <summary>
    /// The other side of F2: the footing invariant must not shut the feature off. A GENUINE service invoice still
    /// prints as a tax invoice and foots to the posted party leg exactly — for a taxed one, a cess-bearing one and a
    /// no-tax (zero-rated) one alike.
    /// <para><b>Bite:</b> make the footing invariant compare against Σ service legs only (drop the posted tax) and
    /// every taxed invoice below is demoted to a plain voucher.</para>
    /// </summary>
    [Fact]
    public void GenuineServiceInvoices_stillPrintAsTaxInvoices_andFootExactly()
    {
        foreach (var (name, ledger, amount, expected) in new (string, Func<Kit, Guid>, string, decimal)[]
        {
            ("taxed",  k => k.ConsultancyId,  "5000",  5900m),
            ("cess",   k => k.CessServiceId, "10000", 13000m),
            ("zero",   k => k.ZeroRatedId,    "5000",  5000m),
        })
        {
            var k = NewServiceKit("Fix Foots " + name + " Co");
            var entry = OpenAccountingSale(k);
            SelectParty(entry, k.LocalCustomerId);
            FillLine(entry, ledger(k), amount);
            Assert.True(entry.Accept());

            var c = k.Vm.Company!;
            var v = PostedSale(c);
            Assert.True(VoucherPrintProjector.IsTaxInvoice(c, v));
            var data = VoucherPrintProjector.ProjectInvoice(c, v);
            Assert.Equal(expected, data.GrandTotal.Amount);
            Assert.Equal(PartyLegAmount(c, v), data.GrandTotal.Amount);
        }
    }

    // ================================================================ F3 / F4: the item path's cess is POSTED, not live

    /// <summary>
    /// <b>F4 (MEDIUM, contract mismatch) — FIX-1's comment claimed "the printed cess is the cess that was posted", but
    /// the ITEM path RE-RESOLVED it live.</b> Change the item's cess rate after posting and the reprint moved off the
    /// GL: measured, a printed Grand Total against a party leg that never changed. The service path already reads the
    /// POSTED cess legs; the item path now does too, falling back to a live resolve only when no cess leg was posted.
    /// <para><b>Bite:</b> use <c>invoiceTax.TotalCess</c> (the live re-resolve) instead of the posted cess legs and the
    /// reprint foots to 1,780 against a posted party leg of 1,300.</para>
    ///
    /// <para><b>F10 — re-pointed at an ODD-PAISA fixture.</b> This ran on 10 @ ₹100 (party leg 1,300), where the print
    /// path's invented nearest-rupee round-off was 0.00, so it could not see the ±0.50 defect it was otherwise
    /// perfectly placed to catch. On 7 @ ₹133.33 the posted party leg is 1213.31 and the invented round-off was −0.31,
    /// so this test now bites <c>applyInvoiceRoundOff: true</c> as well as its own F4 mutation.</para>
    /// </summary>
    [Fact]
    public void ItemInvoice_cessRateChangedAfterPosting_reprintStillFootsToThePostedPartyLeg()
    {
        var vm = NewItemInvoiceKit("Fix ItemCessRate Co", out var widgetId, out var godownId, out var customerId,
            cess: g => { g.CessApplicable = true; g.CessValuationMode = CessValuationMode.AdValorem; g.CessRateBasisPoints = 1200; });
        // 7 @ ₹133.33 ⇒ taxable 933.31; 18% ⇒ CGST 84.00 + SGST 84.00; 12% cess ⇒ 112.00; party leg 1213.31.
        var c = PostItemInvoice(vm, widgetId, godownId, customerId, qty: "7", rate: "133.33");
        var v = PostedSale(c);
        Assert.Equal(112m, PostedHead(v, GstTaxHead.Cess));
        Assert.Equal(1213.31m, PartyLegAmount(c, v));

        var before = VoucherPrintProjector.ProjectInvoice(c, v);
        Assert.Equal(112m, before.TotalCess.Amount);
        Assert.Equal(1213.31m, before.GrandTotal.Amount);

        // The de-merit cess on this HSN is revised 12% -> 60% AFTER the invoice was issued.
        c.FindStockItem(widgetId)!.Gst!.CessRateBasisPoints = 6000;

        var after = VoucherPrintProjector.ProjectInvoice(c, v);
        Assert.Equal(112m, after.TotalCess.Amount);          // the POSTED cess, not the new master rate
        Assert.Equal(84m, after.TotalCgst.Amount);
        Assert.Equal(84m, after.TotalSgst.Amount);
        Assert.Equal(933.31m, after.TotalTaxable.Amount);
        Assert.Equal(0m, after.RoundOff.Amount);             // no round-off leg was posted ⇒ none is printed (F10)
        Assert.Equal(1213.31m, after.GrandTotal.Amount);
        Assert.Equal(PartyLegAmount(c, v), after.GrandTotal.Amount);
    }

    /// <summary>
    /// <b>F3 (MEDIUM, a NEW crash) — reprinting a historical ITEM invoice could throw.</b> FIX-1 introduced a
    /// <c>gst.ResolveCess(...)</c> call on the print path; that method is deliberately FAIL-FAST for an RSP-factor cess
    /// with no declared Retail Sale Price. The accept path catches it and shows a message; the print path did not, and
    /// HEAD never called it here at all. Measured: post an RSP-factor cess item invoice, clear the item's RSP on the
    /// master, reprint ⇒ <c>InvalidOperationException</c>. Reprinting an issued document must never depend on a live
    /// master still being valid.
    /// <para><b>Bite:</b> resolve the cess unconditionally (before consulting the posted cess legs) and this throws.</para>
    /// </summary>
    [Fact]
    public void ItemInvoice_rspCessWhoseRetailSalePriceIsLaterCleared_reprintsWithoutThrowing()
    {
        var vm = NewItemInvoiceKit("Fix ItemRspCess Co", out var widgetId, out var godownId, out var customerId,
            cess: g =>
            {
                g.CessApplicable = true;
                g.CessValuationMode = CessValuationMode.RetailSalePriceFactor;
                g.CessRspFactorMillis = 100;                  // 10 units x RSP 150 x 100/1000 = 150.00
                g.RetailSalePrice = Money.FromRupees(150m);
            });
        var c = PostItemInvoice(vm, widgetId, godownId, customerId);
        var v = PostedSale(c);
        Assert.Equal(150m, PostedHead(v, GstTaxHead.Cess));
        Assert.Equal(1330m, PartyLegAmount(c, v));

        // The RSP is cleared on the master (re-classified, or simply corrected) AFTER the invoice was issued.
        c.FindStockItem(widgetId)!.Gst!.RetailSalePrice = null;

        var data = VoucherPrintProjector.ProjectInvoice(c, v);   // must not throw
        Assert.Equal(150m, data.TotalCess.Amount);
        Assert.Equal(1330m, data.GrandTotal.Amount);
        Assert.Equal(PartyLegAmount(c, v), data.GrandTotal.Amount);
        Assert.NotEmpty(new VoucherDetailViewModel(c, v).BuildPrintPreview().PdfBytes);
    }

    /// <summary>
    /// The second half of F3, which the posted-cess preference alone does NOT cover: a voucher that posted <b>no</b>
    /// cess leg falls back to a live resolve, and that resolve can still throw if the master gained an unvaluable
    /// RSP-factor cess after posting. Printing must degrade to zero cess, never crash.
    /// <para><b>Bite:</b> delete the try/catch around the fallback <c>ResolveCess</c> and this throws
    /// <c>InvalidOperationException</c>.</para>
    ///
    /// <para><b>F10 — re-pointed at an ODD-PAISA fixture</b> (7 @ ₹133.33 ⇒ a posted party leg of 1101.31, where the
    /// invented print-time round-off was −0.31). It used to run on 10 @ ₹100, whose round-off was 0.00.</para>
    /// </summary>
    [Fact]
    public void ItemInvoice_masterGainsAnUnvaluableCessAfterPosting_reprintsWithZeroCessInsteadOfThrowing()
    {
        var vm = NewItemInvoiceKit("Fix ItemLateCess Co", out var widgetId, out var godownId, out var customerId);
        var c = PostItemInvoice(vm, widgetId, godownId, customerId, qty: "7", rate: "133.33");
        var v = PostedSale(c);
        Assert.Equal(0m, PostedHead(v, GstTaxHead.Cess));     // no cess leg was ever posted
        Assert.Equal(1101.31m, PartyLegAmount(c, v));

        // The item master later declares an RSP-factor cess but carries no Retail Sale Price — unvaluable.
        var gstBlock = c.FindStockItem(widgetId)!.Gst!;
        gstBlock.CessApplicable = true;
        gstBlock.CessValuationMode = CessValuationMode.RetailSalePriceFactor;
        gstBlock.CessRspFactorMillis = 100;
        gstBlock.RetailSalePrice = null;

        var data = VoucherPrintProjector.ProjectInvoice(c, v); // must not throw
        Assert.Equal(0m, data.TotalCess.Amount);
        Assert.Equal(0m, data.RoundOff.Amount);                // F10: no posted round-off leg ⇒ none printed
        Assert.Equal(1101.31m, data.GrandTotal.Amount);
        Assert.Equal(PartyLegAmount(c, v), data.GrandTotal.Amount);
    }

    /// <summary>
    /// F4's third strand: the print loop resolved the RATE with the <b>un-dated</b> overload while resolving the CESS
    /// with the DATED one. The accept path uses the dated overload for both, so a company carrying a dated rate-history
    /// window printed a different rate from the one it posted. Here the item's HSN is re-rated 18% ⇒ 40% from
    /// 22-Sep-2025; an invoice dated BEFORE that must reprint at the historic 18% it posted.
    /// <para><b>Bite:</b> call <c>gst.ResolveRate(item, valueLedger)</c> (drop <c>voucher.Date</c>) and the reprint
    /// states 40% tax on a voucher whose GL carries 18%.</para>
    ///
    /// <para><b>F10 — re-pointed at an ODD-PAISA fixture</b> (7 @ ₹133.33 ⇒ a posted party leg of 1101.31). On the old
    /// 10 @ ₹100 fixture the invented print-time round-off was 0.00 and this test could not see it.</para>
    /// </summary>
    [Fact]
    public void ItemInvoice_reprint_resolvesTheRateAsOfTheVoucherDate_likeTheAcceptPath()
    {
        // The item MASTER carries the current 40% (GST 2.0) rate — the un-dated resolution's answer …
        var vm = NewItemInvoiceKit("Fix ItemDatedRate Co", out var widgetId, out var godownId, out var customerId,
            rateBasisPoints: 4000);
        var c = vm.Company!;
        // … while two dated windows on the same HSN say 18% up to 21-Sep-2025 and 40% from 22-Sep-2025.
        c.Gst!.AddRateHistory(new GstRateHistoryEntry(Guid.NewGuid(), "847130", 1800, GstRateClass.Legacy,
            new DateOnly(2017, 7, 1), new DateOnly(2025, 9, 21), GstValuationBasis.TransactionValue, "18% (legacy)"));
        c.Gst!.AddRateHistory(new GstRateHistoryEntry(Guid.NewGuid(), "847130", 4000, GstRateClass.DeMerit,
            new DateOnly(2025, 9, 22), null, GstValuationBasis.TransactionValue, "40% (GST 2.0)"));

        // 7 @ ₹133.33 ⇒ taxable 933.31; the historic 18% ⇒ CGST 84.00 + SGST 84.00; party leg 1101.31.
        PostItemInvoice(vm, widgetId, godownId, customerId, dateText: "01-05-2024", qty: "7", rate: "133.33");
        var v = PostedSale(c);
        Assert.Equal(new DateOnly(2024, 5, 1), v.Date);
        Assert.Equal(84m, PostedHead(v, GstTaxHead.Central));  // POSTED at the historic 18% (the accept path is dated)
        Assert.Equal(84m, PostedHead(v, GstTaxHead.State));
        Assert.Equal(1101.31m, PartyLegAmount(c, v));

        var data = VoucherPrintProjector.ProjectInvoice(c, v);
        Assert.Equal("18%", Assert.Single(data.TaxRows).RateLabel);   // not the master's live 40%
        Assert.Equal(84m, data.TotalCgst.Amount);
        Assert.Equal(84m, data.TotalSgst.Amount);
        Assert.Equal(0m, data.RoundOff.Amount);                       // F10: no posted round-off leg ⇒ none printed
        Assert.Equal(1101.31m, data.GrandTotal.Amount);
        Assert.Equal(PartyLegAmount(c, v), data.GrandTotal.Amount);
    }

    // ================================================================ F5: the three surviving mutations

    /// <summary>
    /// <b>F5(a).</b> No test posted an ITEM invoice carrying the accounting-invoice flag, so the
    /// <c>if (voucher.HasInventoryLines) return false;</c> conjunct of <c>IsServiceAccountingInvoice</c> was never
    /// exercised in isolation — deleting it survived all 3,469 tests. An imported flagged item invoice would divert
    /// into the service projection and print a single "Sales" row with no quantity and no rate instead of the goods.
    /// (The flag is read-only after construction, so the realistic vector is an import — used here.)
    /// <para><b>Bite:</b> delete that conjunct and the Widget row becomes a "Sales" row with blank Qty/Rate.</para>
    /// </summary>
    [Fact]
    public void ImportedFlaggedItemInvoice_stillPrintsTheItemProjection()
    {
        var vm = NewItemInvoiceKit("Fix FlaggedItem Source Co", out var widgetId, out var godownId, out var customerId);
        var source = PostItemInvoice(vm, widgetId, godownId, customerId);

        var doc = XDocument.Parse(System.Text.Encoding.UTF8.GetString(CanonicalXml.Export(source)));
        doc.Descendants("voucher").Single().SetAttributeValue("isAccountingInvoice", "true");
        var (model, errors) = CanonicalXml.Parse(
            System.Text.Encoding.UTF8.GetBytes(doc.ToString(SaveOptions.DisableFormatting)));
        Assert.Empty(errors);

        var target = NewEmptyCompany("Fix FlaggedItem Target Co");
        Assert.True(new CompanyImportService(target).Apply(model!, DuplicatePolicy.Skip).Applied);

        var v = PostedSale(target);
        Assert.True(v.IsAccountingInvoice);       // flagged …
        Assert.True(v.HasInventoryLines);         // … but it carries stock, so it is an ITEM invoice

        Assert.False(VoucherPrintProjector.IsServiceAccountingInvoice(target, v));
        Assert.True(VoucherPrintProjector.IsTaxInvoice(target, v));
        var data = VoucherPrintProjector.ProjectInvoice(target, v);
        var row = Assert.Single(data.Items);
        Assert.Equal("Widget", row.Description);          // the GOODS row, not a "Sales" ledger row
        Assert.Equal("847130", row.HsnSac);
        Assert.Equal("10 Nos", row.QuantityText);
        Assert.Equal("100.00", row.RateText);
        Assert.Equal(1180m, data.GrandTotal.Amount);
    }

    /// <summary>
    /// <b>F5(c).</b> Deleting <c>IsReverseCharge: false</c> from <c>PostedCess</c> survived the whole suite: nothing
    /// posted a reverse-charge cess leg on a printable invoice. An RCM cess is the RECIPIENT's liability to the
    /// government — it is never charged on the supplier's invoice, so the party leg excludes it and the printed Grand
    /// Total must too.
    /// <para><b>Bite:</b> drop the <c>IsReverseCharge: false</c> pattern from <c>PostedCess</c> and the printed cess
    /// becomes 200 — which also breaks the footing invariant, so the document is demoted to a plain voucher.</para>
    /// </summary>
    [Fact]
    public void ServiceInvoice_reverseChargeCessLeg_isNeverBilledToTheCustomer()
    {
        var k = NewServiceKit("Fix RcmCess Co");
        var c = k.Vm.Company!;
        var salesType = c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Sales && t.IsActive);
        var gst = new GstService(c);
        gst.EnsureCessLedgers();
        var cgst = gst.FindTaxLedger(GstTaxHead.Central, GstTaxDirection.Output)!.Id;
        var sgst = gst.FindTaxLedger(GstTaxHead.State, GstTaxDirection.Output)!.Id;
        // A ring-fenced REVERSE-CHARGE cess liability parked on the same voucher. Its ledger is a GST (Duties & Taxes)
        // ledger, so it is not a service leg; the liability is the recipient's to the government, so it is NOT part of
        // the party's 5,900 — and must not reach the printed Grand Total.
        var rcmCess = gst.FindTaxLedger(GstTaxHead.Cess, GstTaxDirection.Output)!.Id;
        var expense = AddLedger(c, "RCM Cess Borne", "Indirect Expenses");

        var v = new Voucher(Guid.NewGuid(), salesType.Id, FyStart.AddDays(20), new[]
        {
            new EntryLine(k.LocalCustomerId, Money.FromRupees(5900m), DrCr.Debit),
            new EntryLine(k.ConsultancyId, Money.FromRupees(5000m), DrCr.Credit),
            new EntryLine(cgst, Money.FromRupees(450m), DrCr.Credit,
                gst: new GstLineTax(GstTaxHead.Central, 900, Money.FromRupees(5000m))),
            new EntryLine(sgst, Money.FromRupees(450m), DrCr.Credit,
                gst: new GstLineTax(GstTaxHead.State, 900, Money.FromRupees(5000m))),
            new EntryLine(expense.Id, Money.FromRupees(200m), DrCr.Debit),
            new EntryLine(rcmCess, Money.FromRupees(200m), DrCr.Credit,
                gst: new GstLineTax(GstTaxHead.Cess, 400, Money.FromRupees(5000m), isReverseCharge: true)),
        }, partyId: k.LocalCustomerId, isAccountingInvoice: true);
        new LedgerService(c).Post(v);

        Assert.Equal(5900m, PartyLegAmount(c, v));
        Assert.True(VoucherPrintProjector.IsTaxInvoice(c, v));

        var data = VoucherPrintProjector.ProjectInvoice(c, v);
        Assert.Equal(0m, data.TotalCess.Amount);           // the RCM cess is NOT the customer's charge
        Assert.Equal(450m, data.TotalCgst.Amount);
        Assert.Equal(450m, data.TotalSgst.Amount);
        Assert.Equal(5000m, data.TotalTaxable.Amount);
        Assert.Equal(5900m, data.GrandTotal.Amount);
        Assert.Equal(PartyLegAmount(c, v), data.GrandTotal.Amount);
        // …and it injects no phantom rate row either.
        Assert.Equal("18%", Assert.Single(data.TaxRows).RateLabel);
    }

    // ---------------------------------------------------------------- scaffolding

    private sealed class Kit
    {
        public required MainWindowViewModel Vm { get; init; }
        public required Guid ConsultancyId { get; init; }
        public required Guid PlainSalesId { get; init; }
        public required Guid CessServiceId { get; init; }
        public required Guid ExemptServiceId { get; init; }
        public required Guid ZeroRatedId { get; init; }
        public required Guid LocalCustomerId { get; init; }
        public required Guid InterCustomerId { get; init; }
    }

    /// <summary>A GST-enabled (home Maharashtra 27) company with SAC-bearing service ledgers + an in-state and an
    /// out-of-state customer.</summary>
    private Kit NewServiceKit(string companyName)
    {
        var vm = NewCompany(companyName);
        var c = vm.Company!;

        var consultancy = AddLedger(c, "Consultancy Income", "Sales Accounts");
        consultancy.SalesPurchaseGst = Sac("998311", 1800);
        var plainSales = AddLedger(c, "Sales @18%", "Sales Accounts");
        plainSales.SalesPurchaseGst = Sac("998314", 1800);
        var cessService = AddLedger(c, "Cess Service", "Sales Accounts");
        cessService.SalesPurchaseGst = Sac("998399", 1800);
        cessService.SalesPurchaseGst.CessApplicable = true;
        cessService.SalesPurchaseGst.CessValuationMode = CessValuationMode.AdValorem;
        cessService.SalesPurchaseGst.CessRateBasisPoints = 1200;
        var exemptService = AddLedger(c, "Exempt Service", "Sales Accounts");
        exemptService.SalesPurchaseGst = new StockItemGstDetails
        { HsnSac = "999999", Taxability = GstTaxability.Exempt, SupplyType = GstSupplyType.Services };
        var zeroRated = AddLedger(c, "Export Service (LUT)", "Sales Accounts");
        zeroRated.SalesPurchaseGst = Sac("998313", 0);

        var localCustomer = AddLedger(c, "Local Customer", "Sundry Debtors");
        localCustomer.PartyGst = new PartyGstDetails
        { RegistrationType = GstRegistrationType.Regular, Gstin = GstinMaharashtra, StateCode = "27" };
        var interCustomer = AddLedger(c, "Gujarat Customer", "Sundry Debtors");
        interCustomer.PartyGst = new PartyGstDetails
        { RegistrationType = GstRegistrationType.Regular, Gstin = GstinGujarat, StateCode = "24" };

        _storage.Save(c);
        return new Kit
        {
            Vm = vm,
            ConsultancyId = consultancy.Id,
            PlainSalesId = plainSales.Id,
            CessServiceId = cessService.Id,
            ExemptServiceId = exemptService.Id,
            ZeroRatedId = zeroRated.Id,
            LocalCustomerId = localCustomer.Id,
            InterCustomerId = interCustomer.Id,
        };
    }

    private static StockItemGstDetails Sac(string sac, int bp) => new()
    {
        HsnSac = sac, Taxability = GstTaxability.Taxable, RateBasisPoints = bp,
        SupplyType = GstSupplyType.Services,
    };

    /// <summary>A GST-enabled company with a Widget (HSN 847130 @18%, optional cess), a Sales ledger and a local
    /// customer — the item-invoice fixture.</summary>
    private MainWindowViewModel NewItemInvoiceKit(
        string companyName, out Guid widgetId, out Guid godownId, out Guid customerId,
        Action<StockItemGstDetails>? cess = null, int rateBasisPoints = 1800)
    {
        var vm = NewCompany(companyName);
        var c = vm.Company!;

        var inv = new InventoryService(c);
        var grp = inv.CreateStockGroup("Goods");
        var nos = inv.CreateSimpleUnit("Nos", "Numbers", unitQuantityCode: "NOS");
        var main = c.MainLocation!.Id;
        var widget = inv.CreateStockItem("Widget", grp.Id, nos.Id);
        widget.Gst = new StockItemGstDetails
        { HsnSac = "847130", Taxability = GstTaxability.Taxable, RateBasisPoints = rateBasisPoints };
        cess?.Invoke(widget.Gst);
        inv.AddOpeningBalance(widget.Id, main, 200m, Money.FromRupees(100m));

        var salesLedger = AddLedger(c, "Sales", "Sales Accounts");
        salesLedger.SalesPurchaseGst = new StockItemGstDetails
        {
            HsnSac = "847130", Taxability = GstTaxability.Taxable, RateBasisPoints = 1800,
            SupplyType = GstSupplyType.Goods,
        };
        var customer = AddLedger(c, "Local Customer", "Sundry Debtors");
        customer.PartyGst = new PartyGstDetails
        { RegistrationType = GstRegistrationType.Regular, Gstin = GstinMaharashtra, StateCode = "27" };
        _storage.Save(c);

        widgetId = widget.Id;
        godownId = main;
        customerId = customer.Id;
        return vm;
    }

    private MainWindowViewModel NewCompany(string companyName)
    {
        var vm = new MainWindowViewModel(_storage);
        vm.NewCompanyName = companyName;
        vm.CreateCompany();
        var c = vm.Company!;
        c.FinancialYearStart = FyStart;
        c.BooksBeginFrom = FyStart;
        new GstService(c).EnableGst(new GstConfig
        {
            HomeStateCode = "27", Gstin = GstinMaharashtra, RegistrationType = GstRegistrationType.Regular,
            ApplicableFrom = FyStart, Periodicity = GstReturnPeriodicity.Monthly,
        });
        return vm;
    }

    private Company NewEmptyCompany(string companyName) => NewCompany(companyName).Company!;

    /// <summary>
    /// Posts Widgets through the REAL item-invoice screen; returns the company. Defaults to the original 10 @ ₹100.
    /// <para><b>F10:</b> <paramref name="qty"/>/<paramref name="rate"/> exist so a test can post an <b>ODD-PAISA</b>
    /// invoice (7 @ ₹133.33 ⇒ taxable 933.31). Every foot-to-the-party-leg assertion in this file used to run on whole
    /// rupees (1,180 / 1,300 / 1,330), where the print path's invented nearest-rupee round-off happened to be 0.00 —
    /// which is precisely why a ±0.50 money defect on the customer-facing document survived the whole suite.</para>
    /// </summary>
    private static Company PostItemInvoice(
        MainWindowViewModel vm, Guid widgetId, Guid godownId, Guid customerId, string? dateText = null,
        string qty = "10", string rate = "100.00")
    {
        vm.OpenVoucher(VoucherBaseType.Sales);
        var entry = vm.VoucherEntry!;
        entry.ToggleItemInvoice();
        if (dateText is not null) entry.DateText = dateText;
        SelectParty(entry, customerId);
        while (entry.InventoryLines.Count == 0) entry.AddInventoryLine();
        var line = entry.InventoryLines[0];
        line.SelectedItem = entry.StockItems.Single(i => i.Id == widgetId);
        line.SelectedGodown = entry.Godowns.Single(g => g.Id == godownId);
        line.QuantityText = qty;
        line.RateText = rate;
        Assert.True(entry.Accept());
        return vm.Company!;
    }

    private static DomainLedger AddLedger(Company c, string name, string groupName)
    {
        var group = c.FindGroupByName(groupName) ?? throw new InvalidOperationException($"No group '{groupName}'.");
        var ledger = new DomainLedger(Guid.NewGuid(), name, group.Id, Money.Zero, openingIsDebit: false);
        c.AddLedger(ledger);
        return ledger;
    }

    private static VoucherEntryViewModel OpenAccountingSale(Kit k)
    {
        k.Vm.OpenVoucher(VoucherBaseType.Sales);
        var entry = k.Vm.VoucherEntry!;
        entry.ChangeMode(); // As Voucher -> Item Invoice
        entry.ChangeMode(); // Item Invoice -> Accounting Invoice
        Assert.True(entry.IsAccountingInvoice);
        Assert.True(entry.IsAccountingGstInvoice);
        return entry;
    }

    private static void SelectParty(VoucherEntryViewModel entry, Guid partyId) =>
        entry.SelectedParty = entry.Parties.Single(p => p.Ledger?.Id == partyId);

    private static void FillLine(VoucherEntryViewModel entry, Guid ledgerId, string amount, int index = 0)
    {
        while (entry.AccountingInvoiceLines.Count <= index) entry.AddAccountingInvoiceLine();
        var line = entry.AccountingInvoiceLines[index];
        line.SelectedLedger = entry.AccountingInvoiceLedgers.Single(l => l.Id == ledgerId);
        line.AmountText = amount;
    }

    private static Voucher PostedSale(Company c) =>
        c.Vouchers.Single(v => c.FindVoucherType(v.TypeId)!.BaseType == VoucherBaseType.Sales);

    private static Guid TaxLedgerId(Company c, GstTaxHead head, GstTaxDirection direction) =>
        new GstService(c).FindTaxLedger(head, direction)!.Id;

    private static decimal PostedHead(Voucher v, GstTaxHead head)
    {
        var total = 0m;
        foreach (var l in v.Lines)
            if (l.Gst is { } g && !g.IsReverseCharge && g.TaxHead == head)
                total += l.Amount.Amount;
        return total;
    }

    private static decimal PartyLegAmount(Company c, Voucher v) =>
        v.Lines.Single(l => l.LedgerId == v.PartyId!.Value).Amount.Amount;

    public void Dispose()
    {
        try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true); }
        catch { /* best-effort temp cleanup */ }
    }
}
