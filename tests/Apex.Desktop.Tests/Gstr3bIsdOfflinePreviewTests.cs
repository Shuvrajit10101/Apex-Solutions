using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Io;
using Apex.Ledger.Services;
using Apex.Desktop.Services;
using Apex.Desktop.ViewModels;
using DomainLedger = Apex.Ledger.Domain.Ledger;

namespace Apex.Desktop.Tests;

/// <summary>
/// 🔴 <b>THE OFFLINE-RETURNS SCREEN, END TO END — review findings R-M1 and R-M2.</b>
///
/// <para><b>R-M1</b> (the statutory-filing defect): the exported GSTR-3B JSON — the artefact actually <b>filed</b> —
/// emitted no 4(A)(4) key at all, so an ISD credit the book had availed and the GSTR-3B <i>screen</i> displayed was
/// simply absent from the file, understating the filed ITC by the whole of Table 4(A)(4). <b>R-M2</b>: the preview
/// on this same screen, which is what the operator reads before pressing export, listed 4(A)(2)/(3)/(5), 4(B),
/// 4(D)(1) and 6.1 and no 4(A)(4) — the same omission one layer up.
///
/// <para>These tests drive the <b>real route a user takes</b>: menu → screen → period → the projected figures the
/// operator reads → <c>BuildJson()</c>, the bytes written to disk. Every assertion is on an observable artefact,
/// never on an internal the code just set.</para>
///
/// <para><b>The hand-computed arithmetic.</b> The head office distributes to this registration CGST ₹1,500.00 +
/// SGST ₹1,500.00 + IGST ₹2,000.00 + Cess ₹500.00 = <b>₹5,500.00</b>. The shared fixture's own purchase already
/// gives 4(A)(5) CGST ₹450.00 / SGST ₹450.00, so the two rows carry <b>different</b> figures and a row that folded
/// one into the other would be visible.</para>
///
/// <para><b>Grounding (R7, ruling 14 tier 1), read BY CONTENT.</b>
/// <c>help.tallysolutions.com/tally-prime/india-gst/isd-credit/</c>, verbatim: <i>"The voucher will be included in
/// GSTR-3B and appear under 4A: Input Tax Credit Available (either in part or in full) &gt; Inward supplies from
/// ISD."</i></para>
/// </summary>
public sealed class Gstr3bIsdOfflinePreviewTests : IDisposable
{
    private const string GstinMaharashtra = "27AAPFU0939F1ZV";
    private const string GstinSupplier = "27AAACC1206D1ZM";

    private static readonly DateOnly FyStart = new(2024, 4, 1);
    private static readonly DateOnly PurchaseDate = new(2024, 4, 3);
    private static readonly DateOnly IsdDocDate = new(2024, 4, 9);
    private static readonly DateOnly IsdPostDate = new(2024, 4, 12);

    private const long Cgst = 150_000L;   // ₹1,500.00
    private const long Sgst = 150_000L;   // ₹1,500.00
    private const long Igst = 200_000L;   // ₹2,000.00
    private const long Cess = 50_000L;    // ₹  500.00

    private readonly string _tempDir;
    private readonly CompanyStorage _storage;

    public Gstr3bIsdOfflinePreviewTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "ApexGstr3bIsdPreviewTests_" + Guid.NewGuid().ToString("N"));
        _storage = new CompanyStorage(_tempDir);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, true); }
        catch (IOException) { /* a held handle must not fail the assertion under test */ }
    }

    private static DomainLedger Add(Company c, string name, string groupName, bool debit)
    {
        var ledger = new DomainLedger(
            Guid.NewGuid(), name, c.FindGroupByName(groupName)!.Id, Money.Zero, openingIsDebit: debit);
        c.AddLedger(ledger);
        return ledger;
    }

    /// <summary>A Regular GST company with the ₹5,000 purchase (4(A)(5) ITC 450 + 450) AND one ISD receipt.</summary>
    private MainWindowViewModel NewCompanyWithIsdCredit(string name)
    {
        var vm = new MainWindowViewModel(_storage);
        vm.NewCompanyName = name;
        vm.CreateCompany();
        var c = vm.Company!;
        c.FinancialYearStart = FyStart;
        c.BooksBeginFrom = FyStart;

        var gst = new GstService(c);
        gst.EnableGst(new GstConfig
        {
            HomeStateCode = "27",
            Gstin = GstinMaharashtra,
            RegistrationType = GstRegistrationType.Regular,
            ApplicableFrom = FyStart,
            Periodicity = GstReturnPeriodicity.Monthly,
        });

        var purchases = Add(c, "Purchases", "Purchase Accounts", true);
        var supplier = Add(c, "Local Supplier", "Sundry Creditors", false);
        supplier.PartyGst = new PartyGstDetails
        { RegistrationType = GstRegistrationType.Regular, Gstin = GstinSupplier, StateCode = "27" };

        var pTax = gst.ComputeInvoiceTax(
            new[] { new GstService.TaxableLine(Money.FromRupees(5000m), 1800) }, false, GstTaxDirection.Input);
        var pLines = new System.Collections.Generic.List<EntryLine>
        {
            new(purchases.Id, Money.FromRupees(5000m), DrCr.Debit),
            new(supplier.Id, Money.FromRupees(5900m), DrCr.Credit),
        };
        pLines.AddRange(pTax.TaxLines);
        var purchaseType = c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Purchase).Id;
        new LedgerService(c).Post(new Voucher(Guid.NewGuid(), purchaseType, PurchaseDate, pLines, partyId: supplier.Id));

        var headOffice = Add(c, "Head Office Account", "Indirect Expenses", true);
        new GstIsdCreditService(c).Post(
            "ISD/2024-25/001", IsdDocDate,
            new GstIsdCreditService.IsdCreditAmount(Cgst, Sgst, Igst, Cess),
            headOffice.Id, IsdPostDate);

        _storage.Save(c);
        vm.ShowGateway();
        return vm;
    }

    private static string Figure(GstOfflineReturnsViewModel page, string label) =>
        page.Figures.Single(f => f.Label == label).Value;

    private GstOfflineReturnsViewModel OpenAprilGstr3b(string companyName)
    {
        var vm = NewCompanyWithIsdCredit(companyName);
        vm.OpenGstOfflineReturns(GstOfflineReturnKind.Gstr3b);
        var page = vm.GstOfflineReturns!;
        page.SelectedPeriod = page.Periods.Single(p => p.Label == "April 2024");
        return page;
    }

    // ============================================================================== R-M2, the preview

    /// <summary>
    /// 🔴 <b>R-M2.</b> The operator reads the 4(A)(4) credit on the preview, per head, before exporting. Reverting
    /// the four <c>Add("4(A)(4) …")</c> calls makes <c>Figures.Single</c> throw here — the label is not in the list.
    /// </summary>
    [Fact]
    public void The_offline_gstr3b_preview_shows_table_4a4_isd_credit_per_head()
    {
        var page = OpenAprilGstr3b("Isd Preview Co");

        Assert.Equal("1,500.00", Figure(page, "4(A)(4) ITC from ISD CGST"));
        Assert.Equal("1,500.00", Figure(page, "4(A)(4) ITC from ISD SGST"));
        Assert.Equal("2,000.00", Figure(page, "4(A)(4) ITC from ISD IGST"));
        Assert.Equal("500.00", Figure(page, "4(A)(4) ITC from ISD Cess"));
    }

    /// <summary>
    /// The 4(A)(4) row and the 4(A)(5) row carry <b>different</b> figures on the same preview: ₹1,500.00 of ISD
    /// credit beside ₹450.00 of all-other ITC. A preview that folded one into the other would show 1,950.00 on
    /// 4(A)(5), and this is what catches it.
    /// </summary>
    [Fact]
    public void The_preview_keeps_4a4_and_4a5_apart_on_the_same_period()
    {
        var page = OpenAprilGstr3b("Isd Preview Split Co");

        Assert.Equal("1,500.00", Figure(page, "4(A)(4) ITC from ISD CGST"));
        Assert.Equal("450.00", Figure(page, "4(A)(5) ITC CGST"));
        Assert.Equal("450.00", Figure(page, "4(A)(5) ITC SGST"));
    }

    // ============================================================================== R-M1, the filed bytes

    /// <summary>
    /// 🔴 <b>R-M1, THROUGH THE REAL SCREEN.</b> The bytes this screen writes carry the four 4(A)(4) heads at their
    /// exact paisa. This is the artefact that gets filed; before the fix it had no 4(A)(4) key at all and the
    /// return understated ITC by ₹5,500.00 while the screen beside it showed the credit.
    /// </summary>
    [Fact]
    public void The_json_this_screen_writes_carries_table_4a4_at_its_exact_paisa()
    {
        var page = OpenAprilGstr3b("Isd Filed Json Co");

        var bytes = page.BuildJson();
        using var doc = JsonDocument.Parse(bytes);
        var root = doc.RootElement;

        Assert.Equal(150_000L, root.GetProperty("tbl4a4_isd_camt_paisa").GetInt64());
        Assert.Equal(150_000L, root.GetProperty("tbl4a4_isd_samt_paisa").GetInt64());
        Assert.Equal(200_000L, root.GetProperty("tbl4a4_isd_iamt_paisa").GetInt64());
        Assert.Equal(50_000L, root.GetProperty("tbl4a4_isd_csamt_paisa").GetInt64());

        // 4(A)(5) is the fixture's own purchase and must not have absorbed the ISD credit.
        Assert.Equal(45_000L, root.GetProperty("tbl4a5_camt_paisa").GetInt64());

        // And the key is in the byte stream as text, not merely defaulted by the parser.
        Assert.Contains("\"tbl4a4_isd_camt_paisa\"", Encoding.UTF8.GetString(bytes), StringComparison.Ordinal);
    }

    /// <summary>
    /// 🔴 <b>THE SCREEN AND THE FILE AGREE.</b> The defect was not an omission in isolation — it was a file that
    /// disagreed with the screen above it. Every head the preview displays is the head the exported bytes carry.
    /// </summary>
    [Fact]
    public void Every_4a4_head_on_the_preview_equals_the_head_in_the_exported_bytes()
    {
        var page = OpenAprilGstr3b("Isd Screen Vs File Co");

        using var doc = JsonDocument.Parse(page.BuildJson());
        var root = doc.RootElement;

        // MoneyCodec.FromPaisa is the ONE paisa→rupees rule (drift lock D3) and IndianFormat.AmountAlways is the
        // one money formatter — this helper rolls neither of them by hand.
        static string Rupees(long paisa) =>
            IndianFormat.AmountAlways(MoneyCodec.FromPaisa(paisa));

        Assert.Equal(
            Figure(page, "4(A)(4) ITC from ISD CGST"),
            Rupees(root.GetProperty("tbl4a4_isd_camt_paisa").GetInt64()));
        Assert.Equal(
            Figure(page, "4(A)(4) ITC from ISD SGST"),
            Rupees(root.GetProperty("tbl4a4_isd_samt_paisa").GetInt64()));
        Assert.Equal(
            Figure(page, "4(A)(4) ITC from ISD IGST"),
            Rupees(root.GetProperty("tbl4a4_isd_iamt_paisa").GetInt64()));
        Assert.Equal(
            Figure(page, "4(A)(4) ITC from ISD Cess"),
            Rupees(root.GetProperty("tbl4a4_isd_csamt_paisa").GetInt64()));
    }
}
