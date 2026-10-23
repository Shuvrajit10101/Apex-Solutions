using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Io;
using Apex.Ledger.Services;
using Apex.Desktop.Services;
using Apex.Desktop.ViewModels;
using Xunit;
using DomainLedger = Apex.Ledger.Domain.Ledger;

namespace Apex.Desktop.Tests;

/// <summary>
/// 🔴 <b>BJ-1 — A FILED ANNUAL RETURN PRINTED AND EXPORTED WITH ITS MONEY DELETED.</b>
///
/// <para><b>The defect.</b> <c>PrintReport</c> and <c>TabularExport</c> carry exactly ONE column band. Every
/// writer loops to <c>Columns.Count</c> (<c>DelimitedText.RowFields</c>, <c>XlsxWriter</c>,
/// <c>HtmlReportWriter</c>, <c>XmlReportWriter</c>, <c>JsonReportWriter</c>) or clamps with
/// <c>Math.Min(Columns.Count, row.Cells.Count)</c> (<c>ReportPdf</c>). Both egress projectors built that band
/// from the matrix's FIRST section alone and then appended the SECOND section's rows underneath it. Harmless
/// while section 2 is the narrower of the two — which was true of the only report that had one when the feature
/// was written (PF Form 6A: 9 columns on page 1, 8 on page 2) — and a silent amputation the moment it is wider.
/// GSTR-9 Part VI Table 17 declares SIX columns under a TWO-column band 1, so <c>Quantity</c>,
/// <c>Taxable Value</c> and <c>Total Tax</c> were cut off every printed page, every CSV, every XLSX, every HTML,
/// XML and JSON export, every emailed PDF and every Alt+M share — and the statutory foot line went out as the
/// bare string <c>"17  Total"</c> with nothing beside it.</para>
///
/// <para>🔴 <b>WHY A 4922-TEST SUITE COULD NOT SEE IT, AND WHY THESE ASSERTIONS ARE SHAPED AS THEY ARE.</b> The
/// ON-SCREEN grid was always correct: the view renders the two bands as two independent grids, each against its
/// own columns. Every assertion that read <c>PayrollColumns2</c> / <c>PayrollRows2</c> off the view model
/// therefore passed on the broken build, and one that compared the exported captions to <c>PayrollColumns</c>
/// actively PINNED the defect. So every assertion here reads the EMITTED ARTEFACT — the parsed CSV records and
/// the rendered PDF bytes — and never an internal the test itself could have set.</para>
///
/// <para>🔴 <b>IT IS THE SHAPE, NOT THE INSTANCE, AND THE COUNT IS THREE — NOT TWO.</b> Grepping every
/// <c>PayrollColumns2</c> declaration site in <c>ReportsViewModel</c> and comparing each against its own
/// builder's band 1 gives exactly three kinds whose second band is WIDER than their first, all three losing
/// money or figures on egress:
/// <list type="bullet">
///   <item><see cref="ReportKind.Gstr9AnnualReturn"/> — 2 then 6: Quantity, Taxable Value, Total Tax.</item>
///   <item><see cref="ReportKind.IncomeTaxComputation"/> — 2 then 3: the Component-details Amount.</item>
///   <item><see cref="ReportKind.PayrollStatutorySummary"/> — 4 then 6: the detail lines' Paid and Balance.</item>
/// </list>
/// The last two were ALREADY SHIPPED ON MAIN and predate the GSTR-9 re-home. The fix is one merged band in
/// <see cref="ReportMatrixEgressBand"/> read by both projectors, so all three close at once and the fourth
/// cannot open. <see cref="ReportKind.PfForm6A"/> is carried below as the control: its second band is NARROWER,
/// so its egress band must be byte-for-byte what it always was.</para>
/// </summary>
public sealed class MatrixSecondBandEgressTests : IDisposable
{
    private const string GstinMaharashtra = "27AAPFU0939F1ZV";
    private const string Sac = "998313";
    private static readonly DateOnly FyStart = new(2024, 4, 1);
    private static readonly DateOnly SaleDate = new(2024, 4, 5);

    private readonly string _tempDir;
    private readonly CompanyStorage _storage;

    public MatrixSecondBandEgressTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "ApexBandEgress_" + Guid.NewGuid().ToString("N"));
        _storage = new CompanyStorage(_tempDir);
    }

    public void Dispose()
    {
        try
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    // ============================================================ the emitted spreadsheet

    /// <summary>
    /// 🔴 <b>THE CENTRAL ASSERTION OF THIS FIX.</b> A Regular GST company with one intra-state B2B sale of
    /// ₹1,000 @ 18% on a Sales ledger carrying SAC 998313. The exported CSV's Table 17 body record must carry
    /// the SAC <b>and</b> ₹1,000.00 taxable value <b>and</b> ₹180.00 of tax, and the statutory foot must carry
    /// the same two figures. On the broken build both records were two fields long — <c>998313,Sales</c> and
    /// <c>17  Total,</c> — which is a filed annual return with its money removed.
    /// </summary>
    [Fact]
    public void The_exported_annual_return_carries_table_17s_taxable_value_and_tax_not_just_its_hsn_code()
    {
        var reports = Gstr9OnAnHsnBearingBook("Band Csv");
        var records = ParseCsv(CsvWriter.Write(ReportTabularProjector.Project(reports)));

        var body = Assert.Single(records.Where(r => r.Count > 0 && r[0] == Sac));
        Assert.True(body.Count >= 6,
            $"Table 17's body record came out {body.Count} field(s) wide: [{string.Join(" | ", body)}]. "
          + "Quantity, Taxable Value and Total Tax were truncated off the export.");
        Assert.Equal(1000m, Decimal.Parse(body[4], CultureInfo.InvariantCulture));
        Assert.Equal(180m, Decimal.Parse(body[5], CultureInfo.InvariantCulture));

        var foot = Assert.Single(records.Where(r => r.Count > 0 && r[0] == "17  Total"));
        Assert.True(foot.Count >= 6,
            $"Table 17's statutory foot came out {foot.Count} field(s) wide: [{string.Join(" | ", foot)}].");
        Assert.Equal(1000m, Decimal.Parse(foot[4], CultureInfo.InvariantCulture));
        Assert.Equal(180m, Decimal.Parse(foot[5], CultureInfo.InvariantCulture));

        // The band-2 heading record is emitted too, so a reader of the sheet knows which caption each figure is
        // under — the merged top band repeats band 1's captions for the first two positions by design.
        Assert.Contains(records, r => r.Count >= 6 && r[4] == "Taxable Value" && r[5] == "Total Tax");
    }

    /// <summary>
    /// The printed page, read back out of the rendered PDF bytes. Captions AND figures, because a caption with
    /// no figure under it is the very document this fix exists to stop: Table 17 printed its HSN codes and then
    /// a line reading "17  Total" with blank money beside it.
    /// </summary>
    [Fact]
    public void The_printed_annual_return_puts_table_17s_quantity_value_and_tax_on_the_page()
    {
        var reports = Gstr9OnAnHsnBearingBook("Band Pdf");
        string page = Encoding.Latin1.GetString(
            ReportPdf.Render(ReportPrintProjector.Project(reports), new PageConfig()));

        foreach (var caption in new[] { "Quantity", "Taxable Value", "Total Tax" })
            Assert.True(page.Contains(caption, StringComparison.Ordinal),
                $"the printed annual return carries no \"{caption}\" column at all.");

        Assert.Contains(Sac, page, StringComparison.Ordinal);
        // 1,000.00 and 180.00 are Table 17's own figures and appear nowhere else in Parts II-VI of this fixture
        // under those exact groupings, so their presence is Table 17's money reaching the paper.
        Assert.Contains("1,000.00", page, StringComparison.Ordinal);
        Assert.Contains("180.00", page, StringComparison.Ordinal);
    }

    // ============================================================ the shape

    /// <summary>
    /// 🔴 <b>THE INVARIANT, STATED OVER THE MECHANISM RATHER THAN OVER ONE REPORT.</b> For ANY matrix report
    /// with a populated second section, the egress band both projectors read must be wide enough for the WIDEST
    /// row either section can produce — otherwise a writer that loops to <c>Columns.Count</c> deletes cells. And
    /// band 1's captions must still LEAD it unchanged, so band 1's own rows never slide under someone else's
    /// heading (the "believable heading over the wrong figures" failure, which is worse than a blank one).
    /// </summary>
    [Fact]
    public void The_egress_band_is_never_narrower_than_the_widest_row_either_section_emits()
    {
        var reports = Gstr9OnAnHsnBearingBook("Band Shape");
        AssertBandCoversBothSections(reports);
    }

    /// <summary>
    /// 🔴 <b>NO BLANK CAPTIONS — census 12.1(b), re-opened by the obvious fix and closed by this one.</b> The
    /// naive widening is to pad band 1 out with <c>new PrintColumn(string.Empty, …)</c>, which prints a
    /// statutory form with unlabelled columns: the exact defect this project has already shipped once. Every
    /// surplus position is captioned from band 2 instead, because band 2's rows are the only rows that can ever
    /// occupy it.
    /// </summary>
    [Fact]
    public void Widening_the_egress_band_introduces_no_blank_caption_on_either_path()
    {
        var reports = Gstr9OnAnHsnBearingBook("Band Captions");

        Assert.All(ReportTabularProjector.Project(reports).Columns,
            c => Assert.False(string.IsNullOrWhiteSpace(c.Header), "a BLANK exported column caption."));
        Assert.All(ReportPrintProjector.Project(reports).Columns,
            c => Assert.False(string.IsNullOrWhiteSpace(c.Header), "a BLANK printed column caption."));

        // The surplus positions are band 2's own captions, verbatim and in order.
        var band = ReportMatrixEgressBand.For(reports).Select(c => c.Header).ToList();
        Assert.Equal(new[] { "Particulars", "Amount", "UQC", "Quantity", "Taxable Value", "Total Tax" }, band);
    }

    /// <summary>
    /// 🔴 <b>THE CONTROL.</b> PF Form 6A's second band (8) is NARROWER than its first (9), which is the case the
    /// one-band egress model always handled correctly. Its band must come back REFERENCE-IDENTICAL to band 1 —
    /// not merely equal — so this fix provably cannot have moved a column on the only report that had a second
    /// section before it.
    /// </summary>
    [Fact]
    public void A_narrower_second_section_leaves_the_egress_band_exactly_as_it_was()
    {
        var reports = new ReportsViewModel(GstBook("Band Control"), ReportKind.PfForm6A);

        Assert.Same(reports.PayrollColumns, ReportMatrixEgressBand.For(reports));
        Assert.Equal(
            reports.PayrollColumns.Select(c => c.Header).ToList(),
            ReportTabularProjector.Project(reports).Columns.Select(c => c.Header).ToList());
    }

    // ============================================================ scaffolding

    /// <summary>The invariant, reusable from any fixture that can build a matrix report with a second band.</summary>
    internal static void AssertBandCoversBothSections(ReportsViewModel reports)
    {
        Assert.True(reports.HasPayrollSection2, "this report has no second section, so it proves nothing here.");
        Assert.NotEmpty(reports.PayrollRows2);

        int widest = Math.Max(
            reports.PayrollRows.Count == 0 ? 0 : reports.PayrollRows.Max(r => r.Cells.Count),
            reports.PayrollRows2.Max(r => r.Cells.Count));

        var exported = ReportTabularProjector.Project(reports);
        var printed = ReportPrintProjector.Project(reports);

        Assert.True(exported.Columns.Count >= widest,
            $"{reports.Kind}: the export band is {exported.Columns.Count} wide but a row emits {widest} cells — "
          + "every writer loops to Columns.Count, so the surplus cells are deleted from the file.");
        Assert.True(printed.Columns.Count >= widest,
            $"{reports.Kind}: the print band is {printed.Columns.Count} wide but a row emits {widest} cells — "
          + "ReportPdf clamps with Math.Min, so the surplus cells never reach the page.");

        Assert.Equal(
            reports.PayrollColumns.Select(c => c.Header).ToList(),
            exported.Columns.Take(reports.PayrollColumns.Count).Select(c => c.Header).ToList());
    }

    /// <summary>GSTR-9 built over a book whose Sales ledger carries an SAC, so Part VI Table 17 has a real body
    /// row with real money rather than a lone "17  Total" over two zeroes.</summary>
    private ReportsViewModel Gstr9OnAnHsnBearingBook(string name)
    {
        var reports = new ReportsViewModel(GstBook(name), ReportKind.Gstr9AnnualReturn);
        Assert.NotEmpty(reports.PayrollRows);
        Assert.True(reports.PayrollRows2.Count >= 2,
            "the fixture produced no Table 17 BODY row, so this test would pass over an empty band — which is "
          + "exactly how BJ-1 stayed invisible.");
        return reports;
    }

    private Company GstBook(string name)
    {
        var vm = new MainWindowViewModel(_storage);
        vm.NewCompanyName = name + " " + Guid.NewGuid().ToString("N")[..6];
        vm.CreateCompany();

        var c = vm.Company!;
        c.FinancialYearStart = FyStart;
        c.BooksBeginFrom = FyStart;

        new GstService(c).EnableGst(new GstConfig
        {
            HomeStateCode = "27",
            Gstin = GstinMaharashtra,
            RegistrationType = GstRegistrationType.Regular,
            ApplicableFrom = FyStart,
            Periodicity = GstReturnPeriodicity.Monthly,
        });

        var gst = new GstService(c);
        var ledgers = new LedgerService(c);

        var sales = Add(c, "Sales", "Sales Accounts", false);
        sales.SalesPurchaseGst = new StockItemGstDetails
        {
            HsnSac = Sac, Taxability = GstTaxability.Taxable, RateBasisPoints = 1800,
            SupplyType = GstSupplyType.Services,
        };
        var debtor = Add(c, "Local Debtor", "Sundry Debtors", true);
        debtor.PartyGst = new PartyGstDetails
        { RegistrationType = GstRegistrationType.Regular, Gstin = GstinMaharashtra, StateCode = "27" };

        var salesType = c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Sales).Id;
        var tax = gst.ComputeInvoiceTax(
            new[] { new GstService.TaxableLine(Money.FromRupees(1000m), 1800) }, false, GstTaxDirection.Output);
        var lines = new List<EntryLine>
        {
            new(debtor.Id, Money.FromRupees(1180m), DrCr.Debit),
            new(sales.Id, Money.FromRupees(1000m), DrCr.Credit),
        };
        lines.AddRange(tax.TaxLines);
        ledgers.Post(new Voucher(Guid.NewGuid(), salesType, SaleDate, lines, number: 1, partyId: debtor.Id));

        _storage.Save(c);
        return c;
    }

    private static DomainLedger Add(Company c, string name, string groupName, bool debit)
    {
        var group = c.FindGroupByName(groupName) ?? throw new InvalidOperationException($"No group '{groupName}'.");
        var ledger = new DomainLedger(Guid.NewGuid(), name, group.Id, Money.Zero, openingIsDebit: debit);
        c.AddLedger(ledger);
        return ledger;
    }

    /// <summary>RFC-4180 reader — the emitted file is parsed, never the model that produced it.</summary>
    private static List<List<string>> ParseCsv(byte[] bytes)
    {
        int start = (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF) ? 3 : 0;
        string text = Encoding.UTF8.GetString(bytes, start, bytes.Length - start);

        var records = new List<List<string>>();
        var record = new List<string>();
        var field = new StringBuilder();
        bool inQuotes = false;

        for (int i = 0; i < text.Length; i++)
        {
            char ch = text[i];
            if (inQuotes)
            {
                if (ch == '"')
                {
                    if (i + 1 < text.Length && text[i + 1] == '"') { field.Append('"'); i++; }
                    else inQuotes = false;
                }
                else field.Append(ch);
            }
            else if (ch == '"') inQuotes = true;
            else if (ch == ',') { record.Add(field.ToString()); field.Clear(); }
            else if (ch == '\r') { /* swallowed; the \n ends the record */ }
            else if (ch == '\n')
            {
                record.Add(field.ToString()); field.Clear();
                records.Add(record); record = new List<string>();
            }
            else field.Append(ch);
        }
        if (field.Length > 0 || record.Count > 0) { record.Add(field.ToString()); records.Add(record); }
        return records;
    }
}
