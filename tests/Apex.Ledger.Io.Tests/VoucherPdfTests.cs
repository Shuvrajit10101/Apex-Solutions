using System;
using System.Collections.Generic;
using System.Text;
using Apex.Ledger;
using Apex.Ledger.Io;

namespace Apex.Ledger.Io.Tests;

/// <summary>
/// Tests for <see cref="VoucherPdf"/> (RQ-10): a posted voucher renders to a valid, de-branded, deterministic
/// PDF carrying the header, the Dr/Cr lines, the totals, the amount-in-words and the narration (with the F12
/// narration toggle honoured).
/// </summary>
public sealed class VoucherPdfTests
{
    private static string AsLatin1(byte[] bytes) => Encoding.Latin1.GetString(bytes);

    // A deterministic payment voucher: Dr Rent 10,000, Cr Cash 10,000.
    private static VoucherPrintData SampleVoucher() => new()
    {
        CompanyName = "Bright Traders",
        VoucherTypeName = "Payment",
        VoucherNumber = "42",
        DateText = "31-03-2025",
        PartyName = "",
        Lines = new[]
        {
            new VoucherPrintLine { LedgerName = "Rent A/c", IsDebit = true, Amount = new Money(10000m) },
            new VoucherPrintLine { LedgerName = "Cash-in-Hand", IsDebit = false, Amount = new Money(10000m) },
        },
        Narration = "Office rent for March 2025",
    };

    [Fact]
    public void Renders_a_valid_pdf()
    {
        var bytes = VoucherPdf.Render(SampleVoucher(), new PrintConfig(), new PageConfig());
        string s = AsLatin1(bytes);
        Assert.StartsWith("%PDF-", s);
        Assert.Contains("xref", s);
        Assert.Contains("trailer", s);
        Assert.Contains("%%EOF", s);
    }

    [Fact]
    public void Contains_header_lines_totals_and_amount_in_words()
    {
        var bytes = VoucherPdf.Render(SampleVoucher(), new PrintConfig(), new PageConfig());
        string s = AsLatin1(bytes);

        Assert.Contains("Bright Traders", s);
        Assert.Contains("Payment Voucher", s);
        Assert.Contains("No: 42", s);
        Assert.Contains("31-03-2025", s);
        Assert.Contains("Rent A/c", s);
        Assert.Contains("Cash-in-Hand", s);
        Assert.Contains("Total", s);
        Assert.Contains("10,000.00", s);
        Assert.Contains("Rupees Ten Thousand Only", s);
    }

    [Fact]
    public void Narration_prints_when_enabled_and_is_suppressed_when_disabled()
    {
        var on = AsLatin1(VoucherPdf.Render(SampleVoucher(), new PrintConfig { ShowNarration = true }, new PageConfig()));
        Assert.Contains("Office rent for March 2025", on);

        var off = AsLatin1(VoucherPdf.Render(SampleVoucher(), new PrintConfig { ShowNarration = false }, new PageConfig()));
        Assert.DoesNotContain("Office rent for March 2025", off);
    }

    [Fact]
    public void Title_override_is_honoured()
    {
        var bytes = VoucherPdf.Render(SampleVoucher(), new PrintConfig { TitleOverride = "RECEIPT NOTE" }, new PageConfig());
        Assert.Contains("RECEIPT NOTE", AsLatin1(bytes));
    }

    [Fact]
    public void Same_input_renders_byte_identical()
    {
        var a = VoucherPdf.Render(SampleVoucher(), new PrintConfig(), new PageConfig());
        var b = VoucherPdf.Render(SampleVoucher(), new PrintConfig(), new PageConfig());
        Assert.Equal(a, b);
    }

    [Fact]
    public void No_tally_branding_and_debranded_metadata()
    {
        var bytes = VoucherPdf.Render(SampleVoucher(), new PrintConfig(), new PageConfig());
        Assert.DoesNotContain("tally", AsLatin1(bytes).ToLowerInvariant());
        Assert.Contains("/Producer (Apex Solutions)", AsLatin1(bytes));
    }

    [Fact]
    public void Totals_match_the_dr_and_cr_sums()
    {
        var v = SampleVoucher();
        Assert.Equal(10000m, v.TotalDebit.Amount);
        Assert.Equal(10000m, v.TotalCredit.Amount);
    }

    // ---- Fix 3: a user title override containing the forbidden brand is scrubbed before it reaches the PDF ----

    [Fact]
    public void Title_override_is_debranded_before_reaching_the_pdf()
    {
        var bytes = VoucherPdf.Render(SampleVoucher(), new PrintConfig { TitleOverride = "Tally Voucher Copy" }, new PageConfig());
        string s = AsLatin1(bytes);
        Assert.DoesNotContain("tally", s.ToLowerInvariant());
        Assert.Contains("Voucher Copy", s);
    }

    // ---- Fix 2: a long voucher paginates; the closing block (totals + words) is never clipped off-page ----

    // A balanced N-line voucher: N Dr lines of 100 each and a single Cr line of 100*N.
    private static VoucherPrintData ManyLineVoucher(int drLines)
    {
        var lines = new List<VoucherPrintLine>(drLines + 1);
        for (int i = 0; i < drLines; i++)
            lines.Add(new VoucherPrintLine { LedgerName = "Expense Ledger " + (i + 1), IsDebit = true, Amount = new Money(100m) });
        lines.Add(new VoucherPrintLine { LedgerName = "Bank A/c", IsDebit = false, Amount = new Money(100m * drLines) });

        return new VoucherPrintData
        {
            CompanyName = "Bright Traders",
            VoucherTypeName = "Payment",
            VoucherNumber = "99",
            DateText = "31-03-2025",
            Lines = lines,
            Narration = "Bulk expense settlement run",
        };
    }

    [Fact]
    public void Long_voucher_paginates_and_the_closing_block_is_present_not_clipped()
    {
        var data = ManyLineVoucher(90);
        var bytes = VoucherPdf.Render(data, new PrintConfig(), new PageConfig());
        string s = AsLatin1(bytes);

        Assert.True(PdfPageCount(s) > 1, "a 90-line voucher must span more than one page");
        Assert.True(AllTextYPositive(s), "no text may be drawn at a negative y (clipped off-page)");

        // 90 Dr @ 100 = 9,000 total (== the single Cr line).
        Assert.Contains("Total", s);
        Assert.Contains("9,000.00", s);
        Assert.Contains("Rupees Nine Thousand Only", s);      // amount-in-words for the grand total
        Assert.Contains("Bulk expense settlement run", s);      // narration in the closing block
        // The Particulars/Debit/Credit column header repeats on the continuation page(s).
        Assert.True(CountOccurrences(s, "Particulars") >= 2, "the posting-table column header must repeat on continuation pages");
    }

    private static int PdfPageCount(string latin1)
    {
        var m = System.Text.RegularExpressions.Regex.Match(latin1, @"/Type /Pages /Count (\d+)");
        return m.Success ? int.Parse(m.Groups[1].Value) : 0;
    }

    private static bool AllTextYPositive(string latin1)
    {
        foreach (System.Text.RegularExpressions.Match m in
                 System.Text.RegularExpressions.Regex.Matches(latin1, @"(-?[\d.]+) (-?[\d.]+) Td"))
        {
            if (double.Parse(m.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture) < 0) return false;
        }
        return true;
    }

    private static int CountOccurrences(string haystack, string needle)
    {
        int n = 0, i = 0;
        while ((i = haystack.IndexOf(needle, i, StringComparison.Ordinal)) >= 0) { n++; i += needle.Length; }
        return n;
    }
}
