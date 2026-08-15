using Apex.Ledger;
using Apex.Desktop.Services;

namespace Apex.Desktop.ViewModels;

/// <summary>
/// The FRONT-LINE test every typed voucher-entry amount owes the INTEGER-paisa store, and the one place its two
/// refusal messages are worded (W0-13 PART 2, slice S2a).
///
/// <para><b>Why a shared home and not another private copy.</b> The voucher-entry family — the plain-grid line
/// amount, the bill-wise allocation row, the cost allocation row and the four POS tender rows — is FOUR screens
/// applying one rule. <c>TaxDeclarationViewModel.TryMoney</c>, <c>GstConfigViewModel.TryStatutoryRupees</c> and
/// <c>PostItcReversalViewModel</c> each grew their own copy, and the three already disagree on wording and on
/// whether a magnitude ceiling exists at all. A fifth, sixth, seventh and eighth copy would be the divergence
/// this campaign exists to remove, so the four new sites share this one.</para>
///
/// <para><b>The branch order is load-bearing and matches the shipped <c>TryStatutoryRupees</c>:</b> magnitude
/// first, exactness second. The sub-paisa predicate scales by a hundred and the store's conversion then narrows
/// to <c>long</c>; a 17-digit figure passes parse, sign AND the paisa test and only then raises
/// <c>OverflowException</c> deep inside the store — an <c>ArithmeticException</c> that the narrow
/// <c>when (ex is InvalidOperationException or ArgumentException)</c> filters on the invoice and POS Accept paths
/// do NOT catch. Testing magnitude first turns that into a message. See
/// <see cref="PaisaConversion.FitsPaisaStore"/>, which owns the predicate itself.</para>
/// </summary>
internal static class StorableAmount
{
    /// <summary>
    /// True when <paramref name="value"/> can be persisted at all — the predicate the row/line
    /// <c>IsComplete</c> gates fold in, so a value that cannot be stored is never built into a domain object.
    /// </summary>
    public static bool IsStorable(decimal value) => PaisaConversion.FitsPaisaStore(value);

    /// <summary>
    /// The field-level refusal for <paramref name="value"/>, or <c>null</c> when it is storable.
    /// <paramref name="text"/> is echoed verbatim so the operator sees what they typed, and
    /// <paramref name="fieldLabel"/> names the field the way the on-screen caption does.
    /// The sub-paisa wording is the convention set by <c>TaxDeclarationViewModel</c>.
    /// </summary>
    public static string? ErrorFor(decimal value, string? text, string fieldLabel)
    {
        // MAGNITUDE FIRST — see the type doc. The branch below would THROW on the input this one rejects.
        if (value < -PaisaConversion.MaxStorableRupees || value > PaisaConversion.MaxStorableRupees)
            return $"'{text}' is too large for {fieldLabel} (the most that can be stored is "
                 + $"₹{IndianFormat.AmountAlways(PaisaConversion.MaxStorableRupees)}).";

        if (!new Money(value).IsPaisaExact)
            return $"'{text}' is finer than a paisa for {fieldLabel} (enter at most two decimal places).";

        return null;
    }
}
