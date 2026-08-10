using System;
using System.Collections.Generic;
using System.Text;
using Apex.Ledger;
using Apex.Ledger.Io;
using Apex.Ledger.Services;

namespace Apex.Ledger.Io.Tests;

/// <summary>
/// Tests for <see cref="InvoicePdf"/> (RQ-11): a Sales item-invoice renders as a GST tax invoice carrying the
/// Rule-46 mandatory fields — both GSTINs, HSN, per-rate CGST/SGST (intra) or IGST (inter) matching the GST
/// engine to the paisa, taxable + tax + grand total, amount-in-words, the copy-marking label and the
/// declaration/signature. De-branded and deterministic.
/// </summary>
public sealed class InvoicePdfTests
{
    private static string AsLatin1(byte[] bytes) => Encoding.Latin1.GetString(bytes);

    // Two valid GSTINs (checksum-correct) — seller West Bengal (19), buyer differs for inter-state cases.
    // Bright-style figures: 2 items @ 18% intra-state.
    private const string SellerGstin = "19AAAAA0000A1Z5"; // WB (computed check digit)
    private const string BuyerGstinWb = "19BBBBB0000B1Z"; // placeholder; fixed up below

    // Item 1: 10 @ 500 = 5,000.00 ; Item 2: 3 @ 1,250 = 3,750.00 ; taxable 8,750.00 @ 18%.
    private static InvoicePrintData IntraStateInvoice(out GstService.LineTax engineTax)
    {
        var taxable = new Money(8750m);
        engineTax = GstService.ComputeLineTax(taxable, 1800, interState: false);

        return new InvoicePrintData
        {
            Seller = new InvoicePartyBlock
            {
                Name = "Bright Traders",
                AddressLines = new[] { "12 Market Street", "Kolkata" },
                Gstin = ValidGstin("19AAAAA0000A1Z"),
                StateText = "West Bengal (19)",
            },
            Buyer = new InvoicePartyBlock
            {
                Name = "Acme Retail",
                AddressLines = new[] { "5 Mall Road", "Kolkata" },
                Gstin = ValidGstin("19CCCCC0000C1Z"),
                StateText = "West Bengal (19)",
            },
            InvoiceNumber = "INV-001",
            InvoiceDateText = "31-03-2025",
            PlaceOfSupply = "West Bengal (19)",
            IsInterState = false,
            Items = new[]
            {
                new InvoiceItemRow { Description = "Widget", HsnSac = "84713010", QuantityText = "10.000", RateText = "500.00", TaxableValue = new Money(5000m) },
                new InvoiceItemRow { Description = "Gadget", HsnSac = "8530", QuantityText = "3.000", RateText = "1,250.00", TaxableValue = new Money(3750m) },
            },
            TaxRows = new[]
            {
                new InvoiceTaxRow { RateLabel = "18%", TaxableValue = taxable, Cgst = engineTax.Cgst, Sgst = engineTax.Sgst, Igst = Money.Zero },
            },
            TotalTaxable = taxable,
            TotalCgst = engineTax.Cgst,
            TotalSgst = engineTax.Sgst,
            TotalIgst = Money.Zero,
            Narration = "Sold as per PO 77",
        };
    }

    // The same taxable value, inter-state (IGST).
    private static InvoicePrintData InterStateInvoice(out GstService.LineTax engineTax)
    {
        var taxable = new Money(8750m);
        engineTax = GstService.ComputeLineTax(taxable, 1800, interState: true);

        return new InvoicePrintData
        {
            Seller = new InvoicePartyBlock { Name = "Bright Traders", Gstin = ValidGstin("19AAAAA0000A1Z"), StateText = "West Bengal (19)" },
            Buyer = new InvoicePartyBlock { Name = "North Supplies", Gstin = ValidGstin("07DDDDD0000D1Z"), StateText = "Delhi (07)" },
            InvoiceNumber = "INV-002",
            InvoiceDateText = "31-03-2025",
            PlaceOfSupply = "Delhi (07)",
            IsInterState = true,
            Items = new[]
            {
                new InvoiceItemRow { Description = "Widget", HsnSac = "84713010", QuantityText = "17.500", RateText = "500.00", TaxableValue = taxable },
            },
            TaxRows = new[]
            {
                new InvoiceTaxRow { RateLabel = "18%", TaxableValue = taxable, Cgst = Money.Zero, Sgst = Money.Zero, Igst = engineTax.Igst },
            },
            TotalTaxable = taxable,
            TotalIgst = engineTax.Igst,
        };
    }

