using System;
using System.Linq;
using Apex.Ledger.Domain;
using Apex.Ledger.Reports;
using Apex.Ledger.Services;
using Xunit;

namespace Apex.Ledger.Tests;

/// <summary>
/// RQ-7 (universal drill-down) — ENGINE / row-identity side. Every accounting-report ledger row now
/// carries its owning ledger's stable id (<c>LedgerId</c>) so the UI can drill (Enter) into that ledger's
/// vouchers; every voucher row (Day Book, ledger-book) carries its <c>VoucherId</c> so the UI can drill
/// into voucher detail. Synthetic/computed rows (folded Net Profit, derived Stock-in-Hand) carry no id and
/// report <c>IsDrillable == false</c>, so Enter is a safe no-op there. The ledger-vouchers projection the UI
/// opens on drill is the existing <see cref="LedgerBook"/> — these tests pin that it returns the drilled
/// ledger's postings (with per-row VoucherId) for the period. Robert (accounts-only) and Bright (trading)
/// remain the regression anchors (R8); figures are asserted unchanged by the sibling report tests.
/// </summary>
public class DrillIdentityTests
{
    private static FixtureLoader.LoadedFixture Robert() => FixtureLoader.Load("robert.json");
    private static FixtureLoader.LoadedFixture Bright() => FixtureLoader.Load("bright.json");

    // ---------------------------------------------------------------- Trial Balance rows carry LedgerId

    [Fact]
    [Trait("Category", "Fixture")]
    public void TrialBalance_every_row_carries_its_ledger_id_and_is_drillable()
    {
        var f = Robert();
        var tb = TrialBalance.Build(f.Company, f.AsOf);

        Assert.NotEmpty(tb.Rows);
        foreach (var row in tb.Rows)
        {
            // The row's LedgerId resolves to a real ledger whose name matches the displayed name.
            var ledger = f.Company.FindLedger(row.LedgerId);
            Assert.NotNull(ledger);
            Assert.Equal(row.LedgerName, ledger!.Name);
            Assert.True(row.IsDrillable);
        }
    }

    [Fact]
    [Trait("Category", "Fixture")]
    public void TrialBalance_default_row_id_is_empty_and_not_drillable()
    {
        // A hand-built row with no ledger id (a synthetic/heading row the UI might inject) must be a safe
        // Enter no-op — the appended positional field defaults to Guid.Empty.
        var row = new TrialBalanceRow("Total", "", new Money(100m), Money.Zero);
        Assert.Equal(Guid.Empty, row.LedgerId);
        Assert.False(row.IsDrillable);
    }

    // ---------------------------------------------------------------- Balance Sheet lines carry LedgerId

    [Fact]
    [Trait("Category", "Fixture")]
    public void BalanceSheet_ledger_lines_carry_ledger_id_and_synthetic_heads_do_not()
    {
        var f = Bright();
        var bs = BalanceSheet.Build(f.Company, f.AsOf);

        foreach (var line in bs.Liabilities.Concat(bs.Assets))
        {
            if (line.IsDrillable)
            {
                // A drillable line names a real ledger.
                var ledger = f.Company.FindLedger(line.LedgerId);
                Assert.NotNull(ledger);
                Assert.Equal(line.Name, ledger!.Name);
            }
            else
            {
                // The only non-drillable line here is the folded period Net Profit (synthetic head).
                Assert.Equal(Guid.Empty, line.LedgerId);
            }
        }

        // The folded Net Profit head is explicitly a non-drillable computed line.
        var netProfit = bs.Liabilities.Single(l => l.Name == "Net Profit (period)");
        Assert.False(netProfit.IsDrillable);
    }

    [Fact]
    [Trait("Category", "Fixture")]
    public void BalanceSheet_derived_stock_in_hand_head_is_not_drillable()
    {
        var f = Bright();
        var bs = BalanceSheet.Build(f.Company, f.AsOf, ClosingStockMode.InventoryDerived);

        var stock = bs.Assets.SingleOrDefault(a => a.Name == "Stock-in-Hand" && a.GroupId is null);
        if (stock is not null)
            Assert.False(stock.IsDrillable); // derived Σ line has no single owning ledger
    }

    // ---------------------------------------------------------------- P&L lines carry LedgerId

    [Fact]
    [Trait("Category", "Fixture")]
    public void ProfitAndLoss_income_and_expense_lines_carry_their_ledger_id()
    {
        var f = Bright();
        var pl = ProfitAndLoss.Build(f.Company, f.AsOf);

        Assert.NotEmpty(pl.Income.Concat(pl.Expenses));
        foreach (var line in pl.Income.Concat(pl.Expenses))
        {
            var ledger = f.Company.FindLedger(line.LedgerId);
            Assert.NotNull(ledger);
            Assert.Equal(line.LedgerName, ledger!.Name);
            Assert.True(line.IsDrillable);
        }
    }

