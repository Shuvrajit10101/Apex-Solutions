using System;
using System.Collections.Generic;
using System.Text;
using Apex.Ledger;
using Apex.Ledger.Io;
using Apex.Ledger.Reports;
using Apex.Ledger.Services;

namespace Apex.Ledger.Io.Tests;

/// <summary>
/// <b>W2-31 / census row 12.4 — the F10 page RANGE and STARTING NUMBER on the four DOCUMENT renderers.</b>
///
/// <para><b>🔴 WHY THIS FILE EXISTS.</b> Row 12.4 landed `PARTIAL` with a precise and honest description of its
/// own gap: <c>ReportPdf</c> honours every knob end-to-end, while <c>InvoicePdf</c>, <c>VoucherPdf</c>,
/// <c>PayslipPdf</c> and <c>PosReceiptPdf</c> honour <b>only the copy count</b>. The panel was fixed by
/// WITHDRAWING the inert knobs (<c>PrintConfigViewModel.SupportsPageKnobs</c>) rather than by implementing them
/// — the honest half. This file is the other half: the renderers learn the range, after which the predicate can
/// widen and the caption lock guards the pairing instead of forbidding it.</para>
///
/// <para><b>The rule, stated once and applied by all four</b> — it is <c>ReportPdf</c>'s, deliberately, so a
/// range means the same thing whatever is being printed:
/// <list type="bullet">
///   <item><c>StartPageNumber</c> RENUMBERS: sheet one carries that number, so a continuation document reads
///     "Page 7 of 10". It does not select anything.</item>
///   <item>The RANGE SELECTS which sheets are drawn and never renumbers them — the operator is holding sheet 3
///     of a 4-sheet document, not sheet 1 of a 2-sheet one.</item>
///   <item>A range that selects nothing yields ONE BLANK SHEET, never the whole document. Silently falling back
///     to "print everything" is the failure this guards against, and a PDF must carry at least one page.</item>
///   <item>The defaults reproduce the shipped bytes exactly (ER-13).</item>
/// </list></para>
///
/// <para>🔴 <b>AND THE HEADER ABOVE USED TO BE FALSE.</b> This file said "the four DOCUMENT renderers" and named
/// all four, while holding five facts for <c>VoucherPdf</c>, three for <c>PosReceiptPdf</c> and <b>none at all</b>
/// for <c>InvoicePdf</c> or <c>PayslipPdf</c> — so <c>PayslipPdf</c>'s entire <c>IncludesPage(1)</c> blank-sheet
/// path and its <c>StartPageNumber</c> footer renumbering shipped unexercised, and on <c>InvoicePdf</c> the
/// reviewer's mutation of <c>bool isFirst = p == 0;</c> to <c>isFirst = drawn == 0;</c> passed the FULL FOUR-LEG
/// GATE GREEN. A comment claiming coverage that does not exist is worse than no comment: it stops the next
/// reader looking. Both renderers are covered below, and the invoice's first-header rule is pinned by the test
/// that mutation was run against.</para>
/// </summary>
public sealed class DocumentPageRangeTests
{
    private static string AsLatin1(byte[] bytes) => Encoding.Latin1.GetString(bytes);

    /// <summary>Counts page objects, excluding the single "/Type /Pages" tree node.</summary>
    private static int PageCount(byte[] pdf)
    {
        string s = AsLatin1(pdf);
        int count = 0, idx = 0;
        while ((idx = s.IndexOf("/Type /Page", idx, System.StringComparison.Ordinal)) >= 0)
        {
            int after = idx + "/Type /Page".Length;
            if (after >= s.Length || s[after] != 's') count++;
            idx = after;
        }
        return count;
    }

    // ---- fixtures: deterministic, and deliberately long enough to paginate ----

