using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Reports;
using Apex.Ledger.Services;
using Xunit;
using Domain = Apex.Ledger.Domain;

namespace Apex.Ledger.Io.Tests;

/// <summary>
/// Phase 7 slice 7 — the TDS/TCS statutory <b>certificates &amp; control chart</b> rendered as PDFs through the same
/// deterministic, de-branded pipeline as the GST tax invoice (<see cref="InvoicePdf"/> / <see cref="PdfWriter"/> /
/// <see cref="IndianAmountInWords"/>): <see cref="Form16APdf"/> (TDS cert), <see cref="Form27DPdf"/> (TCS cert) and
/// <see cref="Form27APdf"/> (return control chart). Each certificate's figures match the corresponding
/// <see cref="Form26Q"/> / <see cref="Form27EQ"/> projection exactly; every PDF is byte-identical across two runs and
/// carries no third-party brand (even from a "Tally"-named party); ER-13 (no TDS/TCS ⇒ an empty, still-valid PDF).
/// </summary>
public sealed class CertificatePdfTests
{
    private static string AsLatin1(byte[] bytes) => Encoding.Latin1.GetString(bytes);

    private const string ValidTan = "MUMA12345B";
    private const string DeducteePan = "AAPFU0939F";
    private const string BuyerPan = "AAQCS1234K";
    private const string GstinMaharashtra = "27AAPFU0939F1ZV";
    private static readonly DateOnly FyStart = new(2025, 4, 1);

    // ---------------------------------------------------------------- TDS fixture (194J golden) ----

    private static Domain.Ledger AddLedger(Company c, string name, string groupName, bool openingIsDebit)
    {
        var l = new Domain.Ledger(Guid.NewGuid(), name, c.FindGroupByName(groupName)!.Id, Money.Zero, openingIsDebit);
        c.AddLedger(l);
        return l;
    }

    private static (Company C, Guid DeducteeId) BuildTdsCompany(string vendorName = "Consultant", string? partyPan = null)
    {
        var c = CompanyFactory.CreateSeeded("Return Co", FyStart);
        new TdsTcsService(c).EnableTds(new TdsConfig
        {
            Tan = ValidTan, DeductorType = DeductorType.Company,
            ResponsiblePersonName = "A. Sharma", ResponsiblePersonPan = DeducteePan,
            ResponsiblePersonDesignation = "Director", ResponsiblePersonAddress = "12 MG Road",
        });
        var nop = c.FindNatureOfPaymentByCode("194J(b)")!;
        var vendor = AddLedger(c, vendorName, "Sundry Creditors", false);
        vendor.TdsApplicable = true; vendor.TdsNatureOfPaymentId = nop.Id; vendor.DeducteeType = DeducteeType.Firm;
        vendor.PartyPan = partyPan ?? DeducteePan;

        var on = new DateOnly(2025, 5, 10);
        var fees = AddLedger(c, "Professional Fees", "Indirect Expenses", true);
        var gross = Money.FromRupees(1_00_000m);
        var carve = new TdsService(c).BuildCarveOut(gross, gross, nop, vendor, on);
        new LedgerService(c).Post(new Voucher(Guid.NewGuid(), c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Journal).Id, on,
            new[] { new EntryLine(fees.Id, gross, DrCr.Debit), carve.PartyLine, carve.TdsPayableLine! }));

        var dep = new TdsDepositService(c);
        var statType = dep.EnsureStatPaymentType();
        var bank = AddLedger(c, "HDFC Bank", "Bank Accounts", true);
        var posted = new LedgerService(c).Post(dep.BuildStatPayment(Money.FromRupees(10_000m), bank, new DateOnly(2025, 6, 5), statType));
        dep.RecordChallan("00123", "0510308", new DateOnly(2025, 6, 5), Money.FromRupees(10_000m), "194J(b)", "200", posted);

