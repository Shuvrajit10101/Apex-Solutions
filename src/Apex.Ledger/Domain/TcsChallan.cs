namespace Apex.Ledger.Domain;

/// <summary>
/// A <b>TCS deposit challan</b> (ITNS-281) — the government receipt for a TCS payment made into the bank
/// (Phase 7 slice 6; catalog §13). It mirrors <see cref="TdsChallan"/> exactly, but for the <i>collector</i>'s
/// monthly deposit of collected TCS: it captures the ITNS-281 fields the TCS Challan Reconciliation and Form 27EQ
/// challan block need — the challan serial <see cref="ChallanNo"/>, the collecting-branch <see cref="BsrCode"/>
/// (7-digit BSR), the <see cref="DepositDate"/>, the <see cref="Amount"/> deposited, the §206C Form-27EQ
/// <see cref="CollectionCode"/> (e.g. "6CE" scrap — the major head the deposit is for) and the
/// <see cref="MinorHead"/> (ITNS-281 minor head — "200" TCS payable by the taxpayer / "400" TCS on regular
/// assessment).
/// <para>
/// A challan is <b>linked</b> to the Stat-Payment Payment voucher that booked the deposit (Dr "TCS Payable" / Cr
/// Bank) through the company's TCS <c>ChallanVoucherLink</c> set, mirroring how a deposit voucher pairs to a challan
/// in Tally. Unlike the withholding TDS deposit, TCS was collected <i>additively</i> on the sale; the deposit
/// discharges the collected liability the same way. The challan itself is framework-, DB- and clock-free; it is a
/// pure value object with identity.
/// </para>
/// </summary>
public sealed class TcsChallan
{
    /// <summary>Stable surrogate key.</summary>
    public Guid Id { get; }

    /// <summary>The challan serial / CIN tender number (required).</summary>
    public string ChallanNo { get; }

    /// <summary>The 7-digit BSR code of the collecting bank branch (required).</summary>
    public string BsrCode { get; }

    /// <summary>The date the tax was deposited into the bank.</summary>
    public DateOnly DepositDate { get; }

    /// <summary>The amount deposited (paisa-exact; a whole-rupee TCS deposit in practice).</summary>
    public Money Amount { get; }

    /// <summary>The §206C Form-27EQ collection code / major head the deposit is for (e.g. "6CE"); required.</summary>
    public string CollectionCode { get; }

    /// <summary>The ITNS-281 minor head ("200" = TCS payable by taxpayer, "400" = TCS on regular assessment).</summary>
    public string MinorHead { get; }

    public TcsChallan(
        Guid id, string challanNo, string bsrCode, DateOnly depositDate, Money amount, string collectionCode,
        string minorHead)
    {
        if (string.IsNullOrWhiteSpace(challanNo))
            throw new ArgumentException("Challan number is required.", nameof(challanNo));
        if (string.IsNullOrWhiteSpace(bsrCode))
            throw new ArgumentException("BSR code is required.", nameof(bsrCode));
        if (string.IsNullOrWhiteSpace(collectionCode))
            throw new ArgumentException("Challan collection code is required.", nameof(collectionCode));
        if (string.IsNullOrWhiteSpace(minorHead))
            throw new ArgumentException("Challan minor head is required.", nameof(minorHead));
        if (amount.Amount <= 0m)
            throw new ArgumentException("Challan amount must be > 0.", nameof(amount));
        if (!amount.IsPaisaExact)
            throw new InvalidOperationException($"Challan amount {amount} must be paisa-exact.");

        Id = id;
        ChallanNo = challanNo.Trim();
        BsrCode = bsrCode.Trim();
        DepositDate = depositDate;
        Amount = amount;
        CollectionCode = collectionCode.Trim();
        MinorHead = minorHead.Trim();
    }
}
