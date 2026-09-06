using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Io;
using Apex.Ledger.Reports;

namespace Apex.Ledger.Io.Tests;

/// <summary>
/// Tests for <see cref="PaymentAdvicePdf"/> — the <b>supplier</b> payment advice letter (catalog §8 Banking;
/// census row 8.7).
///
/// <para><b>Vendor grounding.</b> <c>help.tallysolutions.com/payment-advice/</c>: the printed advice carries
/// "Invoice-wise details, such as: Invoice numbers, Amounts paid, Deductions (if any), TDS details, Payment mode
/// (NEFT, RTGS, cheque, etc.)", the party's address, and whether the payment is "matched (reconciled) or not";
/// and the report offers <i>"Print each transaction on a fresh page"</i>.</para>
///
/// <para><b>🔴 This is NOT the payroll payment advice.</b> <c>Reports.PaymentAdvice</c> is the payroll bank
/// advice (employee / bank / IFSC / net pay) and is a different document for a different counterparty — the
/// conflation census row 8.7 exists to record.</para>
/// </summary>
public sealed class PaymentAdvicePdfTests
{
    private static string AsLatin1(byte[] bytes) => Encoding.Latin1.GetString(bytes);

    private static SupplierPaymentAdviceRow Advice(
        string party = "Acme Supplies",
        decimal gross = 30000m,
        decimal tds = 0m,
        BankTransactionType? mode = BankTransactionType.NEFT,
        DateOnly? bankDate = null,
        IReadOnlyList<SupplierPaymentAdviceBill>? bills = null) =>
        new(
            VoucherId: Guid.NewGuid(),
            VoucherNumber: 7,
            FormattedNumber: "7",
            Date: new DateOnly(2026, 5, 20),
            PartyLedgerId: Guid.NewGuid(),
            PartyName: party,
            AddresseeName: party,
            AddressLines: new[] { "14 Park Street", "Kolkata", "700016" },
            GrossAmount: Money.FromRupees(gross),
            TdsDeducted: Money.FromRupees(tds),
            NetPaid: Money.FromRupees(gross - tds),
            PaymentMode: mode,
            InstrumentNumber: "UTR77",
            InstrumentDate: new DateOnly(2026, 5, 20),
            BankLedgerId: Guid.NewGuid(),
            BankName: "HDFC Bank",
            BankDate: bankDate,
            Bills: bills ?? new[]
            {
                new SupplierPaymentAdviceBill("INV-001", BillRefType.AgstRef, Money.FromRupees(18000m),
                    new DateOnly(2026, 6, 19)),
                new SupplierPaymentAdviceBill("INV-002", BillRefType.AgstRef, Money.FromRupees(12000m), null),
            });

    private static byte[] Render(
        IReadOnlyList<SupplierPaymentAdviceRow> advices, bool freshPageEach = true) =>
        PaymentAdvicePdf.Render(advices, "Apex Solutions", "12 MG Road\nKolkata", new PageConfig(), freshPageEach);

    [Fact]
    public void Renders_a_valid_debranded_pdf()
    {
        var s = AsLatin1(Render(new[] { Advice() }));
        Assert.StartsWith("%PDF-", s);
        Assert.Contains("%%EOF", s);
        Assert.DoesNotContain("tally", s.ToLowerInvariant());   // ER-11 brand guard (body AND /Title)
    }

