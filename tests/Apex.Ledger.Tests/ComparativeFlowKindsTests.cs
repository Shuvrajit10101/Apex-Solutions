using Apex.Ledger.Domain;
using Apex.Ledger.Reports;
using Apex.Ledger.Services;
using Xunit;
using Domain = Apex.Ledger.Domain;

namespace Apex.Ledger.Tests;

/// <summary>
/// Census 11.14 / 11.17 (cluster C3) ENGINE tests: the two flow statements the comparative engine could not
/// compose — <see cref="CashFlow"/> and <see cref="FundsFlow"/> — now compose as
/// <see cref="ComparativeReportKind"/> members, and the Alt+N axis repeats at the vendor's own periodicities.
///
/// <para>These pin the things a comparative column can get wrong and a green suite would not notice:</para>
/// <list type="bullet">
/// <item>a one-column comparative reproduces the plain single-column statement exactly (no drift);</item>
/// <item>two columns over the SAME window do not duplicate or drop a line (the census's explicit demand);</item>
/// <item>a counterparty that changes side between periods stays ONE row, signed — not two half-blank rows;</item>
/// <item>the folded Funds-Flow operations line appears exactly once per column under one stable key;</item>
/// <item>period columns partition the window, so the flow columns sum back to the single column;</item>
/// <item>a scenario column on a scenario-blind kind is REFUSED rather than silently reproducing the actuals.</item>
/// </list>
/// </summary>
public class ComparativeFlowKindsTests
{
    private static FixtureLoader.LoadedFixture Robert() => FixtureLoader.Load("robert.json");

    private static ComparativeReport.ColumnSpec FullPeriod(FixtureLoader.LoadedFixture f, string label)
        => new(label, new PeriodRange(f.Company.BooksBeginFrom, f.AsOf), Scenario: null);

    // ============================================================ Cash Flow ====================

    [Fact]
    [Trait("Category", "Fixture")]
    public void CashFlow_single_spec_reproduces_the_plain_single_column_statement()
    {
        var f = Robert();
        var period = new PeriodRange(f.Company.BooksBeginFrom, f.AsOf);
        var single = CashFlow.Build(f.Company, period);

        var cmp = ComparativeReport.Build(
            f.Company, ComparativeReportKind.CashFlow, new[] { FullPeriod(f, "Current") });

        Assert.Single(cmp.Columns);
        Assert.Equal(single.OpeningBalance, cmp.Columns[0].CashOpening);
        Assert.Equal(single.NetCashFlow, cmp.Columns[0].CashNetFlow);
        Assert.Equal(single.ClosingBalance, cmp.Columns[0].CashClosing);

        // Every single-column line is present exactly once, with the sign carrying its side.
        Assert.Equal(single.Inflows.Count + single.Outflows.Count, cmp.Rows.Count);
        foreach (var line in single.Inflows)
        {
            var row = Assert.Single(cmp.Rows, r => r.Label == line.Name);
            Assert.Equal(line.Amount, row.Values[0]);
        }
        foreach (var line in single.Outflows)
        {
            var row = Assert.Single(cmp.Rows, r => r.Label == line.Name);
            Assert.Equal(-line.Amount, row.Values[0]);
        }
    }

    [Fact]
    [Trait("Category", "Fixture")]
    public void CashFlow_two_columns_over_one_window_duplicate_nothing_and_drop_nothing()
    {
        // 🔴 The census's explicit demand: a comparative column multiplies every figure it shows, so prove a
        // two-period comparison of a SINGLE-period book neither duplicates a line nor loses one.
        var f = Robert();
        var single = CashFlow.Build(f.Company, new PeriodRange(f.Company.BooksBeginFrom, f.AsOf));

        var cmp = ComparativeReport.Build(f.Company, ComparativeReportKind.CashFlow,
            new[] { FullPeriod(f, "A"), FullPeriod(f, "B") });

        Assert.Equal(2, cmp.Columns.Count);
        // Row COUNT is the single-column line count — not twice it.
        Assert.Equal(single.Inflows.Count + single.Outflows.Count, cmp.Rows.Count);
        // No key appears twice.
        Assert.Equal(cmp.Rows.Count, cmp.Rows.Select(r => r.Key).Distinct().Count());
        // Both columns are fully populated and identical — nothing dropped into a blank.
        foreach (var row in cmp.Rows)
        {
            Assert.NotNull(row.Values[0]);
            Assert.NotNull(row.Values[1]);
            Assert.Equal(row.Values[0], row.Values[1]);
        }
        Assert.Equal(cmp.Columns[0].CashClosing, cmp.Columns[1].CashClosing);
    }

