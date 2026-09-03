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
/// <b>T0-11 slice S3 — the LEDGER-ONLY (accounting / service) PURCHASE prints as a recipient-side RECORD.</b>
///
/// <para><b>The defect these tests exist for, and why it is a second slice rather than part of S2.</b> Slice S2
/// closed the ITEM purchase invoice by widening the classifier alone; the ledger-only purchase takes the OTHER
/// projection pass (<c>ProjectServiceInvoice</c>), which is reached through a different, Sales-only gate
/// (<c>GstReportSupport.IsServiceAccountingInvoice</c>). So a purchase Accounting Invoice — a shape the shipped
/// screen has posted since <c>CanBeAccountingInvoice</c> was widened to Purchase — still printed the plain Dr/Cr
/// voucher: no supplier block, no SAC lines, no tax detail, nothing an auditor could tie the input tax credit to.
/// </para>
///
/// <para><b>🔴 WHERE EVERY EXPECTED VALUE BELOW COMES FROM.</b> Requirement <b>RQ-11a</b>
/// (<c>docs/phase5-reports-io-requirements.md</c>), whose scope phrase is
/// <i>"For a PURCHASE item-invoice or purchase accounting-(service)-invoice the system SHALL render the voucher as
/// a RECORD of the supplier's document, not as a document of ours"</i> — written by slice S0 <b>before</b> a line
/// of this code existed — and the statute it cites: CGST Act §31(1)/(2) ("a registered person <b>supplying</b>"),
/// CGST Rules 46 and 49. Not one expectation here is read off the projector: every money literal is computed from
/// the fixture's own arithmetic at the top of this file, and every string is one RQ-11a names.</para>
///
/// <para><b>Ruling 9 — the title is OURS.</b> The corpus names no title for a purchase print and evidences no
/// law-driven title derivation; <c>PURCHASE RECORD</c> is a documented divergence.</para>
///
/// <para>Money is odd to the paisa throughout: a round figure passes under a rounding defect and asserts
/// nothing.</para>
/// </summary>
public sealed class PurchaseServiceRecordPrintTests
{
    private const string OurGstin = "27AAPFU0939F1ZV";        // Maharashtra (27) — the company
    private const string SupplierGstin = "24AAACC1206D1ZM";   // Gujarat (24)     — the supplier
    private static readonly DateOnly FyStart = new(2025, 4, 1);
    private static readonly DateOnly DocDate = new(2025, 4, 12);

    // ---------------------------------------------------------------- the fixture arithmetic, stated once
    //
    // Two service legs on an INTER-state inward supply (home 27 vs supplier 24 ⇒ IGST), deliberately odd to the
    // paisa. Every literal below is derived HERE and nowhere else.
    /// <summary>Professional Fees — SAC 998311, taxable at 18%.</summary>
    private const decimal TaxableLeg = 34_567.89m;
    /// <summary>Statutory Filing Charges — SAC 999999, EXEMPT. It carries value but bears no tax.</summary>
    private const decimal ExemptLeg = 12_345.67m;
    /// <summary>The value of the inward supply: 34,567.89 + 12,345.67.</summary>
    private const decimal ServiceValue = TaxableLeg + ExemptLeg;      // 46,913.56
    /// <summary>IGST at 18% on the TAXABLE leg alone: 34,567.89 x 0.18 = 6,222.2202, to the paisa 6,222.22.</summary>
    private const decimal Igst = 6_222.22m;
    /// <summary>What we owe the supplier — the posted CREDIT leg on his account. 46,913.56 + 6,222.22.</summary>
    private const decimal SupplierLeg = ServiceValue + Igst;          // 53,135.78

    private const string SupplierDocNumber = "GJ/SVC/2025-26/0091";

