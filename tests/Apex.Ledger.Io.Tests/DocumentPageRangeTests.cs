using System.Collections.Generic;
using System.Text;
using Apex.Ledger;
using Apex.Ledger.Io;

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
}