    [Fact]
    public void CashFlow_a_counterparty_that_changes_side_between_periods_stays_one_signed_row()
    {
        // 🔴 The merge key is the LEDGER, not the ledger-and-side. A debtor that pays down in April (releasing
        // cash) and grows again in May (absorbing it) must be ONE row with a +ve and a −ve, not two rows each
        // blank where the other has a figure. Keying by side would read as two different parties.
        var c = FlowSeed(out var cash, out var debtor);
        var svc = new LedgerService(c);
        var receipt = c.FindVoucherTypeByName("Receipt")!.Id;
        var journal = c.FindVoucherTypeByName("Journal")!.Id;

        // April: the debtor pays 40,000 — debtor credit-ward, cash in (an INFLOW).
        svc.Post(new Voucher(Guid.NewGuid(), receipt, new DateOnly(2024, 4, 10),
            new[]
            {
                new EntryLine(cash.Id, Money.FromRupees(40000m), DrCr.Debit),
                new EntryLine(debtor.Id, Money.FromRupees(40000m), DrCr.Credit),
            }));
        // May: the debtor grows 15,000 against cash — debtor debit-ward (an OUTFLOW of the cash pool).
        svc.Post(new Voucher(Guid.NewGuid(), journal, new DateOnly(2024, 5, 12),
            new[]
            {
                new EntryLine(debtor.Id, Money.FromRupees(15000m), DrCr.Debit),
                new EntryLine(cash.Id, Money.FromRupees(15000m), DrCr.Credit),
            }));

        var cmp = ComparativeReport.Build(c, ComparativeReportKind.CashFlow, new[]
        {
            new ComparativeReport.ColumnSpec("Apr", new PeriodRange(new DateOnly(2024, 4, 1), new DateOnly(2024, 4, 30))),
            new ComparativeReport.ColumnSpec("May", new PeriodRange(new DateOnly(2024, 5, 1), new DateOnly(2024, 5, 31))),
        });

        var row = Assert.Single(cmp.Rows, r => r.Label == "Debtor A");
        Assert.Equal(Money.FromRupees(40000m), row.Values[0]);   // April: released cash
        Assert.Equal(Money.FromRupees(-15000m), row.Values[1]);  // May: absorbed cash
    }

    [Fact]
    public void CashFlow_period_columns_partition_the_window_and_the_closing_carries_through()
    {
        var c = FlowSeed(out var cash, out var debtor);
        var svc = new LedgerService(c);
        var receipt = c.FindVoucherTypeByName("Receipt")!.Id;
        svc.Post(new Voucher(Guid.NewGuid(), receipt, new DateOnly(2024, 4, 10),
            new[]
            {
                new EntryLine(cash.Id, Money.FromRupees(40000m), DrCr.Debit),
                new EntryLine(debtor.Id, Money.FromRupees(40000m), DrCr.Credit),
            }));
        svc.Post(new Voucher(Guid.NewGuid(), receipt, new DateOnly(2024, 7, 3),
            new[]
            {
                new EntryLine(cash.Id, Money.FromRupees(25000m), DrCr.Debit),
                new EntryLine(debtor.Id, Money.FromRupees(25000m), DrCr.Credit),
            }));

        var period = new PeriodRange(new DateOnly(2024, 4, 1), new DateOnly(2025, 3, 31));
        var full = CashFlow.Build(c, period);

        var monthly = ComparativeReport.PeriodicColumns(period, ColumnPeriodicity.Monthly);
        var cmp = ComparativeReport.Build(c, ComparativeReportKind.CashFlow, monthly);

        Assert.Equal(12, cmp.Columns.Count);
        // The monthly net flows partition the window, so they sum to the full-period net flow.
        var sum = cmp.Columns.Aggregate(Money.Zero, (acc, col) => acc + col.CashNetFlow);
        Assert.Equal(full.NetCashFlow, sum);
        // The first column opens where the period opens and the last closes where the period closes.
        Assert.Equal(full.OpeningBalance, cmp.Columns[0].CashOpening);
        Assert.Equal(full.ClosingBalance, cmp.Columns[^1].CashClosing);
        // Each column reconciles opening + net == closing, the statement's own double-entry guarantee.
        foreach (var col in cmp.Columns)
            Assert.Equal(col.CashClosing, col.CashOpening + col.CashNetFlow);
    }

    // ============================================================ Funds Flow ===================