    // Completes a 14-char GSTIN prefix with its correct Luhn-mod-36 check digit (so the fixture is a real GSTIN).
    private static string ValidGstin(string first14) => first14 + Apex.Ledger.Domain.Gstin.ComputeCheckDigit(first14 + "0");

    // ================================================================ W0-1 (census T0-7): BILL OF SUPPLY

    /// <summary>
    /// A wholly exempt supply of ODD value (60.125 Nos @ ₹786.64 = ₹47,296.73), rendered as the bill of supply CGST Act
    /// §31(3)(c) requires. Deliberately constructed WITH populated tax rows and non-zero heads so the renderer's own
    /// suppression is under test, not the projector's: the layout layer must be safe for any caller.
    /// </summary>
    private static InvoicePrintData BillOfSupply(bool composition)
    {
        var value = new Money(47_296.73m);
        return new InvoicePrintData
        {
            DocumentTitle = "BILL OF SUPPLY",
            IsBillOfSupply = true,
            TopDeclaration = composition
                ? "Composition taxable person, not eligible to collect tax on supplies"
                : string.Empty,
            Seller = new InvoicePartyBlock
            {
                Name = "Bright Traders", Gstin = ValidGstin("19AAAAA0000A1Z"), StateText = "West Bengal (19)",
            },
            Buyer = new InvoicePartyBlock
            {
                Name = "Acme Retail", Gstin = ValidGstin("19CCCCC0000C1Z"), StateText = "West Bengal (19)",
            },
            InvoiceNumber = "BOS-0417",
            InvoiceDateText = "31-03-2025",
            PlaceOfSupply = "West Bengal (19)",
            IsInterState = false,
            Items = new[]
            {
                new InvoiceItemRow
                {
                    Description = "Fresh Milk", HsnSac = "040110",
                    QuantityText = "60.125 Nos", RateText = "786.64", TaxableValue = value,
                },
            },
            // A hostile caller: tax the document may not show. Rule 49 prescribes no rate and no tax-amount
            // particular, so NONE of this may reach the page.
            TaxRows = new[]
            {
                new InvoiceTaxRow
                {
                    RateLabel = "18%", TaxableValue = value,
                    Cgst = new Money(4_256.71m), Sgst = new Money(4_256.70m), Igst = Money.Zero,
                },
            },
            TotalTaxable = value,
        };
    }

    /// <summary>CGST Act §31(3)(c) + Rule 49: the document is titled BILL OF SUPPLY, never TAX INVOICE, and shows
    /// Rule 49(g)'s "value of supply" — with no rate, no tax head and no breakup table anywhere on the page.</summary>
    [Fact]
    public void A_bill_of_supply_is_titled_correctly_and_shows_no_tax_particular_at_all()
    {
        var bytes = InvoicePdf.Render(BillOfSupply(composition: false), new PrintConfig(), new PageConfig());
        string s = AsLatin1(bytes);

        Assert.StartsWith("%PDF-", s);
        Assert.Contains("BILL OF SUPPLY", s);
        Assert.DoesNotContain("TAX INVOICE", s);          // incl. the /Title metadata, which follows the document
        Assert.DoesNotContain("Tax Invoice", s);

        // Rule 49 has no counterpart to Rule 46 (l) rate of tax / (m) amount of tax / (n) inter-State place of supply.
        Assert.DoesNotContain("GST Breakup", s);
        Assert.DoesNotContain("CGST", s);
        Assert.DoesNotContain("SGST", s);
        Assert.DoesNotContain("IGST", s);
        Assert.DoesNotContain("4,256.7", s);              // the suppressed rows' figures
        Assert.DoesNotContain("Taxable Value", s);
        Assert.DoesNotContain("Intra-State", s);

        // Rule 49(g): value of supply — and it is the whole of what the recipient owes.
        Assert.Contains("Value of Supply", s);
        Assert.Contains("47,296.73", s);
        Assert.Contains("Rupees Forty Seven Thousand Two Hundred Ninety Six and Seventy Three Paise Only", s);
        Assert.Contains("this bill of supply shows the actual price", s);
        Assert.Contains("Authorised Signatory", s);
        Assert.DoesNotContain("tally", s.ToLowerInvariant());
    }

