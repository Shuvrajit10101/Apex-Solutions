using Apex.Ledger.Domain;

namespace Apex.Ledger.Reports;

/// <summary>
/// One row of a ledger book (design §7.6) — a posting with the running balance after it. <see cref="VoucherId"/>
/// is the posting voucher's stable id (RQ-7 universal drill-down): Enter on a ledger-vouchers row opens that
/// voucher's detail. Every ledger-book row is a real posting, so <see cref="IsDrillable"/> is always true.
/// </summary>
public sealed record LedgerBookRow(
    DateOnly Date,
    string VoucherTypeName,
    int Number,
    string? CounterParticulars,
    Money Debit,
    Money Credit,
    DrCr RunningSide,
    Money RunningAmount,
    Guid VoucherId = default)
{
    /// <summary>True iff Enter should drill this row into the underlying voucher's detail.</summary>
    public bool IsDrillable => VoucherId != Guid.Empty;
}

/// <summary>
/// A single ledger's book (design §7.6): opening balance, then every posting to that
/// ledger in date order with a running balance after each. The final running balance
/// equals the ledger's closing balance. Cash Book / Bank Book are this projection
/// filtered to a Cash-in-Hand / Bank-Accounts ledger.
/// </summary>
public sealed record LedgerBook(
    string LedgerName,
    DrCr OpeningSide,
    Money OpeningAmount,
    IReadOnlyList<LedgerBookRow> Rows,
    DrCr ClosingSide,
    Money ClosingAmount)
{
    /// <summary>
    /// Builds the ledger's book over [<paramref name="from"/>,<paramref name="to"/>].
    /// <para><paramref name="movement"/> selects the running-balance origin so the book reconciles to the
    /// figure the drill was launched from (RQ-7):</para>
    /// <list type="bullet">
    /// <item><b>false</b> (default — a Trial-Balance / Balance-Sheet, i.e. a point-in-time / closing-balance
    /// drill): the running balance starts from the signed OPENING and accumulates every posting up to each
    /// row, and the closing is the ledger's closing-as-at-<paramref name="to"/> — the same figure the TB/BS
    /// row showed.</item>
    /// <item><b>true</b> (a Profit-&amp;-Loss, i.e. a flow / period-movement drill): the running balance starts
    /// from ZERO and accumulates only the in-window postings, so the opening line reads 0 and the closing
    /// equals the period MOVEMENT — the same figure the P&amp;L row showed. A cumulative closing would diverge
    /// from the displayed period figure whenever the ledger had activity before the period start.</item>
    /// </list>
    /// </summary>
    public static LedgerBook Build(Company company, Guid ledgerId, DateOnly from, DateOnly to, bool movement = false)
    {
        // RQ-7 defensive guard: a drill on a non-drillable row (Guid.Empty ledger id — a folded/synthetic/total
        // row) must never throw. The UI's IsDrillable guard already prevents this call, but returning an EMPTY
        // book here makes the engine safe regardless of the caller.
        if (ledgerId == Guid.Empty)
            return new LedgerBook(string.Empty, DrCr.Debit, Money.Zero, [], DrCr.Debit, Money.Zero);

        var ledger = company.FindLedger(ledgerId)
            ?? throw new InvalidOperationException($"Ledger {ledgerId} not found.");

        // Point-in-time (TB/BS) drills carry the opening forward; a flow (P&L) drill measures the in-window
        // movement only, so its running balance and opening line both start at zero.
        var openingSigned = movement ? 0m : ledger.SignedOpening;
        var openingSide = openingSigned < 0m ? DrCr.Credit : DrCr.Debit;
        var openingAmount = movement ? Money.Zero : ledger.OpeningBalance;

        // Running balance starts from the chosen origin (opening for point-in-time, zero for movement).
        var running = openingSigned;

        // Collect this ledger's postings that count as-of, ordered by (date, number).
        var contributing = new List<(Voucher Voucher, EntryLine Line)>();
        foreach (var v in company.Vouchers)
        {
            if (!LedgerBalances.CountsAsOf(v, to)) continue;
            foreach (var line in v.Lines)
                if (line.LedgerId == ledgerId)
                    contributing.Add((v, line));
        }

        contributing.Sort((a, b) =>
        {
            var byDate = a.Voucher.Date.CompareTo(b.Voucher.Date);
            return byDate != 0 ? byDate : a.Voucher.Number.CompareTo(b.Voucher.Number);
        });

        var rows = new List<LedgerBookRow>();
        foreach (var (v, line) in contributing)
        {
            // A movement book ignores pre-window postings entirely (they belong to the opening it is
            // deliberately excluding); a point-in-time book accumulates them into the running balance so the
            // first in-window row already reflects everything before it.
            if (movement && v.Date < from) continue;

            running += line.Signed;

            // Only emit rows within the requested display window.
            if (v.Date < from || v.Date > to) continue;

            var type = company.FindVoucherType(v.TypeId);
            var counter = CounterParty(company, v, ledgerId);
            var runBal = LedgerBalance.FromSigned(running);

            rows.Add(new LedgerBookRow(
                v.Date,
                type?.Name ?? "(unknown)",
                v.Number,
                counter,
                line.Side == DrCr.Debit ? line.Amount : Money.Zero,
                line.Side == DrCr.Credit ? line.Amount : Money.Zero,
                runBal.Side,
                runBal.Amount,
                v.Id));
        }

        // Point-in-time: the ledger's closing-as-at-To (== the TB/BS figure). Movement: the accumulated
        // in-window running balance (== the P&L period figure), which the running total already holds.
        var closing = movement
            ? LedgerBalance.FromSigned(running)
            : LedgerBalances.Closing(company, ledger, to);

        return new LedgerBook(
            ledger.Name,
            openingSide,
            openingAmount,
            rows,
            closing.Side,
            closing.Amount);
    }

    /// <summary>The single other-side ledger name (or "(multiple)") for a voucher's counter particulars.</summary>
    private static string? CounterParty(Company company, Voucher v, Guid selfLedgerId)
    {
        var others = v.Lines
            .Where(l => l.LedgerId != selfLedgerId)
            .Select(l => company.FindLedger(l.LedgerId)?.Name ?? "(unknown)")
            .Distinct()
            .ToList();

        return others.Count switch
        {
            0 => null,
            1 => others[0],
            _ => "(multiple)",
        };
    }
}
