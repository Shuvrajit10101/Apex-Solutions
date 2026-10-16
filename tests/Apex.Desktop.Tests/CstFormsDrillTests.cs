using System;
using System.IO;
using System.Linq;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Reports;
using Apex.Ledger.Services;
using Apex.Desktop.Services;
using Apex.Desktop.ViewModels;
using Apex.Desktop.Views;
using Xunit;
using DomainLedger = Apex.Ledger.Domain.Ledger;

namespace Apex.Desktop.Tests;

/// <summary>
/// 🔴 <b>Wave 46 — the DRILL-WIDENING arm, census 11.15 (and the two kinds it moves, census 15.6).</b>
///
/// <para><b>The shape being closed.</b> <c>ReportsViewModel.Drill</c> has no <c>default</c> arm, so a
/// <see cref="ReportKind"/> with no <c>case</c> falls straight out of the switch and Enter does nothing — even
/// when the builder has already stamped the row's drill id. That split (id present, arm absent) was recorded
/// against the four Job Work registers and then against the Memorandum Register. A sweep of every dispatched
/// <c>ReportKind</c> against every builder that assigns a <c>Drill*</c> id found <b>exactly two</b> left with
/// it: <see cref="ReportKind.CstFormsReceivable"/> and <see cref="ReportKind.CstFormsIssuable"/>.
/// <c>AddCstFormsSection</c> stamps <c>DrillVoucherId = r.VoucherId</c> on every form row, <c>Alt+S</c> resolved
/// that id through the shell, and <c>CstFormRow</c>'s own shipped doc comment said <i>"The voucher, so Enter
/// drills to it"</i> — while Enter did nothing at all.</para>
///
/// <para><b>Vendor grounding (R7, ruling 14).</b> The gesture is attested generically for every report rather
/// than per report: <i>"Select a line &gt; press <b>Enter</b>."</i>, and the same page separates it from
/// <i>"Select a line &gt; press <b>Shift+Enter</b> on the selected line."</i>, which expands or collapses
/// details within the same report view
/// (<c>help.tallysolutions.com/working-with-reports/</c>, opened by content 2026-10-16). The two reports
/// themselves are the vendor's <i>Forms Receivable</i> / <i>Forms Issuable</i>
/// (<c>help.tallysolutions.com/tally-prime/reports/forms-receivables-tally/</c>).</para>
///
/// <para>🔴 <b>EVERY Enter HERE IS THE REAL KEY ON A SHOWN <see cref="MainWindow"/>.</b> Calling
/// <c>Reports.Drill(row)</c> directly would stay green on a tree where the window never routes Enter to the
/// drill at all — which is precisely the hole a shipped comment already claimed closed for the Job Work
/// registers while Enter did nothing.</para>
/// </summary>
public sealed class CstFormsDrillTests : IDisposable
{
    private readonly string _tempDir;
    private readonly CompanyStorage _storage;