        return (c, vendor.Id);
    }

    // ---------------------------------------------------------------- TCS fixture (scrap golden) ----

    private static (Company C, Guid CollecteeId) BuildTcsCompany(string buyerName = "Scrap Buyer")
    {
        var c = CompanyFactory.CreateSeeded("Collecting Co", FyStart);
        new TdsTcsService(c).EnableTcs(new TcsConfig
        {
            Tan = ValidTan, CollectorType = DeductorType.Company,
            ResponsiblePersonName = "A. Sharma", ResponsiblePersonPan = "AAPFU0939F",
            ResponsiblePersonDesignation = "Director", ResponsiblePersonAddress = "12 MG Road",
        });
        new GstService(c).EnableGst(new GstConfig
        {
            HomeStateCode = "27", Gstin = GstinMaharashtra, RegistrationType = GstRegistrationType.Regular,
            ApplicableFrom = FyStart, Periodicity = GstReturnPeriodicity.Monthly,
        });

        var inv = new InventoryService(c);
        var scrap = inv.CreateStockItem("Scrap Metal", inv.CreateStockGroup("Waste").Id, inv.CreateSimpleUnit("Kg", "Kilogram").Id);
        scrap.Gst = new StockItemGstDetails { Taxability = GstTaxability.Taxable, RateBasisPoints = 1800 };
        scrap.TcsNatureOfGoodsId = c.FindNatureOfGoodsByCode("6CE")!.Id;
        var main = c.MainLocation!.Id;

        var sales = AddLedger(c, "Scrap Sales", "Sales Accounts", false);
        var buyer = AddLedger(c, buyerName, "Sundry Debtors", true);
        buyer.PartyGst = new PartyGstDetails { RegistrationType = GstRegistrationType.Regular, Gstin = GstinMaharashtra, StateCode = "27" };
        buyer.TcsApplicable = true; buyer.CollecteeType = CollecteeType.Individual; buyer.PartyPan = BuyerPan;

        var purchases = AddLedger(c, "Purchases", "Purchase Accounts", true);
        var creditor = AddLedger(c, "Creditor", "Sundry Creditors", false);
        new LedgerService(c).Post(new Voucher(Guid.NewGuid(), c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Purchase).Id,
            c.BooksBeginFrom,
            new[] { new EntryLine(purchases.Id, Money.FromRupees(5_00_000m), DrCr.Debit), new EntryLine(creditor.Id, Money.FromRupees(5_00_000m), DrCr.Credit) },
            inventoryLines: new[] { new VoucherInventoryLine(scrap.Id, main, 10_000m, Money.FromRupees(50m)) }));

        var on = new DateOnly(2025, 5, 10);
        var gst = new GstService(c);
        var value = Money.FromRupees(1_00_000m);
        var interState = gst.IsInterState(buyer.PartyGst!.StateCode);
        var tax = gst.ComputeInvoiceTax(new[] { new GstService.TaxableLine(value, 1800) }, interState, GstTaxDirection.Output);
        var nature = new TcsService(c).ResolveNature(scrap, sales)!;
        var col = new TcsService(c).BuildCollection(value, tax.TotalTax, nature, buyer, on);
        var lines = new List<EntryLine>
        {
            new(buyer.Id, value + tax.TotalTax + col.TcsAmount, DrCr.Debit),
            new(sales.Id, value, DrCr.Credit),
        };
        lines.AddRange(tax.TaxLines);
        lines.Add(col.TcsPayableLine!);
        new LedgerService(c).Post(new Voucher(Guid.NewGuid(), c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Sales).Id, on, lines,
            inventoryLines: new[] { new VoucherInventoryLine(scrap.Id, main, 1000m, Money.FromRupees(100m)) }));

        var dep = new TcsDepositService(c);
        var statType = dep.EnsureStatPaymentType();
        var bank = AddLedger(c, "HDFC Bank", "Bank Accounts", true);
        var posted = new LedgerService(c).Post(dep.BuildStatPayment(Money.FromRupees(1_180m), bank, new DateOnly(2025, 6, 5), statType));
        dep.RecordChallan("00123", "0510308", new DateOnly(2025, 6, 5), Money.FromRupees(1_180m), "6CE", "200", posted);

        return (c, buyer.Id);
    }

    // ================================================================ Form 16A (TDS certificate)

    [Fact]
    public void Form16A_renders_a_valid_debranded_pdf_matching_the_26Q_row()
    {
        var (c, deducteeId) = BuildTdsCompany();
        var cert = Form16A.Build(c, 2025, 1, deducteeId);
        var bytes = Form16APdf.Render(cert, new PageConfig());
        string s = AsLatin1(bytes);

        Assert.StartsWith("%PDF-", s);
        Assert.Contains("%%EOF", s);
        Assert.Contains("/Producer (Apex Solutions)", s);
        Assert.DoesNotContain("tally", s.ToLowerInvariant());
        Assert.Contains("FORM 16A", s);

        // The 26Q figures appear on the certificate: TAN, deductee PAN, section, FVU code, amounts.
        Assert.Contains(ValidTan, s);
        Assert.Contains(DeducteePan, s);
        Assert.Contains("94J-B", s);
        Assert.Contains("1,00,000.00", s);
        Assert.Contains("10,000.00", s);
        Assert.Contains("00123", s);   // challan serial
        Assert.Contains("0510308", s); // BSR code

        // Amount-in-words of the total TDS deducted.
        Assert.Contains(IndianAmountInWords.Convert(10_000m), s);
    }

    [Fact]
    public void Form16A_is_byte_identical_across_two_runs()
    {
        var (c, deducteeId) = BuildTdsCompany();
        var cert = Form16A.Build(c, 2025, 1, deducteeId);
        var a = Form16APdf.Render(cert, new PageConfig());
        var b = Form16APdf.Render(cert, new PageConfig());
        Assert.Equal(a, b);
    }

    /// <summary>
    /// 🔴 <b>INVERTED BY RULING 18 — this test previously asserted the defect.</b> It was named
    /// <c>Form16A_debrands_a_tally_named_deductee</c> and demanded that a deductee legally named "Tally
    /// Consultants" have that token stripped. The deductee is the COUNTERPARTY: Form 16A is issued TO them and
    /// the name is matched against their PAN by the department, so the old behaviour handed a supplier a
    /// statutory certificate naming somebody who does not exist, and disagreed with the 26Q return filed about
    /// them. The deductor block, the "For &lt;deductor&gt;" signatory line and the <c>/Title</c> metadata are OURS
    /// and are still asserted brand-free in the same breath — de-branding did not stop, it stopped at the
    /// counterparty.
    /// </summary>
    [Fact]
    public void Form16A_keeps_a_deductees_own_name_intact_while_our_side_stays_debranded()
    {
        var (c, deducteeId) = BuildTdsCompany("Tally Consultants");
        var cert = Form16A.Build(c, 2025, 1, deducteeId);
        string s = AsLatin1(Form16APdf.Render(cert, new PageConfig()));

        // The counterparty's legal name is printed EXACTLY as it stands in the books.
        Assert.Contains("Tally Consultants", s);
        // Our own strings still carry no third-party brand: the ONLY occurrence of the token in the whole
        // document is the one inside the deductee's own name. A leak anywhere else raises the count.
        Assert.Equal(1, OccurrencesOfBrand(s));
        Assert.Contains("/Producer (Apex Solutions)", s);
        Assert.Contains("For Return Co", s);
    }

    /// <summary>
    /// 🔴 <b>The deductee's PAN is theirs too, and the guard was mangling it into an invalid one.</b> The Name
    /// line was exempted from the ER-11 scrub when ruling 18 landed; the PAN line IMMEDIATELY BELOW IT was not.
    /// A PAN is 5 letters + 4 digits + 1 letter, and the vendor token is a structurally valid 5-letter prefix
    /// (4th character L = Local Authority), so a real PAN <c>TALLY1234F</c> was printed on the certificate as
    /// <c>1234F</c> — not a PAN at all, and not the number filed in the 26Q return for the same row.
    /// </summary>
    [Fact]
    public void Form16A_prints_the_deductees_own_pan_intact_even_when_it_starts_with_the_vendor_token()
    {
        var (c, deducteeId) = BuildTdsCompany("Bright Consultants", partyPan: "TALLY1234F");
        var cert = Form16A.Build(c, 2025, 1, deducteeId);
        string s = AsLatin1(Form16APdf.Render(cert, new PageConfig()));

        Assert.Contains("TALLY1234F", s, StringComparison.Ordinal);
        // The party name is CLEAN here, so the PAN is the only possible source of the token — exactly one.
        Assert.Equal(1, OccurrencesOfBrand(s));
        // And our own side of the certificate is untouched by the change.
        Assert.Contains("/Producer (Apex Solutions)", s);
        Assert.Contains("For Return Co", s);
    }

    /// <summary>Case-insensitive count of the forbidden vendor token in a rendered document.</summary>
    private static int OccurrencesOfBrand(string text)
    {
        int n = 0;
        for (int i = text.IndexOf("tally", StringComparison.OrdinalIgnoreCase); i >= 0;
             i = text.IndexOf("tally", i + 5, StringComparison.OrdinalIgnoreCase))
        {
            n++;
        }
        return n;
    }

    [Fact]
    public void Form16A_empty_certificate_still_renders_a_valid_pdf_er13()
    {
        var c = CompanyFactory.CreateSeeded("Return Co", FyStart);
        new TdsTcsService(c).EnableTds(new TdsConfig { Tan = ValidTan, DeductorType = DeductorType.Company });
        var some = AddLedger(c, "Nobody", "Sundry Creditors", false);
        var cert = Form16A.Build(c, 2025, 1, some.Id);
        Assert.True(cert.IsEmpty);

        var bytes = Form16APdf.Render(cert, new PageConfig());
        string s = AsLatin1(bytes);
        Assert.StartsWith("%PDF-", s);
        Assert.Contains("%%EOF", s);
        Assert.DoesNotContain("tally", s.ToLowerInvariant());
    }

    // ================================================================ Form 27D (TCS certificate)

    [Fact]
    public void Form27D_renders_a_valid_debranded_pdf_matching_the_27EQ_row()
    {
        var (c, collecteeId) = BuildTcsCompany();
        var cert = Form27D.Build(c, 2025, 1, collecteeId);
        var bytes = Form27DPdf.Render(cert, new PageConfig());
        string s = AsLatin1(bytes);

        Assert.StartsWith("%PDF-", s);
        Assert.Contains("%%EOF", s);
        Assert.DoesNotContain("tally", s.ToLowerInvariant());
        Assert.Contains("FORM 27D", s);

        Assert.Contains(ValidTan, s);
        Assert.Contains(BuyerPan, s);
        Assert.Contains("6CE", s);
        Assert.Contains("1,18,000.00", s);
        Assert.Contains("1,180.00", s);
        Assert.Contains("00123", s);
        Assert.Contains(IndianAmountInWords.Convert(1_180m), s);
    }

    [Fact]
    public void Form27D_is_byte_identical_across_two_runs()
    {
        var (c, collecteeId) = BuildTcsCompany();
        var cert = Form27D.Build(c, 2025, 1, collecteeId);
        var a = Form27DPdf.Render(cert, new PageConfig());
        var b = Form27DPdf.Render(cert, new PageConfig());
        Assert.Equal(a, b);
    }

    /// <summary>
    /// 🔴 <b>INVERTED BY RULING 18</b>, for the same reason as the Form 16A case above: the collectee is the
    /// customer the TCS certificate is issued to, and their legal name is not ours to rewrite. The collector
    /// block and the signatory line are ours and stay de-branded.
    /// </summary>
    [Fact]
    public void Form27D_keeps_a_collectees_own_name_intact_while_our_side_stays_debranded()
    {
        var (c, collecteeId) = BuildTcsCompany("Tally Scrap Buyers");
        var cert = Form27D.Build(c, 2025, 1, collecteeId);
        string s = AsLatin1(Form27DPdf.Render(cert, new PageConfig()));

        Assert.Contains("Tally Scrap Buyers", s);
        Assert.Equal(1, OccurrencesOfBrand(s));
        Assert.Contains("/Producer (Apex Solutions)", s);
    }

    // ================================================================ Form 27A (control chart)

    [Fact]
    public void Form27A_control_chart_renders_and_shows_the_control_totals()
    {
        var (c, _) = BuildTdsCompany();
        var q1 = Form26Q.Build(c, 2025, 1);
        var chart = Form27A.FromForm26Q(q1);
        var bytes = Form27APdf.Render(chart, new PageConfig());
        string s = AsLatin1(bytes);

        Assert.StartsWith("%PDF-", s);
        Assert.Contains("%%EOF", s);
        Assert.DoesNotContain("tally", s.ToLowerInvariant());
        Assert.Contains("FORM 27A", s);
        Assert.Contains("26Q", s);
        Assert.Contains(ValidTan, s);
        Assert.Contains("10,000.00", s);   // total tax
        Assert.Contains("1,00,000.00", s); // total amount

        // The chart tallies, so the PDF states so.
        Assert.True(chart.Tallies);
    }

    [Fact]
    public void Form27A_is_byte_identical_across_two_runs()
    {
        var (c, _) = BuildTdsCompany();
        var chart = Form27A.FromForm26Q(Form26Q.Build(c, 2025, 1));
        var a = Form27APdf.Render(chart, new PageConfig());
        var b = Form27APdf.Render(chart, new PageConfig());
        Assert.Equal(a, b);
    }
}
