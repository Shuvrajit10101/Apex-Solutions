using System;
using System.IO;
using System.Linq;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Seed;
using Apex.Ledger.Services;
using Apex.Desktop.Services;
using Apex.Desktop.ViewModels;
using Apex.Desktop.Views;
using Xunit;
using DomainLedger = Apex.Ledger.Domain.Ledger;

namespace Apex.Desktop.Tests;

/// <summary>
/// Navigation, identity and keys for voucher types — the four defects that between them made types unreachable
/// or made the UI lie about what it opens:
/// <list type="bullet">
/// <item><b>Credit Note / Debit Note had no menu row anywhere.</b> Reachable only by Alt+F6 / Alt+F5 or the
/// Day-Book Alt+A picker, so an operator who does not already know the accelerator could not find them. They now
/// sit under the Vouchers section beside Sales and Purchase, in the corpus's ordering (Book p.24 lists Credit
/// Note at #11 and Debit Note at #12 of the 24 predefined types) — decision D9 option A.</item>
/// <item><b>Physical Stock advertised a key that did something else.</b> Seeded "F10", menu row printed "F10",
/// while Ctrl+F7 was bound to nothing and F10 opens Apex's Other Vouchers menu. TallyPrime's key is
/// <b>Ctrl+F7</b> ("To open Physical Stock | Ctrl+F7"); F10 there lists vouchers/masters. Decision X1.</item>
/// <item><b>Voucher-type identity was discarded.</b> Every route resolved by BASE kind
/// (<c>FirstOrDefault(BaseType == x &amp;&amp; IsActive) ?? FirstOrDefault(BaseType == x)</c>), so a company with a
/// second Sales series could never reach it — and the second arm silently opened a <b>deactivated</b> type,
/// which made <see cref="VoucherType.IsActive"/> decorative.</item>
/// <item><b>Attendance was dead seed data</b> — nothing in the repository ever posts a Voucher of that base kind
/// (the Attendance screen writes <c>AttendanceEntry</c> rows). The seed row is gone (23 predefined types), and
/// the Day-Book picker refuses it even on a legacy company whose stored row survives.</item>
/// </list>
/// Everything keyboard-shaped here is driven through the REAL <see cref="MainWindow"/> tunnel handler
/// (<c>window.KeyPressQwerty</c>), never by asserting that a binding exists in isolation.
/// </summary>
public sealed class VoucherTypeNavigationIdentityTests
{
    private static (MainWindow Window, MainWindowViewModel Vm, string TempDir) NewWindow()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "ApexVtIdentity_" + Guid.NewGuid().ToString("N"));
        var storage = new CompanyStorage(tempDir);
        var vm = new MainWindowViewModel(storage);
        var window = new MainWindow { DataContext = vm };
        window.Show();
        return (window, vm, tempDir);
    }

    private static void NewCompany(MainWindowViewModel vm, string name)
    {
        vm.NewCompanyName = name;
        vm.CreateCompany();
        Assert.Equal(Screen.Gateway, vm.CurrentScreen);
    }

    /// <summary>Steps the active menu-column highlight (real Down keys) until it lands on <paramref name="label"/>.</summary>
    private static void NavigateMenuTo(MainWindowViewModel vm, MainWindow window, string label)
    {
        for (var i = 0; i < vm.Menu.Count + 2; i++)
        {
            if (vm.Menu[vm.SelectedIndex].Label == label) return;
            window.KeyPressQwerty(PhysicalKey.ArrowDown, RawInputModifiers.None);
        }
        Assert.Equal(label, vm.Menu[vm.SelectedIndex].Label);
    }

    private static string[] ItemLabels(MainWindowViewModel vm) =>
        vm.Menu.Where(m => m.IsSelectable).Select(m => m.Label).ToArray();

    /// <summary>
    /// A company carrying one posted Receipt so the Day Book has a drillable row. The figure is ODD-PAISA on
    /// purpose — a round number asserts nothing about money handling.
    /// </summary>
    private static void SeedOneReceipt(MainWindow window, MainWindowViewModel vm, string name)
    {
        NewCompany(vm, name);

        vm.ShowLedgerMaster();
        vm.LedgerMaster!.Name = "Capital A/c";
        vm.LedgerMaster!.SelectedGroup = vm.Company!.FindGroupByName("Capital Account");
        window.KeyPressQwerty(PhysicalKey.A, RawInputModifiers.Control);
        var capital = vm.Company!.FindLedgerByName("Capital A/c")!;
        var cash = vm.Company!.FindLedgerByName("Cash")!;

        vm.OpenVoucher(VoucherBaseType.Receipt);
        var e = vm.VoucherEntry!;
        e.Date = vm.Company!.FinancialYearStart.AddDays(5);
        e.Lines[0].SelectedLedger = cash;
        e.Lines[0].Side = DrCr.Debit;
        e.Lines[0].AmountText = "50123.47";
        e.Lines[1].SelectedLedger = capital;
        e.Lines[1].Side = DrCr.Credit;
        e.Lines[1].AmountText = "50123.47";
        window.KeyPressQwerty(PhysicalKey.A, RawInputModifiers.Control);
        Assert.Single(vm.Company!.Vouchers);
        Assert.Equal(50123.47m, vm.Company!.Vouchers[0].Lines[0].Amount.Amount);
    }

    /// <summary>Opens the Day Book and the Alt+A "Add Voucher" picker over it, returning the picker's rows.</summary>
    private static string[] OpenPicker(MainWindow window, MainWindowViewModel vm)
    {
        vm.OpenReport(ReportKind.DayBook);
        window.KeyPressQwerty(PhysicalKey.A, RawInputModifiers.Alt);
        Assert.Equal(Screen.AddVoucherPicker, vm.CurrentScreen);
        return vm.Columns[^1].Items.Where(i => i.IsSelectable).Select(i => i.Label).ToArray();
    }

    // ==================================================== (1) Credit / Debit Note have a menu row

    /// <summary>
    /// THE DRIVING TEST for the missing rows: Transactions → Vouchers lists Credit Note and Debit Note, nested
    /// under the same "Vouchers" section header as Sales and Purchase (never a flat dump), each printing its
    /// official accelerator. Before the fix the strings did not occur in the menu at all.
    /// </summary>
    [AvaloniaFact]
    public void Vouchers_menu_lists_credit_note_and_debit_note_under_the_vouchers_section()
    {
        var (window, vm, tempDir) = NewWindow();
        try
        {
            NewCompany(vm, "CNDN Menu Co");
            vm.ShowVouchersMenu();

            Assert.Equal(
                new[] { "Contra", "Payment", "Receipt", "Journal", "Sales", "Purchase",
                        "Credit Note", "Debit Note",
                        "Order Vouchers", "Inventory Vouchers", "Other Vouchers" },
                ItemLabels(vm));

            // Both are indented children of the VOUCHERS header — above the "Inventory" section header, so they
            // are nested under Vouchers rather than dumped flat or buried with the provisional kinds.
            var rows = vm.Menu.ToList();
            var creditIdx = rows.FindIndex(m => m.Label == "Credit Note");
            var debitIdx = rows.FindIndex(m => m.Label == "Debit Note");
            var inventoryHeaderIdx = rows.FindIndex(m => m.IsHeader && m.Label == "Inventory");
            Assert.True(creditIdx > 0 && debitIdx > creditIdx);
            Assert.True(debitIdx < inventoryHeaderIdx, "CN/DN must sit under Vouchers, above the Inventory section");
            Assert.True(rows[creditIdx].IsSubItem);
            Assert.True(rows[debitIdx].IsSubItem);

            // The advertised accelerators are TallyPrime's (Alt+F6 Credit Note, Alt+F5 Debit Note) — and they are
            // the keys this app already binds, so the rows cannot advertise a dead key.
            Assert.Equal("Alt+F6", rows[creditIdx].Hint);
            Assert.Equal("Alt+F5", rows[debitIdx].Hint);
        }
        finally { Close(window, tempDir); }
    }

    /// <summary>The Credit Note row actually opens a Credit Note entry — reached by real arrow keys + Enter.</summary>
    [AvaloniaFact]
    public void Credit_note_menu_row_opens_a_credit_note_entry()
    {
        var (window, vm, tempDir) = NewWindow();
        try
        {
            NewCompany(vm, "CN Row Co");
            vm.ShowVouchersMenu();
            NavigateMenuTo(vm, window, "Credit Note");
            window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);

            Assert.Equal(Screen.VoucherEntry, vm.CurrentScreen);
            Assert.Equal(VoucherBaseType.CreditNote, vm.VoucherEntry!.Type.BaseType);
        }
        finally { Close(window, tempDir); }
    }

    /// <summary>The Debit Note row actually opens a Debit Note entry.</summary>
    [AvaloniaFact]
    public void Debit_note_menu_row_opens_a_debit_note_entry()
    {
        var (window, vm, tempDir) = NewWindow();
        try
        {
            NewCompany(vm, "DN Row Co");
            vm.ShowVouchersMenu();
            NavigateMenuTo(vm, window, "Debit Note");
            window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);

            Assert.Equal(Screen.VoucherEntry, vm.CurrentScreen);
            Assert.Equal(VoucherBaseType.DebitNote, vm.VoucherEntry!.Type.BaseType);
        }
        finally { Close(window, tempDir); }
    }

    // ==================================================== (2) Physical Stock = Ctrl+F7

    /// <summary>
    /// THE DRIVING TEST for the dead key: real Ctrl+F7 opens the Physical Stock entry (it was bound to nothing),
    /// and the menu row + the seeded type both advertise Ctrl+F7 rather than F10.
    /// </summary>
    [AvaloniaFact]
    public void CtrlF7_opens_physical_stock_and_every_advertised_string_says_so()
    {
        var (window, vm, tempDir) = NewWindow();
        try
        {
            NewCompany(vm, "PhysStock Key Co");

            window.KeyPressQwerty(PhysicalKey.F7, RawInputModifiers.Control);

            Assert.Equal(Screen.InventoryVoucherEntry, vm.CurrentScreen);
            Assert.Equal(VoucherBaseType.PhysicalStock, vm.InventoryVoucherEntry!.Type.BaseType);

            // The seeded type carries the key it can actually be opened with.
            var seeded = vm.Company!.VoucherTypes.Single(t => t.BaseType == VoucherBaseType.PhysicalStock);
            Assert.Equal("Ctrl+F7", seeded.DefaultShortcut);

            // …and so does the menu row.
            vm.ShowInventoryVouchersMenu();
            var row = vm.Menu.Single(m => m.Label == "Physical Stock");
            Assert.Equal("Ctrl+F7", row.Hint);
        }
        finally { Close(window, tempDir); }
    }

    /// <summary>
    /// Ctrl+F7 shadows nothing: every neighbouring modifier+F-key still opens the screen it always opened, and
    /// bare F10 still opens Apex's "Other Vouchers" menu (a deliberate, documented divergence — decision D7
    /// option A / X6 — since Other Vouchers is the only route to Memorandum / Reversing Journal / Job Work).
    /// </summary>
    [AvaloniaFact]
    public void CtrlF7_does_not_shadow_any_neighbouring_accelerator()
    {
        var (window, vm, tempDir) = NewWindow();
        try
        {
            NewCompany(vm, "Shadow Co");

            window.KeyPressQwerty(PhysicalKey.F7, RawInputModifiers.Alt);     // Alt+F7 = Stock Journal
            Assert.Equal(VoucherBaseType.StockJournal, vm.InventoryVoucherEntry!.Type.BaseType);
            vm.ShowGateway();

            window.KeyPressQwerty(PhysicalKey.F6, RawInputModifiers.Control); // Ctrl+F6 = Rejection In
            Assert.Equal(VoucherBaseType.RejectionIn, vm.InventoryVoucherEntry!.Type.BaseType);
            vm.ShowGateway();

            window.KeyPressQwerty(PhysicalKey.F5, RawInputModifiers.Control); // Ctrl+F5 = Rejection Out
            Assert.Equal(VoucherBaseType.RejectionOut, vm.InventoryVoucherEntry!.Type.BaseType);
            vm.ShowGateway();

            window.KeyPressQwerty(PhysicalKey.F8, RawInputModifiers.Control); // Ctrl+F8 = Sales Order
            Assert.Equal(VoucherBaseType.SalesOrder, vm.InventoryVoucherEntry!.Type.BaseType);
            vm.ShowGateway();

            window.KeyPressQwerty(PhysicalKey.F9, RawInputModifiers.Control); // Ctrl+F9 = Purchase Order
            Assert.Equal(VoucherBaseType.PurchaseOrder, vm.InventoryVoucherEntry!.Type.BaseType);
            vm.ShowGateway();

            window.KeyPressQwerty(PhysicalKey.F10, RawInputModifiers.None);   // F10 = Other Vouchers menu
            Assert.Equal(GatewayMenu.OtherVouchers, vm.CurrentGatewayMenu);
        }
        finally { Close(window, tempDir); }
    }

    // ==================================================== (3) identity

    /// <summary>
    /// THE DRIVING TEST for identity: a company with a SECOND Sales series ("Export Sales") could not reach it —
    /// picking its row in the Day-Book Alt+A picker opened the predefined Sales type instead, because the picker
    /// passed only the BASE kind. The entry must now carry the very type whose row was chosen.
    /// </summary>
    [AvaloniaFact]
    public void Day_book_picker_opens_the_exact_type_picked_not_the_first_of_its_base_kind()
    {
        var (window, vm, tempDir) = NewWindow();
        try
        {
            SeedOneReceipt(window, vm, "Identity Sales Co");
            var export = new VoucherType(Guid.NewGuid(), "Export Sales", VoucherBaseType.Sales);
            vm.Company!.AddVoucherType(export);

            var labels = OpenPicker(window, vm);
            Assert.Contains("Export Sales", labels);

            NavigateMenuTo(vm, window, "Export Sales");
            window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);

            Assert.Equal(Screen.VoucherEntry, vm.CurrentScreen);
            Assert.Equal(export.Id, vm.VoucherEntry!.Type.Id);
            Assert.Equal("Export Sales", vm.VoucherEntry!.Type.Name);
        }
        finally { Close(window, tempDir); }
    }

    /// <summary>
    /// The same defect on the inventory side: a second Receipt-Note series is reachable as itself, not silently
    /// swapped for the seeded one (a different series means a different name and a different number sequence).
    /// </summary>
    [AvaloniaFact]
    public void Day_book_picker_opens_the_exact_inventory_type_picked()
    {
        var (window, vm, tempDir) = NewWindow();
        try
        {
            SeedOneReceipt(window, vm, "Identity GRN Co");
            var branch = new VoucherType(Guid.NewGuid(), "Branch Receipt Note", VoucherBaseType.ReceiptNote);
            vm.Company!.AddVoucherType(branch);

            OpenPicker(window, vm);
            NavigateMenuTo(vm, window, "Branch Receipt Note");
            window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);

            Assert.Equal(Screen.InventoryVoucherEntry, vm.CurrentScreen);
            Assert.Equal(branch.Id, vm.InventoryVoucherEntry!.Type.Id);
        }
        finally { Close(window, tempDir); }
    }

    /// <summary>
    /// Identity that means a DIFFERENT SCREEN, not just a different series: a Manufacturing Journal is a
    /// Stock-Journal-based type, so picking it opened a PLAIN Stock Journal entry — a different screen with a
    /// different posting rule (a manufacture need not balance by quantity). It must open its own screen.
    /// </summary>
    [AvaloniaFact]
    public void Day_book_picker_opens_the_manufacturing_journal_not_a_plain_stock_journal()
    {
        var (window, vm, tempDir) = NewWindow();
        try
        {
            SeedOneReceipt(window, vm, "Identity MJ Co");
            vm.Company!.SetComponentsBom = true;
            vm.OpenManufacturingJournal();                      // creates the MJ type on first use
            var mj = vm.Company!.VoucherTypes.Single(t => t.IsManufacturingJournal);
            vm.ShowGateway();

            OpenPicker(window, vm);
            NavigateMenuTo(vm, window, mj.Name);
            window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);

            Assert.Equal(Screen.ManufacturingJournalEntry, vm.CurrentScreen);
            Assert.Equal(mj.Id, vm.ManufacturingJournalEntry!.Type.Id);
        }
        finally { Close(window, tempDir); }
    }

    /// <summary>A POS Sales type picked from the picker opens POS Billing under THAT type — not a plain Sales
    /// entry under the predefined Sales series (which would take the wrong number and skip the tender split).</summary>
    [AvaloniaFact]
    public void Day_book_picker_opens_pos_billing_for_a_pos_sales_type()
    {
        var (window, vm, tempDir) = NewWindow();
        try
        {
            SeedOneReceipt(window, vm, "Identity POS Co");
            vm.OpenPosBilling();                                // creates the "Sales (POS)" type on first use
            var pos = vm.Company!.VoucherTypes.Single(t => t.IsPosSales);
            vm.ShowGateway();

            OpenPicker(window, vm);
            NavigateMenuTo(vm, window, pos.Name);
            window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);

            Assert.Equal(Screen.PosBilling, vm.CurrentScreen);
            Assert.Equal(pos.Id, vm.PosBilling!.Type.Id);
        }
        finally { Close(window, tempDir); }
    }

    /// <summary>
    /// The accelerator/menu route stays deterministic when several types share a base kind: the "Sales" route
    /// opens the PREDEFINED series, never whichever user type happens to sort first in the list.
    /// </summary>
    [AvaloniaFact]
    public void A_base_type_route_opens_the_predefined_series_when_several_share_the_base()
    {
        var (window, vm, tempDir) = NewWindow();
        try
        {
            NewCompany(vm, "Default Series Co");
            var predefined = vm.Company!.VoucherTypes.Single(t => t.BaseType == VoucherBaseType.Sales && t.IsPredefined);
            vm.Company!.AddVoucherType(new VoucherType(Guid.NewGuid(), "Branch Sales", VoucherBaseType.Sales));

            vm.OpenVoucher(VoucherBaseType.Sales);

            Assert.Equal(Screen.VoucherEntry, vm.CurrentScreen);
            Assert.Equal(predefined.Id, vm.VoucherEntry!.Type.Id);
        }
        finally { Close(window, tempDir); }
    }

    /// <summary>
    /// A base-kind route never lands on a SPECIALISED variant of that base — a POS Sales type belongs to POS
    /// Billing, not to the plain Dr/Cr Sales screen. With the seeded Sales series switched off and only the POS
    /// type left active, the plain Sales route must refuse rather than open a till in the wrong screen.
    /// </summary>
    [AvaloniaFact]
    public void A_base_type_route_never_lands_on_a_specialised_variant()
    {
        var (window, vm, tempDir) = NewWindow();
        try
        {
            NewCompany(vm, "Specialised Variant Co");
            vm.OpenPosBilling();                                // creates the POS Sales type
            var pos = vm.Company!.VoucherTypes.Single(t => t.IsPosSales);
            Assert.True(pos.IsActive);
            vm.Company!.VoucherTypes.Single(t => t.BaseType == VoucherBaseType.Sales && t.IsPredefined).IsActive = false;
            vm.ShowGateway();

            vm.OpenVoucher(VoucherBaseType.Sales);

            Assert.NotEqual(Screen.VoucherEntry, vm.CurrentScreen);
            Assert.Contains("active", vm.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally { Close(window, tempDir); }
    }

    // ==================================================== (4) IsActive is not decorative

    /// <summary>
    /// THE DRIVING TEST for the inactive fallback: with the only Sales type deactivated, the route used to fall
    /// back to it and open a DEACTIVATED type silently. It must refuse and say so instead — otherwise the
    /// documented "show inactive → activate" gesture means nothing.
    /// </summary>
    [AvaloniaFact]
    public void An_inactive_voucher_type_is_never_opened_silently()
    {
        var (window, vm, tempDir) = NewWindow();
        try
        {
            NewCompany(vm, "Inactive Sales Co");
            vm.Company!.VoucherTypes.Single(t => t.BaseType == VoucherBaseType.Sales).IsActive = false;

            vm.OpenVoucher(VoucherBaseType.Sales);

            Assert.NotEqual(Screen.VoucherEntry, vm.CurrentScreen);
            Assert.Null(vm.VoucherEntry);
            Assert.Contains("Sales", vm.Message);
            Assert.Contains("active", vm.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally { Close(window, tempDir); }
    }

    /// <summary>The same rule on the inventory routes — a deactivated Physical Stock type is not opened by Ctrl+F7.</summary>
    [AvaloniaFact]
    public void An_inactive_inventory_type_is_never_opened_silently()
    {
        var (window, vm, tempDir) = NewWindow();
        try
        {
            NewCompany(vm, "Inactive PhysStock Co");
            vm.Company!.VoucherTypes.Single(t => t.BaseType == VoucherBaseType.PhysicalStock).IsActive = false;

            window.KeyPressQwerty(PhysicalKey.F7, RawInputModifiers.Control);

            Assert.NotEqual(Screen.InventoryVoucherEntry, vm.CurrentScreen);
            Assert.Null(vm.InventoryVoucherEntry);
            Assert.Contains("active", vm.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally { Close(window, tempDir); }
    }

    /// <summary>An ACTIVE second series is reachable even when the predefined one is switched off — which is the
    /// whole point of the Active flag.</summary>
    [AvaloniaFact]
    public void A_route_falls_to_the_remaining_active_series_when_the_predefined_one_is_off()
    {
        var (window, vm, tempDir) = NewWindow();
        try
        {
            NewCompany(vm, "Active Fallback Co");
            vm.Company!.VoucherTypes.Single(t => t.BaseType == VoucherBaseType.Sales).IsActive = false;
            var branch = new VoucherType(Guid.NewGuid(), "Branch Sales", VoucherBaseType.Sales);
            vm.Company!.AddVoucherType(branch);

            vm.OpenVoucher(VoucherBaseType.Sales);

            Assert.Equal(Screen.VoucherEntry, vm.CurrentScreen);
            Assert.Equal(branch.Id, vm.VoucherEntry!.Type.Id);
        }
        finally { Close(window, tempDir); }
    }

    // ==================================================== (5) Attendance is dead — and gone

    /// <summary>
    /// THE DRIVING TEST for the dead seed row: a fresh company seeds <b>23</b> predefined voucher types and none
    /// of them is Attendance. Nothing in the product ever posts a <c>Voucher</c> of that base kind — the
    /// Attendance / Production screen writes <c>AttendanceEntry</c> rows — so the row was master data that
    /// nothing read, while propping up a "24 of 24" completeness claim that was not true.
    /// </summary>
    [AvaloniaFact]
    public void A_fresh_company_seeds_no_attendance_voucher_type()
    {
        var (window, vm, tempDir) = NewWindow();
        try
        {
            NewCompany(vm, "No Attendance Co");

            Assert.Equal(23, SeedVoucherTypes.Count);
            Assert.Equal(23, vm.Company!.VoucherTypes.Count);
            Assert.DoesNotContain(vm.Company!.VoucherTypes, t => t.BaseType == VoucherBaseType.Attendance);

            // The Attendance / Production SCREEN is untouched — it never needed a voucher type.
            new PayrollService(vm.Company!).EnablePayroll();
            vm.ShowAttendanceVoucher();
            Assert.Equal(Screen.AttendanceVoucherEntry, vm.CurrentScreen);
        }
        finally { Close(window, tempDir); }
    }

    /// <summary>
    /// A company created BEFORE the seed row was dropped still carries a stored Attendance row (its
    /// <c>base_type</c> ordinal is persisted, so the enum member must stay and the row cannot be deleted without
    /// a data migration). The Day-Book picker must still refuse to offer it — choosing it could not produce a
    /// voucher. Payroll, which DOES post, stays offered and opens its own computed screen rather than a bare
    /// Dr/Cr grid.
    /// </summary>
    [AvaloniaFact]
    public void Day_book_picker_refuses_a_legacy_attendance_row_but_routes_payroll_to_its_own_screen()
    {
        var (window, vm, tempDir) = NewWindow();
        try
        {
            SeedOneReceipt(window, vm, "Legacy Attendance Co");
            new PayrollService(vm.Company!).EnablePayroll();

            // Re-create the legacy shape by hand: a stored, ACTIVE Attendance type.
            vm.Company!.AddVoucherType(new VoucherType(
                Guid.NewGuid(), "Attendance", VoucherBaseType.Attendance, abbreviation: "Attd"));
            // Nothing in the product activates the Payroll type today (a separate, known defect) — do it here so
            // this test does not depend on that fix landing.
            vm.Company!.VoucherTypes.Single(t => t.BaseType == VoucherBaseType.Payroll).IsActive = true;

            var labels = OpenPicker(window, vm);
            Assert.DoesNotContain("Attendance", labels);
            Assert.Contains("Payroll", labels);

            NavigateMenuTo(vm, window, "Payroll");
            window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
            Assert.Equal(Screen.PayrollVoucherEntry, vm.CurrentScreen);
        }
        finally { Close(window, tempDir); }
    }

    // ============================================ (6) the WRITING paths obey the same rule as the routes

    /// <summary>
    /// Builds a company with a bill-by-bill debtor carrying ONE open bill, posted through the engine. The figure
    /// is odd-paisa on purpose (Rs 42,137.63 — a round number would assert nothing about the money that flows
    /// through settlement), and it is returned so the caller can assert on it.
    /// </summary>
    private static (DomainLedger Debtor, decimal Amount, string Reference) SeedOneOpenBill(
        MainWindowViewModel vm, string name)
    {
        NewCompany(vm, name);
        var c = vm.Company!;

        var sales = new DomainLedger(Guid.NewGuid(), "Sales A/c",
            c.FindGroupByName("Sales Accounts")!.Id, Money.Zero, openingIsDebit: false);
        var debtor = new DomainLedger(Guid.NewGuid(), "Acme Traders",
            c.FindGroupByName("Sundry Debtors")!.Id, Money.Zero, openingIsDebit: true,
            maintainBillByBill: true, defaultCreditPeriodDays: 30);
        c.AddLedger(sales);
        c.AddLedger(debtor);

        const decimal amount = 42137.63m;   // ODD PAISA
        const string reference = "INV-901";
        var salesVt = c.FindVoucherTypeByName("Sales")!;
        new LedgerService(c).Post(new Voucher(Guid.NewGuid(), salesVt.Id, c.FinancialYearStart.AddDays(3), new[]
        {
            new EntryLine(debtor.Id, Money.FromRupees(amount), DrCr.Debit, new[]
            {
                new BillAllocation(BillRefType.NewRef, reference, Money.FromRupees(amount)),
            }),
            new EntryLine(sales.Id, Money.FromRupees(amount), DrCr.Credit),
        }));
        return (debtor, amount, reference);
    }

    /// <summary>Opens Outstandings → Receivables and spacebar-selects the first bill, through the real tunnel.</summary>
    private static void SelectFirstBill(MainWindow window, MainWindowViewModel vm)
    {
        vm.OpenOutstandings(OutstandingsKind.Receivables);
        Assert.Equal(Screen.Outstandings, vm.CurrentScreen);
        vm.Outstandings!.HighlightedIndex = 0;
        window.KeyPressQwerty(PhysicalKey.Space, RawInputModifiers.None);
        Assert.Single(vm.Outstandings!.SelectedRows);
    }

    /// <summary>
    /// THE DRIVING TEST for the sixth, unconverted call site. Bill Settlement (Ctrl+B) carried the byte-identical
    /// pre-change shape — <c>FirstOrDefault(BaseType == want &amp;&amp; IsActive) ?? FirstOrDefault(BaseType == want)</c> —
    /// so a company that had switched its only Receipt series OFF could not OPEN the type (F6 refuses) yet could
    /// still be made to POST under it: a real Receipt voucher for the full odd-paisa bill was written and the bill
    /// knocked off. The writing path was the one left unguarded, which is worse than any of the five navigation
    /// sites. It must refuse with the same message the routes give, and leave the books untouched.
    /// </summary>
    [AvaloniaFact]
    public void Bill_settlement_refuses_to_post_under_a_deactivated_receipt_type()
    {
        var (window, vm, tempDir) = NewWindow();
        try
        {
            var (_, amount, reference) = SeedOneOpenBill(vm, "Settle Inactive Co");
            var receipt = vm.Company!.VoucherTypes.Single(t => t.BaseType == VoucherBaseType.Receipt);
            receipt.IsActive = false;

            // The navigation route already refuses …
            vm.OpenVoucher(VoucherBaseType.Receipt);
            Assert.NotEqual(Screen.VoucherEntry, vm.CurrentScreen);

            // … so the writing route must refuse too.
            SelectFirstBill(window, vm);
            var before = vm.Company!.Vouchers.Count;
            window.KeyPressQwerty(PhysicalKey.B, RawInputModifiers.Control);

            Assert.Equal("No active 'Receipt' voucher type is configured for this company.",
                vm.Outstandings!.Message);
            Assert.Equal(before, vm.Company!.Vouchers.Count);
            Assert.DoesNotContain(vm.Company!.Vouchers, v => v.TypeId == receipt.Id);
            // The bill is still open, for its full odd-paisa amount — nothing was knocked off.
            var row = Assert.Single(vm.Outstandings!.Rows);
            Assert.Equal(reference, row.Bill.Reference);
            Assert.Equal(amount, row.Bill.Pending.Amount);
        }
        finally { Close(window, tempDir); }
    }

    /// <summary>
    /// The other half of the same rule: with an ACTIVE series the settlement still posts, under the series the
    /// resolver picks (the seeded predefined one, not whichever row happens to come first), for the exact
    /// odd-paisa pending amount. Guards against "fixing" the refusal by breaking settlement.
    /// </summary>
    [AvaloniaFact]
    public void Bill_settlement_posts_the_odd_paisa_bill_under_the_resolved_active_series()
    {
        var (window, vm, tempDir) = NewWindow();
        try
        {
            var (debtor, amount, _) = SeedOneOpenBill(vm, "Settle Active Co");
            // A second, non-predefined Receipt series exists — the settlement must still land on the seeded one.
            vm.Company!.AddVoucherType(new VoucherType(Guid.NewGuid(), "Branch Receipt", VoucherBaseType.Receipt));
            var seeded = vm.Company!.VoucherTypes.Single(
                t => t.BaseType == VoucherBaseType.Receipt && t.IsPredefined);

            SelectFirstBill(window, vm);
            window.KeyPressQwerty(PhysicalKey.B, RawInputModifiers.Control);

            Assert.Equal("Settled 1 bill.", vm.Outstandings!.Message);
            var settlement = vm.Company!.Vouchers.Single(v => v.TypeId == seeded.Id);
            Assert.Equal(amount, settlement.Lines.Single(l => l.LedgerId == debtor.Id).Amount.Amount);
            Assert.Empty(vm.Outstandings!.Rows);   // the bill is knocked off
        }
        finally { Close(window, tempDir); }
    }

    /// <summary>
    /// A SEVENTH site with the same shape, found while converting the sixth: the Forex report's "book the
    /// adjustment" action resolved its Journal type with the same inactive fallback, and it POSTS. With the only
    /// Journal series switched off it must refuse rather than book a real adjusting journal under it. The
    /// revaluation is deliberately odd-paisa on both sides (US$1,000 booked at 83.44675, revalued at 84.55331).
    /// </summary>
    [AvaloniaFact]
    public void Forex_adjustment_refuses_to_book_under_a_deactivated_journal_type()
    {
        var (window, vm, tempDir) = NewWindow();
        try
        {
            NewCompany(vm, "Forex Inactive Journal Co");
            var c = vm.Company!;
            var usd = new Currency(Guid.NewGuid(), "$", "USD", decimalPlaces: 2);
            c.AddCurrency(usd);
            c.AddExchangeRate(new ExchangeRate(Guid.NewGuid(), usd.Id, c.FinancialYearStart, 83.44675m));
            c.AddExchangeRate(new ExchangeRate(Guid.NewGuid(), usd.Id, c.FinancialYearStart.AddMonths(3), 84.55331m));

            var exportSales = new DomainLedger(Guid.NewGuid(), "Export Sales",
                c.FindGroupByName("Sales Accounts")!.Id, Money.Zero, openingIsDebit: false);
            var usCustomer = new DomainLedger(Guid.NewGuid(), "US Customer",
                c.FindGroupByName("Sundry Debtors")!.Id, Money.Zero, openingIsDebit: true, currencyId: usd.Id);
            c.AddLedger(exportSales);
            c.AddLedger(usCustomer);

            var booked = Money.FromRupees(83446.75m);   // US$1,000 @ 83.44675 — ODD PAISA
            new LedgerService(c).Post(new Voucher(
                Guid.NewGuid(), c.FindVoucherTypeByName("Sales")!.Id, c.FinancialYearStart.AddDays(9), new[]
                {
                    new EntryLine(usCustomer.Id, booked, DrCr.Debit,
                        forex: new ForexInfo(usd.Id, Money.FromRupees(1000m), 83.44675m)),
                    new EntryLine(exportSales.Id, booked, DrCr.Credit),
                }));

            var journal = c.VoucherTypes.Single(t => t.BaseType == VoucherBaseType.Journal);
            journal.IsActive = false;

            var forex = new ForexReportViewModel(c, new CompanyStorage(tempDir), () => { })
            {
                AsOfText = ApexDate.Format(c.FinancialYearStart.AddMonths(3)),
            };
            Assert.True(forex.CanBook, "the revaluation must have something to book, else the test proves nothing");
            var before = c.Vouchers.Count;

            var posted = forex.BookAdjustment();

            Assert.Null(posted);
            Assert.Equal("No active 'Journal' voucher type is configured for this company.", forex.Message);
            Assert.Equal(before, c.Vouchers.Count);
            Assert.DoesNotContain(c.Vouchers, v => v.TypeId == journal.Id);
        }
        finally { Close(window, tempDir); }
    }

    // ============================================ (7) the refusal message names the type the way the UI does

    /// <summary>
    /// The refusal message is the only thing the operator sees, so it must name the type the way every other
    /// surface does. It interpolated the raw enum identifier — "No active 'CreditNote' voucher type…",
    /// "No active 'PhysicalStock'…" — tokens that appear nowhere in the UI (the menu rows say "Credit Note",
    /// "Physical Stock"). It now uses the type's own stored name when the company has one, else the enum name
    /// split into words.
    /// </summary>
    [AvaloniaFact]
    public void The_no_active_type_message_names_the_type_the_way_the_menu_does()
    {
        var (window, vm, tempDir) = NewWindow();
        try
        {
            NewCompany(vm, "Message Wording Co");
            vm.Company!.VoucherTypes.Single(t => t.BaseType == VoucherBaseType.CreditNote).IsActive = false;
            vm.Company!.VoucherTypes.Single(t => t.BaseType == VoucherBaseType.PhysicalStock).IsActive = false;

            // The row the operator pressed says "Credit Note", so the refusal must too.
            window.KeyPressQwerty(PhysicalKey.F6, RawInputModifiers.Alt);
            Assert.Equal("No active 'Credit Note' voucher type is configured for this company.", vm.Message);

            window.KeyPressQwerty(PhysicalKey.F7, RawInputModifiers.Control);
            Assert.Equal("No active 'Physical Stock' voucher type is configured for this company.", vm.Message);

            // A company that RENAMED its series is told about the thing it can actually see.
            var branch = new VoucherType(Guid.NewGuid(), "Branch Contra", VoucherBaseType.Contra);
            vm.Company!.VoucherTypes.Single(t => t.BaseType == VoucherBaseType.Contra && t.IsPredefined).Name =
                "Bank Transfer";
            vm.Company!.AddVoucherType(branch);
            branch.IsActive = false;
            Assert.Equal(
                "No active 'Bank Transfer' voucher type is configured for this company.",
                VoucherTypeResolver.NoActiveTypeMessage(vm.Company!, VoucherBaseType.Contra));

            // And with no company in hand the enum name is still split into words, never shown as an identifier.
            Assert.Equal(
                "No active 'Job Work In Order' voucher type is configured for this company.",
                VoucherTypeResolver.NoActiveTypeMessage(VoucherBaseType.JobWorkInOrder));
            Assert.DoesNotContain("JobWorkInOrder",
                VoucherTypeResolver.NoActiveTypeMessage(VoucherBaseType.JobWorkInOrder));
        }
        finally { Close(window, tempDir); }
    }

    // ============================================ (8) the two Physical Stock shortcut surfaces agree

    /// <summary>
    /// Rebuilds the pre-change stored shape on disk — a company whose Physical Stock type still persists the old
    /// "F10" — and returns it loaded back through the real <see cref="CompanyStorage"/> round trip.
    /// </summary>
    private static Company LegacyCompanyOnDisk(MainWindowViewModel vm, string tempDir, string name)
    {
        NewCompany(vm, name);
        vm.Company!.VoucherTypes.Single(t => t.BaseType == VoucherBaseType.PhysicalStock).DefaultShortcut = "F10";
        var storage = new CompanyStorage(tempDir);
        storage.Save(vm.Company!);
        var entry = storage.ListCompanies().Single(e => e.Name == name);
        return storage.Load(entry);
    }

    /// <summary>
    /// THE DRIVING TEST for the stale stored key. <c>voucher_types.default_shortcut</c> is persisted per company,
    /// so a company created before Physical Stock moved from F10 to Ctrl+F7 still stores "F10" — and F10 is LIVE
    /// in this app (it opens the Other Vouchers menu), so the stale string is not merely cosmetic. The load path
    /// repairs the superseded seeded value instead of shipping a v50 schema migration for one hint string.
    /// </summary>
    [AvaloniaFact]
    public void A_legacy_companys_stale_physical_stock_shortcut_is_repaired_on_load()
    {
        var (window, vm, tempDir) = NewWindow();
        try
        {
            var reloaded = LegacyCompanyOnDisk(vm, tempDir, "Legacy PhysStock Co");

            Assert.Equal(23, reloaded.VoucherTypes.Count);
            Assert.Equal("Ctrl+F7",
                reloaded.VoucherTypes.Single(t => t.BaseType == VoucherBaseType.PhysicalStock).DefaultShortcut);
            // The repair is idempotent and touches nothing else — every other seeded key survives verbatim.
            Assert.Equal("F4", reloaded.FindVoucherTypeByName("Contra")!.DefaultShortcut);
            Assert.Equal("Alt+F7", reloaded.FindVoucherTypeByName("Stock Journal")!.DefaultShortcut);
            Assert.Equal(0, VoucherTypeResolver.RepairSupersededSeedShortcuts(reloaded));
        }
        finally { Close(window, tempDir); }
    }

    /// <summary>
    /// The two surfaces that advertise the Physical Stock key — the authored Inventory Vouchers menu row and the
    /// data-driven Day-Book Alt+A picker row — must agree on an EXISTING company. Before the repair the menu said
    /// "Ctrl+F7" while the picker said "F10", and pressing F10 there opened the Other Vouchers menu.
    /// </summary>
    [AvaloniaFact]
    public void Both_physical_stock_shortcut_surfaces_agree_on_a_legacy_company()
    {
        var (window, vm, tempDir) = NewWindow();
        try
        {
            const string name = "Legacy PhysStock Surfaces Co";
            SeedOneReceipt(window, vm, name);
            vm.Company!.VoucherTypes.Single(t => t.BaseType == VoucherBaseType.PhysicalStock).DefaultShortcut = "F10";
            new CompanyStorage(tempDir).Save(vm.Company!);

            // Re-open it the way an operator does — Company Info → Select Company → Enter.
            vm.ShowCompanySelect();
            NavigateMenuTo(vm, window, name);
            window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
            Assert.Equal(Screen.Gateway, vm.CurrentScreen);

            vm.ShowInventoryVouchersMenu();
            var menuHint = vm.Menu.Single(m => m.Label == "Physical Stock").Hint;

            OpenPicker(window, vm);
            var pickerHint = vm.Columns[^1].Items.Single(i => i.Label == "Physical Stock").Hint;

            Assert.Equal("Ctrl+F7", menuHint);
            Assert.Equal(menuHint, pickerHint);
        }
        finally { Close(window, tempDir); }
    }

    // ============================================ (9) the bare-letter surface is pinned

    /// <summary>Label → painted bare letter for every selectable row of the active menu column.</summary>
    private static string HotKeyMap(MainWindowViewModel vm) =>
        string.Join(" ", vm.Menu.Where(m => m.IsSelectable)
            .Select(m => $"{m.Label}={(m.HotKey?.ToString() ?? "-")}"));

    /// <summary>
    /// PINS the computed bare-letter hotkeys of the Vouchers column. <c>GatewayColumn.AssignHotKeys</c> hands out
    /// the first unclaimed letter in ROW ORDER, so inserting a row silently re-shuffles the letters of every row
    /// below it — which is exactly what adding Credit Note / Debit Note did: "Order Vouchers" lost the <b>d</b> of
    /// "Order" to "Debit Note" and now paints the <b>V</b> of "Vouchers". That single move is unavoidable (D is
    /// Debit Note's own initial and Order Vouchers held it only by accident — O itself is reserved for Import),
    /// but it must never happen UNNOTICED again: any future row inserted into this column that moves a letter
    /// fails here. Every painted letter is also checked to resolve back to its own row, so no row can advertise a
    /// letter that activates a different one.
    /// </summary>
    [AvaloniaFact]
    public void The_vouchers_column_bare_letters_are_pinned()
    {
        var (window, vm, tempDir) = NewWindow();
        try
        {
            NewCompany(vm, "Hotkey Pin Co");
            vm.ShowVouchersMenu();

            Assert.Equal(
                "Contra=C Payment=P Receipt=R Journal=J Sales=S Purchase=u " +
                "Credit Note=e Debit Note=D " +
                "Order Vouchers=V Inventory Vouchers=I Other Vouchers=t",
                HotKeyMap(vm));

            // Every painted letter activates the row it is painted on (and O/Y stay unclaimed — they are bound
            // globally to Import / Export Data on every Gateway column).
            var column = vm.Columns[^1];
            foreach (var row in column.Items.Where(i => i.IsSelectable))
                Assert.Same(row, column.FindByHotKey(row.HotKey!.Value));
            Assert.Null(column.FindByHotKey('O'));
            Assert.Null(column.FindByHotKey('Y'));
        }
        finally { Close(window, tempDir); }
    }

    /// <summary>
    /// The same pin for the FULL column — the Statutory (TDS/TCS) and Payroll rows only appear when their F11
    /// feature is on, and they sit below the new rows, so they are the ones most exposed to a re-shuffle. This
    /// configuration is where the second displacement happens: with TDS on, "Attendance / Production" no longer
    /// paints the <b>e</b> of "Attendance" (Credit Note now claims E) and falls to its <b>n</b>.
    /// </summary>
    [AvaloniaFact]
    public void The_vouchers_column_bare_letters_are_pinned_with_statutory_and_payroll_rows()
    {
        var (window, vm, tempDir) = NewWindow();
        try
        {
            NewCompany(vm, "Hotkey Pin Full Co");
            vm.Company!.Tds = new TdsConfig { Enabled = true };
            vm.Company!.Tcs = new TcsConfig { Enabled = true };
            vm.Company!.PayrollEnabled = true;
            vm.ShowVouchersMenu();

            Assert.Equal(
                "Contra=C Payment=P Receipt=R Journal=J Sales=S Purchase=u " +
                "Credit Note=e Debit Note=D " +
                "Order Vouchers=V Inventory Vouchers=I Other Vouchers=t " +
                "TDS Stat Payment=a TCS Stat Payment=m " +
                "Attendance / Production=n Payroll=l",
                HotKeyMap(vm));

            var column = vm.Columns[^1];
            foreach (var row in column.Items.Where(i => i.IsSelectable))
                Assert.Same(row, column.FindByHotKey(row.HotKey!.Value));
        }
        finally { Close(window, tempDir); }
    }

    private static void Close(MainWindow window, string tempDir)
    {
        window.Close();
        try
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true);
        }
        catch (IOException) { /* best effort */ }
    }
}
