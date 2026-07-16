namespace Apex.Ledger.Domain;

/// <summary>
/// One per-state / per-transaction-type <b>e-Way consignment-threshold override</b> (Phase 9 slice 5; §2.6; RQ-6). Rule
/// 138 sets a ₹50,000 default, but several states notified a different <b>intra-state</b> threshold (Delhi / Maharashtra /
/// Bihar ₹1 L, Rajasthan ₹1 L–₹2 L variants, WB ₹50,000). A company with <b>no</b> override rows keeps the flat ₹50,000
/// default and stays byte-identical (ER-13). The override resolves on the <b>place-of-supply</b> state and applies to
/// <b>intra-state</b> movements only — an inter-state consignment always uses the ₹50,000 default (risk #5).
/// </summary>
/// <remarks>Immutable master with a stable surrogate id; framework- and DB-agnostic. Mirrors the dated-master shape of
/// <see cref="GstCessRate"/> / <see cref="RcmCategory"/> (this one is not dated — a state threshold is a standing rule).</remarks>
public sealed record EWayStateThreshold
{
    /// <summary>Stable surrogate key.</summary>
    public Guid Id { get; }

    /// <summary>The 2-digit GST state code the override applies to (place of supply). Required.</summary>
    public string StateCode { get; }

    /// <summary>The transaction type the override applies to (defaults to <see cref="EWayTransactionType.Regular"/>).</summary>
    public EWayTransactionType TxnType { get; }

    /// <summary>The overriding consignment threshold (paisa-exact); ≥ 0.</summary>
    public Money Threshold { get; }

    public EWayStateThreshold(Guid id, string stateCode, EWayTransactionType txnType, Money threshold)
    {
        if (string.IsNullOrWhiteSpace(stateCode))
            throw new ArgumentException("An e-Way state-threshold override requires a 2-digit state code.", nameof(stateCode));
        if (threshold.Amount < 0)
            throw new ArgumentException("An e-Way state-threshold override must be ≥ 0.", nameof(threshold));

        Id = id;
        StateCode = stateCode;
        TxnType = txnType;
        Threshold = threshold;
    }
}
