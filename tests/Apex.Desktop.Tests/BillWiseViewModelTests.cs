using System;
using System.IO;
using System.Linq;
using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Desktop.Services;
using Apex.Desktop.ViewModels;
using DomainLedger = Apex.Ledger.Domain.Ledger;

namespace Apex.Desktop.Tests;

/// <summary>
/// End-to-end coverage for the Bill-wise UI surfaced in the cascade (catalog §5): a party ledger is
/// created with "Maintain bill-by-bill" + a default credit period; a Sales voucher captures a New-Ref
/// bill-wise allocation and posts through the engine + SQLite; the Outstandings (Receivables) page shows
/// the open bill with its due date + pending + ageing; spacebar multi-select + Ctrl+B settle it through
/// the engine and it disappears; and every path keeps the cascade correct (Outstandings opens as ONE page
/// column that REPLACES any prior page). Drives the real shell VMs over a throwaway .db — no UI toolkit.
/// </summary>
public sealed class BillWiseViewModelTests : IDisposable
{
    private readonly string _tempDir;
    private readonly CompanyStorage _storage;

    public BillWiseViewModelTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "ApexBillWiseTests_" + Guid.NewGuid().ToString("N"));
        _storage = new CompanyStorage(_tempDir);
    }

    // ---------------------------------------------------------------- helpers

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

    /// <summary>Creates a bill-wise "Robert Traders" debtor with a 30-day default credit period.</summary>
    private DomainLedger CreatePartyDebtor(MainWindowViewModel vm, string name = "Robert Traders", int creditDays = 30)
    {
        vm.ShowLedgerMaster();
        var master = vm.LedgerMaster!;
        master.Name = name;
        master.SelectedGroup = vm.Company!.FindGroupByName("Sundry Debtors");

        // Selecting a party group auto-turns on bill-by-bill (party default) and reveals the prompts.
        Assert.True(master.IsPartyGroup);
        Assert.True(master.MaintainBillByBill);
        master.DefaultCreditPeriodText = creditDays.ToString();

        Assert.True(master.Create());
        var party = vm.Company!.FindLedgerByName(name);
        Assert.NotNull(party);
        Assert.True(party!.MaintainBillByBill);
        Assert.Equal(creditDays, party.DefaultCreditPeriodDays);
        return party;
    }

    /// <summary>
    /// Posts a Sales voucher: Dr party (bill-wise New-Ref) 50000 / Cr Sales 50000, capturing a single
    /// bill-wise allocation on the party line. Returns the entry VM after Accept.
    /// </summary>
    private void PostBillWiseSale(MainWindowViewModel vm, DomainLedger party, string billRef, decimal amount)
    {
        // A revenue ledger to credit.
        vm.ShowLedgerMaster();
        var master = vm.LedgerMaster!;
        if (vm.Company!.FindLedgerByName("Sales A/c") is null)
        {
            master.Name = "Sales A/c";
            master.SelectedGroup = vm.Company!.FindGroupByName("Sales Accounts");
            Assert.True(master.Create());
        }
        var sales = vm.Company!.FindLedgerByName("Sales A/c")!;

        vm.OpenVoucher(VoucherBaseType.Sales);
        var entry = vm.VoucherEntry!;

        // Line 0 = Dr party (bill-wise) ; line 1 = Cr Sales.
        var l0 = entry.Lines[0];
        l0.SelectedLedger = party;
        l0.Side = DrCr.Debit;
        l0.AmountText = amount.ToString();

        // Selecting a bill-wise ledger turns the sub-panel on and seeds a first New-Ref row.
        Assert.True(l0.IsBillWise);
        Assert.Single(l0.BillAllocations);
        var alloc = l0.BillAllocations[0];
        Assert.Equal(BillRefType.NewRef, alloc.RefType);
        alloc.Name = billRef;
        alloc.AmountText = amount.ToString();
        Assert.True(l0.BillSplitOk);

        var l1 = entry.Lines[1];
        l1.SelectedLedger = sales;
        l1.Side = DrCr.Credit;
        l1.AmountText = amount.ToString();

        Assert.True(entry.CanAccept);
        Assert.True(entry.Accept());
        Assert.Equal(Screen.Gateway, vm.CurrentScreen);
    }

    // ---------------------------------------------------------------- (1) capture posts a bill

    [Fact]
    public void Party_billwise_capture_posts_a_bill_and_persists_the_allocation()
    {
        const string companyName = "BillWise Post Co";
        var vm = NewSeededCompany(companyName);
        var party = CreatePartyDebtor(vm);

        PostBillWiseSale(vm, party, "INV-001", 50000m);

        // Posted in memory: the party line carries exactly one New-Ref allocation of 50000.
        var salesType = vm.Company!.VoucherTypes.Single(t => t.BaseType == VoucherBaseType.Sales);
        var posted = vm.Company!.Vouchers.Single(v => v.TypeId == salesType.Id);
        var partyLine = posted.Lines.Single(l => l.LedgerId == party.Id);
        Assert.True(partyLine.HasBillAllocations);
        var a = Assert.Single(partyLine.BillAllocations);
        Assert.Equal(BillRefType.NewRef, a.RefType);
        Assert.Equal("INV-001", a.Name);
        Assert.Equal(50000m, a.Amount.Amount);

        // PERSISTED: reload the .db and the allocation survives (schema v2 bill_allocations).
        var reloaded = Reload(companyName);
        var reloadedParty = reloaded.FindLedgerByName("Robert Traders")!;
        Assert.True(reloadedParty.MaintainBillByBill);
        var reloadedType = reloaded.VoucherTypes.Single(t => t.BaseType == VoucherBaseType.Sales);
        var reloadedVoucher = reloaded.Vouchers.Single(v => v.TypeId == reloadedType.Id);
        var reloadedLine = reloadedVoucher.Lines.Single(l => l.LedgerId == reloadedParty.Id);
        Assert.Equal("INV-001", Assert.Single(reloadedLine.BillAllocations).Name);
    }

    // ---------------------------------------------------------------- (2) Outstandings shows it

    [Fact]
    public void Outstandings_receivables_shows_the_open_bill_with_due_date_and_pending()
    {
        var vm = NewSeededCompany("BillWise Outstanding Co");
        var party = CreatePartyDebtor(vm, creditDays: 30);
        PostBillWiseSale(vm, party, "INV-777", 42000m);

        vm.OpenOutstandings(OutstandingsKind.Receivables);
        Assert.Equal(Screen.Outstandings, vm.CurrentScreen);
        var os = vm.Outstandings!;

        var row = Assert.Single(os.Rows);
        Assert.Equal("Robert Traders", row.Party);
        Assert.Equal("INV-777", row.Reference);
        // Pending is the full 42,000 (Indian grouping), and a due date is present.
        Assert.Contains("42,000", row.Pending);
        Assert.False(string.IsNullOrWhiteSpace(row.DueDate));
        // Payables side is empty (this is a debtor).
        vm.OpenOutstandings(OutstandingsKind.Payables);
        Assert.Empty(vm.Outstandings!.Rows);
    }

    // ---------------------------------------------------------------- (3) Ctrl+B settles it

    [Fact]
    public void Spacebar_select_then_CtrlB_settles_the_bill_and_it_disappears()
    {
        const string companyName = "BillWise Settle Co";
        var vm = NewSeededCompany(companyName);
        var party = CreatePartyDebtor(vm);
        PostBillWiseSale(vm, party, "INV-900", 30000m);

        vm.OpenOutstandings(OutstandingsKind.Receivables);
        var os = vm.Outstandings!;
        Assert.Single(os.Rows);
        Assert.True(vm.IsOutstandingsScreen);

        // Highlight is on the first row; spacebar selects it, Ctrl+B settles.
        Assert.Equal(0, os.HighlightedIndex);
        vm.ToggleOutstandingSelection();
        Assert.True(os.Rows[0].IsSelected);
        Assert.Single(os.SelectedRows);

        vm.SettleBills();

        // The bill is knocked off — the row is gone and the report is empty.
        Assert.Empty(os.Rows);
        Assert.Contains("Settled", os.Message);

        // The settlement voucher (a Receipt: Dr Cash / Cr party bill-wise Agst) posted + persisted.
        var receiptType = vm.Company!.VoucherTypes.Single(t => t.BaseType == VoucherBaseType.Receipt);
        var settlement = vm.Company!.Vouchers.Single(v => v.TypeId == receiptType.Id);
        var partyLine = settlement.Lines.Single(l => l.LedgerId == party.Id);
        Assert.Equal(BillRefType.AgstRef, Assert.Single(partyLine.BillAllocations).RefType);

        // Reloading the .db confirms the bill is settled (no open receivable remains).
        var reloaded = Reload(companyName);
        var report = Apex.Ledger.Reports.Outstandings.Build(reloaded, DomainAsOf(reloaded));
        Assert.Empty(report.Receivables);
    }

    private static DateOnly DomainAsOf(Company c)
    {
        DateOnly? last = null;
        foreach (var v in c.Vouchers)
            if (last is null || v.Date > last.Value) last = v.Date;
        return last ?? c.FinancialYearStart.AddYears(1).AddDays(-1);
    }

    // ---------------------------------------------------------------- (4) cascade correctness

    [Fact]
    public void Outstandings_nests_under_reports_and_opens_as_a_single_page_column_that_replaces()
    {
        var vm = NewSeededCompany("BillWise Nav Co");
        var party = CreatePartyDebtor(vm);
        PostBillWiseSale(vm, party, "INV-1", 10000m);

        // Reports → Statements of Accounts is a Group item that opens a submenu column.
        vm.ShowOutstandingsMenu();
        Assert.Equal(GatewayMenu.Outstandings, vm.CurrentGatewayMenu);
        var submenu = vm.Columns[^1];
        Assert.True(submenu.IsMenu);
        var labels = submenu.Items.Where(m => m.IsSelectable).Select(m => m.Label).ToArray();
        Assert.Equal(new[] { "Receivables", "Payables" }, labels);

        // Drilling into Receivables adds exactly ONE page column beside the menu columns.
        // (highlight Receivables — it's the first selectable — then drill in)
        vm.DrillIn();
        Assert.Equal(1, vm.Columns.Count(c => c.IsPage));
        Assert.True(vm.Columns[^1].IsPage);
        Assert.NotNull(vm.Outstandings);
        Assert.Same(vm.Outstandings, vm.Columns[^1].Outstanding);
        Assert.Equal(OutstandingsKind.Receivables, vm.Outstandings!.Kind);

        // Opening another page (Balance Sheet) REPLACES the Outstandings page — still one page column.
        vm.OpenReport(ReportKind.BalanceSheet);
        Assert.Equal(1, vm.Columns.Count(c => c.IsPage));
        Assert.Null(vm.Outstandings);
        Assert.NotNull(vm.Reports);

        // And opening Outstandings again replaces the report page — still exactly one page column.
        vm.OpenOutstandings(OutstandingsKind.Payables);
        Assert.Equal(1, vm.Columns.Count(c => c.IsPage));
        Assert.Null(vm.Reports);
        Assert.Equal(OutstandingsKind.Payables, vm.Outstandings!.Kind);
    }

    // ---------------------------------------------------------------- (5) split enforcement

    [Fact]
    public void Billwise_split_must_sum_to_the_line_amount_or_accept_is_blocked()
    {
        var vm = NewSeededCompany("BillWise Split Co");
        var party = CreatePartyDebtor(vm);

        vm.ShowLedgerMaster();
        var master = vm.LedgerMaster!;
        master.Name = "Sales A/c";
        master.SelectedGroup = vm.Company!.FindGroupByName("Sales Accounts");
        Assert.True(master.Create());
        var sales = vm.Company!.FindLedgerByName("Sales A/c")!;

        vm.OpenVoucher(VoucherBaseType.Sales);
        var entry = vm.VoucherEntry!;

        var l0 = entry.Lines[0];
        l0.SelectedLedger = party;
        l0.Side = DrCr.Debit;
        l0.AmountText = "50000";
        // Under-allocate the bill (only 20000 of 50000) → split invalid → accept blocked.
        l0.BillAllocations[0].Name = "INV-A";
        l0.BillAllocations[0].AmountText = "20000";
        Assert.False(l0.BillSplitOk);

        entry.Lines[1].SelectedLedger = sales;
        entry.Lines[1].Side = DrCr.Credit;
        entry.Lines[1].AmountText = "50000";

        Assert.False(entry.CanAccept);          // balanced Dr=Cr, but the bill split is short
        Assert.False(entry.Accept());
        Assert.Equal(Screen.VoucherEntry, vm.CurrentScreen);

        // Fix the split (add a second bill for the remaining 30000) → now valid and accepts.
        vm.AddBillAllocation(l0);
        l0.BillAllocations[1].Name = "INV-B";
        l0.BillAllocations[1].AmountText = "30000";
        Assert.True(l0.BillSplitOk);
        Assert.True(entry.CanAccept);
        Assert.True(entry.Accept());

        // Two bills opened for the party.
        vm.OpenOutstandings(OutstandingsKind.Receivables);
        Assert.Equal(2, vm.Outstandings!.Rows.Count);
    }

    public void Dispose()
    {
        try
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true);
        }
        catch (IOException) { /* best effort */ }
    }
}
