using System;
using System.Collections.Generic;
using System.Linq;
using Apex.Ledger.Domain;
using Apex.Ledger.Reports;
using Apex.Ledger.Services;
using Xunit;

namespace Apex.Ledger.Tests;

/// <summary>
/// <b>Census row 16.5 — Split Company Data: the engine gate.</b>
///
/// <para>🔴 <b>Why the central test here is a RECONCILIATION and not a set of expected figures.</b> A split
/// rewrites an entire book. A test that asserted "the from-book's Cash is ₹12,345" would pass on a split that
/// carried Cash correctly and silently dropped a debtor — the shape of defect this project has already shipped
/// green three times. So the assertions are <b>identities against the unsplit book</b>, ledger by ledger, over
/// both deterministic fixtures:</para>
/// <list type="number">
/// <item><b>The before-book is the original, truncated.</b> Every ledger's closing balance at the day before
///   the split date is identical to the original's, because only later vouchers were dropped.</item>
/// <item><b>The from-book's opening balances ARE the before-book's closing balances.</b> Measured directly, on
///   every balance-sheet ledger.</item>
/// <item><b>The from-book reports what the unsplit book reports.</b> Every balance-sheet ledger's closing at
///   the last entry date is identical to the original's — which is only true if every opening was carried.
///   Drop one and this fails on that ledger's row.</item>
/// <item><b>Income and expense are partitioned, not duplicated.</b> before-closing + from-closing == the
///   original's closing, for every P&amp;L ledger.</item>
/// <item><b>Both books balance</b> (Σ Dr == Σ Cr on their own trial balances).</item>
/// </list>
///
/// <para><b>Independent copies, by construction.</b> Every case loads the fixture <b>afresh</b> for the source
/// and once more for each book it shapes. <see cref="FixtureLoader.Load"/> builds a new aggregate from JSON
/// every call, so no two of them share an object — the same guarantee the shell gets by re-loading the
/// <c>.db</c>. <c>Split_leaves_the_source_book_untouched</c> is the standing proof that the guarantee holds.</para>
/// </summary>
public class CompanySplitTests
{
    private const string Robert = "robert.json";
    private const string Bright = "bright.json";

    /// <summary>
    /// Robert's book runs 02-Apr-2020 → 30-Apr-2020; this cuts it mid-month. The date is chosen because Robert
    /// <b>has an entry dated exactly on it</b>, so the boundary rule ("a voucher on the split date belongs to
    /// the from-book") is exercised by the reconciliation gate itself, not only by the boundary test.
    /// </summary>
    private static readonly DateOnly RobertSplit = new(2020, 4, 18);

    /// <summary>Bright's book runs 03-Apr-2021 → 31-Mar-2022; this cuts it at the half-year.</summary>
    private static readonly DateOnly BrightSplit = new(2021, 10, 1);

    private static Company Load(string fixture) => FixtureLoader.Load(fixture).Company;

    private static decimal Closing(Company c, string ledgerName, DateOnly asOf)
    {
        var ledger = c.FindLedgerByName(ledgerName)
            ?? throw new InvalidOperationException($"'{ledgerName}' is not in '{c.Name}'.");
        return LedgerBalances.SignedClosing(c, ledger, asOf);
    }

    private static IEnumerable<string> LedgerNames(Company c) => c.Ledgers.Select(l => l.Name);

    // ================================================================= the reconciliation gate

