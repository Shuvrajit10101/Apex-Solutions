using Apex.Ledger.Domain;

namespace Apex.Ledger.Reports;

/// <summary>
/// One foreign-currency ledger's unrealized-forex position at a period-end revaluation (catalog §2/§20
/// Multi-currency; plan.md §10 C-1). It carries the ledger's <see cref="ForexBalance"/> (the net amount
/// held in its foreign currency), the <see cref="BookedBase"/> value at the original transaction rates,
/// the <see cref="AsOfRate"/> used for revaluation, the <see cref="RevaluedBase"/> value at that rate,
/// and the resulting <see cref="GainLoss"/> — a signed base amount (positive = gain, negative = loss)
/// on the reporting entity's books.
/// </summary>
public sealed record ForexRevaluationLine(
    Guid LedgerId,
    string LedgerName,
    Guid CurrencyId,
    Money ForexBalance,
    bool BalanceIsDebit,
    Money BookedBase,
    decimal AsOfRate,
    Money RevaluedBase,
    decimal GainLoss);

/// <summary>
/// A period-end unrealized-forex revaluation over every open foreign-currency ledger balance (catalog
/// §2/§20; plan.md §10 C-1): one <see cref="ForexRevaluationLine"/> per foreign-currency ledger with a
/// non-zero balance, plus the net gain/loss across them and a flag for the overall direction.
/// </summary>
public sealed record ForexRevaluation(
    DateOnly AsOf,
    IReadOnlyList<ForexRevaluationLine> Lines)
{
    /// <summary>Signed net gain/loss across every line (positive = net gain, negative = net loss).</summary>
    public decimal NetGainLoss
    {
        get
        {
            var s = 0m;
            foreach (var l in Lines) s += l.GainLoss;
            return s;
        }
    }

    /// <summary>The net gain/loss as a magnitude.</summary>
    public Money NetGainLossMagnitude => new(Math.Abs(NetGainLoss));

    /// <summary>True iff the net revaluation is a gain (or zero); false when it is a net loss.</summary>
    public bool IsNetGain => NetGainLoss >= 0m;
}

/// <summary>
/// Pure forex gain/loss projection over the posted voucher set (catalog §2/§20 Multi-currency; plan.md
/// §10 C-1). No UI, no DB. Two things live here:
/// <list type="bullet">
/// <item><b>Realized gain/loss on settlement</b> — <see cref="SettlementGainLoss"/> — the base difference
///   between the rate a forex amount was transacted at and the rate it was settled at.</item>
/// <item><b>Unrealized period-end revaluation</b> — <see cref="Revalue"/> — revalues each open
///   foreign-currency ledger balance at a given as-of rate and computes the gain/loss;
///   <see cref="BuildAdjustingJournal"/> turns that into a balanced adjusting Journal against a
///   <b>Forex Gain/Loss</b> ledger.</item>
/// </list>
/// </summary>
public static class ForexGainLoss
{
    /// <summary>The conventional name of the seeded/created Forex Gain/Loss ledger (catalog §2).</summary>
    public const string ForexGainLossLedgerName = "Forex Gain/Loss";

    /// <summary>
    /// Realized forex gain/loss on settling a foreign amount (catalog §2): the base difference between the
    /// transaction rate and the settlement rate, as a <b>signed</b> amount from the point of view of a
    /// <b>receivable</b> (an asset you will receive foreign currency for). For a receivable, a settlement
    /// rate <i>higher</i> than the transaction rate is a gain (you receive more base); a lower rate is a
    /// loss. For a payable, negate the sign (or pass the amounts from the payable's perspective).
    /// </summary>
    /// <param name="forexAmount">The foreign-currency magnitude being settled (&gt; 0).</param>
    /// <param name="transactionRate">The rate the amount was originally booked at (base per foreign).</param>
    /// <param name="settlementRate">The rate at settlement (base per foreign).</param>
    /// <returns>Signed base gain (positive) or loss (negative) for a receivable.</returns>
    public static decimal SettlementGainLoss(Money forexAmount, decimal transactionRate, decimal settlementRate)
    {
        if (forexAmount.Amount <= 0m)
            throw new ArgumentException("Forex amount must be > 0.", nameof(forexAmount));
        if (transactionRate <= 0m)
            throw new ArgumentException("Transaction rate must be > 0.", nameof(transactionRate));
        if (settlementRate <= 0m)
            throw new ArgumentException("Settlement rate must be > 0.", nameof(settlementRate));

        // Paisa-round the base difference: forexAmount × (settlement − transaction) can carry a sub-paisa
        // tail on non-round rates, and a realized gain/loss that is booked must be paisa-exact.
        return Math.Round(forexAmount.Amount * (settlementRate - transactionRate), 2, MidpointRounding.AwayFromZero);
    }

