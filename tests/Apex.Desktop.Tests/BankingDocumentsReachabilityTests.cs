using System;
using System.IO;
using System.Linq;
using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Io;
using Apex.Ledger.Reports;
using Apex.Desktop.Services;
using Apex.Desktop.ViewModels;
using Xunit;
using DomainLedger = Apex.Ledger.Domain.Ledger;

namespace Apex.Desktop.Tests;

/// <summary>
/// 🔴 <b>THE REACHABILITY TESTS FOR THE WAVE-7 BANKING DOCUMENTS</b> (census rows 8.4 and 8.7).
///
/// <para><b>What these exist to stop.</b> This product has already filed two capabilities that were fully built,
/// fully tested and reachable by <i>nobody</i> — a report builder with zero <c>src/</c> callers and a
/// ~432-line print view model with zero references. A projection test cannot tell you a feature is reachable,
/// and six census cells have been wrong in exactly that way. So every test below walks the cascade with the
/// ARROW KEYS from the Gateway, activates the row the operator would activate, and then asserts the screen or
/// the print artefact that actually appears.</para>
///
/// <para><b>And the one that made these rows real.</b> <c>Ledger.EnableChequePrinting</c> is a schema-v5 column
/// with seventeen references across Domain / Io / Sqlite and, until this wave, <b>zero in
/// <c>src/Apex.Desktop</c></b>. No operator could switch it on, so the Cheque Printing report would have been
/// structurally empty for every real company — a menu row leading to a permanently blank page. The
/// ledger-master tests here drive the checkbox, save, RELOAD FROM SQLITE, and only then look at the report.</para>
///
/// <para>Vendor grounding: <c>help.tallysolutions.com/banking/</c> ("Banking Utilities in TallyPrime"),
/// <c>help.tallysolutions.com/print-cheques/</c> ("Cheque Printing Report"),
/// <c>help.tallysolutions.com/payment-advice/</c> and
/// <c>help.tallysolutions.com/cheque-payments-set-up/</c> ("Specify Cheque Range and Format in Bank Ledger").</para>
/// </summary>
public sealed class BankingDocumentsReachabilityTests : IDisposable
{
    private readonly string _tempDir;
    private readonly CompanyStorage _storage;

    public BankingDocumentsReachabilityTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "ApexBankDocs_" + Guid.NewGuid().ToString("N"));
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

    /// <summary>Arrows down the ACTIVE column until the highlighted row carries this label, then drills in —
    /// the exact sequence an operator's fingers perform. Fails loudly when the row is not arrow-reachable,
    /// which is the failure mode a "does the ViewModel have the method" test cannot see.</summary>
    private static void ArrowToAndDrill(MainWindowViewModel vm, string label)
    {
        for (var i = 0; i < vm.Menu.Count + 2; i++)
        {
            if (vm.Menu[vm.SelectedIndex].Label == label) { vm.DrillIn(); return; }
            vm.MoveDown();
        }
        Assert.Fail($"'{label}' was not reachable by arrow navigation in the active column.");
    }

    /// <summary>Gateway → Transactions → Banking, by keyboard.</summary>
    private static void ArrowToBankingMenu(MainWindowViewModel vm)
    {
        vm.ShowGateway();
        ArrowToAndDrill(vm, "Banking");
        Assert.Equal(GatewayMenu.Banking, vm.CurrentGatewayMenu);
    }

    private DomainLedger AddBank(MainWindowViewModel vm, string name, bool chequePrinting)
    {
        var bank = new DomainLedger(
            Guid.NewGuid(), name, vm.Company!.FindGroupByName("Bank Accounts")!.Id,
            Money.FromRupees(500000m), openingIsDebit: true)
        { EnableChequePrinting = chequePrinting };
        vm.Company!.AddLedger(bank);
        return bank;
    }

    private DomainLedger AddSupplier(MainWindowViewModel vm, string name)
    {
        var party = new DomainLedger(
            Guid.NewGuid(), name, vm.Company!.FindGroupByName("Sundry Creditors")!.Id,
            Money.Zero, openingIsDebit: false);
        vm.Company!.AddLedger(party);
        return party;
    }

    /// <summary>Posts a cheque payment to a supplier straight through the engine (the voucher-entry path is
    /// covered by <c>BankingViewModelTests</c>; what is under test here is the route IN, not re-entry).</summary>
    private static Voucher PostChequePayment(
        Company c, DomainLedger party, DomainLedger bank, decimal amount, string instrument, DateOnly date,
        DateOnly? bankDate = null)
    {
        var payment = c.VoucherTypes.Single(t => t.BaseType == VoucherBaseType.Payment);
        return new Apex.Ledger.Services.LedgerService(c).Post(new Voucher(
            Guid.NewGuid(), payment.Id, date,
            new[]
            {
                new EntryLine(party.Id, Money.FromRupees(amount), DrCr.Debit),
                new EntryLine(bank.Id, Money.FromRupees(amount), DrCr.Credit,
                    bankAllocation: new BankAllocation(
                        BankTransactionType.ChequeOrDD, instrument, date, bankDate)),
            },
            partyId: party.Id));
    }

    /// <summary>Puts the report's period around a date, so a fixture voucher is inside the window whatever the
    /// company's own financial year happens to be.</summary>
    private static void WidenPeriod(ReportsViewModel reports, DateOnly around) =>
        reports.SetPeriod(new DateOnly(around.Year - 1, 4, 1), new DateOnly(around.Year + 1, 3, 31));

    // ================================================================ the Banking column itself

    /// <summary>
    /// Census row 8.9 records the Banking column as carrying "exactly two rows". This turns that complaint into
    /// an assertion: three NAMED sections, six rows, nested — never a flat dump (the standing UI rule).
    /// </summary>
    [Fact]
    public void The_banking_column_nests_the_new_documents_under_named_section_headers()
    {
        var vm = NewSeededCompany("Banking Column Co");
        ArrowToBankingMenu(vm);

        var column = vm.Columns[^1];
        Assert.True(column.IsMenu);

        var headers = column.Items.Where(i => i.IsHeader).Select(i => i.Label).ToArray();
        // "Slips & Advices" since the Deposit Slip (census 8.6) landed with schema v57. It read "Advices" alone
        // before that, deliberately: a section captioned for a document the menu does not carry is how a census
        // cell gets graded present when it is absent. The caption follows the contents, in both directions.
        // ✅ "Payments" joined them in wave K2, when e-Payments (census 8.10) landed. It is its own section on
        // the same principle the note above states: an e-payment is neither a reconciliation nor a piece of
        // stationery, and filing it under one of those captions would misdescribe money about to leave the
        // account. The caption follows the contents, in both directions — this lock is EXTENDED, never relaxed.
        Assert.Equal(new[] { "Reconciliation", "Cheque Management", "Slips & Advices", "Payments" }, headers);

        var rows = column.Items.Where(i => i.IsSelectable).Select(i => i.Label).ToArray();
        Assert.Equal(
            new[]
            {
                "Bank Reconciliation", "Import Bank Statement",
                "Cheque Printing", "Cheque Register",
                "Deposit Slip", "Payment Advice (Suppliers)",
                "e-Payments",
            },
            rows);

        // Every row sits AFTER a header — the flat-dump guard. (A row before the first header would be an
        // orphan, which is exactly the shape the professional-hierarchy rule forbids.)
        var items = column.Items.ToList();
        var firstHeader = items.FindIndex(i => i.IsHeader);
        Assert.True(firstHeader >= 0);
        Assert.DoesNotContain(items.Take(firstHeader), i => i.IsSelectable);
    }

    // ================================================================ 8.4 Cheque Printing

    /// <summary>
    /// 🔴 <b>The reachability test for row 8.4.</b> Arrow to the row, activate it, and the Cheque Printing report
    /// is what appears — carrying the cheque, with the drill that makes the row printable. This is the test that
    /// would have caught the row being a dead field for nine phases.
    /// </summary>
    [Fact]
    public void Activating_cheque_printing_opens_the_report_and_it_lists_the_pending_cheque()
    {
        var vm = NewSeededCompany("Cheque Route Co");
        var bank = AddBank(vm, "HDFC Bank", chequePrinting: true);
        var acme = AddSupplier(vm, "Acme Supplies");
        var date = new DateOnly(2026, 5, 12);
        var posted = PostChequePayment(vm.Company!, acme, bank, 20000m, "100123", date);

        ArrowToBankingMenu(vm);
        ArrowToAndDrill(vm, "Cheque Printing");

        Assert.NotNull(vm.Reports);
        Assert.Equal(ReportKind.ChequePrinting, vm.Reports!.Kind);
        Assert.Equal("Cheque Printing", vm.Reports.Title);
        WidenPeriod(vm.Reports, date);

        var row = vm.Reports.Rows.Single(r => r.Particulars.Contains("Acme Supplies", StringComparison.Ordinal));
        Assert.Contains("HDFC Bank", row.Secondary);
        Assert.Contains("20,000", row.Amount);

        // 🔴 AND IT SURVIVES THE EGRESS. The cheque number used to sit only in `ReportRow.Secondary`, which
        // NEITHER projection carries — so the printed, PDF-exported, CSV/XLSX-exported and emailed Cheque
        // Printing report was a list of cheques with no cheque numbers on it, the one field that says which leaf
        // is being printed. Asserted on both projectors, because they are two independent egresses.
        var printed = ReportPrintProjector.Project(vm.Reports);
        var printedRow = printed.Rows.Single(r => r.Cells[0].Contains("Acme Supplies", StringComparison.Ordinal));
        Assert.Contains("100123", printedRow.Cells[0]);

        var exported = ReportTabularProjector.Project(vm.Reports);
        var exportedRow = exported.Rows.Single(
            r => r.Cells[0].TextValue.Contains("Acme Supplies", StringComparison.Ordinal));
        Assert.Contains("100123", exportedRow.Cells[0].TextValue);

        // The source of both: the cheque number is on the row's PRIMARY label, which is the cell the projections
        // carry. (`Secondary` keeps the bank and the instrument date — context, not identity.)
        Assert.Contains("Cheque No. 100123", row.Particulars);

        // The drill is what makes a listed cheque a PRINTABLE cheque rather than a line of text. Asserted as the
        // POSTED VOUCHER'S OWN id: `Assert.NotNull` on a Guid is a value type and can never fail, which is the
        // shape of assertion that lets a broken drill ship green (xUnit2002 flagged it as exactly that).
        Assert.NotEqual(Guid.Empty, row.DrillVoucherId);
        Assert.Equal(posted.Id, row.DrillVoucherId);
        Assert.True(row.IsDrillable);

        // It is a ReportKind, so it inherits Ctrl+P instead of being a bespoke page Screen that silently
        // switches print, export, period and saved views off at once (the defect that hollowed out 8.1/11.9-11).
        vm.OpenPrintPreview();
        Assert.Equal(Screen.PrintPreview, vm.CurrentScreen);
        Assert.NotNull(vm.PrintPreview);
    }

    /// <summary>
    /// A bank with cheque printing OFF contributes nothing, and the report says WHY rather than showing an empty
    /// page — an empty report that reads as "broken" is the defect an empty-state line exists to prevent.
    /// </summary>
    [Fact]
    public void With_no_cheque_printing_bank_the_report_explains_itself_instead_of_being_blank()
    {
        var vm = NewSeededCompany("Cheque Empty Co");
        var bank = AddBank(vm, "Axis Bank", chequePrinting: false);
        var acme = AddSupplier(vm, "Acme Supplies");
        PostChequePayment(vm.Company!, acme, bank, 9000m, "900001", new DateOnly(2026, 5, 12));

        ArrowToBankingMenu(vm);
        ArrowToAndDrill(vm, "Cheque Printing");
        WidenPeriod(vm.Reports!, new DateOnly(2026, 5, 12));

        var only = Assert.Single(vm.Reports!.Rows);
        Assert.Contains("No cheques pending for printing", only.Particulars);
        Assert.Contains("Enable Cheque Printing", only.Particulars);
    }

    // ================================================================ 8.7 supplier Payment Advice

    /// <summary>
    /// 🔴 <b>The reachability test for row 8.7</b>, and the one that pins the document. Ctrl+P on this report
    /// must yield the supplier's <b>letter</b> — not the grid that lists the letters. A report the operator
    /// cannot turn into the thing they post is not the vendor's Payment Advice.
    /// </summary>
    [Fact]
    public void Activating_payment_advice_opens_the_report_and_ctrl_p_yields_the_letter_not_the_grid()
    {
        var vm = NewSeededCompany("Advice Route Co");
        var bank = AddBank(vm, "HDFC Bank", chequePrinting: true);
        var acme = AddSupplier(vm, "Acme Supplies");
        var date = new DateOnly(2026, 5, 20);
        PostChequePayment(vm.Company!, acme, bank, 30000m, "100200", date, bankDate: date.AddDays(2));

        ArrowToBankingMenu(vm);
        ArrowToAndDrill(vm, "Payment Advice (Suppliers)");

        Assert.NotNull(vm.Reports);
        Assert.Equal(ReportKind.SupplierPaymentAdvice, vm.Reports!.Kind);
        Assert.True(vm.Reports.IsSupplierPaymentAdvice);
        WidenPeriod(vm.Reports, date);

        var row = vm.Reports.Rows.Single(r => r.Particulars.Contains("Acme Supplies", StringComparison.Ordinal));
        Assert.Contains("Cheque/DD", row.Secondary);
        Assert.Contains("No. 100200", row.Secondary);
        Assert.Contains("reconciled", row.Secondary);
        Assert.DoesNotContain("not reconciled", row.Secondary);

        vm.OpenPrintPreview();
        Assert.Equal(Screen.PrintPreview, vm.CurrentScreen);
        // 🔴 The LETTER, not the report grid.
        Assert.Equal(PrintPreviewViewModel.PrintKind.PaymentAdviceLetter, vm.PrintPreview!.Kind);
        var cells = vm.PrintPreview.Pages.SelectMany(p => p.Lines).SelectMany(l => l.Cells).ToList();
        Assert.Contains(cells, c => c.Contains("Acme Supplies", StringComparison.Ordinal));
    }

    /// <summary>
    /// 🔴 The supplier advice and the PAYROLL Payment Advice are two different documents on two different menus,
    /// which is the conflation census row 8.7 exists to record. Both routes must still work, and neither may
    /// resolve to the other's report kind.
    /// </summary>
    [Fact]
    public void The_payroll_payment_advice_keeps_its_own_route_and_its_own_report_kind()
    {
        var vm = NewSeededCompany("Two Advices Co");

        ArrowToBankingMenu(vm);
        ArrowToAndDrill(vm, "Payment Advice (Suppliers)");
        Assert.Equal(ReportKind.SupplierPaymentAdvice, vm.Reports!.Kind);

        // The payroll one is a different report kind entirely and is untouched by this wave.
        vm.OpenReport(ReportKind.PaymentAdvice);
        Assert.Equal(ReportKind.PaymentAdvice, vm.Reports!.Kind);
        Assert.False(vm.Reports.IsSupplierPaymentAdvice);
    }

    /// <summary>
    /// 🔴 <b>F8 = reconciled only</b> (<c>help.tallysolutions.com/payment-advice/</c> — the report shows whether
    /// each payment is "matched (reconciled) or not" and narrows to the matched ones).
    ///
    /// <para>The engine has carried a <c>reconciledOnly</c> parameter since it was written and <b>nothing could
    /// set it</b>: the report always called <c>Build(company, period)</c>. A parameter with no reachable caller is
    /// the same defect as a report with no menu row, one layer down — so this test drives the key's own handler
    /// path (<c>MainWindowViewModel.ReportToggleAdviceReconciledOnly</c>, guarded by
    /// <c>IsSupplierPaymentAdviceReport</c>, which is exactly the <c>when</c> clause the window's F8 arm tests)
    /// and asserts the ROWS change, not a flag.</para>
    /// </summary>
    [Fact]
    public void F8_narrows_the_payment_advice_to_the_reconciled_payments_and_back()
    {
        var vm = NewSeededCompany("Advice Filter Co");
        var bank = AddBank(vm, "HDFC Bank", chequePrinting: true);
        var cleared = AddSupplier(vm, "Cleared Supplies");
        var pending = AddSupplier(vm, "Pending Supplies");
        var date = new DateOnly(2026, 5, 20);
        PostChequePayment(vm.Company!, cleared, bank, 15000m, "100500", date, bankDate: date.AddDays(3));
        PostChequePayment(vm.Company!, pending, bank, 25000m, "100501", date, bankDate: null);

        ArrowToBankingMenu(vm);
        ArrowToAndDrill(vm, "Payment Advice (Suppliers)");
        WidenPeriod(vm.Reports!, date);

        // The guard the window's F8 arm tests must actually be true on this report, or the key never fires.
        Assert.True(vm.IsSupplierPaymentAdviceReport);
        Assert.Contains(vm.Reports!.Rows, r => r.Particulars.Contains("Cleared Supplies", StringComparison.Ordinal));
        Assert.Contains(vm.Reports!.Rows, r => r.Particulars.Contains("Pending Supplies", StringComparison.Ordinal));

        vm.ReportToggleAdviceReconciledOnly();     // F8

        Assert.Contains(vm.Reports!.Rows, r => r.Particulars.Contains("Cleared Supplies", StringComparison.Ordinal));
        Assert.DoesNotContain(vm.Reports!.Rows, r => r.Particulars.Contains("Pending Supplies", StringComparison.Ordinal));
        Assert.Contains("reconciled only (F8)", vm.Reports!.Subtitle);

        vm.ReportToggleAdviceReconciledOnly();     // F8 again → back

        Assert.Contains(vm.Reports!.Rows, r => r.Particulars.Contains("Pending Supplies", StringComparison.Ordinal));
        Assert.DoesNotContain("reconciled only (F8)", vm.Reports!.Subtitle);
    }

    // ================================================================ the ledger master (the route IN to 8.4)

    /// <summary>
    /// 🔴 <b>The block that made row 8.4 reachable at all.</b> The cheque-printing settings are captured on the
    /// BANK LEDGER master (<c>help.tallysolutions.com/cheque-payments-set-up/</c>, "Specify Cheque Range and
    /// Format in Bank Ledger"), and until this wave there was no screen for them anywhere in the product. This
    /// drives the real screen, saves, RELOADS FROM SQLITE and then checks the report — because a flag that does
    /// not survive a reload is a flag the operator sets once and loses.
    /// </summary>
    [Fact]
    public void The_ledger_master_captures_cheque_printing_for_a_bank_and_it_survives_a_reload()
    {
        const string companyName = "Cheque Master Co";
        var vm = NewSeededCompany(companyName);

        vm.ShowLedgerMaster();
        var master = vm.LedgerMaster!;

        // A non-bank group does not offer the block at all.
        master.SelectedGroup = vm.Company!.FindGroupByName("Indirect Expenses");
        Assert.False(master.ShowChequePrinting);

        // Picking a bank group reveals it, without leaving and re-entering the screen.
        master.Name = "HDFC Bank";
        master.SelectedGroup = vm.Company!.FindGroupByName("Bank Accounts");
        Assert.True(master.ShowChequePrinting);

        master.OpeningBalanceText = "500000";
        master.EnableChequePrinting = true;
        master.ChequePrintingBankName = "HDFC Bank Ltd, Park Street";
        Assert.True(master.Create(), master.Message);

        // PERSISTED — through the real SQLite store, on the schema-v5 columns that had no screen until now.
        var reloaded = Reload(companyName);
        var saved = reloaded.FindLedgerByName("HDFC Bank")!;
        Assert.True(saved.EnableChequePrinting);
        Assert.Equal("HDFC Bank Ltd, Park Street", saved.ChequePrintingBankName);

        // And the report the operator opens next now has something to show.
        var acme = new DomainLedger(Guid.NewGuid(), "Acme Supplies",
            reloaded.FindGroupByName("Sundry Creditors")!.Id, Money.Zero, openingIsDebit: false);
        reloaded.AddLedger(acme);
        var date = new DateOnly(2026, 6, 1);
        PostChequePayment(reloaded, acme, saved, 15000m, "100777", date);

        var listed = Assert.Single(ChequePrinting.Build(
            reloaded, new PeriodRange(date.AddYears(-1), date.AddYears(1))));
        Assert.Equal("100777", listed.InstrumentNumber);
        Assert.Equal("Acme Supplies", listed.FavouringName);
    }

    /// <summary>
    /// Altering a bank ledger pre-fills the block from what is stored, so re-opening the master shows the truth
    /// and a save that touches only the name cannot silently switch cheque printing off.
    /// </summary>
    [Fact]
    public void Altering_a_bank_ledger_preserves_its_cheque_printing_settings()
    {
        const string companyName = "Cheque Alter Co";
        var vm = NewSeededCompany(companyName);

        vm.ShowLedgerMaster();
        var master = vm.LedgerMaster!;
        master.Name = "HDFC Bank";
        master.SelectedGroup = vm.Company!.FindGroupByName("Bank Accounts");
        master.EnableChequePrinting = true;
        master.ChequePrintingBankName = "HDFC Bank Ltd";
        Assert.True(master.Create(), master.Message);

        var bank = vm.Company!.FindLedgerByName("HDFC Bank")!;

        // Re-open the master over that ledger through the shell's real ALTER route: the block loads what is
        // stored, so re-opening the master shows the truth rather than a blank checkbox.
        vm.ShowLedgerAlter(bank.Id);
        var alter = vm.LedgerMaster!;
        Assert.True(alter.IsAltering);
        Assert.True(alter.EnableChequePrinting);
        Assert.Equal("HDFC Bank Ltd", alter.ChequePrintingBankName);

        // Rename only, then save. The cheque block must come through untouched.
        alter.Name = "HDFC Bank - Current";
        Assert.True(alter.Alter(), alter.Message);

        var reloaded = Reload(companyName);
        var saved = reloaded.FindLedgerByName("HDFC Bank - Current")!;
        Assert.True(saved.EnableChequePrinting);
        Assert.Equal("HDFC Bank Ltd", saved.ChequePrintingBankName);
    }

    /// <summary>
    /// 🔴 The hidden-sub-form rule, applied to this block. A NON-bank ledger never renders the cheque block, so
    /// saving one must not write its "value" — otherwise moving a bank ledger under a non-bank group and saving
    /// would silently switch its cheque printing off, and the operator would never see the field that did it.
    /// </summary>
    [Fact]
    public void Saving_a_ledger_whose_cheque_block_never_rendered_leaves_the_stored_flag_alone()
    {
        var vm = NewSeededCompany("Hidden Block Co");

        // A bank ledger with cheque printing already on, created directly (as an import or a prior release would).
        var bank = AddBank(vm, "HDFC Bank", chequePrinting: true);
        bank.ChequePrintingBankName = "HDFC Bank Ltd";

        vm.ShowLedgerAlter(bank.Id);
        var master = vm.LedgerMaster!;
        Assert.True(master.IsAltering);

        // Move it under a group that is NOT a bank group: the block disappears from the screen.
        master.SelectedGroup = vm.Company!.FindGroupByName("Indirect Expenses");
        Assert.False(master.ShowChequePrinting);
        Assert.True(master.Alter(), master.Message);

        // The stored flag and the stored name are exactly as they were — parked, not discarded.
        Assert.True(bank.EnableChequePrinting);
        Assert.Equal("HDFC Bank Ltd", bank.ChequePrintingBankName);
    }

    /// <summary>The block must not carry into the NEXT ledger — leaving it on would give the following bank the
    /// previous one's cheque stationery without the operator ever saying so.</summary>
    [Fact]
    public void The_cheque_block_is_cleared_for_the_next_ledger_after_a_create()
    {
        var vm = NewSeededCompany("Cheque Reset Co");

        vm.ShowLedgerMaster();
        var master = vm.LedgerMaster!;
        master.Name = "HDFC Bank";
        master.SelectedGroup = vm.Company!.FindGroupByName("Bank Accounts");
        master.EnableChequePrinting = true;
        master.ChequePrintingBankName = "HDFC Bank Ltd";
        Assert.True(master.Create(), master.Message);

        Assert.False(master.EnableChequePrinting);
        Assert.Equal(string.Empty, master.ChequePrintingBankName);
    }

    // ================================================================ the drilled voucher (Ctrl+P must survive)

    /// <summary>
    /// 🔴 <b>THE REGRESSION THIS SLICE ALMOST SHIPPED.</b> Switching "Enable cheque printing" on is the ONLY way
    /// to make the Cheque Printing report show anything, so every operator who uses row 8.4 will switch it on.
    /// The first cut of the shell then refused to print any drilled cheque payment on that bank at all —
    /// <c>OpenPrintPreview</c> returned early with "Cheque dimensions are not set for this bank" — and because
    /// the cheque DIMENSIONS do not persist yet (no <c>cheque_layouts</c> table; see the block in
    /// <c>Ledger.cs</c>), that refusal could never be cleared by anything the operator could do. Enabling one
    /// feature would have permanently switched off Ctrl+P on another.
    ///
    /// <para>So the rule is: a bank with no dimensions captured has simply not configured cheque printing, and a
    /// payment drawn on it prints the ordinary Dr/Cr voucher exactly as it always has. A refusal is raised only
    /// once dimensions EXIST and the cheque itself is unprintable — a state no operator can reach today, and the
    /// state the leaf renderer lands into when its migration is taken.</para>
    ///
    /// <para>This walks the operator's route: post the payment, drill the voucher from the Day Book, press
    /// Ctrl+P, and assert a preview carrying that voucher's own figures actually opens.</para>
    /// </summary>
    [Fact]
    public void A_cheque_payment_on_a_cheque_printing_bank_still_prints_its_voucher()
    {
        var vm = NewSeededCompany("Cheque Voucher Print Co");
        var bank = AddBank(vm, "HDFC Bank", chequePrinting: true);
        var acme = AddSupplier(vm, "Acme Supplies");
        var date = new DateOnly(2026, 5, 20);
        var voucher = PostChequePayment(vm.Company!, acme, bank, 41250m, "100300", date);

        // The operator's own route in: Banking → Cheque Printing → Enter on the cheque → Ctrl+P.
        ArrowToBankingMenu(vm);
        ArrowToAndDrill(vm, "Cheque Printing");
        WidenPeriod(vm.Reports!, date);
        var row = vm.Reports!.Rows.Single(r => r.DrillVoucherId == voucher.Id);
        vm.DrillReport(row);
        Assert.Equal(Screen.VoucherDetail, vm.CurrentScreen);

        vm.OpenPrintPreview();

        // The preview OPENED. Before the fix the shell returned early and the screen never changed.
        Assert.Equal(Screen.PrintPreview, vm.CurrentScreen);
        Assert.NotNull(vm.PrintPreview);
        Assert.Empty(vm.Notice);                     // and no refusal was raised in its place

        // And it is that voucher's own document, not an empty shell.
        var cells = vm.PrintPreview!.Pages.SelectMany(p => p.Lines).SelectMany(l => l.Cells).ToList();
        Assert.Contains(cells, c => c.Contains("Acme Supplies", StringComparison.Ordinal));
        Assert.Contains(cells, c => c.Contains("41,250.00", StringComparison.Ordinal));
    }

    /// <summary>
    /// The companion half of the rule above, asserted at the source: with no dimensions captured there is NO
    /// refusal, because "not configured" is not a failure. The property must not report a problem the operator
    /// has no screen to fix.
    /// </summary>
    [Fact]
    public void A_bank_with_no_cheque_dimensions_reports_no_refusal_at_all()
    {
        var vm = NewSeededCompany("No Refusal Co");
        var bank = AddBank(vm, "HDFC Bank", chequePrinting: true);
        var acme = AddSupplier(vm, "Acme Supplies");
        var date = new DateOnly(2026, 5, 20);
        var voucher = PostChequePayment(vm.Company!, acme, bank, 4100m, "100400", date);

        var detail = new VoucherDetailViewModel(vm.Company!, vm.Company!.FindVoucher(voucher.Id)!);
        Assert.Null(bank.ChequeLayout);              // this fixture never captured dimensions (v57 persists them when it does)
        Assert.NotNull(detail.ChequePrintData);      // the voucher IS a cheque payment
        Assert.Null(detail.ChequePrintRefusal);      // ...and that is not a refusal

        // Once dimensions DO exist, an unprintable cheque refuses again — the guard is dormant, not deleted.
        bank.ChequeLayout = new ChequeLayout
        {
            LeafWidthTmm = 2030,
            LeafHeightTmm = 920,
            PayeeTopTmm = 300,
            PayeeLeftTmm = 250,
            WordsLine1TopTmm = 400,
            WordsLine1LeftTmm = 250,
            WordsLine2TopTmm = 470,
            WordsLine2LeftTmm = 250,
            WordsWidthTmm = 1400,
            FiguresTopTmm = 400,
            FiguresLeftTmm = 1600,
        };
        var configured = new VoucherDetailViewModel(vm.Company!, vm.Company!.FindVoucher(voucher.Id)!);
        Assert.Null(configured.ChequePrintRefusal);  // this one is printable: it actually places its elements

        // 🔴 THIS CASE WAS CORRECTED, AND THE CORRECTION IS THE POINT. The "printable" layout above used to be a
        // LEAF SIZE AND NOTHING ELSE. Every element in the renderer is guarded by its own offsets, so such a
        // layout produced a correctly-sized page with no payee, no amount, no date and no signatory on it — and
        // printing it pulls a real, pre-numbered leaf out of the cheque book, which the operator then has to
        // void. A blank leaf is not a printable cheque; it refuses, and it names the calibration sheet.
        bank.ChequeLayout = new ChequeLayout { LeafWidthTmm = 2030, LeafHeightTmm = 920 };
        var nothingPlaced = new VoucherDetailViewModel(vm.Company!, vm.Company!.FindVoucher(voucher.Id)!);
        Assert.Contains("place nothing on this leaf", nothingPlaced.ChequePrintRefusal);

        bank.ChequeLayout = new ChequeLayout();      // dimensions captured but the leaf size never set
        var unusable = new VoucherDetailViewModel(vm.Company!, vm.Company!.FindVoucher(voucher.Id)!);
        Assert.Equal(
            "Cheque dimensions are not set for this bank. Set them on the bank ledger before printing.",
            unusable.ChequePrintRefusal);
    }
}
