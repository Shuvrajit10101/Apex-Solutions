namespace Apex.Ledger.Domain;

/// <summary>
/// The vendor's <b>Rounding Method</b> on a <see cref="VoucherClassAdditionalEntry"/> (census 2.6; schema v62) —
/// the four options the vendor lists, paired with a <b>Rounding Limit</b> (the multiple the value snaps to).
/// Stored as the enum ordinal, <c>0 = NotApplicable</c>, so "column absent" and "no rounding" coincide.
/// </summary>
/// <remarks>
/// <para><b>R7 — ATTESTED.</b> <c>help.tallysolutions.com/round-off-invoice-and-ledger-values/</c> lists exactly
/// these four and no others: <i>"Not Applicable"</i> (the value is left unchanged), <i>"Downward Rounding"</i>
/// (to the nearest LOWER multiple of the rounding limit), <i>"Normal Rounding"</i> (to the nearest lower multiple
/// when the fraction is below half, to the higher when it is half or more) and <i>"Upward Rounding"</i> (to the
/// nearest HIGHER multiple). The same page defines the <b>Rounding Limit</b> as the numeric value whose nearest
/// multiple is taken — <i>a limit of 1 rounds to whole rupees, a limit of 5 to multiples of 5</i>.</para>
///
/// <para>🔴 <b>WHY THIS IS A SECOND ENUM AND NOT <see cref="PayHeadRoundingMethod"/>.</b> The vendor offers the
/// same four strings on two unrelated screens — the pay-head master and the voucher class — and the two are
/// configured, stored and evaluated by two engines that share nothing else. Making payroll's enum the type of a
/// voucher-class column would couple the salary engine to voucher entry for the sake of four members, so the
/// member set is duplicated and the <b>arithmetic is not</b>: <see cref="VoucherClassRounding.Apply"/> is the one
/// implementation this row uses, and <c>VoucherClassRoundingAgreesWithPayrollTests</c> pins it against
/// <c>PayrollComputationService</c>'s copy over a table of values so the two can never silently diverge.</para>
/// </remarks>
public enum VoucherClassRoundingMethod
{
    /// <summary>Vendor <i>"Not Applicable"</i> — the computed value is used to the paisa, unrounded. A class whose
    /// method is this must carry a <b>zero</b> rounding limit (the vendor's own instruction is to leave the
    /// Rounding Limit blank); <see cref="Apex.Ledger.Services.VoucherClassService"/> enforces the pairing.</summary>
    NotApplicable = 0,

    /// <summary>Vendor <i>"Normal Rounding"</i> — to the NEAREST multiple of the rounding limit, a half going away
    /// from zero.</summary>
    Normal = 1,

    /// <summary>Vendor <i>"Upward Rounding"</i> — always to the next HIGHER multiple of the rounding limit.</summary>
    Upward = 2,

    /// <summary>Vendor <i>"Downward Rounding"</i> — always to the next LOWER multiple of the rounding limit.</summary>
    Downward = 3,
}