    [Fact]
    [Trait("Category", "Fixture")]
    public void FundsFlow_single_spec_reproduces_the_plain_single_column_statement()
    {
        var f = Robert();
        var period = new PeriodRange(f.Company.BooksBeginFrom, f.AsOf);
        var single = FundsFlow.Build(f.Company, period);

        var cmp = ComparativeReport.Build(
            f.Company, ComparativeReportKind.FundsFlow, new[] { FullPeriod(f, "Current") });

        Assert.Single(cmp.Columns);
        Assert.Equal(single.TotalSources, cmp.Columns[0].TotalSources);
        Assert.Equal(single.TotalApplications, cmp.Columns[0].TotalApplications);
        // The statement balances, and so does the column that reproduces it.
        Assert.Equal(cmp.Columns[0].TotalSources, cmp.Columns[0].TotalApplications);
        // Every line survives the projection exactly once (the folded operations line included, once).
        Assert.Equal(single.Sources.Count + single.Applications.Count, cmp.Rows.Count);
        Assert.Equal(cmp.Rows.Count, cmp.Rows.Select(r => r.Key).Distinct().Count());
    }

    [Fact]
    public void FundsFlow_the_operations_line_is_one_signed_row_across_a_profit_month_and_a_loss_month()
    {
        // 🔴 The single-column statement captions the folded result "Funds From Operations" when it is a source
        // and "Funds Lost In Operations" when it is an application — two captions for one quantity. Carried
        // through verbatim, a book that profits in one column and loses in the next would show TWO rows, each
        // blank in the other's column, and no operations line at all in half the grid.
        var c = FlowSeed(out var cash, out _);
        var svc = new LedgerService(c);
        var journal = c.FindVoucherTypeByName("Journal")!.Id;

        var sales = new Domain.Ledger(Guid.NewGuid(), "Sales", c.FindGroupByName("Sales Accounts")!.Id,
            Money.Zero, openingIsDebit: false);
        c.AddLedger(sales);

        var rent = new Domain.Ledger(Guid.NewGuid(), "Rent", c.FindGroupByName("Indirect Expenses")!.Id,
            Money.Zero, openingIsDebit: true);
        c.AddLedger(rent);

        // April: a 30,000 cash sale — a profit month.
        svc.Post(new Voucher(Guid.NewGuid(), journal, new DateOnly(2024, 4, 8),
            new[]
            {
                new EntryLine(cash.Id, Money.FromRupees(30000m), DrCr.Debit),
                new EntryLine(sales.Id, Money.FromRupees(30000m), DrCr.Credit),
            }));
        // May: 12,000 rent paid in cash and nothing earned — a loss month.
        svc.Post(new Voucher(Guid.NewGuid(), journal, new DateOnly(2024, 5, 8),
            new[]
            {
                new EntryLine(rent.Id, Money.FromRupees(12000m), DrCr.Debit),
                new EntryLine(cash.Id, Money.FromRupees(12000m), DrCr.Credit),
            }));

        var cmp = ComparativeReport.Build(c, ComparativeReportKind.FundsFlow, new[]
        {
            new ComparativeReport.ColumnSpec("Apr", new PeriodRange(new DateOnly(2024, 4, 1), new DateOnly(2024, 4, 30))),
            new ComparativeReport.ColumnSpec("May", new PeriodRange(new DateOnly(2024, 5, 1), new DateOnly(2024, 5, 31))),
        });

        // Exactly ONE operations row, carrying both months.
        var ops = Assert.Single(cmp.Rows, r => r.Label == "Funds From Operations");
        Assert.Equal(Money.FromRupees(30000m), ops.Values[0]);   // April profit: a source
        Assert.Equal(Money.FromRupees(-12000m), ops.Values[1]);  // May loss: an application, signed

        // And it is still exactly one row when the same month is compared with itself.
        var same = ComparativeReport.Build(c, ComparativeReportKind.FundsFlow, new[]
        {
            new ComparativeReport.ColumnSpec("A", new PeriodRange(new DateOnly(2024, 4, 1), new DateOnly(2024, 4, 30))),
            new ComparativeReport.ColumnSpec("B", new PeriodRange(new DateOnly(2024, 4, 1), new DateOnly(2024, 4, 30))),
        });
        Assert.Single(same.Rows, r => r.Label == "Funds From Operations");
    }