    /// <summary>
    /// The net <b>forex</b> balance (in the ledger's own currency) and the <b>booked base</b> balance for a
    /// foreign-currency ledger as of <paramref name="asOf"/>. Sums the signed forex amounts and the signed
    /// base amounts of the ledger's lines (Dr = +, Cr = −), counting the same posted vouchers the balance
    /// reports use. Opening balances are treated as base-only (they carry no forex detail) and are excluded
    /// from the forex sum (only forex-tagged movements are revalued).
    /// </summary>
    public static (decimal Forex, decimal Base) ForexPosition(Company company, Domain.Ledger ledger, DateOnly asOf)
    {
        var forex = 0m;
        var baseSigned = 0m;
        foreach (var v in company.Vouchers)
        {
            if (!LedgerBalances.CountsAsOf(v, asOf, company.FindVoucherType(v.TypeId)?.BaseType)) continue;
            foreach (var line in v.Lines)
            {
                if (line.LedgerId != ledger.Id) continue;
                var sign = line.Side == DrCr.Debit ? 1m : -1m;
                baseSigned += sign * line.Amount.Amount;
                if (line.Forex is { } fx)
                    forex += sign * fx.ForexAmount.Amount;
            }
        }
        return (forex, baseSigned);
    }

    /// <summary>
    /// Revalues every open foreign-currency ledger at its currency's rate in force on <paramref name="asOf"/>
    /// (or an explicit override supplied through <paramref name="rateOverride"/>), producing the unrealized
    /// gain/loss per ledger. A ledger with a zero forex balance, no currency, or no available rate is
    /// skipped. The per-ledger gain/loss is <c>revaluedBase − bookedBase</c> where
    /// <c>revaluedBase = forexBalance × asOfRate</c>; the sign is already correct for the books (a debit-side
    /// asset that is now worth more base is a gain, a credit-side liability that now costs more base is a loss).
    /// </summary>
    /// <param name="rateOverride">
    /// Optional as-of rate per currency id (base per 1 foreign unit). When a currency is present here its rate
    /// is used directly; otherwise the latest dated <see cref="ExchangeRate"/> on/before <paramref name="asOf"/>
    /// (its <see cref="ExchangeRateKind.Standard"/> rate) is used.
    /// </param>
    public static ForexRevaluation Revalue(
        Company company,
        DateOnly asOf,
        IReadOnlyDictionary<Guid, decimal>? rateOverride = null)
    {
        var lines = new List<ForexRevaluationLine>();
        foreach (var ledger in company.Ledgers)
        {
            if (ledger.CurrencyId is not { } currencyId) continue;

            var (forex, bookedBase) = ForexPosition(company, ledger, asOf);
            if (forex == 0m) continue;

            var rate = ResolveRate(company, currencyId, asOf, rateOverride);
            if (rate is not { } asOfRate) continue;

            // The revalued base = forex × asOfRate, snapped to the paisa so a non-round rate does not leave a
            // sub-paisa tail (bookedBase is already paisa-exact, so the gain/loss is paisa-exact too — the
            // adjusting-journal legs derived from it can then persist without a Paisa.FromDecimal throw).
            // The revalued base = forex × asOfRate, snapped to the paisa so a non-round rate does not leave a
            // sub-paisa tail (bookedBase is already paisa-exact, so the gain/loss is paisa-exact too — the
            // adjusting-journal legs derived from it can then persist without a Paisa.FromDecimal throw).
            var revaluedBase = Money.ForexBase(new Money(forex), asOfRate).Amount;
            var gainLoss = revaluedBase - bookedBase;

            lines.Add(new ForexRevaluationLine(
                ledger.Id, ledger.Name, currencyId,
                ForexBalance: new Money(Math.Abs(forex)),
                BalanceIsDebit: forex >= 0m,
                BookedBase: new Money(Math.Abs(bookedBase)),
                AsOfRate: asOfRate,
                RevaluedBase: new Money(Math.Abs(revaluedBase)),
                GainLoss: gainLoss));
        }
        return new ForexRevaluation(asOf, lines);
    }