    /// <summary>
    /// The letter carries every element the vendor's configuration list names: the addressee and address, the
    /// bill-wise invoice numbers and amounts, the payment mode with its instrument, and the reconciled status.
    /// </summary>
    [Fact]
    public void The_letter_carries_the_addressee_the_bill_wise_detail_the_mode_and_the_status()
    {
        var s = AsLatin1(Render(new[] { Advice(bankDate: new DateOnly(2026, 5, 22)) }));

        Assert.Contains("Acme Supplies", s);
        Assert.Contains("14 Park Street", s);
        Assert.Contains("700016", s);

        Assert.Contains("PAYMENT ADVICE", s);
        Assert.Contains("Payment Mode:", s);
        Assert.Contains("NEFT", s);
        Assert.Contains("Instrument No:", s);
        Assert.Contains("UTR77", s);
        Assert.Contains("HDFC Bank", s);

        Assert.Contains("INV-001", s);
        Assert.Contains("INV-002", s);
        Assert.Contains("18,000.00", s);
        Assert.Contains("12,000.00", s);
        Assert.Contains("30,000.00", s);

        // The report's headline fact, stated in the letter too.
        Assert.Contains("Cleared on 22-05-2026", s);

        // Amount in words comes from the shared converter — not a second implementation.
        Assert.Contains(IndianAmountInWords.Convert(30000m), s);
    }

    /// <summary>An uncleared payment says so, in words, rather than leaving the operator to infer it.</summary>
    [Fact]
    public void An_uncleared_payment_reads_as_not_yet_cleared()
    {
        var s = AsLatin1(Render(new[] { Advice() }));
        Assert.Contains("Not yet cleared", s);
        Assert.DoesNotContain("Cleared on", s);
    }

    /// <summary>
    /// 🔴 "Deductions (if any)". A deduction line is printed only when there IS one — an advice for a payment
    /// carrying no withholding must not imply a TDS of zero, which reads to a supplier as tax having been
    /// deducted.
    /// </summary>
    [Fact]
    public void The_tds_line_appears_only_when_tax_was_actually_deducted()
    {
        var none = AsLatin1(Render(new[] { Advice(tds: 0m) }));
        Assert.DoesNotContain("Tax Deducted at Source", none);

        var withTds = AsLatin1(Render(new[] { Advice(gross: 30000m, tds: 3000m) }));
        Assert.Contains("Less: Tax Deducted at Source", withTds);
        Assert.Contains("3,000.00", withTds);
        Assert.Contains("27,000.00", withTds);      // net = gross − deduction
    }

    /// <summary>A payment made out of cash carries no bank allocation, so it names no mode and no instrument —
    /// the captions are omitted, never printed empty.</summary>
    [Fact]
    public void A_cash_payment_omits_the_mode_caption_rather_than_printing_it_empty()
    {
        var cash = Advice(mode: null) with { InstrumentNumber = string.Empty, InstrumentDate = null, BankName = "" };
        var s = AsLatin1(Render(new[] { cash }));

        Assert.DoesNotContain("Payment Mode:", s);
        Assert.DoesNotContain("Instrument No:", s);
        Assert.DoesNotContain("Bank:", s);
        Assert.Contains("Acme Supplies", s);        // the letter is still a letter
    }

    /// <summary>A payment with bill-by-bill off is a legitimate posting; the table says so instead of being an
    /// empty ruled box the operator has to interpret.</summary>
    [Fact]
    public void A_payment_with_no_bill_wise_allocation_says_so()
    {
        var s = AsLatin1(Render(new[] { Advice(bills: Array.Empty<SupplierPaymentAdviceBill>()) }));
        // "(" and ")" are PDF string delimiters, so the writer escapes them in the content stream.
        Assert.Contains(@"\(no bill-wise detail recorded for this payment\)", s);
    }

