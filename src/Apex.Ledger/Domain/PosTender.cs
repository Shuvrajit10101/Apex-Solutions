namespace Apex.Ledger.Domain;

/// <summary>
/// One POS payment tender on a POS Sales voucher (catalog §11; Phase 6 slice 7 RQ-39/RQ-40; DP-6). A POS bill's
/// single customer debit is replaced by a split of tender debits — this record is the per-tender metadata the
/// balanced accounting <see cref="EntryLine"/> cannot carry (tender kind, cash tendered/change, card/bank/cheque
/// references). It is <b>voucher-level</b> metadata paired 1:1 with the tender debit lines; the credit side
/// (Cr Sales + Cr Output CGST/SGST/IGST) is byte-identical to a normal sale, so GST reuses the Phase-4 engine
/// unchanged.
/// </summary>
/// <param name="Type">The tender kind (Gift / Card / Cheque / Cash).</param>
/// <param name="LedgerId">The ledger this tender debits — validated to the required group (Gift → Sundry Debtors,
/// Card/Cheque → Bank, Cash → Cash-in-Hand) by <see cref="Services.PosTenderService.EnsureGrouping"/>.</param>
/// <param name="Amount">The <b>posted</b> payable share for this tender, paisa-exact. For Cash this is the
/// <b>residual</b> (bill total − the non-cash tenders), NOT the tendered amount — the books never see the change.
/// Σ over all tenders equals the bill total.</param>
/// <param name="Tendered">Cash only: the cash handed over (≥ <see cref="Amount"/>); <c>null</c> for non-cash tenders.</param>
/// <param name="Change">Cash only: the informational change = <see cref="Tendered"/> − <see cref="Amount"/> (≥ 0);
/// it produces NO ledger line. <c>null</c> for non-cash tenders.</param>
/// <param name="CardNo">Card only: the card number reference; <c>null</c> otherwise.</param>
/// <param name="BankName">Cheque/DD only: the drawee bank name; <c>null</c> otherwise.</param>
/// <param name="ChequeNo">Cheque/DD only: the cheque/DD number; <c>null</c> otherwise.</param>
public sealed record PosTender(
    PosTenderType Type,
    Guid LedgerId,
    Money Amount,
    Money? Tendered = null,
    Money? Change = null,
    string? CardNo = null,
    string? BankName = null,
    string? ChequeNo = null)
{
    // The initialisers are what carry the PRIMARY-CONSTRUCTOR parameters into the backing fields. Declaring a
    // property whose name matches a positional parameter suppresses the compiler's own assignment — without these
    // the parameter is silently dropped and every tender would post as ZERO (warning CS8907 is the only thing
    // standing between that and a shipped wrong figure). The `init` accessors below then re-apply the same check
    // so a `with` expression cannot slip past it either.
    private readonly Money _amount = Exact(Amount, nameof(Amount));
    private readonly Money? _tendered = ExactOrNull(Tendered, nameof(Tendered));
    private readonly Money? _change = ExactOrNull(Change, nameof(Change));

    /// <summary>
    /// All THREE money members persist through <c>Paisa.FromMoney</c> — <c>amount_paisa</c>,
    /// <c>tendered_paisa</c> and <c>change_paisa</c> in <c>InsertPosTenders</c> — and that conversion throws on a
    /// sub-paisa figure. The record's own doc already promised "paisa-exact"; until now nothing enforced it, and
    /// <c>PosBillingViewModel.TryBuildTenders</c> builds these from a TYPED tender amount whose only gate is
    /// "Σ tenders == bill total" — which a sub-paisa card tender and its (equally sub-paisa) cash residual satisfy
    /// exactly. The bill then Posted and the Save threw, out of an Accept whose catch does not roll back.
    ///
    /// <para>Validated in the <c>init</c> accessor rather than a field initialiser so a <c>with</c> expression is
    /// held to the invariant too. Both non-screen callers — <c>ImportPlan</c> and the SQLite read path — build
    /// from INTEGER paisa and cannot trip it.</para>
    /// </summary>
    public Money Amount
    {
        get => _amount;
        init => _amount = Exact(value, nameof(Amount));
    }

    /// <inheritdoc cref="Amount"/>
    public Money? Tendered
    {
        get => _tendered;
        init => _tendered = ExactOrNull(value, nameof(Tendered));
    }

    /// <inheritdoc cref="Amount"/>
    public Money? Change
    {
        get => _change;
        init => _change = ExactOrNull(value, nameof(Change));
    }

    /// <summary>
    /// FitsPaisaStore, not IsPaisaExact — "storable" is magnitude AND exactness, and the exactness half alone
    /// THROWS <see cref="OverflowException"/> on a big enough figure instead of refusing it (the predicate scales
    /// by a hundred; the store's conversion then narrows to <c>long</c>). FitsPaisaStore owns that branch order,
    /// magnitude first, and is the reason this is a real backstop rather than half of one.
    /// </summary>
    private static Money Exact(Money value, string member) =>
        PaisaConversion.FitsPaisaStore(value.Amount)
            ? value
            : throw new InvalidOperationException(
                $"POS tender {member} {value.Amount} cannot be stored as integer paisa: it must be paisa-exact "
              + $"(2 decimal places) and no larger than {PaisaConversion.MaxStorableRupees}.");

    private static Money? ExactOrNull(Money? value, string member) =>
        value is { } v ? Exact(v, member) : null;
}