    /// <summary>CGST Rule 5(1)(f): a composition taxable person's bill of supply carries the declaration "at the top".
    /// A non-composition (exempt) bill of supply carries none — he is not a composition taxable person.</summary>
    [Fact]
    public void The_composition_declaration_prints_at_the_top_and_only_for_a_composition_dealer()
    {
        const string decl = "Composition taxable person, not eligible to collect tax on supplies";

        string withDecl = AsLatin1(InvoicePdf.Render(BillOfSupply(composition: true), new PrintConfig(), new PageConfig()));
        Assert.Contains(decl, withDecl);
        // "At the top": above the supplier/recipient blocks — i.e. drawn at a HIGHER y than the "Supplier:" caption.
        Assert.True(FirstTextY(withDecl, decl) > FirstTextY(withDecl, "Supplier:"),
            "Rule 5(1)(f) requires the declaration at the TOP of the bill of supply, above the party blocks.");

        string without = AsLatin1(InvoicePdf.Render(BillOfSupply(composition: false), new PrintConfig(), new PageConfig()));
        Assert.DoesNotContain("Composition taxable person", without);
    }

    /// <summary>The F12 title override must not be able to reissue, through a print knob, the very tax invoice
    /// §31(3)(c) forbids. It still applies to an ordinary tax invoice.</summary>
    [Fact]
    public void The_title_override_is_refused_on_a_bill_of_supply_but_still_applies_to_a_tax_invoice()
    {
        var cfg = new PrintConfig { TitleOverride = "TAX INVOICE" };
        string bos = AsLatin1(InvoicePdf.Render(BillOfSupply(composition: true), cfg, new PageConfig()));
        Assert.Contains("BILL OF SUPPLY", bos);
        Assert.DoesNotContain("TAX INVOICE", bos);

        string proforma = AsLatin1(InvoicePdf.Render(
            IntraStateInvoice(out _), new PrintConfig { TitleOverride = "PROFORMA INVOICE" }, new PageConfig()));
        Assert.Contains("PROFORMA INVOICE", proforma);
    }

    /// <summary>The y-coordinate of the text-positioning operator that precedes the first occurrence of
    /// <paramref name="needle"/> — used to assert vertical ORDER on the page (PDF y grows upward).</summary>
    private static double FirstTextY(string latin1, string needle)
    {
        int at = latin1.IndexOf(needle, StringComparison.Ordinal);
        Assert.True(at >= 0, $"'{needle}' not found in the rendered PDF.");
        int td = latin1.LastIndexOf(" Td", at, StringComparison.Ordinal);
        Assert.True(td > 0, $"No text-position operator precedes '{needle}'.");
        int lineStart = latin1.LastIndexOf('\n', td) + 1;
        var parts = latin1[lineStart..td].Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return double.Parse(parts[^1], System.Globalization.CultureInfo.InvariantCulture);
    }

    [Fact]
    public void Renders_a_valid_debranded_pdf()
    {
        var bytes = InvoicePdf.Render(IntraStateInvoice(out _), new PrintConfig(), new PageConfig());
        string s = AsLatin1(bytes);
        Assert.StartsWith("%PDF-", s);
        Assert.Contains("%%EOF", s);
        Assert.DoesNotContain("tally", s.ToLowerInvariant());
        Assert.Contains("/Producer (Apex Solutions)", s);
        Assert.Contains("TAX INVOICE", s);
    }

