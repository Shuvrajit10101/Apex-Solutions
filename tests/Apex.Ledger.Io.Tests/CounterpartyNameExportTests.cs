using System;
using System.IO;
using System.IO.Compression;
using System.Text;
using Apex.Ledger;
using Apex.Ledger.Io;

namespace Apex.Ledger.Io.Tests;

/// <summary>
/// 🔴 <b>RULING 18 — the de-brander keeps rewriting OUR strings and stops rewriting a COUNTERPARTY'S name,
/// product-wide.</b>
///
/// <para><b>The defect this file locks out.</b> <see cref="Debrand.Text"/> strips a case-insensitive vendor token
/// from a string. That is right for our own document furniture and catastrophic for a customer's, supplier's or
/// bank's legal name: the shared report egress ran it over EVERY CELL of EVERY printed report and of the CSV /
/// ASCII / XLSX / HTML / XML / JSON exports, so a real party whose name carries that token was silently renamed
/// on the statement posted to them, in the ledger the auditor reads, and inside the spreadsheet the customer
/// opens. The whole existing suite missed it because every fixture in it uses CLEAN party names — so every
/// fixture here deliberately does not.</para>
///
/// <para><b>Why the API needed a seam and where it is.</b> A <see cref="string"/> carries no provenance, so a
/// renderer handed a cell cannot tell our text from theirs. For most of the model it does not have to, because
/// provenance is structural: a <see cref="PrintColumn.Header"/>/<see cref="TabularColumn.Header"/> caption and a
/// report's own subtitle are compile-time strings this product authored and stay guarded, and the overwhelming
/// majority of <see cref="PrintRow"/> / <see cref="TabularRow"/> cells are book data projected from masters and
/// vouchers and ship verbatim. The TITLE is the one string that is genuinely either — <c>"Trial Balance"</c> is
/// ours, <c>"Ledger Account - " + ledger.Name</c> is half theirs — so the PRODUCER declares it, through
/// <see cref="PrintReport.TitleCarriesMasterName"/> for the PDF and
/// <see cref="TabularExport.TitleCarriesMasterName"/> for HTML / XML / JSON / XLSX, resolved once in
/// <see cref="TabularExport.TitleText"/> so the five formats cannot disagree about one heading.</para>
///
/// <para>🔴 <b>A BODY CELL IS NOT AUTOMATICALLY THEIRS.</b> An earlier version of this note said cells are
/// "ALWAYS book data", and that claim was false and load-bearing: a handful of producers write OUR OWN prose
/// into a body cell — most importantly the <c>"For &lt;our company&gt;"</c> signatory line of the Reminder Letter
/// and the Confirmation of Accounts — and when the renderer's body-cell scrub was removed, our own branding
/// started printing on two letters posted to counterparties. There is no per-cell flag; a producer that writes
/// its own text into a cell de-brands it AT SOURCE, and <c>MultiAccountPartyNameOnPaperTests</c> pins both
/// letters in both directions.</para>
/// </summary>
public sealed class CounterpartyNameExportTests
{
    // A supplier whose LEGAL NAME contains the vendor token. Nothing about it is ours.
    private const string Counterparty = "Tally Traders Pvt Ltd";

    private static string AsLatin1(byte[] bytes) => Encoding.Latin1.GetString(bytes);
    // CsvWriter emits a UTF-8 BOM (Excel needs it); the other writers do not. Trimming it here lets one helper
    // read every format's bytes back as text.
    private static string Utf8(byte[] bytes) => new UTF8Encoding(false).GetString(bytes).TrimStart('﻿');

    /// <summary>Case-insensitive count of the vendor token, so a test can say "it survives HERE and nowhere else".</summary>
    private static int CountBrand(string text)
    {
        int n = 0;
        for (int i = text.IndexOf("tally", StringComparison.OrdinalIgnoreCase); i >= 0;
             i = text.IndexOf("tally", i + 5, StringComparison.OrdinalIgnoreCase))
        {
            n++;
        }
        return n;
    }

