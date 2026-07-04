using Apex.Ledger.Domain;
using Apex.Ledger.Reports;
using Apex.Ledger.Services;
using Xunit;
using Domain = Apex.Ledger.Domain;

namespace Apex.Ledger.Tests;

/// <summary>
/// Scenarios, Reversing Journals, Memoranda, and Optional vouchers (catalog §7; plan.md §5).
/// Covers: an Optional voucher excluded from the actual books but surfaced under an including scenario;
/// a Reversing Journal counted only within its "Applicable upto"; a Memorandum non-affecting until
/// converted to a real voucher; and scenario-aware Trial Balance / P&amp;L / Balance Sheet differing from
/// the plain actual reports by exactly the provisional amount.
/// </summary>
public class ScenarioTests
{
    // A small accounts-only company:
    //   Provision for Expenses under Provisions (Current Liabilities → Liability/Cr nature)
    //   Rent under Indirect Expenses (Expense/Dr)
    //   Sales under Sales Accounts (Income/Cr)
    //   Cash (opening 10,00,000 Dr)
    private static Company Seed(
        out Domain.Ledger rent,
        out Domain.Ledger provision,
        out Domain.Ledger cash,
        out Domain.Ledger sales)
    {
        var c = CompanyFactory.CreateSeeded("Scenario Co", new DateOnly(2024, 4, 1));

        cash = c.FindLedgerByName("Cash")!;
        cash.OpeningBalance = Money.FromRupees(1000000m);
        cash.OpeningIsDebit = true;

        // A matching Capital opening so the opening balances net to zero (Trial Balance balances).
        var capital = new Domain.Ledger(Guid.NewGuid(), "Capital", c.FindGroupByName("Capital Account")!.Id,
            Money.FromRupees(1000000m), openingIsDebit: false);
        c.AddLedger(capital);

        rent = new Domain.Ledger(Guid.NewGuid(), "Rent", c.FindGroupByName("Indirect Expenses")!.Id,
            Money.Zero, openingIsDebit: true);
        provision = new Domain.Ledger(Guid.NewGuid(), "Provision for Expenses", c.FindGroupByName("Provisions")!.Id,
            Money.Zero, openingIsDebit: false);
        sales = new Domain.Ledger(Guid.NewGuid(), "Sales", c.FindGroupByName("Sales Accounts")!.Id,
            Money.Zero, openingIsDebit: false);
        c.AddLedger(rent);
        c.AddLedger(provision);
        c.AddLedger(sales);

        return c;
    }

    private static Voucher Journal(Company c, DateOnly date, Guid dr, Guid cr, decimal amt,
        bool optional = false) =>
        new(Guid.NewGuid(), c.FindVoucherTypeByName("Journal")!.Id, date, new[]
        {
            new EntryLine(dr, Money.FromRupees(amt), DrCr.Debit),
            new EntryLine(cr, Money.FromRupees(amt), DrCr.Credit),
        }, optional: optional);

    // ---------------------------------------------------------------- Optional vouchers (Ctrl+L)

    [Fact]
    public void Optional_voucher_is_excluded_from_the_actual_books()
    {
        var c = Seed(out var rent, out _, out var cash, out _);
        var svc = new LedgerService(c);
        svc.Post(Journal(c, new DateOnly(2024, 4, 10), rent.Id, cash.Id, 5000m, optional: true));

        var asOf = new DateOnly(2024, 4, 30);
        // The actual books never see an Optional voucher (like a not-yet-due PostDated one).
        Assert.Equal(0m, LedgerBalances.SignedClosing(c, rent, asOf));

        var tb = TrialBalance.Build(c, asOf);
        Assert.DoesNotContain(tb.Rows, r => r.LedgerName == "Rent");
    }

    [Fact]
    public void Optional_voucher_is_included_under_a_scenario_that_includes_its_type()
    {
        var c = Seed(out var rent, out _, out var cash, out _);
        var svc = new LedgerService(c);
        svc.Post(Journal(c, new DateOnly(2024, 4, 10), rent.Id, cash.Id, 5000m, optional: true));

        var journalTypeId = c.FindVoucherTypeByName("Journal")!.Id;
        var scenario = new Scenario(Guid.NewGuid(), "Provisional", includeActuals: true,
            includedTypeIds: new[] { journalTypeId });
        c.AddScenario(scenario);

        var asOf = new DateOnly(2024, 4, 30);
        // Under the scenario the Optional Rent movement is now visible.
        Assert.Equal(5000m, LedgerBalances.SignedClosing(c, rent, asOf, scenario));
    }

