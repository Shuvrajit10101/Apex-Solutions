using System;
using System.Text;
using Apex.Ledger;
using Apex.Ledger.Io;

namespace Apex.Ledger.Io.Tests;

/// <summary>
/// Tests for <see cref="PosReceiptPdf"/> (catalog §11; Phase 6 slice 7 RQ-44; Study Guide pp.240–242; DP-6): a POS
/// bill renders as a retail <b>receipt</b> — title + store, bill meta, the item table, the per-rate GST breakup,
/// taxable + tax + grand total, the tender lines with references and the informational change, then the thank-you
/// messages and declaration. The fixture is the PR-9 worked example (taxable ₹10,225 @ 18% intra ⇒ CGST/SGST
/// 920.25 each, grand ₹12,065.50; multi-tender Gift 500 + Card 5,000 + Cheque 5,000 + Cash residual 1,565.50,
/// cash tendered 1,600 → change 34.50). Deterministic (byte-stable) and de-branded (ER-11: never the word "Tally").
/// </summary>
public sealed class PosReceiptPdfTests
{
    private static string AsLatin1(byte[] bytes) => Encoding.Latin1.GetString(bytes);

    // The PR-9 multi-tender receipt: taxable 10,225 @ 18% intra ⇒ CGST/SGST 920.25 each; grand 12,065.50.
    private static PosReceiptData Pr9MultiTender() => new()
    {
        Title = "Retail Invoice",
        StoreName = "Apex Retail Co",
        BillNumber = "7",
        DateText = "10-Apr-2024",
        Party = "(cash)",
        IsInterState = false,
        Items = new[]
        {
            new PosReceiptItem { Description = "Widget", QuantityText = "1", RateText = "10,225.00", Value = new Money(10225m) },
        },
        TaxRows = new[]
        {
            new PosReceiptTaxRow { RateLabel = "18%", TaxableValue = new Money(10225m), Cgst = new Money(920.25m), Sgst = new Money(920.25m), Igst = Money.Zero },
        },
        Tenders = new[]
        {
            new PosReceiptTender { Label = "Gift Voucher", Amount = new Money(500m) },
            new PosReceiptTender { Label = "Credit/Debit Card", Amount = new Money(5000m), Reference = "Card No. 4111" },
            new PosReceiptTender { Label = "Cheque/DD", Amount = new Money(5000m), Reference = "SBI Cheque No. 235681" },
            new PosReceiptTender { Label = "Cash", Amount = new Money(1565.50m) },
        },
        TotalTaxable = new Money(10225m),
        TotalCgst = new Money(920.25m),
        TotalSgst = new Money(920.25m),
        TotalIgst = Money.Zero,
        CashTendered = new Money(1600m),
        Change = new Money(34.50m),
        Message1 = "Thank you for shopping with us",
        Message2 = "Goods once sold are not returnable",
        Declaration = "Prices inclusive of all taxes",
    };

    [Fact]
    public void Renders_a_valid_debranded_pdf()
    {
        var s = AsLatin1(PosReceiptPdf.Render(Pr9MultiTender(), new PageConfig()));
        Assert.StartsWith("%PDF-", s);
        Assert.Contains("%%EOF", s);
        Assert.DoesNotContain("tally", s.ToLowerInvariant());   // ER-11 brand guard (body AND /Title)
    }

    [Fact]
    public void Receipt_contains_title_totals_tenders_change_messages_and_declaration()
    {
        var s = AsLatin1(PosReceiptPdf.Render(Pr9MultiTender(), new PageConfig()));

        Assert.Contains("Retail Invoice", s);
        Assert.Contains("Apex Retail Co", s);
        Assert.Contains("Bill No: 7", s);

        // Taxable + per-head GST + grand total (12,065.50).
        Assert.Contains("10,225.00", s);
        Assert.Contains("920.25", s);
        Assert.Contains("12,065.50", s);
        Assert.Contains("CGST", s);
        Assert.Contains("SGST", s);
        Assert.DoesNotContain("IGST", s);   // intra-state supply

        // Every tender line + its reference detail.
        Assert.Contains("Gift Voucher", s);
        Assert.Contains("Credit/Debit Card", s);
        Assert.Contains("Card No. 4111", s);
        Assert.Contains("Cheque/DD", s);
        Assert.Contains("SBI Cheque No. 235681", s);
        Assert.Contains("500.00", s);
        Assert.Contains("5,000.00", s);
        Assert.Contains("1,565.50", s);   // cash posts the residual, not the tendered

        // Informational change block: tendered 1,600 → change 34.50.
        Assert.Contains("Cash Tendered", s);
        Assert.Contains("1,600.00", s);
        Assert.Contains("Change", s);
        Assert.Contains("34.50", s);

        // Messages + declaration.
        Assert.Contains("Thank you for shopping with us", s);
        Assert.Contains("Goods once sold are not returnable", s);
        Assert.Contains("Prices inclusive of all taxes", s);
    }