    // ---------------------------------------------------------------- Day Book rows carry VoucherId

    [Fact]
    [Trait("Category", "Fixture")]
    public void DayBook_every_row_carries_its_voucher_id_and_is_drillable()
    {
        var f = Bright();
        var rows = DayBook.Build(f.Company, f.Company.BooksBeginFrom, f.AsOf);

        Assert.NotEmpty(rows);
        foreach (var row in rows)
        {
            Assert.NotEqual(Guid.Empty, row.VoucherId);
            Assert.True(row.IsDrillable);
            // The id resolves to a real voucher whose header matches the row.
            var v = f.Company.Vouchers.Single(x => x.Id == row.VoucherId);
            Assert.Equal(row.Date, v.Date);
            Assert.Equal(row.Number, v.Number);
        }
    }

    // ---------------------------------------------------------------- ledger-vouchers drill target (LedgerBook)

    [Fact]
    [Trait("Category", "Fixture")]
    public void Drilling_a_trial_balance_row_opens_that_ledgers_vouchers_for_the_period()
    {
        var f = Bright();
        var from = f.Company.BooksBeginFrom;
        var to = f.AsOf;

        // Pick a drillable TB row that actually has postings (Cash moves in Bright).
        var tb = TrialBalance.Build(f.Company, to);
        var cashRow = tb.Rows.Single(r => r.LedgerName == "Cash");
        Assert.True(cashRow.IsDrillable);

        // The UI opens LedgerBook.Build(company, row.LedgerId, from, to) as the drill target column.
        var book = LedgerBook.Build(f.Company, cashRow.LedgerId, from, to);

        Assert.Equal("Cash", book.LedgerName);
        Assert.NotEmpty(book.Rows);

        // The projection returns exactly this ledger's postings for the period; each row is a real voucher
        // (VoucherId set) that itself carries a Cash line — the identity the UI drills further into.
        foreach (var row in book.Rows)
        {
            Assert.NotEqual(Guid.Empty, row.VoucherId);
            Assert.True(row.IsDrillable);
            var v = f.Company.Vouchers.Single(x => x.Id == row.VoucherId);
            Assert.Contains(v.Lines, l => l.LedgerId == cashRow.LedgerId);
            Assert.InRange(v.Date, from, to);
        }

        // The book's final running balance equals the ledger's closing (the TB figure), to the paisa.
        var closing = LedgerBalances.Closing(f.Company, f.Company.FindLedger(cashRow.LedgerId)!, to);
        Assert.Equal(closing.Amount, book.ClosingAmount);
        Assert.Equal(closing.Side, book.ClosingSide);
    }

    [Fact]
    [Trait("Category", "Fixture")]
    public void LedgerBook_build_with_empty_ledger_id_returns_empty_and_does_not_throw()
    {
        // RQ-7 defensive guard: a drill on a non-drillable (Guid.Empty) row must never call the engine with a
        // real ledger, but if it does the build returns an EMPTY book rather than throwing.
        var f = Bright();
        var book = LedgerBook.Build(f.Company, Guid.Empty, f.Company.BooksBeginFrom, f.AsOf);

        Assert.Empty(book.Rows);
        Assert.Equal(Money.Zero, book.OpeningAmount);
        Assert.Equal(Money.Zero, book.ClosingAmount);
    }

    // ---------------------------------------------------------------- RQ-7 defect-2: P&L (flow) drill reconciles
    //
    // A P&L line is a period-MOVEMENT figure. Drilling it must open a ledger-book whose closing equals that
    // in-window movement (running balance from 0) — NOT a cumulative closing (opening + everything to-date),
    // which diverges once the ledger has pre-period activity. A TB/BS (point-in-time) drill keeps the
    // cumulative closing-as-at-To. This test builds a Sales ledger with a posting BEFORE the window and one
    // INSIDE it, so the two modes give different closings, and pins each to its report figure.

    /// <summary>Cash (Dr opening) + a Sales income ledger, one Sales voucher before the window, one inside it.</summary>
    private static Company MidBookSalesCompany(out Domain.Ledger sales, DateOnly fyStart)
    {
        var c = CompanyFactory.CreateSeeded("Movement Co", fyStart);
        var cash = c.FindLedgerByName("Cash")!;

        var salesGroup = c.FindGroupByName("Sales Accounts")!;
        sales = new Domain.Ledger(Guid.NewGuid(), "Sales", salesGroup.Id, Money.Zero, openingIsDebit: false);
        c.AddLedger(sales);

        var receipt = c.FindVoucherTypeByName("Receipt")!;
        var svc = new LedgerService(c);

        // Pre-window sale: Dr Cash 40,000 / Cr Sales 40,000 on day 5.
        svc.Post(new Voucher(Guid.NewGuid(), receipt.Id, fyStart.AddDays(5), new[]
        {
            new EntryLine(cash.Id, Money.FromRupees(40000m), DrCr.Debit),
            new EntryLine(sales.Id, Money.FromRupees(40000m), DrCr.Credit),
        }));
        // In-window sale: Dr Cash 25,000 / Cr Sales 25,000 on day 40.
        svc.Post(new Voucher(Guid.NewGuid(), receipt.Id, fyStart.AddDays(40), new[]
        {
            new EntryLine(cash.Id, Money.FromRupees(25000m), DrCr.Debit),
            new EntryLine(sales.Id, Money.FromRupees(25000m), DrCr.Credit),
        }));
        return c;
    }

