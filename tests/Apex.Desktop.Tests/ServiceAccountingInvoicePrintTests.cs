using System;
using System.IO;
using System.Linq;
using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Io;
using Apex.Ledger.Services;
using Apex.Desktop.Services;
using Apex.Desktop.ViewModels;
using DomainLedger = Apex.Ledger.Domain.Ledger;

namespace Apex.Desktop.Tests;

/// <summary>
/// Printing a <b>SERVICE (Accounting Invoice) sale as a GST tax invoice</b> (user decision, 2026-07-26). Before this
/// slice <c>VoucherPrintProjector.IsTaxInvoice</c> gated on <c>voucher.HasInventoryLines</c>, which is FALSE for a
/// service invoice <i>by design</i> — so the shipped Accounting-Invoice feature produced no customer-facing document
/// at all: no SAC, no GSTIN blocks, no rate breakup.
///
/// <para>Every assertion here drives a <b>REAL posted voucher</b> (created through the real entry VM over a throwaway
/// <c>.db</c>) through the <b>REAL projector</b> and checks the <b>posted</b> money — never "a field exists". The tax a
/// service invoice prints is read off its posted <c>GstLineTax</c> legs and is <b>never recomputed</b>: mutating the
/// service ledger's rate master after posting must not move a single printed figure
/// (<see cref="ServiceInvoice_printedTax_equalsPostedTax_neverRecomputed"/>).</para>
///
/// <para><b>The safety argument</b> is <see cref="HandKeyedAsVoucherSale_printOutput_isUnchanged"/>: an existing
/// hand-keyed As-Voucher GST sale must print EXACTLY as it does today, same label, same rows. The discriminator is the
/// PERSISTED <c>Voucher.IsAccountingInvoice</c> flag (schema v49) — stamped by the Accounting-Invoice accept path and
/// by nothing else, so a hand-keyed sale is excluded structurally and every already-posted voucher (flag false by
/// migration default) prints exactly as it did. It replaced an inference from posted <c>GstLineTax</c> forward legs
/// that was wrong in BOTH directions: it excluded zero-rated (LUT/export) and wholly-exempt service invoices, which
/// post no tax leg yet ARE Rule-46 tax invoices
/// (<see cref="ZeroRatedServiceInvoice_printsAsTaxInvoice"/>,
/// <see cref="WhollyExemptServiceInvoice_printsAsTaxInvoice"/>), and its exclusion of hand-keyed sales rested on "no
/// other path stamps <c>GstLineTax</c> on a ledger-only Sales voucher" — true of the code, not of the data. The tax
/// itself is still read off the posted legs and never recomputed.</para>
/// </summary>
public sealed class ServiceAccountingInvoicePrintTests : IDisposable
{
    private const string GstinMaharashtra = "27AAPFU0939F1ZV";
    private const string GstinGujarat = "24AAACC1206D1ZM";
    private static readonly DateOnly FyStart = new(2024, 4, 1);

    private readonly string _tempDir;
    private readonly CompanyStorage _storage;

    public ServiceAccountingInvoicePrintTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "ApexServiceInvoicePrintTests_" + Guid.NewGuid().ToString("N"));
        _storage = new CompanyStorage(_tempDir);
    }

    // ================================================================ (1) the feature: SAC + posted tax on the invoice

    [Fact]
    public void ServiceInvoice_projectsAsTaxInvoice_withSacAndPostedTax()
    {
        var k = NewServiceKit("Svc Print Tax Co");
        var entry = OpenAccountingSale(k);
        SelectParty(entry, k.LocalCustomerId);

        // Consultancy (SAC 998311) @18% ₹5,000 intra-state ⇒ POSTED CGST 450 + SGST 450; customer 5,900.
        FillLine(entry, k.ConsultancyId, "5000");
        Assert.True(entry.Accept());

        var c = k.Vm.Company!;
        var v = PostedSale(c);
        Assert.False(v.HasInventoryLines);                       // it really is a service (ledger-only) invoice
        Assert.Equal(450m, PostedHead(c, v, GstTaxHead.Central)); // and the POSTED tax is what we will assert against
        Assert.Equal(450m, PostedHead(c, v, GstTaxHead.State));

        // The gate now says "tax invoice" — this is the whole user-visible change.
        Assert.True(VoucherPrintProjector.IsTaxInvoice(c, v));
        Assert.Equal("Tax Invoice", new VoucherDetailViewModel(c, v).DocumentLabel);

        var data = VoucherPrintProjector.ProjectInvoice(c, v);

        // --- the service LINE, with its SAC (Rule 46 (f)/(g)) ---
        var row = Assert.Single(data.Items);
        Assert.Equal("Consultancy Income", row.Description);
        Assert.Equal("998311", row.HsnSac);
        Assert.Equal(5000m, row.TaxableValue.Amount);
        Assert.Equal(string.Empty, row.QuantityText);   // a service carries no quantity …
        Assert.Equal(string.Empty, row.RateText);       // … and no per-unit rate

        // --- the rate breakup + totals, straight off the POSTED legs ---
        Assert.False(data.IsInterState);
        var tr = Assert.Single(data.TaxRows);
        Assert.Equal("18%", tr.RateLabel);
        Assert.Equal(5000m, tr.TaxableValue.Amount);
        Assert.Equal(450m, tr.Cgst.Amount);
        Assert.Equal(450m, tr.Sgst.Amount);
        Assert.Equal(0m, tr.Igst.Amount);
        Assert.Equal(5000m, data.TotalTaxable.Amount);
        Assert.Equal(450m, data.TotalCgst.Amount);
        Assert.Equal(450m, data.TotalSgst.Amount);
        Assert.Equal(0m, data.TotalIgst.Amount);
        Assert.Equal(0m, data.RoundOff.Amount);
        Assert.Equal(5900m, data.GrandTotal.Amount);   // foots to the posted party leg exactly
        Assert.Equal(5900m, PartyLegAmount(c, v));

        // --- the GSTIN / address blocks (Rule 46 (a)/(d)/(e)) ---
        Assert.Equal(GstinMaharashtra, data.Seller.Gstin);
        Assert.Equal("Local Customer", data.Buyer.Name);
        Assert.Equal(GstinMaharashtra, data.Buyer.Gstin);
        Assert.Equal("Maharashtra (27)", data.Buyer.StateText);
        Assert.Equal("Maharashtra (27)", data.PlaceOfSupply);

        // --- and the REAL renderer produces a real tax-invoice PDF through the REAL routing ---
        var preview = new VoucherDetailViewModel(c, v).BuildPrintPreview();
        Assert.Equal(PrintPreviewViewModel.PrintKind.Invoice, preview.Kind);
        Assert.NotEmpty(preview.PdfBytes);
    }

    // ================================================================ (2) inter-state ⇒ IGST only

    [Fact]
    public void ServiceInvoice_interState_printsIgst()
    {
        var k = NewServiceKit("Svc Print Igst Co");
        var entry = OpenAccountingSale(k);
        SelectParty(entry, k.InterCustomerId);   // Gujarat (24) vs home Maharashtra (27)

        FillLine(entry, k.ConsultancyId, "5000");
        Assert.True(entry.Accept());

        var c = k.Vm.Company!;
        var v = PostedSale(c);
        Assert.Equal(900m, PostedHead(c, v, GstTaxHead.Integrated));  // POSTED IGST 900, no CGST/SGST
        Assert.Equal(0m, PostedHead(c, v, GstTaxHead.Central));

        var data = VoucherPrintProjector.ProjectInvoice(c, v);
        Assert.True(data.IsInterState);
        Assert.Equal(900m, data.TotalIgst.Amount);
        Assert.Equal(0m, data.TotalCgst.Amount);
        Assert.Equal(0m, data.TotalSgst.Amount);
        var tr = Assert.Single(data.TaxRows);
        Assert.Equal("18%", tr.RateLabel);
        Assert.Equal(900m, tr.Igst.Amount);
        Assert.Equal(0m, tr.Cgst.Amount);
        Assert.Equal(0m, tr.Sgst.Amount);
        Assert.Equal(5900m, data.GrandTotal.Amount);
        Assert.Equal("Gujarat (24)", data.PlaceOfSupply);
    }

    // ================================================================ (3) multi-rate ⇒ each SAC + each rate row

    [Fact]
    public void ServiceInvoice_multiRate_printsEachSacAndRate()
    {
        var k = NewServiceKit("Svc Print MultiRate Co");
        var entry = OpenAccountingSale(k);
        SelectParty(entry, k.LocalCustomerId);

        // Consultancy (998311) @18% ₹10,000 ⇒ CGST 900 / SGST 900; Freight Income (996511) @5% ₹4,000 ⇒ 100 / 100.
        FillLine(entry, k.ConsultancyId, "10000", index: 0);
        FillLine(entry, k.FreightIncomeId, "4000", index: 1);
        Assert.True(entry.Accept());

        var c = k.Vm.Company!;
        var v = PostedSale(c);
        Assert.Equal(1000m, PostedHead(c, v, GstTaxHead.Central));  // 900 + 100, posted
        Assert.Equal(1000m, PostedHead(c, v, GstTaxHead.State));

        var data = VoucherPrintProjector.ProjectInvoice(c, v);

        // BOTH service lines print, each with ITS OWN SAC.
        Assert.Equal(2, data.Items.Count);
        var consult = data.Items.Single(i => i.HsnSac == "998311");
        Assert.Equal(10000m, consult.TaxableValue.Amount);
        Assert.Equal("Consultancy Income", consult.Description);
        var freight = data.Items.Single(i => i.HsnSac == "996511");
        Assert.Equal(4000m, freight.TaxableValue.Amount);
        Assert.Equal("Freight Income", freight.Description);

        // BOTH rate rows print, each carrying ITS OWN posted tax — never a blended rate.
        Assert.Equal(2, data.TaxRows.Count);
        var r5 = data.TaxRows.Single(t => t.RateLabel == "5%");
        Assert.Equal(4000m, r5.TaxableValue.Amount);
        Assert.Equal(100m, r5.Cgst.Amount);
        Assert.Equal(100m, r5.Sgst.Amount);
        var r18 = data.TaxRows.Single(t => t.RateLabel == "18%");
        Assert.Equal(10000m, r18.TaxableValue.Amount);
        Assert.Equal(900m, r18.Cgst.Amount);
        Assert.Equal(900m, r18.Sgst.Amount);

        Assert.Equal(14000m, data.TotalTaxable.Amount);
        Assert.Equal(1000m, data.TotalCgst.Amount);
        Assert.Equal(1000m, data.TotalSgst.Amount);
        Assert.Equal(16000m, data.GrandTotal.Amount);
        Assert.Equal(16000m, PartyLegAmount(c, v));
    }

    // ================================================================ (4) THE GUARD: hand-keyed As-Voucher sale untouched

    /// <summary>
    /// A hand-keyed ledger-only GST sale — plain grid, tax legs typed by hand, and above all <b>never stamped
    /// <c>IsAccountingInvoice</c></b> — must project EXACTLY as before: still NOT a tax invoice, still labelled by its
    /// voucher type, same Dr/Cr rows. The fixture ALSO carries a SAC-bearing sales ledger and the same economics as a
    /// real service invoice, so nothing but the flag separates the two.
    /// <para><b>Bite:</b> dropping the <c>if (!voucher.IsAccountingInvoice) return false;</c> conjunct from
    /// <c>IsServiceAccountingInvoice</c> flips <c>IsTaxInvoice</c> to true here and the label to "Tax Invoice" — every
    /// assertion below fails. That relabelling would hit vouchers the user has ALREADY posted.</para>
    /// </summary>
    [Fact]
    public void HandKeyedAsVoucherSale_printOutput_isUnchanged()
    {
        var k = NewServiceKit("Svc Print HandKeyed Co");
        var c = k.Vm.Company!;
        var service = new LedgerService(c);
        var salesType = c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Sales && t.IsActive);

        // Hand-keyed: the SAME economics as the service invoice above (5,000 + 450 + 450 = 5,900) against a
        // SAC-bearing sales ledger — but the tax legs are PLAIN EntryLines carrying no GstLineTax.
        var v = new Voucher(Guid.NewGuid(), salesType.Id, FyStart.AddDays(10), new[]
        {
            new EntryLine(k.LocalCustomerId, Money.FromRupees(5900m), DrCr.Debit),
            new EntryLine(k.PlainSalesId, Money.FromRupees(5000m), DrCr.Credit),
            new EntryLine(TaxLedgerId(c, GstTaxHead.Central, GstTaxDirection.Output), Money.FromRupees(450m), DrCr.Credit),
            new EntryLine(TaxLedgerId(c, GstTaxHead.State, GstTaxDirection.Output), Money.FromRupees(450m), DrCr.Credit),
        }, partyId: k.LocalCustomerId);
        service.Post(v);
        Assert.False(v.IsAccountingInvoice);            // THE discriminator: it was never billed as a service invoice
        Assert.All(v.Lines, l => Assert.Null(l.Gst));   // (and, as before, no posted GstLineTax anywhere)

        // Unchanged: not a tax invoice, no document label, plain-voucher projection with the posted legs verbatim.
        Assert.False(VoucherPrintProjector.IsTaxInvoice(c, v));
        var detail = new VoucherDetailViewModel(c, v);
        Assert.False(detail.IsTaxInvoice);
        Assert.Equal(string.Empty, detail.DocumentLabel);
        Assert.Equal(PrintPreviewViewModel.PrintKind.Voucher, detail.BuildPrintPreview().Kind);

        var data = VoucherPrintProjector.ProjectVoucher(c, v);
        Assert.Equal("Sales", data.VoucherTypeName);
        Assert.Equal(4, data.Lines.Count);
        Assert.Single(data.Lines, l => l.IsDebit && l.Amount.Amount == 5900m);
        Assert.Single(data.Lines, l => !l.IsDebit && l.Amount.Amount == 5000m);
        Assert.Equal(2, data.Lines.Count(l => !l.IsDebit && l.Amount.Amount == 450m));
    }

    // ================================================================ (5) item invoices print exactly as before

    /// <summary>
    /// An item invoice must keep printing the ITEM projection — Widget, HSN from the stock item, a real Qty and Rate.
    ///
    /// <para><b>FIX-5 — this test did not used to bite its own mutation.</b> The fixture's Sales ledger carried NO
    /// <c>SalesPurchaseGst</c> block, so <c>Gstr1.ServiceLegs</c> found nothing on it and the service branch was
    /// UNREACHABLE in this fixture: deleting the guards from <c>IsServiceAccountingInvoice</c> left all six print
    /// tests green. Real companies DO configure a ledger-level GST block on their sales ledger
    /// (<see cref="NewItemInvoiceKit"/> now does), and with it the service branch is reachable, so the guards are
    /// genuinely exercised.</para>
    ///
    /// <para><b>Bite:</b> gate <c>IsServiceAccountingInvoice</c> on <c>Gstr1.ServiceLegs(...).Any()</c> alone (delete
    /// the <c>HasInventoryLines</c> and <c>IsAccountingInvoice</c> conjuncts) and this item invoice is hijacked to the
    /// service path: it prints <c>desc="Sales" qty="" rate=""</c> instead of the Widget line.</para>
    /// </summary>
    [Fact]
    public void ItemInvoice_printOutput_isUnchanged()
    {
        var vm = NewItemInvoiceKit("Svc Print ItemParity Co", out var widgetId, out var godownId, out var customerId);
        vm.OpenVoucher(VoucherBaseType.Sales);
        var entry = vm.VoucherEntry!;
        entry.ToggleItemInvoice();
        SelectParty(entry, customerId);
        FillItemLine(entry, widgetId, godownId, 10m, "100.00");
        Assert.True(entry.Accept());

        var c = vm.Company!;
        var v = PostedSale(c);
        Assert.True(v.HasInventoryLines);
        Assert.True(VoucherPrintProjector.IsTaxInvoice(c, v));

        // Byte-for-byte the pre-slice item-invoice projection: HSN from the STOCK ITEM, a real Qty and Rate, and the
        // engine's paisa-exact 18% split on ₹1,000.
        var data = VoucherPrintProjector.ProjectInvoice(c, v);
        var row = Assert.Single(data.Items);
        Assert.Equal("Widget", row.Description);
        Assert.Equal("847130", row.HsnSac);
        Assert.Equal("10 Nos", row.QuantityText);
        Assert.Equal("100.00", row.RateText);
        Assert.Equal(1000m, row.TaxableValue.Amount);

        var tr = Assert.Single(data.TaxRows);
        Assert.Equal("18%", tr.RateLabel);
        Assert.Equal(1000m, tr.TaxableValue.Amount);
        Assert.Equal(90m, tr.Cgst.Amount);
        Assert.Equal(90m, tr.Sgst.Amount);
        Assert.False(data.IsInterState);
        Assert.Equal(1000m, data.TotalTaxable.Amount);
        Assert.Equal(90m, data.TotalCgst.Amount);
        Assert.Equal(90m, data.TotalSgst.Amount);
        Assert.Equal(0m, data.TotalIgst.Amount);
        Assert.Equal(1180m, data.GrandTotal.Amount);
    }

    // ================================================================ (6) posted, never recomputed

    /// <summary>
    /// The divergence the conservative ruling exists to prevent: the printed tax is READ off the posted
    /// <c>GstLineTax</c> legs, so changing the service ledger's GST rate master AFTER posting cannot move the printed
    /// figures. A print-time recompute would print 28% tax on an invoice whose GL carries 18% — the document and the
    /// books would disagree.
    /// <para><b>Bite:</b> resolving the rate from the ledger master at print time makes this print 700/700 on a
    /// voucher that posted 450/450.</para>
    /// </summary>
    [Fact]
    public void ServiceInvoice_printedTax_equalsPostedTax_neverRecomputed()
    {
        var k = NewServiceKit("Svc Print NoRecompute Co");
        var entry = OpenAccountingSale(k);
        SelectParty(entry, k.LocalCustomerId);
        FillLine(entry, k.ConsultancyId, "5000");
        Assert.True(entry.Accept());

        var c = k.Vm.Company!;
        var v = PostedSale(c);
        var before = VoucherPrintProjector.ProjectInvoice(c, v);
        Assert.Equal(450m, before.TotalCgst.Amount);
        Assert.Equal(450m, before.TotalSgst.Amount);
        Assert.Equal("18%", Assert.Single(before.TaxRows).RateLabel);

        // Now MUTATE the master: the service is re-rated 18% -> 28% going forward.
        c.FindLedger(k.ConsultancyId)!.SalesPurchaseGst = new StockItemGstDetails
        {
            HsnSac = "998311", Taxability = GstTaxability.Taxable, RateBasisPoints = 2800,
            SupplyType = GstSupplyType.Services,
        };
        _storage.Save(c);

        // The already-posted invoice still prints ITS OWN posted tax — unchanged in every figure.
        var after = VoucherPrintProjector.ProjectInvoice(c, v);
        Assert.Equal(450m, after.TotalCgst.Amount);
        Assert.Equal(450m, after.TotalSgst.Amount);
        Assert.Equal(0m, after.TotalIgst.Amount);
        Assert.Equal(5000m, after.TotalTaxable.Amount);
        Assert.Equal(5900m, after.GrandTotal.Amount);
        var tr = Assert.Single(after.TaxRows);
        Assert.Equal("18%", tr.RateLabel);          // the POSTED slab, not the new master rate
        Assert.Equal(450m, tr.Cgst.Amount);
        Assert.Equal(450m, tr.Sgst.Amount);
        Assert.Equal(5000m, tr.TaxableValue.Amount);
        Assert.Equal("998311", Assert.Single(after.Items).HsnSac);
    }

    // ================================================================ (7) FIX-1: Compensation Cess reaches the bill

    /// <summary>
    /// <b>FIX-1 (high) — the printed Grand Total under-billed the customer by the whole Compensation Cess.</b>
    /// <c>ReadPostedRateGroups</c> correctly ring-fences cess out of the per-RATE rows (a cess leg records the same
    /// group taxable on its own cess-rate key, so it would inject a phantom rate row), but the totals then dropped it
    /// entirely and <c>InvoicePrintData</c> had nowhere to carry it. Measured on this very invoice: printed GrandTotal
    /// 11,800 against a posted party leg of 13,000 — a 1,200 shortfall on the document the customer pays from.
    /// <para><b>Bite:</b> stop carrying <c>TotalCess</c> into the projection (or drop it from
    /// <c>InvoicePrintData.GrandTotal</c>) and the foot-to-the-party-leg assertion fails by exactly the cess.</para>
    /// </summary>
    [Fact]
    public void ServiceInvoice_compensationCess_reachesTheGrandTotal()
    {
        var k = NewServiceKit("Svc Print Cess Co");
        var entry = OpenAccountingSale(k);
        SelectParty(entry, k.LocalCustomerId);

        // Cess Service @18% + 12% cess on ₹10,000 intra ⇒ CGST 900 + SGST 900 + Cess 1,200; customer owes 13,000.
        FillLine(entry, k.CessServiceId, "10000");
        Assert.True(entry.Accept());

        var c = k.Vm.Company!;
        var v = PostedSale(c);
        Assert.Equal(900m, PostedHead(c, v, GstTaxHead.Central));
        Assert.Equal(900m, PostedHead(c, v, GstTaxHead.State));
        Assert.Equal(1200m, PostedHead(c, v, GstTaxHead.Cess));   // the cess really was posted…
        Assert.Equal(13000m, PartyLegAmount(c, v));               // …and really is part of the recorded debt

        var data = VoucherPrintProjector.ProjectInvoice(c, v);

        Assert.Equal(1200m, data.TotalCess.Amount);
        // Ring-fenced: cess is NOT folded into the GST heads, and injects no phantom rate row.
        Assert.Equal(900m, data.TotalCgst.Amount);
        Assert.Equal(900m, data.TotalSgst.Amount);
        Assert.Equal(1800m, data.TotalTax.Amount);
        var tr = Assert.Single(data.TaxRows);
        Assert.Equal("18%", tr.RateLabel);
        Assert.Equal(10000m, tr.TaxableValue.Amount);
        // …but it IS what the customer owes.
        Assert.Equal(13000m, data.GrandTotal.Amount);
        Assert.Equal(PartyLegAmount(c, v), data.GrandTotal.Amount);

        // And the real renderer still produces a real invoice with the extra totals row.
        Assert.NotEmpty(new VoucherDetailViewModel(c, v).BuildPrintPreview().PdfBytes);
    }

    /// <summary>
    /// The SAME defect on the ITEM path, which the HEAD oracle confirmed is identically wrong today: a cess-bearing
    /// item invoice printed 1,180 against a posted party leg of 1,300. The item path recomputes its tax, so the fix is
    /// to hand each line its resolved <c>CessCharge</c> to the engine — exactly what the ACCEPT path does — instead of
    /// feeding rate-aggregated, cess-free lines.
    /// <para><b>Bite:</b> drop the <c>ResolveCess</c> call (pass <c>TaxableLine(value, bp)</c>) and the grand total
    /// falls back to 1,180.</para>
    /// </summary>
    [Fact]
    public void ItemInvoice_compensationCess_reachesTheGrandTotal()
    {
        var vm = NewItemInvoiceKit("Svc Print ItemCess Co", out var widgetId, out var godownId, out var customerId,
            cessBasisPoints: 1200);
        vm.OpenVoucher(VoucherBaseType.Sales);
        var entry = vm.VoucherEntry!;
        entry.ToggleItemInvoice();
        SelectParty(entry, customerId);
        FillItemLine(entry, widgetId, godownId, 10m, "100.00");
        Assert.True(entry.Accept());

        var c = vm.Company!;
        var v = PostedSale(c);
        Assert.Equal(90m, PostedHead(c, v, GstTaxHead.Central));
        Assert.Equal(90m, PostedHead(c, v, GstTaxHead.State));
        Assert.Equal(120m, PostedHead(c, v, GstTaxHead.Cess));
        Assert.Equal(1300m, PartyLegAmount(c, v));

        var data = VoucherPrintProjector.ProjectInvoice(c, v);
        Assert.Equal(120m, data.TotalCess.Amount);
        Assert.Equal(90m, data.TotalCgst.Amount);
        Assert.Equal(90m, data.TotalSgst.Amount);
        Assert.Equal(1000m, data.TotalTaxable.Amount);
        Assert.Equal(1300m, data.GrandTotal.Amount);
        Assert.Equal(PartyLegAmount(c, v), data.GrandTotal.Amount);
        // The item row itself is untouched by the cess fix.
        var row = Assert.Single(data.Items);
        Assert.Equal("Widget", row.Description);
        Assert.Equal("10 Nos", row.QuantityText);
        Assert.Equal("100.00", row.RateText);
    }

    // ================================================================ (8) FIX-3: the document cannot contradict itself

    /// <summary>
    /// <b>FIX-3 (med-high) — editing the party's State after posting made the printed document self-contradictory.</b>
    /// The posted tax is (rightly) never recomputed, so the invoice kept printing intra-state CGST+SGST; but the buyer
    /// block and the Place of Supply were read from the LIVE party master, so the same page then declared an
    /// INTER-state place of supply and a re-stated buyer GSTIN. CGST+SGST asserts the place of supply IS the
    /// supplier's State — no such document is valid.
    /// <para><b>Bite:</b> revert <c>BuyerBlock</c>/<c>PlaceOfSupply</c> to read <c>party.PartyGst.StateCode</c>
    /// directly and this prints "Gujarat (24)" beside CGST+SGST.</para>
    /// </summary>
    [Fact]
    public void ServiceInvoice_partyStateEditedAfterPosting_documentStaysSelfConsistent()
    {
        var k = NewServiceKit("Svc Print StateEdit Co");
        var entry = OpenAccountingSale(k);
        SelectParty(entry, k.LocalCustomerId);           // Maharashtra (27) — the company's own State
        FillLine(entry, k.ConsultancyId, "5000");
        Assert.True(entry.Accept());

        var c = k.Vm.Company!;
        var v = PostedSale(c);
        var before = VoucherPrintProjector.ProjectInvoice(c, v);
        Assert.False(before.IsInterState);
        Assert.Equal("Maharashtra (27)", before.PlaceOfSupply);
        Assert.Equal(GstinMaharashtra, before.Buyer.Gstin);

        // The customer relocates (or the State was simply mis-keyed and is corrected) AFTER the invoice was issued.
        c.FindLedger(k.LocalCustomerId)!.PartyGst = new PartyGstDetails
        { RegistrationType = GstRegistrationType.Regular, Gstin = GstinGujarat, StateCode = "24" };
        _storage.Save(c);

        var after = VoucherPrintProjector.ProjectInvoice(c, v);

        // The posted tax is unchanged — it is history, and it is never recomputed.
        Assert.False(after.IsInterState);
        Assert.Equal(450m, after.TotalCgst.Amount);
        Assert.Equal(450m, after.TotalSgst.Amount);
        Assert.Equal(0m, after.TotalIgst.Amount);

        // …and the buyer block + Place of Supply AGREE with it, instead of contradicting it.
        Assert.Equal("Maharashtra (27)", after.PlaceOfSupply);
        Assert.Equal("Maharashtra (27)", after.Buyer.StateText);
        Assert.NotEqual(GstinGujarat, after.Buyer.Gstin);   // a 24… GSTIN under a 27 Place of Supply is the same lie

        // The invariant, stated once: an intra-state document names the supplier's own State as the place of supply.
        Assert.Equal(after.Seller.StateText, after.PlaceOfSupply);
    }

    // ================================================================ (9) FIX-0: no tax leg is still a tax invoice

    /// <summary>
    /// <b>FIX-0 (user decision) — a ZERO-RATED (LUT/export, 0%) service invoice posts no tax leg at all.</b> Under the
    /// old "has forward GstLineTax legs" inference it printed as a plain accounting voucher: no SAC, no GSTIN blocks,
    /// no document label — though it IS a Rule-46 tax invoice. Keying on the persisted <c>IsAccountingInvoice</c> flag
    /// makes the document type a fact about what the user did, not a guess from the tax.
    /// <para><b>Bite:</b> gate <c>IsServiceAccountingInvoice</c> on <c>GstReportSupport.HasForwardTaxLines</c> again —
    /// this voucher has none, so it reverts to a plain voucher and every assertion below fails.</para>
    /// </summary>
    [Fact]
    public void ZeroRatedServiceInvoice_printsAsTaxInvoice()
    {
        var k = NewServiceKit("Svc Print ZeroRated Co");
        var entry = OpenAccountingSale(k);
        SelectParty(entry, k.LocalCustomerId);
        FillLine(entry, k.ZeroRatedId, "5000");
        Assert.True(entry.Accept());

        var c = k.Vm.Company!;
        var v = PostedSale(c);
        Assert.True(v.IsAccountingInvoice);
        Assert.All(v.Lines, l => Assert.Null(l.Gst));      // NOT ONE posted tax leg …
        Assert.Equal(5000m, PartyLegAmount(c, v));

        Assert.True(VoucherPrintProjector.IsTaxInvoice(c, v));                       // … and still a tax invoice
        Assert.Equal("Tax Invoice", new VoucherDetailViewModel(c, v).DocumentLabel);

        var data = VoucherPrintProjector.ProjectInvoice(c, v);
        var row = Assert.Single(data.Items);
        Assert.Equal("Export Service (LUT)", row.Description);
        Assert.Equal("998313", row.HsnSac);                // the SAC Rule 46(g) requires
        Assert.Equal(5000m, row.TaxableValue.Amount);
        Assert.Empty(data.TaxRows);                        // no rate row — there is no tax
        Assert.Equal(0m, data.TotalTax.Amount);
        Assert.Equal(5000m, data.GrandTotal.Amount);
        Assert.Equal(PartyLegAmount(c, v), data.GrandTotal.Amount);
        Assert.Equal(GstinMaharashtra, data.Seller.Gstin); // the GSTIN blocks Rule 46(a)/(d)/(e) requires
        Assert.Equal(GstinMaharashtra, data.Buyer.Gstin);
        Assert.Equal(PrintPreviewViewModel.PrintKind.Invoice, new VoucherDetailViewModel(c, v).BuildPrintPreview().Kind);
    }

    /// <summary>
    /// The other half of FIX-0: a <b>WHOLLY EXEMPT</b> service invoice also posts no tax leg, and was documented as a
    /// deliberate known limitation of the previous gate ("keeps printing as a plain voucher"). It is a Rule-46
    /// document too, and the persisted flag now says so.
    /// <para><b>Bite:</b> the same one — restore the <c>HasForwardTaxLines</c> gate.</para>
    /// </summary>
    [Fact]
    public void WhollyExemptServiceInvoice_printsAsTaxInvoice()
    {
        var k = NewServiceKit("Svc Print Exempt Co");
        var entry = OpenAccountingSale(k);
        SelectParty(entry, k.LocalCustomerId);
        FillLine(entry, k.ExemptServiceId, "7500");
        Assert.True(entry.Accept());

        var c = k.Vm.Company!;
        var v = PostedSale(c);
        Assert.True(v.IsAccountingInvoice);
        Assert.All(v.Lines, l => Assert.Null(l.Gst));
        Assert.Equal(7500m, PartyLegAmount(c, v));

        Assert.True(VoucherPrintProjector.IsTaxInvoice(c, v));
        var data = VoucherPrintProjector.ProjectInvoice(c, v);
        var row = Assert.Single(data.Items);
        Assert.Equal("Exempt Service", row.Description);
        Assert.Equal("999999", row.HsnSac);
        Assert.Equal(7500m, row.TaxableValue.Amount);
        Assert.Empty(data.TaxRows);
        Assert.Equal(7500m, data.GrandTotal.Amount);
        Assert.Equal(PartyLegAmount(c, v), data.GrandTotal.Amount);
    }

    // ================================================================ (10) FIX-6: a PARTLY exempt service invoice

    /// <summary>
    /// <b>FIX-6 (test adequacy) — the partly-exempt shape was correct but UNGUARDED.</b> A service invoice mixing a
    /// taxed line and an exempt line must print BOTH rows, tax only the taxable one, and still foot to the posted
    /// party leg. Nothing tested it, so a one-word money mutation survived the whole suite.
    /// <para><b>Bite:</b> make <c>ProjectServiceInvoice</c> accumulate <c>totalServiceValue</c> only for taxable legs
    /// (<c>if (!Gstr1.IsNonTaxableServiceLedger(ledger)) totalServiceValue += value;</c>) and the invoice under-bills
    /// by the exempt amount — 11,800 printed against 16,800 posted — with all six original tests still green.</para>
    /// </summary>
    [Fact]
    public void ServiceInvoice_partlyExempt_printsTheExemptLineAndFootsToThePostedPartyLeg()
    {
        var k = NewServiceKit("Svc Print PartExempt Co");
        var entry = OpenAccountingSale(k);
        SelectParty(entry, k.LocalCustomerId);

        // Consultancy @18% ₹10,000 (CGST 900 + SGST 900) + Exempt Service ₹5,000 (untaxed) ⇒ customer owes 16,800.
        FillLine(entry, k.ConsultancyId, "10000", index: 0);
        FillLine(entry, k.ExemptServiceId, "5000", index: 1);
        Assert.True(entry.Accept());

        var c = k.Vm.Company!;
        var v = PostedSale(c);
        Assert.Equal(900m, PostedHead(c, v, GstTaxHead.Central));
        Assert.Equal(900m, PostedHead(c, v, GstTaxHead.State));
        Assert.Equal(16800m, PartyLegAmount(c, v));

        var data = VoucherPrintProjector.ProjectInvoice(c, v);

        // BOTH lines print — the exempt supply is never silently dropped from the document.
        Assert.Equal(2, data.Items.Count);
        Assert.Equal(10000m, data.Items.Single(i => i.HsnSac == "998311").TaxableValue.Amount);
        Assert.Equal(5000m, data.Items.Single(i => i.HsnSac == "999999").TaxableValue.Amount);

        // Only the TAXABLE line carries a rate row, on its own taxable value (never the whole 15,000).
        var tr = Assert.Single(data.TaxRows);
        Assert.Equal("18%", tr.RateLabel);
        Assert.Equal(10000m, tr.TaxableValue.Amount);
        Assert.Equal(900m, tr.Cgst.Amount);
        Assert.Equal(900m, tr.Sgst.Amount);

        // …and the exempt value is still IN the total the customer pays.
        Assert.Equal(15000m, data.TotalTaxable.Amount);
        Assert.Equal(16800m, data.GrandTotal.Amount);
        Assert.Equal(PartyLegAmount(c, v), data.GrandTotal.Amount);
    }

    // ================================================================ (11) F7: the base-type check lives in the gate

    /// <summary>
    /// <b>F7 (small).</b> <c>IsServiceAccountingInvoice</c> is public and <c>ProjectInvoice</c> calls it directly, so
    /// relying on <c>IsTaxInvoice</c> to have checked the base type first left a ledger-only PURCHASE able to divert
    /// into the service projection when <c>ProjectInvoice</c> was called directly — a divergence from HEAD. The check
    /// now lives INSIDE the gate.
    /// <para><b>Bite:</b> delete the base-type conjunct from <c>IsServiceAccountingInvoice</c> and this ledger-only
    /// purchase projects a service row instead of the empty item projection HEAD produced.</para>
    /// </summary>
    [Fact]
    public void LedgerOnlyPurchase_neverDivertsIntoTheServiceProjection()
    {
        var k = NewServiceKit("Svc Print PurchaseGuard Co");
        var c = k.Vm.Company!;
        var purchaseType = c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Purchase && t.IsActive);

        // A ledger-only PURCHASE against a SAC-bearing expense ledger — and stamped with the accounting-invoice flag,
        // which the purchase side of the screen cannot do today. The base-type check is what keeps it out regardless.
        var v = new Voucher(Guid.NewGuid(), purchaseType.Id, FyStart.AddDays(12), new[]
        {
            new EntryLine(k.ProfessionalFeesId, Money.FromRupees(5000m), DrCr.Debit),
            new EntryLine(k.SupplierId, Money.FromRupees(5000m), DrCr.Credit),
        }, partyId: k.SupplierId, isAccountingInvoice: true);
        new LedgerService(c).Post(v);

        Assert.False(VoucherPrintProjector.IsServiceAccountingInvoice(c, v));
        Assert.False(VoucherPrintProjector.IsTaxInvoice(c, v));
        // HEAD's behaviour for a direct ProjectInvoice call on a ledger-only purchase: the (empty) item projection.
        Assert.Empty(VoucherPrintProjector.ProjectInvoice(c, v).Items);
        // …and it still prints as the plain Dr/Cr voucher it is.
        Assert.Equal(PrintPreviewViewModel.PrintKind.Voucher, new VoucherDetailViewModel(c, v).BuildPrintPreview().Kind);
    }

    // ---------------------------------------------------------------- scaffolding

    private sealed class Kit
    {
        public required MainWindowViewModel Vm { get; init; }
        public required Guid ConsultancyId { get; init; }    // Income, taxable @18% (SAC 998311)
        public required Guid FreightIncomeId { get; init; }  // Income, taxable @5%  (SAC 996511)
        public required Guid PlainSalesId { get; init; }     // Income, taxable @18% — the hand-keyed sale's value ledger
        public required Guid CessServiceId { get; init; }    // Income, taxable @18% + 12% ad-valorem Compensation Cess
        public required Guid ExemptServiceId { get; init; }  // Income, EXEMPT (SAC 999999) — no tax leg at all
        public required Guid ZeroRatedId { get; init; }      // Income, taxable @0% (LUT/export) — no tax leg at all
        public required Guid ProfessionalFeesId { get; init; } // Expense, taxable @18% (SAC 998311) — purchase-side leg
        public required Guid SupplierId { get; init; }       // Sundry Creditors, in-state
        public required Guid LocalCustomerId { get; init; }  // in-state (27), registered
        public required Guid InterCustomerId { get; init; }  // Gujarat (24), inter-state
    }

    /// <summary>A GST-enabled (home Maharashtra 27) company with SAC-bearing service-income ledgers + two parties.</summary>
    private Kit NewServiceKit(string companyName)
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

        var consultancy = AddLedger(c, "Consultancy Income", "Sales Accounts");
        consultancy.SalesPurchaseGst = new StockItemGstDetails
        {
            HsnSac = "998311", Taxability = GstTaxability.Taxable, RateBasisPoints = 1800,
            SupplyType = GstSupplyType.Services,
        };
        var freight = AddLedger(c, "Freight Income", "Direct Incomes");
        freight.SalesPurchaseGst = new StockItemGstDetails
        {
            HsnSac = "996511", Taxability = GstTaxability.Taxable, RateBasisPoints = 500,
            SupplyType = GstSupplyType.Services,
        };
        var plainSales = AddLedger(c, "Sales @18%", "Sales Accounts");
        plainSales.SalesPurchaseGst = new StockItemGstDetails
        {
            HsnSac = "998314", Taxability = GstTaxability.Taxable, RateBasisPoints = 1800,
            SupplyType = GstSupplyType.Services,
        };

        // A cess-bearing service: 18% GST + a 12% ad-valorem Compensation Cess declared on the ledger itself.
        var cessService = AddLedger(c, "Cess Service", "Sales Accounts");
        cessService.SalesPurchaseGst = new StockItemGstDetails
        {
            HsnSac = "998399", Taxability = GstTaxability.Taxable, RateBasisPoints = 1800,
            SupplyType = GstSupplyType.Services,
            CessApplicable = true, CessValuationMode = CessValuationMode.AdValorem, CessRateBasisPoints = 1200,
        };
        // A wholly EXEMPT service and a ZERO-RATED (LUT/export) one: neither posts a single tax leg, and both are
        // still Rule-46 tax invoices.
        var exemptService = AddLedger(c, "Exempt Service", "Sales Accounts");
        exemptService.SalesPurchaseGst = new StockItemGstDetails
        {
            HsnSac = "999999", Taxability = GstTaxability.Exempt, SupplyType = GstSupplyType.Services,
        };
        var zeroRated = AddLedger(c, "Export Service (LUT)", "Sales Accounts");
        zeroRated.SalesPurchaseGst = new StockItemGstDetails
        {
            HsnSac = "998313", Taxability = GstTaxability.Taxable, RateBasisPoints = 0,
            SupplyType = GstSupplyType.Services,
        };
        // Purchase side: an expense ledger carrying a SAC block + a creditor, for the base-type guard (F7).
        var professionalFees = AddLedger(c, "Professional Fees", "Indirect Expenses");
        professionalFees.SalesPurchaseGst = new StockItemGstDetails
        {
            HsnSac = "998311", Taxability = GstTaxability.Taxable, RateBasisPoints = 1800,
            SupplyType = GstSupplyType.Services,
        };
        var supplier = AddLedger(c, "Local Supplier", "Sundry Creditors");
        supplier.PartyGst = new PartyGstDetails
        { RegistrationType = GstRegistrationType.Regular, Gstin = GstinMaharashtra, StateCode = "27" };

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
            FreightIncomeId = freight.Id,
            PlainSalesId = plainSales.Id,
            CessServiceId = cessService.Id,
            ExemptServiceId = exemptService.Id,
            ZeroRatedId = zeroRated.Id,
            ProfessionalFeesId = professionalFees.Id,
            SupplierId = supplier.Id,
            LocalCustomerId = localCustomer.Id,
            InterCustomerId = interCustomer.Id,
        };
    }

    /// <summary>A GST-enabled company with a stock item + a sales ledger, for the item-invoice parity guard.
    /// <paramref name="cessBasisPoints"/> optionally declares an ad-valorem Compensation Cess on the item (null ⇒ no
    /// cess at all, i.e. the original fixture, byte-identical).</summary>
    private MainWindowViewModel NewItemInvoiceKit(
        string companyName, out Guid widgetId, out Guid godownId, out Guid customerId, int? cessBasisPoints = null)
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

        var inv = new InventoryService(c);
        var grp = inv.CreateStockGroup("Goods");
        var nos = inv.CreateSimpleUnit("Nos", "Numbers", unitQuantityCode: "NOS");
        var main = c.MainLocation!.Id;
        var widget = inv.CreateStockItem("Widget", grp.Id, nos.Id);
        widget.Gst = new StockItemGstDetails
        {
            HsnSac = "847130", Taxability = GstTaxability.Taxable, RateBasisPoints = 1800,
            CessApplicable = cessBasisPoints is not null,
            CessValuationMode = cessBasisPoints is null ? null : CessValuationMode.AdValorem,
            CessRateBasisPoints = cessBasisPoints,
        };
        inv.AddOpeningBalance(widget.Id, main, 200m, Money.FromRupees(100m));

        // FIX-5: the sales VALUE ledger carries a ledger-level GST block, as a real company's does. Without it
        // Gstr1.ServiceLegs finds no leg here and the service branch of the projector is unreachable in this fixture —
        // which is exactly why this parity test used to survive deleting the guards that protect it. With it, the
        // guards genuinely bite. Declared as GOODS (SupplyType default), which is what a goods sales ledger is.
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

    private static void FillItemLine(VoucherEntryViewModel entry, Guid itemId, Guid godownId, decimal qty, string rate, int index = 0)
    {
        while (entry.InventoryLines.Count <= index) entry.AddInventoryLine();
        var line = entry.InventoryLines[index];
        line.SelectedItem = entry.StockItems.Single(i => i.Id == itemId);
        line.SelectedGodown = entry.Godowns.Single(g => g.Id == godownId);
        line.QuantityText = qty.ToString(System.Globalization.CultureInfo.InvariantCulture);
        line.RateText = rate;
    }

    private static Voucher PostedSale(Company c) =>
        c.Vouchers.Single(v => c.FindVoucherType(v.TypeId)!.BaseType == VoucherBaseType.Sales);

    private static Guid TaxLedgerId(Company c, GstTaxHead head, GstTaxDirection direction) =>
        new GstService(c).FindTaxLedger(head, direction)!.Id;

    /// <summary>The voucher's POSTED total under one GST head, read off its <c>GstLineTax</c> legs (never recomputed).</summary>
    private static decimal PostedHead(Company c, Voucher v, GstTaxHead head)
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