    [Fact]
    public void Inter_state_receipt_shows_igst_not_cgst_sgst()
    {
        var data = new PosReceiptData
        {
            Title = "Retail Invoice",
            StoreName = "Apex Retail Co",
            BillNumber = "9",
            DateText = "10-Apr-2024",
            Party = "North Buyer",
            IsInterState = true,
            Items = new[] { new PosReceiptItem { Description = "Widget", QuantityText = "1", RateText = "10,000.00", Value = new Money(10000m) } },
            TaxRows = new[] { new PosReceiptTaxRow { RateLabel = "18%", TaxableValue = new Money(10000m), Igst = new Money(1800m) } },
            Tenders = new[] { new PosReceiptTender { Label = "Cash", Amount = new Money(11800m) } },
            TotalTaxable = new Money(10000m),
            TotalIgst = new Money(1800m),
            CashTendered = new Money(11800m),
            Change = Money.Zero,
        };
        var s = AsLatin1(PosReceiptPdf.Render(data, new PageConfig()));
        Assert.Contains("IGST", s);
        Assert.DoesNotContain("CGST", s);
        Assert.Contains("1,800.00", s);
        Assert.Contains("11,800.00", s);   // grand total = taxable + IGST
    }

    [Fact]
    public void Same_input_renders_byte_identical()
    {
        var a = PosReceiptPdf.Render(Pr9MultiTender(), new PageConfig());
        var b = PosReceiptPdf.Render(Pr9MultiTender(), new PageConfig());
        Assert.Equal(a, b);   // deterministic: no clock, no RNG
    }

    [Fact]
    public void A_title_carrying_the_forbidden_brand_is_scrubbed()
    {
        var branded = new PosReceiptData
        {
            Title = "Tally Retail Receipt",
            StoreName = "Tally Store",
            BillNumber = "1",
            DateText = "10-Apr-2024",
            Party = "(cash)",
            Items = new[] { new PosReceiptItem { Description = "Widget", QuantityText = "1", RateText = "100.00", Value = new Money(100m) } },
            Tenders = new[] { new PosReceiptTender { Label = "Cash", Amount = new Money(100m) } },
            TotalTaxable = new Money(100m),
            CashTendered = new Money(100m),
            Change = Money.Zero,
        };
        var s = AsLatin1(PosReceiptPdf.Render(branded, new PageConfig()));
        Assert.DoesNotContain("tally", s.ToLowerInvariant());   // scrubbed from body AND /Title
    }

    // ================================================================ W0-1 follow-up: the bill-of-supply receipt

    /// <summary>A §31(3)(c) receipt at an odd value, hostile-loaded: tax figures and a per-rate breakup the document
    /// may not show, on the requested routing.</summary>
    private static PosReceiptData BillOfSupplyReceipt(bool composition, bool interState) => new()
    {
        Title = "Retail Invoice",                    // a print preference — it may not re-title a §31(3)(c) document
        IsBillOfSupply = true,
        TopDeclaration = composition
            ? "Composition taxable person, not eligible to collect tax on supplies"
            : string.Empty,
        StoreName = "Apex Retail Co",
        BillNumber = "11",
        DateText = "10-Apr-2024",
        Party = "Gujarat Buyer",
        IsInterState = interState,
        Items = new[]
        {
            new PosReceiptItem
            {
                Description = "Fresh Milk", QuantityText = "60.125", RateText = "786.64",
                Value = new Money(47_296.73m),
            },
        },
        TaxRows = new[]
        {
            new PosReceiptTaxRow
            {
                RateLabel = "18%", TaxableValue = new Money(47_296.73m),
                Cgst = interState ? Money.Zero : new Money(4_256.71m),
                Sgst = interState ? Money.Zero : new Money(4_256.70m),
                Igst = interState ? new Money(8_513.41m) : Money.Zero,
            },
        },
        Tenders = new[] { new PosReceiptTender { Label = "Cash", Amount = new Money(47_296.73m) } },
        TotalTaxable = new Money(47_296.73m),
        CashTendered = new Money(47_296.73m),
        Change = Money.Zero,
    };

