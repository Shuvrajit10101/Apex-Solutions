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
/// renderer handed a cell cannot tell our text from theirs. It does not have to: provenance is STRUCTURAL here.
/// A <see cref="PrintColumn.Header"/>/<see cref="TabularColumn.Header"/> caption and a report's own subtitle are
/// compile-time strings this product authored; a <see cref="PrintRow"/> / <see cref="TabularRow"/> cell is ALWAYS
/// book data projected from masters and vouchers. The single place the two are concatenated is a report TITLE
/// (<c>"Ledger Account - " + ledger.Name</c>), and that one mixing point is what
/// <see cref="PrintReport.TitleCarriesMasterName"/> exists to declare. Every assertion below is written against
/// that split.</para>
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
