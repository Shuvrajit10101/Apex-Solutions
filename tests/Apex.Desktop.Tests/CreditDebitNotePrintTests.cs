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
/// <b>T0-11 slice S4 — CREDIT and DEBIT NOTES print as §34 / Rule-53 note documents, at VALUE level.</b>
///
/// <para><b>🔴 WHERE EVERY EXPECTED VALUE BELOW COMES FROM.</b> Requirement <b>RQ-11b</b>
/// (<c>docs/phase5-reports-io-requirements.md</c>), written by slice S0 <b>before</b> any of this code existed:
/// <i>"For a Credit Note or a Debit Note the system SHALL render a note document carrying, at value level: the
/// nature of the document, the serial number and date of the corresponding tax invoice, and the value of the
/// taxable supply, the rate of tax, and the amount credited or debited — projected from the voucher's accounting
/// lines and the persisted original-invoice link, with no HSN, no quantity and no per-item table required."</i>
/// The statute is CGST Act §34(1)/(3)/(4) and CGST <b>Rule 53</b>.</para>
///
/// <para><b>⚠️ CITATION LIMIT — no clause letter appears in this file, deliberately.</b> The SUBSTANCE of the
/// Rule 53 particulars is verified at primary source; the CLAUSE LETTERING is unreached
/// (<c>taxinformation.cbic.gov.in</c> fails TLS chain verification and the Part-A rules PDF 404s), so it is written
/// into no test name, no assertion and no comment. This project has already had to strip mis-attributed statutory
/// citations out of shipped code once.</para>
///
/// <para><b>ENTITLEMENT IS NOT THE BASE TYPE OF THE NOTE.</b> §34 puts the note on "the registered person who has
/// SUPPLIED", so the discriminator is the ORIGINAL voucher's base type, resolved through the persisted link — and it
/// is THREE-valued, not two. A note whose discriminator is ABSENT (a consolidated-party reference, which
/// <c>GstCreditDebitNoteLink</c> accepts by design under ER-12) is titled NOTHING and prints as the plain Dr/Cr
/// voucher; guessing "recorded" there would title OUR OWN §34(1) credit note as our customer's document, which is
/// strictly worse than today's untitled fallback.</para>
///
/// <para><b>NO DEPENDENCY ON CENSUS T0-10.</b> A note cannot carry inventory lines at all — pinned below — so the
/// value-level shape IS the statutory minimum and is fully reachable today.</para>
///
/// <para><b>Ruling 9 — every title here is OURS.</b> The corpus names no title for a note and evidences no
/// law-driven title derivation.</para>
/// </summary>
public sealed class CreditDebitNotePrintTests
{
    private const string OurGstin = "27AAPFU0939F1ZV";         // Maharashtra (27) — the company
    private const string CustomerGstin = "27AACCC1206D1ZQ";    // Maharashtra (27) — intra-state ⇒ CGST+SGST
    private const string SupplierGstin = "24AAACC1206D1ZM";    // Gujarat (24)     — inter-state ⇒ IGST
    private static readonly DateOnly FyStart = new(2025, 4, 1);
    private static readonly DateOnly InvoiceDate = new(2025, 4, 10);
    private static readonly DateOnly NoteDate = new(2025, 5, 20);

    // ---------------------------------------------------------------- the fixture arithmetic, stated once
    //
    // Every literal is derived HERE by hand and nowhere else. Odd to the paisa throughout.

    /// <summary>Sales-return CREDIT NOTE, intra-state: the value of the supply credited back.</summary>
    private const decimal CnValue = 8_765.43m;
    /// <summary>9% of 8,765.43 = 788.8887, to the paisa 788.89. CGST and SGST each.</summary>
    private const decimal CnCgst = 788.89m;
    /// <summary>What the customer's account is CREDITED: 8,765.43 + 788.89 + 788.89.</summary>
    private const decimal CnPartyLeg = CnValue + CnCgst + CnCgst;    // 10,343.21