    private sealed class Fx
    {
        public required Company Company { get; init; }
        public required Guid ProfessionalFeesId { get; init; }   // Expense, taxable 18%, SAC 998311
        public required Guid FilingChargesId { get; init; }      // Expense, EXEMPT, SAC 999999
        public required Guid ConsultancyIncomeId { get; init; }  // Income, taxable 18%, SAC 998311 (the Sales mirror)
        public required Guid SupplierId { get; init; }
        public required Guid CustomerId { get; init; }
        public Guid PurchaseTypeId => Company.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Purchase).Id;
        public Guid SalesTypeId => Company.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Sales).Id;
    }

    private static DomainLedger Add(Company c, string name, string groupName, bool openingIsDebit)
    {
        var l = new DomainLedger(Guid.NewGuid(), name, c.FindGroupByName(groupName)!.Id, Money.Zero, openingIsDebit);
        c.AddLedger(l);
        return l;
    }

    private static Fx Build()
    {
        var c = CompanyFactory.CreateSeeded("Apex Service Record Fixture", FyStart);
        // A captured company address, so OUR block on the record is a complete Rule 46(a)-shaped block and the
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

        var fees = Add(c, "Professional Fees", "Indirect Expenses", true);
        fees.SalesPurchaseGst = new StockItemGstDetails
        { HsnSac = "998311", Taxability = GstTaxability.Taxable, RateBasisPoints = 1800 };

        var filing = Add(c, "Statutory Filing Charges", "Indirect Expenses", true);
        filing.SalesPurchaseGst = new StockItemGstDetails { HsnSac = "999999", Taxability = GstTaxability.Exempt };

        var income = Add(c, "Consultancy Income", "Sales Accounts", false);
        income.SalesPurchaseGst = new StockItemGstDetails
        { HsnSac = "998311", Taxability = GstTaxability.Taxable, RateBasisPoints = 1800 };

        var supplier = Add(c, "Gujarat Advisers", "Sundry Creditors", false);
        supplier.PartyGst = new PartyGstDetails
        { RegistrationType = GstRegistrationType.Regular, Gstin = SupplierGstin, StateCode = "24" };
        supplier.Mailing = new PartyMailingDetails { Address = "9 GIDC Estate\nSurat", Pincode = "395003" };

        var customer = Add(c, "Gujarat Client", "Sundry Debtors", true);
        customer.PartyGst = new PartyGstDetails
        { RegistrationType = GstRegistrationType.Regular, Gstin = SupplierGstin, StateCode = "24" };

        return new Fx
        {
            Company = c,
            ProfessionalFeesId = fees.Id,
            FilingChargesId = filing.Id,
            ConsultancyIncomeId = income.Id,
            SupplierId = supplier.Id,
            CustomerId = customer.Id,
        };
    }

    private static Guid HeadId(Company c, GstTaxDirection direction, GstTaxHead head) =>
        c.Ledgers.Single(l => l.GstClassification is { IsReverseCharge: false } g
            && g.Direction == direction && g.TaxHead == head).Id;

    /// <summary>
    /// The shape the whole slice is about: an INTER-state purchase ACCOUNTING (service) invoice from a registered
    /// supplier — two SAC-bearing expense legs, one taxable and one exempt, and the Input IGST he charged us posted
    /// as a tagged leg. The accounting-invoice flag is the one the shipped screen stamps
    /// (<c>VoucherEntryViewModel.AcceptAccountingInvoice</c>).
    /// </summary>
    private static Voucher TaxedServicePurchase(Fx f, int number = 1, bool untaggedTax = false)
    {
        var inputIgst = HeadId(f.Company, GstTaxDirection.Input, GstTaxHead.Integrated);
        return new Voucher(Guid.NewGuid(), f.PurchaseTypeId, DocDate, new List<EntryLine>
        {
            new(f.ProfessionalFeesId, new Money(TaxableLeg), DrCr.Debit),
            new(f.FilingChargesId, new Money(ExemptLeg), DrCr.Debit),
            untaggedTax
                ? new EntryLine(inputIgst, new Money(Igst), DrCr.Debit)
                : new EntryLine(inputIgst, new Money(Igst), DrCr.Debit,
                    gst: new GstLineTax(GstTaxHead.Integrated, 1800, new Money(TaxableLeg))),
            new(f.SupplierId, new Money(SupplierLeg), DrCr.Credit),
        }, number: number, partyId: f.SupplierId,
            referenceNo: SupplierDocNumber, referenceDate: new DateOnly(2025, 4, 9),
            isAccountingInvoice: true);
    }

    /// <summary>The OUTWARD mirror, byte-for-byte the shape slice S3 must not move: a Sales accounting invoice.</summary>
    private static Voucher TaxedServiceSale(Fx f, int number = 7)
    {
        var outputIgst = HeadId(f.Company, GstTaxDirection.Output, GstTaxHead.Integrated);
        return new Voucher(Guid.NewGuid(), f.SalesTypeId, DocDate, new List<EntryLine>
        {
            new(f.ConsultancyIncomeId, new Money(TaxableLeg), DrCr.Credit),
            new EntryLine(outputIgst, new Money(Igst), DrCr.Credit,
                gst: new GstLineTax(GstTaxHead.Integrated, 1800, new Money(TaxableLeg))),
            new(f.CustomerId, new Money(TaxableLeg + Igst), DrCr.Debit),
        }, number: number, partyId: f.CustomerId, isAccountingInvoice: true);
    }

    /// <summary>The rendered PDF as text. Latin-1, the same decode every other print test here uses.</summary>
    private static string PdfText(byte[] bytes) => Encoding.Latin1.GetString(bytes);

    private static InvoicePrintData Project(Fx f, Voucher v) =>
        VoucherPrintProjector.ProjectInvoice(f.Company, v);

    // ================================================================ THE DEFECT

    /// <summary>
    /// <b>The user-visible defect.</b> RQ-11a puts the purchase <i>accounting-(service)-invoice</i> inside its scope
    /// in as many words, and requires every figure to tie to the posted voucher to the paisa (ER-4). Today the
    /// voucher takes <c>ProjectVoucher</c>, whose only loop is over <c>voucher.Lines</c> and which names no
    /// document, no supplier and no tax.
    /// <para>Every figure is derived from the fixture arithmetic at the top of this file — the two leg values, their
    /// sum, the 18% IGST computed by hand on the TAXABLE leg alone, and the supplier's credit leg — never read back
    /// from the projector.</para>
    /// </summary>
    [Fact]
    public void A_purchase_accounting_invoice_prints_as_a_record_with_its_service_lines()
    {
        var f = Build();
        var v = TaxedServicePurchase(f);

        // The routing question, at the real user path (P / Ctrl+P).
        Assert.Equal(PrintPreviewViewModel.PrintKind.Invoice,
            new VoucherDetailViewModel(f.Company, v).BuildPrintPreview().Kind);

        var data = Project(f, v);

        // Both legs print — the EXEMPT one is never silently dropped from the value of the supply.
        Assert.Equal(2, data.Items.Count);
        Assert.Equal(TaxableLeg, data.Items.Single(i => i.HsnSac == "998311").TaxableValue.Amount);
        Assert.Equal(ExemptLeg, data.Items.Single(i => i.HsnSac == "999999").TaxableValue.Amount);
        Assert.Equal("Professional Fees", data.Items.Single(i => i.HsnSac == "998311").Description);
        // A service carries neither a quantity nor a per-unit rate.
        Assert.All(data.Items, i => Assert.Equal(string.Empty, i.QuantityText));

        // Only the TAXABLE leg carries a rate row, on its own taxable value — never the whole 46,913.56.
        var tr = Assert.Single(data.TaxRows);
        Assert.Equal("18%", tr.RateLabel);
        Assert.Equal(TaxableLeg, tr.TaxableValue.Amount);
        Assert.Equal(Igst, tr.Igst.Amount);

        // ER-4 to the paisa, against the fixture's arithmetic and the posted supplier leg.
        Assert.Equal(ServiceValue, data.TotalTaxable.Amount);
        Assert.Equal(Igst, data.TotalIgst.Amount);
        Assert.Equal(SupplierLeg, data.GrandTotal.Amount);
        Assert.Equal(SupplierLeg,
            v.Lines.Single(l => l.LedgerId == f.SupplierId && l.Side == DrCr.Credit).Amount.Amount);

        // …and the services actually reach the page.
        var text = PdfText(new VoucherDetailViewModel(f.Company, v).BuildPrintPreview().PdfBytes);
        Assert.Contains("Professional Fees", text);
        Assert.Contains("998311", text);
    }

    // ================================================================ WHAT IT MAY AND MAY NOT BE CALLED

    /// <summary>
    /// RQ-11a: the record "SHALL NOT be titled <i>Tax Invoice</i>, NOR <i>Bill of Supply</i>" — CGST Act §31(1) puts
    /// the tax invoice on the person <b>supplying</b>, and CGST Rule 49 opens the bill of supply with the same
    /// words, so widening either outward title to an inward supply swaps one false statement for another.
    /// </summary>
    [Fact]
    public void A_purchase_accounting_record_bears_neither_outward_title()
    {
        var f = Build();
        var v = TaxedServicePurchase(f);

        var doc = GstReportSupport.ClassifyPrintedDocument(f.Company, v);
        Assert.Equal(DocumentRole.Recorded, doc.Role);
        Assert.Equal(GstReportSupport.PurchaseRecordTitle, doc.Title);
        Assert.Equal("PURCHASE RECORD", doc.Title);
        Assert.True(doc.RendersItemDetail);
        Assert.Equal(TaxParticulars.AsChargedByTheSupplier, doc.StatesTax);

        var data = Project(f, v);
        Assert.Equal(GstReportSupport.PurchaseRecordTitle, data.DocumentTitle);
        Assert.False(data.IsBillOfSupply);
        Assert.True(data.IsRecipientRecord);

        var text = PdfText(new VoucherDetailViewModel(f.Company, v).BuildPrintPreview().PdfBytes);
        Assert.Contains("PURCHASE RECORD", text);
        Assert.DoesNotContain("TAX INVOICE", text);
        Assert.DoesNotContain("BILL OF SUPPLY", text);

        // The badge over the drill reads the SAME decision the paper does.
        Assert.Equal("Purchase Record", new VoucherDetailViewModel(f.Company, v).DocumentLabel);
    }

    // ================================================================ THE THIRD AXIS — WHOSE IDENTITY HEADS IT

    /// <summary>
    /// RQ-11a: the record "SHALL be headed by the SUPPLIER's identity — supplier name / address / GSTIN in the
    /// supplier block and our name / address / GSTIN in the recipient block". CGST Rule 46(a) requires the "name,
    /// address and GSTIN of the supplier"; on a supply made TO us that is not us. Without this the record prints
    /// OUR GSTIN as the supplier's — a false statutory statement of exactly the FIX-W1e class.
    /// </summary>
    [Fact]
    public void A_purchase_accounting_record_puts_the_supplier_in_the_supplier_block_and_us_in_the_recipient_block()
    {
        var f = Build();
        var data = Project(f, TaxedServicePurchase(f));

        Assert.Equal(PartyOrientation.WeAreRecipient, data.Heads);
        Assert.Equal(SupplierGstin, data.Seller.Gstin);      // the REVERSE of every shipped Sales invoice
        Assert.Equal("Gujarat Advisers", data.Seller.Name);
        Assert.Equal(OurGstin, data.Buyer.Gstin);
        Assert.Contains("Marine Lines", string.Join("|", data.Buyer.AddressLines));
    }

    /// <summary>
    /// RQ-11a: the record "SHALL suppress every particular that is the supplier's to state: place of supply, our
    /// declaration and our signature". CGST Rule 46(n) puts the place of supply on the supplier — we do not
    /// determine the place of supply of a supply made TO us — and Rule 46(q) puts the signature on the ISSUER.
    /// </summary>
    [Fact]
    public void A_purchase_accounting_record_states_no_place_of_supply_and_no_declaration_or_signature_of_ours()
    {
        var f = Build();
        var v = TaxedServicePurchase(f);
        var data = Project(f, v);

        Assert.Equal(string.Empty, data.PlaceOfSupply);
        Assert.Equal(string.Empty, data.TopDeclaration);
        Assert.False(data.StatesOurDeclarationAndSignature);

        // The supplier's own document number rides the existing reference pair; OUR number may never appear under
        // an "Invoice No." caption on a document headed by his identity (RQ-11a).
        Assert.Equal("Supplier Invoice No.", data.ReferenceCaption);
        Assert.Equal(SupplierDocNumber, data.ReferenceNo);

        // Every drawn text run opens with "(" in the PDF content stream, so "(Invoice No." is OUR number under the
        // forbidden caption while "(Supplier Invoice No." is the supplier's under the required one. Asserting on the
        // bare substring would have matched the second inside the first and passed vacuously.
        var text = PdfText(new VoucherDetailViewModel(f.Company, v).BuildPrintPreview().PdfBytes);
        Assert.DoesNotContain("(Invoice No.", text);
        Assert.Contains("(Supplier Invoice No.: " + SupplierDocNumber, text);
        Assert.Contains("(" + GstReportSupport.RecordNumberCaption, text);
    }

    // ================================================================ WHOSE TAX IT STATES

    /// <summary>
    /// The record MUST state the tax — it is what substantiates the input tax credit we claim — but that tax is the
    /// supplier's charge to us, and a page headed by his identity presenting it as tax <i>we</i> charged makes a
    /// false statutory statement. So the figures are stated and captioned as his.
    /// </summary>
    [Fact]
    public void A_purchase_accounting_record_states_the_tax_as_the_suppliers_charge()
    {
        var f = Build();
        var v = TaxedServicePurchase(f);

        Assert.Equal(TaxParticulars.AsChargedByTheSupplier,
            GstReportSupport.ClassifyPrintedDocument(f.Company, v).StatesTax);

        var text = PdfText(new VoucherDetailViewModel(f.Company, v).BuildPrintPreview().PdfBytes);
        Assert.Contains(GstReportSupport.SupplierTaxCaption, text);
    }

    // ================================================================ THE FILING FREEZE (Decision 1)

    /// <summary>
    /// <b>The predicate this slice does NOT move.</b> <c>IsTaxInvoice</c> gates <c>IsBillOfSupply</c>'s exempt limb,
    /// which feeds <c>IsBillOfSupplyForFiling</c> and through it the NIC e-Way Part-A <c>docType</c> we file with a
    /// government portal. Both it and the Sales-only service gate stay FALSE for every purchase shape; the record is
    /// produced one level up, by the classifier.
    /// </summary>
    [Fact]
    public void The_outward_predicates_stay_false_for_every_purchase_accounting_shape()
    {
        var f = Build();
        foreach (var v in new[] { TaxedServicePurchase(f), TaxedServicePurchase(f, untaggedTax: true) })
        {
            Assert.False(GstReportSupport.IsTaxInvoice(f.Company, v));
            Assert.False(GstReportSupport.IsServiceAccountingInvoice(f.Company, v));
            Assert.False(GstReportSupport.IsBillOfSupply(f.Company, v));
        }
    }

    // ================================================================ ER-13 — THE OUTWARD SIDE IS UNMOVED

    /// <summary>
    /// The Sales accounting invoice is byte-for-byte what it was: a TAX INVOICE headed by US, stating a place of
    /// supply and carrying our declaration and signature. S3 widens the INWARD branch only.
    /// </summary>
    [Fact]
    public void A_sales_accounting_invoice_is_unchanged_by_the_record_extension()
    {
        var f = Build();
        var v = TaxedServiceSale(f);

        Assert.True(GstReportSupport.IsServiceAccountingInvoice(f.Company, v));
        var data = Project(f, v);

        Assert.Equal(GstReportSupport.TaxInvoiceTitle, data.DocumentTitle);
        Assert.False(data.IsRecipientRecord);
        Assert.Equal(PartyOrientation.WeAreSupplier, data.Heads);
        Assert.True(data.StatesOurDeclarationAndSignature);
        Assert.Equal(OurGstin, data.Seller.Gstin);
        Assert.NotEqual(string.Empty, data.PlaceOfSupply);
        Assert.Equal("Reference No.", data.ReferenceCaption);
        Assert.Equal(TaxableLeg + Igst, data.GrandTotal.Amount);
    }

    // ================================================================ THE CONSERVATIVE DIRECTION

    /// <summary>
    /// <b>A purchase whose posted Input tax the printer cannot see is NOT a record document.</b> The record derives
    /// 100% of its tax from <c>GstLineTax</c> metadata — that is what makes a reprint immune to a later master edit
    /// — so an untagged Input IGST leg would print a Grand Total short of the posted supplier leg by the whole tax,
    /// which RQ-11a's ER-4 forbids. The conservative direction is the one the outward side already takes: it is not
    /// a document at all and prints as the plain Dr/Cr voucher, which states every posted leg exactly.
    /// <para><b>Bite:</b> drop the footing conjunct from the inward accounting gate and this voucher prints a
    /// PURCHASE RECORD demanding 46,913.56 against a posted supplier credit of 53,135.78.</para>
    /// </summary>
    [Fact]
    public void A_purchase_accounting_invoice_whose_input_tax_is_untagged_prints_the_plain_voucher()
    {
        var f = Build();
        var v = TaxedServicePurchase(f, untaggedTax: true);

        Assert.Equal(DocumentRole.NoStatutoryDocument,
            GstReportSupport.ClassifyPrintedDocument(f.Company, v).Role);
        Assert.Equal(PrintPreviewViewModel.PrintKind.Voucher,
            new VoucherDetailViewModel(f.Company, v).BuildPrintPreview().Kind);
        Assert.Equal(string.Empty, new VoucherDetailViewModel(f.Company, v).DocumentLabel);
    }

    /// <summary>
    /// <b>A withholding purchase does not foot, so it is not a record either.</b> A §194J carve-out credits TDS
    /// Payable and REDUCES the supplier's credit leg, so the projection (services + posted tax) would state
    /// 53,135.78 against a posted supplier credit of 49,678.78 — a document overstating what the supplier is owed by
    /// the whole withholding. <c>InvoicePrintData</c> has no vocabulary for a withholding, so the voucher takes the
    /// plain Dr/Cr print, which states every posted leg exactly.
    /// </summary>
    [Fact]
    public void A_purchase_accounting_invoice_carrying_a_tds_carve_out_prints_the_plain_voucher()
    {
        var f = Build();
        var tds = Add(f.Company, "TDS Payable - 194J", "Duties & Taxes", false);
        var inputIgst = HeadId(f.Company, GstTaxDirection.Input, GstTaxHead.Integrated);

        // 10% of the professional-fee leg, to the rupee: 34,567.89 -> 3,457.00.
        const decimal Withheld = 3_457.00m;
        const decimal NetSupplierLeg = SupplierLeg - Withheld;   // 49,678.78

        var v = new Voucher(Guid.NewGuid(), f.PurchaseTypeId, DocDate, new List<EntryLine>
        {
            new(f.ProfessionalFeesId, new Money(TaxableLeg), DrCr.Debit),
            new(f.FilingChargesId, new Money(ExemptLeg), DrCr.Debit),
            new EntryLine(inputIgst, new Money(Igst), DrCr.Debit,
                gst: new GstLineTax(GstTaxHead.Integrated, 1800, new Money(TaxableLeg))),
            new(tds.Id, new Money(Withheld), DrCr.Credit),
            new(f.SupplierId, new Money(NetSupplierLeg), DrCr.Credit),
        }, number: 3, partyId: f.SupplierId, isAccountingInvoice: true);

        Assert.Equal(DocumentRole.NoStatutoryDocument,
            GstReportSupport.ClassifyPrintedDocument(f.Company, v).Role);
        Assert.Equal(PrintPreviewViewModel.PrintKind.Voucher,
            new VoucherDetailViewModel(f.Company, v).BuildPrintPreview().Kind);
    }

    /// <summary>
    /// <b>A plain (As-Voucher) purchase is untouched.</b> The accounting-invoice flag is what the shipped screen
    /// stamps when the operator entered the voucher in Accounting Invoice mode; a hand-keyed Dr/Cr purchase never
    /// sets it and must keep printing the plain voucher — the same structural exclusion the outward side relies on.
    /// </summary>
    [Fact]
    public void A_plain_as_voucher_purchase_is_still_the_plain_voucher()
    {
        var f = Build();
        var inputIgst = HeadId(f.Company, GstTaxDirection.Input, GstTaxHead.Integrated);
        var v = new Voucher(Guid.NewGuid(), f.PurchaseTypeId, DocDate, new List<EntryLine>
        {
            new(f.ProfessionalFeesId, new Money(TaxableLeg), DrCr.Debit),
            new(f.FilingChargesId, new Money(ExemptLeg), DrCr.Debit),
            new EntryLine(inputIgst, new Money(Igst), DrCr.Debit,
                gst: new GstLineTax(GstTaxHead.Integrated, 1800, new Money(TaxableLeg))),
            new(f.SupplierId, new Money(SupplierLeg), DrCr.Credit),
        }, number: 4, partyId: f.SupplierId); // no isAccountingInvoice

        Assert.Equal(DocumentRole.NoStatutoryDocument,
            GstReportSupport.ClassifyPrintedDocument(f.Company, v).Role);
        Assert.Equal(PrintPreviewViewModel.PrintKind.Voucher,
            new VoucherDetailViewModel(f.Company, v).BuildPrintPreview().Kind);
    }
}
