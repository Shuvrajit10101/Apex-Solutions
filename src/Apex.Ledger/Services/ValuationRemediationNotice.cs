using System.Text;
using Apex.Ledger.Domain;

namespace Apex.Ledger.Services;

/// <summary>
/// 🔴 <b>THE ON-OPEN WARNING REQUIRED BY USER RULING 26 — the half of the ruling that is not a schema change.</b>
///
/// <para>Schema v65 moves every stock item costed at the retired <see cref="StockValuationMethod.LastSaleCost"/>
/// ordinal onto <see cref="StockValuationMethod.LastPurchaseCost"/>, because the old method valued closing stock
/// at our own <i>selling</i> rate and put the whole unrealised margin on the Balance Sheet. Ruling 26 accepted
/// that such a book's closing stock value <b>changes on the day it upgrades</b> — and required, in the same
/// breath, that the operator be <b>told</b>. A book whose Balance Sheet moves underneath it without a word is
/// the same class of defect as the one being fixed, just quieter.</para>
///
/// <para>🔴 <b>THE WARNING IS TARGETED, AND THAT IS THE DESIGN, NOT AN OPTIMISATION.</b> It is built from the
/// <see cref="StockItem.ValuationRemediatedFrom"/> marker the migration stamps, so it fires <b>only</b> for books
/// that actually chose the retired method. A book that never did gets <see cref="string.Empty"/> and sees
/// nothing. A warning shown to everyone is a warning nobody reads, which would leave the affected operator no
/// better off than silence.</para>
///
/// <para><b>Pure and deterministic</b> — no UI, no database, no clock — so the "affected book warns / unaffected
/// book stays silent" contract is asserted directly in the Ledger test project rather than through a view model.</para>
/// </summary>
public static class ValuationRemediationNotice
{
    /// <summary>At most this many item names are listed before the notice switches to a count.</summary>
    private const int MaxNamedItems = 5;

    /// <summary>
    /// The operator-facing warning for a freshly-opened book, or <see cref="string.Empty"/> when the book was
    /// never affected.
    ///
    /// <para>The text states <b>what changed, why, and what it means for the figures</b> — an operator who is
    /// told only "the valuation method changed" cannot tell whether to trust last month's Balance Sheet.</para>
    /// </summary>
    public static string For(Company company)
    {
        ArgumentNullException.ThrowIfNull(company);

        var affected = company.StockItems
            .Where(i => i.ValuationRemediatedFrom == StockValuationMethod.LastSaleCost)
            .OrderBy(i => i.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (affected.Count == 0) return string.Empty;

        var names = new StringBuilder();
        foreach (var item in affected.Take(MaxNamedItems))
        {
            if (names.Length > 0) names.Append(", ");
            names.Append(item.Name);
        }
        if (affected.Count > MaxNamedItems)
            names.Append($" and {affected.Count - MaxNamedItems} more");

        var plural = affected.Count == 1 ? "item" : "items";
        var verb = affected.Count == 1 ? "was" : "were";

        return
            $"Closing stock valuation changed when this company was upgraded. {affected.Count} stock {plural} " +
            $"({names}) {verb} valued at the last SALE price — your own selling rate — which overstated " +
            "Stock-in-Hand on the Balance Sheet, and profit, by the unrealised margin. They now use Last " +
            "Purchase Cost, an actual cost you paid. Closing stock, Profit & Loss and the Balance Sheet may " +
            "therefore differ from what earlier versions of Apex Solutions reported for the same period. " +
            "Review the affected items under Masters, and if you want a different costing basis, change it there. " +
            "A selling-price basis is still available, separately, as the Market Valuation field.";
    }
}
