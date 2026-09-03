using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Io;
using Apex.Ledger.Reports;
using Apex.Ledger.Services;
using Apex.Desktop.Services;
using Apex.Desktop.ViewModels;
using Xunit;
using DomainLedger = Apex.Ledger.Domain.Ledger;

namespace Apex.Desktop.Tests;

/// <summary>
/// <b>T0-11 slice S2 — a PURCHASE item invoice prints as a recipient-side RECORD, with its item detail.</b>
///
/// <para><b>The defect these tests exist for.</b> A Purchase item invoice printed as a plain Dr/Cr voucher with
/// <b>zero item detail</b>: <c>GstReportSupport.IsTaxInvoice</c> is Sales-only — correctly, because CGST Act §31(1)
/// attaches the duty to "a registered person <b>supplying</b>" — and the print routing used that ENTITLEMENT
/// predicate to answer the unrelated RENDERING question. <c>ProjectVoucher</c> loops over <c>voucher.Lines</c> and
/// never reads <c>voucher.InventoryLines</c> at all, so the goods simply did not appear.</para>
///
/// <para><b>🔴 WHERE EVERY EXPECTED VALUE BELOW COMES FROM.</b> Requirement <b>RQ-11a</b>
/// (<c>docs/phase5-reports-io-requirements.md</c>), amended and added by slice S0 <i>before</i> a line of this code
/// was written, and the statute it cites — CGST Act §31(1)/(2), CGST Rules 46 and 49. Not one expectation here is
/// read off the projector: the money literals are computed from the fixture's own arithmetic, and the strings are
/// the ones RQ-11a names. That ordering is the whole discipline — this project's documented failure mode is a
/// golden edited to match the code.</para>
///
/// <para><b>Ruling 9 — the title is OURS.</b> The Apex corpus names no title for a purchase print, shows no
/// specimen and evidences no law-driven title derivation; <c>PURCHASE RECORD</c>, the record legend and the
/// supplier-tax caption are a documented divergence and can never join the corpus-verified set.</para>
///
/// <para>Money is odd to the paisa throughout: a round figure passes under a rounding defect and asserts
/// nothing.</para>
/// </summary>
public sealed class PurchaseRecordPrintTests
{
    private const string OurGstin = "27AAPFU0939F1ZV";        // Maharashtra (27) — the company
    private const string SupplierGstin = "24AAACC1206D1ZM";   // Gujarat (24)     — the supplier
    private static readonly DateOnly FyStart = new(2025, 4, 1);
    private static readonly DateOnly DocDate = new(2025, 4, 10);

    // ---------------------------------------------------------------- the fixture arithmetic, stated once
    //
    // Three stock lines, deliberately odd to the paisa. Every literal below is DERIVED here and nowhere else; the
    // tests assert against these, never against what the projector returns.
    private const decimal Rate1 = 12_345.67m; private const decimal Qty1 = 2m;
    private const decimal Rate2 = 8_901.23m; private const decimal Qty2 = 3m;
    private const decimal Rate3 = 45_678.91m; private const decimal Qty3 = 1m;
    private const decimal Line1 = Rate1 * Qty1;   // 24,691.34
    private const decimal Line2 = Rate2 * Qty2;   // 26,703.69
    private const decimal Line3 = Rate3 * Qty3;   // 45,678.91
    private const decimal Goods = Line1 + Line2 + Line3;   // 97,073.94
    /// <summary>IGST at 18% on the goods value: 97,073.94 x 0.18 = 17,473.3092, to the paisa 17,473.31.</summary>
    private const decimal Igst = 17_473.31m;
    /// <summary>What we owe the supplier — the posted CREDIT leg on his account. 97,073.94 + 17,473.31.</summary>
    private const decimal SupplierLeg = Goods + Igst;       // 1,14,547.25

    private const string SupplierDocNumber = "GJ/2025-26/0417";