    /// <summary>A payment voucher with <paramref name="lines"/> posting lines — enough to span several sheets.</summary>
    private static VoucherPrintData LongVoucher(int lines)
    {
        var rows = new List<VoucherPrintLine>();
        for (int i = 1; i <= lines; i++)
            rows.Add(new VoucherPrintLine
            {
                LedgerName = $"Expense Head {i:D3}",
                IsDebit = i % 2 == 1,
                Amount = new Money(1000m),
            });
        return new VoucherPrintData
        {
            CompanyName = "Bright Traders",
            VoucherTypeName = "Journal",
            VoucherNumber = "42",
            DateText = "31-03-2025",
            PartyName = string.Empty,
            Lines = rows,
            Narration = "Month-end allocations",
        };
    }

    /// <summary>A minimal deterministic POS receipt — a SINGLE-sheet document, which is the other shape the
    /// rule has to cover.</summary>
    private static PosReceiptData SampleReceipt() => new()
    {
        Title = "RETAIL INVOICE",
        StoreName = "Bright Traders",
        BillNumber = "7",
        DateText = "31-03-2025",
    };

    // ================================================================= VoucherPdf

    [Fact]
    public void The_voucher_fixture_really_paginates_so_the_range_tests_mean_something()
    {
        // Non-vacuity. Without this every range assertion below could be trivially satisfied by a 1-page document.
        Assert.True(PageCount(VoucherPdf.Render(LongVoucher(120), new PrintConfig(), new PageConfig())) >= 3,
            "the 120-line voucher fixture must span at least three sheets");
    }

    [Fact]
    public void A_voucher_page_range_draws_only_the_selected_sheets()
    {
        var all = VoucherPdf.Render(LongVoucher(120), new PrintConfig(), new PageConfig());
        int total = PageCount(all);

        // Sheets 2..3 of a document of `total` sheets ⇒ exactly two sheets.
        var ranged = VoucherPdf.Render(LongVoucher(120), new PrintConfig(),
            new PageConfig { FirstPage = 2, LastPage = 3 });

        Assert.Equal(2, PageCount(ranged));
        Assert.True(total > 2, $"the fixture produced {total} sheet(s); the range must be a real subset");
    }

    [Fact]
    public void A_voucher_range_that_selects_nothing_yields_one_blank_sheet_not_the_whole_document()
    {
        var ranged = VoucherPdf.Render(LongVoucher(120), new PrintConfig(),
            new PageConfig { FirstPage = 900, LastPage = 901 });

        Assert.Equal(1, PageCount(ranged));
    }

    [Fact]
    public void A_voucher_start_page_number_renumbers_the_footer_without_selecting_anything()
    {
        // 7 sheets' worth is not the point: StartPageNumber only renumbers. A 3-sheet voucher starting at 7
        // therefore reads "Page 7 of 9" on its first sheet — 7 + 3 - 1 = 9, derived by hand from the rule.
        var doc = LongVoucher(120);
        int total = PageCount(VoucherPdf.Render(doc, new PrintConfig(), new PageConfig()));
        string s = AsLatin1(VoucherPdf.Render(doc, new PrintConfig(), new PageConfig { StartPageNumber = 7 }));

        Assert.Contains($"Page 7 of {7 + total - 1}", s, System.StringComparison.Ordinal);
        Assert.Equal(total, PageCount(Encoding.Latin1.GetBytes(s)));
    }

    [Fact]
    public void The_voucher_defaults_leave_the_shipped_bytes_untouched()
    {
        var shipped = VoucherPdf.Render(LongVoucher(120), new PrintConfig(), new PageConfig());
        var explicitDefaults = VoucherPdf.Render(LongVoucher(120), new PrintConfig(),
            new PageConfig { FirstPage = 1, LastPage = 0, StartPageNumber = 1 });

        Assert.Equal(shipped, explicitDefaults);
    }

    // ================================================================= PosReceiptPdf (single sheet)

    [Fact]
    public void A_receipt_start_page_number_renumbers_its_only_sheet()
    {
        // A receipt is one sheet, so "Page 4 of 4" is the whole of the rule applied to a single-sheet document:
        // first = 4, last = 4 + 1 - 1 = 4. Derived by hand from the rule, not read off the code.
        string s = AsLatin1(PosReceiptPdf.Render(SampleReceipt(), new PageConfig { StartPageNumber = 4 }));
        Assert.Contains("Page 4 of 4", s, System.StringComparison.Ordinal);
    }