    /// <summary>Purchase-return DEBIT NOTE, inter-state: the value of the inward supply returned.</summary>
    private const decimal PrValue = 6_543.21m;
    /// <summary>18% of 6,543.21 = 1,177.7778, to the paisa 1,177.78.</summary>
    private const decimal PrIgst = 1_177.78m;
    /// <summary>What the supplier's account is DEBITED: 6,543.21 + 1,177.78.</summary>
    private const decimal PrPartyLeg = PrValue + PrIgst;             // 7,720.99

    /// <summary>Sale-side upward-revision DEBIT NOTE, intra-state: the extra value charged on our own sale.</summary>
    private const decimal DnValue = 4_321.09m;
    /// <summary>9% of 4,321.09 = 388.8981, to the paisa 388.90. CGST and SGST each.</summary>
    private const decimal DnCgst = 388.90m;
    /// <summary>What the customer's account is DEBITED: 4,321.09 + 388.90 + 388.90.</summary>
    private const decimal DnPartyLeg = DnValue + DnCgst + DnCgst;    // 5,098.89

    private sealed class Fx
    {
        public required Company Company { get; init; }
        public required Guid SalesId { get; init; }
        public required Guid SalesReturnsId { get; init; }
        public required Guid PurchasesId { get; init; }
        public required Guid PurchaseReturnsId { get; init; }
        public required Guid CustomerId { get; init; }
        public required Guid SupplierId { get; init; }
        public Guid TypeOf(VoucherBaseType b) => Company.VoucherTypes.First(t => t.BaseType == b && t.IsActive).Id;
    }

    private static DomainLedger Add(Company c, string name, string groupName, bool openingIsDebit)
    {
        var l = new DomainLedger(Guid.NewGuid(), name, c.FindGroupByName(groupName)!.Id, Money.Zero, openingIsDebit);
        c.AddLedger(l);
        return l;
    }

    private static Fx Build()
    {
        var c = CompanyFactory.CreateSeeded("Apex Note Fixture", FyStart);
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

        var sales = c.FindLedgerByName("Sales") ?? Add(c, "Sales", "Sales Accounts", false);
        var salesReturns = Add(c, "Sales Returns", "Sales Accounts", true);
        var purchases = Add(c, "Purchases", "Purchase Accounts", true);
        var purchaseReturns = Add(c, "Purchase Returns", "Purchase Accounts", false);

        var customer = Add(c, "Mumbai Traders", "Sundry Debtors", true);
        customer.PartyGst = new PartyGstDetails
        { RegistrationType = GstRegistrationType.Regular, Gstin = CustomerGstin, StateCode = "27" };
        customer.Mailing = new PartyMailingDetails { Address = "44 Fort Street\nMumbai", Pincode = "400001" };

        var supplier = Add(c, "Gujarat Supplier", "Sundry Creditors", false);
        supplier.PartyGst = new PartyGstDetails
        { RegistrationType = GstRegistrationType.Regular, Gstin = SupplierGstin, StateCode = "24" };
        supplier.Mailing = new PartyMailingDetails { Address = "9 GIDC Estate\nSurat", Pincode = "395003" };

        return new Fx
        {
            Company = c,
            SalesId = sales.Id,
            SalesReturnsId = salesReturns.Id,
            PurchasesId = purchases.Id,
            PurchaseReturnsId = purchaseReturns.Id,
            CustomerId = customer.Id,
            SupplierId = supplier.Id,
        };
    }

    private static Guid HeadId(Company c, GstTaxDirection direction, GstTaxHead head) =>
        c.Ledgers.Single(l => l.GstClassification is { IsReverseCharge: false } g
            && g.Direction == direction && g.TaxHead == head).Id;

    // ---------------------------------------------------------------- the originals a note can adjust

