using Apex.Ledger.Domain;
using Apex.Ledger.Reports;
using Xunit;

namespace Apex.Ledger.Tests;

/// <summary>
/// RQ-5 (part 1) — the three Statements reports (Cash Flow, Funds Flow, Ratio Analysis). Each is a
/// pure, UI-independent projection composed from the existing statement builders / LedgerBalances.
/// The two deterministic fixtures (Robert accounts-only, Bright trading) are the regression baseline (R8).
/// </summary>
public class StatementReportsTests
{
    private static FixtureLoader.LoadedFixture Robert() => FixtureLoader.Load("robert.json");
    private static FixtureLoader.LoadedFixture Bright() => FixtureLoader.Load("bright.json");

    // ---------------------------------------------------------------- Cash Flow

    [Fact]
    [Trait("Category", "Fixture")]
    public void CashFlow_bright_net_movement_reconciles_opening_to_closing_cash_and_bank()
    {
        var f = Bright();
        var from = new DateOnly(2021, 4, 1);
        var to = f.AsOf;

        var cf = CashFlow.Build(f.Company, new PeriodRange(from, to));

        // Bright: Cash 20,000 → 27,000 (+7,000); HDFC Bank 50,000 → 53,000 (+3,000).
        // Opening cash+bank = 70,000; closing = 80,000; net movement = +10,000.
        Assert.Equal(Money.FromRupees(70000m), cf.OpeningBalance);
        Assert.Equal(Money.FromRupees(80000m), cf.ClosingBalance);
        Assert.Equal(Money.FromRupees(10000m), cf.NetCashFlow);

        // The reconciliation identity: opening + net = closing, to the paisa.
        Assert.Equal(cf.ClosingBalance, cf.OpeningBalance + cf.NetCashFlow);
        Assert.True(cf.Reconciles);

        // Net = inflows − outflows.
        Assert.Equal(cf.NetCashFlow, cf.TotalInflows - cf.TotalOutflows);
    }

    [Fact]
    [Trait("Category", "Fixture")]
    public void CashFlow_robert_reconciles_and_lines_net_to_movement()
    {
        var f = Robert();
        var cf = CashFlow.Build(f.Company, new PeriodRange(new DateOnly(2021, 4, 1), f.AsOf));

        Assert.True(cf.Reconciles);
        Assert.Equal(cf.ClosingBalance, cf.OpeningBalance + cf.NetCashFlow);

        // Every inflow/outflow section line's signed sum equals the net movement.
        var lineNet = 0m;
        foreach (var s in cf.Inflows) lineNet += s.Amount.Amount;
        foreach (var s in cf.Outflows) lineNet -= s.Amount.Amount;
        Assert.Equal(cf.NetCashFlow.Amount, lineNet);
    }

    // ---------------------------------------------------------------- Funds Flow

    [Fact]
    [Trait("Category", "Fixture")]
    public void FundsFlow_bright_sources_equal_applications()
    {
        var f = Bright();
        var ff = FundsFlow.Build(f.Company, new PeriodRange(new DateOnly(2021, 4, 1), f.AsOf));

        // A funds-flow statement always balances: total sources == total applications.
        Assert.Equal(ff.TotalSources, ff.TotalApplications);
        Assert.True(ff.Balanced);
    }

    [Fact]
    [Trait("Category", "Fixture")]
    public void FundsFlow_robert_sources_equal_applications()
    {
        var f = Robert();
        var ff = FundsFlow.Build(f.Company, new PeriodRange(new DateOnly(2021, 4, 1), f.AsOf));

        Assert.Equal(ff.TotalSources, ff.TotalApplications);
        Assert.True(ff.Balanced);
    }

    // ---------------------------------------------------------------- Ratio Analysis

