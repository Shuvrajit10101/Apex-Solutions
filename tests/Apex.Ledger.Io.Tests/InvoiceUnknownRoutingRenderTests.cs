using System.Collections.Generic;
using System.Linq;
using System.Text;
using Apex.Ledger;
using Apex.Ledger.Io;
using Xunit;

namespace Apex.Ledger.Io.Tests;

/// <summary>
/// <b>W0-15 review — widening <see cref="InvoicePrintData.IsInterState"/> to <c>bool?</c> silently changed its
/// DEFAULT from <c>false</c> to <c>null</c>, and the renderer read that null two different ways in one document.</b>
///
/// <para><c>InvoicePdf</c> deliberately keeps its tax-breakup block "belt-and-braces … safe against any future caller"
/// that does not suppress the rows itself. After the widening, a DTO that merely OMITS the property — the ordinary
/// shape of a hand-built <c>InvoicePrintData</c> — reached the breakup table's bare <c>else</c> and was drawn as
/// INTRA-state (CGST and SGST columns, both amounts), while the totals band read the same <c>null</c> as "state no
/// head" and printed neither. One page, two answers, and the tax was left out of the money band while
/// <see cref="InvoicePrintData.GrandTotal"/> went on adding it.</para>
///
/// <para>The projector cannot produce this shape (a null routing there means no forward tax leg was posted, so there
/// are no rate rows), which is exactly why nothing in the suite covered it — and exactly why the renderer's own
/// comment says it must not depend on the projector.</para>
///
/// <para><b>RED PROOFS (each measured, one mutation at a time):</b>
/// <list type="bullet">
/// <item>Drop <c>data.IsInterState is not null</c> from <c>InvoicePdf.StatesTaxBreakup</c> ⇒ the CGST/SGST breakup
/// comes back and <see cref="A_rate_bearing_document_with_no_routing_names_no_head_anywhere"/> fails on its very
/// first assertion.</item>
/// <item>Delete <c>HeadRows</c>'s <c>null</c> limb (return an empty list unconditionally) ⇒ the "Tax" line disappears
/// and <see cref="A_rate_bearing_document_with_no_routing_still_states_the_tax_it_charges"/> fails, leaving a Grand
/// Total ₹1,575.06 above the only figure the page states.</item>
/// <item>Change that limb to the CGST/SGST pair — the "collapse the null" mutation, which under the old
/// <c>int HeadRowCount</c> shape reached ONLY the measured height and turned nothing red — ⇒
/// <see cref="A_rate_bearing_document_with_no_routing_names_no_head_anywhere"/> fails, because the count and the drawn
/// rows are now one expression.</item>
/// </list></para>
///
/// <para>Odd to the paisa: ₹8,750.37 taxable, CGST ₹787.53 + SGST ₹787.53 = ₹1,575.06 of tax, Grand Total
/// ₹10,325.43.</para>
/// </summary>
public sealed class InvoiceUnknownRoutingRenderTests
{
    private static readonly Money Taxable = Money.FromRupees(8_750.37m);
    private static readonly Money Half = Money.FromRupees(787.53m);
    private static readonly Money Tax = Money.FromRupees(1_575.06m);
    private static readonly Money Grand = Money.FromRupees(10_325.43m);

    /// <summary>A caller-built tax invoice carrying rate rows and head totals and <b>no</b> routing — the property is
    /// simply not set, which is how a DTO acquires <c>null</c> without anybody choosing it.</summary>
    private static InvoicePrintData UnroutedWithTax() => new()
    {
        InvoiceNumber = "INV-UNROUTED-1",
        InvoiceDateText = "10-04-2025",
        TotalTaxable = Taxable,
        TotalCgst = Half,
        TotalSgst = Half,
        TaxRows = new[]
        {
            new InvoiceTaxRow { RateLabel = "18%", TaxableValue = Taxable, Cgst = Half, Sgst = Half },
        },
        Items = new[]
        {
            new InvoiceItemRow
            {
                Description = "Widget", HsnSac = "847130", QuantityText = "3 Nos",
                RateText = "2,916.79", TaxableValue = Taxable,
            },
        },
        // IsInterState deliberately NOT set.
    };

