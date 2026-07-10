using Apex.Ledger.Domain;

namespace Apex.Ledger.Reports;

/// <summary>
/// <b>Challan Reconciliation (Alt+R)</b> — a pure, read-only projection matching TDS <b>deposits</b> (the ITNS-281
/// <see cref="TdsChallan"/>s) against TDS <b>deductions</b> (the posted <see cref="TdsLineTax"/> withholdings) over
/// <c>[from, to]</c>, per income-tax section (catalog §13; Phase 7 slice 3). For each section it reports the total
/// deducted, the total deposited, and the <b>remaining payable</b> (= deducted − deposited); a section is
/// <see cref="SectionReconciliation.IsMatched"/> when the two tie (remaining 0). Every figure is read off posted
/// vouchers and recorded challans — never recomputed — so the reconciliation reconciles to the "TDS Payable"
/// ledger postings by construction. Deterministic (ordered by section, ordinal) with no clock/RNG.
/// </summary>
public sealed record ChallanReconciliation(
    DateOnly From,
    DateOnly To,
    IReadOnlyList<SectionReconciliation> Sections)
{
    /// <summary>Σ TDS deducted across all sections in the window.</summary>
    public Money TotalDeducted => new(Sections.Sum(s => s.Deducted.Amount));

    /// <summary>Σ TDS deposited (challans) across all sections in the window.</summary>
    public Money TotalDeposited => new(Sections.Sum(s => s.Deposited.Amount));

    /// <summary>Σ remaining payable across all sections (deducted − deposited); a positive total is undeposited TDS.</summary>
    public Money TotalRemaining => new(Sections.Sum(s => s.Remaining.Amount));

    /// <summary>True iff every section is matched (nothing deducted-but-undeposited and no orphan deposit).</summary>
    public bool IsFullyReconciled => Sections.All(s => s.IsMatched);

    /// <summary>The sections that do not tie (deducted ≠ deposited) — the reconciliation exceptions.</summary>
    public IReadOnlyList<SectionReconciliation> Unmatched => Sections.Where(s => !s.IsMatched).ToList();

    /// <summary>
    /// Builds the reconciliation over <c>[from, to]</c>: sums the withheld TDS off every posted, non-cancelled
    /// voucher's <see cref="TdsLineTax"/> (grouped by section code) and the deposited amount off every
    /// <see cref="TdsChallan"/> whose <see cref="TdsChallan.DepositDate"/> falls in the window (grouped by section)
    /// <b>and whose booking Stat-Payment voucher is still live</b> (a cancelled/absent booking drops the challan, so
    /// the deposited side tracks the ledger). A section appears when it has any deduction OR any deposit in the window.
    /// </summary>
    public static ChallanReconciliation Build(Company company, DateOnly from, DateOnly to)
    {
        ArgumentNullException.ThrowIfNull(company);

        var deducted = new Dictionary<string, decimal>(StringComparer.Ordinal);
        foreach (var v in company.Vouchers)
        {
            if (v.Cancelled) continue;
            if (v.Date < from || v.Date > to) continue;
            foreach (var line in v.Lines)
            {
                if (line.Tds is not { } t) continue;
                if (t.TdsAmount.Amount <= 0m) continue; // a below-threshold assessment carries no deposit obligation
                deducted[t.SectionCode] = deducted.GetValueOrDefault(t.SectionCode) + t.TdsAmount.Amount;
            }
        }

        var deposited = new Dictionary<string, decimal>(StringComparer.Ordinal);
        foreach (var ch in company.TdsChallans)
        {
            if (ch.DepositDate < from || ch.DepositDate > to) continue;
            // A challan only counts as deposited while its booking Stat-Payment voucher is live. If that voucher was
            // cancelled (or the link is absent), the deposit was reversed off the "TDS Payable" ledger — the deducted
            // side already skips cancelled vouchers (above) and LedgerBalances.SignedClosing likewise — so the
            // deposited side must drop it too, keeping the reconciliation tied to the ledger balance by construction.
            if (!ChallanHasLiveVoucher(company, ch.Id)) continue;
            deposited[ch.Section] = deposited.GetValueOrDefault(ch.Section) + ch.Amount.Amount;
        }

        var sections = deducted.Keys.Union(deposited.Keys, StringComparer.Ordinal)
            .OrderBy(s => s, StringComparer.Ordinal)
            .Select(s =>
            {
                var d = deducted.GetValueOrDefault(s);
                var p = deposited.GetValueOrDefault(s);
                return new SectionReconciliation(s, new Money(d), new Money(p), new Money(d - p));
            })
            .ToList();

        return new ChallanReconciliation(from, to, sections);
    }

    /// <summary>True iff <paramref name="challanId"/> is linked to at least one Stat-Payment voucher that still
    /// exists and is not cancelled — i.e. the deposit it records is live on the "TDS Payable" ledger.</summary>
    private static bool ChallanHasLiveVoucher(Company company, Guid challanId)
    {
        foreach (var voucherId in company.VouchersLinkedToChallan(challanId))
            if (company.FindVoucher(voucherId) is { Cancelled: false })
                return true;
        return false;
    }
}

/// <summary>One section's row in the <see cref="ChallanReconciliation"/> (Phase 7 slice 3).</summary>
/// <param name="Section">The income-tax section / major head (e.g. "194J(b)").</param>
/// <param name="Deducted">TDS withheld under this section in the window.</param>
/// <param name="Deposited">TDS deposited (challans) under this section in the window.</param>
/// <param name="Remaining">Deducted − Deposited: positive ⇒ undeposited; negative ⇒ over-deposited/orphan.</param>
public sealed record SectionReconciliation(string Section, Money Deducted, Money Deposited, Money Remaining)
{
    /// <summary>True iff the deposit exactly discharges the deduction (remaining 0).</summary>
    public bool IsMatched => Remaining.Amount == 0m;

    /// <summary>True iff deducted &gt; deposited — TDS withheld but not (fully) deposited (an outstanding liability).</summary>
    public bool IsUnderpaid => Remaining.Amount > 0m;

    /// <summary>True iff deposited &gt; deducted — a challan with no matching deduction (an orphan/over-deposit).</summary>
    public bool IsOverpaid => Remaining.Amount < 0m;
}