    [Theory]
    [InlineData(Robert)]
    [InlineData(Bright)]
    [Trait("Category", "Fixture")]
    public void The_two_halves_reconcile_to_the_unsplit_book(string fixture)
    {
        var source = Load(fixture);
        var splitDate = fixture == Robert ? RobertSplit : BrightSplit;
        var cutoff = splitDate.AddDays(-1);
        var last = CompanySplit.LastEntryDate(source)!.Value;

        Assert.Empty(CompanySplit.Check(source, splitDate, CompanySplitMode.IntoTwoCompanies));

        var before = Load(fixture);
        CompanySplit.ShapeAsBeforeBook(before, splitDate, "Split — before");
        var from = Load(fixture);
        CompanySplit.ShapeAsFromBook(from, splitDate, "Split — from");

        // (1) The before-book is the original, truncated: same ledgers, same closings at the cutoff.
        Assert.Equal(LedgerNames(source).OrderBy(n => n), LedgerNames(before).OrderBy(n => n));
        foreach (var name in LedgerNames(source))
            Assert.Equal(Closing(source, name, cutoff), Closing(before, name, cutoff));

        foreach (var name in LedgerNames(source))
        {
            var sourceLedger = source.FindLedgerByName(name)!;
            var fromLedger = from.FindLedgerByName(name)!;
            var isPl = ClassificationRules.IsProfitAndLossLedger(sourceLedger, source);
            var isPlAccount = string.Equals(name, Seed.SeedLedgers.ProfitAndLossName, StringComparison.OrdinalIgnoreCase);

            if (isPl)
            {
                // (2b) A new book does not open with last period's revenue.
                Assert.Equal(0m, fromLedger.SignedOpening);
                // (4) Income and expense are PARTITIONED across the two books, never duplicated.
                Assert.Equal(
                    Closing(source, name, last),
                    Closing(before, name, cutoff) + Closing(from, name, last));
            }
            else if (!isPlAccount)
            {
                // (2a) The from-book's opening IS the before-book's closing.
                Assert.Equal(Closing(before, name, cutoff), fromLedger.SignedOpening);
                // (3) …so the from-book reports what the unsplit book reports.
                Assert.Equal(Closing(source, name, last), Closing(from, name, last));
            }
        }

        // (5) Both books balance on their own.
        foreach (var (book, asOf) in new[] { (before, cutoff), (from, last) })
        {
            var tb = TrialBalance.Build(book, asOf);
            Assert.Equal(tb.TotalDebit, tb.TotalCredit);
        }
    }

    /// <summary>
    /// The accumulated profit of the period before the split is carried onto the <b>Profit &amp; Loss A/c</b>
    /// ledger — the one algebraic move that keeps the from-book's opening trial balance balanced once the
    /// income and expense ledgers are zeroed. Asserted as an identity against the P&amp;L ledgers' own
    /// balances, so it holds under every closing-stock basis.
    /// </summary>
    [Theory]
    [InlineData(Robert)]
    [InlineData(Bright)]
    [Trait("Category", "Fixture")]
    public void Accumulated_profit_before_the_split_is_carried_onto_the_Profit_and_Loss_account(string fixture)
    {
        var source = Load(fixture);
        var splitDate = fixture == Robert ? RobertSplit : BrightSplit;
        var cutoff = splitDate.AddDays(-1);

        var from = Load(fixture);
        CompanySplit.ShapeAsFromBook(from, splitDate, "Split — from");

        var transfer = source.Ledgers
            .Where(l => ClassificationRules.IsProfitAndLossLedger(l, source))
            .Sum(l => LedgerBalances.SignedClosing(source, l, cutoff));

        // A profitable period leaves the P&L ledgers net CREDIT, i.e. a negative signed sum.
        Assert.NotEqual(0m, transfer);

        var expected = Closing(source, Seed.SeedLedgers.ProfitAndLossName, cutoff) + transfer;
        Assert.Equal(expected, from.FindLedgerByName(Seed.SeedLedgers.ProfitAndLossName)!.SignedOpening);

        // And the whole opening set balances: Σ signed opening over every ledger is exactly zero.
        Assert.Equal(0m, from.Ledgers.Sum(l => l.SignedOpening));
    }

