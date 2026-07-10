using Apex.Ledger.Domain;
using Apex.Ledger.Seed;

namespace Apex.Ledger.Services;

/// <summary>
/// The TDS/TCS <b>enable + master</b> engine (Phase 7 slice 1; mirrors <see cref="GstService.EnableGst"/> /
/// <c>EnsureTaxLedger</c>). Framework-, DB-, clock- and RNG-free: pure, deterministic mutation over the
/// <see cref="Company"/> aggregate. Responsibilities in this slice:
/// <list type="bullet">
///   <item><see cref="EnableTds"/> — idempotently enable TDS, seed the config-driven Nature-of-Payment masters,
///     and auto-create the "TDS Payable" liability ledger under Duties &amp; Taxes tagged
///     <see cref="TdsTcsLedgerKind.Tds"/>.</item>
///   <item><see cref="EnableTcs"/> — the TCS mirror: seed Nature-of-Goods (§206C) masters + auto-create
///     "TCS Payable" tagged <see cref="TdsTcsLedgerKind.Tcs"/>.</item>
/// </list>
/// <b>No tax computation ships in this slice</b> — the withholding/collection engine is Phase 7 slice 2/5. The
/// auto-created payable ledgers live under Duties &amp; Taxes, so <c>ClassificationRules.IsDutiesAndTaxesLedger</c>
/// already excludes them from the item-invoice pairing sum, exactly like the GST tax ledgers.
/// </summary>
public sealed class TdsTcsService
{
    private readonly Company _company;

    public TdsTcsService(Company company)
        => _company = company ?? throw new ArgumentNullException(nameof(company));

    /// <summary>The auto-created TDS liability ledger name.</summary>
    public const string TdsPayableLedgerName = "TDS Payable";

    /// <summary>The auto-created TCS liability ledger name.</summary>
    public const string TcsPayableLedgerName = "TCS Payable";

    // ---- Enable TDS (idempotent) ----

    /// <summary>
    /// Enables TDS on the company with the given config (F11; Phase 7 slice 1), <b>idempotently</b>. Validates the
    /// config (fail-fast: TAN required + valid, responsible-person PAN valid when set), stores it, seeds the
    /// predefined Nature-of-Payment masters (FY 2025-26) if the config has none, and auto-creates the "TDS Payable"
    /// ledger under Duties &amp; Taxes tagged <see cref="TdsTcsLedgerKind.Tds"/> — skipping it if already present, so
    /// re-enabling never duplicates. Returns the enabled config.
    /// </summary>
    public TdsConfig EnableTds(TdsConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        config.Enabled = true;
        config.EnsureValid();

        // Preserve any natures already seeded on a prior enable (or supplied by import); else seed the FY defaults.
        if (config.NaturesOfPayment.Count == 0)
            foreach (var nature in SeedTdsTcsRates.BuildTdsDefaults())
                config.AddNatureOfPayment(nature);

        _company.Tds = config;

        EnsurePayableLedger(TdsPayableLedgerName, TdsTcsLedgerKind.Tds);
        return config;
    }

    // ---- Enable TCS (idempotent) ----

    /// <summary>
    /// Enables TCS on the company with the given config (F11; Phase 7 slice 1), <b>idempotently</b>. Mirrors
    /// <see cref="EnableTds"/>: validates, seeds the predefined Nature-of-Goods (§206C) masters if none, and
    /// auto-creates "TCS Payable" under Duties &amp; Taxes tagged <see cref="TdsTcsLedgerKind.Tcs"/>.
    /// </summary>
    public TcsConfig EnableTcs(TcsConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        config.Enabled = true;
        config.EnsureValid();

        if (config.NaturesOfGoods.Count == 0)
            foreach (var nature in SeedTdsTcsRates.BuildTcsDefaults())
                config.AddNatureOfGoods(nature);

        _company.Tcs = config;

        EnsurePayableLedger(TcsPayableLedgerName, TdsTcsLedgerKind.Tcs);
        return config;
    }

    /// <summary>The TDS/TCS payable ledger for a kind, or <c>null</c> if not enabled / not created.</summary>
    public Domain.Ledger? FindPayableLedger(TdsTcsLedgerKind kind) =>
        _company.Ledgers.FirstOrDefault(l => l.TdsTcsClassification == kind);

    private void EnsurePayableLedger(string name, TdsTcsLedgerKind kind)
    {
        if (FindPayableLedger(kind) is not null) return; // idempotent by classification

        var dutiesAndTaxes = _company.FindGroupByName("Duties & Taxes")
            ?? throw new InvalidOperationException(
                "Seed missing 'Duties & Taxes' group; cannot auto-create the TDS/TCS payable ledger.");

        // If a ledger by that name exists (e.g. user pre-created), tag it; else create a fresh liability ledger.
        var existing = _company.FindLedgerByName(name);
        if (existing is not null)
        {
            existing.TdsTcsClassification ??= kind;
            if (existing.GroupId == Guid.Empty) existing.GroupId = dutiesAndTaxes.Id;
            return;
        }

        // A payable is a liability → opens on the credit side (openingIsDebit false), like the Output GST ledgers.
        _company.AddLedger(new Domain.Ledger(
            Guid.NewGuid(), name, dutiesAndTaxes.Id, Money.Zero, openingIsDebit: false)
        {
            TdsTcsClassification = kind,
        });
    }
}