    [Fact]
    public void FundsFlow_every_period_column_balances_sources_against_applications()
    {
        var c = FlowSeed(out var cash, out var debtor);
        var svc = new LedgerService(c);
        var receipt = c.FindVoucherTypeByName("Receipt")!.Id;
        svc.Post(new Voucher(Guid.NewGuid(), receipt, new DateOnly(2024, 4, 10),
            new[]
            {
                new EntryLine(cash.Id, Money.FromRupees(40000m), DrCr.Debit),
                new EntryLine(debtor.Id, Money.FromRupees(40000m), DrCr.Credit),
            }));

        var period = new PeriodRange(new DateOnly(2024, 4, 1), new DateOnly(2025, 3, 31));
        var cmp = ComparativeReport.Build(
            c, ComparativeReportKind.FundsFlow,
            ComparativeReport.PeriodicColumns(period, ColumnPeriodicity.Quarterly));

        Assert.Equal(4, cmp.Columns.Count);
        foreach (var col in cmp.Columns)
            Assert.Equal(col.TotalSources, col.TotalApplications);
    }

    // ============================================================ scenario refusal =============

    [Theory]
    [InlineData(ComparativeReportKind.CashFlow)]
    [InlineData(ComparativeReportKind.FundsFlow)]
    public void A_scenario_column_is_REFUSED_on_the_scenario_blind_flow_kinds(ComparativeReportKind kind)
    {
        // 🔴 Both statements read unscenarioed ledger movements, so a "scenario" column would run the ACTUAL
        // books and render a column identical to the base one under the scenario's name — a what-if answer that
        // is not one. The engine refuses it loudly instead of composing it.
        var c = FlowSeed(out _, out _);
        var scenario = new Scenario(Guid.NewGuid(), "Provisional", includeActuals: true,
            includedTypeIds: new[] { c.FindVoucherTypeByName("Journal")!.Id });
        c.AddScenario(scenario);

        var period = new PeriodRange(new DateOnly(2024, 4, 1), new DateOnly(2025, 3, 31));

        Assert.False(ComparativeReport.SupportsScenarioColumns(kind));

        var ex = Assert.Throws<ArgumentException>(() => ComparativeReport.Build(c, kind, new[]
        {
            new ComparativeReport.ColumnSpec("Actual", period),
            new ComparativeReport.ColumnSpec("Provisional", period, scenario),
        }));
        Assert.Contains("scenario column", ex.Message, StringComparison.OrdinalIgnoreCase);

        // A scenario riding inside the carried ReportOptions is caught too — not just the spec's own field.
        Assert.Throws<ArgumentException>(() => ComparativeReport.Build(c, kind, new[]
        {
            new ComparativeReport.ColumnSpec(
                "Via options", period, Scenario: null,
                Options: ReportOptions.ForPeriod(period).WithScenario(scenario)),
        }));

        // Period-only columns on the same kind are fine — the refusal is narrow.
        var ok = ComparativeReport.Build(c, kind, new[]
        {
            new ComparativeReport.ColumnSpec("Apr", new PeriodRange(new DateOnly(2024, 4, 1), new DateOnly(2024, 4, 30))),
            new ComparativeReport.ColumnSpec("May", new PeriodRange(new DateOnly(2024, 5, 1), new DateOnly(2024, 5, 31))),
        });
        Assert.Equal(2, ok.Columns.Count);
    }

    [Theory]
    [InlineData(ComparativeReportKind.TrialBalance)]
    [InlineData(ComparativeReportKind.BalanceSheet)]
    [InlineData(ComparativeReportKind.ProfitAndLoss)]
    [InlineData(ComparativeReportKind.StockSummary)]
    public void The_four_original_kinds_still_admit_a_scenario_column(ComparativeReportKind kind)
        => Assert.True(ComparativeReport.SupportsScenarioColumns(kind));

    // ============================================================ periodicity axis =============

    [Fact]
    public void Every_periodicity_partitions_the_window_exactly_once_with_no_gap_and_no_overlap()
    {
        // A column axis that overlaps double-counts a flow and one that gaps loses it, and neither shows on the
        // screen as anything but a plausible figure. Assert the partition itself, for every periodicity.
        var period = new PeriodRange(new DateOnly(2024, 4, 1), new DateOnly(2025, 3, 31));

        foreach (ColumnPeriodicity p in Enum.GetValues<ColumnPeriodicity>())
        {
            var specs = ComparativeReport.PeriodicColumns(period, p);
            Assert.NotEmpty(specs);

            var windows = specs.Select(s => s.Period!.Value).ToList();
            Assert.Equal(period.From, windows[0].From);
            Assert.Equal(period.To, windows[^1].To);
            for (var i = 0; i < windows.Count; i++)
            {
                Assert.True(windows[i].IsValid, $"{p} bucket {i} is inverted.");
                if (i > 0)
                    Assert.Equal(windows[i - 1].To.AddDays(1), windows[i].From);
            }
            // The day counts sum to the window's own day count — no gap, no overlap.
            var covered = windows.Sum(w => w.To.DayNumber - w.From.DayNumber + 1);
            Assert.Equal(period.To.DayNumber - period.From.DayNumber + 1, covered);
            // An auto-column axis never carries a scenario.
            Assert.All(specs, s => Assert.Null(s.Scenario));
        }
    }