    /// <summary>An ORIGINAL outward supply — the document a §34(1) credit note or a §34(3) debit note of ours
    /// adjusts. Its base type is what makes us the supplier, and therefore the issuer.</summary>
    private static Voucher PostOriginalSale(Fx f, decimal value = 1_00_000m)
    {
        var v = new Voucher(Guid.NewGuid(), f.TypeOf(VoucherBaseType.Sales), InvoiceDate, new List<EntryLine>
        {
            new(f.CustomerId, new Money(value), DrCr.Debit),
            new(f.SalesId, new Money(value), DrCr.Credit),
        }, partyId: f.CustomerId);
        return new LedgerService(f.Company).Post(v);
    }

    /// <summary>An ORIGINAL inward supply. A note adjusting it is a document our SUPPLIER issues; ours records it.</summary>
    private static Voucher PostOriginalPurchase(Fx f, decimal value = 80_000m)
    {
        var v = new Voucher(Guid.NewGuid(), f.TypeOf(VoucherBaseType.Purchase), InvoiceDate, new List<EntryLine>
        {
            new(f.PurchasesId, new Money(value), DrCr.Debit),
            new(f.SupplierId, new Money(value), DrCr.Credit),
        }, partyId: f.SupplierId);
        return new LedgerService(f.Company).Post(v);
    }

    /// <summary>Registers the persisted §34 link the classifier resolves entitlement through.</summary>
    private static GstCreditDebitNoteLink Link(
        Fx f, Voucher note, CdnType type, Voucher? original, string? denormalisedNumber = null)
    {
        var link = new GstCreditDebitNoteLink(
            Guid.NewGuid(), note.Id, type,
            original?.Id,
            original is null ? denormalisedNumber : f.Company.FormatVoucherNumber(original),
            original?.Date ?? InvoiceDate,
            type == CdnType.Credit ? "01 Sales return" : "01 Purchase return");
        f.Company.AddCreditDebitNoteLink(link);
        return link;
    }

    // ---------------------------------------------------------------- the three note shapes

    /// <summary>A SALES-RETURN credit note: Dr Sales Returns + Dr Output CGST/SGST (the reducing side), Cr Customer.</summary>
    private static Voucher PostSalesReturnCreditNote(Fx f, bool untaggedTax = false)
    {
        var cgst = HeadId(f.Company, GstTaxDirection.Output, GstTaxHead.Central);
        var sgst = HeadId(f.Company, GstTaxDirection.Output, GstTaxHead.State);
        var v = new Voucher(Guid.NewGuid(), f.TypeOf(VoucherBaseType.CreditNote), NoteDate, new List<EntryLine>
        {
            new(f.SalesReturnsId, new Money(CnValue), DrCr.Debit),
            untaggedTax
                ? new EntryLine(cgst, new Money(CnCgst), DrCr.Debit)
                : new EntryLine(cgst, new Money(CnCgst), DrCr.Debit,
                    gst: new GstLineTax(GstTaxHead.Central, 900, new Money(CnValue))),
            untaggedTax
                ? new EntryLine(sgst, new Money(CnCgst), DrCr.Debit)
                : new EntryLine(sgst, new Money(CnCgst), DrCr.Debit,
                    gst: new GstLineTax(GstTaxHead.State, 900, new Money(CnValue))),
            new(f.CustomerId, new Money(CnPartyLeg), DrCr.Credit),
        }, partyId: f.CustomerId);
        return new LedgerService(f.Company).Post(v);
    }

    /// <summary>A PURCHASE-RETURN debit note: Dr Supplier, Cr Purchase Returns + Cr Input IGST (the reversal).</summary>
    private static Voucher PostPurchaseReturnDebitNote(Fx f)
    {
        var igst = HeadId(f.Company, GstTaxDirection.Input, GstTaxHead.Integrated);
        var v = new Voucher(Guid.NewGuid(), f.TypeOf(VoucherBaseType.DebitNote), NoteDate, new List<EntryLine>
        {
            new(f.SupplierId, new Money(PrPartyLeg), DrCr.Debit),
            new(f.PurchaseReturnsId, new Money(PrValue), DrCr.Credit),
            new EntryLine(igst, new Money(PrIgst), DrCr.Credit,
                gst: new GstLineTax(GstTaxHead.Integrated, 1800, new Money(PrValue))),
        }, partyId: f.SupplierId);
        return new LedgerService(f.Company).Post(v);
    }