    [Fact]
    public void A_receipt_range_that_excludes_its_only_sheet_yields_a_blank_sheet()
    {
        var ranged = PosReceiptPdf.Render(SampleReceipt(), new PageConfig { FirstPage = 2 });

        Assert.Equal(1, PageCount(ranged));
        // The blank sheet carries none of the receipt's own content — otherwise "excluded" would mean nothing.
        Assert.DoesNotContain("Bright Traders", AsLatin1(ranged), System.StringComparison.Ordinal);
    }

    [Fact]
    public void The_receipt_defaults_leave_the_shipped_bytes_untouched()
    {
        var shipped = PosReceiptPdf.Render(SampleReceipt(), new PageConfig());
        var explicitDefaults = PosReceiptPdf.Render(SampleReceipt(),
            new PageConfig { FirstPage = 1, LastPage = 0, StartPageNumber = 1 });

        Assert.Equal(shipped, explicitDefaults);
    }

    // ================================================================= InvoicePdf
    //
    // The renderer with a STATUTORY first page. Everything below turns on one line of InvoicePdf.Render:
    //     bool isFirst = p == 0;
    // `p` is the sheet's index in the DOCUMENT. Derive it from the count of sheets DRAWN instead and the whole
    // Rule 46(a) / 48(1) / 5(1)(f) header migrates onto whichever sheet the operator happens to reprint.

    private static string ValidGstin(string first14)
        => first14 + Apex.Ledger.Domain.Gstin.ComputeCheckDigit(first14 + "0");

    /// <summary>
    /// An outward intra-State tax invoice with <paramref name="items"/> item rows — long enough to paginate, so
    /// "sheet 2" is a real sheet and not a hypothetical. Deterministic: fixed parties, fixed money, no clock.
    /// </summary>
    private static InvoicePrintData LongTaxInvoice(int items)
    {
        var perLine = new Money(100m);
        var taxable = new Money(100m * items);
        var tax = GstService.ComputeLineTax(taxable, 1800, interState: false);

        var rows = new List<InvoiceItemRow>();
        for (int i = 1; i <= items; i++)
            rows.Add(new InvoiceItemRow
            {
                Description = $"Cotton Bale {i:D3}",
                HsnSac = "520100",
                QuantityText = "1.000",
                RateText = "100.000",
                TaxableValue = perLine,
            });

        return new InvoicePrintData
        {
            DocumentTitle = GstReportSupport.TaxInvoiceTitle,
            Seller = new InvoicePartyBlock
            {
                Name = "Bright Traders",
                AddressLines = new[] { "12 Market Street", "Kolkata" },
                Gstin = ValidGstin("19AAAAA0000A1Z"),
                StateText = "West Bengal (19)",
            },
            Buyer = new InvoicePartyBlock
            {
                Name = "Northern Mills",
                AddressLines = new[] { "9 Mill Road", "Howrah" },
                Gstin = ValidGstin("19EEEEE0000E1Z"),
                StateText = "West Bengal (19)",
            },
            InvoiceNumber = "INV-0042",
            InvoiceDateText = "31-03-2025",
            PlaceOfSupply = "West Bengal (19)",
            IsInterState = false,
            Items = rows,
            TaxRows = new[]
            {
                new InvoiceTaxRow
                {
                    RateLabel = "18%", TaxableValue = taxable,
                    Cgst = tax.Cgst, Sgst = tax.Sgst, Igst = Money.Zero,
                },
            },
            TotalTaxable = taxable,
            TotalCgst = tax.Cgst,
            TotalSgst = tax.Sgst,
            TotalIgst = Money.Zero,
        };
    }

    [Fact]
    public void The_invoice_fixture_really_paginates_so_the_range_tests_mean_something()
    {
        // Non-vacuity, exactly as the voucher fixture has. Every invoice assertion below is about "sheet 2 of
        // several"; on a one-sheet fixture they would all pass for the wrong reason.
        Assert.True(PageCount(InvoicePdf.Render(LongTaxInvoice(120), new PrintConfig(), new PageConfig())) >= 3,
            "the 120-item invoice fixture must span at least three sheets");
    }

