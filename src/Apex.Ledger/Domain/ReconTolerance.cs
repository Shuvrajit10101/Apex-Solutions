namespace Apex.Ledger.Domain;

/// <summary>
/// The <b>matching tolerance</b> for GSTR-2B reconciliation (Phase 9 slice 6; DP-13): a paisa slack on the taxable/tax
/// comparison and a ± day window on the document date. Default <see cref="Exact"/> (zero paisa, zero days) ⇒ an
/// exact-match reconciliation ⇒ a company that never touches 2B is byte-identical (ER-13). It is a <b>matching</b>
/// parameter only — it never changes a posted figure (ER-14).
/// </summary>
/// <param name="ValueTolerancePaisa">The paisa slack on the taxable + total-tax comparison (≥ 0).</param>
/// <param name="DateWindowDays">The ± day window on the document-date comparison (≥ 0).</param>
public readonly record struct ReconTolerance(long ValueTolerancePaisa, int DateWindowDays)
{
    /// <summary>Exact matching: zero paisa slack, same-day only (the byte-identical default, ER-13).</summary>
    public static readonly ReconTolerance Exact = new(0L, 0);
}
