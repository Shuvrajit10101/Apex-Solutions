using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Services;
using Apex.Desktop.Services;
using Apex.Desktop.ViewModels;
using Apex.Desktop.Views;
using Xunit;
using DomainLedger = Apex.Ledger.Domain.Ledger;

namespace Apex.Desktop.Tests;

/// <summary>
/// 🔴 <b>CENSUS ROW 8.10 — e-PAYMENTS, AND THE INPUT IT CANNOT LIVE WITHOUT.</b>
///
/// <para><b>The defect these were written against, measured on this branch.</b>
/// <c>ledgers.bank_account_number</c> and <c>ledgers.bank_ifsc</c> have existed on EVERY ledger since schema v57,
/// and the only screen block that wrote them was nested inside the cheque-dimensions panel — visible solely for a
/// BANK group with cheque printing switched on. So <b>no operator could record a supplier's account number or
/// IFSC</b>, every e-payment would have sat permanently in the "Incomplete/Incorrect Transaction Details" bucket,
/// and the payment-instruction file would have been permanently empty. That is the "capability no user can reach"
/// shape this project has already filed three times, and it needed no schema change to fix: the columns were
/// there, the door was not.</para>
///
/// <para>So these tests walk the ledger master the way an operator does, look at the controls ACTUALLY REALISED
/// on screen for a party ledger, save, <b>reload from SQLite</b>, and only then ask the report whether the payment
/// is exportable.</para>
/// </summary>
public sealed class EPaymentsReachabilityTests : IDisposable
{
    private readonly string _tempDir;
    private readonly CompanyStorage _storage;

