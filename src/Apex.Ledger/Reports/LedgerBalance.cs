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
    /// <summary>Whether a voucher contributes to as-of balances at <paramref name="asOf"/>.</summary>
    public static bool CountsAsOf(Voucher v, DateOnly asOf)
    {
        if (v.Cancelled || v.Optional) return false;
        if (v.PostDated && v.Date > asOf) return false;
        return v.Date <= asOf;
    }

    /// <summary>Signed closing (§7.1): signed opening + Σ signed movements on/before <paramref name="asOf"/>.</summary>
    public static decimal SignedClosing(Company company, Domain.Ledger ledger, DateOnly asOf)
    {
        var signed = ledger.SignedOpening;
        foreach (var v in company.Vouchers)
        {
            if (!CountsAsOf(v, asOf)) continue;
            foreach (var line in v.Lines)
                if (line.LedgerId == ledger.Id)
                    signed += line.Signed;
        }
        return signed;
    }

    /// <summary>Closing balance as a (side, magnitude) pair.</summary>
    public static LedgerBalance Closing(Company company, Domain.Ledger ledger, DateOnly asOf)
        => LedgerBalance.FromSigned(SignedClosing(company, ledger, asOf));
}
