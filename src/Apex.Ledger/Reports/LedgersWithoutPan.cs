using Apex.Ledger.Domain;
using Apex.Ledger.Services;

namespace Apex.Ledger.Reports;

/// <summary>One party in the <see cref="LedgersWithoutPanReport"/> (R9): a deductee/collectee that either had the
/// no-PAN higher rate applied this FY, or is a party ledger with a deductee/collectee status but no recorded
/// PAN.</summary>
/// <param name="LedgerId">The party ledger id.</param>
/// <param name="Party">The party ledger name.</param>
/// <param name="PartyType">"Deductee", "Collectee", or "Deductee &amp; Collectee".</param>
/// <param name="PanPresent">True iff the ledger currently carries a structurally valid PAN.</param>
/// <param name="Codes">The distinct sections / collection codes where the no-PAN rate was applied (or the ledger's
/// default nature codes when it is a supplement-only row with no posted no-PAN line).</param>
/// <param name="TaxAtNoPanRate">Σ TDS + TCS assessed at the no-PAN rate for this party in the FY of the as-of date.</param>
public sealed record LedgerWithoutPanRow(
    Guid LedgerId, string Party, string PartyType, bool PanPresent, string Codes, Money TaxAtNoPanRate);

/// <summary>
/// <b>R9 — Ledgers / Parties without PAN</b> (Phase 7 slice 8). Two signals, merged per party:
/// <list type="bullet">
///   <item><b>Primary</b> — every posted <see cref="TdsLineTax"/>/<see cref="TcsLineTax"/> with
///   <c>PanApplied == false</c> (the §206AA/§206CC no-PAN higher rate was applied), summed over the FY of
///   <see cref="AsOf"/>; the sections/codes and the no-PAN tax are surfaced.</item>
///   <item><b>Supplement</b> — every party ledger with a <see cref="Ledger.DeducteeType"/>/<see cref="Ledger.CollecteeType"/>
///   set but no structurally-valid <see cref="Ledger.PartyPan"/> (a party primed for TDS/TCS that is still missing
///   its PAN), even if it has no posted transaction yet.</item>
/// </list>
/// A party is listed only when it is <b>currently</b> without a valid PAN (F1/F4): a party charged the no-PAN rate
/// earlier in the FY that has since recorded a valid PAN is excluded (it is no longer "without PAN"; its historical
/// no-PAN transactions remain visible via <c>PanApplied</c> in the other reports). Consequently <c>PanPresent</c> is
/// always <c>false</c> for a listed row. Rows ordered by party name then ledger id. No UI, no DB.
/// </summary>
public sealed record LedgersWithoutPanReport(DateOnly AsOf, IReadOnlyList<LedgerWithoutPanRow> Rows)
{
    /// <summary>Σ TDS + TCS assessed at the no-PAN rate across all rows this FY.</summary>
    public Money TotalTaxAtNoPanRate => new(Rows.Sum(r => r.TaxAtNoPanRate.Amount));

    /// <summary>Builds R9 for <paramref name="company"/> as of <paramref name="asOf"/>.</summary>
    public static LedgersWithoutPanReport Build(Company company, DateOnly asOf)
    {
        ArgumentNullException.ThrowIfNull(company);
        var (fyStart, _) = TdsService.FinancialYearOf(asOf);

        var acc = new Dictionary<Guid, Builder>();

        Builder For(Guid ledgerId)
        {
            if (!acc.TryGetValue(ledgerId, out var b)) { b = new Builder(); acc[ledgerId] = b; }
            return b;
        }

        // Primary signal: posted no-PAN lines in the FY window.
        foreach (var v in company.Vouchers)
        {
            if (v.Cancelled || v.Date < fyStart || v.Date > asOf) continue;
            foreach (var line in v.Lines)
            {
                if (line.Tds is { PanApplied: false } t)
                {
                    var b = For(t.DeducteeLedgerId);
                    b.IsDeductee = true;
                    b.Tax += t.TdsAmount.Amount;
                    b.Codes.Add(t.SectionCode);
                }
                if (line.Tcs is { PanApplied: false } tc)
                {
                    var b = For(tc.CollecteeLedgerId);
                    b.IsCollectee = true;
                    b.Tax += tc.TcsAmount.Amount;
                    b.Codes.Add(tc.CollectionCode);
                }
            }
        }

        // Supplement: party ledgers with a deductee/collectee status but no valid PAN.
        foreach (var ledger in company.Ledgers)
        {
            var isDeductee = ledger.DeducteeType is not null;
            var isCollectee = ledger.CollecteeType is not null;
            if (!isDeductee && !isCollectee) continue;
            if (Pan.IsValid(ledger.PartyPan)) continue; // has a valid PAN — not a "without PAN" party

            var b = For(ledger.Id);
            b.IsDeductee |= isDeductee;
            b.IsCollectee |= isCollectee;
            if (isDeductee && ledger.TdsNatureOfPaymentId is { } nid && company.FindNatureOfPayment(nid) is { } nop)
                b.Codes.Add(nop.SectionCode);
            if (isCollectee && ledger.TcsNatureOfGoodsId is { } gid && company.FindNatureOfGoods(gid) is { } nog)
                b.Codes.Add(nog.CollectionCode);
        }

        var rows = new List<LedgerWithoutPanRow>();
        foreach (var (ledgerId, b) in acc)
        {
            var ledger = company.FindLedger(ledgerId);
            // F4: keep the report internally consistent with its "without PAN" title. A party surfaced only by the
            // historical no-PAN signal (a PanApplied==false line) that has SINCE recorded a valid PAN is no longer
            // "without PAN" and must not appear here — its past no-PAN transactions remain visible via PanApplied in
            // the other reports. (The supplement already excludes valid-PAN ledgers; this also filters the primary.)
            if (Pan.IsValid(ledger?.PartyPan)) continue;
            var partyType = (b.IsDeductee, b.IsCollectee) switch
            {
                (true, true) => "Deductee & Collectee",
                (true, false) => "Deductee",
                (false, true) => "Collectee",
                _ => "Party",
            };
            var codes = string.Join(", ", b.Codes.OrderBy(x => x, StringComparer.Ordinal));
            rows.Add(new LedgerWithoutPanRow(
                ledgerId, ledger?.Name ?? "(unknown)", partyType, Pan.IsValid(ledger?.PartyPan),
                codes, new Money(b.Tax)));
        }

        rows.Sort((a, z) =>
        {
            var byName = string.Compare(a.Party, z.Party, StringComparison.OrdinalIgnoreCase);
            return byName != 0 ? byName : a.LedgerId.CompareTo(z.LedgerId);
        });
        return new LedgersWithoutPanReport(asOf, rows);
    }

    private sealed class Builder
    {
        public bool IsDeductee;
        public bool IsCollectee;
        public decimal Tax;
        public readonly SortedSet<string> Codes = new(StringComparer.Ordinal);
    }
}