    [Fact]
    [Trait("Category", "Fixture")]
    public void RatioAnalysis_bright_gross_profit_percent_and_current_ratio()
    {
        var f = Bright();
        var ra = RatioAnalysis.Build(f.Company, f.AsOf);

        // Working capital figures (Bright, closing):
        //   Current Assets = Cash 27,000 + HDFC 53,000 + Closing Stock 15,000 + Debtor 35,000 = 130,000
        //   Current Liabilities = Sundry Creditors 35,000
        //   Working capital = 95,000; Current ratio = 130000/35000 ≈ 3.714…
        Assert.Equal(Money.FromRupees(130000m), ra.CurrentAssets);
        Assert.Equal(Money.FromRupees(35000m), ra.CurrentLiabilities);
        Assert.Equal(Money.FromRupees(95000m), ra.WorkingCapital);
        Assert.Equal(Math.Round(130000m / 35000m, 4), Math.Round(ra.CurrentRatio!.Value, 4));

        // Quick ratio = (CA − closing stock) / CL = (130000 − 15000)/35000 = 115000/35000.
        Assert.Equal(Math.Round(115000m / 35000m, 4), Math.Round(ra.QuickRatio!.Value, 4));

        // Gross profit % = GP / Sales × 100 = 15,000 / 73,000 × 100.
        Assert.Equal(Money.FromRupees(15000m), ra.GrossProfit);
        Assert.Equal(Math.Round(15000m / 73000m * 100m, 4), Math.Round(ra.GrossProfitPercent!.Value, 4));

        // Net profit % = NP / Sales × 100 = −1,000 / 73,000 × 100 (Bright makes a small loss).
        Assert.Equal(Math.Round(-1000m / 73000m * 100m, 4), Math.Round(ra.NetProfitPercent!.Value, 4));
    }

    [Fact]
    [Trait("Category", "Fixture")]
    public void RatioAnalysis_bright_new_ratios_match_hand_computed_values()
    {
        var f = Bright();
        var ra = RatioAnalysis.Build(f.Company, f.AsOf);

        // Principal-group figures we newly expose (Bright, closing):
        //   Sundry Debtors  = Ram & Co 35,000 ; Sundry Creditors = Shyam Traders 35,000
        //   Capital Account = Bright's Capital 150,000 (EXCLUDES the folded −1,000 period net profit)
        // 🔴 T0-27: these are the CLOSING balances, and they are asserted on the CLOSING members. The
        // Balance-Sheet classification they prove is unchanged by the T0-27 fix — only which figure the
        // report PUBLISHES changed.
        Assert.Equal(Money.FromRupees(35000m), ra.SundryDebtorsClosing);
        Assert.Equal(Money.FromRupees(35000m), ra.SundryCreditorsClosing);

        // 🔴 …and the PUBLISHED "due till today" figures are 0 for Bright, which is correct and not a
        // regression: neither Ram & Co nor Shyam Traders maintains bill-wise details (bright.json sets no
        // bill-by-bill flag and posts no bill allocations), so the book contains NO bills and nothing can
        // have fallen due. A book where the two figures genuinely differ is covered by
        // RatioAnalysis_publishes_sundry_figures_due_till_today_not_the_closing_balance below.
        Assert.Equal(Money.Zero, ra.SundryDebtorsDueTillToday);
        Assert.Equal(Money.Zero, ra.SundryCreditorsDueTillToday);

        Assert.Equal(Money.FromRupees(150000m), ra.CapitalAccount);
        // Proprietor's funds = Capital + Nett Profit = 150,000 + (−1,000) = 149,000.
        Assert.Equal(Money.FromRupees(149000m), ra.ProprietorsFunds);

        // Working Capital Turnover = Sales / Working Capital = 73,000 / 95,000.
        Assert.Equal(Math.Round(73000m / 95000m, 4), Math.Round(ra.WorkingCapitalTurnover!.Value, 4));

        // Operating Cost % = 100 − Nett Profit % = 100 − (−1,000/73,000×100) ≈ 101.3699…
        var expectedNetProfitPct = -1000m / 73000m * 100m;
        Assert.Equal(Math.Round(100m - expectedNetProfitPct, 4), Math.Round(ra.OperatingCostPercent!.Value, 4));

        // Receivables Turnover (days) = Sundry Debtors DUE TILL TODAY ÷ Sales × days-in-period.
        // Default window = books-begin 2021-04-01 → as-of 2022-03-31 (inclusive) = 365 days.
        // 🔴 T0-27: this assertion used to read 175 days — 35,000 ÷ 73,000 × 365 — computed from the CLOSING
        // debtor balance. The vendor defines this ratio "irrespective of the outstanding balance on the
        // statement date", so the old expectation was itself the defect. Bright has no bills, so the
        // numerator is 0 and the ratio is an exact 0.
        Assert.Equal(0m, Math.Round(ra.ReceivablesTurnoverDays!.Value, 4));

        // Return on Working Capital % = Nett Profit ÷ Working Capital × 100 = −1,000 / 95,000 × 100.
        Assert.Equal(Math.Round(-1000m / 95000m * 100m, 4), Math.Round(ra.ReturnOnWorkingCapitalPercent!.Value, 4));

        // CORRECTED Return on Investment % = Nett Profit ÷ (Capital + Nett Profit) × 100
        //   = −1,000 / (150,000 + (−1,000)) × 100 = −1,000 / 149,000 × 100  (NOT capital-employed incl. loans).
        Assert.Equal(Math.Round(-1000m / 149000m * 100m, 4), Math.Round(ra.ReturnOnInvestmentPercent!.Value, 4));

        // Debt/Equity = Loans ÷ (Capital + Nett Profit); Bright has no loans → 0 / 149,000 = 0.
        Assert.Equal(0m, ra.DebtEquityRatio!.Value);

        // The two render-ready columns are populated and label-complete.
        Assert.Contains(ra.PrincipalGroups, g => g.Label == "Sundry Debtors (due till today)");
        Assert.Contains(ra.PrincipalRatios, r => r.Label == "Working Capital Turnover" && r.Unit == RatioUnit.Ratio);
        Assert.Contains(ra.PrincipalRatios, r => r.Label == "Receivables Turnover (days)" && r.Unit == RatioUnit.Days);
        Assert.Contains(ra.PrincipalRatios, r => r.Label == "Operating Cost %" && r.Unit == RatioUnit.Percent);
    }