    /// <summary>A SALE-SIDE UPWARD-REVISION debit note — the same base type as the purchase return, the OPPOSITE
    /// entitlement: Dr Customer, Cr Sales + Cr Output CGST/SGST (the increasing side).</summary>
    private static Voucher PostSaleRevisionDebitNote(Fx f)
    {
        var cgst = HeadId(f.Company, GstTaxDirection.Output, GstTaxHead.Central);
        var sgst = HeadId(f.Company, GstTaxDirection.Output, GstTaxHead.State);
        var v = new Voucher(Guid.NewGuid(), f.TypeOf(VoucherBaseType.DebitNote), NoteDate, new List<EntryLine>
        {
            new(f.CustomerId, new Money(DnPartyLeg), DrCr.Debit),
            new(f.SalesId, new Money(DnValue), DrCr.Credit),
            new EntryLine(cgst, new Money(DnCgst), DrCr.Credit,
                gst: new GstLineTax(GstTaxHead.Central, 900, new Money(DnValue))),
            new EntryLine(sgst, new Money(DnCgst), DrCr.Credit,
                gst: new GstLineTax(GstTaxHead.State, 900, new Money(DnValue))),
        }, partyId: f.CustomerId);
        return new LedgerService(f.Company).Post(v);
    }

    private static string PdfText(byte[] bytes) => Encoding.Latin1.GetString(bytes);

    // ================================================================ the corresponding invoice serial and date

    /// <summary>
    /// RQ-11b: the note SHALL carry "the serial number and date of the corresponding tax invoice", and the reference
    /// caption SHALL read <b>"Original Invoice No."</b> — today it reads "Reference No.", because the caption helper
    /// is Purchase-vs-everything-else. Both the caption AND the value come from the persisted §34 link, never from
    /// the voucher's own counterparty-reference field: captioning THAT "Original Invoice No." would swap one wrong
    /// label for a new false statement.
    /// </summary>
    [Fact]
    public void A_credit_note_states_the_corresponding_invoice_serial_and_date()
    {
        var f = Build();
        var original = PostOriginalSale(f);
        var note = PostSalesReturnCreditNote(f);
        var link = Link(f, note, CdnType.Credit, original);

        var data = VoucherPrintProjector.ProjectInvoice(f.Company, note);

        Assert.Equal(GstReportSupport.OriginalInvoiceCaption, data.ReferenceCaption);
        Assert.Equal("Original Invoice No.", data.ReferenceCaption);
        Assert.Equal(link.OriginalInvoiceNumber, data.ReferenceNo);
        Assert.Equal(f.Company.FormatVoucherNumber(original), data.ReferenceNo);
        Assert.Equal("10-04-2025", data.ReferenceDateText);

        var text = PdfText(new VoucherDetailViewModel(f.Company, note).BuildPrintPreview().PdfBytes);
        Assert.Contains("Original Invoice No.: " + link.OriginalInvoiceNumber, text);
    }

    // ================================================================ value, rate and amount — with NO item lines

