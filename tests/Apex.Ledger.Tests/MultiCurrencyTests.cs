using Apex.Ledger.Domain;
using Apex.Ledger.Reports;
using Apex.Ledger.Services;
using Xunit;
using Domain = Apex.Ledger.Domain;

namespace Apex.Ledger.Tests;

/// <summary>
/// Multi-currency tests (catalog §2/§20; plan.md §10 C-1): the Currency master + base-currency seed;
/// dated Rates of Exchange and the rate in force; a foreign-currency line converting forex × rate to the
/// exact base amount (with the base ledger math unchanged); the validator rejecting a forex line whose
/// base amount ≠ forex × rate; realized forex gain/loss on settlement; and the period-end unrealized-forex
/// revaluation that books the right gain/loss into a balanced adjusting Journal.
/// </summary>
public class MultiCurrencyTests
{
    // A company with a USD currency and a couple of USD rates. Returns the USD currency id.
    private static Company SeedWithUsd(out Guid usdId)
    {
        var c = CompanyFactory.CreateSeeded("Forex Co", new DateOnly(2024, 1, 1));
        var usd = new Currency(Guid.NewGuid(), "$", "USD", decimalPlaces: 2);
        c.AddCurrency(usd);
        usdId = usd.Id;

        // ₹80 per US$1 on 1-Jan, ₹83 per US$1 on 31-Mar.
        c.AddExchangeRate(new ExchangeRate(Guid.NewGuid(), usd.Id, new DateOnly(2024, 1, 1), 80m,
            sellingRate: 80.5m, buyingRate: 79.5m));
        c.AddExchangeRate(new ExchangeRate(Guid.NewGuid(), usd.Id, new DateOnly(2024, 3, 31), 83m));
        return c;
    }

    // ---------------------------------------------------------------- (1) Currency master + base seed

    [Fact]
    public void Base_currency_INR_is_seeded_on_company_create()
    {
        var c = CompanyFactory.CreateSeeded("Seed Co");

        var baseCur = c.BaseCurrency;
        Assert.NotNull(baseCur);
        Assert.True(baseCur!.IsBaseCurrency);
        Assert.Equal("₹", baseCur.Symbol);
        Assert.Equal("INR", baseCur.FormalName);
        Assert.Equal(2, baseCur.DecimalPlaces);

        // Exactly one base currency; a second base is rejected.
        Assert.Single(c.Currencies, x => x.IsBaseCurrency);
        Assert.Throws<InvalidOperationException>(() =>
            c.AddCurrency(new Currency(Guid.NewGuid(), "$", "USD", isBaseCurrency: true)));
    }

    [Fact]
    public void A_currency_and_rate_can_be_created_and_the_rate_in_force_resolves()
    {
        var c = SeedWithUsd(out var usdId);

        Assert.NotNull(c.FindCurrency(usdId));
        Assert.Equal(usdId, c.FindCurrencyByName("USD")!.Id);
        Assert.Equal(usdId, c.FindCurrencyByName("$")!.Id);

        // Rate in force: latest dated on/before the as-of date.
        Assert.Equal(80m, c.RateInForce(usdId, new DateOnly(2024, 1, 15))!.StandardRate);
        Assert.Equal(83m, c.RateInForce(usdId, new DateOnly(2024, 4, 1))!.StandardRate);
        // Before the first quote: none.
        Assert.Null(c.RateInForce(usdId, new DateOnly(2023, 12, 31)));

        // Selling/Buying resolve; a missing directional rate falls back to Standard.
        var jan = c.RateInForce(usdId, new DateOnly(2024, 1, 1))!;
        Assert.Equal(80.5m, jan.RateOf(ExchangeRateKind.Selling));
        Assert.Equal(79.5m, jan.RateOf(ExchangeRateKind.Buying));
        var mar = c.RateInForce(usdId, new DateOnly(2024, 3, 31))!;
        Assert.Equal(83m, mar.RateOf(ExchangeRateKind.Selling)); // falls back to Standard
    }

    // ---------------------------------------------------------------- (2) forex line = forex × rate

