using Apex.Ledger.Domain;
using Apex.Ledger.Reports;
using Apex.Ledger.Services;
using Xunit;
using Domain = Apex.Ledger.Domain;

namespace Apex.Ledger.Tests;

/// <summary>
/// W2-12 — the three ABSENT report-family rows of census area 11, built as real engine projections:
/// <list type="bullet">
///   <item><b>11.6</b> the five accounting registers (Sales / Purchase / Journal / Credit Note / Debit
///     Note) — <b>month-wise summary first, drilling to the voucher-wise listing of the picked month</b>.
///     That shape is the substantive finding of the wave-2 verification pass: a register is NOT a filtered
///     Day Book, so it cannot be built by adding a voucher-kind filter to <see cref="DayBook"/>.</item>
///   <item><b>11.7</b> Group Summary (the sub-groups and directly-attached ledgers of a chosen group with
///     their closing balances) and Group Vouchers (every voucher carrying at least one ledger line under
///     that group).</item>
///   <item><b>11.8</b> Statistics — the counts of vouchers entered per voucher type, and of masters
///     created per master kind.</item>
/// </list>
/// Every expected figure below is derived by hand from the fixture comments, to the paisa.
/// </summary>
public class ReportFamiliesTests
{
    private static readonly DateOnly FyStart = new(2024, 4, 1);
    private static readonly DateOnly QuarterEnd = new(2024, 6, 30);