    // ---------------------------------------------------------------- Reversing Journals (Applicable upto)

    [Fact]
    public void Reversing_journal_counts_only_within_its_applicable_upto()
    {
        var c = Seed(out var rent, out var provision, out _, out _);
        // A Reversing Journal accruing rent 3,000, applicable only up to 2024-04-30.
        var revType = c.FindVoucherTypeByName("Reversing Journal")!;
        var rev = new Voucher(Guid.NewGuid(), revType.Id, new DateOnly(2024, 4, 10), new[]
        {
            new EntryLine(rent.Id, Money.FromRupees(3000m), DrCr.Debit),
            new EntryLine(provision.Id, Money.FromRupees(3000m), DrCr.Credit),
        }, applicableUpto: new DateOnly(2024, 4, 30));
        new LedgerService(c).Post(rev);

        var scenario = new Scenario(Guid.NewGuid(), "With Reversing", includeActuals: true,
            includedTypeIds: new[] { revType.Id });
        c.AddScenario(scenario);

        // As-of within the window: the reversing entry is in force.
        Assert.Equal(3000m, LedgerBalances.SignedClosing(c, rent, new DateOnly(2024, 4, 30), scenario));
        // As-of after "Applicable upto": it has reversed out and no longer counts.
        Assert.Equal(0m, LedgerBalances.SignedClosing(c, rent, new DateOnly(2024, 5, 1), scenario));
        // It never touches the actual books either way.
        Assert.Equal(0m, LedgerBalances.SignedClosing(c, rent, new DateOnly(2024, 4, 30)));
    }

    // ---------------------------------------------------------------- Memoranda (+ ConvertToRegular)

    [Fact]
    public void Memorandum_voucher_is_non_affecting()
    {
        var c = Seed(out var rent, out _, out var cash, out _);
        var memoType = c.FindVoucherTypeByName("Memorandum")!;
        var memo = new Voucher(Guid.NewGuid(), memoType.Id, new DateOnly(2024, 4, 12), new[]
        {
            new EntryLine(rent.Id, Money.FromRupees(2500m), DrCr.Debit),
            new EntryLine(cash.Id, Money.FromRupees(2500m), DrCr.Credit),
        });
        new LedgerService(c).Post(memo);

        // A memo is suspense: it does not affect the real books.
        Assert.Equal(0m, LedgerBalances.SignedClosing(c, rent, new DateOnly(2024, 4, 30)));
    }

    [Fact]
    public void Memorandum_surfaces_under_an_including_scenario()
    {
        var c = Seed(out var rent, out _, out var cash, out _);
        var memoType = c.FindVoucherTypeByName("Memorandum")!;
        var memo = new Voucher(Guid.NewGuid(), memoType.Id, new DateOnly(2024, 4, 12), new[]
        {
            new EntryLine(rent.Id, Money.FromRupees(2500m), DrCr.Debit),
            new EntryLine(cash.Id, Money.FromRupees(2500m), DrCr.Credit),
        });
        new LedgerService(c).Post(memo);

        var scenario = new Scenario(Guid.NewGuid(), "With Memo", includeActuals: true,
            includedTypeIds: new[] { memoType.Id });
        c.AddScenario(scenario);

        Assert.Equal(2500m, LedgerBalances.SignedClosing(c, rent, new DateOnly(2024, 4, 30), scenario));
    }