    [Fact]
    public void A_foreign_line_converts_forex_times_rate_to_the_exact_base_amount()
    {
        var c = SeedWithUsd(out var usdId);

        // A US$1,000 sale at ₹80 → base ₹80,000 exactly.
        var forex = new ForexInfo(usdId, Money.FromRupees(1000m), 80m);
        Assert.Equal(Money.FromRupees(80000m), forex.BaseValue);

        var cash = c.FindLedgerByName("Cash")!;
        var sales = new Domain.Ledger(Guid.NewGuid(), "Export Sales",
            c.FindGroupByName("Sales Accounts")!.Id, Money.Zero, openingIsDebit: false, currencyId: usdId);
        c.AddLedger(sales);

        var svc = new LedgerService(c);
        var receipt = c.FindVoucherTypeByName("Receipt")!;
        var v = svc.Post(new Voucher(Guid.NewGuid(), receipt.Id, new DateOnly(2024, 1, 10), new[]
        {
            new EntryLine(cash.Id, Money.FromRupees(80000m), DrCr.Debit),
            new EntryLine(sales.Id, Money.FromRupees(80000m), DrCr.Credit,
                forex: new ForexInfo(usdId, Money.FromRupees(1000m), 80m)),
        }));

        // The base amount the ledger engine sees is the exact paisa value; balances reconcile.
        var salesBal = LedgerBalances.Closing(c, sales, new DateOnly(2024, 1, 31));
        Assert.Equal(DrCr.Credit, salesBal.Side);
        Assert.Equal(Money.FromRupees(80000m), salesBal.Amount);

        var line = v.Lines.Single(l => l.HasForex);
        Assert.Equal(usdId, line.Forex!.CurrencyId);
        Assert.Equal(Money.FromRupees(1000m), line.Forex.ForexAmount);
        Assert.Equal(80m, line.Forex.Rate);
    }

    // ---------------------------------------------------------------- (2b) NON-ROUND rate = paisa-exact base

    [Fact]
    public void A_forex_line_at_a_non_round_rate_posts_with_a_paisa_exact_base_and_never_throws()
    {
        // A rate whose product carries a sub-paisa tail: US$100 × 83.33335 = ₹8 333.335 (3 dp). The base
        // MUST be snapped to the paisa (₹8 333.34) — otherwise the paisa store throws on Save. This is the
        // exact bug: the line posts in memory but ₹8 333.335 is not paisa-exact.
        var c = SeedWithUsd(out var usdId);
        var cash = c.FindLedgerByName("Cash")!;
        var sales = new Domain.Ledger(Guid.NewGuid(), "Export Sales",
            c.FindGroupByName("Sales Accounts")!.Id, Money.Zero, openingIsDebit: false, currencyId: usdId);
        c.AddLedger(sales);

        // The authoritative base = paisa-rounded forex × rate.
        var forexAmount = Money.FromRupees(100m);
        var rate = 83.33335m;
        var expectedBase = Money.FromRupees(Math.Round(100m * rate, 2, MidpointRounding.AwayFromZero)); // 8333.34
        Assert.Equal(Money.FromRupees(8333.34m), expectedBase);

        var forex = new ForexInfo(usdId, forexAmount, rate);
        // BaseValue is paisa-exact (not the raw 8333.335 product).
        Assert.Equal(expectedBase, forex.BaseValue);

        var svc = new LedgerService(c);
        var receipt = c.FindVoucherTypeByName("Receipt")!;
        // (a) Posts without throwing, with the paisa-exact base on both legs.
        var v = svc.Post(new Voucher(Guid.NewGuid(), receipt.Id, new DateOnly(2024, 1, 10), new[]
        {
            new EntryLine(cash.Id, expectedBase, DrCr.Debit),
            new EntryLine(sales.Id, expectedBase, DrCr.Credit, forex: forex),
        }));

        var line = v.Lines.Single(l => l.HasForex);
        Assert.Equal(expectedBase, line.Amount);
        // The base is paisa-exact: rupees × 100 is a whole number.
        Assert.Equal(Math.Truncate(line.Amount.Amount * 100m), line.Amount.Amount * 100m);
        // The forex detail is preserved verbatim (only the BASE is rounded, not the rate/amount).
        Assert.Equal(rate, line.Forex!.Rate);
        Assert.Equal(forexAmount, line.Forex.ForexAmount);

        var bal = LedgerBalances.Closing(c, sales, new DateOnly(2024, 1, 31));
        Assert.Equal(DrCr.Credit, bal.Side);
        Assert.Equal(expectedBase, bal.Amount);
    }