    public EPaymentsReachabilityTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "ApexEPay_" + Guid.NewGuid().ToString("N"));
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

    private static IEnumerable<Visual> Descendants(Visual v)
    {
        foreach (var c in v.GetVisualChildren())
        {
            yield return c;
            foreach (var g in Descendants(c)) yield return g;
        }
    }

    private static void Pump(MainWindow w)
    {
        w.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        w.UpdateLayout();
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

    private MainWindowViewModel NewCompany(string name)
    {
        var vm = new MainWindowViewModel(_storage);
        vm.NewCompanyName = name;
        vm.CreateCompany();
        return vm;
    }

    private Company Reload(string companyName) =>
        _storage.Load(_storage.ListCompanies().Single(e => e.Name == companyName));

    // ================================================================ the menu route

    /// <summary>Gateway → Banking → <b>e-Payments</b>, by arrow keys, opening the report — not a bespoke page, so
    /// it inherits F2 period, F4 bank scope, Ctrl+P, Alt+E export and Alt+K saved views.</summary>
    [Fact]
    public void E_payments_is_arrow_reachable_under_transactions_banking()
    {
        var vm = NewCompany("EPay Route Co");
        vm.ShowGateway();
        ArrowToAndDrill(vm, "Banking");
        Assert.Equal(GatewayMenu.Banking, vm.CurrentGatewayMenu);

        ArrowToAndDrill(vm, "e-Payments");
        Assert.Equal(Screen.Report, vm.CurrentScreen);
        Assert.Equal(ReportKind.EPayments, vm.Reports!.Kind);
        Assert.True(vm.Reports.IsEPayments);
        // F4 must be able to scope it to one remitting bank, exactly as the other banking reports are scoped.
        Assert.True(vm.Reports.ShowChequeBankPicker);
    }

    /// <summary>The two subscription rows are deliberately NOT in the menu. A row that leads to a screen this
    /// build cannot honour is how a census cell gets graded present when it is absent.</summary>
    [Fact]
    public void Connected_banking_and_payment_request_are_not_offered()
    {
        var vm = NewCompany("EPay Scope Co");
        vm.ShowGateway();
        ArrowToAndDrill(vm, "Banking");

        var labels = vm.Menu.Select(m => m.Label).ToList();
        Assert.DoesNotContain(labels, l => l.Contains("Connected Banking", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(labels, l => l.Contains("Payment Request", StringComparison.OrdinalIgnoreCase));
    }

    // ================================================================ the input door

    /// <summary>
    /// 🔴 <b>THE ONE THAT WOULD HAVE CAUGHT THE DEAD FEATURE.</b> A party ledger's Beneficiary Bank Details block
    /// is realised on screen, the operator types into it, saves, and the values come back <b>out of SQLite</b>.
    /// Without this door no supplier can ever be paid electronically.
    /// </summary>
    [AvaloniaFact]
    public void A_party_ledger_can_capture_beneficiary_bank_details_that_survive_a_reload()
    {
        var vm = new MainWindowViewModel(_storage);
        var window = new MainWindow { DataContext = vm, Width = 1280, Height = 900 };
        window.Show();
        try
        {
            vm.NewCompanyName = "EPay Master Co";
            vm.CreateCompany();

            vm.ShowLedgerMaster();
            var master = vm.LedgerMaster!;
            master.Name = "Acme Supplies";
            master.SelectedGroup = master.Groups.Single(g => g.Name == "Sundry Creditors");
            Pump(window);

            Assert.True(master.ShowBankIdentity,
                "A party ledger does not offer the Bank Identity block, so a supplier's account number and IFSC "
                + "can never be recorded and every e-payment is stuck as an exception for ever.");
            Assert.Equal("Beneficiary Bank Details (e-payment instructions)", master.BankIdentityHeading);

            // The heading and all three boxes are ACTUALLY on screen — not merely true on the view model.
            var texts = Descendants(window).OfType<TextBlock>()
                .Where(t => t.IsEffectivelyVisible)
                .Select(t => t.Text ?? string.Empty).ToList();
            Assert.Contains(texts, t => t.Contains("Beneficiary Bank Details", StringComparison.Ordinal));
            Assert.Contains(texts, t => t == "Account number");
            Assert.Contains(texts, t => t == "IFSC");

            master.BankAccountNumber = "9876543210";
            master.BankIfsc = "ICIC0000456";
            master.BankBranch = "Andheri";
            Assert.True(master.Create(), master.Message);

            var reloaded = Reload("EPay Master Co");
            var acme = reloaded.FindLedgerByName("Acme Supplies")!;
            Assert.Equal("9876543210", acme.BankAccountNumber);
            Assert.Equal("ICIC0000456", acme.BankIfsc);
            Assert.Equal("Andheri", acme.BankBranch);
        }
        finally { window.Close(); }
    }

    /// <summary>A ledger that is neither a bank nor a party has no bank identity to record, so the block stays
    /// off — a caption over a box nobody should fill is the dead-field defect in the other direction.</summary>
    [Fact]
    public void An_expense_ledger_is_not_offered_bank_identity()
    {
        var vm = NewCompany("EPay Gate Co");
        vm.ShowLedgerMaster();
        var master = vm.LedgerMaster!;
        master.SelectedGroup = master.Groups.Single(g => g.Name == "Indirect Expenses");
        Assert.False(master.ShowBankIdentity);
    }

    // ================================================================ end to end

    /// <summary>
    /// 🔴 The whole row, in the order an operator lives it: record both masters through the screens, post a NEFT
    /// payment, open the report from the menu, and press <b>Ctrl+A</b> — which writes a payment-instruction file
    /// carrying that beneficiary. The file is deleted afterwards; what is asserted is that it was written and
    /// what was in it.
    /// </summary>
    [Fact]
    public void The_report_moves_a_payment_to_ready_once_both_masters_are_complete_and_ctrl_a_writes_the_file()
    {
        var vm = NewCompany("EPay E2E Co");

        // Bank master, through the screen: a bank group with cheque printing on is what reveals its own block.
        vm.ShowLedgerMaster();
        var m = vm.LedgerMaster!;
        m.Name = "HDFC Bank";
        m.SelectedGroup = m.Groups.Single(g => g.Name == "Bank Accounts");
        m.EnableChequePrinting = true;
        Assert.True(m.ShowBankIdentity);
        m.BankAccountNumber = "50200012345678";
        m.BankIfsc = "HDFC0000123";
        Assert.True(m.Create(), m.Message);

        // Beneficiary master, through the same screen.
        vm.ShowLedgerMaster();
        m = vm.LedgerMaster!;
        m.Name = "Acme Supplies";
        m.SelectedGroup = m.Groups.Single(g => g.Name == "Sundry Creditors");
        m.BankAccountNumber = "9876543210";
        m.BankIfsc = "ICIC0000456";
        Assert.True(m.Create(), m.Message);

        var company = vm.Company!;
        var bank = company.FindLedgerByName("HDFC Bank")!;
        var acme = company.FindLedgerByName("Acme Supplies")!;
        var payDate = company.BooksBeginFrom.AddDays(20);

        new LedgerService(company).Post(new Voucher(
            Guid.NewGuid(), company.FindVoucherTypeByName("Payment")!.Id, payDate,
            new[]
            {
                new EntryLine(acme.Id, Money.FromRupees(125000m), DrCr.Debit),
                new EntryLine(bank.Id, Money.FromRupees(125000m), DrCr.Credit,
                    bankAllocation: new BankAllocation(BankTransactionType.NEFT, "NEFT-77")),
            },
            partyId: acme.Id));

        vm.ShowGateway();
        ArrowToAndDrill(vm, "Banking");
        ArrowToAndDrill(vm, "e-Payments");
        var report = vm.Reports!;
        report.SetPeriod(company.BooksBeginFrom, company.BooksBeginFrom.AddYears(1));

        Assert.Contains(report.Rows, r => r.Particulars.Contains("Ready for Sending to Bank", StringComparison.Ordinal));
        Assert.Contains(report.Rows, r => r.Particulars.Contains("Acme Supplies", StringComparison.Ordinal));

        string? path = null;
        try
        {
            path = report.ExportPaymentInstructions();
            Assert.True(path != null,
                "Ctrl+A wrote no payment instruction file. Status was: " + report.EPaymentsExportStatus);
            Assert.True(File.Exists(path!));

            var text = File.ReadAllText(path!);
            Assert.Contains("Acme Supplies", text);
            Assert.Contains("9876543210", text);
            Assert.Contains("ICIC0000456", text);
            Assert.Contains("125000.00", text);
            Assert.Contains("not a bank-specific format", text);
            Assert.Contains("Saved", report.EPaymentsExportStatus);
        }
        finally
        {
            if (path != null)
                try { File.Delete(path); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    /// <summary>
    /// With nothing ready, Ctrl+A writes NOTHING and says why. An instruction file with no rows uploaded to a
    /// bank portal is a support call, not an export.
    /// </summary>
    [Fact]
    public void Ctrl_a_refuses_to_write_a_file_when_nothing_is_ready()
    {
        var vm = NewCompany("EPay Empty Co");
        var company = vm.Company!;

        var bank = new DomainLedger(Guid.NewGuid(), "HDFC Bank",
            company.FindGroupByName("Bank Accounts")!.Id, Money.FromRupees(500000m), openingIsDebit: true);
        company.AddLedger(bank);                                    // no account number, no IFSC
        var acme = new DomainLedger(Guid.NewGuid(), "Acme Supplies",
            company.FindGroupByName("Sundry Creditors")!.Id, Money.Zero, openingIsDebit: false);
        company.AddLedger(acme);

        new LedgerService(company).Post(new Voucher(
            Guid.NewGuid(), company.FindVoucherTypeByName("Payment")!.Id,
            company.BooksBeginFrom.AddDays(10),
            new[]
            {
                new EntryLine(acme.Id, Money.FromRupees(1000m), DrCr.Debit),
                new EntryLine(bank.Id, Money.FromRupees(1000m), DrCr.Credit,
                    bankAllocation: new BankAllocation(BankTransactionType.NEFT, "X")),
            },
            partyId: acme.Id));

        vm.ShowGateway();
        ArrowToAndDrill(vm, "Banking");
        ArrowToAndDrill(vm, "e-Payments");
        var report = vm.Reports!;
        report.SetPeriod(company.BooksBeginFrom, company.BooksBeginFrom.AddYears(1));

        Assert.Null(report.ExportPaymentInstructions());
        Assert.Contains("Nothing is ready to send", report.EPaymentsExportStatus);
        // And the report told the operator which master to fix.
        Assert.Contains(report.Rows,
            r => r.Particulars.Contains("Incomplete/Incorrect Bank Ledger Master Details", StringComparison.Ordinal));
    }
}