    private static string Render(InvoicePrintData data) =>
        Encoding.Latin1.GetString(InvoicePdf.Render(data, new PrintConfig(), new PageConfig()));

    /// <summary>The premise, stated rather than assumed: the DTO really does default to <c>null</c>.</summary>
    [Fact]
    public void An_unset_routing_is_null_and_not_intra_state()
    {
        Assert.Null(UnroutedWithTax().IsInterState);
        Assert.Equal(Tax.Amount, UnroutedWithTax().TotalTax.Amount);
        Assert.Equal(Grand.Amount, UnroutedWithTax().GrandTotal.Amount);
    }

    /// <summary>
    /// No head is NAMED anywhere on the page — not in the totals band, not as a breakup column header, not as a
    /// supply caption. Naming one would assert a routing the document does not have.
    /// </summary>
    [Fact]
    public void A_rate_bearing_document_with_no_routing_names_no_head_anywhere()
    {
        var pdf = Render(UnroutedWithTax());

        Assert.DoesNotContain("CGST", pdf);
        Assert.DoesNotContain("SGST", pdf);
        Assert.DoesNotContain("IGST", pdf);
        Assert.DoesNotContain("Intra-State", pdf);
        Assert.DoesNotContain("Inter-State", pdf);
        Assert.DoesNotContain("GST Breakup", pdf);          // the breakup table is suppressed whole
    }

    /// <summary>
    /// …and the money still foots. The tax is stated under the head-free label "Tax", so the page does not show a
    /// Grand Total exceeding its own "Taxable Value" by ₹1,575.06 that nothing on it explains. An amount asserts no
    /// routing; a head would.
    /// </summary>
    [Fact]
    public void A_rate_bearing_document_with_no_routing_still_states_the_tax_it_charges()
    {
        var pdf = Render(UnroutedWithTax());

        Assert.Contains("(Tax) Tj", pdf);                   // the label, as PdfWriter emits it
        Assert.Contains("(1,575.06) Tj", pdf);              // the amount beside it
        Assert.Contains("(Taxable Value) Tj", pdf);
        Assert.Contains("(8,750.37) Tj", pdf);
        Assert.Contains("(Grand Total) Tj", pdf);
        Assert.Contains("(10,325.43) Tj", pdf);
    }

    /// <summary>
    /// The ROUTED documents are untouched — an explicitly intra-state twin of the same DTO still prints its CGST/SGST
    /// pair and its breakup (ER-13). Without this, "suppress everything" would be a passing answer to both tests
    /// above.
    /// </summary>
    [Theory]
    [InlineData(false, "CGST", "SGST")]
    [InlineData(true, "IGST", "IGST")]
    public void An_explicitly_routed_twin_of_the_same_document_is_unchanged(bool interState, string head, string other)
    {
        var routed = Routed(interState, 1);

        var pdf = Render(routed);
        Assert.Contains($"({head}) Tj", pdf);
        Assert.Contains($"({other}) Tj", pdf);
        Assert.Contains("(GST Breakup) Tj", pdf);
        // PdfWriter escapes the caption's own parentheses, so the literal reads `(Inter-State \(IGST\)) Tj`.
        Assert.Contains(interState ? @"(Inter-State \(IGST\)) Tj" : @"(Intra-State \(CGST + SGST\)) Tj", pdf);
        Assert.Contains("(10,325.43) Tj", pdf);
        Assert.DoesNotContain("(Tax) Tj", pdf);             // the head-free label appears ONLY where nothing routed
    }

    // ================================================================ the MEASURED height, which nothing pinned

