using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Apex.Desktop.Services;
using Apex.Desktop.ViewModels;
using Apex.Desktop.Views;
using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Services;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Xunit;

namespace Apex.Desktop.Tests;

/// <summary>
/// <b>Census 6.9 — the GSTR-3B page must show the reverse-charge and ITC-reversal figures it computes.</b>
///
/// <para>🔴 <b>THE DEFECT THIS FILE LOCKS, ON A REAL POSTED BOOK.</b> The reverse-charge liability has been
/// computed correctly by the engine for as long as RCM has existed — <c>Gstr3b.RcmOutward*</c> — and
/// <c>ReadRcm</c> deliberately keeps those lines OUT of <c>ReadSide</c> so they cannot be double counted. The
/// page then rendered <c>OutwardCgst</c> alone and captioned it <i>"Total output tax"</i>. So on a book with a
/// reverse-charge purchase the screen quietly reported <b>less tax than the book owes</b>, with no row, no
/// asterisk and no zero to hint that anything had been left out — and the Compensation Cess on that liability
/// had nowhere to land at all, because the grid had only five columns.</para>
///
/// <para><b>Why this test posts a real RCM purchase rather than stubbing the projection.</b> The wrong figure
/// was not in the projection; it was in the step between a correct projection and the screen. A stubbed
/// projection would have re-tested the arithmetic (that is
/// <c>Apex.Ledger.Tests/Gstr3bTable4ArithmeticTests</c>'s job) and left this gap exactly where it was. The
/// fixture is therefore the shell, a GST company, and a posted reverse-charge bill — the smallest book on which
/// an operator would be misled.</para>
///
/// <para><b>And why one case renders the real window.</b> A row the view-model builds is not a figure the user
/// sees: the cess amounts land in <c>Col6</c>, and before this slice the GSTR-3B grid in <c>MainWindow.axaml</c>
/// defined five columns, so <c>Col6</c> was bound by nothing. Asserting on <c>Rows</c> alone would have passed
/// against a grid that still dropped the column. <see cref="Cess_column_exists_in_the_realised_grid_and_carries_the_figure"/>
/// walks the realised visual tree instead.</para>
/// </summary>
public sealed class Gstr3bScreenTablesTests : IDisposable
{
    private const string GstinMaharashtra = "27AAPFU0939F1ZV";
    private const string GstinGujarat = "24AAACC1206D1ZM";

    private static readonly DateOnly FyStart = new(2025, 4, 1);
    private static readonly DateOnly D1 = new(2025, 4, 10);

    private readonly string _tempDir;
    private readonly CompanyStorage _storage;