    [Fact]
    public void Intra_state_invoice_contains_both_gstins_hsn_taxes_grandtotal_and_words()
    {
        var data = IntraStateInvoice(out var tax);
        var bytes = InvoicePdf.Render(data, new PrintConfig { CopyMarking = CopyMarking.Original }, new PageConfig());
        string s = AsLatin1(bytes);

        // Both GSTINs present (Rule 46 supplier + recipient).
        Assert.Contains(data.Seller.Gstin, s);
        Assert.Contains(data.Buyer.Gstin, s);

        // HSN codes present.
        Assert.Contains("84713010", s);
        Assert.Contains("8530", s);

        // CGST + SGST match the engine to the paisa: 8,750 @ 18% = 1,575 total -> 787.50 each.
        Assert.Equal(787.50m, tax.Cgst.Amount);
        Assert.Equal(787.50m, tax.Sgst.Amount);
        Assert.Contains("787.50", s);

        // Taxable + grand total. Grand = 8,750 + 1,575 = 10,325.00.
        Assert.Contains("8,750.00", s);
        Assert.Contains("10,325.00", s);

        // Amount in words for the grand total.
        Assert.Contains("Rupees Ten Thousand Three Hundred Twenty Five Only", s);

        // Copy-marking label.
        Assert.Contains("ORIGINAL FOR RECIPIENT", s);

        // Declaration + signature.
        Assert.Contains("Declaration", s);
        Assert.Contains("Authorised Signatory", s);

        // CGST/SGST head labels appear (not IGST) for an intra-state supply.
        Assert.Contains("CGST", s);
        Assert.Contains("SGST", s);
    }

    [Fact]
    public void Inter_state_invoice_shows_igst_matching_the_engine()
    {
        var data = InterStateInvoice(out var tax);
        var bytes = InvoicePdf.Render(data, new PrintConfig { CopyMarking = CopyMarking.Duplicate }, new PageConfig());
        string s = AsLatin1(bytes);

        // 8,750 @ 18% IGST = 1,575.00.
        Assert.Equal(1575.00m, tax.Igst.Amount);
        Assert.Contains("1,575.00", s);
        Assert.Contains("IGST", s);
        // Parentheses are backslash-escaped inside the PDF literal string.
        Assert.Contains(@"Inter-State \(IGST\)", s);
        Assert.Contains("DUPLICATE FOR SUPPLIER", s);
        // Grand total 8,750 + 1,575 = 10,325.
        Assert.Contains("10,325.00", s);
    }

    [Fact]
    public void Grand_total_foots_taxable_plus_tax()
    {
        var data = IntraStateInvoice(out _);
        Assert.Equal(8750m, data.TotalTaxable.Amount);
        Assert.Equal(1575m, data.TotalTax.Amount);
        Assert.Equal(10325m, data.GrandTotal.Amount);
    }

    [Fact]
    public void Triplicate_and_none_copy_markings()
    {
        var trip = AsLatin1(InvoicePdf.Render(IntraStateInvoice(out _), new PrintConfig { CopyMarking = CopyMarking.Triplicate }, new PageConfig()));
        Assert.Contains("TRIPLICATE FOR TRANSPORTER", trip);

        var none = AsLatin1(InvoicePdf.Render(IntraStateInvoice(out _), new PrintConfig { CopyMarking = CopyMarking.None }, new PageConfig()));
        Assert.DoesNotContain("FOR RECIPIENT", none);
        Assert.DoesNotContain("FOR SUPPLIER", none);
        Assert.DoesNotContain("FOR TRANSPORTER", none);
    }

    [Fact]
    public void Same_input_renders_byte_identical()
    {
        var a = InvoicePdf.Render(IntraStateInvoice(out _), new PrintConfig { CopyMarking = CopyMarking.Original }, new PageConfig());
        var b = InvoicePdf.Render(IntraStateInvoice(out _), new PrintConfig { CopyMarking = CopyMarking.Original }, new PageConfig());
        Assert.Equal(a, b);
    }

