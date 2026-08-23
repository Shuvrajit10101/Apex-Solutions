using System.Text;
using Apex.Ledger;
using Apex.Ledger.Io;
using Apex.Ledger.Reports;
using Apex.Ledger.Services;

namespace Apex.Ledger.Io.Tests;

/// <summary>
/// The <b>CGST Rule 48(1) copy marking</b> — the label a physical print carries naming WHICH of the statutory
/// copies it is (RQ-12, <c>docs/phase5-reports-io-requirements.md:306</c>).
///
/// <para><b>🔴 THE RULE, VERIFIED AT THE PRIMARY SOURCE, not from a chart.</b> CGST Rules 2017 as published by
/// CBIC — <c>https://cbic-gst.gov.in/pdf/cgst-rules-30122017.pdf</c>, PDF p.40 (printed p.37), extracted with
/// <c>pdftotext -raw</c>. Rule 48 "Manner of issuing invoice" sub-rule (1) reads, verbatim:
/// <code>
/// (1) The invoice shall be prepared in triplicate, in the case of supply of goods, in the
///     following manner, namely,-
///     (a) the original copy being marked as ORIGINAL FOR RECIPIENT;
///     (b) the duplicate copy being marked as DUPLICATE FOR TRANSPORTER; and
///     (c) the triplicate copy being marked as TRIPLICATE FOR SUPPLIER.
/// </code>
/// So the DUPLICATE is the TRANSPORTER's and the TRIPLICATE is the SUPPLIER's. The app shipped them the other way
/// round — <c>Duplicate ⇒ "DUPLICATE FOR SUPPLIER"</c>, <c>Triplicate ⇒ "TRIPLICATE FOR TRANSPORTER"</c> — across
/// the model, the F12 radio captions and TWO GREEN TESTS (T0-11 review C10/L1-10). A transporter handed the copy
/// this app calls his is carrying the triplicate on a roadside check, and the marking on its face says so.</para>
///
/// <para><b>Rule 48(2) — the OTHER set, and why it does not license the old pairing.</b> For a supply of SERVICES
/// the invoice is prepared in DUPLICATE, "(a) … ORIGINAL FOR RECIPIENT; and (b) the duplicate copy being marked as
/// DUPLICATE FOR SUPPLIER" — so "DUPLICATE FOR SUPPLIER" is a real statutory marking, but of the two-copy services
/// set, in which no triplicate exists at all. <see cref="CopyMarking"/> is one three-valued set offering a
/// Triplicate beside the Duplicate, i.e. the goods set of Rule 48(1), and inside that set the old pairing is
/// simply transposed. A goods/services split of the marking is not modelled and is not smuggled in here.</para>
///
/// <para><b>And the copy marking is an ISSUER particular, so it must not print on a document we do not issue</b>
/// (T0-11 review C3/L1-03): Rule 48(1) prescribes the markings for the invoice prepared by the supplier under
/// §31(1) / Rule 46. Stamping one on a recipient-side PURCHASE RECORD makes that page assert it is one of the
/// supplier's statutory copies — the same class of false self-description slice S2 removed from the title, the
/// number caption, the place of supply, the declaration and the signature.</para>
/// </summary>
public sealed class CopyMarkingRule48Tests
{
    private static string AsLatin1(byte[] bytes) => Encoding.Latin1.GetString(bytes);

    private static string ValidGstin(string first14) => first14 + Apex.Ledger.Domain.Gstin.ComputeCheckDigit(first14 + "0");

    /// <summary>
    /// An outward tax invoice — the document Rule 48(1) is ABOUT. Taxable ₹4,321.00 @ 18% intra-State
    /// ⇒ CGST 388.89 + SGST 388.89, grand 5,098.78.
    /// </summary>
    private static InvoicePrintData OutwardTaxInvoice()
    {
        var taxable = new Money(4_321m);
        var tax = GstService.ComputeLineTax(taxable, 1800, interState: false);
        return new InvoicePrintData
        {
            DocumentTitle = GstReportSupport.TaxInvoiceTitle,
            Seller = new InvoicePartyBlock
            {
                Name = "Bright Traders", AddressLines = new[] { "12 Market Street", "Kolkata" },
                Gstin = ValidGstin("19AAAAA0000A1Z"), StateText = "West Bengal (19)",
            },
            Buyer = new InvoicePartyBlock
            {
                Name = "Gujarat Supplier", AddressLines = new[] { "9 Dockyard Road", "Surat" },
                Gstin = ValidGstin("24EEEEE0000E1Z"), StateText = "Gujarat (24)",
            },
            InvoiceNumber = "INV-0007",
            InvoiceDateText = "10-04-2025",
            PlaceOfSupply = "West Bengal (19)",
            IsInterState = false,
            Items = new[]
            {
                new InvoiceItemRow
                {
                    Description = "Raw Cotton", HsnSac = "520100",
                    QuantityText = "8.000", RateText = "540.125", TaxableValue = taxable,
                },
            },
            TaxRows = new[]
            {
                new InvoiceTaxRow
                {
                    RateLabel = "18%", TaxableValue = taxable, Cgst = tax.Cgst, Sgst = tax.Sgst, Igst = Money.Zero,
                },
            },
            TotalTaxable = taxable,
            TotalCgst = tax.Cgst,
            TotalSgst = tax.Sgst,
            TotalIgst = Money.Zero,
        };
    }