    [Fact]
    public void An_invoice_page_range_draws_only_the_selected_sheets()
    {
        var all = InvoicePdf.Render(LongTaxInvoice(120), new PrintConfig(), new PageConfig());
        int total = PageCount(all);

        var ranged = InvoicePdf.Render(LongTaxInvoice(120), new PrintConfig(),
            new PageConfig { FirstPage = 2, LastPage = 3 });

        Assert.Equal(2, PageCount(ranged));
        Assert.True(total > 2, $"the fixture produced {total} sheet(s); the range must be a real subset");
    }

    /// <summary>
    /// 🔴 <b>THE TEST THE F3 MUTATION WAS RUN AGAINST. Reprinting sheet 2 must not FABRICATE a first page.</b>
    ///
    /// <para><b>What is at stake.</b> <c>InvoicePdf.DrawFirstHeader</c> is the only place three statutory
    /// particulars are drawn — the CGST <b>Rule 46(a)</b> supplier and recipient identity blocks, the
    /// <b>Rule 48(1)</b> copy marking, and the <b>Rule 46</b> invoice-number caption. Which sheet gets them is
    /// decided by <c>bool isFirst = p == 0;</c>, where <c>p</c> is the sheet's index in the DOCUMENT. Change it
    /// to the count of sheets drawn — the natural-looking edit once a range exists — and an operator who reprints
    /// sheet 2 of a 3-sheet invoice receives a sheet headed as the first page of a tax invoice, carrying party
    /// blocks and an ORIGINAL FOR RECIPIENT marking, for a page the posted document never contained. He is
    /// holding a document that states something about a supply that is not true, and the figures on it are right,
    /// which is what makes it hard to notice.</para>
    ///
    /// <para><b>Why it is asserted on the BYTES and negatively.</b> There is no flag to read: the defect is
    /// entirely in what ink lands on the sheet. So the assertions say what must NOT be on the reprinted sheet,
    /// and one says what must be — the "(continued)" heading <c>DrawContinuationHeader</c> writes, which is the
    /// positive proof the sheet was drawn as a continuation rather than merely missing its header.</para>
    ///
    /// <para><b>Mutation-verified</b> — see the slice report for the verbatim red.</para>
    /// </summary>
    [Fact]
    public void Reprinting_sheet_two_of_an_invoice_never_fabricates_the_statutory_first_page()
    {
        var data = LongTaxInvoice(120);
        var config = new PrintConfig { CopyMarking = CopyMarking.Original };

        // Sanity: on the REAL first sheet all three particulars are present. Without this the negatives below
        // could pass on a renderer that had simply stopped drawing them anywhere.
        string sheetOne = AsLatin1(InvoicePdf.Render(data, config, new PageConfig { FirstPage = 1, LastPage = 1 }));
        Assert.Contains("Supplier:", sheetOne, StringComparison.Ordinal);
        Assert.Contains("Invoice No:", sheetOne, StringComparison.Ordinal);
        Assert.Contains("ORIGINAL FOR RECIPIENT", sheetOne, StringComparison.Ordinal);
        Assert.DoesNotContain("continued", sheetOne, StringComparison.Ordinal);

        // The reprint of sheet 2 alone.
        string sheetTwo = AsLatin1(InvoicePdf.Render(data, config, new PageConfig { FirstPage = 2, LastPage = 2 }));

        Assert.Equal(1, PageCount(Encoding.Latin1.GetBytes(sheetTwo)));
        // Rule 46(a) — the party identity blocks belong to the first page of the document.
        Assert.DoesNotContain("Supplier:", sheetTwo, StringComparison.Ordinal);
        Assert.DoesNotContain("Recipient (Bill to):", sheetTwo, StringComparison.Ordinal);
        // Rule 46 — the document-number caption.
        Assert.DoesNotContain("Invoice No:", sheetTwo, StringComparison.Ordinal);
        // Rule 48(1) — the copy marking.
        Assert.DoesNotContain("ORIGINAL FOR RECIPIENT", sheetTwo, StringComparison.Ordinal);
        // …and it IS a continuation sheet, not a first page with its header mislaid.
        Assert.Contains("continued", sheetTwo, StringComparison.Ordinal);
    }

