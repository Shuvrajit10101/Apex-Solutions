namespace Apex.Ledger.Domain;

/// <summary>
/// A <b>TDS deposit challan</b> (ITNS-281) — the government receipt for a TDS payment made into the bank
/// (Phase 7 slice 3; catalog §13). It captures the ITNS-281 fields the Challan Reconciliation (Alt+R) and Form
/// 26Q challan block need: the challan serial <see cref="ChallanNo"/>, the collecting-branch <see cref="BsrCode"/>
/// (7-digit BSR), the <see cref="DepositDate"/>, the <see cref="Amount"/> deposited, the income-tax
/// <see cref="Section"/> (major head / section code the deposit is for, e.g. "194J(b)") and the
/// <see cref="MinorHead"/> (ITNS-281 minor head — "200" TDS payable by the taxpayer / "400" TDS on regular
/// assessment).
/// <para>
/// A challan is <b>linked</b> to the Stat-Payment Payment voucher that booked the deposit (Dr "TDS Payable" / Cr
/// Bank) through the company's <c>ChallanVoucherLink</c> set, mirroring how a deposit voucher pairs to a challan
/// in Tally. The challan itself is framework-, DB- and clock-free; it is a pure value object with identity.
/// </para>
/// </summary>
public sealed class TdsChallan
{
    /// <summary>Stable surrogate key.</summary>
    public Guid Id { get; }

    /// <summary>The challan serial / CIN tender number (required).</summary>
    public string ChallanNo { get; }

    /// <summary>The 7-digit BSR code of the collecting bank branch (required).</summary>
    public string BsrCode { get; }

    /// <summary>The date the tax was deposited into the bank.</summary>
    public DateOnly DepositDate { get; }

    /// <summary>The amount deposited (paisa-exact; a whole-rupee TDS deposit in practice).</summary>
    public Money Amount { get; }

    /// <summary>The income-tax section / major head the deposit is for (e.g. "194J(b)"); required.</summary>
    public string Section { get; }

    /// <summary>The ITNS-281 minor head ("200" = TDS payable by taxpayer, "400" = TDS on regular assessment).</summary>
    public string MinorHead { get; }

    public TdsChallan(
        Guid id, string challanNo, string bsrCode, DateOnly depositDate, Money amount, string section,
        string minorHead)
    {
        if (string.IsNullOrWhiteSpace(challanNo))
            throw new ArgumentException("Challan number is required.", nameof(challanNo));
        if (string.IsNullOrWhiteSpace(bsrCode))
            throw new ArgumentException("BSR code is required.", nameof(bsrCode));
        if (string.IsNullOrWhiteSpace(section))
            throw new ArgumentException("Challan section is required.", nameof(section));
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
        Section = section.Trim();
        MinorHead = minorHead.Trim();
    }
}