    /// <summary>
    /// The W2-12 fixture — a small trading book whose every figure is stated here and nowhere else.
    ///
    /// <para><b>Masters</b> (on top of the documented seed of 28 groups / 2 ledgers / 24 voucher types /
    /// 1 cost category / 1 currency / 1 godown):</para>
    /// <list type="bullet">
    ///   <item>group <b>North Zone</b> under <b>Sundry Debtors</b> — 1 new group (29 in all).</item>
    ///   <item>6 new ledgers (8 in all): <b>Acme Traders</b> (Sundry Debtors, opening 0),
    ///     <b>Gamma Ltd</b> (North Zone, opening ₹5,000.00 Dr), <b>Sales A/c</b> (Sales Accounts),
    ///     <b>Purchase A/c</b> (Purchase Accounts), <b>Beta Supplies</b> (Sundry Creditors),
    ///     <b>Rent</b> (Indirect Expenses).</item>
    /// </list>
    ///
    /// <para><b>Vouchers</b> — six entered, one of them cancelled:</para>
    /// <list type="number">
    ///   <item>Sales  10-Apr-2024  Acme Dr ₹15,000.00 / Sales A/c Cr ₹15,000.00</item>
    ///   <item>Sales  25-Apr-2024  Acme Dr ₹9,999.00 / Sales A/c Cr ₹9,999.00 — <b>CANCELLED</b></item>
    ///   <item>Sales  05-May-2024  Acme Dr ₹25,500.50 / Sales A/c Cr ₹25,500.50</item>
    ///   <item>Sales  20-May-2024  Cash Dr ₹4,499.50 / Sales A/c Cr ₹4,499.50</item>
    ///   <item>Purchase 18-Apr-2024  Purchase A/c Dr ₹8,000.00 / Beta Cr ₹8,000.00</item>
    ///   <item>Journal 02-Jun-2024  Rent Dr ₹2,000.00 / Beta Cr ₹2,000.00</item>
    /// </list>
    /// </summary>
    private static Company Seed(
        out Domain.Ledger acme,
        out Domain.Ledger gamma,
        out Domain.Ledger salesLedger,
        out Group sundryDebtors,
        out Group northZone)
    {
        var c = CompanyFactory.CreateSeeded("Register Co", FyStart);

        sundryDebtors = c.FindGroupByName("Sundry Debtors")!;
        northZone = new Group(Guid.NewGuid(), "North Zone", sundryDebtors.Nature, sundryDebtors.Id);
        c.AddGroup(northZone);

        acme = new Domain.Ledger(Guid.NewGuid(), "Acme Traders", sundryDebtors.Id, Money.Zero, openingIsDebit: true);
        c.AddLedger(acme);

        gamma = new Domain.Ledger(Guid.NewGuid(), "Gamma Ltd", northZone.Id,
            Money.FromRupees(5000m), openingIsDebit: true);
        c.AddLedger(gamma);

        salesLedger = new Domain.Ledger(Guid.NewGuid(), "Sales A/c", c.FindGroupByName("Sales Accounts")!.Id,
            Money.Zero, openingIsDebit: false);
        c.AddLedger(salesLedger);

        var purchaseLedger = new Domain.Ledger(Guid.NewGuid(), "Purchase A/c",
            c.FindGroupByName("Purchase Accounts")!.Id, Money.Zero, openingIsDebit: true);
        c.AddLedger(purchaseLedger);

        var beta = new Domain.Ledger(Guid.NewGuid(), "Beta Supplies", c.FindGroupByName("Sundry Creditors")!.Id,
            Money.Zero, openingIsDebit: false);
        c.AddLedger(beta);

        var rent = new Domain.Ledger(Guid.NewGuid(), "Rent", c.FindGroupByName("Indirect Expenses")!.Id,
            Money.Zero, openingIsDebit: true);
        c.AddLedger(rent);

        var cash = c.FindLedgerByName("Cash")!;
        var sales = c.FindVoucherTypeByName("Sales")!;
        var purchase = c.FindVoucherTypeByName("Purchase")!;
        var journal = c.FindVoucherTypeByName("Journal")!;
        var svc = new LedgerService(c);

        svc.Post(new Voucher(Guid.NewGuid(), sales.Id, new DateOnly(2024, 4, 10), new[]
        {
            new EntryLine(acme.Id, Money.FromRupees(15000m), DrCr.Debit),
            new EntryLine(salesLedger.Id, Money.FromRupees(15000m), DrCr.Credit),
        }));

        var cancelled = svc.Post(new Voucher(Guid.NewGuid(), sales.Id, new DateOnly(2024, 4, 25), new[]
        {
            new EntryLine(acme.Id, Money.FromRupees(9999m), DrCr.Debit),
            new EntryLine(salesLedger.Id, Money.FromRupees(9999m), DrCr.Credit),
        }));
        svc.Cancel(cancelled.Id);

        svc.Post(new Voucher(Guid.NewGuid(), sales.Id, new DateOnly(2024, 5, 5), new[]
        {
            new EntryLine(acme.Id, Money.FromRupees(25500.50m), DrCr.Debit),
            new EntryLine(salesLedger.Id, Money.FromRupees(25500.50m), DrCr.Credit),
        }));

        svc.Post(new Voucher(Guid.NewGuid(), sales.Id, new DateOnly(2024, 5, 20), new[]
        {
            new EntryLine(cash.Id, Money.FromRupees(4499.50m), DrCr.Debit),
            new EntryLine(salesLedger.Id, Money.FromRupees(4499.50m), DrCr.Credit),
        }));

        svc.Post(new Voucher(Guid.NewGuid(), purchase.Id, new DateOnly(2024, 4, 18), new[]
        {
            new EntryLine(purchaseLedger.Id, Money.FromRupees(8000m), DrCr.Debit),
            new EntryLine(beta.Id, Money.FromRupees(8000m), DrCr.Credit),
        }));

        svc.Post(new Voucher(Guid.NewGuid(), journal.Id, new DateOnly(2024, 6, 2), new[]
        {
            new EntryLine(rent.Id, Money.FromRupees(2000m), DrCr.Debit),
            new EntryLine(beta.Id, Money.FromRupees(2000m), DrCr.Credit),
        }));

        return c;
    }

    // ================================================================= the month axis (T1-32 primitive)

    [Fact]
    public void Month_axis_spans_every_calendar_month_touched_and_clips_the_end_buckets()
    {
        // 15-Apr-2024 → 02-Jun-2024 touches three calendar months. The first bucket starts on the
        // window start (not 1-Apr) and the last ends on the window end (not 30-Jun).
        var months = MonthAxis.Months(new DateOnly(2024, 4, 15), new DateOnly(2024, 6, 2));

        Assert.Equal(3, months.Count);
        Assert.Equal(new DateOnly(2024, 4, 15), months[0].From);
        Assert.Equal(new DateOnly(2024, 4, 30), months[0].To);
        Assert.Equal(new DateOnly(2024, 5, 1), months[1].From);
        Assert.Equal(new DateOnly(2024, 5, 31), months[1].To);
        Assert.Equal(new DateOnly(2024, 6, 1), months[2].From);
        Assert.Equal(new DateOnly(2024, 6, 2), months[2].To);
        Assert.Equal("Apr-2024", months[0].Label);
        Assert.Equal("Jun-2024", months[2].Label);
    }