    /// <summary>
    /// The same rule for the <b>Rule 5(1)(f) composition declaration</b>, which is drawn by the same
    /// <c>DrawFirstHeader</c> and is the third statutory item that would migrate. A bill of supply carries it
    /// "at the top of the bill of supply"; reprinting a later sheet must not put it at the top of that sheet.
    /// </summary>
    [Fact]
    public void Reprinting_a_later_sheet_never_moves_the_rule_5_1_f_declaration_onto_it()
    {
        const string Declaration =
            "Composition taxable person, not eligible to collect tax on supplies";

        var billOfSupply = new InvoicePrintData
        {
            IsBillOfSupply = true,
            TopDeclaration = Declaration,
            Seller = LongTaxInvoice(1).Seller,
            Buyer = LongTaxInvoice(1).Buyer,
            InvoiceNumber = "BOS-0009",
            InvoiceDateText = "31-03-2025",
            PlaceOfSupply = "West Bengal (19)",
            IsInterState = false,
            Items = LongTaxInvoice(120).Items,
            TotalTaxable = new Money(12_000m),
        };

        string sheetOne = AsLatin1(InvoicePdf.Render(billOfSupply, new PrintConfig(),
            new PageConfig { FirstPage = 1, LastPage = 1 }));
        Assert.Contains(Declaration, sheetOne, StringComparison.Ordinal);

        string sheetTwo = AsLatin1(InvoicePdf.Render(billOfSupply, new PrintConfig(),
            new PageConfig { FirstPage = 2, LastPage = 2 }));
        Assert.DoesNotContain(Declaration, sheetTwo, StringComparison.Ordinal);
        Assert.DoesNotContain("Supplier:", sheetTwo, StringComparison.Ordinal);
    }

    [Fact]
    public void An_invoice_range_that_selects_nothing_yields_one_blank_sheet_not_the_whole_invoice()
    {
        var ranged = InvoicePdf.Render(LongTaxInvoice(120), new PrintConfig(),
            new PageConfig { FirstPage = 900, LastPage = 901 });

        Assert.Equal(1, PageCount(ranged));
        string s = AsLatin1(ranged);
        // "Print everything" would be the dangerous fallback here: a full tax invoice the operator did not ask
        // to reprint. The blank sheet carries none of the document's own content.
        Assert.DoesNotContain("Bright Traders", s, StringComparison.Ordinal);
        Assert.DoesNotContain("Supplier:", s, StringComparison.Ordinal);
        Assert.DoesNotContain("Invoice No:", s, StringComparison.Ordinal);
    }

    [Fact]
    public void An_invoice_start_page_number_renumbers_the_footer_without_selecting_anything()
    {
        var data = LongTaxInvoice(120);
        int total = PageCount(InvoicePdf.Render(data, new PrintConfig(), new PageConfig()));
        string s = AsLatin1(InvoicePdf.Render(data, new PrintConfig(), new PageConfig { StartPageNumber = 7 }));

        // 7 + total - 1, derived by hand from the rule, not read off the code.
        Assert.Contains($"Page 7 of {7 + total - 1}", s, StringComparison.Ordinal);
        Assert.Equal(total, PageCount(Encoding.Latin1.GetBytes(s)));
        // Renumbering selects nothing, so the statutory first page is still the first page.
        Assert.Contains("Supplier:", s, StringComparison.Ordinal);
    }

    [Fact]
    public void The_invoice_defaults_leave_the_shipped_bytes_untouched()
    {
        var shipped = InvoicePdf.Render(LongTaxInvoice(120), new PrintConfig(), new PageConfig());
        var explicitDefaults = InvoicePdf.Render(LongTaxInvoice(120), new PrintConfig(),
            new PageConfig { FirstPage = 1, LastPage = 0, StartPageNumber = 1 });

        Assert.Equal(shipped, explicitDefaults);
    }