    public CstFormsDrillTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "ApexCstDrill_" + Guid.NewGuid().ToString("N"));
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

    // ================================================================ the central test

    /// <summary>
    /// 🔴 <b>THE CENTRAL TEST.</b> On BOTH declaration-forms reports, the real Enter on the highlighted row must
    /// open the voucher that row NAMES.
    ///
    /// <para><b>Mutation:</b> delete the two new <c>case</c> labels from <c>ReportsViewModel.Drill</c> and this
    /// reddens on <c>Assert.Equal(Screen.VoucherDetail, vm.CurrentScreen)</c> — the keystroke leaves the report
    /// where it is and <c>vm.VoucherDetail</c> stays null.</para>
    /// </summary>
    [AvaloniaTheory]
    [InlineData(ReportKind.CstFormsReceivable)]
    [InlineData(ReportKind.CstFormsIssuable)]
    public void Enter_on_a_declaration_forms_row_opens_the_voucher_that_row_names(ReportKind kind)
    {
        var vm = VatCompany(out var company, out var sale, out var purchase);
        var expected = kind == ReportKind.CstFormsReceivable ? sale : purchase;

        var window = new MainWindow { DataContext = vm, Width = 1440, Height = 900 };
        window.Show();
        Pump(window);

        vm.OpenReport(kind);
        Pump(window);
        WidenPeriod(vm.Reports!, company.BooksBeginFrom);
        Pump(window);
        Assert.Equal(Screen.Report, vm.CurrentScreen);

        // 🔴 The id must be on the ROW, not merely on the engine record: a report whose rows all carry
        // Guid.Empty cannot address anything, whatever the Drill switch says.
        var row = Assert.Single(vm.Reports!.Rows.Where(r => r.DrillVoucherId != Guid.Empty));
        Assert.Equal(expected.Id, row.DrillVoucherId);
        Assert.True(row.IsDrillable);

        vm.Reports!.SelectedRow = row;
        Pump(window);
        var before = vm.Columns.Count;

        window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);   // the real drill key
        Pump(window);

        Assert.Equal(Screen.VoucherDetail, vm.CurrentScreen);
        Assert.NotNull(vm.VoucherDetail);
        Assert.Equal(expected.Id, vm.VoucherDetail!.VoucherId);
        Assert.Equal(before + 1, vm.Columns.Count);                          // added, not replaced

        window.Close();
    }

    /// <summary>
    /// Enter on a row that carries no drill key — the section HEADING and the footing TOTAL — must stay a safe
    /// no-op. Both new arms resolve on <c>DrillVoucherId != Guid.Empty</c>; an arm that fired anyway would ask
    /// the shell to open "the voucher <see cref="Guid.Empty"/>".
    /// </summary>
    [AvaloniaFact]
    public void Enter_on_a_declaration_forms_heading_or_total_is_a_safe_no_op()
    {
        var vm = VatCompany(out var company, out _, out _);

        var window = new MainWindow { DataContext = vm, Width = 1440, Height = 900 };
        window.Show();
        Pump(window);

        vm.OpenReport(ReportKind.CstFormsReceivable);
        Pump(window);
        WidenPeriod(vm.Reports!, company.BooksBeginFrom);
        Pump(window);

        foreach (var dead in new[]
                 {
                     vm.Reports!.Rows.First(r => r.IsHeader),
                     vm.Reports!.Rows.First(r => r.IsTotal),
                 })
        {
            Assert.Equal(Guid.Empty, dead.DrillVoucherId);
            Assert.False(dead.IsDrillable);

            vm.Reports!.SelectedRow = dead;
            Pump(window);
            var before = vm.Columns.Count;

            window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
            Pump(window);

            Assert.Equal(Screen.Report, vm.CurrentScreen);
            Assert.Equal(before, vm.Columns.Count);
            Assert.Null(vm.VoucherDetail);
        }

        window.Close();
    }

    /// <summary>
    /// 🔴 <b>The drill must not have been bought by turning a navigation keystroke into a MASTER EDIT.</b>
    /// <c>Alt+S</c> (Set/Alter Form No.) already resolves the same <c>DrillVoucherId</c>, so the cheap wrong fix
    /// here was to route Enter into that editor. Enter opens the VOUCHER and leaves the voucher's CST form
    /// fields exactly as they were, read back off the RELOADED book.
    /// </summary>
    [AvaloniaFact]
    public void Drilling_a_declaration_forms_row_records_no_form_on_the_voucher()
    {
        var vm = VatCompany(out var company, out var sale, out _);
        var companyName = company.Name;

        var window = new MainWindow { DataContext = vm, Width = 1440, Height = 900 };
        window.Show();
        Pump(window);

        vm.OpenReport(ReportKind.CstFormsReceivable);
        Pump(window);
        WidenPeriod(vm.Reports!, company.BooksBeginFrom);
        Pump(window);

        vm.Reports!.SelectedRow = vm.Reports!.Rows.First(r => r.DrillVoucherId != Guid.Empty);
        Pump(window);
        window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        Pump(window);
        Assert.Equal(Screen.VoucherDetail, vm.CurrentScreen);

        // Off the RELOADED book, not off the in-memory aggregate the screen is holding.
        var reloaded = _storage.Load(_storage.ListCompanies().Single(e => e.Name == companyName));
        var readBack = reloaded.FindVoucher(sale.Id)!;
        Assert.Null(readBack.CstFormType);
        Assert.Null(readBack.CstFormNumber);
        Assert.Null(readBack.CstFormSeriesNumber);
        Assert.Null(readBack.CstFormDate);

        // …and the drill changed no figure either: the report's own exposure is still nil.
        var after = CstDeclarationFormsReport.Build(
            reloaded, CstFormsSide.Receivable, reloaded.BooksBeginFrom, reloaded.BooksBeginFrom.AddYears(1));
        Assert.Equal(Money.Zero, after.PendingValue);

        window.Close();
    }

    // ---------------------------------------------------------------- scaffolding

    private static void Pump(MainWindow w)
    {
        w.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        w.UpdateLayout();
    }

    /// <summary>Puts the report's window around a date, so the fixture vouchers are inside it whatever the
    /// company's financial year happens to be.</summary>
    private static void WidenPeriod(ReportsViewModel reports, DateOnly around) =>
        reports.SetPeriod(new DateOnly(around.Year - 1, 4, 1), new DateOnly(around.Year + 1, 3, 31));

    /// <summary>
    /// A VAT-enabled company carrying exactly ONE Sales voucher and ONE Purchase voucher, so each side of the
    /// Declaration Forms report has exactly one drillable row and <c>Assert.Single</c> is meaningful.
    /// </summary>
    private MainWindowViewModel VatCompany(out Company company, out Voucher sale, out Voucher purchase)
    {
        var vm = new MainWindowViewModel(_storage);
        vm.NewCompanyName = "Cst Drill Co " + Guid.NewGuid().ToString("N")[..6];
        vm.CreateCompany();
        Assert.Equal(Screen.Gateway, vm.CurrentScreen);

        var c = vm.Company!;
        new VatService(c).EnableVat(tin: "29123456789");

        var customer = new DomainLedger(
            Guid.NewGuid(), "Highway Fuels", c.FindGroupByName("Sundry Debtors")!.Id,
            Money.Zero, openingIsDebit: true)
        { PartyCstNumber = "CST/07/1234" };
        c.AddLedger(customer);

        var supplier = new DomainLedger(
            Guid.NewGuid(), "Refinery Supplies", c.FindGroupByName("Sundry Creditors")!.Id,
            Money.Zero, openingIsDebit: false)
        { PartyCstNumber = "CST/27/9876" };
        c.AddLedger(supplier);

        var salesLedger = new DomainLedger(
            Guid.NewGuid(), "Fuel Sales", c.FindGroupByName("Sales Accounts")!.Id,
            Money.Zero, openingIsDebit: false);
        c.AddLedger(salesLedger);

        var purchaseLedger = new DomainLedger(
            Guid.NewGuid(), "Fuel Purchases", c.FindGroupByName("Purchase Accounts")!.Id,
            Money.Zero, openingIsDebit: true);
        c.AddLedger(purchaseLedger);

        var svc = new LedgerService(c);
        var day = c.BooksBeginFrom.AddDays(5);

        sale = svc.Post(new Voucher(
            Guid.NewGuid(), c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Sales).Id, day,
            new[]
            {
                new EntryLine(customer.Id, Money.FromRupees(61_000m), DrCr.Debit),
                new EntryLine(salesLedger.Id, Money.FromRupees(61_000m), DrCr.Credit),
            },
            partyId: customer.Id));

        purchase = svc.Post(new Voucher(
            Guid.NewGuid(), c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Purchase).Id, day.AddDays(1),
            new[]
            {
                new EntryLine(purchaseLedger.Id, Money.FromRupees(43_500m), DrCr.Debit),
                new EntryLine(supplier.Id, Money.FromRupees(43_500m), DrCr.Credit),
            },
            partyId: supplier.Id));

        _storage.Save(c);
        company = c;
        return vm;
    }
}