    [Fact]
    [Trait("Category", "Fixture")]
    public void RatioAnalysis_guards_divide_by_zero()
    {
        // Robert (transport, accounts-only) has NO Sales Accounts and NO Stock-in-Hand, so every ratio whose
        // denominator is Sales or Stock is a guarded divide-by-zero → null (N/A), never a throw. Balance-based
        // ratios (proprietor's funds / current liabilities non-zero) still yield a value.
        var f = Robert();
        var ra = RatioAnalysis.Build(f.Company, f.AsOf);

        // The projection never throws and yields a full row set; each ratio is either a value or null.
        Assert.NotNull(ra);

        // A ratio is null ONLY when its own denominator is zero. Robert has Sales = 0 and Stock = 0, so
        // ratios dividing BY Sales or Stock are null; ratios dividing by a non-zero balance are a real number.
        Assert.Equal(Money.Zero, ra.Sales);

        // Denominator = Sales (0) → null.
        Assert.Null(ra.GrossProfitPercent);
        Assert.Null(ra.NetProfitPercent);
        Assert.Null(ra.OperatingCostPercent);           // derives from Nett Profit % → null when Sales = 0
        Assert.Null(ra.ReceivablesTurnoverDays);

        // Denominator = Stock-in-Hand (0) → null.
        Assert.Null(ra.InventoryTurnover);

        // Denominator = Working Capital (65,000 ≠ 0) → a real number, NOT null (the guard only nulls a
        // ZERO denominator). Working-Capital Turnover numerator is Sales (0) → exactly 0.
        Assert.Equal(0m, ra.WorkingCapitalTurnover!.Value);          // Sales 0 ÷ WC 65,000 = 0
        // Return on Working Capital = Nett Profit 5,000 ÷ WC 65,000 × 100 (Robert earns a small profit).
        Assert.Equal(Math.Round(5000m / 65000m * 100m, 4), Math.Round(ra.ReturnOnWorkingCapitalPercent!.Value, 4));

        // ROI denominator = Capital + Nett Profit ≠ 0 → present (no throw).
        Assert.NotNull(ra.ReturnOnInvestmentPercent);
        _ = ra.DebtEquityRatio;
    }

    // ------------------------------------------------- Ratio Analysis — T0-27 ("due till today")