    // ================================================================= PayslipPdf (single sheet)
    //
    // PayslipPdf.Render had NO range test anywhere in the tree: both the IncludesPage(1) early return and the
    // StartPageNumber footer renumbering shipped entirely unexercised.

    /// <summary>
    /// A deterministic one-sheet payslip, built as a literal record rather than through the payroll services —
    /// nothing here needs a posted voucher, and a literal keeps the fixture free of a clock and of the pay-head
    /// setup's own behaviour.
    /// </summary>
    private static Payslip SamplePayslip() => new(
        EmployerName: "Bright Traders",
        EmployerAddress: "12 Market Street, Kolkata",
        EmployeeId: new Guid("11111111-2222-3333-4444-555555555555"),
        EmployeeName: "Rajkumar Sharma",
        EmployeeNumber: "E-001",
        Designation: "Driver",
        Department: "Operations",
        DateOfJoining: new DateOnly(2024, 4, 1),
        Pan: "ABCPS1234K",
        Uan: null,
        EsiNumber: null,
        BankName: "State Bank",
        BankAccountNumber: "1234567890",
        BankIfsc: "SBIN0001234",
        PeriodFrom: new DateOnly(2025, 4, 1),
        PeriodTo: new DateOnly(2025, 4, 30),
        Earnings: new[] { new PayslipLine("Basic", new Money(30_000m)) },
        Deductions: new[] { new PayslipLine("Advance Recovery", new Money(2_000m)) },
        EmployerContributions: Array.Empty<PayslipLine>(),
        GrossEarnings: new Money(30_000m),
        TotalDeductions: new Money(2_000m),
        NetPayable: new Money(28_000m),
        DaysPaid: 30m,
        DaysLop: 0m,
        YtdGrossEarnings: new Money(30_000m),
        YtdTotalDeductions: new Money(2_000m),
        YtdNetPayable: new Money(28_000m));

    [Fact]
    public void A_payslip_start_page_number_renumbers_its_only_sheet()
    {
        // One sheet ⇒ first = 4 and last = 4 + 1 - 1 = 4. Derived by hand from the rule.
        string s = AsLatin1(PayslipPdf.Render(SamplePayslip(), new PageConfig { StartPageNumber = 4 }));
        Assert.Contains("Page 4 of 4", s, StringComparison.Ordinal);
        // And renumbering selects nothing — the payslip itself is still on the sheet.
        Assert.Contains("Rajkumar Sharma", s, StringComparison.Ordinal);
    }

    /// <summary>
    /// 🔴 The whole <c>PayslipPdf.cs:40-45</c> early-return path, which had no test at all. An employee's pay
    /// particulars are the most sensitive thing this renderer draws, so "the range excluded the only sheet"
    /// must mean a blank sheet — never a silent fallback that prints them anyway.
    /// </summary>
    [Fact]
    public void A_payslip_range_that_excludes_its_only_sheet_yields_a_blank_sheet_with_no_pay_particulars()
    {
        var ranged = PayslipPdf.Render(SamplePayslip(), new PageConfig { FirstPage = 2 });

        Assert.Equal(1, PageCount(ranged));
        string s = AsLatin1(ranged);
        Assert.DoesNotContain("Rajkumar Sharma", s, StringComparison.Ordinal);
        Assert.DoesNotContain("Basic", s, StringComparison.Ordinal);
        Assert.DoesNotContain("28,000", s, StringComparison.Ordinal);
    }

    [Fact]
    public void The_payslip_defaults_leave_the_shipped_bytes_untouched()
    {
        var shipped = PayslipPdf.Render(SamplePayslip(), new PageConfig());
        var explicitDefaults = PayslipPdf.Render(SamplePayslip(),
            new PageConfig { FirstPage = 1, LastPage = 0, StartPageNumber = 1 });

        Assert.Equal(shipped, explicitDefaults);
    }
}