    /// <summary>
    /// RQ-11b: the note states "the value of the taxable supply, the rate of tax, and the amount credited or
    /// debited … with no HSN, no quantity and no per-item table required". This is the test that PROVES the T0-10
    /// decoupling — and it asserts the empty item table as CORRECT <b>for a stated statutory reason</b>, which is the
    /// only thing that distinguishes it from a golden edited to match the code.
    /// <para>Every figure is derived from this file's own arithmetic: 8,765.43 credited back, 9% of it twice to the
    /// paisa (788.89 each), and a customer credit of 10,343.21.</para>
    /// </summary>
    [Fact]
    public void A_credit_note_states_value_rate_and_amount_credited_without_requiring_item_lines()
    {
        var f = Build();
        var note = PostSalesReturnCreditNote(f);
        Link(f, note, CdnType.Credit, PostOriginalSale(f));

        Assert.Equal(PrintPreviewViewModel.PrintKind.Invoice,
            new VoucherDetailViewModel(f.Company, note).BuildPrintPreview().Kind);

        var data = VoucherPrintProjector.ProjectInvoice(f.Company, note);

        // No per-item detail is required, and the note carries none: a §34 note cannot hold inventory lines at all.
        Assert.Empty(data.Items);
        Assert.Empty(note.InventoryLines);

        // The value of the taxable supply.
        Assert.Equal(CnValue, data.TotalTaxable.Amount);
        // The RATE, and the amount credited under it.
        var row = Assert.Single(data.TaxRows);
        Assert.Equal("18%", row.RateLabel);
        Assert.Equal(CnValue, row.TaxableValue.Amount);
        Assert.Equal(CnCgst, row.Cgst.Amount);
        Assert.Equal(CnCgst, row.Sgst.Amount);
        Assert.Equal(CnCgst, data.TotalCgst.Amount);
        Assert.Equal(CnCgst, data.TotalSgst.Amount);
        Assert.Equal(0m, data.TotalIgst.Amount);
        // …and the whole of it ties to what the customer's account actually moved by (ER-4).
        Assert.Equal(CnPartyLeg, data.GrandTotal.Amount);
        Assert.Equal(CnPartyLeg,
            note.Lines.Single(l => l.LedgerId == f.CustomerId && l.Side == DrCr.Credit).Amount.Amount);
    }

    /// <summary>
    /// The premise RQ-11b rests on, pinned rather than asserted in prose: a §34 note <b>cannot</b> carry inventory
    /// lines, so the value-level shape above IS the statutory minimum and nothing here waits on census T0-10.
    /// </summary>
    [Fact]
    public void A_note_cannot_carry_inventory_lines_at_all()
    {
        var f = Build();
        var ex = Assert.ThrowsAny<Exception>(() => new LedgerService(f.Company).Post(new Voucher(
            Guid.NewGuid(), f.TypeOf(VoucherBaseType.CreditNote), NoteDate, new List<EntryLine>
            {
                new(f.SalesReturnsId, new Money(CnValue), DrCr.Debit),
                new(f.CustomerId, new Money(CnValue), DrCr.Credit),
            }, partyId: f.CustomerId, inventoryLines: new[]
            {
                new VoucherInventoryLine(Guid.NewGuid(), Guid.NewGuid(), 1m, new Money(CnValue)),
            })));
        Assert.Contains("Item-invoice stock lines are only valid on a Purchase or Sales voucher", ex.Message);
    }

    // ================================================================ THE BIDIRECTIONAL RULING — a matched pair

    /// <summary>
    /// <b>Half one of the pair.</b> §34 puts the note on "the registered person who has SUPPLIED". A debit note
    /// raised for a PURCHASE RETURN adjusts a supply made TO us, so the statutory document is our supplier's credit
    /// note and ours is only a RECORD of it — it may not be titled DEBIT NOTE.
    /// <para>Same base type as the other half, OPPOSITE entitlement, discriminated only by the original voucher's
    /// base type behind the persisted link. A single-direction test would pass under an implementation that keyed on
    /// the note's own base type, which is exactly the wrong rule.</para>
    /// </summary>
    [Fact]
    public void A_purchase_return_debit_note_is_NOT_titled_DEBIT_NOTE()
    {
        var f = Build();
        var note = PostPurchaseReturnDebitNote(f);
        Link(f, note, CdnType.Debit, PostOriginalPurchase(f));

        var doc = GstReportSupport.ClassifyPrintedDocument(f.Company, note);
        Assert.Equal(DocumentRole.Recorded, doc.Role);
        Assert.Equal(GstReportSupport.PurchaseReturnRecordTitle, doc.Title);
        Assert.Equal("PURCHASE RETURN RECORD", doc.Title);
        Assert.NotEqual(GstReportSupport.DebitNoteTitle, doc.Title);
        Assert.Equal(PartyOrientation.WeAreRecipient, doc.Heads);
        Assert.False(doc.StatesOurDeclarationAndSignature);

        var data = VoucherPrintProjector.ProjectInvoice(f.Company, note);
        Assert.True(data.IsRecipientRecord);
        Assert.Equal(SupplierGstin, data.Seller.Gstin);   // the supplier heads his own document
        Assert.Equal(OurGstin, data.Buyer.Gstin);
        Assert.Equal(PrValue, data.TotalTaxable.Amount);
        Assert.Equal(PrIgst, data.TotalIgst.Amount);
        Assert.Equal(PrPartyLeg, data.GrandTotal.Amount);

        var text = PdfText(new VoucherDetailViewModel(f.Company, note).BuildPrintPreview().PdfBytes);
        Assert.Contains("PURCHASE RETURN RECORD", text);
        Assert.DoesNotContain("TAX INVOICE", text);
    }