    /// <summary>
    /// 🔴 The vendor's two ranges — <i>"till one day before the split date"</i> and <i>"from the split
    /// date"</i> — partition the book with no overlap and no gap, so a voucher dated <b>exactly on</b> the
    /// split date belongs to the FROM book. Robert has an entry dated 16-Apr-2020, which is the split date
    /// used here, so this case is not hypothetical.
    /// </summary>
    [Fact]
    [Trait("Category", "Fixture")]
    public void A_voucher_dated_on_the_split_date_lands_in_the_from_book()
    {
        // Identities are per-load, so the books are compared by DATE — the only thing the split keys on.
        var source = Load(Robert);
        var onTheDate = source.Vouchers.Count(v => v.Date == RobertSplit);
        Assert.True(onTheDate > 0, "the fixture must carry an entry dated exactly on the split date");

        var before = Load(Robert);
        CompanySplit.ShapeAsBeforeBook(before, RobertSplit, "before");
        var from = Load(Robert);
        CompanySplit.ShapeAsFromBook(from, RobertSplit, "from");

        Assert.DoesNotContain(before.Vouchers, v => v.Date >= RobertSplit);
        Assert.Equal(onTheDate, from.Vouchers.Count(v => v.Date == RobertSplit));

        // And every voucher lands in exactly one of the two books.
        Assert.Equal(source.Vouchers.Count, before.Vouchers.Count + from.Vouchers.Count);
    }

    /// <summary>
    /// <i>"After splitting your company, the original company will remain as it is."</i> The source aggregate
    /// this test hands the engine is never passed to a shaping method, and is re-measured afterwards against a
    /// freshly loaded twin — voucher count, every ledger opening, and every closing balance.
    /// </summary>
    [Theory]
    [InlineData(Robert)]
    [InlineData(Bright)]
    [Trait("Category", "Fixture")]
    public void Split_leaves_the_source_book_untouched(string fixture)
    {
        var source = Load(fixture);
        var splitDate = fixture == Robert ? RobertSplit : BrightSplit;
        var last = CompanySplit.LastEntryDate(source)!.Value;

        var beforeNames = LedgerNames(source).ToList();
        var beforeVoucherCount = source.Vouchers.Count;
        var beforeInventoryCount = source.InventoryVouchers.Count;
        var beforeOpenings = source.Ledgers.ToDictionary(l => l.Name, l => l.SignedOpening);
        var beforeClosings = beforeNames.ToDictionary(n => n, n => Closing(source, n, last));
        var beforeStock = source.StockOpeningBalances.Count;
        var beforeName = source.Name;
        var beforeBooks = source.BooksBeginFrom;

        // Shape TWO independent copies, exactly as the shell does.
        var a = Load(fixture);
        CompanySplit.ShapeAsBeforeBook(a, splitDate, "copy A");
        var b = Load(fixture);
        CompanySplit.ShapeAsFromBook(b, splitDate, "copy B");

        Assert.Equal(beforeName, source.Name);
        Assert.Equal(beforeBooks, source.BooksBeginFrom);
        Assert.Equal(beforeVoucherCount, source.Vouchers.Count);
        Assert.Equal(beforeInventoryCount, source.InventoryVouchers.Count);
        Assert.Equal(beforeStock, source.StockOpeningBalances.Count);
        foreach (var name in beforeNames)
        {
            Assert.Equal(beforeOpenings[name], source.FindLedgerByName(name)!.SignedOpening);
            Assert.Equal(beforeClosings[name], Closing(source, name, last));
        }
    }

    // ================================================================= inventory

    /// <summary>
    /// Bright's stock is carried godown-by-godown: the from-book <b>opens</b> with the quantity and value the
    /// before-book <b>closes</b> with, so the derived Stock-in-Hand at the end of the year is the same figure
    /// in the from-book as in the unsplit book (₹15,000 — the Phase-3 gate's own number).
    /// </summary>
    [Fact]
    [Trait("Category", "Fixture")]
    public void Closing_stock_before_the_split_becomes_the_new_books_opening_stock()
    {
        var source = FixtureLoader.Load(Bright, skipManualClosingStock: true).Company;
        var cutoff = BrightSplit.AddDays(-1);
        var last = CompanySplit.LastEntryDate(source)!.Value;

        var closingBefore = GodownSummary.Build(source, cutoff);
        Assert.NotEmpty(closingBefore.Rows);

        var from = FixtureLoader.Load(Bright, skipManualClosingStock: true).Company;
        CompanySplit.ShapeAsFromBook(from, BrightSplit, "Bright — H2");

        // The carried opening rows reproduce the before-period's closing quantity and value exactly. Matched by
        // NAME, because each fixture load mints its own ids.
        Assert.Equal(closingBefore.Rows.Count, from.StockOpeningBalances.Count);
        foreach (var row in closingBefore.Rows)
        {
            var carried = from.StockOpeningBalances.Single(s =>
                from.FindStockItem(s.StockItemId)!.Name == row.ItemName
                && from.FindGodown(s.GodownId)!.Name == row.GodownName);
            Assert.Equal(row.ClosingQuantity, carried.Quantity);
            Assert.Equal(row.ClosingValue, carried.Value);
        }

        // …and the year-end derived stock is unchanged by the split.
        Assert.Equal(
            new StockValuationService(source).TotalClosingStockValue(last),
            new StockValuationService(from).TotalClosingStockValue(last));
    }