    /// <summary>
    /// The SAME supply, recorded from the recipient's side — the one difference is the role flag, exactly as
    /// <c>InvoicePdfTests</c>'s own negative control is built. The party blocks are swapped so the page is a
    /// coherent record throughout (Rule 46(a): the supplier HEADS it).
    /// </summary>
    private static InvoicePrintData RecipientRecord()
    {
        var outward = OutwardTaxInvoice();
        return new InvoicePrintData
        {
            IsRecipientRecord = true,                        // ⇒ Heads = WeAreRecipient, StatesOurDeclarationAndSignature = false
            DocumentTitle = GstReportSupport.PurchaseRecordTitle,
            Seller = outward.Buyer,                          // the supplier heads a record
            Buyer = outward.Seller,
            InvoiceNumber = "PUR-0007",
            InvoiceDateText = outward.InvoiceDateText,
            PlaceOfSupply = string.Empty,
            IsInterState = false,
            Items = outward.Items,
            TaxRows = outward.TaxRows,
            TotalTaxable = outward.TotalTaxable,
            TotalCgst = outward.TotalCgst,
            TotalSgst = outward.TotalSgst,
            TotalIgst = outward.TotalIgst,
        };
    }

    // ============================================================ L1-10 — the labels themselves

    /// <summary>
    /// <b>The three labels are the three Rule 48(1) markings, spelled as the rule spells them.</b> Every expected
    /// literal here is transcribed from the CBIC text quoted in this class's summary, never from
    /// <see cref="PrintConfig.CopyMarkingLabel"/>.
    /// <para><b>Bite:</b> transpose (b) and (c) — which is what shipped — and the second and third rows go red.</para>
    /// </summary>
    [Theory]
    [InlineData(CopyMarking.Original, "ORIGINAL FOR RECIPIENT")]     // Rule 48(1)(a)
    [InlineData(CopyMarking.Duplicate, "DUPLICATE FOR TRANSPORTER")] // Rule 48(1)(b)
    [InlineData(CopyMarking.Triplicate, "TRIPLICATE FOR SUPPLIER")]  // Rule 48(1)(c)
    [InlineData(CopyMarking.None, "")]
    public void The_copy_marking_labels_are_the_ones_Rule_48_1_prescribes(CopyMarking marking, string expected)
        => Assert.Equal(expected, new PrintConfig { CopyMarking = marking }.CopyMarkingLabel);

    /// <summary>
    /// <b>The corrected labels reach the page, and the transposed ones are gone from it.</b> The label is computed
    /// in <c>PrintConfig</c> and written by <c>InvoicePdf.DrawFirstHeader</c>, so a label test alone would not
    /// prove the marking a transporter actually holds; these are the bytes.
    /// </summary>
    [Fact]
    public void An_outward_invoice_prints_the_corrected_duplicate_and_triplicate_markings()
    {
        string dup = AsLatin1(InvoicePdf.Render(
            OutwardTaxInvoice(), new PrintConfig { CopyMarking = CopyMarking.Duplicate }, new PageConfig()));
        Assert.Contains("DUPLICATE FOR TRANSPORTER", dup);
        Assert.DoesNotContain("DUPLICATE FOR SUPPLIER", dup);

        string trip = AsLatin1(InvoicePdf.Render(
            OutwardTaxInvoice(), new PrintConfig { CopyMarking = CopyMarking.Triplicate }, new PageConfig()));
        Assert.Contains("TRIPLICATE FOR SUPPLIER", trip);
        Assert.DoesNotContain("TRIPLICATE FOR TRANSPORTER", trip);

        string orig = AsLatin1(InvoicePdf.Render(
            OutwardTaxInvoice(), new PrintConfig { CopyMarking = CopyMarking.Original }, new PageConfig()));
        Assert.Contains("ORIGINAL FOR RECIPIENT", orig);
    }