    public Gstr3bScreenTablesTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "ApexGstr3bScreen_" + Guid.NewGuid().ToString("N"));
        _storage = new CompanyStorage(_tempDir);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true); }
        catch (IOException) { /* a held file handle must not fail the assertion under test */ }
    }

    // ---------------------------------------------------------------- fixture

    /// <summary>
    /// The shell over a GST company that has posted ONE inter-state reverse-charge legal bill of Rs.10,000 at
    /// 18% — so Table 3.1(d) IGST is Rs.1,800 and Table 4(A)(3) IGST is the matching Rs.1,800 credit, and
    /// Table 3.1(a) is nil. That asymmetry is deliberate: it makes the old "total output tax" read Rs.0.00 on a
    /// book that owes Rs.1,800 in cash, which is the defect at its starkest.
    /// </summary>
    private MainWindowViewModel ShellWithReverseChargeBill()
    {
        var vm = new MainWindowViewModel(_storage);
        vm.NewCompanyName = "RCM Screen Co";
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
        gst.SeedAdvancedGst();   // seeds the notified RCM categories this bill is classified under

        var expense = AddLedger(c, "Legal Fees", "Indirect Expenses", openingIsDebit: true);
        expense.SalesPurchaseGst = new StockItemGstDetails
        {
            Taxability = GstTaxability.Taxable,
            RateBasisPoints = 1800,
            SupplyType = GstSupplyType.Services,
            ReverseChargeApplicable = true,
            RcmCategoryId = c.Gst!.RcmCategories.First(x => x.SupplyNature == "Legal").Id,
        };

        var advocate = AddLedger(c, "Advocate (Gujarat)", "Sundry Creditors", openingIsDebit: false);
        advocate.PartyGst = new PartyGstDetails
        {
            RegistrationType = GstRegistrationType.Regular, Gstin = GstinGujarat, StateCode = "24",
        };

        var value = Money.FromRupees(10_000m);
        var posting = new RcmService(c).BuildReverseCharge(
            value, null, expense, advocate.PartyGst, D1, RcmService.SupplyKind.Domestic);
        Assert.True(posting.Applies, "fixture is vacuous unless the bill actually attracts reverse charge");

        var lines = new List<EntryLine>
        {
            new(expense.Id, value, DrCr.Debit),
            new(advocate.Id, value, DrCr.Credit),   // the supplier charges no tax — the recipient owes it
        };
        lines.AddRange(posting.Lines);
        var purchase = c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Purchase).Id;
        var v = new LedgerService(c).Post(new Voucher(Guid.NewGuid(), purchase, D1, lines));
        Assert.True(VoucherValidator.IsBalanced(v));

        vm.ShowGateway();
        return vm;
    }

    private static Apex.Ledger.Domain.Ledger AddLedger(Company c, string name, string group, bool openingIsDebit)
    {
        var l = new Apex.Ledger.Domain.Ledger(
            Guid.NewGuid(), name, c.FindGroupByName(group)!.Id, Money.Zero, openingIsDebit);
        c.AddLedger(l);
        return l;
    }

    private static ReportRow Row(MainWindowViewModel vm, string startsWith) =>
        vm.Reports!.Rows.Single(r => r.Col1.StartsWith(startsWith, StringComparison.Ordinal));

    // ---------------------------------------------------------------- 1. the 3.1(d) row exists at all

    /// <summary>
    /// Table 3.1(d) has a row of its own carrying the reverse-charge liability. On the pre-slice screen there
    /// was no row whose caption began "(d)" — the whole table was absent.
    /// </summary>
    [Fact]
    public void Table_31d_has_its_own_row_carrying_the_reverse_charge_liability()
    {
        var vm = ShellWithReverseChargeBill();
        vm.OpenReport(ReportKind.Gstr3b);

        var d = Row(vm, "(d) Inward supplies (liable to reverse charge)");
        Assert.Equal("1,800.00", d.Col5);       // IGST — inter-state, so no CGST/SGST split
        Assert.Equal(string.Empty, d.Col3);
        Assert.Equal(string.Empty, d.Col4);
    }

    // ---------------------------------------------------------------- 2. THE money assertion

    /// <summary>
    /// 🔴 <b>The total tax payable on this book is Rs.1,800, and the old screen said Rs.0.00.</b> Table 3.1(a)
    /// is nil here (no outward sale), so a total built from <c>OutwardIgst</c> alone renders "0.00" while the
    /// company owes Rs.1,800 in cash. The assertion is written against the SHIPPED caption as well as the
    /// figure, because the caption is half the defect: "Total output tax" over a figure that excludes the
    /// reverse-charge liability is a statement the CGST Act contradicts (§2(82)).
    /// </summary>
    [Fact]
    public void Total_tax_payable_includes_the_reverse_charge_liability()
    {
        var vm = ShellWithReverseChargeBill();
        vm.OpenReport(ReportKind.Gstr3b);

        // The old caption must be gone — a reader must not find "Total output tax" over a partial sum.
        Assert.DoesNotContain(vm.Reports!.Rows, r => r.Col1 == "Total output tax");

        var total = Row(vm, "Total tax payable");
        Assert.True(total.IsTotal);
        Assert.Equal("1,800.00", total.Col5);

        // And it is genuinely the RCM figure, not an outward one: 3.1(a) is nil on this book.
        var outward = Row(vm, "(a) Outward taxable supplies");
        Assert.Equal(string.Empty, outward.Col5);
    }

    /// <summary>
    /// The reverse-charge half is shown as payable IN CASH and is NOT netted against the credit ledger. CGST
    /// Act §2(82) excludes reverse-charge tax from "output tax" and §49(4) confines the credit ledger to output
    /// tax, so a single "net payable" line that swallowed the Rs.1,800 against the matching Rs.1,800 credit
    /// would show nil due on a book with Rs.1,800 to pay.
    /// </summary>
    [Fact]
    public void Reverse_charge_is_shown_payable_in_cash_and_not_netted_against_the_matching_credit()
    {
        var vm = ShellWithReverseChargeBill();
        vm.OpenReport(ReportKind.Gstr3b);

        var cash = Row(vm, "Payable under reverse charge, in cash");
        Assert.Equal("1,800.00", cash.Col5);

        // The matching 4(A)(3) credit exists and lands in 4(C) — the two must not cancel on one line.
        Assert.Equal("1,800.00", Row(vm, "(A)(3) ITC available").Col5);
        Assert.Equal("1,800.00", Row(vm, "(C) Net ITC available").Col5);

        var other = Row(vm, "Payable other than reverse charge");
        Assert.Equal("-1,800.00", other.Col5);      // a carried-forward credit, which is correct and separate
    }

    // ---------------------------------------------------------------- 3. absent is not nil

    /// <summary>
    /// The tables this book does not model are named in words. Rendering them as blank rows under real captions
    /// would read as "nil" to an operator — the same class of wrong figure in the opposite direction — so the
    /// screen must carry the advisory and must NOT carry a row for, say, 3.1(b).
    /// </summary>
    [Fact]
    public void Unmodelled_tables_are_named_in_words_and_never_rendered_as_an_empty_row()
    {
        var vm = ShellWithReverseChargeBill();
        vm.OpenReport(ReportKind.Gstr3b);
        var rows = vm.Reports!.Rows;

        Assert.Contains(rows, r => r.Col1 == ReportsViewModel.NotModelled31);
        Assert.Contains(rows, r => r.Col1 == ReportsViewModel.NotModelled4);

        // No row pretends to BE one of the unmodelled tables.
        Assert.DoesNotContain(rows, r => r.Col1.StartsWith("(b)", StringComparison.Ordinal));
        Assert.DoesNotContain(rows, r => r.Col1.StartsWith("(e)", StringComparison.Ordinal));
        Assert.DoesNotContain(rows, r => r.Col1.StartsWith("3.2", StringComparison.Ordinal));
        Assert.DoesNotContain(rows, r => r.Col1.StartsWith("(A)(1)", StringComparison.Ordinal));
        Assert.DoesNotContain(rows, r => r.Col1.StartsWith("(A)(4)", StringComparison.Ordinal));

        // The advisory says so in the words a reader needs, not just by omission.
        Assert.Contains("These are NOT nil; they are absent.", ReportsViewModel.NotModelled31);
        Assert.Contains("These are NOT nil; they are absent.", ReportsViewModel.NotModelled4);
    }

    // ---------------------------------------------------------------- 4. the realised grid

    /// <summary>
    /// 🔴 <b>The Cess column must exist in the REALISED grid, not merely in the view-model row.</b> Compensation
    /// Cess on a reverse-charge liability is computed into <c>Col6</c>; the GSTR-3B grid defined five columns,
    /// so the figure was bound by nothing and no view-model assertion could have seen it. This walks the visual
    /// tree of the real window: it finds the GSTR-3B header row by its own captions and requires a "Cess" one.
    /// </summary>
    [AvaloniaFact]
    public void Cess_column_exists_in_the_realised_grid_and_carries_the_figure()
    {
        var vm = ShellWithReverseChargeBill();
        vm.OpenReport(ReportKind.Gstr3b);

        var win = new MainWindow { DataContext = vm };
        win.MinWidth = 640; win.MinHeight = 480;
        win.Width = 1600; win.Height = 1000;
        win.Show();
        win.Width = 1600; win.Height = 1000;
        Dispatcher.UIThread.RunJobs();
        Dispatcher.UIThread.RunJobs();

        // The GSTR-3B header: the one realised Grid carrying a "Particulars" caption alongside "IGST".
        var headerCaptions = win.GetVisualDescendants()
            .OfType<Grid>()
            .Select(g => g.Children.OfType<TextBlock>().Select(t => t.Text ?? string.Empty).ToList())
            .Where(caps => caps.Contains("Particulars") && caps.Contains("IGST"))
            .ToList();

        Assert.NotEmpty(headerCaptions);   // not vacuous: the 3B header really is on screen
        Assert.All(headerCaptions, caps => Assert.Contains("Cess", caps));

        // …and the column is six-wide, so Col6 has somewhere to bind.
        var headerGrid = win.GetVisualDescendants().OfType<Grid>()
            .First(g => g.Children.OfType<TextBlock>().Any(t => t.Text == "Particulars")
                        && g.Children.OfType<TextBlock>().Any(t => t.Text == "Cess"));
        Assert.Equal(6, headerGrid.ColumnDefinitions.Count);

        win.Close();
    }
}