    [Fact]
    public void A_non_round_rate_revaluation_and_adjusting_journal_are_paisa_exact_and_never_throw()
    {
        // Book a US$100 receivable at a non-round 83.33335 (base ₹8 333.34), then revalue at another
        // non-round rate 84.44445 (base ₹8 444.45 → gain ₹111.11). Both the revaluation and the adjusting
        // journal must be paisa-exact so they never throw when posted/saved.
        var c = SeedWithUsd(out var usdId);
        var sales = new Domain.Ledger(Guid.NewGuid(), "Export Sales",
            c.FindGroupByName("Sales Accounts")!.Id, Money.Zero, openingIsDebit: false);
        var debtor = new Domain.Ledger(Guid.NewGuid(), "US Customer",
            c.FindGroupByName("Sundry Debtors")!.Id, Money.Zero, openingIsDebit: true, currencyId: usdId);
        var forexGl = new Domain.Ledger(Guid.NewGuid(), ForexGainLoss.ForexGainLossLedgerName,
            c.FindGroupByName("Indirect Expenses")!.Id, Money.Zero, openingIsDebit: true);
        c.AddLedger(sales);
        c.AddLedger(debtor);
        c.AddLedger(forexGl);

        var txnRate = 83.33335m;
        var baseBooked = new ForexInfo(usdId, Money.FromRupees(100m), txnRate).BaseValue; // 8333.34
        var svc = new LedgerService(c);
        var salesVt = c.FindVoucherTypeByName("Sales")!;
        svc.Post(new Voucher(Guid.NewGuid(), salesVt.Id, new DateOnly(2024, 1, 10), new[]
        {
            new EntryLine(debtor.Id, baseBooked, DrCr.Debit, forex: new ForexInfo(usdId, Money.FromRupees(100m), txnRate)),
            new EntryLine(sales.Id, baseBooked, DrCr.Credit),
        }));

        var asOf = new DateOnly(2024, 2, 15);
        var asOfRate = 84.44445m;
        var reval = ForexGainLoss.Revalue(c, asOf, new Dictionary<Guid, decimal> { [usdId] = asOfRate });
        var line = Assert.Single(reval.Lines);

        // Revalued base is paisa-exact (100 × 84.44445 = 8444.445 → 8444.45).
        var expectedRevalued = Money.FromRupees(Math.Round(100m * asOfRate, 2, MidpointRounding.AwayFromZero));
        Assert.Equal(Money.FromRupees(8444.45m), expectedRevalued);
        Assert.Equal(expectedRevalued, line.RevaluedBase);
        // Gain/loss is paisa-exact.
        Assert.Equal(Math.Truncate(line.GainLoss * 100m), line.GainLoss * 100m);
        Assert.Equal(111.11m, line.GainLoss); // 8444.45 − 8333.34

        // (c) The adjusting journal builds, balances, and every leg is paisa-exact — posting never throws.
        var journalVt = c.FindVoucherTypeByName("Journal")!;
        var adj = ForexGainLoss.BuildAdjustingJournal(c, reval, journalVt.Id, forexGl.Id);
        Assert.NotNull(adj);
        Assert.Equal(adj!.TotalDebit, adj.TotalCredit);
        Assert.All(adj.Lines, l => Assert.Equal(Math.Truncate(l.Amount.Amount * 100m), l.Amount.Amount * 100m));
        // Posting the adjusting journal moves the debtor to the revalued base without throwing.
        new LedgerService(c).Post(adj);
        Assert.Equal(expectedRevalued, LedgerBalances.Closing(c, debtor, asOf).Amount);
    }

