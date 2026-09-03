using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Reports;
using Apex.Ledger.Services;
using Apex.Desktop.Services;
using Apex.Desktop.ViewModels;
using DomainLedger = Apex.Ledger.Domain.Ledger;

namespace Apex.Desktop.Tests;

/// <summary>
/// CORE-1 (A) — <b>Opening Balance on the Ledger master</b>. The domain (<see cref="DomainLedger.OpeningBalance"/> /
/// <see cref="DomainLedger.OpeningIsDebit"/> / <see cref="DomainLedger.SignedOpening"/>) and the SQLite store
/// (<c>ledgers.opening_balance_paisa</c> / <c>opening_is_debit</c>, both since v1) have always carried an opening
/// balance — but the Ledger CREATION screen never captured one, hard-coding <see cref="Money.Zero"/>. A set of books
/// therefore could not be OPENED: an accountant migrating onto the app had no way to state what each ledger was
/// carrying on day one, so the first Balance Sheet was structurally empty. This is the UI gap, and these tests are
/// the contract for closing it.
///
/// <para>Corpus: TallyPrime Study Guide pp.65–66 ("Single Ledger Creation" — Name, alias, Under, then "Enter the
/// Opening Balance (if required) along with Dr/Cr depending on the nature"). The Opening Balance is the LAST field
/// of the ledger screen and it is NOT feature-gated — no F11 capability and no F12 visibility toggle stands between
/// the operator and it, so all four config layers permit it unconditionally.</para>
///
/// <para>Every money fixture here is ODD-PAISA on purpose. A round-rupee opening asserts almost nothing: a ±0.50
/// defect survived this project's entire life under six round-number assertions, so an opening-balance suite that
/// only ever posts ₹50,000.00 would not notice the store truncating, the parser dropping the tail, or the report
/// re-rounding.</para>
/// </summary>
public sealed class LedgerOpeningBalanceViewModelTests : IDisposable
{
    private readonly string _tempDir;
    private readonly CompanyStorage _storage;

    public LedgerOpeningBalanceViewModelTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "ApexOpeningBalTests_" + Guid.NewGuid().ToString("N"));
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

    /// <summary>Walks the ACTIVE cascade column with Down until the highlighted row is <paramref name="label"/>.</summary>
    private static void SelectActiveItem(MainWindowViewModel vm, string label)
    {
        for (var i = 0; i < vm.Menu.Count + 2; i++)
        {
            if (vm.Menu[vm.SelectedIndex].Label == label) return;
            vm.MoveDown();
        }
        Assert.Fail($"menu item '{label}' was not reachable by arrow navigation");
    }

    // ---------------------------------------------------------------- (1) the field exists and is captured

    /// <summary>
    /// The core gap. A ledger created through the master with an opening typed into the screen carries that exact
    /// paisa amount — and it survives a full save/reload through SQLite, which is what makes it a set of BOOKS and
    /// not a session variable.
    /// </summary>
    [Fact]
    public void A_ledger_created_with_a_typed_opening_balance_stores_it_to_the_paisa_and_survives_reload()
    {
        var vm = NewSeededCompany("Opening Capture Co");
        var master = new LedgerMasterViewModel(vm.Company!, _storage, onChanged: () => { });

        master.Name = "Bank of Baroda";
        master.SelectedGroup = vm.Company!.Groups.Single(g => g.Name == "Bank Accounts");
        master.OpeningBalanceText = "41237.53";      // odd paisa — a round figure would assert nothing
        master.OpeningIsDebit = true;                // an asset opens on the debit side

        Assert.True(master.Create(), master.Message);

        var reloaded = Reload("Opening Capture Co");
        var ledger = reloaded.Ledgers.Single(l => l.Name == "Bank of Baroda");
        Assert.Equal(41237.53m, ledger.OpeningBalance.Amount);
        Assert.True(ledger.OpeningIsDebit);
        Assert.Equal(41237.53m, ledger.SignedOpening);
    }

    /// <summary>
    /// The credit side. A supplier's opening is a CREDIT, and the sign flip is what makes the sheet balance —
    /// storing the magnitude without the side would silently turn every creditor into a debtor.
    /// </summary>
    [Fact]
    public void A_credit_opening_is_stored_on_the_credit_side_and_signs_negative()
    {
        var vm = NewSeededCompany("Credit Opening Co");
        var master = new LedgerMasterViewModel(vm.Company!, _storage, onChanged: () => { });

        master.Name = "Nandi Traders";
        master.SelectedGroup = vm.Company!.Groups.Single(g => g.Name == "Sundry Creditors");
        master.OpeningBalanceText = "18904.27";
        master.OpeningIsDebit = false;

        Assert.True(master.Create(), master.Message);

        var ledger = Reload("Credit Opening Co").Ledgers.Single(l => l.Name == "Nandi Traders");
        Assert.Equal(18904.27m, ledger.OpeningBalance.Amount);
        Assert.False(ledger.OpeningIsDebit);
        Assert.Equal(-18904.27m, ledger.SignedOpening);
    }

    /// <summary>
    /// Corpus: "along with Dr/Cr <b>depending on the nature</b>" (Study Guide p.66). The side is PROPOSED from the
    /// chosen group's nature — Asset/Expense ⇒ Dr, Liability/Income ⇒ Cr — so the common case needs no thought.
    /// </summary>
    [Fact]
    public void The_opening_side_defaults_from_the_group_nature_and_follows_the_group_until_overridden()
    {
        var vm = NewSeededCompany("Opening Side Co");
        var master = new LedgerMasterViewModel(vm.Company!, _storage, onChanged: () => { });

        master.SelectedGroup = vm.Company!.Groups.Single(g => g.Name == "Bank Accounts");   // Asset
        Assert.True(master.OpeningIsDebit);

        master.SelectedGroup = vm.Company.Groups.Single(g => g.Name == "Sundry Creditors"); // Liability
        Assert.False(master.OpeningIsDebit);

        master.SelectedGroup = vm.Company.Groups.Single(g => g.Name == "Indirect Expenses"); // Expense
        Assert.True(master.OpeningIsDebit);

        // Once the operator states the side by hand it STOPS tracking the group — a deliberate "contra" opening
        // (an overdrawn bank, a debit balance with a creditor) must not be silently flipped back by a later
        // re-pick of the Under group.
        master.OpeningIsDebit = false;
        master.SelectedGroup = vm.Company.Groups.Single(g => g.Name == "Bank Accounts");     // Asset again
        Assert.False(master.OpeningIsDebit);
    }

    /// <summary>
    /// The screen's Dr/Cr picker binds to <c>OpeningSide</c> (a <see cref="DrCr"/>, the same type and control the
    /// voucher line uses) while the domain stores a bool. They must be ONE value seen two ways — a picker that
    /// could drift from the stored side would show "Dr" over a credit opening, the most dangerous possible lie on
    /// this screen. Both directions are asserted, plus the property-change that keeps the picker in step when the
    /// side moves underneath it.
    /// </summary>
    [Fact]
    public void The_Dr_Cr_picker_and_the_stored_side_are_one_value_seen_two_ways()
    {
        var vm = NewSeededCompany("Opening Side Projection Co");
        var master = new LedgerMasterViewModel(vm.Company!, _storage, onChanged: () => { });

        var raised = new List<string?>();
        master.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        // bool → picker, including the nature proposal that fires when the group changes.
        master.SelectedGroup = vm.Company!.Groups.Single(g => g.Name == "Bank Accounts");   // Asset ⇒ Dr
        Assert.True(master.OpeningIsDebit);
        Assert.Equal(DrCr.Debit, master.OpeningSide);

        // The notification is asserted over a real Dr→Cr transition. (Bank Accounts above could not test it: the
        // screen opens on Sundry Debtors, also an Asset, so the side never actually moved — and a no-op assignment
        // correctly raises nothing.)
        raised.Clear();
        master.SelectedGroup = vm.Company.Groups.Single(g => g.Name == "Sundry Creditors"); // Liability ⇒ Cr
        Assert.Equal(DrCr.Credit, master.OpeningSide);
        Assert.Contains(nameof(LedgerMasterViewModel.OpeningSide), raised);

        // picker → bool, and picking a side by hand LATCHES it exactly as setting the bool does.
        master.OpeningSide = DrCr.Credit;
        Assert.False(master.OpeningIsDebit);
        master.SelectedGroup = vm.Company.Groups.Single(g => g.Name == "Indirect Expenses"); // Expense ⇒ would be Dr
        Assert.Equal(DrCr.Credit, master.OpeningSide);

        // ...and the latched side is what actually gets stored.
        master.Name = "Overdrawn Bank";
        master.SelectedGroup = vm.Company.Groups.Single(g => g.Name == "Bank Accounts");
        master.OpeningSide = DrCr.Credit;
        master.OpeningBalanceText = "5140.73";
        Assert.True(master.Create(), master.Message);

        var ledger = Reload("Opening Side Projection Co").Ledgers.Single(l => l.Name == "Overdrawn Bank");
        Assert.False(ledger.OpeningIsDebit);
        Assert.Equal(-5140.73m, ledger.SignedOpening);
    }

    /// <summary>
    /// Regression: CONFIRMING the proposed side latches it just as overriding does. The first cut of this feature
    /// latched inside an <c>[ObservableProperty]</c> change callback, which the generated setter skips when the
    /// assigned value already equals the current one — so an operator who picked "Cr" on a Sundry Creditors ledger
    /// (where Cr was already proposed) left the side UNLATCHED, and correcting the group afterwards silently flipped
    /// their stated Cr to Dr: a 2× restatement of the opening, with no message. Confirming a proposal is a
    /// statement, not a no-op.
    /// </summary>
    [Fact]
    public void Confirming_the_already_proposed_side_latches_it_against_a_later_group_change()
    {
        var vm = NewSeededCompany("Opening Confirm Latch Co");
        var master = new LedgerMasterViewModel(vm.Company!, _storage, onChanged: () => { });

        master.SelectedGroup = vm.Company!.Groups.Single(g => g.Name == "Sundry Creditors"); // Liability ⇒ proposes Cr
        Assert.False(master.OpeningIsDebit);

        master.OpeningIsDebit = false;   // the operator CONFIRMS Cr — same value, but now a stated fact
        master.SelectedGroup = vm.Company.Groups.Single(g => g.Name == "Bank Accounts");     // Asset ⇒ would propose Dr

        Assert.False(master.OpeningIsDebit);
        Assert.Equal(DrCr.Credit, master.OpeningSide);
    }

    // ---------------------------------------------------------------- (2) it reaches the opening statements

    /// <summary>
    /// The point of the whole feature: with NO vouchers posted at all, the opening balances alone must produce a
    /// Trial Balance that foots, and a Balance Sheet that balances. This is "opening a set of books".
    /// </summary>
    [Fact]
    public void Openings_alone_with_no_vouchers_produce_a_footing_trial_balance_and_a_balancing_sheet()
    {
        var vm = NewSeededCompany("Opening Books Co");
        var company = vm.Company!;

        // A deliberately odd-paisa opening set that foots exactly: assets 63,481.19 + 18,904.27 = 82,385.46
        // against capital 82,385.46. Nothing here is round, so a truncation anywhere shows up as a mismatch.
        Create(company, "Bank of Baroda", "Bank Accounts", "63481.19", debit: true);
        Create(company, "Nandi Traders", "Sundry Debtors", "18904.27", debit: true);
        Create(company, "Opening Capital", "Capital Account", "82385.46", debit: false);

        var asOf = new DateOnly(2026, 4, 1);
        var tb = TrialBalance.Build(company, asOf);

        Assert.Equal(82385.46m, tb.TotalDebit.Amount);
        Assert.Equal(82385.46m, tb.TotalCredit.Amount);

        var bank = tb.Rows.Single(r => r.LedgerName == "Bank of Baroda");
        Assert.Equal(63481.19m, bank.Debit.Amount);
        Assert.Equal(0m, bank.Credit.Amount);

        var capital = tb.Rows.Single(r => r.LedgerName == "Opening Capital");
        Assert.Equal(82385.46m, capital.Credit.Amount);
        Assert.Equal(0m, capital.Debit.Amount);

        var bs = BalanceSheet.Build(company, asOf);
        Assert.True(bs.Balanced, $"assets {bs.TotalAssets.Amount} != liabilities {bs.TotalLiabilities.Amount}");
        Assert.Equal(82385.46m, bs.TotalAssets.Amount);
        Assert.Equal(82385.46m, bs.TotalLiabilities.Amount);
    }

    /// <summary>
    /// An opening must be the STARTING point, not a replacement for movement: a voucher posted after the opening
    /// moves the closing balance off the opening by exactly the voucher amount. A defect that stored the opening
    /// but then let the closing computation overwrite it would pass the report-shape test above and fail here.
    /// </summary>
    [Fact]
    public void A_voucher_moves_the_closing_balance_off_the_opening_by_exactly_its_amount()
    {
        var vm = NewSeededCompany("Opening Plus Movement Co");
        var company = vm.Company!;

        var cashId = Create(company, "Petty Cash", "Cash-in-Hand", "7311.83", debit: true);
        var salesId = Create(company, "Opening Capital", "Capital Account", "7311.83", debit: false);

        // Spend 1,204.37 of the petty cash on an indirect expense.
        var expenseId = Create(company, "Courier Charges", "Indirect Expenses", string.Empty, debit: true);
        var type = company.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Payment && t.IsActive);
        new LedgerService(company).Post(new Voucher(
            Guid.NewGuid(), type.Id, new DateOnly(2026, 5, 9),
            new[]
            {
                new EntryLine(expenseId, Money.FromRupees(1204.37m), DrCr.Debit),
                new EntryLine(cashId, Money.FromRupees(1204.37m), DrCr.Credit),
            }));
        _storage.Save(company);

        var closing = LedgerBalances.SignedClosing(company, company.FindLedger(cashId)!, new DateOnly(2026, 5, 31));
        Assert.Equal(7311.83m - 1204.37m, closing);
        Assert.Equal(6107.46m, closing);

        // The opening itself is untouched by the posting — it is a stated fact about day one, not a running total.
        Assert.Equal(7311.83m, company.FindLedger(cashId)!.OpeningBalance.Amount);
        Assert.Equal(7311.83m, company.FindLedger(salesId)!.OpeningBalance.Amount);
    }

    // ---------------------------------------------------------------- (3) the alteration round-trip

    /// <summary>
    /// The round-trip that the ledger master's own comment warns about: "an omission [in LoadFrom] shows the user a
    /// default, and accepting then writes that default back — silent data loss". Now that the screen OWNS the
    /// opening, altering an UNRELATED field must not zero it.
    /// </summary>
    [Fact]
    public void Altering_an_unrelated_field_preserves_the_stored_opening_balance()
    {
        var vm = NewSeededCompany("Opening Alter Co");
        var company = vm.Company!;
        var id = Create(company, "Nandi Traders", "Sundry Creditors", "18904.27", debit: false);

        var alter = LedgerMasterViewModel.ForAlter(company, _storage, id, onChanged: () => { });
        Assert.NotNull(alter);

        // The stored opening is LOADED back into the form — not shown as blank.
        Assert.Equal("18904.27", alter!.OpeningBalanceText);
        Assert.False(alter.OpeningIsDebit);

        alter.Name = "Nandi Traders & Sons";     // rename only
        Assert.True(alter.Alter(), alter.Message);

        var reloaded = Reload("Opening Alter Co").Ledgers.Single(l => l.Name == "Nandi Traders & Sons");
        Assert.Equal(18904.27m, reloaded.OpeningBalance.Amount);
        Assert.False(reloaded.OpeningIsDebit);
    }

    /// <summary>
    /// Migration reality: the first opening entered is very often wrong, and the accountant must be able to correct
    /// it from the same screen (Study Guide p.66 — the alteration screen IS the creation screen, pre-filled).
    /// </summary>
    [Fact]
    public void An_opening_balance_can_be_corrected_through_the_alteration_screen()
    {
        var vm = NewSeededCompany("Opening Correct Co");
        var company = vm.Company!;
        var id = Create(company, "Bank of Baroda", "Bank Accounts", "41237.53", debit: true);

        var alter = LedgerMasterViewModel.ForAlter(company, _storage, id, onChanged: () => { });
        alter!.OpeningBalanceText = "39118.61";
        Assert.True(alter.Alter(), alter.Message);

        var reloadedCompany = Reload("Opening Correct Co");
        var reloaded = reloadedCompany.Ledgers.Single(l => l.Name == "Bank of Baroda");
        Assert.Equal(39118.61m, reloaded.OpeningBalance.Amount);
        Assert.True(reloaded.OpeningIsDebit);

        // ...and the corrected figure is what the opening Trial Balance now reports.
        var tb = TrialBalance.Build(reloadedCompany, new DateOnly(2026, 4, 1));
        Assert.Equal(39118.61m, tb.Rows.Single(r => r.LedgerName == "Bank of Baroda").Debit.Amount);
    }

    /// <summary>
    /// The Dr/Cr side is correctable too — an opening booked on the wrong side is a 2× error, not a sign nit.
    /// </summary>
    [Fact]
    public void Correcting_the_opening_side_flips_the_signed_opening_by_twice_the_magnitude()
    {
        var vm = NewSeededCompany("Opening Side Fix Co");
        var company = vm.Company!;
        var id = Create(company, "Nandi Traders", "Sundry Debtors", "18904.27", debit: true);
        Assert.Equal(18904.27m, company.FindLedger(id)!.SignedOpening);

        var alter = LedgerMasterViewModel.ForAlter(company, _storage, id, onChanged: () => { });
        alter!.OpeningIsDebit = false;
        Assert.True(alter.Alter(), alter.Message);

        var reloaded = Reload("Opening Side Fix Co").Ledgers.Single(l => l.Name == "Nandi Traders");
        Assert.Equal(-18904.27m, reloaded.SignedOpening);
    }

    // ---------------------------------------------------------------- (4) validation + the untouched default

    /// <summary>
    /// ER-13: a blank opening is the norm (most ledgers open at nil) and must stay byte-identical to today's
    /// hard-coded <see cref="Money.Zero"/> — the feature must not give every new ledger a non-zero opening.
    /// </summary>
    [Fact]
    public void A_blank_opening_creates_a_nil_ledger_exactly_as_before()
    {
        var vm = NewSeededCompany("Blank Opening Co");
        var master = new LedgerMasterViewModel(vm.Company!, _storage, onChanged: () => { });

        master.Name = "Courier Charges";
        master.SelectedGroup = vm.Company!.Groups.Single(g => g.Name == "Indirect Expenses");
        Assert.Equal(string.Empty, master.OpeningBalanceText);   // the field opens blank, not "0"
        Assert.True(master.Create(), master.Message);

        var ledger = Reload("Blank Opening Co").Ledgers.Single(l => l.Name == "Courier Charges");
        Assert.Equal(Money.Zero, ledger.OpeningBalance);
    }

    /// <summary>
    /// A non-numeric or negative opening is rejected with a message and adds NOTHING — the ledger master's standing
    /// rule that a failed validation may never leave a half-built master behind for Save to persist.
    /// </summary>
    [Theory]
    [InlineData("not a number")]
    [InlineData("-5140.73")]
    public void An_invalid_opening_is_rejected_and_no_ledger_is_added(string bad)
    {
        var vm = NewSeededCompany("Bad Opening Co " + bad.Length);
        var company = vm.Company!;
        var before = company.Ledgers.Count;

        var master = new LedgerMasterViewModel(company, _storage, onChanged: () => { });
        master.Name = "Suspense";
        master.SelectedGroup = company.Groups.Single(g => g.Name == "Suspense A/c");
        master.OpeningBalanceText = bad;

        Assert.False(master.Create());
        Assert.NotNull(master.Message);
        Assert.Equal(before, company.Ledgers.Count);
    }

    /// <summary>
    /// A sub-paisa opening cannot round-trip the INTEGER-paisa store (NFR-3), so it is refused at the screen with a
    /// clean message rather than surfacing as a raw persistence exception three layers down.
    /// </summary>
    [Fact]
    public void A_sub_paisa_opening_is_refused_at_the_screen()
    {
        var vm = NewSeededCompany("Sub Paisa Co");
        var company = vm.Company!;
        var master = new LedgerMasterViewModel(company, _storage, onChanged: () => { });

        master.Name = "Rounding Trap";
        master.SelectedGroup = company.Groups.Single(g => g.Name == "Suspense A/c");
        master.OpeningBalanceText = "1234.567";

        Assert.False(master.Create());
        Assert.NotNull(master.Message);
        Assert.DoesNotContain(company.Ledgers, l => l.Name == "Rounding Trap");
    }

    // ---------------------------------------------------------------- (5) the list column + reachability

    /// <summary>
    /// The existing-ledgers list on the master already has an Opening column; a created opening must actually show
    /// there with its side, or the operator has no confirmation the figure landed.
    /// </summary>
    [Fact]
    public void The_existing_ledgers_list_shows_the_new_opening_with_its_side()
    {
        var vm = NewSeededCompany("Opening List Co");
        var master = new LedgerMasterViewModel(vm.Company!, _storage, onChanged: () => { });

        master.Name = "Nandi Traders";
        master.SelectedGroup = vm.Company!.Groups.Single(g => g.Name == "Sundry Creditors");
        master.OpeningBalanceText = "18904.27";
        master.OpeningIsDebit = false;
        Assert.True(master.Create(), master.Message);

        var row = master.Existing.Single(r => r.Name == "Nandi Traders");
        Assert.Contains("Cr", row.Opening);
        Assert.Contains("18,904.27", row.Opening);   // Indian digit grouping
    }

    /// <summary>
    /// Reachability through the REAL cascade — Create → Ledger — so the field is proven to sit on the screen the
    /// operator actually lands on, not merely on a view model a test can construct.
    /// </summary>
    [Fact]
    public void The_opening_balance_field_is_on_the_ledger_master_reached_from_the_Create_menu()
    {
        var vm = NewSeededCompany("Opening Nav Co");

        SelectActiveItem(vm, "Create");
        vm.DrillIn();
        Assert.Equal(GatewayMenu.Create, vm.CurrentGatewayMenu);

        SelectActiveItem(vm, "Ledger");
        vm.DrillIn();
        Assert.Equal(Screen.LedgerMaster, vm.CurrentScreen);

        var master = vm.LedgerMaster!;
        master.Name = "Bank of Baroda";
        master.SelectedGroup = vm.Company!.Groups.Single(g => g.Name == "Bank Accounts");
        master.OpeningBalanceText = "41237.53";

        vm.ActivateSelected();      // Ctrl+A

        Assert.Equal(41237.53m, Reload("Opening Nav Co").Ledgers
            .Single(l => l.Name == "Bank of Baroda").OpeningBalance.Amount);
    }

    // ---------------------------------------------------------------- helper

    /// <summary>Creates a ledger THROUGH the master screen (never by hand) so every test exercises the real path.</summary>
    private Guid Create(Company company, string name, string groupName, string opening, bool debit)
    {
        var master = new LedgerMasterViewModel(company, _storage, onChanged: () => { });
        master.Name = name;
        master.SelectedGroup = company.Groups.Single(g => g.Name == groupName);
        master.OpeningBalanceText = opening;
        master.OpeningIsDebit = debit;
        Assert.True(master.Create(), master.Message);
        return company.Ledgers.Single(l => l.Name == name).Id;
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true); }
        catch { /* best effort */ }
    }
}
