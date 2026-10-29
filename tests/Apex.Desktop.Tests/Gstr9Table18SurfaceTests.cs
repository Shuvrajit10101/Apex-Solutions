using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Apex.Desktop.Services;
using Apex.Desktop.ViewModels;
using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Io;
using Apex.Ledger.Services;
using Xunit;
using DomainLedger = Apex.Ledger.Domain.Ledger;

namespace Apex.Desktop.Tests;

/// <summary>
/// 🔴 <b>Census 6.12 — GSTR-9 Table 18 (inward HSN summary) is REACHABLE and LEAVES THE SCREEN.</b> The book is saved,
/// reopened through a FRESH view model by the operator's own route (Company Select, arrow keys, Enter), the report
/// is reached by arrows through Reports → Statutory Reports → Annual Returns → GSTR-9, and the Table 18 figures are
/// read back off the EXPORTED CSV bytes and the PRINT projection — the artefacts a reviewer actually receives.
///
/// <para>Fixture (worked by hand): one intra-state purchase of an SAC-bearing service ledger "Repairs" (SAC 998719),
/// ₹12,345.67 @ 18% ⇒ 18% = 2,222.2206 ⇒ CGST 1,111.11 + SGST 1,111.11 = ₹2,222.22.</para>
/// </summary>
public sealed class Gstr9Table18SurfaceTests : IDisposable
{
    private const string GstinMaharashtra = "27AAPFU0939F1ZV";
    private const string GstinSupplier = "27AAACC1206D1ZM";
    private const string InwardSac = "998719";
    private const string Table18Heading = "18  HSN-wise summary of inward supplies";
    private static readonly DateOnly FyStart = new(2024, 4, 1);

    private readonly string _tempDir;
    private readonly CompanyStorage _storage;