    // ================================================================= refusals

    /// <summary>
    /// 🔴 A book whose party ledgers still carry open bill-wise references is <b>refused</b>, not split: a
    /// carried opening balance is one figure per ledger with nowhere to record which bills make it up, and a
    /// book whose party balance and Outstandings disagree is worse than no split at all. The refusal names the
    /// count and the references.
    /// </summary>
    [Fact]
    public void A_book_with_open_bill_wise_references_is_refused()
    {
        var company = BillWiseCompany(out var splitDate);

        var reasons = CompanySplit.Check(company, splitDate, CompanySplitMode.IntoTwoCompanies);
        var reason = Assert.Single(reasons); // the open bill is the ONLY thing wrong with this book
        Assert.Contains("bill-wise reference", reason, StringComparison.Ordinal);
        Assert.Contains("Ace Traders / INV-1", reason, StringComparison.Ordinal);

        // And the engine refuses to shape rather than trusting the caller to have checked.
        var thrown = Assert.Throws<InvalidOperationException>(
            () => CompanySplit.ShapeAsFromBook(company, splitDate, "new"));
        Assert.Contains("bill-wise reference", thrown.Message, StringComparison.Ordinal);

        // Nothing was written: the book still holds every voucher it started with.
        Assert.Equal(3, company.Vouchers.Count);
    }

    /// <summary>A settled bill is not an open one — the same book splits once the invoice is paid.</summary>
    [Fact]
    public void A_book_whose_bills_are_settled_before_the_split_date_is_not_refused()
    {
        var company = BillWiseCompany(out var splitDate, settle: true);
        Assert.Empty(CompanySplit.Check(company, splitDate, CompanySplitMode.IntoTwoCompanies));
    }

    [Fact]
    [Trait("Category", "Fixture")]
    public void A_split_date_outside_the_book_is_refused_for_the_half_that_would_be_empty()
    {
        var source = Load(Robert);

        var tooEarly = CompanySplit.Check(source, new DateOnly(2020, 4, 1), CompanySplitMode.IntoTwoCompanies);
        Assert.NotEmpty(tooEarly);

        var afterTheEnd = CompanySplit.Check(source, new DateOnly(2020, 6, 1), CompanySplitMode.FromSplitDate);
        Assert.Contains(afterTheEnd, r => r.Contains("no entries on or after", StringComparison.Ordinal));

        // The other half of the same date is fine — the before-book is not empty.
        Assert.Empty(CompanySplit.Check(source, new DateOnly(2020, 6, 1), CompanySplitMode.BeforeSplitDate));
    }

    [Fact]
    [Trait("Category", "Fixture")]
    public void A_book_holding_voucher_linked_statutory_records_is_refused()
    {
        var source = Load(Robert);
        source.AddGstChallan(new GstChallan(
            Guid.NewGuid(), "12345678901234", cin: null, brn: null,
            depositDate: new DateOnly(2020, 4, 10), majorHead: GstTaxHead.Integrated,
            minorHead: GstMinorHead.Tax, amount: Money.FromRupees(100m),
            voucherId: source.Vouchers[0].Id));

        var reasons = CompanySplit.Check(source, RobertSplit, CompanySplitMode.IntoTwoCompanies);
        Assert.Contains(reasons, r => r.Contains("GST challans", StringComparison.Ordinal));
    }

