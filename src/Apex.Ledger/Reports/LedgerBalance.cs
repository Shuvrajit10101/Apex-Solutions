using Apex.Ledger.Domain;

namespace Apex.Ledger.Reports;

/// <summary>
/// The shared closing-balance primitive (design §7.1). Debit is positive, Credit is
/// negative internally; the external shape is a (side, magnitude) pair matching the
/// fixtures' <c>{side, amount}</c>.
/// </summary>
public readonly record struct LedgerBalance(DrCr Side, Money Amount)
{
    /// <summary>The zero balance, conventionally shown on the debit side.</summary>
    public static readonly LedgerBalance Zero = new(DrCr.Debit, Money.Zero);

    /// <summary>Builds a (side, magnitude) balance from a signed decimal (Dr = +, Cr = −).</summary>
    public static LedgerBalance FromSigned(decimal signed) =>
        signed >= 0m
            ? new LedgerBalance(DrCr.Debit, new Money(signed))
            : new LedgerBalance(DrCr.Credit, new Money(-signed));

    /// <summary>Signed value: +magnitude when debit, −magnitude when credit.</summary>
    public decimal Signed => Side == DrCr.Debit ? Amount.Amount : -Amount.Amount;
}

/// <summary>
/// Pure helpers over the posted voucher set. "Posted" for an as-of date excludes
/// <c>Cancelled</c>, <c>Optional</c>, and <c>PostDated</c>-and-not-yet-due vouchers, and
/// only counts vouchers dated ≤ the as-of date (design §7).
/// </summary>
public static class LedgerBalances
{
    /// <summary>
    /// The voucher base types that are <b>provisional</b> (catalog §7): they never touch the real books
    /// and are surfaced only through a scenario. A <b>Memorandum</b> is a non-affecting suspense entry;
    /// a <b>Reversing Journal</b> is a what-if accrual that reverses out after its "Applicable upto".
    /// </summary>
    public static bool IsProvisionalBaseType(VoucherBaseType baseType) =>
        baseType is VoucherBaseType.Memorandum or VoucherBaseType.ReversingJournal;

    /// <summary>
    /// Whether a voucher contributes to as-of balances at <paramref name="asOf"/> (real books). Excludes
    /// Cancelled, Optional (Ctrl+L), not-yet-due PostDated (Ctrl+T), and — when the base type is known —
    /// provisional types (Memorandum / Reversing Journal), which never affect the actual books.
    /// </summary>
    public static bool CountsAsOf(Voucher v, DateOnly asOf, VoucherBaseType? baseType = null)
    {
        if (v.Cancelled || v.Optional) return false;
        if (v.PostDated && v.Date > asOf) return false;
        if (baseType is { } bt && IsProvisionalBaseType(bt)) return false;
        return v.Date <= asOf;
    }

    /// <summary>
    /// Whether a voucher contributes to as-of balances at <paramref name="asOf"/> <b>under a scenario</b>
    /// (catalog §7). Rules, in order:
    /// <list type="bullet">
    /// <item>Cancelled vouchers never count.</item>
    /// <item>A voucher dated after <paramref name="asOf"/> never counts.</item>
    /// <item>A voucher whose type is <b>excluded</b> never counts (exclusion has precedence).</item>
    /// <item>A <b>Reversing Journal</b> whose <see cref="Voucher.ApplicableUpto"/> is before
    ///   <paramref name="asOf"/> has lapsed and no longer counts.</item>
    /// <item>A <b>provisional</b> voucher (Optional / not-yet-due PostDated / Memorandum / Reversing
    ///   Journal) counts only when its type is in the scenario's <b>included</b> set.</item>
    /// <item>A <b>real</b> (actual-books) voucher counts iff the scenario <see cref="Scenario.IncludeActuals"/>
    ///   is set (and its type is not excluded).</item>
    /// </list>
    /// </summary>
    public static bool CountsAsOf(Voucher v, DateOnly asOf, Scenario scenario, Company company)
    {
        if (v.Cancelled) return false;
        if (v.Date > asOf) return false;
        if (scenario.Excludes(v.TypeId)) return false;

        var baseType = company.FindVoucherType(v.TypeId)?.BaseType;

        // A Reversing Journal that has passed its "Applicable upto" date has reversed out.
        if (baseType == VoucherBaseType.ReversingJournal
            && v.ApplicableUpto is { } upto && asOf > upto)
            return false;

        var isProvisional =
            v.Optional
            || (v.PostDated && v.Date > asOf) // already excluded above, kept for clarity
            || (baseType is { } bt && IsProvisionalBaseType(bt));

        if (isProvisional)
            return scenario.Includes(v.TypeId);

        // A real voucher is only in the scenario column when actuals are included.
        return scenario.IncludeActuals;
    }