    // A report whose HEADING and CAPTION are ours and carry the brand (they must be scrubbed) and whose BODY
    // holds a counterparty's name (it must survive). One fixture proves both halves of the ruling at once.
    private static PrintReport BrandedChromeCleanIntent() => new()
    {
        Title = "Tally Ledger Account",
        Subtitle = "Bright Traders  -  as at 31-03-2025",
        Columns = new[]
        {
            new PrintColumn("Tally Particulars", 3.0, CellAlign.Left),
            new PrintColumn("Debit", 1.5, CellAlign.Right),
        },
        Rows = new[] { new PrintRow(Counterparty, "1,05,000.00") },
    };

    private static TabularExport TabularChromeAndParty() => new(
        title: "Tally Trial Balance",
        columns: new[]
        {
            new TabularColumn("Tally Particulars", CellType.Text),
            new TabularColumn("Debit", CellType.Number),
        },
        rows: new[]
        {
            TabularRow.Of(TabularCell.Text(Counterparty), TabularCell.Number(105000.00m)),
        });

    // ================================================================ the printed report (ReportPdf)

    /// <summary>
    /// The printed page: the party's name survives in the particulars column, and it is the ONLY surviving
    /// occurrence — the product-authored heading and the column caption are still scrubbed in the same document.
    /// </summary>
    [Fact]
    public void A_printed_report_keeps_a_party_name_and_still_debrands_its_own_heading_and_caption()
    {
        string s = AsLatin1(ReportPdf.Render(BrandedChromeCleanIntent(), new PageConfig()));

        Assert.Contains(Counterparty, s, StringComparison.Ordinal);
        Assert.Equal(1, CountBrand(s));

        // De-branding is not blanking: our own strings keep everything but the token.
        Assert.Contains("Ledger Account", s, StringComparison.Ordinal);
        Assert.Contains("Particulars", s, StringComparison.Ordinal);
    }

    /// <summary>
    /// The multi-document job path (census 12.6) shares the same drawing code, so it is asserted separately —
    /// fixing only one of the two overloads is exactly how the original defect survived its own review.
    /// </summary>
    [Fact]
    public void A_multi_document_job_keeps_a_party_name_in_every_member()
    {
        var docs = new[] { BrandedChromeCleanIntent(), BrandedChromeCleanIntent() };

        string s = AsLatin1(ReportPdf.Render(docs, new PageConfig()));

        Assert.Equal(2, CountBrand(s));   // once per sheet, and nowhere else
    }

    /// <summary>
    /// 🔴 <b>The OTHER half of Ruling 18, and a live hole this pass found.</b> <c>PageConfig.HeaderText</c> is the
    /// running page header — OURS. <c>ReportPdf</c> drew it with no scrub at all, while scrubbing the subtitle on
    /// the same page. It is reachable today: <c>Form16ViewModel</c> renders the salary-TDS certificate through
    /// <c>ReportPdf.Render(…, CertificatePages.Build(company.Name))</c>, and <c>CertificatePages</c> puts the
    /// company name straight into <c>HeaderText</c> under a doc-comment claiming it is "already de-branded by the
    /// writers" — a guarantee nothing enforced.
    /// </summary>
    [Fact]
    public void The_running_page_header_is_ours_and_is_debranded()
    {
        var report = new PrintReport
        {
            Title = "Salary-TDS Certificate",
            Columns = new[] { new PrintColumn("Particular", 3.0, CellAlign.Left) },
            Rows = new[] { new PrintRow("Gross salary") },
        };
        var page = new PageConfig { HeaderText = "Tally Solutions Retail" };

        string s = AsLatin1(ReportPdf.Render(report, page));

        Assert.Equal(0, CountBrand(s));
        Assert.Contains("Solutions Retail", s, StringComparison.Ordinal);   // scrubbed, not blanked
    }

    // ================================================================ the title provenance seam