    /// <summary>
    /// <b>The closing block's measured HEIGHT is load-bearing, and until now no test in this repository asserted
    /// it.</b> <c>HeadRows</c> reaches the page in two ways: the rows drawn (covered above) and the block's measured
    /// height, which decides whether the closing block still fits under the last item row or starts a fresh page.
    /// The measurement path is invisible to every content assertion — a review pass measured a two-row
    /// over-statement and found the ENTIRE repository still green.
    ///
    /// <para><b>What this asserts, and why it is not a hard-coded page count.</b> Two documents identical in every
    /// figure — same taxable value, same tax, same Grand Total, same words line, and <b>neither</b> carrying rate
    /// rows, so the breakup is out of the comparison entirely — differ only in their head ROWS: the null-routing one
    /// states the amount on a single head-free "Tax" line, its explicitly intra-state twin on a CGST + SGST pair.
    /// Its closing block is therefore exactly ONE row-height shorter, so there must exist an item count at which the
    /// first still fits on one page and the second does not. The test SEARCHES for such a count instead of
    /// hard-coding one, so it survives a font, margin or row-pitch change and fails only if the measurement stops
    /// distinguishing them.</para>
    ///
    /// <para><b>Bite:</b> add two phantom rows to <c>BuildClosing</c>'s <c>totalRows</c> for a null routing while
    /// leaving the drawing correct — the mutation that reaches ONLY geometry, and the one measured to leave the whole
    /// repository green before this test existed — and the null document becomes the TALLER of the two, so no such
    /// count exists and this fails. Measured.</para>
    /// </summary>
    [Fact]
    public void The_measured_closing_block_is_one_row_shorter_when_no_head_is_named()
    {
        var differing = Enumerable.Range(1, 80)
            .Where(n => PageCount(NoBreakup(null, n)) < PageCount(NoBreakup(false, n)))
            .ToList();

        Assert.True(differing.Count > 0,
            "A document that names NO tax head measures one row-height shorter than the same document stating "
            + "CGST + SGST, so some item count must fit the first on one page and push the second onto two. None "
            + "does — BuildClosing is no longer measuring the head rows it draws.");

        // …and where they differ, the shorter one really is the one-pager (the search above only compares them).
        var n0 = differing[0];
        Assert.Equal(1, PageCount(NoBreakup(null, n0)));
        Assert.Equal(2, PageCount(NoBreakup(false, n0)));
    }

    /// <summary>The same document twice, with and without a stated routing, carrying tax but <b>no</b> rate rows — so
    /// the per-rate breakup contributes nothing to either height and the head rows are the only difference.</summary>
    private static InvoicePrintData NoBreakup(bool? interState, int items) => new()
    {
        InvoiceNumber = "INV-HEIGHT-1",
        InvoiceDateText = "10-04-2025",
        TotalTaxable = Taxable,
        TotalCgst = Half,
        TotalSgst = Half,
        IsInterState = interState,
        Items = Enumerable.Range(1, items).Select(Item).ToList(),
    };

    /// <summary>The page count PdfWriter itself declares in the page tree.</summary>
    private static int PageCount(InvoicePrintData data) =>
        int.Parse(System.Text.RegularExpressions.Regex
            .Match(Render(data), @"/Type /Pages /Count (\d+)").Groups[1].Value);

    private static InvoiceItemRow Item(int n) => new()
    {
        Description = "Widget " + n, HsnSac = "847130", QuantityText = "3 Nos",
        RateText = "2,916.79", TaxableValue = Taxable,
    };

    private static InvoicePrintData Unrouted(int items) => new()
    {
        InvoiceNumber = "INV-UNROUTED-1",
        InvoiceDateText = "10-04-2025",
        TotalTaxable = Taxable,
        TotalCgst = Half,
        TotalSgst = Half,
        TaxRows = new[]
        {
            new InvoiceTaxRow { RateLabel = "18%", TaxableValue = Taxable, Cgst = Half, Sgst = Half },
        },
        Items = Enumerable.Range(1, items).Select(Item).ToList(),
        // IsInterState deliberately NOT set.
    };

    private static InvoicePrintData Routed(bool interState, int items) => new()
    {
        InvoiceNumber = "INV-ROUTED-1",
        InvoiceDateText = "10-04-2025",
        TotalTaxable = Taxable,
        TotalCgst = interState ? Money.Zero : Half,
        TotalSgst = interState ? Money.Zero : Half,
        TotalIgst = interState ? Tax : Money.Zero,
        IsInterState = interState,
        TaxRows = new[]
        {
            new InvoiceTaxRow
            {
                RateLabel = "18%", TaxableValue = Taxable,
                Cgst = interState ? Money.Zero : Half,
                Sgst = interState ? Money.Zero : Half,
                Igst = interState ? Tax : Money.Zero,
            },
        },
        Items = Enumerable.Range(1, items).Select(Item).ToList(),
    };
}
