using System;
using System.Collections.Generic;
using System.Text;
using Apex.Ledger;
using Apex.Ledger.Io;
using Apex.Ledger.Reports;
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
    private static InvoicePrintData BillOfSupply(bool composition, bool interState = false)
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
                Name = "Acme Retail",
                Gstin = ValidGstin(interState ? "24CCCCC0000C1Z" : "19CCCCC0000C1Z"),
                StateText = interState ? "Gujarat (24)" : "West Bengal (19)",
            },
            InvoiceNumber = "BOS-0417",
            InvoiceDateText = "31-03-2025",
            PlaceOfSupply = interState ? "Gujarat (24)" : "West Bengal (19)",
            IsInterState = interState,
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
                    Cgst = interState ? Money.Zero : new Money(4_256.71m),
                    Sgst = interState ? Money.Zero : new Money(4_256.70m),
                    Igst = interState ? new Money(8_513.41m) : Money.Zero,
                },
            },
            TotalTaxable = value,
            // W0-1 follow-up review, finding #8: the hostile caller also loads the INTER-State and CESS branches, the
            // two the whole slice never rendered once. `IsInterState` selects a DIFFERENT head line in all four
            // renderers, so `!IsBillOfSupply` on the intra branch alone proves nothing about the inter one.
            TotalIgst = interState ? new Money(8_513.41m) : Money.Zero,
            TotalCess = interState ? new Money(2_364.83m) : Money.Zero,
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

        // FIX-W1f: the document-number caption must name the document this IS. DrawFirstHeader hardcoded
        // "Invoice No: " on every page, so a bill of supply called its own serial an invoice number directly under a
        // title band reading BILL OF SUPPLY — and disagreed with the on-screen preview mirror, which W0-1 had already
        // changed to "Bill of Supply No.". No test in the slice inspected the number row.
        Assert.Contains("Bill of Supply No", s);
        Assert.DoesNotContain("Invoice No", s);

        // W0-1 follow-up review, finding #9: the Place of Supply row IS printed on a bill of supply —
        // `DrawFirstHeader` emits it unconditionally (InvoicePdf.cs); only the intra/inter HEAD CAPTION beside it is
        // gated on `!IsBillOfSupply`. Nothing asserted its PRESENCE, so "completing" the suppression the
        // ServiceAccountingInvoicePrintFixTests comment described would have dropped the recipient's place of supply
        // from every bill of supply with the suite still green. Pinned here so the row cannot go silently.
        Assert.Contains(@"Place of Supply: West Bengal \(19\)", s);   // PDF literal-string escaping of ( )

        // Rule 49(g): value of supply — and it is the whole of what the recipient owes.
        Assert.Contains("Value of Supply", s);
        Assert.Contains("47,296.73", s);
        Assert.Contains("Rupees Forty Seven Thousand Two Hundred Ninety Six and Seventy Three Paise Only", s);
        Assert.Contains("this bill of supply shows the actual price", s);
        Assert.Contains("Authorised Signatory", s);
        Assert.DoesNotContain("tally", s.ToLowerInvariant());
    }

    /// <summary>
    /// <b>W0-1 follow-up review, finding #8 (MEDIUM) — the inter-State bill of supply nobody ever rendered.</b>
    /// Every rendered bill-of-supply fixture in the repository is INTRA-State: this file's own factory hardcoded
    /// <c>IsInterState = false</c>, every <c>BillOfSupplyRoutingTests</c> customer is State 27 against home 27, and the
    /// POS fixture selects no party at all. The suite DOES post inter-State bills of supply (a wholly exempt service
    /// to a Gujarat party) but stops at the DTO. So the <c>!IsBillOfSupply</c> gate around the IGST head line had
    /// <b>zero</b> coverage: the one-token slip <c>if (!data.IsBillOfSupply || data.IsInterState)</c> would print
    /// <c>IGST</c> on a Rule-49 document handed to a customer, with all four per-project suites still green — because
    /// the intra-State <c>DoesNotContain("IGST")</c> assertions cannot reach a line that is unreachable intra-State
    /// whether the gate is there or not.
    /// <para><b>Bite:</b> change <c>if (!data.IsBillOfSupply)</c> to <c>if (!data.IsBillOfSupply ||
    /// data.IsInterState)</c> in <c>InvoicePdf.DrawClosingBlock</c> and this goes red on the IGST assertion.</para>
    /// </summary>
    [Fact]
    public void An_inter_state_bill_of_supply_shows_no_igst_no_cess_and_no_routing_caption()
    {
        string s = AsLatin1(InvoicePdf.Render(BillOfSupply(composition: true, interState: true),
            new PrintConfig(), new PageConfig()));

        Assert.StartsWith("%PDF-", s);
        Assert.Contains("BILL OF SUPPLY", s);
        Assert.Contains("Bill of Supply No", s);
        Assert.DoesNotContain("Invoice No", s);

        // Rule 49 prescribes no rate and no tax-amount particular — on EITHER routing.
        Assert.DoesNotContain("IGST", s);
        Assert.DoesNotContain("CGST", s);
        Assert.DoesNotContain("SGST", s);
        Assert.DoesNotContain("Compensation Cess", s);
        Assert.DoesNotContain("GST Breakup", s);
        Assert.DoesNotContain("Inter-State", s);
        Assert.DoesNotContain("Intra-State", s);
        Assert.DoesNotContain("8,513.41", s);      // the suppressed IGST figure
        Assert.DoesNotContain("2,364.83", s);      // the suppressed cess figure

        // …and Rule 49(g)'s value of supply, plus the recipient's place of supply, are still stated.
        Assert.Contains("Value of Supply", s);
        Assert.Contains("47,296.73", s);
        Assert.Contains(@"Place of Supply: Gujarat \(24\)", s);
        Assert.Contains("Composition taxable person, not eligible to collect tax on supplies", s);
    }

    /// <summary>
    /// <b>W0-1 follow-up review, finding #6 (LOW) — the declaration was still caller-trusted after the TITLE stopped
    /// being.</b> FIX-W1h/FIX-W2b made the title renderer-derived precisely so the renderer would be "safe against any
    /// future caller that does not" gate it. The Rule 5(1)(f) declaration was left drawn on nothing but
    /// <c>TopDeclaration</c> being non-blank, so a caller could centre "Composition taxable person, not eligible to
    /// collect tax on supplies" over a page that goes on to print CGST 4,256.71 and SGST 4,256.70 — the exact
    /// badge/declaration contradiction FIX-W1e removed from the drilled-voucher pane, reborn in the renderer. Not
    /// reachable from <c>src/</c> today (both callers gate it), so this closes a hardening gap, not a live defect.
    /// </summary>
    [Fact]
    public void The_composition_declaration_is_refused_on_a_document_that_is_not_a_bill_of_supply()
    {
        const string decl = "Composition taxable person, not eligible to collect tax on supplies";
        var hostile = IntraStateInvoice(out var engineTax);
        var taxInvoiceCarryingTheDeclaration = new InvoicePrintData
        {
            DocumentTitle = hostile.DocumentTitle,
            IsBillOfSupply = false,
            TopDeclaration = decl,                    // the mirror of the mistake FIX-W1h fixed on the title
            Seller = hostile.Seller,
            Buyer = hostile.Buyer,
            InvoiceNumber = "INV-0432",
            InvoiceDateText = "31-03-2025",
            PlaceOfSupply = "West Bengal (19)",
            IsInterState = false,
            Items = hostile.Items,
            TaxRows = hostile.TaxRows,
            TotalTaxable = hostile.TotalTaxable,
            TotalCgst = engineTax.Cgst,
            TotalSgst = engineTax.Sgst,
        };

        string s = AsLatin1(InvoicePdf.Render(taxInvoiceCarryingTheDeclaration, new PrintConfig(), new PageConfig()));
        Assert.Contains("TAX INVOICE", s);
        Assert.Contains("CGST", s);                   // it really is a taxed document …
        Assert.DoesNotContain(decl, s);               // … so §10's wording may not appear on it
        Assert.DoesNotContain("Composition taxable person", s);
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

    /// <summary>
    /// <b>FIX-W1h — the renderer's safety net could not fire, because the DTO's default defeated it.</b>
    /// <c>InvoicePdf.Render</c> handled a bill of supply as "take <c>DocumentTitle</c>; if BLANK, use BILL OF SUPPLY",
    /// while <c>InvoicePrintData.DocumentTitle</c> defaulted to the non-blank string "TAX INVOICE". So a caller
    /// writing <c>new InvoicePrintData { IsBillOfSupply = true, … }</c> and forgetting the title got a page with
    /// every tax head, the breakup table and the intra/inter caption correctly suppressed — and the title
    /// "TAX INVOICE", stamped into the PDF metadata too. That is exactly the document §31(3)(c) forbids, produced by
    /// the renderer whose own comment advertises it is "safe against any future caller". The default is now empty AND
    /// the branch rejects the tax-invoice title structurally, so neither side has to remember.
    /// </summary>
    [Fact]
    public void A_caller_that_sets_only_the_bill_of_supply_flag_never_gets_a_page_titled_tax_invoice()
    {
        var minimal = new InvoicePrintData
        {
            IsBillOfSupply = true,
            InvoiceNumber = "BOS-0418",
            InvoiceDateText = "31-03-2025",
            TotalTaxable = new Money(47_296.73m),
        };
        string s = AsLatin1(InvoicePdf.Render(minimal, new PrintConfig(), new PageConfig()));

        Assert.Contains("BILL OF SUPPLY", s);
        Assert.DoesNotContain("TAX INVOICE", s);      // incl. SafeTitle's /Title metadata
        Assert.DoesNotContain("Tax Invoice", s);

        // …and the same holds when the DTO carries the tax-invoice title outright (a stale/hand-built projection).
        string stale = AsLatin1(InvoicePdf.Render(
            new InvoicePrintData
            {
                IsBillOfSupply = true, DocumentTitle = "TAX INVOICE", InvoiceNumber = "BOS-0419",
                InvoiceDateText = "31-03-2025", TotalTaxable = new Money(47_296.73m),
            },
            new PrintConfig(), new PageConfig()));
        Assert.Contains("BILL OF SUPPLY", stale);
        Assert.DoesNotContain("TAX INVOICE", stale);
    }

    /// <summary>
    /// <b>FIX-W2b — the FIX-W1h safety net was spelled ORDINALLY, so one letter of case defeated it.</b> The guard
    /// read <c>title == GstReportSupport.TaxInvoiceTitle</c>, i.e. an exact match on the upper-case constant. A DTO
    /// carrying "Tax Invoice" — the exact spelling <c>VoucherDetailViewModel.DocumentLabel</c> uses on screen, and the
    /// spelling this project's own prose uses throughout — therefore sailed straight through and printed a bill of
    /// supply headed "Tax Invoice", with that string stamped into the PDF <c>/Title</c> metadata as well. Every tax
    /// head, the breakup and the intra/inter caption were still correctly suppressed, so the ONLY thing wrong with the
    /// page was what it called itself — which is precisely the failure §31(3)(c) is about.
    /// <para>Locks the casing variants the guard has to survive, including surrounding whitespace.</para>
    /// </summary>
    [Theory]
    [InlineData("Tax Invoice")]
    [InlineData("tax invoice")]
    [InlineData("Tax INVOICE")]
    [InlineData("  TAX INVOICE  ")]
    public void A_bill_of_supply_rejects_the_tax_invoice_title_in_any_casing(string documentTitle)
    {
        string s = AsLatin1(InvoicePdf.Render(
            new InvoicePrintData
            {
                IsBillOfSupply = true, DocumentTitle = documentTitle, InvoiceNumber = "BOS-0420",
                InvoiceDateText = "31-03-2025", TotalTaxable = new Money(47_296.73m),
            },
            new PrintConfig(), new PageConfig()));

        Assert.Contains("BILL OF SUPPLY", s);
        Assert.DoesNotContain("TAX INVOICE", s);
        Assert.DoesNotContain("Tax Invoice", s);
        Assert.DoesNotContain("tax invoice", s);
        Assert.DoesNotContain("Tax INVOICE", s);
    }

    /// <summary>An ordinary tax invoice whose projection sets no <c>DocumentTitle</c> at all still renders "TAX
    /// INVOICE" — the empty default must not blank the title of the common document (ER-13).</summary>
    [Fact]
    public void A_tax_invoice_with_no_explicit_document_title_still_renders_tax_invoice()
    {
        string s = AsLatin1(InvoicePdf.Render(IntraStateInvoice(out _), new PrintConfig(), new PageConfig()));
        Assert.Contains("TAX INVOICE", s);
        Assert.Contains("Invoice No", s);
        Assert.DoesNotContain("BILL OF SUPPLY", s);
    }

    // ================================ T0-11 slice S2 (RQ-11a): RECIPIENT-SIDE RECORD — the renderer's safety property
    //
    // 🔴 WHY THESE LIVE HERE, in Apex.Ledger.Io.Tests, and not only in Apex.Desktop.Tests.
    // The refusal in `InvoicePdf.Render`'s `IsRecipientRecord` branch is THE safety property of the whole T0-11
    // change: it is what stops this renderer ever printing a document CGST §31(1) / Rule 49 do not entitle us to
    // issue. It shipped with its ONLY coverage in a DIFFERENT project — deleting the entire four-line guard left
    // Apex.Ledger.Io.Tests at a full green, and `IsRecipientRecord` did not appear in this file even once. A safety
    // net the owning project cannot feel is one refactor away from being deleted by someone who runs this suite,
    // sees green, and concludes the guard is dead code. Everything below drives `InvoicePdf` DIRECTLY, from
    // hand-built DTOs, with no projector and no view model in the path — which is also the only way to prove the
    // renderer is safe against a caller that fills the DTO wrongly rather than merely safe when the projector fills
    // it rightly (the same standard FIX-W1h/FIX-W2b set for the bill-of-supply branch above).

    /// <summary>
    /// A recipient-side record of an inward supply, built as a HOSTILE caller would: the flag set, the tax rows
    /// populated, and <paramref name="documentTitle"/> whatever the caller pleases — including the two outward titles
    /// the renderer must refuse. Taxable ₹4,321.00 @ 18% intra-State ⇒ CGST 388.89 + SGST 388.89, grand 5,098.78.
    /// </summary>
    private static InvoicePrintData RecipientRecord(string documentTitle)
    {
        var taxable = new Money(4_321m);
        var tax = GstService.ComputeLineTax(taxable, 1800, interState: false);
        return new InvoicePrintData
        {
            IsRecipientRecord = true,
            DocumentTitle = documentTitle,
            // On a record the LEFT block is the real supplier and the RIGHT block is us (Rule 46(a)).
            Seller = new InvoicePartyBlock
            {
                Name = "Gujarat Supplier", AddressLines = new[] { "9 Dockyard Road", "Surat" },
                Gstin = ValidGstin("24EEEEE0000E1Z"), StateText = "Gujarat (24)",
            },
            Buyer = new InvoicePartyBlock
            {
                Name = "Bright Traders", AddressLines = new[] { "12 Market Street", "Kolkata" },
                Gstin = ValidGstin("19AAAAA0000A1Z"), StateText = "West Bengal (19)",
            },
            InvoiceNumber = "PUR-0007",
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

    /// <summary>
    /// <b>(a)/(b) — the refusal itself, in the project that owns the renderer.</b> CGST Act §31(1)/(2) put the tax
    /// invoice on "a registered person <b>supplying</b>" and CGST Rule 49 opens the bill of supply with the same
    /// words, so on a document recording a supply made TO us <b>neither</b> outward title may appear. The guard
    /// SUBSTITUTES rather than throws — it rewrites the title to <see cref="GstReportSupport.PurchaseRecordTitle"/> —
    /// so that substitution is what is asserted, on the body text and on the <c>/Title</c> metadata alike (both live
    /// in these bytes, which is how "Tax Invoice" reached the metadata undetected once already: FIX-W2b).
    /// <para>The casing and padding variants are not decoration: the guard claims to be case-insensitive and to trim,
    /// and the ordinal spelling of exactly this guard on the bill-of-supply branch let "Tax Invoice" — the spelling
    /// this app's own drill badge uses — through to paper once already.</para>
    /// <para><b>Bite:</b> delete the four-line <c>if (string.IsNullOrWhiteSpace(title) || … ) title =
    /// GstReportSupport.PurchaseRecordTitle;</c> block from <c>InvoicePdf.Render</c> and every row here goes red in
    /// THIS project.</para>
    /// </summary>
    [Theory]
    [InlineData("TAX INVOICE")]
    [InlineData("Tax Invoice")]
    [InlineData("tax invoice")]
    [InlineData("  Tax Invoice  ")]
    [InlineData("\tTAX INVOICE\r\n")]
    [InlineData("BILL OF SUPPLY")]
    [InlineData("Bill of Supply")]
    [InlineData("  bill of supply  ")]
    [InlineData("")]
    [InlineData("   ")]
    public void A_recipient_record_refuses_both_outward_titles_and_a_blank_one(string documentTitle)
    {
        string s = AsLatin1(InvoicePdf.Render(RecipientRecord(documentTitle), new PrintConfig(), new PageConfig()));

        Assert.StartsWith("%PDF-", s);
        Assert.Contains(GstReportSupport.PurchaseRecordTitle, s);   // "PURCHASE RECORD" — body band AND /Title
        Assert.DoesNotContain("TAX INVOICE", s);
        Assert.DoesNotContain("Tax Invoice", s);
        Assert.DoesNotContain("tax invoice", s);
        Assert.DoesNotContain("BILL OF SUPPLY", s);
        Assert.DoesNotContain("Bill of Supply", s);
        Assert.DoesNotContain("bill of supply", s);

        // The substitution is not cosmetic — it lands on a page that is a record throughout: our number is captioned
        // as ours, no place of supply is stated (Rule 46(n) is a supplier particular), the tax IS stated but as the
        // supplier's charge, and OUR declaration and signature block are gone (Rule 46(q) puts those on the issuer).
        Assert.Contains(GstReportSupport.RecordNumberCaption, s);
        Assert.DoesNotContain("Invoice No", s);
        Assert.DoesNotContain("Place of Supply", s);
        Assert.Contains(GstReportSupport.SupplierTaxCaption, s);
        Assert.DoesNotContain("GST Breakup", s);
        Assert.DoesNotContain("Authorised Signatory", s);
        Assert.DoesNotContain("For Gujarat Supplier", s);
        Assert.DoesNotContain("tally", s.ToLowerInvariant());
    }

    /// <summary>
    /// <b>(a) again, through the OTHER door — the F12 title override.</b> The override is a print preference; the
    /// document kind follows the transaction. A knob able to re-title a record into a tax invoice would issue,
    /// through the print dialog, precisely the document §31(1) denies us — so the override must not reach this
    /// branch at all, exactly as it does not reach a bill of supply.
    /// </summary>
    [Theory]
    [InlineData("TAX INVOICE")]
    [InlineData("Tax Invoice")]
    [InlineData("BILL OF SUPPLY")]
    public void The_title_override_cannot_promote_a_recipient_record_into_a_document_we_issue(string overrideTitle)
    {
        string s = AsLatin1(InvoicePdf.Render(
            RecipientRecord(GstReportSupport.PurchaseRecordTitle),
            new PrintConfig { TitleOverride = overrideTitle },
            new PageConfig()));

        Assert.Contains(GstReportSupport.PurchaseRecordTitle, s);
        Assert.DoesNotContain("TAX INVOICE", s);
        Assert.DoesNotContain("Tax Invoice", s);
        Assert.DoesNotContain("BILL OF SUPPLY", s);
        Assert.DoesNotContain("Bill of Supply", s);
    }

    /// <summary>
    /// <b>(c) — the guard is a refusal of two titles, not a hard-coding of one.</b> A record title that is neither
    /// outward title passes through UNCHANGED and reaches the page: the branch reads <c>title = data.DocumentTitle</c>
    /// first and only rewrites it when the title is blank or one of the two forbidden spellings. Without this, a
    /// "fix" that replaced the guard with an unconditional <c>title = PurchaseRecordTitle</c> would still pass every
    /// refusal probe above while silently discarding whatever the projector chose to call the document.
    /// </summary>
    [Theory]
    [InlineData("PURCHASE RECORD")]
    [InlineData("INWARD SUPPLY RECORD")]
    [InlineData("Goods Received Note")]
    public void A_recipient_record_keeps_a_title_that_is_not_an_outward_one(string documentTitle)
    {
        string s = AsLatin1(InvoicePdf.Render(RecipientRecord(documentTitle), new PrintConfig(), new PageConfig()));

        Assert.Contains(documentTitle, s);
        Assert.DoesNotContain("TAX INVOICE", s);
        Assert.DoesNotContain("BILL OF SUPPLY", s);

        // …and the record's own particulars are on the page with it: our reference, the supplier-captioned tax at the
        // engine's paisa-exact figures, and the legend that says whose document this is.
        Assert.Contains("PUR-0007", s);
        Assert.Contains(GstReportSupport.SupplierTaxCaption, s);
        Assert.Contains("388.89", s);        // CGST = SGST = 9% of 4,321.00
        Assert.Contains("4,321.00", s);
        Assert.Contains("5,098.78", s);      // grand total = taxable + 777.78 tax
        // The legend is WRAPPED across several lines by the renderer, so only its opening survives contiguously.
        Assert.Contains("This is the recipient", s);
    }

    /// <summary>
    /// <b>(d) — THE NEGATIVE CONTROL, and the reason it is not optional.</b> Every probe above asserts that something
    /// does NOT appear, and a blanket refusal — a guard widened by one mistaken edit to fire whatever the flag says —
    /// satisfies all of them while breaking every outward invoice this app prints. So: the same DTO shape, the same
    /// "TAX INVOICE" title, <c>IsRecipientRecord = <b>false</b></c>, and the ordinary Rule-46 document must come out
    /// whole — title, "Invoice No.", place of supply, the GST breakup caption, our declaration and our signature.
    /// </summary>
    [Fact]
    public void The_guard_is_gated_on_the_flag_a_tax_invoice_with_the_same_title_still_renders_whole()
    {
        var record = RecipientRecord(GstReportSupport.TaxInvoiceTitle);
        var outward = new InvoicePrintData
        {
            IsRecipientRecord = false,                       // the ONLY difference from the fixture above
            DocumentTitle = GstReportSupport.TaxInvoiceTitle,
            Seller = record.Seller,
            Buyer = record.Buyer,
            InvoiceNumber = record.InvoiceNumber,
            InvoiceDateText = record.InvoiceDateText,
            PlaceOfSupply = record.PlaceOfSupply,
            IsInterState = false,
            Items = record.Items,
            TaxRows = record.TaxRows,
            TotalTaxable = record.TotalTaxable,
            TotalCgst = record.TotalCgst,
            TotalSgst = record.TotalSgst,
            TotalIgst = record.TotalIgst,
        };

        string s = AsLatin1(InvoicePdf.Render(outward, new PrintConfig(), new PageConfig()));

        Assert.Contains("TAX INVOICE", s);
        Assert.Contains("Invoice No", s);
        Assert.Contains(@"Place of Supply: West Bengal \(19\)", s);   // PDF literal-string escaping of ( )
        Assert.Contains("GST Breakup", s);
        Assert.Contains("Declaration", s);
        Assert.Contains("Authorised Signatory", s);
        Assert.Contains("For Gujarat Supplier", s);
        Assert.Contains("388.89", s);

        // …and none of the record-only wording leaks onto a document we DID issue.
        Assert.DoesNotContain(GstReportSupport.PurchaseRecordTitle, s);
        Assert.DoesNotContain(GstReportSupport.RecordNumberCaption, s);
        Assert.DoesNotContain(GstReportSupport.SupplierTaxCaption, s);
        Assert.DoesNotContain("This is the recipient's record", s);
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
        // CGST Rule 48(1)(b): "the duplicate copy being marked as DUPLICATE FOR TRANSPORTER". This assertion read
        // "DUPLICATE FOR SUPPLIER" until T0-11 review C10/L1-10 — the duplicate and triplicate captions shipped
        // transposed, and this line was one of the two green tests locking the transposition in. Re-pointed to the
        // rule, whose verbatim text and CBIC source are on `CopyMarking`; the pairing is exercised on its own in
        // CopyMarkingRule48Tests.
        Assert.Contains("DUPLICATE FOR TRANSPORTER", s);
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
        // CGST Rule 48(1)(c): "the triplicate copy being marked as TRIPLICATE FOR SUPPLIER" — the issuer's own
        // retained copy. This assertion read "TRIPLICATE FOR TRANSPORTER" until T0-11 review C10/L1-10; see the
        // note on the duplicate above.
        Assert.Contains("TRIPLICATE FOR SUPPLIER", trip);

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
