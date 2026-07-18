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
/// WI-3 (master <b>Alteration</b>) + WI-4 (party <b>Mailing Details</b>) at the shell level, driven through the
/// real view models over a throwaway <c>.db</c> — no UI toolkit.
///
/// <para>The load-bearing tests here are the two that catch silent damage rather than loud failure:
/// <see cref="A_no_op_alteration_changes_absolutely_nothing"/> diffs every field of a ledger across an alter that
/// should be neutral — that is what catches an asymmetric <c>LoadFrom</c>, where a field the form forgets to load
/// gets written back as a default and the data is gone with no error; and
/// <see cref="A_rename_through_the_master_propagates_to_a_voucher_posted_BEFORE_it"/> proves the alteration saves
/// against the stable Guid, so history follows the rename instead of breaking.</para>
/// </summary>
public sealed class MasterAlterationViewModelTests : IDisposable
{
    private readonly string _tempDir;
    private readonly CompanyStorage _storage;

    public MasterAlterationViewModelTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "ApexAlterTests_" + Guid.NewGuid().ToString("N"));
        _storage = new CompanyStorage(_tempDir);
    }

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

    /// <summary>Creates a party ledger through the real Ledger master and returns its id.</summary>
    private static Guid CreateParty(MainWindowViewModel vm, string name, Action<LedgerMasterViewModel>? fill = null)
    {
        vm.ShowLedgerMaster();
        var m = vm.LedgerMaster!;
        m.SelectedGroup = m.Groups.First(g => g.Name == "Sundry Debtors");
        m.Name = name;
        fill?.Invoke(m);
        Assert.True(m.Create(), m.Message);
        return vm.Company!.FindLedgerByName(name)!.Id;
    }

    // ================================================================= (1) the no-op alteration

    [Fact]
    public void A_no_op_alteration_changes_absolutely_nothing()
    {
        // Open a ledger for alteration and accept WITHOUT touching anything. Every field must be exactly as it
        // was. This is the test that catches a LoadFrom which forgets a field: the form would show a default, the
        // accept would write that default back, and the value would be silently gone.
        const string companyName = "No-op Alter Co";
        var vm = NewSeededCompany(companyName);

        var id = CreateParty(vm, "Acme Traders", m =>
        {
            m.MaintainBillByBill = true;
            m.DefaultCreditPeriodText = "45";
            m.EnableInterest = true;
            m.InterestRateText = "18";
            m.MailingName = "Acme Traders Pvt Ltd";
            m.MailingAddress = "12 Park Street\nKolkata";
            m.MailingCountry = "India";
            m.MailingPincode = "700019";
            m.MailingState = m.PartyStates.First(s => s.Code == "19");
        });

        var before = Snapshot(vm.Company!.FindLedger(id)!);

        vm.ShowLedgerAlter(id);
        var alter = vm.LedgerMaster!;
        Assert.True(alter.IsAltering);
        Assert.True(alter.Alter(), alter.Message);

        Assert.Equal(before, Snapshot(vm.Company!.FindLedger(id)!));

        // …and it survived the save/reload too.
        Assert.Equal(before, Snapshot(Reload(companyName).FindLedger(id)!));
    }

    /// <summary>Every persisted field of a ledger, rendered as one comparable string.</summary>
    private static string Snapshot(DomainLedger l) => string.Join("|", new[]
    {
        l.Id.ToString(), l.Name, l.GroupId.ToString(),
        l.OpeningBalance.Amount.ToString(), l.OpeningIsDebit.ToString(),
        l.Alias ?? "-", l.IsPredefined.ToString(),
        l.MaintainBillByBill.ToString(), l.DefaultCreditPeriodDays?.ToString() ?? "-",
        l.CostCentresApplicable?.ToString() ?? "-",
        l.EnableChequePrinting.ToString(), l.ChequePrintingBankName ?? "-",
        l.Interest is { } i ? $"{i.Enabled}/{i.RatePercent}/{i.Per}/{i.OnBalance}/{i.Applicability}/{i.Style}" : "-",
        l.CurrencyId?.ToString() ?? "-",
        l.PartyGst is { } g ? $"{g.RegistrationType}/{g.Gstin ?? "-"}/{g.StateCode ?? "-"}/{g.IsPromoter}/{g.IsBodyCorporate}" : "-",
        l.SalesPurchaseGst is null ? "-" : "present",
        l.GstClassification is null ? "-" : "present",
        l.MethodOfAppropriation?.ToString() ?? "-",
        l.DefaultPriceLevelId?.ToString() ?? "-",
        l.TdsApplicable.ToString(), l.TdsNatureOfPaymentId?.ToString() ?? "-",
        l.DeducteeType?.ToString() ?? "-", l.PartyPan ?? "-", l.DeductTdsInSameVoucher.ToString(),
        l.TcsApplicable.ToString(), l.TcsNatureOfGoodsId?.ToString() ?? "-",
        l.CollecteeType?.ToString() ?? "-", l.TdsTcsClassification?.ToString() ?? "-",
        l.Mailing is { } m ? $"{m.MailingName ?? "-"}/{m.Address ?? "-"}/{m.Country ?? "-"}/{m.Pincode ?? "-"}" : "-",
        l.MailingStateCode ?? "-",
    });

    // ================================================================= (2) rename propagates retroactively

    [Fact]
    public void A_rename_through_the_master_propagates_to_a_voucher_posted_BEFORE_it()
    {
        const string companyName = "Rename Propagation Co";
        var vm = NewSeededCompany(companyName);

        var partyId = CreateParty(vm, "Acme Traders");
        var company = vm.Company!;
        var sales = new DomainLedger(
            Guid.NewGuid(), "Sales", company.FindGroupByName("Sales Accounts")!.Id, Money.Zero, false);
        company.AddLedger(sales);

        var journal = company.FindVoucherTypeByName("Journal")!;
        new Apex.Ledger.Services.LedgerService(company).Post(
            new Voucher(Guid.NewGuid(), journal.Id, company.FinancialYearStart, new[]
            {
                new EntryLine(partyId, Money.FromRupees(5000m), DrCr.Debit),
                new EntryLine(sales.Id, Money.FromRupees(5000m), DrCr.Credit),
            }));
        _storage.Save(company);
        var voucherId = company.Vouchers.Single().Id;

        // Alter: rename in place.
        vm.ShowLedgerAlter(partyId);
        var alter = vm.LedgerMaster!;
        alter.Name = "Acme Traders Pvt Ltd";
        Assert.True(alter.Alter(), alter.Message);

        // Reload from disk — the rename is retroactive, and the pre-existing voucher still resolves.
        var reloaded = Reload(companyName);
        Assert.Null(reloaded.FindLedgerByName("Acme Traders"));
        Assert.Equal(partyId, reloaded.FindLedgerByName("Acme Traders Pvt Ltd")!.Id);
        Assert.Single(reloaded.Ledgers, l => l.Id == partyId);   // no duplicate master was created

        var line = reloaded.FindVoucher(voucherId)!.Lines.Single(l => l.LedgerId == partyId);
        Assert.Equal("Acme Traders Pvt Ltd", reloaded.FindLedger(line.LedgerId)!.Name);
    }

    [Fact]
    public void Accepting_an_alteration_without_renaming_succeeds()
    {
        // The except-self uniqueness regression: a create-time check reused on Alter rejects this.
        var vm = NewSeededCompany("Accept As-is Co");
        var id = CreateParty(vm, "Acme Traders");

        vm.ShowLedgerAlter(id);
        var alter = vm.LedgerMaster!;
        alter.DefaultCreditPeriodText = "30";       // change something unrelated; do NOT rename
        Assert.True(alter.Alter(), alter.Message);
        Assert.Equal(30, vm.Company!.FindLedger(id)!.DefaultCreditPeriodDays);
    }

    [Fact]
    public void Renaming_onto_another_ledgers_name_is_rejected_with_a_message()
    {
        var vm = NewSeededCompany("Collision Co");
        var alphaId = CreateParty(vm, "Alpha");
        CreateParty(vm, "Beta");

        vm.ShowLedgerAlter(alphaId);
        var alter = vm.LedgerMaster!;
        alter.Name = "Beta";
        Assert.False(alter.Alter());
        Assert.Contains("already exists", alter.Message!, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("Alpha", vm.Company!.FindLedger(alphaId)!.Name);   // unchanged
    }

    [Fact]
    public void A_reserved_ledger_cannot_be_renamed_through_the_master()
    {
        var vm = NewSeededCompany("Reserved Co");
        var cash = vm.Company!.FindLedgerByName("Cash")!;

        vm.ShowLedgerAlter(cash.Id);
        var alter = vm.LedgerMaster!;
        alter.Name = "Petty Cash";
        Assert.False(alter.Alter());
        Assert.Equal("Cash", vm.Company!.FindLedger(cash.Id)!.Name);
    }

    // ================================================================= (3) Chart of Accounts: keyboard + drill

    [Fact]
    public void Chart_of_accounts_rows_carry_identity_and_the_arrows_move_a_highlight()
    {
        var vm = NewSeededCompany("CoA Keyboard Co");
        var partyId = CreateParty(vm, "Acme Traders");

        vm.ShowChartOfAccounts();
        Assert.Equal(Screen.ChartOfAccounts, vm.CurrentScreen);
        Assert.True(vm.IsChartOfAccountsScreen);

        var chart = vm.ChartOfAccounts!;
        Assert.All(chart.Rows, r => Assert.True(r.IsAlterable));      // every row resolves to a master
        Assert.Contains(chart.Rows, r => r.LedgerId == partyId);

        // Before any keystroke nothing is highlighted, and Enter is inert rather than opening an arbitrary master.
        Assert.Null(chart.HighlightedRow);
        vm.AcceptCurrent();
        Assert.Equal(Screen.ChartOfAccounts, vm.CurrentScreen);

        // Down moves the highlight (and does NOT collapse the page column — the CoA arm runs before the cascade).
        vm.MoveDown();
        Assert.Equal(0, chart.HighlightedIndex);
        Assert.True(chart.Rows[0].IsHighlighted);
        Assert.Equal(Screen.ChartOfAccounts, vm.CurrentScreen);

        vm.MoveDown();
        Assert.Equal(1, chart.HighlightedIndex);
        Assert.False(chart.Rows[0].IsHighlighted);

        // Up from the top wraps to the bottom.
        vm.MoveUp();
        vm.MoveUp();
        Assert.Equal(chart.Rows.Count - 1, chart.HighlightedIndex);
    }

    [Fact]
    public void Enter_on_a_ledger_row_opens_that_ledger_for_alteration()
    {
        var vm = NewSeededCompany("CoA Drill Co");
        var partyId = CreateParty(vm, "Acme Traders");

        vm.ShowChartOfAccounts();
        var chart = vm.ChartOfAccounts!;
        chart.HighlightedIndex = chart.Rows.ToList().FindIndex(r => r.LedgerId == partyId);

        vm.AcceptCurrent();   // Enter / Ctrl+A

        Assert.Equal(Screen.LedgerMaster, vm.CurrentScreen);
        Assert.NotNull(vm.LedgerMaster);
        Assert.True(vm.LedgerMaster!.IsAltering);
        Assert.Equal(partyId, vm.LedgerMaster!.EditingLedger!.Id);
        Assert.Equal("Acme Traders", vm.LedgerMaster!.Name);
        Assert.Equal("Ledger Alteration", vm.Columns[^1].Title);
    }

    [Fact]
    public void Enter_on_a_group_row_opens_that_group_for_alteration()
    {
        var vm = NewSeededCompany("CoA Group Drill Co");
        vm.ShowChartOfAccounts();
        var chart = vm.ChartOfAccounts!;

        var index = chart.Rows.ToList().FindIndex(r => r.GroupId is not null && r.Name == "Current Liabilities");
        Assert.True(index >= 0);
        chart.HighlightedIndex = index;

        vm.AcceptCurrent();

        Assert.Equal(Screen.AccountGroupMaster, vm.CurrentScreen);
        Assert.True(vm.AccountGroupMaster!.IsAltering);
        Assert.Equal("Current Liabilities", vm.AccountGroupMaster!.Name);
        Assert.Equal("Group Alteration", vm.Columns[^1].Title);
    }

    [Fact]
    public void The_tree_refreshes_after_an_alteration_and_keeps_the_highlight_on_the_same_master()
    {
        // A stale snapshot shows the OLD name, which reads as a failed save. The highlight must follow the master
        // by id, not by index — a rename re-sorts the tree.
        var vm = NewSeededCompany("CoA Refresh Co");
        // Two parties, so a rename genuinely RE-SORTS the tree: "Zebra" sits after "Mango", "Alpha" before it.
        // An index-based highlight restore would silently land on Mango; only an id-based one follows the master.
        CreateParty(vm, "Mango Traders");
        var partyId = CreateParty(vm, "Zebra Traders");

        vm.ShowChartOfAccounts();
        var chart = vm.ChartOfAccounts!;
        chart.HighlightedIndex = chart.Rows.ToList().FindIndex(r => r.LedgerId == partyId);
        var indexBefore = chart.HighlightedIndex;

        vm.AcceptCurrent();
        var alter = vm.LedgerMaster!;
        alter.Name = "Alpha Traders";               // sorts to a DIFFERENT position
        Assert.True(alter.Alter(), alter.Message);

        Assert.Contains(chart.Rows, r => r.Name == "Alpha Traders");
        Assert.DoesNotContain(chart.Rows, r => r.Name == "Zebra Traders");
        Assert.Equal(partyId, chart.HighlightedRow!.LedgerId);
        Assert.NotEqual(indexBefore, chart.HighlightedIndex);   // it genuinely moved, and was followed by id
        Assert.True(chart.Rows[chart.HighlightedIndex].IsHighlighted);
    }

    // ================================================================= (4) group alteration

    [Fact]
    public void Altering_a_group_re_parents_it_and_cascades_the_nature_to_descendants()
    {
        const string companyName = "Group Alter Co";
        var vm = NewSeededCompany(companyName);

        vm.ShowAccountGroupMaster();
        var create = vm.AccountGroupMaster!;
        create.SelectedParent = create.ParentOptions.First(g => g.Name == "Current Liabilities");
        create.Name = "Staff Costs";
        Assert.True(create.Create(), create.Message);
        var parentId = vm.Company!.FindGroupByName("Staff Costs")!.Id;

        create.SelectedParent = create.ParentOptions.First(g => g.Id == parentId);
        create.Name = "Bonus Payable";
        Assert.True(create.Create(), create.Message);
        var childId = vm.Company!.FindGroupByName("Bonus Payable")!.Id;

        Assert.Equal(GroupNature.Liability, vm.Company!.FindGroup(childId)!.Nature);

        // Alter the PARENT to sit under an expense head — the child's nature must follow.
        vm.ShowAccountGroupAlter(parentId);
        var alter = vm.AccountGroupMaster!;
        Assert.True(alter.IsAltering);
        Assert.Equal("Staff Costs", alter.Name);
        alter.SelectedParent = alter.ParentOptions.First(g => g.Name == "Indirect Expenses");
        Assert.True(alter.Alter(), alter.Message);

        Assert.Equal(GroupNature.Expense, vm.Company!.FindGroup(parentId)!.Nature);
        Assert.Equal(GroupNature.Expense, vm.Company!.FindGroup(childId)!.Nature);

        var reloaded = Reload(companyName);
        Assert.Equal(GroupNature.Expense, reloaded.FindGroup(childId)!.Nature);
    }

    [Fact]
    public void A_predefined_group_alteration_is_refused_with_a_message()
    {
        var vm = NewSeededCompany("Predefined Group Co");
        var liabilities = vm.Company!.FindGroupByName("Current Liabilities")!;

        vm.ShowAccountGroupAlter(liabilities.Id);
        var alter = vm.AccountGroupMaster!;
        alter.Name = "My Liabilities";
        Assert.False(alter.Alter());
        Assert.Contains("predefined", alter.Message!, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("Current Liabilities", vm.Company!.FindGroup(liabilities.Id)!.Name);
    }

    // ================================================================= (5) stock-item alteration

    [Fact]
    public void Altering_a_stock_item_renames_it_without_re_adding_its_opening_stock()
    {
        const string companyName = "Item Alter Co";
        var vm = NewSeededCompany(companyName);

        // A stock item needs a stock group and a unit; a seeded company ships with neither.
        vm.ShowStockGroupMaster();
        vm.StockGroupMaster!.Name = "Finished Goods";
        Assert.True(vm.StockGroupMaster!.Create(), vm.StockGroupMaster!.Message);

        vm.ShowUnitMaster();
        vm.UnitMaster!.Symbol = "Nos";
        vm.UnitMaster!.FormalName = "Numbers";
        Assert.True(vm.UnitMaster!.Create(), vm.UnitMaster!.Message);

        vm.ShowStockItemMaster();
        var create = vm.StockItemMaster!;
        create.SelectedGroup = create.Groups.First();
        create.SelectedUnit = create.Units.First();
        create.Name = "Widget";
        create.OpeningQuantityText = "10";
        create.OpeningRateText = "100";
        Assert.True(create.Create(), create.Message);

        var itemId = vm.Company!.FindStockItemByName("Widget")!.Id;
        var openingsBefore = vm.Company!.StockOpeningBalances.Count(b => b.StockItemId == itemId);
        Assert.Equal(1, openingsBefore);

        var alterVm = StockItemMasterViewModel.ForAlter(vm.Company!, _storage, itemId, () => { })!;
        Assert.True(alterVm.IsAltering);
        Assert.Equal("Widget", alterVm.Name);
        alterVm.Name = "Widget Mk II";
        Assert.True(alterVm.Alter(), alterVm.Message);

        Assert.Equal("Widget Mk II", vm.Company!.FindStockItem(itemId)!.Name);
        // Accepting the alteration must NOT add a second opening allocation.
        Assert.Equal(openingsBefore, vm.Company!.StockOpeningBalances.Count(b => b.StockItemId == itemId));

        // Alter again — still exactly one opening, and the except-self name check permits the no-op accept.
        Assert.True(alterVm.Alter(), alterVm.Message);
        Assert.Equal(openingsBefore, vm.Company!.StockOpeningBalances.Count(b => b.StockItemId == itemId));

        var reloaded = Reload(companyName);
        Assert.Equal("Widget Mk II", reloaded.FindStockItem(itemId)!.Name);
    }

    // ================================================================= (6) WI-4 mailing details

    [Fact]
    public void The_mailing_block_shows_for_a_NESTED_party_group_and_hides_for_a_non_party_group()
    {
        // The gate walks the full ancestry, not just the direct parent — a sub-group of Sundry Debtors is still
        // a party.
        var vm = NewSeededCompany("Mailing Gate Co");

        vm.ShowAccountGroupMaster();
        var groups = vm.AccountGroupMaster!;
        groups.SelectedParent = groups.ParentOptions.First(g => g.Name == "Sundry Debtors");
        groups.Name = "North Zone Debtors";
        Assert.True(groups.Create(), groups.Message);

        vm.ShowLedgerMaster();
        var m = vm.LedgerMaster!;

        m.SelectedGroup = m.Groups.First(g => g.Name == "North Zone Debtors");
        Assert.True(m.ShowMailingDetails);                      // nested party group

        m.SelectedGroup = m.Groups.First(g => g.Name == "Sundry Creditors");
        Assert.True(m.ShowMailingDetails);                      // the creditor half of the CA's ask

        m.SelectedGroup = m.Groups.First(g => g.Name == "Indirect Expenses");
        Assert.False(m.ShowMailingDetails);                     // not a party
    }

    [Fact]
    public void The_mailing_name_defaults_from_the_ledger_name_until_it_is_edited_by_hand()
    {
        var vm = NewSeededCompany("Mailing Name Co");
        vm.ShowLedgerMaster();
        var m = vm.LedgerMaster!;
        m.SelectedGroup = m.Groups.First(g => g.Name == "Sundry Debtors");

        m.Name = "Acme";
        Assert.Equal("Acme", m.MailingName);
        m.Name = "Acme Traders";
        Assert.Equal("Acme Traders", m.MailingName);

        m.MailingName = "Acme Traders Private Limited";   // a deliberate edit
        m.Name = "Acme Trading Co";
        Assert.Equal("Acme Traders Private Limited", m.MailingName);   // no longer tracks
    }

    [Fact]
    public void The_mailing_State_and_the_GST_State_are_one_field_in_the_view_model_too()
    {
        var vm = NewSeededCompany("One State Co");
        vm.ShowLedgerMaster();
        var m = vm.LedgerMaster!;
        m.SelectedGroup = m.Groups.First(g => g.Name == "Sundry Debtors");

        m.MailingState = m.PartyStates.First(s => s.Code == "19");
        Assert.Same(m.MailingState, m.PartyState);

        m.PartyState = m.PartyStates.First(s => s.Code == "27");
        Assert.Same(m.PartyState, m.MailingState);

        // …and it lands on the ONE stored value that drives place of supply.
        m.Name = "Acme";
        Assert.True(m.Create(), m.Message);
        var saved = vm.Company!.FindLedgerByName("Acme")!;
        Assert.Equal("27", saved.PartyGst!.StateCode);
        Assert.Equal("27", saved.MailingStateCode);
    }

    [Fact]
    public void A_bad_PIN_is_rejected_with_a_message_and_nothing_is_created()
    {
        var vm = NewSeededCompany("Bad PIN Co");
        vm.ShowLedgerMaster();
        var m = vm.LedgerMaster!;
        m.SelectedGroup = m.Groups.First(g => g.Name == "Sundry Debtors");
        m.Name = "Acme";
        m.MailingPincode = "12";

        Assert.False(m.Create());
        Assert.Contains("PIN", m.Message!, StringComparison.OrdinalIgnoreCase);
        Assert.Null(vm.Company!.FindLedgerByName("Acme"));   // nothing half-built was added
    }

    [Fact]
    public void A_party_with_no_mailing_input_stores_no_block_at_all()
    {
        // ER-13 at the screen: merely rendering the section must not start persisting an empty block.
        var vm = NewSeededCompany("Empty Block Co");
        vm.ShowLedgerMaster();
        var m = vm.LedgerMaster!;
        m.SelectedGroup = m.Groups.First(g => g.Name == "Sundry Debtors");
        m.MailingCountry = string.Empty;
        m.Name = "Plain Party";

        Assert.True(m.Create(), m.Message);
        Assert.Null(vm.Company!.FindLedgerByName("Plain Party")!.Mailing);
    }

    [Fact]
    public void Mailing_details_captured_at_create_reload_into_the_alteration_form()
    {
        const string companyName = "Mailing Alter Co";
        var vm = NewSeededCompany(companyName);

        var id = CreateParty(vm, "Naresh Traders", m =>
        {
            m.MailingName = "Naresh Traders Pvt Ltd";
            m.MailingAddress = "12 Park Street\nKolkata";
            m.MailingCountry = "India";
            m.MailingPincode = "700019";
            m.MailingState = m.PartyStates.First(s => s.Code == "19");
        });

        vm.ShowLedgerAlter(id);
        var alter = vm.LedgerMaster!;
        Assert.Equal("Naresh Traders Pvt Ltd", alter.MailingName);
        Assert.Equal("12 Park Street\nKolkata", alter.MailingAddress);
        Assert.Equal("India", alter.MailingCountry);
        Assert.Equal("700019", alter.MailingPincode);
        Assert.Equal("19", alter.MailingState!.Code);

        // Correct the PIN — the CA's scenario ("a user who mistypes a PIN cannot fix it" was the gap).
        alter.MailingPincode = "700020";
        Assert.True(alter.Alter(), alter.Message);

        Assert.Equal("700020", Reload(companyName).FindLedger(id)!.Mailing!.Pincode);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true); }
        catch { /* best effort */ }
    }
}
