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

        // Receivables Turnover (days) — 🔴 N/A ON BRIGHT, AND THIS ASSERTION HAS BEEN WRONG TWICE.
        //   It first read 175 days (35,000 ÷ 73,000 × 365), computed from the CLOSING debtor balance — the
        //   T0-27 defect itself, since the vendor defines the ratio "irrespective of the outstanding balance on
        //   the statement date". Closing T0-27 then rewrote it to an exact 0, which is WORSE: Ram & Co does not
        //   maintain bill-wise details, so the bill-wise numerator is 0 because the book RECORDS NO BILLS, not
        //   because customers pay instantly — and "0 days" printed beside a Balance Sheet showing 35,000 of
        //   debtors is a figure a bank or an auditor reads as fact. The only honest answer this book supports is
        //   the one the report already has for an unanswerable ratio: null, rendered "N/A".
        Assert.Null(ra.ReceivablesTurnoverDays);
        // The money that forces it: 35,000 of Sundry Debtors that no bill can account for.
        Assert.Equal(Money.FromRupees(35000m),
            Outstandings.BillWiseBlindClosing(f.Company, f.AsOf, "Sundry Debtors"));
        // 🔴 ANTI-REVERT, BY FIGURE. Both historic wrong answers are named so a regression fails with its own
        // number rather than a bare "expected null". 175 = the closing-balance numerator; 0 = the confident zero.
        // The sentinel stands for "withheld" and is neither, so a correct null passes both.
        var published = ra.ReceivablesTurnoverDays ?? decimal.MinValue;
        Assert.NotEqual(175m, published);
        Assert.NotEqual(0m, published);

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
        // Receivables Turnover is null on Robert for TWO independent reasons, and this test no longer claims to
        // isolate the zero-denominator one: Sales = 0, AND Robert's debtors keep no bill-wise details so the
        // numerator is unknowable. The zero-denominator guard is proved in isolation by InventoryTurnover below
        // (Stock = 0 while the book IS otherwise measurable) and the bill-wise guard by its own test.
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

        // 🔴 AND THE REASON THE FIGURE IS PUBLISHABLE AT ALL ON THIS BOOK: every rupee of Sundry Debtors is
        // carried by a bill-wise ledger, so the bill-wise numerator is a COMPLETE measurement, not a partial
        // one. This is the zero that the withholding guard tests below are the complement of.
        Assert.Equal(Money.Zero, Outstandings.BillWiseBlindClosing(c, asOf, "Sundry Debtors"));
    }

    /// <summary>
    /// 🔴 THE DEFAULT BOOK SHAPE, AND THE FIGURE A BANK READS. <c>MaintainBillByBill</c> defaults to
    /// <c>false</c>, so a book whose debtors carry real balances and no bills at all is the ordinary case, not
    /// an edge case. Closing T0-27 correctly moved Receivables Turnover onto the bill-wise projection; on this
    /// book that projection sees nothing, and the report published a confident <b>"0 days"</b> beside a Balance
    /// Sheet showing the money. Zero is the one answer that is definitely wrong: it states that customers pay
    /// instantly. The ratio must be WITHHELD — the same <c>null</c> → "N/A" the report already uses for an
    /// unanswerable ratio.
    /// <para>This test isolates the new guard: Sales is NON-zero here, so the pre-existing zero-denominator
    /// guard cannot be what produces the null.</para>
    /// </summary>
    [Fact]
    public void RatioAnalysis_withholds_receivables_turnover_when_debtor_money_is_invisible_to_bills()
    {
        // Books begin 2024-04-01; statement date 2024-06-30 → window 30 + 31 + 30 = 91 days inclusive.
        var c = Services.CompanyFactory.CreateSeeded(
            "Plain Books Co", new DateOnly(2024, 4, 1), new DateOnly(2024, 4, 1));
        var asOf = new DateOnly(2024, 6, 30);
        var journal = c.FindVoucherTypeByName("Journal")!;

        var sales = new Domain.Ledger(Guid.NewGuid(), "Sales", c.FindGroupByName("Sales Accounts")!.Id,
            Money.Zero, openingIsDebit: false);
        c.AddLedger(sales);

        // 🔴 NO maintainBillByBill argument — this is the DEFAULT, and that is the whole point of the fixture.
        var debtor = new Domain.Ledger(Guid.NewGuid(), "Plain Co", c.FindGroupByName("Sundry Debtors")!.Id,
            Money.Zero, openingIsDebit: true);
        c.AddLedger(debtor);
        Assert.False(debtor.MaintainBillByBill);

        var svc = new Services.LedgerService(c);
        svc.Post(new Voucher(Guid.NewGuid(), journal.Id, new DateOnly(2024, 4, 20), new[]
        {
            new EntryLine(debtor.Id, Money.FromRupees(100000m), DrCr.Debit),
            new EntryLine(sales.Id, Money.FromRupees(100000m), DrCr.Credit),
        }));

        var ra = RatioAnalysis.Build(c, asOf);

        // ---- Hand-computed ----
        // Closing debtor = 1,00,000 and the Balance Sheet shows it. Bills = none, so due till today = 0.
        Assert.Equal(Money.FromRupees(100000m), ra.SundryDebtorsClosing);
        Assert.Equal(Money.Zero, ra.SundryDebtorsDueTillToday);
        Assert.Equal(Money.FromRupees(100000m), ra.Sales);
        // The measured blind spot: 1,00,000 of debtors that no bill accounts for.
        Assert.Equal(Money.FromRupees(100000m), Outstandings.BillWiseBlindClosing(c, asOf, "Sundry Debtors"));

        // 🔴 THE ASSERTION THIS WHOLE TRACK EXISTS FOR.
        Assert.Null(ra.ReceivablesTurnoverDays);

        // 🔴 ANTI-REVERT, BY FIGURE — both wrong answers named, so a regression fails with its own number.
        //   0 days  = the bill-wise numerator published as fact (the defect this test closes).
        //   91 days = 1,00,000 ÷ 1,00,000 × 91, i.e. a revert to the closing balance (which would reopen T0-27).
        var published = ra.ReceivablesTurnoverDays ?? decimal.MinValue;
        Assert.NotEqual(0m, published);
        Assert.NotEqual(91m, published);

        // Sales is non-zero, so the OTHER ratios that divide by Sales are real numbers on this same book —
        // proof that the null above comes from the new bill-wise guard and not from a zero denominator.
        Assert.NotNull(ra.GrossProfitPercent);
        Assert.NotNull(ra.NetProfitPercent);

        // The two Principal-Group rows still publish their "(due till today)" figures, qualifier and all: they
        // say what they are in their caption, which a bare ratio row cannot.
        var debtorLine = Assert.Single(ra.PrincipalGroups, g => g.Label == "Sundry Debtors (due till today)");
        Assert.Equal(Money.Zero, debtorLine.Value);
    }

    /// <summary>
    /// The MIXED book: one debtor keeps bill-wise details, another does not. A ratio built here would divide a
    /// partial numerator (one party's bills) by whole-book Sales — a mixed-basis figure that is wrong in a way
    /// no reader can see, which is the same class of defect as feeding it a scenario denominator. It is withheld.
    /// </summary>
    [Fact]
    public void RatioAnalysis_withholds_receivables_turnover_when_only_some_debtors_are_bill_wise()
    {
        var c = Services.CompanyFactory.CreateSeeded(
            "Mixed Books Co", new DateOnly(2024, 4, 1), new DateOnly(2024, 4, 1));
        var asOf = new DateOnly(2024, 6, 30);
        var journal = c.FindVoucherTypeByName("Journal")!;

        var sales = new Domain.Ledger(Guid.NewGuid(), "Sales", c.FindGroupByName("Sales Accounts")!.Id,
            Money.Zero, openingIsDebit: false);
        c.AddLedger(sales);
        var tracked = new Domain.Ledger(Guid.NewGuid(), "Tracked Ltd", c.FindGroupByName("Sundry Debtors")!.Id,
            Money.Zero, openingIsDebit: true, maintainBillByBill: true);
        c.AddLedger(tracked);
        var untracked = new Domain.Ledger(Guid.NewGuid(), "Untracked Ltd", c.FindGroupByName("Sundry Debtors")!.Id,
            Money.Zero, openingIsDebit: true);
        c.AddLedger(untracked);

        var svc = new Services.LedgerService(c);
        // Tracked: a 70,000 bill already fallen due → the bill-wise projection DOES see 70,000.
        svc.Post(new Voucher(Guid.NewGuid(), journal.Id, new DateOnly(2024, 4, 10), new[]
        {
            new EntryLine(tracked.Id, Money.FromRupees(70000m), DrCr.Debit, new[]
            {
                new BillAllocation(BillRefType.NewRef, "INV-T1", Money.FromRupees(70000m),
                    dueDate: new DateOnly(2024, 5, 1)),
            }),
            new EntryLine(sales.Id, Money.FromRupees(70000m), DrCr.Credit),
        }));
        // Untracked: 30,000 the projection cannot see at all.
        svc.Post(new Voucher(Guid.NewGuid(), journal.Id, new DateOnly(2024, 4, 11), new[]
        {
            new EntryLine(untracked.Id, Money.FromRupees(30000m), DrCr.Debit),
            new EntryLine(sales.Id, Money.FromRupees(30000m), DrCr.Credit),
        }));

        var ra = RatioAnalysis.Build(c, asOf);

        // ---- Hand-computed ---- closing debtors 1,00,000; bill-wise due till today 70,000; blind 30,000.
        Assert.Equal(Money.FromRupees(100000m), ra.SundryDebtorsClosing);
        Assert.Equal(Money.FromRupees(70000m), ra.SundryDebtorsDueTillToday);
        Assert.Equal(Money.FromRupees(30000m), Outstandings.BillWiseBlindClosing(c, asOf, "Sundry Debtors"));
        Assert.Equal(Money.FromRupees(100000m), ra.Sales);

        Assert.Null(ra.ReceivablesTurnoverDays);
        // 🔴 The partial-basis figure that must NOT be published: 70,000 ÷ 1,00,000 × 91 = 63.7 days.
        Assert.NotEqual(63.7m, ra.ReceivablesTurnoverDays ?? decimal.MinValue);
    }

    /// <summary>
    /// The blind-spot measure sums MAGNITUDES, not signed balances, and this is the book that makes the
    /// difference matter: a customer advance sits as a CREDIT balance under Sundry Debtors, so two blind
    /// ledgers on opposite sides can net to exactly zero while neither is visible to any bill. Netting them
    /// would report the book as fully covered and let the ratio publish a number built on nothing.
    /// </summary>
    [Fact]
    public void BillWise_blind_closing_sums_magnitudes_so_opposite_blind_balances_cannot_cancel()
    {
        var c = Services.CompanyFactory.CreateSeeded(
            "Contra Debtors Co", new DateOnly(2024, 4, 1), new DateOnly(2024, 4, 1));
        var asOf = new DateOnly(2024, 6, 30);
        var journal = c.FindVoucherTypeByName("Journal")!;

        var sales = new Domain.Ledger(Guid.NewGuid(), "Sales", c.FindGroupByName("Sales Accounts")!.Id,
            Money.Zero, openingIsDebit: false);
        c.AddLedger(sales);
        var cash = new Domain.Ledger(Guid.NewGuid(), "Cash A", c.FindGroupByName("Cash-in-Hand")!.Id,
            Money.Zero, openingIsDebit: true);
        c.AddLedger(cash);
        // Both blind (no bill-wise details), and their closing balances are equal and opposite.
        var owing = new Domain.Ledger(Guid.NewGuid(), "Owes Us Ltd", c.FindGroupByName("Sundry Debtors")!.Id,
            Money.Zero, openingIsDebit: true);
        c.AddLedger(owing);
        var advance = new Domain.Ledger(Guid.NewGuid(), "Paid Ahead Ltd", c.FindGroupByName("Sundry Debtors")!.Id,
            Money.Zero, openingIsDebit: true);
        c.AddLedger(advance);

        var svc = new Services.LedgerService(c);
        svc.Post(new Voucher(Guid.NewGuid(), journal.Id, new DateOnly(2024, 4, 20), new[]
        {
            new EntryLine(owing.Id, Money.FromRupees(45000m), DrCr.Debit),
            new EntryLine(sales.Id, Money.FromRupees(45000m), DrCr.Credit),
        }));
        // An advance received: Sundry Debtors goes CREDIT by the same 45,000.
        svc.Post(new Voucher(Guid.NewGuid(), journal.Id, new DateOnly(2024, 4, 21), new[]
        {
            new EntryLine(cash.Id, Money.FromRupees(45000m), DrCr.Debit),
            new EntryLine(advance.Id, Money.FromRupees(45000m), DrCr.Credit),
        }));

        // 🔴 Netted, these two are 0. Summed as magnitudes they are 90,000 — and 90,000 is the truth about
        // how much money no bill can account for.
        Assert.Equal(Money.FromRupees(90000m), Outstandings.BillWiseBlindClosing(c, asOf, "Sundry Debtors"));
        Assert.NotEqual(Money.Zero, Outstandings.BillWiseBlindClosing(c, asOf, "Sundry Debtors"));

        // …so the ratio is withheld on this book too, even though the net debtor position is zero.
        Assert.Null(RatioAnalysis.Build(c, asOf).ReceivablesTurnoverDays);
    }

    /// <summary>
    /// F3 — the scenario must reach the <b>numerator</b>, not only the Balance Sheet and P&amp;L around it.
    /// <c>Outstandings.Build</c> had no scenario overload, so with a scenario active Receivables Turnover
    /// divided an ACTUAL-book numerator by a scenario-basis denominator: a mixed-basis figure wrong on both
    /// readings, and invisible because every number on screen looked plausible.
    /// </summary>
    [Fact]
    public void RatioAnalysis_and_outstandings_honour_the_scenario_in_the_due_till_today_numerator()
    {
        var c = Services.CompanyFactory.CreateSeeded(
            "Scenario Books Co", new DateOnly(2024, 4, 1), new DateOnly(2024, 4, 1));
        var asOf = new DateOnly(2024, 6, 30);   // window 2024-04-01 → 2024-06-30 inclusive = 91 days
        var journal = c.FindVoucherTypeByName("Journal")!;

        var sales = new Domain.Ledger(Guid.NewGuid(), "Sales", c.FindGroupByName("Sales Accounts")!.Id,
            Money.Zero, openingIsDebit: false);
        c.AddLedger(sales);
        var cash = new Domain.Ledger(Guid.NewGuid(), "Cash A", c.FindGroupByName("Cash-in-Hand")!.Id,
            Money.Zero, openingIsDebit: true);
        c.AddLedger(cash);
        var debtor = new Domain.Ledger(Guid.NewGuid(), "Acme Ltd", c.FindGroupByName("Sundry Debtors")!.Id,
            Money.Zero, openingIsDebit: true, maintainBillByBill: true);
        c.AddLedger(debtor);

        var svc = new Services.LedgerService(c);
        // REAL: INV-1 60,000 due 2024-05-10 (fallen due) and INV-2 40,000 due 2024-07-20 (not yet due).
        svc.Post(new Voucher(Guid.NewGuid(), journal.Id, new DateOnly(2024, 4, 10), new[]
        {
            new EntryLine(debtor.Id, Money.FromRupees(60000m), DrCr.Debit, new[]
            {
                new BillAllocation(BillRefType.NewRef, "INV-1", Money.FromRupees(60000m),
                    dueDate: new DateOnly(2024, 5, 10)),
            }),
            new EntryLine(sales.Id, Money.FromRupees(60000m), DrCr.Credit),
        }));
        svc.Post(new Voucher(Guid.NewGuid(), journal.Id, new DateOnly(2024, 4, 12), new[]
        {
            new EntryLine(debtor.Id, Money.FromRupees(40000m), DrCr.Debit, new[]
            {
                new BillAllocation(BillRefType.NewRef, "INV-2", Money.FromRupees(40000m),
                    dueDate: new DateOnly(2024, 7, 20)),
            }),
            new EntryLine(sales.Id, Money.FromRupees(40000m), DrCr.Credit),
        }));
        // OPTIONAL (provisional — never in the real books): a 20,000 receipt knocking off part of INV-1. It
        // touches the NUMERATOR only, so the ratio moves for exactly one reason.
        svc.Post(new Voucher(Guid.NewGuid(), journal.Id, new DateOnly(2024, 6, 15), new[]
        {
            new EntryLine(cash.Id, Money.FromRupees(20000m), DrCr.Debit),
            new EntryLine(debtor.Id, Money.FromRupees(20000m), DrCr.Credit, new[]
            {
                new BillAllocation(BillRefType.AgstRef, "INV-1", Money.FromRupees(20000m)),
            }),
        }, optional: true));

        var scenario = new Scenario(Guid.NewGuid(), "What-if", includeActuals: true,
            includedTypeIds: new[] { journal.Id });

        // ---- The projection itself honours the scenario ----
        // Actual books: the optional receipt is invisible, so INV-1 still stands at 60,000 and is due.
        Assert.Equal(Money.FromRupees(60000m), Outstandings.Build(c, asOf).ReceivableDueTillToday);
        // Under the scenario the receipt surfaces: INV-1 falls to 40,000; INV-2 is still not due.
        Assert.Equal(Money.FromRupees(40000m), Outstandings.Build(c, asOf, scenario).ReceivableDueTillToday);

        // ---- And so does the ratio, on ONE basis end to end ----
        // Sales = 60,000 + 40,000 = 1,00,000 on BOTH bases (the receipt touches no sales ledger), so the only
        // thing that may move is the numerator — which is exactly what F3 said never moved.
        var actual = RatioAnalysis.Build(c, asOf, ReportOptions.AsOf(asOf));
        var whatIf = RatioAnalysis.Build(c, asOf, ReportOptions.AsOf(asOf).WithScenario(scenario));
        Assert.Equal(Money.FromRupees(100000m), actual.Sales);
        Assert.Equal(Money.FromRupees(100000m), whatIf.Sales);

        // Hand-computed: 60,000 ÷ 1,00,000 × 91 = 54.6 days actual; 40,000 ÷ 1,00,000 × 91 = 36.4 under the
        // scenario. 🔴 Before the fix BOTH read 54.6 — the scenario column silently showed the actual numerator.
        Assert.Equal(54.6m, Math.Round(actual.ReceivablesTurnoverDays!.Value, 4));
        Assert.Equal(36.4m, Math.Round(whatIf.ReceivablesTurnoverDays!.Value, 4));
        Assert.NotEqual(actual.ReceivablesTurnoverDays, whatIf.ReceivablesTurnoverDays);

        // The published Sundry Debtors row moves with it rather than contradicting the ratio beside it.
        Assert.Equal(Money.FromRupees(60000m), actual.SundryDebtorsDueTillToday);
        Assert.Equal(Money.FromRupees(40000m), whatIf.SundryDebtorsDueTillToday);
    }

    /// <summary>
    /// F9 — <c>Outstandings.Build</c> now makes ONE pass over the vouchers instead of one pass per bill-wise
    /// ledger. This pins the refactor's only obligation: the rows it emits must be identical, in the same
    /// order, to the per-ledger <c>OpenBillsFor</c> projection it replaced. A multi-party book with
    /// interleaved vouchers is used, because a single-party book cannot tell the two shapes apart.
    /// </summary>
    [Fact]
    public void Outstandings_build_emits_exactly_the_per_ledger_projection_in_the_same_order()
    {
        var c = Services.CompanyFactory.CreateSeeded(
            "Many Parties Co", new DateOnly(2024, 4, 1), new DateOnly(2024, 4, 1));
        var asOf = new DateOnly(2024, 6, 30);
        var journal = c.FindVoucherTypeByName("Journal")!;

        var sales = new Domain.Ledger(Guid.NewGuid(), "Sales", c.FindGroupByName("Sales Accounts")!.Id,
            Money.Zero, openingIsDebit: false);
        c.AddLedger(sales);
        var purchases = new Domain.Ledger(Guid.NewGuid(), "Purchases", c.FindGroupByName("Purchase Accounts")!.Id,
            Money.Zero, openingIsDebit: true);
        c.AddLedger(purchases);

        var debtors = new List<Domain.Ledger>();
        var creditors = new List<Domain.Ledger>();
        for (var i = 1; i <= 4; i++)
        {
            var d = new Domain.Ledger(Guid.NewGuid(), $"Customer {i}", c.FindGroupByName("Sundry Debtors")!.Id,
                Money.Zero, openingIsDebit: true, maintainBillByBill: true);
            c.AddLedger(d);
            debtors.Add(d);
            var cr = new Domain.Ledger(Guid.NewGuid(), $"Vendor {i}", c.FindGroupByName("Sundry Creditors")!.Id,
                Money.Zero, openingIsDebit: false, maintainBillByBill: true);
            c.AddLedger(cr);
            creditors.Add(cr);
        }

        var svc = new Services.LedgerService(c);
        // Interleave the parties ACROSS vouchers so per-ledger and single-pass orderings could diverge.
        for (var v = 0; v < 6; v++)
        {
            for (var i = 0; i < 4; i++)
            {
                var day = new DateOnly(2024, 4, 2).AddDays(v * 5 + i);
                var amount = Money.FromRupees(1000m * (v + 1) + 100m * (i + 1));
                svc.Post(new Voucher(Guid.NewGuid(), journal.Id, day, new[]
                {
                    new EntryLine(debtors[i].Id, amount, DrCr.Debit, new[]
                    {
                        new BillAllocation(BillRefType.NewRef, $"D{i}-{v}", amount,
                            dueDate: day.AddDays(20)),
                    }),
                    new EntryLine(sales.Id, amount, DrCr.Credit),
                }));
                svc.Post(new Voucher(Guid.NewGuid(), journal.Id, day, new[]
                {
                    new EntryLine(purchases.Id, amount, DrCr.Debit),
                    new EntryLine(creditors[i].Id, amount, DrCr.Credit, new[]
                    {
                        new BillAllocation(BillRefType.NewRef, $"C{i}-{v}", amount,
                            dueDate: day.AddDays(20)),
                    }),
                }));
            }
        }

        var report = Outstandings.Build(c, asOf);

        // Rebuild the SAME thing the old per-ledger loop produced: company.Ledgers order, each ledger's own
        // first-opened order, receivables and payables split by kind.
        var expectedReceivable = new List<OutstandingBill>();
        var expectedPayable = new List<OutstandingBill>();
        foreach (var ledger in c.Ledgers)
        {
            if (!ledger.MaintainBillByBill) continue;
            foreach (var bill in Outstandings.OpenBillsFor(c, ledger, asOf))
            {
                if (bill.Kind == OutstandingKind.Receivable) expectedReceivable.Add(bill);
                else expectedPayable.Add(bill);
            }
        }

        Assert.NotEmpty(expectedReceivable);
        Assert.NotEmpty(expectedPayable);
        Assert.Equal(expectedReceivable, report.Receivables);   // records ⇒ value equality, order included
        Assert.Equal(expectedPayable, report.Payables);
        Assert.Equal(4 * 6, expectedReceivable.Count);
        Assert.Equal(4 * 6, expectedPayable.Count);

        // 🔴 THE ASSERTION ABOVE IS NOT ENOUGH ON ITS OWN AND A MUTATION PROVED IT. Build and OpenBillsFor
        // share the emitter, so breaking the emitter's ordering breaks BOTH sides identically and the
        // comparison stays green — a self-consistency test wearing an ordering test's clothes. So the expected
        // sequence is also spelled out INDEPENDENTLY of any production code path: party by party in
        // company.Ledgers order, and within each party in first-opened (voucher-date) order.
        var expectedRefs = new List<string>();
        for (var i = 0; i < 4; i++)
            for (var v = 0; v < 6; v++)
                expectedRefs.Add($"D{i}-{v}");
        Assert.Equal(expectedRefs, report.Receivables.Select(b => b.Reference).ToList());

        var expectedPayableRefs = new List<string>();
        for (var i = 0; i < 4; i++)
            for (var v = 0; v < 6; v++)
                expectedPayableRefs.Add($"C{i}-{v}");
        Assert.Equal(expectedPayableRefs, report.Payables.Select(b => b.Reference).ToList());

        // Each party's rows are CONTIGUOUS — the grouping the per-ledger loop gave for free and that a single
        // voucher pass could silently interleave.
        var ledgerRuns = new List<Guid>();
        foreach (var bill in report.Receivables)
            if (ledgerRuns.Count == 0 || ledgerRuns[^1] != bill.LedgerId)
                ledgerRuns.Add(bill.LedgerId);
        Assert.Equal(4, ledgerRuns.Count);
        Assert.Equal(debtors.Select(d => d.Id).ToList(), ledgerRuns);
        // And the ageing/total roll-ups built on top of them agree.
        Assert.Equal(report.TotalReceivable, report.ReceivableAgeing.Aggregate(
            Money.Zero, (acc, b) => acc + b.Pending));
    }
}