    [Fact]
    public void Month_axis_of_an_inverted_window_is_empty()
    {
        Assert.Empty(MonthAxis.Months(new DateOnly(2024, 6, 1), new DateOnly(2024, 4, 1)));
    }

    // ================================================================= 11.6 — the accounting registers

    [Fact]
    public void Sales_register_is_month_wise_and_excludes_a_cancelled_voucher()
    {
        var c = Seed(out _, out _, out _, out _, out _);

        var reg = VoucherRegister.Build(c, VoucherRegisterKind.Sales, FyStart, QuarterEnd);

        // Three month buckets, one per calendar month in 01-Apr → 30-Jun, INCLUDING the empty June.
        Assert.Equal(3, reg.Months.Count);

        // Apr-2024: voucher 1 only — voucher 2 is cancelled and contributes nothing. ₹15,000.00.
        Assert.Equal("Apr-2024", reg.Months[0].Month.Label);
        Assert.Equal(1, reg.Months[0].VoucherCount);
        Assert.Equal(15000.00m, reg.Months[0].Value.Amount);

        // May-2024: vouchers 3 + 4 = 25,500.50 + 4,499.50 = ₹30,000.00.
        Assert.Equal("May-2024", reg.Months[1].Month.Label);
        Assert.Equal(2, reg.Months[1].VoucherCount);
        Assert.Equal(30000.00m, reg.Months[1].Value.Amount);

        // Jun-2024: no sales at all — the month still appears with a zero.
        Assert.Equal("Jun-2024", reg.Months[2].Month.Label);
        Assert.Equal(0, reg.Months[2].VoucherCount);
        Assert.Equal(0.00m, reg.Months[2].Value.Amount);

        // 15,000.00 + 30,000.00 = ₹45,000.00 over 3 vouchers.
        Assert.Equal(3, reg.TotalCount);
        Assert.Equal(45000.00m, reg.Total.Amount);
    }

    [Fact]
    public void Sales_register_month_drills_to_that_months_voucher_wise_listing()
    {
        var c = Seed(out _, out _, out _, out _, out _);

        var may = VoucherRegister.Vouchers(c, VoucherRegisterKind.Sales,
            new DateOnly(2024, 5, 1), new DateOnly(2024, 5, 31));

        Assert.Equal(2, may.Count);
        Assert.Equal(new DateOnly(2024, 5, 5), may[0].Date);
        Assert.Equal(25500.50m, may[0].Value.Amount);
        Assert.Equal("Acme Traders", may[0].Particulars);
        Assert.Equal(new DateOnly(2024, 5, 20), may[1].Date);
        Assert.Equal(4499.50m, may[1].Value.Amount);
        Assert.Equal("Cash", may[1].Particulars);

        // RQ-7: every register row drills to its voucher.
        Assert.All(may, r => Assert.NotEqual(Guid.Empty, r.VoucherId));

        // The drilled month's rows foot back to the month row of the summary above them.
        Assert.Equal(30000.00m, may.Sum(r => r.Value.Amount));
    }