    /// <summary>
    /// <b>W0-1 follow-up review, finding #8 (MEDIUM) — the POS twin of the unrendered inter-State branch.</b> Every
    /// rendered bill-of-supply receipt in the suite is intra-State (the POS fixture never selects a party, so
    /// <c>interState</c> is false), which leaves the <c>!IsBillOfSupply</c> gate around the IGST head line untested in
    /// this renderer as well. <c>DoesNotContain("IGST")</c> on an intra-State receipt proves nothing: that line is
    /// unreachable intra-State whether the gate exists or not.
    /// <para><b>Bite:</b> change <c>if (!data.IsBillOfSupply)</c> to <c>if (!data.IsBillOfSupply ||
    /// data.IsInterState)</c> in <c>PosReceiptPdf</c> and this goes red.</para>
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_bill_of_supply_receipt_shows_no_tax_head_on_either_routing(bool interState)
    {
        var s = AsLatin1(PosReceiptPdf.Render(BillOfSupplyReceipt(composition: true, interState), new PageConfig()));

        Assert.Contains("BILL OF SUPPLY", s);
        Assert.DoesNotContain("Retail Invoice", s);
        Assert.DoesNotContain("IGST", s);
        Assert.DoesNotContain("CGST", s);
        Assert.DoesNotContain("SGST", s);
        Assert.DoesNotContain("GST Breakup", s);
        Assert.DoesNotContain("Taxable", s);
        Assert.Contains("Value of Supply", s);
        Assert.Contains("47,296.73", s);
        Assert.Contains("Composition taxable person, not eligible to collect tax on supplies", s);
    }

    /// <summary>
    /// <b>W0-1 follow-up review, finding #6 (LOW).</b> The Rule 5(1)(f) declaration was drawn on nothing but
    /// <c>TopDeclaration</c> being non-blank — the same caller-trusting shape FIX-W1h removed from the TITLE, which
    /// the renderer's own comment claims makes it "safe against any future caller that does not" gate. A caller could
    /// therefore centre §10's wording over a receipt that then prints CGST 4,256.71 / SGST 4,256.70 head lines. Gated
    /// on the structural flag now, so the declaration and the heads can never appear together.
    /// </summary>
    [Fact]
    public void The_composition_declaration_is_refused_on_a_receipt_that_is_not_a_bill_of_supply()
    {
        const string decl = "Composition taxable person, not eligible to collect tax on supplies";
        var hostile = new PosReceiptData
        {
            Title = "Retail Invoice",
            IsBillOfSupply = false,
            TopDeclaration = decl,
            StoreName = "Apex Retail Co",
            BillNumber = "12",
            DateText = "10-Apr-2024",
            Party = "(cash)",
            IsInterState = false,
            Items = new[]
            {
                new PosReceiptItem
                {
                    Description = "Widget", QuantityText = "60.125", RateText = "786.64",
                    Value = new Money(47_296.73m),
                },
            },
            Tenders = new[] { new PosReceiptTender { Label = "Cash", Amount = new Money(55_810.14m) } },
            TotalTaxable = new Money(47_296.73m),
            TotalCgst = new Money(4_256.71m),
            TotalSgst = new Money(4_256.70m),
            CashTendered = new Money(55_810.14m),
            Change = Money.Zero,
        };

        var s = AsLatin1(PosReceiptPdf.Render(hostile, new PageConfig()));
        Assert.Contains("CGST", s);                  // it really is a taxed receipt …
        Assert.Contains("4,256.71", s);
        Assert.DoesNotContain(decl, s);              // … so §10's wording may not appear on it
        Assert.DoesNotContain("Composition taxable person", s);
    }
}