    private sealed class Fx
    {
        public required Company Company { get; init; }
        public required Guid GodownId { get; init; }
        public required Guid TaxableItemId { get; init; }
        public required Guid ExemptItemId { get; init; }
        public required Guid PurchaseLedgerId { get; init; }
        public required Guid SupplierId { get; init; }
        public Guid PurchaseTypeId => Company.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Purchase).Id;
    }

    private static DomainLedger Add(Company c, string name, string groupName, bool openingIsDebit)
    {
        var l = new DomainLedger(Guid.NewGuid(), name, c.FindGroupByName(groupName)!.Id, Money.Zero, openingIsDebit);
        c.AddLedger(l);
        return l;
    }

    private static Fx Build()
    {
        var c = CompanyFactory.CreateSeeded("Apex Record Fixture", FyStart);
        // A captured supplier address, so OUR block on the record is a complete Rule 46(a)-shaped block and the
        // party-swap assertions are about real content rather than about two empty boxes.
        c.Address = "12 Marine Lines\nMumbai";
        c.Pin = "400020";
        new GstService(c).EnableGst(new GstConfig
        {
            HomeStateCode = "27",
            Gstin = OurGstin,
            RegistrationType = GstRegistrationType.Regular,
            ApplicableFrom = FyStart,
            Periodicity = GstReturnPeriodicity.Monthly,
        });

        var inv = new InventoryService(c);
        var grp = inv.CreateStockGroup("Goods");
        var nos = inv.CreateSimpleUnit("Nos", "Numbers", unitQuantityCode: "NOS");

        var taxable = inv.CreateStockItem("Widget", grp.Id, nos.Id);
        taxable.Gst = new StockItemGstDetails
        { HsnSac = "847130", Taxability = GstTaxability.Taxable, RateBasisPoints = 1800 };
        var exempt = inv.CreateStockItem("Fresh Milk", grp.Id, nos.Id);
        exempt.Gst = new StockItemGstDetails { HsnSac = "040110", Taxability = GstTaxability.Exempt };

        var purchases = Add(c, "Purchases", "Purchase Accounts", true);

        var supplier = Add(c, "Gujarat Supplier", "Sundry Creditors", false);
        supplier.PartyGst = new PartyGstDetails
        { RegistrationType = GstRegistrationType.Regular, Gstin = SupplierGstin, StateCode = "24" };
        supplier.Mailing = new PartyMailingDetails { Address = "9 GIDC Estate\nSurat", Pincode = "395003" };

        return new Fx
        {
            Company = c,
            GodownId = c.MainLocation!.Id,
            TaxableItemId = taxable.Id,
            ExemptItemId = exempt.Id,
            PurchaseLedgerId = purchases.Id,
            SupplierId = supplier.Id,
        };
    }

    private static Guid InputHeadId(Company c, GstTaxHead head) =>
        c.Ledgers.Single(l => l.GstClassification is
        { Direction: GstTaxDirection.Input, IsReverseCharge: false } g && g.TaxHead == head).Id;

    /// <summary>The shape the whole slice is about: an INTER-state purchase item invoice from a registered supplier,
    /// carrying the Input IGST he charged us as a tagged, posted leg.</summary>
    private static Voucher TaxedPurchase(Fx f, int number = 1, bool untaggedTax = false)
    {
        var inputIgst = InputHeadId(f.Company, GstTaxHead.Integrated);
        return new Voucher(Guid.NewGuid(), f.PurchaseTypeId, DocDate, new List<EntryLine>
        {
            new(f.PurchaseLedgerId, new Money(Goods), DrCr.Debit),
            untaggedTax
                ? new EntryLine(inputIgst, new Money(Igst), DrCr.Debit)
                : new EntryLine(inputIgst, new Money(Igst), DrCr.Debit,
                    gst: new GstLineTax(GstTaxHead.Integrated, 1800, new Money(Goods))),
            new(f.SupplierId, new Money(SupplierLeg), DrCr.Credit),
        }, number: number, partyId: f.SupplierId,
            referenceNo: SupplierDocNumber, referenceDate: new DateOnly(2025, 4, 8),
            inventoryLines: new[]
            {
                // The fourth argument is the per-unit RATE; the line's Value is quantity x rate.
                new VoucherInventoryLine(f.TaxableItemId, f.GodownId, Qty1, new Money(Rate1)),
                new VoucherInventoryLine(f.TaxableItemId, f.GodownId, Qty2, new Money(Rate2)),
                new VoucherInventoryLine(f.TaxableItemId, f.GodownId, Qty3, new Money(Rate3)),
            });
    }

    /// <summary>A wholly EXEMPT purchase — the hazard shape. It posts no tax leg at all, which is exactly the
    /// condition under which the naive "widen the Sales gate" fix would have titled it BILL OF SUPPLY.</summary>
    private static Voucher ExemptPurchase(Fx f, int number = 2) =>
        new(Guid.NewGuid(), f.PurchaseTypeId, DocDate, new List<EntryLine>
        {
            new(f.PurchaseLedgerId, new Money(Goods), DrCr.Debit),
            new(f.SupplierId, new Money(Goods), DrCr.Credit),
        }, number: number, partyId: f.SupplierId, inventoryLines: new[]
        {
            new VoucherInventoryLine(f.ExemptItemId, f.GodownId, 1m, new Money(Goods)),
        });

    /// <summary>The rendered PDF as text. Latin-1, the same decode every other print test in this repository
    /// uses — the PDF content streams are ASCII-safe by construction (<c>ReportPrintProjector.Ascii</c>).</summary>
    private static string PdfText(byte[] bytes) => Encoding.Latin1.GetString(bytes);

    private static InvoicePrintData Project(Fx f, Voucher v) =>
        VoucherPrintProjector.ProjectInvoice(f.Company, v);

    // ================================================================ THE DEFECT

    /// <summary>
    /// <b>THE user-visible defect.</b> RQ-11a: a purchase item invoice "SHALL carry the same item detail RQ-11(c)
    /// requires — serial, description, HSN/SAC, quantity + unit, rate, discount, taxable value". Today it prints the
    /// plain Dr/Cr voucher, whose only loop is over <c>voucher.Lines</c>.
    ///
    /// <para>Every figure is derived from the fixture's own arithmetic at the top of this file — the three line
    /// values, their sum, the 18% IGST computed by hand, and the supplier's credit leg — never read back from the
    /// projector. ER-4: the document must tie to the posted voucher to the paisa.</para>
    /// </summary>
    [Fact]
    public void A_purchase_item_invoice_prints_its_item_detail_and_foots_to_the_supplier_leg()
    {
        var f = Build();
        var v = TaxedPurchase(f);

        // The routing question, at the real user path (P / Ctrl+P).
        Assert.Equal(PrintPreviewViewModel.PrintKind.Invoice,
            new VoucherDetailViewModel(f.Company, v).BuildPrintPreview().Kind);

        var data = Project(f, v);

        Assert.Equal(3, data.Items.Count);
        Assert.Equal(v.InventoryLines.Count, data.Items.Count);
        Assert.Equal(new[] { Line1, Line2, Line3 }, data.Items.Select(i => i.TaxableValue.Amount).ToArray());
        Assert.All(data.Items, i => Assert.Equal("847130", i.HsnSac));
        Assert.All(data.Items, i => Assert.Contains("Nos", i.QuantityText));

        // ER-4 to the paisa, against the fixture's arithmetic and the posted supplier leg.
        Assert.Equal(Goods, data.TotalTaxable.Amount);
        Assert.Equal(Igst, data.TotalIgst.Amount);
        Assert.Equal(SupplierLeg, data.GrandTotal.Amount);
        Assert.Equal(SupplierLeg,
            v.Lines.Single(l => l.LedgerId == f.SupplierId && l.Side == DrCr.Credit).Amount.Amount);

        // …and the goods actually reach the page.
        var text = PdfText(new VoucherDetailViewModel(f.Company, v).BuildPrintPreview().PdfBytes);
        Assert.Contains("Widget", text);
        Assert.Contains("847130", text);
    }

    // ================================================================ WHAT IT MAY AND MAY NOT BE CALLED

    /// <summary>
    /// RQ-11a: the record "SHALL NOT be titled <i>Tax Invoice</i>, NOR <i>Bill of Supply</i>". CGST Act §31(1) puts
    /// the tax invoice on the person <b>supplying</b>; CGST Rule 49 opens "a bill of supply … issued by the
    /// SUPPLIER" — so widening either outward title to an inward supply swaps one false statement for another.
    /// The title it does bear, <c>PURCHASE RECORD</c>, is OURS (ruling 9).
    /// </summary>
    [Fact]
    public void A_purchase_record_bears_neither_outward_title()
    {
        var f = Build();
        var v = TaxedPurchase(f);

        var doc = GstReportSupport.ClassifyPrintedDocument(f.Company, v);
        Assert.Equal(DocumentRole.Recorded, doc.Role);
        Assert.Equal(GstReportSupport.PurchaseRecordTitle, doc.Title);
        Assert.Equal("PURCHASE RECORD", doc.Title);
        Assert.Equal("Purchase Record", doc.ScreenLabel);

        var data = Project(f, v);
        Assert.Equal(GstReportSupport.PurchaseRecordTitle, data.DocumentTitle);
        Assert.False(data.IsBillOfSupply);
        Assert.True(data.IsRecipientRecord);

        var text = PdfText(new VoucherDetailViewModel(f.Company, v).BuildPrintPreview().PdfBytes);
        Assert.Contains("PURCHASE RECORD", text);
        Assert.DoesNotContain("TAX INVOICE", text);
        Assert.DoesNotContain("BILL OF SUPPLY", text);

        // The badge over the drill reads the same decision the paper does.
        Assert.Equal("Purchase Record", new VoucherDetailViewModel(f.Company, v).DocumentLabel);
    }

    /// <summary>
    /// <b>The hazard the census never saw.</b> A WHOLLY EXEMPT purchase posts no forward tax at all, so it is the
    /// one purchase shape that would fall through <c>IsBillOfSupply</c>'s exempt limb under the naive fix (widen
    /// <c>IsTaxInvoice</c>'s Sales gate) and be titled BILL OF SUPPLY — a document CGST Rule 49 says is issued by
    /// the supplier, on the strength of a predicate that also feeds the NIC e-Way <c>docType</c>.
    /// </summary>
    [Fact]
    public void A_wholly_exempt_purchase_is_never_titled_BILL_OF_SUPPLY()
    {
        var f = Build();
        var v = ExemptPurchase(f);

        var doc = GstReportSupport.ClassifyPrintedDocument(f.Company, v);
        Assert.Equal(DocumentRole.Recorded, doc.Role);
        Assert.Equal(GstReportSupport.PurchaseRecordTitle, doc.Title);
        Assert.NotEqual(GstReportSupport.BillOfSupplyTitle, doc.Title);

        var text = PdfText(new VoucherDetailViewModel(f.Company, v).BuildPrintPreview().PdfBytes);
        Assert.DoesNotContain("BILL OF SUPPLY", text);
        Assert.Contains("PURCHASE RECORD", text);

        // …and the entitlement predicates are unmoved, which is what keeps the filed docType unmoved.
        Assert.False(GstReportSupport.IsTaxInvoice(f.Company, v));
        Assert.False(GstReportSupport.IsBillOfSupply(f.Company, v));
    }

    // ================================================================ THE THIRD AXIS — WHOSE DOCUMENT IS THIS

    /// <summary>
    /// RQ-11a: the record "SHALL be headed by the SUPPLIER's identity — supplier name / address / GSTIN in the
    /// supplier block and OUR name / address / GSTIN in the recipient block". CGST Rule 46(a) puts "name, address
    /// and GSTIN of the supplier" at the head of the document. This is the exact REVERSE of every document this app
    /// has ever printed, and getting it wrong prints our GSTIN as the supplier's on someone else's document.
    /// </summary>
    [Fact]
    public void A_purchase_record_is_headed_by_the_supplier_and_names_us_as_the_recipient()
    {
        var f = Build();
        var v = TaxedPurchase(f);
        var data = Project(f, v);

        Assert.Equal(PartyOrientation.WeAreRecipient,
            GstReportSupport.ClassifyPrintedDocument(f.Company, v).Heads);

        Assert.Equal("Gujarat Supplier", data.Seller.Name);
        Assert.Equal(SupplierGstin, data.Seller.Gstin);
        Assert.Contains("Surat", data.Seller.AddressLines);

        Assert.Equal("Apex Record Fixture", data.Buyer.Name);
        Assert.Equal(OurGstin, data.Buyer.Gstin);
        Assert.Contains("Mumbai", data.Buyer.AddressLines);
    }

    // ================================================================ WHAT WE MAY NOT STATE ON SOMEONE ELSE'S DOC

    /// <summary>
    /// RQ-11a: the record "SHALL suppress every particular that is the supplier's to state: place of supply, our
    /// declaration and our signature" (CGST Rule 46 — the place-of-supply, delivery-address, reverse-charge and
    /// signature particulars are all SUPPLIER particulars).
    ///
    /// <para>The signature is the sharpest of the three. After the party blocks are swapped, the shipped renderer's
    /// "For {Seller.Name} / Authorised Signatory" block would print the SUPPLIER's name over a signature line on a
    /// document we produced — not a mislabel but a forged attestation.</para>
    /// </summary>
    [Fact]
    public void A_purchase_record_states_no_place_of_supply_no_declaration_and_no_signature()
    {
        var f = Build();
        var v = TaxedPurchase(f);
        var data = Project(f, v);

        Assert.False(GstReportSupport.ClassifyPrintedDocument(f.Company, v).StatesOurDeclarationAndSignature);
        Assert.Equal(string.Empty, data.PlaceOfSupply);
        Assert.Equal(string.Empty, data.TopDeclaration);

        var text = PdfText(new VoucherDetailViewModel(f.Company, v).BuildPrintPreview().PdfBytes);
        Assert.DoesNotContain("Place of Supply", text);
        Assert.DoesNotContain("We declare that this", text);
        Assert.DoesNotContain("Authorised Signatory", text);
        Assert.DoesNotContain("For Gujarat Supplier", text);
    }

    // ================================================================ THE NUMBERS — OBJ-3

    /// <summary>
    /// RQ-11a, stated as a prohibition because it is a truth condition and not a label choice: "OUR voucher number
    /// SHALL carry a caption of its own reading <i>Our Record Ref.</i> and SHALL NEVER appear under a caption
    /// reading <i>Invoice No.</i> — under the supplier's identity that caption is a FALSE STATEMENT". The supplier's
    /// own number rides the existing reference pair, whose helper already returns "Supplier Invoice No." for a
    /// Purchase, so no money field is added anywhere.
    /// </summary>
    [Fact]
    public void Our_number_is_captioned_as_our_record_ref_and_the_suppliers_number_keeps_its_own_caption()
    {
        var f = Build();
        var v = TaxedPurchase(f, number: 42);
        var data = Project(f, v);

        Assert.Equal("Supplier Invoice No.", data.ReferenceCaption);
        Assert.Equal(SupplierDocNumber, data.ReferenceNo);
        Assert.Equal("08-04-2025", data.ReferenceDateText);

        var ourNumber = f.Company.FormatVoucherNumber(v);
        Assert.Equal(ourNumber, data.InvoiceNumber);

        var text = PdfText(new VoucherDetailViewModel(f.Company, v).BuildPrintPreview().PdfBytes);
        Assert.Contains(GstReportSupport.RecordNumberCaption + ": " + ourNumber, text);
        Assert.DoesNotContain("Invoice No: " + ourNumber, text);
        Assert.Contains("Supplier Invoice No.: " + SupplierDocNumber, text);
    }

    // ================================================================ THE TAX — WHOSE CHARGE IS IT

    /// <summary>
    /// The record substantiates the input tax credit we claim, so a purchase record with no tax cannot do its job —
    /// but the tax it states is the tax the SUPPLIER charged us, and saying otherwise on a document headed by his
    /// identity is a false statutory statement. The figures need no projector change (the posted-money readers filter
    /// on the tax metadata and never on direction, and the engine stamps that metadata on Input legs too); the
    /// binding requirement is the CAPTION.
    /// </summary>
    [Fact]
    public void A_purchase_record_states_the_tax_the_supplier_charged_captioned_as_his()
    {
        var f = Build();
        var v = TaxedPurchase(f);
        var data = Project(f, v);

        Assert.Equal(TaxParticulars.AsChargedByTheSupplier,
            GstReportSupport.ClassifyPrintedDocument(f.Company, v).StatesTax);

        var row = Assert.Single(data.TaxRows);
        Assert.Equal("18%", row.RateLabel);
        Assert.Equal(Goods, row.TaxableValue.Amount);
        Assert.Equal(Igst, row.Igst.Amount);
        Assert.Equal(Igst, data.TotalIgst.Amount);

        var text = PdfText(new VoucherDetailViewModel(f.Company, v).BuildPrintPreview().PdfBytes);
        Assert.Contains(GstReportSupport.SupplierTaxCaption, text);
        Assert.Equal("Tax Charged by the Supplier", GstReportSupport.SupplierTaxCaption);
        Assert.DoesNotContain("GST Breakup", text);
    }

    /// <summary>
    /// The inward mirror of the guard that keeps an outward invoice honest: every rupee of GST the general ledger
    /// carries must be VISIBLE to the printer. The record derives 100% of its tax from the posted tax metadata, so a
    /// purchase whose Input tax legs carry none would print a Grand Total short of the posted supplier leg by the
    /// whole tax. The conservative direction is the one the outward side already takes — it is not a record document
    /// at all, and prints as the plain Dr/Cr voucher, which states every posted leg exactly.
    /// </summary>
    [Fact]
    public void A_purchase_whose_input_tax_legs_carry_no_metadata_prints_as_the_plain_voucher()
    {
        var f = Build();
        var v = TaxedPurchase(f, untaggedTax: true);

        var doc = GstReportSupport.ClassifyPrintedDocument(f.Company, v);
        Assert.Equal(DocumentRole.NoStatutoryDocument, doc.Role);
        Assert.False(doc.RendersItemDetail);
        Assert.Equal(PrintPreviewViewModel.PrintKind.Voucher,
            new VoucherDetailViewModel(f.Company, v).BuildPrintPreview().Kind);
    }

    // ================================================================ THE RENDERER'S SAFETY PROPERTY

    /// <summary>
    /// <b>The safety property of this whole slice, asserted at the renderer rather than at the projector.</b>
    /// <c>InvoicePdf</c> refuses, case-insensitively, to print "TAX INVOICE" on a document that is not one — a rule
    /// that exists because a caller who suppresses every tax particular and then titles the page TAX INVOICE
    /// produces exactly the document the law forbids, from the safety net itself. Widening the title branch to
    /// accept the record titles must not weaken it, and the F12 title override must not re-title a record either:
    /// a print knob may not promote a document we did not issue into one we did.
    /// </summary>
    [Fact]
    public void The_renderer_refuses_both_outward_titles_on_a_recipient_record()
    {
        var page = new PageConfig();

        foreach (var attempt in new[] { "TAX INVOICE", "Tax Invoice", "  tax invoice  ", "BILL OF SUPPLY", "", null })
        {
            var data = new InvoicePrintData
            {
                IsRecipientRecord = true,
                DocumentTitle = attempt!,
                Seller = new InvoicePartyBlock { Name = "Gujarat Supplier" },
                Buyer = new InvoicePartyBlock { Name = "Apex Record Fixture" },
                InvoiceNumber = "1",
                InvoiceDateText = "10-04-2025",
                TotalTaxable = new Money(Goods),
            };
            var text = PdfText(InvoicePdf.Render(data, new PrintConfig(), page));
            Assert.Contains("PURCHASE RECORD", text);
            Assert.DoesNotContain("TAX INVOICE", text);
            Assert.DoesNotContain("BILL OF SUPPLY", text);
        }

        // …and the F12 override cannot promote it either.
        var overridden = new InvoicePrintData
        {
            IsRecipientRecord = true,
            DocumentTitle = GstReportSupport.PurchaseRecordTitle,
            Seller = new InvoicePartyBlock { Name = "Gujarat Supplier" },
            Buyer = new InvoicePartyBlock { Name = "Apex Record Fixture" },
            InvoiceNumber = "1",
            InvoiceDateText = "10-04-2025",
            TotalTaxable = new Money(Goods),
        };
        var overriddenText = PdfText(
            InvoicePdf.Render(overridden, new PrintConfig { TitleOverride = "TAX INVOICE" }, page));
        Assert.Contains("PURCHASE RECORD", overriddenText);
        Assert.DoesNotContain("TAX INVOICE", overriddenText);
    }

    // ================================================================ SCREEN AND PAPER

    /// <summary>
    /// The on-screen mirror is what the operator approves; the bytes are what leaves the building. They are
    /// re-derived from the SAME classification, and this asserts they say the same thing — the FIX-W1e failure class,
    /// where a drill badged one document sat above a declaration belonging to another.
    /// </summary>
    [Fact]
    public void The_screen_mirror_names_the_same_document_the_paper_does()
    {
        var f = Build();
        var v = TaxedPurchase(f, number: 42);
        var preview = new VoucherDetailViewModel(f.Company, v).BuildPrintPreview();

        var ourNumber = f.Company.FormatVoucherNumber(v);
        Assert.Equal("Purchase Record No. " + ourNumber, preview.ReportTitle);

        var page = preview.Pages[0];
        Assert.Equal(GstReportSupport.PurchaseRecordTitle, page.Title);
        Assert.Equal("Gujarat Supplier", page.Subtitle);

        var lines = string.Join("\n", page.Lines.SelectMany(l => l.Cells));
        Assert.Contains(GstReportSupport.RecordNumberCaption + " " + ourNumber, lines);
        Assert.Contains("Supplier: Gujarat Supplier", lines);
        // The binding caption reaches the screen too. The mirror has no per-rate breakup table, so it cannot carry
        // it where the PDF does; without it the operator approves "IGST 17,473.31" with nothing saying whose charge
        // that is — screen and paper would then differ on the one claim RQ-11a makes binding about a record's tax.
        Assert.Contains(GstReportSupport.SupplierTaxCaption, lines);
        Assert.DoesNotContain("Place of Supply", lines);
        Assert.DoesNotContain("Invoice No.", lines);
    }

    // ================================================================ THE ROUTE'S CRASH GUARD (design R5)

    /// <summary>
    /// <c>ProjectInvoice</c> opens with a structural refusal for the §10 contradiction, and a Purchase now reaches
    /// that line for the first time. It cannot fire — the refusal's predicate requires a composition OUTWARD supply
    /// and is Sales-scoped — but "cannot fire" is a property of a limb someone may later edit, so it is pinned here
    /// rather than reasoned about in a comment.
    /// </summary>
    [Fact]
    public void The_composition_refusal_cannot_fire_on_a_purchase()
    {
        var f = Build();
        f.Company.Gst!.RegistrationType = GstRegistrationType.Composition;
        f.Company.Gst!.CompositionSubType = CompositionSubType.Trader;

        var v = TaxedPurchase(f);
        Assert.False(GstReportSupport.IsCompositionSupplyCarryingForwardTax(f.Company, v));
        var data = Project(f, v);   // must not throw
        Assert.Equal(GstReportSupport.PurchaseRecordTitle, data.DocumentTitle);
    }
}
