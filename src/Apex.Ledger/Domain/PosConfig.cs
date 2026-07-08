namespace Apex.Ledger.Domain;

/// <summary>
/// The POS configuration carried by a <b>POS-flagged Sales voucher type</b> (<see cref="VoucherType.UseForPos"/>;
/// catalog §11; Phase 6 slice 7 RQ-38; DP-4). Kept off the lean <see cref="VoucherType"/> row (persisted to the
/// <c>pos_voucher_type_config</c> side table), it records the retail-till defaults: the pre-selected godown, the
/// default party (a named customer OR a walk-in "(cash)" when <c>null</c> — the party is informational B2C, the
/// accounting debit is the tender ledger), whether to open the receipt preview after Accept, the receipt title,
/// two thank-you messages, and the declaration line. It also holds the optional <b>POS Voucher Class</b>
/// tender-ledger pre-map (DP-4): a default ledger per tender kind, pre-filled at entry and overridable, persisted
/// to <c>pos_tender_ledger_defaults</c>. Everything is optional so a bare POS type is valid. Present only when the
/// type is POS-flagged, so a non-POS type is byte-identical (ER-13).
/// </summary>
public sealed class PosConfig
{
    private readonly Dictionary<PosTenderType, Guid> _tenderLedgerDefaults = new();

    /// <summary>The godown pre-selected on a POS entry (Study Guide p.237); <c>null</c> = none.</summary>
    public Guid? DefaultGodownId { get; set; }

    /// <summary>The default party ledger; <c>null</c> = walk-in "(cash)" (B2C). Informational — never the debit.</summary>
    public Guid? DefaultPartyId { get; set; }

    /// <summary>Open the retail-receipt preview after Accept (Study Guide p.238).</summary>
    public bool PrintAfterSave { get; set; }

    /// <summary>The receipt title printed at the top; <c>null</c> = default.</summary>
    public string? DefaultTitle { get; set; }

    /// <summary>Thank-you message line 1 printed on the receipt; <c>null</c> = none.</summary>
    public string? Message1 { get; set; }

    /// <summary>Thank-you message line 2 printed on the receipt; <c>null</c> = none.</summary>
    public string? Message2 { get; set; }

    /// <summary>The declaration line printed on the receipt; <c>null</c> = none.</summary>
    public string? Declaration { get; set; }

    /// <summary>The POS Voucher Class tender-ledger defaults (DP-4): a default ledger per tender kind, overridable
    /// at entry. Up to four entries (Gift/Card/Cheque/Cash).</summary>
    public IReadOnlyDictionary<PosTenderType, Guid> TenderLedgerDefaults => _tenderLedgerDefaults;

    /// <summary>Sets (or replaces) the default ledger for a tender kind.</summary>
    public void SetTenderLedgerDefault(PosTenderType type, Guid ledgerId) => _tenderLedgerDefaults[type] = ledgerId;

    /// <summary>The default ledger for a tender kind, or <c>null</c> when none is mapped.</summary>
    public Guid? TenderLedgerDefault(PosTenderType type) =>
        _tenderLedgerDefaults.TryGetValue(type, out var id) ? id : null;
}
