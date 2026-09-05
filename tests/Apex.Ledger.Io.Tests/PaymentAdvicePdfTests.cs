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
    /// The vendor's "Print each transaction on a fresh page". On ⇒ one letter per page; off ⇒ they flow together.
    /// </summary>
    [Fact]
    public void Print_each_transaction_on_a_fresh_page_controls_the_page_count()
    {
        var three = new[] { Advice("A Ltd"), Advice("B Ltd"), Advice("C Ltd") };

        Assert.Equal(3, PageCount(Render(three, freshPageEach: true)));
        Assert.Equal(1, PageCount(Render(three, freshPageEach: false)));

        static int PageCount(byte[] pdf) =>
            Regex.Matches(Encoding.Latin1.GetString(pdf), @"/Type\s*/Page[^s]").Count;
    }

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