    [Fact]
    public void A_forex_line_whose_base_does_not_equal_forex_times_rate_is_rejected()
    {
        var c = SeedWithUsd(out var usdId);
        var cash = c.FindLedgerByName("Cash")!;
        var sales = new Domain.Ledger(Guid.NewGuid(), "Export Sales",
            c.FindGroupByName("Sales Accounts")!.Id, Money.Zero, openingIsDebit: false, currencyId: usdId);
        c.AddLedger(sales);
        var svc = new LedgerService(c);
        var receipt = c.FindVoucherTypeByName("Receipt")!;

        // 1000 × 80 = 80,000, but the line's base amount says 79,000 → invalid.
        Assert.Throws<InvalidVoucherException>(() =>
            svc.Post(new Voucher(Guid.NewGuid(), receipt.Id, new DateOnly(2024, 1, 10), new[]
            {
                new EntryLine(cash.Id, Money.FromRupees(79000m), DrCr.Debit),
                new EntryLine(sales.Id, Money.FromRupees(79000m), DrCr.Credit,
                    forex: new ForexInfo(usdId, Money.FromRupees(1000m), 80m)),
            })));
    }

    [Fact]
    public void The_base_currency_INR_path_is_unchanged_by_multi_currency()
    {
        // A plain base-currency company + voucher (no forex anywhere) behaves exactly as before.
        var c = CompanyFactory.CreateSeeded("Base Only Co", new DateOnly(2024, 1, 1));
        var cash = c.FindLedgerByName("Cash")!;
        var rent = new Domain.Ledger(Guid.NewGuid(), "Rent",
            c.FindGroupByName("Indirect Expenses")!.Id, Money.Zero, openingIsDebit: true);
        c.AddLedger(rent);

        var svc = new LedgerService(c);
        var payment = c.FindVoucherTypeByName("Payment")!;
        var v = svc.Post(new Voucher(Guid.NewGuid(), payment.Id, new DateOnly(2024, 1, 5), new[]
        {
            new EntryLine(rent.Id, Money.FromRupees(5000m), DrCr.Debit),
            new EntryLine(cash.Id, Money.FromRupees(5000m), DrCr.Credit),
        }));

        Assert.All(v.Lines, l => Assert.False(l.HasForex));
        Assert.False(rent.IsForeignCurrency);
        var bal = LedgerBalances.Closing(c, rent, new DateOnly(2024, 1, 31));
        Assert.Equal(DrCr.Debit, bal.Side);
        Assert.Equal(Money.FromRupees(5000m), bal.Amount);
    }

    // ---------------------------------------------------------------- (3) settlement gain/loss

    [Fact]
    public void Settlement_gain_and_loss_reflect_the_rate_change_between_transaction_and_settlement()
    {
        // Receivable US$1,000 booked at 80, settled at 83 → +₹3,000 gain (received more base).
        var gain = ForexGainLoss.SettlementGainLoss(Money.FromRupees(1000m), transactionRate: 80m, settlementRate: 83m);
        Assert.Equal(3000m, gain);

        // Settled at 78 → −₹2,000 loss.
        var loss = ForexGainLoss.SettlementGainLoss(Money.FromRupees(1000m), transactionRate: 80m, settlementRate: 78m);
        Assert.Equal(-2000m, loss);

        // No rate change → zero.
        Assert.Equal(0m, ForexGainLoss.SettlementGainLoss(Money.FromRupees(1000m), 80m, 80m));
    }

    // ---------------------------------------------------------------- (4) period-end revaluation