    private static decimal? ResolveRate(
        Company company, Guid currencyId, DateOnly asOf, IReadOnlyDictionary<Guid, decimal>? rateOverride)
    {
        if (rateOverride is not null && rateOverride.TryGetValue(currencyId, out var overridden))
            return overridden;
        return company.RateInForce(currencyId, asOf)?.RateOf(ExchangeRateKind.Standard);
    }

    /// <summary>
    /// Builds the balanced adjusting Journal that books a period-end revaluation's net gain/loss against the
    /// <b>Forex Gain/Loss</b> ledger (catalog §2). Each foreign-currency ledger is adjusted by its own
    /// gain/loss so its base balance moves to the revalued base; the opposite side goes to the Forex
    /// Gain/Loss ledger. The Journal is returned <b>unposted</b> so the caller can post it through
    /// <see cref="Services.LedgerService"/>; it balances by construction (Σ Dr = Σ Cr).
    /// </summary>
    /// <returns>
    /// The adjusting <see cref="Voucher"/>, or <c>null</c> when there is nothing to adjust (no lines, or a
    /// net-zero revaluation with no individual ledger movements).
    /// </returns>
    public static Voucher? BuildAdjustingJournal(
        Company company,
        ForexRevaluation revaluation,
        Guid journalTypeId,
        Guid forexGainLossLedgerId)
    {
        ArgumentNullException.ThrowIfNull(revaluation);

        var lines = new List<EntryLine>();
        var netForexGlSigned = 0m; // signed movement that must go to the Forex Gain/Loss ledger

        foreach (var l in revaluation.Lines)
        {
            if (l.GainLoss == 0m) continue;

            // The foreign-currency ledger's base balance must move by +gainLoss (as a signed book value:
            // Dr = +). A positive gain/loss increases the ledger's debit-side value; a negative one credits it.
            // Snap to the paisa so a caller-supplied sub-paisa gain/loss cannot make the leg unpersistable.
            // Snap to the paisa so a caller-supplied sub-paisa gain/loss cannot make the leg unpersistable.
            var signedGl = Math.Round(l.GainLoss, 2, MidpointRounding.AwayFromZero);
            if (signedGl == 0m) continue;
            var magnitude = Math.Abs(signedGl);
            var ledgerSide = signedGl > 0m ? DrCr.Debit : DrCr.Credit;
            lines.Add(new EntryLine(l.LedgerId, new Money(magnitude), ledgerSide));

            // The contra to Forex Gain/Loss is the opposite sign.
            netForexGlSigned += -signedGl;
        }

        // Nothing to adjust: no foreign-currency ledger moved.
        if (lines.Count == 0) return null;

        // The contra to the Forex Gain/Loss ledger is the net of the ledger legs. It is zero only when the
        // per-ledger gains and losses already cancel across ledgers — in which case the ledger legs alone
        // balance and no Forex Gain/Loss leg is needed.
        var glMagnitude = Math.Abs(netForexGlSigned);
        if (glMagnitude > 0m)
        {
            var glSide = netForexGlSigned > 0m ? DrCr.Debit : DrCr.Credit;
            lines.Add(new EntryLine(forexGainLossLedgerId, new Money(glMagnitude), glSide));
        }

        // A single ledger leg with a zero contra cannot balance on its own — guard the degenerate case.
        if (lines.Count < 2) return null;

        return new Voucher(
            Guid.NewGuid(),
            journalTypeId,
            revaluation.AsOf,
            lines,
            number: 0,
            narration: "Unrealized forex gain/loss revaluation");
    }
}