    [Fact]
    public void ProfitAndLoss_movement_drill_closes_at_the_period_figure_not_the_cumulative_balance()
    {
        var fyStart = new DateOnly(2024, 4, 1);
        var c = MidBookSalesCompany(out var sales, fyStart);

        // A mid-book window that starts AFTER the first (pre-window) sale: [day 30, day 60].
        var from = fyStart.AddDays(30);
        var to = fyStart.AddDays(60);

        // The P&L period figure for Sales over the window is the IN-WINDOW movement only = 25,000.
        var pl = ProfitAndLoss.Build(c, to, ReportOptions.ForPeriod(new PeriodRange(from, to)));
        var salesLine = pl.Income.Single(l => l.LedgerName == "Sales");
        Assert.Equal(Money.FromRupees(25000m), salesLine.Amount);

        // A MOVEMENT drill (P&L flow) reconciles to that figure: closing == 25,000 Cr, opening line == 0,
        // and only the in-window posting is listed.
        var movementBook = LedgerBook.Build(c, sales.Id, from, to, movement: true);
        Assert.Equal(Money.FromRupees(25000m), movementBook.ClosingAmount);
        Assert.Equal(DrCr.Credit, movementBook.ClosingSide);
        Assert.Equal(Money.Zero, movementBook.OpeningAmount);
        Assert.Single(movementBook.Rows);
        Assert.All(movementBook.Rows, r => Assert.InRange(r.Date, from, to));

        // A POINT-IN-TIME drill (TB/BS, the default) instead shows the cumulative closing-as-at-To =
        // 40,000 + 25,000 = 65,000 Cr — which is the correct closing balance but NOT the P&L figure. This is
        // exactly the divergence the movement mode fixes.
        var pointInTimeBook = LedgerBook.Build(c, sales.Id, from, to);
        Assert.Equal(Money.FromRupees(65000m), pointInTimeBook.ClosingAmount);
        Assert.Equal(DrCr.Credit, pointInTimeBook.ClosingSide);
        Assert.NotEqual(movementBook.ClosingAmount, pointInTimeBook.ClosingAmount);
    }

    [Fact]
    public void TrialBalance_point_in_time_drill_still_shows_the_cumulative_closing()
    {
        var fyStart = new DateOnly(2024, 4, 1);
        var c = MidBookSalesCompany(out var sales, fyStart);
        var from = fyStart.AddDays(30);
        var to = fyStart.AddDays(60);

        // The TB (closing-as-at-To) figure for Sales = 65,000 Cr regardless of From (opening carried forward).
        var tb = TrialBalance.Build(c, ReportOptions.ForPeriod(new PeriodRange(from, to)));
        var salesRow = tb.Rows.Single(r => r.LedgerName == "Sales");
        Assert.Equal(Money.FromRupees(65000m), salesRow.Credit);

        // The point-in-time drill (movement:false) matches that cumulative closing to the paisa.
        var book = LedgerBook.Build(c, sales.Id, from, to);
        Assert.Equal(salesRow.Credit, book.ClosingAmount);
        Assert.Equal(DrCr.Credit, book.ClosingSide);
    }

    [Fact]
    [Trait("Category", "Fixture")]
    public void LedgerBook_row_voucher_id_matches_the_daybook_voucher_id_for_the_same_voucher()
    {
        // A voucher touching Cash must surface with the SAME VoucherId in both the Day Book and the Cash
        // ledger-book — the id is the single stable drill key the UI reuses across reports.
        var f = Bright();
        var from = f.Company.BooksBeginFrom;
        var to = f.AsOf;

        var cash = f.Company.FindLedgerByName("Cash")!;
        var book = LedgerBook.Build(f.Company, cash.Id, from, to);
        var dayBook = DayBook.Build(f.Company, from, to);

        foreach (var row in book.Rows)
        {
            var day = dayBook.SingleOrDefault(d => d.VoucherId == row.VoucherId);
            Assert.NotNull(day); // the same voucher appears in the Day Book with the same id
        }
    }
}