    /// <summary>Signed closing (§7.1): signed opening + Σ signed movements on/before <paramref name="asOf"/>.
    /// Provisional vouchers (Optional / Memorandum / Reversing Journal) are excluded from the real books.</summary>
    public static decimal SignedClosing(Company company, Domain.Ledger ledger, DateOnly asOf)
    {
        var signed = ledger.SignedOpening;
        foreach (var v in company.Vouchers)
        {
            if (!CountsAsOf(v, asOf, company.FindVoucherType(v.TypeId)?.BaseType)) continue;
            foreach (var line in v.Lines)
                if (line.LedgerId == ledger.Id)
                    signed += line.Signed;
        }
        return signed;
    }

    /// <summary>
    /// Signed closing (§7.1) <b>under a scenario</b> (catalog §7): signed opening + Σ signed movements
    /// from vouchers that count under the scenario on/before <paramref name="asOf"/>. When
    /// <paramref name="scenario"/> is <c>null</c> this is identical to the plain
    /// <see cref="SignedClosing(Company, Domain.Ledger, DateOnly)"/>, so report builders can pass a scenario
    /// through unconditionally without changing the no-scenario result.
    /// </summary>
    public static decimal SignedClosing(Company company, Domain.Ledger ledger, DateOnly asOf, Scenario? scenario)
    {
        if (scenario is null) return SignedClosing(company, ledger, asOf);

        var signed = ledger.SignedOpening * (scenario.IncludeActuals ? 1 : 0);
        foreach (var v in company.Vouchers)
        {
            if (!CountsAsOf(v, asOf, scenario, company)) continue;
            foreach (var line in v.Lines)
                if (line.LedgerId == ledger.Id)
                    signed += line.Signed;
        }
        return signed;
    }

    /// <summary>Closing balance as a (side, magnitude) pair.</summary>
    public static LedgerBalance Closing(Company company, Domain.Ledger ledger, DateOnly asOf)
        => LedgerBalance.FromSigned(SignedClosing(company, ledger, asOf));

    /// <summary>Closing balance as a (side, magnitude) pair, under an optional scenario.</summary>
    public static LedgerBalance Closing(Company company, Domain.Ledger ledger, DateOnly asOf, Scenario? scenario)
        => LedgerBalance.FromSigned(SignedClosing(company, ledger, asOf, scenario));

    /// <summary>
    /// Signed <b>nett transactions</b> for a ledger within <c>[from, to]</c> (§7 budgets, "On Nett
    /// Transactions"): Σ of signed movements from counted vouchers dated in the window — the opening
    /// balance is <b>not</b> included. Dr movements are positive, Cr negative.
    /// </summary>
    public static decimal SignedMovement(Company company, Domain.Ledger ledger, DateOnly from, DateOnly to)
    {
        var signed = 0m;
        foreach (var v in company.Vouchers)
        {
            // Cancelled/Optional/not-yet-due PostDated/provisional excluded + date ≤ to.
            if (!CountsAsOf(v, to, company.FindVoucherType(v.TypeId)?.BaseType)) continue;
            if (v.Date < from) continue;      // window lower bound
            foreach (var line in v.Lines)
                if (line.LedgerId == ledger.Id)
                    signed += line.Signed;
        }
        return signed;
    }
}