    [Fact]
    public void Narration_toggle_is_honoured()
    {
        var on = AsLatin1(InvoicePdf.Render(IntraStateInvoice(out _), new PrintConfig { ShowNarration = true }, new PageConfig()));
        Assert.Contains("Sold as per PO 77", on);
        var off = AsLatin1(InvoicePdf.Render(IntraStateInvoice(out _), new PrintConfig { ShowNarration = false }, new PageConfig()));
        Assert.DoesNotContain("Sold as per PO 77", off);
    }

    [Fact]
    public void Title_override_replaces_tax_invoice()
    {
        var bytes = InvoicePdf.Render(IntraStateInvoice(out _), new PrintConfig { TitleOverride = "PROFORMA INVOICE" }, new PageConfig());
        string s = AsLatin1(bytes);
        Assert.Contains("PROFORMA INVOICE", s);
    }

    // ---- Fix 3: a user title override containing the forbidden brand is scrubbed before it reaches the PDF ----

    [Fact]
    public void Title_override_is_debranded_before_reaching_the_pdf()
    {
        var bytes = InvoicePdf.Render(IntraStateInvoice(out _), new PrintConfig { TitleOverride = "Tally Report" }, new PageConfig());
        string s = AsLatin1(bytes);
        Assert.DoesNotContain("tally", s.ToLowerInvariant());   // scrubbed from body AND /Title
        Assert.Contains("Report", s);                            // the rest survives
    }

    // ---- Fix 2: a long invoice paginates; the closing block is never clipped off-page ----

    // Builds an N-line intra-state invoice: N Widgets, 1 unit each @ 100 (18%). Taxable = 100*N, tax = 18*N.
    private static InvoicePrintData ManyLineInvoice(int lineCount)
    {
        var perLine = new Money(100m);
        var items = new List<InvoiceItemRow>(lineCount);
        for (int i = 0; i < lineCount; i++)
            items.Add(new InvoiceItemRow { Description = "Item " + (i + 1), HsnSac = "847130", QuantityText = "1.000", RateText = "100.00", TaxableValue = perLine });

        var taxable = new Money(100m * lineCount);
        var tax = GstService.ComputeLineTax(taxable, 1800, interState: false);
        return new InvoicePrintData
        {
            Seller = new InvoicePartyBlock { Name = "Bright Traders", Gstin = ValidGstin("19AAAAA0000A1Z"), StateText = "West Bengal (19)" },
            Buyer = new InvoicePartyBlock { Name = "Acme Retail", Gstin = ValidGstin("19CCCCC0000C1Z"), StateText = "West Bengal (19)" },
            InvoiceNumber = "INV-LONG",
            InvoiceDateText = "31-03-2025",
            PlaceOfSupply = "West Bengal (19)",
            IsInterState = false,
            Items = items,
            TaxRows = new[] { new InvoiceTaxRow { RateLabel = "18%", TaxableValue = taxable, Cgst = tax.Cgst, Sgst = tax.Sgst, Igst = Money.Zero } },
            TotalTaxable = taxable,
            TotalCgst = tax.Cgst,
            TotalSgst = tax.Sgst,
            TotalIgst = Money.Zero,
        };
    }

    [Fact]
    public void Long_invoice_paginates_and_the_closing_block_is_present_not_clipped()
    {
        var data = ManyLineInvoice(80);
        var bytes = InvoicePdf.Render(data, new PrintConfig(), new PageConfig());
        string s = AsLatin1(bytes);

        // More than one page rendered.
        Assert.True(PdfPageCount(s) > 1, "an 80-line invoice must span more than one page");

        // Every text baseline is at a positive y (nothing clipped off the bottom of a page).
        Assert.True(AllTextYPositive(s), "no text may be drawn at a negative y (clipped off-page)");

        // Closing block content is all present in the bytes (Grand Total, amount-in-words, signature/declaration).
        // taxable 8,000 + tax 1,440 = grand 9,440.
        Assert.Contains("Grand Total", s);
        Assert.Contains("9,440.00", s);
        Assert.Contains("Rupees Nine Thousand Four Hundred Forty Only", s);   // amount-in-words for the grand total
        Assert.Contains("Declaration", s);
        Assert.Contains("Authorised Signatory", s);
        // The item-table header repeats on the continuation page(s).
        Assert.True(CountOccurrences(s, "Description") >= 2, "the item-table column header must repeat on continuation pages");
    }