    /// <summary>
    /// <b>Half two of the pair.</b> A debit note raised for an UPWARD REVISION of our OWN sale adjusts a supply made
    /// BY us, so §34(3) obliges us to issue it and it IS titled DEBIT NOTE — the same base type as the half above,
    /// resolved the other way by the original voucher alone.
    /// </summary>
    [Fact]
    public void A_sale_side_upward_revision_debit_note_IS_titled_DEBIT_NOTE()
    {
        var f = Build();
        var note = PostSaleRevisionDebitNote(f);
        Link(f, note, CdnType.Debit, PostOriginalSale(f));

        var doc = GstReportSupport.ClassifyPrintedDocument(f.Company, note);
        Assert.Equal(DocumentRole.Issued, doc.Role);
        Assert.Equal(GstReportSupport.DebitNoteTitle, doc.Title);
        Assert.Equal("DEBIT NOTE", doc.Title);
        Assert.Equal(PartyOrientation.WeAreSupplier, doc.Heads);
        Assert.True(doc.StatesOurDeclarationAndSignature);

        var data = VoucherPrintProjector.ProjectInvoice(f.Company, note);
        Assert.False(data.IsRecipientRecord);
        Assert.Equal(OurGstin, data.Seller.Gstin);        // we head our own document
        Assert.Equal(CustomerGstin, data.Buyer.Gstin);
        Assert.Equal(DnValue, data.TotalTaxable.Amount);
        Assert.Equal(DnCgst, data.TotalCgst.Amount);
        Assert.Equal(DnCgst, data.TotalSgst.Amount);
        Assert.Equal(DnPartyLeg, data.GrandTotal.Amount);

        Assert.Equal("Debit Note", new VoucherDetailViewModel(f.Company, note).DocumentLabel);
    }

    /// <summary>A sales-return CREDIT note is the §34(1) document we are entitled to issue, and is titled so.</summary>
    [Fact]
    public void A_sales_return_credit_note_IS_titled_CREDIT_NOTE()
    {
        var f = Build();
        var note = PostSalesReturnCreditNote(f);
        Link(f, note, CdnType.Credit, PostOriginalSale(f));

        var doc = GstReportSupport.ClassifyPrintedDocument(f.Company, note);
        Assert.Equal(DocumentRole.Issued, doc.Role);
        Assert.Equal(GstReportSupport.CreditNoteTitle, doc.Title);
        Assert.Equal("CREDIT NOTE", doc.Title);
        Assert.Equal("Credit Note", new VoucherDetailViewModel(f.Company, note).DocumentLabel);

        var text = PdfText(new VoucherDetailViewModel(f.Company, note).BuildPrintPreview().PdfBytes);
        Assert.Contains("CREDIT NOTE", text);
        Assert.DoesNotContain("TAX INVOICE", text);
        Assert.DoesNotContain("PURCHASE RETURN RECORD", text);
    }

    // ================================================================ 🔴 THE THIRD VALUE — DISCRIMINATOR ABSENT

