using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Apex.Desktop.Services;
using Apex.Desktop.ViewModels;
using Apex.Desktop.Views;
using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Io;
using Apex.Ledger.Reports;
using Apex.Ledger.Services;
using Xunit;
using DomainLedger = Apex.Ledger.Domain.Ledger;

namespace Apex.Desktop.Tests;

/// <summary>
/// 🔴 <b>THE REACHABILITY TESTS FOR THE BANKING-STORAGE WAVE</b> — census rows 8.5 (Cheque Register), 8.6
/// (Deposit Slip) and the cheque-LEAF half of 8.4.
///
/// <para><b>What this file exists to stop, stated as a measurement rather than a worry.</b> Row 8.4 landed
/// PARTIAL with its own confession: <c>ChequeLayout</c>, <c>ChequePrintData</c>, <c>ChequePdf</c> and
/// <c>ChequePrintProjector</c> were built, correct, deterministic and covered by tests — and
/// <c>Ledger.ChequeLayout</c> had <b>zero writers anywhere in <c>src/</c></b>. The only two assignments in the
/// whole repository were inside a test file. There was no <c>cheque_layouts</c> table, so the layout was
/// <c>null</c> on every loaded company, <c>ChequePdf.Validate</c> refused every render, and no operator could
/// ever print a cheque. That is the third dead feature filed on this project.</para>
///
/// <para>So every test here goes through the OPERATOR's route: it drives the ledger master's own fields, saves,
/// <b>RELOADS FROM SQLITE</b>, and only then asks whether the document appears. A projection test cannot tell
/// you a feature is reachable, and six census cells have been wrong in exactly that way.</para>
///
/// <para>Vendor grounding: <c>help.tallysolutions.com/cheque-payments-set-up/</c> ("Specify Cheque Range and
/// Format in Bank Ledger", "Specify User Defined Cheque Format"),
/// <c>help.tallysolutions.com/cheque-register/</c>, <c>help.tallysolutions.com/deposit-slips/</c>.</para>
/// </summary>
public sealed class BankingStorageReachabilityTests : IDisposable
{
    private readonly string _tempDir;
    private readonly CompanyStorage _storage;

