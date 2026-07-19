using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Io;
using Apex.Ledger.Reports;
using Apex.Ledger.Services;
using Apex.Desktop.ViewModels;
using Xunit;
using Domain = Apex.Ledger.Domain;

namespace Apex.Desktop.Tests;

/// <summary>
/// CA slice S9 closeout — <b>the printed TDS/TCS certificate must say what the screen that produced it says.</b>
///
/// <para>S9 renumbered the <i>screen</i> vocabulary (menu row, column header, page heading) on the Income-tax Act
/// 1961 → 2025 gate but left <see cref="Form16APdf"/> and <see cref="Form27DPdf"/> holding <b>compile-time
/// constants</b> — <c>"FORM 16A"</c> and <c>"Certificate under section 203 of the Income-tax Act, 1961 …"</c> — with
/// no financial year in scope at all. The result was a live contradiction on the default path: because
/// <c>CompanyFactory</c> defaults a new company's financial year to <c>1 April of the current year</c>, a company
/// created today (FY 2026-27) showed "Form 131" on screen and printed a certificate <b>naming a repealed Act by name
/// and year</b>. A statute the deductee cites back to the department is not a cosmetic string.</para>
///
/// <para><b>Why these assertions read the real PDF bytes.</b> Asserting a view-model property would prove nothing —
/// the defect lived entirely below the view model, in a <c>const</c> the view model never touches. Each test therefore
/// drives the real <c>ExportPdf</c> handler and greps the rendered document. The PDF content stream is uncompressed
/// (no <c>FlateDecode</c> anywhere in <c>Apex.Ledger.Io</c>), so the drawn strings appear verbatim in the bytes.</para>
///
/// <para><b>Why both directions are asserted.</b> Each year asserts the expected vocabulary is <b>present</b> and the
/// other year's vocabulary is <b>absent</b>. A present-only assertion would still pass if the renderer emitted both
/// form numbers, which on a statutory certificate is worse than emitting the wrong one.</para>
///
/// <para><b>ER-13.</b> The FY 2025-26 cases pin the 1961-Act wording character-for-character. The gate keys off the
/// <i>certificate's own</i> financial year — never off "today" — so a prior-year certificate reprinted in any later
/// year still cites the law that governed the deduction, and reprints byte-identically.</para>
/// </summary>
public sealed class CertificatePrintVocabularyTests : IDisposable
{
    private const string ValidTan = "MUMA12345B";
    private const string DeducteePan = "AAPFU0939F";
    private const string BuyerPan = "AAQCS1234K";
    private const string GstinMaharashtra = "27AAPFU0939F1ZV";

    private readonly string _tempDir;

    public CertificatePrintVocabularyTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "ApexCertPrintVocabTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true); } catch { /* best effort */ }
    }

    /// <summary>The rendered document as text. Latin-1 because the PDF content stream is uncompressed single-byte.</summary>
    private static string AsLatin1(byte[] bytes) => Encoding.Latin1.GetString(bytes);

    // ---------------------------------------------------------------- fixtures (FY-parameterised)

    private static Domain.Ledger AddLedger(Company c, string name, string groupName, bool openingIsDebit)
    {
        var l = new Domain.Ledger(Guid.NewGuid(), name, c.FindGroupByName(groupName)!.Id, Money.Zero, openingIsDebit);
        c.AddLedger(l);
        return l;
    }

    /// <summary>A company with one §194J deduction and its challan, in the financial year starting
    /// <paramref name="fyStartYear"/> — the single input the whole vocabulary gate keys off.</summary>
    private static Company TdsCompany(int fyStartYear)
    {
        var fyStart = new DateOnly(fyStartYear, 4, 1);
        var c = CompanyFactory.CreateSeeded("Return Co", fyStart);
        new TdsTcsService(c).EnableTds(new TdsConfig
        {
            Tan = ValidTan, DeductorType = DeductorType.Company,
            ResponsiblePersonName = "A. Sharma", ResponsiblePersonPan = DeducteePan,
            ResponsiblePersonDesignation = "Director", ResponsiblePersonAddress = "12 MG Road",
        });
        var nop = c.FindNatureOfPaymentByCode("194J(b)")!;
        var vendor = AddLedger(c, "Consultant", "Sundry Creditors", false);
        vendor.TdsApplicable = true; vendor.TdsNatureOfPaymentId = nop.Id;
        vendor.DeducteeType = DeducteeType.Firm; vendor.PartyPan = DeducteePan;

        var on = new DateOnly(fyStartYear, 5, 10);
        var fees = AddLedger(c, "Professional Fees", "Indirect Expenses", true);
        var gross = Money.FromRupees(1_00_000m);
        var carve = new TdsService(c).BuildCarveOut(gross, gross, nop, vendor, on);
        new LedgerService(c).Post(new Voucher(Guid.NewGuid(),
            c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Journal).Id, on,
            new[] { new EntryLine(fees.Id, gross, DrCr.Debit), carve.PartyLine, carve.TdsPayableLine! }));

        var dep = new TdsDepositService(c);
        var statType = dep.EnsureStatPaymentType();
        var bank = AddLedger(c, "HDFC Bank", "Bank Accounts", true);
        var posted = new LedgerService(c).Post(
            dep.BuildStatPayment(Money.FromRupees(10_000m), bank, new DateOnly(fyStartYear, 6, 5), statType));
        dep.RecordChallan("00123", "0510308", new DateOnly(fyStartYear, 6, 5),
            Money.FromRupees(10_000m), "194J(b)", "200", posted);
        return c;
    }

    /// <summary>A company with one 6CE scrap collection and its challan, in the financial year starting
    /// <paramref name="fyStartYear"/>.</summary>
    private static Company TcsCompany(int fyStartYear)
    {
        var fyStart = new DateOnly(fyStartYear, 4, 1);
        var c = CompanyFactory.CreateSeeded("Collecting Co", fyStart);
        new TdsTcsService(c).EnableTcs(new TcsConfig
        {
            Tan = ValidTan, CollectorType = DeductorType.Company,
            ResponsiblePersonName = "A. Sharma", ResponsiblePersonPan = "AAPFU0939F",
            ResponsiblePersonDesignation = "Director", ResponsiblePersonAddress = "12 MG Road",
        });
        new GstService(c).EnableGst(new GstConfig
        {
            HomeStateCode = "27", Gstin = GstinMaharashtra, RegistrationType = GstRegistrationType.Regular,
            ApplicableFrom = fyStart, Periodicity = GstReturnPeriodicity.Monthly,
        });

        var inv = new InventoryService(c);
        var scrap = inv.CreateStockItem("Scrap Metal", inv.CreateStockGroup("Waste").Id,
            inv.CreateSimpleUnit("Kg", "Kilogram").Id);
        scrap.Gst = new StockItemGstDetails { Taxability = GstTaxability.Taxable, RateBasisPoints = 1800 };
        scrap.TcsNatureOfGoodsId = c.FindNatureOfGoodsByCode("6CE")!.Id;
        var main = c.MainLocation!.Id;

        var sales = AddLedger(c, "Scrap Sales", "Sales Accounts", false);
        var buyer = AddLedger(c, "Scrap Buyer", "Sundry Debtors", true);
        buyer.PartyGst = new PartyGstDetails
        { RegistrationType = GstRegistrationType.Regular, Gstin = GstinMaharashtra, StateCode = "27" };
        buyer.TcsApplicable = true; buyer.CollecteeType = CollecteeType.Individual; buyer.PartyPan = BuyerPan;

        var purchases = AddLedger(c, "Purchases", "Purchase Accounts", true);
        var creditor = AddLedger(c, "Creditor", "Sundry Creditors", false);
        new LedgerService(c).Post(new Voucher(Guid.NewGuid(),
            c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Purchase).Id, c.BooksBeginFrom,
            new[]
            {
                new EntryLine(purchases.Id, Money.FromRupees(5_00_000m), DrCr.Debit),
                new EntryLine(creditor.Id, Money.FromRupees(5_00_000m), DrCr.Credit),
            },
            inventoryLines: new[] { new VoucherInventoryLine(scrap.Id, main, 10_000m, Money.FromRupees(50m)) }));

        var on = new DateOnly(fyStartYear, 5, 10);
        var gst = new GstService(c);
        var value = Money.FromRupees(1_00_000m);
        var tax = gst.ComputeInvoiceTax(new[] { new GstService.TaxableLine(value, 1800) },
            gst.IsInterState(buyer.PartyGst!.StateCode), GstTaxDirection.Output);
        var nature = new TcsService(c).ResolveNature(scrap, sales)!;
        var col = new TcsService(c).BuildCollection(value, tax.TotalTax, nature, buyer, on);
        var lines = new List<EntryLine>
        {
            new(buyer.Id, value + tax.TotalTax + col.TcsAmount, DrCr.Debit),
            new(sales.Id, value, DrCr.Credit),
        };
        lines.AddRange(tax.TaxLines);
        lines.Add(col.TcsPayableLine!);
        new LedgerService(c).Post(new Voucher(Guid.NewGuid(),
            c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Sales).Id, on, lines,
            inventoryLines: new[] { new VoucherInventoryLine(scrap.Id, main, 1000m, Money.FromRupees(100m)) }));

        var dep = new TcsDepositService(c);
        var statType = dep.EnsureStatPaymentType();
        var bank = AddLedger(c, "HDFC Bank", "Bank Accounts", true);
        var posted = new LedgerService(c).Post(
            dep.BuildStatPayment(Money.FromRupees(1_180m), bank, new DateOnly(fyStartYear, 6, 5), statType));
        dep.RecordChallan("00123", "0510308", new DateOnly(fyStartYear, 6, 5),
            Money.FromRupees(1_180m), "6CE", "200", posted);
        return c;
    }

    /// <summary>Drives the real Form 16A page for FY <paramref name="fyStartYear"/> Q1 and exports through the real
    /// handler, returning the screen heading, the export file name and the <b>rendered PDF bytes</b>.</summary>
    private (string Title, string FileName, byte[] Pdf) PrintForm16A(int fyStartYear)
    {
        var vm = new Form16AViewModel(TdsCompany(fyStartYear));
        vm.SelectedYear = vm.FinancialYears.Single(y => y.StartYear == fyStartYear);
        vm.SelectedQuarter = vm.Quarters.Single(q => q.Quarter == 1);
        vm.ExportFolder = _tempDir;
        Assert.False(vm.IsEmpty);   // a blank certificate would make the vocabulary assertions vacuous

        byte[]? bytes = null;
        Assert.True(vm.ExportPdf((_, b) => bytes = b));
        Assert.NotNull(bytes);
        return (vm.Title, vm.ExportResolvedFileName, bytes!);
    }

    /// <summary>The Form 27D mirror of <see cref="PrintForm16A"/>.</summary>
    private (string Title, string FileName, byte[] Pdf) PrintForm27D(int fyStartYear)
    {
        var vm = new Form27DViewModel(TcsCompany(fyStartYear));
        vm.SelectedYear = vm.FinancialYears.Single(y => y.StartYear == fyStartYear);
        vm.SelectedQuarter = vm.Quarters.Single(q => q.Quarter == 1);
        vm.ExportFolder = _tempDir;
        Assert.False(vm.IsEmpty);

        byte[]? bytes = null;
        Assert.True(vm.ExportPdf((_, b) => bytes = b));
        Assert.NotNull(bytes);
        return (vm.Title, vm.ExportResolvedFileName, bytes!);
    }

    // ================================================================ Form 16A — TDS certificate

    /// <summary>ER-13: FY 2025-26 is governed by the 1961 Act, so the printed certificate is exactly what it always
    /// was — Form 16A, section 203, "Income-tax Act, 1961" — and carries no trace of the 2025-Act vocabulary.</summary>
    [Fact]
    public void Form16A_printed_for_fy2025_cites_the_1961_act_exactly_as_before()
    {
        var (title, fileName, pdf) = PrintForm16A(2025);
        var s = AsLatin1(pdf);

        Assert.Contains("FORM 16A", s);
        Assert.Contains("Certificate under section 203 of the Income-tax Act, 1961 for tax deducted at source", s);
        Assert.DoesNotContain("FORM 131", s);
        Assert.DoesNotContain("section 395", s);
        Assert.DoesNotContain("Income-tax Act, 2025", s);

        // The screen and the file name say the same thing the print does.
        Assert.Contains("Form 16A", title);
        Assert.DoesNotContain("Form 131", title);
        Assert.StartsWith("Form16A_2025_26_Q1_", fileName);
    }

    /// <summary>Finding A: FY 2026-27 is governed by the 2025 Act. The printed certificate must be Form 131 citing
    /// section 395 of the Income-tax Act, 2025 — and must <b>not</b> name the repealed Act.</summary>
    [Fact]
    public void Form16A_printed_for_fy2026_cites_the_2025_act_and_never_the_repealed_one()
    {
        var (title, fileName, pdf) = PrintForm16A(2026);
        var s = AsLatin1(pdf);

        Assert.Contains("FORM 131", s);
        Assert.Contains("Certificate under section 395 of the Income-tax Act, 2025 for tax deducted at source", s);
        Assert.DoesNotContain("FORM 16A", s);
        Assert.DoesNotContain("section 203", s);
        Assert.DoesNotContain("Income-tax Act, 1961", s);   // the repealed Act, named on a live document

        Assert.Contains("Form 131", title);
        Assert.DoesNotContain("Form 16A", title);
        Assert.StartsWith("Form131_2026_27_Q1_", fileName);
    }

    // ================================================================ Form 27D — TCS certificate

    /// <summary>ER-13 mirror for the TCS certificate.</summary>
    [Fact]
    public void Form27D_printed_for_fy2025_cites_the_1961_act_exactly_as_before()
    {
        var (title, fileName, pdf) = PrintForm27D(2025);
        var s = AsLatin1(pdf);

        Assert.Contains("FORM 27D", s);
        Assert.Contains("Certificate under section 206C of the Income-tax Act, 1961 for tax collected at source", s);
        Assert.DoesNotContain("FORM 133", s);
        Assert.DoesNotContain("section 394", s);
        Assert.DoesNotContain("Income-tax Act, 2025", s);

        Assert.Contains("Form 27D", title);
        Assert.DoesNotContain("Form 133", title);
        Assert.StartsWith("Form27D_2025_26_Q1_", fileName);
    }

    /// <summary>Finding A, TCS side. Note the section asymmetry: Form 27D cites §206C, the TCS <b>charging</b> section
    /// (not a certificate provision), which the 2025 Act renumbers to §394 — not to §395, which is where the TDS
    /// certificate provision §203 lands. Getting these two crossed would cite a real but wrong section.</summary>
    [Fact]
    public void Form27D_printed_for_fy2026_cites_section_394_not_the_tds_certificate_section()
    {
        var (title, fileName, pdf) = PrintForm27D(2026);
        var s = AsLatin1(pdf);

        Assert.Contains("FORM 133", s);
        Assert.Contains("Certificate under section 394 of the Income-tax Act, 2025 for tax collected at source", s);
        Assert.DoesNotContain("FORM 27D", s);
        Assert.DoesNotContain("section 206C", s);
        Assert.DoesNotContain("section 395", s);            // §395 is the TDS certificate section, not this one
        Assert.DoesNotContain("Income-tax Act, 1961", s);

        Assert.Contains("Form 133", title);
        Assert.StartsWith("Form133_2026_27_Q1_", fileName);
    }

    // ================================================================ screen == print, as one statement

    /// <summary>
    /// The agreement itself, stated once for every (certificate, year) pair: the form number the <b>screen</b> shows in
    /// its page heading is the form number the <b>printed</b> certificate carries in its title band. This is the
    /// invariant the slice broke — screen renumbered, print frozen — and it is asserted here without either side
    /// naming a literal, so it keeps holding if the vocabulary is ever extended.
    /// </summary>
    [Theory]
    [InlineData("16A", 2025)]
    [InlineData("16A", 2026)]
    [InlineData("27D", 2025)]
    [InlineData("27D", 2026)]
    public void The_printed_certificate_carries_the_same_form_number_the_screen_shows(string legacyForm, int fyStartYear)
    {
        var (title, fileName, pdf) = legacyForm == "16A" ? PrintForm16A(fyStartYear) : PrintForm27D(fyStartYear);

        var expected = StatuteVocabulary.FormLabel(legacyForm, fyStartYear);
        Assert.Contains($"Form {expected}", title);                       // the screen heading
        Assert.Contains($"FORM {expected}", AsLatin1(pdf));               // the printed title band
        Assert.StartsWith($"Form{expected}_", fileName);                  // the exported artifact's name

        // …and the vocabulary of the other era appears nowhere on the printed page.
        var other = StatuteVocabulary.FormLabel(legacyForm, fyStartYear == 2025 ? 2026 : 2025);
        Assert.NotEqual(expected, other);
        Assert.DoesNotContain($"FORM {other}", AsLatin1(pdf));
    }
}
