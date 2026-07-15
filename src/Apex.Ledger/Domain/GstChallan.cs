namespace Apex.Ledger.Domain;

/// <summary>
/// A <b>PMT-06 GST deposit challan</b> — the record of money paid into the <b>electronic cash ledger</b> (Phase 9
/// slice 7; RQ-22; A14-CONFIRMED §11.2/§11.3). It captures the CPIN → CIN/BRN lifecycle and the (major head, minor
/// head) cell the deposit lands in: <see cref="Cpin"/> (14-digit, 15-day validity) is generated on challan creation;
/// the <b>cash ledger is credited ONLY on CIN</b> (the bank-credit reference), so the two-step deposit-then-utilise
/// model materialises a CIN-gated, minor-head-split cash balance. <see cref="MajorHead"/> is the GST head
/// (IGST/CGST/SGST-UTGST/Cess) and <see cref="MinorHead"/> is Tax / Interest / Penalty / Fee / Other — a deposit is
/// drawable only within its own (major, minor) cell (cross-cell movement needs PMT-09, out of scope). PMT-06 is also
/// the QRMP M1 / M2 monthly challan.
/// <para>
/// A challan maps to exactly one deposit voucher (<see cref="VoucherId"/> — Dr Electronic Cash Ledger / Cr Bank), so
/// no M:N link table is needed (unlike TDS). §50 interest is captured flag-only (<see cref="InterestFlag"/>, DP-34;
/// 18% p.a. if ever surfaced — §11.6), never auto-computed. Framework-, DB- and clock-free — a pure value object with
/// identity.
/// </para>
/// </summary>
public sealed class GstChallan
{
    /// <summary>Stable surrogate key.</summary>
    public Guid Id { get; }

    /// <summary>The CPIN — the 14-digit Common Portal Identification Number generated on challan creation (required).</summary>
    public string Cpin { get; }

    /// <summary>The CIN (Challan Identification Number) — the bank-credit reference; <c>null</c> until the bank credits.</summary>
    public string? Cin { get; }

    /// <summary>The BRN (Bank Reference Number) recorded on payment; <c>null</c> until paid.</summary>
    public string? Brn { get; }

    /// <summary>The date the deposit was made into the cash ledger.</summary>
    public DateOnly DepositDate { get; }

    /// <summary>The MAJOR head the deposit lands in (IGST/CGST/SGST-UTGST/Cess).</summary>
    public GstTaxHead MajorHead { get; }

    /// <summary>The MINOR head the deposit lands in (Tax / Interest / Penalty / Fee / Other).</summary>
    public GstMinorHead MinorHead { get; }

    /// <summary>The amount deposited (paisa-exact, &gt; 0).</summary>
    public Money Amount { get; }

    /// <summary>The deposit voucher that booked this challan (Dr Electronic Cash Ledger / Cr Bank).</summary>
    public Guid VoucherId { get; }

    /// <summary>§50 interest flag-only (DP-34) — set when the deposit carries an interest component; never auto-computed.</summary>
    public bool InterestFlag { get; }

    public GstChallan(
        Guid id, string cpin, string? cin, string? brn, DateOnly depositDate, GstTaxHead majorHead,
        GstMinorHead minorHead, Money amount, Guid voucherId, bool interestFlag = false)
    {
        if (string.IsNullOrWhiteSpace(cpin))
            throw new ArgumentException("Challan CPIN is required.", nameof(cpin));
        if (amount.Amount <= 0m)
            throw new ArgumentException("Challan amount must be > 0.", nameof(amount));
        if (!amount.IsPaisaExact)
            throw new InvalidOperationException($"Challan amount {amount} must be paisa-exact.");
        if (voucherId == Guid.Empty)
            throw new ArgumentException("Challan must reference its deposit voucher.", nameof(voucherId));

        Id = id;
        Cpin = cpin.Trim();
        Cin = string.IsNullOrWhiteSpace(cin) ? null : cin.Trim();
        Brn = string.IsNullOrWhiteSpace(brn) ? null : brn.Trim();
        DepositDate = depositDate;
        MajorHead = majorHead;
        MinorHead = minorHead;
        Amount = amount;
        VoucherId = voucherId;
        InterestFlag = interestFlag;
    }

    /// <summary>Rehydrates a persisted / imported challan verbatim (the fail-fast ctor guards a malformed record ⇒
    /// all-or-nothing on import, RQ-23).</summary>
    public static GstChallan Rehydrate(
        Guid id, string cpin, string? cin, string? brn, DateOnly depositDate, GstTaxHead majorHead,
        GstMinorHead minorHead, Money amount, Guid voucherId, bool interestFlag) =>
        new(id, cpin, cin, brn, depositDate, majorHead, minorHead, amount, voucherId, interestFlag);
}