    [Fact]
    public void ConvertToRegular_makes_a_memorandum_affect_the_books()
    {
        var c = Seed(out var rent, out _, out var cash, out _);
        var memoType = c.FindVoucherTypeByName("Memorandum")!;
        var memo = new Voucher(Guid.NewGuid(), memoType.Id, new DateOnly(2024, 4, 12), new[]
        {
            new EntryLine(rent.Id, Money.FromRupees(2500m), DrCr.Debit),
            new EntryLine(cash.Id, Money.FromRupees(2500m), DrCr.Credit),
        });
        var svc = new LedgerService(c);
        svc.Post(memo);
        var asOf = new DateOnly(2024, 4, 30);
        Assert.Equal(0m, LedgerBalances.SignedClosing(c, rent, asOf)); // memo: not in books

        var paymentTypeId = c.FindVoucherTypeByName("Payment")!.Id;
        var regular = svc.ConvertToRegular(memo.Id, paymentTypeId);

        // The memo is gone; a real Payment now affects the actual books.
        Assert.Null(c.FindVoucher(memo.Id));
        Assert.Equal(paymentTypeId, regular.TypeId);
        Assert.False(regular.Optional);
        Assert.Equal(2500m, LedgerBalances.SignedClosing(c, rent, asOf));   // Rent now Dr 2,500
        Assert.Equal(-2500m, LedgerBalances.SignedClosing(c, cash, asOf) - 1000000m); // Cash down 2,500
    }

    [Fact]
    public void ConvertToRegular_rejects_a_non_memorandum_voucher()
    {
        var c = Seed(out var rent, out _, out var cash, out _);
        var svc = new LedgerService(c);
        var journal = Journal(c, new DateOnly(2024, 4, 10), rent.Id, cash.Id, 5000m);
        svc.Post(journal);

        Assert.Throws<InvalidOperationException>(() =>
            svc.ConvertToRegular(journal.Id, c.FindVoucherTypeByName("Payment")!.Id));
    }

    // ---------------------------------------------------------------- Scenario-aware reports

    [Fact]
    public void Scenario_trial_balance_surfaces_the_optional_voucher_versus_the_actual()
    {
        var c = Seed(out var rent, out var provision, out var cash, out _);
        var svc = new LedgerService(c);
        // One real receipt (Cash 8,000 from Sales) and one OPTIONAL provision accrual of 5,000
        // (Rent Dr / Provision for Expenses Cr) — a new liability, so it adds to both TB columns.
        var sales = c.FindLedgerByName("Sales")!;
        svc.Post(Journal(c, new DateOnly(2024, 4, 5), cash.Id, sales.Id, 8000m));
        svc.Post(Journal(c, new DateOnly(2024, 4, 10), rent.Id, provision.Id, 5000m, optional: true));

        var journalTypeId = c.FindVoucherTypeByName("Journal")!.Id;
        var scenario = new Scenario(Guid.NewGuid(), "Provisional", includeActuals: true,
            includedTypeIds: new[] { journalTypeId });
        c.AddScenario(scenario);

        var asOf = new DateOnly(2024, 4, 30);
        var actual = TrialBalance.Build(c, asOf);
        var underScenario = TrialBalance.Build(c, asOf, scenario);

        Assert.True(actual.Balanced);
        Assert.True(underScenario.Balanced);
        // Actual TB has neither Rent nor Provision (the accrual is Optional); the scenario TB adds both.
        Assert.DoesNotContain(actual.Rows, r => r.LedgerName == "Rent");
        var rentRow = Assert.Single(underScenario.Rows, r => r.LedgerName == "Rent");
        var provRow = Assert.Single(underScenario.Rows, r => r.LedgerName == "Provision for Expenses");
        Assert.Equal(5000m, rentRow.Debit.Amount);
        Assert.Equal(5000m, provRow.Credit.Amount);
        // The provision accrual adds 5,000 to each column of the scenario relative to the actual.
        Assert.Equal(actual.TotalDebit.Amount + 5000m, underScenario.TotalDebit.Amount);
        Assert.Equal(actual.TotalCredit.Amount + 5000m, underScenario.TotalCredit.Amount);
    }

