using Apex.Ledger.Domain;

namespace Apex.Ledger.Reports;

/// <summary>
/// <b>TCS Challan Reconciliation</b> — a pure, read-only projection matching TCS <b>deposits</b> (the ITNS-281
/// <see cref="TcsChallan"/>s) against TCS <b>collections</b> (the posted <see cref="TcsLineTax"/> collections) over
/// <c>[from, to]</c>, per §206C collection code (catalog §13; Phase 7 slice 6). The exact mirror of the TDS
/// <see cref="ChallanReconciliation"/>: for each collection code it reports the total collected, the total
/// deposited, and the <b>remaining payable</b> (= collected − deposited); a code is
/// <see cref="TcsCodeReconciliation.IsMatched"/> when the two tie (remaining 0). Every figure is read off posted
/// vouchers and recorded challans — never recomputed — so the reconciliation reconciles to the "TCS Payable"
/// ledger postings by construction. Deterministic (ordered by collection code, ordinal) with no clock/RNG.
/// </summary>
public sealed record TcsChallanReconciliation(
    DateOnly From,
    DateOnly To,
    IReadOnlyList<TcsCodeReconciliation> Codes)
{
    /// <summary>Σ TCS collected across all collection codes in the window.</summary>
    public Money TotalCollected => new(Codes.Sum(s => s.Collected.Amount));

    /// <summary>Σ TCS deposited (challans) across all collection codes in the window.</summary>
    public Money TotalDeposited => new(Codes.Sum(s => s.Deposited.Amount));

    /// <summary>Σ remaining payable across all codes (collected − deposited); a positive total is undeposited TCS.</summary>
    public Money TotalRemaining => new(Codes.Sum(s => s.Remaining.Amount));

    /// <summary>True iff every code is matched (nothing collected-but-undeposited and no orphan deposit).</summary>
    public bool IsFullyReconciled => Codes.All(s => s.IsMatched);

    /// <summary>The codes that do not tie (collected ≠ deposited) — the reconciliation exceptions.</summary>
    public IReadOnlyList<TcsCodeReconciliation> Unmatched => Codes.Where(s => !s.IsMatched).ToList();

    /// <summary>
    /// Builds the reconciliation over <c>[from, to]</c>: sums the collected TCS off every posted, non-cancelled
    /// voucher's <see cref="TcsLineTax"/> (grouped by collection code) and the deposited amount off every
    /// <see cref="TcsChallan"/> whose <see cref="TcsChallan.DepositDate"/> falls in the window (grouped by code)
    /// <b>and whose booking Stat-Payment voucher is still live</b> (a cancelled/absent booking drops the challan, so
    /// the deposited side tracks the ledger). A code appears when it has any collection OR any deposit in the window.
    /// </summary>
    public static TcsChallanReconciliation Build(Company company, DateOnly from, DateOnly to)
    {
        ArgumentNullException.ThrowIfNull(company);

        var collected = new Dictionary<string, decimal>(StringComparer.Ordinal);
        foreach (var v in company.Vouchers)
        {
            if (v.Cancelled) continue;
            if (v.Date < from || v.Date > to) continue;
            foreach (var line in v.Lines)
            {
                if (line.Tcs is not { } t) continue;
                if (t.TcsAmount.Amount <= 0m) continue; // a below-threshold assessment carries no deposit obligation
                collected[t.CollectionCode] = collected.GetValueOrDefault(t.CollectionCode) + t.TcsAmount.Amount;
            }
        }

        var deposited = new Dictionary<string, decimal>(StringComparer.Ordinal);
        foreach (var ch in company.TcsChallans)
        {
            if (ch.DepositDate < from || ch.DepositDate > to) continue;
            // A challan only counts as deposited while its booking Stat-Payment voucher is live. If that voucher was
            // cancelled (or the link is absent), the deposit was reversed off the "TCS Payable" ledger — the collected
            // side already skips cancelled vouchers (above) and LedgerBalances.SignedClosing likewise — so the
            // deposited side must drop it too, keeping the reconciliation tied to the ledger balance by construction.
            if (!ChallanHasLiveVoucher(company, ch.Id)) continue;
            deposited[ch.CollectionCode] = deposited.GetValueOrDefault(ch.CollectionCode) + ch.Amount.Amount;
        }

        var codes = collected.Keys.Union(deposited.Keys, StringComparer.Ordinal)
            .OrderBy(s => s, StringComparer.Ordinal)
            .Select(s =>
            {
                var d = collected.GetValueOrDefault(s);
                var p = deposited.GetValueOrDefault(s);
                return new TcsCodeReconciliation(s, new Money(d), new Money(p), new Money(d - p));
            })
            .ToList();

        return new TcsChallanReconciliation(from, to, codes);
    }

    /// <summary>True iff <paramref name="challanId"/> is linked to at least one Stat-Payment voucher that still
    /// exists and is not cancelled — i.e. the deposit it records is live on the "TCS Payable" ledger.</summary>
    private static bool ChallanHasLiveVoucher(Company company, Guid challanId)
    {
        foreach (var voucherId in company.VouchersLinkedToTcsChallan(challanId))
            if (company.FindVoucher(voucherId) is { Cancelled: false })
                return true;
        return false;
    }
}

/// <summary>One §206C collection code's row in the <see cref="TcsChallanReconciliation"/> (Phase 7 slice 6).</summary>
/// <param name="CollectionCode">The §206C Form-27EQ collection code (e.g. "6CE" scrap).</param>
/// <param name="Collected">TCS collected under this code in the window.</param>
/// <param name="Deposited">TCS deposited (challans) under this code in the window.</param>
/// <param name="Remaining">Collected − Deposited: positive ⇒ undeposited; negative ⇒ over-deposited/orphan.</param>
public sealed record TcsCodeReconciliation(string CollectionCode, Money Collected, Money Deposited, Money Remaining)
{
    /// <summary>True iff the deposit exactly discharges the collection (remaining 0).</summary>
    public bool IsMatched => Remaining.Amount == 0m;

    /// <summary>True iff collected &gt; deposited — TCS collected but not (fully) deposited (an outstanding liability).</summary>
    public bool IsUnderpaid => Remaining.Amount > 0m;

    /// <summary>True iff deposited &gt; collected — a challan with no matching collection (an orphan/over-deposit).</summary>
    public bool IsOverpaid => Remaining.Amount < 0m;
}
