using Apex.Ledger.Domain;

namespace Apex.Ledger.Services;

/// <summary>
/// The ONE implementation of the vendor's <b>Rounding Method</b> + <b>Rounding Limit</b> arithmetic for census row
/// 2.6 (schema v62).
/// </summary>
/// <remarks>
/// <para><b>R7 — ATTESTED.</b> <c>help.tallysolutions.com/round-off-invoice-and-ledger-values/</c>:
/// <i>"Downward Rounding"</i> takes <i>the nearest lower multiple of the rounding limit</i>, <i>"Upward
/// Rounding"</i> <i>the nearest higher multiple</i>, and <i>"Normal Rounding"</i> rounds to the nearest lower
/// when the fraction is below half and up when it is half or more; <i>"Not Applicable"</i> leaves the value
/// unchanged. The rounding limit is the value whose nearest multiple is taken.</para>
///
/// <para>🔴 <b>WHY THE ARITHMETIC IS FACTORED OUT OF THE ENGINE.</b> This repository already carries one copy of
/// this arithmetic, in <c>PayrollComputationService.ApplyRounding</c>, for the pay-head master's identical
/// four-option field. A second free-hand copy inside the voucher-class engine is exactly how the two drift by a
/// rupee at a boundary that no test looks at, so the voucher-class side has ONE function and
/// <c>VoucherClassRoundingTests</c> pins it value-for-value against the payroll copy's behaviour.</para>
///
/// <para>🔴 <b>NEGATIVE AMOUNTS ARE REAL HERE AND ARE NOT FOLDED TO ZERO.</b> A total-amount-rounding leg is the
/// negative of what a downward rounding removed. <see cref="Math.Ceiling(decimal)"/> and
/// <see cref="Math.Floor(decimal)"/> are used, NOT a magnitude-then-sign construction:
/// <c>Floor(-100.4) == -101</c>, which is the next LOWER multiple and therefore what "Downward Rounding" means
/// on a negative value. A <c>Truncate</c>-based version would return -100 and silently round a negative figure
/// the wrong way.</para>
/// </remarks>
public static class VoucherClassRounding
{
    /// <summary>
    /// Applies <paramref name="method"/> at <paramref name="limit"/> to <paramref name="raw"/>, returning a
    /// paisa-exact <see cref="Money"/>.
    ///
    /// <para><see cref="VoucherClassRoundingMethod.NotApplicable"/> snaps to the paisa (away from zero), which is
    /// what "unchanged" has to mean for a value that must survive the INTEGER-paisa store. A non-positive
    /// <paramref name="limit"/> on any other method is treated as "no rounding" rather than dividing by zero —
    /// <see cref="VoucherClassService"/> rejects that pairing at the master-save boundary, so reaching here with
    /// one means a book was written by something other than this product, and a silent pass-through is safer than
    /// a <see cref="DivideByZeroException"/> in the middle of posting a voucher.</para>
    /// </summary>
    public static Money Apply(decimal raw, VoucherClassRoundingMethod method, Money limit)
    {
        if (method == VoucherClassRoundingMethod.NotApplicable)
            return new Money(Math.Round(raw, 2, MidpointRounding.AwayFromZero));

        var step = limit.Amount;
        if (step <= 0m)
            return new Money(Math.Round(raw, 2, MidpointRounding.AwayFromZero));

        return method switch
        {
            VoucherClassRoundingMethod.Normal =>
                new Money(Math.Round(raw / step, 0, MidpointRounding.AwayFromZero) * step),
            VoucherClassRoundingMethod.Upward => new Money(Math.Ceiling(raw / step) * step),
            VoucherClassRoundingMethod.Downward => new Money(Math.Floor(raw / step) * step),
            _ => new Money(Math.Round(raw, 2, MidpointRounding.AwayFromZero)),
        };
    }

    /// <summary>Convenience overload for a <see cref="Money"/> input.</summary>
    public static Money Apply(Money raw, VoucherClassRoundingMethod method, Money limit) =>
        Apply(raw.Amount, method, limit);
}