    // Books a US$1,000 export receivable at ₹80 (debtor Dr ₹80,000) and revalues at ₹83.
    private static Company SeedReceivable(out Guid usdId, out Domain.Ledger debtor, out Domain.Ledger forexGl)
    {
        var c = SeedWithUsd(out usdId);
        var cash = c.FindLedgerByName("Cash")!;
        var sales = new Domain.Ledger(Guid.NewGuid(), "Export Sales",
            c.FindGroupByName("Sales Accounts")!.Id, Money.Zero, openingIsDebit: false);
        debtor = new Domain.Ledger(Guid.NewGuid(), "US Customer",
            c.FindGroupByName("Sundry Debtors")!.Id, Money.Zero, openingIsDebit: true, currencyId: usdId);
        forexGl = new Domain.Ledger(Guid.NewGuid(), ForexGainLoss.ForexGainLossLedgerName,
            c.FindGroupByName("Indirect Expenses")!.Id, Money.Zero, openingIsDebit: true);
        c.AddLedger(sales);
        c.AddLedger(debtor);
        c.AddLedger(forexGl);

        var svc = new LedgerService(c);
        var salesVt = c.FindVoucherTypeByName("Sales")!;
        // Debtor Dr 80,000 (forex US$1,000 @ 80); Sales Cr 80,000.
        svc.Post(new Voucher(Guid.NewGuid(), salesVt.Id, new DateOnly(2024, 1, 10), new[]
        {
            new EntryLine(debtor.Id, Money.FromRupees(80000m), DrCr.Debit,
                forex: new ForexInfo(usdId, Money.FromRupees(1000m), 80m)),
            new EntryLine(sales.Id, Money.FromRupees(80000m), DrCr.Credit),
        }));
        return c;
    }

    [Fact]
    public void Period_end_revaluation_books_an_unrealized_gain_on_a_receivable()
    {
        var c = SeedReceivable(out _, out var debtor, out var forexGl);
        var asOf = new DateOnly(2024, 3, 31); // rate now ₹83

        var reval = ForexGainLoss.Revalue(c, asOf);
        var line = Assert.Single(reval.Lines);
        Assert.Equal(debtor.Id, line.LedgerId);
        Assert.Equal(Money.FromRupees(1000m), line.ForexBalance);
        Assert.True(line.BalanceIsDebit);
        Assert.Equal(Money.FromRupees(80000m), line.BookedBase);
        Assert.Equal(83m, line.AsOfRate);
        Assert.Equal(Money.FromRupees(83000m), line.RevaluedBase);
        Assert.Equal(3000m, line.GainLoss);           // +₹3,000 unrealized gain
        Assert.Equal(3000m, reval.NetGainLoss);
        Assert.True(reval.IsNetGain);

        // The adjusting Journal: debtor Dr 3,000; Forex Gain/Loss Cr 3,000 (a gain = credit). Balanced.
        var journalVt = c.FindVoucherTypeByName("Journal")!;
        var adj = ForexGainLoss.BuildAdjustingJournal(c, reval, journalVt.Id, forexGl.Id);
        Assert.NotNull(adj);
        Assert.Equal(adj!.TotalDebit, adj.TotalCredit);

        var debtorLeg = adj.Lines.Single(l => l.LedgerId == debtor.Id);
        Assert.Equal(DrCr.Debit, debtorLeg.Side);
        Assert.Equal(Money.FromRupees(3000m), debtorLeg.Amount);
        var glLeg = adj.Lines.Single(l => l.LedgerId == forexGl.Id);
        Assert.Equal(DrCr.Credit, glLeg.Side);
        Assert.Equal(Money.FromRupees(3000m), glLeg.Amount);

        // Posting it moves the debtor's base balance to the revalued ₹83,000 and books the gain.
        new LedgerService(c).Post(adj);
        var debtorBal = LedgerBalances.Closing(c, debtor, asOf);
        Assert.Equal(DrCr.Debit, debtorBal.Side);
        Assert.Equal(Money.FromRupees(83000m), debtorBal.Amount);
        var glBal = LedgerBalances.Closing(c, forexGl, asOf);
        Assert.Equal(DrCr.Credit, glBal.Side);   // net income (gain)
        Assert.Equal(Money.FromRupees(3000m), glBal.Amount);
    }