    // ============================================================ L1-03 — the band on a document we do not issue

    /// <summary>
    /// <b>A recipient-side record carries NO Rule 48(1) copy marking.</b> The markings are prescribed for the
    /// invoice the SUPPLIER prepares (Rule 48(1) over §31(1) / Rule 46); a page recording a supply made TO us is
    /// none of his three copies, and a print of it claiming to be one asserts we issued the document. This is the
    /// one issuer particular slice S2 did not suppress.
    /// <para><b>Bite:</b> restore the ungated <c>if (config.CopyMarking != CopyMarking.None)</c> in
    /// <c>InvoicePdf.DrawFirstHeader</c> and all three rows go red.</para>
    /// </summary>
    [Theory]
    [InlineData(CopyMarking.Original)]
    [InlineData(CopyMarking.Duplicate)]
    [InlineData(CopyMarking.Triplicate)]
    public void A_recipient_record_carries_no_Rule_48_copy_marking(CopyMarking marking)
    {
        string s = AsLatin1(InvoicePdf.Render(RecipientRecord(), new PrintConfig { CopyMarking = marking }, new PageConfig()));

        // The page IS the record — so the absence below is a suppression, not an empty render.
        Assert.Contains(GstReportSupport.PurchaseRecordTitle, s);
        Assert.Contains("5,098.78", s);

        Assert.DoesNotContain("FOR RECIPIENT", s);
        Assert.DoesNotContain("FOR TRANSPORTER", s);
        Assert.DoesNotContain("FOR SUPPLIER", s);
    }

    /// <summary>
    /// <b>THE NEGATIVE CONTROL.</b> A blanket removal of the band satisfies every absence asserted above while
    /// silently deleting a Rule 48(1) marking from the outward invoices that must carry it. Same fixture, same
    /// config, role flag off ⇒ the marking is on the page.
    /// </summary>
    [Theory]
    [InlineData(CopyMarking.Original, "ORIGINAL FOR RECIPIENT")]
    [InlineData(CopyMarking.Duplicate, "DUPLICATE FOR TRANSPORTER")]
    [InlineData(CopyMarking.Triplicate, "TRIPLICATE FOR SUPPLIER")]
    public void The_suppression_is_gated_an_outward_invoice_still_carries_its_marking(CopyMarking marking, string expected)
    {
        string s = AsLatin1(InvoicePdf.Render(OutwardTaxInvoice(), new PrintConfig { CopyMarking = marking }, new PageConfig()));
        Assert.Contains(expected, s);
    }

    /// <summary>
    /// <b>The gate is the ISSUER axis, not the role axis</b> — <see cref="InvoicePrintData.StatesOurDeclarationAndSignature"/>,
    /// the same axis <c>InvoicePdf</c> drops the Rule 46(q) signature on. Rule 48(1)'s markings and Rule 46(q)'s
    /// signature are the same question ("is this a copy of a document WE issued?"), and answering them off two
    /// different flags is how this one leaked. It also decides the shapes that are coming: on slice S5's §31(3)(f)
    /// self-invoice the role is <c>Recorded</c> and WE are the issuer, so the markings belong on it — which a
    /// role-axis gate would wrongly suppress.
    /// </summary>
    [Fact]
    public void A_record_we_do_issue_keeps_its_copy_marking()
    {
        var selfIssued = new InvoicePrintData
        {
            IsRecipientRecord = true,                        // role: still a record …
            StatesOurDeclarationAndSignature = true,         // … but WE issue it (the §31(3)(f) shape)
            DocumentTitle = GstReportSupport.PurchaseRecordTitle,
            Seller = OutwardTaxInvoice().Seller,
            Buyer = OutwardTaxInvoice().Buyer,
            InvoiceNumber = "SELF-0001",
            InvoiceDateText = "10-04-2025",
            IsInterState = false,
            Items = OutwardTaxInvoice().Items,
            TaxRows = OutwardTaxInvoice().TaxRows,
            TotalTaxable = OutwardTaxInvoice().TotalTaxable,
            TotalCgst = OutwardTaxInvoice().TotalCgst,
            TotalSgst = OutwardTaxInvoice().TotalSgst,
            TotalIgst = OutwardTaxInvoice().TotalIgst,
        };

        string s = AsLatin1(InvoicePdf.Render(selfIssued, new PrintConfig { CopyMarking = CopyMarking.Triplicate }, new PageConfig()));
        Assert.Contains("TRIPLICATE FOR SUPPLIER", s);
    }
}