    /// <summary>
    /// The vendor's "Print each transaction on a fresh page". On ⇒ one letter per page; off ⇒ they share a page
    /// whenever the next one FITS on it.
    ///
    /// <para><b>🔴 This assertion was corrected, and the correction is the point.</b> It used to read
    /// <c>Assert.Equal(1, PageCount(Render(three, freshPageEach: false)))</c> — three full A4 letters certified
    /// onto a single sheet, which is not a layout the paper can hold. It was asserting the overflow defect that
    /// <c>Flowing_advices_break_the_page_instead_of_running_off_the_bottom_of_it</c> measures at y = -820: a
    /// page count cannot distinguish "packed tightly" from "drawn off the bottom edge". So the flowing arm is
    /// now driven with COMPACT advices that genuinely fit two to a sheet, and it asserts what the operator
    /// actually gets — fewer sheets than one-per-letter, and never more.</para>
    /// </summary>
    [Fact]
    public void Print_each_transaction_on_a_fresh_page_controls_the_page_count()
    {
        var three = new[] { Advice("A Ltd"), Advice("B Ltd"), Advice("C Ltd") };
        Assert.Equal(3, PageCount(Render(three, freshPageEach: true)));

        // A short letter — no address block, no instrument or bank captions, one bill line — so that flowing
        // really can put more than one on a sheet. Three of these are two sheets; one per letter would be three.
        var compact = new[] { Compact("A Ltd"), Compact("B Ltd"), Compact("C Ltd") };
        Assert.Equal(3, PageCount(Render(compact, freshPageEach: true)));
        Assert.Equal(2, PageCount(Render(compact, freshPageEach: false)));

        static int PageCount(byte[] pdf) =>
            Regex.Matches(Encoding.Latin1.GetString(pdf), @"/Type\s*/Page[^s]").Count;
    }

    /// <summary>A deliberately short advice: no address lines, no payment mode, no instrument, no bank, no
    /// bill-wise detail — the smallest letter this renderer produces.</summary>
    private static SupplierPaymentAdviceRow Compact(string party) =>
        new(
            VoucherId: Guid.NewGuid(),
            VoucherNumber: 7,
            FormattedNumber: "7",
            Date: new DateOnly(2026, 5, 20),
            PartyLedgerId: Guid.NewGuid(),
            PartyName: party,
            AddresseeName: party,
            AddressLines: Array.Empty<string>(),
            GrossAmount: Money.FromRupees(100m),
            TdsDeducted: Money.Zero,
            NetPaid: Money.FromRupees(100m),
            PaymentMode: null,
            InstrumentNumber: string.Empty,
            InstrumentDate: null,
            BankLedgerId: Guid.Empty,
            BankName: string.Empty,
            BankDate: null,
            Bills: Array.Empty<SupplierPaymentAdviceBill>());

    /// <summary>
    /// 🔴 <b>A LETTER FLOWED OFF THE BOTTOM OF THE PAGE IS NOT PRINTED — IT IS LOST.</b>
    ///
    /// <para>With "print each transaction on a fresh page" OFF the advices flow one after another, and the first
    /// cut of this renderer never broke the page: <c>y</c> decremented monotonically for the whole run, so the
    /// second and third letters were laid down at coordinates BELOW the sheet. The PDF was structurally valid,
    /// the page count was a tidy 1, every string the other tests look for was present in the content stream —
    /// and the operator's printer produced one letter and two blank-ish sheets' worth of nothing. The supplier
    /// never receives an advice that was drawn at y = -300.</para>
    ///
    /// <para>So this asserts the property that actually matters: <b>no text is placed outside the sheet</b>. It
    /// reads the <c>Td</c> placements straight out of the (uncompressed) content stream, which is the only place
    /// the truth is — a page count or a substring search cannot see this defect at all, and the page-count test
    /// above was in fact asserting the broken shape.</para>
    /// </summary>
    [Fact]
    public void Flowing_advices_break_the_page_instead_of_running_off_the_bottom_of_it()
    {
        var page = new PageConfig();
        var many = new[] { Advice("A Ltd"), Advice("B Ltd"), Advice("C Ltd"), Advice("D Ltd") };
        var pdf = PaymentAdvicePdf.Render(many, "Apex Solutions", "12 MG Road\nKolkata", page, freshPageEach: false);
        var s = AsLatin1(pdf);

        var ys = Regex.Matches(s, @"(-?\d+(?:\.\d+)?) (-?\d+(?:\.\d+)?) Td")
            .Select(m => double.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture))
            .ToList();
        Assert.NotEmpty(ys);

        // Nothing below the sheet, and nothing above it either.
        Assert.True(ys.Min() >= 0, $"text was placed at y={ys.Min()}, off the bottom of the sheet");
        Assert.True(ys.Max() <= page.PageHeight,
            $"text was placed at y={ys.Max()}, above the top of the sheet ({page.PageHeight})");

