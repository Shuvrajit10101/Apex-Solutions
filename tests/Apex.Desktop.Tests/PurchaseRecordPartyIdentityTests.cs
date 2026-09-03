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
/// <b>T0-11 review C2/L1-02 and C24/L3-10 — WHOSE identity the record states, and off WHICH axis.</b>
///
/// <para><b>The defect C2 measured.</b> An ordinary supplier-master correction blanked the SUPPLIER's GSTIN on a
/// purchase record, and <c>InvoicePdf.DrawPartyBlock</c> does not print a blank — it prints the positive assertion
/// "GSTIN: Unregistered", on the same page that states CGST and SGST under "Tax Charged by the Supplier". CGST Act
/// §32(1) bars a person who is not registered from collecting any amount by way of tax, so the page refuted itself
/// and named no registered supplier against the credit it exists to verify. The root was not the renderer:
/// <c>IssuedBuyerStateCode</c> and <c>ConsistentBuyerGstin</c> are the FIX-3 reconciliation, and every clause of it
/// is about what an <b>ISSUED</b> document may state about ITS BUYER. Slice S2 flipped the counterparty block into
/// the supplier slot without revisiting either, so an outward rule ran over an inward party.</para>
///
/// <para><b>The defect C24 measured.</b> <c>PrintedDocumentClass</c> holds seven fields across three axes and
/// <c>InvoicePrintData</c> carried ONE boolean for all of them, so the renderer answered the ROLE question, the
/// ORIENTATION question and the DECLARATION/SIGNATURE question off the same flag. Any classification pairing
/// <c>Recorded</c> with <c>WeAreSupplier</c> therefore produced a half-swapped page. The DTO now carries the axes.</para>
///
/// <para><b>🔴 WHERE EVERY EXPECTED VALUE BELOW COMES FROM.</b> The money is computed from this file's own fixture
/// arithmetic, stated once at the top and never read back off the projector. The identity expectations come from
/// CGST Rule 46(a) — "name, address and GSTIN of the SUPPLIER" — read against the supplier master this book
/// records, not against what the projector happens to return.</para>
/// </summary>
public sealed class PurchaseRecordPartyIdentityTests
{
    private const string OurGstin = "27AAPFU0939F1ZV";           // Maharashtra (27) — the company
    private const string PostedTimeGstin = "27AAACC1206D1Z9";    // Maharashtra (27) — the supplier, as first keyed
    private const string CorrectedGstin = "24AAACC1206D1ZM";     // Gujarat (24)     — his real one
    private static readonly DateOnly FyStart = new(2025, 4, 1);
    private static readonly DateOnly DocDate = new(2025, 4, 10);

    // ---------------------------------------------------------------- the fixture arithmetic, stated once
    //
    // Odd to the paisa throughout: a round figure passes under a rounding defect and asserts nothing.
    private const decimal Rate = 4_321.09m; private const decimal Qty = 3m;
    private const decimal Goods = Rate * Qty;          // 12,963.27
    /// <summary>CGST at 9% on the goods value: 12,963.27 x 0.09 = 1,166.6943, to the paisa 1,166.69. SGST the same.</summary>
    private const decimal Cgst = 1_166.69m;
    private const decimal Sgst = 1_166.69m;
    /// <summary>What we owe the supplier — the posted CREDIT leg. 12,963.27 + 1,166.69 + 1,166.69.</summary>
    private const decimal SupplierLeg = Goods + Cgst + Sgst;   // 15,296.65