    [Fact]
    public void Period_end_revaluation_books_an_unrealized_loss_on_a_payable()
    {
        var c = SeedWithUsd(out var usdId);
        var cash = c.FindLedgerByName("Cash")!;
        var purchases = new Domain.Ledger(Guid.NewGuid(), "Import Purchases",
            c.FindGroupByName("Purchase Accounts")!.Id, Money.Zero, openingIsDebit: true);
        var creditor = new Domain.Ledger(Guid.NewGuid(), "US Supplier",
            c.FindGroupByName("Sundry Creditors")!.Id, Money.Zero, openingIsDebit: false, currencyId: usdId);
        var forexGl = new Domain.Ledger(Guid.NewGuid(), ForexGainLoss.ForexGainLossLedgerName,
            c.FindGroupByName("Indirect Expenses")!.Id, Money.Zero, openingIsDebit: true);
        c.AddLedger(purchases);
        c.AddLedger(creditor);
        c.AddLedger(forexGl);

        var svc = new LedgerService(c);
        var purchaseVt = c.FindVoucherTypeByName("Purchase")!;
        // Creditor Cr 80,000 (forex US$1,000 @ 80); Purchases Dr 80,000.
        svc.Post(new Voucher(Guid.NewGuid(), purchaseVt.Id, new DateOnly(2024, 1, 10), new[]
        {
            new EntryLine(purchases.Id, Money.FromRupees(80000m), DrCr.Debit),
            new EntryLine(creditor.Id, Money.FromRupees(80000m), DrCr.Credit,
                forex: new ForexInfo(usdId, Money.FromRupees(1000m), 80m)),
        }));

        var asOf = new DateOnly(2024, 3, 31); // ₹83 now → the US$1,000 liability costs ₹83,000
        var reval = ForexGainLoss.Revalue(c, asOf);
        var line = Assert.Single(reval.Lines);
        Assert.Equal(creditor.Id, line.LedgerId);
        Assert.False(line.BalanceIsDebit);           // credit-side liability
        Assert.Equal(Money.FromRupees(83000m), line.RevaluedBase);
        Assert.Equal(-3000m, line.GainLoss);          // −₹3,000 unrealized loss

        // Adjusting Journal: creditor Cr 3,000 (liability grows); Forex Gain/Loss Dr 3,000 (a loss).
        var journalVt = c.FindVoucherTypeByName("Journal")!;
        var adj = ForexGainLoss.BuildAdjustingJournal(c, reval, journalVt.Id, forexGl.Id)!;
        Assert.Equal(adj.TotalDebit, adj.TotalCredit);
        var creditorLeg = adj.Lines.Single(l => l.LedgerId == creditor.Id);
        Assert.Equal(DrCr.Credit, creditorLeg.Side);
        var glLeg = adj.Lines.Single(l => l.LedgerId == forexGl.Id);
        Assert.Equal(DrCr.Debit, glLeg.Side);

        new LedgerService(c).Post(adj);
        var creditorBal = LedgerBalances.Closing(c, creditor, asOf);
        Assert.Equal(DrCr.Credit, creditorBal.Side);
        Assert.Equal(Money.FromRupees(83000m), creditorBal.Amount);
        var glBal = LedgerBalances.Closing(c, forexGl, asOf);
        Assert.Equal(DrCr.Debit, glBal.Side);        // net expense (loss)
        Assert.Equal(Money.FromRupees(3000m), glBal.Amount);
    }

    [Fact]
    public void Revaluation_with_an_explicit_as_of_rate_override_uses_that_rate()
    {
        var c = SeedReceivable(out var usdId, out var debtor, out _);
        // No dated rate for 15-Feb beyond the 1-Jan @80; override to 85.
        var reval = ForexGainLoss.Revalue(c, new DateOnly(2024, 2, 15),
            new Dictionary<Guid, decimal> { [usdId] = 85m });
        var line = Assert.Single(reval.Lines);
        Assert.Equal(85m, line.AsOfRate);
        Assert.Equal(Money.FromRupees(85000m), line.RevaluedBase);
        Assert.Equal(5000m, line.GainLoss); // 1000 × (85 − 80)
    }