    /// <summary>
    /// The seam itself. The SAME heading text is scrubbed when the producer says the title is ours and printed
    /// verbatim when the producer says it carries a master name — which is the only thing that distinguishes
    /// "Ledger Account - Tally Traders Pvt Ltd" from an F12 title override somebody typed into our document.
    /// </summary>
    [Fact]
    public void A_title_marked_as_carrying_a_master_name_is_printed_verbatim_and_an_unmarked_one_is_scrubbed()
    {
        var master = new PrintReport
        {
            Title = "Ledger Account - " + Counterparty,
            TitleCarriesMasterName = true,
            Columns = new[] { new PrintColumn("Particulars", 3.0, CellAlign.Left) },
            Rows = new[] { new PrintRow("Opening") },
        };
        var ours = new PrintReport
        {
            Title = "Ledger Account - " + Counterparty,
            Columns = new[] { new PrintColumn("Particulars", 3.0, CellAlign.Left) },
            Rows = new[] { new PrintRow("Opening") },
        };

        string kept = AsLatin1(ReportPdf.Render(master, new PageConfig()));
        string scrubbed = AsLatin1(ReportPdf.Render(ours, new PageConfig()));

        Assert.Contains("Ledger Account - " + Counterparty, kept, StringComparison.Ordinal);
        Assert.DoesNotContain(Counterparty, scrubbed, StringComparison.Ordinal);
        Assert.Contains("Traders Pvt Ltd", scrubbed, StringComparison.Ordinal);   // scrubbed, not blanked
    }

    /// <summary>The flag defaults to false, so every producer that predates the seam keeps the ER-11 guard.</summary>
    [Fact]
    public void The_master_name_flag_defaults_to_false()
        => Assert.False(new PrintReport().TitleCarriesMasterName);

    // ================================================================ the tabular exports

