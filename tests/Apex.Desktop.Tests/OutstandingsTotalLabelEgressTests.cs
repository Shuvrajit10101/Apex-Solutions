using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Io;
using Apex.Ledger.Reports;
using Apex.Ledger.Services;
using Apex.Desktop.Services;
using Apex.Desktop.ViewModels;
using Xunit;
using DomainLedger = Apex.Ledger.Domain.Ledger;

namespace Apex.Desktop.Tests;

/// <summary>
/// Census <b>11.13</b> — the total row of Bills Receivable / Bills Payable names what it sums, under BOTH Ctrl+B
/// Type of Bills values, on BOTH sides, in EVERY egress byte stream the report reaches (CSV, XLSX, HTML, XML,
/// JSON, ASCII, PDF). The tabular exports carry the report title but not its subtitle, so under Overdue Bills the
/// row label is the only place an exported file says the total is the overdue part; under All Bills the row must
/// NOT claim to be overdue.
///
/// <para>Independent fixture (wave 9 verifier): figures read off a book RELOADED through a fresh
/// <see cref="CompanyStorage"/>; every amount ends in an odd paisa. As-of = books-begin + 90. Orion Retail (debtor):
/// SR-1 4,321.09 due s+30 (overdue), SR-2 1,111.11 due s+200 (not due). Vega Mills (creditor): VP-1 2,468.13 due
/// s+60 (overdue), VP-2 5,555.55 due s+150 (not due).</para>
/// </summary>
public sealed class OutstandingsTotalLabelEgressTests : IDisposable
{
    private readonly string _tempDir;
    private readonly CompanyStorage _storage;

    public OutstandingsTotalLabelEgressTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "ApexTotLbl_" + Guid.NewGuid().ToString("N"));
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

    private (Company Company, DateOnly AsOf) Build()
    {
        var vm = new MainWindowViewModel(_storage);
        vm.NewCompanyName = "Total Label " + Guid.NewGuid().ToString("N")[..6];
        vm.CreateCompany();
        var c = vm.Company!;
        var s = c.BooksBeginFrom;
        var svc = new LedgerService(c);
        var journal = c.FindVoucherTypeByName("Journal")!;
        var orion = new DomainLedger(Guid.NewGuid(), "Orion Retail", c.FindGroupByName("Sundry Debtors")!.Id,
            Money.Zero, true, maintainBillByBill: true);
        var vega = new DomainLedger(Guid.NewGuid(), "Vega Mills", c.FindGroupByName("Sundry Creditors")!.Id,
            Money.Zero, false, maintainBillByBill: true);
        var sales = new DomainLedger(Guid.NewGuid(), "Sales TL", c.FindGroupByName("Sales Accounts")!.Id,
            Money.Zero, false);
        var purchases = new DomainLedger(Guid.NewGuid(), "Purchases TL", c.FindGroupByName("Purchase Accounts")!.Id,
            Money.Zero, true);
        foreach (var l in new[] { orion, vega, sales, purchases }) c.AddLedger(l);

        void Post(DomainLedger party, DomainLedger other, bool debtor, int day, string reference, decimal amount, int due)
        {
            var bill = new[] { new BillAllocation(BillRefType.NewRef, reference, Money.FromRupees(amount), dueDate: s.AddDays(due)) };
            svc.Post(new Voucher(Guid.NewGuid(), journal.Id, s.AddDays(day), debtor
                ? new[]
                {
                    new EntryLine(party.Id, Money.FromRupees(amount), DrCr.Debit, billAllocations: bill),
                    new EntryLine(other.Id, Money.FromRupees(amount), DrCr.Credit),
                }
                : new[]
                {
                    new EntryLine(other.Id, Money.FromRupees(amount), DrCr.Debit),
                    new EntryLine(party.Id, Money.FromRupees(amount), DrCr.Credit, billAllocations: bill),
                }));
        }

        Post(orion, sales, true, 5, "SR-1", 4_321.09m, 30);
        Post(orion, sales, true, 80, "SR-2", 1_111.11m, 200);
        Post(vega, purchases, false, 10, "VP-1", 2_468.13m, 60);
        Post(vega, purchases, false, 85, "VP-2", 5_555.55m, 150);
        _storage.Save(c);

        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        var fresh = new CompanyStorage(_tempDir);
        var reloaded = fresh.Load(fresh.ListCompanies().Single(e => e.Name == c.Name));
        return (reloaded, s.AddDays(90));
    }

    private static string XlsxText(byte[] bytes)
    {
        using var zip = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
        using var reader = new StreamReader(zip.GetEntry("xl/worksheets/sheet1.xml")!.Open(), Encoding.UTF8);
        return reader.ReadToEnd();
    }

    private static List<string> PdfStrings(byte[] pdf)
    {
        string text = Encoding.Latin1.GetString(pdf);
        var drawn = new List<string>();
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] != '(' || (i > 0 && text[i - 1] == '\\')) continue;
            var sb = new StringBuilder();
            int j = i + 1;
            for (; j < text.Length; j++)
            {
                if (text[j] == '\\') { if (j + 1 < text.Length) sb.Append(text[++j]); continue; }
                if (text[j] == ')') break;
                sb.Append(text[j]);
            }
            if (j + 2 < text.Length && text[j + 1] == ' ' && text[j + 2] == 'T') drawn.Add(sb.ToString());
            i = j;
        }
        return drawn;
    }

    [Theory]
    [InlineData(ReportKind.ReceivablesOutstanding, TypeOfBills.AllBills, "Total Receivable", "5432.20", "5,432.20")]
    [InlineData(ReportKind.ReceivablesOutstanding, TypeOfBills.OverdueBills, "Total Overdue Receivables", "4321.09", "4,321.09")]
    [InlineData(ReportKind.PayablesOutstanding, TypeOfBills.AllBills, "Total Payable", "8023.68", "8,023.68")]
    [InlineData(ReportKind.PayablesOutstanding, TypeOfBills.OverdueBills, "Total Overdue Payables", "2468.13", "2,468.13")]
    public void Total_row_label_matches_the_type_of_bills_in_every_egress_byte_stream(
        ReportKind kind, TypeOfBills type, string label, string plain, string grouped)
    {
        var (company, asOf) = Build();
        var r = new ReportsViewModel(company, kind);
        r.SetAsOf(asOf);
        // Go through the other value first so a label left over from a previous build is caught.
        r.SetTypeOfBills(type == TypeOfBills.AllBills ? TypeOfBills.OverdueBills : TypeOfBills.AllBills);
        r.SetTypeOfBills(type);

        var total = r.PayrollRows.Single(x => x.IsTotal);
        Assert.Equal(label, total.Cells[0].Text);
        Assert.Equal(grouped, total.Cells[5].Text);

        var export = ReportTabularProjector.Project(r);
        var csv = Encoding.UTF8.GetString(CsvWriter.Write(export));
        Assert.Contains($"{label},,,,,{plain}", csv);

        string[] streams =
        {
            csv,
            XlsxText(XlsxWriter.Write(export)),
            Encoding.UTF8.GetString(HtmlReportWriter.Write(export)),
            Encoding.UTF8.GetString(XmlReportWriter.Write(export)),
            Encoding.UTF8.GetString(JsonReportWriter.Write(export)),
            Encoding.UTF8.GetString(AsciiReportWriter.Write(export)),
        };
        var pdf = PdfStrings(ReportPdf.Render(ReportPrintProjector.Project(r),
            new PageConfig { Size = PageSize.A4, Orientation = PageOrientation.Portrait }));

        // The label that must NOT appear: under All Bills nothing claims to be overdue; under Overdue Bills the bare
        // whole-side label is gone ("Total Overdue Receivables" does not contain "Total Receivable").
        var forbidden = type == TypeOfBills.AllBills
            ? "Total Overdue"
            : (kind == ReportKind.ReceivablesOutstanding ? "Total Receivable" : "Total Payable");
        foreach (var s in streams)
        {
            Assert.Contains(label, s);
            Assert.DoesNotContain(forbidden, s);
            Assert.True(s.Contains(plain, StringComparison.Ordinal) || s.Contains(grouped, StringComparison.Ordinal),
                $"the stream must carry the total {grouped} to the paisa");
        }
        Assert.Contains(label, pdf);
        Assert.DoesNotContain(pdf, d => d.Contains(forbidden, StringComparison.Ordinal));
        Assert.Contains(pdf, d => d.Contains(grouped, StringComparison.Ordinal));
    }
}