    /// <summary>
    /// <b>🔴 The shape a two-valued rule gets WRONG, and gets wrong in the worst direction.</b>
    /// <c>GstCreditDebitNoteLink</c> documents a null <c>OriginalInvoiceVoucherId</c> as a consolidated-party
    /// reference and its constructor explicitly ACCEPTS null given a denormalised original-invoice number (ER-12).
    /// So a consolidated-party sales-return credit note — an ordinary, valid, supported shape the entry screen offers
    /// through its "Consolidated…" option — carries NO discriminator at all.
    ///
    /// <para>A two-valued rule ("Sales ⇒ issued, otherwise recorded") would title OUR OWN §34(1) credit note
    /// <c>PURCHASE RETURN RECORD</c> — our customer's document, headed by our customer's identity, with our
    /// signature suppressed. That is strictly WORSE than today's untitled fallback. So the third value is a REFUSAL:
    /// no title at all, and the plain Dr/Cr voucher, which states every posted leg exactly.</para>
    /// </summary>
    [Fact]
    public void A_consolidated_party_credit_note_is_not_titled_as_a_purchase_return()
    {
        var f = Build();
        var note = PostSalesReturnCreditNote(f);
        var link = Link(f, note, CdnType.Credit, original: null, denormalisedNumber: "INV-1042");

        // The shape is exactly the one ER-12 supports: no voucher link, a denormalised number instead.
        Assert.Null(link.OriginalInvoiceVoucherId);
        Assert.Equal("INV-1042", link.OriginalInvoiceNumber);

        var doc = GstReportSupport.ClassifyPrintedDocument(f.Company, note);
        Assert.Equal(DocumentRole.NoStatutoryDocument, doc.Role);
        Assert.Equal(string.Empty, doc.Title);
        Assert.NotEqual(GstReportSupport.PurchaseReturnRecordTitle, doc.Title);
        Assert.Equal(PartyOrientation.WeAreSupplier, doc.Heads);   // never headed by the counterparty on a guess
        Assert.False(doc.RendersItemDetail);

        Assert.Equal(PrintPreviewViewModel.PrintKind.Voucher,
            new VoucherDetailViewModel(f.Company, note).BuildPrintPreview().Kind);
        Assert.Equal(string.Empty, new VoucherDetailViewModel(f.Company, note).DocumentLabel);

        var text = PdfText(new VoucherDetailViewModel(f.Company, note).BuildPrintPreview().PdfBytes);
        Assert.DoesNotContain("PURCHASE RETURN RECORD", text);
        Assert.DoesNotContain("CREDIT NOTE", text);
    }

    /// <summary>
    /// A note carrying no §34 link at all is the OTHER absent-discriminator shape, and it is the common one: the §34
    /// details are opt-in, so an ordinary inter-branch or exempt-supply note creates no link. It is titled nothing.
    /// </summary>
    [Fact]
    public void An_unlinked_note_is_titled_nothing_and_prints_the_plain_voucher()
    {
        var f = Build();
        var note = PostSalesReturnCreditNote(f);   // no Link(...) call

        Assert.Null(GstReportSupport.CdnLinkFor(f.Company, note));
        Assert.Equal(DocumentRole.NoStatutoryDocument,
            GstReportSupport.ClassifyPrintedDocument(f.Company, note).Role);
        Assert.Equal(PrintPreviewViewModel.PrintKind.Voucher,
            new VoucherDetailViewModel(f.Company, note).BuildPrintPreview().Kind);
    }

    // ================================================================ THE CONSERVATIVE DIRECTION