    /// <summary>
    /// 🔴 <b>The bill-wise half of the reconciliation.</b> On a book the split ACCEPTS — every bill opened
    /// before the split date settled before it — the two halves' Outstandings reconcile to the unsplit book's:
    /// the before-book shows what the original showed at the cutoff (nothing), and the from-book shows exactly
    /// the bills the original still has open at the end. A split that dropped a post-split bill, or that
    /// resurrected the settled one, fails here.
    /// </summary>
    [Fact]
    public void Bill_wise_outstandings_reconcile_across_the_split()
    {
        var end = new DateOnly(2024, 5, 31);
        var source = BillWiseCompany(out var splitDate, settle: true, openBillAfterSplit: true);
        Assert.Empty(CompanySplit.Check(source, splitDate, CompanySplitMode.IntoTwoCompanies));

        var originalAtCutoff = Outstandings.Build(source, splitDate.AddDays(-1));
        var originalAtEnd = Outstandings.Build(source, end);
        Assert.Empty(originalAtCutoff.Receivables);                       // INV-1 was settled on 06-Apr
        var stillOpen = Assert.Single(originalAtEnd.Receivables);
        Assert.Equal("INV-2", stillOpen.Reference);

        var before = BillWiseCompany(out _, settle: true, openBillAfterSplit: true);
        CompanySplit.ShapeAsBeforeBook(before, splitDate, "bills — before");
        var from = BillWiseCompany(out _, settle: true, openBillAfterSplit: true);
        CompanySplit.ShapeAsFromBook(from, splitDate, "bills — from");

        // The before-book carries the cutoff picture exactly: no open bill, and the settled one is not revived.
        Assert.Empty(Outstandings.Build(before, splitDate.AddDays(-1)).Receivables);
        Assert.Empty(Outstandings.Build(before, splitDate.AddDays(-1)).Payables);

        // The from-book carries the end picture exactly — same reference, same pending, same original.
        var carried = Assert.Single(Outstandings.Build(from, end).Receivables);
        Assert.Equal(stillOpen.Reference, carried.Reference);
        Assert.Equal(stillOpen.Pending, carried.Pending);
        Assert.Equal(stillOpen.Original, carried.Original);
        Assert.Equal(stillOpen.DueDate, carried.DueDate);

        // …and the party ledger's own balance in the from-book agrees with the bills it shows, which is the
        // contradiction the bill-wise refusal exists to prevent.
        var party = from.FindLedgerByName("Ace Traders")!;
        Assert.Equal(carried.Pending.Amount, LedgerBalances.SignedClosing(from, party, end));
    }

    /// <summary>
    /// A book with no entries at all cannot be split, and the message says so rather than producing two empty
    /// companies.
    /// </summary>
    [Fact]
    public void An_empty_book_is_refused()
    {
        var company = CompanyFactory.CreateSeeded("Empty", new DateOnly(2024, 4, 1), new DateOnly(2024, 4, 1));
        var reasons = CompanySplit.Check(company, new DateOnly(2024, 10, 1), CompanySplitMode.IntoTwoCompanies);
        Assert.Contains(reasons, r => r.Contains("has no entries", StringComparison.Ordinal));
    }

    /// <summary>Dates in a refusal are rendered invariantly — the message must read the same on the ubuntu,
    /// macos and windows legs of the gate.</summary>
    [Fact]
    [Trait("Category", "Fixture")]
    public void Refusal_messages_are_culture_invariant()
    {
        var source = Load(Robert);
        var reasons = CompanySplit.Check(source, new DateOnly(2020, 6, 1), CompanySplitMode.FromSplitDate);
        Assert.Contains(reasons, r => r.Contains("01-Jun-2020", StringComparison.Ordinal));
    }

    // ----------------------------------------------------------------- helpers