    public Gstr9Table18SurfaceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "ApexG9T18_" + Guid.NewGuid().ToString("N"));
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

    [Fact]
    public void Table_18_reaches_the_exported_csv_and_the_print_off_a_reopened_book_by_arrow_keys()
    {
        var name = SaveFixture();

        var vm = new MainWindowViewModel(_storage);
        OpenSavedCompany(vm, name);
        Assert.NotNull(vm.Company);

        vm.ShowAnnualReturnsMenu();
        ArrowToAndDrill(vm, "GSTR-9");
        Assert.Equal(Screen.Report, vm.CurrentScreen);
        var reports = vm.Reports!;
        Assert.Equal("Form GSTR-9 — Annual Return", reports.Title);

        // On screen: the band-2 row and its foot.
        // W8 review F3: the HSN / SAC cell holds the BARE code (as Table 17's rows do), and the table is told apart
        // by its own sub-heading row, which sits after Table 17's foot and before Table 18's rows and foot.
        var band = reports.PayrollRows2.ToList();
        var row = Assert.Single(band.Where(r => r.Cells[0].Text == InwardSac));
        Assert.DoesNotContain(band, r => r.Cells[0].Text.StartsWith("18  " + InwardSac, StringComparison.Ordinal));
        Assert.Equal("Repairs", row.Cells[1].Text);
        Assert.Equal("12,345.67", row.Cells[4].Text);
        Assert.Equal("2,222.22", row.Cells[5].Text);
        var foot = Assert.Single(band.Where(r => r.Cells[0].Text == "18  Total"));
        Assert.Equal("12,345.67", foot.Cells[4].Text);
        Assert.Equal("2,222.22", foot.Cells[5].Text);
        var heading = Assert.Single(band.Where(r => r.Cells[0].Text == Table18Heading));
        var foot17 = Assert.Single(band.Where(r => r.Cells[0].Text == "17  Total"));
        Assert.True(band.IndexOf(foot17) < band.IndexOf(heading)
                    && band.IndexOf(heading) < band.IndexOf(row)
                    && band.IndexOf(row) < band.IndexOf(foot),
            "Table 18's rows must sit between its own sub-heading and its own foot, after Table 17's foot.");

        // The exported BYTES: the record's FIRST field is the bare SAC, and the sub-heading is its own record.
        var csv = Encoding.UTF8.GetString(CsvWriter.Write(ReportTabularProjector.Project(reports)));
        var lines = csv.Split('\n').Select(l => l.TrimEnd('\r')).ToList();
        var line = lines.Single(l => l.Contains(InwardSac));
        Assert.StartsWith(InwardSac + ",", line);
        Assert.Contains("12345.67", line);
        Assert.Contains("2222.22", line);
        Assert.Contains(lines, l => l.StartsWith(Table18Heading, StringComparison.Ordinal));
        Assert.True(lines.FindIndex(l => l.StartsWith(Table18Heading, StringComparison.Ordinal)) < lines.IndexOf(line));
        Assert.Contains("18  Total", csv);

        // The printed document.
        var printed = ReportPrintProjector.Project(reports);
        Assert.Contains(printed.Rows, r => r.Cells.Count > 0 && r.Cells[0] == Table18Heading);
        var printedRow = Assert.Single(printed.Rows.Where(r => r.Cells.Count > 0 && r.Cells[0] == InwardSac));
        Assert.Contains(printedRow.Cells, c => c.Contains("2,222.22"));
        Assert.Contains(printedRow.Cells, c => c.Contains("12,345.67"));
        Assert.DoesNotContain("tally", csv, StringComparison.OrdinalIgnoreCase);
    }

    // ================================================================= scaffolding

    private string SaveFixture()
    {
        var vm = new MainWindowViewModel(_storage);
        var name = "G9 Inward " + Guid.NewGuid().ToString("N")[..6];
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

        var repairs = Add(c, "Repairs", "Indirect Expenses", true);
        repairs.SalesPurchaseGst = new StockItemGstDetails
        {
            HsnSac = InwardSac, Taxability = GstTaxability.Taxable, RateBasisPoints = 1800,
            SupplyType = GstSupplyType.Services,
        };
        var supplier = Add(c, "Local Supplier", "Sundry Creditors", false);
        supplier.PartyGst = new PartyGstDetails
        { RegistrationType = GstRegistrationType.Regular, Gstin = GstinSupplier, StateCode = "27" };

        var tax = gst.ComputeInvoiceTax(
            new[] { new GstService.TaxableLine(Money.FromRupees(12_345.67m), 1800) }, false, GstTaxDirection.Input);
        Assert.Equal(2_222.22m, tax.TotalTax.Amount);
        var lines = new List<EntryLine>
        {
            new(repairs.Id, Money.FromRupees(12_345.67m), DrCr.Debit),
            new(supplier.Id, Money.FromRupees(12_345.67m + tax.TotalTax.Amount), DrCr.Credit),
        };
        lines.AddRange(tax.TaxLines);
        new LedgerService(c).Post(new Voucher(Guid.NewGuid(),
            c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Purchase).Id, new DateOnly(2024, 5, 7), lines,
            partyId: supplier.Id));

        _storage.Save(c);
        return name;
    }

    private static void OpenSavedCompany(MainWindowViewModel vm, string companyName)
    {
        vm.ShowCompanySelect();
        for (var i = 0; i < vm.Menu.Count + 2; i++)
        {
            if (vm.Menu[vm.SelectedIndex].Label == companyName) { vm.Menu[vm.SelectedIndex].Activate(); return; }
            vm.MoveDown();
        }
        Assert.Fail($"'{companyName}' was not reachable by arrow navigation on the Company Select screen.");
    }

    private static void ArrowToAndDrill(MainWindowViewModel vm, string label)
    {
        for (var i = 0; i < vm.Menu.Count + 2; i++)
        {
            if (vm.Menu[vm.SelectedIndex].Label == label) { vm.DrillIn(); return; }
            vm.MoveDown();
        }
        Assert.Fail($"'{label}' was not reachable by arrow navigation in the active column.");
    }

    private static DomainLedger Add(Company c, string name, string groupName, bool debit)
    {
        var group = c.FindGroupByName(groupName) ?? throw new InvalidOperationException($"No group '{groupName}'.");
        var ledger = new DomainLedger(Guid.NewGuid(), name, group.Id, Money.Zero, openingIsDebit: debit);
        c.AddLedger(ledger);
        return ledger;
    }
}
