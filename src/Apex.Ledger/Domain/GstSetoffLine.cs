namespace Apex.Ledger.Domain;

/// <summary>
/// One <b>Table 6.1</b> allocation row of a posted Rule-88A set-off (Phase 9 slice 7; RQ-21; ER-7). It records that
/// <see cref="AmountPaisa"/> of the <see cref="CreditHead"/> credit pool (or cash, when <see cref="IsCash"/>) was used
/// to discharge the <see cref="LiabilityHead"/> liability, keyed to the posted set-off Journal (<see cref="VoucherId"/>)
/// and the <see cref="Period"/> — the audit trail behind the electronic-credit-ledger utilisation.
/// <para>
/// The <b>CGST↔SGST cross-utilisation wall</b> (ER-7) and the <b>cess ring-fence</b> (ER-2) are enforced <b>twice</b>:
/// here in the fail-fast ctor AND by a DB <c>CHECK</c> constraint on <c>gst_setoff_lines</c> — a cross-head or
/// cess↔non-cess allocation is <b>impossible to persist</b>. A pure value object with identity.
/// </para>
/// </summary>
public sealed class GstSetoffLine
{
    /// <summary>Stable surrogate key.</summary>
    public Guid Id { get; }

    /// <summary>The posted set-off Journal this allocation belongs to.</summary>
    public Guid VoucherId { get; }

    /// <summary>The set-off period ("yyyy-MM").</summary>
    public string Period { get; }

    /// <summary>The credit pool used (or — when <see cref="IsCash"/> — the head paid in cash, = <see cref="LiabilityHead"/>).</summary>
    public GstTaxHead CreditHead { get; }

    /// <summary>The liability head discharged.</summary>
    public GstTaxHead LiabilityHead { get; }

    /// <summary>True ⇒ this allocation was paid from the electronic cash ledger (not a credit pool).</summary>
    public bool IsCash { get; }

    /// <summary>The amount allocated, in paisa (&gt; 0).</summary>
    public long AmountPaisa { get; }

    public GstSetoffLine(
        Guid id, Guid voucherId, string period, GstTaxHead creditHead, GstTaxHead liabilityHead, bool isCash,
        long amountPaisa)
    {
        if (voucherId == Guid.Empty)
            throw new ArgumentException("Set-off line must reference its set-off voucher.", nameof(voucherId));
        if (string.IsNullOrWhiteSpace(period))
            throw new ArgumentException("Set-off line period is required.", nameof(period));
        if (amountPaisa <= 0)
            throw new ArgumentException("Set-off line amount must be > 0 paisa.", nameof(amountPaisa));

        // ER-7 CGST↔SGST cross-utilisation wall + ER-2 cess ring-fence — enforced here AND by the DB CHECK. A cash line
        // carries CreditHead = LiabilityHead by convention, so both walls pass trivially for it.
        if (creditHead == GstTaxHead.Central && liabilityHead == GstTaxHead.State)
            throw new InvalidOperationException("CGST credit can never discharge an SGST liability (ER-7 cross-head wall).");
        if (creditHead == GstTaxHead.State && liabilityHead == GstTaxHead.Central)
            throw new InvalidOperationException("SGST credit can never discharge a CGST liability (ER-7 cross-head wall).");
        if ((creditHead == GstTaxHead.Cess) != (liabilityHead == GstTaxHead.Cess))
            throw new InvalidOperationException("Cess credit is ring-fenced: it discharges only cess, and only cess discharges cess (ER-2).");

        Id = id;
        VoucherId = voucherId;
        Period = period.Trim();
        CreditHead = creditHead;
        LiabilityHead = liabilityHead;
        IsCash = isCash;
        AmountPaisa = amountPaisa;
    }

    /// <summary>Rehydrates a persisted / imported set-off line verbatim (the fail-fast ctor re-checks the walls).</summary>
    public static GstSetoffLine Rehydrate(
        Guid id, Guid voucherId, string period, GstTaxHead creditHead, GstTaxHead liabilityHead, bool isCash,
        long amountPaisa) =>
        new(id, voucherId, period, creditHead, liabilityHead, isCash, amountPaisa);
}