    [Fact]
    public void The_adjusting_journal_balances_across_several_foreign_ledgers()
    {
        // A US receivable that gains and a US payable that loses the SAME base amount → the ledger legs
        // balance and NO Forex Gain/Loss leg is needed, but each ledger is still adjusted.
        var c = SeedWithUsd(out var usdId);
        var cash = c.FindLedgerByName("Cash")!;
        var sales = new Domain.Ledger(Guid.NewGuid(), "Export Sales",
            c.FindGroupByName("Sales Accounts")!.Id, Money.Zero, openingIsDebit: false);
        var purchases = new Domain.Ledger(Guid.NewGuid(), "Import Purchases",
            c.FindGroupByName("Purchase Accounts")!.Id, Money.Zero, openingIsDebit: true);
        var debtor = new Domain.Ledger(Guid.NewGuid(), "US Customer",
            c.FindGroupByName("Sundry Debtors")!.Id, Money.Zero, openingIsDebit: true, currencyId: usdId);
        var creditor = new Domain.Ledger(Guid.NewGuid(), "US Supplier",
            c.FindGroupByName("Sundry Creditors")!.Id, Money.Zero, openingIsDebit: false, currencyId: usdId);
        var forexGl = new Domain.Ledger(Guid.NewGuid(), ForexGainLoss.ForexGainLossLedgerName,
            c.FindGroupByName("Indirect Expenses")!.Id, Money.Zero, openingIsDebit: true);
        foreach (var l in new[] { sales, purchases, debtor, creditor, forexGl }) c.AddLedger(l);

        var svc = new LedgerService(c);
        var salesVt = c.FindVoucherTypeByName("Sales")!;
        var purchaseVt = c.FindVoucherTypeByName("Purchase")!;
        // Both US$1,000 @ 80. At 83, the receivable gains +3,000 and the payable loses −3,000 → net 0.
        svc.Post(new Voucher(Guid.NewGuid(), salesVt.Id, new DateOnly(2024, 1, 10), new[]
        {
            new EntryLine(debtor.Id, Money.FromRupees(80000m), DrCr.Debit,
                forex: new ForexInfo(usdId, Money.FromRupees(1000m), 80m)),
            new EntryLine(sales.Id, Money.FromRupees(80000m), DrCr.Credit),
        }));
        svc.Post(new Voucher(Guid.NewGuid(), purchaseVt.Id, new DateOnly(2024, 1, 10), new[]
        {
            new EntryLine(purchases.Id, Money.FromRupees(80000m), DrCr.Debit),
            new EntryLine(creditor.Id, Money.FromRupees(80000m), DrCr.Credit,
                forex: new ForexInfo(usdId, Money.FromRupees(1000m), 80m)),
        }));

        var asOf = new DateOnly(2024, 3, 31);
        var reval = ForexGainLoss.Revalue(c, asOf);
        Assert.Equal(2, reval.Lines.Count);
        Assert.Equal(0m, reval.NetGainLoss); // gains and losses cancel across the two ledgers

        var journalVt = c.FindVoucherTypeByName("Journal")!;
        var adj = ForexGainLoss.BuildAdjustingJournal(c, reval, journalVt.Id, forexGl.Id)!;
        Assert.Equal(adj.TotalDebit, adj.TotalCredit);
        // No Forex Gain/Loss leg when the ledger legs already net to zero.
        Assert.DoesNotContain(adj.Lines, l => l.LedgerId == forexGl.Id);
        Assert.Equal(2, adj.Lines.Count);

        new LedgerService(c).Post(adj);
        Assert.Equal(Money.FromRupees(83000m), LedgerBalances.Closing(c, debtor, asOf).Amount);
        Assert.Equal(Money.FromRupees(83000m), LedgerBalances.Closing(c, creditor, asOf).Amount);
    }

    [Fact]
    public void A_ledger_with_no_forex_movement_is_not_revalued()
    {
        var c = SeedWithUsd(out var usdId);
        // A foreign-currency ledger declared but never transacted → no revaluation line.
        var idle = new Domain.Ledger(Guid.NewGuid(), "Idle USD",
            c.FindGroupByName("Sundry Debtors")!.Id, Money.Zero, openingIsDebit: true, currencyId: usdId);
        c.AddLedger(idle);

        var reval = ForexGainLoss.Revalue(c, new DateOnly(2024, 3, 31));
        Assert.Empty(reval.Lines);
        Assert.Equal(0m, reval.NetGainLoss);
    }
}