    [Fact]
    public void The_periodicity_bucket_counts_and_labels_are_the_expected_ones_for_a_financial_year()
    {
        var period = new PeriodRange(new DateOnly(2024, 4, 1), new DateOnly(2025, 3, 31)); // 365 days

        Assert.Equal(365, ComparativeReport.PeriodicColumns(period, ColumnPeriodicity.Daily).Count);
        Assert.Equal(53, ComparativeReport.PeriodicColumns(period, ColumnPeriodicity.Weekly).Count);
        Assert.Equal(27, ComparativeReport.PeriodicColumns(period, ColumnPeriodicity.Fortnightly).Count);
        Assert.Equal(12, ComparativeReport.PeriodicColumns(period, ColumnPeriodicity.Monthly).Count);
        Assert.Equal(4, ComparativeReport.PeriodicColumns(period, ColumnPeriodicity.Quarterly).Count);
        Assert.Single(ComparativeReport.PeriodicColumns(period, ColumnPeriodicity.Yearly));

        // For the standard Indian financial year the four CALENDAR quarters coincide with the four FY quarters,
        // so the calendar anchor is not visible here — that is why it was chosen.
        var quarters = ComparativeReport.PeriodicColumns(period, ColumnPeriodicity.Quarterly);
        Assert.Equal(new DateOnly(2024, 4, 1), quarters[0].Period!.Value.From);
        Assert.Equal(new DateOnly(2024, 6, 30), quarters[0].Period!.Value.To);
        Assert.Equal(new DateOnly(2025, 1, 1), quarters[3].Period!.Value.From);
        Assert.Equal(new DateOnly(2025, 3, 31), quarters[3].Period!.Value.To);
        Assert.Equal("Q2 2024", quarters[0].Label);
    }

    [Fact]
    public void MonthlyColumns_is_byte_for_byte_what_PeriodicColumns_Monthly_produces()
    {
        // MonthlyColumns is the long-shipped entry point; it now delegates, and must not have moved.
        var period = new PeriodRange(new DateOnly(2024, 4, 15), new DateOnly(2025, 2, 10)); // partial first/last
        var legacy = ComparativeReport.MonthlyColumns(period);
        var viaPeriodicity = ComparativeReport.PeriodicColumns(period, ColumnPeriodicity.Monthly);

        Assert.Equal(legacy.Count, viaPeriodicity.Count);
        for (var i = 0; i < legacy.Count; i++)
        {
            Assert.Equal(legacy[i].Label, viaPeriodicity[i].Label);
            Assert.Equal(legacy[i].Period, viaPeriodicity[i].Period);
        }
        // The partial first bucket honours the period start, not the month start.
        Assert.Equal(new DateOnly(2024, 4, 15), legacy[0].Period!.Value.From);
        Assert.Equal(new DateOnly(2025, 2, 10), legacy[^1].Period!.Value.To);
    }

    [Fact]
    public void An_inverted_window_yields_no_columns_rather_than_an_endless_axis()
    {
        var inverted = new PeriodRange(new DateOnly(2025, 3, 31), new DateOnly(2024, 4, 1));
        foreach (ColumnPeriodicity p in Enum.GetValues<ColumnPeriodicity>())
            Assert.Empty(ComparativeReport.PeriodicColumns(inverted, p));
    }

    // ---- seed ---------------------------------------------------------------------------------

    /// <summary>A minimal book with an opening cash balance, capital, a debtor and a sales ledger, so the two
    /// flow statements have something to classify on both sides.</summary>
    private static Company FlowSeed(out Domain.Ledger cash, out Domain.Ledger debtor)
    {
        var c = CompanyFactory.CreateSeeded("Flow Co", new DateOnly(2024, 4, 1));

        cash = c.FindLedgerByName("Cash")!;
        cash.OpeningBalance = Money.FromRupees(100000m);
        cash.OpeningIsDebit = true;

        var capital = new Domain.Ledger(Guid.NewGuid(), "Capital", c.FindGroupByName("Capital Account")!.Id,
            Money.FromRupees(150000m), openingIsDebit: false);
        c.AddLedger(capital);

        debtor = new Domain.Ledger(Guid.NewGuid(), "Debtor A", c.FindGroupByName("Sundry Debtors")!.Id,
            Money.FromRupees(50000m), openingIsDebit: true);
        c.AddLedger(debtor);

        return c;
    }
}