    [Fact]
    public void Scenario_profit_and_loss_reflects_an_optional_expense()
    {
        var c = Seed(out var rent, out _, out var cash, out _);
        var svc = new LedgerService(c);
        var sales = c.FindLedgerByName("Sales")!;
        svc.Post(Journal(c, new DateOnly(2024, 4, 5), cash.Id, sales.Id, 8000m));           // real income
        svc.Post(Journal(c, new DateOnly(2024, 4, 10), rent.Id, cash.Id, 5000m, optional: true)); // optional expense

        var journalTypeId = c.FindVoucherTypeByName("Journal")!.Id;
        var scenario = new Scenario(Guid.NewGuid(), "Provisional", includeActuals: true,
            includedTypeIds: new[] { journalTypeId });
        c.AddScenario(scenario);

        var asOf = new DateOnly(2024, 4, 30);
        var actual = ProfitAndLoss.Build(c, asOf);
        var underScenario = ProfitAndLoss.Build(c, asOf, ClosingStockMode.AsPostedLedger, scenario);

        // Actual net profit = income 8,000 (no expenses recorded — rent is Optional).
        Assert.Equal(8000m, actual.NetProfit.Amount);
        // Scenario net profit = 8,000 − 5,000 provisional rent = 3,000.
        Assert.Equal(3000m, underScenario.NetProfit.Amount);
    }

    [Fact]
    public void Scenario_with_actuals_off_shows_only_provisional_movements()
    {
        var c = Seed(out var rent, out _, out var cash, out _);
        var svc = new LedgerService(c);
        var sales = c.FindLedgerByName("Sales")!;
        svc.Post(Journal(c, new DateOnly(2024, 4, 5), cash.Id, sales.Id, 8000m));           // real
        svc.Post(Journal(c, new DateOnly(2024, 4, 10), rent.Id, cash.Id, 5000m, optional: true)); // optional

        var journalTypeId = c.FindVoucherTypeByName("Journal")!.Id;
        var scenario = new Scenario(Guid.NewGuid(), "Only Provisional", includeActuals: false,
            includedTypeIds: new[] { journalTypeId });
        c.AddScenario(scenario);

        var asOf = new DateOnly(2024, 4, 30);
        // Actuals off: neither the opening cash nor the real receipt is counted — only the Optional rent.
        Assert.Equal(5000m, LedgerBalances.SignedClosing(c, rent, asOf, scenario));
        Assert.Equal(-5000m, LedgerBalances.SignedClosing(c, cash, asOf, scenario)); // opening excluded
    }

    [Fact]
    public void Scenario_excludes_a_voucher_type_even_when_actuals_are_included()
    {
        var c = Seed(out var rent, out _, out var cash, out _);
        var svc = new LedgerService(c);
        // A real Payment (excluded by the scenario) and a real Journal (kept).
        var payType = c.FindVoucherTypeByName("Payment")!;
        var pay = new Voucher(Guid.NewGuid(), payType.Id, new DateOnly(2024, 4, 6), new[]
        {
            new EntryLine(rent.Id, Money.FromRupees(1000m), DrCr.Debit),
            new EntryLine(cash.Id, Money.FromRupees(1000m), DrCr.Credit),
        });
        svc.Post(pay);
        svc.Post(Journal(c, new DateOnly(2024, 4, 8), rent.Id, cash.Id, 2000m));

        var scenario = new Scenario(Guid.NewGuid(), "No Payments", includeActuals: true,
            excludedTypeIds: new[] { payType.Id });
        c.AddScenario(scenario);

        var asOf = new DateOnly(2024, 4, 30);
        // Actuals = 1,000 (Payment) + 2,000 (Journal) = 3,000; scenario excludes the Payment → 2,000.
        Assert.Equal(3000m, LedgerBalances.SignedClosing(c, rent, asOf));
        Assert.Equal(2000m, LedgerBalances.SignedClosing(c, rent, asOf, scenario));
    }

    [Fact]
    public void Null_scenario_matches_the_plain_actual_report()
    {
        var c = Seed(out var rent, out _, out var cash, out _);
        var svc = new LedgerService(c);
        svc.Post(Journal(c, new DateOnly(2024, 4, 8), rent.Id, cash.Id, 2000m));

        var asOf = new DateOnly(2024, 4, 30);
        // Passing a null scenario is behaviour-identical to the no-scenario call.
        Assert.Equal(
            LedgerBalances.SignedClosing(c, rent, asOf),
            LedgerBalances.SignedClosing(c, rent, asOf, scenario: null));

        var tbPlain = TrialBalance.Build(c, asOf);
        var tbNull = TrialBalance.Build(c, asOf, scenario: null);
        Assert.Equal(tbPlain.TotalDebit.Amount, tbNull.TotalDebit.Amount);
        Assert.Equal(tbPlain.Rows.Count, tbNull.Rows.Count);
    }
}