    /// <summary>
    /// A book built so the CLOSING balance and the DUE-TILL-TODAY figure differ on BOTH sides, with every
    /// number hand-computed below. This is the regression test for defect <b>T0-27</b>: Ratio Analysis
    /// published the Balance-Sheet closing balance as "Sundry Debtors" and "Sundry Creditors" and fed the
    /// first into Receivables Turnover — three wrong figures at once.
    /// <para>Source: the vendor's Ratio Analysis help page captions the two Principal Groups
    /// <b>"Sundry Debtors (due till today)"</b> / <b>"Sundry Creditors (due till today)"</b> and defines
    /// Receivables Turnover as the average time customers take to pay their bills <i>"irrespective of the
    /// outstanding balance on the statement date"</i> — which names the closing balance as the wrong input.</para>
    /// </summary>
    [Fact]
    public void RatioAnalysis_publishes_sundry_figures_due_till_today_not_the_closing_balance()
    {
        // Books begin 2024-04-01; statement date 2024-06-30.
        var c = Services.CompanyFactory.CreateSeeded(
            "Due-Till-Today Co", new DateOnly(2024, 4, 1), new DateOnly(2024, 4, 1));
        var asOf = new DateOnly(2024, 6, 30);
        var journal = c.FindVoucherTypeByName("Journal")!;

        var sales = new Domain.Ledger(Guid.NewGuid(), "Sales", c.FindGroupByName("Sales Accounts")!.Id,
            Money.Zero, openingIsDebit: false);
        c.AddLedger(sales);
        var purchases = new Domain.Ledger(Guid.NewGuid(), "Purchases", c.FindGroupByName("Purchase Accounts")!.Id,
            Money.Zero, openingIsDebit: true);
        c.AddLedger(purchases);

        var debtor = new Domain.Ledger(Guid.NewGuid(), "Acme Ltd", c.FindGroupByName("Sundry Debtors")!.Id,
            Money.Zero, openingIsDebit: true, maintainBillByBill: true);
        c.AddLedger(debtor);
        var creditor = new Domain.Ledger(Guid.NewGuid(), "Supplier Co", c.FindGroupByName("Sundry Creditors")!.Id,
            Money.Zero, openingIsDebit: false, maintainBillByBill: true);
        c.AddLedger(creditor);

        var svc = new Services.LedgerService(c);

        // Receivables. Explicit due dates so the arithmetic needs no credit-period reasoning.
        //   INV-1  60,000  due 2024-05-10  → fallen due by 30-Jun  ✔ counts
        //   INV-2  40,000  due 2024-07-20  → NOT yet due by 30-Jun ✘ excluded
        svc.Post(new Voucher(Guid.NewGuid(), journal.Id, new DateOnly(2024, 4, 10), new[]
        {
            new EntryLine(debtor.Id, Money.FromRupees(60000m), DrCr.Debit, new[]
            {
                new BillAllocation(BillRefType.NewRef, "INV-1", Money.FromRupees(60000m),
                    dueDate: new DateOnly(2024, 5, 10)),
            }),
            new EntryLine(sales.Id, Money.FromRupees(60000m), DrCr.Credit),
        }));
        svc.Post(new Voucher(Guid.NewGuid(), journal.Id, new DateOnly(2024, 6, 20), new[]
        {
            new EntryLine(debtor.Id, Money.FromRupees(40000m), DrCr.Debit, new[]
            {
                new BillAllocation(BillRefType.NewRef, "INV-2", Money.FromRupees(40000m),
                    dueDate: new DateOnly(2024, 7, 20)),
            }),
            new EntryLine(sales.Id, Money.FromRupees(40000m), DrCr.Credit),
        }));

        // 🔴 THE BOUNDARY CASE, and it is the one an off-by-one gets wrong:
        //   INV-3  25,000  due EXACTLY 2024-06-30 (the statement date) → it HAS fallen due ✔ counts.
        //   Note this bill scores 0 overdue days and so sits in the ageing "Not due" bucket — "due till
        //   today" is inclusive of today and is deliberately one day wider than "overdue".
        svc.Post(new Voucher(Guid.NewGuid(), journal.Id, new DateOnly(2024, 6, 1), new[]
        {
            new EntryLine(debtor.Id, Money.FromRupees(25000m), DrCr.Debit, new[]
            {
                new BillAllocation(BillRefType.NewRef, "INV-3", Money.FromRupees(25000m),
                    dueDate: new DateOnly(2024, 6, 30)),
            }),
            new EntryLine(sales.Id, Money.FromRupees(25000m), DrCr.Credit),
        }));

        // Payables.
        //   BILL-1 50,000  due 2024-05-30  → fallen due ✔ counts
        //   BILL-2 30,000  due 2024-08-09  → NOT yet due ✘ excluded
        svc.Post(new Voucher(Guid.NewGuid(), journal.Id, new DateOnly(2024, 4, 15), new[]
        {
            new EntryLine(purchases.Id, Money.FromRupees(50000m), DrCr.Debit),
            new EntryLine(creditor.Id, Money.FromRupees(50000m), DrCr.Credit, new[]
            {
                new BillAllocation(BillRefType.NewRef, "BILL-1", Money.FromRupees(50000m),
                    dueDate: new DateOnly(2024, 5, 30)),
            }),
        }));
        svc.Post(new Voucher(Guid.NewGuid(), journal.Id, new DateOnly(2024, 6, 25), new[]
        {
            new EntryLine(purchases.Id, Money.FromRupees(30000m), DrCr.Debit),
            new EntryLine(creditor.Id, Money.FromRupees(30000m), DrCr.Credit, new[]
            {
                new BillAllocation(BillRefType.NewRef, "BILL-2", Money.FromRupees(30000m),
                    dueDate: new DateOnly(2024, 8, 9)),
            }),
        }));

        var ra = RatioAnalysis.Build(c, asOf);

        // ---- Hand-computed ----
        // Closing debtor   = 60,000 + 40,000 + 25,000 = 1,25,000
        //   due till today = 60,000 (INV-1) + 25,000 (INV-3, due exactly today) = 85,000; INV-2 not yet due.
        // Closing creditor = 50,000 + 30,000 = 80,000 ; due till today = 50,000 (BILL-2 not yet due).
        Assert.Equal(Money.FromRupees(125000m), ra.SundryDebtorsClosing);
        Assert.Equal(Money.FromRupees(80000m), ra.SundryCreditorsClosing);
        Assert.Equal(Money.FromRupees(85000m), ra.SundryDebtorsDueTillToday);
        Assert.Equal(Money.FromRupees(50000m), ra.SundryCreditorsDueTillToday);

        // The whole point of the fixture: the two bases genuinely differ, so an implementation that
        // reverted to the closing balance CANNOT pass the assertions above by coincidence.
        Assert.NotEqual(ra.SundryDebtorsClosing, ra.SundryDebtorsDueTillToday);
        Assert.NotEqual(ra.SundryCreditorsClosing, ra.SundryCreditorsDueTillToday);

        // Receivables Turnover (days) = due-till-today debtors ÷ Sales × days-in-period.
        //   Default window = books-begin 2024-04-01 → 2024-06-30 inclusive = 30 + 31 + 30 = 91 days.
        //   Sales = 60,000 + 40,000 + 25,000 = 1,25,000.
        //   85,000 ÷ 1,25,000 × 91 = 0.68 × 91 = 61.88 days.
        Assert.Equal(Money.FromRupees(125000m), ra.Sales);
        Assert.Equal(61.88m, Math.Round(ra.ReceivablesTurnoverDays!.Value, 4));

        // 🔴 THE ANTI-REVERT ASSERTION. Had the numerator stayed the closing balance the ratio would be
        //   1,25,000 ÷ 1,25,000 × 91 = 91 days exactly. Naming the wrong answer makes the test fail loudly
        //   with the defect's own figure rather than a bare inequality.
        Assert.NotEqual(91m, Math.Round(ra.ReceivablesTurnoverDays!.Value, 4));
        // 🔴 AND THE OFF-BY-ONE. Dropping INV-3 (due exactly today) would give 60,000 ÷ 1,25,000 × 91 =
        //   43.68 days, so a strict "<" boundary is caught by figure, not by luck.
        Assert.NotEqual(43.68m, Math.Round(ra.ReceivablesTurnoverDays!.Value, 4));

        // The published Principal-Group rows carry the vendor's captions AND the bill-wise values — the
        // report a user actually reads, not just the typed members a test can reach.
        var debtorLine = Assert.Single(ra.PrincipalGroups, g => g.Label == "Sundry Debtors (due till today)");
        var creditorLine = Assert.Single(ra.PrincipalGroups, g => g.Label == "Sundry Creditors (due till today)");
        Assert.Equal(Money.FromRupees(85000m), debtorLine.Value);
        Assert.Equal(Money.FromRupees(50000m), creditorLine.Value);

        // And it agrees with the Outstandings report on the same book — the reuse that keeps the two
        // screens from disagreeing about one set of bills.
        var outstandings = Outstandings.Build(c, asOf);
        Assert.Equal(outstandings.ReceivableDueTillToday, ra.SundryDebtorsDueTillToday);
        Assert.Equal(outstandings.PayableDueTillToday, ra.SundryCreditorsDueTillToday);
        // Sanity: total pending (1,25,000) exceeds due-till-today (85,000) precisely by the un-due INV-2.
        Assert.Equal(Money.FromRupees(125000m), outstandings.TotalReceivable);
        // And INV-3 really is in the "Not due" ageing bucket while still counting as due till today —
        // the one-day distinction, asserted rather than only described.
        var inv3 = Assert.Single(outstandings.Receivables, b => b.Reference == "INV-3");
        Assert.Equal(0, inv3.OverdueDays(asOf));
        Assert.True(inv3.IsDueBy(asOf));
    }
}