    private sealed class Fx
    {
        public required Company Company { get; init; }
        public required Guid GodownId { get; init; }
        public required Guid TaxableItemId { get; init; }
        public required Guid ExemptItemId { get; init; }
        public required Guid PurchaseLedgerId { get; init; }
        public required Guid SupplierId { get; init; }
        public DomainLedger Supplier => Company.FindLedger(SupplierId)!;
        public Guid PurchaseTypeId => Company.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Purchase).Id;
    }

    private static DomainLedger Add(Company c, string name, string groupName, bool openingIsDebit)
    {
        var l = new DomainLedger(Guid.NewGuid(), name, c.FindGroupByName(groupName)!.Id, Money.Zero, openingIsDebit);
        c.AddLedger(l);
        return l;
    }

    /// <summary>A book whose supplier is recorded IN OUR OWN STATE at posting time, so the purchase posts intra-state
    /// Input CGST + SGST — the shape C2 measured.</summary>
    private static Fx Build()
    {
        var c = CompanyFactory.CreateSeeded("Apex Identity Fixture", FyStart);
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

        var supplier = Add(c, "Local Supplier", "Sundry Creditors", false);
        supplier.PartyGst = new PartyGstDetails
        { RegistrationType = GstRegistrationType.Regular, Gstin = PostedTimeGstin, StateCode = "27" };
        supplier.Mailing = new PartyMailingDetails { Address = "9 Fort Street\nMumbai", Pincode = "400001" };

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

    /// <summary>The intra-state purchase item invoice, with the supplier's forward CGST/SGST posted as tagged legs.</summary>
    private static Voucher IntraStatePurchase(Fx f, int number = 1) =>
        new(Guid.NewGuid(), f.PurchaseTypeId, DocDate, new List<EntryLine>
        {
            new(f.PurchaseLedgerId, new Money(Goods), DrCr.Debit),
            new(InputHeadId(f.Company, GstTaxHead.Central), new Money(Cgst), DrCr.Debit,
                gst: new GstLineTax(GstTaxHead.Central, 900, new Money(Goods))),
            new(InputHeadId(f.Company, GstTaxHead.State), new Money(Sgst), DrCr.Debit,
                gst: new GstLineTax(GstTaxHead.State, 900, new Money(Goods))),
            new(f.SupplierId, new Money(SupplierLeg), DrCr.Credit),
        }, number: number, partyId: f.SupplierId, inventoryLines: new[]
        {
            new VoucherInventoryLine(f.TaxableItemId, f.GodownId, Qty, new Money(Rate)),
        });

    /// <summary>A wholly EXEMPT inward supply from the same supplier: no tax leg at all, so the record states no tax
    /// figure and could bear none.</summary>
    private static Voucher ExemptPurchase(Fx f, int number = 2) =>
        new(Guid.NewGuid(), f.PurchaseTypeId, DocDate, new List<EntryLine>
        {
            new(f.PurchaseLedgerId, new Money(Goods), DrCr.Debit),
            new(f.SupplierId, new Money(Goods), DrCr.Credit),
        }, number: number, partyId: f.SupplierId, inventoryLines: new[]
        {
            new VoucherInventoryLine(f.ExemptItemId, f.GodownId, 1m, new Money(Goods)),
        });

    private static string PdfText(byte[] bytes) => Encoding.Latin1.GetString(bytes);

    // ================================================================ C2 / L1-02

    /// <summary>
    /// <b>THE defect.</b> One ordinary master correction — the operator discovers the supplier is a Gujarat dealer and
    /// fixes his GSTIN and State — and the reprinted record asserts "GSTIN: Unregistered" above the CGST and SGST he
    /// charged. CGST Act §32(1): "a person who is not a registered person shall not collect in respect of any supply
    /// of goods or services or both any amount by way of tax". The page contradicts itself.
    ///
    /// <para><b>What the record must state instead, and why it is the master verbatim.</b> Rule 46(a) puts "name,
    /// address and GSTIN of the SUPPLIER" at the head of the document. On a record that is a fact ABOUT HIM, held in
    /// his own master; there is no reconciliation to perform, because we are not the ones who determined it. The
    /// FIX-3 ladder that produced the blank exists for the opposite question — what an ISSUED document may state
    /// about ITS BUYER, where the printed State had to be reconciled against tax WE posted — and it must not be
    /// reached from the flipped block at all.</para>
    ///
    /// <para>Print-only, and asserted as such: not one posted figure moves, and the Grand Total still ties to the
    /// supplier's credit leg to the paisa.</para>
    /// </summary>
    [Fact]
    public void A_supplier_master_correction_never_prints_the_supplier_as_unregistered()
    {
        var f = Build();
        var v = IntraStatePurchase(f);

        // Before the correction: the master and the posted tax agree, and the record states him as keyed.
        var before = VoucherPrintProjector.ProjectInvoice(f.Company, v);
        Assert.True(before.IsRecipientRecord);
        Assert.Equal("Local Supplier", before.Seller.Name);
        Assert.Equal(PostedTimeGstin, before.Seller.Gstin);
        Assert.Equal("Maharashtra (27)", before.Seller.StateText);

        // ONE ordinary master correction. The voucher is untouched.
        f.Supplier.PartyGst = new PartyGstDetails
        { RegistrationType = GstRegistrationType.Regular, Gstin = CorrectedGstin, StateCode = "24" };

        var after = VoucherPrintProjector.ProjectInvoice(f.Company, v);

        // Rule 46(a) — the supplier's own recorded identity, and it is internally consistent: a GSTIN's first two
        // characters ARE its State code, so "24…" under "Gujarat (24)" is the one pairing that states no falsehood.
        Assert.Equal("Local Supplier", after.Seller.Name);
        Assert.Equal(CorrectedGstin, after.Seller.Gstin);
        Assert.Equal("Gujarat (24)", after.Seller.StateText);
        Assert.StartsWith("24", after.Seller.Gstin, StringComparison.Ordinal);

        // Our own block is untouched — we are the recipient and our GSTIN is ours.
        Assert.Equal(OurGstin, after.Buyer.Gstin);
        Assert.Equal("Maharashtra (27)", after.Buyer.StateText);

        // Not a money change: the posted legs decide the figures, and the demand still foots (RQ-11a ER-4).
        Assert.Equal(Goods, after.TotalTaxable.Amount);
        Assert.Equal(Cgst, after.TotalCgst.Amount);
        Assert.Equal(Sgst, after.TotalSgst.Amount);
        Assert.Equal(SupplierLeg, after.GrandTotal.Amount);
        Assert.Equal(SupplierLeg,
            v.Lines.Single(l => l.LedgerId == f.SupplierId && l.Side == DrCr.Credit).Amount.Amount);

        // …and on the bytes that leave the building, through the real user path (P / Ctrl+P).
        var text = PdfText(new VoucherDetailViewModel(f.Company, v).BuildPrintPreview().PdfBytes);
        Assert.Contains(GstReportSupport.PurchaseRecordTitle, text);
        Assert.Contains(GstReportSupport.SupplierTaxCaption, text);
        Assert.Contains("1,166.69", text);
        Assert.Contains(CorrectedGstin, text);
        Assert.DoesNotContain("GSTIN: Unregistered", text);
    }

    /// <summary>
    /// The OUTWARD reconciliation the record must not reach is still live where it belongs. Same book, same edit,
    /// but the supply is one WE issued: the buyer's printed State was reconciled to the CGST+SGST we posted, so
    /// re-stating his "24…" GSTIN under "Maharashtra (27)" would put the contradiction straight back on a document
    /// that left the building. FIX-3 is unmoved.
    /// </summary>
    [Fact]
    public void The_outward_buyer_reconciliation_is_untouched_by_the_record_fix()
    {
        var f = Build();
        var salesType = f.Company.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Sales);
        var sales = Add(f.Company, "Sales", "Sales Accounts", false);
        var customer = Add(f.Company, "Local Customer", "Sundry Debtors", true);
        customer.PartyGst = new PartyGstDetails
        { RegistrationType = GstRegistrationType.Regular, Gstin = PostedTimeGstin, StateCode = "27" };

        var outputCgst = f.Company.Ledgers.Single(l => l.GstClassification is
        { Direction: GstTaxDirection.Output, IsReverseCharge: false } g && g.TaxHead == GstTaxHead.Central).Id;
        var outputSgst = f.Company.Ledgers.Single(l => l.GstClassification is
        { Direction: GstTaxDirection.Output, IsReverseCharge: false } g && g.TaxHead == GstTaxHead.State).Id;

        var v = new Voucher(Guid.NewGuid(), salesType.Id, DocDate, new List<EntryLine>
        {
            new(customer.Id, new Money(SupplierLeg), DrCr.Debit),
            new(sales.Id, new Money(Goods), DrCr.Credit),
            new(outputCgst, new Money(Cgst), DrCr.Credit, gst: new GstLineTax(GstTaxHead.Central, 900, new Money(Goods))),
            new(outputSgst, new Money(Sgst), DrCr.Credit, gst: new GstLineTax(GstTaxHead.State, 900, new Money(Goods))),
        }, number: 9, partyId: customer.Id, inventoryLines: new[]
        {
            new VoucherInventoryLine(f.TaxableItemId, f.GodownId, Qty, new Money(Rate)),
        });

        customer.PartyGst = new PartyGstDetails
        { RegistrationType = GstRegistrationType.Regular, Gstin = CorrectedGstin, StateCode = "24" };

        var data = VoucherPrintProjector.ProjectInvoice(f.Company, v);
        Assert.False(data.IsRecipientRecord);
        // Posted CGST+SGST asserts the place of supply IS our State, so the State is recovered — and the GSTIN whose
        // own prefix denies it is dropped rather than printed against it.
        Assert.Equal("Maharashtra (27)", data.Buyer.StateText);
        Assert.Equal(string.Empty, data.Buyer.Gstin);
    }

    // ================================================================ C24 / L3-10 — the axes

    /// <summary>
    /// The three axes reach the renderer as three answers. Read from the ONE classification, never re-derived, so a
    /// future branch cannot pair them differently on the DTO than the classifier paired them.
    /// </summary>
    [Fact]
    public void The_record_carries_every_axis_of_its_classification_not_one_boolean()
    {
        var f = Build();
        var v = IntraStatePurchase(f);

        var doc = GstReportSupport.ClassifyPrintedDocument(f.Company, v);
        var data = VoucherPrintProjector.ProjectInvoice(f.Company, v);

        Assert.Equal(DocumentRole.Recorded, doc.Role);
        Assert.True(data.IsRecipientRecord);                                  // ROLE
        Assert.Equal(PartyOrientation.WeAreRecipient, doc.Heads);
        Assert.Equal(PartyOrientation.WeAreRecipient, data.Heads);            // ORIENTATION
        Assert.False(doc.StatesOurDeclarationAndSignature);
        Assert.False(data.StatesOurDeclarationAndSignature);                  // Rule 46(q)
        Assert.Equal(TaxParticulars.AsChargedByTheSupplier, doc.StatesTax);
        Assert.False(data.IsBillOfSupply);                                    // TAX: not the None case

        // The outward control: every axis takes the other value, off the same classification.
        var outward = VoucherPrintProjector.ProjectInvoice(f.Company, OutwardSale(f));
        Assert.False(outward.IsRecipientRecord);
        Assert.Equal(PartyOrientation.WeAreSupplier, outward.Heads);
        Assert.True(outward.StatesOurDeclarationAndSignature);
    }

    private static Voucher OutwardSale(Fx f)
    {
        var salesType = f.Company.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Sales);
        var sales = Add(f.Company, "Sales Control", "Sales Accounts", false);
        var customer = Add(f.Company, "Cash Customer", "Sundry Debtors", true);
        customer.PartyGst = new PartyGstDetails
        { RegistrationType = GstRegistrationType.Regular, Gstin = PostedTimeGstin, StateCode = "27" };
        var outputCgst = f.Company.Ledgers.Single(l => l.GstClassification is
        { Direction: GstTaxDirection.Output, IsReverseCharge: false } g && g.TaxHead == GstTaxHead.Central).Id;
        var outputSgst = f.Company.Ledgers.Single(l => l.GstClassification is
        { Direction: GstTaxDirection.Output, IsReverseCharge: false } g && g.TaxHead == GstTaxHead.State).Id;
        return new Voucher(Guid.NewGuid(), salesType.Id, DocDate, new List<EntryLine>
        {
            new(customer.Id, new Money(SupplierLeg), DrCr.Debit),
            new(sales.Id, new Money(Goods), DrCr.Credit),
            new(outputCgst, new Money(Cgst), DrCr.Credit, gst: new GstLineTax(GstTaxHead.Central, 900, new Money(Goods))),
            new(outputSgst, new Money(Sgst), DrCr.Credit, gst: new GstLineTax(GstTaxHead.State, 900, new Money(Goods))),
        }, number: 11, partyId: customer.Id, inventoryLines: new[]
        {
            new VoucherInventoryLine(f.TaxableItemId, f.GodownId, Qty, new Money(Rate)),
        });
    }

    /// <summary>
    /// <b>The half-swapped page C24 rendered.</b> A document that is a RECORD but headed by US — the shape a future
    /// branch reaches (S4's purchase-return record, S5's §31(3)(f) self-invoice, where we ARE the issuer) — used to
    /// print the record legend "issued by the supplier named above" with OUR name in the block above it. Nothing
    /// pairs the axes on <c>PrintedDocumentClass</c> (a bare positional record) so the DTO must render each axis's
    /// own answer rather than one flag's answer three times.
    ///
    /// <para>The party captions are the check that bites: they are the orientation question, and they now follow
    /// <c>Heads</c>. The legend is the role+orientation question and is stated only when both agree.</para>
    /// </summary>
    [Fact]
    public void A_record_headed_by_us_states_no_legend_about_a_supplier_who_is_us()
    {
        var us = new InvoicePartyBlock
        { Name = "Apex Identity Fixture", Gstin = OurGstin, StateText = "Maharashtra (27)" };
        var them = new InvoicePartyBlock
        { Name = "Local Supplier", Gstin = PostedTimeGstin, StateText = "Maharashtra (27)" };

        // A real per-rate group, so the breakup band is actually DRAWN and the caption assertions below are not
        // satisfied by a table that was never emitted (`InvoicePdf.StatesTaxBreakup` needs rows AND a routing).
        var rate = new[]
        {
            new InvoiceTaxRow
            {
                RateLabel = "18%", TaxableValue = new Money(Goods),
                Cgst = new Money(Cgst), Sgst = new Money(Sgst),
            },
        };

        var halfSwapped = new InvoicePrintData
        {
            DocumentTitle = GstReportSupport.PurchaseRecordTitle,
            IsRecipientRecord = true,                       // ROLE: a record
            Heads = PartyOrientation.WeAreSupplier,         // ORIENTATION: but headed by US
            StatesOurDeclarationAndSignature = true,        // Rule 46(q): so our signature belongs on it
            Seller = us,
            Buyer = them,
            InvoiceNumber = "7",
            InvoiceDateText = "10-04-2025",
            IsInterState = false,
            TaxRows = rate,
            TotalTaxable = new Money(Goods),
            TotalCgst = new Money(Cgst),
            TotalSgst = new Money(Sgst),
        };

        var text = PdfText(InvoicePdf.Render(halfSwapped, new PrintConfig(), new PageConfig()));

        // The role axis is unchanged and still refuses both outward titles.
        Assert.Contains(GstReportSupport.PurchaseRecordTitle, text);
        Assert.DoesNotContain(GstReportSupport.TaxInvoiceTitle, text);
        // The ORIENTATION axis. We head the page, so the other party IS being billed and the Rule 46(a) caption
        // says so — the shipped record's plain "Recipient:" would be the false one here. Asserted on the paren-free
        // fragment: a PDF string literal escapes its brackets, so the bytes carry "Recipient \(Bill to\):".
        Assert.Contains("Bill to", text);
        // The legend asserts a supplier who is not us. With us in the head block it must not be stated.
        Assert.DoesNotContain("record of a document issued by the supplier", text);
        // Rule 46(q) — the signature follows its own axis, not the role flag.
        Assert.Contains("Authorised Signatory", text);
        Assert.Contains("For Apex Identity Fixture", text);
        // The tax caption is the WHOSE-tax question and follows orientation: nobody else charged this.
        Assert.Contains("GST Breakup", text);
        Assert.DoesNotContain(GstReportSupport.SupplierTaxCaption, text);

        // The coherent shape — the only one production reaches today — is unmoved, and every one of the five
        // assertions above takes its other value on it. Nothing here is satisfied by a page nobody drew.
        var coherent = new InvoicePrintData
        {
            DocumentTitle = GstReportSupport.PurchaseRecordTitle,
            IsRecipientRecord = true,
            Seller = them,
            Buyer = us,
            InvoiceNumber = "7",
            InvoiceDateText = "10-04-2025",
            IsInterState = false,
            TaxRows = rate,
            TotalTaxable = new Money(Goods),
            TotalCgst = new Money(Cgst),
            TotalSgst = new Money(Sgst),
        };
        var coherentText = PdfText(InvoicePdf.Render(coherent, new PrintConfig(), new PageConfig()));
        Assert.Contains("Recipient:", coherentText);
        Assert.DoesNotContain("Bill to", coherentText);
        Assert.Contains("record of a document issued by the supplier", coherentText);
        Assert.DoesNotContain("Authorised Signatory", coherentText);
        Assert.Contains(GstReportSupport.SupplierTaxCaption, coherentText);
        Assert.DoesNotContain("GST Breakup", coherentText);
    }

    // ================================================================ the inward-exempt fact

    /// <summary>
    /// <b>Expressibility only, and deliberately so.</b> C6/L1-06 and C7/L1-07 are two statements a wholly exempt
    /// inward supply makes that nothing on it bears — an intra/inter head classification with CGST 0.00 / SGST 0.00
    /// head rows, and the label "Taxable Value" over money that was never taxable. Both are corrections to what a
    /// purchase record SAYS ABOUT TAX, which is an open R12 question for the user (plan.md Phase 10.13 question 1),
    /// so this pass moves neither. What it does is make the fact the fix will need <b>sayable</b>: the DTO now
    /// carries it, the projector derives it from the POSTED legs like every other figure here, and this test pins
    /// the derivation so the later one-line wording change has something true to key on.
    /// </summary>
    [Fact]
    public void A_record_that_states_no_tax_figure_carries_the_inward_exempt_fact()
    {
        var f = Build();

        var exempt = VoucherPrintProjector.ProjectInvoice(f.Company, ExemptPurchase(f));
        Assert.True(exempt.IsRecipientRecord);
        Assert.Equal(Goods, exempt.TotalTaxable.Amount);
        Assert.Equal(0m, exempt.TotalTax.Amount);
        Assert.Equal(0m, exempt.TotalCess.Amount);
        Assert.True(exempt.IsInwardExempt);

        // A TAXED record is NOT it: "Taxable Value" is the truth there, and the heads have a referent.
        var taxed = VoucherPrintProjector.ProjectInvoice(f.Company, IntraStatePurchase(f));
        Assert.Equal(Cgst + Sgst, taxed.TotalTax.Amount);
        Assert.False(taxed.IsInwardExempt);

        // Nor is an OUTWARD exempt supply: it is a bill of supply, which already says "Value of Supply".
        Assert.False(new InvoicePrintData { IsBillOfSupply = true }.IsInwardExempt);
    }
}
