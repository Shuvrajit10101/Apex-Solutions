using System.Text;
using Apex.Ledger;
using Apex.Ledger.Io;
using Apex.Ledger.Services;
using Xunit;

namespace Apex.Ledger.Io.Tests;

/// <summary>
/// The shipped documents group rupees the Indian (lakh/crore) way — drift lock D2, asserted at the PDF level
/// where the defect actually lived.
///
/// <para><b>Why these tests did not exist before, and why that mattered.</b> Every pre-existing invoice, POS and
/// voucher fixture used amounts BELOW ₹1,00,000, where Indian and Western grouping are byte-identical. The whole
/// suite therefore passed with the tax invoice printing "100,000.00" and a Form-16A certificate printing
/// "1,00,000.00" from the same assembly. A grouping rule can only be tested by a fixture large enough to group —
/// so these fixtures are lakh- and crore-scale, and carry ODD PAISA so no rounding coincidence can mask them.</para>
///
/// <para><b>Sourced (R7):</b> Tally exposes digit style as the currency-master flag "Show Amounts in Millions"
/// (<c>664311548-Tally-Prime-Book.pdf</c>, company-creation field list at author page 9 and the Currency Create
/// field list, read with <c>pdftotext -layout</c>). Millions is the explicit opt-in, so Indian grouping is the
/// default; and because the flag lives on the CURRENCY it cannot differ between an invoice and a certificate.</para>
/// </summary>
public sealed class IndianDigitGroupingTests
{
    private static string AsLatin1(byte[] bytes) => Encoding.Latin1.GetString(bytes);

    private static string ValidGstin(string first14) =>
        first14 + Apex.Ledger.Domain.Gstin.ComputeCheckDigit(first14 + "0");

    /// <summary>A lakh-scale intra-state tax invoice: taxable ₹12,34,567.89 @ 18%.</summary>
    private static InvoicePrintData LakhScaleInvoice()
    {
        var taxable = new Money(1234567.89m);
        var tax = GstService.ComputeLineTax(taxable, 1800, interState: false);

        return new InvoicePrintData
        {
            Seller = new InvoicePartyBlock { Name = "Bright Traders", Gstin = ValidGstin("19AAAAA0000A1Z"), StateText = "West Bengal (19)" },
            Buyer = new InvoicePartyBlock { Name = "Acme Retail", Gstin = ValidGstin("19CCCCC0000C1Z"), StateText = "West Bengal (19)" },
            InvoiceNumber = "INV-LAKH-1",
            InvoiceDateText = "31-03-2025",
            PlaceOfSupply = "West Bengal (19)",
            IsInterState = false,
            Items = new[]
            {
                new InvoiceItemRow
                {
                    Description = "Widget", HsnSac = "84713010",
                    // RateText is written into the PDF VERBATIM by InvoicePdf (it is a pre-formatted string the
                    // projector supplies), so it must NOT be the value under test: a fixture that hard-codes
                    // "12,34,567.89" here puts that byte sequence in the PDF itself and every Contains assertion
                    // on it passes even with the formatter reverted to the invariant culture. Keep it a value the
                    // formatter cannot produce for any asserted amount, so the only possible source of a grouped
                    // lakh figure in the rendered PDF is Fmt().
                    QuantityText = "1.000", RateText = "rate-not-under-test", TaxableValue = taxable,
                },
            },
            TaxRows = new[]
            {
                new InvoiceTaxRow { RateLabel = "18%", TaxableValue = taxable, Cgst = tax.Cgst, Sgst = tax.Sgst, Igst = Money.Zero },
            },
            TotalTaxable = taxable,
            TotalCgst = tax.Cgst,
            TotalSgst = tax.Sgst,
            TotalIgst = Money.Zero,
        };
    }

    /// <summary>
    /// The tax invoice groups the Indian way. ₹12,34,567.89 must print as "12,34,567.89"; the Western
    /// "1,234,567.89" — what the invariant culture produced before this slice — must appear nowhere.
    ///
    /// <para>Two independently-formatted numbers are pinned, not one: the taxable total AND the CGST component
    /// (₹12,34,567.89 × 9% = ₹1,11,111.11). Both come from <c>InvoicePdf.Fmt</c> and neither is supplied by the
    /// fixture, so this cannot pass on a hard-coded string the way the earlier version could.</para>
    /// </summary>
    [Fact]
    public void TaxInvoiceGroupsRupeesTheIndianWay()
    {
        var pdf = AsLatin1(InvoicePdf.Render(LakhScaleInvoice(), new PrintConfig(), new PageConfig()));

        Assert.Contains("12,34,567.89", pdf);
        Assert.DoesNotContain("1,234,567.89", pdf);

        Assert.Contains("1,11,111.11", pdf);   // CGST @ 9%
        Assert.DoesNotContain("111,111.11", pdf);
    }