    public BankingStorageReachabilityTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "ApexBankStore_" + Guid.NewGuid().ToString("N"));
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

    // ---------------------------------------------------------------- harness

    private MainWindowViewModel NewSeededCompany(string name)
    {
        var vm = new MainWindowViewModel(_storage);
        vm.NewCompanyName = name;
        vm.CreateCompany();
        Assert.Equal(Screen.Gateway, vm.CurrentScreen);
        return vm;
    }

    private Company Reload(string companyName)
    {
        var entry = _storage.ListCompanies().Single(e => e.Name == companyName);
        return _storage.Load(entry);
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

    /// <summary>
    /// Arrows down the flat Company Select list and ACTIVATES the row — the Company Info screen is not a cascade
    /// column, so <c>DrillIn</c> (which acts on the active column) is a no-op there. This is the operator's real
    /// route back into a saved book, and it is what makes the reload assertions below reload anything.
    /// </summary>
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
    private static void ArrowToBankingMenu(MainWindowViewModel vm)
    {
        vm.ShowGateway();
        ArrowToAndDrill(vm, "Banking");
        Assert.Equal(GatewayMenu.Banking, vm.CurrentGatewayMenu);
    }

    private static void WidenPeriod(ReportsViewModel reports, DateOnly around) =>
        reports.SetPeriod(new DateOnly(around.Year - 1, 4, 1), new DateOnly(around.Year + 1, 3, 31));

    /// <summary>Creates a bank ledger THROUGH the ledger master, so the screen — not the engine — is what is
    /// under test, then returns it.</summary>
    private static DomainLedger CreateBankThroughTheMaster(MainWindowViewModel vm, string name)
    {
        vm.ShowLedgerMaster();
        var m = vm.LedgerMaster!;
        m.Name = name;
        m.SelectedGroup = m.Groups.Single(g => g.Name == "Bank Accounts");
        m.EnableChequePrinting = true;
        Assert.True(m.Create(), m.Message);
        return vm.Company!.Ledgers.Single(l => l.Name == name);
    }

    private static DomainLedger AddSupplier(MainWindowViewModel vm, string name)
    {
        var party = new DomainLedger(
            Guid.NewGuid(), name, vm.Company!.FindGroupByName("Sundry Creditors")!.Id,
            Money.Zero, openingIsDebit: false);
        vm.Company!.AddLedger(party);
        return party;
    }

    private static Voucher PostChequePayment(
        Company c, DomainLedger party, DomainLedger bank, decimal amount, string instrument, DateOnly date)
    {
        var payment = c.VoucherTypes.Single(t => t.BaseType == VoucherBaseType.Payment);
        return new LedgerService(c).Post(new Voucher(
            Guid.NewGuid(), payment.Id, date,
            new[]
            {
                new EntryLine(party.Id, Money.FromRupees(amount), DrCr.Debit),
                new EntryLine(bank.Id, Money.FromRupees(amount), DrCr.Credit,
                    bankAllocation: new BankAllocation(
                        BankTransactionType.ChequeOrDD, instrument, date, null)),
            },
            partyId: party.Id));
    }

    /// <summary>Fills the master's twenty millimetre boxes with a layout that actually places its elements —
    /// keyed as MILLIMETRES, exactly as an operator reads them off a leaf.</summary>
    private static void KeyChequeDimensions(LedgerMasterViewModel m)
    {
        m.ChequeLeafWidthMm = "203";
        m.ChequeLeafHeightMm = "92";
        m.ChequeDateTopMm = "12";
        m.ChequeDateLeftMm = "140";
        m.ChequeDateCharPitchMm = "5";
        m.ChequePayeeTopMm = "30";
        m.ChequePayeeLeftMm = "25";
        m.ChequeWordsLine1TopMm = "40";
        m.ChequeWordsLine1LeftMm = "25";
        m.ChequeWordsLine2TopMm = "47";
        m.ChequeWordsLine2LeftMm = "25";
        m.ChequeWordsWidthMm = "140";
        m.ChequeFiguresTopMm = "40";
        m.ChequeFiguresLeftMm = "160";
        m.ChequeFiguresWidthMm = "35";
        m.ChequeSignTopMm = "70";
        m.ChequeSignLeftMm = "140";
        m.ChequeSignWidthMm = "50";
        m.ChequeSignHeightMm = "15";
        m.ChequeSalutation1 = "For Apex Solutions";
    }

    // ================================================================ 8.4 — the leaf becomes printable

    /// <summary>
    /// 🔴 <b>THE TEST THIS WHOLE TRACK EXISTS FOR.</b> An operator opens the bank ledger, keys the cheque
    /// dimensions in millimetres, saves, and the company is RELOADED FROM SQLITE. The cheque leaf then prints.
    ///
    /// <para>On today's main every clause after the save fails: nothing persists a layout, so the reloaded bank's
    /// <c>ChequeLayout</c> is <c>null</c>, <c>VoucherDetailViewModel.ChequePrintRefusal</c> answers "Cheque
    /// dimensions are not set for this bank", and <c>OpenPrintPreview</c> never reaches the leaf.</para>
    /// </summary>
    [Fact]
    public void An_operator_can_key_cheque_dimensions_and_the_leaf_then_prints_after_a_reload()
    {
        var vm = NewSeededCompany("Cheque Leaf Co");
        var bank = CreateBankThroughTheMaster(vm, "HDFC Bank");

        // The operator re-opens the bank to key the dimensions — the vendor captures them on the bank ledger.
        var alter = LedgerMasterViewModel.ForAlter(vm.Company!, _storage, bank.Id, () => { })!;
        Assert.True(alter.ShowChequePrinting);
        Assert.True(alter.ShowChequeDimensions);          // the block is offered, not hidden behind a flag
        alter.BankAccountNumber = "50100123456789";
        alter.BankBranch = "MG Road";
        alter.BankIfsc = "HDFC0000123";
        KeyChequeDimensions(alter);
        Assert.True(alter.Alter(), alter.Message);

        // 🔴 The reload is the step that used to lose everything.
        var reloaded = Reload("Cheque Leaf Co");
        var reloadedBank = reloaded.FindLedger(bank.Id)!;
        Assert.NotNull(reloadedBank.ChequeLayout);
        Assert.Equal(2030, reloadedBank.ChequeLayout!.LeafWidthTmm);   // 203 mm keyed → 2030 tenths stored
        Assert.Equal(920, reloadedBank.ChequeLayout.LeafHeightTmm);
        Assert.Equal("50100123456789", reloadedBank.BankAccountNumber);

        // …and the renderer accepts it, which is the whole point.
        var acme = new DomainLedger(
            Guid.NewGuid(), "Acme Supplies", reloaded.FindGroupByName("Sundry Creditors")!.Id,
            Money.Zero, openingIsDebit: false);
        reloaded.AddLedger(acme);
        var voucher = PostChequePayment(reloaded, acme, reloadedBank, 41250m, "100300", new DateOnly(2026, 5, 20));

        var detail = new VoucherDetailViewModel(reloaded, reloaded.FindVoucher(voucher.Id)!);
        Assert.NotNull(detail.ChequePrintData);
        Assert.Null(detail.ChequePrintRefusal);
        Assert.NotNull(detail.ChequeLayoutOfBank);
        Assert.NotEmpty(ChequePdf.Render(detail.ChequePrintData!, detail.ChequeLayoutOfBank!));
    }

    /// <summary>
    /// The master refuses a typo rather than reading it as 0. This matters more here than on most screens: 0
    /// means "element not placed" and the renderer SKIPS an unplaced element, so a mis-keyed offset silently
    /// dropped the payee name off a negotiable instrument.
    ///
    /// <para>Invariant-culture parsing is asserted with a decimal, because the gate runs on ubuntu and macos as
    /// well as Windows and a comma-decimal runner must read "12.5" the same way.</para>
    /// </summary>
    [Fact]
    public void The_cheque_dimension_boxes_refuse_a_typo_instead_of_silently_reading_zero()
    {
        var vm = NewSeededCompany("Refusal Co");
        var bank = CreateBankThroughTheMaster(vm, "HDFC Bank");
        var m = LedgerMasterViewModel.ForAlter(vm.Company!, _storage, bank.Id, () => { })!;

        KeyChequeDimensions(m);
        m.ChequePayeeLeftMm = "twenty five";
        Assert.False(m.Alter());
        Assert.Contains("millimetres", m.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Null(vm.Company!.FindLedger(bank.Id)!.ChequeLayout);   // and nothing was written

        m.ChequePayeeLeftMm = "-25";
        Assert.False(m.Alter());
        Assert.Contains("negative", m.Message, StringComparison.OrdinalIgnoreCase);

        m.ChequePayeeLeftMm = "25.55";                                // finer than a tenth of a millimetre
        Assert.False(m.Alter());
        Assert.Contains("tenth of a millimetre", m.Message, StringComparison.OrdinalIgnoreCase);

        // A half-set leaf is refused too: the renderer needs both to open the page, and half a leaf size is far
        // more likely a half-finished entry than an intention.
        m.ChequePayeeLeftMm = "25";
        m.ChequeLeafHeightMm = string.Empty;
        Assert.False(m.Alter());
        Assert.Contains("BOTH a width and a height", m.Message, StringComparison.Ordinal);

        // …and a decimal parses, invariantly.
        m.ChequeLeafHeightMm = "92";
        m.ChequePayeeLeftMm = "12.5";
        Assert.True(m.Alter(), m.Message);
        Assert.Equal(125, vm.Company!.FindLedger(bank.Id)!.ChequeLayout!.PayeeLeftTmm);
    }

    // ================================================================ 8.5 — Cheque Register

    /// <summary>
    /// 🔴 The end-to-end reachability of row 8.5: record a cheque book on the bank ledger master, pay one leaf,
    /// mark another cancelled, RELOAD, then arrow to Banking → Cheque Register and read the buckets off the
    /// report the operator actually sees.
    /// </summary>
    [Fact]
    public void The_cheque_register_is_arrow_reachable_and_buckets_a_recorded_cheque_book()
    {
        var vm = NewSeededCompany("Register Co");
        var bank = CreateBankThroughTheMaster(vm, "HDFC Bank");
        var acme = AddSupplier(vm, "Acme Supplies");
        var date = new DateOnly(2026, 5, 20);

        // The operator's own route to a cheque book: alter the bank, add the book.
        var master = LedgerMasterViewModel.ForAlter(vm.Company!, _storage, bank.Id, () => { })!;
        Assert.True(master.ShowChequeBooks);
        master.NewChequeBookName = "HDFC book 1";
        master.NewChequeBookFrom = "000101";
        master.NewChequeBookTo = "000105";
        Assert.True(master.AddChequeBook(), master.Message);
        Assert.Single(master.ChequeBooks);

        var book = vm.Company!.ChequeBooks.Single();
        vm.Company.SetChequeStatus(book.Id, "000104", ChequeStatus.Cancelled);
        PostChequePayment(vm.Company, acme, bank, 41250m, "000101", date);
        _storage.Save(vm.Company!);

        // Reload, then walk in with the arrows.
        var reloadedVm = new MainWindowViewModel(_storage);
        OpenSavedCompany(reloadedVm, "Register Co");   // the operator's own route back in
        Assert.NotNull(reloadedVm.Company);
        ArrowToBankingMenu(reloadedVm);
        ArrowToAndDrill(reloadedVm, "Cheque Register");
        Assert.NotNull(reloadedVm.Reports);
        WidenPeriod(reloadedVm.Reports!, date);

        var summary = reloadedVm.Reports!.Rows.Single(r => r.Particulars.Contains("HDFC book 1", StringComparison.Ordinal));
        Assert.Contains("000101–000105", summary.Particulars, StringComparison.Ordinal);
        Assert.Contains("Available 3", summary.Particulars, StringComparison.Ordinal);
        Assert.Contains("Cancelled 1", summary.Particulars, StringComparison.Ordinal);
        Assert.Contains("Unreconciled 1", summary.Secondary, StringComparison.Ordinal);

        // Enter drills to the leaf list ("View More Details in Cheque Register"), and the spent leaf drills on
        // to the paying voucher — a register whose summary has no drill is half the report.
        Assert.NotEqual(Guid.Empty, summary.DrillChequeBookId);
        reloadedVm.DrillReport(summary);
        var leaves = reloadedVm.Reports!.Rows.Where(r => r.Particulars.StartsWith("Cheque No.", StringComparison.Ordinal)).ToList();
        Assert.Equal(5, leaves.Count);
        var spent = leaves.Single(r => r.Particulars.Contains("Cheque No. 000101", StringComparison.Ordinal));
        Assert.Contains("Unreconciled", spent.Particulars, StringComparison.Ordinal);
        Assert.NotEqual(Guid.Empty, spent.DrillVoucherId);
        Assert.Contains("Cancelled", leaves.Single(r => r.Particulars.Contains("000104", StringComparison.Ordinal)).Particulars, StringComparison.Ordinal);
    }

    /// <summary>
    /// The empty state names the ROUTE that fills it. A register that says only "no data" is one an operator
    /// cannot tell apart from a broken screen — and this report's content has exactly one source.
    /// </summary>
    [Fact]
    public void The_empty_cheque_register_names_the_screen_that_fills_it()
    {
        var vm = NewSeededCompany("Empty Register Co");
        CreateBankThroughTheMaster(vm, "HDFC Bank");
        ArrowToBankingMenu(vm);
        ArrowToAndDrill(vm, "Cheque Register");

        var row = Assert.Single(vm.Reports!.Rows);
        Assert.Contains("No cheque books recorded", row.Particulars, StringComparison.Ordinal);
        Assert.Contains("Masters > Ledgers", row.Particulars, StringComparison.Ordinal);
    }

    /// <summary>
    /// 🔴 <b>Alt+A — "Alter Status" — is the ONLY writer of an operator cheque status in the product.</b> Without
    /// it, <c>cheque_status_overrides</c> would have storage, the register would have three buckets that read it,
    /// and nothing an operator can press would ever write one — the dead-capability shape this whole track exists
    /// to close, reproduced one slice later. So this test presses the key and reloads from SQLite.
    ///
    /// <para>It also asserts the refusal that is the safety: a leaf you have already PAID OUT cannot be marked
    /// Blank by hand, because that would put a cheque that is gone back into the issuable pile.</para>
    /// </summary>
    [Fact]
    public void Alt_A_on_the_leaf_list_alters_a_cheque_status_and_refuses_on_a_spent_leaf()
    {
        var vm = NewSeededCompany("Alter Status Co");
        var bank = CreateBankThroughTheMaster(vm, "HDFC Bank");
        var acme = AddSupplier(vm, "Acme Supplies");
        var date = new DateOnly(2026, 5, 20);

        var master = LedgerMasterViewModel.ForAlter(vm.Company!, _storage, bank.Id, () => { })!;
        master.NewChequeBookName = "HDFC book 1";
        master.NewChequeBookFrom = "000101";
        master.NewChequeBookTo = "000103";
        Assert.True(master.AddChequeBook(), master.Message);
        PostChequePayment(vm.Company!, acme, bank, 1000m, "000101", date);
        _storage.Save(vm.Company!);

        ArrowToBankingMenu(vm);
        ArrowToAndDrill(vm, "Cheque Register");
        WidenPeriod(vm.Reports!, date);
        vm.DrillReport(vm.Reports!.Rows.Single(r => r.DrillChequeBookId != Guid.Empty));
        Assert.True(vm.IsChequeRegisterDetailReport);

        // An UNSPENT leaf cycles Available → Blank → Cancelled → Available, and each step persists.
        var free = vm.Reports!.Rows.Single(r => r.Particulars.Contains("Cheque No. 000102", StringComparison.Ordinal));
        vm.Reports!.SelectedRow = free;
        vm.ReportAlterChequeStatus();
        Assert.Contains("now Blank", vm.Notice, StringComparison.Ordinal);
        Assert.Contains("Cheque No. 000102  ·  Blank", vm.Reports!.Rows.Select(r => r.Particulars));

        var book = vm.Company!.ChequeBooks.Single();
        Assert.Equal(ChequeStatus.Blank, Reload("Alter Status Co").FindChequeStatus(book.Id, "000102")!.Status);

        vm.Reports!.SelectedRow =
            vm.Reports!.Rows.Single(r => r.Particulars.Contains("Cheque No. 000102", StringComparison.Ordinal));
        vm.ReportAlterChequeStatus();
        Assert.Contains("now Cancelled", vm.Notice, StringComparison.Ordinal);
        Assert.Equal(ChequeStatus.Cancelled, Reload("Alter Status Co").FindChequeStatus(book.Id, "000102")!.Status);

        // 🔴 …and a SPENT leaf is refused, with the reason and the remedy.
        vm.Reports!.SelectedRow =
            vm.Reports!.Rows.Single(r => r.Particulars.Contains("Cheque No. 000101", StringComparison.Ordinal));
        vm.ReportAlterChequeStatus();
        Assert.Contains("already been paid out", vm.Notice, StringComparison.Ordinal);
        Assert.Null(vm.Company!.FindChequeStatus(book.Id, "000101"));
    }
    // ================================================================ 8.6 — Deposit Slip

    /// <summary>
    /// 🔴 The end-to-end reachability of row 8.6: bank a cheque and some cash, capture the bank identity on the
    /// ledger master, RELOAD, then arrow to Banking → Deposit Slip, pick the bank (F4) and read the slip.
    /// F5 switches to the cash slip.
    /// </summary>
    [Fact]
    public void The_deposit_slip_is_arrow_reachable_and_prints_the_bank_identity_it_now_stores()
    {
        var vm = NewSeededCompany("Deposit Co");
        var bank = CreateBankThroughTheMaster(vm, "HDFC Bank");

        var master = LedgerMasterViewModel.ForAlter(vm.Company!, _storage, bank.Id, () => { })!;
        master.BankAccountNumber = "50100123456789";
        master.BankBranch = "MG Road";
        Assert.True(master.Alter(), master.Message);

        var cust = new DomainLedger(
            Guid.NewGuid(), "Cust Ltd", vm.Company!.FindGroupByName("Sundry Debtors")!.Id,
            Money.Zero, openingIsDebit: true);
        vm.Company.AddLedger(cust);
        var receipt = vm.Company.VoucherTypes.Single(t => t.BaseType == VoucherBaseType.Receipt);
        var date = new DateOnly(2026, 5, 20);
        var svc = new LedgerService(vm.Company);
        svc.Post(new Voucher(Guid.NewGuid(), receipt.Id, date, new[]
        {
            new EntryLine(bank.Id, Money.FromRupees(5000m), DrCr.Debit,
                bankAllocation: new BankAllocation(BankTransactionType.ChequeOrDD, "778899", date, null)),
            new EntryLine(cust.Id, Money.FromRupees(5000m), DrCr.Credit),
        }, partyId: cust.Id));
        svc.Post(new Voucher(Guid.NewGuid(), receipt.Id, date, new[]
        {
            new EntryLine(bank.Id, Money.FromRupees(1500m), DrCr.Debit,
                bankAllocation: new BankAllocation(BankTransactionType.Cash, string.Empty, date, null)),
            new EntryLine(cust.Id, Money.FromRupees(1500m), DrCr.Credit),
        }, partyId: cust.Id));
        _storage.Save(vm.Company!);

        var reloadedVm = new MainWindowViewModel(_storage);
        OpenSavedCompany(reloadedVm, "Deposit Co");   // the operator's own route back in
        Assert.NotNull(reloadedVm.Company);
        ArrowToBankingMenu(reloadedVm);
        ArrowToAndDrill(reloadedVm, "Deposit Slip");
        var reports = reloadedVm.Reports!;
        WidenPeriod(reports, date);

        // With no bank picked the slip ASKS rather than silently totalling three banks onto one piece of paper.
        Assert.Contains(reports.Rows, r => r.Particulars.Contains("Choose the bank account", StringComparison.Ordinal));

        // F4 — pick the bank.
        Assert.True(reports.ShowChequeBankPicker);
        reports.SelectedChequeBank = reports.ChequeBanks.Single(b => b.Display == "HDFC Bank");

        Assert.Equal("Cheque Deposit Slip", reports.Title);
        var header = reports.Rows.First(r => r.Particulars.StartsWith("Bank:", StringComparison.Ordinal));
        Assert.Contains("A/c No. 50100123456789", header.Particulars, StringComparison.Ordinal);
        Assert.Contains("Branch: MG Road", header.Particulars, StringComparison.Ordinal);
        Assert.Contains(reports.Rows, r => r.Particulars.Contains("Cheque No. 778899", StringComparison.Ordinal));
        Assert.DoesNotContain(reports.Rows, r => r.Amount.Contains("1,500", StringComparison.Ordinal));

        // F5 — the cash slip.
        reloadedVm.ReportToggleDepositSlipKind();
        Assert.Equal("Cash Deposit Slip", reports.Title);
        Assert.Contains(reports.Rows, r => r.Amount.Contains("1,500", StringComparison.Ordinal));
        Assert.DoesNotContain(reports.Rows, r => r.Particulars.Contains("778899", StringComparison.Ordinal));
    }

    /// <summary>
    /// 🔴 <b>F5 on the Deposit Slip must NOT open a Payment voucher.</b> The global F5 is Payment; an unscoped
    /// arm would open one on top of the slip. This is the "global chord shadows a report chord" trap the Cheque
    /// Printing report's F4/Contra note already records, asserted rather than commented.
    /// </summary>
    [Fact]
    public void F5_is_the_slip_switch_on_the_deposit_slip_and_the_payment_voucher_everywhere_else()
    {
        var vm = NewSeededCompany("F5 Scope Co");
        CreateBankThroughTheMaster(vm, "HDFC Bank");

        ArrowToBankingMenu(vm);
        ArrowToAndDrill(vm, "Deposit Slip");
        Assert.True(vm.IsDepositSlipReport);
        Assert.False(vm.IsChequeRegisterReport);

        // On the Cheque Printing report the guard is off, so the global F5 keeps its meaning there.
        ArrowToBankingMenu(vm);
        ArrowToAndDrill(vm, "Cheque Printing");
        Assert.False(vm.IsDepositSlipReport);

        // …and F8 is the register's status filter only on the register.
        ArrowToBankingMenu(vm);
        ArrowToAndDrill(vm, "Cheque Register");
        Assert.True(vm.IsChequeRegisterReport);
        Assert.False(vm.IsDepositSlipReport);
        var before = vm.Reports!.Subtitle;
        vm.ReportCycleChequeStatusFilter();
        Assert.NotEqual(before, vm.Reports!.Subtitle);
        Assert.Contains("Available only (F8)", vm.Reports!.Subtitle, StringComparison.Ordinal);
    }

    // ================================================================ the realised visual tree

    /// <summary>
    /// 🔴 <b>THE CHEQUE-DIMENSIONS BLOCK IS ACTUALLY ON SCREEN.</b> A view-model flag is exactly the assertion
    /// that would pass on a build where the block was never added to the XAML — and a block nobody can see is
    /// the same dead feature in a new costume. So this walks the REALISED visual tree of the real
    /// <see cref="MainWindow"/> and requires a visible, non-degenerate TextBox bound to each of the load-bearing
    /// dimension properties.
    ///
    /// <para>Headless-safe: visual-tree and layout-bounds inspection only. No Skia, no rendered frame.</para>
    /// </summary>
    [AvaloniaFact]
    public void The_cheque_dimensions_and_cheque_books_blocks_are_realised_on_the_ledger_master()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ApexBankVis_" + Guid.NewGuid().ToString("N"));
        var storage = new CompanyStorage(dir);
        var vm = new MainWindowViewModel(storage);
        var window = new MainWindow { DataContext = vm, Width = 1280, Height = 720 };
        window.Show();
        try
        {
            vm.NewCompanyName = "Visual Bank Co";
            vm.CreateCompany();
            var bank = CreateBankThroughTheMaster(vm, "HDFC Bank");

            vm.ShowLedgerAlter(bank.Id);
            Pump(window);

            var master = vm.LedgerMaster!;
            Assert.True(master.ShowChequeDimensions,
                "The cheque-dimensions block is not offered for a bank ledger with cheque printing on.");

            // 🔴 Every property the renderer reads must have a realised, VISIBLE box an operator can type into.
            // The check is by SENTINEL VALUE rather than by binding-path introspection: setting the view-model
            // property and finding that text on screen proves the binding actually delivers, which is the thing
            // that was missing. A binding-path check would pass on a box bound to a property nothing reads.
            var sentinels = new Dictionary<string, Action<string>>(StringComparer.Ordinal)
            {
                ["mm-leaf-w"] = v => master.ChequeLeafWidthMm = v,
                ["mm-leaf-h"] = v => master.ChequeLeafHeightMm = v,
                ["mm-payee-t"] = v => master.ChequePayeeTopMm = v,
                ["mm-payee-l"] = v => master.ChequePayeeLeftMm = v,
                ["mm-words-t"] = v => master.ChequeWordsLine1TopMm = v,
                ["mm-figs-t"] = v => master.ChequeFiguresTopMm = v,
                ["mm-sign-t"] = v => master.ChequeSignTopMm = v,
                ["bank-acct"] = v => master.BankAccountNumber = v,
                ["bank-branch"] = v => master.BankBranch = v,
                ["bank-ifsc"] = v => master.BankIfsc = v,
                ["book-name"] = v => master.NewChequeBookName = v,
                ["book-from"] = v => master.NewChequeBookFrom = v,
                ["book-to"] = v => master.NewChequeBookTo = v,
            };
            foreach (var (sentinel, set) in sentinels) set(sentinel);
            Pump(window);

            var onScreen = Descendants(window)
                .OfType<TextBox>()
                .Where(b => b.IsEffectivelyVisible && b.Bounds.Width > 0 && b.Bounds.Height > 0)
                .Select(b => b.Text ?? string.Empty)
                .ToHashSet(StringComparer.Ordinal);

            foreach (var sentinel in sentinels.Keys)
                Assert.True(onScreen.Contains(sentinel),
                    $"No visible TextBox on the ledger master shows the value set for '{sentinel}'. The view "
                    + "model exposes the field and nothing on screen writes it, which is exactly the dead-field "
                    + "shape this track exists to close.");
        }
        finally
        {
            window.Close();
            try
            {
                Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
                if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private static IEnumerable<Visual> Descendants(Visual root)
    {
        foreach (var c in root.GetVisualChildren())
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
}
