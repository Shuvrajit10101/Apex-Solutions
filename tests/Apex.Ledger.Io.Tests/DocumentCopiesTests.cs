using System.Text;
using Apex.Ledger;
using Apex.Ledger.Io;

namespace Apex.Ledger.Io.Tests;

/// <summary>
/// W2-31 / census row 12.4, the F5 <b>number of copies</b> knob applied to the DOCUMENT renderers, not only to
/// the report grid. An invoice printed in triplicate is the case this knob exists for, so a copy count that
/// worked on a Trial Balance and not on a tax invoice would close row 12.4 on the half nobody asks for.
///
/// <para>Copies are collated whole documents (1,2,1,2), and one copy must leave the shipped byte stream exactly
/// as it was — every golden in this suite depends on that (ER-13).</para>
/// </summary>
public sealed class DocumentCopiesTests
{
    private static string AsLatin1(byte[] bytes) => Encoding.Latin1.GetString(bytes);

    private static int PageObjectCount(string s)
    {
        int count = 0, idx = 0;
        while ((idx = s.IndexOf("/Type /Page", idx, System.StringComparison.Ordinal)) >= 0)
        {
            int after = idx + "/Type /Page".Length;
            if (after >= s.Length || s[after] != 's') count++;
            idx = after;
        }
        return count;
    }

    // A deterministic payment voucher: Dr Rent 10,000.00, Cr Cash 10,000.00.
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
    public void One_copy_leaves_a_voucher_pdf_byte_identical()
    {
        var shipped = VoucherPdf.Render(SampleVoucher(), new PrintConfig(), new PageConfig());
        var explicitOne = VoucherPdf.Render(SampleVoucher(), new PrintConfig(), new PageConfig { Copies = 1 });

        Assert.Equal(shipped, explicitOne);
    }

    [Fact]
    public void A_voucher_printed_in_triplicate_carries_three_collated_sets()
    {
        string one = AsLatin1(VoucherPdf.Render(SampleVoucher(), new PrintConfig(), new PageConfig()));
        string three = AsLatin1(VoucherPdf.Render(SampleVoucher(), new PrintConfig(),
            new PageConfig { Copies = 3 }));

        int pages = PageObjectCount(one);
        Assert.Equal(1, pages);
        Assert.Equal(3, PageObjectCount(three));
        // The figure must be on every copy, to the paisa — a copy that lost the amount is not a copy.
        Assert.Contains("10,000.00", three);
    }

    [Fact]
    public void A_copy_count_below_one_is_treated_as_one_on_a_voucher_too()
    {
        string s = AsLatin1(VoucherPdf.Render(SampleVoucher(), new PrintConfig(), new PageConfig { Copies = 0 }));
        Assert.Equal(1, PageObjectCount(s));
    }

    [Fact]
    public void Copies_are_deterministic()
    {
        var cfg = new PageConfig { Copies = 2 };
        Assert.Equal(
            VoucherPdf.Render(SampleVoucher(), new PrintConfig(), cfg),
            VoucherPdf.Render(SampleVoucher(), new PrintConfig(), cfg));
    }
}
