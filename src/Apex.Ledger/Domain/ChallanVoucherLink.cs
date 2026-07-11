namespace Apex.Ledger.Domain;

/// <summary>
/// Links a <see cref="TdsChallan"/> to the Stat-Payment Payment voucher that booked its deposit (Phase 7 slice 3;
/// the SQLite <c>challan_voucher_links</c> row). A many-to-many seam (a challan may cover more than one deposit
/// voucher, a voucher may span more than one challan), though the common case is one-to-one. Pure value with no
/// identity of its own — the pair (<see cref="ChallanId"/>, <see cref="VoucherId"/>) is the key.
/// </summary>
public readonly record struct ChallanVoucherLink(Guid ChallanId, Guid VoucherId);