    /// <summary>
    /// A minimal two-voucher book with a bill-by-bill debtor: a sales invoice opening bill "INV-1" on
    /// 05-Apr-2024 and, when <paramref name="settle"/> is set, a receipt knocking it off on 06-Apr-2024.
    /// The split date is 01-May-2024, so the bill's fate is decided entirely before the cutoff.
    /// </summary>
    private static Company BillWiseCompany(out DateOnly splitDate, bool settle = false, bool openBillAfterSplit = false)
    {
        splitDate = new DateOnly(2024, 5, 1);
        var company = CompanyFactory.CreateSeeded("Bill-wise Ltd", new DateOnly(2024, 4, 1), new DateOnly(2024, 4, 1));
        var service = new LedgerService(company);

        var party = new Domain.Ledger(Guid.NewGuid(), "Ace Traders",
            company.FindGroupByName("Sundry Debtors")!.Id, Money.Zero, openingIsDebit: true,
            maintainBillByBill: true, defaultCreditPeriodDays: 30);
        var salesLedger = new Domain.Ledger(Guid.NewGuid(), "Sales",
            company.FindGroupByName("Sales Accounts")!.Id, Money.Zero, openingIsDebit: false);
        company.AddLedger(party);
        company.AddLedger(salesLedger);
        var cash = company.FindLedgerByName(Seed.SeedLedgers.CashName)!;
        var journal = company.FindVoucherTypeByName("Journal")!;
        var receiptType = company.FindVoucherTypeByName("Receipt")!;

        // A credit sale opening bill "INV-1".
        service.Post(new Voucher(Guid.NewGuid(), journal.Id, new DateOnly(2024, 4, 5), new[]
        {
            new EntryLine(party.Id, Money.FromRupees(1000m), DrCr.Debit, new[]
            {
                new BillAllocation(BillRefType.NewRef, "INV-1", Money.FromRupees(1000m)),
            }),
            new EntryLine(salesLedger.Id, Money.FromRupees(1000m), DrCr.Credit),
        }));

        // Either the receipt that knocks it off, or an unrelated one that leaves it open — either way the
        // book carries exactly two vouchers, so the refusal case and the clear case are otherwise identical.
        var second = settle
            ? new Voucher(Guid.NewGuid(), receiptType.Id, new DateOnly(2024, 4, 6), new[]
            {
                new EntryLine(cash.Id, Money.FromRupees(1000m), DrCr.Debit),
                new EntryLine(party.Id, Money.FromRupees(1000m), DrCr.Credit, new[]
                {
                    new BillAllocation(BillRefType.AgstRef, "INV-1", Money.FromRupees(1000m)),
                }),
            })
            : new Voucher(Guid.NewGuid(), receiptType.Id, new DateOnly(2024, 4, 6), new[]
            {
                new EntryLine(cash.Id, Money.FromRupees(10m), DrCr.Debit),
                new EntryLine(salesLedger.Id, Money.FromRupees(10m), DrCr.Credit),
            });
        service.Post(second);

        // One entry after the split date, so the period refusals are clear and the bill-wise refusal is the
        // only thing this book can be refused for.
        service.Post(new Voucher(Guid.NewGuid(), receiptType.Id, new DateOnly(2024, 5, 2), new[]
        {
            new EntryLine(cash.Id, Money.FromRupees(20m), DrCr.Debit),
            new EntryLine(salesLedger.Id, Money.FromRupees(20m), DrCr.Credit),
        }));

        // A bill opened AFTER the split date — it belongs to the from-book alone, and is what proves the split
        // carries the later period's bills rather than only clearing the earlier one's.
        if (openBillAfterSplit)
            service.Post(new Voucher(Guid.NewGuid(), journal.Id, new DateOnly(2024, 5, 3), new[]
            {
                new EntryLine(party.Id, Money.FromRupees(500m), DrCr.Debit, new[]
                {
                    new BillAllocation(BillRefType.NewRef, "INV-2", Money.FromRupees(500m)),
                }),
                new EntryLine(salesLedger.Id, Money.FromRupees(500m), DrCr.Credit),
            }));

        return company;
    }
}