    [Fact]
    public void Purchase_journal_credit_and_debit_note_registers_each_select_their_own_base_type()
    {
        var c = Seed(out _, out _, out _, out _, out _);

        var purchase = VoucherRegister.Build(c, VoucherRegisterKind.Purchase, FyStart, QuarterEnd);
        Assert.Equal(1, purchase.TotalCount);
        Assert.Equal(8000.00m, purchase.Total.Amount);
        Assert.Equal(8000.00m, purchase.Months[0].Value.Amount);   // Apr-2024
        Assert.Equal(0.00m, purchase.Months[1].Value.Amount);      // May-2024

        var journal = VoucherRegister.Build(c, VoucherRegisterKind.Journal, FyStart, QuarterEnd);
        Assert.Equal(1, journal.TotalCount);
        Assert.Equal(2000.00m, journal.Total.Amount);
        Assert.Equal(2000.00m, journal.Months[2].Value.Amount);    // Jun-2024

        // The Journal Register is Journal ONLY — the notes are their own registers and are empty here.
        Assert.Equal(0, VoucherRegister.Build(c, VoucherRegisterKind.CreditNote, FyStart, QuarterEnd).TotalCount);
        Assert.Equal(0, VoucherRegister.Build(c, VoucherRegisterKind.DebitNote, FyStart, QuarterEnd).TotalCount);

        Assert.Equal(VoucherBaseType.Sales, VoucherRegister.BaseTypeOf(VoucherRegisterKind.Sales));
        Assert.Equal(VoucherBaseType.Purchase, VoucherRegister.BaseTypeOf(VoucherRegisterKind.Purchase));
        Assert.Equal(VoucherBaseType.Journal, VoucherRegister.BaseTypeOf(VoucherRegisterKind.Journal));
        Assert.Equal(VoucherBaseType.CreditNote, VoucherRegister.BaseTypeOf(VoucherRegisterKind.CreditNote));
        Assert.Equal(VoucherBaseType.DebitNote, VoucherRegister.BaseTypeOf(VoucherRegisterKind.DebitNote));
    }

    [Fact]
    public void A_register_is_not_a_filtered_day_book()
    {
        // The shape guard for the finding this slice exists to honour. The Day Book over the same window
        // is a FLAT chronological list of all six vouchers minus none (cancelled rows list but are
        // flagged); the Sales Register's top level is THREE month rows.
        var c = Seed(out _, out _, out _, out _, out _);

        var dayBook = DayBook.Build(c, FyStart, QuarterEnd);
        var reg = VoucherRegister.Build(c, VoucherRegisterKind.Sales, FyStart, QuarterEnd);

        Assert.Equal(6, dayBook.Count);        // all six entered vouchers, cancelled included and flagged
        Assert.Equal(3, reg.Months.Count);     // three months, not four sales rows
    }

    // ================================================================= the Ledger Monthly Summary (T1-32)

    [Fact]
    public void Ledger_monthly_summary_carries_opening_month_movement_and_running_closing()
    {
        var c = Seed(out var acme, out _, out _, out _, out _);

        var summary = LedgerMonthlySummary.Build(c, acme.Id, FyStart, QuarterEnd);

        Assert.Equal("Acme Traders", summary.LedgerName);
        Assert.Equal(0.00m, summary.OpeningAmount.Amount);
        Assert.Equal(3, summary.Rows.Count);

        // Apr: one live posting of ₹15,000.00 Dr (the cancelled ₹9,999.00 never counts). Closing 15,000.00 Dr.
        Assert.Equal(15000.00m, summary.Rows[0].Debit.Amount);
        Assert.Equal(0.00m, summary.Rows[0].Credit.Amount);
        Assert.Equal(DrCr.Debit, summary.Rows[0].ClosingSide);
        Assert.Equal(15000.00m, summary.Rows[0].ClosingAmount.Amount);

        // May: ₹25,500.50 Dr. Closing 15,000.00 + 25,500.50 = ₹40,500.50 Dr.
        Assert.Equal(25500.50m, summary.Rows[1].Debit.Amount);
        Assert.Equal(40500.50m, summary.Rows[1].ClosingAmount.Amount);

        // Jun: nothing. Closing unchanged at ₹40,500.50 Dr.
        Assert.Equal(0.00m, summary.Rows[2].Debit.Amount);
        Assert.Equal(40500.50m, summary.Rows[2].ClosingAmount.Amount);

        Assert.Equal(DrCr.Debit, summary.ClosingSide);
        Assert.Equal(40500.50m, summary.ClosingAmount.Amount);
    }

    // ================================================================= 11.7 — Group Summary

