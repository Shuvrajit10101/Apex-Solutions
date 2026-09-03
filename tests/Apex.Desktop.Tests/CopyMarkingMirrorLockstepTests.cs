using System.Collections.Generic;
using System.Linq;
using System.Text;
using Apex.Ledger;
using Apex.Ledger.Io;
using Apex.Ledger.Reports;
using Apex.Ledger.Services;
using Apex.Desktop.ViewModels;

namespace Apex.Desktop.Tests;

/// <summary>
/// The CGST Rule 48(1) copy marking, held in LOCKSTEP between the bytes that leave the building and the pane the
/// operator approves (T0-11 review C3/L1-03).
///
/// <para><b>🔴 WHY A LOCKSTEP TEST AND NOT TWO SUPPRESSION TESTS.</b> The band leaked onto a recipient-side record
/// from TWO sites — <c>InvoicePdf.DrawFirstHeader</c> and <c>PrintPreviewViewModel.BuildInvoicePreviewReport</c> —
/// each an ungated <c>if (CopyMarking != None)</c>. Fixing either alone produces the preview/paper drift this same
/// review found elsewhere (L1-05), and which the mirror's own comments name as the thing to avoid: "if the mirror
/// and the bytes disagreed the operator would approve one document and issue another". So the property asserted
/// here is the AGREEMENT itself, on every marking and on both document roles — it goes red if either site is
/// changed without the other, in either direction.</para>
///
/// <para>The statutory ground for the suppression and for the corrected label spellings, with the verbatim CBIC
/// text, is in <c>Apex.Ledger.Io.Tests/CopyMarkingRule48Tests.cs</c>; this file does not restate it.</para>
///
/// <para>Built from hand-made DTOs straight into <see cref="PrintPreviewViewModel"/>: the projector and the
/// database are not in the path, because what is under test is the two RENDERINGS of one DTO agreeing.</para>
/// </summary>
public sealed class CopyMarkingMirrorLockstepTests
{
    private static string AsLatin1(byte[] bytes) => Encoding.Latin1.GetString(bytes);

    private static string ValidGstin(string first14) => first14 + Apex.Ledger.Domain.Gstin.ComputeCheckDigit(first14 + "0");

    /// <summary>Taxable ₹4,321.00 @ 18% intra-State ⇒ CGST 388.89 + SGST 388.89, grand 5,098.78.</summary>
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

    /// <summary>The same supply recorded from the recipient's side — the role flag is the difference.</summary>
    private static InvoicePrintData RecipientRecord()
    {
        var outward = OutwardTaxInvoice();
        return new InvoicePrintData
        {
            IsRecipientRecord = true,
            DocumentTitle = GstReportSupport.PurchaseRecordTitle,
            Seller = outward.Buyer,                          // the supplier heads a record (Rule 46(a))
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

    private static IReadOnlyList<string> MirrorCells(PrintPreviewViewModel vm) =>
        vm.Pages.SelectMany(p => p.Lines).SelectMany(l => l.Cells).ToList();

    /// <summary>
    /// <b>The mirror and the bytes make the same statement about the copy marking, on every marking and on both
    /// roles.</b> Nine cells of one truth table, asserted as an equality rather than as two lists of expectations,
    /// so a one-sided edit cannot pass.
    /// </summary>
    [Theory]
    [InlineData(true, CopyMarking.None)]
    [InlineData(true, CopyMarking.Original)]
    [InlineData(true, CopyMarking.Duplicate)]
    [InlineData(true, CopyMarking.Triplicate)]
    [InlineData(false, CopyMarking.None)]
    [InlineData(false, CopyMarking.Original)]
    [InlineData(false, CopyMarking.Duplicate)]
    [InlineData(false, CopyMarking.Triplicate)]
    public void The_screen_and_the_paper_agree_about_the_copy_marking(bool record, CopyMarking marking)
    {
        var vm = new PrintPreviewViewModel(record ? RecipientRecord() : OutwardTaxInvoice())
        {
            CopyMarking = marking,
        };

        string label = new PrintConfig { CopyMarking = marking }.CopyMarkingLabel;
        var cells = MirrorCells(vm);

        // Sanity: the pane really is the document under test, so an absence below is a suppression, not an empty pane.
        Assert.Contains(record ? GstReportSupport.PurchaseRecordTitle : GstReportSupport.TaxInvoiceTitle,
            vm.Pages.Select(p => p.Title));

        bool onPaper = label.Length > 0 && AsLatin1(vm.PdfBytes).Contains(label);
        bool onScreen = label.Length > 0 && cells.Contains(label);
        Assert.Equal(onPaper, onScreen);

        // …and the shared answer is the right one: an issuer's copy is marked, a record we did not issue is not.
        Assert.Equal(!record && marking != CopyMarking.None, onPaper);
    }

    /// <summary>
    /// <b>The record's pane carries no copy caption at all</b> — not merely "not the label the config asked for".
    /// A mirror that emitted some other marking string, or that emitted a stale one after the config changed, would
    /// pass the equality above only by the bytes carrying it too; this pins the pane directly.
    /// </summary>
    [Theory]
    [InlineData(CopyMarking.Original)]
    [InlineData(CopyMarking.Duplicate)]
    [InlineData(CopyMarking.Triplicate)]
    public void A_purchase_records_approval_pane_shows_no_copy_marking(CopyMarking marking)
    {
        var vm = new PrintPreviewViewModel(RecipientRecord()) { CopyMarking = marking };

        foreach (var cell in MirrorCells(vm))
        {
            Assert.DoesNotContain("FOR RECIPIENT", cell);
            Assert.DoesNotContain("FOR TRANSPORTER", cell);
            Assert.DoesNotContain("FOR SUPPLIER", cell);
        }

        // The negative control on the same pane machinery: the outward document DOES show it.
        var outward = new PrintPreviewViewModel(OutwardTaxInvoice()) { CopyMarking = marking };
        Assert.Contains(new PrintConfig { CopyMarking = marking }.CopyMarkingLabel, MirrorCells(outward));
    }
}