    [Fact]
    public void Csv_keeps_a_party_name_and_still_debrands_the_header_row()
    {
        string text = Utf8(CsvWriter.Write(TabularChromeAndParty()));

        Assert.Contains(Counterparty, text, StringComparison.Ordinal);
        Assert.Equal(1, CountBrand(text));
        Assert.Contains("Particulars", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Ascii_delimited_keeps_a_party_name_and_still_debrands_the_header_row()
    {
        string text = Utf8(AsciiReportWriter.Write(TabularChromeAndParty()));

        Assert.Contains(Counterparty, text, StringComparison.Ordinal);
        Assert.Equal(1, CountBrand(text));
    }

    [Fact]
    public void Xlsx_keeps_a_party_name_in_the_sheet_and_still_debrands_the_header_row()
    {
        using var zip = new ZipArchive(new MemoryStream(XlsxWriter.Write(TabularChromeAndParty())), ZipArchiveMode.Read);
        using var reader = new StreamReader(zip.GetEntry("xl/worksheets/sheet1.xml")!.Open(), Encoding.UTF8);
        string sheet = reader.ReadToEnd();

        Assert.Contains(Counterparty, sheet, StringComparison.Ordinal);
        Assert.Equal(1, CountBrand(sheet));
        Assert.Contains("Particulars", sheet, StringComparison.Ordinal);
    }

    [Fact]
    public void Html_keeps_a_party_name_and_still_debrands_the_title_and_header_cells()
    {
        string text = Utf8(HtmlReportWriter.Write(TabularChromeAndParty()));

        Assert.Contains(Counterparty, text, StringComparison.Ordinal);
        Assert.Equal(1, CountBrand(text));
        Assert.Contains("Trial Balance", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Xml_keeps_a_party_name_and_still_debrands_the_title_and_column_attributes()
    {
        string text = Utf8(XmlReportWriter.Write(TabularChromeAndParty()));

        Assert.Contains(Counterparty, text, StringComparison.Ordinal);
        Assert.Equal(1, CountBrand(text));
        Assert.Contains("column=\"Particulars\"", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Json_keeps_a_party_name_and_still_debrands_the_title_and_headers()
    {
        string text = Utf8(JsonReportWriter.Write(TabularChromeAndParty()));

        Assert.Contains(Counterparty, text, StringComparison.Ordinal);
        Assert.Equal(1, CountBrand(text));
        Assert.Contains("\"header\": \"Particulars\"", text, StringComparison.Ordinal);
    }

    /// <summary>
    /// 🔴 <b>A text cell's WHITESPACE is now verbatim too, and that is deliberate rather than incidental.</b>
    /// <c>TabularDebrand.Cell</c> called <c>Debrand.Text</c> unconditionally, and that helper also collapses runs
    /// of whitespace and trims — so until ruling 18 every text cell in every export was silently trimmed and
    /// whitespace-collapsed. Removing the de-brand removed that too. It is a real change to every document the
    /// product emits and nothing pinned it, so it is pinned here.
    ///
    /// <para>It has two consequences worth naming. The good one: <c>MasterListTabularProjector</c> indents each
    /// Chart-of-Accounts row by depth and documents that the export mirrors the screen — the trim had been
    /// destroying that indentation in all five formats, and it now survives. The dangerous one: the CSV
    /// formula-injection guard used to be handed pre-trimmed text and so could test <c>field[0]</c>; it can no
    /// longer, and <c>TabularExportTests</c> pins the indented-injection case that fell through.</para>
    /// </summary>
    [Fact]
    public void A_text_cell_keeps_its_leading_trailing_and_doubled_internal_whitespace()
    {
        const string Indented = "    Sundry Debtors";              // hierarchy indentation from the projector
        const string Doubled = "Smith  and  Co";                   // a doubled internal space the user typed
        const string Trailing = "Bright Traders  ";

        var export = new TabularExport("Chart of Accounts",
            new[] { new TabularColumn("Name", CellType.Text) },
            new[]
            {
                TabularRow.Of(TabularCell.Text(Indented)),
                TabularRow.Of(TabularCell.Text(Doubled)),
                TabularRow.Of(TabularCell.Text(Trailing)),
            });

        string html = Utf8(HtmlReportWriter.Write(export));
        string json = Utf8(JsonReportWriter.Write(export));

        Assert.Contains(Indented, html, StringComparison.Ordinal);
        Assert.Contains(Doubled, html, StringComparison.Ordinal);
        Assert.Contains(Trailing, html, StringComparison.Ordinal);
        Assert.Contains(Indented, json, StringComparison.Ordinal);
        Assert.Contains(Doubled, json, StringComparison.Ordinal);
    }

    // ================================================================ the TABULAR title seam (ruling 18)
    //
    // 🔴 The four tests above pin the CHROME half in each format with a product-authored title. These pin the
    // OTHER half — the title that IS a counterparty's name — which is the case that shipped broken: the seam was
    // built on PrintReport for the PDF and never given a tabular twin, so a report headed "Ledger Monthly
    // Summary — Tally Traders Pvt Ltd" reached the HTML <title>, the XML @title, the JSON title and the XLSX
    // worksheet name with the party's name mangled, from four shipped egress points (Export, Email, Print
    // preview, WhatsApp share).

    // Deliberately SHORT (30 chars): the XLSX worksheet name is capped at 31 characters by Excel, and a longer
    // realistic heading would be truncated before the party's name appeared, so the test would prove nothing
    // about that format. The shape is what matters — our label concatenated with a master name.
    private const string HeadingWithMasterName = "Ledger - " + Counterparty;

    private static TabularExport TabularTitleCarriesAMasterName() => new(
        title: HeadingWithMasterName,
        columns: new[] { new TabularColumn("Particulars", CellType.Text) },
        rows: new[] { TabularRow.Of(TabularCell.Text("Opening Balance")) },
        titleCarriesMasterName: true);

    private static TabularExport TabularSameHeadingUnflagged() => new(
        title: HeadingWithMasterName,
        columns: new[] { new TabularColumn("Particulars", CellType.Text) },
        rows: new[] { TabularRow.Of(TabularCell.Text("Opening Balance")) });

    /// <summary>
    /// The seam itself: one title string, two provenances, two outcomes. Flagged, it is emitted verbatim;
    /// unflagged — the same bytes, a product-authored heading that merely happens to carry the token — it is
    /// still scrubbed. Asserting BOTH is what stops a "fix" that simply stops de-branding titles altogether.
    /// </summary>
    [Fact]
    public void A_tabular_title_marked_as_carrying_a_master_name_is_emitted_verbatim_and_an_unmarked_one_is_scrubbed()
    {
        Assert.Equal(HeadingWithMasterName, TabularExport.TitleText(TabularTitleCarriesAMasterName()));
        Assert.Equal("Ledger - Traders Pvt Ltd", TabularExport.TitleText(TabularSameHeadingUnflagged()));
    }

    [Fact]
    public void The_tabular_master_name_flag_defaults_to_false()
    {
        var export = new TabularExport("Trial Balance",
            new[] { new TabularColumn("Particulars", CellType.Text) },
            System.Array.Empty<TabularRow>());
        Assert.False(export.TitleCarriesMasterName,
            "the guarded treatment is the fail-safe default; a producer that forgets the flag must scrub its own "
          + "text, never leak the brand.");
    }

    /// <summary>
    /// End of each pipe: a heading flagged as carrying a counterparty's name reaches the HTML document title,
    /// the XML report attribute, the JSON title and the XLSX worksheet name IN FULL. One theory over all four so
    /// a future format cannot be added on the scrubbing path unnoticed.
    /// </summary>
    [Theory]
    [InlineData("html")]
    [InlineData("xml")]
    [InlineData("json")]
    [InlineData("xlsx")]
    public void A_report_headed_with_a_counterparty_name_keeps_that_name_in_every_tabular_format(string format)
    {
        var export = TabularTitleCarriesAMasterName();
        string text = format switch
        {
            "html" => Utf8(HtmlReportWriter.Write(export)),
            "xml" => Utf8(XmlReportWriter.Write(export)),
            "json" => Utf8(JsonReportWriter.Write(export)),
            _ => WorkbookXml(XlsxWriter.Write(export)),
        };

        // The party's own name is on the heading, in full.
        Assert.Contains(Counterparty, text, StringComparison.Ordinal);
        // Scrubbed, not blanked, is not enough here: the token must be PRESENT, which is the whole point.
        Assert.Contains("Tally", text, StringComparison.Ordinal);
    }

    /// <summary>The same heading UNFLAGGED is still scrubbed in every format — the guard was not simply removed.</summary>
    [Theory]
    [InlineData("html")]
    [InlineData("xml")]
    [InlineData("json")]
    [InlineData("xlsx")]
    public void The_same_heading_unflagged_is_still_debranded_in_every_tabular_format(string format)
    {
        var export = TabularSameHeadingUnflagged();
        string text = format switch
        {
            "html" => Utf8(HtmlReportWriter.Write(export)),
            "xml" => Utf8(XmlReportWriter.Write(export)),
            "json" => Utf8(JsonReportWriter.Write(export)),
            _ => WorkbookXml(XlsxWriter.Write(export)),
        };

        Assert.Equal(0, CountBrand(text));
        // De-branded, not blanked: the rest of the heading survives.
        Assert.Contains("Traders Pvt Ltd", text, StringComparison.Ordinal);
    }

    /// <summary>The XLSX worksheet NAME lives in xl/workbook.xml, which is where a title-shaped defect shows up.</summary>
    private static string WorkbookXml(byte[] xlsx)
    {
        using var zip = new ZipArchive(new MemoryStream(xlsx), ZipArchiveMode.Read);
        using var reader = new StreamReader(zip.GetEntry("xl/workbook.xml")!.Open(), Encoding.UTF8);
        return reader.ReadToEnd();
    }

    /// <summary>
    /// The de-brander that used to run over cells was newline-aware (<c>TabularDebrand.Cell</c> scrubbed each
    /// physical line and rejoined), so removing it must not disturb a multi-line cell — a party's postal address
    /// is the everyday case. This is the regression guard for deleting that helper.
    /// </summary>
    [Fact]
    public void A_multi_line_address_cell_keeps_its_line_structure_in_every_format()
    {
        var export = new TabularExport(
            title: "Ledger Vouchers",
            columns: new[] { new TabularColumn("Party", CellType.Text) },
            rows: new[] { TabularRow.Of(TabularCell.Text(Counterparty + "\r\n42 MG Road\r\nMumbai")) });

        // CSV: RFC-4180 quotes the field and carries the CRLF inside it verbatim.
        Assert.Contains("\"" + Counterparty + "\r\n42 MG Road\r\nMumbai\"", Utf8(CsvWriter.Write(export)), StringComparison.Ordinal);
        // HTML: the newlines become <br> and the address reads as three lines.
        Assert.Contains(Counterparty + "<br>42 MG Road<br>Mumbai", Utf8(HtmlReportWriter.Write(export)), StringComparison.Ordinal);

        // 🔴 "in_every_format" now means what the name says. TabularDebrand served FIVE writers and was
        // newline-aware; this guard asserted only two of them, so a newline regression in XLSX, XML or JSON
        // would have shipped under a test whose name claimed to cover it.
        //
        // XLSX: the cell text is XML-escaped with xml:space="preserve", so the newlines survive as raw LF.
        Assert.Contains(Counterparty + "\r\n42 MG Road\r\nMumbai", SheetXml(XlsxWriter.Write(export)), StringComparison.Ordinal);
        // XML: a body cell carries the newlines through the element text.
        Assert.Contains(Counterparty + "\r\n42 MG Road\r\nMumbai", Utf8(XmlReportWriter.Write(export)), StringComparison.Ordinal);
        // JSON: newlines are the \r\n escapes, not literal breaks that would make the document invalid.
        Assert.Contains(Counterparty + "\\r\\n42 MG Road\\r\\nMumbai", Utf8(JsonReportWriter.Write(export)), StringComparison.Ordinal);
    }

    private static string SheetXml(byte[] xlsx)
    {
        using var zip = new ZipArchive(new MemoryStream(xlsx), ZipArchiveMode.Read);
        using var reader = new StreamReader(zip.GetEntry("xl/worksheets/sheet1.xml")!.Open(), Encoding.UTF8);
        return reader.ReadToEnd();
    }

    // ================================================================ the retail receipt

    /// <summary>
    /// The POS receipt is the counterparty document a walk-in customer is physically handed. The customer's name
    /// and the BANK NAME on the cheque tender line are theirs; the store name, the receipt title and the
    /// <c>/Title</c> metadata are ours and stay de-branded.
    /// </summary>
    [Fact]
    public void A_retail_receipt_keeps_the_customer_and_the_cheque_bank_and_still_debrands_the_store_block()
    {
        var data = new PosReceiptData
        {
            Title = "Tally Retail Receipt",
            StoreName = "Tally Store",
            BillNumber = "1",
            DateText = "10-Apr-2024",
            Party = Counterparty,
            Items = new[] { new PosReceiptItem { Description = "Widget", QuantityText = "1", RateText = "100.00", Value = new Money(100m) } },
            Tenders = new[]
            {
                new PosReceiptTender { Label = "Cheque/DD", Amount = new Money(100m), Reference = "Tally Co-operative Bank Cheque No. 235681" },
            },
            TotalTaxable = new Money(100m),
            CashTendered = Money.Zero,
            Change = Money.Zero,
        };

        string s = AsLatin1(PosReceiptPdf.Render(data, new PageConfig()));

        Assert.Contains("Customer: " + Counterparty, s, StringComparison.Ordinal);
        Assert.Contains("Tally Co-operative Bank Cheque No. 235681", s, StringComparison.Ordinal);
        // Exactly two survivors: the customer and the bank. The title, the store name and the /Title metadata —
        // all ours — contributed none.
        Assert.Equal(2, CountBrand(s));
        Assert.Contains("Retail Receipt", s, StringComparison.Ordinal);
        Assert.Contains("Store", s, StringComparison.Ordinal);
    }
}
