using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Apex.Desktop.Services;
using Apex.Desktop.ViewModels;
using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Services;
using Xunit;

namespace Apex.Desktop.Tests;

/// <summary>
/// 🔴 <b>REVIEW FIXTURE (adversarial, written from OUTSIDE the branch's own tests).</b>
/// An ISD credit receipt now enters GSTR-3B Table 4(C) "Net ITC available" through the new 4(A)(4) term, but the
/// GSTR-3B SCREEN renders no 4(A)(4) row and still tells the operator in words that 4(A)(4) is "NOT nil; absent".
/// So (C) no longer equals the (A) rows shown minus the (B) rows shown, and the advisory above it is false.
/// </summary>
public sealed class Gstr3bIsdRowReviewTests : IDisposable
{
    private static readonly DateOnly FyStart = new(2025, 4, 1);
    private static readonly DateOnly D1 = new(2025, 4, 10);

    private readonly string _tempDir;
    private readonly CompanyStorage _storage;

    public Gstr3bIsdRowReviewTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "ApexIsdRowReview_" + Guid.NewGuid().ToString("N"));
        _storage = new CompanyStorage(_tempDir);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true); }
        catch (IOException) { }
    }

    private MainWindowViewModel ShellWithIsdReceipt()
    {
        var vm = new MainWindowViewModel(_storage);
        vm.NewCompanyName = "ISD Row Review Co";
        vm.CreateCompany();

        var c = vm.Company!;
        c.FinancialYearStart = FyStart;
        c.BooksBeginFrom = FyStart;

        new GstService(c).EnableGst(new GstConfig
        {
            HomeStateCode = "27",
            Gstin = "27AAPFU0939F1ZV",
            RegistrationType = GstRegistrationType.Regular,
            ApplicableFrom = FyStart,
            Periodicity = GstReturnPeriodicity.Monthly,
        });

        var branch = new Apex.Ledger.Domain.Ledger(
            Guid.NewGuid(), "Head Office A/c", c.FindGroupByName("Sundry Creditors")!.Id, Money.Zero, false);
        c.AddLedger(branch);

        new GstIsdCreditService(c).Post(
            "ISD/2025/07", D1,
            new GstIsdCreditService.IsdCreditAmount(50_000, 50_000, 0, 0),   // Rs.500 CGST + Rs.500 SGST
            branch.Id, D1);

        vm.ShowGateway();
        return vm;
    }

    private static ReportRow? Find(MainWindowViewModel vm, string startsWith) =>
        vm.Reports!.Rows.FirstOrDefault(r => r.Col1.StartsWith(startsWith, StringComparison.Ordinal));

    [Fact]
    public void The_screen_shows_a_4a4_row_for_the_isd_credit_it_has_already_added_into_4c()
    {
        var vm = ShellWithIsdReceipt();
        vm.OpenReport(ReportKind.Gstr3b);

        // The engine HAS the figure: 4(C) moved by Rs.500 per head.
        var c = Find(vm, "(C) Net ITC available");
        Assert.NotNull(c);
        Assert.Equal("500.00", c!.Col3);
        Assert.Equal("500.00", c.Col4);

        // ...so the screen must itemise where it came from.
        var a4 = Find(vm, "(A)(4)");
        Assert.True(a4 is not null,
            "GSTR-3B Table 4(C) includes the ISD credit but the screen renders no 4(A)(4) row — the displayed " +
            "(A) rows minus the (B) rows no longer reconcile to the displayed (C).");
        Assert.Equal("500.00", a4!.Col3);
        Assert.Equal("500.00", a4.Col4);
    }

    [Fact]
    public void The_not_modelled_advisory_no_longer_claims_4a4_is_absent()
    {
        Assert.DoesNotContain("4(A)(4)", ReportsViewModel.NotModelled4);
    }

    [Fact]
    public void The_displayed_a_rows_minus_the_b_rows_equal_the_displayed_c_row()
    {
        var vm = ShellWithIsdReceipt();
        vm.OpenReport(ReportKind.Gstr3b);

        decimal Col(string prefix, Func<ReportRow, string> pick)
        {
            var row = Find(vm, prefix);
            if (row is null) return 0m;
            var t = pick(row);
            return string.IsNullOrWhiteSpace(t) ? 0m : decimal.Parse(t, System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture);
        }

        foreach (var pick in new Func<ReportRow, string>[] { r => r.Col3, r => r.Col4, r => r.Col5 })
        {
            var shown = Col("(A)(2)", pick) + Col("(A)(3)", pick) + Col("(A)(4)", pick) + Col("(A)(5)", pick)
                        - Col("(B)(1)", pick) - Col("(B)(2)", pick);
            Assert.Equal(Col("(C) Net ITC available", pick), shown);
        }
    }
    /// <summary>
    /// 🔴 Money on the new Record-ISD-Credit screen must group in lakh/crore like every other money cell in the
    /// application. It used to call <c>ToString("N2", CultureInfo.InvariantCulture)</c> — Western grouping, and
    /// a spelling drift-lock D2 cannot see, because D2 matches the "#,##0.00" form of the same mistake.
    /// </summary>
    [Fact]
    public void The_isd_screen_groups_money_in_the_indian_system()
    {
        var vm = new MainWindowViewModel(_storage);
        vm.NewCompanyName = "ISD Grouping Co";
        vm.CreateCompany();
        var c = vm.Company!;
        c.FinancialYearStart = FyStart;
        c.BooksBeginFrom = FyStart;
        new GstService(c).EnableGst(new GstConfig
        {
            HomeStateCode = "27",
            Gstin = "27AAPFU0939F1ZV",
            RegistrationType = GstRegistrationType.Regular,
            ApplicableFrom = FyStart,
            Periodicity = GstReturnPeriodicity.Monthly,
        });
        var ho = new Apex.Ledger.Domain.Ledger(
            Guid.NewGuid(), "Head Office A/c", c.FindGroupByName("Sundry Creditors")!.Id, Money.Zero, false);
        c.AddLedger(ho);

        var page = new RecordIsdCreditViewModel(c, _storage);
        page.DocumentNumberText = "ISD/2025/LAKH";
        page.DocumentDateText = "10-Apr-2025";
        page.VoucherDateText = "10-Apr-2025";
        page.CgstText = "1234567";
        page.SelectedCounterpart = page.CounterpartOptions.Single(l => l.Id == ho.Id);

        Assert.True(page.Record(), page.Message);
        Assert.Equal("12,34,567.00", page.Recorded[0].Cgst);
        Assert.Equal("12,34,567.00", page.Recorded[0].Total);
    }
}
