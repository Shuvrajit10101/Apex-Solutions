using System;

namespace Apex.Ledger;

/// <summary>
/// <b>The ONE home for pro-rata apportionment of a posted group total across that group's legs</b> (drift lock
/// D1). A rate group's posted CGST/SGST/IGST/cess is split across the group's lines or service legs in proportion
/// to each leg's value; the caller gives the group's LAST leg the arithmetic remainder so the split foots to the
/// posted total exactly, and uses this method for every other leg.
///
/// <para><b>The divergence this replaces — a PURE DE-DUPLICATION, with no caller-visible change.</b> Three
/// private copies existed — <c>Gstr1.Apportion</c> (decimal rupees), <c>EInvoiceJson.Apportion</c> and
/// <c>EWayBillJson.Apportion</c> (integer paisa). The two Io copies guarded <c>totalValue == 0</c> and returned 0;
/// the GSTR-1 copy had no guard of its own.
///
/// <para><b>That missing guard was NOT a live defect, and this doc previously claimed it was.</b> Both GSTR-1 call
/// sites — the stock/HSN path and the service-SAC path — already <c>continue</c> on <c>groupValue == 0m</c>
/// BEFORE entering the apportionment loop, and those six calls are the only way <c>Apportion</c> is reached. A
/// zero denominator was therefore unreachable and no <see cref="DivideByZeroException"/> could ever be raised
/// while building a filed return. Unifying the three copies changed no caller's answer at all.</para>
///
/// <para><b>So treat the caller-side guards as LOAD-BEARING, not as redundant leftovers.</b> They SKIP the group
/// (no HSN row is emitted for it); the <c>== 0</c> here is defence in depth, not the operative behaviour. Deleting
/// a caller guard on the belief that this shared rule now covers it would NOT produce zeros — it would send every
/// non-final leg to 0 while the loop's remainder branch dumps the group's entire posted tax onto the last leg, a
/// silently wrong filed return. <c>Gstr1ZeroValueRateGroupTests</c> pins that.</para>
///
/// <para><b>Why the guard is <c>== 0</c> and not <c>&lt;= 0</c>.</b> A negative <paramref name="totalValue"/> is
/// still a meaningful denominator: on a credit note both the leg value and the group value carry the same sign,
/// so their ratio is the correct positive share. Only an exactly-zero denominator has no answer. Widening the
/// guard to <c>&lt;= 0</c> would silently zero the tax split on every negative-value document, which is a worse
/// defect than the one being fixed. Call sites that additionally test <c>totalValue &gt; 0</c> before calling
/// keep their own guard — narrowing those is a separate behavioural change and is deliberately not made here.
///
/// <para><b>Rounding.</b> Away-from-zero at the target scale, matching every one of the three replaced copies and
/// the app-wide money convention (<see cref="Money.RoundToPaisa"/>).</para>
/// </summary>
public static class ProRata
{
    /// <summary>
    /// A leg's share of a decimal-rupee group total, rounded to 2 dp away from zero. Returns 0 when the group
    /// value is exactly zero (there is no share to take of nothing).
    /// </summary>
    public static decimal Rupees(decimal total, decimal value, decimal totalValue) =>
        totalValue == 0m ? 0m : Math.Round(total * value / totalValue, 2, MidpointRounding.AwayFromZero);

    /// <summary>
    /// A leg's share of an integer-paisa group total, rounded to whole paisa away from zero. Returns 0 when the
    /// group value is exactly zero. The multiply is widened to <see cref="decimal"/> so a large paisa total
    /// cannot overflow before the divide.
    /// </summary>
    public static long Paisa(long total, long value, long totalValue) =>
        totalValue == 0 ? 0 : (long)Math.Round((decimal)total * value / totalValue, MidpointRounding.AwayFromZero);
}