    /// <summary>
    /// The invoice and the statutory certificate — rendered from the SAME assembly — group identically. This is
    /// the exact contradiction that existed before: <c>CertificatePdfSupport</c>'s doc comment claimed it mirrored
    /// <c>InvoicePdf</c>, while the two in fact disagreed on every amount of a lakh or more.
    ///
    /// <para><b>This calls the real certificate formatter.</b> An earlier version routed through a local helper
    /// that was itself <c>IndianMoneyFormat.Amount</c> — the shared rule, not the certificate — so it was
    /// structurally incapable of observing <c>CertificatePdfSupport</c> and would have stayed green if the
    /// certificate reverted to Western grouping. Its stated reason ("<c>CertificatePdfSupport.Rupees</c> is
    /// internal to Apex.Ledger.Io") was also wrong: <c>Apex.Ledger.Io.csproj</c> carries
    /// <c>&lt;InternalsVisibleTo Include="Apex.Ledger.Io.Tests" /&gt;</c>, so this project can call it directly —
    /// which is what it now does.</para>
    /// </summary>
    [Fact]
    public void TheInvoiceAndTheCertificateAgreeOnGrouping()
    {
        var amount = new Money(1234567.89m);

        var certificate = CertificatePdfSupport.Rupees(amount);
        Assert.Equal("12,34,567.89", certificate);

        var invoice = AsLatin1(InvoicePdf.Render(LakhScaleInvoice(), new PrintConfig(), new PageConfig()));
        Assert.Contains(certificate, invoice);
        Assert.DoesNotContain("1,234,567.89", invoice);
    }

    /// <summary>Crore scale groups 3;2;2 all the way up — ₹1,00,00,000.01, not "10,000,000.01".</summary>
    [Fact]
    public void CroreScaleGroupsThreeTwoTwo()
    {
        Assert.Equal("1,00,00,000.01", IndianMoneyFormat.Amount(10000000.01m));
        Assert.Equal("12,34,56,789.99", IndianMoneyFormat.Amount(123456789.99m));
    }

    // ================================================================ the other two shipped documents

    /// <summary>
    /// <b>The POS receipt groups the Indian way too.</b> This slice changed the printed output of FOUR documents —
    /// the tax invoice, the POS receipt, the printed voucher and the certificate — but only the invoice and the
    /// shared formatter gained a test. Nothing rendered a POS receipt above ₹1,00,000, so
    /// <c>PosReceiptPdf.Fmt</c> was protected only by the TEXTUAL invariant-culture drift lock — which does not
    /// match <c>ToString("N2")</c> (current culture), <c>ToString("#,##0.00")</c> or any other host-bound form.
    /// A revert to any of those would print "1,234,567.89" on the receipt while the tax invoice for the same sale
    /// printed "12,34,567.89", and the whole suite would stay green.
    ///
    /// <para>Every asserted figure is produced by the formatter, not supplied by the fixture: <c>RateText</c> is
    /// written verbatim by the renderer, so it is deliberately not a groupable number here.</para>
    /// </summary>
    [Fact]
    public void PosReceiptGroupsRupeesTheIndianWay()
    {
        var taxable = new Money(1234567.89m);
        var tax = GstService.ComputeLineTax(taxable, 1800, interState: false);
        var grand = new Money(taxable.Amount + tax.Cgst.Amount + tax.Sgst.Amount);

        var receipt = new PosReceiptData
        {
            Title = "Retail Invoice",
            StoreName = "Apex Retail Co",
            BillNumber = "LAKH-1",
            DateText = "31-03-2025",
            Party = "(cash)",
            IsInterState = false,
            Items = new[]
            {
                new PosReceiptItem
                {
                    Description = "Widget", QuantityText = "1",
                    RateText = "rate-not-under-test", Value = taxable,
                },
            },
            TaxRows = new[]
            {
                new PosReceiptTaxRow
                {
                    RateLabel = "18%", TaxableValue = taxable,
                    Cgst = tax.Cgst, Sgst = tax.Sgst, Igst = Money.Zero,
                },
            },
            Tenders = new[] { new PosReceiptTender { Label = "Cash", Amount = grand } },
            TotalTaxable = taxable,
            TotalCgst = tax.Cgst,
            TotalSgst = tax.Sgst,
            TotalIgst = Money.Zero,
            CashTendered = grand,
            Change = Money.Zero,
        };

        var pdf = AsLatin1(PosReceiptPdf.Render(receipt, new PageConfig()));

        Assert.Contains("12,34,567.89", pdf);        // taxable
        Assert.DoesNotContain("1,234,567.89", pdf);
        Assert.Contains("1,11,111.11", pdf);         // CGST @ 9%
        Assert.DoesNotContain("111,111.11", pdf);
    }

    /// <summary>
    /// <b>The printed voucher groups the Indian way too</b> — the fourth changed document, and likewise untested
    /// at any scale that could group. Odd paisa, and the totals row is formatted independently of the line rows,
    /// so two separately-formatted numbers are pinned.
    /// </summary>
    [Fact]
    public void PrintedVoucherGroupsRupeesTheIndianWay()
    {
        var voucher = new VoucherPrintData
        {
            CompanyName = "Bright Traders",
            VoucherTypeName = "Payment",
            VoucherNumber = "LAKH-1",
            DateText = "31-03-2025",
            PartyName = "",
            Lines = new[]
            {
                new VoucherPrintLine { LedgerName = "Rent A/c", IsDebit = true, Amount = new Money(1234567.89m) },
                new VoucherPrintLine { LedgerName = "Cash-in-Hand", IsDebit = false, Amount = new Money(1234567.89m) },
            },
            Narration = "Lakh-scale grouping fixture",
        };

        var pdf = AsLatin1(VoucherPdf.Render(voucher, new PrintConfig(), new PageConfig()));

        Assert.Contains("12,34,567.89", pdf);
        Assert.DoesNotContain("1,234,567.89", pdf);
    }
}