    /// <summary>
    /// <b>A note whose posted tax the printer cannot see is not a Rule-53 document.</b> The note derives its rate and
    /// its tax amounts from <c>GstLineTax</c> metadata, because that is the only source of a RATE that is not
    /// invented at print time — and the rate is a mandatory particular. With the tax legs untagged the projection
    /// would state 8,765.43 against a posted customer credit of 10,343.21, i.e. a note crediting the customer with
    /// less than his account actually moved by. So it is refused and prints as the plain Dr/Cr voucher.
    ///
    /// <para><b>▶ 🔴 THIS IS ALSO A REACHABILITY LIMIT, RECORDED RATHER THAN LEFT SILENT.</b> The shipped §34 entry
    /// path is the plain Dr/Cr grid, on which the operator types the tax legs by hand and no path stamps
    /// <c>GstLineTax</c> on them (<c>VoucherEntryViewModel.RegisterSection34Link</c> adds the link and nothing else).
    /// So today the Rule-53 document is reached by a note posted through the engine or import, and by a note bearing
    /// no tax at all; a hand-typed TAXED note still prints the plain voucher. Stamping the note's tax legs is a
    /// separate change to the entry screen, and inventing the rate here instead would put a figure on a statutory
    /// document that no posted leg supports.</para>
    /// </summary>
    [Fact]
    public void A_note_whose_posted_tax_is_untagged_prints_the_plain_voucher()
    {
        var f = Build();
        var note = PostSalesReturnCreditNote(f, untaggedTax: true);
        Link(f, note, CdnType.Credit, PostOriginalSale(f));

        Assert.Equal(DocumentRole.NoStatutoryDocument,
            GstReportSupport.ClassifyPrintedDocument(f.Company, note).Role);
        Assert.Equal(PrintPreviewViewModel.PrintKind.Voucher,
            new VoucherDetailViewModel(f.Company, note).BuildPrintPreview().Kind);
    }

    // ================================================================ THE TITLE IS NOT A PRINT PREFERENCE

    /// <summary>
    /// <b>The F12 title override may not re-title a §34 note.</b> The override exists so an operator can print e.g.
    /// "PROFORMA INVOICE" over a tax invoice. The <b>nature of the document</b> is a mandatory Rule-53 particular and
    /// a consequence of the transaction, not a print preference — and re-titling a credit note "TAX INVOICE" through
    /// a print knob would state, on paper, that we supplied something we did not. This is the same structural rule
    /// the bill of supply and the recipient-side record already carry, applied to the third document class.
    /// <para>Asserted on BOTH surfaces: the on-screen mirror the operator approves and the bytes that leave the
    /// printer. A fix applied to one alone is the preview/paper drift this project has had to close twice.</para>
    /// </summary>
    [Fact]
    public void The_title_override_cannot_re_title_a_credit_note()
    {
        var f = Build();
        var note = PostSalesReturnCreditNote(f);
        Link(f, note, CdnType.Credit, PostOriginalSale(f));

        var preview = new VoucherDetailViewModel(f.Company, note).BuildPrintPreview();
        preview.TitleOverride = "TAX INVOICE";

        var text = PdfText(preview.PdfBytes);
        Assert.Contains("CREDIT NOTE", text);
        Assert.DoesNotContain("TAX INVOICE", text);
        Assert.Equal(GstReportSupport.CreditNoteTitle, preview.Pages[0].Title);
    }

    // ================================================================ THE FILING FREEZE (Decision 1)

    /// <summary>
    /// The predicates this slice does not move. <c>IsTaxInvoice</c> gates <c>IsBillOfSupply</c>'s exempt limb, which
    /// feeds the NIC e-Way Part-A <c>docType</c>; both stay FALSE for every note shape, issued or recorded. The note
    /// document is produced one level up, by the classifier.
    /// </summary>
    [Fact]
    public void The_outward_predicates_stay_false_for_every_note_shape()
    {
        var f = Build();
        var cn = PostSalesReturnCreditNote(f);
        Link(f, cn, CdnType.Credit, PostOriginalSale(f));
        var pr = PostPurchaseReturnDebitNote(f);
        Link(f, pr, CdnType.Debit, PostOriginalPurchase(f));
        var dn = PostSaleRevisionDebitNote(f);
        Link(f, dn, CdnType.Debit, PostOriginalSale(f));

        foreach (var v in new[] { cn, pr, dn })
        {
            Assert.False(GstReportSupport.IsTaxInvoice(f.Company, v));
            Assert.False(GstReportSupport.IsBillOfSupply(f.Company, v));
            Assert.False(GstReportSupport.IsServiceAccountingInvoice(f.Company, v));
            Assert.False(v.HasInventoryLines);
        }
    }
}