    [Fact]
    public void Group_summary_lists_sub_groups_and_directly_attached_ledgers_with_closing_balances()
    {
        var c = Seed(out _, out _, out _, out var sundryDebtors, out _);

        var gs = GroupSummary.Build(c, sundryDebtors.Id, FyStart, QuarterEnd);

        Assert.Equal("Sundry Debtors", gs.GroupName);
        Assert.Equal(2, gs.Rows.Count);

        // Sub-groups first, then the directly-attached ledgers; each block name-sorted.
        var north = gs.Rows[0];
        Assert.True(north.IsGroup);
        Assert.Equal("North Zone", north.Name);
        // Gamma's ₹5,000.00 Dr opening, no postings → closing ₹5,000.00 Dr, zero movement.
        Assert.Equal(5000.00m, north.OpeningAmount.Amount);
        Assert.Equal(0.00m, north.Debit.Amount);
        Assert.Equal(0.00m, north.Credit.Amount);
        Assert.Equal(5000.00m, north.ClosingAmount.Amount);
        Assert.Equal(DrCr.Debit, north.ClosingSide);

        var acmeRow = gs.Rows[1];
        Assert.False(acmeRow.IsGroup);
        Assert.Equal("Acme Traders", acmeRow.Name);
        // 15,000.00 + 25,500.50 = ₹40,500.50 Dr movement over a nil opening.
        Assert.Equal(0.00m, acmeRow.OpeningAmount.Amount);
        Assert.Equal(40500.50m, acmeRow.Debit.Amount);
        Assert.Equal(0.00m, acmeRow.Credit.Amount);
        Assert.Equal(40500.50m, acmeRow.ClosingAmount.Amount);

        // Group total: 5,000.00 + 40,500.50 = ₹45,500.50 Dr.
        Assert.Equal(DrCr.Debit, gs.ClosingSide);
        Assert.Equal(45500.50m, gs.ClosingAmount.Amount);
    }

    [Fact]
    public void Group_summary_of_a_leaf_group_lists_its_own_ledgers()
    {
        var c = Seed(out _, out var gamma, out _, out _, out var northZone);

        var gs = GroupSummary.Build(c, northZone.Id, FyStart, QuarterEnd);

        var row = Assert.Single(gs.Rows);
        Assert.False(row.IsGroup);
        Assert.Equal("Gamma Ltd", row.Name);
        Assert.Equal(gamma.Id, row.LedgerId);
        Assert.Equal(5000.00m, row.ClosingAmount.Amount);
    }

    // ================================================================= 11.7 — Group Vouchers

    [Fact]
    public void Group_vouchers_lists_every_voucher_touching_a_ledger_under_the_group()
    {
        var c = Seed(out _, out _, out _, out var sundryDebtors, out _);

        var gv = GroupVouchers.Build(c, sundryDebtors.Id, FyStart, QuarterEnd);

        // Vouchers 1 and 3 touch Acme. Voucher 4 is Cash/Sales — no debtor line. Voucher 2 is cancelled.
        Assert.Equal(2, gv.Rows.Count);
        Assert.Equal(new DateOnly(2024, 4, 10), gv.Rows[0].Date);
        Assert.Equal("Sales", gv.Rows[0].VoucherTypeName);
        Assert.Equal(15000.00m, gv.Rows[0].Debit.Amount);
        Assert.Equal(0.00m, gv.Rows[0].Credit.Amount);
        Assert.Equal(new DateOnly(2024, 5, 5), gv.Rows[1].Date);
        Assert.Equal(25500.50m, gv.Rows[1].Debit.Amount);

        // Only the GROUP's own lines are footed, not the whole voucher: 15,000.00 + 25,500.50.
        Assert.Equal(40500.50m, gv.TotalDebit.Amount);
        Assert.Equal(0.00m, gv.TotalCredit.Amount);

        // RQ-7: each row drills to its voucher.
        Assert.All(gv.Rows, r => Assert.NotEqual(Guid.Empty, r.VoucherId));
    }

