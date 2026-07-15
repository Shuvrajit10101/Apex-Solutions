namespace Apex.Ledger.Domain;

/// <summary>
/// A <b>DRC-03</b> voluntary / self-ascertained GST payment (Phase 9 slice 7; RQ-22; A14-CONFIRMED §11.3, Rule
/// 142(2)/(3)). It is the landing instrument for a payment that cannot be netted in a return — a voluntary tax
/// deposit, a Rule 37/37A ITC reversal paid in cash, or a 2B/IMS mismatch. Payable from cash and/or credit
/// <b>subject to the tax-only credit rule</b> (§11.2 — credit settles only the tax component; interest / penalty /
/// fee / late-fee are cash-only). Per-head amounts are integer paisa; <see cref="InterestPaisa"/> is captured
/// flag/field-only (DP-34; 18% p.a. if surfaced — §11.6), never auto-computed.
/// <para>
/// <see cref="Drc03aDemandRef"/> links this payment to a <b>confirmed demand</b> (DRC-03A, Nov-2024, Notn 12/2024-CT).
/// <see cref="VoucherId"/> is the posted payment voucher (or <c>null</c> for a pure credit-reduction disclosure). A
/// pure value object with identity — framework-, DB- and clock-free.
/// </para>
/// </summary>
public sealed class GstDrc03
{
    /// <summary>Stable surrogate key.</summary>
    public Guid Id { get; }

    /// <summary>The DRC-03 ARN / reference the portal returns, or <c>null</c> for an offline draft.</summary>
    public string? Drc03Ref { get; }

    /// <summary>The rule / section / cause text (Rule 37/37A / §17(5) / 2B-IMS mismatch / voluntary) — required.</summary>
    public string Cause { get; }

    /// <summary>The period the payment relates to ("yyyy-MM" or an FY) — required.</summary>
    public string Period { get; }

    /// <summary>The CGST component discharged, in paisa (≥ 0).</summary>
    public long CgstPaisa { get; }

    /// <summary>The SGST/UTGST component discharged, in paisa (≥ 0).</summary>
    public long SgstPaisa { get; }

    /// <summary>The IGST component discharged, in paisa (≥ 0).</summary>
    public long IgstPaisa { get; }

    /// <summary>The Compensation-Cess component discharged, in paisa (≥ 0; ring-fenced, ER-2).</summary>
    public long CessPaisa { get; }

    /// <summary>The §50 interest component, in paisa (flag/field-only, DP-34; cash-only, never credit-settled).</summary>
    public long InterestPaisa { get; }

    /// <summary>The DRC-03A confirmed-demand reference (Nov-2024, Notn 12/2024-CT), or <c>null</c>.</summary>
    public string? Drc03aDemandRef { get; }

    /// <summary>The posted payment voucher, or <c>null</c> for a pure credit-reduction disclosure.</summary>
    public Guid? VoucherId { get; }

    /// <summary>When the record was created (caller-supplied, never a clock read).</summary>
    public DateTimeOffset CreatedAt { get; }

    public GstDrc03(
        Guid id, string? drc03Ref, string cause, string period, long cgstPaisa, long sgstPaisa, long igstPaisa,
        long cessPaisa, long interestPaisa, string? drc03aDemandRef, Guid? voucherId, DateTimeOffset createdAt)
    {
        if (string.IsNullOrWhiteSpace(cause))
            throw new ArgumentException("DRC-03 cause is required.", nameof(cause));
        if (string.IsNullOrWhiteSpace(period))
            throw new ArgumentException("DRC-03 period is required.", nameof(period));
        if (cgstPaisa < 0 || sgstPaisa < 0 || igstPaisa < 0 || cessPaisa < 0 || interestPaisa < 0)
            throw new ArgumentException("DRC-03 amounts must be ≥ 0 paisa.");

        Id = id;
        Drc03Ref = string.IsNullOrWhiteSpace(drc03Ref) ? null : drc03Ref.Trim();
        Cause = cause.Trim();
        Period = period.Trim();
        CgstPaisa = cgstPaisa;
        SgstPaisa = sgstPaisa;
        IgstPaisa = igstPaisa;
        CessPaisa = cessPaisa;
        InterestPaisa = interestPaisa;
        Drc03aDemandRef = string.IsNullOrWhiteSpace(drc03aDemandRef) ? null : drc03aDemandRef.Trim();
        VoucherId = voucherId;
        CreatedAt = createdAt;
    }

    /// <summary>The total tax discharged across the four GST heads (excludes interest), in paisa.</summary>
    public long TotalTaxPaisa => CgstPaisa + SgstPaisa + IgstPaisa + CessPaisa;

    /// <summary>Rehydrates a persisted / imported DRC-03 verbatim (the fail-fast ctor guards a malformed record).</summary>
    public static GstDrc03 Rehydrate(
        Guid id, string? drc03Ref, string cause, string period, long cgstPaisa, long sgstPaisa, long igstPaisa,
        long cessPaisa, long interestPaisa, string? drc03aDemandRef, Guid? voucherId, DateTimeOffset createdAt) =>
        new(id, drc03Ref, cause, period, cgstPaisa, sgstPaisa, igstPaisa, cessPaisa, interestPaisa, drc03aDemandRef,
            voucherId, createdAt);
}