        // And every letter really is in the document — breaking the page must not drop one.
        foreach (var party in new[] { "A Ltd", "B Ltd", "C Ltd", "D Ltd" })
            Assert.Contains(party, s, StringComparison.Ordinal);
    }

    /// <summary>
    /// 🔴 <b>ONE letter can be longer than one sheet, and the money at the bottom of it was being drawn off the
    /// paper.</b>
    ///
    /// <para>The flowing-mode guard above only ever fired BETWEEN letters. Inside a letter the cursor decremented
    /// monotonically from the letterhead to the signature, so an advice carrying eighty bills — an ordinary month
    /// for a supplier paid on account — put its bill table, its <b>Gross</b>, its <b>Less: TDS</b>, its <b>Net
    /// Amount Paid</b> and the signatory at negative coordinates. The sheet that reached the supplier stopped
    /// mid-table with no total on it. Nothing failed: the PDF was valid, the page count was 1, and every
    /// substring these tests look for was present in the content stream, because a string drawn at y = −400 is
    /// still in the stream.</para>
    ///
    /// <para>So this reads the actual <c>Td</c> placements — the only place the truth is — and additionally
    /// requires that the continuation sheet be usable: it names the letter it belongs to and repeats the
    /// bill-table caption band, because a column of bare figures under no headings is not a bill table.</para>
    /// </summary>
    [Fact]
    public void A_letter_longer_than_one_sheet_breaks_the_page_instead_of_running_off_the_bottom()
    {
        var page = new PageConfig();
        var many = Enumerable.Range(1, 80)
            .Select(i => new SupplierPaymentAdviceBill(
                $"INV-{i:000}", BillRefType.AgstRef, Money.FromRupees(100m + i), new DateOnly(2026, 6, 19)))
            .ToArray();

        var pdf = PaymentAdvicePdf.Render(
            new[] { Advice(gross: 30000m, tds: 3000m, bills: many) },
            "Apex Solutions", "12 MG Road\nKolkata", page, freshPageEach: true);
        var s = AsLatin1(pdf);

        var ys = Regex.Matches(s, @"(-?\d+(?:\.\d+)?) (-?\d+(?:\.\d+)?) Td")
            .Select(m => double.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture))
            .ToList();
        Assert.NotEmpty(ys);
        Assert.True(ys.Min() >= 0, $"text was placed at y={ys.Min()}, off the bottom of the sheet");
        Assert.True(ys.Max() <= page.PageHeight, $"text was placed at y={ys.Max()}, above the top of the sheet");

        // It really did take more than one sheet — a "fix" that merely squeezed the type would not.
        Assert.True(PageCountOf(pdf) > 1, "an eighty-bill advice must occupy more than one sheet");

        // And the tail of the letter — the part that was being lost — is on the paper.
        Assert.Contains("INV-080", s);
        Assert.Contains("Net Amount Paid", s);
        Assert.Contains("Less: Tax Deducted at Source", s);
        Assert.Contains("Authorised Signatory", s);

        // The continuation sheet is identifiable and its table is captioned.
        Assert.Contains("continued", s);
        Assert.True(Regex.Matches(s, "Bill / Reference").Count > 1,
            "the bill-table caption band must be repeated on the continuation sheet");
    }

    /// <summary>
    /// Every sheet of a multi-sheet run is footed with its OWN page number. The footer replacements were
    /// hard-coded to <c>1</c> and <c>1</c>, so a three-letter run printed "Page 1 of 1" three times — the reader
    /// cannot tell a complete run from a lost sheet. Same contract <c>ReportPdf</c> and <c>VoucherPdf</c> keep.
    /// </summary>
    [Fact]
    public void Every_sheet_of_a_multi_sheet_run_is_footed_with_its_own_page_number()
    {
        var page = new PageConfig { FooterText = "Page {page} of {pages}" };
        var s = AsLatin1(PaymentAdvicePdf.Render(
            new[] { Advice("A Ltd"), Advice("B Ltd"), Advice("C Ltd") },
            "Apex Solutions", "12 MG Road\nKolkata", page, freshPageEach: true));

        Assert.Contains("Page 1 of 3", s);
        Assert.Contains("Page 2 of 3", s);
        Assert.Contains("Page 3 of 3", s);
        Assert.DoesNotContain("Page 1 of 1", s);
    }

    /// <summary>
    /// 🔴 <b>THE DE-BRANDING GUARD MUST NOT REWRITE THE SUPPLIER'S OWN LEGAL NAME.</b>
    ///
    /// <para><c>Debrand.Text</c> strips a case-insensitive vendor token, which is right for text this product
    /// owns and catastrophic for a counterparty's identity: it was applied to the addressee, the address block,
    /// the bank and the bill references, so a letter to <b>Metally Traders Pvt Ltd</b> went out addressed to
    /// "Me Traders Pvt Ltd", quoting bill "/2026/001" against a bank called "gunge Co-operative Bank". Every one
    /// of those is a wrong figure of a different kind on a document sent to somebody else — the supplier cannot
    /// find the bill in its own ledger, and the letter is not even addressed to it.</para>
    ///
    /// <para>The guard still runs on what is OURS, which the letterhead assertion pins: the company name is
    /// de-branded on the same page on which the supplier's is not.</para>
    /// </summary>
    [Fact]
    public void The_suppliers_own_legal_name_address_bank_and_bill_refs_are_never_debranded()
    {
        var counterparty = Advice() with
        {
            AddresseeName = "Metally Traders Pvt Ltd",
            AddressLines = new[] { "7 Tallygunge Circular Road", "Kolkata" },
            BankName = "Tallygunge Co-operative Bank",
            Bills = new[]
            {
                new SupplierPaymentAdviceBill(
                    "TALLY/2026/001", BillRefType.AgstRef, Money.FromRupees(30000m), null),
            },
        };

        var s = AsLatin1(PaymentAdvicePdf.Render(
            new[] { counterparty }, "Apex Tally Solutions", "12 MG Road", new PageConfig()));

        Assert.Contains("Metally Traders Pvt Ltd", s);
        Assert.Contains("7 Tallygunge Circular Road", s);
        Assert.Contains("Tallygunge Co-operative Bank", s);
        Assert.Contains("TALLY/2026/001", s);

        // ...while OUR OWN letterhead is still de-branded (ER-11): the company prints as "Apex Solutions".
        Assert.DoesNotContain("Apex Tally Solutions", s);
        Assert.Contains("Apex Solutions", s);
    }

    private static int PageCountOf(byte[] pdf) =>
        Regex.Matches(Encoding.Latin1.GetString(pdf), @"/Type\s*/Page[^s]").Count;

    /// <summary>An empty run must READ as "no payments", never as a blank sheet.</summary>
    [Fact]
    public void An_empty_run_says_there_were_no_payments_instead_of_printing_a_blank_sheet()
    {
        var s = AsLatin1(Render(Array.Empty<SupplierPaymentAdviceRow>()));
        Assert.Contains("No supplier payments in this period.", s);
        Assert.Contains("Apex Solutions", s);
        Assert.StartsWith("%PDF-", s);
    }

    [Fact]
    public void Rendering_the_same_advices_twice_is_byte_identical()
    {
        var advices = new[] { Advice("A Ltd"), Advice("B Ltd") };
        Assert.Equal(Render(advices), Render(advices));
    }

    /// <summary>
    /// The letter must not depend on the ambient culture: a decimal-comma locale would change every money figure
    /// and every coordinate in the content stream. This is the class of platform assumption that has escaped the
    /// gate seven times on this project.
    /// </summary>
    [Fact]
    public void Rendering_is_culture_invariant()
    {
        var advices = new[] { Advice(gross: 1234567.89m, tds: 12345.67m) };
        var original = CultureInfo.CurrentCulture;
        try
        {
            var reference = Render(advices);
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            Assert.Equal(reference, Render(advices));
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }
}