    // ---- F5(b): the RENDERING half of the Compensation-Cess fix ----

    /// <summary>
    /// <b>F5(b) — the rendering half of FIX-1 was untested.</b> The projector was pinned to carry <c>TotalCess</c> into
    /// the Grand Total, but no test ever RENDERED a cess-bearing invoice: deleting the
    /// <c>TotalLine("Compensation Cess", …)</c> call from <c>InvoicePdf</c> survived the whole suite. The totals box
    /// reserves the row height either way, so the defect is invisible to a layout assertion — the customer simply reads
    /// a Grand Total 1,050 higher than the lines above it, with no line saying why.
    /// <para><b>Bite:</b> delete that <c>TotalLine</c> call and the label + the cess amount vanish from the bytes.</para>
    /// </summary>
    [Fact]
    public void Compensation_cess_prints_its_own_totals_row_and_reaches_the_grand_total()
    {
        var data = CessBearingInvoice();
        string s = AsLatin1(InvoicePdf.Render(data, new PrintConfig(), new PageConfig()));

        Assert.Contains("Compensation Cess", s);   // the charge is NAMED on the bill …
        Assert.Contains("1,050.00", s);            // … at its own amount …
        Assert.Contains("CGST", s);
        Assert.Contains("787.50", s);

        // … and the Grand Total = 8,750 + 1,575 + 1,050 = 11,375, in figures and in words.
        Assert.Equal(11375m, data.GrandTotal.Amount);
        Assert.Contains("11,375.00", s);
        Assert.Contains("Rupees Eleven Thousand Three Hundred Seventy Five Only", s);
    }

    /// <summary>ER-13: an invoice bearing no cess renders exactly as it always did — no row, no label.</summary>
    [Fact]
    public void Cess_free_invoice_prints_no_compensation_cess_row()
    {
        string s = AsLatin1(InvoicePdf.Render(IntraStateInvoice(out _), new PrintConfig(), new PageConfig()));
        Assert.DoesNotContain("Compensation Cess", s);
    }

    /// <summary>The intra-state fixture plus a ₹1,050 ring-fenced Compensation Cess (kept OUT of CGST/SGST).</summary>
    private static InvoicePrintData CessBearingInvoice()
    {
        var data = IntraStateInvoice(out _);
        return new InvoicePrintData
        {
            Seller = data.Seller,
            Buyer = data.Buyer,
            InvoiceNumber = data.InvoiceNumber,
            InvoiceDateText = data.InvoiceDateText,
            PlaceOfSupply = data.PlaceOfSupply,
            IsInterState = false,
            Items = data.Items,
            TaxRows = data.TaxRows,
            TotalTaxable = data.TotalTaxable,
            TotalCgst = data.TotalCgst,
            TotalSgst = data.TotalSgst,
            TotalIgst = data.TotalIgst,
            TotalCess = new Money(1050m),
        };
    }

    // ---- PDF-inspection helpers (parse the content-stream text operators) ----

    /// <summary>The number of page objects in the PDF (from the /Type /Pages Count).</summary>
    private static int PdfPageCount(string latin1)
    {
        var m = System.Text.RegularExpressions.Regex.Match(latin1, @"/Type /Pages /Count (\d+)");
        return m.Success ? int.Parse(m.Groups[1].Value) : 0;
    }

    /// <summary>True iff every "x y Td" text-positioning operator uses a positive y (no clipped text).</summary>
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