    [Fact]
    public void Group_vouchers_counts_a_ledger_nested_below_the_group()
    {
        var c = Seed(out _, out var gamma, out var salesLedger, out var sundryDebtors, out _);
        var svc = new LedgerService(c);
        var sales = c.FindVoucherTypeByName("Sales")!;

        // A sale to Gamma, which hangs off North Zone — a CHILD of Sundry Debtors.
        svc.Post(new Voucher(Guid.NewGuid(), sales.Id, new DateOnly(2024, 6, 10), new[]
        {
            new EntryLine(gamma.Id, Money.FromRupees(1234.56m), DrCr.Debit),
            new EntryLine(salesLedger.Id, Money.FromRupees(1234.56m), DrCr.Credit),
        }));

        var gv = GroupVouchers.Build(c, sundryDebtors.Id, FyStart, QuarterEnd);

        Assert.Equal(3, gv.Rows.Count);
        Assert.Equal(new DateOnly(2024, 6, 10), gv.Rows[2].Date);
        Assert.Equal(1234.56m, gv.Rows[2].Debit.Amount);
        // 15,000.00 + 25,500.50 + 1,234.56 = ₹41,735.06.
        Assert.Equal(41735.06m, gv.TotalDebit.Amount);
    }

    // ================================================================= 11.8 — Statistics

    [Fact]
    public void Statistics_counts_vouchers_entered_per_type_including_the_cancelled_one()
    {
        var c = Seed(out _, out _, out _, out _, out _);

        var stats = Statistics.Build(c, FyStart, QuarterEnd);

        // Every one of the 23 seeded voucher types appears, entries or not.
        // 23, not 24: SeedVoucherTypes' own Count guard is 23 and its header explains why — the dead
        // "Attendance" row was deliberately removed (decision D24 option B). CompanyFactory's class doc
        // comment still says "24 voucher types" and is STALE; this test derived 24 from it and went red,
        // which is how the stale comment was found.
        Assert.Equal(23, stats.VoucherTypes.Count);

        var sales = stats.VoucherTypes.Single(r => r.Name == "Sales");
        Assert.Equal(4, sales.Count);            // 3 live + 1 cancelled — all four were ENTERED
        Assert.Equal(1, sales.CancelledCount);

        Assert.Equal(1, stats.VoucherTypes.Single(r => r.Name == "Purchase").Count);
        Assert.Equal(1, stats.VoucherTypes.Single(r => r.Name == "Journal").Count);
        Assert.Equal(0, stats.VoucherTypes.Single(r => r.Name == "Contra").Count);

        // 4 + 1 + 1 = 6 vouchers entered in the window.
        Assert.Equal(6, stats.TotalVouchers);
    }

    [Fact]
    public void Statistics_counts_the_masters_created()
    {
        var c = Seed(out _, out _, out _, out _, out _);

        var stats = Statistics.Build(c, FyStart, QuarterEnd);

        // Seed contract: 28 groups + our "North Zone" = 29. 2 seeded ledgers + our 6 = 8.
        // Voucher types: SeedVoucherTypes.Count == 23 (see the note in the sibling test).
        Assert.Equal(29, stats.Masters.Single(m => m.Name == "Groups").Count);
        Assert.Equal(8, stats.Masters.Single(m => m.Name == "Ledgers").Count);
        Assert.Equal(23, stats.Masters.Single(m => m.Name == "Voucher Types").Count);
        Assert.Equal(1, stats.Masters.Single(m => m.Name == "Cost Categories").Count);
        Assert.Equal(0, stats.Masters.Single(m => m.Name == "Cost Centres").Count);
        Assert.Equal(1, stats.Masters.Single(m => m.Name == "Currencies").Count);
        Assert.Equal(1, stats.Masters.Single(m => m.Name == "Godowns").Count);
        Assert.Equal(0, stats.Masters.Single(m => m.Name == "Stock Items").Count);
    }

    [Fact]
    public void Statistics_only_counts_vouchers_inside_the_window()
    {
        var c = Seed(out _, out _, out _, out _, out _);

        // April alone: voucher 1 (live) + voucher 2 (cancelled) + voucher 5 (purchase) = 3.
        var april = Statistics.Build(c, FyStart, new DateOnly(2024, 4, 30));

        Assert.Equal(3, april.TotalVouchers);
        Assert.Equal(2, april.VoucherTypes.Single(r => r.Name == "Sales").Count);
        Assert.Equal(1, april.VoucherTypes.Single(r => r.Name == "Sales").CancelledCount);
        Assert.Equal(1, april.VoucherTypes.Single(r => r.Name == "Purchase").Count);
        Assert.Equal(0, april.VoucherTypes.Single(r => r.Name == "Journal").Count);
    }
}
